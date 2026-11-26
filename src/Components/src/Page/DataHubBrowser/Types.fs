module Arcitisa.Components.Page.DataHub.DataHubTypes

open Fable.Core
open Arcitisa.Components.Api.GitLabApi

[<RequireQualifiedAccess>]
type ExploreTab =
    | All
    | YourRepos
    | MostStarred
    | YourOrganisations

[<RequireQualifiedAccess>]
type ExploreSortField =
    | LastUpdated
    | DateCreated
    | Name
    | Stars

module ExploreSortField =

    let toProjectSortField =
        function
        | ExploreSortField.LastUpdated -> ProjectSortField.UpdatedAt
        | ExploreSortField.DateCreated -> ProjectSortField.CreatedAt
        | ExploreSortField.Name -> ProjectSortField.Name
        | ExploreSortField.Stars -> ProjectSortField.StarCount

type ExploreLoadRequest = {
    Target: ExploreTab
    SearchTerm: string
    Page: int
    PerPage: int
    SortField: ExploreSortField
    SortDirection: SortDirection
    SelectedGroupId: int option
    IsAuthenticated: bool
}

type ExploreLoadResult = {
    Repos: ExploreProjectDto array
    Pagination: PaginationMetadata option
    Groups: GroupDto array
    GroupsLoaded: bool
    GroupsLoadError: string option
}

module ExploreLoadResult =

    let empty = {
        Repos = [||]
        Pagination = None
        Groups = [||]
        GroupsLoaded = false
        GroupsLoadError = None
    }

type ExploreRepoQuery = {
    SearchTerm: string
    Page: int
    PerPage: int
    OrderBy: ProjectSortField
    Sort: SortDirection
    Visibility: string option
}

type ExploreMostStarredQuery = {
    SearchTerm: string
    Page: int
    PerPage: int
    Visibility: string option
}

type ExploreGroupsQuery = { Page: int; PerPage: int }

type ExploreGroupProjectsQuery = {
    GroupId: int
    SearchTerm: string
    Page: int
    PerPage: int
    OrderBy: ProjectSortField
    Sort: SortDirection
    IncludeSubgroups: bool
    WithShared: bool
}

type ExploreLoaders = {
    LoadAllRepos: ExploreRepoQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    LoadMostStarredRepos: ExploreMostStarredQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    LoadUserRepos: ExploreRepoQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    LoadOrganisationGroups: ExploreGroupsQuery -> JS.Promise<Result<PagedResponse<GroupDto>, GitLabError>>
    LoadOrganisationRepos:
        ExploreGroupProjectsQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
}

/// Information about a successfully completed operation.
type OperationResult = {
    Message: string
    Timestamp: System.DateTime
}

/// Represents the overall connection state of the sidebar.
[<StringEnum; RequireQualifiedAccess>]
type ConnectionState =
    | Disconnected
    | Connecting
    | Connected

/// Represents in-flight operation states.
[<StringEnum; RequireQualifiedAccess>]
type OperationState =
    | Idle
    | Loading
    | Success
    | Error

/// Status of a locally changed file.
[<StringEnum; RequireQualifiedAccess>]
type ChangedFileStatus =
    | [<CompiledName("new")>] New
    | [<CompiledName("changed")>] Changed
    | [<CompiledName("deleted")>] Deleted
    | [<CompiledName("moved")>] Moved

/// A file with local changes that hasn't been saved to DataHUB yet.
type ChangedFile = {
    Path: string
    Status: ChangedFileStatus
    OldPath: string option
}

/// Filter mode for the ARC Browser section.
[<StringEnum; RequireQualifiedAccess>]
type ARCBrowserMode =
    | [<CompiledName("your-arcs")>] YourARCs
    | [<CompiledName("latest")>] Latest
    | [<CompiledName("featured")>] Featured
