module Api

open Swate.Components.Shared
open Swate.Electron.Shared.IPCTypes

open Fable.Remoting.Client
open Fable.Electron.Remoting.Renderer

let ipcGitLabApi = Remoting.createIpc () |> Remoting.buildProxySender<IGitLabApi>

let ipcVersionControlApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IVersionControlApi>

let ipcArcVaultApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IArcVaultsApi>

let ipcMarkdownResolverApi =
    Remoting.createIpc () |> Remoting.buildProxySender<IMarkdownResolverApi>

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
