namespace Swate.Components.Composite.GraphCanvas

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.JsBindings
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Composite.GraphCanvas.Pills
open Swate.Components.Composite.GraphCanvas.Layout

module private GraphCanvasTypes =

    /// The epoch counts the views the canvas has shown. It rises every time resetKey changes.
    type Navigation = { ViewKey: string; Epoch: int }

    /// The edges that are expanded, and the epoch they were expanded in.
    type Expansion = { ExpandedIn: int; Ids: string[] }

    /// The result of one layout and the epoch it was requested in. No drawn layout means the layout failed,
    /// and Reason holds the message of the error.
    type Outcome = {
        RequestedIn: int
        Drawn: GraphLayout option
        Reason: string
    }

    /// The open pill list. It belongs to one layout and one box width.
    type OpenMenu = {
        MenuEdgeId: string
        MenuLayout: GraphLayout
        MenuBoxWidth: float
        Opener: Element
    }

module private GraphCanvasHelper =

    /// One shared empty array, so that "nothing expanded" keeps its identity between renders.
    let noExpansion: string[] = [||]

    let nodeRadius = 6.0
    let stripeWidth = 4.0
    // Where the 4px stripe meets the rounded corner of the node.
    let stripeCorner =
        nodeRadius - sqrt (nodeRadius ** 2.0 - (nodeRadius - stripeWidth) ** 2.0)

    let minZoom = 0.25
    let maxZoom = 2.0
    let zoomStep = 0.25

    let mutedText = mutedTextColor

    // Tailwind reads class names from the generated code, so these are complete literals. The colors come
    // from custom properties on the root of the component, which are set from accentColor and neutralBorder.
    let interactiveClass =
        "swt:cursor-pointer swt:focus-visible:outline-3 swt:focus-visible:outline-(color:--graph-accent) swt:focus-visible:outline-offset-2"

    /// The custom properties of the root: the palette and the colors of the chrome.
    let rootColors =
        palette
        @ [
            "--graph-accent", accentColor
            "--graph-border", neutralBorder
        ]

    let isActivationKey (key: string) = key = "Enter" || key = " "

    /// Left edge of a node: the rounded corners of the node cut a 4px wide stripe.
    let stripePath (height: float) : string =
        let bottom = height - stripeCorner

        $"M {stripeWidth} {stripeCorner} A {nodeRadius} {nodeRadius} 0 0 0 0 {nodeRadius} V {height - nodeRadius} A {nodeRadius} {nodeRadius} 0 0 0 {stripeWidth} {bottom} Z"

    let nodeBorderColor (color: CanvasColor) : string =
        match color with
        | CanvasColor.Neutral -> neutralBorder
        | other -> colorVar other

    let edgeColor (laid: LaidOutEdge) : string =
        if laid.Edge.Highlighted then
            accentColor
        else
            colorVar laid.Edge.Color

    /// The color a pill draws its border, dot and the dot of its list entry in.
    let pillColor (view: PillView) (highlighted: bool) : string =
        if view.Kind = PillViewKind.Process then
            if highlighted then
                accentColor
            else
                "var(--color-base-content)"
        else
            match view.Category with
            | Some category -> categoryVar category
            | None -> neutralBorder

    let listEntryColor (pill: CanvasPill) : string =
        if pill.Kind = PillKind.Process then
            "var(--color-base-content)"
        else
            match pill.Category with
            | Some category -> categoryVar category
            | None -> neutralBorder

    /// Marker names of the arrow heads, one per edge color and one for highlighted edges.
    let arrowNames = [| "study"; "assay"; "run"; "neutral"; "accent" |]

    let arrowName (laid: LaidOutEdge) : string =
        if laid.Edge.Highlighted then
            "accent"
        else
            match laid.Edge.Color with
            | CanvasColor.Study -> "study"
            | CanvasColor.Assay -> "assay"
            | CanvasColor.Run -> "run"
            | CanvasColor.Neutral -> "neutral"

    let arrowColor (name: string) : string =
        match name with
        | "study" -> colorVar CanvasColor.Study
        | "assay" -> colorVar CanvasColor.Assay
        | "run" -> colorVar CanvasColor.Run
        | "accent" -> accentColor
        | _ -> colorVar CanvasColor.Neutral

    /// Inline style of an HTML element.
    let css (pairs: (string * string) list) =
        prop.style [ for name, value in pairs -> style.custom (name, value) ]

    // SVG elements take their own attribute type, so role, label, test id and style have SVG versions.
    let svgStyle (pairs: (string * string) list) =
        svg.custom ("style", createObj [ for name, value in pairs -> name ==> value ])

    let svgRole (role: string) = svg.custom ("role", role)
    let svgLabel (label: string) = svg.custom ("aria-label", label)
    let svgTestId (id: string) = svg.custom ("data-testid", id)

    let pxOf (value: float) : string = $"{value}px"

    /// What the minimap needs from the DOM. The drawing starts at the origin of the content in the scroll
    /// coordinates of the root, which depends on the margin that centers a small drawing.
    let readGeometry (root: HTMLElement) (content: HTMLElement) (zoom: float) : ScrollGeometry =
        let rootRect = root.getBoundingClientRect ()
        let contentRect = content.getBoundingClientRect ()

        {
            ScrollLeft = root.scrollLeft
            ScrollTop = root.scrollTop
            ClientWidth = root.clientWidth
            ClientHeight = root.clientHeight
            ScrollWidth = root.scrollWidth
            ScrollHeight = root.scrollHeight
            OffsetX = contentRect.left - rootRect.left - root.clientLeft + root.scrollLeft
            OffsetY = contentRect.top - rootRect.top - root.clientTop + root.scrollTop
            Zoom = zoom
        }

    let minimapNode (laid: LaidOutNode) : ReactElement =
        React.KeyedFragment(
            laid.Node.Id,
            [
                Svg.rect [
                    svg.x laid.X
                    svg.y laid.Y
                    svg.width laid.Width
                    svg.height laid.Height
                    svg.rx nodeRadius
                    svgStyle [
                        "fill", nodeBorderColor laid.Node.Color
                        "fillOpacity", "0.55"
                    ]
                ]
            ]
        )

    /// The marker for the visible part. The stroke keeps its width when the minimap scales the drawing down.
    let minimapViewport (visible: Rect) : ReactElement =
        Svg.rect [
            svgTestId "graph-minimap-viewport"
            svg.x visible.Left
            svg.y visible.Top
            svg.width (visible.Right - visible.Left)
            svg.height (visible.Bottom - visible.Top)
            svgStyle [
                "fill", "color-mix(in oklch, var(--color-primary) 15%, transparent)"
                "stroke", accentColor
                "strokeWidth", "1.5"
                "vectorEffect", "non-scaling-stroke"
            ]
        ]

    // A pointer that is not active, such as the synthetic pointer of a test, cannot be captured.
    // The drag then follows the pointer while it stays over the minimap.
    let tryCapturePointer (target: Element) (pointerId: float) : unit =
        try
            target.setPointerCapture pointerId
        with _ ->
            ()

open GraphCanvasTypes
open GraphCanvasHelper

/// Draws nodes, edges and pills of a graph as SVG. The layout comes from ELK and is computed
/// asynchronously. An expanded edge wraps its pills into rows when the box is wide enough, otherwise a
/// list of the hidden pills opens next to its toggle. A portrait box gets the compact top to bottom layout.
/// A graph that does not fit a box of at least 260 by 220 px shows a minimap of the whole drawing, with the
/// visible part marked. Passing imageFileName shows the Download image and Copy image buttons, and the name is
/// the file name of the downloaded image. Copy image appears only where the clipboard can take images, which needs a
/// secure context with navigator.clipboard.write and ClipboardItem. After a view change the canvas keeps drawing the
/// last finished layout, inert, until the new layout resolves. A wait of more than 200 ms shows a loading indicator.
/// A layout with no drawing to keep (the first one, or one after a failed layout) shows its status text after the
/// same delay.
[<Erase; Mangle(false)>]
type GraphCanvas =

    [<ReactComponent>]
    static member private Pill
        (
            placed: PlacedPill,
            ?highlighted: bool,
            ?owner: string,
            ?listOpen: bool,
            ?edgeId: string,
            ?onToggle: Element -> unit
        ) =
        let view = placed.View
        let highlighted = defaultArg highlighted false
        let color = pillColor view highlighted
        let hasDot = view.Category.IsSome

        let valueStyle =
            if view.Kind = PillViewKind.Process then
                [ "fill", "var(--color-base-content)"; "fontWeight", "600" ]
            elif view.Empty then
                [ "fill", mutedText; "fontStyle", "italic" ]
            else
                [ "fill", "var(--color-base-content)" ]

        let content = [
            Svg.title view.Tooltip
            Svg.rect [
                svg.width view.Width
                svg.height pillHeight
                svg.rx (pillHeight / 2.0)
                svgStyle [
                    "fill", "var(--color-base-100)"
                    "stroke", color
                    "strokeWidth", "1"
                ]
            ]
            if hasDot then
                Svg.circle [
                    svg.cx 10.0
                    svg.cy (pillHeight / 2.0)
                    svg.r 3.0
                    svgStyle [ "fill", color ]
                ]
            Svg.text [
                svg.x (if hasDot then 17.0 else 7.0)
                svg.y (pillHeight / 2.0)
                svg.custom ("dominantBaseline", "central")
                svg.custom ("fontSize", 11)
                svg.children [
                    if view.NamePart <> "" then
                        Svg.tspan [ svgStyle [ "fill", mutedText ]; svg.text view.NamePart ]
                    Svg.tspan [ svgStyle valueStyle; svg.text view.ValuePart ]
                ]
            ]
        ]

        let transform = $"translate({placed.X} {placed.Y})"

        match view.Toggle, onToggle with
        | Some toggle, Some onToggle ->
            let activate (event: Event) =
                event.stopPropagation ()
                onToggle (event.currentTarget :?> Element)

            Svg.g [
                svg.custom ("transform", transform)
                svg.className interactiveClass
                svgRole "button"
                svg.tabIndex 0
                svgLabel (
                    match owner with
                    | Some owner -> $"{view.Tooltip} of {owner}"
                    | None -> view.Tooltip
                )
                if listOpen.IsSome then
                    svg.custom ("aria-haspopup", "dialog")
                svg.custom ("aria-expanded", defaultArg listOpen (toggle = ToggleKind.Less))
                match edgeId with
                | Some id -> svgTestId $"graph-edge-toggle-{id}"
                | None -> ()
                svg.onClick (fun (event: MouseEvent) -> activate event)
                svg.onKeyDown (fun (event: KeyboardEvent) ->
                    if isActivationKey event.key then
                        event.preventDefault ()
                        activate event
                )
                svg.children content
            ]
        | _ ->
            Svg.g [
                svg.custom ("transform", transform)
                svgRole "img"
                svgLabel view.Tooltip
                svg.children content
            ]

    [<ReactComponent>]
    static member private NodeShape(laid: LaidOutNode, ?onClick: string -> unit) =
        let node = laid.Node
        let handler = if node.Clickable then onClick else None
        // The pills hold information that a screen reader would otherwise miss. The tooltip is left out,
        // because ariaLabel already carries what it says.
        let label =
            node.AriaLabel :: [ for placed in laid.Pills -> placed.View.FullText ]
            |> String.concat ", "

        let borderStyle =
            if node.Selected then
                [ "stroke", accentColor; "strokeWidth", "3" ]
            elif node.Emphasized then
                [
                    "stroke", "var(--color-base-content)"
                    "strokeWidth", "2"
                ]
            else
                [ "stroke", nodeBorderColor node.Color; "strokeWidth", "1" ]

        Svg.g [
            svg.custom ("transform", $"translate({laid.X} {laid.Y})")
            svgLabel label
            svgTestId $"graph-node-{node.Id}"
            match handler with
            | Some onClick ->
                svg.className interactiveClass

                svgRole "button"
                svg.tabIndex 0
                svg.onClick (fun (_: MouseEvent) -> onClick node.Id)

                svg.onKeyDown (fun (event: KeyboardEvent) ->
                    if isActivationKey event.key then
                        event.preventDefault ()
                        onClick node.Id
                )
            | None -> svgRole "group"
            svg.children [
                Svg.title (defaultArg node.Tooltip node.Title)
                Svg.rect [
                    svg.width laid.Width
                    svg.height laid.Height
                    svg.rx nodeRadius
                    svgStyle [ "fill", "var(--color-base-200)" ]
                ]
                Svg.path [
                    svg.d (stripePath laid.Height)
                    svgStyle [ "fill", nodeBorderColor node.Color ]
                ]
                Svg.rect [
                    svg.width laid.Width
                    svg.height laid.Height
                    svg.rx nodeRadius
                    svg.fill "none"
                    svgStyle borderStyle
                    if node.Dashed then
                        svg.custom ("strokeDasharray", "6 4")
                ]
                Svg.text [
                    svg.x 15.0
                    svg.y laid.TitleY
                    svg.custom ("dominantBaseline", "central")
                    svg.custom ("fontSize", 13)
                    svg.custom ("fontWeight", 600)
                    if node.Mono then
                        svg.custom ("fontFamily", Dom.monoFamily)
                    svgStyle [ "fill", "var(--color-base-content)" ]
                    svg.text laid.TitleText
                ]
                if laid.SubtitleText <> "" then
                    Svg.text [
                        svg.x 15.0
                        svg.y laid.SubtitleY
                        svg.custom ("dominantBaseline", "central")
                        svg.custom ("fontSize", 12)
                        svgStyle [ "fill", mutedText ]
                        svg.text laid.SubtitleText
                    ]
                for placed in laid.Pills do
                    React.KeyedFragment(placed.View.Key, [ GraphCanvas.Pill(placed) ])
            ]
        ]

    /// The list of pills that did not fit on an edge. It sits in the scrolling content, so it moves with its toggle.
    [<ReactComponent>]
    static member private PillMenu
        (
            label: string,
            pills: CanvasPill[],
            boxHeight: float,
            rootRef: IRefValue<HTMLElement option>,
            opener: Element,
            onClose: unit -> unit
        ) =
        let dialogRef = React.useRef<HTMLElement option> None
        let listRef = React.useRef<HTMLElement option> None

        // The list takes the visible area of the box as its limit: it opens on the side of the toggle with
        // more room and scrolls inside itself when it needs more. The width follows the text up to the
        // visible width. boxHeight is a dependency only, so the list fits again when the box changes its height.
        React.useLayoutEffect (
            (fun () ->
                match dialogRef.current, rootRef.current with
                | Some dialog, Some root when not (isNull dialog.parentElement) ->
                    let canvas = dialog.parentElement
                    let rootRect = root.getBoundingClientRect ()
                    let anchor = opener.getBoundingClientRect ()
                    let origin = canvas.getBoundingClientRect ()

                    let rectOf (rect: ClientRect) : Rect = {
                        Left = rect.left
                        Top = rect.top
                        Right = rect.right
                        Bottom = rect.bottom
                    }

                    let geometry: PillMenuGeometry = {
                        Root = rectOf rootRect
                        RootClientLeft = root.clientLeft
                        RootClientTop = root.clientTop
                        RootClientWidth = root.clientWidth
                        RootClientHeight = root.clientHeight
                        Anchor = rectOf anchor
                        Origin = rectOf origin
                    }

                    let placement =
                        pillMenuPlacement
                            geometry
                            (fun maxWidth maxHeight ->
                                Dom.setStyleProperty dialog "max-width" (pxOf maxWidth)
                                Dom.setStyleProperty dialog "max-height" (pxOf maxHeight)
                                dialog.offsetWidth
                            )

                    Dom.setStyleProperty
                        dialog
                        "top"
                        (match placement.MenuTop with
                         | Some top -> pxOf top
                         | None -> "auto")

                    Dom.setStyleProperty
                        dialog
                        "bottom"
                        (match placement.MenuBottom with
                         | Some bottom -> pxOf bottom
                         | None -> "auto")

                    Dom.setStyleProperty dialog "left" (pxOf placement.MenuLeft)
                | _ -> ()
            ),
            [| box rootRef; box opener; box boxHeight |]
        )

        ReactHooks.useLayoutEffectWithCleanup (
            (fun () ->
                let dialog = dialogRef.current
                listRef.current |> Option.iter (fun list -> list.focus ())

                let insideDialog (target: Node) =
                    match dialog with
                    | Some dialog -> dialog.contains target
                    | None -> false

                // The list closes when the focus leaves it, and a tap on a blank spot moves no focus on some touch browsers.
                let close (event: Event) =
                    let target = event.target :?> Node

                    if not (insideDialog target) && not (opener.contains target) then
                        onClose ()

                let escape (event: Event) =
                    if (event :?> KeyboardEvent).key = "Escape" then
                        onClose ()

                Browser.Dom.document.addEventListener ("pointerdown", close, true)
                Browser.Dom.document.addEventListener ("keydown", escape)

                box (fun () ->
                    Browser.Dom.document.removeEventListener ("pointerdown", close, true)
                    Browser.Dom.document.removeEventListener ("keydown", escape)

                    if
                        not (isNull Browser.Dom.document.activeElement)
                        && insideDialog Browser.Dom.document.activeElement
                        && Dom.isConnected opener
                    then
                        (opener :?> HTMLElement).focus ()
                )
            ),
            [| box opener; box onClose |]
        )

        Html.div [
            prop.ref dialogRef
            prop.testId "graph-pill-menu"
            prop.className
                "swt:absolute swt:z-10 swt:w-max swt:overflow-auto swt:rounded-md swt:border swt:border-(color:--graph-border) swt:bg-base-100 swt:py-1 swt:text-xs swt:leading-snug swt:shadow-lg swt:focus:outline-none"
            prop.role.dialog
            prop.ariaLabel label
            prop.tabIndex -1
            prop.onKeyDown (fun (event: KeyboardEvent) ->
                // Tab would leave the canvas or land on its last node, so the list closes and the toggle takes the focus.
                if event.key = "Tab" then
                    event.preventDefault ()
                    onClose ()
            )
            prop.onBlur (fun (event: FocusEvent) ->
                let next = event.relatedTarget

                let focusStaysIn =
                    not (isNull next)
                    && ((match dialogRef.current with
                         | Some dialog -> dialog.contains (next :?> Node)
                         | None -> false)
                        || opener.contains (next :?> Node))

                if not focusStaysIn then
                    onClose ()
            )
            prop.children [
                Html.ul [
                    prop.ref listRef
                    prop.className
                        "swt:m-0 swt:list-none swt:p-0 swt:focus-visible:outline-2 swt:focus-visible:-outline-offset-2 swt:focus-visible:outline-(color:--graph-accent)"
                    prop.tabIndex 0
                    prop.ariaLabel label
                    prop.children [
                        for pill in pills ->
                            Html.li [
                                prop.key pill.Key
                                prop.className
                                    "swt:flex swt:items-baseline swt:gap-2 swt:px-2.5 swt:py-[3px] swt:wrap-anywhere"
                                if pill.Kind = PillKind.Property then
                                    prop.title (describePill pill).Tooltip
                                prop.children [
                                    Html.span [
                                        prop.className "swt:size-2 swt:flex-none swt:rounded-full"
                                        prop.ariaHidden true
                                        css [ "backgroundColor", listEntryColor pill ]
                                    ]
                                    Html.span (pillListText pill)
                                ]
                            ]
                    ]
                ]
            ]
        ]

    /// The whole drawing scaled down, with the visible part marked. Pressing or dragging on it scrolls the root
    /// so that the pointer's point is in the middle. It sits beside the scroll root, so it cannot change the
    /// scroll size. The minimap is a pointer shortcut for scrolling, so it is hidden from assistive technology.
    /// The visible part is state of this component, so scrolling does not render the canvas.
    [<ReactComponent>]
    static member private Minimap
        (
            drawn: GraphLayout,
            zoom: float,
            rootRef: IRefValue<HTMLElement option>,
            contentRef: IRefValue<HTMLElement option>,
            boxWidth: float,
            boxHeight: float,
            stale: bool,
            dimmed: bool
        ) =
        let frameWidth, frameHeight =
            Minimap.frameSize boxWidth boxHeight drawn.LayoutWidth drawn.LayoutHeight

        let visible, setVisible = React.useState<Rect option> None
        let lastVisible = React.useRef<Rect option> None
        let dragging = React.useRef false

        let nodeRects =
            React.useMemo ((fun () -> [ for laid in drawn.LaidOutNodes -> minimapNode laid ]), [| box drawn |])

        // One frame at most reads the DOM, however many scroll events arrive. boxWidth and boxHeight are
        // dependencies only, so the marker follows a resize of the box.
        ReactHooks.useLayoutEffectWithCleanup (
            (fun () ->
                match rootRef.current, contentRef.current with
                | Some root, Some content ->
                    let pending: float option ref = ref None

                    let read () =
                        pending.Value <- None

                        let next =
                            Minimap.viewport (readGeometry root content zoom) drawn.LayoutWidth drawn.LayoutHeight

                        if next <> lastVisible.current then
                            lastVisible.current <- next
                            setVisible next

                    let schedule () =
                        if pending.Value.IsNone then
                            pending.Value <- Some(Browser.Dom.window.requestAnimationFrame (fun _ -> read ()))

                    let onScroll (_: Event) = schedule ()
                    let passive = jsOptions<AddEventListenerOptions> (fun o -> o.passive <- true)

                    root.addEventListener ("scroll", onScroll, passive)
                    schedule ()

                    box (fun () ->
                        root.removeEventListener ("scroll", onScroll)

                        match pending.Value with
                        | Some frame -> Browser.Dom.window.cancelAnimationFrame frame
                        | None -> ()
                    )
                | _ -> box (fun () -> ())
            ),
            [| box drawn; box zoom; box boxWidth; box boxHeight |]
        )

        let centerAt (event: PointerEvent) =
            match rootRef.current, contentRef.current with
            | Some root, Some content ->
                let area = (event.currentTarget :?> Element).getBoundingClientRect ()

                let x, y =
                    Minimap.pointerToLayout
                        boxWidth
                        boxHeight
                        drawn.LayoutWidth
                        drawn.LayoutHeight
                        (event.clientX - area.left)
                        (event.clientY - area.top)

                let left, top = Minimap.centerOn (readGeometry root content zoom) x y
                root.scrollLeft <- left
                root.scrollTop <- top
            | _ -> ()

        let stopDragging (_: PointerEvent) = dragging.current <- false

        Html.div [
            prop.testId "graph-minimap"
            prop.className (
                "swt:absolute swt:right-3 swt:bottom-16 swt:z-20 swt:rounded-lg swt:border swt:border-(color:--graph-border) swt:bg-base-100 swt:p-1 swt:shadow-md"
                + (if stale then " swt:pointer-events-none" else "")
                + (if dimmed then " swt:opacity-60" else "")
            )
            prop.ariaHidden true
            css [ "touchAction", "none"; "cursor", "pointer" ]
            prop.children [
                Svg.svg [
                    svg.xmlns "http://www.w3.org/2000/svg"
                    svg.className "swt:block"
                    svg.width frameWidth
                    svg.height frameHeight
                    svg.custom ("viewBox", $"0 0 {drawn.LayoutWidth} {drawn.LayoutHeight}")
                    svg.custom ("preserveAspectRatio", "xMidYMid meet")
                    svg.onPointerDown (fun (event: PointerEvent) ->
                        if not stale then
                            event.preventDefault ()
                            dragging.current <- true
                            centerAt event
                            tryCapturePointer (event.currentTarget :?> Element) event.pointerId
                    )
                    svg.onPointerMove (fun (event: PointerEvent) ->
                        // A move without the primary button is a hover, also after a release that was never reported.
                        if not stale && dragging.current && (int event.buttons &&& 1) <> 0 then
                            centerAt event
                    )
                    svg.onPointerUp stopDragging
                    svg.onPointerCancel stopDragging
                    svg.onLostPointerCapture stopDragging
                    svg.children (
                        nodeRects
                        @ (
                            match visible with
                            | Some visible -> [ minimapViewport visible ]
                            | None -> []
                        )
                    )
                ]
            ]
        ]

    /// The buttons that save the whole graph as a PNG file or copy it to the clipboard, and the notice about the
    /// result. They render in the zoom group. The state of an export belongs here, so a running export does
    /// not render the canvas. The image shows the whole layout, whatever the zoom and the scroll position.
    [<ReactComponent>]
    static member private ExportControls
        (
            drawn: GraphLayout,
            svgRef: IRefValue<Element option>,
            rootRef: IRefValue<HTMLElement option>,
            fileName: string,
            boxWidth: float,
            stale: bool
        ) =
        let busy, setBusy = React.useState false
        let notice, setNotice = React.useState<string option> None
        let timer = React.useRef None
        let mounted = React.useRef false
        // The click handlers read this, because the state of a render comes too late for a second click in a row.
        let working = React.useRef false

        let clearTimer () =
            match timer.current with
            | Some id ->
                Browser.Dom.window.clearTimeout id
                timer.current <- None
            | None -> ()

        React.useEffect (
            (fun () ->
                mounted.current <- true

                FsReact.createDisposable (fun () ->
                    mounted.current <- false
                    clearTimer ()
                )
            ),
            [||]
        )

        let showNotice (text: string) =
            clearTimer ()
            setNotice (Some text)

            timer.current <-
                Some(
                    Browser.Dom.window.setTimeout (
                        (fun () ->
                            timer.current <- None
                            setNotice None
                        ),
                        3000
                    )
                )

        // Every path of an export ends here, so the buttons never stay busy. The canvas may be gone by then.
        let settle (message: string option) =
            working.current <- false

            if mounted.current then
                setBusy false

                match message with
                | Some text -> showNotice text
                | None -> ()

        // The layout of the latest render. The export reads it after the frame, together with the elements.
        let latest = React.useRef drawn
        React.useLayoutEffect (fun () -> latest.current <- drawn)

        // The heavy work waits for the next frame, so the busy state is painted first. The elements and the layout
        // are read after the wait, because the canvas may have changed in the meantime.
        let render () : JS.Promise<Blob> = promise {
            do! ImageExport.nextFrame ()

            let width = latest.current.LayoutWidth
            let height = latest.current.LayoutHeight

            match svgRef.current, rootRef.current with
            | Some svg, Some root ->
                return! ImageExport.renderPng (ImageExport.svgMarkup svg root width height) width height
            | _ -> return! Promise.reject (exn "The graph is no longer shown.")
        }

        let failToCreate (error: obj) =
            Browser.Dom.console.error ("Could not create the image", error)
            settle (Some "Could not create the image.")

        let download () =
            if not working.current && not stale then
                working.current <- true
                setBusy true

                try
                    (render ())
                        .``then`` (
                            (fun blob ->
                                try
                                    ImageExport.downloadBlob blob fileName
                                    settle None
                                with error ->
                                    failToCreate error
                            ),
                            failToCreate
                        )
                    |> ignore
                with error ->
                    failToCreate error

        // write runs synchronously in the handler, before the PNG exists: the clipboard accepts the click as the
        // user action only then. The PNG promise logs its own error and marks the failure, because write rejects
        // without saying whether the rendering or the clipboard failed.
        let copy () =
            if not working.current && not stale then
                working.current <- true
                setBusy true
                let renderFailed = ref false

                try
                    let png = promise {
                        try
                            return! render ()
                        with error ->
                            Browser.Dom.console.error ("Could not create the image", error)
                            renderFailed.Value <- true
                            return! Promise.reject error
                    }

                    (ImageExport.copyPng png)
                        .``then`` (
                            (fun () -> settle (Some "Image copied.")),
                            (fun error ->
                                if renderFailed.Value then
                                    settle (Some "Could not create the image.")
                                else
                                    Browser.Dom.console.error ("Could not copy the image", error)

                                    settle (
                                        Some(
                                            if Dom.errorName error = "NotAllowedError" then
                                                "Could not copy the image. Click the graph window and try again."
                                            else
                                                "Could not copy the image."
                                        )
                                    )
                            )
                        )
                    |> ignore
                with error ->
                    Browser.Dom.console.error ("Could not copy the image", error)
                    settle (Some "Could not copy the image.")

        // aria-disabled keeps the focus on the button, which disabled would drop. The handlers ignore clicks while busy.
        // A stale drawing belongs to the view that was left, so it is never exported.
        let exportButton (testId: string) (label: string) (icon: string) (onClick: unit -> unit) =
            Html.button [
                prop.type'.button
                prop.testId testId
                prop.key testId
                prop.className (
                    if busy || stale then
                        "swt:btn swt:btn-ghost swt:btn-square swt:btn-sm swt:btn-disabled"
                    else
                        "swt:btn swt:btn-ghost swt:btn-square swt:btn-sm"
                )
                prop.ariaLabel label
                prop.title label
                prop.custom ("aria-disabled", busy || stale)
                prop.onClick (fun _ -> onClick ())
                prop.children [ Html.i [ prop.className icon; prop.ariaHidden true ] ]
            ]

        // Keys and Html.none keep every conditional element in its slot, so no button of the zoom group is remounted.
        // The status element stays in the DOM while it is empty, because screen readers announce a change in a live
        // region but often miss a region that appears with its text already in it.
        //
        // The visible notice is positioned outside the toolbar row, left of the toolbar, so it does not widen the
        // toolbar. The toolbar is about 250 px wide at 12 px from the right edge of the box and the minimap is at most
        // 222 px wide at the same edge, so the notice never covers the minimap. Its width is limited to the room that
        // is left in the box. A box with less room than a short text needs keeps the notice for screen readers only.
        let room = max 0.0 (boxWidth - 300.0)
        let noticeShown = notice.IsSome && room >= 96.0

        let noticeElement =
            Html.span [
                prop.key "notice"
                prop.testId "graph-image-notice"
                prop.role.status
                prop.custom ("aria-live", "polite")
                prop.className (
                    if noticeShown then
                        "swt:absolute swt:right-full swt:bottom-0 swt:mr-2 swt:w-max swt:overflow-hidden swt:rounded-md swt:border swt:border-(color:--graph-border) swt:bg-base-100 swt:px-2 swt:py-1 swt:text-xs swt:whitespace-normal swt:shadow-md"
                    else
                        "swt:sr-only"
                )
                css [ "maxWidth", (if noticeShown then pxOf room else "none") ]
                prop.text (defaultArg notice "")
            ]

        let copyButton =
            if ImageExport.canCopy () then
                exportButton
                    "graph-image-copy"
                    "Copy image"
                    "swt:iconify swt:fluent--image-copy-24-regular swt:size-5"
                    copy
            else
                Html.none

        React.Fragment [
            noticeElement
            Html.span [
                prop.key "divider"
                prop.className "swt:mx-0.5 swt:h-5 swt:w-px swt:bg-base-300"
                prop.ariaHidden true
            ]
            exportButton
                "graph-image-download"
                "Download image"
                "swt:iconify swt:fluent--arrow-download-24-regular swt:size-5"
                download
            copyButton
        ]

    [<ReactComponent>]
    static member GraphCanvas
        (
            nodes: CanvasNode[],
            edges: CanvasEdge[],
            ariaLabel: string,
            resetKey: string,
            ?onNodeClick: string -> unit,
            ?centerNodeId: string,
            ?focusOnMount: bool,
            ?imageFileName: string,
            ?layout: CanvasNode[] -> CanvasEdge[] -> string[] -> MeasureText -> LayoutOptions -> JS.Promise<GraphLayout>
        ) =
        let rootRef = React.useRef<HTMLElement option> None
        let contentRef = React.useRef<HTMLElement option> None
        let svgRef = React.useRef<Element option> None
        let zoom, setZoom = React.useState 1.0
        // True when the graph does not fit the box. The minimap and its toggle only appear then.
        let overflowing, setOverflowing = React.useState false
        let minimapOpen, setMinimapOpen = React.useState true
        let scrolledIn = React.useRef<int option> None
        let markerPrefix = (React.useId ()).Replace(":", "")

        // The layout function is read through a ref, so a new function identity per render never starts a layout.
        let layoutRef = React.useRef (defaultArg layout layoutGraph)

        React.useLayoutEffect (fun () -> layoutRef.current <- defaultArg layout layoutGraph)

        // A changed resetKey is a new view. React lets a component reset state while it renders, so the new
        // epoch is known before anything is drawn. Results, expansion and the scroll position are tied to an
        // epoch, so a result that arrives late for an earlier view is never drawn, and returning to an earlier
        // resetKey starts like a new view.
        let navigation, setNavigation = React.useState { ViewKey = resetKey; Epoch = 0 }

        let epoch =
            if navigation.ViewKey = resetKey then
                navigation.Epoch
            else
                navigation.Epoch + 1

        if navigation.ViewKey <> resetKey then
            setNavigation { ViewKey = resetKey; Epoch = epoch }

        let expansion, setExpansion =
            React.useStateWithUpdater { ExpandedIn = 0; Ids = noExpansion }

        let expanded =
            if expansion.ExpandedIn = epoch then
                expansion.Ids
            else
                noExpansion

        let toggle =
            React.useCallback (
                (fun (edgeId: string) ->
                    setExpansion (fun current ->
                        let ids =
                            if current.ExpandedIn = epoch then
                                current.Ids
                            else
                                noExpansion

                        let next =
                            if Array.contains edgeId ids then
                                ids |> Array.filter (fun id -> id <> edgeId)
                            else
                                Array.append ids [| edgeId |]

                        { ExpandedIn = epoch; Ids = next }
                    )
                ),
                [| box epoch |]
            )

        // Size of the box the canvas is shown in, None until it is measured. The shape comes from the border
        // box, which a scrollbar does not change. A scrollbar takes 15px off the client size, which near a
        // square box would flip the layout mode back and forth. The client size serves for the widths and heights.
        let measuredBox, setMeasuredBox = React.useStateWithUpdater<MeasuredBox option> None

        ReactHooks.useLayoutEffectWithCleanup (
            (fun () ->
                match rootRef.current with
                | None -> box (fun () -> ())
                | Some root ->
                    let measureBox () =
                        setMeasuredBox (fun current ->
                            let next: MeasuredBox = {
                                ClientWidth = root.clientWidth
                                ClientHeight = root.clientHeight
                                Portrait = isPortrait root.clientWidth root.offsetHeight root.offsetWidth
                            }

                            match current with
                            | Some current when sameBox current next -> Some current
                            | _ -> Some next
                        )

                    measureBox ()

                    if Dom.ResizeObserver.isAvailable then
                        let observer = Dom.ResizeObserver(fun _ -> measureBox ())
                        observer.observe root
                        box (fun () -> observer.disconnect ())
                    else
                        box (fun () -> ())
            ),
            [||]
        )

        let measured = measuredBox.IsSome

        let boxWidth =
            match measuredBox with
            | Some size -> size.ClientWidth
            | None -> 0.0

        let boxHeight =
            match measuredBox with
            | Some size -> size.ClientHeight
            | None -> 0.0

        let portrait =
            match measuredBox with
            | Some size -> size.Portrait
            | None -> false

        // The width shapes the layout only through the rows of an expanded edge. A portrait box takes the
        // compact layout, which has no expanded edges and does not depend on the width.
        let layoutWidth = effectiveLayoutWidth portrait expanded.Length boxWidth

        // The previous drawing stays until the next layout resolves. Inside a view it stays active. After a view
        // change it stays as an inert drawing until the new layout resolves. A layout with no drawing to keep shows
        // the status text instead.
        let outcome, setOutcome = React.useState<Outcome option> None

        React.useEffect (
            (fun () ->
                if not measured then
                    FsReact.createDisposable (fun () -> ())
                else
                    let current = ref true

                    let family =
                        match rootRef.current with
                        | Some root -> Dom.computedFontFamily root
                        | None -> ""

                    let measure = Dom.createTextMeasure (if family = "" then "sans-serif" else family)

                    let run =
                        try
                            layoutRef.current nodes edges expanded measure {
                                Width = layoutWidth
                                Portrait = portrait
                            }
                        with error ->
                            Promise.reject error

                    run.``then`` (
                        (fun drawn ->
                            if current.Value then
                                setOutcome (
                                    Some {
                                        RequestedIn = epoch
                                        Drawn = Some drawn
                                        Reason = ""
                                    }
                                )
                        ),
                        (fun error ->
                            Browser.Dom.console.error ("Could not lay out the graph canvas", error)

                            if current.Value then
                                setOutcome (
                                    Some {
                                        RequestedIn = epoch
                                        Drawn = None
                                        Reason = Errors.messageOf error
                                    }
                                )
                        )
                    )
                    |> ignore

                    FsReact.createDisposable (fun () -> current.Value <- false)
            ),
            [|
                box nodes
                box edges
                box expanded
                box epoch
                box measured
                box layoutWidth
                box portrait
            |]
        )

        let shown = outcome |> Option.filter (fun result -> result.RequestedIn = epoch)
        let drawn = shown |> Option.bind (fun result -> result.Drawn)
        let failed = shown.IsSome && drawn.IsNone

        // The finished drawing of an earlier view, kept until the current view has an outcome. A failed earlier
        // layout has no drawing to keep.
        let stale =
            match outcome with
            | Some result when result.RequestedIn <> epoch -> result.Drawn
            | _ -> None

        let displayed =
            match drawn with
            | Some _ -> drawn
            | None when not failed -> stale
            | None -> None

        let isStale = drawn.IsNone && displayed.IsSome
        let waiting = drawn.IsNone && not failed

        // The indicator for a slow layout appears after 200 ms of waiting, so a quick layout shows nothing.
        let slow, setSlow = React.useState false

        React.useEffect (
            (fun () ->
                if waiting then
                    let timer = Browser.Dom.window.setTimeout ((fun () -> setSlow true), 200)
                    FsReact.createDisposable (fun () -> Browser.Dom.window.clearTimeout timer)
                else
                    setSlow false
                    FsReact.createDisposable (fun () -> ())
            ),
            [| box waiting; box epoch |]
        )

        // A stale drawing dims and the loading indicator shows only once the wait has passed 200 ms.
        let dimmed = isStale && slow

        let titles =
            match displayed with
            | Some drawn ->
                drawn.LaidOutNodes
                |> Array.fold (fun titles laid -> titles |> Map.add laid.Node.Id laid.Node.Title) Map.empty
            | None -> Map.empty

        let titleOf (id: string) =
            titles |> Map.tryFind id |> Option.defaultValue id

        let ownerOf (laid: LaidOutEdge) =
            $"{titleOf laid.Edge.From} to {titleOf laid.Edge.To}"

        // One list at a time. It belongs to the layout and the box width it was opened on, so a new layout
        // (an expansion, other data, a resize that relays out) or a new width closes it. The state is cleared
        // then, so the list cannot reappear when the box returns to the old width.
        let menu, setMenu = React.useState<OpenMenu option> None

        let openMenu =
            match menu, drawn with
            | Some open', Some drawn when
                LanguagePrimitives.PhysicalEquality open'.MenuLayout drawn
                && open'.MenuBoxWidth = boxWidth
                ->
                menu
            | _ -> None

        if menu.IsSome && openMenu.IsNone then
            setMenu None

        let closeMenu = React.useCallback ((fun () -> setMenu None), [||])

        let menuEdge =
            match openMenu, drawn with
            | Some open', Some drawn ->
                drawn.LaidOutEdges
                |> Array.tryFind (fun laid -> laid.Edge.Id = open'.MenuEdgeId)
            | _ -> None

        // After a navigation the container takes focus. The node that was activated stays in the DOM as an inert node
        // of the stale drawing, and the browser moves the focus off it. The first mount leaves focus alone unless
        // focusOnMount is set. The draw effect below decides whether the focus may move.
        let lastEpoch = React.useRef epoch
        let focusOnDraw = React.useRef (defaultArg focusOnMount false)

        React.useLayoutEffect (
            (fun () ->
                if lastEpoch.current <> epoch then
                    lastEpoch.current <- epoch
                    focusOnDraw.current <- true
            ),
            [| box epoch |]
        )

        // The focus may move to the container when nothing holds it: it sits on the body, where an inert or removed
        // node leaves it, or inside the scroll root, which holds the drawing and the pill list. A control that
        // survives the navigation keeps the focus. These are the toolbar controls of the canvas, such as zoom, and
        // controls outside the canvas, such as an up button of the page, also when the canvas mounts anew with
        // focusOnMount set.
        let focusMayMove () : bool =
            let active = Browser.Dom.document.activeElement

            let insideRoot =
                match rootRef.current with
                | Some root -> root.contains (active :> Node)
                | None -> false

            isNull (box active)
            || obj.ReferenceEquals(active, Browser.Dom.document.body)
            || insideRoot

        // A new view starts at the top left, or on centerNodeId. Expanding an edge keeps the scroll position.
        // A failed layout takes the focus too, because the container then shows the alert.
        React.useLayoutEffect (
            (fun () ->
                match rootRef.current with
                | Some root when (drawn.IsSome || failed) && scrolledIn.current <> Some epoch ->
                    if focusOnDraw.current then
                        focusOnDraw.current <- false

                        if focusMayMove () then
                            Dom.focusWithoutScroll root

                    match drawn with
                    | None -> ()
                    | Some drawn ->
                        scrolledIn.current <- Some epoch
                        root.scrollLeft <- 0.0
                        root.scrollTop <- 0.0

                        let target =
                            centerNodeId
                            |> Option.bind (fun id ->
                                drawn.LaidOutNodes |> Array.tryFind (fun laid -> laid.Node.Id = id)
                            )

                        match target with
                        | None -> ()
                        | Some target ->
                            let clamp (value: float) (limit: float) = max 0.0 (min value limit)

                            // The compact layout keeps its main flow on the left, so it only scrolls down to the node.
                            if not drawn.Compact then
                                root.scrollLeft <-
                                    clamp
                                        ((target.X + target.Width / 2.0) * zoom - root.clientWidth / 2.0)
                                        (root.scrollWidth - root.clientWidth)

                            root.scrollTop <-
                                clamp
                                    ((target.Y + target.Height / 2.0) * zoom - root.clientHeight / 2.0)
                                    (root.scrollHeight - root.clientHeight)
                | _ -> ()
            ),
            [|
                box drawn
                box failed
                box epoch
                box centerNodeId
                box zoom
            |]
        )

        React.useLayoutEffect (
            (fun () ->
                let next =
                    match displayed, rootRef.current, contentRef.current with
                    | Some _, Some root, Some content -> Minimap.overflows (readGeometry root content zoom)
                    | _ -> false

                if next <> overflowing then
                    setOverflowing next
            ),
            [| box displayed; box zoom; box boxWidth; box boxHeight |]
        )

        let edgeGroup (drawn: GraphLayout) (laid: LaidOutEdge) : ReactElement =
            let owner = ownerOf laid
            // The list takes over the "+N" toggle when the expanded edge would not fit the box, and in compact mode.
            let listed = drawn.Compact || not (fitsInline laid.MinInlineWidth boxWidth)

            // A count pill is a bare number, so its tooltip says what is counted. Property pills keep the full
            // text, because their tooltip only adds the category.
            let texts = [
                for placed in laid.EdgePills do
                    if placed.View.Kind = PillViewKind.Property then
                        placed.View.FullText
                    elif placed.View.Kind <> PillViewKind.Toggle then
                        placed.View.Tooltip
            ]

            let spoken = String.concat ", " texts

            let menuOpenOnEdge =
                match openMenu with
                | Some open' -> open'.MenuEdgeId = laid.Edge.Id
                | None -> false

            let pill (placed: PlacedPill) =
                let onToggle (anchor: Element) =
                    if not listed || placed.View.Toggle = Some ToggleKind.Less then
                        toggle laid.Edge.Id
                    elif menuOpenOnEdge then
                        closeMenu ()
                    else
                        setMenu (
                            Some {
                                MenuEdgeId = laid.Edge.Id
                                MenuLayout = drawn
                                MenuBoxWidth = boxWidth
                                Opener = anchor
                            }
                        )

                let listOpen =
                    if listed && placed.View.Toggle = Some ToggleKind.More then
                        Some menuOpenOnEdge
                    else
                        None

                React.KeyedFragment(
                    placed.View.Key,
                    [
                        GraphCanvas.Pill(
                            placed,
                            highlighted = laid.Edge.Highlighted,
                            owner = owner,
                            ?listOpen = listOpen,
                            edgeId = laid.Edge.Id,
                            ?onToggle = (if isStale then None else Some onToggle)
                        )
                    ]
                )

            Svg.g [
                svgRole "group"
                svgTestId $"graph-edge-{laid.Edge.Id}"
                svgLabel (
                    if texts.IsEmpty then
                        $"From {owner}"
                    else
                        $"From {owner}: {spoken}"
                )
                svg.children [
                    if laid.Path <> "" then
                        Svg.path [
                            svg.d laid.Path
                            svg.fill "none"
                            svgStyle [
                                "stroke", edgeColor laid
                                "strokeWidth", (if laid.Edge.Highlighted then "2.5" else "1.5")
                            ]
                            svg.custom ("markerEnd", $"url(#{markerPrefix}-{arrowName laid})")
                            svg.children [
                                match laid.Edge.Tooltip with
                                | Some tooltip -> Svg.title tooltip
                                | None -> ()
                            ]
                        ]
                    for placed in laid.EdgePills do
                        pill placed
                ]
            ]

        let canvas (drawn: GraphLayout) =
            Html.div [
                prop.ref contentRef
                prop.testId "graph-canvas-content"
                prop.custom ("data-compact", drawn.Compact)
                prop.custom ("data-stale", isStale)
                // A stale drawing is inert, so it takes no input. It dims when the wait for the new layout gets long.
                if isStale then
                    prop.custom ("inert", true)
                prop.className (
                    (if drawn.Compact then
                         "swt:relative swt:flex-none swt:mt-auto swt:mr-auto swt:mb-auto swt:ml-0"
                     else
                         "swt:relative swt:flex-none swt:m-auto")
                    + (if dimmed then
                           " swt:transition-opacity swt:opacity-60"
                       else
                           " swt:transition-opacity")
                )
                prop.children [
                    Svg.svg [
                        svg.custom ("ref", svgRef)
                        svg.xmlns "http://www.w3.org/2000/svg"
                        svg.className "swt:block swt:max-w-none"
                        svg.width (drawn.LayoutWidth * zoom)
                        svg.height (drawn.LayoutHeight * zoom)
                        svg.custom ("viewBox", $"0 0 {drawn.LayoutWidth} {drawn.LayoutHeight}")
                        svgRole "group"
                        svg.children [
                            Svg.defs [
                                for name in arrowNames do
                                    Svg.marker [
                                        svg.id $"{markerPrefix}-{name}"
                                        svg.markerWidth 8
                                        svg.markerHeight 8
                                        svg.custom ("refX", 7)
                                        svg.custom ("refY", 4)
                                        svg.custom ("orient", "auto")
                                        svg.children [
                                            Svg.path [
                                                svg.d "M0,0 L8,4 L0,8 z"
                                                svgStyle [ "fill", arrowColor name ]
                                            ]
                                        ]
                                    ]
                            ]
                            for laid in drawn.LaidOutEdges do
                                React.KeyedFragment(laid.Edge.Id, [ edgeGroup drawn laid ])
                            for laid in drawn.LaidOutNodes do
                                React.KeyedFragment(
                                    laid.Node.Id,
                                    [
                                        GraphCanvas.NodeShape(laid, ?onClick = (if isStale then None else onNodeClick))
                                    ]
                                )
                        ]
                    ]
                    match openMenu, menuEdge with
                    | Some open', Some edge ->
                        GraphCanvas.PillMenu(
                            (if drawn.Compact then "Details" else "More properties") + " of " + ownerOf edge,
                            (if drawn.Compact then
                                 Array.append edge.Edge.Pills edge.Edge.MorePills
                             else
                                 edge.Edge.MorePills),
                            boxHeight,
                            rootRef,
                            open'.Opener,
                            closeMenu
                        )
                    | _ -> ()
                ]
            ]

        let zoomButton (testId: string) (label: string) (glyph: string) (disabled: bool) (next: float) =
            Html.button [
                prop.type'.button
                prop.testId testId
                prop.className "swt:btn swt:btn-ghost swt:btn-square swt:btn-sm swt:text-xl"
                prop.ariaLabel label
                prop.title label
                prop.disabled disabled
                prop.onClick (fun _ ->
                    closeMenu ()
                    setZoom next
                )
                prop.text glyph
            ]

        // A minimap in a small box would hide too much of the graph.
        let minimapAvailable = overflowing && Minimap.fitsBox boxWidth boxHeight

        let minimapToggle () =
            Html.button [
                prop.type'.button
                prop.testId "graph-minimap-toggle"
                prop.className "swt:btn swt:btn-ghost swt:btn-square swt:btn-sm"
                prop.ariaPressed minimapOpen
                prop.ariaLabel "Minimap"
                prop.title "Minimap"
                prop.onClick (fun _ -> setMinimapOpen (not minimapOpen))
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--map-24-regular swt:size-5"
                        prop.ariaHidden true
                    ]
                ]
            ]

        let loadingClass =
            if dimmed then
                "swt:pointer-events-none swt:absolute swt:top-3 swt:right-3 swt:z-20 swt:flex swt:items-center swt:gap-2 swt:rounded-lg swt:border swt:border-(color:--graph-border) swt:bg-base-100 swt:px-2 swt:py-1 swt:text-xs swt:shadow-md"
            else
                "swt:sr-only"

        let loadingContent =
            if dimmed then
                [
                    Html.span [
                        prop.className
                            "swt:inline-block swt:size-3 swt:animate-spin swt:rounded-full swt:border-2 swt:border-current swt:border-t-transparent"
                        prop.ariaHidden true
                    ]
                    Html.span "Laying out…"
                ]
            else
                []

        Html.div [
            prop.className "swt:relative swt:size-full swt:min-h-0"
            prop.style [
                for name, value in rootColors -> style.custom (name, value)
            ]
            prop.children [
                Html.div [
                    prop.ref rootRef
                    prop.testId "graph-canvas"
                    prop.custom ("aria-busy", isStale)
                    prop.className
                        "swt:relative swt:box-border swt:flex swt:size-full swt:overflow-auto swt:overscroll-auto swt:[scrollbar-gutter:stable] swt:text-base-content swt:focus:outline-none"
                    prop.role.region
                    prop.ariaLabel ariaLabel
                    prop.tabIndex -1
                    prop.children [
                        match displayed with
                        | Some displayed -> canvas displayed
                        | None when shown.IsSome ->
                            Html.p [
                                prop.testId "graph-status"
                                prop.className "swt:m-0 swt:px-3.5 swt:py-4 swt:text-base-content"
                                prop.role.alert
                                prop.children [
                                    Html.span "Could not lay out this graph."
                                    // The reason is the message of the error, so the user can report it.
                                    Html.span [
                                        prop.testId "graph-status-reason"
                                        prop.className "swt:ml-1 swt:break-words"
                                        prop.text (
                                            shown |> Option.map (fun result -> result.Reason) |> Option.defaultValue ""
                                        )
                                    ]
                                ]
                            ]
                        | None ->
                            Html.p [
                                prop.testId "graph-status"
                                prop.className "swt:m-0 swt:px-3.5 swt:py-4 swt:text-base-content/80"
                                prop.role.status
                                prop.text (if slow then "Laying out…" else "")
                            ]
                    ]
                ]
                // Only a stale drawing needs this indicator. A layout with no drawing to keep shows its text in the
                // status paragraph. The region stays mounted while a drawing is displayed, so screen readers
                // announce the text when it appears. The spinner is a CSS rotation, which the compositor keeps
                // moving while the main thread is busy with a layout.
                if displayed.IsSome then
                    Html.div [
                        prop.testId "graph-loading"
                        prop.role.status
                        prop.custom ("data-shown", dimmed)
                        prop.className loadingClass
                        prop.children loadingContent
                    ]
                else
                    Html.none
                if displayed.IsSome then
                    Html.div [
                        prop.className
                            "swt:absolute swt:right-3 swt:bottom-3 swt:z-20 swt:flex swt:items-center swt:gap-1.5 swt:rounded-lg swt:border swt:border-(color:--graph-border) swt:bg-base-100 swt:p-1 swt:text-xs swt:tabular-nums swt:shadow-md"
                        prop.role.group
                        prop.ariaLabel "Graph view controls"
                        prop.children [
                            // The toggle keeps its slot while it is absent, so the buttons after it are not remounted and keep their focus.
                            if minimapAvailable then minimapToggle () else Html.none
                            zoomButton "graph-zoom-out" "Zoom out" "−" (zoom <= minZoom) (max minZoom (zoom - zoomStep))
                            Html.span [
                                prop.className "swt:min-w-10 swt:text-center"
                                prop.custom ("aria-live", "polite")
                                prop.custom ("aria-atomic", "true")
                                prop.text (string (floor (zoom * 100.0 + 0.5)) + "%")
                            ]
                            zoomButton "graph-zoom-in" "Zoom in" "+" (zoom >= maxZoom) (min maxZoom (zoom + zoomStep))
                            match imageFileName, displayed with
                            | Some fileName, Some displayed ->
                                GraphCanvas.ExportControls(displayed, svgRef, rootRef, fileName, boxWidth, isStale)
                            | _ -> Html.none
                        ]
                    ]
                else
                    Html.none
                match displayed with
                | Some displayed when minimapAvailable && minimapOpen && openMenu.IsNone ->
                    GraphCanvas.Minimap(displayed, zoom, rootRef, contentRef, boxWidth, boxHeight, isStale, dimmed)
                | _ -> Html.none
            ]
        ]
