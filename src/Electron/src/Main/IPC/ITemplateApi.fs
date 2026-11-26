module Main.IPC.TemplateApi

open ARCtrl
open Fable.Core
open Arcitisa.Electron.Shared.IPCTypes

/// TEMPORARY DUCT-TAPE WORKAROUND: ARCtrl's template web and JSON implementations currently hit Fable-only
/// dummy bindings on the .NET server, while the same download is blocked by GitHub CORS in the renderer.
/// Running ARCtrl.Template.Web in Electron main avoids both problems. Remove this module once the
/// regular ARCitISA template API works on .NET or the templates are served with suitable CORS headers.
let api: ITemplateApi = {
    getTemplates =
        fun () -> promise {
            try
                let! templates = ARCtrl.Template.Web.getTemplates None |> Async.StartAsPromise
                return Ok(ARCtrl.Json.Templates.toJsonString 0 (Array.ofSeq templates))
            with error ->
                return Error error
        }
}
