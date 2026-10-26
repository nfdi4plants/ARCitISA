module Renderer.Components.LeftSidebar.FileExplorer.FileTreeContextMenu

open System
open Swate.Components
open Fable.Core
open Fable.Core.JsInterop
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameHelper
open Renderer.Components.LeftSidebar.FileExplorer.Helper
open Renderer.Components.LeftSidebar.FileExplorer.Types

let private withDividers (groups: ContextMenuItem list list) =
    groups
    |> List.filter (List.isEmpty >> not)
    |> List.mapi (fun index group ->
        if index = 0 then
            group
        else
            ContextMenuItem.divider :: group
    )
    |> List.collect id

let tryGetAbsoluteItemPath (arcRootPath: string option) (item: FileItem) =
    match arcRootPath, item.Path with
    | Some rootPath, Some relativePath ->
        let normalizedRootPath = PathHelpers.normalizePath rootPath
        let normalizedRelativePath = PathHelpers.normalizeRelativePath relativePath

        if String.IsNullOrWhiteSpace normalizedRootPath then
            None
        elif String.IsNullOrWhiteSpace normalizedRelativePath then
            Some normalizedRootPath
        else
            Some(PathHelpers.normalizePath $"{normalizedRootPath}/{normalizedRelativePath}")
    | _ -> None

let tryGetRelativeItemPath (item: FileItem) =
    item.Path
    |> Option.map PathHelpers.normalizeRelativePath
    |> Option.map PathHelpers.normalizePath
    |> Option.filter (String.IsNullOrWhiteSpace >> not)

let private applyPathActionError (config: PathActionConfig) (title: string) (message: string) =
    config.enqueueError (ErrorModalRequest.create (message, title = title))

let private runPathAction
    (config: PathActionConfig)
    (title: string)
    (execute: string -> JS.Promise<Result<unit, exn>>)
    (relativePath: string)
    =
    promise {
        match! execute relativePath with
        | Ok() -> ()
        | Error exn -> applyPathActionError config title exn.Message
    }
    |> Promise.catch (fun exn -> applyPathActionError config title exn.Message)
    |> Promise.start

let private copyTextToClipboard (text: string) =
    promise {
        try
            do! navigator.clipboard.writeText text
        with ex ->
            Browser.Dom.console.warn ($"Could not copy filetree path: {text}", ex)
    }
    |> Promise.start

let private importContextMenuItems (config: PathActionConfig) (item: FileItem) (targetRelativePath: string) =
    fileSystemCreateKinds
    |> List.filter (fun kind -> canCreateFileSystemItemIn kind item)
    |> List.map (fun kind ->
        let label, icon =
            match kind with
            | FileSystemItemKind.File -> "Import files", "swt:fluent--arrow-import-24-regular swt:rotate-180"
            | FileSystemItemKind.Folder -> "Import folders", "swt:fluent--folder-arrow-left-24-regular"

        ContextMenuItem.create
            label
            icon
            (fun () -> runPathAction config "Import failed" (config.importExternalPaths kind) targetRelativePath)
    )

let private pathActionContextMenuItemsForRelativePath
    (config: PathActionConfig)
    (item: FileItem)
    (relativePath: string)
    =
    [
        if not item.IsDirectory then
            ContextMenuItem.create
                "Open with Default Application"
                "swt:fluent--open-24-regular"
                (fun () -> runPathAction config "Open file failed" config.openPathWithDefaultApplication relativePath)

        else
            yield! importContextMenuItems config item relativePath

        ContextMenuItem.create
            "Open Folder Location"
            "swt:fluent--folder-open-24-regular"
            (fun () -> runPathAction config "Open folder location failed" config.openPathInFileExplorer relativePath)
    ]

let pathActionContextMenuItems (config: PathActionConfig) (item: FileItem) =
    match tryGetRelativeItemPath item with
    | None -> []
    | Some relativePath -> pathActionContextMenuItemsForRelativePath config item relativePath

let openContextMenuItems (config: ContextMenuConfig) (item: FileItem) =
    match tryGetRelativeItemPath item with
    | None -> []
    | Some relativePath -> [
        if
            not item.IsDirectory
            || tryGetArcEntityWorkbookName relativePath |> Option.isSome
        then
            ContextMenuItem.create "Open" "swt:fluent--open-24-regular" (fun () -> config.openItem item)

        yield! pathActionContextMenuItemsForRelativePath config.pathActionConfig item relativePath
      ]

let refreshContextMenuItems (refreshDirectory: FileItem -> unit) (item: FileItem) = [
    if item.IsDirectory && item.Path.IsSome then
        ContextMenuItem.create "Refresh folder" "swt:fluent--arrow-sync-24-regular" (fun () -> refreshDirectory item)
]

let copyPathContextMenuItems (arcRootPath: string option) (item: FileItem) = [
    match tryGetRelativeItemPath item with
    | Some relativePath ->
        ContextMenuItem.create "Copy Path" "swt:fluent--copy-24-regular" (fun () -> copyTextToClipboard relativePath)
    | None -> ()

    match tryGetAbsoluteItemPath arcRootPath item with
    | Some fullPath ->
        ContextMenuItem.create "Copy Full Path" "swt:fluent--copy-24-regular" (fun () -> copyTextToClipboard fullPath)
    | None -> ()
]

let arcCreateContextMenuItems
    (openCreateModal: ArcFilesDiscriminate -> unit)
    (openNoteDraft: unit -> unit)
    (item: FileItem)
    =
    if item.IsDirectory then
        [
            yield!
                arcCreateKinds
                |> List.map (fun config ->
                    ContextMenuItem.create $"Add {config.Label}" config.Icon (fun () -> openCreateModal config.Kind)
                )

            ContextMenuItem.create "Add Note" "swt:fluent--note-add-24-regular" openNoteDraft
        ]
    else
        []

let dataMapContextMenuItems
    (createDataMap: DatamapParentInfo -> unit)
    (requestDeleteItem: FileItem -> unit)
    (tryFindDataMapItemByPath: string -> FileItem option)
    (item: FileItem)
    =
    let materializedDataMapItem =
        item.Children
        |> Option.defaultValue []
        |> List.tryFind (fun child -> child.Path |> Option.bind DatamapParentInfo.tryFromPath |> Option.isSome)

    let parentInfo =
        if item.IsDirectory then
            item.Path |> Option.bind DatamapParentInfo.tryFromFolderPath
        else
            None

    let dataMapItem =
        materializedDataMapItem
        |> Option.orElseWith (fun () ->
            parentInfo
            |> Option.map DatamapParentInfo.toPath
            |> Option.bind tryFindDataMapItemByPath
        )

    match parentInfo, dataMapItem with
    | Some parentInfo, None -> [
        ContextMenuItem.create
            "Add DataMap"
            "swt:fluent--database-arrow-up-20-regular"
            (fun () -> createDataMap parentInfo)
      ]
    | Some _, Some dataMap -> [
        ContextMenuItem.styled
            "Delete DataMap"
            "swt:fluent--delete-24-regular"
            "swt:text-error"
            (fun () -> requestDeleteItem dataMap)
      ]
    | _ -> []

let fileSystemCreateContextMenuItems
    (openFileSystemCreateModal: FileSystemItemKind -> FileItem -> unit)
    (item: FileItem)
    =
    fileSystemCreateKinds
    |> List.filter (fun kind -> canCreateFileSystemItemIn kind item)
    |> List.map (fun kind ->
        ContextMenuItem.create
            $"New {fileSystemCreateKindLabel kind}"
            (fileSystemCreateKindIcon kind)
            (fun () -> openFileSystemCreateModal kind item)
    )

let rootContextMenuItems (config: ContextMenuConfig) (rootItem: FileItem) =
    withDividers [
        importContextMenuItems config.pathActionConfig rootItem ""
        fileSystemCreateContextMenuItems config.openFileSystemCreateModal rootItem
        arcCreateContextMenuItems config.openCreateModal config.openNoteDraft rootItem
    ]

/// True when a Download or Free runs on the item or on a path inside the item's delete scope.
/// Moving or removing such a path makes the running action fail after its transfer. The delete
/// scope of a canonical entity workbook is its entity folder, because deleting the workbook
/// removes that folder.
let isLockedByLfsActivity (lfsActivePaths: string seq) (item: FileItem) =
    item.LfsActivity.IsSome
    || item.Path
       |> Option.map (PathHelpers.normalizeCanonicalRelativePath >> ArcEntityPathRules.deleteScopePath)
       |> Option.exists (fun scopePath ->
           lfsActivePaths
           |> Seq.exists (fun activePath ->
               PathHelpers.isSameOrDescendantPathForFsComparison
                   (PathHelpers.normalizeCanonicalRelativePath activePath)
                   scopePath
           )
       )

let private disableWhileLfsActive (lfsActivePaths: string seq) (item: FileItem) (menuItem: ContextMenuItem) =
    if isLockedByLfsActivity lfsActivePaths item then
        { menuItem with Disabled = Some true }
    else
        menuItem

let renameContextMenuItems (lfsActivePaths: string seq) (requestRenameItem: FileItem -> unit) (item: FileItem) =
    if canRenameFileItem item then
        [
            ContextMenuItem.create "Rename" "swt:fluent--edit-24-regular" (fun () -> requestRenameItem item)
            |> disableWhileLfsActive lfsActivePaths item
        ]
    else
        []

let deleteContextMenuItems (lfsActivePaths: string seq) (requestDeleteItem: FileItem -> unit) (item: FileItem) =
    if canDeleteFileItem item then
        [
            ContextMenuItem.styled
                "Delete"
                "swt:fluent--delete-24-regular"
                "swt:text-error"
                (fun () -> requestDeleteItem item)
            |> disableWhileLfsActive lfsActivePaths item
        ]
    else
        []

let createContextMenuItems (config: ContextMenuConfig) arcScopeId =
    let toggleLfsMark =
        Renderer.Components.FileExplorerLfs.createToggleLfsMark config.enqueueError arcScopeId config.runToggleLfsMark

    let downloadLfsFile =
        Renderer.Components.FileExplorerLfs.createDownloadLfsFile
            config.enqueueError
            arcScopeId
            config.runDownloadLfsFile

    let freeLocalLfsCopy =
        Renderer.Components.FileExplorerLfs.createFreeLocalLfsCopy
            config.enqueueError
            arcScopeId
            config.runFreeLocalLfsCopy

    fun item ->
        withDividers [
            openContextMenuItems config item
            refreshContextMenuItems config.refreshDirectory item
            copyPathContextMenuItems config.arcRootPath item
            fileSystemCreateContextMenuItems config.openFileSystemCreateModal item
            dataMapContextMenuItems config.createDataMap config.requestDeleteItem config.tryFindDataMapItemByPath item
            Swate.Components.Page.FileExplorer.FileExplorerGitLfsHelper.contextMenuItems
                item
                toggleLfsMark
                (Some downloadLfsFile)
                (Some freeLocalLfsCopy)
            arcCreateContextMenuItems config.openCreateModal config.openNoteDraft item
            [
                yield! renameContextMenuItems config.lfsActivePaths config.requestRenameItem item
                yield! deleteContextMenuItems config.lfsActivePaths config.requestDeleteItem item
            ]
        ]
