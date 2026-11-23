module Api

open Browser.Types
open Swate.Components.Shared
open Swate.Electron.Shared.IPCTypes

open Fable.Core
open Fable.Core.JsInterop
open Fable.Remoting.Client
open Fable.Electron.Remoting.Renderer

type IFilePathApi =
    abstract getPathForFile: File -> string

[<Emit("window.ARCITISA_FILE_PATHS")>]
let filePathApi: IFilePathApi = jsNative

let ipcGitLabApi = Remoting.createIpc () |> Remoting.buildProxySender<IGitLabApi>

let ipcVersionControlApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IVersionControlApi>

let ipcArcVaultApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IArcVaultsApi>

let ipcAuthApi = Remoting.createIpc () |> Remoting.buildProxySender<IAuthApi>

let ipcAppVersionApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IAppVersionApi>

let ipcUiSettingsApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IUiSettingsApi>

let ipcUserDefaultsApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IUserDefaultsApi>

let ipcTemplateApi =
    Remoting.createIpc () |> Remoting.buildProxySender<ITemplateApi>

let ipcValidationPackageApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IValidationPackageIPC>
