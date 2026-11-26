module MainComponents.SpreadsheetView.DataMap

open ARCtrl
open Feliz
open Model
open SpreadsheetInterface
open Messages
open Arcitisa.Components.Shared

open Arcitisa.Components
open Arcitisa.Components.Composite.DataMapTable

let Main (model: Model, dispatch: Msg -> unit) =

    let datamap = model.SpreadsheetModel.DataMapOrDefault

    let setDatamap =
        fun (dm: Datamap) -> Spreadsheet.UpdateDatamap(Some dm) |> SpreadsheetMsg |> dispatch

    DataMapTable.DataMapTable(datamap, setDatamap)
