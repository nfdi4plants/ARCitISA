/// Which folders of the file tree are expanded, loading or failed, plus the focus revalidation of the loaded
/// folders and the content stamp of the selected file. The state lives above the sidebar panels, so a panel switch
/// that remounts the file tree keeps it. It resets when the ARC root changes.
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

/// What an expanded set needs from a snapshot. Expanded keeps the ids that are not known to be gone. ToLoad lists
/// the expanded directories that are in the tree but not loaded, parents first.
type ExpansionPlan = {
    Expanded: Set<string>
    ToLoad: string list
}

let planExpansion (snapshot: FileTreeSnapshot) (expandedIds: Set<string>) : ExpansionPlan =
    let states = expandedIds |> Seq.map (fun id -> id, knows snapshot id) |> Seq.toList

    let expanded =
        states
        |> List.filter (fun (_, state) -> state <> Absent)
        |> List.map fst
        |> Set.ofList

    let toLoad =
        states
        |> List.choose (fun (id, state) ->
            match state with
            | Present entry when entry.isDirectory && not (isLoaded snapshot id) -> Some id
            | _ -> None
        )
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
    refreshContentStamp: unit -> unit
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
        }
    )

[<Hook>]
let useFileTreeExpansionCtx () = React.useContext FileTreeExpansionCtx

let private debounceMs = 300
let private minRevalidationGapMs = 2000.0
let private recentDirectoryLimit = 64

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
    let apiRef = React.useRef api
    apiRef.current <- api
    let inFlight = React.useRef (Map.empty: Map<PathKey, JS.Promise<unit>>)
    // Folders whose last load failed. The automatic loader skips them until the user expands them again.
    let failed = React.useRef (Set.empty: Set<PathKey>)
    let autoLoading = React.useRef false
    let recentLoads = React.useRef ([]: string list)
    let lastRevalidatedAt = React.useRef -1.0e12
    let previousScope = React.useRef scope
    // Grows with every root change, so a load that started for the old root does not apply to the new one.
    let scopeToken = React.useRef 0
    let isMounted = React.useRef true

    let keyOf (path: string) =
        PathKey.ofPath store.Snapshot.CaseInsensitive path

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

                        // An update that the store drops as older leaves the directory unloaded.
                        if isLoaded store.Snapshot path then
                            failed.current <- failed.current.Remove(keyOf path)
                            recentLoads.current <- path :: (recentLoads.current |> List.filter ((<>) path))
                            setLoadStates (fun current -> current.Remove key)
                        else
                            markFailed path "The folder could not be loaded." collapseOnFailure
                    | Error error -> markFailed path error.Message collapseOnFailure
                with error when token = scopeToken.current ->
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
            setExpandedIds (fun current -> Set.intersect current plan.Expanded)

        if not autoLoading.current then
            let next =
                plan.ToLoad
                |> List.tryFind (fun path ->
                    let key = keyOf path
                    not (inFlight.current.ContainsKey key) && not (failed.current.Contains key)
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
                |> Promise.start
            | None -> ()

    let evaluateRef = React.useRef evaluate
    evaluateRef.current <- evaluate

    React.useEffect (
        (fun () ->
            isMounted.current <- true

            let unsubscribe = store.Subscribe(fun () -> evaluateRef.current ())

            { new System.IDisposable with
                member _.Dispose() =
                    isMounted.current <- false
                    unsubscribe ()
            }
        ),
        [| box store |]
    )

    // The expanded ids are ARC-relative and stay valid for a renamed or another root. The next snapshot decides
    // which of them still exist, and the loader loads the ones that are not loaded. Load bookkeeping is per root.
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
                evaluate ()
        ),
        [| box scope |]
    )

    React.useEffect ((fun () -> evaluate ()), [| box expandedIds |])

    let refreshContentStamp =
        React.useCallback (
            (fun () ->
                match selectionRef.current with
                | None -> setContentStamp None
                | Some path ->
                    promise {
                        match! apiRef.current.statFile path with
                        | Ok(Some stat) when isMounted.current && selectionRef.current = Some path ->
                            setContentStamp (
                                Some {
                                    Path = path
                                    SizeBytes = stat.sizeBytes
                                    MtimeMs = stat.mtimeMs
                                }
                            )
                        | Ok None when isMounted.current -> setContentStamp None
                        | _ -> ()
                    }
                    |> Promise.catch ignore
                    |> Promise.start
            ),
            [||]
        )

    // The first stamp of a selection is the baseline for later comparisons.
    React.useEffect ((fun () -> refreshContentStamp ()), [| box selectionPath |])

    React.useEffect (
        (fun () ->
            fileCtx.subscribeFileContentChanged (fun stamp ->
                if selectionRef.current = Some(PathHelpers.normalizePath stamp.Path) then
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
            match selectionPath with
            | None -> ()
            | Some path ->
                promise {
                    let ancestors = ancestorPaths path

                    for ancestor in ancestors do
                        match knows store.Snapshot ancestor with
                        | Present entry when entry.isDirectory && not (isLoaded store.Snapshot ancestor) ->
                            do! loadDirectory ancestor false
                        | _ -> ()

                    let present =
                        ancestors
                        |> List.filter (fun ancestor ->
                            match knows store.Snapshot ancestor with
                            | Present entry -> entry.isDirectory
                            | _ -> false
                        )
                        |> Set.ofList

                    if isMounted.current && not present.IsEmpty && selectionRef.current = Some path then
                        setExpandedIds (fun current ->
                            if Set.isSubset present current then
                                current
                            else
                                Set.union current present
                        )
                }
                |> Promise.start
        ),
        [| box selectionPath |]
    )

    // A window focus revalidates the root and the most recently loaded folders, and stats the selection.
    React.useEffect (
        (fun () ->
            let timeout = ref (None: float option)

            let revalidate () =
                let now = JS.Constructors.Date.now ()

                if now - lastRevalidatedAt.current >= minRevalidationGapMs then
                    let snapshot = store.Snapshot

                    let recent = recentLoads.current |> List.filter (isLoaded snapshot)

                    let others =
                        snapshot.Loaded
                        |> Seq.choose (fun key -> snapshot.Entries |> Map.tryFind key)
                        |> Seq.map _.path
                        |> Seq.filter (fun path -> path <> "" && not (List.contains path recent))
                        |> Seq.toList

                    let directories =
                        "" :: (recent @ others |> List.truncate recentDirectoryLimit) |> List.toArray

                    promise {
                        try
                            let! _ = apiRef.current.revalidateDirectories directories
                            ()
                        finally
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
            }),
            [|
                box expandedIds
                box loadStates
                box contentStamp
                box refreshContentStamp
            |]
        )

    FileTreeExpansionCtx.Provider(controller, children)
