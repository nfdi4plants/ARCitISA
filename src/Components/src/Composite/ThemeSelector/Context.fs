module Arcitisa.Components.Composite.ThemeSelector.Context

open Feliz
open Arcitisa.Components
open Arcitisa.Components.Composite.ThemeSelector.Types

let ThemeCtx = React.createContext<StateContext<Theme>> ()

[<Hook>]
let useThemeCtx () = React.useContext ThemeCtx
