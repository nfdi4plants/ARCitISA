namespace Arcitisa.Components.Composite.TutorialOverlay

open Fable.Core
open Feliz
open Browser.Types
open Arcitisa.Components.Composite.TutorialOverlay.Types

/// Spotlight geometry, relative to the tutorial content wrapper.
type private SpotlightRect = {
    Top: float
    Left: float
    Width: float
    Height: float
}

type private SpotlightState = {
    Rects: SpotlightRect list
    HostWidth: float
    HostHeight: float
}

module private TutorialSupport =

    [<Emit("$0.closest ? $0.closest($1) : null")>]
    let closest (_element: obj) (_selector: string) : Element = jsNative

    [<Emit("Array.from($0.querySelectorAll($1))")>]
    let queryAll (_container: HTMLElement) (_selector: string) : Element[] = jsNative

    [<Emit("$0.scrollIntoView({ block: 'nearest', inline: 'nearest' })")>]
    let scrollIntoView (_element: Element) : unit = jsNative

    // Pointer capture keeps fast drags glued to the card handle; it is only an
    // enhancement, so failures (e.g. synthetic events without an active
    // pointer) must not break the drag itself.
    [<Emit("(() => { try { $0.setPointerCapture($1); } catch {} })()")>]
    let setPointerCapture (_element: obj) (_pointerId: float) : unit = jsNative

    [<Emit("(() => { try { $0.releasePointerCapture($1); } catch {} })()")>]
    let releasePointerCapture (_element: obj) (_pointerId: float) : unit = jsNative

    // Selector lists often pair a region with controls inside it (a rail and
    // its chevron, say) so that folded layouts still get a highlight. When
    // both exist, ringing the region around the control's own ring is just
    // noise - the innermost matches carry the meaning.
    let private contains (outer: SpotlightRect) (inner: SpotlightRect) =
        outer.Left <= inner.Left
        && outer.Top <= inner.Top
        && outer.Left + outer.Width >= inner.Left + inner.Width
        && outer.Top + outer.Height >= inner.Top + inner.Height

    let holePadding = 6.

    // The dim's cutouts are one even-odd SVG path, where overlapping holes
    // cancel back into dim - so rects whose padded holes would intersect are
    // fused into their bounding box and highlighted as one region.
    let private intersectsPadded pad (a: SpotlightRect) (b: SpotlightRect) =
        a.Left - pad < b.Left + b.Width + pad
        && b.Left - pad < a.Left + a.Width + pad
        && a.Top - pad < b.Top + b.Height + pad
        && b.Top - pad < a.Top + a.Height + pad

    let private boundingBox (a: SpotlightRect) (b: SpotlightRect) =
        let left = min a.Left b.Left
        let top = min a.Top b.Top

        {
            Top = top
            Left = left
            Width = max (a.Left + a.Width) (b.Left + b.Width) - left
            Height = max (a.Top + a.Height) (b.Top + b.Height) - top
        }

    let private mergeOverlapping (rects: SpotlightRect list) =
        let rec add merged rect =
            match merged |> List.tryFind (intersectsPadded holePadding rect) with
            | Some other ->
                let rest = merged |> List.filter (fun existing -> existing <> other)
                add rest (boundingBox rect other)
            | None -> rect :: merged

        rects |> List.fold add [] |> List.rev

    let measure (container: HTMLElement) (selector: string option) : SpotlightState =
        let host = container.getBoundingClientRect ()

        let rects =
            selector
            |> Option.map (fun selector ->
                queryAll container selector
                |> Array.truncate 12
                |> Array.map (fun target ->
                    let rect = target.getBoundingClientRect ()

                    {
                        Top = rect.top - host.top
                        Left = rect.left - host.left
                        Width = rect.width
                        Height = rect.height
                    }
                )
                |> Array.toList
            )
            |> Option.defaultValue []

        let rects =
            rects
            |> List.filter (fun rect ->
                rects
                |> List.exists (fun other ->
                    // Strictly larger, so identical rects survive as a pair
                    // instead of eliminating each other.
                    contains rect other && rect.Width * rect.Height > other.Width * other.Height
                )
                |> not
            )
            |> mergeOverlapping

        {
            Rects = rects
            HostWidth = host.width
            HostHeight = host.height
        }

    /// Minimum distance between the card and the host edges / the spotlight.
    let cardMargin = 12.

    let cardGap = 14.

[<Erase; Mangle(false)>]
type TutorialOverlay =

    /// Interactive tutorial chrome around arbitrary content: a spotlight that
    /// follows the current step's target elements, an explanation card with
    /// Back/Skip/Close controls, and a feature list to jump between steps or
    /// restart from the beginning. Explanation steps block interactions outside
    /// the spotlight; hands-on task steps highlight every selector match (e.g.
    /// a drag source and its dropzone) and keep the whole UI interactive.
    /// `render` receives the active step's (inherited) checkpoint and its
    /// result is remounted whenever that checkpoint changes, so every step
    /// starts from the state its instructions assume.
    [<ReactComponent(true)>]
    static member Main
        (
            steps: TutorialStep[],
            onClose: unit -> unit,
            render: string option -> ReactElement,
            ?title: string,
            ?debug: bool
        ) =
        let title = defaultArg title "Interactive tutorial"
        let debug = defaultArg debug false
        let contentRef = React.useElementRef ()
        let activeIndex, setActiveIndex = React.useState 0
        let completed, setCompleted = React.useState Set.empty<string>
        let latestCompleted = React.useRef completed
        let spotlight, setSpotlight = React.useState<SpotlightState option> None
        let lastSpotlight = React.useRef (None: SpotlightState option)
        // Bumped by "Play from start" so a restart always remounts the content,
        // even when the checkpoint key alone would not change.
        let generation, setGeneration = React.useState 0

        let currentStep =
            if steps.Length = 0 then
                None
            else
                Some steps.[min activeIndex (steps.Length - 1)]

        // A step without its own checkpoint continues from the nearest earlier
        // one; the content only remounts when this effective key changes.
        let effectiveCheckpoint =
            if steps.Length = 0 then
                None
            else
                steps.[.. min activeIndex (steps.Length - 1)]
                |> Array.rev
                |> Array.tryPick (fun step -> step.Checkpoint)

        // The content is rebuilt only when the effective checkpoint or the restart
        // generation changes - the same signal the KeyedFragment below remounts on.
        // Without this, `render` runs on every overlay render (spotlight polling,
        // card drags) and the reconciler discards the fresh subtree it builds.
        let renderedContent =
            React.useMemo ((fun () -> render effectiveCheckpoint), [| box generation; box effectiveCheckpoint |])

        let markCompleted stepId =
            let next = Set.add stepId latestCompleted.current
            latestCompleted.current <- next
            setCompleted next

        let goToNext () =
            // Explanation steps count as explored once read past; a task step
            // only earns its checkmark from the interaction itself, so
            // skipping it leaves the step open in the feature list.
            currentStep
            |> Option.iter (fun step ->
                if step.Task.IsNone then
                    markCompleted step.Id
            )

            if activeIndex < steps.Length - 1 then
                setActiveIndex (activeIndex + 1)
            else
                onClose ()

        // Spotlight tracking: scroll the first target into view once per step,
        // then poll the rects - the host UI moves elements around as the user
        // follows task instructions, and a step's target may only appear after
        // an earlier task (polling picks it up without any host cooperation).
        React.useEffect (
            (fun () ->
                let measureNow () =
                    match contentRef.current, currentStep with
                    | Some container, Some step ->
                        let next = Some(TutorialSupport.measure container step.TargetSelector)

                        if next <> lastSpotlight.current then
                            lastSpotlight.current <- next
                            setSpotlight next
                    | _ ->
                        if lastSpotlight.current.IsSome then
                            lastSpotlight.current <- None
                            setSpotlight None

                match contentRef.current, currentStep with
                | Some container, Some { TargetSelector = Some selector } ->
                    let target = container.querySelector selector

                    if not (isNull target) then
                        TutorialSupport.scrollIntoView target
                | _ -> ()

                measureNow ()
                let interval = JS.setInterval measureNow 250
                FsReact.createDisposable (fun () -> JS.clearInterval interval)
            ),
            [| box activeIndex |]
        )

        // Hands-on steps watch for their interaction and mark the step
        // completed when it happens - the card then shows the success state
        // and Skip turns into Next; the user always moves on themselves.
        React.useEffect (
            (fun () ->
                match currentStep with
                | None -> FsReact.createDisposable ignore
                | Some step ->
                    let isPending () =
                        not (latestCompleted.current.Contains step.Id)

                    match step.Advance with
                    | TutorialAdvance.Manual -> FsReact.createDisposable ignore
                    | TutorialAdvance.OnEvent(eventType, selector) ->
                        let handler =
                            fun (event: Event) ->
                                // Only events inside the wrapped content count: the
                                // document-level listener would otherwise complete the
                                // step from selector matches in unrelated host UI.
                                let insideContent =
                                    match contentRef.current with
                                    | Some container ->
                                        not (isNull event.target) && container.contains (unbox event.target)
                                    | None -> false

                                if
                                    isPending ()
                                    && insideContent
                                    && not (isNull (TutorialSupport.closest event.target selector))
                                then
                                    markCompleted step.Id

                        // Capture phase, so a host handler stopping propagation
                        // cannot swallow the completion signal.
                        Browser.Dom.document.addEventListener (eventType, handler, true)

                        FsReact.createDisposable (fun () ->
                            Browser.Dom.document.removeEventListener (eventType, handler, true)
                        )
                    | TutorialAdvance.OnCondition check ->
                        let interval =
                            JS.setInterval
                                (fun () ->
                                    match contentRef.current with
                                    | Some container when isPending () && check container -> markCompleted step.Id
                                    | _ -> ()
                                )
                                400

                        FsReact.createDisposable (fun () -> JS.clearInterval interval)
            ),
            [| box activeIndex |]
        )

        let restart () =
            latestCompleted.current <- Set.empty
            setCompleted Set.empty
            setActiveIndex 0
            setGeneration (generation + 1)

        let closeButton testId =
            Html.button [
                prop.type'.button
                prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:size-7 swt:p-0"
                prop.title "Close tutorial"
                prop.ariaLabel "Close tutorial"
                if debug then
                    prop.testId testId
                prop.onClick (fun _ -> onClose ())
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--dismiss-circle-24-regular swt:size-4"
                    ]
                ]
            ]

        let spotlightRing (index: int) (rect: SpotlightRect) =
            let pad = TutorialSupport.holePadding

            Html.div [
                prop.key $"tutorial-spotlight-ring-{index}"
                prop.className
                    "swt:absolute swt:pointer-events-none swt:rounded-lg swt:ring-2 swt:ring-primary swt:transition-all swt:duration-200"
                // Tests and users only need one canonical spotlight handle.
                if debug && index = 0 then
                    prop.testId "tutorial-spotlight"
                prop.style [
                    style.top (length.px (rect.Top - pad))
                    style.left (length.px (rect.Left - pad))
                    style.width (length.px (rect.Width + 2. * pad))
                    style.height (length.px (rect.Height + 2. * pad))
                ]
            ]

        let spotlightPanels =
            match currentStep, spotlight with
            | Some step,
              Some {
                       Rects = (_ :: _) as rects
                       HostWidth = hostWidth
                       HostHeight = hostHeight
                   } when step.Task.IsSome ->
                // Task steps must leave the whole UI usable (a drag can travel
                // across regions no spotlight covers), so the dim is a single
                // click-through SVG with a cutout per highlighted target.
                let pad = TutorialSupport.holePadding

                let holePath (rect: SpotlightRect) =
                    let x = max 0. (rect.Left - pad)
                    let y = max 0. (rect.Top - pad)
                    let width = rect.Width + 2. * pad
                    let height = rect.Height + 2. * pad
                    $"M{x} {y}h{width}v{height}h{-width}Z"

                let path =
                    $"M0 0H{hostWidth}V{hostHeight}H0Z"
                    + (rects |> List.map holePath |> String.concat "")

                [
                    Html.div [
                        prop.key "tutorial-dim-cutouts"
                        prop.className "swt:absolute swt:inset-0 swt:pointer-events-none swt:text-neutral/50"
                        prop.children [
                            Svg.svg [
                                svg.className "swt:h-full swt:w-full"
                                svg.children [
                                    Svg.path [
                                        svg.d path
                                        svg.custom ("fill", "currentColor")
                                        svg.custom ("fillRule", "evenodd")
                                    ]
                                ]
                            ]
                        ]
                    ]
                    yield! rects |> List.mapi spotlightRing
                ]
            | _,
              Some {
                       Rects = firstRect :: _
                       HostWidth = hostWidth
                       HostHeight = hostHeight
                   } ->
                // Explanation steps: spotlight the first match and block
                // interactions everywhere else so the tour stays on rails.
                // Everything in the overlay carries a stable key: the children
                // list changes shape between step kinds, and index-based
                // reconciliation would morph the step card's DOM node into a
                // dim panel (breaking element identity mid-tour).
                let pad = TutorialSupport.holePadding
                let holeTop = max 0. (firstRect.Top - pad)
                let holeLeft = max 0. (firstRect.Left - pad)
                let holeRight = min hostWidth (firstRect.Left + firstRect.Width + pad)
                let holeBottom = min hostHeight (firstRect.Top + firstRect.Height + pad)

                let dim (key: string) (top: float) (left: float) (width: float) (height: float) =
                    Html.div [
                        prop.key key
                        prop.className
                            "swt:absolute swt:pointer-events-auto swt:bg-neutral/60 swt:transition-all swt:duration-200"
                        prop.style [
                            style.top (length.px top)
                            style.left (length.px left)
                            style.width (length.px (max 0. width))
                            style.height (length.px (max 0. height))
                        ]
                    ]

                [
                    dim "tutorial-dim-top" 0. 0. hostWidth holeTop
                    dim "tutorial-dim-bottom" holeBottom 0. hostWidth (hostHeight - holeBottom)
                    dim "tutorial-dim-left" holeTop 0. holeLeft (holeBottom - holeTop)
                    dim "tutorial-dim-right" holeTop holeRight (hostWidth - holeRight) (holeBottom - holeTop)
                    spotlightRing 0 firstRect
                ]
            | _ -> [
                // No target found (or none configured): keep the dim purely
                // visual so a task's target can still be created by the user.
                Html.div [
                    prop.key "tutorial-dim-full"
                    prop.className "swt:absolute swt:inset-0 swt:pointer-events-none swt:bg-neutral/30"
                ]
              ]

        // The card is measured after every commit so placement can use its real
        // size: the height changes with the step and completion state, and the
        // width falls below its swt:w-80 default when its max-width clamps in.
        let cardRef = React.useElementRef ()
        let cardSize, setCardSize = React.useState ((320., 220.))
        let cardWidth, cardHeight = cardSize

        React.useLayoutEffect (fun () ->
            match cardRef.current with
            | Some card ->
                let rect = card.getBoundingClientRect ()

                if abs (rect.width - cardWidth) > 0.5 || abs (rect.height - cardHeight) > 0.5 then
                    setCardSize (rect.width, rect.height)
            | None -> ()
        )

        // Dragging the card by its header overrides automatic placement until
        // the next step; automatic placement resumes there.
        let manualPosition, setManualPosition = React.useState<(float * float) option> None
        let cardDragOffset = React.useRef (None: (float * float) option)

        React.useEffect ((fun () -> setManualPosition None), [| box activeIndex |])

        let startCardDrag (event: PointerEvent) =
            // Buttons in the header (close) keep their click behavior.
            if isNull (TutorialSupport.closest event.target "button") then
                match cardRef.current with
                | Some card ->
                    let cardRect = card.getBoundingClientRect ()

                    cardDragOffset.current <- Some(event.clientX - cardRect.left, event.clientY - cardRect.top)

                    TutorialSupport.setPointerCapture event.currentTarget event.pointerId
                    event.preventDefault ()
                | None -> ()

        let moveCardDrag (event: PointerEvent) =
            match cardDragOffset.current, contentRef.current with
            | Some(offsetX, offsetY), Some host ->
                let hostRect = host.getBoundingClientRect ()

                let left =
                    event.clientX - hostRect.left - offsetX
                    |> min (hostRect.width - cardWidth)
                    |> max 0.

                let top =
                    event.clientY - hostRect.top - offsetY
                    |> min (hostRect.height - cardHeight)
                    |> max 0.

                setManualPosition (Some(left, top))
            | _ -> ()

        let endCardDrag (event: PointerEvent) =
            if cardDragOffset.current.IsSome then
                cardDragOffset.current <- None
                TutorialSupport.releasePointerCapture event.currentTarget event.pointerId

        let stepCard =
            match currentStep with
            | None -> Html.none
            | Some step ->
                let isStepDone = completed.Contains step.Id

                let positionProps =
                    match manualPosition, spotlight with
                    | Some(left, top), _ -> [ style.left (length.px left); style.top (length.px top) ]
                    | None,
                      Some {
                               Rects = (first :: _) as rects
                               HostWidth = hostWidth
                               HostHeight = hostHeight
                           } ->
                        // The card must never cover a highlighted element: try
                        // below, above, right and left of the (first) spotlight
                        // rect and take the first spot that clears every
                        // highlight; if the layout leaves no clear spot, take
                        // the one covering the least of it.
                        let highlighted = if step.Task.IsSome then rects else [ first ]
                        let cardW = cardWidth
                        let margin = TutorialSupport.cardMargin
                        let gap = TutorialSupport.holePadding + TutorialSupport.cardGap

                        let clampLeft value =
                            value |> min (hostWidth - cardW - margin) |> max margin

                        let clampTop value =
                            value |> min (hostHeight - cardHeight - margin) |> max margin

                        let centeredLeft = clampLeft (first.Left + first.Width / 2. - cardW / 2.)

                        let centeredTop = clampTop (first.Top + first.Height / 2. - cardHeight / 2.)

                        let candidates = [
                            centeredLeft, clampTop (first.Top + first.Height + gap)
                            centeredLeft, clampTop (first.Top - gap - cardHeight)
                            clampLeft (first.Left + first.Width + gap), centeredTop
                            clampLeft (first.Left - gap - cardW), centeredTop
                        ]

                        let overlapArea (left: float, top: float) (rect: SpotlightRect) =
                            let pad = TutorialSupport.holePadding

                            let overlapWidth =
                                min (left + cardW) (rect.Left + rect.Width + pad) - max left (rect.Left - pad)

                            let overlapHeight =
                                min (top + cardHeight) (rect.Top + rect.Height + pad) - max top (rect.Top - pad)

                            max 0. overlapWidth * max 0. overlapHeight

                        let left, top =
                            candidates
                            |> List.tryFind (fun candidate ->
                                highlighted |> List.forall (fun rect -> overlapArea candidate rect = 0.)
                            )
                            |> Option.defaultWith (fun () ->
                                candidates
                                |> List.minBy (fun candidate -> highlighted |> List.sumBy (overlapArea candidate))
                            )

                        [ style.left (length.px left); style.top (length.px top) ]
                    | _ -> [
                        style.top (length.percent 50)
                        style.left (length.percent 50)
                        style.custom ("transform", "translate(-50%, -50%)")
                      ]

                Html.div [
                    prop.key "tutorial-step-card"
                    prop.ref cardRef
                    prop.className
                        "swt:absolute swt:z-50 swt:pointer-events-auto swt:flex swt:w-80 swt:max-w-[calc(100%-1.5rem)] swt:flex-col swt:gap-2 swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:p-4 swt:shadow-xl swt:motion-pop-in"
                    prop.style positionProps
                    if debug then
                        prop.testId "tutorial-step-card"
                    prop.children [
                        Html.div [
                            prop.className
                                "swt:flex swt:cursor-move swt:touch-none swt:select-none swt:items-center swt:justify-between swt:gap-2"
                            prop.title "Drag to move this card"
                            prop.onPointerDown startCardDrag
                            prop.onPointerMove moveCardDrag
                            prop.onPointerUp endCardDrag
                            prop.onPointerCancel endCardDrag
                            if debug then
                                prop.testId "tutorial-card-handle"
                            prop.children [
                                Html.span [
                                    prop.className "swt:badge swt:badge-primary swt:badge-sm"
                                    prop.text $"Step {activeIndex + 1} of {steps.Length}"
                                ]
                                closeButton "tutorial-card-close"
                            ]
                        ]
                        Html.h3 [
                            prop.className "swt:text-sm swt:font-semibold swt:text-primary"
                            prop.text step.Title
                        ]
                        Html.p [ prop.className "swt:text-sm"; prop.text step.Description ]
                        match step.Task with
                        | Some task ->
                            Html.div [
                                prop.className [
                                    "swt:flex swt:items-start swt:gap-2 swt:rounded-md swt:border swt:p-2 swt:text-sm"
                                    if isStepDone then
                                        "swt:border-success/60 swt:bg-success/10"
                                    else
                                        "swt:border-primary/40 swt:bg-primary/10"
                                ]
                                if debug then
                                    prop.testId "tutorial-task"
                                prop.children [
                                    Html.i [
                                        prop.className [
                                            "swt:iconify swt:mt-0.5 swt:size-4 swt:shrink-0"
                                            if isStepDone then
                                                "swt:fluent--checkmark-circle-20-regular swt:text-success"
                                            else
                                                "swt:fluent--lightbulb-20-regular swt:text-primary"
                                        ]
                                    ]
                                    Html.span [
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:font-medium"
                                                prop.text (if isStepDone then "Completed: " else "Try it: ")
                                            ]
                                            Html.span task
                                        ]
                                    ]
                                ]
                            ]
                        | None -> Html.none
                        Html.div [
                            prop.className "swt:flex swt:items-center swt:justify-between swt:gap-2 swt:pt-1"
                            prop.children [
                                Html.button [
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                                    prop.disabled ((activeIndex = 0))
                                    if debug then
                                        prop.testId "tutorial-back"
                                    prop.onClick (fun _ -> setActiveIndex (max 0 (activeIndex - 1)))
                                    prop.text "Back"
                                ]
                                Html.button [
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-primary swt:btn-xs"
                                    if debug then
                                        prop.testId "tutorial-next"
                                    prop.onClick (fun _ -> goToNext ())
                                    prop.children [
                                        Html.span (
                                            if activeIndex = steps.Length - 1 then "Finish"
                                            elif step.Task.IsSome && not isStepDone then "Skip"
                                            else "Next"
                                        )
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--arrow-right-20-regular swt:size-4"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]

        let sidebarStep index (step: TutorialStep) =
            let isCurrent = index = activeIndex
            let isCompleted = completed.Contains step.Id

            Html.li [
                prop.key step.Id
                prop.children [
                    Html.button [
                        prop.type'.button
                        prop.className [
                            "swt:flex swt:w-full swt:items-center swt:gap-2 swt:rounded-md swt:border swt:p-2 swt:text-left swt:text-sm swt:transition-colors"
                            if isCurrent then
                                "swt:border-primary swt:bg-primary/10"
                            else
                                "swt:border-transparent swt:hover:bg-base-200"
                        ]
                        if debug then
                            prop.testId $"tutorial-sidebar-step-{step.Id}"
                        prop.onClick (fun _ -> setActiveIndex index)
                        prop.children [
                            if isCompleted then
                                Html.i [
                                    prop.className
                                        "swt:iconify swt:fluent--checkmark-circle-20-regular swt:size-5 swt:shrink-0 swt:text-success"
                                ]
                            else
                                Html.span [
                                    prop.className [
                                        "swt:flex swt:size-5 swt:shrink-0 swt:items-center swt:justify-center swt:rounded-full swt:text-xs swt:font-semibold"
                                        if isCurrent then
                                            "swt:bg-primary swt:text-primary-content"
                                        else
                                            "swt:bg-base-300"
                                    ]
                                    prop.text (string (index + 1))
                                ]
                            Html.span [
                                prop.className "swt:min-w-0 swt:truncate"
                                prop.text step.Title
                            ]
                            if step.Task.IsSome then
                                Html.i [
                                    prop.className
                                        "swt:iconify swt:fluent--lightbulb-20-regular swt:ml-auto swt:size-4 swt:shrink-0 swt:text-base-content/50"
                                    prop.title "Hands-on step"
                                ]
                        ]
                    ]
                ]
            ]

        let sidebar =
            Html.aside [
                prop.className
                    "swt:flex swt:w-72 swt:shrink-0 swt:flex-col swt:gap-3 swt:overflow-y-auto swt:border-l swt:border-base-300 swt:bg-base-100 swt:p-3"
                if debug then
                    prop.testId "tutorial-sidebar"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:items-center swt:justify-between swt:gap-2"
                        prop.children [
                            Html.div [
                                prop.className
                                    "swt:flex swt:min-w-0 swt:items-center swt:gap-2 swt:text-sm swt:font-semibold"
                                prop.children [
                                    Html.i [
                                        prop.className
                                            "swt:iconify swt:fluent--book-open-24-regular swt:size-5 swt:shrink-0 swt:text-primary"
                                    ]
                                    Html.span [
                                        prop.className "swt:min-w-0 swt:truncate"
                                        prop.text title
                                    ]
                                ]
                            ]
                            closeButton "tutorial-close"
                        ]
                    ]
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-primary swt:btn-sm"
                        if debug then
                            prop.testId "tutorial-play"
                        prop.onClick (fun _ -> restart ())
                        prop.children [
                            Html.i [
                                prop.className "swt:iconify swt:fluent--play-24-regular swt:size-4"
                            ]
                            Html.span "Play from start"
                        ]
                    ]
                    Html.span [
                        prop.className "swt:text-xs swt:text-base-content/60"
                        prop.text $"{completed.Count} of {steps.Length} features explored"
                    ]
                    Html.ol [
                        prop.className "swt:flex swt:flex-col swt:gap-1"
                        prop.children (steps |> Array.mapi sidebarStep |> Array.toList)
                    ]
                ]
            ]

        Html.div [
            prop.className "swt:flex swt:h-full swt:min-h-0 swt:w-full swt:overflow-hidden"
            if debug then
                prop.testId "tutorial-overlay"
            prop.children [
                Html.div [
                    prop.ref contentRef
                    prop.className "swt:relative swt:min-w-0 swt:flex-1 swt:overflow-hidden"
                    prop.children [
                        // The key remounts the content whenever the effective
                        // checkpoint (or the restart generation) changes, so
                        // the host rebuilds the state this step starts from.
                        React.KeyedFragment(
                            $"""tutorial-content-{generation}-{defaultArg effectiveCheckpoint "initial"}""",
                            [ renderedContent ]
                        )
                        Html.div [
                            prop.className "swt:absolute swt:inset-0 swt:z-40 swt:pointer-events-none"
                            prop.children [ yield! spotlightPanels; stepCard ]
                        ]
                    ]
                ]
                sidebar
            ]
        ]
