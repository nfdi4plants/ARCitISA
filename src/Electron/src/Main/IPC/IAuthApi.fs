module Main.IPC.AuthApi

open Fable.Core
open Main
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.AuthTypes
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
    signOut =
        fun () -> promise {
            try
                AuthService.signOut ()
                broadcastAccountsUpdate ()
                return Ok()
            with _ ->
                return Error(exn "Sign-out failed due to an unexpected error.")
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
        fun (localSwateAccountId: string) -> promise {
            try
                let! result = AuthService.rotatePersonalAccessToken localSwateAccountId

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
        fun (localSwateAccountId: string) -> promise {
            try
                let state = AuthService.setActiveAccount localSwateAccountId
                broadcastAccountsUpdate ()
                return Ok state
            with _ ->
                return Error(exn "Failed to switch active account.")
        }
    removeAccount =
        fun (localSwateAccountId: string) -> promise {
            try
                AuthService.removeAccount localSwateAccountId
                broadcastAccountsUpdate ()
                return Ok()
            with _ ->
                return Error(exn "Failed to remove account.")
        }
}
