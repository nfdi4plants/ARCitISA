module Swate.Components.Composite.TextEditor.Types

open Fable.Core

/// The text a save writes. The editor keeps the document it was read from, so only this exact
/// version counts as saved when the save succeeds, also if the user typed while it ran.
type TextEditorSnapshot = {| text: string |}

/// A selection as offsets into the text. head is the end where the cursor sits. A cursor has anchor = head.
type TextSelection = {| anchor: int; head: int |}

/// One replacement. from and to are offsets into the text before the edit.
type TextChange = {|
    from: int
    ``to``: int
    insert: string
|}

/// Changes that apply together as one undo step. The changes are sorted by from and do not overlap. Their offsets
/// refer to the text before the edit, and selection refers to the text after it.
type TextEdit = {|
    changes: TextChange[]
    selection: TextSelection
|}

/// The input of an edit command: the text with every line break written as "\n", and the main selection.
type TextEditState = {|
    text: string
    selection: TextSelection
|}

/// A key that runs an edit command. key uses CodeMirror's notation, for example "Mod-b". When run returns None,
/// the key goes on to the editor's other bindings. A read-only editor runs no binding, so its keys go to the
/// editor's default bindings.
type TextEditorKeyBinding = {|
    key: string
    run: TextEditState -> TextEdit option
|}

/// Whether the editor's undo history holds a change to undo or to redo.
type TextEditorHistory = {| canUndo: bool; canRedo: bool |}

/// Where completions are asked for: the cursor as a document position, the text of its line before and after it,
/// whether the user asked with a key (Ctrl-Space) instead of by typing, and the names of the syntax nodes at the
/// cursor, innermost first.
type TextCompletionContext = {|
    position: int
    lineBefore: string
    lineAfter: string
    explicit: bool
    syntaxPath: string[]
|}

/// One entry of the completion list. label is shown, matched against the typed text and inserted. iconClass holds
/// the CSS classes of the icon in front of it. With reopen the list opens again after the entry is inserted, for
/// example to continue inside a folder. boost moves the entry up (positive) or down among entries that match the
/// typed text equally well.
type TextCompletionOption = {|
    label: string
    iconClass: string option
    reopen: bool
    boost: int
|}

/// Entries for the text between the document positions from and to, which the chosen entry replaces. While validFor
/// accepts the text between from and to after an edit, the editor filters the same entries again and does not ask
/// the source. With None it asks the source after every edit.
type TextCompletionResult = {|
    from: int
    ``to``: int
    options: TextCompletionOption[]
    validFor: (string -> bool) option
|}

/// Answers a completion request, with None when there is nothing to complete at the cursor. A rejected promise
/// shows no completions, and the editor logs the error.
type TextCompletionSource = TextCompletionContext -> JS.Promise<TextCompletionResult option>

/// Imperative access for the host page. All members are safe to call before the editor exists
/// (they then do nothing, return false or "").
type TextEditorHandle =
    abstract isDirty: unit -> bool
    /// Runs the onSave callback for the current snapshot. Resolves true when the host reported success.
    abstract save: unit -> JS.Promise<bool>
    abstract getText: unit -> string
    /// The text and the main selection that edit commands work on. Unlike getText, the text writes every line break
    /// as "\n", so its offsets are editor positions. Before the editor exists the text is empty.
    abstract getEditState: unit -> TextEditState
    /// Applies the edit as one undo step and scrolls the selection into view. Returns false and changes nothing when
    /// the editor does not exist or is read-only.
    abstract applyEdit: edit: TextEdit -> bool
    /// Undoes the last change, like the editor's undo key. Returns false when there is nothing to undo, the editor
    /// does not exist or is read-only.
    abstract undo: unit -> bool
    /// Redoes the last undone change, like the editor's redo key. Returns false when there is nothing to redo, the
    /// editor does not exist or is read-only.
    abstract redo: unit -> bool
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
