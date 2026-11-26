module Arcitisa.Components.Composite.Template.TemplateCacheContext

open Feliz
open ARCtrl
open Arcitisa.Components.Composite.Template.Types

type TemplateCacheContext = {
    Templates: Template[]
    IsLoading: bool
    RefreshTemplates: unit -> unit
} with

    static member Empty = {
        Templates = [||]
        IsLoading = false
        RefreshTemplates = ignore
    }

let TemplateCacheCtx =
    React.createContext<TemplateCacheContext> (TemplateCacheContext.Empty)

[<Hook>]
let useTemplateCacheCtx () = React.useContext TemplateCacheCtx
