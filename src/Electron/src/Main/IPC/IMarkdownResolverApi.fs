module Main.IPC.MarkdownResolverApi

open Fable.Core
open Fable.Electron
open Fable.Electron.Main
open Main
open Main.ArcVaultHelper
open Arcitisa.Electron.Shared.IPCTypes

let private noVaultError (windowId: int) =
    exn $"The ARC for window id {windowId} should exist"

let api (event: IpcMainInvokeEvent) : IMarkdownResolverApi = {
    getAssetBaseUrl =
        fun () -> promise {
            try
                let windowId = IPCHelper.windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault windowId with
                | None -> return Error(noVaultError windowId)
                | Some vault when vault.path.IsNone -> return Error(arcNotOpenError ())
                | Some vault -> return Ok vault.MarkdownResolver.AssetBaseUrl
            with error ->
                return Error error
        }
    describeAsset =
        fun arcPath -> promise {
            try
                let windowId = IPCHelper.windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault windowId with
                | None -> return Error(noVaultError windowId)
                | Some vault ->
                    let! refusal = vault.MarkdownResolver.DescribeAsset arcPath
                    return Ok refusal
            with error ->
                return Error error
        }
    checkPaths =
        fun arcPaths -> promise {
            try
                let windowId = IPCHelper.windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault windowId with
                | None -> return Error(noVaultError windowId)
                | Some vault -> return! vault.MarkdownResolver.CheckPaths arcPaths
            with error ->
                return Error error
        }
    listFolder =
        fun request -> promise {
            try
                let windowId = IPCHelper.windowIdFromIpcEvent event

                match ARC_VAULTS.TryGetVault windowId with
                | None -> return Error(noVaultError windowId)
                | Some vault -> return! vault.MarkdownResolver.ListFolder(request.folder, request.namePrefix)
            with error ->
                return Error error
        }
}
