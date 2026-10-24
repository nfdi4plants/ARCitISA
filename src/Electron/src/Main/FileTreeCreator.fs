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

/// The ARC-relative path of an absolute path inside the root, "" for the root itself, and None for a path outside
/// the root or one with traversal segments. A name that ends in whitespace keeps it, unlike in the helpers that trim.
let toTreeRelativePath (repoRoot: string) (absolutePath: string) : string option =
    let root = PathHelpers.normalizePathKeepingWhitespace repoRoot
    let path = PathHelpers.normalizePathKeepingWhitespace absolutePath

    if String.IsNullOrEmpty root || String.IsNullOrEmpty path then
        None
    elif String.Equals(path, root, StringComparison.OrdinalIgnoreCase) then
        Some ""
    elif path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) then
        let relative = path.Substring(root.Length + 1)

        if
            String.IsNullOrEmpty relative
            || PathHelpers.containsPathTraversalSegments relative
        then
            None
        else
            Some relative
    else
        None

/// Converts tree entries to the entries the renderer sees. Paths become ARC-relative and the root is "".
/// Entries outside the ARC root are dropped.
let toRendererEntries (repoRoot: string) (entries: seq<FileEntry>) : FileEntry[] =
    entries
    |> Seq.choose (fun entry ->
        toTreeRelativePath repoRoot entry.path
        |> Option.map (fun relativePath -> { entry with path = relativePath })
    )
    |> Seq.toArray

let private relativeToRoot (arcRoot: string) (fullPath: string) =
    let root = PathHelpers.normalizePathKeepingWhitespace arcRoot
    (PathHelpers.normalizePathKeepingWhitespace fullPath).Substring(root.Length).TrimStart('/')

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
                Some(FileEntry.create (name, fullPath, isDirectory))
        )
        |> Array.sortWith (fun left right -> PathOrder.compareNames left.name right.name)
}

let private isMissingDirectoryError (error: exn) =
    match tryGetNodeErrorCode error with
    | Some "ENOENT"
    | Some "ENOTDIR" -> true
    | _ -> false

let private zoneFolderNames =
    ArcEntityPathRules.allAddZones |> List.map ArcEntityPathRules.zoneFolderName

/// Where a directory sits in the ARC structure: the root, a zone folder (studies, assays, workflows, runs) or an
/// entity folder inside a zone. Everything else is a payload directory.
type private StructuralRole =
    | RootFolder
    | ZoneFolder
    | EntityFolder
    | PayloadFolder

let private structuralRole (arcRoot: string) (caseInsensitive: bool) (directoryPath: string) =
    let segments =
        (relativeToRoot arcRoot directoryPath).Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)

    let isZoneName (name: string) =
        let key = PathKey.ofPath caseInsensitive name

        zoneFolderNames
        |> List.exists (fun zone -> PathKey.ofPath caseInsensitive zone = key)

    match segments.Length with
    | 0 -> RootFolder
    | 1 when isZoneName segments.[0] -> ZoneFolder
    | 2 when isZoneName segments.[0] -> EntityFolder
    | _ -> PayloadFolder

/// Loads a directory the structure wants loaded: zone folders with their entity folders, and entity folders. Other
/// directories stay unloaded. Returns the directories whose child list changed.
let rec private loadStructuralAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (directoryPath: string)
    : JS.Promise<string[]> =
    promise {
        match structuralRole arcRoot tree.CaseInsensitive directoryPath with
        | RootFolder
        | PayloadFolder -> return [||]
        | role ->
            try
                let! children = readDirectoryChildrenAsync arcRoot directoryPath
                let changed = tree.ReplaceChildren(directoryPath, children)
                let mutable all = [ changed ]

                if role = ZoneFolder then
                    for child in children do
                        if child.isDirectory then
                            let! nested = loadStructuralAsync arcRoot tree child.path
                            all <- nested :: all

                return mergeChangedDirectories tree.CaseInsensitive all
            with error when isMissingDirectoryError error ->
                return [||]
    }

/// Reads one directory and applies its children to the tree. A directory that no longer exists loses its
/// subtree. A new child directory stays unloaded, except a new zone folder under the root and a new entity folder
/// inside a zone folder, which load their structure so the structural invariant holds for changes made outside the
/// app. Returns the directories whose child list changed.
let rec reconcileFileTreeDirectoryAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (directoryPath: string)
    : JS.Promise<string[]> =
    promise {
        let! attempt = promise {
            try
                let! children = readDirectoryChildrenAsync arcRoot directoryPath

                let changed, newDirectories, replacedLoaded =
                    tree.ReplaceChildrenReportingNew(directoryPath, children)

                let mutable all = [ changed ]

                match structuralRole arcRoot tree.CaseInsensitive directoryPath with
                | RootFolder
                | ZoneFolder ->
                    for directory in newDirectories do
                        let! loaded = loadStructuralAsync arcRoot tree directory.path
                        all <- loaded :: all
                | EntityFolder
                | PayloadFolder -> ()

                // A directory that was loaded and got another spelling lost its subtree. The renderer expects its
                // children in the same update, so it loads again here whatever its place in the structure.
                for directory in replacedLoaded do
                    if not (tree.IsLoaded directory.path) then
                        try
                            let! reloaded = readDirectoryChildrenAsync arcRoot directory.path
                            all <- tree.ReplaceChildren(directory.path, reloaded) :: all
                        with error when isMissingDirectoryError error ->
                            ()

                return Ok(mergeChangedDirectories tree.CaseInsensitive all)
            with error when isMissingDirectoryError error ->
                return Error error
        }

        match attempt with
        | Ok changed -> return changed
        | Error error ->
            let removed = tree.RemoveSubtree directoryPath

            // A folder that became a file leaves a listing in its parent that has to show the file.
            if tryGetNodeErrorCode error = Some "ENOTDIR" then
                match PathHelpers.tryGetParentPathKeepingWhitespace directoryPath with
                | Some parent when tree.IsLoaded parent ->
                    let! parentChanged = reconcileFileTreeDirectoryAsync arcRoot tree parent
                    return mergeChangedDirectories tree.CaseInsensitive [ removed; parentChanged ]
                | _ -> return removed
            else
                return removed
    }

/// Scans the structure of the ARC folder: the root, every existing zone folder and every entity folder inside a
/// zone are loaded. Other directories are entries of their loaded parent and stay unloaded. The root entry is the
/// ARC folder itself.
let scanStructuralFileTreeAsync (arcRoot: string) (caseInsensitive: bool) : JS.Promise<IndexedFileTree> = promise {
    let repoRoot = normalizeRootPath arcRoot
    let tree = IndexedFileTree(caseInsensitive)

    let! rootStats = statAsync repoRoot
    let rootIsDirectory = rootStats.isDirectory ()
    tree.SetRoot(FileEntry.create (basename repoRoot, repoRoot, rootIsDirectory))

    if rootIsDirectory then
        let! children = readDirectoryChildrenAsync repoRoot repoRoot
        tree.ReplaceChildren(repoRoot, children) |> ignore

        for child in children do
            if child.isDirectory then
                let! _ = loadStructuralAsync repoRoot tree child.path
                ()

    return tree
}

/// Reconciles the given directories that the tree has loaded, from the root downwards. A directory that is not
/// loaded is skipped. One failing directory is reported to onError and does not stop the others. Returns the
/// directories whose child list changed.
let reconcileLoadedDirectoriesAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (onError: string -> exn -> unit)
    (directories: string[])
    : JS.Promise<string[]> =
    promise {
        let depth (path: string) =
            (PathHelpers.normalizePath path).Split('/').Length

        let ordered = directories |> Array.sortBy depth
        let reconciled = HashSet<string>()
        let mutable all: string[] list = []

        for directory in ordered do
            // The check runs per directory, because an earlier reconcile can drop a later one from the tree.
            if
                tree.IsLoaded directory
                && reconciled.Add(PathKey.ofPath tree.CaseInsensitive directory |> PathKey.value)
            then
                try
                    let! changed = reconcileFileTreeDirectoryAsync arcRoot tree directory
                    all <- changed :: all
                with error ->
                    onError directory error

        return mergeChangedDirectories tree.CaseInsensitive all
    }

/// Reconciles directories that a writer marked dirty, from the root downwards. A directory the tree has not
/// loaded is replaced by its nearest loaded ancestor. One failing directory is reported to onError and does not
/// stop the others. Returns the directories whose child list changed.
let reconcileDirtyDirectoriesAsync
    (arcRoot: string)
    (tree: IndexedFileTree)
    (onError: string -> exn -> unit)
    (directories: string[])
    : JS.Promise<string[]> =
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
            match PathHelpers.tryGetParentPathKeepingWhitespace path with
            | Some parent -> nearestLoaded parent
            | None -> None

    // Resolved up front. A loaded directory can only vanish during the run, and reconcileLoadedDirectoriesAsync
    // checks again before each directory.
    let targets = directories |> Array.choose nearestLoaded

    reconcileLoadedDirectoriesAsync arcRoot tree onError targets
