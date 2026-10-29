namespace Swate.Components.Page.GitSidebarTypes

open System
open Fable.Core

[<StringEnum(CaseRules.None)>]
type GitSidebarBranchKind =
    | Local
    | Remote

type GitSidebarStatus = {
    CurrentBranch: string option
    TrackingBranch: string option
    Ahead: int
    Behind: int
    IsClean: bool
    IsMergeInProgress: bool
}

type GitSidebarChange = {
    Path: string
    OriginalPath: string option
    IndexStatus: string
    WorkingTreeStatus: string
    IsConflicted: bool
}

type GitSidebarBranchOption = {
    RefName: string
    DisplayLabel: string
    Kind: GitSidebarBranchKind
    IsCurrent: bool
    IsTracking: bool
}

type GitSidebarProgress = {
    Method: string option
    Stage: string option
    ProgressPercent: float option
}

/// One step of the running operation, for example "Fetching online changes". The steps before the current one
/// are done. Detail is the latest message of the current step, such as "Receiving objects (12/80)".
type GitSidebarStep = {
    Label: string
    Detail: string option
    Percent: float option
    IsCurrent: bool
}

/// One line of the raw output of a command. Kind is "command", "stdout", "stderr" or "notice".
type GitSidebarLogLine = { Kind: string; Text: string }

/// The raw output of an operation. Omitted counts the oldest lines that the log no longer holds.
type GitSidebarLog = {
    Lines: GitSidebarLogLine[]
    Omitted: int
}

[<RequireQualifiedAccess>]
type GitSidebarRunStatus =
    | Idle
    | Busy of notice: string
    | Progress of GitSidebarProgress
    /// Busy with no banner, for a refresh the user did not start.
    | BusyWithoutNotice

/// A task that runs next to the sidebar's own operation, for example a download that the file tree started.
type GitSidebarBackgroundActivity = {
    Label: string
    /// Extra text after the label, such as a count.
    Detail: string option
    ProgressPercent: float option
    /// The raw output of the task. The notice offers "Show logs" when it holds a line.
    Log: GitSidebarLog option
}

type GitSidebarCreateBranchRequest = {
    BranchName: string
    StartPoint: string option
}

type GitSidebarCommitSelectionRequest = { Message: string; Paths: string[] }

type GitSidebarConfirmationDialog = {
    Title: string
    Message: string
    ConfirmLabel: string
    CancelLabel: string
}

/// One choice of the owner picker. NamespaceId None is the personal namespace.
/// A DisabledReason marks an owner that cannot be selected and says why.
type GitSidebarPublishOwner = {
    NamespaceId: int option
    Label: string
    DisabledReason: string option
}

/// Owners is empty when the dialog only asks for another name.
type GitSidebarPublishRenamePrompt = {
    CurrentName: string
    Message: string
    Owners: GitSidebarPublishOwner[]
    SelectedNamespaceId: int option
}

type GitSidebarPublishSubmit = {
    Name: string
    NamespaceId: int option
}

type GitSidebarCallbacks = {
    OnRefresh: unit -> unit
    OnFetch: unit -> unit
    OnPush: unit -> unit
    OnUpdateFromOnline: unit -> unit
    OnPrimarySaveSelection: GitSidebarCommitSelectionRequest -> unit
    OnPrimarySaveAll: string -> unit
    OnCommitSelection: GitSidebarCommitSelectionRequest -> unit
    OnCommitAll: string -> unit
    OnDiscardSelection: string[] -> unit
    OnConfirmPendingRemoteAction: unit -> unit
    OnCancelPendingRemoteAction: unit -> unit
    OnSaveDownloadLargeFiles: bool -> unit
    OnSaveLfsAutoTrackThreshold: int -> unit
    OnSaveDiffIndexingLimit: int -> unit
    OnCreateBranch: GitSidebarCreateBranchRequest -> unit
    OnSwitchBranch: string -> unit
    OnSelectChange: GitSidebarChange -> JS.Promise<Result<unit, string>>
    OnPruneLfsCache: unit -> unit
    OnDedupLfsStorage: unit -> unit
    OnCancelOperation: unit -> unit
}

[<RequireQualifiedAccess>]
module GitStatusCode =

    let normalize (code: string) =
        let trimmed = code |> Option.ofObj |> Option.defaultValue String.Empty |> _.Trim()

        if String.IsNullOrWhiteSpace trimmed then "." else trimmed

    let isStagedIndexStatus (code: string) =
        let normalized = normalize code
        normalized <> "." && normalized <> "?"
