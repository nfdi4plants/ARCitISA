module JsonExporter.Core

open System
open Fable.Core.JsInterop
open Elmish

open Arcitisa.Components.Shared

open Model

open Messages

open Browser.Dom

open Feliz

open ExcelJS.Fable
open GlobalBindings

open ARCtrl
open ARCtrl.Spreadsheet

let download (filename, text) =
    let element = document.createElement ("a")
    element.setAttribute ("href", "data:text/plain;charset=utf-8," + Fable.Core.JS.encodeURIComponent (text))
    element.setAttribute ("download", filename)

    element?style?display <- "None"
    let _ = document.body.appendChild (element)

    element.click ()

    document.body.removeChild (element) |> ignore
    ()

type private JsonExportState = {
    ExportFormat: JsonExportFormat
} with

    static member init() = {
        ExportFormat = JsonExportFormat.ROCrate
    }

type FileExporter =

    static member private FileFormat(efm: JsonExportFormat, state: JsonExportState, setState) =
        Html.option [ prop.text (efm.AsStringRdbl) ]

    [<ReactComponent>]
    static member JsonExport(model: Model, dispatch) =
        let state, setState = React.useState JsonExportState.init

        let keys = Spreadsheet.IO.Json.readFromJsonMap |> Map.keys |> Seq.toList

        Html.div [
            prop.className "swt:join"
            prop.children [
                Html.select [
                    prop.className "swt:select swt:join-item swt:min-w-fit"
                    prop.onChange (fun (e: Browser.Types.Event) ->
                        let jef: JsonExportFormat = JsonExportFormat.fromString (e.target?value)
                        { state with ExportFormat = jef } |> setState
                    )
                    prop.defaultValue (string state.ExportFormat)
                    prop.children [
                        match model.SpreadsheetModel.ArcFile with
                        | Some arcfile ->
                            for af, jf in keys do
                                if af = arcfile.RelatedArcFilesDiscriminate then
                                    FileExporter.FileFormat(jf, state, setState)
                        | None ->
                            FileExporter.FileFormat(JsonExportFormat.ROCrate, state, setState)
                            FileExporter.FileFormat(JsonExportFormat.ISA, state, setState)
                            FileExporter.FileFormat(JsonExportFormat.ARCtrl, state, setState)
                            FileExporter.FileFormat(JsonExportFormat.ARCtrlCompressed, state, setState)
                    ]
                ]
                Html.button [
                    prop.className "swt:btn swt:btn-primary swt:grow swt:join-item"
                    prop.text "Download"
                    prop.onClick (fun _ ->
                        let host = model.PersistentStorageState.Host

                        match host with
                        | Some Arcitisahost.Excel ->
                            promise {
                                let! result = OfficeInterop.Core.Main.tryParseToArcFile ()

                                match result with
                                | Result.Ok arcFile ->
                                    SpreadsheetInterface.ExportJson(arcFile, state.ExportFormat)
                                    |> InterfaceMsg
                                    |> dispatch
                                | Result.Error msgs ->
                                    OfficeInterop.SendErrorsToFront msgs |> OfficeInteropMsg |> dispatch
                            }
                            |> ignore
                        | Some Arcitisahost.Browser
                        | Some Arcitisahost.ARCitect ->
                            if model.SpreadsheetModel.ArcFile.IsSome then
                                SpreadsheetInterface.ExportJson(
                                    model.SpreadsheetModel.ArcFile.Value,
                                    state.ExportFormat
                                )
                                |> InterfaceMsg
                                |> dispatch
                        | _ -> failwith "not implemented"
                    )
                ]
            ]
        ]

    static member Main(model: Model, dispatch: Messages.Msg -> unit) =
        SidebarComponents.SidebarLayout.Container [
            SidebarComponents.SidebarLayout.Header "File Export"

            SidebarComponents.SidebarLayout.Description(
                Html.div [
                    Html.p "Export ARCitISA annotation tables to official JSON."
                    Html.ul [
                        Html.li [
                            Html.b "ARCtrl"
                            Html.text ": A simple ARCtrl specific format."
                        ]
                        Html.li [
                            Html.b "ARCtrl Compressed"
                            Html.text ": A compressed ARCtrl specific format."
                        ]
                        Html.li [
                            Html.b "ISA"
                            Html.text ": ISA-JSON format ("
                            Html.a [
                                prop.target.blank
                                prop.href "https://isa-specs.readthedocs.io/en/latest/isajson.html#"
                                prop.text "ISA-JSON"
                            ]
                            Html.text ")."
                        ]
                        Html.li [
                            Html.b "RO-Crate Metadata"
                            Html.text ": RO-Crate format ("
                            Html.a [
                                prop.target.blank
                                prop.href "https://www.researchobject.org/ro-crate/"
                                prop.text "RO-Crate"
                            ]
                            Html.text ", "
                            Html.a [
                                prop.target.blank
                                prop.href
                                    "https://github.com/nfdi4plants/isa-ro-crate-profile/blob/main/profile/isa_ro_crate.md"
                                prop.text "ISA-Profile"
                            ]
                            Html.text ")."
                        ]
                    ]
                ]
            )
            SidebarComponents.SidebarLayout.LogicContainer [ FileExporter.JsonExport(model, dispatch) ]
        ]
