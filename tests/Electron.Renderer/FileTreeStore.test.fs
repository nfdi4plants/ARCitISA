module ElectronRenderer.FileTreeStoreTests

open Renderer.Context.FileTreeStore
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
open Vitest

let private nameOf (path: string) =
    path.Substring(path.LastIndexOf '/' + 1)

let private file path =
    FileEntry.create (nameOf path, path, false)

let private dir path =
    FileEntry.create (nameOf path, path, true)

let private snapshotInput version caseInsensitive : FileTreeSnapshotDto = {|
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
    fileWatcher = {|
        state = FileWatcherStatus.Running
        reason = ""
    |}
|}

/// One writer job can change several directories, and the update lists all of them with one version.
let private updateMany version (directories: (string * FileEntry[]) list) : FileTreeDirectoryUpdateDto = {|
    treeVersion = version
    directories =
        directories
        |> List.map (fun (directory, entries) ->
            let entry: FileTreeDirectoryChildrenDto = {|
                directoryPath = directory
                children = entries
            |}

            entry
        )
        |> Array.ofList
|}

let private update version directory children : FileTreeDirectoryUpdateDto =
    updateMany version [ directory, children ]

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
            "drops updates with an older treeVersion and applies the same version",
            fun () ->
                let store = loadedStore ()
                let before = store.Snapshot
                store.ApplyDirectoryUpdate(update 4 "src" [| file "src/c.txt" |])
                Vitest.expect(obj.ReferenceEquals(store.Snapshot, before)).toBe (true)

                // One writer version is one state of the tree, so the same version applies.
                store.ApplyDirectoryUpdate(update 5 "src" [| file "src/c.txt" |])
                Vitest.expect(names (children store.Snapshot "src")).toEqual ([| "c.txt" |])
        )

        Vitest.test (
            "applies every directory of one update, parents first, however they are listed",
            fun () ->
                let store = loadedStore ()

                // A new entity folder and its zone folder from one job. The child comes first in the input.
                store.ApplyDirectoryUpdate(
                    updateMany 6 [
                        "src/fresh", [| file "src/fresh/isa.xlsx" |]
                        "src", [| dir "src/fresh"; dir "src/inner"; file "src/b.txt" |]
                    ]
                )

                Vitest.expect(names (children store.Snapshot "src/fresh")).toEqual ([| "isa.xlsx" |])
                Vitest.expect(isLoaded store.Snapshot "src/fresh").toBe (true)
                Vitest.expect(store.Snapshot.TreeVersion).toBe 6
        )

        Vitest.test (
            "notifies once for an update with several directories",
            fun () ->
                let store = loadedStore ()
                let notifications = ref 0

                store.Subscribe(fun () -> notifications.Value <- notifications.Value + 1)
                |> ignore

                store.ApplyDirectoryUpdate(
                    updateMany 6 [
                        "src", [| dir "src/inner"; file "src/b.txt"; file "src/c.txt" |]
                        "src/inner", [| file "src/inner/y.txt" |]
                    ]
                )

                Vitest.expect(notifications.Value).toBe 1
        )

        Vitest.test (
            "a loadDirectory answer at the current version applies",
            fun () ->
                let store = loadedStore ()
                store.ApplyDirectoryUpdate(update 6 "src" [| dir "src/inner"; file "src/b.txt"; file "src/c.txt" |])

                // The answer of a load carries the writer version that the push of the same job carried.
                store.ApplyDirectoryUpdate(update 6 "src/inner" [| file "src/inner/z.txt" |])
                Vitest.expect(names (children store.Snapshot "src/inner")).toEqual ([| "z.txt" |])
        )

        Vitest.test (
            "buffers updates during a load and applies the ones at or above the snapshot version",
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
            "a folder that gets another spelling loses its old content and loaded mark",
            fun () ->
                // The snapshot ignores case, so src/inner and src/Inner are the same folder with another spelling.
                let store = loadedStore ()
                Vitest.expect(isLoaded store.Snapshot "src/inner").toBe true

                store.ApplyDirectoryUpdate(update 6 "src" [| dir "src/Inner"; file "src/b.txt" |])

                Vitest.expect(isLoaded store.Snapshot "src/Inner").toBe false
                Vitest.expect((tryFind store.Snapshot "src/inner/x.txt").IsNone).toBe true

                // The update of the renamed folder follows with its children.
                store.ApplyDirectoryUpdate(update 6 "src/Inner" [| file "src/Inner/x.txt" |])
                Vitest.expect(isLoaded store.Snapshot "src/Inner").toBe true
                Vitest.expect((tryFind store.Snapshot "src/inner/x.txt").IsSome).toBe true
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

                store.ApplySnapshot {|
                    treeVersion = 1
                    caseInsensitive = false
                    entries = [|
                        dir "src"
                        dir "Data"
                        file "src/b.txt"
                        file "Data/y.txt"
                    |]
                    loadedDirectories = [| ""; "src" |]
                    fileWatcher = {|
                        state = FileWatcherStatus.Running
                        reason = ""
                    |}
                |}

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
