namespace Arcitisa.Components.Page.ProvenanceGrouping

/// Formatting helpers for provenance values rendered in labels, chips, and sort keys.
module Formatting =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping

    let formatValue value unit' = valueText value unit'

/// Editor-wide density, shared through context so nested cards and chips can
/// tighten their spacing without prop drilling.
module Density =

    open Fable.Core
    open Fable.Core.JsInterop
    open Feliz

    [<RequireQualifiedAccess>]
    type EditorDensity =
        | Comfortable
        | Compact

    let context = React.createContext (defaultValue = EditorDensity.Comfortable)

    [<ImportMember("react")>]
    let private createElement (comp: obj) (props: obj) (children: ReactElement) : ReactElement = jsNative

    /// Feliz 3 ships no contextProvider helper, so render the provider directly.
    let provider (value: EditorDensity) (children: ReactElement) : ReactElement =
        createElement !!context?Provider {| value = value |} children

/// Publishes the connection interaction in flight — a live handle drag or an armed
/// click-to-connect handle — so opposite-side handles can advertise themselves as
/// valid targets, and every handle can route its taps to the editor.
module ConnectionDragHints =

    open Fable.Core
    open Fable.Core.JsInterop
    open Feliz
    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Page.ProvenanceGrouping.Types

    type Interaction = {
        /// Side the active connection gesture started from (drag or armed click).
        SourceSide: ProvenanceSide option
        /// Handle armed by click/keyboard, waiting for a target tap.
        Armed: ConnectionHandleRef option
        /// Invoked when a handle is clicked or activated with the keyboard.
        OnHandleTap: ConnectionHandleRef -> unit
    }

    let idle = {
        SourceSide = None
        Armed = None
        OnHandleTap = ignore
    }

    let context = React.createContext (defaultValue = idle)

    [<ImportMember("react")>]
    let private createElement (comp: obj) (props: obj) (children: ReactElement) : ReactElement = jsNative

    /// Feliz 3 ships no contextProvider helper, so render the provider directly.
    let provider (value: Interaction) (children: ReactElement) : ReactElement =
        createElement !!context?Provider {| value = value |} children

/// Shared motion primitives: reduced-motion detection, an imperative pulse, and a
/// FLIP position animator for regrouped cards.
module Motion =

    open Fable.Core
    open Fable.Core.JsInterop
    open Browser.Types

    [<Emit("window.matchMedia('(prefers-reduced-motion: reduce)').matches")>]
    let prefersReduced () : bool = jsNative

    [<Emit("performance.now()")>]
    let now () : float = jsNative

    [<Emit("Array.from($0.querySelectorAll($1))")>]
    let queryAll (_container: HTMLElement) (_selector: string) : HTMLElement[] = jsNative

    [<Emit("$0.closest ? $0.closest($1) : null")>]
    let closest (_element: obj) (_selector: string) : Element = jsNative

    [<Emit("$0.style.transform = $1")>]
    let setTransform (_node: HTMLElement) (_value: string) : unit = jsNative

    [<Emit("$0.style.opacity = $1")>]
    let setOpacity (_node: HTMLElement) (_value: string) : unit = jsNative

    [<Emit("$0.style.removeProperty($1)")>]
    let removeStyleProperty (_node: HTMLElement) (_name: string) : unit = jsNative

    [<Emit("$0.animate($1, $2)")>]
    let private animateNode (_node: HTMLElement) (_keyframes: obj) (_options: obj) : unit = jsNative

    [<Emit("requestAnimationFrame($0)")>]
    let requestFrame (_callback: unit -> unit) : float = jsNative

    [<Emit("cancelAnimationFrame($0)")>]
    let cancelFrame (_handle: float) : unit = jsNative

    let easeOutCubic (t: float) = 1. - (1. - t) ** 3.

    /// One-off attention pulse on a DOM node. Runs through the Web Animations API so
    /// React re-renders cannot strip it mid-flight, and cleans itself up.
    let pulse (node: HTMLElement) =
        if not (prefersReduced ()) then
            animateNode
                node
                [|
                    createObj [
                        "boxShadow"
                        ==> "0 0 0 0 color-mix(in oklch, var(--color-primary) 55%, transparent)"
                    ]
                    createObj [
                        "boxShadow"
                        ==> "0 0 0 12px color-mix(in oklch, var(--color-primary) 0%, transparent)"
                    ]
                |]
                (createObj [ "duration" ==> 500; "easing" ==> "ease-out" ])

    /// Brief brightness flash that points out a chip or tab that just appeared or
    /// changed. Same WAAPI approach as pulse, so re-renders cannot strip it.
    let flash (node: HTMLElement) =
        if not (prefersReduced ()) then
            animateNode
                node
                [|
                    createObj [ "filter" ==> "brightness(1.9)" ]
                    createObj [ "filter" ==> "brightness(1)" ]
                |]
                (createObj [ "duration" ==> 600; "easing" ==> "ease-out" ])

/// FLIP (First-Last-Invert-Play) animation state for one card column. Transforms are
/// stepped per animation frame with inline styles instead of WAAPI, because the
/// connector overlay's mutation observer only sees style-attribute writes — this is
/// what lets connectors track cards while they fly to their regrouped positions.
module Flip =

    open Fable.Core
    open Browser.Types

    type Entry = {
        Node: HTMLElement
        FromX: float
        FromY: float
        FadeIn: bool
        Start: float
        /// Transform currently applied, so mid-animation re-measures can recover the
        /// node's true layout position.
        mutable AppliedX: float
        mutable AppliedY: float
    }

    let duration = 220.

    let applyAt (time: float) (entry: Entry) =
        let progress = ((time - entry.Start) / duration) |> max 0. |> min 1.
        let eased = Motion.easeOutCubic progress

        if progress >= 1. then
            Motion.removeStyleProperty entry.Node "transform"
            Motion.removeStyleProperty entry.Node "opacity"
            entry.AppliedX <- 0.
            entry.AppliedY <- 0.
            false
        else
            entry.AppliedX <- entry.FromX * (1. - eased)
            entry.AppliedY <- entry.FromY * (1. - eased)
            Motion.setTransform entry.Node $"translate({entry.AppliedX}px, {entry.AppliedY}px)"

            if entry.FadeIn then
                Motion.setOpacity entry.Node (string eased)

            true

/// CSS class builders shared by ProvenanceGrouping draggable cards, buttons, chips, and overlay previews.
module Styles =

    let dragIndicatorClasses isDragging = [
        "swt:transition swt:duration-150"
        if isDragging then
            "swt:ring-2 swt:ring-primary swt:border-primary swt:bg-primary/10 swt:shadow-md swt:opacity-80"
    ]

    let draggableButtonClasses isDragging = [
        "swt:cursor-grab swt:active:cursor-grabbing"
        yield! dragIndicatorClasses isDragging
    ]

    let draggableBoxClasses isDragging = [
        "swt:rounded-md swt:border swt:border-base-300 swt:bg-base-100 swt:shadow-sm"
        yield! draggableButtonClasses isDragging
    ]

    /// Value chips hug their content up to the property-header cap; the cap yields to
    /// the panel width when the rail gets narrow. The quiet outline (the ungrouped
    /// header button look) keeps them from competing with the header button, which
    /// turns primary while the property is grouped.
    let propertyValueButtonClasses density isDragging = [
        "swt:btn swt:btn-sm swt:btn-outline swt:bg-base-100/90 swt:w-fit swt:max-w-[min(18rem,100%)] swt:h-auto swt:justify-start swt:normal-case swt:font-medium swt:@max-xs/provenancePanel:px-2 swt:@max-xs/provenancePanel:text-[0.7rem]"
        match density with
        | Density.EditorDensity.Compact -> "swt:min-h-6 swt:px-2 swt:py-1 swt:text-[0.7rem]"
        | _ -> "swt:min-h-8 swt:px-3 swt:py-1.5 swt:text-xs"
        yield! draggableButtonClasses isDragging
    ]

    let propertyValueOverlayClasses = [
        "swt:btn swt:btn-sm swt:btn-outline swt:bg-base-100 swt:w-fit swt:max-w-[18rem] swt:min-h-8 swt:h-auto swt:justify-start swt:normal-case swt:px-3 swt:py-1.5 swt:text-xs swt:font-medium swt:pointer-events-none swt:shadow-lg swt:ring-2 swt:ring-primary swt:ring-offset-2 swt:ring-offset-base-100"
    ]

    let addPropertyValueButtonClasses = [
        "swt:btn swt:btn-sm swt:btn-outline swt:btn-primary swt:w-fit swt:max-w-full swt:min-h-8 swt:h-auto swt:justify-start swt:normal-case swt:px-3 swt:py-1.5 swt:text-xs swt:font-medium swt:@max-xs/provenancePanel:px-2 swt:@max-xs/provenancePanel:text-[0.7rem]"
    ]

/// Column wrapper that FLIP-animates its keyed children whenever a re-render moves
/// them: cards glide to their regrouped positions instead of teleporting, and the
/// per-frame style writes let the connector overlay track them mid-flight.
module FlipColumn =

    open Fable.Core
    open Feliz
    open Browser.Types

    [<ReactComponent>]
    let View (className: string, keyAttribute: string, children: ReactElement list) =
        let containerRef = React.useElementRef ()

        let positions =
            React.useRef (System.Collections.Generic.Dictionary<string, float * float>())

        let entries = React.useRef ([]: Flip.Entry list)
        let frame = React.useRef (None: float option)

        let rec runFrame () =
            frame.current <- None
            let time = Motion.now ()
            let remaining = entries.current |> List.filter (Flip.applyAt time)
            entries.current <- remaining

            if not remaining.IsEmpty then
                frame.current <- Some(Motion.requestFrame runFrame)

        let scheduleFrame () =
            match frame.current with
            | Some _ -> ()
            | None -> frame.current <- Some(Motion.requestFrame runFrame)

        // Runs on every commit of this component; the parent memoizes the element,
        // so commits only happen when the column's data actually changed.
        React.useLayoutEffect (fun () ->
            match containerRef.current with
            | None -> ()
            | Some container ->
                let containerRect = container.getBoundingClientRect ()
                let nodes = Motion.queryAll container ("[" + keyAttribute + "]")
                let hadPositions = positions.current.Count > 0
                let reduced = Motion.prefersReduced ()
                let time = Motion.now ()

                let next = System.Collections.Generic.Dictionary<string, float * float>()

                let started = ResizeArray<Flip.Entry>()

                for node in nodes do
                    let key = node.getAttribute keyAttribute
                    let rect = node.getBoundingClientRect ()

                    // Recover the true layout position of nodes that are still animating.
                    let appliedX, appliedY =
                        entries.current
                        |> List.tryFind (fun entry -> System.Object.ReferenceEquals(entry.Node, node))
                        |> Option.map (fun entry -> entry.AppliedX, entry.AppliedY)
                        |> Option.defaultValue (0., 0.)

                    let left = rect.left - containerRect.left - appliedX
                    let top = rect.top - containerRect.top - appliedY
                    next.[key] <- (left, top)

                    if not reduced && hadPositions then
                        match positions.current.TryGetValue key with
                        | true, (previousLeft, previousTop) ->
                            let deltaX = previousLeft - left
                            let deltaY = previousTop - top

                            if abs deltaX > 0.5 || abs deltaY > 0.5 then
                                started.Add {
                                    Node = node
                                    FromX = deltaX
                                    FromY = deltaY
                                    FadeIn = false
                                    Start = time
                                    AppliedX = 0.
                                    AppliedY = 0.
                                }
                        | _ ->
                            started.Add {
                                Node = node
                                FromX = 0.
                                FromY = 6.
                                FadeIn = true
                                Start = time
                                AppliedX = 0.
                                AppliedY = 0.
                            }

                positions.current <- next

                if started.Count > 0 then
                    let startedList = List.ofSeq started

                    entries.current <-
                        startedList
                        @ (entries.current
                           |> List.filter (fun entry ->
                               startedList
                               |> List.forall (fun s -> not (System.Object.ReferenceEquals(s.Node, entry.Node)))
                           ))

                    // First frame renders at the previous position immediately so the
                    // new layout never flashes before the animation starts.
                    for entry in startedList do
                        Flip.applyAt time entry |> ignore

                    scheduleFrame ()
        )

        React.useEffectOnce (fun () ->
            FsReact.createDisposable (fun () ->
                match frame.current with
                | Some handle -> Motion.cancelFrame handle
                | None -> ()
            )
        )

        Html.div [
            prop.ref containerRef
            prop.className className
            prop.children children
        ]
