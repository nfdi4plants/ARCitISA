module ElectronCore.IndexedFileTreeTests

open Fable.Core
open Main
open Main.ARCtrlExtensions
open Main.ArcVault
open Main.ArcVaultTypes
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Vitest

module FileTreeCreator = Main.FileTreeCreator

let private entry (name: string) (directory: string) (isDirectory: bool) =
    FileEntry.create (name, $"{directory}/{name}", isDirectory)

let private names (entries: FileEntry[]) = entries |> Array.map _.name

let private mkdirAsync' (path: string) = promise {
    let! _ = mkdirAsync path (MkdirOptions(recursive = true))
    return ()
}

let private writeAsync (path: string) =
    writeFileAsync path "x" TextEncoding.Utf8

let private withTempFolder (body: string -> JS.Promise<unit>) = promise {
    let! created = TestHelpers.createTempDirectoryAsync "swate-indexed-tree-"
    let root = FileTreeCreator.normalizeRootPath created

    try
        do! body root
    finally
        TestHelpers.removeDirectoryAsync root |> ignore
}

let private treePaths (tree: IndexedFileTree) =
    tree.Entries |> Seq.map (fun e -> e.path) |> Seq.sort |> Seq.toArray

Vitest.describe (
    "IndexedFileTree.ReplaceChildren",
    fun () ->
        Vitest.test (
            "an unchanged child keeps its instance and nothing is reported",
            fun () ->
                let tree = IndexedFileTree(false)
                let child = entry "a.txt" "/arc" false
                Vitest.expect(tree.ReplaceChildren("/arc", [| child |])).toEqual ([| "/arc" |])
                let stored = (tree.TryFind "/arc/a.txt").Value
                Vitest.expect(tree.ReplaceChildren("/arc", [| { child with path = child.path } |])).toEqual ([||])
                Vitest.expect(obj.ReferenceEquals((tree.TryFind "/arc/a.txt").Value, stored)).toBe (true)
        )

        Vitest.test (
            "a removed child drops its whole subtree",
            fun () ->
                let tree = IndexedFileTree(false)

                tree.ReplaceChildren("/arc", [| entry "keep.txt" "/arc" false; entry "dir" "/arc" true |])
                |> ignore

                tree.ReplaceChildren("/arc/dir", [| entry "inner" "/arc/dir" true |]) |> ignore

                tree.ReplaceChildren("/arc/dir/inner", [| entry "deep.txt" "/arc/dir/inner" false |])
                |> ignore

                Vitest.expect(tree.ReplaceChildren("/arc", [| entry "keep.txt" "/arc" false |])).toEqual ([| "/arc" |])
                Vitest.expect(treePaths tree).toEqual ([| "/arc/keep.txt" |])
                Vitest.expect(tree.IsLoaded "/arc/dir").toBe (false)
        )

        Vitest.test (
            "children are sorted by PathOrder.compareNames",
            fun () ->
                let tree = IndexedFileTree(false)

                tree.ReplaceChildren(
                    "/arc",
                    [|
                        entry "b.txt" "/arc" false
                        entry "a.txt" "/arc" false
                        entry "A.txt" "/arc" false
                    |]
                )
                |> ignore

                Vitest.expect(names (tree.Children "/arc").Value).toEqual ([| "A.txt"; "a.txt"; "b.txt" |])
        )

        Vitest.test (
            "a case-insensitive tree treats A.txt and a.txt as one path",
            fun () ->
                let insensitive = IndexedFileTree(true)
                let sensitive = IndexedFileTree(false)
                let children = [| entry "A.txt" "/arc" false; entry "a.txt" "/arc" false |]
                insensitive.ReplaceChildren("/arc", children) |> ignore
                sensitive.ReplaceChildren("/arc", children) |> ignore

                Vitest.expect(insensitive.Count).toBe 1
                Vitest.expect((insensitive.TryFind "/ARC/a.txt").IsSome).toBe (true)
                Vitest.expect(sensitive.Count).toBe 2
        )
)

Vitest.describe (
    "reconcileFileTreeDirectoryAsync",
    fun () ->
        Vitest.test (
            "applies added files, removed folders and new subdirectories",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "old"; "inner" |])
                    do! writeAsync (join [| root; "old"; "inner"; "f.txt" |])

                    let! tree = TestHelpers.loadWholeTreeAsync root false
                    Vitest.expect((tree.TryFind(root + "/old/inner/f.txt")).IsSome).toBe (true)

                    do! rmAsync (join [| root; "old" |]) (RmOptions(recursive = true, force = true))
                    do! mkdirAsync' (join [| root; "fresh"; "nested" |])
                    do! writeAsync (join [| root; "fresh"; "nested"; "n.txt" |])
                    do! writeAsync (join [| root; "added.txt" |])

                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root

                    Vitest.expect(changed).toEqual ([| root |])

                    // A new payload folder is an entry of its parent and stays unloaded.
                    Vitest.expect(treePaths tree).toEqual ([| root; root + "/added.txt"; root + "/fresh" |])

                    Vitest.expect(tree.IsLoaded(root + "/fresh")).toBe (false)

                    let! unchanged = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root
                    Vitest.expect(unchanged).toEqual ([||])
                })
        )

        Vitest.test (
            "a missing directory removes its subtree",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "gone" |])
                    do! writeAsync (join [| root; "gone"; "f.txt" |])
                    let! tree = TestHelpers.loadWholeTreeAsync root false

                    do! rmAsync (join [| root; "gone" |]) (RmOptions(recursive = true, force = true))
                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree (root + "/gone")

                    Vitest.expect(changed).toEqual ([| root |])
                    Vitest.expect(treePaths tree).toEqual ([| root |])
                })
        )

        Vitest.test (
            "ignored inventory entries never appear",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; ".git" |])
                    do! writeAsync (join [| root; ".git"; "config" |])
                    do! writeAsync (join [| root; "~$x.xlsx" |])
                    do! writeAsync (join [| root; "keep.txt" |])

                    let! scanned = TestHelpers.loadWholeTreeAsync root false
                    let reconciled = IndexedFileTree(false)
                    let! _ = FileTreeCreator.reconcileFileTreeDirectoryAsync root reconciled root

                    Vitest.expect(treePaths scanned).toEqual ([| root; root + "/keep.txt" |])
                    Vitest.expect(treePaths reconciled).toEqual ([| root + "/keep.txt" |])
                })
        )
)

Vitest.describe (
    "FileTreeWriter",
    fun () ->
        let createWriter () =
            let published = ResizeArray<FileTreeChange>()

            let writer = FileTreeWriter(false, ignore, published.Add)

            writer, published

        Vitest.test (
            "jobs run in order and TreeVersion counts only changed jobs",
            fun () -> promise {
                let writer, published = createWriter ()
                let order = ResizeArray<int>()

                let job (id: int) (delay: int) (changed: bool) =
                    writer.Enqueue(fun _ -> promise {
                        do! Promise.sleep delay
                        order.Add id
                        return (if changed then [| "/arc" |] else [||])
                    })

                let first = job 1 60 true
                let second = job 2 0 false
                let third = job 3 0 true
                do! first
                do! second
                do! third

                Vitest.expect(order |> Seq.map string |> String.concat ",").toBe "1,2,3"
                Vitest.expect(writer.TreeVersion).toBe 2
                Vitest.expect(published.Count).toBe 2
            }
        )

        Vitest.test (
            "a throwing job does not stop the next job",
            fun () -> promise {
                let writer, _ = createWriter ()
                let mutable ran = false
                do! writer.Enqueue(fun _ -> promise { return (failwith "boom": string[]) })

                do!
                    writer.Enqueue(fun _ -> promise {
                        ran <- true
                        return [| "/arc" |]
                    })

                Vitest.expect(ran).toBe (true)
                Vitest.expect(writer.TreeVersion).toBe 1
            }
        )

        Vitest.test (
            "a job enqueued before Reset is skipped",
            fun () -> promise {
                let writer, published = createWriter ()
                let gate, release = TestHelpers.deferred ()
                let mutable laterRan = false

                let running =
                    writer.Enqueue(fun _ -> promise {
                        do! gate
                        return [| "/arc" |]
                    })

                let later =
                    writer.Enqueue(fun _ -> promise {
                        laterRan <- true
                        return [| "/arc" |]
                    })

                writer.Reset false
                release ()
                do! running
                do! later

                Vitest.expect(laterRan).toBe (false)
                Vitest.expect(writer.TreeVersion).toBe 0
                Vitest.expect(published.Count).toBe 0
            }
        )

        Vitest.test (
            "a slow full scan cannot overwrite a later directory reconcile",
            fun () ->
                withTempFolder (fun root -> promise {
                    let writer, published = createWriter ()
                    do! writeAsync (join [| root; "a.txt" |])

                    let scanned, signalScanned = TestHelpers.deferred ()

                    let slowScan =
                        writer.EnqueueReplace(fun () -> promise {
                            let! stale = TestHelpers.loadWholeTreeAsync root false
                            signalScanned ()
                            do! Promise.sleep 150
                            return stale
                        })

                    do! scanned
                    do! writeAsync (join [| root; "b.txt" |])

                    let reconcile =
                        writer.Enqueue(fun tree -> FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root)

                    do! slowScan
                    do! reconcile

                    let expected = [| root; root + "/a.txt"; root + "/b.txt" |]
                    Vitest.expect(treePaths writer.Tree).toEqual (expected)
                    // The replace bumps the version without a publish, so only the reconcile publishes.
                    Vitest.expect(writer.TreeVersion).toBe 2
                    Vitest.expect(published.Count).toBe 1
                    Vitest.expect(published.[0].TreeVersion).toBe 2
                    Vitest.expect(published.[0].ChangedDirectories).toEqual ([| root |])
                })
        )
)

Vitest.describe (
    "ApplyWatcherFileTreeEvents",
    fun () ->
        Vitest.test (
            "a batch reconciles its dirty directories and publishes once",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "studies" |])
                    let vault = ArcVault(TestHelpers.testWindow ())
                    vault.path <- Some root
                    do! vault.EnqueueFileTreeScan()

                    let mutable publishCount = 0
                    vault.FileTreeWriter.Publish <- fun _ -> publishCount <- publishCount + 1

                    do! mkdirAsync' (join [| root; "studies"; "S1" |])
                    do! mkdirAsync' (join [| root; "studies"; "S2" |])
                    do! writeAsync (join [| root; "studies"; "S1"; "isa.study.xlsx" |])

                    let event name relative : ArcVaultFileSystemEvent = {
                        EventName = name
                        RelativePath = relative
                        AbsolutePath = join [| root; relative |]
                    }

                    Vitest
                        .expect(
                            WatcherHelpers.dirtyDirectoriesOfEvents false [
                                event "add" "studies/S1/isa.study.xlsx"
                                event "addDir" "studies/S2"
                            ]
                            |> Array.sort
                        )
                        .toEqual (
                            [|
                                root + "/studies"
                                root + "/studies/S1"
                                root + "/studies/S2"
                            |]
                        )

                    do!
                        vault.ApplyWatcherFileTreeEvents [
                            event "add" "studies/S1/isa.study.xlsx"
                            event "addDir" "studies/S2"
                        ]

                    Vitest
                        .expect((vault.FileTreeWriter.Tree.TryFind(root + "/studies/S1/isa.study.xlsx")).IsSome)
                        .toBe (true)

                    Vitest.expect(vault.FileTreeWriter.Tree.IsLoaded(root + "/studies/S2")).toBe (true)
                    Vitest.expect(publishCount).toBe 1
                })
        )
)

Vitest.describe (
    "SaveArc and EnqueueFileTreeRevalidateLoaded",
    fun () ->
        let withTempArc = TestHelpers.withTempArcWith "swate-indexed-save-" "IndexedSaveArc"

        let openVault (arcPath: string) = promise {
            let vault = ArcVault(TestHelpers.testWindow ())
            vault.path <- Some arcPath
            do! vault.LoadArc()
            do! vault.EnqueueFileTreeScan()
            return vault
        }

        Vitest.test (
            "WriteArc lists the file of a renamed study",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddStudy(ARCtrl.ArcStudy("S1")))
                    (fun arcPath -> promise {
                        let! vault = openVault arcPath
                        vault.arc.Value.RenameStudy("S1", "S2")

                        match! vault.WriteArc() with
                        | Error error -> failwith error.Message
                        | Ok paths -> Vitest.expect(paths |> Array.contains "studies/S2/isa.study.xlsx").toBe (true)

                        let! exists =
                            TestHelpers.pathExistsAsync (join [| arcPath; "studies"; "S2"; "isa.study.xlsx" |])

                        Vitest.expect(exists).toBe (true)
                    })
        )

        Vitest.test (
            "SaveArc adds the renamed study to the tree without a full scan",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddStudy(ARCtrl.ArcStudy("S1")))
                    (fun arcPath -> promise {
                        let! vault = openVault arcPath
                        let root = FileTreeCreator.normalizeRootPath arcPath
                        let tree = vault.FileTreeWriter.Tree
                        let investigationBefore = tree.TryFind(root + "/isa.investigation.xlsx")
                        Vitest.expect(investigationBefore.IsSome).toBe (true)

                        let mutable publishCount = 0
                        vault.FileTreeWriter.Publish <- fun _ -> publishCount <- publishCount + 1
                        vault.arc.Value.RenameStudy("S1", "S2")

                        match! vault.SaveArc() with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        Vitest.expect((tree.TryFind(root + "/studies/S2/isa.study.xlsx")).IsSome).toBe (true)
                        Vitest.expect(publishCount).toBe 1

                        Vitest
                            .expect(
                                obj.ReferenceEquals(
                                    (tree.TryFind(root + "/isa.investigation.xlsx")).Value,
                                    investigationBefore.Value
                                )
                            )
                            .toBe (true)
                    })
        )

        Vitest.test (
            "revalidating the loaded directories finds deep changes with one publish",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ARCtrl.ArcAssay("A1")))
                    (fun arcPath -> promise {
                        let dataset = join [| arcPath; "assays"; "A1"; "dataset" |]
                        do! mkdirAsync' (join [| dataset; "sub" |])
                        do! mkdirAsync' (join [| dataset; "old" |])
                        do! mkdirAsync' (join [| dataset; "keep" |])
                        do! writeAsync (join [| dataset; "sub"; "first.txt" |])
                        do! writeAsync (join [| dataset; "old"; "gone.txt" |])
                        do! writeAsync (join [| dataset; "keep"; "kept.txt" |])

                        let! vault = openVault arcPath
                        let root = FileTreeCreator.normalizeRootPath arcPath
                        let tree = vault.FileTreeWriter.Tree

                        // The payload folders are not loaded by the structural scan.
                        for folder in [ "dataset"; "dataset/sub"; "dataset/old"; "dataset/keep" ] do
                            let! loaded = vault.LoadDirectory $"assays/A1/{folder}"
                            Vitest.expect(loaded.IsOk).toBe (true)

                        let keptBefore = tree.TryFind(root + "/assays/A1/dataset/keep/kept.txt")
                        Vitest.expect(keptBefore.IsSome).toBe (true)

                        let mutable publishCount = 0
                        vault.FileTreeWriter.Publish <- fun _ -> publishCount <- publishCount + 1

                        do! writeAsync (join [| dataset; "sub"; "second.txt" |])
                        do! rmAsync (join [| dataset; "old" |]) (RmOptions(recursive = true, force = true))

                        do! vault.EnqueueFileTreeRevalidateLoaded()

                        Vitest.expect((tree.TryFind(root + "/assays/A1/dataset/sub/second.txt")).IsSome).toBe (true)
                        Vitest.expect((tree.TryFind(root + "/assays/A1/dataset/old")).IsSome).toBe (false)
                        Vitest.expect((tree.TryFind(root + "/assays/A1/dataset/old/gone.txt")).IsSome).toBe (false)
                        Vitest.expect(publishCount).toBe 1

                        Vitest
                            .expect(
                                obj.ReferenceEquals(
                                    (tree.TryFind(root + "/assays/A1/dataset/keep/kept.txt")).Value,
                                    keptBefore.Value
                                )
                            )
                            .toBe (true)
                    })
        )
)

Vitest.describe (
    "replaced directories and path keys",
    fun () ->
        Vitest.test (
            "a case-only rename reports the replaced directory as new",
            fun () ->
                let tree = IndexedFileTree(true)
                tree.ReplaceChildren("/arc", [| entry "assays" "/arc" true |]) |> ignore

                tree.ReplaceChildren("/arc/assays", [| entry "A" "/arc/assays" true |])
                |> ignore

                tree.ReplaceChildren("/arc/assays/A", [| entry "x.txt" "/arc/assays/A" false |])
                |> ignore

                let changed, newDirectories =
                    tree.ReplaceChildrenReportingNew("/arc/assays", [| entry "a" "/arc/assays" true |])

                Vitest.expect(changed).toEqual ([| "/arc/assays" |])
                Vitest.expect(newDirectories |> Array.map _.path).toEqual ([| "/arc/assays/a" |])
                Vitest.expect(tree.IsLoaded "/arc/assays/a").toBe (false)
                Vitest.expect((tree.TryFind "/arc/assays/a/x.txt").IsSome).toBe (false)
        )

        Vitest.test (
            "reconcile keeps the content of a folder that was renamed by case only",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "assays"; "A" |])
                    do! writeAsync (join [| root; "assays"; "A"; "x.txt" |])
                    let! tree = TestHelpers.loadWholeTreeAsync root true

                    do! rmAsync (join [| root; "assays"; "A" |]) (RmOptions(recursive = true, force = true))
                    do! mkdirAsync' (join [| root; "assays"; "a" |])
                    do! writeAsync (join [| root; "assays"; "a"; "x.txt" |])

                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree (root + "/assays")

                    Vitest.expect(changed |> Array.sort).toEqual ([| root + "/assays"; root + "/assays/a" |])
                    Vitest.expect((tree.TryFind(root + "/assays/a/x.txt")).IsSome).toBe (true)
                    Vitest.expect(tree.IsLoaded(root + "/assays/a")).toBe (true)
                })
        )

        Vitest.test (
            "a directory that replaced a file becomes an unloaded entry",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! writeAsync (join [| root; "item" |])
                    let! tree = TestHelpers.loadWholeTreeAsync root false

                    do! rmAsync (join [| root; "item" |]) (RmOptions(force = true))
                    do! mkdirAsync' (join [| root; "item" |])
                    do! writeAsync (join [| root; "item"; "inner.txt" |])

                    let! _ = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root

                    Vitest.expect((tree.TryFind(root + "/item")).Value.isDirectory).toBe (true)
                    Vitest.expect(tree.IsLoaded(root + "/item")).toBe (false)
                })
        )

        Vitest.test (
            "path keys keep whitespace in names",
            fun () ->
                let key (value: string) =
                    PathKey.ofPath false value |> PathKey.value

                Vitest.expect(key "/arc/data " <> key "/arc/data").toBe (true)
                Vitest.expect(key "/arc/data/").toBe (key "/arc/data")
        )

        Vitest.test (
            "contractWrittenPaths lists deleted paths and rename targets",
            fun () ->
                let paths =
                    ARCtrl.Contract.Contract.createDelete "studies/S1/old.txt"
                    |> fun deleted ->
                        contractWrittenPaths [|
                            deleted
                            ARCtrl.Contract.Contract.createRename ("assays/A", "assays/B")
                            ARCtrl.Contract.Contract.createDelete "studies/S1/old.txt"
                        |]

                Vitest.expect(paths |> String.concat ",").toBe ("studies/S1/old.txt,assays/A,assays/B")
        )
)

Vitest.describe (
    "app-initiated file operations update the tree",
    fun () ->
        let withRegisteredVault (windowId: int) (body: ArcVault -> JS.Promise<unit>) =
            TestHelpers.withTempArcWith
                "swate-ipc-tree-"
                "IpcTreeArc"
                (fun arc -> arc.AddAssay(ARCtrl.ArcAssay("A1")))
                (fun arcPath -> promise {
                    do! mkdirAsync' (join [| arcPath; "assays"; "A1"; "dataset" |])
                    do! writeAsync (join [| arcPath; "assays"; "A1"; "dataset"; "move-me.txt" |])
                    let vault = TestHelpers.registerVault windowId arcPath
                    do! vault.LoadArc()
                    do! vault.EnqueueFileTreeScan()

                    try
                        do! body vault
                    finally
                        ARC_VAULTS.Vaults.Remove windowId |> ignore
                })

        Vitest.test (
            "creating a file in a payload folder adds it to the tree",
            fun () ->
                withRegisteredVault
                    71
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 71)
                        let! loaded = api.loadDirectory "assays/A1/dataset"
                        Vitest.expect(loaded.IsOk).toBe (true)

                        let! created =
                            api.createFileSystemItem {
                                parentPath = "assays/A1/dataset"
                                name = "created.txt"
                                kind = FileSystemItemKind.File
                            }

                        Vitest.expect(created.IsOk).toBe (true)
                        let root = FileTreeCreator.normalizeRootPath vault.path.Value

                        Vitest
                            .expect(
                                (vault.FileTreeWriter.Tree.TryFind(root + "/assays/A1/dataset/created.txt")).IsSome
                            )
                            .toBe (true)
                    })
        )

        Vitest.test (
            "moving a file between payload folders updates both sides",
            fun () ->
                withRegisteredVault
                    72
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 72)
                        let! loaded = api.loadDirectory "assays/A1/dataset"
                        Vitest.expect(loaded.IsOk).toBe (true)

                        let! moved =
                            api.movePath {
                                sourceRelativePath = "assays/A1/dataset/move-me.txt"
                                targetRelativePath = "assays/A1/other/move-me.txt"
                                overwrite = false
                            }

                        Vitest.expect(moved.IsOk).toBe (true)
                        let root = FileTreeCreator.normalizeRootPath vault.path.Value
                        let tree = vault.FileTreeWriter.Tree
                        Vitest.expect((tree.TryFind(root + "/assays/A1/dataset/move-me.txt")).IsSome).toBe (false)
                        // The new target folder is an entry of the entity folder and loads on demand.
                        Vitest.expect((tree.TryFind(root + "/assays/A1/other")).IsSome).toBe (true)
                        Vitest.expect(tree.IsLoaded(root + "/assays/A1/other")).toBe (false)

                        let! otherLoaded = api.loadDirectory "assays/A1/other"
                        Vitest.expect(otherLoaded.IsOk).toBe (true)
                        Vitest.expect((tree.TryFind(root + "/assays/A1/other/move-me.txt")).IsSome).toBe (true)
                    })
        )
)

Vitest.describe (
    "structural file tree",
    fun () ->
        Vitest.test (
            "the structural scan loads the root, zone folders and entity folders only",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "assays"; "A1"; "dataset"; "deep" |])
                    do! mkdirAsync' (join [| root; "studies"; "S1" |])
                    do! mkdirAsync' (join [| root; "notes"; "2026" |])
                    do! writeAsync (join [| root; "assays"; "A1"; "isa.assay.xlsx" |])
                    do! writeAsync (join [| root; "assays"; "A1"; "dataset"; "raw.bin" |])
                    do! writeAsync (join [| root; "isa.investigation.xlsx" |])

                    let! tree = FileTreeCreator.scanStructuralFileTreeAsync root false

                    let loaded = tree.LoadedDirectories |> Array.sort

                    Vitest
                        .expect(loaded)
                        .toEqual (
                            [|
                                root
                                root + "/assays"
                                root + "/assays/A1"
                                root + "/studies"
                                root + "/studies/S1"
                            |]
                        )

                    // The payload folder is an entry of its entity folder. Nothing below it is known.
                    Vitest.expect((tree.TryFind(root + "/assays/A1/dataset")).IsSome).toBe (true)
                    Vitest.expect((tree.TryFind(root + "/assays/A1/dataset/raw.bin")).IsSome).toBe (false)
                    Vitest.expect((tree.TryFind(root + "/notes")).IsSome).toBe (true)
                    Vitest.expect(tree.IsLoaded(root + "/notes")).toBe (false)
                })
        )

        Vitest.test (
            "a new entity folder gets its direct children and a new payload folder stays unloaded",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "assays"; "A1" |])
                    let! tree = FileTreeCreator.scanStructuralFileTreeAsync root false

                    do! mkdirAsync' (join [| root; "assays"; "A2"; "dataset" |])
                    do! writeAsync (join [| root; "assays"; "A2"; "isa.assay.xlsx" |])
                    do! writeAsync (join [| root; "assays"; "A2"; "dataset"; "raw.bin" |])

                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree (root + "/assays")

                    Vitest.expect(tree.IsLoaded(root + "/assays/A2")).toBe (true)
                    Vitest.expect((tree.TryFind(root + "/assays/A2/isa.assay.xlsx")).IsSome).toBe (true)
                    Vitest.expect((tree.TryFind(root + "/assays/A2/dataset")).IsSome).toBe (true)
                    Vitest.expect(tree.IsLoaded(root + "/assays/A2/dataset")).toBe (false)
                    Vitest.expect((tree.TryFind(root + "/assays/A2/dataset/raw.bin")).IsSome).toBe (false)
                    Vitest.expect(changed |> Array.sort).toEqual ([| root + "/assays"; root + "/assays/A2" |])
                })
        )

        Vitest.test (
            "a new zone folder loads together with its entity folders",
            fun () ->
                withTempFolder (fun root -> promise {
                    let! tree = FileTreeCreator.scanStructuralFileTreeAsync root false
                    do! mkdirAsync' (join [| root; "workflows"; "W1" |])
                    do! writeAsync (join [| root; "workflows"; "W1"; "isa.workflow.xlsx" |])

                    let! _ = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root

                    Vitest.expect(tree.IsLoaded(root + "/workflows")).toBe (true)
                    Vitest.expect(tree.IsLoaded(root + "/workflows/W1")).toBe (true)
                    Vitest.expect((tree.TryFind(root + "/workflows/W1/isa.workflow.xlsx")).IsSome).toBe (true)
                })
        )

        Vitest.test (
            "a writer job publishes the changed directories once, including the parent of a removed directory",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "sub"; "gone" |])
                    let! scanned = TestHelpers.loadWholeTreeAsync root false
                    let published = ResizeArray<FileTreeChange>()
                    let writer = FileTreeWriter(false, ignore, published.Add)
                    do! writer.EnqueueReplace(fun () -> promise { return scanned })

                    // The replace bumps the version and publishes nothing.
                    Vitest.expect(writer.TreeVersion).toBe 1
                    Vitest.expect(published.Count).toBe 0

                    do! rmAsync (join [| root; "sub"; "gone" |]) (RmOptions(recursive = true, force = true))
                    do! writeAsync (join [| root; "added.txt" |])

                    do!
                        writer.Enqueue(fun tree ->
                            FileTreeCreator.reconcileDirtyDirectoriesAsync
                                root
                                tree
                                (fun _ error -> failwith error.Message)
                                [| root; root + "/sub/gone" |]
                        )

                    Vitest.expect(published.Count).toBe 1
                    Vitest.expect(published.[0].TreeVersion).toBe 2
                    Vitest.expect(published.[0].ChangedDirectories |> Array.sort).toEqual ([| root; root + "/sub" |])
                })
        )
)

Vitest.describe (
    "lazy file tree IPC",
    fun () ->
        let withRegisteredArc (windowId: int) (body: ArcVault -> JS.Promise<unit>) =
            TestHelpers.withTempArcWith
                "swate-lazy-tree-"
                "LazyTreeArc"
                (fun arc -> arc.AddAssay(ARCtrl.ArcAssay("A1")))
                (fun arcPath -> promise {
                    do! mkdirAsync' (join [| arcPath; "assays"; "A1"; "dataset"; "sub" |])
                    do! writeAsync (join [| arcPath; "assays"; "A1"; "dataset"; "raw.txt" |])
                    let vault = TestHelpers.registerVault windowId arcPath
                    do! vault.LoadArc()
                    do! vault.EnqueueFileTreeScan()

                    try
                        do! body vault
                    finally
                        ARC_VAULTS.Vaults.Remove windowId |> ignore
                })

        Vitest.test (
            "the snapshot lists the loaded directories with ARC-relative paths",
            fun () ->
                withRegisteredArc
                    81
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 81)

                        match! api.getFileTreeSnapshot () with
                        | Error error -> failwith error.Message
                        | Ok snapshot ->
                            Vitest.expect(snapshot.treeVersion).toBe vault.FileTreeWriter.TreeVersion
                            Vitest.expect(snapshot.loadedDirectories |> Array.contains "").toBe true
                            Vitest.expect(snapshot.loadedDirectories |> Array.contains "assays/A1").toBe true
                            Vitest.expect(snapshot.loadedDirectories |> Array.contains "assays/A1/dataset").toBe false

                            Vitest
                                .expect(snapshot.entries |> Array.exists (fun e -> e.path = "assays/A1/dataset"))
                                .toBe
                                true

                            Vitest.expect(snapshot.entries |> Array.exists (fun e -> e.path = "")).toBe true
                    })
        )

        Vitest.test (
            "loadDirectory loads a payload folder and answers again for a loaded folder with unchanged children",
            fun () ->
                withRegisteredArc
                    82
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 82)
                        let publishedUpdates = ResizeArray<FileTreeChange>()
                        vault.FileTreeWriter.Publish <- publishedUpdates.Add

                        let names (update: Swate.Electron.Shared.IPCTypes.FileTreeDirectoryUpdateDto) =
                            update.children |> Array.map _.path

                        let! first = api.loadDirectory "assays/A1/dataset"
                        let first = first |> Result.defaultWith (fun error -> failwith error.Message)

                        Vitest.expect(first.directoryPath).toBe "assays/A1/dataset"

                        Vitest
                            .expect(names first)
                            .toEqual (
                                [|
                                    "assays/A1/dataset/.gitkeep"
                                    "assays/A1/dataset/raw.txt"
                                    "assays/A1/dataset/sub"
                                |]
                            )

                        Vitest.expect(first.treeVersion).toBe vault.FileTreeWriter.TreeVersion
                        Vitest.expect(publishedUpdates.Count).toBe 1

                        let! second = api.loadDirectory "assays/A1/dataset"
                        let second = second |> Result.defaultWith (fun error -> failwith error.Message)

                        Vitest.expect(names second).toEqual (names first)
                        Vitest.expect(second.treeVersion).toBe first.treeVersion
                        Vitest.expect(publishedUpdates.Count).toBe 1
                    })
        )

        Vitest.test (
            "loadDirectory reports a missing directory and a path outside the ARC as errors",
            fun () ->
                withRegisteredArc
                    83
                    (fun _ -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 83)
                        let! missing = api.loadDirectory "assays/A1/nothing"
                        let! outside = api.loadDirectory "../elsewhere"
                        Vitest.expect(missing.IsError).toBe true
                        Vitest.expect(outside.IsError).toBe true
                    })
        )

        Vitest.test (
            "revalidateDirectories publishes only changed directories and skips unloaded ones",
            fun () ->
                withRegisteredArc
                    84
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 84)
                        let root = FileTreeCreator.normalizeRootPath vault.path.Value
                        let! _ = api.loadDirectory "assays/A1/dataset"
                        let! _ = api.loadDirectory "assays/A1/dataset/sub"

                        let published = ResizeArray<FileTreeChange>()
                        vault.FileTreeWriter.Publish <- published.Add

                        do!
                            writeAsync (
                                join [|
                                    vault.path.Value
                                    "assays"
                                    "A1"
                                    "dataset"
                                    "second.txt"
                                |]
                            )

                        do! mkdirAsync' (join [| vault.path.Value; "elsewhere" |])

                        let! result =
                            api.revalidateDirectories [|
                                ""
                                "assays/A1/dataset"
                                "assays/A1/dataset/sub"
                                "elsewhere"
                            |]

                        Vitest.expect(result.IsOk).toBe true
                        Vitest.expect(published.Count).toBe 1

                        // The new root entry "elsewhere" changes the root too.
                        Vitest
                            .expect(published.[0].ChangedDirectories |> Array.sort)
                            .toEqual ([| root; root + "/assays/A1/dataset" |])

                        Vitest.expect(vault.FileTreeWriter.Tree.IsLoaded(root + "/elsewhere")).toBe false
                    })
        )

        Vitest.test (
            "revalidateDirectories does nothing while a write is running",
            fun () ->
                withRegisteredArc
                    85
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 85)
                        let! _ = api.loadDirectory "assays/A1/dataset"
                        let gate, release = TestHelpers.deferred ()
                        let write = vault.WithBusyWritingScope(fun () -> gate)

                        do!
                            writeAsync (
                                join [|
                                    vault.path.Value
                                    "assays"
                                    "A1"
                                    "dataset"
                                    "late.txt"
                                |]
                            )

                        let! result = api.revalidateDirectories [| "assays/A1/dataset" |]

                        Vitest.expect(result.IsOk).toBe true

                        Vitest
                            .expect(
                                (vault.FileTreeWriter.Tree.TryFind(
                                    FileTreeCreator.normalizeRootPath vault.path.Value
                                    + "/assays/A1/dataset/late.txt"
                                ))
                                    .IsSome
                            )
                            .toBe
                            false

                        release ()
                        do! write
                    })
        )

        Vitest.test (
            "statFile answers size and time for a file and None for a folder or a missing file",
            fun () ->
                withRegisteredArc
                    86
                    (fun _ -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 86)

                        match! api.statFile "assays/A1/dataset/raw.txt" with
                        | Ok(Some stat) ->
                            Vitest.expect(stat.sizeBytes).toBe 1.0
                            Vitest.expect(stat.mtimeMs > 0.0).toBe true
                        | other -> failwith $"Unexpected stat result: {other}"

                        let! folder = api.statFile "assays/A1/dataset"
                        let! missing = api.statFile "assays/A1/dataset/none.txt"
                        let! outside = api.statFile "../x"
                        Vitest.expect(folder).toEqual (Ok None)
                        Vitest.expect(missing).toEqual (Ok None)
                        Vitest.expect(outside.IsError).toBe true
                    })
        )

        Vitest.test (
            "readNotes finds a nested note without scanning the file tree",
            fun () ->
                withRegisteredArc
                    87
                    (fun vault -> promise {
                        let noteFolder = join [| vault.path.Value; "notes"; "2026-04-27"; "nested" |]
                        do! mkdirAsync' noteFolder

                        do!
                            writeFileAsync
                                (join [| noteFolder; "note.md" |])
                                "---\ntitle: Nested note\ndate: 2026-04-27T00:00:00.0000000\n---\n\nBody\n"
                                TextEncoding.Utf8

                        let treeCountBefore = vault.FileTreeWriter.Tree.Count
                        let versionBefore = vault.FileTreeWriter.TreeVersion
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 87)

                        match! api.readNotes () with
                        | Error error -> failwith error.Message
                        | Ok notes ->
                            Vitest.expect(notes.Length).toBe 1
                            Vitest.expect(notes.[0].RelativePath).toBe "notes/2026-04-27/nested/note.md"

                        Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe treeCountBefore
                        Vitest.expect(vault.FileTreeWriter.TreeVersion).toBe versionBefore
                    })
        )

        Vitest.test (
            "an entity rename is visible in the tree when the handler returns",
            fun () ->
                withRegisteredArc
                    88
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 88)
                        let root = FileTreeCreator.normalizeRootPath vault.path.Value

                        let! renamed =
                            api.renamePath {
                                relativePath = "assays/A1"
                                newName = "A2"
                            }

                        Vitest.expect(renamed.IsOk).toBe true
                        let tree = vault.FileTreeWriter.Tree
                        Vitest.expect((tree.TryFind(root + "/assays/A1")).IsSome).toBe false
                        Vitest.expect(tree.IsLoaded(root + "/assays/A2")).toBe true
                        Vitest.expect((tree.TryFind(root + "/assays/A2/isa.assay.xlsx")).IsSome).toBe true
                    })
        )

        Vitest.test (
            "an entity delete is visible in the tree when the handler returns",
            fun () ->
                withRegisteredArc
                    89
                    (fun vault -> promise {
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent 89)
                        let root = FileTreeCreator.normalizeRootPath vault.path.Value

                        let! deleted = api.deletePath "assays/A1"

                        Vitest.expect(deleted.IsOk).toBe true
                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(root + "/assays/A1")).IsSome).toBe false
                    })
        )
)
