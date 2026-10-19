module Main.ArcNodesReader

open ARCtrl
open Swate.Electron.Shared.DTOs.ProvenanceGroupingDto
open Swate.Electron.Shared.DTOs.ArcNodesDto

/// Keeps only the input and output columns, so IPC does not carry columns the renderer never reads.
let private tryCreateNodeTable (table: ArcTable) =
    let nodeTable = ArcTable.init table.Name

    for column in List.choose id [ table.TryGetInputColumn(); table.TryGetOutputColumn() ] do
        nodeTable.AddColumn(column.Header, ResizeArray column.Cells)

    if nodeTable.ColumnCount = 0 then None else Some nodeTable

let private nodeTablesFor scope parentIdentifier (tables: ResizeArray<ArcTable>) =
    tables
    |> Seq.choose tryCreateNodeTable
    |> Seq.map (ArcNodeTableDto.create scope parentIdentifier)
    |> Seq.toArray

/// Read the input and output columns of every study, assay and run table,
/// including edits that have not been saved to disk.
let read (arc: ARC) : ArcNodeTableDto[] = [|
    for study in arc.Studies do
        yield! nodeTablesFor ProvenanceTableScopeDto.Study study.Identifier study.Tables

    for assay in arc.Assays do
        yield! nodeTablesFor ProvenanceTableScopeDto.Assay assay.Identifier assay.Tables

    for run in arc.Runs do
        yield! nodeTablesFor ProvenanceTableScopeDto.Run run.Identifier run.Tables
|]
