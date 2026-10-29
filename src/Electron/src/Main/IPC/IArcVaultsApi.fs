module Main.IPC.ArcVaultsApi

open System
open Fable.Core
open Fable.Electron
open Fable.Electron.Main
open Swate.Components.Shared
open Swate.Components.Shared.ProvenanceGraph.BuildIndex
open Swate.Electron.Shared
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.DTOs.NoteSearchDto
open Node.Api
open Main
open Main.ArcVaultState
open Main.Bindings
open Main.ARCtrlExtensions
open Main.ArcVaultHelper
open Main.FileImportAuthorization
open Main.IPC.Delete
open Main.IPC.Rename
open Swate.Electron.Shared.DTOs.ProvenanceGroupingDto
open Main.IPC.FileSystemIO
open Main.VersionControl
open VersionControlService.Abstractions

/// The structural watcher does not see payload folders, so app-initiated file operations update the tree
/// themselves. This reconciles the parent directory of every absolute path and waits for the writer, so the
/// renderer finds the path when it asks next.
let private reconcileParentsOfAbsolutePaths (vault: ArcVault) (absolutePaths: string seq) = promise {
    let parents =
        absolutePaths
        |> Seq.choose PathHelpers.tryGetParentPath
        |> Seq.distinct
        |> Seq.toArray

    if parents.Length > 0 then
        do! vault.EnqueueFileTreeDirectoriesReconcile parents
}

let private reconcileParentsOfRelativePaths (vault: ArcVault) (relativePaths: string seq) = promise {
    match vault.path with
    | Some arcPath ->
        do!
            relativePaths
            |> Seq.map (fun relativePath -> Main.Bindings.Path.join [| arcPath; relativePath |])
            |> reconcileParentsOfAbsolutePaths vault
    | None -> ()
}

/// Reconciles the parents of the given relative paths after a successful operation and passes the result on.
let private reconcileAfterSuccess
    (vault: ArcVault)
    (relativePaths: string seq)
    (result: Result<'T, exn>)
    : JS.Promise<Result<'T, exn>> =
    promise {
        match result with
        | Ok _ -> do! reconcileParentsOfRelativePaths vault relativePaths
        | Error _ -> ()

        return result
    }

/// The entity folder (zone folder plus identifier) that holds a path. The entity operations reconcile its parent,
/// the zone folder, because a renamed or deleted entity changes the child list of the zone.
let private entityFolderOf (relativePath: string) =
    (PathHelpers.normalizeRelativePath relativePath).Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.truncate 2
    |> String.concat "/"

let private withLoadedArcVault<'T>
    (event: IpcMainInvokeEvent)
    (operation: ArcVault -> JS.Promise<Result<'T, exn>>)
    : JS.Promise<Result<'T, exn>> =
    promise {
        let windowId = windowIdFromIpcEvent event

        match ARC_VAULTS.TryGetVault(windowId) with
        | None -> return Error(exn $"The ARC for window id {windowId} should exist")
        | Some vault ->
            match vault.path, vault.arc with
            | Some _, Some _ -> return! operation vault
            | _ -> return Error(arcNotOpenError ())
    }

/// Returns the refusal for an import while the window's vault is busy or its window is closing.
/// A window without a loaded vault is not busy, so the callers keep their own handling for it.
let private tryGetImportBusyError (event: IpcMainInvokeEvent) : exn option =
    match ARC_VAULTS.TryGetVault(windowIdFromIpcEvent event) with
    | Some vault when
        vault.isBusyWriting
        || vault.activeFileImport.IsSome
        || vault.isWaitingForImportCleanup
        ->
        Some(exn "ARCitISA is still saving another change or importing. Try again when it is done.")
    | _ -> None

let private tryResolveExistingArcRelativePath
    (arcPath: string)
    (relativePath: string)
    : JS.Promise<Result<string, exn>> =
    promise {
        match tryResolveArcRelativePath arcPath relativePath with
        | Error pathError -> return Error pathError
        | Ok absolutePath ->
            let! exists = pathExistsAsync absolutePath

            if exists then
                return Ok absolutePath
            else
                return Error(exn $"Path '{relativePath}' does not exist.")
    }

let private showPathInFileExplorerAsync (arcPath: string) (relativePath: string) : JS.Promise<Result<unit, exn>> = promise {
    match! tryResolveExistingArcRelativePath arcPath relativePath with
    | Error pathError -> return Error pathError
    | Ok absolutePath ->
        try
            shell.showItemInFolder absolutePath
            return Ok()
        with shellError ->
            return Error(exn $"Could not show '{relativePath}' in file explorer: {shellError.Message}")
}

let private openPathWithDefaultApplicationAsync
    (arcPath: string)
    (relativePath: string)
    : JS.Promise<Result<unit, exn>> =
    promise {
        match! tryResolveExistingArcRelativePath arcPath relativePath with
        | Error pathError -> return Error pathError
        | Ok absolutePath ->
            let! shellOpenResult = shell.openPath absolutePath

            if String.IsNullOrWhiteSpace shellOpenResult then
                return Ok()
            else
                return Error(exn $"Could not open '{relativePath}' with the default application: {shellOpenResult}")
    }

let private runLoadedArcPathAction
    (event: IpcMainInvokeEvent)
    (operation: string -> JS.Promise<Result<'T, exn>>)
    : JS.Promise<Result<'T, exn>> =
    promise {
        try
            return! withLoadedArcVault event (fun vault -> operation vault.path.Value)
        with e ->
            return Error e
    }

let private notifyGitRepositoryInitialized (arcPath: string) =
    ARC_VAULTS.TryGetVaultByPath arcPath
    |> Option.iter (fun vault ->
        WindowSend.send<IGitRepositoryRendererApi>
            vault.window
            (fun rendererApi -> rendererApi.gitRepositoryInitialized arcPath)
    )

let private showArcOpenError (window: BaseWindow option) (arcPath: string option) (error: exn) = promise {
    let detail =
        arcPath
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.map (fun path -> $"Folder: {path}\n\n{error.Message}")
        |> Option.defaultValue error.Message

    let options =
        Dialog.ShowMessageBox.Options(
            "The ARC could not be opened.",
            ``type`` = Enums.Dialog.ShowMessageBox.Options.Type.Error,
            title = "Could not open ARC",
            detail = detail
        )

    let! _ = dialog.showMessageBox (?window = window, options = options)
    return ()
}

let private reportArcOpenError
    (event: IpcMainInvokeEvent)
    (window: BaseWindow option)
    (arcPath: string option)
    (originalError: exn)
    =
    promise {
        try
            do! showArcOpenError window arcPath originalError
        with dialogError ->
            swatelogfn event.sender.id "Failed to show ARC-open error dialog: %s" dialogError.Message

        return Error originalError
    }

let private openArcAtPath (event: IpcMainInvokeEvent) (requestedPath: string) = promise {
    let window = dialogParentFromIpcEvent event

    let normalizedPathResult =
        try
            Ok(PathHelpers.normalizePath requestedPath)
        with error ->
            Error error

    match normalizedPathResult with
    | Error error -> return! reportArcOpenError event window None error
    | Ok arcPath ->
        let windowId = windowIdFromIpcEvent event

        try
            let! disposition = ARC_VAULTS.OpenOrFocusArc(windowId, arcPath)
            return Ok disposition
        with
        | ArcLoadCancelledException _ as error -> return Error error
        | error -> return! reportArcOpenError event window (Some arcPath) error
}

/// This depends on the types in this file, but the types on this file must call this to bind IPC calls :/
let api (event: IpcMainInvokeEvent) : IPCTypes.IArcVaultsApi = {
    openARC =
        fun () -> promise {
            try
                let window = dialogParentFromIpcEvent event

                let! selectionResult =
                    promise {
                        return!
                            dialog.showOpenDialog (
                                ?window = window,
                                properties = [|
                                    Enums.Dialog.ShowOpenDialog.Options.Properties.OpenDirectory
                                |]
                            )
                    }
                    |> Promise.result

                match selectionResult with
                | Error error -> return! reportArcOpenError event window None error
                | Ok r ->
                    if r.canceled then
                        return Ok None
                    elif r.filePaths.Length <> 1 then
                        let error = exn "Not exactly one path"
                        return! reportArcOpenError event window None error
                    else
                        match! openArcAtPath event (Array.exactlyOne r.filePaths) with
                        | Ok disposition -> return Ok(Some(ArcOpenDisposition.path disposition))
                        | Error error -> return Error error
            with error ->
                return Error error
        }
    openARCByPath =
        fun (arcPath: string) -> promise {
            try
                match! openArcAtPath event arcPath with
                | Ok disposition -> return Ok(ArcOpenDisposition.path disposition)
                | Error error -> return Error error
            with error ->
                return Error error
        }
    createARC =
        fun (request: CreateArcRequest) -> promise {
            try
                let window = dialogParentFromIpcEvent event

                let! r =
                    dialog.showOpenDialog (
                        ?window = window,
                        properties = [|
                            Enums.Dialog.ShowOpenDialog.Options.Properties.OpenDirectory
                        |]
                    )

                if r.canceled then
                    return Ok CreateArcOutcome.Cancelled
                elif r.filePaths.Length <> 1 then
                    return Error(exn "Not exactly one path")
                else
                    let arcContainerPath = r.filePaths |> Array.exactlyOne

                    let arcPath =
                        ARCtrl.ArcPathHelper.combine arcContainerPath request.identifier
                        |> PathHelpers.normalizePath

                    let windowId = windowIdFromIpcEvent event

                    let! disposition, terminalOutcome = promise {
                        try
                            let! disposition = ARC_VAULTS.CreateOrFocusArc(windowId, arcPath, request.identifier)
                            return Some disposition, None
                        with
                        | ArcCreatedButClosedException _ -> return None, Some(CreateArcOutcome.CreatedButClosed arcPath)
                        | ArcLoadCancelledException _ -> return None, Some CreateArcOutcome.Cancelled
                    }

                    let createdArcPath =
                        match disposition, terminalOutcome with
                        | Some disposition, _ -> disposition.CreatedArcPath
                        | None, Some(CreateArcOutcome.CreatedButClosed path) -> Some path
                        | _ -> None

                    do!
                        if request.initGit then
                            match createdArcPath with
                            | Some createdArcPath -> promise {
                                try
                                    let host = WorkspaceSessionHost.get ()

                                    let tracked =
                                        host.BeginOperation(
                                            "create-arc-initialize-" + createdArcPath,
                                            Some createdArcPath,
                                            Some(
                                                ARC_VAULTS.TryGetVaultByPath createdArcPath
                                                |> Option.map (fun vault -> vault.window.id)
                                                |> Option.defaultValue (windowIdFromIpcEvent event)
                                            ),
                                            true,
                                            ignore
                                        )

                                    try
                                        let! initResult =
                                            IVersionControlApi.initializeLocalWorkspace
                                                host
                                                createdArcPath
                                                tracked.Context
                                            |> Async.StartAsPromise

                                        match initResult with
                                        | Failed failure ->
                                            Browser.Dom.console.error (
                                                $"The ARC was created, but its Git repository could not be initialized: {failure.Code}: {failure.Message}"
                                            )

                                            return ()
                                        | Succeeded _
                                        | PartiallySucceeded _ ->
                                            notifyGitRepositoryInitialized createdArcPath
                                            return ()
                                    finally
                                        tracked.Complete()
                                with error ->
                                    Browser.Dom.console.error (
                                        $"The ARC was created, but Git initialization failed: {error.Message}"
                                    )

                                    return ()
                              }
                            | None -> promise { return () }
                        else
                            promise { return () }

                    match disposition, terminalOutcome with
                    | None, Some outcome -> return Ok outcome
                    | Some disposition, None ->
                        match disposition with
                        | ArcOpenDisposition.FocusedExisting path -> return Ok(CreateArcOutcome.FocusedExisting path)
                        | ArcOpenDisposition.CreatedInCurrent path
                        | ArcOpenDisposition.CreatedInNewWindow path -> return Ok(CreateArcOutcome.Created path)
                        | ArcOpenDisposition.OpenedInCurrent path
                        | ArcOpenDisposition.OpenedInNewWindow path ->
                            return Error(exn $"Unexpected open disposition while creating ARC at '{path}'.")
                    | _ -> return Error(exn "ARC creation returned an inconsistent lifecycle result.")
            with error ->
                return Error error
        }
    ensureNotesFolder =
        fun () -> promise {
            try
                match tryGetVaultAndArcPath event with
                | Error error -> return Error error
                | Ok(vault, arcPath) ->
                    let! result = Main.Notes.NoteScaffolding.ensureNotesFolderAtArcPath arcPath

                    match result with
                    | Ok _ -> do! vault.EnqueueFileTreeDirectoriesReconcile [| arcPath |]
                    | Error _ -> ()

                    return result
            with error ->
                return Error error
        }
    closeARC =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event
                let vault = ARC_VAULTS.TryGetVault(windowId)

                // Ensure the ARC stays in recent list before disposal marks it inactive.
                if vault.IsSome && vault.Value.path.IsSome then
                    RECENT_ARCS.Add(vault.Value.path.Value) |> ignore

                ARC_VAULTS.DisposeVault(windowId)
                return Ok()
            with e ->
                return Error e
        }
    getOpenPath =
        fun () -> promise {
            let windowId = windowIdFromIpcEvent event
            let vault = ARC_VAULTS.TryGetVault(windowId)

            return vault |> Option.bind (fun v -> v.path)
        }
    openArcFolderInFileExplorer =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault ->
                    match vault.path with
                    | None -> return Error(arcNotOpenError ())
                    | Some arcPath ->
                        let! shellOpenResult = shell.openPath arcPath

                        if String.IsNullOrWhiteSpace shellOpenResult then
                            return Ok()
                        else
                            return Error(exn $"Could not open ARC folder in file explorer: {shellOpenResult}")
            with e ->
                return Error e
        }
    showPathInFileExplorer =
        fun (relativePath: string) ->
            runLoadedArcPathAction event (fun arcPath -> showPathInFileExplorerAsync arcPath relativePath)
    openPathWithDefaultApplication =
        fun (relativePath: string) ->
            runLoadedArcPathAction event (fun arcPath -> openPathWithDefaultApplicationAsync arcPath relativePath)
    getRecentARCs = fun _ -> promise { return RECENT_ARCS.Get() }
    removeRecentARC =
        fun arcpointer -> promise {
            try
                RECENT_ARCS.Remove(arcpointer.path) |> ignore
                ARC_VAULTS.BroadcastRecentARCs()
                return Ok()
            with e ->
                return Error e
        }
    pickArcPaths =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault ->
                    match vault.path with
                    | None -> return Error(arcNotOpenError ())
                    | Some arcPath ->
                        let properties = [|
                            Enums.Dialog.ShowOpenDialog.Options.Properties.OpenFile
                            Enums.Dialog.ShowOpenDialog.Options.Properties.MultiSelections
                        |]

                        let window = dialogParentFromIpcEvent event

                        let! result =
                            dialog.showOpenDialog (?window = window, properties = properties, defaultPath = arcPath)

                        if result.canceled then
                            return Error(exn "Cancelled")
                        else
                            let relativePaths = result.filePaths |> Array.map (tryGetArcRelativePath arcPath)

                            match relativePaths |> Array.tryFind Result.isError with
                            | Some(Error pathError) -> return Error pathError
                            | _ ->
                                return
                                    relativePaths
                                    |> Array.choose (
                                        function
                                        | Ok path when String.IsNullOrWhiteSpace path -> None
                                        | Ok path -> Some path
                                        | Error _ -> None
                                    )
                                    |> Ok
            with e ->
                return Error e
        }
    pickDirectory =
        fun () -> promise {
            try
                let properties = [|
                    Enums.Dialog.ShowOpenDialog.Options.Properties.OpenDirectory
                |]

                let window = dialogParentFromIpcEvent event

                let! result = dialog.showOpenDialog (?window = window, properties = properties)

                if result.canceled then
                    return Error(exn "Cancelled")
                elif result.filePaths.Length <> 1 then
                    return Error(exn "Not exactly one path")
                else
                    return Ok(result.filePaths |> Array.exactlyOne)
            with e ->
                return Error(exn $"Could not pick directory: {e.Message}")
        }
    pickAbsolutePaths =
        fun kind -> promise {
            try
                match tryGetImportBusyError event with
                | Some busyError -> return Error busyError
                | None ->
                    let pickProperty =
                        match kind with
                        | FileSystemItemKind.File -> Enums.Dialog.ShowOpenDialog.Options.Properties.OpenFile
                        | FileSystemItemKind.Folder -> Enums.Dialog.ShowOpenDialog.Options.Properties.OpenDirectory

                    let properties = [|
                        pickProperty
                        Enums.Dialog.ShowOpenDialog.Options.Properties.MultiSelections
                    |]

                    let window = dialogParentFromIpcEvent event
                    let! result = dialog.showOpenDialog (?window = window, properties = properties)

                    if result.canceled then
                        issue (windowIdFromIpcEvent event) [||] |> ignore
                        return Ok None
                    else
                        return Ok(Some(issue (windowIdFromIpcEvent event) result.filePaths))
            with e ->
                return Error(exn $"Could not pick items to import: {e.Message}")
        }
    pickExternalTextFiles =
        fun _ -> promise {
            try
                let properties = [|
                    Enums.Dialog.ShowOpenDialog.Options.Properties.OpenFile
                    Enums.Dialog.ShowOpenDialog.Options.Properties.MultiSelections
                |]

                let filters = [|
                    FileFilter("Delimited text files", [| "csv"; "tsv"; "txt" |])
                |]

                let window = dialogParentFromIpcEvent event

                let! result = dialog.showOpenDialog (?window = window, properties = properties, filters = filters)

                if result.canceled then
                    return Error(exn "Cancelled")
                else
                    let importedFiles = ResizeArray<ImportedTextFile>()

                    for filePath in result.filePaths do
                        let absolutePath = resolveAbsolutePath filePath
                        let! content = ARCtrl.FileSystemHelper.readFileTextAsync absolutePath

                        importedFiles.Add {
                            Name = path.basename absolutePath
                            Content = content
                        }

                    return Ok(importedFiles.ToArray())
            with e ->
                return Error(exn $"Could not import external text files: {e.Message}")
        }
    tryImportExternalFiles =
        fun (request: ImportExternalFilesRequest) -> promise {
            try
                if String.IsNullOrWhiteSpace request.requestId then
                    raise (exn "Import request ID must not be empty.")

                let windowId = windowIdFromIpcEvent event

                // A refusal for being busy does not use up the authorization, so it stays valid until the next pick replaces it.
                match tryGetImportBusyError event with
                | Some busyError -> raise busyError
                | None -> ()

                let sourceAbsolutePaths =
                    match consume windowId request.authorizationId with
                    | Ok paths -> paths
                    | Error authorizationError -> raise authorizationError

                if sourceAbsolutePaths.Length = 0 then
                    raise (exn "No items were selected for import.")

                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            // The watcher ignores temporary import directories. While the import's marks last, creation
                            // events of imported ARC structure files start no merge. Later edits and deletions do, and so
                            // do unrelated external ARC changes. The import reconciles its target folder in the file tree
                            // before the notice closes.

                            let publishImportProgress (progress: FileImportProgress) =
                                match progress.step with
                                | FileImportStep.Scanning ->
                                    vault.window.setProgressBar (
                                        0.0,
                                        BrowserWindow.SetProgressBar.Options(
                                            Enums.BrowserWindow.SetProgressBar.Options.Mode.Indeterminate
                                        )
                                    )
                                | FileImportStep.Copying
                                | FileImportStep.Publishing ->
                                    vault.window.setProgressBar (
                                        FileImportProgress.overallFraction progress,
                                        BrowserWindow.SetProgressBar.Options(
                                            Enums.BrowserWindow.SetProgressBar.Options.Mode.Normal
                                        )
                                    )

                                match vault.activeFileImport with
                                | Some activeImport when
                                    activeImport.State.requestId = request.requestId
                                    && activeImport.State.phase = FileImportPhase.Copying
                                    ->
                                    FileImportCoordinator.publishState vault.window (Some activeImport)
                                | _ -> ()

                            let publisher =
                                FileImportCoordinator.createThrottledProgressPublisher 100 publishImportProgress

                            let reportImportProgress (progress: FileImportProgress) =
                                match vault.activeFileImport with
                                | Some activeImport when
                                    activeImport.State.requestId = request.requestId
                                    && activeImport.State.phase = FileImportPhase.Copying
                                    ->
                                    let updatedImport = {
                                        activeImport with
                                            State = {
                                                activeImport.State with
                                                    progress = progress
                                            }
                                    }

                                    vault.activeFileImport <- Some updatedImport
                                | _ -> ()

                                publisher.Report progress

                            return!
                                IPCHelper.withExclusiveBusyWriting
                                    vault
                                    (fun () ->
                                        FileImportCoordinator.run
                                            vault.window
                                            request.requestId
                                            (fun () ->
                                                if vault.isWaitingForImportCleanup then
                                                    Error(exn "Cannot start an import while the window is closing.")
                                                else
                                                    Ok()
                                            )
                                            (fun () -> vault.activeFileImport)
                                            (fun value -> vault.activeFileImport <- value)
                                            (fun abortSignal -> promise {
                                                let importedEvents = ResizeArray()
                                                let importToken = vault.StartImport()

                                                let registerImportedPaths plannedPaths =
                                                    importedEvents.AddRange(
                                                        WatcherHelpers.createImportedWatcherEvents
                                                            vault.path.Value
                                                            request.targetRelativePath
                                                            plannedPaths
                                                    )

                                                    // Chokidar's awaitWriteFinish can emit these well after the import
                                                    // releases the busy flag, so ownership must outlive the write itself.
                                                    vault.ClaimImportedFileEvents(importToken, importedEvents)

                                                try
                                                    let! result =
                                                        ArcFileSystemHelper.importExternalFilesOnDisk
                                                            vault.path.Value
                                                            request.targetRelativePath
                                                            sourceAbsolutePaths
                                                            reportImportProgress
                                                            (fun () -> abortSignal.aborted)
                                                            registerImportedPaths
                                                            (fun () ->
                                                                publisher.Stop()

                                                                match vault.activeFileImport with
                                                                | Some activeImport when
                                                                    activeImport.State.requestId = request.requestId
                                                                    ->
                                                                    let finalizingImport = {
                                                                        activeImport with
                                                                            State = {
                                                                                activeImport.State with
                                                                                    phase =
                                                                                        FileImportPhase.Finalizing
                                                                            }
                                                                    }

                                                                    vault.activeFileImport <- Some finalizingImport

                                                                    FileImportCoordinator.publishState
                                                                        vault.window
                                                                        (Some finalizingImport)
                                                                | _ -> ()
                                                            )
                                                            (fun () ->
                                                                vault.TryTriggerArcInMemoryMergeOnFileWatcherEvents(
                                                                    importedEvents |> Seq.toList
                                                                )
                                                            )
                                                        |> Promise.catch Error

                                                    vault.ReleaseImportedFileEventsAfterImport(importToken, result)

                                                    // Partly copied items and a staging folder that could not be
                                                    // removed count too, so the target folder is reconciled for every outcome.
                                                    do!
                                                        Main.Bindings.Path.join [|
                                                            vault.path.Value
                                                            request.targetRelativePath
                                                        |]
                                                        |> PathHelpers.normalizePathKeepingWhitespace
                                                        |> vault.EnqueueFileTreeDirectoryReconcile

                                                    vault.window.setProgressBar -1.0
                                                    return result
                                                finally
                                                    publisher.Stop()
                                            })
                                    )
                        })
            with e ->
                return Error(exn $"Could not import items: {e.Message}")
        }
    cancelImportExternalFiles =
        fun requestId -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | Some vault ->
                    match vault.activeFileImport with
                    | Some activeImport when
                        activeImport.State.requestId = requestId
                        && activeImport.State.phase = FileImportPhase.Copying
                        ->
                        activeImport.AbortController.abort ()
                    | _ -> ()
                | None -> ()

                return Ok()
            with e ->
                return Error e
        }
    getActiveFileImport =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                return
                    match ARC_VAULTS.TryGetVault(windowId) with
                    | Some vault -> Ok(vault.activeFileImport |> Option.map _.State)
                    | None -> Error(exn $"The ARC for window id {windowId} should exist")
            with e ->
                return Error e
        }
    getFileTreeSnapshot =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault ->
                    let! snapshot = vault.GetFileTreeSnapshot()
                    return Ok snapshot
            with e ->
                return Error e
        }
    loadDirectory =
        fun (relativePath: string) -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault -> return! vault.LoadDirectory relativePath
            with e ->
                return Error e
        }
    revalidateDirectories =
        fun (relativePaths: string[]) -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault ->
                    do! vault.RevalidateDirectories relativePaths
                    return Ok()
            with e ->
                return Error e
        }
    statFile =
        fun (relativePath: string) -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault -> return! vault.StatFile relativePath
            with e ->
                return Error e
        }
    getLargeObjectIndex =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault -> return Ok(vault.GetLargeObjectIndexSnapshot())
            with e ->
                return Error e
        }
    pathExists =
        fun (relativePath: string) ->
            runLoadedArcPathAction
                event
                (fun arcPath -> promise {
                    match tryResolveArcRelativePath arcPath relativePath with
                    | Error pathError -> return Error pathError
                    | Ok absolutePath ->
                        let! exists = pathExistsAsync absolutePath
                        return Ok exists
                })
    readNotes =
        fun () -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault ->
                    match vault.path with
                    | None -> return Error(arcNotOpenError ())
                    | Some arcPath ->
                        let! fileEntries = Main.NoteSearchReader.scanNoteEntriesAsync arcPath
                        let! notes = Main.NoteSearchReader.readNotes arcPath fileEntries
                        return Ok(notes |> Array.map NoteSearchNoteDto.ofNote)
            with e ->
                return Error e
        }
    getPersons =
        fun () -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise { return Ok(Main.ArcPersonsReader.serialize vault.arc.Value) })
            with error ->
                return Error error
        }
    getArcNodes =
        fun () -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise { return Ok(Main.ArcNodesReader.read vault.arc.Value) })
            with error ->
                return Error error
        }
    listProvenanceTables =
        fun () -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            return Ok(Main.Provenance.ProvenanceGroupingReader.listTables vault.arc.Value)
                        })
            with e ->
                return Error e
        }
    loadProvenanceTable =
        fun (selection: ProvenanceTableSelectionDto) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            return Ok(Main.Provenance.ProvenanceGroupingReader.loadTable selection vault.arc.Value)
                        })
            with e ->
                return Error e
        }
    loadProvenanceGraph =
        fun () -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise { return Ok(ProvenanceGraphData.ofInvestigation vault.arc.Value) })
            with e ->
                return Error e
        }
    saveArcFile =
        fun () -> promise {
            try
                return! withLoadedArcVault event (fun vault -> promise { return! vault.SaveArc() })
            with e ->
                return Error e
        }
    setArcFileInMemory =
        fun (request: FileContentDTO) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            match vault.UpdateArcByFileContentDTO request with
                            | Error saveError -> return Error saveError
                            | Ok() -> return Ok()
                        })
            with e ->
                return Error e
        }
    addArcFile =
        fun (request: FileContentDTO) -> promise {
            try
                return! withLoadedArcVault event (fun vault -> promise { return! vault.AddArcFile request })
            with e ->
                return Error e
        }
    createFileSystemItem =
        fun (request: CreateFileSystemItemRequest) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            let! created = ArcFileSystemHelper.createFileSystemItemOnDisk vault.path.Value request

                            match created with
                            | Ok targetPath -> do! reconcileParentsOfRelativePaths vault [ targetPath ]
                            | Error _ -> ()

                            return created
                        })
            with e ->
                return Error e
        }
    getHasUnsavedArcChanges =
        fun () -> promise {
            try
                return! withLoadedArcVault event (fun vault -> promise { return Ok vault.hasUnsavedArcChanges })
            with e ->
                return Error e
        }
    deletePath =
        fun (relativePath: string) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            let arcPath = vault.path.Value

                            match! ArcFileSystemHelper.resolveDeletePathRouteAsync arcPath relativePath with
                            | ArcFileSystemHelper.DeletePathRoute.GenericItem _ ->
                                let! deleted =
                                    ArcFileSystemHelper.deleteGenericFileSystemItemOnDisk arcPath relativePath

                                return! reconcileAfterSuccess vault [ relativePath ] deleted
                            | ArcFileSystemHelper.DeletePathRoute.ArcEntity normalizedRelativePath ->
                                match vault.arc with
                                | None -> return Error(arcNotOpenError ())
                                | Some arcLocal ->
                                    return!
                                        IPCHelper.withBusyWritingScope
                                            vault
                                            (fun () -> promise {
                                                match!
                                                    ArcDeleteHelper.deleteArcEntityAsync
                                                        arcPath
                                                        normalizedRelativePath
                                                        arcLocal
                                                with
                                                | Error deleteError -> return Error deleteError
                                                | Ok deletedArc ->
                                                    vault.SetArc deletedArc
                                                    vault.RefreshHasUnsavedArcChangesFlag()
                                                    // The watcher reports this late, so the zone folder is reconciled here.
                                                    do!
                                                        reconcileParentsOfRelativePaths vault [
                                                            entityFolderOf normalizedRelativePath
                                                        ]

                                                    return Ok()
                                            })
                            | ArcFileSystemHelper.DeletePathRoute.DataMap normalizedDataMapPath ->
                                match vault.arc, DatamapParentInfo.tryFromPath normalizedDataMapPath with
                                | None, _ -> return Error(arcNotOpenError ())
                                | _, None ->
                                    return
                                        Error(
                                            exn
                                                "ARCitISA could not determine which assay, study, run, or workflow the selected DataMap belongs to. Refresh the File Explorer and try again."
                                        )
                                | Some arc, Some parentInfo ->
                                    return!
                                        IPCHelper.withBusyWritingScope
                                            vault
                                            (fun () -> promise {
                                                match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                                                | Error deleteError -> return Error deleteError
                                                | Ok() ->
                                                    vault.RefreshHasUnsavedArcChangesFlag()

                                                    let absoluteDataMapPath =
                                                        Main.Bindings.Path.join [| arcPath; normalizedDataMapPath |]

                                                    match
                                                        Swate.Components.Shared.PathHelpers.tryGetParentPath
                                                            absoluteDataMapPath
                                                    with
                                                    | Some parentDirectory ->
                                                        do! vault.EnqueueFileTreeDirectoryReconcile parentDirectory
                                                    | None -> ()

                                                    return Ok()
                                            })
                            | ArcFileSystemHelper.DeletePathRoute.Rejected message -> return Error(exn message)
                        })
            with e ->
                return Error e
        }
    renamePath =
        fun (request: RenamePathRequest) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            let arcPath = vault.path.Value

                            let! isGenericRename =
                                ArcFileSystemHelper.isGenericRenamePathAsync arcPath request.relativePath

                            if isGenericRename then
                                let! renamed = ArcFileSystemHelper.renameGenericFileSystemItemOnDisk arcPath request
                                return! reconcileAfterSuccess vault [ request.relativePath ] renamed
                            else
                                match vault.arc with
                                | None -> return Error(arcNotOpenError ())
                                | Some arcLocal ->
                                    return!
                                        IPCHelper.withBusyWritingScope
                                            vault
                                            (fun () -> promise {
                                                match!
                                                    ArcRenameHelper.renameArcEntityAsync arcPath request arcLocal
                                                with
                                                | Error renameError -> return Error renameError
                                                | Ok renamedArc ->
                                                    vault.SetArc renamedArc
                                                    vault.RefreshHasUnsavedArcChangesFlag()

                                                    // The watcher reports this late. The zone folder shows the new
                                                    // entity folder, and the zone rule loads its direct children.
                                                    do!
                                                        reconcileParentsOfRelativePaths vault [
                                                            request.relativePath
                                                            entityFolderOf request.relativePath
                                                        ]

                                                    return Ok()
                                            })
                        })
            with e ->
                return Error e
        }
    movePath =
        fun (request: MovePathRequest) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            let! moved = ArcFileSystemHelper.moveGenericFileSystemItemOnDisk vault.path.Value request

                            return!
                                reconcileAfterSuccess
                                    vault
                                    [ request.sourceRelativePath; request.targetRelativePath ]
                                    moved
                        })
            with e ->
                return Error e
        }
    renameOpenArcRoot =
        fun (newName: string) -> promise {
            try
                return!
                    withLoadedArcVault
                        event
                        (fun vault -> promise {
                            let oldPath = vault.path
                            let! renameResult = vault.RenameOpenArcRoot newName

                            match renameResult with
                            | Error renameError -> return Error renameError
                            | Ok renamedPath ->
                                oldPath |> Option.iter (fun path -> RECENT_ARCS.Remove(path) |> ignore)
                                RECENT_ARCS.Add(renamedPath) |> ignore
                                ARC_VAULTS.BroadcastRecentARCs()
                                return Ok renamedPath
                        })
            with e ->
                swatelogfn
                    (try
                        windowIdFromIpcEvent event
                     with _ ->
                         -1)
                    "Renaming the ARC folder failed: %s"
                    e.Message

                return Error e
        }
    writeFile =
        fun (request: FileContentDTO) -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault ->
                    match vault.path with
                    | None -> return Error(arcNotOpenError ())
                    | Some arcPath ->
                        match tryResolveArcRelativePath arcPath request.path with
                        | Error pathError -> return Error pathError
                        | Ok absolutePath ->
                            // The shared scope counts nesting, so a write that overlaps a
                            // version control operation does not clear the busy flag under it.
                            return!
                                IPCHelper.withBusyWritingScope
                                    vault
                                    (fun () -> promise {
                                        match request.fileType with
                                        | FileContentType.FileContentTypeIsPlainTextVariant ->
                                            let directoryPath = path.dirname absolutePath
                                            do! ARCtrl.FileSystemHelper.createDirectoryAsync directoryPath

                                            do! ARCtrl.FileSystemHelper.writeFileTextAsync absolutePath request.content

                                            do! vault.EnqueueFileTreeDirectoryReconcile directoryPath
                                            return Ok()
                                        | FileContentType.CLI ->
                                            return Error(exn "Direct writing of CLI files is not supported.")
                                        | FileContentType.FileContentTypeIsISAFileVariant ->
                                            return
                                                Error(
                                                    exn
                                                        "Direct writing of ARC content files is not supported. Use saveArcFile for these file types to ensure ARC integrity."
                                                )
                                        | _ ->
                                            return
                                                Error(
                                                    exn
                                                        $"Unsupported file content type for writing: {request.fileType}"
                                                )
                                    })
            with e ->
                return Error e
        }
    openFile =
        fun (relativePath: string) -> promise {
            try
                let windowId = windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault(windowId) with
                | None -> return Error(exn $"The ARC for window id {windowId} should exist")
                | Some vault when vault.arc.IsSome ->
                    let arcfileDTO = FileContentDTO.fromArcByPath relativePath vault.arc.Value

                    match arcfileDTO with
                    | Some dto -> return Ok dto
                    | _ ->
                        // Fallback to text preview for unknown file types
                        try
                            let absolutePath = tryResolveArcRelativePath vault.path.Value relativePath

                            match absolutePath with
                            | Error pathError -> return Error pathError
                            | Ok path ->
                                let fileType = FileContentDTO.inferTextFileTypeFromPath relativePath

                                let! content =
                                    match fileType with
                                    | FileContentType.PDF
                                    | FileContentType.PNG
                                    | FileContentType.JPG
                                    | FileContentType.SVG
                                    | FileContentType.GIF
                                    | FileContentType.Html -> Main.Bindings.Filesystem.readFileBase64Async path
                                    | _ -> ARCtrl.FileSystemHelper.readFileTextAsync path

                                let dto = FileContentDTO.create fileType content relativePath

                                return Ok dto
                        with e ->
                            return Error(exn $"Could not read file {relativePath}: {e.Message}")
                | _ -> return Error(arcNotOpenError ())
            with e ->
                return Error e
        }
    resolveCloseRequest =
        fun (decision: IPCTypesHelper.SaveBeforeQuitDecision) -> promise {
            try
                let windowId = windowIdFromIpcEvent event
                return! ARC_VAULTS.ResolveCloseRequest(windowId, decision)
            with e ->
                return Error e
        }
}
