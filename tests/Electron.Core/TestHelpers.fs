module ElectronCore.TestHelpers

open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron
open Fable.Electron.Main
open Main
open Main.ArcVaultState
open Main.Bindings.Path
open Main.VersionControl
open Arcitisa.Components.Composite.Authentication.Types
open Arcitisa.Electron.Shared.IPCTypes
open Arcitisa.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open ARCtrl

let electronMock: obj = import "__electronMock" "electron"

[<Emit("require('node:child_process').execFileSync($0, $1, { cwd: $2, stdio: 'pipe' }).toString()")>]
let execFile (_file: string) (_args: string[]) (_cwd: string) : string = jsNative

[<Import("symlink", "fs/promises")>]
let private symlink (_target: string) (_linkPath: string) (_linkType: string) : JS.Promise<unit> = jsNative

/// Links `linkPath` to the folder `target`. Windows creates a junction, which needs no extra
/// privileges. Other platforms ignore the type and create a folder symlink.
let createFolderLinkAsync (target: string) (linkPath: string) = symlink target linkPath "junction"

[<Emit("process.platform")>]
let nodePlatform () : string = jsNative

type private ISpawnSyncResult =
    abstract error: exn option
    abstract status: int

[<Import("spawnSync", "node:child_process")>]
let private spawnSync (command: string, args: string[]) : ISpawnSyncResult = jsNative

/// True when the mkfifo program runs, so a test can create a named pipe.
let hasMkfifo () =
    (spawnSync ("mkfifo", [| "--help" |])).error.IsNone

/// Creates a named pipe and returns the exit status of mkfifo.
let makeFifo (path: string) =
    (spawnSync ("mkfifo", [| path |])).status

/// chmod does not stop deletes or writes for root.
[<Emit("typeof process.getuid === 'function' && process.getuid() === 0")>]
let runsAsRoot () : bool = jsNative

[<Import("chmod", "fs/promises")>]
let chmodAsync (path: string, mode: int) : JS.Promise<unit> = jsNative

let writeText (filePath: string) (content: string) =
    Main.Bindings.Filesystem.writeFileSync filePath content Main.Bindings.Filesystem.TextEncoding.Utf8

let noAccounts: DataHubStrategies.DataHubAccountSource = {
    GetState = fun () -> AuthStateDto.Empty
    TryGetTokenForAccount = fun _ -> None
    TryGetTokenForHost = fun _ -> None
}

/// A function list in which every entry is missing. A fake provider replaces the entries it has.
let missingFunctions: ProviderFunctions =
    let missing () =
        Missing "The fake provider does not have this function."

    {
        VerifyLocation = missing ()
        Initialize = missing ()
        Clone = missing ()
        Bind = missing ()
        Open = missing ()
        CheckDependencies = missing ()
        InstallDependency = missing ()
        GetStatus = missing ()
        ListRefs = missing ()
        CreateRef = missing ()
        PreflightSwitchRef = missing ()
        SwitchRef = missing ()
        CreateRevision = missing ()
        RestorePaths = missing ()
        GetDiffSummary = missing ()
        Synchronization = {
            Refresh = missing ()
            PreviewUpdate = missing ()
            Update = missing ()
            Publish = missing ()
            Synchronize = missing ()
        }
        ConflictResolution = {
            GetActiveSession = missing ()
            Resolve = missing ()
            Finalize = missing ()
            Cancel = missing ()
        }
        TextDiff = {
            Open = missing ()
            ReadPage = missing ()
            ReplayPage = missing ()
            Expand = missing ()
            ReadLine = missing ()
            GetSourceInfo = missing ()
            Close = missing ()
        }
        History = {
            ListRevisions = missing ()
            GetRevisionChanges = missing ()
        }
        RepositoryBrowser = { GetRepositoryWebUrl = missing () }
        ObjectMaterialization = {
            ListObjects = missing ()
            Materialize = missing ()
            Dematerialize = missing ()
        }
        StoragePolicy = {
            SetPathPolicy = missing ()
            GetSettings = missing ()
            SetSettings = missing ()
        }
        Maintenance = {
            Prune = missing ()
            Deduplicate = missing ()
            CanDeduplicate = missing ()
        }
    }

/// The repository a fake provider opens.
type FakeRepository(descriptor: RepositoryDescriptor) =
    inherit Repository(descriptor)

/// A fake provider of the app: the probe, the close and the functions it has.
let fakeProvider
    (id: ProviderId)
    (probe: string -> Async<ProbeResult>)
    (close: Repository -> Async<unit>)
    (functions: ProviderFunctions)
    : ProviderComposition.AppProvider =
    ProviderComposition.createAppProvider id (ListedProvider(probe, close, functions))

let createRuntime (settingsRoot: string) lakeFsCredentials : VersionControlRuntime.VersionControlRuntime = {
    Providers = [|
        ProviderComposition.createGitProvider noAccounts WorkspaceSessionHost.windowOwnerOf
        ProviderComposition.createLakeFsProvider {
            ProviderComposition.lakeFsOptions settingsRoot CaseInsensitive with
                Credentials = lakeFsCredentials
        }
    |]
    PathCaseSensitivity = CaseInsensitive
    SessionDefaults = UserDefaults.builtInSessionDefaults
}

let expectValue (operation: string) (result: OperationResult<'T>) =
    match result with
    | Succeeded outcome
    | PartiallySucceeded(outcome, _) -> outcome.Value
    | Failed failure -> failwith $"{operation} failed ({failure.Code}): {failure.Message}"

let expectDtoValue (operation: string) (result: Result<OperationResultDto<'T>, exn>) =
    match result with
    | Ok(OperationResultDto.Succeeded outcome)
    | Ok(OperationResultDto.PartiallySucceeded(outcome, _)) -> outcome
    | Ok(OperationResultDto.Failed failure) -> failwith $"{operation} failed ({failure.Code}): {failure.Message}"
    | Error error -> failwith $"{operation} threw: {error.Message}"

let expectDtoFailure (operation: string) (result: Result<OperationResultDto<'T>, exn>) =
    match result with
    | Ok(OperationResultDto.Failed failure) -> failure
    | Ok _ -> failwith $"{operation} unexpectedly succeeded."
    | Error error -> failwith $"{operation} threw: {error.Message}"

[<Emit("(() => { let resolve; const promise = new Promise((r) => { resolve = r; }); return [promise, resolve]; })()")>]
let deferred () : JS.Promise<unit> * (unit -> unit) = jsNative

let detached name = OperationContext.detached name

let ipcEvent (windowId: int) : IpcMainInvokeEvent =
    createObj [ "sender" ==> createObj [ "id" ==> windowId ] ]
    |> unbox<IpcMainInvokeEvent>

let request (operationId: string) : OperationRequestDto = { OperationId = operationId }

let private fsPromisesDynamic: obj = importAll "fs/promises"
let private osDynamic: obj = importAll "os"

let expectLoadedArc (result: Result<ARC, string[]>) =
    match result with
    | Ok arc -> arc
    | Error errors -> failwith (errors |> String.concat "\n")

let createTempDirectoryAsync (tempPrefix: string) : JS.Promise<string> =
    let prefix = join [| osDynamic?tmpdir () |> unbox<string>; tempPrefix |]

    fsPromisesDynamic?mkdtemp (prefix) |> unbox<JS.Promise<string>>

/// A process killed by a canceled operation can hold the directory for a moment, so
/// the removal retries before giving up.
let removeDirectoryAsync (path: string) : JS.Promise<unit> = promise {
    let! _ =
        fsPromisesDynamic?rm (
            path,
            createObj [
                "recursive" ==> true
                "force" ==> true
                "maxRetries" ==> 10
                "retryDelay" ==> 200
            ]
        )
        |> unbox<JS.Promise<obj>>

    return ()
}

[<Import("tmpdir", "os")>]
let private tmpdir () : string = jsNative

[<Import("mkdtempSync", "fs")>]
let private mkdtempSync (_prefix: string) : string = jsNative

[<Import("symlinkSync", "fs")>]
let private symlinkSync (_target: string) (_linkPath: string) (_linkType: string) : unit = jsNative

[<Import("rmSync", "fs")>]
let private rmSync (_path: string) (_options: Main.Bindings.Filesystem.RmOptions) : unit = jsNative

let canCreateFileLinks () =
    let probeDirectory = mkdtempSync (join [| tmpdir (); "arcitisa-link-probe-" |])

    try
        try
            let targetPath = join [| probeDirectory; "target.txt" |]
            writeText targetPath ""
            symlinkSync targetPath (join [| probeDirectory; "link.txt" |]) "file"
            true
        with _ ->
            false
    finally
        rmSync probeDirectory (Main.Bindings.Filesystem.RmOptions(recursive = true, force = true))

let createFileLinkAsync (target: string) (linkPath: string) = symlink target linkPath "file"

let pathExistsAsync (path: string) : JS.Promise<bool> = promise {
    try
        let! _ = fsPromisesDynamic?access (path) |> unbox<JS.Promise<obj>>
        return true
    with _ ->
        return false
}

/// Adds a study, assay, workflow and run that each carry a DataMap.
let addDataMapToAllEntityTypes (arc: ARC) =
    let study = ArcStudy("Study With Datamap")
    study.Datamap <- Some(Datamap.init ())
    arc.AddStudy(study)

    let assay = ArcAssay("Assay With DataMap")
    assay.Datamap <- Some(Datamap.init ())
    arc.AddAssay(assay)

    let workflow = ArcWorkflow("Workflow With DataMap")
    workflow.Datamap <- Some(Datamap.init ())
    arc.AddWorkflow(workflow)

    let run = ArcRun("Run With DataMap")
    run.Datamap <- Some(Datamap.init ())
    arc.AddRun(run)

let loadArcAsync (arcPath: string) : JS.Promise<ARC> = promise {
    let! loaded = ARC.tryLoadAsync arcPath
    return expectLoadedArc loaded
}

let withTempArcWith
    (tempPrefix: string)
    (arcName: string)
    (seedArc: ARC -> unit)
    (testBody: string -> JS.Promise<unit>)
    : JS.Promise<unit> =
    promise {
        let! rootPath = createTempDirectoryAsync tempPrefix
        let arcPath = join [| rootPath; "arc" |]

        try
            let arc = ARC(arcName)
            seedArc arc
            do! arc.WriteAsync arcPath
            do! testBody arcPath
            do! removeDirectoryAsync rootPath
        with error ->
            do! removeDirectoryAsync rootPath
            return raise error
    }

let testWindow () =
    let noopSend: obj = emitJsExpr () "((..._args) => {})"

    createObj [
        "id" ==> 0
        "title" ==> ""
        "isDestroyed" ==> (fun () -> false)
        "webContents"
        ==> createObj [ "send" ==> noopSend; "isDestroyed" ==> (fun () -> false) ]
    ]
    |> unbox<BrowserWindow>

let registerVault (windowId: int) (arcPath: string) =
    let window = testWindow ()
    window?id <- windowId
    let vault = ArcVault(window)
    vault.path <- Some arcPath
    ARC_VAULTS.Vaults.[windowId] <- vault

    electronMock?setBrowserWindowFromWebContents (fun (webContents: obj) ->
        if unbox<int> webContents?id = windowId then
            box window
        else
            null
    )
    |> ignore

    vault

/// Loads every directory below the root, which gives the deep tree that the lazy scan leaves out.
let loadWholeTreeAsync (root: string) (caseInsensitive: bool) : JS.Promise<IndexedFileTree> = promise {
    let! tree = Main.FileTreeCreator.scanStructuralFileTreeAsync root caseInsensitive

    let unloadedDirectories () =
        tree.Entries
        |> Seq.filter (fun entry -> entry.isDirectory && not (tree.IsLoaded entry.path))
        |> Seq.toArray

    let mutable pending = unloadedDirectories ()

    while pending.Length > 0 do
        for directory in pending do
            let! _ = Main.FileTreeCreator.reconcileFileTreeDirectoryAsync root tree directory.path
            ()

        pending <- unloadedDirectories ()

    return tree
}

/// Sets the known disk versions of a vault to the current files, as a load does. A test makes its external change afterwards.
let seedKnownDisk (vault: ArcVault) : JS.Promise<unit> = promise {
    let! snapshot = vault.DiskScan vault.isCaseInsensitive vault.path.Value
    vault.KnownDisk <- Some snapshot
}

[<Import("utimes", "fs/promises")>]
let private utimes (_path: string) (_atime: float) (_mtime: float) : JS.Promise<unit> = jsNative

/// Moves the modified time of a file forward without changing its content.
let touchFileAsync (path: string) : JS.Promise<unit> = promise {
    let later: float = emitJsExpr () "Date.now() / 1000 + 5"
    do! utimes path later later
}

/// Polls until the condition holds, up to five seconds.
let waitForCondition (description: string) (condition: unit -> bool) : JS.Promise<unit> = promise {
    let mutable attempts = 0

    while attempts < 250 && not (condition ()) do
        do! Promise.sleep 20
        attempts <- attempts + 1

    if not (condition ()) then
        failwithf "Timed out waiting for: %s." description
}

[<Import("writeFile", "fs/promises")>]
let writeBufferAsync (_path: string) (_data: obj) : JS.Promise<unit> = jsNative

/// Runs the operation, then the cleanup, and reports the operation error first.
let withAsyncCleanup cleanup operation = promise {
    let! operationResult = operation () |> Promise.result
    let! cleanupResult = cleanup () |> Promise.result

    match operationResult, cleanupResult with
    | Ok value, Ok() -> return value
    | Error operationError, _ -> return raise operationError
    | Ok _, Error cleanupError -> return raise cleanupError
}
