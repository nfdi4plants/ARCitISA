module Swate.Components.Primitive.ComboBox.Types

open Fable.Core
open Feliz

type ComboBoxRef = {|
    focus: unit -> unit
    close: unit -> unit
    isOpen: unit -> bool
|}

/// React's event object, which remembers whether a handler called stopPropagation.
type private ReactEvent =
    abstract isPropagationStopped: unit -> bool

module ComboBoxKey =

    /// True for a key that the list of a ComboBox used: an Enter that picked the active item, Escape, and the arrow
    /// keys that move the active item. The ComboBox stops the propagation of Enter and Escape before it runs
    /// onChange or onKeyDown, and its list navigation does so for the arrows, so a host's onKeyDown can tell them
    /// apart from the keys that the list left alone.
    let usedByList (e: Browser.Types.KeyboardEvent) : bool =
        (unbox<ReactEvent> e).isPropagationStopped ()
