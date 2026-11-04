namespace Swate.Components.Page

open System
open Browser.Types
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.BaseModal

open Swate.Components.Page.GitSidebarTypes

module private GitSidebarInternal =

    type GitChangeKind =
        | Added
        | Modified
        | Deleted
        | Renamed
        | Untracked
        | Conflict

    type ChangePresentation = {
        Label: string
        Shorthand: string
        ToneClassName: string
    }

    let hasConflicts (status: GitSidebarStatus) (changedFiles: GitSidebarChange[]) =
        status.IsMergeInProgress || (changedFiles |> Array.exists _.IsConflicted)

    let private rawChangeCode (change: GitSidebarChange) =
        let indexCode = GitStatusCode.normalize change.IndexStatus
        let worktreeCode = GitStatusCode.normalize change.WorkingTreeStatus
        $"{indexCode}{worktreeCode}"

    let describeChange (change: GitSidebarChange) = $"git: {rawChangeCode change}"

    let classifyChange (change: GitSidebarChange) =
        let indexCode = GitStatusCode.normalize change.IndexStatus
        let worktreeCode = GitStatusCode.normalize change.WorkingTreeStatus

        if change.IsConflicted then
            GitChangeKind.Conflict
        elif
            change.OriginalPath.IsSome
            || indexCode.StartsWith("R", StringComparison.Ordinal)
            || worktreeCode.StartsWith("R", StringComparison.Ordinal)
        then
            GitChangeKind.Renamed
        elif indexCode = "?" || worktreeCode = "?" then
            GitChangeKind.Untracked
        elif indexCode = "D" || worktreeCode = "D" then
            GitChangeKind.Deleted
        elif indexCode = "A" || worktreeCode = "A" then
            GitChangeKind.Added
        else
            GitChangeKind.Modified

    let changePresentation (change: GitSidebarChange) : ChangePresentation =
        match classifyChange change with
        | GitChangeKind.Added -> {
            Label = "Added"
            Shorthand = "A"
            ToneClassName = "swt:text-success"
          }
        | GitChangeKind.Modified -> {
            Label = "Modified"
            Shorthand = "M"
            ToneClassName = "swt:text-warning"
          }
        | GitChangeKind.Deleted -> {
            Label = "Deleted"
            Shorthand = "D"
            ToneClassName = "swt:text-error"
          }
        | GitChangeKind.Renamed -> {
            Label = "Renamed"
            Shorthand = "R"
            ToneClassName = "swt:text-info"
          }
        | GitChangeKind.Untracked -> {
            Label = "New (Untracked)"
            Shorthand = "N"
            ToneClassName = "swt:text-success"
          }
        | GitChangeKind.Conflict -> {
            Label = "Conflict"
            Shorthand = "C"
            ToneClassName = "swt:text-error"
          }

    /// Splits a repository-relative path into the file name and its parent directory. A trailing slash stays with the name.
    let splitPath (path: string) : string * string option =
        match path.TrimEnd('/').LastIndexOf '/' with
        | index when index > 0 -> path.Substring(index + 1), Some(path.Substring(0, index))
        | _ -> path, None

    let commitCountLabel (count: int) =
        if count = 1 then "1 commit" else $"{count} commits"

    let branchKindLabel (kind: GitSidebarBranchKind) =
        match kind with
        | GitSidebarBranchKind.Local -> "Local"
        | GitSidebarBranchKind.Remote -> "Remote"

    let progressPercentValue (progress: GitSidebarProgress) =
        progress.ProgressPercent
        |> Option.map (fun value -> value |> max 0.0 |> min 100.0)

    let progressText (progress: GitSidebarProgress) =
        [
            progress.Method
            progress.Stage
            progress
            |> progressPercentValue
            |> Option.map (fun value -> $"{Math.Round(value)}%%")
        ]
        |> List.choose id
        |> String.concat " | "

    /// The detail line of the current step: its message and its percent.
    let stepDetailText (step: GitSidebarStep) =
        [
            step.Detail
            step.Percent
            |> Option.map (fun value -> value |> max 0.0 |> min 100.0 |> (fun value -> $"{Math.Round(value)}%%"))
        ]
        |> List.choose id
        |> String.concat " | "

    let omittedLinesText (omitted: int) =
        if omitted = 1 then
            "1 earlier line was left out"
        else
            $"{omitted} earlier lines were left out"

    /// Command lines are bold and notices are muted. A stderr line keeps the text color of the panel and has a
    /// warning stripe at its left edge, because the warning color as text has too little contrast on the light panel.
    /// Every line reserves the stripe so the text of all kinds starts at the same place.
    let logLineClassName (kind: string) =
        match kind with
        | "command" -> "swt:border-transparent swt:font-bold"
        | "stderr" -> "swt:border-warning"
        | "notice" -> "swt:border-transparent swt:italic swt:opacity-70"
        | _ -> "swt:border-transparent"

    let logLineText (line: GitSidebarLogLine) =
        if line.Kind = "command" then
            "$ " + line.Text
        else
            line.Text

    /// The lines of one log. A title names the task when a viewer shows the logs of several tasks.
    type LogSection = {
        Title: string option
        Log: GitSidebarLog
    }

    /// The label state of a copy button.
    type LogCopyState =
        | Idle
        | Copied
        | Failed

    /// The text that the copy button puts on the clipboard. It holds the lines of every section, in the order of
    /// the panel.
    let logCopyText (sections: LogSection[]) : string =
        sections
        |> Array.map (fun section ->
            [
                yield! section.Title |> Option.toList
                if section.Log.Omitted > 0 then
                    omittedLinesText section.Log.Omitted
                yield! section.Log.Lines |> Array.map logLineText
            ]
            |> String.concat "\n"
        )
        |> String.concat "\n\n"

    // The elements below are built outside of the component functions with flat lists. The React Compiler is slow
    // on the nested closures that Fable emits for Feliz lists with conditional items.

    let stepElement (index: int) (step: GitSidebarStep) : ReactElement =
        let detail = stepDetailText step

        let marker =
            if step.IsCurrent then
                Html.span [
                    prop.className "swt:loading swt:loading-spinner swt:loading-xs swt:shrink-0"
                ]
            else
                Html.span [
                    prop.className "swt:iconify swt:fluent--checkmark-circle-24-regular swt:size-4 swt:shrink-0"
                ]

        let detailLine =
            if step.IsCurrent && detail <> "" then
                Html.span [
                    prop.testId $"GitSidebarStepDetail-{index}"
                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                    prop.text detail
                ]
            else
                Html.none

        Html.li [
            prop.key index
            prop.testId $"GitSidebarStep-{index}"
            prop.className "swt:flex swt:min-w-0 swt:items-start swt:gap-2"
            prop.children [
                marker
                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:flex-col"
                    prop.children [
                        Html.span [
                            prop.className "swt:min-w-0 swt:wrap-anywhere"
                            prop.text step.Label
                        ]
                        detailLine
                    ]
                ]
            ]
        ]

    /// One background activity: the label with its detail, and a bar while it has a percent.
    let backgroundActivityElement (index: int) (activity: GitSidebarBackgroundActivity) : ReactElement =
        let text =
            match activity.Detail with
            | Some detail -> $"{activity.Label} | {detail}"
            | None -> activity.Label

        let bar =
            match activity.ProgressPercent with
            | Some percent ->
                let value = Math.Max(0.0, Math.Min(100.0, percent))

                Html.div [
                    prop.testId $"GitSidebarBackgroundActivityBar-{index}"
                    prop.role "progressbar"
                    prop.ariaLabel activity.Label
                    prop.custom ("aria-valuenow", Math.Round value)
                    prop.custom ("aria-valuemin", 0)
                    prop.custom ("aria-valuemax", 100)
                    prop.className "swt:h-1.5 swt:w-full swt:rounded-full swt:bg-base-300"
                    prop.children [
                        Html.div [
                            prop.className "swt:h-full swt:rounded-full swt:transition-all swt:duration-300"
                            prop.style [
                                style.width (length.percent value)
                                style.custom ("backgroundColor", "var(--color-base-content)")
                            ]
                        ]
                    ]
                ]
            | None -> Html.none

        Html.div [
            prop.key index
            prop.testId $"GitSidebarBackgroundActivityItem-{index}"
            prop.className "swt:flex swt:min-w-0 swt:flex-col swt:gap-1"
            prop.children [
                Html.span [
                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                    prop.text text
                ]
                bar
            ]
        ]

    let logSectionElement (panelTestId: string) (sectionIndex: int) (section: LogSection) : ReactElement =
        let title =
            match section.Title with
            | Some title ->
                Html.div [
                    prop.className "swt:font-sans swt:font-semibold swt:wrap-anywhere"
                    prop.text title
                ]
            | None -> Html.none

        let omitted =
            if section.Log.Omitted > 0 then
                Html.div [
                    prop.testId $"{panelTestId}Omitted-{sectionIndex}"
                    prop.className "swt:italic swt:opacity-70"
                    prop.text (omittedLinesText section.Log.Omitted)
                ]
            else
                Html.none

        // The keys count from the first line of the whole log. When the oldest lines go, a line keeps its key.
        let lines =
            section.Log.Lines
            |> Array.mapi (fun lineIndex line ->
                Html.div [
                    prop.key (section.Log.Omitted + lineIndex)
                    prop.className ("swt:whitespace-pre swt:border-l-2 swt:pl-1.5 " + logLineClassName line.Kind)
                    prop.text (logLineText line)
                ]
            )

        Html.div [
            prop.key sectionIndex
            prop.className "swt:flex swt:flex-col"
            prop.children (Array.append [| title; omitted |] lines)
        ]

    let formatThresholdInput (thresholdMb: int) = string thresholdMb

    /// The owner a new publish prompt starts with: the requested one if it is offered and enabled,
    /// otherwise the personal namespace if it is enabled, otherwise the first enabled owner.
    /// None when no owner is enabled or the prompt offers no owners.
    let initialPublishNamespaceId (prompt: GitSidebarPublishRenamePrompt option) : int option =
        match prompt with
        | None -> None
        | Some prompt ->
            let enabled =
                prompt.Owners |> Array.filter (fun owner -> owner.DisabledReason.IsNone)

            enabled
            |> Array.tryFind (fun owner ->
                prompt.SelectedNamespaceId.IsSome
                && owner.NamespaceId = prompt.SelectedNamespaceId
            )
            |> Option.orElse (enabled |> Array.tryFind (fun owner -> owner.NamespaceId.IsNone))
            |> Option.orElse (Array.tryHead enabled)
            |> Option.bind _.NamespaceId

    /// Test id suffix of an owner choice.
    let publishOwnerKey (owner: GitSidebarPublishOwner) : string =
        match owner.NamespaceId with
        | Some id -> string id
        | None -> "personal"

    let tryRangeBetween (orderedPaths: string[]) (anchorPath: string) (clickedPath: string) =
        match
            orderedPaths
            |> Array.tryFindIndex (fun path -> String.Equals(path, anchorPath, StringComparison.Ordinal)),
            orderedPaths
            |> Array.tryFindIndex (fun path -> String.Equals(path, clickedPath, StringComparison.Ordinal))
        with
        | Some anchorIndex, Some clickedIndex ->
            let lower = min anchorIndex clickedIndex
            let upper = max anchorIndex clickedIndex
            Some orderedPaths.[lower..upper]
        | _ -> None

[<RequireQualifiedAccess>]
type private ActiveDialog =
    | None
    | AdvancedActions
    | CreateBranch
    | SwitchBranch

type private BranchHeaderProps = {
    Status: GitSidebarStatus
    HasRemote: bool
    HasConflicts: bool
    IsBusy: bool
    CanOpenRemoteRepository: bool
    OnOpenRemoteRepository: unit -> unit
    OnRefresh: unit -> unit
}

type private LfsSettingsSectionProps = {
    IsBusy: bool
    LfsThresholdInput: string
    SetLfsThresholdInput: string -> unit
    CanSaveLfsThreshold: bool
    SubmitLfsThreshold: unit -> unit
}

type private DiffIndexingLimitSectionProps = {
    IsBusy: bool
    DiffIndexingLimitInput: string
    SetDiffIndexingLimitInput: string -> unit
    CanSaveDiffIndexingLimit: bool
    SubmitDiffIndexingLimit: unit -> unit
}

type private AdvancedActionsProps = {
    DownloadLargeFilesInput: bool
    IsBusy: bool
    RemoteActionsEnabled: bool
    RemoteActionsWarning: string option
    SubmitDownloadLargeFiles: bool -> unit
    SubmitUpdateFromOnline: unit -> unit
    IsAdvancedActionsOpen: bool
    ToggleAdvancedActions: unit -> unit
    SubmitFetch: unit -> unit
    SubmitPush: unit -> unit
    OpenCreateBranchModal: unit -> unit
    OpenSwitchBranchModal: unit -> unit
    CanSwitchBranch: bool
    LfsSettings: LfsSettingsSectionProps
    DiffIndexingLimit: DiffIndexingLimitSectionProps
    SubmitPruneLfsCache: unit -> unit
    SubmitDedupLfsStorage: unit -> unit
}

type private CommitSectionProps = {
    Status: GitSidebarStatus
    HasConflicts: bool
    CanEditCommit: bool
    CommitMessage: string
    SetCommitMessage: string -> unit
    MarkedCount: int
    HasMarkedFiles: bool
    CanRunPrimarySave: bool
    UploadRightAway: bool
    SetUploadRightAway: bool -> unit
    SubmitSaveSelected: unit -> unit
    SubmitSaveAll: unit -> unit
}

type private ChangedFilesListProps = {
    Status: GitSidebarStatus
    ChangedFiles: GitSidebarChange[]
    MarkedPaths: Set<string>
    IsBusy: bool
    UpdateMarkedSelection: GitSidebarChange -> bool -> bool -> unit
    OpenChange: GitSidebarChange -> unit
    DiscardChanges: string[] -> unit
}

type private ChangedFileRowProps = {
    Change: GitSidebarChange
    Index: int
    IsMarked: bool
    IsBusy: bool
    DiscardPaths: string[]
    DiscardChanges: string[] -> unit
    UpdateMarkedSelection: GitSidebarChange -> bool -> bool -> unit
    OpenChange: GitSidebarChange -> unit
    VirtualStart: int
    MeasureElementRef: VirtualMeasureElementRef
}

type private ChangedFileVirtualItem = { Index: int; Start: int }

type private ModalsProps = {
    IsCreateBranchModalOpen: bool
    SetCreateBranchModalOpen: bool -> unit
    BranchName: string
    SetBranchName: string -> unit
    BranchOptionsWithHead: (string option * string)[]
    SelectedStartPoint: string option
    SetSelectedStartPoint: string option -> unit
    SubmitCreateBranch: unit -> unit
    IsSwitchBranchModalOpen: bool
    SetSwitchBranchModalOpen: bool -> unit
    BranchOptionsForSwitch: GitSidebarBranchOption[]
    SelectedSwitchBranch: string option
    SetSelectedSwitchBranch: string option -> unit
    SubmitSwitchBranch: unit -> unit
    IsMissingMessageModalOpen: bool
    SetMissingMessageModalOpen: bool -> unit
    CloseDialog: unit -> unit
}

[<NoEquality; NoComparison>]
type private PublishRenameDialogProps = {
    Prompt: GitSidebarPublishRenamePrompt option
    InputValue: string
    SetInputValue: string -> unit
    SelectedNamespaceId: int option
    SetSelectedNamespaceId: int option -> unit
    IsBusy: bool
    Submit: unit -> unit
    Cancel: unit -> unit
}

[<NoEquality; NoComparison>]
type private PendingRemoteActionDialogProps = {
    PendingConfirmation: GitSidebarConfirmationDialog option
    ConfirmPendingRemoteAction: unit -> unit
    CancelPendingRemoteAction: unit -> unit
}

type private LogViewerProps = {
    Sections: GitSidebarInternal.LogSection[]
    ShowTestId: string
    PanelTestId: string
    PanelLabel: string
    CopyTestId: string
    /// The noun in the toggle text, as in "Show logs".
    Subject: string
}

[<Erase; Mangle(false)>]
type GitSidebar =

    /// The steps of the running operation. The earlier steps are done and the last one is the current step.
    [<ReactComponent>]
    static member private Steps(steps: GitSidebarStep[]) =
        Html.ol [
            prop.testId "GitSidebarSteps"
            prop.className "swt:flex swt:min-w-0 swt:flex-col swt:gap-1 swt:text-xs"
            prop.children (steps |> Array.mapi GitSidebarInternal.stepElement)
        ]

    /// A button that folds and unfolds the panel with the raw output of one or more tasks. The panel stays
    /// scrolled to its last line while lines arrive, until the user scrolls up.
    [<ReactComponent>]
    static member private LogViewer(props: LogViewerProps) =
        let isOpen, setIsOpen = React.useState false
        let panelId = React.useId ()
        let panelRef = React.useElementRef ()
        let followsEnd = React.useRef true

        React.useLayoutEffect (fun () ->
            match panelRef.current with
            | Some panel when followsEnd.current -> panel.scrollTop <- panel.scrollHeight
            | _ -> ()
        )

        let copyState, setCopyState = React.useState GitSidebarInternal.LogCopyState.Idle

        // Two seconds after a copy attempt the label goes back to Copy.
        React.useEffect (
            (fun () ->
                match copyState with
                | GitSidebarInternal.LogCopyState.Idle -> FsReact.createDisposable (fun () -> ())
                | _ ->
                    let timer =
                        Browser.Dom.window.setTimeout (
                            (fun () -> setCopyState GitSidebarInternal.LogCopyState.Idle),
                            2000
                        )

                    FsReact.createDisposable (fun () -> Browser.Dom.window.clearTimeout timer)
            ),
            [| box copyState |]
        )

        // The text is built at click time, so it holds every line that arrived so far.
        let copyLogs () =
            promise {
                try
                    do!
                        Swate.Components.GlobalBindings.navigator.clipboard.writeText (
                            GitSidebarInternal.logCopyText props.Sections
                        )

                    setCopyState GitSidebarInternal.LogCopyState.Copied
                with _ ->
                    setCopyState GitSidebarInternal.LogCopyState.Failed
            }
            |> Promise.start

        let toggle =
            Html.button [
                prop.testId props.ShowTestId
                prop.type'.button
                prop.ariaExpanded isOpen
                prop.custom ("aria-controls", panelId)
                prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:self-start swt:gap-1 swt:normal-case"
                prop.onClick (fun _ ->
                    followsEnd.current <- true
                    setIsOpen (not isOpen)
                )
                prop.children [
                    Html.span [
                        prop.className (
                            if isOpen then
                                "swt:iconify swt:fluent--chevron-down-24-regular swt:size-4"
                            else
                                "swt:iconify swt:fluent--chevron-right-24-regular swt:size-4"
                        )
                    ]
                    Html.span [
                        prop.text (
                            if isOpen then
                                $"Hide {props.Subject}"
                            else
                                $"Show {props.Subject}"
                        )
                    ]
                ]
            ]

        let panel =
            if isOpen then
                Html.div [
                    prop.id panelId
                    prop.role "region"
                    prop.ariaLabel props.PanelLabel
                    prop.testId props.PanelTestId
                    prop.ref panelRef
                    prop.onScroll (fun _ ->
                        panelRef.current
                        |> Option.iter (fun panel ->
                            followsEnd.current <- panel.scrollHeight - panel.scrollTop - panel.clientHeight <= 8.0
                        )
                    )
                    prop.className
                        "swt:max-h-40 swt:min-w-0 swt:max-w-full swt:overflow-auto swt:rounded swt:border swt:border-base-content/30 swt:bg-base-100 swt:p-2 swt:font-mono swt:text-xs swt:text-base-content"
                    prop.children (
                        props.Sections
                        |> Array.mapi (GitSidebarInternal.logSectionElement props.PanelTestId)
                    )
                ]
            else
                Html.none

        let copyButton =
            if isOpen then
                let icon, label =
                    match copyState with
                    | GitSidebarInternal.LogCopyState.Idle -> "swt:fluent--copy-24-regular", "Copy logs"
                    | GitSidebarInternal.LogCopyState.Copied -> "swt:fluent--checkmark-24-regular", "Logs copied"
                    | GitSidebarInternal.LogCopyState.Failed -> "swt:fluent--warning-24-regular", "Copy failed"

                Html.button [
                    prop.type'.button
                    prop.testId props.CopyTestId
                    prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:btn-square"
                    prop.title label
                    prop.ariaLabel label
                    prop.onClick (fun _ -> copyLogs ())
                    prop.children [
                        Html.span [ prop.className $"swt:iconify swt:size-4 {icon}" ]
                    ]
                ]
            else
                Html.none

        // The live region exists before the click, so a screen reader announces the change of its text.
        let copyAnnouncement =
            if isOpen then
                Html.span [
                    prop.className "swt:sr-only"
                    prop.custom ("aria-live", "polite")
                    prop.text (
                        match copyState with
                        | GitSidebarInternal.LogCopyState.Idle -> ""
                        | GitSidebarInternal.LogCopyState.Copied -> "Copied"
                        | GitSidebarInternal.LogCopyState.Failed -> "Copy failed"
                    )
                ]
            else
                Html.none

        let header =
            Html.div [
                prop.className "swt:flex swt:min-w-0 swt:flex-wrap swt:items-center swt:gap-1"
                prop.children [ toggle; copyButton; copyAnnouncement ]
            ]

        Html.div [
            prop.className "swt:flex swt:min-w-0 swt:flex-col swt:gap-1"
            prop.children [ header; panel ]
        ]

    [<ReactComponent>]
    static member OperationStatusNotice
        (
            ?runStatus: GitSidebarRunStatus,
            ?errorNotice: string,
            ?warningNotice: string,
            ?busyTestId: string,
            ?errorTestId: string,
            ?warningTestId: string,
            ?onCancelOperation: unit -> unit,
            ?backgroundActivities: GitSidebarBackgroundActivity[],
            ?busyHeadlineOverride: string,
            ?operationSteps: GitSidebarStep[],
            ?operationLog: GitSidebarLog,
            ?lastLog: GitSidebarLastLog
        ) =
        let runStatus = defaultArg runStatus GitSidebarRunStatus.Idle

        // One step repeats the headline, so the list needs two or more.
        let stepsView =
            match operationSteps with
            | Some steps when steps.Length >= 2 -> GitSidebar.Steps(steps)
            | _ -> Html.none

        let logView =
            match operationLog with
            | Some log when log.Lines.Length > 0 ->
                let section: GitSidebarInternal.LogSection = { Title = None; Log = log }

                GitSidebar.LogViewer(
                    {
                        Sections = [| section |]
                        ShowTestId = "GitSidebarShowLogs"
                        PanelTestId = "GitSidebarLogPanel"
                        PanelLabel = "Operation logs"
                        CopyTestId = "GitSidebarCopyLogs"
                        Subject = "logs"
                    }
                )
            | _ -> Html.none

        // One panel for all background tasks. The lines of a task stand under its label.
        let backgroundLogView (activities: GitSidebarBackgroundActivity[]) =
            let sections =
                activities
                |> Array.choose (fun activity ->
                    activity.Log
                    |> Option.filter (fun log -> log.Lines.Length > 0)
                    |> Option.map (fun log ->
                        let section: GitSidebarInternal.LogSection = {
                            Title = Some activity.Label
                            Log = log
                        }

                        section
                    )
                )

            if sections.Length = 0 then
                Html.none
            else
                GitSidebar.LogViewer(
                    {
                        Sections = sections
                        ShowTestId = "GitSidebarBackgroundShowLogs"
                        PanelTestId = "GitSidebarBackgroundLogPanel"
                        PanelLabel = "Background activity logs"
                        CopyTestId = "GitSidebarBackgroundCopyLogs"
                        Subject = "logs"
                    }
                )

        // The last log appears only while no operation of this sidebar runs, so it never competes with the live log.
        let lastLogView =
            match runStatus, lastLog with
            | (GitSidebarRunStatus.Idle | GitSidebarRunStatus.BusyWithoutNotice), Some lastLog when
                lastLog.Log.Lines.Length > 0
                ->
                Html.div [
                    prop.className "swt:min-w-0 swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        GitSidebar.LogViewer(
                            {
                                Sections = [|
                                    {
                                        Title = Some lastLog.Title
                                        Log = lastLog.Log
                                    }
                                |]
                                ShowTestId = "GitSidebarShowLastLog"
                                PanelTestId = "GitSidebarLastLogPanel"
                                PanelLabel = "Last Git log"
                                CopyTestId = "GitSidebarCopyLastLog"
                                Subject = "last log"
                            }
                        )
                    ]
                ]
            | _ -> Html.none

        let cancelButton =
            onCancelOperation
            |> Option.map (fun cancel ->
                Html.button [
                    prop.testId "GitSidebarCancelOperationButton"
                    prop.className
                        "swt:btn swt:btn-ghost swt:btn-xs swt:shrink-0 swt:gap-1 swt:normal-case swt:text-info-content"
                    prop.title "Cancel"
                    prop.onClick (fun _ -> cancel ())
                    prop.children [
                        Html.span [
                            prop.className "swt:iconify swt:fluent--dismiss-circle-24-regular swt:size-4"
                        ]
                        Html.span [ prop.text "Cancel" ]
                    ]
                ]
            )

        React.Fragment [
            match runStatus with
            | GitSidebarRunStatus.Progress progress ->
                let progressValue = GitSidebarInternal.progressPercentValue progress

                Html.div [
                    prop.className "swt:min-w-0 swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            if busyTestId.IsSome then
                                prop.testId busyTestId.Value
                            prop.className
                                "swt:alert swt:alert-info swt:min-w-0 swt:items-start swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                            prop.children [
                                Html.span [
                                    prop.className
                                        "swt:loading swt:loading-spinner swt:loading-xs swt:mt-0.5 swt:shrink-0"
                                ]
                                Html.div [
                                    prop.className "swt:flex swt:w-full swt:min-w-0 swt:flex-1 swt:flex-col swt:gap-2"
                                    prop.children [
                                        Html.div [
                                            prop.className "swt:flex swt:min-w-0 swt:items-start swt:gap-2"
                                            prop.children [
                                                Html.span [
                                                    prop.className "swt:min-w-0 swt:flex-1 swt:wrap-anywhere"
                                                    prop.text (
                                                        GitSidebarInternal.progressText progress
                                                        |> function
                                                            | "" -> "Git operation in progress"
                                                            | text -> text
                                                    )
                                                ]
                                                cancelButton |> Option.defaultValue Html.none
                                            ]
                                        ]

                                        match progressValue with
                                        | Some value ->
                                            Html.div [
                                                prop.testId "GitSidebarProgressBar"
                                                prop.role "progressbar"
                                                prop.ariaLabel "Operation progress"
                                                prop.custom ("aria-valuenow", Math.Round value)
                                                prop.custom ("aria-valuemin", 0)
                                                prop.custom ("aria-valuemax", 100)
                                                prop.className "swt:h-1.5 swt:w-full swt:rounded-full swt:bg-base-300"
                                                prop.children [
                                                    Html.div [
                                                        prop.className
                                                            "swt:h-full swt:rounded-full swt:transition-all swt:duration-300"
                                                        prop.style [
                                                            style.width (length.percent value)
                                                            style.custom (
                                                                "backgroundColor",
                                                                "var(--color-base-content)"
                                                            )
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        | None -> Html.none

                                        stepsView
                                        logView
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            | GitSidebarRunStatus.Busy notice ->
                // Same structure as the progress notice, so the open state of the logs survives the switch from
                // one to the other when the first progress event arrives.
                Html.div [
                    prop.className "swt:min-w-0 swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            if busyTestId.IsSome then
                                prop.testId busyTestId.Value
                            prop.className
                                "swt:alert swt:alert-info swt:min-w-0 swt:items-start swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                            prop.children [
                                Html.span [
                                    prop.className
                                        "swt:loading swt:loading-spinner swt:loading-xs swt:mt-0.5 swt:shrink-0"
                                ]
                                Html.div [
                                    prop.className "swt:flex swt:w-full swt:min-w-0 swt:flex-1 swt:flex-col swt:gap-2"
                                    prop.children [
                                        Html.div [
                                            prop.className "swt:flex swt:min-w-0 swt:items-start swt:gap-2"
                                            prop.children [
                                                Html.span [
                                                    prop.testId "GitSidebarBusyHeadline"
                                                    prop.className "swt:min-w-0 swt:flex-1 swt:wrap-anywhere"
                                                    prop.text (defaultArg busyHeadlineOverride notice)
                                                ]
                                                cancelButton |> Option.defaultValue Html.none
                                            ]
                                        ]

                                        Html.none

                                        stepsView
                                        logView
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            | GitSidebarRunStatus.Idle
            | GitSidebarRunStatus.BusyWithoutNotice -> Html.none

            match backgroundActivities with
            | Some activities when activities.Length > 0 ->
                Html.div [
                    prop.className "swt:min-w-0 swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            prop.testId "GitSidebarBackgroundActivity"
                            prop.className
                                "swt:alert swt:alert-info swt:min-w-0 swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                            prop.children [
                                Html.span [
                                    prop.className "swt:loading swt:loading-spinner swt:loading-xs swt:shrink-0"
                                ]
                                Html.div [
                                    prop.className "swt:flex swt:w-full swt:min-w-0 swt:flex-1 swt:flex-col swt:gap-2"
                                    prop.children [
                                        Html.div [
                                            prop.className "swt:flex swt:min-w-0 swt:flex-col swt:gap-2"
                                            prop.children (
                                                activities |> Array.mapi GitSidebarInternal.backgroundActivityElement
                                            )
                                        ]

                                        backgroundLogView activities
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            | _ -> Html.none

            match errorNotice with
            | Some message ->
                Html.div [
                    prop.className "swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            if errorTestId.IsSome then
                                prop.testId errorTestId.Value
                            prop.className
                                "swt:alert swt:alert-error swt:min-w-0 swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                            prop.children [
                                Html.span [
                                    prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4 swt:shrink-0"
                                ]
                                Html.span [
                                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                                    prop.text message
                                ]
                            ]
                        ]
                    ]
                ]
            | None -> Html.none

            match warningNotice with
            | Some message ->
                Html.div [
                    prop.className "swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            if warningTestId.IsSome then
                                prop.testId warningTestId.Value
                            prop.className
                                "swt:alert swt:alert-warning swt:min-w-0 swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                            prop.children [
                                Html.span [
                                    prop.className
                                        "swt:iconify swt:fluent--warning-shield-20-regular swt:size-4 swt:shrink-0"
                                ]
                                Html.span [
                                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                                    prop.text message
                                ]
                            ]
                        ]
                    ]
                ]
            | None -> Html.none

            lastLogView
        ]

    [<ReactComponent>]
    static member DownloadLargeFilesToggle
        (
            downloadLargeFiles: bool,
            isBusy: bool,
            onChange: bool -> unit,
            ?testId: string,
            ?label: string,
            ?description: string
        ) =
        Html.label [
            prop.className
                "swt:flex swt:min-w-0 swt:items-start swt:gap-3 swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-100 swt:px-3 swt:py-3 swt:@max-xs:px-2"
            prop.children [
                Html.input [
                    if testId.IsSome then
                        prop.testId testId.Value
                    prop.className "swt:checkbox swt:checkbox-sm swt:mt-0.5 swt:shrink-0"
                    prop.type'.checkbox
                    prop.disabled isBusy
                    prop.isChecked downloadLargeFiles
                    prop.onChange onChange
                ]
                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:flex-col"
                    prop.children [
                        Html.span [
                            prop.className "swt:min-w-0 swt:wrap-break-word swt:text-sm swt:font-medium"
                            prop.text (defaultArg label "Download Large Files")
                        ]
                        match description with
                        | Some text ->
                            Html.span [
                                prop.className "swt:min-w-0 swt:wrap-break-word swt:text-xs swt:text-base-content/70"
                                prop.text text
                            ]
                        | None -> Html.none
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private SectionHeader(title: string, countText: string option) =
        Html.div [
            prop.className
                "swt:flex swt:min-w-0 swt:items-center swt:justify-between swt:gap-2 swt:px-3 swt:pt-3 swt:text-xs swt:font-semibold swt:uppercase swt:tracking-[0.2em] swt:text-base-content/60 swt:@max-xs:px-2 swt:@max-xs:tracking-[0.12em]"
            prop.children [
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate"
                    prop.text title
                ]
                match countText with
                | Some value ->
                    Html.span [
                        prop.className "swt:shrink-0 swt:text-[0.65rem] swt:text-base-content/50"
                        prop.text value
                    ]
                | None -> Html.none
            ]
        ]

    [<ReactComponent>]
    static member private Tooltip
        (label: string, children: ReactElement, ?placementClassName: string, ?testId: string, ?className: string)
        =
        Html.div [
            if testId.IsSome then
                prop.testId testId.Value

            prop.className [
                "swt:tooltip"
                placementClassName |> Option.defaultValue "swt:tooltip-left"
                yield! Option.toList className
            ]
            prop.ariaLabel label
            prop.children [
                Html.div [
                    prop.className "swt:tooltip-content swt:max-w-64 swt:text-xs swt:text-left swt:whitespace-pre-line"
                    prop.text label
                ]
                children
            ]
        ]

    [<ReactComponent>]
    static member private ActionButton
        (
            label: string,
            iconClassName: string,
            isBusy: bool,
            onClick: unit -> unit,
            ?isActive: bool,
            ?testId: string,
            ?tooltipText: string
        ) =
        let isActive = defaultArg isActive false
        let titleText = defaultArg tooltipText label

        Html.button [
            if testId.IsSome then
                prop.testId testId.Value

            prop.className [
                "swt:btn swt:btn-sm swt:w-full swt:min-w-0 swt:justify-start swt:gap-2 swt:overflow-hidden swt:px-2 swt:normal-case"
                "swt:@max-2xs/gitSidebar:gap-1 swt:@max-3xs/gitSidebar:justify-center swt:@max-3xs/gitSidebar:gap-0 swt:@max-3xs/gitSidebar:px-0"
                if isActive then
                    "swt:btn-primary"
                else
                    "swt:bg-base-100 swt:border-base-300"
            ]
            prop.disabled isBusy
            prop.ariaLabel label
            prop.title titleText
            prop.onClick (fun _ -> onClick ())
            prop.children [
                Html.span [
                    prop.className [ "swt:iconify"; iconClassName; "swt:size-4 swt:shrink-0" ]
                ]
                Html.span [
                    if testId.IsSome then
                        prop.testId (testId.Value + "Label")

                    prop.className "swt:min-w-0 swt:flex-1 swt:truncate swt:text-left swt:@max-3xs/gitSidebar:sr-only"
                    prop.text label
                ]
            ]
        ]

    [<ReactComponent>]
    static member private BranchHeader(props: BranchHeaderProps) =
        React.Fragment [
            Html.div [
                prop.className
                    "swt:flex swt:min-w-0 swt:items-center swt:justify-between swt:gap-2 swt:border-b swt:border-base-content/10 swt:px-3 swt:py-3 swt:@max-xs:px-2"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:min-w-0 swt:items-center swt:gap-2"
                        prop.children [
                            Html.span [
                                prop.className "swt:iconify swt:fluent--branch-fork-24-regular swt:size-5 swt:shrink-0"
                            ]
                            Html.div [
                                prop.className "swt:flex swt:min-w-0 swt:flex-col"
                                prop.children [
                                    Html.span [
                                        prop.testId "GitSidebarTitle"
                                        prop.className "swt:truncate swt:text-sm swt:font-semibold"
                                        prop.title "Source Control"
                                        prop.text "Source Control"
                                    ]
                                    let branchName =
                                        match props.Status.CurrentBranch with
                                        | Some branch -> branch
                                        | None -> "Detached HEAD"

                                    Html.span [
                                        prop.className "swt:truncate swt:text-xs swt:text-base-content/60"
                                        prop.title branchName
                                        prop.text branchName
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "swt:flex swt:shrink-0 swt:items-center swt:gap-1"
                        prop.children [
                            Html.button [
                                prop.testId "GitSidebarOpenRemoteRepositoryButton"
                                prop.className "swt:btn swt:btn-ghost swt:btn-square swt:btn-sm swt:shrink-0"
                                prop.disabled (props.IsBusy || not props.CanOpenRemoteRepository)
                                prop.title "Open ARC in DataHUB"
                                prop.ariaLabel "Open ARC in DataHUB"
                                prop.onClick (fun _ -> props.OnOpenRemoteRepository())
                                prop.children [
                                    Html.span [
                                        prop.className "swt:iconify swt:fluent--open-24-regular swt:size-4"
                                    ]
                                ]
                            ]
                            Html.button [
                                prop.testId "GitSidebarRefreshButton"
                                prop.className "swt:btn swt:btn-ghost swt:btn-square swt:btn-sm swt:shrink-0"
                                prop.disabled props.IsBusy
                                prop.title "Refresh git status"
                                prop.onClick (fun _ -> props.OnRefresh())
                                prop.children [
                                    Html.span [
                                        prop.className "swt:iconify swt:fluent--arrow-clockwise-24-regular swt:size-4"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private BranchStatusDetails(props: BranchHeaderProps) =
        React.Fragment [
            Html.div [
                prop.className "swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                prop.children [
                    Html.div [
                        prop.className
                            "swt:min-w-0 swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-200/60 swt:p-3 swt:@max-xs:p-2"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                                prop.children [
                                    Html.span [
                                        prop.className "swt:badge swt:badge-primary swt:badge-sm"
                                        prop.text (if props.Status.IsClean then "Clean" else "Changes")
                                    ]

                                    if props.HasConflicts then
                                        Html.span [
                                            prop.className "swt:badge swt:badge-warning swt:badge-sm"
                                            prop.text "Merge in progress"
                                        ]

                                    if props.Status.Ahead > 0 then
                                        let label =
                                            $"{GitSidebarInternal.commitCountLabel props.Status.Ahead} ahead of remote"

                                        Html.span [
                                            prop.testId "GitSidebarAheadBadge"
                                            prop.className "swt:badge swt:badge-outline swt:badge-sm swt:gap-0.5"
                                            prop.title label
                                            prop.children [
                                                Html.span [
                                                    prop.ariaHidden true
                                                    prop.className
                                                        "swt:iconify swt:fluent--arrow-up-20-regular swt:size-3 swt:shrink-0"
                                                ]
                                                Html.span [
                                                    prop.testId "GitSidebarAheadCount"
                                                    prop.ariaHidden true
                                                    prop.text props.Status.Ahead
                                                ]
                                                Html.span [ prop.className "swt:sr-only"; prop.text label ]
                                            ]
                                        ]

                                    if props.Status.Behind > 0 then
                                        let label =
                                            $"{GitSidebarInternal.commitCountLabel props.Status.Behind} behind remote"

                                        Html.span [
                                            prop.testId "GitSidebarBehindBadge"
                                            prop.className "swt:badge swt:badge-outline swt:badge-sm swt:gap-0.5"
                                            prop.title label
                                            prop.children [
                                                Html.span [
                                                    prop.ariaHidden true
                                                    prop.className
                                                        "swt:iconify swt:fluent--arrow-down-20-regular swt:size-3 swt:shrink-0"
                                                ]
                                                Html.span [
                                                    prop.testId "GitSidebarBehindCount"
                                                    prop.ariaHidden true
                                                    prop.text props.Status.Behind
                                                ]
                                                Html.span [ prop.className "swt:sr-only"; prop.text label ]
                                            ]
                                        ]
                                ]
                            ]

                            match props.Status.TrackingBranch with
                            | Some trackingBranch ->
                                Html.div [
                                    prop.className
                                        "swt:mt-2 swt:flex swt:min-w-0 swt:items-start swt:gap-2 swt:text-xs swt:text-base-content/70"
                                    prop.children [
                                        Html.span [
                                            prop.className
                                                "swt:iconify swt:fluent--cloud-24-regular swt:size-4 swt:shrink-0"
                                        ]
                                        Html.span [
                                            prop.className "swt:min-w-0 swt:wrap-anywhere"
                                            prop.text $"Tracking {trackingBranch}"
                                        ]
                                    ]
                                ]
                            | None ->
                                match props.Status.CurrentBranch with
                                | Some currentBranch ->
                                    Html.div [
                                        prop.className
                                            "swt:mt-2 swt:flex swt:min-w-0 swt:items-start swt:gap-2 swt:text-xs swt:text-base-content/70"
                                        prop.children [
                                            Html.span [
                                                prop.className
                                                    "swt:iconify swt:fluent--branch-request-20-regular swt:size-4 swt:shrink-0"
                                            ]
                                            Html.span [
                                                prop.className "swt:min-w-0 swt:wrap-anywhere"
                                                prop.text (
                                                    if props.HasRemote then
                                                        $"No upstream configured yet. Push will publish and track origin/{currentBranch}."
                                                    else
                                                        "Not published yet. Save to create the online ARC."
                                                )
                                            ]
                                        ]
                                    ]
                                | None -> Html.none
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private LfsSettingsSection(props: LfsSettingsSectionProps) =
        Html.div [
            prop.className
                "swt:mt-3 swt:min-w-0 swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-100 swt:p-3 swt:@max-xs:p-2"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:items-center swt:gap-2"
                    prop.children [
                        Html.span [
                            prop.className
                                "swt:iconify swt:fluent--document-arrow-right-24-regular swt:size-4 swt:shrink-0"
                        ]
                        Html.span [
                            prop.className "swt:min-w-0 swt:truncate swt:text-sm swt:font-medium"
                            prop.text "Git LFS auto-track threshold"
                        ]
                    ]
                ]
                Html.p [
                    prop.className "swt:mt-2 swt:wrap-break-word swt:text-xs swt:text-base-content/70"
                    prop.text
                        "Files larger than this limit are automatically re-staged through Git LFS during save operations."
                ]
                Html.div [
                    prop.className "swt:mt-3 swt:flex swt:flex-wrap swt:items-end swt:gap-2"
                    prop.children [
                        Html.label [
                            prop.className "swt:flex swt:min-w-40 swt:flex-1 swt:flex-col swt:gap-2 swt:@max-xs:min-w-0"
                            prop.children [
                                Html.span [
                                    prop.className "swt:text-xs swt:font-medium swt:text-base-content/70"
                                    prop.text "Threshold (MB)"
                                ]
                                Html.input [
                                    prop.testId "GitSidebarLfsThresholdInput"
                                    prop.className "swt:input swt:input-bordered swt:w-full swt:min-w-0"
                                    prop.type'.number
                                    prop.custom ("step", "1")
                                    prop.custom ("min", string GitSettingRanges.MinLfsAutoTrackThresholdMb)
                                    prop.custom ("max", string GitSettingRanges.MaxLfsAutoTrackThresholdMb)
                                    prop.disabled props.IsBusy
                                    prop.value props.LfsThresholdInput
                                    prop.onChange props.SetLfsThresholdInput
                                ]
                            ]
                        ]
                        Html.button [
                            prop.testId "GitSidebarLfsThresholdSaveButton"
                            prop.className
                                "swt:btn swt:btn-sm swt:btn-outline swt:min-w-0 swt:gap-2 swt:overflow-hidden swt:px-2 swt:normal-case swt:@max-xs:justify-center swt:@max-xs:gap-0"
                            prop.disabled (not props.CanSaveLfsThreshold)
                            prop.title "Save Threshold"
                            prop.onClick (fun _ -> props.SubmitLfsThreshold())
                            prop.children [
                                Html.span [
                                    prop.className "swt:iconify swt:fluent--save-24-regular swt:size-4 swt:shrink-0"
                                ]
                                Html.span [
                                    prop.className "swt:min-w-0 swt:truncate swt:@max-xs:sr-only"
                                    prop.text "Save Threshold"
                                ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:mt-2 swt:wrap-break-word swt:text-xs swt:text-base-content/60"
                    prop.text "Threshold setting: 1-100 MB. This does not cap LFS-tracked file size."
                ]
            ]
        ]

    [<ReactComponent>]
    static member private DiffIndexingLimitSection(props: DiffIndexingLimitSectionProps) =
        Html.div [
            prop.className
                "swt:mt-3 swt:min-w-0 swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-100 swt:p-3 swt:@max-xs:p-2"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:items-center swt:gap-2"
                    prop.children [
                        Html.span [
                            prop.className "swt:iconify swt:fluent--data-usage-24-regular swt:size-4 swt:shrink-0"
                        ]
                        Html.span [
                            prop.className "swt:min-w-0 swt:truncate swt:text-sm swt:font-medium"
                            prop.text "Background indexing limit"
                        ]
                    ]
                ]
                Html.p [
                    prop.className "swt:mt-2 swt:wrap-break-word swt:text-xs swt:text-base-content/70"
                    prop.testId "GitSidebarDiffIndexingLimitHelp"
                    prop.text
                        "1-63: the diff stays in memory with this budget in MB. 64 and up: temp files, background reading stops after this many MB."
                ]
                Html.div [
                    prop.className "swt:mt-3 swt:flex swt:flex-wrap swt:items-end swt:gap-2"
                    prop.children [
                        Html.label [
                            prop.className "swt:flex swt:min-w-40 swt:flex-1 swt:flex-col swt:gap-2 swt:@max-xs:min-w-0"
                            prop.children [
                                Html.span [
                                    prop.className "swt:text-xs swt:font-medium swt:text-base-content/70"
                                    prop.text "Limit (MB)"
                                ]
                                Html.input [
                                    prop.testId "GitSidebarDiffIndexingLimitInput"
                                    prop.className "swt:input swt:input-bordered swt:w-full swt:min-w-0"
                                    prop.type'.number
                                    prop.custom ("step", "1")
                                    prop.custom ("min", string GitSettingRanges.MinDiffIndexingLimitMb)
                                    prop.custom ("max", string GitSettingRanges.MaxDiffIndexingLimitMb)
                                    prop.disabled props.IsBusy
                                    prop.value props.DiffIndexingLimitInput
                                    prop.onChange props.SetDiffIndexingLimitInput
                                ]
                            ]
                        ]
                        Html.button [
                            prop.testId "GitSidebarDiffIndexingLimitSaveButton"
                            prop.className
                                "swt:btn swt:btn-sm swt:btn-outline swt:min-w-0 swt:gap-2 swt:overflow-hidden swt:px-2 swt:normal-case swt:@max-xs:justify-center swt:@max-xs:gap-0"
                            prop.disabled (not props.CanSaveDiffIndexingLimit)
                            prop.title "Save Limit"
                            prop.onClick (fun _ -> props.SubmitDiffIndexingLimit())
                            prop.children [
                                Html.span [
                                    prop.className "swt:iconify swt:fluent--save-24-regular swt:size-4 swt:shrink-0"
                                ]
                                Html.span [
                                    prop.className "swt:min-w-0 swt:truncate swt:@max-xs:sr-only"
                                    prop.text "Save Limit"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private AdvancedActions(props: AdvancedActionsProps) =
        React.Fragment [
            GitSidebar.SectionHeader("Actions", None)

            Html.div [
                prop.className "swt:grid swt:grid-cols-2 swt:gap-2 swt:px-3 swt:@max-xs:gap-1 swt:@max-xs:px-2"
                prop.children [
                    GitSidebar.ActionButton(
                        "Download Changes",
                        "swt:fluent--arrow-download-24-regular",
                        props.IsBusy || not props.RemoteActionsEnabled,
                        props.SubmitUpdateFromOnline,
                        testId = "GitSidebarPullButton",
                        tooltipText =
                            "Download Changes:\n- git fetch origin\n- git merge-tree (conflict preflight)\n- git merge origin/<branch>\n- git lfs fetch origin <branch> and git lfs checkout (when Download Large Files is on)"
                    )
                    GitSidebar.ActionButton(
                        "Upload Changes",
                        "swt:fluent--arrow-upload-24-regular",
                        props.IsBusy || not props.RemoteActionsEnabled,
                        props.SubmitPush,
                        testId = "GitSidebarPushButton",
                        tooltipText =
                            "Upload Changes:\n- git fetch origin\n- git merge-tree (conflict preflight)\n- git merge origin/<branch> (when the online copy is ahead)\n- git lfs fetch origin <branch> and git lfs checkout (after a merge, when Download Large Files is on)\n- git lfs push --object-id origin --stdin (when there are new large files)\n- git push origin"
                    )
                ]
            ]

            Html.div [
                prop.className "swt:px-3 swt:pt-2 swt:@max-xs:px-2 swt:@max-xs:pt-1"
                prop.children [
                    GitSidebar.ActionButton(
                        "More Git Actions",
                        (if props.IsAdvancedActionsOpen then
                             "swt:fluent--chevron-up-24-regular"
                         else
                             "swt:fluent--chevron-down-24-regular"),
                        props.IsBusy,
                        props.ToggleAdvancedActions,
                        isActive = props.IsAdvancedActionsOpen,
                        testId = "GitSidebarAdvancedActionsButton"
                    )
                ]
            ]

            match props.RemoteActionsWarning with
            | Some warning when not props.RemoteActionsEnabled ->
                Html.div [
                    prop.className "swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            prop.testId "GitSidebarRemoteAuthWarning"
                            prop.className
                                "swt:alert swt:alert-warning swt:min-w-0 swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                            prop.children [
                                Html.span [
                                    prop.className
                                        "swt:iconify swt:fluent--warning-shield-20-regular swt:size-4 swt:shrink-0"
                                ]
                                Html.span [
                                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                                    prop.text warning
                                ]
                            ]
                        ]
                    ]
                ]
            | _ -> Html.none

            if props.IsAdvancedActionsOpen then
                Html.div [
                    prop.className "swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                    prop.children [
                        Html.div [
                            prop.testId "GitSidebarAdvancedActionsDivider"
                            prop.className "swt:mb-3 swt:border-t swt:border-base-content/10"
                        ]
                        Html.div [
                            prop.className "swt:grid swt:grid-cols-2 swt:gap-2 swt:@max-xs:gap-1"
                            prop.children [
                                GitSidebar.ActionButton(
                                    "Check for Changes",
                                    "swt:fluent--arrow-sync-24-regular",
                                    props.IsBusy || not props.RemoteActionsEnabled,
                                    props.SubmitFetch,
                                    testId = "GitSidebarFetchButton",
                                    tooltipText = "Check for Changes:\n- git fetch origin"
                                )
                                GitSidebar.ActionButton(
                                    "Create Work Copy",
                                    "swt:fluent--branch-fork-24-regular",
                                    props.IsBusy,
                                    props.OpenCreateBranchModal,
                                    testId = "GitSidebarCreateBranchButton",
                                    tooltipText = "Create Work Copy:\n- git checkout -b <name>"
                                )
                                GitSidebar.ActionButton(
                                    "Switch To Work Copy",
                                    "swt:fluent--arrow-swap-24-regular",
                                    props.IsBusy || not props.CanSwitchBranch,
                                    props.OpenSwitchBranchModal,
                                    testId = "GitSidebarSwitchBranchButton",
                                    tooltipText = "Switch To Work Copy:\n- git checkout <branch>"
                                )
                                GitSidebar.ActionButton(
                                    "Clean LFS Cache",
                                    "swt:fluent--broom-24-regular",
                                    props.IsBusy,
                                    props.SubmitPruneLfsCache,
                                    testId = "GitSidebarLfsPruneButton",
                                    tooltipText =
                                        "Clean LFS Cache:\n- git lfs prune --verify-remote --no-verify-unreachable --when-unverified=halt"
                                )
                                GitSidebar.ActionButton(
                                    "Reduce LFS Storage",
                                    "swt:fluent--folder-sync-24-regular",
                                    props.IsBusy,
                                    props.SubmitDedupLfsStorage,
                                    testId = "GitSidebarLfsDedupButton",
                                    tooltipText = "Reduce LFS Storage:\n- git lfs dedup"
                                )
                            ]
                        ]
                        Html.div [
                            prop.className "swt:mt-3"
                            prop.children [
                                GitSidebar.DownloadLargeFilesToggle(
                                    props.DownloadLargeFilesInput,
                                    props.IsBusy,
                                    props.SubmitDownloadLargeFiles,
                                    testId = "GitSidebarDownloadLargeFilesCheckbox"
                                )
                            ]
                        ]
                        GitSidebar.LfsSettingsSection(props.LfsSettings)
                        GitSidebar.DiffIndexingLimitSection(props.DiffIndexingLimit)
                    ]
                ]
            else
                Html.none
        ]

    [<ReactComponent>]
    static member private CommitSaveButton
        (testId: string, label: string, iconClassName: string, isFilled: bool, isDisabled: bool, onClick: unit -> unit)
        =
        Html.button [
            prop.testId testId
            prop.className [
                "swt:btn swt:btn-sm swt:btn-success swt:min-w-0 swt:flex-1 swt:basis-0 swt:gap-2 swt:overflow-hidden swt:px-2 swt:normal-case"
                "swt:@max-3xs/gitSidebar:gap-0 swt:@max-3xs/gitSidebar:px-0"
                if not isFilled then
                    "swt:btn-outline"
            ]
            prop.disabled isDisabled
            prop.ariaLabel label
            prop.title label
            prop.onClick (fun _ -> onClick ())
            prop.children [
                Html.span [
                    prop.className [ "swt:iconify"; iconClassName; "swt:size-4 swt:shrink-0" ]
                ]
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate swt:@max-3xs/gitSidebar:sr-only"
                    prop.text label
                ]
            ]
        ]

    [<ReactComponent>]
    static member private CommitSection(props: CommitSectionProps) =
        React.Fragment [
            GitSidebar.SectionHeader("Save", None)

            Html.div [
                prop.testId "GitSidebarCommitSection"
                prop.className "swt:px-3 swt:pb-3 swt:@max-xs:px-2"
                prop.children [
                    Html.label [
                        prop.className "swt:flex swt:flex-col swt:gap-2"
                        prop.children [
                            Html.textarea [
                                prop.testId "GitSidebarCommitMessageInput"
                                prop.className
                                    "swt:textarea swt:textarea-bordered swt:min-h-24 swt:w-full swt:min-w-0 swt:resize-y swt:@max-xs:text-sm"
                                prop.disabled (not props.CanEditCommit)
                                prop.value props.CommitMessage
                                prop.placeholder (
                                    if props.HasConflicts then
                                        "Resolve merge conflicts before saving."
                                    elif props.Status.IsClean then
                                        "No changes to save."
                                    else
                                        "Describe your changes"
                                )
                                prop.onChange props.SetCommitMessage
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className
                            "swt:mt-2 swt:flex swt:min-w-0 swt:flex-wrap swt:items-center swt:justify-between swt:gap-2 swt:text-xs swt:text-base-content/60"
                        prop.children [
                            Html.span [
                                prop.className "swt:min-w-0 swt:wrap-anywhere"
                                prop.text (
                                    if props.MarkedCount = 1 then
                                        "1 file marked to save"
                                    else
                                        $"{props.MarkedCount} files marked to save"
                                )
                            ]
                            if not props.CanEditCommit && props.HasConflicts then
                                Html.span [
                                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                                    prop.text "Saving files is disabled while conflicts remain."
                                ]
                            elif not props.CanEditCommit && props.Status.IsClean then
                                Html.span [
                                    prop.className "swt:min-w-0 swt:wrap-anywhere"
                                    prop.text "No changes available to save."
                                ]
                            else
                                Html.none
                        ]
                    ]
                    Html.div [
                        prop.className "swt:mt-3 swt:flex swt:min-w-0 swt:gap-2"
                        prop.children [
                            // Save All keeps the left edge and the filled style. Clicking a row marks it,
                            // so the outlined Save Selected reads as the second choice.
                            GitSidebar.CommitSaveButton(
                                "GitSidebarSaveAllButton",
                                "Save All Changes",
                                "swt:fluent--checkmark-circle-24-regular",
                                true,
                                not props.CanRunPrimarySave,
                                props.SubmitSaveAll
                            )

                            if props.HasMarkedFiles then
                                GitSidebar.CommitSaveButton(
                                    "GitSidebarSaveSelectedButton",
                                    "Save Selected Changes",
                                    "swt:fluent--document-checkmark-24-regular",
                                    false,
                                    not props.CanRunPrimarySave,
                                    props.SubmitSaveSelected
                                )
                        ]
                    ]
                    Html.label [
                        prop.className "swt:mt-2 swt:flex swt:min-w-0 swt:items-center swt:gap-2 swt:text-xs"
                        prop.title
                            "When unchecked, changes are saved on this computer only and not uploaded to the online repository."
                        prop.children [
                            Html.input [
                                prop.testId "GitSidebarUploadRightAwayCheckbox"
                                prop.className "swt:checkbox swt:checkbox-xs swt:shrink-0"
                                prop.type'.checkbox
                                prop.disabled (not props.CanEditCommit)
                                prop.isChecked props.UploadRightAway
                                prop.onChange props.SetUploadRightAway
                            ]
                            Html.span [
                                prop.className "swt:min-w-0 swt:truncate"
                                prop.text "Upload right away"
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ChangeStatusTooltip(index: int, change: GitSidebarChange) =
        let presentation = GitSidebarInternal.changePresentation change
        let gitReturn = GitSidebarInternal.describeChange change
        let tooltipText = $"{presentation.Label}:\n- {gitReturn}"
        let stopRowActivation (event: MouseEvent) = event.stopPropagation ()

        Html.span [
            prop.testId $"GitSidebarChangeStatusIcon-{index}"
            prop.className [
                "swt:shrink-0 swt:text-center"
                presentation.ToneClassName
            ]
            prop.text presentation.Shorthand
            prop.title tooltipText
            prop.ariaLabel tooltipText
            prop.onMouseDown stopRowActivation
            prop.onClick stopRowActivation
        ]

    [<ReactComponent>]
    static member private ChangedFileRow(props: ChangedFileRowProps) =
        let change = props.Change
        let fileName, directory = GitSidebarInternal.splitPath change.Path

        let activateRow ctrlKey shiftKey =
            props.UpdateMarkedSelection change ctrlKey shiftKey
            props.OpenChange change

        Html.div [
            prop.role "listitem"
            prop.custom ("data-index", props.Index)
            prop.ref (fun element -> props.MeasureElementRef(Option.ofObj element))
            prop.className "swt:absolute swt:left-0 swt:w-full"
            prop.style [
                style.top 0
                style.left 0
                style.width (length.percent 100)
                style.custom ("transform", $"translateY({props.VirtualStart}px)")
            ]
            prop.children [
                Html.div [
                    prop.testId $"GitSidebarChangeRow-{props.Index}"
                    prop.custom ("data-index", props.Index)
                    prop.role "button"
                    prop.tabIndex 0
                    prop.title change.Path
                    prop.className [
                        "swt:group swt:flex swt:min-h-6 swt:w-full swt:min-w-0 swt:items-center swt:gap-2 swt:rounded-box swt:border swt:px-2 swt:text-left swt:transition-colors swt:select-none swt:@max-xs:gap-1.5"
                        "swt:cursor-pointer"
                        if change.IsConflicted then
                            "swt:border-error/40 swt:bg-error/5 hover:swt:bg-error/10"
                        elif props.IsMarked then
                            "swt:border-success/40 swt:bg-success/10 hover:swt:bg-success/15"
                        else
                            "swt:border-base-content/10 swt:bg-base-100 hover:swt:bg-base-200/80"
                    ]
                    prop.onClick (fun (event: MouseEvent) ->
                        activateRow (event.ctrlKey || event.metaKey) event.shiftKey
                    )
                    prop.onKeyDown (fun (event: KeyboardEvent) ->
                        if event.key = "Enter" || event.key = " " then
                            event.preventDefault ()
                            activateRow (event.ctrlKey || event.metaKey) event.shiftKey
                    )
                    prop.children [
                        Html.div [
                            prop.className "swt:min-w-0 swt:flex-1"
                            prop.children [
                                Html.div [
                                    prop.className "swt:min-w-0"
                                    prop.children [
                                        // One truncating line: the ellipsis cuts the directory first and the file name only when it does not fit alone.
                                        Html.span [
                                            prop.className
                                                "swt:block swt:min-w-0 swt:truncate swt:text-sm swt:@max-xs:text-xs"
                                            prop.children [
                                                Html.span [
                                                    prop.testId $"GitSidebarChangeName-{props.Index}"
                                                    prop.text fileName
                                                ]
                                                match directory with
                                                | Some directory ->
                                                    // The space keeps assistive tech from reading name and directory as one word.
                                                    React.Fragment [
                                                        Html.text " "
                                                        Html.span [
                                                            prop.testId $"GitSidebarChangeDirectory-{props.Index}"
                                                            prop.className
                                                                "swt:ml-1 swt:text-xs swt:text-base-content/60"
                                                            prop.text directory
                                                        ]
                                                    ]
                                                | None -> Html.none
                                            ]
                                        ]
                                        match change.OriginalPath with
                                        | Some originalPath ->
                                            Html.div [
                                                prop.testId $"GitSidebarChangeRenamedFrom-{props.Index}"
                                                prop.className
                                                    "swt:mt-0.5 swt:truncate swt:text-xs swt:text-base-content/60"
                                                prop.title originalPath
                                                prop.text $"Renamed from {originalPath}"
                                            ]
                                        | None -> Html.none
                                    ]
                                ]
                            ]
                        ]
                        Html.div [
                            prop.testId $"GitSidebarChangeStatusSlot-{props.Index}"
                            prop.className
                                "swt:ml-auto swt:flex swt:shrink-0 swt:items-center swt:gap-1 swt:self-center"
                            prop.children [
                                let discardLabel =
                                    if props.DiscardPaths.Length = 1 then
                                        "Discard change"
                                    else
                                        $"Discard {props.DiscardPaths.Length} selected changes"

                                // The row offers no discard for a conflicted file, since discarding it would drop one side of the merge.
                                if not change.IsConflicted && props.DiscardPaths.Length > 0 then
                                    Html.button [
                                        prop.testId $"GitSidebarDiscardChangeButton-{props.Index}"
                                        prop.type'.button
                                        prop.className
                                            "swt:btn swt:btn-ghost swt:btn-square swt:btn-xs swt:opacity-0 swt:transition-opacity swt:group-hover:opacity-100 swt:focus:opacity-100"
                                        prop.ariaLabel discardLabel
                                        prop.title discardLabel
                                        prop.disabled props.IsBusy
                                        prop.onClick (fun (event: MouseEvent) ->
                                            event.preventDefault ()
                                            event.stopPropagation ()
                                            props.DiscardChanges props.DiscardPaths
                                        )
                                        prop.children [
                                            Html.span [
                                                prop.className
                                                    "swt:iconify swt:fluent--arrow-undo-24-regular swt:size-4 swt:text-error"
                                            ]
                                        ]
                                    ]

                                GitSidebar.ChangeStatusTooltip(props.Index, change)
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ChangedFilesList(props: ChangedFilesListProps) =
        let scrollContainerRef: IRefValue<HTMLElement option> = React.useElementRef ()
        let itemSize = 28
        let itemGap = 4
        let overscan = 8

        let discardPathsForChange (change: GitSidebarChange) =
            let conflictedPaths =
                props.ChangedFiles
                |> Array.filter _.IsConflicted
                |> Array.map _.Path
                |> Set.ofArray

            let paths =
                if
                    Set.contains change.Path props.MarkedPaths
                    && not (Set.isEmpty props.MarkedPaths)
                then
                    props.MarkedPaths |> Set.toArray |> Array.sort
                else
                    [| change.Path |]

            paths |> Array.filter (fun path -> not (Set.contains path conflictedPaths))

        let changedFileListVirtualizer =
            Virtual.useVirtualizer (
                count = props.ChangedFiles.Length,
                getScrollElement = (fun () -> scrollContainerRef.current),
                estimateSize = (fun _ -> itemSize),
                overscan = overscan,
                gap = itemGap
            )

        let virtualItems =
            let measuredItems = changedFileListVirtualizer.getVirtualItems ()

            if measuredItems.Length = 0 && props.ChangedFiles.Length > 0 then
                let isInitialScrollPosition =
                    scrollContainerRef.current
                    |> Option.map (fun element -> element.scrollTop = 0.0)
                    |> Option.defaultValue true

                if not isInitialScrollPosition then
                    [||]
                else
                    [| 0 .. min (props.ChangedFiles.Length - 1) overscan |]
                    |> Array.map (fun index -> {
                        Index = index
                        Start = index * (itemSize + itemGap)
                    })
            else
                measuredItems
                |> Array.map (fun item -> {
                    Index = item.index
                    Start = item.start
                })

        React.Fragment [
            GitSidebar.SectionHeader(
                "Changes",
                Some(
                    if props.ChangedFiles.Length = 1 then
                        "1 file"
                    else
                        $"{props.ChangedFiles.Length} files"
                )
            )

            Html.div [
                prop.testId "GitSidebarChangedFilesScrollContainer"
                prop.role "region"
                prop.ariaLabel "Changed files"
                prop.ref scrollContainerRef
                prop.className
                    "swt:min-h-26 swt:min-w-0 swt:flex-1 swt:overflow-y-auto swt:overflow-x-hidden swt:px-2 swt:pb-2 swt:@max-xs:px-1"
                prop.children [
                    if props.ChangedFiles.Length = 0 then
                        Html.div [
                            prop.className
                                "swt:mt-2 swt:min-w-0 swt:wrap-break-word swt:rounded-box swt:border swt:border-dashed swt:border-base-content/15 swt:bg-base-200/40 swt:px-4 swt:py-6 swt:text-sm swt:text-base-content/60 swt:@max-xs:px-2"
                            prop.text (
                                if
                                    props.Status.TrackingBranch.IsSome
                                    && props.Status.Ahead = 0
                                    && props.Status.Behind = 0
                                then
                                    "No changed files. Your repository is in sync."
                                else
                                    "No changed files."
                            )
                        ]
                    else
                        Html.div [
                            prop.testId "GitSidebarChangedFilesVirtualContent"
                            prop.role "list"
                            prop.className "swt:relative swt:mt-1 swt:min-w-0"
                            prop.style [
                                style.height (changedFileListVirtualizer.getTotalSize ())
                            ]
                            prop.children [
                                for virtualItem in virtualItems do
                                    let change = props.ChangedFiles.[virtualItem.Index]

                                    let isMarked = Set.contains change.Path props.MarkedPaths
                                    let discardPaths = discardPathsForChange change

                                    React.KeyedFragment(
                                        change.Path,
                                        [
                                            GitSidebar.ChangedFileRow(
                                                {
                                                    Change = change
                                                    Index = virtualItem.Index
                                                    IsMarked = isMarked
                                                    IsBusy = props.IsBusy
                                                    DiscardPaths = discardPaths
                                                    DiscardChanges = props.DiscardChanges
                                                    UpdateMarkedSelection = props.UpdateMarkedSelection
                                                    OpenChange = props.OpenChange
                                                    VirtualStart = virtualItem.Start
                                                    MeasureElementRef = changedFileListVirtualizer.measureElement
                                                }
                                            )
                                        ]
                                    )
                            ]
                        ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private Modals(props: ModalsProps) =
        React.Fragment [
            BaseModal.Modal(
                isOpen = props.IsCreateBranchModalOpen,
                setIsOpen = props.SetCreateBranchModalOpen,
                header = Html.text "Create Branch From",
                description = Html.text "Create a new branch and optionally base it on a local or remote ref.",
                debug = "GitSidebarCreateBranchModal",
                children =
                    Html.div [
                        prop.testId "GitSidebarCreateBranchModal"
                        prop.className "swt:flex swt:flex-col swt:gap-3"
                        prop.children [
                            Html.label [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.span [
                                        prop.className "swt:text-sm swt:font-medium"
                                        prop.text "Branch name"
                                    ]
                                    Html.input [
                                        prop.testId "GitSidebarBranchNameInput"
                                        prop.className "swt:input swt:input-bordered swt:w-full"
                                        prop.value props.BranchName
                                        prop.placeholder "feature/my-change"
                                        prop.onChange props.SetBranchName
                                    ]
                                ]
                            ]
                            Html.label [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.span [
                                        prop.className "swt:text-sm swt:font-medium"
                                        prop.text "Start point"
                                    ]
                                    Html.select [
                                        prop.testId "GitSidebarStartPointSelect"
                                        prop.className "swt:select swt:select-bordered swt:w-full"
                                        prop.value (props.SelectedStartPoint |> Option.defaultValue "")
                                        prop.onChange (fun nextValue ->
                                            if String.IsNullOrWhiteSpace nextValue then
                                                props.SetSelectedStartPoint None
                                            else
                                                props.SetSelectedStartPoint(Some nextValue)
                                        )
                                        prop.children [
                                            for startPoint, label in props.BranchOptionsWithHead do
                                                Html.option [
                                                    prop.key (defaultArg startPoint "")
                                                    prop.value (defaultArg startPoint "")
                                                    prop.text label
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ],
                footer =
                    React.Fragment [
                        Html.button [
                            prop.className "swt:btn swt:btn-ghost"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> props.CloseDialog())
                        ]
                        Html.button [
                            prop.testId "GitSidebarCreateBranchSubmit"
                            prop.className "swt:btn swt:btn-primary swt:ml-auto"
                            prop.text "Create Branch"
                            prop.onClick (fun _ -> props.SubmitCreateBranch())
                        ]
                    ]
            )

            BaseModal.Modal(
                isOpen = props.IsSwitchBranchModalOpen,
                setIsOpen = props.SetSwitchBranchModalOpen,
                header = Html.text "Switch Branch",
                description = Html.text "Switch to an existing branch.",
                debug = "GitSidebarSwitchBranchModal",
                children =
                    Html.div [
                        prop.testId "GitSidebarSwitchBranchModal"
                        prop.className "swt:flex swt:flex-col swt:gap-3"
                        prop.children [
                            Html.label [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.span [
                                        prop.className "swt:text-sm swt:font-medium"
                                        prop.text "Branch"
                                    ]
                                    Html.select [
                                        prop.testId "GitSidebarSwitchBranchSelect"
                                        prop.className "swt:select swt:select-bordered swt:w-full"
                                        prop.value (props.SelectedSwitchBranch |> Option.defaultValue "")
                                        prop.onChange (fun (nextValue: string) ->
                                            if String.IsNullOrWhiteSpace nextValue then
                                                props.SetSelectedSwitchBranch None
                                            else
                                                props.SetSelectedSwitchBranch(Some nextValue)
                                        )
                                        prop.children [
                                            for branch in props.BranchOptionsForSwitch do
                                                Html.option [
                                                    prop.key branch.RefName
                                                    prop.value branch.RefName
                                                    prop.text
                                                        $"{branch.DisplayLabel} ({GitSidebarInternal.branchKindLabel branch.Kind})"
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ],
                footer =
                    React.Fragment [
                        Html.button [
                            prop.className "swt:btn swt:btn-ghost"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> props.CloseDialog())
                        ]
                        Html.button [
                            prop.testId "GitSidebarSwitchBranchSubmit"
                            prop.className "swt:btn swt:btn-primary swt:ml-auto"
                            prop.disabled (props.BranchOptionsForSwitch.Length = 0)
                            prop.text "Switch Branch"
                            prop.onClick (fun _ -> props.SubmitSwitchBranch())
                        ]
                    ]
            )

            BaseModal.Modal(
                isOpen = props.IsMissingMessageModalOpen,
                setIsOpen = props.SetMissingMessageModalOpen,
                header = Html.text "Missing save message",
                children = Html.p [ prop.text "Please add a description." ],
                footer =
                    Html.button [
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.text "OK"
                        prop.onClick (fun _ -> props.SetMissingMessageModalOpen false)
                    ],
                debug = "GitSidebarMissingMessage"
            )
        ]

    [<ReactComponent>]
    static member private PendingRemoteActionDialog(props: PendingRemoteActionDialogProps) =
        BaseModal.Modal(
            isOpen = props.PendingConfirmation.IsSome,
            setIsOpen =
                (fun isOpen ->
                    if not isOpen then
                        props.CancelPendingRemoteAction()
                ),
            header = Html.text (props.PendingConfirmation |> Option.map _.Title |> Option.defaultValue "Confirm"),
            children =
                Html.p [
                    prop.className "swt:whitespace-pre-wrap"
                    prop.text (props.PendingConfirmation |> Option.map _.Message |> Option.defaultValue "")
                ],
            footer =
                React.Fragment [
                    Html.button [
                        prop.className "swt:btn"
                        prop.text (
                            props.PendingConfirmation
                            |> Option.map _.CancelLabel
                            |> Option.defaultValue "Cancel"
                        )
                        prop.onClick (fun _ -> props.CancelPendingRemoteAction())
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.text (
                            props.PendingConfirmation
                            |> Option.map _.ConfirmLabel
                            |> Option.defaultValue "Continue"
                        )
                        prop.onClick (fun _ -> props.ConfirmPendingRemoteAction())
                    ]
                ],
            debug = "GitSidebarPendingRemoteAction"
        )

    [<ReactComponent>]
    static member private PublishRenameDialog(props: PublishRenameDialogProps) =
        let prompt = props.Prompt
        let normalizedInput = props.InputValue.Trim()
        let ownerGroupName = React.useId ()
        let owners = prompt |> Option.map _.Owners |> Option.defaultValue [||]
        let hasOwners = owners.Length > 0

        let selectedOwnerIsEnabled =
            owners
            |> Array.exists (fun owner -> owner.NamespaceId = props.SelectedNamespaceId && owner.DisabledReason.IsNone)

        let keepsName =
            match prompt with
            | Some prompt -> normalizedInput = prompt.CurrentName
            | None -> false

        BaseModal.Modal(
            isOpen = prompt.IsSome,
            setIsOpen =
                (fun isOpen ->
                    if not isOpen then
                        props.Cancel()
                ),
            header =
                Html.text (
                    if hasOwners then
                        "Upload to DataHUB"
                    else
                        "Rename Repository"
                ),
            description =
                Html.text (
                    prompt
                    |> Option.map _.Message
                    |> Option.defaultValue "Choose a different repository name."
                ),
            children =
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        Html.label [
                            prop.className "swt:flex swt:flex-col swt:gap-2"
                            prop.children [
                                Html.span [
                                    prop.className "swt:text-sm swt:font-medium"
                                    prop.text "Repository name"
                                ]
                                Html.input [
                                    prop.testId "GitSidebarPublishRenameInput"
                                    prop.className "swt:input swt:input-bordered swt:w-full"
                                    prop.disabled props.IsBusy
                                    prop.value props.InputValue
                                    prop.onChange props.SetInputValue
                                ]
                            ]
                        ]
                        if hasOwners then
                            Html.p [
                                prop.className "swt:text-xs swt:text-base-content/60"
                                prop.text "Changing the name also renames the ARC folder."
                            ]
                        if hasOwners then
                            Html.fieldSet [
                                prop.className "swt:fieldset"
                                prop.children [
                                    Html.legend [ prop.className "swt:fieldset-legend"; prop.text "Owner" ]
                                    for owner in owners do
                                        let ownerKey = GitSidebarInternal.publishOwnerKey owner
                                        let reasonId = $"{ownerGroupName}-reason-{ownerKey}"

                                        Html.div [
                                            prop.key ownerKey
                                            prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-x-2"
                                            prop.children [
                                                Html.label [
                                                    prop.className (
                                                        "swt:label swt:gap-2 "
                                                        + (if owner.DisabledReason.IsSome then
                                                               "swt:text-base-content/50 swt:cursor-not-allowed"
                                                           else
                                                               "swt:text-base-content")
                                                    )
                                                    prop.children [
                                                        Html.input [
                                                            prop.testId $"GitSidebarPublishOwner-{ownerKey}"
                                                            prop.type'.radio
                                                            prop.name ownerGroupName
                                                            prop.className
                                                                "swt:radio swt:radio-sm swt:disabled:opacity-60 swt:disabled:border-base-content/40"
                                                            prop.disabled (props.IsBusy || owner.DisabledReason.IsSome)
                                                            if owner.DisabledReason.IsSome then
                                                                prop.ariaDescribedBy reasonId
                                                            prop.isChecked (
                                                                owner.NamespaceId = props.SelectedNamespaceId
                                                            )
                                                            prop.onChange (fun (_: bool) ->
                                                                props.SetSelectedNamespaceId owner.NamespaceId
                                                            )
                                                        ]
                                                        Html.span [ prop.text owner.Label ]
                                                    ]
                                                ]
                                                match owner.DisabledReason with
                                                | Some reason ->
                                                    Html.span [
                                                        prop.id reasonId
                                                        prop.testId $"GitSidebarPublishOwnerReason-{ownerKey}"
                                                        prop.className "swt:text-xs swt:text-base-content/60"
                                                        prop.text reason
                                                    ]
                                                | None -> ()
                                            ]
                                        ]
                                ]
                            ]
                    ]
                ],
            footer =
                React.Fragment [
                    Html.button [
                        prop.className "swt:btn swt:btn-ghost"
                        prop.disabled props.IsBusy
                        prop.text "Cancel"
                        prop.onClick (fun _ -> props.Cancel())
                    ]
                    Html.button [
                        prop.testId "GitSidebarPublishRenameSubmit"
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.disabled (
                            props.IsBusy
                            || String.IsNullOrWhiteSpace normalizedInput
                            || (hasOwners && not selectedOwnerIsEnabled)
                            // Without an owner to change, the same name would only be refused again.
                            || (not hasOwners && keepsName)
                        )
                        prop.text (
                            if hasOwners && keepsName then
                                "Upload"
                            else
                                "Rename and Upload"
                        )
                        prop.onClick (fun _ -> props.Submit())
                    ]
                ],
            debug = "GitSidebarPublishRename"
        )

    [<ReactComponent(true)>]
    static member Main
        (
            status: GitSidebarStatus,
            changedFiles: GitSidebarChange[],
            branchOptions: GitSidebarBranchOption[],
            callbacks: GitSidebarCallbacks,
            downloadLargeFiles: bool,
            lfsAutoTrackThresholdMb: int,
            diffIndexingLimitMb: int,
            ?runStatus: GitSidebarRunStatus,
            ?selectedFile: string,
            ?errorNotice: string,
            ?warningNotice: string,
            ?pendingConfirmation: GitSidebarConfirmationDialog,
            ?publishRenamePrompt: GitSidebarPublishRenamePrompt,
            ?remoteActionsEnabled: bool,
            ?remoteActionsWarning: string,
            ?hasRemote: bool,
            ?canOpenRemoteRepository: bool,
            ?canCancelOperation: bool,
            ?onOpenRemoteRepository: unit -> unit,
            ?onSubmitPublishRename: GitSidebarPublishSubmit -> unit,
            ?onCancelPublishRename: unit -> unit,
            ?uploadRightAway: bool,
            ?onUploadRightAwayChange: bool -> unit,
            ?backgroundActivities: GitSidebarBackgroundActivity[],
            ?busyHeadlineOverride: string,
            ?operationSteps: GitSidebarStep[],
            ?operationLog: GitSidebarLog,
            ?lastLog: GitSidebarLastLog
        ) =

        let runStatus = defaultArg runStatus GitSidebarRunStatus.Idle
        let errorNotice = errorNotice
        let warningNotice = warningNotice
        let pendingConfirmation = pendingConfirmation
        let publishRenamePrompt = publishRenamePrompt
        let remoteActionsEnabled = defaultArg remoteActionsEnabled true
        let remoteActionsWarning = remoteActionsWarning
        let hasRemote = defaultArg hasRemote true
        let canOpenRemoteRepository = defaultArg canOpenRemoteRepository false
        let canCancelOperation = defaultArg canCancelOperation false
        let onOpenRemoteRepository = defaultArg onOpenRemoteRepository (fun () -> ())
        let onSubmitPublishRename = defaultArg onSubmitPublishRename (fun _ -> ())
        let onCancelPublishRename = defaultArg onCancelPublishRename (fun () -> ())
        let _selectedFileForCompatibility = selectedFile
        let onRefresh = callbacks.OnRefresh
        let onFetch = callbacks.OnFetch
        let onPush = callbacks.OnPush
        let onUpdateFromOnline = callbacks.OnUpdateFromOnline
        let onPrimarySaveSelection = callbacks.OnPrimarySaveSelection
        let onPrimarySaveAll = callbacks.OnPrimarySaveAll
        let onCommitSelection = callbacks.OnCommitSelection
        let onCommitAll = callbacks.OnCommitAll
        let onDiscardSelection = callbacks.OnDiscardSelection
        let onConfirmPendingRemoteAction = callbacks.OnConfirmPendingRemoteAction
        let onCancelPendingRemoteAction = callbacks.OnCancelPendingRemoteAction
        let onSaveDownloadLargeFiles = callbacks.OnSaveDownloadLargeFiles
        let onSaveLfsAutoTrackThreshold = callbacks.OnSaveLfsAutoTrackThreshold
        let onSaveDiffIndexingLimit = callbacks.OnSaveDiffIndexingLimit
        let onCreateBranch = callbacks.OnCreateBranch
        let onSwitchBranch = callbacks.OnSwitchBranch
        let onSelectChange = callbacks.OnSelectChange
        let onCancelOperation = callbacks.OnCancelOperation

        let localError, setLocalError = React.useState (None: string option)
        let activeDialog, setActiveDialog = React.useState ActiveDialog.None
        let branchName, setBranchName = React.useState ""
        let commitMessage, setCommitMessage = React.useState ""
        let isMissingMessageModalOpen, setMissingMessageModalOpen = React.useState false
        let localUploadRightAway, setLocalUploadRightAway = React.useState true
        let isUploadRightAwayControlled = uploadRightAway.IsSome
        let uploadRightAway = defaultArg uploadRightAway localUploadRightAway

        let setUploadRightAway (value: bool) =
            if not isUploadRightAwayControlled then
                setLocalUploadRightAway value

            onUploadRightAwayChange |> Option.iter (fun notify -> notify value)

        let publishRenameInput, setPublishRenameInput =
            React.useState (publishRenamePrompt |> Option.map _.CurrentName |> Option.defaultValue "")

        let publishNamespaceId, setPublishNamespaceId =
            React.useState (GitSidebarInternal.initialPublishNamespaceId publishRenamePrompt)

        let downloadLargeFilesInput, setDownloadLargeFilesInput =
            React.useState downloadLargeFiles

        let lfsThresholdInput, setLfsThresholdInput =
            React.useState (GitSidebarInternal.formatThresholdInput lfsAutoTrackThresholdMb)

        let diffIndexingLimitInput, setDiffIndexingLimitInput =
            React.useState (GitSidebarInternal.formatThresholdInput diffIndexingLimitMb)

        let markedPaths, setMarkedPaths = React.useStateWithUpdater Set.empty<string>

        let selectionAnchorPath, setSelectionAnchorPath =
            React.useStateWithUpdater (None: string option)

        let selectedStartPoint, setSelectedStartPoint = React.useState (None: string option)

        let selectedSwitchBranch, setSelectedSwitchBranch =
            React.useState (None: string option)

        let isAdvancedActionsOpen = activeDialog = ActiveDialog.AdvancedActions
        let isCreateBranchModalOpen = activeDialog = ActiveDialog.CreateBranch
        let isSwitchBranchModalOpen = activeDialog = ActiveDialog.SwitchBranch

        let setCreateBranchModalOpen isOpen =
            setActiveDialog (
                if isOpen then
                    ActiveDialog.CreateBranch
                else
                    ActiveDialog.None
            )

        let setSwitchBranchModalOpen isOpen =
            setActiveDialog (
                if isOpen then
                    ActiveDialog.SwitchBranch
                else
                    ActiveDialog.None
            )

        let isBusy =
            match runStatus with
            | GitSidebarRunStatus.Idle -> false
            | GitSidebarRunStatus.Busy _
            | GitSidebarRunStatus.BusyWithoutNotice
            | GitSidebarRunStatus.Progress _ -> true

        // A silent refresh disables the actions but leaves the commit message and the upload
        // checkbox editable, so typing is not interrupted when the sidebar opens.
        let isBusyWithVisibleOperation =
            match runStatus with
            | GitSidebarRunStatus.Busy _
            | GitSidebarRunStatus.Progress _ -> true
            | GitSidebarRunStatus.Idle
            | GitSidebarRunStatus.BusyWithoutNotice -> false

        let visibleError = errorNotice |> Option.orElse localError

        // Sync marked paths with changedFiles: remove paths that are no longer in the changed
        // files list (e.g., after a commit or discard operation updates the server-side file list).
        React.useEffect (
            (fun () ->
                let changedPathSet = changedFiles |> Array.map _.Path |> Set.ofArray

                setMarkedPaths (fun current -> current |> Set.filter (fun path -> Set.contains path changedPathSet))

                setSelectionAnchorPath (fun current ->
                    current |> Option.filter (fun path -> Set.contains path changedPathSet)
                )
            ),
            [| box changedFiles |]
        )

        // Reset the local downloadLargeFiles toggle when the prop changes
        // (e.g., after the server confirms or rejects a preference update).
        React.useEffect ((fun () -> setDownloadLargeFilesInput downloadLargeFiles), [| box downloadLargeFiles |])

        // Reset the local LFS threshold input when the prop changes
        // (same sync pattern as downloadLargeFiles above).
        React.useEffect (
            (fun () -> setLfsThresholdInput (GitSidebarInternal.formatThresholdInput lfsAutoTrackThresholdMb)),
            [| box lfsAutoTrackThresholdMb |]
        )

        // The same sync pattern for the background indexing limit.
        React.useEffect (
            (fun () -> setDiffIndexingLimitInput (GitSidebarInternal.formatThresholdInput diffIndexingLimitMb)),
            [| box diffIndexingLimitMb |]
        )

        React.useEffect (
            (fun () ->
                setPublishRenameInput (publishRenamePrompt |> Option.map _.CurrentName |> Option.defaultValue "")
                setPublishNamespaceId (GitSidebarInternal.initialPublishNamespaceId publishRenamePrompt)
            ),
            [| box publishRenamePrompt |]
        )

        let branchOptionsWithHead =
            if branchOptions.Length = 0 then
                [| None, "Current HEAD" |]
            else
                [| None, "Current HEAD" |]
                |> Array.append (
                    branchOptions
                    |> Array.map (fun branch ->
                        let marker =
                            [
                                if branch.IsCurrent then
                                    "current"

                                if branch.IsTracking then
                                    "tracking"
                            ]
                            |> String.concat ", "

                        let label =
                            if String.IsNullOrWhiteSpace marker then
                                $"{branch.DisplayLabel} ({GitSidebarInternal.branchKindLabel branch.Kind})"
                            else
                                $"{branch.DisplayLabel} ({GitSidebarInternal.branchKindLabel branch.Kind}, {marker})"

                        Some branch.RefName, label
                    )
                )

        let branchOptionsForSwitch =
            branchOptions |> Array.filter (fun branch -> not branch.IsCurrent)

        let canSwitchBranch = branchOptionsForSwitch.Length > 0

        let runSelectChangeAction (change: GitSidebarChange) =
            promise {
                setLocalError None

                let! result = onSelectChange change

                match result with
                | Ok() -> ()
                | Error message -> setLocalError (Some message)
            }
            |> Promise.start

        let openCreateBranchModal () =
            setLocalError None
            setBranchName ""

            let defaultStartPoint =
                branchOptions
                |> Array.tryFind _.IsTracking
                |> Option.orElseWith (fun () -> branchOptions |> Array.tryFind _.IsCurrent)
                |> Option.map _.RefName

            setSelectedStartPoint defaultStartPoint
            setActiveDialog ActiveDialog.CreateBranch

        let openSwitchBranchModal () =
            setLocalError None

            let defaultBranch = branchOptionsForSwitch |> Array.tryHead |> Option.map _.RefName

            setSelectedSwitchBranch defaultBranch
            setActiveDialog ActiveDialog.SwitchBranch

        let updateMarkedSelection (change: GitSidebarChange) (ctrlKey: bool) (shiftKey: bool) =
            let orderedPaths = changedFiles |> Array.map _.Path
            let anchorPath = selectionAnchorPath |> Option.defaultValue change.Path

            let rangePaths =
                GitSidebarInternal.tryRangeBetween orderedPaths anchorPath change.Path
                |> Option.defaultValue [| change.Path |]
                |> Set.ofArray

            let nextMarkedPaths =
                match ctrlKey, shiftKey with
                | false, false -> Set.singleton change.Path
                | true, false ->
                    if Set.contains change.Path markedPaths then
                        Set.remove change.Path markedPaths
                    else
                        Set.add change.Path markedPaths
                | false, true -> rangePaths
                | true, true -> Set.union markedPaths rangePaths

            setMarkedPaths (fun _ -> nextMarkedPaths)
            setSelectionAnchorPath (fun _ -> Some change.Path)

        let submitSave (selectedOnly: bool) =
            let normalizedCommitMessage = commitMessage.Trim()

            if String.IsNullOrWhiteSpace normalizedCommitMessage then
                setMissingMessageModalOpen true
            else
                setLocalError None

                if selectedOnly then
                    let request = {
                        Message = normalizedCommitMessage
                        Paths = markedPaths |> Set.toArray |> Array.sort
                    }

                    if uploadRightAway then
                        onPrimarySaveSelection request
                    else
                        onCommitSelection request

                    setMarkedPaths (fun _ -> Set.empty)
                elif uploadRightAway then
                    onPrimarySaveAll normalizedCommitMessage
                else
                    onCommitAll normalizedCommitMessage

                setCommitMessage ""

        let submitDiscardSelection (paths: string[]) =
            let normalizedPaths =
                paths
                |> Array.map _.Trim()
                |> Array.filter (String.IsNullOrWhiteSpace >> not)
                |> Array.distinct
                |> Array.sort

            if normalizedPaths.Length = 0 then
                setLocalError (Some "No selected changes to discard.")
            else
                setLocalError None
                onDiscardSelection normalizedPaths
                setMarkedPaths (fun _ -> Set.empty)
                setSelectionAnchorPath (fun _ -> None)

        let submitLfsThreshold () =
            let normalizedInput = lfsThresholdInput.Trim()

            let success, parsedThresholdMb = Int32.TryParse normalizedInput

            if not success then
                setLocalError (Some "Git LFS threshold must be a whole number.")
            elif parsedThresholdMb < GitSettingRanges.MinLfsAutoTrackThresholdMb then
                setLocalError (
                    Some $"Git LFS threshold must be at least {GitSettingRanges.MinLfsAutoTrackThresholdMb} MB."
                )
            elif parsedThresholdMb > GitSettingRanges.MaxLfsAutoTrackThresholdMb then
                setLocalError (
                    Some $"Git LFS threshold must not exceed {GitSettingRanges.MaxLfsAutoTrackThresholdMb} MB."
                )
            else
                setLocalError None
                onSaveLfsAutoTrackThreshold parsedThresholdMb
                setLfsThresholdInput (GitSidebarInternal.formatThresholdInput parsedThresholdMb)

        let submitDiffIndexingLimit () =
            let success, parsedLimitMb = Int32.TryParse(diffIndexingLimitInput.Trim())

            if not success then
                setLocalError (Some "Background indexing limit must be a whole number.")
            elif parsedLimitMb < GitSettingRanges.MinDiffIndexingLimitMb then
                setLocalError (
                    Some $"Background indexing limit must be at least {GitSettingRanges.MinDiffIndexingLimitMb} MB."
                )
            elif parsedLimitMb > GitSettingRanges.MaxDiffIndexingLimitMb then
                setLocalError (
                    Some $"Background indexing limit must not exceed {GitSettingRanges.MaxDiffIndexingLimitMb} MB."
                )
            else
                setLocalError None
                onSaveDiffIndexingLimit parsedLimitMb
                setDiffIndexingLimitInput (GitSidebarInternal.formatThresholdInput parsedLimitMb)

        let submitDownloadLargeFiles (nextValue: bool) =
            if nextValue = downloadLargeFilesInput then
                ()
            else
                setLocalError None
                setDownloadLargeFilesInput nextValue
                onSaveDownloadLargeFiles nextValue

        let submitCreateBranch () =
            let normalizedBranchName = branchName.Trim()

            if String.IsNullOrWhiteSpace normalizedBranchName then
                setLocalError (Some "Branch name must not be empty.")
            else
                setLocalError None

                onCreateBranch {
                    BranchName = normalizedBranchName
                    StartPoint = selectedStartPoint
                }

                setActiveDialog ActiveDialog.None
                setBranchName ""

        let submitSwitchBranch () =
            match selectedSwitchBranch with
            | None -> setLocalError (Some "Select a branch to switch to.")
            | Some branchName ->
                setLocalError None
                onSwitchBranch branchName
                setActiveDialog ActiveDialog.None

        let submitPublishRename () =
            let normalizedName = publishRenameInput.Trim()

            if String.IsNullOrWhiteSpace normalizedName then
                setLocalError (Some "Repository name must not be empty.")
            else
                setLocalError None

                onSubmitPublishRename {
                    Name = normalizedName
                    NamespaceId = publishNamespaceId
                }

        let submitPruneLfsCache () =
            setLocalError None
            callbacks.OnPruneLfsCache()

        let submitDedupLfsStorage () =
            setLocalError None
            callbacks.OnDedupLfsStorage()

        let hasConflicts = GitSidebarInternal.hasConflicts status changedFiles

        let canEditCommit =
            not status.IsClean && not hasConflicts && not isBusyWithVisibleOperation

        let markedCount = Set.count markedPaths
        let hasMarkedFiles = markedCount > 0
        let canRunPrimarySave = canEditCommit && not isBusy

        let normalizedLfsThresholdInput = lfsThresholdInput.Trim()

        let canSaveLfsThreshold =
            not isBusy
            && not (String.IsNullOrWhiteSpace normalizedLfsThresholdInput)
            && not (
                String.Equals(
                    normalizedLfsThresholdInput,
                    GitSidebarInternal.formatThresholdInput lfsAutoTrackThresholdMb,
                    StringComparison.Ordinal
                )
            )

        let normalizedDiffIndexingLimitInput = diffIndexingLimitInput.Trim()

        let canSaveDiffIndexingLimit =
            not isBusy
            && not (String.IsNullOrWhiteSpace normalizedDiffIndexingLimitInput)
            && not (
                String.Equals(
                    normalizedDiffIndexingLimitInput,
                    GitSidebarInternal.formatThresholdInput diffIndexingLimitMb,
                    StringComparison.Ordinal
                )
            )

        let branchHeaderProps = {
            Status = status
            HasRemote = hasRemote
            HasConflicts = hasConflicts
            IsBusy = isBusy
            CanOpenRemoteRepository = canOpenRemoteRepository
            OnOpenRemoteRepository = onOpenRemoteRepository
            OnRefresh =
                fun () ->
                    setLocalError None
                    onRefresh ()
        }

        Html.div [
            prop.testId "GitSidebar"
            prop.className
                "swt:@container/gitSidebar swt:flex swt:h-full swt:min-h-0 swt:min-w-0 swt:flex-col swt:overflow-x-hidden swt:overflow-y-auto swt:bg-base-100"
            prop.children [
                GitSidebar.BranchHeader(branchHeaderProps)

                GitSidebar.OperationStatusNotice(
                    ?runStatus = Some runStatus,
                    ?errorNotice = visibleError,
                    ?warningNotice = warningNotice,
                    busyTestId = "GitSidebarProgressNotice",
                    errorTestId = "GitSidebarErrorNotice",
                    ?onCancelOperation =
                        (if canCancelOperation && isBusy then
                             Some onCancelOperation
                         else
                             None),
                    ?backgroundActivities = backgroundActivities,
                    ?busyHeadlineOverride = busyHeadlineOverride,
                    ?operationSteps = operationSteps,
                    ?operationLog = operationLog,
                    ?lastLog = lastLog
                )

                GitSidebar.BranchStatusDetails(branchHeaderProps)

                GitSidebar.AdvancedActions(
                    {
                        DownloadLargeFilesInput = downloadLargeFilesInput
                        IsBusy = isBusy
                        RemoteActionsEnabled = remoteActionsEnabled
                        RemoteActionsWarning = remoteActionsWarning
                        SubmitDownloadLargeFiles = submitDownloadLargeFiles
                        SubmitUpdateFromOnline =
                            fun () ->
                                setLocalError None
                                onUpdateFromOnline ()
                        IsAdvancedActionsOpen = isAdvancedActionsOpen
                        ToggleAdvancedActions =
                            fun () ->
                                setActiveDialog (
                                    if isAdvancedActionsOpen then
                                        ActiveDialog.None
                                    else
                                        ActiveDialog.AdvancedActions
                                )
                        SubmitFetch =
                            fun () ->
                                setLocalError None
                                onFetch ()
                        SubmitPush =
                            fun () ->
                                setLocalError None
                                onPush ()
                        OpenCreateBranchModal = openCreateBranchModal
                        OpenSwitchBranchModal = openSwitchBranchModal
                        CanSwitchBranch = canSwitchBranch
                        SubmitPruneLfsCache = submitPruneLfsCache
                        SubmitDedupLfsStorage = submitDedupLfsStorage
                        LfsSettings = {
                            IsBusy = isBusy
                            LfsThresholdInput = lfsThresholdInput
                            SetLfsThresholdInput = setLfsThresholdInput
                            CanSaveLfsThreshold = canSaveLfsThreshold
                            SubmitLfsThreshold = submitLfsThreshold
                        }
                        DiffIndexingLimit = {
                            IsBusy = isBusy
                            DiffIndexingLimitInput = diffIndexingLimitInput
                            SetDiffIndexingLimitInput = setDiffIndexingLimitInput
                            CanSaveDiffIndexingLimit = canSaveDiffIndexingLimit
                            SubmitDiffIndexingLimit = submitDiffIndexingLimit
                        }
                    }
                )

                GitSidebar.CommitSection(
                    {
                        Status = status
                        HasConflicts = hasConflicts
                        CanEditCommit = canEditCommit
                        CommitMessage = commitMessage
                        SetCommitMessage = setCommitMessage
                        MarkedCount = markedCount
                        HasMarkedFiles = hasMarkedFiles
                        CanRunPrimarySave = canRunPrimarySave
                        UploadRightAway = uploadRightAway
                        SetUploadRightAway = setUploadRightAway
                        SubmitSaveSelected = fun () -> submitSave true
                        SubmitSaveAll = fun () -> submitSave false
                    }
                )

                if hasConflicts then
                    Html.div [
                        prop.className "swt:px-3 swt:pt-3 swt:@max-xs:px-2"
                        prop.children [
                            Html.div [
                                prop.testId "GitSidebarMergeBanner"
                                prop.className
                                    "swt:alert swt:alert-warning swt:min-w-0 swt:px-3 swt:py-2 swt:text-sm swt:@max-xs:px-2"
                                prop.children [
                                    Html.span [
                                        prop.className
                                            "swt:iconify swt:fluent--warning-shield-20-regular swt:size-4 swt:shrink-0"
                                    ]
                                    Html.span [
                                        prop.className "swt:min-w-0 swt:wrap-anywhere"
                                        prop.text "Resolve all conflicted files before pushing."
                                    ]
                                ]
                            ]
                        ]
                    ]

                GitSidebar.ChangedFilesList(
                    {
                        Status = status
                        ChangedFiles = changedFiles
                        MarkedPaths = markedPaths
                        IsBusy = isBusy
                        UpdateMarkedSelection = updateMarkedSelection
                        OpenChange = fun change -> runSelectChangeAction change
                        DiscardChanges = submitDiscardSelection
                    }
                )

                GitSidebar.Modals(
                    {
                        IsCreateBranchModalOpen = isCreateBranchModalOpen
                        SetCreateBranchModalOpen = setCreateBranchModalOpen
                        BranchName = branchName
                        SetBranchName = setBranchName
                        BranchOptionsWithHead = branchOptionsWithHead
                        SelectedStartPoint = selectedStartPoint
                        SetSelectedStartPoint = setSelectedStartPoint
                        SubmitCreateBranch = submitCreateBranch
                        IsSwitchBranchModalOpen = isSwitchBranchModalOpen
                        SetSwitchBranchModalOpen = setSwitchBranchModalOpen
                        BranchOptionsForSwitch = branchOptionsForSwitch
                        SelectedSwitchBranch = selectedSwitchBranch
                        SetSelectedSwitchBranch = setSelectedSwitchBranch
                        SubmitSwitchBranch = submitSwitchBranch
                        IsMissingMessageModalOpen = isMissingMessageModalOpen
                        SetMissingMessageModalOpen = setMissingMessageModalOpen
                        CloseDialog = fun () -> setActiveDialog ActiveDialog.None
                    }
                )

                GitSidebar.PendingRemoteActionDialog(
                    {
                        PendingConfirmation = pendingConfirmation
                        ConfirmPendingRemoteAction = onConfirmPendingRemoteAction
                        CancelPendingRemoteAction = onCancelPendingRemoteAction
                    }
                )

                GitSidebar.PublishRenameDialog(
                    {
                        Prompt = publishRenamePrompt
                        InputValue = publishRenameInput
                        SetInputValue = setPublishRenameInput
                        SelectedNamespaceId = publishNamespaceId
                        SetSelectedNamespaceId = setPublishNamespaceId
                        IsBusy = isBusy
                        Submit = submitPublishRename
                        Cancel = onCancelPublishRename
                    }
                )
            ]
        ]
