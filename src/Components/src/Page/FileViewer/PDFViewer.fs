namespace Swate.Components.Page.FileViewer

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

[<Erase; Mangle(false)>]
type PDFViewer =
    [<ReactComponent>]
    static member Main(filehtml, _path: string) =
        let pdfUrl, setPdfUrl = React.useState<string option> (None)

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
                    ]

                | None -> LoadingSpinner.LoadingSpinner("Loading PDF", size = DaisyuiSize.XL)
            ]
        ]
