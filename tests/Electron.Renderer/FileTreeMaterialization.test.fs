module ElectronRenderer.FileTreeMaterializationTests

open System.Collections.Generic
open Renderer.Components.LeftSidebar.FileExplorer.Helper
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Vitest

let private fileNode (name: string) (path: string) =
    FileTreeNode.create (name, false, path, Dictionary())

let private directoryNode (name: string) (path: string) (children: FileTreeNode list) =
    let childrenByName = Dictionary<string, FileTreeNode>()

    children |> List.iter (fun child -> childrenByName.[child.name] <- child)

    FileTreeNode.create (name, true, path, childrenByName)

let private toFileItemTreeWith isLoaded materializedDirectoryPaths node =
    toMaterializedFileItemTree
        (fun node ->
            let item =
                if node.isDirectory then
                    FileTree.createFolder node.name (Some node.path) FileItemIcon.Folder
                else
                    FileTree.createFile node.name (Some node.path) FileItemIcon.Document

            { item with Id = node.path }
        )
        isLoaded
        materializedDirectoryPaths
        node
        true

let private toFileItemTree materializedDirectoryPaths node =
    toFileItemTreeWith (fun _ -> true) materializedDirectoryPaths node

Vitest.describe (
    "Electron file-tree materialization",
    fun () ->
        Vitest.test (
            "materializing a directory normalizes its path and preserves its ARC scope",
            fun () ->
                let state = {
                    empty with
                        ArcScopeId = Some "C:/arc"
                }

                let materialized = materialize "arc\\notes" state

                Vitest.expect(materialized.ArcScopeId).toEqual (Some "C:/arc")
                Vitest.expect(materialized.Paths |> Set.toList).toEqual ([ "arc/notes" ])
        )

        Vitest.test (
            "maps an unmaterialized non-empty directory without children or expansion state",
            fun () ->
                let directory =
                    directoryNode "notes" "arc/notes" [ fileNode "note.md" "arc/notes/note.md" ]

                let item = toFileItemTree Set.empty directory

                Vitest.expect(item.Children.IsNone).toBe (true)
                Vitest.expect(item.IsExpanded).toBe (false)
        )

        Vitest.test (
            "an unloaded directory has unknown children and a loaded empty one has none",
            fun () ->
                let unloaded = directoryNode "data" "arc/data" []
                let loadedEmpty = directoryNode "empty" "arc/empty" []
                let isLoaded path = path = "arc/empty"

                let unloadedItem = toFileItemTreeWith isLoaded (Set.singleton "arc/data") unloaded
                let emptyItem = toFileItemTreeWith isLoaded Set.empty loadedEmpty

                Vitest.expect(unloadedItem.Children.IsNone).toBe (true)
                Vitest.expect(emptyItem.Children).toEqual (Some [])
        )

        Vitest.test (
            "maps children for a materialized directory without setting expansion state",
            fun () ->
                let directory =
                    directoryNode "notes" "arc/notes" [ fileNode "note.md" "arc/notes/note.md" ]

                let item = toFileItemTree (Set.singleton "arc/notes") directory

                Vitest.expect(item.Children.IsSome).toBe (true)
                Vitest.expect(item.Children.Value.Length).toBe (1)
                Vitest.expect(item.Children.Value.Head.Name).toBe ("note.md")
                Vitest.expect(item.IsExpanded).toBe (false)
        )

        Vitest.test (
            "orders recognized ARC items before other root-level items",
            fun () ->
                let root =
                    directoryNode "arc" "arc" [
                        fileNode "unrelated.txt" "arc/unrelated.txt"
                        directoryNode "runs" "arc/runs" []
                        directoryNode "assays" "arc/assays" []
                        directoryNode "studies" "arc/studies" []
                        fileNode "isa.investigation.xlsx" "arc/isa.investigation.xlsx"
                        fileNode "README.md" "arc/README.md"
                        directoryNode "notes" "arc/notes" []
                        directoryNode "workflows" "arc/workflows" []
                    ]

                let item = toFileItemTree (Set.singleton "arc") root

                Vitest.expect(item.Children.Value |> List.map _.Name).toEqual [
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
            "orders only root-level items",
            fun () ->
                let notes =
                    directoryNode "notes" "arc/notes" [
                        fileNode "zebra.md" "arc/notes/zebra.md"
                        fileNode "apple.md" "arc/notes/apple.md"
                    ]

                let root =
                    directoryNode "arc" "arc" [
                        fileNode "another-file.txt" "arc/another-file.txt"
                        notes
                    ]

                let item = toFileItemTree (Set.ofList [ "arc"; "arc/notes" ]) root
                let notesItem = item.Children.Value |> List.find (fun child -> child.Name = "notes")

                Vitest.expect(item.Children.Value |> List.map _.Name).toEqual [ "notes"; "another-file.txt" ]
                Vitest.expect(notesItem.Children.Value |> List.map _.Name).toEqual [ "zebra.md"; "apple.md" ]
        )

        Vitest.test (
            "snapshot reconciliation preserves surviving paths, prunes removed paths, and materializes selection",
            fun () ->
                let kept =
                    directoryNode "kept" "arc/kept" [ fileNode "kept.txt" "arc/kept/kept.txt" ]

                let selected =
                    directoryNode "selected" "arc/selected" [ fileNode "selected.txt" "arc/selected/selected.txt" ]

                let root = directoryNode "arc" "arc" [ kept; selected ]

                let current = {
                    ArcScopeId = Some "C:/arc"
                    Paths = Set.ofList [ "arc"; "arc/kept"; "arc/removed" ]
                }

                let reconciled =
                    reconcileMaterializedState (Some "C:/arc") (Some "arc/selected/selected.txt") (Some root) current

                Vitest.expect(reconciled.Paths |> Set.toList).toEqual ([ "arc"; "arc/kept"; "arc/selected" ])
        )

        Vitest.test (
            "changing ARC scope resets materialized paths to the required root and selection chain",
            fun () ->
                let kept =
                    directoryNode "kept" "arc/kept" [ fileNode "kept.txt" "arc/kept/kept.txt" ]

                let root = directoryNode "arc" "arc" [ kept ]

                let current = {
                    ArcScopeId = Some "C:/old-arc"
                    Paths = Set.ofList [ "arc"; "arc/kept" ]
                }

                let reconciled =
                    reconcileMaterializedState (Some "C:/new-arc") None (Some root) current

                Vitest.expect(reconciled.ArcScopeId).toEqual (Some "C:/new-arc")
                Vitest.expect(reconciled.Paths |> Set.toList).toEqual ([ "arc" ])
        )
)
