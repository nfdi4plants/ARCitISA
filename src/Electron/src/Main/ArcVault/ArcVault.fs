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

/// State of the permanent structural file watcher of a vault.
type FileWatcherState =
    | Stopped
    | Starting
    | Running
    /// The watcher is absent after a failed start, a readiness timeout or an error. Focus and the restart backoff bring it back.
    | Degraded of reason: string
    /// Restarts stopped after the last backoff step because the system ran out of watches or file descriptors.
    | Unavailable of reason: string

type FileWatcherStartOutcome =
    | Ready
    | TimedOut
    | Failed of exn
    /// The watcher was stopped, replaced or retired while the start was running, so the caller owns nothing.
    | Stopped

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

/// What the focus check compares to notice Git changes made outside the app.
/// Head is the commit id that HEAD resolves to.
type GitStateStamp = { Head: string; IndexMtimeMs: float }

/// Finds the git directory of a working tree without starting git. A .git folder is the git directory. A .git file
/// (worktrees, submodules) names it in a gitdir line, relative to the root unless the path is absolute.
let private tryResolveGitDirAsync (root: string) : Fable.Core.JS.Promise<string option> = promise {
    let dotGit = join [| root; ".git" |]

    try
        let! stats = statAsync dotGit

        if stats.isDirectory () then
            return Some dotGit
        else
            let! content = readFileAsync dotGit TextEncoding.Utf8

            return
                content.Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.tryFind (fun line -> line.StartsWith("gitdir:", System.StringComparison.Ordinal))
                |> Option.map (fun line ->
                    let target = line.Substring("gitdir:".Length).Trim()

                    if isAbsolute target then
                        target
                    else
                        resolve [| root; target |]
                )
    with error ->
        if tryGetNodeErrorCode error = Some "ENOENT" then
            return None
        else
            return raise error
}

let private tryReadTextFileAsync (path: string) : Fable.Core.JS.Promise<string option> = promise {
    try
        let! content = readFileAsync path TextEncoding.Utf8
        return Some content
    with error ->
        if tryGetNodeErrorCode error = Some "ENOENT" then
            return None
        else
            return raise error
}

let rec firstSomeAsync
    (candidates: (unit -> Fable.Core.JS.Promise<string option>) list)
    : Fable.Core.JS.Promise<string option> =
    promise {
        match candidates with
        | [] -> return None
        | next :: rest ->
            match! next () with
            | Some found -> return Some found
            | None -> return! firstSomeAsync rest
    }

/// Resolves HEAD to a commit id without starting git. A symbolic HEAD is followed to the loose ref file and then
/// to packed-refs, in the git directory and in the common directory of a linked worktree. A detached HEAD already
/// holds the commit id. A ref that cannot be found keeps the HEAD text, which still changes when the branch switches.
let private readHeadCommitAsync (gitDir: string) : Fable.Core.JS.Promise<string> = promise {
    let! headText = readFileAsync (join [| gitDir; "HEAD" |]) TextEncoding.Utf8
    let head = headText.Trim()

    if not (head.StartsWith("ref:", System.StringComparison.Ordinal)) then
        return head
    else
        let refName = head.Substring("ref:".Length).Trim()
        let! commonText = tryReadTextFileAsync (join [| gitDir; "commondir" |])

        let directories =
            match commonText with
            | Some text -> [ gitDir; resolve [| gitDir; text.Trim() |] ]
            | None -> [ gitDir ]

        let loose =
            directories
            |> List.map (fun directory () -> promise {
                let! content = tryReadTextFileAsync (join [| directory; refName |])
                return content |> Option.map (fun text -> text.Trim())
            })

        let packed =
            directories
            |> List.map (fun directory () -> promise {
                let! content = tryReadTextFileAsync (join [| directory; "packed-refs" |])

                return
                    content
                    |> Option.bind (fun text ->
                        text.Split('\n')
                        |> Array.map (fun line -> line.Trim())
                        |> Array.tryFind (fun line ->
                            not (line.StartsWith("#", System.StringComparison.Ordinal))
                            && line.EndsWith(" " + refName, System.StringComparison.Ordinal)
                        )
                        |> Option.map (fun line -> line.Substring(0, line.IndexOf ' '))
                    )
            })

        let! found = firstSomeAsync (loose @ packed)
        return found |> Option.defaultValue head
}

let private readGitStateStampAsync (gitDir: string) : Fable.Core.JS.Promise<GitStateStamp> = promise {
    let! head = readHeadCommitAsync gitDir

    // A fresh repository has no index file yet.
    let! indexMtimeMs = promise {
        try
            let! stats = statAsync (join [| gitDir; "index" |])
            return stats.mtimeMs
        with error ->
            if tryGetNodeErrorCode error = Some "ENOENT" then
                return 0.0
            else
                return raise error
    }

    return {
        Head = head
        IndexMtimeMs = indexMtimeMs
    }
}

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

    let mutable arcPath: string option = None

    let defaultCaseInsensitive =
        match Main.VersionControl.ProviderComposition.currentPathCaseSensitivity () with
        | VersionControlService.Abstractions.CaseInsensitive -> true
        | VersionControlService.Abstractions.CaseSensitive -> false

    /// Every change of the main-process file tree runs through this writer. After a changed job it sends the
    /// full renderer snapshot through the existing fileTreeUpdate push.
    let fileTreeWriter =
        FileTreeWriter(
            defaultCaseInsensitive,
            (fun message -> swatelogfn window.id "%s" message),
            (fun tree ->
                match arcPath with
                | Some root ->
                    let rendererFileTree = toRendererFileTree root tree.Entries
                    WindowSend.send<IFileTreeRendererApi> window (fun api -> api.fileTreeUpdate rendererFileTree)
                | None -> ()
            )
        )

    member val window: BrowserWindow = window with get

    member this.path
        with get () = arcPath
        and set (value: string option) = arcPath <- value

    /// Whether the file system under the open ARC root ignores case. Starts as the platform default and is
    /// probed when the ARC opens.
    member val isCaseInsensitive: bool = defaultCaseInsensitive with get, set

    member val arc: ARC option = None with get, private set
    // This flag is intentionally coarse and can remain true even if later edits restore the previous logical state.
    //Good workaround, for a missing member 👍 Might even be more performant, than calculating a isDirty flag. On the other hand, it is unable to detect if changes are removed again. For example:
    //Add Assay 1 -> Set hasUnsavedArcChanges <- true
    //Remove Assay 1 -> Still true, even tough changes were removed again and it is in its original state.
    //I am not sure if performance of calculating changes or precision should be more important. Lets see in the future. Maybe you can add this comment as /// comment on this member?
    /// Dirty marker for unsaved in-memory ARC mutations.
    member val hasUnsavedArcChanges: bool = false with get, private set
    member this.FileTreeWriter = fileTreeWriter
    member val watcher: Chokidar.IWatcher option = None with get, set

    /// What the watcher is doing. Unavailable is the "file watching unavailable" state.
    member val fileWatcherState: FileWatcherState = FileWatcherState.Stopped with get, set
    /// Identifies the current watcher instance. Listeners capture it and drop events once it has changed.
    member val internal FileWatcherToken: int = 0 with get, set
    member val internal FileWatcherEventHandler: (string -> string -> unit) option = None with get, set
    /// Events that arrive while the ARC is initializing. They run after the initial tree snapshot is installed.
    member val internal BufferedFileWatcherEvents: ResizeArray<unit -> unit> = ResizeArray() with get
    /// True from the end of initialization until the buffered events are flushed. Events keep buffering meanwhile.
    member val internal IsFlushingBufferedFileWatcherEvents: bool = false with get, set
    /// Settles the start that still waits for the watcher to become ready. StopFileWatcher calls it.
    member val internal PendingFileWatcherStart: (FileWatcherStartOutcome -> unit) option = None with get, set
    /// Permission errors already logged, as code and path. A repeated error stays quiet.
    member val internal ReportedFileWatcherPermissionErrors: HashSet<string> = HashSet() with get
    member val internal FileWatcherReadinessTimeoutMs: int = 10000 with get, set
    member val internal FileWatcherRestartBackoffMs: int[] = [| 1000; 5000; 30000 |] with get, set
    /// A restarted watcher that stays error-free this long starts its backoff over.
    member val internal FileWatcherStableAfterMs: int = 60000 with get, set
    member val internal FileWatcherRestartAttempt: int = 0 with get, set
    member val internal FileWatcherRestartTimeout: int option = None with get, set
    member val internal FileWatcherStableTimeout: int option = None with get, set
    /// True while RenameOpenArcRoot runs. The focus handler leaves the watcher alone then.
    member val isRenamingArcRoot: bool = false with get, set

    /// Memoized LFS state of the open ARC. None while no ARC is open.
    member val LargeObjectIndex: Main.LargeObjectIndex.LargeObjectIndex option = None with get, set

    /// Lists the LFS objects of a repository. Tests replace it to avoid a real session.
    member val internal LargeObjectListing: string
        -> VersionControlService.Abstractions.OperationContext
        -> Fable.Core.JS.Promise<Result<Swate.Electron.Shared.VersionControlTypes.ObjectStateDto[], string>> =
        Main.LargeObjectIndex.listObjectsFromSession with get, set

    /// Invalidations within this window cause one listing.
    member val internal LargeObjectInvalidationDebounceMs: int = 500 with get, set
    member val internal LargeObjectInvalidationTimeout: int option = None with get, set
    /// The Git state at the start of the last listing or at the previous focus check. None when it could not be read.
    member val internal GitStateStamp: GitStateStamp option = None with get, set
    member val internal ResolvedGitDir: (string * string) option = None with get, set
    /// The next index starts here, so versions never fall below what the renderer has applied.
    member val internal LargeObjectVersionFloor: int = 0 with get, set
    member val internal GitStateCheckErrorLogged: bool = false with get, set

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

        /// Queues a full scan of the ARC folder. The scanned tree replaces the current one.
        member this.EnqueueFileTreeScan() : Fable.Core.JS.Promise<unit> =
            match this.path with
            | Some arcPath ->
                let caseInsensitive = this.isCaseInsensitive
                this.FileTreeWriter.EnqueueReplace(fun () -> scanFileTreeAsync arcPath caseInsensitive)
            | None -> promise { return () }

        member private this.PushLargeObjectIndex(index: Main.LargeObjectIndex.LargeObjectIndex) =
            match this.LargeObjectIndex with
            | Some current when obj.ReferenceEquals(current, index) ->
                let snapshot = index.Snapshot this.isCaseInsensitive
                WindowSend.send<IFileTreeRendererApi> this.window (fun api -> api.largeObjectIndexUpdate snapshot)
            | _ -> ()

        /// Answers the pull of the renderer. A vault without an index is not loaded.
        member this.GetLargeObjectIndexSnapshot() : LargeObjectIndexSnapshot =
            match this.LargeObjectIndex with
            | Some index -> index.Snapshot this.isCaseInsensitive
            | None -> {|
                version = 0
                state = LargeObjectIndexStatus.NotLoaded
                caseInsensitive = this.isCaseInsensitive
                entries = [||]
              |}

        /// Cancels a running listing and removes the index. The renderer receives no further updates from it.
        member this.DisposeLargeObjectIndex() =
            this.LargeObjectInvalidationTimeout |> Option.iter Fable.Core.JS.clearTimeout
            this.LargeObjectInvalidationTimeout <- None

            this.LargeObjectIndex
            |> Option.iter (fun index ->
                this.LargeObjectVersionFloor <- max this.LargeObjectVersionFloor (index.Version + 1)
                index.Dispose()
            )

            this.LargeObjectIndex <- None
            this.GitStateStamp <- None
            this.ResolvedGitDir <- None
            this.GitStateCheckErrorLogged <- false

        /// Replaces the index with a new one for the current root and builds it in the background. A seed keeps
        /// the previous ready state and version visible while the new listing runs.
        member internal this.StartLargeObjectIndex(?seed: Main.LargeObjectIndex.ReadyIndex) =
            this.DisposeLargeObjectIndex()

            match this.path with
            | Some root when not (this.window.isDestroyed ()) ->
                let index =
                    Main.LargeObjectIndex.LargeObjectIndex(
                        (fun context -> this.LargeObjectListing root context),
                        (fun () -> this.isCaseInsensitive),
                        (fun message -> swatelogfn this.window.id "%s" message),
                        Main.LargeObjectIndex.DefaultTimeLimitMs,
                        (match seed with
                         | Some ready -> ready.Version
                         | None -> this.LargeObjectVersionFloor),
                        seed
                    )

                this.LargeObjectIndex <- Some index

                index.Subscribe(fun () -> this.PushLargeObjectIndex index) |> ignore

                // The Git state is recorded first, so a change made while or after the listing runs is seen by the next focus check.
                promise {
                    do! this.RecordGitStateStamp index

                    match this.LargeObjectIndex with
                    | Some current when obj.ReferenceEquals(current, index) ->
                        if seed.IsSome then
                            index.Invalidate()
                        else
                            index.EnsureBuilt()
                    | _ -> ()
                }
                |> Promise.start
            | _ -> ()

        /// Resolves the git directory of the open root. A found directory is kept. A missing one is looked up again,
        /// because the ARC can be initialized later.
        member private this.ResolveGitDirAsync(root: string) : Fable.Core.JS.Promise<string option> = promise {
            match this.ResolvedGitDir with
            | Some(resolvedRoot, resolved) when resolvedRoot = root -> return Some resolved
            | _ ->
                let! found = tryResolveGitDirAsync root
                this.ResolvedGitDir <- found |> Option.map (fun dir -> root, dir)
                return found
        }

        /// Stores the current Git state as the baseline of the next focus check. A failed read or a missing git directory
        /// leaves no baseline, so the first focus check records one.
        member private this.RecordGitStateStamp
            (index: Main.LargeObjectIndex.LargeObjectIndex)
            : Fable.Core.JS.Promise<unit> =
            promise {
                let! stamp = promise {
                    match this.path with
                    | Some root ->
                        try
                            match! this.ResolveGitDirAsync root with
                            | Some directory ->
                                let! read = readGitStateStampAsync directory
                                return Some read
                            | None -> return None
                        with _ ->
                            return None
                    | None -> return None
                }

                match this.LargeObjectIndex with
                | Some current when obj.ReferenceEquals(current, index) -> this.GitStateStamp <- stamp
                | _ -> ()
            }

        /// Marks the index stale. Calls within the debounce window cause one rebuild.
        member this.InvalidateLargeObjectIndex() =
            if this.LargeObjectIndex.IsSome then
                this.LargeObjectInvalidationTimeout |> Option.iter Fable.Core.JS.clearTimeout

                this.LargeObjectInvalidationTimeout <-
                    Fable.Core.JS.setTimeout
                        (fun () ->
                            this.LargeObjectInvalidationTimeout <- None

                            match this.LargeObjectIndex with
                            | Some index ->
                                promise {
                                    do! this.RecordGitStateStamp index

                                    match this.LargeObjectIndex with
                                    | Some current when obj.ReferenceEquals(current, index) -> index.Invalidate()
                                    | _ -> ()
                                }
                                |> Promise.start
                            | None -> ()
                        )
                        this.LargeObjectInvalidationDebounceMs
                    |> Some

        /// True when a window focus should compare the Git state with the previous check.
        member this.CanCheckGitStateOnFocus =
            this.path.IsSome
            && this.LargeObjectIndex.IsSome
            && not this.isInitializingArc
            && not this.isBusyWriting
            && not this.isRenamingArcRoot
            && not this.isCloseRequestPending
            && not this.isCloseApproved
            && not (this.window.isDestroyed ())

        /// Invalidates the index when HEAD or the index file of the repository changed since the previous check.
        /// Without a baseline from the start of the last listing, the check only records the state. Errors are logged once.
        member this.CheckGitStateOnFocus() : Fable.Core.JS.Promise<unit> = promise {
            match this.path, this.LargeObjectIndex with
            | Some root, Some index ->
                try
                    let! gitDir = this.ResolveGitDirAsync root

                    match gitDir with
                    | None -> ()
                    | Some directory ->
                        let! stamp = readGitStateStampAsync directory

                        match this.LargeObjectIndex with
                        | Some current when obj.ReferenceEquals(current, index) ->
                            let previous = this.GitStateStamp
                            this.GitStateStamp <- Some stamp
                            this.GitStateCheckErrorLogged <- false

                            match previous with
                            | Some before when before <> stamp ->
                                let signals =
                                    [
                                        if before.Head <> stamp.Head then
                                            "HEAD"
                                        if before.IndexMtimeMs <> stamp.IndexMtimeMs then
                                            "index"
                                    ]
                                    |> String.concat " and "

                                swatelogfn
                                    this.window.id
                                    "The Git state changed after window focus (%s), so the large object index is rebuilt."
                                    signals

                                this.InvalidateLargeObjectIndex()
                            | _ -> ()
                        | _ -> ()
                with error ->
                    if not this.GitStateCheckErrorLogged then
                        this.GitStateCheckErrorLogged <- true
                        swatelogfn this.window.id "Unable to check the Git state after window focus: %s" error.Message
            | _ -> ()
        }

        /// Queues one writer job that reconciles several directories, each through its nearest loaded ancestor.
        member this.EnqueueFileTreeDirectoriesReconcile(directoryPaths: string[]) : Fable.Core.JS.Promise<unit> =
            match this.path with
            | Some arcPath ->
                this.FileTreeWriter.Enqueue(fun tree ->
                    reconcileDirtyDirectoriesAsync
                        arcPath
                        tree
                        (fun directory error ->
                            swatelogfn
                                this.window.id
                                "Unable to reconcile file tree directory '%s': %s"
                                directory
                                error.Message
                        )
                        directoryPaths
                )
            | None -> promise { return () }

        /// Queues a reconcile of one directory, or of its nearest loaded ancestor.
        member this.EnqueueFileTreeDirectoryReconcile(directoryPath: string) : Fable.Core.JS.Promise<unit> =
            this.EnqueueFileTreeDirectoriesReconcile [| directoryPath |]

        /// Queues one writer job that reconciles the root and every loaded directory, from the root down.
        /// It makes no LFS call, because the renderer joins LFS state by path from the large object index. An empty tree gets a full scan.
        member this.EnqueueFileTreeRevalidateLoaded() : Fable.Core.JS.Promise<unit> =
            if this.FileTreeWriter.Tree.Count = 0 then
                this.EnqueueFileTreeScan()
            else
                match this.path with
                | Some arcPath ->
                    this.FileTreeWriter.Enqueue(fun tree ->
                        reconcileDirtyDirectoriesAsync
                            arcPath
                            tree
                            (fun directory error ->
                                swatelogfn
                                    this.window.id
                                    "Unable to revalidate file tree directory '%s': %s"
                                    directory
                                    error.Message
                            )
                            tree.LoadedDirectories
                    )
                | None -> promise { return () }

        /// Saves the ARC, then reconciles the parent directory of every written path in one writer job.
        member this.SaveArc() : Fable.Core.JS.Promise<Result<unit, exn>> = promise {
            match! this.WriteArc() with
            | Error saveError -> return Error saveError
            | Ok writtenPaths ->
                match this.path with
                | None -> return Error(arcNotOpenError ())
                | Some arcPath ->
                    let parents =
                        writtenPaths
                        |> Array.choose (fun relativePath ->
                            PathHelpers.tryGetParentPath (join [| arcPath; relativePath |])
                        )
                        |> Array.distinct

                    do! this.EnqueueFileTreeDirectoriesReconcile parents
                    return Ok()
        }

        /// Marks the directories of a watcher batch dirty and reconciles them in one writer job.
        /// A pending-state reset after the call drops the job, so an update queued before it cannot publish the old root.
        member internal this.ApplyWatcherFileTreeEvents(events: ArcVaultFileSystemEvent list) =
            let capturedWatcherEpoch = this.WatcherEpoch

            let dirtyDirectories =
                WatcherHelpers.dirtyDirectoriesOfEvents this.isCaseInsensitive events

            // A changed .gitattributes can change which files are LFS pointers.
            let gitAttributesKey = PathKey.ofPath this.isCaseInsensitive ".gitattributes"

            if
                events
                |> List.exists (fun event ->
                    PathKey.ofPath this.isCaseInsensitive event.RelativePath = gitAttributesKey
                )
            then
                this.InvalidateLargeObjectIndex()

            this.FileTreeWriter.Enqueue(fun tree -> promise {
                match this.path with
                | Some arcPath when capturedWatcherEpoch = this.WatcherEpoch ->
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
                | _ -> return false
            })

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
        /// Returns the ARC-relative paths the save wrote. GetUpdateContracts updates the stored static hashes,
        /// so the contracts are read once and both written and listed from that one array.
        member this.WriteArc() : Fable.Core.JS.Promise<Result<string[], exn>> = promise {
            match this.isBusyWriting, this.path, this.arc with
            | true, _, _ ->
                return Error(exn "ARCitISA is still saving another change. Please wait a moment and try again.")
            | false, Some arcPath, Some arc ->
                return!
                    this.WithBusyWritingScope(fun () -> promise {
                        try
                            let contracts = arc.GetUpdateContractsSwate()
                            let writtenPaths = contractWrittenPaths contracts

                            match! ARCtrl.Contract.fullFillContractBatchAsync arcPath contracts with
                            | Error errors -> return Error(exn (PathHelpers.formatContractErrors errors))
                            | Ok _ ->
                                this.RefreshHasUnsavedArcChangesFlag()
                                return Ok writtenPaths
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
                                        let datamapPath = join [| arcPath; DatamapParentInfo.toPath parentInfo |]

                                        match PathHelpers.tryGetParentPath datamapPath with
                                        | Some parentDirectory ->
                                            do! this.EnqueueFileTreeDirectoryReconcile parentDirectory
                                        | None -> ()
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

        member this.GetRendererFileTreeSnapshot() = promise {
            match this.path with
            | None -> return Dictionary<string, FileEntry>()
            | Some _ when this.isInitializingArc -> return Dictionary<string, FileEntry>()
            | Some arcPath ->
                if this.FileTreeWriter.Tree.Count = 0 then
                    do! this.EnqueueFileTreeScan()

                return toRendererFileTree arcPath this.FileTreeWriter.Tree.Entries
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

        /// Starts the watcher and resolves when chokidar reports ready, or after FileWatcherReadinessTimeoutMs.
        /// It never raises. A failed start closes the watcher and leaves the vault degraded, so a readable ARC still opens.
        member internal this.StartFileWatcherAsync(?usePolling: bool) : Fable.Core.JS.Promise<FileWatcherStartOutcome> =
            match this.watcher, this.path with
            | Some _, _ -> Fable.Core.JS.Constructors.Promise.resolve FileWatcherStartOutcome.Ready
            | None, None ->
                Fable.Core.JS.Constructors.Promise.resolve (FileWatcherStartOutcome.Failed(arcNotOpenError ()))
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
                        this.FileWatcherEventHandler <- Some handler

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
                                    promise {
                                        if
                                            this.watcher.IsNone && this.path.IsSome && not (this.window.isDestroyed ())
                                        then
                                            match! this.StartFileWatcherAsync() with
                                            | FileWatcherStartOutcome.Ready -> do! this.EnqueueWatcherResync()
                                            | FileWatcherStartOutcome.TimedOut -> this.ScheduleFileWatcherRestart None
                                            | FileWatcherStartOutcome.Failed startError ->
                                                this.ScheduleFileWatcherRestart(Some startError)
                                            | FileWatcherStartOutcome.Stopped -> ()
                                    }
                                    |> Promise.catch (fun restartError ->
                                        swatelogfn
                                            this.window.id
                                            "Unable to restart the structural file watcher: %s"
                                            restartError.Message
                                    )
                                    |> Promise.start
                            )
                            steps.[min attempt (steps.Length - 1)]
                    )

        /// Catches up on changes that the watcher missed while it was down. The entity files are merged again
        /// and the loaded directories of the file tree are revalidated.
        member internal this.EnqueueWatcherResync() : Fable.Core.JS.Promise<unit> = promise {
            match this.path, this.FileWatcherEventHandler with
            | Some root, Some handle ->
                let known zone =
                    match this.arc with
                    | None -> []
                    | Some arc ->
                        match zone with
                        | ArcEntityPathRules.AddZone.Studies -> arc.Studies |> Seq.map (fun s -> s.Identifier)
                        | ArcEntityPathRules.AddZone.Assays -> arc.Assays |> Seq.map (fun a -> a.Identifier)
                        | ArcEntityPathRules.AddZone.Workflows -> arc.Workflows |> Seq.map (fun w -> w.Identifier)
                        | ArcEntityPathRules.AddZone.Runs -> arc.Runs |> Seq.map (fun r -> r.Identifier)
                        |> Seq.toList

                for zone in ArcEntityPathRules.allAddZones do
                    let knownIdentifiers = known zone
                    let zoneFolderPath = join [| root; ArcEntityPathRules.zoneFolderName zone |]

                    let! onDisk = promise {
                        try
                            let! names = readdirAsync zoneFolderPath

                            return
                                names
                                |> Array.filter (fun name ->
                                    not (name.StartsWith ".") && not (List.contains name knownIdentifiers)
                                )
                                |> Array.toList
                        with listError ->
                            match tryGetNodeErrorCode listError with
                            | Some "ENOENT"
                            | Some "ENOTDIR" -> return []
                            | _ -> return raise listError
                    }

                    for identifier in knownIdentifiers @ onDisk do
                        let eventName =
                            if List.contains identifier knownIdentifiers then
                                "change"
                            else
                                "add"

                        for relativePath in ArcEntityPathRules.buildCanonicalEntityPaths zone identifier do
                            handle eventName (join [| root; relativePath |])

                handle "change" (join [| root; ARCtrl.ArcPathHelper.InvestigationFileName |])

                do! this.EnqueueFileTreeRevalidateLoaded()
            | _ -> ()
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
            this.IncrementWatcherEpoch()
            this.ResetWatcherDeferralCount()
            this.fileWatcherReloadArcTimeout |> Option.iter Fable.Core.JS.clearTimeout
            this.fileWatcherReloadArcTimeout <- None
            this.fileWatcherPendingEvents.Clear()
            this.fileWatcherPendingArcMergeEvents.Clear()
            this.importedFileWatcherPaths.Clear()

        member this.StopFileWatcher() = promise {
            // Retire the instance first, so events that arrive while it closes are dropped.
            this.FileWatcherToken <- this.FileWatcherToken + 1
            this.CancelFileWatcherRestart()
            this.FileWatcherRestartAttempt <- 0
            this.BufferedFileWatcherEvents.Clear()
            this.IsFlushingBufferedFileWatcherEvents <- false
            this.FileWatcherEventHandler <- None

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

        member internal this.RestoreEmptyVaultAfterFailedInitialization() = promise {
            this.DisposeLargeObjectIndex()
            do! this.StopFileWatcher()

            let hadUnsavedArcChanges = this.hasUnsavedArcChanges

            this.isInitializingArc <- false
            this.path <- None
            this.FileTreeWriter.Reset(this.isCaseInsensitive)

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

        /// Starts the watcher and loads the ARC during initialization. Watcher events are buffered until
        /// FinalizeArcInitialization installs the initial file-tree snapshot, so an older scan cannot overwrite them.
        member this.Startup() = promise {
            match this.path with
            | Some arcRoot ->
                let! isCaseInsensitive = detectCaseInsensitiveAsync arcRoot
                this.isCaseInsensitive <- isCaseInsensitive
                this.FileTreeWriter.Reset(isCaseInsensitive)

                swatelogfn
                    this.window.id
                    "ARC root file system is %s."
                    (if isCaseInsensitive then
                         "case-insensitive"
                     else
                         "case-sensitive")
            | None -> ()

            // The watcher is ready before any snapshot is read. A readiness timeout or start error only degrades it.
            match! this.StartFileWatcherAsync() with
            | FileWatcherStartOutcome.Ready -> ()
            | FileWatcherStartOutcome.TimedOut ->
                swatelogfn
                    this.window.id
                    "The file watcher was not ready in time. The ARC opens without live file watching."

                this.ScheduleFileWatcherRestart None
            | FileWatcherStartOutcome.Failed watcherError ->
                swatelogfn
                    this.window.id
                    "The file watcher could not start. The ARC opens without live file watching: %s"
                    watcherError.Message

                this.ScheduleFileWatcherRestart(Some watcherError)
            | FileWatcherStartOutcome.Stopped -> ()

            try
                do! this.LoadArc()
            with loadError ->
                // The caller reports the failure. The watcher must not outlive an ARC that never loaded.
                do! this.StopFileWatcher()
                return raise loadError
        }

        /// Finalizes a prepared ARC after every asynchronous initialization step succeeded.
        member internal this.FinalizeArcInitialization(fileTree: IndexedFileTree) = promise {
            match this.window.isDestroyed (), this.path with
            | true, _ -> return raise (ArcLoadCancelledException this.window.id)
            | false, None -> return swatefailfn this.window.id "Unable to commit ARC initialization without a path."
            | false, Some normalizedPath ->
                // Events keep buffering until the tree is installed and the buffer is flushed, so none of them
                // is applied to the old tree or overtakes an earlier one.
                this.IsFlushingBufferedFileWatcherEvents <- true
                this.isInitializingArc <- false

                // The renderer learns the path only once the tree is installed, so a pull on pathChange sees it.
                do!
                    this.FileTreeWriter.EnqueueReplace(
                        (fun () -> promise { return fileTree }),
                        fun () ->
                            WindowSend.send<IPathChangeRendererApi>
                                this.window
                                (fun api -> api.pathChange (Some normalizedPath))
                    )

                let bufferedEvents = this.BufferedFileWatcherEvents.ToArray()
                this.BufferedFileWatcherEvents.Clear()
                this.IsFlushingBufferedFileWatcherEvents <- false
                bufferedEvents |> Array.iter (fun applyEvent -> applyEvent ())

                this.StartLargeObjectIndex()

                // Every ARC open asks for the prewarm. It does nothing once the diff workers
                // started, and a failed prewarm is tried again by the next open. Prewarming never
                // throws and does not hold up the ARC.
                Main.VersionControl.TextDiffWorkers.prewarm ()
        }

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

        member private this.RenameOpenArcRootCore(newName: string) : Fable.Core.JS.Promise<Result<string, exn>> = promise {
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
                    this.CancelFileWatcherRestart()
                    this.ClearPendingFileWatcherState()

                // A running git ls-files can have the ARC folder as its working directory, which blocks the rename on
                // Windows. The index is canceled and the rename waits (up to 3 s) until that call has returned.
                let previousIndex = this.LargeObjectIndex

                let previousReady =
                    previousIndex
                    |> Option.bind (fun index ->
                        match index.State with
                        | Main.LargeObjectIndex.Ready ready -> Some ready
                        | _ -> None
                    )

                this.DisposeLargeObjectIndex()

                match previousIndex with
                | Some index -> do! index.CancelAndWaitAsync 3000
                | None -> ()

                // A write can start while the watcher stops.
                let! renameResult =
                    if this.isBusyWriting then
                        promise { return Error(busyWritingError ()) }
                    else
                        promise {
                            // A queued scan or reconcile would enumerate the folder while it moves.
                            do! this.FileTreeWriter.Enqueue(fun _ -> promise { return false })

                            // A write can start while the queue drains.
                            if this.isBusyWriting then
                                return Error(busyWritingError ())
                            else
                                return! renameOpenArcRootDirectoryOnDisk currentPath newName
                        }

                match renameResult with
                | Error renameError ->
                    swatelogfn
                        this.window.id
                        "Renaming the ARC folder '%s' to '%s' failed: %s"
                        currentPath
                        newName
                        renameError.Message

                    if hadWatcher then
                        this.StartFileWatcher()

                    // The renderer keeps the badges of the previous index while the replacement lists again.
                    match previousReady with
                    | Some ready -> this.StartLargeObjectIndex(ready)
                    | None -> this.StartLargeObjectIndex()

                    return Error renameError
                | Ok renamedPath ->
                    this.path <- Some renamedPath
                    this.FileTreeWriter.Reset(this.isCaseInsensitive)

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

                    do! this.EnqueueFileTreeScan()
                    this.StartLargeObjectIndex()

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

        member this.RenameOpenArcRoot(newName: string) : Fable.Core.JS.Promise<Result<string, exn>> = promise {
            this.isRenamingArcRoot <- true

            try
                return! this.RenameOpenArcRootCore newName
            finally
                this.isRenamingArcRoot <- false
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
            vault.DisposeLargeObjectIndex()
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
                    | Ok _ ->
                        vault.isCloseApproved <- true
                        vault.window.close ()
                        return Ok()
                else
                    vault.isCloseApproved <- true
                    vault.window.close ()
                    return Ok()
    }

    member this.OnCloseWindow(window: BrowserWindow, vault: ArcVault, id: int) =
        // Focus brings back a missing watcher and checks for Git changes made outside the app. Each vault guard checks that nothing else is in flight.
        window.onFocus (fun () ->
            if vault.CanRestartFileWatcherOnFocus then
                vault.RestartFileWatcherOnFocus()
                |> Promise.catch (fun restartError ->
                    swatelogfn id "Unable to restart the file watcher after window focus: %s" restartError.Message
                )
                |> Promise.start

            if vault.CanCheckGitStateOnFocus then
                vault.CheckGitStateOnFocus() |> Promise.start
        )

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
        vault.DisposeLargeObjectIndex()
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
            do! vault.FinalizeArcInitialization fileTree
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
                do! vault.FinalizeArcInitialization fileTree
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
        let! fileTreeResult = scanFileTreeAsync arcPath expectedVault.isCaseInsensitive |> Promise.result

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
                    do! vault.FinalizeArcInitialization fileTree
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
