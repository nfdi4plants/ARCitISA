namespace Swate.Components.Composite.Widgets.JsonImport

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Browser.Types
open Swate.Components.Composite.Widgets
open Swate.Components.Composite.Widgets.JsonImport.Types
open Swate.Components.Shared
open ARCtrl

module private JsonImportWidgetHelper =

    let rootClass = "swt:join swt:w-fit swt:max-w-full"

    let actionClass = "swt:btn swt:btn-primary swt:join-item swt:w-32 swt:shrink-0"

    let fileInputClass =
        "swt:file-input swt:file-input-primary swt:join-item swt:w-32 swt:shrink-0"

    let defaultFormat (arcFile: ArcFiles) =
        Json.Generic.tryGetDefaultImportFormat arcFile.RelatedArcFilesDiscriminate
        |> Option.defaultValue JsonExportFormat.ARCtrl

    let selectedOrDefaultFormat (supportedFormats: JsonExportFormat list) (arcFile: ArcFiles) selectedFormat =
        if supportedFormats |> List.contains selectedFormat then
            selectedFormat
        else
            defaultFormat arcFile

    let defaultImportJson (arcFile: ArcFiles) (setArcFile: ArcFiles -> unit) (request: JsonImportRequest) = promise {
        match Json.Import.applyToCurrentArcFile (arcFile, request.ImportedFile) with
        | Ok nextArcFile ->
            setArcFile nextArcFile
            return Ok()
        | Error exn -> return Error exn
    }

open JsonImportWidgetHelper

[<Erase; Mangle(false)>]
type JsonImport =

    [<ReactComponent(true)>]
    static member JsonImport
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            ?pickJsonFile: unit -> JS.Promise<Result<JsonImportFile option, exn>>,
            ?onImportJson: JsonImportRequest -> JS.Promise<Result<unit, exn>>,
            ?onError: exn -> unit
        ) =
        let selectedJsonFormat, setSelectedJsonFormat =
            React.useState (fun () -> defaultFormat arcFile)

        let loading, setLoading = React.useState false

        let onError = defaultArg onError (fun exn -> Browser.Dom.console.error exn)

        let importJson =
            defaultArg onImportJson (fun request -> defaultImportJson arcFile setArcFile request)

        let fileType = arcFile.RelatedArcFilesDiscriminate

        let supportedFormats =
            React.useMemo ((fun () -> Json.Generic.supportedImportFormats fileType), [| box fileType |])

        let jsonFormat = selectedOrDefaultFormat supportedFormats arcFile selectedJsonFormat

        let setJsonFormat format =
            if supportedFormats |> List.contains format then
                setSelectedJsonFormat format

        let importFile (jsonFile: JsonImportFile) =
            setLoading true

            promise {
                match
                    Json.Import.tryParseFromJsonString (
                        jsonFile.Content,
                        Some jsonFormat,
                        Some fileType,
                        jsonFile.FileName
                    )
                with
                | Error exn -> return Error exn
                | Ok importedFile ->
                    return!
                        importJson {
                            ImportedFile = importedFile
                            SourceFileName = jsonFile.FileName
                            JsonFormat = jsonFormat
                        }
            }
            |> Promise.catch (fun exn -> Error exn)
            |> Promise.map (fun result ->
                setLoading false

                match result with
                | Ok() -> ()
                | Error exn -> onError exn
            )
            |> Promise.start

        let pickAndImportFile () =
            match pickJsonFile with
            | None -> ()
            | Some pickJsonFile ->
                setLoading true

                pickJsonFile ()
                |> Promise.catch (fun exn -> Error exn)
                |> Promise.map (fun result ->
                    match result with
                    | Ok(Some jsonFile) -> importFile jsonFile
                    | Ok None -> setLoading false
                    | Error exn ->
                        setLoading false
                        onError exn
                )
                |> Promise.start

        Html.div [
            prop.className rootClass
            prop.children [
                JsonFormatSelect.JsonFormatSelect(
                    supportedFormats,
                    jsonFormat,
                    setJsonFormat,
                    disabled = loading,
                    testId = "json-import-format-select"
                )

                match pickJsonFile with
                | Some _ ->
                    Html.button [
                        prop.testId "json-import-picker-button"
                        prop.type'.button
                        prop.className actionClass
                        prop.disabled (loading || supportedFormats.IsEmpty)
                        prop.onClick (fun _ ->
                            if not loading then
                                pickAndImportFile ()
                        )
                        prop.text (if loading then "Importing..." else "Import")
                    ]
                | None ->
                    Html.input [
                        prop.testId "json-import-file-input"
                        prop.type'.file
                        prop.accept ".json,application/json"
                        prop.className fileInputClass
                        prop.disabled (loading || supportedFormats.IsEmpty)
                        prop.onChange (fun (file: File) ->
                            let reader = Browser.Dom.FileReader.Create()

                            reader.onload <-
                                fun evt ->
                                    let content: string = evt.target?result

                                    importFile {
                                        FileName = Some file.name
                                        Content = content
                                    }

                            reader.onerror <-
                                fun _ ->
                                    onError (exn $"Could not read JSON file '{file.name}'.")
                                    setLoading false

                            reader.readAsText (file)
                            ()
                        )
                        prop.onClick (fun e -> e.target?value <- null)
                    ]
            ]
        ]
