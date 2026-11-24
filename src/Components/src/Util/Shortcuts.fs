/// Keys in CodeMirror's notation and how the UI writes them, for example in a button title. Every view that names
/// a key goes through here, so a toolbar, a menu and a settings page write the same key the same way.
module Swate.Components.Util.Shortcuts

open Swate.Components.Util.UserOS

/// The key that saves the editor.
let saveKey = "Mod-s"

/// The undo key of the editor's history.
let undoKey = "Mod-z"

/// The redo key of the editor's history, which differs on a Mac.
let redoKey (isMac: bool) =
    if isMac then "Mod-Shift-z" else "Mod-y"

/// True where CodeMirror's "Mod" is the command key, as on macOS and iOS.
let isMac () =
    match getUserOS () with
    | MacOS
    | IOS -> true
    | _ -> false

/// How the UI writes "Mod": "⌘" on a Mac, "Ctrl" elsewhere.
let modLabel (isMac: bool) = if isMac then "⌘" else "Ctrl"

/// "Mod-b" becomes "Ctrl+B", or "⌘+B" on a Mac.
let label (isMac: bool) (key: string) : string =
    let parts = key.Split '-'

    parts
    |> Array.mapi (fun i part ->
        if i = parts.Length - 1 then part.ToUpperInvariant()
        elif part = "Mod" then modLabel isMac
        else part
    )
    |> String.concat "+"

/// "Save" with the key "Mod-s" becomes "Save (Ctrl+S)", or "Save (⌘+S)" on a Mac.
let title (isMac: bool) (name: string) (key: string) : string = name + " (" + label isMac key + ")"
