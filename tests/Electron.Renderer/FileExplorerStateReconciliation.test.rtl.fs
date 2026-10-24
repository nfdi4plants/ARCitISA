module ElectronRenderer.FileExplorerStateReconciliationRtlTests

open Fable.Core
open Feliz
open Renderer.Components.Helper
open Renderer.Context.FileStateContext
open Renderer.Context.FileTreeStore
open Renderer.Types
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

type private HarnessProps = {
    Lookup: string -> ObjectStateDto option
    Selection: string
    Stamp: FileContentStamp option
}

let private selectedPath = "data/note.md"

let private lfsState (isMaterialized: bool) : ObjectStateDto = {
    Path = selectedPath
    IsMaterialized = isMaterialized
    IsLocallyAvailable = isMaterialized
    SizeBytes = Some 10.0
    ObjectId = None
}

let private stamp (path: string) (mtimeMs: float) : FileContentStamp option =
    Some {
        Path = path
        SizeBytes = 10.0
        MtimeMs = mtimeMs
        OwnWrite = false
    }

/// A store with the root, a data folder that holds the given files and an unrelated folder.
let private storeWith (files: string list) =
    let store = FileTreeStore()
    store.BeginLoad()

    store.ApplySnapshot {|
        treeVersion = 1
        caseInsensitive = false
        entries =
            Array.ofList (
                [
                    FileEntry.create ("arc", "", true)
                    FileEntry.create ("data", "data", true)
                    FileEntry.create ("other", "other", true)
                ]
                @ (files
                   |> List.map (fun path -> FileEntry.create (path.Substring(path.LastIndexOf '/' + 1), path, false)))
            )
        loadedDirectories = [| ""; "data"; "other" |]
        fileWatcher = {| state = "running"; reason = "" |}
    |}

    store

let private controllerFor (store: FileTreeStore) : FileStateController = {
    state = FileState.init ()
    fileTreeStore = store
    subscribeFileContentChanged = fun _ -> ignore
    rootChangeIsRename = false
    fileWatcher = runningFileWatcher
    setSelection = ignore
    updateSelection = ignore
    activeFileImport = None
    isCancellingFileImport = false
    importExternalFiles = fun _ -> JS.Constructors.Promise.resolve (Ok())
    cancelFileImport = fun () -> JS.Constructors.Promise.resolve (Ok())
}

[<ReactComponent>]
let private ProbeHost (props: HarnessProps, reloads: ResizeArray<string>, clears: int ref) =
    FileExplorerStateReconciliation.useReconcileSelectedPreview
        props.Lookup
        (Some props.Selection)
        (Some PageState.UnknownPage)
        props.Stamp
        (fun path _ -> reloads.Add path)
        (fun () -> clears.Value <- clears.Value + 1)

    Html.none

[<ReactComponent>]
let private Harness
    (
        store: FileTreeStore,
        initial: HarnessProps,
        reloads: ResizeArray<string>,
        clears: int ref,
        expose: (HarnessProps -> unit) -> unit
    ) =
    let props, setProps = React.useState initial
    expose setProps

    FileStateCtx.Provider(controllerFor store, ProbeHost(props, reloads, clears))

let private render store props reloads clears =
    let setProps = ref (fun (_: HarnessProps) -> ())

    RTL.render (Harness(store, props, reloads, clears, (fun setter -> setProps.Value <- setter)))
    |> ignore

    fun next -> RTL.act (fun () -> promise { setProps.Value next })

let private noLfs (_: string) : ObjectStateDto option = None

Vitest.test (
    "the selected preview reloads only for changes of the selected file, its LFS state or its content stamp",
    fun () -> promise {
        let store = storeWith [ selectedPath; "other/x.txt" ]
        let reloads = ResizeArray<string>()
        let clears = ref 0

        let initial = {
            Lookup = noLfs
            Selection = selectedPath
            Stamp = stamp selectedPath 1.0
        }

        let update = render store initial reloads clears

        // A new lookup that answers the same for the selected file, like the one after an unrelated push.
        do! update { initial with Lookup = fun _ -> None }
        Vitest.expect(reloads.Count).toBe 0

        // A directory update for an unrelated folder keeps the selected entry instance.
        do!
            RTL.act (fun () -> promise {
                store.ApplyDirectoryUpdate {|
                    treeVersion = 2
                    directories = [|
                        {|
                            directoryPath = "other"
                            children = [| FileEntry.create ("y.txt", "other/y.txt", false) |]
                        |}
                    |]
                |}
            })

        Vitest.expect(reloads.Count).toBe 0
        Vitest.expect(clears.Value).toBe 0

        // A changed content stamp of the selected file reloads it once.
        do!
            update {
                initial with
                    Stamp = stamp selectedPath 2.0
            }

        Vitest.expect(reloads.ToArray()).toEqual [| selectedPath |]

        // The same stamp again does nothing.
        do!
            update {
                initial with
                    Stamp = stamp selectedPath 2.0
                    Lookup = fun _ -> None
            }

        Vitest.expect(reloads.Count).toBe 1

        do!
            update {
                initial with
                    Stamp = stamp selectedPath 2.0
                    Lookup = fun path -> if path = selectedPath then Some(lfsState false) else None
            }

        Vitest.expect(clears.Value).toBe 1
        Vitest.expect(reloads.Count).toBe 1

        do!
            update {
                initial with
                    Stamp = stamp selectedPath 2.0
                    Lookup = fun path -> if path = selectedPath then Some(lfsState true) else None
            }

        Vitest.expect(reloads.Count).toBe 2

        // Selecting another file changes the key, but the click opens that file itself.
        do!
            update {
                initial with
                    Selection = "data/other.bin"
                    Stamp = None
            }

        Vitest.expect(reloads.Count).toBe 2
        Vitest.expect(clears.Value).toBe 1
    }
)

Vitest.test (
    "a stamp that the app recorded after its own write is a baseline and an outside change after it reloads",
    fun () -> promise {
        let store = storeWith [ selectedPath ]
        let reloads = ResizeArray<string>()
        let clears = ref 0

        let initial = {
            Lookup = noLfs
            Selection = selectedPath
            Stamp = stamp selectedPath 1.0
        }

        let update = render store initial reloads clears

        let ownWrite =
            stamp selectedPath 2.0
            |> Option.map (fun value -> { value with OwnWrite = true })

        do! update { initial with Stamp = ownWrite }
        Vitest.expect(reloads.Count).toBe 0

        // The next stat finds the same file, so it is no change.
        do!
            update {
                initial with
                    Stamp = stamp selectedPath 2.0
            }

        Vitest.expect(reloads.Count).toBe 0

        do!
            update {
                initial with
                    Stamp = stamp selectedPath 3.0
            }

        Vitest.expect(reloads.ToArray()).toEqual [| selectedPath |]
    }
)

Vitest.test (
    "a download of the second selected pointer reloads that file once",
    fun () -> promise {
        let first = "data/p1.bin"
        let second = "data/p2.bin"
        let store = storeWith [ first; second ]

        let pointerLookup (_: string) : ObjectStateDto option = Some { lfsState false with Path = "x" }

        let materializedSecond (path: string) : ObjectStateDto option =
            if path = second then
                Some(lfsState true)
            else
                Some(lfsState false)

        let reloads = ResizeArray<string>()
        let clears = ref 0

        let initial = {
            Lookup = pointerLookup
            Selection = first
            Stamp = None
        }

        let update = render store initial reloads clears

        // Same key as the first pointer, so only the selection changes.
        do! update { initial with Selection = second }
        Vitest.expect(reloads.Count).toBe 0

        do!
            update {
                Lookup = materializedSecond
                Selection = second
                Stamp = None
            }

        Vitest.expect(reloads.ToArray()).toEqual [| second |]
    }
)
