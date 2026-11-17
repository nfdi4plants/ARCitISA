module Renderer.Components.MainContent.TextDocumentTargetView

open Fable.Core
open Feliz
open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Electron.Shared
open Swate.Electron.Shared.FileIOTypes
open Renderer.Types
open Renderer.Context.LeaveGuardContext

module private TextDocumentTargetHelpers =

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

    let noticeMessage (notice: DiskNotice) =
        match notice with
        | ChangedOnDisk -> "The file changed on disk since you opened it."
        | SaveConflict -> "The file changed on disk since you opened it. Your changes are not saved."
        | SaveConflictMissing -> "The file no longer exists on disk. Your changes are not saved."
        | RemovedFromDisk -> "The file is no longer available on disk. Save to write your version again."

open TextDocumentTargetHelpers

/// The text editor page for one file. It saves through saveTextFile with the hash it read, so a save never
/// overwrites a change made outside the app without asking.
[<ReactComponent(true)>]
let TextDocumentTarget (data: TextDocumentPageData) =
    let leaveGuard = useLeaveGuardCtx ()

    let expansionCtx =
        Renderer.Context.FileTreeExpansionContext.useFileTreeExpansionCtx ()

    let handleRef = React.useRef<TextEditorHandle option> None

    let baseHashRef =
        React.useRef (data.Info |> Option.map _.contentHash |> Option.defaultValue "")

    let lastSavedTextRef = React.useRef data.Content
    let overwriteNextRef = React.useRef false
    let missingOnDiskRef = React.useRef false
    let dirty, setDirty = React.useState false
    let saving, setSaving = React.useState false
    let error, setError = React.useState (None: string option)
    let notice, setNotice = React.useState (None: DiskNotice option)

    let readOnlyText =
        match data.Info with
        | None -> Some "This file could not be read as text. It opens read-only."
        | Some info -> info.readOnly |> Option.map readOnlyMessage

    let isReadOnly = readOnlyText.IsSome

    let onSave (snapshot: TextEditorSnapshot) : JS.Promise<bool> = promise {
        setSaving true
        setError None

        let request: SaveTextFileRequest = {|
            path = data.Path
            content = snapshot.text
            encoding = data.Info |> Option.map _.encoding |> Option.defaultValue TextFileEncoding.Utf8
            // A file known to be missing is created, and the save is refused if one appeared meanwhile.
            expectedHash =
                if missingOnDiskRef.current then
                    None
                else
                    Some baseHashRef.current
            overwrite = overwriteNextRef.current
        |}

        overwriteNextRef.current <- false

        let! result = Api.ipcArcVaultApi.saveTextFile request |> Promise.catch (fun e -> Error e)

        setSaving false

        match result with
        | Ok(SaveTextFileOutcome.Saved info) ->
            baseHashRef.current <- info.contentHash
            lastSavedTextRef.current <- snapshot.text
            missingOnDiskRef.current <- false
            // The tree sees the new file stamp as our own write and does not reload the page.
            expansionCtx.recordOwnWrite ()
            setNotice None
            return true
        | Ok(SaveTextFileOutcome.Conflict current) ->
            setNotice (Some(if current.IsSome then SaveConflict else SaveConflictMissing))
            return false
        | Error e ->
            setError (Some $"Saving failed: {e.Message}")
            return false
    }

    let onDirtyChange (value: bool) =
        setDirty value
        leaveGuard.setDirty value

    let save () =
        handleRef.current |> Option.iter (fun h -> h.save () |> ignore)

    let overwrite () =
        overwriteNextRef.current <- true
        save ()

    let reloadFromDisk () =
        promise {
            let! result = Api.ipcArcVaultApi.openFile data.Path

            match result, handleRef.current with
            | Ok opened, Some handle ->
                match opened.text with
                | Some info ->
                    handle.replaceContent opened.content.content
                    baseHashRef.current <- info.contentHash
                    lastSavedTextRef.current <- opened.content.content
                    missingOnDiskRef.current <- false
                    setNotice None
                    setError None
                | None -> setError (Some "The file could not be read as text.")
            | Error e, _ -> setError (Some $"Reloading failed: {e.Message}")
            | Ok _, None -> ()
        }
        |> Promise.start

    // The same path opens again after a change on disk. A hash equal to the base is the echo of our own save.
    React.useEffect (
        (fun () ->
            match data.Info, handleRef.current with
            | Some info, Some handle when info.contentHash <> baseHashRef.current ->
                if handle.isDirty () then
                    setNotice (Some ChangedOnDisk)
                else
                    handle.replaceContent data.Content
                    baseHashRef.current <- info.contentHash
                    lastSavedTextRef.current <- data.Content
                    setNotice None
            | _ -> ()
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
                    discard =
                        fun () ->
                            handleRef.current
                            |> Option.iter (fun h -> h.replaceContent lastSavedTextRef.current)
                    onRemovedFromDisk =
                        fun () ->
                            missingOnDiskRef.current <- true
                            setNotice (Some RemovedFromDisk)
                }

            { new System.IDisposable with
                member _.Dispose() = unregister ()
            }
        ),
        [| box data.Path |]
    )

    let status, statusClass =
        match isReadOnly, saving, error with
        | true, _, _ -> "Read-only", "swt:opacity-70"
        | _, true, _ -> "Saving...", "swt:opacity-70"
        | _, _, Some message -> message, "swt:text-error"
        | _ when dirty -> "Unsaved changes", "swt:opacity-70"
        | _ -> "Saved", "swt:opacity-70"

    let noticeButtons (current: DiskNotice) =
        match current with
        | RemovedFromDisk -> [ ("Save", save) ]
        | ChangedOnDisk
        | SaveConflict
        | SaveConflictMissing -> [
            "Overwrite", overwrite
            "Reload from disk", reloadFromDisk
          ]

    Html.div [
        prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:flex swt:flex-col swt:bg-base-100"
        prop.children [
            Html.div [
                prop.className
                    "swt:flex swt:flex-wrap swt:items-center swt:gap-2 swt:px-3 swt:py-2 swt:border-b swt:border-base-300"
                prop.children [
                    Html.span [
                        prop.className "swt:font-mono swt:text-sm swt:truncate"
                        prop.title data.Path
                        prop.text data.Path
                    ]
                    Html.span [
                        prop.className $"swt:text-xs {statusClass}"
                        prop.text status
                    ]
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-sm swt:btn-primary swt:ml-auto"
                        prop.disabled (isReadOnly || saving || not dirty)
                        prop.text "Save"
                        prop.onClick (fun _ -> save ())
                    ]
                ]
            ]
            match readOnlyText with
            | Some text ->
                Html.div [
                    prop.role "status"
                    prop.className "swt:alert swt:alert-info swt:rounded-none"
                    prop.text text
                ]
            | None -> Html.none
            match notice with
            | Some current ->
                Html.div [
                    prop.role "alert"
                    prop.className "swt:alert swt:alert-warning swt:rounded-none swt:flex swt:flex-wrap swt:gap-2"
                    prop.children [
                        Html.span [ prop.text (noticeMessage current) ]
                        for label, action in noticeButtons current do
                            Html.button [
                                prop.type'.button
                                prop.key label
                                prop.className "swt:btn swt:btn-sm"
                                prop.disabled saving
                                prop.text label
                                prop.onClick (fun _ -> action ())
                            ]
                    ]
                ]
            | None -> Html.none
            Html.div [
                prop.className "swt:flex-1 swt:min-h-0"
                prop.children [
                    TextEditor.TextEditor(
                        TextDocumentHelper.documentUri data.Path,
                        data.Content,
                        fileName = data.Path,
                        readOnly = isReadOnly,
                        onSave = onSave,
                        onDirtyChange = onDirtyChange,
                        handleRef = handleRef
                    )
                ]
            ]
        ]
    ]
