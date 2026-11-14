namespace Swate.Components.Composite.Layout

open Feliz
open Fable.Core
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.Navbar
open Swate.Components.Primitive.Banner
open Swate.Components.Primitive.Banner.Types

module private Mocks =

    [<StringEnum>]
    type RightSidebarTargetMock =
        | Home
        | Settings
        | Info

open Swate.Components.Composite.Layout.LeftSidebarContext
open Swate.Components.Composite.Layout.RightSidebarContext

module private LayoutHelper =

    module Sidebar =
        [<Literal>]
        let DefaultWidth = 300

        [<Literal>]
        let MinWidth = 200

        /// If it goes below this width, auto toggle the sidebar
        [<Literal>]
        let ToggleWidth = 75

        [<RequireQualifiedAccess>]
        type Side =
            | Left
            | Right

    [<Literal>]
    let MainContentMinWidth = 300

    let clamp (min: int, max: int, value: int) : int =
        if value < min then min
        elif value > max then max
        else value

    type ResizeState =
        | CloseTargetSidebar
        | ResizeTarget of int
        | ResizeTargetCollapseOther of int
        | ResizeBoth of int * int

    let calcResize (newWidth: int) (windowX: int) (otherWidth: int) =

        if newWidth < Sidebar.ToggleWidth then // collapse resizing sidebar
            CloseTargetSidebar
        elif newWidth < Sidebar.MinWidth then // resize to min width
            ResizeTarget Sidebar.MinWidth
        else // other sidebar is open and may be resized
            let mainContent = windowX - newWidth - otherWidth

            if mainContent >= MainContentMinWidth then // enough main content space, just resize
                ResizeTarget newWidth
            else // must change other sidebar
                let deficit = MainContentMinWidth - mainContent // how much to take from other sidebar
                let newRightWidth = otherWidth - deficit

                if newRightWidth < Sidebar.MinWidth then // collapse other sidebar if below min
                    let maxWidth = windowX - MainContentMinWidth
                    let clampedWidth = clamp (Sidebar.MinWidth, maxWidth, newWidth)

                    ResizeTargetCollapseOther clampedWidth
                else // resize both
                    ResizeBoth(newWidth, newRightWidth)



open LayoutHelper
open Fable.Core.JsInterop

[<Erase; Mangle(false)>]
type Layout =

    /// A square icon button for the action columns beside the sidebars. The optional indicator is a short label
    /// in a badge on the top right corner of the button, for a feature that is new, for example.
    [<ReactComponent>]
    static member LayoutBtn
        (
            iconClassName: string,
            tooltip: string,
            onClick: unit -> unit,
            ?tooltipClassName: string,
            ?isActive: bool,
            ?indicator: string
        ) =
        let isActive = defaultArg isActive false

        let button =
            Html.button [
                prop.ariaLabel tooltip
                prop.className "swt:btn swt:btn-square swt:btn-ghost swt:btn-sm"
                prop.children [
                    Html.i [
                        prop.className ("swt:size-4 swt:iconify " + iconClassName)
                    ]
                ]
                prop.onClick (fun _ -> onClick ())
            ]

        Html.div [
            prop.className [
                "swt:tooltip swt:border-r-2"
                tooltipClassName |> Option.defaultValue "swt:tooltip-right"
                if isActive then
                    "swt:border-primary"
                else
                    "swt:border-transparent"
            ]
            prop.ariaLabel tooltip
            prop.children [
                Html.div [ prop.className "swt:tooltip-content"; prop.text tooltip ]
                match indicator with
                | Some label ->
                    // The badge sits inside the right edge of the button, so the narrow column does not grow.
                    Html.div [
                        prop.className "swt:indicator"
                        prop.children [
                            Html.span [
                                prop.testId "layout-btn-indicator"
                                prop.className
                                    "swt:indicator-item swt:badge swt:badge-primary swt:badge-xs swt:px-1 swt:[--indicator-x:0px]"
                                prop.text label
                            ]
                            button
                        ]
                    ]
                | None -> button
            ]
        ]

    [<ReactComponent>]
    static member LeftSidebarToggleBtn() =
        let ctx = useLeftSidebarCtx ()

        Layout.LayoutBtn(
            iconClassName =
                (if ctx.state then
                     "swt:fluent--panel-left-48-filled"
                 else
                     "swt:fluent--panel-left-48-regular"),
            tooltip = "Toggle left sidebar",
            tooltipClassName = "swt:tooltip-left",
            onClick = (fun () -> ctx.setState (not ctx.state))
        )

    [<ReactComponent>]
    static member RightSidebarToggleBtn() =
        let ctx = useRightSidebarCtx ()

        Layout.LayoutBtn(
            iconClassName =
                (if ctx.isOpen then
                     "swt:fluent--panel-right-48-filled"
                 else
                     "swt:fluent--panel-right-48-regular"),
            tooltip = "Toggle right sidebar",
            tooltipClassName = "swt:tooltip-left",
            onClick = fun () -> ctx.setIsOpen (not ctx.isOpen)
        )

    [<ReactComponent>]
    static member private ResizeHandler(setPosition: float option -> unit, side: Sidebar.Side) =
        let dragging = React.useRef false

        React.useEffectOnce (fun () ->

            let onMove =
                fun (e: Browser.Types.PointerEvent) ->
                    if dragging.current then
                        Some e.clientX |> setPosition
                    else
                        ()

            let rec stop =
                fun () ->
                    dragging.current <- false
                    setPosition None
                    Browser.Dom.document.removeEventListener ("pointermove", unbox onMove)
                    Browser.Dom.document.removeEventListener ("pointerup", unbox stop)

            Browser.Dom.document.addEventListener ("pointermove", unbox onMove)
            Browser.Dom.document.addEventListener ("pointerup", unbox stop)

            FsReact.createDisposable (fun () ->
                Browser.Dom.document.removeEventListener ("pointermove", unbox onMove)
                Browser.Dom.document.removeEventListener ("pointerup", unbox stop)
            )
        )

        Html.div [
            prop.onPointerDown (fun _ -> dragging.current <- true)
            prop.style [
                match side with
                | Sidebar.Side.Left -> style.right -1
                | Sidebar.Side.Right -> style.left -1
            ]
            prop.className
                "swt:w-1 swt:cursor-col-resize swt:hover:bg-primary swt:absolute swt:top-0 swt:bottom-0 swt:transition-colors swt:select-none"
        ]

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    static member private SidebarActions(children: ReactElement, side: Sidebar.Side, ?key: string) =
        Html.div [
            prop.testId (
                match side with
                | Sidebar.Side.Left -> "layout-main-left-navbar"
                | Sidebar.Side.Right -> "layout-main-right-navbar"
            )
            prop.className [
                "swt:flex swt:shrink-0 swt:flex-col swt:gap-2 swt:p-1 swt:border-base-content/50"
                match side with
                | Sidebar.Side.Left -> "swt:border-r"
                | Sidebar.Side.Right -> "swt:border-l"
            ]
            prop.children children
        ]

    [<ReactMemoComponent>]
    static member private SidebarArea
        (
            children: ReactElement,
            width: int,
            isOpen,
            ref: IRefValue<option<Browser.Types.HTMLElement>>,
            setPointerPosition,
            side: Sidebar.Side
        ) =
        Html.div [
            prop.ref ref
            prop.testId (
                match side with
                | Sidebar.Side.Left -> "layout-main-left-sidebar"
                | Sidebar.Side.Right -> "layout-main-right-sidebar"
            )
            prop.style [
                if not isOpen then style.width 0 else style.width width
            ]
            prop.className [
                "swt:flex swt:shrink-0 swt:flex-row swt:h-full swt:relative swt:overflow-hidden"
                if isOpen then
                    "swt:border-base-content/50 swt:bg-base-100"

                    match side with
                    | Sidebar.Side.Left -> "swt:border-r"
                    | Sidebar.Side.Right -> "swt:border-l"
            ]
            prop.children [
                Html.div [
                    prop.className [
                        "swt:w-full swt:h-full swt:overflow-x-hidden swt:overflow-y-auto"
                        "swt:scrollbar-fade"
                    ]
                    prop.children children
                ]
                if isOpen then
                    Layout.ResizeHandler(setPointerPosition, side)
            ]
        ]

    [<ReactMemoComponent>]
    static member private Sidebars
        (
            children: ReactElement,
            ?leftContent: ReactElement,
            ?leftActions: ReactElement,
            ?rightContent: ReactElement,
            ?rightActions: ReactElement
        ) =
        let ctxLeft = useLeftSidebarCtx ()
        let ctxRight = useRightSidebarCtx ()

        let widthLeft, setWidthLeft =
            React.useLocalStorage (
                Keys.mkLocalStorageKey "layout" "main" "leftSidebarWidth",
                LayoutHelper.Sidebar.DefaultWidth
            )

        let widthRight, setWidthRight =
            React.useLocalStorage (
                Keys.mkLocalStorageKey "layout" "main" "rightSidebarWidth",
                LayoutHelper.Sidebar.DefaultWidth
            )

        let StartResizeState = React.useRef (None: {| isOpen: bool; width: int |} option)

        let leftPointerPosition, setLeftPointerPosition =
            React.useState (None: float option)

        let setLeftPointerPositionWrapper =
            function
            | Some pos ->
                if StartResizeState.current.IsNone then
                    StartResizeState.current <-
                        Some {|
                            isOpen = ctxRight.isOpen
                            width = widthRight
                        |}

                setLeftPointerPosition (Some pos)
            | None ->
                StartResizeState.current <- None
                setLeftPointerPosition None


        let throttledLeftPointerPosition = React.useThrottle (leftPointerPosition, 16)

        let rightPointerPosition, setRightPointerPosition =
            React.useState (None: float option)

        let setRightPointerPositionWrapper =
            function
            | Some pos ->
                if StartResizeState.current.IsNone then
                    StartResizeState.current <-
                        Some {|
                            isOpen = ctxLeft.state
                            width = widthLeft
                        |}

                setRightPointerPosition (Some pos)
            | None ->
                StartResizeState.current <- None
                setRightPointerPosition None

        let throttledRightPointerPosition = React.useThrottle (rightPointerPosition, 16)

        let leftRef = React.useElementRef ()
        let rightRef = React.useElementRef ()

        React.useEffect (
            (fun () ->
                if
                    throttledLeftPointerPosition.IsSome
                    && StartResizeState.current.IsSome
                    && leftRef.current.IsSome
                then
                    let windowX = int Browser.Dom.window.innerWidth

                    let newWidth =
                        throttledLeftPointerPosition.Value - leftRef.current.Value.offsetLeft |> int

                    match calcResize newWidth windowX widthRight with
                    | CloseTargetSidebar ->
                        ctxRight.setIsOpen StartResizeState.current.Value.isOpen
                        ctxLeft.setState false
                    | ResizeTarget width ->
                        ctxRight.setIsOpen StartResizeState.current.Value.isOpen
                        ctxLeft.setState true
                        setWidthLeft width
                    | ResizeBoth(leftWidth, rightWidth) ->
                        ctxRight.setIsOpen StartResizeState.current.Value.isOpen
                        setWidthLeft leftWidth
                        setWidthRight rightWidth
                    | ResizeTargetCollapseOther width ->
                        ctxLeft.setState true
                        setWidthLeft width
                        ctxRight.setIsOpen false
            ),
            [| box throttledLeftPointerPosition |]
        )

        React.useEffect (
            (fun () ->

                if
                    throttledRightPointerPosition.IsSome
                    && StartResizeState.current.IsSome
                    && rightRef.current.IsSome
                then
                    let windowX = int Browser.Dom.window.innerWidth

                    let offsetRight =
                        rightRef.current.Value.offsetLeft + rightRef.current.Value.clientWidth

                    let newWidth = int offsetRight - int throttledRightPointerPosition.Value

                    match calcResize newWidth windowX widthLeft with
                    | CloseTargetSidebar ->
                        ctxLeft.setState StartResizeState.current.Value.isOpen
                        ctxRight.setIsOpen false
                    | ResizeTarget width ->
                        ctxLeft.setState StartResizeState.current.Value.isOpen
                        ctxRight.setIsOpen true
                        setWidthRight width
                    | ResizeBoth(rightWidth, leftWidth) ->
                        ctxLeft.setState StartResizeState.current.Value.isOpen
                        setWidthRight rightWidth
                        setWidthLeft leftWidth
                    | ResizeTargetCollapseOther width ->
                        ctxRight.setIsOpen true
                        setWidthRight width
                        ctxLeft.setState false
            ),
            [| box throttledRightPointerPosition |]
        )

        Html.div [
            prop.className "swt:flex swt:flex-row swt:w-full swt:flex-1 swt:min-h-0"
            prop.children [
                if leftActions.IsSome then
                    Layout.SidebarActions(leftActions.Value, Sidebar.Side.Left)
                if leftContent.IsSome then
                    Layout.SidebarArea(
                        leftContent.Value,
                        widthLeft,
                        ctxLeft.state,
                        leftRef,
                        setLeftPointerPositionWrapper,
                        Sidebar.Side.Left
                    )
                Html.div [
                    prop.className "swt:grow swt:min-w-0"
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:size-full"
                            prop.children children
                        ]
                    ]
                ]
                if rightContent.IsSome then
                    Layout.SidebarArea(
                        rightContent.Value,
                        widthRight,
                        ctxRight.isOpen,
                        rightRef,
                        setRightPointerPositionWrapper,
                        Sidebar.Side.Right
                    )
                if rightActions.IsSome then
                    Layout.SidebarActions(rightActions.Value, Sidebar.Side.Right)
            ]
        ]

    [<ReactComponent(true)>]
    static member Main<'A>
        (
            children: ReactElement,
            ?navbar: ReactElement,
            ?banners: BannerEntry[],
            ?leftSidebar: ReactElement,
            ?rightSidebar: ReactElement,
            ?leftActions: ReactElement,
            ?rightActions: ReactElement,
            ?rightSidebarState: RightSidebarState<'A>,
            ?leftSidebarDefaultOpen: bool,
            ?rightSidebarDefaultOpen: bool
        ) =

        let leftSidebarDefaultOpen = defaultArg leftSidebarDefaultOpen false
        let rightSidebarDefaultOpen = defaultArg rightSidebarDefaultOpen false

        // Keep the legacy storage key so existing left-sidebar visibility preferences survive the naming fix.
        let leftSidebarIsOpen, setLeftSidebarIsOpen =
            React.useLocalStorage (Keys.mkLocalStorageKey "layout" "main" "rightSidebarOpen", leftSidebarDefaultOpen)

        let unmanagedRightSidebarIsOpen, setUnmanagedRightSidebarIsOpen =
            React.useState rightSidebarDefaultOpen

        let unmanagedRightSidebarType, setUnmanagedRightSidebarType =
            React.useState (Unchecked.defaultof<'A>)

        let rightSidebarState =
            defaultArg rightSidebarState {
                isOpen = unmanagedRightSidebarIsOpen
                setIsOpen = setUnmanagedRightSidebarIsOpen
                sidebarType = unmanagedRightSidebarType
                setSidebarType = setUnmanagedRightSidebarType
            }

        let navbar = React.useMemo ((fun () -> navbar), [| box navbar |])

        let leftSidebar = React.useMemo ((fun () -> leftSidebar), [| box leftSidebar |])

        let rightSidebar = React.useMemo ((fun () -> rightSidebar), [| box rightSidebar |])
        let leftActions = React.useMemo ((fun () -> leftActions), [| box leftActions |])
        let rightActions = React.useMemo ((fun () -> rightActions), [| box rightActions |])

        let children =
            React.useMemo (
                (fun () ->
                    Html.div [
                        prop.className "swt:isolate swt:grow swt:min-w-0 swt:min-h-0 swt:overflow-hidden"
                        prop.testId "layout-main-content"
                        prop.children children
                    ]
                ),
                [| box children |]
            )

        LeftSidebarCtx.Provider(
            {
                state = leftSidebarIsOpen
                setState = setLeftSidebarIsOpen
            },
            RightSidebarCtx.Provider(
                RightSidebarHelper.toRightSidebarCtxState rightSidebarState,
                Html.div [
                    prop.className "swt:flex-1 swt:flex swt:flex-col swt:h-screen swt:overflow-hidden"
                    prop.children [
                        if navbar.IsSome then
                            Html.div [
                                // Keep navbar menus above content-local buttons and floating widgets.
                                prop.className
                                    "swt:isolate swt:z-20 swt:h-12 swt:shrink-0 swt:grow-0 swt:flex swt:flex-row swt:items-center swt:border-b swt:border-base-content/50"
                                prop.testId "layout-main-navbar"
                                prop.children navbar.Value
                            ]
                        // Below the navbar, so the navbar does not move when a banner comes or goes. Without
                        // banners the slot stays as an empty child, so the sidebars keep their position among the
                        // children and React does not remount them and the content.
                        match banners with
                        | Some banners when banners.Length > 0 ->
                            Html.div [
                                prop.className "swt:w-full swt:shrink-0 swt:grow-0 swt:flex swt:flex-col"
                                prop.testId "layout-main-banners"
                                prop.children [
                                    for entry in banners do
                                        React.KeyedFragment(
                                            entry.Key,
                                            [
                                                Banner.Banner(
                                                    entry.Message,
                                                    actions = entry.Actions,
                                                    variant = entry.Variant,
                                                    ?icon = entry.Icon,
                                                    testId = entry.Key,
                                                    ?onClose = entry.OnClose
                                                )
                                            ]
                                        )
                                ]
                            ]
                        | _ -> Html.none
                        Layout.Sidebars(
                            children,
                            ?leftContent = leftSidebar,
                            ?leftActions = leftActions,
                            ?rightContent = rightSidebar,
                            ?rightActions = rightActions
                        )
                    ]
                ]
            )
        )


    [<ReactComponent>]
    static member private Wrapper (children: ReactElement) (className: string) =
        Html.div [
            prop.className [
                "swt:flex swt:flex-col swt:gap-2 swt:grow swt:items-center swt:justify-center"
                className
            ]
            prop.children children
        ]

    [<ReactComponent>]
    static member Entry() =

        let rightSidebarTarget, setRightSidebarTarget =
            React.useLocalStorage (
                Keys.mkLocalStorageKey "layout" "main" "rightSidebarTarget",
                Mocks.RightSidebarTargetMock.Home
            )

        let rightSidebarIsOpen, setRightSidebarIsOpen =
            React.useLocalStorage (Keys.mkLocalStorageKey "layout" "main" "rightSidebarIsOpen", true)

        let toggleRightSidebarTarget =
            fun target ->
                if rightSidebarTarget = target then
                    setRightSidebarIsOpen (not rightSidebarIsOpen)
                else
                    setRightSidebarIsOpen true
                    setRightSidebarTarget target

        Layout.Main(
            children =
                Layout.Wrapper
                    (React.Fragment [
                        Html.div "Main Content"
                        Html.div (string rightSidebarIsOpen)
                        Html.div (string rightSidebarTarget)
                    ])
                    "swt:bg-base-300 swt:h-full",
            navbar = Navbar.Entry(),
            leftSidebar =
                Html.ul [
                    prop.className "swt:menu swt:w-full swt:p-2 swt:rounded-box swt:h-full swt:flex-nowrap"
                    prop.children [
                        for i in 0..100 do
                            Html.li [ Html.a [ prop.text $"Sidebar Item {i}" ] ]
                    ]
                ],
            rightSidebar =
                Html.ul [
                    prop.className "swt:menu swt:w-full swt:flex-nowrap swt:p-2 swt:h-full"
                    prop.children [
                        for i in 0..100 do
                            Html.li [ Html.a [ prop.text $"Right Sidebar Item {i}" ] ]
                    ]
                ],
            rightActions =
                React.Fragment [
                    Layout.LayoutBtn(
                        iconClassName = "swt:fluent--search-24-regular",
                        tooltip = "Search",
                        onClick = fun () -> Browser.Dom.window.alert "Search clicked"
                    )
                    Layout.LayoutBtn(
                        iconClassName = "swt:fluent--info-24-regular",
                        tooltip = "Help",
                        onClick = fun () -> Browser.Dom.window.alert "Help clicked"
                    )
                ],
            leftActions =
                React.Fragment [
                    Layout.LayoutBtn(
                        iconClassName = "swt:fluent--home-24-regular",
                        tooltip = "Home",
                        isActive = (rightSidebarTarget = Mocks.RightSidebarTargetMock.Home),
                        onClick = fun () -> toggleRightSidebarTarget Mocks.RightSidebarTargetMock.Home
                    )
                    Layout.LayoutBtn(
                        iconClassName = "swt:fluent--settings-24-regular",
                        tooltip = "Settings",
                        isActive = (rightSidebarTarget = Mocks.RightSidebarTargetMock.Settings),
                        onClick = fun () -> toggleRightSidebarTarget Mocks.RightSidebarTargetMock.Settings
                    )
                    Layout.LayoutBtn(
                        iconClassName = "swt:fluent--info-24-regular",
                        tooltip = "Info",
                        isActive = (rightSidebarTarget = Mocks.RightSidebarTargetMock.Info),
                        onClick = fun () -> toggleRightSidebarTarget Mocks.RightSidebarTargetMock.Info
                    )
                ],
            rightSidebarState = {
                isOpen = rightSidebarIsOpen
                setIsOpen = setRightSidebarIsOpen
                sidebarType = rightSidebarTarget
                setSidebarType = setRightSidebarTarget
            },
            leftSidebarDefaultOpen = true
        )
