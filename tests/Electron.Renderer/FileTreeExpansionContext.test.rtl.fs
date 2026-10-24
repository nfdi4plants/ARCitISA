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
                setSelection = setSelection
                updateSelection = ignore
                activeFileImport = None
                isCancellingFileImport = false
                importExternalFiles = fun _ -> JS.Constructors.Promise.resolve (Ok())
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
                        Renderer.Components.Helper.FileExplorerStateReconciliation.isSelectionMissing
                            store.Snapshot
                            (Some selected)
                    )
                    .toBe
                    false

                do! RTL.act (fun () -> promise { env.SetSelection(ArcSelection.forTreePath (Some selected)) })

                do! waitFor (fun () -> ctx.Value.Value.expandedIds.Contains "notes/2026")

                Vitest.expect(calls.Loads.ToArray()).toEqual [| "notes"; "notes/2026" |]
                Vitest.expect(ctx.Value.Value.expandedIds.Contains "notes").toBe true

                Vitest
                    .expect(
                        Renderer.Components.Helper.FileExplorerStateReconciliation.isSelectionMissing
                            store.Snapshot
                            (Some selected)
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
let private SelectorProbe (path: string, onValues: FileEntry option * FileEntry[] * KnownState -> unit) =
    let entry = useFileTreeEntry (Some path)
    let childEntries = useFileTreeChildren "data"
    let known = useFileTreeKnows (Some path)
    onValues (entry, childEntries, known)
    Html.none

Vitest.describe (
    "file tree selector hooks",
    fun () ->
        Vitest.test (
            "return the same instances when an unrelated directory changes",
            fun () -> promise {
                let store = newStore ()
                store.ApplyDirectoryUpdate(update 2 "data" [ file "data/a.txt"; file "data/b.txt" ])
                let seen = ResizeArray<FileEntry option * FileEntry[] * KnownState>()
                let calls = newCalls ()

                mount
                    store
                    (apiWith calls (fun _ -> JS.Constructors.Promise.resolve (Error(exn "unused"))))
                    (SelectorProbe("data/a.txt", seen.Add))
                |> ignore

                let entry, childEntries, _ = seen.[seen.Count - 1]

                do! RTL.act (fun () -> promise { store.ApplyDirectoryUpdate(update 3 "notes" [ file "notes/x.md" ]) })

                let entryAfter, childrenAfter, _ = seen.[seen.Count - 1]
                Vitest.expect(obj.ReferenceEquals(entry.Value, entryAfter.Value)).toBe true
                Vitest.expect(obj.ReferenceEquals(childEntries, childrenAfter)).toBe true

                // A change inside the folder replaces its children and keeps the untouched entry.
                do!
                    RTL.act (fun () -> promise {
                        store.ApplyDirectoryUpdate(update 4 "data" [ file "data/a.txt"; file "data/c.txt" ])
                    })

                let entryLater, childrenLater, _ = seen.[seen.Count - 1]
                Vitest.expect(obj.ReferenceEquals(entry.Value, entryLater.Value)).toBe true
                Vitest.expect(obj.ReferenceEquals(childEntries, childrenLater)).toBe false
            }
        )
)
