module Main.IPC.UserDefaultsApi

open Fable.Core
open Fable.Electron.Main
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

/// The API over a given store. A successful update broadcasts the new values to every window. A changed
/// Git executable path reaches the library at once and replaces a running dependency check.
let createApi (store: Main.UserDefaults.IUserDefaultsStore) : IUserDefaultsApi = {
    getUserDefaults = fun () -> promise { return toDto store }
    setUserDefaults =
        fun values -> promise {
            match store.Update values with
            | Error message -> return Error message
            | Ok _ ->
                let dto = toDto store
                let previousPath = Main.VersionControl.GitExecutableSetting.get ()
                Main.VersionControl.GitExecutableSetting.set dto.Effective.GitExecutablePath
                broadcast dto

                if previousPath <> dto.Effective.GitExecutablePath then
                    Main.VersionControl.DependencyState.restart ()

                return Ok dto
        }
}

let api: IUserDefaultsApi = createApi Main.UserDefaults.production
