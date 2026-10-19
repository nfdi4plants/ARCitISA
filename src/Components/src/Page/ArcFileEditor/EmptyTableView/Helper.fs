module Swate.Components.Page.ArcFileEditor.EmptyTableView.Helper

open ARCtrl
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter
open Swate.Components.Page.ArcFileEditor.EmptyTableView.Types

let tryGetActiveTable (arcFile: ArcFiles) (activeTableIndex: int option) =
    arcFile.TryGetActiveTable(activeTableIndex)

let createMinimalTable (arcFile: ArcFiles) (activeTableIndex: int option) (setArcFile: ArcFiles -> unit) =
    match tryGetActiveTable arcFile activeTableIndex with
    | Some(_, activeTable) ->
        let newColumns = [|
            CompositeColumn.create (CompositeHeader.Input IOType.Source)
            CompositeColumn.create CompositeHeader.ProtocolUri
            CompositeColumn.create (CompositeHeader.Output IOType.Sample)
        |]

        activeTable.AddColumns(newColumns)
        activeTable.AddRowsEmpty(3)

        let nextArcFile = ArcFiles.refreshRef arcFile

        setArcFile nextArcFile
    | None -> ()

/// The study, assay or run the file stands for, if its tables can be part of an ARC.
let tryGetFileOwner (arcFile: ArcFiles) =
    match arcFile with
    | ArcFiles.Study(study, _) -> Some(ArcTableScope.Study, study.Identifier)
    | ArcFiles.Assay assay -> Some(ArcTableScope.Assay, assay.Identifier)
    | ArcFiles.Run run -> Some(ArcTableScope.Run, run.Identifier)
    | _ -> None

let ownerLabel (scope: ArcTableScope) (identifier: string) =
    match scope with
    | ArcTableScope.Study -> $"Study {identifier}"
    | ArcTableScope.Assay -> $"Assay {identifier}"
    | ArcTableScope.Run -> $"Run {identifier}"

let private tryCreateNodeOption group (table: ArcTable) kind (column: CompositeColumn option) =
    column
    |> Option.filter (fun column -> column.Header.TryIOType().IsSome)
    |> Option.map (fun column -> {
        Key = $"{group}\u001f{table.Name}\u001f{kind}"
        Group = group
        TableName = table.Name
        Kind = kind
        Column = column
    })

/// Outputs come first, because continuing from the outputs of a prior table is the common case.
let nodeOptionsOfTable (group: string) (table: ArcTable) =
    [|
        ArcNodeKind.Output, table.TryGetOutputColumn()
        ArcNodeKind.Input, table.TryGetInputColumn()
    |]
    |> Array.choose (fun (kind, column) -> tryCreateNodeOption group table kind column)

/// Input and output columns of the other tables in the open file.
let localNodeOptions (arcFile: ArcFiles) (activeTableIndex: int option) =
    let group =
        match tryGetFileOwner arcFile with
        | Some(scope, identifier) -> $"{ownerLabel scope identifier} (this file)"
        | None -> "This file"

    arcFile.Tables()
    |> Seq.indexed
    |> Seq.filter (fun (index, _) -> Some index <> activeTableIndex)
    |> Seq.collect (fun (_, table) -> nodeOptionsOfTable group table)
    |> Seq.toArray

/// Input and output columns of tables from the whole ARC.
/// Tables of the open file are left out, because the open file can hold changes the ARC does not know yet.
let arcNodeOptions (arcFile: ArcFiles) (tables: ArcNodeTable[]) =
    let fileOwner = tryGetFileOwner arcFile

    tables
    |> Array.filter (fun nodeTable -> Some(nodeTable.Location.Scope, nodeTable.Location.ParentIdentifier) <> fileOwner)
    |> Array.collect (fun nodeTable ->
        let group = ownerLabel nodeTable.Location.Scope nodeTable.Location.ParentIdentifier

        nodeOptionsOfTable group nodeTable.Table
    )

/// The column the active table gets when the node option is imported.
let importedColumn (node: ArcNodeOption) =
    match node.Column.Header.TryIOType() with
    | Some ioType -> Some(CompositeColumn.create (CompositeHeader.Input ioType, ResizeArray node.Column.Cells))
    | None -> None

let previewCells (cells: seq<CompositeCell>) =
    cells
    |> Seq.truncate 10
    |> Seq.map string
    |> Seq.toArray
    |> Array.filter (fun x -> not (System.String.IsNullOrWhiteSpace x))

let importNodeOption
    (arcFile: ArcFiles)
    (activeTableIndex: int option)
    (node: ArcNodeOption)
    (setArcFile: ArcFiles -> unit)
    =
    match tryGetActiveTable arcFile activeTableIndex, importedColumn node with
    | Some(_, activeTable), Some column ->
        activeTable.AddColumn(column.Header, column.Cells)
        setArcFile (ArcFiles.refreshRef arcFile)
        true
    | _ -> false
