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

let compositeCellPreviewValues (cell: CompositeCell) =
    match cell with
    | CompositeCell.FreeText text -> [| text |]
    | CompositeCell.Term ontologyAnnotation -> [|
        ontologyAnnotation.NameText
        defaultArg ontologyAnnotation.TermSourceREF ""
        defaultArg ontologyAnnotation.TermAccessionNumber ""
      |]
    | CompositeCell.Unitized(value, ontologyAnnotation) -> [|
        value
        ontologyAnnotation.NameText
        defaultArg ontologyAnnotation.TermSourceREF ""
        defaultArg ontologyAnnotation.TermAccessionNumber ""
      |]
    | CompositeCell.Data data -> [|
        defaultArg data.FilePath ""
        data.NameText
        defaultArg data.Selector ""
        defaultArg data.Format ""
        defaultArg data.SelectorFormat ""
      |]
    |> Array.choose tryTrimmed

let columnValuePreview (table: ArcTable) (columnIndex: int) =
    seq {
        for rowIndex in 0 .. table.RowCount - 1 do
            match table.TryGetCellAt(columnIndex, rowIndex) with
            | Some cell -> yield cell
            | None -> ()
    }
    |> Seq.collect compositeCellPreviewValues
    |> Seq.distinct
    |> Seq.truncate 3
    |> String.concat " | "
    |> fun preview -> if preview = "" then "No values" else preview

[<RequireQualifiedAccess>]
module ImportMode =

    /// Each entry is (mode, key, label, description).
    let options: (ARCtrl.TableJoinOptions * string * string * string)[] = [|
        ARCtrl.TableJoinOptions.Headers,
        "Headers",
        "Headers only",
        "Adds the column headers only. The cells stay empty."
        ARCtrl.TableJoinOptions.WithUnit,
        "WithUnit",
        "With units",
        "Adds the column headers and the units of cells that have one. Other values are left out."
        ARCtrl.TableJoinOptions.WithValues,
        "WithValues",
        "With values",
        "Adds the column headers with all values, including terms, units and data files."
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

let isColumnSelected (decisions: TableImportDecisions) (sourceIndex: int) (columnIndex: int) =
    not (decisions.DeselectedColumns.Contains(sourceIndex, columnIndex))

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

/// Applies the decisions to a copy of the current file. The current file is not changed.
let applyImport
    (current: ArcFiles)
    (sources: ImportSource[])
    (decisions: TableImportDecisions)
    : Result<TableImportResult, exn> =
    let actionOf = actionOf decisions

    let selected =
        sources
        |> Array.filter (fun source -> actionOf source.Index <> TableImportAction.NoImport)

    // A table without columns has nothing to select and is imported as it is.
    let tableWithoutSelection =
        selected
        |> Array.tryFind (fun source ->
            source.Table.ColumnCount > 0
            && not (
                Seq.init source.Table.ColumnCount id
                |> Seq.exists (isColumnSelected decisions source.Index)
            )
        )

    let names =
        newTableNames (current.ArcTables().TableNames) sources actionOf decisions.EnteredNames

    let nameError =
        selected
        |> Array.tryPick (fun source -> names.TryFind source.Index |> Option.bind (fun name -> name.Error))

    let appendTarget = tryGetAppendTarget current decisions.ActiveTableIndex

    let appends =
        selected
        |> Array.exists (fun source -> actionOf source.Index = TableImportAction.AppendToActiveTable)

    if selected.Length = 0 then
        Error(exn "Select at least one table to import.")
    elif tableWithoutSelection.IsSome then
        Error(exn $"Select at least one column of \"{tableWithoutSelection.Value.Name}\".")
    elif nameError.IsSome then
        Error(exn nameError.Value)
    elif appends && appendTarget.IsNone then
        Error(exn "Select a table in the editor to append columns to.")
    else
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

        try
            let next = ArcFiles.refreshRef current

            updateTables
                (selected |> Array.map (fun source -> source.Table) |> ResizeArray)
                config
                (appendTarget |> Option.map fst)
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
