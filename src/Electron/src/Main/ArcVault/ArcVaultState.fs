/// The ArcVault class, its state types and the ARC lifecycle exceptions that the ArcVault modules share.
module Main.ArcVaultState

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
open Main.ArcDiskState
open Main.FileImportCoordinator
open Swate.Components.Shared
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.IPCTypesHelper
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open ARCtrl
open Main.GitStateStamp

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

/// The watcher state as the renderer sees it. Only unavailable asks the user for anything.
let fileWatcherStateToDto (state: FileWatcherState) : FileWatcherStateDto =
    match state with
    | FileWatcherState.Degraded reason -> {|
        state = FileWatcherStatus.Degraded
        reason = reason
      |}
    | FileWatcherState.Unavailable reason -> {|
        state = FileWatcherStatus.Unavailable
        reason = reason
      |}
    | FileWatcherState.Stopped
    | FileWatcherState.Starting
    | FileWatcherState.Running -> {|
        state = FileWatcherStatus.Running
        reason = ""
      |}

/// The size of a file, or None when it is gone or not a file.
let private statFileSize (path: string) : Fable.Core.JS.Promise<float option> = promise {
    try
        let! stats = statAsync path
        return (if stats.isFile () then Some stats.size else None)
    with _ ->
        return None
}

type FileWatcherStartOutcome =
    | Ready
    | TimedOut
    | Failed of exn
    /// The watcher was stopped, replaced or retired while the start was running, so the caller owns nothing.
    | Stopped

/// Who owns the files of the vault. Own writes and syncs exclude everything else. External operations can overlap each other.
type ArcSyncMode =
    | Idle
    /// A scan is looking for changes. Edits are accepted and no notice shows.
    | Checking
    | Syncing
    | OwnWrite
    | ExternalOperation of depth: int
    /// The ARC folder moves. Callers that ask meanwhile wait or are refused.
    | RootRename

/// <summary>
/// Represents a vault window in the application, optionally associated with a file path.
/// </summary>
/// <param name="path">Can be None if not opened ARC.</param>
type ArcVault(window: BrowserWindow) =

    let mutable syncMode = ArcSyncMode.Idle
    let ownerWaiters = ResizeArray<unit -> unit>()

    let mutable arcPath: string option = None
    let mutable fileWatcherStateValue = FileWatcherState.Stopped

    let defaultCaseInsensitive =
        match Main.VersionControl.ProviderComposition.currentPathCaseSensitivity () with
        | VersionControlService.Abstractions.CaseInsensitive -> true
        | VersionControlService.Abstractions.CaseSensitive -> false

    /// Every change of the main-process file tree runs through this writer. After a job with changes it sends
    /// one fileTreeDirectoryUpdate that lists every changed directory, parents first, with the version of the job.
    let fileTreeWriter =
        FileTreeWriter(
            defaultCaseInsensitive,
            (fun message -> swatelogfn window.id "%s" message),
            (fun change ->
                match arcPath with
                | Some root ->
                    // A directory that left the tree has nothing to send. Its parent is in the same change.
                    let directories =
                        change.ChangedDirectories
                        |> Array.choose (fun directory ->
                            match
                                toTreeRelativePath change.Tree.CaseInsensitive root directory,
                                change.Tree.Children directory
                            with
                            | Some relativeDirectory, Some children ->
                                let entry: FileTreeDirectoryChildrenDto = {|
                                    directoryPath = relativeDirectory
                                    children = toRendererEntries change.Tree.CaseInsensitive root children
                                |}

                                Some entry
                            | _ -> None
                        )

                    if directories.Length > 0 then
                        let update: FileTreeDirectoryUpdateDto = {|
                            treeVersion = change.TreeVersion
                            directories = directories
                        |}

                        WindowSend.send<IFileTreeRendererApi> window (fun api -> api.fileTreeDirectoryUpdate update)
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
    member this.fileWatcherState
        with get () = fileWatcherStateValue
        and set (value: FileWatcherState) =
            if value <> fileWatcherStateValue then
                fileWatcherStateValue <- value

                if not (window.isDestroyed ()) then
                    WindowSend.send<IFileTreeRendererApi>
                        window
                        (fun api -> api.fileWatcherStateChanged (fileWatcherStateToDto value))

    /// Identifies the current watcher instance. Listeners capture it and drop events once it has changed.
    member val internal FileWatcherToken: int = 0 with get, set
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

    /// True while a content check of the large objects runs, so checks do not overlap.
    member val internal LfsContentCheckRunning: bool = false with get, set

    /// Reads the size of a file for the content check of the large object index. Tests replace it to count calls.
    member val internal LargeObjectContentStat: string -> Fable.Core.JS.Promise<float option> =
        statFileSize with get, set

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
    member val activeFileImport: ActiveFileImport option = None with get, set
    member val isWaitingForImportCleanup = false with get, set
    /// True only while an ARC is being opened or initially created in this vault.
    member val isInitializingArc = false with get, set
    member val isWaitingForOperationsOnClose = false with get, set
    member val isOperationCloseApproved = false with get, set

    member internal this.SyncMode
        with get () = syncMode
        and set value = syncMode <- value

    /// Runs the retry when the current owner releases the vault. The retry runs in the tick of the release, so no other caller can get in between.
    member internal this.WaitForOwnerRelease(retry: unit -> unit) = ownerWaiters.Add retry

    /// Ends the current ownership and lets the waiting callers try again in order.
    member internal this.ReleaseOwnership() =
        syncMode <- ArcSyncMode.Idle
        let waiting = ownerWaiters.ToArray()
        ownerWaiters.Clear()

        for retry in waiting do
            retry ()

    /// False while the vault is idle or a check runs. A check only reads.
    member this.isBusyWriting =
        match syncMode with
        | ArcSyncMode.OwnWrite
        | ArcSyncMode.RootRename
        | ArcSyncMode.ExternalOperation _
        | ArcSyncMode.Syncing -> true
        | ArcSyncMode.Idle
        | ArcSyncMode.Checking -> false

    /// The file versions memory was last in sync with. None until a load or a first scan set it, which only a vault opened without Startup sees.
    member val internal KnownDisk: ArcDiskSnapshot option = None with get, set
    /// Grows each time a sync changes memory.
    member val internal ArcRevision = 0 with get, set
    /// The revision at which a sync last changed each file. A DataMap counts as its parent workbook through resolveArcPreviewPath, because one page edits both.
    member val internal FileRevisions: Map<PathKey, int> = Map.empty with get, set
    /// True while an entity delete or an import holds the vault. Other external operations, rename and DataMap delete wait for it to end. Save and add are refused.
    member val internal ExclusiveExternalOperation = false with get, set
    /// A sync was asked for while it could not run.
    member val internal SyncRequested = false with get, set
    member val internal SyncRequestAnnounces = false with get, set
    /// A sync failed and no sync has finished since.
    member val internal LastSyncFailed = false with get, set
    /// Moves when the pending watcher state is cleared, which a root rename and a close do. A running check or sync compares it after each await and discards its result if it moved.
    member val internal RootToken = 0 with get, set

    /// Scans the structural files. Tests replace it to gate or fail a scan.
    member val internal DiskScan: bool -> string -> Fable.Core.JS.Promise<ArcDiskSnapshot> =
        ArcDiskState.scan with get, set

    /// Loads the ARC for a sync. Tests replace it to gate a load or count loads.
    member val internal SnapshotLoad: string -> Fable.Core.JS.Promise<Result<ARC, string[]>> =
        (fun arcPath -> ARC.LoadAsyncSwate arcPath) with get, set

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
