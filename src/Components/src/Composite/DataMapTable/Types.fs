module Swate.Components.Composite.DataMapTable.Types

open Swate.Components
open Swate.Components.Composite.Table.Types

[<RequireQualifiedAccess>]
type Modal = Details of CellCoordinate

[<AutoOpen>]
module ARCtrlExtensions =

    open ARCtrl
    open Swate.Components
    open Helper
    open ArcTableAux

    type Datamap with

        member this.ClearCells(coordinates: seq<CellCoordinate>) =
            coordinates
            |> Seq.distinct
            |> Seq.iter (fun coordinate -> this.Clear(coordinate.x - 1, coordinate.y - 1))

        member this.ClearSelectedCells(selectHandle: SelectHandle) =
            match selectHandle.getCount () with
            | c when c <= 100 ->
                let selectedCells = selectHandle.getSelectedCells ()

                this.ClearCells(selectedCells)
            | _ ->
                for col in 0 .. this.ColumnCount - 1 do
                    for row in 0 .. this.RowCount - 1 do
                        if selectHandle.contains ({| x = col + 1; y = row + 1 |}) then
                            this.Clear(col, row)
