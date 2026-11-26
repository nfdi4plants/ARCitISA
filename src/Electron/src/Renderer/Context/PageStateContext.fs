module Renderer.Context.PageStateContext

open Feliz
open Renderer.Types
open Arcitisa.Components

let PageStateCtx =
    React.createContext<StateContext<PageState option>> ({ state = None; setState = ignore })

[<Hook>]
let usePageStateCtx () = React.useContext PageStateCtx

/// Applies a function to the page state the app holds when the update runs, so a change
/// computed from an older page state cannot undo a newer one.
let PageStateUpdateCtx =
    React.createContext<(PageState option -> PageState option) -> unit> (ignore)

[<Hook>]
let usePageStateUpdateCtx () = React.useContext PageStateUpdateCtx
