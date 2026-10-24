module Renderer.Context.FileStateContext

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Renderer
open Renderer.Context.FileTreeStore

type FileState = {
    /// The loaded part of the file tree as a flat list. Directories that are not loaded have no children here.
    FileTree: FileEntry[]
    /// The snapshot FileTree was built from, for questions about what is loaded and what is known.
    TreeSnapshot: FileTreeSnapshot
    Selection: ArcSelection
} with

    static member init() : FileState = {
        FileTree = [||]
        TreeSnapshot = FileTreeStore().Snapshot
        Selection = ArcSelection.empty
    }

type FileStateController = {
    state: FileState
    fileTreeStore: FileTreeStore
    fileTreeIsLoading: bool
    refreshFileTree: unit -> unit
    setSelection: ArcSelection -> unit
    updateSelection: (ArcSelection -> ArcSelection) -> unit
    activeFileImport: ActiveFileImportState option
    isCancellingFileImport: bool
    importExternalFiles: string -> JS.Promise<Result<unit, exn>>
    cancelFileImport: unit -> JS.Promise<Result<unit, exn>>
}

let FileStateCtx =
    React.createContext<FileStateController> (
        {
            state = FileState.init ()
            fileTreeStore = FileTreeStore()
            fileTreeIsLoading = true
            refreshFileTree = ignore
            setSelection = ignore
            updateSelection = ignore
            activeFileImport = None
            isCancellingFileImport = false
            importExternalFiles = fun _ -> JS.Constructors.Promise.resolve (Ok())
            cancelFileImport = fun () -> JS.Constructors.Promise.resolve (Ok())
        }
    )

[<Hook>]
let useFileStateCtx () = React.useContext FileStateCtx

type FileTreeSnapshotLoader = unit -> JS.Promise<Result<FileTreeSnapshotDto, exn>>
type ActiveFileImportLoader = unit -> JS.Promise<Result<ActiveFileImportState option, exn>>

/// The ARC the file tree belongs to. changeVersion grows with every pathChange, so a pull also happens when the
/// path stays the same, for example when the same ARC is opened again.
type FileTreeSource = {
    arcRootPath: string option
    caseInsensitive: bool
    changeVersion: int
}

type FileImportApi = {
    loadActiveImport: ActiveFileImportLoader
    pickAbsolutePaths: unit -> JS.Promise<Result<string option, exn>>
    runImport: ImportExternalFilesRequest -> JS.Promise<Result<ImportExternalFilesResult, exn>>
    cancelImport: string -> JS.Promise<Result<unit, exn>>
}

[<ReactComponent>]
let FileStateCtxProviderWithSnapshots
    (
        source: FileTreeSource,
        loadFileTreeSnapshot: FileTreeSnapshotLoader,
        fileImportApi: FileImportApi,
        children: ReactElement
    ) =
    let selection, setSelectionState = React.useStateWithUpdater ArcSelection.empty
    let isFilePickerOpenRef = React.useRef false
    let isCancellingFileImport, setIsCancellingFileImport = React.useState false
    let store, _ = React.useState (fun () -> FileTreeStore())
    let fileTreeIsLoading, setFileTreeIsLoading = React.useState true
    // Counts the pulls. A result that belongs to an older pull or an older root is dropped.
    let requestCounter = React.useRef 0
    let previousRootRef = React.useRef (None: string option)
    // The pull reads the loader through a ref, so it stays stable and still uses the loader of the latest commit.
    let loaderRef = React.useRef loadFileTreeSnapshot
    React.useLayoutEffect (fun () -> loaderRef.current <- loadFileTreeSnapshot)

    // The subscription is created before the pull effect runs, so updates that arrive during the pull reach the
    // store, which buffers them until the snapshot is applied.
    React.useEffect (
        (fun () ->
            let dispose =
                Renderer.IpcReceiver.subscribeProxyReceiver<IFileTreeRendererApi> {
                    fileTreeDirectoryUpdate = store.ApplyDirectoryUpdate
                    fileContentChanged = ignore
                    largeObjectIndexUpdate = Renderer.Context.LargeObjectStore.forwardToActiveStore
                }

            { new System.IDisposable with
                member _.Dispose() = dispose ()
            }
        ),
        [| box store |]
    )

    let pull =
        React.useCallback (
            (fun (hasRoot: bool) ->
                requestCounter.current <- requestCounter.current + 1
                let requestId = requestCounter.current

                if not hasRoot then
                    store.Reset()
                    setFileTreeIsLoading false
                else
                    store.BeginLoad()
                    setFileTreeIsLoading true

                    let isCurrent () = requestCounter.current = requestId

                    let fail (message: string) =
                        if isCurrent () then
                            store.FailLoad()
                            setFileTreeIsLoading false
                            console.error ("Failed to load file tree snapshot.", message)

                    promise {
                        match! loaderRef.current () with
                        | Ok snapshot ->
                            if isCurrent () then
                                store.ApplySnapshot snapshot
                                setFileTreeIsLoading false
                        | Error error -> fail error.Message
                    }
                    |> Promise.catch (fun error -> fail error.Message)
                    |> Promise.start
            ),
            [| box store |]
        )

    React.useEffect (
        (fun () ->
            // Data of another root must not stay visible when the pull for the new root fails.
            if previousRootRef.current <> source.arcRootPath then
                store.Reset()

            previousRootRef.current <- source.arcRootPath
            pull source.arcRootPath.IsSome

            { new System.IDisposable with
                member _.Dispose() =
                    requestCounter.current <- requestCounter.current + 1
            }
        ),
        [|
            box source.arcRootPath
            box source.changeVersion
            box pull
        |]
    )

    // A stable subscribe function, so React does not resubscribe on every render.
    let subscribe = React.useMemo ((fun () -> store.Subscribe), [| box store |])
    let snapshot = React.useSyncExternalStore (subscribe, (fun () -> store.Snapshot))

    // The flat list for consumers that have not moved to the store yet. Map values come in path order, so a
    // parent always comes before its children.
    let fileTree =
        React.useMemo (
            (fun () -> snapshot.Entries |> Seq.map (fun entry -> entry.Value) |> Seq.toArray),
            [| box snapshot |]
        )

    let fileState =
        React.useMemo (
            (fun _ -> {
                FileTree = fileTree
                TreeSnapshot = snapshot
                Selection = selection
            }),
            [| box fileTree; box selection |]
        )

    let activeFileImport =
        Renderer.MainSyncedState.useMainSyncedState {
            initial = None
            load =
                fun () -> promise {
                    match! fileImportApi.loadActiveImport () with
                    | Ok state -> return state
                    | Error ex -> return raise ex
                }
            subscribe =
                fun setActiveImport ->
                    Renderer.IpcReceiver.subscribeProxyReceiver<IFileImportRendererApi> {
                        fileImportStateUpdate =
                            fun state ->
                                if
                                    state.IsNone
                                    || state |> Option.exists (fun value -> value.phase = FileImportPhase.Finalizing)
                                then
                                    setIsCancellingFileImport false

                                setActiveImport state
                    }
            onError = fun ex -> console.error ("Failed to load active file import state.", ex.Message)
            dependencies = [||]
        }

    let importExternalFiles targetRelativePath = promise {
        if activeFileImport.state.IsSome || isFilePickerOpenRef.current then
            return Ok()
        else
            isFilePickerOpenRef.current <- true

            try
                match! fileImportApi.pickAbsolutePaths () with
                | Error ex -> return Error ex
                | Ok None -> return Ok()
                | Ok(Some authorizationId) ->
                    let requestId = System.Guid.NewGuid().ToString()

                    match!
                        fileImportApi.runImport {
                            requestId = requestId
                            targetRelativePath = targetRelativePath
                            authorizationId = authorizationId
                        }
                    with
                    | Error ex -> return Error ex
                    | Ok ImportExternalFilesResult.Completed
                    | Ok ImportExternalFilesResult.Cancelled -> return Ok()
            finally
                isFilePickerOpenRef.current <- false
    }

    let cancelFileImport () = promise {
        match activeFileImport.state with
        | None
        | Some { phase = FileImportPhase.Finalizing } -> return Ok()
        | Some activeImport ->
            setIsCancellingFileImport true

            match! fileImportApi.cancelImport activeImport.requestId with
            | Ok() -> return Ok()
            | Error ex ->
                setIsCancellingFileImport false
                return Error ex
    }

    let fileStateCtx: FileStateController =
        React.useMemo (
            (fun _ -> {
                state = fileState
                fileTreeStore = store
                fileTreeIsLoading = fileTreeIsLoading
                refreshFileTree = fun () -> pull source.arcRootPath.IsSome
                setSelection = fun selection -> setSelectionState (fun _ -> selection |> ArcSelection.normalize)
                updateSelection =
                    fun update ->
                        setSelectionState (fun currentSelection -> update currentSelection |> ArcSelection.normalize)
                activeFileImport = activeFileImport.state
                isCancellingFileImport = isCancellingFileImport
                importExternalFiles = importExternalFiles
                cancelFileImport = cancelFileImport
            }),
            [|
                box fileState
                box store
                box fileTreeIsLoading
                box source.arcRootPath
                box activeFileImport.state
                box isCancellingFileImport
            |]
        )

    FileStateCtx.Provider(fileStateCtx, children)
