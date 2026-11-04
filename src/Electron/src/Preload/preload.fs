module Preload

open Fable.Electron.Remoting.Preload
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc

Remoting.createIpc () |> Remoting.buildTwoWayBridge<IArcVaultsApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IVersionControlApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IGitLabApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IAuthApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<ITemplateApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IValidationPackageIPC>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IAppVersionApi>
Remoting.createIpc () |> Remoting.buildBridge<IAppVersionRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IReleaseNotesRendererApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IUiSettingsApi>
Remoting.createIpc () |> Remoting.buildTwoWayBridge<IUserDefaultsApi>

Remoting.createIpc () |> Remoting.buildBridge<IPathChangeRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IRecentArcsRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IAuthAccountsRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IFileTreeRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IFileImportRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IGitRepositoryRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IVersionControlRendererApi>

Remoting.createIpc ()
|> Remoting.buildBridge<IVersionControlDependencyRendererApi>

Remoting.createIpc () |> Remoting.buildBridge<IHasUnsavedArcChangesRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IUiSettingsRendererApi>
Remoting.createIpc () |> Remoting.buildBridge<IUserDefaultsRendererApi>

Remoting.createIpc () |> Remoting.buildBridge<IArcFileWatcherApi>
Remoting.createIpc () |> Remoting.buildBridge<IMainSaveBeforeQuitApi>
