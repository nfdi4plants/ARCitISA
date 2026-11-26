module Arcitisa.Electron.Shared.AuthTypes

open Arcitisa.Components.Composite.Authentication.Types

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

/// What happened to the tokens on their DataHUBs when their accounts were removed.
[<RequireQualifiedAccess>]
type TokenDeletionOutcome =
    | Kept
    /// Every token was deleted.
    | Deleted
    /// The reason is a message for the user that names each account whose token was not deleted.
    | Failed of reason: string

/// Result returned by auth sign-in / revalidate operations.
type AuthResult = {
    Success: bool
    User: AuthStateDto option
    FailureKind: AuthFailureKind option
    Message: string option
}
