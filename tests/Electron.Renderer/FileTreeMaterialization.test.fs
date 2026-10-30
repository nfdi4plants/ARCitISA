module ElectronRenderer.FileTreeMaterializationTests

open Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization
open Renderer.Context.FileTreeStore
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
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
        fileWatcher = {|
            state = FileWatcherStatus.Running
            reason = ""
        |}
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
            "a second build with the same snapshot reuses the cached items of untouched entries",
            fun () ->
                let snapshot =
                    snapshotOf [
                        dir "big"
                        file "big/a.txt"
                        file "big/b.txt"
                        dir "small"
                        file "small/c.txt"
                    ] [ "big"; "small" ]

                // The cache keeps the row of an entry instance, like the file tree does.
                let cache = Renderer.ReferenceCache.ReferenceCache<FileEntry, FileItem>()
                let built = ref 0

                let cachedItem (entry: FileEntry) =
                    cache.GetOrAdd(
                        entry,
                        fun entry ->
                            built.Value <- built.Value + 1
                            createItem (ResizeArray()) entry
                    )

                let first = buildVisibleItems cachedItem noBusy snapshot (Set.ofList [ "big" ])
                let builtByFirst = built.Value

                // Expanding another folder changes the expanded ids and nothing else.
                let second =
                    buildVisibleItems cachedItem noBusy snapshot (Set.ofList [ "big"; "small" ])

                let bigFiles (items: FileItem list) =
                    (items |> List.find (fun item -> item.Name = "big")).Children.Value

                Vitest.expect(builtByFirst).toBe 4
                // Only the two entries that became visible were built.
                Vitest.expect(built.Value - builtByFirst).toBe 1

                for before, after in List.zip (bigFiles first) (bigFiles second) do
                    Vitest.expect(obj.ReferenceEquals(before, after)).toBe true
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

        Vitest.test (
            "keeps the selected branch materialized below collapsed directories",
            fun () ->
                let snapshot =
                    snapshotOf [
                        dir "studies"
                        dir "studies/selected-study"
                        file "studies/selected-study/isa.study.xlsx"
                        dir "studies/selected-study/resources"
                        dir "studies/selected-study/resources/raw-data"
                        dir "studies/selected-study/resources/raw-data/batch-01"
                        dir "studies/selected-study/resources/raw-data/batch-01/replicate-01"
                        file "studies/selected-study/resources/raw-data/batch-01/replicate-01/selected-report.txt"
                        file "studies/selected-study/resources/raw-data/batch-01/replicate-01/sibling-report.txt"
                        dir "studies/selected-study/resources/raw-data/batch-02"
                        file "studies/selected-study/resources/raw-data/batch-02/other-report.txt"
                        dir "studies/selected-study/study-assays"
                        dir "studies/selected-study/study-assays/rna-seq"
                        file "studies/selected-study/study-assays/rna-seq/isa.assay.xlsx"
                        dir "studies/neighbor-study"
                        file "studies/neighbor-study/isa.study.xlsx"
                        dir "assays"
                        dir "assays/neighbor-assay"
                    ] [
                        "studies"
                        "studies/selected-study"
                        "studies/selected-study/resources"
                        "studies/selected-study/resources/raw-data"
                        "studies/selected-study/resources/raw-data/batch-01"
                        "studies/selected-study/resources/raw-data/batch-01/replicate-01"
                        "studies/selected-study/resources/raw-data/batch-02"
                        "studies/selected-study/study-assays"
                        "studies/selected-study/study-assays/rna-seq"
                        "studies/neighbor-study"
                        "assays"
                    ]

                let items =
                    let selectedPath =
                        "studies/selected-study/resources/raw-data/batch-01/replicate-01/selected-report.txt"

                    build snapshot (Renderer.Context.FileTreeExpansionContext.ancestorPaths selectedPath)

                let selectedPath =
                    FileTree.getPath
                        "studies/selected-study/resources/raw-data/batch-01/replicate-01/selected-report.txt"
                        items
                        []
                    |> Option.map (List.map _.Id)

                Vitest
                    .expect(selectedPath)
                    .toEqual (
                        Some [
                            "studies"
                            "studies/selected-study"
                            "studies/selected-study/resources"
                            "studies/selected-study/resources/raw-data"
                            "studies/selected-study/resources/raw-data/batch-01"
                            "studies/selected-study/resources/raw-data/batch-01/replicate-01"
                            "studies/selected-study/resources/raw-data/batch-01/replicate-01/selected-report.txt"
                        ]
                    )

                let visible = FileExplorerLogic.flattenVisibleRows Set.empty items |> Array.map _.Id
                Vitest.expect(visible).toEqual [| "studies"; "assays" |]

                let studies = items |> List.find (fun item -> item.Id = "studies")

                Vitest.expect(studies.Children.Value |> List.map _.Id).toEqual [
                    "studies/neighbor-study"
                    "studies/selected-study"
                ]

                let neighbor =
                    studies.Children.Value
                    |> List.find (fun item -> item.Id = "studies/neighbor-study")

                Vitest.expect(neighbor.Children.IsNone).toBe true

                let study =
                    studies.Children.Value
                    |> List.find (fun item -> item.Id = "studies/selected-study")

                let isaStudy =
                    study.Children.Value
                    |> List.find (fun item -> item.Id.EndsWith "isa.study.xlsx")

                Vitest.expect(isaStudy.Children.IsNone).toBe true

                let studyAssays =
                    study.Children.Value
                    |> List.find (fun item -> item.Id = "studies/selected-study/study-assays")

                Vitest.expect(studyAssays.Children.IsNone).toBe true
        )
)
