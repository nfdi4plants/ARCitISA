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
