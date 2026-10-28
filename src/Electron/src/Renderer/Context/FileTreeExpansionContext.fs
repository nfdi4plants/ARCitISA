/// Which folders of the file tree are expanded, loading or failed, plus the focus revalidation of the loaded
/// folders and the content stamp of the selected file. The state lives above the sidebar panels, so a panel switch
/// that remounts the file tree keeps it. Expanded folders are ARC-relative paths, so they survive a rename of the
/// ARC folder and are cleared for another ARC or a closed one. The load bookkeeping resets with every root change.
module Renderer.Context.FileTreeExpansionContext

open Fable.Core
open Feliz
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
open Renderer.Context.FileStateContext
open Renderer.Context.FileTreeStore

type FolderLoadState =
    | Loading
    | Failed of string

/// What an expanded set needs from a snapshot. Expanded keeps the ids that are not known to be gone, spelled like
/// the entry in the tree. ToLoad lists the expanded directories that are in the tree but not loaded, parents first.
type ExpansionPlan = {
    Expanded: Set<string>
    ToLoad: string list
}

let planExpansion (snapshot: FileTreeSnapshot) (expandedIds: Set<string>) : ExpansionPlan =
    let states = expandedIds |> Seq.map (fun id -> id, knows snapshot id) |> Seq.toList

    // The tree compares paths by their key, so an id with another case than the entry (after a case-only rename)
    // becomes the entry's own spelling. That is the id the rows carry.
    let expanded =
        states
        |> List.choose (fun (id, state) ->
            match state with
            | Absent -> None
            | Present entry -> Some entry.path
            | Unknown -> Some id
        )
        |> Set.ofList

    let toLoad =
        states
        |> List.choose (fun (_, state) ->
            match state with
            | Present entry when entry.isDirectory && not (isLoaded snapshot entry.path) -> Some entry.path
            | _ -> None
        )
        |> List.distinct
        |> List.sortBy (fun id -> (PathHelpers.normalizePath id).Split('/').Length, id)

    { Expanded = expanded; ToLoad = toLoad }

/// The directories above a path, from the root's child down to the parent of the path.
let ancestorPaths (path: string) : string list =
    let segments = (PathHelpers.normalizePath path).Split('/')

    [ 1 .. segments.Length - 1 ]
    |> List.map (fun count -> String.concat "/" segments.[.. count - 1])

type FileTreeExpansionApi = {
    loadDirectory: string -> JS.Promise<Result<FileTreeDirectoryUpdateDto, exn>>
    revalidateDirectories: string[] -> JS.Promise<Result<unit, exn>>
    statFile: string -> JS.Promise<Result<FileStatDto option, exn>>
}

type FileTreeExpansionController = {
    expandedIds: Set<string>
    /// Folders that are loading or failed, by their path key. A folder that is not listed has no pending state.
    loadStates: Map<PathKey, FolderLoadState>
    setExpandedIds: Set<string> -> unit
    /// The user expands a folder. Loads it when it is not loaded and clears an earlier failure.
    expandDirectory: string -> unit
    /// Loads a folder again, also when it is loaded already.
    refreshDirectory: string -> unit
    /// The stat of the selected file as of the last refresh, focus or change push.
    contentStamp: FileContentStamp option
    /// Stats the selected file again, for example after a window focus.
    refreshContentStamp: unit -> unit
    /// Records the stat of the selected file after the app wrote it. The stamp is a new baseline, so the preview
    /// does not reload for it.
    recordOwnWrite: unit -> unit
    /// Opens the folders down to a created or imported path once the tree contains it, without selecting it.
    /// A folder target opens too.
    revealPath: string -> unit
}

let FileTreeExpansionCtx =
    React.createContext<FileTreeExpansionController> (
        {
            expandedIds = Set.empty
            loadStates = Map.empty
            setExpandedIds = ignore
            expandDirectory = ignore
            refreshDirectory = ignore
            contentStamp = None
            refreshContentStamp = ignore
            recordOwnWrite = ignore
            revealPath = ignore
        }
    )

[<Hook>]
let useFileTreeExpansionCtx () = React.useContext FileTreeExpansionCtx

let private debounceMs = 300
let private minRevalidationGapMs = 2000.0
let private recentDirectoryLimit = 64

/// The directories a window focus revalidates: the root, every loaded zone folder and entity folder, then the
/// most recently loaded other directories up to a limit. The structure always comes first, because it holds the
/// entities that outside changes touch most. Each directory appears once.
let focusRevalidationDirectories (snapshot: FileTreeSnapshot) (recentlyLoaded: string list) : string[] =
    let zoneKeys =
        ArcEntityPathRules.allAddZones
        |> List.map (ArcEntityPathRules.zoneFolderName >> PathKey.ofPath snapshot.CaseInsensitive)
        |> Set.ofList

    let loadedPaths =
        snapshot.Loaded
        |> Seq.choose (fun key -> snapshot.Entries |> Map.tryFind key)
        |> Seq.map _.path
        |> Seq.filter (fun path -> path <> "")
        |> Seq.toList

    let isStructural (path: string) =
        let segments = path.Split('/')

        (segments.Length = 1 || segments.Length = 2)
        && zoneKeys.Contains(PathKey.ofPath snapshot.CaseInsensitive segments.[0])

    let structural =
        loadedPaths
        |> List.filter isStructural
        |> List.sortBy (fun path -> path.Split('/').Length, path)

    let others =
        (recentlyLoaded |> List.filter (isLoaded snapshot)) @ loadedPaths
        |> List.filter (isStructural >> not)
        |> List.distinct
        |> List.truncate recentDirectoryLimit

    ("" :: structural @ others) |> List.distinct |> List.toArray

/// A path to open in the tree. Selection targets and explicit targets (a created or imported item) share the
/// type. The id tells two requests for the same path apart.
type private RevealTarget = {
    Id: int
    Path: string
    IsExplicit: bool
}

[<ReactComponent>]
let FileTreeExpansionCtxProvider (api: FileTreeExpansionApi, children: ReactElement) =
    let arcRootPath = Renderer.Context.AppStateContext.useAppStateCtx ()
    let fileCtx = useFileStateCtx ()
    let store = fileCtx.fileTreeStore

    let selectionPath =
        fileCtx.state.Selection.TreePath |> Option.map PathHelpers.normalizePath

    let scope = arcRootPath |> Option.map PathHelpers.normalizePath

    let expandedIds, setExpandedIds = React.useStateWithUpdater<Set<string>> Set.empty

    let loadStates, setLoadStates =
        React.useStateWithUpdater<Map<PathKey, FolderLoadState>> Map.empty

    let contentStamp, setContentStamp = React.useState (None: FileContentStamp option)

    // The handlers below run outside renders and read the latest values through refs.
    let expandedRef = React.useRef expandedIds
    expandedRef.current <- expandedIds
    let selectionRef = React.useRef selectionPath
    selectionRef.current <- selectionPath
    let scopeRef = React.useRef scope
    scopeRef.current <- scope
    let apiRef = React.useRef api
    apiRef.current <- api
    let inFlight = React.useRef (Map.empty: Map<PathKey, JS.Promise<unit>>)
    // Folders whose last load failed. The automatic loader skips them until the user expands them again.
    let failed = React.useRef (Set.empty: Set<PathKey>)
    // Folders whose answer did not make them loaded, for example while a pull holds the updates back. The next
    // store change tries them again.
    let pending = React.useRef (Set.empty: Set<PathKey>)
    let isRevalidating = React.useRef false
    // The selected, created or imported path whose folders are not all loaded and expanded yet.
    // A store change tries it again.
    let revealTarget = React.useRef (None: RevealTarget option)
    let nextRevealId = React.useRef 0
    let isRevealing = React.useRef false
    // A reveal was requested while a pass was running, so the pass runs again when it ends.
    let rerunRequested = React.useRef false
    // Paths with an own-write stat under way. A stat that lands meanwhile may show the file before the write.
    let ownWriteInFlight = React.useRef (Set.empty: Set<string>)
    let autoLoading = React.useRef false
    let recentLoads = React.useRef ([]: string list)
    let lastRevalidatedAt = React.useRef -1.0e12
    let previousScope = React.useRef scope
    // Grows with every root change, so a load that started for the old root does not apply to the new one.
    let scopeToken = React.useRef 0
    let isMounted = React.useRef true

    let keyOf (path: string) =
        PathKey.ofPath store.Snapshot.CaseInsensitive path

    let newTarget (path: string) (isExplicit: bool) =
        nextRevealId.current <- nextRevealId.current + 1

        {
            Id = nextRevealId.current
            Path = path
            IsExplicit = isExplicit
        }

    let markFailed (path: string) (message: string) (collapse: bool) =
        failed.current <- failed.current.Add(keyOf path)
        setLoadStates (fun current -> current.Add(keyOf path, Failed message))

        if collapse then
            setExpandedIds (fun current -> current.Remove path)

        Browser.Dom.console.error ($"Failed to load directory '{path}'.", message)

    /// Loads one directory and applies the answer to the store. A directory that is already loading is skipped.
    let loadDirectory (path: string) (collapseOnFailure: bool) : JS.Promise<unit> =
        let key = keyOf path
        let token = scopeToken.current

        match inFlight.current |> Map.tryFind key with
        | Some running -> running
        | None ->
            setLoadStates (fun current -> current.Add(key, Loading))

            let running = promise {
                try
                    match! apiRef.current.loadDirectory path with
                    | _ when token <> scopeToken.current -> ()
                    | Ok update ->
                        store.ApplyDirectoryUpdate update
                        setLoadStates (fun current -> current.Remove key)

                        if isLoaded store.Snapshot path then
                            failed.current <- failed.current.Remove key
                            recentLoads.current <- path :: (recentLoads.current |> List.filter ((<>) path))
                        else
                            // The store holds the update back during a pull, or dropped it as older. Neither is
                            // a failure of the folder, so the next store change tries it again.
                            pending.current <- pending.current.Add key
                    | Error error -> markFailed path error.Message collapseOnFailure
                with error ->
                    if token = scopeToken.current then
                        markFailed path error.Message collapseOnFailure

                if token = scopeToken.current then
                    inFlight.current <- inFlight.current.Remove key
            }

            inFlight.current <- inFlight.current.Add(key, running)
            running

    /// Drops expanded ids that are gone from the tree and loads the next expanded directory that is in the tree
    /// but not loaded, one at a time. A directory with a failure is left alone until the user expands it again.
    let rec evaluate () =
        let snapshot = store.Snapshot
        let plan = planExpansion snapshot expandedRef.current

        if plan.Expanded <> expandedRef.current then
            setExpandedIds (fun current -> (planExpansion store.Snapshot current).Expanded)

        if not autoLoading.current then
            let next =
                plan.ToLoad
                |> List.tryFind (fun path ->
                    let key = keyOf path

                    not (inFlight.current.ContainsKey key)
                    && not (failed.current.Contains key)
                    && not (pending.current.Contains key)
                )

            match next with
            | Some path ->
                autoLoading.current <- true

                loadDirectory path true
                |> Promise.map (fun () ->
                    autoLoading.current <- false

                    if isMounted.current then
                        evaluate ()
                )
                |> Promise.catch ignore
                |> Promise.start
            | None -> ()

    /// Loads the folders above the reveal target one after another and expands them. A folder whose answer is held
    /// back (a pull is running) leaves the target in place, and the next store change runs this again. An explicit
    /// target (a created or imported item) also waits until the tree contains it, and expands nothing before that.
    let rec reveal () =
        match revealTarget.current with
        | Some r when not isRevealing.current ->
            isRevealing.current <- true
            rerunRequested.current <- false

            promise {
                try
                    let path = r.Path
                    let ancestors = ancestorPaths path

                    for ancestor in ancestors do
                        match knows store.Snapshot ancestor with
                        | Present entry when
                            entry.isDirectory
                            && not (isLoaded store.Snapshot ancestor)
                            && not (r.IsExplicit && failed.current.Contains(keyOf ancestor))
                            && not (pending.current.Contains(keyOf ancestor))
                            ->
                            do! loadDirectory ancestor false
                        | _ -> ()

                    let states =
                        ancestors |> List.map (fun ancestor -> ancestor, knows store.Snapshot ancestor)

                    let present =
                        states
                        |> List.choose (fun (_, state) ->
                            match state with
                            | Present entry when entry.isDirectory -> Some entry.path
                            | _ -> None
                        )
                        |> Set.ofList

                    if not r.IsExplicit then
                        if isMounted.current && not present.IsEmpty && selectionRef.current = Some path then
                            setExpandedIds (fun current ->
                                if Set.isSubset present current then
                                    current
                                else
                                    Set.union current present
                            )

                        // Done when every folder above the path is loaded, or when the path cannot be reached.
                        let reachable =
                            states
                            |> List.forall (fun (ancestor, state) ->
                                state <> Absent && not (failed.current.Contains(keyOf ancestor))
                            )

                        let complete =
                            states
                            |> List.forall (fun (_, state) ->
                                match state with
                                | Present entry -> entry.isDirectory && isLoaded store.Snapshot entry.path
                                | _ -> false
                            )

                        if complete || not reachable then
                            if revealTarget.current = Some r then
                                revealTarget.current <- None
                    else
                        let ancestorFailed =
                            ancestors
                            |> List.exists (fun ancestor -> failed.current.Contains(keyOf ancestor))

                        match knows store.Snapshot path with
                        | Present entry ->
                            let toOpen =
                                if entry.isDirectory then
                                    present.Add entry.path
                                else
                                    present

                            if isMounted.current && revealTarget.current = Some r then
                                setExpandedIds (fun current ->
                                    if Set.isSubset toOpen current then
                                        current
                                    else
                                        Set.union current toOpen
                                )

                                revealTarget.current <- None
                        | _ ->
                            // The create call can return before the tree push that contains the item, so an
                            // absent target waits for the next store change.
                            if ancestorFailed && revealTarget.current = Some r then
                                revealTarget.current <- None
                finally
                    isRevealing.current <- false

                // A request that came in while this pass waited for a folder has its own pass now.
                if
                    isMounted.current
                    && revealTarget.current.IsSome
                    && (rerunRequested.current || revealTarget.current <> Some r)
                then
                    reveal ()
            }
            |> Promise.catch ignore
            |> Promise.start
        | Some _ -> rerunRequested.current <- true
        | None -> ()

    let evaluateRef = React.useRef evaluate
    evaluateRef.current <- evaluate
    let revealRef = React.useRef reveal
    revealRef.current <- reveal

    React.useEffect (
        (fun () ->
            isMounted.current <- true

            let unsubscribe =
                store.Subscribe(fun () ->
                    pending.current <- Set.empty
                    evaluateRef.current ()
                    revealRef.current ()
                )

            { new System.IDisposable with
                member _.Dispose() =
                    isMounted.current <- false
                    unsubscribe ()
            }
        ),
        [| box store |]
    )

    // The expanded ids are ARC-relative and stay valid across a rename. The next snapshot decides which of them
    // still exist, and the loader loads the ones that are not loaded. Load bookkeeping is per root.
    React.useEffect (
        (fun () ->
            if previousScope.current <> scope then
                previousScope.current <- scope
                scopeToken.current <- scopeToken.current + 1
                inFlight.current <- Map.empty
                autoLoading.current <- false
                setLoadStates (fun _ -> Map.empty)
                recentLoads.current <- []
                failed.current <- Set.empty
                pending.current <- Set.empty

                match revealTarget.current with
                | Some r when r.IsExplicit -> revealTarget.current <- None
                | _ -> ()

                // A rename keeps the expanded folders. Another ARC and a closed one start collapsed.
                if scope.IsNone || not fileCtx.rootChangeIsRename then
                    setExpandedIds (fun _ -> Set.empty)

                evaluate ()
        ),
        [| box scope |]
    )

    React.useEffect ((fun () -> evaluate ()), [| box expandedIds |])

    let statSelection =
        React.useCallback (
            (fun (ownWrite: bool) ->
                match selectionRef.current with
                | None -> setContentStamp None
                | Some path ->
                    if ownWrite then
                        ownWriteInFlight.current <- ownWriteInFlight.current.Add path

                    promise {
                        try
                            match! apiRef.current.statFile path with
                            | Ok _ when not ownWrite && ownWriteInFlight.current.Contains path -> ()
                            | Ok(Some stat) when isMounted.current && selectionRef.current = Some path ->
                                setContentStamp (
                                    Some {
                                        Path = path
                                        SizeBytes = stat.sizeBytes
                                        MtimeMs = stat.mtimeMs
                                        OwnWrite = ownWrite
                                    }
                                )
                            | Ok None when isMounted.current && selectionRef.current = Some path ->
                                setContentStamp None
                            | _ -> ()
                        finally
                            if ownWrite then
                                ownWriteInFlight.current <- ownWriteInFlight.current.Remove path
                    }
                    |> Promise.catch ignore
                    |> Promise.start
            ),
            [||]
        )

    let refreshContentStamp =
        React.useCallback ((fun () -> statSelection false), [| box statSelection |])

    let recordOwnWrite =
        React.useCallback ((fun () -> statSelection true), [| box statSelection |])

    // The first stamp of a selection is the baseline for later comparisons.
    React.useEffect ((fun () -> refreshContentStamp ()), [| box selectionPath |])

    React.useEffect (
        (fun () ->
            fileCtx.subscribeFileContentChanged (fun stamp ->
                if
                    selectionRef.current = Some(PathHelpers.normalizePath stamp.Path)
                    && not (ownWriteInFlight.current.Contains(PathHelpers.normalizePath stamp.Path))
                then
                    setContentStamp (Some stamp)
            )
            |> fun unsubscribe ->
                { new System.IDisposable with
                    member _.Dispose() = unsubscribe ()
                }
        ),
        [| box fileCtx.subscribeFileContentChanged |]
    )

    let expandDirectory (path: string) =
        failed.current <- failed.current.Remove(keyOf path)
        pending.current <- pending.current.Remove(keyOf path)

        setLoadStates (fun current ->
            match current |> Map.tryFind (keyOf path) with
            | Some(Failed _) -> current.Remove(keyOf path)
            | _ -> current
        )

        match knows store.Snapshot path with
        | Present entry when entry.isDirectory && not (isLoaded store.Snapshot path) ->
            loadDirectory path true |> Promise.start
        | _ -> ()

    let refreshDirectory (path: string) =
        loadDirectory path false |> Promise.start

    // Selecting a path, for example a note from the note search, loads the folders above it one after another and
    // expands them.
    React.useEffect (
        (fun () ->
            revealTarget.current <- selectionPath |> Option.map (fun p -> newTarget p false)
            revealRef.current ()
        ),
        [| box selectionPath |]
    )

    let revealPath =
        React.useCallback (
            (fun (path: string) ->
                if isMounted.current && scopeRef.current = scope then
                    revealTarget.current <- Some(newTarget (PathHelpers.normalizePath path) true)
                    revealRef.current ()
            ),
            [| box scope |]
        )

    // A window focus revalidates the root and the most recently loaded folders, and stats the selection.
    React.useEffect (
        (fun () ->
            let timeout = ref (None: float option)

            let revalidate () =
                let now = JS.Constructors.Date.now ()

                // A slow revalidation blocks the next one too, because it has not finished yet.
                if
                    not isRevalidating.current
                    && now - lastRevalidatedAt.current >= minRevalidationGapMs
                then
                    isRevalidating.current <- true
                    let snapshot = store.Snapshot

                    let directories = focusRevalidationDirectories snapshot recentLoads.current

                    promise {
                        try
                            let! _ = apiRef.current.revalidateDirectories directories
                            ()
                        finally
                            isRevalidating.current <- false
                            lastRevalidatedAt.current <- JS.Constructors.Date.now ()

                        refreshContentStamp ()
                    }
                    |> Promise.catch ignore
                    |> Promise.start

            let onFocus (_: Browser.Types.Event) =
                timeout.Value |> Option.iter Browser.Dom.window.clearTimeout
                timeout.Value <- Some(Browser.Dom.window.setTimeout ((fun () -> revalidate ()), debounceMs) |> float)

            Browser.Dom.window.addEventListener ("focus", onFocus)

            { new System.IDisposable with
                member _.Dispose() =
                    Browser.Dom.window.removeEventListener ("focus", onFocus)
                    timeout.Value |> Option.iter Browser.Dom.window.clearTimeout
            }
        ),
        [| box store; box arcRootPath |]
    )

    let controller =
        React.useMemo (
            (fun () -> {
                expandedIds = expandedIds
                loadStates = loadStates
                setExpandedIds = fun ids -> setExpandedIds (fun _ -> ids)
                expandDirectory = expandDirectory
                refreshDirectory = refreshDirectory
                contentStamp = contentStamp
                refreshContentStamp = refreshContentStamp
                recordOwnWrite = recordOwnWrite
                revealPath = revealPath
            }),
            [|
                box expandedIds
                box loadStates
                box contentStamp
                box refreshContentStamp
                box recordOwnWrite
                box revealPath
            |]
        )

    FileTreeExpansionCtx.Provider(controller, children)
