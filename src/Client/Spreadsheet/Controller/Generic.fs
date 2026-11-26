module Spreadsheet.Controller.Generic

open Spreadsheet
open ARCtrl
open Arcitisa.Components
open Arcitisa.Components.Shared

let getCell ((ci, ri): int * int) (state: Spreadsheet.Model) : CompositeCell =
    match state.ActiveView with
    | ActiveView.Table _ -> state.ActiveTable.GetCellAt(ci, ri)
    | ActiveView.DataMap -> state.DataMapOrDefault.GetCell(ci, ri)
    | ActiveView.Metadata -> failwith "Cannot get cell in metadata view"

let setCell ((ci, ri): int * int) (cell: CompositeCell) (state: Spreadsheet.Model) : unit =
    match state.ActiveView with
    | ActiveView.Table _ -> state.ActiveTable.SetCellAt(ci, ri, cell)
    | ActiveView.DataMap -> state.DataMapOrDefault.SetCell(ci, ri, cell)
    | ActiveView.Metadata -> failwith "Cannot set cell in metadata view"

let setCells (cells: (CellCoordinate * CompositeCell)[]) (state: Spreadsheet.Model) : unit =
    match state.ActiveView with
    | ActiveView.Table _ -> state.ActiveTable.SetCellsAt cells
    | ActiveView.DataMap ->
        for (cellCoordinate, cell) in cells do
            state.DataMapOrDefault.SetCell(cellCoordinate.x, cellCoordinate.y, cell)
    | ActiveView.Metadata -> failwith "Unable to UpdateCell on Metadata sheet"

let getHeader (index: int) (state: Spreadsheet.Model) : CompositeHeader =
    match state.ActiveView with
    | ActiveView.Table _ -> state.ActiveTable.Headers.[index]
    | ActiveView.DataMap -> state.DataMapOrDefault.GetHeader(index)
    | ActiveView.Metadata -> failwith "Cannot get header in metadata view"

let getRowCount (state: Spreadsheet.Model) : int =
    match state.ActiveView with
    | ActiveView.Table _ -> state.ActiveTable.RowCount
    | ActiveView.DataMap -> state.DataMapOrDefault.RowCount
    | ActiveView.Metadata -> failwith "Cannot get row count in metadata view"

let getColCount (state: Spreadsheet.Model) : int =
    match state.ActiveView with
    | ActiveView.Table _ -> state.ActiveTable.ColumnCount
    | ActiveView.DataMap -> state.DataMapOrDefault.ColumnCount
    | ActiveView.Metadata -> failwith "Cannot get column count in metadata view"
