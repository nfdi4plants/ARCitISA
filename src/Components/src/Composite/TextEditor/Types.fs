module Swate.Components.Composite.TextEditor.Types

open Fable.Core

/// The text a save writes. The editor keeps the document it was read from, so only this exact
/// version counts as saved when the save succeeds, also if the user typed while it ran.
type TextEditorSnapshot = {| text: string |}

/// Imperative access for the host page. All members are safe to call before the editor exists
/// (they then do nothing, return false or "").
type TextEditorHandle =
    abstract isDirty: unit -> bool
    /// Runs the onSave callback for the current snapshot. Resolves true when the host reported success.
    abstract save: unit -> JS.Promise<bool>
    abstract getText: unit -> string
    /// Replaces the whole content, for example after a reload from disk. The new content counts as saved and the undo history starts over.
    abstract replaceContent: text: string -> unit
    abstract focus: unit -> unit

/// One finding of a lint pass, with offsets into the text. Same shape as a @codemirror/lint
/// Diagnostic. Severity is "error", "warning", "info" or "hint". Source is "syntax" or "schema".
type LintDiagnostic = {|
    from: int
    ``to``: int
    severity: string
    message: string
    source: string
|}

/// A lint finding as the host sees it, with the 1-based line and column of its start.
type TextEditorDiagnostic = {|
    from: int
    ``to``: int
    line: int
    column: int
    severity: string
    message: string
    source: string
|}
