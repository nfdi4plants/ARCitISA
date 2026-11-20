module Renderer.Context.AuthStateContext

open Feliz
open Swate.Components
open Swate.Electron.Shared.AuthTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Components.Composite.Authentication.Types


module private Helper =

    let private isEmptyAuthState (state: AuthStateDto) =
        state.ActiveAccount.IsNone && state.StoredAccounts.Length = 0

    let shouldLogRevalidationFailure (response: AuthResult) =
        match response.Success, response.FailureKind, response.User with
        | false, Some AuthFailureKind.Unauthorized, Some state when isEmptyAuthState state -> false
        | false, _, _ -> true
        | true, _, _ -> false

let AuthStateCtx = React.createContext<AuthStateDto> AuthStateDto.Empty

[<Hook>]
let useAuthStateCtx () = React.useContext AuthStateCtx

open Helper

/// This component stores the current account information. If you want to switch account, login, etc.
/// - you simply can use the IAuthApi via IPC. Any changes will be broadcasted to all open windows via: `authAccountsUpdate`
[<ReactComponent>]
let Provider (children: ReactElement) =
    let loadAuthState () = promise {
        match! Api.ipcAuthApi.revalidate () with
        | Ok response ->
            let! stateResult = Api.ipcAuthApi.getAuthState ()

            if shouldLogRevalidationFailure response then
                console.error (response.FailureKind, Fable.Core.JS.JSON.stringify response.Message)

            match stateResult with
            | Ok state -> return state
            | Error _ -> return AuthStateDto.Empty
        | Error ex ->
            console.error (Fable.Core.JS.JSON.stringify ex.Message)
            return AuthStateDto.Empty
    }

    let authState =
        Renderer.MainSyncedState.useMainSyncedState {
            initial = AuthStateDto.Empty
            load = loadAuthState
            subscribe =
                fun setAuthState ->
                    Renderer.IpcReceiver.subscribeProxyReceiver<IAuthAccountsRendererApi> {
                        authAccountsUpdate = setAuthState
                    }
            onError = fun ex -> console.error (Fable.Core.JS.JSON.stringify ex.Message)
            dependencies = [||]
        }

    AuthStateCtx.Provider(authState.state, children)
