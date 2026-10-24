[<AutoOpen>]
module Main.IndexedFileTree

open System.Collections.Generic
open Fable.Core
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes

/// The main-process view of an ARC folder. Entries are keyed by the PathKey of their absolute path and keep the
/// display path as scanned. A children index lists the direct children of each loaded directory in
/// PathOrder.compareNames order. The case flag is fixed per tree, so a new ARC root or a changed flag needs a new tree.
type IndexedFileTree(caseInsensitive: bool) =
    let entries = Dictionary<string, FileEntry>()
    let childKeys = Dictionary<string, string[]>()
    let loadedDirectories = HashSet<string>()

    let keyOf (path: string) =
        PathKey.ofPath caseInsensitive path |> PathKey.value

    let sameKeys (left: string[]) (right: string[]) =
        left.Length = right.Length
        && Array.forall2 (fun (a: string) b -> a = b) left right

    let rec removeKeyAndDescendants (key: string) =
        match childKeys.TryGetValue key with
        | true, children ->
            childKeys.Remove key |> ignore
            children |> Array.iter removeKeyAndDescendants
        | false, _ -> ()

        loadedDirectories.Remove key |> ignore
        entries.Remove key |> ignore

    member _.CaseInsensitive = caseInsensitive

    member _.Count = entries.Count

    member _.Entries: seq<FileEntry> = entries.Values :> seq<FileEntry>

    member _.TryFind(path: string) : FileEntry option =
        match entries.TryGetValue(keyOf path) with
        | true, entry -> Some entry
        | false, _ -> None

    /// Display paths of the loaded directories.
    member _.LoadedDirectories: string[] =
        loadedDirectories
        |> Seq.choose (fun key ->
            match entries.TryGetValue key with
            | true, entry -> Some entry.path
            | false, _ -> None
        )
        |> Seq.toArray

    member _.IsLoaded(directoryPath: string) =
        loadedDirectories.Contains(keyOf directoryPath)

    /// The direct children of a loaded directory, or None when the directory is not loaded.
    member _.Children(directoryPath: string) : FileEntry[] option =
        let key = keyOf directoryPath

        if loadedDirectories.Contains key then
            match childKeys.TryGetValue key with
            | true, children -> children |> Array.map (fun childKey -> entries.[childKey]) |> Some
            | false, _ -> Some [||]
        else
            None

    member _.SetRoot(entry: FileEntry) = entries.[keyOf entry.path] <- entry

    /// Replaces the direct children of a directory and marks it loaded. Children that disappeared lose their
    /// subtree. A child with the same name, path and kind keeps its existing instance. Returns whether anything
    /// changed and the child directories that were inserted or replaced. A replaced directory (for example after a
    /// case-only rename) has lost its subtree and its loaded mark, so the caller has to load it again.
    member _.ReplaceChildrenReportingNew(directoryPath: string, children: FileEntry[]) : bool * FileEntry[] =
        let newDirectories = ResizeArray<FileEntry>()
        let directoryKey = keyOf directoryPath

        let incoming =
            children
            |> Array.sortWith (fun left right -> PathOrder.compareNames left.name right.name)
            |> Array.map (fun child -> keyOf child.path, child)
            |> Array.distinctBy fst

        let incomingKeys = incoming |> Array.map fst

        let previousKeys =
            match childKeys.TryGetValue directoryKey with
            | true, keys -> keys
            | false, _ -> [||]

        let mutable changed = loadedDirectories.Add directoryKey

        let incomingKeySet = HashSet<string>(incomingKeys)

        for previousKey in previousKeys do
            if not (incomingKeySet.Contains previousKey) then
                removeKeyAndDescendants previousKey
                changed <- true

        for (key, child) in incoming do
            match entries.TryGetValue key with
            | true, existing when
                existing.name = child.name
                && existing.path = child.path
                && existing.isDirectory = child.isDirectory
                ->
                ()
            | true, existing ->
                if existing.isDirectory then
                    removeKeyAndDescendants key

                entries.[key] <- child
                changed <- true

                if child.isDirectory then
                    newDirectories.Add child
            | false, _ ->
                entries.[key] <- child
                changed <- true

                if child.isDirectory then
                    newDirectories.Add child

        if not (sameKeys previousKeys incomingKeys) then
            changed <- true

        if incomingKeys.Length = 0 then
            childKeys.Remove directoryKey |> ignore
        else
            childKeys.[directoryKey] <- incomingKeys

        changed, newDirectories.ToArray()

    /// Replaces the direct children of a directory. Returns true when anything changed.
    member this.ReplaceChildren(directoryPath: string, children: FileEntry[]) : bool =
        this.ReplaceChildrenReportingNew(directoryPath, children) |> fst

    /// Removes a path with everything below it, also from the child list of its parent.
    member _.RemoveSubtree(path: string) : bool =
        let key = keyOf path

        if not (entries.ContainsKey key) && not (childKeys.ContainsKey key) then
            false
        else
            removeKeyAndDescendants key

            match PathHelpers.tryGetParentPath path with
            | Some parentPath ->
                let parentKey = keyOf parentPath

                match childKeys.TryGetValue parentKey with
                | true, siblings ->
                    let remaining = siblings |> Array.filter (fun sibling -> sibling <> key)

                    if remaining.Length = 0 then
                        childKeys.Remove parentKey |> ignore
                    else
                        childKeys.[parentKey] <- remaining
                | false, _ -> ()
            | None -> ()

            true

    member _.Clear() =
        entries.Clear()
        childKeys.Clear()
        loadedDirectories.Clear()

/// Runs every main-process change of one vault's tree one after another. TreeVersion grows with each job that
/// changed the tree, and the publish callback runs once per such job.
type FileTreeWriter(caseInsensitive: bool, log: string -> unit, publish: IndexedFileTree -> unit) =
    let mutable tree = IndexedFileTree(caseInsensitive)
    let mutable tail: JS.Promise<unit> = JS.Constructors.Promise.resolve ()
    let mutable generation = 0
    let mutable treeVersion = 0

    member val Publish: IndexedFileTree -> unit = publish with get, set

    member _.Tree = tree

    member _.TreeVersion = treeVersion

    /// Counts the resets. Jobs enqueued before a reset are skipped.
    member _.Generation = generation

    member private this.EnqueueCore
        (job: IndexedFileTree -> JS.Promise<IndexedFileTree * bool>, beforePublish: (unit -> unit) option)
        : JS.Promise<unit> =
        let capturedGeneration = generation

        let run () = promise {
            if capturedGeneration = generation then
                try
                    let! (nextTree, changed) = job tree

                    // A reset during the job retired this tree, so the result is dropped.
                    if capturedGeneration = generation then
                        tree <- nextTree

                        if changed then
                            treeVersion <- treeVersion + 1
                            beforePublish |> Option.iter (fun callback -> callback ())
                            this.Publish tree
                with error ->
                    log $"File tree update failed: {error.Message}"
        }

        let queued = tail |> Promise.bind run
        tail <- queued
        queued

    /// Queues a job that changes the given tree in place. It returns true when it changed the tree.
    member this.Enqueue(job: IndexedFileTree -> JS.Promise<bool>) : JS.Promise<unit> =
        this.EnqueueCore((fun current -> job current |> Promise.map (fun changed -> current, changed)), None)

    /// Queues a full scan. The scanned tree replaces the current one. beforePublish runs after the tree is
    /// installed and before it is published.
    member this.EnqueueReplace
        (scan: unit -> JS.Promise<IndexedFileTree>, ?beforePublish: unit -> unit)
        : JS.Promise<unit> =
        this.EnqueueCore((fun _ -> scan () |> Promise.map (fun scanned -> scanned, true)), beforePublish)

    /// Starts over with an empty tree. Jobs that were enqueued earlier will not run.
    member _.Reset(newCaseInsensitive: bool) =
        generation <- generation + 1
        tree <- IndexedFileTree(newCaseInsensitive)
