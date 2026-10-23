module internal Swate.Components.Tests.FileExplorerLogic

open Vitest
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
    folder
        "src"
        (Some [
            file "a.txt"
            folder "sub" (Some [ file "deep.txt" ])
            folder "lazy" None
        ])
    file "z.txt"
]

let private ids (rows: FileExplorerLogic.VisibleRow[]) = rows |> Array.map _.Id

Vitest.describe (
    "flattenVisibleRows",
    fun () ->
        Vitest.test (
            "reports depth, set size and position in set in display order",
            fun () ->
                let rows = FileExplorerLogic.flattenVisibleRows (set [ "src"; "sub"; "lazy" ]) tree

                Vitest.expect(ids rows).toEqual [| "src"; "a.txt"; "sub"; "deep.txt"; "lazy"; "z.txt" |]
                Vitest.expect(rows |> Array.map _.Depth).toEqual [| 0; 1; 1; 2; 1; 0 |]
                Vitest.expect(rows |> Array.map _.SetSize).toEqual [| 2; 3; 3; 1; 3; 2 |]
                Vitest.expect(rows |> Array.map _.PosInSet).toEqual [| 1; 1; 2; 1; 3; 2 |]
        )

        Vitest.test (
            "hides the children of collapsed directories",
            fun () ->
                let rows = FileExplorerLogic.flattenVisibleRows (set [ "src" ]) tree

                Vitest.expect(ids rows).toEqual [| "src"; "a.txt"; "sub"; "lazy"; "z.txt" |]
                Vitest.expect(ids (FileExplorerLogic.flattenVisibleRows Set.empty tree)).toEqual [| "src"; "z.txt" |]
        )

        Vitest.test (
            "hides the children of an expanded directory that has no loaded children",
            fun () ->
                let rows = FileExplorerLogic.flattenVisibleRows (set [ "src"; "lazy" ]) tree

                Vitest.expect(ids rows).toEqual [| "src"; "a.txt"; "sub"; "lazy"; "z.txt" |]
        )
)
