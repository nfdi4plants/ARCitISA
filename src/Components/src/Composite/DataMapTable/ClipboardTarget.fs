module Swate.Components.Composite.DataMapTable.ClipboardTarget

open global.ARCtrl
open Swate.Components
open Swate.Components.ClipboardContract.Contract
open Swate.Components.Composite.Table.Types

[<AutoOpen>]
module ARCtrlExtensions =

    type Datamap with

        member private this.ApplyClipboardCell(targetCoordinate: CellCoordinate, source: CompositeCell) =
            let columnIndex = targetCoordinate.x - 1
            let rowIndex = targetCoordinate.y - 1
            let target = this.GetCell(columnIndex, rowIndex)

            let cell =
                match source, columnIndex with
                | CompositeCell.Unitized(_, unit), column when this.GetHeader(column).IsTermColumn ->
                    CompositeCell.createTerm unit
                | CompositeCell.Term term, column when this.GetHeader(column).IsTermColumn ->
                    CompositeCell.createTerm term
                | CompositeCell.FreeText _ as value, column when this.GetHeader(column).IsTermColumn ->
                    CompositeCell.createTerm (OntologyAnnotation.create (value.ToString()))
                | CompositeCell.Data _ as value, column when this.GetHeader(column).IsTermColumn ->
                    CompositeCell.createTerm (OntologyAnnotation.create (value.ToString()))
                | CompositeCell.Data _, DataMapIndices.Data -> source
                | _ -> target.UpdateMainField(source.ToVisibleString())

            this.SetCell(columnIndex, rowIndex, cell)

        member this.PasteStructuredCells
            (startCoordinate: CellCoordinate, selection: CellCoordinate[], source: CompositeCell[][])
            =
            let mapped = Mapping.map source startCoordinate selection

            let requiredRowCount =
                mapped
                |> Array.map (_.Target.y)
                |> Array.filter (fun row -> row > 0)
                |> Array.append [| this.RowCount |]
                |> Array.max

            if requiredRowCount > this.RowCount then
                this.DataContexts.AddRange(Array.init (requiredRowCount - this.RowCount) (fun _ -> DataContext()))

            mapped
            |> Array.filter (fun mapped ->
                mapped.Target.x > 0
                && mapped.Target.x <= this.ColumnCount
                && mapped.Target.y > 0
            )
            |> Array.iter (fun mapped -> this.ApplyClipboardCell(mapped.Target, mapped.Source))

        member this.PasteTabText(startCoordinate: CellCoordinate, selection: CellCoordinate[], clipboardText: string) =
            let rows =
                Swate.Components.ClipboardContract.Contract.PlainText.parseRows clipboardText

            let mapped = Mapping.map rows startCoordinate selection

            let requiredRowCount =
                mapped
                |> Array.map (_.Target.y)
                |> Array.filter (fun row -> row > 0)
                |> Array.append [| this.RowCount |]
                |> Array.max

            if requiredRowCount > this.RowCount then
                this.DataContexts.AddRange(Array.init (requiredRowCount - this.RowCount) (fun _ -> DataContext()))

            mapped
            |> Array.filter (fun mapped ->
                mapped.Target.x > 0
                && mapped.Target.x <= this.ColumnCount
                && mapped.Target.y > 0
            )
            |> Array.iter (fun mapped ->
                let columnIndex = mapped.Target.x - 1
                let rowIndex = mapped.Target.y - 1

                this.GetCell(columnIndex, rowIndex).UpdateMainField(mapped.Source)
                |> fun cell -> this.SetCell(columnIndex, rowIndex, cell)
            )
