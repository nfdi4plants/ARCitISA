/// The file tree members of ArcVault: scans, reconciles, directory loads and file stats.
[<AutoOpen>]
module Main.ArcVaultFileTree

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

type ArcVault with

    /// Queues a structural scan of the ARC folder. The scanned tree replaces the current one.
    member this.EnqueueFileTreeScan() : Fable.Core.JS.Promise<unit> =
        match this.path with
        | Some arcPath ->
            let caseInsensitive = this.isCaseInsensitive
            this.FileTreeWriter.EnqueueReplace(fun () -> scanStructuralFileTreeAsync arcPath caseInsensitive)
        | None -> promise { return () }

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
    /// It makes no LFS call, because the renderer joins LFS state by path from the large object index. An empty tree gets a structural scan.
    member this.EnqueueFileTreeRevalidateLoaded() : Fable.Core.JS.Promise<unit> =
        if this.FileTreeWriter.Tree.Count = 0 then
            this.EnqueueFileTreeScan()
        else
            match this.path with
            | Some arcPath ->
                this.FileTreeWriter.Enqueue(fun tree ->
                    reconcileLoadedDirectoriesAsync
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

    /// True when a directory may load. A root rename or a close replaces the tree anyway.
    member this.CanLoadDirectory =
        this.path.IsSome
        && not this.isRenamingArcRoot
        && not this.isCloseRequestPending
        && not this.isCloseApproved
        && not (this.window.isDestroyed ())

    /// Queues one writer job over the given ARC-relative directories. Unloaded directories are skipped. After
    /// the job, a content check of the large objects in those directories runs without holding the writer.
    member this.RevalidateDirectories(relativeDirectories: string[]) : Fable.Core.JS.Promise<unit> =
        match this.path with
        | Some arcPath when this.CanRevalidateFileTree ->
            let absolute =
                relativeDirectories
                |> Array.map (fun relative ->
                    join [| arcPath; relative |] |> PathHelpers.normalizePathKeepingWhitespace
                )

            promise {
                do!
                    this.FileTreeWriter.Enqueue(fun tree ->
                        reconcileLoadedDirectoriesAsync
                            arcPath
                            tree
                            (fun directory error ->
                                swatelogfn
                                    this.window.id
                                    "Unable to revalidate file tree directory '%s': %s"
                                    directory
                                    error.Message
                            )
                            absolute
                    )

                this.CheckLfsContentOf relativeDirectories
                |> Promise.catch (fun error ->
                    swatelogfn this.window.id "The lfs content check failed: %s" error.Message
                )
                |> Promise.start
            }
        | _ -> promise { return () }

    /// Reconciles one ARC-relative directory, loads it when it was not loaded and answers with its current
    /// children and the writer version after the job, also when nothing changed. A path outside the ARC and
    /// a missing directory are errors.
    member this.LoadDirectory
        (relativeDirectory: string)
        : Fable.Core.JS.Promise<Result<FileTreeDirectoryUpdateDto, exn>> =
        promise {
            match this.path with
            | None -> return Error(arcNotOpenError ())
            | Some _ when not this.CanLoadDirectory ->
                return Error(exn "The ARC is renaming or closing. Try again in a moment.")
            | Some arcPath ->
                let absolute =
                    join [| arcPath; relativeDirectory |]
                    |> PathHelpers.normalizePathKeepingWhitespace

                match toTreeRelativePath this.isCaseInsensitive arcPath absolute with
                | None -> return Error(exn $"The path '{relativeDirectory}' is outside the ARC.")
                | Some relativePath ->
                    let! outcome =
                        this.FileTreeWriter.EnqueueWithResult(fun tree -> promise {
                            let! changed = reconcileFileTreeDirectoryAsync arcPath tree absolute

                            match tree.Children absolute with
                            | Some children ->
                                return changed, Ok(toRendererEntries tree.CaseInsensitive arcPath children)
                            | None -> return changed, Error(exn $"The directory '{relativePath}' does not exist.")
                        })

                    match outcome with
                    | Error error -> return Error error
                    | Ok(Error error, _) -> return Error error
                    | Ok(Ok children, treeVersion) ->
                        return
                            Ok {|
                                treeVersion = treeVersion
                                directories = [|
                                    {|
                                        directoryPath = relativePath
                                        children = children
                                    |}
                                |]
                            |}
        }

    /// Stats one ARC-relative file. None when it is missing or not a file.
    member this.StatFile(relativePath: string) : Fable.Core.JS.Promise<Result<FileStatDto option, exn>> = promise {
        match this.path with
        | None -> return Error(arcNotOpenError ())
        | Some arcPath ->
            let absolute =
                join [| arcPath; relativePath |] |> PathHelpers.normalizePathKeepingWhitespace

            match
                toTreeRelativePath this.isCaseInsensitive arcPath absolute
                |> Option.filter (fun relative -> relative <> "")
            with
            | None -> return Error(exn $"The path '{relativePath}' is outside the ARC.")
            | Some _ ->
                try
                    let! stats = statAsync absolute

                    if stats.isFile () then
                        return
                            Ok(
                                Some {|
                                    sizeBytes = float stats.size
                                    mtimeMs = stats.mtimeMs
                                |}
                            )
                    else
                        return Ok None
                with error ->
                    match tryGetNodeErrorCode error with
                    | Some "ENOENT"
                    | Some "ENOTDIR" -> return Ok None
                    | _ -> return Error error
    }

    /// Tells the renderer about files that changed in place. The watcher only sees ARC structure files, so
    /// payload files never come through here. A file that cannot be stat-ed is skipped.
    member internal this.PublishFileContentChanges(events: ArcVaultFileSystemEvent list) = promise {
        match this.path with
        | None -> ()
        | Some root ->
            let changedFiles =
                events
                |> List.filter (fun event -> WatcherHelpers.eventNameEquals Chokidar.Events.Change event.EventName)
                |> List.map _.AbsolutePath
                |> List.distinctBy (PathKey.ofPath this.isCaseInsensitive)

            for absolutePath in changedFiles do
                try
                    let! stats = statAsync absolutePath

                    match
                        toTreeRelativePath this.isCaseInsensitive root absolutePath
                        |> Option.filter (fun relative -> relative <> "")
                    with
                    | Some relativePath when stats.isFile () ->
                        WindowSend.send<IFileTreeRendererApi>
                            this.window
                            (fun api ->
                                api.fileContentChanged {|
                                    path = relativePath
                                    sizeBytes = float stats.size
                                    mtimeMs = stats.mtimeMs
                                |}
                            )
                    | _ -> ()
                with _ ->
                    ()
    }

    /// The structural file tree for the renderer pull. While the ARC initializes the snapshot is empty. The
    /// renderer pulls again when pathChange announces the installed tree.
    member this.GetFileTreeSnapshot() : Fable.Core.JS.Promise<FileTreeSnapshotDto> = promise {
        match this.path with
        | Some arcPath when not this.isInitializingArc ->
            if this.FileTreeWriter.Tree.Count = 0 then
                do! this.EnqueueFileTreeScan()

            // Read inside the writer queue, so a job that is still running is never seen half done.
            let! outcome =
                this.FileTreeWriter.EnqueueWithResult(fun tree -> promise {
                    let entries = toRendererEntries tree.CaseInsensitive arcPath tree.Entries

                    let loaded =
                        tree.LoadedDirectories
                        |> Array.choose (toTreeRelativePath tree.CaseInsensitive arcPath)

                    return [||], (entries, loaded)
                })

            match outcome with
            | Ok((entries, loaded), treeVersion) ->
                return {|
                    treeVersion = treeVersion
                    entries = entries
                    loadedDirectories = loaded
                    caseInsensitive = this.isCaseInsensitive
                    fileWatcher = fileWatcherStateToDto this.fileWatcherState
                |}
            | Error _ ->
                // A reset dropped the job. The renderer pulls again when pathChange announces the new tree.
                return {|
                    treeVersion = this.FileTreeWriter.TreeVersion
                    entries = [||]
                    loadedDirectories = [||]
                    caseInsensitive = this.isCaseInsensitive
                    fileWatcher = fileWatcherStateToDto this.fileWatcherState
                |}
        | _ ->
            return {|
                treeVersion = this.FileTreeWriter.TreeVersion
                entries = [||]
                loadedDirectories = [||]
                caseInsensitive = this.isCaseInsensitive
                fileWatcher = fileWatcherStateToDto this.fileWatcherState
            |}
    }
