module Renderer.Components.MainContent.FileViewerTarget

open Feliz
open Renderer.Types
open Swate.Electron.Shared.FileIOTypes

[<ReactComponent>]
let FileViewerTarget (pageState: PageState) =
    match pageState with
    | PageState.PDFPage(content, path) ->
        Swate.Components.Page.PDFViewer.DisplayPDF.Main(content, path)
    | PageState.ImagePage(fileType, content, path) ->
        let mimeType =
            match fileType with
            | FileContentType.PNG -> "image/png"
            | FileContentType.JPG -> "image/jpeg"
            | FileContentType.SVG -> "image/svg+xml"
            | FileContentType.GIF -> "image/gif"
            | _ -> "application/octet-stream"

        Swate.Components.Page.ImageViewer.Main(mimeType, content, path)
    | PageState.HtmlPage(content, path) ->
        Swate.Components.Page.HtmlViewer.Main(content, path)
    | _ -> Html.div [ prop.text "Unsupported file type." ]
