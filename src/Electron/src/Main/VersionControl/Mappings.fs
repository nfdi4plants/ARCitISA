/// Structural mappings between the library's abstractions and the IPC DTOs. Nothing in
/// here reads a message text. Failures keep their category, code, state flags, paths,
/// recovery action and revision evidence, and results keep their three shapes.
module Main.VersionControl.Mappings

open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Main.Bindings.Node

let failureCategory (category: FailureCategory) : FailureCategoryDto =
    match category with
    | FailureCategory.Validation -> FailureCategoryDto.Validation
    | FailureCategory.NotFound -> FailureCategoryDto.NotFound
    | FailureCategory.Concurrency -> FailureCategoryDto.Concurrency
    | FailureCategory.Authentication -> FailureCategoryDto.Authentication
    | FailureCategory.Authorization -> FailureCategoryDto.Authorization
    | FailureCategory.DependencyMissing -> FailureCategoryDto.DependencyMissing
    | FailureCategory.Network -> FailureCategoryDto.Network
    | FailureCategory.Timeout -> FailureCategoryDto.Timeout
    | FailureCategory.Canceled -> FailureCategoryDto.Canceled
    | FailureCategory.Conflict -> FailureCategoryDto.Conflict
    | FailureCategory.Unsupported -> FailureCategoryDto.Unsupported
    | FailureCategory.ProviderError -> FailureCategoryDto.ProviderError

let recoveryAction (action: RecoveryAction) : RecoveryActionDto = {
    Code = action.Code
    Instructions = action.Instructions
}

let diffSide (side: DiffSide) : DiffSideDto =
    match side with
    | DiffSide.Previous -> DiffSideDto.Previous
    | DiffSide.Current -> DiffSideDto.Current

let diffSideFromDto (side: DiffSideDto) : DiffSide =
    match side with
    | DiffSideDto.Previous -> DiffSide.Previous
    | DiffSideDto.Current -> DiffSide.Current

let diffContentBlocked (detail: DiffContentBlocked) : DiffContentBlockedDto = {
    Side = diffSide detail.Side
    Evidence = detail.Evidence
}

let failure (failure: OperationFailure) : OperationFailureDto = {
    Category = failureCategory failure.Category
    Code = failure.Code
    Message = failure.Message
    StateChanged = failure.StateChanged
    Retryable = failure.Retryable
    AffectedPaths = Array.copy failure.AffectedPaths
    RecoveryAction = failure.RecoveryAction |> Option.map recoveryAction
    Details = Array.copy failure.Details
    RevisionEvidence =
        failure.RevisionEvidence
        |> Array.map (fun (label, revision) -> {
            Label = label
            Revision = RevisionId.value revision
        })
    DiffDetail = failure.DiffDetail |> Option.map diffContentBlocked
}

let warning (warning: OperationWarning) : OperationWarningDto = {
    Code = warning.Code
    Message = warning.Message
}

let effect (effect: OperationEffect) : OperationEffectDto =
    match effect with
    | Performed -> OperationEffectDto.Performed
    | NoOp reason -> OperationEffectDto.NoOp reason

let publication (state: PublicationState) : PublicationStateDto =
    match state with
    | PublicationNotApplicable -> PublicationStateDto.NotApplicable
    | LocalOnly -> PublicationStateDto.LocalOnly
    | Published -> PublicationStateDto.Published

let outcome (mapValue: 'T -> 'U) (outcome: OperationOutcome<'T>) : OperationOutcomeDto<'U> = {
    Value = mapValue outcome.Value
    Effect = effect outcome.Effect
    Warnings = outcome.Warnings |> Array.map warning
    AffectedPaths = Array.copy outcome.AffectedPaths
    ResultingRevision = outcome.ResultingRevision |> Option.map RevisionId.value
    ResultingWorkspaceVersion = outcome.ResultingWorkspaceVersion
    Publication = publication outcome.Publication
}

let result (mapValue: 'T -> 'U) (result: OperationResult<'T>) : OperationResultDto<'U> =
    match result with
    | Succeeded value -> OperationResultDto.Succeeded(outcome mapValue value)
    | PartiallySucceeded(value, error) -> OperationResultDto.PartiallySucceeded(outcome mapValue value, failure error)
    | Failed error -> OperationResultDto.Failed(failure error)

let progress (operationId: string) (progress: OperationProgress) : VersionControlProgressDto = {
    OperationId = operationId
    PhaseCode = progress.PhaseCode
    Item = progress.Item
    Completed = progress.Completed
    Total = progress.Total
    DisplayMessage = progress.DisplayMessage
}

let outputKind (kind: OperationOutputKind) : string =
    match kind with
    | OperationOutputKind.Command -> "command"
    | OperationOutputKind.StdOut -> "stdout"
    | OperationOutputKind.StdErr -> "stderr"
    | OperationOutputKind.Notice -> "notice"

let output (operationId: string) (entries: OperationOutput[]) : VersionControlOutputDto = {
    OperationId = operationId
    Entries =
        entries
        |> Array.map (fun entry -> {
            CommandId = entry.CommandId
            Kind = outputKind entry.Kind
            Text = entry.Text
            IsPartial = entry.IsPartial
        })
}

let logicalRef (reference: LogicalRef) : LogicalRefDto = {
    Name = reference.Name
    ProviderRef = ProviderRef.value reference.ProviderRef
    Kind =
        match reference.Kind with
        | LocalRef -> RefKindDto.Local
        | RemoteRef -> RefKindDto.Remote
    IsCurrent = reference.IsCurrent
}

let fileChangeKind (kind: FileChangeKind) : FileChangeKindDto =
    match kind with
    | AddedChange -> FileChangeKindDto.Added
    | ModifiedChange -> FileChangeKindDto.Modified
    | DeletedChange -> FileChangeKindDto.Deleted
    | RenamedChange -> FileChangeKindDto.Renamed
    | ConflictedChange -> FileChangeKindDto.Conflicted

let fileChange (change: FileChange) : FileChangeDto = {
    Path = RepositoryPath.value change.Path
    OldPath = change.OldPath |> Option.map RepositoryPath.value
    Kind = fileChangeKind change.Kind
}

let relationship (relationship: RevisionRelationship) : RevisionRelationshipDto =
    match relationship with
    | UpToDate -> RevisionRelationshipDto.UpToDate
    | LocalAhead -> RevisionRelationshipDto.LocalAhead
    | TargetAhead -> RevisionRelationshipDto.TargetAhead
    | Diverged -> RevisionRelationshipDto.Diverged
    | NoTarget -> RevisionRelationshipDto.NoTarget
    | UnknownRelationship -> RevisionRelationshipDto.Unknown

let synchronizationState (state: SynchronizationState) : SynchronizationStateDto = {
    BaseRevision = state.BaseRevision |> Option.map RevisionId.value
    WorkspaceRevision = state.WorkspaceRevision |> Option.map RevisionId.value
    TargetRevision = state.TargetRevision |> Option.map RevisionId.value
    TargetRef = state.TargetRef |> Option.map logicalRef
    LocalRevisionCount = state.LocalRevisionCount
    TargetRevisionCount = state.TargetRevisionCount
    RemoteChangedPaths = state.RemoteChangedPaths |> Option.map (Array.map RepositoryPath.value)
    Relationship = relationship state.Relationship
}

let conflictHandle (handle: ConflictSessionHandle) : ConflictSessionHandleDto = {
    SessionId = handle.SessionId
    Version = handle.Version
}

let conflictPreview (preview: ConflictPreview) : ContentViewDto =
    match preview with
    | TextPreview content -> ContentViewDto.Text content
    | UnsupportedPreview reason -> ContentViewDto.Unsupported reason

let conflictItem (item: ConflictItem) : ConflictItemDto = {
    Path = RepositoryPath.value item.Path
    Candidates =
        item.Candidates
        |> Array.map (fun candidate -> {
            CandidateId = candidate.CandidateId
            Label = candidate.Label
            Revision = candidate.Revision |> Option.map RevisionId.value
            Preview = candidate.Preview |> Option.map conflictPreview
            Object =
                candidate.Object
                |> Option.map (fun candidateObject -> {
                    SizeBytes = candidateObject.SizeBytes
                    ObjectId = candidateObject.ObjectId
                    IsLocallyAvailable = candidateObject.IsLocallyAvailable
                })
        })
    CombinedPreview = item.CombinedPreview |> Option.map conflictPreview
    SupportsResolvedContent = item.SupportsResolvedContent
}

let conflictSession (summary: ConflictSessionSummary) : ConflictSessionSummaryDto = {
    Handle = conflictHandle summary.Handle
    Items = summary.Items |> Array.map conflictItem
}

let conflictOutcome (outcome: ConflictResolutionOutcome) : ConflictResolutionOutcomeDto = {
    RefreshedHandle = conflictHandle outcome.RefreshedHandle
    RemainingItems = outcome.RemainingItems |> Array.map conflictItem
}

let workspaceStatus (status: WorkspaceStatus) : WorkspaceStatusDto = {
    CurrentRef = status.CurrentRef |> Option.map logicalRef
    WorkspaceVersion = status.WorkspaceVersion
    Changes = status.Changes |> Array.map fileChange
    ActiveConflictSession = status.ActiveConflictSession |> Option.map conflictSession
    Synchronization = status.Synchronization |> Option.map synchronizationState
}

let switchPreflight (preflight: SwitchPreflight) : SwitchPreflightDto = {
    PathsAtRisk = preflight.PathsAtRisk |> Array.map RepositoryPath.value
    IsSafe = preflight.IsSafe
}

let objectState (state: ObjectState) : ObjectStateDto = {
    Path = RepositoryPath.value state.Path
    IsMaterialized = state.IsMaterialized
    IsLocallyAvailable = state.IsLocallyAvailable
    SizeBytes = state.SizeBytes
    ObjectId = state.ObjectId
}

let dependencyStatus (status: DependencyStatus) : DependencyStatusDto = {
    Component = status.Component
    Installed = status.Installed
    Version = status.Version
    Compatible = status.Compatible
    Remediation = status.Remediation
}

let repositoryLocation (location: RepositoryLocation) : RepositoryLocationDto = {
    ProviderId = ProviderId.value location.ProviderId
    DisplayName = location.DisplayName
    ProviderLocation = location.ProviderLocation
    ConnectionProfileId = location.ConnectionProfileId
}

let serviceAvailability (session: WorkspaceSession) : ServiceAvailabilityDto = {
    Synchronization = session.Synchronization.IsSome
    TextDiff = session.TextDiff.IsSome
    ConflictResolution = session.ConflictResolution.IsSome
    ObjectMaterialization = session.ObjectMaterialization.IsSome
    ObjectBatchMaterialization = session.ObjectBatchMaterialization.IsSome
    StoragePolicy = session.StoragePolicy.IsSome
    Maintenance = session.Maintenance.IsSome
    RepositoryBrowser = session.RepositoryBrowser.IsSome
}

let sessionInfo (sessionId: string) (session: WorkspaceSession) : WorkspaceSessionInfoDto = {
    SessionId = sessionId
    ProviderId = ProviderId.value session.Descriptor.ProviderId
    WorkspaceRoot = session.Descriptor.WorkspaceRoot
    Location = session.Descriptor.Location |> Option.map repositoryLocation
    Services = serviceAvailability session
}

let conflictHandleFromDto (handle: ConflictSessionHandleDto) : ConflictSessionHandle = {
    SessionId = handle.SessionId
    Version = handle.Version
}

let conflictResolutionFromDto (resolution: ConflictResolutionDto) : ConflictResolution =
    match resolution with
    | ConflictResolutionDto.PickCandidate candidateId -> PickCandidate candidateId
    | ConflictResolutionDto.SupplyResolvedContent content -> SupplyResolvedContent content

/// A validation failure for a path the renderer sent that the library would reject.
let invalidPathFailure (path: string) (message: string) : OperationFailure = {
    OperationFailure.create Validation VersionControlCodes.InvalidPath message with
        AffectedPaths = [| path |]
}

let tryRepositoryPath (path: string) : Result<RepositoryPath, OperationFailure> =
    RepositoryPath.tryCreate path |> Result.mapError (invalidPathFailure path)

/// Converts every path or fails on the first invalid one without touching the provider.
let tryRepositoryPaths (paths: string[]) : Result<RepositoryPath[], OperationFailure> =
    let converted = ResizeArray<RepositoryPath>()
    let mutable failure = None
    let mutable index = 0

    while failure.IsNone && index < paths.Length do
        match tryRepositoryPath paths.[index] with
        | Ok repositoryPath -> converted.Add repositoryPath
        | Error error -> failure <- Some error

        index <- index + 1

    match failure with
    | Some error -> Error error
    | None -> Ok(converted.ToArray())

let tryProviderRef (value: string) : Result<ProviderRef, OperationFailure> =
    ProviderRef.tryCreate value
    |> Result.mapError (OperationFailure.create Validation VersionControlCodes.InvalidRef)

let tryRevisionId (value: string) : Result<RevisionId, OperationFailure> =
    RevisionId.tryCreate value
    |> Result.mapError (OperationFailure.create Validation VersionControlCodes.InvalidRevision)

/// The longest operation id or handle id a text diff request may carry. Swate checks no length of
/// the other tokens (cursors, page ids, gap ids, continuations, preparation tokens, handle versions
/// and encoding names). They are opaque here, and the library rejects a value it did not issue
/// or does not support.
[<Literal>]
let MaxTextDiffIdLength = 128

/// The largest failure message, in UTF-8 bytes, a text diff reply carries.
[<Literal>]
let MaxTextDiffMessageBytes = 4096

/// The longest DiffDetail evidence, in UTF-16 code units, a text diff reply carries.
[<Literal>]
let MaxDiffEvidenceLength = 256

/// The longest failure Details line, in UTF-16 code units, a text diff reply carries.
[<Literal>]
let MaxTextDiffDetailLength = 256

/// The largest sum of failure Details lines, in UTF-8 bytes, a text diff reply carries.
[<Literal>]
let MaxTextDiffDetailsBytes = 4096

/// The largest warning message, in UTF-8 bytes, a text diff reply carries.
[<Literal>]
let MaxTextDiffWarningBytes = 4096

let private int64Text (value: int64) : string = string value

let diffHandle (handle: DiffHandle) : DiffHandleDto = {
    Id = handle.Id
    Version = handle.Version
}

let lineRange (range: LineRange) : LineRangeDto = {
    Start = int64Text range.Start
    Count = int64Text range.Count
}

let lineEnding (ending: LineEnding) : LineEndingDto =
    match ending with
    | LineEnding.NoEnding -> LineEndingDto.NoEnding
    | LineEnding.LF -> LineEndingDto.LF
    | LineEnding.CRLF -> LineEndingDto.CRLF
    | LineEnding.CR -> LineEndingDto.CR

let highlight (highlight: Highlight) : HighlightDto = {
    Start = highlight.Start
    Length = highlight.Length
    Kind =
        match highlight.Kind with
        | HighlightKind.UnchangedText -> HighlightKindDto.UnchangedText
        | HighlightKind.ChangedText -> HighlightKindDto.ChangedText
}

let lineSlice (slice: LineSlice) : LineSliceDto = {
    OffsetUtf16 = int64Text slice.OffsetUtf16
    TotalUtf16 = slice.TotalUtf16 |> Option.map int64Text
    Text = slice.Text
    Highlights = slice.Highlights |> Array.map highlight
}

let diffLine (line: DiffLine) : DiffLineDto = {
    Number = int64Text line.Number
    Ending = lineEnding line.Ending
    Slice = lineSlice line.Slice
}

let diffRowKind (kind: DiffRowKind) : DiffRowKindDto =
    match kind with
    | DiffRowKind.Context -> DiffRowKindDto.Context
    | DiffRowKind.Added -> DiffRowKindDto.Added
    | DiffRowKind.Removed -> DiffRowKindDto.Removed
    | DiffRowKind.Replaced -> DiffRowKindDto.Replaced
    | DiffRowKind.EndingChanged -> DiffRowKindDto.EndingChanged

let diffRow (row: DiffRow) : DiffRowDto = {
    Id = row.Id
    Kind = diffRowKind row.Kind
    Previous = row.Previous |> Option.map diffLine
    Current = row.Current |> Option.map diffLine
}

let hunkBody (body: HunkBody) : HunkBodyDto =
    match body with
    | HunkBody.AlignedRows rows -> HunkBodyDto.AlignedRows(rows |> Array.map diffRow)
    | HunkBody.UnalignedSides(previous, current) ->
        HunkBodyDto.UnalignedSides(previous |> Array.map diffLine, current |> Array.map diffLine)

let hunkFragment (fragment: HunkFragment) : HunkFragmentDto = {
    HunkId = fragment.HunkId
    PreviousRange = lineRange fragment.PreviousRange
    CurrentRange = lineRange fragment.CurrentRange
    StartsHunk = fragment.StartsHunk
    EndsHunk = fragment.EndsHunk
    Body = hunkBody fragment.Body
}

let equalGap (gap: EqualGap) : EqualGapDto = {
    GapId = gap.GapId
    PreviousRange = lineRange gap.PreviousRange
    CurrentRange = lineRange gap.CurrentRange
}

let diffPart (part: DiffPart) : DiffPartDto =
    match part with
    | DiffPart.Hunk fragment -> DiffPartDto.Hunk(hunkFragment fragment)
    | DiffPart.HiddenEqual gap -> DiffPartDto.HiddenEqual(equalGap gap)
    | DiffPart.ExpandedContext(gapId, rows) -> DiffPartDto.ExpandedContext(gapId, rows |> Array.map diffRow)

let scanProgress (progress: ScanProgress) : ScanProgressDto = {
    ValidatedBytes = int64Text progress.ValidatedBytes
    TotalBytes = int64Text progress.TotalBytes
    ScanComplete = progress.ScanComplete
}

let snippetEnd (snippetEnd: SnippetEnd) : SnippetEndDto =
    match snippetEnd with
    | SnippetEnd.Truncated -> SnippetEndDto.Truncated
    | SnippetEnd.MoreTextPending -> SnippetEndDto.MoreTextPending
    | SnippetEnd.LineEnd -> SnippetEndDto.LineEnd
    | SnippetEnd.EndOfFile -> SnippetEndDto.EndOfFile

let pendingSide (side: PendingSide) : PendingSideDto =
    match side with
    | PendingSide.NoActiveLine -> PendingSideDto.NoActiveLine
    | PendingSide.Snippet snippet ->
        PendingSideDto.Snippet {
            Line = int64Text snippet.Line
            OffsetUtf16 = int64Text snippet.OffsetUtf16
            Text = snippet.Text
            End = snippetEnd snippet.End
        }
    | PendingSide.Exhausted lineCount -> PendingSideDto.Exhausted(int64Text lineCount)

let pendingPreview (preview: PendingPreview) : PendingPreviewDto = {
    Previous = pendingSide preview.Previous
    Current = pendingSide preview.Current
    Mismatch =
        preview.Mismatch
        |> Option.map (fun marker -> {
            PreviousOffsetUtf16 = int64Text marker.PreviousOffsetUtf16
            CurrentOffsetUtf16 = int64Text marker.CurrentOffsetUtf16
        })
}

let diffPage (page: DiffPage) : DiffPageDto = {
    PageId = page.PageId
    NextCursor = page.NextCursor
    Parts = page.Parts |> Array.map diffPart
    Progress = scanProgress page.Progress
    OutputComplete = page.OutputComplete
    Pending = page.Pending |> Option.map pendingPreview
}

let diffSourceInfo (info: DiffSourceInfo) : DiffSourceInfoDto = {
    Path = RepositoryPath.value info.Path
    Revision = info.Revision |> Option.map RevisionId.value
    IsAbsent = info.IsAbsent
    ByteLength = int64Text info.ByteLength
    LineCount = info.LineCount |> Option.map int64Text
    Encoding = info.Encoding
    EncodingWasChosen = info.EncodingWasChosen
    HasBom = info.HasBom
}

let diffSourceInfoPair (previous: DiffSourceInfo, current: DiffSourceInfo) : DiffSourceInfoPairDto = {
    Previous = diffSourceInfo previous
    Current = diffSourceInfo current
}

/// The cause of a memory diff or of a blocker that memory causes. A MemoryOnly policy is the
/// indexing limit setting. A PreferDisk policy that ended in memory is the low-space fallback.
let memoryCause (policy: DiffStoragePolicy) : MemoryCauseDto =
    match policy with
    | DiffStoragePolicy.MemoryOnly _ -> MemoryCauseDto.BySetting
    | DiffStoragePolicy.PreferDisk _ -> MemoryCauseDto.ByLowSpace

let diffStorage (policy: DiffStoragePolicy) (storage: DiffStorage) : DiffStorageDto =
    match storage with
    | DiffStorage.OnDisk -> DiffStorageDto.OnDisk
    | DiffStorage.InMemory budgetBytes -> DiffStorageDto.InMemory(int64Text budgetBytes, memoryCause policy)

let diffBlocker (policy: DiffStoragePolicy) (blocker: DiffBlocker) : DiffBlockerDto =
    match blocker with
    | DiffBlocker.Binary(side, evidence) -> DiffBlockerDto.Binary(diffSide side, evidence)
    | DiffBlocker.LocalContentUnavailable(side, objectId) ->
        DiffBlockerDto.LocalContentUnavailable(diffSide side, objectId)
    | DiffBlocker.EncodingRequired(side, token, candidates) ->
        DiffBlockerDto.EncodingRequired(
            diffSide side,
            { PreparationTokenDto.Id = token.Id },
            candidates
            |> Array.map (fun candidate -> {
                EncodingCandidateDto.Encoding = candidate.Encoding
                Preview = candidate.Preview
            })
        )
    | DiffBlocker.NotRegularFile side -> DiffBlockerDto.NotRegularFile(diffSide side)
    | DiffBlocker.ProviderUnsupported -> DiffBlockerDto.ProviderUnsupported
    | DiffBlocker.BlobTooLargeForMemory(side, blobBytes, limitBytes) ->
        DiffBlockerDto.BlobTooLargeForMemory(
            diffSide side,
            int64Text blobBytes,
            int64Text limitBytes,
            memoryCause policy
        )

let resumablePage (resumable: Resumable<DiffPage>) : ResumablePageDto =
    match resumable with
    | Resumable.Ready page -> ResumablePageDto.Ready(diffPage page)
    | Resumable.Scanning(progress, continuation, pending) ->
        ResumablePageDto.Scanning(scanProgress progress, continuation, pending |> Option.map pendingPreview)

let openDiffResult (policy: DiffStoragePolicy) (result: OpenDiffResult) : OpenDiffResultDto =
    match result with
    | OpenDiffResult.NotDiffable blocker -> OpenDiffResultDto.NotDiffable(diffBlocker policy blocker)
    | OpenDiffResult.Opened(handle, previous, current, first, storage) ->
        OpenDiffResultDto.Opened(
            diffHandle handle,
            diffSourceInfo previous,
            diffSourceInfo current,
            resumablePage first,
            diffStorage policy storage
        )

let resumableOpen (policy: DiffStoragePolicy) (resumable: Resumable<OpenDiffResult>) : ResumableOpenDto =
    match resumable with
    | Resumable.Ready result -> ResumableOpenDto.Ready(openDiffResult policy result)
    | Resumable.Scanning(progress, continuation, pending) ->
        ResumableOpenDto.Scanning(scanProgress progress, continuation, pending |> Option.map pendingPreview)

let resumableParts (resumable: Resumable<DiffPart[]>) : ResumablePartsDto =
    match resumable with
    | Resumable.Ready parts -> ResumablePartsDto.Ready(parts |> Array.map diffPart)
    | Resumable.Scanning(progress, continuation, pending) ->
        ResumablePartsDto.Scanning(scanProgress progress, continuation, pending |> Option.map pendingPreview)

let resumableLine (resumable: Resumable<DiffLine>) : ResumableLineDto =
    match resumable with
    | Resumable.Ready line -> ResumableLineDto.Ready(diffLine line)
    | Resumable.Scanning(progress, continuation, pending) ->
        ResumableLineDto.Scanning(scanProgress progress, continuation, pending |> Option.map pendingPreview)

// Fable cannot emit a lone surrogate literal, so this compares code values.
let private isHighSurrogate (character: char) =
    int character >= 0xD800 && int character <= 0xDBFF

/// Cuts text to at most maxBytes of UTF-8 and never splits a surrogate pair.
let truncateUtf8 (maxBytes: int) (text: string) : string =
    if isNull text || utf8ByteLength text <= maxBytes then
        text
    else
        text.Substring(0, utf8PrefixUnits text maxBytes)

let private truncateUtf16 (maxUnits: int) (text: string) : string =
    if isNull text || text.Length <= maxUnits then
        text
    elif isHighSurrogate text[maxUnits - 1] then
        text.Substring(0, maxUnits - 1)
    else
        text.Substring(0, maxUnits)

/// Cuts every line to MaxTextDiffDetailLength and keeps lines while their UTF-8 sum stays
/// within MaxTextDiffDetailsBytes. The first line that does not fit and all later lines are
/// dropped. Failure details and affected paths are bounded this way.
let private boundLines (lines: string[]) : string[] =
    if isNull lines then
        lines
    else
        let lines = lines |> Array.map (truncateUtf16 MaxTextDiffDetailLength)

        let sums =
            lines
            |> Array.scan (fun total line -> total + utf8ByteLength line) 0
            |> Array.tail

        let kept =
            sums
            |> Array.takeWhile (fun total -> total <= MaxTextDiffDetailsBytes)
            |> Array.length

        Array.truncate kept lines

/// Keeps the first warning and cuts its message, so the warnings add a fixed size to a reply.
let private boundWarnings (warnings: OperationWarningDto[]) : OperationWarningDto[] =
    if isNull warnings then
        warnings
    else
        warnings
        |> Array.truncate 1
        |> Array.map (fun warning -> {
            warning with
                Message = truncateUtf8 MaxTextDiffWarningBytes warning.Message
        })

/// Bounds the variable text of a text diff failure before it crosses IPC. The other
/// fields pass unchanged.
let boundTextDiffFailure (failure: OperationFailureDto) : OperationFailureDto = {
    failure with
        Message = truncateUtf8 MaxTextDiffMessageBytes failure.Message
        Details = boundLines failure.Details
        AffectedPaths = boundLines failure.AffectedPaths
        DiffDetail =
            failure.DiffDetail
            |> Option.map (fun detail -> {
                detail with
                    Evidence = truncateUtf16 MaxDiffEvidenceLength detail.Evidence
            })
}

let boundTextDiffResult (result: OperationResultDto<'T>) : OperationResultDto<'T> =
    match result with
    | OperationResultDto.Succeeded outcome ->
        OperationResultDto.Succeeded {
            outcome with
                Warnings = boundWarnings outcome.Warnings
        }
    | OperationResultDto.PartiallySucceeded(outcome, failure) ->
        // A page fills the reply up to its limit, so the failure next to it carries no lists.
        OperationResultDto.PartiallySucceeded(
            {
                outcome with
                    Warnings = boundWarnings outcome.Warnings
            },
            {
                boundTextDiffFailure failure with
                    Details = [||]
                    AffectedPaths = [||]
            }
        )
    | OperationResultDto.Failed failure -> OperationResultDto.Failed(boundTextDiffFailure failure)

/// Maps a text diff result and bounds its failure text for the IPC reply.
let textDiffResult (mapValue: 'T -> 'U) (value: OperationResult<'T>) : OperationResultDto<'U> =
    result mapValue value |> boundTextDiffResult

let private invalidDiffRequest (message: string) : OperationFailure =
    OperationFailure.create Validation VersionControlCodes.InvalidDiffRequest message

/// Accepts a present value of at most maxLength characters.
let private tryBounded (name: string) (maxLength: int) (value: string) : Result<string, OperationFailure> =
    if isNull value then
        Error(invalidDiffRequest $"The {name} is missing.")
    elif value.Length > maxLength then
        Error(invalidDiffRequest $"The {name} is longer than {maxLength} characters.")
    else
        Ok value

/// An operation id or handle id is at most MaxTextDiffIdLength characters.
let private tryTextDiffId (name: string) : string -> Result<string, OperationFailure> =
    tryBounded name MaxTextDiffIdLength

/// Swate checks only that the token is present, since the token is opaque here.
let private tryPresent (name: string) (value: string) : Result<string, OperationFailure> =
    if isNull value then
        Error(invalidDiffRequest $"The {name} is missing.")
    else
        Ok value

let private tryOptionalToken (name: string) (value: string option) : Result<string option, OperationFailure> =
    match value with
    | None -> Ok None
    | Some text -> tryPresent name text |> Result.map Some

/// Parses a line number or offset the renderer sent as decimal text.
let tryNonNegativeInt64 (name: string) (text: string) : Result<int64, OperationFailure> =
    let isDecimal =
        not (System.String.IsNullOrEmpty text)
        && text |> Seq.forall (fun character -> character >= '0' && character <= '9')

    if not isDecimal then
        Error(invalidDiffRequest $"The {name} must be a non-negative decimal integer.")
    else
        match System.Int64.TryParse text with
        | true, value -> Ok value
        | _ -> Error(invalidDiffRequest $"The {name} is too large.")

let private tryOptionalRepositoryPath (path: string option) : Result<RepositoryPath option, OperationFailure> =
    match path with
    | None -> Ok None
    | Some text -> tryRepositoryPath text |> Result.map Some

/// The storage policy comes from the settings of the session, which the validation here does not see.
let tryOpenDiffRequest
    (storage: DiffStoragePolicy)
    (request: OpenTextDiffRequestDto)
    : Result<OpenDiffRequest, OperationFailure> =
    tryTextDiffId "operation id" request.OperationId
    |> Result.bind (fun _ -> tryOptionalToken "preparation token" request.PreparationTokenId)
    |> Result.bind (fun _ -> tryOptionalToken "previous encoding" request.PreviousEncoding)
    |> Result.bind (fun _ -> tryOptionalToken "current encoding" request.CurrentEncoding)
    |> Result.bind (fun _ -> tryOptionalToken "continuation" request.Continuation)
    |> Result.bind (fun _ -> tryRepositoryPath request.Path)
    |> Result.bind (fun path ->
        tryOptionalRepositoryPath request.PreviousPath
        |> Result.map (fun previousPath -> {
            OpenDiffRequest.Path = path
            PreviousPath = previousPath
            Preparation =
                request.PreparationTokenId
                |> Option.map (fun tokenId -> { PreparationToken.Id = tokenId })
            PreviousEncoding = request.PreviousEncoding
            CurrentEncoding = request.CurrentEncoding
            ContextLines = request.ContextLines
            Continuation = request.Continuation
            Storage = storage
        })
    )

let private tryHandleRequest
    (operationId: string)
    (handleId: string)
    (handleVersion: string)
    (build: DiffHandle -> Result<'T, OperationFailure>)
    : Result<'T, OperationFailure> =
    tryTextDiffId "operation id" operationId
    |> Result.bind (fun _ -> tryPresent "handle version" handleVersion)
    |> Result.bind (fun _ -> tryTextDiffId "handle id" handleId)
    |> Result.bind (fun id ->
        build {
            DiffHandle.Id = id
            Version = handleVersion
        }
    )

let tryDiffHandle (request: TextDiffHandleRequestDto) : Result<DiffHandle, OperationFailure> =
    tryHandleRequest request.OperationId request.HandleId request.HandleVersion Ok

let tryReadPageRequest (request: ReadTextDiffPageRequestDto) : Result<ReadPageRequest, OperationFailure> =
    tryHandleRequest
        request.OperationId
        request.HandleId
        request.HandleVersion
        (fun handle ->
            tryPresent "cursor" request.Cursor
            |> Result.map (fun cursor -> {
                ReadPageRequest.Handle = handle
                Cursor = cursor
                Background = request.Background
            })
        )

let tryReplayPageRequest (request: ReplayTextDiffPageRequestDto) : Result<ReplayPageRequest, OperationFailure> =
    tryHandleRequest
        request.OperationId
        request.HandleId
        request.HandleVersion
        (fun handle ->
            tryPresent "page id" request.PageId
            |> Result.map (fun pageId -> {
                ReplayPageRequest.Handle = handle
                PageId = pageId
            })
        )

let tryExpandRequest (request: ExpandTextDiffRequestDto) : Result<ExpandRequest, OperationFailure> =
    tryHandleRequest
        request.OperationId
        request.HandleId
        request.HandleVersion
        (fun handle ->
            tryPresent "gap id" request.GapId
            |> Result.bind (fun gapId ->
                tryOptionalToken "continuation" request.Continuation
                |> Result.map (fun continuation -> {
                    ExpandRequest.Handle = handle
                    GapId = gapId
                    FromStart = request.FromStart
                    Count = request.Count
                    Continuation = continuation
                })
            )
        )

let tryReadLineRequest (request: ReadTextDiffLineRequestDto) : Result<ReadLineRequest, OperationFailure> =
    tryHandleRequest
        request.OperationId
        request.HandleId
        request.HandleVersion
        (fun handle ->
            tryNonNegativeInt64 "line" request.Line
            |> Result.bind (fun line ->
                tryNonNegativeInt64 "UTF-16 offset" request.OffsetUtf16
                |> Result.bind (fun offset ->
                    tryOptionalToken "continuation" request.Continuation
                    |> Result.map (fun continuation -> {
                        ReadLineRequest.Handle = handle
                        Side = diffSideFromDto request.Side
                        Line = line
                        OffsetUtf16 = offset
                        MaxUtf16 = request.MaxUtf16
                        Continuation = continuation
                    })
                )
            )
        )
