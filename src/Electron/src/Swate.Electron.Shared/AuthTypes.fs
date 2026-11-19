module Swate.Electron.Shared.AuthTypes

open Swate.Components.Composite.Authentication.Types

/// Request sent from Renderer to Main to sign in.
type AuthSignInRequest = {
    GitLabBaseUrl: string
    PersonalAccessToken: string
}

/// Typed failure categories for auth operations.
[<RequireQualifiedAccess>]
[<Fable.Core.StringEnum>]
type AuthFailureKind =
    | Unauthorized
    | Forbidden
    | Network
    | EndpointInvalid
    | StorageUnavailable
    | Unknown

/// What happened to the token on the DataHUB when its account was removed.
[<RequireQualifiedAccess>]
type TokenDeletionOutcome =
    | Kept
    | Deleted
    /// The reason is a message for the user.
    | Failed of reason: string

/// Result returned by auth sign-in / revalidate operations.
type AuthResult = {
    Success: bool
    User: AuthStateDto option
    FailureKind: AuthFailureKind option
    Message: string option
}
