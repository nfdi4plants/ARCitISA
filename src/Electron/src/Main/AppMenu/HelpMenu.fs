module Main.HelpMenu

open Fable.Core
open Fable.Electron.Main
open Arcitisa.Electron.Shared.IPCTypes
open Arcitisa.Electron.Shared.IPCTypes.MainToRendererIpc

let private showMessage window title message detail messageType = promise {
    let! _ =
        dialog.showMessageBox (
            ?window = window,
            options = Dialog.ShowMessageBox.Options(message, title = title, detail = detail, ``type`` = messageType)
        )

    return ()
}

let showAbout window =
    showMessage
        window
        "About ARCitISA"
        "ARCitISA"
        $"Version {Main.Settings.AppVersion.getState().CurrentVersion}"
        Enums.Dialog.ShowMessageBox.Options.Type.Info

let checkForUpdates window (check: unit -> JS.Promise<Result<AppVersionState, exn>>) = promise {
    let! result = check ()

    match result with
    | Ok state ->
        let message =
            match state.UpdateVersion with
            | Some version -> $"ARCitISA {version} is available."
            | None -> "ARCitISA is up to date."

        do!
            showMessage
                window
                "Check for updates"
                message
                $"Current version: {state.CurrentVersion}"
                Enums.Dialog.ShowMessageBox.Options.Type.Info
    | Error error ->
        do!
            showMessage
                window
                "Check for updates"
                "Could not check for updates."
                error.Message
                Enums.Dialog.ShowMessageBox.Options.Type.Error
}

let showReleaseNotes (window: BaseWindow option) =
    window
    |> Option.bind (fun window -> BrowserWindow.fromId window.id)
    |> Option.iter (fun window ->
        Main.WindowSend.send<IReleaseNotesRendererApi>
            window
            (fun api -> api.showReleaseNotes (Main.Settings.AppVersion.getState().CurrentVersion))
    )

let create () =
    MenuItem.Options(
        id = "arcitisa-help",
        label = "Help",
        role = Enums.MenuItem.Options.Role.Help,
        submenu =
            U2.Case1 [|
                MenuItem.Options(
                    id = "arcitisa-check-for-updates",
                    label = "Check for updates ...",
                    click =
                        Main.Bindings.Menu.adaptClick (fun (item, window, _) ->
                            item.enabled <- false

                            promise {
                                try
                                    do! checkForUpdates window Main.Settings.AppVersion.checkForUpdateWithResult
                                finally
                                    item.enabled <- true
                            }
                            |> Promise.catch (fun error ->
                                Browser.Dom.console.warn ("Update dialog failed", error.Message)
                            )
                            |> Promise.start
                        )
                )
                MenuItem.Options(
                    id = "arcitisa-about",
                    label = "About",
                    click = Main.Bindings.Menu.adaptClick (fun (_, window, _) -> showAbout window |> Promise.start)
                )
                MenuItem.Options(
                    id = "arcitisa-release-notes",
                    label = "Show Release Notes",
                    click = Main.Bindings.Menu.adaptClick (fun (_, window, _) -> showReleaseNotes window)
                )
            |]
    )
