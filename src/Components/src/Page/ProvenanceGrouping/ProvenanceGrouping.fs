namespace Arcitisa.Components.Page.ProvenanceGrouping

open System
open System.Globalization
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Composite.FolderedDraggableList
open Arcitisa.Components.Composite.FolderedDraggableList.Types
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

[<Erase; Mangle(false)>]
type ProvenanceGrouping =

    [<ReactComponent(true)>]
    static member Main
        (
            session: ProvenanceSession,
            onChange: ProvenanceEditorChange -> unit,
            ?height: int,
            ?debug: bool,
            ?initUiState: ProvenanceSession -> UiState,
            ?initialOpenRail: ProvenanceSide
        ) =
        let debug = defaultArg debug false

        // The tutorial sandbox seeds checkpoints through initUiState (e.g.
        // "Species already sits on the input rail"); regular hosts start fresh.
        let rawUiState, setUiState =
            React.useState (fun () ->
                match initUiState with
                | Some init -> init session
                | None -> State.init session
            )

        let activeDrag, setActiveDrag = React.useState<ActiveDrag option> None
        let surfaceRef = React.useElementRef ()
        let splitDrag = React.useRef (None: Splitter.SplitterSide option)
        let rootRef = React.useElementRef ()
        let tier, setTier = React.useState LayoutTier.Wide
        let openRail, setOpenRail = React.useState<ProvenanceSide option> initialOpenRail

        // Optical side hiding: the hidden side's rail and cards leave the layout,
        // but every state rule (placements, grouping, connections) keeps running
        // on both sides, so revealing the side restores it unchanged.
        let hiddenSide, setHiddenSide = React.useState<ProvenanceSide option> None
        let density, setDensity = React.useState Density.EditorDensity.Comfortable
        let isPropertyShelfExpanded, setIsPropertyShelfExpanded = React.useState true
        let isTutorialOpen, setIsTutorialOpen = React.useState false

        let propertyShelfActiveFolder, setPropertyShelfActiveFolder =
            React.useState<(ProvenanceLayerId * string) option> None

        let showPropertyHeaderConnectors, setShowPropertyHeaderConnectors =
            React.useState true

        let liveDragStore = React.useRef (LiveDrag.create ())
        let hoverStore = React.useRef (HoverHighlight.create ())
        let isValueChipDragging, setIsValueChipDragging = React.useState false

        // Click-to-connect: a tapped handle stays armed until a target handle is
        // tapped, Escape is pressed, or the pointer goes down elsewhere.
        let armedHandle, setArmedHandle = React.useState<ConnectionHandleRef option> None
        let latestArmedHandle = React.useRef armedHandle
        let latestDragContext = React.useRef (None: DragContext option)

        React.useEffectOnce (fun () ->
            let onPointerDown =
                fun (event: Browser.Types.Event) ->
                    if latestArmedHandle.current.IsSome then
                        let onHandle =
                            not (isNull event.target)
                            && not (isNull (Motion.closest event.target "[data-provenance-connection-drop-id]"))

                        if not onHandle then
                            setArmedHandle None

            let onKeyDown =
                fun (event: Browser.Types.KeyboardEvent) ->
                    if event.key = "Escape" && latestArmedHandle.current.IsSome then
                        setArmedHandle None

            Browser.Dom.document.addEventListener ("pointerdown", unbox onPointerDown)
            Browser.Dom.document.addEventListener ("keydown", unbox onKeyDown)

            FsReact.createDisposable (fun () ->
                Browser.Dom.document.removeEventListener ("pointerdown", unbox onPointerDown)
                Browser.Dom.document.removeEventListener ("keydown", unbox onKeyDown)
            )
        )

        let handleTap (handle: ConnectionHandleRef) =
            match latestArmedHandle.current with
            | Some armed when armed = handle -> setArmedHandle None
            | Some armed ->
                match ConnectionRouting.action armed handle with
                | Some _ ->
                    latestDragContext.current
                    |> Option.iter (fun context -> DragHandlers.routeConnectionHandle context armed handle)

                    setArmedHandle None
                // A tap on an incompatible handle re-arms from there instead.
                | None -> setArmedHandle (Some handle)
            | None -> setArmedHandle (Some handle)

        React.useEffectOnce (fun () ->
            let applyTier () =
                match rootRef.current with
                | Some root -> setTier (LayoutTier.forWidth root.clientWidth)
                | None -> ()

            applyTier ()
            let observer = TierObserver.create applyTier

            match rootRef.current with
            | Some root -> TierObserver.observe observer root
            | None -> ()

            FsReact.createDisposable (fun () -> TierObserver.disconnect observer)
        )

        let uiState =
            React.useMemo ((fun () -> State.Sides.ensure session rawUiState), [| box session; box rawUiState |])

        let layer, inputGroups, outputGroups, connections =
            React.useMemo ((fun () -> Display.displayLayer session uiState), [| box session; box uiState.SideStates |])

        // Event handlers below read these refs instead of closing over render values,
        // so their identities stay stable and memoized subtrees never act on stale
        // sessions or UI state. Written in a useLayoutEffect further down (once
        // dragContext exists) rather than here in the render body: a render can
        // be discarded under concurrent rendering / StrictMode double-invoke, and
        // a discarded render must not leave these refs pointing at values the
        // user never actually saw committed.
        let latestUiState = React.useRef uiState
        let latestSession = React.useRef session
        let latestLayer = React.useRef layer
        let latestOnChange = React.useRef onChange
        let latestConnections = React.useRef connections
        let latestGroups = React.useRef (inputGroups, outputGroups)

        // Marks the cards connected to the hovered card with a data attribute, styled
        // by the cards' CSS; imperative on purpose so hovering never re-renders the
        // editor tree.
        React.useEffectOnce (fun () ->
            let markRelated () =
                match surfaceRef.current with
                | None -> ()
                | Some surface ->
                    Motion.queryAll surface "[data-provenance-related]"
                    |> Array.iter (fun node -> node.removeAttribute "data-provenance-related")

                    match hoverStore.current.Current with
                    | None -> ()
                    | Some target ->
                        let related =
                            latestConnections.current
                            |> List.choose (fun connection ->
                                match target.Side with
                                | ProvenanceSide.Input when connection.SourceGroupId = target.GroupId ->
                                    Some(ProvenanceSide.Output, connection.TargetGroupId)
                                | ProvenanceSide.Output when connection.TargetGroupId = target.GroupId ->
                                    Some(ProvenanceSide.Input, connection.SourceGroupId)
                                | _ -> None
                            )
                            |> List.distinct

                        for relatedSide, groupId in related do
                            let node: Browser.Types.HTMLElement =
                                !!
                                    surface.querySelector
                                    ($"[data-provenance-group-node='{DragDrop.groupNodeId relatedSide groupId}']")

                            if not (isNull node) then
                                node.setAttribute ("data-provenance-related", "true")

            let unsubscribe = hoverStore.current |> HoverHighlight.subscribe markRelated
            FsReact.createDisposable unsubscribe
        )

        let commitUiState =
            React.useCallback (
                (fun next ->
                    latestUiState.current <- next
                    setUiState next
                ),
                [||]
            )

        let applyUiState: (UiState -> UiState) -> unit =
            React.useCallback ((fun update -> commitUiState (update latestUiState.current)), [||])

        let panelRatios = State.PanelLayout.get layer.Id uiState

        let endpointKinds =
            React.useMemo ((fun () -> Endpoints.endpointKindOptions ()), [||])

        let existingEndpointNames =
            React.useMemo (
                (fun () -> [
                    yield!
                        inputGroups
                        |> List.collect (fun group -> group.Members |> List.map (fun member' -> member'.Name))
                    yield!
                        outputGroups
                        |> List.collect (fun group -> group.Members |> List.map (fun member' -> member'.Name))
                ]),
                [| box inputGroups; box outputGroups |]
            )

        let lookups =
            React.useMemo (
                (fun () -> EditorLookups.create layer uiState inputGroups outputGroups),
                [|
                    box layer
                    box uiState.PaletteValues
                    box inputGroups
                    box outputGroups
                |]
            )

        let latestLookups = React.useRef lookups

        let inputRailProjection =
            React.useMemo (
                (fun () ->
                    PropertyProjection.railProjectionWithFilters
                        session
                        layer.Id
                        ProvenanceSide.Input
                        layer.Model
                        uiState
                ),
                [|
                    box session
                    box layer.Id
                    box layer.Model
                    box uiState.PropertyRailPlacements
                    box uiState.PropertyRailOrders
                    box uiState.ExpandedProperties
                    box uiState.PaletteValues
                    box uiState.PropertyColors
                    box uiState.Filters
                |]
            )

        let outputRailProjection =
            React.useMemo (
                (fun () ->
                    PropertyProjection.railProjectionWithFilters
                        session
                        layer.Id
                        ProvenanceSide.Output
                        layer.Model
                        uiState
                ),
                [|
                    box session
                    box layer.Id
                    box layer.Model
                    box uiState.PropertyRailPlacements
                    box uiState.PropertyRailOrders
                    box uiState.ExpandedProperties
                    box uiState.PaletteValues
                    box uiState.PropertyColors
                    box uiState.Filters
                |]
            )

        let inputSideState = State.Sides.get layer.InputSideId uiState
        let outputSideState = State.Sides.get layer.OutputSideId uiState

        let activeInputRailProjection =
            React.useMemo (
                (fun () ->
                    EditorSurface.dropZoneProjection
                        layer.Id
                        ProvenanceSide.Input
                        uiState
                        inputSideState.GroupingAssignments
                        inputRailProjection
                ),
                [|
                    box layer.Id
                    box uiState.PropertyRailPlacements
                    box inputSideState
                    box inputRailProjection
                |]
            )

        let activeOutputRailProjection =
            React.useMemo (
                (fun () ->
                    EditorSurface.dropZoneProjection
                        layer.Id
                        ProvenanceSide.Output
                        uiState
                        outputSideState.GroupingAssignments
                        outputRailProjection
                ),
                [|
                    box layer.Id
                    box uiState.PropertyRailPlacements
                    box outputSideState
                    box outputRailProjection
                |]
            )

        let propertyShelfFolders =
            React.useMemo (
                (fun () -> PropertyShelf.folders session layer uiState inputRailProjection outputRailProjection),
                [|
                    box session
                    box layer
                    box uiState.PropertyRailPlacements
                    box uiState.PropertyColors
                    box uiState.SideStates
                    box inputRailProjection
                    box outputRailProjection
                |]
            )

        // The shelf falls back to its first tab internally, so only a
        // selection made on the current layer is forwarded.
        let propertyShelfActiveFolderId =
            match propertyShelfActiveFolder with
            | Some(selectedLayerId, folderId) when selectedLayerId = layer.Id -> Some folderId
            | _ -> None

        let setPropertyShelfActiveFolderId folderId =
            setPropertyShelfActiveFolder (Some(latestLayer.current.Id, folderId))

        let setPropertyShelfFolderColor folderId color =
            applyUiState (PropertyShelf.setFolderColor latestSession.current folderId color)

        let propertyShelf =
            React.useMemo (
                (fun () ->
                    Html.section [
                        prop.className
                            "swt:flex swt:min-w-0 swt:flex-col swt:gap-3 swt:rounded-lg swt:border swt:border-base-300 swt:bg-base-100/80 swt:p-3 swt:shadow-sm"
                        // Always-on anchor for the interactive tutorial's spotlight.
                        prop.custom ("data-tutorial", "provenance-property-shelf")
                        if debug then
                            prop.testId "provenance-property-shelf"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:min-w-0 swt:items-center swt:justify-between swt:gap-2"
                                prop.children [
                                    Html.div [
                                        prop.className
                                            "swt:flex swt:min-w-0 swt:items-center swt:gap-2 swt:text-sm swt:font-medium"
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:min-w-0 swt:truncate"
                                                prop.text "Annotations"
                                            ]
                                        ]
                                    ]
                                    Html.button [
                                        prop.title (
                                            if isPropertyShelfExpanded then
                                                "Minimize annotation folders"
                                            else
                                                "Expand annotation folders"
                                        )
                                        prop.type'.button
                                        prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:size-8 swt:p-0"
                                        prop.custom ("aria-expanded", isPropertyShelfExpanded)
                                        // Always-on anchor (paired with aria-expanded) so the
                                        // tutorial can point at the toggle of a minimized shelf.
                                        prop.custom ("data-tutorial", "provenance-property-shelf-toggle")
                                        prop.ariaLabel (
                                            if isPropertyShelfExpanded then
                                                "Minimize annotation folders"
                                            else
                                                "Expand annotation folders"
                                        )
                                        if debug then
                                            prop.testId "provenance-property-shelf-toggle"
                                        prop.onClick (fun _ ->
                                            setIsPropertyShelfExpanded (not isPropertyShelfExpanded)
                                        )
                                        prop.children [
                                            Html.i [
                                                prop.className [
                                                    "swt:iconify swt:size-4"
                                                    if isPropertyShelfExpanded then
                                                        "swt:fluent--chevron-up-20-regular"
                                                    else
                                                        "swt:fluent--chevron-down-20-regular"
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            if isPropertyShelfExpanded then
                                FolderedDraggableList.FolderedDraggableList<PropertyShelfItemPayload>(
                                    propertyShelfFolders,
                                    (fun _ item ->
                                        DragDrop.folderPropertyDragId item.Payload.SourceSide item.Payload.Property
                                    ),
                                    ?activeFolderId = propertyShelfActiveFolderId,
                                    onActiveFolderIdChange = setPropertyShelfActiveFolderId,
                                    onSetFolderColor = setPropertyShelfFolderColor,
                                    className = "swt:min-w-0 swt:motion-pop-in",
                                    debug = debug
                                )
                        ]
                    ]
                ),
                [|
                    box propertyShelfFolders
                    box isPropertyShelfExpanded
                    box propertyShelfActiveFolderId
                |]
            )

        React.useEffect (
            (fun () ->
                let next =
                    latestUiState.current
                    |> State.RailOrder.ensure layer.Id ProvenanceSide.Input inputRailProjection.Headers
                    |> State.RailOrder.ensure layer.Id ProvenanceSide.Output outputRailProjection.Headers

                if next <> latestUiState.current then
                    commitUiState next
            ),
            [|
                box layer.Id
                box inputRailProjection.Headers
                box outputRailProjection.Headers
            |]
        )

        // Side hiding consolidates annotations onto the visible rail for the layer
        // it runs on, so the flag must not carry over to another layer whose hidden
        // side was never consolidated. Reveal both sides again on every layer switch.
        React.useEffect ((fun () -> setHiddenSide None), [| box layer.Id |])

        // Splitter drags write the grid template straight to the DOM per animation
        // frame and commit the ratios to state once on release; committing per
        // pointermove re-rendered the entire editor for every mouse step.
        let liveSplitRatios = React.useRef (None: PanelRatios option)
        let splitFrame = React.useRef (None: float option)
        let activeSplit, setActiveSplit = React.useState<Splitter.SplitterSide option> None

        let applyLiveSplitTemplate =
            React.useCallback (
                (fun () ->
                    splitFrame.current <- None

                    match surfaceRef.current, liveSplitRatios.current with
                    | Some surface, Some ratios -> Splitter.applyTemplate surface (Splitter.template ratios)
                    | _ -> ()
                ),
                [||]
            )

        let updateLiveSplit =
            React.useCallback (
                (fun (side, clientX) ->
                    match surfaceRef.current with
                    | None -> ()
                    | Some surface ->
                        let rect = surface.getBoundingClientRect ()

                        let rawPercent =
                            if rect.width <= 0. then
                                0.
                            else
                                ((clientX - rect.left) / rect.width) * 100.

                        let splitPercent = rawPercent |> max 0. |> min 100. |> round |> int

                        let current =
                            liveSplitRatios.current
                            |> Option.defaultWith (fun () ->
                                State.PanelLayout.get latestLayer.current.Id latestUiState.current
                            )

                        let next =
                            match side with
                            | Splitter.Left -> State.PanelLayout.clamped splitPercent current.Right
                            | Splitter.Right -> State.PanelLayout.clamped current.Left (100 - splitPercent)

                        liveSplitRatios.current <- Some next

                        match splitFrame.current with
                        | Some _ -> ()
                        | None -> splitFrame.current <- Some(AnimationFrame.request applyLiveSplitTemplate)
                ),
                [||]
            )

        let finishSplitDrag =
            React.useCallback (
                (fun () ->
                    match splitDrag.current with
                    | None -> ()
                    | Some _ ->
                        splitDrag.current <- None

                        match splitFrame.current with
                        | Some handle ->
                            AnimationFrame.cancel handle
                            splitFrame.current <- None
                        | None -> ()

                        match liveSplitRatios.current with
                        | Some ratios -> applyUiState (State.PanelLayout.set latestLayer.current.Id ratios)
                        | None -> ()

                        liveSplitRatios.current <- None
                        setActiveSplit None
                ),
                [||]
            )

        React.useEffectOnce (fun () ->
            let onMove =
                fun (event: Browser.Types.PointerEvent) ->
                    match splitDrag.current with
                    | Some side -> updateLiveSplit (side, event.clientX)
                    | None -> ()

            let stopDragging = fun (_: Browser.Types.PointerEvent) -> finishSplitDrag ()

            Browser.Dom.document.addEventListener ("pointermove", unbox onMove)
            Browser.Dom.document.addEventListener ("pointerup", unbox stopDragging)
            Browser.Dom.document.addEventListener ("pointercancel", unbox stopDragging)

            FsReact.createDisposable (fun () ->
                Browser.Dom.document.removeEventListener ("pointermove", unbox onMove)
                Browser.Dom.document.removeEventListener ("pointerup", unbox stopDragging)
                Browser.Dom.document.removeEventListener ("pointercancel", unbox stopDragging)
            )
        )

        let startPanelDrag side (event: Browser.Types.PointerEvent) =
            event.preventDefault ()
            splitDrag.current <- Some side
            liveSplitRatios.current <- Some(State.PanelLayout.get latestLayer.current.Id latestUiState.current)
            setActiveSplit (Some side)
            updateLiveSplit (side, event.clientX)

        let nudgeSplit side delta =
            applyUiState (fun state ->
                let layerId = latestLayer.current.Id
                let current = State.PanelLayout.get layerId state

                match side with
                | Splitter.Left -> State.PanelLayout.setLeft layerId (current.Left + delta) state
                | Splitter.Right -> State.PanelLayout.setRight layerId (current.Right - delta) state
            )

        let resetSplit () =
            applyUiState (State.PanelLayout.set latestLayer.current.Id State.PanelLayout.defaultRatios)

        let pointerSensor =
            DndKit.useSensor (
                DndKit.PointerSensor,
                {|
                    activationConstraint = {| distance = 6 |}
                |}
            )

        let sensors = DndKit.useSensors [| pointerSensor |]

        // Single-step undo: the session as it was before the last published edit.
        // Layer switches deliberately do not overwrite this slot, so an edit stays
        // undoable across navigation. State (not a ref) so the Undo button's
        // disabled state is render-pure; a companion ref mirrors it for handlers
        // that need a synchronous read (every write site sets both together, so
        // they never diverge within a render).
        let undoSession, setUndoSession = React.useState (None: ProvenanceSession option)
        let latestUndoSession = React.useRef undoSession

        // The session this component itself last published (via publish or
        // undo). Lets an effect below tell "the host echoed back what we
        // published" apart from "the host swapped the session prop under us",
        // so an external replacement invalidates a stale undo snapshot instead
        // of leaving it able to resurrect data the host has already replaced.
        let lastPublishedSession = React.useRef session

        React.useEffect (
            (fun () ->
                if not (Object.ReferenceEquals(session, lastPublishedSession.current)) then
                    latestUndoSession.current <- None
                    setUndoSession None
                    lastPublishedSession.current <- session
            ),
            [| box session |]
        )

        // publish and the actions below read session/layer/UI state through the
        // latest refs so that memoized subtrees never fire an action against a
        // session that has since been replaced.
        let publishResult recordUndo (result: SessionResult) =
            match result with
            | Ok(next, patches) ->
                LiveDrag.clear liveDragStore.current

                if recordUndo then
                    latestUndoSession.current <- Some latestSession.current
                    setUndoSession (Some latestSession.current)

                commitUiState (State.Publish.onSuccess next latestUiState.current)

                lastPublishedSession.current <- next
                latestOnChange.current { Session = next; Patches = patches }
            | Error error ->
                LiveDrag.clear liveDragStore.current

                commitUiState (State.Publish.onError (SessionErrors.text error) latestUiState.current)

        let publish =
            React.useCallback ((fun (result: SessionResult) -> publishResult true result), [||])

        let undoLast () =
            match latestUndoSession.current with
            | Some previous ->
                latestUndoSession.current <- None
                setUndoSession None
                LiveDrag.clear liveDragStore.current

                commitUiState {
                    State.Publish.onSuccess previous latestUiState.current with
                        Detail = None
                }

                lastPublishedSession.current <- previous
                latestOnChange.current { Session = previous; Patches = [] }
            | None -> ()

        let createSet =
            React.useCallback ((fun command -> EditorActions.createSet latestSession.current publish command), [||])

        let addPaletteValue side property value unit =
            let layer = latestLayer.current
            applyUiState (State.Palette.addValue layer.Id side property value unit)

        let toggleSideGrouping sideId side header =
            applyUiState (State.GroupingAssignments.toggleSide sideId side header)

        let togglePropertyExpanded side header =
            applyUiState (State.PropertyExpansion.toggle latestLayer.current.Id side header)

        let toggleSelection side groupId =
            applyUiState (State.Selection.toggle latestLayer.current.Id side groupId)

        let toggleGroupDetail side groupId =
            applyUiState (State.Detail.toggleGroup side groupId)

        let sourceInfoForValue value =
            Session.propertyValueSourceInfo layer value

        let setPropertyColor header color =
            let colorContext =
                State.PropertyColors.visibleColorContextForLayer latestSession.current latestLayer.current

            let update =
                match color with
                | Some selectedColor -> State.PropertyColors.setColor colorContext.Id header selectedColor
                | None -> State.PropertyColors.clearColor colorContext.Id header

            applyUiState update

        let setSourceColor sourceId color =
            let update =
                match color with
                | Some selectedColor -> State.PropertyColors.setSourceColor sourceId selectedColor
                | None -> State.PropertyColors.clearSourceColor sourceId

            applyUiState update

        let setPropertySort sort =
            let sortedInputHeaders =
                PropertyProjection.sortHeaders
                    sort
                    inputRailProjection.StatsByHeader
                    inputRailProjection.ConnectionCountByHeader
                    inputRailProjection.Headers

            let sortedOutputHeaders =
                PropertyProjection.sortHeaders
                    sort
                    outputRailProjection.StatsByHeader
                    outputRailProjection.ConnectionCountByHeader
                    outputRailProjection.Headers

            applyUiState (
                State.Filters.setPropertySort sort
                >> State.RailOrder.reorderVisible layer.Id ProvenanceSide.Input sortedInputHeaders
                >> State.RailOrder.reorderVisible layer.Id ProvenanceSide.Output sortedOutputHeaders
            )

        // Defensive against double-fire regressions (e.g. a stray onPointerUp
        // reintroduced next to onClick): a second invocation for a batch that
        // was already confirmed and cleared is a no-op rather than reapplying
        // the batch to whatever session happens to be current.
        let confirmBatch (pending: PendingAssignmentBatch) =
            if latestUiState.current.PendingAssignmentBatch.IsSome then
                EditorActions.applyAssignmentBatch latestSession.current publish pending.Batch

        let connectSetPairs pairs =
            EditorActions.connectSetPairs latestSession.current publish pairs

        let removeDisplayConnection =
            React.useCallback (
                (fun (connection: DisplayConnection) ->
                    match Session.removeConnections connection.ConnectionIds latestSession.current with
                    | Ok(next, patches) ->
                        LiveDrag.clear liveDragStore.current
                        latestUndoSession.current <- Some latestSession.current
                        setUndoSession (Some latestSession.current)

                        commitUiState {
                            State.Publish.onSuccess next latestUiState.current with
                                Detail = None
                        }

                        lastPublishedSession.current <- next
                        latestOnChange.current { Session = next; Patches = patches }
                    | Error error ->
                        LiveDrag.clear liveDragStore.current

                        commitUiState (State.Publish.onError (SessionErrors.text error) latestUiState.current)
                ),
                [||]
            )

        let resolveAllToAll (pending: PendingMemberResolution) =
            match
                latestLookups.current.FindGroup ProvenanceSide.Input pending.InputGroupId,
                latestLookups.current.FindGroup ProvenanceSide.Output pending.OutputGroupId
            with
            | Some inputGroup, Some outputGroup ->
                EditorActions.allMemberPairs inputGroup outputGroup |> connectSetPairs
            | _ -> applyUiState State.MemberResolution.clearPending

        let resolvePairByOrder (pending: PendingMemberResolution) =
            match
                latestLookups.current.FindGroup ProvenanceSide.Input pending.InputGroupId,
                latestLookups.current.FindGroup ProvenanceSide.Output pending.OutputGroupId
            with
            | Some inputGroup, Some outputGroup ->
                match EditorActions.orderedMemberPairs inputGroup outputGroup with
                | Some pairs -> connectSetPairs pairs
                | None -> applyUiState State.MemberResolution.clearPending
            | _ -> applyUiState State.MemberResolution.clearPending

        let isGroupedCard side groupId =
            lookups.FindGroup side groupId
            |> Option.exists (fun group -> group.GroupingValues |> List.isEmpty |> not)

        let isConnectedToExpanded side groupId =
            // Only a single manually expanded card pulls its connected grouped cards
            // open with it; explicit multi-card expansion (manual connection
            // resolution) opens exactly the requested cards.
            uiState.ExpandedGroups.Count = 1
            && isGroupedCard side groupId
            && (connections
                |> List.exists (fun connection ->
                    match side with
                    | ProvenanceSide.Input ->
                        connection.SourceGroupId = groupId
                        && State.Detail.isGroupExpanded ProvenanceSide.Output connection.TargetGroupId uiState
                    | ProvenanceSide.Output ->
                        connection.TargetGroupId = groupId
                        && State.Detail.isGroupExpanded ProvenanceSide.Input connection.SourceGroupId uiState
                ))

        let isGroupExpanded side groupId =
            State.Detail.isGroupExpanded side groupId uiState
            || isConnectedToExpanded side groupId

        // Rule: any handler that can publish (or that reads session/uiState/
        // lookups from a callback that might fire after further renders, e.g.
        // layer select, add layer, member-resolution actions) must read
        // through a `latest*` ref, never close over a render-scope value.
        // Render values (session, uiState, lookups, layer, ...) are only for
        // this render's JSX output. React 18 currently flushes discrete events
        // synchronously so a stale closure usually "gets away with it", but
        // batching across a transition or a memoized subtree (React.memo)
        // would turn a stale read into a real lost-update race.
        let dragContext = {
            Session = session
            Layer = layer
            UiState = uiState
            GetUiState = fun () -> latestUiState.current
            Publish = publish
            SetUiState = commitUiState
            Lookups = lookups
            ConnectSetPairs = connectSetPairs
        }

        // Runs on every commit (no dep array), after the browser can deliver a
        // user event but before it actually does - so a handler firing right
        // after this commit always sees this render's values, and a render
        // that gets thrown away without committing never writes these at all.
        React.useLayoutEffect (fun () ->
            latestUiState.current <- uiState
            latestSession.current <- session
            latestLayer.current <- layer
            latestOnChange.current <- onChange
            latestConnections.current <- connections
            latestGroups.current <- inputGroups, outputGroups
            latestLookups.current <- lookups
            latestArmedHandle.current <- armedHandle
            latestDragContext.current <- Some dragContext
        )

        let railSideLabel side =
            match side with
            | ProvenanceSide.Input -> "input"
            | ProvenanceSide.Output -> "output"

        let visibleSideFor hidden =
            match hidden with
            | ProvenanceSide.Input -> ProvenanceSide.Output
            | ProvenanceSide.Output -> ProvenanceSide.Input

        let toggleRail side =
            setOpenRail (if openRail = Some side then None else Some side)

        // Hiding a side removes its rail and cards from the layout, and really
        // relocates every switchable annotation on it to the visible rail - a
        // permanent move, so the property stays put once the side is revealed and
        // any solo grouping it held on the hidden side is dropped (it can no longer
        // be managed). Non-switchable annotations stay parked until the side is
        // shown again. Hiding one side always reveals the other.
        let hideSide (side: ProvenanceSide) =
            let hiddenSideId =
                match side with
                | ProvenanceSide.Input -> layer.InputSideId
                | ProvenanceSide.Output -> layer.OutputSideId

            applyUiState (
                State.SideVisibility.consolidateToVisible
                    layer.Id
                    side
                    hiddenSideId
                    (fun header -> PropertyRails.canSwitchHeader header layer.Model)
            )

            setHiddenSide (Some side)

        // Icon-only (a left/right panel glyph naming the column it governs) keeps
        // the pair from wrapping the already-dense toolbar onto a second row.
        let sideVisibilityToggle (side: ProvenanceSide) =
            let isVisible = hiddenSide <> Some side
            let sideText = railSideLabel side

            let label =
                if isVisible then
                    $"Hide {sideText}s and their annotations"
                else
                    $"Show {sideText}s and their annotations"

            Html.button [
                prop.type'.button
                prop.title label
                prop.className [
                    "swt:btn swt:btn-xs swt:btn-square"
                    if isVisible then "swt:btn-primary" else "swt:btn-ghost"
                ]
                prop.custom ("aria-pressed", isVisible)
                prop.ariaLabel label
                // Always-on anchor for the interactive tutorial's spotlight.
                prop.custom ("data-tutorial", $"provenance-side-visibility-{side}")
                if debug then
                    prop.testId $"provenance-side-visibility-{side}"
                prop.onClick (fun _ -> if isVisible then hideSide side else setHiddenSide None)
                prop.children [
                    Html.i [
                        prop.className [
                            "swt:iconify swt:size-4"
                            match side, isVisible with
                            | ProvenanceSide.Input, true -> "swt:fluent--panel-left-20-regular"
                            | ProvenanceSide.Input, false -> "swt:fluent--panel-left-20-filled"
                            | ProvenanceSide.Output, true -> "swt:fluent--panel-right-20-regular"
                            | ProvenanceSide.Output, false -> "swt:fluent--panel-right-20-filled"
                        ]
                    ]
                ]
            ]

        let isRejectedPropertyRailDrop targetSide =
            let headerCannotSwitch sourceSide headerId =
                sourceSide <> targetSide
                && (lookups.FindProperty headerId
                    |> Option.exists (fun property -> PropertyRails.canSwitchHeader property layer.Model |> not))

            match activeDrag with
            | Some {
                       Payload = DragDrop.Payload.FolderPropertyHeader(sourceSide, headerId)
                   }
            | Some {
                       Payload = DragDrop.Payload.PropertyHeader(sourceSide, headerId)
                   } -> headerCannotSwitch sourceSide headerId
            | _ -> false

        let isPropertyDragActive =
            match activeDrag with
            | Some {
                       Payload = DragDrop.Payload.FolderPropertyHeader _
                   }
            | Some {
                       Payload = DragDrop.Payload.PropertyHeader _
                   } -> true
            | _ -> false

        let connectionDragSide =
            match activeDrag with
            | Some {
                       Payload = DragDrop.Payload.ConnectionHandle handle
                   } -> Some handle.Side
            | _ -> None

        let connectionInteraction: ConnectionDragHints.Interaction =
            React.useMemo (
                (fun () -> {
                    SourceSide =
                        connectionDragSide
                        |> Option.orElse (armedHandle |> Option.map (fun handle -> handle.Side))
                    Armed = armedHandle
                    OnHandleTap = handleTap
                }),
                [| box connectionDragSide; box armedHandle |]
            )

        let selectedInputGroups =
            inputGroups
            |> List.filter (fun group -> State.Selection.contains layer.Id ProvenanceSide.Input group.Id uiState)

        let selectedOutputGroups =
            outputGroups
            |> List.filter (fun group -> State.Selection.contains layer.Id ProvenanceSide.Output group.Id uiState)

        let selectedGroupCount = selectedInputGroups.Length + selectedOutputGroups.Length

        let selectedEntityCount =
            [
                yield!
                    selectedInputGroups
                    |> List.collect (fun group ->
                        group.Members |> List.map (fun member' -> ProvenanceSide.Input, member'.SetId)
                    )
                yield!
                    selectedOutputGroups
                    |> List.collect (fun group ->
                        group.Members |> List.map (fun member' -> ProvenanceSide.Output, member'.SetId)
                    )
            ]
            |> List.distinct
            |> List.length

        let layerSeedSummary =
            if selectedGroupCount > 0 then
                let groupText =
                    if selectedGroupCount = 1 then
                        "1 selected group"
                    else
                        $"{selectedGroupCount} selected groups"

                let entityText =
                    if selectedEntityCount = 1 then
                        "1 entity"
                    else
                        $"{selectedEntityCount} entities"

                $"Starts from {groupText} ({entityText})."
            else
                match layer.Model.OutputSets.Count with
                | 0 -> "Starts empty: this layer has no outputs and nothing is selected."
                | 1 -> "Starts from the single output of this layer (default)."
                | outputCount -> $"Starts from all {outputCount} outputs of this layer (default)."

        let applyValueToSelection =
            fun (propertyValue: ProvenancePropertyValue) ->
                latestDragContext.current
                |> Option.iter (fun context -> DragHandlers.applyPropertyValueToSelection context propertyValue.Id)

        let applySelectionLabel =
            if selectedGroupCount = 1 then
                "Apply to 1 selected group"
            else
                $"Apply to {selectedGroupCount} selected groups"

        let inputRailDropRejected = isRejectedPropertyRailDrop ProvenanceSide.Input
        let outputRailDropRejected = isRejectedPropertyRailDrop ProvenanceSide.Output

        let buildRailPanel side isDropRejected =
            let sideId, oppositeSideId, targetSide =
                match side with
                | ProvenanceSide.Input -> layer.InputSideId, layer.OutputSideId, ProvenanceSide.Output
                | ProvenanceSide.Output -> layer.OutputSideId, layer.InputSideId, ProvenanceSide.Input

            EditorSurface.propertyRail
                side
                layer.Model.Source
                sideId
                (match side with
                 | ProvenanceSide.Input -> activeInputRailProjection
                 | ProvenanceSide.Output -> activeOutputRailProjection)
                (match side with
                 | ProvenanceSide.Input -> inputSideState.GroupingAssignments
                 | ProvenanceSide.Output -> outputSideState.GroupingAssignments)
                (fun header -> toggleSideGrouping sideId side header)
                (fun header ->
                    applyUiState (State.GroupingAssignments.toggleBoth layer.InputSideId layer.OutputSideId header)
                )
                (fun header ->
                    applyUiState (State.GroupingAssignments.move layer.Id sideId oppositeSideId targetSide header)
                )
                (fun header -> togglePropertyExpanded side header)
                (fun header value unit -> addPaletteValue side header value unit)
                setPropertyColor
                sourceInfoForValue
                // A rail value is tentative until the same header/value/unit exists on
                // some entity; the palette copy keeps rendering after a drop, so the
                // check must look at value identity rather than the chip's own id.
                (fun propertyValue ->
                    let property = ProvenancePropertyValue.propertyKey propertyValue

                    layer.Model.PropertyValues
                    |> Map.exists (fun _ existing ->
                        ProvenancePropertyValue.belongsTo property existing
                        && existing.Value = propertyValue.Value
                        && existing.Unit = propertyValue.Unit
                    )
                    |> not
                )
                (if selectedGroupCount > 0 then
                     Some applyValueToSelection
                 else
                     None)
                applySelectionLabel
                isDropRejected
                (isPropertyDragActive && not isDropRejected)
                debug
                setIsValueChipDragging

        let inputRailPanel =
            React.useMemo (
                (fun () -> buildRailPanel ProvenanceSide.Input inputRailDropRejected),
                [|
                    box layer
                    box activeInputRailProjection
                    box inputSideState
                    box inputRailDropRejected
                    box isPropertyDragActive
                    box selectedGroupCount
                |]
            )

        let outputRailPanel =
            React.useMemo (
                (fun () -> buildRailPanel ProvenanceSide.Output outputRailDropRejected),
                [|
                    box layer
                    box activeOutputRailProjection
                    box outputSideState
                    box outputRailDropRejected
                    box isPropertyDragActive
                    box selectedGroupCount
                |]
            )

        let railPanel side =
            match side with
            | ProvenanceSide.Input -> inputRailPanel
            | ProvenanceSide.Output -> outputRailPanel

        let railColumn side =
            Html.div [
                prop.className "swt:@container/provenancePanel swt:min-w-0 swt:overflow-hidden"
                prop.children [ railPanel side ]
            ]

        let connectionCounts =
            React.useMemo (
                (fun () ->
                    connections
                    |> List.collect (fun connection -> [
                        (ProvenanceSide.Input, connection.SourceGroupId), connection.ConnectionIds.Length
                        (ProvenanceSide.Output, connection.TargetGroupId), connection.ConnectionIds.Length
                    ])
                    |> List.groupBy fst
                    |> List.map (fun (key, grouped) -> key, grouped |> List.sumBy snd)
                    |> Map.ofList
                ),
                [| box connections |]
            )

        let connectionCountFor side groupId =
            connectionCounts |> Map.tryFind (side, groupId) |> Option.defaultValue 0

        let sortedInputGroups =
            React.useMemo (
                (fun () -> Display.sortGroups uiState.Filters.GroupSort connections inputGroups),
                [| box uiState.Filters; box connections; box inputGroups |]
            )

        let sortedOutputGroups =
            React.useMemo (
                (fun () -> Display.sortGroups uiState.Filters.GroupSort connections outputGroups),
                [| box uiState.Filters; box connections; box outputGroups |]
            )

        // Group columns carry one card per display group; memoizing the rendered
        // columns keeps unrelated state changes (search text, rail expansion, panel
        // toggles) from reconciling hundreds of cards on large models.
        let withConnectionBadges = tier = LayoutTier.Narrow

        let buildGroupColumn side groups =
            let counts groupId =
                if withConnectionBadges then
                    Some(connectionCountFor side groupId)
                else
                    None

            EditorSurface.groupColumn
                side
                layer
                layer.Model
                groups
                endpointKinds
                existingEndpointNames
                createSet
                uiState
                isGroupExpanded
                toggleSelection
                toggleGroupDetail
                counts
                sourceInfoForValue
                debug
                isValueChipDragging

        let inputGroupColumn =
            React.useMemo (
                (fun () -> buildGroupColumn ProvenanceSide.Input sortedInputGroups),
                [|
                    box layer
                    box sortedInputGroups
                    box existingEndpointNames
                    box uiState.SelectedInputs
                    box uiState.ExpandedGroups
                    box connections
                    box lookups
                    box connectionCounts
                    box withConnectionBadges
                    box isValueChipDragging
                |]
            )

        let outputGroupColumn =
            React.useMemo (
                (fun () -> buildGroupColumn ProvenanceSide.Output sortedOutputGroups),
                [|
                    box layer
                    box sortedOutputGroups
                    box existingEndpointNames
                    box uiState.SelectedOutputs
                    box uiState.ExpandedGroups
                    box connections
                    box lookups
                    box connectionCounts
                    box withConnectionBadges
                    box isValueChipDragging
                |]
            )

        let groupColumnFor side =
            match side with
            | ProvenanceSide.Input -> inputGroupColumn
            | ProvenanceSide.Output -> outputGroupColumn

        // Medium tier: rails fold into slim vertical strips, one open at a time.
        let mediumRailColumn side =
            if openRail = Some side then
                Html.div [
                    prop.className
                        "swt:@container/provenancePanel swt:flex swt:min-w-0 swt:flex-col swt:gap-2 swt:overflow-hidden"
                    prop.children [
                        Html.button [
                            prop.title (
                                if side = ProvenanceSide.Input then
                                    "Hide input annotations"
                                else
                                    "Hide output annotations"
                            )
                            prop.type'.button
                            prop.className [
                                "swt:btn swt:btn-ghost swt:btn-xs swt:w-fit"
                                if side = ProvenanceSide.Output then
                                    "swt:self-end"
                            ]
                            prop.ariaLabel $"Hide {railSideLabel side} annotations"
                            if debug then
                                prop.testId $"provenance-rail-toggle-{side}"
                            prop.onClick (fun _ -> toggleRail side)
                            prop.children [
                                Html.i [

                                    prop.className [
                                        "swt:iconify swt:size-4"
                                        if side = ProvenanceSide.Input then
                                            "swt:fluent--chevron-left-20-regular"
                                        else
                                            "swt:fluent--chevron-right-20-regular"
                                    ]
                                ]
                            ]
                        ]
                        railPanel side
                    ]
                ]
            else
                Html.button [
                    prop.title (
                        if side = ProvenanceSide.Input then
                            "Show input annotations"
                        else
                            "Show output annotations"
                    )
                    prop.type'.button
                    prop.className
                        "swt:btn swt:btn-ghost swt:btn-xs swt:h-auto swt:min-h-24 swt:w-fit swt:px-1 swt:py-2"
                    prop.ariaLabel $"Show {railSideLabel side} annotations"
                    // Stable hook for the interactive tutorial: this collapsed
                    // strip is the fallback spotlight target when the rail folds,
                    // so the tour must find it without coupling to aria-label copy.
                    prop.custom ("data-tutorial", $"provenance-rail-toggle-{side}")
                    if debug then
                        prop.testId $"provenance-rail-toggle-{side}"
                    prop.onClick (fun _ -> toggleRail side)
                    prop.children [
                        Html.span [
                            prop.className "swt:[writing-mode:vertical-rl] swt:text-xs"
                            prop.text "Annotations"
                        ]
                    ]
                ]

        // Narrow tier: everything stacks and the rails collapse behind toggles.
        let narrowRailSection side =
            Html.div [
                prop.className "swt:@container/provenancePanel swt:flex swt:min-w-0 swt:flex-col swt:gap-2"
                prop.children [
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:w-fit"
                        prop.ariaLabel (
                            if openRail = Some side then
                                $"Hide {railSideLabel side} annotations"
                            else
                                $"Show {railSideLabel side} annotations"
                        )
                        // Stable hook for the interactive tutorial's fallback
                        // spotlight, only while folded - once open, the rail
                        // panel's own data-tutorial anchor is the target.
                        if openRail <> Some side then
                            prop.custom ("data-tutorial", $"provenance-rail-toggle-{side}")
                        if debug then
                            prop.testId $"provenance-rail-toggle-{side}"
                        prop.onClick (fun _ -> toggleRail side)
                        prop.children [
                            Html.i [
                                prop.className [
                                    "swt:iconify swt:size-4"
                                    if openRail = Some side then
                                        "swt:fluent--chevron-up-20-regular"
                                    else
                                        "swt:fluent--chevron-down-20-regular"
                                ]
                            ]
                            Html.span (
                                match side with
                                | ProvenanceSide.Input -> "Input annotations"
                                | ProvenanceSide.Output -> "Output annotations"
                            )
                        ]
                    ]
                    if openRail = Some side then
                        railPanel side
                ]
            ]

        let connectorLayoutSignature =
            [
                string tier
                string openRail
                string hiddenSide
                string density
                string isPropertyShelfExpanded
                string panelRatios.Left
                string panelRatios.Middle
                string panelRatios.Right
            ]
            |> String.concat "|"

        let connectorOverlayState =
            React.useMemo (
                (fun () -> ConnectorOverlayState.fromUiState uiState),
                [|
                    box uiState.ExpandedGroups
                    box uiState.Detail
                    box uiState.ExpandedProperties
                |]
            )

        let selectConnection =
            React.useCallback (
                (fun (connection: DisplayConnection) -> applyUiState (State.Detail.showConnection connection.Id)),
                [||]
            )

        let connectorOverlay =
            React.useMemo (
                (fun () ->
                    ConnectorOverlay.Main(
                        surfaceRef,
                        layer.Id,
                        layer.Model,
                        inputGroups,
                        outputGroups,
                        connections,
                        activeInputRailProjection,
                        activeOutputRailProjection,
                        connectorOverlayState,
                        connectorLayoutSignature,
                        showPropertyHeaderConnectors,
                        liveDragStore.current,
                        selectConnection,
                        onRemove = removeDisplayConnection,
                        debug = debug
                    )
                ),
                [|
                    box layer
                    box inputGroups
                    box outputGroups
                    box connections
                    box activeInputRailProjection
                    box activeOutputRailProjection
                    box connectorOverlayState
                    box connectorLayoutSignature
                    box showPropertyHeaderConnectors
                |]
            )

        // One side hidden (wide or medium): the visible rail and its cards form a
        // single cluster centered in the editor instead of stretching across it with
        // the cards hugging one edge. Two equal `1fr` spacer columns flank the cluster,
        // so whatever space is left over is split evenly on both sides at any width -
        // unlike `justify-center`, which only centers space beyond the tracks' max and
        // so leaves the cluster flush whenever the editor is not much wider than it.
        // The card column keeps the same generous minimum the two-column middle panel
        // gives it, and a fixed gutter track carries the rail-to-card connectors. No
        // splitter: there is only one rail, and the bounded tracks keep it balanced.
        let soloSurface (visibleSide: ProvenanceSide) =
            let railTrack = "minmax(10rem, 16rem)"
            let cardTrack = "minmax(24rem, 44rem)"

            let gutter =
                match density with
                | Density.EditorDensity.Compact -> "3rem"
                | _ -> "4rem"

            let filler () = Html.div [ prop.ariaHidden true ]

            Html.div [
                prop.key layer.Id
                prop.ref surfaceRef
                prop.className "swt:relative swt:mx-4 swt:grid swt:min-w-0 swt:items-start swt:motion-fade-in"
                prop.style [
                    style.custom (
                        "gridTemplateColumns",
                        match visibleSide with
                        | ProvenanceSide.Input -> $"minmax(0,1fr) {railTrack} {gutter} {cardTrack} minmax(0,1fr)"
                        | ProvenanceSide.Output -> $"minmax(0,1fr) {cardTrack} {gutter} {railTrack} minmax(0,1fr)"
                    )
                ]
                if debug then
                    prop.testId "provenance-surface"
                prop.children [
                    connectorOverlay
                    filler ()
                    match visibleSide with
                    | ProvenanceSide.Input ->
                        railColumn ProvenanceSide.Input
                        filler ()
                        groupColumnFor ProvenanceSide.Input
                    | ProvenanceSide.Output ->
                        groupColumnFor ProvenanceSide.Output
                        filler ()
                        railColumn ProvenanceSide.Output
                    filler ()
                ]
            ]

        // Keying the surface by layer remounts it on layer switches, so the change
        // fades in as one deliberate transition instead of flashing in place.
        let surface =
            match tier, hiddenSide with
            | (LayoutTier.Wide | LayoutTier.Medium), Some hidden -> soloSurface (visibleSideFor hidden)
            | LayoutTier.Wide, None ->
                Html.div [
                    prop.key layer.Id
                    prop.ref surfaceRef
                    prop.className "swt:relative swt:mx-4 swt:grid swt:min-w-0 swt:items-start swt:motion-fade-in"
                    prop.style [
                        style.custom ("gridTemplateColumns", Splitter.template panelRatios)
                    ]
                    if debug then
                        prop.testId "provenance-surface"
                    prop.children [
                        connectorOverlay
                        railColumn ProvenanceSide.Input

                        Splitter.handle
                            Splitter.Left
                            (activeSplit = Some Splitter.Left)
                            panelRatios.Left
                            (startPanelDrag Splitter.Left)
                            (nudgeSplit Splitter.Left)
                            resetSplit
                            debug

                        Html.div [
                            // The wide column gap is the gutter the group-to-group
                            // connectors are drawn in.
                            prop.className [
                                "swt:grid swt:min-w-0 swt:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] swt:items-start"
                                match density with
                                | Density.EditorDensity.Compact -> "swt:gap-12"
                                | _ -> "swt:gap-16"
                            ]
                            prop.children [
                                groupColumnFor ProvenanceSide.Input
                                groupColumnFor ProvenanceSide.Output
                            ]
                        ]

                        Splitter.handle
                            Splitter.Right
                            (activeSplit = Some Splitter.Right)
                            (100 - panelRatios.Right)
                            (startPanelDrag Splitter.Right)
                            (nudgeSplit Splitter.Right)
                            resetSplit
                            debug

                        railColumn ProvenanceSide.Output
                    ]
                ]
            | LayoutTier.Medium, None ->
                let railTrack side =
                    if openRail = Some side then
                        "minmax(10rem, 16rem)"
                    else
                        "auto"

                Html.div [
                    prop.key layer.Id
                    prop.ref surfaceRef
                    prop.className
                        "swt:relative swt:mx-4 swt:grid swt:min-w-0 swt:items-start swt:gap-x-8 swt:motion-fade-in"
                    prop.style [
                        style.custom (
                            "gridTemplateColumns",
                            $"{railTrack ProvenanceSide.Input} minmax(0, 1fr) {railTrack ProvenanceSide.Output}"
                        )
                    ]
                    if debug then
                        prop.testId "provenance-surface"
                    prop.children [
                        connectorOverlay
                        mediumRailColumn ProvenanceSide.Input
                        Html.div [
                            prop.className
                                "swt:grid swt:min-w-0 swt:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] swt:items-start swt:gap-8"
                            prop.children [
                                groupColumnFor ProvenanceSide.Input
                                groupColumnFor ProvenanceSide.Output
                            ]
                        ]
                        mediumRailColumn ProvenanceSide.Output
                    ]
                ]
            | LayoutTier.Narrow, _ ->
                // Stacked cards cannot host readable connector curves; connection
                // badges on the cards carry that information instead.
                Html.div [
                    prop.key layer.Id
                    prop.ref surfaceRef
                    prop.className
                        "swt:relative swt:mx-4 swt:flex swt:min-w-0 swt:flex-col swt:gap-4 swt:motion-fade-in"
                    if debug then
                        prop.testId "provenance-surface"
                    prop.children [
                        match hiddenSide with
                        | Some ProvenanceSide.Output ->
                            narrowRailSection ProvenanceSide.Input
                            groupColumnFor ProvenanceSide.Input
                        | Some ProvenanceSide.Input ->
                            narrowRailSection ProvenanceSide.Output
                            groupColumnFor ProvenanceSide.Output
                        | None ->
                            narrowRailSection ProvenanceSide.Input
                            groupColumnFor ProvenanceSide.Input
                            groupColumnFor ProvenanceSide.Output
                            narrowRailSection ProvenanceSide.Output
                    ]
                ]

        let content =
            Html.div [
                prop.ref rootRef
                prop.className [
                    "swt:flex swt:flex-col swt:bg-base-200 swt:overflow-auto swt:pb-4"
                    // Without an explicit height the editor fills its host instead of
                    // forcing a fixed pixel height into responsive layouts.
                    if height.IsNone then
                        "swt:h-full swt:min-h-0"
                ]
                match height with
                | Some height -> prop.style [ style.height height ]
                | None -> ()
                if debug then
                    prop.testId "provenance-editor-root"
                prop.children [
                    // The toolbar and pending prompts stay pinned while the surface scrolls.
                    Html.div [
                        prop.className
                            "swt:sticky swt:top-0 swt:z-20 swt:flex swt:flex-col swt:gap-4 swt:bg-base-200 swt:p-4"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:min-w-0 swt:flex-wrap swt:items-center swt:gap-2"
                                if debug then
                                    prop.testId "provenance-top-controls"
                                prop.children [
                                    // Search, sort and filters sit on one line
                                    // above the property shelf.
                                    Controls.FilterToolbar(
                                        uiState.Filters,
                                        (fun text -> applyUiState (State.Filters.setSearch text)),
                                        setPropertySort,
                                        (fun sort -> applyUiState (State.Filters.setGroupSort sort)),
                                        (fun filter -> applyUiState (State.Filters.setValueCountFilter filter)),
                                        (fun filter -> applyUiState (State.Filters.setOriginFilter filter)),
                                        debug = debug
                                    )
                                    Html.div [
                                        prop.className
                                            "swt:ml-auto swt:flex swt:shrink-0 swt:flex-wrap swt:items-center swt:gap-2"
                                        if debug then
                                            prop.testId "provenance-view-actions"
                                        prop.children [
                                            Html.button [
                                                prop.type'.button
                                                prop.className "swt:btn swt:btn-xs swt:btn-ghost"
                                                prop.title "Undo last change"
                                                prop.ariaLabel "Undo last change"
                                                // Always-on anchor for the interactive tutorial;
                                                // its enabled state doubles as the connect step's
                                                // "an edit was published" completion signal.
                                                prop.custom ("data-tutorial", "provenance-undo")
                                                prop.disabled undoSession.IsNone
                                                if debug then
                                                    prop.testId "provenance-undo"
                                                prop.onClick (fun _ -> undoLast ())
                                                prop.children [
                                                    Html.i [
                                                        prop.className
                                                            "swt:iconify swt:fluent--arrow-undo-20-regular swt:size-4"
                                                    ]
                                                    Html.span "Undo"
                                                ]
                                            ]
                                            sideVisibilityToggle ProvenanceSide.Input
                                            sideVisibilityToggle ProvenanceSide.Output
                                            Html.button [
                                                prop.title (
                                                    if showPropertyHeaderConnectors then
                                                        "Hide annotation header connectors"
                                                    else
                                                        "Show annotation header connectors"
                                                )
                                                prop.type'.button
                                                prop.className [
                                                    "swt:btn swt:btn-xs"
                                                    if showPropertyHeaderConnectors then
                                                        "swt:btn-primary"
                                                    else
                                                        "swt:btn-ghost"
                                                ]
                                                prop.custom ("aria-pressed", showPropertyHeaderConnectors)
                                                prop.ariaLabel (
                                                    if showPropertyHeaderConnectors then
                                                        "Hide annotation header connectors"
                                                    else
                                                        "Show annotation header connectors"
                                                )
                                                if debug then
                                                    prop.testId "provenance-property-connectors-toggle"
                                                prop.onClick (fun _ ->
                                                    setShowPropertyHeaderConnectors (not showPropertyHeaderConnectors)
                                                )
                                                prop.children [
                                                    Html.i [
                                                        prop.className [
                                                            "swt:iconify swt:size-4"
                                                            if showPropertyHeaderConnectors then
                                                                "swt:fluent--eye-20-regular"
                                                            else
                                                                "swt:fluent--eye-hide-20-regular"
                                                        ]
                                                    ]
                                                    Html.span "Annotation connectors"
                                                ]
                                            ]
                                            Html.button [
                                                match density with
                                                | Density.EditorDensity.Compact ->
                                                    prop.title "Toggle comfortable density"
                                                | Density.EditorDensity.Comfortable ->
                                                    prop.title "Toggle compact density"

                                                prop.type'.button
                                                prop.className [
                                                    "swt:btn swt:btn-xs"
                                                    if density = Density.EditorDensity.Compact then
                                                        "swt:btn-primary"
                                                    else
                                                        "swt:btn-ghost"
                                                ]
                                                prop.custom ("aria-pressed", (density = Density.EditorDensity.Compact))
                                                prop.ariaLabel (
                                                    if density = Density.EditorDensity.Compact then
                                                        "Switch to comfortable density"
                                                    else
                                                        "Switch to compact density"
                                                )
                                                if debug then
                                                    prop.testId "provenance-density-toggle"
                                                prop.onClick (fun _ ->
                                                    setDensity (
                                                        if density = Density.EditorDensity.Compact then
                                                            Density.EditorDensity.Comfortable
                                                        else
                                                            Density.EditorDensity.Compact
                                                    )
                                                )
                                                prop.text "Compact"
                                            ]
                                            Controls.HelpLegend(debug = debug)
                                            // The tutorial's sandboxed editor (marked by its
                                            // initUiState seed) must not offer a tutorial of
                                            // its own - task steps keep the whole surface
                                            // interactive, so the trigger would nest modals.
                                            if initUiState.IsNone then
                                                Html.button [
                                                    prop.type'.button
                                                    prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                                                    prop.title "Open the interactive tutorial"
                                                    prop.ariaLabel "Open the interactive tutorial"
                                                    if debug then
                                                        prop.testId "provenance-tutorial-trigger"
                                                    prop.onClick (fun _ -> setIsTutorialOpen true)
                                                    prop.children [
                                                        Html.i [
                                                            prop.className
                                                                "swt:iconify swt:fluent--lightbulb-20-regular swt:size-4"
                                                        ]
                                                        Html.span "Tutorial"
                                                    ]
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                            propertyShelf
                            // The selection bar keeps the otherwise invisible group
                            // selection visible: it drives fan-out drops and layer seeding.
                            if selectedGroupCount > 0 then
                                Html.div [
                                    prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2 swt:px-2"
                                    if debug then
                                        prop.testId "provenance-selection-bar"
                                    prop.children [
                                        Html.span [
                                            prop.className "swt:badge swt:badge-primary swt:badge-sm"
                                            prop.text (
                                                if selectedGroupCount = 1 then
                                                    "1 group selected"
                                                else
                                                    $"{selectedGroupCount} groups selected"
                                            )
                                        ]
                                        Html.span [
                                            prop.className "swt:text-xs swt:text-base-content/60"
                                            prop.text
                                                $"Dropped values apply to all {selectedEntityCount} selected entities; a new layer starts from the selection."
                                        ]
                                        Html.button [
                                            prop.type'.button
                                            prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                                            if debug then
                                                prop.testId "provenance-clear-selection"
                                            prop.onClick (fun _ ->
                                                applyUiState (State.Selection.clearLayer latestLayer.current.Id)
                                            )
                                            prop.text "Clear selection"
                                        ]
                                    ]
                                ]
                            // Alerts and prompts float over the surface from a zero-height
                            // anchor: showing them no longer pushes the whole editor down
                            // (which also forced a full connector remeasure).
                            Html.div [
                                prop.className "swt:relative swt:z-30 swt:h-0"
                                prop.children [
                                    Html.div [
                                        prop.className
                                            "swt:pointer-events-none swt:absolute swt:inset-x-0 swt:top-2 swt:mx-auto swt:flex swt:w-full swt:max-w-3xl swt:flex-col swt:gap-2 swt:px-4"
                                        prop.children [
                                            let floatingPanel content =
                                                Html.div [
                                                    prop.className
                                                        "swt:pointer-events-auto swt:rounded-box swt:shadow-lg swt:motion-pop-in"
                                                    prop.children [ content ]
                                                ]

                                            match uiState.Error with
                                            | Some error -> floatingPanel (EditorPanels.errorAlert error)
                                            | None -> Html.none

                                            match uiState.PendingAssignmentBatch with
                                            | Some batch ->
                                                floatingPanel (
                                                    EditorPanels.assignmentBatchWarning
                                                        debug
                                                        batch
                                                        confirmBatch
                                                        (fun () -> applyUiState State.AssignmentBatch.clear)
                                                )
                                            | None -> Html.none

                                            match uiState.PendingMemberResolution with
                                            | Some pending ->
                                                floatingPanel (
                                                    EditorPanels.memberResolutionPrompt
                                                        debug
                                                        pending
                                                        resolvePairByOrder
                                                        resolveAllToAll
                                                        (fun pending ->
                                                            applyUiState (State.MemberResolution.chooseManual pending)
                                                        )
                                                        (fun () -> applyUiState State.MemberResolution.clearPending)
                                                )
                                            | None -> Html.none

                                            match uiState.Hint with
                                            | Some hint ->
                                                floatingPanel (
                                                    EditorPanels.hintPanel
                                                        debug
                                                        hint
                                                        (fun () -> applyUiState State.Hint.clear)
                                                )
                                            | None -> Html.none
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    surface

                    EditorPanels.connectionDetails
                        debug
                        layer.Model
                        inputGroups
                        outputGroups
                        connections
                        uiState.Detail
                        removeDisplayConnection

                    // Layer navigation is pinned to the bottom, but only the
                    // centered pagination control itself is visually boxed.
                    Html.div [
                        prop.className
                            "swt:pointer-events-none swt:sticky swt:bottom-0 swt:z-20 swt:mt-auto swt:flex swt:justify-center swt:px-4 swt:py-2"
                        prop.children [
                            Controls.LayerPagination(
                                session,
                                (fun layerId ->
                                    Session.selectLayer layerId latestSession.current |> publishResult false
                                ),
                                (fun name ->
                                    let currentInputGroups, currentOutputGroups = latestGroups.current

                                    EditorActions.addLayer
                                        latestSession.current
                                        latestLayer.current.Id
                                        currentInputGroups
                                        currentOutputGroups
                                        latestUiState.current
                                        name
                                        publish
                                ),
                                sourceColors = uiState.PropertyColors.SourceColors,
                                onSetSourceColor = setSourceColor,
                                seedSummary = layerSeedSummary,
                                debug = debug
                            )
                        ]
                    ]

                    // The tour runs on a sandboxed sample-data editor instance, so
                    // trying the interactions cannot touch the host's session.
                    // The overlay rebuilds it from the step's checkpoint seed
                    // whenever the active step's checkpoint changes.
                    if isTutorialOpen then
                        ProvenanceTutorial.Modal(
                            (fun () -> setIsTutorialOpen false),
                            (fun checkpoint ->
                                let seed = ProvenanceTutorialSteps.checkpointSeed checkpoint

                                ProvenanceGrouping.Editor(
                                    seed.Model(),
                                    ignore,
                                    debug = debug,
                                    initUiState = seed.InitUiState,
                                    ?initialOpenRail = seed.OpenRail
                                )
                            ),
                            debug = debug
                        )
                ]
            ]

        DndKit.DndContext(
            sensors = sensors,
            collisionDetection = DndKit.pointerWithin,
            onDragStart =
                (fun event ->
                    HoverHighlight.clear hoverStore.current
                    DragHandlers.handleStart surfaceRef setActiveDrag liveDragStore.current event
                ),
            onDragMove = DragHandlers.handleMove liveDragStore.current,
            onDragCancel =
                (fun _ ->
                    setActiveDrag None
                    setArmedHandle None
                    LiveDrag.clear liveDragStore.current
                ),
            onDragEnd =
                (fun event ->
                    setActiveDrag None
                    setArmedHandle None
                    LiveDrag.clear liveDragStore.current
                    DragHandlers.handleEnd dragContext event
                ),
            children =
                Density.provider
                    density
                    (ConnectionDragHints.provider
                        connectionInteraction
                        (HoverHighlight.provider
                            hoverStore.current
                            (React.Fragment [
                                content
                                DndKit.DragOverlay(
                                    children = EditorSurface.dragOverlay lookups.FindPropertyValue debug activeDrag
                                )
                            ])))
        )

    [<ReactComponent>]
    static member Editor
        (
            initialModel: ProvenanceModel,
            onChange: ProvenanceEditorChange -> unit,
            ?height: int,
            ?debug: bool,
            ?initUiState: ProvenanceSession -> UiState,
            ?initialOpenRail: ProvenanceSide
        ) =
        let session, setSession = React.useState (fun () -> Session.init initialModel)

        let change (next: ProvenanceEditorChange) =
            setSession next.Session
            onChange next

        ProvenanceGrouping.Main(
            session,
            change,
            ?height = height,
            ?debug = debug,
            ?initUiState = initUiState,
            ?initialOpenRail = initialOpenRail
        )
