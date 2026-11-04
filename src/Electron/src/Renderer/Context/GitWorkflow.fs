module Renderer.Context.GitWorkflow

open System
open Elmish
open Fable.Core

open Renderer.Types
open Swate.Components.Api.GitLabApi
open Swate.Components.Page.GitSidebarTypes
open Swate.Electron.Shared
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.VersionControlTypes

/// Large copies need time to settle before Git hashes their contents again.
let private externalChangesRefreshDelayMs = 1500

/// The background indexing limit of a session, in MiB, until the main process answers with its own.
[<Literal>]
let DefaultDiffIndexingLimitMb = 1024

/// The defaults Swate ships with. The renderer starts from these values until the main process
/// reports the effective ones, and keeps them when that call fails.
let builtInUserDefaults: UserDefaultValuesDto = {
    LfsAutoTrackThresholdMb = 1
    DownloadLargeFiles = false
    DiffIndexingLimitMb = DefaultDiffIndexingLimitMb
    UploadRightAway = true
    AutoCreateNotesFolder = true
}

[<RequireQualifiedAccess>]
type GitRefreshState =
    | Idle
    | Loading

/// Where the read of the stored "Upload right away" value stands for the current ARC. The first
/// refresh that reaches an open session starts the read. The answer applies only while the state is
/// Pending, so it never overwrites a change by the user (Settled) or a new ARC (back to NotStarted).
[<RequireQualifiedAccess>]
type UploadRightAwayLoad =
    | NotStarted
    | Pending
    | Settled

[<RequireQualifiedAccess>]
type GitBusyOperation =
    | Refreshing
    | InitializingRepository
    | FetchingFromRemote
    | PullingFromRemote
    | PushingToRemote
    | CloningRepository of targetPath: string
    | CommittingSelectedChanges
    | CommittingAllChanges
    | DiscardingSelectedChanges
    | SavingGitLfsThreshold
    | SavingDiffIndexingLimit
    | SavingGitLfsDownloadPreference
    | CreatingBranch
    | SwitchingBranch
    | InstallingDependency of componentName: string
    | RenamingRepository
    | PruningGitLfsCache
    | DeduplicatingGitLfsStorage
    | ConfirmingMergeResolution of path: string
    | FinalizingMerge
    | AbandoningMerge
    | ClearingStaleLock
    | RestoringInterruptedPaths
    | RetryingMaterialization

[<RequireQualifiedAccess>]
type GitRepositoryAvailability =
    | Ready
    | MissingRepository

[<RequireQualifiedAccess>]
type GitInstallRetryState =
    | Idle
    | PromptingForInstall of promptMessage: string * retryOperation: GitBusyOperation
    | InstallingForRetry of retryOperation: GitBusyOperation

[<RequireQualifiedAccess>]
type GitPageChange =
    | NoChange
    | Set of PageState
    | Clear

/// Whether the user has accepted the update a synchronize flagged. An acceptance carries the
/// target revision and the workspace token the preview was computed for, so it never applies
/// to a state the user did not see.
[<RequireQualifiedAccess>]
type GitUpdateAcceptance =
    | RequirePreview
    | Accepted of observedTarget: string * workspaceVersion: string

/// What the confirmation dialog continues with when the user confirms.
[<RequireQualifiedAccess>]
type GitPendingRemoteAction =
    | None
    | DiscardSelection of string[]
    | UpdateFromOnline of GitUpdateAcceptance
    | PublishAfterUpdate of GitUpdateAcceptance
    | FinalizeMerge
    /// The dialog offers the recovery stored in PendingRecovery.
    | Recover

/// One owner a first publish can create the DataHUB repository for. NamespaceId None is the
/// personal namespace. A DisabledReason marks an owner the user cannot pick and says why.
type GitPublishOwner = {
    NamespaceId: int option
    Label: string
    DisabledReason: string option
}

/// Owners is empty when the dialog only asks for another name.
type GitPublishRenamePrompt = {
    CurrentName: string
    Message: string
    Owners: GitPublishOwner[]
    SelectedNamespaceId: int option
}

/// The owner list and the owner choice a publish rename carries across its reply. A path
/// change that arrives before the reply resets the model, so the reply restores both.
type GitPublishOwnerSelection = {
    Owners: GitPublishOwner[] option
    Choice: GitPublishOwner option
}

/// A canceled update left paths that a killed fast-forward may have partially
/// rewritten. The user discards exactly those paths, keeps them, or retries the update.
type GitInterruptedUpdate = {
    AffectedPaths: string[]
    Instructions: string option
}

/// A recovery the user has to act on. Each case maps one structured recovery code of
/// the library to a dialog or notice of the sidebar.
[<RequireQualifiedAccess>]
type GitPendingRecovery =
    | RestoreInterruptedPaths of GitInterruptedUpdate
    | RetryMaterialization of message: string
    | RetryPublish of message: string
    | ClearStaleLock of instructions: string option
    | ClearCloneTarget of targetPath: string * instructions: string option

/// Remote provisioning that has created a project but not finished binding or
/// publishing. A retry resumes here and never creates a second project.
type GitProvisionedRemote = {
    RemoteUrl: string
    ProjectName: string
    IsBound: bool
}

type GitErrorNotification = { Title: string; Message: string }

/// The automatic reopen of a diff page whose worker session ended (it sat idle or a newer diff
/// took its slot), whose preparation token went stale, or whose side turned out not to be UTF-8
/// and needs an encoding choice without a token. Page boundaries differ between sessions, so
/// after the open, pages are read forward until a page reaches TargetLine, the first line the
/// user was reading. Without a target line the reopen reads until TargetPageIndex is loaded again.
type GitDiffReopen = {
    Generation: int
    TargetLine: GitDiffLinePosition option
    TargetPageIndex: int
}

type GitState = {
    Status: GitSidebarStatus
    ChangedFiles: GitSidebarChange[]
    BranchOptions: GitSidebarBranchOption[]
    /// The provider refs behind BranchOptions, kept so requests carry the opaque ref.
    Refs: LogicalRefDto[]
    OriginRemoteRepositoryWebUrl: string option
    PendingConfirmation: GitSidebarConfirmationDialog option
    PendingRemoteAction: GitPendingRemoteAction
    PendingPublishRename: GitPublishRenamePrompt option
    /// The owners the upload dialog offered, kept until the publish it started completes. None
    /// when no dialog asked for an owner.
    PublishOwners: GitPublishOwner[] option
    /// The owner the next project creation uses, set when the upload dialog is submitted.
    PublishOwnerChoice: GitPublishOwner option
    /// A primary save that still has to publish once the update or merge is done.
    PendingPostMergePush: bool
    PendingRecovery: GitPendingRecovery option
    /// The message of a large-file download that an index lock stopped. The lock dialog is open for it. A removed
    /// lock turns it into the download offer, and any other end of that dialog drops it.
    RetryMaterializationAfterLockClear: string option
    ProvisionedRemote: GitProvisionedRemote option
    /// A publish that waits until the refresh of a renamed ARC root has loaded the
    /// workspace token.
    PendingPublishAfterRefresh: bool
    PendingPublishForPath: string option
    /// The effective user defaults of the main process. A state without a session starts from them,
    /// and a reset for another ARC keeps them.
    UserDefaults: UserDefaultValuesDto
    LfsAutoTrackThresholdMb: int
    /// Whole MiB. A value from 1 to 63 keeps a new diff in memory with that budget. A larger value
    /// lets the diff use temp files, and the background indexing of an open diff stops once the
    /// pages it read add up to this size.
    DiffIndexingLimitMb: int
    DownloadLargeFiles: bool
    /// The "Upload right away" checkbox of the sidebar. The main process holds the value in the
    /// session of the open ARC, so it survives a renderer reload. This copy keeps the checkbox
    /// instant and survives the unmount of the sidebar panel. It resets with the ARC, like the LFS
    /// settings above, and the first refresh of the ARC reads the stored value.
    UploadRightAway: bool
    UploadRightAwayLoad: UploadRightAwayLoad
    RepositoryAvailability: GitRepositoryAvailability
    RefreshState: GitRefreshState
    RefreshRequestId: int
    SidebarVisible: bool
    ExternalChangesSeen: bool
    ExternalRefreshGeneration: int
    /// A refresh requested while a write is active runs after the write completes.
    RefreshPending: bool
    RefreshPendingSilently: bool
    BusyOperation: GitBusyOperation option
    BusyNotice: string option
    /// The operation the renderer can cancel. The renderer chooses its id before the call starts.
    CurrentOperation: OperationRequestDto option
    /// The id of the non-silent refresh that runs. A refresh keeps CurrentOperation empty, because a new refresh
    /// request replaces a running refresh, while a write that reports the Refreshing phase owns CurrentOperation
    /// and holds new refreshes back.
    RefreshOperationId: string option
    CurrentProgress: GitSidebarProgress option
    CurrentProgressPhase: string option
    /// The steps that the progress events of the running operation went through, in the order they started.
    /// The step with IsCurrent is the one that runs.
    CurrentSteps: GitSidebarStep[]
    /// The phase code of each step, in the order of CurrentSteps. A step start event with a known phase code
    /// makes that step current again instead of adding one.
    CurrentStepPhases: string[]
    /// The raw output that the running operation sent. Only the output of the operation that the sidebar shows
    /// lands here, see isForeignOperation.
    CurrentLog: OperationLog.OperationLog
    /// Whether the busy operation has sent any progress or output of its own since it started. The sidebar says
    /// that the operation waits for a file tree action only while this is false. The flag survives a phase change
    /// of a write and resets when a busy operation starts or ends.
    OperationReported: bool
    /// Date.now () when the busy operation started. A file tree action that started earlier can hold the session
    /// lock of the library, so only those actions can make the operation wait.
    OperationStartedAt: float option
    ErrorNotice: string option
    WarningNotice: string option
    PendingRefreshWarningNotice: string option
    SelectedChangePath: string option
    MergeResolutionPendingPath: string option
    PendingMaterializations: (string * bool) list
    PendingMaterializationSessionId: string option
    InstallRetryState: GitInstallRetryState
    PageLoadRequestId: int
    WriteRequestId: int
    CurrentArcPath: ArcRootPath
    ArcSessionId: int
    /// Opaque optimistic-concurrency token of the last refreshed status.
    WorkspaceVersion: string option
    ActiveConflict: ConflictSessionSummaryDto option
    Services: ServiceAvailabilityDto option
    /// The open text diff page. The page state shows a copy, and responses apply to this one.
    DiffPage: GitDiffPageData option
    /// Set while the diff page reopens by itself. A failure during that reopen settles the page.
    DiffReopen: GitDiffReopen option
} with

    static member Empty = {
        Status = {
            CurrentBranch = None
            TrackingBranch = None
            Ahead = 0
            Behind = 0
            IsClean = true
            IsMergeInProgress = false
        }
        ChangedFiles = [||]
        BranchOptions = [||]
        Refs = [||]
        OriginRemoteRepositoryWebUrl = None
        PendingConfirmation = None
        PendingRemoteAction = GitPendingRemoteAction.None
        PendingPublishRename = None
        PublishOwners = None
        PublishOwnerChoice = None
        PendingPostMergePush = false
        PendingRecovery = None
        RetryMaterializationAfterLockClear = None
        ProvisionedRemote = None
        PendingPublishAfterRefresh = false
        PendingPublishForPath = None
        UserDefaults = builtInUserDefaults
        LfsAutoTrackThresholdMb = builtInUserDefaults.LfsAutoTrackThresholdMb
        DiffIndexingLimitMb = builtInUserDefaults.DiffIndexingLimitMb
        DownloadLargeFiles = builtInUserDefaults.DownloadLargeFiles
        UploadRightAway = builtInUserDefaults.UploadRightAway
        UploadRightAwayLoad = UploadRightAwayLoad.NotStarted
        RepositoryAvailability = GitRepositoryAvailability.Ready
        RefreshState = GitRefreshState.Idle
        RefreshRequestId = 0
        SidebarVisible = false
        ExternalChangesSeen = false
        ExternalRefreshGeneration = 0
        RefreshPending = false
        RefreshPendingSilently = false
        BusyOperation = None
        BusyNotice = None
        CurrentOperation = None
        RefreshOperationId = None
        CurrentProgress = None
        CurrentProgressPhase = None
        CurrentSteps = [||]
        CurrentStepPhases = [||]
        CurrentLog = OperationLog.empty
        OperationReported = false
        OperationStartedAt = None
        ErrorNotice = None
        WarningNotice = None
        PendingRefreshWarningNotice = None
        SelectedChangePath = None
        MergeResolutionPendingPath = None
        PendingMaterializations = []
        PendingMaterializationSessionId = None
        InstallRetryState = GitInstallRetryState.Idle
        PageLoadRequestId = 0
        WriteRequestId = 0
        CurrentArcPath = None
        ArcSessionId = 0
        WorkspaceVersion = None
        ActiveConflict = None
        Services = None
        DiffPage = None
        DiffReopen = None
    }

type GitRefreshResult = {
    Session: Result<WorkspaceSessionInfoDto, OperationFailureDto>
    Status: Result<WorkspaceStatusDto, OperationFailureDto>
    Refs: Result<LogicalRefDto[], OperationFailureDto>
    LfsSettings: Result<StoragePolicySettingsDto, OperationFailureDto>
    OriginRemoteRepositoryWebUrl: string option
}

type Reply<'T> = Result<'T, string> -> unit

/// The handle and workspace token bind a user's choice to the conflict state that was shown.
type GitMergeResolutionRequest = {
    Path: string
    Handle: ConflictSessionHandleDto
    WorkspaceVersion: string
    Resolution: ConflictResolutionDto
}

type ConfirmMergeResolutionOutcome = {
    UpdatedStatus: WorkspaceStatusDto
    NextConflictedPath: string option
    PageChange: GitPageChange
    Notice: string option
    PendingMaterializationPath: (string * bool) option
    ResolutionSessionId: string
    /// True when the last item was resolved and the merge was finalized.
    Finalized: bool
}

type FileChoice =
    | KeepMine
    | UseOnline

let keepMineCandidateId = "workspace"
let useOnlineCandidateId = "target"

let pickRequestFor (page: VersionControlFileChoicePage) (choice: FileChoice) : GitMergeResolutionRequest =
    let candidateId =
        match choice with
        | KeepMine -> keepMineCandidateId
        | UseOnline -> useOnlineCandidateId

    {
        Path = page.Path
        Handle = page.Handle
        WorkspaceVersion = page.WorkspaceVersion
        Resolution = ConflictResolutionDto.PickCandidate candidateId
    }

let candidateToFileChoiceVersion (candidate: ConflictCandidateDto) : FileChoiceVersion = {
    CandidateId = candidate.CandidateId
    SizeBytes = candidate.Object |> Option.bind _.SizeBytes
    IsDownloaded = candidate.Object |> Option.map _.IsLocallyAvailable
    Revision =
        candidate.Revision
        |> Option.map (fun revision ->
            if revision.Length > 7 then
                revision.Substring(0, 7)
            else
                revision
        )
    IsDeleted = candidate.Preview.IsNone && candidate.Object.IsNone
}

let private formatSizeBytes (sizeBytes: float) =
    if sizeBytes < 1024.0 then
        Math.Round(sizeBytes).ToString("0", Globalization.CultureInfo.InvariantCulture)
        + " bytes"
    else
        let mutable scaledSize = sizeBytes
        let mutable unit = "KB"

        for nextUnit in [| "KB"; "MB"; "GB" |] do
            if scaledSize >= 1024.0 then
                scaledSize <- scaledSize / 1024.0
                unit <- nextUnit

        scaledSize.ToString("0.0", Globalization.CultureInfo.InvariantCulture)
        + " "
        + unit

let versionLine (label: string) (showRevision: bool) (version: FileChoiceVersion) =
    if version.IsDeleted then
        Some $"{label}: deleted"
    else
        let parts =
            [
                version.SizeBytes |> Option.map formatSizeBytes
                version.IsDownloaded
                |> Option.map (fun isDownloaded -> if isDownloaded then "downloaded" else "not downloaded")
                if showRevision then
                    version.Revision |> Option.map (fun revision -> $"from {revision}")
                else
                    None
            ]
            |> List.choose id

        if List.isEmpty parts then
            None
        else
            let joinedParts = String.concat ", " parts
            Some $"{label}: {joinedParts}"

let conflictPageFor
    (conflict: ConflictSessionSummaryDto)
    (workspaceVersion: string)
    (requestedPath: string)
    : Result<PageState, string> =
    match conflict.Items |> Array.tryFind (fun item -> item.Path = requestedPath) with
    | None -> Error $"'{requestedPath}' is not part of the open conflict session anymore."
    | Some item ->
        let unsupportedReason =
            match item.CombinedPreview with
            | Some(ContentViewDto.Unsupported reason) -> reason
            | _ -> Some "The provider offers no text preview for this conflict."

        let workspaceCandidate =
            item.Candidates
            |> Array.tryFind (fun candidate -> candidate.CandidateId = keepMineCandidateId)

        let targetCandidate =
            item.Candidates
            |> Array.tryFind (fun candidate -> candidate.CandidateId = useOnlineCandidateId)

        let fileChoicePage workspace target =
            Ok(
                PageState.GitFileChoiceConflictPage {
                    Path = requestedPath
                    Handle = conflict.Handle
                    WorkspaceVersion = workspaceVersion
                    Mine = candidateToFileChoiceVersion workspace
                    Online = candidateToFileChoiceVersion target
                }
            )

        let isDeleted candidate =
            candidate.Preview.IsNone && candidate.Object.IsNone

        match item.SupportsResolvedContent, item.CombinedPreview, workspaceCandidate, targetCandidate with
        | _, _, Some workspace, Some target when isDeleted workspace || isDeleted target ->
            fileChoicePage workspace target
        | true, Some(ContentViewDto.Text content), _, _ ->
            Ok(
                PageState.GitMergeConflictPage {
                    Path = requestedPath
                    ConflictContent = content
                    Handle = conflict.Handle
                    WorkspaceVersion = workspaceVersion
                }
            )
        | false, _, Some workspace, Some target -> fileChoicePage workspace target
        | false, _, _, _ ->
            Ok(
                PageState.GitUnsupportedPage {
                    Path = requestedPath
                    Reason = unsupportedReason
                }
            )
        | _, Some(ContentViewDto.Unsupported reason), _, _ ->
            Ok(
                PageState.GitUnsupportedPage {
                    Path = requestedPath
                    Reason = reason
                }
            )
        | _ ->
            Ok(
                PageState.GitUnsupportedPage {
                    Path = requestedPath
                    Reason = unsupportedReason
                }
            )

[<RequireQualifiedAccess>]
type ConfirmMergeResolutionError =
    /// The handle or the workspace changed since the preview was shown.
    | Stale of
        message: string *
        pendingMaterializationPath: (string * bool) option *
        materializationsConsumed: bool *
        resolutionSessionId: string
    | Failed of
        message: string *
        pendingMaterializationPath: (string * bool) option *
        materializationsConsumed: bool *
        resolutionSessionId: string

type PreparedCommitOperation = {
    BusyOperation: GitBusyOperation
    NormalizedMessage: string
    PathsToCommit: string[]
}

type WriteRequest =
    | Fetch
    | Pull of GitUpdateAcceptance
    | Push of GitUpdateAcceptance
    | PrimarySave of PreparedCommitOperation
    | Clone of CloneWorkspaceRequestDto * Reply<string>
    | CommitSelection of PreparedCommitOperation
    | CommitAll of PreparedCommitOperation
    | DiscardSelection of string[]
    | SaveLfsSettings of GitBusyOperation * StoragePolicySettingsDto
    | PruneLfsCache
    | DedupLfsStorage
    | CreateBranch of GitSidebarCreateBranchRequest
    | SwitchBranch of string
    | FinalizeMerge
    | AbandonMerge
    | ClearStaleLock
    | RestoreInterruptedPaths of string[]
    | RetryMaterialization

/// SelectedChangePath uses None for no override and Some None to clear the selection.
/// Published says whether the synchronize published. It is None when the write was not a synchronize.
/// ObjectsMissing says whether a large object is still missing after a lock removal. It is None when the write was
/// not a lock removal or when the listing after it failed.
type WriteUnitSuccess = {
    Refresh: GitRefreshResult
    PageChange: GitPageChange
    SelectedChangePath: string option option
    Warning: string option
    Partial: OperationFailureDto option
    Published: bool option
    ObjectsMissing: bool option
}

type WriteSuccess =
    | UnitSuccess of WriteUnitSuccess
    | CloneSuccess of string

let private unitSuccess (refresh: GitRefreshResult) = {
    Refresh = refresh
    PageChange = GitPageChange.NoChange
    SelectedChangePath = None
    Warning = None
    Partial = None
    Published = None
    ObjectsMissing = None
}

let private joinMessages first second =
    match first, second with
    | Some firstMessage, Some secondMessage -> Some $"{firstMessage} {secondMessage}"
    | Some message, None
    | None, Some message -> Some message
    | None, None -> None

type WriteAttemptOutcome =
    | Completed of WriteSuccess
    | FinalizedButRefreshFailed of message: string
    | CompletedWithPendingRemoteConfirmation of WriteSuccess * GitSidebarConfirmationDialog * GitPendingRemoteAction
    | CompletedWithPendingRemoteFailure of WriteSuccess * string
    /// Opens the upload dialog. owners Some is a freshly loaded owner list to store and None keeps
    /// the stored one. clearOwnerChoice drops the owner the user picked after DataHUB refused it.
    | RequiresRemoteProjectRename of message: string * owners: GitPublishOwner[] option * clearOwnerChoice: bool
    | RequiresDependencyInstall of componentName: string * promptMessage: string
    | RequiresRecovery of GitPendingRecovery * message: string
    /// The workspace token was stale while the workspace itself did not change. The
    /// write is retried once against a refreshed token before this reaches the user.
    | StaleWorkspaceVersion of message: string * targetMoved: bool
    | OperationCancelled of string
    /// The remote project exists now. Binding or publishing still has to be retried.
    | ProvisioningIncomplete of GitProvisionedRemote * message: string

[<RequireQualifiedAccess>]
type private RoutedFailure =
    | Cancelled of string
    | DependencyInstall of message: string
    | Recovery of GitPendingRecovery * message: string
    | RefreshAfterCancel of message: string
    | RefreshThenReport of message: string
    | StaleWorkspace of message: string * targetMoved: bool
    /// The workspace has a conflict session the user has to resolve first.
    | ConflictSession of OperationFailureDto
    | UpdateAcceptanceRequired of dialog: GitSidebarConfirmationDialog * observedTarget: string
    | Error of string

/// Requests of the diff page and the responses of the text diff calls. Each one names the
/// generation of the page it belongs to.
[<RequireQualifiedAccess>]
type GitDiffMsg =
    | LoadNext of generation: int
    /// Starts the background read of every page after the loaded ones. The pages arrive one
    /// request at a time and are evicted within the window limits, so the viewer learns the extent
    /// of the whole diff.
    | Index of generation: int
    | Expand of generation: int * gapId: string * fromStart: bool
    | LoadLineSlice of generation: int * side: DiffSideDto * line: float * offsetUtf16: float
    /// Asks for the text in front of the displayed slice of a line, which starts at displayedStart.
    | LoadLineBefore of generation: int * side: DiffSideDto * line: float * displayedStart: float
    /// Asks for the rows of an evicted page. The visible pages are the pages the viewer shows
    /// at that moment, and eviction keeps them.
    | Replay of generation: int * pageId: string * visiblePages: string list
    | ChooseEncoding of generation: int * side: DiffSideDto * encoding: string
    /// The page state changed. Any page other than a diff page means the user left the diff.
    | PageStateObserved of PageState option
    | OpenCompleted of
        generation: int *
        request: OpenTextDiffRequestDto *
        result: Result<OperationResultDto<ResumableOpenDto>, string>
    | PageCompleted of
        generation: int *
        request: ReadTextDiffPageRequestDto *
        result: Result<OperationResultDto<ResumablePageDto>, string>
    | ReplayCompleted of
        generation: int *
        request: ReplayTextDiffPageRequestDto *
        result: Result<OperationResultDto<DiffPageDto>, string>
    | ExpandCompleted of
        generation: int *
        request: ExpandTextDiffRequestDto *
        result: Result<OperationResultDto<ResumablePartsDto>, string>
    | LineCompleted of
        generation: int *
        request: ReadTextDiffLineRequestDto *
        result: Result<OperationResultDto<ResumableLineDto>, string>

type Msg =
    | ResetWorkflow
    | DiffPageMsg of GitDiffMsg
    | SetCurrentProgress of VersionControlProgressDto
    | OperationOutputReceived of VersionControlOutputDto
    | OperationStarted of OperationRequestDto
    | ArcPathChanged of ArcRootPath
    | GitRepositoryInitialized of arcPath: string
    | RefreshRequested
    | RefreshRequestedSilently
    | ExternalChangesDetected
    | ExternalRefreshDue of generation: int
    | SidebarVisibilityChanged of bool
    | UploadRightAwayChanged of bool
    | UploadRightAwayLoaded of arcSessionId: int * value: bool
    | UserDefaultsLoaded of UserDefaultValuesDto
    | RefreshCompleted of requestId: int * result: Result<GitRefreshResult, string>
    | SilentRefreshCompleted of requestId: int * result: Result<GitRefreshResult, string>
    | InitRepositoryRequested
    | InitRepositoryCompleted of sessionId: int * result: Result<unit, string>
    | SelectChangeRequested of GitSidebarChange * Reply<unit>
    | SelectChangeCompleted of
        requestId: int *
        path: string *
        reply: Reply<unit> *
        result: Result<GitPageChange, string>
    | ConfirmMergeResolutionRequested of GitMergeResolutionRequest
    | ConfirmMergeResolutionCompleted of
        sessionId: int *
        result: Result<ConfirmMergeResolutionOutcome, ConfirmMergeResolutionError>
    | SaveLfsAutoTrackThresholdRequested of int
    | SaveDiffIndexingLimitRequested of int
    | SaveDownloadLargeFilesRequested of bool
    | FetchRequested
    | PushRequested
    | CancelCurrentOperationRequested
    | CancelCurrentOperationCompleted of sessionId: int * operationKey: OperationRequestDto * Result<bool, string>
    | UpdateFromOnlineRequested
    | CloneRequested of CloneWorkspaceRequestDto * Reply<string>
    | PrimarySaveSelectionRequested of GitSidebarCommitSelectionRequest
    | PrimarySaveAllRequested of string
    | CommitSelectionRequested of GitSidebarCommitSelectionRequest
    | CommitAllRequested of string
    | DiscardSelectionRequested of string[]
    | ConfirmPendingRemoteActionRequested
    | CancelPendingRemoteActionRequested
    | SubmitPublishRenameRequested of GitSidebarPublishSubmit
    | CancelPublishRenameRequested
    | PublishRenameCompleted of sessionId: int * result: Result<string, string> * selection: GitPublishOwnerSelection
    | CreateBranchRequested of GitSidebarCreateBranchRequest
    | SwitchBranchRequested of string
    | SwitchBranchPreflightCompleted of
        sessionId: int *
        refName: string *
        Result<OperationResultDto<SwitchPreflightDto>, string>
    | PruneLfsCacheRequested
    | DedupLfsStorageRequested
    | ClearStaleLockRequested
    | RestoreInterruptedPathsRequested
    | RetryMaterializationRequested
    | DismissRecoveryRequested
    | WriteRequested of WriteRequest
    | WritePhaseChanged of sessionId: int * writeRequestId: int * GitBusyOperation
    | WriteCompleted of sessionId: int * writeRequestId: int * WriteRequest * Result<WriteAttemptOutcome, string>
    | WriteInstallPromptAnswered of sessionId: int * WriteRequest * componentName: string * bool
    | WriteInstallCompleted of sessionId: int * WriteRequest * Result<OperationResultDto<DependencyStatusDto>, string>

/// The text diff calls of the main process, kept in one record so tests can replace them.
type GitTextDiffClient = {
    openTextDiff: OpenTextDiffRequestDto -> JS.Promise<Result<OperationResultDto<ResumableOpenDto>, string>>
    readTextDiffPage: ReadTextDiffPageRequestDto -> JS.Promise<Result<OperationResultDto<ResumablePageDto>, string>>
    replayTextDiffPage: ReplayTextDiffPageRequestDto -> JS.Promise<Result<OperationResultDto<DiffPageDto>, string>>
    expandTextDiff: ExpandTextDiffRequestDto -> JS.Promise<Result<OperationResultDto<ResumablePartsDto>, string>>
    readTextDiffLine: ReadTextDiffLineRequestDto -> JS.Promise<Result<OperationResultDto<ResumableLineDto>, string>>
    closeTextDiff: TextDiffHandleRequestDto -> JS.Promise<Result<OperationResultDto<unit>, string>>
}

/// What the renderer needs from the main process. Every function returns the
/// structured result of the library, so the workflow routes on categories and codes.
/// Calls that can be canceled take the request with the operation id the workflow
/// allocated, so the cancel button knows the id before the call has started.
type GitDependencies = {
    getSessionInfo: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceSessionInfoDto>, string>>
    getStatus: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceStatusDto>, string>>
    listRefs: OperationRequestDto -> JS.Promise<Result<OperationResultDto<LogicalRefDto[]>, string>>
    getRepositoryWebUrl: OperationRequestDto -> JS.Promise<Result<OperationResultDto<string option>, string>>
    getStoragePolicySettings:
        OperationRequestDto -> JS.Promise<Result<OperationResultDto<StoragePolicySettingsDto>, string>>
    setStoragePolicySettings: StoragePolicySettingsRequestDto -> JS.Promise<Result<OperationResultDto<unit>, string>>
    /// The checkbox value the main process holds for the open ARC. These two calls are not
    /// operations: they take no operation id and the main process runs them without a lock.
    getUploadRightAway: unit -> JS.Promise<Result<OperationResultDto<bool>, string>>
    setUploadRightAway: bool -> JS.Promise<Result<OperationResultDto<unit>, string>>
    loadConflictPage: ConflictSessionSummaryDto -> string -> string -> JS.Promise<Result<PageState, string>>
    /// Replaces the page state with the result of the function, applied to the page state the
    /// app holds when the update runs.
    updatePageState: (PageState option -> PageState option) -> unit
    initializeWorkspace: InitializeWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
    bindWorkspace: BindWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceSessionInfoDto>, string>>
    createRemoteProject: CreateProjectRequest -> JS.Promise<Result<ExploreProjectDto, CreateProjectError>>
    loadPublishOwners: unit -> JS.Promise<Result<PublishOwnersDto, GitLabError>>
    renameOpenArcRoot: string -> JS.Promise<Result<string, string>>
    checkDependencies: OperationRequestDto -> JS.Promise<Result<OperationResultDto<DependencyStatusDto[]>, string>>
    installDependency:
        InstallDependencyRequestDto -> JS.Promise<Result<OperationResultDto<DependencyStatusDto>, string>>
    refreshSynchronization:
        OperationRequestDto -> JS.Promise<Result<OperationResultDto<SynchronizationStateDto>, string>>
    synchronize: SynchronizeRequestDto -> JS.Promise<Result<OperationResultDto<SynchronizationStateDto>, string>>
    cancelOperation: OperationRequestDto -> JS.Promise<Result<bool, string>>
    cloneWorkspace: CloneWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
    createRef: CreateRefRequestDto -> JS.Promise<Result<OperationResultDto<LogicalRefDto>, string>>
    switchRef: SwitchRefRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceStatusDto>, string>>
    preflightSwitchRef: SwitchRefRequestDto -> JS.Promise<Result<OperationResultDto<SwitchPreflightDto>, string>>
    createRevision: CreateRevisionRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
    restorePaths: RestorePathsRequestDto -> JS.Promise<Result<OperationResultDto<unit>, string>>
    resolveConflict:
        ResolveConflictRequestDto -> JS.Promise<Result<OperationResultDto<ConflictResolutionOutcomeDto>, string>>
    finalizeConflict: FinalizeConflictRequestDto -> JS.Promise<Result<OperationResultDto<string option>, string>>
    cancelConflict: CancelConflictRequestDto -> JS.Promise<Result<OperationResultDto<unit>, string>>
    listObjects: OperationRequestDto -> JS.Promise<Result<OperationResultDto<ObjectStateDto[]>, string>>
    materializeObject: ObjectPathRequestDto -> JS.Promise<Result<OperationResultDto<unit>, string>>
    materializeObjects: ObjectPathsRequestDto -> JS.Promise<Result<OperationResultDto<unit>, string>>
    pruneStorage: OperationRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
    deduplicateStorage: OperationRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
    clearStaleLock: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceStatusDto>, string>>
    textDiff: GitTextDiffClient
    hasUsableAccount: unit -> bool
    delay: int -> JS.Promise<unit>
    newOperationId: unit -> string
    confirmLfsPrune: string -> bool
    confirmInstall: string -> bool
    reportError: GitErrorNotification -> unit
}

let staleArcSessionMessage =
    "Git operation was canceled because the active ARC changed."

let busyNoticeFromOperation =
    function
    | GitBusyOperation.Refreshing -> Some "Refreshing Git state"
    | GitBusyOperation.InitializingRepository -> Some "Initializing repository"
    | GitBusyOperation.FetchingFromRemote -> Some "Fetching from remote"
    | GitBusyOperation.PullingFromRemote -> Some "Pulling from remote"
    | GitBusyOperation.PushingToRemote -> Some "Pushing to remote"
    | GitBusyOperation.CloningRepository _ -> Some "Cloning repository"
    | GitBusyOperation.CommittingSelectedChanges -> Some "Committing selected changes"
    | GitBusyOperation.CommittingAllChanges -> Some "Committing all changes"
    | GitBusyOperation.DiscardingSelectedChanges -> Some "Discarding selected changes"
    | GitBusyOperation.SavingGitLfsThreshold -> Some "Saving Git LFS threshold"
    | GitBusyOperation.SavingDiffIndexingLimit -> Some "Saving background indexing limit"
    | GitBusyOperation.SavingGitLfsDownloadPreference -> Some "Saving Git LFS download preference"
    | GitBusyOperation.CreatingBranch -> Some "Creating branch"
    | GitBusyOperation.SwitchingBranch -> Some "Switching branch"
    | GitBusyOperation.InstallingDependency componentName -> Some $"Installing {componentName}"
    | GitBusyOperation.RenamingRepository -> Some "Renaming ARC"
    | GitBusyOperation.PruningGitLfsCache -> Some "Cleaning Git LFS cache"
    | GitBusyOperation.DeduplicatingGitLfsStorage -> Some "Reducing Git LFS duplicate storage"
    | GitBusyOperation.ConfirmingMergeResolution _ -> Some "Confirming merge resolution"
    | GitBusyOperation.FinalizingMerge -> Some "Finalizing merge"
    | GitBusyOperation.AbandoningMerge -> Some "Abandoning merge"
    | GitBusyOperation.ClearingStaleLock -> Some "Clearing stale lock"
    | GitBusyOperation.RestoringInterruptedPaths -> Some "Restoring interrupted files"
    | GitBusyOperation.RetryingMaterialization -> Some "Downloading large files"

let currentRunStatus (model: GitState) =
    match model.CurrentProgress with
    | Some progress -> Some(GitSidebarRunStatus.Progress progress)
    | None ->
        match model.BusyNotice, model.BusyOperation with
        | Some notice, _ -> Some(GitSidebarRunStatus.Busy notice)
        // Only a silent refresh runs without notice text.
        | None, Some _ -> Some GitSidebarRunStatus.BusyWithoutNotice
        | None, None -> None

let private countOrZero (value: int option) = value |> Option.defaultValue 0

let mapStatus (status: WorkspaceStatusDto) : GitSidebarStatus = {
    CurrentBranch = status.CurrentRef |> Option.map _.Name
    TrackingBranch = status.Synchronization |> Option.bind _.TargetRef |> Option.map _.Name
    Ahead = status.Synchronization |> Option.bind _.LocalRevisionCount |> countOrZero
    Behind = status.Synchronization |> Option.bind _.TargetRevisionCount |> countOrZero
    IsClean = status.Changes.Length = 0
    IsMergeInProgress = status.ActiveConflictSession.IsSome
}

/// Porcelain-style status letters the sidebar already renders.
let private changeCode (kind: FileChangeKindDto) =
    match kind with
    | FileChangeKindDto.Added -> "A"
    | FileChangeKindDto.Modified -> "M"
    | FileChangeKindDto.Deleted -> "D"
    | FileChangeKindDto.Renamed -> "R"
    | FileChangeKindDto.Conflicted -> "U"

let mapChanges (status: WorkspaceStatusDto) : GitSidebarChange[] =
    let conflictedPaths =
        status.ActiveConflictSession
        |> Option.map (fun session -> session.Items |> Array.map _.Path |> Set.ofArray)
        |> Option.defaultValue Set.empty

    status.Changes
    |> Array.map (fun change ->
        let isConflicted =
            change.Kind = FileChangeKindDto.Conflicted
            || conflictedPaths.Contains change.Path

        {
            Path = change.Path
            OriginalPath = change.OldPath
            IndexStatus = if isConflicted then "U" else changeCode change.Kind
            WorkingTreeStatus = if isConflicted then "U" else "."
            IsConflicted = isConflicted
        }
    )

let private mapRefKind (kind: RefKindDto) : GitSidebarBranchKind =
    match kind with
    | RefKindDto.Local -> GitSidebarBranchKind.Local
    | RefKindDto.Remote -> GitSidebarBranchKind.Remote

let mapBranches (targetRef: LogicalRefDto option) (refs: LogicalRefDto[]) : GitSidebarBranchOption[] =
    refs
    |> Array.map (fun reference -> {
        RefName = reference.Name
        DisplayLabel = reference.Name
        Kind = mapRefKind reference.Kind
        IsCurrent = reference.IsCurrent
        IsTracking =
            targetRef
            |> Option.exists (fun target -> target.ProviderRef = reference.ProviderRef)
    })

let hasRemote (model: GitState) =
    model.Status.TrackingBranch.IsSome
    || (model.BranchOptions
        |> Array.exists (fun branch -> branch.Kind = GitSidebarBranchKind.Remote))
    || (model.Refs |> Array.exists (fun reference -> reference.Kind = RefKindDto.Remote))

/// The opaque provider ref behind a branch option. The sidebar works with names, the
/// provider only accepts the ref it handed out.
let providerRefOf (model: GitState) (refName: string) =
    model.Refs
    |> Array.tryFind (fun reference -> String.Equals(reference.Name, refName, StringComparison.Ordinal))
    |> Option.map _.ProviderRef

let progressPercent (progress: VersionControlProgressDto) =
    match progress.Completed, progress.Total with
    | Some completed, Some total when total > 0.0 -> Some(Math.Min(100.0, completed / total * 100.0))
    | _ -> None

/// The headline of an event that starts a phase without a display message (a reset) stays on the label of the
/// current step. The busy notice shows only while the operation has no step yet.
let private mergeProgressUpdate (model: GitState) (steps: GitSidebarStep[]) (progress: VersionControlProgressDto) =
    let incomingPercent = progressPercent progress
    let samePhase = model.CurrentProgressPhase = Some progress.PhaseCode

    let currentStepLabel = steps |> Array.tryFind _.IsCurrent |> Option.map _.Label

    let current =
        model.CurrentProgress
        |> Option.defaultValue {
            Method = None
            Stage = model.BusyNotice
            ProgressPercent = None
        }

    if samePhase then
        {
            Method = current.Method
            Stage =
                progress.DisplayMessage
                |> Option.orElse current.Stage
                |> Option.orElse model.BusyNotice
            ProgressPercent = incomingPercent |> Option.orElse current.ProgressPercent
        }
    else
        {
            Method = None
            Stage =
                progress.DisplayMessage
                |> Option.orElse currentStepLabel
                |> Option.orElse model.BusyNotice
            ProgressPercent = incomingPercent
        }

/// The label of a meter message: the text before the first " (". A message without " (" is its own label.
let stepLabelOf (message: string) : string =
    match message.IndexOf(" (", StringComparison.Ordinal) with
    | index when index > 0 -> message.Substring(0, index)
    | _ -> message

/// True for a meter message: a label followed by " (k/n)", with or without a trailing size or rate.
let private isMeterMessage (message: string) =
    System.Text.RegularExpressions.Regex.IsMatch(message, @"^[^(]+ \(\d+/\d+\)")

/// Applies one progress event to the step list. A step belongs to one phase code, so the steps never outnumber
/// the phase codes of the operation. A phase that repeats (one materialization per file) keeps its step.
/// - An event with a display message and no numbers starts the step of its phase code. A phase that has a step
///   makes that step current again and keeps its label.
/// - An event with a display message that is a meter ("Downloading LFS objects (0/1), 1.2 MB") starts the step of
///   its phase code when the phase has none. The step is labelled with the meter's label only.
/// - Every other event changes only the step with the phase code of the event. The detail is the display message
///   when it differs from the label, and the percent is the one of the event, or none when the event has no
///   numbers. The step becomes current. An event of a phase without a step that starts none changes nothing, for
///   example the "Preparing" and "materialized" events of a download that carry numbers but no meter.
let private applyProgressToSteps
    (steps: GitSidebarStep[])
    (stepPhases: string[])
    (progress: VersionControlProgressDto)
    : GitSidebarStep[] * string[] =
    let percent = progressPercent progress
    let hasNumbers = progress.Completed.IsSome || progress.Total.IsSome

    let message =
        progress.DisplayMessage
        |> Option.filter (fun text -> not (String.IsNullOrWhiteSpace text))

    let detailFor (label: string) (previous: string option) =
        match message with
        | Some text when text <> label -> Some text
        | Some _ -> None
        | None -> previous

    match stepPhases |> Array.tryFindIndex (fun phase -> phase = progress.PhaseCode) with
    | Some index ->
        let updated =
            steps
            |> Array.mapi (fun stepIndex step ->
                if stepIndex <> index then
                    { step with IsCurrent = false }
                else
                    {
                        step with
                            Detail = detailFor step.Label step.Detail
                            Percent = percent
                            IsCurrent = true
                    }
            )

        updated, stepPhases
    | None ->
        match message with
        | Some text when not hasNumbers || isMeterMessage text ->
            let label = if isMeterMessage text then stepLabelOf text else text
            let earlier = steps |> Array.map (fun step -> { step with IsCurrent = false })

            let started: GitSidebarStep = {
                Label = label
                Detail = detailFor label None
                Percent = percent
                IsCurrent = true
            }

            Array.append earlier [| started |], Array.append stepPhases [| progress.PhaseCode |]
        | _ -> steps, stepPhases

/// The message shown for a structured failure: the library message plus its
/// recovery instructions when it has any. A path_not_found failure also lists up to five affected paths.
let failureMessage (failure: OperationFailureDto) =
    // Only a commit that exists while the index lags behind gets Swate's text. A held
    // lock without a commit keeps the library's instructions for the lock dialog.
    let usesIndexRecoveryText =
        failure.Code = "index_reconciliation_failed"
        || failure.Code = VersionControlCodes.IndexLocked

    let instructions =
        match failure.RecoveryAction with
        | Some recovery when
            usesIndexRecoveryText
            && recovery.Code = VersionControlCodes.Recovery.ReconcileIndex
            ->
            Some
                "The changes are saved in a commit, but Git's file index is out of date. Close other Git programs for this ARC, then run \"git reset\" in the ARC folder."
        | Some recovery -> recovery.Instructions
        | None -> None

    // The library message names no path, so the sentence lists the first few paths that the library reports.
    let message =
        if
            failure.Code = VersionControlCodes.PathNotFound
            && failure.AffectedPaths.Length > 0
        then
            let maxShown = 5

            let shown =
                failure.AffectedPaths
                |> Array.truncate maxShown
                |> Array.map (fun path -> $"'{path}'")
                |> String.concat ", "

            let more = failure.AffectedPaths.Length - maxShown

            if more > 0 then
                $"{failure.Message} Affected paths: {shown} and {more} more."
            else
                $"{failure.Message} Affected paths: {shown}."
        else
            failure.Message

    match instructions with
    | Some instructions when not (String.IsNullOrWhiteSpace instructions) -> $"{message} {instructions}"
    | _ -> message

/// A partial success counts as a success here. The explorer actions have no warning
/// channel, so a partial that lands on one of these calls (a session-open failure the
/// host reports once) is dropped on this path.
let toUnitResult (result: Result<OperationResultDto<unit>, string>) : Result<unit, string> =
    match result with
    | Error message -> Error message
    | Ok(OperationResultDto.Succeeded _) -> Ok()
    | Ok(OperationResultDto.PartiallySucceeded _) -> Ok()
    | Ok(OperationResultDto.Failed failure) -> Error(failureMessage failure)

let recoveryCode (failure: OperationFailureDto) =
    failure.RecoveryAction |> Option.map _.Code

let isCanceled (failure: OperationFailureDto) =
    failure.Category = FailureCategoryDto.Canceled

let needsDependencyInstall (failure: OperationFailureDto) =
    failure.Category = FailureCategoryDto.DependencyMissing

/// Remote provisioning is offered only when the workspace has no publication target
/// at all. An unreachable target is an outage, never a missing project.
let needsPublishTarget (failure: OperationFailureDto) =
    failure.Code = VersionControlCodes.PublishTargetMissing

/// The pending recovery a completed write leaves behind, from the structured partial
/// failure the library attached to it.
let recoveryOfPartial (partial: OperationFailureDto option) : GitPendingRecovery option =
    partial
    |> Option.bind (fun failure ->
        match recoveryCode failure with
        | Some code when code = VersionControlCodes.Recovery.RetryMaterialization ->
            Some(GitPendingRecovery.RetryMaterialization(failureMessage failure))
        | Some code when code = VersionControlCodes.Recovery.RetryPublish ->
            Some(GitPendingRecovery.RetryPublish(failureMessage failure))
        | Some code when code = VersionControlCodes.Recovery.RemoveIndexLock ->
            Some(GitPendingRecovery.ClearStaleLock(failure.RecoveryAction |> Option.bind _.Instructions))
        | _ -> None
    )

/// The message of a large-file download that a held index lock stopped. The failure keeps the
/// hydration code and carries the lock removal as its recovery. The message leaves out the lock
/// instructions, because the offer that uses it opens after the lock is removed.
let lockBlockedDownloadMessage (partial: OperationFailureDto option) : string option =
    partial
    |> Option.filter (fun failure ->
        failure.Code = VersionControlCodes.HydrationFailed
        && recoveryCode failure = Some VersionControlCodes.Recovery.RemoveIndexLock
    )
    |> Option.map _.Message

/// The text of the download offer after a Download now retry stopped at a Git lock. The library message of that
/// failure says the lock is still held, which is no longer true when the offer opens.
let retryLockStoppedMessage =
    "The download of the large files stopped at a Git lock."

/// The large objects whose file git-lfs already wrote while the index entry stayed stale, as after a checkout that a
/// held lock stopped. The listing marks such an object as materialized because the file has the full size, and the
/// status lists the path as modified. The status has no split between index and working tree, so a path counts when
/// it is a plain modification. An added, deleted, renamed or conflicted path is left out. The library restages only
/// files that hold the committed object and refuses a real edit, so a plain modification that is an edit does no harm.
let staleLfsPaths (objects: ObjectStateDto[]) (status: WorkspaceStatusDto) : string[] =
    let modified =
        status.Changes
        |> Array.filter (fun change -> change.Kind = FileChangeKindDto.Modified && change.OldPath.IsNone)
        |> Array.map _.Path
        |> Set.ofArray

    objects
    |> Array.filter (fun objectState -> objectState.IsMaterialized && modified.Contains objectState.Path)
    |> Array.map _.Path
    |> Array.distinct

/// Whether every path of the failure was refused because it has a local edit. The restage of a stale path and the
/// download of a pending path ask for paths that may hold real edits, and the library refuses those with its own code.
let private isLocalChangesRefusal (failure: OperationFailureDto) =
    failure.Code = VersionControlCodes.LocalChanges

/// The dialog the sidebar shows for a recovery. Confirm runs it, cancel keeps the
/// workspace as it is. Restoring interrupted files discards local changes on them.
let recoveryDialog (recovery: GitPendingRecovery) : GitSidebarConfirmationDialog =
    match recovery with
    | GitPendingRecovery.RestoreInterruptedPaths interrupted ->
        let separator = ", "
        let files = String.Join(separator, interrupted.AffectedPaths)

        let instructions =
            interrupted.Instructions
            |> Option.map (fun text -> $" {text}")
            |> Option.defaultValue ""

        {
            Title = "Update interrupted"
            Message =
                $"The update was canceled while these files may have been partially rewritten: {files}.{instructions} Restore discards the current content of exactly these files. Keep leaves them for review."
            ConfirmLabel = "Restore listed files"
            CancelLabel = "Keep files"
        }
    | GitPendingRecovery.RetryMaterialization message -> {
        Title = "Large files not downloaded"
        Message = $"{message} Download the missing large files now?"
        ConfirmLabel = "Download now"
        CancelLabel = "Later"
      }
    | GitPendingRecovery.RetryPublish message -> {
        Title = "Online sync pending"
        Message = message
        ConfirmLabel = "Publish now"
        CancelLabel = "Later"
      }
    | GitPendingRecovery.ClearStaleLock _ -> {
        Title = "Repository is locked"
        Message =
            "Git's index is locked. Another program may be using the repository, or an interrupted operation left the lock behind. Close other Git programs for this ARC, then remove the lock."
        ConfirmLabel = "Remove lock"
        CancelLabel = "Leave it"
      }
    | GitPendingRecovery.ClearCloneTarget(targetPath, instructions) ->
        let detail =
            instructions |> Option.map (fun text -> $" {text}") |> Option.defaultValue ""

        {
            Title = "Clone folder not cleaned up"
            Message = $"The canceled clone could not clean up '{targetPath}'.{detail} Clear the folder and clone again."
            ConfirmLabel = "OK"
            CancelLabel = "Dismiss"
        }

let private tryGetPathLeaf (pathValue: string) =
    pathValue
    |> Option.ofObj
    |> Option.map (fun value -> value.Trim().TrimEnd('/', '\\'))
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.bind (fun trimmed ->
        trimmed.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.tryLast
    )
    |> Option.filter (String.IsNullOrWhiteSpace >> not)

let private publishRenamePrompt message model : GitPublishRenamePrompt =
    let currentName =
        model.CurrentArcPath |> Option.bind tryGetPathLeaf |> Option.defaultValue "ARC"

    {
        CurrentName = currentName
        Message = message
        Owners = model.PublishOwners |> Option.defaultValue [||]
        SelectedNamespaceId = model.PublishOwnerChoice |> Option.bind _.NamespaceId
    }

/// The owners a first publish offers: the personal namespace, then the groups in the given
/// order. A group that does not accept new projects is disabled and carries the reason.
let publishOwnersOf (dto: PublishOwnersDto) : GitPublishOwner[] =
    let disabledReason (group: PublishGroupDto) =
        if group.can_create_projects then
            None
        else
            match group.project_creation_level with
            | Some "noone" -> Some "Nobody can create repositories in this group."
            | Some "administrator" -> Some "Only administrators can create repositories in this group."
            | Some "owner" -> Some "Only owners of this group can create repositories in it."
            | Some "maintainer" -> Some "Only maintainers and owners of this group can create repositories in it."
            | Some "developer" ->
                Some "You need at least the Developer role in this group to create repositories in it."
            | _ -> Some "You can't create repositories in this group."

    [|
        {
            NamespaceId = None
            Label = $"{dto.username} (personal)"
            DisabledReason = None
        }
        yield!
            dto.groups
            |> Array.map (fun group -> {
                NamespaceId = Some group.id
                Label = group.full_path
                DisabledReason = disabledReason group
            })
    |]

let private hasEnabledGroup (owners: GitPublishOwner[]) =
    owners
    |> Array.exists (fun owner -> owner.NamespaceId.IsSome && owner.DisabledReason.IsNone)

/// The owner the upload goes to when the dialog offered no owners.
let private personalPublishOwner: GitPublishOwner = {
    NamespaceId = None
    Label = "personal"
    DisabledReason = None
}

/// True when renaming the ARC folder to the given name leaves its path unchanged. The main
/// process refuses such a rename, so the check joins the parent of the current path with the
/// name and compares both with PathHelpers.normalizePathForFsComparison, as ArcVaultHelper does.
let private keepsArcFolderName (currentArcPath: string) (name: string) =
    let comparable = Swate.Components.Shared.PathHelpers.normalizePathForFsComparison

    let target =
        match Swate.Components.Shared.PathHelpers.tryGetParentPath currentArcPath with
        | Some parent -> $"{parent}/{name}"
        | None -> name

    comparable target = comparable currentArcPath

let private refreshFailure (refreshResult: GitRefreshResult) : OperationFailureDto option =
    match refreshResult.Session, refreshResult.Status, refreshResult.Refs, refreshResult.LfsSettings with
    | Error failure, _, _, _ -> Some failure
    | _, Error failure, _, _ -> Some failure
    | _, _, Error failure, _ -> Some failure
    | _, _, _, Error failure -> Some failure
    | Ok _, Ok _, Ok _, Ok _ -> None

let private refreshErrorMessage (refreshResult: GitRefreshResult) =
    refreshFailure refreshResult |> Option.map failureMessage

/// A workspace whose folder is not under version control at all. The sidebar then
/// offers to initialize one and shows no error.
let isMissingRepository (failure: OperationFailureDto) =
    failure.Code = VersionControlCodes.WorkspaceUnmanaged

let private enqueuePendingMaterialization
    (sessionId: string)
    (pendingMaterializationPath: (string * bool) option)
    (pendingMaterializations: (string * bool) list)
    (pendingMaterializationSessionId: string option)
    =
    match pendingMaterializationPath with
    | None -> pendingMaterializations, pendingMaterializationSessionId
    | Some(path, localObject) ->
        let existingMaterializations =
            if pendingMaterializationSessionId = Some sessionId then
                pendingMaterializations
            else
                []

        if
            existingMaterializations
            |> List.exists (fun (queuedPath, _) -> queuedPath = path)
        then
            let updatedMaterializations =
                existingMaterializations
                |> List.map (fun (queuedPath, queuedLocalObject) ->
                    if queuedPath = path then
                        queuedPath, (queuedLocalObject || localObject)
                    else
                        queuedPath, queuedLocalObject
                )

            updatedMaterializations, Some sessionId
        else
            existingMaterializations @ [ (path, localObject) ], Some sessionId

let private pendingMaterializationsForSession
    (sessionId: string)
    (pendingMaterializationSessionId: string option)
    (pendingMaterializations: (string * bool) list)
    =
    if pendingMaterializationSessionId = Some sessionId then
        pendingMaterializations
    else
        []

let applyStatus (status: WorkspaceStatusDto) (model: GitState) =
    let mappedChanges = mapChanges status

    let nextSelectedPath =
        model.SelectedChangePath
        |> Option.filter (fun selectedPath -> mappedChanges |> Array.exists (fun change -> change.Path = selectedPath))

    let pendingMaterializations, pendingMaterializationSessionId =
        match status.ActiveConflictSession, model.PendingMaterializationSessionId with
        | Some conflict, Some sessionId when conflict.Handle.SessionId = sessionId ->
            model.PendingMaterializations, model.PendingMaterializationSessionId
        | _ -> [], None

    {
        model with
            Status = mapStatus status
            ChangedFiles = mappedChanges
            SelectedChangePath = nextSelectedPath
            WorkspaceVersion = Some status.WorkspaceVersion
            ActiveConflict = status.ActiveConflictSession
            PendingMaterializations = pendingMaterializations
            PendingMaterializationSessionId = pendingMaterializationSessionId
    }

let private applyRefreshResult (refreshResult: GitRefreshResult) (model: GitState) =
    let modelWithStatus =
        match refreshResult.Status with
        | Ok status -> applyStatus status model
        | Error _ -> {
            model with
                Status = GitState.Empty.Status
                ChangedFiles = [||]
                SelectedChangePath = None
                WorkspaceVersion = None
                ActiveConflict = None
          }

    let modelWithBranches =
        match refreshResult.Status, refreshResult.Refs with
        | Ok status, Ok refs -> {
            modelWithStatus with
                Refs = refs
                BranchOptions = mapBranches (status.Synchronization |> Option.bind _.TargetRef) refs
          }
        | _ -> {
            modelWithStatus with
                Refs = [||]
                BranchOptions = [||]
          }

    let modelWithSettings =
        match refreshResult.LfsSettings with
        | Ok settings -> {
            modelWithBranches with
                LfsAutoTrackThresholdMb =
                    settings.AutoPolicyThresholdMb
                    |> Option.defaultValue model.UserDefaults.LfsAutoTrackThresholdMb
                DiffIndexingLimitMb =
                    settings.DiffIndexingLimitMb
                    |> Option.defaultValue model.UserDefaults.DiffIndexingLimitMb
                DownloadLargeFiles = settings.MaterializeLargeObjects
          }
        | Error _ -> modelWithBranches

    {
        modelWithSettings with
            Services = refreshResult.Session |> Result.toOption |> Option.map _.Services
            OriginRemoteRepositoryWebUrl = refreshResult.OriginRemoteRepositoryWebUrl
            RepositoryAvailability = GitRepositoryAvailability.Ready
            RefreshState = GitRefreshState.Idle
            ErrorNotice = refreshErrorMessage refreshResult
            WarningNotice = model.PendingRefreshWarningNotice
            PendingRefreshWarningNotice = None
    }

let private applyWriteSuccessModel model success =
    match success with
    | UnitSuccess success ->
        let refreshedModel = applyRefreshResult success.Refresh model

        let selectionAdjustedModel =
            match success.SelectedChangePath with
            | Some selectedChangePath -> {
                refreshedModel with
                    SelectedChangePath = selectedChangePath
              }
            | None -> refreshedModel

        selectionAdjustedModel,
        success.PageChange,
        success.Warning,
        recoveryOfPartial success.Partial,
        success.Partial |> Option.map failureMessage,
        success.Published
    | CloneSuccess _ -> model, GitPageChange.NoChange, None, None, None, None

let nextRefreshRequestId (model: GitState) = model.RefreshRequestId + 1

let nextPageLoadRequestId (model: GitState) = model.PageLoadRequestId + 1

let nextArcSessionId (model: GitState) = model.ArcSessionId + 1

/// Paths are exact repository keys. Duplicates are dropped, nothing is trimmed.
let private distinctPaths (paths: string[]) =
    paths |> Array.filter (String.IsNullOrEmpty >> not) |> Array.distinct

let prepareCommitSelection (request: GitSidebarCommitSelectionRequest) = {
    BusyOperation = GitBusyOperation.CommittingSelectedChanges
    NormalizedMessage = request.Message.Trim()
    PathsToCommit = distinctPaths request.Paths
}

let prepareCommitAll (state: GitState) (message: string) = {
    BusyOperation = GitBusyOperation.CommittingAllChanges
    NormalizedMessage = message.Trim()
    PathsToCommit = state.ChangedFiles |> Array.map _.Path |> distinctPaths
}

let buildUpdatedLfsSettings
    (state: GitState)
    (thresholdMb: int option)
    (downloadLargeFiles: bool option)
    (diffIndexingLimitMb: int option)
    : StoragePolicySettingsDto =
    {
        AutoPolicyThresholdMb = Some(thresholdMb |> Option.defaultValue state.LfsAutoTrackThresholdMb)
        MaterializeLargeObjects = downloadLargeFiles |> Option.defaultValue state.DownloadLargeFiles
        DiffIndexingLimitMb = Some(diffIndexingLimitMb |> Option.defaultValue state.DiffIndexingLimitMb)
    }

/// Resolves a caller-provided reply callback synchronously when the command runs.
let private resolveReplyCmd (reply: Reply<'T>) (result: Result<'T, string>) : Cmd<'msg> = [
    fun _dispatch -> reply result
]

let private applyPageChangeCmd (setPageState: PageState option -> unit) =
    function
    | GitPageChange.NoChange -> Cmd.none
    | GitPageChange.Set page -> [
        fun _dispatch -> setPageState (Some page)
      ]
    | GitPageChange.Clear -> [
        fun _dispatch -> setPageState None
      ]

let private reportErrorCmd (deps: GitDependencies) (title: string) (message: string) : Cmd<Msg> = [
    fun _dispatch -> deps.reportError { Title = title; Message = message }
]

/// The checkbox is a convenience. The update logs a failed call and never shows it to the user.
let private logUploadRightAwayFailure (action: string) (detail: string) =
    Browser.Dom.console.error ($"Could not {action} the Upload right away choice: {detail}")

let private failureOfResult (result: Result<OperationResultDto<'T>, string>) =
    match result with
    | Error message -> Some message
    | Ok(OperationResultDto.Failed failure) -> Some failure.Message
    | Ok _ -> None

/// Hands the new checkbox value to the main process. The model already shows it.
let private storeUploadRightAwayCmd (deps: GitDependencies) (value: bool) : Cmd<Msg> =
    Cmd.ofEffect (fun _ ->
        promise {
            try
                let! result = deps.setUploadRightAway value
                failureOfResult result |> Option.iter (logUploadRightAwayFailure "store")
            with err ->
                logUploadRightAwayFailure "store" (string err)
        }
        |> Promise.start
    )

/// Reads the value the main process holds for the open ARC. The answer carries the ARC session
/// id, so the update drops a read that finishes after the ARC changed.
let private loadUploadRightAwayCmd (deps: GitDependencies) (arcSessionId: int) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        promise {
            try
                let! result = deps.getUploadRightAway ()

                match result with
                | Ok(OperationResultDto.Succeeded outcome)
                | Ok(OperationResultDto.PartiallySucceeded(outcome, _)) ->
                    dispatch (UploadRightAwayLoaded(arcSessionId, outcome.Value))
                // A failed result means the main process holds no value, so the checkbox keeps its own.
                | Ok(OperationResultDto.Failed _) -> ()
                | Error message -> logUploadRightAwayFailure "load" message
            with err ->
                logUploadRightAwayFailure "load" (string err)
        }
        |> Promise.start
    )

/// The first refresh of an ARC that reaches an open session starts the read of the stored
/// checkbox value. Later refreshes of the same ARC do not read again, because the load has left NotStarted.
let private startUploadRightAwayLoad
    (deps: GitDependencies)
    (refreshResult: GitRefreshResult)
    (model: GitState)
    : GitState * Cmd<Msg> =
    match model.UploadRightAwayLoad, refreshResult.Session with
    | UploadRightAwayLoad.NotStarted, Ok _ ->
        {
            model with
                UploadRightAwayLoad = UploadRightAwayLoad.Pending
        },
        loadUploadRightAwayCmd deps model.ArcSessionId
    | _ -> model, Cmd.none

let private titleForWriteRequest =
    function
    | Fetch -> "Could not fetch changes"
    | Pull _ -> "Could not pull changes"
    | Push _ -> "Could not push changes"
    | PrimarySave _ -> "Could not save changes"
    | Clone _ -> "Could not clone repository"
    | CommitSelection _
    | CommitAll _ -> "Could not commit changes"
    | DiscardSelection _ -> "Could not discard changes"
    | SaveLfsSettings(GitBusyOperation.SavingDiffIndexingLimit, _) -> "Could not save the background indexing limit"
    | SaveLfsSettings _ -> "Could not save Git LFS settings"
    | PruneLfsCache -> "Could not clean Git LFS cache"
    | DedupLfsStorage -> "Could not reduce Git LFS storage"
    | CreateBranch _ -> "Could not create branch"
    | SwitchBranch _ -> "Could not switch branch"
    | FinalizeMerge -> "Could not finalize merge"
    | AbandonMerge -> "Could not abandon merge"
    | ClearStaleLock -> "Could not clear the stale lock"
    | RestoreInterruptedPaths _ -> "Could not restore interrupted files"
    | RetryMaterialization -> "Could not download large files"

let private reportWriteErrorCmd deps request message =
    reportErrorCmd deps (titleForWriteRequest request) message

let private isLocalPrimarySaveFailure (success: WriteSuccess) =
    let partialFailure =
        match success with
        | UnitSuccess success -> success.Partial
        | CloneSuccess _ -> None

    partialFailure
    |> Option.exists (fun failure ->
        let recovery = recoveryCode failure
        recovery = Some VersionControlCodes.Recovery.ReconcileIndex
    )

let private withBusyOperation busyOperation model = {
    model with
        BusyOperation = busyOperation
        BusyNotice = busyOperation |> Option.bind busyNoticeFromOperation
        CurrentProgress = None
        CurrentProgressPhase = None
        CurrentSteps = [||]
        CurrentStepPhases = [||]
        CurrentLog = OperationLog.empty
        OperationReported = false
        OperationStartedAt = busyOperation |> Option.map (fun _ -> JS.Constructors.Date.now ())
        CurrentOperation = None
        RefreshOperationId = None
}

let private startRefreshRequest requestId model = {
    model with
        RefreshRequestId = requestId
        RefreshState = GitRefreshState.Loading
}

/// A transport failure (the IPC call itself failed) as a structured failure, so the
/// flows below match on one shape.
let private transportFailure (message: string) : OperationFailureDto = {
    Category = FailureCategoryDto.ProviderError
    Code = VersionControlCodes.TransportError
    Message = message
    StateChanged = false
    Retryable = true
    AffectedPaths = [||]
    RecoveryAction = None
    Details = [||]
    RevisionEvidence = [||]
    DiffDetail = None
}

/// Flattens the transport error and the structured result: Ok carries the outcome and
/// the partial failure when there is one, Error carries the failure.
let private toResult (call: JS.Promise<Result<OperationResultDto<'T>, string>>) = promise {
    let! result = call

    return
        match result with
        | Error message -> Error(transportFailure message)
        | Ok(OperationResultDto.Succeeded outcome) -> Ok(outcome, None)
        | Ok(OperationResultDto.PartiallySucceeded(outcome, failure)) -> Ok(outcome, Some failure)
        | Ok(OperationResultDto.Failed failure) -> Error failure
}

let private request (deps: GitDependencies) : OperationRequestDto = { OperationId = deps.newOperationId () }

let private refreshAllAsync (deps: GitDependencies) = promise {
    let! sessionResult = toResult (deps.getSessionInfo (request deps))

    match sessionResult with
    | Error failure ->
        return {
            Session = Error failure
            Status = Error failure
            Refs = Error failure
            LfsSettings = Error failure
            OriginRemoteRepositoryWebUrl = None
        }
    | Ok(sessionOutcome, _) ->
        let session = sessionOutcome.Value
        // The session info runs first because it decides whether the repository URL call is
        // needed. The other four calls are independent and run together. and! awaits them
        // through Promise.all, so a rejected call never stays unobserved.
        let! statusResult = toResult (deps.getStatus (request deps))
        and! refsResult = toResult (deps.listRefs (request deps))

        and! settingsResult = promise {
            let! result = toResult (deps.getStoragePolicySettings (request deps))
            return result |> Result.map (fun (outcome, _) -> outcome.Value)
        }

        and! webUrl =
            if session.Services.RepositoryBrowser then
                promise {
                    let! result = toResult (deps.getRepositoryWebUrl (request deps))

                    return
                        match result with
                        | Ok(outcome, _) -> outcome.Value
                        | Error _ -> None
                }
            else
                promise { return None }

        return {
            Session = Ok session
            Status = statusResult |> Result.map (fun (outcome, _) -> outcome.Value)
            Refs = refsResult |> Result.map (fun (outcome, _) -> outcome.Value)
            LfsSettings = settingsResult
            OriginRemoteRepositoryWebUrl = webUrl
        }
}

let private versionOf (refreshResult: GitRefreshResult) =
    refreshResult.Status |> Result.toOption |> Option.map _.WorkspaceVersion

let private runInitRepositoryAsync (deps: GitDependencies) (arcPath: string) = promise {
    let! initResult =
        toResult (
            deps.initializeWorkspace {
                OperationId = deps.newOperationId ()
                TargetPath = arcPath
            }
        )

    match initResult with
    | Error failure -> return Error(failureMessage failure)
    | Ok _ -> return Ok()
}

let private loadPageAsync
    (deps: GitDependencies)
    (activeConflict: ConflictSessionSummaryDto option)
    (workspaceVersion: string option)
    (change: GitSidebarChange)
    =
    if not change.IsConflicted then
        promise { return Ok GitPageChange.NoChange }
    else
        promise {
            let path = change.Path

            match activeConflict, workspaceVersion with
            | Some conflict, Some version ->
                let! page = deps.loadConflictPage conflict version path
                return page |> Result.map GitPageChange.Set
            | _ ->
                let! status = toResult (deps.getStatus (request deps))

                match status with
                | Ok(outcome, _) ->
                    match outcome.Value.ActiveConflictSession with
                    | Some conflict ->
                        let! page = deps.loadConflictPage conflict outcome.Value.WorkspaceVersion path
                        return page |> Result.map GitPageChange.Set
                    | None -> return Ok GitPageChange.NoChange
                | Error failure -> return Error(failureMessage failure)
        }

/// Opens a text diff of one changed file and pages through it. Every response carries the
/// generation of the page it was requested for. A response for a page that was left or
/// replaced is dropped, and a handle that arrives for such a page is closed at once.
module GitDiffPageLoader =

    module Paged = Swate.Components.Page.GitComparison.GitPagedDiffTypes
    module Presentation = Renderer.GitDiffPresentation

    [<Literal>]
    let MaxLoadedPages = 8

    /// The time background page arrivals wait before the viewer shows them. The arrivals in that
    /// time show as one update.
    [<Literal>]
    let PublishCoalesceMs = 100

    /// The size limit of the loaded pages. Sizes are JSON lengths in UTF-16 code units, which
    /// match bytes for ASCII text.
    let MaxLoadedBytes = 8.0 * 1024.0 * 1024.0

    [<Literal>]
    let ContextLines = 3

    [<Literal>]
    let ExpandLineCount = 100

    [<Literal>]
    let LineSliceUtf16 = 8192

    // Failure codes of the text diff calls, produced by the library.

    /// The temp drive has less free space than the reserve. The session stays open, replays keep
    /// working, and a later request may succeed.
    [<Literal>]
    let DiffTempSpaceLow = "diff_temp_space_low"

    /// A memory diff refused a read or a write because of its budget. The session stays open and
    /// replays keep working.
    [<Literal>]
    let DiffMemoryBudgetReached = "diff_memory_budget_reached"

    [<Literal>]
    let DiffContentNotText = "diff_content_not_text"

    [<Literal>]
    let SourceChanged = "source_changed"

    [<Literal>]
    let DiffWorkerFailed = "diff_worker_failed"

    /// The worker closed the session behind the handle, after it sat idle or when a newer diff took
    /// its slot.
    [<Literal>]
    let DiffSessionClosed = "diff_session_closed"

    /// The worker dropped the suspended read that the continuation belongs to.
    [<Literal>]
    let ContinuationMismatch = "continuation_mismatch"

    /// The preparation token of an encoding choice expired or no longer matches the sources.
    [<Literal>]
    let PreparationMismatch = "preparation_mismatch"

    /// A side the library read as UTF-8 holds bytes that are not UTF-8 further on. DiffDetail
    /// names the side.
    [<Literal>]
    let DiffEncodingMismatch = "diff_encoding_mismatch"

    /// The encodings offered for a side that turned out not to be UTF-8. The library sends no
    /// preview for them.
    let private mismatchCandidates: EncodingCandidateDto[] = [|
        { Encoding = "utf-8"; Preview = "" }
        {
            Encoding = "windows-1252"
            Preview = ""
        }
    |]

    let changeKindOf (change: GitSidebarChange) =
        match change.IndexStatus with
        | "A" -> Paged.GitDiffChangeKind.Added
        | "D" -> Paged.GitDiffChangeKind.Deleted
        | _ -> Paged.GitDiffChangeKind.Modified

    /// The size a DTO takes on the wire, measured as the length of its JSON in UTF-16 code units.
    let private payloadBytes (value: obj) = float (JS.JSON.stringify value).Length

    /// A page in one of these states shows only a status message and takes no further requests.
    let private isSettled (status: GitDiffPageStatus) =
        match status with
        | GitDiffPageStatus.Blocked _
        | GitDiffPageStatus.SourceChanged
        | GitDiffPageStatus.WorkerFailed _
        | GitDiffPageStatus.Failed _ -> true
        | _ -> false

    let private isReopening (status: GitDiffPageStatus) =
        match status with
        | GitDiffPageStatus.Reopening _ -> true
        | _ -> false

    let failureStatus (failure: OperationFailureDto) =
        match failure.Code, failure.DiffDetail with
        | DiffContentNotText, detail ->
            GitDiffPageStatus.Blocked(
                detail |> Option.map _.Side,
                GitDiffBlockReason.NotText(detail |> Option.map _.Evidence |> Option.defaultValue "")
            )
        | SourceChanged, _ -> GitDiffPageStatus.SourceChanged
        | DiffWorkerFailed, _ -> GitDiffPageStatus.WorkerFailed(failureMessage failure)
        | DiffEncodingMismatch, Some detail -> GitDiffPageStatus.EncodingChoice(detail.Side, None, mismatchCandidates)
        // A session without a text diff service answers every diff call this way.
        | VersionControlCodes.ServiceUnavailable, _ ->
            GitDiffPageStatus.Blocked(None, GitDiffBlockReason.ProviderUnsupported)
        | _ -> GitDiffPageStatus.Failed(failureMessage failure)

    /// Statuses that end the diff when a request on the shown rows fails with them. Any other
    /// failure of an expansion, a line slice or a replay stays with the control that asked.
    let private endsDiff (status: GitDiffPageStatus) =
        match status with
        | GitDiffPageStatus.SourceChanged
        | GitDiffPageStatus.Blocked _
        | GitDiffPageStatus.EncodingChoice _
        | GitDiffPageStatus.WorkerFailed _ -> true
        | _ -> false

    let private valueOf (result: Result<OperationResultDto<'T>, string>) : Result<'T, GitDiffPageStatus> =
        match result with
        | Error message -> Error(GitDiffPageStatus.Failed message)
        | Ok(OperationResultDto.Succeeded outcome) -> Ok outcome.Value
        | Ok(OperationResultDto.PartiallySucceeded(outcome, _)) -> Ok outcome.Value
        | Ok(OperationResultDto.Failed failure) -> Error(failureStatus failure)

    let blockedStatus (blocker: DiffBlockerDto) =
        match blocker with
        | DiffBlockerDto.Binary(side, evidence) ->
            GitDiffPageStatus.Blocked(Some side, GitDiffBlockReason.Binary evidence)
        | DiffBlockerDto.LocalContentUnavailable(side, objectId) ->
            GitDiffPageStatus.Blocked(Some side, GitDiffBlockReason.LocalContentUnavailable objectId)
        | DiffBlockerDto.NotRegularFile side -> GitDiffPageStatus.Blocked(Some side, GitDiffBlockReason.NotRegularFile)
        | DiffBlockerDto.ProviderUnsupported -> GitDiffPageStatus.Blocked(None, GitDiffBlockReason.ProviderUnsupported)
        | DiffBlockerDto.BlobTooLargeForMemory(side, blobBytes, limitBytes, cause) ->
            GitDiffPageStatus.Blocked(
                Some side,
                GitDiffBlockReason.TooLargeForMemory(
                    Presentation.number blobBytes,
                    Presentation.number limitBytes,
                    cause
                )
            )
        | DiffBlockerDto.EncodingRequired(side, token, candidates) ->
            GitDiffPageStatus.EncodingChoice(side, Some token, candidates)

    /// Shows the page the user just selected.
    let publishCmd (setPageState: PageState option -> unit) (page: GitDiffPageData) : Cmd<Msg> = [
        fun _ -> setPageState (Some(PageState.GitDiffPage page))
    ]

    /// Shows the updated page only while the page state still shows this diff. An answer that
    /// arrives after the user went to another page leaves that page in place.
    let private publishShown (deps: GitDependencies) (page: GitDiffPageData) =
        deps.updatePageState (fun current ->
            match current with
            | Some(PageState.GitDiffPage shown) when shown.Generation = page.Generation ->
                Some(PageState.GitDiffPage page)
            | other -> other
        )

    /// The page the delayed publish of background arrivals shows. It is set while a publish is waiting.
    let mutable private pendingPublish: GitDiffPageData option = None

    /// Shows the page at once. A delayed publish that waits is older than this page and drops out.
    let private publishNowCmd (deps: GitDependencies) (page: GitDiffPageData) : Cmd<Msg> = [
        fun _ ->
            pendingPublish <- None
            publishShown deps page
    ]

    /// Shows the page after PublishCoalesceMs. The pages that arrive in the meantime replace it, so
    /// background arrivals that follow each other render the viewer once.
    let private publishLaterCmd (deps: GitDependencies) (page: GitDiffPageData) : Cmd<Msg> = [
        fun _ ->
            let waiting = pendingPublish.IsSome
            pendingPublish <- Some page

            if not waiting then
                deps.delay PublishCoalesceMs
                |> Promise.iter (fun () ->
                    match pendingPublish with
                    | Some latest ->
                        pendingPublish <- None
                        publishShown deps latest
                    | None -> ()
                )
    ]

    let private send
        (call: 'Request -> JS.Promise<Result<'T, string>>)
        (request: 'Request)
        (toMsg: 'Request -> Result<'T, string> -> GitDiffMsg)
        : Cmd<Msg> =
        Cmd.OfPromise.either
            call
            request
            (fun result -> DiffPageMsg(toMsg request result))
            (fun error -> DiffPageMsg(toMsg request (Error error.Message)))

    let private track (operationId: string) (page: GitDiffPageData) = {
        page with
            RunningOperations = operationId :: page.RunningOperations
    }

    let private finish (operationId: string) (page: GitDiffPageData) = {
        page with
            RunningOperations = page.RunningOperations |> List.filter (fun running -> running <> operationId)
    }

    /// Closes a handle nobody uses any more. Nothing waits for the answer, so errors are ignored.
    let private closeHandle (deps: GitDependencies) (handle: DiffHandleDto) =
        deps.textDiff.closeTextDiff {
            OperationId = deps.newOperationId ()
            HandleId = handle.Id
            HandleVersion = handle.Version
        }
        |> Promise.catch (fun error -> Error error.Message)
        |> Promise.start

    let private closeHandleCmd (deps: GitDependencies) (handle: DiffHandleDto) : Cmd<Msg> = [
        fun _ -> closeHandle deps handle
    ]

    /// Cancels the running requests of a page that was left and closes its handle. Nothing
    /// waits for either answer, so errors are ignored.
    let closeCmd (deps: GitDependencies) (page: GitDiffPageData) : Cmd<Msg> = [
        for operationId in page.RunningOperations do
            fun _ ->
                deps.cancelOperation { OperationId = operationId }
                |> Promise.catch (fun error -> Error error.Message)
                |> Promise.start
        match page.Handle with
        | Some handle -> yield! closeHandleCmd deps handle
        | None -> ()
    ]

    let private sendOpen (deps: GitDependencies) (page: GitDiffPageData) (request: OpenTextDiffRequestDto) =
        track request.OperationId page,
        send
            deps.textDiff.openTextDiff
            request
            (fun request result -> GitDiffMsg.OpenCompleted(page.Generation, request, result))

    let private openFresh (deps: GitDependencies) (page: GitDiffPageData) (preparationTokenId: string option) =
        sendOpen deps page {
            OperationId = deps.newOperationId ()
            Path = page.Path
            PreviousPath = page.PreviousPath
            PreparationTokenId = preparationTokenId
            PreviousEncoding = page.PreviousEncoding
            CurrentEncoding = page.CurrentEncoding
            ContextLines = ContextLines
            Continuation = None
        }

    /// A diff page for the change that is still opening, and the command that opens it.
    let start (deps: GitDependencies) (generation: int) (change: GitSidebarChange) =
        let page: GitDiffPageData = {
            Path = change.Path
            PreviousPath = change.OriginalPath
            ChangeKind = Some(changeKindOf change)
            Generation = generation
            Handle = None
            SourceInfos = None
            Pages = [||]
            RequestedPageIndex = 0
            NextCursor = None
            NextRequest = None
            PendingLineSlices = []
            RestartedLineSlices = []
            ExpandingGaps = []
            PendingReplay = None
            VisiblePages = []
            KeepRequestedPage = false
            Indexing = false
            IndexingPaused = false
            JournalBytes = 0.0
            Storage = None
            Stop = None
            FailureCause = None
            NextFailed = false
            NextFailure = None
            FailedGaps = []
            FailedLineSlices = []
            FailedReplays = []
            Progress = None
            Pending = None
            OutputComplete = false
            Status = GitDiffPageStatus.Opening
            PreviousEncoding = None
            CurrentEncoding = None
            RunningOperations = []
            ScrollTarget = None
        }

        openFresh deps page None

    let private evictedCopy (windowPage: GitDiffWindowPage) = {
        windowPage with
            Parts = [|
                Paged.PagedPart.EvictedPage(windowPage.PageId, windowPage.RowCount)
            |]
            PayloadBytes = 0.0
            IsEvicted = true
            ExpandedGaps = []
    }

    /// Puts the rows of the least recently expanded gap of the page back as a hidden gap with
    /// that gap's id. Gaps the expansion returned stay where they are.
    let private collapseOldestGap (windowPage: GitDiffWindowPage) =
        match windowPage.ExpandedGaps with
        | [] -> windowPage
        | oldest :: rest ->
            let parts =
                windowPage.Parts
                |> Array.map (fun part ->
                    match part with
                    | Paged.PagedPart.ExpandedRows(gapId, _) when gapId = oldest.GapId ->
                        Paged.PagedPart.HiddenGap(oldest.GapId, oldest.Previous, oldest.Current)
                    | _ -> part
                )

            {
                windowPage with
                    Parts = parts
                    PayloadBytes = windowPage.PayloadBytes - oldest.PayloadBytes
                    ExpandedGaps = rest
            }

    /// Replaces the loaded pages farthest from the requested page by placeholders until the
    /// window holds at most MaxLoadedPages pages and MaxLoadedBytes, measured as JSON length in
    /// UTF-16 code units. The requested page stays, and so do the pages the viewer showed at the
    /// last replay request while the requested page is one of them, even when they alone exceed
    /// the limits. When the requested page alone still exceeds MaxLoadedBytes, the rows of its
    /// least recently expanded gaps collapse. The newest expansion stays, since the user just
    /// asked for it. While the diff reopens, the page the new session read last stays as well,
    /// since its pages can hold fewer lines and the page with the target line can land far from
    /// the requested page.
    let evict (page: GitDiffPageData) : GitDiffPageData =
        let anchor = page.RequestedPageIndex

        let lastRead =
            match page.Status with
            | GitDiffPageStatus.Reopening pagesRead when pagesRead > 0 -> Some(pagesRead - 1)
            | _ -> None

        // Once the requested page moves away from the pages of the last replay request, those
        // pages are no longer on screen.
        let visiblePages =
            match page.Pages |> Array.tryItem anchor with
            | Some anchorPage when page.VisiblePages |> List.contains anchorPage.PageId -> page.VisiblePages
            | _ -> []

        // A diff has thousands of evicted pages and a few loaded ones. The loaded pages are found
        // in one pass, and the array is copied only when a page changes.
        let loadedIndices = ResizeArray<int>()
        let mutable loadedBytes = 0.0

        for index in 0 .. page.Pages.Length - 1 do
            let windowPage = page.Pages.[index]

            if not windowPage.IsEvicted then
                loadedIndices.Add index
                loadedBytes <- loadedBytes + windowPage.PayloadBytes

        let pages = ref page.Pages
        let copied = ref false

        let changePage (index: int) (changed: GitDiffWindowPage) =
            if not copied.Value then
                pages.Value <- Array.copy pages.Value
                copied.Value <- true

            pages.Value.[index] <- changed

        let mutable evicting = true

        while evicting do
            if loadedIndices.Count > MaxLoadedPages || loadedBytes > MaxLoadedBytes then
                // The loaded page farthest from the requested one goes first, the lowest index on a tie.
                let mutable victim = -1
                let mutable farthest = -1

                for index in loadedIndices do
                    if
                        abs (index - anchor) > farthest
                        && index <> anchor
                        && Some index <> lastRead
                        && not (visiblePages |> List.contains pages.Value.[index].PageId)
                    then
                        victim <- index
                        farthest <- abs (index - anchor)

                if victim < 0 then
                    evicting <- false
                else
                    loadedBytes <- loadedBytes - pages.Value.[victim].PayloadBytes
                    loadedIndices.Remove victim |> ignore
                    changePage victim (evictedCopy pages.Value.[victim])
            else
                evicting <- false

        let otherBytes =
            loadedIndices
            |> Seq.sumBy (fun index ->
                if index <> anchor then
                    pages.Value.[index].PayloadBytes
                else
                    0.0
            )

        let rec collapse (windowPage: GitDiffWindowPage) =
            if
                not windowPage.IsEvicted
                && otherBytes + windowPage.PayloadBytes > MaxLoadedBytes
                && windowPage.ExpandedGaps.Length > 1
            then
                collapse (collapseOldestGap windowPage)
            else
                windowPage

        if anchor >= 0 && anchor < pages.Value.Length then
            let collapsed = collapse pages.Value.[anchor]

            if not (obj.ReferenceEquals(collapsed, pages.Value.[anchor])) then
                changePage anchor collapsed

        { page with Pages = pages.Value }

    let private position (side: DiffSideDto) (number: float) : GitDiffLinePosition = { Side = side; Number = number }

    /// The lines a part shows on one side. A gap and an evicted page show none.
    let private linesOnSide (side: Paged.PagedDiffSide) (part: Paged.PagedPart) : Paged.PagedLine[] =
        match part with
        | Paged.PagedPart.HunkRows(_, _, _, _, _, rows)
        | Paged.PagedPart.ExpandedRows(_, rows) ->
            rows
            |> Array.choose (fun row ->
                match side with
                | Paged.PagedDiffSide.Previous -> row.Previous
                | Paged.PagedDiffSide.Current -> row.Current
            )
        | Paged.PagedPart.UnalignedRegion(_, previous, current, _, _) ->
            match side with
            | Paged.PagedDiffSide.Previous -> previous
            | Paged.PagedDiffSide.Current -> current
        | Paged.PagedPart.HiddenGap _
        | Paged.PagedPart.EvictedPage _ -> [||]

    /// The first line of the parts and the last line number on each side. A hidden gap covers
    /// the lines of its ranges.
    let private spanOf (parts: Paged.PagedPart[]) : GitDiffPageSpan =
        let firstOf (previous: Paged.PagedLine option) (current: Paged.PagedLine option) =
            match previous, current with
            | Some line, _ -> Some(position DiffSideDto.Previous line.Number)
            | None, Some line -> Some(position DiffSideDto.Current line.Number)
            | None, None -> None

        let first =
            parts
            |> Array.tryPick (
                function
                | Paged.PagedPart.HunkRows(_, _, _, _, _, rows)
                | Paged.PagedPart.ExpandedRows(_, rows) ->
                    rows |> Array.tryPick (fun row -> firstOf row.Previous row.Current)
                | Paged.PagedPart.UnalignedRegion(_, previous, current, _, _) ->
                    firstOf (Array.tryHead previous) (Array.tryHead current)
                | Paged.PagedPart.HiddenGap(_, previous, current) ->
                    if previous.Count > 0.0 then
                        Some(position DiffSideDto.Previous previous.Start)
                    elif current.Count > 0.0 then
                        Some(position DiffSideDto.Current current.Start)
                    else
                        None
                | Paged.PagedPart.EvictedPage _ -> None
            )

        let lastOn (side: Paged.PagedDiffSide) =
            let numbers =
                parts
                |> Array.collect (
                    function
                    | Paged.PagedPart.HiddenGap(_, previous, current) ->
                        let range =
                            match side with
                            | Paged.PagedDiffSide.Previous -> previous
                            | Paged.PagedDiffSide.Current -> current

                        if range.Count > 0.0 then
                            [| range.Start + range.Count - 1.0 |]
                        else
                            [||]
                    | part -> linesOnSide side part |> Array.map _.Number
                )

            if numbers.Length = 0 then None else Some(Array.max numbers)

        {
            First = first
            LastPrevious = lastOn Paged.PagedDiffSide.Previous
            LastCurrent = lastOn Paged.PagedDiffSide.Current
        }

    /// Whether the page shows the line or a later line of the same side.
    let private reaches (line: GitDiffLinePosition) (windowPage: GitDiffWindowPage) =
        let last =
            match line.Side with
            | DiffSideDto.Previous -> windowPage.Span.LastPrevious
            | DiffSideDto.Current -> windowPage.Span.LastCurrent

        last |> Option.exists (fun number -> number >= line.Number)

    /// The page of the DTO. The JSON of the DTO is passed in, since the caller may need it too.
    let private windowPageOfJson (dto: DiffPageDto) (json: string) : GitDiffWindowPage =
        let parts = dto.Parts |> Array.map Presentation.part

        {
            PageId = dto.PageId
            Parts = parts
            RowCount = Paged.displayRowCount parts
            PayloadBytes = float json.Length
            IsEvicted = false
            ExpandedGaps = []
            Span = spanOf parts
        }

    let private windowPage (dto: DiffPageDto) : GitDiffWindowPage =
        windowPageOfJson dto (JS.JSON.stringify dto)

    /// The size of the JSON in UTF-8 bytes. The encoding runs through the TextEncoder of the runtime.
    let private utf8Bytes (json: string) =
        float (System.Text.Encoding.UTF8.GetBytes json).Length

    /// Whether the pages the session answered add up to the limit of the background indexing. A
    /// memory diff has its own budget, so the limit does not apply to it.
    let indexingLimitReached (limitMb: int) (page: GitDiffPageData) =
        match page.Storage with
        | Some(DiffStorageDto.InMemory _) -> false
        | _ -> page.JournalBytes >= float limitMb * 1024.0 * 1024.0

    /// The library refused the request because of where the diff keeps its data. The session stays
    /// usable, so the refusal is not a failure of the diff.
    let private refusalOf (result: Result<OperationResultDto<'T>, string>) : GitDiffStop option =
        match result with
        | Ok(OperationResultDto.Failed failure) ->
            match failure.Code with
            | DiffTempSpaceLow -> Some GitDiffStop.TempSpaceLow
            | DiffMemoryBudgetReached -> Some GitDiffStop.MemoryBudget
            | _ -> None
        | _ -> None

    /// The blocked page of an open that a refusal ended before any row was read.
    let private refusedStatus (refusal: GitDiffStop) =
        match refusal with
        | GitDiffStop.TempSpaceLow -> GitDiffPageStatus.Blocked(None, GitDiffBlockReason.TempSpaceLow)
        | _ -> GitDiffPageStatus.Blocked(None, GitDiffBlockReason.MemoryBudgetReached)

    /// Records the refusal of a read and ends the indexing. The rows and the cursor stay. The
    /// continue button can ask again after a low-space stop and after the 3/4 background stop of a
    /// memory diff, which leaves the rest of the budget for the user. It cannot ask again at the
    /// full budget, and that stop stays.
    let private stopReading (background: bool) (refusal: GitDiffStop) (page: GitDiffPageData) =
        let stop =
            match refusal, page.Stop with
            | _, Some GitDiffStop.MemoryBudget -> GitDiffStop.MemoryBudget
            | GitDiffStop.MemoryBudget, _ when background -> GitDiffStop.MemoryShareUsed
            | _ -> refusal

        {
            page with
                Status =
                    match page.Status with
                    | GitDiffPageStatus.LoadingNext -> GitDiffPageStatus.Ready
                    | status -> status
                Indexing = false
                Stop = Some stop
        }

    /// Requests the page at the cursor, unless that cursor is already being read. The request
    /// remembers whether the background indexing sent it.
    let private readPage (deps: GitDependencies) (page: GitDiffPageData) (cursor: string) (background: bool) =
        match page.Handle, page.NextRequest with
        | None, _ -> page, Cmd.none
        | Some _, Some running when running.Cursor = cursor -> page, Cmd.none
        | Some handle, _ ->
            let request: ReadTextDiffPageRequestDto = {
                OperationId = deps.newOperationId ()
                HandleId = handle.Id
                HandleVersion = handle.Version
                Cursor = cursor
                Background = background
            }

            track request.OperationId {
                page with
                    NextRequest =
                        Some {
                            Cursor = cursor
                            OperationId = request.OperationId
                            Background = background
                        }
            },
            send
                deps.textDiff.readTextDiffPage
                request
                (fun request result -> GitDiffMsg.PageCompleted(page.Generation, request, result))

    /// A Ready page joins the window once. A page id that is already in the window is ignored.
    /// While the diff reopens, the page takes the place of the old page at the same index, and
    /// the requested page stays where the user was.
    /// A Scanning result keeps its continuation as the cursor and asks for the page again with it.
    /// The continuation belongs to the background indexing when the read that answered did.
    let private applyResumablePage
        (deps: GitDependencies)
        (page: GitDiffPageData)
        (background: bool)
        (result: ResumablePageDto)
        =
        // While the diff reopens, only the pages the new session has read count as read.
        let readPages =
            match page.Status with
            | GitDiffPageStatus.Reopening pagesRead -> page.Pages |> Array.truncate pagesRead
            | _ -> page.Pages

        match result with
        | ResumablePageDto.Ready dto when readPages |> Array.exists (fun windowPage -> windowPage.PageId = dto.PageId) ->
            let status =
                match page.Status with
                | GitDiffPageStatus.LoadingNext when page.NextRequest.IsNone -> GitDiffPageStatus.Ready
                | status -> status

            { page with Status = status }, Cmd.none
        | ResumablePageDto.Ready dto ->
            // The JSON is made once and gives both sizes of the page.
            let json = JS.JSON.stringify dto
            let arrived = windowPageOfJson dto json

            let pages, requestedPageIndex, status =
                match page.Status with
                | GitDiffPageStatus.Reopening pagesRead when pagesRead < page.Pages.Length ->
                    page.Pages |> Array.updateAt pagesRead arrived,
                    page.RequestedPageIndex,
                    GitDiffPageStatus.Reopening(pagesRead + 1)
                | GitDiffPageStatus.Reopening pagesRead ->
                    Array.append page.Pages [| arrived |],
                    page.RequestedPageIndex,
                    GitDiffPageStatus.Reopening(pagesRead + 1)
                | _ ->
                    let pages = Array.append page.Pages [| arrived |]

                    // The user went back to another page while this one loaded. That page and
                    // the pages of its replay stay, and the new page is the one that may go.
                    let requestedPageIndex =
                        if page.KeepRequestedPage then
                            page.RequestedPageIndex
                        else
                            pages.Length - 1

                    pages, requestedPageIndex, GitDiffPageStatus.Ready

            evict {
                page with
                    Pages = pages
                    RequestedPageIndex = requestedPageIndex
                    KeepRequestedPage = false
                    JournalBytes = page.JournalBytes + utf8Bytes json
                    // A page the user asked for shows that the temp drive has room again.
                    Stop =
                        match page.Stop with
                        | Some GitDiffStop.TempSpaceLow when not background -> None
                        | stop -> stop
                    NextCursor = dto.NextCursor
                    NextFailed = false
                    NextFailure = None
                    Progress = Some dto.Progress
                    Pending = dto.Pending
                    OutputComplete = dto.OutputComplete
                    Status = status
            },
            Cmd.none
        | ResumablePageDto.Scanning(progress, continuation, pending) ->
            let status =
                match page.Status with
                | GitDiffPageStatus.Reopening _ -> page.Status
                | _ when page.Pages.Length = 0 -> GitDiffPageStatus.Scanning
                | _ -> GitDiffPageStatus.LoadingNext

            readPage
                deps
                {
                    page with
                        Progress = Some progress
                        Pending = pending
                        NextCursor = Some continuation
                        Status = status
                }
                continuation
                background

    let private isGap (gapId: string) (part: Paged.PagedPart) =
        match part with
        | Paged.PagedPart.HiddenGap(id, _, _) -> id = gapId
        | _ -> false

    let private pageIndexOfGap (gapId: string) (page: GitDiffPageData) =
        page.Pages
        |> Array.tryFindIndex (fun windowPage ->
            not windowPage.IsEvicted && windowPage.Parts |> Array.exists (isGap gapId)
        )

    let private showsLine (side: Paged.PagedDiffSide) (number: float) (part: Paged.PagedPart) =
        linesOnSide side part |> Array.exists (fun line -> line.Number = number)

    /// The loaded page that shows the line on that side.
    let private pageIndexOfLine (side: DiffSideDto) (number: float) (page: GitDiffPageData) =
        page.Pages
        |> Array.tryFindIndex (fun windowPage ->
            not windowPage.IsEvicted
            && windowPage.Parts |> Array.exists (showsLine (Presentation.side side) number)
        )

    /// Puts the expanded parts where the gap was. A gap on a page evicted in the meantime is left alone.
    /// Each expansion result is recorded on its own, so its rows can be collapsed again.
    /// Expanding a gap whose rows were collapsed returns the library's recorded first result,
    /// whose remaining gaps the page already shows or has expanded further. Those gaps are
    /// dropped, so no line appears twice.
    let private replaceGap (gapId: string) (parts: DiffPartDto[]) (page: GitDiffPageData) =
        match pageIndexOfGap gapId page with
        | None -> page
        | Some index ->
            let windowPage = page.Pages.[index]
            let bytes = payloadBytes parts

            let shownGapIds =
                windowPage.Parts
                |> Array.choose (
                    function
                    | Paged.PagedPart.HiddenGap(id, _, _)
                    | Paged.PagedPart.ExpandedRows(id, _) when id <> gapId -> Some id
                    | _ -> None
                )
                |> Set.ofArray

            let mapped =
                parts
                |> Array.map Presentation.part
                |> Array.filter (
                    function
                    | Paged.PagedPart.HiddenGap(id, _, _) -> not (shownGapIds.Contains id)
                    | _ -> true
                )

            let replaced =
                windowPage.Parts
                |> Array.collect (fun part -> if isGap gapId part then mapped else [| part |])

            let gapRanges =
                windowPage.Parts
                |> Array.pick (
                    function
                    | Paged.PagedPart.HiddenGap(id, previous, current) when id = gapId -> Some(previous, current)
                    | _ -> None
                )

            let linesOf (pick: Paged.PagedRow -> Paged.PagedLine option) (fallback: Paged.PagedRange) rows =
                match rows |> Array.choose pick with
                | [||] -> { fallback with Count = 0.0 }
                | lines ->
                    ({
                        Start = lines.[0].Number
                        Count = float lines.Length
                    }
                    : Paged.PagedRange)

            let expandedGap =
                mapped
                |> Array.tryPick (
                    function
                    | Paged.PagedPart.ExpandedRows(id, rows) when id = gapId ->
                        Some {
                            GapId = gapId
                            Previous = linesOf _.Previous (fst gapRanges) rows
                            Current = linesOf _.Current (snd gapRanges) rows
                            PayloadBytes = bytes
                        }
                    | _ -> None
                )

            let updated = {
                windowPage with
                    Parts = replaced
                    PayloadBytes = windowPage.PayloadBytes + bytes
                    ExpandedGaps =
                        (windowPage.ExpandedGaps |> List.filter (fun expanded -> expanded.GapId <> gapId))
                        @ Option.toList expandedGap
            }

            {
                page with
                    Pages = page.Pages |> Array.updateAt index updated
                    RequestedPageIndex = index
            }

    /// Merges the slice into every loaded line it continues or replaces.
    let private mergeLine (side: DiffSideDto) (lineDto: DiffLineDto) (page: GitDiffPageData) =
        let slice = Presentation.line lineDto
        let viewSide = Presentation.side side
        let bytes = payloadBytes lineDto

        let merged =
            page.Pages
            |> Array.map (fun windowPage ->
                if windowPage.IsEvicted then
                    windowPage, false
                else
                    let parts =
                        windowPage.Parts
                        |> Array.map (fun part ->
                            match Presentation.mergeSliceInPart viewSide slice part with
                            | Some mergedPart -> mergedPart, true
                            | None -> part, false
                        )

                    // Bytes merged into expanded rows leave the page with their gap when it collapses.
                    let mergedGapIds =
                        parts
                        |> Array.choose (
                            function
                            | Paged.PagedPart.ExpandedRows(gapId, _), true -> Some gapId
                            | _ -> None
                        )

                    if parts |> Array.exists snd then
                        {
                            windowPage with
                                Parts = parts |> Array.map fst
                                PayloadBytes = windowPage.PayloadBytes + bytes
                                ExpandedGaps =
                                    windowPage.ExpandedGaps
                                    |> List.map (fun expanded ->
                                        if mergedGapIds |> Array.contains expanded.GapId then
                                            {
                                                expanded with
                                                    PayloadBytes = expanded.PayloadBytes + bytes
                                            }
                                        else
                                            expanded
                                    )
                        },
                        true
                    else
                        windowPage, false
            )

        {
            page with
                Pages = merged |> Array.map fst
                RequestedPageIndex = merged |> Array.tryFindIndex snd |> Option.defaultValue page.RequestedPageIndex
        }

    /// Puts a replayed page back in place of its placeholder. A page that is loaded again by
    /// the time the answer arrives keeps its rows, with the expansions and slices merged into them.
    let private replacePage (dto: DiffPageDto) (page: GitDiffPageData) =
        match
            page.Pages
            |> Array.tryFindIndex (fun windowPage -> windowPage.PageId = dto.PageId && windowPage.IsEvicted)
        with
        | None -> page
        | Some index -> {
            page with
                Pages = page.Pages |> Array.updateAt index (windowPage dto)
                RequestedPageIndex = index
          }

    /// The library keeps the worker session of a Scanning Open until its continuation is used.
    /// Reopening with the continuation and canceling that operation at once releases the session.
    /// An answer that still carries a handle closes it, and another Scanning answer is abandoned
    /// the same way.
    let rec private abandonOpen
        (deps: GitDependencies)
        (request: OpenTextDiffRequestDto)
        (continuation: string)
        : unit =
        let operationId = deps.newOperationId ()

        deps.textDiff.openTextDiff {
            request with
                OperationId = operationId
                Continuation = Some continuation
        }
        |> Promise.map (fun result ->
            match valueOf result with
            | Ok(ResumableOpenDto.Ready(OpenDiffResultDto.Opened(handle, _, _, _, _))) -> closeHandle deps handle
            | Ok(ResumableOpenDto.Scanning(_, next, _)) -> abandonOpen deps request next
            | _ -> ()
        )
        |> Promise.catch ignore
        |> Promise.start

        deps.cancelOperation { OperationId = operationId }
        |> Promise.catch (fun error -> Error error.Message)
        |> Promise.start

    /// Releases what an Open answer holds when its page was left or replaced.
    let private releaseArrivedOpenCmd
        (deps: GitDependencies)
        (request: OpenTextDiffRequestDto)
        (result: Result<OperationResultDto<ResumableOpenDto>, string>)
        =
        match valueOf result with
        | Ok(ResumableOpenDto.Ready(OpenDiffResultDto.Opened(handle, _, _, _, _))) -> closeHandleCmd deps handle
        | Ok(ResumableOpenDto.Scanning(_, continuation, _)) -> [
            fun _ -> abandonOpen deps request continuation
          ]
        | _ -> Cmd.none

    let private generationOf (msg: GitDiffMsg) =
        match msg with
        | GitDiffMsg.LoadNext generation
        | GitDiffMsg.Index generation
        | GitDiffMsg.Expand(generation, _, _)
        | GitDiffMsg.LoadLineSlice(generation, _, _, _)
        | GitDiffMsg.LoadLineBefore(generation, _, _, _)
        | GitDiffMsg.Replay(generation, _, _)
        | GitDiffMsg.ChooseEncoding(generation, _, _)
        | GitDiffMsg.OpenCompleted(generation, _, _)
        | GitDiffMsg.PageCompleted(generation, _, _)
        | GitDiffMsg.ReplayCompleted(generation, _, _)
        | GitDiffMsg.ExpandCompleted(generation, _, _)
        | GitDiffMsg.LineCompleted(generation, _, _) -> Some generation
        | GitDiffMsg.PageStateObserved _ -> None

    let private withEncoding (side: DiffSideDto) (encoding: string) (page: GitDiffPageData) =
        match side with
        | DiffSideDto.Previous -> {
            page with
                PreviousEncoding = Some encoding
          }
        | DiffSideDto.Current -> {
            page with
                CurrentEncoding = Some encoding
          }

    /// Applies a message to the current page of the same generation.
    let private updatePage (deps: GitDependencies) (msg: GitDiffMsg) (page: GitDiffPageData) =
        // A reopening page takes no requests, since its handle is about to change.
        let handleRequest () =
            page.Handle
            |> Option.filter (fun _ -> not (isSettled page.Status) && not (isReopening page.Status))

        let readLineSlice (side: DiffSideDto) (line: float) (offsetUtf16: float) (maxUtf16: int) =
            let slice: Paged.PagedLineSliceRequest = {
                Side = Presentation.side side
                Line = line
                OffsetUtf16 = offsetUtf16
            }

            match handleRequest () with
            | Some handle when not (page.PendingLineSlices |> List.contains slice) ->
                let page = {
                    page with
                        FailedLineSlices =
                            page.FailedLineSlices
                            |> List.filter (fun failed -> failed.Side <> slice.Side || failed.Line <> slice.Line)
                }

                let request: ReadTextDiffLineRequestDto = {
                    OperationId = deps.newOperationId ()
                    HandleId = handle.Id
                    HandleVersion = handle.Version
                    Side = side
                    Line = Presentation.decimalText line
                    OffsetUtf16 = Presentation.decimalText offsetUtf16
                    MaxUtf16 = maxUtf16
                    Continuation = None
                }

                track request.OperationId {
                    page with
                        PendingLineSlices = slice :: page.PendingLineSlices
                },
                send
                    deps.textDiff.readTextDiffLine
                    request
                    (fun request result -> GitDiffMsg.LineCompleted(page.Generation, request, result))
            | _ -> page, Cmd.none

        match msg with
        | GitDiffMsg.PageStateObserved _ -> page, Cmd.none
        | GitDiffMsg.Index _ when page.Stop.IsSome -> page, Cmd.none
        | GitDiffMsg.Index _ -> { page with Indexing = true }, Cmd.none
        | GitDiffMsg.LoadNext _ ->
            match page.Status, page.NextCursor, page.NextRequest with
            // A diff at its full memory budget refuses every read of a new page, so none is sent.
            // Replays and recorded answers still work.
            | GitDiffPageStatus.Ready, Some _, None when page.Stop = Some GitDiffStop.MemoryBudget -> page, Cmd.none
            | GitDiffPageStatus.Ready, Some cursor, None ->
                // The viewer asks for the next page from the end of the diff, where the pages of
                // the last replay request are out of view.
                readPage
                    deps
                    {
                        page with
                            Status = GitDiffPageStatus.LoadingNext
                            VisiblePages = []
                            KeepRequestedPage = false
                            NextFailed = false
                            NextFailure = None
                    }
                    cursor
                    false
            | _ -> page, Cmd.none
        | GitDiffMsg.Expand(_, gapId, fromStart) ->
            match handleRequest (), pageIndexOfGap gapId page with
            | Some handle, Some index when not (page.ExpandingGaps |> List.contains gapId) ->
                let page = {
                    page with
                        KeepRequestedPage = page.KeepRequestedPage || page.NextRequest.IsSome
                        FailedGaps = page.FailedGaps |> List.filter (fun failed -> failed <> gapId)
                }

                let request: ExpandTextDiffRequestDto = {
                    OperationId = deps.newOperationId ()
                    HandleId = handle.Id
                    HandleVersion = handle.Version
                    GapId = gapId
                    FromStart = fromStart
                    Count = ExpandLineCount
                    Continuation = None
                }

                track request.OperationId {
                    page with
                        ExpandingGaps = gapId :: page.ExpandingGaps
                        RequestedPageIndex = index
                },
                send
                    deps.textDiff.expandTextDiff
                    request
                    (fun request result -> GitDiffMsg.ExpandCompleted(page.Generation, request, result))
            | _ -> page, Cmd.none
        | GitDiffMsg.LoadLineSlice(_, side, line, offsetUtf16) -> readLineSlice side line offsetUtf16 LineSliceUtf16
        | GitDiffMsg.LoadLineBefore(_, side, line, displayedStart) when displayedStart > 0.0 ->
            // The request ends one unit after the displayed start, and the answer goes in front of
            // the displayed text. When the start splits a surrogate pair, the library starts and
            // ends the slice one unit earlier, and the unit of overlap keeps it reaching the
            // displayed text.
            let offsetUtf16 = max 0.0 (displayedStart - float (LineSliceUtf16 - 1))
            readLineSlice side line offsetUtf16 (int (displayedStart - offsetUtf16) + 1)
        | GitDiffMsg.LoadLineBefore _ -> page, Cmd.none
        | GitDiffMsg.Replay(_, pageId, visiblePages) ->
            let isEvicted =
                page.Pages
                |> Array.exists (fun windowPage -> windowPage.PageId = pageId && windowPage.IsEvicted)

            // One replay at a time. Each answer evicts other pages, and replays running side by
            // side would evict each other's pages.
            match handleRequest (), page.Pages |> Array.tryFindIndex (fun windowPage -> windowPage.PageId = pageId) with
            | Some handle, Some index when isEvicted && page.PendingReplay.IsNone ->
                let request: ReplayTextDiffPageRequestDto = {
                    OperationId = deps.newOperationId ()
                    HandleId = handle.Id
                    HandleVersion = handle.Version
                    PageId = pageId
                }

                // A replay asked for from a folded placeholder names no visible part of that page,
                // so the replayed page joins the pages eviction keeps. The replayed page is also
                // the requested page, so a next page that lands before the replay answers keeps
                // the pages on screen.
                track request.OperationId {
                    page with
                        PendingReplay = Some pageId
                        RequestedPageIndex = index
                        VisiblePages = pageId :: visiblePages |> List.distinct
                        KeepRequestedPage = page.KeepRequestedPage || page.NextRequest.IsSome
                        FailedReplays = page.FailedReplays |> List.filter (fun failed -> failed <> pageId)
                },
                send
                    deps.textDiff.replayTextDiffPage
                    request
                    (fun request result -> GitDiffMsg.ReplayCompleted(page.Generation, request, result))
            | _ -> page, Cmd.none
        | GitDiffMsg.ChooseEncoding(_, side, encoding) ->
            match page.Status with
            | GitDiffPageStatus.EncodingChoice(choiceSide, Some token, _) when choiceSide = side ->
                openFresh
                    deps
                    {
                        withEncoding side encoding page with
                            Status = GitDiffPageStatus.Opening
                    }
                    (Some token.Id)
            | _ -> page, Cmd.none
        | GitDiffMsg.OpenCompleted(_, request, result) ->
            let page = finish request.OperationId page

            // An open that fails or asks for an encoding ends a reopen before the new session
            // read any page, and the old pages go with it.
            let settledWith status =
                let pages =
                    match page.Status with
                    | GitDiffPageStatus.Reopening pagesRead -> page.Pages |> Array.truncate pagesRead
                    | _ -> page.Pages

                {
                    page with
                        Pages = pages
                        Status = status
                }

            match refusalOf result, valueOf result with
            | Some refusal, _ -> settledWith (refusedStatus refusal), Cmd.none
            | None, Error status -> settledWith status, Cmd.none
            | None, Ok(ResumableOpenDto.Scanning(progress, continuation, pending)) ->
                let status =
                    if isReopening page.Status then
                        page.Status
                    else
                        GitDiffPageStatus.Scanning

                sendOpen
                    deps
                    {
                        page with
                            Status = status
                            Progress = Some progress
                            Pending = pending
                    }
                    {
                        request with
                            OperationId = deps.newOperationId ()
                            Continuation = Some continuation
                    }
            | None, Ok(ResumableOpenDto.Ready(OpenDiffResultDto.NotDiffable blocker)) ->
                settledWith (blockedStatus blocker), Cmd.none
            | None, Ok(ResumableOpenDto.Ready(OpenDiffResultDto.Opened(handle, previous, current, first, storage))) ->
                applyResumablePage
                    deps
                    {
                        page with
                            Handle = Some handle
                            Storage = Some storage
                            SourceInfos =
                                Some(
                                    {
                                        Previous = previous
                                        Current = current
                                    }
                                    : DiffSourceInfoPairDto
                                )
                    }
                    false
                    first
        | GitDiffMsg.PageCompleted(_, request, result) ->
            let page = finish request.OperationId page

            // A Scanning answer reads on with a continuation, which keeps the origin of this read.
            let background =
                match page.NextRequest with
                | Some running when running.OperationId = request.OperationId -> running.Background
                | _ -> false

            let page =
                match page.NextRequest with
                | Some running when running.OperationId = request.OperationId -> { page with NextRequest = None }
                | _ -> page

            // The rows the page keeps: the pages the new session read while the diff reopens.
            let rowsRead =
                match page.Status with
                | GitDiffPageStatus.Reopening pagesRead -> pagesRead
                | _ -> page.Pages.Length

            if isSettled page.Status then
                page, Cmd.none
            else
                match refusalOf result, valueOf result with
                // A refusal leaves the session open. A reopen ends at the page it read last, which
                // continueReopen does once the stop is recorded.
                | Some refusal, _ when rowsRead > 0 -> stopReading background refusal page, Cmd.none
                | Some refusal, _ ->
                    {
                        page with
                            Pages = [||]
                            Status = refusedStatus refusal
                    },
                    Cmd.none
                // An error that ends the diff, or one while the diff has no rows or reopens,
                // settles the page. Any other error keeps the rows, and the continue button asks again.
                | None, Error status when endsDiff status || isReopening page.Status || page.Pages.Length = 0 ->
                    { page with Status = status }, Cmd.none
                | None, Error status ->
                    {
                        page with
                            Status = GitDiffPageStatus.Ready
                            NextFailed = true
                            NextFailure =
                                match status with
                                | GitDiffPageStatus.Failed message -> Some message
                                | _ -> None
                    },
                    Cmd.none
                | None, Ok resumable -> applyResumablePage deps page background resumable
        | GitDiffMsg.ReplayCompleted(_, request, result) ->
            let page = {
                finish request.OperationId page with
                    PendingReplay = page.PendingReplay |> Option.filter (fun pageId -> pageId <> request.PageId)
            }

            if isSettled page.Status then
                page, Cmd.none
            else
                match valueOf result with
                | Error status when endsDiff status -> { page with Status = status }, Cmd.none
                | Error _ ->
                    {
                        page with
                            FailedReplays = request.PageId :: page.FailedReplays |> List.distinct
                    },
                    Cmd.none
                | Ok dto -> evict (replacePage dto page), Cmd.none
        | GitDiffMsg.ExpandCompleted(_, request, result) ->
            let page = finish request.OperationId page

            let expanded () = {
                page with
                    ExpandingGaps = page.ExpandingGaps |> List.filter (fun gapId -> gapId <> request.GapId)
            }

            if isSettled page.Status then
                expanded (), Cmd.none
            else
                match valueOf result with
                | Error status when endsDiff status -> { expanded () with Status = status }, Cmd.none
                | Error _ ->
                    let expanded = expanded ()

                    {
                        expanded with
                            FailedGaps = request.GapId :: expanded.FailedGaps |> List.distinct
                            FailureCause = refusalOf result
                    },
                    Cmd.none
                | Ok(ResumablePartsDto.Scanning(progress, continuation, _)) ->
                    let next = {
                        request with
                            OperationId = deps.newOperationId ()
                            Continuation = Some continuation
                    }

                    track next.OperationId { page with Progress = Some progress },
                    send
                        deps.textDiff.expandTextDiff
                        next
                        (fun request result -> GitDiffMsg.ExpandCompleted(page.Generation, request, result))
                | Ok(ResumablePartsDto.Ready parts) -> evict (replaceGap request.GapId parts (expanded ())), Cmd.none
        | GitDiffMsg.LineCompleted(_, request, result) ->
            let page = finish request.OperationId page

            let slice: Paged.PagedLineSliceRequest = {
                Side = Presentation.side request.Side
                Line = Presentation.number request.Line
                OffsetUtf16 = Presentation.number request.OffsetUtf16
            }

            let answered () = {
                page with
                    PendingLineSlices = page.PendingLineSlices |> List.filter (fun pending -> pending <> slice)
                    RestartedLineSlices = page.RestartedLineSlices |> List.filter (fun pending -> pending <> slice)
            }

            if isSettled page.Status then
                answered (), Cmd.none
            else
                let droppedRead =
                    match result with
                    | Ok(OperationResultDto.Failed failure) ->
                        failure.Code = ContinuationMismatch
                        && request.Continuation.IsSome
                        && not (page.RestartedLineSlices |> List.contains slice)
                    | _ -> false

                match valueOf result with
                | Error _ when droppedRead ->
                    // The worker keeps eight suspended line reads and dropped this one. The restart
                    // goes back to the start offset of the slice without a continuation, once per slice.
                    let restart = {
                        request with
                            OperationId = deps.newOperationId ()
                            Continuation = None
                    }

                    track restart.OperationId {
                        page with
                            RestartedLineSlices = slice :: page.RestartedLineSlices
                    },
                    send
                        deps.textDiff.readTextDiffLine
                        restart
                        (fun request result -> GitDiffMsg.LineCompleted(page.Generation, request, result))
                | Error status when endsDiff status -> { answered () with Status = status }, Cmd.none
                | Error _ ->
                    let answered = answered ()

                    {
                        answered with
                            FailedLineSlices = slice :: answered.FailedLineSlices
                            FailureCause = refusalOf result
                    },
                    Cmd.none
                | Ok(ResumableLineDto.Scanning(progress, continuation, _)) ->
                    let next = {
                        request with
                            OperationId = deps.newOperationId ()
                            Continuation = Some continuation
                    }

                    track next.OperationId { page with Progress = Some progress },
                    send
                        deps.textDiff.readTextDiffLine
                        next
                        (fun request result -> GitDiffMsg.LineCompleted(page.Generation, request, result))
                | Ok(ResumableLineDto.Ready lineDto) -> evict (mergeLine request.Side lineDto (answered ())), Cmd.none

    let private failureCode (result: Result<OperationResultDto<'T>, string>) =
        match result with
        | Ok(OperationResultDto.Failed failure) -> Some failure.Code
        | _ -> None

    /// The operation id, the handle id, the handle version and the failure code of an answer
    /// to a request that was sent on an open handle.
    let private handleAnswerOf (msg: GitDiffMsg) =
        match msg with
        | GitDiffMsg.PageCompleted(_, request, result) ->
            Some(request.OperationId, request.HandleId, request.HandleVersion, failureCode result)
        | GitDiffMsg.ReplayCompleted(_, request, result) ->
            Some(request.OperationId, request.HandleId, request.HandleVersion, failureCode result)
        | GitDiffMsg.ExpandCompleted(_, request, result) ->
            Some(request.OperationId, request.HandleId, request.HandleVersion, failureCode result)
        | GitDiffMsg.LineCompleted(_, request, result) ->
            Some(request.OperationId, request.HandleId, request.HandleVersion, failureCode result)
        | _ -> None

    /// Where the user was reading when the request behind the answer was sent: the requested
    /// line, else the first line of the page being replayed, of the last page for the read of the
    /// next page, or of the requested page. The index of that page is the target when the page
    /// shows no line.
    let private reopenTargetOf (msg: GitDiffMsg) (page: GitDiffPageData) : GitDiffReopen =
        let shownPage =
            match msg with
            | GitDiffMsg.ReplayCompleted(_, request, _) ->
                page.Pages
                |> Array.tryFindIndex (fun windowPage -> windowPage.PageId = request.PageId)
            | GitDiffMsg.LineCompleted(_, request, _) ->
                pageIndexOfLine request.Side (Presentation.number request.Line) page
            // The viewer asks for the next page at the end of the pages read, and the background
            // indexing leaves the requested page where the user asked for it last.
            | GitDiffMsg.PageCompleted _ when page.Pages.Length > 0 -> Some(page.Pages.Length - 1)
            | _ -> None

        let pageIndex = shownPage |> Option.defaultValue page.RequestedPageIndex

        let targetLine =
            match msg with
            | GitDiffMsg.LineCompleted(_, request, _) -> Some(position request.Side (Presentation.number request.Line))
            | _ -> page.Pages |> Array.tryItem pageIndex |> Option.bind _.Span.First

        {
            Generation = page.Generation
            TargetLine = targetLine
            TargetPageIndex = pageIndex
        }

    /// Opens the diff again with the chosen encodings and no preparation token. The pages
    /// stay on screen while the new session reads forward, and each page it reads replaces the
    /// old page at the same index. Expanded gaps and line slices of the old session are gone.
    /// The old handle is closed so the main process forgets it.
    let private reopen (deps: GitDependencies) (target: GitDiffReopen) (page: GitDiffPageData) =
        let next, openCmd =
            openFresh
                deps
                {
                    page with
                        Handle = None
                        RequestedPageIndex = target.TargetPageIndex
                        NextCursor = None
                        NextRequest = None
                        PendingLineSlices = []
                        RestartedLineSlices = []
                        ExpandingGaps = []
                        PendingReplay = None
                        VisiblePages = []
                        KeepRequestedPage = false
                        IndexingPaused = false
                        // The new session has read nothing yet and decides its storage again.
                        JournalBytes = 0.0
                        Storage = None
                        Stop = None
                        FailureCause = None
                        NextFailed = false
                        NextFailure = None
                        FailedGaps = []
                        FailedLineSlices = []
                        FailedReplays = []
                        Progress = None
                        Pending = None
                        OutputComplete = false
                        Status = GitDiffPageStatus.Reopening 0
                }
                None

        let closeOld =
            match page.Handle with
            | Some handle -> closeHandleCmd deps handle
            | None -> Cmd.none

        next, Cmd.batch [ openCmd; closeOld ]

    /// Reads the next page while the reopened diff has not reached the target. The reopen ends
    /// on the first page read that reaches the target line, or once the diff has no further
    /// page, the page settles or the library asks for an encoding. The requested page is then
    /// the page that reached the target, and the old pages after the last page read go away,
    /// since the next page read from the new session takes their place. Pages too far from the
    /// requested page to share a window with it are evicted, and the viewer scrolls to the
    /// target line, or to the first line of the requested page when the diff ended before it.
    let private continueReopen (deps: GitDependencies) (target: GitDiffReopen) (page: GitDiffPageData) =
        match page.Status with
        | GitDiffPageStatus.Reopening _ when page.Handle.IsNone || page.NextRequest.IsSome ->
            page, Some target, Cmd.none
        | GitDiffPageStatus.Reopening pagesRead ->
            let landing =
                match target.TargetLine with
                | Some line -> page.Pages |> Array.truncate pagesRead |> Array.tryFindIndex (reaches line)
                | None when pagesRead > target.TargetPageIndex -> Some target.TargetPageIndex
                | None -> None

            match landing, page.NextCursor with
            | None, Some cursor when page.Stop.IsNone ->
                let next, cmd = readPage deps page cursor false
                next, Some target, cmd
            | _ ->
                let requested = landing |> Option.defaultValue (max 0 (pagesRead - 1))

                // The old neighbors of the requested page stayed loaded while the reopen read
                // forward. Left in place, they would hold the window away from the requested page.
                let pages =
                    page.Pages
                    |> Array.truncate pagesRead
                    |> Array.mapi (fun index windowPage ->
                        if abs (index - requested) >= MaxLoadedPages && not windowPage.IsEvicted then
                            evictedCopy windowPage
                        else
                            windowPage
                    )

                let scrollLine =
                    (if landing.IsSome then target.TargetLine else None)
                    |> Option.orElse (pages |> Array.tryItem requested |> Option.bind _.Span.First)

                let finished =
                    evict {
                        page with
                            Pages = pages
                            RequestedPageIndex = requested
                            Status = GitDiffPageStatus.Ready
                            ScrollTarget =
                                scrollLine
                                |> Option.map (fun line ->
                                    ({
                                        Side = Presentation.side line.Side
                                        Line = line.Number
                                        Token =
                                            page.ScrollTarget
                                            |> Option.map (fun previous -> previous.Token + 1)
                                            |> Option.defaultValue 1
                                    }
                                    : Paged.PagedScrollTarget)
                                )
                    }

                finished, None, Cmd.none
        | _ -> page, None, Cmd.none

    /// Reads the page after the last one while the viewer asked for the indexing, once no other
    /// read runs. The page joins the window without becoming the requested page, so the pages the
    /// user looks at stay loaded and the new page goes first when the window is full. A diff
    /// whose output is exhausted ends the indexing. Only a ready page reads on, so a failed or
    /// settled diff, a reopen and a user request for the next page all hold it back. The one
    /// pending read stays the only read, and the answer of a closed diff finds no page to continue.
    /// Once the pages read add up to the limit, the read that just ended is the last one. The
    /// indexing ends, and indexingRequest asks for it again when the limit rises.
    let private continueIndexing (deps: GitDependencies) (limitMb: int) (page: GitDiffPageData) =
        match page.Status, page.NextCursor, page.Handle with
        | GitDiffPageStatus.Ready, Some cursor, Some _ when
            page.Indexing
            && not page.NextFailed
            && page.NextRequest.IsNone
            && page.Stop.IsNone
            ->
            if indexingLimitReached limitMb page then
                { page with Indexing = false }, Cmd.none
            else
                readPage deps { page with KeepRequestedPage = true } cursor true
        | GitDiffPageStatus.Ready, None, _ when page.Indexing -> { page with Indexing = false }, Cmd.none
        | _ -> page, Cmd.none

    /// The message that starts the indexing, once the first page is ready and more pages follow.
    /// None while the indexing runs, waits for a retry, is paused, has read up to the limit or
    /// stopped because the library refused a read.
    let indexingRequest (limitMb: int) (page: GitDiffPageData) : GitDiffMsg option =
        match page.Status, page.NextCursor, page.Handle with
        | GitDiffPageStatus.Ready, Some _, Some _ when
            not page.Indexing
            && not page.NextFailed
            && not page.IndexingPaused
            && page.Stop.IsNone
            && not (indexingLimitReached limitMb page)
            ->
            Some(GitDiffMsg.Index page.Generation)
        | _ -> None

    let private choosesWithoutToken (side: DiffSideDto) (status: GitDiffPageStatus) =
        match status with
        | GitDiffPageStatus.EncodingChoice(choiceSide, None, _) -> choiceSide = side
        | _ -> false

    /// Reopens the diff once when its worker session ended or the preparation token of an
    /// encoding choice went stale. Any failure while that reopen runs settles the page. An
    /// encoding chosen for a side that turned out not to be UTF-8 reopens the diff the same way.
    let private updateWithReopen
        (deps: GitDependencies)
        (limitMb: int)
        (msg: GitDiffMsg)
        (reopening: GitDiffReopen option)
        (page: GitDiffPageData)
        =
        match msg, handleAnswerOf msg with
        // The window of the old handle is gone, so its late answers change nothing.
        | _, Some(operationId, handleId, handleVersion, _) when
            page.Handle
            |> Option.forall (fun handle -> handle.Id <> handleId || handle.Version <> handleVersion)
            ->
            finish operationId page, reopening, Cmd.none
        // A background indexing read whose session closed pauses the indexing. Reopening here
        // would evict the next indexer, which would reopen in turn, so no indexer would keep its
        // progress. Any user request still reopens, and the reopen lifts the pause. The read
        // may be the continuation of a Scanning answer, which left the page LoadingNext, so the
        // pause sets that page Ready for the continue button. Any other status, such as an
        // encoding choice that an expansion asked for meanwhile, stays.
        | GitDiffMsg.PageCompleted(_, request, result), _ when
            reopening.IsNone
            && not (isSettled page.Status)
            && page.NextRequest
               |> Option.exists (fun running -> running.OperationId = request.OperationId && running.Background)
            && failureCode result = Some DiffSessionClosed
            ->
            {
                finish request.OperationId page with
                    NextRequest = None
                    Indexing = false
                    IndexingPaused = true
                    Status =
                        match page.Status with
                        | GitDiffPageStatus.LoadingNext -> GitDiffPageStatus.Ready
                        | status -> status
            },
            reopening,
            Cmd.none
        | _, Some(operationId, _, _, Some DiffSessionClosed) when reopening.IsNone && not (isSettled page.Status) ->
            let target = reopenTargetOf msg page
            let next, cmd = reopen deps target (finish operationId page)
            next, Some target, cmd
        // An Open sent with a continuation carries no handle. The library answers it with
        // diff_session_closed when the session it was scanning in closed or was evicted.
        | GitDiffMsg.OpenCompleted(_, request, result), _ when
            reopening.IsNone
            && not (isSettled page.Status)
            && request.Continuation.IsSome
            && failureCode result = Some DiffSessionClosed
            ->
            let target = reopenTargetOf msg page
            let next, cmd = reopen deps target (finish request.OperationId page)
            next, Some target, cmd
        | GitDiffMsg.OpenCompleted(_, request, result), _ when
            reopening.IsNone
            && request.PreparationTokenId.IsSome
            && failureCode result = Some PreparationMismatch
            ->
            let target: GitDiffReopen = {
                Generation = page.Generation
                TargetLine = None
                TargetPageIndex = 0
            }

            let next, cmd = reopen deps target (finish request.OperationId page)
            next, Some target, cmd
        | GitDiffMsg.ChooseEncoding(_, side, encoding), _ when reopening.IsNone && choosesWithoutToken side page.Status ->
            let target = reopenTargetOf msg page
            let next, cmd = reopen deps target (withEncoding side encoding page)
            next, Some target, cmd
        | _ ->
            let next, cmd = updatePage deps msg page

            let next, reopening, cmd =
                match reopening with
                | Some target ->
                    let next, reopening, readCmd = continueReopen deps target next
                    next, reopening, Cmd.batch [ cmd; readCmd ]
                | None -> next, None, cmd

            // A reopen reads on by itself, and the indexing waits for it to land.
            if reopening.IsSome then
                next, reopening, cmd
            else
                let next, indexCmd = continueIndexing deps limitMb next
                next, reopening, Cmd.batch [ cmd; indexCmd ]

    /// Leaving the diff only drops the page here. The workflow's update closes the handle of
    /// any diff page that disappears or is replaced.
    let update (deps: GitDependencies) (msg: GitDiffMsg) (model: GitState) : GitState * Cmd<Msg> =
        match msg, model.DiffPage with
        | GitDiffMsg.PageStateObserved(Some(PageState.GitDiffPage _)), _ -> model, Cmd.none
        | GitDiffMsg.PageStateObserved _, _ -> { model with DiffPage = None }, Cmd.none
        | _, Some page when generationOf msg = Some page.Generation ->
            let reopening =
                model.DiffReopen
                |> Option.filter (fun reopen -> reopen.Generation = page.Generation)

            let next, reopening, cmd =
                updateWithReopen deps model.DiffIndexingLimitMb msg reopening page

            // The answer of a background read that leaves the indexing running shows after a
            // short delay. Every other message shows its page at once, so a page the user asked
            // for never waits.
            let backgroundArrival =
                match msg with
                | GitDiffMsg.PageCompleted(_, request, _) ->
                    page.NextRequest
                    |> Option.exists (fun running -> running.OperationId = request.OperationId && running.Background)
                | _ -> false

            let indexingContinues =
                next.Indexing
                && not next.NextFailed
                && next.NextCursor.IsSome
                && (
                    match next.Status with
                    | GitDiffPageStatus.Ready
                    | GitDiffPageStatus.LoadingNext -> true
                    | _ -> false
                )

            let publish =
                if backgroundArrival && indexingContinues then
                    publishLaterCmd deps next
                else
                    publishNowCmd deps next

            {
                model with
                    DiffPage = Some next
                    DiffReopen = reopening
            },
            Cmd.batch [ publish; cmd ]
        | GitDiffMsg.OpenCompleted(_, request, result), _ -> model, releaseArrivedOpenCmd deps request result
        | _ -> model, Cmd.none

let private conflictSessionConfirmationDialog (overlappingPaths: string[]) : GitSidebarConfirmationDialog =
    let overlapping =
        if overlappingPaths.Length > 0 then
            let joined = String.Join(", ", overlappingPaths |> Array.truncate 5)
            let moreCount = overlappingPaths.Length - min overlappingPaths.Length 5
            let more = if moreCount > 0 then $" and {moreCount} more" else ""
            $" Changed both locally and online: {joined}{more}."
        else
            ""

    {
        Title = "Merge resolution required"
        Message = $"Updating from online will require merge resolution.{overlapping} Continue?"
        ConfirmLabel = "Open Merge Resolution"
        CancelLabel = "Cancel"
    }

let private localChangesOverwriteMessage (affectedPaths: string[]) =
    if affectedPaths.Length > 0 then
        let joined = String.Join(", ", affectedPaths)
        $"Updating from online would change files you also changed locally: {joined}. Save or discard those changes, then try again."
    else
        "Updating from online would change files you also changed locally. Save or discard your local changes, then try again."

let private observedTargetOf (failure: OperationFailureDto) =
    failure.RevisionEvidence
    |> Array.tryFind (fun evidence -> evidence.Label = "observed_target")
    |> Option.map _.Revision

let private hasRevisionEvidence label (failure: OperationFailureDto) =
    failure.RevisionEvidence
    |> Array.exists (fun evidence -> evidence.Label = label)

let private isConflictPartial (failure: OperationFailureDto) =
    failure.Category = FailureCategoryDto.Conflict
    || failure.Code = VersionControlCodes.ConflictsDetected
    || failure.Code = VersionControlCodes.ConflictSessionActive

/// The stale message for a pinned acceptance whose target moved. The write is never
/// replayed, so the text names the online copy instead of the workspace token.
let private movedTargetMessage =
    "The online copy changed since the preview. Review the current changes and try again."

/// Routes a structured failure. Categories and codes decide, never the message.
let private routeFailure (targetPath: string option) (failure: OperationFailureDto) : RoutedFailure =
    if recoveryCode failure = Some VersionControlCodes.Recovery.RemoveIndexLock then
        RoutedFailure.Recovery(
            GitPendingRecovery.ClearStaleLock(failure.RecoveryAction |> Option.bind _.Instructions),
            failureMessage failure
        )
    elif isCanceled failure then
        match recoveryCode failure with
        | Some code when code = VersionControlCodes.Recovery.RestoreWorkspace ->
            RoutedFailure.Recovery(
                GitPendingRecovery.RestoreInterruptedPaths {
                    AffectedPaths = failure.AffectedPaths
                    Instructions = failure.RecoveryAction |> Option.bind _.Instructions
                },
                failureMessage failure
            )
        | Some code when code = VersionControlCodes.Recovery.RemoveCloneTarget ->
            RoutedFailure.Recovery(
                GitPendingRecovery.ClearCloneTarget(
                    targetPath |> Option.defaultValue "",
                    failure.RecoveryAction |> Option.bind _.Instructions
                ),
                failureMessage failure
            )
        | Some code when code = VersionControlCodes.Recovery.RefreshWorkspace ->
            RoutedFailure.RefreshAfterCancel(failureMessage failure)
        | Some code when
            code = VersionControlCodes.Recovery.InspectWorkspace
            || code = VersionControlCodes.Recovery.AbortMerge
            ->
            RoutedFailure.RefreshThenReport(failureMessage failure)
        | _ -> RoutedFailure.Cancelled(failureMessage failure)
    elif failure.Code = VersionControlCodes.UpdateWouldOverwriteLocalChanges then
        RoutedFailure.Error(localChangesOverwriteMessage failure.AffectedPaths)
    else
        match observedTargetOf failure with
        | Some target when failure.Code = VersionControlCodes.UpdateWouldCreateConflictSession ->
            RoutedFailure.UpdateAcceptanceRequired(conflictSessionConfirmationDialog failure.AffectedPaths, target)
        | _ when needsDependencyInstall failure -> RoutedFailure.DependencyInstall(failureMessage failure)
        | _ when
            failure.Code = VersionControlCodes.PreconditionFailed
            && failure.Category = FailureCategoryDto.Concurrency
            && not failure.StateChanged
            ->
            let targetMoved = hasRevisionEvidence "expected_target" failure

            let message =
                if targetMoved then
                    movedTargetMessage
                else
                    failureMessage failure

            RoutedFailure.StaleWorkspace(message, targetMoved)
        | _ when
            failure.Code = VersionControlCodes.ConflictsDetected
            || failure.Code = VersionControlCodes.ConflictSessionActive
            ->
            RoutedFailure.ConflictSession failure
        | _ when failure.StateChanged -> RoutedFailure.RefreshThenReport(failureMessage failure)
        | _ -> RoutedFailure.Error(failureMessage failure)

let private routedFailureMessage (originalFailure: OperationFailureDto) (routed: RoutedFailure) =
    match routed with
    | RoutedFailure.Cancelled message
    | RoutedFailure.DependencyInstall message
    | RoutedFailure.Recovery(_, message)
    | RoutedFailure.RefreshAfterCancel message
    | RoutedFailure.RefreshThenReport message
    | RoutedFailure.StaleWorkspace(message, _)
    | RoutedFailure.Error message -> message
    | RoutedFailure.ConflictSession failure -> failureMessage failure
    | RoutedFailure.UpdateAcceptanceRequired _ -> failureMessage originalFailure

/// A dependency failure names no component. The dependency report does: the first
/// component that is missing or incompatible decides whether an installation can be
/// offered (only the provider's configuration component is installable).
let private resolveDependencyComponentAsync (deps: GitDependencies) (message: string) = promise {
    let! report = toResult (deps.checkDependencies (request deps))

    let missing =
        match report with
        | Ok(outcome, _) ->
            outcome.Value
            |> Array.tryFind (fun status -> not status.Installed || not status.Compatible)
        | Error _ -> None

    return
        match missing with
        | Some status ->
            let remediation =
                status.Remediation
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
                |> Option.map (fun text -> $" {text}")
                |> Option.defaultValue ""

            Ok(RequiresDependencyInstall(status.Component, $"{message}{remediation} Install or configure it now?"))
        | None -> Error message
}

/// After an update or a conflict failure: opens the first conflicted item, asks to
/// finalize an emptied conflict session, or reports the refreshed state. The pending
/// warning is the caller's (a primary save keeps its saved-locally notice).
let private completeAfterUpdateAsync
    (deps: GitDependencies)
    (partial: OperationFailureDto option)
    (pendingWarning: string option)
    =
    promise {
        let! refreshResult = refreshAllAsync deps

        match refreshResult.Status, refreshErrorMessage refreshResult with
        | Error failure, _ -> return Error(failureMessage failure)
        | Ok _, Some message -> return Error message
        | Ok latestStatus, None ->
            let isConflictOutcome = partial |> Option.exists isConflictPartial

            let partialToKeep = if isConflictOutcome then None else partial

            match latestStatus.ActiveConflictSession with
            | Some conflict when conflict.Items.Length > 0 ->
                let firstConflictPath = conflict.Items.[0].Path
                let! pageResult = deps.loadConflictPage conflict latestStatus.WorkspaceVersion firstConflictPath

                return
                    pageResult
                    |> Result.map (fun page ->
                        Completed(
                            UnitSuccess {
                                Refresh = refreshResult
                                PageChange = GitPageChange.Set page
                                SelectedChangePath = Some(Some firstConflictPath)
                                Warning = pendingWarning
                                Partial = partialToKeep
                                Published = None
                                ObjectsMissing = None
                            }
                        )
                    )
            | Some _ ->
                return
                    Ok(
                        CompletedWithPendingRemoteConfirmation(
                            UnitSuccess {
                                unitSuccess refreshResult with
                                    Warning = pendingWarning
                                    Partial = partialToKeep
                            },
                            {
                                Title = "All conflicts resolved"
                                Message =
                                    "Every conflicted file has been resolved. Finalize the merge to keep the result, or abandon it."
                                ConfirmLabel = "Finalize merge"
                                CancelLabel = "Abandon merge"
                            },
                            GitPendingRemoteAction.FinalizeMerge
                        )
                    )
            | None when isConflictOutcome ->
                return
                    Ok(
                        CompletedWithPendingRemoteFailure(
                            UnitSuccess(unitSuccess refreshResult),
                            partial
                            |> Option.map failureMessage
                            |> Option.defaultValue "The update reported conflicts."
                        )
                    )
            | None ->
                return
                    Ok(
                        Completed(
                            UnitSuccess {
                                unitSuccess refreshResult with
                                    Warning = partial |> Option.map failureMessage
                                    Partial = partial
                            }
                        )
                    )
    }

let private routedToOutcome (deps: GitDependencies) (routed: RoutedFailure) = promise {
    match routed with
    | RoutedFailure.Cancelled message -> return Ok(OperationCancelled message)
    | RoutedFailure.DependencyInstall message -> return! resolveDependencyComponentAsync deps message
    | RoutedFailure.Recovery(recovery, message) -> return Ok(RequiresRecovery(recovery, message))
    | RoutedFailure.RefreshAfterCancel message ->
        let! refreshResult = refreshAllAsync deps

        match refreshResult.Status with
        | Ok _ ->
            return
                Ok(
                    Completed(
                        UnitSuccess {
                            unitSuccess refreshResult with
                                Warning = Some message
                        }
                    )
                )
        | Error refreshFailure -> return Error(failureMessage refreshFailure)
    | RoutedFailure.RefreshThenReport message ->
        let! refreshResult = refreshAllAsync deps

        match refreshResult.Status with
        | Ok _ -> return Ok(CompletedWithPendingRemoteFailure(UnitSuccess(unitSuccess refreshResult), message))
        | Error refreshFailure ->
            let refreshMessage = failureMessage refreshFailure

            let combined =
                $"{message} Refreshing the workspace afterwards failed: {refreshMessage}"

            return Error combined
    | RoutedFailure.StaleWorkspace(message, targetMoved) -> return Ok(StaleWorkspaceVersion(message, targetMoved))
    | RoutedFailure.ConflictSession failure -> return! completeAfterUpdateAsync deps (Some failure) None
    | RoutedFailure.UpdateAcceptanceRequired(_, _) ->
        return Error "The synchronization needs a decision that this write cannot offer."
    | RoutedFailure.Error message -> return Error message
}

let private refreshAfterSuccessWithOutcome
    (deps: GitDependencies)
    (partial: OperationFailureDto option)
    (pageChange: GitPageChange)
    (selectedChangePathOverride: string option option)
    (warningMessage: string option)
    (published: bool option)
    =
    promise {
        let! refreshResult = refreshAllAsync deps

        return
            match refreshResult.Status, refreshErrorMessage refreshResult with
            | Error failure, _ -> Error(failureMessage failure)
            | Ok _, Some message -> Error message
            | Ok _, None ->
                Ok(
                    Completed(
                        UnitSuccess {
                            Refresh = refreshResult
                            PageChange = pageChange
                            SelectedChangePath = selectedChangePathOverride
                            Warning = warningMessage
                            Partial = partial
                            Published = published
                            ObjectsMissing = None
                        }
                    )
                )
    }

let private refreshAfterSuccess
    (deps: GitDependencies)
    (partial: OperationFailureDto option)
    (pageChange: GitPageChange)
    (selectedChangePathOverride: string option option)
    (warningMessage: string option)
    =
    let combinedWarningMessage =
        joinMessages warningMessage (partial |> Option.map failureMessage)

    refreshAfterSuccessWithOutcome deps partial pageChange selectedChangePathOverride combinedWarningMessage None

let private completeAfterSynchronizeAsync
    (deps: GitDependencies)
    (partial: OperationFailureDto option)
    (pendingWarning: string option)
    (published: bool option)
    =
    promise {
        if partial |> Option.exists isConflictPartial then
            return! completeAfterUpdateAsync deps partial pendingWarning
        else
            let partialMessage = partial |> Option.map failureMessage

            let finalWarning =
                match recoveryOfPartial partial with
                | None -> joinMessages pendingWarning partialMessage
                | Some _ -> pendingWarning |> Option.orElse partialMessage

            return! refreshAfterSuccessWithOutcome deps partial GitPageChange.NoChange None finalWarning published
    }

let private acceptanceConfirmation
    (deps: GitDependencies)
    (dialog: GitSidebarConfirmationDialog)
    (action: GitPendingRemoteAction)
    (pendingWarning: string option)
    =
    promise {
        let! refreshResult = refreshAllAsync deps

        match refreshResult.Status, refreshErrorMessage refreshResult with
        | Error failure, _ -> return Error(failureMessage failure)
        | Ok _, Some message -> return Error message
        | Ok _, None ->
            return
                Ok(
                    CompletedWithPendingRemoteConfirmation(
                        UnitSuccess {
                            unitSuccess refreshResult with
                                Warning = pendingWarning
                        },
                        dialog,
                        action
                    )
                )
    }

/// Runs one library call and hands the outcome and its partial failure on. Failures are
/// routed structurally.
let private runTrackedWriteAsync
    (deps: GitDependencies)
    (call: unit -> JS.Promise<Result<OperationResultDto<'T>, string>>)
    (onSuccess: OperationOutcomeDto<'T> -> OperationFailureDto option -> JS.Promise<Result<WriteAttemptOutcome, string>>)
    : JS.Promise<Result<WriteAttemptOutcome, string>> =
    promise {
        let! result = toResult (call ())

        match result with
        | Ok(outcome, partial) -> return! onSuccess outcome partial
        | Error failure -> return! routedToOutcome deps (routeFailure None failure)
    }

let private simpleWriteAsync
    (deps: GitDependencies)
    (call: unit -> JS.Promise<Result<OperationResultDto<'T>, string>>)
    =
    runTrackedWriteAsync deps call (fun _ partial -> refreshAfterSuccess deps partial GitPageChange.NoChange None None)

let private requireWorkspaceVersion (state: GitState) =
    match state.WorkspaceVersion with
    | Some version -> Ok version
    | None -> Error "The workspace state is not loaded yet. Refresh and try again."

let private requireSynchronization (state: GitState) =
    match state.Services with
    | Some services when not services.Synchronization ->
        Error "This workspace provider does not support online synchronization."
    | _ -> Ok()

let private synchronizeRequest
    (deps: GitDependencies)
    (version: string)
    (acceptance: GitUpdateAcceptance)
    (publish: bool)
    : SynchronizeRequestDto =
    match acceptance with
    | GitUpdateAcceptance.RequirePreview -> {
        OperationId = deps.newOperationId ()
        ExpectedWorkspaceVersion = version
        ExpectedTargetRevision = None
        AcceptUpdateRisks = false
        PublishLocalRevisions = publish
      }
    | GitUpdateAcceptance.Accepted(target, acceptedVersion) -> {
        OperationId = deps.newOperationId ()
        ExpectedWorkspaceVersion = acceptedVersion
        ExpectedTargetRevision = Some target
        AcceptUpdateRisks = true
        PublishLocalRevisions = publish
      }

let private runCloneAttemptAsync (deps: GitDependencies) (cloneRequest: CloneWorkspaceRequestDto) = promise {
    let! result =
        toResult (
            deps.cloneWorkspace {
                cloneRequest with
                    OperationId = deps.newOperationId ()
            }
        )

    match result with
    | Ok(outcome, Some failure) when recoveryCode failure = Some VersionControlCodes.Recovery.RetryMaterialization ->
        return
            Error(
                $"The ARC was cloned to '{outcome.Value}', but its large files could not be downloaded. Open the folder and use Download LFS file to get them."
            )
    | Ok(outcome, Some failure) when recoveryCode failure = Some VersionControlCodes.Recovery.RemoveIndexLock ->
        return
            Error(
                $"The ARC was cloned to '{outcome.Value}', but a Git lock (index.lock) blocked the download of its large files. Open the folder, remove the lock once no other Git program uses it, and use Download LFS file to get the files."
            )
    | Ok(outcome, _) -> return Ok(Completed(CloneSuccess outcome.Value))
    | Error failure ->
        let routed = routeFailure (Some cloneRequest.TargetPath) failure

        let routed =
            match routed with
            | RoutedFailure.RefreshThenReport message when not (isCanceled failure) -> RoutedFailure.Error message
            | _ -> routed

        return! routedToOutcome deps routed
}

/// After an update: opens the first conflicted item, asks to finalize an emptied
/// conflict session, or reports the refreshed state. A conflict outcome without a
/// session stops here with its failure so nothing publishes on top of it.
let private runPullAttemptAsync (deps: GitDependencies) (state: GitState) (acceptance: GitUpdateAcceptance) = promise {
    match requireSynchronization state, requireWorkspaceVersion state with
    | Error message, _
    | _, Error message -> return Error message
    | Ok(), Ok version ->
        let! result = toResult (deps.synchronize (synchronizeRequest deps version acceptance false))

        match result with
        | Ok(outcome, partial) ->
            return!
                completeAfterSynchronizeAsync
                    deps
                    partial
                    None
                    (Some(outcome.Publication = PublicationStateDto.Published))
        | Error failure ->
            match routeFailure None failure with
            | RoutedFailure.UpdateAcceptanceRequired(dialog, target) ->
                return!
                    acceptanceConfirmation
                        deps
                        dialog
                        (GitPendingRemoteAction.UpdateFromOnline(GitUpdateAcceptance.Accepted(target, version)))
                        None
            | routed -> return! routedToOutcome deps routed
}

let private runCommitAttemptAsync (deps: GitDependencies) (state: GitState) (prepared: PreparedCommitOperation) = promise {
    if String.IsNullOrWhiteSpace prepared.NormalizedMessage then
        return Error "Commit message must not be empty."
    elif prepared.PathsToCommit.Length = 0 then
        return Error "No changes available to commit."
    else
        match requireWorkspaceVersion state with
        | Error message -> return Error message
        | Ok version ->
            // One request with the exact selected paths. The library stages and commits
            // them as one revision, so nothing is staged or unstaged separately here.
            return!
                simpleWriteAsync
                    deps
                    (fun () ->
                        deps.createRevision {
                            OperationId = deps.newOperationId ()
                            Message = prepared.NormalizedMessage
                            Paths = prepared.PathsToCommit
                            ExpectedWorkspaceVersion = version
                        }
                    )
}

let private runDiscardAttemptAsync (deps: GitDependencies) (state: GitState) (paths: string[]) = promise {
    let pathsToDiscard = distinctPaths paths

    if pathsToDiscard.Length = 0 then
        return Error "No selected changes to discard."
    else
        match requireWorkspaceVersion state with
        | Error message -> return Error message
        | Ok version ->
            return!
                runTrackedWriteAsync
                    deps
                    (fun () ->
                        deps.restorePaths {
                            OperationId = deps.newOperationId ()
                            Paths = pathsToDiscard
                            ExpectedWorkspaceVersion = version
                        }
                    )
                    (fun _ partial -> refreshAfterSuccess deps partial GitPageChange.Clear (Some None) None)
}

let private pendingPrimarySaveWarning =
    "Changes were saved locally. Online sync is still pending."

let private projectNameRefusedMessage =
    "A DataHUB repository with this name already exists. Enter a different name."

let private missingPublishAccountNotice =
    "Saved locally. Sign in to a DataHUB account to publish this ARC."

/// The saved-locally outcome after a remote step failed. The snapshot is refreshed
/// first, because the failed step may have changed the workspace.
let private pendingPrimarySaveRemoteFailureAsync (deps: GitDependencies) (message: string) = promise {
    let! refreshResult = refreshAllAsync deps

    match refreshResult.Status with
    | Ok _ ->
        return
            Ok(
                CompletedWithPendingRemoteFailure(
                    UnitSuccess {
                        unitSuccess refreshResult with
                            Warning = Some pendingPrimarySaveWarning
                    },
                    message
                )
            )
    | Error failure -> return Error(failureMessage failure)
}

[<RequireQualifiedAccess>]
type private PublishFailure =
    | Routed of RoutedFailure
    | AcceptanceRequired of dialog: GitSidebarConfirmationDialog * observedTarget: string * workspaceVersion: string
    | ProjectNameRefused of string
    /// The signed-in user can create the repository in a group, so the dialog asks for the owner first.
    | OwnerChoiceRequired of GitPublishOwner[]
    /// DataHUB refused the owner with this namespace id (None is the personal namespace).
    | OwnerNotAllowed of namespaceId: int option * message: string
    | ProvisioningIncomplete of GitProvisionedRemote * string
    | NoUsableAccount

/// Synchronizes the workspace. Without a publication target the repository is created on the
/// DataHub of the signed-in account (or the one created earlier is reused), the workspace is
/// bound to it and the synchronize runs once more. An unreachable target is an outage and
/// never triggers provisioning.
let private runPublishAsync
    (deps: GitDependencies)
    (model: GitState)
    (version: string)
    (acceptance: GitUpdateAcceptance)
    : JS.Promise<Result<OperationOutcomeDto<SynchronizationStateDto> * OperationFailureDto option, PublishFailure>> =
    promise {
        let publishOnce version acceptance =
            toResult (deps.synchronize (synchronizeRequest deps version acceptance true))

        let projectName =
            model.CurrentArcPath |> Option.bind tryGetPathLeaf |> Option.defaultValue "ARC"

        let bindAndPublish (acceptance: GitUpdateAcceptance) (provisioned: GitProvisionedRemote) = promise {
            let! bound =
                if provisioned.IsBound then
                    promise { return Ok() }
                else
                    promise {
                        let! result =
                            toResult (
                                deps.bindWorkspace {
                                    OperationId = deps.newOperationId ()
                                    ProviderLocation = provisioned.RemoteUrl
                                    DisplayName = Some provisioned.ProjectName
                                }
                            )

                        return result |> Result.map ignore
                    }

            match bound with
            | Error failure -> return Error(PublishFailure.ProvisioningIncomplete(provisioned, failureMessage failure))
            | Ok() ->
                let boundRemote = { provisioned with IsBound = true }
                // The session was reopened by the main process, so the token is read again.
                let! statusResult = toResult (deps.getStatus (request deps))

                match statusResult with
                | Error failure ->
                    return Error(PublishFailure.ProvisioningIncomplete(boundRemote, failureMessage failure))
                | Ok(statusOutcome, _) ->
                    let! published = publishOnce statusOutcome.Value.WorkspaceVersion acceptance

                    match published with
                    | Ok(outcome, partial) -> return Ok(outcome, partial)
                    | Error failure when isCanceled failure ->
                        return Error(PublishFailure.ProvisioningIncomplete(boundRemote, failureMessage failure))
                    | Error failure ->
                        match routeFailure None failure with
                        | RoutedFailure.UpdateAcceptanceRequired(dialog, target) ->
                            return
                                Error(
                                    PublishFailure.AcceptanceRequired(
                                        dialog,
                                        target,
                                        statusOutcome.Value.WorkspaceVersion
                                    )
                                )
                        | routed -> return Error(PublishFailure.Routed routed)
        }

        let provisionAndPublish acceptance = promise {
            match model.ProvisionedRemote with
            | Some provisioned -> return! bindAndPublish acceptance provisioned
            | None ->
                let createFor (owner: GitPublishOwner option) = promise {
                    let! created =
                        deps.createRemoteProject {
                            Name = projectName
                            NamespaceId = owner |> Option.bind _.NamespaceId
                        }

                    match created with
                    | Error(CreateProjectError.NameRefused _) ->
                        return Error(PublishFailure.ProjectNameRefused projectNameRefusedMessage)
                    | Error(CreateProjectError.NotAllowed details) ->
                        // GitLab's 403 text for the personal namespace can name a project limit, which the
                        // user can act on. A group refusal carries no text worth showing.
                        let message =
                            match owner, details with
                            | Some { NamespaceId = Some _; Label = label }, _ ->
                                $"DataHUB did not allow a new repository in {label}."
                            | _, Some text ->
                                $"DataHUB did not allow a new repository in your personal namespace. {text}"
                            | _, None -> "DataHUB did not allow a new repository in your personal namespace."

                        return Error(PublishFailure.OwnerNotAllowed(owner |> Option.bind _.NamespaceId, message))
                    | Error(CreateProjectError.Failed error) ->
                        return Error(PublishFailure.Routed(RoutedFailure.Error error.GitLabErrorToString))
                    | Ok project ->
                        return!
                            bindAndPublish acceptance {
                                RemoteUrl = project.http_url_to_repo
                                ProjectName = projectName
                                IsBound = false
                            }
                }

                match model.PublishOwnerChoice with
                | Some owner -> return! createFor (Some owner)
                | None ->
                    let! loaded = deps.loadPublishOwners ()

                    match loaded with
                    | Error error ->
                        return
                            Error(
                                PublishFailure.Routed(
                                    RoutedFailure.Error
                                        $"Could not load your DataHUB groups, so nothing was uploaded. Try the upload again. ({error.GitLabErrorToString})"
                                )
                            )
                    | Ok dto ->
                        let owners = publishOwnersOf dto

                        if hasEnabledGroup owners then
                            return Error(PublishFailure.OwnerChoiceRequired owners)
                        else
                            return! createFor None
        }

        match model.ProvisionedRemote with
        | Some _ when not (deps.hasUsableAccount ()) -> return Error PublishFailure.NoUsableAccount
        | Some _ -> return! provisionAndPublish acceptance
        | None ->
            let! first = publishOnce version acceptance

            match first with
            | Ok(_, Some partial) when needsPublishTarget partial && not (deps.hasUsableAccount ()) ->
                return Error PublishFailure.NoUsableAccount
            | Ok(_, Some partial) when needsPublishTarget partial -> return! provisionAndPublish acceptance
            | Ok(outcome, partial) -> return Ok(outcome, partial)
            | Error failure when needsPublishTarget failure && not (deps.hasUsableAccount ()) ->
                return Error PublishFailure.NoUsableAccount
            | Error failure when needsPublishTarget failure -> return! provisionAndPublish acceptance
            | Error failure ->
                match routeFailure None failure with
                | RoutedFailure.UpdateAcceptanceRequired(dialog, target) ->
                    return Error(PublishFailure.AcceptanceRequired(dialog, target, version))
                | routed -> return Error(PublishFailure.Routed routed)
    }

/// The stored owner list with the refused owner disabled. None when the list is missing or has
/// no enabled owner left, so the dialog has nothing to offer.
let private ownersAfterRefusal (namespaceId: int option) (publishOwners: GitPublishOwner[] option) =
    publishOwners
    |> Option.map (
        Array.map (fun owner ->
            if owner.NamespaceId = namespaceId then
                {
                    owner with
                        DisabledReason = Some "DataHUB refused a new repository here."
                }
            else
                owner
        )
    )
    |> Option.filter (Array.exists (fun owner -> owner.DisabledReason.IsNone))

/// publishOwners is the owner list the model stores. A refused owner reopens the dialog when
/// an enabled owner is left after the refused one is marked disabled (the personal namespace
/// counts). Otherwise the refusal is an error like any other.
let private publishFailureToOutcome
    (onRouted: RoutedFailure -> JS.Promise<Result<WriteAttemptOutcome, string>>)
    (publishOwners: GitPublishOwner[] option)
    (failure: PublishFailure)
    =
    promise {
        match failure with
        | PublishFailure.Routed routed -> return! onRouted routed
        | PublishFailure.NoUsableAccount -> return Error missingPublishAccountNotice
        | PublishFailure.AcceptanceRequired _ ->
            return Error "The synchronization needs a decision that this write cannot offer."
        | PublishFailure.ProjectNameRefused message -> return Ok(RequiresRemoteProjectRename(message, None, false))
        | PublishFailure.OwnerChoiceRequired owners ->
            return
                Ok(
                    RequiresRemoteProjectRename(
                        "Choose where to create the DataHUB repository for this ARC.",
                        Some owners,
                        false
                    )
                )
        | PublishFailure.OwnerNotAllowed(namespaceId, message) ->
            match ownersAfterRefusal namespaceId publishOwners with
            | Some owners ->
                return Ok(RequiresRemoteProjectRename($"{message} Choose a different owner.", Some owners, true))
            | None -> return! onRouted (RoutedFailure.Error message)
        | PublishFailure.ProvisioningIncomplete(provisioned, message) ->
            return Ok(ProvisioningIncomplete(provisioned, message))
    }

let private runPushAttemptAsync (deps: GitDependencies) (state: GitState) (acceptance: GitUpdateAcceptance) = promise {
    match requireSynchronization state, requireWorkspaceVersion state with
    | Error message, _
    | _, Error message -> return Error message
    | Ok(), Ok version ->
        let! result = runPublishAsync deps state version acceptance

        match result with
        | Ok(outcome, partial) ->
            return!
                completeAfterSynchronizeAsync
                    deps
                    partial
                    None
                    (Some(outcome.Publication = PublicationStateDto.Published))
        | Error(PublishFailure.AcceptanceRequired(dialog, target, workspaceVersion)) ->
            return!
                acceptanceConfirmation
                    deps
                    dialog
                    (GitPendingRemoteAction.PublishAfterUpdate(GitUpdateAcceptance.Accepted(target, workspaceVersion)))
                    None
        | Error failure -> return! publishFailureToOutcome (routedToOutcome deps) state.PublishOwners failure
}

let private runFetchAttemptAsync (deps: GitDependencies) (state: GitState) = promise {
    match requireSynchronization state with
    | Error message -> return Error message
    | Ok() -> return! simpleWriteAsync deps (fun () -> deps.refreshSynchronization (request deps))
}

/// Primary save commits the exact selection, then synchronizes the workspace. A partial
/// commit that needs reconciliation stops here with its recovery. A no-op commit still
/// continues with synchronization because the workspace may be ahead of the target.
let private runPrimarySaveAttemptAsync
    (deps: GitDependencies)
    (state: GitState)
    (prepared: PreparedCommitOperation)
    (reportPhase: GitBusyOperation -> unit)
    =
    promise {
        let! commitAttempt = runCommitAttemptAsync deps state prepared

        match commitAttempt with
        | Error message -> return Error message
        | Ok(Completed(UnitSuccess success)) when success.Partial.IsSome ->
            // The revision exists but the library asks for reconciliation first.
            let partial = success.Partial.Value

            return
                Ok(
                    CompletedWithPendingRemoteFailure(
                        UnitSuccess {
                            success with
                                Warning =
                                    if recoveryCode partial = Some VersionControlCodes.Recovery.ReconcileIndex then
                                        None
                                    else
                                        Some pendingPrimarySaveWarning
                        },
                        failureMessage partial
                    )
                )
        | Ok(Completed(UnitSuccess success)) when
            success.Partial.IsNone
            && (requireSynchronization (applyRefreshResult success.Refresh state)).IsError
            ->
            // The revision exists. Without an online service the local save is the whole outcome.
            return
                Ok(
                    Completed(
                        UnitSuccess {
                            success with
                                Warning =
                                    Some
                                        "Changes were saved locally. This workspace provider does not support online synchronization."
                        }
                    )
                )
        | Ok(Completed(UnitSuccess success)) ->
            let refreshedState = applyRefreshResult success.Refresh state

            let runPushAfterLocalCommit (version: string) = promise {
                reportPhase GitBusyOperation.PushingToRemote
                let! pushResult = runPublishAsync deps refreshedState version GitUpdateAcceptance.RequirePreview

                let followsRefresh =
                    match pushResult with
                    | Ok _
                    | Error(PublishFailure.AcceptanceRequired _) -> true
                    | Error(PublishFailure.Routed(RoutedFailure.Recovery _)) -> false
                    | Error(PublishFailure.Routed _) -> true
                    | Error(PublishFailure.ProjectNameRefused _)
                    | Error(PublishFailure.OwnerChoiceRequired _)
                    | Error(PublishFailure.ProvisioningIncomplete _)
                    | Error PublishFailure.NoUsableAccount -> false
                    // The dialog reopens when an enabled owner is left after the refused one is disabled.
                    // Otherwise the refusal ends in the refresh of the saved-locally outcome.
                    | Error(PublishFailure.OwnerNotAllowed(namespaceId, _)) ->
                        (ownersAfterRefusal namespaceId refreshedState.PublishOwners).IsNone

                if followsRefresh then
                    reportPhase GitBusyOperation.Refreshing

                match pushResult with
                | Ok(outcome, partial) ->
                    return!
                        completeAfterSynchronizeAsync
                            deps
                            partial
                            (if partial.IsSome && outcome.Publication <> PublicationStateDto.Published then
                                 Some pendingPrimarySaveWarning
                             else
                                 None)
                            (Some(outcome.Publication = PublicationStateDto.Published))
                | Error(PublishFailure.AcceptanceRequired(dialog, target, workspaceVersion)) ->
                    let! confirmationRefreshResult = refreshAllAsync deps

                    let confirmationRefreshSnapshot =
                        match confirmationRefreshResult.Status, refreshErrorMessage confirmationRefreshResult with
                        | Ok _, None -> confirmationRefreshResult
                        | _ -> success.Refresh

                    return
                        Ok(
                            CompletedWithPendingRemoteConfirmation(
                                UnitSuccess {
                                    success with
                                        Refresh = confirmationRefreshSnapshot
                                        Warning = Some pendingPrimarySaveWarning
                                },
                                dialog,
                                GitPendingRemoteAction.PublishAfterUpdate(
                                    GitUpdateAcceptance.Accepted(target, workspaceVersion)
                                )
                            )
                        )
                | Error PublishFailure.NoUsableAccount ->
                    let! refreshResult = refreshAllAsync deps

                    match refreshResult.Status with
                    | Ok _ ->
                        return
                            Ok(
                                Completed(
                                    UnitSuccess {
                                        unitSuccess refreshResult with
                                            Warning = Some missingPublishAccountNotice
                                    }
                                )
                            )
                    | Error failure -> return Error(failureMessage failure)
                | Error failure ->
                    return!
                        publishFailureToOutcome
                            (fun routed -> promise {
                                match routed with
                                | RoutedFailure.UpdateAcceptanceRequired _ ->
                                    // The wrapper turns every decision into PublishFailure.AcceptanceRequired.
                                    return!
                                        pendingPrimarySaveRemoteFailureAsync
                                            deps
                                            "The synchronization needs a decision that this write cannot offer."
                                | RoutedFailure.Recovery(recovery, message) ->
                                    return Ok(RequiresRecovery(recovery, message))
                                | RoutedFailure.ConflictSession failure ->
                                    return!
                                        completeAfterUpdateAsync deps (Some failure) (Some pendingPrimarySaveWarning)
                                | RoutedFailure.DependencyInstall message
                                | RoutedFailure.StaleWorkspace(message, _)
                                | RoutedFailure.Error message
                                | RoutedFailure.RefreshThenReport message ->
                                    // The local commit already succeeded, so the saved-locally outcome stays.
                                    return! pendingPrimarySaveRemoteFailureAsync deps message
                                | RoutedFailure.Cancelled _
                                | RoutedFailure.RefreshAfterCancel _ ->
                                    let! refreshResult = refreshAllAsync deps

                                    match refreshResult.Status with
                                    | Ok _ ->
                                        return
                                            Ok(
                                                Completed(
                                                    UnitSuccess {
                                                        unitSuccess refreshResult with
                                                            Warning = Some pendingPrimarySaveWarning
                                                    }
                                                )
                                            )
                                    | Error refreshFailure -> return Error(failureMessage refreshFailure)
                            })
                            refreshedState.PublishOwners
                            failure
            }

            match versionOf success.Refresh with
            | None ->
                return! pendingPrimarySaveRemoteFailureAsync deps "The workspace state could not be read after saving."
            | Some version -> return! runPushAfterLocalCommit version
        | Ok(Completed(CloneSuccess _)) -> return Error "Primary save produced an invalid result."
        | Ok other -> return Ok other
    }

let private runSaveLfsSettingsAttemptAsync (deps: GitDependencies) (settings: StoragePolicySettingsDto) =
    simpleWriteAsync
        deps
        (fun () ->
            deps.setStoragePolicySettings {
                OperationId = deps.newOperationId ()
                Settings = settings
            }
        )

let private runCreateBranchAttemptAsync
    (deps: GitDependencies)
    (state: GitState)
    (branchRequest: GitSidebarCreateBranchRequest)
    =
    promise {
        match requireWorkspaceVersion state with
        | Error message -> return Error message
        | Ok version ->
            let baseRef =
                match branchRequest.StartPoint with
                | None -> Ok None
                | Some startPoint ->
                    match providerRefOf state startPoint with
                    | Some providerRef -> Ok(Some providerRef)
                    | None -> Error $"Branch '{startPoint}' is not known. Refresh and try again."

            match baseRef with
            | Error message -> return Error message
            | Ok baseRef ->
                return!
                    runTrackedWriteAsync
                        deps
                        (fun () ->
                            deps.createRef {
                                OperationId = deps.newOperationId ()
                                Name = branchRequest.BranchName
                                BaseRef = baseRef
                                SwitchTo = true
                                ExpectedWorkspaceVersion = version
                            }
                        )
                        (fun _ partial -> refreshAfterSuccess deps partial GitPageChange.Clear (Some None) None)
    }

let private runSwitchBranchAttemptAsync (deps: GitDependencies) (state: GitState) (refName: string) = promise {
    match requireWorkspaceVersion state, providerRefOf state refName with
    | Error message, _ -> return Error message
    | Ok _, None -> return Error $"Branch '{refName}' is not known. Refresh and try again."
    | Ok version, Some providerRef ->
        return!
            runTrackedWriteAsync
                deps
                (fun () ->
                    deps.switchRef {
                        OperationId = deps.newOperationId ()
                        TargetRef = providerRef
                        ExpectedWorkspaceVersion = version
                    }
                )
                (fun _ partial -> refreshAfterSuccess deps partial GitPageChange.Clear (Some None) None)
}

let private materializePendingAfterFinalizeAsync
    (deps: GitDependencies)
    (downloadLargeFiles: bool)
    (pendingMaterializations: (string * bool) list)
    =
    promise {
        let pathsToMaterialize =
            pendingMaterializations
            |> List.choose (fun (path, localObject) ->
                if downloadLargeFiles || localObject then
                    Some(path, localObject)
                else
                    None
            )

        let mutable firstFailedPath = None

        for (path, localObject) in pathsToMaterialize do
            let! materialized =
                toResult (
                    deps.materializeObject {
                        OperationId = deps.newOperationId ()
                        Path = path
                        RefreshTree = Some true
                    }
                )

            match materialized with
            | Ok(_, None) -> ()
            | Ok(_, Some _)
            | Error _ ->
                if firstFailedPath.IsNone then
                    firstFailedPath <- Some(path, localObject)

        return
            firstFailedPath
            |> Option.map (fun (path, localObject) ->
                if localObject then
                    $"Could not restore '{path}' from the local cache. Use Download LFS file to try again."
                else
                    $"Could not download '{path}'. Use Download LFS file to try again."
            )
    }

let private runFinalizeMergeAttemptAsync (deps: GitDependencies) (state: GitState) = promise {
    match requireWorkspaceVersion state, state.ActiveConflict with
    | Error message, _ -> return Error message
    | Ok _, None -> return Error "There is no merge to finalize."
    | Ok version, Some conflict ->
        let pendingMaterializations =
            pendingMaterializationsForSession
                conflict.Handle.SessionId
                state.PendingMaterializationSessionId
                state.PendingMaterializations

        return!
            runTrackedWriteAsync
                deps
                (fun () ->
                    deps.finalizeConflict {
                        OperationId = deps.newOperationId ()
                        Handle = conflict.Handle
                        ExpectedWorkspaceVersion = version
                        Message = None
                    }
                )
                (fun _ partial -> promise {
                    let! materializationNotice =
                        materializePendingAfterFinalizeAsync deps state.DownloadLargeFiles pendingMaterializations

                    let! refreshed =
                        refreshAfterSuccess deps partial GitPageChange.Clear (Some None) materializationNotice

                    return
                        match refreshed with
                        | Ok outcome -> Ok outcome
                        | Error message -> Ok(FinalizedButRefreshFailed message)
                })
}

let private runAbandonMergeAttemptAsync (deps: GitDependencies) (state: GitState) = promise {
    match requireWorkspaceVersion state, state.ActiveConflict with
    | Error message, _ -> return Error message
    | Ok _, None -> return Error "There is no merge to abandon."
    | Ok version, Some conflict ->
        return!
            runTrackedWriteAsync
                deps
                (fun () ->
                    deps.cancelConflict {
                        OperationId = deps.newOperationId ()
                        Handle = conflict.Handle
                        ExpectedWorkspaceVersion = version
                    }
                )
                (fun _ partial -> refreshAfterSuccess deps partial GitPageChange.Clear (Some None) None)
}

/// Lists the large objects and the paths among them that a lock-blocked checkout left with a stale index.
/// The status is read only when some object is on disk, because only those can be stale.
let private listObjectsWithStalePathsAsync (deps: GitDependencies) = promise {
    let! listed = toResult (deps.listObjects (request deps))

    match listed with
    | Error failure -> return Error failure
    | Ok(objectsOutcome, _) ->
        let objects = objectsOutcome.Value

        if objects |> Array.exists _.IsMaterialized then
            let! status = toResult (deps.getStatus (request deps))

            return
                status
                |> Result.map (fun (statusOutcome, _) -> objects, staleLfsPaths objects statusOutcome.Value)
        else
            return Ok(objects, [||])
}

let private anyObjectMissing (objects: ObjectStateDto[]) =
    objects |> Array.exists (fun objectState -> not objectState.IsMaterialized)

/// Asks the library to restage the stale large-file paths after a lock removal. A path that holds a real edit
/// is refused with the code local_changes and stays as it is. Any other failure of that call, a held lock
/// included, is returned. A listing or status that fails means there is nothing known to restage. The refresh
/// that follows reports a workspace that is really broken.
/// The result says whether a large object is still missing after the restage. It is None when the listing
/// after the restage failed.
let private restageStaleLfsPathsAsync (deps: GitDependencies) : JS.Promise<Result<bool option, OperationFailureDto>> = promise {
    let! listed = listObjectsWithStalePathsAsync deps

    match listed with
    | Error _ -> return Ok None
    | Ok(objects, [||]) -> return Ok(Some(anyObjectMissing objects))
    | Ok(_, stalePaths) ->
        let! restaged =
            toResult (
                deps.materializeObjects {
                    OperationId = deps.newOperationId ()
                    Paths = stalePaths
                }
            )

        match restaged with
        | Error failure
        | Ok(_, Some failure) when not (isLocalChangesRefusal failure) -> return Error failure
        | _ ->
            let! relisted = toResult (deps.listObjects (request deps))

            return
                match relisted with
                | Ok(objectsOutcome, _) -> Ok(Some(anyObjectMissing objectsOutcome.Value))
                | Error _ -> Ok None
}

/// Removes a stale lock, restages the large files that git-lfs had already written when the lock stopped a
/// checkout, and refreshes. The refreshed status decides what follows: an active conflict session is abandoned
/// through the normal path.
let private runClearStaleLockAttemptAsync (deps: GitDependencies) =
    runTrackedWriteAsync
        deps
        (fun () -> deps.clearStaleLock (request deps))
        (fun outcome partial -> promise {
            let removedLockPaths =
                outcome.Warnings
                |> Array.choose (fun warning ->
                    if warning.Code = VersionControlCodes.LockRemoved then
                        Some warning.Message
                    else
                        None
                )

            let warningMessage =
                match removedLockPaths with
                | [||] -> None
                | [| path |] -> Some $"Removed stale lock file: {path}."
                | paths ->
                    let joined = String.concat ", " paths
                    Some $"Removed stale lock files: {joined}."

            let! restaged = restageStaleLfsPathsAsync deps

            match restaged with
            | Error failure -> return! routedToOutcome deps (routeFailure None failure)
            | Ok objectsMissing ->
                let! refreshed = refreshAfterSuccess deps partial GitPageChange.NoChange None warningMessage

                return
                    refreshed
                    |> Result.map (
                        function
                        | Completed(UnitSuccess success) ->
                            Completed(
                                UnitSuccess {
                                    success with
                                        ObjectsMissing = objectsMissing
                                }
                            )
                        | other -> other
                    )
        })

let private runRestoreInterruptedPathsAttemptAsync (deps: GitDependencies) (state: GitState) (paths: string[]) = promise {
    match requireWorkspaceVersion state with
    | Error message -> return Error message
    | Ok version ->
        return!
            runTrackedWriteAsync
                deps
                (fun () ->
                    deps.restorePaths {
                        OperationId = deps.newOperationId ()
                        Paths = paths
                        ExpectedWorkspaceVersion = version
                    }
                )
                (fun _ partial -> refreshAfterSuccess deps partial GitPageChange.Clear (Some None) None)
}

/// Downloads the large objects an interrupted update left behind, one per object,
/// stopping at the first failure so its message is reported. The stale paths of a lock-blocked checkout
/// are requested as well, so the library restages them. A requested path that holds a real edit, stale or
/// pending, is refused with the code local_changes. It is skipped, keeps its edit and does not count as a failure.
let private runRetryMaterializationAttemptAsync (deps: GitDependencies) = promise {
    let! listed = listObjectsWithStalePathsAsync deps

    match listed with
    | Error failure -> return! routedToOutcome deps (routeFailure None failure)
    | Ok(objects, stalePaths) ->
        let pending =
            objects
            |> Array.filter (fun objectState -> not objectState.IsMaterialized)
            |> Array.map _.Path
            |> Array.append stalePaths
            |> Array.distinct

        let mutable failure: OperationFailureDto option = None

        for index, path in pending |> Array.indexed do
            if failure.IsNone then
                let! materialized =
                    toResult (
                        deps.materializeObject {
                            OperationId = deps.newOperationId ()
                            Path = path
                            RefreshTree = Some(index = pending.Length - 1)
                        }
                    )

                match materialized with
                | Ok _ -> ()
                | Error error when isLocalChangesRefusal error -> ()
                | Error error -> failure <- Some error

        match failure with
        | Some error -> return! routedToOutcome deps (routeFailure None error)
        | None -> return! refreshAfterSuccess deps None GitPageChange.NoChange None None
}

let private executeWriteAttemptOnce
    (deps: GitDependencies)
    (state: GitState)
    (writeRequest: WriteRequest)
    (reportPhase: GitBusyOperation -> unit)
    =
    promise {
        match writeRequest with
        | Fetch -> return! runFetchAttemptAsync deps state
        | Pull acceptance -> return! runPullAttemptAsync deps state acceptance
        | Push acceptance -> return! runPushAttemptAsync deps state acceptance
        | PrimarySave prepared -> return! runPrimarySaveAttemptAsync deps state prepared reportPhase
        | Clone(cloneRequest, _) -> return! runCloneAttemptAsync deps cloneRequest
        | CommitSelection prepared -> return! runCommitAttemptAsync deps state prepared
        | CommitAll prepared -> return! runCommitAttemptAsync deps state prepared
        | DiscardSelection paths -> return! runDiscardAttemptAsync deps state paths
        | SaveLfsSettings(_, settings) -> return! runSaveLfsSettingsAttemptAsync deps settings
        | PruneLfsCache -> return! simpleWriteAsync deps (fun () -> deps.pruneStorage (request deps))
        | DedupLfsStorage -> return! simpleWriteAsync deps (fun () -> deps.deduplicateStorage (request deps))
        | CreateBranch branchRequest -> return! runCreateBranchAttemptAsync deps state branchRequest
        | SwitchBranch refName -> return! runSwitchBranchAttemptAsync deps state refName
        | FinalizeMerge -> return! runFinalizeMergeAttemptAsync deps state
        | AbandonMerge -> return! runAbandonMergeAttemptAsync deps state
        | ClearStaleLock -> return! runClearStaleLockAttemptAsync deps
        | RestoreInterruptedPaths paths -> return! runRestoreInterruptedPathsAttemptAsync deps state paths
        | RetryMaterialization -> return! runRetryMaterializationAttemptAsync deps
    }

/// Writes that change the working tree from a state the user reviewed are not replayed
/// against a workspace that moved in between. A discard or a restore would drop content
/// the user has not seen. An update would run without its preview. An accepted synchronize is
/// pinned to the target and the token the user saw, so a stale token means the decision no
/// longer applies and a fresh preview is needed. Finalizing or
/// abandoning a merge would act on whichever conflict session the refresh found, which
/// is not the one the user reviewed.
/// A stale branch switch must run its preflight again before retrying the switch.
let private replaysAfterStaleToken =
    function
    | DiscardSelection _
    | RestoreInterruptedPaths _
    | Pull _
    | Push(GitUpdateAcceptance.Accepted _)
    | SwitchBranch _
    | FinalizeMerge
    | AbandonMerge -> false
    | _ -> true

let private staleWithoutReplayMessage =
    "The workspace changed since it was last refreshed, so the action was not repeated. Review the current changes and try again."

/// A stale workspace token that left the workspace unchanged is the normal outcome
/// when the workspace moved between the refresh and the click. The state is refreshed
/// and the write runs once more against the current token, unless replaying it could
/// discard content the user has not reviewed.
let private executeWriteAttempt
    (deps: GitDependencies)
    (state: GitState)
    (writeRequest: WriteRequest)
    (reportPhase: GitBusyOperation -> unit)
    =
    promise {
        let! first = executeWriteAttemptOnce deps state writeRequest reportPhase

        match first with
        | Ok(StaleWorkspaceVersion(message, targetMoved)) when not (replaysAfterStaleToken writeRequest) ->
            let reported = if targetMoved then message else staleWithoutReplayMessage

            return Ok(StaleWorkspaceVersion(reported, targetMoved))
        | Ok(StaleWorkspaceVersion(_, _)) ->
            let! refreshResult = refreshAllAsync deps

            match refreshResult.Status, refreshErrorMessage refreshResult with
            | Error failure, _ -> return Error(failureMessage failure)
            | Ok _, Some message -> return Error message
            | Ok _, None ->
                let! second =
                    executeWriteAttemptOnce deps (applyRefreshResult refreshResult state) writeRequest reportPhase

                match second with
                | Ok(StaleWorkspaceVersion(message, _)) -> return Error message
                | other -> return other
        | other -> return other
    }

/// Resolves one conflicted file with the choice the user reviewed, against the handle
/// and token captured with the page. When nothing remains, the merge is finalized.
let private confirmMergeResolutionAsync
    (deps: GitDependencies)
    (downloadLargeFiles: bool)
    (pendingMaterializations: (string * bool) list)
    (pendingMaterializationSessionId: string option)
    (deletedCandidatePick: bool)
    (resolution: GitMergeResolutionRequest)
    =
    promise {
        let! resolved =
            toResult (
                deps.resolveConflict {
                    OperationId = deps.newOperationId ()
                    Handle = resolution.Handle
                    ExpectedWorkspaceVersion = resolution.WorkspaceVersion
                    Path = resolution.Path
                    Resolution = resolution.Resolution
                }
            )

        let staleOrFailed pendingMaterializationPath materializationsConsumed (failure: OperationFailureDto) =
            if
                failure.Category = FailureCategoryDto.Concurrency
                || recoveryCode failure = Some VersionControlCodes.Recovery.RefreshConflictSession
            then
                ConfirmMergeResolutionError.Stale(
                    failureMessage failure,
                    pendingMaterializationPath,
                    materializationsConsumed,
                    resolution.Handle.SessionId
                )
            else
                ConfirmMergeResolutionError.Failed(
                    failureMessage failure,
                    pendingMaterializationPath,
                    materializationsConsumed,
                    resolution.Handle.SessionId
                )

        match resolved with
        | Error failure -> return Error(staleOrFailed None false failure)
        | Ok(outcome, partialFailure) ->
            let isCandidatePick =
                match resolution.Resolution with
                | ConflictResolutionDto.PickCandidate _ -> true
                | ConflictResolutionDto.SupplyResolvedContent _ -> false

            let objectWasNotMaterialized =
                isCandidatePick
                && (outcome.Warnings
                    |> Array.exists (fun warning -> warning.Code = VersionControlCodes.ObjectNotMaterialized))

            let partialNeedsMaterialization =
                isCandidatePick
                && (partialFailure
                    |> Option.exists (fun failure ->
                        failure.Code = "object_materialization_failed"
                        || recoveryCode failure = Some VersionControlCodes.Recovery.RetryMaterialization
                    ))

            let localObjectMaterializationFailure =
                isCandidatePick
                && (partialFailure
                    |> Option.exists (fun failure -> failure.Code = "object_materialization_failed"))

            let pendingMaterializationPath =
                if
                    not deletedCandidatePick
                    && (objectWasNotMaterialized || partialNeedsMaterialization)
                then
                    Some(resolution.Path, localObjectMaterializationFailure)
                else
                    None

            let notice =
                if pendingMaterializationPath.IsNone then
                    None
                elif partialNeedsMaterialization then
                    Some
                        "The file was resolved, but its content could not be restored from the local cache yet. ARCitISA restores it after the merge."
                elif objectWasNotMaterialized && not downloadLargeFiles then
                    match resolution.Resolution with
                    | ConflictResolutionDto.PickCandidate candidateId when candidateId = useOnlineCandidateId ->
                        Some "The online version isn't downloaded yet. Use Download LFS file to get it after the merge."
                    | ConflictResolutionDto.PickCandidate candidateId when candidateId = keepMineCandidateId ->
                        Some "Your version is not in the local cache. Use Download LFS file to get it after the merge."
                    | _ -> Some "Use Download LFS file to get this file after the merge."
                else
                    None

            let! statusResult = toResult (deps.getStatus (request deps))

            match statusResult with
            | Error failure -> return Error(staleOrFailed pendingMaterializationPath false failure)
            | Ok(statusOutcome, _) ->
                let status = statusOutcome.Value

                if outcome.Value.RemainingItems.Length = 0 then
                    let! finalized =
                        toResult (
                            deps.finalizeConflict {
                                OperationId = deps.newOperationId ()
                                Handle = outcome.Value.RefreshedHandle
                                ExpectedWorkspaceVersion = status.WorkspaceVersion
                                Message = None
                            }
                        )

                    match finalized with
                    | Error failure -> return Error(staleOrFailed pendingMaterializationPath false failure)
                    | Ok _ ->
                        let sessionMaterializations =
                            pendingMaterializationsForSession
                                resolution.Handle.SessionId
                                pendingMaterializationSessionId
                                pendingMaterializations

                        let pathsToMaterialize, _ =
                            enqueuePendingMaterialization
                                resolution.Handle.SessionId
                                pendingMaterializationPath
                                sessionMaterializations
                                (Some resolution.Handle.SessionId)

                        let! materializationNotice =
                            materializePendingAfterFinalizeAsync deps downloadLargeFiles pathsToMaterialize

                        let! afterFinalize = toResult (deps.getStatus (request deps))

                        match afterFinalize with
                        | Error failure ->
                            return
                                Error(
                                    ConfirmMergeResolutionError.Failed(
                                        failureMessage failure,
                                        None,
                                        true,
                                        resolution.Handle.SessionId
                                    )
                                )
                        | Ok(finalStatus, _) ->
                            return
                                Ok {
                                    UpdatedStatus = finalStatus.Value
                                    NextConflictedPath = None
                                    PageChange = GitPageChange.Clear
                                    Notice = materializationNotice |> Option.orElse notice
                                    PendingMaterializationPath = pendingMaterializationPath
                                    ResolutionSessionId = resolution.Handle.SessionId
                                    Finalized = true
                                }
                else
                    let nextPath = outcome.Value.RemainingItems.[0].Path

                    let refreshedSession: ConflictSessionSummaryDto = {
                        Handle = outcome.Value.RefreshedHandle
                        Items = outcome.Value.RemainingItems
                    }

                    let! pageResult = deps.loadConflictPage refreshedSession status.WorkspaceVersion nextPath

                    return
                        pageResult
                        |> Result.map (fun page -> {
                            UpdatedStatus = {
                                status with
                                    ActiveConflictSession = Some refreshedSession
                            }
                            NextConflictedPath = Some nextPath
                            PageChange = GitPageChange.Set page
                            Notice = notice
                            PendingMaterializationPath = pendingMaterializationPath
                            ResolutionSessionId = resolution.Handle.SessionId
                            Finalized = false
                        })
                        |> Result.mapError (fun message ->
                            ConfirmMergeResolutionError.Failed(
                                message,
                                pendingMaterializationPath,
                                false,
                                resolution.Handle.SessionId
                            )
                        )
    }

let init () : GitState * Cmd<Msg> = GitState.Empty, Cmd.none

/// The four git values a state shows before the main process reports the ones of a session.
let private withSeededGitValues (defaults: UserDefaultValuesDto) (model: GitState) = {
    model with
        UserDefaults = defaults
        LfsAutoTrackThresholdMb = defaults.LfsAutoTrackThresholdMb
        DiffIndexingLimitMb = defaults.DiffIndexingLimitMb
        DownloadLargeFiles = defaults.DownloadLargeFiles
        UploadRightAway = defaults.UploadRightAway
}

let private resetFor (model: GitState) =
    {
        GitState.Empty with
            SidebarVisible = model.SidebarVisible
            ExternalRefreshGeneration = model.ExternalRefreshGeneration + 1
            // The page load counter keeps counting. A page load of an ARC that closed could finish after
            // a counter that started again at 0 has reached its id in a later ARC.
            PageLoadRequestId = model.PageLoadRequestId
    }
    |> withSeededGitValues model.UserDefaults

let private missingRepositoryModel (model: GitState) = {
    resetFor model with
        CurrentArcPath = model.CurrentArcPath
        ArcSessionId = model.ArcSessionId
        RefreshRequestId = model.RefreshRequestId
        PageLoadRequestId = model.PageLoadRequestId
        WriteRequestId = model.WriteRequestId
        RepositoryAvailability = GitRepositoryAvailability.MissingRepository
}

/// Opening the Git tab refreshes silently. For an ARC without a repository that refresh fails
/// every time, and clearing the page then would close whatever page the main area shows. The
/// page is cleared only when the model learns about the missing repository.
let private clearPageOnFirstMissingCmd (setPageState: PageState option -> unit) (model: GitState) =
    match model.RepositoryAvailability with
    | GitRepositoryAvailability.MissingRepository -> Cmd.none
    | GitRepositoryAvailability.Ready -> applyPageChangeCmd setPageState GitPageChange.Clear

let private clearBusy (model: GitState) = {
    model with
        BusyOperation = None
        BusyNotice = None
        CurrentProgress = None
        CurrentProgressPhase = None
        CurrentSteps = [||]
        CurrentStepPhases = [||]
        CurrentLog = OperationLog.empty
        OperationReported = false
        OperationStartedAt = None
        CurrentOperation = None
        RefreshOperationId = None
}

let private writeErrorModel (message: string) (model: GitState) = {
    clearBusy model with
        ErrorNotice = Some message
        WarningNotice = None
}

let private busyOperationForWriteRequest =
    function
    | Fetch -> GitBusyOperation.FetchingFromRemote
    | Pull _ -> GitBusyOperation.PullingFromRemote
    | Push _ -> GitBusyOperation.PushingToRemote
    | PrimarySave prepared -> prepared.BusyOperation
    | Clone(cloneRequest, _) -> GitBusyOperation.CloningRepository cloneRequest.TargetPath
    | CommitSelection prepared -> prepared.BusyOperation
    | CommitAll prepared -> prepared.BusyOperation
    | DiscardSelection _ -> GitBusyOperation.DiscardingSelectedChanges
    | SaveLfsSettings(busyOperation, _) -> busyOperation
    | PruneLfsCache -> GitBusyOperation.PruningGitLfsCache
    | DedupLfsStorage -> GitBusyOperation.DeduplicatingGitLfsStorage
    | CreateBranch _ -> GitBusyOperation.CreatingBranch
    | SwitchBranch _ -> GitBusyOperation.SwitchingBranch
    | FinalizeMerge -> GitBusyOperation.FinalizingMerge
    | AbandonMerge -> GitBusyOperation.AbandoningMerge
    | ClearStaleLock -> GitBusyOperation.ClearingStaleLock
    | RestoreInterruptedPaths _ -> GitBusyOperation.RestoringInterruptedPaths
    | RetryMaterialization -> GitBusyOperation.RetryingMaterialization

let private requiresArcForWriteRequest =
    function
    | Clone _ -> false
    | _ -> true

let private resolveCloneReplyCmd writeRequest result =
    match writeRequest, result with
    | Clone(_, reply), Ok(CloneSuccess path) -> resolveReplyCmd reply (Ok path)
    | Clone(_, reply), Error message -> resolveReplyCmd reply (Error message)
    | Clone(_, reply), Ok _ -> resolveReplyCmd reply (Error "Clone request produced an invalid result.")
    | _ -> Cmd.none

let private resolveStaleWriteCompletedCmd writeRequest result =
    match result with
    | Ok(Completed success) -> resolveCloneReplyCmd writeRequest (Ok success)
    | Ok(CompletedWithPendingRemoteConfirmation(success, _, _)) -> resolveCloneReplyCmd writeRequest (Ok success)
    | Ok(CompletedWithPendingRemoteFailure(success, _)) -> resolveCloneReplyCmd writeRequest (Ok success)
    | Ok(RequiresDependencyInstall _) -> resolveCloneReplyCmd writeRequest (Error staleArcSessionMessage)
    | Ok(RequiresRemoteProjectRename _) -> resolveCloneReplyCmd writeRequest (Error staleArcSessionMessage)
    | Ok(RequiresRecovery(_, message)) -> resolveCloneReplyCmd writeRequest (Error message)
    | Ok(ProvisioningIncomplete(_, message)) -> resolveCloneReplyCmd writeRequest (Error message)
    | Ok(OperationCancelled message) -> resolveCloneReplyCmd writeRequest (Error message)
    | Ok(StaleWorkspaceVersion(message, _)) -> resolveCloneReplyCmd writeRequest (Error message)
    | Ok(FinalizedButRefreshFailed message) -> resolveCloneReplyCmd writeRequest (Error message)
    | Error message -> resolveCloneReplyCmd writeRequest (Error message)

/// The first library call of a write uses the id allocated when the write was
/// requested, so a cancel that lands before the started event still reaches it.
let private withFirstOperationId (deps: GitDependencies) (operationId: string) =
    let handedOut = ref false
    let nextSuffix = ref 1

    {
        deps with
            newOperationId =
                fun () ->
                    if handedOut.Value then
                        let suffix = nextSuffix.Value
                        nextSuffix.Value <- suffix + 1
                        $"{operationId}/{suffix}"
                    else
                        handedOut.Value <- true
                        operationId
    }

let private operationRootId (operationId: string) =
    let separatorIndex = operationId.IndexOf "/"

    if separatorIndex < 0 then
        operationId
    else
        operationId.Substring(0, separatorIndex)

/// Whether an event belongs to an operation other than the one this workflow runs, such as
/// a file tree action that started in the same window. The sub-operations of a write share the root
/// id of the write. A workflow that does not know its operation id (a silent refresh, the rename of the ARC
/// folder) takes every event.
let private isForeignOperation (model: GitState) (operationId: string) =
    let ownOperationId =
        model.CurrentOperation
        |> Option.map _.OperationId
        |> Option.orElse model.RefreshOperationId

    match ownOperationId with
    | Some current -> operationRootId current <> operationRootId operationId
    | None -> false

/// Whether the sidebar shows the events (progress and output) of the operation with this id. A silent refresh has
/// no notice text. Events from another source, such as the text diff scan, would otherwise show the progress
/// banner that the silent refresh avoids. The rename of the ARC folder runs no version control call, so any event
/// belongs to another operation.
let private showsOperationEvents (model: GitState) (operationId: string) =
    match model.BusyOperation with
    | None -> false
    | Some GitBusyOperation.Refreshing when model.BusyNotice.IsNone -> false
    | Some GitBusyOperation.RenamingRepository -> false
    | Some _ -> not (isForeignOperation model operationId)

let private writeCmd
    (deps: GitDependencies)
    (model: GitState)
    (writeRequest: WriteRequest)
    (sessionId: int)
    (writeRequestId: int)
    (operationId: string)
    =
    Cmd.ofEffect (fun dispatch ->
        promise {
            try
                let writeDeps = withFirstOperationId deps operationId

                let reportPhase =
                    fun phase -> dispatch (WritePhaseChanged(sessionId, writeRequestId, phase))

                let! result = executeWriteAttempt writeDeps model writeRequest reportPhase

                dispatch (WriteCompleted(sessionId, writeRequestId, writeRequest, result))
            with err ->
                dispatch (WriteCompleted(sessionId, writeRequestId, writeRequest, Error(string err)))
        }
        |> Promise.start
    )

/// Whether the post-merge publish should run now: the primary save asked for it and the
/// workspace has no open conflict session anymore.
let private shouldRunPostMergePush (model: GitState) =
    model.PendingPostMergePush && model.ActiveConflict.IsNone

/// The restage after a lock removal found every large object on disk, so Download now would have nothing to fetch.
let private lockRemovalFoundNoMissingObject (success: WriteSuccess) =
    match success with
    | UnitSuccess { ObjectsMissing = Some false } -> true
    | _ -> false

let private updateCore
    (deps: GitDependencies)
    (setPageState: PageState option -> unit)
    (msg: Msg)
    (model: GitState)
    : GitState * Cmd<Msg> =
    match msg with
    | ResetWorkflow -> resetFor model, Cmd.none
    | DiffPageMsg diffMsg -> GitDiffPageLoader.update deps diffMsg model
    | SetCurrentProgress progress when showsOperationEvents model progress.OperationId ->
        let steps, stepPhases =
            applyProgressToSteps model.CurrentSteps model.CurrentStepPhases progress

        {
            model with
                CurrentProgress = Some(mergeProgressUpdate model steps progress)
                CurrentProgressPhase = Some progress.PhaseCode
                CurrentSteps = steps
                CurrentStepPhases = stepPhases
                OperationReported = true
        },
        Cmd.none
    // Progress of a silent refresh, of another operation, of the rename of the ARC folder or after the busy
    // operation ended. The same model instance returns, so the many events of a file tree action cause no render.
    | SetCurrentProgress _ -> model, Cmd.none
    | OperationOutputReceived output when showsOperationEvents model output.OperationId ->
        {
            model with
                CurrentLog = OperationLog.append output.Entries model.CurrentLog
                OperationReported = true
        },
        Cmd.none
    | OperationOutputReceived _ -> model, Cmd.none
    | OperationStarted key when model.BusyOperation.IsSome ->
        match model.CurrentOperation with
        | Some currentOperation ->
            let rootOperationId = operationRootId currentOperation.OperationId

            let belongsToWrite =
                key.OperationId = rootOperationId
                || key.OperationId.StartsWith(rootOperationId + "/", StringComparison.Ordinal)

            if belongsToWrite then
                {
                    model with
                        CurrentOperation = Some key
                },
                Cmd.none
            else
                model, Cmd.none
        | None -> model, Cmd.none
    | OperationStarted _ -> model, Cmd.none
    | ArcPathChanged arcPath when arcPath = model.CurrentArcPath -> model, Cmd.none
    | ArcPathChanged arcPath ->
        let pendingPublishAfterRefresh =
            match model.PendingPublishForPath, arcPath with
            | Some pendingPath, Some nextPath -> Swate.Components.Shared.PathHelpers.pathsEqual pendingPath nextPath
            | _ -> false

        let nextModel = {
            resetFor model with
                CurrentArcPath = arcPath
                ArcSessionId = nextArcSessionId model
                // The request counters keep counting across ARCs. Resetting them let a
                // refresh or a page load of the previous ARC match an id of the new one.
                RefreshRequestId = nextRefreshRequestId model
                PageLoadRequestId = nextPageLoadRequestId model
                PendingPublishAfterRefresh = pendingPublishAfterRefresh
                PendingPublishForPath = None
                // The renamed ARC still publishes for the owner the user picked.
                PublishOwners =
                    if pendingPublishAfterRefresh then
                        model.PublishOwners
                    else
                        None
                PublishOwnerChoice =
                    if pendingPublishAfterRefresh then
                        model.PublishOwnerChoice
                    else
                        None
        }

        let cmd =
            match arcPath with
            | Some _ ->
                Cmd.batch [
                    applyPageChangeCmd setPageState GitPageChange.Clear
                    Cmd.ofMsg RefreshRequested
                ]
            | None -> applyPageChangeCmd setPageState GitPageChange.Clear

        nextModel, cmd
    | SidebarVisibilityChanged false ->
        {
            model with
                SidebarVisible = false
                ExternalRefreshGeneration = model.ExternalRefreshGeneration + 1
        },
        Cmd.none
    | SidebarVisibilityChanged true ->
        let nextModel = {
            model with
                SidebarVisible = true
                ExternalChangesSeen = false
        }

        // Without an ARC the silent refresh would reset the model, including a clone that runs
        // from the DataHub browser. A running refresh would be replaced, and its errors would
        // no longer reach the user. The running refresh shows the state when it finishes, and an
        // ARC change starts its own refresh, so the flag is cleared in both cases.
        if
            model.CurrentArcPath.IsNone
            || model.BusyOperation = Some GitBusyOperation.Refreshing
        then
            nextModel, Cmd.none
        else
            nextModel, Cmd.ofMsg RefreshRequestedSilently
    | UploadRightAwayChanged value ->
        {
            model with
                UploadRightAway = value
                // The user's choice is newer than anything a read of the stored value returns.
                UploadRightAwayLoad = UploadRightAwayLoad.Settled
        },
        storeUploadRightAwayCmd deps value
    | UploadRightAwayLoaded(arcSessionId, value) when
        arcSessionId = model.ArcSessionId
        && model.UploadRightAwayLoad = UploadRightAwayLoad.Pending
        ->
        {
            model with
                UploadRightAway = value
                UploadRightAwayLoad = UploadRightAwayLoad.Settled
        },
        Cmd.none
    | UploadRightAwayLoaded _ -> model, Cmd.none
    | UserDefaultsLoaded defaults when model.CurrentArcPath.IsNone -> withSeededGitValues defaults model, Cmd.none
    // The values of an open ARC come from its session. Only the seed for the next reset changes.
    | UserDefaultsLoaded defaults -> { model with UserDefaults = defaults }, Cmd.none
    | ExternalChangesDetected when not model.SidebarVisible ->
        {
            model with
                ExternalChangesSeen = true
        },
        Cmd.none
    | ExternalChangesDetected ->
        let generation = model.ExternalRefreshGeneration + 1

        {
            model with
                ExternalRefreshGeneration = generation
        },
        Cmd.OfPromise.perform
            (fun () -> deps.delay externalChangesRefreshDelayMs)
            ()
            (fun () -> ExternalRefreshDue generation)
    | ExternalRefreshDue generation when generation <> model.ExternalRefreshGeneration -> model, Cmd.none
    | ExternalRefreshDue _ -> model, Cmd.ofMsg RefreshRequestedSilently
    | GitRepositoryInitialized arcPath ->
        match model.CurrentArcPath with
        | Some currentArcPath when Swate.Components.Shared.PathHelpers.pathsEqual currentArcPath arcPath ->
            model, Cmd.ofMsg RefreshRequested
        | _ -> model, Cmd.none
    | RefreshRequested when model.CurrentArcPath.IsNone ->
        {
            resetFor model with
                CurrentArcPath = model.CurrentArcPath
                ArcSessionId = model.ArcSessionId
        },
        Cmd.none
    | RefreshRequested when
        model.BusyOperation.IsSome
        && not (
            model.BusyOperation = Some GitBusyOperation.Refreshing
            && model.CurrentOperation.IsNone
        )
        ->
        // A write may report Refreshing while it still owns CurrentOperation. Queue the
        // refresh until the write completes so it does not clear the write state.
        {
            model with
                RefreshPending = true
                RefreshPendingSilently = false
        },
        Cmd.none
    | RefreshRequested ->
        let requestId = nextRefreshRequestId model
        // The calls of the refresh take sub-ids of this id. isForeignOperation then drops the progress of other
        // operations, such as a file tree action in the same window.
        let operationId = deps.newOperationId ()

        let nextModel =
            model
            |> withBusyOperation (Some GitBusyOperation.Refreshing)
            |> startRefreshRequest requestId
            |> fun state -> {
                state with
                    RefreshOperationId = Some operationId
                    ErrorNotice = None
                    WarningNotice =
                        match state.PendingRefreshWarningNotice with
                        | Some _ -> state.WarningNotice
                        | None -> None
            }

        let cmd =
            Cmd.OfPromise.either
                refreshAllAsync
                (withFirstOperationId deps operationId)
                (fun refreshResult -> RefreshCompleted(requestId, Ok refreshResult))
                (fun err -> RefreshCompleted(requestId, Error(string err)))

        nextModel, cmd
    | RefreshRequestedSilently when model.CurrentArcPath.IsNone ->
        {
            resetFor model with
                CurrentArcPath = model.CurrentArcPath
                ArcSessionId = model.ArcSessionId
        },
        Cmd.none
    | RefreshRequestedSilently when
        model.BusyOperation.IsSome
        && not (
            model.BusyOperation = Some GitBusyOperation.Refreshing
            && model.CurrentOperation.IsNone
        )
        ->
        if model.RefreshPending then
            model, Cmd.none
        else
            {
                model with
                    RefreshPending = true
                    RefreshPendingSilently = true
            },
            Cmd.none
    | RefreshRequestedSilently ->
        let requestId = nextRefreshRequestId model

        let nextModel =
            model
            |> withBusyOperation (Some GitBusyOperation.Refreshing)
            |> startRefreshRequest requestId
            // No notice text, so the sidebar shows no banner that pushes its content down.
            |> fun state -> { state with BusyNotice = None }

        let cmd =
            Cmd.OfPromise.either
                refreshAllAsync
                deps
                (fun refreshResult -> SilentRefreshCompleted(requestId, Ok refreshResult))
                (fun err -> SilentRefreshCompleted(requestId, Error(string err)))

        nextModel, cmd
    | RefreshCompleted(requestId, _) when requestId <> model.RefreshRequestId -> model, Cmd.none
    | RefreshCompleted(_, Error message) ->
        let nextModel = {
            clearBusy model with
                RefreshState = GitRefreshState.Idle
                ErrorNotice = Some message
                WarningNotice = None
                PendingRefreshWarningNotice = None
        }

        nextModel,
        Cmd.batch [
            applyPageChangeCmd setPageState GitPageChange.Clear
            reportErrorCmd deps "Could not refresh Git state" message
        ]
    | RefreshCompleted(_, Ok refreshResult) when refreshFailure refreshResult |> Option.exists isMissingRepository ->
        missingRepositoryModel model, clearPageOnFirstMissingCmd setPageState model
    | RefreshCompleted(_, Ok refreshResult) ->
        let nextModel = model |> applyRefreshResult refreshResult |> clearBusy

        let cmd =
            match refreshErrorMessage refreshResult with
            | Some message ->
                Cmd.batch [
                    applyPageChangeCmd setPageState GitPageChange.Clear
                    reportErrorCmd deps "Could not refresh Git state" message
                ]
            | None when nextModel.PendingPublishAfterRefresh ->
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            | None -> Cmd.none

        let loadingModel, loadCmd =
            startUploadRightAwayLoad deps refreshResult {
                nextModel with
                    PendingPublishAfterRefresh = false
            }

        loadingModel, Cmd.batch [ cmd; loadCmd ]
    | SilentRefreshCompleted(requestId, _) when requestId <> model.RefreshRequestId -> model, Cmd.none
    | SilentRefreshCompleted(_, Error _) ->
        {
            clearBusy model with
                RefreshState = GitRefreshState.Idle
        },
        Cmd.none
    | SilentRefreshCompleted(_, Ok refreshResult) when refreshFailure refreshResult |> Option.exists isMissingRepository ->
        missingRepositoryModel model, clearPageOnFirstMissingCmd setPageState model
    | SilentRefreshCompleted(_, Ok refreshResult) when refreshFailure refreshResult |> Option.isSome ->
        {
            clearBusy model with
                RefreshState = GitRefreshState.Idle
        },
        Cmd.none
    | SilentRefreshCompleted(_, Ok refreshResult) ->
        let nextModel = model |> applyRefreshResult refreshResult |> clearBusy

        let loadingModel, loadCmd =
            startUploadRightAwayLoad deps refreshResult {
                nextModel with
                    ErrorNotice = model.ErrorNotice
                    WarningNotice = model.WarningNotice
                    PendingRefreshWarningNotice = model.PendingRefreshWarningNotice
                    PendingPublishAfterRefresh = false
            }

        let publishCmd =
            if nextModel.PendingPublishAfterRefresh then
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            else
                Cmd.none

        loadingModel, Cmd.batch [ publishCmd; loadCmd ]
    | InitRepositoryRequested when model.CurrentArcPath.IsNone -> model, Cmd.none
    | InitRepositoryRequested ->
        let operationId = deps.newOperationId ()

        let nextModel =
            model
            |> withBusyOperation (Some GitBusyOperation.InitializingRepository)
            |> fun state -> {
                state with
                    ErrorNotice = None
                    WarningNotice = None
                    CurrentOperation = Some { OperationId = operationId }
            }

        let cmd =
            Cmd.OfPromise.either
                (fun (deps, arcPath) -> runInitRepositoryAsync deps arcPath)
                (withFirstOperationId deps operationId, Option.get model.CurrentArcPath)
                (fun result -> InitRepositoryCompleted(model.ArcSessionId, result))
                (fun err -> InitRepositoryCompleted(model.ArcSessionId, Error(string err)))

        nextModel, cmd
    | InitRepositoryCompleted(sessionId, _) when sessionId <> model.ArcSessionId -> model, Cmd.none
    | InitRepositoryCompleted(_, Error message) ->
        let nextModel = {
            clearBusy model with
                ErrorNotice = Some message
                WarningNotice = None
                PendingRefreshWarningNotice = None
        }

        nextModel, reportErrorCmd deps "Could not initialize Git repository" message
    | InitRepositoryCompleted(_, Ok()) ->
        let nextModel = {
            clearBusy model with
                RepositoryAvailability = GitRepositoryAvailability.Ready
                ErrorNotice = None
                WarningNotice = None
                PendingRefreshWarningNotice = None
        }

        nextModel, Cmd.ofMsg RefreshRequested
    | SelectChangeRequested(_, reply) when model.CurrentArcPath.IsNone ->
        model, resolveReplyCmd reply (Error "No ARC is loaded.")
    | SelectChangeRequested(change, reply) when not change.IsConflicted ->
        let requestId = nextPageLoadRequestId model
        let page, openCmd = GitDiffPageLoader.start deps requestId change

        {
            model with
                ErrorNotice = None
                PageLoadRequestId = requestId
                SelectedChangePath = Some change.Path
                DiffPage = Some page
        },
        Cmd.batch [
            GitDiffPageLoader.publishCmd setPageState page
            openCmd
            resolveReplyCmd reply (Ok())
        ]
    | SelectChangeRequested(change, reply) ->
        let requestId = nextPageLoadRequestId model

        // An open diff stays with its handle until the loaded page replaces it on screen. The
        // page state then leaves the diff, and the handle closes. A load that fails or finds no
        // conflict leaves the diff as it was, with a handle that still answers.
        let nextModel = {
            model with
                ErrorNotice = None
                PageLoadRequestId = requestId
        }

        let cmd =
            Cmd.OfPromise.either
                (fun (deps: GitDependencies, change: GitSidebarChange) ->
                    loadPageAsync deps model.ActiveConflict model.WorkspaceVersion change
                )
                (deps, change)
                (fun result -> SelectChangeCompleted(requestId, change.Path, reply, result))
                (fun err -> SelectChangeCompleted(requestId, change.Path, reply, Error(string err)))

        nextModel, cmd
    | SelectChangeCompleted(requestId, _path, reply, _) when requestId <> model.PageLoadRequestId ->
        model, resolveReplyCmd reply (Ok())
    | SelectChangeCompleted(_, path, reply, Ok pageChange) ->
        let nextModel = {
            model with
                SelectedChangePath = Some path
                ErrorNotice = None
        }

        nextModel,
        Cmd.batch [
            applyPageChangeCmd setPageState pageChange
            resolveReplyCmd reply (Ok())
        ]
    | SelectChangeCompleted(_, _path, reply, Error message) ->
        let nextModel = {
            model with
                ErrorNotice = Some message
        }

        nextModel,
        Cmd.batch [
            resolveReplyCmd reply (Error message)
            reportErrorCmd deps "Could not open Git change" message
        ]
    | ConfirmMergeResolutionRequested _ when model.CurrentArcPath.IsNone -> model, Cmd.none
    | ConfirmMergeResolutionRequested resolution ->
        match model.BusyOperation with
        | Some _ -> model, Cmd.none
        | None when model.MergeResolutionPendingPath = Some resolution.Path -> model, Cmd.none
        | None ->
            let deletedCandidatePick =
                match resolution.Resolution with
                | ConflictResolutionDto.PickCandidate candidateId ->
                    model.ActiveConflict
                    |> Option.bind (fun conflict ->
                        conflict.Items |> Array.tryFind (fun item -> item.Path = resolution.Path)
                    )
                    |> Option.bind (fun item ->
                        item.Candidates
                        |> Array.tryFind (fun candidate -> candidate.CandidateId = candidateId)
                    )
                    |> Option.exists (fun candidate -> candidate.Preview.IsNone && candidate.Object.IsNone)
                | ConflictResolutionDto.SupplyResolvedContent _ -> false

            let operationId = deps.newOperationId ()

            let nextModel =
                model
                |> withBusyOperation (Some(GitBusyOperation.ConfirmingMergeResolution resolution.Path))
                |> fun state -> {
                    state with
                        CurrentOperation = Some { OperationId = operationId }
                        MergeResolutionPendingPath = Some resolution.Path
                        ErrorNotice = None
                        WarningNotice =
                            if List.isEmpty state.PendingMaterializations then
                                None
                            else
                                state.WarningNotice
                }

            let cmd =
                Cmd.OfPromise.either
                    (fun
                        (deps,
                         downloadLargeFiles,
                         pendingMaterializations,
                         pendingMaterializationSessionId,
                         deletedCandidatePick,
                         resolution) ->
                        confirmMergeResolutionAsync
                            deps
                            downloadLargeFiles
                            pendingMaterializations
                            pendingMaterializationSessionId
                            deletedCandidatePick
                            resolution
                    )
                    (withFirstOperationId deps operationId,
                     model.DownloadLargeFiles,
                     model.PendingMaterializations,
                     model.PendingMaterializationSessionId,
                     deletedCandidatePick,
                     resolution)
                    (fun result -> ConfirmMergeResolutionCompleted(model.ArcSessionId, result))
                    (fun err ->
                        ConfirmMergeResolutionCompleted(
                            model.ArcSessionId,
                            Error(
                                ConfirmMergeResolutionError.Failed(
                                    string err,
                                    None,
                                    false,
                                    resolution.Handle.SessionId
                                )
                            )
                        )
                    )

            nextModel, cmd
    | ConfirmMergeResolutionCompleted(sessionId, _) when sessionId <> model.ArcSessionId -> model, Cmd.none
    | ConfirmMergeResolutionCompleted(_,
                                      Error(ConfirmMergeResolutionError.Stale(message,
                                                                              pendingPath,
                                                                              consumed,
                                                                              resolutionSessionId))) ->
        // The handle or the workspace changed underneath the resolution. Reload both
        // and let the user redo the choice deliberately.
        let pendingMaterializations, pendingMaterializationSessionId =
            if consumed then
                [], None
            else
                enqueuePendingMaterialization
                    resolutionSessionId
                    pendingPath
                    model.PendingMaterializations
                    model.PendingMaterializationSessionId

        let nextModel = {
            clearBusy model with
                MergeResolutionPendingPath = None
                SelectedChangePath = None
                ErrorNotice = Some message
                WarningNotice = None
                PendingMaterializations = pendingMaterializations
                PendingMaterializationSessionId = pendingMaterializationSessionId
        }

        nextModel,
        Cmd.batch [
            applyPageChangeCmd setPageState GitPageChange.Clear
            Cmd.ofMsg RefreshRequested
            reportErrorCmd deps "Could not confirm merge resolution" message
        ]
    | ConfirmMergeResolutionCompleted(_,
                                      Error(ConfirmMergeResolutionError.Failed(message,
                                                                               pendingPath,
                                                                               consumed,
                                                                               resolutionSessionId))) ->
        let pendingMaterializations, pendingMaterializationSessionId =
            if consumed then
                [], None
            else
                enqueuePendingMaterialization
                    resolutionSessionId
                    pendingPath
                    model.PendingMaterializations
                    model.PendingMaterializationSessionId

        let nextModel = {
            clearBusy model with
                MergeResolutionPendingPath = None
                ErrorNotice = Some message
                WarningNotice = None
                PendingMaterializations = pendingMaterializations
                PendingMaterializationSessionId = pendingMaterializationSessionId
        }

        // Resolve may have written the file before failing, so the state is reloaded.
        nextModel,
        Cmd.batch [
            Cmd.ofMsg RefreshRequested
            reportErrorCmd deps "Could not confirm merge resolution" message
        ]
    | ConfirmMergeResolutionCompleted(_, Ok outcome) ->
        let nextModel =
            model
            |> applyStatus outcome.UpdatedStatus
            |> clearBusy
            |> fun state ->
                let pendingMaterializations, pendingMaterializationSessionId =
                    if outcome.Finalized then
                        [], None
                    else
                        enqueuePendingMaterialization
                            outcome.ResolutionSessionId
                            outcome.PendingMaterializationPath
                            state.PendingMaterializations
                            state.PendingMaterializationSessionId

                {
                    state with
                        MergeResolutionPendingPath = None
                        SelectedChangePath = outcome.NextConflictedPath
                        ErrorNotice = None
                        WarningNotice =
                            outcome.Notice
                            |> Option.orElse (
                                if
                                    not (List.isEmpty model.PendingMaterializations)
                                    && (not outcome.Finalized || not model.DownloadLargeFiles)
                                then
                                    model.WarningNotice
                                else
                                    None
                            )
                        PendingMaterializations = pendingMaterializations
                        PendingMaterializationSessionId = pendingMaterializationSessionId
                }

        if outcome.Finalized && shouldRunPostMergePush nextModel then
            {
                nextModel with
                    PendingPostMergePush = false
            },
            Cmd.batch [
                applyPageChangeCmd setPageState outcome.PageChange
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            ]
        else
            nextModel, applyPageChangeCmd setPageState outcome.PageChange
    | SaveDownloadLargeFilesRequested downloadLargeFiles when model.CurrentArcPath.IsNone ->
        {
            model with
                DownloadLargeFiles = downloadLargeFiles
        },
        Cmd.none
    | SaveDownloadLargeFilesRequested downloadLargeFiles ->
        model,
        Cmd.ofMsg (
            WriteRequested(
                SaveLfsSettings(
                    GitBusyOperation.SavingGitLfsDownloadPreference,
                    buildUpdatedLfsSettings model None (Some downloadLargeFiles) None
                )
            )
        )
    | SaveLfsAutoTrackThresholdRequested thresholdMb ->
        model,
        Cmd.ofMsg (
            WriteRequested(
                SaveLfsSettings(
                    GitBusyOperation.SavingGitLfsThreshold,
                    buildUpdatedLfsSettings model (Some thresholdMb) None None
                )
            )
        )
    | SaveDiffIndexingLimitRequested limitMb ->
        model,
        Cmd.ofMsg (
            WriteRequested(
                SaveLfsSettings(
                    GitBusyOperation.SavingDiffIndexingLimit,
                    buildUpdatedLfsSettings model None None (Some limitMb)
                )
            )
        )
    | FetchRequested -> model, Cmd.ofMsg (WriteRequested Fetch)
    | PushRequested -> model, Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
    | CancelCurrentOperationRequested ->
        match model.CurrentOperation with
        | None -> model, Cmd.none
        | Some key ->
            let cmd =
                Cmd.OfPromise.either
                    deps.cancelOperation
                    key
                    (fun result -> CancelCurrentOperationCompleted(model.ArcSessionId, key, result))
                    (fun err -> CancelCurrentOperationCompleted(model.ArcSessionId, key, Error(string err)))

            model, cmd
    | CancelCurrentOperationCompleted(sessionId, key, _) when
        sessionId <> model.ArcSessionId
        || (model.CurrentOperation
            |> Option.map (fun current -> operationRootId current.OperationId))
           <> Some(operationRootId key.OperationId)
        ->
        model, Cmd.none
    | CancelCurrentOperationCompleted(_, _, Error message) ->
        {
            model with
                ErrorNotice = Some message
                WarningNotice = None
        },
        reportErrorCmd deps "Could not cancel Git operation" message
    | CancelCurrentOperationCompleted(_, _, Ok true) -> model, Cmd.none
    | CancelCurrentOperationCompleted(_, _, Ok false) -> model, Cmd.none
    | UpdateFromOnlineRequested when model.CurrentArcPath.IsNone || model.BusyOperation.IsSome -> model, Cmd.none
    | UpdateFromOnlineRequested -> model, Cmd.ofMsg (WriteRequested(Pull GitUpdateAcceptance.RequirePreview))
    | CloneRequested(cloneRequest, reply) -> model, Cmd.ofMsg (WriteRequested(Clone(cloneRequest, reply)))
    | PrimarySaveSelectionRequested selection ->
        model, Cmd.ofMsg (WriteRequested(PrimarySave(prepareCommitSelection selection)))
    | PrimarySaveAllRequested message -> model, Cmd.ofMsg (WriteRequested(PrimarySave(prepareCommitAll model message)))
    | CommitSelectionRequested selection ->
        model, Cmd.ofMsg (WriteRequested(CommitSelection(prepareCommitSelection selection)))
    | CommitAllRequested message -> model, Cmd.ofMsg (WriteRequested(CommitAll(prepareCommitAll model message)))
    | DiscardSelectionRequested _ when model.PendingConfirmation.IsSome -> model, Cmd.none
    | DiscardSelectionRequested paths ->
        let message =
            if paths.Length = 1 then
                $"Discard your changes to '{paths[0]}'? This can't be undone."
            else
                let shownPaths = paths |> Array.truncate 5 |> String.concat "\n"
                let moreCount = paths.Length - min paths.Length 5
                let moreMessage = if moreCount > 0 then $"\nand {moreCount} more" else ""
                $"Discard your changes to {paths.Length} files? This can't be undone.\n{shownPaths}{moreMessage}"

        let dialog = {
            Title = "Discard changes?"
            Message = message
            ConfirmLabel = "Discard"
            CancelLabel = "Keep changes"
        }

        {
            model with
                PendingConfirmation = Some dialog
                PendingRemoteAction = GitPendingRemoteAction.DiscardSelection paths
        },
        Cmd.none
    | ConfirmPendingRemoteActionRequested ->
        match model.PendingRemoteAction with
        | GitPendingRemoteAction.DiscardSelection paths ->
            {
                model with
                    PendingConfirmation = None
                    PendingRemoteAction = GitPendingRemoteAction.None
            },
            Cmd.ofMsg (WriteRequested(DiscardSelection paths))
        | GitPendingRemoteAction.UpdateFromOnline acceptance ->
            {
                model with
                    PendingConfirmation = None
                    PendingRemoteAction = GitPendingRemoteAction.None
            },
            Cmd.ofMsg (WriteRequested(Pull acceptance))
        | GitPendingRemoteAction.PublishAfterUpdate acceptance ->
            {
                model with
                    PendingConfirmation = None
                    PendingRemoteAction = GitPendingRemoteAction.None
                    PendingPostMergePush = true
            },
            Cmd.ofMsg (WriteRequested(Push acceptance))
        | GitPendingRemoteAction.FinalizeMerge ->
            {
                model with
                    PendingConfirmation = None
                    PendingRemoteAction = GitPendingRemoteAction.None
            },
            Cmd.ofMsg (WriteRequested FinalizeMerge)
        | GitPendingRemoteAction.Recover ->
            let followUp =
                match model.PendingRecovery with
                | Some(GitPendingRecovery.RestoreInterruptedPaths _) -> Cmd.ofMsg RestoreInterruptedPathsRequested
                | Some(GitPendingRecovery.RetryMaterialization _) -> Cmd.ofMsg RetryMaterializationRequested
                | Some(GitPendingRecovery.RetryPublish _) ->
                    Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
                | Some(GitPendingRecovery.ClearStaleLock _) -> Cmd.ofMsg ClearStaleLockRequested
                | Some(GitPendingRecovery.ClearCloneTarget _)
                | None -> Cmd.ofMsg DismissRecoveryRequested

            {
                model with
                    PendingConfirmation = None
                    PendingRemoteAction = GitPendingRemoteAction.None
                    PendingRecovery =
                        match model.PendingRecovery with
                        | Some(GitPendingRecovery.RetryPublish _) -> None
                        | _ -> model.PendingRecovery
            },
            followUp
        | GitPendingRemoteAction.None -> model, Cmd.none
    | CancelPendingRemoteActionRequested ->
        let cmd =
            match model.PendingRemoteAction with
            | GitPendingRemoteAction.FinalizeMerge -> Cmd.ofMsg (WriteRequested AbandonMerge)
            | GitPendingRemoteAction.Recover -> Cmd.ofMsg DismissRecoveryRequested
            | _ -> Cmd.none

        {
            model with
                PendingConfirmation = None
                PendingRemoteAction = GitPendingRemoteAction.None
                PendingPostMergePush =
                    match model.PendingRemoteAction with
                    | GitPendingRemoteAction.Recover -> model.PendingPostMergePush
                    | _ -> false
        },
        cmd
    | CancelPublishRenameRequested ->
        {
            clearBusy model with
                PendingPublishRename = None
                // The next upload asks for the owner again.
                PublishOwners = None
                PublishOwnerChoice = None
                ErrorNotice = None
                WarningNotice = None
                PendingRefreshWarningNotice = Some pendingPrimarySaveWarning
        },
        Cmd.ofMsg RefreshRequested
    | SubmitPublishRenameRequested submit ->
        match model.PendingPublishRename with
        | None -> model, Cmd.none
        | Some prompt ->
            let normalizedName =
                submit.Name
                |> Option.ofObj
                |> Option.map _.Trim()
                |> Option.defaultValue String.Empty

            if String.IsNullOrWhiteSpace normalizedName then
                {
                    model with
                        ErrorNotice = Some "ARC folder name must not be empty."
                },
                reportErrorCmd deps "Could not rename ARC" "ARC folder name must not be empty."
            else
                // The prompt is the list the user saw, so the choice is looked up there.
                let choice =
                    prompt.Owners
                    |> Array.tryFind (fun owner -> owner.NamespaceId = submit.NamespaceId)
                    |> Option.defaultValue personalPublishOwner

                let selection = {
                    Owners =
                        if prompt.Owners.Length = 0 then
                            None
                        else
                            Some prompt.Owners
                    Choice = Some choice
                }

                let keepsName =
                    model.CurrentArcPath
                    |> Option.exists (fun currentPath -> keepsArcFolderName currentPath normalizedName)

                if keepsName then
                    {
                        model with
                            PendingPublishRename = None
                            PublishOwners = selection.Owners
                            PublishOwnerChoice = selection.Choice
                            ErrorNotice = None
                    },
                    Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
                else
                    let nextModel =
                        {
                            model with
                                PublishOwnerChoice = Some choice
                        }
                        |> withBusyOperation (Some GitBusyOperation.RenamingRepository)
                        |> fun state -> { state with ErrorNotice = None }

                    let cmd =
                        Cmd.OfPromise.either
                            deps.renameOpenArcRoot
                            normalizedName
                            (fun result -> PublishRenameCompleted(model.ArcSessionId, result, selection))
                            (fun err -> PublishRenameCompleted(model.ArcSessionId, Error(string err), selection))

                    nextModel, cmd
    | PublishRenameCompleted(sessionId, Ok renamedPath, selection) when
        sessionId = model.ArcSessionId
        && not (
            model.CurrentArcPath
            |> Option.exists (fun currentPath -> Swate.Components.Shared.PathHelpers.pathsEqual currentPath renamedPath)
        )
        ->
        {
            clearBusy model with
                PendingPublishRename = None
                PendingPublishForPath = Some renamedPath
                PublishOwners = selection.Owners
                PublishOwnerChoice = selection.Choice
                ErrorNotice = None
        },
        Cmd.none
    | PublishRenameCompleted(sessionId, Ok renamedPath, selection) when
        sessionId <> model.ArcSessionId
        && (model.CurrentArcPath
            |> Option.exists (fun currentPath -> Swate.Components.Shared.PathHelpers.pathsEqual currentPath renamedPath))
        ->
        // The path change already reset the model. Its refresh may still be running or
        // may have finished before this reply, so a refresh is asked for either way and
        // the publish runs when it has loaded the workspace token.
        {
            model with
                PendingPublishRename = None
                PublishOwners = selection.Owners
                PublishOwnerChoice = selection.Choice
                ErrorNotice = None
                PendingPublishAfterRefresh = true
        },
        Cmd.ofMsg RefreshRequested
    | PublishRenameCompleted(sessionId, _, _) when sessionId <> model.ArcSessionId -> model, Cmd.none
    | PublishRenameCompleted(_, Error message, _) ->
        {
            clearBusy model with
                ErrorNotice = Some message
        },
        reportErrorCmd deps "Could not rename ARC" message
    | PublishRenameCompleted(_, Ok _, selection) ->
        {
            clearBusy model with
                PendingPublishRename = None
                PublishOwners = selection.Owners
                PublishOwnerChoice = selection.Choice
                ErrorNotice = None
        },
        Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
    | CreateBranchRequested branchRequest -> model, Cmd.ofMsg (WriteRequested(CreateBranch branchRequest))
    | SwitchBranchRequested _ when model.CurrentArcPath.IsNone || model.BusyOperation.IsSome -> model, Cmd.none
    | SwitchBranchRequested branchName ->
        let normalizedBranchName = branchName.Trim()

        if String.IsNullOrWhiteSpace normalizedBranchName then
            model, Cmd.none
        else
            match requireWorkspaceVersion model, providerRefOf model normalizedBranchName with
            | Error message, _ ->
                {
                    model with
                        ErrorNotice = Some message
                },
                reportErrorCmd deps "Could not switch branch" message
            | Ok _, None ->
                let message =
                    $"Branch '{normalizedBranchName}' is not known. Refresh and try again."

                {
                    model with
                        ErrorNotice = Some message
                },
                reportErrorCmd deps "Could not switch branch" message
            | Ok version, Some providerRef ->
                let operationId = deps.newOperationId ()

                let nextModel =
                    model
                    |> withBusyOperation (Some GitBusyOperation.SwitchingBranch)
                    |> fun state -> {
                        state with
                            ErrorNotice = None
                            WarningNotice = None
                            CurrentOperation = Some { OperationId = operationId }
                    }

                let cmd =
                    Cmd.OfPromise.either
                        deps.preflightSwitchRef
                        {
                            OperationId = operationId
                            TargetRef = providerRef
                            ExpectedWorkspaceVersion = version
                        }
                        (fun result -> SwitchBranchPreflightCompleted(model.ArcSessionId, normalizedBranchName, result))
                        (fun err ->
                            SwitchBranchPreflightCompleted(model.ArcSessionId, normalizedBranchName, Error(string err))
                        )

                nextModel, cmd
    | SwitchBranchPreflightCompleted(sessionId, _, _) when sessionId <> model.ArcSessionId -> model, Cmd.none
    | SwitchBranchPreflightCompleted(_, _, Ok(OperationResultDto.PartiallySucceeded(_, failure))) ->
        let message = failureMessage failure

        {
            clearBusy model with
                ErrorNotice = Some message
                WarningNotice = None
        },
        reportErrorCmd deps "Could not switch branch" message
    | SwitchBranchPreflightCompleted(_, refName, Ok(OperationResultDto.Succeeded outcome)) when outcome.Value.IsSafe ->
        clearBusy model, Cmd.ofMsg (WriteRequested(SwitchBranch refName))
    | SwitchBranchPreflightCompleted(_, _, Ok(OperationResultDto.Succeeded outcome)) ->
        let paths = String.Join(", ", outcome.Value.PathsAtRisk)

        let message =
            if outcome.Value.PathsAtRisk.Length > 0 then
                $"Switching branches would overwrite local changes in: {paths}. Save or discard those files, then switch."
            else
                "Switching branches would overwrite local changes. Save or discard those files, then switch."

        {
            clearBusy model with
                ErrorNotice = Some message
                WarningNotice = None
        },
        reportErrorCmd deps "Could not switch branch" message
    | SwitchBranchPreflightCompleted(_, _, Ok(OperationResultDto.Failed failure)) when isCanceled failure ->
        {
            clearBusy model with
                ErrorNotice = None
                WarningNotice = Some "Git operation cancelled."
        },
        Cmd.none
    | SwitchBranchPreflightCompleted(_, _, Ok(OperationResultDto.Failed failure)) ->
        match routeFailure None failure with
        | RoutedFailure.StaleWorkspace(message, _) ->
            {
                clearBusy model with
                    ErrorNotice = Some message
                    WarningNotice = None
            },
            Cmd.batch [
                reportErrorCmd deps "Could not switch branch" message
                Cmd.ofMsg RefreshRequested
            ]
        | routed ->
            let message = routedFailureMessage failure routed

            {
                clearBusy model with
                    ErrorNotice = Some message
                    WarningNotice = None
            },
            reportErrorCmd deps "Could not switch branch" message
    | SwitchBranchPreflightCompleted(_, _, Error message) ->
        {
            clearBusy model with
                ErrorNotice = Some message
                WarningNotice = None
        },
        reportErrorCmd deps "Could not switch branch" message
    | PruneLfsCacheRequested ->
        let message =
            "This cleans hidden Git LFS cache files for the current ARC. Files that are still needed can be downloaded again from the remote. Continue?"

        if deps.confirmLfsPrune message then
            model, Cmd.ofMsg (WriteRequested PruneLfsCache)
        else
            model, Cmd.none
    | DedupLfsStorageRequested -> model, Cmd.ofMsg (WriteRequested DedupLfsStorage)
    | ClearStaleLockRequested -> { model with PendingRecovery = None }, Cmd.ofMsg (WriteRequested ClearStaleLock)
    | RestoreInterruptedPathsRequested ->
        match model.PendingRecovery with
        | Some(GitPendingRecovery.RestoreInterruptedPaths interrupted) when interrupted.AffectedPaths.Length > 0 ->
            { model with PendingRecovery = None },
            Cmd.ofMsg (WriteRequested(RestoreInterruptedPaths interrupted.AffectedPaths))
        | _ -> { model with PendingRecovery = None }, Cmd.none
    | RetryMaterializationRequested ->
        { model with PendingRecovery = None }, Cmd.ofMsg (WriteRequested RetryMaterialization)
    | DismissRecoveryRequested ->
        // The offer is gone, so the sidebar catches up with what the canceled step left.
        let followUp =
            if shouldRunPostMergePush model then
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            elif model.CurrentArcPath.IsSome then
                Cmd.ofMsg RefreshRequested
            else
                Cmd.none

        {
            model with
                PendingRecovery = None
                RetryMaterializationAfterLockClear = None
                RefreshPending = false
                PendingPostMergePush =
                    if shouldRunPostMergePush model then
                        false
                    else
                        model.PendingPostMergePush
        },
        followUp
    | WriteRequested writeRequest when requiresArcForWriteRequest writeRequest && model.CurrentArcPath.IsNone ->
        model, Cmd.none
    | WriteRequested writeRequest when model.BusyOperation.IsSome ->
        // One mutation at a time. The sidebar disables its buttons while busy, so this
        // only catches a request that raced the completion.
        model, resolveCloneReplyCmd writeRequest (Error "Another Git operation is still running.")
    | WriteRequested writeRequest ->
        let writeRequestId = model.WriteRequestId + 1
        let operationId = deps.newOperationId ()

        let nextModel =
            model
            |> withBusyOperation (Some(busyOperationForWriteRequest writeRequest))
            |> fun state -> {
                state with
                    ErrorNotice = None
                    WarningNotice = None
                    WriteRequestId = writeRequestId
                    CurrentOperation = Some { OperationId = operationId }
            }

        nextModel, writeCmd deps model writeRequest model.ArcSessionId writeRequestId operationId
    | WritePhaseChanged(sessionId, writeRequestId, _) when
        sessionId <> model.ArcSessionId || writeRequestId <> model.WriteRequestId
        ->
        model, Cmd.none
    | WritePhaseChanged(_, _, phase) ->
        {
            model with
                BusyOperation = Some phase
                BusyNotice = busyNoticeFromOperation phase
                // The bar starts over with each phase. The steps and the log cover the whole write.
                CurrentProgress = None
                CurrentProgressPhase = None
        },
        Cmd.none
    | WriteCompleted(sessionId, writeRequestId, writeRequest, result) when
        sessionId <> model.ArcSessionId || writeRequestId <> model.WriteRequestId
        ->
        model, resolveStaleWriteCompletedCmd writeRequest result
    | WriteCompleted(_, _, writeRequest, Ok(StaleWorkspaceVersion(message, _))) ->
        // Reached when the retry after a refresh was stale again, or when the write is
        // one that is never replayed. The refresh shows the state the user has to review.
        let nextModel = {
            writeErrorModel message model with
                PendingPostMergePush = false
                RefreshPending = false
        }

        nextModel,
        Cmd.batch [
            resolveCloneReplyCmd writeRequest (Error message)
            reportWriteErrorCmd deps writeRequest message
            Cmd.ofMsg RefreshRequested
        ]
    | WriteCompleted(_, _, writeRequest, Ok(OperationCancelled message)) ->
        let nextModel = {
            clearBusy model with
                ErrorNotice = None
                WarningNotice = Some "Git operation cancelled."
                PendingPostMergePush = false
        }

        nextModel, resolveCloneReplyCmd writeRequest (Error message)
    | WriteCompleted(_, _, writeRequest, Error message) ->
        let nextModel = {
            writeErrorModel message model with
                PendingPostMergePush = false
        }

        nextModel,
        Cmd.batch [
            resolveCloneReplyCmd writeRequest (Error message)
            reportWriteErrorCmd deps writeRequest message
        ]
    | WriteCompleted(_, _, _, Ok(RequiresRemoteProjectRename(message, owners, clearOwnerChoice))) ->
        let ownedModel = {
            model with
                PublishOwners = owners |> Option.orElse model.PublishOwners
                PublishOwnerChoice = if clearOwnerChoice then None else model.PublishOwnerChoice
        }

        let nextModel = {
            clearBusy ownedModel with
                ErrorNotice = None
                PendingPublishRename = Some(publishRenamePrompt message ownedModel)
        }

        nextModel, Cmd.none
    | WriteCompleted(_, _, writeRequest, Ok(ProvisioningIncomplete(provisioned, message))) ->
        // The remote project exists. The next publish resumes with binding or publishing.
        // The refresh would clear the notice, so it is carried across as a warning.
        let nextModel = {
            clearBusy model with
                ErrorNotice = Some message
                PendingRefreshWarningNotice = Some message
                ProvisionedRemote = Some provisioned
        }

        nextModel,
        Cmd.batch [
            Cmd.ofMsg RefreshRequested
            reportWriteErrorCmd deps writeRequest message
        ]
    | WriteCompleted(_, _, writeRequest, Ok(RequiresRecovery(recovery, message))) ->
        // The recovery offer stays open. A refresh requested meanwhile runs after the offer is resolved.
        // A download retry that a held lock stopped is remembered like the hydration of a pull, so the
        // download is offered again once the lock is gone. The library message of the retry failure says
        // the lock is still held, so the offer gets a fixed sentence.
        let nextModel = {
            clearBusy model with
                ErrorNotice = None
                WarningNotice = Some message
                RetryMaterializationAfterLockClear =
                    match writeRequest, recovery with
                    | RetryMaterialization, GitPendingRecovery.ClearStaleLock _ -> Some retryLockStoppedMessage
                    | _ -> model.RetryMaterializationAfterLockClear
                PendingRecovery = Some recovery
                PendingConfirmation = Some(recoveryDialog recovery)
                PendingRemoteAction = GitPendingRemoteAction.Recover
        }

        nextModel, resolveCloneReplyCmd writeRequest (Error message)
    | WriteCompleted(sessionId, _, writeRequest, Ok(RequiresDependencyInstall(componentName, promptMessage))) ->
        let nextModel = {
            model with
                InstallRetryState =
                    GitInstallRetryState.PromptingForInstall(promptMessage, busyOperationForWriteRequest writeRequest)
        }

        let cmd =
            Cmd.OfFunc.either
                deps.confirmInstall
                promptMessage
                (fun shouldInstall -> WriteInstallPromptAnswered(sessionId, writeRequest, componentName, shouldInstall))
                (fun _ -> WriteInstallPromptAnswered(sessionId, writeRequest, componentName, false))

        nextModel, cmd
    | WriteInstallPromptAnswered(sessionId, writeRequest, _, _) when sessionId <> model.ArcSessionId ->
        model, resolveCloneReplyCmd writeRequest (Error staleArcSessionMessage)
    | WriteInstallPromptAnswered(_, writeRequest, componentName, false) ->
        let message =
            $"The version control dependency '{componentName}' is required to continue."

        let nextModel =
            model
            |> writeErrorModel message
            |> fun state -> {
                state with
                    InstallRetryState = GitInstallRetryState.Idle
            }

        nextModel,
        Cmd.batch [
            resolveCloneReplyCmd writeRequest (Error message)
            reportErrorCmd deps "Version control dependency required" message
        ]
    | WriteInstallPromptAnswered(sessionId, writeRequest, componentName, true) ->
        let busyOperation = busyOperationForWriteRequest writeRequest

        let installing = GitBusyOperation.InstallingDependency componentName

        // The install is the operation that runs now, so the cancel button targets it
        // instead of the write that asked for the dependency.
        let installOperationId = deps.newOperationId ()

        let nextModel = {
            model with
                InstallRetryState = GitInstallRetryState.InstallingForRetry busyOperation
                BusyOperation = Some installing
                BusyNotice = busyNoticeFromOperation installing
                CurrentOperation = Some { OperationId = installOperationId }
        }

        let cmd =
            Cmd.OfPromise.either
                deps.installDependency
                {
                    OperationId = installOperationId
                    Component = componentName
                }
                (fun installResult -> WriteInstallCompleted(sessionId, writeRequest, installResult))
                (fun err -> WriteInstallCompleted(sessionId, writeRequest, Error(string err)))

        nextModel, cmd
    | WriteInstallCompleted(sessionId, writeRequest, _) when sessionId <> model.ArcSessionId ->
        model, resolveCloneReplyCmd writeRequest (Error staleArcSessionMessage)
    | WriteInstallCompleted(_, writeRequest, Error message) ->
        let nextModel =
            model
            |> writeErrorModel message
            |> fun state -> {
                state with
                    InstallRetryState = GitInstallRetryState.Idle
            }

        nextModel,
        Cmd.batch [
            resolveCloneReplyCmd writeRequest (Error message)
            reportErrorCmd deps "Could not install the dependency" message
        ]
    | WriteInstallCompleted(_, writeRequest, Ok(OperationResultDto.Failed failure)) ->
        let message = failureMessage failure

        let nextModel =
            model
            |> writeErrorModel message
            |> fun state -> {
                state with
                    InstallRetryState = GitInstallRetryState.Idle
            }

        nextModel,
        Cmd.batch [
            resolveCloneReplyCmd writeRequest (Error message)
            reportErrorCmd deps "Could not install the dependency" message
        ]
    | WriteInstallCompleted(sessionId, writeRequest, Ok _) ->
        // The dependency is configured. The failed stage runs again with the same
        // workspace token, because installing a dependency does not change the workspace.
        let busyOperation = busyOperationForWriteRequest writeRequest
        let operationId = deps.newOperationId ()

        let nextModel = {
            model with
                InstallRetryState = GitInstallRetryState.Idle
                BusyOperation = Some busyOperation
                BusyNotice = busyNoticeFromOperation busyOperation
                CurrentOperation = Some { OperationId = operationId }
        }

        nextModel, writeCmd deps model writeRequest sessionId model.WriteRequestId operationId
    | WriteCompleted(_,
                     _,
                     writeRequest,
                     Ok(CompletedWithPendingRemoteConfirmation(success, dialog, pendingRemoteAction))) ->
        let baseModel, pageChange, warningMessage, _, recoveryMessage, _ =
            applyWriteSuccessModel model success

        // The confirmation dialog owns the pending action, so a recovery offer cannot be shown at the same time.
        // Surface its message as a warning until the dialog is resolved.
        let nextModel = {
            clearBusy baseModel with
                ErrorNotice = None
                WarningNotice = warningMessage |> Option.orElse recoveryMessage
                PendingConfirmation = Some dialog
                PendingRemoteAction = pendingRemoteAction
                PendingRecovery = None
                // PrimarySave with FinalizeMerge is the "All conflicts resolved" question after
                // a save whose update opened a conflict session. The publish resumes after finalization.
                // A save acceptance arrives as PublishAfterUpdate, so it falls through to `_ -> false`
                // because its confirm branch sets the flag. Merge previews no longer exist.
                PendingPostMergePush =
                    match writeRequest, pendingRemoteAction with
                    | PrimarySave _, GitPendingRemoteAction.FinalizeMerge -> true
                    | _, GitPendingRemoteAction.FinalizeMerge -> model.PendingPostMergePush
                    | _ -> false
        }

        nextModel, applyPageChangeCmd setPageState pageChange
    | WriteCompleted(_, _, writeRequest, Ok(CompletedWithPendingRemoteFailure(success, message))) ->
        let baseModel, pageChange, warningMessage, recovery, _, _ =
            applyWriteSuccessModel model success

        let nextModel = {
            clearBusy baseModel with
                ErrorNotice = Some message
                WarningNotice = warningMessage
                PendingConfirmation = None
                PendingRemoteAction = GitPendingRemoteAction.None
                PendingPostMergePush = false
                PendingRecovery = recovery
        }

        let report =
            match writeRequest with
            | PrimarySave _ when isLocalPrimarySaveFailure success ->
                reportErrorCmd deps "Changes saved, but Git's index needs attention" message
            | PrimarySave _ -> reportErrorCmd deps "Could not push saved changes" message
            | _ -> reportWriteErrorCmd deps writeRequest message

        nextModel,
        Cmd.batch [
            applyPageChangeCmd setPageState pageChange
            resolveCloneReplyCmd writeRequest (Error message)
            report
        ]
    | WriteCompleted(_, _, writeRequest, Ok(FinalizedButRefreshFailed message)) ->
        let nextModel = {
            writeErrorModel message model with
                PendingMaterializations = []
                PendingMaterializationSessionId = None
                PendingPostMergePush = false
        }

        nextModel, reportWriteErrorCmd deps writeRequest message
    | WriteCompleted(_, _, writeRequest, Ok(Completed success)) ->
        let baseModel, pageChange, warningMessage, recovery, partial, published =
            applyWriteSuccessModel model success

        let nextModel = {
            clearBusy baseModel with
                ErrorNotice = None
                WarningNotice = warningMessage
                PendingPublishRename = None
                PendingRecovery = recovery
                RetryMaterializationAfterLockClear =
                    match success with
                    | UnitSuccess completed -> lockBlockedDownloadMessage completed.Partial
                    | CloneSuccess _ -> None
                PendingConfirmation = recovery |> Option.map recoveryDialog
                PendingRemoteAction =
                    match recovery with
                    | Some _ -> GitPendingRemoteAction.Recover
                    | None -> GitPendingRemoteAction.None
                PendingMaterializations =
                    match writeRequest with
                    | FinalizeMerge -> []
                    | _ -> baseModel.PendingMaterializations
                PendingMaterializationSessionId =
                    match writeRequest with
                    | FinalizeMerge -> None
                    | _ -> baseModel.PendingMaterializationSessionId
                ProvisionedRemote =
                    match writeRequest with
                    | Push _
                    | PrimarySave _ -> None
                    | _ -> model.ProvisionedRemote
        }

        let followUp =
            match writeRequest with
            // A cleared lock revealed an open merge: the abandon path runs it.
            | ClearStaleLock when nextModel.ActiveConflict.IsSome -> Cmd.ofMsg (WriteRequested AbandonMerge)
            // No download offer follows the lock removal, so the publish that waited for the download resumes here.
            | ClearStaleLock when
                model.RetryMaterializationAfterLockClear.IsSome
                && lockRemovalFoundNoMissingObject success
                && shouldRunPostMergePush nextModel
                ->
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            | FinalizeMerge when shouldRunPostMergePush nextModel ->
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            | RetryMaterialization when shouldRunPostMergePush nextModel ->
                Cmd.ofMsg (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
            | _ -> Cmd.none

        let nextModel =
            match writeRequest with
            | FinalizeMerge when shouldRunPostMergePush nextModel -> {
                nextModel with
                    PendingPostMergePush = false
              }
            | RetryMaterialization when shouldRunPostMergePush nextModel -> {
                nextModel with
                    PendingPostMergePush = false
              }
            | ClearStaleLock when
                model.RetryMaterializationAfterLockClear.IsSome
                && lockRemovalFoundNoMissingObject success
                && shouldRunPostMergePush nextModel
                  ->
                  {
                      nextModel with
                          PendingPostMergePush = false
                  }
            | AbandonMerge -> {
                nextModel with
                    PendingPostMergePush = false
                    PendingMaterializations = []
                    PendingMaterializationSessionId = None
              }
            // Only a materialization recovery leaves the publish undone with nothing else
            // offering it. The publish resumes after that recovery. The retry_publish partial
            // is its own offer, and an unmapped partial has no path that could resume.
            // A download that an index lock stopped reaches the same retry once the lock is gone.
            | (PrimarySave _ | Push _) when published = Some false && partial.IsSome ->
                let resumesAfterMaterialization =
                    match recovery with
                    | Some(GitPendingRecovery.RetryMaterialization _) -> true
                    | Some(GitPendingRecovery.ClearStaleLock _) -> nextModel.RetryMaterializationAfterLockClear.IsSome
                    | _ -> false

                {
                    nextModel with
                        PendingPostMergePush = resumesAfterMaterialization
                }
            // The update inside the save opened a conflict session. The publish resumes
            // once the merge is finalized.
            | PrimarySave _ when nextModel.ActiveConflict.IsSome -> {
                nextModel with
                    PendingPostMergePush = true
              }
            // The push finished without a conflict session, so no publish is pending.
            | Push _ when nextModel.ActiveConflict.IsNone -> {
                nextModel with
                    PendingPostMergePush = false
              }
            | _ -> nextModel

        nextModel,
        Cmd.batch [
            applyPageChangeCmd setPageState pageChange
            resolveCloneReplyCmd writeRequest (Ok success)
            followUp
        ]

/// A removed index lock turns the stopped download into the download offer. Any other end of the
/// lock removal drops it, because the next pull offers the download again. The offer is dropped as well when
/// the restage after the lock removal found no large object missing, because Download now would have nothing to fetch.
let private offerDownloadAfterLockRemoval (msg: Msg) (model: GitState) (next: GitState) =
    match msg with
    | WriteCompleted(sessionId, writeRequestId, ClearStaleLock, result) when
        sessionId = model.ArcSessionId && writeRequestId = model.WriteRequestId
        ->
        match result, model.RetryMaterializationAfterLockClear with
        | Ok(Completed success), Some message when not (lockRemovalFoundNoMissingObject success) ->
            let recovery = GitPendingRecovery.RetryMaterialization message

            {
                next with
                    RetryMaterializationAfterLockClear = None
                    PendingRecovery = Some recovery
                    PendingConfirmation = Some(recoveryDialog recovery)
                    PendingRemoteAction = GitPendingRemoteAction.Recover
            }
        | _ -> {
            next with
                RetryMaterializationAfterLockClear = None
          }
    | _ -> next

/// The wrapper holds a refresh until the write finishes and any open confirmation is resolved.
/// It also waits for the pending recovery to be handled.
let update
    (deps: GitDependencies)
    (setPageState: PageState option -> unit)
    (msg: Msg)
    (model: GitState)
    : GitState * Cmd<Msg> =
    let next, cmd = updateCore deps setPageState msg model

    // A publish consumes the owner choice. A write that opens the upload dialog again keeps the
    // owners for the next try.
    // A completion of an outdated write is dropped by updateCore and leaves the owners alone.
    let next =
        match msg with
        | WriteCompleted(_, _, _, Ok(RequiresRemoteProjectRename _)) -> next
        | WriteCompleted(sessionId, writeRequestId, _, _) when
            sessionId = model.ArcSessionId && writeRequestId = model.WriteRequestId
              ->
              {
                  next with
                      PublishOwners = None
                      PublishOwnerChoice = None
              }
        | _ -> next

    let next = offerDownloadAfterLockRemoval msg model next

    // Every way of leaving a diff page ends here: another selection, another page, a closed
    // page or a reset for another ARC.
    let cmd =
        match model.DiffPage with
        | Some previous when
            next.DiffPage
            |> Option.forall (fun page -> page.Generation <> previous.Generation)
            ->
            Cmd.batch [ cmd; GitDiffPageLoader.closeCmd deps previous ]
        | _ -> cmd

    if
        next.RefreshPending
        && next.BusyOperation.IsNone
        && next.PendingConfirmation.IsNone
        && next.PendingRecovery.IsNone
    then
        let refreshMessage =
            if next.RefreshPendingSilently then
                RefreshRequestedSilently
            else
                RefreshRequested

        {
            next with
                RefreshPending = false
                RefreshPendingSilently = false
        },
        Cmd.batch [ cmd; Cmd.ofMsg refreshMessage ]
    else
        next, cmd

let subscribe (_model: GitState) : Sub<Msg> = [
    [ "versionControlProgress" ],
    fun dispatch ->
        let dispose =
            Renderer.IpcReceiver.subscribeProxyReceiver<IVersionControlRendererApi> {
                versionControlProgress = fun progress -> dispatch (SetCurrentProgress progress)
                versionControlOperationStarted = fun key -> dispatch (OperationStarted key)
                versionControlOutput = fun output -> dispatch (OperationOutputReceived output)
            }

        { new System.IDisposable with
            member _.Dispose() = dispose ()
        }
    [ "gitRepositoryInitialized" ],
    fun dispatch ->
        let dispose =
            Renderer.IpcReceiver.subscribeProxyReceiver<IGitRepositoryRendererApi> {
                gitRepositoryInitialized = fun arcPath -> dispatch (GitRepositoryInitialized arcPath)
            }

        { new System.IDisposable with
            member _.Dispose() = dispose ()
        }
    [ "arcFileWatcher" ],
    fun dispatch ->
        let dispose =
            Renderer.IpcReceiver.subscribeProxyReceiver<IArcFileWatcherApi> {
                IsLoadingChanges =
                    fun isLoading ->
                        if not isLoading then
                            dispatch ExternalChangesDetected
            }

        { new System.IDisposable with
            member _.Dispose() = dispose ()
        }
]
