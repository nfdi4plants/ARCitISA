module Renderer.Components.FileExplorerLfs

open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes

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

let getFileTreeNodeLfsState (node: FileTreeNode) : FileTreeNodeLfsState =
    let lfsSize, lfsSizeFormatted =
        mapLfsSize (node.largeObject |> Option.bind _.SizeBytes)

    {
        IsLFS = node.largeObject |> Option.map (fun _ -> true)
        IsLFSPointer = node.largeObject |> Option.map (fun info -> not info.IsMaterialized)
        Downloaded = node.largeObject |> Option.map _.IsLocallyAvailable
        Size = lfsSize
        SizeFormatted = lfsSizeFormatted
    }

let withFileTreeNodeLfsState (node: FileTreeNode) (item: FileItem) : FileItem =
    let lfsState = getFileTreeNodeLfsState node

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
