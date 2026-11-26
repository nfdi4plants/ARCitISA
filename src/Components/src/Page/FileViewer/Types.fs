module Arcitisa.Components.Page.FileViewer.Types

open Fable.Core

/// How a text file looks when it is not shown as source.
[<RequireQualifiedAccess>]
type TextRendering =
    /// Through the sanitizing Markdown preview.
    | Markdown
    /// In the sandboxed frame of HtmlViewer.
    | Html
    /// As an image, so its scripts never run.
    | Svg

/// A button of a TextDocumentNotice.
type TextDocumentNoticeAction = {
    Label: string
    Run: unit -> unit
    Disabled: bool
}

/// A warning of the host above the editor, such as a change on disk, with the buttons that answer it.
type TextDocumentNotice = {
    /// Tells the notices apart, so each keeps its element between renders.
    Key: string
    Message: string
    Actions: TextDocumentNoticeAction list
}

/// Access to a TextDocument for the host that owns its file. All members are safe to call before the editor exists.
type TextDocumentHandle =
    /// Replaces the text of the editor and of the rendered side, for example after a reload from disk. The new text
    /// counts as saved and the undo history starts over.
    abstract replaceText: text: string -> unit
    /// Runs the onSave of the page for the text in the editor. Resolves true when the host reported success, and
    /// false without an editor.
    abstract save: unit -> JS.Promise<bool>
    /// Whether the editor holds changes since the last save or replaceText.
    abstract isDirty: unit -> bool
