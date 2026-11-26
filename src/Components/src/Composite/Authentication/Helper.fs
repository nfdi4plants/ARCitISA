module Arcitisa.Components.Composite.Authentication.Helper

open Types
open Arcitisa.Components

module GitLabUrls =

    /// The canonical form of an absolute HTTPS URL: lowercase scheme and host, no default port and no
    /// trailing slashes. Spellings of one address then compare equal.
    let tryNormalize (url: string) : string option =
        let mutable uri = Unchecked.defaultof<System.Uri>

        if
            System.Uri.TryCreate(url.Trim(), System.UriKind.Absolute, &uri)
            && uri.Scheme = "https"
        then
            Some(uri.AbsoluteUri.TrimEnd('/'))
        else
            None

    let prefillGitLabPATScopes (gitlabBaseUrl: string) =
        let gitlabBaseUrl = gitlabBaseUrl.TrimEnd('/')

        let scopes = [
            "read_user"
            "read_repository"
            "read_api"
            "api"
            "write_repository"
            "self_rotate" // This is used to allow users to rotate their token from within ARCitISA without having to log in to GitLab. It is a scope that only allows the token itself to be revoked, not any other tokens or account access.
        ]

        let scopeParam = scopes |> String.concat ","

        let description =
            "👉 What to do:
The permissions are already selected. Simply click \"Generate token\" below, then copy it and paste it back into ARCitISA.

ARCitISA uses this token to access your repositories and user information. You can revoke it at any time."
            |> Fable.Core.JS.encodeURIComponent

        sprintf
            "%s/-/user_settings/personal_access_tokens?name=arcitisa-electron&description=%s&scopes=%s"
            gitlabBaseUrl
            description
            scopeParam

    let profileUrl (user: Types.AuthUserDto) =
        let normalizedBaseUrl = user.TargetDataHub.TrimEnd('/')
        $"{normalizedBaseUrl}/-/u/{user.Id}"

type DataHubInformation = {
    Name: string
    Url: string
    Description: string option
} with

    member this.GetDescription() =
        match this.Description with
        | Some description -> description
        | None -> this.Url

    /// A DataHUB known only by its address. A valid address is named without its scheme.
    static member FromUrl(url: string) : DataHubInformation = {
        Name =
            GitLabUrls.tryNormalize url
            |> Option.map (fun normalized -> normalized.Substring(normalized.IndexOf "://" + 3))
            |> Option.defaultValue url
        Url = url
        Description = None
    }

[<Literal>]
let Default_DataHub_Url = "https://git.nfdi4plants.org/"

let Default_DataHub = {
    Name = "PLANTdataHUB (official)"
    Url = Default_DataHub_Url
    Description = Some "The official PLANTdataHUB instance, hosted by the nfdi4plants. Recommended for most users."
}

/// The DataHUBs the sign-in offers next to the official one until the user changes the list, as normalized
/// addresses.
let Default_DataHub_Urls = [|
    "https://gitlab.plantmicrobe.de"
    "https://datahub.rz.rptu.de"
|]

/// The official DataHUB and the others the sign-in offers.
type DataHubCollection = {
    Default: DataHubInformation
    Supported: DataHubInformation list
} with

    /// Whether one of the DataHUBs has this address. Trailing slashes do not matter.
    member this.Offers(url: string) =
        match GitLabUrls.tryNormalize url with
        | Some normalized ->
            this.Default :: this.Supported
            |> List.exists (fun dataHub -> GitLabUrls.tryNormalize dataHub.Url = Some normalized)
        | None -> false

    /// The official DataHUB with these addresses after it. An address offered already is left out.
    static member FromUrls(urls: string seq) =
        urls
        |> Seq.fold
            (fun (collection: DataHubCollection) url ->
                if collection.Offers url then
                    collection
                else
                    {
                        collection with
                            Supported = collection.Supported @ [ DataHubInformation.FromUrl url ]
                    }
            )
            {
                Default = Default_DataHub
                Supported = []
            }

    /// The addresses with this one added in normalized form. None when it is no HTTPS address or the official
    /// DataHUB or the addresses offer it already.
    static member TryAddUrl(url: string, urls: string[]) : string[] option =
        match GitLabUrls.tryNormalize url with
        | Some normalized when not (DataHubCollection.FromUrls(urls).Offers normalized) ->
            Some(Array.append urls [| normalized |])
        | _ -> None


module GitLabAPI =

    open Fetch

    [<RequireQualifiedAccess>]
    type SignInError =
        | NetworkError of exn
        | Unauthorized
        | Forbidden
        | NotFound
        | HttpError of int
        | DecodeError of exn

    let getUserAPIRequest (signInInfo: SignInInformation) : Fable.Core.JS.Promise<Result<AuthUserDto, SignInError>> = promise {
        let baseUrl = signInInfo.GitLabBaseUrl.TrimEnd('/')
        let pat = signInInfo.PersonalAccessToken
        let url = $"{baseUrl}/api/v4/user"

        let requestOptions = [
            RequestProperties.Method HttpMethod.GET
            requestHeaders [ HttpRequestHeaders.Custom("PRIVATE-TOKEN", pat) ]
        ]

        try
            console.log ($"Making API request to {url} with PAT")
            let! response = fetchUnsafe url requestOptions

            // ---- HTTP STATUS HANDLING ----
            if not response.Ok then
                return
                    match response.Status with
                    | 401 -> Result.Error SignInError.Unauthorized
                    | 403 -> Result.Error SignInError.Forbidden
                    | 404 -> Result.Error SignInError.NotFound
                    | code -> Result.Error(SignInError.HttpError code)
            else

                // ---- JSON PARSE ----
                try
                    let! gitLabUserInfo = response.json<Types.GitLabUser> ()

                    let userInfo = AuthUserDto.FromGitLabUser gitLabUserInfo signInInfo.GitLabBaseUrl

                    return Ok userInfo

                with ex ->
                    return Error(SignInError.DecodeError ex)

        // ---- NETWORK ERROR ----
        with ex ->
            return Error(SignInError.NetworkError ex)
    }
