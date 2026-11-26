module Arcitisa.Components.Composite.Authentication.Types

type SignInInformation = {
    GitLabBaseUrl: string
    PersonalAccessToken: string
    OnErrorCallback: exn -> unit
}

type GitLabIdentity = {
    provider: string
    extern_uid: string
    saml_provider_id: int option
}

type GitLabScimIdentity = obj

/// https://docs.gitlab.com/api/users/#as-an-administrator-2
type GitLabUser = {
    id: int
    username: string
    public_email: string option
    name: string
    state: string
    locked: bool
    avatar_url: string
    web_url: string
    created_at: string
    bio: string
    location: string
    linkedin: string
    twitter: string
    discord: string
    website_url: string
    github: string
    job_title: string
    pronouns: string option
    organization: string
    bot: bool
    work_information: string option
    local_time: string option
    last_sign_in_at: string
    confirmed_at: string
    last_activity_on: string
    email: string
    theme_id: int
    color_scheme_id: int
    projects_limit: int
    current_sign_in_at: string
    identities: GitLabIdentity list
    can_create_group: bool
    can_create_project: bool
    two_factor_enabled: bool
    external: bool
    private_profile: bool
    commit_email: string
    preferred_language: string
    shared_runners_minutes_limit: int option
    extra_shared_runners_minutes_limit: int option
    scim_identities: GitLabScimIdentity list
}

type AuthUserDto = {
    Id: int
    LocalArcitisaAccountId: string
    /// GitLab account shorthand. Used as the git commit author name so commits link to the account.
    Username: string
    Name: string
    Email: string
    /// GitLab commit email; a noreply address when the account keeps its email private in commits.
    CommitEmail: string option
    AvatarUrl: string
    TargetDataHub: string
} with

    static member FromGitLabUser (gitLabUser: GitLabUser) (targetDataHub: string) : AuthUserDto = {
        Id = gitLabUser.id
        LocalArcitisaAccountId = string gitLabUser.id
        Username = gitLabUser.username
        Name = gitLabUser.name
        Email = gitLabUser.email
        CommitEmail = Some gitLabUser.commit_email
        AvatarUrl = gitLabUser.avatar_url
        TargetDataHub = targetDataHub
    }

[<RequireQualifiedAccess>]
type TokenStatus =
    | Ok
    | Expiring
    | Invalid

/// Platform-agnostic account summary for multi-account UI.
type AccountSummary = {
    User: AuthUserDto
    DateAdded: string
    TokenStatus: TokenStatus
    TokenExpiresOn: string option
}

/// Current auth state returned by getAuthState.
type AuthStateDto = {
    ActiveAccount: AccountSummary option
    StoredAccounts: AccountSummary array
} with

    static member Empty = {
        ActiveAccount = None
        StoredAccounts = [||]
    }

    member this.ActiveUser() = this.ActiveAccount |> Option.map _.User

    member this.UsableActiveAccount() =
        this.ActiveAccount
        |> Option.filter (fun account -> account.TokenStatus <> TokenStatus.Invalid)

    member this.UsableActiveUser() =
        this.UsableActiveAccount() |> Option.map _.User

/// Removes stored accounts. With DeleteTokensOnDataHub their tokens are also deleted on their DataHUBs.
type RemoveAccountsRequest = {
    LocalArcitisaAccountIds: string[]
    DeleteTokensOnDataHub: bool
}
