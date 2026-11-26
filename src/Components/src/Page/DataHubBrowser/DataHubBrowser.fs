namespace Arcitisa.Components.Page.DataHub

open Fable.Core
open Elmish
open Feliz
open Feliz.UseElmish
open Arcitisa.Components
open Arcitisa.Components.Composite.Authentication
open Arcitisa.Components.Composite.Authentication.Types
open Arcitisa.Components.Composite.Actionbar.Types
open Arcitisa.Components.Page.DataHub.DataHubTypes
open Arcitisa.Components.Api.GitLabApi
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.Buttons
open Arcitisa.Components.Page.MockData.DataHub
open Arcitisa.Components.Page.DataHubBrowser.sub

[<Erase; Mangle(false)>]
type DataHubBrowser =

    [<ReactComponent>]
    static member SectionHeading(text: string) =
        Html.h3 [
            prop.className "swt:text-sm swt:font-semibold swt:text-base-content/70 swt:uppercase swt:tracking-wide"
            prop.text text
        ]

    [<ReactComponent>]
    static member private Filter
        (
            tab: ExploreTab,
            isAuthenticated: bool,
            searchTerm: string,
            setSearchTerm: string -> unit,
            onSearchSubmit: unit -> unit,
            sortField: ExploreSortField,
            setSortField: ExploreSortField -> unit,
            sortDirection: SortDirection,
            setSortDirection: SortDirection -> unit,
            groups: GroupDto array,
            selectedGroupId: int option,
            setSelectedGroupId: int option -> unit,
            groupsLoadError: string option
        ) =

        let sortLabel =
            match sortDirection with
            | SortDirection.Asc -> "Sort ascending"
            | SortDirection.Desc -> "Sort descending"

        Html.div [
            prop.className
                "swt:flex swt:flex-col swt:sm:flex-row sm:swt:flex-row swt:gap-2 swt:px-2 swt:py-6 swt:bg-base-300 swt:border-y swt:border-base-content/50"
            prop.children [
                Html.div [
                    prop.className "swt:join swt:grow"
                    prop.children [
                        Html.input [
                            prop.testId "GitLabExploreSearchInput"
                            prop.className
                                "swt:input swt:input-sm swt:input-bordered swt:border-base-content swt:w-full swt:join-item"
                            prop.placeholder "Search repositories"
                            prop.value searchTerm
                            prop.onChange setSearchTerm
                            prop.onKeyDown (key.enter, (fun _ -> onSearchSubmit ()))
                        ]
                        Html.button [
                            prop.testId "GitLabExploreSearchButton"
                            prop.className "swt:btn swt:btn-sm swt:btn-outline swt:join-item"
                            prop.text "Search"
                            prop.onClick (fun _ -> onSearchSubmit ())
                        ]
                    ]
                ]
                if tab <> ExploreTab.MostStarred then
                    Html.div [
                        prop.className "swt:join"
                        prop.children [
                            Html.select [
                                prop.testId "GitLabExploreSortFieldSelect"
                                prop.className
                                    "swt:select swt:select-sm swt:btn swt:join-item swt:max-sm:w-full swt:*:min-w-max"
                                prop.value (
                                    match sortField with
                                    | ExploreSortField.LastUpdated -> "last-updated"
                                    | ExploreSortField.DateCreated -> "date-created"
                                    | ExploreSortField.Name -> "name"
                                    | ExploreSortField.Stars -> "stars"
                                )
                                prop.onChange (fun (value: string) ->
                                    let next =
                                        match value with
                                        | "last-updated" -> ExploreSortField.LastUpdated
                                        | "date-created" -> ExploreSortField.DateCreated
                                        | "name" -> ExploreSortField.Name
                                        | "stars" -> ExploreSortField.Stars
                                        | _ -> ExploreSortField.LastUpdated

                                    setSortField next
                                )
                                prop.children [
                                    Html.option [ prop.value "last-updated"; prop.text "Last Updated" ]
                                    Html.option [ prop.value "date-created"; prop.text "Date Created" ]
                                    Html.option [ prop.value "name"; prop.text "Name" ]
                                    Html.option [ prop.value "stars"; prop.text "Stars" ]
                                ]
                            ]
                            Html.button [
                                prop.testId "GitLabExploreSortDirectionButton"
                                prop.className
                                    "swt:btn swt:btn-sm swt:btn-outline swt:join-item swt:bg-base-100 swt:border-base-content/30!"
                                prop.ariaLabel sortLabel
                                prop.title sortLabel
                                prop.onClick (fun _ ->
                                    let nextDirection =
                                        match sortDirection with
                                        | SortDirection.Asc -> SortDirection.Desc
                                        | SortDirection.Desc -> SortDirection.Asc

                                    setSortDirection nextDirection
                                )
                                prop.children [
                                    Html.i [
                                        prop.className [
                                            "swt:iconify"
                                            match sortDirection with
                                            | SortDirection.Asc ->
                                                "swt:fluent--arrow-sort-up-lines-16-regular swt:size-4"
                                            | SortDirection.Desc ->
                                                "swt:fluent--arrow-sort-down-lines-16-regular swt:size-4"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                if tab = ExploreTab.YourOrganisations then
                    Html.select [
                        prop.testId "GitLabExploreOrganisationSelect"
                        prop.className "swt:select swt:select-sm swt:min-w-56"
                        prop.value (selectedGroupId |> Option.map string |> Option.defaultValue "")
                        prop.onChange (fun (value: string) ->
                            if System.String.IsNullOrWhiteSpace value then
                                setSelectedGroupId None
                            else
                                setSelectedGroupId (Some(int value))
                        )
                        prop.disabled ((not isAuthenticated) || groups.Length = 0 || groupsLoadError.IsSome)
                        prop.children [
                            Html.option [
                                prop.value ""
                                prop.text "Select group"
                                prop.disabled true
                            ]
                            for g in groups do
                                Html.option [ prop.value (string g.id); prop.text g.name ]
                        ]
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private Tabs(tab, setTab, isAuthenticated) =
        let tooltipIdPrefix = React.useId ()

        Html.div [
            prop.testId "GitLabExploreTabs"
            prop.role.tabList
            prop.className "swt:tabs swt:tabs-box swt:tabs-sm"
            prop.children [
                let tabs = [
                    ExploreTab.All, "All", "All"
                    ExploreTab.YourRepos, "Your Repos", "YourRepos"
                    ExploreTab.MostStarred, "Most Starred", "MostStarred"
                    ExploreTab.YourOrganisations, "Your Groups", "YourOrganisations"
                ]

                for mode, label, tid in tabs do
                    let isRestricted =
                        (not isAuthenticated)
                        && (mode = ExploreTab.YourRepos || mode = ExploreTab.YourOrganisations)

                    let tooltipId = $"{tooltipIdPrefix}-{tid}-tooltip"

                    // daisyUI styles a tab only when it is a direct child of the tabs container,
                    // so the tooltip classes go on the tab itself.
                    Html.a [
                        prop.testId ("GitLabExploreTab-" + tid)
                        prop.tabIndex 0
                        // The label keeps the tooltip text out of the accessible name of the tab.
                        // A restricted tab gets the tooltip as its description instead.
                        prop.ariaLabel label
                        if isRestricted then
                            prop.ariaDescribedBy tooltipId
                        prop.custom ("aria-disabled", if isRestricted then "true" else "false")
                        prop.className [
                            "swt:tab"
                            if tab = mode then
                                "swt:tab-active"
                            if isRestricted then
                                // Opacity would fade the tooltip inside as well, so only the text color fades.
                                "swt:tooltip swt:tooltip-bottom swt:text-base-content/50 swt:cursor-not-allowed"
                        ]
                        prop.onClick (fun _ ->
                            if not isRestricted then
                                setTab mode
                        )
                        prop.children [
                            if isRestricted then
                                Html.div [
                                    prop.id tooltipId
                                    prop.className "swt:tooltip-content"
                                    prop.text "Log in to access this tab"
                                ]
                            Html.text label
                        ]
                    ]
            ]
        ]

    [<ReactComponentAttribute>]
    static member private Pagination(pagination, onPageChange) =
        match pagination with
        | Some pageMeta ->
            Html.div [
                prop.testId "GitLabExplorePagination"
                prop.className "swt:flex swt:items-center swt:justify-center swt:gap-6"
                prop.children [
                    Html.button [
                        prop.testId "GitLabExploreFirstPageButton"
                        prop.className "swt:btn swt:btn-xs"
                        prop.text "First"
                        prop.disabled (pageMeta.PrevPage.IsNone || pageMeta.Page = Some 1)
                        prop.onClick (fun _ -> Some 1 |> Option.iter onPageChange)
                    ]
                    Html.button [
                        prop.testId "GitLabExplorePrevPageButton"
                        prop.className "swt:btn swt:btn-xs"
                        prop.text "Prev"
                        prop.disabled pageMeta.PrevPage.IsNone
                        prop.onClick (fun _ -> pageMeta.PrevPage |> Option.iter onPageChange)
                    ]
                    Html.span [
                        prop.testId "GitLabExplorePageIndicator"
                        prop.className "swt:text-xs swt:text-base-content/70"
                        prop.textf "Page %s" (pageMeta.Page |> Option.map string |> Option.defaultValue "1")
                    ]
                    Html.button [
                        prop.testId "GitLabExploreNextPageButton"
                        prop.className "swt:btn swt:btn-xs"
                        prop.text "Next"
                        prop.disabled pageMeta.NextPage.IsNone
                        prop.onClick (fun _ -> pageMeta.NextPage |> Option.iter onPageChange)
                    ]
                    Html.button [
                        prop.testId "GitLabExploreLastPageButton"
                        prop.className "swt:btn swt:btn-xs"
                        prop.text (string pageMeta.TotalPages)
                        prop.disabled (pageMeta.TotalPages.IsNone || pageMeta.Page = pageMeta.TotalPages)
                        prop.onClick (fun _ -> pageMeta.TotalPages |> Option.iter onPageChange)
                    ]
                ]
            ]
        | None -> Html.none

    [<ReactComponent>]
    static member ExplorePanel
        (
            accounts: Arcitisa.Components.Composite.Authentication.Types.AuthStateDto,
            loaders: ExploreLoaders,
            ?reloadTrigger: int,
            ?onRender: (ExploreProjectDto -> unit),
            ?projectActionBtns: (ExploreProjectDto -> ButtonInfo[]),
            ?classNames: string,
            ?onClose: Browser.Types.MouseEvent -> unit
        ) =

        let user = accounts.UsableActiveUser()

        let emptyPagination (page: int) (perPage: int) : PaginationMetadata = {
            Link = None
            NextPage = None
            Page = Some page
            PerPage = Some perPage
            PrevPage = None
            Total = Some 0
            TotalPages = Some 1
            NextCursor = None
            PrevCursor = None
        }

        let emptyProjectsResponse (page: int) (perPage: int) : PagedResponse<ExploreProjectDto> = {
            Items = [||]
            Pagination = emptyPagination page perPage
        }

        let emptyGroupsResponse (page: int) (perPage: int) : PagedResponse<GroupDto> = {
            Items = [||]
            Pagination = emptyPagination page perPage
        }

        let loadRepos (request: ExploreLoadRequest) = promise {
            let visibility = if request.IsAuthenticated then None else Some "public"

            let orderBy =
                if request.Target = ExploreTab.MostStarred then
                    ProjectSortField.StarCount
                else
                    ExploreSortField.toProjectSortField request.SortField

            let sort =
                if request.Target = ExploreTab.MostStarred then
                    SortDirection.Desc
                else
                    request.SortDirection

            let repoQuery = {
                SearchTerm = request.SearchTerm
                Page = request.Page
                PerPage = request.PerPage
                OrderBy = orderBy
                Sort = sort
                Visibility = visibility
            }

            let! groupsResult =
                if request.Target = ExploreTab.YourOrganisations && request.IsAuthenticated then
                    loaders.LoadOrganisationGroups { Page = 1; PerPage = 100 }
                else
                    promise { return Ok(emptyGroupsResponse 1 100) }

            let groups, groupsLoaded, groupsLoadError =
                match groupsResult with
                | Ok g -> g.Items, (request.Target <> ExploreTab.YourOrganisations) || request.IsAuthenticated, None
                | Error _ -> [||], true, Some "Failed to load groups"

            let selectedGroupId =
                match request.SelectedGroupId with
                | Some gid -> Some gid
                | None when groups.Length > 0 -> Some groups[0].id
                | None -> None

            let! result =
                match request.Target with
                | ExploreTab.All -> loaders.LoadAllRepos repoQuery
                | ExploreTab.YourRepos ->
                    if request.IsAuthenticated then
                        loaders.LoadUserRepos repoQuery
                    else
                        promise { return Ok(emptyProjectsResponse request.Page request.PerPage) }
                | ExploreTab.MostStarred ->
                    loaders.LoadMostStarredRepos {
                        SearchTerm = request.SearchTerm
                        Page = request.Page
                        PerPage = request.PerPage
                        Visibility = visibility
                    }
                | ExploreTab.YourOrganisations ->
                    if request.IsAuthenticated then
                        match selectedGroupId with
                        | Some gid ->
                            loaders.LoadOrganisationRepos {
                                GroupId = gid
                                SearchTerm = request.SearchTerm
                                Page = request.Page
                                PerPage = request.PerPage
                                OrderBy = orderBy
                                Sort = sort
                                IncludeSubgroups = true
                                WithShared = true
                            }
                        | None -> promise { return Ok(emptyProjectsResponse request.Page request.PerPage) }
                    else
                        promise { return Ok(emptyProjectsResponse request.Page request.PerPage) }

            match result with
            | Ok okResult ->
                return
                    Ok {
                        Repos = okResult.Items
                        Pagination = Some okResult.Pagination
                        Groups = groups
                        GroupsLoaded = groupsLoaded
                        GroupsLoadError = groupsLoadError
                    }
            | Error err -> return Error(err)
        }

        let model, dispatch =
            React.useElmish (
                (fun () -> DataHubBrowserModel.init user),
                DataHubBrowserModel.update loadRepos,
                [| box user; box reloadTrigger |]
            )

        React.useEffect (fun () ->
            match onRender with
            | Some fn -> model.Repos |> Array.iter fn
            | None -> ()
        )

        let onSearchSubmit () =
            dispatch DataHubBrowserModel.SubmitSearch

        Html.div [
            prop.testId "GitLabExplorePanel"
            prop.className [
                classNames
                |> Option.defaultValue
                    "swt:flex swt:flex-col swt:gap-2 swt:p-2 swt:grow swt:overflow-hidden swt:h-full swt:w-full swt:overflow-y-hidden"
            ]
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:items-center swt:gap-2"
                    prop.children [
                        DataHubBrowser.SectionHeading("Download ARC from DataHUB")
                        match onClose with
                        | Some closeFn ->
                            Html.div [
                                prop.className "swt:ml-auto"
                                prop.children [ Buttons.DeleteButton(props = [ prop.onClick closeFn ]) ]
                            ]
                        | None -> Html.none
                    ]
                ]
                // tabs
                DataHubBrowser.Tabs(
                    model.Tab,
                    (fun next -> dispatch (DataHubBrowserModel.SetTab next)),
                    model.IsAuthenticated
                )
                // filter
                DataHubBrowser.Filter(
                    model.Tab,
                    model.IsAuthenticated,
                    model.DraftSearchTerm,
                    (fun term -> dispatch (DataHubBrowserModel.SetDraftSearchTerm term)),
                    onSearchSubmit,
                    model.SortField,
                    (fun next -> dispatch (DataHubBrowserModel.SetSortField next)),
                    model.SortDirection,
                    (fun next -> dispatch (DataHubBrowserModel.SetSortDirection next)),
                    model.Groups,
                    model.SelectedGroupId,
                    (fun gid -> dispatch (DataHubBrowserModel.SetSelectedGroupId gid)),
                    model.GroupsLoadError
                )
                match model with
                | { IsLoading = true } ->
                    Html.div [
                        prop.className "swt:grow"
                        prop.children [
                            Primitive.LoadingSpinner.LoadingSpinner.LoadingSpinner(
                                size = DaisyuiSize.XL,
                                text = "Loading repositories..."
                            )
                        ]
                    ]
                | { Error = Some err } ->
                    Html.div [
                        prop.testId "GitLabExploreError"
                        prop.className "swt:alert swt:alert-error swt:text-xs"
                        prop.children [ Html.span err.GitLabErrorToString ]
                    ]
                | {
                      Tab = ExploreTab.YourOrganisations
                      IsAuthenticated = true
                      GroupsLoaded = true
                      Groups = groups
                      GroupsLoadError = None
                  } when groups.Length = 0 ->

                    let showOrgLoadError =
                        model.Tab = ExploreTab.YourOrganisations && model.GroupsLoadError.IsSome

                    if showOrgLoadError then
                        Html.p [
                            prop.testId "GitLabExploreGroupsError"
                            prop.className "swt:text-sm swt:text-error"
                            prop.text "Failed to load groups"
                        ]
                    else
                        Html.p [
                            prop.testId "GitLabExploreGroupsEmpty"
                            prop.className "swt:text-sm swt:text-base-content/60"
                            prop.text "No groups found"
                        ]
                | { IsLoading = false; Repos = [||] } ->
                    Html.p [
                        prop.testId "GitLabExploreEmpty"
                        prop.className "swt:text-sm swt:text-base-content/60"
                        prop.text "No repositories found for current filter and search."
                    ]
                | { IsLoading = false; Repos = repos } ->
                    Html.ul [
                        prop.testId "GitLabExploreRepoList"
                        prop.className "swt:list swt:bg-base-100 swt:grow swt:overflow-y-auto swt:h-full"
                        prop.children [
                            for repo in repos do
                                RepoListRow.RepoListRow(repo, ?extraButtons = projectActionBtns)
                        ]
                    ]

                // Pagination
                DataHubBrowser.Pagination(model.Pagination, (fun page -> dispatch (DataHubBrowserModel.SetPage page)))
            ]
        ]

    [<ReactComponent(true)>]
    static member Entry() =
        let accounts, setAccounts = React.useState (AuthStateDto.Empty: AuthStateDto)

        let mockUser: AccountSummary = {
            User = {
                Id = 1
                LocalArcitisaAccountId = "1"
                Username = "storybook-user"
                Name = "storybook-user"
                Email = "Storybook User"
                CommitEmail = None
                AvatarUrl = ""
                TargetDataHub = ""
            }
            DateAdded = "2026-01-01T00:00:00.0000000Z"
            TokenStatus = TokenStatus.Ok
            TokenExpiresOn = None
        }

        let login () =
            setAccounts (
                {
                    AuthStateDto.Empty with
                        ActiveAccount = Some mockUser
                        StoredAccounts = [| mockUser |]
                }
            )

        let logout () = setAccounts AuthStateDto.Empty

        let reloadTrigger = 0

        let callCount, setCallCount =
            React.useState (
                {|
                    all = 0
                    mostStarred = 0
                    userRepos = 0
                    organisationGroups = 0
                    organisationRepos = 0
                |}
            )

        let isLocallyCloned (p: ExploreProjectDto) = p.id % 2 = 0

        let paginate (items: 'T array) (page: int) (pageSize: int) =
            let totalPages =
                let f = float items.Length / float pageSize
                let ceilV = System.Math.Ceiling f |> int
                max 1 ceilV

            let p = max 1 (min page totalPages)
            let startIndex = (p - 1) * pageSize
            let pageItems = items |> Array.skip startIndex |> Array.truncate pageSize

            let pageMeta: PaginationMetadata = {
                Link = None
                NextPage = if p < totalPages then Some(p + 1) else None
                Page = Some p
                PerPage = Some pageSize
                PrevPage = if p > 1 then Some(p - 1) else None
                Total = Some items.Length
                TotalPages = Some totalPages
                NextCursor = None
                PrevCursor = None
            }

            pageItems, pageMeta

        let containsCi (text: string) (query: string) =
            text.ToLowerInvariant().Contains(query.ToLowerInvariant())

        let sortRepos (sortField: ExploreSortField) (sortDirection: SortDirection) (items: ExploreProjectDto array) =
            let sorted =
                match sortField with
                | ExploreSortField.LastUpdated -> items |> Array.sortBy (fun p -> p.last_activity_at)
                | ExploreSortField.DateCreated -> items |> Array.sortBy (fun p -> p.created_at)
                | ExploreSortField.Name -> items |> Array.sortBy (fun p -> p.name.ToLowerInvariant())
                | ExploreSortField.Stars -> items |> Array.sortBy (fun p -> p.star_count)

            match sortDirection with
            | SortDirection.Asc -> sorted
            | SortDirection.Desc -> sorted |> Array.rev

        let allRepos =
            [|
                yield! yourRepos
                yield! mostStarred
                for kvp in orgRepos do
                    yield! kvp.Value
            |]
            |> Array.distinctBy (fun p -> p.id)

        let isPublicRepo (project: ExploreProjectDto) =
            not (project.path_with_namespace.Contains("/private/"))

        let sortReposByProjectSort
            (sortField: ProjectSortField)
            (sortDirection: SortDirection)
            (items: ExploreProjectDto array)
            =
            let sorted =
                match sortField with
                | ProjectSortField.Name -> items |> Array.sortBy (fun p -> p.name.ToLowerInvariant())
                | ProjectSortField.CreatedAt -> items |> Array.sortBy (fun p -> p.created_at)
                | ProjectSortField.UpdatedAt -> items |> Array.sortBy (fun p -> p.last_activity_at)
                | ProjectSortField.StarCount -> items |> Array.sortBy (fun p -> p.star_count)
                | ProjectSortField.LastActivityAt -> items |> Array.sortBy (fun p -> p.last_activity_at)

            match sortDirection with
            | SortDirection.Asc -> sorted
            | SortDirection.Desc -> sorted |> Array.rev

        let filterRepos (searchTerm: string) (items: ExploreProjectDto array) =
            items
            |> Array.filter (fun p ->
                System.String.IsNullOrWhiteSpace searchTerm
                || containsCi p.name searchTerm
                || containsCi p.path_with_namespace searchTerm
                || containsCi (p.description |> Option.defaultValue "") searchTerm
            )

        /// This is used to test race conditions by including specific tokens in the search term that trigger different response delays
        let getMockDelayAndSearchTerm (searchTerm: string) =
            let slowToken = "__race_slow__"
            let fastToken = "__race_fast__"

            let delayMs =
                if searchTerm.Contains(slowToken) then 900
                elif searchTerm.Contains(fastToken) then 60
                else 250

            let cleanedSearchTerm =
                searchTerm.Replace(slowToken, "").Replace(fastToken, "").Trim()

            delayMs, cleanedSearchTerm

        let loadAllRepos (query: ExploreRepoQuery) = promise {
            let delayMs, cleanedSearchTerm = getMockDelayAndSearchTerm query.SearchTerm

            setCallCount {|
                callCount with
                    all = callCount.all + 1
            |}

            do! Promise.sleep delayMs

            let source =
                match query.Visibility with
                | Some "public" -> allRepos |> Array.filter isPublicRepo
                | _ -> allRepos

            let filtered =
                source
                |> filterRepos cleanedSearchTerm
                |> sortReposByProjectSort query.OrderBy query.Sort

            let pageItems, pageMeta = paginate filtered query.Page query.PerPage

            return
                Ok {
                    Items = pageItems
                    Pagination = pageMeta
                }
        }

        let loadMostStarredRepos (query: ExploreMostStarredQuery) = promise {
            let delayMs, cleanedSearchTerm = getMockDelayAndSearchTerm query.SearchTerm

            setCallCount {|
                callCount with
                    mostStarred = callCount.mostStarred + 1
            |}

            do! Promise.sleep delayMs

            let source =
                match query.Visibility with
                | Some "public" -> mostStarred |> Array.filter isPublicRepo
                | _ -> mostStarred

            let filtered =
                source
                |> filterRepos cleanedSearchTerm
                |> Array.sortByDescending (fun p -> p.star_count)

            let pageItems, pageMeta = paginate filtered query.Page query.PerPage

            return
                Ok {
                    Items = pageItems
                    Pagination = pageMeta
                }
        }

        let loadUserRepos (query: ExploreRepoQuery) = promise {
            let delayMs, cleanedSearchTerm = getMockDelayAndSearchTerm query.SearchTerm

            setCallCount {|
                callCount with
                    userRepos = callCount.userRepos + 1
            |}

            do! Promise.sleep delayMs

            let filtered =
                yourRepos
                |> filterRepos cleanedSearchTerm
                |> sortReposByProjectSort query.OrderBy query.Sort

            let pageItems, pageMeta = paginate filtered query.Page query.PerPage

            return
                Ok {
                    Items = pageItems
                    Pagination = pageMeta
                }
        }

        let loadOrganisationGroups (query: ExploreGroupsQuery) = promise {
            setCallCount {|
                callCount with
                    organisationGroups = callCount.organisationGroups + 1
            |}

            do! Promise.sleep 250

            let pageItems, pageMeta = paginate groups query.Page query.PerPage

            return
                Ok {
                    Items = pageItems
                    Pagination = pageMeta
                }
        }

        let loadOrganisationRepos (query: ExploreGroupProjectsQuery) = promise {
            let delayMs, cleanedSearchTerm = getMockDelayAndSearchTerm query.SearchTerm

            setCallCount {|
                callCount with
                    organisationRepos = callCount.organisationRepos + 1
            |}

            do! Promise.sleep delayMs

            let source = orgRepos |> Map.tryFind query.GroupId |> Option.defaultValue [||]

            let filtered =
                source
                |> filterRepos cleanedSearchTerm
                |> sortReposByProjectSort query.OrderBy query.Sort

            let pageItems, pageMeta = paginate filtered query.Page query.PerPage

            return
                Ok {
                    Items = pageItems
                    Pagination = pageMeta
                }
        }

        let loaders: ExploreLoaders = {
            LoadAllRepos = loadAllRepos
            LoadMostStarredRepos = loadMostStarredRepos
            LoadUserRepos = loadUserRepos
            LoadOrganisationGroups = loadOrganisationGroups
            LoadOrganisationRepos = loadOrganisationRepos
        }

        let currentUser = accounts.ActiveUser()

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.div [
                    prop.testId "GitLabExploreMockAuthControls"
                    prop.className "swt:flex swt:items-center swt:gap-2 swt:flex-wrap swt:text-xs"
                    prop.children [
                        Html.span [
                            prop.testId "GitLabExploreMockAuthState"
                            prop.textf "signed-in:%s" (if currentUser.IsSome then "true" else "false")
                        ]
                        Html.button [
                            prop.testId "GitLabExploreMockLoginButton"
                            prop.className "swt:btn swt:btn-xs"
                            prop.text "Login"
                            prop.disabled currentUser.IsSome
                            prop.onClick (fun _ -> login ())
                        ]
                        Html.button [
                            prop.testId "GitLabExploreMockLogoutButton"
                            prop.className "swt:btn swt:btn-xs swt:btn-outline"
                            prop.text "Logout"
                            prop.disabled currentUser.IsNone
                            prop.onClick (fun _ -> logout ())
                        ]
                    ]
                ]
                Html.div [
                    prop.testId "GitLabExploreMockCallCounter"
                    prop.className "swt:flex swt:gap-2 swt:flex-wrap swt:text-xs swt:text-base-content/70"
                    prop.children [
                        Html.span [
                            prop.testId "GitLabExploreMockCountAll"
                            prop.textf "all:%i" callCount.all
                        ]
                        Html.span [
                            prop.testId "GitLabExploreMockCountMostStarred"
                            prop.textf "most-starred:%i" callCount.mostStarred
                        ]
                        Html.span [
                            prop.testId "GitLabExploreMockCountUserRepos"
                            prop.textf "user-repos:%i" callCount.userRepos
                        ]
                        Html.span [
                            prop.testId "GitLabExploreMockCountOrgGroups"
                            prop.textf "org-groups:%i" callCount.organisationGroups
                        ]
                        Html.span [
                            prop.testId "GitLabExploreMockCountOrgRepos"
                            prop.textf "org-repos:%i" callCount.organisationRepos
                        ]
                    ]
                ]
                DataHubBrowser.ExplorePanel(
                    accounts,
                    loaders,
                    reloadTrigger,
                    projectActionBtns =
                        (fun p -> [|
                            ButtonInfo.create (
                                "swt:fluent--arrow-download-24-regular swt:size-5",
                                "Clone repository",
                                (fun _ -> Browser.Dom.window.alert ("Clone " + p.web_url))
                            )
                        |])
                )
            ]
        ]

    [<ReactComponent>]
    static member GitLabEntry() =
        let accounts, setAccounts = React.useState (AuthStateDto.Empty)
        let currentUser = accounts.ActiveUser()
        let baseUrl, setBaseUrl = React.useState "https://git.nfdi4plants.org"
        let pat, setPat = React.useState ""
        let reloadTrigger, setReloadTrigger = React.useState 0

        let isConnected = not (System.String.IsNullOrWhiteSpace baseUrl)

        let emptyPagedResponse (page: int) (perPage: int) : PagedResponse<'T> = {
            Items = [||]
            Pagination = {
                Link = None
                NextPage = None
                Page = Some page
                PerPage = Some perPage
                PrevPage = None
                Total = Some 0
                TotalPages = Some 1
                NextCursor = None
                PrevCursor = None
            }
        }

        let login =
            fun (pat: string) -> promise {
                let! userResponse = GitLabApi.GetCurrentUser(baseUrl, pat)

                match userResponse with
                | Ok user ->
                    let userDTO: AccountSummary = {
                        User = {
                            Id = user.id
                            LocalArcitisaAccountId = string user.id
                            Username = user.username
                            Name = user.name
                            AvatarUrl = user.avatar_url |> Option.defaultValue ""
                            Email = user.email
                            CommitEmail = user.commit_email
                            TargetDataHub = baseUrl
                        }
                        DateAdded = "2026-01-01T00:00:00.0000000Z"
                        TokenStatus = TokenStatus.Ok
                        TokenExpiresOn = None
                    }

                    setAccounts {
                        ActiveAccount = Some userDTO
                        StoredAccounts = [| userDTO |]
                    }
                | Error err ->
                    Browser.Dom.console.error (
                        "Failed to get current user: "
                        + (
                            match err with
                            | GitLabError.DecodeError ex -> ex.Message
                            | _ -> ""
                        )
                    )

                    setAccounts AuthStateDto.Empty
            }

        let loadAllRepos (query: ExploreRepoQuery) =
            if isConnected then
                let requestPat = if query.Visibility.IsSome then "" else pat

                GitLabApi.ListProjects(
                    baseUrl,
                    requestPat,
                    page = query.Page,
                    perPage = query.PerPage,
                    search = query.SearchTerm,
                    orderBy = query.OrderBy,
                    sort = query.Sort,
                    ?visibility = query.Visibility
                )
            else
                promise { return Ok(emptyPagedResponse query.Page query.PerPage) }

        let loadMostStarredRepos (query: ExploreMostStarredQuery) =
            if isConnected then
                let requestPat = if query.Visibility.IsSome then "" else pat

                GitLabApi.ListExploreMostStarred(
                    baseUrl,
                    requestPat,
                    page = query.Page,
                    perPage = query.PerPage,
                    search = query.SearchTerm,
                    ?visibility = query.Visibility
                )
            else
                promise { return Ok(emptyPagedResponse query.Page query.PerPage) }

        let loadUserRepos (query: ExploreRepoQuery) =
            if isConnected then
                GitLabApi.ListUserPersonalProjects(
                    baseUrl,
                    pat,
                    ?userId = (currentUser |> Option.map _.Id),
                    page = query.Page,
                    perPage = query.PerPage,
                    search = query.SearchTerm,
                    orderBy = query.OrderBy,
                    sort = query.Sort
                )
            else
                promise { return Ok(emptyPagedResponse query.Page query.PerPage) }

        let loadOrganisationGroups (query: ExploreGroupsQuery) =
            if isConnected then
                GitLabApi.ListGroupsForCurrentUser(baseUrl, pat, page = query.Page, perPage = query.PerPage)
            else
                promise { return Ok(emptyPagedResponse query.Page query.PerPage) }

        let loadOrganisationRepos (query: ExploreGroupProjectsQuery) =
            if isConnected then
                GitLabApi.ListGroupProjects(
                    baseUrl,
                    pat,
                    query.GroupId,
                    page = query.Page,
                    perPage = query.PerPage,
                    includeSubgroups = query.IncludeSubgroups,
                    withShared = query.WithShared,
                    search = query.SearchTerm,
                    orderBy = query.OrderBy,
                    sort = query.Sort
                )
            else
                promise { return Ok(emptyPagedResponse query.Page query.PerPage) }

        let loaders: ExploreLoaders = {
            LoadAllRepos = loadAllRepos
            LoadMostStarredRepos = loadMostStarredRepos
            LoadUserRepos = loadUserRepos
            LoadOrganisationGroups = loadOrganisationGroups
            LoadOrganisationRepos = loadOrganisationRepos
        }

        Html.div [
            prop.testId "GitLabEntryPanel"
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-col sm:swt:flex-row swt:gap-2"
                    prop.children [
                        Html.input [
                            prop.testId "GitLabEntryBaseUrlInput"
                            prop.className "swt:input swt:input-sm swt:w-full"
                            prop.placeholder "GitLab Base URL"
                            prop.value baseUrl
                            prop.onChange setBaseUrl
                        ]
                        Html.div [
                            prop.className "swt:join swt:w-full"
                            prop.children [
                                Html.input [
                                    prop.type'.password
                                    prop.testId "GitLabEntryPatInput"
                                    prop.className "swt:input swt:input-sm swt:w-full swt:join-item"
                                    prop.placeholder "Personal Access Token"
                                    prop.value pat
                                    prop.onChange setPat
                                ]
                                Html.button [
                                    prop.className "swt:btn swt:btn-sm swt:join-item"
                                    prop.text "Login"
                                    prop.disabled (not isConnected || System.String.IsNullOrWhiteSpace pat)
                                    prop.onClick (fun _ -> login pat |> Promise.start)
                                ]
                            ]
                        ]
                        Html.button [
                            prop.testId "GitLabEntryLoadButton"
                            prop.className "swt:btn swt:btn-sm swt:btn-primary"
                            prop.text "Reload Repos"
                            prop.disabled (not isConnected)
                            prop.onClick (fun _ -> setReloadTrigger (reloadTrigger + 1))
                        ]
                        Html.button [
                            prop.text "Test"
                            prop.onClick (fun _ ->
                                Browser.Dom.console.log "Testing GitLab API connection with current credentials..."

                                promise {
                                    let! user = GitLabApi.ListExploreMostStarred(baseUrl, pat)
                                    Browser.Dom.console.log (user)
                                }
                                |> Promise.start
                            )
                        ]
                    ]
                ]

                let btnInfo =
                    fun (project: ExploreProjectDto) -> [|
                        ButtonInfo.create (
                            "swt:fluent--arrow-download-24-filled",
                            "Download",
                            fun _ -> Browser.Dom.window.alert ("Download " + project.name)
                        )
                        ButtonInfo.create (
                            "swt:fluent--star-24-regular",
                            "Star",
                            fun _ -> Browser.Dom.window.alert ("Star " + project.name)
                        )
                    |]

                DataHubBrowser.ExplorePanel(accounts, loaders, reloadTrigger, projectActionBtns = btnInfo)
            ]
        ]
