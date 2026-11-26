module Arcitisa.Components.Page.ValidationPackageBrowser.Context

open Feliz
open Types

type ValidationPackageBrowserContext = {
    RowStateMap: Map<string, PackageRowState>
    Toggle: ValidationPackageDTO -> unit
    UpdateToLatest: ValidationPackageDTO -> unit
}

let ValidationPackageBrowserCtx =
    React.createContext<ValidationPackageBrowserContext> (
        {
            RowStateMap = Map.empty
            Toggle = ignore
            UpdateToLatest = ignore
        }
    )

[<Hook>]
let useValidationPackageBrowserCtx () =
    React.useContext ValidationPackageBrowserCtx
