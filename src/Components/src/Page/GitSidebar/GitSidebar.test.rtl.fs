module internal Swate.Components.Tests.GitSidebarNotices

open Feliz
open Swate.Components.Page
open Swate.Components.Page.GitSidebarTypes
open Vitest

let private callbacks: GitSidebarCallbacks = {
    OnRefresh = ignore
    OnFetch = ignore
    OnPush = ignore
    OnUpdateFromOnline = ignore
    OnPrimarySaveSelection = ignore
    OnPrimarySaveAll = ignore
    OnCommitSelection = ignore
    OnCommitAll = ignore
    OnDiscardSelection = ignore
    OnConfirmPendingRemoteAction = ignore
    OnCancelPendingRemoteAction = ignore
    OnSaveDownloadLargeFiles = ignore
    OnSaveLfsAutoTrackThreshold = ignore
    OnSaveDiffIndexingLimit = ignore
    OnCreateBranch = ignore
    OnSwitchBranch = ignore
    OnSelectChange = fun _ -> promise { return Ok() }
    OnPruneLfsCache = ignore
    OnDedupLfsStorage = ignore
    OnCancelOperation = ignore
}

let private status: GitSidebarStatus = {
    CurrentBranch = Some "main"
    TrackingBranch = None
    Ahead = 0
    Behind = 0
    IsClean = true
    IsMergeInProgress = false
}

let private activity label detail percent : GitSidebarBackgroundActivity = {
    Label = label
    Detail = detail
    ProgressPercent = percent
}

let private render
    (runStatus: GitSidebarRunStatus)
    (backgroundActivities: GitSidebarBackgroundActivity[])
    (busyHeadlineOverride: string option)
    =
    RTL.render (
        GitSidebar.Main(
            status = status,
            changedFiles = [||],
            branchOptions = [||],
            callbacks = callbacks,
            downloadLargeFiles = true,
            lfsAutoTrackThresholdMb = 1,
            diffIndexingLimitMb = 1024,
            runStatus = runStatus,
            backgroundActivities = backgroundActivities,
            ?busyHeadlineOverride = busyHeadlineOverride
        )
    )
    |> ignore

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "the background activity notice lists each activity with its detail and percent",
    fun () ->
        render
            GitSidebarRunStatus.Idle
            [|
                activity "Downloading raw-data" (Some "12 files") (Some 40.0)
                activity "Freeing notes.bin" None None
            |]
            None

        RTL.screen.getByTestId "GitSidebarBackgroundActivity" |> ignore

        Vitest.expect((RTL.screen.getByTestId "GitSidebarBackgroundActivityItem-0").textContent).toBe
            "Downloading raw-data | 12 files"

        Vitest.expect((RTL.screen.getByTestId "GitSidebarBackgroundActivityItem-1").textContent).toBe
            "Freeing notes.bin"

        RTL.screen.getByTestId "GitSidebarBackgroundActivityBar-0" |> ignore
        Vitest.expect(RTL.screen.queryByTestId "GitSidebarBackgroundActivityBar-1" |> Option.isNone).toBe true
)

Vitest.test (
    "no background activity notice renders for an empty list",
    fun () ->
        render GitSidebarRunStatus.Idle [||] None

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarBackgroundActivity" |> Option.isNone).toBe true
)

Vitest.test (
    "the headline override replaces the headline of the busy notice",
    fun () ->
        render (GitSidebarRunStatus.Busy "Committing all changes") [||] (Some "Waits for downloading raw-data")

        let headline = RTL.screen.getByTestId "GitSidebarBusyHeadline"

        Vitest.expect(headline.textContent).toBe "Waits for downloading raw-data"
)
