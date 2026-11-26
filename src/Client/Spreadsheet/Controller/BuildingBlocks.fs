module Spreadsheet.Controller.BuildingBlocks

open Spreadsheet
open Types
open ARCtrl
open Arcitisa.Components.Shared

open ExcelJS.Fable
open Excel
open GlobalBindings

open Regex
open Arcitisa.Components
open Arcitisa.Components.Shared


module SidebarControllerAux =

    /// <summary>
    /// Uses the first selected columnIndex from `state.SelectedCells` to determine if new column should be inserted or appended.
    /// </summary>
    /// <param name="state"></param>
    let getNextColumnIndex (index: int option) (state: Spreadsheet.Model) =
        // if cell is selected get column of selected cell we want to insert AFTER
        if index.IsSome then
            System.Math.Min(index.Value + 1, state.ActiveTable.ColumnCount)
        else
            state.ActiveTable.ColumnCount

module SanityChecks =
    /// Make sure only one column is selected
    let verifyOnlyOneColumnSelected (selectedCells: CellCoordinate[]) =
        let isOneColumn =
            let columnIndex = selectedCells.[0].x // can just use head of selected cells as all must be same column
            selectedCells |> Array.forall (fun coordinate -> coordinate.x = columnIndex)

        if not isOneColumn then
            failwith "Can only paste term in one column at a time!"


open SidebarControllerAux

let addBuildingBlock
    (index: int option)
    (newColumn: CompositeColumn)
    (state: Spreadsheet.Model)
    : Messages.Msg * Spreadsheet.Model =
    let table = state.ActiveTable
    // add one to last column index OR to selected column index to append one to the right.
    let mutable nextIndex = getNextColumnIndex index state
    let mutable newColumn = newColumn

    let msg =
        // nested if cases to only run table operations if necessary
        if newColumn.Header.isOutput then
            let hasOutput = table.TryGetOutputColumn()

            if hasOutput.IsSome then
                let txt =
                    $"Found existing output column. Changed output column to \"{newColumn.Header.ToString()}\"."


                let msg0 =
                    Messages.UpdateDisplayLogList [ Model.LogItem.createWarningNow txt ]
                    |> Messages.DevMsg

                newColumn <- {
                    newColumn with
                        Cells = hasOutput.Value.Cells
                }

                msg0
            else
                Messages.Msg.DoNothing
        elif newColumn.Header.isInput then
            let hasInput = table.TryGetInputColumn()

            if hasInput.IsSome then
                let txt =
                    $"Found existing input column. Changed input column to \"{newColumn.Header.ToString()}\"."

                let msg0 =
                    Messages.UpdateDisplayLogList [ Model.LogItem.createWarningNow txt ]
                    |> Messages.DevMsg

                newColumn <- {
                    newColumn with
                        Cells = hasInput.Value.Cells
                }

                msg0
            else
                Messages.Msg.DoNothing
        else
            Messages.Msg.DoNothing

    table.AddColumn(newColumn.Header, newColumn.Cells, nextIndex, true)
    msg, { state with ArcFile = state.ArcFile }


let addBuildingBlocks index (newColumns: CompositeColumn[]) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let table = state.ActiveTable
    let mutable newColumns = newColumns
    let mutable nextIndex = getNextColumnIndex index state
    table.AddColumns(newColumns, nextIndex)
    { state with ArcFile = state.ArcFile }

let addDataAnnotation
    (data:
        {|
            fragmentSelectors: string[]
            fileName: string
            fileType: string
            targetColumn: DataAnnotator.TargetColumn
        |})
    (state: Spreadsheet.Model)
    : Spreadsheet.Model =
    let table = state.ActiveTable

    let tryIfNone () =
        match table.TryGetInputColumn(), table.TryGetOutputColumn() with
        | Some _, None
        | None, None -> CompositeHeader.Output IOType.Data
        | None, Some _ -> CompositeHeader.Input IOType.Data
        | Some _, Some _ -> failwith "Both Input and Output columns exist and no target column was specified"

    let newHeader =
        match data.targetColumn with
        | DataAnnotator.TargetColumn.Input -> CompositeHeader.Input IOType.Data
        | DataAnnotator.TargetColumn.Output -> CompositeHeader.Output IOType.Data
        | DataAnnotator.TargetColumn.Autodetect -> tryIfNone ()

    if table.ColumnCount > 0 && data.fragmentSelectors.Length > table.RowCount then
        table.AddRowsEmpty(data.fragmentSelectors.Length - table.RowCount)

    let mkEmptyDataCell () =
        let d = Data()
        CompositeCell.createData d

    let targetRowCount = System.Math.Max(table.RowCount, data.fragmentSelectors.Length)

    let values = [|
        for rowIndex in 0 .. targetRowCount - 1 do
            let d = Data()

            if rowIndex < data.fragmentSelectors.Length then
                let selector = data.fragmentSelectors.[rowIndex]
                d.FilePath <- Some data.fileName
                d.Selector <- Some selector
                d.Format <- Some data.fileType
                d.SelectorFormat <- Some Arcitisa.Components.Shared.URLs.Data.SelectorFormat.csv
                CompositeCell.createData d
            else
                mkEmptyDataCell ()
    |]

    table.AddColumn(newHeader, values |> ResizeArray, forceReplace = true)
    { state with ArcFile = state.ArcFile }

let joinTable
    (tableToAdd: ArcTable)
    (index: int option)
    (options: TableJoinOptions option)
    (state: Spreadsheet.Model)
    (templateName: string option)
    : Spreadsheet.Model =

    if templateName.IsSome then
        //Should be updated to remove all kinds of extra symbols
        let templateName =
            System.Text.RegularExpressions.Regex.Replace(templateName.Value, "\W", "")

        let newTable =
            let body =
                state.ActiveTable.Columns |> Seq.map (fun column -> column.Cells) |> ResizeArray

            ArcTable.create (templateName, state.ActiveTable.Headers, body)

        state.ArcFile.Value.ArcTables().SetTable(state.ActiveTable.Name, newTable)

    let table = state.ActiveTable
    table.Join(tableToAdd, ?index = index, ?joinOptions = options, forceReplace = true)
    { state with ArcFile = state.ArcFile }

let insertTerm (term: OntologyAnnotation) (index: CellCoordinateRange) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let selected = CellCoordinateRange.toArray index |> Array.ofSeq
    SanityChecks.verifyOnlyOneColumnSelected selected

    for coordinate in selected do
        let c = Generic.getCell (coordinate.x, coordinate.y) state
        let newCell = c.UpdateWithOA term
        Controller.Generic.setCell (coordinate.x, coordinate.y) newCell state

    { state with ArcFile = state.ArcFile }
