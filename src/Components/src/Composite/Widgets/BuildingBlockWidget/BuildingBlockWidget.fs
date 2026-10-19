namespace Swate.Components.Composite.Widgets.BuildingBlockWidget

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.AnnotationTable.Context
open Swate.Components.Composite.TermSearch
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.Widgets.Context

module private BuildingBlockWidgetState =

    type Model = {
        HeaderCellType: CompositeHeaderDiscriminate
        HeaderArg: U2<OntologyAnnotation, IOType> option
        BodyCellType: CompositeCellDiscriminate
        BodyArg: U2<string, OntologyAnnotation> option
        CommentHeader: string
    } with

        static member init() = {
            HeaderCellType = CompositeHeaderDiscriminate.Parameter
            HeaderArg = None
            BodyCellType = CompositeCellDiscriminate.Term
            BodyArg = None
            CommentHeader = ""
        }

        member this.TryHeaderOA() =
            match this.HeaderCellType, this.HeaderArg with
            | CompositeHeaderDiscriminate.ProtocolType, _ -> CompositeHeader.ProtocolType.ToTerm() |> Some
            | _, Some(U2.Case1 oa) -> Some oa
            | _ -> None

        member this.TryHeaderIO() =
            match this.HeaderArg with
            | Some(U2.Case2 io) -> Some io
            | _ -> None

        member this.TryBodyOA() =
            match this.BodyArg with
            | Some(U2.Case2 oa) -> Some oa
            | _ -> None

    let isSameMajorCompositeHeaderDiscriminate
        (left: CompositeHeaderDiscriminate)
        (right: CompositeHeaderDiscriminate)
        =
        (left.IsTermColumn() = right.IsTermColumn())
        && (left.HasIOType() = right.HasIOType())

    let createCompositeHeaderFromState (state: Model) =
        let getOA () =
            state.TryHeaderOA() |> Option.defaultValue (OntologyAnnotation.empty ())

        let getIOType () =
            state.TryHeaderIO() |> Option.defaultValue (IOType.FreeText "")

        match state.HeaderCellType with
        | CompositeHeaderDiscriminate.Component -> CompositeHeader.Component(getOA ())
        | CompositeHeaderDiscriminate.Characteristic -> CompositeHeader.Characteristic(getOA ())
        | CompositeHeaderDiscriminate.Factor -> CompositeHeader.Factor(getOA ())
        | CompositeHeaderDiscriminate.Parameter -> CompositeHeader.Parameter(getOA ())
        | CompositeHeaderDiscriminate.ProtocolType -> CompositeHeader.ProtocolType
        | CompositeHeaderDiscriminate.ProtocolDescription -> CompositeHeader.ProtocolDescription
        | CompositeHeaderDiscriminate.ProtocolUri -> CompositeHeader.ProtocolUri
        | CompositeHeaderDiscriminate.ProtocolVersion -> CompositeHeader.ProtocolVersion
        | CompositeHeaderDiscriminate.ProtocolREF -> CompositeHeader.ProtocolREF
        | CompositeHeaderDiscriminate.Performer -> CompositeHeader.Performer
        | CompositeHeaderDiscriminate.Date -> CompositeHeader.Date
        | CompositeHeaderDiscriminate.Input -> CompositeHeader.Input(getIOType ())
        | CompositeHeaderDiscriminate.Output -> CompositeHeader.Output(getIOType ())
        | CompositeHeaderDiscriminate.Comment -> CompositeHeader.Comment state.CommentHeader
        | CompositeHeaderDiscriminate.Freetext -> failwith "Freetext header type is not yet implemented"

    let tryCreateCompositeCellFromState (state: Model) =
        let isDataHeader =
            state.TryHeaderIO() |> Option.exists (fun ioType -> ioType = IOType.Data)

        if isDataHeader then
            CompositeCell.emptyData |> Some
        else
            match state.BodyCellType, state.BodyArg with
            | CompositeCellDiscriminate.Term, Some(U2.Case2 oa) -> CompositeCell.createTerm oa |> Some
            | CompositeCellDiscriminate.Unitized, Some(U2.Case2 oa) -> CompositeCell.createUnitized ("", oa) |> Some
            | CompositeCellDiscriminate.Text, Some(U2.Case1 text) -> CompositeCell.createFreeText text |> Some
            | _ -> None

    let isValidColumn (header: CompositeHeader) =
        header.IsFeaturedColumn
        || (header.IsTermColumn && header.ToTerm().NameText.Length > 0)
        || header.IsSingleColumn

module private BuildingBlockWidgetView =

    let disabledState (message: string) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-80 swt:p-2"
            prop.children [
                Html.h3 [
                    prop.className "swt:text-sm swt:font-semibold"
                    prop.text "Add Building Block"
                ]
                Html.p [
                    prop.className "swt:text-xs swt:opacity-70"
                    prop.text message
                ]
            ]
        ]

[<Erase; Mangle(false)>]
type BuildingBlockWidget =

    [<ReactComponent>]
    static member private HeaderControls
        (
            state: BuildingBlockWidgetState.Model,
            setCommentHeader: string -> unit,
            setHeaderTerm: Term option -> unit,
            setHeaderIOType: CompositeHeaderDiscriminate -> IOType -> unit,
            setHeaderCellType: CompositeHeaderDiscriminate -> unit
        ) =

        let headerInput () =
            if state.HeaderCellType = CompositeHeaderDiscriminate.Comment then
                Html.input [
                    prop.className "swt:input swt:border-current swt:join-item swt:w-full"
                    prop.valueOrDefault state.CommentHeader
                    prop.placeholder "Comment header"
                    prop.onChange setCommentHeader
                ]
            elif state.HeaderCellType.HasOA() then
                TermSearch.TermSearch(
                    (state.TryHeaderOA() |> Option.map (fun oa -> oa.ToTerm())),
                    setHeaderTerm,
                    classNames = TermSearchStyle(U2.Case1 "swt:border-current swt:join-item swt:w-full")
                )
            elif state.HeaderCellType.HasIOType() then
                match state.TryHeaderIO() with
                | Some(IOType.FreeText freeText) ->
                    Html.input [
                        prop.className "swt:input swt:border-current swt:join-item swt:w-full"
                        prop.valueOrDefault freeText
                        prop.placeholder "Input/Output text"
                        prop.onChange (fun (value: string) ->
                            setHeaderIOType state.HeaderCellType (IOType.FreeText value)
                        )
                    ]
                | Some ioType ->
                    Html.input [
                        prop.className "swt:input swt:border-current swt:join-item swt:w-full"
                        prop.readOnly true
                        prop.valueOrDefault (ioType.ToString())
                    ]
                | None -> Html.none
            else
                Html.none

        Html.div [
            prop.testId "building-block-header-controls"
            prop.style [ style.position.relative ]
            prop.children [
                Html.div [
                    prop.className "swt:join swt:w-full"
                    prop.children [
                        BuildingBlockDropdown.Main(
                            state.HeaderCellType,
                            state.TryHeaderIO(),
                            setHeaderCellType,
                            setHeaderIOType
                        )
                        headerInput ()
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private BodyControls
        (
            state: BuildingBlockWidgetState.Model,
            setState: BuildingBlockWidgetState.Model -> unit,
            setBodyTerm: Term option -> unit
        ) =
        if state.HeaderCellType.IsTermColumn() then
            Html.div [
                prop.children [
                    Html.div [
                        prop.className "swt:join swt:w-full"
                        prop.style [ style.position.relative ]
                        prop.children [
                            Html.button [
                                prop.type'.button
                                prop.className [
                                    "swt:btn swt:join-item swt:border swt:border-base-content!"
                                    if state.BodyCellType = CompositeCellDiscriminate.Term then
                                        "swt:btn-primary"
                                    else
                                        "swt:btn-neutral/50"
                                ]
                                prop.text "Term"
                                prop.onClick (fun _ ->
                                    setState {
                                        state with
                                            BodyCellType = CompositeCellDiscriminate.Term
                                    }
                                )
                            ]
                            Html.button [
                                prop.type'.button
                                prop.className [
                                    "swt:btn swt:join-item swt:border swt:border-base-content!"
                                    if state.BodyCellType = CompositeCellDiscriminate.Unitized then
                                        "swt:btn-primary"
                                    else
                                        "swt:btn-neutral/50"
                                ]
                                prop.text "Unit"
                                prop.onClick (fun _ ->
                                    setState {
                                        state with
                                            BodyCellType = CompositeCellDiscriminate.Unitized
                                    }
                                )
                            ]
                            TermSearch.TermSearch(
                                (state.TryBodyOA() |> Option.map (fun oa -> oa.ToTerm())),
                                setBodyTerm,
                                classNames = TermSearchStyle(U2.Case1 "swt:border-current swt:w-full"),
                                ?parentId = (state.TryHeaderOA() |> Option.map (fun oa -> oa.TermAccessionShort))
                            )
                        ]
                    ]
                ]
            ]
        else
            Html.none

    [<ReactComponent(true)>]
    static member Main
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\ARCFileEditor\ArcFileEditor.fs` as well!
        (arcFile: ArcFiles, activeTableIndex: int option, setArcFile: ArcFiles -> unit) =

        let state, setState = React.useState (BuildingBlockWidgetState.Model.init ())

        let annotationCtx = useAnnotationTableStateCtx ()

        let _widgetCtx = useWidgetControllerCtx ()

        match arcFile.TryGetActiveTable(activeTableIndex) with
        | None -> BuildingBlockWidgetView.disabledState "Switch to a table tab to add a building block."
        | Some(_, table) ->
            let selectedColumnIndex =
                annotationCtx.state
                |> Map.tryFind table.Name
                |> Option.bind (fun tableCtx -> tableCtx.SelectedCells)
                |> Option.map (fun selectedCells -> selectedCells.xEnd - 2)

            let setHeaderCellType (nextHeaderType: CompositeHeaderDiscriminate) =
                let nextState =
                    if
                        BuildingBlockWidgetState.isSameMajorCompositeHeaderDiscriminate
                            state.HeaderCellType
                            nextHeaderType
                    then
                        {
                            state with
                                HeaderCellType = nextHeaderType
                        }
                    else
                        let nextBodyCellType =
                            if nextHeaderType.IsTermColumn() then
                                CompositeCellDiscriminate.Term
                            else
                                CompositeCellDiscriminate.Text

                        {
                            state with
                                HeaderCellType = nextHeaderType
                                BodyCellType = nextBodyCellType
                                HeaderArg = None
                                BodyArg = None
                        }

                setState nextState

            let setCommentHeader (value: string) =
                setState { state with CommentHeader = value }

            let setHeaderIOType (headerType: CompositeHeaderDiscriminate) (ioType: IOType) =
                setState {
                    state with
                        HeaderCellType = headerType
                        HeaderArg = Some(U2.Case2 ioType)
                        BodyArg = None
                        BodyCellType = CompositeCellDiscriminate.Text
                }

            let setHeaderTerm (termOpt: Term option) =
                let nextHeaderArg =
                    termOpt |> Option.map (fun term -> term |> OntologyAnnotation.from |> U2.Case1)

                setState { state with HeaderArg = nextHeaderArg }

            let setBodyTerm (termOpt: Term option) =
                let nextBodyArg =
                    termOpt |> Option.map (fun term -> term |> OntologyAnnotation.from |> U2.Case2)

                setState { state with BodyArg = nextBodyArg }

            let addBuildingBlock () =
                let header = BuildingBlockWidgetState.createCompositeHeaderFromState state

                let cells =
                    match BuildingBlockWidgetState.tryCreateCompositeCellFromState state with
                    | Some bodyCell ->
                        let rowCount = System.Math.Max(1, table.RowCount)
                        Array.init rowCount (fun _ -> bodyCell.Copy()) |> ResizeArray
                    | None -> state.HeaderCellType.CreateEmptyDefaultCells table.RowCount

                let cells =
                    match header with
                    | CompositeHeader.Output _ ->
                        match table.TryGetOutputColumn() with
                        | Some outputColumn -> outputColumn.Cells
                        | None -> cells
                    | CompositeHeader.Input _ ->
                        match table.TryGetInputColumn() with
                        | Some inputColumn -> inputColumn.Cells
                        | None -> cells
                    | _ -> cells

                let insertionIndex =
                    match selectedColumnIndex with
                    | Some columnIndex -> System.Math.Min(columnIndex + 1, table.ColumnCount)
                    | None -> table.ColumnCount

                table.AddColumn(header, cells, insertionIndex, true)
                setArcFile (ArcFiles.refreshRef arcFile)

            let header = BuildingBlockWidgetState.createCompositeHeaderFromState state
            let isValid = BuildingBlockWidgetState.isValidColumn header

            Html.div [
                Html.form [
                    prop.className "swt:flex swt:flex-col swt:gap-4 swt:p-2 swt:min-w-80"
                    prop.onSubmit (fun event ->
                        event.preventDefault ()

                        if isValid then
                            addBuildingBlock ()
                    )
                    prop.children [
                        BuildingBlockWidget.HeaderControls(
                            state,
                            setCommentHeader,
                            setHeaderTerm,
                            setHeaderIOType,
                            setHeaderCellType
                        )
                        BuildingBlockWidget.BodyControls(state, setState, setBodyTerm)
                        Html.div [
                            prop.className "swt:flex swt:justify-center"
                            prop.children [
                                Html.button [
                                    prop.testId "building-block-add-column"
                                    prop.type'.submit
                                    prop.className [
                                        "swt:btn swt:btn-wide"
                                        if isValid then "swt:btn-primary" else "swt:btn-error"
                                    ]
                                    prop.disabled (not isValid)
                                    prop.text "Add Column"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
