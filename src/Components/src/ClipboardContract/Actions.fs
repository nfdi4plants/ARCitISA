module Arcitisa.Components.ClipboardContract.Actions

open Arcitisa.Components.ClipboardContract.Types

let cut (content: ClipboardContent) (clearSource: unit -> unit) = promise {
    do! Arcitisa.Components.ClipboardCodec.write content.PlainText content.Cells
    clearSource ()
}
