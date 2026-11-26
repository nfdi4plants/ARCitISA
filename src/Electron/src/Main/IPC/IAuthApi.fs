module Main.IPC.AuthApi

open Fable.Core
open Main
open Arcitisa.Electron.Shared.IPCTypes
open Arcitisa.Electron.Shared.IPCTypes.MainToRendererIpc
open Arcitisa.Electron.Shared.AuthTypes
open Arcitisa.Components.Composite.Authentication.Types
open Main.Auth

let private broadcastAccountsUpdate () =
    let authState = AuthService.getState ()

    ARC_VAULTS.Vaults.Values
    |> Array.ofSeq
    |> Array.iter (fun window ->
        WindowSend.send<IAuthAccountsRendererApi> window.window (fun client -> client.authAccountsUpdate authState)
    )

let api: IAuthApi = {
    signIn =
        fun (request: AuthSignInRequest) -> promise {
            try
                let! result = AuthService.signIn request

                if result.Success then
                    broadcastAccountsUpdate ()
                    UserDefaultsApi.rememberDataHub UserDefaults.production request.GitLabBaseUrl

                return Ok result
            with _ ->
                return Error(exn "Sign-in failed due to an unexpected error.")
        }
    getAuthState =
        fun () -> promise {
            try
                return Ok(AuthService.getState ())
            with _ ->
                return Error(exn "Failed to retrieve auth state.")
        }
    revalidate =
        fun () -> promise {
            try
                let! result, didRevalidate = AuthService.revalidate ()

                if didRevalidate then
                    broadcastAccountsUpdate ()

                return Ok result
            with _ ->
                return Error(exn "Token revalidation failed due to an unexpected error.")
        }
    listAccounts =
        fun () -> promise {
            try
                return Ok(AuthService.listAccounts ())
            with _ ->
                return Error(exn "Failed to list accounts.")
        }
    rotatePersonalAccessToken =
        fun (localArcitisaAccountId: string) -> promise {
            try
                let! result = AuthService.rotatePersonalAccessToken localArcitisaAccountId

                return
                    match result with
                    | Ok state ->
                        broadcastAccountsUpdate ()
                        Ok state
                    | Error failure -> Error(exn failure.Message)
            with _ ->
                return Error(exn "Failed to rotate personal access token.")
        }
    setActiveAccount =
        fun (localArcitisaAccountId: string) -> promise {
            try
                let state = AuthService.setActiveAccount localArcitisaAccountId
                broadcastAccountsUpdate ()
                return Ok state
            with _ ->
                return Error(exn "Failed to switch active account.")
        }
    removeAccounts =
        fun (request: RemoveAccountsRequest) -> promise {
            try
                // The accounts go at once. Deleting the tokens waits for the DataHUBs.
                let removed = AuthService.removeAccounts request.LocalArcitisaAccountIds
                broadcastAccountsUpdate ()

                if request.DeleteTokensOnDataHub then
                    let! outcome = AuthService.deleteTokens removed
                    return Ok outcome
                else
                    return Ok TokenDeletionOutcome.Kept
            with _ ->
                return Error(exn "Failed to remove accounts.")
        }
}
