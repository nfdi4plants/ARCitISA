module Main.Auth.AuthService

open System
open Fable.Core
open Swate.Electron.Shared.AuthTypes
open Swate.Components.Composite.Authentication.Types

// ── Types ────────────────────────────────────────────────────────────

type internal AuthFailure = GitLabApi.GitLabAuthFailure

type private AccountState = {
    Summary: AccountSummary
    Token: string
}

let private revalidationCooldown = TimeSpan.FromSeconds 30.0
let mutable private lastRevalidationStartedAtUtc: DateTime option = None

let shouldSkipRevalidation (lastStartedAtUtc: DateTime option) (now: DateTime) =
    lastStartedAtUtc
    |> Option.exists (fun lastStart -> (now - lastStart) < revalidationCooldown)

let nextTokenStatusState (currentTokenStatus: TokenStatus) (failureKind: AuthFailureKind) =
    match failureKind with
    | AuthFailureKind.Unauthorized
    | AuthFailureKind.Forbidden -> TokenStatus.Invalid
    | _ -> currentTokenStatus

let private tryParseIsoDateTime (raw: string option) : DateTime option =
    match raw with
    | Some value ->
        let mutable parsed = DateTime.MinValue

        if DateTime.TryParse(value, &parsed) then
            Some parsed
        else
            None
    | None -> None

let private tryParseIsoDate (raw: string option) : DateTime option =
    match raw with
    | Some value ->
        let mutable parsed = DateTime.MinValue

        if DateTime.TryParse(value, &parsed) then
            Some parsed.Date
        else
            None
    | None -> None

let private normalizeExpiresOnValue (expiresOn: DateTime option) : string option =
    expiresOn |> Option.map (fun dt -> dt.ToString("yyyy-MM-dd"))

let evaluateTokenStatus
    (nowUtc: DateTime)
    (createdAtRaw: string option)
    (expiresAtRaw: string option)
    (isActive: bool)
    (isRevoked: bool)
    : TokenStatus * string option =
    let expiresOn = expiresAtRaw |> tryParseIsoDate
    let expiresOnValue = normalizeExpiresOnValue expiresOn

    if isRevoked || not isActive then
        TokenStatus.Invalid, expiresOnValue
    else
        match expiresOn with
        | Some expiresAt when nowUtc.Date > expiresAt.Date -> TokenStatus.Invalid, expiresOnValue
        | Some expiresAt ->
            let startedAt = createdAtRaw |> tryParseIsoDateTime

            let startedWithOneMonthOrLonger =
                startedAt
                |> Option.exists (fun createdAt ->
                    let totalLifetime = expiresAt.Date - createdAt.Date
                    totalLifetime >= TimeSpan.FromDays 30.0
                )

            let remainingLifetime = expiresAt.Date - nowUtc.Date

            if startedWithOneMonthOrLonger && remainingLifetime <= TimeSpan.FromDays 14.0 then
                TokenStatus.Expiring, expiresOnValue
            else
                TokenStatus.Ok, expiresOnValue
        | None -> TokenStatus.Ok, None

/// Normalize and validate a GitLab base URL. HTTPS only.
let private normalizeBaseUrl (baseUrl: string) : Result<string, AuthFailure> =
    match Swate.Components.Composite.Authentication.Helper.GitLabUrls.tryNormalize baseUrl with
    | Some normalized -> Ok normalized
    | None when String.IsNullOrWhiteSpace(baseUrl.Trim().TrimEnd('/')) ->
        Error {
            Kind = AuthFailureKind.EndpointInvalid
            Message = "DataHUB URL is empty."
        }
    | None ->
        Error {
            Kind = AuthFailureKind.EndpointInvalid
            Message = "DataHUB URL must be a valid HTTPS URL."
        }


// ── Mutable in-memory state (multi-account) ──────────────────────────
//
// Token provider policy:
//   1. Check active account first — if its host matches, use its token.
//   2. If no match, search all accounts for a host match.
//   3. If still no match, return None.

/// Map of localSwateAccountId → account state (summary + PAT).
let mutable private accounts: Map<string, AccountState> = Map.empty

/// Currently active local account key.
let mutable private activeLocalSwateAccountId: string option = None

let private getActiveAccountState () =
    activeLocalSwateAccountId |> Option.bind (fun id -> accounts |> Map.tryFind id)

let private persistActiveSelection () =
    SecureAuthStore.setActiveLocalSwateAccountId activeLocalSwateAccountId

let private invalidateRevalidationWindow () = lastRevalidationStartedAtUtc <- None

let private reconcileActiveAccountInvariant () =
    let nextActive =
        if accounts.IsEmpty then
            None
        else
            match activeLocalSwateAccountId with
            | Some id when accounts |> Map.containsKey id -> Some id
            | _ -> accounts |> Map.tryPick (fun id _ -> Some id)

    if activeLocalSwateAccountId <> nextActive then
        activeLocalSwateAccountId <- nextActive
        persistActiveSelection ()

let private toMetadata (localSwateAccountId: string) (summary: AccountSummary) : SecureAuthStore.AuthMetadata = {
    LocalSwateAccountId = localSwateAccountId
    Id = summary.User.Id
    Username = summary.User.Username
    Name = summary.User.Name
    Email = summary.User.Email
    CommitEmail = summary.User.CommitEmail
    AvatarUrl = summary.User.AvatarUrl
    TargetDataHub = summary.User.TargetDataHub
    DateAdded = summary.DateAdded
    TokenStatus = summary.TokenStatus
    TokenExpiresOn = summary.TokenExpiresOn
}

let private persistAccountState (localSwateAccountId: string) (accountState: AccountState) : unit =
    let storeResult =
        SecureAuthStore.store {
            Metadata = toMetadata localSwateAccountId accountState.Summary
            Token = accountState.Token
        }

    match storeResult with
    | Error msg -> Browser.Dom.console.warn $"Auth storage warning: {msg}"
    | Ok() -> ()

let private getAuthStateDto () : AuthStateDto = {
    ActiveAccount = getActiveAccountState () |> Option.map _.Summary
    StoredAccounts = accounts |> Map.toArray |> Array.map (fun (_, v) -> v.Summary)
}

let private canUseToken (accountState: AccountState) =
    accountState.Summary.TokenStatus <> TokenStatus.Invalid

/// Try to get token for a given host.
/// Policy: active account first, then any account matching the host.
let tryGetTokenForHost (host: string) : string option =
    // 1. Check active account
    match getActiveAccountState () with
    | Some accountState when
        canUseToken accountState
        && String.Equals(
            SecureAuthStore.extractHost accountState.Summary.User.TargetDataHub,
            host,
            StringComparison.OrdinalIgnoreCase
        )
        ->
        Some accountState.Token
    | _ ->
        // 2. Search all accounts
        accounts
        |> Map.tryPick (fun _ accountState ->
            if
                canUseToken accountState
                && String.Equals(
                    SecureAuthStore.extractHost accountState.Summary.User.TargetDataHub,
                    host,
                    StringComparison.OrdinalIgnoreCase
                )
            then
                Some accountState.Token
            else
                None
        )

/// Usable token of one stored account, by local account id.
let tryGetTokenForAccount (localSwateAccountId: string) : string option =
    accounts
    |> Map.tryFind localSwateAccountId
    |> Option.filter canUseToken
    |> Option.map _.Token

/// Name and email a commit by this account carries. The GitLab username links the
/// commit to the account, and the display name is used only for accounts stored before
/// usernames were persisted. The commit email wins over the primary email so GitLab's
/// "use a private email in commits" setting is respected. GitLab links noreply
/// addresses too.
let commitNameAndEmail (user: AuthUserDto) : string * string =
    let name =
        if String.IsNullOrWhiteSpace user.Username then
            user.Name
        else
            user.Username

    let email =
        match user.CommitEmail with
        | Some commitEmail when not (String.IsNullOrWhiteSpace commitEmail) -> commitEmail
        | _ -> user.Email

    name, email

/// Get the current in-memory auth state for the active account.
let getState () : AuthStateDto =
    reconcileActiveAccountInvariant ()
    getAuthStateDto ()

/// The in-memory auth state without reconciling or persisting anything. Version
/// control strategies read through this on every git command.
let peekState () : AuthStateDto = getAuthStateDto ()

/// List all stored account summaries.
let listAccounts () : AccountSummary array = (getState ()).StoredAccounts

/// Main-process only helper to read the active account and token.
let tryGetActiveAccountWithToken () : (AuthUserDto * string) option =
    getActiveAccountState ()
    |> Option.filter canUseToken
    |> Option.map (fun accountState -> accountState.Summary.User, accountState.Token)

/// Main-process helper to resolve a DataHUB endpoint for unauthenticated/public browsing.
let tryGetPreferredDataHub () : string option =
    match getActiveAccountState () with
    | Some accountState when not (String.IsNullOrWhiteSpace accountState.Summary.User.TargetDataHub) ->
        Some accountState.Summary.User.TargetDataHub
    | None ->
        accounts
        |> Map.tryPick (fun _ accountState ->
            if String.IsNullOrWhiteSpace accountState.Summary.User.TargetDataHub then
                None
            else
                Some accountState.Summary.User.TargetDataHub
        )
    | _ ->
        accounts
        |> Map.tryPick (fun _ accountState ->
            if String.IsNullOrWhiteSpace accountState.Summary.User.TargetDataHub then
                None
            else
                Some accountState.Summary.User.TargetDataHub
        )

// ── Public operations ────────────────────────────────────────────────

let private toAuthResult (result: Result<AuthStateDto, AuthFailure>) : AuthResult =
    match result with
    | Ok user -> {
        Success = true
        User = Some user
        FailureKind = None
        Message = None
      }
    | Error failure -> {
        Success = false
        User = None
        FailureKind = Some failure.Kind
        Message = Some failure.Message
      }

/// Sign in: validate, verify with GitLab, persist, set as active, update token provider.
let signIn (request: AuthSignInRequest) : JS.Promise<AuthResult> = promise {
    if not (SecureAuthStore.isAvailable ()) then
        return
            toAuthResult (
                Error {
                    Kind = AuthFailureKind.StorageUnavailable
                    Message = "ARCitISA safe storage is not available on this system."
                }
            )
    else
        match normalizeBaseUrl request.GitLabBaseUrl with
        | Error failure -> return toAuthResult (Error failure)
        | Ok baseUrl ->
            let! verifyResult = GitLabApi.verifyToken baseUrl request.PersonalAccessToken

            match verifyResult with
            | Error failure -> return toAuthResult (Error failure)
            | Ok user ->
                let! tokenInfoResult = GitLabApi.getCurrentPersonalAccessToken baseUrl request.PersonalAccessToken

                match tokenInfoResult with
                | Error failure -> return toAuthResult (Error failure)
                | Ok tokenInfo ->
                    let tokenStatus, tokenExpiresOn =
                        evaluateTokenStatus
                            DateTime.UtcNow
                            tokenInfo.created_at
                            tokenInfo.expires_at
                            tokenInfo.active
                            tokenInfo.revoked

                    if tokenStatus = TokenStatus.Invalid then
                        return
                            toAuthResult (
                                Error {
                                    Kind = AuthFailureKind.Unauthorized
                                    Message = "Personal Access Token is invalid or expired."
                                }
                            )
                    else
                        let dateAdded =
                            accounts
                            |> Map.tryFind user.LocalSwateAccountId
                            |> Option.map (fun accountState -> accountState.Summary.DateAdded)
                            |> Option.defaultValue (Swate.Components.DateTimeExtensions.getUtcNowISO ())

                        let accountState = {
                            Summary = {
                                User = user
                                DateAdded = dateAdded
                                TokenStatus = tokenStatus
                                TokenExpiresOn = tokenExpiresOn
                            }
                            Token = request.PersonalAccessToken
                        }

                        persistAccountState user.LocalSwateAccountId accountState

                        accounts <- accounts |> Map.add user.LocalSwateAccountId accountState
                        activeLocalSwateAccountId <- Some user.LocalSwateAccountId
                        persistActiveSelection ()
                        invalidateRevalidationWindow ()
                        let authStateDto = getState ()
                        return toAuthResult (Ok authStateDto)
}

/// Set a different account as active.
let setActiveAccount (localSwateAccountId: string) : AuthStateDto =
    match accounts |> Map.tryFind localSwateAccountId with
    | Some _ ->
        activeLocalSwateAccountId <- Some localSwateAccountId
        persistActiveSelection ()
        invalidateRevalidationWindow ()
    | None -> ()

    getState ()

/// The outcome of deleting a token on its DataHUB. A token the DataHUB no longer accepts can't be used
/// anywhere, so it counts as deleted.
let tokenDeletionOutcome (result: Result<unit, AuthFailure>) : TokenDeletionOutcome =
    match result with
    | Ok()
    | Error { Kind = AuthFailureKind.Unauthorized } -> TokenDeletionOutcome.Deleted
    | Error failure -> TokenDeletionOutcome.Failed failure.Message

/// Remove a specific account (by local key). If it was active, switch to next or clear. Returns the DataHUB
/// and the token of the removed account, so the token can still be deleted there.
let removeAccount (localSwateAccountId: string) : (string * string) option =
    let removed =
        accounts
        |> Map.tryFind localSwateAccountId
        |> Option.map (fun accountState -> accountState.Summary.User.TargetDataHub, accountState.Token)

    SecureAuthStore.remove localSwateAccountId
    accounts <- accounts |> Map.remove localSwateAccountId

    reconcileActiveAccountInvariant ()
    invalidateRevalidationWindow ()
    removed

/// Delete a token on its DataHUB.
let deleteToken (dataHub: string) (token: string) : JS.Promise<TokenDeletionOutcome> =
    GitLabApi.revokeCurrentPersonalAccessToken dataHub token
    |> Promise.map tokenDeletionOutcome

/// Rotate PAT for a specific account and replace the stored token.
let rotatePersonalAccessToken (localSwateAccountId: string) : JS.Promise<Result<AuthStateDto, AuthFailure>> = promise {
    match accounts |> Map.tryFind localSwateAccountId with
    | None ->
        return
            Error {
                Kind = AuthFailureKind.EndpointInvalid
                Message = "The selected account no longer exists."
            }
    | Some accountState ->
        let baseUrl = accountState.Summary.User.TargetDataHub
        let! rotateResult = GitLabApi.rotateCurrentPersonalAccessToken baseUrl accountState.Token

        match rotateResult with
        | Error failure -> return Error failure
        | Ok rotatedToken ->
            let tokenStatus, tokenExpiresOn =
                evaluateTokenStatus
                    DateTime.UtcNow
                    rotatedToken.created_at
                    rotatedToken.expires_at
                    rotatedToken.active
                    rotatedToken.revoked

            if tokenStatus = TokenStatus.Invalid then
                return
                    Error {
                        Kind = AuthFailureKind.Unauthorized
                        Message = "Refreshed Personal Access Token is invalid or expired."
                    }
            else
                let updatedAccountState = {
                    accountState with
                        Summary = {
                            accountState.Summary with
                                TokenStatus = tokenStatus
                                TokenExpiresOn = tokenExpiresOn
                        }
                        Token = rotatedToken.token
                }

                accounts <- accounts |> Map.add localSwateAccountId updatedAccountState
                persistAccountState localSwateAccountId updatedAccountState
                invalidateRevalidationWindow ()

                return Ok(getState ())
}

/// Revalidate all stored accounts and update token status without removing accounts.
/// The boolean indicates whether a network-backed revalidation actually ran.
let revalidate () : JS.Promise<AuthResult * bool> = promise {
    if accounts.IsEmpty then
        let authStateDto = getState ()

        return
            ({
                Success = false
                User = Some authStateDto
                FailureKind = Some AuthFailureKind.Unauthorized
                Message = Some "No stored accounts."
             },
             false)
    else
        let now = DateTime.UtcNow

        if shouldSkipRevalidation lastRevalidationStartedAtUtc now then
            let authStateDto = getState ()

            return
                ({
                    Success = true
                    User = Some authStateDto
                    FailureKind = None
                    Message = None
                 },
                 false)
        else
            lastRevalidationStartedAtUtc <- Some now

            let mutable nextAccounts = accounts
            let mutable firstFailure: AuthFailure option = None

            for localSwateAccountId, accountState in accounts |> Map.toArray do
                let currentUser = accountState.Summary.User

                let! tokenInfoResult =
                    GitLabApi.getCurrentPersonalAccessToken currentUser.TargetDataHub accountState.Token

                match tokenInfoResult with
                | Ok tokenInfo ->
                    let tokenStatus, tokenExpiresOn =
                        evaluateTokenStatus
                            now
                            tokenInfo.created_at
                            tokenInfo.expires_at
                            tokenInfo.active
                            tokenInfo.revoked

                    let! verifyResult = GitLabApi.verifyToken currentUser.TargetDataHub accountState.Token

                    match verifyResult with
                    | Ok verifiedUser ->
                        // Keep the persisted local key stable to avoid file renames on profile changes.
                        let stableUser = {
                            verifiedUser with
                                LocalSwateAccountId = localSwateAccountId
                        }

                        let updatedAccountState = {
                            accountState with
                                Summary = {
                                    accountState.Summary with
                                        User = stableUser
                                        TokenStatus = tokenStatus
                                        TokenExpiresOn = tokenExpiresOn
                                }
                        }

                        nextAccounts <- nextAccounts |> Map.add localSwateAccountId updatedAccountState
                        persistAccountState localSwateAccountId updatedAccountState

                    | Error failure ->
                        if firstFailure.IsNone then
                            firstFailure <- Some failure

                        let updatedAccountState = {
                            accountState with
                                Summary = {
                                    accountState.Summary with
                                        TokenStatus = tokenStatus
                                        TokenExpiresOn = tokenExpiresOn
                                }
                        }

                        nextAccounts <- nextAccounts |> Map.add localSwateAccountId updatedAccountState
                        persistAccountState localSwateAccountId updatedAccountState

                | Error failure ->
                    if firstFailure.IsNone then
                        firstFailure <- Some failure

                    let nextTokenStatus =
                        nextTokenStatusState accountState.Summary.TokenStatus failure.Kind

                    let updatedAccountState = {
                        accountState with
                            Summary = {
                                accountState.Summary with
                                    TokenStatus = nextTokenStatus
                            }
                    }

                    nextAccounts <- nextAccounts |> Map.add localSwateAccountId updatedAccountState
                    persistAccountState localSwateAccountId updatedAccountState

            accounts <- nextAccounts
            reconcileActiveAccountInvariant ()

            let authStateDto = getState ()

            match firstFailure with
            | Some failure ->
                return
                    ({
                        Success = false
                        User = Some authStateDto
                        FailureKind = Some failure.Kind
                        Message = Some failure.Message
                     },
                     true)
            | None ->
                return
                    ({
                        Success = true
                        User = Some authStateDto
                        FailureKind = None
                        Message = None
                     },
                     true)
}

/// Restore all accounts from persisted secure storage on app startup.
let tryRestoreFromStorage () : unit =
    let stored = SecureAuthStore.loadAll ()

    for credential in stored do
        let user: AuthUserDto = {
            Id = credential.Metadata.Id
            LocalSwateAccountId = credential.Metadata.LocalSwateAccountId
            Username = credential.Metadata.Username
            Name = credential.Metadata.Name
            Email = credential.Metadata.Email
            CommitEmail = credential.Metadata.CommitEmail
            AvatarUrl = credential.Metadata.AvatarUrl
            TargetDataHub = credential.Metadata.TargetDataHub
        }

        let accountState = {
            Summary = {
                User = user
                DateAdded = credential.Metadata.DateAdded
                TokenStatus = credential.Metadata.TokenStatus
                TokenExpiresOn = credential.Metadata.TokenExpiresOn
            }
            Token = credential.Token
        }

        accounts <- accounts |> Map.add user.LocalSwateAccountId accountState

    // Restore persisted active account selection
    activeLocalSwateAccountId <- SecureAuthStore.getActiveLocalSwateAccountId ()

    reconcileActiveAccountInvariant ()
