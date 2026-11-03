module Renderer.Components.MainContent.PDFViewer

open Feliz
open Fable.Core.JsInterop
open Fable.Core
open Swate.Components.Primitive
open Swate.Components.Primitive.LoadingSpinner

module private PDFData =

    [<Emit("atob($0).length")>]
    let byteLength (base64Content: string) : int = jsNative

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
        let failedPdfUrl, setFailedPdfUrl = React.useState<string option> (None)
        let fileSize = PDFData.byteLength filehtml / 1024

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
                | Some url ->
                    Html.iframe [
                        prop.src url
                        prop.className "swt:w-full swt:h-full"
                        prop.onError (fun (_: Browser.Types.Event) -> setFailedPdfUrl (Some url))
                    ]

                | None -> LoadingSpinner.LoadingSpinner("Loading PDF", size = DaisyuiSize.XL)

                if pdfUrl |> Option.exists (fun url -> failedPdfUrl = Some url) then
                    Html.div [
                        prop.role.alert
                        prop.className "swt:badge swt:badge-error swt:text-error-content"
                        prop.text "This file is broken or corrupted."
                    ]

                Html.div [
                    prop.className "swt:w-full swt:max-w-full swt:rounded swt:bg-base-200 swt:p-3"
                    prop.children [
                        Html.p [
                            prop.className "swt:font-bold swt:text-lg"
                            prop.text "Metadata"
                        ]
                        Html.pre [
                            prop.className "swt:overflow-auto swt:whitespace-pre-wrap"
                            prop.text (
                                String.concat
                                    "\n"
                                    [ $"File: {path}"
                                      "MIME type: application/pdf"
                                      $"File size: {fileSize} kB" ]
                            )
                        ]
                    ]
                ]
            ]
        ]
