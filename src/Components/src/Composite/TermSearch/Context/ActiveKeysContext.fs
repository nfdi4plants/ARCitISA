[<AutoOpenAttribute>]
module Arcitisa.Components.Composite.TermSearch.Context.ActiveKeysContext

open Feliz
open Arcitisa.Components
open Arcitisa.Components.Composite.TermSearch

type TermSearchActiveKeysContext = {|
    disableDefault: bool
    activeKeys: string[]
|}

module TermSearchActiveKeysContext =

    let init (defaultActive: Set<string>) : TermSearchActiveKeysContext = {|
        disableDefault = false
        activeKeys = Set.toArray defaultActive
    |}

let TermSearchActiveKeysCtx =
    React.createContext<StateContext<TermSearchActiveKeysContext>> (
        {
            state = TermSearchActiveKeysContext.init (Set.empty)
            setState = fun keys -> printfn "Setting active keys not given: %A" keys
        }
    )

/// This context is used to track the active keys for the term search queries, and whether the default search is disabled. It is used by the TermSearchConfigProvider and TermSearchConfigSetter components.
///
/// It is stored in local storage, therefore all types related to this must be serializable to JSON by native JavaScript function ``JSON.stringify``. This means that it should not contain any functions or complex types that cannot be serialized to JSON.
[<Hook>]
let useTermSearchActiveKeysCtx () =
    React.useContext TermSearchActiveKeysCtx
