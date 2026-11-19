/// The owner of a text document's lifecycle: it holds the TextDocumentState, talks to Main for save and reload,
/// follows changes on disk and registers the document with the leave guard. The page only renders what it returns.
module Renderer.Components.MainContent.TextDocument.TextDocumentSync

open Fable.Core
open Feliz
open Swate.Components.Composite.TextEditor.Types
open Swate.Electron.Shared
open Swate.Electron.Shared.FileIOTypes
open Renderer.Types
open Renderer.Context.LeaveGuardContext
open Renderer.Components.MainContent.TextDocument.TextDocumentState

type TextDocumentSync = {
    State: TextDocumentState
    /// For the editor's onSave.
    OnSave: TextEditorSnapshot -> JS.Promise<bool>
    /// For the editor's onDirtyChange.
    OnDirtyChange: bool -> unit
    Save: unit -> unit
    /// Saves over a change on disk.
    Overwrite: unit -> unit
    /// Reads the file again and drops the unsaved edits.
    ReloadFromDisk: unit -> unit
}

/// handleRef is the editor of the page. onReplaced hears the text the editor shows after a reload, a discard or an
/// adopted change on disk.
[<Hook>]
let useTextDocumentSync
    (data: TextDocumentPageData)
    (handleRef: IRefValue<TextEditorHandle option>)
    (onReplaced: string -> unit)
    : TextDocumentSync =
    let leaveGuard = useLeaveGuardCtx ()

    let expansionCtx =
        Renderer.Context.FileTreeExpansionContext.useFileTreeExpansionCtx ()

    let state, setState = React.useState (fun () -> init data.Content data.Info)
    // The save callback and the leave guard run outside renders, so they read the state through this one ref.
    let stateRef = React.useRef state
    let onReplacedRef = React.useRef onReplaced
    onReplacedRef.current <- onReplaced

    let update (change: TextDocumentState -> TextDocumentState) =
        stateRef.current <- change stateRef.current
        setState stateRef.current

    let replaceEditorText (text: string) =
        handleRef.current |> Option.iter (fun handle -> handle.replaceContent text)
        onReplacedRef.current text

    let onSave (snapshot: TextEditorSnapshot) : JS.Promise<bool> = promise {
        let request = saveRequest data.Path snapshot.text stateRef.current
        update saveStarted

        let! result = Api.ipcArcVaultApi.saveTextFile request |> Promise.catch (fun e -> Error e)

        match result with
        | Ok(SaveTextFileOutcome.Saved info) ->
            update (saveSucceeded snapshot.text info)
            // The tree sees the new file stamp as our own write and does not reload the page.
            expansionCtx.recordOwnWrite ()
            return true
        | Ok(SaveTextFileOutcome.Conflict current) ->
            update (saveConflicted current.IsSome)
            return false
        | Error e ->
            update (saveFailed e.Message)
            return false
    }

    let onDirtyChange (dirty: bool) =
        update (dirtyChanged dirty)
        leaveGuard.setDirty dirty

    let save () =
        handleRef.current |> Option.iter (fun h -> h.save () |> ignore)

    let overwrite () =
        match handleRef.current with
        | Some h ->
            update overwriteRequested
            // The editor calls no onSave while read-only or while a save runs, so the request is cleared here too.
            h.save () |> Promise.map (fun _ -> update overwriteFinished) |> ignore
        | None -> ()

    let reloadFromDisk () =
        promise {
            match! Api.ipcArcVaultApi.openFile data.Path |> Promise.catch (fun e -> Error e) with
            | Ok opened ->
                match opened.text with
                | Some info ->
                    update (adoptDiskVersion opened.content.content (Some info))
                    replaceEditorText opened.content.content
                | None ->
                    update (fun s -> {
                        s with
                            Error = Some "The file could not be read as text."
                    })
            | Error e ->
                update (fun s -> {
                    s with
                        Error = Some $"Reloading failed: {e.Message}"
                })
        }
        |> Promise.start

    // The same path opens again after a change on disk, with the version Main read.
    React.useEffect (
        (fun () ->
            match data.Info with
            | Some info ->
                let next, adopted = diskChanged data.Content info stateRef.current

                if not (obj.ReferenceEquals(next, stateRef.current)) then
                    update (fun _ -> next)

                if adopted then
                    replaceEditorText data.Content
            | None -> ()
        ),
        [| box data.Content; box data.Info |]
    )

    React.useEffect (
        (fun () ->
            let unregister =
                leaveGuard.register {
                    documentPath = data.Path
                    isDirty = fun () -> handleRef.current |> Option.exists (fun h -> h.isDirty ())
                    save =
                        fun () ->
                            match handleRef.current with
                            | Some h -> h.save ()
                            | None -> Promise.lift false
                    isSaving = fun () -> stateRef.current.SavesRunning > 0
                    discard = fun () -> replaceEditorText stateRef.current.SavedText
                    onRemovedFromDisk = fun () -> update removedFromDisk
                }

            { new System.IDisposable with
                member _.Dispose() = unregister ()
            }
        ),
        [| box data.Path |]
    )

    {
        State = state
        OnSave = onSave
        OnDirtyChange = onDirtyChange
        Save = save
        Overwrite = overwrite
        ReloadFromDisk = reloadFromDisk
    }
