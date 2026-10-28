module ElectronCore.TestHelpers

open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron
open Fable.Electron.Main
open Main
open Main.ArcVaultState
open Main.Bindings.Path
open Main.VersionControl
open Swate.Components.Composite.Authentication.Types
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes
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

let writeText (filePath: string) (content: string) =
    Main.Bindings.Filesystem.writeFileSync filePath content Main.Bindings.Filesystem.TextEncoding.Utf8

let noAccounts: DataHubStrategies.DataHubAccountSource = {
    GetState = fun () -> AuthStateDto.Empty
    TryGetTokenForAccount = fun _ -> None
    TryGetTokenForHost = fun _ -> None
}

let memoryBindings () =
    let mutable content: string option = None

    WorkspaceBindingStore.create
        CaseInsensitive
        (fun () -> content)
        (fun next ->
            content <- Some next
            Ok()
        )

let createRuntime
    (settingsRoot: string)
    lakeFsCredentials
    (bindings: WorkspaceBindingStore.IWorkspaceBindingStore)
    : VersionControlRuntime.VersionControlRuntime =
    {
        Catalog =
            ProviderComposition.createCatalog [
                ProviderComposition.createGitFactory noAccounts WorkspaceSessionHost.windowOwnerOf
                ProviderComposition.createLakeFsFactory
                    (ProviderComposition.lakeFsOptions settingsRoot CaseInsensitive)
                    lakeFsCredentials
            ]
        Bindings = bindings
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
    let probeDirectory = mkdtempSync (join [| tmpdir (); "swate-link-probe-" |])

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
