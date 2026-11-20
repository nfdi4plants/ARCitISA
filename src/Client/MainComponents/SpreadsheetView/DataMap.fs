module MainComponents.SpreadsheetView.DataMap

open ARCtrl
open Feliz
open Model
open SpreadsheetInterface
open Messages
open Swate.Components.Shared

open Swate.Components
open Swate.Components.Composite.DataMapTable

let Main (model: Model, dispatch: Msg -> unit) =

    let datamap = model.SpreadsheetModel.DataMapOrDefault

    let setDatamap =
        fun (dm: Datamap) -> Spreadsheet.UpdateDatamap(Some dm) |> SpreadsheetMsg |> dispatch

    DataMapTable.DataMapTable(datamap, setDatamap)
