module Renderer.Components.MainContent.FileViewerTarget

open Feliz
open Renderer.Types
open Arcitisa.Electron.Shared.FileIOTypes

[<ReactComponent>]
let FileViewerTarget (pageState: PageState) =
    match pageState with
    | PageState.PDFPage(content, path) -> Arcitisa.Components.Page.FileViewer.PDFViewer.Main(content, path)
    | PageState.ImagePage(fileType, content, path) ->
        let mimeType =
            match fileType with
            | FileContentType.PNG -> "image/png"
            | FileContentType.JPG -> "image/jpeg"
            | FileContentType.SVG -> "image/svg+xml"
            | FileContentType.GIF -> "image/gif"
            | _ -> "application/octet-stream"

        Arcitisa.Components.Page.FileViewer.ImageViewer.Main(mimeType, content, path)
    | PageState.HtmlPage(content, path) -> Arcitisa.Components.Page.FileViewer.HtmlViewer.Main(content, path)
    | _ -> Html.div [ prop.text "Unsupported file type." ]
