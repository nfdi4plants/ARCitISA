namespace Arcitisa.Components.Composite.Widgets

open Fable.Core
open Feliz
open Arcitisa.Components.Shared

module private JsonFormatSelectHelper =

    let width (formats: JsonExportFormat list) (selectedFormat: JsonExportFormat) =
        selectedFormat :: formats
        |> List.map (fun format -> format.AsStringRdbl.Length)
        |> List.max
        |> fun characters -> $"calc({characters}ch + 4rem)"

open JsonFormatSelectHelper

[<Erase; Mangle(false)>]
type JsonFormatSelect =

    [<ReactComponent>]
    static member JsonFormatSelect
        (
            formats: JsonExportFormat list,
            selectedFormat: JsonExportFormat,
            onFormatChange: JsonExportFormat -> unit,
            ?disabled: bool,
            ?testId: string
        ) =
        let disabled = defaultArg disabled false
        let selectWidth = width formats selectedFormat

        Html.select [
            if testId.IsSome then
                prop.testId testId.Value
            prop.className "swt:select swt:join-item swt:shrink-0"
            prop.style [
                style.custom ("width", selectWidth)
                style.custom ("minWidth", selectWidth)
            ]
            prop.onChange (JsonExportFormat.fromString >> onFormatChange)
            prop.value (string selectedFormat)
            prop.disabled disabled
            prop.children [
                for format in formats do
                    Html.option [
                        prop.value (string format)
                        prop.text (format.AsStringRdbl)
                    ]
            ]
        ]
