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

    let withBusyWritingScope (vault: ArcVault) (operation: unit -> JS.Promise<'T>) : JS.Promise<'T> =
        vault.WithBusyWritingScope operation

    let withExclusiveBusyWriting
        (vault: ArcVault)
        (operation: unit -> JS.Promise<Result<'T, exn>>)
        : JS.Promise<Result<'T, exn>> =
        if vault.isBusyWriting then
            JS.Constructors.Promise.resolve (
                Error(exn "ARCitISA is still saving another change. Please wait a moment and try again.")
            )
        else
            withBusyWritingScope vault operation
