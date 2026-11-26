namespace Arcitisa.Components.Page.ProvenanceGrouping

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Browser.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Primitive.ContextMenu
open Arcitisa.Components.Primitive.ContextMenu.Types
open Arcitisa.Components.Page.ProvenanceGrouping.Types

[<Erase; Mangle(false)>]
type ConnectorOverlay =

    [<ReactComponent>]
    static member private LiveConnectorLayer(store: LiveDrag.Store, ?debug: bool) =
        let _, bump = React.useStateWithUpdater 0

        React.useEffect (
            (fun () ->
                let unsubscribe =
                    store |> LiveDrag.subscribe (fun () -> bump (fun version -> version + 1))

                FsReact.createDisposable unsubscribe
            ),
            [| box store |]
        )

        match ConnectorPaths.liveConnection store.Current with
        | Some measured ->
            Svg.svg [
                svg.className "swt:absolute swt:inset-0 swt:pointer-events-none swt:size-full"
                svg.children [
                    yield!
                        ConnectorSvg.strokeElements
                            measured
                            measured.StrokeWidth
                            1.0
                            false
                            false
                            (defaultArg debug false)
                ]
            ]
        | None -> Html.none

    [<ReactComponent>]
    static member Main
        (
            containerRef: IRefValue<HTMLElement option>,
            layerId: ProvenanceLayerId,
            model: ProvenanceModel,
            inputGroups: DisplayGroup list,
            outputGroups: DisplayGroup list,
            connections: DisplayConnection list,
            inputRailProjection: PropertyRails.RailProjection,
            outputRailProjection: PropertyRails.RailProjection,
            overlayState: ConnectorOverlayState,
            layoutSignature: string,
            showPropertyHeaderConnectors: bool,
            liveDragStore: LiveDrag.Store,
            onSelect: DisplayConnection -> unit,
            ?onRemove: DisplayConnection -> unit,
            ?debug: bool,
            ?railColorByHeader: Map<ProvenancePropertyHeader, string option>
        ) =
        let measuredState, setMeasuredState =
            React.useStateWithUpdater ((([]: MeasuredConnector list), false))

        let hoveredKey, setHoveredKey = React.useState<string option> None
        let pendingFrame = React.useRef (None: float option)

        // Hovering a group card (published through the store) emphasizes the
        // connectors attached to it; only this overlay re-renders on hover changes.
        let hoverStore = React.useContext HoverHighlight.context
        let _, bumpHover = React.useStateWithUpdater 0

        React.useEffect (
            (fun () ->
                let unsubscribe =
                    hoverStore
                    |> HoverHighlight.subscribe (fun () -> bumpHover (fun version -> version + 1))

                FsReact.createDisposable unsubscribe
            ),
            [| box hoverStore |]
        )

        let hoveredGroup = hoverStore.Current

        // Discrete data/layout changes morph paths to their new shape; continuous
        // sources (scroll, resize, drag-driven mutations) snap per frame instead.
        let animateNextMeasure = React.useRef false
        let debugEnabled = defaultArg debug false

        let colorByHeader =
            React.useMemo ((fun () -> defaultArg railColorByHeader Map.empty), [| box railColorByHeader |])

        // The logical connector list is pure model/UI derivation; recomputing it on
        // every observer-driven remeasure made scroll and resize expensive on large
        // models, so it is memoized here and only the DOM rect reads happen per frame.
        let specs =
            React.useMemo (
                (fun () ->
                    ConnectorPaths.specs
                        layerId
                        model
                        inputGroups
                        outputGroups
                        connections
                        inputRailProjection
                        outputRailProjection
                        colorByHeader
                        overlayState
                        showPropertyHeaderConnectors
                ),
                [|
                    box layerId
                    box model
                    box inputGroups
                    box outputGroups
                    box connections
                    box inputRailProjection
                    box outputRailProjection
                    box colorByHeader
                    box overlayState.ExpandedGroups
                    box overlayState.ExpandedProperties
                    box showPropertyHeaderConnectors
                |]
            )

        let latestSpecs = React.useRef specs

        // useLayoutEffect (not a render-phase write) so a render that gets
        // discarded under concurrent rendering / StrictMode never leaves this
        // pointing at specs that were never actually committed. The sibling
        // `React.useEffect(..., [| box specs |])` below runs after this, so
        // ordering is unaffected.
        React.useLayoutEffect ((fun () -> latestSpecs.current <- specs), [| box specs |])

        let setMeasuredPaths animate next =
            setMeasuredState (fun (current, currentAnimate) ->
                if current = next then
                    (current, currentAnimate)
                else
                    (next, animate)
            )

        let measureNow () =
            pendingFrame.current <- None
            let animate = animateNextMeasure.current
            animateNextMeasure.current <- false

            match containerRef.current with
            | None -> setMeasuredPaths false []
            | Some container ->
                let context = ConnectorMeasure.createContext container

                ConnectorPaths.measure context latestSpecs.current |> setMeasuredPaths animate

        let scheduleMeasure () =
            match pendingFrame.current with
            | Some _ -> ()
            | None -> pendingFrame.current <- Some(AnimationFrame.request measureNow)

        let cancelPendingFrame () =
            match pendingFrame.current with
            | Some handle ->
                AnimationFrame.cancel handle
                pendingFrame.current <- None
            | None -> ()

        // Layout plumbing is bound once per mount: the surface element identity is
        // stable across renders, and node churn inside it is picked up by the
        // mutation observer rather than by re-binding the observers.
        React.useEffectOnce (fun () ->
            match containerRef.current with
            | None -> FsReact.createDisposable cancelPendingFrame
            | Some container ->
                let onLayout = fun (_: Event) -> scheduleMeasure ()
                container.addEventListener ("scroll", onLayout)
                Browser.Dom.window.addEventListener ("resize", onLayout)

                let observer = ConnectorObserver.create scheduleMeasure

                let observeCurrentNodes () =
                    ConnectorObserver.observeNode observer container

                    ConnectorObserver.observeMatching
                        container
                        "[data-provenance-group-node],[data-provenance-member-node],[data-provenance-connection-node],[data-provenance-resize-node]"
                        observer

                let mutationFrame = ref (None: float option)

                let cancelMutationFrame () =
                    match mutationFrame.Value with
                    | Some handle ->
                        AnimationFrame.cancel handle
                        mutationFrame.Value <- None
                    | None -> ()

                let scheduleMutationMeasure () =
                    match mutationFrame.Value with
                    | Some _ -> ()
                    | None ->
                        mutationFrame.Value <-
                            Some(
                                AnimationFrame.request (fun () ->
                                    mutationFrame.Value <- None
                                    observeCurrentNodes ()
                                    // Measure in the same frame instead of scheduling
                                    // another one; live panel resizes flow through this
                                    // path and should trail the layout by one frame at
                                    // most.
                                    measureNow ()
                                )
                            )

                let mutationObserver =
                    ConnectorMutationObserver.create (fun () -> scheduleMutationMeasure ())

                observeCurrentNodes ()
                ConnectorMutationObserver.observe mutationObserver container

                FsReact.createDisposable (fun () ->
                    container.removeEventListener ("scroll", onLayout)
                    Browser.Dom.window.removeEventListener ("resize", onLayout)
                    ConnectorObserver.disconnect observer
                    ConnectorMutationObserver.disconnect mutationObserver
                    cancelMutationFrame ()
                    cancelPendingFrame ()
                )
        )

        // Spec changes measure synchronously so connectors track data edits within
        // the same committed frame, exactly like the previous effect did.
        React.useEffect (
            (fun () ->
                animateNextMeasure.current <- true
                measureNow ()
            ),
            [| box specs |]
        )

        React.useEffect (
            (fun () ->
                animateNextMeasure.current <- true
                scheduleMeasure ()
            ),
            [| box layoutSignature |]
        )

        let selectedConnectionId = overlayState.SelectedConnectionId
        let paths, measuredWithAnimation = measuredState
        let animatePaths = measuredWithAnimation && not (Motion.prefersReduced ())

        // Keys seen in the previous committed render; connectors not in this set are
        // freshly created and get their entrance animation.
        let renderedKeys = React.useRef (Set.empty: Set<string>)
        let previousKeys = renderedKeys.current

        React.useEffect (fun () -> renderedKeys.current <- paths |> List.map (fun m -> m.Key) |> Set.ofList)

        React.Fragment [
            Svg.svg [
                svg.className "swt:absolute swt:inset-0 swt:pointer-events-none swt:size-full"
                svg.children [
                    for measured in paths do
                        let activateFromKeyboard (event: KeyboardEvent) =
                            match measured.InteractiveConnection, event.key with
                            | Some connection, "Enter"
                            | Some connection, " "
                            | Some connection, "Spacebar" ->
                                event.preventDefault ()
                                onSelect connection
                            | Some connection, "Delete"
                            | Some connection, "Backspace" ->
                                match onRemove with
                                | Some remove ->
                                    event.preventDefault ()
                                    remove connection
                                | None -> ()
                            | _ -> ()

                        let isSelected =
                            match measured.InteractiveConnection, selectedConnectionId with
                            | Some connection, Some selectedId -> connection.Id = selectedId
                            | _ -> false

                        let isHoverRelated =
                            match measured.InteractiveConnection, hoveredGroup with
                            | Some connection, Some target ->
                                (target.Side = ProvenanceSide.Input && connection.SourceGroupId = target.GroupId)
                                || (target.Side = ProvenanceSide.Output && connection.TargetGroupId = target.GroupId)
                            | _ -> false

                        let isEmphasized = isSelected || hoveredKey = Some measured.Key || isHoverRelated

                        let strokeWidth =
                            if isEmphasized then
                                measured.StrokeWidth + 1.25
                            else
                                measured.StrokeWidth

                        // Selecting a connection keeps it bright and recedes its siblings.
                        let strokeOpacity =
                            match measured.InteractiveConnection with
                            | Some _ when isEmphasized -> 1.0
                            | Some _ when selectedConnectionId.IsSome -> 0.3
                            | Some _ -> 0.85
                            | None -> 1.0

                        let debugAttributes = ConnectorSvg.debugAttributes debugEnabled measured

                        Svg.g [
                            svg.key measured.Key
                            svg.children [
                                yield!
                                    ConnectorSvg.strokeElements
                                        measured
                                        strokeWidth
                                        strokeOpacity
                                        animatePaths
                                        (not (previousKeys.Contains measured.Key))
                                        debugEnabled
                                // A collapsed group connector summarizes several underlying
                                // connections; the midpoint badge makes that multiplicity
                                // visible without expanding the groups.
                                match measured.InteractiveConnection, measured.Midpoint with
                                | Some connection, Some midpoint when connection.ConnectionIds.Length > 1 ->
                                    let countText = string connection.ConnectionIds.Length
                                    let radius = if countText.Length > 2 then 12. else 9.

                                    Svg.g [
                                        svg.className "swt:pointer-events-none"
                                        svg.custom ("opacity", strokeOpacity)
                                        if debugEnabled then
                                            svg.custom ("data-testid", "provenance-connection-count")
                                            svg.custom ("data-provenance-connection-key", measured.Key)
                                        svg.children [
                                            Svg.circle [
                                                svg.cx midpoint.X
                                                svg.cy midpoint.Y
                                                svg.r radius
                                                svg.custom ("fill", "var(--color-base-100)")
                                                svg.custom ("stroke", "var(--color-primary)")
                                                svg.custom ("strokeWidth", 1.5)
                                            ]
                                            Svg.text [
                                                svg.x midpoint.X
                                                svg.y midpoint.Y
                                                svg.custom ("textAnchor", "middle")
                                                svg.custom ("dominantBaseline", "central")
                                                svg.custom ("fill", "var(--color-primary)")
                                                svg.custom ("fontSize", 10)
                                                svg.custom ("fontWeight", 600)
                                                svg.text countText
                                            ]
                                        ]
                                    ]
                                | _ -> ()
                                // The pointer/keyboard target: for ribbons the filled band
                                // itself (transparent fill still hit-tests), for lines a wide
                                // transparent stroke so selecting a thin curve no longer
                                // needs pixel accuracy.
                                match measured.InteractiveConnection with
                                | Some connection ->
                                    let hitPath = measured.RibbonPath |> Option.defaultValue measured.Path

                                    Svg.path [
                                        svg.d hitPath
                                        svg.custom ("style", ConnectorSvg.pathStyle hitPath animatePaths)

                                        match measured.RibbonPath with
                                        | Some _ ->
                                            svg.fill "transparent"
                                            svg.stroke "none"
                                        | None ->
                                            svg.fill "none"
                                            svg.stroke "transparent"
                                            svg.strokeWidth 14
                                        svg.className
                                            "swt:pointer-events-auto swt:cursor-pointer swt:outline-none swt:shadow-none"
                                        svg.custom ("tabIndex", "0")
                                        svg.custom ("role", "button")
                                        svg.custom (
                                            "aria-label",
                                            measured.AriaLabel
                                            |> Option.defaultValue $"Select connection {connection.Id}"
                                        )
                                        svg.custom (ConnectorContextMenu.connectionKeyAttribute, measured.Key)
                                        yield! debugAttributes
                                        svg.onClick (fun _ -> onSelect connection)
                                        svg.onKeyDown activateFromKeyboard
                                        svg.onMouseEnter (fun _ -> setHoveredKey (Some measured.Key))
                                        svg.onMouseLeave (fun _ -> setHoveredKey None)
                                        svg.onFocus (fun _ -> setHoveredKey (Some measured.Key))
                                        svg.onBlur (fun _ -> setHoveredKey None)
                                    ]
                                | None -> ()
                            ]
                        ]
                ]
            ]
            ConnectorOverlay.LiveConnectorLayer(liveDragStore, ?debug = debug)
            match onRemove with
            | Some remove ->
                ContextMenu.ContextMenu(
                    ConnectorContextMenu.items remove,
                    ref = containerRef,
                    onSpawn = ConnectorContextMenu.spawnData paths,
                    debug = debugEnabled
                )
            | None -> Html.none
        ]
