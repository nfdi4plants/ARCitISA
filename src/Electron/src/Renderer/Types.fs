[<AutoOpenAttribute>]
module Renderer.Types

open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.VersionControlTypes
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Page.GitComparison.GitPagedDiffTypes

[<RequireQualifiedAccess>]
type LeftSidebarPage =
    | FileExplorer
    | Git

/// A conflicted file with the provider's combined preview. The handle and the
/// workspace token are the ones the preview was taken with, so a confirmation is
/// checked against exactly that state.
type VersionControlConflictPage = {
    Path: string
    ConflictContent: string
    Handle: ConflictSessionHandleDto
    WorkspaceVersion: string
}

type FileChoiceVersion = {
    CandidateId: string
    SizeBytes: float option
    /// None when the version is not a separately stored object, so download state does not apply.
    IsDownloaded: bool option
    /// Short source revision of the version (first 7 characters), when the provider names one.
    Revision: string option
    IsDeleted: bool
}

type VersionControlFileChoicePage = {
    Path: string
    Handle: ConflictSessionHandleDto
    WorkspaceVersion: string
    Mine: FileChoiceVersion
    Online: FileChoiceVersion
}

type GitUnsupportedPageData = { Path: string; Reason: string option }

/// Why a text diff cannot be shown.
[<RequireQualifiedAccess>]
type GitDiffBlockReason =
    | Binary of evidence: string
    | LocalContentUnavailable of objectId: string option
    | NotRegularFile
    | ProviderUnsupported
    /// A side turned out not to be text after the diff was opened. The evidence names what the
    /// library found: a NUL character, a high share of control characters, an HDF5 signature or
    /// bytes that fail strict decoding.
    | NotText of evidence: string
    /// A committed side is larger than the share of the memory budget that holds it. The sizes are
    /// in bytes. The cause tells whether the indexing limit setting or the low space of the temp
    /// drive put the diff in memory.
    | TooLargeForMemory of blobBytes: float * limitBytes: float * cause: MemoryCauseDto
    /// The open was refused because the temp drive has less free space than the reserve.
    | TempSpaceLow
    /// The open was refused because the memory budget of the diff is used up.
    | MemoryBudgetReached

/// Why a diff stopped reading data. The session stays usable in each case.
[<RequireQualifiedAccess>]
type GitDiffStop =
    /// A read was refused because the temp drive has less free space than the reserve. A retry
    /// after freeing space may work.
    | TempSpaceLow
    /// A background read was refused because a memory diff holds most of its budget. The rest of
    /// the budget is kept for reads the user asks for.
    | MemoryShareUsed
    /// A read the user asked for was refused because the memory budget is used up. Only a reopen
    /// starts a new session.
    | MemoryBudget

[<RequireQualifiedAccess>]
type GitDiffPageStatus =
    | Opening
    | Scanning
    /// The library needs the encoding of one side. Open asks with a preparation token before the
    /// first page. A side that turns out not to be UTF-8 while the diff is read asks without one.
    | EncodingChoice of side: DiffSideDto * token: PreparationTokenDto option * candidates: EncodingCandidateDto[]
    | Ready
    | LoadingNext
    /// The diff opens again after its session ended. The pages the new session has read so far
    /// replace the old pages at the same index. The old pages after them stay on screen until
    /// the reopen reaches the page the user was viewing.
    | Reopening of pagesRead: int
    | Blocked of side: DiffSideDto option * reason: GitDiffBlockReason
    | SourceChanged
    | WorkerFailed of message: string
    | Failed of message: string

/// The rows one expansion of a gap returned. Collapsing puts a hidden gap with the expanded
/// gap's id back in their place, covering exactly their lines. Expanding that id again returns
/// the result the library recorded for it, so the same rows come back.
type GitDiffExpandedGap = {
    GapId: string
    /// Lines of the expanded rows on each side.
    Previous: PagedRange
    Current: PagedRange
    /// JSON length of the expansion result and of the line slices merged into its rows.
    PayloadBytes: float
}

/// A line number of one source. Line numbers start at 0.
type GitDiffLinePosition = { Side: DiffSideDto; Number: float }

/// The source lines a page covers. Pages of two sessions of one diff can start at different
/// lines, so a reopen finds the place the user was reading by these lines.
type GitDiffPageSpan = {
    /// The first line the page shows, on the previous side when its first row has one.
    First: GitDiffLinePosition option
    LastPrevious: float option
    LastCurrent: float option
}

/// One page of the loaded window. Parts are already mapped for the viewer, so a part keeps
/// its identity across renders. An evicted page holds a single placeholder part.
type GitDiffWindowPage = {
    PageId: string
    Parts: PagedPart[]
    /// Display rows of the page as it arrived. An evicted page keeps them, so its placeholder is
    /// as tall as the replayed rows. Expanding a gap does not change them.
    RowCount: int
    /// JSON length of the DTOs this page was built from, including expanded context and line slices.
    PayloadBytes: float
    IsEvicted: bool
    /// One entry per expansion result on this page, the least recently expanded first.
    ExpandedGaps: GitDiffExpandedGap list
    /// The lines of the page as it arrived, kept when the page is evicted.
    Span: GitDiffPageSpan
}

/// The running request for the page after the last loaded one. The background indexing sets
/// Background on its reads, and a Scanning continuation of such a read keeps it.
type GitDiffNextRequest = {
    Cursor: string
    OperationId: string
    Background: bool
}

/// A paged text diff of one changed file. Generation is the page load request id of the
/// selection that opened it, so responses for an older selection can be recognized.
type GitDiffPageData = {
    Path: string
    PreviousPath: string option
    ChangeKind: GitDiffChangeKind option
    Generation: int
    Handle: DiffHandleDto option
    SourceInfos: DiffSourceInfoPairDto option
    Pages: GitDiffWindowPage[]
    /// The page the user asked for last. Eviction drops the pages farthest from it.
    RequestedPageIndex: int
    NextCursor: string option
    /// Set while a next page is loading, so the same cursor is not requested twice.
    NextRequest: GitDiffNextRequest option
    /// Line slices requested and not answered yet.
    PendingLineSlices: PagedLineSliceRequest list
    /// Pending line slices that restarted once without a continuation after the worker dropped their read.
    RestartedLineSlices: PagedLineSliceRequest list
    /// Gaps whose expansion is running. A gap is expanded by one request at a time.
    ExpandingGaps: string list
    /// The evicted page whose replay is running.
    PendingReplay: string option
    /// Pages the viewer showed when it asked for the last replay. Eviction keeps them.
    VisiblePages: string list
    /// Set when a replay or an expansion was asked for while the next page was loading, and by
    /// every background read of the indexing. That page then joins the window without becoming
    /// the requested page, so the page the user went back to stays loaded.
    KeepRequestedPage: bool
    /// Set once the viewer asked for the background read of every remaining page. While it is
    /// set, each arriving page starts the read of the next one, until the output is exhausted.
    Indexing: bool
    /// The indexing stopped because the worker session of a background read closed. The rows
    /// stay and the indexing waits. A user request reopens the diff and lifts the pause.
    IndexingPaused: bool
    /// The size of the pages the session answered, as the length of their JSON in UTF-8 bytes. It
    /// is an upper bound of what the session keeps for them. The background indexing stops when
    /// it reaches the indexing limit. A reopen starts a new session and starts it again at 0.
    JournalBytes: float
    /// Where the session of the open diff keeps its data. None until the diff opened. The storage
    /// of a reopened diff can differ from the one before, since the library decides again.
    Storage: DiffStorageDto option
    /// Why the diff stopped reading, if it did. A new diff and a reopen start without a stop.
    Stop: GitDiffStop option
    /// Why the last expansion or line slice that failed was refused, when the library refused it
    /// because of the storage of the diff. None for any other failure.
    FailureCause: GitDiffStop option
    /// The last read of the next page failed with an error that leaves the rows usable. The rows
    /// stay, the indexing waits, and the continue button of the viewer asks again.
    NextFailed: bool
    /// The message of the failed read behind NextFailed, which the status bar shows.
    NextFailure: string option
    /// Gaps, line slices and evicted pages whose last request failed. The viewer marks their
    /// controls, and the next request of the same control clears the mark.
    FailedGaps: string list
    FailedLineSlices: PagedLineSliceRequest list
    FailedReplays: string list
    Progress: ScanProgressDto option
    Pending: PendingPreviewDto option
    OutputComplete: bool
    Status: GitDiffPageStatus
    /// Encodings the user picked, sent with every later open of this page.
    PreviousEncoding: string option
    CurrentEncoding: string option
    /// Requests still running, canceled when the page closes.
    RunningOperations: string list
    /// The line the viewer scrolls to when a reopen lands. No row of the old session is left
    /// after a reopen, so the scroll position cannot follow a row there.
    ScrollTarget: PagedScrollTarget option
}

[<RequireQualifiedAccess>]
type PageState =
    | ArcFilePage of arcFile: ArcFiles * requestedView: ActiveView option
    | MarkdownPage of string
    | PDFPage of string
    | ImagePage of FileContentType * string
    | HtmlPage of string
    | TextPage of string
    | UnknownPage
    | LargeFilePage of path: string * sizeBytes: float * reason: NoPreviewReason
    //| LandingDraftPage
    | NotesDraftPage
    | NotesSearchPage
    | ProvenanceGroupingPage
    | ProvenanceGraphPage of requestId: string
    | GitDiffPage of GitDiffPageData
    | GitMergeConflictPage of VersionControlConflictPage
    | GitFileChoiceConflictPage of VersionControlFileChoicePage
    | GitUnsupportedPage of GitUnsupportedPageData
    | ErrorPage of string
    | DataHubBrowser
    | ValidationPackageBrowser
    | Changelog of version: string * requestId: string
    | SettingsPage

    static member fromFileContentDTO(dto: FileContentDTO) : PageState =
        match dto.fileType with
        | FileContentType.Markdown -> PageState.MarkdownPage dto.content
        | FileContentType.PDF -> PageState.PDFPage dto.content
        | FileContentType.PNG
        | FileContentType.JPG
        | FileContentType.SVG
        | FileContentType.GIF
            -> PageState.ImagePage(dto.fileType, dto.content)
        | FileContentType.Html -> PageState.HtmlPage dto.content
        | FileContentType.FileContentTypeIsPlainTextVariant -> PageState.TextPage dto.content
        | FileContentType.FileContentTypeIsISAFileVariant ->
            let arcfile = FileContentDTO.toArcFile dto

            match arcfile with
            | Some arcFile ->
                let normalizedPath = PathHelpers.normalizePath dto.path

                if
                    normalizedPath.EndsWith(
                        ARCtrl.ArcPathHelper.DataMapFileName,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                then
                    PageState.ArcFilePage(arcFile, Some ActiveView.DataMap)
                elif normalizedPath.EndsWith(".xlsx", System.StringComparison.OrdinalIgnoreCase) then
                    let startingView =
                        if arcFile.Tables().Count > 0 then
                            ActiveView.Table 0
                        else
                            ActiveView.Metadata

                    PageState.ArcFilePage(arcFile, Some startingView)
                else
                    PageState.ArcFilePage(arcFile, None)
            | None ->
                PageState.ErrorPage
                    $"Failed to parse ARC file: {dto.path} - {dto.fileType} - unsupported format or corrupted content."
        | FileContentType.NoPreview ->
            match dto.noPreview with
            | Some info -> PageState.LargeFilePage(dto.path, info.sizeBytes, info.reason)
            | None -> PageState.UnknownPage
        | _ -> PageState.UnknownPage


type ArcSelection = {
    TreePath: string option
    ExplorerNodeId: string option
} with

    static member Empty = {
        TreePath = None
        ExplorerNodeId = None
    }

[<RequireQualifiedAccess>]
module ArcSelection =

    let private normalizeTreePath = Option.map PathHelpers.normalizePath

    let empty = ArcSelection.Empty

    let normalize (selection: ArcSelection) = {
        selection with
            TreePath = normalizeTreePath selection.TreePath
    }

    let forTreePath (treePath: string option) =
        {
            TreePath = treePath
            ExplorerNodeId = None
        }
        |> normalize

    let forExplorerNode (explorerNodeId: string) (treePath: string option) =
        {
            TreePath = treePath
            ExplorerNodeId = Some explorerNodeId
        }
        |> normalize

    let clearExplorerNode (selection: ArcSelection) = {
        normalize selection with
            ExplorerNodeId = None
    }
