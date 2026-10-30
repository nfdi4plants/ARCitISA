module Renderer.Components.MainContent.ImageViewer

open Feliz
open Renderer.Components.Helper.MetadataFormatting

[<ReactComponent>]
let Main (filetype: string, base64Content: string, path: string) =
    let imageUrl = "data:" + filetype + ";base64," + base64Content
    let failedImageUrl, setFailedImageUrl = React.useState<string option> (None)
    let metadata, setMetadata = React.useState<string option> (None)

    React.useEffect (
        (fun () ->
            promise {
                try
                    match! Api.ipcArcVaultApi.readFileMetadata path with
                    | Ok metadataText -> setMetadata (Some metadataText)
                    | Error ex -> setMetadata (Some("Metadata unavailable: " + ex.Message))
                with ex ->
                    setMetadata (Some("Metadata unavailable: " + ex.Message))
            }
            |> Promise.start
        ),
        [| box imageUrl |]
    )

    Html.div [
        prop.className "swt:w-full swt:h-full swt:flex swt:flex-col swt:items-center swt:overflow-auto swt:p-4"
        prop.children [
            Html.div [
                prop.className
                    "swt:flex swt:min-h-0 swt:w-full swt:flex-1 swt:flex-col swt:items-center swt:justify-center swt:gap-2"
                prop.children [
                    Html.img [
                        prop.src imageUrl
                        prop.alt "Image preview"
                        prop.className "swt:max-h-full swt:max-w-full swt:object-contain"
                        prop.onError (fun (_: Browser.Types.Event) -> setFailedImageUrl (Some imageUrl))
                    ]
                    if failedImageUrl = Some imageUrl then
                        Html.div [
                            prop.role.alert
                            prop.className "swt:badge swt:badge-error swt:text-error-content"
                            prop.text "This file is broken or corrupted."
                        ]
                ]
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
