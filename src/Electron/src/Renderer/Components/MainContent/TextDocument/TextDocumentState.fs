/// What the text page knows about its file, and how a save, a reload or a change on disk changes it. Every fact
/// about the document's file lives in this one record, so encoding, hash, read-only state and the saved text cannot
/// drift apart. The functions are pure, and TextDocumentSync applies them.
module Renderer.Components.MainContent.TextDocument.TextDocumentState

open Swate.Electron.Shared.FileIOTypes

/// Why the file on disk and the editor no longer agree.
type DiskNotice =
    /// Another program changed the file while the editor has unsaved edits.
    | ChangedOnDisk
    /// A save was refused because the file changed since it was read.
    | SaveConflict
    /// A save was refused because the file is gone.
    | SaveConflictMissing
    /// The file was deleted, or freed to a large file pointer, while the editor has unsaved edits.
    | RemovedFromDisk

type TextDocumentState = {
    /// What Main reported when it last read or wrote the file: encoding, hash and read-only reason. None when the
    /// file could not be read as text.
    Info: TextDocumentInfo option
    /// The text of that version. Discard goes back to it, and a remounted editor starts from it.
    SavedText: string
    /// The file is gone from disk, so the next save creates it, and is refused if one appeared meanwhile.
    MissingOnDisk: bool
    /// Set by Overwrite. The next save ignores a change on disk, and clears it.
    OverwriteNext: bool
    SavesRunning: int
    Dirty: bool
    Notice: DiskNotice option
    Error: string option
}

let init (content: string) (info: TextDocumentInfo option) : TextDocumentState = {
    Info = info
    SavedText = content
    MissingOnDisk = false
    OverwriteNext = false
    SavesRunning = 0
    Dirty = false
    Notice = None
    Error = None
}

let readOnlyMessage (reason: TextReadOnlyReason) =
    match reason with
    | TextReadOnlyReason.NotUtf8 ->
        "This file is not valid UTF-8. It opens read-only so a save cannot change its bytes."
    | TextReadOnlyReason.InvalidUtf16 ->
        "This file contains invalid UTF-16. It opens read-only so a save cannot change its bytes."
    | TextReadOnlyReason.TooLargeToEdit -> "This file is larger than 5 MB and opens read-only."
    | TextReadOnlyReason.SymbolicLink -> "This file is a link. It opens read-only."
    | TextReadOnlyReason.OutsideArc -> "This file lies outside the open ARC. It opens read-only."
    | TextReadOnlyReason.WriteProtected -> "This file is write-protected. It opens read-only."
    | TextReadOnlyReason.ChangedWhileReading -> "This file changed while it was read. Reload it to edit."

/// Why the editor is read-only, or None when the file can be edited. It follows the version last read, so a reload
/// that finds a file Main would refuse to write makes the editor read-only too.
let readOnlyText (state: TextDocumentState) : string option =
    match state.Info with
    | None -> Some "This file could not be read as text. It opens read-only."
    | Some info -> info.readOnly |> Option.map readOnlyMessage

let noticeMessage (notice: DiskNotice) =
    match notice with
    | ChangedOnDisk -> "The file changed on disk since you opened it."
    | SaveConflict -> "The file changed on disk since you opened it. Your changes are not saved."
    | SaveConflictMissing -> "The file no longer exists on disk. Your changes are not saved."
    | RemovedFromDisk -> "The file is no longer available on disk. Save to write your version again."

/// A version on disk becomes the document's version: the editor shows it, and the next save compares with it.
let adoptDiskVersion (content: string) (info: TextDocumentInfo option) (state: TextDocumentState) = {
    state with
        Info = info
        SavedText = content
        MissingOnDisk = false
        OverwriteNext = false
        Notice = None
        Error = None
}

/// A hash equal to the document's own is the echo of the page's own save (or of the read it started from).
let isNewVersion (info: TextDocumentInfo) (state: TextDocumentState) =
    state.Info |> Option.forall (fun own -> own.contentHash <> info.contentHash)

/// The page received another version from disk. Unsaved edits are kept and the page only says so, because the user
/// decides between Overwrite and Reload. Without unsaved edits the new version is adopted. The flag tells whether the
/// editor has to take the new text.
let diskChanged (content: string) (info: TextDocumentInfo) (state: TextDocumentState) : TextDocumentState * bool =
    if not (isNewVersion info state) then
        state, false
    elif state.Dirty then
        {
            state with
                Notice = Some ChangedOnDisk
        },
        false
    else
        adoptDiskVersion content (Some info) state, true

let removedFromDisk (state: TextDocumentState) = {
    state with
        MissingOnDisk = true
        Notice = Some RemovedFromDisk
}

let dirtyChanged (dirty: bool) (state: TextDocumentState) = { state with Dirty = dirty }

let overwriteRequested (state: TextDocumentState) = { state with OverwriteNext = true }

/// Clears an overwrite that no save consumed, for example because the editor refused to save.
let overwriteFinished (state: TextDocumentState) = { state with OverwriteNext = false }

/// The request that saves text over the version the document knows. The request takes OverwriteNext with it.
let saveRequest (path: string) (text: string) (state: TextDocumentState) : SaveTextFileRequest = {|
    path = path
    content = text
    encoding = state.Info |> Option.map _.encoding |> Option.defaultValue TextFileEncoding.Utf8
    // A file known to be missing is created, and the save is refused if one appeared meanwhile.
    expectedHash =
        if state.MissingOnDisk then
            None
        else
            state.Info |> Option.map _.contentHash
    overwrite = state.OverwriteNext
|}

let saveStarted (state: TextDocumentState) = {
    state with
        SavesRunning = state.SavesRunning + 1
        OverwriteNext = false
        Error = None
}

let saveSucceeded (text: string) (info: TextDocumentInfo) (state: TextDocumentState) = {
    state with
        SavesRunning = state.SavesRunning - 1
        Info = Some info
        SavedText = text
        MissingOnDisk = false
        Notice = None
}

/// currentExists tells whether the file still exists on disk.
let saveConflicted (currentExists: bool) (state: TextDocumentState) = {
    state with
        SavesRunning = state.SavesRunning - 1
        Notice = Some(if currentExists then SaveConflict else SaveConflictMissing)
}

let saveFailed (message: string) (state: TextDocumentState) = {
    state with
        SavesRunning = state.SavesRunning - 1
        Error = Some $"Saving failed: {message}"
}

/// The status next to the path, and whether it reports an error.
let status (state: TextDocumentState) : string * bool =
    match readOnlyText state, state.SavesRunning > 0, state.Error with
    | Some _, _, _ -> "Read-only", false
    | _, true, _ -> "Saving...", false
    | _, _, Some message -> message, true
    | _ when state.Dirty -> "Unsaved changes", false
    | _ -> "Saved", false
