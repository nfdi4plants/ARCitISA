namespace Update

open Messages
open Elmish
open Spreadsheet
open Model
open Swate.Components.Shared
open FsSpreadsheet.Js
open ARCtrl
open ARCtrl.Spreadsheet

module Spreadsheet =

    let update
        (state: Spreadsheet.Model)
        (model: Model)
        (msg: Spreadsheet.Msg)
        : Spreadsheet.Model * Model * Cmd<Messages.Msg> =

        let innerUpdate (state: Spreadsheet.Model) (model: Model) (msg: Spreadsheet.Msg) =

            match msg with
            | UpdateState nextState -> nextState, model, Cmd.none
            | ImportJsonRaw importData ->
                let cmd =
                    Cmd.OfFunc.either
                        (Json.Import.parseFromJsonString)
                        (importData.jsonString, importData.jsonType, importData.filetype, importData.fileName)
                        (UpdateArcFile >> SpreadsheetMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | UpdateDatamap datamapOption ->
                let nextState = Controller.DataMap.updateDatamap datamapOption state
                nextState, model, Cmd.none
            | UpdateDataMapDataContextAt(index, dtx) ->
                let nextState = Controller.DataMap.updateDataMapDataContextAt dtx index state
                nextState, model, Cmd.none
            | AddTable table ->
                let nextState = Controller.Table.addTable table state
                nextState, model, Cmd.none
            | UpdateTable table ->
                let nextTable = table.Copy()
                let nextState = Controller.Table.updateTable nextTable state
                nextState, model, Cmd.none
            | CreateAnnotationTable usePrevOutput ->
                let nextState = Controller.Table.createTable usePrevOutput state
                nextState, model, Cmd.none
            | AddAnnotationBlock(index, column) ->
                let msg, nextState = Controller.BuildingBlocks.addBuildingBlock index column state
                let cmd = Cmd.ofMsg msg
                nextState, model, cmd
            | AddAnnotationBlocks(index, columns) ->
                let nextState = Controller.BuildingBlocks.addBuildingBlocks index columns state
                nextState, model, Cmd.none
            | AddDataAnnotation data ->
                let nextState =
                    match state.ActiveView with
                    | ActiveView.DataMap -> Controller.DataMap.addDataAnnotation data state
                    | ActiveView.Table _ -> Controller.BuildingBlocks.addDataAnnotation data state
                    | ActiveView.Metadata -> failwith "Unable to add data annotation in metadata view"

                nextState, model, Cmd.none
            | AddTemplates(tables, importType) ->
                let arcFile = model.SpreadsheetModel.ArcFile

                let updatedArcFile =
                    UpdateUtil.JsonImportHelper.updateTables
                        (tables |> ResizeArray)
                        importType
                        model.SpreadsheetModel.ActiveView.TryTableIndex
                        arcFile

                let nextState = {
                    state with
                        ArcFile = Some updatedArcFile
                }

                let nextModel: Model = {
                    model with
                        Model.ProtocolState.TemplatesSelected = []
                }

                nextState, nextModel, Cmd.none
            | JoinTable(table, index, options, templateName) ->
                let nextState =
                    Controller.BuildingBlocks.joinTable table index options state templateName

                nextState, model, Cmd.none
            | UpdateArcFile arcFile ->
                let nextState = {
                    state with
                        ArcFile = Some arcFile
                        ActiveView = ActiveView.Forward(arcFile, state.ActiveView)
                }

                nextState, model, Cmd.none
            | InitFromArcFile arcFile ->
                let nextState =
                    Spreadsheet.Model.init (arcFile, ActiveView.Forward(arcFile, ActiveView.Metadata))

                nextState, model, Cmd.none
            | InsertOntologyAnnotation(range, oa) ->
                let nextState = Controller.BuildingBlocks.insertTerm oa range state
                nextState, model, Cmd.none
            | InsertOntologyAnnotations oas ->
                failwith "InsertOntologyTerms not implemented in Spreadsheet.Update"
                //let cmd = createPromiseCmd <| fun _ -> Controller.insertTerms termMinimals state
                let cmd = Cmd.none
                state, model, cmd
            | UpdateCell(index, cell) ->
                Controller.Generic.setCell (index.x, index.y) cell state
                let nextState = { state with ArcFile = state.ArcFile }
                nextState, model, Cmd.none
            | UpdateCells arr ->
                Controller.Generic.setCells arr state
                let nextState = { state with ArcFile = state.ArcFile }
                nextState, model, Cmd.none
            | UpdateHeader(index, header) ->
                let nextState =
                    state.ActiveTable.UpdateHeader(index, header)
                    { state with ArcFile = state.ArcFile }

                nextState, model, Cmd.none
            | UpdateActiveView nextView ->
                let nextState = { state with ActiveView = nextView }

                nextState, model, Cmd.none
            | RemoveTable removeIndex ->
                let nextState = Controller.Table.removeTable removeIndex state
                nextState, model, Cmd.none
            | RenameTable(index, name) ->
                let nextState = Controller.Table.renameTable index name state
                nextState, model, Cmd.none
            | UpdateTableOrder(prev_index, new_index) ->
                let nextState = Controller.Table.updateTableOrder (prev_index, new_index) state
                nextState, model, Cmd.none
            | AddRows(n) ->
                let nextState =
                    if state.TableViewIsActive() then
                        Controller.Table.addRows n state
                    else
                        Controller.DataMap.addRows n state

                nextState, model, Cmd.none
            | Reset ->
                let nextState = Controller.Table.resetTableState ()

                let nextModel = {
                    model with
                        History = LocalHistory.Model.init ()
                }

                nextState, nextModel, Cmd.none
            | DeleteRow index ->
                let nextState =
                    if state.TableViewIsActive() then
                        Controller.Table.deleteRow index state
                    else
                        Controller.DataMap.deleteRow index state

                nextState, model, Cmd.none
            | DeleteRows indexArr ->
                let nextState =
                    if state.TableViewIsActive() then
                        Controller.Table.deleteRows indexArr state
                    else
                        Controller.DataMap.deleteRows indexArr state

                nextState, model, Cmd.none
            | DeleteColumn index ->
                let nextState = Controller.Table.deleteColumn index state
                nextState, model, Cmd.none
            | SetColumn(index, column) ->
                let nextState = Controller.Table.setColumn index column state
                nextState, model, Cmd.none
            | MoveColumn(current, next) ->
                let nextState = Controller.Table.moveColumn current next state
                nextState, model, Cmd.none
            | Clear indices ->
                let nextState = Controller.Table.clearCells indices state
                nextState, model, Cmd.none
            | FillColumnWithTerm index ->
                let nextState = Controller.Table.fillColumnWithCell index state
                nextState, model, Cmd.none
            //| EditColumn (columnIndex, newCellType, b_type) ->
            //    let cmd = createPromiseCmd <| fun _ -> Controller.editColumn (columnIndex, newCellType, b_type) state
            //    state, model, cmd
            | ImportXlsx bytes ->
                let cmd =
                    Cmd.OfPromise.either
                        Spreadsheet.IO.Xlsx.readFromBytes
                        bytes
                        (UpdateArcFile >> Messages.SpreadsheetMsg)
                        (Messages.curry Messages.GenericError Cmd.none >> Messages.DevMsg)

                state, model, cmd
            | ExportJson(arcfile, jef) ->
                let jsonExport = Json.Export.parseToJsonString (arcfile, jef)
                UpdateUtil.downloadFromString (jsonExport)
                state, model, Cmd.none
            | ExportXlsx arcfile ->
                let name, fswb =
                    let n = System.DateTime.Now.ToUniversalTime().ToString("yyyyMMdd_hhmmss")

                    match arcfile with
                    | ArcFiles.Investigation ai -> n + "_" + ArcInvestigation.FileName, ArcInvestigation.toFsWorkbook ai
                    | ArcFiles.Study(as', aaList) -> n + "_" + ArcStudy.FileName, ArcStudy.toFsWorkbook (as', aaList)
                    | ArcFiles.Assay aa -> n + "_" + ArcAssay.FileName, ArcAssay.toFsWorkbook aa
                    | ArcFiles.Template t -> n + "_" + t.FileName, Spreadsheet.Template.toFsWorkbook t
                    | ArcFiles.Run r -> n + "_" + ArcRun.FileName, ArcRun.toFsWorkbook r
                    | ArcFiles.Workflow w -> n + "_" + ArcWorkflow.FileName, ArcWorkflow.toFsWorkbook w
                    | ArcFiles.DataMap(_, d) -> n + "_" + "datamap.xlsx", Spreadsheet.Datamap.toFsWorkbook d

                let cmd =
                    Cmd.OfPromise.either
                        Xlsx.toXlsxBytes
                        fswb
                        (fun bytes -> ExportXlsxDownload(name, bytes) |> Messages.SpreadsheetMsg)
                        (Messages.curry Messages.GenericError Cmd.none >> Messages.DevMsg)

                state, model, cmd
            | ExportXlsxDownload(name, xlsxBytes) ->
                let _ = UpdateUtil.download (name, xlsxBytes)
                state, model, Cmd.none
            | SetCell(x, y) ->
                failwith "SetCell not implemented in Spreadsheet.Update"
                state, model, Cmd.none

        try
            innerUpdate state model msg
        with e ->
            let cmd = GenericError(Cmd.none, e) |> DevMsg |> Cmd.ofMsg
            state, model, cmd
