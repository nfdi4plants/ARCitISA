namespace Main.IPC

open Fable.Core
open Fable.Electron
open Fable.Electron.Main
open Main
open Main.ArcVaultState
open Main.ArcVaultHelper

[<AutoOpen>]
module IPCHelper =
    let windowFromIpcEvent (event: IpcMainInvokeEvent) =
        BrowserWindow.fromWebContents (event.sender)

    let dialogParentFromIpcEvent (event: IpcMainInvokeEvent) : BaseWindow option =
        windowFromIpcEvent event |> Option.map (fun window -> unbox<BaseWindow> window)

    let windowIdFromIpcEvent (event: IpcMainInvokeEvent) =
        BrowserWindow.fromWebContents (event.sender)
        |> Option.map _.id
        |> function
            | Some id -> id
            | None -> failwith $"Unable to access window from web-contents-id: '{event.sender.id}'"

    let tryGetVaultAndArcPath (event: IpcMainInvokeEvent) =
        let windowId = windowIdFromIpcEvent event

        match ARC_VAULTS.TryGetVault(windowId) with
        | None -> Error(exn $"The ARC for window id {windowId} should exist")
        | Some vault ->
            match vault.path with
            | Some arcPath -> Ok(vault, arcPath)
            | None -> Error(arcNotOpenError ())
