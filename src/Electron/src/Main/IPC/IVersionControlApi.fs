/// Provider-neutral version control IPC. Every handler resolves the vault of the calling
/// window, opens (or reuses) its repository, registers the operation for
/// cancellation, forwards progress to the window and maps the structured result.
/// Mutations run as external operations, so what they change loads through a sync, and they refresh the file tree afterwards.
module Main.IPC.IVersionControlApi

open Fable.Core
open Fable.Electron
open Fable.Electron.Main
open Main
open Main.VersionControl
open Main.VersionControl.VersionControlSettings
open Arcitisa.Electron.Shared.IPCTypes
open Arcitisa.Electron.Shared.IPCTypes.MainToRendererIpc
open Arcitisa.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions

/// Output is None for a call without a window. Its operations ask the library for no output.
type private RendererBridge = {
    Progress: VersionControlProgressDto -> unit
    Started: OperationRequestDto -> unit
    Output: (VersionControlOutputDto -> unit) option
}

let private silentBridge: RendererBridge = {
    Progress = ignore
    Started = ignore
    Output = None
}

/// Coalesces the progress of one operation. The library can report many events per second,
/// and the renderer only needs the latest state of a phase. An event goes out at once when its
/// phase differs from the last forwarded one or when `windowMilliseconds` have passed since
/// the last forward. A pending event of the previous phase goes out first, because it is the
/// final state of that phase. Any other event waits as the pending one (the latest wins) and
/// goes out when the window ends. `Flush` sends the pending event, so the last state of the
/// operation reaches the renderer before the result does.
type ProgressThrottle
    (
        forward: VersionControlProgressDto -> unit,
        now: unit -> float,
        schedule: int -> (unit -> unit) -> (unit -> unit),
        windowMilliseconds: int
    ) =
    let mutable lastPhase: string option = None
    let mutable lastForwardedAt = 0.0
    let mutable pending: VersionControlProgressDto option = None
    let mutable cancelTimer: (unit -> unit) option = None

    let send (progress: VersionControlProgressDto) =
        pending <- None
        cancelTimer |> Option.iter (fun cancel -> cancel ())
        cancelTimer <- None
        lastPhase <- Some progress.PhaseCode
        lastForwardedAt <- now ()
        forward progress

    member _.Report(progress: VersionControlProgressDto) =
        let elapsed = now () - lastForwardedAt

        if lastPhase <> Some progress.PhaseCode then
            pending |> Option.iter send
            send progress
        elif elapsed >= float windowMilliseconds then
            send progress
        else
            pending <- Some progress

            if cancelTimer.IsNone then
                let remaining = windowMilliseconds - int elapsed

                cancelTimer <-
                    Some(
                        schedule
                            remaining
                            (fun () ->
                                cancelTimer <- None
                                pending |> Option.iter send
                            )
                    )

    member _.Flush() = pending |> Option.iter send

/// The throttle of a running operation, on the real clock and timers.
let private newProgressThrottle (forward: VersionControlProgressDto -> unit) =
    ProgressThrottle(
        forward,
        (fun () -> JS.Constructors.Date.now ()),
        (fun milliseconds callback ->
            let timer = JS.setTimeout callback milliseconds
            fun () -> JS.clearTimeout timer
        ),
        100
    )

// Tests replace this delay to avoid waiting ten seconds.
let mutable waitForStaleLock: int -> Async<unit> = Async.Sleep

let private logOperationStarted (operationName: string) (operationId: string) =
    Browser.Dom.console.log ($"[version-control] started operation={operationName} id={operationId}")

let private logOperationFinished
    (operationName: string)
    (operationId: string)
    (result: OperationResult<'T>)
    (durationMilliseconds: int64)
    =
    let resultKind =
        match result with
        | Succeeded _ -> "Succeeded"
        | PartiallySucceeded(_, failure) -> $"PartiallySucceeded failureCode={failure.Code}"
        | Failed failure -> $"Failed failureCode={failure.Code}"

    Browser.Dom.console.log (
        $"[version-control] ended operation={operationName} id={operationId} result={resultKind} durationMs={durationMilliseconds}"
    )

let private bridgeForWindow (window: BrowserWindow) : RendererBridge =
    let send = WindowSend.sender<IVersionControlRendererApi> window

    {
        Progress = fun progress -> send (fun api -> api.versionControlProgress progress)
        Started = fun key -> send (fun api -> api.versionControlOperationStarted key)
        Output = Some(fun output -> send (fun api -> api.versionControlOutput output))
    }

let private tryBridgeFromEvent (event: IpcMainInvokeEvent) =
    windowFromIpcEvent event
    |> Option.map bridgeForWindow
    |> Option.defaultValue silentBridge

/// A session that could not be opened keeps the provider's category and code. A detail
/// line marks that the failure came from opening the session, not from the operation.
let private sessionUnavailable (failure: OperationFailure) : OperationFailure = {
    failure with
        Details = Array.append [| "The workspace session could not be opened." |] failure.Details
}

/// A provider or strategy that throws is reported as a structured failure, so the
/// renderer's result contract holds even then.
let private unexpectedFailure (error: exn) : OperationFailure =
    OperationFailure.createRedacted ProviderError VersionControlCodes.UnexpectedException error.Message

let private failedDto (failure: OperationFailure) : OperationResultDto<'T> =
    OperationResultDto.Failed(Mappings.failure failure)

let private validationFailed (code: string) (message: string) : OperationResult<'T> =
    Failed(OperationFailure.create Validation code message)

let private storagePolicySettingsDto (settings: VersionControlSettings) : StoragePolicySettingsDto = {
    AutoPolicyThresholdMb = Some settings.AutoTrackThresholdMb
    MaterializeLargeObjects = settings.DownloadLargeFiles
    DiffIndexingLimitMb = Some settings.DiffIndexingLimitMb
}

/// Registers and announces one operation before any await, and turns exceptions into failures.
/// An operation id that a running operation already has is answered with a failure and runs nothing.
let private runTracked
    (host: WorkspaceSessionHost.WorkspaceSessionHost)
    (bridge: RendererBridge)
    (operationName: string)
    (operationId: string)
    (workspaceRoot: string option)
    (windowId: int option)
    (mutating: bool)
    (operation: OperationContext -> Async<OperationResult<'T>>)
    : JS.Promise<OperationResult<'T>> =
    promise {
        let throttle = newProgressThrottle bridge.Progress

        // Output is not throttled here, because the library batches it. The bridge sends
        // without an async hop, so the batches the library flushes before the operation
        // completes reach the window before the result does.
        let reportOutput =
            bridge.Output
            |> Option.map (fun send -> fun entries -> send (Mappings.output operationId entries))

        match
            host.TryBeginOperation(
                operationId,
                workspaceRoot,
                windowId,
                mutating,
                (fun progress -> throttle.Report(Mappings.progress operationId progress)),
                reportOutput
            )
        with
        | Error failure ->
            let result: OperationResult<'T> = Failed failure
            logOperationFinished operationName operationId result 0L
            return result
        | Ok tracked ->
            let startedAt = System.DateTime.UtcNow

            if mutating then
                logOperationStarted operationName operationId

            let! result = promise {
                try
                    try
                        bridge.Started { OperationId = operationId }
                        return! operation tracked.Context |> Async.StartAsPromise
                    with error ->
                        return Failed(unexpectedFailure error)
                finally
                    // The last pending event goes out before the result does.
                    throttle.Flush()
                    tracked.Complete()
            }

            let durationMilliseconds =
                int64 (System.DateTime.UtcNow.Subtract(startedAt).TotalMilliseconds)

            match mutating, result with
            | false, Succeeded _ -> ()
            | _ -> logOperationFinished operationName operationId result durationMilliseconds

            return result
    }

/// Opens the vault session of the calling window and runs the operation in it. The
/// operation is registered and announced before the first await, so a cancel that
/// arrives right after the call started is honored even while the session opens.
let private withSession
    (operationName: string)
    (event: IpcMainInvokeEvent)
    (operationId: string)
    (mutating: bool)
    (operation: WorkspaceSessionHost.HostedSession -> OperationContext -> Async<OperationResult<'T>>)
    (mapValue: 'T -> 'U)
    : JS.Promise<Result<OperationResultDto<'U>, exn>> =
    promise {
        match tryGetVaultAndArcPath event with
        | Error error -> return Error error
        | Ok(_, arcPath) ->
            let host = WorkspaceSessionHost.get ()
            let bridge = tryBridgeFromEvent event

            let! result =
                runTracked
                    host
                    bridge
                    operationName
                    operationId
                    (Some arcPath)
                    (windowFromIpcEvent event |> Option.map _.id)
                    mutating
                    (fun context -> async {
                        let! opened = host.OpenSession(arcPath, context)

                        match opened with
                        | Succeeded outcome ->
                            let hosted = outcome.Value
                            return! operation hosted context
                        | PartiallySucceeded(outcome, openFailure) ->
                            // Only a fresh open is partial, a reuse is not, so the failure of
                            // the open (a rejected settings push, say) reaches the renderer
                            // once, on the call that opened the session.
                            let hosted = outcome.Value
                            let! result = operation hosted context

                            return
                                match result with
                                | Succeeded valueOutcome -> PartiallySucceeded(valueOutcome, openFailure)
                                | other -> other
                        | Failed failure -> return Failed(sessionUnavailable failure)
                    })

            try
                return Ok(Mappings.result mapValue result)
            with error ->
                return Ok(failedDto (unexpectedFailure error))
    }

/// Whether a result reports a change of the workspace, which is when the file tree
/// is refreshed after a mutation.
let resultChangedState (result: Result<OperationResultDto<'U>, exn>) =
    match result with
    | Ok(OperationResultDto.Succeeded outcome) -> outcome.Effect = OperationEffectDto.Performed
    | Ok(OperationResultDto.PartiallySucceeded _) -> true
    | Ok(OperationResultDto.Failed failure) -> failure.StateChanged
    | Error _ -> false

let private shouldRefreshObjectTree (request: ObjectPathsRequestDto) (result: Result<OperationResultDto<unit>, exn>) =
    if request.AlwaysRefreshTree then
        true
    else
        resultChangedState result

/// Same as withSession, with the vault marked busy for the duration and the file tree
/// refreshed afterwards when the result predicate allows it.
let private withMutatingSessionUsingRefreshPredicate
    (operationName: string)
    (event: IpcMainInvokeEvent)
    (operationId: string)
    (shouldRefresh: Result<OperationResultDto<'U>, exn> -> bool)
    (operation: WorkspaceSessionHost.HostedSession -> OperationContext -> Async<OperationResult<'T>>)
    (mapValue: 'T -> 'U)
    : JS.Promise<Result<OperationResultDto<'U>, exn>> =
    promise {
        match tryGetVaultAndArcPath event with
        | Error error -> return Error error
        | Ok(vault, _) ->
            return!
                vault.WithExternalOperationScope(
                    announce = true,
                    operation =
                        fun () -> promise {
                            let! result = withSession operationName event operationId true operation mapValue

                            if shouldRefresh result then
                                do! vault.EnqueueFileTreeRevalidateLoaded()
                                vault.InvalidateLargeObjectIndex()

                            return result
                        }
                )
    }

/// Same as withSession, with the vault marked busy for the duration and the file tree
/// refreshed afterwards when the operation changed the workspace.
let private withMutatingSession
    (operationName: string)
    (event: IpcMainInvokeEvent)
    (operationId: string)
    (refreshTree: bool)
    (operation: WorkspaceSessionHost.HostedSession -> OperationContext -> Async<OperationResult<'T>>)
    (mapValue: 'T -> 'U)
    : JS.Promise<Result<OperationResultDto<'U>, exn>> =
    withMutatingSessionUsingRefreshPredicate
        operationName
        event
        operationId
        (fun result -> refreshTree && resultChangedState result)
        operation
        mapValue

/// Calls one function of the subscribed functions of the provider that opened the repository, with
/// that repository. A function the provider does not have gets the failure the renderer knows for
/// it, with the name of the service in the message.
let private withFunction
    (select: ProviderFunctions -> Entry<'call>)
    (serviceName: string)
    (call: 'call -> Repository -> OperationContext -> Async<OperationResult<'T>>)
    (hosted: WorkspaceSessionHost.HostedSession)
    (context: OperationContext)
    : Async<OperationResult<'T>> =
    ProviderComposition.callFunction
        (select hosted.Provider.Functions)
        serviceName
        (fun entry -> call entry hosted.Repository context)

/// Runs one text diff call in the session of the calling window. An invalid request is
/// answered before the operation is registered, and every failure of the reply is bounded.
/// The calls are reads, so they use the tracked operation without the busy flag.
let private withTextDiff
    (operationName: string)
    (event: IpcMainInvokeEvent)
    (operationId: string)
    (request: Result<'R, OperationFailure>)
    (select: TextDiffFunctions -> Entry<'call>)
    (call: WorkspaceSessionHost.HostedSession -> 'call -> 'R -> OperationContext -> Async<OperationResult<'T>>)
    (mapValue: 'T -> 'U)
    : JS.Promise<Result<OperationResultDto<'U>, exn>> =
    promise {
        match request with
        | Error failure -> return Ok(Mappings.textDiffResult mapValue (Failed failure))
        | Ok converted ->
            let! result =
                withSession
                    operationName
                    event
                    operationId
                    false
                    (fun hosted context ->
                        withFunction
                            (fun functions -> select functions.TextDiff)
                            "text diffs"
                            (fun entry _ serviceContext -> call hosted entry converted serviceContext)
                            hosted
                            context
                    )
                    mapValue

            return result |> Result.map Mappings.boundTextDiffResult
    }

/// The storage policy a diff opened now gets, from the settings of the session of the calling window.
let private diffStoragePolicyOf (event: IpcMainInvokeEvent) : DiffStoragePolicy =
    match tryGetVaultAndArcPath event with
    | Ok(_, arcPath) -> VersionControlSettings.diffStoragePolicy (WorkspaceSessionHost.get().GetSettings arcPath)
    | Error _ ->
        let defaults = WorkspaceSessionHost.get().Runtime.SessionDefaults()
        VersionControlSettings.diffStoragePolicy defaults.InitialSettings

/// The window of the IPC call. Electron supplies it, so the renderer cannot choose it.
let private callingWindowId (event: IpcMainInvokeEvent) : int option =
    windowFromIpcEvent event |> Option.map _.id

let private withPath (path: string) (call: RepositoryPath -> Async<OperationResult<'T>>) : Async<OperationResult<'T>> =
    match Mappings.tryRepositoryPath path with
    | Ok repositoryPath -> call repositoryPath
    | Error failure -> async { return Failed failure }

let private tryGetRegularFileSize (workspaceRoot: string) (relativePath: string) : Async<int64 option> = async {
    try
        let! stats =
            Main.Bindings.Filesystem.statAsync (Main.Bindings.Path.join [| workspaceRoot; relativePath |])
            |> Async.AwaitPromise

        if stats.isFile () then
            return Some(int64 stats.size)
        else
            return None
    with _ ->
        return None
}

let private withPaths (paths: string[]) (call: RepositoryPath[] -> Async<OperationResult<'T>>) =
    match Mappings.tryRepositoryPaths paths with
    | Ok repositoryPaths -> call repositoryPaths
    | Error failure -> async { return Failed failure }

let private withProviderRef (value: string) (call: ProviderRef -> Async<OperationResult<'T>>) =
    match Mappings.tryProviderRef value with
    | Ok reference -> call reference
    | Error failure -> async { return Failed failure }

/// An optional ref or revision the renderer sent is either valid or a validation
/// failure. It is never dropped, because the provider treats these values as exact.
let private withOptionalProviderRef (value: string option) (call: ProviderRef option -> Async<OperationResult<'T>>) =
    match value with
    | None -> call None
    | Some text -> withProviderRef text (Some >> call)

let private withRevision (value: string) (call: RevisionId -> Async<OperationResult<'T>>) =
    match Mappings.tryRevisionId value with
    | Ok revision -> call revision
    | Error failure -> async { return Failed failure }

let private withOptionalRevision (value: string option) (call: RevisionId option -> Async<OperationResult<'T>>) =
    match value with
    | None -> call None
    | Some text -> withRevision text (Some >> call)

/// Closes the repository that Clone, Initialize or Bind opened and answers with the folder the app named.
/// The session host opens the folder later.
let private closeProvisioned
    (provider: ProviderComposition.AppProvider)
    (folder: string)
    (result: OperationResult<Repository>)
    : Async<OperationResult<string>> =
    let closeOutcome (outcome: OperationOutcome<Repository>) = async {
        do! provider.Provider.Close outcome.Value

        return WorkspaceSessionHost.withValue folder outcome
    }

    async {
        match result with
        | Succeeded outcome ->
            let! closed = closeOutcome outcome
            return Succeeded closed
        | PartiallySucceeded(outcome, failure) ->
            let! closed = closeOutcome outcome
            return PartiallySucceeded(closed, failure)
        | Failed failure -> return Failed failure
    }

let initializeLocalWorkspace
    (host: WorkspaceSessionHost.WorkspaceSessionHost)
    (targetPath: string)
    (context: OperationContext)
    : Async<OperationResult<string>> =
    async {
        let provider = ProviderComposition.defaultProvider host.Runtime.Providers

        let! initialized =
            ProviderComposition.callFunction
                provider.Functions.Initialize
                "initialization"
                (fun initialize ->
                    initialize
                        {
                            TargetPath = targetPath
                            Location = None
                        }
                        context
                )

        return! closeProvisioned provider targetPath initialized
    }

/// Runs a provisioning operation, which answers with the workspace root.
let private provision
    (host: WorkspaceSessionHost.WorkspaceSessionHost)
    (bridge: RendererBridge)
    (operationName: string)
    (operationId: string)
    (workspaceRoot: string)
    (windowId: int option)
    (run: OperationContext -> Async<OperationResult<string>>)
    : JS.Promise<Result<OperationResultDto<string>, exn>> =
    promise {
        let! result = runTracked host bridge operationName operationId (Some workspaceRoot) windowId true run

        return Ok(Mappings.result id result)
    }

let private removeExistingFile (path: string) =
    if Main.Bindings.Filesystem.existsSync path then
        Main.Bindings.Filesystem.unlinkSync path
        true
    else
        false

let private locationFor
    (host: WorkspaceSessionHost.WorkspaceSessionHost)
    (providerLocation: string)
    (displayName: string option)
    =
    match ProviderComposition.tryCreateLocation providerLocation displayName with
    | Error message -> Error(OperationFailure.create Validation VersionControlCodes.LocationUnsupported message)
    | Ok location ->
        match ProviderComposition.tryFindProvider host.Runtime.Providers location.ProviderId with
        | Some provider -> Ok(location, provider)
        | None ->
            Error(
                OperationFailure.create
                    Validation
                    VersionControlCodes.LocationUnsupported
                    $"No provider is registered for '{ProviderId.value location.ProviderId}'."
            )

let api (event: IpcMainInvokeEvent) : IVersionControlApi = {
    // The first read of a vault opens its repository: the host asks the providers which one owns
    // the folder. That side effect is intended.
    getSessionInfo =
        fun request ->
            withSession
                "getSessionInfo"
                event
                request.OperationId
                false
                (fun hosted _ -> async { return OperationResult.succeeded hosted })
                (fun hosted ->
                    Mappings.sessionInfo hosted.SessionId hosted.WorkspaceRoot hosted.Provider hosted.Repository
                )
    cloneWorkspace =
        fun request ->
            let host = WorkspaceSessionHost.get ()
            let bridge = tryBridgeFromEvent event

            provision
                host
                bridge
                "cloneWorkspace"
                request.OperationId
                request.TargetPath
                (windowFromIpcEvent event |> Option.map _.id)
                (fun context ->
                    match locationFor host request.ProviderLocation request.DisplayName with
                    | Error failure -> async { return Failed failure }
                    | Ok(location, provider) -> async {
                        let! cloned =
                            withOptionalProviderRef
                                request.TargetRef
                                (fun targetRef ->
                                    ProviderComposition.callFunction
                                        provider.Functions.Clone
                                        "cloning"
                                        (fun clone ->
                                            clone
                                                {
                                                    Location = location
                                                    TargetPath = request.TargetPath
                                                    TargetRef = targetRef
                                                    MaterializeAllObjects = request.MaterializeAllObjects
                                                }
                                                context
                                        )
                                )

                        return! closeProvisioned provider request.TargetPath cloned
                      }
                )
    initializeWorkspace =
        fun request ->
            let host = WorkspaceSessionHost.get ()
            let bridge = tryBridgeFromEvent event

            provision
                host
                bridge
                "initializeWorkspace"
                request.OperationId
                request.TargetPath
                (windowFromIpcEvent event |> Option.map _.id)
                (fun context -> initializeLocalWorkspace host request.TargetPath context)
    // Bind changes the vault's repository configuration, so it runs as a mutation of
    // the open vault and refreshes the tree afterwards.
    bindWorkspace =
        fun request -> promise {
            match tryGetVaultAndArcPath event with
            | Error error -> return Error error
            | Ok(vault, _) ->
                let host = WorkspaceSessionHost.get ()
                let bridge = tryBridgeFromEvent event

                let restoreSavedSettings arcPath savedSettings context = async {
                    try
                        let! restored = host.SetSettings(arcPath, savedSettings, context)

                        match restored with
                        | Failed failure ->
                            Browser.Dom.console.error (
                                $"Could not restore version-control settings after binding ({failure.Code}): {failure.Message}"
                            )
                        | Succeeded _
                        | PartiallySucceeded _ -> ()
                    with error ->
                        Browser.Dom.console.error (
                            $"Could not restore version-control settings after binding: {error.Message}"
                        )
                }

                let reopenWithSavedSettings arcPath context = async {
                    // Read the values right before the close, so a change acknowledged
                    // while the bind ran is the one restored afterwards.
                    let savedSettings = host.GetSettings arcPath

                    let savedUploadRightAway =
                        match host.GetUploadRightAway arcPath with
                        | Succeeded outcome
                        | PartiallySucceeded(outcome, _) -> Some outcome.Value
                        | Failed _ -> None

                    let! reopened = host.ReopenSession(arcPath, context)

                    match reopened with
                    | Succeeded outcome
                    | PartiallySucceeded(outcome, _) ->
                        // The new session starts from the current defaults, so the checkbox value goes back first.
                        savedUploadRightAway
                        |> Option.iter (fun saved -> host.SetUploadRightAway(arcPath, saved) |> ignore)

                        // Only settings that differ from what the fresh session started with need restoring.
                        if savedSettings <> outcome.Value.Settings then
                            do! restoreSavedSettings arcPath savedSettings context
                    | _ -> ()

                    return reopened
                }

                return!
                    vault.WithExternalOperationScope(
                        announce = true,
                        operation =
                            fun () -> promise {
                                // The path is read inside the scope, because a root rename can move the folder while this call waits.
                                match tryGetVaultAndArcPath event with
                                | Error error -> return Error error
                                | Ok(_, arcPath) ->
                                    let! bound =
                                        runTracked
                                            host
                                            bridge
                                            "bindWorkspace"
                                            request.OperationId
                                            (Some arcPath)
                                            (windowFromIpcEvent event |> Option.map _.id)
                                            true
                                            (fun context -> async {
                                                match
                                                    locationFor host request.ProviderLocation request.DisplayName
                                                with
                                                | Error failure -> return Failed failure
                                                | Ok(location, provider) ->
                                                    let! bindResult =
                                                        ProviderComposition.callFunction
                                                            provider.Functions.Bind
                                                            "binding"
                                                            (fun bind ->
                                                                bind
                                                                    {
                                                                        RepositoryRoot = arcPath
                                                                        Location = location
                                                                    }
                                                                    context
                                                            )

                                                    match! closeProvisioned provider arcPath bindResult with
                                                    | Succeeded _ ->
                                                        // The opened repository keeps the location it was opened with.
                                                        return! reopenWithSavedSettings arcPath context
                                                    | PartiallySucceeded(_, failure) ->
                                                        // The host reopens the repository, and the result keeps the
                                                        // provider's partial failure.
                                                        let! reopened = reopenWithSavedSettings arcPath context

                                                        return
                                                            match reopened with
                                                            | Succeeded outcome -> PartiallySucceeded(outcome, failure)
                                                            | other -> other
                                                    | Failed failure -> return Failed failure
                                            })

                                    let result =
                                        Ok(
                                            Mappings.result
                                                (fun (hosted: WorkspaceSessionHost.HostedSession) ->
                                                    Mappings.sessionInfo
                                                        hosted.SessionId
                                                        hosted.WorkspaceRoot
                                                        hosted.Provider
                                                        hosted.Repository
                                                )
                                                bound
                                        )

                                    if resultChangedState result then
                                        do! vault.EnqueueFileTreeRevalidateLoaded()
                                        vault.InvalidateLargeObjectIndex()

                                    return result
                            }
                    )
        }
    cancelOperation =
        fun key -> promise {
            let operationName = "cancelOperation"
            let startedAt = System.DateTime.UtcNow
            logOperationStarted operationName key.OperationId

            let host = WorkspaceSessionHost.get ()
            // A window cancels only the operations it started.
            let canceled = host.Cancel(key.OperationId, callingWindowId event)

            let durationMilliseconds =
                int64 (System.DateTime.UtcNow.Subtract(startedAt).TotalMilliseconds)

            logOperationFinished operationName key.OperationId (OperationResult.succeeded canceled) durationMilliseconds
            return Ok canceled
        }
    // The install flow checks the dependencies in the operation of its window. The shared check asks
    // the current provider.
    checkDependencies =
        fun request -> promise {
            let host = WorkspaceSessionHost.get ()
            let bridge = tryBridgeFromEvent event

            let! result =
                runTracked
                    host
                    bridge
                    "checkDependencies"
                    request.OperationId
                    None
                    (windowFromIpcEvent event |> Option.map _.id)
                    false
                    (DependencyState.checkDependencies host.Runtime.Providers)

            return Ok(Mappings.result (Array.map Mappings.dependencyStatus) result)
        }
    getDependencyState = fun () -> promise { return DependencyState.getState () }
    recheckDependencies = fun () -> promise { return DependencyState.recheck () }
    // The current provider answers for the component, so what it says about one it cannot
    // install reaches the user as its own failure.
    installDependency =
        fun request -> promise {
            let host = WorkspaceSessionHost.get ()
            let bridge = tryBridgeFromEvent event

            let! result =
                runTracked
                    host
                    bridge
                    "installDependency"
                    request.OperationId
                    None
                    (windowFromIpcEvent event |> Option.map _.id)
                    true
                    (fun context ->
                        ProviderComposition.callFunction
                            (ProviderComposition.defaultProvider host.Runtime.Providers).Functions.InstallDependency
                            $"installation of '{request.Component}'"
                            (fun install -> install request.Component context)
                    )

            // The banner shows the shared check, so a fix made here needs a new check to clear it.
            match result with
            | Failed _ -> ()
            | Succeeded _
            | PartiallySucceeded _ -> DependencyState.trigger ()

            return Ok(Mappings.result Mappings.dependencyStatus result)
        }
    getStatus =
        fun request ->
            withSession
                "getStatus"
                event
                request.OperationId
                false
                (withFunction _.GetStatus "workspace status" id)
                Mappings.workspaceStatus
    listRefs =
        fun request ->
            withSession
                "listRefs"
                event
                request.OperationId
                false
                (withFunction _.ListRefs "refs" id)
                (Array.map Mappings.logicalRef)
    createRef =
        fun request ->
            withMutatingSession
                "createRef"
                event
                request.OperationId
                request.SwitchTo
                (withFunction
                    _.CreateRef
                    "ref creation"
                    (fun createRef repository context ->
                        withOptionalProviderRef
                            request.BaseRef
                            (fun baseRef ->
                                createRef
                                    repository
                                    {
                                        Name = request.Name
                                        BaseRef = baseRef
                                        SwitchTo = request.SwitchTo
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                    }
                                    context
                            )
                    ))
                Mappings.logicalRef
    preflightSwitchRef =
        fun request ->
            withSession
                "preflightSwitchRef"
                event
                request.OperationId
                false
                (withFunction
                    _.PreflightSwitchRef
                    "ref switching"
                    (fun preflightSwitchRef repository context ->
                        withProviderRef
                            request.TargetRef
                            (fun targetRef ->
                                preflightSwitchRef
                                    repository
                                    {
                                        TargetRef = targetRef
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                    }
                                    context
                            )
                    ))
                Mappings.switchPreflight
    switchRef =
        fun request ->
            withMutatingSession
                "switchRef"
                event
                request.OperationId
                true
                (withFunction
                    _.SwitchRef
                    "ref switching"
                    (fun switchRef repository context ->
                        withProviderRef
                            request.TargetRef
                            (fun targetRef ->
                                switchRef
                                    repository
                                    {
                                        TargetRef = targetRef
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                    }
                                    context
                            )
                    ))
                Mappings.workspaceStatus
    createRevision =
        fun request ->
            withMutatingSession
                "createRevision"
                event
                request.OperationId
                true
                (withFunction
                    _.CreateRevision
                    "revision creation"
                    (fun createRevision repository context ->
                        withPaths
                            request.Paths
                            (fun paths ->
                                createRevision
                                    repository
                                    {
                                        Message = request.Message
                                        Paths = paths
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                    }
                                    context
                            )
                    ))
                RevisionId.value
    restorePaths =
        fun request ->
            withMutatingSession
                "restorePaths"
                event
                request.OperationId
                true
                (withFunction
                    _.RestorePaths
                    "path restoring"
                    (fun restorePaths repository context ->
                        withPaths
                            request.Paths
                            (fun paths ->
                                restorePaths
                                    repository
                                    {
                                        Paths = paths
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                    }
                                    context
                            )
                    ))
                id
    refreshSynchronization =
        fun request ->
            withSession
                "refreshSynchronization"
                event
                request.OperationId
                false
                (withFunction _.Synchronization.Refresh "synchronization" id)
                Mappings.synchronizationState
    synchronize =
        fun request ->
            withMutatingSession
                "synchronize"
                event
                request.OperationId
                true
                (withFunction
                    _.Synchronization.Synchronize
                    "synchronization"
                    (fun synchronize repository context ->
                        withOptionalRevision
                            request.ExpectedTargetRevision
                            (fun expectedTarget ->
                                synchronize
                                    repository
                                    {
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                        ExpectedTargetRevision = expectedTarget
                                        AcceptUpdateRisks = request.AcceptUpdateRisks
                                        PublishLocalRevisions = request.PublishLocalRevisions
                                    }
                                    context
                            )
                    ))
                Mappings.synchronizationState
    resolveConflict =
        fun request ->
            withMutatingSession
                "resolveConflict"
                event
                request.OperationId
                true
                (withFunction
                    _.ConflictResolution.Resolve
                    "conflict resolution"
                    (fun resolve repository context ->
                        withPath
                            request.Path
                            (fun path ->
                                resolve
                                    repository
                                    {
                                        Handle = Mappings.conflictHandleFromDto request.Handle
                                        ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                        Path = path
                                        Resolution = Mappings.conflictResolutionFromDto request.Resolution
                                    }
                                    context
                            )
                    ))
                Mappings.conflictOutcome
    finalizeConflict =
        fun request ->
            withMutatingSession
                "finalizeConflict"
                event
                request.OperationId
                true
                (withFunction
                    _.ConflictResolution.Finalize
                    "conflict resolution"
                    (fun finalize repository context ->
                        finalize
                            repository
                            {
                                Handle = Mappings.conflictHandleFromDto request.Handle
                                ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                                Message = request.Message
                            }
                            context
                    ))
                (Option.map RevisionId.value)
    cancelConflict =
        fun request ->
            withMutatingSession
                "cancelConflict"
                event
                request.OperationId
                true
                (withFunction
                    _.ConflictResolution.Cancel
                    "conflict resolution"
                    (fun cancel repository context ->
                        cancel
                            repository
                            {
                                Handle = Mappings.conflictHandleFromDto request.Handle
                                ExpectedRepositoryVersion = request.ExpectedWorkspaceVersion
                            }
                            context
                    ))
                id
    listObjects =
        fun request ->
            withSession
                "listObjects"
                event
                request.OperationId
                false
                (withFunction _.ObjectMaterialization.ListObjects "large object materialization" id)
                (Array.map Mappings.objectState)
    materializeObjects =
        fun request ->
            withMutatingSessionUsingRefreshPredicate
                "materializeObjects"
                event
                request.OperationId
                (shouldRefreshObjectTree request)
                (withFunction
                    _.ObjectMaterialization.Materialize
                    "large object materialization"
                    (fun materialize repository context ->
                        withPaths request.Paths (fun paths -> materialize repository paths context)
                    ))
                id
    dematerializeObjects =
        fun request ->
            withMutatingSessionUsingRefreshPredicate
                "dematerializeObjects"
                event
                request.OperationId
                (shouldRefreshObjectTree request)
                (withFunction
                    _.ObjectMaterialization.Dematerialize
                    "large object materialization"
                    (fun dematerialize repository context ->
                        withPaths request.Paths (fun paths -> dematerialize repository paths context)
                    ))
                id
    getStoragePolicySettings =
        fun request ->
            withSession
                "getStoragePolicySettings"
                event
                request.OperationId
                false
                (fun hosted _ -> async {
                    let host = WorkspaceSessionHost.get ()

                    return OperationResult.succeeded (storagePolicySettingsDto (host.GetSettings hosted.WorkspaceRoot))
                })
                id
    setStoragePolicySettings =
        fun request ->
            withMutatingSession
                "setStoragePolicySettings"
                event
                request.OperationId
                false
                (fun hosted context ->
                    let host = WorkspaceSessionHost.get ()
                    let current = host.GetSettings hosted.WorkspaceRoot

                    let settings = {
                        AutoTrackThresholdMb =
                            request.Settings.AutoPolicyThresholdMb
                            |> Option.defaultValue current.AutoTrackThresholdMb
                        DownloadLargeFiles = request.Settings.MaterializeLargeObjects
                        DiffIndexingLimitMb =
                            request.Settings.DiffIndexingLimitMb
                            |> Option.defaultValue current.DiffIndexingLimitMb
                    }

                    host.SetSettings(hosted.WorkspaceRoot, settings, context)
                )
                id
    // The checkbox calls skip runTracked and the busy flag, because they change no workspace
    // state. They never open a session, so a vault without one answers with a failure.
    getUploadRightAway =
        fun () -> promise {
            match tryGetVaultAndArcPath event with
            | Error error -> return Error error
            | Ok(_, arcPath) -> return Ok(Mappings.result id (WorkspaceSessionHost.get().GetUploadRightAway arcPath))
        }
    setUploadRightAway =
        fun value -> promise {
            match tryGetVaultAndArcPath event with
            | Error error -> return Error error
            | Ok(_, arcPath) ->
                return Ok(Mappings.result id (WorkspaceSessionHost.get().SetUploadRightAway(arcPath, value)))
        }
    // The DataHUB ruleset is checked here as well as in the renderer, because the main
    // process is the trust boundary. The main process reads the file size from the
    // workspace before checking the size rule.
    setPathStoragePolicy =
        fun request ->
            withMutatingSession
                "setPathStoragePolicy"
                event
                request.OperationId
                true
                (fun hosted context ->
                    withFunction
                        _.StoragePolicy.SetPathPolicy
                        "storage policies"
                        (fun setPathPolicy repository policyContext ->
                            withPath
                                request.Path
                                (fun path -> async {
                                    let relativePath = RepositoryPath.value path

                                    let! fileSize = tryGetRegularFileSize hosted.WorkspaceRoot relativePath

                                    match
                                        Arcitisa.Components.Shared.GitLfsRules.tryGetToggleBlockedReason
                                            relativePath
                                            fileSize
                                            request.UseLargeObjectStorage
                                    with
                                    | Some reason ->
                                        return validationFailed VersionControlCodes.StoragePolicyBlocked reason
                                    | None ->
                                        return!
                                            setPathPolicy repository path request.UseLargeObjectStorage policyContext
                                })
                        )
                        hosted
                        context
                )
                id
    pruneStorage =
        fun request ->
            withMutatingSession
                "pruneStorage"
                event
                request.OperationId
                false
                (withFunction _.Maintenance.Prune "storage maintenance" id)
                id
    deduplicateStorage =
        fun request ->
            withMutatingSession
                "deduplicateStorage"
                event
                request.OperationId
                false
                (withFunction _.Maintenance.Deduplicate "storage maintenance" id)
                id
    canDeduplicateStorage =
        fun request ->
            withSession
                "canDeduplicateStorage"
                event
                request.OperationId
                false
                (withFunction _.Maintenance.CanDeduplicate "storage maintenance" id)
                id
    getRepositoryWebUrl =
        fun request ->
            withSession
                "getRepositoryWebUrl"
                event
                request.OperationId
                false
                (withFunction _.RepositoryBrowser.GetRepositoryWebUrl "a repository browser" id)
                id
    listRevisions =
        fun request ->
            withSession
                "listRevisions"
                event
                request.OperationId
                false
                (withFunction
                    _.History.ListRevisions
                    "a commit history"
                    (fun listRevisions repository context ->
                        listRevisions repository (Mappings.listRevisionsRequest request) context
                    ))
                Mappings.revisionPage
    getRevisionChanges =
        fun request ->
            withSession
                "getRevisionChanges"
                event
                request.OperationId
                false
                (withFunction
                    _.History.GetRevisionChanges
                    "a commit history"
                    (fun getRevisionChanges repository context ->
                        withRevision request.Revision (fun revision -> getRevisionChanges repository revision context)
                    ))
                (fun summary -> summary.Entries |> Array.map Mappings.diffEntryChange)
    // Removes a stale provider lock only while this operation is the only one of the
    // session, then refreshes and returns the status. Removed files are reported as
    // warnings whose message is the absolute path, because they are not repository
    // paths and a success outcome has no other field for them.
    clearStaleLock =
        fun request ->
            withMutatingSession
                "clearStaleLock"
                event
                request.OperationId
                true
                (fun hosted context -> async {
                    let host = WorkspaceSessionHost.get ()

                    let otherOperations =
                        host.RunningOperationIds hosted.WorkspaceRoot
                        |> Array.filter (fun id -> id <> request.OperationId)

                    if otherOperations.Length > 0 then
                        return
                            Failed {
                                OperationFailure.create
                                    Concurrency
                                    VersionControlCodes.LockRemovalRefused
                                    "Another operation is still running on this workspace. Wait for it to finish, then try again." with
                                    Retryable = true
                            }
                    else
                        let recentLockPaths =
                            ProviderComposition.recentLockPaths hosted.Provider.Id hosted.WorkspaceRoot

                        if recentLockPaths.Length > 0 then
                            let waitMilliseconds =
                                recentLockPaths
                                |> Array.map ProviderComposition.indexLockWaitMilliseconds
                                |> Array.max
                                |> min 10_000

                            do! waitForStaleLock waitMilliseconds

                        let lockPaths =
                            ProviderComposition.staleLockPaths hosted.Provider.Id hosted.WorkspaceRoot

                        let removed = lockPaths |> Array.filter removeExistingFile

                        match hosted.Provider.Functions.Synchronization.Refresh with
                        | Implemented refresh ->
                            let! _ = refresh hosted.Repository context
                            ()
                        | Missing _ -> ()

                        let! status = withFunction _.GetStatus "workspace status" id hosted context

                        let removedWarnings =
                            removed
                            |> Array.map (fun path -> {
                                Code = VersionControlCodes.LockRemoved
                                Message = path
                            })

                        let effect =
                            if removed.Length > 0 then
                                Performed
                            else
                                Unchanged(Some "no stale lock")

                        // A partial status refresh still reports the removed lock files, since these
                        // warnings are the only evidence of the removal.
                        return
                            match status with
                            | Succeeded outcome ->
                                Succeeded {
                                    outcome with
                                        Effect = effect
                                        Warnings = Array.append outcome.Warnings removedWarnings
                                }
                            | PartiallySucceeded(outcome, failure) ->
                                PartiallySucceeded(
                                    {
                                        outcome with
                                            Effect = effect
                                            Warnings = Array.append outcome.Warnings removedWarnings
                                    },
                                    failure
                                )
                            | Failed failure -> Failed failure
                })
                Mappings.workspaceStatus
    // Every handle an open returns is recorded with the window of the operation, so its
    // handles close with the window. The library keeps each window to its own handles. A
    // handle that arrives after its window closed is closed at once.
    openTextDiff =
        fun request ->
            // The settings of the session decide where the diff keeps its data.
            let policy = diffStoragePolicyOf event

            withTextDiff
                "openTextDiff"
                event
                request.OperationId
                (Mappings.tryOpenDiffRequest policy request)
                _.Open
                (fun hosted openDiff openRequest context -> async {
                    let windowId = callingWindowId event
                    let! opened = openDiff hosted.Repository openRequest context

                    match opened with
                    | Succeeded outcome
                    | PartiallySucceeded(outcome, _) ->
                        match outcome.Value with
                        | Resumable.Ready(OpenDiffResult.Opened(handle, _, _, _, _)) ->
                            TextDiffHandles.recordOrClose windowId hosted handle
                        | _ -> ()
                    | Failed _ -> ()

                    return opened
                })
                (Mappings.resumableOpen policy)
    readTextDiffPage =
        fun request ->
            withTextDiff
                "readTextDiffPage"
                event
                request.OperationId
                (Mappings.tryReadPageRequest request)
                _.ReadPage
                (fun hosted readPage pageRequest context -> readPage hosted.Repository pageRequest context)
                Mappings.resumablePage
    replayTextDiffPage =
        fun request ->
            withTextDiff
                "replayTextDiffPage"
                event
                request.OperationId
                (Mappings.tryReplayPageRequest request)
                _.ReplayPage
                (fun hosted replayPage replayRequest context -> replayPage hosted.Repository replayRequest context)
                Mappings.diffPage
    expandTextDiff =
        fun request ->
            withTextDiff
                "expandTextDiff"
                event
                request.OperationId
                (Mappings.tryExpandRequest request)
                _.Expand
                (fun hosted expand expandRequest context -> expand hosted.Repository expandRequest context)
                Mappings.resumableParts
    readTextDiffLine =
        fun request ->
            withTextDiff
                "readTextDiffLine"
                event
                request.OperationId
                (Mappings.tryReadLineRequest request)
                _.ReadLine
                (fun hosted readLine lineRequest context -> readLine hosted.Repository lineRequest context)
                Mappings.resumableLine
    getTextDiffSourceInfo =
        fun request ->
            withTextDiff
                "getTextDiffSourceInfo"
                event
                request.OperationId
                (Mappings.tryDiffHandle request)
                _.GetSourceInfo
                (fun hosted getSourceInfo handle context ->
                    getSourceInfo hosted.Repository { SourceInfoRequest.Handle = handle } context
                )
                Mappings.diffSourceInfoPair
    // A failed close keeps the registry entry, so the window close retries it.
    closeTextDiff =
        fun request ->
            withTextDiff
                "closeTextDiff"
                event
                request.OperationId
                (Mappings.tryDiffHandle request)
                _.Close
                (fun hosted closeDiff handle context -> async {
                    let! closed = closeDiff hosted.Repository handle context

                    match closed with
                    | Failed _ -> ()
                    | Succeeded _
                    | PartiallySucceeded _ -> TextDiffHandles.remove handle.Id

                    return closed
                })
                id
}
