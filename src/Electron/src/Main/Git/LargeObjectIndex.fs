module Main.LargeObjectIndex

open Fable.Core
open Main.Bindings.PromiseRace
open Main.VersionControl
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions

/// Index built from one successful listing.
type ReadyIndex = {
    Version: int
    /// Case flag the renderer keys the entries with.
    CaseInsensitive: bool
    Entries: ObjectStateDto[]
    LastError: string option
}

type LargeObjectIndexState =
    | NotLoaded
    | Loading
    | Ready of ReadyIndex
    | Unavailable of reason: string

/// Lists the LFS objects of a repository through its version-control session. The session is opened when
/// it is not hosted yet.
let listObjectsFromSession
    (repoRoot: string)
    (context: OperationContext)
    : JS.Promise<Result<ObjectStateDto[], string>> =
    promise {
        try
            let host = WorkspaceSessionHost.get ()
            let! opened = host.OpenSession(repoRoot, context) |> Async.StartAsPromise

            match opened with
            | Failed failure -> return Error failure.Message
            | Succeeded outcome
            | PartiallySucceeded(outcome, _) ->
                match outcome.Value.Session.ObjectMaterialization with
                | None -> return Error "Object materialization is not available for this repository."
                | Some materialization ->
                    let! listed = materialization.ListObjects context |> Async.StartAsPromise

                    match listed with
                    | Succeeded listing
                    | PartiallySucceeded(listing, _) -> return Ok(listing.Value |> Array.map Mappings.objectState)
                    | Failed failure -> return Error failure.Message
        with error ->
            return Error error.Message
    }

[<Literal>]
let DefaultTimeLimitMs = 120000

/// Memoized LFS state of one vault. At most one listing runs at a time. An invalidation during a run
/// triggers exactly one more run afterwards. A failed run keeps the previous index and is not retried
/// until the next invalidation.
type LargeObjectIndex
    (
        listObjects: OperationContext -> JS.Promise<Result<ObjectStateDto[], string>>,
        isCaseInsensitive: unit -> bool,
        log: string -> unit,
        timeLimitMs: int,
        startVersion: int,
        initialReady: ReadyIndex option
    ) =
    let mutable state =
        match initialReady with
        | Some ready -> Ready ready
        | None -> NotLoaded

    let mutable version = startVersion
    let mutable running = false
    let mutable dirty = false
    let mutable disposed = false
    let mutable currentSource: OperationCancellation.Source option = None
    let mutable inFlight: JS.Promise<unit> option = None
    let mutable cancelReason = "listing canceled"
    let mutable lastLogged: string option = None
    let listeners = ResizeArray<unit -> unit>()

    let notify () =
        for listener in listeners.ToArray() do
            try
                listener ()
            with error ->
                log $"Large object index listener failed: {error.Message}"

    let setState next =
        state <- next
        notify ()

    let logOnce (message: string) =
        if lastLogged <> Some message then
            lastLogged <- Some message
            log $"Large object listing failed: {message}"

    let buildIndex (caseInsensitive: bool) (entries: ObjectStateDto[]) : ReadyIndex = {
        Version = version
        CaseInsensitive = caseInsensitive
        Entries = entries
        LastError = None
    }

    let runOnce () : JS.Promise<unit> = promise {
        let source = OperationCancellation.Source()
        currentSource <- Some source
        cancelReason <- "listing canceled"

        let context =
            OperationContext.create "large-object-index" source.Cancellation ignore

        // A rebuild keeps showing the previous index.
        match state with
        | Ready _ -> ()
        | _ -> setState Loading

        // Settles when the listing is canceled, so a provider that ignores cancellation cannot hang the run.
        let canceled: JS.Promise<Result<ObjectStateDto[], string>> =
            Promise.create (fun resolve _ -> source.Cancellation.Register(fun () -> resolve (Error cancelReason)))

        let timer =
            JS.setTimeout
                (fun () ->
                    cancelReason <- $"listing timed out after {float timeLimitMs / 1000.0} s"
                    source.Cancel()
                )
                timeLimitMs

        // The race below settles on cancellation, while this promise settles when listObjects itself returns.
        let listing = promise {
            try
                return! listObjects context
            with error ->
                return Error error.Message
        }

        let settled = listing |> Promise.map ignore
        inFlight <- Some settled

        settled
        |> Promise.iter (fun () ->
            if inFlight |> Option.exists (fun current -> obj.ReferenceEquals(current, settled)) then
                inFlight <- None
        )

        let! outcome = race [| listing; canceled |]

        JS.clearTimeout timer
        currentSource <- None

        if not disposed then
            match outcome with
            | Ok entries ->
                version <- version + 1
                lastLogged <- None
                setState (Ready(buildIndex (isCaseInsensitive ()) entries))
            | Error message ->
                logOnce message

                match state with
                | Ready index -> setState (Ready { index with LastError = Some message })
                | _ -> setState (Unavailable message)
    }

    // The flag is cleared in the same step that finds no more work, so an Invalidate after that step starts a new loop.
    let rec startLoop () : unit =
        running <- true
        dirty <- true
        loop ()

    and loop () : unit =
        if dirty && not disposed then
            dirty <- false

            runOnce ()
            |> Promise.either
                (fun () -> loop ())
                (fun error ->
                    log $"Large object index run failed: {error.Message}"
                    loop ()
                )
            |> ignore
        else
            running <- false

    /// Starts at version 0 without a ready index.
    new(listObjects, isCaseInsensitive, log, timeLimitMs) =
        LargeObjectIndex(listObjects, isCaseInsensitive, log, timeLimitMs, 0, None)

    static member Create(listObjects, isCaseInsensitive, log) =
        LargeObjectIndex(listObjects, isCaseInsensitive, log, DefaultTimeLimitMs, 0, None)

    member _.State = state
    member _.Version = version

    /// Starts a listing when the index was never built. Later calls are no-ops.
    member _.EnsureBuilt() =
        match state with
        | NotLoaded when not disposed && not running -> startLoop ()
        | _ -> ()

    /// Marks the index stale and rebuilds it in the background.
    member _.Invalidate() =
        if not disposed then
            if running then dirty <- true else startLoop ()

    /// Calls the listener after every state change. The returned function removes it.
    member _.Subscribe(listener: unit -> unit) : unit -> unit =
        listeners.Add listener
        fun () -> listeners.Remove listener |> ignore

    /// The vault flag is the fallback while no index is ready.
    member _.Snapshot(vaultCaseInsensitive: bool) : LargeObjectIndexSnapshot =
        let name, caseInsensitive, entries =
            match state with
            | NotLoaded -> LargeObjectIndexStatus.NotLoaded, vaultCaseInsensitive, [||]
            | Loading -> LargeObjectIndexStatus.Loading, vaultCaseInsensitive, [||]
            | Ready index -> LargeObjectIndexStatus.Ready, index.CaseInsensitive, index.Entries
            | Unavailable _ -> LargeObjectIndexStatus.Unavailable, vaultCaseInsensitive, [||]

        {|
            version = version
            state = name
            caseInsensitive = caseInsensitive
            entries = entries
        |}

    /// Cancels the running listing and resolves when the listObjects call itself has returned, or after capMs.
    /// The call can still own a git process with the ARC folder as its working directory when it is canceled.
    member _.CancelAndWaitAsync(capMs: int) : JS.Promise<unit> = promise {
        match currentSource with
        | Some source -> source.Cancel()
        | None -> ()

        match inFlight with
        | None -> ()
        | Some settled ->
            let timer = ref None

            let cap =
                Promise.create (fun resolve _ -> timer.Value <- Some(JS.setTimeout (fun () -> resolve ()) capMs))

            do! race [| settled; cap |]
            timer.Value |> Option.iter JS.clearTimeout
    }

    /// Cancels a running listing and blocks further runs. Called when the vault closes.
    member _.Dispose() =
        disposed <- true
        listeners.Clear()

        match currentSource with
        | Some source ->
            cancelReason <- "vault closed"
            source.Cancel()
        | None -> ()
