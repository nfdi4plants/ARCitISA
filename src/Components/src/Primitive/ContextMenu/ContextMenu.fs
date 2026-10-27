namespace Swate.Components.Primitive.ContextMenu

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.ContextMenu.Types

module private ContextMenuDom =

    let tryGetEventTargetElement (e: Event) : Element option =
        let targetObj: obj = box e.target

        if isNullOrUndefined targetObj then
            None
        elif isNullOrUndefined targetObj?closest then
            let parentElement: obj = targetObj?parentElement

            if isNullOrUndefined parentElement then
                None
            else
                Some(unbox<Element> parentElement)
        else
            Some(unbox<Element> targetObj)

[<Erase>]
[<Mangle(false)>]
type ContextMenu =

    [<ReactComponent>]
    static member private Item(child: ContextMenuItem, props: IReactProperty list, ?key: string) =
        if child.isDivider then
            Html.div [ prop.className "swt:divider swt:my-0 swt:col-span-3" ]
        else
            Html.button [
                if child.label.IsSome then
                    prop.ariaLabel child.label.Value
                prop.className
                    "swt:col-span-3 swt:grid swt:grid-cols-subgrid swt:gap-x-2 swt:text-sm /
                    swt:text-base-content swt:px-2 swt:py-1 /
                    swt:w-full swt:text-left /
                    swt:hover:bg-base-100 /
                    swt:focus:bg-base-100 swt:focus:outline-hidden swt:focus:ring-2 swt:focus:ring-primary"
                prop.children [
                    if child.icon.IsSome then
                        Html.div [
                            prop.className
                                "swt:col-start-1 swt:justify-self-start swt:self-center swt:flex swt:items-center"
                            prop.children child.icon.Value
                        ]
                    else
                        Html.none
                    if child.text.IsSome then
                        Html.div [
                            prop.custom ("data-context-menu-item-label", "")
                            prop.className "swt:col-start-2 swt:justify-self-start"
                            prop.children child.text.Value
                        ]
                    if child.kbdbutton.IsSome then
                        Html.div [
                            prop.className "swt:col-start-3 swt:justify-self-end"
                            prop.children child.kbdbutton.Value.element
                        ]
                    else
                        Html.none
                ]
                yield! props
            ]

    /// <summary></summary>
    /// <param name="childInfo"></param>
    /// <param name="ref">Element that owns the local context-menu listener.</param>
    /// <param name="onSpawn">A function that returns an option of any data. Data is stored in react
    /// state and can be accessed in context menu item onClick handlers. By default stores just the
    /// event target. Only if Some is returned context menu is opened.</param>
    /// <param name="renderTrigger">Renders a trigger with an opener anchored to ref.current. Passes that
    /// element to childInfo as spawn data, without invoking the mouse-specific onSpawn callback.</param>
    /// <returns></returns>
    [<ReactComponent(true)>]
    static member ContextMenu
        (
            childInfo: obj -> ContextMenuItem list,
            ref: IRefValue<HTMLElement option>,
            ?onSpawn: Browser.Types.MouseEvent -> obj option,
            ?portalRootRef: IRefValue<HTMLElement option>,
            ?debug: bool,
            ?renderTrigger: (unit -> unit) -> ReactElement
        ) =

        let (spawnData: obj), setSpawnData = React.useState (null)
        let children, setChildren = React.useState ([])
        let onSpawn = defaultArg onSpawn (fun e -> Some(box e))
        let (isOpen, setIsOpen) = React.useState (false)
        let (activeIndex: int option), setActiveIndex = React.useState (None)

        let listItemsRef: IRefValue<ResizeArray<HTMLElement>> = React.useRef (ResizeArray())

        let listContentRef: IRefValue<ResizeArray<string option>> =
            React.useRef (ResizeArray())

        let debug = defaultArg debug false

        let floating =
            FloatingUI.useFloating (
                ``open`` = isOpen,
                onOpenChange = setIsOpen,
                middleware = [|
                    FloatingUI.Middleware.offset {| mainAxis = 5; alignmentAxis = 4 |}
                    FloatingUI.Middleware.flip {|
                        fallbackPlacements = [| "left-start" |]
                    |}
                    FloatingUI.Middleware.shift {| padding = 10 |}
                |],
                placement = FloatingUI.Placement.RightStart,
                strategy = FloatingUI.FloatingStrategy.Fixed,
                whileElementsMounted = FloatingUI.autoUpdate
            )

        let dismiss = FloatingUI.useDismiss (floating.context)

        let role =
            FloatingUI.useRole (floating.context, FloatingUI.UseRoleProps(role = FloatingUI.RoleAttribute.Menu))

        let listNavigation =
            FloatingUI.useListNavigation (
                floating.context,
                FloatingUI.UseListNavigationProps(
                    listRef = listItemsRef,
                    activeIndex = activeIndex,
                    onNavigate = setActiveIndex
                )
            )

        let typeahead =
            FloatingUI.useTypeahead (
                floating.context,
                FloatingUI.UseTypeaheadProps(
                    enabled = isOpen,
                    listRef = listContentRef,
                    onMatch = setActiveIndex,
                    activeIndex = activeIndex
                )
            )

        let interactions =
            FloatingUI.useInteractions [| role; dismiss; listNavigation; typeahead |]

        let prepareMenu data =
            let items = childInfo data
            listContentRef.current.Clear()
            listItemsRef.current.Clear()
            // Ref callbacks assign by item index, including the slots occupied by dividers.
            listItemsRef.current.AddRange(items |> List.map (fun _ -> Unchecked.defaultof<HTMLElement>))
            setActiveIndex None
            setChildren items
            setSpawnData data
            listContentRef.current.AddRange(items |> List.map _.label)
            let hasItems = not (List.isEmpty items)
            setIsOpen hasItems

            hasItems

        let openMenu () =
            match ref.current with
            | Some anchor ->
                floating.refs.setPositionReference !^anchor
                prepareMenu (box anchor) |> ignore
            | None -> ()

        React.useEffect (
            (fun () ->

                let cleanupListeners =
                    match ref.current with
                    | Some scopedElement ->
                        let onContextMenu (e: Event) =
                            let e = e :?> Browser.Types.MouseEvent

                            let isInsideContextMenuScope =
                                e
                                |> ContextMenuDom.tryGetEventTargetElement
                                |> Option.exists (fun target -> scopedElement.contains target)

                            if isInsideContextMenuScope then
                                match onSpawn e with
                                | Some data ->
                                    if prepareMenu data then
                                        e.preventDefault ()

                                        let rect: ClientRect =
                                            {|
                                                width = 0
                                                height = 0
                                                x = e.clientX
                                                y = e.clientY
                                                top = e.clientY
                                                left = e.clientX
                                                right = e.clientX
                                                bottom = e.clientY
                                            |}
                                            |> unbox

                                        let vEl = FloatingUI.VirtualElement(fun () -> rect)
                                        floating.refs.setPositionReference !^vEl

                                | None -> ()

                        scopedElement.addEventListener ("contextmenu", onContextMenu)

                        fun () -> scopedElement.removeEventListener ("contextmenu", onContextMenu)
                    | None -> ignore

                cleanupListeners
            ),
            [| box floating.refs; box childInfo; box onSpawn; box ref |]
        )

        React.Fragment [
            match renderTrigger with
            | Some render -> render openMenu
            | None -> Html.none

            if isOpen then
                FloatingUI.FloatingPortal(
                    FloatingUI.FloatingOverlay(
                        lockScroll = true,
                        className = "swt:z-9999",
                        children =
                            FloatingUI.FloatingFocusManager(
                                context = floating.context,
                                initialFocus = floating.refs.floating,
                                visuallyHiddenDismiss = true,
                                children =
                                    Html.div [
                                        prop.ref floating.refs.setFloating
                                        if debug then
                                            prop.testId "context_menu"
                                        prop.custom ("style", floating.floatingStyles)
                                        for key, v in
                                            interactions.getFloatingProps (null)
                                            |> Fable.Core.JS.Constructors.Object.entries do
                                            prop.custom (key, v)
                                        prop.className
                                            "swt:grid swt:grid-cols-[auto_1fr_auto] swt:bg-base-100 swt:border-2 swt:border-base-300 swt:min-w-56 swt:rounded-md swt:focus:outline-hidden"
                                        prop.children [
                                            for index in 0 .. children.Length - 1 do
                                                let child = children.[index]

                                                let triggerEvent =
                                                    fun (e: Browser.Types.MouseEvent) ->
                                                        let data = {|
                                                            buttonEvent = e
                                                            spawnData = spawnData
                                                        |}

                                                        child.onClick |> Option.iter (fun callback -> callback data)
                                                        setIsOpen false

                                                let props =
                                                    interactions.getItemProps (
                                                        {|
                                                            ref =
                                                                fun (node: HTMLElement) ->
                                                                    // React also clears the previous menu's refs when a
                                                                    // new menu has fewer items (or no items at all).
                                                                    if index < listItemsRef.current.Count then
                                                                        listItemsRef.current.[index] <- node

                                                                    if
                                                                        child.label.IsNone
                                                                        && not (isNullOrUndefined node)
                                                                    then
                                                                        match
                                                                            node.querySelector (
                                                                                "[data-context-menu-item-label]"
                                                                            )
                                                                        with
                                                                        | :? HTMLElement as labelElement ->
                                                                            listContentRef.current.[index] <-
                                                                                labelElement.textContent
                                                                                |> Option.ofObj
                                                                                |> Option.map _.Trim()
                                                                                |> Option.filter (
                                                                                    System.String.IsNullOrWhiteSpace
                                                                                    >> not
                                                                                )
                                                                        | _ -> ()
                                                            tabIndex =
                                                                if activeIndex.IsSome && activeIndex.Value = index then
                                                                    0
                                                                else
                                                                    -1
                                                            onClick = triggerEvent
                                                            label = child.label
                                                        |}
                                                    )
                                                    |> Fable.Core.JS.Constructors.Object.entries

                                                ContextMenu.Item(
                                                    child,
                                                    [ for key, value in props -> prop.custom (key, value) ],
                                                    key = string index
                                                )
                                        ]
                                    ]
                            )
                    ),
                    ?root = (portalRootRef |> Option.bind _.current |> Option.map box)
                )
            else
                Html.none
        ]

    [<ReactComponent>]
    static member Example() =

        let containerRef = React.useElementRef ()

        Html.div [
            prop.className
                "swt:w-full swt:h-72 swt:border swt:border-dashed swt:border-primary swt:rounded-sm swt:flex swt:items-center swt:justify-center swt:flex-col swt:gap-4"
            prop.ref containerRef

            prop.children [
                Html.span [
                    prop.className "swt:select-none"
                    prop.text "Click here for context menu!"
                ]
                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.text "Example Table Cell"
                    prop.dataRow 12
                    prop.dataColumn 5
                ]
                ContextMenu.ContextMenu(
                    (fun (data: obj) -> [
                        for i in 0..5 do
                            ContextMenuItem(
                                text = Html.span $"Item {i}",
                                ?icon = (if i = 4 then Icons.Check() |> Some else None),
                                ?kbdbutton =
                                    (if i = 3 then
                                         {|
                                             element =
                                                 Html.kbd [
                                                     prop.className "swt:ml-auto swt:kbd swt:kbd-sm"
                                                     prop.text "Back"
                                                 ]
                                             label = "Back"
                                         |}
                                         |> Some
                                     else
                                         None),
                                onClick =
                                    (fun e ->
                                        e.buttonEvent.stopPropagation ()
                                        let index = e.spawnData |> unbox<CellCoordinate>
                                        console.log (sprintf "Item clicked: %i" i, index)
                                    )
                            )
                    ]),
                    ref = containerRef,
                    onSpawn =
                        (fun e ->
                            let tableCell =
                                e
                                |> ContextMenuDom.tryGetEventTargetElement
                                |> Option.bind (fun target -> target.closest ("[data-row][data-column]"))

                            match tableCell with
                            | Some cell ->
                                let cell = cell :?> HTMLElement
                                let row = int cell?dataset?row
                                let col = int cell?dataset?column
                                let indices: CellCoordinate = {| y = row; x = col |}
                                Some indices
                            | _ -> None
                        )
                )
            ]
        ]
