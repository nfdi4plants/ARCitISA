/// The sync between the in-memory ARC and the structural files on disk, and the scopes that own the vault while something writes.
[<AutoOpen>]
module Main.ArcVaultSync

open Fable.Core
open Main
open Main.ARCtrlExtensions
open Main.ArcMerge
open Main.ArcDiskState
open Main.ArcVaultHelper
open Swate.Components.Shared
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.FileIOHelper
open ARCtrl
open Main.ArcVaultState

/// How a sync ended.
type internal ArcSyncOutcome =
    | Synced of finalScan: ArcDiskSnapshot
    /// The root changed or the vault closed while it ran, so nothing was applied.
    | Discarded
    | SyncFailed of exn

/// What Save knows before it writes.
type internal SaveDiskState =
    /// Also covers a write with syncFirst = false, where nothing was compared before the write.
    | NoUnsyncedFiles
    /// The sync before the Save failed, so these changed files are not in memory.
    | UnsyncedFiles of changes: ArcDiskChange list * syncError: exn

let private toFileEvents (changes: ArcDiskChange list) : FileEvent list =
    changes
    |> List.map (fun change ->
        match change with
        | Added path -> {
            EventName = EventName.Add
            Path = path
          }
        | Changed path -> {
            EventName = EventName.Change
            Path = path
          }
        | Removed path -> {
            EventName = EventName.Unlink
            Path = path
          }
    )

let internal changedPath (change: ArcDiskChange) : string =
    match change with
    | Added path
    | Changed path
    | Removed path -> path

let private writeBusyError () =
    exn "ARCitISA is still saving another change. Please wait a moment and try again."

let private externalOperationBusyError () =
    exn
        "Another operation, such as a Git pull or a file download, is still running. Please try again when it has finished."

let private loadingBusyError () =
    exn "ARCitISA is still loading external changes. Please wait a moment and try again."

let private resolvedUnit () : JS.Promise<unit> = JS.Constructors.Promise.resolve ()

type ArcVault with

    member internal this.ReportSyncStarted() =
        if not (this.window.isDestroyed ()) then
            swatelogfn this.window.id "Loading external changes to the ARC."
            WindowSend.send<IArcSyncRendererApi> this.window (fun api -> api.arcSyncStarted ())

    member internal this.ReportSyncFinished(changedPaths: string[], replacedUnsaved: string[]) =
        this.LastSyncFailed <- false

        if not (this.window.isDestroyed ()) then
            swatelogfn this.window.id "Loaded external changes to the ARC."

            WindowSend.send<IArcSyncRendererApi>
                this.window
                (fun api ->
                    api.arcSyncFinished {|
                        changedPaths = changedPaths
                        replacedUnsaved = replacedUnsaved
                    |}
                )

    member internal this.ReportSyncFailed(message: string) =
        this.LastSyncFailed <- true

        if not (this.window.isDestroyed ()) then
            swatelogfn this.window.id "Unable to load external changes to the ARC: %s" message
            WindowSend.send<IArcSyncRendererApi> this.window (fun api -> api.arcSyncFailed message)

    member internal this.PublishSyncSnapshot(snapshot: ARC, events: FileEvent list) : Result<unit, exn> =
        match this.arc with
        | None -> Error(arcNotOpenError ())
        | Some arcLocal ->
            // The snapshot is the disk baseline. Without hashes unchanged entities look new
            // and the next save rewrites their workbooks.
            baselineArcStaticHashes snapshot

            try
                let mergedArc = ARC.merge arcLocal snapshot events
                this.SetArc mergedArc
                this.RefreshHasUnsavedArcChangesFlag()
                Ok()
            with mergeError ->
                Error mergeError

    /// Loads the ARC until a scan after the load matches the scan before it, at most three times.
    /// Ok None means the root changed meanwhile.
    member internal this.LoadStableSnapshot
        (arcPath: string, token: int, before: ArcDiskSnapshot, attempt: int)
        : JS.Promise<Result<(ARC * ArcDiskSnapshot) option, exn>> =
        promise {
            match! this.SnapshotLoad arcPath with
            | Error errors -> return Error(exn (PathHelpers.formatContractErrors errors))
            | Ok snapshot ->
                if token <> this.RootToken then
                    return Ok None
                else
                    let! after = this.DiskScan this.isCaseInsensitive arcPath

                    if token <> this.RootToken then
                        return Ok None
                    elif (diff before after).IsEmpty then
                        return Ok(Some(snapshot, after))
                    elif attempt >= 3 then
                        return Error(exn "The ARC files kept changing while they were loading.")
                    else
                        return! this.LoadStableSnapshot(arcPath, token, after, attempt + 1)
        }

    /// Bumps the revision once and stamps the given ARC-relative paths with it.
    member internal this.RecordFileRevisions(paths: string[]) =
        this.ArcRevision <- this.ArcRevision + 1
        let caseInsensitive = this.isCaseInsensitive

        for path in paths do
            let key = PathKey.ofPath caseInsensitive (resolveArcPreviewPath path)
            this.FileRevisions <- this.FileRevisions.Add(key, this.ArcRevision)

    /// Records files that changed in memory outside a sync. An edit based on their older content is then refused, and open pages reload.
    /// The removed path, a deleted or renamed workbook, stays out of the message, because its page goes away or the renderer moves it.
    member internal this.PublishMemoryChange(removedPath: string, changedPaths: string[]) =
        this.RecordFileRevisions(Array.distinct (Array.append [| removedPath |] changedPaths))

        if changedPaths.Length > 0 && not (this.window.isDestroyed ()) then
            WindowSend.send<IArcSyncRendererApi>
                this.window
                (fun api ->
                    api.arcSyncFinished {|
                        changedPaths = changedPaths
                        replacedUnsaved = [||]
                    |}
                )

    /// The load and merge of a sync. The caller owns the vault and has set the mode to Syncing.
    member internal this.SyncFromDisk(before: ArcDiskSnapshot, announce: bool) : JS.Promise<ArcSyncOutcome> = promise {
        let token = this.RootToken

        match this.path with
        | None -> return Discarded
        | Some arcPath ->
            let fail (error: exn) =
                this.ReportSyncFailed error.Message
                SyncFailed error

            let discard () =
                // The renderer blocks input after an announced start, so the notice has to end.
                if announce then
                    this.ReportSyncFinished([||], [||])

                Discarded

            try
                if announce then
                    this.ReportSyncStarted()

                match! this.LoadStableSnapshot(arcPath, token, before, 1) with
                // A vault that closed during a failing load has no ARC left to report on.
                | Error _ when token <> this.RootToken -> return discard ()
                | Error error -> return fail error
                | Ok None -> return discard ()
                | Ok(Some(snapshot, after)) ->
                    let known = defaultArg this.KnownDisk Map.empty
                    let events = toFileEvents (diff known after)

                    // Read before the publish, which replaces the local ARC.
                    let replacedUnsaved =
                        match this.arc with
                        | Some local when local.hasInMemoryChanges () ->
                            ArcMergeHelper.replacedUnsavedFiles local snapshot events
                        | _ -> [||]

                    match this.PublishSyncSnapshot(snapshot, events) with
                    | Error error -> return fail error
                    | Ok() ->
                        this.KnownDisk <- Some after
                        let changedPaths = events |> List.map _.Path |> List.distinct |> List.toArray

                        if not events.IsEmpty then
                            this.RecordFileRevisions changedPaths

                        this.ReportSyncFinished(changedPaths, replacedUnsaved)
                        return Synced after
            with error ->
                return fail error
    }

    /// Checks the structural files against the known versions and loads them when they differ. It never rejects.
    /// While something else owns the vault it only records the request, which runs when that owner ends.
    member this.RequestSync(announce: bool) : JS.Promise<unit> =
        if this.path.IsNone || this.arc.IsNone || this.isInitializingArc then
            this.SyncRequested <- false
            this.SyncRequestAnnounces <- false
            resolvedUnit ()
        else
            match this.SyncMode with
            | ArcSyncMode.Idle ->
                this.SyncMode <- ArcSyncMode.Checking
                let shouldAnnounce = announce || this.SyncRequestAnnounces
                this.SyncRequested <- false
                this.SyncRequestAnnounces <- false
                let token = this.RootToken

                promise {
                    try
                        match this.path with
                        | None -> ()
                        | Some arcPath ->
                            let! before = this.DiskScan this.isCaseInsensitive arcPath

                            if token = this.RootToken then
                                match this.KnownDisk with
                                | None -> this.KnownDisk <- Some before
                                | Some known when (diff known before).IsEmpty ->
                                    // Memory matches disk again, so the failure notice of an earlier sync ends.
                                    if this.LastSyncFailed then
                                        this.ReportSyncFinished([||], [||])
                                | Some _ ->
                                    this.SyncMode <- ArcSyncMode.Syncing
                                    let! _ = this.SyncFromDisk(before, shouldAnnounce)
                                    ()
                    with error ->
                        if token = this.RootToken then
                            this.ReportSyncFailed error.Message

                    this.ReleaseOwnership()
                    do! this.RunRequestedSync()
                }
            | _ ->
                this.SyncRequested <- true
                this.SyncRequestAnnounces <- this.SyncRequestAnnounces || announce
                resolvedUnit ()

    member internal this.RunRequestedSync() : JS.Promise<unit> =
        match this.SyncMode with
        | ArcSyncMode.Idle when this.SyncRequested -> this.RequestSync false
        | _ -> resolvedUnit ()

    /// Takes the vault for an own write. Save and add (exclusive) are refused while another owner runs, except a running check, which they wait for. Rename and DataMap delete wait, and never run nested in an external operation, because a nested write is not scanned and its files would load as external changes.
    member internal this.AcquireOwnWrite(exclusive: bool) : JS.Promise<Result<unit, exn>> =
        JS.Constructors.Promise.Create(fun resolve _ ->
            let rec tryAcquire () : unit =
                match this.SyncMode with
                | ArcSyncMode.Idle ->
                    this.SyncMode <- ArcSyncMode.OwnWrite
                    resolve (Ok())
                | ArcSyncMode.Checking -> this.WaitForOwnerRelease tryAcquire
                | ArcSyncMode.Syncing -> resolve (Error(loadingBusyError ()))
                | ArcSyncMode.OwnWrite when exclusive -> resolve (Error(writeBusyError ()))
                | ArcSyncMode.OwnWrite -> this.WaitForOwnerRelease tryAcquire
                | ArcSyncMode.RootRename when exclusive -> resolve (Error(writeBusyError ()))
                | ArcSyncMode.RootRename -> this.WaitForOwnerRelease tryAcquire
                | ArcSyncMode.ExternalOperation _ when exclusive -> resolve (Error(writeBusyError ()))
                | ArcSyncMode.ExternalOperation _ -> this.WaitForOwnerRelease tryAcquire

            tryAcquire ()
        )

    member internal this.LeaveExternalOperation() : JS.Promise<unit> =
        match this.SyncMode with
        | ArcSyncMode.ExternalOperation depth when depth > 1 ->
            this.SyncMode <- ArcSyncMode.ExternalOperation(depth - 1)
            resolvedUnit ()
        | _ ->
            this.ExclusiveExternalOperation <- false
            this.ReleaseOwnership()
            this.RunRequestedSync()

    member internal this.RunOwnWrite<'T>
        (exclusive: bool, syncFirst: bool, operation: SaveDiskState -> JS.Promise<Result<'T, exn>>)
        : JS.Promise<Result<'T, exn>> =
        promise {
            let runOperation (state: SaveDiskState) = promise {
                try
                    return! operation state
                with error ->
                    return Error error
            }

            match! this.AcquireOwnWrite exclusive with
            | Error error -> return Error error
            | Ok() ->
                match this.path with
                | None ->
                    this.ReleaseOwnership()
                    return Error(arcNotOpenError ())
                | Some arcPath ->
                    let! preScan = promise {
                        try
                            let! scanned = this.DiskScan this.isCaseInsensitive arcPath
                            return Ok scanned
                        with error ->
                            return Error error
                    }

                    match preScan with
                    | Error error ->
                        this.ReleaseOwnership()
                        this.RunRequestedSync() |> Promise.start
                        return Error(exn $"Could not read the ARC folder: {error.Message}")
                    | Ok pre ->
                        let! prepared = promise {
                            match this.KnownDisk with
                            | Some known when syncFirst && not (diff known pre).IsEmpty ->
                                this.SyncMode <- ArcSyncMode.Syncing
                                let! outcome = this.SyncFromDisk(pre, true)
                                this.SyncMode <- ArcSyncMode.OwnWrite

                                // The load can take seconds, so the files are compared again after it failed.
                                let failedSync (syncError: exn) = promise {
                                    try
                                        let! rescan = this.DiskScan this.isCaseInsensitive arcPath
                                        return Ok(UnsyncedFiles(diff known rescan, syncError), rescan)
                                    with scanError ->
                                        return Error(exn $"Could not read the ARC folder: {scanError.Message}")
                                }

                                match outcome with
                                | Synced final -> return Ok(NoUnsyncedFiles, final)
                                | SyncFailed error -> return! failedSync error
                                | Discarded when this.path = Some arcPath ->
                                    return! failedSync (exn "The load was interrupted.")
                                | Discarded -> return Error(arcNotOpenError ())
                            | _ -> return Ok(NoUnsyncedFiles, pre)
                        }

                        match prepared with
                        | Error error ->
                            this.ReleaseOwnership()
                            this.RunRequestedSync() |> Promise.start
                            return Error error
                        | Ok(state, pre) ->
                            let! result = runOperation state

                            match result with
                            | Ok _ ->
                                if this.path = Some arcPath then
                                    try
                                        let! post = this.DiskScan this.isCaseInsensitive arcPath

                                        this.KnownDisk <- Some(acknowledge (defaultArg this.KnownDisk pre) pre post)
                                    with _ ->
                                        this.SyncRequested <- true
                            | Error _ ->
                                match state with
                                // The sync just failed and the next event or focus tries again. A request here
                                // would load right away and fail the same way.
                                | UnsyncedFiles _ -> ()
                                | NoUnsyncedFiles ->
                                    this.SyncRequested <- true
                                    this.SyncRequestAnnounces <- true

                            this.ReleaseOwnership()
                            // A Save does not wait for a load.
                            this.RunRequestedSync() |> Promise.start
                            return result
        }

    /// An own write: its changes to disk are already in memory, so they count as known afterwards.
    member this.WithOwnWriteScope<'T>
        (exclusive: bool, operation: unit -> JS.Promise<Result<'T, exn>>)
        : JS.Promise<Result<'T, exn>> =
        this.RunOwnWrite(exclusive = exclusive, syncFirst = false, operation = (fun _ -> operation ()))

    /// Takes the vault for an external operation and runs onAcquired in the tick of the acquisition, so the
    /// operation starts at once when nothing else owns the vault. Ok carries the result of onAcquired.
    /// Error means the vault refused an exclusive request.
    member internal this.AcquireExternalOperation<'R>
        (exclusive: bool, onAcquired: unit -> JS.Promise<'R>)
        : JS.Promise<Result<'R, exn>> =
        JS.Constructors.Promise.Create(fun resolve reject ->
            let run () =
                promise {
                    try
                        let! value = onAcquired ()
                        resolve (Ok value)
                    with error ->
                        reject error
                }
                |> Promise.start

            let rec tryAcquire () : unit =
                match this.SyncMode with
                | ArcSyncMode.Idle ->
                    this.SyncMode <- ArcSyncMode.ExternalOperation 1
                    this.ExclusiveExternalOperation <- exclusive
                    run ()
                | ArcSyncMode.Checking -> this.WaitForOwnerRelease tryAcquire
                | ArcSyncMode.Syncing when exclusive -> resolve (Error(loadingBusyError ()))
                | ArcSyncMode.OwnWrite when exclusive -> resolve (Error(writeBusyError ()))
                | ArcSyncMode.RootRename when exclusive -> resolve (Error(writeBusyError ()))
                | ArcSyncMode.RootRename
                | ArcSyncMode.Syncing
                | ArcSyncMode.OwnWrite -> this.WaitForOwnerRelease tryAcquire
                | ArcSyncMode.ExternalOperation _ when exclusive -> resolve (Error(externalOperationBusyError ()))
                | ArcSyncMode.ExternalOperation _ when this.ExclusiveExternalOperation ->
                    this.WaitForOwnerRelease tryAcquire
                | ArcSyncMode.ExternalOperation depth ->
                    this.SyncMode <- ArcSyncMode.ExternalOperation(depth + 1)
                    run ()

            tryAcquire ()
        )

    /// The part of an external operation scope that runs once the vault is acquired.
    member private this.RunExternalOperation<'T>
        (announce: bool, operation: unit -> JS.Promise<'T>)
        : JS.Promise<Result<'T, exn>> =
        promise {
            this.SyncRequested <- true
            this.SyncRequestAnnounces <- this.SyncRequestAnnounces || announce

            let! outcome = promise {
                try
                    let! value = operation ()
                    return Ok value
                with error ->
                    return Error error
            }

            do! this.LeaveExternalOperation()
            return outcome
        }

    /// Git, LFS, ARC creation and plain text writes. Files they change count as external and load in a normal sync when the last one ends, which the scope waits for. They wait while an entity delete or an import holds the vault.
    member this.WithExternalOperationScope<'T>(announce: bool, operation: unit -> JS.Promise<'T>) : JS.Promise<'T> = promise {
        match! this.AcquireExternalOperation(false, fun () -> this.RunExternalOperation(announce, operation)) with
        | Error error -> return raise error
        | Ok(Ok value) -> return value
        | Ok(Error error) -> return raise error
    }

    /// Entity delete and import. It is refused while another external operation runs. Other external operations, rename and DataMap delete wait until it ends, because they could change files that the delete or import has already read. Save and add are refused.
    member this.TryWithExclusiveExternalOperationScope<'T>
        (announce: bool, operation: unit -> JS.Promise<Result<'T, exn>>)
        : JS.Promise<Result<'T, exn>> =
        promise {
            match! this.AcquireExternalOperation(true, fun () -> this.RunExternalOperation(announce, operation)) with
            | Error error -> return Error error
            | Ok(Ok result) -> return result
            | Ok(Error error) -> return raise error
        }

    /// Owns the vault alone while the ARC folder moves. Every other caller waits or is refused, and reads the new path once it runs.
    member internal this.TryRunRootRename<'T>
        (operation: unit -> JS.Promise<Result<'T, exn>>)
        : JS.Promise<Result<'T, exn>> =
        promise {
            let! acquired =
                JS.Constructors.Promise.Create(fun resolve _ ->
                    let rec tryAcquire () : unit =
                        match this.SyncMode with
                        | ArcSyncMode.Idle ->
                            this.SyncMode <- ArcSyncMode.RootRename
                            resolve true
                        | ArcSyncMode.Checking -> this.WaitForOwnerRelease tryAcquire
                        | _ -> resolve false

                    tryAcquire ()
                )

            if not acquired then
                return Error(writeBusyError ())
            else
                let! result = promise {
                    try
                        return! operation ()
                    with error ->
                        return Error error
                }

                this.SyncRequested <- true
                this.ReleaseOwnership()
                this.RunRequestedSync() |> Promise.start
                return result
        }

    /// Loads changes while an import owns the vault, so the import can act on a failed load. The scope is exclusive, so no other operation can end during the load and release the vault.
    member internal this.SyncWithinExternalOperation(announce: bool) : JS.Promise<Result<unit, exn>> = promise {
        match this.SyncMode, this.path with
        | ArcSyncMode.ExternalOperation 1, Some arcPath when this.ExclusiveExternalOperation ->
            try
                let! before = this.DiskScan this.isCaseInsensitive arcPath

                match this.KnownDisk with
                | None ->
                    this.KnownDisk <- Some before
                    return Ok()
                | Some known when (diff known before).IsEmpty ->
                    if this.LastSyncFailed then
                        this.ReportSyncFinished([||], [||])

                    return Ok()
                | Some _ ->
                    this.SyncMode <- ArcSyncMode.Syncing
                    let! outcome = this.SyncFromDisk(before, announce)
                    this.SyncMode <- ArcSyncMode.ExternalOperation 1

                    match outcome with
                    | Synced _ -> return Ok()
                    | Discarded -> return Error(arcNotOpenError ())
                    | SyncFailed error -> return Error error
            with error ->
                return Error error
        | ArcSyncMode.ExternalOperation _, Some _ ->
            return Error(exn "Only an entity delete or an import can load changes inside its scope.")
        | _ -> return Error(arcNotOpenError ())
    }

    member this.CanRequestSyncOnFocus =
        this.path.IsSome
        && this.arc.IsSome
        && not this.isInitializingArc
        && not this.isRenamingArcRoot
        && not this.isCloseRequestPending
        && not this.isCloseApproved
        && not (this.window.isDestroyed ())
