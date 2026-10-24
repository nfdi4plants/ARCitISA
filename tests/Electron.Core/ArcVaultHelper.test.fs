module ElectronCore.ArcVaultHelperTests

open ARCtrl
open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron
open Fable.Electron.Main
open Main.ARCtrlExtensions
open Main.ArcVault
open Main.ArcVaultHelper
open Main.ArcVaultTypes
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.Notes.NoteConstants
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.IPCTypesHelper
open Vitest

module FileImportCoordinator = Main.FileImportCoordinator
module WatcherHelpers = Main.WatcherHelpers
module Abort = Main.Bindings.Abort

let private electronMock: obj = import "__electronMock" "electron"

let private resetElectronMock () = electronMock?reset () |> ignore

let private setBrowserWindowFactory (factory: obj -> obj) =
    electronMock?setBrowserWindowFactory (factory) |> ignore

let private setBrowserWindowFromWebContents (handler: obj -> obj) =
    electronMock?setBrowserWindowFromWebContents (handler) |> ignore

let private setShowOpenDialog (handler: obj -> obj -> obj) =
    electronMock?setShowOpenDialog (handler) |> ignore

let private setShowMessageBox (handler: obj -> obj -> obj) =
    electronMock?setShowMessageBox (handler) |> ignore

let private ipcEventWithSenderId senderId : IpcMainInvokeEvent =
    createObj [ "sender" ==> createObj [ "id" ==> senderId ] ]
    |> unbox<IpcMainInvokeEvent>

let private withTempArc =
    TestHelpers.withTempArcWith "swate-watcher-merge-validation-" "WatcherMergeValidationArc"

let private watcherEvent arcPath eventName relativePath : ArcVaultFileSystemEvent = {
    EventName = eventName
    RelativePath = relativePath
    AbsolutePath = join [| arcPath; relativePath |]
}

let private mkdirWatcherDirectoryAsync (directoryPath: string) = promise {
    let! _ = mkdirAsync directoryPath (MkdirOptions(recursive = true))
    return ()
}

let private writeWatcherTextFileAsync (filePath: string) (content: string) =
    writeFileAsync filePath content TextEncoding.Utf8

let private recordingWatcherApi (loadingChanges: ResizeArray<bool>) : IArcFileWatcherApi = {
    IsLoadingChanges = fun isLoading -> loadingChanges.Add isLoading
}

type private WatcherLoadingCall = {
    IsLoading: bool
    PendingEvents: int
    PendingArcMergeEvents: int
}

let private recordingWatcherApiWithPendingState
    (vault: ArcVault)
    (loadingCalls: ResizeArray<WatcherLoadingCall>)
    : IArcFileWatcherApi =
    {
        IsLoadingChanges =
            fun isLoading ->
                loadingCalls.Add {
                    IsLoading = isLoading
                    PendingEvents = vault.fileWatcherPendingEvents.Count
                    PendingArcMergeEvents = vault.fileWatcherPendingArcMergeEvents.Count
                }
    }

let private nowMs () : float = emitJsExpr () "Date.now()"

let private windowWithStates (windowDestroyed: unit -> bool) (webContentsDestroyed: unit -> bool) (send: obj) =
    createObj [
        "id" ==> 1
        "isDestroyed" ==> windowDestroyed
        "webContents"
        ==> createObj [ "send" ==> send; "isDestroyed" ==> webContentsDestroyed ]
    ]
    |> unbox<BrowserWindow>

Vitest.describe (
    "WindowSend",
    fun () ->
        Vitest.test (
            "isAlive is false when web contents are destroyed",
            fun () ->
                let send: obj = emitJsExpr () "((..._args) => {})"

                let window = windowWithStates (fun () -> false) (fun () -> true) send

                Vitest.expect(Main.WindowSend.isAlive window).toBe false
        )

        Vitest.test (
            "sender drops messages after the window is destroyed",
            fun () ->
                let mutable windowDestroyed = false
                let mutable sendCount = 0

                let send: obj =
                    emitJsExpr (fun () -> sendCount <- sendCount + 1) "((..._args) => $0())"

                let window = windowWithStates (fun () -> windowDestroyed) (fun () -> false) send

                let deliver = Main.WindowSend.sender<IArcFileWatcherApi> window
                deliver (fun api -> api.IsLoadingChanges true)
                windowDestroyed <- true
                deliver (fun api -> api.IsLoadingChanges false)

                Vitest.expect(sendCount).toBe 1
        )
)

let private expectWatcherAssayTitle (vault: ArcVault) expectedTitle = promise {
    let titleMatches () =
        vault.arc.Value.GetAssay("DiskAssay").Title = Some expectedTitle

    let mutable attempts = 0

    while attempts < 250 && not (titleMatches ()) do
        do! Promise.sleep 20
        attempts <- attempts + 1

    Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some expectedTitle)
}

let private runFileImport
    (vault: ArcVault)
    (requestId, operation: Main.Bindings.Abort.IAbortSignal -> JS.Promise<Result<ImportExternalFilesResult, exn>>)
    =
    Main.IPC.IPCHelper.withExclusiveBusyWriting
        vault
        (fun () ->
            FileImportCoordinator.run
                vault.window
                requestId
                (fun () -> Ok())
                (fun () -> vault.activeFileImport)
                (fun value -> vault.activeFileImport <- value)
                operation
        )

Vitest.describe (
    "ArcVault merge queue",
    fun () ->
        Vitest.test (
            "nested busy scopes release the flag with the outermost scope",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())

                do!
                    Main.IPC.IPCHelper.withBusyWritingScope
                        vault
                        (fun () -> promise {
                            Vitest.expect(vault.isBusyWriting).toBe true

                            do!
                                vault.WithBusyWritingScope(fun () -> promise {
                                    Vitest.expect(vault.isBusyWriting).toBe true
                                })

                            Vitest.expect(vault.isBusyWriting).toBe true
                        })

                Vitest.expect(vault.isBusyWriting).toBe false
            }
        )

        Vitest.test (
            "active import cancellation waits for import cleanup to finish",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let mutable finishCleanup = ignore
                let mutable waitFinished = false

                let cleanupCompletion =
                    JS.Constructors.Promise.Create(fun resolve _ -> finishCleanup <- fun () -> resolve ())

                let import =
                    runFileImport
                        vault
                        ("import-request",
                         fun abortSignal -> promise {
                             do! cleanupCompletion

                             if abortSignal.aborted then
                                 return Ok ImportExternalFilesResult.Cancelled
                             else
                                 return Ok ImportExternalFilesResult.Completed
                         })

                let cancellation = promise {
                    let activeImport = vault.activeFileImport.Value
                    activeImport.AbortController.abort ()
                    let! _ = activeImport.Completion
                    waitFinished <- true
                }

                do! Promise.sleep 0
                Vitest.expect(waitFinished).toBe (false)

                finishCleanup ()
                do! cancellation
                let! importResult = import
                Vitest.expect(importResult).toEqual (Ok ImportExternalFilesResult.Cancelled)
                Vitest.expect(waitFinished).toBe (true)
            }
        )

        Vitest.test (
            "file import owns busy-write state and exposes its lifecycle until cleanup completes",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let mutable finishImport = ignore

                let importCompletion =
                    JS.Constructors.Promise.Create(fun resolve _ -> finishImport <- fun () -> resolve ())

                let import =
                    runFileImport
                        vault
                        ("coordinated-import",
                         fun _ -> promise {
                             do! importCompletion
                             return Ok ImportExternalFilesResult.Completed
                         })

                Vitest.expect(vault.isBusyWriting).toBe (true)

                Vitest
                    .expect(vault.activeFileImport |> Option.map _.State)
                    .toEqual (
                        Some {
                            requestId = "coordinated-import"
                            phase = FileImportPhase.Copying
                        }
                    )

                let activeImport = vault.activeFileImport.Value

                vault.activeFileImport <-
                    Some {
                        activeImport with
                            State = {
                                activeImport.State with
                                    phase = FileImportPhase.Finalizing
                            }
                    }

                Vitest.expect(vault.activeFileImport.Value.State.phase = FileImportPhase.Copying).toBe (false)

                Vitest.expect(vault.activeFileImport.Value.State.phase).toEqual (FileImportPhase.Finalizing)

                finishImport ()
                let! result = import
                Vitest.expect(result).toEqual (Ok ImportExternalFilesResult.Completed)
                Vitest.expect(vault.isBusyWriting).toBe (false)
                Vitest.expect(vault.activeFileImport |> Option.map _.State).toEqual (None)
            }
        )

        Vitest.test (
            "synchronously rejected file import clears its active and busy-write state",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let mutable failureMessage = None

                try
                    let! _ =
                        runFileImport vault ("rejected-import", fun _ -> raise (exn "Import failed synchronously."))

                    ()
                with error ->
                    failureMessage <- Some error.Message

                Vitest.expect(failureMessage).toEqual (Some "Import failed synchronously.")
                Vitest.expect(vault.activeFileImport).toEqual (None)
                Vitest.expect(vault.isBusyWriting).toBe (false)
            }
        )

        Vitest.test (
            "file import does not start while another ARC write owns the vault",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let mutable operationStarted = false

                do!
                    vault.WithBusyWritingScope(fun () -> promise {
                        match!
                            runFileImport
                                vault
                                ("blocked-import",
                                 fun _ ->
                                     operationStarted <- true
                                     JS.Constructors.Promise.resolve (Ok ImportExternalFilesResult.Completed))
                        with
                        | Ok _ -> return failwith "Expected the import to be rejected while the vault is busy."
                        | Error error ->
                            Vitest.expect(error.Message).toContain ("still saving")
                            Vitest.expect(operationStarted).toBe (false)
                            Vitest.expect(vault.isBusyWriting).toBe (true)
                    })

                Vitest.expect(vault.isBusyWriting).toBe (false)
            }
        )

        Vitest.test (
            "runs overlapping merges sequentially so the second observes the first result",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let executionOrder = ResizeArray<string>()
                let mutable mergeResult = 0
                let mutable releaseFirstMerge = ignore

                let firstMergeGate =
                    JS.Constructors.Promise.Create(fun resolve _ -> releaseFirstMerge <- fun () -> resolve ())

                let firstMerge =
                    vault.EnqueueArcMerge(fun () -> promise {
                        executionOrder.Add "first-start"
                        do! firstMergeGate
                        mergeResult <- 1
                        executionOrder.Add "first-end"
                    })

                let secondMerge =
                    vault.EnqueueArcMerge(fun () -> promise {
                        executionOrder.Add "second-start"
                        Vitest.expect(mergeResult).toBe (1)
                        mergeResult <- 2
                        executionOrder.Add "second-end"
                    })

                releaseFirstMerge ()
                do! firstMerge
                do! secondMerge

                Vitest
                    .expect(executionOrder.ToArray())
                    .toEqual (
                        [|
                            "first-start"
                            "first-end"
                            "second-start"
                            "second-end"
                        |]
                    )

                Vitest.expect(mergeResult).toBe (2)
            }
        )

        Vitest.test (
            "continues processing after a merge operation fails",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let mutable continued = false

                let failedMerge =
                    vault.EnqueueArcMerge(fun () -> promise { return raise (exn "Expected merge failure") })

                try
                    do! failedMerge
                with error ->
                    Vitest.expect(error.Message).toContain ("Expected merge failure")

                do! Promise.sleep 0
                let followingMerge = vault.EnqueueArcMerge(fun () -> promise { continued <- true })
                do! followingMerge
                Vitest.expect(continued).toBe (true)
            }
        )

)

type private TestWindowLoadBehavior =
    | LoadImmediately
    | FailLoadWith of exn
    | ControlLoad

type private TestWindowOptions = {
    Id: int
    InitiallyDestroyed: bool
    LoadBehavior: TestWindowLoadBehavior
    OnLoad: unit -> unit
    OnShow: unit -> unit
    OnFocus: unit -> unit
    OnSend: obj array -> unit
    OnTitleWrite: int -> string -> unit
}

type private TestWindowState = {
    Window: BrowserWindow
    LoadStarted: JS.Promise<unit>
    /// Releases a controlled renderer load so concurrency tests can hold initialization at a deterministic boundary.
    ResolveLoad: unit -> unit
    RejectLoad: exn -> unit
    Destroy: unit -> unit
    TriggerClose: unit -> unit
    IsDestroyed: unit -> bool
    WasShown: unit -> bool
    CloseHandlerAttached: unit -> bool
    ClosedHandlerAttached: unit -> bool
    LifecycleWasAttachedWhenLoadStarted: unit -> bool
    PreventedCloseCount: unit -> int
    SendsAfterDestroy: unit -> int
    TitleWritesAfterDestroy: unit -> int
    SentMessages: ResizeArray<obj array>
}

let private testWindowOptions id = {
    Id = id
    InitiallyDestroyed = false
    LoadBehavior = LoadImmediately
    OnLoad = ignore
    OnShow = ignore
    OnFocus = ignore
    OnSend = ignore
    OnTitleWrite = fun _ _ -> ()
}

let private createTestWindow options =
    let mutable destroyed = options.InitiallyDestroyed
    let mutable shown = false
    let mutable closeHandler: (obj -> unit) option = None
    let mutable closedHandler: (unit -> unit) option = None
    let mutable lifecycleWasAttachedWhenLoadStarted = false
    let mutable preventedCloseCount = 0
    let mutable sendsAfterDestroy = 0
    let mutable titleWritesAfterDestroy = 0
    let mutable titleWriteCount = 0
    let mutable title = ""
    let mutable signalLoadStarted = ignore
    let mutable resolveControlledLoad: (unit -> unit) option = None
    let mutable rejectControlledLoad: (exn -> unit) option = None
    let sentMessages = ResizeArray<obj array>()
    let noop: obj = emitJsExpr () "((..._args) => {})"

    let loadStarted =
        JS.Constructors.Promise.Create(fun resolve _ -> signalLoadStarted <- fun () -> resolve ())

    let load (_: string) =
        lifecycleWasAttachedWhenLoadStarted <- closeHandler.IsSome && closedHandler.IsSome
        options.OnLoad()
        signalLoadStarted ()

        match options.LoadBehavior with
        | LoadImmediately -> JS.Constructors.Promise.resolve ()
        | FailLoadWith error -> JS.Constructors.Promise.reject error
        | ControlLoad ->
            JS.Constructors.Promise.Create(fun resolve reject ->
                resolveControlledLoad <- Some(fun () -> resolve ())
                rejectControlledLoad <- Some(fun error -> reject error)
            )

    let triggerClosed () =
        destroyed <- true
        closedHandler |> Option.iter (fun handler -> handler ())

    let triggerClose () =
        let preventedBeforeAttempt = preventedCloseCount

        closeHandler.Value(
            createObj [
                "preventDefault" ==> (fun () -> preventedCloseCount <- preventedCloseCount + 1)
            ]
        )

        if preventedCloseCount = preventedBeforeAttempt then
            triggerClosed ()

    let onEvent (eventName: string) (handler: obj) =
        if eventName = "close" then
            closeHandler <- Some(unbox handler)
        elif eventName = "closed" then
            closedHandler <- Some(unbox handler)

    let onEventJs: obj =
        emitJsExpr onEvent "((eventName, handler) => $0(eventName, handler))"

    let send: obj =
        emitJsExpr
            (fun (args: obj array) ->
                sentMessages.Add(args)

                if destroyed then
                    sendsAfterDestroy <- sendsAfterDestroy + 1

                options.OnSend args
            )
            "((...args) => $0(args))"

    let windowObject =
        createObj [
            "id" ==> options.Id
            "isDestroyed" ==> (fun () -> destroyed)
            "destroy" ==> (fun () -> destroyed <- true)
            "close" ==> triggerClose
            "focus" ==> options.OnFocus
            "show"
            ==> (fun () ->
                shown <- true
                options.OnShow()
            )
            "loadFile" ==> load
            "loadURL" ==> load
            "on" ==> onEventJs
            "webContents"
            ==> createObj [
                "send" ==> send
                "isDestroyed" ==> (fun () -> destroyed)
                "setWindowOpenHandler" ==> noop
                "on" ==> noop
                "openDevTools" ==> noop
            ]
        ]

    let setTitle (value: string) =
        titleWriteCount <- titleWriteCount + 1

        if destroyed then
            titleWritesAfterDestroy <- titleWritesAfterDestroy + 1
            failwith "Object has been destroyed"

        options.OnTitleWrite titleWriteCount value
        title <- value

    emitJsExpr
        (windowObject, (fun () -> title), setTitle)
        "Object.defineProperty($0, 'title', { configurable: true, get: () => $1(), set: value => $2(value) })"
    |> ignore

    {
        Window = windowObject |> unbox<BrowserWindow>
        LoadStarted = loadStarted
        ResolveLoad = fun () -> resolveControlledLoad.Value()
        RejectLoad = fun error -> rejectControlledLoad.Value(error)
        Destroy = fun () -> destroyed <- true
        TriggerClose = triggerClose
        IsDestroyed = fun () -> destroyed
        WasShown = fun () -> shown
        CloseHandlerAttached = fun () -> closeHandler.IsSome
        ClosedHandlerAttached = fun () -> closedHandler.IsSome
        LifecycleWasAttachedWhenLoadStarted = fun () -> lifecycleWasAttachedWhenLoadStarted
        PreventedCloseCount = fun () -> preventedCloseCount
        SendsAfterDestroy = fun () -> sendsAfterDestroy
        TitleWritesAfterDestroy = fun () -> titleWritesAfterDestroy
        SentMessages = sentMessages
    }

let private waitUntilAttemptLimit = 250

let rec private waitUntilWithin phase predicate remaining = promise {
    if predicate () then
        return ()
    elif remaining <= 0 then
        return failwithf "Timed out waiting for expected ARC lifecycle phase: %s." phase
    else
        do! Promise.sleep 0
        return! waitUntilWithin phase predicate (remaining - 1)
}

let private waitUntil phase predicate =
    waitUntilWithin phase predicate waitUntilAttemptLimit

let private withAsyncCleanup cleanup operation = promise {
    let! operationResult = operation () |> Promise.result
    let! cleanupResult = cleanup () |> Promise.result

    match operationResult, cleanupResult with
    | Ok value, Ok() -> return value
    | Error operationError, _ -> return raise operationError
    | Ok _, Error cleanupError -> return raise cleanupError
}

let private expectRegistrationLoadFailure
    (expectedError: exn)
    (vaults: ArcVaults)
    (windowId: int)
    (isDestroyed: unit -> bool)
    (registration: unit -> JS.Promise<'T>)
    =
    promise {
        let mutable capturedError: exn option = None

        try
            let! _ = registration ()
            ()
        with error ->
            capturedError <- Some error

        Vitest.expect(capturedError.Value).toBe (expectedError)
        Vitest.expect(vaults.Vaults.ContainsKey(windowId)).toBe (false)
        Vitest.expect(isDestroyed ()).toBe (true)
    }

Vitest.describe (
    "watcher merge validation",
    fun () ->
        Vitest.test (
            "a write that starts while the watcher merge waits in the queue defers it",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        let mutable watcherMergeBarrierCalled = false

                        vault.WatcherMergeBarrier <-
                            Some(fun () ->
                                watcherMergeBarrierCalled <- true
                                promise { return () }
                            )

                        let mutable releaseFirstMerge = ignore

                        let firstMergeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseFirstMerge <- fun () -> resolve ())

                        let firstMerge = vault.EnqueueArcMerge(fun () -> firstMergeGate)

                        let watcherEvents = [
                            watcherEvent arcPath "change" "assays/DiskAssay/isa.assay.xlsx"
                        ]

                        let watcherMerge = vault.TryApplyWatcherArcMergeIfEligible watcherEvents

                        let mutable releaseWrite = ignore

                        let writeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                        let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)
                        releaseFirstMerge ()
                        do! firstMerge

                        match! watcherMerge with
                        | WatcherMergeOutcome.Deferred -> ()
                        | _ -> return failwith "The watcher merge should defer while the write is busy."

                        Vitest.expect(watcherMergeBarrierCalled).toBe (false)
                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Old title")

                        releaseWrite ()
                        do! writeScope
                        do! Promise.sleep 600

                        vault.WatcherMergeBarrier <- None

                        match! vault.TryApplyWatcherArcMergeIfEligible watcherEvents with
                        | WatcherMergeOutcome.Applied -> ()
                        | _ -> return failwith "The watcher merge should apply after the write suppression ends."

                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Changed on disk")
                    })
        )

        Vitest.test (
            "a write that starts and ends while the snapshot loads defers it even after suppression",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        vault.WatcherMergeBarrier <-
                            Some(fun () -> promise {
                                do! vault.WithBusyWritingScope(fun () -> promise { return () })

                                let mutable attempts = 0

                                while not vault.IsFileWatcherArcMergeEligible && attempts < 200 do
                                    do! Promise.sleep 20
                                    attempts <- attempts + 1

                                if not vault.IsFileWatcherArcMergeEligible then
                                    return failwith "Watcher merge eligibility did not return after 200 polls."
                            })

                        match!
                            vault.TryApplyWatcherArcMergeIfEligible [
                                watcherEvent arcPath "change" "assays/DiskAssay/isa.assay.xlsx"
                            ]
                        with
                        | WatcherMergeOutcome.Deferred -> ()
                        | _ -> return failwith "The watcher merge should defer after the write generation changes."

                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Old title")
                        Vitest.expect(vault.IsFileWatcherArcMergeEligible).toBe (true)
                    })
        )

        Vitest.test (
            "an unlink admitted before a write that recreated the file merges as a change",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        match!
                            vault.TryApplyWatcherArcMergeIfEligible [
                                watcherEvent arcPath "unlink" "assays/DiskAssay/isa.assay.xlsx"
                            ]
                        with
                        | WatcherMergeOutcome.Applied -> ()
                        | _ -> return failwith "The recreated assay should apply as a change."

                        Vitest.expect(vault.arc.Value.ContainsAssay("DiskAssay")).toBe (true)
                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Changed on disk")
                    })
        )

        Vitest.test (
            "a stale directory delete keeps the entity whose canonical file came back and drops the one that did not",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddAssay(ArcAssay("A1"))
                        arc.AddAssay(ArcAssay("A2"))
                    )
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let a1Folder = join [| arcPath; "assays"; "A1" |]
                        let a2Folder = join [| arcPath; "assays"; "A2" |]
                        do! rmAsync a1Folder (RmOptions(recursive = true, force = true))
                        do! rmAsync a2Folder (RmOptions(recursive = true, force = true))

                        let persistedArc = ARC("WatcherMergeValidationArc")
                        persistedArc.AddAssay(ArcAssay("A1"))

                        match! persistedArc.TryWriteAsyncSwate arcPath with
                        | Error _ -> return failwith "Could not recreate the canonical A1 file."
                        | Ok _ -> ()

                        do! mkdirWatcherDirectoryAsync a2Folder

                        match!
                            vault.TryApplyWatcherArcMergeIfEligible [
                                watcherEvent arcPath "unlinkDir" "assays/A1"
                                watcherEvent arcPath "unlinkDir" "assays/A2"
                            ]
                        with
                        | WatcherMergeOutcome.Applied -> ()
                        | _ -> return failwith "The stale directory deletes should apply."

                        Vitest.expect(vault.arc.Value.ContainsAssay("A1")).toBe (true)
                        Vitest.expect(vault.arc.Value.ContainsAssay("A2")).toBe (false)
                    })
        )

        Vitest.test (
            "a change for a file that is missing at merge time is dropped",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Persisted title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        vault.arc.Value.GetAssay("DiskAssay").Title <- Some "Unsaved title"

                        let assayFile = join [| arcPath; "assays"; "DiskAssay"; "isa.assay.xlsx" |]
                        do! rmAsync assayFile (RmOptions(force = true))

                        match!
                            vault.TryApplyWatcherArcMergeIfEligible [
                                watcherEvent arcPath "change" "assays/DiskAssay/isa.assay.xlsx"
                            ]
                        with
                        | WatcherMergeOutcome.Applied -> ()
                        | _ -> return failwith "The missing-file change should apply with an empty ARC batch."

                        Vitest.expect(vault.arc.Value.ContainsAssay("DiskAssay")).toBe (true)
                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Unsaved title")
                    })
        )

        Vitest.test (
            "normalizeAgainstDisk keeps directory deletes and drops missing-file changes",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-watcher-normalize-"

                try
                    let existingDirectory = join [| rootPath; "existing-directory" |]
                    let existingFile = join [| rootPath; "existing-file.txt" |]
                    let missingFile = join [| rootPath; "missing-file.txt" |]
                    do! mkdirWatcherDirectoryAsync existingDirectory
                    do! writeWatcherTextFileAsync existingFile "existing"

                    let directoryDelete = watcherEvent rootPath "unlinkDir" "existing-directory"
                    let missingChange = watcherEvent rootPath "change" "missing-file.txt"
                    let existingUnlink = watcherEvent rootPath "unlink" "existing-file.txt"

                    let absoluteEvent: ArcVaultFileSystemEvent = {
                        EventName = "change"
                        RelativePath = existingFile
                        AbsolutePath = existingFile
                    }

                    let normalized =
                        WatcherHelpers.normalizeAgainstDisk [
                            directoryDelete
                            missingChange
                            existingUnlink
                            absoluteEvent
                        ]

                    Vitest.expect(normalized |> List.exists (fun event -> event = directoryDelete)).toBe (true)
                    Vitest.expect(normalized |> List.exists (fun event -> event = missingChange)).toBe (false)

                    let existingFileEvent =
                        normalized
                        |> List.find (fun event -> event.RelativePath = existingUnlink.RelativePath)

                    Vitest.expect(existingFileEvent.EventName).toBe ("change")
                    Vitest.expect(normalized |> List.exists (fun event -> event = absoluteEvent)).toBe (true)

                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "normalizeAgainstDisk keeps case-only rename unlinks",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-watcher-case-rename-"

                try
                    let oldFilePath = join [| rootPath; "sample_metadata_neg.csv" |]
                    let newFilePath = join [| rootPath; "Sample_Metadata_NEG.csv" |]
                    let existingFilePath = join [| rootPath; "existing-file.txt" |]
                    do! writeWatcherTextFileAsync oldFilePath "renamed"
                    do! writeWatcherTextFileAsync existingFilePath "existing"
                    do! renameAsync oldFilePath newFilePath

                    let caseOnlyUnlink = watcherEvent rootPath "unlink" "sample_metadata_neg.csv"
                    let existingUnlink = watcherEvent rootPath "unlink" "existing-file.txt"

                    let normalized =
                        WatcherHelpers.normalizeAgainstDisk [ caseOnlyUnlink; existingUnlink ]

                    Vitest.expect(normalized |> List.exists (fun event -> event = caseOnlyUnlink)).toBe (true)

                    let existingFileEvent =
                        normalized
                        |> List.find (fun event -> event.RelativePath = existingUnlink.RelativePath)

                    Vitest.expect(existingFileEvent.EventName).toBe ("change")

                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "overlapping tree updates keep both entries",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath

                        do! vault.EnqueueFileTreeScan false

                        let firstPath = join [| arcPath; "tree-first.txt" |]
                        let secondPath = join [| arcPath; "tree-second.txt" |]
                        do! writeWatcherTextFileAsync firstPath "first"
                        do! writeWatcherTextFileAsync secondPath "second"

                        let firstUpdate =
                            vault.ApplyWatcherFileTreeEvents [ watcherEvent arcPath "add" "tree-first.txt" ]

                        let secondUpdate =
                            vault.ApplyWatcherFileTreeEvents [ watcherEvent arcPath "add" "tree-second.txt" ]

                        do! firstUpdate
                        do! secondUpdate

                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(firstPath)).IsSome).toBe (true)
                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(secondPath)).IsSome).toBe (true)
                    })
        )

        Vitest.test (
            "a snapshot loaded before a rename is not published",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        vault.WatcherMergeBarrier <-
                            Some(fun () ->
                                vault.ClearPendingFileWatcherState()
                                promise { return () }
                            )

                        match!
                            vault.TryApplyWatcherArcMergeIfEligible [
                                watcherEvent arcPath "change" "assays/DiskAssay/isa.assay.xlsx"
                            ]
                        with
                        | WatcherMergeOutcome.Deferred -> ()
                        | _ -> return failwith "The watcher merge should defer after the pending state reset."

                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Old title")
                    })
        )

        Vitest.test (
            "the import merge inside a busy scope still applies",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        do!
                            vault.WithBusyWritingScope(fun () -> promise {
                                match!
                                    vault.TryTriggerArcInMemoryMergeOnFileWatcherEvents [
                                        watcherEvent arcPath "change" "assays/DiskAssay/isa.assay.xlsx"
                                    ]
                                with
                                | Ok() ->
                                    Vitest
                                        .expect(vault.arc.Value.GetAssay("DiskAssay").Title)
                                        .toEqual (Some "Changed on disk")
                                | Error mergeError -> return raise mergeError
                            })
                    })
        )

        Vitest.test (
            "the reload keeps admitted events while a write runs and merges them after it",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        let loadingChanges = ResizeArray<bool>()

                        let handleFileEvent =
                            vault._FileEventController (recordingWatcherApi loadingChanges)

                        let assayPath = join [| arcPath; "assays/DiskAssay/isa.assay.xlsx" |]
                        handleFileEvent "change" assayPath

                        let mutable releaseWrite = ignore

                        let writeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                        let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)

                        do! Promise.sleep 700
                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Old title")

                        Vitest
                            .expect(
                                vault.fileWatcherPendingArcMergeEvents
                                |> Seq.exists (fun event -> event.RelativePath = "assays/DiskAssay/isa.assay.xlsx")
                            )
                            .toBe (true)

                        releaseWrite ()
                        do! writeScope
                        do! expectWatcherAssayTitle vault "Changed on disk"

                        // The merge publishes before the callback applies the tree batch and clears the loading flag.
                        let mutable flagAttempts = 0

                        while flagAttempts < 250
                              && (loadingChanges.Count = 0 || loadingChanges.[loadingChanges.Count - 1]) do
                            do! Promise.sleep 20
                            flagAttempts <- flagAttempts + 1

                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe (0)
                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe (0)
                        Vitest.expect(loadingChanges.[loadingChanges.Count - 1]).toBe (false)
                    })
        )

        Vitest.test (
            "a deferred reload keeps the tree batch and retries",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        let mutable releaseMergeQueue = ignore

                        let mergeQueueGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseMergeQueue <- fun () -> resolve ())

                        let queuedMerge = vault.EnqueueArcMerge(fun () -> mergeQueueGate)
                        let loadingChanges = ResizeArray<bool>()

                        let handleFileEvent =
                            vault._FileEventController (recordingWatcherApi loadingChanges)

                        let handlerLoadingCallCount = loadingChanges.Count
                        let assayPath = join [| arcPath; "assays/DiskAssay/isa.assay.xlsx" |]
                        handleFileEvent "change" assayPath

                        do! Promise.sleep 600

                        let mutable releaseWrite = ignore

                        let writeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                        let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)
                        releaseMergeQueue ()
                        do! queuedMerge
                        do! Promise.sleep 100

                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count > 0).toBe (true)
                        Vitest.expect(vault.fileWatcherPendingEvents.Count > 0).toBe (true)

                        let falseRecordedSinceHandler =
                            loadingChanges
                            |> Seq.skip handlerLoadingCallCount
                            |> Seq.exists (fun isLoading -> not isLoading)

                        Vitest.expect(falseRecordedSinceHandler).toBe (false)

                        releaseWrite ()
                        do! writeScope
                        do! expectWatcherAssayTitle vault "Changed on disk"

                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe (0)
                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe (0)
                    })
        )

        Vitest.test (
            "a deferred batch is merged when an overlapping reload applies",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        let assayPath = join [| arcPath; "assays/DiskAssay/isa.assay.xlsx" |]
                        let loadingCalls = ResizeArray<WatcherLoadingCall>()
                        let mutable handleFileEvent: string -> string -> unit = fun _ _ -> ()
                        let mutable barrierCallCount = 0

                        handleFileEvent <-
                            vault._FileEventController (recordingWatcherApiWithPendingState vault loadingCalls)

                        vault.WatcherMergeBarrier <-
                            Some(fun () -> promise {
                                if barrierCallCount = 0 then
                                    barrierCallCount <- barrierCallCount + 1
                                    handleFileEvent "change" assayPath
                                    let secondHandlerStartedAt = nowMs ()
                                    do! vault.WithBusyWritingScope(fun () -> promise { return () })

                                    let mutable attempts = 0

                                    while attempts < 150
                                          && (not vault.IsFileWatcherArcMergeEligible
                                              || nowMs () - secondHandlerStartedAt < 1100.) do
                                        do! Promise.sleep 20
                                        attempts <- attempts + 1

                                    if
                                        not vault.IsFileWatcherArcMergeEligible
                                        || nowMs () - secondHandlerStartedAt < 1100.
                                    then
                                        return failwith "The overlapping reload did not pass the suppression window."
                                else
                                    ()
                            })

                        handleFileEvent "change" assayPath

                        let mutable attempts = 0

                        while attempts < 300
                              && (loadingCalls.Count = 0 || loadingCalls.[loadingCalls.Count - 1].IsLoading) do
                            do! Promise.sleep 20
                            attempts <- attempts + 1

                        if loadingCalls.Count = 0 then
                            return failwith "The watcher controller did not report a loading state."

                        let falseWithPendingEvents =
                            loadingCalls
                            |> Seq.exists (fun call ->
                                not call.IsLoading && (call.PendingEvents > 0 || call.PendingArcMergeEvents > 0)
                            )

                        Vitest.expect(barrierCallCount > 0).toBe (true)
                        Vitest.expect(loadingCalls.[loadingCalls.Count - 1].IsLoading).toBe (false)
                        Vitest.expect(falseWithPendingEvents).toBe (false)
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe (0)
                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe (0)
                        Vitest.expect(vault.fileWatcherReloadArcTimeout).toEqual (None)
                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Changed on disk")
                    })
        )

        Vitest.test (
            "a busy reload with nothing pending finishes",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let loadingChanges = ResizeArray<bool>()

                        let handleFileEvent =
                            vault._FileEventController (recordingWatcherApi loadingChanges)

                        let mutable releaseWrite = ignore

                        let writeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                        let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)
                        handleFileEvent "change" (join [| arcPath; "assays/DiskAssay/isa.assay.xlsx" |])

                        // The handler admitted the event to the tree list. Dropping it makes the reload fire
                        // with nothing pending while the write is still open.
                        vault.fileWatcherPendingEvents.Clear()
                        vault.fileWatcherPendingArcMergeEvents.Clear()

                        do! Promise.sleep 700

                        Vitest.expect(loadingChanges.Count > 0).toBe (true)
                        Vitest.expect(loadingChanges.[loadingChanges.Count - 1]).toBe (false)
                        Vitest.expect(vault.fileWatcherReloadArcTimeout).toEqual (None)

                        releaseWrite ()
                        do! writeScope
                    })
        )

        Vitest.test (
            "a reload deferred three times during a write publishes the tree and merges the ARC after it",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc
                        do! vault.EnqueueFileTreeScan false

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        let treeEntryPath = join [| arcPath; "watcher-deferred-tree.txt" |]
                        do! writeWatcherTextFileAsync treeEntryPath "tree entry"

                        let loadingChanges = ResizeArray<bool>()

                        let handleFileEvent =
                            vault._FileEventController (recordingWatcherApi loadingChanges)

                        let assayPath = join [| arcPath; "assays/DiskAssay/isa.assay.xlsx" |]
                        handleFileEvent "change" assayPath
                        handleFileEvent "add" treeEntryPath

                        let mutable releaseWrite = ignore

                        let writeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                        let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)

                        try
                            let waitStartedAt = nowMs ()
                            let mutable attempts = 0

                            // Watcher events carry normalized paths, so the tree key uses forward slashes.
                            let treeHasEntry () =
                                let expectedKey = PathHelpers.normalizePath treeEntryPath

                                vault.FileTreeWriter.Tree.Entries
                                |> Seq.exists (fun entry -> PathHelpers.normalizePath entry.path = expectedKey)

                            let fallbackPublished () =
                                vault.HasReachedWatcherDeferralLimit
                                && treeHasEntry ()
                                && vault.fileWatcherPendingArcMergeEvents.Count > 0

                            while attempts < 80
                                  && (nowMs () - waitStartedAt < 2300.0 || not (fallbackPublished ())) do
                                do! Promise.sleep 50
                                attempts <- attempts + 1

                            Vitest.expect(vault.HasReachedWatcherDeferralLimit).toBe (true)
                            Vitest.expect(treeHasEntry ()).toBe (true)
                            Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count > 0).toBe (true)
                            Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Old title")
                        finally
                            releaseWrite ()

                        do! writeScope
                        do! expectWatcherAssayTitle vault "Changed on disk"

                        Vitest.expect(vault.HasReachedWatcherDeferralLimit).toBe (false)
                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe (0)
                    })
        )

        Vitest.test (
            "a reload whose batch went stale finishes",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DiskAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        let loadingChanges = ResizeArray<bool>()

                        let handleFileEvent =
                            vault._FileEventController (recordingWatcherApi loadingChanges)

                        vault.WatcherMergeBarrier <-
                            Some(fun () ->
                                vault.ClearPendingFileWatcherState()
                                promise { return () }
                            )

                        handleFileEvent "change" (join [| arcPath; "assays/DiskAssay/isa.assay.xlsx" |])

                        let mutable attempts = 0

                        while attempts < 250
                              && (loadingChanges.Count = 0 || loadingChanges.[loadingChanges.Count - 1]) do
                            do! Promise.sleep 20
                            attempts <- attempts + 1

                        Vitest.expect(loadingChanges.[loadingChanges.Count - 1]).toBe (false)
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe (0)
                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe (0)
                        Vitest.expect(vault.arc.Value.GetAssay("DiskAssay").Title).toEqual (Some "Old title")
                    })
        )

        Vitest.test (
            "a tree unlink for a path that exists again keeps the entry",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath

                        do! vault.EnqueueFileTreeScan false

                        let filePath = join [| arcPath; "tree-kept.txt" |]
                        do! writeWatcherTextFileAsync filePath "kept"

                        do! vault.ApplyWatcherFileTreeEvents [ watcherEvent arcPath "add" "tree-kept.txt" ]
                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(filePath)).IsSome).toBe (true)

                        do! vault.ApplyWatcherFileTreeEvents [ watcherEvent arcPath "unlink" "tree-kept.txt" ]
                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(filePath)).IsSome).toBe (true)

                        do! rmAsync filePath (RmOptions(force = true))
                        do! vault.ApplyWatcherFileTreeEvents [ watcherEvent arcPath "unlink" "tree-kept.txt" ]
                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(filePath)).IsSome).toBe (false)
                    })
        )

        Vitest.test (
            "a tree update queued before a state reset does not publish",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath

                        do! vault.EnqueueFileTreeScan false

                        let filePath = join [| arcPath; "tree-reset.txt" |]
                        do! writeWatcherTextFileAsync filePath "reset"

                        let mutable releaseTail = ignore

                        let tailGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseTail <- fun () -> resolve ())

                        // The update waits behind the tail while the pending state is reset.
                        vault.FileTreeWriter.Enqueue(fun _ -> promise {
                            do! tailGate
                            return false
                        })
                        |> ignore

                        let update =
                            vault.ApplyWatcherFileTreeEvents [ watcherEvent arcPath "add" "tree-reset.txt" ]

                        vault.ClearPendingFileWatcherState()
                        releaseTail ()
                        do! update

                        Vitest.expect((vault.FileTreeWriter.Tree.TryFind(filePath)).IsSome).toBe (false)
                    })
        )

)

let private lifecycleTestWindow id isDestroyed onSend =
    // The remoting proxy calls webContents.send with channel and payload arguments.
    // Discard those transport details so lifecycle tests only observe whether a send occurred.
    let send: obj = emitJsExpr onSend "((..._args) => $0())"

    // ArcVault only needs this subset of BrowserWindow for lifecycle broadcasts. Keeping the
    // fixture minimal avoids constructing a real Electron window in the Vitest environment.
    createObj [
        "id" ==> id
        "isDestroyed" ==> (fun () -> isDestroyed)
        "webContents"
        ==> createObj [
            "send" ==> send
            "isDestroyed" ==> (fun () -> isDestroyed)
        ]
    ]
    |> unbox<BrowserWindow>

let private mkdirRecursiveAsync (directoryPath: string) = promise {
    let! _ = mkdirAsync directoryPath (MkdirOptions(recursive = true))
    return ()
}

let private writeTextFileAsync (filePath: string) (content: string) =
    writeFileAsync filePath content TextEncoding.Utf8

let private arctrlDefaultGitignoreContent () =
    match ARCtrl.Contract.Git.gitignoreContract.DTO with
    | Some(ARCtrl.Contract.DTO.Text content) -> content
    | _ -> failwith "ARCtrl default .gitignore contract does not contain text content."

let private addDataMapToAllEntityTypes (arc: ARC) =
    let study = ArcStudy("Study With DataMap")
    study.DataMap <- Some(DataMap.init ())
    arc.AddStudy(study)

    let assay = ArcAssay("Assay With DataMap")
    assay.DataMap <- Some(DataMap.init ())
    arc.AddAssay(assay)

    let workflow = ArcWorkflow("Workflow With DataMap")
    workflow.DataMap <- Some(DataMap.init ())
    arc.AddWorkflow(workflow)

    let run = ArcRun("Run With DataMap")
    run.DataMap <- Some(DataMap.init ())
    arc.AddRun(run)

let private tryGetDirtyStateMessage (args: obj array) =
    if args.Length = 2 && (string args.[0]).Contains("arcUnsavedChangesUpdate") then
        Some(unbox<bool> args.[1])
    else
        None

let private isPathChangeMessage (args: obj array) =
    args.Length > 0 && (string args.[0]).Contains("pathChange")

let private isFileTreeMessage (args: obj array) =
    args.Length > 0 && (string args.[0]).Contains("fileTreeUpdate")

Vitest.describe (
    "ArcVaultHelper",
    fun () ->
        Vitest.afterEach (fun () -> resetElectronMock ())

        Vitest.test (
            "BrowserWindow factory preserves title property descriptors",
            fun () ->
                let mutable writtenTitle = ""
                let factoryWindow = createObj []

                emitJsExpr
                    (factoryWindow, fun (value: string) -> writtenTitle <- value)
                    "Object.defineProperty($0, 'title', { configurable: true, get: () => '', set: value => $1(value) })"
                |> ignore

                setBrowserWindowFactory (fun _ -> factoryWindow)

                let window = BrowserWindow()
                window.title <- "Descriptor-preserving title"

                Vitest.expect(writtenTitle).toBe ("Descriptor-preserving title")
        )

        Vitest.test (
            "DataMap add synchronization preserves the persisted static-hash baseline",
            fun () ->
                let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay
                let persistedDataMap = DataMap.init ()
                persistedDataMap.StaticHash <- 123
                let persistedAssay = ArcAssay("DataMapAssay")
                persistedAssay.DataMap <- Some persistedDataMap
                let persistedArc = ARC("PersistedArc")
                persistedArc.AddAssay persistedAssay

                let localDataMap = DataMap.init ()
                localDataMap.StaticHash <- 456
                let localAssay = ArcAssay("DataMapAssay")
                localAssay.DataMap <- Some localDataMap
                let localArc = ARC("LocalArc")
                localArc.AddAssay localAssay

                syncAddedArcFileFromPersisted
                    persistedArc
                    localArc
                    (ArcFiles.DataMap(Some parentInfo, persistedDataMap))

                Vitest.expect(persistedDataMap.StaticHash).toBe (123)
                Vitest.expect(persistedArc.GetAssay("DataMapAssay").DataMap.Value.StaticHash).toBe (123)
                Vitest.expect(localArc.GetAssay("DataMapAssay").DataMap.Value.StaticHash).toBe (123)
        )

        Vitest.test (
            "recent ARC broadcasts skip windows destroyed during simultaneous shutdown",
            fun () ->
                let mutable aliveWindowSendCount = 0

                let aliveWindow =
                    (createTestWindow {
                        testWindowOptions 1 with
                            OnSend = fun _ -> aliveWindowSendCount <- aliveWindowSendCount + 1
                    })
                        .Window

                let destroyedWindow =
                    (createTestWindow {
                        testWindowOptions 2 with
                            InitiallyDestroyed = true
                            OnSend = fun _ -> failwith "Destroyed window received an IPC message."
                    })
                        .Window

                let vaults = ArcVaults()
                vaults.Vaults.Add(aliveWindow.id, ArcVault(aliveWindow))
                vaults.Vaults.Add(destroyedWindow.id, ArcVault(destroyedWindow))

                vaults.BroadcastRecentARCs()

                Vitest.expect(aliveWindowSendCount).toBe (1)
        )

        Vitest.test (
            "RegisterVault cleans up when renderer loading fails",
            fun () -> promise {
                let windowId = 11
                let loadError = exn "Expected renderer load failure"

                let windowState =
                    createTestWindow {
                        testWindowOptions windowId with
                            LoadBehavior = FailLoadWith loadError
                    }

                setBrowserWindowFactory (fun _ -> windowState.Window :> obj)

                let vaults = ArcVaults()
                let mutable windowWasAliveWhenFailureWasReported = false

                do!
                    expectRegistrationLoadFailure
                        loadError
                        vaults
                        windowId
                        windowState.IsDestroyed
                        (fun () ->
                            vaults.RegisterVault(
                                onFailureBeforeCleanup =
                                    fun _ -> windowWasAliveWhenFailureWasReported <- not (windowState.IsDestroyed())
                            )
                        )

                Vitest.expect(windowWasAliveWhenFailureWasReported).toBe (true)
            }
        )

        Vitest.test (
            "RegisterVault shows a successfully loaded renderer and keeps its vault registered",
            fun () -> promise {
                let windowId = 14
                let mutable constructorOptions: obj option = None

                let windowState = createTestWindow (testWindowOptions windowId)

                setBrowserWindowFactory (fun options ->
                    constructorOptions <- Some options
                    windowState.Window :> obj
                )

                let vaults = ArcVaults()
                let! registeredWindowId = vaults.RegisterVault()

                Vitest.expect(registeredWindowId).toBe (windowId)
                Vitest.expect(constructorOptions.IsSome).toBe (true)
                Vitest.expect(constructorOptions.Value?show).toBe (false)
                Vitest.expect(windowState.WasShown()).toBe (true)
                Vitest.expect(vaults.Vaults.ContainsKey(windowId)).toBe (true)
                Vitest.expect(windowState.IsDestroyed()).toBe (false)
            }
        )

        Vitest.test (
            "OpenOrFocusArc rejects an invalid target before creating a window for an occupied caller",
            fun () -> promise {
                let! invalidTargetPath = TestHelpers.createTempDirectoryAsync "swate-open-invalid-occupied-"
                let callingWindowId = 42
                let targetWindowId = 43
                let existingArcPath = "C:/already-open-validation-guard"
                let callingWindow = (createTestWindow (testWindowOptions callingWindowId)).Window
                let callingVault = ArcVault(callingWindow)
                let existingArc = ARC("Existing ARC")
                let seededEntry = FileEntry.create ("existing.txt", "existing.txt", false)
                let mutable createdWindowCount = 0

                callingVault.path <- Some existingArcPath
                callingVault.SetArc(existingArc)
                callingVault.FileTreeWriter.Tree.SetRoot seededEntry

                let targetWindow = (createTestWindow (testWindowOptions targetWindowId)).Window

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    targetWindow :> obj
                )

                let vaults = ArcVaults()
                vaults.Vaults.Add(callingWindowId, callingVault)
                let mutable openError: exn option = None

                try
                    try
                        let! _ = vaults.OpenOrFocusArc(callingWindowId, invalidTargetPath)
                        ()
                    with error ->
                        openError <- Some error

                    Vitest.expect(openError.IsSome).toBe (true)
                    Vitest.expect(openError.Value.Message).toContain (ARCtrl.ArcPathHelper.InvestigationFileName)
                    Vitest.expect(createdWindowCount).toBe (0)
                    Vitest.expect(vaults.TryGetVault(callingWindowId)).toEqual (Some callingVault)
                    Vitest.expect(callingVault.path).toEqual (Some existingArcPath)
                    Vitest.expect(callingVault.arc.IsSome).toBe (true)
                    Vitest.expect(callingVault.arc.Value).toBe (existingArc)
                    Vitest.expect(callingVault.watcher).toEqual (None)
                    Vitest.expect(callingVault.FileTreeWriter.Tree.Count).toBe (1)
                    Vitest.expect((callingVault.FileTreeWriter.Tree.TryFind(seededEntry.path)).IsSome).toBe (true)

                    vaults.Vaults.Remove(callingWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync invalidTargetPath
                with error ->
                    vaults.Vaults.Remove(callingWindowId) |> ignore
                    vaults.Vaults.Remove(targetWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync invalidTargetPath
                    return raise error
            }
        )

        Vitest.test (
            "concurrent OpenOrFocusArc requests create exactly one owner for the same ARC",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-concurrent-"
                    "Concurrent Open ARC"
                    ignore
                    (fun arcPath -> promise {
                        let callingWindowId = 49
                        let normalizedArcPath = PathHelpers.normalizePath arcPath
                        let mutable createdWindowCount = 0

                        let callingWindow = (createTestWindow (testWindowOptions callingWindowId)).Window

                        let callingVault = ArcVault(callingWindow)
                        let vaults = ArcVaults()
                        vaults.Vaults.Add(callingWindowId, callingVault)

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            failwith "Concurrent opens of one ARC must not create a second BrowserWindow."
                        )

                        let cleanup () = promise {
                            do! callingVault.StopFileWatcher()
                            vaults.Vaults.Remove(callingWindowId) |> ignore
                        }

                        let operation () = promise {
                            // Both promises begin validation before either filesystem validation has completed.
                            let firstOpen = vaults.OpenOrFocusArc(callingWindowId, normalizedArcPath)
                            let secondOpen = vaults.OpenOrFocusArc(callingWindowId, normalizedArcPath)
                            let! firstDisposition = firstOpen
                            let! secondDisposition = secondOpen

                            let dispositions = [| firstDisposition; secondDisposition |]

                            let openedCount =
                                dispositions
                                |> Array.filter (
                                    function
                                    | ArcOpenDisposition.OpenedInCurrent path when path = normalizedArcPath -> true
                                    | _ -> false
                                )
                                |> Array.length

                            let focusedCount =
                                dispositions
                                |> Array.filter (
                                    function
                                    | ArcOpenDisposition.FocusedExisting path when path = normalizedArcPath -> true
                                    | _ -> false
                                )
                                |> Array.length

                            let ownerCount =
                                vaults.Vaults.Values
                                |> Seq.filter (fun vault ->
                                    vault.path
                                    |> Option.exists (fun path -> PathHelpers.pathsEqual path normalizedArcPath)
                                )
                                |> Seq.length

                            Vitest.expect(openedCount).toBe (1)
                            Vitest.expect(focusedCount).toBe (1)
                            Vitest.expect(ownerCount).toBe (1)
                            Vitest.expect(createdWindowCount).toBe (0)

                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "concurrent OpenOrFocusArc requests use one new-window owner for an occupied caller",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-concurrent-window-"
                    "Concurrent Window ARC"
                    ignore
                    (fun arcPath -> promise {
                        let callingWindowId = 50
                        let targetWindowId = 51
                        let normalizedArcPath = PathHelpers.normalizePath arcPath

                        let callingVault =
                            ArcVault((createTestWindow (testWindowOptions callingWindowId)).Window)

                        callingVault.path <- Some "C:/occupied-concurrent-open"
                        callingVault.SetArc(ARC("Occupied"))

                        let targetWindow =
                            createTestWindow {
                                testWindowOptions targetWindowId with
                                    LoadBehavior = ControlLoad
                            }

                        let mutable createdWindowCount = 0

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            targetWindow.Window :> obj
                        )

                        let vaults = ArcVaults()
                        vaults.Vaults.Add(callingWindowId, callingVault)

                        let cleanup () = promise {
                            match vaults.TryGetVault(targetWindowId) with
                            | Some vault -> do! vault.StopFileWatcher()
                            | None -> ()

                            vaults.Vaults.Clear()
                        }

                        let operation () = promise {
                            let firstOpen = vaults.OpenOrFocusArc(callingWindowId, normalizedArcPath)
                            let secondOpen = vaults.OpenOrFocusArc(callingWindowId, normalizedArcPath)
                            do! targetWindow.LoadStarted
                            Vitest.expect(createdWindowCount).toBe (1)
                            targetWindow.ResolveLoad()

                            let! firstDisposition = firstOpen
                            let! secondDisposition = secondOpen
                            let dispositions = [| firstDisposition; secondDisposition |]

                            let openedInNewWindow =
                                dispositions
                                |> Array.exists (
                                    function
                                    | ArcOpenDisposition.OpenedInNewWindow path ->
                                        PathHelpers.pathsEqual path normalizedArcPath
                                    | _ -> false
                                )

                            let focusedExisting =
                                dispositions
                                |> Array.exists (
                                    function
                                    | ArcOpenDisposition.FocusedExisting path ->
                                        PathHelpers.pathsEqual path normalizedArcPath
                                    | _ -> false
                                )

                            Vitest.expect(openedInNewWindow).toBe (true)
                            Vitest.expect(focusedExisting).toBe (true)
                            Vitest.expect(createdWindowCount).toBe (1)
                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "concurrent CreateOrFocusArc requests write once and use one new-window owner",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-concurrent-window-"
                let callingWindowId = 52
                let targetWindowId = 53
                let identifier = "Concurrent Created ARC"

                let arcPath =
                    ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                let callingVault =
                    ArcVault((createTestWindow (testWindowOptions callingWindowId)).Window)

                callingVault.path <- Some "C:/occupied-concurrent-create"
                callingVault.SetArc(ARC("Occupied"))

                let targetWindow =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            LoadBehavior = ControlLoad
                    }

                let mutable createdWindowCount = 0

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    targetWindow.Window :> obj
                )

                let vaults = ArcVaults()
                vaults.Vaults.Add(callingWindowId, callingVault)

                let cleanup () = promise {
                    match vaults.TryGetVault(targetWindowId) with
                    | Some vault -> do! vault.StopFileWatcher()
                    | None -> ()

                    vaults.Vaults.Clear()
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let firstCreate = vaults.CreateOrFocusArc(callingWindowId, arcPath, identifier)
                    let secondCreate = vaults.CreateOrFocusArc(callingWindowId, arcPath, identifier)
                    do! targetWindow.LoadStarted
                    Vitest.expect(createdWindowCount).toBe (1)
                    targetWindow.ResolveLoad()

                    let! firstDisposition = firstCreate
                    let! secondDisposition = secondCreate
                    let dispositions = [| firstDisposition; secondDisposition |]

                    let createdInNewWindow =
                        dispositions
                        |> Array.exists (
                            function
                            | ArcOpenDisposition.CreatedInNewWindow path -> PathHelpers.pathsEqual path arcPath
                            | _ -> false
                        )

                    let focusedExisting =
                        dispositions
                        |> Array.exists (
                            function
                            | ArcOpenDisposition.FocusedExisting path -> PathHelpers.pathsEqual path arcPath
                            | _ -> false
                        )

                    Vitest.expect(createdInNewWindow).toBe (true)
                    Vitest.expect(focusedExisting).toBe (true)
                    Vitest.expect(createdWindowCount).toBe (1)
                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "OpenOrFocusArc does not publish renderer state when the initial file-tree scan fails",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-tree-transaction-current-"
                    "Open Tree Transaction"
                    ignore
                    (fun arcPath -> promise {
                        let movedPath = arcPath + "-during-tree-scan"
                        let mutable movedForTreeFailure = false

                        let windowState =
                            createTestWindow {
                                testWindowOptions 54 with
                                    OnTitleWrite =
                                        fun _ _ ->
                                            if not movedForTreeFailure && existsSync arcPath then
                                                movedForTreeFailure <- true
                                                renameSync arcPath movedPath
                            }

                        let vault = ArcVault(windowState.Window)
                        let vaults = ArcVaults()
                        vaults.Vaults.Add(54, vault)

                        let cleanup () = promise {
                            do! vault.StopFileWatcher()
                            vaults.Vaults.Clear()

                            if existsSync movedPath then
                                renameSync movedPath arcPath
                        }

                        let operation () = promise {
                            let! result = vaults.OpenOrFocusArc(54, arcPath) |> Promise.result

                            match result with
                            | Error error -> Vitest.expect(error.Message).toContain ("ENOENT")
                            | Ok disposition -> failwithf "Expected file-tree failure, received %A." disposition

                            Vitest.expect(vault.path).toEqual (None)
                            Vitest.expect(vault.arc).toEqual (None)
                            Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (0)
                            Vitest.expect(vault.watcher).toEqual (None)
                            Vitest.expect(windowState.SentMessages |> Seq.exists isPathChangeMessage).toBe (false)
                            Vitest.expect(windowState.SentMessages |> Seq.exists isFileTreeMessage).toBe (false)
                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "CreateOrFocusArc keeps the written ARC but does not publish renderer state when the initial tree fails",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-tree-transaction-current-"
                let arcPath = join [| rootPath; "created-arc" |] |> PathHelpers.normalizePath
                let movedPath = arcPath + "-during-tree-scan"
                let mutable movedForTreeFailure = false

                let windowState =
                    createTestWindow {
                        testWindowOptions 55 with
                            OnTitleWrite =
                                fun _ _ ->
                                    if not movedForTreeFailure && existsSync arcPath then
                                        movedForTreeFailure <- true
                                        renameSync arcPath movedPath
                    }

                let vault = ArcVault(windowState.Window)
                let vaults = ArcVaults()
                vaults.Vaults.Add(55, vault)

                let cleanup () = promise {
                    do! vault.StopFileWatcher()
                    vaults.Vaults.Clear()
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let! result = vaults.CreateOrFocusArc(55, arcPath, "Created ARC") |> Promise.result

                    match result with
                    | Error error -> Vitest.expect(error.Message).toContain ("ENOENT")
                    | Ok disposition -> failwithf "Expected file-tree failure, received %A." disposition

                    Vitest.expect(vault.path).toEqual (None)
                    Vitest.expect(vault.arc).toEqual (None)
                    Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (0)
                    Vitest.expect(vault.watcher).toEqual (None)
                    Vitest.expect(windowState.SentMessages |> Seq.exists isPathChangeMessage).toBe (false)
                    Vitest.expect(windowState.SentMessages |> Seq.exists isFileTreeMessage).toBe (false)

                    renameSync movedPath arcPath

                    let investigationPath =
                        ARCtrl.ArcPathHelper.combine arcPath ARCtrl.ArcPathHelper.InvestigationFileName

                    let! investigationExists = TestHelpers.pathExistsAsync investigationPath
                    Vitest.expect(investigationExists).toBe (true)
                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "new-window open preserves the initial file-tree error and cleans up without late IPC",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-tree-transaction-window-"
                    "Open Tree Window Transaction"
                    ignore
                    (fun arcPath -> promise {
                        let movedPath = arcPath + "-during-tree-scan"
                        let mutable movedForTreeFailure = false
                        let callingVault = ArcVault((createTestWindow (testWindowOptions 56)).Window)
                        callingVault.path <- Some "C:/occupied-open-tree-transaction"
                        callingVault.SetArc(ARC("Occupied"))

                        let targetWindow =
                            createTestWindow {
                                testWindowOptions 57 with
                                    OnTitleWrite =
                                        fun _ _ ->
                                            if not movedForTreeFailure && existsSync arcPath then
                                                movedForTreeFailure <- true
                                                renameSync arcPath movedPath
                            }

                        setBrowserWindowFactory (fun _ -> targetWindow.Window :> obj)
                        let vaults = ArcVaults()
                        vaults.Vaults.Add(56, callingVault)

                        let cleanup () = promise {
                            vaults.Vaults.Clear()

                            if existsSync movedPath then
                                renameSync movedPath arcPath
                        }

                        let operation () = promise {
                            let! result = vaults.OpenOrFocusArc(56, arcPath) |> Promise.result

                            match result with
                            | Error error -> Vitest.expect(error.Message).toContain ("ENOENT")
                            | Ok disposition -> failwithf "Expected file-tree failure, received %A." disposition

                            Vitest.expect(targetWindow.IsDestroyed()).toBe (true)
                            Vitest.expect(vaults.TryGetVault(57)).toEqual (None)
                            Vitest.expect(targetWindow.SendsAfterDestroy()).toBe (0)
                            Vitest.expect(targetWindow.SentMessages |> Seq.exists isPathChangeMessage).toBe (false)
                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "new-window create keeps the written ARC and cleans up without late IPC when the initial tree fails",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-tree-transaction-window-"
                let arcPath = join [| rootPath; "created-window-arc" |] |> PathHelpers.normalizePath
                let movedPath = arcPath + "-during-tree-scan"
                let mutable movedForTreeFailure = false
                let callingVault = ArcVault((createTestWindow (testWindowOptions 58)).Window)
                callingVault.path <- Some "C:/occupied-create-tree-transaction"
                callingVault.SetArc(ARC("Occupied"))

                let targetWindow =
                    createTestWindow {
                        testWindowOptions 59 with
                            OnTitleWrite =
                                fun _ _ ->
                                    if not movedForTreeFailure && existsSync arcPath then
                                        movedForTreeFailure <- true
                                        renameSync arcPath movedPath
                    }

                setBrowserWindowFactory (fun _ -> targetWindow.Window :> obj)
                let vaults = ArcVaults()
                vaults.Vaults.Add(58, callingVault)

                let cleanup () = promise {
                    vaults.Vaults.Clear()
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let! result = vaults.CreateOrFocusArc(58, arcPath, "Created Window ARC") |> Promise.result

                    match result with
                    | Error error -> Vitest.expect(error.Message).toContain ("ENOENT")
                    | Ok disposition -> failwithf "Expected file-tree failure, received %A." disposition

                    Vitest.expect(targetWindow.IsDestroyed()).toBe (true)
                    Vitest.expect(vaults.TryGetVault(59)).toEqual (None)
                    Vitest.expect(targetWindow.SendsAfterDestroy()).toBe (0)
                    Vitest.expect(targetWindow.SentMessages |> Seq.exists isPathChangeMessage).toBe (false)

                    renameSync movedPath arcPath

                    let investigationPath =
                        ARCtrl.ArcPathHelper.combine arcPath ARCtrl.ArcPathHelper.InvestigationFileName

                    let! investigationExists = TestHelpers.pathExistsAsync investigationPath
                    Vitest.expect(investigationExists).toBe (true)
                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "successful initialization publishes the path once after ARC and initial tree readiness",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-transaction-commit-"
                    "Finalized Initialization ARC"
                    ignore
                    (fun arcPath -> promise {
                        let mutable vaultAtPathPublication: ArcVault option = None
                        let mutable wasReadyAtPathPublication = false

                        let windowState =
                            createTestWindow {
                                testWindowOptions 60 with
                                    OnSend =
                                        fun args ->
                                            if isPathChangeMessage args then
                                                wasReadyAtPathPublication <-
                                                    vaultAtPathPublication
                                                    |> Option.exists (fun vault ->
                                                        vault.arc.IsSome
                                                        && vault.path.IsSome
                                                        && vault.FileTreeWriter.Tree.Count > 0
                                                        && vault.watcher.IsSome
                                                        && not vault.isInitializingArc
                                                    )
                            }

                        let vault = ArcVault(windowState.Window)
                        vaultAtPathPublication <- Some vault
                        let vaults = ArcVaults()
                        vaults.Vaults.Add(60, vault)

                        let cleanup () = promise {
                            do! vault.StopFileWatcher()
                            vaults.Vaults.Clear()
                        }

                        let operation () = promise {
                            let! _ = vaults.OpenOrFocusArc(60, arcPath)

                            let messages = windowState.SentMessages |> Seq.toArray

                            let pathMessageIndexes =
                                messages
                                |> Array.indexed
                                |> Array.choose (fun (index, args) ->
                                    if isPathChangeMessage args then Some index else None
                                )

                            let treeMessageIndex = messages |> Array.findIndex isFileTreeMessage

                            Vitest.expect(pathMessageIndexes.Length).toBe (1)
                            Vitest.expect(wasReadyAtPathPublication).toBe (true)
                            Vitest.expect(pathMessageIndexes.[0] < treeMessageIndex).toBe (true)
                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "OpenOrFocusArc shows the new renderer before loading an existing ARC",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-or-focus-order-"
                    "Renderer First ARC"
                    ignore
                    (fun arcPath -> promise {
                        let callingWindowId = 26
                        let targetWindowId = 27
                        let vaults = ArcVaults()
                        let mutable createdWindowCount = 0
                        let mutable registeredVault: ArcVault option = None
                        let mutable vaultWasRegisteredWhenLoadStarted = false
                        let mutable vaultWasEmptyWhenShown = false

                        let windowState =
                            createTestWindow {
                                testWindowOptions targetWindowId with
                                    OnLoad =
                                        fun () ->
                                            registeredVault <- vaults.TryGetVault(targetWindowId)
                                            vaultWasRegisteredWhenLoadStarted <- registeredVault.IsSome
                                    OnShow =
                                        fun () ->
                                            vaultWasEmptyWhenShown <-
                                                registeredVault
                                                |> Option.exists (fun vault -> vault.path.IsNone && vault.arc.IsNone)
                            }

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            windowState.Window :> obj
                        )

                        let cleanup () = promise {
                            match registeredVault with
                            | Some vault -> do! vault.StopFileWatcher()
                            | None -> ()
                        }

                        let operation () = promise {
                            Vitest.expect(vaults.Vaults.ContainsKey(callingWindowId)).toBe (false)

                            let! disposition = vaults.OpenOrFocusArc(callingWindowId, arcPath)
                            let normalizedArcPath = PathHelpers.normalizePath arcPath

                            match disposition with
                            | ArcOpenDisposition.OpenedInNewWindow openedPath ->
                                Vitest.expect(openedPath).toBe (normalizedArcPath)
                            | _ -> failwith "Expected the existing ARC to open in a new window."

                            Vitest.expect(createdWindowCount).toBe (1)
                            Vitest.expect(vaultWasRegisteredWhenLoadStarted).toBe (true)
                            Vitest.expect(windowState.LifecycleWasAttachedWhenLoadStarted()).toBe (true)
                            Vitest.expect(windowState.WasShown()).toBe (true)
                            Vitest.expect(vaultWasEmptyWhenShown).toBe (true)
                            Vitest.expect(windowState.IsDestroyed()).toBe (false)
                            Vitest.expect(registeredVault.IsSome).toBe (true)
                            Vitest.expect(vaults.Vaults.ContainsKey(targetWindowId)).toBe (true)
                            Vitest.expect(registeredVault.Value.path).toEqual (Some normalizedArcPath)
                            Vitest.expect(registeredVault.Value.arc.IsSome).toBe (true)

                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "OpenOrFocusArc cleans up when the target window closes during ARC loading",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-or-focus-close-during-load-"
                    "Closing ARC"
                    ignore
                    (fun arcPath -> promise {
                        let callingWindowId = 28
                        let targetWindowId = 29
                        let vaults = ArcVaults()
                        let mutable createdWindowCount = 0
                        let mutable loadingVault: ArcVault option = None
                        let mutable arcWasOpeningWhenClosed = false

                        let isArcOpening () =
                            loadingVault <- vaults.TryGetVault(targetWindowId)

                            loadingVault
                            |> Option.exists (fun vault -> vault.path.IsSome && vault.arc.IsNone)

                        let windowState = createTestWindow (testWindowOptions targetWindowId)

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            windowState.Window :> obj
                        )

                        let mutable capturedError: exn option = None

                        try
                            Vitest.expect(vaults.Vaults.ContainsKey(callingWindowId)).toBe (false)

                            let openOperation = vaults.OpenOrFocusArc(callingWindowId, arcPath)
                            do! waitUntil "ARC parse pending in a newly opened vault" isArcOpening
                            arcWasOpeningWhenClosed <- isArcOpening ()
                            windowState.TriggerClose()

                            try
                                let! _ = openOperation
                                ()
                            with error ->
                                capturedError <- Some error

                            match capturedError with
                            | Some(ArcLoadCancelledException cancelledWindowId) ->
                                Vitest.expect(cancelledWindowId).toBe (targetWindowId)
                            | _ -> failwith "Expected an explicit ARC-load cancellation marker."

                            Vitest.expect(createdWindowCount).toBe (1)
                            Vitest.expect(windowState.WasShown()).toBe (true)
                            Vitest.expect(windowState.CloseHandlerAttached()).toBe (true)
                            Vitest.expect(windowState.ClosedHandlerAttached()).toBe (true)
                            Vitest.expect(arcWasOpeningWhenClosed).toBe (true)
                            Vitest.expect(windowState.IsDestroyed()).toBe (true)
                            Vitest.expect(loadingVault.IsSome).toBe (true)
                            Vitest.expect(loadingVault.Value.watcher.IsNone).toBe (true)
                            Vitest.expect(vaults.Vaults.ContainsKey(targetWindowId)).toBe (false)
                            Vitest.expect(windowState.SendsAfterDestroy()).toBe (0)
                            Vitest.expect(windowState.TitleWritesAfterDestroy()).toBe (0)
                        with error ->
                            match loadingVault with
                            | Some vault -> do! vault.StopFileWatcher()
                            | None -> ()

                            return raise error
                    })
        )

        Vitest.test (
            "OpenOrFocusArc cleans up when renderer loading fails",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-or-focus-renderer-failure-"
                    "Renderer Failure ARC"
                    ignore
                    (fun arcPath -> promise {
                        let callingWindowId = 30
                        let targetWindowId = 31
                        let loadError = exn "Expected renderer load failure"
                        let vaults = ArcVaults()
                        let mutable createdWindowCount = 0
                        let mutable vaultAtLoad: ArcVault option = None

                        let windowState =
                            createTestWindow {
                                testWindowOptions targetWindowId with
                                    LoadBehavior = FailLoadWith loadError
                                    OnLoad = fun () -> vaultAtLoad <- vaults.TryGetVault(targetWindowId)
                            }

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            windowState.Window :> obj
                        )

                        let mutable capturedError: exn option = None

                        try
                            Vitest.expect(vaults.Vaults.ContainsKey(callingWindowId)).toBe (false)

                            try
                                let! _ = vaults.OpenOrFocusArc(callingWindowId, arcPath)
                                ()
                            with error ->
                                capturedError <- Some error

                            Vitest.expect(capturedError).toEqual (Some loadError)
                            Vitest.expect(createdWindowCount).toBe (1)
                            Vitest.expect(vaultAtLoad.IsSome).toBe (true)
                            Vitest.expect(windowState.LifecycleWasAttachedWhenLoadStarted()).toBe (true)
                            Vitest.expect(vaultAtLoad.Value.path).toEqual (None)
                            Vitest.expect(vaultAtLoad.Value.arc).toEqual (None)
                            Vitest.expect(vaultAtLoad.Value.watcher.IsNone).toBe (true)
                            Vitest.expect(windowState.IsDestroyed()).toBe (true)
                            Vitest.expect(vaults.Vaults.ContainsKey(targetWindowId)).toBe (false)
                        with error ->
                            match vaultAtLoad with
                            | Some vault -> do! vault.StopFileWatcher()
                            | None -> ()

                            return raise error
                    })
        )

        Vitest.test (
            "openARCByPath treats a destroyed target during pending renderer load as cancellation",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-open-destroyed-renderer-load-"
                    "Destroyed During Renderer Load"
                    ignore
                    (fun arcPath -> promise {
                        let originatingWindowId = 50
                        let targetWindowId = 51
                        let loadError = exn "ERR_FAILED"

                        let originatingWindow =
                            (createTestWindow (testWindowOptions originatingWindowId)).Window

                        let originatingVault = ArcVault(originatingWindow)
                        originatingVault.path <- Some "C:/already-open-renderer-load-cancellation"
                        originatingVault.SetArc(ARC("Originating ARC"))

                        let targetWindowState =
                            createTestWindow {
                                testWindowOptions targetWindowId with
                                    LoadBehavior = ControlLoad
                            }

                        let mutable dialogCount = 0

                        setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)
                        setBrowserWindowFactory (fun _ -> targetWindowState.Window :> obj)

                        setShowMessageBox (fun _ _ ->
                            dialogCount <- dialogCount + 1
                            createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                        )

                        ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                        try
                            let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)
                            let openOperation = api.openARCByPath arcPath

                            do! targetWindowState.LoadStarted
                            Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                            Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                            targetWindowState.Destroy()
                            targetWindowState.RejectLoad(loadError)

                            match! openOperation with
                            | Ok _ -> failwith "Expected renderer-load destruction to cancel ARC opening."
                            | Error(ArcLoadCancelledException cancelledWindowId) ->
                                Vitest.expect(cancelledWindowId).toBe (targetWindowId)
                            | Error error -> failwithf "Expected cancellation, received %s." error.Message

                            Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                            Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)
                            Vitest.expect(dialogCount).toBe (0)

                        finally
                            ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                            ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    })
        )

        Vitest.test (
            "openARCByPath preserves the open error when the native error dialog throws",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-ipc-dialog-failure-"
                    "Dialog Failure ARC"
                    ignore
                    (fun arcPath -> promise {
                        let originatingWindowId = 21
                        let targetWindowId = 22

                        let originatingWindow =
                            (createTestWindow (testWindowOptions originatingWindowId)).Window

                        let originatingVault = ArcVault(originatingWindow)
                        originatingVault.path <- Some "C:/already-open-dialog-failure"
                        originatingVault.SetArc(ARC("Originating ARC"))

                        let openError = exn "Expected renderer load failure"
                        let dialogError = exn "Expected native dialog failure"

                        let targetWindowState =
                            createTestWindow {
                                testWindowOptions targetWindowId with
                                    LoadBehavior = FailLoadWith openError
                            }

                        let mutable dialogCount = 0

                        setBrowserWindowFactory (fun _ -> targetWindowState.Window :> obj)
                        setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                        setShowMessageBox (fun _ _ ->
                            dialogCount <- dialogCount + 1
                            raise dialogError
                        )

                        ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                        try
                            let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                            match! api.openARCByPath arcPath with
                            | Ok _ -> failwith "Expected ARC opening to fail."
                            | Error returnedError ->
                                Vitest.expect(returnedError).toBe (openError)
                                Vitest.expect(returnedError).not.toBe (dialogError)
                                Vitest.expect(dialogCount).toBe (1)
                                Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                                Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)

                        finally
                            ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                            ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    })
        )

        Vitest.test (
            "openARCByPath treats closing a newly opened window during ARC loading as cancellation",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-ipc-close-during-load-"
                    "Closing ARC"
                    ignore
                    (fun arcPath -> promise {
                        let originatingWindowId = 18
                        let targetWindowId = 20

                        let originatingWindow =
                            (createTestWindow (testWindowOptions originatingWindowId)).Window

                        let originatingVault = ArcVault(originatingWindow)
                        originatingVault.path <- Some "C:/already-open-arc"
                        originatingVault.SetArc(ARC("Originating ARC"))

                        let mutable loadingVault: ArcVault option = None

                        let isArcOpening () =
                            loadingVault <- ARC_VAULTS.TryGetVault(targetWindowId)

                            loadingVault
                            |> Option.exists (fun vault -> vault.path.IsSome && vault.arc.IsNone)

                        let targetWindowState = createTestWindow (testWindowOptions targetWindowId)
                        let mutable arcWasOpeningWhenClosed = false

                        let mutable dialogCount = 0

                        setBrowserWindowFactory (fun _ -> targetWindowState.Window :> obj)
                        setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                        setShowMessageBox (fun _ _ ->
                            dialogCount <- dialogCount + 1
                            createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                        )

                        ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                        try
                            let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                            let openOperation = api.openARCByPath arcPath
                            do! waitUntil "ARC parse pending in the IPC-opened vault" isArcOpening
                            arcWasOpeningWhenClosed <- isArcOpening ()
                            targetWindowState.TriggerClose()

                            match! openOperation with
                            | Ok _ -> failwith "Expected closing the loading ARC window to cancel the operation."
                            | Error cancellation ->
                                match cancellation with
                                | ArcLoadCancelledException cancelledWindowId ->
                                    Vitest.expect(cancelledWindowId).toBe (targetWindowId)
                                | _ -> failwith "Expected an explicit ARC-load cancellation marker."

                                Vitest.expect(targetWindowState.WasShown()).toBe (true)
                                Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                                Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                                Vitest.expect(arcWasOpeningWhenClosed).toBe (true)
                                Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                                Vitest.expect(loadingVault.IsSome).toBe (true)
                                Vitest.expect(loadingVault.Value.watcher.IsNone).toBe (true)
                                Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)
                                Vitest.expect(targetWindowState.SendsAfterDestroy()).toBe (0)
                                Vitest.expect(targetWindowState.TitleWritesAfterDestroy()).toBe (0)
                                Vitest.expect(dialogCount).toBe (0)
                                Vitest.expect(originatingWindow.isDestroyed ()).toBe (false)

                            ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                        with error ->
                            ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                            ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                            return raise error
                    })
        )

        Vitest.test (
            "openARCByPath treats closing a new window after startup but before file-tree publication as cancellation",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-ipc-close-during-open-tree-"
                    "Closing During Open Tree Scan"
                    ignore
                    (fun arcPath -> promise {
                        let originatingWindowId = 44
                        let targetWindowId = 45
                        let expectedPath = PathHelpers.normalizePath arcPath

                        let originatingWindow =
                            (createTestWindow (testWindowOptions originatingWindowId)).Window

                        let originatingVault = ArcVault(originatingWindow)
                        originatingVault.path <- Some "C:/already-open-tree-race"
                        originatingVault.SetArc(ARC("Originating ARC"))
                        let mutable createdWindowCount = 0
                        let mutable targetVault: ArcVault option = None
                        let mutable targetWasRegistered = false
                        let mutable pathWasAssigned = false
                        let mutable arcWasLoaded = false
                        let mutable watcherWasStarted = false
                        let mutable fileTreeWasEmpty = false

                        let isArcStartedBeforeFileTreePublication () =
                            targetVault <- ARC_VAULTS.TryGetVault(targetWindowId)

                            match targetVault with
                            | Some vault ->
                                targetWasRegistered <- true
                                pathWasAssigned <- vault.path = Some expectedPath
                                arcWasLoaded <- vault.arc.IsSome
                                watcherWasStarted <- vault.watcher.IsSome
                                fileTreeWasEmpty <- vault.FileTreeWriter.Tree.Count = 0

                                pathWasAssigned
                                && arcWasLoaded
                                && watcherWasStarted
                                && fileTreeWasEmpty
                                && vault.isInitializingArc
                            | None -> false

                        let targetWindowState = createTestWindow (testWindowOptions targetWindowId)
                        let mutable startupWasCompleteWhenClosed = false

                        let mutable dialogCount = 0

                        setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            targetWindowState.Window :> obj
                        )

                        setShowMessageBox (fun _ _ ->
                            dialogCount <- dialogCount + 1
                            createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                        )

                        ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                        let cleanup () = promise {
                            match targetVault with
                            | Some vault -> do! vault.StopFileWatcher()
                            | None -> ()

                            ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                            ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                        }

                        let operation () = promise {
                            let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)
                            let openOperation = api.openARCByPath arcPath

                            do!
                                waitUntil
                                    "file-tree initialization pending in a newly opened vault"
                                    isArcStartedBeforeFileTreePublication

                            startupWasCompleteWhenClosed <- isArcStartedBeforeFileTreePublication ()
                            targetWindowState.TriggerClose()
                            let! openResult = openOperation

                            Vitest.expect(createdWindowCount).toBe (1)
                            Vitest.expect(targetWasRegistered).toBe (true)
                            Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                            Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                            Vitest.expect(targetWindowState.WasShown()).toBe (true)
                            Vitest.expect(pathWasAssigned).toBe (true)
                            Vitest.expect(arcWasLoaded).toBe (true)
                            Vitest.expect(watcherWasStarted).toBe (true)
                            Vitest.expect(fileTreeWasEmpty).toBe (true)
                            Vitest.expect(startupWasCompleteWhenClosed).toBe (true)
                            Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                            Vitest.expect(targetVault.IsSome).toBe (true)
                            Vitest.expect(targetVault.Value.FileTreeWriter.Tree.Count).toBe (0)
                            Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)
                            Vitest.expect(targetWindowState.SendsAfterDestroy()).toBe (0)
                            Vitest.expect(targetWindowState.TitleWritesAfterDestroy()).toBe (0)
                            Vitest.expect(dialogCount).toBe (0)

                            do! waitUntil "closed vault watcher cleanup" (fun () -> targetVault.Value.watcher.IsNone)
                            Vitest.expect(targetVault.Value.watcher.IsNone).toBe (true)

                            match openResult with
                            | Ok _ -> failwith "Expected closing during the file-tree scan to cancel ARC opening."
                            | Error cancellation ->
                                match cancellation with
                                | ArcLoadCancelledException cancelledWindowId ->
                                    Vitest.expect(cancelledWindowId).toBe (targetWindowId)
                                | _ -> failwith "Expected an explicit ARC-load cancellation marker."

                        }

                        return! withAsyncCleanup cleanup operation
                    })
        )

        Vitest.test (
            "openARCByPath treats closing the current window during ARC loading as cancellation",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-ipc-current-close-during-load-"
                    "Closing Current ARC"
                    ignore
                    (fun arcPath -> promise {
                        let windowId = 32
                        let mutable loadingVault: ArcVault option = None
                        let mutable vaultExistedWhenClosed = false
                        let mutable pathWasAssignedWhenClosed = false
                        let mutable arcWasNoneWhenClosed = false
                        let mutable watcherWasStartedWhenClosed = false

                        let isArcOpening () =
                            loadingVault <- ARC_VAULTS.TryGetVault(windowId)

                            match loadingVault with
                            | Some vault ->
                                vaultExistedWhenClosed <- true
                                pathWasAssignedWhenClosed <- vault.path = Some(PathHelpers.normalizePath arcPath)

                                arcWasNoneWhenClosed <- vault.arc.IsNone
                                watcherWasStartedWhenClosed <- vault.watcher.IsSome
                            | None -> ()

                            vaultExistedWhenClosed
                            && pathWasAssignedWhenClosed
                            && arcWasNoneWhenClosed
                            && watcherWasStartedWhenClosed

                        let windowState = createTestWindow (testWindowOptions windowId)
                        let mutable arcWasOpeningWhenClosed = false
                        let vault = ArcVault(windowState.Window)
                        let mutable dialogCount = 0
                        let mutable createdWindowCount = 0

                        ARC_VAULTS.Vaults.Add(windowId, vault)
                        ARC_VAULTS.OnCloseWindow(windowState.Window, vault, windowId)
                        setBrowserWindowFromWebContents (fun _ -> windowState.Window :> obj)

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            failwith "Same-window ARC opening must not create a BrowserWindow."
                        )

                        setShowMessageBox (fun _ _ ->
                            dialogCount <- dialogCount + 1
                            createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                        )

                        try
                            let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId windowId)

                            let openOperation = api.openARCByPath arcPath
                            do! waitUntil "ARC parse pending in the current vault" isArcOpening
                            arcWasOpeningWhenClosed <- isArcOpening ()
                            windowState.TriggerClose()

                            match! openOperation with
                            | Ok _ -> failwith "Expected closing the current ARC window to cancel the operation."
                            | Error cancellation ->
                                match cancellation with
                                | ArcLoadCancelledException cancelledWindowId ->
                                    Vitest.expect(cancelledWindowId).toBe (windowId)
                                | _ -> failwith "Expected an explicit ARC-load cancellation marker."

                                Vitest.expect(createdWindowCount).toBe (0)
                                Vitest.expect(arcWasOpeningWhenClosed).toBe (true)
                                Vitest.expect(vaultExistedWhenClosed).toBe (true)
                                Vitest.expect(pathWasAssignedWhenClosed).toBe (true)
                                Vitest.expect(arcWasNoneWhenClosed).toBe (true)
                                Vitest.expect(watcherWasStartedWhenClosed).toBe (true)
                                Vitest.expect(windowState.IsDestroyed()).toBe (true)
                                Vitest.expect(vault.watcher.IsNone).toBe (true)
                                Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(windowId)).toBe (false)
                                Vitest.expect(windowState.SendsAfterDestroy()).toBe (0)
                                Vitest.expect(windowState.TitleWritesAfterDestroy()).toBe (0)
                                Vitest.expect(dialogCount).toBe (0)
                        with error ->
                            ARC_VAULTS.Vaults.Remove(windowId) |> ignore
                            do! vault.StopFileWatcher()
                            return raise error
                    })
        )

        Vitest.test (
            "openARCByPath treats closing the current window after startup but before file-tree publication as cancellation",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-ipc-current-close-during-tree-"
                    "Closing Current ARC During Tree Scan"
                    ignore
                    (fun arcPath -> promise {
                        let windowId = 48
                        let expectedPath = PathHelpers.normalizePath arcPath
                        let mutable currentVault: ArcVault option = None
                        let mutable pathWasAssigned = false
                        let mutable arcWasLoaded = false
                        let mutable watcherWasStarted = false
                        let mutable fileTreeWasEmpty = false

                        let isArcStartedBeforeFileTreePublication () =
                            currentVault <- ARC_VAULTS.TryGetVault(windowId)

                            match currentVault with
                            | Some vault ->
                                pathWasAssigned <- vault.path = Some expectedPath
                                arcWasLoaded <- vault.arc.IsSome
                                watcherWasStarted <- vault.watcher.IsSome
                                fileTreeWasEmpty <- vault.FileTreeWriter.Tree.Count = 0

                                pathWasAssigned
                                && arcWasLoaded
                                && watcherWasStarted
                                && fileTreeWasEmpty
                                && vault.isInitializingArc
                            | None -> false

                        let windowState = createTestWindow (testWindowOptions windowId)
                        let mutable startupWasCompleteWhenClosed = false
                        let vault = ArcVault(windowState.Window)
                        let mutable dialogCount = 0
                        let mutable createdWindowCount = 0

                        ARC_VAULTS.Vaults.Add(windowId, vault)
                        ARC_VAULTS.OnCloseWindow(windowState.Window, vault, windowId)
                        setBrowserWindowFromWebContents (fun _ -> windowState.Window :> obj)

                        setBrowserWindowFactory (fun _ ->
                            createdWindowCount <- createdWindowCount + 1
                            failwith "Same-window ARC opening must not create a BrowserWindow."
                        )

                        setShowMessageBox (fun _ _ ->
                            dialogCount <- dialogCount + 1
                            createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                        )

                        try
                            let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId windowId)

                            let openOperation = api.openARCByPath arcPath

                            do!
                                waitUntil
                                    "file-tree initialization pending in the current vault"
                                    isArcStartedBeforeFileTreePublication

                            startupWasCompleteWhenClosed <- isArcStartedBeforeFileTreePublication ()
                            windowState.TriggerClose()

                            match! openOperation with
                            | Ok _ -> failwith "Expected closing during the file-tree scan to cancel ARC opening."
                            | Error cancellation ->
                                match cancellation with
                                | ArcLoadCancelledException cancelledWindowId ->
                                    Vitest.expect(cancelledWindowId).toBe (windowId)
                                | _ -> failwith "Expected an explicit ARC-load cancellation marker."

                            Vitest.expect(createdWindowCount).toBe (0)
                            Vitest.expect(pathWasAssigned).toBe (true)
                            Vitest.expect(arcWasLoaded).toBe (true)
                            Vitest.expect(watcherWasStarted).toBe (true)
                            Vitest.expect(fileTreeWasEmpty).toBe (true)
                            Vitest.expect(startupWasCompleteWhenClosed).toBe (true)
                            Vitest.expect(windowState.IsDestroyed()).toBe (true)
                            Vitest.expect(currentVault.IsSome).toBe (true)
                            Vitest.expect(currentVault.Value.FileTreeWriter.Tree.Count).toBe (0)
                            Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(windowId)).toBe (false)
                            Vitest.expect(windowState.SendsAfterDestroy()).toBe (0)
                            Vitest.expect(windowState.TitleWritesAfterDestroy()).toBe (0)
                            Vitest.expect(dialogCount).toBe (0)
                        with error ->
                            ARC_VAULTS.Vaults.Remove(windowId) |> ignore
                            do! vault.StopFileWatcher()
                            return raise error
                    })
        )

        Vitest.test (
            "openARCByPath rejects an invalid ARC and shows one native error with folder context",
            fun () -> promise {
                let! folderPath = TestHelpers.createTempDirectoryAsync "swate-ipc-invalid-arc-"
                let windowId = 16
                let window = (createTestWindow (testWindowOptions windowId)).Window
                let vault = ArcVault(window)
                let mutable dialogCount = 0
                let mutable dialogOptions: obj option = None

                ARC_VAULTS.Vaults.Add(windowId, vault)
                setBrowserWindowFromWebContents (fun _ -> window :> obj)

                setShowMessageBox (fun _ options ->
                    dialogCount <- dialogCount + 1
                    dialogOptions <- Some options
                    createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                )

                try
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId windowId)

                    match! api.openARCByPath folderPath with
                    | Ok _ -> failwith "Expected a folder without an investigation file to be rejected."
                    | Error error ->
                        Vitest.expect(error :? ArcLoadCancelledException).toBe (false)
                        Vitest.expect(error.Message).toContain (ARCtrl.ArcPathHelper.InvestigationFileName)
                        Vitest.expect(vault.path).toEqual (None)
                        Vitest.expect(vault.arc).toEqual (None)
                        Vitest.expect(dialogCount).toBe (1)
                        Vitest.expect(dialogOptions.IsSome).toBe (true)
                        Vitest.expect(dialogOptions.Value?message).toBe ("The ARC could not be opened.")

                        Vitest
                            .expect(dialogOptions.Value?detail)
                            .toContain ($"Folder: {PathHelpers.normalizePath folderPath}")

                        Vitest.expect(dialogOptions.Value?detail).toContain (ARCtrl.ArcPathHelper.InvestigationFileName)

                    ARC_VAULTS.Vaults.Remove(windowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync folderPath
                with error ->
                    ARC_VAULTS.Vaults.Remove(windowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync folderPath
                    return raise error
            }
        )

        Vitest.test (
            "openARC omits folder context when the dialog returns multiple paths",
            fun () -> promise {
                let windowId = 17
                let window = (createTestWindow (testWindowOptions windowId)).Window
                let mutable dialogOptions: obj option = None

                setBrowserWindowFromWebContents (fun _ -> window :> obj)

                setShowOpenDialog (fun _ _ ->
                    createObj [
                        "canceled" ==> false
                        "filePaths" ==> [| "C:/first"; "C:/second" |]
                    ]
                )

                setShowMessageBox (fun _ options ->
                    dialogOptions <- Some options
                    createObj [ "response" ==> 0; "checkboxChecked" ==> false ]
                )

                let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId windowId)

                match! api.openARC () with
                | Ok _ -> failwith "Expected multiple dialog paths to be rejected."
                | Error error ->
                    Vitest.expect(error.Message).toContain ("Not exactly one path")
                    Vitest.expect(dialogOptions.IsSome).toBe (true)
                    Vitest.expect(dialogOptions.Value?detail).toContain ("Not exactly one path")
                    Vitest.expect(dialogOptions.Value?detail).not.toContain ("Folder:")
            }
        )

        Vitest.test (
            "openARC preserves the multi-selection error when the native error dialog throws",
            fun () -> promise {
                let windowId = 33
                let window = (createTestWindow (testWindowOptions windowId)).Window
                let dialogError = exn "Expected native dialog failure"
                let mutable dialogCount = 0

                setBrowserWindowFromWebContents (fun _ -> window :> obj)

                setShowOpenDialog (fun _ _ ->
                    createObj [
                        "canceled" ==> false
                        "filePaths" ==> [| "C:/first"; "C:/second" |]
                    ]
                )

                setShowMessageBox (fun _ _ ->
                    dialogCount <- dialogCount + 1
                    raise dialogError
                )

                let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId windowId)

                match! api.openARC () with
                | Ok _ -> return failwith "Expected multiple dialog paths to be rejected."
                | Error returnedError ->
                    Vitest.expect(returnedError.Message).toBe ("Not exactly one path")
                    Vitest.expect(returnedError).not.toBe (dialogError)
                    Vitest.expect(dialogCount).toBe (1)
            }
        )

        Vitest.test (
            "openARC preserves the folder-picker error when the native error dialog throws",
            fun () -> promise {
                let windowId = 34
                let window = (createTestWindow (testWindowOptions windowId)).Window
                let openDialogError = exn "Expected folder-picker failure"
                let messageDialogError = exn "Expected native dialog failure"
                let mutable dialogCount = 0

                setBrowserWindowFromWebContents (fun _ -> window :> obj)
                setShowOpenDialog (fun _ _ -> raise openDialogError)

                setShowMessageBox (fun _ _ ->
                    dialogCount <- dialogCount + 1
                    raise messageDialogError
                )

                let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId windowId)

                match! api.openARC () with
                | Ok _ -> return failwith "Expected the throwing folder picker to fail ARC opening."
                | Error returnedError ->
                    Vitest.expect(returnedError).toBe (openDialogError)
                    Vitest.expect(returnedError).not.toBe (messageDialogError)
                    Vitest.expect(dialogCount).toBe (1)
            }
        )

        Vitest.test (
            "createARC resolves Cancelled when the folder picker is cancelled",
            fun () -> promise {
                let originatingWindowId = 35

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let mutable createdWindowCount = 0

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    failwith "Folder-picker cancellation must not create a BrowserWindow."
                )

                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> true; "filePaths" ==> [||] ])

                let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                let request: CreateArcRequest = {
                    identifier = "Cancelled ARC"
                    initGit = false
                }

                match! api.createARC request with
                | Ok CreateArcOutcome.Cancelled -> Vitest.expect(createdWindowCount).toBe (0)
                | Ok outcome -> return failwithf "Expected Cancelled, received %A." outcome
                | Error error -> return failwithf "Expected cancellation to be non-error, received %s." error.Message
            }
        )

        Vitest.test (
            "createARC resolves FocusedExisting without creating or writing another ARC",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-ipc-create-focus-existing-"
                let originatingWindowId = 40
                let existingWindowId = 41
                let identifier = "Already Open ARC"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let mutable focusCount = 0
                let mutable createdWindowCount = 0

                let existingWindow =
                    (createTestWindow {
                        testWindowOptions existingWindowId with
                            OnFocus = fun () -> focusCount <- focusCount + 1
                    })
                        .Window

                let expectedPath =
                    ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                let existingVault = ArcVault(existingWindow)
                existingVault.path <- Some expectedPath
                existingVault.SetArc(ARC(identifier))

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    failwith "Focusing an existing ARC must not create a BrowserWindow."
                )

                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])
                ARC_VAULTS.Vaults.Add(existingWindowId, existingVault)

                let cleanup () = promise {
                    ARC_VAULTS.Vaults.Remove(existingWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let! existedBeforeCreate = TestHelpers.pathExistsAsync expectedPath
                    Vitest.expect(existedBeforeCreate).toBe (false)

                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: CreateArcRequest = {
                        identifier = identifier
                        initGit = false
                    }

                    match! api.createARC request with
                    | Ok(CreateArcOutcome.FocusedExisting focusedPath) -> Vitest.expect(focusedPath).toBe (expectedPath)
                    | Ok outcome -> failwithf "Expected FocusedExisting, received %A." outcome
                    | Error error -> failwithf "Expected focusing to be non-error, received %s." error.Message

                    Vitest.expect(focusCount).toBe (1)
                    Vitest.expect(createdWindowCount).toBe (0)
                    Vitest.expect(ARC_VAULTS.TryGetVault(existingWindowId)).toEqual (Some existingVault)

                    let! existsAfterCreate = TestHelpers.pathExistsAsync expectedPath
                    Vitest.expect(existsAfterCreate).toBe (false)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC resolves Created with the normalized path after successful creation",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-ipc-create-success-"
                let originatingWindowId = 36
                let targetWindowId = 37
                let identifier = "Successful ARC"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let originatingVault = ArcVault(originatingWindow)
                originatingVault.path <- Some "C:/already-open-create-success"
                originatingVault.SetArc(ARC("Originating ARC"))
                let mutable createdWindowCount = 0
                let mutable targetVault: ArcVault option = None

                let targetWindowState =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            OnLoad = fun () -> targetVault <- ARC_VAULTS.TryGetVault(targetWindowId)
                    }

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    targetWindowState.Window :> obj
                )

                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])
                ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                let cleanup () = promise {
                    match targetVault with
                    | Some vault -> do! vault.StopFileWatcher()
                    | None -> ()

                    ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                    ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: CreateArcRequest = {
                        identifier = identifier
                        initGit = false
                    }

                    let expectedPath =
                        ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                    match! api.createARC request with
                    | Ok(CreateArcOutcome.Created createdPath) -> Vitest.expect(createdPath).toBe (expectedPath)
                    | Ok outcome -> failwithf "Expected Created, received %A." outcome
                    | Error error -> failwithf "Expected successful creation, received %s." error.Message

                    Vitest.expect(createdWindowCount).toBe (1)
                    Vitest.expect(targetWindowState.WasShown()).toBe (true)
                    Vitest.expect(targetWindowState.LifecycleWasAttachedWhenLoadStarted()).toBe (true)
                    Vitest.expect(targetWindowState.IsDestroyed()).toBe (false)
                    Vitest.expect(targetVault.IsSome).toBe (true)
                    Vitest.expect(targetVault.Value.path).toEqual (Some expectedPath)
                    Vitest.expect(targetVault.Value.watcher.IsSome).toBe (true)

                    let! arcExists = TestHelpers.pathExistsAsync expectedPath
                    Vitest.expect(arcExists).toBe (true)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC closes without a save prompt while the initial disk baseline is being established",
            fun () -> promise {
                let windowId = 53
                let expectedPath = PathHelpers.normalizePath "C:/create-close-initial-write"
                let mutable initializationWasActiveWhenClosed = false
                let mutable writeWasBusyWhenClosed = false
                let windowState = createTestWindow (testWindowOptions windowId)
                let vault = ArcVault(windowState.Window)
                ARC_VAULTS.Vaults.Add(windowId, vault)
                ARC_VAULTS.OnCloseWindow(windowState.Window, vault, windowId)

                try
                    vault.isInitializingArc <- true
                    vault.path <- Some expectedPath
                    vault.SetArc(ARC("Close During Initial Write ARC"))
                    vault.RefreshHasUnsavedArcChangesFlag()

                    let mutable releaseWrite = ignore

                    let writeGate =
                        JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                    let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)

                    // This is the exact lifecycle point guarded by the initialization close bypass.
                    Vitest.expect(vault.isInitializingArc).toBe (true)
                    Vitest.expect(vault.isBusyWriting).toBe (true)
                    Vitest.expect(vault.path).toEqual (Some expectedPath)
                    Vitest.expect(vault.arc.IsSome).toBe (true)

                    initializationWasActiveWhenClosed <- vault.isInitializingArc
                    writeWasBusyWhenClosed <- vault.isBusyWriting
                    windowState.TriggerClose()

                    Vitest.expect(initializationWasActiveWhenClosed).toBe (true)
                    Vitest.expect(writeWasBusyWhenClosed).toBe (true)
                    Vitest.expect(windowState.PreventedCloseCount()).toBe (0)
                    // The dirty-state notification is the only send; no save-before-close request follows it.
                    Vitest.expect(windowState.SentMessages.Count).toBe (1)
                    Vitest.expect(windowState.IsDestroyed()).toBe (true)
                    Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(windowId)).toBe (false)

                    releaseWrite ()
                    do! writeScope
                finally
                    ARC_VAULTS.Vaults.Remove(windowId) |> ignore
            }
        )

        Vitest.test (
            "createARC treats a destroyed target during pending renderer load as pre-write cancellation",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-destroyed-renderer-load-"
                let originatingWindowId = 54
                let targetWindowId = 55
                let identifier = "Never Written ARC"
                let loadError = exn "ERR_FAILED"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let originatingVault = ArcVault(originatingWindow)
                originatingVault.path <- Some "C:/already-open-create-load-cancellation"
                originatingVault.SetArc(ARC("Originating ARC"))

                let expectedPath =
                    ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                let targetWindowState =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            LoadBehavior = ControlLoad
                    }

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)
                setBrowserWindowFactory (fun _ -> targetWindowState.Window :> obj)
                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])
                ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                let cleanup () = promise {
                    ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                    ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: CreateArcRequest = {
                        identifier = identifier
                        initGit = false
                    }

                    let createOperation = api.createARC request
                    do! targetWindowState.LoadStarted
                    Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                    Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                    targetWindowState.Destroy()
                    targetWindowState.RejectLoad(loadError)

                    match! createOperation with
                    | Ok CreateArcOutcome.Cancelled -> ()
                    | Ok outcome -> failwithf "Expected pre-write Cancelled, received %A." outcome
                    | Error error -> failwithf "Expected non-error cancellation, received %s." error.Message

                    Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                    Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)

                    let! arcDirectoryExists = TestHelpers.pathExistsAsync expectedPath
                    Vitest.expect(arcDirectoryExists).toBe (false)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC does not infer creation when a pre-existing ARC remains after renderer-load cancellation",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-existing-load-cancellation-"
                let originatingWindowId = 56
                let targetWindowId = 57
                let identifier = "Existing ARC"
                let loadError = exn "ERR_FAILED"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let originatingVault = ArcVault(originatingWindow)
                originatingVault.path <- Some "C:/already-open-existing-create-cancellation"
                originatingVault.SetArc(ARC("Originating ARC"))

                let expectedPath =
                    ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                let existingArc = ARC(identifier)
                do! existingArc.WriteAsync expectedPath

                let targetWindowState =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            LoadBehavior = ControlLoad
                    }

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)
                setBrowserWindowFactory (fun _ -> targetWindowState.Window :> obj)
                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])
                ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                let cleanup () = promise {
                    ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                    ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: CreateArcRequest = {
                        identifier = identifier
                        initGit = false
                    }

                    let createOperation = api.createARC request
                    do! targetWindowState.LoadStarted
                    Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                    Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                    targetWindowState.Destroy()
                    targetWindowState.RejectLoad(loadError)

                    match! createOperation with
                    | Ok CreateArcOutcome.Cancelled -> ()
                    | Ok outcome -> failwithf "Expected pre-create Cancelled, received %A." outcome
                    | Error error -> failwithf "Expected non-error cancellation, received %s." error.Message

                    Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                    Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)

                    let investigationPath =
                        ARCtrl.ArcPathHelper.combine expectedPath ARCtrl.ArcPathHelper.InvestigationFileName

                    let! investigationFileExists = TestHelpers.pathExistsAsync investigationPath
                    Vitest.expect(investigationFileExists).toBe (true)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC resolves CreatedButClosed when the new window closes during post-write loading",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-ipc-create-close-during-load-"
                let originatingWindowId = 38
                let targetWindowId = 39
                let identifier = "Created Then Closed ARC"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let originatingVault = ArcVault(originatingWindow)
                originatingVault.path <- Some "C:/already-open-create-close"
                originatingVault.SetArc(ARC("Originating ARC"))

                let expectedPath =
                    ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                let mutable createdWindowCount = 0
                let mutable loadingVault: ArcVault option = None
                let mutable targetWasRegistered = false
                let mutable targetPathWasAssigned = false
                let mutable creationReachedPostWriteLoad = false
                let mutable watcherWasStarted = false
                let mutable vaultWasEmptyWhenShown = false

                let isArcLoadingAfterWrite () =
                    loadingVault <- ARC_VAULTS.TryGetVault(targetWindowId)

                    match loadingVault with
                    | Some vault ->
                        targetWasRegistered <- true
                        targetPathWasAssigned <- vault.path = Some expectedPath
                        creationReachedPostWriteLoad <- vault.arc.IsSome
                        watcherWasStarted <- vault.watcher.IsSome

                        targetWasRegistered
                        && targetPathWasAssigned
                        && creationReachedPostWriteLoad
                        && watcherWasStarted
                        && vault.isInitializingArc
                        && not vault.isBusyWriting
                        && vault.hasUnsavedArcChanges
                    | None -> false

                let targetWindowState =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            OnShow =
                                fun () ->
                                    vaultWasEmptyWhenShown <-
                                        ARC_VAULTS.TryGetVault(targetWindowId)
                                        |> Option.exists (fun vault -> vault.path.IsNone && vault.arc.IsNone)
                    }

                let mutable arcWasLoadingAfterWriteWhenClosed = false

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    targetWindowState.Window :> obj
                )

                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])
                ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                Vitest.vi.stubEnv ("SWATE_TEST_USER_DATA", Some rootPath)

                let versionControlHost =
                    Main.VersionControl.VersionControlRuntime.createProduction
                        Main.VersionControl.WorkspaceSessionHost.windowOwnerOf
                    |> Main.VersionControl.WorkspaceSessionHost.WorkspaceSessionHost

                Main.VersionControl.WorkspaceSessionHost.initialize versionControlHost

                let cleanup () = promise {
                    match loadingVault with
                    | Some vault -> do! vault.StopFileWatcher()
                    | None -> ()

                    ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                    ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    Main.VersionControl.WorkspaceSessionHost.resetForTests ()
                    Vitest.vi.unstubAllEnvs ()
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: CreateArcRequest = {
                        identifier = identifier
                        initGit = true
                    }

                    let createOperation = api.createARC request
                    do! waitUntil "post-write ARC reload pending" isArcLoadingAfterWrite
                    arcWasLoadingAfterWriteWhenClosed <- isArcLoadingAfterWrite ()
                    targetWindowState.TriggerClose()

                    match! createOperation with
                    | Ok(CreateArcOutcome.CreatedButClosed createdPath) ->
                        Vitest.expect(createdPath).toBe (expectedPath)
                    | Ok outcome -> failwithf "Expected CreatedButClosed, received %A." outcome
                    | Error error -> failwithf "Expected close-after-create to be non-error, received %s." error.Message

                    Vitest.expect(createdWindowCount).toBe (1)
                    Vitest.expect(targetWasRegistered).toBe (true)
                    Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                    Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                    Vitest.expect(targetWindowState.WasShown()).toBe (true)
                    Vitest.expect(vaultWasEmptyWhenShown).toBe (true)
                    Vitest.expect(targetPathWasAssigned).toBe (true)
                    Vitest.expect(creationReachedPostWriteLoad).toBe (true)
                    Vitest.expect(arcWasLoadingAfterWriteWhenClosed).toBe (true)
                    Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                    Vitest.expect(loadingVault.IsSome).toBe (true)
                    Vitest.expect(loadingVault.Value.watcher.IsNone).toBe (true)
                    Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)
                    Vitest.expect(targetWindowState.SendsAfterDestroy()).toBe (0)
                    Vitest.expect(targetWindowState.TitleWritesAfterDestroy()).toBe (0)

                    let! arcDirectoryExists = TestHelpers.pathExistsAsync expectedPath

                    let investigationPath =
                        ARCtrl.ArcPathHelper.combine expectedPath ARCtrl.ArcPathHelper.InvestigationFileName

                    let! investigationFileExists = TestHelpers.pathExistsAsync investigationPath
                    let! gitDirectoryExists = TestHelpers.pathExistsAsync (join [| expectedPath; ".git" |])
                    Vitest.expect(arcDirectoryExists).toBe (true)
                    Vitest.expect(investigationFileExists).toBe (true)
                    Vitest.expect(gitDirectoryExists).toBe (true)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC resolves CreatedButClosed when the new window closes after startup but before file-tree publication",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-ipc-create-close-during-tree-"
                let originatingWindowId = 46
                let targetWindowId = 47
                let identifier = "Created During Tree Scan ARC"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let originatingVault = ArcVault(originatingWindow)
                originatingVault.path <- Some "C:/already-open-create-tree-race"
                originatingVault.SetArc(ARC("Originating ARC"))

                let expectedPath =
                    ARCtrl.ArcPathHelper.combine rootPath identifier |> PathHelpers.normalizePath

                let mutable createdWindowCount = 0
                let mutable targetVault: ArcVault option = None
                let mutable targetWasRegistered = false
                let mutable pathWasAssigned = false
                let mutable arcWasLoaded = false
                let mutable watcherWasStarted = false
                let mutable fileTreeWasEmpty = false
                let mutable vaultWasEmptyWhenShown = false

                let isArcStartedBeforeFileTreePublication () =
                    targetVault <- ARC_VAULTS.TryGetVault(targetWindowId)

                    match targetVault with
                    | Some vault ->
                        targetWasRegistered <- true
                        pathWasAssigned <- vault.path = Some expectedPath
                        arcWasLoaded <- vault.arc.IsSome
                        watcherWasStarted <- vault.watcher.IsSome
                        fileTreeWasEmpty <- vault.FileTreeWriter.Tree.Count = 0

                        pathWasAssigned
                        && arcWasLoaded
                        && watcherWasStarted
                        && fileTreeWasEmpty
                        && vault.isInitializingArc
                        && not vault.hasUnsavedArcChanges
                    | None -> false

                let targetWindowState =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            OnShow =
                                fun () ->
                                    vaultWasEmptyWhenShown <-
                                        ARC_VAULTS.TryGetVault(targetWindowId)
                                        |> Option.exists (fun vault -> vault.path.IsNone && vault.arc.IsNone)
                    }

                let mutable startupWasCompleteWhenClosed = false

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    targetWindowState.Window :> obj
                )

                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])
                ARC_VAULTS.Vaults.Add(originatingWindowId, originatingVault)

                let cleanup () = promise {
                    match targetVault with
                    | Some vault -> do! vault.StopFileWatcher()
                    | None -> ()

                    ARC_VAULTS.Vaults.Remove(originatingWindowId) |> ignore
                    ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: CreateArcRequest = {
                        identifier = identifier
                        initGit = false
                    }

                    let createOperation = api.createARC request

                    do!
                        waitUntil
                            "file-tree initialization pending after ARC creation"
                            isArcStartedBeforeFileTreePublication

                    startupWasCompleteWhenClosed <- isArcStartedBeforeFileTreePublication ()
                    targetWindowState.TriggerClose()

                    match! createOperation with
                    | Ok(CreateArcOutcome.CreatedButClosed createdPath) ->
                        Vitest.expect(createdPath).toBe (expectedPath)
                    | Ok outcome -> failwithf "Expected CreatedButClosed, received %A." outcome
                    | Error error -> failwithf "Expected close-after-create to be non-error, received %s." error.Message

                    Vitest.expect(createdWindowCount).toBe (1)
                    Vitest.expect(targetWasRegistered).toBe (true)
                    Vitest.expect(targetWindowState.CloseHandlerAttached()).toBe (true)
                    Vitest.expect(targetWindowState.ClosedHandlerAttached()).toBe (true)
                    Vitest.expect(targetWindowState.WasShown()).toBe (true)
                    Vitest.expect(vaultWasEmptyWhenShown).toBe (true)
                    Vitest.expect(pathWasAssigned).toBe (true)
                    Vitest.expect(arcWasLoaded).toBe (true)
                    Vitest.expect(watcherWasStarted).toBe (true)
                    Vitest.expect(fileTreeWasEmpty).toBe (true)
                    Vitest.expect(startupWasCompleteWhenClosed).toBe (true)
                    Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                    Vitest.expect(targetVault.IsSome).toBe (true)
                    Vitest.expect(targetVault.Value.FileTreeWriter.Tree.Count).toBe (0)
                    Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)
                    Vitest.expect(targetWindowState.SendsAfterDestroy()).toBe (0)
                    Vitest.expect(targetWindowState.TitleWritesAfterDestroy()).toBe (0)

                    let! arcDirectoryExists = TestHelpers.pathExistsAsync expectedPath

                    let investigationPath =
                        ARCtrl.ArcPathHelper.combine expectedPath ARCtrl.ArcPathHelper.InvestigationFileName

                    let! investigationFileExists = TestHelpers.pathExistsAsync investigationPath
                    Vitest.expect(arcDirectoryExists).toBe (true)
                    Vitest.expect(investigationFileExists).toBe (true)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC resolves Error when new-window creation fails",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-ipc-create-failure-"
                let originatingWindowId = 23
                let targetWindowId = 24
                let identifier = "Creation Failure ARC"
                let createError = exn "Expected new ARC renderer load failure"

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let targetWindowState =
                    createTestWindow {
                        testWindowOptions targetWindowId with
                            LoadBehavior = FailLoadWith createError
                    }

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)
                setBrowserWindowFactory (fun _ -> targetWindowState.Window :> obj)

                setShowOpenDialog (fun _ _ -> createObj [ "canceled" ==> false; "filePaths" ==> [| rootPath |] ])

                let cleanup () = promise {
                    ARC_VAULTS.Vaults.Remove(targetWindowId) |> ignore
                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                    let request: Swate.Electron.Shared.IPCTypes.CreateArcRequest = {
                        identifier = identifier
                        initGit = false
                    }

                    match! api.createARC request with
                    | Ok _ -> failwith "Expected new ARC creation to fail."
                    | Error returnedError ->
                        Vitest.expect(returnedError).toBe (createError)
                        Vitest.expect(targetWindowState.IsDestroyed()).toBe (true)
                        Vitest.expect(ARC_VAULTS.Vaults.ContainsKey(targetWindowId)).toBe (false)

                    let requestedArcPath = join [| rootPath; identifier |]
                    let! requestedArcExists = TestHelpers.pathExistsAsync requestedArcPath
                    Vitest.expect(requestedArcExists).toBe (false)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "createARC resolves Error when the folder dialog throws",
            fun () -> promise {
                let originatingWindowId = 25

                let originatingWindow =
                    (createTestWindow (testWindowOptions originatingWindowId)).Window

                let dialogError = exn "Expected folder dialog failure"

                setBrowserWindowFromWebContents (fun _ -> originatingWindow :> obj)
                setShowOpenDialog (fun _ _ -> raise dialogError)

                let api = Main.IPC.ArcVaultsApi.api (ipcEventWithSenderId originatingWindowId)

                let request: Swate.Electron.Shared.IPCTypes.CreateArcRequest = {
                    identifier = "Dialog Failure ARC"
                    initGit = false
                }

                match! api.createARC request with
                | Ok _ -> return failwith "Expected the throwing folder dialog to fail ARC creation."
                | Error returnedError -> Vitest.expect(returnedError).toBe (dialogError)
            }
        )

        Vitest.test (
            "RegisterVaultWithNewArc loads and shows the renderer before creating the ARC",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-registration-create-order-"
                let arcPath = join [| rootPath; "new-arc" |]
                let mutable vaultsRef: ArcVaults option = None
                let mutable vaultWasEmptyWhenShown = false
                let mutable arcDirectoryWasAbsentWhenShown = false
                let mutable registeredVault: ArcVault option = None

                let cleanup () = promise {
                    match registeredVault with
                    | Some vault -> do! vault.StopFileWatcher()
                    | None -> ()

                    do! TestHelpers.removeDirectoryAsync rootPath
                }

                let operation () = promise {
                    let windowId = 13

                    let windowState =
                        createTestWindow {
                            testWindowOptions windowId with
                                OnShow =
                                    fun () ->
                                        registeredVault <- vaultsRef.Value.TryGetVault(windowId)

                                        vaultWasEmptyWhenShown <-
                                            registeredVault
                                            |> Option.exists (fun vault -> vault.path.IsNone && vault.arc.IsNone)

                                        arcDirectoryWasAbsentWhenShown <- not (existsSync arcPath)
                        }

                    let vaults = ArcVaults()
                    vaultsRef <- Some vaults
                    setBrowserWindowFactory (fun _ -> windowState.Window :> obj)

                    let! returnedVault = vaults.RegisterVaultWithNewArc(arcPath, "New ARC")

                    Vitest.expect(returnedVault.window.id).toBe (windowId)
                    Vitest.expect(windowState.LifecycleWasAttachedWhenLoadStarted()).toBe (true)
                    Vitest.expect(windowState.WasShown()).toBe (true)
                    Vitest.expect(vaultWasEmptyWhenShown).toBe (true)
                    Vitest.expect(arcDirectoryWasAbsentWhenShown).toBe (true)
                    Vitest.expect(existsSync arcPath).toBe (true)
                    Vitest.expect(windowState.IsDestroyed()).toBe (false)
                    Vitest.expect(registeredVault.IsSome).toBe (true)
                    Vitest.expect(returnedVault).toBe (registeredVault.Value)
                    Vitest.expect(vaults.Vaults.ContainsKey(windowId)).toBe (true)

                }

                return! withAsyncCleanup cleanup operation
            }
        )

        Vitest.test (
            "RegisterVaultWithNewArc renderer-load failure does not create an ARC",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-registration-create-"
                let arcPath = join [| rootPath; "new-arc" |]
                let mutable initializedVault: ArcVault option = None
                let mutable vaultsRef: ArcVaults option = None

                try
                    let windowId = 19
                    let loadError = exn "Expected renderer load failure"

                    let windowState =
                        createTestWindow {
                            testWindowOptions windowId with
                                LoadBehavior = FailLoadWith loadError
                                OnLoad = fun () -> initializedVault <- vaultsRef.Value.TryGetVault(windowId)
                        }

                    setBrowserWindowFactory (fun _ -> windowState.Window :> obj)

                    let vaults = ArcVaults()
                    vaultsRef <- Some vaults

                    do!
                        expectRegistrationLoadFailure
                            loadError
                            vaults
                            windowId
                            windowState.IsDestroyed
                            (fun () -> vaults.RegisterVaultWithNewArc(arcPath, "New ARC"))

                    Vitest.expect(initializedVault.IsSome).toBe (true)
                    Vitest.expect(initializedVault.Value.watcher.IsNone).toBe (true)

                    let! arcDirectoryExists = TestHelpers.pathExistsAsync arcPath
                    Vitest.expect(arcDirectoryExists).toBe (false)

                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "an import rollback failure keeps the window open and resets the close lifecycle",
            fun () -> promise {
                let mutable closeHandler: obj -> unit = ignore
                let mutable closeCount = 0
                let mutable preventedCloseCount = 0

                let window =
                    createObj [
                        "id" ==> 4
                        "title" ==> ""
                        "isDestroyed" ==> (fun () -> false)
                        "close" ==> (fun () -> closeCount <- closeCount + 1)
                        "on"
                        ==> (fun (eventName: string) (handler: obj -> unit) ->
                            if eventName = "close" then
                                closeHandler <- handler
                        )
                        "webContents"
                        ==> createObj [
                            "send" ==> (fun (_: string) (_: obj) -> ())
                            "isDestroyed" ==> (fun () -> false)
                        ]
                    ]
                    |> unbox<BrowserWindow>

                let vault = ArcVault(window)
                let abortController = Abort.AbortController.create ()

                vault.activeFileImport <-
                    Some {
                        State = {
                            requestId = "failed-rollback"
                            phase = FileImportPhase.Copying
                        }
                        AbortController = abortController
                        Completion = JS.Constructors.Promise.resolve (Error(exn "EPERM during rollback"))
                    }

                let vaults = ArcVaults()
                vaults.OnCloseWindow(window, vault, window.id)

                let closeEvent =
                    createObj [
                        "preventDefault" ==> (fun () -> preventedCloseCount <- preventedCloseCount + 1)
                    ]

                closeHandler closeEvent
                do! Promise.sleep 0

                Vitest.expect(preventedCloseCount).toBe (1)
                Vitest.expect(closeCount).toBe (0)
                Vitest.expect(abortController.signal.aborted).toBe (true)
                Vitest.expect(vault.isWaitingForImportCleanup).toBe (false)
            }
        )

        Vitest.test (
            "file watcher polling defaults to Windows only",
            fun () ->
                Vitest.expect(shouldUsePollingByDefault "win32").toBe (true)
                Vitest.expect(shouldUsePollingByDefault "WIN32").toBe (true)
                Vitest.expect(shouldUsePollingByDefault "linux").toBe (false)
                Vitest.expect(shouldUsePollingByDefault "darwin").toBe (false)
        )

        Vitest.test (
            "Git metadata path detection excludes only exact .git path segments",
            fun () ->
                Vitest.expect(isGitMetadataPath ".git").toBe (true)
                Vitest.expect(isGitMetadataPath ".git/objects/ab/object").toBe (true)
                Vitest.expect(isGitMetadataPath "notes\\.GIT\\config").toBe (true)
                Vitest.expect(isGitMetadataPath ".gitignore").toBe (false)
                Vitest.expect(isGitMetadataPath ".gitattributes").toBe (false)
                Vitest.expect(isGitMetadataPath "notes/my.git/file.txt").toBe (false)
        )

        Vitest.test (
            "legacy isa_datamap paths are ignored in favor of the canonical workbook",
            fun () ->
                Vitest.expect(isLegacyDataMapPath "assays/assay_1/isa_datamap").toBe (true)
                Vitest.expect(isLegacyDataMapPath "assays/assay_1/ISA_DATAMAP").toBe (true)
                Vitest.expect(isLegacyDataMapPath "assays/assay_1/isa.datamap.xlsx").toBe (false)
                Vitest.expect(isLegacyDataMapPath "assays/isa_datamap/data.txt").toBe (false)
        )

        Vitest.test (
            "legacy isa_datamap file is migrated to the canonical workbook path",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-legacy-datamap-"

                try
                    let assayFolder = join [| rootPath; "assays"; "assay_1" |]
                    let legacyPath = join [| assayFolder; "isa_datamap" |]
                    let canonicalPath = join [| assayFolder; "isa.datamap.xlsx" |]
                    do! mkdirRecursiveAsync assayFolder
                    do! writeTextFileAsync legacyPath "legacy-content"

                    let! migratedPaths = migrateLegacyDataMapPathsAsync rootPath [| "assays/assay_1/isa_datamap" |]

                    Vitest.expect(migratedPaths).toEqual ([| "assays/assay_1/isa.datamap.xlsx" |])
                    Vitest.expect(existsSync legacyPath).toBe (false)
                    Vitest.expect(existsSync canonicalPath).toBe (true)

                    let! migratedContent = readFileAsync canonicalPath TextEncoding.Utf8
                    Vitest.expect(migratedContent).toBe ("legacy-content")
                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "canonical DataMap workbook wins over a legacy isa_datamap file",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-canonical-datamap-"

                try
                    let assayFolder = join [| rootPath; "assays"; "assay_1" |]
                    let legacyPath = join [| assayFolder; "isa_datamap" |]
                    let canonicalPath = join [| assayFolder; "isa.datamap.xlsx" |]
                    do! mkdirRecursiveAsync assayFolder
                    do! writeTextFileAsync legacyPath "legacy-content"
                    do! writeTextFileAsync canonicalPath "canonical-content"

                    let! migratedPaths =
                        migrateLegacyDataMapPathsAsync rootPath [|
                            "assays/assay_1/isa_datamap"
                            "assays/assay_1/isa.datamap.xlsx"
                        |]

                    Vitest.expect(migratedPaths).toEqual ([| "assays/assay_1/isa.datamap.xlsx" |])
                    Vitest.expect(existsSync legacyPath).toBe (true)

                    let! canonicalContent = readFileAsync canonicalPath TextEncoding.Utf8
                    Vitest.expect(canonicalContent).toBe ("canonical-content")
                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "Swate write contracts contain only targeted scaffold and ARC files",
            fun () ->
                let arc = ARC("TargetedWriteArc")
                addDataMapToAllEntityTypes arc
                arc.SetLicenseFulltext("license text")

                arc.SetFilePaths(
                    [|
                        ".git/config"
                        "payload.txt"
                        "missing-payload.txt"
                        "assays/Assay With DataMap/README.md"
                    |]
                )

                let actualPaths = arc.GetWriteContractsSwate() |> Array.map _.Path |> Array.sort

                let expectedPaths =
                    [|
                        ".gitignore"
                        "LICENSE"
                        "assays/.gitkeep"
                        "assays/Assay With DataMap/isa.assay.xlsx"
                        "assays/Assay With DataMap/isa.datamap.xlsx"
                        "isa.investigation.xlsx"
                        "notes/README.md"
                        "runs/.gitkeep"
                        "runs/Run With DataMap/isa.datamap.xlsx"
                        "runs/Run With DataMap/isa.run.xlsx"
                        "studies/.gitkeep"
                        "studies/Study With DataMap/isa.datamap.xlsx"
                        "studies/Study With DataMap/isa.study.xlsx"
                        "workflows/.gitkeep"
                        "workflows/Workflow With DataMap/isa.datamap.xlsx"
                        "workflows/Workflow With DataMap/isa.workflow.xlsx"
                    |]
                    |> Array.sort

                Vitest.expect(actualPaths).toEqual (expectedPaths)
        )

        Vitest.test (
            "TryWriteAsyncSwate writes the default gitignore and notes README into an ARC root",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-default-gitignore-"
                let arcPath = join [| rootPath; "arc" |]
                let gitignorePath = join [| arcPath; ".gitignore" |]
                let notesReadmePath = join [| arcPath; NotesRootFolderName; NotesReadmeFileName |]

                try
                    do! mkdirRecursiveAsync arcPath

                    let arc = ARC("ScaffoldArc")

                    match! arc.TryWriteAsyncSwate(arcPath) with
                    | Error errors -> failwith (String.concat "\n" errors)
                    | Ok _ -> ()

                    let! gitignoreContent = readFileAsync gitignorePath TextEncoding.Utf8
                    let! notesReadmeContent = readFileAsync notesReadmePath TextEncoding.Utf8
                    Vitest.expect(gitignoreContent).toBe (arctrlDefaultGitignoreContent ())
                    Vitest.expect(notesReadmeContent).toBe (NotesReadmeContent)
                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "TryWriteAsyncSwate preserves payload and does not create unmanaged file-tree entries",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-targeted-write-"
                let arcPath = join [| rootPath; "arc" |]
                let gitFolder = join [| arcPath; ".git" |]
                let assayFolder = join [| arcPath; "assays"; "Assay 1" |]
                let payloadPath = join [| arcPath; "payload.txt" |]
                let gitConfigPath = join [| gitFolder; "config" |]
                let readmePath = join [| assayFolder; "README.md" |]
                let missingPayloadPath = join [| arcPath; "missing-payload.txt" |]

                try
                    do! mkdirRecursiveAsync gitFolder
                    do! mkdirRecursiveAsync assayFolder
                    do! writeTextFileAsync payloadPath "payload"
                    do! writeTextFileAsync gitConfigPath "git-config"
                    do! writeTextFileAsync readmePath "readme"

                    let arc = ARC("TargetedWriteArc")
                    arc.AddAssay(ArcAssay("Assay 1"))

                    arc.SetFilePaths(
                        [|
                            ".git/config"
                            "payload.txt"
                            "missing-payload.txt"
                            "assays/Assay 1/README.md"
                        |]
                    )

                    match! arc.TryWriteAsyncSwate(arcPath) with
                    | Error errors -> failwith (String.concat "\n" errors)
                    | Ok _ -> ()

                    let! payload = readFileAsync payloadPath TextEncoding.Utf8
                    let! gitConfig = readFileAsync gitConfigPath TextEncoding.Utf8
                    let! readme = readFileAsync readmePath TextEncoding.Utf8
                    let! missingPayloadExists = TestHelpers.pathExistsAsync missingPayloadPath

                    let! assayFileExists = TestHelpers.pathExistsAsync (join [| assayFolder; "isa.assay.xlsx" |])

                    let! collectionGitKeepExists =
                        TestHelpers.pathExistsAsync (join [| arcPath; "assays"; ".gitkeep" |])

                    Vitest.expect(payload).toBe ("payload")
                    Vitest.expect(gitConfig).toBe ("git-config")
                    Vitest.expect(readme).toBe ("readme")
                    Vitest.expect(missingPayloadExists).toBe (false)
                    Vitest.expect(assayFileExists).toBe (true)
                    Vitest.expect(collectionGitKeepExists).toBe (true)
                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "ARC vault save preserves note markdown files",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-save-notes-"
                    "NotesPreservingArc"
                    ignore
                    (fun arcPath -> promise {
                        let rootNotesFolder = join [| arcPath; "notes"; "2026-04-27"; "root_note" |]
                        let studyNotesFolder = join [| arcPath; "notes"; "2026-04-27"; "study_note" |]
                        let rootNotePath = join [| rootNotesFolder; "root_note.md" |]
                        let studyNotePath = join [| studyNotesFolder; "study_note.md" |]

                        let rootNoteContent = "---\ntitle: Root note\n---\n\nRoot note body."
                        let studyNoteContent = "---\ntitle: Study note\n---\n\nStudy note body."

                        do! mkdirRecursiveAsync rootNotesFolder
                        do! mkdirRecursiveAsync studyNotesFolder
                        do! writeTextFileAsync rootNotePath rootNoteContent
                        do! writeTextFileAsync studyNotePath studyNoteContent

                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        do! vault.LoadArc()

                        vault.arc.Value.Title <- Some "Saved title"
                        vault.arc.Value.StaticHash <- 0

                        match! vault.WriteArc() with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        let! rootNoteAfterSave = readFileAsync rootNotePath TextEncoding.Utf8
                        let! studyNoteAfterSave = readFileAsync studyNotePath TextEncoding.Utf8
                        let! persistedArc = TestHelpers.loadArcAsync arcPath

                        Vitest.expect(rootNoteAfterSave).toBe (rootNoteContent)
                        Vitest.expect(studyNoteAfterSave).toBe (studyNoteContent)
                        Vitest.expect(persistedArc.Title).toEqual (Some "Saved title")
                    })
        )

        Vitest.test (
            "ARC loading and writing ignore Git metadata and preserve payload",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-load-arc-ignore-git-"
                    "IgnoreGitArc"
                    ignore
                    (fun arcPath -> promise {
                        let gitObjectFolder = join [| arcPath; ".git"; "objects"; "ab" |]
                        let nestedGitFolder = join [| arcPath; "notes"; ".git" |]
                        let payloadPath = join [| arcPath; "payload.txt" |]
                        let gitObjectPath = join [| gitObjectFolder; "object" |]
                        do! mkdirRecursiveAsync gitObjectFolder
                        do! mkdirRecursiveAsync nestedGitFolder
                        do! writeTextFileAsync gitObjectPath "git-object"
                        do! writeTextFileAsync (join [| nestedGitFolder; "config" |]) "nested-git-config"
                        do! writeTextFileAsync payloadPath "payload"

                        let! loadResult = ARC.LoadAsyncSwate arcPath
                        let loadedArc = TestHelpers.expectLoadedArc loadResult
                        let paths = loadedArc.FileSystem.Tree.ToFilePaths()

                        Vitest.expect(paths |> Array.exists isGitMetadataPath).toBe (false)

                        loadedArc.SetFilePaths(Array.append paths [| ".git/objects/ab/object" |])
                        loadedArc.Title <- Some "Saved title"
                        do! loadedArc.UpdateAsync arcPath

                        let! payload = readFileAsync payloadPath TextEncoding.Utf8
                        let! gitObject = readFileAsync gitObjectPath TextEncoding.Utf8
                        Vitest.expect(payload).toBe ("payload")
                        Vitest.expect(gitObject).toBe ("git-object")
                    })
        )

        Vitest.test (
            "normal ARC save does not restore deleted DTO-less files from a stale file tree",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-save-deleted-file-"
                    "DeletedFileArc"
                    ignore
                    (fun arcPath -> promise {
                        let payloadPath = join [| arcPath; "payload.txt" |]
                        let collectionGitKeepPath = join [| arcPath; "assays"; ".gitkeep" |]
                        do! writeTextFileAsync payloadPath "payload"

                        let! loadResult = ARC.LoadAsyncSwate arcPath
                        let loadedArc = TestHelpers.expectLoadedArc loadResult
                        // Structural loading no longer lists payload files, so the stale tree is built explicitly.
                        let stalePaths =
                            Array.append (loadedArc.FileSystem.Tree.ToFilePaths()) [|
                                "payload.txt"
                                "assays/.gitkeep"
                            |]

                        loadedArc.SetFilePaths stalePaths

                        Vitest.expect(stalePaths |> Array.contains "payload.txt").toBe (true)
                        Vitest.expect(stalePaths |> Array.contains "assays/.gitkeep").toBe (true)

                        do! rmAsync payloadPath (RmOptions())
                        do! rmAsync collectionGitKeepPath (RmOptions())

                        loadedArc.Title <- Some "Saved title"
                        do! loadedArc.UpdateAsync arcPath

                        let! payloadExists = TestHelpers.pathExistsAsync payloadPath
                        let! collectionGitKeepExists = TestHelpers.pathExistsAsync collectionGitKeepPath
                        Vitest.expect(payloadExists).toBe (false)
                        Vitest.expect(collectionGitKeepExists).toBe (false)

                        let! reloadedArc = TestHelpers.loadArcAsync arcPath
                        Vitest.expect(reloadedArc.Title).toEqual (Some "Saved title")
                    })
        )

        Vitest.test (
            "LoadArc reports load errors without crashing the printf formatter",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-load-arc-error-"

                try
                    let vault = ArcVault(TestHelpers.testWindow ())
                    vault.path <- Some rootPath

                    let mutable capturedError: exn option = None

                    try
                        do! vault.LoadArc()
                    with error ->
                        capturedError <- Some error

                    match capturedError with
                    | None ->
                        do! TestHelpers.removeDirectoryAsync rootPath
                        return failwith "Expected LoadArc to fail for an invalid ARC folder."
                    | Some error ->
                        Vitest.expect(error.Message).toContain ("[ARCitISA-0] Unable to load ARC:")
                        Vitest.expect(error.Message).not.toContain ("fmt.cont")
                        do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "RenameOpenArcRoot moves the active ARC folder and updates the vault path",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-rename-arc-root-"
                    "RenameRootArc"
                    ignore
                    (fun arcPath -> promise {
                        let targetPath =
                            join [| dirname arcPath; "renamed-arc" |] |> PathHelpers.normalizePath

                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath

                        do! vault.LoadArc()

                        match! vault.RenameOpenArcRoot "renamed-arc" with
                        | Error error -> failwith error.Message
                        | Ok renamedPath ->
                            Vitest.expect(renamedPath).toBe (targetPath)
                            Vitest.expect(vault.path).toEqual (Some targetPath)

                            let! oldPathExists = TestHelpers.pathExistsAsync arcPath
                            let! newPathExists = TestHelpers.pathExistsAsync targetPath
                            Vitest.expect(oldPathExists).toBe (false)
                            Vitest.expect(newPathExists).toBe (true)

                            let! reloadedArc = TestHelpers.loadArcAsync targetPath
                            Vitest.expect(reloadedArc.Identifier).toBe ("RenameRootArc")
                    })
        )

        Vitest.test (
            "RenameOpenArcRoot clears pending watcher state before moving the active ARC",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-rename-arc-root-watcher-"
                    "RenameRootWatcherArc"
                    ignore
                    (fun arcPath -> promise {
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        do! vault.LoadArc()

                        let timeoutId = Fable.Core.JS.setTimeout (fun () -> ()) 60000

                        vault.fileWatcherReloadArcTimeout <- Some timeoutId

                        vault.fileWatcherPendingEvents.Add {
                            EventName = "change"
                            RelativePath = "isa.investigation.xlsx"
                            AbsolutePath = join [| arcPath; "isa.investigation.xlsx" |]
                        }

                        vault.fileWatcherPendingArcMergeEvents.Add {
                            EventName = "change"
                            RelativePath = "isa.investigation.xlsx"
                            AbsolutePath = join [| arcPath; "isa.investigation.xlsx" |]
                        }

                        try
                            match! vault.RenameOpenArcRoot "renamed-arc-watcher" with
                            | Error error -> failwith error.Message
                            | Ok _ ->
                                Vitest.expect(vault.fileWatcherReloadArcTimeout).toEqual (None)
                                Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe (0)
                                Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe (0)
                        finally
                            vault.fileWatcherReloadArcTimeout |> Option.iter Fable.Core.JS.clearTimeout
                    })
        )

        Vitest.test (
            "RenameOpenArcRoot rejects destination conflicts without moving the active ARC",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-rename-arc-root-conflict-"
                    "RenameRootConflictArc"
                    ignore
                    (fun arcPath -> promise {
                        let targetPath =
                            join [| dirname arcPath; "existing-arc" |] |> PathHelpers.normalizePath

                        do! mkdirRecursiveAsync targetPath

                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        do! vault.LoadArc()

                        match! vault.RenameOpenArcRoot "existing-arc" with
                        | Ok _ -> failwith "Expected active ARC root rename to reject an existing destination."
                        | Error error ->
                            Vitest.expect(error.Message).toContain ("destination already exists")
                            Vitest.expect(vault.path).toEqual (Some arcPath)

                            let! oldPathExists = TestHelpers.pathExistsAsync arcPath
                            let! targetPathExists = TestHelpers.pathExistsAsync targetPath
                            Vitest.expect(oldPathExists).toBe (true)
                            Vitest.expect(targetPathExists).toBe (true)
                    })
        )

        Vitest.test (
            "RenameOpenArcRoot refuses while a write runs and renames after it ends",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-rename-arc-root-busy-"
                    "RenameRootBusyArc"
                    ignore
                    (fun arcPath -> promise {
                        let targetPath =
                            join [| dirname arcPath; "renamed-arc-busy" |] |> PathHelpers.normalizePath

                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath
                        do! vault.LoadArc()

                        let mutable releaseWrite = ignore

                        let writeGate =
                            JS.Constructors.Promise.Create(fun resolve _ -> releaseWrite <- fun () -> resolve ())

                        let writeScope = vault.WithBusyWritingScope(fun () -> writeGate)

                        match! vault.RenameOpenArcRoot "renamed-arc-busy" with
                        | Ok _ -> failwith "Expected the ARC root rename to wait for the running write."
                        | Error _ ->
                            Vitest.expect(vault.path).toEqual (Some arcPath)
                            let! oldPathExists = TestHelpers.pathExistsAsync arcPath
                            let! targetPathExists = TestHelpers.pathExistsAsync targetPath
                            Vitest.expect(oldPathExists).toBe (true)
                            Vitest.expect(targetPathExists).toBe (false)

                        releaseWrite ()
                        do! writeScope

                        match! vault.RenameOpenArcRoot "renamed-arc-busy" with
                        | Error error -> failwith error.Message
                        | Ok renamedPath -> Vitest.expect(renamedPath).toBe (targetPath)
                    })
        )

        Vitest.test (
            "tryBuildOpenArcRootRenamePlan applies the shared rename-name validation rules",
            fun () ->
                match tryBuildOpenArcRootRenamePlan "C:/work/current-arc" "bad\u0000name" with
                | Ok _ -> failwith "Expected ARC root rename to reject null characters."
                | Error error -> Vitest.expect(error.Message).toContain ("null")
        )

        Vitest.test (
            "LoadArc repairs zero-byte canonical ARC workbooks before retrying",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-load-arc-repair-"
                    "RepairArc"
                    ignore
                    (fun arcPath -> promise {
                        let assayFolder = join [| arcPath; "assays"; "New Assay" |]
                        let assayFile = join [| assayFolder; "isa.assay.xlsx" |]

                        do! mkdirRecursiveAsync assayFolder
                        do! writeTextFileAsync assayFile ""

                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath

                        do! vault.LoadArc()

                        Vitest.expect(vault.arc.IsSome).toBe (true)
                        Vitest.expect(vault.arc.Value.ContainsAssay("New Assay")).toBe (true)
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                        Vitest.expect(vault.arc.Value.hasInMemoryChanges ()).toBe (false)

                        let! reloadedArc = TestHelpers.loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsAssay("New Assay")).toBe (true)
                    })
        )

        Vitest.test (
            "LoadArc baselines loaded datamap hashes without marking the ARC dirty",
            fun () ->
                TestHelpers.withTempArcWith
                    "swate-load-arc-datamap-baseline-"
                    "DatamapBaselineArc"
                    addDataMapToAllEntityTypes
                    (fun arcPath -> promise {
                        let vault = ArcVault(TestHelpers.testWindow ())
                        vault.path <- Some arcPath

                        do! vault.LoadArc()

                        Vitest.expect(vault.arc.IsSome).toBe (true)
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                        Vitest.expect(vault.arc.Value.hasInMemoryChanges ()).toBe (false)

                        let loadedArc = vault.arc.Value
                        Vitest.expect(loadedArc.GetAssay("Assay With DataMap").DataMap.Value.StaticHash).not.toBe (0)
                        Vitest.expect(loadedArc.GetStudy("Study With DataMap").DataMap.Value.StaticHash).not.toBe (0)

                        Vitest
                            .expect(loadedArc.GetWorkflow("Workflow With DataMap").DataMap.Value.StaticHash)
                            .not.toBe (0)

                        Vitest.expect(loadedArc.GetRun("Run With DataMap").DataMap.Value.StaticHash).not.toBe (0)
                    })
        )

        Vitest.test (
            "CreateARC write failure restores the current empty vault and leaves it reusable",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-write-rollback-"
                let blockedParent = join [| rootPath; "not-a-directory" |]
                let failedArcPath = join [| blockedParent; "failed-arc" |]
                let reusableArcPath = join [| rootPath; "reusable-arc" |]
                let windowId = 71
                let windowState = createTestWindow (testWindowOptions windowId)
                let sentMessages = windowState.SentMessages
                let vault = ArcVault(windowState.Window)
                let vaults = ArcVaults()
                let seededEntry = FileEntry.create ("seeded.txt", "seeded.txt", false)
                let mutable createdWindowCount = 0

                vault.FileTreeWriter.Tree.SetRoot seededEntry
                vaults.Vaults.Add(windowId, vault)
                do! writeTextFileAsync blockedParent "This file prevents creation of a nested ARC directory."

                setBrowserWindowFactory (fun _ ->
                    createdWindowCount <- createdWindowCount + 1
                    failwith "A reusable current vault must not create another BrowserWindow."
                )

                try
                    let mutable creationError: exn option = None

                    try
                        do! vault.CreateARC(failedArcPath, "Failed ARC")
                    with error ->
                        creationError <- Some error

                    Vitest.expect(creationError.IsSome).toBe (true)
                    Vitest.expect(creationError.Value.Message).toContain ("Could not write ARC")
                    Vitest.expect(vault.path).toEqual (None)
                    Vitest.expect(vault.arc).toEqual (None)
                    Vitest.expect(vault.watcher).toEqual (None)
                    Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (0)
                    Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                    Vitest.expect(vault.window.title).toBe (Swate.Electron.Shared.ApplicationVersion.windowTitle None)

                    Vitest
                        .expect(
                            sentMessages
                            |> Seq.exists (fun message ->
                                JS.JSON.stringify(message).Contains(PathHelpers.normalizePath failedArcPath)
                            )
                        )
                        .toBe (false)

                    let dirtyStateMessages =
                        sentMessages |> Seq.choose tryGetDirtyStateMessage |> Seq.toArray

                    Vitest.expect(dirtyStateMessages).toEqual ([| true; false |])

                    match! vaults.CreateOrFocusArc(windowId, reusableArcPath, "Reusable ARC") with
                    | ArcOpenDisposition.CreatedInCurrent createdPath ->
                        Vitest.expect(createdPath).toBe (PathHelpers.normalizePath reusableArcPath)
                    | disposition -> failwithf "Expected the current vault to be reused, received %A." disposition

                    Vitest.expect(createdWindowCount).toBe (0)
                    Vitest.expect(vault.path).toEqual (Some(PathHelpers.normalizePath reusableArcPath))
                    Vitest.expect(vault.arc.IsSome).toBe (true)
                    Vitest.expect(vault.watcher.IsSome).toBe (true)

                    do! vault.StopFileWatcher()
                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! vault.StopFileWatcher()
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "CreateARC preserves the write failure when rollback dirty-state notification throws",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-notification-rollback-"
                let blockedParent = join [| rootPath; "not-a-directory" |]
                let failedArcPath = join [| blockedParent; "failed-arc" |]
                let rollbackError = exn "Expected rollback renderer notification failure."

                let windowState =
                    createTestWindow {
                        testWindowOptions 73 with
                            OnSend =
                                fun args ->
                                    match tryGetDirtyStateMessage args with
                                    | Some false -> raise rollbackError
                                    | _ -> ()
                    }

                let sentMessages = windowState.SentMessages
                let vault = ArcVault(windowState.Window)
                let seededEntry = FileEntry.create ("seeded.txt", "seeded.txt", false)
                vault.FileTreeWriter.Tree.SetRoot seededEntry
                do! writeTextFileAsync blockedParent "This file prevents creation of a nested ARC directory."

                try
                    let mutable creationError: exn option = None

                    try
                        do! vault.CreateARC(failedArcPath, "Failed ARC")
                    with error ->
                        creationError <- Some error

                    Vitest.expect(creationError.IsSome).toBe (true)
                    Vitest.expect(vault.path).toEqual (None)
                    Vitest.expect(vault.arc).toEqual (None)
                    Vitest.expect(vault.watcher).toEqual (None)
                    Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (0)
                    Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                    Vitest.expect(creationError.Value).not.toBe (rollbackError)
                    Vitest.expect(creationError.Value.Message).toContain ("Could not write ARC")
                    Vitest.expect(creationError.Value.Message).not.toContain (rollbackError.Message)

                    let dirtyStateMessages =
                        sentMessages |> Seq.choose tryGetDirtyStateMessage |> Seq.toArray

                    Vitest.expect(dirtyStateMessages).toEqual ([| true; false |])

                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! vault.StopFileWatcher()
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "CreateARC Startup failure preserves the written ARC while restoring the empty vault",
            fun () -> promise {
                let! rootPath = TestHelpers.createTempDirectoryAsync "swate-create-startup-rollback-"
                let arcPath = join [| rootPath; "written-arc" |]
                let startupError = exn "Expected ARC reload title failure."

                let windowState =
                    createTestWindow {
                        testWindowOptions 72 with
                            OnTitleWrite =
                                fun writeCount _ ->
                                    if writeCount = 2 then
                                        raise startupError
                    }

                let sentMessages = windowState.SentMessages
                let vault = ArcVault(windowState.Window)
                let seededEntry = FileEntry.create ("seeded.txt", "seeded.txt", false)
                vault.FileTreeWriter.Tree.SetRoot seededEntry

                try
                    let mutable creationError: exn option = None

                    try
                        do! vault.CreateARC(arcPath, "Persisted ARC")
                    with error ->
                        creationError <- Some error

                    Vitest.expect(creationError).toEqual (Some startupError)
                    Vitest.expect(vault.path).toEqual (None)
                    Vitest.expect(vault.arc).toEqual (None)
                    Vitest.expect(vault.watcher).toEqual (None)
                    Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (0)
                    Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                    Vitest.expect(vault.window.title).toBe (Swate.Electron.Shared.ApplicationVersion.windowTitle None)

                    Vitest
                        .expect(
                            sentMessages
                            |> Seq.exists (fun message ->
                                JS.JSON.stringify(message).Contains(PathHelpers.normalizePath arcPath)
                            )
                        )
                        .toBe (false)

                    let investigationPath =
                        ARCtrl.ArcPathHelper.combine arcPath ARCtrl.ArcPathHelper.InvestigationFileName

                    let! arcDirectoryExists = TestHelpers.pathExistsAsync arcPath
                    let! investigationExists = TestHelpers.pathExistsAsync investigationPath
                    Vitest.expect(arcDirectoryExists).toBe (true)
                    Vitest.expect(investigationExists).toBe (true)

                    do! TestHelpers.removeDirectoryAsync rootPath
                with error ->
                    do! vault.StopFileWatcher()
                    do! TestHelpers.removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "OpenARC clears pre-existing vault state after opening a non-ARC folder fails",
            fun () -> promise {
                let! folderPath = TestHelpers.createTempDirectoryAsync "swate-open-invalid-arc-"

                try
                    let vault = ArcVault(TestHelpers.testWindow ())
                    let seededEntry = FileEntry.create ("seeded.txt", "seeded.txt", false)
                    vault.FileTreeWriter.Tree.SetRoot seededEntry
                    let mutable failed = false

                    Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (1)

                    try
                        do! vault.OpenARC folderPath
                    with _ ->
                        failed <- true

                    Vitest.expect(failed).toBe (true)
                    Vitest.expect(vault.path).toEqual (None)
                    Vitest.expect(vault.arc).toEqual (None)
                    Vitest.expect(vault.watcher).toEqual (None)
                    Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe (0)
                    do! TestHelpers.removeDirectoryAsync folderPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync folderPath
                    return raise error
            }
        )

        Vitest.test (
            "Startup does not start a watcher when ARC loading fails",
            fun () -> promise {
                let! folderPath = TestHelpers.createTempDirectoryAsync "swate-invalid-arc-startup-"

                try
                    let vault = ArcVault(TestHelpers.testWindow ())
                    vault.path <- Some folderPath
                    let mutable startupError: exn option = None

                    try
                        do! vault.Startup()
                    with error ->
                        startupError <- Some error

                    Vitest.expect(startupError.IsSome).toBe (true)
                    Vitest.expect(startupError.Value.Message).toContain (ARCtrl.ArcPathHelper.InvestigationFileName)
                    Vitest.expect(vault.arc).toEqual (None)
                    Vitest.expect(vault.watcher).toEqual (None)
                    do! TestHelpers.removeDirectoryAsync folderPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync folderPath
                    return raise error
            }
        )

        Vitest.test (
            "ClearArc resets the dirty state and window title",
            fun () ->
                let vault = ArcVault(TestHelpers.testWindow ())
                vault.SetArc(ARC("LoadedArc"))
                vault.RefreshHasUnsavedArcChangesFlag()

                vault.ClearArc()

                Vitest.expect(vault.arc).toEqual (None)
                Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                Vitest.expect(vault.window.title).toBe (Swate.Electron.Shared.ApplicationVersion.windowTitle None)
        )

        Vitest.test (
            "ArcLoadCancelledException includes the closed window id in its message",
            fun () ->
                let error = ArcLoadCancelledException 42
                Vitest.expect(error.Message).toContain ("window 42")
        )

)
