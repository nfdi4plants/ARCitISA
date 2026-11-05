namespace Swate.Components.Composite.DataMapTable

open Feliz
open Fable.Core
open ARCtrl
open Fable.Core.JsInterop
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Primitive
open Swate.Components.Primitive.BaseModal
open Swate.Components.Primitive.ContextMenu
open Swate.Components.Primitive.ContextMenu.Types
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.Table
open Swate.Components.Composite.Table.Types
open Swate.Components.Composite.DataMapTable.Types
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.AnnotationTable.Context
open Swate.Components.Composite.DataMapTable.Helper

module private DatamapMemoComponents =

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    let InactiveCompositeCell (index: CellCoordinate, cell: CompositeCell, debug: bool) =
        TableCell.CompositeCellInactiveCell(index, cell, debug = debug)

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    let InactiveHeaderCell (index: CellCoordinate, header: string, debug: bool) =
        TableCell.StringInactiveCell(index, header, disableActivation = true, debug = debug)

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    let InactiveYIndexCell (index: CellCoordinate) =
        TableCell.BaseCell(
            index.y,
            index.x,
            Html.text index.y,
            className =
                "swt:rounded-0 swt:px-2 swt:py-1 swt:flex swt:items-center swt:justify-center swt:cursor-not-allowed swt:w-full swt:h-full swt:bg-base-200"
        )

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    let ActiveCompositeCell (index: CellCoordinate, cell: CompositeCell, setCell: CompositeCell -> unit) =
        TableCell.CompositeCellActiveCell(index, cell, setCell)

[<Erase; Mangle(false)>]
type DataMapTable =

    static member SelectionContextKey = "__DATAMAP__"

    [<ReactComponent>]
    static member private ModalDetails
        (index: CellCoordinate, datamap: Datamap, setDatamap, setModal: Modal option -> unit)
        =

        let close = fun () -> setModal None

        let cell = datamap.GetCell(index.x - 1, index.y - 1)

        let tempCell, setTempCell = React.useState (cell)

        let submit =
            fun () ->
                datamap.SetCell(index.x - 1, index.y - 1, tempCell)
                close ()

        let Content =
            match tempCell with
            | CompositeCell.FreeText txt ->
                InputField.Input(
                    txt,
                    (fun input -> setTempCell (CompositeCell.FreeText input)),
                    label = "Free Text",
                    rmv = close,
                    submit = submit,
                    autofocus = true
                )
            | CompositeCell.Term oa ->
                let parentOa = datamap.GetHeader(index.x - 1).TryGetTerm()

                React.Fragment [
                    InputField.TermCombi(
                        oa |> Term.fromOntologyAnnotation |> Some,
                        (fun (term: Term option) ->
                            term
                            |> Option.map OntologyAnnotation.from
                            |> Option.map (fun oa -> CompositeCell.Term oa)
                            |> Option.defaultValue CompositeCell.emptyTerm
                            |> setTempCell
                        ),
                        label = "Term name",
                        rmv = close,
                        submit = submit,
                        autofocus = true,
                        ?parentOa = parentOa
                    )
                    InputField.Input(
                        oa.TermSourceREF |> Option.defaultValue "",
                        (fun input ->
                            let copy = oa.Copy()
                            copy.TermSourceREF <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Term copy)
                        ),
                        label = "Term source REF",
                        rmv = close,
                        submit = submit
                    )
                    InputField.Input(
                        oa.TermAccessionNumber |> Option.defaultValue "",
                        (fun input ->
                            let copy = oa.Copy()
                            copy.TermAccessionNumber <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Term copy)
                        ),
                        label = "Term Accession Number",
                        rmv = close,
                        submit = submit
                    )
                ]
            | CompositeCell.Unitized(v, oa) ->
                let parentOa = datamap.GetHeader(index.x - 1).TryGetTerm()

                React.Fragment [
                    InputField.Input(
                        v,
                        (fun input -> setTempCell (CompositeCell.Unitized(input, oa))),
                        label = "Value",
                        rmv = close,
                        autofocus = true,
                        submit = submit
                    )
                    InputField.TermCombi(
                        oa |> Term.fromOntologyAnnotation |> Some,
                        (fun (term: Term option) ->
                            term
                            |> Option.map OntologyAnnotation.from
                            |> Option.map (fun oa -> CompositeCell.Term oa)
                            |> Option.defaultValue CompositeCell.emptyTerm
                            |> setTempCell
                        ),
                        label = "Unit name",
                        rmv = close,
                        submit = submit,
                        ?parentOa = parentOa
                    )
                    InputField.Input(
                        oa.TermSourceREF |> Option.defaultValue "",
                        (fun input ->
                            let copy = oa.Copy()
                            copy.TermSourceREF <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Term copy)
                        ),
                        label = "Term source REF",
                        rmv = close,
                        submit = submit
                    )
                    InputField.Input(
                        oa.TermAccessionNumber |> Option.defaultValue "",
                        (fun input ->
                            let copy = oa.Copy()
                            copy.TermAccessionNumber <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Term copy)
                        ),
                        label = "Term Accession Number",
                        rmv = close,
                        submit = submit
                    )
                ]
            | CompositeCell.Data data ->
                React.Fragment [
                    InputField.Input(
                        data.FilePath |> Option.defaultValue "",
                        (fun input ->
                            let copy = data.Copy()
                            copy.FilePath <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Data copy)
                        ),
                        label = "File Path",
                        rmv = close,
                        submit = submit,
                        autofocus = true
                    )
                    InputField.Input(
                        data.Selector |> Option.defaultValue "",
                        (fun input ->
                            let copy = data.Copy()
                            copy.Selector <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Data copy)
                        ),
                        label = "Selector",
                        rmv = close,
                        submit = submit,
                        autofocus = true
                    )
                    InputField.Input(
                        data.Format |> Option.defaultValue "",
                        (fun input ->
                            let copy = data.Copy()
                            copy.Format <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Data copy)
                        ),
                        label = "File Format",
                        rmv = close,
                        submit = submit,
                        autofocus = true
                    )
                    InputField.Input(
                        data.SelectorFormat |> Option.defaultValue "",
                        (fun input ->
                            let copy = data.Copy()
                            copy.SelectorFormat <- input |> Option.whereNot System.String.IsNullOrWhiteSpace
                            setTempCell (CompositeCell.Data copy)
                        ),
                        label = "Selector Format",
                        rmv = close,
                        submit = submit,
                        autofocus = true
                    )
                ]


        React.Fragment [
            BaseModal.ModalHeader(Html.div "Data Context Details", close)

            BaseModal.ModalContent(Content)

            BaseModal.ModalFooter(
                React.Fragment [
                    FooterButtons.Cancel(close)
                    FooterButtons.Submit(submit)
                ]
            )
        ]

    [<ReactComponent>]
    static member private Modals(modal: Modal option, setModal: Modal option -> unit, datamap, setDatamap) =

        let isOpen = modal.IsSome
        let setIsOpen = fun (b: bool) -> if b then () else setModal None

        let Content =
            match modal with
            | Some(Modal.Details index) -> DataMapTable.ModalDetails(index, datamap, setDatamap, setModal)
            | None ->
                Html.div [
                    prop.className "swt:alert swt:alert-error swt:font-semibold"
                    prop.text "Unknown pattern. Please report this as a bug. DataMapTable.Modals - no modal selected"
                ]

        BaseModal.BaseModal(isOpen, setIsOpen, children = Content)

    [<ReactComponent(true)>]
    static member DataMapTable
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\ARCFileEditor\ArcFileEditor.fs` as well!
        (datamap: Datamap, setDatamap: Datamap -> unit, ?height, ?debug: bool) =

        let modal, setModal = React.useState (None: Modal option)
        let tableRef = React.useRef<TableHandle> (unbox null)
        let containerRef = React.useElementRef ()
        let defaultDebug = defaultArg debug false
        let annotationTableCtx = useAnnotationTableStateCtx ()
        let hasAnnotationCtx = isNullOrUndefined annotationTableCtx |> not

        let onSelect: GridSelect.OnSelect =
            fun _ range ->
                if hasAnnotationCtx then
                    let nextDataMapCtx: AnnotationTableContext = { SelectedCells = range }

                    let nextData =
                        annotationTableCtx.state.Add(DataMapTable.SelectionContextKey, nextDataMapCtx)

                    annotationTableCtx.setState nextData

        let renderCell =
            (fun (index: CellCoordinate) ->
                match index with
                | _ when index.x > 0 && index.y > 0 ->
                    let cell = datamap.GetCell(index.x - 1, index.y - 1)
                    DatamapMemoComponents.InactiveCompositeCell(index, cell, defaultDebug)
                | _ when index.x > 0 && index.y = 0 ->
                    let header = datamap.GetHeader(index.x - 1).ToString()
                    DatamapMemoComponents.InactiveHeaderCell(index, header, defaultDebug)
                | _ -> DatamapMemoComponents.InactiveYIndexCell(index)
            )

        let renderActiveCell =
            (fun (index: CellCoordinate) ->
                match index with
                | _ when index.x > 0 && index.y > 0 ->
                    let cell = datamap.GetCell(index.x - 1, index.y - 1)

                    let setCell newValue =
                        updateDataMap
                            datamap
                            setDatamap
                            (fun nextDataMap -> nextDataMap.SetCell(index.x - 1, index.y - 1, newValue))

                    DatamapMemoComponents.ActiveCompositeCell(index, cell, setCell)

                | _ when index.x > 0 && index.y = 0 -> Html.div "when index.x > 0 && index.y = 0"
                | _ -> Html.div "unknown table pattern"
            )

        React.Fragment [
            DataMapTable.Modals(modal, setModal, datamap, setDatamap)
            Html.div [
                if debug.IsSome && debug.Value then
                    prop.testId "datamap_table"
                    prop.custom ("data-columncount", datamap.ColumnCount)
                    prop.custom ("data-rowcount", datamap.RowCount)
                prop.className "swt:overflow-auto swt:flex swt:flex-col swt:h-full"
                prop.ref containerRef

                prop.children [
                    DataMapContextMenu.ContextMenu(
                        datamap,
                        setDatamap,
                        setModal,
                        tableRef,
                        containerRef,
                        ?debug = debug
                    )
                    Table.Table(
                        datamap.RowCount + 1,
                        datamap.ColumnCount + 1,
                        renderCell,
                        renderActiveCell,
                        ref = tableRef,
                        ?height = height,
                        onSelect = onSelect,
                        onKeydown =
                            (fun (e, selectedCells, activeCell) ->
                                match GridSelect.tryGetKeyboardShortcut e selectedCells activeCell with
                                | Some GridSelect.KeyboardShortcut.Details ->
                                    let cell = selectedCells.selectedCellsReducedSet.MinimumElement
                                    setModal (Some(Modal.Details cell))
                                | Some GridSelect.KeyboardShortcut.Delete ->
                                    updateDataMap
                                        datamap
                                        setDatamap
                                        _.ClearSelectedCells(tableRef.current.SelectHandle)
                                | Some GridSelect.KeyboardShortcut.Copy ->
                                    e.preventDefault ()

                                    tableRef.current.SelectHandle.getSelectedCells ()
                                    |> copyCells datamap
                                    |> Promise.start
                                | Some GridSelect.KeyboardShortcut.Paste ->
                                    e.preventDefault ()

                                    pasteFromClipboard
                                        datamap
                                        selectedCells.selectedCellsReducedSet.MinimumElement
                                        (tableRef.current.SelectHandle.getSelectedCells () |> Seq.toArray)
                                        setDatamap
                                    |> Promise.start
                                | Some GridSelect.KeyboardShortcut.Cut ->
                                    e.preventDefault ()
                                    let coordinates = tableRef.current.SelectHandle.getSelectedCells () |> Seq.toArray

                                    cutCells datamap coordinates setDatamap |> Promise.start
                                | None -> ()
                            )
                    )
                ]
            ]
        ]


    [<ReactComponent>]
    static member Entry() =

        let datamap, setDatamap =
            React.useState (
                ARCtrl.Datamap(
                    ResizeArray [
                        for i in 0..100 do
                            DataContext(
                                name = sprintf "Name %d" i,
                                dataType = DataFile.RawDataFile,
                                format = sprintf "Format %A" i,
                                selectorFormat = sprintf "Selector %A" i,
                                explication = OntologyAnnotation("Explication", "EXP", "EXP:21309813"),
                                unit = OntologyAnnotation("Unit", "UNIT", "UNIT:0000001"),
                                objectType = OntologyAnnotation("ObjectType", "OT", "OT:0000001"),
                                label = sprintf "Label: %d" i,
                                description = sprintf "Description: %d" i,
                                generatedBy = "Kevin F",
                                comments =
                                    ResizeArray(
                                        [
                                            for i in 0..5 do
                                                Comment(sprintf "Comment %d" i, sprintf "Value %d" i)
                                        ]
                                    )
                            )
                    ]
                )
            )

        DataMapTable.DataMapTable(datamap, setDatamap, 400)
