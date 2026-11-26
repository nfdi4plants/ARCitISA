module Renderer.Components.LeftSidebar.FileExplorer.FileTreeFileTargetWorkflow

open Fable.Core
open Renderer.Components.Helper.FileSystemHelper
open Arcitisa.Electron.Shared.FileIOTypes

type AddFileTargetConfig = {
    IsBusy: bool
    PathExists: string -> JS.Promise<Result<bool, exn>>
    WriteTarget: FileContentDTO -> JS.Promise<unit>
    RequestOverwrite: FileContentDTO -> unit
    SetBusy: bool -> unit
}

let addFileTarget (config: AddFileTargetConfig) (request: FileContentDTO) = promise {
    if not config.IsBusy then
        config.SetBusy true

        match! checkTargetAvailability config.PathExists request.path with
        | Error exn ->
            config.SetBusy false
            return Error exn
        | Ok TargetAvailability.Exists ->
            config.RequestOverwrite request
            config.SetBusy false
            return Ok()
        | Ok TargetAvailability.Empty ->
            do! config.WriteTarget request
            config.SetBusy false
            return Ok()
    else
        return Ok()
}
