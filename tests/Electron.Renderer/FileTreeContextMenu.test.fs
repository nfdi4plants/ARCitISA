module ElectronRenderer.FileTreeContextMenuTests

open Renderer.Components.LeftSidebar.FileExplorer.Helper
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeContextMenu
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Vitest
open Renderer.Components.LeftSidebar.FileExplorer.Types


let private createConfig () : PathActionConfig = {
    openPathInFileExplorer = fun _ -> promise { return Ok() }
    openPathWithDefaultApplication = fun _ -> promise { return Ok() }
    importExternalFiles = fun _ -> promise { return Ok() }
    enqueueError = ignore
}

let private createContextMenuConfig () : ContextMenuConfig = {
    openItem = ignore
    arcRootPath = Some "C:\\arc-root"
    openCreateModal = ignore
    openNoteDraft = ignore
    createDataMap = ignore
    tryFindDataMapItemByPath = fun _ -> None
    openFileSystemCreateModal = fun _ _ -> ()
    requestRenameItem = ignore
    requestDeleteItem = ignore
    pathActionConfig = createConfig ()
    enqueueError = ignore
    runToggleLfsMark = fun _ _ -> promise { return Ok() }
    runDownloadLfsFile = fun _ -> promise { return Ok() }
    runFreeLocalLfsCopy = fun _ -> promise { return Ok() }
    refreshDirectory = ignore
    lfsActivePaths = []
}

let private createComposedContextMenuItems config item = createContextMenuItems config None item

let private createFileItem (name: string) (path: string option) = {
    FileTree.createFile name path FileItemIcon.Document with
        Id = defaultArg path name
}

let private createLfsFileItem (name: string) (path: string) (downloaded: bool) (isPointer: bool) = {
    createFileItem name (Some path) with
        IsLFS = Some true
        Downloaded = Some downloaded
        IsLFSPointer = Some isPointer
        LfsActivity = None
        SizeFormatted = Some "42 MB"
}

let private createFolderItem (name: string) (path: string option) = {
    FileTree.createFolder name path FileItemIcon.Folder with
        Id = defaultArg path name
}

let private labels (items: ContextMenuItem list) =
    items |> List.map _.Label |> List.toArray

let private groupedLabels items =
    items
    |> List.map (fun item ->
        if defaultArg item.IsDivider false then
            "<divider>"
        else
            item.Label
    )
    |> List.toArray

let private rootNotesActionContextMenuItems =
    rootFolderContextMenuItems "notes" "Create new item in" "swt:fluent--note-add-24-regular"

Vitest.describe (
    "FileTreeContextMenu",
    fun () ->
        Vitest.test (
            "ARC create drafts include a basic identifier-named annotation table when supported",
            fun () ->
                let tableCapableKinds = [|
                    ArcFilesDiscriminate.Study
                    ArcFilesDiscriminate.Assay
                    ArcFilesDiscriminate.Run
                |]

                for kind in tableCapableKinds do
                    let config = arcCreateKinds |> List.find (fun config -> config.Kind = kind)
                    let identifier = $"Default {config.Label}"

                    let arcFile = ARCtrlHelper.ArcFileDefaults.createDefaultArcFile kind identifier
                    let tables = arcFile.Tables()
                    Vitest.expect(tables.Count).toBe (1)
                    let table = tables.[0]
                    Vitest.expect(table.Name).toBe ($"{identifier} Table")
                    Vitest.expect(table.ColumnCount).toBe (3)
                    Vitest.expect(table.RowCount).toBe (ARCtrlHelper.ArcFileDefaults.BasicAnnotationTableRowCount)
                    Vitest.expect(table.Headers.[0].ToString()).toBe ("Input [Source Name]")
                    Vitest.expect(table.Headers.[1].ToString()).toBe ("Protocol Uri")
                    Vitest.expect(table.Headers.[2].ToString()).toBe ("Output [Sample Name]")

                let workflow =
                    ARCtrlHelper.ArcFileDefaults.createDefaultArcFile ArcFilesDiscriminate.Workflow "Default Workflow"

                Vitest.expect(workflow.Tables().Count).toBe (0)
        )

        Vitest.test (
            "folder path actions import files into and reveal the folder",
            fun () ->
                let item = createFolderItem "AssayA" (Some "assays/AssayA")
                let menuItems = pathActionContextMenuItems (createConfig ()) item

                Vitest.expect(labels menuItems).toEqual ([| "Import files"; "Open Folder Location" |])
        )

        Vitest.test (
            "import files action targets the selected folder",
            fun () -> promise {
                let item = createFolderItem "AssayA" (Some "assays/AssayA")
                let mutable importedInto = None

                let config = {
                    createConfig () with
                        importExternalFiles =
                            fun path -> promise {
                                importedInto <- Some path
                                return Ok()
                            }
                }

                let menuItems = pathActionContextMenuItems config item

                let importItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Import files")

                Vitest.expect(importItem.Icon).toContain ("swt:rotate-180")
                importItem.OnClick()
                do! Promise.sleep 0

                Vitest.expect(importedInto).toEqual (Some "assays/AssayA")
            }
        )

        Vitest.test (
            "file path actions reveal the location and open with the default application",
            fun () ->
                let item = createFileItem "protocol.md" (Some "assays/AssayA/protocol.md")
                let menuItems = pathActionContextMenuItems (createConfig ()) item

                Vitest
                    .expect(labels menuItems)
                    .toEqual (
                        [|
                            "Open with Default Application"
                            "Open Folder Location"
                        |]
                    )
        )

        Vitest.test (
            "items without paths do not expose path actions",
            fun () ->
                let item = createFileItem "virtual.md" None
                let menuItems = pathActionContextMenuItems (createConfig ()) item

                Vitest.expect(menuItems.Length).toBe (0)
        )

        Vitest.test (
            "absolute copy path resolver combines the active ARC root with filetree paths",
            fun () ->
                let item = createFileItem "protocol.md" (Some "assays/AssayA/protocol.md")

                Vitest
                    .expect(tryGetAbsoluteItemPath (Some "C:\\arc-root") item)
                    .toEqual (Some "C:/arc-root/assays/AssayA/protocol.md")
        )

        Vitest.test (
            "relative copy path resolver keeps filetree paths relative",
            fun () ->
                let item = createFileItem "protocol.md" (Some "assays/AssayA/protocol.md")

                Vitest.expect(tryGetRelativeItemPath item).toEqual (Some "assays/AssayA/protocol.md")
        )

        Vitest.test (
            "relative copy path resolver ignores missing paths",
            fun () ->
                let item = createFileItem "virtual.md" None

                Vitest.expect(tryGetRelativeItemPath item).toEqual (None)
        )

        Vitest.test (
            "importRevealPath returns the target only for a completed import",
            fun () ->
                let reveal result = importRevealPath result "assays/A"

                Vitest.expect(reveal (Ok(Some ImportExternalFilesResult.Completed))).toEqual (Some "assays/A")
                Vitest.expect(reveal (Ok(Some ImportExternalFilesResult.Cancelled))).toEqual (None)
                Vitest.expect(reveal (Ok None)).toEqual (None)
                Vitest.expect(reveal (Error(exn "failed"))).toEqual (None)
        )

        Vitest.test (
            "open is only offered for files and folders with ARC metadata",
            fun () ->
                let hasOpen item =
                    openContextMenuItems (createContextMenuConfig ()) item
                    |> List.exists (fun menuItem -> menuItem.Label = "Open")

                Vitest.expect(hasOpen (createFolderItem "AssayA" (Some "assays/AssayA"))).toBe (true)
                Vitest.expect(hasOpen (createFileItem "protocol.md" (Some "assays/AssayA/protocol.md"))).toBe (true)
                Vitest.expect(hasOpen (createFolderItem "protocols" (Some "assays/AssayA/protocols"))).toBe (false)
                Vitest.expect(hasOpen (createFolderItem "data" (Some "data"))).toBe (false)
        )

        Vitest.test (
            "the Open item calls openItem with the folder",
            fun () ->
                let opened = ResizeArray<FileItem>()

                let config = {
                    createContextMenuConfig () with
                        openItem = opened.Add
                }

                let item = createFolderItem "AssayA" (Some "assays/AssayA")

                openContextMenuItems config item
                |> List.find (fun menuItem -> menuItem.Label = "Open")
                |> fun menuItem -> menuItem.OnClick()

                Vitest.expect(opened.Count).toBe (1)
                Vitest.expect(opened[0]).toEqual (item)
        )

        Vitest.test (
            "composed folder context menu is grouped with dividers",
            fun () ->
                let item = createFolderItem "AssayA" (Some "assays/AssayA")
                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                Vitest
                    .expect(groupedLabels menuItems)
                    .toEqual (
                        [|
                            "Open"
                            "Import files"
                            "Open Folder Location"
                            "<divider>"
                            "Refresh folder"
                            "<divider>"
                            "Copy Path"
                            "Copy Full Path"
                            "<divider>"
                            "New File"
                            "New Folder"
                            "<divider>"
                            "Add DataMap"
                            "<divider>"
                            "Add Study"
                            "Add Assay"
                            "Add Workflow"
                            "Add Run"
                            "Add Note"
                            "<divider>"
                            "Rename"
                            "Delete"
                        |]
                    )
        )

        Vitest.test (
            "new folder context menu action opens folder creation for the selected item",
            fun () ->
                let item = createFolderItem "AssayA" (Some "assays/AssayA")
                let mutable requestedCreate: (FileSystemItemKind * FileItem) option = None

                let menuItems =
                    fileSystemCreateContextMenuItems
                        (fun kind selectedItem -> requestedCreate <- Some(kind, selectedItem))
                        item

                let newFolderItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "New Folder")

                newFolderItem.OnClick()

                match requestedCreate with
                | Some(FileSystemItemKind.Folder, selectedItem) -> Vitest.expect(selectedItem.Id).toBe (item.Id)
                | Some(FileSystemItemKind.File, _) -> failwith "Expected folder creation to be requested."
                | None -> failwith "Expected new folder action to request creation."
        )

        Vitest.test (
            "root ARC name context menu exposes import, generic creation, and ARC add actions",
            fun () ->
                let item = createFolderItem "MyArc" (Some "")
                let menuItems = rootContextMenuItems (createContextMenuConfig ()) item

                Vitest
                    .expect(groupedLabels menuItems)
                    .toEqual (
                        [|
                            "Import files"
                            "<divider>"
                            "New File"
                            "New Folder"
                            "<divider>"
                            "Add Study"
                            "Add Assay"
                            "Add Workflow"
                            "Add Run"
                            "Add Note"
                        |]
                    )
        )

        Vitest.test (
            "root import action targets the empty ARC-relative path",
            fun () -> promise {
                let item = createFolderItem "MyArc" (Some "")
                let mutable importedInto = None

                let config = {
                    createContextMenuConfig () with
                        pathActionConfig = {
                            createConfig () with
                                importExternalFiles =
                                    fun path -> promise {
                                        importedInto <- Some path
                                        return Ok()
                                    }
                        }
                }

                let importItem =
                    rootContextMenuItems config item
                    |> List.find (fun menuItem -> menuItem.Label = "Import files")

                importItem.OnClick()
                do! Promise.sleep 0

                Vitest.expect(importedInto).toEqual (Some "")
            }
        )

        Vitest.test (
            "add note action requests note creation",
            fun () ->
                let item = createFolderItem "AssayA" (Some "assays/AssayA")
                let mutable didRequestNote = false

                let config = {
                    createContextMenuConfig () with
                        openNoteDraft = fun () -> didRequestNote <- true
                }

                let menuItems = createComposedContextMenuItems config item

                let addNoteItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Add Note")

                addNoteItem.OnClick()

                Vitest.expect(didRequestNote).toBeTruthy ()
        )

        Vitest.test (
            "supported ARC owner folders expose Add or Delete DataMap based on current content",
            fun () ->
                let owner = createFolderItem "AssayA" (Some "assays/AssayA")
                let mutable requestedParentInfo = None
                let mutable requestedDeleteItem = None

                let config = {
                    createContextMenuConfig () with
                        createDataMap = fun parentInfo -> requestedParentInfo <- Some parentInfo
                        requestDeleteItem = fun item -> requestedDeleteItem <- Some item
                }

                let addDataMap =
                    createComposedContextMenuItems config owner
                    |> List.find (fun item -> item.Label = "Add DataMap")

                addDataMap.OnClick()
                Vitest.expect(requestedParentInfo).toEqual (Some(DatamapParentInfo.create "AssayA" DataMapParent.Assay))

                let dataMapItem =
                    createFileItem DatamapParentInfo.DatamapFileName (Some "assays/AssayA/isa.datamap.xlsx")

                let ownerWithDataMap = {
                    owner with
                        Children = Some [ dataMapItem ]
                }

                let menuItemsWithDataMap = createComposedContextMenuItems config ownerWithDataMap
                let labelsWithDataMap = labels menuItemsWithDataMap

                Vitest.expect(labelsWithDataMap).not.toContain ("Add DataMap")
                Vitest.expect(labelsWithDataMap).toContain ("Delete DataMap")

                menuItemsWithDataMap
                |> List.find (fun item -> item.Label = "Delete DataMap")
                |> fun item -> item.OnClick()

                Vitest.expect(requestedDeleteItem |> Option.map _.Path).toEqual (Some dataMapItem.Path)
        )

        Vitest.test (
            "collapsed ARC owner folders use the full file tree to expose Delete DataMap",
            fun () ->
                let owner = {
                    createFolderItem "AssayA" (Some "assays/AssayA") with
                        Children = None
                }

                let dataMapItem =
                    createFileItem DatamapParentInfo.DatamapFileName (Some "assays/AssayA/isa.datamap.xlsx")

                let mutable requestedDeleteItem = None

                let config = {
                    createContextMenuConfig () with
                        tryFindDataMapItemByPath =
                            fun path ->
                                if PathHelpers.pathsEqual path dataMapItem.Path.Value then
                                    Some dataMapItem
                                else
                                    None
                        requestDeleteItem = fun item -> requestedDeleteItem <- Some item
                }

                let menuItems = createComposedContextMenuItems config owner
                let menuLabels = labels menuItems

                Vitest.expect(menuLabels).not.toContain ("Add DataMap")
                Vitest.expect(menuLabels).toContain ("Delete DataMap")

                menuItems
                |> List.find (fun item -> item.Label = "Delete DataMap")
                |> fun item -> item.OnClick()

                Vitest.expect(requestedDeleteItem |> Option.map _.Path).toEqual (Some dataMapItem.Path)
        )

        Vitest.test (
            "root notes folder row exposes add note action",
            fun () ->
                let item = createFolderItem "notes" (Some "notes")
                let mutable didRequestNote = false

                let menuItems =
                    rootNotesActionContextMenuItems (fun () -> didRequestNote <- true) item

                Vitest.expect(labels menuItems).toEqual ([| "Create new item in" |])
                Vitest.expect(menuItems.Head.Icon).toBe ("swt:fluent--note-add-24-regular")

                menuItems.Head.OnClick()

                Vitest.expect(didRequestNote).toBe (true)
        )

        Vitest.test (
            "root notes action is hidden for nested notes folders",
            fun () ->
                let item = createFolderItem "2026-06-15" (Some "notes/2026-06-15")
                let menuItems = rootNotesActionContextMenuItems ignore item

                Vitest.expect(menuItems.Length).toBe (0)
        )

        Vitest.test (
            "root notes folder context menu does not expose rename",
            fun () ->
                let item = createFolderItem "notes" (Some "notes")
                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                Vitest.expect(groupedLabels menuItems).not.toContain ("Rename")
        )

        Vitest.test (
            "native structural entity child folders do not expose rename or delete",
            fun () ->
                let protectedPaths = [
                    "assays/AssayA/dataset"
                    "assays/AssayA/protocols"
                    "studies/StudyA/protocols"
                    "studies/StudyA/resources"
                ]

                protectedPaths
                |> List.iter (fun path ->
                    let item = createFolderItem (PathHelpers.getNameFromPath path) (Some path)

                    let menuItemLabels =
                        createComposedContextMenuItems (createContextMenuConfig ()) item
                        |> groupedLabels

                    Vitest.expect(menuItemLabels).toContain ("New File")
                    Vitest.expect(menuItemLabels).toContain ("New Folder")
                    Vitest.expect(menuItemLabels).not.toContain ("Rename")
                    Vitest.expect(menuItemLabels).not.toContain ("Delete")
                )
        )

        Vitest.test (
            "new folder action on the ARC root requests root-level folder creation",
            fun () ->
                let item = createFolderItem "MyArc" (Some "")
                let mutable requestedCreate: (FileSystemItemKind * FileItem) option = None

                let menuItems =
                    fileSystemCreateContextMenuItems
                        (fun kind selectedItem -> requestedCreate <- Some(kind, selectedItem))
                        item

                let newFolderItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "New Folder")

                newFolderItem.OnClick()

                match requestedCreate with
                | Some(FileSystemItemKind.Folder, selectedItem) -> Vitest.expect(selectedItem.Path).toEqual (Some "")
                | Some(FileSystemItemKind.File, _) -> failwith "Expected root folder creation to be requested."
                | None -> failwith "Expected new folder action to request root creation."
        )

        Vitest.test (
            "ARC collection roots offer new files but not new folders",
            fun () ->
                [ "studies"; "assays"; "workflows"; "runs" ]
                |> List.iter (fun zone ->
                    let item = createFolderItem zone (Some zone)
                    let menuItems = fileSystemCreateContextMenuItems (fun _ _ -> ()) item

                    Vitest.expect(menuItems |> List.map _.Label |> Array.ofList).toEqual ([| "New File" |])
                )
        )

        Vitest.test (
            "items directly inside ARC collection roots: generic files and entity folders get rename and delete, canonical and README files don't",
            fun () ->
                let labelsFor item = [|
                    yield! renameContextMenuItems [] ignore item |> List.map _.Label
                    yield! deleteContextMenuItems [] ignore item |> List.map _.Label
                |]

                [ "studies", "isa.study.xlsx"; "assays", "isa.assay.xlsx" ]
                |> List.iter (fun (zone, canonicalFileName) ->
                    let fileItem name =
                        createFileItem name (Some $"{zone}/{name}")

                    Vitest.expect(fileItem "overview.md" |> labelsFor).toEqual ([| "Rename"; "Delete" |])
                    Vitest.expect(fileItem canonicalFileName |> labelsFor).toEqual ([||])
                    Vitest.expect(fileItem "README.md" |> labelsFor).toEqual ([||])

                    Vitest
                        .expect(createFolderItem "Entity1" (Some $"{zone}/Entity1") |> labelsFor)
                        .toEqual ([| "Rename"; "Delete" |])
                )
        )

        Vitest.test (
            "composed file context menu is grouped with open, copy, git, and ARC actions",
            fun () ->
                let item = createFileItem "protocol.md" (Some "assays/AssayA/protocol.md")
                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                Vitest
                    .expect(groupedLabels menuItems)
                    .toEqual (
                        [|
                            "Open"
                            "Open with Default Application"
                            "Open Folder Location"
                            "<divider>"
                            "Copy Path"
                            "Copy Full Path"
                            "<divider>"
                            "Mark Git LFS"
                            "Git LFS: not marked"
                            "<divider>"
                            "Rename"
                            "Delete"
                        |]
                    )
        )

        Vitest.test (
            "composed LFS pointer menu enables download and disables freeing the local copy",
            fun () -> promise {
                let item = createLfsFileItem "pointer.bin" "data/pointer.bin" false true
                let mutable downloadedPath = None

                let config = {
                    createContextMenuConfig () with
                        runDownloadLfsFile =
                            fun path -> promise {
                                downloadedPath <- Some path
                                return Ok()
                            }
                }

                let menuItems = createComposedContextMenuItems config item

                Vitest.expect(groupedLabels menuItems).toContain ("Download LFS file")

                let downloadItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Download LFS file")

                let freeItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Free local LFS copy")

                Vitest.expect(downloadItem.Disabled).toEqual (None)
                Vitest.expect(freeItem.Disabled).toEqual (Some true)

                downloadItem.OnClick()
                do! Promise.sleep 0

                Vitest.expect(downloadedPath).toEqual (Some "data/pointer.bin")
            }
        )

        Vitest.test (
            "composed downloaded LFS menu disables download and enables freeing the local copy",
            fun () -> promise {
                let item = createLfsFileItem "downloaded.bin" "data/downloaded.bin" true false
                let mutable freedPath = None

                let config = {
                    createContextMenuConfig () with
                        runFreeLocalLfsCopy =
                            fun path -> promise {
                                freedPath <- Some path
                                return Ok()
                            }
                }

                let menuItems = createComposedContextMenuItems config item

                let downloadItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Download LFS file")

                let freeItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Free local LFS copy")

                Vitest.expect(downloadItem.Disabled).toEqual (Some true)
                Vitest.expect(freeItem.Disabled).toEqual (None)

                freeItem.OnClick()
                do! Promise.sleep 0

                Vitest.expect(freedPath).toEqual (Some "data/downloaded.bin")
            }
        )

        Vitest.test (
            "composed menu disables marking isa metadata files as Git LFS",
            fun () -> promise {
                let item = createFileItem "isa.study.xlsx" (Some "studies/study_01/isa.study.xlsx")

                let mutable toggledPath = None

                let config = {
                    createContextMenuConfig () with
                        runToggleLfsMark =
                            fun path _ -> promise {
                                toggledPath <- Some path
                                return Ok()
                            }
                }

                let menuItems = createComposedContextMenuItems config item

                let markItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Mark Git LFS")

                Vitest.expect(markItem.Disabled).toEqual (Some true)

                markItem.OnClick()
                do! Promise.sleep 0

                Vitest.expect(toggledPath).toEqual (None)
            }
        )

        Vitest.test (
            "composed menu keeps marking ordinary files as Git LFS enabled",
            fun () ->
                let item = createFileItem "raw.bin" (Some "assays/AssayA/raw.bin")
                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                let markItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Mark Git LFS")

                Vitest.expect(markItem.Disabled).toEqual (None)
        )

        Vitest.test (
            "composed menu disables unmarking dataset files from Git LFS",
            fun () ->
                let item = createLfsFileItem "raw.bin" "assays/AssayA/dataset/raw.bin" true false

                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                let unmarkItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Unmark Git LFS")

                Vitest.expect(unmarkItem.Disabled).toEqual (Some true)
        )

        Vitest.test (
            "composed menu disables unmarking LFS files larger than 25 MB",
            fun () ->
                let item = {
                    createLfsFileItem "large.bin" "data/large.bin" true false with
                        Size = Some(26L * 1024L * 1024L)
                }

                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                let unmarkItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Unmark Git LFS")

                Vitest.expect(unmarkItem.Disabled).toEqual (Some true)
        )

        Vitest.test (
            "composed menu keeps unmarking small non-dataset LFS files enabled",
            fun () ->
                let item = {
                    createLfsFileItem "small.bin" "data/small.bin" true false with
                        Size = Some 1024L
                }

                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item

                let unmarkItem =
                    menuItems |> List.find (fun menuItem -> menuItem.Label = "Unmark Git LFS")

                Vitest.expect(unmarkItem.Disabled).toEqual (None)
        )

        Vitest.test (
            "rename and delete are disabled while an LFS action runs on the file",
            fun () ->
                let item = {
                    createLfsFileItem "busy.bin" "data/busy.bin" true false with
                        LfsActivity = Some "Freeing"
                }

                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item
                let renameItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Rename")
                let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                Vitest.expect(renameItem.Disabled).toEqual (Some true)
                Vitest.expect(deleteItem.Disabled).toEqual (Some true)
        )

        Vitest.test (
            "rename and delete stay enabled for an idle LFS file",
            fun () ->
                let item = createLfsFileItem "idle.bin" "data/idle.bin" true false
                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item
                let renameItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Rename")
                let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                Vitest.expect(renameItem.Disabled).toEqual (None)
                Vitest.expect(deleteItem.Disabled).toEqual (None)
        )

        Vitest.test (
            "rename and delete are disabled on every folder above a file with a running LFS action",
            fun () ->
                let config = {
                    createContextMenuConfig () with
                        lfsActivePaths = [ "Data/raw/busy.bin" ]
                }

                for folder in
                    [
                        createFolderItem "data" (Some "data")
                        createFolderItem "raw" (Some "data/raw")
                    ] do
                    let menuItems = createComposedContextMenuItems config folder
                    let renameItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Rename")
                    let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                    Vitest.expect(renameItem.Disabled).toEqual (Some true)
                    Vitest.expect(deleteItem.Disabled).toEqual (Some true)
                    Vitest.expect(isLockedByLfsActivity config.lfsActivePaths folder).toBe (true)
        )

        Vitest.test (
            "rename and delete stay enabled on sibling and unrelated folders of a file with a running LFS action",
            fun () ->
                let config = {
                    createContextMenuConfig () with
                        lfsActivePaths = [ "data/raw/busy.bin" ]
                }

                let folders = [
                    createFolderItem "other" (Some "data/other")
                    createFolderItem "raw-copy" (Some "data/raw-copy")
                    createFolderItem "results" (Some "results")
                ]

                for folder in folders do
                    let menuItems = createComposedContextMenuItems config folder
                    let renameItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Rename")
                    let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                    Vitest.expect(renameItem.Disabled).toEqual (None)
                    Vitest.expect(deleteItem.Disabled).toEqual (None)
                    Vitest.expect(isLockedByLfsActivity config.lfsActivePaths folder).toBe (false)
        )

        Vitest.test (
            "delete is disabled on a canonical entity workbook while an LFS action runs inside its entity folder",
            fun () ->
                let cases = [
                    createFileItem "isa.assay.xlsx" (Some "assays/A/isa.assay.xlsx"), "assays/A/dataset/big.bin"
                    createFileItem "isa.study.xlsx" (Some "studies/S/isa.study.xlsx"), "Studies/S/resources/big.bin"
                ]

                for workbook, busyPath in cases do
                    let config = {
                        createContextMenuConfig () with
                            lfsActivePaths = [ busyPath ]
                    }

                    let menuItems = createComposedContextMenuItems config workbook
                    let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                    Vitest.expect(deleteItem.Disabled).toEqual (Some true)
                    Vitest.expect(isLockedByLfsActivity config.lfsActivePaths workbook).toBe (true)
        )

        Vitest.test (
            "delete stays enabled on a canonical entity workbook while an LFS action runs in another entity folder",
            fun () ->
                let config = {
                    createContextMenuConfig () with
                        lfsActivePaths = [ "assays/B/dataset/big.bin"; "assays/A2/dataset/big.bin" ]
                }

                let workbook = createFileItem "isa.assay.xlsx" (Some "assays/A/isa.assay.xlsx")
                let menuItems = createComposedContextMenuItems config workbook
                let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                Vitest.expect(deleteItem.Disabled).toEqual (None)
                Vitest.expect(isLockedByLfsActivity config.lfsActivePaths workbook).toBe (false)
        )

        Vitest.test (
            "delete action is styled as destructive ARC action",
            fun () ->
                let item = createFileItem "protocol.md" (Some "assays/AssayA/protocol.md")
                let menuItems = createComposedContextMenuItems (createContextMenuConfig ()) item
                let deleteItem = menuItems |> List.find (fun menuItem -> menuItem.Label = "Delete")

                Vitest.expect(deleteItem.ClassName).toEqual (Some "swt:text-error")
        )
)
