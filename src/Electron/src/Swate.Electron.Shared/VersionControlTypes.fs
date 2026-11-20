/// Provider-neutral version control DTOs that cross the IPC boundary. They mirror the
/// library's abstractions with plain strings and serializable unions and carry no
/// provider-specific type. Every request names the operation it belongs to so the
/// renderer can cancel it before the first progress event arrives.
module Swate.Electron.Shared.VersionControlTypes

open Fable.Core

// Paged text diff. Every int64 of the library travels as a decimal string, because
// JSON has no integer above 2^53 and the renderer never computes with these values.
// These types come first so that a field name they share with an older DTO, such as
// Path or Preview, still resolves to the older DTO in code without a type annotation.

/// One of the two sources of a text diff.
[<StringEnum(CaseRules.None); RequireQualifiedAccess>]
type DiffSideDto =
    | Previous
    | Current

/// The side whose content turned out not to be text, with the evidence the library found.
type DiffContentBlockedDto = { Side: DiffSideDto; Evidence: string }

type DiffHandleDto = { Id: string; Version: string }

/// A zero-based range of source lines.
type LineRangeDto = { Start: string; Count: string }

[<StringEnum(CaseRules.None); RequireQualifiedAccess>]
type LineEndingDto =
    | NoEnding
    | LF
    | CRLF
    | CR

[<StringEnum(CaseRules.None); RequireQualifiedAccess>]
type HighlightKindDto =
    | UnchangedText
    | ChangedText

/// A UTF-16 span inside the text of a line slice.
type HighlightDto = {
    Start: int
    Length: int
    Kind: HighlightKindDto
}

/// A UTF-16 slice of one line. TotalUtf16 stays None until the line length is known.
type LineSliceDto = {
    OffsetUtf16: string
    TotalUtf16: string option
    Text: string
    Highlights: HighlightDto[]
}

type DiffLineDto = {
    Number: string
    Ending: LineEndingDto
    Slice: LineSliceDto
}

[<StringEnum(CaseRules.None); RequireQualifiedAccess>]
type DiffRowKindDto =
    | Context
    | Added
    | Removed
    | Replaced
    | EndingChanged

type DiffRowDto = {
    Id: string
    Kind: DiffRowKindDto
    Previous: DiffLineDto option
    Current: DiffLineDto option
}

[<RequireQualifiedAccess>]
type HunkBodyDto =
    | AlignedRows of rows: DiffRowDto[]
    | UnalignedSides of previous: DiffLineDto[] * current: DiffLineDto[]

type HunkFragmentDto = {
    HunkId: string
    PreviousRange: LineRangeDto
    CurrentRange: LineRangeDto
    StartsHunk: bool
    EndsHunk: bool
    Body: HunkBodyDto
}

/// A hidden range whose lines and endings are verified equal.
type EqualGapDto = {
    GapId: string
    PreviousRange: LineRangeDto
    CurrentRange: LineRangeDto
}

[<RequireQualifiedAccess>]
type DiffPartDto =
    | Hunk of fragment: HunkFragmentDto
    | HiddenEqual of gap: EqualGapDto
    | ExpandedContext of gapId: string * rows: DiffRowDto[]

type ScanProgressDto = {
    ValidatedBytes: string
    TotalBytes: string
    ScanComplete: bool
}

/// What follows the last character of a pending snippet.
[<StringEnum(CaseRules.None); RequireQualifiedAccess>]
type SnippetEndDto =
    | Truncated
    | MoreTextPending
    | LineEnd
    | EndOfFile

type PendingSnippetDto = {
    Line: string
    OffsetUtf16: string
    Text: string
    End: SnippetEndDto
}

[<RequireQualifiedAccess>]
type PendingSideDto =
    | NoActiveLine
    | Snippet of snippet: PendingSnippetDto
    | Exhausted of lineCount: string

type MismatchMarkerDto = {
    PreviousOffsetUtf16: string
    CurrentOffsetUtf16: string
}

/// Advisory text of the lines still being scanned. It never forms a diff row.
type PendingPreviewDto = {
    Previous: PendingSideDto
    Current: PendingSideDto
    Mismatch: MismatchMarkerDto option
}

type DiffPageDto = {
    PageId: string
    NextCursor: string option
    Parts: DiffPartDto[]
    Progress: ScanProgressDto
    OutputComplete: bool
    Pending: PendingPreviewDto option
}

type DiffSourceInfoDto = {
    Path: string
    Revision: string option
    IsAbsent: bool
    ByteLength: string
    LineCount: string option
    Encoding: string option
    EncodingWasChosen: bool
    HasBom: bool
}

/// The source infos of both sides of an opened diff.
type DiffSourceInfoPairDto = {
    Previous: DiffSourceInfoDto
    Current: DiffSourceInfoDto
}

/// An encoding the user can pick, with at most 2 KiB of text decoded with it.
type EncodingCandidateDto = { Encoding: string; Preview: string }

type PreparationTokenDto = { Id: string }

/// Why a diff keeps its data in memory. The indexing limit setting asks for it (1 to 63 MB), or
/// the temp drive had less free space than Swate keeps free and the library chose memory.
[<StringEnum(CaseRules.None); RequireQualifiedAccess>]
type MemoryCauseDto =
    | BySetting
    | ByLowSpace

/// Where an opened diff keeps its data. The budget is in bytes as a decimal string.
[<RequireQualifiedAccess>]
type DiffStorageDto =
    | OnDisk
    | InMemory of budgetBytes: string * cause: MemoryCauseDto

[<RequireQualifiedAccess>]
type DiffBlockerDto =
    | Binary of side: DiffSideDto * evidence: string
    | LocalContentUnavailable of side: DiffSideDto * objectId: string option
    | EncodingRequired of side: DiffSideDto * token: PreparationTokenDto * candidates: EncodingCandidateDto[]
    | NotRegularFile of side: DiffSideDto
    | ProviderUnsupported
    /// A committed side is larger than the share of the memory budget that holds it. The sizes are
    /// in bytes as decimal strings.
    | BlobTooLargeForMemory of side: DiffSideDto * blobBytes: string * limitBytes: string * cause: MemoryCauseDto

// A Scanning result carries no value yet. The renderer passes its continuation back with
// the next request of the same kind. Each payload type has its own resumable DTO.

[<RequireQualifiedAccess>]
type ResumablePageDto =
    | Ready of page: DiffPageDto
    | Scanning of progress: ScanProgressDto * continuation: string * pending: PendingPreviewDto option

[<RequireQualifiedAccess>]
type OpenDiffResultDto =
    | NotDiffable of blocker: DiffBlockerDto
    | Opened of
        handle: DiffHandleDto *
        previous: DiffSourceInfoDto *
        current: DiffSourceInfoDto *
        first: ResumablePageDto *
        storage: DiffStorageDto

[<RequireQualifiedAccess>]
type ResumableOpenDto =
    | Ready of result: OpenDiffResultDto
    | Scanning of progress: ScanProgressDto * continuation: string * pending: PendingPreviewDto option

[<RequireQualifiedAccess>]
type ResumablePartsDto =
    | Ready of parts: DiffPartDto[]
    | Scanning of progress: ScanProgressDto * continuation: string * pending: PendingPreviewDto option

[<RequireQualifiedAccess>]
type ResumableLineDto =
    | Ready of line: DiffLineDto
    | Scanning of progress: ScanProgressDto * continuation: string * pending: PendingPreviewDto option

/// Opens a diff of one path. PreparationTokenId and the encodings answer an earlier
/// EncodingRequired blocker, and Continuation resumes an earlier Scanning result.
type OpenTextDiffRequestDto = {
    OperationId: string
    Path: string
    PreviousPath: string option
    PreparationTokenId: string option
    PreviousEncoding: string option
    CurrentEncoding: string option
    ContextLines: int
    Continuation: string option
}

/// Cursor is a page's NextCursor or the continuation of a page Scanning result.
type ReadTextDiffPageRequestDto = {
    OperationId: string
    HandleId: string
    HandleVersion: string
    Cursor: string
    /// The host reads ahead of the user. The library refuses such a read earlier than a user read
    /// once a memory diff holds most of its budget.
    Background: bool
}

type ReplayTextDiffPageRequestDto = {
    OperationId: string
    HandleId: string
    HandleVersion: string
    PageId: string
}

/// The library clamps Count to 100 lines.
type ExpandTextDiffRequestDto = {
    OperationId: string
    HandleId: string
    HandleVersion: string
    GapId: string
    FromStart: bool
    Count: int
    Continuation: string option
}

/// Line and OffsetUtf16 are non-negative decimal integers. The library clamps MaxUtf16 to 8,192.
type ReadTextDiffLineRequestDto = {
    OperationId: string
    HandleId: string
    HandleVersion: string
    Side: DiffSideDto
    Line: string
    OffsetUtf16: string
    MaxUtf16: int
    Continuation: string option
}

/// Names an open diff for getTextDiffSourceInfo and closeTextDiff.
type TextDiffHandleRequestDto = {
    OperationId: string
    HandleId: string
    HandleVersion: string
}

[<StringEnum(CaseRules.None)>]
type FailureCategoryDto =
    | Validation
    | NotFound
    | Concurrency
    | Authentication
    | Authorization
    | DependencyMissing
    | Network
    | Timeout
    | Canceled
    | Conflict
    | Unsupported
    | ProviderError

type RecoveryActionDto = {
    Code: string
    Instructions: string option
}

type RevisionEvidenceDto = { Label: string; Revision: string }

type OperationFailureDto = {
    Category: FailureCategoryDto
    Code: string
    Message: string
    StateChanged: bool
    Retryable: bool
    AffectedPaths: string[]
    RecoveryAction: RecoveryActionDto option
    Details: string[]
    RevisionEvidence: RevisionEvidenceDto[]
    /// Set on a diff_content_not_text or diff_encoding_mismatch failure of a text diff call.
    DiffDetail: DiffContentBlockedDto option
}

type OperationWarningDto = { Code: string; Message: string }

[<RequireQualifiedAccess>]
type OperationEffectDto =
    | Performed
    | NoOp of reason: string option

[<StringEnum(CaseRules.None)>]
type PublicationStateDto =
    | NotApplicable
    | LocalOnly
    | Published

type OperationOutcomeDto<'T> = {
    Value: 'T
    Effect: OperationEffectDto
    Warnings: OperationWarningDto[]
    AffectedPaths: string[]
    ResultingRevision: string option
    ResultingWorkspaceVersion: string option
    Publication: PublicationStateDto
}

[<RequireQualifiedAccess>]
type OperationResultDto<'T> =
    | Succeeded of OperationOutcomeDto<'T>
    | PartiallySucceeded of OperationOutcomeDto<'T> * OperationFailureDto
    | Failed of OperationFailureDto

type VersionControlProgressDto = {
    OperationId: string
    PhaseCode: string
    Item: string option
    Completed: float option
    Total: float option
    DisplayMessage: string option
}

/// One line of the raw output of a command that an operation runs. Kind is "command", "stdout", "stderr" or
/// "notice". Text is redacted. A partial entry is a snapshot of a line that is still being rewritten (a progress
/// meter). The next entry with the same CommandId and Kind replaces it.
type VersionControlOutputEntryDto = {
    CommandId: string
    Kind: string
    Text: string
    IsPartial: bool
}

/// A batch of output entries of the operation with this id.
type VersionControlOutputDto = {
    OperationId: string
    Entries: VersionControlOutputEntryDto[]
}

/// Codes the library and the host report. The renderer routes on some of them,
/// and tests build failures with others.
/// Each group below names its producer.
module VersionControlCodes =

    // Produced by the library (failure codes of its providers).

    [<Literal>]
    let PublishTargetMissing = "publish_target_missing"

    [<Literal>]
    let TargetUnreachable = "target_unreachable"

    [<Literal>]
    let NetworkFailure = "network_failure"

    [<Literal>]
    let PreconditionFailed = "precondition_failed"

    [<Literal>]
    let ConflictsDetected = "conflicts_detected"

    [<Literal>]
    let ConflictSessionActive = "conflict_session_active"

    [<Literal>]
    let UpdateWouldOverwriteLocalChanges = "update_would_overwrite_local_changes"

    [<Literal>]
    let UpdateWouldCreateConflictSession = "update_would_create_conflict_session"

    [<Literal>]
    let AcceptanceTargetRequired = "acceptance_target_required"

    [<Literal>]
    let PreviewIndeterminate = "preview_indeterminate"

    [<Literal>]
    let TargetNotEmpty = "target_not_empty"

    [<Literal>]
    let OperationCanceled = "operation_canceled"

    [<Literal>]
    let BaseContentNotFound = "base_content_not_found"

    /// The library's code for a selected path that exists neither in the workspace nor, for a commit, in HEAD.
    [<Literal>]
    let PathNotFound = "path_not_found"

    /// The library reports this warning when a picked file's content is not in the local cache.
    [<Literal>]
    let ObjectNotMaterialized = "object_not_materialized"

    /// The library's code for a large-file download that did not finish after the update itself was done.
    [<Literal>]
    let HydrationFailed = "hydration_failed"

    /// The library refuses to download or free a large file that has local edits. A call has this code when
    /// every failed path is such a refusal.
    [<Literal>]
    let LocalChanges = "local_changes"

    [<Literal>]
    let InvalidLfsThreshold = "invalid_lfs_threshold"

    /// Produced by the Swate main process when the background indexing limit is out of range.
    [<Literal>]
    let InvalidDiffIndexingLimit = "invalid_diff_indexing_limit"

    // Produced by the Swate main process (session host and IPC handler).
    [<Literal>]
    let ServiceUnavailable = "service_unavailable"

    [<Literal>]
    let SessionUnavailable = "session_unavailable"

    [<Literal>]
    let WorkspaceUnmanaged = "workspace_unmanaged"

    [<Literal>]
    let WorkspaceAmbiguous = "workspace_ambiguous"

    [<Literal>]
    let LocationUnsupported = "location_unsupported"

    [<Literal>]
    let UnexpectedException = "unexpected_exception"

    [<Literal>]
    let LockRemovalRefused = "lock_removal_refused"

    /// A call named an operation id that a running operation already has.
    [<Literal>]
    let OperationIdInUse = "operation_id_in_use"

    /// The library's code for a held index lock.
    [<Literal>]
    let IndexLocked = "index_locked"

    [<Literal>]
    let LockRemoved = "lock_removed"

    [<Literal>]
    let InvalidPath = "invalid_path"

    [<Literal>]
    let InvalidRef = "invalid_ref"

    [<Literal>]
    let InvalidRevision = "invalid_revision"

    [<Literal>]
    let BindingNotPersisted = "binding_not_persisted"

    /// A text diff request with an overlong id or a number that is negative or not a decimal integer.
    [<Literal>]
    let InvalidDiffRequest = "invalid_diff_request"

    /// The IPC call itself failed before a structured result existed.
    [<Literal>]
    let TransportError = "transport_error"

    // Produced by the main process when the DataHUB ruleset refuses a storage policy change.
    // The renderer checks the same rules before calling.
    [<Literal>]
    let StoragePolicyBlocked = "storage_policy_blocked"

    /// Recovery action codes, all produced by the library.
    module Recovery =

        [<Literal>]
        let RefreshConflictSession = "refresh_conflict_session"

        [<Literal>]
        let ResolveConflictSession = "resolve_conflict_session"

        [<Literal>]
        let RetryMaterialization = "retry_materialization"

        [<Literal>]
        let RemoveIndexLock = "remove_index_lock"

        [<Literal>]
        let ReconcileIndex = "reconcile_index"

        [<Literal>]
        let RestoreWorkspace = "restore_workspace"

        [<Literal>]
        let RefreshWorkspace = "refresh_workspace"

        [<Literal>]
        let InspectWorkspace = "inspect_workspace"

        [<Literal>]
        let ReopenWorkspace = "reopen_workspace"

        [<Literal>]
        let CheckDependencies = "check_dependencies"

        [<Literal>]
        let AbortMerge = "abort_merge"

        [<Literal>]
        let RemoveCloneTarget = "remove_clone_target"

        [<Literal>]
        let AcceptUpdateRisks = "accept_update_risks"

        [<Literal>]
        let ResolveLocalChanges = "resolve_local_changes"

        [<Literal>]
        let RetryPublish = "retry_publish"

[<StringEnum(CaseRules.None)>]
type RefKindDto =
    | Local
    | Remote

type LogicalRefDto = {
    Name: string
    ProviderRef: string
    Kind: RefKindDto
    IsCurrent: bool
}

[<StringEnum(CaseRules.None)>]
type FileChangeKindDto =
    | Added
    | Modified
    | Deleted
    | Renamed
    | Conflicted

type FileChangeDto = {
    Path: string
    OldPath: string option
    Kind: FileChangeKindDto
}

[<StringEnum(CaseRules.None)>]
type RevisionRelationshipDto =
    | UpToDate
    | LocalAhead
    | TargetAhead
    | Diverged
    | NoTarget
    | Unknown

type SynchronizationStateDto = {
    BaseRevision: string option
    WorkspaceRevision: string option
    TargetRevision: string option
    TargetRef: LogicalRefDto option
    LocalRevisionCount: int option
    TargetRevisionCount: int option
    RemoteChangedPaths: string[] option
    Relationship: RevisionRelationshipDto
}

type ConflictSessionHandleDto = { SessionId: string; Version: string }

[<RequireQualifiedAccess>]
type ContentViewDto =
    | Text of content: string
    | Unsupported of reason: string option

type ConflictCandidateObjectDto = {
    SizeBytes: float option
    ObjectId: string option
    IsLocallyAvailable: bool
}

type ConflictCandidateDto = {
    CandidateId: string
    Label: string
    Revision: string option
    Preview: ContentViewDto option
    Object: ConflictCandidateObjectDto option
}

type ConflictItemDto = {
    Path: string
    Candidates: ConflictCandidateDto[]
    CombinedPreview: ContentViewDto option
    SupportsResolvedContent: bool
}

type ConflictSessionSummaryDto = {
    Handle: ConflictSessionHandleDto
    Items: ConflictItemDto[]
}

type ConflictResolutionOutcomeDto = {
    RefreshedHandle: ConflictSessionHandleDto
    RemainingItems: ConflictItemDto[]
}

type WorkspaceStatusDto = {
    CurrentRef: LogicalRefDto option
    WorkspaceVersion: string
    Changes: FileChangeDto[]
    ActiveConflictSession: ConflictSessionSummaryDto option
    Synchronization: SynchronizationStateDto option
}

type SwitchPreflightDto = { PathsAtRisk: string[]; IsSafe: bool }

/// Materialization state of one large object whose content may not be downloaded yet.
type ObjectStateDto = {
    Path: string
    IsMaterialized: bool
    IsLocallyAvailable: bool
    SizeBytes: float option
    ObjectId: string option
}

type StoragePolicySettingsDto = {
    AutoPolicyThresholdMb: int option
    MaterializeLargeObjects: bool
    /// Whole MiB. The background indexing of a diff stops when the pages it read add up to this
    /// size. None keeps the value the session has.
    DiffIndexingLimitMb: int option
}

type DependencyStatusDto = {
    Component: string
    Installed: bool
    Version: string option
    Compatible: bool
    Remediation: string option
    /// The Git executable the check ran or tried to run, as an absolute path when it could be resolved.
    ProbeExecutablePath: string option
}

/// What the main process knows about the version control dependencies. Statuses and Failure hold the
/// result of the latest finished check. A check that is running replaces them when it ends.
type VersionControlDependencyStateDto = {
    /// The components of the latest check. Empty before the first check ends or when it failed outright.
    Statuses: DependencyStatusDto[]
    /// Why the latest check could not report every component. None when it reported all of them.
    Failure: string option
    IsChecking: bool
    /// Epoch milliseconds at which the latest check ended. None before the first check ends.
    CheckedAt: float option
}

type RepositoryLocationDto = {
    ProviderId: string
    DisplayName: string option
    ProviderLocation: string
    ConnectionProfileId: string option
}

/// Which optional services the open session offers. The renderer reads Synchronization and
/// RepositoryBrowser. A call to an absent service answers service_unavailable.
type ServiceAvailabilityDto = {
    Synchronization: bool
    TextDiff: bool
    ConflictResolution: bool
    ObjectMaterialization: bool
    StoragePolicy: bool
    Maintenance: bool
    RepositoryBrowser: bool
}

type WorkspaceSessionInfoDto = {
    SessionId: string
    ProviderId: string
    WorkspaceRoot: string
    Location: RepositoryLocationDto option
    Services: ServiceAvailabilityDto
}

/// A request without payload. Every call carries an operation id so it can be canceled, and the
/// same record names a running operation in cancelOperation and versionControlOperationStarted.
type OperationRequestDto = { OperationId: string }

type CloneWorkspaceRequestDto = {
    OperationId: string
    /// Nonsecret repository location text (a clone URL for Git repositories).
    ProviderLocation: string
    DisplayName: string option
    TargetPath: string
    TargetRef: string option
    MaterializeAllObjects: bool
}

type InitializeWorkspaceRequestDto = {
    OperationId: string
    TargetPath: string
}

type BindWorkspaceRequestDto = {
    OperationId: string
    ProviderLocation: string
    DisplayName: string option
}

type CreateRefRequestDto = {
    OperationId: string
    Name: string
    BaseRef: string option
    SwitchTo: bool
    ExpectedWorkspaceVersion: string
}

type SwitchRefRequestDto = {
    OperationId: string
    TargetRef: string
    ExpectedWorkspaceVersion: string
}

type CreateRevisionRequestDto = {
    OperationId: string
    Message: string
    Paths: string[]
    ExpectedWorkspaceVersion: string
}

type RestorePathsRequestDto = {
    OperationId: string
    Paths: string[]
    ExpectedWorkspaceVersion: string
}

/// The objects of one materialization or dematerialization, one file or many.
type ObjectPathsRequestDto = {
    OperationId: string
    Paths: string[]
    /// True refreshes the file tree after the call whatever the result. False refreshes it only
    /// when the result changed state.
    AlwaysRefreshTree: bool
}

/// One synchronization: refresh, update when the target is ahead, publish unless
/// PublishLocalRevisions is false. ExpectedTargetRevision pins an accepted update to the
/// target revision the user saw.
type SynchronizeRequestDto = {
    OperationId: string
    ExpectedWorkspaceVersion: string
    ExpectedTargetRevision: string option
    AcceptUpdateRisks: bool
    PublishLocalRevisions: bool
}

[<RequireQualifiedAccess>]
type ConflictResolutionDto =
    | PickCandidate of candidateId: string
    | SupplyResolvedContent of content: string

type ResolveConflictRequestDto = {
    OperationId: string
    Handle: ConflictSessionHandleDto
    ExpectedWorkspaceVersion: string
    Path: string
    Resolution: ConflictResolutionDto
}

type FinalizeConflictRequestDto = {
    OperationId: string
    Handle: ConflictSessionHandleDto
    ExpectedWorkspaceVersion: string
    Message: string option
}

type CancelConflictRequestDto = {
    OperationId: string
    Handle: ConflictSessionHandleDto
    ExpectedWorkspaceVersion: string
}

type PathStoragePolicyRequestDto = {
    OperationId: string
    Path: string
    UseLargeObjectStorage: bool
}

type StoragePolicySettingsRequestDto = {
    OperationId: string
    Settings: StoragePolicySettingsDto
}

type InstallDependencyRequestDto = {
    OperationId: string
    Component: string
}

module OperationResultDto =

    /// The value of a successful or partially successful result.
    let tryValue (result: OperationResultDto<'T>) : 'T option =
        match result with
        | OperationResultDto.Succeeded outcome
        | OperationResultDto.PartiallySucceeded(outcome, _) -> Some outcome.Value
        | OperationResultDto.Failed _ -> None
