module ElectronRenderer.FileTreeRenameWorkflowTests

open Browser.Dom
open Browser.Types
open Feliz
open ARCtrl
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameHelper
open Renderer.Components.LeftSidebar.FileExplorer.Types
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Vitest
open Renderer

module RenameWorkflow = Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameWorkflow
module FileTreeContextMenu = Renderer.Components.LeftSidebar.FileExplorer.FileTreeContextMenu

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for rename workflow."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

let private renderToBody (element: ReactElement) = promise {
    let container = document.createElement ("div") :?> HTMLDivElement
    document.body.appendChild container |> ignore
    let root = ReactDOM.createRoot container
    root.render element
    do! Promise.sleep 0

    return
        container,
        (fun () ->
            root.unmount ()
            container.remove ()
        )
}

let private createFileItem (name: string) (path: string) = {
    FileTree.createFile name (Some path) FileItemIcon.Document with
        Id = path
}

let private createFolderItem (name: string) (path: string) = {
    FileTree.createFolder name (Some path) FileItemIcon.Folder with
        Id = path
}

Vitest.afterEach (fun () -> document.body.innerHTML <- "")

Vitest.describe (
    "FileTreeRenameHelper",
    fun () ->
        Vitest.test (
            "tryBuildRenameDraft rejects canonical ARC files",
            fun () ->
                let item = createFileItem "isa.assay.xlsx" "assays/OldAssay/isa.assay.xlsx"

                match tryBuildRenameDraft item with
                | Ok _ -> failwith "Expected canonical ARC files to be non-renameable."
                | Error _ -> ()
        )

        Vitest.test (
            "tryBuildRenameDraft rejects native structural entity child folders",
            fun () ->
                let protectedChildFolderPaths = [
                    "assays/AssayA/dataset"
                    "assays/AssayA/protocols"
                    "studies/StudyA/protocols"
                    "studies/StudyA/resources"
                ]

                protectedChildFolderPaths
                |> List.iter (fun path ->
                    let item = createFolderItem (PathHelpers.getNameFromPath path) path

                    match tryBuildRenameDraft item with
                    | Ok _ -> failwith $"Expected native structural child folder '{path}' to be non-renameable."
                    | Error _ -> ()
                )
        )

        Vitest.test (
            "tryRemapPathPrefix remaps descendants under renamed source prefixes",
            fun () ->
                let remapped =
                    PathHelpers.tryRemapPathPrefix
                        "assays/OldAssay"
                        "assays/NewAssay"
                        "assays/OldAssay/notes/protocol.md"

                Vitest.expect(remapped).toEqual (Some "assays/NewAssay/notes/protocol.md")
        )

        Vitest.test (
            "tryRemapPathPrefix does not remap sibling paths that only share a prefix",
            fun () ->
                let remapped =
                    PathHelpers.tryRemapPathPrefix
                        "assays/OldAssay"
                        "assays/NewAssay"
                        "assays/OldAssay2/notes/protocol.md"

                Vitest.expect(remapped).toEqual (None)
        )

)

let private getRenameMenuItems (item: FileItem) =
    FileTreeContextMenu.renameContextMenuItems
        Renderer.Context.LfsActivityContext.LfsActivePathIndex.empty
        (fun _ -> ())
        item

let private expectRenameMenuVisibility expectedCount item =
    let menuItems = getRenameMenuItems item
    Vitest.expect(menuItems.Length).toBe (expectedCount)

    if expectedCount = 1 then
        Vitest.expect(menuItems.[0].Label).toBe ("Rename")
        Vitest.expect(menuItems.[0].Icon).toBe ("swt:fluent--edit-24-regular")

let private buildRenameDraftOrFail item =
    match tryBuildRenameDraft item with
    | Ok draft -> draft
    | Error error -> failwith error

type private RenameWorkflowProbe = {
    mutable RenameRequest: RenamePathRequest option
    mutable RenamedSelection: ArcSelection option
    mutable Closed: bool
    mutable GitStatusRefreshCount: int
    mutable ReloadedPreviewPath: string option
}

let private createRenameWorkflowProbe () = {
    RenameRequest = None
    RenamedSelection = None
    Closed = false
    GitStatusRefreshCount = 0
    ReloadedPreviewPath = None
}

let private createConfirmRenameConfig
    renameDraft
    selectedTreePath
    pageState
    (probe: RenameWorkflowProbe)
    : RenameWorkflow.ConfirmRenameConfig =
    {
        pendingRenameDraft = Some renameDraft
        selectedTreePath = Some selectedTreePath
        pageState = pageState
        closeRenameModal = fun () -> probe.Closed <- true
        setIsRenaming = ignore
        setSelection = fun selection -> probe.RenamedSelection <- Some selection
        refreshGitStatus = fun () -> probe.GitStatusRefreshCount <- probe.GitStatusRefreshCount + 1
        reloadPreviewByPath =
            fun path -> promise {
                probe.ReloadedPreviewPath <- Some path
                return Ok()
            }
        renamePath =
            fun request -> promise {
                probe.RenameRequest <- Some request
                return Ok()
            }
        enqueueError = fun _ -> failwith "Did not expect rename error."
    }

let private expectRenameRequest probe expectedRelativePath expectedNewName =
    match probe.RenameRequest with
    | None -> failwith "Expected renamePath to be dispatched."
    | Some request ->
        Vitest.expect(request.relativePath).toBe (expectedRelativePath)
        Vitest.expect(request.newName).toBe (expectedNewName)

Vitest.describe (
    "FileTreeRenameWorkflow",
    fun () ->

        Vitest.test (
            "rename context menu item is shown for assay, study, workflow, and run entity folders",
            fun () ->
                let entityFolderPaths = [
                    "assays/AssayA"
                    "studies/StudyA"
                    "workflows/WorkflowA"
                    "runs/RunA"
                ]

                entityFolderPaths
                |> List.iter (fun path ->
                    let item = createFolderItem (PathHelpers.getNameFromPath path) path
                    expectRenameMenuVisibility 1 item
                )
        )

        Vitest.test (
            "rename context menu item is hidden for canonical entity and datamap ARC files",
            fun () ->
                let canonicalFilePaths = [
                    "assays/AssayA/isa.assay.xlsx"
                    "assays/AssayA/isa.datamap.xlsx"
                    "studies/StudyA/isa.study.xlsx"
                    "studies/StudyA/isa.datamap.xlsx"
                    "workflows/WorkflowA/isa.workflow.xlsx"
                    "workflows/WorkflowA/isa.datamap.xlsx"
                    "runs/RunA/isa.run.xlsx"
                    "runs/RunA/isa.datamap.xlsx"
                ]

                canonicalFilePaths
                |> List.iter (fun path ->
                    let item = createFileItem (PathHelpers.getNameFromPath path) path
                    expectRenameMenuVisibility 0 item
                )
        )

        Vitest.test (
            "rename context menu item is hidden for add-zone root folders",
            fun () ->
                let addZoneRootPaths = [ "assays"; "studies"; "workflows"; "runs" ]

                addZoneRootPaths
                |> List.iter (fun path ->
                    let item = createFolderItem path path
                    expectRenameMenuVisibility 0 item
                )
        )

        Vitest.test (
            "rename context menu item is hidden for native structural entity child folders",
            fun () ->
                let protectedChildFolderPaths = [
                    "assays/AssayA/dataset"
                    "assays/AssayA/protocols"
                    "studies/StudyA/protocols"
                    "studies/StudyA/resources"
                ]

                protectedChildFolderPaths
                |> List.iter (fun path ->
                    let item = createFolderItem (PathHelpers.getNameFromPath path) path
                    expectRenameMenuVisibility 0 item
                )
        )

        Vitest.test (
            "rename context menu item is shown for safe generic descendants",
            fun () ->
                let item = createFileItem "custom.txt" "studies/MyStudy/notes/custom.txt"
                expectRenameMenuVisibility 1 item
        )

        Vitest.test (
            "rename context menu item is shown for safe root-level generic paths",
            fun () ->
                let item = createFileItem "test.fsx" "test.fsx"
                expectRenameMenuVisibility 1 item
        )

        Vitest.test (
            "rename context menu item is hidden for unsafe generic paths",
            fun () ->
                let unsafePaths = [
                    "assays/AssayA/isa.assay.xlsx"
                    "assays/AssayA/readme.md"
                    "assays/AssayA/.git/config"
                ]

                unsafePaths
                |> List.iter (fun path ->
                    let item = createFileItem (PathHelpers.getNameFromPath path) path
                    expectRenameMenuVisibility 0 item
                )
        )

        Vitest.test (
            "rename context menu action requests a rename draft for renameable items",
            fun () ->
                let item = createFolderItem "OldAssay" "assays/OldAssay"
                let mutable pendingRenameDraft: ArcRenameDraft option = None

                let requestRenameItem =
                    RenameWorkflow.requestRenameItem (fun draft -> pendingRenameDraft <- draft) ignore

                let menuItems =
                    FileTreeContextMenu.renameContextMenuItems
                        Renderer.Context.LfsActivityContext.LfsActivePathIndex.empty
                        requestRenameItem
                        item

                Vitest.expect(menuItems.Length).toBe (1)
                menuItems.[0].OnClick()
                Vitest.expect(pendingRenameDraft.IsSome).toBe (true)
        )

        Vitest.test (
            "rename request surfaces validation errors instead of silently swallowing",
            fun () ->
                let item = createFolderItem "assays" "assays"
                let mutable pendingRenameDraft: ArcRenameDraft option = None
                let mutable errorCount = 0

                RenameWorkflow.requestRenameItem
                    (fun draft -> pendingRenameDraft <- draft)
                    (fun _ -> errorCount <- errorCount + 1)
                    item

                Vitest.expect(pendingRenameDraft).toEqual (None)
                Vitest.expect(errorCount).toBe (1)
        )

        Vitest.test (
            "inline rename button dispatches for assay, study, workflow, and run entity folders",
            fun () -> promise {
                let entityFolderPaths = [
                    "assays/AssayA"
                    "studies/StudyA"
                    "workflows/WorkflowA"
                    "runs/RunA"
                ]

                let items =
                    entityFolderPaths
                    |> List.map (fun path -> createFolderItem (PathHelpers.getNameFromPath path) path)

                let mutable renamedPaths: string list = []

                let onRenameItem (item: FileItem) =
                    let path = item.Path |> Option.defaultValue ""
                    renamedPaths <- path :: renamedPaths

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.FileExplorer.FileExplorer.FileExplorer(
                            initialItems = items,
                            getItemActions =
                                FileTreeContextMenu.renameContextMenuItems
                                    Renderer.Context.LfsActivityContext.LfsActivePathIndex.empty
                                    onRenameItem
                        )
                    )

                try
                    entityFolderPaths
                    |> List.iter (fun path ->
                        let itemName = PathHelpers.getNameFromPath path
                        let buttonSelector = $"button[aria-label='Rename {itemName}']"
                        let renameButton = container.querySelector buttonSelector

                        Vitest.expect(renameButton).not.toBeNull ()
                        (renameButton :?> HTMLElement).click ()
                    )

                    do! Promise.sleep 0

                    Vitest.expect(renamedPaths |> List.rev).toEqual (entityFolderPaths)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "confirmRenameItem dispatches renamePath, remaps active selection, and refreshes git status",
            fun () -> promise {
                let renameDraft =
                    createFolderItem "OldAssay" "assays/OldAssay" |> buildRenameDraftOrFail

                let probe = createRenameWorkflowProbe ()

                let config =
                    createConfirmRenameConfig
                        renameDraft
                        "assays/OldAssay/notes/protocol.md"
                        (Some(Renderer.Types.PageState.ArcFilePage(ArcFiles.Assay(ArcAssay.init "OldAssay"), None)))
                        probe

                RenameWorkflow.confirmRenameItem config "NewAssay"
                do! waitUntil ((fun () -> probe.RenameRequest.IsSome && probe.Closed), 50)

                expectRenameRequest probe "assays/OldAssay" "NewAssay"
                Vitest.expect(probe.RenamedSelection.IsSome).toBe (true)
                Vitest.expect(probe.RenamedSelection.Value.TreePath).toEqual (Some "assays/NewAssay/notes/protocol.md")
                Vitest.expect(probe.ReloadedPreviewPath).toEqual (Some "assays/NewAssay/isa.assay.xlsx")
                Vitest.expect(probe.GitStatusRefreshCount).toBe (1)
            }
        )

        Vitest.test (
            "renaming an entity folder moves the selection along when its workbook is selected",
            fun () -> promise {
                let renameDraft =
                    createFolderItem "OldAssay" "assays/OldAssay" |> buildRenameDraftOrFail

                let probe = createRenameWorkflowProbe ()

                let config =
                    createConfirmRenameConfig
                        renameDraft
                        "assays/OldAssay/isa.assay.xlsx"
                        (Some(Renderer.Types.PageState.ArcFilePage(ArcFiles.Assay(ArcAssay.init "OldAssay"), None)))
                        probe

                RenameWorkflow.confirmRenameItem config "NewAssay"
                do! waitUntil ((fun () -> probe.RenameRequest.IsSome && probe.Closed), 50)

                Vitest.expect(probe.RenamedSelection.Value.TreePath).toEqual (Some "assays/NewAssay/isa.assay.xlsx")
            }
        )

        Vitest.test (
            "confirmRenameItem dispatches generic renamePath and remaps generic file selection",
            fun () -> promise {
                let renameDraft =
                    createFileItem "old.txt" "assays/AssayA/protocols/old.txt"
                    |> buildRenameDraftOrFail

                let probe = createRenameWorkflowProbe ()

                let config =
                    createConfirmRenameConfig
                        renameDraft
                        "assays/AssayA/protocols/old.txt"
                        (Some(Renderer.Types.PageState.TextPage "old content"))
                        probe

                RenameWorkflow.confirmRenameItem config "new.txt"
                do! waitUntil ((fun () -> probe.RenameRequest.IsSome && probe.Closed), 50)

                expectRenameRequest probe "assays/AssayA/protocols/old.txt" "new.txt"
                Vitest.expect(probe.RenamedSelection.IsSome).toBe (true)
                Vitest.expect(probe.RenamedSelection.Value.TreePath).toEqual (Some "assays/AssayA/protocols/new.txt")
                Vitest.expect(probe.GitStatusRefreshCount).toBe (1)
            }
        )
)
