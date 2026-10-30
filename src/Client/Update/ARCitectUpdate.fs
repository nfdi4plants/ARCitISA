namespace Update

open Elmish

open Messages
open Swate.Components.Shared
open Model
open Swate.Components

module ARCitect =

    open ARCtrl
    open ARCtrl.Json

    let api = Model.ARCitect.api

    let update
        (state: ARCitect.Model)
        (model: Model.Model)
        (msg: ARCitect.Msg)
        : ARCitect.Model * Model * Cmd<Messages.Msg> =
        match msg with
        | ARCitect.Init msg ->
            match msg with
            | Start() ->
                console.log "[ARCitISA] ARCitect.Init.Start"

                let cmd =
                    Cmd.OfPromise.either
                        api.Init
                        ()
                        (Finished >> ARCitect.Init >> ARCitectMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | ApiCall.Finished(Some(arcFile, json, dataMapParent)) ->

                let resolvedArcFile = // if the fable transpiled string from ARCitect.Interop.InteropTypes.ARCFile does not exactly match any available here it will somehow fallback to "ARCitect.Interop.InteropTypes.ARCFile.Assay"
                    match arcFile with
                    | ArcFilesDiscriminate.Assay ->
                        let assay = ARCtrl.JsonCompatibility.fromAssayJsonString json
                        ArcFiles.Assay assay
                    | ArcFilesDiscriminate.Study ->
                        let study = ARCtrl.JsonCompatibility.fromStudyJsonString json
                        ArcFiles.Study(study, [])
                    | ArcFilesDiscriminate.Investigation ->
                        let inv = ARCtrl.JsonCompatibility.fromInvestigationJsonString json
                        ArcFiles.Investigation inv
                    | ArcFilesDiscriminate.Run ->
                        let run = ARCtrl.JsonCompatibility.fromRunJsonString json
                        ArcFiles.Run run
                    | ArcFilesDiscriminate.Workflow ->
                        let workflow = ARCtrl.JsonCompatibility.fromWorkflowJsonString json
                        ArcFiles.Workflow workflow
                    | ArcFilesDiscriminate.Template ->
                        let template = Template.fromJsonString json
                        ArcFiles.Template template
                    | ArcFilesDiscriminate.DataMap ->
                        if dataMapParent.IsNone then
                            failwith "No parent for datamap is available!"

                        let dataMap = Datamap.fromJsonString json

                        ArcFiles.DataMap(dataMapParent, dataMap)

                let cmd = Spreadsheet.InitFromArcFile resolvedArcFile |> SpreadsheetMsg |> Cmd.ofMsg
                state, model, cmd

            | ApiCall.Finished(None) -> state, model, Cmd.none

        | ARCitect.Save arcFile ->
            let arcFileEnum, json, datamapParent =
                match arcFile with
                | ArcFiles.Assay assay -> ArcFilesDiscriminate.Assay, ArcAssay.toJsonString 0 assay, None
                | ArcFiles.Study(study, _) -> ArcFilesDiscriminate.Study, ArcStudy.toJsonString 0 study, None
                | ArcFiles.Investigation inv ->
                    ArcFilesDiscriminate.Investigation, ArcInvestigation.toJsonString 0 inv, None
                | ArcFiles.Run run -> ArcFilesDiscriminate.Run, ArcRun.toJsonString 0 run, None
                | ArcFiles.Workflow workflow -> ArcFilesDiscriminate.Workflow, ArcWorkflow.toJsonString 0 workflow, None
                | ArcFiles.Template template -> ArcFilesDiscriminate.Template, Template.toJsonString 0 template, None
                | ArcFiles.DataMap(datamapParent, datamap) ->
                    ArcFilesDiscriminate.DataMap, Datamap.toJsonString 0 datamap, datamapParent

            let cmd =
                Cmd.OfPromise.attempt
                    api.Save
                    (arcFileEnum, json, datamapParent)
                    (curry GenericError Cmd.none >> DevMsg)

            state, model, cmd

        | ARCitect.RequestPaths msg ->
            match msg with
            | Start pojo ->
                let cmd =
                    Cmd.OfPromise.either
                        api.RequestPaths
                        pojo
                        (Finished >> ARCitect.RequestPaths >> ARCitectMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | ApiCall.Finished(wasSuccessful: bool) ->
                let cmd =
                    if wasSuccessful then
                        Cmd.none
                    else
                        GenericError(Cmd.none, exn ("RequestPaths failed")) |> DevMsg |> Cmd.ofMsg

                state, model, cmd

        | ARCitect.ResponsePaths paths ->
            let paths = Array.indexed paths |> List.ofArray

            state,
            {
                model with
                    FilePickerState.FileNames = paths
            },
            Cmd.none

        | ARCitect.RequestFile msg ->
            match msg with
            | Start() ->
                let cmd =
                    Cmd.OfPromise.either
                        api.RequestFile
                        ()
                        (Finished >> ARCitect.RequestFile >> ARCitectMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                let nextModel = {
                    model with
                        DataAnnotatorModel.Loading = true
                        DataAnnotatorModel.DataFile = None
                        DataAnnotatorModel.ParsedFile = None
                }

                state, nextModel, cmd
            | ApiCall.Finished wasSuccessful ->
                let nextModel = {
                    model with
                        DataAnnotatorModel.Loading = false
                }

                let cmd =
                    if wasSuccessful then
                        Cmd.none
                    else
                        GenericError(Cmd.none, exn ("RequestFile failed")) |> DevMsg |> Cmd.ofMsg

                state, nextModel, cmd

        | ARCitect.ResponseFile file ->
            let dataFile =
                DataAnnotator.DataFile.create (file.name, file.mimetype, file.content, file.size)

            let msg = dataFile |> Some |> DataAnnotator.UpdateDataFile |> DataAnnotatorMsg

            state,
            {
                model with
                    DataAnnotatorModel.Loading = true
            },
            Cmd.ofMsg msg

        | ARCitect.RequestPersons msg ->
            match msg with
            | Start() ->
                let cmd =
                    Cmd.OfPromise.either
                        api.RequestPersons
                        ()
                        (Finished >> ARCitect.RequestPersons >> ARCitectMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                state, model, cmd
            | ApiCall.Finished persons ->
                let personsResolved =
                    persons |> Array.map (fun personJson -> Person.fromJsonString personJson)

                { state with Persons = personsResolved }, model, Cmd.none
