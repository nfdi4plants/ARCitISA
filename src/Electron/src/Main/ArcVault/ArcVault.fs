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
open Swate.Electron.Shared.FileIOHelper
open ARCtrl
open Main.ArcVaultState

let private closeWaitTimeoutMilliseconds = 30000

[<AutoOpen>]
module ArcVaultExtensions =

    type ArcVault with

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

        member this.TriggerArcInMemoryMergeOnFileWatcherEvents(events: ArcVaultFileSystemEvent list) = promise {
            match! this.TryTriggerArcInMemoryMergeOnFileWatcherEvents events with
            | Ok() -> ()
            | Error mergeError ->
                swatelogfn this.window.id "Unable to merge ARC after file watcher event: %s" mergeError.Message
        }

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

                                    // The watcher reports the new file late, or never when it is down. The nearest loaded
                                    // folder is reconciled now, which loads a new entity folder through the zone rule.
                                    let addedRelativePath =
                                        match arcFile with
                                        | ArcFiles.DataMap(Some parentInfo, _) ->
                                            Some(DatamapParentInfo.toPath parentInfo)
                                        | _ -> arcFile.TryGetRelativePath()

                                    match addedRelativePath with
                                    | Some relativePath ->
                                        match PathHelpers.tryGetParentPath (join [| arcPath; relativePath |]) with
                                        | Some parentDirectory ->
                                            do! this.EnqueueFileTreeDirectoryReconcile parentDirectory
                                        | None -> ()
                                    | None -> ()

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
                                (fun api ->
                                    api.pathChange (
                                        Some {|
                                            path = normalizedPath
                                            isRename = false
                                        |}
                                    )
                                )
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
                // A watcher that went down and waits for its backoff restart must come back on the renamed folder.
                let hadPendingRestart = this.FileWatcherRestartTimeout.IsSome

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
                            do! this.FileTreeWriter.Enqueue(fun _ -> promise { return [||] })

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

                    if hadWatcher || hadPendingRestart then
                        this.RunFileWatcherRestart()

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

                    if hadWatcher || hadPendingRestart then
                        this.RunFileWatcherRestart()

                    do! this.EnqueueFileTreeScan()

                    // The index keys are ARC-relative, so the previous index still holds after the rename. It keeps the
                    // badges in place while the replacement lists again.
                    match previousReady with
                    | Some ready -> this.StartLargeObjectIndex(ready)
                    | None -> this.StartLargeObjectIndex()

                    try
                        WindowSend.send<IPathChangeRendererApi>
                            this.window
                            (fun api ->
                                api.pathChange (
                                    Some {|
                                        path = renamedPath
                                        isRename = true
                                    |}
                                )
                            )
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
        let! fileTreeResult =
            scanStructuralFileTreeAsync arcPath expectedVault.isCaseInsensitive
            |> Promise.result

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
