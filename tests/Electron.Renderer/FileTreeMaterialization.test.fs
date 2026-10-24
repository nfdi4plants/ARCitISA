module ElectronRenderer.FileTreeMaterializationTests

open Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization
open Renderer.Context.FileTreeStore
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Vitest

let private nameOf (path: string) =
    path.Substring(path.LastIndexOf '/' + 1)

let private dir (path: string) =
    FileEntry.create (nameOf path, path, true)

let private file (path: string) =
    FileEntry.create (nameOf path, path, false)

/// A store snapshot with the given entries. The root entry is added, and loaded lists the loaded directories.
let private snapshotOf (entries: FileEntry list) (loaded: string list) =
    let store = FileTreeStore()
    store.BeginLoad()

    store.ApplySnapshot {|
        treeVersion = 1
        caseInsensitive = false
        entries = Array.ofList (FileEntry.create ("arc", "", true) :: entries)
        loadedDirectories = Array.ofList ("" :: loaded)
    |}

    store.Snapshot

let private createItem (visited: ResizeArray<string>) (entry: FileEntry) =
    visited.Add entry.path

    let item =
        if entry.isDirectory then
            FileTree.createFolder entry.name (Some entry.path) FileItemIcon.Folder
        else
            FileTree.createFile entry.name (Some entry.path) FileItemIcon.Document

    { item with Id = entry.path }

let private noBusy (_: string) (entries: FileEntry[]) = entries

let private build snapshot expanded =
    buildVisibleItems (createItem (ResizeArray())) noBusy snapshot (Set.ofList expanded)

Vitest.describe (
    "Electron file-tree visible items",
    fun () ->
        Vitest.test (
            "an unloaded directory has unknown children and a loaded empty one has none",
            fun () ->
                let snapshot = snapshotOf [ dir "data"; dir "empty" ] [ "empty" ]
                let items = build snapshot []

                Vitest.expect((items |> List.find (fun item -> item.Name = "data")).Children.IsNone).toBe true
                Vitest.expect((items |> List.find (fun item -> item.Name = "empty")).Children).toEqual (Some [])
        )

        Vitest.test (
            "a loaded directory with children is expandable until it is expanded",
            fun () ->
                let snapshot = snapshotOf [ dir "notes"; file "notes/note.md" ] [ "notes" ]

                let collapsed = build snapshot [] |> List.head
                let expanded = build snapshot [ "notes" ] |> List.head

                Vitest.expect(collapsed.Children.IsNone).toBe true
                Vitest.expect(expanded.Children.Value |> List.map _.Name).toEqual [ "note.md" ]
                Vitest.expect(expanded.IsExpanded).toBe false
        )

        Vitest.test (
            "only the expanded directories are visited",
            fun () ->
                let bigFolder = [ for index in 1..500 -> file $"big/file-{index}.txt" ]

                let snapshot =
                    snapshotOf
                        ([
                            dir "big"
                            dir "small"
                            file "small/a.txt"
                            file "small/b.txt"
                         ]
                         @ bigFolder)
                        [ "big"; "small" ]

                let visited = ResizeArray<string>()

                buildVisibleItems (createItem visited) noBusy snapshot (Set.ofList [ "small" ])
                |> ignore

                // The two root children and the two files of the expanded folder.
                Vitest.expect(visited.Count).toBe 4
                Vitest.expect(visited.Exists(fun path -> path.StartsWith "big/")).toBe false
        )

        Vitest.test (
            "orders recognized ARC items before other root-level items",
            fun () ->
                let snapshot =
                    snapshotOf [
                        file "unrelated.txt"
                        dir "runs"
                        dir "assays"
                        dir "studies"
                        file "isa.investigation.xlsx"
                        file "README.md"
                        dir "notes"
                        dir "workflows"
                    ] []

                Vitest.expect(build snapshot [] |> List.map _.Name).toEqual [
                    "notes"
                    "README.md"
                    "isa.investigation.xlsx"
                    "studies"
                    "assays"
                    "workflows"
                    "runs"
                    "unrelated.txt"
                ]
        )

        Vitest.test (
            "lists directories first, then names ignoring case, below the root",
            fun () ->
                let snapshot =
                    snapshotOf [
                        dir "notes"
                        file "notes/zebra.md"
                        file "notes/Apple.md"
                        dir "notes/sub"
                        file "notes/banana.md"
                    ] [ "notes" ]

                let notes = build snapshot [ "notes" ] |> List.head

                Vitest.expect(notes.Children.Value |> List.map _.Name).toEqual [
                    "sub"
                    "Apple.md"
                    "banana.md"
                    "zebra.md"
                ]
        )

        Vitest.test (
            "collapses a single same-named subdirectory when both levels are loaded",
            fun () ->
                let snapshot =
                    snapshotOf [ dir "Data"; dir "Data/data"; file "Data/data/leaf.txt" ] [ "Data"; "Data/data" ]

                let row = build snapshot [] |> List.head

                Vitest.expect(row.Name).toBe "Data"
                Vitest.expect(row.Path).toEqual (Some "Data/data")
                Vitest.expect(row.Id).toBe "Data/data"

                let opened = build snapshot [ "Data/data" ] |> List.head
                Vitest.expect(opened.Children.Value |> List.map _.Name).toEqual [ "leaf.txt" ]
        )

        Vitest.test (
            "does not collapse when the same-named child is not loaded",
            fun () ->
                let snapshot = snapshotOf [ dir "Data"; dir "Data/data" ] [ "Data" ]

                let row = build snapshot [ "Data" ] |> List.head

                Vitest.expect(row.Path).toEqual (Some "Data")
                Vitest.expect(row.Children.Value |> List.map _.Name).toEqual [ "data" ]
        )

        Vitest.test (
            "does not collapse when the child has siblings or a different name",
            fun () ->
                let withSibling =
                    snapshotOf [
                        dir "A"
                        dir "A/A"
                        file "A/notes.txt"
                        dir "B"
                        dir "B/C"
                    ] [ "A"; "A/A"; "B"; "B/C" ]

                let rows = build withSibling []
                Vitest.expect(rows |> List.map (fun row -> row.Path.Value)).toEqual [ "A"; "B" ]
        )
)
