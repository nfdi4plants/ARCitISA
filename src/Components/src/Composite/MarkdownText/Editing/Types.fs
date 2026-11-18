module Swate.Components.Composite.MarkdownText.Editing.Types

open Feliz
open Swate.Components.Composite.TextEditor.Types

/// The text the toolbar edits. A TextEditor is one. A textarea can be another later.
type MarkdownSurface =
    abstract getState: unit -> TextEditState
    abstract apply: edit: TextEdit -> bool
    abstract focus: unit -> unit

module MarkdownSurface =

    /// Reads the handle on every call, so the surface keeps working when the editor mounts later or remounts.
    let ofTextEditor (handleRef: IRefValue<TextEditorHandle option>) : MarkdownSurface =
        { new MarkdownSurface with
            member _.getState() =
                match handleRef.current with
                | Some handle -> handle.getEditState ()
                | None -> {|
                    text = ""
                    selection = {| anchor = 0; head = 0 |}
                  |}

            member _.apply edit =
                match handleRef.current with
                | Some handle -> handle.applyEdit edit
                | None -> false

            member _.focus() =
                handleRef.current |> Option.iter (fun handle -> handle.focus ())
        }

type MarkdownCommand = {
    Id: string
    Label: string
    /// Icon class without "swt:iconify", for example "swt:fluent--text-bold-24-regular".
    Icon: string
    /// CodeMirror key notation, for example "Mod-b".
    Key: string option
    Run: TextEditState -> Result<TextEdit, string>
}
