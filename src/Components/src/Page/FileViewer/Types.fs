module Swate.Components.Page.FileViewer.Types

/// How a text file looks when it is not shown as source.
[<RequireQualifiedAccess>]
type TextRendering =
    /// Through the sanitizing Markdown preview.
    | Markdown
    /// In the sandboxed frame of HtmlViewer.
    | Html
    /// As an image, so its scripts never run.
    | Svg
