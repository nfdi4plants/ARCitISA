module Renderer.Context.AppStateContext

open Arcitisa.Electron.Shared

open Feliz

let AppStateCtx = React.createContext<ArcRootPath> None

[<Hook>]
let useAppStateCtx () = React.useContext AppStateCtx
