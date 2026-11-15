module Swate.Components.Composite.AnnotationTable.Types

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Composite.Table.Types

module AnnotationTableContextMenu =

    open ARCtrl

    type PasteCases =
        | AddColumns of
            {|
                data: ResizeArray<CompositeColumn>
                coordinate: CellCoordinate
                coordinates: CellCoordinate[][]
            |}
        | PasteCells of
            {|
                data: ResizeArray<CompositeColumn>
                coordinates: CellCoordinate[][]
            |}
        | Unknown of
            {|
                data: string[][]
                headers: CompositeHeader[]
            |}

module AnnotationTable =

    open AnnotationTableContextMenu

    [<RequireQualifiedAccess>]
    type ModalTypes =
        | Details of CellCoordinate
        | Transform of CellCoordinate
        | Edit of CellCoordinate
        | PasteCaseUserInput of PasteCases * SelectHandle
        /// 👀 Uses CellCoordinate to identify if clicked cell is part of selected range
        | MoveColumn of uiTableIndex: CellCoordinate * arcTableIndex: CellCoordinate
        | Error of string
        | UnknownPasteCase of PasteCases
        | None

[<AutoOpen>]
module ARCtrlExtensions =

    open ARCtrl

    type ArcTable with
        member this.ClearSelectedCells(selectHandle: SelectHandle) =
            let selectedCells = selectHandle.getSelectedCells ()

            let indices =
                selectedCells
                |> Seq.filter (fun i -> i.x > 0 && i.x <= this.ColumnCount && i.y > 0 && i.y <= this.RowCount)
                |> Seq.map (fun i -> (i.x - 1, i.y - 1))
                |> Seq.toArray

            indices
            |> Array.iter (fun (ci, ri) ->
                let tempIndex = (ci, ri)
                let prev = this.GetCellAt(tempIndex)
                let next = prev.GetEmptyCellFixed()

                this.SetCellAt(ci, ri, next, true)
            )
