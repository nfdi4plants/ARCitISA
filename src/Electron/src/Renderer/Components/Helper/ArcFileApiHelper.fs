module Renderer.Components.Helper.ArcFileApiHelper


open Fable.Core
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes

let withArcFileRequest
    (arcFile: ArcFiles)
    (execute: FileContentDTO -> JS.Promise<Result<'T, exn>>)
    : JS.Promise<Result<'T, exn>> =
    promise {
        match FileContentDTO.fromArcFile arcFile with
        | None -> return Error(exn "Saving this file type is not supported in ARCitISA yet.")
        | Some request -> return! execute request
    }

let saveArcFile (arcFile: ArcFiles) : JS.Promise<Result<unit, exn>> =
    withArcFileRequest
        arcFile
        (fun request -> promise {
            let! setResult = Api.ipcArcVaultApi.setArcFileInMemory request

            match setResult with
            | Error exn -> return Error exn
            | Ok() -> return! Api.ipcArcVaultApi.saveArcFile ()
        })
