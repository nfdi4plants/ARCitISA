/// The app-wide text diff worker pool. Creating the supervisor is asynchronous (it prepares
/// its temp folder), so the first caller starts the setup and every later caller shares the
/// same promise. A failed setup is logged and retried by the next caller. The supervisor
/// asks the library's Git selection each time it starts Git.
module Main.VersionControl.TextDiffWorkers

open Fable.Core
open Fable.Electron.Main
open Node.Api

module TextDiffPool = VersionControlService.Git.TextDiff.TextDiffPool
module TextDiffSupervisor = VersionControlService.Git.TextDiff.TextDiffSupervisor
module TextDiffTransport = VersionControlService.Git.TextDiff.TextDiffTransport

[<Literal>]
let workerFileName = "text-diff-worker.cjs"

/// Development builds find the worker bundle next to main.fs.js in .vite/build. The
/// packaged app loads the copy that packaging unpacks next to app.asar.
let workerPath (isPackaged: bool) (resourcesPath: string) (moduleDirectory: string) : string =
    if isPackaged then
        path.join (resourcesPath, "app.asar.unpacked", ".vite", "build", workerFileName)
    else
        path.join (moduleDirectory, workerFileName)

let private currentWorkerPath () =
    workerPath app.isPackaged Main.Helper.Assets.processResourcesPath __dirname

/// The folder that holds the text-diff folder of spools and scratch files. A spool can grow to
/// the size of the diffed file. Windows and macOS use the Electron temp folder, which is per user
/// there and stays out of roaming or redirected profiles. Linux uses the XDG cache folder of the
/// user (XDG_CACHE_HOME when it is an absolute path, else ~/.cache) below the app name. The
/// config folder is often backed up or synced, and /tmp is shared between users and often lives
/// in memory.
let tempRootFor
    (platform: string)
    (xdgCacheHome: string option)
    (homeDirectory: string)
    (electronTemp: unit -> string)
    (appName: string)
    : string =
    match platform with
    | "win32"
    | "darwin" -> electronTemp ()
    | _ ->
        let cacheRoot =
            match xdgCacheHome with
            | Some folder when path.isAbsolute folder -> folder
            | _ -> path.join (homeDirectory, ".cache")

        path.join (cacheRoot, appName)

let private logFailure (message: string) (error: exn) =
    Browser.Dom.console.error (message, error.Message)

let mutable private pending: JS.Promise<TextDiffPool.TextDiffPool option> option =
    None

let mutable private disposal: JS.Promise<unit> option = None
let mutable private prewarmed = false

let private createPool () : JS.Promise<TextDiffPool.TextDiffPool option> = promise {
    try
        let! supervisor =
            TextDiffSupervisor.create {
                TempRoot =
                    tempRootFor
                        (Main.Bindings.Node.processPlatform ())
                        (Main.Bindings.Node.environmentVariable "XDG_CACHE_HOME")
                        (Main.Bindings.Node.homeDirectory ())
                        (fun () -> app.getPath Enums.App.GetPath.Name.Temp)
                        (app.getName ())
                OnEvent = None
            }

        return
            Some(
                TextDiffPool.create (
                    TextDiffPool.TextDiffPoolOptions.create
                        (fun _ -> TextDiffTransport.WorkerThreadTransport.create (currentWorkerPath ()))
                        supervisor
                )
            )
    with error ->
        logFailure "The text diff workers could not be set up." error
        return None
}

/// The shared pool, or None when the setup failed or the pool was already disposed.
/// The returned promise never rejects.
let pool () : JS.Promise<TextDiffPool.TextDiffPool option> =
    match disposal, pending with
    | Some _, _ -> Promise.lift None
    | None, Some created -> created
    | None, None ->
        let created =
            createPool ()
            |> Promise.map (fun result ->
                if result.IsNone then
                    pending <- None

                result
            )

        pending <- Some created
        created

/// Starts the workers ahead of the first diff. Later calls do nothing once a pool was
/// prewarmed. Failures are logged and never thrown.
let prewarm () : unit =
    if not prewarmed then
        prewarmed <- true

        try
            pool ()
            |> Promise.map (fun created ->
                match created with
                | Some created -> created.Prewarm()
                | None -> prewarmed <- false
            )
            |> Promise.catch (fun error ->
                prewarmed <- false
                logFailure "The text diff workers could not be prewarmed." error
            )
            |> Promise.start
        with error ->
            prewarmed <- false
            logFailure "The text diff workers could not be prewarmed." error

/// Disposes the pool and its supervisor. Repeated calls return the first disposal, and
/// no new pool is created afterwards.
let dispose () : JS.Promise<unit> =
    match disposal with
    | Some running -> running
    | None ->
        let created = pending

        let running = promise {
            match created with
            | Some created ->
                try
                    match! created with
                    | Some created -> do! created.Dispose()
                    | None -> ()
                with error ->
                    logFailure "The text diff workers could not be disposed." error
            | None -> ()
        }

        disposal <- Some running
        running
