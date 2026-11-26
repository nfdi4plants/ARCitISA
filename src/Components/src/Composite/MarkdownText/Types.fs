module Arcitisa.Components.Composite.MarkdownText.Types

[<RequireQualifiedAccess>]
type PreviewMode =
    | Live
    | Edit
    | Preview

type MarkdownOptions = {
    Mode: PreviewMode
    PreviewClassName: string option
}

[<RequireQualifiedAccess>]
module MarkdownOptions =
    let defaults = {
        Mode = PreviewMode.Live
        PreviewClassName = None
    }
