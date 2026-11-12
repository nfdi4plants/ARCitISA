module Renderer.Components.Helper.ArcFileApiHelper


open Fable.Core
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes

let withArcFileRequest
    (arcFile: ArcFiles)
    (execute: FileContentDTO -> JS.Promise<Result<'T, exn>>)
    : JS.Promise<Result<'T, exn>> =
    promise {
        match FileContentDTO.fromArcFile arcFile with
        | None -> return Error(exn "Saving this file type is not supported in ARCitISA yet.")
        | Some request -> return! execute request
    }

/// Sends the file to main together with the revision its content came from. Main refuses the edit when a sync changed
/// the file after that revision.
let setArcFileInMemory (arcFile: ArcFiles) (baseRevision: int) : JS.Promise<Result<SetArcFileOutcome, exn>> =
    withArcFileRequest
        arcFile
        (fun request ->
            Api.ipcArcVaultApi.setArcFileInMemory {|
                file = request
                baseRevision = baseRevision
            |}
        )
