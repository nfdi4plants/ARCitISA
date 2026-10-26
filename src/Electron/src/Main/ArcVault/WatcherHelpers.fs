module Main.WatcherHelpers

open System
open System.Collections.Generic
open Fable.Electron
open Main.Bindings
open Main.Bindings.Filesystem
open Main.ArcMerge
open Main.ArcVaultTypes
open Main.Bindings.Path
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes

let eventNameEquals (expected: Chokidar.Events) (actual: string) =
    String.Equals(actual, expected.ToString(), StringComparison.OrdinalIgnoreCase)

let existsSyncWithExactName (path: string) =
    existsSync path
    && (readdirSync (dirname path)
        |> Array.exists (fun name -> String.Equals(name, basename path, StringComparison.Ordinal)))

/// Builds an ARC-root-relative watcher event from the raw chokidar payload.
let buildWatcherEvent (arcPath: string) (eventName: string) (path: string) =
    let normalizedPath = PathHelpers.normalizePath path
    let normalizedArcPath = PathHelpers.normalizePath arcPath

    let relativePath =
        match tryGetRepoRelativePath arcPath normalizedPath with
        | Some path -> PathHelpers.normalizePath path
        | None ->
            if PathHelpers.isSameOrDescendantPath normalizedPath normalizedArcPath then
                let prefix = normalizedArcPath + "/"

                if normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                    normalizedPath.Substring(prefix.Length)
                else
                    ""
            else
                normalizedPath

    let absolutePath =
        if PathHelpers.isSameOrDescendantPath normalizedPath normalizedArcPath then
            normalizedPath
        else
            $"{normalizedArcPath}/{relativePath}" |> PathHelpers.normalizePath

    {
        EventName = eventName
        RelativePath = relativePath
        AbsolutePath = absolutePath
    }

/// Builds the absolute folder and file paths of an import's planned paths below the target folder.
/// It does not filter paths the watcher ignores.
let importedAbsolutePaths arcPath targetRelativePath (plannedPaths: PlannedImportPaths) : string[] * string[] =
    let targetRelativePath =
        PathHelpers.normalizeCanonicalRelativePath targetRelativePath

    let toAbsolutePaths (importedRelativePaths: string[]) =
        importedRelativePaths
        |> Array.map (fun importedRelativePath ->
            let relativePath =
                if String.IsNullOrWhiteSpace targetRelativePath then
                    importedRelativePath
                else
                    $"{targetRelativePath}/{importedRelativePath}"
                |> PathHelpers.normalizePath

            (buildWatcherEvent arcPath "" relativePath).AbsolutePath
        )

    toAbsolutePaths plannedPaths.FolderPaths, toAbsolutePaths plannedPaths.FilePaths

/// Builds the absolute folder and file paths that the file tree lists after an import. The result matches what
/// `FileTreeCreator.getFileEntries` lists after a reload. It drops `.git` folders with everything inside them
/// and the files the file tree hides, such as lock files and LFS backups. A file named `.git` stays, because
/// git submodules and worktrees have one and the file tree lists it.
let importedFileTreePaths arcPath targetRelativePath (plannedPaths: PlannedImportPaths) : string[] * string[] =
    let segmentsOf (plannedPath: string) =
        (PathHelpers.normalizePath plannedPath).Split '/'

    let isHidden (isDirectory: bool) (plannedPath: string) =
        let segments = segmentsOf plannedPath

        let isInsideHiddenFolder =
            [| 1 .. segments.Length - 1 |]
            |> Array.exists (fun length ->
                isHiddenFileTreeEntry true (String.Join("/", Array.truncate length segments))
            )

        isInsideHiddenFolder || isHiddenFileTreeEntry isDirectory plannedPath

    let visiblePlannedPaths = {
        FolderPaths = plannedPaths.FolderPaths |> Array.filter (isHidden true >> not)
        FilePaths = plannedPaths.FilePaths |> Array.filter (isHidden false >> not)
    }

    importedAbsolutePaths arcPath targetRelativePath visiblePlannedPaths

/// Builds the watcher events of an import's planned folders and files. The polling watcher reports
/// each new folder as addDir, and an unclaimed addDir would start a second ARC merge after the import.
/// It builds no claim for planned paths that `ArcVaultHelper.isFileWatcherPathIgnoredFor` ignores, because
/// the watcher never reports them. A folder gets a claim even when a file with its name would not.
let createImportedWatcherEvents arcPath targetRelativePath (plannedPaths: PlannedImportPaths) =
    let folderPaths, filePaths =
        importedAbsolutePaths arcPath targetRelativePath plannedPaths

    let buildEvents (eventName: Chokidar.Events) (isDirectory: bool) (absolutePaths: string[]) =
        absolutePaths
        |> Array.map (fun absolutePath -> buildWatcherEvent arcPath (eventName.ToString()) absolutePath)
        |> Array.filter (fun event ->
            not (ArcVaultHelper.isFileWatcherPathIgnoredFor event.AbsolutePath (Some isDirectory))
        )

    Array.append (buildEvents Chokidar.Events.AddDir true folderPaths) (buildEvents Chokidar.Events.Add false filePaths)

/// Always queues a watcher event for the file tree, while ARC merge eligibility is controlled separately.
let queueFileWatcherEvent
    isArcMergeEligible
    arcPath
    (pendingEvents: ResizeArray<ArcVaultFileSystemEvent>)
    (pendingArcMergeEvents: ResizeArray<ArcVaultFileSystemEvent>)
    eventName
    changedPath
    =
    match arcPath with
    | Some rootPath ->
        let watcherEvent = buildWatcherEvent rootPath eventName changedPath
        pendingEvents.Add watcherEvent

        if isArcMergeEligible watcherEvent then
            pendingArcMergeEvents.Add watcherEvent
    | None -> ()

/// An admitted unlink for a recreated file becomes a change. An add or change for a file that is
/// missing at merge time is dropped, because the file is either mid-replacement (an add follows)
/// or deleted (a real unlink follows), and converting it would remove an entity together with
/// unsaved edits on it.
/// An unlink-dir event stays admitted because toArcMergeEvents expands it to canonical files.
/// TryApplyWatcherArcMergeIfEligible then checks each file against disk.
let normalizeAgainstDisk (events: ArcVaultFileSystemEvent list) =
    events
    |> List.choose (fun event ->
        if isAbsolute event.RelativePath then
            Some event
        elif
            eventNameEquals Chokidar.Events.Unlink event.EventName
            && existsSyncWithExactName event.AbsolutePath
        then
            Some {
                event with
                    EventName = Chokidar.Events.Change.ToString()
            }
        elif
            (eventNameEquals Chokidar.Events.Add event.EventName
             || eventNameEquals Chokidar.Events.Change event.EventName)
            && not (existsSync event.AbsolutePath)
        then
            None
        else
            Some event
    )

/// Converts raw filesystem events into ARC merge events. Unlink-dir events expand to possible canonical files.
let toArcMergeEvents (events: ArcVaultFileSystemEvent list) =
    events
    |> List.collect (fun event ->
        if eventNameEquals Chokidar.Events.Add event.EventName then
            [
                {
                    EventName = EventName.Add
                    Path = event.RelativePath
                }
            ]
        elif eventNameEquals Chokidar.Events.Change event.EventName then
            [
                {
                    EventName = EventName.Change
                    Path = event.RelativePath
                }
            ]
        elif eventNameEquals Chokidar.Events.Unlink event.EventName then
            [
                {
                    EventName = EventName.Unlink
                    Path = event.RelativePath
                }
            ]
        elif eventNameEquals Chokidar.Events.UnlinkDir event.EventName then
            event.RelativePath
            |> ArcEntityPathRules.buildFallbackUnlinkPaths
            |> List.map (fun path -> {
                EventName = EventName.Unlink
                Path = path
            })
        else
            []
    )
