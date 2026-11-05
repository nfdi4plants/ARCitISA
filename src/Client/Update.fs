[<AutoOpen>]
module Update.Update

open Elmish

open Swate.Components.Shared
open Routing
open Messages
open Model
open Swate.Components

open LocalStorage.AutosaveConfig

let urlUpdate (route: Route option) (model: Model) : Model * Cmd<Messages.Msg> =
    let cmd (host: Swatehost) =
        SpreadsheetInterface.Initialize host |> InterfaceMsg |> Cmd.ofMsg

    let host =
        match route with
        | Some(Routing.Route.Home queryIntegerOption) -> Swatehost.ofQueryParam queryIntegerOption
        | None -> Swatehost.Browser

    {
        model with
            Model.PersistentStorageState.Host = Some host
    },
    cmd host

module AdvancedSearch =

    let update (msg: AdvancedSearch.Msg) (model: Model) : Model * Cmd<Messages.Msg> =
        match msg with
        | AdvancedSearch.GetSearchResults content ->
            let cmd =
                Cmd.OfAsync.either
                    Api.api.getTermsForAdvancedSearch
                    content.config
                    (fun terms -> Run(fun _ -> content.responseSetter terms))
                    (curry GenericError Cmd.none >> DevMsg)

            model, cmd

module PageState =

    let update (msg: PageState.Msg) (model: Model.Model) : Model * Cmd<Messages.Msg> =
        match msg with
        | PageState.UpdateShowSidebar show ->
            {
                model with
                    Model.Model.PageState.ShowSideBar = show
            },
            Cmd.none
        | PageState.UpdateMainPage mainPage ->
            {
                model with
                    Model.Model.PageState.MainPage = mainPage
            },
            Cmd.none
        | PageState.UpdateSidebarPage sidebarPage ->
            {
                model with
                    Model.Model.PageState.SidebarPage = sidebarPage
            },
            Cmd.none

module Dev =

    let update (devMsg: DevMsg) (currentState: DevState) : DevState * Cmd<Messages.Msg> =
        match devMsg with
        | GenericLog(nextCmd, (level, logText)) ->
            let nextState = {
                currentState with
                    Log = (LogItem.ofStringNow level logText) :: currentState.Log
            }

            nextState, nextCmd

        | GenericInteropLogs(nextCmd, logs) ->
            let parsedLogs = logs |> List.map LogItem.ofInteropLogginMsg

            let parsedDisplayLogs =
                parsedLogs
                |> List.filter (fun x ->
                    match x with
                    | Error _
                    | Warning _ -> true
                    | _ -> false
                )

            let nextState = {
                currentState with
                    Log = parsedLogs @ currentState.Log
                    DisplayLogList = parsedDisplayLogs @ currentState.DisplayLogList
            }

            // let batch =
            //     Cmd.batch [
            //         // if List.isEmpty parsedDisplayLogs |> not then
            //         //     Model.ModalState.ExcelModals.InteropLogging
            //         //     |> Model.ModalState.ModalTypes.ExcelModal
            //         //     |> Some
            //         //     |> Messages.UpdateModal
            //         //     |> Cmd.ofMsg
            //         nextCmd
            //     ]

            nextState, nextCmd

        | GenericError(nextCmd, e) ->
            let item = LogItem.Error(System.DateTime.Now, e.GetPropagatedError())

            let nextState = {
                currentState with
                    Log = item :: currentState.Log
                    DisplayLogList = item :: currentState.DisplayLogList
            }

            // let errorMsg =
            //     Model.ModalState.GeneralModals.Error e
            //     |> Model.ModalState.ModalTypes.GeneralModal
            //     |> Some
            //     |> Messages.UpdateModal

            // let batch =
            //     nextCmd
            //     |> Cmd.map (fun cmd ->

            //         match cmd with
            //         | Messages.UpdateModel _ -> errorMsg
            //         | _ -> Msg.Batch [ errorMsg; cmd ]
            //     )

            nextState, nextCmd

        | UpdateDisplayLogList newList ->
            let nextState = {
                currentState with
                    DisplayLogList = newList
            }

            nextState, Cmd.none

        | LogTableMetadata ->
            let cmd =
                Cmd.OfPromise.either
                    OfficeInterop.Core.Main.getTableMetaData
                    ()
                    (curry GenericLog Cmd.none >> DevMsg)
                    (curry GenericError Cmd.none >> DevMsg)

            currentState, cmd

let handlePersistenStorageMsg
    (persistentStorageMsg: PersistentStorage.Msg)
    (currentState: PersistentStorageState)
    : PersistentStorageState * Cmd<Msg> =
    match persistentStorageMsg with
    | PersistentStorage.UpdateAppVersion appVersion ->
        let nextState = {
            currentState with
                AppVersion = appVersion
        }

        nextState, Cmd.none
    | PersistentStorage.UpdateAutosave autosave ->
        let nextState = {
            currentState with
                Autosave = autosave
        }

        setAutosaveConfiguration (nextState.Autosave)

        if not nextState.Autosave then
            LocalHistory.Model.ResetHistoryWebStorage()

        nextState, Cmd.none

module DataAnnotator =
    let update (msg: DataAnnotator.Msg) (state: DataAnnotator.Model) (model: Model.Model) =
        match msg with
        | DataAnnotator.UpdateDataFile dataFile ->
            let parsedFile =
                dataFile
                |> Option.map (fun file ->
                    let s = file.ExpectedSeparator
                    DataAnnotator.ParsedDataFile.fromFileBySeparator s file
                )

            let nextState: DataAnnotator.Model = {
                state with
                    DataFile = dataFile
                    ParsedFile = parsedFile
                    Loading = false
            }

            nextState, model, Cmd.none
        | DataAnnotator.ToggleHeader ->
            let nextState = {
                state with
                    ParsedFile = state.ParsedFile |> Option.map (fun file -> file.ToggleHeader())
            }

            nextState, model, Cmd.none
        | DataAnnotator.UpdateSeperator newSep ->
            let parsedFile =
                state.DataFile
                |> Option.map (fun file -> DataAnnotator.ParsedDataFile.fromFileBySeparator newSep file)

            let nextState = { state with ParsedFile = parsedFile }
            nextState, model, Cmd.none

module History =

    open LocalHistory
    open Spreadsheet
    open ARCtrl
    open ARCtrl.Json

    /// <summary>
    /// This function will store the information correctly.
    /// Can return save information to local storage (persistent between browser sessions) and session storage.
    /// It works based of exlusion. As it specifies certain messages not triggering history update.
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="state"></param>
    /// <param name="model"></param>
    /// <param name="cmd"></param>
    let updateHistoryStorageMsg
        (msg: Spreadsheet.Msg)
        (spreadsheetState: Spreadsheet.Model)
        (historyState: LocalHistory.Model)
        (isAutosave: bool)
        (host: Swatehost option)
        =

        let snapshotJsonString = if isAutosave then spreadsheetState.ToJsonString() else ""

        if isAutosave then
            //This will cache the most up to date table state to local storage.
            //This is used as a simple autosave feature.
            Spreadsheet.Model.SaveToLocalStorage(snapshotJsonString)

        //This matchcase handles undo / redo functionality
        match msg with
        | UpdateActiveView _
        | Reset
        | ExportJson _
        | ExportXlsx _
        | ExportXlsxDownload _
        | ImportXlsx _
        | ImportJsonRaw _
        | InitFromArcFile _ -> historyState
        | _ ->
            let props = LocalHistory.Model.UpdateBy(historyState)
            let nextState = props.nextState
            let key = props.newKey
            let toRemoveList = props.toRemoveList

            if isAutosave then

                LocalHistory.Model.SaveSessionSnapshotIndexedDB(key, snapshotJsonString, nextState, toRemoveList)
                |> Promise.catch (fun e -> console.error ("Error saving session snapshot to IndexedDB: " + e.Message))
                |> Promise.start

            if host = Some Swatehost.ARCitect then
                match spreadsheetState.ArcFile with // model is not yet updated at this position.
                | Some(ArcFiles.Assay assay) ->
                    ARCitect.api.Save(ArcFilesDiscriminate.Assay, ArcAssay.toJsonString 0 assay, None)
                    |> Promise.start
                | Some(ArcFiles.Study(study, _)) ->
                    ARCitect.api.Save(ArcFilesDiscriminate.Study, ArcStudy.toJsonString 0 study, None)
                    |> Promise.start
                | Some(ArcFiles.Investigation inv) ->
                    ARCitect.api.Save(ArcFilesDiscriminate.Investigation, ArcInvestigation.toJsonString 0 inv, None)
                    |> Promise.start
                | Some(ArcFiles.Run run) ->
                    ARCitect.api.Save(ArcFilesDiscriminate.Run, ArcRun.toJsonString 0 run, None)
                    |> Promise.start
                | Some(ArcFiles.Workflow workflow) ->
                    ARCitect.api.Save(ArcFilesDiscriminate.Workflow, ArcWorkflow.toJsonString 0 workflow, None)
                    |> Promise.start
                | Some(ArcFiles.Template template) ->
                    ARCitect.api.Save(ArcFilesDiscriminate.Template, Template.toJsonString 0 template, None)
                    |> Promise.start
                | Some(ArcFiles.DataMap(parent, datamap)) ->
                    if parent.IsSome then
                        ARCitect.api.Save(ArcFilesDiscriminate.DataMap, Datamap.toJsonString 0 datamap, parent)
                        |> Promise.start
                    else
                        failwith "No datamap parent is available"
                | _ -> ()

            nextState

    let update (msg: History.Msg) (model: Model.Model) : Model.Model * Cmd<Messages.Msg> =
        match msg with
        | History.Update(newHistory) -> { model with History = newHistory }, Cmd.none
        | History.UpdateHistoryPosition newPosition ->
            match newPosition with
            | _ when model.History.NextPositionIsValid(newPosition) |> not -> model, Cmd.none
            | _ ->
                let nextModel = {
                    model with
                        History.HistoryCurrentPosition = newPosition
                }

                let cmd =
                    Cmd.OfPromise.either
                        LocalHistory.Model.fromIndexedDBByKeyPosition
                        (newPosition)
                        (History.UpdateHistoryPositionResponse >> Msg.HistoryMsg)
                        (curry GenericError Cmd.none >> DevMsg)

                nextModel, cmd
        | History.UpdateHistoryPositionResponse spreadsheetModel ->
            let nextModel = {
                model with
                    SpreadsheetModel = spreadsheetModel
            }

            nextModel, Cmd.none



let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let innerUpdate (msg: Msg) (currentModel: Model) =
        match msg with
        | DoNothing -> currentModel, Cmd.none
        | Run callback ->
            callback ()
            model, Cmd.none
        // | UpdateModal modal ->
        //     {
        //         model with
        //             Model.ModalState.ActiveModal = modal
        //     },
        //     Cmd.none
        | TestMyAPI ->
            let cmd =
                Cmd.OfAsync.either Api.testAPIv1.test () (curry GenericLog Cmd.none) (curry GenericError Cmd.none)

            currentModel, Cmd.map DevMsg cmd
        | TestMyPostAPI ->
            let cmd =
                Cmd.OfAsync.either
                    Api.testAPIv1.postTest
                    ("instrument Mod")
                    (curry GenericLog Cmd.none)
                    (curry GenericError Cmd.none)

            currentModel, Cmd.map DevMsg cmd
        | Batch msgSeq ->
            let cmd = Cmd.batch [ yield! msgSeq |> Seq.map Cmd.ofMsg ]
            currentModel, cmd

        | OfficeInteropMsg excelMsg ->
            let nextState, nextModel0, nextCmd =
                Update.OfficeInterop.update model.ExcelState currentModel excelMsg

            let nextModel = {
                nextModel0 with
                    ExcelState = nextState
            }

            nextModel, nextCmd

        | SpreadsheetMsg msg ->
            let nextState, nextModel, nextCmd =
                Update.Spreadsheet.update currentModel.SpreadsheetModel currentModel msg

            let nextHistory =
                History.updateHistoryStorageMsg
                    msg
                    nextState
                    nextModel.History
                    nextModel.PersistentStorageState.Autosave
                    nextModel.PersistentStorageState.Host

            let nextModel' = {
                nextModel with
                    SpreadsheetModel = nextState
                    History = nextHistory
            }

            nextModel', nextCmd

        | InterfaceMsg msg -> Update.Interface.update currentModel msg

        | TermSearchMsg termSearchMsg ->
            let nextTermSearchState, nextCmd =
                currentModel.TermSearchState |> TermSearch.update termSearchMsg

            let nextModel = {
                currentModel with
                    TermSearchState = nextTermSearchState
            }

            nextModel, nextCmd
        | AdvancedSearchMsg msg ->
            let nextModel, cmd = AdvancedSearch.update msg model

            nextModel, cmd

        | DevMsg msg ->
            let nextDevState, nextCmd = currentModel.DevState |> Dev.update msg

            let nextModel = {
                currentModel with
                    DevState = nextDevState
            }

            nextModel, nextCmd

        | PersistentStorageMsg persistentStorageMsg ->
            let nextPersistentStorageState, nextCmd =
                currentModel.PersistentStorageState
                |> handlePersistenStorageMsg persistentStorageMsg

            let nextModel = {
                currentModel with
                    PersistentStorageState = nextPersistentStorageState
            }

            nextModel, nextCmd

        | FilePickerMsg filePickerMsg ->
            let nextFilePickerState, nextCmd =
                Pages.FilePicker.update filePickerMsg currentModel.FilePickerState model

            let nextModel = {
                currentModel with
                    FilePickerState = nextFilePickerState
            }

            nextModel, nextCmd

        | BuildingBlockMsg addBuildingBlockMsg ->
            let nextAddBuildingBlockState, nextCmd =
                currentModel.AddBuildingBlockState
                |> BuildingBlock.Core.update addBuildingBlockMsg

            let nextModel = {
                currentModel with
                    AddBuildingBlockState = nextAddBuildingBlockState
            }

            nextModel, nextCmd

        | ProtocolMsg fileUploadJsonMsg ->
            let nextState, model, nextCmd =
                ProtocolState.update fileUploadJsonMsg currentModel.ProtocolState model

            let nextModel = { model with ProtocolState = nextState }
            nextModel, nextCmd

        //| CytoscapeMsg msg ->
        //    let nextState, nextModel0, nextCmd =
        //        Cytoscape.Update.update msg currentModel.CytoscapeModel currentModel
        //    let nextModel =
        //        {nextModel0 with
        //            CytoscapeModel = nextState}
        //    nextModel, nextCmd

        | DataAnnotatorMsg msg ->
            let nextState, nextModel0, nextCmd =
                DataAnnotator.update msg currentModel.DataAnnotatorModel currentModel

            let nextModel = {
                nextModel0 with
                    DataAnnotatorModel = nextState
            }

            nextModel, nextCmd

        | PageStateMsg msg ->
            let nextModel, nextCmd = PageState.update msg currentModel
            nextModel, nextCmd

        | HistoryMsg msg ->
            let nextModel, nextCmd = History.update msg currentModel
            nextModel, nextCmd

        | ARCitectMsg msg ->
            let nextState, nextModel0, nextCmd =
                ARCitect.update model.ARCitectState currentModel msg

            let nextModel = {
                nextModel0 with
                    ARCitectState = nextState
            }

            nextModel, nextCmd

    /// This function is used to determine which msg should be logged to activity log.
    /// The function is exception based, so msg which should not be logged needs to be added here.
    let matchMsgToLog (msg: Msg) =
        match msg with
        | DevMsg _ -> false
        | _ -> true

    let logg (msg: Msg) (model: Model) : Model =
        if matchMsgToLog msg then
            let l = 62
            let txt = $"{msg.ToString()}"
            let txt = if txt.Length > l then txt.Substring(0, l) + ".." else txt

            let nextState = {
                model.DevState with
                    Log = (LogItem.ofStringNow "Info" txt) :: model.DevState.Log
            }

            let nextModel = { model with DevState = nextState }
            nextModel
        else
            model

    logg msg model |> innerUpdate msg
