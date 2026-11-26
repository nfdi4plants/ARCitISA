/// Keeps one opened repository per vault root and tracks running operations so the
/// renderer can cancel them. The host asks the providers which one owns a folder whenever it
/// opens the folder's repository.
module Main.VersionControl.WorkspaceSessionHost

open System
open System.Collections.Generic
open Fable.Core
open Main.VersionControl.VersionControlSettings
open Arcitisa.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions

/// One opened repository of a vault with the provider that opened it, and the app-owned settings.
type HostedSession = {
    SessionId: string
    /// The folder as the app named it at the open. The host finds the entry by it, because a
    /// provider can spell the root of its repository differently (Git resolves links).
    WorkspaceRoot: string
    Provider: ProviderComposition.AppProvider
    Repository: Repository
    Settings: VersionControlSettings
    /// The "Upload right away" checkbox of the git sidebar. It lives next to the settings so it
    /// outlives a renderer reload and ends with the opened repository. It is not a provider setting.
    UploadRightAway: bool
}

/// A registered operation: its context for the library call and the completion
/// callback that removes it from the registry.
type TrackedOperation = {
    Context: OperationContext
    Complete: unit -> unit
}

/// A running operation keeps its workspace root and window id, and a completion that
/// close handling can await.
type private RunningOperation = {
    WorkspaceRoot: string option
    WindowId: int option
    Mutating: bool
    Source: OperationCancellation.Source
    Completion: JS.Promise<unit>
    ResolveCompletion: unit -> unit
}

let private ambiguousFailure (providerIds: ProviderId[]) =
    let providers = providerIds |> Array.map ProviderId.value |> String.concat ", "

    OperationFailure.create
        Validation
        VersionControlCodes.WorkspaceAmbiguous
        $"Several providers claim this workspace ({providers})."

let private unmanagedFailure (workspaceRoot: string) (diagnostics: OperationFailure[]) = {
    OperationFailure.create
        NotFound
        VersionControlCodes.WorkspaceUnmanaged
        "This folder is not a version controlled workspace." with
        Details =
            diagnostics
            |> Array.map (fun diagnostic -> $"{diagnostic.Code}: {diagnostic.Message}")
        AffectedPaths = [| workspaceRoot |]
}

let private sessionNotOpenFailure () =
    OperationFailure.create NotFound VersionControlCodes.SessionUnavailable "The workspace session is not open."

let withValue (value: 'U) (outcome: OperationOutcome<'T>) : OperationOutcome<'U> = {
    Value = value
    Effect = outcome.Effect
    Warnings = outcome.Warnings
    AffectedPaths = outcome.AffectedPaths
    ResultingRevision = outcome.ResultingRevision
    ResultingRepositoryVersion = outcome.ResultingRepositoryVersion
    Publication = outcome.Publication
}

type WorkspaceSessionHost(runtime: VersionControlRuntime.VersionControlRuntime) =

    let sessions = Dictionary<string, HostedSession>()
    let operations = Dictionary<string, RunningOperation>()
    let pendingOpens = Dictionary<string, JS.Promise<OperationResult<HostedSession>>>()
    let closeRequested = HashSet<string>()

    let normalizeRoot (workspaceRoot: string) =
        let normalized = ProviderResolver.normalizePath workspaceRoot

        match runtime.PathCaseSensitivity with
        | CaseSensitive -> normalized
        | CaseInsensitive -> normalized.ToLowerInvariant()

    let sameRoot (left: string) (right: string) =
        ProviderComposition.rootsEqual runtime.PathCaseSensitivity left right

    let tryFindSession (workspaceRoot: string) =
        sessions.Values
        |> Seq.tryFind (fun hosted -> sameRoot hosted.WorkspaceRoot workspaceRoot)

    let closeHostedSession (hosted: HostedSession) = async {
        sessions.Remove hosted.SessionId |> ignore

        try
            do! hosted.Provider.Provider.Close hosted.Repository
        with _ ->
            ()
    }

    /// Keeps a settings value only while that same opened repository is still held. A close that
    /// lands while the provider call runs must not be undone by its continuation. The stored
    /// record is the base, because the checkbox may have changed while the call ran.
    let storeSettingsIfLive (hosted: HostedSession) (settings: VersionControlSettings) =
        match sessions.TryGetValue hosted.SessionId with
        | true, live -> sessions[hosted.SessionId] <- { live with Settings = settings }
        | _ -> ()

    /// Opens the folder with the provider that owns it and starts its settings from the defaults.
    let openFolder (provider: ProviderComposition.AppProvider) (workspaceRoot: string) (context: OperationContext) = async {
        let! opened =
            ProviderComposition.callFunction
                provider.Functions.Open
                "opening a repository"
                (fun openRepository ->
                    openRepository
                        {
                            RepositoryRoot = workspaceRoot
                            ConnectionProfileId = None
                        }
                        context
                )

        let initializeSettings (outcome: OperationOutcome<Repository>) = async {
            let sessionDefaults = runtime.SessionDefaults()

            let hosted = {
                SessionId = Guid.NewGuid().ToString()
                WorkspaceRoot = workspaceRoot
                Provider = provider
                Repository = outcome.Value
                Settings = sessionDefaults.InitialSettings
                UploadRightAway = sessionDefaults.InitialUploadRightAway
            }

            // The app owns these values. Every opened repository starts from the user defaults, or from the
            // built-in ones when the user has none. Provider repository keys only mirror them.
            match hosted.Provider.Functions.StoragePolicy.SetSettings with
            | Implemented setSettings ->
                // A provider that throws here must not lose the repository it just opened,
                // so the exception becomes an ordinary failure of the settings push.
                let! applied = async {
                    try
                        return!
                            setSettings
                                hosted.Repository
                                (VersionControlSettings.toStoragePolicySettings hosted.Settings)
                                context
                    with error ->
                        return
                            Failed(
                                OperationFailure.createRedacted
                                    ProviderError
                                    VersionControlCodes.UnexpectedException
                                    error.Message
                            )
                }

                let settingsFailure =
                    match applied with
                    | Failed failure
                    | PartiallySucceeded(_, failure) ->
                        Browser.Dom.console.error (
                            $"Could not apply default version-control settings ({failure.Code}): {failure.Message}"
                        )

                        Some failure
                    | Succeeded _ -> None

                return withValue hosted outcome, settingsFailure
            | Missing _ -> return withValue hosted outcome, None
        }

        match opened with
        | Succeeded outcome ->
            let! (initialized, settingsFailure) = initializeSettings outcome

            match settingsFailure with
            | Some failure -> return PartiallySucceeded(initialized, failure)
            | None -> return Succeeded initialized
        | PartiallySucceeded(outcome, failure) ->
            let! (initialized, settingsFailure) = initializeSettings outcome

            match settingsFailure with
            | Some settingsFailure ->
                return
                    PartiallySucceeded(
                        initialized,
                        {
                            failure with
                                Details =
                                    Array.append failure.Details [|
                                        $"{settingsFailure.Code}: {settingsFailure.Message}"
                                    |]
                        }
                    )
            | None -> return PartiallySucceeded(initialized, failure)
        | Failed failure -> return Failed failure
    }

    let resolveAndOpen (workspaceRoot: string) (context: OperationContext) = async {
        let! resolution = ProviderComposition.resolveVault runtime.Providers runtime.PathCaseSensitivity workspaceRoot

        match resolution with
        | ProviderComposition.OwnedVault provider -> return! openFolder provider workspaceRoot context
        | ProviderComposition.AmbiguousVault providerIds -> return Failed(ambiguousFailure providerIds)
        | ProviderComposition.UnmanagedVault diagnostics -> return Failed(unmanagedFailure workspaceRoot diagnostics)
    }

    member _.Runtime = runtime

    member _.TryGetSession(workspaceRoot: string) : HostedSession option = tryFindSession workspaceRoot

    member _.GetSettings(workspaceRoot: string) : VersionControlSettings =
        tryFindSession workspaceRoot
        |> Option.map (fun hosted -> hosted.Settings)
        |> Option.defaultWith (fun () -> runtime.SessionDefaults().InitialSettings)

    member _.SetSettings
        (workspaceRoot: string, settings: VersionControlSettings, context: OperationContext)
        : Async<OperationResult<unit>> =
        async {
            match VersionControlSettings.validate settings with
            | Error(code, reason) -> return Failed(OperationFailure.create Validation code reason)
            | Ok settings ->
                match tryFindSession workspaceRoot with
                | None -> return Failed(sessionNotOpenFailure ())
                | Some hosted ->
                    match hosted.Provider.Functions.StoragePolicy.SetSettings with
                    | Implemented setSettings ->
                        let! result =
                            setSettings
                                hosted.Repository
                                (VersionControlSettings.toStoragePolicySettings settings)
                                context

                        match result with
                        | Failed _ -> return result
                        | Succeeded outcome ->
                            storeSettingsIfLive hosted settings
                            return Succeeded(withValue () outcome)
                        | PartiallySucceeded(outcome, failure) ->
                            storeSettingsIfLive hosted settings
                            return PartiallySucceeded(withValue () outcome, failure)
                    | Missing _ ->
                        storeSettingsIfLive hosted settings
                        return OperationResult.succeeded ()
        }

    /// The checkbox value of the open session. A root without a session fails, because
    /// the default would hide that no session holds a value.
    member _.GetUploadRightAway(workspaceRoot: string) : OperationResult<bool> =
        match tryFindSession workspaceRoot with
        | Some hosted -> OperationResult.succeeded hosted.UploadRightAway
        | None -> Failed(sessionNotOpenFailure ())

    /// Stores the checkbox value in memory only. It takes no operation or lock, because the
    /// checkbox is not a workspace change.
    member _.SetUploadRightAway(workspaceRoot: string, value: bool) : OperationResult<unit> =
        match tryFindSession workspaceRoot with
        | Some hosted ->
            sessions[hosted.SessionId] <- { hosted with UploadRightAway = value }
            OperationResult.succeeded ()
        | None -> Failed(sessionNotOpenFailure ())

    /// Returns the open session for the root, or asks the providers which one owns the
    /// root and opens the repository with it. Concurrent callers for one root share a
    /// single open, so a vault never ends up with two opened repositories. The shared
    /// open runs without cancellation, because a cancel of one caller must not fail the
    /// others, and it reports progress and output to the first caller only. Opening is
    /// short (a probe and the open of one repository), so both limits are acceptable.
    member _.OpenSession(workspaceRoot: string, context: OperationContext) : Async<OperationResult<HostedSession>> = async {
        match tryFindSession workspaceRoot with
        | Some hosted -> return OperationResult.succeeded hosted
        | None ->
            let key = normalizeRoot workspaceRoot

            let pending =
                match pendingOpens.TryGetValue key with
                | true, inFlight -> inFlight
                | _ ->
                    let openContext = {
                        context with
                            Cancellation = OperationCancellation.none
                    }

                    let inFlight = promise {
                        try
                            let! opened = resolveAndOpen workspaceRoot openContext |> Async.StartAsPromise
                            let shouldClose = closeRequested.Remove key

                            match opened with
                            | Succeeded outcome
                            | PartiallySucceeded(outcome, _) ->
                                if shouldClose then
                                    do! closeHostedSession outcome.Value |> Async.StartAsPromise

                                    return Failed(sessionNotOpenFailure ())
                                else
                                    sessions[outcome.Value.SessionId] <- outcome.Value
                                    return opened
                            | Failed _ -> return opened
                        finally
                            pendingOpens.Remove key |> ignore
                            closeRequested.Remove key |> ignore
                    }

                    pendingOpens[key] <- inFlight
                    inFlight

            return! Async.AwaitPromise pending
    }

    member _.CloseSession(workspaceRoot: string) : Async<unit> = async {
        let key = normalizeRoot workspaceRoot

        if pendingOpens.ContainsKey key then
            closeRequested.Add key |> ignore

        let matching =
            sessions.Values
            |> Seq.filter (fun hosted -> sameRoot hosted.WorkspaceRoot workspaceRoot)
            |> Seq.toArray

        for hosted in matching do
            do! closeHostedSession hosted
    }

    /// Closes and reopens the repository of the root. Used after a successful Bind, because
    /// an opened repository keeps the location captured at open.
    member this.ReopenSession(workspaceRoot: string, context: OperationContext) = async {
        do! this.CloseSession workspaceRoot
        return! this.OpenSession(workspaceRoot, context)
    }

    /// A vault folder was renamed on disk. The host closes the opened repository of the old root
    /// (if any), and the next open of the new root asks the providers again.
    member this.WorkspaceRenamed(previousRoot: string) : Async<unit> = this.CloseSession previousRoot

    member _.CloseAll() : Async<unit> = async {
        let open' = sessions.Values |> Seq.toArray
        sessions.Clear()

        for hosted in open' do
            try
                do! hosted.Provider.Provider.Close hosted.Repository
            with _ ->
                ()
    }

    /// Registers an operation before any library call so a cancel that arrives before
    /// the first progress event still lands. Each operation retains its workspace root
    /// and window id for lock cleanup and close handling. An id that a running operation
    /// already has is refused, so a call cannot take over the entry of another one.
    /// An operation with a `reportOutput` callback asks the library for raw subprocess
    /// output. Without one the context has no output channel and the library does no
    /// output work.
    member _.TryBeginOperation
        (
            operationId: string,
            workspaceRoot: string option,
            windowId: int option,
            mutating: bool,
            reportProgress: OperationProgress -> unit,
            reportOutput: (OperationOutput[] -> unit) option
        ) : Result<TrackedOperation, OperationFailure> =
        if operations.ContainsKey operationId then
            Error(
                OperationFailure.create
                    Validation
                    VersionControlCodes.OperationIdInUse
                    "Another running operation has the same operation id."
            )
        else
            let source = OperationCancellation.Source()
            let mutable resolveCompletion: unit -> unit = ignore

            let completion: JS.Promise<unit> =
                JS.Constructors.Promise.Create(fun resolve _ -> resolveCompletion <- fun () -> resolve ())

            operations[operationId] <- {
                WorkspaceRoot = workspaceRoot
                WindowId = windowId
                Mutating = mutating
                Source = source
                Completion = completion
                ResolveCompletion = fun () -> resolveCompletion ()
            }

            let context =
                match reportOutput with
                | Some report -> OperationContext.createWithOutput operationId source.Cancellation reportProgress report
                | None -> OperationContext.create operationId source.Cancellation reportProgress

            Ok {
                Context = context
                Complete =
                    fun () ->
                        match operations.TryGetValue operationId with
                        | true, running ->
                            operations.Remove operationId |> ignore
                            running.ResolveCompletion()
                        | _ -> ()
            }

    /// Registers an operation whose id the main process made. A duplicate id throws.
    /// The operation has no output channel.
    member this.BeginOperation
        (
            operationId: string,
            workspaceRoot: string option,
            windowId: int option,
            mutating: bool,
            reportProgress: OperationProgress -> unit
        ) : TrackedOperation =
        match this.TryBeginOperation(operationId, workspaceRoot, windowId, mutating, reportProgress, None) with
        | Ok tracked -> tracked
        | Error failure -> failwith failure.Message

    /// The window that started a running operation. Operations started outside a window
    /// and operations that are not registered have none.
    member _.TryGetOperationWindowId(operationId: string) : int option =
        match operations.TryGetValue operationId with
        | true, running -> running.WindowId
        | _ -> None

    /// Cancels a tracked operation by its operation id when the window that asks started it.
    /// Operations started outside a window match a request without a window.
    member _.Cancel(operationId: string, windowId: int option) : bool =
        match operations.TryGetValue operationId with
        | true, running when running.WindowId = windowId ->
            running.Source.Cancel()
            true
        | _ -> false

    /// Returns operations registered for the queried workspace root.
    member _.RunningOperationIds(workspaceRoot: string) : string[] =
        operations
        |> Seq.filter (fun entry -> entry.Value.WorkspaceRoot |> Option.exists (sameRoot workspaceRoot))
        |> Seq.map (fun entry -> entry.Key)
        |> Seq.toArray

    /// Operations started outside a window have no window id and never match.
    member _.RunningOperationIdsForWindow(windowId: int) : string[] =
        operations
        |> Seq.filter (fun entry -> entry.Value.WindowId = Some windowId)
        |> Seq.map (fun entry -> entry.Key)
        |> Seq.toArray

    /// Mutations started outside a window have no window id and never match.
    member _.RunningMutationIdsForWindow(windowId: int) : string[] =
        operations
        |> Seq.filter (fun entry -> entry.Value.WindowId = Some windowId && entry.Value.Mutating)
        |> Seq.map (fun entry -> entry.Key)
        |> Seq.toArray

    /// Resolves after every operation id that is still registered has completed.
    member _.WhenOperationsComplete(operationIds: string[]) : JS.Promise<unit> =
        let completions =
            operationIds
            |> Array.choose (fun operationId ->
                match operations.TryGetValue operationId with
                | true, running -> Some running.Completion
                | _ -> None
            )

        if completions.Length = 0 then
            JS.Constructors.Promise.resolve ()
        else
            promise {
                for completion in completions do
                    do! completion
            }

    member this.IsIdle(workspaceRoot: string) : bool =
        (this.RunningOperationIds workspaceRoot).Length = 0

let mutable private current: WorkspaceSessionHost option = None

/// Installs the process-wide host. Tests replace it with a host over their own runtime.
let initialize (host: WorkspaceSessionHost) = current <- Some host

/// Clears the process-wide host between tests.
let resetForTests () = current <- None

let get () : WorkspaceSessionHost =
    match current with
    | Some host -> host
    | None -> failwith "The workspace session host has not been initialized."

/// Returns the host only when one was installed. Vault lifecycle hooks use it so they
/// can tolerate failed initialization without building a host.
let tryCurrent () = current

/// Names the window that started the operation of the context, for example "window:3".
/// The text diff pool keeps diff handles apart per window with it. An operation without a
/// registered window, or any call before the host exists, yields "unknown".
let windowOwnerOf (context: OperationContext) : string =
    tryCurrent ()
    |> Option.bind (fun host -> host.TryGetOperationWindowId context.OperationId)
    |> Option.map (fun windowId -> $"window:{windowId}")
    |> Option.defaultValue "unknown"
