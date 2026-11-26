namespace Arcitisa.Components.Composite.Widgets.JsonExport

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Composite.Widgets
open Arcitisa.Components.Shared
open ARCtrl


module private JsonExportHelper =

    let rootClass = "swt:join swt:w-fit swt:max-w-full"

    let actionClass = "swt:btn swt:btn-primary swt:join-item swt:w-32 swt:shrink-0"

    let defaultFormat (arcFile: ArcFiles) =
        Json.Generic.tryGetDefaultExportFormat arcFile.RelatedArcFilesDiscriminate
        |> Option.defaultValue JsonExportFormat.ARCtrl

    let selectedOrDefaultFormat (supportedFormats: JsonExportFormat list) (arcFile: ArcFiles) selectedFormat =
        if supportedFormats |> List.contains selectedFormat then
            selectedFormat
        else
            defaultFormat arcFile

    let downloadJson (arcfile: ArcFiles, jef: JsonExportFormat) =
        let jsonExport = Json.Export.parseToJsonString (arcfile, jef)
        Arcitisa.Components.Util.Download.downloadFromString (jsonExport)

    let defaultExportJson (arcfile: ArcFiles, jef: JsonExportFormat) = promise {
        try
            downloadJson (arcfile, jef)
            return Ok()
        with exn ->
            return Error exn
    }

open JsonExportHelper

[<Erase; Mangle(false)>]
type JsonExport =

    [<ReactComponent(true)>]
    static member JsonExport
        (
            arcFile: ArcFiles,
            ?onExportJson: ArcFiles * JsonExportFormat -> JS.Promise<Result<unit, exn>>,
            ?onError: exn -> unit
        ) =
        let selectedExportFormat, setSelectedExportFormat =
            React.useState (fun () -> defaultFormat arcFile)

        let isExporting, setIsExporting = React.useState false

        let onError = defaultArg onError (fun exn -> Browser.Dom.console.error exn)

        let exportJson =
            defaultArg onExportJson (fun (arcFile, jsonFormat) -> defaultExportJson (arcFile, jsonFormat))

        let supportedFormats =
            React.useMemo (
                (fun () -> Json.Generic.supportedExportFormats arcFile.RelatedArcFilesDiscriminate),
                [| box arcFile.RelatedArcFilesDiscriminate |]
            )

        let exportFormat =
            selectedOrDefaultFormat supportedFormats arcFile selectedExportFormat

        let setExportFormat format =
            if supportedFormats |> List.contains format then
                setSelectedExportFormat format

        Html.div [
            prop.className rootClass
            prop.children [
                JsonFormatSelect.JsonFormatSelect(
                    supportedFormats,
                    exportFormat,
                    setExportFormat,
                    disabled = isExporting
                )
                Html.button [
                    prop.className actionClass
                    prop.disabled (isExporting || supportedFormats.IsEmpty)
                    prop.text (if isExporting then "Exporting..." else "Download")
                    prop.onClick (fun _ ->
                        if not isExporting then
                            setIsExporting true

                            exportJson (arcFile, exportFormat)
                            |> Promise.catch (fun exn -> Error exn)
                            |> Promise.map (fun result ->
                                setIsExporting false

                                match result with
                                | Ok() -> ()
                                | Error exn -> onError exn
                            )
                            |> Promise.start
                    )
                ]
            ]
        ]
