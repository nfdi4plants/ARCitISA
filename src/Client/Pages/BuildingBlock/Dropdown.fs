module BuildingBlock.Dropdown

open Feliz
open Model.BuildingBlock
open Model
open Messages
open ARCtrl
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Shared


[<ReactComponent>]
let FreeTextInputElement (onSubmit: string -> unit) =
    let inputS, setInput = React.useState ""

    Html.div [
        prop.className "swt:flex swt:flex-row swt:gap-0 swt:p-0 swt:join"
        prop.children [
            Html.input [
                prop.placeholder "..."
                prop.className
                    "swt:input swt:input-sm swt:join-item swt:grow swt:truncate swt:rounded-l-(--radius-field)! swt:rounded-r-none!"
                prop.onClick (fun e -> e.stopPropagation ())
                prop.onChange (fun (v: string) -> setInput v)
                prop.onKeyDown (
                    key.enter,
                    fun e ->
                        e.stopPropagation ()
                        onSubmit inputS
                )
            ]
            Html.button [
                prop.className "swt:btn swt:btn-accent swt:btn-sm swt:join-item"
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    onSubmit inputS
                )
                prop.children [ Arcitisa.Components.Primitive.Icons.Check() ]
            ]
        ]
    ]

module private DropdownElements =

    let divider = Html.div [ prop.className "swt:divider swt:mx-2 swt:my-0" ]

    let private annotationsPrinciplesLink =
        Html.a [
            prop.href "#"
            prop.onClick (fun _ ->
                Browser.Dom.window.``open`` (Arcitisa.Components.Shared.URLs.AnnotationPrinciplesUrl, "_blank")
                |> ignore
            )
            prop.className "swt:ml-auto swt:link-info"
            prop.text "info"
        ]

    let createSubBuildingBlockDropdownLink
        (state: BuildingBlockUIState)
        setState
        (subpage: Model.BuildingBlock.DropdownPage)
        =
        Html.li [
            prop.onClick (fun e ->
                e.preventDefault ()
                e.stopPropagation ()
                setState { state with DropdownPage = subpage }
            )
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-row swt:justify-between"
                    prop.children [
                        Html.span subpage.toString
                        Arcitisa.Components.Primitive.Icons.ArrowRight()
                    ]
                ]
            ]
        ]

    /// Navigation element back to main page
    let DropdownContentInfoFooter setState (hasBack: bool) =
        Html.li [
            prop.className "swt:flex swt:flex-row swt:justify-between swt:pt-1"
            prop.onClick (fun e ->
                e.preventDefault ()
                e.stopPropagation ()

                setState {
                    DropdownPage = BuildingBlock.DropdownPage.Main
                    DropdownIsActive = true
                }
            )
            prop.children [
                if hasBack then
                    Html.a [
                        prop.className "swt:content-center"
                        prop.children [ Arcitisa.Components.Primitive.Icons.ArrowLeft() ]
                    ]
                annotationsPrinciplesLink
            ]
        ]

    let createBuildingBlockDropdownItem
        (model: Model)
        (dispatch: Msg -> unit)
        setUiState
        close
        (headerType: CompositeHeaderDiscriminate)
        =
        Html.li [
            Html.a [
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    Helper.selectCompositeHeaderDiscriminate headerType setUiState close dispatch
                )
                prop.onKeyDown (fun k ->
                    if k.code = Arcitisa.Components.kbdEventCode.enter then
                        Helper.selectCompositeHeaderDiscriminate headerType setUiState close dispatch
                )
                prop.text (headerType.ToString())
            ]
        ]

    let createIOTypeDropdownItem
        (model: Model)
        dispatch
        setUiState
        close
        (headerType: CompositeHeaderDiscriminate)
        (iotype: IOType)
        =
        let setIO (ioType) =
            {
                DropdownPage = DropdownPage.Main
                DropdownIsActive = false
            }
            |> setUiState

            close ()

            (headerType, ioType)
            |> BuildingBlock.UpdateHeaderWithIO
            |> BuildingBlockMsg
            |> dispatch

        Html.li [
            match iotype with
            | IOType.FreeText s ->
                let onSubmit =
                    fun (v: string) ->
                        let header = IOType.FreeText v
                        setIO header

                prop.children [ FreeTextInputElement onSubmit ]
            | _ ->
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    setIO iotype
                )

                prop.onKeyDown (fun k ->
                    if k.code = Arcitisa.Components.kbdEventCode.enter then
                        setIO iotype
                )

                prop.children [ Html.div [ prop.text (iotype.ToString()) ] ]
        ]

    /// Main column types subpage for dropdown
    let dropdownContentMain state setState close (model: Model) (dispatch: Msg -> unit) =
        React.Fragment [
            DropdownPage.IOTypes CompositeHeaderDiscriminate.Input
            |> createSubBuildingBlockDropdownLink state setState
            divider
            CompositeHeaderDiscriminate.Parameter
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.Factor
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.Characteristic
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.Component
            |> createBuildingBlockDropdownItem model dispatch setState close
            Model.BuildingBlock.DropdownPage.More
            |> createSubBuildingBlockDropdownLink state setState
            divider
            DropdownPage.IOTypes CompositeHeaderDiscriminate.Output
            |> createSubBuildingBlockDropdownLink state setState
            DropdownContentInfoFooter setState false
        ]

    /// Protocol Type subpage for dropdown
    let dropdownContentProtocolTypeColumns state setState close (model: Model) dispatch =
        React.Fragment [
            CompositeHeaderDiscriminate.Comment
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.Date
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.Performer
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.ProtocolDescription
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.ProtocolREF
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.ProtocolType
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.ProtocolUri
            |> createBuildingBlockDropdownItem model dispatch setState close
            CompositeHeaderDiscriminate.ProtocolVersion
            |> createBuildingBlockDropdownItem model dispatch setState close
            // Navigation element back to main page
            DropdownContentInfoFooter setState true
        ]

    /// Output columns subpage for dropdown
    let dropdownContentIOTypeColumns header state setState close (model: Model) dispatch =
        React.Fragment [
            IOType.Source |> createIOTypeDropdownItem model dispatch setState close header
            IOType.Sample |> createIOTypeDropdownItem model dispatch setState close header
            IOType.Material |> createIOTypeDropdownItem model dispatch setState close header
            IOType.Data |> createIOTypeDropdownItem model dispatch setState close header
            IOType.FreeText ""
            |> createIOTypeDropdownItem model dispatch setState close header
            // Navigation element back to main page
            DropdownContentInfoFooter setState true
        ]

[<ReactComponent>]
let Main (state, setState, model: Model, dispatch: Msg -> unit) =
    let isOpen, setOpen = React.useState false
    let close = fun _ -> setOpen false

    Components.BaseDropdown.Main(
        isOpen,
        setOpen,
        Html.button [
            prop.onClick (fun _ -> setOpen (not isOpen))
            prop.role "button"
            prop.className "swt:btn swt:btn-primary swt:border swt:border-base-content! swt:join-item swt:flex-nowrap"
            prop.children [
                Html.span (model.AddBuildingBlockState.HeaderCellType.ToString())
                Arcitisa.Components.Primitive.Icons.AngleDown()
            ]
        ],
        [
            match state.DropdownPage with
            | Model.BuildingBlock.DropdownPage.Main ->
                DropdownElements.dropdownContentMain state setState close model dispatch
            | Model.BuildingBlock.DropdownPage.More ->
                DropdownElements.dropdownContentProtocolTypeColumns state setState close model dispatch
            | Model.BuildingBlock.DropdownPage.IOTypes iotype ->
                DropdownElements.dropdownContentIOTypeColumns iotype state setState close model dispatch
        ],
        style = Style.init ("swt:join-item swt:dropdown", Map [ "content", Style.init ("swt:min-w-64!") ])
    )
