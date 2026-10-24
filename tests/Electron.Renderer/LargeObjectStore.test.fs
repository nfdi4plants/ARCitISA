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
            "finds an NFD name through the comparison key of the NFC name",
            fun () ->
                let nfc = "Messung_ä.csv"
                let nfd = "Messung_ä.csv"
                let store = LargeObjectStore()
                store.Apply(snapshot 1 LargeObjectIndexStatus.Ready false [| entry nfc |])

                Vitest.expect(pathOf (store.TryFind nfd)).toEqual (Some nfc)
        )

        Vitest.test (
            "does not match a path through a comparison key that two entries share",
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
