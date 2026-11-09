module Main.Main

open Fable.Core
open Fable.Electron
open Fable.Electron.Remoting.Main
open Main

let private registerRequiredWindow (failureContext: string) =
    ARC_VAULTS.RegisterVault(
        onFailureBeforeCleanup =
            fun error ->
                eprintfn "%s: %s" failureContext error.Message

                dialog.showErrorBox ("ARCitISA could not start", $"{failureContext}\n\n{error.Message}")
    )
    |> Promise.map ignore
    |> Promise.catch (fun _ -> app.quit ())
    |> Promise.start

[<Literal>]
let private textDiffDisposeTimeoutMilliseconds = 5000

// Squirrel launches ARCitISA for install/update/uninstall hooks; its startup helper owns
// shortcut maintenance and exits when finished. app.quit() does not stop execution of the code below:
// registering whenReady below can still load a window during shutdown and throw.
// To avoid this issue, we gate the logic below fully behind the SquirrelStartup check.
if SquirrelStartup.started then
    // The helper quits once Update.exe exits. Quitting here too keeps a hook run from
    // staying open without a window if that exit is never reported.
    app.quit ()
else
    // The default user data folder follows the product name. Pin it before anything reads it.
    Main.SettingsStore.pinLegacyUserDataPath ()

    app
        .whenReady()
        .``then`` (fun () ->
            Main.Settings.AppVersion.initialize ()
            Main.AppMenu.install ()
            Main.Settings.AppVersion.checkForUpdate () |> Promise.start

            // Restore persisted auth before any IPC handlers fire
            Main.Auth.AuthService.tryRestoreFromStorage ()

            // The library keeps one Git selection for all providers. It has to exist before the first
            // provider starts Git.
            Main.VersionControl.GitExecutableSetting.install (
                Main.UserDefaults.production.Effective().GitExecutablePath
            )

            // The provider catalog needs the settings root, which exists only once the app is
            // ready. A failure here must not take the IPC registrations below with it.
            try
                let runtime =
                    Main.VersionControl.VersionControlRuntime.createProduction
                        Main.VersionControl.WorkspaceSessionHost.windowOwnerOf

                let host = Main.VersionControl.WorkspaceSessionHost.WorkspaceSessionHost(runtime)
                Main.VersionControl.WorkspaceSessionHost.initialize host
                Main.VersionControl.DependencyState.initialize runtime.Catalog
            with error ->
                Browser.Dom.console.error ("Version control host initialization failed", error.Message)

            registerRequiredWindow "The application window could not be loaded."

            Remoting.createIpc () |> Remoting.fromIpcMainEvent IPC.IVersionControlApi.api
            Remoting.createIpc () |> Remoting.fromValue IPC.IGitLabApi.api
            Remoting.createIpc () |> Remoting.fromIpcMainEvent IPC.ArcVaultsApi.api
            Remoting.createIpc () |> Remoting.fromValue Main.IPC.AuthApi.api
            Remoting.createIpc () |> Remoting.fromValue Main.IPC.TemplateApi.api
            Remoting.createIpc () |> Remoting.fromValue Main.IPC.ValidationPackageApi.api
            Remoting.createIpc () |> Remoting.fromValue Main.IPC.UiSettingsApi.api
            Remoting.createIpc () |> Remoting.fromValue Main.IPC.UserDefaultsApi.api
            Remoting.createIpc () |> Remoting.fromValue Main.IPC.IAppVersionApi.api

            app.onActivate (fun _ ->
                if BrowserWindow.getAllWindows().Length = 0 then
                    registerRequiredWindow "The application window could not be reopened."
            )
        )
    |> ignore

    app.onWindowAllClosed (fun () ->
        Browser.Dom.console.log ("App quit")
        app.quit ()
    )

    app.onBeforeQuit (fun _ -> Browser.Dom.console.log ("Quitting"))

    let mutable textDiffWorkersReleased = false

    app.onWillQuit (fun event ->
        // The before-quit event runs before the windows' close handlers, which may still wait for running operations.
        match Main.VersionControl.WorkspaceSessionHost.tryCurrent () with
        | Some host ->
            host.CloseAll()
            |> Async.StartAsPromise
            |> Promise.catch (fun _ -> ())
            |> Promise.start
        | None -> ()

        // The diff workers own Git child processes and temp folders. The first will-quit holds
        // the quit until the pool is disposed or 5 s have passed, then quits again.
        if not textDiffWorkersReleased then
            textDiffWorkersReleased <- true
            event.preventDefault ()

            let timeout =
                Promise.create (fun resolve _ -> JS.setTimeout resolve textDiffDisposeTimeoutMilliseconds |> ignore)

            Main.Bindings.PromiseRace.race [|
                Main.VersionControl.TextDiffWorkers.dispose ()
                timeout
            |]
            |> Promise.catch ignore
            |> Promise.iter (fun () -> app.quit ())
    )
