/// Git provider strategies backed by the DataHUB account store. The library asks
/// for a credential per host and for a revision identity per operation. Both lookups
/// read the in-memory account state without writing anything and never throw.
module Main.VersionControl.DataHubStrategies

open System
open Arcitisa.Components.Composite.Authentication.Types
open VersionControlService.Git.GitCredentialStrategy

/// Username git sends together with a DataHUB personal access token.
[<Literal>]
let TokenUsername = "oauth2"

/// The reads the strategies need. AuthService supplies the production values, tests
/// build one over a fixed account list.
type DataHubAccountSource = {
    GetState: unit -> AuthStateDto
    /// Usable token of one stored account, by local account id.
    TryGetTokenForAccount: string -> string option
    /// Usable token of the account matching a host, active account first.
    TryGetTokenForHost: string -> string option
}

/// The same host rule the token lookup in AuthService applies.
let hostOfDataHub (targetDataHub: string) : string =
    Main.Auth.SecureAuthStore.extractHost targetDataHub

let private hostMatches (host: string) (account: AccountSummary) =
    String.Equals(hostOfDataHub account.User.TargetDataHub, host, StringComparison.OrdinalIgnoreCase)

/// AuthService selects the account identity, including its name and email. The
/// provider and sidebar then agree on the author.
let identityOfUser (user: AuthUserDto) : RevisionIdentity =
    let name, email = Main.Auth.AuthService.commitNameAndEmail user
    { Name = name; Email = email }

/// Picks the account whose identity a revision should carry.
/// A connection profile names a stored account directly and wins when that account
/// also matches the target host (or no host is known). A known host then selects the
/// active account when it matches, otherwise any stored account for that host, and
/// nothing when no account is signed in for that host. Without a host the active
/// account is used, because publishing will target its hub.
let selectAccount (state: AuthStateDto) (targetHost: string option) (connectionProfileId: string option) =
    let profileAccount =
        connectionProfileId
        |> Option.bind (fun profileId ->
            state.StoredAccounts
            |> Array.tryFind (fun account -> account.User.LocalArcitisaAccountId = profileId)
        )
        |> Option.filter (fun account ->
            match targetHost with
            | Some host -> hostMatches host account
            | None -> true
        )

    match profileAccount with
    | Some account -> Some account
    | None ->
        match targetHost with
        | None -> state.ActiveAccount
        | Some host ->
            state.ActiveAccount
            |> Option.filter (hostMatches host)
            |> Option.orElseWith (fun () -> state.StoredAccounts |> Array.tryFind (hostMatches host))

let resolveIdentity (source: DataHubAccountSource) (request: RevisionIdentityRequest) : RevisionIdentity option =
    try
        selectAccount (source.GetState()) request.TargetHost request.ConnectionProfileId
        |> Option.map (fun account -> identityOfUser account.User)
    with _ ->
        None

/// A credential is only returned for the exact host git connects to. The profile
/// account is tried first when its hub is that host, then the host lookup that
/// prefers the active account.
let resolveCredential (source: DataHubAccountSource) (host: string) (connectionProfileId: string option) =
    try
        let normalizedHost = host.Trim().ToLowerInvariant()

        let profileToken =
            connectionProfileId
            |> Option.bind (fun profileId ->
                (source.GetState()).StoredAccounts
                |> Array.tryFind (fun account ->
                    account.User.LocalArcitisaAccountId = profileId
                    && hostMatches normalizedHost account
                )
            )
            |> Option.bind (fun account -> source.TryGetTokenForAccount account.User.LocalArcitisaAccountId)

        profileToken
        |> Option.orElseWith (fun () -> source.TryGetTokenForHost normalizedHost)
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.map (fun token -> {
            Username = TokenUsername
            Secret = token
        })
    with _ ->
        None

let createIdentityStrategy (source: DataHubAccountSource) : GitIdentityStrategy = {
    ResolveIdentity = fun request -> async { return resolveIdentity source request }
}

let createCredentialStrategy (source: DataHubAccountSource) : GitCredentialStrategy = {
    ResolveCredential = fun host profileId -> async { return resolveCredential source host profileId }
}
