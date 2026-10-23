[<AutoOpen>]
module Main.ArcVault

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
open ARCtrl

type ArcLoadCancelledException(targetWindowId: int) =
    inherit exn($"Loading the ARC was cancelled because window {targetWindowId} was closed.")
    member _.targetWindowId = targetWindowId

let (|ArcLoadCancelledException|_|) (error: exn) =
    match error with
    | :? ArcLoadCancelledException as cancellation -> Some cancellation.targetWindowId
    | _ -> None

exception ArcCreatedButClosedException of targetWindowId: int

/// Tests shorten this delay to cover timeout behavior without waiting 30 seconds.
let mutable closeWaitTimeoutMilliseconds = 30000

let private startFileWatcherOwnWriteArcMergeSuppression suppressionMs currentTimeout onElapsed =
    currentTimeout |> Option.iter Fable.Core.JS.clearTimeout
    Fable.Core.JS.setTimeout onElapsed suppressionMs

/// Describes the result of a queued watcher ARC merge for its caller.
[<RequireQualifiedAccess>]
type WatcherMergeOutcome =
    /// The merge updated the in-memory ARC, so the caller can publish its matching tree batch.
    | Applied
    /// The caller retains the ARC batch and retries it. After three consecutive deferrals it publishes the tree batch first.
    | Deferred
    /// Snapshot loading or merging failed, so the caller can publish the tree batch after logging the error.
    | Failed of exn

/// <summary>
/// Represents a vault window in the application, optionally associated with a file path.
/// </summary>
/// <param name="path">Can be None if not opened ARC.</param>
type ArcVault(window: BrowserWindow) =

    let fileWatcherOwnWriteArcMergeSuppressionMs = 500
    let arcMergeQueue = ArcMergeQueue(window.id)
    let mutable busyWritingDepth = 0
    let mutable writeGeneration = 0
    let mutable watcherDeferralCount = 0
    let mutable watcherEpoch = 0

    let mutable fileTreeUpdateTail: Fable.Core.JS.Promise<unit> =
        JS.Constructors.Promise.resolve ()

    member val window: BrowserWindow = window with get
    member val path: string option = None with get, set

    /// Whether the file system under the open ARC root ignores case. Starts as the platform default and is
    /// probed when the ARC opens.
    member val isCaseInsensitive: bool =
        (match Main.VersionControl.ProviderComposition.currentPathCaseSensitivity () with
         | VersionControlService.Abstractions.CaseInsensitive -> true
         | VersionControlService.Abstractions.CaseSensitive -> false) with get, set

    member val arc: ARC option = None with get, private set
    // This flag is intentionally coarse and can remain true even if later edits restore the previous logical state.
    //Good workaround, for a missing member 👍 Might even be more performant, than calculating a isDirty flag. On the other hand, it is unable to detect if changes are removed again. For example:
    //Add Assay 1 -> Set hasUnsavedArcChanges <- true
    //Remove Assay 1 -> Still true, even tough changes were removed again and it is in its original state.
    //I am not sure if performance of calculating changes or precision should be more important. Lets see in the future. Maybe you can add this comment as /// comment on this member?
    /// Dirty marker for unsaved in-memory ARC mutations.
    member val hasUnsavedArcChanges: bool = false with get, private set
    member val fileTree: Dictionary<string, FileEntry> = Dictionary<string, FileEntry>() with get, set
    member val watcher: Chokidar.IWatcher option = None with get, set

    /// Creates the file watcher. Tests replace it to observe or fake the watcher.
    member val internal FileWatcherFactory: (string -> bool option -> Chokidar.IWatcher) =
        (fun path usePolling -> createFileWatcher path usePolling) with get, set

    member val fileWatcherReloadArcTimeout: int option = None with get, set
    member val fileWatcherPendingEvents: ResizeArray<ArcVaultFileSystemEvent> = ResizeArray() with get
    member val fileWatcherPendingArcMergeEvents: ResizeArray<ArcVaultFileSystemEvent> = ResizeArray() with get
    /// The barrier stays None outside tests. The watcher path awaits it between snapshot loading and the final eligibility check.
    member val internal WatcherMergeBarrier: (unit -> Fable.Core.JS.Promise<unit>) option = None with get, set
    /// Imported paths awaiting their delayed Chokidar events. These events update the tree but must not re-merge the import.
    member val importedFileWatcherPaths: HashSet<string> = HashSet() with get
    member val private isBusyWritingValue: bool = false with get, set
    member val private fileWatcherOwnWriteArcMergeSuppressionTimeout: int option = None with get, set
    member val activeFileImport: ActiveFileImport option = None with get, set
    member val isWaitingForImportCleanup = false with get, set
    /// True only while an ARC is being opened or initially created in this vault.
    member val isInitializingArc = false with get, set
    member val isWaitingForOperationsOnClose = false with get, set
    member val isOperationCloseApproved = false with get, set

    /// Runs ARC merges sequentially so every operation observes the result of the preceding merge.
    member this.EnqueueArcMerge<'T>(operation: unit -> Fable.Core.JS.Promise<'T>) : Fable.Core.JS.Promise<'T> =
        arcMergeQueue.Enqueue operation

    /// Counts entries into write scopes. Only WithBusyWritingScope increments it.
    /// A nested scope increments it again. Comparing it before and after an await detects a write that started and finished in between.
    member this.WriteGeneration = writeGeneration

    member internal this.IncrementWatcherDeferralCount() =
        watcherDeferralCount <- watcherDeferralCount + 1
        watcherDeferralCount

    member internal this.ResetWatcherDeferralCount() = watcherDeferralCount <- 0

    member internal this.HasReachedWatcherDeferralLimit = watcherDeferralCount >= 3

    /// Counts pending-state resets. The watcher controller and ARC merge compare it with a captured value.
    member internal this.WatcherEpoch = watcherEpoch

    member internal this.IncrementWatcherEpoch() = watcherEpoch <- watcherEpoch + 1

    member internal this.FileTreeUpdateTail
        with get () = fileTreeUpdateTail
        and set value = fileTreeUpdateTail <- value

    /// Indicates whether the vault is currently busy writing changes to disk.
    /// When a write finishes, watcher ARC merges stay suppressed briefly to cover delayed own-write events.
    member this.isBusyWriting
        with get () = this.isBusyWritingValue
        and private set value =
            let wasBusyWriting = this.isBusyWritingValue
            this.isBusyWritingValue <- value

            if wasBusyWriting && not value then
                this.fileWatcherOwnWriteArcMergeSuppressionTimeout <-
                    startFileWatcherOwnWriteArcMergeSuppression
                        fileWatcherOwnWriteArcMergeSuppressionMs
                        this.fileWatcherOwnWriteArcMergeSuppressionTimeout
                        (fun () -> this.fileWatcherOwnWriteArcMergeSuppressionTimeout <- None)
                    |> Some

    /// Marks this vault busy until the supplied promise settles, including nested writes.
    /// The increment and the flag update sit before the promise builder, paired with the finally
    /// that undoes them. Fable.Promise runs the body in the calling tick, so the placement is for
    /// readability. What matters is that the flag is set before the caller yields, because the
    /// entry guards (WriteArc and its siblings, withExclusiveBusyWriting) read it right after a
    /// scope opens.
    member this.WithBusyWritingScope<'T>(operation: unit -> Fable.Core.JS.Promise<'T>) : Fable.Core.JS.Promise<'T> =
        busyWritingDepth <- busyWritingDepth + 1
        writeGeneration <- writeGeneration + 1

        if busyWritingDepth = 1 then
            this.isBusyWriting <- true

        promise {
            try
                return! operation ()
            finally
                busyWritingDepth <- busyWritingDepth - 1

                if busyWritingDepth = 0 then
                    this.isBusyWriting <- false
        }

    /// Indicates whether a captured watcher event is eligible to update the in-memory ARC.
    member this.IsFileWatcherArcMergeEligible =
        not this.isBusyWritingValue
        && this.fileWatcherOwnWriteArcMergeSuppressionTimeout.IsNone

    /// Indicates whether a close confirmation dialog is currently open.
    member val isCloseRequestPending: bool = false with get, set
    /// Allows a confirmed close to pass through the onClose handler exactly once.
    member val isCloseApproved: bool = false with get, set

    /// This function mutably sets the active ARC in memory without persisting to disk.
    member this.SetArc(arc: ARC) =
        this.arc <- Some arc

        if not (this.window.isDestroyed ()) then
            this.window.title <- Swate.Electron.Shared.ApplicationVersion.windowTitle (Some arc.Identifier)

    member this.ClearArc() =
        this.arc <- None
        this.hasUnsavedArcChanges <- false

        if not (this.window.isDestroyed ()) then
            this.window.title <- Swate.Electron.Shared.ApplicationVersion.windowTitle None

    /// Sets the dirty marker for unsaved in-memory ARC mutations.
    member this.RefreshHasUnsavedArcChangesFlag() =
        // Use this value to only send updates to the renderer when the dirty state actually changes. This avoids redundant updates.
        if this.arc.IsSome then
            let hasNewChanges = this.arc.Value.hasInMemoryChanges ()
            let valueIsChanging = this.hasUnsavedArcChanges <> hasNewChanges
            this.hasUnsavedArcChanges <- hasNewChanges

            if valueIsChanging then
                sendArcHasUnsavedChangesUpdate hasNewChanges this.window

[<AutoOpen>]
module ArcVaultExtensions =

    type ArcVault with

        member private this.LoadWatcherSnapshot() : Fable.Core.JS.Promise<Result<ARC, exn>> = promise {
            try
                match this.path, this.arc with
                | Some arcPath, None ->
                    match! ARC.LoadAsyncSwateZeroByteRepair arcPath with
                    | Error loadError -> return Error(exn (PathHelpers.formatContractErrors loadError))
                    | Ok snapshot -> return Ok snapshot
                | Some arcPath, Some _ ->
                    match! ARC.LoadAsyncSwate arcPath with
                    | Error loadError -> return Error(exn (PathHelpers.formatContractErrors loadError))
                    | Ok snapshot -> return Ok snapshot
                | _ -> return Error(arcNotOpenError ())
            with loadError ->
                return Error loadError
        }

        member private this.PublishWatcherSnapshot(snapshot: ARC, events: FileEvent list) : Result<unit, exn> =
            match this.arc with
            | None ->
                this.SetArc snapshot
                this.RefreshHasUnsavedArcChangesFlag()
                Ok()
            | Some arcLocal ->
                // The file watcher is our source of truth for changes made on disk. Establish the reloaded
                // ARC as a clean hash baseline before merging it; without this, unchanged entities have
                // missing/stale hashes and the next save can unnecessarily overwrite multiple XLSX files.
                baselineArcStaticHashes snapshot

                let mergeResult =
                    try
                        Ok(ARC.merge arcLocal snapshot events)
                    with mergeError ->
                        Error mergeError

                match mergeResult with
                | Error mergeError -> Error mergeError
                | Ok mergedArc ->
                    // ARC.merge may copy or reconstruct entities without retaining the hashes established
                    // above, so transfer the disk baseline to the merged ARC. Watcher-applied values then
                    // stay clean, while values that still differ because of local edits remain unsaved.
                    syncArcStaticHashes snapshot mergedArc
                    this.SetArc mergedArc
                    this.RefreshHasUnsavedArcChangesFlag()
                    Ok()

        member internal this.ApplyWatcherFileTreeEvents(events: ArcVaultFileSystemEvent list) =
            // Normalize against disk when the queued update reaches the serialized tail, so tree events use apply-time state.
            // Capture the epoch at the call so an update queued before a pending-state reset cannot publish the old root.
            let capturedWatcherEpoch = this.WatcherEpoch

            let queuedUpdate =
                this.FileTreeUpdateTail
                |> Promise.bind (fun () -> promise {
                    match this.path with
                    | None -> ()
                    | Some _ when capturedWatcherEpoch <> this.WatcherEpoch -> ()
                    | Some arcPath ->
                        let normalizedEvents = WatcherHelpers.normalizeAgainstDisk events
                        let mutable nextFileTree = this.fileTree
                        let mutable hasFileTreeChanges = false

                        for event in normalizedEvents do
                            try
                                if
                                    WatcherHelpers.eventNameEquals Chokidar.Events.Add event.EventName
                                    || WatcherHelpers.eventNameEquals Chokidar.Events.Change event.EventName
                                then
                                    let! changedFile = getFileEntryWithLfsMetadata arcPath event.AbsolutePath
                                    nextFileTree <- upsertFileEntry changedFile nextFileTree
                                    hasFileTreeChanges <- true
                                elif WatcherHelpers.eventNameEquals Chokidar.Events.AddDir event.EventName then
                                    let! addedDirectory = getFileEntry event.AbsolutePath
                                    nextFileTree <- upsertFileEntry addedDirectory nextFileTree
                                    hasFileTreeChanges <- true
                                elif
                                    WatcherHelpers.eventNameEquals Chokidar.Events.Unlink event.EventName
                                    || (WatcherHelpers.eventNameEquals Chokidar.Events.UnlinkDir event.EventName
                                        && not (WatcherHelpers.existsSyncWithExactName event.AbsolutePath))
                                then
                                    // Normalization above already turned the unlink of an existing file into a change. Directory unlinks
                                    // stay admitted, so they still need this check.
                                    nextFileTree <- removePathAndDescendants event.AbsolutePath nextFileTree
                                    hasFileTreeChanges <- true
                            with fileTreeError ->
                                swatelogfn
                                    this.window.id
                                    "Unable to update file tree for watcher event '%s' on '%s': %s"
                                    event.EventName
                                    event.RelativePath
                                    fileTreeError.Message

                        if hasFileTreeChanges && capturedWatcherEpoch = this.WatcherEpoch then
                            this.SetFileTree(nextFileTree)
                })

            this.FileTreeUpdateTail <- queuedUpdate |> Promise.catch (fun _ -> ())
            queuedUpdate

        /// Returns the load error to the import caller when loading fails. The import can then report that its in-memory merge did not happen.
        member this.TryTriggerArcInMemoryMergeOnFileWatcherEvents(events: ArcVaultFileSystemEvent list) = promise {
            let arcEvents = WatcherHelpers.toArcMergeEvents events

            return!
                this.EnqueueArcMerge(fun () -> promise {
                    match! this.LoadWatcherSnapshot() with
                    | Error loadError -> return Error loadError
                    | Ok snapshot -> return this.PublishWatcherSnapshot(snapshot, arcEvents)
                })
        }

        member this.TryApplyWatcherArcMergeIfEligible
            (events: ArcVaultFileSystemEvent list)
            : Fable.Core.JS.Promise<WatcherMergeOutcome> =
            // The epoch is captured before the queue wait, so a pending-state reset that lands
            // between the caller's snapshot and the dequeue still invalidates this batch.
            let capturedWatcherEpoch = this.WatcherEpoch

            this.EnqueueArcMerge(fun () -> promise {
                if not this.IsFileWatcherArcMergeEligible then
                    return WatcherMergeOutcome.Deferred
                elif capturedWatcherEpoch <> this.WatcherEpoch then
                    return WatcherMergeOutcome.Deferred
                else
                    let capturedWriteGeneration = this.WriteGeneration

                    match! this.LoadWatcherSnapshot() with
                    | Error loadError ->
                        if
                            not this.IsFileWatcherArcMergeEligible
                            || capturedWriteGeneration <> this.WriteGeneration
                            || capturedWatcherEpoch <> this.WatcherEpoch
                        then
                            return WatcherMergeOutcome.Deferred
                        else
                            return WatcherMergeOutcome.Failed loadError
                    | Ok snapshot ->
                        match this.WatcherMergeBarrier with
                        | Some barrier -> do! barrier ()
                        | None -> ()

                        if
                            not this.IsFileWatcherArcMergeEligible
                            || capturedWriteGeneration <> this.WriteGeneration
                            || capturedWatcherEpoch <> this.WatcherEpoch
                        then
                            return WatcherMergeOutcome.Deferred
                        else
                            match this.path with
                            | None ->
                                return
                                    WatcherMergeOutcome.Failed(
                                        exn "The ARC path was unavailable while applying a watcher merge."
                                    )
                            | Some root ->
                                let normalizedEvents = WatcherHelpers.normalizeAgainstDisk events

                                let arcEvents =
                                    WatcherHelpers.toArcMergeEvents normalizedEvents
                                    |> List.filter (fun event ->
                                        event.EventName <> EventName.Unlink
                                        || not (
                                            WatcherHelpers.existsSyncWithExactName (
                                                if isAbsolute event.Path then
                                                    event.Path
                                                else
                                                    join [| root; event.Path |]
                                            )
                                        )
                                    )

                                match this.PublishWatcherSnapshot(snapshot, arcEvents) with
                                | Ok() -> return WatcherMergeOutcome.Applied
                                | Error mergeError -> return WatcherMergeOutcome.Failed mergeError
            })

        member this.TriggerArcInMemoryMergeOnFileWatcherEvents(events: ArcVaultFileSystemEvent list) = promise {
            match! this.TryTriggerArcInMemoryMergeOnFileWatcherEvents events with
            | Ok() -> ()
            | Error mergeError ->
                swatelogfn this.window.id "Unable to merge ARC after file watcher event: %s" mergeError.Message
        }

        member internal this._FileEventController(sendMsgApi: IArcFileWatcherApi) =
            // A long write retries every 500 ms, so only the first deferral and the limit crossing are logged.
            let logWatcherDeferral deferralCount =
                match deferralCount with
                | 1 ->
                    swatelogfn
                        this.window.id
                        "Watcher merge deferred because a write is running or ran during the snapshot load."
                | 3 ->
                    swatelogfn
                        this.window.id
                        "Watcher merge deferred three times in a row. The tree batch is published on this and every further deferral until a merge applies."
                | _ -> ()

            let rec scheduleReload () =
                let mutable ownTimeoutId = None

                let finishReload () =
                    if
                        this.fileWatcherPendingEvents.Count > 0
                        || this.fileWatcherPendingArcMergeEvents.Count > 0
                    then
                        if
                            this.fileWatcherReloadArcTimeout.IsNone
                            || this.fileWatcherReloadArcTimeout = ownTimeoutId
                        then
                            this.fileWatcherReloadArcTimeout <- None
                            scheduleReload ()
                        else
                            ()
                    else if
                        this.fileWatcherReloadArcTimeout.IsNone
                        || this.fileWatcherReloadArcTimeout = ownTimeoutId
                    then
                        this.fileWatcherReloadArcTimeout <- None
                        sendMsgApi.IsLoadingChanges false
                    else
                        ()

                let timeoutId =
                    Fable.Core.JS.setTimeout
                        (fun () ->
                            promise {
                                swatelogfn this.window.id "Scheduled ARC reload triggered by file watcher."
                                let callbackEpoch = this.WatcherEpoch

                                if this.isBusyWriting then
                                    let hasPendingEvents =
                                        this.fileWatcherPendingEvents.Count > 0
                                        || this.fileWatcherPendingArcMergeEvents.Count > 0

                                    // A fire with nothing pending defers nothing, so it neither counts nor logs.
                                    if hasPendingEvents then
                                        let deferralCount = this.IncrementWatcherDeferralCount()
                                        logWatcherDeferral deferralCount

                                        if this.HasReachedWatcherDeferralLimit then
                                            let pendingEvents = this.fileWatcherPendingEvents |> Seq.toList
                                            this.fileWatcherPendingEvents.Clear()

                                            do! this.ApplyWatcherFileTreeEvents pendingEvents

                                    if
                                        this.fileWatcherPendingEvents.Count = 0
                                        && this.fileWatcherPendingArcMergeEvents.Count = 0
                                    then
                                        // Nothing is left to retry, so the retry chain ends here and the loading
                                        // flag goes off. The flag means that watcher changes are loading, so a
                                        // false during a write is truthful, and the next admitted event turns it
                                        // on again. The counter resets so the next batch starts its own count.
                                        this.ResetWatcherDeferralCount()
                                        finishReload ()
                                    elif this.fileWatcherReloadArcTimeout = ownTimeoutId then
                                        this.fileWatcherReloadArcTimeout <- None
                                        scheduleReload ()
                                    else
                                        ()
                                else
                                    let pendingEvents = this.fileWatcherPendingEvents |> Seq.toList
                                    let pendingArcMergeEvents = this.fileWatcherPendingArcMergeEvents |> Seq.toList
                                    this.fileWatcherPendingEvents.Clear()
                                    this.fileWatcherPendingArcMergeEvents.Clear()

                                    if pendingArcMergeEvents.IsEmpty then
                                        this.ResetWatcherDeferralCount()
                                        do! this.ApplyWatcherFileTreeEvents pendingEvents
                                        finishReload ()
                                    else
                                        match! this.TryApplyWatcherArcMergeIfEligible pendingArcMergeEvents with
                                        | (WatcherMergeOutcome.Applied | WatcherMergeOutcome.Failed _) as outcome ->
                                            match outcome with
                                            | WatcherMergeOutcome.Failed mergeError ->
                                                swatelogfn
                                                    this.window.id
                                                    "Unable to merge ARC after file watcher event: %s"
                                                    mergeError.Message
                                            | _ -> ()

                                            this.ResetWatcherDeferralCount()

                                            // FileTree updates are renderer-visible and can trigger an immediate openFile call.
                                            // A successful merge means that call reads the same ARC state represented by the published tree.
                                            // A batch snapshotted before a pending-state reset carries paths under the old root.
                                            if callbackEpoch = this.WatcherEpoch then
                                                do! this.ApplyWatcherFileTreeEvents pendingEvents

                                            finishReload ()
                                        | WatcherMergeOutcome.Deferred ->
                                            if callbackEpoch <> this.WatcherEpoch then
                                                swatelogfn
                                                    this.window.id
                                                    "Watcher merge deferred after watcher state was cleared. Dropping the stale batch."

                                                finishReload ()
                                            else
                                                let deferralCount = this.IncrementWatcherDeferralCount()

                                                logWatcherDeferral deferralCount

                                                // Read once: an overlapping callback can reset the counter during the await below.
                                                let reachedDeferralLimit = this.HasReachedWatcherDeferralLimit

                                                if reachedDeferralLimit then
                                                    // The limit latches during a write, so every later deferral publishes the
                                                    // tree batch too. A file the tree shows before its entity is merged opens
                                                    // through the disk fallback of openFile until the retained ARC batch merges
                                                    // after the write. That fallback shows text files as text and files above the
                                                    // preview limit or without text content as a no-preview page.
                                                    // A tree that hides every external change
                                                    // for the whole duration of a long write would be worse.
                                                    // The ARC batch is restored first, so a rejected tree update cannot lose it.
                                                    // Appending is enough because both consumers re-check the disk at apply time.
                                                    this.fileWatcherPendingArcMergeEvents.AddRange
                                                        pendingArcMergeEvents

                                                    do! this.ApplyWatcherFileTreeEvents pendingEvents
                                                else
                                                    ()

                                                if callbackEpoch = this.WatcherEpoch then
                                                    if not reachedDeferralLimit then
                                                        this.fileWatcherPendingArcMergeEvents.AddRange
                                                            pendingArcMergeEvents

                                                        this.fileWatcherPendingEvents.AddRange pendingEvents
                                                    else
                                                        ()

                                                    if
                                                        this.fileWatcherReloadArcTimeout.IsNone
                                                        || this.fileWatcherReloadArcTimeout = ownTimeoutId
                                                    then
                                                        this.fileWatcherReloadArcTimeout <- None
                                                        scheduleReload ()
                                                    else
                                                        ()
                                                else
                                                    ()
                            }
                            |> Promise.catch (fun ex ->
                                swatelogfn this.window.id "Scheduled ARC reload failed: %s" ex.Message
                                finishReload ()
                            )
                            |> Promise.start
                        )
                        500

                ownTimeoutId <- Some timeoutId
                this.fileWatcherReloadArcTimeout <- Some timeoutId

            fun (eventName: string) (path: string) ->

                swatelogfn this.window.id "File change detected: %s on %s" eventName path

                WatcherHelpers.queueFileWatcherEvent
                    (fun event ->
                        let isImportedPath =
                            this.importedFileWatcherPaths.Remove(
                                PathHelpers.normalizePath (event.AbsolutePath.ToLowerInvariant())
                            )

                        not isImportedPath
                        && (this.activeFileImport.IsSome || this.IsFileWatcherArcMergeEligible)
                    )
                    this.path
                    this.fileWatcherPendingEvents
                    this.fileWatcherPendingArcMergeEvents
                    eventName
                    path

                match this.fileWatcherReloadArcTimeout with
                | Some timeoutId ->
                    Fable.Core.JS.clearTimeout timeoutId
                    this.fileWatcherReloadArcTimeout <- None
                | None -> sendMsgApi.IsLoadingChanges true

                scheduleReload ()

        /// Applies an ARC content DTO to the in-memory ARC and marks the vault dirty.
        member this.UpdateArcByFileContentDTO(request: FileContentDTO) : Result<unit, exn> =
            match this.arc with
            | None -> Error(arcNotOpenError ())
            | Some arc ->
                let normalizedRequest =
                    Swate.Electron.Shared.FileIOHelper.FileContentDTO.normalizeArcFileRequestPath request

                match updateARCByFileContentDTO arc normalizedRequest with
                | Error saveError -> Error saveError
                | Ok newArc ->
                    this.SetArc(newArc)
                    this.RefreshHasUnsavedArcChangesFlag()
                    Ok()

        /// Writes the active in-memory ARC scaffold to disk without touching unmanaged files such as notes.
        member this.WriteArc() : Fable.Core.JS.Promise<Result<unit, exn>> = promise {
            match this.isBusyWriting, this.path, this.arc with
            | true, _, _ ->
                return Error(exn "ARCitISA is still saving another change. Please wait a moment and try again.")
            | false, Some arcPath, Some arc ->
                return!
                    this.WithBusyWritingScope(fun () -> promise {
                        try
                            match! arc.TryUpdateAsyncSwate(arcPath) with
                            | Error errors -> return Error(exn (PathHelpers.formatContractErrors errors))
                            | Ok _ ->
                                this.RefreshHasUnsavedArcChangesFlag()
                                return Ok()
                        with e ->
                            return Error(exn $"Failed to persist ARC to disk: {e.Message}")
                    })
            | _ -> return Error(arcNotOpenError ())
        }

        /// Adds a new ARC entity through ARCtrl's scoped add path.
        /// Watcher ARC merges are suppressed during the disk write; static hashes are resynced from disk afterwards.
        member this.AddArcFile(request: FileContentDTO) : Fable.Core.JS.Promise<Result<unit, exn>> = promise {
            match this.isBusyWriting, this.path, this.arc with
            | true, _, _ ->
                return Error(exn "ARCitISA is still saving another change. Please wait a moment and try again.")
            | false, Some arcPath, Some arcLocal ->
                let normalizedRequest =
                    Swate.Electron.Shared.FileIOHelper.FileContentDTO.normalizeArcFileRequestPath request

                match Swate.Electron.Shared.FileIOHelper.FileContentDTO.toArcFile normalizedRequest with
                | None -> return Error(exn $"Unsupported file type for adding: {normalizedRequest.fileType}")
                | Some arcFile ->
                    return!
                        this.WithBusyWritingScope(fun () -> promise {
                            match! arcLocal.TryAddArcFileAsync(arcPath, arcFile, false) with
                            | Error errors -> return Error(exn (PathHelpers.formatContractErrors errors))
                            | Ok _ ->
                                match! ARC.LoadAsyncSwate arcPath with
                                | Ok persistedArc ->
                                    baselineArcStaticHashes persistedArc
                                    syncAddedArcFileFromPersisted persistedArc arcLocal arcFile
                                    syncArcStaticHashes persistedArc arcLocal
                                    this.RefreshHasUnsavedArcChangesFlag()

                                    match arcFile with
                                    | ArcFiles.DataMap(Some parentInfo, _) ->
                                        let! refreshedFileTree =
                                            refreshFileTreeEntry
                                                arcPath
                                                (DatamapParentInfo.toPath parentInfo)
                                                this.fileTree

                                        this.SetFileTree refreshedFileTree
                                    | _ -> ()

                                    return Ok()
                                | Error loadErrors ->
                                    this.RefreshHasUnsavedArcChangesFlag()

                                    return
                                        Error(
                                            exn (
                                                "Added ARC file, but could not reload the persisted hash baseline: "
                                                + (PathHelpers.formatContractErrors loadErrors)
                                            )
                                        )
                        })
            | _ -> return Error(arcNotOpenError ())
        }

        member this.SetFileTree(fileTree: Dictionary<string, FileEntry>) =
            this.fileTree <- fileTree

            let rendererFileTree =
                match this.path with
                | Some arcPath -> toRendererFileTree arcPath fileTree.Values
                | None -> Dictionary<string, FileEntry>()

            WindowSend.send<IFileTreeRendererApi> this.window (fun api -> api.fileTreeUpdate rendererFileTree)

        member this.GetRendererFileTreeSnapshot() = promise {
            match this.path with
            | None -> return Dictionary<string, FileEntry>()
            | Some arcPath ->
                if this.fileTree.Count = 0 then
                    let! fileTree = getFileTree arcPath
                    this.fileTree <- fileTree

                return toRendererFileTree arcPath this.fileTree.Values
        }

        member this.LoadArc() = promise {
            if this.path.IsSome then
                match! ARC.LoadAsyncSwateZeroByteRepair this.path.Value with
                | Error e -> swatefailfn this.window.id "Unable to load ARC: %s" (PathHelpers.formatContractErrors e)
                | Ok _ when this.window.isDestroyed () -> return raise (ArcLoadCancelledException this.window.id)
                | Ok arc ->
                    this.SetArc(arc)
                    this.RefreshHasUnsavedArcChangesFlag()
            else
                swatefailfn this.window.id "No path set for StartFileWatcher."
        }

        member this.StartFileWatcher(?usePolling: bool) =
            if this.path.IsSome then
                match this.watcher with
                | Some _ -> ()
                | None ->
                    let watcher = this.FileWatcherFactory this.path.Value usePolling

                    let sendWatcherMessage = WindowSend.sender<IArcFileWatcherApi> this.window

                    let sendMsgApi: IArcFileWatcherApi = {
                        IsLoadingChanges =
                            fun isLoading -> sendWatcherMessage (fun api -> api.IsLoadingChanges isLoading)
                    }

                    watcher.on (Chokidar.Events.All, this._FileEventController sendMsgApi) |> ignore
                    this.watcher <- Some watcher
            else
                swatefailfn this.window.id "No path set for StartFileWatcher."

        member this.ClearPendingFileWatcherState() =
            this.IncrementWatcherEpoch()
            this.ResetWatcherDeferralCount()
            this.fileWatcherReloadArcTimeout |> Option.iter Fable.Core.JS.clearTimeout
            this.fileWatcherReloadArcTimeout <- None
            this.fileWatcherPendingEvents.Clear()
            this.fileWatcherPendingArcMergeEvents.Clear()
            this.importedFileWatcherPaths.Clear()

        member this.StopFileWatcher() = promise {
            match this.watcher with
            | None -> ()
            | Some watcher ->
                try
                    do! watcher.close ()
                with _ ->
                    ()

            this.watcher <- None
            this.ClearPendingFileWatcherState()
        }

        member internal this.RestoreEmptyVaultAfterFailedInitialization() = promise {
            do! this.StopFileWatcher()

            let hadUnsavedArcChanges = this.hasUnsavedArcChanges

            this.isInitializingArc <- false
            this.path <- None
            this.fileTree.Clear()

            try
                this.ClearArc()
            with error ->
                swatelogfn this.window.id "Failed to reset ARC window presentation: %s" error.Message

            if not (this.window.isDestroyed ()) then
                if hadUnsavedArcChanges then
                    try
                        sendArcHasUnsavedChangesUpdate false this.window
                    with error ->
                        swatelogfn this.window.id "Failed to reset ARC dirty state in renderer: %s" error.Message
        }

        /// Loads the ARC during initialization. The watcher is activated only after the initial
        /// file-tree snapshot has been installed, so an older scan cannot overwrite watcher updates.
        member this.Startup() = promise {
            match this.path with
            | Some arcRoot ->
                let! isCaseInsensitive = detectCaseInsensitiveAsync arcRoot
                this.isCaseInsensitive <- isCaseInsensitive

                swatelogfn
                    this.window.id
                    "ARC root file system is %s."
                    (if isCaseInsensitive then
                         "case-insensitive"
                     else
                         "case-sensitive")
            | None -> ()

            do! this.LoadArc()
        }

        /// Finalizes a prepared ARC after every asynchronous initialization step succeeded.
        member internal this.FinalizeArcInitialization(fileTree: Dictionary<string, FileEntry>) =
            match this.window.isDestroyed (), this.path with
            | true, _ -> raise (ArcLoadCancelledException this.window.id)
            | false, None -> swatefailfn this.window.id "Unable to commit ARC initialization without a path."
            | false, Some normalizedPath ->
                this.fileTree <- fileTree
                this.StartFileWatcher()
                this.isInitializingArc <- false

                WindowSend.send<IPathChangeRendererApi> this.window (fun api -> api.pathChange (Some normalizedPath))

                this.SetFileTree fileTree

                // Every ARC open asks for the prewarm. It does nothing once the diff workers
                // started, and a failed prewarm is tried again by the next open. Prewarming never
                // throws and does not hold up the ARC.
                Main.VersionControl.TextDiffWorkers.prewarm ()

        member this.OpenARC(path: string) = promise {
            match this.path with
            | Some _ -> swatefailfn this.window.id "Unable to open ARC in vault bound to ARC."
            | None ->
                let normalizedPath = PathHelpers.normalizePath path

                swatelogfn this.window.id "path: %s" normalizedPath
                this.isInitializingArc <- true
                this.path <- Some normalizedPath

                try
                    do! this.Startup()
                with error ->
                    do! this.RestoreEmptyVaultAfterFailedInitialization()
                    return raise error
        }

        member this.CreateARC(path: string, identifier: string) = promise {
            match this.path, this.arc with
            | Some _, _ -> swatefailfn this.window.id "Unable to create ARC in vault bound to path."
            | _, Some _ -> swatefailfn this.window.id "Unable to create ARC in vault bound to ARC."
            | None, None ->
                let normalizedPath = PathHelpers.normalizePath path

                try
                    this.isInitializingArc <- true
                    let arc = ARC(identifier)
                    this.path <- Some normalizedPath
                    this.SetArc(arc)
                    this.RefreshHasUnsavedArcChangesFlag()

                    do!
                        this.WithBusyWritingScope(fun () -> promise {
                            match! arc.TryWriteAsyncSwate(normalizedPath) with
                            | Ok _ -> ()
                            | Error errors ->
                                failwithf
                                    "Could not write ARC, failed with the following errors %s"
                                    (PathHelpers.formatContractErrors errors)
                        })

                    try
                        do! this.Startup()
                    with ArcLoadCancelledException targetWindowId ->
                        return raise (ArcCreatedButClosedException targetWindowId)

                with error ->
                    do! this.RestoreEmptyVaultAfterFailedInitialization()
                    return raise error
        }

        member this.RenameOpenArcRoot(newName: string) : Fable.Core.JS.Promise<Result<string, exn>> = promise {
            // A running write, such as a Git LFS download or free, holds absolute paths into the
            // ARC folder. Moving the folder under it would make its next step fail, so the rename is refused until the write ends.
            let busyWritingError () =
                exn
                    "Cannot rename the ARC folder while ARCitISA is still writing to it. Wait until the running operation finishes, then try again."

            match this.path with
            | None -> return Error(arcNotOpenError ())
            | Some _ when this.isBusyWriting -> return Error(busyWritingError ())
            | Some currentPath ->
                let hadWatcher = this.watcher.IsSome

                if hadWatcher then
                    do! this.StopFileWatcher()
                else
                    this.ClearPendingFileWatcherState()

                // A write can start while the watcher stops.
                let! renameResult =
                    if this.isBusyWriting then
                        promise { return Error(busyWritingError ()) }
                    else
                        renameOpenArcRootDirectoryOnDisk currentPath newName

                match renameResult with
                | Error renameError ->
                    if hadWatcher then
                        this.StartFileWatcher()

                    return Error renameError
                | Ok renamedPath ->
                    this.path <- Some renamedPath

                    match Main.VersionControl.WorkspaceSessionHost.tryCurrent () with
                    | Some host ->
                        let! moved = host.WorkspaceRenamed(currentPath, renamedPath) |> Async.StartAsPromise

                        match moved with
                        | Ok() -> ()
                        | Error message ->
                            swatelogfn
                                this.window.id
                                "ARC folder was renamed to '%s', but its version control binding could not follow: %s"
                                renamedPath
                                message
                    | None -> ()

                    if this.arc.IsNone then
                        try
                            do! this.LoadArc()
                        with loadError ->
                            swatelogfn
                                this.window.id
                                "ARC folder was renamed to '%s', but reload failed: %s"
                                renamedPath
                                loadError.Message

                    if hadWatcher then
                        try
                            this.StartFileWatcher()
                        with watcherError ->
                            swatelogfn
                                this.window.id
                                "ARC folder was renamed to '%s', but file watcher restart failed: %s"
                                renamedPath
                                watcherError.Message

                    try
                        let! fileTree = getFileTree renamedPath
                        this.SetFileTree fileTree
                    with refreshError ->
                        swatelogfn
                            this.window.id
                            "ARC folder was renamed to '%s', but file tree refresh failed: %s"
                            renamedPath
                            refreshError.Message

                    try
                        WindowSend.send<IPathChangeRendererApi>
                            this.window
                            (fun api -> api.pathChange (Some renamedPath))
                    with notifyError ->
                        swatelogfn
                            this.window.id
                            "ARC folder was renamed to '%s', but renderer path notification failed: %s"
                            renamedPath
                            notifyError.Message

                    return Ok renamedPath
        }


/// Describes the outcome of an ARC lifecycle action performed by the controller.
[<RequireQualifiedAccess>]
type ArcOpenDisposition =
    | OpenedInCurrent of path: string
    | OpenedInNewWindow of path: string
    | FocusedExisting of path: string
    | CreatedInCurrent of path: string
    | CreatedInNewWindow of path: string

    member this.CreatedArcPath =
        match this with
        | CreatedInCurrent path
        | CreatedInNewWindow path -> Some path
        | OpenedInCurrent _
        | OpenedInNewWindow _
        | FocusedExisting _ -> None

module ArcOpenDisposition =
    let path =
        function
        | ArcOpenDisposition.OpenedInCurrent p
        | ArcOpenDisposition.OpenedInNewWindow p
        | ArcOpenDisposition.FocusedExisting p
        | ArcOpenDisposition.CreatedInCurrent p
        | ArcOpenDisposition.CreatedInNewWindow p -> p


type ArcVaults() =

    let pathInitializations = Dictionary<string, JS.Promise<unit>>()

    /// Key is window.id
    member val Vaults = Dictionary<int, ArcVault>() with get

    member this.Paths = this.Vaults.Values |> Seq.choose (fun x -> x.path) |> Array.ofSeq

    member this.BroadcastRecentARCs() =
        let recentARCs = RECENT_ARCS.Get()

        this.Vaults.Values
        |> Array.ofSeq
        |> fun arr ->
            if arr.Length > 0 then
                arr
                |> Array.iter (fun vault ->
                    WindowSend.send<IRecentArcsRendererApi> vault.window (fun api -> api.recentARCsUpdate recentARCs)
                )

    /// Centralized side-effect: update recent ARCs store and broadcast to all windows.
    member private this.TrackRecentAndBroadcast(arcPath: string) =
        let normalizedArcPath = PathHelpers.normalizePath arcPath
        RECENT_ARCS.Add(normalizedArcPath) |> ignore
        this.BroadcastRecentARCs()

    member this.DisposeVault(id: int) =
        match this.Vaults.TryGetValue(id) with
        | false, _ -> swatefailfn id "Failed to remove vault."
        | true, vault ->
            vault.StopFileWatcher() |> Promise.start
            this.Vaults.Remove(id) |> ignore

            match Main.VersionControl.WorkspaceSessionHost.tryCurrent (), vault.path with
            | Some host, Some path ->
                if host.IsIdle path then
                    if this.TryGetVaultByPath path |> Option.isNone then
                        host.CloseSession path |> Async.StartAsPromise |> Promise.start
                else
                    let rec closeWhenIdle () = promise {
                        do! host.WhenOperationsComplete(host.RunningOperationIds path)

                        if this.TryGetVaultByPath path |> Option.isSome then ()
                        elif not (host.IsIdle path) then do! closeWhenIdle ()
                        else do! host.CloseSession path |> Async.StartAsPromise
                    }

                    closeWhenIdle () |> Promise.start
            | _ -> ()

            vault.path |> Option.iter (fun p -> RECENT_ARCS.Inactivate(p) |> ignore)
            this.BroadcastRecentARCs()
            printfn $"[ARCitISA] Removed vault '{id}'"

    member this.ResolveCloseRequest(windowId: int, decision: SaveBeforeQuitDecision) = promise {
        match this.TryGetVault(windowId) with
        | None ->
            let message = "Close request ignored. No vault found."
            swatelogfn windowId "%s" message
            return Error(exn message)
        | Some(vault: ArcVault) ->
            vault.isCloseRequestPending <- false

            match decision with
            | SaveBeforeQuitDecision.CancelClose ->
                swatelogfn windowId "Close request cancelled by user."
                return Ok()
            | SaveBeforeQuitDecision.CloseWithoutSaving ->
                swatelogfn windowId "Close request approved by user. Closing without saving."
                vault.RefreshHasUnsavedArcChangesFlag()
                vault.isCloseApproved <- true
                vault.window.close ()
                return Ok()
            | SaveBeforeQuitDecision.SaveAndClose ->
                swatelogfn windowId "Close request approved by user. Closing after main save."

                if vault.hasUnsavedArcChanges then
                    let! persistResult = vault.WriteArc()

                    match persistResult with
                    | Error saveError -> return Error saveError
                    | Ok() ->
                        vault.isCloseApproved <- true
                        vault.window.close ()
                        return Ok()
                else
                    vault.isCloseApproved <- true
                    vault.window.close ()
                    return Ok()
    }

    member this.OnCloseWindow(window: BrowserWindow, vault: ArcVault, id: int) =
        window.onClose (fun closeEvent ->
            let operationCloseApproved = vault.isOperationCloseApproved
            vault.isOperationCloseApproved <- false

            let host = Main.VersionControl.WorkspaceSessionHost.tryCurrent ()

            let cancelWindowReads () =
                host
                |> Option.iter (fun currentHost ->
                    let mutationIds = currentHost.RunningMutationIdsForWindow id |> Set.ofArray

                    currentHost.RunningOperationIdsForWindow id
                    |> Array.filter (fun operationId -> not (Set.contains operationId mutationIds))
                    |> Array.iter (fun operationId -> currentHost.Cancel(operationId, Some id) |> ignore)
                )

            if not vault.isCloseApproved then
                if vault.activeFileImport.IsSome then
                    closeEvent.preventDefault ()

                    if not vault.isWaitingForImportCleanup then
                        vault.isWaitingForImportCleanup <- true

                        promise {
                            let! importResult = promise {
                                try
                                    let activeImport = vault.activeFileImport.Value

                                    if activeImport.State.phase = FileImportPhase.Copying then
                                        activeImport.AbortController.abort ()

                                    return! activeImport.Completion
                                with importError ->
                                    return Error importError
                            }

                            vault.isWaitingForImportCleanup <- false

                            match importResult with
                            | Ok _ -> window.close ()
                            | Error importError ->
                                swatelogfn id "Active import failed while closing: %s" importError.Message

                                if not (window.isDestroyed ()) then
                                    dialog.showErrorBox (
                                        "Could not close ARCitISA",
                                        $"The active file import could not be rolled back completely. The window was kept open to avoid hiding a partial import.\n\n{importError.Message}"
                                    )
                        }
                        |> Promise.start
                elif vault.isInitializingArc then
                    swatelogfn id "Closing window directly because ARC initialization is still in progress."
                elif
                    host
                    |> Option.exists (fun currentHost ->
                        (currentHost.RunningMutationIdsForWindow id).Length > 0
                        && not operationCloseApproved
                    )
                then
                    closeEvent.preventDefault ()

                    if not vault.isWaitingForOperationsOnClose then
                        vault.isWaitingForOperationsOnClose <- true

                        promise {
                            try
                                match host with
                                | Some currentHost ->
                                    let! response =
                                        dialog.showMessageBox (
                                            ?window = Some(unbox<BaseWindow> window),
                                            ?options =
                                                Some(
                                                    Dialog.ShowMessageBox.Options(
                                                        message =
                                                            "A version control operation is still running in this window.",
                                                        ``type`` = Enums.Dialog.ShowMessageBox.Options.Type.Question,
                                                        detail =
                                                            $"Closing the window cancels it. The window closes once the operation has stopped, or after {closeWaitTimeoutMilliseconds / 1000} seconds if it does not stop.",
                                                        buttons = [|
                                                            "Cancel operation and close"
                                                            "Keep window open"
                                                        |],
                                                        defaultId = 1,
                                                        cancelId = 1
                                                    )
                                                )
                                        )

                                    if response.response = 0.0 then
                                        let operationIds = currentHost.RunningOperationIdsForWindow id

                                        operationIds
                                        |> Array.iter (fun operationId ->
                                            currentHost.Cancel(operationId, Some id) |> ignore
                                        )

                                        let waitForOperations = promise {
                                            do! currentHost.WhenOperationsComplete operationIds
                                            return true
                                        }

                                        let waitForTimeout = promise {
                                            do! Promise.sleep closeWaitTimeoutMilliseconds
                                            return false
                                        }

                                        let! operationsCompleted = race [| waitForOperations; waitForTimeout |]

                                        if not operationsCompleted then
                                            let stillRunning = currentHost.RunningOperationIdsForWindow id

                                            if stillRunning.Length > 0 then
                                                swatelogfn
                                                    id
                                                    "Timed out waiting for version control operations before close: %s"
                                                    (String.concat ", " stillRunning)

                                        vault.isOperationCloseApproved <- true
                                        vault.isWaitingForOperationsOnClose <- false

                                        if not (window.isDestroyed ()) then
                                            window.close ()
                                    else
                                        vault.isWaitingForOperationsOnClose <- false
                                | None -> vault.isWaitingForOperationsOnClose <- false
                            with closeError ->
                                vault.isWaitingForOperationsOnClose <- false

                                swatelogfn
                                    id
                                    "Unable to close while a version control operation is running: %s"
                                    closeError.Message
                        }
                        |> Promise.start
                elif vault.hasUnsavedArcChanges then
                    closeEvent.preventDefault ()

                    if not vault.isCloseRequestPending then
                        vault.isCloseRequestPending <- true

                        WindowSend.send<IMainSaveBeforeQuitApi> vault.window (fun api -> api.requestSaveBeforeQuit ())
                else
                    cancelWindowReads ()
                    swatelogfn id "Closing window directly because no unsaved ARC changes are present."
            else
                cancelWindowReads ()
        )

        window.onClosed (fun () ->
            vault.isWaitingForImportCleanup <- false
            vault.isWaitingForOperationsOnClose <- false
            vault.isOperationCloseApproved <- false
            vault.isCloseRequestPending <- false
            vault.isCloseApproved <- false

            // Runs before the vault disposal, whose session close waits for these operations.
            Main.VersionControl.TextDiffHandles.windowClosed id

            if this.Vaults.ContainsKey(id) then
                this.DisposeVault(id)
        )

    member private this.CleanupFailedRegistration(window: BrowserWindow, vault: ArcVault, id: int) = promise {
        do! vault.StopFileWatcher()
        this.Vaults.Remove(id) |> ignore

        if not (window.isDestroyed ()) then
            window.destroy ()
    }

    member private this.RegisterVaultCore
        (initialize: ArcVault -> JS.Promise<unit>, ?onFailureBeforeCleanup: exn -> unit)
        : JS.Promise<ArcVault> =
        promise {
            let window = createWindow ()
            let id = window.id
            let vault = ArcVault(window)
            this.Vaults.Add(id, vault)
            this.OnCloseWindow(window, vault, id)

            try
                do! loadWindow window
                do! initialize vault

                window.focus ()
                swatelogfn id "Register window"

                return vault
            with error ->
                onFailureBeforeCleanup
                |> Option.iter (fun notifyFailure ->
                    try
                        notifyFailure error
                    with notificationError ->
                        swatelogfn id "Failed to report window registration error: %s" notificationError.Message
                )

                let targetWasDestroyed = window.isDestroyed ()
                do! this.CleanupFailedRegistration(window, vault, id)

                match error with
                | ArcLoadCancelledException _
                | ArcCreatedButClosedException _ -> return raise error
                | _ when targetWasDestroyed -> return raise (ArcLoadCancelledException id)
                | _ -> return raise error
        }

    member this.RegisterVault(?onFailureBeforeCleanup: exn -> unit) : JS.Promise<int> = promise {
        let! vault =
            this.RegisterVaultCore((fun _ -> promise { return () }), ?onFailureBeforeCleanup = onFailureBeforeCleanup)

        return vault.window.id
    }

    member private this.RegisterVaultWithValidatedArc(path: string) =
        this.RegisterVaultCore(fun vault -> promise {
            do! vault.OpenARC(path)
            let! fileTree = this.InitializeFileTreeForActiveVault(vault.window.id, vault, path)
            vault.FinalizeArcInitialization fileTree
        })

    member private this.ValidateArcRoot(path: string) = promise {
        let! arcRootExists = ARCtrl.FileSystemHelper.directoryExistsAsync path

        if not arcRootExists then
            return Error(exn $"The ARC cannot be found at location: '{path}'.")
        else
            let investigationPath =
                ARCtrl.ArcPathHelper.combine path ARCtrl.ArcPathHelper.InvestigationFileName

            let! investigationExists = ARCtrl.FileSystemHelper.fileExistsAsync investigationPath

            if investigationExists then
                return Ok()
            else
                return
                    Error(
                        exn
                            $"The folder does not contain the required ARC investigation file '{ARCtrl.ArcPathHelper.InvestigationFileName}'."
                    )
    }

    member this.RegisterVaultWithNewArc(path: string, newIdentifier: string) : JS.Promise<ArcVault> =
        this.RegisterVaultCore(fun vault -> promise {
            do! vault.CreateARC(path, newIdentifier)
            let! fileTree = this.InitializeFileTreeForCreatedVault(vault.window.id, vault, path)

            try
                vault.FinalizeArcInitialization fileTree
            with ArcLoadCancelledException targetWindowId ->
                return raise (ArcCreatedButClosedException targetWindowId)
        })

    member this.OpenARCInVault(windowId: int, path: string) = promise {
        let normalizedArcPath = PathHelpers.normalizePath path

        match! this.ValidateArcRoot normalizedArcPath with
        | Error error -> return raise error
        | Ok() ->
            match this.Vaults.TryGetValue windowId with
            | false, _ -> return failwith $"Vault with window-id '{windowId}' not found."
            | true, vault ->
                do!
                    this.InitializeArcInCurrentVault(
                        windowId,
                        vault,
                        normalizedArcPath,
                        false,
                        fun () -> vault.OpenARC(normalizedArcPath)
                    )
    }

    member this.CreateARCInVault(windowId: int, path: string, identifier: string) = promise {
        match this.Vaults.TryGetValue windowId with
        | false, _ -> failwith $"Vault with window-id '{windowId}' not found."
        | true, vault ->
            do!
                this.InitializeArcInCurrentVault(
                    windowId,
                    vault,
                    PathHelpers.normalizePath path,
                    true,
                    fun () -> vault.CreateARC(path, identifier)
                )

        return ()
    }

    member this.TryGetVault(windowId: int) =
        match this.Vaults.TryGetValue windowId with
        | true, vault -> Some vault
        | false, _ -> None

    member this.TryGetVaultByPath(path: string) =
        this.Vaults.Values
        |> Seq.tryFind (fun v -> v.path |> Option.exists (fun vaultPath -> PathHelpers.pathsEqual vaultPath path))

    member private this.EnsureVaultIsStillActive(windowId: int, expectedVault: ArcVault) =
        match this.TryGetVault windowId with
        | Some _ when not (expectedVault.window.isDestroyed ()) -> ()
        | _ -> raise (ArcLoadCancelledException windowId)

    member private this.InitializeFileTreeForActiveVault(windowId: int, expectedVault: ArcVault, arcPath: string) = promise {
        let! fileTreeResult = promise { return! getFileTree arcPath } |> Promise.result

        this.EnsureVaultIsStillActive(windowId, expectedVault)

        match fileTreeResult with
        | Ok fileTree -> return fileTree
        | Error error -> return raise error
    }

    member private this.InitializeFileTreeForCreatedVault(windowId: int, expectedVault: ArcVault, arcPath: string) = promise {
        try
            return! this.InitializeFileTreeForActiveVault(windowId, expectedVault, arcPath)
        with ArcLoadCancelledException targetWindowId ->
            return raise (ArcCreatedButClosedException targetWindowId)
    }

    member private this.InitializeArcInCurrentVault
        (windowId: int, vault: ArcVault, arcPath: string, create: bool, initialize: unit -> JS.Promise<unit>)
        =
        promise {
            try
                do! initialize ()

                let! fileTree =
                    if create then
                        this.InitializeFileTreeForCreatedVault(windowId, vault, arcPath)
                    else
                        this.InitializeFileTreeForActiveVault(windowId, vault, arcPath)

                try
                    vault.FinalizeArcInitialization fileTree
                with ArcLoadCancelledException targetWindowId when create ->
                    return raise (ArcCreatedButClosedException targetWindowId)
            with error ->
                do! vault.RestoreEmptyVaultAfterFailedInitialization()
                return raise error
        }

    member private this.WithPathInitialization<'T>
        (arcPath: string, operation: unit -> JS.Promise<'T>)
        : JS.Promise<'T> =
        let pathKey = PathHelpers.normalizePathForFsComparison arcPath

        let rec waitForTurn () = promise {
            match pathInitializations.TryGetValue pathKey with
            | true, pending ->
                // A failed/cancelled owner must not poison the path. Once it settles, retry and
                // either use its active vault or become the next initializer.
                let! _ = pending |> Promise.result
                return! waitForTurn ()
            | false, _ ->
                let mutable release = ignore

                let completion =
                    JS.Constructors.Promise.Create(fun resolve _ -> release <- fun () -> resolve ())

                pathInitializations.Add(pathKey, completion)

                try
                    return! operation ()
                finally
                    pathInitializations.Remove(pathKey) |> ignore
                    release ()
        }

        waitForTurn ()

    // ── ARC Lifecycle Controller ──────────────────────────────────────────
    // All open/create/focus decisions are made here.
    // IPC handlers should delegate to these methods.

    /// Open an existing ARC at the given path.
    /// Decision: already-open → focus, calling window empty → open there, else → new window.
    member this.OpenOrFocusArc(callingWindowId: int, arcPath: string) = promise {
        let normalizedArcPath = PathHelpers.normalizePath arcPath

        return!
            this.WithPathInitialization(
                normalizedArcPath,
                fun () -> promise {
                    match this.TryGetVaultByPath normalizedArcPath with
                    | Some vault ->
                        vault.window.focus ()
                        this.TrackRecentAndBroadcast(normalizedArcPath)
                        return ArcOpenDisposition.FocusedExisting normalizedArcPath
                    | None ->
                        match! this.ValidateArcRoot normalizedArcPath with
                        | Error error -> return raise error
                        | Ok() ->
                            match this.TryGetVault callingWindowId with
                            | Some vault when vault.path.IsNone ->
                                do!
                                    this.InitializeArcInCurrentVault(
                                        callingWindowId,
                                        vault,
                                        normalizedArcPath,
                                        false,
                                        fun () -> vault.OpenARC(normalizedArcPath)
                                    )

                                this.TrackRecentAndBroadcast(normalizedArcPath)
                                return ArcOpenDisposition.OpenedInCurrent normalizedArcPath
                            | _ ->
                                let! _ = this.RegisterVaultWithValidatedArc(normalizedArcPath)
                                this.TrackRecentAndBroadcast(normalizedArcPath)
                                return ArcOpenDisposition.OpenedInNewWindow normalizedArcPath
                }
            )
    }

    /// Create a new ARC at the given path with the given identifier.
    /// Decision: path already open → focus, calling window empty → create there, else → new window.
    member this.CreateOrFocusArc(callingWindowId: int, arcPath: string, identifier: string) = promise {
        let normalizedArcPath = PathHelpers.normalizePath arcPath

        return!
            this.WithPathInitialization(
                normalizedArcPath,
                fun () -> promise {
                    match this.TryGetVaultByPath normalizedArcPath with
                    | Some vault ->
                        vault.window.focus ()
                        this.TrackRecentAndBroadcast(normalizedArcPath)
                        return ArcOpenDisposition.FocusedExisting normalizedArcPath
                    | None ->
                        match this.TryGetVault callingWindowId with
                        | Some vault when vault.path.IsNone ->
                            do!
                                this.InitializeArcInCurrentVault(
                                    callingWindowId,
                                    vault,
                                    normalizedArcPath,
                                    true,
                                    fun () -> vault.CreateARC(normalizedArcPath, identifier)
                                )

                            this.TrackRecentAndBroadcast(normalizedArcPath)
                            return ArcOpenDisposition.CreatedInCurrent normalizedArcPath
                        | _ ->
                            let! _ = this.RegisterVaultWithNewArc(normalizedArcPath, identifier)
                            this.TrackRecentAndBroadcast(normalizedArcPath)
                            return ArcOpenDisposition.CreatedInNewWindow normalizedArcPath
                }
            )
    }


let ARC_VAULTS: ArcVaults = ArcVaults()
