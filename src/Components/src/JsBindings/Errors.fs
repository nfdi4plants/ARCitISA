namespace Arcitisa.Components.JsBindings

open Fable.Core

module Errors =

    /// The message of a thrown or rejected value. JavaScript can throw anything, so a value without a message
    /// shows as its text.
    [<Emit("($0 != null && typeof $0.message === 'string' && $0.message !== '' ? $0.message : String($0))")>]
    let messageOf (value: obj) : string = jsNative
