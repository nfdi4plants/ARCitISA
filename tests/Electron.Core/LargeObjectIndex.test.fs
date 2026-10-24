module ElectronCore.LargeObjectIndexTests

open Fable.Core
open Main.LargeObjectIndex
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest

type private Call = {
    Context: OperationContext
    Resolve: Result<ObjectStateDto[], string> -> unit
}

/// Listing whose promises the test resolves by hand.
type private FakeListing() =
    member val Calls = ResizeArray<Call>()

    member this.List(context: OperationContext) : JS.Promise<Result<ObjectStateDto[], string>> =
        Promise.create (fun resolve _ -> this.Calls.Add { Context = context; Resolve = resolve })

let private entry (path: string) : ObjectStateDto = {
    Path = path
    IsMaterialized = false
    IsLocallyAvailable = false
    SizeBytes = None
    ObjectId = None
}

let private wait (ms: int) : JS.Promise<unit> =
    Promise.create (fun resolve _ -> JS.setTimeout resolve ms |> ignore)

let private settle () = wait 5

let private create (caseInsensitive: bool) (timeLimitMs: int) =
    let fake = FakeListing()
    let logs = ResizeArray<string>()

    let index =
        LargeObjectIndex(fake.List, (fun () -> caseInsensitive), logs.Add, timeLimitMs)

    fake, logs, index

let private paths (found: ObjectStateDto option) =
    found |> Option.map (fun dto -> dto.Path)

let private lastError (index: LargeObjectIndex) =
    match index.State with
    | Ready ready -> ready.LastError
    | _ -> None

Vitest.describe (
    "LargeObjectIndex",
    fun () ->
        Vitest.test (
            "runs one listing for a burst of invalidations and exactly one follow-up run",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.Invalidate()
                do! settle ()
                index.Invalidate()
                do! settle ()
                index.Invalidate()
                do! settle ()
                do! settle ()
                Vitest.expect(fake.Calls.Count).toBe 1
                Vitest.expect(index.State).toEqual Loading

                index.Invalidate()
                do! settle ()
                index.Invalidate()
                do! settle ()
                fake.Calls.[0].Resolve(Ok [| entry "a.bin" |])
                do! settle ()
                Vitest.expect(fake.Calls.Count).toBe 2
                Vitest.expect(index.Version).toBe 1

                fake.Calls.[1].Resolve(Ok [| entry "a.bin"; entry "b.bin" |])
                do! settle ()
                Vitest.expect(fake.Calls.Count).toBe 2
                Vitest.expect(index.Version).toBe 2
                Vitest.expect((index.Snapshot false).state).toBe "ready"
                Vitest.expect((index.Snapshot false).entries.Length).toBe 2
            }
        )

        Vitest.test (
            "EnsureBuilt starts a listing only while the index was never built",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.EnsureBuilt()
                do! settle ()
                index.EnsureBuilt()
                do! settle ()
                fake.Calls.[0].Resolve(Ok [||])
                do! settle ()
                index.EnsureBuilt()
                do! settle ()
                do! settle ()
                Vitest.expect(fake.Calls.Count).toBe 1
            }
        )

        Vitest.test (
            "finds entries by exact key, case-insensitively when the flag is set, and by NFC for an NFD name",
            fun () -> promise {
                let fake, _, index = create true 60000
                index.Invalidate()
                do! settle ()
                let nfc = "arc/Café.bin"
                let nfd = "arc/Café.bin"
                fake.Calls.[0].Resolve(Ok [| entry "Data/File.TXT"; entry nfc |])
                do! settle ()
                Vitest.expect(paths (index.TryFind "Data/File.TXT")).toEqual (Some "Data/File.TXT")
                Vitest.expect(paths (index.TryFind "data/file.txt")).toEqual (Some "Data/File.TXT")
                Vitest.expect(paths (index.TryFind nfd)).toEqual (Some nfc)
                Vitest.expect(paths (index.TryFind "missing.bin")).toEqual None
            }
        )

        Vitest.test (
            "keeps names that differ by case apart when the flag is not set",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.Invalidate()
                do! settle ()
                fake.Calls.[0].Resolve(Ok [| entry "A.bin"; entry "a.bin" |])
                do! settle ()
                Vitest.expect(paths (index.TryFind "A.bin")).toEqual (Some "A.bin")
                Vitest.expect(paths (index.TryFind "a.bin")).toEqual (Some "a.bin")
            }
        )

        Vitest.test (
            "drops ambiguous comparison keys from the fallback map",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.Invalidate()
                do! settle ()
                fake.Calls.[0].Resolve(Ok [| entry "A.bin"; entry "a.bin"; entry "Solo.bin" |])
                do! settle ()
                Vitest.expect(paths (index.TryFind "A.BIN")).toEqual None
                Vitest.expect(paths (index.TryFind "solo.BIN")).toEqual (Some "Solo.bin")
            }
        )

        Vitest.test (
            "a failing rebuild keeps the previous entries, sets LastError and logs the message once",
            fun () -> promise {
                let fake, logs, index = create false 60000
                index.Invalidate()
                do! settle ()
                fake.Calls.[0].Resolve(Ok [| entry "a.bin" |])
                do! settle ()

                index.Invalidate()
                do! settle ()
                fake.Calls.[1].Resolve(Error "boom")
                do! settle ()
                index.Invalidate()
                do! settle ()
                fake.Calls.[2].Resolve(Error "boom")
                do! settle ()

                Vitest.expect(paths (index.TryFind "a.bin")).toEqual (Some "a.bin")
                Vitest.expect(lastError index).toEqual (Some "boom")
                Vitest.expect(index.Version).toBe 1
                Vitest.expect(logs.Count).toBe 1

                index.Invalidate()
                do! settle ()
                fake.Calls.[3].Resolve(Ok [| entry "a.bin" |])
                do! settle ()
                Vitest.expect(lastError index).toEqual None
                Vitest.expect(index.Version).toBe 2
            }
        )

        Vitest.test (
            "the first failure makes the index unavailable and no retry follows",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.Invalidate()
                do! settle ()
                fake.Calls.[0].Resolve(Error "no git lfs")
                do! settle ()
                Vitest.expect(index.State).toEqual (Unavailable "no git lfs")
                Vitest.expect(paths (index.TryFind "a.bin")).toEqual None
                do! wait 30
                Vitest.expect(fake.Calls.Count).toBe 1
            }
        )

        Vitest.test (
            "Dispose cancels the running listing and blocks further runs",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.Invalidate()
                do! settle ()
                Vitest.expect(fake.Calls.[0].Context.Cancellation.IsCancellationRequested()).toBe false
                index.Dispose()
                Vitest.expect(fake.Calls.[0].Context.Cancellation.IsCancellationRequested()).toBe true
                do! settle ()
                index.Invalidate()
                do! settle ()
                index.EnsureBuilt()
                do! settle ()
                do! settle ()
                Vitest.expect(fake.Calls.Count).toBe 1
                Vitest.expect(index.State).toEqual Loading
            }
        )

        Vitest.test (
            "the time limit cancels the listing and records the timeout",
            fun () -> promise {
                let fake, logs, index = create false 50
                index.Invalidate()
                do! settle ()
                do! wait 200
                Vitest.expect(fake.Calls.[0].Context.Cancellation.IsCancellationRequested()).toBe true
                Vitest.expect(index.State).toEqual (Unavailable "listing timed out after 0.05 s")
                Vitest.expect(logs.Count).toBe 1
                Vitest.expect(fake.Calls.Count).toBe 1
            }
        )

        Vitest.test (
            "Subscribe fires on state changes and the returned function unsubscribes",
            fun () -> promise {
                let fake, _, index = create false 60000
                let fired = ref 0
                let unsubscribe = index.Subscribe(fun () -> fired.Value <- fired.Value + 1)
                index.Invalidate()
                do! settle ()
                Vitest.expect(fired.Value).toBe 1
                fake.Calls.[0].Resolve(Ok [||])
                do! settle ()
                Vitest.expect(fired.Value).toBe 2

                unsubscribe ()
                index.Invalidate()
                do! settle ()
                fake.Calls.[1].Resolve(Ok [||])
                do! settle ()
                Vitest.expect(fired.Value).toBe 2
            }
        )
)
