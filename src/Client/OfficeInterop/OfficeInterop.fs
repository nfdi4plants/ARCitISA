module OfficeInterop.Core

open Fable.Core
open ExcelJS.Fable
open Excel
open GlobalBindings

open Arcitisa.Components.Shared
open Arcitisa.Components.Shared.DTOs
open Database

open OfficeInterop

open ARCtrl
open ARCtrl.Spreadsheet

open System

open ARCtrlHelper
open ExcelHelper
open ArcTableHelper

[<AutoOpen>]
module GetHandler =

    /// <summary>
    /// Select a building block, shifted by adaptedIndex from the selected building block
    /// </summary>
    /// <param name="table"></param>
    /// <param name="adaptedIndex"></param>
    /// <param name="context"></param>
    let getAdaptedSelectedBuildingBlock (table: Table) (adaptedIndex: float) (context: RequestContext) = promise {

        let selectedRange =
            context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "columnIndex" |]))

        let headerRange = table.getHeaderRowRange ()

        let _ =
            headerRange.load (U2.Case2(ResizeArray [| "columnIndex"; "values"; "columnCount" |]))
            |> ignore

        return!
            context
                .sync()
                .``then`` (fun _ ->
                    let rebasedIndex =
                        selectedRange.columnIndex - headerRange.columnIndex + adaptedIndex |> int

                    if rebasedIndex < 0 || rebasedIndex >= (int headerRange.columnCount) then
                        failwith "Cannot select building block outside of annotation table!"

                    let headers: string[] = [|
                        for v in headerRange.values.[0] do
                            v.Value :?> string
                    |]

                    let selectedHeader = rebasedIndex, headers.[rebasedIndex]
                    let buildingBlockGroups = groupToBuildingBlocks headers

                    let selectedBuildingBlock =
                        buildingBlockGroups.Find(fun bb -> bb.Contains selectedHeader)

                    selectedBuildingBlock
                )
    }

    /// <summary>
    /// Returns a ResizeArray of indices and header names for the selected building block
    /// The indices are rebased to the excel annotation table.
    /// </summary>
    /// <param name="columns"></param>
    /// <param name="selectedIndex"></param>
    let getBuildingBlockByColumnIndex (table: Table) (excelColumnIndex: float) (context: RequestContext) = promise {
        let headerRange = table.getHeaderRowRange ()

        let _ =
            headerRange.load (U2.Case2(ResizeArray [| "columnIndex"; "values"; "columnCount" |]))
            |> ignore

        return!
            context
                .sync()
                .``then`` (fun _ ->
                    let rebasedIndex = excelColumnIndex - headerRange.columnIndex |> int

                    if rebasedIndex < 0 || rebasedIndex >= (int headerRange.columnCount) then
                        failwith "Cannot select building block outside of annotation table!"

                    let headers: string[] = [|
                        for v in headerRange.values.[0] do
                            v.Value :?> string
                    |]

                    let selectedHeader = rebasedIndex, headers.[rebasedIndex]
                    let buildingBlockGroups = groupToBuildingBlocks headers

                    let selectedBuildingBlock =
                        buildingBlockGroups.Find(fun bb -> bb.Contains selectedHeader)

                    selectedBuildingBlock
                )
    }

    /// <summary>
    /// Get the main column of the arc table of the selected building block of the active annotation table
    /// </summary>
    let getArcIndex (excelTable: Table) (excelColumnIndex: float) (context: RequestContext) = promise {
        let! selectedBlock = getBuildingBlockByColumnIndex excelTable excelColumnIndex context

        let protoHeaders = excelTable.getHeaderRowRange ()
        let _ = protoHeaders.load (U2.Case2(ResizeArray([ "values" ])))

        do! context.sync ()

        let headers = getHeaders protoHeaders.values

        let arcTableIndices =
            (groupToBuildingBlocks headers)
            |> Array.ofSeq
            |> Array.map (fun i -> i |> Array.ofSeq)

        let arcTableIndex =
            let potResult =
                arcTableIndices
                |> Array.mapi (fun i c -> i, c |> Array.tryFind (fun (_, s) -> s = snd selectedBlock.[0]))

            let result =
                potResult
                |> Array.filter (fun (_, c) -> c.IsSome)
                |> Array.map (fun (i, c) -> i, c.Value)

            Array.tryHead result

        let arcTableIndex =
            if arcTableIndex.IsSome then
                fst arcTableIndex.Value
            else
                failwith "Could not find a fitting arc table index"

        return arcTableIndex
    }

    /// <summary>
    /// Returns a ResizeArray of indices and header names for the selected building block
    /// The indices are rebased to the excel annotation table.
    /// </summary>
    /// <param name="columns"></param>
    /// <param name="selectedIndex"></param>
    let getSelectedCompositeColumn (table: Table) (context: RequestContext) = promise {
        let selectedRange =
            context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "columnIndex" |]))

        let headerRange = table.getHeaderRowRange ()

        let _ =
            headerRange.load (U2.Case2(ResizeArray [| "columnIndex"; "values"; "columnCount" |]))
            |> ignore

        return!
            context
                .sync()
                .``then`` (fun _ ->
                    let rebasedIndex = selectedRange.columnIndex - headerRange.columnIndex |> int

                    if rebasedIndex < 0 || rebasedIndex >= (int headerRange.columnCount) then
                        failwith "Cannot select building block outside of annotation table!"

                    let headers: string[] = [|
                        for v in headerRange.values.[0] do
                            v.Value :?> string
                    |]

                    let selectedHeader = rebasedIndex, headers.[rebasedIndex]
                    let buildingBlockGroups = groupToBuildingBlocks headers

                    let selectedBuildingBlock =
                        buildingBlockGroups.Find(fun bb -> bb.Contains selectedHeader)

                    selectedBuildingBlock
                )
    }

    /// <summary>
    /// Returns a ResizeArray of indices and header names for the selected building block
    /// The indices are rebased to the excel annotation table.
    /// </summary>
    /// <param name="columns"></param>
    /// <param name="selectedIndex"></param>
    let getSelectedBuildingBlockCell (table: Table) (context: RequestContext) = promise {

        let selectedRange =
            context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "columnIndex"; "rowIndex" |]))

        let headerRange = table.getHeaderRowRange ()

        let _ =
            headerRange.load (U2.Case2(ResizeArray [| "columnCount"; "columnIndex"; "rowIndex"; "values" |]))
            |> ignore

        return!
            context
                .sync()
                .``then`` (fun _ ->
                    let rebasedIndex = selectedRange.columnIndex - headerRange.columnIndex |> int

                    if rebasedIndex < 0 || rebasedIndex >= (int headerRange.columnCount) then
                        failwith "Cannot select building block outside of annotation table!"

                    let headers: string[] = [|
                        for v in headerRange.values.[0] do
                            v.Value :?> string
                    |]

                    let selectedHeader = rebasedIndex, headers.[rebasedIndex]
                    let buildingBlockGroups = groupToBuildingBlocks headers

                    let selectedBuildingBlock =
                        buildingBlockGroups.Find(fun bb -> bb.Contains selectedHeader)

                    selectedBuildingBlock, selectedRange.rowIndex
                )
    }

    /// <summary>
    /// Get the main column of the arc table of the selected building block of the active annotation table
    /// </summary>
    let getArcMainColumn (excelTable: Table) (arcTable: ArcTable) (context: RequestContext) = promise {
        let! selectedBlock = getSelectedCompositeColumn excelTable context

        let protoHeaders = excelTable.getHeaderRowRange ()
        let _ = protoHeaders.load (U2.Case2(ResizeArray([ "values" ])))

        do! context.sync ()

        let headers = getHeaders protoHeaders.values

        let arcTableIndices =
            (groupToBuildingBlocks headers)
            |> Array.ofSeq
            |> Array.map (fun i -> i |> Array.ofSeq)

        let arcTableIndex =
            let potResult =
                arcTableIndices
                |> Array.mapi (fun i c -> i, c |> Array.tryFind (fun (_, s) -> s = snd selectedBlock.[0]))

            let result =
                potResult
                |> Array.filter (fun (_, c) -> c.IsSome)
                |> Array.map (fun (i, c) -> i, c.Value)

            Array.tryHead result

        let arcTableIndex, columnName =
            if arcTableIndex.IsSome then
                fst arcTableIndex.Value, snd (snd arcTableIndex.Value)
            else
                failwith "Could not find a fitting arc table index"

        let targetColumn =
            let potColumn = arcTable.GetColumn arcTableIndex

            if columnName.Contains(potColumn.Header.ToString()) then
                potColumn
            else
                failwith "Could not find a fitting arc table index with matchin name"

        return (targetColumn, arcTableIndex)
    }

    /// <summary>
    /// Get the deselected column indices of the given table index
    /// </summary>
    /// <param name="deselectedColumns"></param>
    /// <param name="tableIndex"></param>
    let getDeselectedTableColumnIndices (deselectedColumns: Set<int * int>) (tableIndex: int) =
        deselectedColumns
        |> List.ofSeq
        |> List.choose (fun item -> if (fst item) = tableIndex then Some(snd item) else None)

    /// <summary>
    /// Sort the tables and selected columns based on the selected import type
    /// </summary>
    /// <param name="tablesToAdd"></param>
    /// <param name="selectedColumnsCollection"></param>
    /// <param name="importTables"></param>
    let selectTablesToAdd (tablesToAdd: ArcTable[]) (importTables: FileImport.ImportTable list) =

        let importData =
            importTables
            |> List.map (fun importTable -> tablesToAdd.[importTable.Index], importTable.FullImport)

        let mutable tablesToAdd, tablesToJoin = list.Empty, list.Empty

        importData
        |> List.iter (fun (table, importType) ->
            if importType then
                tablesToAdd <- table :: tablesToAdd
            else
                tablesToJoin <- table :: tablesToJoin
        )

        tablesToAdd |> Array.ofList, tablesToJoin |> Array.ofList

    /// <summary>
    /// Tries to select the selected building block of the selected table
    /// </summary>
    /// <param name="table"></param>
    /// <param name="context"></param>
    let tryGetSelectedTableIndex (table: Table) (context: RequestContext) = promise {
        let selectedRange =
            context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "columnIndex" |]))

        let headerRange = table.getHeaderRowRange ()

        let _ =
            headerRange.load (U2.Case2(ResizeArray [| "columnIndex"; "columnCount" |]))
            |> ignore

        return!
            context
                .sync()
                .``then`` (fun _ ->
                    let rebasedIndex = selectedRange.columnIndex - headerRange.columnIndex |> int

                    if rebasedIndex < 0 || rebasedIndex >= (int headerRange.columnCount) then
                        None
                    else
                        Some rebasedIndex
                //failwith "Cannot select building block outside of annotation table!"
                )
    }

    /// <summary>
    /// Validate the selected columns
    /// </summary>
    /// <param name="excelTable"></param>
    /// <param name="selectedIndex"></param>
    /// <param name="targetIndex"></param>
    /// <param name="context"></param>
    let validateSelectedColumns
        (headerRange: ExcelJS.Fable.Excel.Range)
        (bodyRowRange: ExcelJS.Fable.Excel.Range)
        (selectedIndex: int)
        (targetIndex: int)
        (context: RequestContext)
        =
        promise {
            let _ =
                headerRange.load (U2.Case2(ResizeArray [| "columnIndex"; "values"; "columnCount" |]))
                |> ignore

                bodyRowRange.load (U2.Case2(ResizeArray [| "values"; "numberFormat" |]))
                |> ignore

            let! inMemoryTable =
                context
                    .sync()
                    .``then`` (fun _ ->

                        let getRange (ti: int) (si: int) (range: array<'a>) =
                            if ti < si then range.[ti..si] else range.[si..ti]

                        let headerRow =
                            headerRange.values.[0]
                            |> Array.ofSeq
                            |> getRange targetIndex selectedIndex
                            |> Seq.map (fun item ->
                                item |> Option.map string |> Option.defaultValue "" |> (fun s -> s.TrimEnd())
                            )

                        let bodyRows =
                            bodyRowRange.values
                            |> Seq.map (fun items ->
                                items
                                |> Array.ofSeq
                                |> getRange targetIndex selectedIndex
                                |> Seq.map (fun item -> item |> Option.map string |> Option.defaultValue "")
                            )

                        validate headerRow bodyRows
                    )

            return inMemoryTable
        }

    /// <summary>
    /// Validate the selected building block and those next to it
    /// </summary>
    /// <param name="excelTable"></param>
    /// <param name="context"></param>
    let validateBuildingBlock (excelTable: Table) (context: RequestContext) = promise {
        let columns = excelTable.columns
        let selectedRange = context.workbook.getSelectedRange ()

        let _ =
            columns.load (propertyNames = U2.Case2(ResizeArray [| "count"; "items"; "name" |]))
            |> ignore

            selectedRange.load (U2.Case2(ResizeArray [| "values"; "columnIndex" |]))

        do! context.sync().``then`` (fun _ -> ())

        //Get first selected column
        let columnIndex = selectedRange.columnIndex
        let selectedColumns = columns.items |> Array.ofSeq
        let selectedColumn = selectedColumns.[int columnIndex]

        let isMainColumn =
            CompositeHeader.Cases
            |> Array.exists (fun (_, header) -> selectedColumn.name.StartsWith(header))

        let headerRange = excelTable.getHeaderRowRange ()
        let bodyRowRange = excelTable.getDataBodyRange ()

        let mutable errors: list<exn * string> = []

        //When the selected column is main column, check the property columns for problems
        //When the selected column is not a main column, try find the next main column and start checking from there
        if isMainColumn then
            let! selectedBuildingBlock = getSelectedCompositeColumn excelTable context
            let targetIndex = fst (selectedBuildingBlock.Item(selectedBuildingBlock.Count - 1))
            let! result = validateSelectedColumns headerRange bodyRowRange (int columnIndex) targetIndex context

            errors <- result |> List.ofArray

        if columnIndex > 0 && errors.IsEmpty then
            let! selectedBuildingBlock = getAdaptedSelectedBuildingBlock excelTable -1. context
            let targetIndex = selectedBuildingBlock |> Array.ofSeq

            let! result =
                validateSelectedColumns
                    headerRange
                    bodyRowRange
                    (fst targetIndex.[0])
                    (fst targetIndex.[targetIndex.Length - 1])
                    context

            result |> Array.iter (fun r -> errors <- r :: errors)

        if columnIndex < columns.count && errors.IsEmpty then
            let! selectedBuildingBlock = getAdaptedSelectedBuildingBlock excelTable 1. context
            let targetIndex = selectedBuildingBlock |> Array.ofSeq

            let! result =
                validateSelectedColumns
                    headerRange
                    bodyRowRange
                    (fst targetIndex.[0])
                    (fst targetIndex.[targetIndex.Length - 1])
                    context

            result |> Array.iter (fun r -> errors <- r :: errors)

        return (Array.ofList errors)
    }

    /// <summary>
    /// Get term information from database based on names
    /// </summary>
    /// <param name="names"></param>
    let searchTermInDatabase name = promise {
        let term = TermQuery.create (name, searchMode = Database.FullTextSearch.Exact)
        let! results = Async.StartAsPromise(Api.ontology.searchTerm term)
        let result = Array.tryHead results
        return result
    }

    /// <summary>
    /// Get term informations from database based on names
    /// </summary>
    /// <param name="names"></param>
    let searchTermsInDatabase names = promise {
        let terms =
            names
            |> List.map (fun name -> TermQuery.create (name, searchMode = Database.FullTextSearch.Exact))
            |> Array.ofSeq

        let! result = Async.StartAsPromise(Api.ontology.searchTerms terms)
        return result |> Array.map (fun item -> Array.tryHead item.results)
    }

    /// <summary>
    /// Get all annotation tables of the excel file
    /// </summary>
    /// <param name="context"></param>
    let getExcelAnnotationTables (context: RequestContext) = promise {
        let _ = context.workbook.load (propertyNames = U2.Case2(ResizeArray [| "tables" |]))

        do! context.sync ()

        let tables = context.workbook.tables
        let _ = tables.load (propertyNames = U2.Case2(ResizeArray [| "items"; "name" |]))

        do! context.sync ()

        let! annotationTables = context.sync().``then`` (fun _ -> getAnnotationTables tables)

        let inMemoryTables = new ResizeArray<ArcTable>()
        let mutable msgs = List.Empty

        for i in 0 .. annotationTables.Length - 1 do
            let! tableRes = getValidatedExcelTable annotationTables.[i] context

            match tableRes with
            | Result.Ok table ->
                let! resultRes = ArcTable.fromExcelTable (table, context)

                match resultRes with
                | Result.Ok result -> inMemoryTables.Add(result)
                | Result.Error message ->
                    let msg = InteropLogging.Msg.create InteropLogging.Error message.Message
                    msgs <- msg :: msgs
            | Result.Error messages -> messages |> List.iter (fun message -> msgs <- message :: msgs)

        match msgs.Length with
        | 0 -> return Result.Ok inMemoryTables
        | _ -> return Result.Error msgs
    }

    /// <summary>
    /// Checks whether the first selected cell is within the range of an annotation table or not
    /// </summary>
    /// <param name="tableRange"></param>
    /// <param name="selectedRange"></param>
    /// <param name="context"></param>
    let isSelectedOutsideAnnotationTable
        (tableRange: Excel.Range)
        (selectedRange: Excel.Range)
        (context: RequestContext)
        =
        promise {
            let _ =
                tableRange.load (U2.Case2(ResizeArray [| "columnCount"; "rowCount" |]))
                |> ignore

                selectedRange.load (U2.Case2(ResizeArray [| "columnIndex"; "rowIndex" |]))

            do! context.sync ()

            let tableMaxRow = tableRange.rowCount
            let tableMaxColumn = tableRange.columnCount

            let selectedRow = selectedRange.rowIndex + 1.
            let selectedColumn = selectedRange.columnIndex + 1.

            if (selectedRow > tableMaxRow) || (selectedColumn > tableMaxColumn) then
                return true
            else
                return false
        }


[<AutoOpen>]
module UpdateHandler =

    /// <summary>
    /// Add new tamplate tables and worksheets
    /// </summary>
    /// <param name="tablesToAdd"></param>
    /// <param name="selectedColumnsCollection"></param>
    /// <param name="options"></param>
    /// <param name="context"></param>
    let addTemplates
        (tablesToAdd: ArcTable[])
        (deselectedColumnsCollection: Set<int * int>)
        (options: TableJoinOptions option)
        (context: RequestContext)
        =
        promise {
            let! result = tryGetActiveExcelTable context

            for tableIndex in 0 .. tablesToAdd.Length - 1 do
                let tableToAdd = tablesToAdd.[tableIndex]

                let deselectedColumnIndices =
                    getDeselectedTableColumnIndices deselectedColumnsCollection tableIndex

                let newName = createNewTableName ()
                let originTable = ArcTable.init (newName)

                let finalTable =
                    let endTable =
                        Table.selectiveTablePrepare originTable tableToAdd deselectedColumnIndices

                    originTable.Join(endTable, ?joinOptions = options)
                    originTable

                let! activeWorksheet =
                    if tableIndex = 0 && result.IsNone then
                        promise { return context.workbook.worksheets.getActiveWorksheet () }
                    else
                        promise {
                            let! workSheetName = getNewActiveWorkSheetName tableToAdd.Name context
                            let newWorkSheet = context.workbook.worksheets.add (workSheetName)
                            newWorkSheet.activate ()
                            return newWorkSheet
                        }

                let tableValues = finalTable.ToStringSeqs()

                let range =
                    activeWorksheet.getRangeByIndexes (
                        0,
                        0,
                        float (finalTable.RowCount + 1),
                        (tableValues.Item 0).Count
                    )

                range.values <- finalTable.ToStringSeqs()

                let newTable = activeWorksheet.tables.add (U2.Case1 range, true)
                newTable.name <- finalTable.Name
                newTable.style <- TableStyleLight

                do! format (newTable, context, true)

            let templateNames = tablesToAdd |> Array.map (fun item -> item.Name)

            return [
                InteropLogging.Msg.create InteropLogging.Info $"Added templates {templateNames}!"
            ]
        }

    /// <summary>
    /// Prepare the given table to be joined with the currently active annotation table
    /// </summary>
    /// <param name="tableToAdd"></param>
    let prepareTemplateInMemoryForExcel
        (table: Table)
        (tableToAdd: ArcTable)
        (deselectedColumnIndices: int list)
        (context: RequestContext)
        =
        promise {
            let! originTableRes = ArcTable.fromExcelTable (table, context)

            match originTableRes with
            | Result.Error _ -> return failwith $"Failed to create arc table for table {table.name}"
            | Result.Ok originTable ->
                let finalTable =
                    Table.selectiveTablePrepare originTable tableToAdd deselectedColumnIndices

                let selectedRange = context.workbook.getSelectedRange ()
                let tableStartIndex = table.getRange ()

                let _ =
                    tableStartIndex.load (propertyNames = U2.Case2(ResizeArray [| "columnIndex" |]))
                    |> ignore

                    selectedRange.load (propertyNames = U2.Case2(ResizeArray [| "columnIndex" |]))

                // sync with proxy objects after loading values from excel
                do! context.sync().``then`` (fun _ -> ())

                let targetIndex =
                    let adaptedStartIndex = selectedRange.columnIndex - tableStartIndex.columnIndex

                    if adaptedStartIndex > float (originTable.ColumnCount) then
                        originTable.ColumnCount
                    else
                        int adaptedStartIndex + 1

                return finalTable, Some(targetIndex)
        }

    /// <summary>
    /// Convert the body of the given column into free text
    /// </summary>
    /// <param name="column"></param>
    let convertToFreeTextCell (arcTable: ArcTable) (columnIndex: int) (column: CompositeColumn) rowIndex =
        let freeTextCell = column.Cells.[rowIndex].ToFreeTextCell()
        arcTable.UpdateCellAt(columnIndex, rowIndex, freeTextCell, true)

    /// <summary>
    /// Convert the body of the given column into data
    /// </summary>
    /// <param name="column"></param>
    let convertToDataCell (arcTable: ArcTable) (columnIndex: int) (column: CompositeColumn) rowIndex =
        let header = column.Header
        let dataCell = column.Cells.[rowIndex].ToDataCell()

        let newHeader =
            match header with
            | header when header.isOutput -> CompositeHeader.Output IOType.Data
            | header when header.isInput -> CompositeHeader.Input IOType.Data
            | _ -> CompositeHeader.FreeText(header.ToString())

        arcTable.UpdateHeader(columnIndex, newHeader, false)
        arcTable.UpdateCellAt(columnIndex, rowIndex, dataCell, true)

    /// <summary>
    /// Convert the body of the given column into unit
    /// </summary>
    /// <param name="column"></param>
    let convertToUnitCell (column: CompositeColumn) rowIndex =
        let unitCell = column.Cells.[rowIndex].ToUnitizedCell()
        column.Cells.[rowIndex] <- unitCell

    /// <summary>
    /// Convert the body of the given column into term
    /// </summary>
    /// <param name="column"></param>
    let convertToTermCell (column: CompositeColumn) rowIndex =
        let termCell = column.Cells.[rowIndex].ToTermCell()
        column.Cells.[rowIndex] <- termCell

    /// <summary>
    /// Convert selected building block
    /// </summary>
    /// <param name="excelTable"></param>
    /// <param name="arcTable"></param>
    /// <param name="propertyColumns"></param>
    /// <param name="indexedTerms"></param>
    let updateSelectedBuildingBlocks
        (excelTable: Table)
        (arcTable: ArcTable)
        (propertyColumns: array<int * string[]>)
        (indexedTerms: list<int * Term option>)
        =
        promise {
            let headers = ARCtrl.Spreadsheet.ArcTable.helperColumnStrings |> Array.ofSeq

            for pi in 0 .. propertyColumns.Length - 1 do
                let pIndex, pcv = propertyColumns.[pi]
                let values = Array.create (arcTable.RowCount + 1) ""

                indexedTerms
                |> List.iter (fun (mainIndex, potTerm) ->
                    match potTerm with
                    | Some term ->
                        match pcv.[0] with
                        | header when header = headers.[2] -> //Unit
                            values.[mainIndex] <- term.Name
                        | header when header.Contains(headers.[0]) -> //Term Source REF
                            values.[mainIndex] <- term.FK_Ontology
                        | header when header.Contains(headers.[1]) -> //Term Accession Number
                            values.[mainIndex] <- term.Accession
                        | _ -> ()
                    | None -> values.[mainIndex] <- pcv.[mainIndex]

                    let bodyValues =
                        values
                        |> Array.map (box >> Some)
                        |> Array.map (fun c -> ResizeArray[c])
                        |> ResizeArray

                    excelTable.columns.items.[pIndex].values <- bodyValues
                )
        }

    /// <summary>
    /// Try to get the values of top level meta data from excel worksheet
    /// </summary>
    /// <param name="identifier"></param>
    /// <param name="parseToMetadata"></param>
    let parseToTopLevelMetadata<'T>
        identifier
        (parseToMetadata: string option seq seq -> 'T)
        (context: RequestContext)
        =
        promise {
            let workSheet = context.workbook.worksheets.getItem (identifier)
            let range = workSheet.getUsedRange true

            let _ =
                workSheet.load (propertyNames = U2.Case2(ResizeArray [| "name" |])) |> ignore

                range.load (
                    propertyNames =
                        U2.Case2(
                            ResizeArray["values"
                                        "rowCount"]
                        )
                )

            do! context.sync ()

            if range.rowCount > 1 then

                let values =
                    range.values
                    |> Seq.map (fun row ->
                        row
                        |> Seq.map (fun column ->
                            //Have to check for empty strings becaue the parsing to arc file type causes problems with empty strings
                            if column.IsSome && not (String.IsNullOrEmpty(column.ToString())) then
                                Some(column.Value.ToString())
                            else
                                None
                        )
                    )

                return Some(parseToMetadata values)
            else
                return None
        }

    /// <summary>
    /// Deletes the data contained in the selected worksheet and fills it afterwards with the given new data
    /// </summary>
    /// <param name="context"></param>
    /// <param name="fsWorkSheet"></param>
    /// <param name="seqOfSeqs"></param>
    let updateWorkSheet (context: RequestContext) (worksheetName: string) (seqOfSeqs: seq<seq<string option>>) = promise {
        let rowCount = seqOfSeqs |> Seq.length
        let columnCount = seqOfSeqs |> Seq.map Seq.length |> Seq.max

        let worksheet0 = context.workbook.worksheets.getItemOrNullObject (worksheetName)

        let _ = worksheet0.load (U2.Case2(ResizeArray [| "isNullObject" |]))
        do! context.sync ()

        let worksheet =
            if worksheet0.isNullObject then
                context.workbook.worksheets.add (worksheetName)
            else
                worksheet0

        do! context.sync ()

        let range = worksheet.getUsedRange true
        let _ = range.load (propertyNames = U2.Case2(ResizeArray["values"]))
        do! context.sync ()

        range.values <- null

        do! context.sync ()

        let range = worksheet.getRangeByIndexes (0, 0, rowCount, columnCount)
        let _ = range.load (propertyNames = U2.Case2(ResizeArray["values"]))

        do! context.sync ()

        let values = convertSeqToExcelRangeInput (seqOfSeqs)

        range.values <- values

        range.format.autofitColumns ()
        range.format.autofitRows ()

        do! context.sync ()

        return worksheet
    }

    /// <summary>
    /// Add new rows to the end of the given table
    /// </summary>
    /// <param name="table"></param>
    /// <param name="tableRowCount"></param>
    /// <param name="tableColumnCount"></param>
    /// <param name="targetRowCount"></param>
    /// <param name="context"></param>
    let expandTableRowCount
        (table: Table)
        (tableRowCount: int)
        (tableColumnCount: int)
        (targetRowCount: int)
        (context: RequestContext)
        =
        promise {
            let diff = targetRowCount - tableRowCount

            Table.addRows -1. table tableColumnCount diff "" |> ignore

            do! context.sync ()
        }

    /// <summary>
    /// Insert the ontology information in the selected range independent of an annotation table
    /// </summary>
    /// <param name="selectedRange"></param>
    /// <param name="ontology"></param>
    /// <param name="context"></param>
    let insertOntology (selectedRange: Excel.Range) (ontology: OntologyAnnotation) (context: RequestContext) = promise {
        let _ =
            selectedRange.load (U2.Case2(ResizeArray [| "columnCount"; "rowCount"; "values" |]))

        do! context.sync ()

        let selectedRowCount = int selectedRange.rowCount
        let selectedColumnCount = int selectedRange.columnCount

        let selectedValues = selectedRange.values

        for rowIndex in 0 .. selectedRowCount - 1 do
            for columnIndex in 0 .. selectedColumnCount - 1 do
                match columnIndex % 3 with
                | 0 -> selectedValues.[rowIndex].[columnIndex] <- (Option.map box ontology.Name)
                | 1 -> selectedValues.[rowIndex].[columnIndex] <- (Option.map box ontology.TermSourceREF)
                | 2 ->
                    selectedValues.[rowIndex].[columnIndex] <-
                        (ontology.TermAccessionOntobeeUrl
                         |> Option.whereNot String.IsNullOrWhiteSpace
                         |> Option.map box)
                | _ -> ()

        selectedRange.values <- selectedValues

        selectedRange.format.autofitColumns ()
        selectedRange.format.autofitRows ()

        do! context.sync ()
    }

    /// <summary>
    /// This function is used to create a new annotation table. 'isDark' refers to the current styling of excel (darkmode, or not).
    /// </summary>
    /// <param name="isDark"></param>
    /// <param name="tryUseLastOutput"></param>
    /// <param name="range"></param>
    /// <param name="context"></param>
    let createAtRange (isDark: bool) (tryUseLastOutput: bool) (range: Excel.Range) (context: RequestContext) =

        let newName = createNewTableName ()

        /// decide table style by input parameter
        let style = if isDark then "TableStyleMedium15" else TableStyleLight

        // The next part loads relevant information from the excel objects and allows us to access them after 'context.sync()'
        let tableRange = range.getColumn (0)

        let _ =
            tableRange.load (
                U2.Case2(
                    ResizeArray(
                        [
                            "rowIndex"
                            "columnIndex"
                            "rowCount"
                            "address"
                            "isEntireColumn"
                            "worksheet"
                        ]
                    )
                )
            )

        let mutable activeSheet = tableRange.worksheet
        let _ = activeSheet.load (U2.Case2(ResizeArray [| "tables" |]))
        let activeTables = activeSheet.tables.load (propertyNames = U2.Case1 "items")

        let r = context.runtime.load (U2.Case1 "enableEvents")

        //Required because a new tablerange is required for the new table range
        let mutable hasCreatedNewWorkSheet = false

        promise {
            // sync with proxy objects after loading values from excel
            do!
                context
                    .sync()
                    .``then`` (fun _ ->

                        // Filter all names of tables on the active worksheet for names starting with "annotationTable".
                        let annoTables = getAnnotationTables activeTables

                        match annoTables.Length with
                        //Create a new annotation table in the active worksheet
                        | 0 -> ()
                        //Create a mew worksheet with a new annotation table when the active worksheet already contains one
                        | x when x = 1 ->
                            //Create new worksheet and set it active
                            let worksheet = context.workbook.worksheets.add ()
                            worksheet.activate ()
                            activeSheet <- worksheet
                            hasCreatedNewWorkSheet <- true

                        // Fail the function if there are more than 1 annotation table in the active worksheet.
                        // This check is done, to only have one annotationTable per workSheet.
                        | x when x > 1 ->
                            failwith
                                "The active worksheet contains more than one annotationTable. This should not happen. Please report this as a bug to the developers."
                        | _ ->
                            failwith
                                "The active worksheet contains a negative number of annotation tables. Obviously this cannot happen. Please report this as a bug to the developers."
                    )

            let! prevArcTable = ArcTable.tryGetPrevArcTable context

            // Is user input signals to try and find+reuse the output from the previous annotationTable do this, otherwise just return empty array
            let! prevTableOutput =
                if (tryUseLastOutput) then
                    tryGetPrevTableOutput prevArcTable
                else
                    promise { return None }

            let _ =
                activeSheet.load (propertyNames = U2.Case2(ResizeArray [| "name" |])) |> ignore

            let newTableRange =
                if hasCreatedNewWorkSheet then
                    let rowCount =
                        match prevTableOutput with
                        | Some rows -> float rows.Length
                        | None -> tableRange.rowCount

                    activeSheet
                        .getRangeByIndexes(tableRange.rowIndex, tableRange.columnIndex, rowCount, 1)
                        .load (
                            U2.Case2(
                                ResizeArray(
                                    [
                                        "rowIndex"
                                        "columnIndex"
                                        "rowCount"
                                        "address"
                                        "isEntireColumn"
                                        "worksheet"
                                    ]
                                )
                            )
                        )
                else
                    tableRange

            // sync with proxy objects after loading values from excel
            let! table =
                context
                    .sync()
                    .``then`` (fun _ ->

                        r.enableEvents <- false

                        // Create table in current worksheet
                        let inMemoryTable = ArcTable.init (newName)

                        let newCells =
                            match prevTableOutput with
                            | Some rows -> rows |> Array.map CompositeCell.FreeText
                            | None -> Array.init (int tableRange.rowCount - 1) (fun _ -> CompositeCell.emptyFreeText)

                        inMemoryTable.AddColumn(CompositeHeader.Input IOType.Source, newCells |> ResizeArray)

                        let tableStrings = inMemoryTable.ToStringSeqs()

                        let excelTable = activeSheet.tables.add (U2.Case1 newTableRange, true)

                        // Update annotationTable name
                        excelTable.name <- newName

                        newTableRange.values <- tableStrings

                        // Update annotationTable style
                        excelTable.style <- style
                        excelTable
                    )

            let _ = table.rows.load (propertyNames = U2.Case2(ResizeArray [| "count" |]))

            let logging =
                InteropLogging.Msg.create
                    InteropLogging.Info
                    (sprintf
                        "Annotation Table created in [%s] with dimensions 2c x (%.0f + 1h)r."
                        newTableRange.address
                        (newTableRange.rowCount - 1.))

            return (table, logging)
        }

    /// <summary>
    /// Joins an exceltable and an arctable in excel
    /// </summary>
    /// <param name="excelTable"></param>
    /// <param name="arcTable"></param>
    /// <param name="templateName"></param>
    /// <param name="context"></param>
    let joinArcTablesInExcel
        (excelTable: Table)
        (arcTable: ArcTable)
        (templateName: string option)
        (context: RequestContext)
        =
        promise {
            let newTableRange = excelTable.getRange ()

            let _ = newTableRange.load (propertyNames = U2.Case2(ResizeArray["rowCount"]))

            do! context.sync().``then`` (fun _ -> excelTable.delete ())

            let! (newTable, _) = createAtRange false false newTableRange context

            let _ =
                newTable.load (
                    propertyNames =
                        U2.Case2(
                            ResizeArray["name"
                                        "values"
                                        "columns"]
                        )
                )

            let tableSeqs = arcTable.ToStringSeqs()

            if templateName.IsSome then
                let! workSheetName = getNewActiveWorkSheetName templateName.Value context
                let activeWorksheet = context.workbook.worksheets.getActiveWorksheet ()
                let _ = activeWorksheet.load (propertyNames = U2.Case2(ResizeArray["name"]))

                do! context.sync ()

                activeWorksheet.name <- workSheetName

            do!
                context
                    .sync()
                    .``then`` (fun _ ->
                        let headerNames =
                            let names = getHeaders tableSeqs
                            names |> Array.map (fun name -> extendName names name)

                        headerNames
                        |> Array.iteri (fun i header ->
                            Table.addColumn i newTable header (int newTableRange.rowCount) "" |> ignore
                        )
                    )

            let bodyRange = newTable.getDataBodyRange ()

            let _ =
                bodyRange.load (
                    propertyNames =
                        U2.Case2(
                            ResizeArray["columnCount"
                                        "rowCount"
                                        "values"]
                        )
                )

            do!
                context
                    .sync()
                    .``then`` (fun _ ->

                        //We delete the annotation table because we cannot overwrite an existing one
                        //As a result we create a new annotation table that has one column
                        //We delete the newly created column of the newly created table
                        newTable.columns.getItemAt(bodyRange.columnCount - 1.).delete ()
                    )

            let newBodyRange = newTable.getDataBodyRange ()

            let _ =
                newTable.columns.load (
                    propertyNames =
                        U2.Case2(
                            ResizeArray["name"
                                        "items"]
                        )
                )
                |> ignore

                newBodyRange.load (
                    propertyNames =
                        U2.Case2(
                            ResizeArray["name"
                                        "columnCount"
                                        "values"]
                        )
                )

            do! context.sync ()

            let bodyValues = getBody tableSeqs
            newBodyRange.values <- bodyValues

            do! format (newTable, context, true)
        }

    /// <summary>
    /// Join selected template tables after preperation with active table
    /// </summary>
    /// <param name="originTable"></param>
    /// <param name="excelTable"></param>
    /// <param name="tablesToJoin"></param>
    /// <param name="selectedColumnsCollection"></param>
    /// <param name="options"></param>
    /// <param name="context"></param>
    let joinTemplatesToTable
        (originTable: ArcTable)
        (excelTable: Table)
        tablesToJoin
        selectedColumnsCollection
        options
        (context: RequestContext)
        =
        promise {
            let selectedRange = context.workbook.getSelectedRange ()
            let tableStartIndex = excelTable.getRange ()

            let _ =
                tableStartIndex.load (propertyNames = U2.Case2(ResizeArray [| "columnIndex" |]))
                |> ignore

                selectedRange.load (propertyNames = U2.Case2(ResizeArray [| "columnIndex" |]))

            do! context.sync ()

            let targetIndex =
                let adaptedStartIndex = selectedRange.columnIndex - tableStartIndex.columnIndex

                if adaptedStartIndex > float (originTable.ColumnCount) then
                    originTable.ColumnCount
                else
                    int adaptedStartIndex + 1

            //Loop over all tables to be added and add them to the origin table
            let rec loop
                (originTable: ArcTable)
                (tablesToAdd: ArcTable[])
                (deselectedColumns: Set<int * int>)
                (options: TableJoinOptions option)
                i
                =
                let tableToAdd = tablesToAdd.[i]
                let deselectedColumnIndices = getDeselectedTableColumnIndices deselectedColumns i

                let refinedTableToAdd =
                    let temp = Table.distinctByHeader originTable tableToAdd
                    Table.selectiveTablePrepare originTable temp deselectedColumnIndices

                let newTable =
                    if i > 0 then
                        originTable.Join(refinedTableToAdd, ?joinOptions = options, forceReplace = true)
                        originTable
                    else
                        refinedTableToAdd

                if i = tablesToAdd.Length - 1 then
                    newTable
                else
                    loop newTable tablesToAdd deselectedColumns options (i + 1)

            let processedJoinTable =
                loop originTable tablesToJoin selectedColumnsCollection options 0

            let finalTable =
                Table.selectiveTablePrepare originTable processedJoinTable List.empty

            originTable.Join(finalTable, targetIndex, ?joinOptions = options, forceReplace = true)

            do! joinArcTablesInExcel excelTable originTable None context

            let templateNames = tablesToJoin |> Array.map (fun item -> item.Name)

            return [
                InteropLogging.Msg.create
                    InteropLogging.Info
                    $"Joined templates {templateNames} to table {excelTable.name}!"
            ]
        }

type Main =

    /// <summary>
    /// Get metadata of active table.
    /// </summary>
    /// <param name="context0"></param>
    static member getTableMetaData(?context0) =
        excelRunWith context0
        <| fun context -> promise {
            let! excelTable = tryGetActiveExcelTable context

            if excelTable.IsNone then
                failwith "Error. No active table found!"

            let excelTable = excelTable.Value
            let rowRange = excelTable.getRange ()
            let headerRange = excelTable.getHeaderRowRange ()

            let _ =
                headerRange.load (U2.Case2(ResizeArray([ "address"; "columnCount"; "rowCount" ])))
                |> ignore

                rowRange.load (U2.Case2(ResizeArray([ "address"; "columnCount"; "rowCount" ])))
                |> ignore

                excelTable.columns.load (propertyNames = U2.Case1 "count") |> ignore
                excelTable.rows.load (propertyNames = U2.Case1 "count") |> ignore

            let! res =
                context
                    .sync()
                    .``then`` (fun _ ->
                        let colCount, rowCount = excelTable.columns.count, excelTable.rows.count

                        let rowRangeAddr, rowRangeColCount, rowRangeRowCount =
                            rowRange.address, rowRange.columnCount, rowRange.rowCount

                        let headerRangeAddr, headerRangeColCount, headerRangeRowCount =
                            headerRange.address, headerRange.columnCount, headerRange.rowCount

                        "info",
                        sprintf
                            "Table Metadata: [Table] : %ic x %ir ; [TotalRange] : %s : %ic x %ir ; [HeaderRowRange] : %s : %ic x %ir "
                            (colCount |> int)
                            (rowCount |> int)
                            (rowRangeAddr.Replace("Sheet", ""))
                            (rowRangeColCount |> int)
                            (rowRangeRowCount |> int)
                            (headerRangeAddr.Replace("Sheet", ""))
                            (headerRangeColCount |> int)
                            (headerRangeRowCount |> int)
                    )

            return res
        }

    /// <summary>
    /// Delete the annotation block of the selected column in excel.
    /// </summary>
    /// <param name="context0"></param>
    static member removeSelectedAnnotationBlock(?context0) =
        excelRunWith context0
        <| fun context -> promise {

            let! excelTableRes = tryGetActiveExcelTable context

            match excelTableRes with
            | Some excelTable ->
                let! selectedBuildingBlock = getSelectedCompositeColumn excelTable context

                // iterate DESCENDING to avoid index shift
                for i, _ in Seq.sortByDescending fst selectedBuildingBlock do
                    let column = excelTable.columns.getItemAt (i)
                    column.delete ()

                do! context.sync ()

                do! format (excelTable, context, true)

                return [
                    InteropLogging.Msg.create
                        InteropLogging.Info
                        $"The building block associated with column {snd (selectedBuildingBlock.Item 0)} has been deleted"
                ]
            | None -> return [ InteropLogging.NoActiveTableMsg ]
        }

    /// <summary>
    /// Reads all excel information and returns ArcFiles object, with metadata and tables.
    /// </summary>
    /// <param name="getTables"></param>
    /// <param name="context0"></param>
    static member tryParseToArcFile(?getTables, ?context0) =
        let getTables = defaultArg getTables true

        excelRunWith context0
        <| fun context -> promise {
            let _ =
                context.workbook.load (propertyNames = U2.Case2(ResizeArray [| "worksheets" |]))

            do! context.sync ()
            let worksheets = context.workbook.worksheets

            let _ =
                worksheets.load (propertyNames = U2.Case2(ResizeArray [| "items"; "name" |]))

            do! context.sync ()

            let worksheetTopLevelMetadata =
                worksheets.items |> Seq.tryFind (fun item -> isTopLevelMetadataSheet item.name)

            match worksheetTopLevelMetadata with
            | Some worksheet when ArcAssay.isMetadataSheetName worksheet.name ->
                let! assay = parseToTopLevelMetadata worksheet.name ArcAssay.fromMetadataCollection context

                match assay with
                | Some assay when getTables ->
                    let! tableCollection = getExcelAnnotationTables context

                    match tableCollection with
                    | Result.Ok tables ->
                        assay.Tables <- (tables)
                        let result = ArcFiles.Assay assay
                        return Result.Ok result
                    | Result.Error msgs -> return Result.Error msgs
                | Some assay -> return ArcFiles.Assay assay |> Result.Ok
                | None ->
                    return
                        Result.Error [
                            InteropLogging.Msg.create InteropLogging.Error $"No top level metadata sheet is available!"
                        ]
            | Some worksheet when ArcInvestigation.isMetadataSheetName worksheet.name ->
                let! investigation =
                    parseToTopLevelMetadata worksheet.name ArcInvestigation.fromMetadataCollection context

                match investigation with
                | Some investigation ->
                    let result = ArcFiles.Investigation investigation
                    return Result.Ok result
                | None ->
                    return
                        Result.Error [
                            InteropLogging.Msg.create InteropLogging.Error $"No top level metadata sheet is available!"
                        ]
            | Some worksheet when ArcStudy.isMetadataSheetName worksheet.name ->
                let! studyCollection = parseToTopLevelMetadata worksheet.name ArcStudy.fromMetadataCollection context

                match studyCollection with
                | Some(study, assays) when getTables ->
                    let! tableCollection = getExcelAnnotationTables context

                    match tableCollection with
                    | Result.Ok tables ->
                        study.Tables <- tables
                        let result = ArcFiles.Study(study, assays)
                        return Result.Ok result
                    | Result.Error msgs -> return Result.Error msgs
                | Some(study, assays) -> return ArcFiles.Study(study, assays) |> Result.Ok
                | None ->
                    return
                        Result.Error [
                            InteropLogging.Msg.create InteropLogging.Error $"No top level metadata sheet is available!"
                        ]
            | Some worksheet when Template.isMetadataSheetName worksheet.name ->
                let! result = parseToTopLevelMetadata worksheet.name Template.fromMetadataCollection context

                match result with
                | Some(templateInfo, ers, tags, authors) when getTables ->
                    let! tableCollection = getExcelAnnotationTables context

                    let mkTemplate table =
                        Template.fromParts templateInfo ers tags authors table DateTime.Now
                        |> ArcFiles.Template

                    match tableCollection with
                    | Result.Ok tables when tables.Count = 1 ->
                        let table = tables.[0]
                        let template = mkTemplate table
                        return Result.Ok template
                    | Result.Ok tables when tables.Count > 1 -> //when not exactly 1 table is found throw
                        return
                            Result.Error [
                                InteropLogging.Msg.create
                                    InteropLogging.Error
                                    $"Only one annotation table is allowed for an arc template but this one has {tables.Count} tables!"
                            ]
                    | Result.Error _
                    | Result.Ok _ ->
                        let table = ArcTable.init ("New Template Table")
                        let template = mkTemplate table
                        return Result.Ok template
                | Some(templateInfo, ers, tags, authors) ->
                    let template =
                        Template.fromParts
                            templateInfo
                            ers
                            tags
                            authors
                            (ArcTable.init ("New Template Table"))
                            DateTime.Now
                        |> ArcFiles.Template

                    return Result.Ok template
                | None ->
                    return
                        Result.Error [
                            InteropLogging.Msg.create InteropLogging.Error $"No top level metadata sheet is available!"
                        ]

            | _ ->
                return
                    Result.Error [
                        InteropLogging.Msg.create
                            InteropLogging.Error
                            $"No top level metadata sheet is available that determines the type of data!"
                    ]
        }

    /// <summary>
    /// This function is used to create a new annotation table. 'isDark' refers to the current styling of excel (darkmode, or not)
    /// </summary>
    /// <param name="isDark"></param>
    /// <param name="tryUseLastOutput"></param>
    static member createAnnotationTable(isDark: bool, tryUseLastOutput: bool) =
        Excel.run (fun context ->
            let selectedRange = context.workbook.getSelectedRange ()

            promise {
                let! newTableLogging = createAtRange isDark tryUseLastOutput selectedRange context

                // Interop logging expects list of logs
                return [ snd newTableLogging ]
            }
        )

    /// <summary>
    /// Add the given arc table to the active annotation table at the desired index
    /// </summary>
    /// <param name="tableToAdd"></param>
    /// <param name="index"></param>
    /// <param name="options"></param>
    static member joinTable
        (
            tableToAdd: ArcTable,
            deselectedColumnIndices: int list,
            options: TableJoinOptions option,
            templateName: string option,
            ?context0
        ) =
        excelRunWith context0
        <| fun context -> promise {
            //When a name is available get the annotation and arctable for easy access of indices and value adaption
            //Annotation table enables a easy way to adapt the table, updating existing and adding new columns
            let! result = tryGetActiveExcelTable context

            match result with
            | Some excelTable ->
                let! (refinedTableToAdd: ArcTable, index: int option) =
                    prepareTemplateInMemoryForExcel excelTable tableToAdd deselectedColumnIndices context

                //Arctable enables a fast check for the existence of input- and output-columns and their indices
                let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

                //When both tables could be accessed succesfully then check what kind of column shall be added an whether it is already there or not
                match arcTableRes with
                | Result.Ok arcTable ->
                    arcTable.Join(refinedTableToAdd, ?index = index, ?joinOptions = options, forceReplace = true)

                    do! joinArcTablesInExcel excelTable arcTable templateName context

                    return [
                        InteropLogging.Msg.create
                            InteropLogging.Info
                            $"Joined template {refinedTableToAdd.Name} to table {excelTable.name}!"
                    ]
                | Result.Error _ ->
                    return [
                        InteropLogging.Msg.create
                            InteropLogging.Error
                            "No arc table could be created! This should not happen at this stage! Please report this as a bug to the developers.!"
                    ]
            | None -> return [ InteropLogging.NoActiveTableMsg ]
        }

    /// <summary>
    /// Add the given arc table to the active annotation table at the desired index
    /// </summary>
    /// <param name="tableToAdd"></param>
    /// <param name="index"></param>
    /// <param name="options"></param>
    static member joinTables
        (
            tablesToAdd: ArcTable[],
            deselectedColumnsCollection: Set<int * int>,
            options: TableJoinOptions option,
            importTables: FileImport.ImportTable list,
            ?context0
        ) =
        excelRunWith context0
        <| fun context -> promise {
            //When a name is available get the annotation and arctable for easy access of indices and value adaption
            //Annotation table enables a easy way to adapt the table, updating existing and adding new columns
            let tablesToAdd, tablesToJoin = selectTablesToAdd tablesToAdd importTables
            let! result = tryGetActiveExcelTable context

            match result with
            | Some excelTable ->
                let! originTableRes = ArcTable.fromExcelTable (excelTable, context)

                match originTableRes with
                | Result.Error _ -> return failwith $"Failed to create arc table for table {excelTable.name}"
                | Result.Ok originTable ->
                    let! msgJoin =
                        if tablesToJoin.Length > 0 then
                            joinTemplatesToTable
                                originTable
                                excelTable
                                tablesToJoin
                                deselectedColumnsCollection
                                options
                                context
                        else
                            promise { return [] }

                    let! msgAdd = addTemplates tablesToAdd deselectedColumnsCollection options context

                    if msgAdd.IsEmpty then return msgJoin
                    else if msgJoin.IsEmpty then return msgAdd
                    else return [ msgJoin.Head; msgAdd.Head ]
            | None ->
                if tablesToJoin.Length > 0 then
                    return [ InteropLogging.NoActiveTableMsg ]
                else
                    let! msgAdd = addTemplates tablesToAdd deselectedColumnsCollection options context
                    return msgAdd
        }

    /// <summary>
    /// Create a new annotation table based on an arcTable
    /// </summary>
    /// <param name="arcTable"></param>
    /// <param name="context0"></param>
    static member createNewAnnotationTable(arcTable: ArcTable, ?context0) =
        excelRunWith context0
        <| fun context -> promise {
            let worksheetName = arcTable.Name
            //delete existing worksheet with the same name
            let worksheet0 = context.workbook.worksheets.getItemOrNullObject (worksheetName)
            worksheet0.delete ()
            // create new worksheet
            let worksheet = context.workbook.worksheets.add (worksheetName)
            do! context.sync ()
            let tableValues = arcTable.ToStringSeqs()
            let rowCount = tableValues.Count
            let columnCount = tableValues |> Seq.map Seq.length |> Seq.max
            let tableRange = worksheet.getRangeByIndexes (0, 0, rowCount, columnCount)
            let table = worksheet.tables.add (U2.Case1 tableRange, true)
            tableRange.values <- tableValues
            table.name <- createNewTableName ()
            table.style <- TableStyleLight
            // Only activate the worksheet if this function is specifically called
            if context0.IsNone then
                worksheet.activate ()

            do! context.sync ()
            return worksheet, table
        }

    /// <summary>
    /// This function deletes all existing arc tables in the excel file and metadata sheets, and writes a new ArcFile to excel
    /// </summary>
    /// <param name="arcFiles"></param>
    static member updateArcFile(arcFiles: ArcFiles, ?context0) =
        excelRunWith context0
        <| fun context -> promise {
            let worksheetName, seqOfSeqs = arcFiles.MetadataToExcelStringValues()
            let! updatedWorksheet = updateWorkSheet context worksheetName seqOfSeqs
            let tables = arcFiles.ArcTables()

            for table in tables do
                do! Main.createNewAnnotationTable(table, context).``then`` (fun _ -> ())

            updatedWorksheet.activate ()

            return [
                InteropLogging.Msg.create
                    InteropLogging.Info
                    $"Replaced existing ARCitISA information! Added {tables.TableCount} tables!"
            ]
        }

    /// <summary>
    /// Validates the arc table of the currently selected work sheet
    /// When the validations returns an error, an error is returned to the user
    /// When the arc table is valid one or more of the following processes happen:
    /// * When the main column of term or unit is empty, then the Term Source REF and Term Accession Number are emptied
    /// * When the main column of term or unit contains a value, the Term Source REF and Term Accession Number are filled
    /// with the correct value
    /// The later is not implemented yet
    /// </summary>
    static member rectifyTermColumns(?context0, ?getTerms0) =
        excelRunWith context0
        <| fun context -> promise {
            let! excelTableRes = tryGetActiveExcelTable context
            //When there are messages, then there is an error and further processes can be skipped because the annotation table is not valid
            match excelTableRes with
            | None -> return [ InteropLogging.NoActiveTableMsg ]
            | Some excelTable ->
                //Arctable enables a fast check for term and unit building blocks
                let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

                match arcTableRes with
                | Result.Ok arcTable ->
                    let columns = arcTable.Columns

                    let _ =
                        excelTable.columns.load (
                            propertyNames = U2.Case2(ResizeArray [| "items"; "rowCount"; "values" |])
                        )

                    do! context.sync ()
                    let items = excelTable.columns.items

                    let termAndUnitHeaders =
                        columns
                        |> Array.ofSeq
                        |> Array.choose (fun item ->
                            if item.Header.IsTermColumn then
                                Some(item.Header.ToString())
                            else
                                None
                        )

                    let columns =
                        items
                        |> Array.ofSeq
                        |> Array.map (fun c ->
                            c.values
                            |> Array.ofSeq
                            |> Array.collect (fun cc ->
                                cc
                                |> Array.ofSeq
                                |> Array.choose (fun r -> if r.IsSome then Some(r.ToString()) else None)
                            )
                        )

                    let body = excelTable.getDataBodyRange ()
                    let _ = body.load (propertyNames = U2.Case2(ResizeArray [| "values" |]))

                    do! context.sync ()

                    for i in 0 .. columns.Length - 1 do
                        let column = columns.[i]

                        if Array.contains (column.[0].Trim()) termAndUnitHeaders then
                            let! potBuildingBlock = getCompositeColumnInfoByIndex excelTable i context
                            let buildingBlock = potBuildingBlock |> Array.ofSeq

                            // Check whether building block is unit or not
                            // When it is unit, then delete the property column values only when the unit is empty, independent of the main column
                            // When it is a term, then delete the property column values when the main column is empty
                            let mainColumn =
                                if snd buildingBlock.[1] = "Unit" then
                                    excelTable.columns.items.Item(fst buildingBlock.[1])
                                else
                                    excelTable.columns.items.Item(fst buildingBlock.[0])

                            let potPropertyColumns =
                                buildingBlock.[1..]
                                |> Array.map (fun (index, _) -> index, excelTable.columns.items.Item index)

                            let propertyColumns =
                                potPropertyColumns
                                |> Array.map (fun (index, c) ->
                                    index,
                                    c.values
                                    |> Array.ofSeq
                                    |> Array.collect (fun pc ->
                                        pc
                                        |> Array.ofSeq
                                        |> Array.choose (fun pcv -> if pcv.IsSome then Some(pcv.ToString()) else None)
                                    )
                                )

                            let mainColumnHasValues =
                                mainColumn.values
                                |> Array.ofSeq
                                |> Array.collect (fun c ->
                                    c
                                    |> Array.ofSeq
                                    |> Array.choose (fun cc -> if cc.IsSome then Some(cc.ToString()) else None)
                                    |> Array.map (fun cv -> cv, String.IsNullOrEmpty(cv))
                                )

                            let mutable names = []
                            let mutable indices = []

                            //Check whether value of property colum is fitting for value of main column and adapt if not
                            //Delete values of property columns when main column is empty
                            for pi in 0 .. propertyColumns.Length - 1 do
                                let pIndex, pcv = propertyColumns.[pi]
                                let values = Array.create (arcTable.RowCount + 1) ""

                                for mainIndex in 0 .. mainColumnHasValues.Length - 1 do
                                    let mc, isNull = mainColumnHasValues.[mainIndex]

                                    if not isNull then
                                        names <- mc :: names
                                        indices <- mainIndex :: indices
                                        values.[mainIndex] <- pcv.[mainIndex]

                                let bodyValues =
                                    values
                                    |> Array.map (box >> Some)
                                    |> Array.map (fun c -> ResizeArray[c])
                                    |> ResizeArray

                                excelTable.columns.items.[pIndex].values <- bodyValues

                            let getTerms = defaultArg getTerms0 searchTermsInDatabase

                            let! terms = getTerms names

                            let indexedTerms = indices |> List.mapi (fun ii index -> index, terms.[ii])

                            do! updateSelectedBuildingBlocks excelTable arcTable propertyColumns indexedTerms

                    do! format (excelTable, context, true)

                    return [
                        InteropLogging.Msg.create InteropLogging.Info $"The annotation table {excelTable.name} is valid"
                    ]

                | Result.Error ex ->
                    return [
                        InteropLogging.Msg.create InteropLogging.Error ex.Message
                    ]
        }

    /// <summary>
    /// Ge parentTerm of selected column of given table or active table
    /// </summary>
    /// <param name="table"></param>
    /// <param name="context0"></param>
    static member getParentTerm(?table: Excel.Table, ?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {

            let! excelTable =
                match table with
                | Some table -> promise { return Some table }
                | None -> tryGetActiveExcelTable context

            if excelTable.IsNone then
                failwith "Error. No active table found!"

            let excelTable = excelTable.Value

            let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

            match arcTableRes with
            | Ok arcTable ->
                let selectedRange =
                    context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "rowIndex" |]))

                let! (_, arcIndex) = getArcMainColumn excelTable arcTable context

                let parent = arcTable.GetColumn(arcIndex).Header.TryOA()

                return parent

            | Result.Error exn -> return None
        }

    /// <summary>
    /// Get column details of given table or active one
    /// </summary>
    /// <param name="table"></param>
    /// <param name="context0"></param>
    static member getCompositeColumnDetails(?table: Excel.Table, ?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let! excelTable =
                match table with
                | Some table -> promise { return Some table }
                | None -> tryGetActiveExcelTable context

            if excelTable.IsNone then
                return (Result.Error [ InteropLogging.NoActiveTableMsg ])
            else

                let excelTable = excelTable.Value

                let! selectedCompositeColumn = getSelectedCompositeColumn excelTable context
                let selectedRange = context.workbook.getSelectedRange ()
                let tableRange = excelTable.getRange ()

                let _ =
                    tableRange.load (U2.Case2(ResizeArray [| "values" |])) |> ignore
                    selectedRange.load (U2.Case2(ResizeArray [| "rowIndex" |]))

                do! context.sync().``then`` (fun _ -> ())

                let mainColumnIndex = fst (selectedCompositeColumn.Item 0)
                let rowIndex = int selectedRange.rowIndex

                if rowIndex > 0 then
                    let values = getTableValues tableRange

                    let value = values.[rowIndex].[mainColumnIndex]

                    let! termRes = searchTermInDatabase value

                    match termRes with
                    | None ->
                        return
                            (Result.Error [
                                InteropLogging.Msg.create InteropLogging.Warning $"{value} is not a valid term"
                            ])
                    | Some term -> return (Result.Ok term)
                else
                    let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

                    match arcTableRes with
                    | Ok arcTable ->
                        let! (arcMainColumn, _) = getArcMainColumn excelTable arcTable context
                        let value = arcMainColumn.Header.ToTerm().Name
                        let! termRes = searchTermInDatabase value.Value

                        match termRes with
                        | None ->
                            return
                                (Result.Error [
                                    InteropLogging.Msg.create InteropLogging.Warning $"{value} is not a valid term"
                                ])
                        | Some term -> return (Result.Ok term)
                    | Error _ -> return (Result.Error [ InteropLogging.NoActiveTableMsg ])
        }

    /// <summary>
    /// Handle any diverging functionality here. This function is also used to make sure any new building blocks comply to the arcitisa annotation-table definition
    /// </summary>
    /// <param name="newColumn"></param>
    static member addCompositeColumn(newColumn: CompositeColumn, ?table: Excel.Table, ?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let! excelTable =
                match table with
                | Some table -> promise { return Some table }
                | None -> tryGetActiveExcelTable context

            if excelTable.IsNone then
                failwith "Error. No active table found!"

            let excelTable = excelTable.Value

            let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

            match arcTableRes with
            | Ok arcTable ->
                let values =
                    if newColumn.Cells.Count < arcTable.RowCount && newColumn.Cells.Count > 0 then
                        Array.create arcTable.RowCount newColumn.Cells.[0] |> ResizeArray
                    else
                        newColumn.Header.AsDiscriminate.CreateEmptyDefaultCells arcTable.RowCount

                arcTable.AddColumn(newColumn.Header, values, forceReplace = true)

                //Replace old excel table with new one
                let! _ = Main.createNewAnnotationTable (arcTable)

                let! newTable = tryGetActiveExcelTable (context)

                if newTable.IsSome then
                    do! format (newTable.Value, context, true)

                return []

            | Result.Error exn ->
                return [
                    InteropLogging.Msg.create InteropLogging.Error exn.Message
                ]
        }

    /// <summary>
    /// Get the valid cell type for the conversion based on input cell type
    /// </summary>
    /// <param name="cellType"></param>
    static member tryGetValidConversionCellTypes(?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let! result = tryGetActiveExcelTable context

            match result with
            | Some excelTable ->
                let! _, rowIndex = getSelectedBuildingBlockCell excelTable context
                let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

                match arcTableRes with
                | Result.Ok arcTable ->
                    let! (arcMainColumn, _) = getArcMainColumn excelTable arcTable context

                    if rowIndex > 0 then
                        return
                            match arcMainColumn with
                            | amc when amc.Cells.[(int rowIndex) - 1].isUnitized ->
                                (Some CompositeCellDiscriminate.Unitized, Some CompositeCellDiscriminate.Term)
                            | amc when amc.Cells.[(int rowIndex) - 1].isTerm ->
                                (Some CompositeCellDiscriminate.Term, Some CompositeCellDiscriminate.Unitized)
                            | amc when amc.Cells.[(int rowIndex) - 1].isData ->
                                (Some CompositeCellDiscriminate.Data, Some CompositeCellDiscriminate.Text)
                            | amc when amc.Cells.[(int rowIndex) - 1].isFreeText ->
                                if
                                    (arcMainColumn.Header.isInput || arcMainColumn.Header.isOutput)
                                    && arcMainColumn.Header.IsDataColumn
                                then
                                    (Some CompositeCellDiscriminate.Text, Some CompositeCellDiscriminate.Data)
                                else
                                    (Some CompositeCellDiscriminate.Data, None)
                            | _ -> (None, None)
                    else
                        return (None, None)
                | Result.Error _ -> return (None, None)
            | None -> return (None, None)
        }

    /// <summary>
    /// Checks whether the annotation table is a valid arc table or not
    /// BEcause a renamed column header can break a building block, the selected column and its neighbouring building blocks are checked
    /// </summary>
    static member validateSelectedAndNeighbouringBuildingBlocks(?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let! excelTableRes = tryGetActiveExcelTable context

            match excelTableRes with
            | Some excelTable ->
                return [
                    InteropLogging.Msg.create InteropLogging.Info $"The annotation table {excelTable.name} is valid"
                ]
            | None ->
                //Have to try to get the table without check in order to obtain an indexed error of the building block
                let _ = context.workbook.load (propertyNames = U2.Case2(ResizeArray [| "tables" |]))
                let activeWorksheet = context.workbook.worksheets.getActiveWorksheet ()
                let tables = context.workbook.tables

                let _ =
                    activeWorksheet.load (propertyNames = U2.Case2(ResizeArray [| "position" |]))
                    |> ignore

                    tables.load (
                        propertyNames = U2.Case2(ResizeArray [| "items"; "worksheet"; "name"; "position"; "values" |])
                    )

                let! table =
                    context
                        .sync()
                        .``then`` (fun _ ->
                            let activeWorksheetPosition = activeWorksheet.position

                            /// Get name of the table of currently active worksheet.
                            let activeTable =
                                tables.items
                                |> Seq.toArray
                                |> Array.tryFind (fun table ->
                                    table.name.StartsWith("annotationTable")
                                    && table.worksheet.position = activeWorksheetPosition
                                )

                            activeTable
                        )

                if table.IsNone then
                    return [ InteropLogging.NoActiveTableMsg ]
                else
                    let! indexedErrors = validateBuildingBlock table.Value context

                    let messages =
                        indexedErrors
                        |> List.ofArray
                        |> List.collect (fun (ex, header) -> [
                            InteropLogging.Msg.create
                                InteropLogging.Warning
                                $"The building block is not valid for a ARC table / ISA table: {ex.Message}"
                            InteropLogging.Msg.create
                                InteropLogging.Warning
                                $"The column {header} is not valid! It needs further inspection what causes the error"
                        ])

                    return (List.append messages [ InteropLogging.NoActiveTableMsg ])
        }

    /// <summary>
    /// This function is used to convert building blocks that can be converted. Data building blocks can be converted into free text, free text into data,
    /// terms into units and units into terms
    /// </summary>
    static member convertBuildingBlock(?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let! excelTableRes = tryGetActiveExcelTable context

            match excelTableRes with
            | Some excelTable ->
                //Get the selected cell that shall be converted. Based on the row index and cell index, get the main column.
                let! selectedBuildingBlock, rowIndex = getSelectedBuildingBlockCell excelTable context
                let! arcTableRes = ArcTable.fromExcelTable (excelTable, context)

                match arcTableRes with
                | Result.Ok arcTable ->

                    let! (arcMainColumn, arcIndex) = getArcMainColumn excelTable arcTable context

                    let msgText = if rowIndex = 0 then "Headers cannot be converted" else ""

                    //Convert the cells into the matching partner:
                    //Term <-> Unit
                    //Data <-> FreeText
                    match arcMainColumn with
                    | amc when amc.Cells.[(int rowIndex) - 1].isUnitized ->
                        convertToTermCell amc ((int rowIndex) - 1)
                        arcTable.UpdateColumn(arcIndex, arcMainColumn.Header, arcMainColumn.Cells)
                    | amc when amc.Cells.[(int rowIndex) - 1].isTerm ->
                        convertToUnitCell amc ((int rowIndex) - 1)
                        arcTable.UpdateColumn(arcIndex, arcMainColumn.Header, arcMainColumn.Cells)
                    | amc when amc.Cells.[(int rowIndex) - 1].isData ->
                        convertToFreeTextCell arcTable arcIndex amc ((int rowIndex) - 1)
                    | amc when amc.Cells.[(int rowIndex) - 1].isFreeText ->
                        convertToDataCell arcTable arcIndex amc ((int rowIndex) - 1)
                    | _ -> ()

                    let name = excelTable.name
                    let style = excelTable.style

                    let newTableRange = excelTable.getRange ()

                    let newExcelTableValues = arcTable.ToStringSeqs()

                    let _ =
                        newTableRange.load (
                            propertyNames =
                                U2.Case2(
                                    ResizeArray["values"
                                                "columnCount"
                                                "rowCount"]
                                )
                        )

                    //When a term or freetext is converted to data, new columns must be added. The range in excel must also be adapted.
                    do!
                        context
                            .sync()
                            .``then`` (fun _ ->
                                let difference =
                                    (newExcelTableValues.Item 0).Count - (int newTableRange.columnCount)

                                match difference with
                                | diff when diff > 0 ->
                                    for i = 0 to diff - 1 do
                                        Table.addColumn
                                            (newTableRange.columnCount + (float i))
                                            excelTable
                                            (i.ToString())
                                            (int newTableRange.rowCount)
                                            ""
                                        |> ignore
                                | diff when diff < 0 ->
                                    for i = 0 downto diff + 1 do
                                        Table.deleteColumn 0 excelTable
                                | _ -> ()
                            )

                    let newTableRange = excelTable.getRange ()

                    let _ =
                        newTableRange.load (
                            propertyNames =
                                U2.Case2(
                                    ResizeArray["values"
                                                "columnCount"]
                                )
                        )

                    do!
                        context
                            .sync()
                            .``then`` (fun _ ->
                                excelTable.delete ()
                                newTableRange.values <- newExcelTableValues
                            )

                    let _ =
                        newTableRange.load (
                            propertyNames =
                                U2.Case2(
                                    ResizeArray["values"
                                                "worksheet"]
                                )
                        )

                    do! context.sync ()

                    let activeSheet = newTableRange.worksheet
                    let _ = activeSheet.load (U2.Case2(ResizeArray [| "tables" |]))

                    do!
                        context
                            .sync()
                            .``then`` (fun _ ->
                                let newTable = activeSheet.tables.add (U2.Case1 newTableRange, true)
                                newTable.name <- name
                                newTable.style <- style
                            )

                    let! newTable = tryGetActiveExcelTable context

                    match newTable with
                    | Some table -> do! format (table, context, true)
                    | _ -> ()

                    let msg =
                        if String.IsNullOrEmpty(msgText) then
                            $"Converted building block of {snd selectedBuildingBlock.[0]} to unit"
                        else
                            msgText

                    return [ InteropLogging.Msg.create InteropLogging.Info msg ]
                | Result.Error ex ->
                    return [
                        InteropLogging.Msg.create InteropLogging.Error ex.Message
                    ]
            | None -> return [ InteropLogging.NoActiveTableMsg ]
        }

    /// <summary>
    /// Delete excel worksheet that contains top level metadata
    /// </summary>
    /// <param name="identifier"></param>
    static member deleteTopLevelMetadata(?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let worksheets = context.workbook.worksheets

            let _ =
                worksheets.load (propertyNames = U2.Case2(ResizeArray [| "items"; "name"; "values" |]))

            do! context.sync ()

            worksheets.items
            |> Seq.iter (fun worksheet ->
                if isTopLevelMetadataSheet worksheet.name then
                    worksheet.delete ()
            )

            return [
                InteropLogging.Msg.create InteropLogging.Info $"The top level metadata work sheet has been deleted"
            ]
        }

    /// <summary>
    /// Updates top level metadata excel worksheet of assays
    /// </summary>
    /// <param name="assay"></param>
    static member updateTopLevelMetadata(arcFiles: ArcFiles, ?context0: RequestContext) =
        excelRunWith context0
        <| fun context -> promise {
            let worksheetName, seqOfSeqs = arcFiles.MetadataToExcelStringValues()

            let! updatedWorksheet = updateWorkSheet context worksheetName seqOfSeqs

            updatedWorksheet.activate ()

            return [
                InteropLogging.Msg.create InteropLogging.Info $"The worksheet {worksheetName} has been updated"
            ]
        }

    /// <summary>
    /// Fill the selected building blocks, or single columns, with the selected term
    /// </summary>
    /// <param name="ontologyAnnotation"></param>
    static member fillSelectedWithOntologyAnnotation
        (ontologyAnnotation: OntologyAnnotation, ?context0: RequestContext)
        =
        excelRunWith context0
        <| fun context -> promise {
            let! result = tryGetActiveExcelTable context

            match result with
            | Some excelTable ->
                // Get index of first selected row
                let selectedRange =
                    context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "rowCount"; "rowIndex" |]))

                // Get rowcount and first selected column
                let mutable tableRange = excelTable.getRange ()

                let _ =
                    tableRange.load (U2.Case2(ResizeArray [| "columnCount"; "rowCount"; "values" |]))

                do! context.sync ()

                //Checks whether selection in excel is outside a table or not
                let! isAnnotationTableSelected = isSelectedOutsideAnnotationTable tableRange selectedRange context

                //If selection is within table, adapt selected rows and columns
                //If selection is outside table, insert ontology in selected rows and columns, in the order Name, Term Source REF, Term Accession Number, Unit repeatidly
                if isAnnotationTableSelected then

                    do! insertOntology selectedRange ontologyAnnotation context

                    return [
                        InteropLogging.Msg.create InteropLogging.Info "Filled the columns with the selected term"
                    ]
                else

                    let firstRow = selectedRange.rowIndex
                    let selectedRowCount = selectedRange.rowCount

                    if firstRow + selectedRowCount > tableRange.rowCount then
                        let targetRowCount = int (firstRow + selectedRowCount)

                        do!
                            expandTableRowCount
                                excelTable
                                (int tableRange.rowCount)
                                (int tableRange.columnCount)
                                targetRowCount
                                context

                        let newTableRange = excelTable.getRange ()
                        tableRange <- newTableRange

                    let _ =
                        tableRange.load (U2.Case2(ResizeArray [| "columnCount"; "rowCount"; "values" |]))

                    do! context.sync ()

                    let tableValues = getTableValues tableRange

                    let tableHeaders = tableValues.[0]

                    if firstRow = 0 && selectedRowCount = 1 then
                        return [
                            InteropLogging.Msg.create
                                InteropLogging.Error
                                "You cannot fill the headers of an annotation table with terms!"
                        ]
                    else
                        let selectedRowCount =
                            if firstRow = 0. then
                                selectedRowCount - 1.
                            else
                                selectedRowCount

                        let firstRow = if firstRow = 0. then 1. else firstRow

                        let! selectedBuildingBlock = getSelectedCompositeColumn excelTable context

                        let columnIndices =
                            selectedBuildingBlock |> Array.ofSeq |> Array.map (fun (index, _) -> index)

                        let columnHeaders = ARCtrl.Spreadsheet.ArcTable.helperColumnStrings |> Array.ofSeq

                        let firstIndex = Array.head columnIndices
                        let lastIndex = Array.last columnIndices

                        let isUnit = Array.contains columnHeaders.[2] tableHeaders.[firstIndex..lastIndex] //Unit

                        for rowIndex in firstRow .. (firstRow + selectedRowCount - 1.) do
                            columnIndices
                            |> Array.iter (fun columnIndex ->
                                match tableHeaders.[columnIndex] with
                                | header when header = columnHeaders.[2] -> //Unit
                                    tableValues.[int rowIndex].[columnIndex] <-
                                        (if ontologyAnnotation.Name.IsSome then
                                             ontologyAnnotation.Name.Value
                                         else
                                             tableValues.[int rowIndex].[columnIndex])
                                | header when header.Contains(columnHeaders.[0]) -> //Term Source REF
                                    tableValues.[int rowIndex].[columnIndex] <-
                                        (if ontologyAnnotation.TermSourceREF.IsSome then
                                             ontologyAnnotation.TermSourceREF.Value
                                         else
                                             tableValues.[int rowIndex].[columnIndex])
                                | header when header.Contains(columnHeaders.[1]) -> //Term Accession Number
                                    tableValues.[int rowIndex].[columnIndex] <-
                                        (if ontologyAnnotation.TermAccessionNumber.IsSome then
                                             ontologyAnnotation.TermAccessionAndOntobeeUrlIfShort
                                         else
                                             tableValues.[int rowIndex].[columnIndex])
                                | _ -> //Only main column left
                                    if not isUnit then
                                        tableValues.[int rowIndex].[columnIndex] <-
                                            (if ontologyAnnotation.Name.IsSome then
                                                 ontologyAnnotation.Name.Value
                                             else
                                                 tableValues.[int rowIndex].[columnIndex])
                            )

                        let bodyValues =
                            tableValues
                            |> Array.map (fun row -> row |> Array.map (fun item -> box item |> Some) |> ResizeArray)
                            |> ResizeArray

                        tableRange.values <- bodyValues

                        do! context.sync ()

                        do! format (excelTable, context, true)

                        return [
                            InteropLogging.Msg.create InteropLogging.Info "Filled the columns with the selected term"
                        ]

            | _ ->
                let selectedRange =
                    context.workbook.getSelectedRange().load (U2.Case2(ResizeArray [| "rowCount"; "rowIndex" |]))

                do! context.sync ()

                do! insertOntology selectedRange ontologyAnnotation context

                return [
                    InteropLogging.Msg.create InteropLogging.Info "Filled the columns with the selected term"
                ]
        }

    /// <summary>
    /// This function is used to insert file names into the selected range.
    /// </summary>
    /// <param name="fileNameList"></param>
    static member insertFileNamesFromFilePicker(fileNameList: string list, ?context0: RequestContext) =
        excelRunWith context0
        <| fun context ->

            // Ref. 2
            let range = context.workbook.getSelectedRange ()

            let _ =
                range.load (U2.Case2(ResizeArray([ "values"; "columnIndex"; "columnCount" ])))

            // Ref. 1
            let r = context.runtime.load (U2.Case1 "enableEvents")

            // sync with proxy objects after loading values from excel
            context
                .sync()
                .``then`` (fun _ ->

                    if range.columnCount > 1. then
                        failwith "Cannot insert Terms in more than one column at a time."

                    r.enableEvents <- false

                    // create new values for selected Range.
                    let newVals =
                        ResizeArray(
                            [
                                // iterate over the rows of the selected range (there can only be one column as we fail if more are selected)
                                for rowInd in 0 .. range.values.Count - 1 do
                                    let tmp =
                                        // Iterate over col values (1).
                                        range.values.[rowInd]
                                        |> Seq.map (fun _ ->
                                            // This part is a design choice.
                                            // Should the user select less cells than we have items in the 'fileNameList' then we only fill the selected cells.
                                            // Should the user select more cells than we have items in the 'fileNameList' then we fill the leftover cells with none.
                                            let fileName =
                                                if fileNameList.Length - 1 < rowInd then
                                                    None
                                                else
                                                    List.item rowInd fileNameList |> box |> Some

                                            fileName
                                        )

                                    ResizeArray(tmp)
                            ]
                        )

                    range.values <- newVals
                    range.format.autofitColumns ()
                    r.enableEvents <- true
                    //sprintf "%s filled with %s; ExtraCols: %s" range.address v nextColsRange.address

                    // return print msg
                    "Info", sprintf "%A, %A" range.values.Count newVals
                )
