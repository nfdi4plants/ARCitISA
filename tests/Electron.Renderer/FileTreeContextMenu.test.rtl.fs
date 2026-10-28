module ElectronRenderer.FileTreeContextMenuRtlTests

open Renderer.Components.LeftSidebar.FileExplorer.FileTreeContextMenu
open Renderer.Components.LeftSidebar.FileExplorer.Types
open Swate.Components.Page.FileExplorer
open Swate.Components.Page.FileExplorer.Types
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private config (refreshed: ResizeArray<string>) : ContextMenuConfig = {
    openItem = ignore
    arcRootPath = Some "C:/arc-root"
    openCreateModal = ignore
    openNoteDraft = ignore
    createDataMap = ignore
    tryFindDataMapItemByPath = fun _ -> None
    openFileSystemCreateModal = fun _ _ -> ()
    requestRenameItem = ignore
    requestDeleteItem = ignore
    pathActionConfig = {
        openPathInFileExplorer = fun _ -> promise { return Ok() }
        openPathWithDefaultApplication = fun _ -> promise { return Ok() }
        importExternalPaths = fun _ _ -> promise { return Ok() }
        enqueueError = ignore
    }
    enqueueError = ignore
    runToggleLfsMark = fun _ _ -> promise { return Ok() }
    runDownloadLfsFile = fun _ -> promise { return Ok() }
    runFreeLocalLfsCopy = fun _ -> promise { return Ok() }
    refreshDirectory = fun item -> refreshed.Add(defaultArg item.Path "")
    lfsActivePaths = []
}

Vitest.test (
    "the file tree context menu offers Refresh folder for folders and calls refreshDirectory",
    fun () ->
        let refreshed = ResizeArray<string>()

        let items = [
            {
                FileTree.createFolder "data" (Some "data") FileItemIcon.Folder with
                    Id = "data"
                    Children = None
            }
            {
                FileTree.createFile "note.txt" (Some "note.txt") FileItemIcon.Document with
                    Id = "note.txt"
            }
        ]

        RTL.render (
            FileExplorer.FileExplorer(
                initialItems = items,
                onContextMenu = createContextMenuItems (config refreshed) None,
                includeDefaultContextMenuItems = false
            )
        )
        |> ignore

        RTL.fireEvent.custom ("contextMenu", RTL.screen.getByText "note.txt", {| clientX = 10; clientY = 10 |})
        Vitest.expect((RTL.screen.queryByText "Refresh folder").IsNone).toBe true
        RTL.cleanup ()

        RTL.render (
            FileExplorer.FileExplorer(
                initialItems = items,
                onContextMenu = createContextMenuItems (config refreshed) None,
                includeDefaultContextMenuItems = false
            )
        )
        |> ignore

        RTL.fireEvent.custom ("contextMenu", RTL.screen.getByText "data", {| clientX = 10; clientY = 10 |})
        RTL.fireEvent.click (RTL.screen.getByText "Refresh folder")
        Vitest.expect(refreshed.ToArray()).toEqual [| "data" |]
)
