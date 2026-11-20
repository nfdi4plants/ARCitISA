module Main.IPC.UserDefaultsApi

open Fable.Core
open Fable.Electron.Main
open Swate.Components.Composite.Authentication.Helper
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc

let private toDto (store: Main.UserDefaults.IUserDefaultsStore) : UserDefaultsDto = {
    Effective = store.Effective()
    BuiltIn = Main.UserDefaults.builtIn
}

let private broadcast (dto: UserDefaultsDto) =
    BrowserWindow.getAllWindows ()
    |> Array.iter (fun window ->
        Main.WindowSend.send<IUserDefaultsRendererApi> window (fun api -> api.userDefaultsChanged dto)
    )

/// Stores the values and broadcasts them to every window. A changed Git executable path reaches the
/// library at once and replaces a running dependency check.
let private update (store: Main.UserDefaults.IUserDefaultsStore) (values: UserDefaultValuesDto) =
    match store.Update values with
    | Error message -> Error message
    | Ok _ ->
        let dto = toDto store
        let previousPath = Main.VersionControl.GitExecutableSetting.get ()
        Main.VersionControl.GitExecutableSetting.set dto.Effective.GitExecutablePath
        broadcast dto

        if previousPath <> dto.Effective.GitExecutablePath then
            Main.VersionControl.DependencyState.restart ()

        Ok dto

/// The API over a given store.
let createApi (store: Main.UserDefaults.IUserDefaultsStore) : IUserDefaultsApi = {
    getUserDefaults = fun () -> promise { return toDto store }
    setUserDefaults = fun values -> promise { return update store values }
}

let api: IUserDefaultsApi = createApi Main.UserDefaults.production

/// Adds the address of a successful sign-in to the DataHUB addresses, unless the sign-in offers it already.
let rememberDataHub (store: Main.UserDefaults.IUserDefaultsStore) (url: string) =
    let effective = store.Effective()

    match DataHubCollection.TryAddUrl(url, effective.DataHubUrls) with
    | None -> ()
    | Some dataHubUrls ->
        match
            update store {
                effective with
                    DataHubUrls = dataHubUrls
            }
        with
        | Ok _ -> ()
        | Error message -> Browser.Dom.console.warn $"Could not save the DataHUB address: {message}"
