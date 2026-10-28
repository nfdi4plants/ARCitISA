module internal Swate.Components.Tests.FileExplorerGitLfsHelper

open Vitest
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Page.FileExplorer.FileExplorerGitLfsHelper

let private lfsFile (isPointer: bool) : FileItem = {
    FileTree.createFile "data.bin" (Some "data.bin") FileItemIcon.Document with
        IsLFS = Some true
        IsLFSPointer = Some isPointer
        Downloaded = Some(not isPointer)
}

Vitest.describe (
    "lfsPillAction",
    fun () ->
        Vitest.test (
            "offers the download action for a pointer file",
            fun () ->
                let action = lfsPillAction (lfsFile true) (Some ignore)
                Vitest.expect(action.IsSome).toBe true
                Vitest.expect(action.Value.Label).toBe "Download LFS file"
        )

        Vitest.test (
            "offers no action for a file with a local copy",
            fun () ->
                let action = lfsPillAction (lfsFile false) (Some ignore)
                Vitest.expect(action.IsNone).toBe true
        )
)

let private folder: FileItem =
    FileTree.createFolder "data" (Some "data") FileItemIcon.Folder

let private isDisabled (item: ContextMenuItem) = item.Disabled = Some true

Vitest.describe (
    "folderContextMenuItems",
    fun () ->
        Vitest.test (
            "offers nothing for a file",
            fun () ->
                let items = folderContextMenuItems (lfsFile true) false 1 1 ignore ignore
                Vitest.expect(items.Length).toBe 0
        )

        Vitest.test (
            "offers nothing for a folder without an LFS file below it",
            fun () ->
                let items = folderContextMenuItems folder false 0 0 ignore ignore
                Vitest.expect(items.Length).toBe 0
        )

        Vitest.test (
            "enables each entry only when its count is above zero",
            fun () ->
                let onlyDownloads = folderContextMenuItems folder false 2 0 ignore ignore
                Vitest.expect(onlyDownloads.Length).toBe 2
                Vitest.expect(isDisabled onlyDownloads.[0]).toBe false
                Vitest.expect(isDisabled onlyDownloads.[1]).toBe true

                let onlyCopies = folderContextMenuItems folder false 0 3 ignore ignore
                Vitest.expect(onlyCopies.Length).toBe 2
                Vitest.expect(isDisabled onlyCopies.[0]).toBe true
                Vitest.expect(isDisabled onlyCopies.[1]).toBe false
        )

        Vitest.test (
            "passes the folder to the handler of the entry that was clicked",
            fun () ->
                let downloaded = ResizeArray<string>()
                let freed = ResizeArray<string>()

                let items =
                    folderContextMenuItems
                        folder
                        false
                        1
                        1
                        (fun item -> downloaded.Add item.Name)
                        (fun item -> freed.Add item.Name)

                items.[0].OnClick()
                Vitest.expect(downloaded.Count).toBe 1
                Vitest.expect(freed.Count).toBe 0

                items.[1].OnClick()
                Vitest.expect(downloaded.Count).toBe 1
                Vitest.expect(freed.Count).toBe 1
                Vitest.expect(freed.[0]).toBe "data"
        )

        Vitest.test (
            "disables both entries while an action runs on the folder",
            fun () ->
                let busyFolder = {
                    folder with
                        LfsActivity = Some "Downloading"
                }

                let items = folderContextMenuItems busyFolder false 2 3 ignore ignore
                Vitest.expect(items.Length).toBe 2
                Vitest.expect(items |> List.forall isDisabled).toBe true
        )

        Vitest.test (
            "disables both entries when the caller reports the folder as busy",
            fun () ->
                let items = folderContextMenuItems folder true 2 3 ignore ignore
                Vitest.expect(items.Length).toBe 2
                Vitest.expect(items |> List.forall isDisabled).toBe true
        )
)
