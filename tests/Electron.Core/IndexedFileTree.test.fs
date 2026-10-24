module ElectronCore.IndexedFileTreeTests

open Fable.Core
open Main
open Main.ArcVault
open Main.ArcVaultTypes
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Electron.Shared.FileIOTypes
open Vitest

module FileTreeCreator = Main.FileTreeCreator

let private entry (name: string) (directory: string) (isDirectory: bool) =
    FileEntry.create (name, $"{directory}/{name}", isDirectory, None)

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
                Vitest.expect(tree.ReplaceChildren("/arc", [| { child with largeObject = None } |])).toBe (false)
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

                    let! tree = FileTreeCreator.scanFileTreeAsync root false false
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
                    let! tree = FileTreeCreator.scanFileTreeAsync root false false

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

                    let! scanned = FileTreeCreator.scanFileTreeAsync root false false
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
                            let! stale = FileTreeCreator.scanFileTreeAsync root false false
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
                    do! vault.EnqueueFileTreeScan false

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
