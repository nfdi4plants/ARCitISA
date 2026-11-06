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

/// Builds the watcher events of an import's planned folders and files. It builds events only for the paths
/// that the structural watcher reports, so the import merges its ARC structure files and claims nothing that
/// no delayed watcher event would consume.
let createImportedWatcherEvents arcPath targetRelativePath (plannedPaths: PlannedImportPaths) =
    let folderPaths, filePaths =
        importedAbsolutePaths arcPath targetRelativePath plannedPaths

    let buildEvents (eventName: Chokidar.Events) (isDirectory: bool) (absolutePaths: string[]) =
        absolutePaths
        |> Array.map (fun absolutePath -> buildWatcherEvent arcPath (eventName.ToString()) absolutePath)
        |> Array.filter (fun event ->
            ArcVaultHelper.isStructuralWatcherPathOfKind arcPath event.AbsolutePath (Some isDirectory)
        )

    Array.append (buildEvents Chokidar.Events.AddDir true folderPaths) (buildEvents Chokidar.Events.Add false filePaths)

/// The directories a batch of watcher events marks dirty, without duplicates. The parent of every event path is
/// dirty. An added directory is dirty too, because its children are new. A removed directory only dirties its parent.
let dirtyDirectoriesOfEvents (caseInsensitive: bool) (events: ArcVaultFileSystemEvent list) : string[] =
    let dirty = Dictionary<string, string>()

    let mark (path: string) =
        let key = PathKey.ofPath caseInsensitive path |> PathKey.value

        if not (dirty.ContainsKey key) then
            dirty.[key] <- PathHelpers.normalizePath path

    for event in events do
        PathHelpers.tryGetParentPath event.AbsolutePath |> Option.iter mark

        if eventNameEquals Chokidar.Events.AddDir event.EventName then
            mark event.AbsolutePath

    dirty.Values |> Seq.toArray
