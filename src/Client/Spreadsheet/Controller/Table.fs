module Spreadsheet.Controller.Table

open System.Collections.Generic
open Spreadsheet
open Types
open ARCtrl
open Arcitisa.Components
open Arcitisa.Components.Shared

module ControllerTableAux =

    let rec createNewTableName (ind: int) names =
        let name = "NewTable" + string ind

        if Seq.contains name names then
            createNewTableName (ind + 1) names
        else
            name

    let findEarlierTable (tableIndex: int) (tables: ArcTables) =
        let indices = [ 0 .. tables.TableCount - 1 ]
        let lower = indices |> Seq.tryFindBack (fun k -> k < tableIndex)
        Option.map (fun i -> i, tables.GetTableAt i) lower

    let findLaterTable (tableIndex: int) (tables: ArcTables) =
        let indices = [ 0 .. tables.TableCount - 1 ]
        let higher = indices |> Seq.tryFind (fun k -> k > tableIndex)
        Option.map (fun i -> i, tables.GetTableAt i) higher

    let findNeighborTables (tableIndex: int) (tables: ArcTables) =
        findEarlierTable tableIndex tables, findLaterTable tableIndex tables

open ControllerTableAux

let switchTable (nextIndex: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    match state.ActiveView with
    | ActiveView.Table i when i = nextIndex -> state
    | _ -> {
        state with
            ActiveView = ActiveView.Table nextIndex
      }

/// <summary>This is the basic function to create new Tables from an array of ArcitisaBuildingBlocks</summary>
let addTable (newTable: ArcTable) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.Tables.AddTable(newTable)
    switchTable (state.Tables.TableCount - 1) state

/// <summary>This is the basic function to update an existing Table from an array of ArcitisaBuildingBlocks</summary>
let updateTable (newTable: ArcTable) (state: Spreadsheet.Model) : Spreadsheet.Model =

    match state.ArcFile with
    | Some(ArcFiles.Template t) -> t.Table <- newTable
    | _ -> state.Tables.UpdateTable(newTable.Name, newTable)

    state

/// <summary>This function is used to create multiple tables at once.</summary>
let addTables (tables: ArcTable[]) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.Tables.AddTables(tables)
    switchTable (state.Tables.TableCount - 1) state


/// <summary>Adds the most basic empty ARCitISA table with auto generated name.</summary>
let createTable (usePrevOutput: bool) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let tables = state.ArcFile.Value.ArcTables()
    let newName = createNewTableName 0 tables.TableNames
    let newTable = ArcTable.init (newName)

    if usePrevOutput && ((tables.TableCount - 1) >= state.ActiveView.ViewIndex) then
        let table = tables.GetTableAt(state.ActiveView.ViewIndex)
        let output = table.GetOutputColumn()
        let newInput = output.Header.TryOutput().Value |> CompositeHeader.Input
        newTable.AddColumn(newInput, output.Cells, forceReplace = true)

    let nextState = { state with ArcFile = state.ArcFile }
    addTable newTable nextState

let updateTableOrder (prevIndex: int, newIndex: int) (state: Spreadsheet.Model) =
    state.Tables.MoveTable(prevIndex, newIndex)
    { state with ArcFile = state.ArcFile }

let resetTableState () : Spreadsheet.Model =
    LocalHistory.Model.ResetHistoryWebStorage()
    Spreadsheet.Model.init ()

let renameTable (tableIndex: int) (newName: string) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.Tables.RenameTableAt(tableIndex, newName)
    { state with ArcFile = state.ArcFile }

let removeTable (removeIndex: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    if state.Tables.TableCount = 0 then
        state
    else
        state.Tables.RemoveTableAt removeIndex
        // if active table is removed get the next closest table and set it active
        match state.ActiveView with
        | ActiveView.Table i when i = removeIndex ->
            let neighbors = findNeighborTables removeIndex state.Tables

            match neighbors with
            | Some(i, _), _ -> switchTable i state
            | None, Some(i, _) -> switchTable i state
            | _ -> {
                state with
                    ActiveView = ActiveView.Metadata
              }
        | ActiveView.Table i -> // Tables still exist and an inactive one was removed. Just remove it.
            let nextTableIndex = if i > removeIndex then i - 1 else i

            {
                state with
                    ActiveView = ActiveView.Table nextTableIndex
            }
        | _ -> {
            state with
                ActiveView = ActiveView.Metadata
          }

///<summary>Add `n` rows to active table.</summary>
let addRows (n: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.ActiveTable.AddRowsEmpty(n)
    { state with ArcFile = state.ArcFile }

let deleteRow (index: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.ActiveTable.RemoveRow index

    { state with ArcFile = state.ArcFile }

let deleteRows (indexArr: int[]) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.ActiveTable.RemoveRows indexArr

    { state with ArcFile = state.ArcFile }


let deleteColumn (index: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.ActiveTable.RemoveColumn index

    { state with ArcFile = state.ArcFile }

let setColumn (index: int) (column: CompositeColumn) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.ActiveTable.UpdateColumn(index, column.Header, column.Cells)

    { state with ArcFile = state.ArcFile }

let moveColumn (current: int) (next: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.ActiveTable.MoveColumn(current, next)

    { state with ArcFile = state.ArcFile }

let fillColumnWithCell (index: CellCoordinate) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let cell = Generic.getCell (index.x, index.y) state

    for ri in 0 .. Generic.getRowCount state - 1 do
        let copy = cell.Copy()
        Generic.setCell (index.x, ri) copy state

    { state with ArcFile = state.ArcFile }

/// <summary>
/// Transform cells of given indices to their empty equivalents
/// </summary>
/// <param name="indexArr"></param>
/// <param name="state"></param>
let clearCells (indexArr: CellCoordinate[]) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let newCells = [|
        for index in indexArr do
            let cell = Generic.getCell (index.x, index.y) state
            let emptyCell = cell.GetEmptyCellFixed()
            index, emptyCell
    |]

    Generic.setCells newCells state
    state

open Fable.Core.JsInterop
open System

let selectRelativeCell (index: int * int) (move: int * int) (maxColumnIndex: int) (maxRowIndex: int) =
    //let index =
    //    match index with
    //    | U2.Case2 index -> index,-1
    //    | U2.Case1 index -> index
    let columnIndex = Math.Min(Math.Max(fst index + fst move, 0), maxColumnIndex)
    let rowIndex = Math.Min(Math.Max(snd index + snd move, 0), maxRowIndex)
    //if rowIndex = -1 then
    //    U2.Case2 columnIndex
    //else
    //    U2.Case1 (columnIndex, rowIndex)
    columnIndex, rowIndex


// Ui depends on main column name, maybe change this to depends on BuildingBlockType?
// Header main column name must be updated

//let editColumn (columnIndex: int, newType: ArcitisaCell, b_type: BuildingBlockType option)  (state: Spreadsheet.Model) : Spreadsheet.Model =
//    let table = state.ActiveTable
//    let updateHeader (header: ArcitisaCell) =
//        match newType with
//        | IsUnit _ -> header.toUnitHeader(?b_type = b_type)
//        | IsTerm _ -> header.toTermHeader(?b_type = b_type)
//        | IsFreetext _ -> header.toFreetextHeader(?b_type = b_type)
//        | IsHeader _ -> failwith "This is no viable input."
//        |> IsHeader
//    let updateBody (cell: ArcitisaCell) =
//        match newType with
//        | IsUnit _ -> cell.toUnitCell()
//        | IsTerm _ -> cell.toTermCell()
//        | IsFreetext _ -> cell.toFreetextCell()
//        | IsHeader _ -> failwith "This is no viable input."
//    let nextTable =
//        table
//        |> Map.map (fun (c,r) cv ->
//            match r with
//            | 0 when c = columnIndex -> updateHeader cv
//            | _ when c = columnIndex -> updateBody cv
//            | _ -> cv
//        )
//    {state with ActiveTable = nextTable}
