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
    Log = None
}

let private logOf (omitted: int) (lines: (string * string) list) : GitSidebarLog = {
    Lines =
        lines
        |> List.map (fun (kind, text) -> { Kind = kind; Text = text })
        |> List.toArray
    Omitted = omitted
}

let private step label isCurrent : GitSidebarStep = {
    Label = label
    Detail = None
    Percent = None
    IsCurrent = isCurrent
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

let private renderOperation
    (runStatus: GitSidebarRunStatus)
    (steps: GitSidebarStep[] option)
    (log: GitSidebarLog option)
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
            ?operationSteps = steps,
            ?operationLog = log
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

Vitest.test (
    "no show logs button renders without a log or for a log without lines",
    fun () ->
        renderOperation (GitSidebarRunStatus.Busy "Pulling") None None

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarShowLogs" |> Option.isNone).toBe true

        RTL.cleanup ()
        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 3 []))

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarShowLogs" |> Option.isNone).toBe true
)

Vitest.test (
    "the show logs button of a busy notice unfolds and folds the log panel",
    fun () ->
        renderOperation
            (GitSidebarRunStatus.Busy "Pulling")
            None
            (Some(
                logOf 0 [
                    "command", "git fetch origin"
                    "stdout", "Receiving objects: 100%"
                ]
            ))

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarLogPanel" |> Option.isNone).toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        let panel = RTL.screen.getByTestId "GitSidebarLogPanel"
        Vitest.expect(panel.textContent.Contains "git fetch origin").toBe true
        Vitest.expect(panel.textContent.Contains "Receiving objects: 100%").toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarLogPanel" |> Option.isNone).toBe true
)

Vitest.test (
    "the log panel starts with the number of omitted lines only when earlier lines were left out",
    fun () ->
        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 0 [ "stdout", "done" ]))
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarLogPanelOmitted-0" |> Option.isNone).toBe true

        RTL.cleanup ()
        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 1342 [ "stdout", "done" ]))
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        let omitted = RTL.screen.getByTestId "GitSidebarLogPanelOmitted-0"
        Vitest.expect(omitted.textContent.Contains "1342").toBe true
        Vitest.expect(omitted.nextElementSibling.textContent).toBe "done"
)

Vitest.test (
    "the step list renders one entry per step for two or more steps",
    fun () ->
        renderOperation
            (GitSidebarRunStatus.Busy "Pulling")
            (Some [|
                step "Fetching" false
                step "Merging" false
                step "Downloading" true
            |])
            None

        RTL.screen.getByTestId "GitSidebarSteps" |> ignore

        Vitest.expect(RTL.screen.queryAllByTestId(System.Text.RegularExpressions.Regex "^GitSidebarStep-").Count).toBe 3
)

Vitest.test (
    "a single step renders no step list",
    fun () ->
        renderOperation (GitSidebarRunStatus.Busy "Pulling") (Some [| step "Fetching" true |]) None

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarSteps" |> Option.isNone).toBe true
)

Vitest.test (
    "only the current step shows its detail and percent",
    fun () ->
        let current = {
            step "Downloading" true with
                Detail = Some "Receiving objects (12/80)"
                Percent = Some 15.0
        }

        let done' = {
            step "Fetching" false with
                Detail = Some "stale detail"
        }

        renderOperation (GitSidebarRunStatus.Busy "Pulling") (Some [| done'; current |]) None

        let detail = RTL.screen.getByTestId "GitSidebarStepDetail-1"
        Vitest.expect(detail.textContent.Contains "Receiving objects (12/80)").toBe true
        Vitest.expect(detail.textContent.Contains "15").toBe true
        Vitest.expect(RTL.screen.queryByTestId "GitSidebarStepDetail-0" |> Option.isNone).toBe true
)

Vitest.test (
    "the background activity notice offers show logs only when a task has log lines and lists the lines under the task",
    fun () ->
        render GitSidebarRunStatus.Idle [| activity "Downloading raw-data" None None |] None

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarBackgroundShowLogs" |> Option.isNone).toBe true

        RTL.cleanup ()

        let withLog = {
            activity "Downloading raw-data" None None with
                Log = Some(logOf 0 [ "command", "git lfs pull" ])
        }

        render GitSidebarRunStatus.Idle [| withLog; activity "Freeing notes.bin" None None |] None

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarBackgroundLogPanel" |> Option.isNone).toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarBackgroundShowLogs")

        let panel = RTL.screen.getByTestId "GitSidebarBackgroundLogPanel"
        Vitest.expect(panel.textContent.Contains "Downloading raw-data").toBe true
        Vitest.expect(panel.textContent.Contains "git lfs pull").toBe true
        Vitest.expect(panel.textContent.Contains "Freeing notes.bin").toBe false

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarBackgroundShowLogs")

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarBackgroundLogPanel" |> Option.isNone).toBe true
)
