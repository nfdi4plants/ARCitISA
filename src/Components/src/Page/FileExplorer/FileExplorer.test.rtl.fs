module internal Swate.Components.Page.FileExplorer.Tests

open Fable.Core
open Fable.Core.JsInterop
open Vitest
open Swate.Components.Page.FileExplorer
open Swate.Components.Page.FileExplorer.Types

let private file (id: string) : FileItem = {
    FileTree.createFile id None FileItemIcon.Document with
        Id = id
}

let private folder (id: string) (children: FileItem list option) : FileItem = {
    FileTree.createFolder id None FileItemIcon.Folder with
        Id = id
        Children = children
}

let private tree = [
    folder "src" (Some [ file "a.txt"; folder "sub" (Some [ file "deep.txt" ]) ])
    folder "Empty Folder" (Some [])
    folder "Lazy Folder" None
]

let private treeItem (id: string) =
    RTL.screen.getByRole ("treeitem", ByRoleOptions(name = Text id))

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.describe (
    "FileExplorer",
    fun () ->
        Vitest.test (
            "reports the tree position of expanded rows",
            fun () ->
                RTL.render (FileExplorer.FileExplorer(initialItems = tree, expandedIds = set [ "src"; "sub" ]))
                |> ignore

                Vitest.expect((RTL.screen.getByRole "tree").getAttribute "role").toBe "tree"
                let deep = treeItem "deep.txt"
                Vitest.expect(deep.getAttribute "aria-level").toBe "3"
                Vitest.expect(deep.getAttribute "aria-setsize").toBe "1"
                Vitest.expect(deep.getAttribute "aria-posinset").toBe "1"
                let sub = treeItem "sub"
                Vitest.expect(sub.getAttribute "aria-level").toBe "2"
                Vitest.expect(sub.getAttribute "aria-setsize").toBe "2"
                Vitest.expect(sub.getAttribute "aria-posinset").toBe "2"
                Vitest.expect(sub.getAttribute "aria-expanded").toBe "true"
        )

        Vitest.test (
            "sets aria-expanded only on directories that can expand",
            fun () ->
                RTL.render (FileExplorer.FileExplorer(initialItems = tree)) |> ignore

                Vitest.expect((treeItem "src").getAttribute "aria-expanded").toBe "false"
                Vitest.expect((treeItem "Lazy Folder").getAttribute "aria-expanded").toBe "false"
                Vitest.expect((treeItem "Empty Folder").hasAttribute "aria-expanded").toBe false
        )

        Vitest.test (
            "selects a file on click and routes it to onItemClick",
            fun () ->
                let clicked = ResizeArray<string>()

                RTL.render (
                    FileExplorer.FileExplorer(
                        initialItems = tree,
                        expandedIds = set [ "src" ],
                        onItemClick = (fun item -> clicked.Add item.Id)
                    )
                )
                |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "a.txt")
                Vitest.expect((treeItem "a.txt").getAttribute "aria-selected").toBe "true"
                Vitest.expect(clicked.ToArray()).toEqual [| "a.txt" |]
        )

        Vitest.test (
            "reports the new expanded ids when a directory row is clicked",
            fun () ->
                let changes = ResizeArray<Set<string>>()

                RTL.render (
                    FileExplorer.FileExplorer(
                        initialItems = tree,
                        expandedIds = set [ "src" ],
                        onExpandedIdsChange = (fun ids -> changes.Add ids)
                    )
                )
                |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "sub")
                Vitest.expect(changes.Count).toBe 1
                Vitest.expect(changes.[0] |> Set.toArray).toEqual [| "src"; "sub" |]
        )

        Vitest.test (
            "offers Refresh folder for directories and calls the callback",
            fun () ->
                let refreshed = ResizeArray<string>()

                RTL.render (
                    FileExplorer.FileExplorer(
                        initialItems = tree,
                        onRefreshDirectory = (fun item -> refreshed.Add item.Id)
                    )
                )
                |> ignore

                RTL.fireEvent.custom (
                    "contextMenu",
                    RTL.screen.getByText "Empty Folder",
                    {| clientX = 10; clientY = 10 |}
                )

                RTL.fireEvent.click (RTL.screen.getByText "Refresh folder")
                Vitest.expect(refreshed.ToArray()).toEqual [| "Empty Folder" |]
        )
)
