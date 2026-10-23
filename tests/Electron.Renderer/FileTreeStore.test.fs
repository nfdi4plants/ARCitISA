module ElectronRenderer.FileTreeStoreTests

open Renderer.Context.FileTreeStore
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Vitest

let private nameOf (path: string) =
    path.Substring(path.LastIndexOf '/' + 1)

let private file path =
    FileEntry.create (nameOf path, path, false)

let private dir path =
    FileEntry.create (nameOf path, path, true)

let private snapshotInput version caseInsensitive : FileTreeSnapshotInput = {
    treeVersion = version
    caseInsensitive = caseInsensitive
    entries = [|
        dir "src"
        dir "Data"
        file "a.txt"
        dir "src/inner"
        file "src/inner/x.txt"
        file "src/b.txt"
    |]
    loadedDirectories = [| ""; "src"; "src/inner" |]
}

let private update version directory children : FileTreeDirectoryUpdateInput = {
    treeVersion = version
    directoryPath = directory
    children = children
}

let private loadedStore () =
    let store = FileTreeStore()
    store.BeginLoad()
    store.ApplySnapshot(snapshotInput 5 true)
    store

let private names (entries: FileEntry[]) =
    entries |> Array.map (fun entry -> entry.name)

Vitest.describe (
    "FileTreeStore",
    fun () ->
        Vitest.test (
            "keeps the snapshot instance when an update changes nothing",
            fun () ->
                let store = loadedStore ()
                let before = store.Snapshot
                store.ApplyDirectoryUpdate(update 6 "src" (children before "src"))
                Vitest.expect(obj.ReferenceEquals(store.Snapshot, before)).toBe (true)
        )

        Vitest.test (
            "increments Version when an update changes the tree",
            fun () ->
                let store = loadedStore ()
                let before = store.Snapshot
                let mutable notified = 0
                store.Subscribe(fun () -> notified <- notified + 1) |> ignore
                store.ApplyDirectoryUpdate(update 6 "src" [| dir "src/inner"; file "src/c.txt" |])
                Vitest.expect(store.Snapshot.Version).toBe (before.Version + 1)
                Vitest.expect(store.Snapshot.TreeVersion).toBe (6)
                Vitest.expect(notified).toBe (1)
        )

        Vitest.test (
            "drops updates with an older or equal treeVersion",
            fun () ->
                let store = loadedStore ()
                let before = store.Snapshot
                store.ApplyDirectoryUpdate(update 5 "src" [| file "src/c.txt" |])
                store.ApplyDirectoryUpdate(update 4 "src" [| file "src/c.txt" |])
                Vitest.expect(obj.ReferenceEquals(store.Snapshot, before)).toBe (true)
        )

        Vitest.test (
            "buffers updates during a load and applies only the newer ones after the snapshot",
            fun () ->
                let store = FileTreeStore()
                store.BeginLoad()
                store.ApplyDirectoryUpdate(update 3 "src" [| file "src/old.txt" |])
                store.ApplyDirectoryUpdate(update 7 "src" [| file "src/new.txt" |])
                Vitest.expect(store.Snapshot.Entries.IsEmpty).toBe (true)
                store.ApplySnapshot(snapshotInput 5 true)
                Vitest.expect(names (children store.Snapshot "src")).toEqual ([| "new.txt" |])
                Vitest.expect(store.Snapshot.TreeVersion).toBe (7)
        )

        Vitest.test (
            "does not freeze later updates after a failed load",
            fun () ->
                let store = loadedStore ()
                store.BeginLoad()
                store.ApplyDirectoryUpdate(update 6 "src" [| file "src/lost.txt" |])
                store.FailLoad()
                Vitest.expect(names (children store.Snapshot "src")).toEqual ([| "inner"; "b.txt" |])
                store.ApplyDirectoryUpdate(update 8 "src" [| file "src/later.txt" |])
                Vitest.expect(names (children store.Snapshot "src")).toEqual ([| "later.txt" |])
        )

        Vitest.test (
            "ignores updates for directories the tree does not know",
            fun () ->
                let store = loadedStore ()
                let before = store.Snapshot
                store.ApplyDirectoryUpdate(update 6 "missing" [| file "missing/c.txt" |])
                store.ApplyDirectoryUpdate(update 7 "a.txt" [| file "a.txt/c.txt" |])
                Vitest.expect(obj.ReferenceEquals(store.Snapshot, before)).toBe (true)
        )

        Vitest.test (
            "removes the subtree of a vanished child directory",
            fun () ->
                let store = loadedStore ()
                store.ApplyDirectoryUpdate(update 6 "src" [| file "src/b.txt" |])
                let snapshot = store.Snapshot
                Vitest.expect((tryFind snapshot "src/inner").IsNone).toBe (true)
                Vitest.expect((tryFind snapshot "src/inner/x.txt").IsNone).toBe (true)
                Vitest.expect(snapshot.Children.ContainsKey(PathKey "src/inner")).toBe (false)
                Vitest.expect(isLoaded snapshot "src/inner").toBe (false)
                Vitest.expect(isLoaded snapshot "src").toBe (true)
        )

        Vitest.test (
            "keeps the previous instance of a structurally equal child",
            fun () ->
                let store = loadedStore ()
                let before = (tryFind store.Snapshot "src/b.txt").Value
                store.ApplyDirectoryUpdate(update 6 "src" [| dir "src/inner"; file "src/b.txt"; file "src/c.txt" |])
                let after = (tryFind store.Snapshot "src/b.txt").Value
                Vitest.expect(obj.ReferenceEquals(before, after)).toBe (true)
        )

        Vitest.test (
            "sorts children with directories first and names ignoring case",
            fun () ->
                let store = loadedStore ()

                store.ApplyDirectoryUpdate(
                    update 6 "" [|
                        file "b.txt"
                        dir "zeta"
                        file "A.txt"
                        dir "Alpha"
                        dir "src"
                        dir "Data"
                    |]
                )

                Vitest
                    .expect(names (children store.Snapshot ""))
                    .toEqual ([| "Alpha"; "Data"; "src"; "zeta"; "A.txt"; "b.txt" |])
        )

        Vitest.test (
            "reports Present, Absent and Unknown",
            fun () ->
                let store = FileTreeStore()
                store.BeginLoad()

                store.ApplySnapshot {
                    treeVersion = 1
                    caseInsensitive = false
                    entries = [|
                        dir "src"
                        dir "Data"
                        file "src/b.txt"
                        file "Data/y.txt"
                    |]
                    loadedDirectories = [| ""; "src" |]
                }

                let snapshot = store.Snapshot

                match knows snapshot "src/b.txt" with
                | Present entry -> Vitest.expect(entry.name).toBe ("b.txt")
                | _ -> failwith "expected Present"

                Vitest.expect(knows snapshot "src/gone.txt" = Absent).toBe (true)
                Vitest.expect(knows snapshot "Data/z.txt" = Unknown).toBe (true)
                Vitest.expect(knows snapshot "Data/deep/z.txt" = Unknown).toBe (true)
        )

        Vitest.test (
            "finds entries by a different case only when the file system ignores case",
            fun () ->
                let insensitive = FileTreeStore()
                insensitive.ApplySnapshot(snapshotInput 1 true)
                let sensitive = FileTreeStore()
                sensitive.ApplySnapshot(snapshotInput 1 false)
                Vitest.expect((tryFind insensitive.Snapshot "data").IsSome).toBe (true)
                Vitest.expect((tryFind sensitive.Snapshot "data").IsSome).toBe (false)
                Vitest.expect((tryFind sensitive.Snapshot "Data").IsSome).toBe (true)
        )
)
