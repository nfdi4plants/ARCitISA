module Renderer.Context.FileTreeStore

open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes

/// Immutable view of the tree. Version is the store's own change counter and serves as React identity.
/// TreeVersion is the writer version of the last change that reached this snapshot.
type FileTreeSnapshot = {
    Version: int
    TreeVersion: int
    CaseInsensitive: bool
    Entries: Map<PathKey, FileEntry>
    Children: Map<PathKey, PathKey[]>
    /// The same children as entries, in the same order, so a listing needs no lookup per child.
    ChildEntries: Map<PathKey, FileEntry[]>
    Loaded: Set<PathKey>
}

type KnownState =
    | Present of FileEntry
    | Absent
    | Unknown

let private rootKey = PathKey ""

let private keyOf (caseInsensitive: bool) (path: string) = PathKey.ofPath caseInsensitive path

let private parentKey (key: PathKey) =
    let value = PathKey.value key

    match value.LastIndexOf '/' with
    | -1 -> rootKey
    | index -> PathKey(value.Substring(0, index))

let private compareChildren (left: FileEntry) (right: FileEntry) =
    match compare (not left.isDirectory) (not right.isDirectory) with
    | 0 -> PathOrder.compareNames left.name right.name
    | result -> result

/// The main process sorts by name only, which matches this order for folders that hold only files or only
/// directories (the usual case for large folders), so one pass usually replaces the sort.
let private sortChildren (entries: FileEntry[]) =
    let mutable sorted = true
    let mutable index = 1

    while sorted && index < entries.Length do
        sorted <- compareChildren entries.[index - 1] entries.[index] <= 0
        index <- index + 1

    if sorted then
        entries
    else
        entries |> Array.sortWith compareChildren

let private emptySnapshot = {
    Version = 0
    TreeVersion = -1
    CaseInsensitive = false
    Entries = Map.empty
    Children = Map.empty
    ChildEntries = Map.empty
    Loaded = Set.empty
}

let private buildSnapshot (version: int) (input: FileTreeSnapshotDto) : FileTreeSnapshot =
    let toKey = keyOf input.caseInsensitive
    let keyed = input.entries |> Array.map (fun entry -> toKey entry.path, entry)
    let loaded = input.loadedDirectories |> Array.map toKey |> Set.ofArray

    let groupedEntries =
        keyed
        // The root entry has no parent. Without this it would list itself as its own child.
        |> Array.filter (fun (key, _) -> key <> rootKey)
        |> Array.groupBy (fst >> parentKey)
        |> Array.map (fun (parent, items) -> parent, items |> Array.map snd |> sortChildren)
        |> Map.ofArray

    let grouped =
        groupedEntries
        |> Map.map (fun _ entries -> entries |> Array.map (fun entry -> toKey entry.path))

    // A loaded directory without children still needs an entry, so a lookup tells empty from not loaded.
    let children =
        loaded
        |> Set.fold
            (fun (state: Map<PathKey, PathKey[]>) key ->
                if state.ContainsKey key then
                    state
                else
                    state.Add(key, [||])
            )
            grouped

    let childEntries =
        loaded
        |> Set.fold
            (fun (state: Map<PathKey, FileEntry[]>) key ->
                if state.ContainsKey key then
                    state
                else
                    state.Add(key, [||])
            )
            groupedEntries

    {
        Version = version
        TreeVersion = input.treeVersion
        CaseInsensitive = input.caseInsensitive
        Entries = Map.ofArray keyed
        Children = children
        ChildEntries = childEntries
        Loaded = loaded
    }

/// Removes everything below a key from Children and Loaded and its descendants from Entries. The key's own entry stays.
let rec private removeDescendants (snapshot: FileTreeSnapshot) (key: PathKey) : FileTreeSnapshot =
    let below = snapshot.Children |> Map.tryFind key |> Option.defaultValue [||]

    let cleared =
        below
        |> Array.fold
            (fun state child ->
                let state = removeDescendants state child

                {
                    state with
                        Entries = state.Entries.Remove child
                }
            )
            snapshot

    {
        cleared with
            Children = cleared.Children.Remove key
            ChildEntries = cleared.ChildEntries.Remove key
            Loaded = cleared.Loaded.Remove key
    }

let private removeSubtree (snapshot: FileTreeSnapshot) (key: PathKey) : FileTreeSnapshot =
    let cleared = removeDescendants snapshot key

    {
        cleared with
            Entries = cleared.Entries.Remove key
    }

/// Applies the children of one directory. Returns the same instance when nothing changes. A version older than the
/// snapshot is ignored, and the same version applies, because one writer version is one state of the tree.
let private applyDirectory
    (snapshot: FileTreeSnapshot)
    (treeVersion: int)
    (directoryPath: string)
    (childEntries: FileEntry[])
    : FileTreeSnapshot =
    let toKey = keyOf snapshot.CaseInsensitive
    let directory = toKey directoryPath

    let isKnownDirectory =
        directory = rootKey
        || (snapshot.Entries
            |> Map.tryFind directory
            |> Option.exists (fun entry -> entry.isDirectory))

    if treeVersion < snapshot.TreeVersion || not isKnownDirectory then
        snapshot
    else
        let sorted = sortChildren childEntries
        let newKeys = sorted |> Array.map (fun entry -> toKey entry.path)
        let oldKeys = snapshot.Children |> Map.tryFind directory |> Option.defaultValue [||]

        // A directory that was not loaded has no old children, so nothing vanished and every child is new.
        let vanished =
            if oldKeys.Length = 0 then
                [||]
            else
                let present = System.Collections.Generic.HashSet<string>()

                for key in newKeys do
                    present.Add(PathKey.value key) |> ignore

                oldKeys |> Array.filter (fun key -> not (present.Contains(PathKey.value key)))

        let mutable current = vanished |> Array.fold removeSubtree snapshot
        let mutable entries = current.Entries
        let kept = Array.zeroCreate<FileEntry> sorted.Length

        if oldKeys.Length = 0 then
            for index in 0 .. sorted.Length - 1 do
                entries <- entries.Add(newKeys.[index], sorted.[index])
                kept.[index] <- sorted.[index]
        else
            for index in 0 .. sorted.Length - 1 do
                let key = newKeys.[index]
                let entry = sorted.[index]

                match entries |> Map.tryFind key with
                | Some old when old = entry -> kept.[index] <- old
                | Some old ->
                    // A folder that became a file or got another spelling (a case-only rename) is another folder
                    // now. Its old content and loaded mark go, and the update that follows loads it again.
                    if old.isDirectory && (not entry.isDirectory || old.path <> entry.path) then
                        current <- removeDescendants { current with Entries = entries } key
                        entries <- current.Entries

                    entries <- entries.Add(key, entry)
                    kept.[index] <- entry
                | None ->
                    entries <- entries.Add(key, entry)
                    kept.[index] <- entry

        let unchanged =
            vanished.Length = 0
            && oldKeys = newKeys
            && snapshot.Loaded.Contains directory
            && obj.ReferenceEquals(entries, snapshot.Entries)

        if unchanged then
            snapshot
        else
            {
                current with
                    Version = snapshot.Version + 1
                    TreeVersion = treeVersion
                    Entries = entries
                    Children = current.Children.Add(directory, newKeys)
                    ChildEntries = current.ChildEntries.Add(directory, kept)
                    Loaded = current.Loaded.Add directory
            }

/// Applies the directories of one update, parents first whatever order they arrive in, so a new directory is
/// known when its own children come.
let private applyUpdate (snapshot: FileTreeSnapshot) (update: FileTreeDirectoryUpdateDto) : FileTreeSnapshot =
    let depth (path: string) =
        (PathHelpers.normalizePath path).Split('/').Length

    update.directories
    |> Array.sortBy (fun directory ->
        if directory.directoryPath = "" then
            0
        else
            depth directory.directoryPath
    )
    |> Array.fold
        (fun current directory -> applyDirectory current update.treeVersion directory.directoryPath directory.children)
        snapshot

type FileTreeStore() =
    let mutable snapshot = emptySnapshot
    // The newest writer version seen, including updates that changed nothing.
    let mutable lastTreeVersion = -1
    let mutable loading = false
    let mutable buffer: FileTreeDirectoryUpdateDto list = []
    let listeners = ResizeArray<unit -> unit>()

    let notify () =
        for listener in Seq.toArray listeners do
            listener ()

    member _.Snapshot = snapshot

    member _.Subscribe(listener: unit -> unit) : unit -> unit =
        listeners.Add listener
        fun () -> listeners.Remove listener |> ignore

    member _.BeginLoad() =
        loading <- true
        buffer <- []

    member _.ApplySnapshot(input: FileTreeSnapshotDto) =
        let mutable next = buildSnapshot (snapshot.Version + 1) input
        lastTreeVersion <- input.treeVersion

        let pending =
            buffer
            |> List.filter (fun update -> update.treeVersion >= input.treeVersion)
            |> List.sortBy (fun update -> update.treeVersion)

        for update in pending do
            next <- applyUpdate next update
            lastTreeVersion <- max lastTreeVersion update.treeVersion

        buffer <- []
        loading <- false
        snapshot <- next
        notify ()

    member _.FailLoad() =
        loading <- false
        buffer <- []

    /// Drops everything, for a closed ARC or a new root whose snapshot is not here yet.
    member _.Reset() =
        loading <- false
        buffer <- []
        lastTreeVersion <- -1

        snapshot <- {
            emptySnapshot with
                Version = snapshot.Version + 1
        }

        notify ()

    member _.ApplyDirectoryUpdate(update: FileTreeDirectoryUpdateDto) =
        if loading then
            buffer <- update :: buffer
        elif update.treeVersion >= lastTreeVersion then
            lastTreeVersion <- update.treeVersion
            let next = applyUpdate snapshot update

            if not (obj.ReferenceEquals(next, snapshot)) then
                snapshot <- next
                notify ()

let tryFind (snapshot: FileTreeSnapshot) (path: string) : FileEntry option =
    snapshot.Entries |> Map.tryFind (keyOf snapshot.CaseInsensitive path)

let isLoaded (snapshot: FileTreeSnapshot) (path: string) : bool =
    snapshot.Loaded.Contains(keyOf snapshot.CaseInsensitive path)

let children (snapshot: FileTreeSnapshot) (path: string) : FileEntry[] =
    snapshot.ChildEntries
    |> Map.tryFind (keyOf snapshot.CaseInsensitive path)
    |> Option.defaultValue [||]

/// The number of children of a directory, without building the entries. Zero for a directory that is not loaded.
let childCount (snapshot: FileTreeSnapshot) (path: string) : int =
    snapshot.Children
    |> Map.tryFind (keyOf snapshot.CaseInsensitive path)
    |> Option.map (fun keys -> keys.Length)
    |> Option.defaultValue 0

/// Present when the entry is in the tree. Absent when its parent directory is loaded and does not list it.
/// Unknown when the parent is not loaded. The root is Present once a snapshot lists it, and Unknown before that.
let knows (snapshot: FileTreeSnapshot) (path: string) : KnownState =
    let key = keyOf snapshot.CaseInsensitive path

    match snapshot.Entries |> Map.tryFind key with
    | Some entry -> Present entry
    | None when key = rootKey -> Unknown
    | None when snapshot.Loaded.Contains(parentKey key) -> Absent
    | None -> Unknown
