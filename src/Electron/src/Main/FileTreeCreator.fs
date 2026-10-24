[<AutoOpen>]
module Main.FileTreeCreator

open System
open System.Collections.Generic
open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.VersionControl
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions

let normalizeRootPath (path: string) =
    resolve [| path |] |> PathHelpers.normalizePath

/// Result of stat-ing one probe path.
type CaseProbeStat =
    | ProbeIdentity of dev: string * ino: string
    | ProbeMissing
    | ProbeUnreadable

let private swapCase (name: string) =
    name
    |> String.map (fun c ->
        if Char.IsUpper c then Char.ToLowerInvariant c
        elif Char.IsLower c then Char.ToUpperInvariant c
        else c
    )

let private lstatForProbeAsync (path: string) : JS.Promise<CaseProbeStat> = promise {
    try
        let! stats = lstatBigintAsync path (LstatBigintOptions(true))
        return ProbeIdentity(stats.dev.ToString(), stats.ino.ToString())
    with error ->
        return
            match tryGetNodeErrorCode error with
            | Some "ENOENT" -> ProbeMissing
            | _ -> ProbeUnreadable
}

/// Decides whether the file system under an ARC root ignores case. It starts from the platform default and
/// stats the case-swapped name of an existing root entry. When both names resolve to the same device and
/// inode, case is ignored. When the swapped name is missing, case matters. Other results keep the default,
/// and the probe never creates files.
let detectCaseInsensitiveWithAsync
    (defaultCaseInsensitive: bool)
    (listRoot: string -> JS.Promise<string[]>)
    (probeStat: string -> JS.Promise<CaseProbeStat>)
    (arcRoot: string)
    : JS.Promise<bool> =
    promise {
        try
            let! names = listRoot arcRoot

            match names |> Array.tryFind (fun name -> swapCase name <> name) with
            | None -> return defaultCaseInsensitive
            | Some name ->
                let original = join [| arcRoot; name |]
                let swapped = join [| arcRoot; swapCase name |]
                let! originalStat = probeStat original
                let! swappedStat = probeStat swapped

                return
                    match originalStat, swappedStat with
                    | ProbeIdentity(dev, ino), ProbeIdentity(swappedDev, swappedIno) when ino <> "0" ->
                        if dev = swappedDev && ino = swappedIno then
                            true
                        else
                            defaultCaseInsensitive
                    | ProbeIdentity _, ProbeMissing -> false
                    | _ -> defaultCaseInsensitive
        with _ ->
            return defaultCaseInsensitive
    }

/// Case sensitivity of the file system under an ARC root, probed once when the ARC opens.
let detectCaseInsensitiveAsync (arcRoot: string) : JS.Promise<bool> =
    let defaultCaseInsensitive =
        match ProviderComposition.currentPathCaseSensitivity () with
        | CaseInsensitive -> true
        | CaseSensitive -> false

    detectCaseInsensitiveWithAsync defaultCaseInsensitive readdirAsync lstatForProbeAsync arcRoot

let private tryListLargeObjects
    (repoRoot: string)
    (openSession: bool)
    : Fable.Core.JS.Promise<Map<string, ObjectStateDto>> =
    promise {
        try
            let context = OperationContext.detached "file-tree-objects"
            let host = WorkspaceSessionHost.get ()

            let! hostedSession =
                if openSession then
                    promise {
                        let! opened = host.OpenSession(repoRoot, context) |> Async.StartAsPromise

                        return
                            match opened with
                            | Succeeded outcome
                            | PartiallySucceeded(outcome, _) -> Some outcome.Value
                            | Failed _ -> None
                    }
                else
                    promise { return host.TryGetSession repoRoot }

            match hostedSession with
            | None -> return Map.empty
            | Some hosted ->
                match hosted.Session.ObjectMaterialization with
                | None -> return Map.empty
                | Some materialization ->
                    let! listed = materialization.ListObjects context |> Async.StartAsPromise

                    match listed with
                    | Succeeded outcome
                    | PartiallySucceeded(outcome, _) ->
                        return
                            outcome.Value
                            |> Array.map (fun (objectState: ObjectState) ->
                                let dto = Mappings.objectState objectState
                                dto.Path, dto
                            )
                            |> Map.ofArray
                    | Failed _ -> return Map.empty
        with _ ->
            return Map.empty
    }

let private withFileEntryLfsMetadata
    (repoRoot: string)
    (largeObjectsByRelativePath: Map<string, ObjectStateDto>)
    (largeObjectsByComparisonKey: Map<string, ObjectStateDto>)
    (entry: FileEntry)
    : FileEntry =
    if entry.isDirectory then
        entry
    else
        match tryGetRepoRelativePath repoRoot entry.path with
        | Some relativePath ->
            let normalizedRelativePath = PathHelpers.normalizeSeparators relativePath

            let largeObject =
                match Map.tryFind normalizedRelativePath largeObjectsByRelativePath with
                | Some largeObject -> Some largeObject
                | None ->
                    normalizedRelativePath
                    |> PathHelpers.normalizeForUnicodeComparison
                    |> fun comparisonKey -> Map.tryFind comparisonKey largeObjectsByComparisonKey

            { entry with largeObject = largeObject }
        | None -> { entry with largeObject = None }

let private buildLargeObjectsByComparisonKey (largeObjectsByRelativePath: Map<string, ObjectStateDto>) =
    largeObjectsByRelativePath
    |> Map.toSeq
    |> Seq.groupBy (fun (relativePath, _) -> PathHelpers.normalizeForUnicodeComparison relativePath)
    |> Seq.choose (fun (comparisonKey, matchingObjects) ->
        match matchingObjects |> Seq.toList with
        | [ (_, largeObject) ] -> Some(comparisonKey, largeObject)
        | _ -> None
    )
    |> Map.ofSeq

let withFileEntriesLfsMetadata
    (repoRoot: string)
    (largeObjectsByRelativePath: Map<string, ObjectStateDto>)
    (entries: FileEntry[])
    : FileEntry[] =
    let largeObjectsByComparisonKey =
        buildLargeObjectsByComparisonKey largeObjectsByRelativePath

    entries
    |> Array.map (withFileEntryLfsMetadata repoRoot largeObjectsByRelativePath largeObjectsByComparisonKey)

/// Build the renderer snapshot using ARC-relative dictionary keys and FileEntry paths.
let toRendererFileTree (repoRoot: string) (entries: seq<FileEntry>) : Dictionary<string, FileEntry> =
    let rendererFileTree = Dictionary<string, FileEntry>()

    entries
    |> Seq.iter (fun entry ->
        match tryGetRepoRelativePathOrRoot repoRoot entry.path with
        | Some relativePath -> rendererFileTree.[relativePath] <- { entry with path = relativePath }
        | None -> ()
    )

    rendererFileTree

let private relativeToRoot (arcRoot: string) (fullPath: string) =
    let root = PathHelpers.normalizePath arcRoot
    (PathHelpers.normalizePath fullPath).Substring(root.Length).TrimStart('/')

/// Lists the direct children of one directory with a single readdir. Inventory ignore rules apply and no
/// Git LFS call is made.
let readDirectoryChildrenAsync (arcRoot: string) (directoryPath: string) : JS.Promise<FileEntry[]> = promise {
    let! dirents = readdirWithTypesAsync directoryPath (ReaddirOptions(withFileTypes = true))

    return
        dirents
        |> Array.choose (fun dirent ->
            let name = dirent.name
            let isDirectory = dirent.isDirectory ()
            let fullPath = join [| directoryPath; name |] |> PathHelpers.normalizeSeparators

            if isIgnoredInventoryEntry name (relativeToRoot arcRoot fullPath) isDirectory then
                None
            else
                Some(FileEntry.create (name, fullPath, isDirectory, None))
        )
        |> Array.sortWith (fun left right -> PathOrder.compareNames left.name right.name)
}

let private isMissingDirectoryError (error: exn) =
    match tryGetNodeErrorCode error with
    | Some "ENOENT"
    | Some "ENOTDIR" -> true
    | _ -> false

/// Interim until the lazy tree (step 4.1): a directory that is new to the tree is scanned right away, so the
/// tree stays complete. Delete this step when directories load on demand.
let rec private loadNewSubdirectoriesAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (newDirectories: FileEntry[])
    : JS.Promise<bool> =
    promise {
        let mutable changed = false

        for directory in newDirectories do
            try
                let! children = readDirectoryChildrenAsync arcRoot directory.path

                let newChildDirectories =
                    children
                    |> Array.filter (fun child -> child.isDirectory && (tree.TryFind child.path).IsNone)

                if tree.ReplaceChildren(directory.path, children) then
                    changed <- true

                let! nestedChanged = loadNewSubdirectoriesAsync arcRoot tree newChildDirectories

                if nestedChanged then
                    changed <- true
            with error when isMissingDirectoryError error ->
                ()

        return changed
    }

/// Reads one directory and applies its children to the tree. A directory that no longer exists loses its
/// subtree. Returns true when the tree changed.
let reconcileFileTreeDirectoryAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (directoryPath: string)
    : JS.Promise<bool> =
    promise {
        try
            let! children = readDirectoryChildrenAsync arcRoot directoryPath

            let newDirectories =
                children
                |> Array.filter (fun child -> child.isDirectory && (tree.TryFind child.path).IsNone)

            let childrenChanged = tree.ReplaceChildren(directoryPath, children)
            let! subdirectoriesChanged = loadNewSubdirectoriesAsync arcRoot tree newDirectories
            return childrenChanged || subdirectoriesChanged
        with error when isMissingDirectoryError error ->
            return tree.RemoveSubtree directoryPath
    }

/// Scans the whole ARC folder into a tree with every directory loaded. The root entry is the ARC folder itself.
/// LFS metadata is attached once per scan.
let scanFileTreeAsync (arcRoot: string) (caseInsensitive: bool) (openSession: bool) : JS.Promise<IndexedFileTree> = promise {
    let repoRoot = normalizeRootPath arcRoot
    let tree = IndexedFileTree(caseInsensitive)

    let! rootStats = statAsync repoRoot
    let rootIsDirectory = rootStats.isDirectory ()
    tree.SetRoot(FileEntry.create (basename repoRoot, repoRoot, rootIsDirectory, None))

    if rootIsDirectory then
        let listed = ResizeArray<string * FileEntry[]>()
        let pending = Stack<string>()
        pending.Push repoRoot

        while pending.Count > 0 do
            let directoryPath = pending.Pop()
            let! children = readDirectoryChildrenAsync repoRoot directoryPath
            listed.Add((directoryPath, children))

            for child in children do
                if child.isDirectory then
                    pending.Push child.path

        let! largeObjectsByRelativePath = tryListLargeObjects repoRoot openSession

        let largeObjectsByComparisonKey =
            buildLargeObjectsByComparisonKey largeObjectsByRelativePath

        for (directoryPath, children) in listed do
            let withMetadata =
                children
                |> Array.map (withFileEntryLfsMetadata repoRoot largeObjectsByRelativePath largeObjectsByComparisonKey)

            tree.ReplaceChildren(directoryPath, withMetadata) |> ignore

    return tree
}

/// Reconciles directories that a writer marked dirty, from the root downwards. A directory the tree has not
/// loaded is replaced by its nearest loaded ancestor. One failing directory is reported to onError and does not
/// stop the others. Returns true when any reconcile changed the tree.
let reconcileDirtyDirectoriesAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (onError: string -> exn -> unit)
    (directories: string[])
    : JS.Promise<bool> =
    promise {
        let rootKey = PathKey.ofPath tree.CaseInsensitive arcRoot |> PathKey.value

        let isInsideRoot (path: string) =
            let key = PathKey.ofPath tree.CaseInsensitive path |> PathKey.value
            key = rootKey || key.StartsWith(rootKey + "/")

        let rec nearestLoaded (path: string) =
            if not (isInsideRoot path) then
                None
            elif tree.IsLoaded path then
                Some path
            else
                match PathHelpers.tryGetParentPath path with
                | Some parent -> nearestLoaded parent
                | None -> None

        let depth (path: string) =
            (PathHelpers.normalizePath path).Split('/').Length

        let ordered = directories |> Array.sortBy depth
        let reconciled = HashSet<string>()
        let mutable changed = false

        for directory in ordered do
            match nearestLoaded directory with
            | Some target when reconciled.Add(PathKey.ofPath tree.CaseInsensitive target |> PathKey.value) ->
                try
                    let! directoryChanged = reconcileFileTreeDirectoryAsync arcRoot tree target

                    if directoryChanged then
                        changed <- true
                with error ->
                    onError target error
            | _ -> ()

        return changed
    }
