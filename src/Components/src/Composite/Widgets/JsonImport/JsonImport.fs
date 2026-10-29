namespace Swate.Components.Composite.Widgets.JsonImport

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Browser.Types
open Swate.Components.Composite.Widgets
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Composite.Widgets.JsonImport.Types
open Swate.Components.Shared
open ARCtrl

module JsonHelper = Swate.Components.Composite.Widgets.JsonImport.Helper

module private JsonImportWidgetHelper =


    let actionClass = "swt:btn swt:btn-primary swt:join-item swt:w-48 swt:shrink-0"

    let fileInputClass =
        "swt:file-input swt:file-input-primary swt:join-item swt:w-48 swt:shrink-0"

    let defaultFormat (arcFile: ArcFiles) =
        Json.Generic.tryGetDefaultImportFormat arcFile.RelatedArcFilesDiscriminate
        |> Option.defaultValue JsonExportFormat.ARCtrl

    let selectedOrDefaultFormat (supportedFormats: JsonExportFormat list) (arcFile: ArcFiles) selectedFormat =
        if supportedFormats |> List.contains selectedFormat then
            selectedFormat
        else
            defaultFormat arcFile

open JsonImportWidgetHelper

[<Erase; Mangle(false)>]
type JsonImport =

    [<ReactComponent(true)>]
    static member JsonImport
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            ?activeTableIndex: int,
            ?pickJsonFile: unit -> JS.Promise<Result<JsonImportFile option, exn>>,
            ?onImportJson: JsonImportRequest -> JS.Promise<Result<unit, exn>>,
            ?onError: JsonImportError -> unit,
            ?onImportCompleted: unit -> unit
        ) =
        let selectedJsonFormat, setSelectedJsonFormat =
            React.useState (fun () -> defaultFormat arcFile)

        let state, setState = React.useState JsonImportWidgetState.SelectingFile
        let latestRead = React.useRef 0
        let widgetCtx = useWidgetControllerCtx ()

        let onError =
            defaultArg
                onError
                (fun (error: JsonImportError) ->
                    Browser.Dom.console.error (error.Title + ": " + error.Message + " " + defaultArg error.Details "")
                )

        let importJson =
            defaultArg
                onImportJson
                (fun request -> promise {
                    setArcFile request.NextArcFile
                    return Ok()
                })

        let onImportCompleted =
            defaultArg onImportCompleted (fun () -> widgetCtx.closeWidget WidgetType.JsonImport)

        let fileType = arcFile.RelatedArcFilesDiscriminate

        let supportedFormats =
            React.useMemo ((fun () -> Json.Generic.supportedImportFormats fileType), [| box fileType |])

        let jsonFormat = selectedOrDefaultFormat supportedFormats arcFile selectedJsonFormat

        let setJsonFormat format =
            if supportedFormats |> List.contains format then
                setSelectedJsonFormat format

        let busy =
            match state with
            | JsonImportWidgetState.SelectingFile -> false
            | _ -> true

        let startRead () =
            latestRead.current <- latestRead.current + 1
            setState JsonImportWidgetState.ReadingFile
            latestRead.current

        let isLatest (readId: int) = readId = latestRead.current

        let failRead (readId: int) (fileName: string option) (exn: exn) =
            if isLatest readId then
                setState JsonImportWidgetState.SelectingFile
                onError (JsonHelper.readError fileName exn)

        let parseFile (readId: int) (jsonFile: JsonImportFile) =
            if isLatest readId then
                match
                    Json.Import.tryParseFromJsonString (
                        jsonFile.Content,
                        Some jsonFormat,
                        Some fileType,
                        jsonFile.FileName
                    )
                with
                | Error exn ->
                    setState JsonImportWidgetState.SelectingFile
                    onError (JsonHelper.parseError jsonFile.FileName exn)
                | Ok file ->
                    setState (
                        JsonImportWidgetState.Reviewing {
                            Id = readId
                            File = file
                            FileName = jsonFile.FileName
                            Format = jsonFormat
                        }
                    )

        let pickFile () =
            match pickJsonFile with
            | None -> ()
            | Some pickJsonFile ->
                let readId = startRead ()

                pickJsonFile ()
                |> Promise.catch (fun exn -> Error exn)
                |> Promise.map (fun result ->
                    match result with
                    | Ok(Some jsonFile) -> parseFile readId jsonFile
                    | Ok None ->
                        if isLatest readId then
                            setState JsonImportWidgetState.SelectingFile
                    | Error exn -> failRead readId None exn
                )
                |> Promise.start

        let readFile (file: File) =
            let readId = startRead ()
            let reader = Browser.Dom.FileReader.Create()

            reader.onload <-
                fun evt ->
                    let content: string = evt.target?result

                    parseFile readId {
                        FileName = Some file.name
                        Content = content
                    }

            reader.onerror <-
                fun _ -> failRead readId (Some file.name) (exn $"The file '{file.name}' could not be read.")

            try
                reader.readAsText file
            with exn ->
                failRead readId (Some file.name) exn

        let reading =
            match state with
            | JsonImportWidgetState.ReadingFile -> true
            | _ -> false

        let controlsDisabled = busy || supportedFormats.IsEmpty

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:w-fit swt:max-w-full"
            prop.children [
                Html.div [
                    prop.className "swt:join swt:w-fit swt:max-w-full"
                    prop.children [
                        JsonFormatSelect.JsonFormatSelect(
                            supportedFormats,
                            jsonFormat,
                            setJsonFormat,
                            disabled = busy,
                            testId = "json-import-format-select"
                        )

                        match pickJsonFile with
                        | Some _ ->
                            Html.button [
                                prop.testId "json-import-picker-button"
                                prop.type'.button
                                prop.className actionClass
                                prop.disabled controlsDisabled
                                prop.onClick (fun _ ->
                                    if not busy then
                                        pickFile ()
                                )
                                prop.children [
                                    if reading then
                                        Html.span [
                                            prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                                        ]
                                    Html.span [
                                        prop.text (if reading then "Reading file..." else "Choose JSON file")
                                    ]
                                ]
                            ]
                        | None ->
                            Html.input [
                                prop.testId "json-import-file-input"
                                prop.type'.file
                                prop.accept ".json,application/json"
                                prop.className fileInputClass
                                prop.disabled controlsDisabled
                                prop.onChange (fun (file: File) -> readFile file)
                                prop.onClick (fun e -> e.target?value <- null)
                            ]
                    ]
                ]
                Html.div [
                    prop.testId "json-import-format-description"
                    prop.className "swt:text-xs swt:opacity-70"
                    prop.text (JsonHelper.formatDescription jsonFormat)
                ]
                if reading && pickJsonFile.IsNone then
                    Html.div [
                        prop.className "swt:flex swt:items-center swt:gap-2 swt:text-xs"
                        prop.children [
                            Html.span [
                                prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                            ]
                            Html.span [ prop.text "Reading file..." ]
                        ]
                    ]
                match state with
                | JsonImportWidgetState.Reviewing parsed ->
                    React.KeyedFragment(
                        string parsed.Id,
                        [
                            JsonImportModal.JsonImportModal(
                                parsed,
                                arcFile,
                                activeTableIndex,
                                importJson,
                                onBack = (fun () -> setState JsonImportWidgetState.SelectingFile),
                                onCancel = (fun () -> setState JsonImportWidgetState.SelectingFile),
                                onDone =
                                    fun () ->
                                        setState JsonImportWidgetState.SelectingFile
                                        onImportCompleted ()
                            )
                        ]
                    )
                | _ -> Html.none
            ]
        ]
