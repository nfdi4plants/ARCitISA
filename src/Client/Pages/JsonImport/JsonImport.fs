namespace Pages

open Feliz
open Fable.Core
open Fable.React
open Fable.React.Props
//open Fable.Core.JS
open Fable.Core.JsInterop

open Model
open Messages
open Browser.Types
open SpreadsheetInterface
open Messages
open Elmish

open Feliz
open Arcitisa.Components.Shared
open ARCtrl
open Fable.Core.JsInterop

open Modals

type private TemplateFromFileState = {
    /// User select type to upload
    FileType: ArcFilesDiscriminate
    /// User selects json type to upload
    JsonFormat: JsonExportFormat
    Loading: bool
} with

    static member init() = {
        FileType = ArcFilesDiscriminate.Assay
        JsonFormat = JsonExportFormat.ROCrate
        Loading = false
    }

type JsonImport =

    // static member private FileUploadButton
    //     (state: TemplateFromFileState, setState: TemplateFromFileState -> unit, dispatch)
    //     =


    [<ReactComponent>]
    static member JsonUploadComponent(model: Model, dispatch) =
        let state, setState = React.useState (TemplateFromFileState.init)
        let arcFile, setArcFile = React.useState (None: ArcFiles option)
        let closeImportModal = fun _ -> setArcFile None
        let KEY_SEPARATOR = "@"

        let keysToString (arcfile: ArcFilesDiscriminate, jsontype: JsonExportFormat) =
            sprintf "%A%s%A" arcfile KEY_SEPARATOR jsontype

        let tryKeysFromString (s: string) =
            let parts = s.Split KEY_SEPARATOR

            if parts.Length = 2 then
                let arcFile = parts.[0] |> ArcFilesDiscriminate.fromString
                let jsontype = parts.[1] |> JsonExportFormat.fromString

                Some(arcFile, jsontype)
            else
                None

        React.Fragment [
            match arcFile with
            | Some arcFile -> Modals.SelectiveImportModal.Main(arcFile, model, dispatch, closeImportModal)
            | None -> ()
            Html.select [
                prop.className "swt:select swt:w-full"
                prop.value (sprintf "%A@%A" state.FileType state.JsonFormat)
                prop.onChange (fun (ev: string) ->
                    match tryKeysFromString ev with
                    | Some(arcfile, jsontype) ->
                        setState {
                            state with
                                FileType = arcfile
                                JsonFormat = jsontype
                        }
                    | None -> ()
                )
                prop.children [
                    for (arcfile, jsontype) in Spreadsheet.IO.Json.readFromJsonMap.Keys |> Seq.sortBy fst do
                        Html.option [
                            prop.value (keysToString (arcfile, jsontype))
                            prop.text (sprintf "%A - %A" arcfile jsontype)
                        ]
                ]
            ]

            Html.div [
                Html.input [
                    prop.type'.file
                    prop.className "swt:file-input swt:file-input-neutral swt:w-full"
                    prop.onChange (fun (ev: File) ->
                        let reader = Browser.Dom.FileReader.Create()

                        reader.onload <-
                            fun evt ->
                                let (r: string) = evt.target?result

                                promise {
                                    setState { state with Loading = true }

                                    let arcFile =
                                        Spreadsheet.IO.Json.readFromJsonMap.[(state.FileType, state.JsonFormat)] r

                                    setArcFile (Some arcFile)
                                }
                                |> Promise.catch (fun e ->
                                    GenericError(Cmd.none, e) |> DevMsg |> dispatch
                                    setState { state with Loading = false }
                                )
                                |> Promise.map (fun c ->
                                    setState { state with Loading = false }
                                    c
                                )
                                |> Promise.start

                        reader.onerror <- fun evt -> curry GenericError Cmd.none (exn evt.Value) |> DevMsg |> dispatch

                        reader.readAsText (ev)
                    )
                    prop.onClick (fun e -> e.target?value <- null)
                ]
            ]
        ]

    [<ReactComponent>]
    static member Main(model, dispatch) =
        SidebarComponents.SidebarLayout.Container [
            SidebarComponents.SidebarLayout.Header "Json Import"

            // Box 2
            SidebarComponents.SidebarLayout.Description(
                Html.p [
                    Html.b "Import JSON files."
                    Html.text " You can use \"Json Export\" to create these files from existing ARCitISA tables. "
                ]
            )

            SidebarComponents.SidebarLayout.LogicContainer [ JsonImport.JsonUploadComponent(model, dispatch) ]
        ]
