module Arcitisa.Components.Primitive.BaseModal.Context

open Feliz

type BaseModalContext = {
    isOpen: bool
    setIsOpen: bool -> unit
    headerId: string
    descId: string
}

let BaseModalCtx = React.createContext<BaseModalContext option> (None)

[<Hook>]
let useBaseModalCtx () = React.useContext BaseModalCtx
