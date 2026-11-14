module Swate.Components.Composite.AnnotationTable.Helper

open ARCtrl
open Swate.Components

module TableHelper = Swate.Components.Composite.Table.Helper

/// Reorders a copy, retaining the table's name, headers, and complete cell values.
let reorderColumns (table: ArcTable) (columnOrder: int[]) =
    if Array.sort columnOrder <> [| 0 .. table.ColumnCount - 1 |] then
        invalidArg (nameof columnOrder) "Column order must contain every column index exactly once."

    let nextTable = table.Copy()
    let currentOrder = ResizeArray [ 0 .. table.ColumnCount - 1 ]

    for targetIndex in 0 .. columnOrder.Length - 1 do
        let sourceIndex = currentOrder.IndexOf columnOrder.[targetIndex]

        if sourceIndex <> targetIndex then
            nextTable.MoveColumn(sourceIndex, targetIndex)
            currentOrder.RemoveAt sourceIndex
            currentOrder.Insert(targetIndex, columnOrder.[targetIndex])

    nextTable

let duplicateRows (table: ArcTable) (coordinates: seq<CellCoordinate>) =
    let nextTable = table.Copy()

    // Insert from the bottom so every index still refers to its original row.
    for rowIndex in TableHelper.selectedRowIndices table.RowCount coordinates do
        let cells = table.GetRow(rowIndex) |> Seq.map _.Copy() |> ResizeArray
        nextTable.AddRow(cells, index = rowIndex + 1)

    nextTable

let canDuplicateColumns (table: ArcTable) (coordinates: seq<CellCoordinate>) =
    let indices = TableHelper.selectedColumnIndices table.ColumnCount coordinates

    indices.Length > 0
    && indices
       |> Array.forall (fun index ->
           ArcTableAux.tryFindDuplicateUnique table.Headers.[index] table.Headers
           |> Option.isNone
       )

let duplicateColumns (table: ArcTable) (coordinates: seq<CellCoordinate>) =
    let coordinates = coordinates |> Seq.toArray

    if not (canDuplicateColumns table coordinates) then
        invalidArg (nameof coordinates) "Select columns whose headers can occur more than once."

    let nextTable = table.Copy()

    for columnIndex in TableHelper.selectedColumnIndices table.ColumnCount coordinates do
        let column = table.GetColumn(columnIndex)
        let cells = column.Cells |> Seq.map _.Copy() |> ResizeArray
        nextTable.AddColumn(column.Header.Copy(), cells, index = columnIndex + 1)

    nextTable
