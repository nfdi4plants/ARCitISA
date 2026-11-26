module Arcitisa.Components.Page.DataHub.DataHubBrowserModel

open Fable.Core
open Arcitisa.Components
open Arcitisa.Components.Composite.Authentication.Types
open Arcitisa.Components.Api.GitLabApi
open Arcitisa.Components.Page.DataHub.DataHubTypes
open Elmish

type Msg =
    | SetTab of ExploreTab
    | SetDraftSearchTerm of string
    | SubmitSearch
    | SetSortField of ExploreSortField
    | SetSortDirection of SortDirection
    | SetSelectedGroupId of int option
    | SetPage of int
    | LoadReposRequest of ExploreLoadRequest
    | LoadReposResponse of System.Guid * Result<ExploreLoadResult, GitLabError>

type State = {
    /// This field is used to store the id of the latest fetch request for repositories. When a response is received, its id is compared to this field to determine if the response is still relevant (i.e., if it corresponds to the most recent request). This helps prevent race conditions.
    LatestReposFetchID: System.Guid option
    Tab: ExploreTab
    DraftSearchTerm: string
    SubmittedSearchTerm: string
    SortField: ExploreSortField
    SortDirection: SortDirection
    SelectedGroupId: int option
    Groups: GroupDto array
    GroupsLoaded: bool
    GroupsLoadError: string option
    Repos: ExploreProjectDto array
    Page: int
    PerPage: int
    IsLoading: bool
    Error: GitLabError option
    Pagination: PaginationMetadata option
    IsAuthenticated: bool
}

let private buildRequest (state: State) : ExploreLoadRequest = {
    Target = state.Tab
    SearchTerm = state.SubmittedSearchTerm
    Page = state.Page
    PerPage = state.PerPage
    SortField = state.SortField
    SortDirection = state.SortDirection
    SelectedGroupId = state.SelectedGroupId
    IsAuthenticated = state.IsAuthenticated
}

let init (user: AuthUserDto option) =
    let state = {
        LatestReposFetchID = None
        Tab = ExploreTab.All
        DraftSearchTerm = ""
        SubmittedSearchTerm = ""
        SortField = ExploreSortField.LastUpdated
        SortDirection = SortDirection.Desc
        SelectedGroupId = None
        Groups = [||]
        GroupsLoaded = false
        GroupsLoadError = None
        Repos = [||]
        Page = 1
        PerPage = 20
        IsLoading = false
        Error = None
        Pagination = None
        IsAuthenticated = user.IsSome
    }

    state, Cmd.ofMsg (LoadReposRequest(buildRequest state))

/// Wrapper for calling LoadReposRequest after updating the state.
let private reloadWith (state: State) =
    state, Cmd.ofMsg (LoadReposRequest(buildRequest state))

let update (loadRepos: ExploreLoadRequest -> JS.Promise<Result<ExploreLoadResult, GitLabError>>) msg state =
    match msg with
    | SetTab tab ->
        if tab = state.Tab then
            state, Cmd.none
        else
            reloadWith { state with Tab = tab; Page = 1 }
    | SetDraftSearchTerm term ->
        {
            state with
                DraftSearchTerm = term
                Page = 1
        },
        Cmd.none
    | SubmitSearch ->
        let nextState = {
            state with
                SubmittedSearchTerm = state.DraftSearchTerm
                Page = 1
        }

        reloadWith nextState
    | SetSortField sortField ->
        if sortField = state.SortField then
            state, Cmd.none
        else
            reloadWith {
                state with
                    SortField = sortField
                    Page = 1
            }
    | SetSortDirection sortDirection ->
        if sortDirection = state.SortDirection then
            state, Cmd.none
        else
            reloadWith {
                state with
                    SortDirection = sortDirection
                    Page = 1
            }
    | SetSelectedGroupId selectedGroupId ->
        if selectedGroupId = state.SelectedGroupId then
            state, Cmd.none
        else
            reloadWith {
                state with
                    SelectedGroupId = selectedGroupId
                    Page = 1
            }
    | SetPage page ->
        if page = state.Page then
            state, Cmd.none
        else
            reloadWith { state with Page = page }
    | LoadReposRequest request ->
        let requestId = System.Guid.NewGuid()

        let nextState = {
            state with
                LatestReposFetchID = Some requestId
                IsLoading = true
                Error = None
        }

        let cmd =
            Cmd.OfPromise.either
                loadRepos
                request
                (function
                | Ok result -> LoadReposResponse(requestId, Ok result)
                | Error err -> LoadReposResponse(requestId, Error err))
                (fun err -> LoadReposResponse(requestId, Error(GitLabError.Unknown err)))

        nextState, cmd

    | LoadReposResponse(guid, result) ->
        match result with
        | Ok _ when state.LatestReposFetchID <> Some guid ->
            // This response is outdated, ignore it
            state, Cmd.none
        | Ok response ->
            let updatedState = {
                state with
                    LatestReposFetchID = None
                    Repos = response.Repos
                    Pagination = response.Pagination
                    Groups = response.Groups
                    GroupsLoaded = response.GroupsLoaded
                    GroupsLoadError = response.GroupsLoadError
                    IsLoading = false
                    Error = None
            }

            if
                state.Tab = ExploreTab.YourOrganisations
                && state.SelectedGroupId.IsNone
                && response.Groups.Length > 0
            then
                let nextState = {
                    updatedState with
                        SelectedGroupId = Some response.Groups[0].id
                }

                reloadWith nextState
            else
                updatedState, Cmd.none
        | Error err ->
            {
                state with
                    LatestReposFetchID =
                        if state.LatestReposFetchID = Some guid then
                            None
                        else
                            state.LatestReposFetchID
                    Error = Some err
                    Repos = [||]
                    Pagination = None
                    IsLoading = false
            },
            Cmd.none
