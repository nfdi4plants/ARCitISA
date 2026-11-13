module Main.ArcMenu

open Fable.Core
open Fable.Electron.Main
open Main

type Actions = {
    IsVSCodeAvailable: unit -> JS.Promise<bool>
    OpenInVSCode: string -> JS.Promise<unit>
    OpenTerminal: string -> JS.Promise<unit>
    OpenInFileManager: string -> JS.Promise<unit>
}

let private productionActions = {
    IsVSCodeAvailable = Main.ArcExternalTools.isVSCodeAvailable
    OpenInVSCode = Main.ArcExternalTools.openInVSCode
    OpenTerminal = Main.ArcExternalTools.openTerminal
    OpenInFileManager = Main.ArcExternalTools.openInFileManager
}

let private runAction window title action = promise {
    try
        let arcPath =
            window
            |> Option.bind (fun (window: BaseWindow) -> ARC_VAULTS.TryGetVault window.id)
            |> Option.bind _.path
            |> Option.defaultWith (fun () -> raise (Main.ArcVaultHelper.arcNotOpenError ()))

        do! action arcPath
    with error ->
        let! _ =
            dialog.showMessageBox (
                ?window = window,
                options =
                    Dialog.ShowMessageBox.Options(
                        error.Message,
                        title = title,
                        ``type`` = Enums.Dialog.ShowMessageBox.Options.Type.Error
                    )
            )

        ()
}

let createWith (actions: Actions) =
    let item id label action enabled =
        MenuItem.Options(
            id = id,
            label = label,
            enabled = enabled,
            click =
                Main.Bindings.Menu.adaptClick (fun (_, window, _) ->
                    runAction window label action
                    |> Promise.catch (fun error -> Browser.Dom.console.warn ("ARC menu action failed", error.Message))
                    |> Promise.start
                )
        )

    MenuItem.Options(
        id = "swate-arc",
        label = "ARC",
        submenu =
            U2.Case1 [|
                item
                    "swate-open-arc-in-vscode"
                    "Open ARC in Visual Studio Code"
                    (fun path -> promise {
                        let! available = actions.IsVSCodeAvailable()

                        if available then
                            do! actions.OpenInVSCode path
                        else
                            failwith "Visual Studio Code is not installed or its URL handler is unavailable."
                    })
                    false
                item "swate-open-terminal-in-arc" "Open Terminal in ARC" actions.OpenTerminal true
                item "swate-open-arc-in-file-manager" "Open ARC in File Manager" actions.OpenInFileManager true
            |]
    )

let create () = createWith productionActions

let refreshAvailabilityWith check (menu: Menu) = promise {
    let! available = check ()

    menu.getMenuItemById "swate-open-arc-in-vscode"
    |> Option.iter (fun item -> item.enabled <- available)
}

let refreshAvailability menu =
    refreshAvailabilityWith Main.ArcExternalTools.isVSCodeAvailable menu
