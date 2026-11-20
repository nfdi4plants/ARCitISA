module Main.Bindings.Menu

open Fable.Core
open Fable.Electron.Main
open Fable.Electron.Types

type MenuClickHandler = MenuItem * BaseWindow option * KeyboardEvent -> unit

/// Electron passes three arguments; the generated binding expects an F# tuple.
[<Emit("(item, window, event) => $0([item, window ?? undefined, event])")>]
let adaptClick (_callback: MenuClickHandler) : MenuClickHandler = jsNative

/// Delegates preserve listener identity when adding and removing native event handlers.
type AppEvents =
    [<Emit("$0.on('browser-window-focus', $1)")>]
    abstract onWindowFocus: System.Action<Browser.Types.Event, BrowserWindow> -> unit

    [<Emit("$0.off('browser-window-focus', $1)")>]
    abstract offWindowFocus: System.Action<Browser.Types.Event, BrowserWindow> -> unit

[<Import("app", "electron")>]
let appEvents: AppEvents = jsNative
