module Renderer.Components.LeftSidebar.FileExplorer.Types

open Fable.Core
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes

type PathActionConfig = {
    openPathInFileExplorer: string -> JS.Promise<Result<unit, exn>>
    openPathWithDefaultApplication: string -> JS.Promise<Result<unit, exn>>
    importExternalFiles: string -> JS.Promise<Result<unit, exn>>
    enqueueError: ErrorModalRequest -> unit
}

type ContextMenuConfig = {
    openItem: FileItem -> unit
    arcRootPath: string option
    openCreateModal: ArcFilesDiscriminate -> unit
    openNoteDraft: unit -> unit
    createDataMap: DatamapParentInfo -> unit
    tryFindDataMapItemByPath: string -> FileItem option
    openFileSystemCreateModal: FileSystemItemKind -> FileItem -> unit
    requestRenameItem: FileItem -> unit
    requestDeleteItem: FileItem -> unit
    pathActionConfig: PathActionConfig
    enqueueError: ErrorModalRequest -> unit
    runToggleLfsMark: string -> bool -> JS.Promise<Result<unit, string>>
    runDownloadLfsFile: string -> JS.Promise<Result<unit, string>>
    runFreeLocalLfsCopy: string -> JS.Promise<Result<unit, string>>
    /// Loads a folder again from disk, also when it is loaded already.
    refreshDirectory: FileItem -> unit
    /// ARC-relative paths with a running Download or Free. Rename and Delete stay disabled on every
    /// folder that contains one of them.
    lfsActivePaths: string list
}

type ArcCreateDraft = { ArcFile: ArcFiles; Path: string }

type ArcCreateKindConfig = {
    Kind: ArcFilesDiscriminate
    Label: string
    FolderName: string
    Icon: string
}

type FileSystemCreateDraft = {
    Parent: FileItem
    Kind: FileSystemItemKind
}

type ArcRenameDraft = {
    Item: FileItem
    SourcePath: string
    InitialName: string
}
