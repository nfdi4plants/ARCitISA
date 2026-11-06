module internal Swate.Components.Tests.GitSidebarNotices

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
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

let private operationElement
    (runStatus: GitSidebarRunStatus)
    (steps: GitSidebarStep[] option)
    (log: GitSidebarLog option)
    =
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

let private renderOperation
    (runStatus: GitSidebarRunStatus)
    (steps: GitSidebarStep[] option)
    (log: GitSidebarLog option)
    =
    RTL.render (operationElement runStatus steps log) |> ignore

let private lastLogOf (title: string) (lines: (string * string) list) : GitSidebarLastLog = {
    Title = title
    Log = logOf 0 lines
}

let private lastLogElement (runStatus: GitSidebarRunStatus) (lastLog: GitSidebarLastLog option) =
    GitSidebar.Main(
        status = status,
        changedFiles = [||],
        branchOptions = [||],
        callbacks = callbacks,
        downloadLargeFiles = true,
        lfsAutoTrackThresholdMb = 1,
        diffIndexingLimitMb = 1024,
        runStatus = runStatus,
        ?lastLog = lastLog
    )

let private renderWithLastLog (runStatus: GitSidebarRunStatus) (lastLog: GitSidebarLastLog option) =
    RTL.render (lastLogElement runStatus lastLog) |> ignore

/// jsdom has no layout, so the height of an element is a getter that the test replaces on the element.
[<Emit("Object.defineProperty($0, $1, { get: $2, configurable: true })")>]
let private measure (element: HTMLElement) (name: string) (height: unit -> float) : unit = jsNative

/// The line elements of the first log section of a panel.
[<Emit("Array.from($0.firstElementChild.children)")>]
let private logSectionChildren (panel: HTMLElement) : HTMLElement[] = jsNative

let private logLineNode (panel: HTMLElement) (text: string) : HTMLElement =
    logSectionChildren panel |> Array.find (fun line -> line.textContent = text)

/// The clipboard that the copy button writes to. jsdom has none. The copy tests install a stub and afterEach
/// removes it after every test.
type private ClipboardStub =
    abstract writeText: text: string -> JS.Promise<unit>

[<Emit("Object.defineProperty(navigator, 'clipboard', { value: $0, configurable: true })")>]
let private installClipboard (clipboard: ClipboardStub) : unit = jsNative

[<Emit("delete navigator.clipboard")>]
let private removeClipboard () : unit = jsNative

/// A clipboard that stores the written text and resolves.
let private recordingClipboard (written: string option ref) =
    installClipboard
        { new ClipboardStub with
            member _.writeText text =
                written.Value <- Some text
                promise { return () }
        }

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    removeClipboard ()
)

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
    "an idle sidebar unfolds and folds the last log with its title and lines",
    fun () ->
        let lastLog =
            lastLogOf "Pull demo-arc 14:32" [
                "command", "git fetch origin"
                "stderr", "fatal: no remote"
            ]

        renderWithLastLog GitSidebarRunStatus.Idle (Some lastLog)

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarLastLogPanel" |> Option.isNone).toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLastLog")

        let panel = RTL.screen.getByTestId "GitSidebarLastLogPanel"
        Vitest.expect(panel.textContent.Contains "Pull demo-arc 14:32").toBe true
        Vitest.expect(panel.textContent.Contains "git fetch origin").toBe true
        Vitest.expect(panel.textContent.Contains "fatal: no remote").toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLastLog")

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarLastLogPanel" |> Option.isNone).toBe true
)

Vitest.test (
    "no last log button renders while an operation runs, without a last log or for a last log without lines",
    fun () ->
        let lastLog = lastLogOf "Pull demo-arc 14:32" [ "stdout", "done" ]

        let progress =
            GitSidebarRunStatus.Progress {
                Method = Some "pull"
                Stage = None
                ProgressPercent = Some 33.0
            }

        for runStatus in [ GitSidebarRunStatus.Busy "Pulling"; progress ] do
            renderWithLastLog runStatus (Some lastLog)

            Vitest.expect(RTL.screen.queryByTestId "GitSidebarShowLastLog" |> Option.isNone).toBe true

            RTL.cleanup ()

        renderWithLastLog GitSidebarRunStatus.Idle None

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarShowLastLog" |> Option.isNone).toBe true

        RTL.cleanup ()
        renderWithLastLog GitSidebarRunStatus.Idle (Some(lastLogOf "Pull demo-arc 14:32" []))

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarShowLastLog" |> Option.isNone).toBe true
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

Vitest.test (
    "an open log panel stays open when the busy notice turns into the progress notice",
    fun () ->
        let log = Some(logOf 0 [ "stdout", "Receiving objects: 100%" ])

        let result =
            RTL.render (operationElement (GitSidebarRunStatus.Busy "Pulling") None log)

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")
        RTL.screen.getByTestId "GitSidebarLogPanel" |> ignore

        let progress: GitSidebarProgress = {
            Method = None
            Stage = Some "Receiving objects"
            ProgressPercent = Some 40.0
        }

        result.rerender (operationElement (GitSidebarRunStatus.Progress progress) None log)

        RTL.screen.getByTestId "GitSidebarProgressBar" |> ignore
        Vitest.expect(RTL.screen.queryByTestId "GitSidebarBusyHeadline" |> Option.isNone).toBe true
        RTL.screen.getByTestId "GitSidebarLogPanel" |> ignore
)

Vitest.test (
    "an open last log panel stays open when the sidebar turns busy without a notice",
    fun () ->
        let lastLog = Some(lastLogOf "Pull demo-arc 14:32" [ "stdout", "done" ])

        let result = RTL.render (lastLogElement GitSidebarRunStatus.Idle lastLog)

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLastLog")
        RTL.screen.getByTestId "GitSidebarLastLogPanel" |> ignore

        result.rerender (lastLogElement GitSidebarRunStatus.BusyWithoutNotice lastLog)

        RTL.screen.getByTestId "GitSidebarLastLogPanel" |> ignore
)

Vitest.test (
    "a log line keeps its element when the oldest lines are left out",
    fun () ->
        let lines = [ "stdout", "a"; "stdout", "b"; "stdout", "c" ]

        let result =
            RTL.render (operationElement (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 0 lines)))

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        let before = logLineNode (RTL.screen.getByTestId "GitSidebarLogPanel") "c"

        result.rerender (
            operationElement
                (GitSidebarRunStatus.Busy "Pulling")
                None
                (Some(logOf 1 [ "stdout", "b"; "stdout", "c"; "stdout", "d" ]))
        )

        let after = logLineNode (RTL.screen.getByTestId "GitSidebarLogPanel") "c"

        Vitest.expect(obj.ReferenceEquals(before, after)).toBe true
)

Vitest.test (
    "the log panel follows new lines until the user scrolls up and follows again at the end",
    fun () ->
        let logWith (count: int) =
            Some(logOf 0 [ for index in 1..count -> "stdout", $"line {index}" ])

        let element count =
            operationElement (GitSidebarRunStatus.Busy "Pulling") None (logWith count)

        let result = RTL.render (element 2)
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        let panel = RTL.screen.getByTestId "GitSidebarLogPanel"
        let contentHeight = ref 500.0
        measure panel "scrollHeight" (fun () -> contentHeight.Value)
        measure panel "clientHeight" (fun () -> 100.0)

        result.rerender (element 3)
        Vitest.expect(panel.scrollTop).toBe 500.0

        // The user scrolls up. New lines leave the panel where it is.
        contentHeight.Value <- 800.0
        panel.scrollTop <- 0.0
        RTL.fireEvent.custom ("scroll", panel)
        result.rerender (element 4)
        Vitest.expect(panel.scrollTop).toBe 0.0

        // Reaching the end again resumes the following.
        panel.scrollTop <- 700.0
        RTL.fireEvent.custom ("scroll", panel)
        contentHeight.Value <- 900.0
        result.rerender (element 5)
        Vitest.expect(panel.scrollTop).toBe 900.0
)

Vitest.test (
    "the show logs button controls the log panel and the progress bars report their value",
    fun () ->
        let progress: GitSidebarProgress = {
            Method = None
            Stage = Some "Receiving objects"
            ProgressPercent = Some 40.0
        }

        let withLog = {
            activity "Downloading raw-data" None (Some 25.0) with
                Log = Some(logOf 0 [ "stdout", "done" ])
        }

        RTL.render (
            GitSidebar.Main(
                status = status,
                changedFiles = [||],
                branchOptions = [||],
                callbacks = callbacks,
                downloadLargeFiles = true,
                lfsAutoTrackThresholdMb = 1,
                diffIndexingLimitMb = 1024,
                runStatus = GitSidebarRunStatus.Progress progress,
                backgroundActivities = [| withLog |],
                operationLog = logOf 0 [ "stdout", "Receiving objects: 40%" ]
            )
        )
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        let toggle = RTL.screen.getByTestId "GitSidebarShowLogs"
        let panel = RTL.screen.getByTestId "GitSidebarLogPanel"

        Vitest.expect(toggle.getAttribute "aria-controls").toBe panel.id
        Vitest.expect(panel.id <> "").toBe true
        Vitest.expect(panel.getAttribute "aria-label" <> null).toBe true

        let bar = RTL.screen.getByTestId "GitSidebarProgressBar"
        Vitest.expect(bar.getAttribute "role").toBe "progressbar"
        Vitest.expect(bar.getAttribute "aria-valuenow").toBe "40"
        Vitest.expect(bar.getAttribute "aria-valuemin").toBe "0"
        Vitest.expect(bar.getAttribute "aria-valuemax").toBe "100"

        let backgroundBar = RTL.screen.getByTestId "GitSidebarBackgroundActivityBar-0"
        Vitest.expect(backgroundBar.getAttribute "role").toBe "progressbar"
        Vitest.expect(backgroundBar.getAttribute "aria-valuenow").toBe "25"
)

/// The class names of the elements between a panel and its notice, from the panel outward.
[<Emit("(() => { const names = []; for (let node = $0.parentElement; node && node !== $1; node = node.parentElement) names.push(node.className); return names; })()")>]
let private classesBetween (panel: HTMLElement) (notice: HTMLElement) : string[] = jsNative

/// jsdom has no layout, so the test checks the classes that keep a long line inside the notice.
let private expectPanelScrollsInsideNotice (noticeTestId: string) (panelTestId: string) (longLine: string) =
    let notice = RTL.screen.getByTestId noticeTestId
    let panel = RTL.screen.getByTestId panelTestId
    let panelClasses = panel.className

    Vitest.expect(panelClasses.Contains "swt:overflow-auto").toBe true
    Vitest.expect(panelClasses.Contains "swt:max-w-full").toBe true
    Vitest.expect(panelClasses.Contains "swt:min-w-0").toBe true
    Vitest.expect((logLineNode panel longLine).className.Contains "swt:whitespace-pre").toBe true

    // Each element between the panel and the notice may shrink below its content. The column that holds the
    // panel takes the width of its grid track, which a grid item with a start alignment does not do by itself.
    let ancestors = classesBetween panel notice
    Vitest.expect(ancestors |> Array.forall (fun classes -> classes.Contains "swt:min-w-0")).toBe true
    Vitest.expect((Array.last ancestors).Contains "swt:w-full").toBe true

Vitest.test (
    "the operation log panel of the notice scrolls for a long line instead of growing past the notice",
    fun () ->
        let longLine = String.replicate 40 "--include=/assays/assayA/dataset/big.bin,"

        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 0 [ "command", longLine ]))
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")

        expectPanelScrollsInsideNotice "GitSidebarProgressNotice" "GitSidebarLogPanel" ("$ " + longLine)
)

Vitest.test (
    "the background log panel of the notice scrolls for a long line instead of growing past the notice",
    fun () ->
        let longLine = String.replicate 40 "--include=/assays/assayA/dataset/big.bin,"

        let withLog = {
            activity "Downloading raw-data" None None with
                Log = Some(logOf 0 [ "command", longLine ])
        }

        render GitSidebarRunStatus.Idle [| withLog |] None
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarBackgroundShowLogs")

        expectPanelScrollsInsideNotice "GitSidebarBackgroundActivity" "GitSidebarBackgroundLogPanel" ("$ " + longLine)
)

Vitest.test (
    "the copy button writes the whole operation log to the clipboard",
    fun () -> promise {
        let written = ref None
        recordingClipboard written

        renderOperation
            (GitSidebarRunStatus.Busy "Pulling")
            None
            (Some(
                logOf 3 [
                    "command", "git fetch origin"
                    "stdout", "Receiving objects: 100%"
                ]
            ))

        Vitest.expect(RTL.screen.queryByTestId "GitSidebarCopyLogs" |> Option.isNone).toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarCopyLogs")

        let! _ = RTL.screen.findByText "Copied"

        Vitest.expect((RTL.screen.getByTestId "GitSidebarCopyLogs").getAttribute "aria-label").toBe "Logs copied"

        Vitest
            .expect(written.Value)
            .toEqual (Some "3 earlier lines were left out\n$ git fetch origin\nReceiving objects: 100%")
    }
)

Vitest.test (
    "the last log copy button writes the title above the lines to the clipboard",
    fun () -> promise {
        let written = ref None
        recordingClipboard written

        renderWithLastLog
            GitSidebarRunStatus.Idle
            (Some(
                lastLogOf "Pull demo-arc 14:32" [
                    "command", "git fetch origin"
                    "stderr", "fatal: no remote"
                ]
            ))

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLastLog")
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarCopyLastLog")

        do!
            RTL.waitFor (fun () ->
                Vitest.expect(written.Value).toEqual (Some "Pull demo-arc 14:32\n$ git fetch origin\nfatal: no remote")
            )
    }
)

Vitest.test (
    "the copy button reports a clipboard that rejects the text",
    fun () -> promise {
        installClipboard
            { new ClipboardStub with
                member _.writeText _ = Promise.reject (exn "denied")
            }

        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 0 [ "stdout", "done" ]))
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarCopyLogs")

        let! _ = RTL.screen.findByText "Copy failed"
        ()
    }
)

Vitest.test (
    "the background copy button writes each activity label above its lines with a blank line between activities",
    fun () -> promise {
        let written = ref None
        recordingClipboard written

        let withLog label lines = {
            activity label None None with
                Log = Some(logOf 0 lines)
        }

        render
            GitSidebarRunStatus.Idle
            [|
                withLog "Downloading raw-data" [ "command", "git lfs pull"; "stdout", "done" ]
                withLog "Freeing notes.bin" [ "stdout", "freed" ]
            |]
            None

        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarBackgroundShowLogs")
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarBackgroundCopyLogs")

        let! _ = RTL.screen.findByText "Copied"

        Vitest
            .expect(written.Value)
            .toEqual (Some "Downloading raw-data\n$ git lfs pull\ndone\n\nFreeing notes.bin\nfreed")
    }
)

Vitest.test (
    "the copy button reports a missing clipboard",
    fun () -> promise {
        removeClipboard ()

        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 0 [ "stdout", "done" ]))
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarCopyLogs")

        let! _ = RTL.screen.findByText "Copy failed"
        ()
    }
)

Vitest.test (
    "the copy button goes back to Copy two seconds after the attempt",
    fun () -> promise {
        // Fake timers stall the polling of findByText, so this test waits for the real two seconds.
        recordingClipboard (ref None)

        renderOperation (GitSidebarRunStatus.Busy "Pulling") None (Some(logOf 0 [ "stdout", "done" ]))
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarShowLogs")
        RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarCopyLogs")

        let! _ = RTL.screen.findByText "Copied"

        do!
            RTL.waitFor (
                (fun () ->
                    Vitest.expect((RTL.screen.getByTestId "GitSidebarCopyLogs").getAttribute "aria-label").toBe
                        "Copy logs"
                ),
                RTLWaitForOptions(timeout = 3000)
            )
    }
)
