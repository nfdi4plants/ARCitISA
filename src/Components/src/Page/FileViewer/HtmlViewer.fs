namespace Swate.Components.Page.FileViewer

open Feliz
open Fable.Core

module private HtmlViewerHelpers =

    [<Emit("atob($0).length")>]
    let byteLength (base64Content: string) : int = jsNative

    /// Throws on content that is not base64. Bytes that are not UTF-8 become U+FFFD.
    [<Emit("new TextDecoder('utf-8').decode(Uint8Array.from(atob($0), c => c.charCodeAt(0)))")>]
    let decodeUtf8Base64 (base64Content: string) : string = jsNative

[<Erase; Mangle(false)>]
type HtmlViewer =

    /// The one sandbox for HTML files, whatever their size. Scripts run, but without allow-same-origin the page gets
    /// an opaque origin and cannot reach the app.
    /// A srcdoc page resolves URLs against the app's URL, so a link to #section would load the app into the frame.
    /// Against about:srcdoc, #section scrolls inside the page and other relative URLs no longer resolve at all, so
    /// they cannot reach the app's files. The base also wins over one the page sets, because the first base counts.
    /// A srcdoc page never renders in quirks mode, so markup before its doctype changes nothing.
    [<ReactComponent>]
    static member SandboxedFrame(html: string, ?className: string) =
        Html.iframe [
            prop.title "HTML preview"
            prop.custom ("srcDoc", """<base href="about:srcdoc">""" + html)
            prop.custom ("sandbox", "allow-scripts")
            prop.className (defaultArg className "swt:block swt:w-full swt:h-full swt:border-none swt:bg-white")
        ]

    /// An HTML file that is too large to open as text, read as base64. It is decoded as UTF-8.
    [<ReactComponent(true)>]
    static member Main(base64Content: string, path: string) =
        let decoded =
            React.useMemo (
                (fun () ->
                    try
                        Ok(HtmlViewerHelpers.decodeUtf8Base64 base64Content)
                    with e ->
                        Error e.Message
                ),
                [| box base64Content |]
            )

        let fileSize = HtmlViewerHelpers.byteLength base64Content / 1024

        Html.div [
            prop.className "swt:w-full swt:h-full swt:overflow-auto swt:p-4"
            prop.children [
                match decoded with
                | Ok html -> HtmlViewer.SandboxedFrame html
                | Error _ ->
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
