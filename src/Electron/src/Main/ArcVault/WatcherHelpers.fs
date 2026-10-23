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

/// Returns a check whether a child of the directory exists with exactly its name, so a case-only rename
/// counts as gone. The directory is listed once, on the first check of a path that exists.
let existsSyncWithExactNameIn (directoryPath: string) =
    let names = lazy HashSet<string>(readdirSync directoryPath)
    fun (path: string) -> existsSync path && names.Value.Contains(basename path)

let existsSyncWithExactName (path: string) =
    existsSyncWithExactNameIn (dirname path) path

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

let createImportedFileWatcherEvents arcPath targetRelativePath sourceAbsolutePaths =
    let targetRelativePath =
        PathHelpers.normalizeCanonicalRelativePath targetRelativePath

    sourceAbsolutePaths
    |> Array.map (fun sourcePath ->
        let fileName = basename sourcePath

        let relativePath =
            if String.IsNullOrWhiteSpace targetRelativePath then
                fileName
            else
                $"{targetRelativePath}/{fileName}"
            |> PathHelpers.normalizePath

        buildWatcherEvent arcPath (Chokidar.Events.Add.ToString()) relativePath
    )

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

/// Always queues a watcher event for the file tree. The ARC merge queue only gets events that expand to at least one
/// merge event, so a batch of directory additions never loads the ARC. UnlinkDir expands to canonical paths and stays eligible.
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

        // The eligibility check runs first. It also consumes the imported-path marker of the event.
        if
            isArcMergeEligible watcherEvent
            && not (toArcMergeEvents [ watcherEvent ]).IsEmpty
        then
            pendingArcMergeEvents.Add watcherEvent
    | None -> ()
