module Swate.Components.Page.FileExplorer.FileExplorerGitLfsHelper

open Fable.Core
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared

module FileItemHelper = Swate.Components.Page.FileExplorer.Helper

let private downloadLabel = "Download LFS file"
let private downloadIcon = "swt:fluent--cloud-arrow-down-24-regular"
let private freeCopyLabel = "Free local LFS copy"
let private freeCopyIcon = "swt:fluent--document-arrow-up-20-regular"
let private downloadFolderLabel = "Download all LFS files"
let private freeFolderLabel = "Free all local LFS copies"

let toggleLfsMark
    (setError: string option -> unit)
    (runToggle: string -> bool -> JS.Promise<Result<unit, string>>)
    : (FileItem -> bool -> unit) =
    fun item markAsLfs ->
        promise {
            match item.Path with
            | None -> ()
            | Some relativePath ->
                if System.String.IsNullOrWhiteSpace relativePath then
                    setError (Some "Cannot mark ARC root as a Git LFS file.")
                else
                    match GitLfsRules.tryGetToggleBlockedReason relativePath item.Size markAsLfs with
                    | Some blockedReason -> setError (Some blockedReason)
                    | None ->
                        let! result = runToggle relativePath markAsLfs

                        match result with
                        | Ok _ -> setError None
                        | Error msg -> setError (Some $"Git LFS update failed for '{item.Name}': {msg}")
        }
        |> Promise.start

let private runLfsFileAction
    (setError: string option -> unit)
    (runAction: string -> JS.Promise<Result<unit, string>>)
    (rootError: string)
    (canRun: FileItem -> bool)
    (blockedMessage: FileItem -> string)
    (failureMessage: FileItem -> string -> string)
    : FileItem -> unit =
    fun item ->
        promise {
            match item.Path with
            | None -> ()
            | Some path when System.String.IsNullOrWhiteSpace path -> setError (Some rootError)
            | Some _ when not (FileItemHelper.isLfs item) ->
                setError (Some $"'{item.Name}' is not marked as a Git LFS file.")
            | Some _ when not (canRun item) -> setError (Some(blockedMessage item))
            | Some path ->
                match! runAction path with
                | Ok _ -> setError None
                | Error msg -> setError (Some(failureMessage item msg))
        }
        |> Promise.start

let freeLocalLfsCopy
    (setError: string option -> unit)
    (runCleanup: string -> JS.Promise<Result<unit, string>>)
    : FileItem -> unit =
    runLfsFileAction
        setError
        runCleanup
        "Cannot free the ARC root as a Git LFS file."
        FileItemHelper.hasLocalLfsCopy
        (fun item -> $"'{item.Name}' is already stored locally as an LFS pointer.")
        (fun item msg -> $"Git LFS cleanup failed for '{item.Name}': {msg}")

let downloadLfsFile
    (setError: string option -> unit)
    (runDownload: string -> JS.Promise<Result<unit, string>>)
    : FileItem -> unit =
    runLfsFileAction
        setError
        runDownload
        "Cannot download the ARC root as a Git LFS file."
        FileItemHelper.needsLfsDownload
        (fun item -> $"'{item.Name}' is already downloaded.")
        (fun item msg -> $"Git LFS download failed for '{item.Name}': {msg}")

let private lfsAction label icon item enabled action =
    action
    |> Option.map (fun handler ->
        if enabled then
            ContextMenuItem.forItem label icon handler item
        else
            ContextMenuItem.disabled label icon
    )

/// The folder entries that download or free every Git LFS file below a folder. The counts come from the caller.
/// A folder without an LFS file below it gets no entries. The function disables both entries while an action runs
/// on the folder, and while the caller reports the folder as busy (for example when an action runs on a path below
/// it).
let folderContextMenuItems
    (item: FileItem)
    (isBusy: bool)
    (pendingDownloads: int)
    (localCopies: int)
    (onDownloadFolder: FileItem -> unit)
    (onFreeFolder: FileItem -> unit)
    : ContextMenuItem list =
    if not item.IsDirectory || pendingDownloads + localCopies = 0 then
        []
    else
        let isIdle = item.LfsActivity.IsNone && not isBusy

        [
            yield!
                lfsAction downloadFolderLabel downloadIcon item (pendingDownloads > 0 && isIdle) (Some onDownloadFolder)
                |> Option.toList

            yield!
                lfsAction freeFolderLabel freeCopyIcon item (localCopies > 0 && isIdle) (Some onFreeFolder)
                |> Option.toList
        ]

let lfsPillAction (item: FileItem) onDownloadLfsFile =
    let withFallback label icon enabled action =
        lfsAction label icon item enabled action
        |> Option.defaultValue (ContextMenuItem.disabled label icon)

    let isIdle = item.LfsActivity.IsNone

    if item.IsDirectory || not (FileItemHelper.isLfs item) then
        None
    elif FileItemHelper.needsLfsDownload item then
        withFallback downloadLabel downloadIcon isIdle onDownloadLfsFile |> Some
    elif FileItemHelper.hasLocalLfsCopy item then
        // A click on the pill must not free a local copy. Free stays in the context menu.
        None
    else
        ContextMenuItem.disabled downloadLabel downloadIcon |> Some

let contextMenuItems
    (item: FileItem)
    (onToggleLfsMark: FileItem -> bool -> unit)
    (onDownloadLfsFile: (FileItem -> unit) option)
    (onFreeLocalLfsCopy: (FileItem -> unit) option)
    : ContextMenuItem list =
    if item.IsDirectory then
        []
    else
        let isMarked = FileItemHelper.isLfs item
        let isBusy = item.LfsActivity.IsSome
        let needsDownload = FileItemHelper.needsLfsDownload item
        let hasLocalCopy = FileItemHelper.hasLocalLfsCopy item

        let toggleBlockedReason =
            item.Path
            |> Option.bind (fun relativePath ->
                GitLfsRules.tryGetToggleBlockedReason relativePath item.Size (not isMarked)
            )

        let toggleLabel = if isMarked then "Unmark Git LFS" else "Mark Git LFS"

        let toggleIcon =
            if isMarked then
                "swt:fluent--document-dismiss-24-regular"
            else
                "swt:fluent--document-add-24-regular"

        let statusItem =
            ContextMenuItem.disabled
                (if isMarked then
                     "Git LFS: marked"
                 else
                     "Git LFS: not marked")
                "swt:fluent--tag-24-regular"

        if isBusy then
            [
                ContextMenuItem.disabled toggleLabel toggleIcon
                ContextMenuItem.disabled downloadLabel downloadIcon
                ContextMenuItem.disabled freeCopyLabel freeCopyIcon
                statusItem
            ]
        else
            [
                if toggleBlockedReason.IsSome then
                    ContextMenuItem.disabled toggleLabel toggleIcon
                else
                    ContextMenuItem.create toggleLabel toggleIcon (fun () -> onToggleLfsMark item (not isMarked))

                if isMarked then
                    yield!
                        lfsAction downloadLabel downloadIcon item needsDownload onDownloadLfsFile
                        |> Option.toList

                    yield!
                        lfsAction freeCopyLabel freeCopyIcon item hasLocalCopy onFreeLocalLfsCopy
                        |> Option.toList

                statusItem
            ]
