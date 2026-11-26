[<AutoOpenAttribute>]
module Arcitisa.Components.Composite.TermSearch.Context.AllKeysContext

open Feliz

let TermSearchAllKeysCtx = React.createContext<Set<string>> (Set.empty)

[<Hook>]
let useTermSearchAllKeysCtx () = React.useContext TermSearchAllKeysCtx
