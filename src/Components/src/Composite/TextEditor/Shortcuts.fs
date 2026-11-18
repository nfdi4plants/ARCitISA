/// Keys in CodeMirror's notation and how the UI writes them, for example in a button title.
module Swate.Components.Composite.TextEditor.Shortcuts

open Swate.Components.Util.UserOS

/// The key that saves the editor.
let saveKey = "Mod-s"

/// True where CodeMirror's "Mod" is the command key, as on macOS and iOS.
let isMac () =
    match getUserOS () with
    | MacOS
    | IOS -> true
    | _ -> false

/// "Mod-b" becomes "Ctrl+B", or "⌘+B" on a Mac.
let label (isMac: bool) (key: string) : string =
    let parts = key.Split '-'

    parts
    |> Array.mapi (fun i part ->
        if i = parts.Length - 1 then part.ToUpperInvariant()
        elif part = "Mod" then (if isMac then "⌘" else "Ctrl")
        else part
    )
    |> String.concat "+"

/// "Save" with the key "Mod-s" becomes "Save (Ctrl+S)", or "Save (⌘+S)" on a Mac.
let title (isMac: bool) (name: string) (key: string) : string = name + " (" + label isMac key + ")"
