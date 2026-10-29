module ElectronRenderer.LfsActivityContextTests

open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Renderer.Context.LfsActivityContext
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Vitest

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for React to settle."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

let private waitFor predicate = waitUntil (predicate, 100)

let private fileEntry (path: string) =
    FileEntry.create (PathHelpers.getFileName path, path, false)

let private activity label path = {
    Label = label
    Entry = Some(fileEntry path)
}

[<ReactComponent>]
let private ControllerProbe (onController: LfsActivityController -> unit) =
    let controller = useLfsActivityCtx ()
    React.useEffect ((fun () -> onController controller), [| box controller |])
    Html.none

/// `showProbe = false` unmounts the consumer the way a sidebar panel switch unmounts the file tree.
[<ReactComponent>]
let private ProviderHarness (arcRootPath: string option, showProbe: bool, onController: LfsActivityController -> unit) =
    Renderer.Context.AppStateContext.AppStateCtx.Provider(
        arcRootPath,
        LfsActivityCtxProvider(
            if showProbe then
                ControllerProbe onController
            else
                Html.none
        )
    )

type private Deferred = {
    pending: JS.Promise<Result<unit, string>>
    resolve: Result<unit, string> -> unit
}

let private deferred () =
    let mutable resolveResult = ignore

    let pending = Promise.create (fun resolve _ -> resolveResult <- resolve)

    {
        pending = pending
        resolve = fun result -> resolveResult result
    }

let private versionControlBridgeName = "FABLE_REMOTING_IVersionControlRendererApi"

type private VersionControlProgress = Swate.Electron.Shared.VersionControlTypes.VersionControlProgressDto
type private VersionControlOutput = Swate.Electron.Shared.VersionControlTypes.VersionControlOutputDto

/// The provider listens for version control progress on the IPC bridge, which only exists inside Electron.
/// `onListener` receives the listener that the provider registers.
let private installVersionControlBridge
    (onListener: (VersionControlProgress -> unit) option -> unit)
    (onOutputListener: (VersionControlOutput -> unit) option -> unit)
    =
    let startedListeners = ResizeArray<obj>()

    window?(versionControlBridgeName) <-
        createObj [
            "versionControlProgress"
            ==> fun (listener: VersionControlProgress -> unit) ->
                onListener (Some listener)
                fun () -> onListener None
            "versionControlOperationStarted"
            ==> fun (listener: obj) ->
                startedListeners.Add listener
                fun () -> startedListeners.Remove listener |> ignore
            "versionControlOutput"
            ==> fun (listener: VersionControlOutput -> unit) ->
                onOutputListener (Some listener)
                fun () -> onOutputListener None
        ]

let private removeVersionControlBridge () : unit =
    emitJsStatement versionControlBridgeName "delete window[$0]"

Vitest.beforeEach (fun () -> installVersionControlBridge ignore ignore)
Vitest.afterEach (fun () -> removeVersionControlBridge ())

Vitest.describe (
    "LfsActivityState",
    fun () ->
        Vitest.test (
            "a busy path missing from the listing keeps its last known entry",
            fun () ->
                let listed = [| fileEntry "runs/other.bin" |]
                let activities = Map.ofList [ "runs/big.bin", activity "Freeing" "runs/big.bin" ]

                let merged =
                    LfsActivityState.withBusyChildren (LfsActivityState.busyEntriesByParent activities) "runs" listed

                Vitest.expect(merged |> Array.map _.path).toEqual ([| "runs/other.bin"; "runs/big.bin" |])
        )

        Vitest.test (
            "a busy path that is still listed is not added twice",
            fun () ->
                let listed = [| fileEntry "runs/big.bin" |]

                let activities =
                    Map.ofList [ "runs/big.bin", activity "Downloading" "runs/big.bin" ]

                let merged =
                    LfsActivityState.withBusyChildren (LfsActivityState.busyEntriesByParent activities) "runs" listed

                Vitest.expect(obj.ReferenceEquals(merged, listed)).toBe (true)
        )

        Vitest.test (
            "a busy file of another folder is not added",
            fun () ->
                let activities = Map.ofList [ "runs/big.bin", activity "Freeing" "runs/big.bin" ]

                let busyByParent = LfsActivityState.busyEntriesByParent activities

                Vitest.expect(LfsActivityState.withBusyChildren busyByParent "assays" [||]).toEqual ([||])
        )

        Vitest.test (
            "each ARC sees only its own activities and other ARCs' entries stay",
            fun () ->
                let state =
                    LfsActivityState.empty
                    |> LfsActivityState.start "C:/arc-a" "a.bin" (activity "Freeing" "a.bin")
                    |> LfsActivityState.start "C:/arc-b" "b.bin" (activity "Downloading" "b.bin")

                let forB = LfsActivityState.forScope (Some "C:/arc-b") state
                Vitest.expect(forB |> Map.toList |> List.map fst).toEqual ([ "b.bin" ])

                Vitest.expect(LfsActivityState.forScope None state |> Map.count).toBe (0)
                Vitest.expect(LfsActivityState.isBusy "C:/arc-a" "a.bin" state).toBe (true)

                let finished = LfsActivityState.finish "C:/arc-a" "a.bin" state
                Vitest.expect(LfsActivityState.isBusy "C:/arc-a" "a.bin" finished).toBe (false)
                Vitest.expect(LfsActivityState.isBusy "C:/arc-b" "b.bin" finished).toBe (true)
        )

        Vitest.test (
            "a listing path in another Unicode form than its tree entry is keyed by the entry path",
            fun () ->
                let composedPath = "M\u00FCller/big.bin"
                let decomposedFolder = "Mu\u0308ller"
                let decomposedPath = "Mu\u0308ller/big.bin"
                let store = Renderer.Context.FileTreeStore.FileTreeStore()
                store.BeginLoad()

                store.ApplySnapshot {|
                    treeVersion = 1
                    caseInsensitive = false
                    entries = [|
                        FileEntry.create (decomposedFolder, decomposedFolder, true)
                        fileEntry decomposedPath
                    |]
                    loadedDirectories = [| ""; decomposedFolder |]
                    fileWatcher = {|
                        state = Swate.Electron.Shared.IPCTypes.FileWatcherStatus.Running
                        reason = ""
                    |}
                |}

                let key, entry = LfsActivityState.batchTarget store.Snapshot composedPath

                Vitest.expect(key).toBe decomposedPath
                Vitest.expect(entry |> Option.map _.path).toEqual (Some decomposedPath)
                Vitest.expect(LfsActivityState.batchTarget store.Snapshot "other.bin").toEqual ("other.bin", None)
        )
)

Vitest.describe (
    "LfsActivityCtxProvider",
    fun () ->
        Vitest.test (
            "an action that settles while no consumer is mounted still clears its busy state",
            fun () -> promise {
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable latest: LfsActivityController option = None
                let onController controller = latest <- Some controller

                let busyLabel path =
                    latest
                    |> Option.bind (fun c -> Map.tryFind path c.activities)
                    |> Option.map _.Label

                try
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    let action = deferred ()
                    let mutable actionCalls = 0

                    let runAction _ =
                        actionCalls <- actionCalls + 1
                        action.pending

                    let running =
                        latest.Value.run "Downloading" (Some(fileEntry "runs/big.bin")) runAction "runs/big.bin"

                    do! waitFor (fun () -> busyLabel "runs/big.bin" = Some "Downloading")

                    let! secondStart =
                        latest.Value.run "Downloading" (Some(fileEntry "runs/big.bin")) runAction "runs/big.bin"

                    Vitest.expect(secondStart).toEqual (Ok())
                    Vitest.expect(actionCalls).toBe (1)

                    root.render (ProviderHarness(Some "C:/arc", false, onController))
                    do! Promise.sleep 0
                    latest <- None
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    Vitest.expect(busyLabel "runs/big.bin").toEqual (Some "Downloading")

                    root.render (ProviderHarness(Some "C:/arc", false, onController))
                    do! Promise.sleep 0
                    action.resolve (Error "network down")
                    let! result = running
                    Vitest.expect(result).toEqual (Error "network down")

                    latest <- None
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    Vitest.expect(latest.Value.activities.IsEmpty).toBe (true)
                finally
                    root.unmount ()
                    container.remove ()
            }
        )

        Vitest.test (
            "an action keeps its busy state across an ARC switch and back until it settles",
            fun () -> promise {
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable latest: LfsActivityController option = None
                let onController controller = latest <- Some controller
                let action = deferred ()
                let mutable actionCalls = 0

                let runAction _ =
                    actionCalls <- actionCalls + 1
                    action.pending

                let busyLabel path =
                    latest
                    |> Option.bind (fun c -> Map.tryFind path c.activities)
                    |> Option.map _.Label

                try
                    root.render (ProviderHarness(Some "C:/arc-a", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    let running = latest.Value.run "Freeing" None runAction "a.bin"
                    do! waitFor (fun () -> busyLabel "a.bin" = Some "Freeing")

                    root.render (ProviderHarness(Some "C:/arc-b", true, onController))
                    do! waitFor (fun () -> busyLabel "a.bin" = None)

                    root.render (ProviderHarness(Some "C:/arc-a", true, onController))
                    do! waitFor (fun () -> busyLabel "a.bin" = Some "Freeing")

                    let! secondStart = latest.Value.run "Freeing" None runAction "a.bin"
                    Vitest.expect(secondStart).toEqual (Ok())
                    Vitest.expect(actionCalls).toBe (1)

                    action.resolve (Ok())
                    let! _ = running
                    do! waitFor (fun () -> busyLabel "a.bin" = None)

                    Vitest.expect(latest.Value.activities.IsEmpty).toBe (true)
                finally
                    root.unmount ()
                    container.remove ()
            }
        )

        Vitest.test (
            "runAll marks every path busy, runs the action once, and clears them, but leaves a busy path to its owner",
            fun () -> promise {
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable latest: LfsActivityController option = None
                let onController controller = latest <- Some controller
                let owner = deferred ()
                let batch = deferred ()
                let mutable batchCalls = 0

                let runBatch () =
                    batchCalls <- batchCalls + 1
                    batch.pending

                let busyLabel path =
                    latest
                    |> Option.bind (fun c -> Map.tryFind path c.activities)
                    |> Option.map _.Label

                try
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    let ownerRun = latest.Value.run "Downloading" None (fun _ -> owner.pending) "b.bin"
                    do! waitFor (fun () -> busyLabel "b.bin" = Some "Downloading")

                    let running =
                        latest.Value.runAll
                            "Freeing"
                            [|
                                "a.bin", Some(fileEntry "a.bin")
                                "b.bin", None
                                "c.bin", None
                            |]
                            runBatch

                    do! waitFor (fun () -> busyLabel "a.bin" = Some "Freeing" && busyLabel "c.bin" = Some "Freeing")

                    Vitest.expect(busyLabel "b.bin").toEqual (Some "Downloading")
                    Vitest.expect(batchCalls).toBe (1)

                    batch.resolve (Error "disk full")
                    let! result = running
                    Vitest.expect(result).toEqual (Error "disk full")
                    do! waitFor (fun () -> busyLabel "a.bin" = None && busyLabel "c.bin" = None)

                    Vitest.expect(busyLabel "b.bin").toEqual (Some "Downloading")

                    owner.resolve (Ok())
                    let! _ = ownerRun
                    do! waitFor (fun () -> latest.Value.activities.IsEmpty)
                finally
                    root.unmount ()
                    container.remove ()
            }
        )
)

let private eventOf
    (operationId: string)
    (phase: string)
    (completed: float option)
    (total: float option)
    : VersionControlProgress =
    {
        OperationId = operationId
        PhaseCode = phase
        Item = None
        Completed = completed
        Total = total
        DisplayMessage = None
    }

/// A count event of a batch with ten files.
let private countEventFor (operationId: string) (completed: float) =
    eventOf operationId LfsProgressPhase.MaterializeFiles (Some completed) (Some 10.0)

let private runningAction (operationId: string) : LfsRunningAction = {
    OperationId = operationId
    Kind = LfsActionKind.Download
    Target = "runs"
    IsFolder = true
    FileCount = 3
    Count = None
    StepPercent = None
    Log = Renderer.Context.OperationLog.empty
    StartedAt = 0.0
}

[<ReactComponent>]
let private RunningActionsProbe (onActions: LfsRunningAction[] -> unit) =
    let actions = useLfsRunningActionsCtx ()
    React.useEffect ((fun () -> onActions actions), [| box actions |])
    Html.none

/// Mounts the provider with a probe for the controller and one for the running actions. The test reaches the
/// listener that the provider registered on the IPC bridge to send it progress events.
let private withRunningActions
    (body:
        (unit -> LfsActivityController)
            -> (unit -> LfsRunningAction[])
            -> (VersionControlProgress -> unit)
            -> (VersionControlOutput -> unit)
            -> JS.Promise<unit>)
    =
    promise {
        let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
        document.body.appendChild container |> ignore
        let root = ReactDOM.createRoot container
        let controller: LfsActivityController option ref = ref None
        let actions: LfsRunningAction[] ref = ref [||]

        let progressListener: (VersionControlProgress -> unit) option ref = ref None
        let outputListener: (VersionControlOutput -> unit) option ref = ref None

        installVersionControlBridge
            (fun listener -> progressListener.Value <- listener)
            (fun listener -> outputListener.Value <- listener)

        try
            root.render (
                Renderer.Context.AppStateContext.AppStateCtx.Provider(
                    Some "C:/arc",
                    LfsActivityCtxProvider(
                        React.Fragment [
                            ControllerProbe(fun c -> controller.Value <- Some c)
                            RunningActionsProbe(fun a -> actions.Value <- a)
                        ]
                    )
                )
            )

            do!
                waitFor (fun () ->
                    controller.Value.IsSome
                    && progressListener.Value.IsSome
                    && outputListener.Value.IsSome
                )

            do!
                body
                    (fun () -> controller.Value.Value)
                    (fun () -> actions.Value)
                    (fun progress -> progressListener.Value.Value progress)
                    (fun output -> outputListener.Value.Value output)
        finally
            root.unmount ()
            container.remove ()
    }

Vitest.describe (
    "LfsActivityCtxProvider progress",
    fun () ->
        Vitest.test (
            "the latest count event of a tracked operation id is stored with its running action",
            fun () ->
                withRunningActions (fun controller actions sendProgress _ -> promise {
                    let work = deferred ()
                    let running = controller().track (runningAction "op-a") (fun () -> work.pending)

                    do! waitFor (fun () -> actions().Length = 1)
                    Vitest.expect(actions().[0].Count).toEqual (None)

                    sendProgress (countEventFor "op-a" 2.0)
                    sendProgress (countEventFor "op-a" 6.0)

                    do! waitFor (fun () -> actions().[0].Count = Some(6, 10))
                    Vitest.expect(actions().[0].OperationId).toBe ("op-a")

                    work.resolve (Ok())
                    let! _ = running
                    ()
                })
        )

        Vitest.test (
            "track stamps the start time of the running action",
            fun () ->
                withRunningActions (fun controller actions _ _ -> promise {
                    let before = Fable.Core.JS.Constructors.Date.now ()
                    let work = deferred ()
                    let running = controller().track (runningAction "op-a") (fun () -> work.pending)

                    do! waitFor (fun () -> actions().Length = 1)

                    let after = Fable.Core.JS.Constructors.Date.now ()
                    Vitest.expect(actions().[0].StartedAt >= before && actions().[0].StartedAt <= after).toBe true

                    work.resolve (Ok())
                    let! _ = running
                    ()
                })
        )

        Vitest.test (
            "progress of an unknown operation id is ignored",
            fun () ->
                withRunningActions (fun controller actions sendProgress _ -> promise {
                    let work = deferred ()
                    let running = controller().track (runningAction "op-a") (fun () -> work.pending)

                    do! waitFor (fun () -> actions().Length = 1)

                    sendProgress (countEventFor "op-other" 5.0)
                    do! Promise.sleep 20
                    Vitest.expect(actions().[0].Count).toEqual (None)

                    // The provider is listening, because its own operation id still gets through.
                    sendProgress (countEventFor "op-a" 1.0)
                    do! waitFor (fun () -> actions().[0].Count = Some(1, 10))

                    work.resolve (Ok())
                    let! _ = running
                    ()
                })
        )

        Vitest.test (
            "the running action goes away when the work settles and later progress does not bring it back",
            fun () ->
                withRunningActions (fun controller actions sendProgress _ -> promise {
                    let work = deferred ()
                    let running = controller().track (runningAction "op-a") (fun () -> work.pending)

                    do! waitFor (fun () -> actions().Length = 1)

                    work.resolve (Error "network down")
                    let! result = running
                    Vitest.expect(result).toEqual (Error "network down")

                    do! waitFor (fun () -> actions().Length = 0)

                    sendProgress (countEventFor "op-a" 9.0)
                    do! Promise.sleep 20
                    Vitest.expect(actions().Length).toBe (0)
                })
        )
)

let private outputFor (operationId: string) (lines: string list) : VersionControlOutput = {
    OperationId = operationId
    Entries =
        lines
        |> List.map (fun text ->
            let entry: Swate.Electron.Shared.VersionControlTypes.VersionControlOutputEntryDto = {
                CommandId = "cmd-1"
                Kind = "stdout"
                Text = text
                IsPartial = false
            }

            entry
        )
        |> List.toArray
}

Vitest.describe (
    "LfsActivityCtxProvider output",
    fun () ->
        Vitest.test (
            "output of a tracked action is appended to the log of that action",
            fun () ->
                withRunningActions (fun controller actions _ sendOutput -> promise {
                    let work = deferred ()
                    let running = controller().track (runningAction "op-a") (fun () -> work.pending)
                    let other = controller().track (runningAction "op-b") (fun () -> work.pending)

                    do! waitFor (fun () -> actions().Length = 2)

                    sendOutput (outputFor "op-a" [ "first" ])
                    sendOutput (outputFor "op-a" [ "second"; "third" ])
                    sendOutput (outputFor "op-b" [ "other" ])

                    do! waitFor (fun () -> actions().[0].Log.Lines.Length = 3)

                    Vitest.expect(actions().[0].Log.Lines |> Array.map _.Text).toEqual [| "first"; "second"; "third" |]
                    Vitest.expect(actions().[1].Log.Lines |> Array.map _.Text).toEqual [| "other" |]

                    work.resolve (Ok())
                    let! _ = running
                    let! _ = other
                    ()
                })
        )

        Vitest.test (
            "output of an unknown operation id is ignored",
            fun () ->
                withRunningActions (fun controller actions _ sendOutput -> promise {
                    let work = deferred ()
                    let running = controller().track (runningAction "op-a") (fun () -> work.pending)

                    do! waitFor (fun () -> actions().Length = 1)

                    sendOutput (outputFor "op-other" [ "not ours" ])
                    do! Promise.sleep 20
                    Vitest.expect(actions().[0].Log.Lines.Length).toBe 0

                    // The provider is listening, because its own operation id still gets through.
                    sendOutput (outputFor "op-a" [ "ours" ])
                    do! waitFor (fun () -> actions().[0].Log.Lines.Length = 1)

                    work.resolve (Ok())
                    let! _ = running
                    ()
                })
        )
)

let private applyEvent (action: LfsRunningAction) (progress: VersionControlProgress) =
    match LfsRunningActionsState.applyProgress progress [ ("scope", action) ] with
    | [ _, next ] -> next
    | _ -> failwith "Expected the single running action."

Vitest.describe (
    "LfsRunningActionsState.applyProgress",
    fun () ->
        Vitest.test (
            "a meter event with numbers sets the step percent and a meter event without numbers clears it",
            fun () ->
                let transfer = LfsProgressPhase.MaterializeTransfer

                let running =
                    applyEvent (runningAction "op-a") (eventOf "op-a" transfer (Some 45.0) (Some 100.0))

                Vitest.expect(running.StepPercent).toEqual (Some 45.0)

                let checkout =
                    applyEvent running (eventOf "op-a" LfsProgressPhase.MaterializeCheckout (Some 10.0) (Some 100.0))

                Vitest.expect(checkout.StepPercent).toEqual (Some 10.0)

                let cleared = applyEvent checkout (eventOf "op-a" transfer None None)
                Vitest.expect(cleared.StepPercent).toEqual (None)
        )

        Vitest.test (
            "the count and the step percent are independent",
            fun () ->
                let counted = applyEvent (runningAction "op-a") (countEventFor "op-a" 4.0)

                let metered =
                    applyEvent counted (eventOf "op-a" LfsProgressPhase.MaterializeTransfer (Some 30.0) (Some 100.0))

                Vitest.expect(metered.Count).toEqual (Some(4, 10))
                Vitest.expect(metered.StepPercent).toEqual (Some 30.0)

                // A repeated event with the same numbers does not produce a new state.
                let state = [ ("scope", metered) ]

                let repeated =
                    LfsRunningActionsState.applyProgress
                        (eventOf "op-a" LfsProgressPhase.MaterializeTransfer (Some 30.0) (Some 100.0))
                        state

                Vitest.expect(obj.ReferenceEquals(repeated, state)).toBe true
        )

        Vitest.test (
            "an event of a phase that is no meter clears the step percent and keeps the count",
            fun () ->
                let metered = {
                    runningAction "op-a" with
                        Count = Some(4, 10)
                        StepPercent = Some 30.0
                }

                for phase in
                    [
                        "lfs-materialize"
                        "lfs-dematerialize"
                        "lfs-download"
                        "git"
                    ] do
                    let next = applyEvent metered (eventOf "op-a" phase (Some 9.0) (Some 10.0))

                    Vitest.expect(next.StepPercent).toEqual None
                    Vitest.expect(next.Count).toEqual (Some(4, 10))

                // A count event clears the step percent too and applies its count.
                let counted = applyEvent metered (countEventFor "op-a" 5.0)
                Vitest.expect(counted.StepPercent).toEqual None
                Vitest.expect(counted.Count).toEqual (Some(5, 10))

                // Without a step percent such an event changes nothing.
                let idle = { metered with StepPercent = None }
                let state = [ ("scope", idle) ]

                let unchanged =
                    LfsRunningActionsState.applyProgress (eventOf "op-a" "lfs-materialize" (Some 9.0) (Some 10.0)) state

                Vitest.expect(obj.ReferenceEquals(unchanged, state)).toBe true
        )
)

Vitest.describe (
    "LfsRunningAction.progressText",
    fun () ->
        let folder = {
            runningAction "op-a" with
                FileCount = 40
        }

        let single = {
            runningAction "op-a" with
                IsFolder = false
                FileCount = 1
        }

        Vitest.test (
            "a folder reads 0 of its file count before the first count event",
            fun () -> Vitest.expect(LfsRunningAction.progressText folder).toEqual (Some $"0/{folder.FileCount}")
        )

        Vitest.test (
            "a folder reads the count of the latest count event",
            fun () ->
                let counted = { folder with Count = Some(3, 38) }
                Vitest.expect(LfsRunningAction.progressText counted).toEqual (Some "3/38")
        )

        Vitest.test (
            "a folder appends the step percent rounded down",
            fun () ->
                let metered = {
                    folder with
                        Count = Some(3, 40)
                        StepPercent = Some 45.9
                }

                Vitest.expect(LfsRunningAction.progressText metered).toEqual (Some "3/40 45%")
        )

        Vitest.test (
            "a single file shows no text and no percent, even with a step percent",
            fun () ->
                let metered = { single with StepPercent = Some 7.5 }

                Vitest.expect(LfsRunningAction.progressText single).toEqual None
                Vitest.expect(LfsRunningAction.progressText metered).toEqual None
                Vitest.expect(LfsRunningAction.progressPercent metered).toEqual None
        )
)
