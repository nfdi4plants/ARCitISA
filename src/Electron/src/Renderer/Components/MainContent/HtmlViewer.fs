module Renderer.Components.MainContent.HtmlViewer

open Feliz
open Renderer
open Renderer.Components.Helper.MetadataFormatting

[<ReactComponent>]
let Main (base64Content: string, path: string) =
    let htmlContent = "data:text/html;base64," + base64Content
    let failedHtmlUrl, setFailedHtmlUrl = React.useState<string option> (None)
    let metadata, setMetadata = React.useState<string option> (None)

    React.useEffect (
        (fun () ->
            promise {
                match! Api.ipcArcVaultApi.readFileMetadata path with
                | Ok metadataText -> setMetadata (Some metadataText)
                | Error ex -> setMetadata (Some("Metadata unavailable: " + ex.Message))
            }
            |> Promise.start
        ),
        [| box path |]
    )

    Html.div [
        prop.className "swt:w-full swt:h-full swt:overflow-auto swt:p-4"
        prop.children [
            Html.iframe [
                prop.src htmlContent
                prop.title "HTML preview"
                prop.className "swt:block swt:w-full swt:h-full swt:border-none"
                prop.onError (fun (_: Browser.Types.Event) -> setFailedHtmlUrl (Some htmlContent))
            ]

            if failedHtmlUrl = Some htmlContent then
                Html.div [
                    prop.role.alert
                    prop.className "swt:badge swt:badge-error swt:text-error-content"
                    prop.text "This file is broken or corrupted."
                ]

            Html.div [
                prop.className "swt:w-full swt:max-w-2xl swt:rounded swt:bg-base-200 swt:p-3"
                prop.children [
                    Html.p [
                        prop.className "swt:font-bold swt:text-lg"
                        prop.text "Metadata"
                    ]
                    Html.pre [
                        prop.className "swt:max-h-64 swt:overflow-auto swt:whitespace-pre-wrap"
                        prop.text (metadata |> Option.map format |> Option.defaultValue "Reading metadata...")
                    ]
                ]
            ]
        ]
    ]
