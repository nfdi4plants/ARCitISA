namespace Swate.Components.Composite.AnnotationTable

open Swate.Components
open Swate.Components.Primitive.ContextMenu
open Swate.Components.Composite.Table
open Swate.Components.Composite.Table.Types
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.AnnotationTable.Types
open Swate.Components.Composite.AnnotationTable.Context
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open ARCtrl


open Types.AnnotationTableContextMenu
open Types.AnnotationTable

module AnnotationTableMemo =

    [<Erase>]
    type CellRender =

        [<ReactComponent>]
        static member CompositeHeaderActiveRender
            (index: CellCoordinate, header: CompositeHeader, setHeader: CompositeHeader -> unit, ?debug, ?key: string)
            =

            match header with
            | CompositeHeader.Input io ->
                TableCell.StringActiveCell(
                    index,
                    io.ToString(),
                    (fun input -> IOType.ofString input |> CompositeHeader.Input |> setHeader),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Output io ->
                TableCell.StringActiveCell(
                    index,
                    io.ToString(),
                    (fun input -> IOType.ofString input |> CompositeHeader.Output |> setHeader),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Comment txt ->
                TableCell.StringActiveCell(
                    index,
                    txt,
                    (fun input -> setHeader (CompositeHeader.Comment input)),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.FreeText txt ->
                TableCell.StringActiveCell(
                    index,
                    txt,
                    (fun input -> setHeader (CompositeHeader.FreeText input)),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Parameter oa ->
                TableCell.OntologyAnnotationActiveCell(
                    index,
                    oa,
                    (fun t -> setHeader (CompositeHeader.Parameter t)),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Characteristic oa ->
                TableCell.OntologyAnnotationActiveCell(
                    index,
                    oa,
                    (fun t -> setHeader (CompositeHeader.Characteristic t)),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Component oa ->
                TableCell.OntologyAnnotationActiveCell(
                    index,
                    oa,
                    (fun t -> setHeader (CompositeHeader.Component t)),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Factor oa ->
                TableCell.OntologyAnnotationActiveCell(
                    index,
                    oa,
                    (fun t -> setHeader (CompositeHeader.Factor t)),
                    isStickyHeader = true,
                    ?debug = debug
                )
            | CompositeHeader.Performer
            | CompositeHeader.ProtocolDescription
            | CompositeHeader.ProtocolREF
            | CompositeHeader.ProtocolType
            | CompositeHeader.ProtocolUri
            | CompositeHeader.ProtocolVersion
            | CompositeHeader.Date ->
                TableCell.StringActiveCell(
                    index,
                    header.ToString(),
                    (fun input ->
                        let nextHeader = CompositeHeader.OfHeaderString input
                        setHeader nextHeader
                    ),
                    isStickyHeader = true,
                    ?debug = debug
                )


    // let cellRender =
    //     React.memo (
    //         (fun (index: CellCoordinate, compositeCell: U2<CompositeCell, CompositeHeader> option, debug: bool option) ->
    //             match compositeCell with
    //             | None ->
    //                 TableCell.BaseCell(
    //                     index.y,
    //                     index.x,
    //                     Html.text index.y,
    //                     className =
    //                         "swt:rounded-0 swt:px-2 swt:py-1 swt:flex swt:items-center swt:justify-center swt:cursor-not-allowed swt:w-full swt:h-full swt:bg-base-200",
    //                     ?debug = debug
    //                 )
    //             | Some(U2.Case2 header) ->
    //                 let text = header.ToString()
    //                 TableCell.StringInactiveCell(index, text, ?debug = debug)
    //             | Some(U2.Case1 cell) -> TableCell.CompositeCellInactiveCell(index, cell, ?debug = debug)
    //         ),
    //         arePropsEqual =
    //             fun
    //                 (index: CellCoordinate, cell: U2<CompositeCell, CompositeHeader> option, debug: bool option)
    //                 (nextIndex, nextCell, nextDebug) ->
    //                 index = nextIndex
    //                 && debug = nextDebug
    //                 && match cell, nextCell with
    //                    | Some(U2.Case1 c1), Some(U2.Case1 c2) -> c1 = c2
    //                    | Some(U2.Case2 h1), Some(U2.Case2 h2) -> h1 = h2
    //                    | None, None -> true
    //                    | _ -> false
    //     )

    let RenderIndexCell =
        React.memo (
            (fun
                (props:
                    {|
                        row: int
                        column: int
                        debug: bool option
                    |}) ->
                TableCell.BaseCell(
                    props.row,
                    props.column,
                    Html.text (string props.row),
                    className =
                        "swt:rounded-0 swt:px-2 swt:py-1 swt:flex swt:items-center swt:justify-center swt:cursor-not-allowed swt:w-full swt:h-full swt:bg-base-200",
                    ?debug = props.debug
                )
            ),
            areEqual =
                (fun props nextProps ->
                    props.row = nextProps.row
                    && props.column = nextProps.column
                    && props.debug = nextProps.debug
                )
        )

    let RenderHeaderCell =
        React.memo (
            (fun
                (props:
                    {|
                        index: CellCoordinate
                        header: string
                        debug: bool option
                    |}) -> TableCell.StringInactiveCell(props.index, props.header, ?debug = props.debug)
            ),
            areEqual =
                (fun props nextProps ->
                    props.index = nextProps.index
                    && props.header = nextProps.header
                    && props.debug = nextProps.debug
                )
        )

    let RenderBodyCell =
        React.memo (
            (fun
                (props:
                    {|
                        index: CellCoordinate
                        cell: CompositeCell
                        debug: bool option
                    |}) -> TableCell.CompositeCellInactiveCell(props.index, props.cell, ?debug = props.debug)
            ),
            areEqual =
                (fun props nextProps ->
                    props.index = nextProps.index
                    && props.cell = nextProps.cell
                    && props.debug = nextProps.debug
                )
        )

    // let activeCellRender =
    //     React.memo (
    //         (fun (index: CellCoordinate) ->
    //             match index with
    //             | _ when index.x > 0 && index.y > 0 ->
    //                 let setCell =
    //                     fun (cell: CellCoordinate) (cc: CompositeCell) ->
    //                         let nextTable = arcTable |> ArcTable.setCellAt (cell.x - 1, cell.y - 1, cc)
    //                         setArcTable nextTable

    //                 let cell = arcTable.GetCellAt(index.x - 1, index.y - 1)

    //                 let header =
    //                     arcTable.Headers[index.x - 1].ToTerm()
    //                     |> _.TermAccessionShort
    //                     |> Option.whereNot System.String.IsNullOrWhiteSpace

    //                 TableCell.CompositeCellActiveCell(
    //                     index,
    //                     cell,
    //                     setCell index,
    //                     ?parentId = header,
    //                     ?debug = debug,
    //                     key = $"cell-{index.x}-{index.y}"
    //                 )
    //             | _ when index.x > 0 && index.y = 0 ->
    //                 let x = index.x - 1

    //                 let setHeader =
    //                     fun (ch: CompositeHeader) ->
    //                         let nextTable = arcTable |> ArcTable.updateHeader (x, ch)
    //                         setArcTable nextTable

    //                 let header = arcTable.Headers[x]

    //                 AnnotationTable.CompositeHeaderActiveRender(
    //                     index,
    //                     header,
    //                     setHeader,
    //                     ?debug = debug,
    //                     key = $"header-{x}"
    //                 )
    //             | _ -> Html.div "Unknown cell type"
    //         )
    //     )
    let RenderActiveBodyCell =
        React.memo (
            (fun
                (props:
                    {|
                        index: CellCoordinate
                        cell: CompositeCell
                        setCell: CompositeCell -> unit
                        debug: bool option
                    |}) ->
                TableCell.CompositeCellActiveCell(props.index, props.cell, props.setCell, ?debug = props.debug)
            ),
            areEqual =
                (fun props nextProps ->
                    props.index = nextProps.index
                    && props.cell = nextProps.cell
                    && props.debug = nextProps.debug
                )
        )

    let RenderActiveHeaderCell =
        React.memo (
            (fun
                (props:
                    {|
                        index: CellCoordinate
                        header: CompositeHeader
                        setHeader: CompositeHeader -> unit
                        debug: bool option
                    |}) ->
                CellRender.CompositeHeaderActiveRender(
                    props.index,
                    props.header,
                    props.setHeader,
                    ?debug = props.debug,
                    key = $"header-{props.index.x}"
                )
            ),
            areEqual =
                (fun props nextProps ->
                    props.index = nextProps.index
                    && props.header = nextProps.header
                    && props.debug = nextProps.debug
                )
        )

[<Mangle(false); Erase>]
type AnnotationTable =

    [<ReactComponent>]
    static member private ContextMenu
        (
            arcTable: ArcTable,
            setArcTable,
            tableRef: IRefValue<TableHandle>,
            containerRef,
            setModal: ModalTypes option -> unit,
            ?debug: bool
        ) =

        ContextMenu.ContextMenu(
            (fun data ->
                let index = data |> unbox<CellCoordinate>

                if index.x = 0 && index.y > 0 then // index col
                    AnnotationTableContextMenu.IndexColumnContent(
                        index.y,
                        arcTable,
                        setArcTable,
                        tableRef.current.SelectHandle
                    )
                elif index.y = 0 then // header Row
                    AnnotationTableContextMenu.CompositeHeaderContent(
                        index.x,
                        arcTable,
                        setArcTable,
                        tableRef.current.SelectHandle,
                        setModal
                    )
                else // standard cell
                    AnnotationTableContextMenu.CompositeCellContent(
                        {| x = index.x; y = index.y |},
                        arcTable,
                        setArcTable,
                        tableRef.current.SelectHandle,
                        setModal
                    )
            ),
            ref = containerRef,
            onSpawn =
                (fun e ->
                    let target = e.target :?> Browser.Types.HTMLElement

                    match target.closest ("[data-row][data-column]"), containerRef.current with
                    | Some cell, Some container when container.contains (cell) ->
                        let cell = cell :?> Browser.Types.HTMLElement
                        let row = cell.getAttribute ("data-row") |> int
                        let col = cell.getAttribute ("data-column") |> int
                        let indices: CellCoordinate = {| y = row; x = col |}
                        Some indices
                    | _ -> None
                ),
            ?debug = debug
        )

    [<ReactComponent>]
    static member private ModalController
        (
            arcTable: ArcTable,
            setArcTable,
            modal: AnnotationTable.ModalTypes option,
            setModal,
            tableRef: IRefValue<TableHandle>,
            ?debug: bool
        ) =

        let rmv =
            fun _ ->
                tableRef.current.focus ()
                setModal None

        React.Fragment [
            match modal with
            | None -> Html.none
            | Some(ModalTypes.Details cc) ->
                if cc.x = 0 then // no details modal for index col
                    Html.none
                elif cc.y = 0 then // headers
                    let header = arcTable.Headers.[cc.x - 1]

                    let setHeader =
                        fun (newHeader: CompositeHeader) ->
                            try
                                arcTable.UpdateHeader(cc.x - 1, newHeader)
                                setArcTable arcTable
                            with exn ->
                                let exnMessage =
                                    if
                                        exn.Message.StartsWith(
                                            "Tried setting header for column with invalid type of cells."
                                        )
                                    then
                                        "Your change does not work with Details. Use \"Edit\" instead."
                                    else
                                        exn.Message

                                setModal (Some(ModalTypes.Error exnMessage))
                                failwith exn.Message

                    CompositeCellModal.CompositeHeaderModal(header, setHeader, rmv)

                else
                    let cell = arcTable.GetCellAt(cc.x - 1, cc.y - 1)

                    let setCell =
                        fun (cell: CompositeCell) ->
                            arcTable.SetCellAt(cc.x - 1, cc.y - 1, cell)
                            setArcTable arcTable

                    let header = arcTable.Headers.[cc.x - 1]

                    CompositeCellModal.CompositeCellModal(cell, setCell, rmv, header, ?debug = debug)
            | Some(ModalTypes.Transform cc) ->
                if cc.x = 0 then // no details modal for index col
                    Html.none
                elif cc.y = 0 then // headers
                    let cell = arcTable.GetCellAt(cc.x - 1, cc.y - 1)

                    let setCell =
                        fun (cell: CompositeCell) ->
                            arcTable.SetCellAt(cc.x - 1, cc.y - 1, cell)
                            setArcTable arcTable

                    let header = arcTable.Headers.[cc.x - 1]

                    CompositeCellEditModal.CompositeCellTransformModal(cell, header, setCell, rmv)
                else
                    let cell = arcTable.GetCellAt(cc.x - 1, cc.y - 1)

                    let setCell =
                        fun (cell: CompositeCell) ->
                            arcTable.SetCellAt(cc.x - 1, cc.y - 1, cell)
                            setArcTable arcTable

                    let header = arcTable.Headers.[cc.x - 1]

                    CompositeCellEditModal.CompositeCellTransformModal(cell, header, setCell, rmv)
            | Some(ModalTypes.Edit cc) ->
                if cc.x = 0 then // no details modal for index col
                    Html.none
                elif cc.y = 0 then // headers
                    EditConfig.CompositeCellEditModal(cc.x - 1, arcTable, setArcTable, rmv, ?debug = debug)
                else
                    EditConfig.CompositeCellEditModal(cc.x - 1, arcTable, setArcTable, rmv, ?debug = debug)
            | Some(ModalTypes.MoveColumn(uiTableIndex, arcTableIndex)) ->
                ContextMenuModals.MoveColumnModal(
                    arcTable,
                    setArcTable,
                    arcTableIndex,
                    uiTableIndex,
                    setModal,
                    tableRef,
                    ?debug = debug
                )

            | Some(ModalTypes.PasteCaseUserInput(PasteCases.AddColumns addColumns, selectHandle: SelectHandle)) ->
                ContextMenuModals.PasteFullColumnsModal(
                    arcTable,
                    setArcTable,
                    addColumns,
                    selectHandle,
                    setModal,
                    tableRef
                )
            | Some(ModalTypes.Error(exn)) -> ContextMenuModals.ErrorModal(exn, setModal, tableRef)
            | Some(ModalTypes.UnknownPasteCase(PasteCases.Unknown unknownPasteCase)) ->
                ContextMenuModals.UnknownPasteCase(unknownPasteCase.data, unknownPasteCase.headers, setModal, tableRef)
            | anyElse ->
                console.warn ("Unknown modal type", anyElse)
                Html.none
        ]


    [<ReactComponent(true)>]
    static member AnnotationTable
        (
            arcTable: ArcTable,
            setArcTable: ArcTable -> unit,
            ?height: int,
            ?debug: bool,
            ?onCellSelect: GridSelect.OnSelect,
            ?tableRef: IRefValue<TableHandle>,
            ?key: string
        ) =
        let containerRef = React.useElementRef ()
        let tableRefInner = React.useRef<TableHandle> (null)
        let (modal: ModalTypes option), setModal = React.useState None
        let ctx = useAnnotationTableStateCtx ()

        let hasCtx = isNullOrUndefined ctx |> not

        let tableRef = if tableRef.IsSome then tableRef.Value else tableRefInner

        // TODO: Add table to ctx on effect, and remove on unmount
        // Does currently not work, as the disposable is not correctly setup in feliz, will require feliz v3 to fix
        React.useEffectOnce (fun _ ->
            { new System.IDisposable with
                member _.Dispose() = ()
            }
        )

        let setArcTable = fun (newTable: ArcTable) -> setArcTable newTable

        let onSelect: GridSelect.OnSelect =
            fun latest range ->

                if hasCtx then
                    let nextTable: AnnotationTableContext = { SelectedCells = range }
                    let nextData = ctx.state.Add(arcTable.Name, nextTable)
                    ctx.setState nextData

                onCellSelect |> Option.iter (fun f -> f latest range)

        Html.div [
            prop.ref containerRef
            if debug.IsSome && debug.Value then
                prop.testId "annotation_table"
                prop.custom ("data-columncount", arcTable.ColumnCount)
                prop.custom ("data-rowcount", arcTable.RowCount)
            prop.className "swt:overflow-auto swt:flex swt:flex-col swt:h-full"
            prop.children [
                ReactDOM.createPortal ( // Modals
                    AnnotationTable.ModalController(arcTable, setArcTable, modal, setModal, tableRef, ?debug = debug),
                    Browser.Dom.document.body
                )
                AnnotationTable.ContextMenu(arcTable, setArcTable, tableRef, containerRef, setModal, ?debug = debug)
                Table.Table(
                    rowCount = arcTable.RowCount + 1,
                    columnCount = arcTable.ColumnCount + 1,
                    renderCell =
                        (fun (index: CellCoordinate) ->
                            if index.x = 0 then
                                React.memoRender (
                                    AnnotationTableMemo.RenderIndexCell,
                                    {|
                                        row = index.y
                                        column = index.x
                                        debug = debug
                                    |},
                                    withKey = (fun props -> $"index-{props.row}- {props.column}")
                                )
                            elif index.y = 0 then
                                React.memoRender (
                                    AnnotationTableMemo.RenderHeaderCell,
                                    {|
                                        index = index
                                        header = arcTable.Headers.[index.x - 1].ToString()
                                        debug = debug
                                    |},
                                    withKey = (fun props -> $"header-{props.index.x}- {props.header.GetHashCode()}")
                                )
                            else
                                React.memoRender (
                                    AnnotationTableMemo.RenderBodyCell,
                                    {|
                                        index = index
                                        cell = arcTable.GetCellAt(index.x - 1, index.y - 1)
                                        debug = debug
                                    |},
                                    withKey =
                                        (fun props ->
                                            $"cell-{props.index.x}-{props.index.y}- {props.cell.GetHashCode()}"
                                        )
                                )
                        ),
                    renderActiveCell =
                        (fun (index: CellCoordinate) ->

                            if index.y = 0 then
                                let x = index.x - 1

                                let setHeader =
                                    fun (ch: CompositeHeader) ->
                                        let nextTable = arcTable |> ArcTable.updateHeader (x, ch)
                                        setArcTable nextTable

                                let header = arcTable.Headers.[x]

                                React.memoRender (
                                    AnnotationTableMemo.RenderActiveHeaderCell,
                                    {|
                                        index = index
                                        header = header
                                        setHeader = setHeader
                                        debug = debug
                                    |},
                                    withKey =
                                        (fun props -> $"header-active-{props.index.x}- {props.header.GetHashCode()}")
                                )
                            else
                                let cell = arcTable.GetCellAt(index.x - 1, index.y - 1)

                                let setCell =
                                    fun (cc: CompositeCell) ->
                                        let nextTable = arcTable |> ArcTable.setCellAt (index.x - 1, index.y - 1, cc)
                                        setArcTable nextTable

                                React.memoRender (
                                    AnnotationTableMemo.RenderActiveBodyCell,
                                    {|
                                        index = index
                                        cell = cell
                                        setCell = setCell
                                        debug = debug
                                    |},
                                    withKey =
                                        (fun props ->
                                            $"cell-active-{props.index.x}-{props.index.y}- {props.cell.GetHashCode()}"
                                        )
                                )
                        ),
                    ref = tableRef,
                    ?height = height,
                    onKeydown =
                        (fun (e, selectedCells, activeCell) ->

                            match GridSelect.tryGetKeyboardShortcut e selectedCells activeCell with
                            | Some GridSelect.KeyboardShortcut.Details ->
                                let cell = selectedCells.selectedCellsReducedSet.MinimumElement
                                setModal (Some(ModalTypes.Details cell))
                            | Some GridSelect.KeyboardShortcut.Delete ->
                                arcTable.ClearSelectedCells(tableRef.current.SelectHandle)
                                arcTable.Copy() |> setArcTable
                            | Some GridSelect.KeyboardShortcut.Paste ->
                                AnnotationTableContextMenuUtil.tryPasteCopiedCells (
                                    selectedCells.selectedCellsReducedSet.MinimumElement,
                                    arcTable,
                                    tableRef.current.SelectHandle,
                                    setModal,
                                    setArcTable
                                )
                                |> Promise.start
                            | Some GridSelect.KeyboardShortcut.Copy ->
                                AnnotationTableContextMenuUtil.copy (
                                    selectedCells.selectedCellsReducedSet.MinimumElement,
                                    arcTable,
                                    tableRef.current.SelectHandle
                                )
                                |> Promise.start
                            | Some GridSelect.KeyboardShortcut.Cut ->
                                AnnotationTableContextMenuUtil.cut (
                                    selectedCells.selectedCellsReducedSet.MinimumElement,
                                    arcTable,
                                    setArcTable,
                                    tableRef.current.SelectHandle
                                )
                                |> Promise.start
                            | _ -> ()
                        ),
                    enableColumnHeaderSelect = true,
                    onSelect = onSelect,
                    ?debug = debug
                )
            ]
        ]

    static member Entry() =
        let arcTable = ARCtrl.ArcTable("TestTable", ResizeArray())

        let ctx = useAnnotationTableStateCtx ()

        arcTable.AddColumn(
            CompositeHeader.Input IOType.Source,
            [|
                for i in 0..100 do
                    CompositeCell.createFreeText $"Source {i}"
            |]
            |> ResizeArray
        )

        arcTable.AddColumn(
            CompositeHeader.Component(OntologyAnnotation("instrument model", "MS", "MS:1000031")),
            [|
                for i in 0..100 do
                    CompositeCell.createTermFromString ("SCIEX instrument model", "MS", "MS:11111231")
            |]
            |> ResizeArray
        )

        arcTable.AddColumn(
            CompositeHeader.Parameter(OntologyAnnotation("Temperature", "UO", "UO:123435345")),
            [|
                for i in 0..100 do
                    CompositeCell.createUnitizedFromString (string i, "Degree Celsius", "UO", "UO:000000001")
            |]
            |> ResizeArray
        )

        arcTable.AddColumn(
            CompositeHeader.Output IOType.Data,
            [|
                for i in 0..100 do
                    let newData =
                        Data.create (
                            string i,
                            $"Sample {i}",
                            DataFile.RawDataFile,
                            "Some Format",
                            $"Selector Format {i}",
                            ResizeArray[Comment.create ("Test", string i)]
                        )

                    CompositeCell.createData (newData)
            |]
            |> ResizeArray
        )

        let table, setTable = React.useState (arcTable)

        React.Fragment [
            AnnotationTable.AnnotationTable(table, setTable, height = 600)
            Html.div [
                prop.textf
                    "%A"
                    (ctx.state
                     |> Map.tryFind table.Name
                     |> Option.bind _.SelectedCells
                     |> Option.map (fun x -> sprintf "%i - %i, %i - %i" x.xStart x.xEnd x.yStart x.yEnd))
            ]
        ]
