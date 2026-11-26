namespace Update

// <-->
// This order is required to correctly inferre the correct "Msg"s below.
// do not touch and remove
open Messages
open OfficeInterop
open SpreadsheetInterface
open Arcitisa.Components.Shared
// </-->

open Elmish
open Model
open ARCtrl
open Fable.Core.JsInterop

open ARCtrl
open ArcTableHelper
open Arcitisa.Components

module private ModelUtil =

    open LocalHistory
    open Model

    type Model.Model with
        member this.UpdateFromLocalStorage() =

            match this.PersistentStorageState.Host with
            | Some Arcitisahost.Browser -> {
                this with
                    Model.SpreadsheetModel = Spreadsheet.Model.fromLocalStorage ()
                    Model.History = this.History.UpdateFromSessionStorage()
              }
            | _ -> this

open ModelUtil

/// This seems like such a hack :(
module private ExcelHelper =

    open Fable.Core
    open ExcelJS.Fable.GlobalBindings

    let initializeAddIn () =
        Office.onReady().``then`` (fun _ -> ()) |> Async.AwaitPromise

    /// Office-js will kill iframe loading in ARCitect, therefore we must load it conditionally
    let addOfficeJsScript (callback: unit -> unit) =
        let cdn = @"https://appsforoffice.microsoft.com/lib/1/hosted/office.js"
        let _type = "text/javascript"
        let s = Browser.Dom.document.createElement ("script")
        s?``type`` <- _type
        s?src <- cdn
        Browser.Dom.document.head.appendChild s |> ignore
        s.onload <- fun _ -> callback ()
        ()

    /// Make a function that loops short sleep sequences until a mutable variable is set to true
    /// do mutabel dotnet ref for variable
    let myAwaitLoadedThenInit (loaded: ref<bool>) =
        let rec loop () = async {
            if loaded.Value then
                do! initializeAddIn ()
            else
                do! Async.Sleep 100
                do! loop ()
        }

        loop ()

    let officeload () =
        let loaded = ref false

        async {
            addOfficeJsScript (fun _ -> loaded.Value <- true)
            do! myAwaitLoadedThenInit loaded
        }

//open Fable.Core.JS

module Interface =

    open LocalHistory
    open Model

    let update (model: Model) (msg: SpreadsheetInterface.Msg) : Model * Cmd<Messages.Msg> =
        let host = model.PersistentStorageState.Host

        let innerUpdate (model: Model) (msg: SpreadsheetInterface.Msg) =
            match msg with
            | Initialize host ->
                let cmd =
                    match host with
                    | Arcitisahost.Excel ->
                        ExcelHelper.officeload () |> Async.StartImmediate
                        Cmd.none
                    | Arcitisahost.Browser ->
                        Spreadsheet.Model.initHistoryIndexedDB () |> Promise.start
                        Cmd.none
                    | Arcitisahost.ARCitect ->
                        LocalHistory.Model.ResetHistoryWebStorage()
                        Start() |> ARCitect.Init |> ARCitectMsg |> Cmd.ofMsg

                /// Updates from local storage if standalone in browser
                let nextModel = model.UpdateFromLocalStorage()
                nextModel, cmd
            | CreateAnnotationTable usePrevOutput ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd =
                        OfficeInterop.CreateAnnotationTable usePrevOutput
                        |> OfficeInteropMsg
                        |> Cmd.ofMsg

                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        Spreadsheet.CreateAnnotationTable usePrevOutput |> SpreadsheetMsg |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | UpdateDatamap datamapOption ->
                match host with
                | Some Arcitisahost.Excel ->
                    failwith "UpdateDatamap not implemented for Excel"
                    model, Cmd.none
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = Spreadsheet.UpdateDatamap datamapOption |> SpreadsheetMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | UpdateDataMapDataContextAt(index, dc) ->
                match host with
                | Some Arcitisahost.Excel ->
                    //let cmd = OfficeInterop.UpdateDataContextAt (dc, index) |> OfficeInteropMsg |> Cmd.ofMsg
                    failwith "UpdateDataContextAt not implemented for Excel"
                    model, Cmd.none
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        Spreadsheet.UpdateDataMapDataContextAt(index, dc) |> SpreadsheetMsg |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | AddTable table ->
                match host with
                | Some Arcitisahost.Excel ->
                    //let cmd = OfficeInterop.AddTable table |> OfficeInteropMsg |> Cmd.ofMsg
                    failwith "AddTable not implemented for Excel"
                    model, Cmd.none
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = Spreadsheet.AddTable table |> SpreadsheetMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | AddAnnotationBlock(index, minBuildingBlockInfo) ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd =
                        OfficeInterop.AddAnnotationBlock minBuildingBlockInfo
                        |> OfficeInteropMsg
                        |> Cmd.ofMsg

                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        Spreadsheet.AddAnnotationBlock(index, minBuildingBlockInfo)
                        |> SpreadsheetMsg
                        |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | ValidateBuildingBlock ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInterop.ValidateBuildingBlock |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | AddAnnotationBlocks(index, compositeColumns) ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd =
                        OfficeInterop.AddAnnotationBlocks compositeColumns
                        |> OfficeInteropMsg
                        |> Cmd.ofMsg

                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        Spreadsheet.AddAnnotationBlocks(index, compositeColumns)
                        |> SpreadsheetMsg
                        |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | AddTemplates(tables, importType) ->

                match host with
                | Some Arcitisahost.Excel ->
                    let cmd =
                        OfficeInterop.AddTemplates(Array.ofList tables, importType)
                        |> OfficeInteropMsg
                        |> Cmd.ofMsg

                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        Spreadsheet.AddTemplates(Array.ofList tables, importType)
                        |> SpreadsheetMsg
                        |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | JoinTable(table, index, options) ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInterop.JoinTable(table, options) |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        Spreadsheet.JoinTable(table, index, options, None)
                        |> SpreadsheetMsg
                        |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | UpdateArcFile arcFiles ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInterop.UpdateArcFile arcFiles |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = Spreadsheet.UpdateArcFile arcFiles |> SpreadsheetMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | ImportXlsx bytes ->
                match host with
                | Some Arcitisahost.Excel ->
                    Browser.Dom.window.alert "ImportXlsx Not implemented"
                    model, Cmd.none
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = Spreadsheet.ImportXlsx bytes |> SpreadsheetMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | ImportJson data ->
                let deselectedColumns = data.deselectedColumns

                match host with
                | Some Arcitisahost.Excel ->
                    /// In Excel we must get the current information from worksheets and update them with the imported information
                    let updateExistingInfoWithImportedInfo () = promise {
                        match data.importState.ImportMetadata with
                        | true -> // full import, does not require additional information
                            return
                                UpdateUtil.JsonImportHelper.updateWithMetadata
                                    data.importedFile
                                    data.importState
                                    deselectedColumns
                        | false -> // partial import, requires additional information
                            let! arcfile = OfficeInterop.Core.Main.tryParseToArcFile ()
                            let arcfileOpt = arcfile |> Result.toOption

                            let! activeTable =
                                ExcelJS.Fable.GlobalBindings.Excel.run (fun context ->
                                    ArcTable.tryGetActiveArcTable (context)
                                )

                            let activeTableIndex =
                                match arcfileOpt, activeTable with
                                | Some arcfile, Ok activeTable ->
                                    arcfile.ArcTables() |> Seq.tryFindIndex (fun table -> table = activeTable)
                                | _ -> None

                            return
                                UpdateUtil.JsonImportHelper.updateArcFileTables
                                    data.importedFile
                                    data.importState
                                    activeTableIndex
                                    arcfileOpt
                    }

                    let updateArcFile (arcFile: ArcFiles) =
                        SpreadsheetInterface.UpdateArcFile arcFile |> InterfaceMsg

                    let cmd =
                        Cmd.OfPromise.either
                            updateExistingInfoWithImportedInfo
                            ()
                            updateArcFile
                            (curry GenericError Cmd.none >> DevMsg)

                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd =
                        match data.importState.ImportMetadata with
                        | true ->
                            UpdateUtil.JsonImportHelper.updateWithMetadata
                                data.importedFile
                                data.importState
                                deselectedColumns
                        | false ->
                            UpdateUtil.JsonImportHelper.updateArcFileTables
                                data.importedFile
                                data.importState
                                model.SpreadsheetModel.ActiveView.TryTableIndex
                                model.SpreadsheetModel.ArcFile
                        |> SpreadsheetInterface.UpdateArcFile
                        |> InterfaceMsg
                        |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"
            | InsertOntologyAnnotation(range, ontologyAnnotation) ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd =
                        OfficeInterop.InsertOntologyTerm ontologyAnnotation
                        |> OfficeInteropMsg
                        |> Cmd.ofMsg

                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    match range with
                    | Some range ->

                        let cmd =
                            Spreadsheet.InsertOntologyAnnotation(range, ontologyAnnotation)
                            |> SpreadsheetMsg
                            |> Cmd.ofMsg

                        model, cmd
                    | None -> failwith "not implemented"
                | _ -> failwith "not implemented"
            | InsertFileNames(range, fileNames) ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInterop.InsertFileNames fileNames |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    if range.IsSome then
                        let columnIndex, rowIndex =
                            if range.IsSome then
                                range.Value.xStart, range.Value.yStart
                            else
                                [||] |> Array.min

                        let mutable rowIndex = rowIndex

                        let cells = [|
                            for name in fileNames do
                                match model.SpreadsheetModel.ActiveTable.TryGetCellAt(columnIndex, rowIndex) with
                                | Some c ->
                                    let cell = c.UpdateMainField name
                                    let coordinate: CellCoordinate = {| x = columnIndex; y = rowIndex |}
                                    coordinate, cell
                                    rowIndex <- rowIndex + 1
                                | None -> ()
                        |]

                        let cmd = Spreadsheet.UpdateCells cells |> SpreadsheetMsg |> Cmd.ofMsg
                        model, cmd
                    else
                        model, Cmd.ofMsg (DevMsg.GenericError(Cmd.none, exn ("No cell(s) selected.")) |> DevMsg)
                | _ -> failwith "not implemented"
            | RemoveBuildingBlock index ->
                match host, index with
                | Some Arcitisahost.Excel, _ ->
                    let cmd = OfficeInterop.RemoveBuildingBlock |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | Some Arcitisahost.Browser, Some index
                | Some Arcitisahost.ARCitect, Some index ->

                    let deselectedColumns =
                        CellCoordinateRange.toArray index
                        |> Array.ofSeq
                        |> Array.map (fun index -> index.x)

                    let distinct = deselectedColumns |> Array.distinct

                    let cmd =
                        if distinct.Length <> 1 then
                            let msg =
                                exn "Please select one column only if you want to use `Remove Building Block`."

                            GenericError(Cmd.none, msg) |> DevMsg |> Cmd.ofMsg
                        else
                            Spreadsheet.DeleteColumn(distinct.[0]) |> SpreadsheetMsg |> Cmd.ofMsg

                    model, cmd
                | _, _ -> failwith "not implemented"
            | AddDataAnnotation data ->
                match host with
                | Some Arcitisahost.Excel ->
                    //let cmd = OfficeInterop.AddDataAnnotation data |> OfficeInteropMsg |> Cmd.ofMsg
                    failwith "AddDataAnnotation is not implemented"
                    model, Cmd.none
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = Spreadsheet.AddDataAnnotation data |> SpreadsheetMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | ExportJson(arcfile, jef) ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInteropMsg(OfficeInterop.ExportJson(arcfile, jef)) |> Cmd.ofMsg
                    model, cmd
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = SpreadsheetMsg(Spreadsheet.ExportJson(arcfile, jef)) |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | UpdateUnitForCells ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInterop.UpdateUnitForCells |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | RectifyTermColumns ->
                match host with
                | Some Arcitisahost.Excel ->
                    let cmd = OfficeInterop.RectifyTermColumns |> OfficeInteropMsg |> Cmd.ofMsg
                    model, cmd
                | _ -> failwith "not implemented"
            | ImportRawJson data ->
                match host with
                | Some Arcitisahost.Excel -> failwith "not implemented"
                | Some Arcitisahost.Browser
                | Some Arcitisahost.ARCitect ->
                    let cmd = Spreadsheet.ImportJsonRaw data |> SpreadsheetMsg |> Cmd.ofMsg

                    model, cmd
                | _ -> failwith "not implemented"

        try
            innerUpdate model msg
        with e ->
            let cmd = GenericError(Cmd.none, e) |> DevMsg |> Cmd.ofMsg
            model, cmd
