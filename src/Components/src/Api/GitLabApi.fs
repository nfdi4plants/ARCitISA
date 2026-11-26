module Arcitisa.Components.Api.GitLabApi

open System
open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Arcitisa.Components.Api.Helper

// Maintainer notes:
// - This module intentionally uses the same Fable.Fetch request style as Authentication.getUserAPIRequest.
// - PAT auth uses PRIVATE-TOKEN by default.
// - "Trending" is derived from REST data because GitLab REST has no dedicated trending endpoint.

[<RequireQualifiedAccess>]
type ProjectSortField =
    | Name
    | CreatedAt
    | UpdatedAt
    | StarCount
    | LastActivityAt

[<RequireQualifiedAccess>]
type SortDirection =
    | Asc
    | Desc

[<RequireQualifiedAccess>]
type PaginationMode =
    | Offset
    | Keyset

[<RequireQualifiedAccess>]
type ExploreScope =
    | MostStarred
    | Trending
    | MyRepos
    | GroupRepos

[<RequireQualifiedAccess>]
type TrendingStrategy =
    | ByLastActivity
    | ByStarsAndRecency

[<RequireQualifiedAccess>]
type UiProjectSort =
    | Name
    | DateCreated
    | DateUpdated

[<RequireQualifiedAccess>]
type GitLabError =
    | NetworkError of exn
    | Unauthorized
    | Forbidden
    | NotFound
    | HttpError of int
    | DecodeError of exn
    | InvalidRequest of string
    | Unknown of exn

    member this.GitLabErrorToString =
        match this with
        | GitLabError.NetworkError ex -> $"Network error: {ex.Message}"
        | GitLabError.Unauthorized -> "Unauthorized (check your Personal Access Token)."
        | GitLabError.Forbidden -> "Forbidden (missing permissions for this resource)."
        | GitLabError.NotFound -> "GitLab resource not found."
        | GitLabError.HttpError code -> $"GitLab request failed with HTTP {code}."
        | GitLabError.DecodeError ex -> $"Failed to decode GitLab response: {ex.Message}"
        | GitLabError.InvalidRequest message -> message
        | GitLabError.Unknown ex -> $"An unknown error occurred: {ex.Message}"

type ExploreNamespaceDto = {
    id: int
    name: string
    kind: string
    full_path: string
}

type ExploreProjectDto = {
    id: int
    name: string
    path_with_namespace: string
    name_with_namespace: string
    description: string option
    web_url: string
    http_url_to_repo: string
    ssh_url_to_repo: string option
    avatar_url: string option
    visibility: string option
    star_count: int
    created_at: System.DateTime option
    last_activity_at: System.DateTime option
    tag_list: string array
    ``namespace``: ExploreNamespaceDto
}

type CurrentUserDto = {
    id: int
    username: string
    name: string
    email: string
    commit_email: string option
    avatar_url: string option
}

type PersonalAccessTokenDto = {
    id: int
    name: string
    revoked: bool
    active: bool
    created_at: string option
    expires_at: string option
    last_used_at: string option
    scopes: string array
    user_id: int
    description: string option
}

type RotatedPersonalAccessTokenDto = {
    id: int
    name: string
    revoked: bool
    active: bool
    created_at: string option
    expires_at: string option
    last_used_at: string option
    scopes: string array
    user_id: int
    description: string option
    token: string
}

type GroupDto = {
    id: int
    name: string
    full_path: string
    web_url: string
    avatar_url: string option
}

/// A project to create. NamespaceId None creates it in the personal namespace of the token's user.
type CreateProjectRequest = {
    Name: string
    NamespaceId: int option
}

/// Why creating a project failed. The first two cases let the caller ask the user to change the
/// input. Every other failure stays a plain GitLabError.
[<RequireQualifiedAccess>]
type CreateProjectError =
    /// GitLab refused the project name or the path derived from it, for example a duplicate.
    | NameRefused of details: string option
    /// The user may not create projects in the chosen namespace, or reached a creation limit.
    | NotAllowed of details: string option
    | Failed of GitLabError

/// A group the user is a member of, with its project creation setting.
type PublishMemberGroup = {
    id: int
    name: string
    full_path: string
    project_creation_level: string option
}

/// A group in which the user may create projects.
type PublishAcceptingGroup = {
    id: int
    name: string
    full_path: string
}

/// A group the user belongs to. can_create_projects tells whether a project may be created in it.
type PublishGroupDto = {
    id: int
    name: string
    full_path: string
    project_creation_level: string option
    can_create_projects: bool
}

type PublishOwnersDto = {
    username: string
    groups: PublishGroupDto array
}

type PaginationMetadata = {
    Link: string option
    NextPage: int option
    Page: int option
    PerPage: int option
    PrevPage: int option
    Total: int option
    TotalPages: int option
    NextCursor: string option
    PrevCursor: string option
}

type PagedResponse<'T> = {
    Items: 'T array
    Pagination: PaginationMetadata
}

type GroupProjectsDto = {
    Group: GroupDto
    Projects: PagedResponse<ExploreProjectDto>
}

type ExploreBootstrapDto = {
    MostStarred: PagedResponse<ExploreProjectDto>
    Trending: PagedResponse<ExploreProjectDto>
    CurrentUser: CurrentUserDto
    MyRepos: PagedResponse<ExploreProjectDto>
    Groups: PagedResponse<GroupDto>
    GroupRepos: GroupProjectsDto array
}

type private GitLabNamespaceResponse = {
    id: int
    name: string
    kind: string
    full_path: string
}

type private GitLabProjectResponse = {
    id: int
    name: string
    path_with_namespace: string
    name_with_namespace: string
    description: string option
    web_url: string
    http_url_to_repo: string
    ssh_url_to_repo: string option
    avatar_url: string option
    visibility: string option
    star_count: int
    created_at: string
    last_activity_at: string
    topics: string array option
    tag_list: string array option
    ``namespace``: GitLabNamespaceResponse
}

type private GitLabCurrentUserResponse = {
    id: int
    username: string
    name: string
    email: string
    commit_email: string option
    avatar_url: string option
}

type private GitLabPersonalAccessTokenResponse = {
    id: int
    name: string
    revoked: bool
    active: bool
    created_at: string option
    expires_at: string option
    last_used_at: string option
    scopes: string array option
    user_id: int
    description: string option
}

type private GitLabRotatedPersonalAccessTokenResponse = {
    id: int
    name: string
    revoked: bool
    active: bool
    created_at: string option
    expires_at: string option
    last_used_at: string option
    scopes: string array option
    user_id: int
    description: string option
    token: string
}

type private GitLabGroupResponse = {
    id: int
    name: string
    full_path: string
    web_url: string
    avatar_url: string option
    project_creation_level: string option
}

type private GraphQlGroupNode = {
    id: string
    name: string
    fullPath: string
}

type private GraphQlPageInfo = {
    hasNextPage: bool
    endCursor: string option
}

type private GraphQlGroupConnection = {
    nodes: GraphQlGroupNode array
    pageInfo: GraphQlPageInfo
}

type private GraphQlCurrentUser = { groups: GraphQlGroupConnection }

type private GraphQlData = {
    currentUser: GraphQlCurrentUser option
}

type private GraphQlError = { message: string }

type private GraphQlGroupsResponse = {
    data: GraphQlData option
    errors: GraphQlError array option
}

[<RequireQualifiedAccess>]
type private JsonFailure =
    | HttpStatus of status: int * bodyText: string
    | Other of GitLabError

module private Internals =

    let private mapSortField =
        function
        | ProjectSortField.Name -> "name"
        | ProjectSortField.CreatedAt -> "created_at"
        | ProjectSortField.UpdatedAt -> "updated_at"
        | ProjectSortField.StarCount -> "star_count"
        | ProjectSortField.LastActivityAt -> "last_activity_at"

    let private mapSortDirection =
        function
        | SortDirection.Asc -> "asc"
        | SortDirection.Desc -> "desc"

    let private mapPaginationMode =
        function
        | PaginationMode.Offset -> "offset"
        | PaginationMode.Keyset -> "keyset"

    let mapUiSortField =
        function
        | UiProjectSort.Name -> ProjectSortField.Name
        | UiProjectSort.DateCreated -> ProjectSortField.CreatedAt
        | UiProjectSort.DateUpdated -> ProjectSortField.UpdatedAt

    let mapHttpError status =
        match status with
        | 401 -> GitLabError.Unauthorized
        | 403 -> GitLabError.Forbidden
        | 404 -> GitLabError.NotFound
        | code -> GitLabError.HttpError code

    let private compactResponseText (text: string) =
        text
        |> Option.ofObj
        |> Option.map (fun value -> value.Replace("\r", " ").Replace("\n", " ").Trim())
        |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let mapHttpErrorWithBody status bodyText =
        match mapHttpError status, compactResponseText bodyText with
        | GitLabError.HttpError code, Some details ->
            GitLabError.InvalidRequest $"GitLab request failed with HTTP {code}: {details}"
        | mappedError, _ -> mappedError

    let readResponseText (response: Fetch.Types.Response) : JS.Promise<string> = promise {
        try
            let! text = response.text ()
            return text
        with _ ->
            return ""
    }

    let makeGetRequestOptions (pat: string option) (acceptJson: bool) =
        let headers = [
            match pat with
            | Some token when not (String.IsNullOrWhiteSpace token) -> HttpRequestHeaders.Custom("PRIVATE-TOKEN", token)
            | _ -> ()
            if acceptJson then
                HttpRequestHeaders.Accept "application/json"
        ]

        [
            RequestProperties.Method HttpMethod.GET
            requestHeaders headers
        ]

    let tryGetHeader (response: Fetch.Types.Response) (name: string) : string option =
        response.Headers.get name |> Option.ofObj

    let tryParseInt (value: string option) : int option =
        match value with
        | Some raw when not (String.IsNullOrWhiteSpace raw) ->
            let mutable parsed = 0

            if Int32.TryParse(raw, &parsed) then Some parsed else None
        | _ -> None

    let readPagination (response: Fetch.Types.Response) : PaginationMetadata = {
        Link = tryGetHeader response "link"
        NextPage = tryParseInt (tryGetHeader response "x-next-page")
        Page = tryParseInt (tryGetHeader response "x-page")
        PerPage = tryParseInt (tryGetHeader response "x-per-page")
        PrevPage = tryParseInt (tryGetHeader response "x-prev-page")
        Total = tryParseInt (tryGetHeader response "x-total")
        TotalPages = tryParseInt (tryGetHeader response "x-total-pages")
        NextCursor = tryGetHeader response "x-next-cursor"
        PrevCursor = tryGetHeader response "x-prev-cursor"
    }

    let addString (name: string) (value: string option) (query: (string * obj) list) =
        match value with
        | Some x -> (name, box x) :: query
        | None -> query

    let addInt (name: string) (value: int option) (query: (string * obj) list) =
        match value with
        | Some x -> (name, box x) :: query
        | None -> query

    let addBool (name: string) (value: bool option) (query: (string * obj) list) =
        match value with
        | Some x -> (name, box x) :: query
        | None -> query

    let addSortField (value: ProjectSortField option) (query: (string * obj) list) =
        value |> Option.map mapSortField |> addString "order_by" <| query

    let addSortDirection (value: SortDirection option) (query: (string * obj) list) =
        value |> Option.map mapSortDirection |> addString "sort" <| query

    let addPaginationMode (value: PaginationMode option) (query: (string * obj) list) =
        value |> Option.map mapPaginationMode |> addString "pagination" <| query

    let toExploreNamespaceDto (ns: GitLabNamespaceResponse) : ExploreNamespaceDto = {
        id = ns.id
        name = ns.name
        kind = ns.kind
        full_path = ns.full_path
    }

    let toExploreProjectDto (project: GitLabProjectResponse) : ExploreProjectDto = {
        id = project.id
        name = project.name
        path_with_namespace = project.path_with_namespace
        description = project.description
        web_url = project.web_url
        http_url_to_repo = project.http_url_to_repo
        ssh_url_to_repo = project.ssh_url_to_repo
        avatar_url = project.avatar_url
        visibility = project.visibility
        star_count = project.star_count
        created_at =
            match System.DateTime.TryParse project.created_at with
            | (true, dt) -> Some dt
            | _ -> None
        last_activity_at =
            match System.DateTime.TryParse project.last_activity_at with
            | (true, dt) -> Some dt
            | _ -> None
        tag_list = project.topics |> Option.orElse project.tag_list |> Option.defaultValue [||]
        ``namespace`` = toExploreNamespaceDto project.``namespace``
        name_with_namespace = project.name_with_namespace
    }

    let toGroupDto (group: GitLabGroupResponse) : GroupDto = {
        id = group.id
        name = group.name
        full_path = group.full_path
        web_url = group.web_url
        avatar_url = group.avatar_url
    }

    let toExploreProjectPagedResponse
        (response: PagedResponse<GitLabProjectResponse>)
        : PagedResponse<ExploreProjectDto> =
        {
            Items = response.Items |> Array.map toExploreProjectDto
            Pagination = response.Pagination
        }

    let toCurrentUserDto (user: GitLabCurrentUserResponse) : CurrentUserDto = {
        id = user.id
        username = user.username
        name = user.name
        email = user.email
        commit_email = user.commit_email
        avatar_url = user.avatar_url
    }

    let toPersonalAccessTokenDto (token: GitLabPersonalAccessTokenResponse) : PersonalAccessTokenDto = {
        id = token.id
        name = token.name
        revoked = token.revoked
        active = token.active
        created_at = token.created_at
        expires_at = token.expires_at
        last_used_at = token.last_used_at
        scopes = token.scopes |> Option.defaultValue [||]
        user_id = token.user_id
        description = token.description
    }

    let toRotatedPersonalAccessTokenDto
        (token: GitLabRotatedPersonalAccessTokenResponse)
        : RotatedPersonalAccessTokenDto =
        {
            id = token.id
            name = token.name
            revoked = token.revoked
            active = token.active
            created_at = token.created_at
            expires_at = token.expires_at
            last_used_at = token.last_used_at
            scopes = token.scopes |> Option.defaultValue [||]
            user_id = token.user_id
            description = token.description
            token = token.token
        }

    let sendGet<'T> (url: string) (pat: string option) : JS.Promise<Result<PagedResponse<'T>, GitLabError>> =

        promise {
            let requestOptions = makeGetRequestOptions pat true

            try
                let! response = fetchUnsafe url requestOptions

                if not response.Ok then
                    return Error(mapHttpError response.Status)
                else
                    let pagination = readPagination response

                    try
                        let! payload = response.json<'T array> ()

                        return
                            Ok {
                                Items = payload
                                Pagination = pagination
                            }
                    with decodeError ->
                        return Error(GitLabError.DecodeError decodeError)
            with networkError ->
                return Error(GitLabError.NetworkError networkError)
        }

    let sendExploreProjectPage
        (url: string)
        (pat: string option)
        : JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>> =
        promise {
            let! response = sendGet<GitLabProjectResponse> url pat
            return response |> Result.map toExploreProjectPagedResponse
        }

    let sendGetSingle<'T> (url: string) (pat: string) : JS.Promise<Result<'T, GitLabError>> = promise {
        if String.IsNullOrWhiteSpace pat then
            return Error(GitLabError.InvalidRequest "Personal Access Token is required.")
        else
            let requestOptions = makeGetRequestOptions (Some pat) true

            try
                let! response = fetchUnsafe url requestOptions

                if not response.Ok then
                    return Error(mapHttpError response.Status)
                else
                    try
                        let! payload = response.json<'T> ()
                        return Ok payload
                    with decodeError ->
                        return Error(GitLabError.DecodeError decodeError)
            with networkError ->
                return Error(GitLabError.NetworkError networkError)
    }

    let sendJsonRawWith<'TResponse>
        (authHeader: HttpRequestHeaders)
        (url: string)
        (pat: string)
        (method': HttpMethod)
        (body: obj)
        : JS.Promise<Result<'TResponse, JsonFailure>> =
        promise {
            if String.IsNullOrWhiteSpace pat then
                return Error(JsonFailure.Other(GitLabError.InvalidRequest "Personal Access Token is required."))
            else
                let requestOptions = [
                    RequestProperties.Method method'
                    requestHeaders [
                        authHeader
                        HttpRequestHeaders.Accept "application/json"
                        HttpRequestHeaders.ContentType "application/json"
                    ]
                    RequestProperties.Body(unbox (Fable.Core.JS.JSON.stringify body))
                ]

                try
                    let! response = fetchUnsafe url requestOptions

                    if not response.Ok then
                        let! errorText = readResponseText response
                        return Error(JsonFailure.HttpStatus(response.Status, errorText))
                    else
                        try
                            let! payload = response.json<'TResponse> ()
                            return Ok payload
                        with decodeError ->
                            return Error(JsonFailure.Other(GitLabError.DecodeError decodeError))
                with networkError ->
                    return Error(JsonFailure.Other(GitLabError.NetworkError networkError))
        }

    /// A JSON request authenticated with PRIVATE-TOKEN. A failed response keeps its status and body text.
    let sendJsonRaw<'TResponse>
        (url: string)
        (pat: string)
        (method': HttpMethod)
        (body: obj)
        : JS.Promise<Result<'TResponse, JsonFailure>> =
        sendJsonRawWith<'TResponse> (HttpRequestHeaders.Custom("PRIVATE-TOKEN", pat)) url pat method' body

    let sendPostNoBody<'TResponse> (url: string) (pat: string) : JS.Promise<Result<'TResponse, GitLabError>> = promise {
        if String.IsNullOrWhiteSpace pat then
            return Error(GitLabError.InvalidRequest "Personal Access Token is required.")
        else
            let requestOptions = [
                RequestProperties.Method HttpMethod.POST
                requestHeaders [
                    HttpRequestHeaders.Custom("PRIVATE-TOKEN", pat)
                    HttpRequestHeaders.Accept "application/json"
                ]
            ]

            try
                let! response = fetchUnsafe url requestOptions

                if not response.Ok then
                    return Error(mapHttpError response.Status)
                else
                    try
                        let! payload = response.json<'TResponse> ()
                        return Ok payload
                    with decodeError ->
                        return Error(GitLabError.DecodeError decodeError)
            with networkError ->
                return Error(GitLabError.NetworkError networkError)
    }

    /// A DELETE that answers without a body, such as 204 No Content.
    let sendDeleteNoContent (url: string) (pat: string) : JS.Promise<Result<unit, GitLabError>> = promise {
        if String.IsNullOrWhiteSpace pat then
            return Error(GitLabError.InvalidRequest "Personal Access Token is required.")
        else
            let requestOptions = [
                RequestProperties.Method HttpMethod.DELETE
                requestHeaders [ HttpRequestHeaders.Custom("PRIVATE-TOKEN", pat) ]
            ]

            try
                let! response = fetchUnsafe url requestOptions

                if not response.Ok then
                    return Error(mapHttpError response.Status)
                else
                    return Ok()
            with networkError ->
                return Error(GitLabError.NetworkError networkError)
    }

    /// Lists every group of the current user (all pages), including the project_creation_level REST field.
    let listMemberGroups (baseUrl: string) (pat: string) : JS.Promise<Result<GitLabGroupResponse array, GitLabError>> =
        let rec loadPage (page: int) (collected: GitLabGroupResponse array list) = promise {
            let query =
                []
                |> addInt "min_access_level" (Some 10)
                |> addInt "per_page" (Some 100)
                |> addInt "page" (Some page)

            let! response =
                sendGet<GitLabGroupResponse>
                    (appendQueryParams $"{baseUrl.TrimEnd('/')}/api/v4/groups" query)
                    (Some pat)

            match response with
            | Error error -> return Error error
            | Ok paged ->
                let collected = paged.Items :: collected

                match paged.Pagination.NextPage with
                | Some nextPage -> return! loadPage nextPage collected
                | None -> return Ok(collected |> List.rev |> Array.concat)
        }

        loadPage 1 []

    // GitLab has no REST filter for "may create projects here". The GraphQL permissionScope filter does exactly that.
    let private groupsAcceptingProjectsQuery =
        "query($after: String) { currentUser { groups(permissionScope: CREATE_PROJECTS, first: 100, after: $after) { nodes { id name fullPath } pageInfo { hasNextPage endCursor } } } }"

    /// The numeric id at the end of a global id such as gid://gitlab/Group/123.
    let tryParseGlobalId (globalId: string) : int option =
        match Option.ofObj globalId with
        | Some value when value.LastIndexOf '/' >= 0 -> tryParseInt (Some(value.Substring(value.LastIndexOf '/' + 1)))
        | _ -> None

    let private sendGroupsAcceptingProjectsPage
        (baseUrl: string)
        (pat: string)
        (after: string option)
        : JS.Promise<Result<GraphQlGroupConnection, GitLabError>> =
        promise {
            let! response =
                sendJsonRawWith<GraphQlGroupsResponse>
                    (HttpRequestHeaders.Authorization $"Bearer {pat}")
                    $"{baseUrl.TrimEnd('/')}/api/graphql"
                    pat
                    HttpMethod.POST
                    (createObj [
                        "query" ==> groupsAcceptingProjectsQuery
                        "variables" ==> createObj [ "after" ==> Option.toObj after ]
                    ])

            return
                match response with
                | Error(JsonFailure.HttpStatus(status, _)) -> Error(mapHttpError status)
                | Error(JsonFailure.Other error) -> Error error
                | Ok payload ->
                    match payload.errors with
                    | Some errors when errors.Length > 0 ->
                        Error(
                            GitLabError.InvalidRequest(
                                errors |> Array.map (fun error -> error.message) |> String.concat "; "
                            )
                        )
                    | _ ->
                        match payload.data |> Option.bind (fun data -> data.currentUser) with
                        | Some user -> Ok user.groups
                        | None -> Error GitLabError.Unauthorized
        }

    /// Lists the groups the current user may create projects in (all pages). Nodes whose global id has no trailing integer are skipped.
    let listGroupsAcceptingProjects
        (baseUrl: string)
        (pat: string)
        : JS.Promise<Result<PublishAcceptingGroup array, GitLabError>> =
        let toAcceptingGroup (node: GraphQlGroupNode) =
            if isNull (box node) then
                None
            else
                Option.ofObj node.id
                |> Option.bind tryParseGlobalId
                |> Option.map (fun id ->
                    ({
                        id = id
                        name = node.name
                        full_path = node.fullPath
                    }
                    : PublishAcceptingGroup)
                )

        let rec loadFrom after collected = promise {
            let! page = sendGroupsAcceptingProjectsPage baseUrl pat after

            match page with
            | Error error -> return Error error
            | Ok connection ->
                let collected = (connection.nodes |> Array.choose toAcceptingGroup) :: collected

                match connection.pageInfo.hasNextPage, connection.pageInfo.endCursor with
                | true, Some cursor -> return! loadFrom (Some cursor) collected
                | _ -> return Ok(collected |> List.rev |> Array.concat)
        }

        loadFrom None []

/// Reads the `message` and `error` fields of a GitLab error body through typed bindings.
module private ErrorBody =

    [<AllowNullLiteral>]
    type IErrorBody =
        abstract message: obj
        abstract error: obj

    /// GitLab sends the text of a failure as a string or an array of strings, in `message` or in `error`.
    /// The text sits in `error` only when `message` is absent, for example the 403 limit reply
    /// {"error":["Your project limit is 5 projects! ..."]}. Validation failures put an object
    /// in `message` that maps field names to arrays of texts.
    [<RequireQualifiedAccess>]
    type Message =
        | Text of string
        | Fields of (string * string list) list

    let private toTexts (value: obj) : string list =
        if JS.Constructors.Array.isArray value then
            unbox<obj array> value
            |> Array.filter (fun item -> jsTypeof item = "string")
            |> Array.map unbox<string>
            |> List.ofArray
        elif jsTypeof value = "string" then
            [ unbox<string> value ]
        else
            []

    let private isPlainObject (value: obj) =
        not (isNull value)
        && jsTypeof value = "object"
        && not (JS.Constructors.Array.isArray value)

    let private tryText (value: obj) : Message option =
        if JS.Constructors.Array.isArray value || jsTypeof value = "string" then
            Some(Message.Text(String.concat "; " (toTexts value)))
        else
            None

    let tryParse (bodyText: string) : Message option =
        try
            let parsed = JS.JSON.parse bodyText

            if isNull parsed || jsTypeof parsed <> "object" then
                None
            else
                let body = unbox<IErrorBody> parsed

                if isPlainObject body.message then
                    Some(
                        Message.Fields [
                            for key, value in JS.Constructors.Object.entries body.message -> key, toTexts value
                        ]
                    )
                else
                    match tryText body.message with
                    | Some text -> Some text
                    | None -> tryText body.error
        with _ ->
            None

/// Tells a refused name and a missing permission apart from other failures of POST /api/v4/projects.
let classifyCreateProjectFailure (status: int) (bodyText: string) : CreateProjectError =
    let message = ErrorBody.tryParse bodyText

    let joined (texts: string list) =
        texts
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> function
            | [] -> None
            | filtered -> Some(String.concat "; " filtered)

    let messageText =
        match message with
        | Some(ErrorBody.Message.Text text) -> joined [ text ]
        | _ -> None

    let namespaceTexts =
        match message with
        | Some(ErrorBody.Message.Fields fields) ->
            fields |> List.tryFind (fun (key, _) -> key = "namespace") |> Option.map snd
        | _ -> None

    match namespaceTexts, status with
    // GitLab's namespace messages are field fragments such as "is not valid". They mean nothing to the user.
    | Some _, _ -> CreateProjectError.NotAllowed None
    | None, 403 -> CreateProjectError.NotAllowed messageText
    | None, (400 | 409) ->
        match message with
        | Some(ErrorBody.Message.Fields fields) ->
            fields
            |> List.collect (fun (key, texts) -> texts |> List.map (fun text -> $"{key} {text}"))
            |> joined
            |> CreateProjectError.NameRefused
        | _ -> CreateProjectError.NameRefused messageText
    | None, _ -> CreateProjectError.Failed(Internals.mapHttpErrorWithBody status bodyText)

/// Combines the groups of the user with the groups that accept new projects. A group that only
/// appears in the accepting list (for example one shared with a group of the user) is added with an
/// unknown creation level.
let mergePublishGroups
    (memberGroups: PublishMemberGroup array)
    (acceptingGroups: PublishAcceptingGroup array)
    : PublishGroupDto array =
    let acceptingIds =
        acceptingGroups |> Array.map (fun group -> group.id) |> Set.ofArray

    let fromMembers =
        memberGroups
        |> Array.map (fun group -> {
            id = group.id
            name = group.name
            full_path = group.full_path
            project_creation_level = group.project_creation_level
            can_create_projects = acceptingIds.Contains group.id
        })

    let memberIds = fromMembers |> Array.map (fun group -> group.id) |> Set.ofArray

    let onlyAccepting =
        acceptingGroups
        |> Array.filter (fun group -> not (memberIds.Contains group.id))
        |> Array.map (fun group -> {
            id = group.id
            name = group.name
            full_path = group.full_path
            project_creation_level = None
            can_create_projects = true
        })

    Array.append fromMembers onlyAccepting
    |> Array.distinctBy (fun group -> group.id)
    |> Array.sortBy (fun group -> group.full_path.ToLowerInvariant())

[<AttachMembers>]
type GitLabApi =

    /// GET /api/v4/user
    static member GetCurrentUser(baseUrl: string, pat: string) : JS.Promise<Result<CurrentUserDto, GitLabError>> = promise {
        let url = $"{baseUrl.TrimEnd('/')}/api/v4/user"
        let! response = Internals.sendGetSingle<GitLabCurrentUserResponse> url pat
        return response |> Result.map Internals.toCurrentUserDto
    }

    /// GET /api/v4/personal_access_tokens/self
    static member GetCurrentPersonalAccessToken
        (baseUrl: string, pat: string)
        : JS.Promise<Result<PersonalAccessTokenDto, GitLabError>> =
        promise {
            let url = $"{baseUrl.TrimEnd('/')}/api/v4/personal_access_tokens/self"
            let! response = Internals.sendGetSingle<GitLabPersonalAccessTokenResponse> url pat
            return response |> Result.map Internals.toPersonalAccessTokenDto
        }

    /// POST /api/v4/personal_access_tokens/self/rotate
    static member RotateCurrentPersonalAccessToken
        (baseUrl: string, pat: string)
        : JS.Promise<Result<RotatedPersonalAccessTokenDto, GitLabError>> =
        promise {
            let url = $"{baseUrl.TrimEnd('/')}/api/v4/personal_access_tokens/self/rotate"
            let! response = Internals.sendPostNoBody<GitLabRotatedPersonalAccessTokenResponse> url pat
            return response |> Result.map Internals.toRotatedPersonalAccessTokenDto
        }

    /// DELETE /api/v4/personal_access_tokens/self. Any token may delete itself.
    static member RevokeCurrentPersonalAccessToken
        (baseUrl: string, pat: string)
        : JS.Promise<Result<unit, GitLabError>> =
        Internals.sendDeleteNoContent $"{baseUrl.TrimEnd('/')}/api/v4/personal_access_tokens/self" pat

    /// POST /api/v4/projects
    static member CreateProject
        (baseUrl: string, pat: string, request: CreateProjectRequest)
        : JS.Promise<Result<ExploreProjectDto, CreateProjectError>> =
        promise {
            let normalizedName = request.Name.Trim()

            if String.IsNullOrWhiteSpace normalizedName then
                return Error(CreateProjectError.NameRefused(Some "Repository name is required."))
            else
                let! response =
                    Internals.sendJsonRaw<GitLabProjectResponse>
                        $"{baseUrl.TrimEnd('/')}/api/v4/projects"
                        pat
                        HttpMethod.POST
                        (createObj [
                            "name" ==> normalizedName
                            "initialize_with_readme" ==> false
                            match request.NamespaceId with
                            | Some namespaceId -> "namespace_id" ==> namespaceId
                            | None -> ()
                        ])

                return
                    match response with
                    | Ok project -> Ok(Internals.toExploreProjectDto project)
                    | Error(JsonFailure.HttpStatus(status, errorText)) ->
                        Error(classifyCreateProjectFailure status errorText)
                    | Error(JsonFailure.Other error) -> Error(CreateProjectError.Failed error)
        }

    /// The owners to choose from when publishing: the username of the personal namespace and the groups of the user.
    static member ListPublishOwners(baseUrl: string, pat: string) : JS.Promise<Result<PublishOwnersDto, GitLabError>> = promise {
        let! user = GitLabApi.GetCurrentUser(baseUrl, pat)

        match user with
        | Error error -> return Error error
        | Ok user ->
            let! memberGroups = Internals.listMemberGroups baseUrl pat

            match memberGroups with
            | Error error -> return Error error
            | Ok memberGroups ->
                let! acceptingGroups = Internals.listGroupsAcceptingProjects baseUrl pat

                match acceptingGroups with
                | Error error -> return Error error
                | Ok acceptingGroups ->
                    let members =
                        memberGroups
                        |> Array.map (fun group ->
                            ({
                                id = group.id
                                name = group.name
                                full_path = group.full_path
                                project_creation_level = group.project_creation_level
                            }
                            : PublishMemberGroup)
                        )

                    return
                        Ok {
                            username = user.username
                            groups = mergePublishGroups members acceptingGroups
                        }
    }

    /// GET /api/v4/projects
    static member ListProjects
        (
            baseUrl: string,
            pat: string,
            ?page: int,
            ?perPage: int,
            ?pagination: PaginationMode,
            ?orderBy: ProjectSortField,
            ?sort: SortDirection,
            ?membership: bool,
            ?owned: bool,
            ?starred: bool,
            ?search: string,
            ?visibility: string,
            ?minAccessLevel: int,
            ?topic: string
        ) : JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>> =
        promise {
            let baseEndpoint = $"{baseUrl.TrimEnd('/')}/api/v4/projects"

            let queryParams =
                []
                |> Internals.addInt "page" page
                |> Internals.addInt "per_page" perPage
                |> Internals.addPaginationMode pagination
                |> Internals.addSortField orderBy
                |> Internals.addSortDirection sort
                |> Internals.addBool "membership" membership
                |> Internals.addBool "owned" owned
                |> Internals.addBool "starred" starred
                |> Internals.addString "search" search
                |> Internals.addString "visibility" visibility
                |> Internals.addInt "min_access_level" minAccessLevel
                |> Internals.addString "topic" topic

            let effectiveQueryParams =
                match pagination with
                | Some PaginationMode.Keyset ->
                    // For projects, keyset pagination requires order_by=id.
                    queryParams
                    |> List.filter (fun (key, _) -> key <> "order_by")
                    |> fun q -> ("order_by", box "id") :: q
                    |> fun q ->
                        if q |> List.exists (fun (key, _) -> key = "sort") then
                            q
                        else
                            ("sort", box "desc") :: q
                | _ -> queryParams

            let url = appendQueryParams baseEndpoint effectiveQueryParams
            let patHeader = if String.IsNullOrWhiteSpace pat then None else Some pat
            return! Internals.sendExploreProjectPage url patHeader
        }

    /// GET /api/v4/users/:user_id/projects
    static member ListUserPersonalProjects
        (
            baseUrl: string,
            pat: string,
            // Without a userId, this endpoint fetches user by PAT and uses their ID, which is the common case for "My Repos". To improve performance, callers that already have the user ID can provide it to skip the extra user fetch.
            ?userId: int,
            ?page: int,
            ?perPage: int,
            ?pagination: PaginationMode,
            ?orderBy: ProjectSortField,
            ?sort: SortDirection,
            ?membership: bool,
            ?owned: bool,
            ?starred: bool,
            ?search: string,
            ?visibility: string,
            ?minAccessLevel: int
        ) : JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>> =
        promise {
            let! currentUserId =
                match userId with
                | Some id -> Promise.lift (Ok id)
                | None -> promise {
                    let! user = GitLabApi.GetCurrentUser(baseUrl, pat)

                    return
                        match user with
                        | Ok u -> Ok u.id
                        | Error err -> Error err
                  }

            match currentUserId with
            | Error err -> return Error err
            | Ok userId ->
                let baseEndpoint = $"{baseUrl.TrimEnd('/')}/api/v4/users/{userId}/projects"

                let queryParams =
                    []
                    |> Internals.addInt "page" page
                    |> Internals.addInt "per_page" perPage
                    |> Internals.addPaginationMode pagination
                    |> Internals.addSortField orderBy
                    |> Internals.addSortDirection sort
                    |> Internals.addBool "membership" membership
                    |> Internals.addBool "owned" owned
                    |> Internals.addBool "starred" starred
                    |> Internals.addString "search" search
                    |> Internals.addString "visibility" visibility
                    |> Internals.addInt "min_access_level" minAccessLevel

                let url = appendQueryParams baseEndpoint queryParams
                return! Internals.sendExploreProjectPage url (Some pat)
        }

    /// GET /api/v4/groups
    static member ListGroupsForCurrentUser
        (
            baseUrl: string,
            pat: string,
            ?minAccessLevel: int,
            ?owned: bool,
            ?allAvailable: bool,
            ?search: string,
            ?page: int,
            ?perPage: int
        ) : JS.Promise<Result<PagedResponse<GroupDto>, GitLabError>> =
        promise {
            let baseEndpoint = $"{baseUrl.TrimEnd('/')}/api/v4/groups"

            let queryParams =
                []
                |> Internals.addInt "min_access_level" minAccessLevel
                |> Internals.addBool "owned" owned
                |> Internals.addBool "all_available" allAvailable
                |> Internals.addString "search" search
                |> Internals.addInt "page" page
                |> Internals.addInt "per_page" perPage

            let url = appendQueryParams baseEndpoint queryParams
            let! response = Internals.sendGet<GitLabGroupResponse> url (Some pat)

            return
                response
                |> Result.map (fun x -> {
                    Items = x.Items |> Array.map Internals.toGroupDto
                    Pagination = x.Pagination
                })
        }

    /// GET /api/v4/groups/:id/projects
    static member ListGroupProjects
        (
            baseUrl: string,
            pat: string,
            groupId: int,
            ?includeSubgroups: bool,
            ?withShared: bool,
            ?orderBy: ProjectSortField,
            ?sort: SortDirection,
            ?page: int,
            ?perPage: int,
            ?search: string
        ) : JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>> =
        promise {
            let baseEndpoint = $"{baseUrl.TrimEnd('/')}/api/v4/groups/{groupId}/projects"

            let queryParams =
                []
                |> Internals.addBool "include_subgroups" includeSubgroups
                |> Internals.addBool "with_shared" withShared
                |> Internals.addSortField orderBy
                |> Internals.addSortDirection sort
                |> Internals.addInt "page" page
                |> Internals.addInt "per_page" perPage
                |> Internals.addString "search" search

            let url = appendQueryParams baseEndpoint queryParams
            return! Internals.sendExploreProjectPage url (Some pat)
        }

    /// Wrapper over ListProjects for "Most starred".
    static member ListExploreMostStarred
        (
            baseUrl: string,
            pat: string,
            ?page: int,
            ?perPage: int,
            ?pagination: PaginationMode,
            ?visibility: string,
            ?topic: string,
            ?search: string
        ) : JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>> =
        GitLabApi.ListProjects(
            baseUrl,
            pat,
            ?page = page,
            ?perPage = perPage,
            ?pagination = pagination,
            orderBy = ProjectSortField.StarCount,
            sort = SortDirection.Desc,
            ?visibility = visibility,
            ?topic = topic,
            ?search = search
        )

    /// UI compatibility helper for sort mapping.
    static member ListProjectsForUiSort
        (
            baseUrl: string,
            pat: string,
            uiSort: UiProjectSort,
            ?direction: SortDirection,
            ?page: int,
            ?perPage: int,
            ?search: string,
            ?visibility: string
        ) : JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>> =
        GitLabApi.ListProjects(
            baseUrl,
            pat,
            ?page = page,
            ?perPage = perPage,
            orderBy = Internals.mapUiSortField uiSort,
            sort = defaultArg direction SortDirection.Desc,
            ?search = search,
            ?visibility = visibility
        )
