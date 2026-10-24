module Renderer.Components.FileExplorerLfs

open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes

module FileExplorerGitLfsHelper = Swate.Components.Page.FileExplorer.FileExplorerGitLfsHelper

type FileTreeNodeLfsState = {
    IsLFS: bool option
    IsLFSPointer: bool option
    Downloaded: bool option
    Size: int64 option
    SizeFormatted: string option
}

let private mapLfsSize (sizeBytes: float option) =
    let size = sizeBytes |> Option.map int64
    size, (size |> Option.map FileTree.formatSize)

/// Folders never carry large object state. Files get it from the lookup by their ARC-relative path.
let getFileTreeNodeLfsState (tryFindLfs: string -> ObjectStateDto option) (node: FileTreeNode) : FileTreeNodeLfsState =
    let largeObject = if node.isDirectory then None else tryFindLfs node.path
    let lfsSize, lfsSizeFormatted = mapLfsSize (largeObject |> Option.bind _.SizeBytes)

    {
        IsLFS = largeObject |> Option.map (fun _ -> true)
        IsLFSPointer = largeObject |> Option.map (fun info -> not info.IsMaterialized)
        Downloaded = largeObject |> Option.map _.IsLocallyAvailable
        Size = lfsSize
        SizeFormatted = lfsSizeFormatted
    }

let withFileTreeNodeLfsState
    (tryFindLfs: string -> ObjectStateDto option)
    (node: FileTreeNode)
    (item: FileItem)
    : FileItem =
    let lfsState = getFileTreeNodeLfsState tryFindLfs node

    {
        item with
            IsLFS = lfsState.IsLFS
            IsLFSPointer = lfsState.IsLFSPointer
            Downloaded = lfsState.Downloaded
            Size = lfsState.Size
            SizeFormatted = lfsState.SizeFormatted
    }

let private createErrorReporter
    (enqueueErrorModal: ErrorModalRequest -> unit)
    (arcScopeId: string option)
    (title: string)
    =
    function
    | Some msg -> enqueueErrorModal (ErrorModalRequest.create (msg, title = title, ?scopeId = arcScopeId))
    | None -> ()

let private createLfsAction title action enqueueErrorModal arcScopeId runAction =
    action (createErrorReporter enqueueErrorModal arcScopeId title) runAction

let createToggleLfsMark enqueueErrorModal arcScopeId runToggle =
    createLfsAction
        "Git LFS update failed"
        FileExplorerGitLfsHelper.toggleLfsMark
        enqueueErrorModal
        arcScopeId
        runToggle

let createFreeLocalLfsCopy enqueueErrorModal arcScopeId runCleanup =
    createLfsAction
        "Git LFS cleanup failed"
        FileExplorerGitLfsHelper.freeLocalLfsCopy
        enqueueErrorModal
        arcScopeId
        runCleanup

let createDownloadLfsFile enqueueErrorModal arcScopeId runDownload =
    createLfsAction
        "Git LFS download failed"
        FileExplorerGitLfsHelper.downloadLfsFile
        enqueueErrorModal
        arcScopeId
        runDownload

let createLfsPillAction enqueueErrorModal arcScopeId runDownload =
    let downloadLfsFile = createDownloadLfsFile enqueueErrorModal arcScopeId runDownload

    fun item -> FileExplorerGitLfsHelper.lfsPillAction item (Some downloadLfsFile)
