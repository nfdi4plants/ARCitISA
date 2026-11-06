namespace Swate.Components.Page.FileViewer

open Feliz
open Fable.Core

module private HtmlData =

    [<Emit("atob($0).length")>]
    let byteLength (base64Content: string) : int = jsNative

[<Erase; Mangle(false)>]
type HtmlViewer =
    [<ReactComponent>]
    static member Main(base64Content: string, path: string) =
        let htmlContent = "data:text/html;base64," + base64Content
        let failedHtmlUrl, setFailedHtmlUrl = React.useState<string option> (None)
        let fileSize = HtmlData.byteLength base64Content / 1024

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
                            prop.text (
                                String.concat "\n" [
                                    $"File: {path}"
                                    "MIME type: text/html"
                                    $"File size: {fileSize} kB"
                                ]
                            )
                        ]
                    ]
                ]

            ]
        ]
