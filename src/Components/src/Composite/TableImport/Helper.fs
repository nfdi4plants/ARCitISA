module Swate.Components.Composite.TableImport.Helper

open System
open ARCtrl
open Swate.Components.Shared
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
        ARCtrl.TableJoinOptions.Headers, "Headers", "Headers only", "Adds the column headers. The new cells stay empty."
        ARCtrl.TableJoinOptions.WithUnit,
        "WithUnit",
        "With units",
        "Adds the column headers and keeps units. Other values stay empty."
        ARCtrl.TableJoinOptions.WithValues, "WithValues", "With values", "Adds the column headers with all values."
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
                let nextTable = ArcTable.init table.Name
                joinTable nextTable table importConfig.ImportType
                nextTable
            )
            |> Seq.rev
            |> ResizeArray

        existing.ArcTables().AddTables newTables

        existing
    | None -> failwith "Error! Can only append information if metadata sheet exists!"
