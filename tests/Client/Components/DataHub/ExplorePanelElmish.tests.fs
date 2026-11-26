module ExplorePanelElmish

open Fable.Core
open Fable.Mocha
open Arcitisa.Components
open Arcitisa.Components.Api.GitLabApi
open Arcitisa.Components.Page.DataHub
open Arcitisa.Components.Page.DataHub.DataHubTypes

module DataHubBrowserModel = Arcitisa.Components.Page.DataHub.DataHubBrowserModel
module MockData = Arcitisa.Components.Page.MockData.DataHub

let private noopLoadRepos (_: ExploreLoadRequest) = promise { return Ok ExploreLoadResult.empty }

let private reduce msg state =
    DataHubBrowserModel.update noopLoadRepos msg state |> fst

let private mkPageMeta page : PaginationMetadata = {
    Link = None
    NextPage = Some(page + 1)
    Page = Some page
    PerPage = Some 20
    PrevPage = if page > 1 then Some(page - 1) else None
    Total = Some 42
    TotalPages = Some 3
    NextCursor = None
    PrevCursor = None
}

let Main =
    testList "DataHUB ExplorePanel Elmish" [
        testCase "SetTab resets page to first"
        <| fun _ ->
            let initial, _ = DataHubBrowserModel.init None
            let seeded = { initial with Page = 4 }

            let next = reduce (DataHubBrowserModel.SetTab ExploreTab.MostStarred) seeded

            Expect.equal next.Tab ExploreTab.MostStarred "Tab should update to selected tab."
            Expect.equal next.Page 1 "Changing tab should reset pagination to page 1."

        testCase "SubmitSearch promotes draft term and resets page"
        <| fun _ ->
            let initial, _ = DataHubBrowserModel.init None

            let seeded = {
                initial with
                    DraftSearchTerm = "rnaseq"
                    SubmittedSearchTerm = ""
                    Page = 5
            }

            let next = reduce DataHubBrowserModel.SubmitSearch seeded

            Expect.equal next.SubmittedSearchTerm "rnaseq" "Submitted search should use the draft term."
            Expect.equal next.Page 1 "Submitting search should reset pagination to page 1."

        testCase "LoadReposResponse Ok updates repos pagination and groups"
        <| fun _ ->
            let initial, _ = DataHubBrowserModel.init None
            let expectedRepos = MockData.mostStarred |> Array.truncate 2
            let expectedGroups = MockData.groups |> Array.truncate 2

            let loaded: ExploreLoadResult = {
                Repos = expectedRepos
                Pagination = Some(mkPageMeta 2)
                Groups = expectedGroups
                GroupsLoaded = true
                GroupsLoadError = None
            }

            let guid = System.Guid.NewGuid()

            let seeded = {
                initial with
                    LatestReposFetchID = Some guid
                    IsLoading = true
                    Error = GitLabError.Unknown(exn "stale") |> Some
            }

            let next = reduce (DataHubBrowserModel.LoadReposResponse(guid, Ok loaded)) seeded

            Expect.equal next.Repos expectedRepos "Successful response should populate repositories."
            Expect.equal next.Pagination loaded.Pagination "Successful response should set pagination metadata."
            Expect.equal next.Groups expectedGroups "Successful response should update groups."
            Expect.equal next.GroupsLoaded true "Groups loaded flag should be updated from API result."
            Expect.equal next.GroupsLoadError None "Groups load error should be cleared when response has no error."
            Expect.equal next.IsLoading false "Loading should stop after a successful response."
            Expect.equal next.Error None "Top-level error should be cleared on successful response."

        testCase "LoadReposResponse Error sets error and clears list state"
        <| fun _ ->
            let initial, _ = DataHubBrowserModel.init None

            let guid = System.Guid.NewGuid()

            let seeded = {
                initial with
                    LatestReposFetchID = Some guid
                    IsLoading = true
                    Repos = MockData.yourRepos |> Array.truncate 1
                    Pagination = Some(mkPageMeta 1)
            }

            let next =
                reduce (DataHubBrowserModel.LoadReposResponse(guid, Error(GitLabError.Unknown(exn "boom")))) seeded

            Expect.isTrue next.Error.Value.IsUnknown "Error response should be stored in model."

            let errmsg = next.Error.Value.GitLabErrorToString
            Expect.isTrue (errmsg.Contains("boom")) "Error message should be included in error details."

            Expect.equal next.IsLoading false "Loading should stop after an error response."
            Expect.equal next.Repos [||] "Error response should clear repositories."
            Expect.equal next.Pagination None "Error response should clear pagination."

        testCase "YourOrganisations fallback selects first group"
        <| fun _ ->
            let initial, _ = DataHubBrowserModel.init None
            let expectedFirstGroup = MockData.groups[0]
            let guid = System.Guid.NewGuid()

            let seeded = {
                initial with
                    LatestReposFetchID = Some guid
                    Tab = ExploreTab.YourOrganisations
                    SelectedGroupId = None
                    IsLoading = true
            }

            let loaded: ExploreLoadResult = {
                Repos = [||]
                Pagination = None
                Groups = MockData.groups
                GroupsLoaded = true
                GroupsLoadError = None
            }

            let next = reduce (DataHubBrowserModel.LoadReposResponse(guid, Ok loaded)) seeded

            Expect.equal
                next.SelectedGroupId
                (Some expectedFirstGroup.id)
                "When no group is selected in YourOrganisations, first group should be selected automatically."
    ]
