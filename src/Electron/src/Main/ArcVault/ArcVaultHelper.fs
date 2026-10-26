module Main.ArcVaultHelper


open System
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open ARCtrl
open Fable.Electron
open Fable.Core
open Fable.Core.JsInterop
open Main
open Main.ARCtrlExtensions
open Main.Bindings
open Node.Api
open Swate.Electron.Shared.RenamePathRules

let arcNotOpenError () =
    exn "No ARC is open. Open an ARC and try again."

let private fsPromisesDynamic: obj = importAll "fs/promises"
let private pathDynamic: obj = importAll "path"

/// This function mutably sets the datamap on the correct parent based on the datamap parent info included in the file content DTO.
/// It also ensures that the static hash is preserved to avoid unnecessary changes to the ARC when saving a datamap.
let private setDataMapByParentInfo (arc: ARC) (dmpi: DatamapParentInfo) (dm: DataMap) : Result<unit, exn> =
    try
        arc.TryGetDataMap dmpi
        |> Option.iter (fun currentDataMap -> dm.StaticHash <- currentDataMap.StaticHash)

        if arc.TrySetDataMap(dmpi, Some dm) then
            Ok()
        else
            let parentPath = DatamapParentInfo.toFolderPath dmpi

            Error(
                exn
                    $"Could not save the DataMap because parent '{parentPath}' was not found in the current ARC. Refresh the File Explorer and try again."
            )
    with e ->
        Error(exn $"Failed to set datamap on ARC: {e.Message}")

/// Replaces the just-added ARC entity with the disk round-tripped version.
/// This keeps lossy serialization details from becoming immediate unsaved changes.
let syncAddedArcFileFromPersisted (source: ARC) (target: ARC) (arcFile: ArcFiles) : unit =
    match arcFile with
    | ArcFiles.Assay assay ->
        source.TryGetAssay assay.Identifier
        |> Option.iter (fun sourceAssay -> target.SetAssay(assay.Identifier, sourceAssay))
    | ArcFiles.Study(study, _) ->
        source.TryGetStudy study.Identifier
        |> Option.iter (fun sourceStudy -> target.SetStudy(study.Identifier, sourceStudy))
    | ArcFiles.Workflow workflow ->
        source.TryGetWorkflow workflow.Identifier
        |> Option.iter (fun sourceWorkflow -> target.SetWorkflow(workflow.Identifier, sourceWorkflow))
    | ArcFiles.Run run ->
        source.TryGetRun run.Identifier
        |> Option.iter (fun sourceRun -> target.SetRun(run.Identifier, sourceRun))
    | ArcFiles.DataMap(Some parentInfo, _) ->
        source.TryGetDataMap parentInfo
        |> Option.iter (fun persistedDataMap -> target.TrySetDataMap(parentInfo, Some persistedDataMap) |> ignore)
    | _ -> ()

/// This function should only be used for partial updates to an ARC based on a file content DTO.
let updateARCByFileContentDTO (oldArc: ARC) (dto: FileContentDTO) : Result<ARC, exn> =
    let arcfile = FileContentDTO.toArcFile dto

    match arcfile with
    | None -> Error(exn $"Unsupported file type for saving: {dto.fileType}")
    | Some arcfile ->
        match arcfile with
        // if we get an investigation we are only interested in updating the investigation part of the ARC, so we can avoid deserializing and reserializing the whole ARC which is costly for large ARCs.
        // This only works under the assumption, that we did not in fact do any changes to assay, study, ... . These are reused from the existing Investigation.
        | ArcFiles.Investigation newInvestigation ->
            newInvestigation.Assays <- oldArc.Assays
            newInvestigation.Studies <- oldArc.Studies
            newInvestigation.Runs <- oldArc.Runs
            newInvestigation.Workflows <- oldArc.Workflows

            let newArc =
                ARC.fromArcInvestigation (newInvestigation, oldArc.FileSystem, ?license = oldArc.License)

            newArc.StaticHash <- oldArc.StaticHash
            Ok newArc
        | ArcFiles.DataMap(dmpiOpt, dm) ->
            match dmpiOpt with
            | None -> Error(exn "DataMap file must include datamap parent info in its path for saving.")
            | Some dmpi ->
                match setDataMapByParentInfo oldArc dmpi dm with
                | Ok() -> Ok oldArc
                | Error e -> Error e
        | ArcFiles.Assay newAssay ->
            let oldAssayOpt = oldArc.TryGetAssay newAssay.Identifier

            match oldAssayOpt with
            | Some oldAssay ->
                newAssay.StaticHash <- oldAssay.StaticHash
                oldArc.SetAssay(newAssay.Identifier, newAssay)
                Ok oldArc
            | None ->
                oldArc.AddAssay(newAssay)
                Ok oldArc
        | ArcFiles.Study(newStudy, _) ->
            let oldStudyOpt = oldArc.TryGetStudy newStudy.Identifier

            match oldStudyOpt with
            | Some oldStudy ->
                newStudy.StaticHash <- oldStudy.StaticHash
                oldArc.SetStudy(newStudy.Identifier, newStudy)
                Ok oldArc
            | None ->
                oldArc.AddStudy(newStudy)
                Ok oldArc
        | ArcFiles.Workflow newWorkflow ->
            let oldWorkflowOpt = oldArc.TryGetWorkflow newWorkflow.Identifier

            match oldWorkflowOpt with
            | Some oldWorkflow ->
                newWorkflow.StaticHash <- oldWorkflow.StaticHash
                oldArc.SetWorkflow(newWorkflow.Identifier, newWorkflow)
                Ok oldArc
            | None ->
                oldArc.AddWorkflow(newWorkflow)
                Ok oldArc
        | ArcFiles.Run newRun ->
            let oldRunOpt = oldArc.TryGetRun newRun.Identifier

            match oldRunOpt with
            | Some oldRun ->
                newRun.StaticHash <- oldRun.StaticHash
                oldArc.SetRun(newRun.Identifier, newRun)
                Ok oldArc
            | None ->
                oldArc.AddRun(newRun)
                Ok oldArc
        | ArcFiles.Template _ -> Error(exn "Saving of template files is not supported.")

let swatelogfn id fmt =
    Printf.kprintf (fun s -> Browser.Dom.console.log ("[ARCitISA-" + string id + "] " + s)) fmt

let swatefailfn id fmt =
    Printf.kprintf (fun s -> failwith ("[ARCitISA-" + string id + "] " + s)) fmt

/// Serializes ARC merge operations through a single FIFO owner. Individual operation failures
/// reject their caller's promise without terminating the queue processor.
type internal ArcMergeQueue(windowId: int) =

    let mailbox =
        MailboxProcessor.Start(fun inbox ->
            let rec processNext () = async {
                let! operation = inbox.Receive()
                do! operation () |> Async.AwaitPromise
                return! processNext ()
            }

            processNext ()
        )

    member _.Enqueue<'T>(operation: unit -> JS.Promise<'T>) : JS.Promise<'T> =
        JS.Constructors.Promise.Create(fun resolve reject ->
            mailbox.Post(fun () -> promise {
                try
                    let! result = operation ()
                    resolve result
                with error ->
                    swatelogfn windowId "Queued ARC merge failed: %s" error.Message
                    reject error
            })
        )

type OpenArcRootRenamePlan = {
    SourcePath: string
    TargetPath: string
    NewName: string
}

let private resolveAbsolutePath (pathValue: string) =
    pathDynamic?resolve (pathValue) |> unbox<string> |> PathHelpers.normalizePath

let private tryGetNodeErrorCode = Filesystem.tryGetNodeErrorCode

let private pathExistsAsync (absolutePath: string) : JS.Promise<bool> = promise {
    let! fileExists = ARCtrl.FileSystemHelper.fileExistsAsync absolutePath

    if fileExists then
        return true
    else
        return! ARCtrl.FileSystemHelper.directoryExistsAsync absolutePath
}

let private currentNodePlatform () : string =
    emitJsExpr () "process.platform" |> unbox<string>

/// Delays before each rename attempt. Windows holds folders longer (for example while an antivirus scan reads them), so it waits up to 4 s.
let renameRetryDelaysMs (platform: string) : int[] =
    if String.Equals(platform, "win32", StringComparison.OrdinalIgnoreCase) then
        [| 0; 100; 300; 1000; 2000; 4000 |]
    else
        [| 0; 75; 200; 500 |]

let private renameWithRetriesAsync
    (platform: string)
    (sourceAbsolutePath: string)
    (targetAbsolutePath: string)
    : JS.Promise<Result<unit, exn>> =
    let retryDelaysMs = renameRetryDelaysMs platform

    let isTransientRenameError =
        function
        | Some "EPERM"
        | Some "EACCES"
        | Some "EBUSY" -> true
        | _ -> false

    let rec attempt (attemptIndex: int) = promise {
        if attemptIndex > 0 then
            do! Promise.sleep retryDelaysMs.[attemptIndex]

        try
            let! _ =
                fsPromisesDynamic?rename (sourceAbsolutePath, targetAbsolutePath)
                |> unbox<JS.Promise<obj>>

            return Ok()
        with renameError ->
            let errorCode = tryGetNodeErrorCode renameError

            if attemptIndex < retryDelaysMs.Length - 1 && isTransientRenameError errorCode then
                return! attempt (attemptIndex + 1)
            else
                return Error renameError
    }

    attempt 0

/// Maps a failed rename to an exception with a readable message that includes the error code.
/// The message of a raw Node error does not survive the IPC bridge, so the result is always a plain exn.
let mapArcRootRenameDiskError (sourcePath: string) (targetPath: string) (renameError: exn) =
    match tryGetNodeErrorCode renameError with
    | Some code when code = "EPERM" || code = "EACCES" || code = "EBUSY" ->
        exn
            $"Cannot rename '{sourcePath}' to '{targetPath}'. The folder is in use or access is denied ({code}). Close apps that may be using these paths, such as a file explorer window or a terminal inside the ARC, and try again."
    | Some code when code = "ENOTEMPTY" || code = "EEXIST" ->
        exn $"Cannot rename '{sourcePath}' to '{targetPath}' because the destination already exists ({code})."
    | Some "ENOENT" -> exn $"Cannot rename '{sourcePath}' because the source path no longer exists on disk (ENOENT)."
    | code ->
        let codeText = code |> Option.defaultValue "no error code"
        exn $"Cannot rename '{sourcePath}' to '{targetPath}': {renameError.Message} ({codeText})."

let tryBuildOpenArcRootRenamePlan arcPath newName : Result<OpenArcRootRenamePlan, exn> =
    let candidateName = newName |> Option.ofObj |> Option.defaultValue String.Empty

    match validateRenameName candidateName with
    | Error validationError -> Error(exn validationError)
    | Ok normalizedName ->
        if
            (pathDynamic?isAbsolute (normalizedName) |> unbox<bool>)
            || PathHelpers.containsPathTraversalSegments normalizedName
        then
            Error(exn "ARC folder name must be a single folder name without path separators or traversal segments.")
        else
            let sourcePath = resolveAbsolutePath arcPath
            let parentPath = pathDynamic?dirname (sourcePath) |> unbox<string>

            let targetPath =
                pathDynamic?resolve (parentPath, normalizedName)
                |> unbox<string>
                |> PathHelpers.normalizePath

            if
                String.Equals(
                    PathHelpers.normalizePathForFsComparison sourcePath,
                    PathHelpers.normalizePathForFsComparison targetPath,
                    StringComparison.Ordinal
                )
            then
                Error(exn "New ARC folder name must be different from the current folder name.")
            else
                Ok {
                    SourcePath = sourcePath
                    TargetPath = targetPath
                    NewName = normalizedName
                }

let renameOpenArcRootDirectoryOnDisk arcPath newName : JS.Promise<Result<string, exn>> = promise {
    match tryBuildOpenArcRootRenamePlan arcPath newName with
    | Error validationError -> return Error validationError
    | Ok plan ->
        let! sourceExists = ARCtrl.FileSystemHelper.directoryExistsAsync plan.SourcePath

        if not sourceExists then
            return Error(exn $"Cannot rename '{plan.SourcePath}' because the source path no longer exists on disk.")
        else
            let! targetExists = pathExistsAsync plan.TargetPath

            if targetExists then
                return
                    Error(
                        exn
                            $"Cannot rename '{plan.SourcePath}' to '{plan.TargetPath}' because the destination already exists."
                    )
            else
                let! renameResult = renameWithRetriesAsync (currentNodePlatform ()) plan.SourcePath plan.TargetPath

                match renameResult with
                | Ok() -> return Ok plan.TargetPath
                | Error renameError ->
                    return Error(mapArcRootRenameDiskError plan.SourcePath plan.TargetPath renameError)
}

let createWindow () =
    printfn "[ARCitISA] Creating new window"
    let screenSize = screen.getPrimaryDisplay().workAreaSize

    let windowIconPath = Helper.Assets.getIcon ()

    let mainWindowOptions =
        BrowserWindowConstructorOptions(
            title = Swate.Electron.Shared.ApplicationVersion.windowTitle None,
            icon = (windowIconPath |> U2.Case2),
            width = int screenSize.width,
            height = int screenSize.height,
            show = false,
            webPreferences =
                WebPreferences(preload = path.join (__dirname, "preload.fs.js"), zoomFactor = UiSettings.getScale ())
        )

    let window = BrowserWindow(mainWindowOptions)

    UiSettings.registerWindow window

    // Prevent links from opening new Electron windows
    window.webContents.setWindowOpenHandler (fun details ->
        Fable.Electron.Main.shell.openExternal details.url |> Promise.start
        WindowOpenHandlerResponse(Enums.Types.WindowOpenHandlerResponse.Action.Deny)
    )

    // Prevent navigation inside current Electron window
    window.webContents.onWillNavigate (fun event url _ _ _ _ ->
        let currentUrl = window.webContents.getURL ()

        if url <> currentUrl then
            event.preventDefault ()
            Fable.Electron.Main.shell.openExternal url |> Promise.start
    )

    // A reload starts a renderer that knows none of the diff handles the old one opened.
    let windowId = window.id

    window.webContents.onDidNavigate (fun _ _ _ _ -> Main.VersionControl.TextDiffHandles.windowReloaded windowId)

    window

let loadWindow (window: BrowserWindow) = promise {
    if isNullOrUndefined MAIN_WINDOW_VITE_DEV_SERVER_URL then
        do! window.loadFile (path.join (__dirname, $"../renderer/{MAIN_WINDOW_VITE_NAME}/index.html"))
    else
        window.webContents.openDevTools Enums.WebContents.OpenDevTools.Options.Mode.Right
        do! window.loadURL MAIN_WINDOW_VITE_DEV_SERVER_URL

    window.show ()
}

let shouldUsePollingByDefault (platform: string) =
    System.String.Equals(platform, "win32", System.StringComparison.OrdinalIgnoreCase)

/// Chokidar passes cwd-relative paths without telling files from directories, so the shared rule runs as for a file.
/// Import staging folders stay hidden here only, because they exist for the length of an import.
let isFileWatcherPathIgnored (path: string) =
    let normalizedPath = PathHelpers.normalizeSeparators path
    let temporaryImportPattern = """(^|/)\.swate-import-[0-9a-fA-F]{32}(/|$)"""

    isIgnoredInventoryEntry (PathHelpers.getNameFromPath normalizedPath) normalizedPath false
    || System.Text.RegularExpressions.Regex.IsMatch(normalizedPath, temporaryImportPattern)

/// Environment variable that forces polling for network or FUSE mounts, where native file events do not arrive.
[<Literal>]
let watchPollingEnvironmentVariable = "SWATE_WATCH_POLLING"

/// Decides between polling and native events. An explicit choice wins. Otherwise the environment flag
/// forces polling on any platform and the platform default applies without it.
let resolveFileWatcherPolling (platform: string) (environmentFlag: string option) (explicitChoice: bool option) =
    match explicitChoice with
    | Some choice -> choice
    | None -> environmentFlag = Some "1" || shouldUsePollingByDefault platform

/// Timings of the watcher in milliseconds. Tests shorten them. The write values are chokidar's defaults.
type FileWatcherTuning = {
    PollingIntervalMs: int
    BinaryPollingIntervalMs: int
    WriteStabilityMs: int
    WritePollMs: int
}

let defaultFileWatcherTuning = {
    PollingIntervalMs = 200
    BinaryPollingIntervalMs = 400
    WriteStabilityMs = 2000
    WritePollMs = 100
}

/// Keeps the permanent watcher on ARC structure. Chokidar often asks before it has stat data, so the path shape
/// decides first and the stats only separate files from folders where the shape allows both.
let isStructuralWatcherPathOfKind (arcPath: string) (candidatePath: string) (isDirectory: bool option) =
    let absolutePath =
        if Main.Bindings.Path.isAbsolute candidatePath then
            candidatePath
        else
            Main.Bindings.Path.resolve [| arcPath; candidatePath |]

    match tryGetRepoRelativePathOrRoot arcPath absolutePath with
    | None -> false
    | Some relativePath ->
        // Import staging folders and git metadata are checked on the relative path, so a parent folder of the ARC
        // with such a name cannot hide the whole ARC.
        (relativePath = "" || not (isFileWatcherPathIgnored relativePath))
        && ArcEntityPathRules.isStructuralWatcherPath relativePath isDirectory

/// The watcher's own decision, where chokidar's stats (when present) tell files from folders.
let isStructuralFileWatcherPath (arcPath: string) (candidatePath: string) (stats: Chokidar.IWatchedStats option) =
    isStructuralWatcherPathOfKind arcPath candidatePath (stats |> Option.map (fun value -> value.isDirectory ()))

let createWatchOptions (path: string) (usePolling: bool) (tuning: FileWatcherTuning) =
    let ignored =
        System.Func<string, Chokidar.IWatchedStats option, bool>(fun candidatePath stats ->
            not (isStructuralFileWatcherPath path candidatePath stats)
        )

    let awaitWriteFinish =
        Chokidar.AwaitWriteFinishOptions(
            stabilityThreshold = tuning.WriteStabilityMs,
            pollInterval = tuning.WritePollMs
        )

    if usePolling then
        Chokidar.WatchOptions(
            cwd = path,
            awaitWriteFinish = !^awaitWriteFinish,
            ignored = !^ignored,
            ignoreInitial = true,
            usePolling = true,
            interval = tuning.PollingIntervalMs,
            binaryInterval = tuning.BinaryPollingIntervalMs
        )
    else
        Chokidar.WatchOptions(
            cwd = path,
            awaitWriteFinish = !^awaitWriteFinish,
            ignored = !^ignored,
            ignoreInitial = true
        )

let createFileWatcherWith (tuning: FileWatcherTuning) (path: string) (usePolling: bool option) =
    // Native Windows file events can keep handles that block app-initiated folder renames.
    let usePolling =
        resolveFileWatcherPolling
            (currentNodePlatform ())
            (Main.Bindings.Node.environmentVariable watchPollingEnvironmentVariable)
            usePolling

    Chokidar.Chokidar.watch (path, createWatchOptions path usePolling tuning)

let createFileWatcher (path: string) (usePolling: bool option) =
    createFileWatcherWith defaultFileWatcherTuning path usePolling

let sendArcHasUnsavedChangesUpdate (hasUnsavedChanges: bool) (window: BrowserWindow) =
    WindowSend.send<Swate.Electron.Shared.IPCTypes.MainToRendererIpc.IHasUnsavedArcChangesRendererApi>
        window
        (fun api -> api.arcUnsavedChangesUpdate hasUnsavedChanges)

/// Text files above this size get no preview.
let maxTextPreviewBytes = 20.0 * 1024.0 * 1024.0

/// PDFs above this size get no preview.
let maxPdfPreviewBytes = 50.0 * 1024.0 * 1024.0

let private binarySniffBytes = 8192

/// Reads up to maxBytes bytes from the start of the file through one handle. The handle is closed on success
/// and on failure. The close is awaited so the file is released before the caller continues.
let private readFirstBytesAsync (path: string) (maxBytes: int) : JS.Promise<byte[] * int> = promise {
    let! handle = Filesystem.openAsync path "r"
    let buffer = Filesystem.allocBuffer maxBytes

    let! total = promise {
        try
            let total = ref 0
            let reachedEnd = ref false

            while not reachedEnd.Value && total.Value < maxBytes do
                let! result = handle.read (buffer, total.Value, maxBytes - total.Value, total.Value)
                total.Value <- total.Value + result.bytesRead
                reachedEnd.Value <- result.bytesRead = 0

            return total.Value
        with error ->
            do! handle.close ()
            return raise error
    }

    do! handle.close ()
    return buffer, total
}

/// Reads a file of an ARC for the preview. Files above the size limit and files without text content come
/// back as a NoPreview DTO. Their content is not read.
let readFileForPreviewAsync (absolutePath: string) (relativePath: string) : JS.Promise<FileContentDTO> = promise {
    let fileType = FileContentDTO.inferTextFileTypeFromPath relativePath
    let isPdf = fileType = FileContentType.PDF
    let! stats = Filesystem.statAsync absolutePath
    let limit = if isPdf then maxPdfPreviewBytes else maxTextPreviewBytes

    if not (stats.isFile ()) then
        return FileContentDTO.createNoPreview relativePath stats.size NoPreviewReason.Binary
    elif stats.size > limit then
        return FileContentDTO.createNoPreview relativePath stats.size NoPreviewReason.TooLarge
    else
        // The read is capped at the size from the stat plus one byte. A file that grew afterwards is previewed
        // up to that cap, and one that crossed the limit while it was exactly at it shows as too large.
        let capacity = int (min (limit + 1.0) (stats.size + 1.0))
        let! buffer, total = readFirstBytesAsync absolutePath capacity

        if float total > limit then
            return FileContentDTO.createNoPreview relativePath stats.size NoPreviewReason.TooLarge
        elif isPdf then
            return FileContentDTO.create fileType (Filesystem.bufferToBase64Prefix buffer total) relativePath
        elif total >= 2 && buffer[0] = 0xFFuy && buffer[1] = 0xFEuy then
            // UTF-16 text has NUL bytes, so a BOM decides before the NUL sniff.
            return FileContentDTO.create fileType (Filesystem.bufferDecodePrefix buffer total "utf-16le") relativePath
        elif total >= 2 && buffer[0] = 0xFEuy && buffer[1] = 0xFFuy then
            return FileContentDTO.create fileType (Filesystem.bufferDecodePrefix buffer total "utf-16be") relativePath
        elif
            buffer
            |> Array.truncate (min total binarySniffBytes)
            |> Array.exists (fun b -> b = 0uy)
        then
            return FileContentDTO.createNoPreview relativePath stats.size NoPreviewReason.Binary
        else
            return FileContentDTO.create fileType (Filesystem.bufferToUtf8Prefix buffer total) relativePath
}
