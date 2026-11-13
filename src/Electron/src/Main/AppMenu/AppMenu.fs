module Main.AppMenu

open Fable.Core
open Fable.Electron
open Fable.Electron.Main

let private refreshAvailability =
    System.Action<Browser.Types.Event, BrowserWindow>(fun _ _ ->
        Menu.getApplicationMenu ()
        |> Option.iter (fun menu -> Main.ArcMenu.refreshAvailability menu |> Promise.start)
    )

let install () =
    let existingItems =
        match Menu.getApplicationMenu () with
        | Some menu ->
            menu.items
            |> Array.filter (fun item ->
                item.role <> Enums.MenuItem.Role.Help
                && item.id <> "swate-help"
                && item.id <> "swate-arc"
            )
            |> Array.map U2.Case2
        | None ->
            [|
                if Main.Bindings.Node.processPlatform () = "darwin" then
                    Enums.MenuItem.Options.Role.AppMenu
                Enums.MenuItem.Options.Role.FileMenu
                Enums.MenuItem.Options.Role.EditMenu
                Enums.MenuItem.Options.Role.ViewMenu
                Enums.MenuItem.Options.Role.WindowMenu
            |]
            |> Array.map (fun role -> U2.Case1(MenuItem.Options(role = role)))

    let menu =
        Array.append existingItems [|
            U2.Case1(Main.ArcMenu.create ())
            U2.Case1(Main.HelpMenu.create ())
        |]
        |> Menu.buildFromTemplate

    Menu.setApplicationMenu (Some menu)
    Main.ArcMenu.refreshAvailability menu |> Promise.start

    Main.Bindings.Menu.appEvents.offWindowFocus refreshAvailability
    Main.Bindings.Menu.appEvents.onWindowFocus refreshAvailability
