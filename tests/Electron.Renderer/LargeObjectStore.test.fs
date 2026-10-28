module ElectronRenderer.LargeObjectStoreTests

open Renderer.Context.LargeObjectStore
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes
open Vitest

let private entry (path: string) : ObjectStateDto = {
    Path = path
    IsMaterialized = false
    IsLocallyAvailable = false
    SizeBytes = None
    ObjectId = None
}

let private snapshot
    (version: int)
    (state: LargeObjectIndexStatus)
    (caseInsensitive: bool)
    (entries: ObjectStateDto[])
    : LargeObjectIndexSnapshot =
    {|
        version = version
        state = state
        caseInsensitive = caseInsensitive
        entries = entries
    |}

let private pathOf (found: ObjectStateDto option) =
    found |> Option.map (fun dto -> dto.Path)

Vitest.describe (
    "LargeObjectStore",
    fun () ->
        Vitest.test (
            "ignores a snapshot with a lower version",
            fun () ->
                let store = LargeObjectStore()
                store.Apply(snapshot 2 LargeObjectIndexStatus.Ready false [| entry "a.bin" |])
                store.Apply(snapshot 1 LargeObjectIndexStatus.Ready false [||])

                Vitest.expect(store.Snapshot.Version).toBe 2
                Vitest.expect(pathOf (store.TryFind "a.bin")).toEqual (Some "a.bin")
        )

        Vitest.test (
            "applies a snapshot with the same version and a different state, and ignores one with the same state",
            fun () ->
                let store = LargeObjectStore()
                let mutable notifications = 0
                store.Subscribe(fun () -> notifications <- notifications + 1) |> ignore

                store.Apply(snapshot 0 LargeObjectIndexStatus.Loading false [||])
                store.Apply(snapshot 0 LargeObjectIndexStatus.Ready false [| entry "a.bin" |])
                store.Apply(snapshot 0 LargeObjectIndexStatus.Ready false [| entry "b.bin" |])
                store.Apply(snapshot 0 LargeObjectIndexStatus.NotLoaded false [||])

                Vitest.expect(store.Snapshot.State).toBe LargeObjectIndexStatus.Ready
                Vitest.expect(pathOf (store.TryFind "a.bin")).toEqual (Some "a.bin")
                Vitest.expect(notifications).toBe 2
        )

        Vitest.test (
            "finds a path in a different case when the file system ignores case",
            fun () ->
                let store = LargeObjectStore()
                store.Apply(snapshot 1 LargeObjectIndexStatus.Ready true [| entry "Data/Reads.fastq" |])

                Vitest.expect(pathOf (store.TryFind "data/reads.FASTQ")).toEqual (Some "Data/Reads.fastq")
        )

        Vitest.test (
            "does not give Data.csv the state of data.csv on a case-sensitive file system",
            fun () ->
                let store = LargeObjectStore()
                store.Apply(snapshot 1 LargeObjectIndexStatus.Ready false [| entry "data.csv" |])

                Vitest.expect(pathOf (store.TryFind "Data.csv")).toEqual None
                Vitest.expect(pathOf (store.TryFind "data.csv")).toEqual (Some "data.csv")
        )

        Vitest.test (
            "finds an NFD name through the path key of the NFC name",
            fun () ->
                let nfc = "Messung_ä.csv"
                let nfd = "Messung_ä.csv"
                let store = LargeObjectStore()
                store.Apply(snapshot 1 LargeObjectIndexStatus.Ready false [| entry nfc |])

                Vitest.expect(pathOf (store.TryFind nfd)).toEqual (Some nfc)
        )

        Vitest.test (
            "keeps two entries that differ only in case apart on a case-sensitive file system",
            fun () ->
                let store = LargeObjectStore()
                store.Apply(snapshot 1 LargeObjectIndexStatus.Ready false [| entry "data.csv"; entry "Data.csv" |])

                Vitest.expect(pathOf (store.TryFind "data.csv")).toEqual (Some "data.csv")
                Vitest.expect(pathOf (store.TryFind "Data.csv")).toEqual (Some "Data.csv")
                Vitest.expect(pathOf (store.TryFind "DaTa.csv")).toEqual None
        )

        Vitest.test (
            "Clear forgets the entries and accepts a snapshot of a lower version afterwards",
            fun () ->
                let store = LargeObjectStore()
                store.Apply(snapshot 3 LargeObjectIndexStatus.Ready false [| entry "a.bin" |])
                store.Clear()

                Vitest.expect(store.TryFind "a.bin").toEqual None
                Vitest.expect(store.Snapshot.State).toBe LargeObjectIndexStatus.NotLoaded

                store.Apply(snapshot 1 LargeObjectIndexStatus.Loading false [||])
                Vitest.expect(store.Snapshot.State).toBe LargeObjectIndexStatus.Loading
        )
)

let private entryWith (path: string) (isLocallyAvailable: bool) (isMaterialized: bool) : ObjectStateDto = {
    entry path with
        IsLocallyAvailable = isLocallyAvailable
        IsMaterialized = isMaterialized
}

let private folderObjectsOnVolume (caseInsensitive: bool) (folderPath: string) (entries: ObjectStateDto[]) =
    LargeObjectSnapshot.ofIpc (snapshot 1 LargeObjectIndexStatus.Ready caseInsensitive entries)
    |> LargeObjectSnapshot.folderObjects folderPath

let private folderObjectsOf = folderObjectsOnVolume false

Vitest.describe (
    "LargeObjectSnapshot.folderObjects",
    fun () ->
        Vitest.test (
            "includes the objects directly in the folder and in nested subfolders",
            fun () ->
                let found =
                    folderObjectsOf "data" [|
                        entryWith "data/a.bin" true true
                        entryWith "data/raw/b.bin" true true
                        entryWith "data/raw/deep/c.bin" false false
                        entryWith "other/d.bin" true true
                    |]

                Vitest
                    .expect(Array.append found.NeedsDownload found.HasLocalCopy)
                    .toEqual ([| "data/raw/deep/c.bin"; "data/a.bin"; "data/raw/b.bin" |])
        )

        Vitest.test (
            "excludes a sibling folder whose name starts with the folder name",
            fun () ->
                let found =
                    folderObjectsOf "data" [|
                        entryWith "data2/a.bin" true true
                        entryWith "data-old/b.bin" false false
                        entryWith "data/c.bin" true true
                    |]

                Vitest.expect(found.HasLocalCopy).toEqual ([| "data/c.bin" |])
                Vitest.expect(found.NeedsDownload.Length).toBe 0
        )

        Vitest.test (
            "splits the objects by download need and local copy, each sorted by path",
            fun () ->
                let found =
                    folderObjectsOf "data" [|
                        entryWith "data/pointer.bin" true false
                        entryWith "data/freed-twin.bin" false true
                        entryWith "data/zz-local.bin" true true
                        entryWith "data/missing.bin" false false
                        entryWith "data/aa-local.bin" true true
                    |]

                Vitest.expect(found.NeedsDownload).toEqual ([| "data/missing.bin"; "data/pointer.bin" |])

                Vitest
                    .expect(found.HasLocalCopy)
                    .toEqual (
                        [|
                            "data/aa-local.bin"
                            "data/freed-twin.bin"
                            "data/zz-local.bin"
                        |]
                    )
        )

        Vitest.test (
            "keeps a path of different case out of the folder on a case-sensitive volume",
            fun () ->
                let found =
                    folderObjectsOnVolume false "data" [|
                        entryWith "Data/x.bin" true true
                        entryWith "data/y.bin" true true
                    |]

                Vitest.expect(found.HasLocalCopy).toEqual ([| "data/y.bin" |])
        )

        Vitest.test (
            "includes a path of different case on a case-insensitive volume",
            fun () ->
                let found =
                    folderObjectsOnVolume true "data" [|
                        entryWith "Data/x.bin" true true
                        entryWith "data/y.bin" true true
                    |]

                Vitest.expect(found.HasLocalCopy).toEqual ([| "Data/x.bin"; "data/y.bin" |])
        )

        Vitest.test (
            "keeps a folder whose name ends in a space apart from the folder without the space",
            fun () ->
                let entries = [|
                    entryWith "data/a.bin" true true
                    entryWith "data /b.bin" true true
                |]

                Vitest.expect((folderObjectsOf "data" entries).HasLocalCopy).toEqual ([| "data/a.bin" |])
                Vitest.expect((folderObjectsOf "data " entries).HasLocalCopy).toEqual ([| "data /b.bin" |])
        )

        Vitest.test (
            "finds the objects of an NFD folder name in NFC listing paths",
            fun () ->
                let found =
                    folderObjectsOf "Müller" [|
                        entryWith "Müller/a.bin" true true
                        entryWith "Mueller/b.bin" true true
                    |]

                Vitest.expect(found.HasLocalCopy).toEqual ([| "Müller/a.bin" |])
        )

        Vitest.test (
            "returns nothing for the empty folder path",
            fun () ->
                let found =
                    folderObjectsOf "" [|
                        entryWith "a.bin" true true
                        entryWith "data/b.bin" false false
                    |]

                Vitest.expect(found.NeedsDownload.Length).toBe 0
                Vitest.expect(found.HasLocalCopy.Length).toBe 0
        )
)
