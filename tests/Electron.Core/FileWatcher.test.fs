module ElectronCore.FileWatcherTests

open System.Collections.Generic
open ARCtrl
open Fable.Core
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultLargeObjects
open Main.ArcVaultFileTree
open Main.ArcVaultFileWatcher
open Main.ArcVaultHelper
open Main.Bindings
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Vitest

module WatcherHelpers = Main.WatcherHelpers

/// A chokidar stand-in that tests drive by hand.
[<Emit("(() => { const handlers = {}; const w = { closed: false, on(e, cb) { (handlers[e] = handlers[e] || []).push(cb); return w; }, emit(e, ...a) { for (const cb of handlers[e] || []) cb(...a); }, close() { w.closed = true; return Promise.resolve(); }, add() { return w; }, unwatch() { return w; }, getWatched() { return {}; } }; return w; })()")>]
let private createFakeWatcher () : obj = jsNative

[<Emit("$0.emit($1)")>]
let private emitSignal (fake: obj) (eventName: string) : unit = jsNative

[<Emit("$0.emit('all', $1, $2)")>]
let private emitFileEvent (fake: obj) (eventName: string) (path: string) : unit = jsNative

[<Emit("$0.emit('error', Object.assign(new Error($1), { code: $2 }))")>]
let private emitError (fake: obj) (message: string) (code: string) : unit = jsNative

[<Emit("$0.emit('error', Object.assign(new Error($1), { code: $2, path: $3 }))")>]
let private emitErrorAt (fake: obj) (message: string) (code: string) (path: string) : unit = jsNative

[<Emit("$0.closed")>]
let private isClosed (fake: obj) : bool = jsNative

/// Returns a factory that hands out the fake and reports it ready a moment later, after the start has attached its listeners.
let private readyShortlyFactory (fake: obj) =
    fun (_: string) (_: bool option) ->
        JS.setTimeout (fun () -> emitSignal fake "ready") 1 |> ignore
        unbox<Chokidar.IWatcher> fake

let private noopSink = fun (_: bool) -> ()

let private watcherApi: IArcFileWatcherApi = { IsLoadingChanges = noopSink }

let private withTempArc =
    TestHelpers.withTempArcWith "swate-structural-watcher-" "StructuralWatcherArc"

let private seedEntities (arc: ARC) =
    arc.AddStudy(ArcStudy("S1", title = "Old title"))
    arc.AddAssay(ArcAssay("A1"))

let private newVault (arcPath: string) =
    let vault = ArcVault(TestHelpers.testWindow ())
    vault.path <- Some arcPath
    vault

let private waitUntil (what: string) (timeoutMs: int) (condition: unit -> bool) = promise {
    let mutable waited = 0

    while not (condition ()) && waited < timeoutMs do
        do! Promise.sleep 20
        waited <- waited + 20

    if not (condition ()) then
        failwith $"Timed out after {timeoutMs} ms waiting for {what}."
}

/// No reload timer is armed and nothing is queued, so no merge is in flight.
let private isWatcherQuiet (vault: ArcVault) =
    vault.fileWatcherReloadArcTimeout.IsNone
    && vault.fileWatcherPendingEvents.Count = 0
    && vault.fileWatcherPendingArcMergeEvents.Count = 0

let private isUnavailable (state: FileWatcherState) =
    match state with
    | FileWatcherState.Unavailable _ -> true
    | _ -> false

let private fastTuning = {
    PollingIntervalMs = 50
    BinaryPollingIntervalMs = 50
    WriteStabilityMs = 50
    WritePollMs = 20
}

let private readyAsync (watcher: Chokidar.IWatcher) : JS.Promise<unit> =
    JS.Constructors.Promise.Create(fun resolve _ ->
        watcher.on (Chokidar.Events.Ready, (fun () -> resolve ())) |> ignore
    )

let private collectEvents (watcher: Chokidar.IWatcher) =
    let events = ResizeArray<string * string>()

    watcher.on (
        Chokidar.Events.All,
        fun (eventName: string) (path: string) -> events.Add((eventName, PathHelpers.normalizePath path))
    )
    |> ignore

    events

let private writePayloadFile (arcPath: string) (relativePath: string) (content: string) =
    let filePath = join [| arcPath; relativePath |]
    mkdirSync (dirname filePath) (MkdirOptions(recursive = true))
    writeFileSync filePath content TextEncoding.Utf8

let private structuralScopeCase (name: string) (usePolling: bool) =
    Vitest.test (
        $"the structural watcher ignores payload changes and reports entity file changes ({name})",
        fun () ->
            withTempArc
                seedEntities
                (fun arcPath -> promise {
                    for index in 1..20 do
                        writePayloadFile arcPath $"assays/A1/dataset/data{index}.txt" "payload"

                    writePayloadFile arcPath "studies/S1/resources/table.csv" "a,b"
                    writePayloadFile arcPath "extra/deep/file.txt" "deep"

                    let watcher = createFileWatcherWith fastTuning arcPath (Some usePolling)
                    let events = collectEvents watcher
                    do! readyAsync watcher

                    try
                        let watched = watcher.getWatched ()

                        // Chokidar keys the directories relative to the watch root with the native separator.
                        let watchedAt (relativePath: string) =
                            let children = watched.item (join (relativePath.Split('/')))
                            if isNull (box children) then None else Some children

                        Vitest.expect((watchedAt "assays/A1").IsSome).toBe true
                        Vitest.expect((watchedAt "assays/A1/dataset").IsNone).toBe true
                        Vitest.expect((watchedAt "studies/S1/resources").IsNone).toBe true

                        writePayloadFile arcPath "assays/A1/dataset/data1.txt" "changed"
                        writePayloadFile arcPath "assays/A1/dataset/added.txt" "new"
                        writePayloadFile arcPath "studies/S1/resources/table.csv" "c,d"
                        writePayloadFile arcPath "extra/deep/file.txt" "changed"
                        writePayloadFile arcPath "README.md" "readme"
                        writePayloadFile arcPath "assays/A1/.DS_Store" "junk"
                        do! Promise.sleep 1000

                        Vitest.expect(events.Count).toBe 0

                        writePayloadFile arcPath "assays/A1/isa.assay.xlsx" "changed"

                        do!
                            waitUntil
                                "the assay file event"
                                10000
                                (fun () ->
                                    events |> Seq.exists (fun (_, path) -> path.EndsWith "assays/A1/isa.assay.xlsx")
                                )

                        Vitest
                            .expect(
                                events
                                |> Seq.exists (fun (_, path) ->
                                    path.Contains "dataset" || path.Contains "resources" || path.Contains "README"
                                )
                            )
                            .toBe
                            false
                    finally
                        watcher.close () |> Promise.start
                })
    )

Vitest.describe (
    "Structural file watcher",
    fun () ->
        structuralScopeCase "native events" false
        structuralScopeCase "polling" true

        Vitest.test (
            "an atomic save of isa.study.xlsx produces one merge batch",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = newVault arcPath
                        vault.SetArc loadedArc
                        do! vault.EnqueueFileTreeScan()

                        // The batch has to re-add the entry, so the single reconcile job shows as one publish.
                        let root = Main.FileTreeCreator.normalizeRootPath arcPath

                        vault.FileTreeWriter.Tree.RemoveSubtree(root + "/studies/S1/isa.study.xlsx")
                        |> ignore

                        let publishCount = ref 0
                        vault.FileTreeWriter.Publish <- fun _ -> publishCount.Value <- publishCount.Value + 1

                        // A real atomic save writes new bytes, so the replacement comes from an edited copy of the ARC.
                        let alternatePath = join [| dirname arcPath; "alternate" |]
                        let! alternateArc = TestHelpers.loadArcAsync arcPath
                        alternateArc.GetStudy("S1").Title <- Some "Atomic save"
                        do! alternateArc.WriteAsync alternatePath

                        vault.FileWatcherFactory <- fun path polling -> createFileWatcherWith fastTuning path polling
                        let mergeCount = ref 0

                        vault.WatcherMergeBarrier <-
                            Some(fun () ->
                                mergeCount.Value <- mergeCount.Value + 1
                                promise { return () }
                            )

                        let! outcome = vault.StartFileWatcherAsync(false)

                        Vitest
                            .expect(
                                match outcome with
                                | FileWatcherStartOutcome.Ready -> true
                                | _ -> false
                            )
                            .toBe
                            true

                        let! settled = promise {
                            try
                                let studyPath = join [| arcPath; "studies/S1/isa.study.xlsx" |]
                                let replacementPath = studyPath + ".tmp"

                                do!
                                    copyFileAsync
                                        (join [| alternatePath; "studies/S1/isa.study.xlsx" |])
                                        replacementPath
                                        0

                                unlinkSync studyPath
                                renameSync replacementPath studyPath

                                do! waitUntil "the merge" 10000 (fun () -> mergeCount.Value > 0)
                                do! waitUntil "the reload to settle" 10000 (fun () -> isWatcherQuiet vault)
                                do! Promise.sleep 700
                                return Ok mergeCount.Value
                            with error ->
                                return Error error
                        }

                        do! vault.StopFileWatcher()

                        match settled with
                        | Ok count -> Vitest.expect(count).toBe 1
                        | Error error -> raise error

                        Vitest.expect(publishCount.Value).toBe 1
                    })
        )

        Vitest.test (
            "an event during initialization is applied after the snapshot is installed",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = newVault arcPath
                        vault.SetArc loadedArc
                        vault.isInitializingArc <- true
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- readyShortlyFactory fake

                        let! _ = vault.StartFileWatcherAsync()

                        let! diskArc = TestHelpers.loadArcAsync arcPath
                        diskArc.GetStudy("S1").Title <- Some "Changed on disk"
                        do! diskArc.UpdateAsync arcPath

                        emitFileEvent fake "change" (join [| arcPath; "studies/S1/isa.study.xlsx" |])

                        Vitest.expect(vault.BufferedFileWatcherEvents.Count).toBe 1
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 0

                        do! vault.FinalizeArcInitialization(Main.IndexedFileTree.IndexedFileTree(false))

                        Vitest.expect(vault.BufferedFileWatcherEvents.Count).toBe 0
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 1

                        do!
                            waitUntil
                                "the study merge"
                                10000
                                (fun () -> vault.arc.Value.GetStudy("S1").Title = Some "Changed on disk")

                        do! waitUntil "the reload to settle" 10000 (fun () -> isWatcherQuiet vault)
                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "a readiness timeout degrades the watcher and does not fail",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.FileWatcherReadinessTimeoutMs <- 50
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- fun _ _ -> unbox<Chokidar.IWatcher> fake

                        let! outcome = vault.StartFileWatcherAsync()

                        Vitest
                            .expect(
                                match outcome with
                                | FileWatcherStartOutcome.TimedOut -> true
                                | _ -> false
                            )
                            .toBe
                            true

                        Vitest.expect(isClosed fake).toBe true
                        Vitest.expect(vault.watcher.IsNone).toBe true

                        Vitest
                            .expect(
                                match vault.fileWatcherState with
                                | FileWatcherState.Degraded _ -> true
                                | _ -> false
                            )
                            .toBe
                            true
                    })
        )

        Vitest.test (
            "a post-ready error restarts the watcher and revalidates the loaded directories",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        do! vault.EnqueueFileTreeScan()
                        let root = Main.FileTreeCreator.normalizeRootPath arcPath
                        vault.FileWatcherRestartBackoffMs <- [| 20; 40; 60 |]
                        let created = ResizeArray<obj>()

                        vault.FileWatcherFactory <-
                            fun _ _ ->
                                let fake = createFakeWatcher ()
                                created.Add fake
                                JS.setTimeout (fun () -> emitSignal fake "ready") 1 |> ignore
                                unbox<Chokidar.IWatcher> fake

                        let! _ = vault.StartFileWatcherAsync()
                        Vitest.expect(vault.fileWatcherState).toEqual FileWatcherState.Running

                        // The new folder appears while the watcher is down. Only a revalidation finds it.
                        writePayloadFile arcPath "missed/file.txt" "x"
                        let scanCountBefore = vault.FileTreeWriter.TreeVersion

                        emitError created.[0] "watch failed" "EIO"
                        do! waitUntil "the replacement watcher" 5000 (fun () -> created.Count = 2)
                        Vitest.expect(isClosed created.[0]).toBe true

                        do!
                            waitUntil
                                "the running state"
                                5000
                                (fun () -> vault.fileWatcherState = FileWatcherState.Running)

                        do!
                            waitUntil
                                "the revalidated tree"
                                10000
                                (fun () -> (vault.FileTreeWriter.Tree.TryFind(root + "/missed")).IsSome)

                        Vitest.expect(vault.FileTreeWriter.TreeVersion).toBe (scanCountBefore + 1)
                        do! waitUntil "the reload to settle" 10000 (fun () -> isWatcherQuiet vault)
                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "a per-path permission error after ready keeps the watcher and is logged once",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- readyShortlyFactory fake
                        let! _ = vault.StartFileWatcherAsync()

                        emitErrorAt fake "denied" "EPERM" "/arc/locked"
                        emitErrorAt fake "denied" "EPERM" "/arc/locked"
                        emitErrorAt fake "denied" "EACCES" "/arc/locked"

                        Vitest.expect(vault.fileWatcherState).toEqual FileWatcherState.Running
                        Vitest.expect(vault.watcher.IsSome).toBe true
                        Vitest.expect(isClosed fake).toBe false
                        Vitest.expect(vault.FileWatcherRestartTimeout.IsNone).toBe true
                        Vitest.expect(vault.ReportedFileWatcherPermissionErrors.Count).toBe 2
                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "a stop settles a start that still waits for ready",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.FileWatcherReadinessTimeoutMs <- 60000
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- fun _ _ -> unbox<Chokidar.IWatcher> fake

                        // The outcome is collected through a callback, so the start has begun before the stop runs.
                        let startOutcome = ref None

                        vault.StartFileWatcherAsync()
                        |> Promise.iter (fun value -> startOutcome.Value <- Some value)

                        do! vault.StopFileWatcher()
                        do! waitUntil "the start to settle" 2000 (fun () -> startOutcome.Value.IsSome)
                        let outcome = startOutcome.Value.Value

                        Vitest
                            .expect(
                                match outcome with
                                | FileWatcherStartOutcome.Stopped -> true
                                | _ -> false
                            )
                            .toBe
                            true

                        Vitest.expect(vault.fileWatcherState).toEqual FileWatcherState.Stopped
                        Vitest.expect(vault.watcher.IsNone).toBe true
                        Vitest.expect(isClosed fake).toBe true
                    })
        )

        Vitest.test (
            "a start failure during initialization schedules a restart that waits for the initial snapshot",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.isInitializingArc <- true
                        vault.FileWatcherReadinessTimeoutMs <- 50
                        vault.FileWatcherRestartBackoffMs <- [| 20; 20; 20 |]
                        let created = ResizeArray<obj>()

                        vault.FileWatcherFactory <-
                            fun _ _ ->
                                let fake = createFakeWatcher ()
                                created.Add fake

                                if created.Count > 1 then
                                    JS.setTimeout (fun () -> emitSignal fake "ready") 1 |> ignore

                                unbox<Chokidar.IWatcher> fake

                        do! vault.Startup()

                        Vitest.expect(vault.FileWatcherRestartTimeout.IsSome).toBe true

                        // The timer fires several times while initializing and waits at the same backoff step.
                        do! Promise.sleep 200
                        Vitest.expect(created.Count).toBe 1
                        Vitest.expect(vault.FileWatcherRestartTimeout.IsSome).toBe true
                        Vitest.expect(vault.FileWatcherRestartAttempt).toBe 1

                        vault.isInitializingArc <- false
                        do! waitUntil "the restarted watcher" 5000 (fun () -> created.Count = 2)

                        do!
                            waitUntil
                                "the running state"
                                5000
                                (fun () -> vault.fileWatcherState = FileWatcherState.Running)

                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "a restart never revives a stopped vault",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        do! vault.StopFileWatcher()
                        vault.ScheduleFileWatcherRestart None
                        Vitest.expect(vault.FileWatcherRestartTimeout.IsNone).toBe true
                        Vitest.expect(vault.fileWatcherState).toEqual FileWatcherState.Stopped
                    })
        )

        Vitest.test (
            "an import builds events only for the paths that the structural watcher reports",
            fun () ->
                let events =
                    WatcherHelpers.createImportedWatcherEvents "/arc" "assays/A1" {
                        FolderPaths = [||]
                        FilePaths = [| "isa.assay.xlsx"; "data.csv" |]
                    }

                Vitest.expect(events |> Array.map _.RelativePath).toEqual [| "assays/A1/isa.assay.xlsx" |]
        )

        Vitest.test (
            "a file imported into a zone folder gets no event because it is a file",
            fun () ->
                let events =
                    WatcherHelpers.createImportedWatcherEvents "/arc" "assays" {
                        FolderPaths = [||]
                        FilePaths = [| "notes.txt" |]
                    }

                Vitest.expect(events.Length).toBe 0
        )

        Vitest.test (
            "a completed import releases every mark when the watcher is not running",
            fun () ->
                let vault = ArcVault(TestHelpers.testWindow ())

                let events =
                    WatcherHelpers.createImportedWatcherEvents "/arc" "assays/A1" {
                        FolderPaths = [||]
                        FilePaths = [| "isa.assay.xlsx" |]
                    }

                let token = vault.StartImport()
                vault.ClaimImportedFileEvents(token, events)
                vault.ReleaseImportedFileEventsAfterImport(token, Ok ImportExternalFilesResult.Completed)

                Vitest.expect(vault.IsImportedWatcherEvent events.[0]).toBe false
                Vitest.expect(vault.importedPathOwners.Count).toBe 0
                Vitest.expect(vault.importedPathLeases.Count).toBe 0
        )

        Vitest.test (
            "a root rename resyncs after the restart it took over reaches ready",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        let watchedRoots = ResizeArray<string>()
                        let fake = createFakeWatcher ()

                        vault.FileWatcherFactory <-
                            fun root polling ->
                                watchedRoots.Add root
                                readyShortlyFactory fake root polling

                        vault.FileWatcherRestartTimeout <- Some(JS.setTimeout ignore 60000)

                        try
                            match! vault.RenameOpenArcRoot "renamed-pending-restart" with
                            | Error error -> failwith error.Message
                            | Ok renamedPath ->
                                // The resync replays the entity files as watcher events.
                                do!
                                    waitUntil
                                        "the resync events"
                                        5000
                                        (fun () ->
                                            vault.fileWatcherPendingEvents.Count > 0
                                            || vault.fileWatcherReloadArcTimeout.IsSome
                                        )

                                Vitest.expect(watchedRoots.ToArray()).toEqual [| renamedPath |]
                                Vitest.expect(vault.FileWatcherRestartTimeout.IsNone).toBe true
                                vault.CancelFileWatcherRestart()
                                do! vault.StopFileWatcher()
                        finally
                            vault.CancelFileWatcherRestart()
                            vault.ClearPendingFileWatcherState()
                    })
        )

        Vitest.test (
            "a root rename schedules a new restart when the taken-over start times out",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.FileWatcherReadinessTimeoutMs <- 30
                        vault.FileWatcherRestartBackoffMs <- [| 60000 |]
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- fun _ _ -> unbox<Chokidar.IWatcher> fake
                        vault.FileWatcherRestartTimeout <- Some(JS.setTimeout ignore 60000)

                        try
                            match! vault.RenameOpenArcRoot "renamed-restart-timeout" with
                            | Error error -> failwith error.Message
                            | Ok _ ->
                                do!
                                    waitUntil
                                        "the next restart"
                                        5000
                                        (fun () -> vault.FileWatcherRestartTimeout.IsSome)

                                vault.CancelFileWatcherRestart()
                                do! vault.StopFileWatcher()
                        finally
                            vault.CancelFileWatcherRestart()
                            vault.ClearPendingFileWatcherState()
                    })
        )

        Vitest.test (
            "a root rename schedules a backoff restart when the restarted running watcher times out",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        let created = ResizeArray<obj>()

                        vault.FileWatcherFactory <-
                            fun root polling ->
                                let fake = createFakeWatcher ()
                                created.Add fake

                                if created.Count = 1 then
                                    readyShortlyFactory fake root polling
                                else
                                    unbox<Chokidar.IWatcher> fake

                        let! _ = vault.StartFileWatcherAsync()
                        vault.FileWatcherReadinessTimeoutMs <- 30
                        vault.FileWatcherRestartBackoffMs <- [| 60000 |]

                        try
                            match! vault.RenameOpenArcRoot "renamed-running-timeout" with
                            | Error error -> failwith error.Message
                            | Ok _ ->
                                do!
                                    waitUntil
                                        "the backoff restart"
                                        5000
                                        (fun () -> vault.FileWatcherRestartTimeout.IsSome)

                                Vitest.expect(created.Count).toBe 2
                        finally
                            vault.CancelFileWatcherRestart()
                            vault.ClearPendingFileWatcherState()
                    })
        )

        Vitest.test (
            "a watcher error forgets the imported paths",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.FileWatcherRestartBackoffMs <- [| 60000 |]
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- readyShortlyFactory fake
                        let! _ = vault.StartFileWatcherAsync()

                        try
                            vault.ClaimImportedFileEvents(
                                vault.StartImport(),
                                [|
                                    WatcherHelpers.buildWatcherEvent arcPath "add" ".gitattributes"
                                |]
                            )

                            emitError fake "watch failed" "EIO"

                            Vitest.expect(vault.importedPathOwners.Count).toBe 0
                            Vitest.expect(vault.importedPathLeases.Count).toBe 0
                        finally
                            vault.CancelFileWatcherRestart()
                            vault.ClearPendingFileWatcherState()

                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "a failed root rename runs the restart that was pending",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        let watchedRoots = ResizeArray<string>()
                        let fake = createFakeWatcher ()

                        vault.FileWatcherFactory <-
                            fun root polling ->
                                watchedRoots.Add root
                                readyShortlyFactory fake root polling

                        mkdirSync
                            (join [| dirname arcPath; "existing-pending-restart" |])
                            (MkdirOptions(recursive = true))

                        vault.FileWatcherRestartTimeout <- Some(JS.setTimeout ignore 60000)

                        try
                            match! vault.RenameOpenArcRoot "existing-pending-restart" with
                            | Ok _ -> failwith "Expected the rename to reject an existing destination."
                            | Error _ ->
                                do! waitUntil "the watcher on the old root" 5000 (fun () -> vault.watcher.IsSome)
                                Vitest.expect(watchedRoots.ToArray()).toEqual [| arcPath |]
                                Vitest.expect(vault.FileWatcherRestartTimeout.IsNone).toBe true
                                vault.CancelFileWatcherRestart()
                                do! vault.StopFileWatcher()
                        finally
                            vault.CancelFileWatcherRestart()
                            vault.ClearPendingFileWatcherState()
                    })
        )

        Vitest.test (
            "no tree snapshot is scanned while the ARC initializes",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.isInitializingArc <- true
                        let! snapshot = vault.GetFileTreeSnapshot()

                        Vitest.expect(snapshot.entries.Length).toBe 0
                        Vitest.expect(vault.FileTreeWriter.Tree.Count).toBe 0
                    })
        )

        Vitest.test (
            "events that arrive while the snapshot is installed stay buffered until the flush",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let! loadedArc = TestHelpers.loadArcAsync arcPath
                        let vault = newVault arcPath
                        vault.SetArc loadedArc
                        vault.isInitializingArc <- true
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- readyShortlyFactory fake
                        let! _ = vault.StartFileWatcherAsync()

                        emitFileEvent fake "change" (join [| arcPath; "studies/S1/isa.study.xlsx" |])

                        let finalized = ref false

                        vault.FinalizeArcInitialization(Main.IndexedFileTree.IndexedFileTree(false))
                        |> Promise.iter (fun () -> finalized.Value <- true)

                        emitFileEvent fake "change" (join [| arcPath; "assays/A1/isa.assay.xlsx" |])

                        Vitest.expect(vault.BufferedFileWatcherEvents.Count).toBe 2
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 0

                        do! waitUntil "the initialization to finish" 5000 (fun () -> finalized.Value)

                        Vitest.expect(vault.BufferedFileWatcherEvents.Count).toBe 0
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 2
                        do! waitUntil "the reload to settle" 10000 (fun () -> isWatcherQuiet vault)
                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "ENOSPC stops the restarts after the last backoff step and marks file watching unavailable",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        vault.FileWatcherRestartBackoffMs <- [| 10; 10; 10 |]
                        let created = ResizeArray<obj>()

                        vault.FileWatcherFactory <-
                            fun _ _ ->
                                let fake = createFakeWatcher ()
                                let isFirst = created.Count = 0
                                created.Add fake

                                if isFirst then
                                    JS.setTimeout (fun () -> emitSignal fake "ready") 2 |> ignore
                                    JS.setTimeout (fun () -> emitError fake "no watches left" "ENOSPC") 15 |> ignore
                                else
                                    JS.setTimeout (fun () -> emitError fake "no watches left" "ENOSPC") 5 |> ignore

                                unbox<Chokidar.IWatcher> fake

                        let! _ = vault.StartFileWatcherAsync()

                        do! waitUntil "the unavailable state" 10000 (fun () -> isUnavailable vault.fileWatcherState)

                        Vitest.expect(created.Count).toBe 4
                        do! Promise.sleep 200
                        Vitest.expect(created.Count).toBe 4
                        Vitest.expect(vault.watcher.IsNone).toBe true
                        do! vault.StopFileWatcher()
                    })
        )

        Vitest.test (
            "events from a closed watcher instance are ignored",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        let fake = createFakeWatcher ()
                        vault.FileWatcherFactory <- readyShortlyFactory fake

                        let! _ = vault.StartFileWatcherAsync()

                        emitFileEvent fake "change" (join [| arcPath; "studies/S1/isa.study.xlsx" |])
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 1

                        do! vault.StopFileWatcher()
                        Vitest.expect(isClosed fake).toBe true

                        emitFileEvent fake "change" (join [| arcPath; "studies/S1/isa.study.xlsx" |])
                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 0
                        Vitest.expect(vault.BufferedFileWatcherEvents.Count).toBe 0
                    })
        )

        Vitest.test (
            "a batch of directory additions does not load the ARC",
            fun () ->
                withTempArc
                    seedEntities
                    (fun arcPath -> promise {
                        let vault = newVault arcPath
                        let handleFileEvent = vault._FileEventController watcherApi
                        handleFileEvent "addDir" (join [| arcPath; "assays/NewAssay" |])
                        handleFileEvent "addDir" (join [| arcPath; "assays/NewAssay/dataset" |])

                        Vitest.expect(vault.fileWatcherPendingEvents.Count).toBe 2
                        Vitest.expect(vault.fileWatcherPendingArcMergeEvents.Count).toBe 0

                        do! waitUntil "the batch to be applied" 10000 (fun () -> isWatcherQuiet vault)
                        Vitest.expect(vault.arc.IsNone).toBe true
                    })
        )

        Vitest.test (
            "only events that expand to merge events reach the ARC merge queue",
            fun () ->
                let queue eventName relativePath =
                    let pending = ResizeArray()
                    let mergeQueue = ResizeArray()

                    WatcherHelpers.queueFileWatcherEvent
                        (fun _ -> true)
                        (Some "/arc")
                        pending
                        mergeQueue
                        eventName
                        $"/arc/{relativePath}"

                    pending.Count, mergeQueue.Count

                Vitest.expect(queue "addDir" "assays/A1").toEqual ((1, 0))
                Vitest.expect(queue "unlinkDir" "assays/A1").toEqual ((1, 1))
                Vitest.expect(queue "unlinkDir" "assays/A1/dataset").toEqual ((1, 0))
                Vitest.expect(queue "add" "assays/A1/isa.assay.xlsx").toEqual ((1, 1))
        )

        Vitest.test (
            "platform junk names are never structural",
            fun () ->
                let junkPaths = [
                    ".DS_Store"
                    "._isa.investigation.xlsx"
                    ".directory"
                    "Thumbs.db"
                    "desktop.ini"
                    "assays/.DS_Store"
                    "assays/A1/Thumbs.db"
                    "assays/A1/._isa.assay.xlsx"
                    "studies/S1/desktop.ini"
                ]

                for junkPath in junkPaths do
                    Vitest.expect(ArcEntityPathRules.isStructuralWatcherPath junkPath None).toBe false

                    Vitest.expect(isStructuralFileWatcherPath "/arc" ("/arc/" + junkPath) None).toBe false

                let structuralPaths = [
                    ""
                    "isa.investigation.xlsx"
                    "LICENSE"
                    ".gitattributes"
                    "assays"
                    "assays/A1"
                    "assays/A1/isa.assay.xlsx"
                    "assays/A1/isa.datamap.xlsx"
                    "studies/S1/isa.study.xlsx"
                    "workflows/W1/isa.workflow.xlsx"
                    "runs/R1/isa.run.xlsx"
                ]

                for structuralPath in structuralPaths do
                    Vitest.expect(ArcEntityPathRules.isStructuralWatcherPath structuralPath None).toBe true

                let payloadPaths = [
                    "README.md"
                    "notes"
                    "assays/A1/dataset"
                    "assays/A1/dataset/data.txt"
                    "assays/A1/protocols/isa.assay.xlsx"
                    "studies/S1/resources/table.csv"
                    "extra/deep/file.txt"
                    "assays/.hidden"
                ]

                for payloadPath in payloadPaths do
                    Vitest.expect(ArcEntityPathRules.isStructuralWatcherPath payloadPath None).toBe false

                // A directory hint separates files from folders where the shape allows both.
                Vitest.expect(ArcEntityPathRules.isStructuralWatcherPath "assays/readme.txt" (Some false)).toBe false
                Vitest.expect(ArcEntityPathRules.isStructuralWatcherPath "assays/A1" (Some false)).toBe false
                Vitest.expect(ArcEntityPathRules.isStructuralWatcherPath "assays/A1" (Some true)).toBe true

                Vitest
                    .expect(
                        isStructuralFileWatcherPath
                            "/arc"
                            "/arc/assays/A1/.swate-import-0123456789abcdef0123456789abcdef"
                            None
                    )
                    .toBe
                    false
        )

        Vitest.test (
            "a candidate path relative to the watch root is judged like an absolute one",
            fun () ->
                let root = Main.FileTreeCreator.normalizeRootPath "/arc"

                Vitest.expect(isStructuralFileWatcherPath root "assays/A1/isa.assay.xlsx" None).toBe true
                Vitest.expect(isStructuralFileWatcherPath root "assays/A1/dataset/data.txt" None).toBe false
                Vitest.expect(isStructuralFileWatcherPath root "studies/S1/isa.study.xlsx" None).toBe true
                Vitest.expect(isStructuralFileWatcherPath root "../outside/isa.assay.xlsx" None).toBe false
        )

        Vitest.test (
            "a staging folder directly inside a zone folder does not look like an entity",
            fun () ->
                let staging = ".swate-import-0123456789abcdef0123456789abcdef"

                Vitest.expect(isStructuralFileWatcherPath "/arc" "/arc/assays/A1/isa.assay.xlsx" None).toBe true

                Vitest.expect(isStructuralFileWatcherPath "/arc" $"/arc/assays/{staging}/isa.assay.xlsx" None).toBe
                    false
        )

        Vitest.test (
            "an ARC folder named like a staging folder, or inside a staging folder or .git, is not hidden",
            fun () ->
                let staging = ".swate-import-0123456789abcdef0123456789abcdef"

                for arcFolder in
                    [
                        $"/tmp/{staging}"
                        $"/tmp/{staging}/arc"
                        "/tmp/.git/arc"
                    ] do
                    let root = Main.FileTreeCreator.normalizeRootPath arcFolder

                    Vitest.expect(isStructuralFileWatcherPath root (root + "/assays/A1/isa.assay.xlsx") None).toBe true
                    Vitest.expect(isStructuralFileWatcherPath root "studies/S1/isa.study.xlsx" None).toBe true
        )

        Vitest.test (
            "SWATE_WATCH_POLLING forces polling on any platform",
            fun () ->
                Vitest.expect(resolveFileWatcherPolling "linux" None None).toBe false
                Vitest.expect(resolveFileWatcherPolling "darwin" None None).toBe false
                Vitest.expect(resolveFileWatcherPolling "win32" None None).toBe true
                Vitest.expect(resolveFileWatcherPolling "linux" (Some "1") None).toBe true
                Vitest.expect(resolveFileWatcherPolling "darwin" (Some "1") None).toBe true
                Vitest.expect(resolveFileWatcherPolling "linux" (Some "0") None).toBe false
                Vitest.expect(resolveFileWatcherPolling "linux" (Some "1") (Some false)).toBe false

                let polling = createWatchOptions "/arc" true defaultFileWatcherTuning
                let native = createWatchOptions "/arc" false defaultFileWatcherTuning
                Vitest.expect(polling.usePolling).toEqual (Some true)
                Vitest.expect(native.usePolling).toEqual None
        )
)
