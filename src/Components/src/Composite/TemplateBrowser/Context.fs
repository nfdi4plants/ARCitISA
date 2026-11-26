module Arcitisa.Components.Composite.Template.Context

open ARCtrl
open Feliz
open Arcitisa.Components

let FilteredTemplateCtx =
    React.createContext<StateContext<Template[]>> ({ state = [||]; setState = fun _ -> () })

[<Hook>]
let useFilteredTemplateCtx () = React.useContext FilteredTemplateCtx
