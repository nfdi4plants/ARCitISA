/// The file watcher members of ArcVault, with the watcher event controller.
[<AutoOpen>]
module Main.ArcVaultFileWatcher

open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron
open Fable.Electron.Main
open Main
open Main.Bindings.PromiseRace
open Main.ARCtrlExtensions
open Main.Bindings
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.ArcMerge
open Main.ArcVaultHelper
open Main.FileImportCoordinator
open Swate.Components.Shared
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.IPCTypesHelper
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open ARCtrl
open Main.ArcVaultState

/// A completed import keeps its marks until the watcher has reported none of its paths for the quiet time,
/// and at most for the maximum time after the import completed. Polling reports a large import over many
/// seconds, so a fixed time drops marks while events still arrive. A native watcher can miss events, so a
/// mark must not wait for an event forever.
let private importedPathQuietMilliseconds = 30000
let private importedPathMaximumMilliseconds = 300000

type ArcVault with

    /// Marks the directories of a watcher batch dirty and reconciles them in one writer job.
    /// A pending-state reset after the call drops the job, so an update queued before it cannot publish the old root.
    member internal this.ApplyWatcherFileTreeEvents(events: ArcVaultFileSystemEvent list) =
        let capturedRootToken = this.RootToken

        let dirtyDirectories =
            WatcherHelpers.dirtyDirectoriesOfEvents this.isCaseInsensitive events

        // A changed .gitattributes can change which files are LFS pointers.
        let gitAttributesKey = PathKey.ofPath this.isCaseInsensitive ".gitattributes"

        if
            events
            |> List.exists (fun event -> PathKey.ofPath this.isCaseInsensitive event.RelativePath = gitAttributesKey)
        then
            this.InvalidateLargeObjectIndex()

        promise {
            do!
                this.FileTreeWriter.Enqueue(fun tree -> promise {
                    match this.path with
                    | Some arcPath when capturedRootToken = this.RootToken ->
                        return!
                            reconcileDirtyDirectoriesAsync
                                arcPath
                                tree
                                (fun directory error ->
                                    swatelogfn
                                        this.window.id
                                        "Unable to update file tree directory '%s': %s"
                                        directory
                                        error.Message
                                )
                                dirtyDirectories
                    | _ -> return [||]
                })

            if capturedRootToken = this.RootToken then
                do! this.PublishFileContentChanges events
        }

    member this.StartImport() : int =
        this.nextImportToken <- this.nextImportToken + 1
        this.nextImportToken

    /// Marks the paths of the events as written by the import with this token. A later claim of the same path
    /// takes the mark over from an earlier import.
    member this.ClaimImportedFileEvents(token: int, events: ArcVaultFileSystemEvent seq) =
        let keys =
            events
            |> Seq.map (fun event -> event.AbsolutePath.ToLowerInvariant())
            |> Seq.toArray

        for key in keys do
            this.importedPathOwners.[key] <- token

        match this.importedPathLeases.TryGetValue token with
        | true, lease -> lease.Paths <- Array.append lease.Paths keys |> Array.distinct
        | _ ->
            this.importedPathLeases.[token] <- {
                Paths = Array.distinct keys
                CompletedAt = None
                LastActivity = Main.Bindings.Node.performanceNow ()
                Timer = None
            }

    member private this.ReleaseImport(token: int) =
        match this.importedPathLeases.TryGetValue token with
        | true, lease ->
            lease.Timer |> Option.iter Fable.Core.JS.clearTimeout
            lease.Timer <- None

            for key in lease.Paths do
                match this.importedPathOwners.TryGetValue key with
                | true, owner when owner = token -> this.importedPathOwners.Remove key |> ignore
                | _ -> ()

            this.importedPathLeases.Remove token |> ignore
        | _ -> ()

    member private this.ImportedLeaseDeadline(lease: ImportedPathLease) : float =
        match lease.CompletedAt with
        | Some completedAt ->
            min
                (completedAt + float importedPathMaximumMilliseconds)
                ((max completedAt lease.LastActivity) + float importedPathQuietMilliseconds)
        | None -> infinity

    member private this.ArmImportRelease(token: int) =
        match this.importedPathLeases.TryGetValue token with
        | true, lease when lease.CompletedAt.IsSome ->
            lease.Timer |> Option.iter Fable.Core.JS.clearTimeout

            let delay =
                max 0.0 (this.ImportedLeaseDeadline lease - Main.Bindings.Node.performanceNow ())

            lease.Timer <-
                Some(
                    Fable.Core.JS.setTimeout
                        (fun () ->
                            match this.importedPathLeases.TryGetValue token with
                            | true, current ->
                                current.Timer <- None

                                if Main.Bindings.Node.performanceNow () >= this.ImportedLeaseDeadline current then
                                    this.ReleaseImport token
                                else
                                    this.ArmImportRelease token
                            | _ -> ()
                        )
                        (int delay)
                )
        | _ -> ()

    /// Ends the marks of an import. A completed import keeps them until the watcher has reported none of its
    /// paths for the quiet time, and for the maximum time at the latest. Without a running watcher no event
    /// will come, because a restarted watcher skips files that already exist, so the marks end at once. A
    /// cancelled or failed import removed what it wrote, so a later creation event for its paths does not
    /// come from the import. If that cleanup failed, the leftover files stay unmarked.
    member this.ReleaseImportedFileEventsAfterImport(token: int, result: Result<ImportExternalFilesResult, exn>) =
        match result with
        | Ok(ImportExternalFilesResult.Completed _) when this.fileWatcherState = FileWatcherState.Running ->
            match this.importedPathLeases.TryGetValue token with
            | true, lease when lease.CompletedAt.IsNone ->
                let now = Main.Bindings.Node.performanceNow ()
                lease.CompletedAt <- Some now
                lease.LastActivity <- now
                this.ArmImportRelease token
            | _ -> ()
        | _ -> this.ReleaseImport token

    /// Removes the mark of a path, and with includeDescendants the marks below it, from the owners and from
    /// the paths of their leases. The lease and its timer stay, so the timer still ends an emptied lease.
    member private this.ReleaseImportedPath(key: string, includeDescendants: bool) =
        let prefix = key + "/"

        let keys =
            this.importedPathOwners.Keys
            |> Seq.filter (fun candidate -> candidate = key || (includeDescendants && candidate.StartsWith prefix))
            |> Seq.toArray

        for removedKey in keys do
            match this.importedPathOwners.TryGetValue removedKey with
            | true, token ->
                match this.importedPathLeases.TryGetValue token with
                | true, lease -> lease.Paths <- lease.Paths |> Array.filter (fun path -> path <> removedKey)
                | _ -> ()
            | _ -> ()

            this.importedPathOwners.Remove removedKey |> ignore

    /// True for a creation event of a path that an import wrote. Only creation events count, so an external
    /// edit or deletion of an imported path still merges. A creation event leaves the mark in place, because
    /// an old event must not use up the mark of a newer import of the same path. A deletion event releases
    /// the mark of its path, and for a folder the marks below it, so a recreated path merges again. A
    /// matching call also restarts the quiet time of its import, so call it once per watcher event.
    member this.IsImportedWatcherEvent(event: ArcVaultFileSystemEvent) : bool =
        let isCreation =
            WatcherHelpers.eventNameEquals Chokidar.Events.Add event.EventName
            || WatcherHelpers.eventNameEquals Chokidar.Events.AddDir event.EventName

        if not isCreation then
            if WatcherHelpers.eventNameEquals Chokidar.Events.Unlink event.EventName then
                this.ReleaseImportedPath(event.AbsolutePath.ToLowerInvariant(), false)
            elif WatcherHelpers.eventNameEquals Chokidar.Events.UnlinkDir event.EventName then
                this.ReleaseImportedPath(event.AbsolutePath.ToLowerInvariant(), true)

            false
        else
            match this.importedPathOwners.TryGetValue(event.AbsolutePath.ToLowerInvariant()) with
            | true, token ->
                match this.importedPathLeases.TryGetValue token with
                | true, lease ->
                    let now = Main.Bindings.Node.performanceNow ()

                    if lease.CompletedAt.IsSome && now >= this.ImportedLeaseDeadline lease then
                        this.ReleaseImport token
                        false
                    else
                        lease.LastActivity <- now
                        true
                | _ -> false
            | _ -> false

    /// Drops the marks of every import, together with their release timers.
    member internal this.ClearImportedFileEvents() =
        for lease in this.importedPathLeases.Values do
            lease.Timer |> Option.iter Fable.Core.JS.clearTimeout

        this.importedPathLeases.Clear()
        this.importedPathOwners.Clear()

    /// The doorbell of the watcher. An event only starts a quiet timer. The sync itself decides what changed
    /// by comparing the files with the known versions.
    member internal this._FileEventController(sendMsgApi: IArcFileWatcherApi) =
        let isLoading = ref false
        let runningFlushes = ref 0

        let finishFlush () =
            runningFlushes.Value <- runningFlushes.Value - 1

            if
                runningFlushes.Value = 0
                && this.fileWatcherReloadArcTimeout.IsNone
                && this.fileWatcherPendingEvents.Count = 0
            then
                isLoading.Value <- false
                sendMsgApi.IsLoadingChanges false

        let flush () =
            this.fileWatcherReloadArcTimeout <- None
            let capturedRootToken = this.RootToken
            let events = this.fileWatcherPendingEvents |> Seq.toList
            this.fileWatcherPendingEvents.Clear()
            runningFlushes.Value <- runningFlushes.Value + 1

            promise {
                try
                    swatelogfn this.window.id "Scheduled ARC sync triggered by file watcher."
                    // Resolves at once while a write runs, so the tree is published immediately then.
                    do! this.RequestSync true

                    // A tree update is renderer-visible and can trigger an immediate openFile call.
                    // After a sync that call reads the state the tree shows. During a write it reads the
                    // disk fallback of openFile, which is better than a tree that hides external changes
                    // for the whole write. A batch from before a root change carries paths of the old root.
                    if capturedRootToken = this.RootToken then
                        do! this.ApplyWatcherFileTreeEvents events
                with ex ->
                    swatelogfn this.window.id "Scheduled ARC sync failed: %s" ex.Message

                finishFlush ()
            }
            |> Promise.start

        fun (eventName: string) (path: string) ->
            swatelogfn this.window.id "File change detected: %s on %s" eventName path

            this.path
            |> Option.iter (fun rootPath ->
                this.fileWatcherPendingEvents.Add(WatcherHelpers.buildWatcherEvent rootPath eventName path)
            )

            this.fileWatcherReloadArcTimeout |> Option.iter Fable.Core.JS.clearTimeout
            this.fileWatcherReloadArcTimeout <- None

            if not isLoading.Value then
                isLoading.Value <- true
                sendMsgApi.IsLoadingChanges true

            this.fileWatcherReloadArcTimeout <- Some(Fable.Core.JS.setTimeout flush 500)

    /// Starts the watcher and resolves when chokidar reports ready, or after FileWatcherReadinessTimeoutMs.
    /// It never raises. A failed start closes the watcher and leaves the vault degraded, so a readable ARC still opens.
    member internal this.StartFileWatcherAsync(?usePolling: bool) : Fable.Core.JS.Promise<FileWatcherStartOutcome> =
        match this.watcher, this.path with
        | Some _, _ -> Fable.Core.JS.Constructors.Promise.resolve FileWatcherStartOutcome.Ready
        | None, None -> Fable.Core.JS.Constructors.Promise.resolve (FileWatcherStartOutcome.Failed(arcNotOpenError ()))
        // A rename or restart that outlives its window or a close must not leave a watcher behind.
        | None, Some _ when this.window.isDestroyed () || this.isCloseApproved ->
            Fable.Core.JS.Constructors.Promise.resolve FileWatcherStartOutcome.Stopped
        | None, Some arcPath ->
            let previousState = this.fileWatcherState
            this.FileWatcherToken <- this.FileWatcherToken + 1
            let token = this.FileWatcherToken
            let isRetired = ref false

            let isCurrent () =
                this.FileWatcherToken = token && not isRetired.Value

            let startedWatcher: Chokidar.IWatcher option ref = ref None
            this.fileWatcherState <- FileWatcherState.Starting

            let startup =
                try
                    let watcher = this.FileWatcherFactory arcPath usePolling
                    startedWatcher.Value <- Some watcher
                    this.watcher <- Some watcher
                    let sendWatcherMessage = WindowSend.sender<IArcFileWatcherApi> this.window

                    let sendMsgApi: IArcFileWatcherApi = {
                        IsLoadingChanges =
                            fun isLoading -> sendWatcherMessage (fun api -> api.IsLoadingChanges isLoading)
                    }

                    let handler = this._FileEventController sendMsgApi

                    watcher.on (
                        Chokidar.Events.All,
                        fun (eventName: string) (changedPath: string) ->
                            if isCurrent () then
                                if this.isInitializingArc || this.IsFlushingBufferedFileWatcherEvents then
                                    this.BufferedFileWatcherEvents.Add(fun () ->
                                        if isCurrent () then
                                            handler eventName changedPath
                                    )
                                else
                                    handler eventName changedPath
                    )
                    |> ignore

                    Fable.Core.JS.Constructors.Promise.Create(fun resolve _ ->
                        let settled = ref false
                        let timeoutId: int option ref = ref None

                        let finish (outcome: FileWatcherStartOutcome) =
                            if not settled.Value then
                                settled.Value <- true
                                this.PendingFileWatcherStart <- None
                                timeoutId.Value |> Option.iter Fable.Core.JS.clearTimeout

                                match outcome with
                                | FileWatcherStartOutcome.Ready -> ()
                                | _ -> isRetired.Value <- true

                                resolve outcome

                        timeoutId.Value <-
                            Some(
                                Fable.Core.JS.setTimeout
                                    (fun () -> finish FileWatcherStartOutcome.TimedOut)
                                    this.FileWatcherReadinessTimeoutMs
                            )

                        this.PendingFileWatcherStart <- Some finish

                        watcher.on (Chokidar.Events.Ready, (fun () -> finish FileWatcherStartOutcome.Ready))
                        |> ignore

                        watcher.on (
                            Chokidar.Events.Error,
                            fun (error: exn) ->
                                if isCurrent () then
                                    if settled.Value then
                                        match tryGetNodeErrorCode error with
                                        | Some code when code = "EPERM" || code = "EACCES" ->
                                            this.ReportFileWatcherPermissionError(code, error)
                                        | _ -> this.OnFileWatcherError(token, error)
                                    else
                                        finish (FileWatcherStartOutcome.Failed error)
                        )
                        |> ignore
                    )
                with startError ->
                    isRetired.Value <- true
                    Fable.Core.JS.Constructors.Promise.resolve (FileWatcherStartOutcome.Failed startError)

            promise {
                let! outcome = startup
                let mutable result = outcome

                match outcome with
                | FileWatcherStartOutcome.Ready ->
                    if this.FileWatcherToken = token then
                        this.fileWatcherState <- FileWatcherState.Running
                        this.ArmFileWatcherStableReset token
                    else
                        result <- FileWatcherStartOutcome.Stopped
                | FileWatcherStartOutcome.Stopped -> ()
                | failure ->
                    let reason =
                        match failure with
                        | FileWatcherStartOutcome.Failed error -> error.Message
                        | _ -> $"The watcher was not ready after {this.FileWatcherReadinessTimeoutMs} ms."

                    match startedWatcher.Value with
                    | Some watcher ->
                        try
                            do! watcher.close ()
                        with _ ->
                            ()
                    | None -> ()

                    // A stop that ran in the meantime owns the state.
                    if this.FileWatcherToken <> token then
                        result <- FileWatcherStartOutcome.Stopped

                    if this.FileWatcherToken = token then
                        this.FileWatcherToken <- token + 1
                        this.watcher <- None
                        this.BufferedFileWatcherEvents.Clear()

                        match previousState with
                        | FileWatcherState.Unavailable _ -> this.fileWatcherState <- previousState
                        | _ ->
                            swatelogfn this.window.id "Structural file watcher did not start: %s" reason
                            this.fileWatcherState <- FileWatcherState.Degraded reason

                return result
            }

    /// Logs a per-path permission error once. The watcher keeps running because the other paths still work.
    member internal this.ReportFileWatcherPermissionError(code: string, error: exn) =
        let key = code + "|" + (tryGetNodeErrorPath error |> Option.defaultValue "")

        if this.ReportedFileWatcherPermissionErrors.Add key then
            swatelogfn this.window.id "Structural file watcher cannot read a path (%s): %s" code error.Message

    /// Starts the watcher without waiting for it. The outcome is recorded in fileWatcherState.
    member this.StartFileWatcher(?usePolling: bool) =
        this.StartFileWatcherAsync(?usePolling = usePolling) |> Promise.iter ignore

    member internal this.CancelFileWatcherRestart() =
        this.FileWatcherRestartTimeout |> Option.iter Fable.Core.JS.clearTimeout
        this.FileWatcherRestartTimeout <- None
        this.FileWatcherStableTimeout |> Option.iter Fable.Core.JS.clearTimeout
        this.FileWatcherStableTimeout <- None

    member internal this.ArmFileWatcherStableReset(token: int) =
        this.FileWatcherStableTimeout |> Option.iter Fable.Core.JS.clearTimeout

        this.FileWatcherStableTimeout <-
            Some(
                Fable.Core.JS.setTimeout
                    (fun () ->
                        this.FileWatcherStableTimeout <- None

                        if this.FileWatcherToken = token then
                            this.FileWatcherRestartAttempt <- 0
                    )
                    this.FileWatcherStableAfterMs
            )

    /// Retires the failing watcher and restarts it after the next backoff step. A watcher error that follows an
    /// earlier ready signal arrives here.
    member internal this.OnFileWatcherError(token: int, error: exn) =
        if this.FileWatcherToken = token then
            swatelogfn this.window.id "Structural file watcher error: %s" error.Message
            this.FileWatcherToken <- token + 1
            this.CancelFileWatcherRestart()
            // The restarted watcher skips files that already exist, so these marks could only swallow a later change.
            this.ClearImportedFileEvents()
            let failedWatcher = this.watcher
            this.watcher <- None
            this.fileWatcherState <- FileWatcherState.Degraded error.Message

            failedWatcher
            |> Option.iter (fun watcher ->
                try
                    watcher.close () |> Promise.iter ignore
                with _ ->
                    ()
            )

            this.ScheduleFileWatcherRestart(Some error)

    /// Waits 1 s, 5 s and 30 s between restarts, then repeats the last wait. A watch or descriptor limit
    /// (ENOSPC, EMFILE) ends the restarts after the last step.
    member internal this.ScheduleFileWatcherRestart(error: exn option) =
        let steps = this.FileWatcherRestartBackoffMs
        let attempt = this.FileWatcherRestartAttempt

        let isResourceLimit =
            error
            |> Option.bind tryGetNodeErrorCode
            |> Option.exists (fun code -> code = "ENOSPC" || code = "EMFILE")

        let isStopped =
            this.path.IsNone
            || this.window.isDestroyed ()
            || (
                match this.fileWatcherState with
                | FileWatcherState.Stopped -> true
                | _ -> false
            )

        if isStopped || this.FileWatcherRestartTimeout.IsSome then
            ()
        elif isResourceLimit && attempt >= steps.Length then
            let reason = error |> Option.map (fun e -> e.Message) |> Option.defaultValue ""

            match this.fileWatcherState with
            | FileWatcherState.Unavailable _ -> ()
            | _ ->
                swatelogfn
                    this.window.id
                    "File watching is unavailable (%s). On Linux, raise fs.inotify.max_user_watches. On macOS, raise the open file limit."
                    reason

            this.fileWatcherState <- FileWatcherState.Unavailable reason
        else
            this.FileWatcherRestartAttempt <- attempt + 1
            let scheduledToken = this.FileWatcherToken

            this.FileWatcherRestartTimeout <-
                Some(
                    Fable.Core.JS.setTimeout
                        (fun () ->
                            this.FileWatcherRestartTimeout <- None

                            if this.FileWatcherToken <> scheduledToken then
                                ()
                            elif this.isInitializingArc then
                                // The initial snapshot is not installed yet. Wait the same step again.
                                this.FileWatcherRestartAttempt <- attempt
                                this.ScheduleFileWatcherRestart error
                            else
                                this.RunFileWatcherRestart()
                        )
                        steps.[min attempt (steps.Length - 1)]
                )

    /// One restart attempt: starts the watcher, resyncs on Ready and schedules the next attempt on a timeout or failure.
    /// The backoff timer and a root rename that took over a pending restart both run it.
    member internal this.RunFileWatcherRestart() : unit =
        promise {
            if this.watcher.IsNone && this.path.IsSome && not (this.window.isDestroyed ()) then
                match! this.StartFileWatcherAsync() with
                | FileWatcherStartOutcome.Ready -> do! this.EnqueueWatcherResync()
                | FileWatcherStartOutcome.TimedOut -> this.ScheduleFileWatcherRestart None
                | FileWatcherStartOutcome.Failed startError -> this.ScheduleFileWatcherRestart(Some startError)
                | FileWatcherStartOutcome.Stopped -> ()
        }
        |> Promise.catch (fun restartError ->
            swatelogfn this.window.id "Unable to restart the structural file watcher: %s" restartError.Message
        )
        |> Promise.start

    /// Catches up on changes that the watcher missed while it was down. The files are compared with the known
    /// versions and the loaded directories of the file tree are revalidated.
    member internal this.EnqueueWatcherResync() : Fable.Core.JS.Promise<unit> = promise {
        match this.path with
        | Some _ ->
            do! this.RequestSync true
            do! this.EnqueueFileTreeRevalidateLoaded()
        | None -> ()
    }

    /// True when the window gaining focus should try to bring a missing watcher back.
    member this.CanRestartFileWatcherOnFocus =
        this.path.IsSome
        && this.watcher.IsNone
        && this.FileWatcherRestartTimeout.IsNone
        && not this.isInitializingArc
        && not this.isBusyWriting
        && not this.isRenamingArcRoot
        && not this.isCloseRequestPending
        && not this.isCloseApproved
        && not this.isWaitingForImportCleanup
        && not this.isWaitingForOperationsOnClose
        && not (this.window.isDestroyed ())

    member this.RestartFileWatcherOnFocus() = promise {
        if this.CanRestartFileWatcherOnFocus then
            this.FileWatcherRestartAttempt <- 0

            match! this.StartFileWatcherAsync() with
            | FileWatcherStartOutcome.Ready -> do! this.EnqueueWatcherResync()
            | _ -> ()
    }

    member this.ClearPendingFileWatcherState() =
        this.RootToken <- this.RootToken + 1
        this.fileWatcherReloadArcTimeout |> Option.iter Fable.Core.JS.clearTimeout
        this.fileWatcherReloadArcTimeout <- None
        this.fileWatcherPendingEvents.Clear()
        this.ClearImportedFileEvents()

    member this.StopFileWatcher() = promise {
        // Retire the instance first, so events that arrive while it closes are dropped.
        this.FileWatcherToken <- this.FileWatcherToken + 1
        this.CancelFileWatcherRestart()
        this.FileWatcherRestartAttempt <- 0
        this.BufferedFileWatcherEvents.Clear()
        this.IsFlushingBufferedFileWatcherEvents <- false

        // A start that still waits for the ready signal settles now, so its caller does not wait for the timeout.
        this.PendingFileWatcherStart
        |> Option.iter (fun settle -> settle FileWatcherStartOutcome.Stopped)

        this.PendingFileWatcherStart <- None
        let stoppedWatcher = this.watcher
        this.watcher <- None
        this.fileWatcherState <- FileWatcherState.Stopped

        match stoppedWatcher with
        | None -> ()
        | Some watcher ->
            try
                do! watcher.close ()
            with _ ->
                ()

        this.ClearPendingFileWatcherState()
    }
