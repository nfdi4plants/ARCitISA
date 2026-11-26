namespace Update

open Elmish

open Messages
open OfficeInterop
open OfficeInterop.Core
open Model
open Arcitisa.Components.Shared
open ARCtrl

module OfficeInterop =

    let update
        (state: OfficeInterop.Model)
        (model: Model)
        (msg: OfficeInterop.Msg)
        : OfficeInterop.Model * Model * Cmd<Messages.Msg> =

        let innerUpdate (state: OfficeInterop.Model) (model: Model) (msg: OfficeInterop.Msg) =

            match msg with

            | UpdateArcFile arcFile ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.updateArcFile
                        arcFile
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd

            | AutoFitTable hidecols ->
                let p =
                    fun () -> ExcelJS.Fable.GlobalBindings.Excel.run (fun c -> ExcelHelper.formatActive c hidecols)

                let cmd = Cmd.OfPromise.attempt p () (curry GenericError Cmd.none >> DevMsg)
                state, model, cmd

            | InsertOntologyTerm ontologyAnnotation ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.fillSelectedWithOntologyAnnotation
                        (ontologyAnnotation)
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd

            | AddAnnotationBlock compositeColumn ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.addCompositeColumn
                        (compositeColumn)
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd

            | AddAnnotationBlocks compositeColumn ->
                failwith "AddAnnotationBlocks not implemented yet"
                //let cmd =
                //    Cmd.OfPromise.either
                //        OfficeInterop.Core.addAnnotationBlocks
                //        compositeColumn
                //        (curry GenericInteropLogs Cmd.none >> DevMsg)
                //        (curry GenericError Cmd.none >> DevMsg)
                state, model, Cmd.none

            //| ImportFile buildingBlockTables ->
            //    let nextCmd =
            //        Cmd.OfPromise.either
            //            OfficeInterop.Core.addAnnotationBlocksInNewSheets
            //            buildingBlockTables
            //            (curry GenericInteropLogs Cmd.none >> DevMsg)
            //            (curry GenericError Cmd.none >> DevMsg)
            //    state, model, nextCmd

            | ExportJson(arcfile, jef) ->
                let jsonExport = Json.Export.parseToJsonString (arcfile, jef)
                UpdateUtil.downloadFromString (jsonExport)
                state, model, Cmd.none
            | AddTemplates(tables, importType) ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.joinTables
                        (tables, importType.DeselectedColumns, Some importType.ImportType, importType.ImportTables)
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                let nextModel: Model = {
                    model with
                        Model.ProtocolState.TemplatesSelected = []
                }

                state, nextModel, cmd
            | JoinTable(table, options) ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.joinTable
                        (table, List.empty, options, None)
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | RemoveBuildingBlock ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.removeSelectedAnnotationBlock
                        ()
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | UpdateUnitForCells ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.convertBuildingBlock
                        ()
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | CreateAnnotationTable tryUsePrevOutput ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.createAnnotationTable
                        (false, tryUsePrevOutput)
                        (curry GenericInteropLogs Cmd.none >> DevMsg) //success
                        (curry GenericError Cmd.none >> DevMsg) //error

                state, model, cmd
            | ValidateBuildingBlock ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.validateSelectedAndNeighbouringBuildingBlocks
                        ()
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | SendErrorsToFront msgs ->
                let cmd = Cmd.ofMsg (curry GenericInteropLogs Cmd.none msgs |> DevMsg)
                state, model, cmd
            | RectifyTermColumns ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.rectifyTermColumns
                        ()
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | UpdateFillHiddenColsState newState ->
                let nextState = {
                    model.ExcelState with
                        FillHiddenColsStateStore = newState
                }

                nextState, model, Cmd.none
            | InsertFileNames fileNameList ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.insertFileNamesFromFilePicker
                        (fileNameList)
                        (curry GenericLog Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | UpdateTopLevelMetadata arcFiles ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.updateTopLevelMetadata
                        (arcFiles)
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg) //error

                state, model, cmd
            | DeleteTopLevelMetadata ->
                let cmd =
                    Cmd.OfPromise.either
                        Main.deleteTopLevelMetadata
                        ()
                        (curry GenericInteropLogs Cmd.none >> DevMsg)
                        (curry GenericError Cmd.none >> DevMsg) //error

                state, model, cmd

        try
            innerUpdate state model msg
        with e ->
            let cmd = GenericError(Cmd.none, e) |> DevMsg |> Cmd.ofMsg
            state, model, cmd
