namespace Swate.Components.Primitive.ComboBox

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.ComboBox.Types

[<Erase; Mangle(false)>]
type ComboBox =

    [<ReactComponent>]
    static member ListItem<'a>(children: ReactElement, active: bool, props: seq<IReactProperty>) =
        let newId = FloatingUI.useId ()

        Html.li [
            prop.className [
                "swt:list-row swt:rounded-none swt:p-1"
                if active then
                    "swt:bg-base-content swt:text-base-300"
            ]
            prop.ariaSelected active
            prop.children children
            prop.role.option
            prop.id newId
            yield! props
        ]

    [<ReactComponent(true)>]
    static member ComboBox<'a>
        (
            inputValue: string,
            onInputChange: string -> unit,
            items: 'a[],
            filterFn: {| item: 'a; search: string |} -> bool,
            itemToString: 'a -> string,
            ?onChange: int -> 'a -> unit,
            ?loading: bool,
            ?placeholder: string,
            ?itemRenderer:
                {|
                    item: 'a
                    index: int
                    isActive: bool
                    props: ResizeArray<IReactProperty>
                |}
                    -> ReactElement,
            ?itemContainerRenderer:
                {|
                    props: ResizeArray<IReactProperty>
                    children: ReactElement
                |}
                    -> ReactElement,
            ?noResultsRenderer: unit -> ReactElement,
            ?comboBoxRef: IRefValue<ComboBoxRef>,
            ?inputLeadingVisual: ReactElement,
            ?inputTrailingVisual: ReactElement,
            ?labelClassName: string,
            ?onKeyDown: Browser.Types.KeyboardEvent -> unit,
            ?onFocus: Browser.Types.FocusEvent -> unit,
            ?onBlur: Browser.Types.FocusEvent -> unit,
            ?onOpen: bool -> unit,
            ?onClick:
                Browser.Types.MouseEvent
                    -> {|
                        isOpen: bool
                        setIsOpen: bool -> unit
                        setActiveIndex: int option -> unit
                    |}
                    -> unit,
            ?props: obj,
            ?onDoubleClick:
                Browser.Types.MouseEvent
                    -> {|
                        isOpen: bool
                        setIsOpen: bool -> unit
                    |}
                    -> unit
        ) : ReactElement =

        let isOpen, setOpen = React.useState (false)

        let activeIndex, setActiveIndex = React.useState (None: int option)

        let listRef = React.useRef<ResizeArray<Browser.Types.HTMLElement>> (ResizeArray())

        let fluiContext =
            FloatingUI.useFloating (
                placement = FloatingUI.Placement.BottomStart,
                whileElementsMounted = FloatingUI.autoUpdate,
                ``open`` = isOpen,
                onOpenChange =
                    (fun b ->
                        onOpen |> Option.iter (fun oo -> oo b)
                        setOpen b
                    ),
                middleware = [|
                    FloatingUI.Middleware.offset (10)
                    FloatingUI.Middleware.flip ({| padding = 10 |})
                    FloatingUI.Middleware.shift ()
                    FloatingUI.Middleware.size ()
                |]
            )

        let role =
            FloatingUI.useRole (fluiContext.context, FloatingUI.UseRoleProps(role = FloatingUI.RoleAttribute.Listbox))

        let dismiss = FloatingUI.useDismiss (fluiContext.context)

        let listNav =
            FloatingUI.useListNavigation (
                fluiContext.context,
                FloatingUI.UseListNavigationProps(
                    listRef = listRef,
                    activeIndex = activeIndex,
                    onNavigate = setActiveIndex,
                    ``virtual`` = true,
                    loop = true
                )
            )

        let useInteractions = FloatingUI.useInteractions [| role; dismiss; listNav |]

        let close =
            fun () ->
                setOpen false
                setActiveIndex None

        let onInputChange (e: Browser.Types.InputEvent) =

            let value = e.target?value |> unbox string
            onInputChange value

            if System.String.IsNullOrWhiteSpace value |> not then
                setOpen true

                setActiveIndex None
            else
                close ()

        let onSelect =
            fun (index: int) (item: 'a) ->
                onChange |> Option.iter (fun fn -> fn index item)
                close ()
                fluiContext.refs.domReference.current |> Option.iter (fun el -> el.focus ())

        let filteredItems =
            items
            |> Array.filter (fun item -> filterFn {| item = item; search = inputValue |})

        let onBlur =
            fun (e: Browser.Types.FocusEvent) ->
                if not isOpen then
                    onBlur |> Option.iter (fun f -> f e)
                else
                    ()

        let ItemRenderer =
            React.useMemo (
                (fun () ->
                    match itemRenderer with
                    | Some renderer -> renderer
                    | None ->
                        fun props ->
                            ComboBox.ListItem<'a>(
                                children = Html.text (itemToString props.item),
                                active = props.isActive,
                                props = props.props
                            )
                ),
                [| box itemRenderer |]
            )

        let ItemContainerRenderer =
            React.useMemo (
                (fun () ->
                    match itemContainerRenderer with
                    | Some renderer -> renderer
                    | None ->
                        fun props ->
                            Html.ul [
                                prop.className [
                                    "swt:list swt:py-2"
                                    "swt:bg-base-100 swt:shadow-sm swt:rounded-xs"
                                    "swt:overflow-y-auto swt:max-h-1/2 swt:lg:max-h-1/3 swt:min-w-md swt:max-w-xl"
                                    "swt:border-2 swt:border-base-content/50 swt:z-999"
                                ]
                                prop.children props.children
                                yield! props.props
                            ]
                ),
                [| box itemContainerRenderer |]
            )

        let LoadingRenderer =
            React.useMemo (
                (fun () ->
                    fun () ->
                        Html.li [
                            prop.className
                                "swt:p-4 swt:tracking-widest swt:flex swt:items-center swt:gap-2 swt:justify-center swt:uppercase"
                            prop.children [
                                Html.span "Loading..."
                                Html.span [ prop.className "swt:loading swt:loading-ring" ]
                            ]
                        ]
                ),
                [||]
            )

        let NoResultsRenderer =
            React.useMemo (
                (fun () ->
                    match noResultsRenderer with
                    | Some renderer -> renderer
                    | None ->
                        fun () ->
                            Html.li [
                                prop.className "swt:p-4 swt:tracking-wide"
                                prop.text "No results found."
                            ]
                ),
                [| box noResultsRenderer |]
            )

        let placeholder = defaultArg placeholder "Search..."

        React.useImperativeHandle (
            unbox comboBoxRef,
            (fun () -> {|
                focus = fun () -> fluiContext.refs.domReference.current |> Option.iter (fun el -> el.focus ())
                close =
                    fun () ->
                        setOpen false
                        setActiveIndex None
                isOpen = fun () -> isOpen
            |}),
            [| box fluiContext.refs.reference; box isOpen |]
        )

        let inputId = FloatingUI.useId ()

        React.Fragment [
            Html.label [
                prop.htmlFor inputId
                prop.ref (unbox fluiContext.refs.setReference)
                prop.onClick (fun (ev: Browser.Types.MouseEvent) ->
                    onClick
                    |> Option.iter (fun fn ->
                        fn ev {|
                            isOpen = isOpen
                            setIsOpen = setOpen
                            setActiveIndex = setActiveIndex
                        |}
                    )
                )
                prop.className [
                    "swt:input swt:group"
                    if labelClassName.IsSome then
                        labelClassName.Value
                ]
                prop.children [
                    if inputLeadingVisual.IsSome then
                        inputLeadingVisual.Value
                    Html.input [
                        prop.id inputId
                        prop.autoComplete "off"
                        prop.className "swt:grow swt:shrink swt:min-w-0"
                        if props.IsSome then
                            yield! prop.spread props.Value
                        prop.role.comboBox
                        for key, v in
                            useInteractions.getReferenceProps (
                                {|
                                    onChange = onInputChange
                                    value = inputValue
                                    placeholder = placeholder
                                    ``aria-autocomplete`` = "list"
                                    onFocus =
                                        fun (ev: Browser.Types.FocusEvent) -> onFocus |> Option.iter (fun fn -> fn ev)
                                    onBlur = onBlur
                                    onKeyDown =
                                        fun (ev: Browser.Types.KeyboardEvent) ->
                                            if
                                                ev.key = "Enter"
                                                && activeIndex.IsSome
                                                && Array.tryItem activeIndex.Value filteredItems |> Option.isSome
                                            then
                                                ev.stopPropagation ()
                                                onSelect activeIndex.Value filteredItems.[activeIndex.Value]
                                            elif ev.key = "Escape" then
                                                ev.stopPropagation ()
                                                setOpen false
                                                setActiveIndex None

                                            onKeyDown |> Option.iter (fun fn -> fn ev)
                                    onDoubleClick =
                                        fun (ev: Browser.Types.MouseEvent) ->
                                            onDoubleClick
                                            |> Option.iter (fun fn ->
                                                fn ev {|
                                                    isOpen = isOpen
                                                    setIsOpen = setOpen
                                                |}
                                            )
                                |}
                            )
                            |> Fable.Core.JS.Constructors.Object.entries do
                            prop.custom (key, v)
                    ]
                    if inputTrailingVisual.IsSome then
                        inputTrailingVisual.Value
                ]
            ]
            if isOpen then
                FloatingUI.FloatingPortal(
                    FloatingUI.FloatingFocusManager(
                        context = fluiContext.context,
                        initialFocus = -1,
                        visuallyHiddenDismiss = true,
                        children =
                            ItemContainerRenderer {|
                                props =
                                    (useInteractions.getFloatingProps (
                                        {|
                                            ref = fluiContext.refs.setFloating
                                            style = fluiContext.floatingStyles
                                        |}
                                     )
                                     |> Fable.Core.JS.Constructors.Object.entries
                                     |> Seq.map (fun (key, v) -> prop.custom (key, v))
                                     |> ResizeArray)
                                children =
                                    React.Fragment [
                                        if loading.IsSome && loading.Value then
                                            LoadingRenderer()
                                        elif filteredItems.Length = 0 then
                                            NoResultsRenderer()
                                        else
                                            yield!
                                                filteredItems
                                                |> Array.mapi (fun index item ->
                                                    let props =
                                                        (useInteractions.getItemProps (
                                                            {|
                                                                key = index
                                                                ref =
                                                                    fun node ->
                                                                        let nodes = listRef.current

                                                                        while nodes.Count <= index do
                                                                            nodes.Add
                                                                                Unchecked.defaultof<
                                                                                    Browser.Types.HTMLElement
                                                                                 >

                                                                        nodes.[index] <- node
                                                                onClick = fun _ -> onSelect index item
                                                                onMouseDown = fun _ -> onSelect index item
                                                            |}
                                                         )
                                                         |> Fable.Core.JS.Constructors.Object.entries
                                                         |> Seq.map (fun (key, v) -> prop.custom (key, v)))
                                                        |> ResizeArray

                                                    let isActive = activeIndex = Some index

                                                    ItemRenderer {|
                                                        item = item
                                                        index = index
                                                        props = props
                                                        isActive = isActive
                                                    |}
                                                )
                                    ]
                            |}
                    )
                )
        ]


    [<ReactComponent>]
    static member Entry() =
        let fruitPool = [|
            "Apple"
            "Banana"
            "Orange"
            "Strawberry"
            "Pineapple"
            "Grape"
            "Peach"
            "Mango"
            "Blueberry"
            "Raspberry"
            "Watermelon"
            "Cherry"
            "Pear"
            "Plum"
            "Kiwi"
            "Lemon"
            "Lime"
            "Grapefruit"
            "Coconut"
            "Apricot"
            "Fig"
            "Date"
            "Pomegranate"
            "Guava"
            "Passionfruit"
            "Lychee"
            "Papaya"
            "Blackberry"
            "Tangerine"
            "Cantaloupe"
            "Cranberry"
            "Nectarine"
            "Persimmon"
            "Dragonfruit"
            "Jackfruit"
            "Starfruit"
            "Mulberry"
            "Gooseberry"
            "Quince"
            "Currant"
            "Durian"
            "Longan"
            "Rambutan"
            "Soursop"
            "Ugli fruit"
            "Salak"
            "Jujube"
            "Sapodilla"
            "Feijoa"
            "Medlar"
        |]


        let filterFn =
            fun (props: {| item: string; search: string |}) -> props.item.ToLower().Contains(props.search.ToLower())

        let itemRendererFn
            (props:
                {|
                    item: string
                    index: int
                    isActive: bool
                    props: ResizeArray<IReactProperty>
                |})
            =

            Html.li [
                prop.className [
                    "swt:list-row swt:rounded-none swt:p-1"
                    if props.isActive then
                        "swt:bg-base-content swt:text-base-300"
                ]
                prop.children [
                    Html.div [
                        prop.className
                            "swt:size-10 swt:bg-neutral swt:rounded-box swt:text-neutral-content swt:flex swt:items-center swt:justify-center"
                        prop.text props.index
                    ]
                    Html.div props.item
                ]
                yield! props.props
            ]

        let itemContainerRendererFn
            (props:
                {|
                    props: ResizeArray<IReactProperty>
                    children: ReactElement
                |})
            =
            Html.ul [
                prop.className [
                    "swt:list swt:py-2"
                    "swt:bg-base-100 swt:shadow-sm swt:rounded-xs"
                    "swt:overflow-y-auto swt:max-h-1/2 swt:lg:max-h-1/3 swt:min-w-md swt:max-w-xl"
                    "swt:border-2 swt:border-base-content/50"
                ]
                prop.children props.children
                yield! props.props
            ]


        let comboBoxRef = React.useRef<ComboBoxRef> (unbox None)

        React.useListener.onKeyDown (fun (ev: Browser.Types.KeyboardEvent) ->
            if ev.key = "/" then
                comboBoxRef.current.focus ()
                ev.preventDefault ()
        )

        let inputValue, setInputValue = React.useState ("")

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-4"
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-primary swt:max-w-sm"
                    prop.text "Focus ComboBox"
                    prop.onClick (fun _ -> comboBoxRef.current.focus ())
                ]

                ComboBox.ComboBox<string>(
                    inputValue,
                    onInputChange = setInputValue,
                    items = fruitPool,
                    filterFn = filterFn,
                    itemToString = id,
                    itemRenderer = itemRendererFn,
                    itemContainerRenderer = itemContainerRendererFn,
                    comboBoxRef = comboBoxRef,
                    inputLeadingVisual = Icons.MagnifyingClass(),
                    inputTrailingVisual =
                        React.Fragment [
                            Html.kbd [ prop.className "swt:kbd"; prop.text "/" ]
                            Icons.ChevronDown()
                        ],
                    onBlur = fun e -> console.log ("ComboBox blurred")
                )
            ]
        ]
