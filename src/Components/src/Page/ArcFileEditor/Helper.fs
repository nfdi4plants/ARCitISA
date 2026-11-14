module Swate.Components.Page.ArcFileEditor.Helper

open Swate.Components.Shared
open Swate.Components
open ARCtrl
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Composite.Widgets.DataAnnotator.Types

[<Literal>]
let TableDragIdPrefix = "table-"

let tableDragId index = $"{TableDragIdPrefix}{index}"

let tableDragIds tableCount =
    Seq.init tableCount tableDragId |> ResizeArray

let tryGetAddRowsTarget (activeView: ActiveView, arcFileState: ArcFiles) =
    match activeView with
    | ActiveView.Table tableIndex ->
        arcFileState.TryGetActiveTable(Some tableIndex)
        |> Option.map (snd >> AddRowsTarget.Table)
    | ActiveView.DataMap -> arcFileState.TryGetDataMap() |> Option.map AddRowsTarget.DataMap
    | ActiveView.Metadata -> None

/// Searches the displayed cell values, in row order, using table UI coordinates.
let findSearchMatches (target: AddRowsTarget option) (query: string) : CellCoordinate[] =
    if System.String.IsNullOrWhiteSpace query then
        [||]
    else
        let query = query.ToLowerInvariant()

        let find rowCount columnCount getValue = [|
            for row in 0 .. rowCount - 1 do
                for column in 0 .. columnCount - 1 do
                    let value: string = getValue column row

                    if value.ToLowerInvariant().Contains query then
                        yield {| x = column + 1; y = row + 1 |}
        |]

        match target with
        | Some(AddRowsTarget.Table table) ->
            find table.RowCount table.ColumnCount (fun column row -> table.GetCellAt(column, row).ToString())
        | Some(AddRowsTarget.DataMap dataMap) ->
            find dataMap.RowCount dataMap.ColumnCount (fun column row -> dataMap.GetCell(column, row).ToString())
        | None -> [||]
