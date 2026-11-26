namespace Arcitisa.Components.Primitive.Select

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive.Select.Context
open Arcitisa.Components.Primitive.Select.Types


[<Erase; Mangle(false)>]
type GenericSelect =

    [<ReactComponent>]
    static member InnerBaseOptionRender
        (label: string, isSelected: bool, ?ref: IRefValue<option<Browser.Types.HTMLInputElement>>)
        =
        React.Fragment [
            Html.div [
                prop.custom ("data-selectoption", label)
                prop.className
                    "swt:text-sm swt:font-normal swt:text-success swt:min-w-4 swt:h-full swt:flex swt:items-center"
                prop.children [
                    Html.input [
                        prop.ref (if ref.IsSome then ref.Value else unbox null)
                        prop.tabIndex -1
                        prop.className "swt:checkbox swt:checkbox-xs"
                        prop.type'.checkbox
                        prop.isChecked isSelected
                        prop.readOnly true
                    ]
                ]
            ]
            Html.div label
        ]

    [<ReactComponent>]
    static member OuterBaseOptionRender
        (
            isActive: bool,
            isSelected: bool,
            key: obj,
            listItem:
                {|
                    index: int
                    ref: IRefValue<option<Browser.Types.HTMLElement>>
                |},
            selectContext: SelectContextValue,
            toggleSelect: bool -> unit,
            children: ReactElement
        ) =
        Html.li [
            prop.key (unbox<string> key)
            prop.ref listItem.ref
            prop.role.option
            prop.ariaSelected (isActive && isSelected)
            prop.tabIndex 0
            prop.className [
                "swt:list-row swt:rounded-none swt:p-1 swt:border-l-4 swt:border-transparent swt:focus-within:outline-none swt:cursor-pointer"
                if isActive then
                    "swt:border-primary! swt:bg-base-content/10"
                if isSelected then
                    "swt:border-accent"
            ]
            yield!
                prop.spread
                <| selectContext.getItemProps (
                    {|
                        onClick = fun () -> toggleSelect isSelected
                        onKeyDown =
                            fun (e: Browser.Types.KeyboardEvent) ->
                                if e.code = kbdEventCode.enter then
                                    e.stopPropagation ()
                                    e.preventDefault ()
                                    toggleSelect isSelected
                    |}
                )
            prop.children children
        ]

    [<ReactComponent>]
    static member SelectItem<'a>
        (option: SelectItem<'a>, key: int, ?optionRenderFn: SelectItemRender<'a> -> ReactElement)
        =
        let OptionRender =
            optionRenderFn
            |> Option.defaultValue (fun (props: SelectItemRender<'a>) ->
                GenericSelect.InnerBaseOptionRender(props.item.label, props.isSelected)
            )

        let index = key

        let selectContext = useSelectCtx ()
        let listItem = FloatingUI.useListItem ()

        let isActive = selectContext.activeIndex = Some listItem.index
        let isSelected = selectContext.selectedIndices.Contains index

        let toggleSelect = fun (_) -> selectContext.handleSelect (Some index)

        GenericSelect.OuterBaseOptionRender(
            isActive,
            isSelected,
            key,
            listItem,
            selectContext,
            toggleSelect,
            OptionRender {|
                isActive = isActive
                isSelected = isSelected
                item = option
            |}
        )

    [<ReactComponent(true)>]
    static member GenericSelect<'a, 'selection>
        (
            options: SelectItem<'a>[],
            selected: 'selection,
            setSelected: 'selection -> unit,
            behavior: SelectBehavior<'selection>,
            ?onSelect: int option -> unit,
            ?triggerRenderFn: {| isOpen: bool |} -> ReactElement,
            ?optionRenderFn: SelectItemRender<'a> -> ReactElement,
            ?dropdownPlacement: FloatingUI.Placement,
            ?middleware: FloatingUI.IMiddleware[],
            ?leadingListItem: ReactElement
        ) =

        let mkLabel (indices: int seq) =
            indices |> Seq.map (fun i -> options.[i].label) |> String.concat ", "

        let isOpen, setIsOpen = React.useState (false)
        let activeIndex, setActiveIndex = React.useState (None: int option)

        let flui =
            FloatingUI.useFloating (
                placement = defaultArg dropdownPlacement FloatingUI.Placement.BottomStart,
                ``open`` = isOpen,
                onOpenChange = setIsOpen,
                whileElementsMounted = FloatingUI.autoUpdate,
                middleware = defaultArg middleware [| FloatingUI.Middleware.flip () |]
            )

        let elementsRef = React.useRef<Browser.Types.HTMLElement option[]> ([||])

        let labelsRef = React.useRef<string option[]> ([||])

        let selectedIndices =
            React.useMemo ((fun () -> behavior.selectedIndices selected), [| box selected |])

        let handleSelect =
            (fun (index: int option) ->
                onSelect |> Option.iter (fun f -> f index)

                if index.IsSome then
                    let next =
                        if behavior.isSelected selected index.Value then
                            behavior.deselect selected index.Value
                        else
                            behavior.select selected index.Value

                    setSelected (next)
            )

        let handleTypeaheadMatch =
            fun (index: int option) ->
                if isOpen then
                    setActiveIndex (index)

        let listNav =
            FloatingUI.useListNavigation (
                flui.context,
                FloatingUI.UseListNavigationProps(
                    listRef = elementsRef,
                    activeIndex = activeIndex,
                    onNavigate = setActiveIndex
                )
            )

        let typeahead =
            FloatingUI.useTypeahead (
                flui.context,
                FloatingUI.UseTypeaheadProps(
                    listRef = labelsRef,
                    activeIndex = activeIndex,
                    onMatch = handleTypeaheadMatch
                )
            )

        let click = FloatingUI.useClick (flui.context)
        let dismiss = FloatingUI.useDismiss (flui.context)

        let role =
            FloatingUI.useRole (flui.context, FloatingUI.UseRoleProps(role = FloatingUI.RoleAttribute.Listbox))

        let interactions =
            FloatingUI.useInteractions ([| listNav; typeahead; click; dismiss; role |])

        let selectContext: SelectContextValue =
            React.useMemo (
                (fun () -> {
                    activeIndex = activeIndex
                    selectedIndices = selectedIndices
                    optionCount = options.Length
                    getItemProps = interactions.getItemProps
                    handleSelect = handleSelect
                }),
                [|
                    activeIndex
                    selectedIndices
                    interactions.getItemProps
                    handleSelect
                    options.Length
                |]
            )

        let TriggerRender =
            triggerRenderFn
            |> Option.defaultValue (fun _ ->
                Html.button [
                    prop.tabIndex -1
                    prop.className [ "swt:btn swt:w-fit swt:pointer-events-none" ]
                    prop.text (
                        if selectedIndices.Count = 0 then
                            "Select an option"
                        else
                            mkLabel selectedIndices
                    )
                ]
            )

        let floatingStyle =
            let entries =
                JS.Constructors.Object.entries flui.floatingStyles
                |> Seq.choose (
                    function
                    | key, value when not (isNull value) -> Some(style.custom (key, value))
                    | _ -> None
                )
                |> Seq.toList

            entries @ [ style.zIndex 999 ]

        React.Fragment [
            Html.div [
                prop.className "swt:size-fit swt:cursor-pointer swt:select-none"
                prop.ref (unbox flui.refs.setReference)
                prop.tabIndex 0
                yield! prop.spread <| interactions.getReferenceProps (null)
                prop.children (TriggerRender {| isOpen = isOpen |})
            ]
            SelectCtx.Provider(
                selectContext,
                React.Fragment [
                    if isOpen then
                        FloatingUI.FloatingPortal(
                            FloatingUI.FloatingFocusManager(
                                flui.context,
                                modal = false,
                                children =
                                    Html.div [
                                        prop.ref (unbox flui.refs.setFloating)
                                        prop.style floatingStyle
                                        yield! prop.spread <| interactions.getFloatingProps (null)
                                        prop.children [
                                            FloatingUI.FloatingList(
                                                elementsRef = elementsRef,
                                                labelsRef = labelsRef,
                                                children =
                                                    Html.ul [
                                                        prop.className [
                                                            "swt:list swt:p-2"
                                                            "swt:bg-base-100 swt:shadow-sm swt:rounded-xs"
                                                            "swt:overflow-y-auto swt:max-h-[400px]"
                                                            "swt:border-2 swt:border-base-content/50"
                                                        ]
                                                        prop.children [

                                                            // if showSelectAll then
                                                            //     Select.SelectAll(setSelected, key = "select-all")
                                                            match leadingListItem with
                                                            | Some item -> item
                                                            | None -> Html.none

                                                            for i in 0 .. options.Length - 1 do
                                                                let option = options.[i]

                                                                GenericSelect.SelectItem(
                                                                    option,
                                                                    key = i,
                                                                    ?optionRenderFn = optionRenderFn
                                                                )
                                                        ]
                                                    ]
                                            )
                                        ]
                                    ]
                            )
                        )
                ]
            )
        ]
