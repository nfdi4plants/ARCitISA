module Renderer.Components.MainContent.PDFViewer

open Feliz
open Fable.Core.JsInterop
open Fable.Core
open Swate.Components.Primitive
open Swate.Components.Primitive.LoadingSpinner
open Renderer
open Renderer.Components.Helper.MetadataFormatting

module private PDFData =

    [<Emit("new Uint8Array([...atob($0)].map(c => c.charCodeAt(0)))")>]
    let fromBase64 (value: string) : obj = jsNative

    [<Emit("URL.createObjectURL(new Blob([$0], { type: 'application/pdf' }))")>]
    let createUrl (data: obj) : string = jsNative

    [<Emit("URL.revokeObjectURL($0)")>]
    let revokeUrl (url: string) : unit = jsNative

type DisplayPDF =
    [<ReactComponent>]
    static member Main(filehtml, path: string) =

        let pdfUrl, setPdfUrl = React.useState<string option> (None)
        let metadata, setMetadata = React.useState<string option>(None)

        React.useEffect (
            (fun () ->
                promise {
                    match! Api.ipcArcVaultApi.readFileMetadata path with
                    | Ok metadataText -> setMetadata (Some metadataText)
                    | Error ex -> setMetadata (Some ("Metadata unavailable: " + ex.Message))
                }
                |> Promise.start),
            [| box path |]
        )

        React.useEffect (
            (fun () ->
                let data = PDFData.fromBase64 filehtml
                let url = PDFData.createUrl data

                setPdfUrl (Some url)
                fun () -> PDFData.revokeUrl url

            ),
            [| box filehtml |]
        )

        Html.div [
            prop.className "swt:w-full"
            prop.children [
                match pdfUrl with
                | Some url -> Html.iframe [ prop.src url; prop.className "swt:w-full swt:h-full" ]

                | None -> LoadingSpinner.LoadingSpinner("Loading PDF", size = DaisyuiSize.XL)

                Html.pre [
                    prop.className "swt:max-h-64 swt:overflow-auto swt:whitespace-pre-wrap"
                    prop.text (metadata |> Option.map format |> Option.defaultValue "Reading metadata...")
                ]
            ]
        ]
