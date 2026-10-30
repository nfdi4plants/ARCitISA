module Swate.Components.Composite.TableImport.Helper

open System
open ARCtrl
open Swate.Components.Shared
open Swate.Components.Shared.ARCtrlHelper
open Swate.Components.Composite.TableImport.Types

let private tryTrimmed (value: string) =
    if String.IsNullOrWhiteSpace value then
        None
    else
        Some(value.Trim())

/// The text a grid cell shows: the value and unit of a unitized cell, the name of a term or data cell.
let cellText (cell: CompositeCell) : string =
    let text =
        match cell with
        | CompositeCell.FreeText text -> text
        | CompositeCell.Term ontologyAnnotation -> ontologyAnnotation.NameText
        | CompositeCell.Unitized(value, unit) ->
            [| value; unit.NameText |] |> Array.choose tryTrimmed |> String.concat " "
        | CompositeCell.Data data -> data.NameText

    if isNull text then "" else text.Trim()

/// Returns a new table with the name and headers of the given table and only the given rows, in the given order.
let filterRows (table: ArcTable) (selectedRowIndices: int[]) : ArcTable =
    // RemoveRows rebuilds every sparse column once per removed row in ARCtrl beta.12, which is quadratic
    // for many rows. Building the result from the kept rows costs O(kept rows x columns).
    let result = ArcTable.init table.Name

    let columns =
        table.Headers
        |> Seq.mapi (fun columnIndex header ->
            // GetCellAt infers the empty cell of a gap from the stored cells of the column. It is
            // costly and returns the same value for every gap, so it runs once per column.
            let gapCell: CompositeCell option ref = ref None

            let cells =
                selectedRowIndices
                |> Array.map (fun rowIndex ->
                    match table.TryGetCellAt(columnIndex, rowIndex) with
                    | Some cell -> cell.Copy()
                    | None ->
                        if gapCell.Value.IsNone then
                            gapCell.Value <- Some(table.GetCellAt(columnIndex, rowIndex))

                        gapCell.Value.Value.Copy()
                )

            CompositeColumn.create (header.Copy(), ResizeArray cells)
        )
        |> ResizeArray

    result.AddColumns columns
    result

/// Returns a new table with the rows of the given table, where each deselected row holds empty cells.
/// The table ends after the last kept row, so left-out rows at the end do not add rows to the active table.
/// An appended source keeps its rows at their position this way, so they keep lining up with the rows
/// of the active table.
let clearRows (table: ArcTable) (deselectedRowIndices: Set<int>) : ArcTable =
    // Like filterRows, this builds the columns directly and avoids RemoveRows.
    let result = ArcTable.init table.Name

    let rowCount =
        match
            Seq.tryFindBack
                (fun rowIndex -> not (deselectedRowIndices.Contains rowIndex))
                (seq { 0 .. table.RowCount - 1 })
        with
        | Some lastKept -> lastKept + 1
        | None -> 0

    let columns =
        table.Headers
        |> Seq.mapi (fun columnIndex header ->
            // A gap is already empty, and GetCellAt infers it once per column as in filterRows.
            let gapCell: CompositeCell option ref = ref None

            let cells =
                Array.init
                    rowCount
                    (fun rowIndex ->
                        match table.TryGetCellAt(columnIndex, rowIndex) with
                        | Some cell when deselectedRowIndices.Contains rowIndex -> cell.GetEmptyCell()
                        | Some cell -> cell.Copy()
                        | None ->
                            if gapCell.Value.IsNone then
                                gapCell.Value <- Some(table.GetCellAt(columnIndex, rowIndex))

                            gapCell.Value.Value.Copy()
                    )

            CompositeColumn.create (header.Copy(), ResizeArray cells)
        )
        |> ResizeArray

    result.AddColumns columns
    result

[<RequireQualifiedAccess>]
module ImportMode =

    let options: ImportModeOption[] = [|
        {
            Mode = ARCtrl.TableJoinOptions.Headers
            Key = "Headers"
            Label = "Headers only"
            Description = "Adds the column headers only. The cells stay empty."
        }
        {
            Mode = ARCtrl.TableJoinOptions.WithUnit
            Key = "WithUnit"
            Label = "With units"
            Description = "Adds the column headers and the units of cells that have one. Other values are left out."
        }
        {
            Mode = ARCtrl.TableJoinOptions.WithValues
            Key = "WithValues"
            Label = "With values"
            Description = "Adds the column headers with all values, including terms, units and data files."
        }
    |]

let private getDeselectedTableColumnIndices (deselectedColumns: Set<int * int>) (tableIndex: int) =
    deselectedColumns
    |> Seq.choose (fun (candidateTableIndex, columnIndex) ->
        if candidateTableIndex = tableIndex then
            Some columnIndex
        else
            None
    )
    |> List.ofSeq

// https://github.com/nfdi4plants/ARCtrl/issues/647
// If this issue is fixed, we will no longer need to manually add empty columns for header-only imports.
let private joinTable (target: ArcTable) (source: ArcTable) (importType: TableJoinOptions) =
    match importType with
    | TableJoinOptions.Headers ->
        // ARCtrl beta.12's Headers join omits backing column entries, which breaks later reads and saves.
        // Add empty columns through the normal API to preserve header-only import semantics and stored refs.
        target.AddColumns(source.Headers |> Seq.map (fun header -> CompositeColumn.create header))
    | _ -> target.Join(source, joinOptions = importType)

let createUpdatedTables
    (arcTables: ResizeArray<ArcTable>)
    (state: SelectiveImportConfig)
    (deselectedColumns: Set<int * int>)
    fullImport
    =
    [
        for importTable in state.ImportTables do
            let fullImport = defaultArg fullImport importTable.FullImport

            if importTable.FullImport = fullImport then
                let deselectedColumnIndices =
                    getDeselectedTableColumnIndices deselectedColumns importTable.Index

                let sourceTable = arcTables.[importTable.Index]

                // A full WithValues import is a plain copy. The prepare and join path rebuilds the table
                // and rejects or changes some tables ARCtrl reads from JSON.
                if
                    importTable.FullImport
                    && state.ImportType = TableJoinOptions.WithValues
                    && List.isEmpty deselectedColumnIndices
                then
                    let name = defaultArg importTable.Name sourceTable.Name
                    let copy = sourceTable.Copy()

                    if copy.Name = name then
                        copy
                    else
                        ArcTable.fromArcTableValues (name, copy.Headers, copy.Values)
                else
                    let appliedTable = ArcTable.init (defaultArg importTable.Name sourceTable.Name)

                    let finalTable =
                        Table.selectiveTablePrepare appliedTable sourceTable deselectedColumnIndices

                    joinTable appliedTable finalTable state.ImportType
                    appliedTable
    ]
    |> ResizeArray

let updateTables
    (importTables: ResizeArray<ArcTable>)
    (importConfig: SelectiveImportConfig)
    (activeTableIndex: int option)
    (existingOpt: ArcFiles option)
    =
    let deselectedColumns = importConfig.DeselectedColumns

    match existingOpt with
    | Some existing ->
        let existingTables = existing.Tables()

        match activeTableIndex with
        | Some tableIndex when tableIndex >= 0 && tableIndex < existingTables.Count ->
            let activeTable = existingTables.[tableIndex]

            let selectedColumnTables =
                createUpdatedTables importTables importConfig deselectedColumns (Some false)
                |> Array.ofSeq
                |> Array.rev

            let tempTable =
                if activeTable.ColumnCount = 0 then
                    ArcTable.init activeTable.Name
                else
                    activeTable.Copy()

            for table in selectedColumnTables do
                if table.RowCount = 0 then
                    let cells =
                        table.Columns
                        |> Array.ofSeq
                        |> Array.map (fun column ->
                            match column.Header with
                            | CompositeHeader.Factor _
                            | CompositeHeader.Component _
                            | CompositeHeader.Parameter _
                            | CompositeHeader.Characteristic _
                            | CompositeHeader.ProtocolType ->
                                match importConfig.ImportType with
                                | TableJoinOptions.WithUnit -> CompositeCell.Unitized("", OntologyAnnotation.empty ())
                                | _ -> CompositeCell.Term(OntologyAnnotation.empty ())
                            | _ -> CompositeCell.FreeText ""
                        )
                        |> ResizeArray

                    let rows = Array.create tempTable.RowCount cells
                    table.AddRows(ResizeArray rows)
                elif table.RowCount < tempTable.RowCount then
                    table.AddRowsEmpty(tempTable.RowCount - table.RowCount)

                let preparedTemplate = Table.distinctByHeader tempTable table

                if tempTable.ColumnCount = 0 then
                    tempTable.AddColumns(preparedTemplate.Columns)
                else
                    joinTable tempTable preparedTemplate importConfig.ImportType

            existingTables.[tableIndex] <- tempTable
        | _ -> ()

        let selectedColumnTables =
            createUpdatedTables importTables importConfig deselectedColumns (Some true)
            |> Array.ofSeq
            |> Array.rev

        let newTables =
            selectedColumnTables
            |> Seq.map (fun table ->
                if importConfig.ImportType = TableJoinOptions.WithValues then
                    table
                else
                    let nextTable = ArcTable.init table.Name
                    joinTable nextTable table importConfig.ImportType
                    nextTable
            )
            |> Seq.rev
            |> ResizeArray

        existing.ArcTables().AddTables newTables

        existing
    | None -> failwith "Error! Can only append information if metadata sheet exists!"

let actionOf (decisions: TableImportDecisions) (sourceIndex: int) =
    decisions.Actions.TryFind sourceIndex
    |> Option.defaultValue decisions.DefaultAction

/// The number of rows of one source that the user left out.
let deselectedRowCount (decisions: TableImportDecisions) (sourceIndex: int) : int =
    decisions.DeselectedRows
    |> Seq.filter (fun (index, _) -> index = sourceIndex)
    |> Seq.length

let newTableNames
    (existingTableNames: string seq)
    (sources: ImportSource[])
    (actionOf: int -> TableImportAction)
    (enteredNames: Map<int, string>)
    =
    let newTableSources =
        sources
        |> Array.filter (fun source -> actionOf source.Index = TableImportAction.ImportAsNewTable)

    newTableSources
    |> Array.map (fun source -> source.Table.Name, enteredNames.TryFind source.Index)
    |> resolveNewTableNames existingTableNames
    |> Array.zip newTableSources
    |> Array.map (fun (source, newTableName) -> source.Index, newTableName)
    |> Map.ofArray

let tryGetAppendTarget (arcFile: ArcFiles) (activeTableIndex: int option) : (int * ArcTable) option =
    arcFile.TryGetActiveTable(activeTableIndex)

/// The decisions the JSON import starts from. The caller sets ActiveTableIndex.
let defaultJsonDecisions: TableImportDecisions = {
    ImportType = TableJoinOptions.WithValues
    DefaultAction = TableImportAction.ImportAsNewTable
    Actions = Map.empty
    DeselectedColumns = Set.empty
    DeselectedRows = Set.empty
    EnteredNames = Map.empty
    ActiveTableIndex = None
}

/// The first reason the decisions cannot be applied, or None when they can.
let validate (current: ArcFiles) (sources: ImportSource[]) (decisions: TableImportDecisions) : string option =
    let actionOf = actionOf decisions

    let selected =
        sources
        |> Array.filter (fun source -> actionOf source.Index <> TableImportAction.NoImport)

    // A table without columns has nothing to select and is imported as it is.
    let tableWithoutSelection =
        selected
        |> Array.tryFind (fun source ->
            source.Table.ColumnCount > 0
            && (Seq.init source.Table.ColumnCount id
                |> Seq.forall (fun columnIndex -> decisions.DeselectedColumns.Contains(source.Index, columnIndex)))
        )

    // Headers mode imports no values, so it has no row rule.
    let tableWithoutRows =
        if decisions.ImportType = TableJoinOptions.Headers then
            None
        else
            selected
            |> Array.tryFind (fun source ->
                source.Table.RowCount > 0
                && (Seq.init source.Table.RowCount id
                    |> Seq.forall (fun rowIndex -> decisions.DeselectedRows.Contains(source.Index, rowIndex)))
            )

    let names =
        newTableNames (current.ArcTables().TableNames) sources actionOf decisions.EnteredNames

    let nameError =
        selected
        |> Array.tryPick (fun source -> names.TryFind source.Index |> Option.bind (fun name -> name.Error))

    let appends =
        selected
        |> Array.exists (fun source -> actionOf source.Index = TableImportAction.AppendToActiveTable)

    if selected.Length = 0 then
        Some "Select at least one table to import."
    elif tableWithoutSelection.IsSome then
        Some $"Select at least one column of \"{tableWithoutSelection.Value.Name}\"."
    elif tableWithoutRows.IsSome then
        Some $"Select at least one row of \"{tableWithoutRows.Value.Name}\"."
    elif nameError.IsSome then
        nameError
    elif appends && (tryGetAppendTarget current decisions.ActiveTableIndex).IsNone then
        Some "Select a table in the editor to append columns to."
    else
        None

/// Applies the decisions to a copy of the current file and leaves the current file as it is.
let applyImport
    (current: ArcFiles)
    (sources: ImportSource[])
    (decisions: TableImportDecisions)
    : Result<TableImportResult, exn> =
    match validate current sources decisions with
    | Some message -> Error(exn message)
    | None ->
        let actionOf = actionOf decisions

        let selected =
            sources
            |> Array.filter (fun source -> actionOf source.Index <> TableImportAction.NoImport)

        let names =
            newTableNames (current.ArcTables().TableNames) sources actionOf decisions.EnteredNames

        let appendTarget = tryGetAppendTarget current decisions.ActiveTableIndex

        let appends =
            selected
            |> Array.exists (fun source -> actionOf source.Index = TableImportAction.AppendToActiveTable)

        let positionBySourceIndex =
            selected
            |> Array.mapi (fun position source -> source.Index, position)
            |> Map.ofArray

        let newTableName (source: ImportSource) =
            names.TryFind source.Index |> Option.map (fun name -> name.Name.Trim())

        let config = {
            SelectiveImportConfig.init () with
                ImportType = decisions.ImportType
                ImportTables =
                    selected
                    |> Array.mapi (fun position source ->
                        let fullImport = actionOf source.Index = TableImportAction.ImportAsNewTable

                        {
                            Index = position
                            FullImport = fullImport
                            Name = if fullImport then newTableName source else None
                        }
                    )
                    |> List.ofArray
                DeselectedColumns =
                    decisions.DeselectedColumns
                    |> Seq.choose (fun (sourceIndex, columnIndex) ->
                        positionBySourceIndex.TryFind sourceIndex
                        |> Option.map (fun position -> position, columnIndex)
                    )
                    |> Set.ofSeq
        }

        // Deselected columns are column indices and stay valid after the rows are filtered or cleared.
        // A new table packs the kept rows. An appended source keeps its row positions.
        let rowFilteredTable (source: ImportSource) =
            if decisions.ImportType = TableJoinOptions.Headers then
                source.Table
            else
                let selectedRows =
                    Array.init source.Table.RowCount id
                    |> Array.filter (fun rowIndex -> not (decisions.DeselectedRows.Contains(source.Index, rowIndex)))

                if selectedRows.Length = source.Table.RowCount then
                    source.Table
                elif actionOf source.Index = TableImportAction.AppendToActiveTable then
                    let deselectedRows =
                        Set.difference (Set.ofSeq (seq { 0 .. source.Table.RowCount - 1 })) (Set.ofArray selectedRows)

                    clearRows source.Table deselectedRows
                else
                    filterRows source.Table selectedRows

        try
            let next = ArcFiles.refreshRef current

            updateTables
                (selected |> Array.map rowFilteredTable |> ResizeArray)
                config
                (if appends then appendTarget |> Option.map fst else None)
                (Some next)
            |> ignore

            Ok {
                ArcFile = next
                CreatedTableNames =
                    selected
                    |> Array.filter (fun source -> actionOf source.Index = TableImportAction.ImportAsNewTable)
                    |> Array.choose newTableName
                    |> List.ofArray
                ModifiedTableNames =
                    if appends then
                        appendTarget |> Option.map (fun (_, table) -> table.Name) |> Option.toList
                    else
                        []
            }
        with ex ->
            Error ex
