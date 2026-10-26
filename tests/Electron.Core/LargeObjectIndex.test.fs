module ElectronCore.LargeObjectIndexTests

open Fable.Core
open Main.LargeObjectIndex
open Swate.Electron.Shared.IPCTypes
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

let private entryPaths (index: LargeObjectIndex) =
    (index.Snapshot false).entries |> Array.map (fun dto -> dto.Path) |> Array.sort

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
                Vitest.expect((index.Snapshot false).state).toBe LargeObjectIndexStatus.Ready
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

                Vitest.expect(entryPaths index).toEqual [| "a.bin" |]
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
                Vitest.expect(entryPaths index).toEqual [||]
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

        Vitest.test (
            "CancelAndWaitAsync resolves when the listing call returns, or after the cap",
            fun () -> promise {
                let fake, _, index = create false 60000
                index.Invalidate()
                do! settle ()

                let returned = ref false

                let waiting =
                    index.CancelAndWaitAsync 5000 |> Promise.map (fun () -> returned.Value <- true)

                do! settle ()
                Vitest.expect(fake.Calls.[0].Context.Cancellation.IsCancellationRequested()).toBe true
                Vitest.expect(returned.Value).toBe false

                fake.Calls.[0].Resolve(Error "canceled")
                do! waiting
                Vitest.expect(returned.Value).toBe true

                let fake2, _, index2 = create false 60000
                index2.Invalidate()
                do! settle ()
                let capped = ref false

                let cappedWait =
                    index2.CancelAndWaitAsync 30 |> Promise.map (fun () -> capped.Value <- true)

                do! wait 100
                Vitest.expect(capped.Value).toBe true
                Vitest.expect(fake2.Calls.Count).toBe 1
                do! cappedWait
            }
        )

        Vitest.test (
            "an Invalidate from a microtask after the last run finished starts another run",
            fun () -> promise {
                for hops in 0..6 do
                    let fake, _, index = create false 60000
                    let fired = ref false

                    let rec hop (remaining: int) =
                        if remaining = 0 then
                            index.Invalidate()
                        else
                            JS.Constructors.Promise.resolve ()
                            |> Promise.iter (fun () -> hop (remaining - 1))

                    index.Subscribe(fun () ->
                        match index.State with
                        | Ready _ when not fired.Value ->
                            fired.Value <- true
                            hop hops
                        | _ -> ()
                    )
                    |> ignore

                    index.Invalidate()
                    do! settle ()
                    fake.Calls.[0].Resolve(Ok [||])
                    do! settle ()
                    Vitest.expect(fake.Calls.Count).toBe 2
                    index.Dispose()
            }
        )

        Vitest.test (
            "a seeded index keeps showing the ready state while the replacement lists",
            fun () -> promise {
                let fake = FakeListing()

                let seed: ReadyIndex = {
                    Version = 4
                    CaseInsensitive = false
                    Entries = [| entry "a.bin" |]
                    LastError = None
                }

                let index =
                    LargeObjectIndex(fake.List, (fun () -> false), ignore, 60000, 4, Some seed)

                let states = ResizeArray<LargeObjectIndexStatus>()
                index.Subscribe(fun () -> states.Add (index.Snapshot false).state) |> ignore

                Vitest.expect((index.Snapshot false).state).toBe LargeObjectIndexStatus.Ready
                Vitest.expect(index.Version).toBe 4

                index.Invalidate()
                do! settle ()
                Vitest.expect((index.Snapshot false).state).toBe LargeObjectIndexStatus.Ready
                Vitest.expect(states.Count).toBe 0

                fake.Calls.[0].Resolve(Ok [| entry "b.bin" |])
                do! settle ()
                Vitest.expect(index.Version).toBe 5
                Vitest.expect(entryPaths index).toEqual [| "b.bin" |]
                Vitest.expect(states.Count).toBe 1
            }
        )

)
