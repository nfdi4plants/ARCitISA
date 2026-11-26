module Main.Auth.GitLabApi

open Fable.Core
open Arcitisa.Electron.Shared.AuthTypes
open Arcitisa.Components.Composite.Authentication.Types

module ComponentsGitLabApi = Arcitisa.Components.Api.GitLabApi

type GitLabAuthFailure = {
    Kind: AuthFailureKind
    Message: string
}

let private mapGitLabError (error: ComponentsGitLabApi.GitLabError) : GitLabAuthFailure =
    match error with
    | ComponentsGitLabApi.GitLabError.Unauthorized -> {
        Kind = AuthFailureKind.Unauthorized
        Message = "Personal Access Token is invalid or expired."
      }
    | ComponentsGitLabApi.GitLabError.Forbidden -> {
        Kind = AuthFailureKind.Forbidden
        Message = "Access forbidden. The token may lack required scopes."
      }
    | ComponentsGitLabApi.GitLabError.NotFound -> {
        Kind = AuthFailureKind.EndpointInvalid
        Message = "GitLab API endpoint not found. Check the DataHUB URL."
      }
    | ComponentsGitLabApi.GitLabError.NetworkError _ -> {
        Kind = AuthFailureKind.Network
        Message = "Network error contacting the DataHUB. Check your connection."
      }
    | ComponentsGitLabApi.GitLabError.DecodeError ex -> {
        Kind = AuthFailureKind.Unknown
        Message = $"Failed to decode GitLab API response: {ex.Message}"
      }
    | ComponentsGitLabApi.GitLabError.InvalidRequest message -> {
        Kind = AuthFailureKind.EndpointInvalid
        Message = message
      }
    | ComponentsGitLabApi.GitLabError.HttpError code -> {
        Kind = AuthFailureKind.Unknown
        Message = $"Unexpected HTTP status {code}."
      }
    | ComponentsGitLabApi.GitLabError.Unknown ex -> {
        Kind = AuthFailureKind.Unknown
        Message = ex.Message
      }

let private toAuthUserDto (baseUrl: string) (user: ComponentsGitLabApi.CurrentUserDto) : AuthUserDto =
    let localArcitisaAccountId =
        SecureAuthStore.generateLocalArcitisaAccountId baseUrl user.email

    {
        Id = user.id
        LocalArcitisaAccountId = localArcitisaAccountId
        Username = user.username
        Name = user.name
        Email = user.email
        CommitEmail = user.commit_email
        AvatarUrl = user.avatar_url |> Option.defaultValue ""
        TargetDataHub = baseUrl
    }

/// Call GitLab /api/v4/user with the provided PAT to verify user identity.
let verifyToken (baseUrl: string) (pat: string) : JS.Promise<Result<AuthUserDto, GitLabAuthFailure>> = promise {
    let! response = ComponentsGitLabApi.GitLabApi.GetCurrentUser(baseUrl, pat)

    return
        match response with
        | Ok user -> Ok(toAuthUserDto baseUrl user)
        | Error error -> Error(mapGitLabError error)
}

/// Call GitLab /api/v4/personal_access_tokens/self with the provided PAT.
let getCurrentPersonalAccessToken
    (baseUrl: string)
    (pat: string)
    : JS.Promise<Result<ComponentsGitLabApi.PersonalAccessTokenDto, GitLabAuthFailure>> =
    promise {
        let! response = ComponentsGitLabApi.GitLabApi.GetCurrentPersonalAccessToken(baseUrl, pat)

        return
            match response with
            | Ok token -> Ok token
            | Error error -> Error(mapGitLabError error)
    }

/// Delete the PAT itself via GitLab /api/v4/personal_access_tokens/self.
let revokeCurrentPersonalAccessToken (baseUrl: string) (pat: string) : JS.Promise<Result<unit, GitLabAuthFailure>> = promise {
    let! response = ComponentsGitLabApi.GitLabApi.RevokeCurrentPersonalAccessToken(baseUrl, pat)
    return response |> Result.mapError mapGitLabError
}

/// Rotate PAT via GitLab /api/v4/personal_access_tokens/self/rotate.
let rotateCurrentPersonalAccessToken
    (baseUrl: string)
    (pat: string)
    : JS.Promise<Result<ComponentsGitLabApi.RotatedPersonalAccessTokenDto, GitLabAuthFailure>> =
    promise {
        let! response = ComponentsGitLabApi.GitLabApi.RotateCurrentPersonalAccessToken(baseUrl, pat)

        return
            match response with
            | Ok token -> Ok token
            | Error error -> Error(mapGitLabError error)
    }
