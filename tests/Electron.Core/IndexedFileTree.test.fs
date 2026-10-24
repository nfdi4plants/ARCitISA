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
                Vitest.expect(tree.ReplaceChildren("/arc", [| child |])).toBe (true)
                let stored = (tree.TryFind "/arc/a.txt").Value
                Vitest.expect(tree.ReplaceChildren("/arc", [| { child with path = child.path } |])).toBe (false)
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

                Vitest.expect(tree.ReplaceChildren("/arc", [| entry "keep.txt" "/arc" false |])).toBe (true)
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

                    let! tree = FileTreeCreator.scanFileTreeAsync root false
                    Vitest.expect((tree.TryFind(root + "/old/inner/f.txt")).IsSome).toBe (true)

                    do! rmAsync (join [| root; "old" |]) (RmOptions(recursive = true, force = true))
                    do! mkdirAsync' (join [| root; "fresh"; "nested" |])
                    do! writeAsync (join [| root; "fresh"; "nested"; "n.txt" |])
                    do! writeAsync (join [| root; "added.txt" |])

                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root

                    Vitest.expect(changed).toBe (true)

                    Vitest
                        .expect(treePaths tree)
                        .toEqual (
                            [|
                                root
                                root + "/added.txt"
                                root + "/fresh"
                                root + "/fresh/nested"
                                root + "/fresh/nested/n.txt"
                            |]
                        )

                    let! unchanged = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root
                    Vitest.expect(unchanged).toBe (false)
                })
        )

        Vitest.test (
            "a missing directory removes its subtree",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! mkdirAsync' (join [| root; "gone" |])
                    do! writeAsync (join [| root; "gone"; "f.txt" |])
                    let! tree = FileTreeCreator.scanFileTreeAsync root false

                    do! rmAsync (join [| root; "gone" |]) (RmOptions(recursive = true, force = true))
                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree (root + "/gone")

                    Vitest.expect(changed).toBe (true)
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

                    let! scanned = FileTreeCreator.scanFileTreeAsync root false
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
            let published = ResizeArray<string[]>()

            let writer =
                FileTreeWriter(false, ignore, (fun tree -> published.Add(treePaths tree)))

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
                        return changed
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
                do! writer.Enqueue(fun _ -> promise { return (failwith "boom": bool) })

                do!
                    writer.Enqueue(fun _ -> promise {
                        ran <- true
                        return true
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
                        return true
                    })

                let later =
                    writer.Enqueue(fun _ -> promise {
                        laterRan <- true
                        return true
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
                            let! stale = FileTreeCreator.scanFileTreeAsync root false
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
                    Vitest.expect(published.Count).toBe 2
                    Vitest.expect(published.[1]).toEqual (expected)
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

                Vitest.expect(changed).toBe (true)
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
                    let! tree = FileTreeCreator.scanFileTreeAsync root true

                    do! rmAsync (join [| root; "assays"; "A" |]) (RmOptions(recursive = true, force = true))
                    do! mkdirAsync' (join [| root; "assays"; "a" |])
                    do! writeAsync (join [| root; "assays"; "a"; "x.txt" |])

                    let! changed = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree (root + "/assays")

                    Vitest.expect(changed).toBe (true)
                    Vitest.expect((tree.TryFind(root + "/assays/a/x.txt")).IsSome).toBe (true)
                    Vitest.expect(tree.IsLoaded(root + "/assays/a")).toBe (true)
                })
        )

        Vitest.test (
            "reconcile loads a directory that replaced a file",
            fun () ->
                withTempFolder (fun root -> promise {
                    do! writeAsync (join [| root; "item" |])
                    let! tree = FileTreeCreator.scanFileTreeAsync root false

                    do! rmAsync (join [| root; "item" |]) (RmOptions(force = true))
                    do! mkdirAsync' (join [| root; "item" |])
                    do! writeAsync (join [| root; "item"; "inner.txt" |])

                    let! _ = FileTreeCreator.reconcileFileTreeDirectoryAsync root tree root

                    Vitest.expect((tree.TryFind(root + "/item/inner.txt")).IsSome).toBe (true)
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
                        Vitest.expect((tree.TryFind(root + "/assays/A1/other/move-me.txt")).IsSome).toBe (true)
                    })
        )
)
