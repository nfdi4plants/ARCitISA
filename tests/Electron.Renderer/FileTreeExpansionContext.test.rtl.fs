module ElectronRenderer.FileTreeExpansionContextRtlTests

open Fable.Core
open Feliz
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization
open Renderer.Types
open Renderer.Context.FileStateContext
open Renderer.Context.FileTreeExpansionContext
open Renderer.Context.FileTreeHooks
open Renderer.Context.FileTreeStore
open Swate.Components
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private dir (path: string) =
    FileEntry.create (path.Substring(path.LastIndexOf '/' + 1), path, true)

let private file (path: string) =
    FileEntry.create (path.Substring(path.LastIndexOf '/' + 1), path, false)

[<Emit("window.dispatchEvent(new Event('focus'))")>]
let private dispatchFocus () : unit = jsNative

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting."
    else
        do! Promise.sleep 5
        return! waitUntil (predicate, attempts - 1)
}

let private waitFor predicate = waitUntil (predicate, 200)

let private deferred<'T> () : JS.Promise<'T> * ('T -> unit) =
    let resolver = ref (fun (_: 'T) -> ())

    let promise =
        JS.Constructors.Promise.Create(fun resolve _ -> resolver.Value <- resolve)

    promise, (fun value -> resolver.Value value)

/// The root and its loaded children. data and notes are directories that are not loaded.
let private newStore () =
    let store = FileTreeStore()
    store.BeginLoad()

    store.ApplySnapshot {|
        treeVersion = 1
        caseInsensitive = false
        entries = [|
            FileEntry.create ("arc", "", true)
            dir "data"
            dir "notes"
            file "readme.md"
        |]
        loadedDirectories = [| "" |]
        fileWatcher = {|
            state = FileWatcherStatus.Running
            reason = ""
        |}
    |}

    store

let private update version directory (children: FileEntry list) : FileTreeDirectoryUpdateDto = {|
    treeVersion = version
    directories = [|
        {|
            directoryPath = directory
            children = Array.ofList children
        |}
    |]
|}

type private Calls = {
    Loads: ResizeArray<string>
    Revalidations: ResizeArray<string[]>
    Stats: ResizeArray<string>
}

let private newCalls () = {
    Loads = ResizeArray()
    Revalidations = ResizeArray()
    Stats = ResizeArray()
}

let private apiWith
    (calls: Calls)
    (load: string -> JS.Promise<Result<FileTreeDirectoryUpdateDto, exn>>)
    : FileTreeExpansionApi =
    {
        loadDirectory =
            fun path ->
                calls.Loads.Add path
                load path
        revalidateDirectories =
            fun paths ->
                calls.Revalidations.Add paths
                JS.Constructors.Promise.resolve (Ok())
        statFile =
            fun path ->
                calls.Stats.Add path
                JS.Constructors.Promise.resolve (Ok(Some {| sizeBytes = 5.0; mtimeMs = 7.0 |}))
    }

type private Env = {
    Store: FileTreeStore
    Listeners: ResizeArray<FileContentStamp -> unit>
    SetSelection: ArcSelection -> unit
    SetRoot: string -> bool -> unit
}

[<ReactComponent>]
let private Host
    (
        store: FileTreeStore,
        api: FileTreeExpansionApi,
        listeners: ResizeArray<FileContentStamp -> unit>,
        expose: (ArcSelection -> unit) -> (string -> bool -> unit) -> unit,
        children: ReactElement
    ) =
    let selection, setSelection = React.useState ArcSelection.empty
    let rootState, setRootState = React.useState (("arc", false))
    let root, isRename = rootState
    expose setSelection (fun nextRoot nextIsRename -> setRootState (nextRoot, nextIsRename))

    let subscribe =
        React.useCallback (
            (fun (listener: FileContentStamp -> unit) ->
                listeners.Add listener
                fun () -> listeners.Remove listener |> ignore
            ),
            [||]
        )

    let controller: FileStateController =
        React.useMemo (
            (fun () -> {
                state = { Selection = selection }
                fileTreeStore = store
                subscribeFileContentChanged = subscribe
                rootChangeIsRename = isRename
                fileWatcher = runningFileWatcher
                setSelection = setSelection
                updateSelection = ignore
                activeFileImport = None
                isCancellingFileImport = false
                importExternalPaths = fun _ _ -> JS.Constructors.Promise.resolve (Ok None)
                cancelFileImport = fun () -> JS.Constructors.Promise.resolve (Ok())
            }),
            [| box selection; box store; box isRename |]
        )

    Renderer.Context.AppStateContext.AppStateCtx.Provider(
        (if root = "" then None else Some root),
        FileStateCtx.Provider(controller, FileTreeExpansionCtxProvider(api, children))
    )

[<ReactComponent>]
let private Probe (onCtx: FileTreeExpansionController -> unit) =
    let ctx = useFileTreeExpansionCtx ()
    onCtx ctx
    Html.none

[<ReactComponent>]
let private ExplorerView () =
    let ctx = useFileTreeExpansionCtx ()
    let snapshot = useFileTreeSnapshot ()

    let createItem (entry: FileEntry) =
        let item =
            if entry.isDirectory then
                FileTree.createFolder entry.name (Some entry.path) FileItemIcon.Folder
            else
                FileTree.createFile entry.name (Some entry.path) FileItemIcon.Document

        { item with Id = entry.path }

    let items =
        buildVisibleItems createItem (fun _ entries -> entries) snapshot ctx.expandedIds

    Swate.Components.Page.FileExplorer.FileExplorer.FileExplorer(
        initialItems = items,
        expandedIds = ctx.expandedIds,
        onExpandedIdsChange = ctx.setExpandedIds,
        onDirectoryExpansionChange =
            (fun item willExpand ->
                if willExpand then
                    item.Path |> Option.iter ctx.expandDirectory
            ),
        onRefreshDirectory = (fun item -> item.Path |> Option.iter ctx.refreshDirectory)
    )

let private mount (store: FileTreeStore) (api: FileTreeExpansionApi) (view: ReactElement) =
    let listeners = ResizeArray<FileContentStamp -> unit>()
    let setSelection = ref (fun (_: ArcSelection) -> ())
    let setRoot = ref (fun (_: string) (_: bool) -> ())

    RTL.render (
        Host(
            store,
            api,
            listeners,
            (fun selectionSetter rootSetter ->
                setSelection.Value <- selectionSetter
                setRoot.Value <- rootSetter
            ),
            view
        )
    )
    |> ignore

    {
        Store = store
        Listeners = listeners
        SetSelection = fun selection -> setSelection.Value selection
        SetRoot = fun root isRename -> setRoot.Value root isRename
    }

Vitest.describe (
    "file tree expansion context",
    fun () ->
        Vitest.test (
            "expanding an unloaded folder loads it once, also on a double expand, and shows its children",
            fun () -> promise {
                let calls = newCalls ()
                let answer, resolveAnswer = deferred<Result<FileTreeDirectoryUpdateDto, exn>> ()
                let ctx = ref None
                let store = newStore ()

                let view =
                    React.Fragment [
                        ExplorerView()
                        Probe(fun value -> ctx.Value <- Some value)
                    ]

                mount store (apiWith calls (fun _ -> answer)) view |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "data")
                do! RTL.act (fun () -> promise { ctx.Value.Value.expandDirectory "data" })

                Vitest.expect(calls.Loads.ToArray()).toEqual [| "data" |]

                do! RTL.act (fun () -> promise { resolveAnswer (Ok(update 2 "data" [ file "data/raw.txt" ])) })

                do! waitFor (fun () -> RTL.screen.queryByText("raw.txt").IsSome)
                Vitest.expect(calls.Loads.Count).toBe 1
            }
        )

        Vitest.test (
            "a failed load keeps the folder collapsed with a failure and the next expand retries it",
            fun () -> promise {
                let calls = newCalls ()
                let attempt = ref 0
                let ctx = ref None
                let store = newStore ()

                let view =
                    React.Fragment [
                        ExplorerView()
                        Probe(fun value -> ctx.Value <- Some value)
                    ]

                let load (_: string) =
                    attempt.Value <- attempt.Value + 1

                    if attempt.Value = 1 then
                        JS.Constructors.Promise.resolve (Error(exn "disk error"))
                    else
                        JS.Constructors.Promise.resolve (Ok(update 2 "data" [ file "data/raw.txt" ]))

                mount store (apiWith calls load) view |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "data")

                do!
                    waitFor (fun () ->
                        ctx.Value.Value.loadStates.Count = 1
                        && (
                            match ctx.Value.Value.loadStates |> Map.toList |> List.head |> snd with
                            | Failed message -> message = "disk error"
                            | Loading -> false
                        )
                    )

                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data").toBe false

                RTL.fireEvent.click (RTL.screen.getByText "data")
                do! waitFor (fun () -> RTL.screen.queryByText("raw.txt").IsSome)
                Vitest.expect(calls.Loads.Count).toBe 2
                Vitest.expect(ctx.Value.Value.loadStates.Count).toBe 0
            }
        )

        Vitest.test (
            "selecting a deep path under unloaded folders loads the ancestors root to leaf and keeps the selection",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let view = React.Fragment [ Probe(fun value -> ctx.Value <- Some value) ]

                let load (path: string) =
                    match path with
                    | "notes" -> JS.Constructors.Promise.resolve (Ok(update 2 "notes" [ dir "notes/2026" ]))
                    | _ -> JS.Constructors.Promise.resolve (Ok(update 3 "notes/2026" [ file "notes/2026/n.md" ]))

                let env = mount store (apiWith calls load) view
                let selected = "notes/2026/n.md"

                // Before the folders load, the path is unknown and not missing.
                Vitest
                    .expect(
                        Renderer.Components.Helper.FileExplorerStateReconciliation.isKnownAbsent (
                            knows store.Snapshot selected
                        )
                    )
                    .toBe
                    false

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some selected)) })

                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "notes/2026")

                Vitest.expect(calls.Loads.ToArray()).toEqual [| "notes"; "notes/2026" |]
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "notes").toBe true

                Vitest
                    .expect(
                        Renderer.Components.Helper.FileExplorerStateReconciliation.isKnownAbsent (
                            knows store.Snapshot selected
                        )
                    )
                    .toBe
                    false

                match knows store.Snapshot selected with
                | Present _ -> ()
                | other -> failwith $"Expected the selected file to be present, got {other}."
            }
        )

        Vitest.test (
            "a window focus revalidates once within the debounce and not again within two seconds",
            fun () -> promise {
                let calls = newCalls ()
                let store = newStore ()

                mount store (apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused")))) (Html.none)
                |> ignore

                let focus () = dispatchFocus ()

                focus ()
                focus ()
                focus ()
                do! waitFor (fun () -> calls.Revalidations.Count = 1)
                do! Promise.sleep 50

                Vitest.expect(calls.Revalidations.Count).toBe 1
                Vitest.expect(calls.Revalidations.[0]).toEqual [| "" |]

                // The previous revalidation just finished, so this focus does nothing.
                focus ()
                do! Promise.sleep 450
                Vitest.expect(calls.Revalidations.Count).toBe 1
            }
        )

        Vitest.test (
            "planExpansion drops absent ids and lists present unloaded directories parents first",
            fun () ->
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/deep"; file "data/x.txt" ])

                let plan =
                    planExpansion store.Snapshot (set [ "data/deep"; "gone"; "notes"; "data/deep/inner" ])

                Vitest.expect(plan.ToLoad).toEqual [ "notes"; "data/deep" ]
                Vitest.expect(plan.Expanded |> Set.toList).toEqual ([ "data/deep"; "data/deep/inner"; "notes" ])
        )

        Vitest.test (
            "an expanded folder that a new snapshot lists as not loaded is loaded again and absent ids leave the set",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let version = ref 100

                let load (path: string) =
                    version.Value <- version.Value + 1
                    JS.Constructors.Promise.resolve (Ok(update version.Value path [ file $"{path}/raw.txt" ]))

                mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))
                |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (set [ "data"; "missing" ]) })
                do! waitFor (fun () -> isLoaded store.Snapshot "data")
                Vitest.expect(ctx.Value.Value.expandedIds |> Set.toList).toEqual [ "data" ]

                // A snapshot after a root rename lists the folder as not loaded again.
                do!
                    RTL.act (fun () -> promise {
                        store.BeginLoad()

                        store.ApplySnapshot {|
                            treeVersion = 20
                            caseInsensitive = false
                            entries = [| FileEntry.create ("arc", "", true); dir "data" |]
                            loadedDirectories = [| "" |]
                            fileWatcher = {|
                                state = FileWatcherStatus.Running
                                reason = ""
                            |}
                        |}
                    })

                do! waitFor (fun () -> calls.Loads.Count = 2 && isLoaded store.Snapshot "data")
                Vitest.expect((children store.Snapshot "data").Length).toBe 1
            }
        )

        Vitest.test (
            "expanded folders stay expanded across a root rename and the payload folder loads again",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = FileTreeStore()
                store.BeginLoad()

                let snapshot version : FileTreeSnapshotDto = {|
                    treeVersion = version
                    caseInsensitive = false
                    entries = [|
                        FileEntry.create ("arc", "", true)
                        dir "assays"
                        dir "assays/A1"
                        dir "assays/A1/dataset"
                    |]
                    loadedDirectories = [| ""; "assays"; "assays/A1" |]
                    fileWatcher = {|
                        state = FileWatcherStatus.Running
                        reason = ""
                    |}
                |}

                store.ApplySnapshot(snapshot 1)
                let version = ref 100

                let load (path: string) =
                    version.Value <- version.Value + 1
                    JS.Constructors.Promise.resolve (Ok(update version.Value path [ file $"{path}/raw.txt" ]))

                let env =
                    mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))

                let expanded = set [ "assays"; "assays/A1"; "assays/A1/dataset" ]

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds expanded })
                do! waitFor (fun () -> isLoaded store.Snapshot "assays/A1/dataset")

                // The rename changes the root path, and the new snapshot lists the payload folder as not loaded.
                do! RTL.act (fun () -> promise { env.SetRoot "arc-renamed" true })
                // The answers of the new root carry versions above its snapshot.
                version.Value <- 300

                do!
                    RTL.act (fun () -> promise {
                        store.Reset()
                        store.BeginLoad()
                        store.ApplySnapshot(snapshot 200)
                    })

                do! waitFor (fun () -> calls.Loads.Count = 2 && isLoaded store.Snapshot "assays/A1/dataset")
                Vitest.expect(calls.Loads.ToArray()).toEqual [| "assays/A1/dataset"; "assays/A1/dataset" |]
                Vitest.expect(ctx.Value.Value.expandedIds |> Set.toList).toEqual (expanded |> Set.toList)
            }
        )

        Vitest.test (
            "a load that started before a root change does not apply to the new root",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let oldAnswer, resolveOld = deferred<Result<FileTreeDirectoryUpdateDto, exn>> ()
                let newAnswer, _ = deferred<Result<FileTreeDirectoryUpdateDto, exn>> ()

                let env =
                    mount
                        store
                        (apiWith calls (fun _ -> if calls.Loads.Count = 1 then oldAnswer else newAnswer))
                        (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (set [ "data" ]) })
                do! waitFor (fun () -> calls.Loads.Count = 1)

                do!
                    RTL.act (fun () -> promise {
                        env.SetRoot "other-arc" true
                        store.Reset()
                        store.BeginLoad()

                        store.ApplySnapshot {|
                            treeVersion = 50
                            caseInsensitive = false
                            entries = [| FileEntry.create ("other", "", true); dir "data" |]
                            loadedDirectories = [| "" |]
                            fileWatcher = {|
                                state = FileWatcherStatus.Running
                                reason = ""
                            |}
                        |}
                    })

                do! waitFor (fun () -> calls.Loads.Count = 2)

                do! RTL.act (fun () -> promise { resolveOld (Ok(update 99 "data" [ file "data/old.txt" ])) })
                do! Promise.sleep 30

                Vitest.expect(isLoaded store.Snapshot "data").toBe false
            }
        )

        Vitest.test (
            "another ARC and a closed one start collapsed while a rename keeps the expanded folders",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/deep" ])

                let load (path: string) =
                    JS.Constructors.Promise.resolve (Ok(update 3 path []))

                let env =
                    mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))

                let expanded = set [ "data" ]

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds expanded })
                do! RTL.act (fun () -> promise { env.SetRoot "arc-renamed" true })
                Vitest.expect(ctx.Value.Value.expandedIds |> Set.toList).toEqual [ "data" ]

                do! RTL.act (fun () -> promise { env.SetRoot "another-arc" false })
                Vitest.expect(ctx.Value.Value.expandedIds.IsEmpty).toBe true

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds expanded })
                // An empty root stands for a closed ARC, even when the flag says rename.
                do! RTL.act (fun () -> promise { env.SetRoot "" true })
                Vitest.expect(ctx.Value.Value.expandedIds.IsEmpty).toBe true
            }
        )

        Vitest.test (
            "an answer that the store holds back during a pull is pending, not failed, and is tried again",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let version = ref 5

                let load (path: string) =
                    version.Value <- version.Value + 1
                    JS.Constructors.Promise.resolve (Ok(update version.Value path [ file $"{path}/raw.txt" ]))

                mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))
                |> ignore

                // A pull is under way, so the store buffers the answer and the folder stays unloaded.
                store.BeginLoad()
                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (set [ "data" ]) })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! Promise.sleep 20

                Vitest.expect(isLoaded store.Snapshot "data").toBe false
                Vitest.expect(ctx.Value.Value.loadStates.IsEmpty).toBe true
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data").toBe true

                version.Value <- 40

                do!
                    RTL.act (fun () -> promise {
                        store.ApplySnapshot {|
                            treeVersion = 30
                            caseInsensitive = false
                            entries = [| FileEntry.create ("arc", "", true); dir "data" |]
                            loadedDirectories = [| "" |]
                            fileWatcher = {|
                                state = FileWatcherStatus.Running
                                reason = ""
                            |}
                        |}
                    })

                do! waitFor (fun () -> isLoaded store.Snapshot "data")
                Vitest.expect(calls.Loads.Count).toBe 2
            }
        )

        Vitest.test (
            "an expanded id with another case than its entry becomes the entry spelling",
            fun () ->
                let store = FileTreeStore()
                store.BeginLoad()

                store.ApplySnapshot {|
                    treeVersion = 1
                    caseInsensitive = true
                    entries = [|
                        FileEntry.create ("arc", "", true)
                        dir "Assays"
                        dir "Assays/a"
                    |]
                    loadedDirectories = [| ""; "Assays" |]
                    fileWatcher = {|
                        state = FileWatcherStatus.Running
                        reason = ""
                    |}
                |}

                let plan = planExpansion store.Snapshot (set [ "assays"; "assays/A" ])

                Vitest.expect(plan.Expanded |> Set.toList).toEqual [ "Assays"; "Assays/a" ]
                Vitest.expect(plan.ToLoad).toEqual [ "Assays/a" ]
        )

        Vitest.test (
            "a focus during a slow revalidation starts no second one and a focus after the gap revalidates again",
            TestOptions(timeout = 15000),
            fun () -> promise {
                let calls = newCalls ()
                let slow, finishSlow = deferred<Result<unit, exn>> ()

                let api =
                    apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused")))

                let slowApi = {
                    api with
                        revalidateDirectories =
                            fun paths ->
                                calls.Revalidations.Add paths

                                if calls.Revalidations.Count = 1 then
                                    slow
                                else
                                    JS.Constructors.Promise.resolve (Ok())
                }

                mount (newStore ()) slowApi Html.none |> ignore

                dispatchFocus ()
                do! waitFor (fun () -> calls.Revalidations.Count = 1)

                dispatchFocus ()
                do! Promise.sleep 450
                Vitest.expect(calls.Revalidations.Count).toBe 1

                finishSlow (Ok())
                do! Promise.sleep 2200
                dispatchFocus ()
                do! waitFor (fun () -> calls.Revalidations.Count = 2)
            }
        )

        Vitest.test (
            "a selection made during a pull is revealed after the snapshot",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let version = ref 5

                let load (path: string) =
                    version.Value <- version.Value + 1

                    match path with
                    | "notes" -> JS.Constructors.Promise.resolve (Ok(update version.Value "notes" [ dir "notes/2026" ]))
                    | _ ->
                        JS.Constructors.Promise.resolve (
                            Ok(update version.Value "notes/2026" [ file "notes/2026/n.md" ])
                        )

                let env =
                    mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))

                let selected = "notes/2026/n.md"

                // A pull is under way, so the store holds back the answer for the first folder.
                store.BeginLoad()
                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some selected)) })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! Promise.sleep 20
                Vitest.expect(isLoaded store.Snapshot "notes").toBe false
                Vitest.expect(ctx.Value.Value.expandedIds.IsEmpty).toBe true

                version.Value <- 40

                do!
                    RTL.act (fun () -> promise {
                        store.ApplySnapshot {|
                            treeVersion = 30
                            caseInsensitive = false
                            entries = [| FileEntry.create ("arc", "", true); dir "notes" |]
                            loadedDirectories = [| "" |]
                            fileWatcher = {|
                                state = FileWatcherStatus.Running
                                reason = ""
                            |}
                        |}
                    })

                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "notes/2026")
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "notes").toBe true

                match knows store.Snapshot selected with
                | Present _ -> ()
                | other -> failwith $"Expected the selected file to be present, got {other}."
            }
        )

        Vitest.test (
            "a selection under a folder that fails to load expands nothing",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()

                let load (_: string) =
                    JS.Constructors.Promise.resolve (Error(exn "disk error"))

                let env =
                    mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "data/f.txt")) })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! Promise.sleep 30

                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data").toBe false

                // An unrelated store change must not run the dropped reveal again.
                do! RTL.act (fun () -> promise { store.ApplyDirectoryUpdate(update 2 "notes" []) })
                do! Promise.sleep 30

                Vitest.expect(calls.Loads.Count).toBe 1
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data").toBe false
            }
        )

        Vitest.test (
            "a selection that changes while an earlier reveal waits for a folder is revealed too",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let notesAnswer, resolveNotes = deferred<Result<FileTreeDirectoryUpdateDto, exn>> ()
                let version = ref 10

                let load (path: string) =
                    version.Value <- version.Value + 1

                    match path with
                    | "notes" -> notesAnswer
                    | "notes/2026" ->
                        JS.Constructors.Promise.resolve (Ok(update version.Value path [ file "notes/2026/a.md" ]))
                    | _ -> JS.Constructors.Promise.resolve (Ok(update version.Value path [ file "notes/2027/b.md" ]))

                let env =
                    mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "notes/2026/a.md")) })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "notes/2027/b.md")) })

                do!
                    RTL.act (fun () -> promise {
                        resolveNotes (Ok(update 11 "notes" [ dir "notes/2026"; dir "notes/2027" ]))
                    })

                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "notes/2027")
                Vitest.expect(isLoaded store.Snapshot "notes/2027").toBe true
            }
        )

        Vitest.test (
            "a late stat answer of an earlier selection does not clear the stamp of the current one",
            fun () -> promise {
                let calls = newCalls ()
                let lateNone, finishLateNone = deferred<Result<FileStatDto option, exn>> ()
                let ctx = ref None

                let baseApi =
                    apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused")))

                let api = {
                    baseApi with
                        statFile =
                            fun path ->
                                if path = "first.md" then
                                    lateNone
                                else
                                    JS.Constructors.Promise.resolve (Ok(Some {| sizeBytes = 5.0; mtimeMs = 7.0 |}))
                }

                let env = mount (newStore ()) api (Probe(fun value -> ctx.Value <- Some value))
                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "first.md")) })
                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "second.md")) })
                do! waitFor (fun () -> ctx.Value.Value.contentStamp.IsSome)

                do! RTL.act (fun () -> promise { finishLateNone (Ok None) })
                do! Promise.sleep 20
                Vitest.expect(ctx.Value.Value.contentStamp.Value.Path).toBe "second.md"
            }
        )

        Vitest.test (
            "a focus revalidates the structure and the most recent folders once each",
            fun () ->
                let payload = [
                    for index in 0..69 -> "assays/A1/dir-" + (string index).PadLeft(2, '0')
                ]

                let store = FileTreeStore()
                store.BeginLoad()

                store.ApplySnapshot {|
                    treeVersion = 1
                    caseInsensitive = false
                    entries =
                        Array.ofList (
                            [
                                FileEntry.create ("arc", "", true)
                                dir "assays"
                                dir "assays/A1"
                                dir "notes"
                            ]
                            @ (payload |> List.map dir)
                        )
                    loadedDirectories = Array.ofList ([ ""; "assays"; "assays/A1"; "notes" ] @ payload)
                    fileWatcher = {|
                        state = FileWatcherStatus.Running
                        reason = ""
                    |}
                |}

                let directories =
                    focusRevalidationDirectories store.Snapshot [ "assays/A1/dir-69"; "assays" ]

                Vitest.expect(directories |> Array.take 3).toEqual [| ""; "assays"; "assays/A1" |]
                Vitest.expect(directories.[3]).toBe "assays/A1/dir-69"
                Vitest.expect(directories.Length).toBe (3 + 64)
                Vitest.expect((directories |> Array.distinct).Length).toBe directories.Length
        )

        Vitest.test (
            "a stat that lands while an own-write stat is under way does not replace it",
            fun () -> promise {
                let calls = newCalls ()
                let ownStat, finishOwnStat = deferred<Result<FileStatDto option, exn>> ()
                let statCalls = ref 0
                let ctx = ref None

                let baseApi =
                    apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused")))

                let api = {
                    baseApi with
                        statFile =
                            fun _ ->
                                statCalls.Value <- statCalls.Value + 1

                                match statCalls.Value with
                                | 1 -> JS.Constructors.Promise.resolve (Ok(Some {| sizeBytes = 5.0; mtimeMs = 7.0 |}))
                                | 2 -> ownStat
                                | _ -> JS.Constructors.Promise.resolve (Ok(Some {| sizeBytes = 5.0; mtimeMs = 9.0 |}))
                }

                let env = mount (newStore ()) api (Probe(fun value -> ctx.Value <- Some value))
                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "readme.md")) })
                do! waitFor (fun () -> ctx.Value.Value.contentStamp.IsSome)

                do! RTL.act (fun () -> promise { ctx.Value.Value.recordOwnWrite () })
                // A focus stat resolves with another time while the own-write stat is still under way.
                do! RTL.act (fun () -> promise { ctx.Value.Value.refreshContentStamp () })
                do! Promise.sleep 30
                Vitest.expect(ctx.Value.Value.contentStamp.Value.MtimeMs).toBe 7.0

                do! RTL.act (fun () -> promise { finishOwnStat (Ok(Some {| sizeBytes = 6.0; mtimeMs = 11.0 |})) })
                do! waitFor (fun () -> ctx.Value.Value.contentStamp.Value.MtimeMs = 11.0)
                Vitest.expect(ctx.Value.Value.contentStamp.Value.OwnWrite).toBe true
            }
        )

        Vitest.test (
            "Refresh folder loads the folder again through loadDirectory",
            fun () -> promise {
                let calls = newCalls ()
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ file "data/old.txt" ])

                let load (_: string) =
                    JS.Constructors.Promise.resolve (Ok(update 3 "data" [ file "data/new.txt" ]))

                mount store (apiWith calls load) (ExplorerView()) |> ignore

                RTL.fireEvent.custom ("contextMenu", RTL.screen.getByText "data", {| clientX = 10; clientY = 10 |})
                RTL.fireEvent.click (RTL.screen.getByText "Refresh folder")

                do! waitFor (fun () -> calls.Loads.Count = 1)
                Vitest.expect(calls.Loads.[0]).toBe "data"
                do! waitFor (fun () -> (children store.Snapshot "data" |> Array.map _.name) = [| "new.txt" |])
            }
        )

        Vitest.test (
            "the content stamp follows the selected file and ignores pushes for other files",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()

                let env =
                    mount
                        store
                        (apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused"))))
                        (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "readme.md")) })
                do! waitFor (fun () -> ctx.Value.Value.contentStamp.IsSome)
                Vitest.expect(ctx.Value.Value.contentStamp.Value.MtimeMs).toBe 7.0

                do!
                    RTL.act (fun () -> promise {
                        for listener in env.Listeners.ToArray() do
                            listener {
                                Path = "other.txt"
                                SizeBytes = 1.0
                                MtimeMs = 99.0
                                OwnWrite = false
                            }
                    })

                Vitest.expect(ctx.Value.Value.contentStamp.Value.MtimeMs).toBe 7.0

                do!
                    RTL.act (fun () -> promise {
                        for listener in env.Listeners.ToArray() do
                            listener {
                                Path = "readme.md"
                                SizeBytes = 6.0
                                MtimeMs = 8.0
                                OwnWrite = false
                            }
                    })

                Vitest.expect(ctx.Value.Value.contentStamp.Value.MtimeMs).toBe 8.0
            }
        )
)

[<ReactComponent>]
let private SelectorProbe (path: string, onValues: FileEntry option * KnownState -> unit) =
    let entry = useFileTreeEntry (Some path)
    let known = useFileTreeKnows (Some path)
    onValues (entry, known)
    Html.none

Vitest.describe (
    "file tree selector hooks",
    fun () ->
        Vitest.test (
            "return the same instances when an unrelated directory changes",
            fun () -> promise {
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ file "data/a.txt"; file "data/b.txt" ])
                let seen = ResizeArray<FileEntry option * KnownState>()
                let calls = newCalls ()

                mount
                    store
                    (apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused"))))
                    (SelectorProbe("data/a.txt", seen.Add))
                |> ignore

                let entry, known = seen.[seen.Count - 1]

                do! RTL.act (fun () -> promise { store.ApplyDirectoryUpdate(update 3 "notes" [ file "notes/x.md" ]) })

                let entryAfter, knownAfter = seen.[seen.Count - 1]
                Vitest.expect(obj.ReferenceEquals(entry.Value, entryAfter.Value)).toBe true
                Vitest.expect(obj.ReferenceEquals(known, knownAfter)).toBe true

                // A change inside the folder keeps the untouched entry.
                do!
                    RTL.act (fun () -> promise {
                        store.ApplyDirectoryUpdate(update 4 "data" [ file "data/a.txt"; file "data/c.txt" ])
                    })

                let entryLater, _ = seen.[seen.Count - 1]
                Vitest.expect(obj.ReferenceEquals(entry.Value, entryLater.Value)).toBe true
            }
        )
)

Vitest.describe (
    "revealExpansion",
    fun () ->
        let reveal (current: string list) (toOpen: string list) =
            revealExpansion (Set.ofList current) (Set.ofList toOpen) |> Set.toList

        Vitest.test (
            "drops the subfolders of a newly opened folder",
            fun () ->
                Vitest.expect(reveal [ "assays/A"; "assays/A/x"; "assays/B"; "data" ] [ "assays" ]).toEqual [
                    "assays"
                    "data"
                ]
        )

        Vitest.test (
            "keeps the subfolders of a folder that was already expanded",
            fun () ->
                Vitest.expect(reveal [ "assays"; "assays/A" ] [ "assays"; "assays/B" ]).toEqual [
                    "assays"
                    "assays/A"
                    "assays/B"
                ]
        )

        Vitest.test (
            "drops ids below a newly opened folder that are not in the reveal",
            fun () ->
                Vitest.expect(reveal [ "assays/A/raw"; "assays/B" ] [ "assays"; "assays/A" ]).toEqual [
                    "assays"
                    "assays/A"
                ]
        )

        Vitest.test (
            "a root target keeps the expanded folders",
            fun () -> Vitest.expect(reveal [ "a"; "b"; "b/c" ] [ ""; "x" ]).toEqual [ ""; "a"; "b"; "b/c"; "x" ]
        )

        Vitest.test (
            "returns the same instance when nothing new opens",
            fun () ->
                let current = Set.ofList [ "assays"; "assays/A" ]
                Vitest.expect(obj.ReferenceEquals(revealExpansion current (Set.ofList [ "assays" ]), current)).toBe true
        )
)

/// data and data/sub are loaded, and data/sub holds one file.
let private storeWithNestedFile () =
    let store = newStore ()
    store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/sub" ])
    store.ApplyDirectoryUpdate(update 3 "data/sub" [ file "data/sub/f.txt" ])
    store

let private unusedLoad (_: string) =
    JS.Constructors.Promise.resolve (Error(exn "unused"))

Vitest.describe (
    "FileTreeExpansionContext revealPaths",
    fun () ->
        Vitest.test (
            "opens the folders above a file that is in the tree",
            fun () -> promise {
                let ctx = ref None
                let store = storeWithNestedFile ()

                let view = Probe(fun value -> ctx.Value <- Some value)

                mount store (apiWith (newCalls ()) unusedLoad) view |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/sub/f.txt" ] })

                do!
                    waitFor (fun () ->
                        ctx.Value.Value.expandedIds.Contains "data"
                        && ctx.Value.Value.expandedIds.Contains "data/sub"
                    )
            }
        )

        Vitest.test (
            "expands nothing while the path is not in the tree and opens its folders once it arrives",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let view = Probe(fun value -> ctx.Value <- Some value)

                let load (path: string) =
                    match path with
                    | "data" -> JS.Constructors.Promise.resolve (Ok(update 2 "data" []))
                    | _ -> JS.Constructors.Promise.resolve (Ok(update 4 "data/sub" [ file "data/sub/f.txt" ]))

                mount store (apiWith calls load) view |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/sub/f.txt" ] })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! Promise.sleep 30

                Vitest.expect(ctx.Value.Value.expandedIds.IsEmpty).toBe true

                do! RTL.act (fun () -> promise { store.ApplyDirectoryUpdate(update 3 "data" [ dir "data/sub" ]) })

                do!
                    waitFor (fun () ->
                        ctx.Value.Value.expandedIds.Contains "data"
                        && ctx.Value.Value.expandedIds.Contains "data/sub"
                    )
            }
        )

        Vitest.test (
            "a selection change keeps a create or import reveal that waits for the tree push",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/other" ])
                store.ApplyDirectoryUpdate(update 3 "data/other" [ file "data/other/n.md" ])

                let load (path: string) =
                    let children = if path = "data/A" then [ dir "data/A/raw" ] else []
                    JS.Constructors.Promise.resolve (Ok(update 6 path children))

                let env =
                    mount store (apiWith (newCalls ()) load) (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/A/raw" ] })
                do! Promise.sleep 30
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/A").toBe false

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "data/other/n.md")) })
                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "data/other")

                do!
                    RTL.act (fun () -> promise {
                        store.ApplyDirectoryUpdate(update 4 "data" [ dir "data/other"; dir "data/A" ])
                    })

                do!
                    waitFor (fun () ->
                        [ "data"; "data/other"; "data/A"; "data/A/raw" ]
                        |> List.forall ctx.Value.Value.expandedIds.Contains
                    )
            }
        )

        Vitest.test (
            "a selected folder stays collapsed while the folders above it open",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/S1" ])
                store.ApplyDirectoryUpdate(update 3 "data/S1" [ file "data/S1/isa.study.xlsx" ])

                let env =
                    mount store (apiWith (newCalls ()) unusedLoad) (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data" ] })
                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "data/S1")) })
                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "data")
                do! Promise.sleep 30

                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/S1").toBe false
            }
        )

        Vitest.test (
            "opens a folder target itself as well as the folders above it",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/sub" ])
                let view = Probe(fun value -> ctx.Value <- Some value)

                let load (_: string) =
                    JS.Constructors.Promise.resolve (Ok(update 3 "data/sub" []))

                mount store (apiWith (newCalls ()) load) view |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/sub" ] })

                do!
                    waitFor (fun () ->
                        ctx.Value.Value.expandedIds.Contains "data"
                        && ctx.Value.Value.expandedIds.Contains "data/sub"
                    )
            }
        )

        Vitest.test (
            "waits for every path to be in the tree and then opens the folders among them",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/A" ])
                store.ApplyDirectoryUpdate(update 3 "data/A" [ dir "data/A/raw" ])
                let view = Probe(fun value -> ctx.Value <- Some value)

                let version = ref 10

                let load (path: string) =
                    version.Value <- version.Value + 1
                    JS.Constructors.Promise.resolve (Ok(update version.Value path []))

                mount store (apiWith (newCalls ()) load) view |> ignore

                do!
                    RTL.act (fun () -> promise {
                        ctx.Value.Value.revealPaths [ "data/A"; "data/A/raw"; "data/A/docs" ]
                    })

                do! Promise.sleep 30
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/A/raw").toBe false

                do!
                    RTL.act (fun () -> promise {
                        store.ApplyDirectoryUpdate(update 4 "data/A" [ dir "data/A/raw"; dir "data/A/docs" ])
                    })

                do!
                    waitFor (fun () ->
                        [ "data"; "data/A"; "data/A/raw"; "data/A/docs" ]
                        |> List.forall ctx.Value.Value.expandedIds.Contains
                    )
            }
        )

        Vitest.test (
            "loads a folder that is not loaded yet when imported folders lie below it",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/A" ])
                let view = Probe(fun value -> ctx.Value <- Some value)
                let version = ref 10

                let load (path: string) =
                    version.Value <- version.Value + 1

                    if path = "data/A" then
                        JS.Constructors.Promise.resolve (Ok(update version.Value path [ dir "data/A/raw" ]))
                    else
                        JS.Constructors.Promise.resolve (Ok(update version.Value path []))

                mount store (apiWith calls load) view |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/A"; "data/A/raw" ] })

                do!
                    waitFor (fun () ->
                        [ "data"; "data/A"; "data/A/raw" ]
                        |> List.forall ctx.Value.Value.expandedIds.Contains
                    )

                Vitest.expect(calls.Loads.Contains "data/A").toBe true
            }
        )

        Vitest.test (
            "does not reopen a revealed folder that the user closed when the store changes again",
            fun () -> promise {
                let ctx = ref None
                let store = storeWithNestedFile ()
                let view = Probe(fun value -> ctx.Value <- Some value)

                mount store (apiWith (newCalls ()) unusedLoad) view |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/sub/f.txt" ] })
                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "data/sub")

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (Set.ofList [ "data" ]) })

                do!
                    RTL.act (fun () -> promise {
                        store.ApplyDirectoryUpdate(update 4 "data" [ dir "data/sub"; file "data/x.txt" ])
                    })

                do! Promise.sleep 30
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/sub").toBe false
            }
        )

        Vitest.test (
            "a revealPaths from an earlier ARC is ignored",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let view = Probe(fun value -> ctx.Value <- Some value)

                // The answers contain the path, so a reveal that ran would open its folders.
                let load (path: string) =
                    match path with
                    | "data" -> JS.Constructors.Promise.resolve (Ok(update 2 "data" [ dir "data/sub" ]))
                    | _ -> JS.Constructors.Promise.resolve (Ok(update 3 "data/sub" [ file "data/sub/f.txt" ]))

                let env = mount store (apiWith calls load) view
                let earlierRevealPaths = ctx.Value.Value.revealPaths

                do! RTL.act (fun () -> promise { env.SetRoot "other" false })
                do! RTL.act (fun () -> promise { earlierRevealPaths [ "data/sub/f.txt" ] })

                do! Promise.sleep 30
                Vitest.expect(calls.Loads.Count).toBe 0
                Vitest.expect(ctx.Value.Value.expandedIds.IsEmpty).toBe true
            }
        )

        Vitest.test (
            "a failed folder load ends a waiting reveal",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()
                let view = Probe(fun value -> ctx.Value <- Some value)

                let load (_: string) =
                    JS.Constructors.Promise.resolve (Error(exn "disk error"))

                mount store (apiWith calls load) view |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/f.txt" ] })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! Promise.sleep 30

                do! RTL.act (fun () -> promise { store.ApplyDirectoryUpdate(update 2 "data" [ file "data/f.txt" ]) })

                do! Promise.sleep 30
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data").toBe false
            }
        )

        Vitest.test (
            "a revealPaths retries a folder that failed before",
            fun () -> promise {
                let calls = newCalls ()
                let ctx = ref None
                let store = newStore ()

                let load (_: string) =
                    if calls.Loads.Count = 1 then
                        JS.Constructors.Promise.resolve (Error(exn "disk error"))
                    else
                        JS.Constructors.Promise.resolve (Ok(update 2 "data" [ file "data/f.txt" ]))

                mount store (apiWith calls load) (Probe(fun value -> ctx.Value <- Some value))
                |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/f.txt" ] })
                do! waitFor (fun () -> calls.Loads.Count = 1)
                do! Promise.sleep 30

                Vitest.expect(isLoaded store.Snapshot "data").toBe false

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/f.txt" ] })
                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "data")

                Vitest.expect(isLoaded store.Snapshot "data").toBe true
            }
        )

        Vitest.test (
            "a reveal does not reopen subfolders of a collapsed folder",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/A"; dir "data/B"; dir "data/C" ])
                store.ApplyDirectoryUpdate(update 3 "data/A" [ file "data/A/a.txt" ])
                store.ApplyDirectoryUpdate(update 4 "data/B" [ file "data/B/b.txt" ])

                mount store (apiWith (newCalls ()) unusedLoad) (Probe(fun value -> ctx.Value <- Some value))
                |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (Set.ofList [ "data/A"; "data/B" ]) })

                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "data/C" ] })
                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "data")

                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/A").toBe false
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/B").toBe false
            }
        )

        Vitest.test (
            "a selection does not reopen subfolders of a collapsed folder",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ dir "data/A"; file "data/f.txt" ])
                store.ApplyDirectoryUpdate(update 3 "data/A" [ file "data/A/a.txt" ])

                let env =
                    mount store (apiWith (newCalls ()) unusedLoad) (Probe(fun value -> ctx.Value <- Some value))

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (Set.ofList [ "data/A" ]) })

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some "data/f.txt")) })
                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "data")

                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data/A").toBe false
            }
        )

        Vitest.test (
            "an import at the ARC root keeps the expanded folders",
            fun () -> promise {
                let ctx = ref None
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ file "data/f.txt" ])

                mount store (apiWith (newCalls ()) unusedLoad) (Probe(fun value -> ctx.Value <- Some value))
                |> ignore

                do! RTL.act (fun () -> promise { ctx.Value.Value.setExpandedIds (Set.ofList [ "data" ]) })
                do! RTL.act (fun () -> promise { ctx.Value.Value.revealPaths [ "" ] })
                do! Promise.sleep 30

                Vitest.expect(ctx.Value.Value.expandedIds.Contains "data").toBe true
            }
        )
)
