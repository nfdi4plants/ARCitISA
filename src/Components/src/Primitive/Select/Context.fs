module Arcitisa.Components.Primitive.Select.Context

open Feliz

type SelectContextValue = {
    activeIndex: int option
    selectedIndices: Set<int>
    optionCount: int
    getItemProps: obj -> obj
    handleSelect: int option -> unit
} with

    static member init() : SelectContextValue = {
        activeIndex = None
        selectedIndices = Set.empty
        optionCount = 0
        getItemProps = fun _ -> null
        handleSelect = fun _ -> ()
    }

let SelectCtx = React.createContext<SelectContextValue> (SelectContextValue.init ())

[<Hook>]
let useSelectCtx () = React.useContext SelectCtx
