module Swate.Components.Composite.Table.Helper

open Feliz
open Swate.Components
open Swate.Components.Composite.Table.Types

type ATCMC =
    static member Icon(className: string) = Html.i [ prop.className className ]

    static member KbdHint(text: string, ?label: string) =
        let label = defaultArg label text

        {|
            element =
                Html.kbd [
                    prop.className "swt:ml-auto swt:kbd swt:kbd-sm"
                    prop.text text
                ]
            label = label
        |}

let fillColumn
    (rowCount: int)
    (coordinate: CellCoordinate)
    (getCell: CellCoordinate -> 'Cell)
    (copyCell: 'Cell -> 'Cell)
    (setCell: CellCoordinate -> 'Cell -> unit)
    =
    let sourceCell = getCell coordinate

    for row in 1..rowCount do
        setCell {| coordinate with y = row |} (copyCell sourceCell)

let selectedRowIndices (rowCount: int) (coordinates: seq<CellCoordinate>) =
    coordinates
    |> Seq.map (fun coordinate -> coordinate.y - 1)
    |> Seq.filter (fun rowIndex -> rowIndex >= 0 && rowIndex < rowCount)
    |> Seq.distinct
    |> Seq.sortDescending
    |> Seq.toArray

let selectedColumnIndices (columnCount: int) (coordinates: seq<CellCoordinate>) =
    coordinates
    |> Seq.map (fun coordinate -> coordinate.x - 1)
    |> Seq.filter (fun columnIndex -> columnIndex >= 0 && columnIndex < columnCount)
    |> Seq.distinct
    |> Seq.sortDescending
    |> Seq.toArray

let targetCoordinates (coordinate: CellCoordinate) (selectHandle: SelectHandle) =
    if selectHandle.contains coordinate then
        selectHandle.getSelectedCells () |> Seq.toArray
    else
        [| coordinate |]
