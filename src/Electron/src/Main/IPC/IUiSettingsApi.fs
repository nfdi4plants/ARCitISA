module Main.IPC.UiSettingsApi

open Fable.Core
open Arcitisa.Electron.Shared.IPCTypes

let api: IUiSettingsApi = {
    setUiScale = fun scale -> promise { return Main.UiSettings.setScale scale }
    getUiScale = fun () -> promise { return Main.UiSettings.getScale () }
}
