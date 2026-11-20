/// The large object index members of ArcVault, with the content check and the Git state focus check that keep the index current.
[<AutoOpen>]
module Main.ArcVaultLargeObjects

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
open Main.GitStateStamp

/// Whether the file on disk disagrees with the indexed state of its large object. Git LFS lists an object as checked
/// out only when the file has the object size. A materialized entry whose file has another size is stale, and so is
/// a pointer entry whose file has the object size. A pointer entry with any other size stays as it is, because a
/// locally modified file keeps that state however often the index is rebuilt. A checkout or pull made outside the
/// app changes the working files and nothing in the Git metadata that the focus check reads, so this size
/// comparison is the signal for it.
let lfsContentMismatch (isMaterialized: bool) (indexedSize: float option) (fileSize: float) : bool =
    indexedSize
    |> Option.exists (fun expected ->
        if isMaterialized then
            fileSize <> expected
        else
            fileSize = expected
    )

/// Files are stat-ed in groups of this size.
let private lfsContentStatConcurrency = 8

type ArcVault with

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

    /// True when the renderer may ask for a revalidation. During a write, a root rename, an initialization or a
    /// pending close the tree is about to change anyway, so the request is skipped.
    member this.CanRevalidateFileTree =
        this.path.IsSome
        && not this.isInitializingArc
        && not this.isBusyWriting
        && not this.isRenamingArcRoot
        && not this.isCloseRequestPending
        && not this.isCloseApproved
        && not (this.window.isDestroyed ())

    /// Compares the large objects of the index that sit directly in the given ARC-relative directories with their
    /// files. A mismatch means that content changed outside the app, for example by git lfs checkout or pull,
    /// and invalidates the index once. Only the files of those directories are stat-ed, a few at a time. The check
    /// stays out of the way of a rebuild that is pending or running, and one check runs at a time.
    member internal this.CheckLfsContentOf(relativeDirectories: string[]) : Fable.Core.JS.Promise<unit> = promise {
        let isRebuildUnderWay (index: Main.LargeObjectIndex.LargeObjectIndex) =
            this.LargeObjectInvalidationTimeout.IsSome || index.IsListing

        match this.path, this.LargeObjectIndex with
        | Some root, Some index when not this.LfsContentCheckRunning && not (isRebuildUnderWay index) ->
            match index.State with
            | Main.LargeObjectIndex.LargeObjectIndexState.Ready ready ->
                this.LfsContentCheckRunning <- true

                try
                    let caseInsensitive = this.isCaseInsensitive

                    let directoryKeys =
                        relativeDirectories |> Array.map (PathKey.ofPath caseInsensitive) |> Set.ofArray

                    let parentKeyOf (path: string) =
                        match path.LastIndexOf '/' with
                        | -1 -> PathKey.ofPath caseInsensitive ""
                        | position -> PathKey.ofPath caseInsensitive (path.Substring(0, position))

                    let candidates =
                        ready.Entries
                        |> Array.filter (fun entry -> directoryKeys.Contains(parentKeyOf entry.Path))

                    let mutable mismatch = false
                    let mutable position = 0

                    while not mismatch && position < candidates.Length && this.CanRevalidateFileTree do
                        let group =
                            candidates |> Array.skip position |> Array.truncate lfsContentStatConcurrency

                        position <- position + group.Length

                        let! sizes =
                            group
                            |> Array.map (fun entry ->
                                this.LargeObjectContentStat(join [| root; entry.Path |])
                                |> Promise.map (fun size -> entry, size)
                            )
                            |> Promise.all

                        mismatch <-
                            sizes
                            |> Array.exists (fun (entry, size) ->
                                size |> Option.exists (lfsContentMismatch entry.IsMaterialized entry.SizeBytes)
                            )

                    // The result counts only for the index state that was checked. A rebuild that started or
                    // finished meanwhile has seen the files itself.
                    let sameReadyState =
                        match this.LargeObjectIndex with
                        | Some current when obj.ReferenceEquals(current, index) ->
                            match current.State with
                            | Main.LargeObjectIndex.LargeObjectIndexState.Ready now ->
                                now.Version = ready.Version && not (isRebuildUnderWay current)
                            | _ -> false
                        | _ -> false

                    if mismatch && sameReadyState then
                        swatelogfn
                            this.window.id
                            "A Git LFS file no longer matches the large object index after window focus, so the index is rebuilt."

                        this.InvalidateLargeObjectIndex()
                finally
                    this.LfsContentCheckRunning <- false
            | _ -> ()
        | _ -> ()
    }
