module Spreadsheet.KeyboardShortcuts

open Arcitisa.Components

let x = 0

// let onKeydownEvent (dispatch: Messages.Msg -> unit) =
//     fun (e: Browser.Types.Event) ->
//         let e = e :?> Browser.Types.KeyboardEvent

//         match (e.ctrlKey || e.metaKey), e.code with
//         // escape, enter, tab, shift
//         | false, kbdEventCode.escape
//         | false, kbdEventCode.enter
//         | false, kbdEventCode.tab
//         | false, kbdEventCode.shift -> ()
//         | false, kbdEventCode.delete -> // del
//             Spreadsheet.ClearSelected |> Messages.SpreadsheetMsg |> dispatch
//         | false, kbdEventCode.arrowLeft -> // arrow left
//             MoveSelectedCell Key.Left |> Messages.SpreadsheetMsg |> dispatch
//         | false, kbdEventCode.arrowUp -> // arrow up
//             MoveSelectedCell Key.Up |> Messages.SpreadsheetMsg |> dispatch
//         | false, kbdEventCode.arrowRight -> // arrow right
//             MoveSelectedCell Key.Right |> Messages.SpreadsheetMsg |> dispatch
//         | false, kbdEventCode.arrowDown -> // arrow down
//             MoveSelectedCell Key.Down |> Messages.SpreadsheetMsg |> dispatch
//         | false, key -> SetActiveCellFromSelected |> Messages.SpreadsheetMsg |> dispatch
//         // Ctrl + c
//         | true, _ ->
//             match e.code with
//             | "KeyC" -> // Ctrl + c
//                 Spreadsheet.CopySelectedCells |> Messages.SpreadsheetMsg |> dispatch
//             | "KeyX" -> // Ctrl + x
//                 Spreadsheet.CutSelectedCells |> Messages.SpreadsheetMsg |> dispatch
//             | "KeyV" -> // Ctrl + v
//                 Spreadsheet.PasteSelectedCells |> Messages.SpreadsheetMsg |> dispatch
//             | _ -> ()
