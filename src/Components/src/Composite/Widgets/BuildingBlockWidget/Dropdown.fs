namespace Swate.Components.Composite.Widgets.BuildingBlockWidget

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.JsBindings
open Swate.Components.Primitive.Popover
open Swate.Components.Shared

module private BuildingBlockDropdownHelper =

    let MainHeaderTypes: CompositeHeaderDiscriminate[] = [|
        CompositeHeaderDiscriminate.Parameter
        CompositeHeaderDiscriminate.Factor
        CompositeHeaderDiscriminate.Characteristic
        CompositeHeaderDiscriminate.Component
    |]

    let MoreHeaderTypes: CompositeHeaderDiscriminate[] = [|
        CompositeHeaderDiscriminate.Comment
        CompositeHeaderDiscriminate.Date
        CompositeHeaderDiscriminate.Performer
        CompositeHeaderDiscriminate.ProtocolDescription
        CompositeHeaderDiscriminate.ProtocolREF
        CompositeHeaderDiscriminate.ProtocolType
        CompositeHeaderDiscriminate.ProtocolUri
        CompositeHeaderDiscriminate.ProtocolVersion
    |]

    let IOTypeOptions: IOType[] = [|
        IOType.Source
        IOType.Sample
        IOType.Material
        IOType.Data
    |]

    let middleware () = [|
        FloatingUI.Middleware.offset 10
        FloatingUI.Middleware.flip ()
        FloatingUI.Middleware.shift {| padding = 8 |}
        FloatingUI.Middleware.size (
            FloatingUI.SizeOptions(
                padding = 8.,
                apply =
                    (fun state ->
                        let floating = state.elements.floating
                        let width = min 256. (max 0. state.availableWidth)
                        Dom.setStyleProperty floating "width" $"{width}px"
                        Dom.setStyleProperty floating "max-width" $"{max 0. state.availableWidth}px"

                        Dom.setStyleProperty
                            floating
                            "--building-block-menu-height"
                            $"{max 0. state.availableHeight}px"
                    )
            )
        )
    |]

[<Erase; Mangle(false)>]
type BuildingBlockDropdown =

    [<ReactComponent>]
    static member private FreeTextInputElement(onSubmit: string -> unit, ?freeText: string) =
        let inputS, setInput = React.useState (defaultArg freeText "")

        Html.div [
            prop.className "swt:flex! swt:flex-row swt:gap-0! swt:join"
            prop.children [
                Html.input [
                    prop.value inputS
                    prop.ariaLabel "Custom input or output type"
                    prop.placeholder "..."
                    prop.className
                        "swt:input swt:input-sm swt:join-item swt:min-w-0 swt:w-0 swt:grow swt:truncate swt:rounded-l-(--radius-field)! swt:rounded-r-none!"
                    prop.onChange (fun (value: string) -> setInput value)
                    prop.onKeyDown (
                        key.enter,
                        fun e ->
                            e.preventDefault ()
                            e.stopPropagation ()
                            onSubmit inputS
                    )
                ]
                Html.button [
                    prop.type'.button
                    prop.ariaLabel "Confirm custom type"
                    prop.className "swt:btn swt:btn-accent swt:btn-sm swt:join-item"
                    prop.onClick (fun e ->
                        e.preventDefault ()
                        e.stopPropagation ()
                        onSubmit inputS
                    )
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--checkmark-20-regular"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private HeaderDropdownItem
        (headerType: CompositeHeaderDiscriminate, onSelectHeaderType: CompositeHeaderDiscriminate -> unit)
        =
        Html.li [
            Html.button [
                prop.type'.button
                prop.onClick (fun _ -> onSelectHeaderType headerType)
                prop.text (headerType.ToString())
            ]
        ]

    [<ReactComponent>]
    static member private InfoFooter() =
        Html.li [
            Html.a [
                prop.href URLs.AnnotationPrinciplesUrl
                prop.target "_blank"
                prop.rel "noopener noreferrer"
                prop.className "swt:justify-end swt:text-info"
                prop.text "info"
            ]
        ]

    [<ReactComponent>]
    static member private Submenu(title: string, children: ReactElement list) =
        Html.li [ Html.details [ Html.summary title; Html.ul children ] ]

    [<ReactComponent>]
    static member private IOTypes
        (
            headerType: CompositeHeaderDiscriminate,
            selectedIOType: IOType option,
            onSelectHeaderIOType: CompositeHeaderDiscriminate -> IOType -> unit
        ) =
        let freeText =
            selectedIOType
            |> Option.bind (
                function
                | IOType.FreeText text -> Some text
                | _ -> None
            )

        BuildingBlockDropdown.Submenu(
            headerType.ToString(),
            [
                for ioType in BuildingBlockDropdownHelper.IOTypeOptions do
                    Html.li [
                        Html.button [
                            prop.type'.button
                            prop.onClick (fun _ -> onSelectHeaderIOType headerType ioType)
                            prop.text (ioType.ToString())
                        ]
                    ]
                Html.li [
                    prop.className "swt:menu-title"
                    prop.children [
                        BuildingBlockDropdown.FreeTextInputElement(
                            (fun text -> onSelectHeaderIOType headerType (IOType.FreeText text)),
                            ?freeText = freeText
                        )
                    ]
                ]
                BuildingBlockDropdown.InfoFooter()
            ]
        )

    [<ReactComponent>]
    static member Main
        (
            selectedHeaderType: CompositeHeaderDiscriminate,
            selectedIOType: IOType option,
            onSelectHeaderType: CompositeHeaderDiscriminate -> unit,
            onSelectHeaderIOType: CompositeHeaderDiscriminate -> IOType -> unit
        ) =
        let isOpen, setIsOpen = React.useState false

        let selectHeaderType headerType =
            onSelectHeaderType headerType
            setIsOpen false

        let selectIOType headerType ioType =
            onSelectHeaderIOType headerType ioType
            setIsOpen false

        Popover.Popover(
            isOpen = isOpen,
            onOpenChange = setIsOpen,
            modal = false,
            placement = FloatingUI.Placement.BottomStart,
            middleware = BuildingBlockDropdownHelper.middleware (),
            children =
                React.Fragment [
                    Popover.TriggerRender(fun trigger ->
                        Html.button [
                            prop.type'.button
                            prop.custom ("ref", trigger.setReference)
                            prop.className
                                "swt:btn swt:btn-primary swt:border swt:border-base-content! swt:join-item swt:flex-nowrap"
                            yield! prop.spread trigger.referenceProps
                            prop.children [
                                Html.span (selectedHeaderType.ToString())
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--chevron-down-20-regular"
                                ]
                            ]
                        ]
                    )
                    Popover.Content(
                        ariaLabel = "Building block type",
                        className = "swt:min-w-0! swt:p-0! swt:rounded-box! swt:bg-base-200!",
                        children =
                            Html.ul [
                                prop.className
                                    "swt:menu swt:menu-paged swt:w-full swt:flex-nowrap! swt:overflow-y-auto swt:overflow-x-hidden swt:max-h-[max(0px,calc(var(--building-block-menu-height,80vh)-2px))]"
                                prop.children [
                                    BuildingBlockDropdown.IOTypes(
                                        CompositeHeaderDiscriminate.Input,
                                        selectedIOType,
                                        selectIOType
                                    )
                                    Html.li [ prop.role "separator" ]
                                    for headerType in BuildingBlockDropdownHelper.MainHeaderTypes do
                                        BuildingBlockDropdown.HeaderDropdownItem(headerType, selectHeaderType)
                                    BuildingBlockDropdown.Submenu(
                                        "More",
                                        [
                                            for headerType in BuildingBlockDropdownHelper.MoreHeaderTypes do
                                                BuildingBlockDropdown.HeaderDropdownItem(headerType, selectHeaderType)
                                            BuildingBlockDropdown.InfoFooter()
                                        ]
                                    )
                                    Html.li [ prop.role "separator" ]
                                    BuildingBlockDropdown.IOTypes(
                                        CompositeHeaderDiscriminate.Output,
                                        selectedIOType,
                                        selectIOType
                                    )
                                    BuildingBlockDropdown.InfoFooter()
                                ]
                            ]
                    )
                ]
        )
