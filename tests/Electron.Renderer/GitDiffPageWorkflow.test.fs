module ElectronRenderer.GitDiffPageWorkflowTests

open System.Collections.Generic
open Browser.Dom
open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Renderer.Context.GitWorkflow
open Renderer.Types
open Swate.Components.Page.GitSidebarTypes
open Swate.Electron.Shared.VersionControlTypes
open Vitest

module Paged = Swate.Components.Page.GitComparison.GitPagedDiffTypes

[<Import("renderToStaticMarkup", "react-dom/server")>]
let private renderToStaticMarkup (element: ReactElement) : string = jsNative

// The diff target reaches the IPC bridges through the Git state context. The preload is
// absent in tests, so the bridges are replaced by empty objects that no test calls.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcGitLabApi" ==> createObj []
            "ipcVersionControlApi" ==> createObj []
            "ipcArcVaultApi" ==> createObj []
            "ipcAuthApi" ==> createObj []
            "ipcTemplateApi" ==> createObj []
            "ipcValidationPackageApi" ==> createObj []
        ]
    )
)
|> ignore

let private never<'T> (name: string) : JS.Promise<'T> = promise { return failwith $"unexpected call: {name}" }

let private reply value = promise { return Ok value }

let private succeeded value : OperationResultDto<'T> =
    OperationResultDto.Succeeded {
        Value = value
        Effect = OperationEffectDto.Performed
        Warnings = [||]
        AffectedPaths = [||]
        ResultingRevision = None
        ResultingWorkspaceVersion = None
        Publication = PublicationStateDto.NotApplicable
    }

let private failedWith code (detail: DiffContentBlockedDto option) : OperationResultDto<'T> =
    OperationResultDto.Failed {
        Category = FailureCategoryDto.ProviderError
        Code = code
        Message = code
        StateChanged = false
        Retryable = false
        AffectedPaths = [||]
        RecoveryAction = None
        Details = [||]
        RevisionEvidence = [||]
        DiffDetail = detail
    }

/// Records every text diff request and answers it with the reply function the test set.
type private FakeDiffClient() =
    let mutable operationCount = 0

    member val Opens = ResizeArray<OpenTextDiffRequestDto>()
    member val Reads = ResizeArray<ReadTextDiffPageRequestDto>()
    member val Replays = ResizeArray<ReplayTextDiffPageRequestDto>()
    member val Expands = ResizeArray<ExpandTextDiffRequestDto>()
    member val Lines = ResizeArray<ReadTextDiffLineRequestDto>()
    member val Closes = ResizeArray<TextDiffHandleRequestDto>()
    member val Cancels = ResizeArray<string>()
    member val PageStates = ResizeArray<PageState option>()

    member val OpenReply: OpenTextDiffRequestDto -> OperationResultDto<ResumableOpenDto> =
        fun _ -> failwith "open" with get, set

    member val ReadReply: ReadTextDiffPageRequestDto -> OperationResultDto<ResumablePageDto> =
        fun _ -> failwith "read" with get, set

    member val ReplayReply: ReplayTextDiffPageRequestDto -> OperationResultDto<DiffPageDto> =
        fun _ -> failwith "replay" with get, set

    member val ExpandReply: ExpandTextDiffRequestDto -> OperationResultDto<ResumablePartsDto> =
        fun _ -> failwith "expand" with get, set

    member val LineReply: ReadTextDiffLineRequestDto -> OperationResultDto<ResumableLineDto> =
        fun _ -> failwith "line" with get, set

    /// Answers the waits of the workflow. A test sets it to hold a wait back.
    member val Delay: int -> JS.Promise<unit> = (fun _ -> promise { return () }) with get, set

    /// The page state the app shows, changed by plain sets and by functional updates.
    member val CurrentPageState: PageState option = None with get, set

    member this.SetPageState(page: PageState option) =
        this.CurrentPageState <- page
        this.PageStates.Add page

    member this.UpdatePageState(update: PageState option -> PageState option) =
        this.SetPageState(update this.CurrentPageState)

    member this.Dependencies: GitDependencies = {
        getSessionInfo = fun _ -> never "getSessionInfo"
        getStatus = fun _ -> never "getStatus"
        listRefs = fun _ -> never "listRefs"
        getRepositoryWebUrl = fun _ -> never "getRepositoryWebUrl"
        getStoragePolicySettings = fun _ -> never "getStoragePolicySettings"
        setStoragePolicySettings = fun _ -> never "setStoragePolicySettings"
        getUploadRightAway = fun () -> never "getUploadRightAway"
        setUploadRightAway = fun _ -> never "setUploadRightAway"
        loadConflictPage = fun _ _ _ -> never "loadConflictPage"
        updatePageState = fun update -> this.UpdatePageState update
        initializeWorkspace = fun _ -> never "initializeWorkspace"
        bindWorkspace = fun _ -> never "bindWorkspace"
        createRemoteProject = fun _ -> never "createRemoteProject"
        loadPublishOwners = fun () -> never "loadPublishOwners"
        renameOpenArcRoot = fun _ -> never "renameOpenArcRoot"
        checkDependencies = fun _ -> never "checkDependencies"
        installDependency = fun _ -> never "installDependency"
        refreshSynchronization = fun _ -> never "refreshSynchronization"
        synchronize = fun _ -> never "synchronize"
        cancelOperation =
            fun request ->
                this.Cancels.Add request.OperationId
                reply true
        cloneWorkspace = fun _ -> never "cloneWorkspace"
        createRef = fun _ -> never "createRef"
        preflightSwitchRef = fun _ -> never "preflightSwitchRef"
        switchRef = fun _ -> never "switchRef"
        createRevision = fun _ -> never "createRevision"
        restorePaths = fun _ -> never "restorePaths"
        resolveConflict = fun _ -> never "resolveConflict"
        finalizeConflict = fun _ -> never "finalizeConflict"
        cancelConflict = fun _ -> never "cancelConflict"
        listObjects = fun _ -> never "listObjects"
        materializeObjects = fun _ -> never "materializeObjects"
        pruneStorage = fun _ -> never "pruneStorage"
        deduplicateStorage = fun _ -> never "deduplicateStorage"
        clearStaleLock = fun _ -> never "clearStaleLock"
        textDiff = {
            openTextDiff =
                fun request ->
                    this.Opens.Add request
                    reply (this.OpenReply request)
            readTextDiffPage =
                fun request ->
                    this.Reads.Add request
                    reply (this.ReadReply request)
            replayTextDiffPage =
                fun request ->
                    this.Replays.Add request
                    reply (this.ReplayReply request)
            expandTextDiff =
                fun request ->
                    this.Expands.Add request
                    reply (this.ExpandReply request)
            readTextDiffLine =
                fun request ->
                    this.Lines.Add request
                    reply (this.LineReply request)
            closeTextDiff =
                fun request ->
                    this.Closes.Add request
                    reply (succeeded ())
        }
        hasUsableAccount = fun () -> true
        delay = fun milliseconds -> this.Delay milliseconds
        newOperationId =
            fun () ->
                operationCount <- operationCount + 1
                $"op-{operationCount}"
        confirmLfsPrune = fun _ -> false
        confirmInstall = fun _ -> false
        reportError = ignore
    }

let private collectMessages (cmd: Cmd<Msg>) = promise {
    let messages = ResizeArray<Msg>()
    cmd |> List.iter (fun sub -> sub messages.Add)
    do! Promise.sleep 0
    return messages.ToArray()
}

/// Applies the message and every message its commands produce until none are left.
let private run (fake: FakeDiffClient) (msg: Msg) (state: GitState) = promise {
    let queue = Queue<Msg>([ msg ])
    let mutable current = state

    while queue.Count > 0 do
        let next, cmd = update fake.Dependencies fake.SetPageState (queue.Dequeue()) current
        current <- next
        let! messages = collectMessages cmd

        for message in messages do
            queue.Enqueue message

    return current
}

let private runningState = {
    GitState.Empty with
        CurrentArcPath = Some "C:/arc"
        ArcSessionId = 1
}

let private change path : GitSidebarChange = {
    Path = path
    OriginalPath = None
    IndexStatus = "M"
    WorkingTreeStatus = "."
    IsConflicted = false
}

let private select path =
    SelectChangeRequested(change path, ignore)

let private diffOf (state: GitState) =
    match state.DiffPage with
    | Some page -> page
    | None -> failwith "Expected an open diff page."

let private diffMsg msg = DiffPageMsg msg

let private handle: DiffHandleDto = { Id = "diff-1"; Version = "1" }

let private sourceInfo path : DiffSourceInfoDto = {
    Path = path
    Revision = None
    IsAbsent = false
    ByteLength = "10"
    LineCount = Some "2"
    Encoding = Some "utf-8"
    EncodingWasChosen = false
    HasBom = false
}

let private progress complete : ScanProgressDto = {
    ValidatedBytes = "10"
    TotalBytes = "10"
    ScanComplete = complete
}

let private lineRange start count : LineRangeDto = { Start = start; Count = count }

let private textLine (number: int) (text: string) : DiffLineDto = {
    Number = string number
    Ending = LineEndingDto.LF
    Slice = {
        OffsetUtf16 = "0"
        TotalUtf16 = Some(string text.Length)
        Text = text
        Highlights = [||]
    }
}

let private changedRow id : DiffRowDto = {
    Id = id
    Kind = DiffRowKindDto.Replaced
    Previous = Some(textLine 0 "old")
    Current = Some(textLine 0 "new")
}

let private hunkWith id (rows: DiffRowDto[]) =
    DiffPartDto.Hunk {
        HunkId = id
        PreviousRange = lineRange "0" "1"
        CurrentRange = lineRange "0" "1"
        StartsHunk = true
        EndsHunk = true
        Body = HunkBodyDto.AlignedRows rows
    }

let private hunk id =
    hunkWith id [| changedRow $"{id}-row" |]

/// A changed row that shows the line on both sides.
let private rowAt (id: string) (line: int) : DiffRowDto = {
    Id = id
    Kind = DiffRowKindDto.Replaced
    Previous = Some(textLine line "old")
    Current = Some(textLine line "new")
}

/// A hunk whose row shows the line on both sides, so pages read in order cover growing lines.
let private hunkAt id (line: int) =
    hunkWith id [| rowAt $"{id}-row" line |]

let private gap id =
    DiffPartDto.HiddenEqual {
        GapId = id
        PreviousRange = lineRange "1" "10"
        CurrentRange = lineRange "1" "10"
    }

let private diffPage pageId (nextCursor: string option) parts : DiffPageDto = {
    PageId = pageId
    NextCursor = nextCursor
    Parts = parts
    Progress = progress nextCursor.IsNone
    OutputComplete = nextCursor.IsNone
    Pending = None
}

let private openedWithStorage (storage: DiffStorageDto) first =
    succeeded (
        ResumableOpenDto.Ready(OpenDiffResultDto.Opened(handle, sourceInfo "a.txt", sourceInfo "a.txt", first, storage))
    )

let private openedWith first =
    openedWithStorage DiffStorageDto.OnDisk first

let private openedPage page =
    openedWith (ResumablePageDto.Ready page)

/// The worker hands out a new handle for every open, so a reopened diff has a handle of its own.
let private handleOfOpen (openCount: int) : DiffHandleDto = {
    Id = $"diff-{openCount}"
    Version = "1"
}

let private openedOn (openHandle: DiffHandleDto) page =
    succeeded (
        ResumableOpenDto.Ready(
            OpenDiffResultDto.Opened(
                openHandle,
                sourceInfo "a.txt",
                sourceInfo "a.txt",
                ResumablePageDto.Ready page,
                DiffStorageDto.OnDisk
            )
        )
    )

let private isFailed (status: GitDiffPageStatus) =
    match status with
    | GitDiffPageStatus.Failed _ -> true
    | _ -> false

let private describePart (part: Paged.PagedPart) =
    match part with
    | Paged.PagedPart.HunkRows(hunkId, _, _, _, _, _) -> $"hunk:{hunkId}"
    | Paged.PagedPart.UnalignedRegion(hunkId, _, _, _, _) -> $"unaligned:{hunkId}"
    | Paged.PagedPart.HiddenGap(gapId, _, _) -> $"gap:{gapId}"
    | Paged.PagedPart.ExpandedRows(gapId, rows) -> $"expanded:{gapId}:{rows.Length}"
    | Paged.PagedPart.EvictedPage(pageId, _) -> $"evicted:{pageId}"

let private pageParts (page: GitDiffWindowPage) = page.Parts |> Array.map describePart

let private openFirstPage (fake: FakeDiffClient) (first: DiffPageDto) = promise {
    fake.OpenReply <- fun _ -> openedPage first
    return! run fake (select "a.txt") runningState
}

let private currentLine (page: GitDiffPageData) =
    page.Pages.[0].Parts
    |> Array.pick (
        function
        | Paged.PagedPart.HunkRows(_, _, _, _, _, rows) -> rows.[0].Current
        | _ -> None
    )

let private noChangesShown (page: GitDiffPageData) =
    let markup =
        renderToStaticMarkup (Renderer.Components.MainContent.GitDiffTarget.Main page)

    markup.Contains("data-testid=\"renderer-git-diff-no-changes\"")

/// The diff target rendered to static markup inside a detached element, so tests can query it.
let private renderTarget (page: GitDiffPageData) =
    let container = document.createElement "div"
    container.innerHTML <- renderToStaticMarkup (Renderer.Components.MainContent.GitDiffTarget.Main page)
    container

/// A diff of five pages whose first worker session closes once the indexing reads past the second
/// page. The reopened diff gets a session of its own that answers every read.
let private pagesOfClosingSession (fake: FakeDiffClient) =
    let pageDto index =
        diffPage $"p{index}" (if index < 5 then Some $"cursor-{index}" else None) [| hunkAt $"h{index}" (index * 10) |]

    fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

    fake.ReadReply <-
        fun request ->
            let index = int (request.Cursor.Replace("cursor-", "")) + 1

            if request.HandleId = "diff-1" && index > 2 then
                failedWith "diff_session_closed" None
            else
                succeeded (ResumablePageDto.Ready(pageDto index))

let private memoryStorage =
    DiffStorageDto.InMemory("5242880", MemoryCauseDto.BySetting)

let private openedInMemory page =
    openedWithStorage memoryStorage (ResumablePageDto.Ready page)

/// A page of a chain that ends at the page numbered last. Each page has a hunk and a gap.
let private chainPage (index: int) (last: int) : DiffPageDto =
    diffPage $"p{index}" (if index < last then Some $"cursor-{index}" else None) [|
        hunkAt $"h{index}" (index * 10)
        gap $"g{index}"
    |]

let private cursorIndex (cursor: string) = int (cursor.Replace("cursor-", ""))

/// The data-state of the status bar of the rendered diff, or None when it shows none.
let private barState (page: GitDiffPageData) =
    let status =
        (renderTarget page).querySelector "[data-testid=\"renderer-git-diff-status\"]"

    if isNull status then
        None
    else
        Some(status.getAttribute "data-state")

let private shows (page: GitDiffPageData) (testId: string) =
    not (isNull ((renderTarget page).querySelector $"[data-testid=\"{testId}\"]"))

Vitest.describe (
    "Git diff page workflow",
    fun () ->
        Vitest.test (
            "Opening follows the preparation continuation, then reads the first page from the page continuation",
            fun () -> promise {
                let fake = FakeDiffClient()

                fake.OpenReply <-
                    fun request ->
                        match request.Continuation with
                        | None -> succeeded (ResumableOpenDto.Scanning(progress false, "open-continuation", None))
                        | Some _ -> openedWith (ResumablePageDto.Scanning(progress false, "page-continuation", None))

                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p1" None [| hunk "h1" |]))

                let renamed = {
                    change "new.txt" with
                        OriginalPath = Some "old.txt"
                        IndexStatus = "R"
                }

                let! state = run fake (SelectChangeRequested(renamed, ignore)) runningState
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(fake.Opens.[0].Path).toBe ("new.txt")
                Vitest.expect(fake.Opens.[0].PreviousPath).toEqual (Some "old.txt")
                Vitest.expect(fake.Opens.[0].ContextLines).toBe (3)
                Vitest.expect(fake.Opens.[0].Continuation).toEqual (None)

                Vitest
                    .expect(
                        {
                            fake.Opens.[1] with
                                OperationId = ""
                                Continuation = None
                        }
                    )
                    .toEqual ({ fake.Opens.[0] with OperationId = "" })

                Vitest.expect(fake.Opens.[1].Continuation).toEqual (Some "open-continuation")
                Vitest.expect(fake.Reads.Count).toBe (1)
                Vitest.expect(fake.Reads.[0].Cursor).toBe ("page-continuation")
                Vitest.expect(fake.Reads.[0].HandleId).toBe (handle.Id)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.Handle).toEqual (Some handle)
                Vitest.expect(page.SourceInfos.IsSome).toBe (true)
                Vitest.expect(page.Pages |> Array.map pageParts).toEqual ([| [| "hunk:h1" |] |])
                Vitest.expect(fake.PageStates.[fake.PageStates.Count - 1]).toEqual (Some(PageState.GitDiffPage page))
            }
        )

        Vitest.test (
            "A diff that cannot be shown opens a blocked page for the side the library names",
            fun () -> promise {
                let cases = [
                    DiffBlockerDto.Binary(DiffSideDto.Previous, "nul byte"),
                    GitDiffPageStatus.Blocked(Some DiffSideDto.Previous, GitDiffBlockReason.Binary "nul byte")
                    DiffBlockerDto.LocalContentUnavailable(DiffSideDto.Current, Some "oid-1"),
                    GitDiffPageStatus.Blocked(
                        Some DiffSideDto.Current,
                        GitDiffBlockReason.LocalContentUnavailable(Some "oid-1")
                    )
                    DiffBlockerDto.NotRegularFile DiffSideDto.Current,
                    GitDiffPageStatus.Blocked(Some DiffSideDto.Current, GitDiffBlockReason.NotRegularFile)
                    DiffBlockerDto.ProviderUnsupported,
                    GitDiffPageStatus.Blocked(None, GitDiffBlockReason.ProviderUnsupported)
                ]

                for blocker, expected in cases do
                    let fake = FakeDiffClient()
                    fake.OpenReply <- fun _ -> succeeded (ResumableOpenDto.Ready(OpenDiffResultDto.NotDiffable blocker))
                    let! state = run fake (select "a.txt") runningState
                    Vitest.expect((diffOf state).Status).toEqual (expected)
                    Vitest.expect((diffOf state).Handle).toEqual (None)
            }
        )

        Vitest.test (
            "An encoding pick reopens with the preparation token, and the other side's choice follows",
            fun () -> promise {
                let fake = FakeDiffClient()
                let token: PreparationTokenDto = { Id = "token-1" }

                let previousCandidates: EncodingCandidateDto[] = [|
                    {
                        Encoding = "windows-1252"
                        Preview = "a"
                    }
                |]

                let currentCandidates: EncodingCandidateDto[] = [| { Encoding = "latin1"; Preview = "b" } |]

                let encodingRequired side candidates =
                    succeeded (
                        ResumableOpenDto.Ready(
                            OpenDiffResultDto.NotDiffable(DiffBlockerDto.EncodingRequired(side, token, candidates))
                        )
                    )

                fake.OpenReply <-
                    fun request ->
                        match request.PreviousEncoding, request.CurrentEncoding with
                        | None, _ -> encodingRequired DiffSideDto.Previous previousCandidates
                        | Some _, None -> encodingRequired DiffSideDto.Current currentCandidates
                        | Some _, Some _ -> openedPage (diffPage "p1" None [| hunk "h1" |])

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation

                Vitest
                    .expect((diffOf state).Status)
                    .toEqual (GitDiffPageStatus.EncodingChoice(DiffSideDto.Previous, Some token, previousCandidates))

                let! state =
                    run
                        fake
                        (diffMsg (GitDiffMsg.ChooseEncoding(generation, DiffSideDto.Previous, "windows-1252")))
                        state

                Vitest.expect(fake.Opens.[1].PreparationTokenId).toEqual (Some token.Id)
                Vitest.expect(fake.Opens.[1].PreviousEncoding).toEqual (Some "windows-1252")
                Vitest.expect(fake.Opens.[1].CurrentEncoding).toEqual (None)

                Vitest
                    .expect((diffOf state).Status)
                    .toEqual (GitDiffPageStatus.EncodingChoice(DiffSideDto.Current, Some token, currentCandidates))

                let! state =
                    run fake (diffMsg (GitDiffMsg.ChooseEncoding(generation, DiffSideDto.Current, "latin1"))) state

                Vitest.expect(fake.Opens.[2].PreparationTokenId).toEqual (Some token.Id)
                Vitest.expect(fake.Opens.[2].PreviousEncoding).toEqual (Some "windows-1252")
                Vitest.expect(fake.Opens.[2].CurrentEncoding).toEqual (Some "latin1")
                Vitest.expect((diffOf state).Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "The next page is read with the cursor of the last page and joins the window",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" None [| hunk "h2" |]))

                let! state = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf state).Generation)) state
                let page = diffOf state

                Vitest.expect(fake.Reads.[0].Cursor).toBe ("cursor-1")
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(page.NextCursor).toEqual (None)
                Vitest.expect(page.OutputComplete).toBe (true)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "An expansion passes its continuation back and replaces the gap with the returned parts",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" None [| hunk "h1"; gap "g1"; hunk "h2" |])

                fake.ExpandReply <-
                    fun request ->
                        match request.Continuation with
                        | None -> succeeded (ResumablePartsDto.Scanning(progress false, "expand-continuation", None))
                        | Some _ ->
                            succeeded (
                                ResumablePartsDto.Ready [|
                                    DiffPartDto.ExpandedContext("g1", [| changedRow "e1"; changedRow "e2" |])
                                    gap "g2"
                                |]
                            )

                let! state = run fake (diffMsg (GitDiffMsg.Expand((diffOf state).Generation, "g1", true))) state
                let page = diffOf state

                Vitest.expect(fake.Expands.[0].GapId).toBe ("g1")
                Vitest.expect(fake.Expands.[0].FromStart).toBe (true)
                Vitest.expect(fake.Expands.[0].Continuation).toEqual (None)

                Vitest
                    .expect(
                        {
                            fake.Expands.[1] with
                                OperationId = ""
                                Continuation = None
                        }
                    )
                    .toEqual (
                        {
                            fake.Expands.[0] with
                                OperationId = ""
                        }
                    )

                Vitest.expect(fake.Expands.[1].Continuation).toEqual (Some "expand-continuation")

                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "hunk:h1"; "expanded:g1:2"; "gap:g2"; "hunk:h2" |])

                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "A line slice whose suspended read was dropped restarts once from its start offset",
            fun () -> promise {
                let fake = FakeDiffClient()

                let longRow = {
                    changedRow "r1" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "0"
                                        TotalUtf16 = Some "6"
                                        Text = "abc"
                                        Highlights = [||]
                                    }
                            }
                }

                let! state = openFirstPage fake (diffPage "p1" None [| hunkWith "h1" [| longRow |] |])

                fake.LineReply <-
                    fun request ->
                        match request.Continuation with
                        | None when fake.Lines.Count = 1 ->
                            succeeded (ResumableLineDto.Scanning(progress false, "line-continuation", None))
                        | Some _ -> failedWith "continuation_mismatch" None
                        | None ->
                            succeeded (
                                ResumableLineDto.Ready {
                                    Number = request.Line
                                    Ending = LineEndingDto.LF
                                    Slice = {
                                        OffsetUtf16 = request.OffsetUtf16
                                        TotalUtf16 = Some "6"
                                        Text = "def"
                                        Highlights = [||]
                                    }
                                }
                            )

                let generation = (diffOf state).Generation

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 3.0))) state

                let page = diffOf state

                Vitest.expect(fake.Lines.Count).toBe (3)
                Vitest.expect(fake.Lines.[1].Continuation).toEqual (Some "line-continuation")
                Vitest.expect(fake.Lines.[2].Continuation).toEqual (None)
                Vitest.expect(fake.Lines.[2].OffsetUtf16).toBe ("3")
                Vitest.expect((currentLine page).Text).toBe ("abcdef")
                Vitest.expect(page.FailedLineSlices).toEqual ([])
                Vitest.expect(page.PendingLineSlices).toEqual ([])
            }
        )

        Vitest.test (
            "A second dropped read of the same line slice fails the slice without another request",
            fun () -> promise {
                let fake = FakeDiffClient()

                let longRow = {
                    changedRow "r1" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "0"
                                        TotalUtf16 = Some "6"
                                        Text = "abc"
                                        Highlights = [||]
                                    }
                            }
                }

                let! state = openFirstPage fake (diffPage "p1" None [| hunkWith "h1" [| longRow |] |])

                fake.LineReply <-
                    fun request ->
                        match request.Continuation with
                        | None -> succeeded (ResumableLineDto.Scanning(progress false, "line-continuation", None))
                        | Some _ -> failedWith "continuation_mismatch" None

                let generation = (diffOf state).Generation

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 3.0))) state

                let page = diffOf state

                Vitest.expect(fake.Lines.Count).toBe (4)
                Vitest.expect(fake.Lines.[2].Continuation).toEqual (None)
                Vitest.expect(page.FailedLineSlices.Length).toBe (1)
                Vitest.expect(page.PendingLineSlices).toEqual ([])
                Vitest.expect(page.RestartedLineSlices).toEqual ([])
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "Consecutive line slices are appended to the displayed line",
            fun () -> promise {
                let fake = FakeDiffClient()

                let longRow = {
                    changedRow "r1" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "0"
                                        TotalUtf16 = None
                                        Text = "abc"
                                        Highlights = [||]
                                    }
                            }
                }

                let! state = openFirstPage fake (diffPage "p1" None [| hunkWith "h1" [| longRow |] |])

                fake.LineReply <-
                    fun request ->
                        let text, total =
                            if request.OffsetUtf16 = "3" then
                                "def", None
                            else
                                "ghi", Some "9"

                        succeeded (
                            ResumableLineDto.Ready {
                                Number = request.Line
                                Ending = LineEndingDto.LF
                                Slice = {
                                    OffsetUtf16 = request.OffsetUtf16
                                    TotalUtf16 = total
                                    Text = text
                                    Highlights = [|
                                        {
                                            Start = 0
                                            Length = 1
                                            Kind = HighlightKindDto.ChangedText
                                        }
                                    |]
                                }
                            }
                        )

                let generation = (diffOf state).Generation

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 3.0))) state

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 6.0))) state

                let line = currentLine (diffOf state)

                Vitest.expect(fake.Lines.[0].Side).toEqual (DiffSideDto.Current)
                Vitest.expect(fake.Lines.[0].Line).toBe ("0")
                Vitest.expect(fake.Lines.[0].OffsetUtf16).toBe ("3")
                Vitest.expect(fake.Lines.[1].OffsetUtf16).toBe ("6")
                Vitest.expect(line.Text).toBe ("abcdefghi")
                Vitest.expect(line.TotalUtf16).toEqual (Some 9.0)
                Vitest.expect(line.Highlights |> Array.map _.Start).toEqual ([| 3; 6 |])
            }
        )

        Vitest.test (
            "A ninth page evicts the farthest page, and a replay puts it back in place",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunk $"h{index}" |]

                let! state = openFirstPage fake (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        let index = int (request.Cursor.Replace("cursor-", "")) + 1
                        succeeded (ResumablePageDto.Ready(pageDto index))

                let current = ref state

                for _ in 2..9 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf current.Value).Generation)) current.Value
                    current.Value <- next

                let loadedIds (page: GitDiffPageData) =
                    page.Pages
                    |> Array.filter (fun windowPage -> not windowPage.IsEvicted)
                    |> Array.map _.PageId

                let state = current.Value
                let page = diffOf state
                Vitest.expect(page.Pages.Length).toBe (9)
                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "evicted:p1" |])
                Vitest.expect(loadedIds page).toEqual ([| for index in 2..9 -> $"p{index}" |])

                fake.ReplayReply <-
                    fun request ->
                        let index = int (request.PageId.Replace("p", ""))
                        succeeded (pageDto index)

                let! state = run fake (diffMsg (GitDiffMsg.Replay(page.Generation, "p1", []))) state
                let page = diffOf state

                Vitest.expect(fake.Replays.[0].PageId).toBe ("p1")
                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "hunk:h1" |])
                Vitest.expect(pageParts page.Pages.[8]).toEqual ([| "evicted:p9" |])
                Vitest.expect(loadedIds page).toEqual ([| for index in 1..8 -> $"p{index}" |])
                Vitest.expect(page.NextCursor).toEqual (Some "cursor-9")
            }
        )

        Vitest.test (
            "An evicted page keeps the display rows it arrived with, and its replay has the same rows",
            fun () -> promise {
                let fake = FakeDiffClient()

                // A header and a row, a gap, and a second hunk with its own header make five rows.
                let pageDto index =
                    diffPage
                        $"p{index}"
                        (Some $"cursor-{index}")
                        (if index = 1 then
                             [| hunk "h1"; gap "g1"; hunk "h2" |]
                         else
                             [| hunk $"h{index}" |])

                let displayRows (windowPage: GitDiffWindowPage) = Paged.displayRowCount windowPage.Parts

                let! state = openFirstPage fake (pageDto 1)

                // Expanding the gap adds rows to the loaded page and leaves the extent it arrived with.
                fake.ExpandReply <-
                    fun _ ->
                        succeeded (
                            ResumablePartsDto.Ready [|
                                DiffPartDto.ExpandedContext(
                                    "g1",
                                    [| changedRow "e1"; changedRow "e2"; changedRow "e3" |]
                                )
                                gap "g2"
                            |]
                        )

                let! state = run fake (diffMsg (GitDiffMsg.Expand((diffOf state).Generation, "g1", true))) state
                Vitest.expect((diffOf state).Pages.[0].RowCount).toBe (5)
                Vitest.expect(displayRows (diffOf state).Pages.[0]).toBe (8)

                fake.ReadReply <-
                    fun request ->
                        let index = int (request.Cursor.Replace("cursor-", "")) + 1
                        succeeded (ResumablePageDto.Ready(pageDto index))

                let current = ref state

                for _ in 2..9 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf current.Value).Generation)) current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "evicted:p1" |])
                Vitest.expect(displayRows page.Pages.[0]).toBe (5)

                fake.ReplayReply <- fun _ -> succeeded (pageDto 1)
                let! state = run fake (diffMsg (GitDiffMsg.Replay(page.Generation, "p1", []))) current.Value
                let replayed = (diffOf state).Pages.[0]
                Vitest.expect(replayed.IsEvicted).toBe (false)
                Vitest.expect(displayRows replayed).toBe (5)
            }
        )

        Vitest.test (
            "Indexing reads every page after the first, one request at a time, and evicts within the window",
            fun () -> promise {
                let fake = FakeDiffClient()
                let lastIndex = 12

                let pageDto index =
                    diffPage $"p{index}" (if index < lastIndex then Some $"cursor-{index}" else None) [|
                        hunk $"h{index}"
                    |]

                let! state = openFirstPage fake (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        let index = int (request.Cursor.Replace("cursor-", "")) + 1
                        succeeded (ResumablePageDto.Ready(pageDto index))

                let! state = run fake (diffMsg (GitDiffMsg.Index (diffOf state).Generation)) state
                let page = diffOf state

                // The loop stops at the end of the output, and no read is left running.
                Vitest
                    .expect(fake.Reads |> Seq.map _.Cursor |> Seq.toArray)
                    .toEqual ([| for index in 1 .. lastIndex - 1 -> $"cursor-{index}" |])

                Vitest.expect(page.Pages.Length).toBe (lastIndex)
                Vitest.expect(page.NextCursor).toEqual (None)
                Vitest.expect(page.NextRequest).toEqual (None)
                Vitest.expect(page.OutputComplete).toBe (true)
                Vitest.expect(page.Indexing).toBe (false)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)

                // The first window stays loaded. Every other page is a placeholder with the rows it had.
                let loadedIds =
                    page.Pages
                    |> Array.filter (fun windowPage -> not windowPage.IsEvicted)
                    |> Array.map _.PageId

                Vitest
                    .expect(loadedIds)
                    .toEqual (
                        [|
                            for index in 1 .. GitDiffPageLoader.MaxLoadedPages -> $"p{index}"
                        |]
                    )

                Vitest.expect(page.Pages |> Array.forall (fun windowPage -> windowPage.RowCount = 2)).toBe (true)

                Vitest
                    .expect(
                        page.Pages
                        |> Array.sumBy (fun windowPage -> Paged.displayRowCount windowPage.Parts)
                    )
                    .toBe (2 * lastIndex)
            }
        )

        Vitest.test (
            "Indexing keeps one read pending, and a request for the next page does not start another",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                let generation = (diffOf state).Generation

                fake.ReadReply <-
                    fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" (Some "cursor-2") [| hunk "h2" |]))

                let step msg state =
                    update fake.Dependencies fake.SetPageState (diffMsg msg) state

                let indexing, indexCmd = step (GitDiffMsg.Index generation) state
                let again, againCmd = step (GitDiffMsg.Index generation) indexing
                let other, otherCmd = step (GitDiffMsg.LoadNext generation) again

                let! _ = collectMessages indexCmd
                let! _ = collectMessages againCmd
                let! _ = collectMessages otherCmd

                Vitest.expect(fake.Reads.Count).toBe (1)
                Vitest.expect((diffOf other).NextRequest.IsSome).toBe (true)
                Vitest.expect((diffOf other).Indexing).toBe (true)
            }
        )

        Vitest.test (
            "Indexing stops when the diff closes, and the late answer starts no read",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                let generation = (diffOf state).Generation

                fake.ReadReply <-
                    fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" (Some "cursor-2") [| hunk "h2" |]))

                let indexing, indexCmd =
                    update fake.Dependencies fake.SetPageState (diffMsg (GitDiffMsg.Index generation)) state

                let! answers = collectMessages indexCmd

                let! closed = run fake (diffMsg (GitDiffMsg.PageStateObserved None)) indexing
                Vitest.expect(closed.DiffPage).toEqual (None)

                let current = ref closed

                for answer in answers do
                    let! next = run fake answer current.Value
                    current.Value <- next

                Vitest.expect(current.Value.DiffPage).toEqual (None)
                Vitest.expect(fake.Reads.Count).toBe (1)
            }
        )

        Vitest.test (
            "A failed read keeps the rows and stops the indexing until the next page is asked for again",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                let generation = (diffOf state).Generation

                let pageDto index =
                    diffPage $"p{index}" (if index < 4 then Some $"cursor-{index}" else None) [| hunk $"h{index}" |]

                let failing = ref true

                fake.ReadReply <-
                    fun request ->
                        let index = int (request.Cursor.Replace("cursor-", "")) + 1

                        if index = 3 && failing.Value then
                            failedWith "git_failure" None
                        else
                            succeeded (ResumablePageDto.Ready(pageDto index))

                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                let page = diffOf state

                // The failure leaves the two pages read, and no further read starts by itself.
                Vitest.expect(fake.Reads.Count).toBe (2)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.NextFailed).toBe (true)
                Vitest.expect(page.Pages.Length).toBe (2)
                Vitest.expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb page).toEqual (None)

                // Asking for the next page again reads it, and the indexing goes on to the end.
                failing.Value <- false
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let page = diffOf state

                Vitest.expect(page.NextFailed).toBe (false)
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2"; "p3"; "p4" |])
                Vitest.expect(page.NextCursor).toEqual (None)
            }
        )

        Vitest.test (
            "A read that ends the diff settles the page while the indexing runs",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                fake.ReadReply <- fun _ -> failedWith "source_changed" None

                let! state = run fake (diffMsg (GitDiffMsg.Index (diffOf state).Generation)) state

                Vitest.expect(fake.Reads.Count).toBe (1)
                Vitest.expect((diffOf state).Status).toEqual (GitDiffPageStatus.SourceChanged)
            }
        )

        Vitest.test (
            "The loader asks for the indexing once the first page is ready and more pages follow",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! single = openFirstPage fake (diffPage "p1" None [| hunk "h1" |])

                Vitest
                    .expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb (diffOf single))
                    .toEqual (None)

                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                let page = diffOf state

                Vitest
                    .expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb page)
                    .toEqual (Some(GitDiffMsg.Index page.Generation))

                // Once the indexing runs, or while a page loads, the loader asks for nothing.
                fake.ReadReply <-
                    fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" (Some "cursor-2") [| hunk "h2" |]))

                let indexing, indexCmd =
                    update fake.Dependencies fake.SetPageState (diffMsg (GitDiffMsg.Index page.Generation)) state

                let! _ = collectMessages indexCmd

                Vitest
                    .expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb (diffOf indexing))
                    .toEqual (None)

                Vitest
                    .expect(
                        GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb {
                            page with
                                Status = GitDiffPageStatus.LoadingNext
                        }
                    )
                    .toEqual (None)
            }
        )

        Vitest.test (
            "The journal estimate adds the UTF-8 size of the JSON of every page, and a reopen starts it again",
            fun () -> promise {
                let fake = FakeDiffClient()

                // Characters of 2 and 3 bytes in UTF-8 make the size differ from the length of the JSON text.
                let pageWith (index: int) (text: string) (nextCursor: string option) =
                    diffPage $"p{index}" nextCursor [|
                        hunkWith $"h{index}" [|
                            {
                                changedRow $"h{index}-row" with
                                    Current = Some(textLine index text)
                            }
                        |]
                    |]

                let first = pageWith 1 "é€é" (Some "cursor-1")
                let second = pageWith 2 "ü€ü€" (Some "cursor-2")
                let third = pageWith 3 "x" None

                // Counts the bytes per character, so the check shares no code with the loader.
                let utf8Bytes (dto: DiffPageDto) =
                    JS.JSON.stringify dto
                    |> Seq.sumBy (fun character ->
                        if int character < 0x80 then 1.0
                        elif int character < 0x800 then 2.0
                        else 3.0
                    )

                Vitest.expect(utf8Bytes first).toBeGreaterThan (float (JS.JSON.stringify first).Length)

                // The first session answers two pages and then closes. The second session answers them all.
                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) first

                let answerOf (cursor: string) =
                    if cursor = "cursor-1" then second else third

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" && request.Cursor <> "cursor-1" then
                            failedWith "diff_session_closed" None
                        else
                            succeeded (ResumablePageDto.Ready(answerOf request.Cursor))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                Vitest.expect((diffOf state).JournalBytes).toBe (utf8Bytes first)

                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect((diffOf state).JournalBytes).toBe (utf8Bytes first + utf8Bytes second)
                Vitest.expect((diffOf state).IndexingPaused).toBe (true)

                // The user request reopens the diff. The estimate then counts the pages of the new session only.
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                Vitest.expect(fake.Opens.Count).toBe (2)

                let newSessionBytes =
                    fake.Reads
                    |> Seq.filter (fun read -> read.HandleId = "diff-2")
                    |> Seq.sumBy (fun read -> utf8Bytes (answerOf read.Cursor))

                Vitest.expect((diffOf state).JournalBytes).toBe (utf8Bytes first + newSessionBytes)
                Vitest.expect((diffOf state).JournalBytes).toBeLessThan (utf8Bytes first + utf8Bytes second)
            }
        )

        Vitest.test (
            "The background indexing stops once the pages read reach the limit, and a user read still reads on",
            fun () -> promise {
                let fake = FakeDiffClient()

                // Every page is a bit above 1 MiB of JSON, and the limit is 2 MiB.
                let pageOf (index: int) =
                    diffPage $"p{index}" (Some $"cursor-{index}") [|
                        hunkWith $"h{index}" [|
                            {
                                changedRow $"h{index}-row" with
                                    Current = Some(textLine index (String.replicate 1100000 "x"))
                            }
                        |]
                    |]

                fake.OpenReply <- fun _ -> openedPage (pageOf 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageOf (int (request.Cursor.Replace("cursor-", "")) + 1)))

                let limited = {
                    runningState with
                        DiffIndexingLimitMb = 2
                }

                let! state = run fake (select "a.txt") limited
                let generation = (diffOf state).Generation
                Vitest.expect(GitDiffPageLoader.indexingLimitReached 2 (diffOf state)).toBe (false)

                // The second page brings the estimate to the limit. Its read was the last background read.
                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                let page = diffOf state
                Vitest.expect(fake.Reads.Count).toBe (1)
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(GitDiffPageLoader.indexingLimitReached 2 page).toBe (true)
                Vitest.expect(page.Indexing).toBe (false)
                Vitest.expect(GitDiffPageLoader.indexingRequest 2 page).toEqual (None)

                // The user asks for the next page, and the read is not held back by the limit.
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                Vitest.expect(fake.Reads.Count).toBe (2)
                Vitest.expect((diffOf state).Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2"; "p3" |])
                Vitest.expect((diffOf state).Indexing).toBe (false)

                // A higher limit asks for the indexing again, and it stops at the new limit.
                Vitest.expect(GitDiffPageLoader.indexingRequest 2 (diffOf state)).toEqual (None)

                Vitest
                    .expect(GitDiffPageLoader.indexingRequest 4 (diffOf state))
                    .toEqual (Some(GitDiffMsg.Index generation))

                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) { state with DiffIndexingLimitMb = 4 }
                Vitest.expect(fake.Reads.Count).toBe (3)
                Vitest.expect((diffOf state).Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2"; "p3"; "p4" |])
                Vitest.expect((diffOf state).Indexing).toBe (false)
            }
        )

        Vitest.test (
            "Lowering the limit stops the indexing after the read in flight",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageOf (index: int) =
                    diffPage $"p{index}" (Some $"cursor-{index}") [|
                        hunkWith $"h{index}" [|
                            {
                                changedRow $"h{index}-row" with
                                    Current = Some(textLine index (String.replicate 1100000 "x"))
                            }
                        |]
                    |]

                fake.OpenReply <- fun _ -> openedPage (pageOf 1)

                // The test answers the read itself, so the reply of the client goes nowhere.
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(pageOf 99))
                let! opened = run fake (select "a.txt") runningState
                let generation = (diffOf opened).Generation
                let deps = fake.Dependencies

                let dispatchNothing (cmd: Cmd<Msg>) =
                    cmd |> List.iter (fun sub -> sub ignore)

                // Starts the indexing, which leaves one background read in flight.
                let indexing, indexCmd =
                    update deps fake.SetPageState (diffMsg (GitDiffMsg.Index generation)) opened

                dispatchNothing indexCmd
                Vitest.expect((diffOf indexing).NextRequest.IsSome).toBe (true)

                // Answers the read in flight with the next page and returns the state after it.
                let answer (state: GitState) =
                    let page = diffOf state
                    let running = page.NextRequest.Value

                    let request: ReadTextDiffPageRequestDto = {
                        OperationId = running.OperationId
                        HandleId = page.Handle.Value.Id
                        HandleVersion = page.Handle.Value.Version
                        Cursor = running.Cursor
                        Background = running.Background
                    }

                    let reply = Ok(succeeded (ResumablePageDto.Ready(pageOf 2)))

                    update deps fake.SetPageState (diffMsg (GitDiffMsg.PageCompleted(generation, request, reply))) state
                    |> fst

                // With the default limit the arriving page starts the next read.
                let continued = answer indexing
                Vitest.expect((diffOf continued).Indexing).toBe (true)
                Vitest.expect((diffOf continued).NextRequest.IsSome).toBe (true)

                // With a lower limit the same page ends the indexing, and no further read starts.
                let stopped =
                    answer {
                        indexing with
                            DiffIndexingLimitMb = 1
                    }

                Vitest.expect((diffOf stopped).Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect((diffOf stopped).Indexing).toBe (false)
                Vitest.expect((diffOf stopped).NextRequest).toEqual (None)
                Vitest.expect(GitDiffPageLoader.indexingRequest 1 (diffOf stopped)).toEqual (None)
            }
        )

        Vitest.test (
            "The bar above the diff names the background state of the diff",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                let page = diffOf state

                let statusOf (shown: GitDiffPageData) =
                    let status =
                        (renderTarget shown).querySelector "[data-testid=\"renderer-git-diff-status\"]"

                    if isNull status then
                        None
                    else
                        Some(status.getAttribute "data-state", status.getAttribute "data-limit-mb")

                // Nothing runs before the indexing starts.
                Vitest.expect(statusOf page).toEqual (None)

                Vitest.expect(statusOf { page with Indexing = true }).toEqual (Some("indexing", "1024"))
                Vitest.expect(statusOf { page with IndexingPaused = true }).toEqual (Some("paused", "1024"))

                // The pages read add up to the limit of the context, which is 1024 MiB here.
                Vitest
                    .expect(
                        statusOf {
                            page with
                                JournalBytes = 1024.0 * 1024.0 * 1024.0
                        }
                    )
                    .toEqual (Some("limit", "1024"))

                Vitest
                    .expect(
                        statusOf {
                            page with
                                JournalBytes = 1024.0 * 1024.0 * 1024.0 - 1.0
                        }
                    )
                    .toEqual (None)

                // A finished indexing, a failed read and a settled diff show no state.
                Vitest
                    .expect(
                        statusOf {
                            page with
                                Indexing = true
                                NextCursor = None
                        }
                    )
                    .toEqual (None)

                Vitest
                    .expect(
                        statusOf {
                            page with
                                Indexing = true
                                NextFailed = true
                        }
                    )
                    .toEqual (None)

                Vitest
                    .expect(
                        statusOf {
                            page with
                                Indexing = true
                                Status = GitDiffPageStatus.SourceChanged
                        }
                    )
                    .toEqual (None)
            }
        )

        Vitest.test (
            "Background arrivals show together after a short wait and a user request shows at once",
            fun () -> promise {
                let fake = FakeDiffClient()
                let releases = ResizeArray<unit -> unit>()

                fake.Delay <- fun _ -> Promise.create (fun resolve _ -> releases.Add(fun () -> resolve ()))

                fake.ReadReply <-
                    fun _ -> succeeded (ResumablePageDto.Ready(diffPage "unused" (Some "cursor-x") [| hunk "hx" |]))

                let! opened = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                let generation = (diffOf opened).Generation
                let deps = fake.Dependencies

                let dispatchNothing (cmd: Cmd<Msg>) =
                    cmd |> List.iter (fun sub -> sub ignore)

                let indexing, indexCmd =
                    update deps fake.SetPageState (diffMsg (GitDiffMsg.Index generation)) opened

                dispatchNothing indexCmd

                // Answers the background read that runs and returns the state with the new page.
                let arrive (state: GitState) (index: int) =
                    let page = diffOf state
                    let running = page.NextRequest.Value

                    let request: ReadTextDiffPageRequestDto = {
                        OperationId = running.OperationId
                        HandleId = page.Handle.Value.Id
                        HandleVersion = page.Handle.Value.Version
                        Cursor = running.Cursor
                        Background = running.Background
                    }

                    let answer =
                        Ok(
                            succeeded (
                                ResumablePageDto.Ready(
                                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunk $"h{index}" |]
                                )
                            )
                        )

                    let next, cmd =
                        update
                            deps
                            fake.SetPageState
                            (diffMsg (GitDiffMsg.PageCompleted(generation, request, answer)))
                            state

                    dispatchNothing cmd
                    next

                let shownPageCount () =
                    match fake.CurrentPageState with
                    | Some(PageState.GitDiffPage shown) -> shown.Pages.Length
                    | _ -> -1

                let publishedBefore = fake.PageStates.Count
                let state = arrive (arrive indexing 2) 3

                // The model has both pages, and the shown page waits for the one delayed publish.
                Vitest.expect((diffOf state).Pages.Length).toBe (3)
                Vitest.expect(fake.PageStates.Count).toBe (publishedBefore)
                Vitest.expect(releases.Count).toBe (1)

                releases.[0] ()
                do! Promise.sleep 0
                Vitest.expect(fake.PageStates.Count).toBe (publishedBefore + 1)
                Vitest.expect(shownPageCount ()).toBe (3)

                // A page that arrives later waits again, and a user request shows the newest page
                // at once. The delayed publish that is still waiting has nothing left to show.
                let state = arrive state 4
                Vitest.expect(releases.Count).toBe (2)
                Vitest.expect(shownPageCount ()).toBe (3)

                let _, loadCmd =
                    update deps fake.SetPageState (diffMsg (GitDiffMsg.LoadNext generation)) state

                dispatchNothing loadCmd
                Vitest.expect(shownPageCount ()).toBe (4)

                let publishedAfterRequest = fake.PageStates.Count
                releases.[1] ()
                do! Promise.sleep 0
                Vitest.expect(fake.PageStates.Count).toBe (publishedAfterRequest)
            }
        )

        Vitest.test (
            "A background read that finds the session closed pauses the indexing without reopening the diff",
            fun () -> promise {
                let fake = FakeDiffClient()
                pagesOfClosingSession fake

                let! state = run fake (select "a.txt") runningState
                let! state = run fake (diffMsg (GitDiffMsg.Index (diffOf state).Generation)) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (1)
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 1))
                Vitest.expect(page.NextRequest).toEqual (None)
                Vitest.expect(page.Indexing).toBe (false)
                Vitest.expect(page.IndexingPaused).toBe (true)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(state.DiffReopen).toEqual (None)
                Vitest.expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb page).toEqual (None)
            }
        )

        Vitest.test (
            "A background read that answers Scanning and then finds the session closed pauses the indexing as well",
            fun () -> promise {
                let fake = FakeDiffClient()
                pagesOfClosingSession fake
                let closingReply = fake.ReadReply

                fake.ReadReply <-
                    fun request ->
                        match request.HandleId, request.Cursor with
                        | "diff-1", "cursor-2" -> succeeded (ResumablePageDto.Scanning(progress false, "scan-1", None))
                        | "diff-1", "scan-1" -> failedWith "diff_session_closed" None
                        | _ -> closingReply request

                let nextKey state =
                    Renderer.Components.MainContent.GitDiffTarget.nextPageKeyOf (diffOf state)

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let keyOfFirstPage = nextKey state

                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let keyOfSecondPage = nextKey state
                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                let page = diffOf state

                // The key moves when a page joins. The Scanning answer and the pause keep it, so
                // the viewer does not ask for the next page again on the closed handle.
                Vitest.expect(keyOfSecondPage).not.toEqual (keyOfFirstPage)
                Vitest.expect(nextKey state).toEqual (keyOfSecondPage)

                Vitest
                    .expect(fake.Reads |> Seq.map (fun read -> read.HandleId, read.Cursor) |> Seq.toArray)
                    .toEqual (
                        [|
                            "diff-1", "cursor-1"
                            "diff-1", "cursor-2"
                            "diff-1", "scan-1"
                        |]
                    )

                Vitest.expect(fake.Opens.Count).toBe (1)
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(page.NextRequest).toEqual (None)
                Vitest.expect(page.Indexing).toBe (false)
                Vitest.expect(page.IndexingPaused).toBe (true)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(state.DiffReopen).toEqual (None)
                Vitest.expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb page).toEqual (None)
            }
        )

        Vitest.test (
            "A user request on a paused diff reopens it once, and the indexing then reads on to the end with the new handle",
            fun () -> promise {
                let fake = FakeDiffClient()
                pagesOfClosingSession fake

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect(fake.Opens.Count).toBe (1)
                Vitest.expect((diffOf state).IndexingPaused).toBe (true)

                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.IndexingPaused).toBe (false)
                Vitest.expect(state.DiffReopen).toEqual (None)

                Vitest
                    .expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb page)
                    .toEqual (Some(GitDiffMsg.Index generation))

                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(fake.Reads |> Seq.last |> _.HandleId).toBe ("diff-2")
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| for index in 1..5 -> $"p{index}" |])
                Vitest.expect(page.NextCursor).toEqual (None)
                Vitest.expect(page.Indexing).toBe (false)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "Loaded pages above 8 MiB evict the page farthest from the requested page, and a single oversized page stays loaded",
            fun () -> promise {
                let fake = FakeDiffClient()

                let bigPage (index: int) =
                    let row = {
                        changedRow $"big-{index}" with
                            Current = Some(textLine index (String.replicate (3 * 1024 * 1024) "x"))
                    }

                    diffPage $"p{index}" (if index < 3 then Some $"cursor-{index}" else None) [|
                        hunkWith $"h{index}" [| row |]
                    |]

                let! state = openFirstPage fake (bigPage 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(bigPage (int (request.Cursor.Replace("cursor-", "")) + 1)))

                let! state = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf state).Generation)) state
                Vitest.expect((diffOf state).Pages |> Array.map _.IsEvicted).toEqual ([| false; false |])

                let! state = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf state).Generation)) state
                Vitest.expect((diffOf state).Pages |> Array.map _.IsEvicted).toEqual ([| true; false; false |])

                let oversized = FakeDiffClient()

                let hugeRow = {
                    changedRow "huge" with
                        Current = Some(textLine 0 (String.replicate (9 * 1024 * 1024) "x"))
                }

                let! single = openFirstPage oversized (diffPage "p1" None [| hunkWith "h1" [| hugeRow |] |])
                Vitest.expect((diffOf single).Pages |> Array.map _.IsEvicted).toEqual ([| false |])
            }
        )

        Vitest.test (
            "When the user switches files quickly, the late open of the first file closes its handle and the second file stays ready",
            fun () -> promise {
                let fake = FakeDiffClient()

                fake.OpenReply <-
                    fun request ->
                        openedOn
                            {
                                Id = $"diff-{request.Path}"
                                Version = "1"
                            }
                            (diffPage "p1" None [| hunk "h1" |])

                let first, firstCmd =
                    update fake.Dependencies fake.SetPageState (select "a.txt") runningState

                let second, secondCmd =
                    update fake.Dependencies fake.SetPageState (select "b.txt") first

                let firstOpenId = (diffOf first).RunningOperations |> List.head
                let! lateMessages = collectMessages firstCmd
                let! secondMessages = collectMessages secondCmd
                let current = ref second

                for message in secondMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).Status).toEqual (GitDiffPageStatus.Ready)
                let pageStatesBefore = fake.PageStates.Count

                for message in lateMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(page.Path).toBe ("b.txt")
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.Handle).toEqual (Some { Id = "diff-b.txt"; Version = "1" })
                Vitest.expect(fake.Cancels.Contains firstOpenId).toBe (true)
                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| "diff-a.txt" |])
                Vitest.expect(fake.PageStates.Count).toBe (pageStatesBefore)
            }
        )

        Vitest.test (
            "A handle that arrives after the user left the diff is closed at once",
            fun () -> promise {
                let fake = FakeDiffClient()
                fake.OpenReply <- fun _ -> openedPage (diffPage "p1" None [| hunk "h1" |])

                let opening, openCmd =
                    update fake.Dependencies fake.SetPageState (select "a.txt") runningState

                let! left = run fake (diffMsg (GitDiffMsg.PageStateObserved(Some(PageState.TextPage "notes")))) opening

                let! lateMessages = collectMessages openCmd
                let current = ref left

                for message in lateMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                Vitest.expect(current.Value.DiffPage).toEqual (None)
                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| handle.Id |])
            }
        )

        Vitest.test (
            "Leaving an opened diff page closes its handle",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" None [| hunk "h1" |])

                let! state = run fake (diffMsg (GitDiffMsg.PageStateObserved None)) state

                Vitest.expect(state.DiffPage).toEqual (None)
                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| handle.Id |])
            }
        )

        Vitest.test (
            "Changed sources, a failed worker, content that is not text and a session without a diff service get their own statuses",
            fun () -> promise {
                let detail: DiffContentBlockedDto = {
                    Side = DiffSideDto.Current
                    Evidence = "invalid utf-8"
                }

                let cases = [
                    failedWith "source_changed" None, GitDiffPageStatus.SourceChanged
                    failedWith "diff_worker_failed" None, GitDiffPageStatus.WorkerFailed "diff_worker_failed"
                    failedWith "diff_content_not_text" (Some detail),
                    GitDiffPageStatus.Blocked(Some DiffSideDto.Current, GitDiffBlockReason.NotText "invalid utf-8")
                    failedWith "service_unavailable" None,
                    GitDiffPageStatus.Blocked(None, GitDiffBlockReason.ProviderUnsupported)
                ]

                for failure, expected in cases do
                    let fake = FakeDiffClient()
                    let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                    fake.ReadReply <- fun _ -> failure
                    let! state = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf state).Generation)) state
                    Vitest.expect((diffOf state).Status).toEqual (expected)
            }
        )

        Vitest.test (
            "An expansion during a running next page request neither repeats the request nor adds its page twice",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1"; gap "g1" |])
                let generation = (diffOf state).Generation
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" None [| hunk "h2" |]))

                fake.ExpandReply <-
                    fun _ ->
                        succeeded (
                            ResumablePartsDto.Ready [|
                                DiffPartDto.ExpandedContext("g1", [| changedRow "e1" |])
                            |]
                        )

                let step msg state =
                    update fake.Dependencies fake.SetPageState (diffMsg msg) state

                let loading, loadCmd = step (GitDiffMsg.LoadNext generation) state
                let expanding, expandCmd = step (GitDiffMsg.Expand(generation, "g1", true)) loading
                let again, againCmd = step (GitDiffMsg.LoadNext generation) expanding

                let! pageMessages = collectMessages loadCmd
                let! expandMessages = collectMessages expandCmd
                let! repeatedMessages = collectMessages againCmd
                let current = ref again

                // The page answer is delivered twice, as if the same cursor had been read twice.
                for message in
                    Array.concat [
                        pageMessages
                        repeatedMessages
                        pageMessages
                        expandMessages
                    ] do
                    let! next = run fake message current.Value
                    current.Value <- next

                let page = diffOf current.Value

                Vitest.expect(fake.Reads.Count).toBe (1)
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "hunk:h1"; "expanded:g1:1" |])
                Vitest.expect(page.NextRequest).toEqual (None)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "A repeated line slice request is sent once, and an overlapping answer appends only the text beyond the displayed end",
            fun () -> promise {
                let fake = FakeDiffClient()

                let sliceLine (offset: string) (text: string) (total: string option) highlights : DiffLineDto = {
                    Number = "0"
                    Ending = LineEndingDto.LF
                    Slice = {
                        OffsetUtf16 = offset
                        TotalUtf16 = total
                        Text = text
                        Highlights = highlights
                    }
                }

                let longRow = {
                    changedRow "r1" with
                        Current = Some(sliceLine "0" "abc" None [||])
                }

                let! state = openFirstPage fake (diffPage "p1" None [| hunkWith "h1" [| longRow |] |])
                let generation = (diffOf state).Generation
                fake.LineReply <- fun _ -> succeeded (ResumableLineDto.Ready(sliceLine "3" "def" None [||]))

                let request =
                    diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 3.0))

                let first, firstCmd = update fake.Dependencies fake.SetPageState request state
                let second, secondCmd = update fake.Dependencies fake.SetPageState request first
                let! messages = collectMessages (Cmd.batch [ firstCmd; secondCmd ])

                Vitest.expect(fake.Lines.Count).toBe (1)
                Vitest.expect((diffOf second).PendingLineSlices.Length).toBe (1)

                let current = ref second

                for message in messages do
                    let! next = run fake message current.Value
                    current.Value <- next

                let answer line =
                    diffMsg (
                        GitDiffMsg.LineCompleted(
                            generation,
                            fake.Lines.[0],
                            Ok(succeeded (ResumableLineDto.Ready line))
                        )
                    )

                let overlapping =
                    sliceLine "3" "defghi" (Some "9") [|
                        {
                            Start = 4
                            Length = 2
                            Kind = HighlightKindDto.ChangedText
                        }
                    |]

                let! state = run fake (answer overlapping) current.Value
                let! state = run fake (answer (sliceLine "0" "abc" (Some "9") [||])) state
                let line = currentLine (diffOf state)

                Vitest.expect(line.Text).toBe ("abcdefghi")
                Vitest.expect(line.TotalUtf16).toEqual (Some 9.0)

                Vitest
                    .expect(
                        line.Highlights
                        |> Array.map (fun highlight -> highlight.Start, highlight.Length)
                    )
                    .toEqual ([| 7, 2 |])

                Vitest.expect((diffOf state).PendingLineSlices).toEqual ([])
            }
        )

        Vitest.test (
            "A Scanning open answer that arrives after the user left the diff is abandoned once",
            fun () -> promise {
                let fake = FakeDiffClient()

                fake.OpenReply <-
                    fun request ->
                        match request.Continuation with
                        | None -> succeeded (ResumableOpenDto.Scanning(progress false, "open-continuation", None))
                        | Some _ -> failedWith "operation_canceled" None

                let opening, openCmd =
                    update fake.Dependencies fake.SetPageState (select "a.txt") runningState

                let! left = run fake (diffMsg (GitDiffMsg.PageStateObserved(Some(PageState.TextPage "notes")))) opening
                let! lateMessages = collectMessages openCmd
                let current = ref left

                for message in lateMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                let abandons =
                    fake.Opens
                    |> Seq.filter (fun request -> request.Continuation = Some "open-continuation")
                    |> Seq.toArray

                Vitest.expect(abandons.Length).toBe (1)

                Vitest
                    .expect(
                        {
                            abandons.[0] with
                                OperationId = ""
                                Continuation = None
                        }
                    )
                    .toEqual ({ fake.Opens.[0] with OperationId = "" })

                Vitest
                    .expect(
                        fake.Cancels
                        |> Seq.filter (fun id -> id = abandons.[0].OperationId)
                        |> Seq.length
                    )
                    .toBe (1)

                Vitest.expect(current.Value.DiffPage).toEqual (None)
            }
        )

        Vitest.test (
            "Expansions on one page stay within 8 MiB by collapsing the least recently expanded gap, which expands again",
            fun () -> promise {
                let fake = FakeDiffClient()
                let gaps = [| for index in 1..5 -> gap $"g{index}" |]
                let! state = openFirstPage fake (diffPage "p1" None (Array.append [| hunk "h1" |] gaps))
                let generation = (diffOf state).Generation
                let bigText = String.replicate (2 * 1024 * 1024) "x"

                fake.ExpandReply <-
                    fun request ->
                        let row = {
                            changedRow $"{request.GapId}-row" with
                                Current = Some(textLine 0 bigText)
                        }

                        succeeded (ResumablePartsDto.Ready [| DiffPartDto.ExpandedContext(request.GapId, [| row |]) |])

                let loadedBytes (page: GitDiffPageData) =
                    page.Pages
                    |> Array.filter (fun windowPage -> not windowPage.IsEvicted)
                    |> Array.sumBy _.PayloadBytes

                let current = ref state

                for gapId in [ "g1"; "g2"; "g3"; "g4"; "g5"; "g1" ] do
                    let! next = run fake (diffMsg (GitDiffMsg.Expand(generation, gapId, true))) current.Value
                    current.Value <- next
                    Vitest.expect(loadedBytes (diffOf next)).toBeLessThanOrEqual (GitDiffPageLoader.MaxLoadedBytes)

                let page = diffOf current.Value

                Vitest
                    .expect(fake.Expands |> Seq.map _.GapId |> Seq.toArray)
                    .toEqual ([| "g1"; "g2"; "g3"; "g4"; "g5"; "g1" |])

                Vitest
                    .expect(pageParts page.Pages.[0])
                    .toEqual (
                        [|
                            "hunk:h1"
                            "expanded:g1:1"
                            "gap:g2"
                            "gap:g3"
                            "expanded:g4:1"
                            "expanded:g5:1"
                        |]
                    )
            }
        )

        Vitest.test (
            "Expanding the remainder of one gap again and again collapses the oldest rows, which expand again with the same lines",
            fun () -> promise {
                let fake = FakeDiffClient()

                let familyGap (index: int) =
                    DiffPartDto.HiddenEqual {
                        GapId = $"g{index}"
                        PreviousRange = lineRange (string index) (string (1000 - index))
                        CurrentRange = lineRange (string index) (string (1000 - index))
                    }

                let! state = openFirstPage fake (diffPage "p1" None [| hunk "h1"; familyGap 0 |])
                let generation = (diffOf state).Generation
                // Both sides carry the text, so each expansion adds a little over 2 MiB.
                let bigText = String.replicate (1024 * 1024) "x"

                // The library answers an already expanded gap with its recorded first result.
                let recorded = Dictionary<string, ResumablePartsDto>()

                fake.ExpandReply <-
                    fun request ->
                        let index = int (request.GapId.Replace("g", ""))

                        if not (recorded.ContainsKey request.GapId) then
                            let row: DiffRowDto = {
                                Id = $"row-{index}"
                                Kind = DiffRowKindDto.Context
                                Previous = Some(textLine index bigText)
                                Current = Some(textLine index bigText)
                            }

                            recorded.[request.GapId] <-
                                ResumablePartsDto.Ready [|
                                    DiffPartDto.ExpandedContext(request.GapId, [| row |])
                                    familyGap (index + 1)
                                |]

                        succeeded recorded.[request.GapId]

                let loadedBytes (page: GitDiffPageData) =
                    page.Pages
                    |> Array.filter (fun windowPage -> not windowPage.IsEvicted)
                    |> Array.sumBy _.PayloadBytes

                let newestChunkBytes (page: GitDiffPageData) =
                    page.Pages.[0].ExpandedGaps |> List.last |> _.PayloadBytes

                let expanded (page: GitDiffPageData) (gapId: string) =
                    page.Pages.[0].Parts
                    |> Array.pick (
                        function
                        | Paged.PagedPart.ExpandedRows(id, rows) when id = gapId -> Some rows
                        | _ -> None
                    )

                let current = ref state

                for gapId in [ "g0"; "g1"; "g2"; "g3"; "g4"; "g0" ] do
                    let! next = run fake (diffMsg (GitDiffMsg.Expand(generation, gapId, true))) current.Value
                    current.Value <- next
                    let page = diffOf next

                    Vitest
                        .expect(loadedBytes page)
                        .toBeLessThanOrEqual (GitDiffPageLoader.MaxLoadedBytes + newestChunkBytes page)

                    if gapId = "g4" then
                        Vitest
                            .expect(pageParts page.Pages.[0])
                            .toEqual (
                                [|
                                    "hunk:h1"
                                    "gap:g0"
                                    "gap:g1"
                                    "expanded:g2:1"
                                    "expanded:g3:1"
                                    "expanded:g4:1"
                                    "gap:g5"
                                |]
                            )

                        Vitest
                            .expect(
                                page.Pages.[0].Parts
                                |> Array.pick (
                                    function
                                    | Paged.PagedPart.HiddenGap("g0", previous, current) -> Some(previous, current)
                                    | _ -> None
                                )
                            )
                            .toEqual (
                                ({ Start = 0.0; Count = 1.0 }: Paged.PagedRange),
                                ({ Start = 0.0; Count = 1.0 }: Paged.PagedRange)
                            )

                let page = diffOf current.Value
                let rows = expanded page "g0"

                Vitest
                    .expect(pageParts page.Pages.[0])
                    .toEqual (
                        [|
                            "hunk:h1"
                            "expanded:g0:1"
                            "gap:g1"
                            "gap:g2"
                            "expanded:g3:1"
                            "expanded:g4:1"
                            "gap:g5"
                        |]
                    )

                Vitest.expect(rows |> Array.map _.Id).toEqual ([| "row-0" |])

                Vitest
                    .expect(rows |> Array.map (fun row -> row.Current |> Option.map _.Number))
                    .toEqual ([| Some 0.0 |])
            }
        )

        Vitest.test (
            "No changes is shown only when the scan and the output are complete and no hunk exists",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" None [| gap "g1" |])
                let complete = diffOf state

                let withParts parts = {
                    complete with
                        Pages = [|
                            {
                                complete.Pages.[0] with
                                    Parts = parts |> Array.map Renderer.GitDiffPresentation.part
                            }
                        |]
                }

                Vitest.expect(noChangesShown complete).toBe (true)

                Vitest.expect(noChangesShown { complete with OutputComplete = false }).toBe (false)

                Vitest
                    .expect(
                        noChangesShown {
                            complete with
                                Progress = Some(progress false)
                        }
                    )
                    .toBe (false)

                Vitest.expect(noChangesShown (withParts [| hunk "h1" |])).toBe (false)

                let evicted = {
                    complete with
                        Pages = [|
                            {
                                complete.Pages.[0] with
                                    Parts = [| Paged.PagedPart.EvictedPage("p1", 3) |]
                            }
                        |]
                }

                Vitest.expect(noChangesShown evicted).toBe (false)
            }
        )

        Vitest.test (
            "An expired session during a next page request reopens the diff and reads forward to the requested page",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunkAt $"h{index}" (index * 10) |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" && request.Cursor = "cursor-2" then
                            failedWith "diff_session_closed" None
                        else
                            let index = int (request.Cursor.Replace("cursor-", "")) + 1
                            succeeded (ResumablePageDto.Ready(pageDto index))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                Vitest.expect((diffOf state).RequestedPageIndex).toBe (1)

                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)

                Vitest
                    .expect({ fake.Opens.[1] with OperationId = "" })
                    .toEqual ({ fake.Opens.[0] with OperationId = "" })

                Vitest
                    .expect(fake.Reads |> Seq.map (fun read -> read.HandleId, read.Cursor) |> Seq.toArray)
                    .toEqual (
                        [|
                            "diff-1", "cursor-1"
                            "diff-1", "cursor-2"
                            "diff-2", "cursor-1"
                        |]
                    )

                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(page.RequestedPageIndex).toBe (1)
                Vitest.expect(page.NextCursor).toEqual (Some "cursor-2")
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(state.DiffReopen).toEqual (None)
                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| "diff-1" |])
            }
        )

        Vitest.test (
            "An expired session during an expansion reopens the diff with the gap collapsed",
            fun () -> promise {
                let fake = FakeDiffClient()
                let first = diffPage "p1" None [| hunk "h1"; gap "g1" |]
                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) first
                fake.ExpandReply <- fun _ -> failedWith "diff_session_closed" None

                let! state = run fake (select "a.txt") runningState
                let! state = run fake (diffMsg (GitDiffMsg.Expand((diffOf state).Generation, "g1", true))) state
                let page = diffOf state

                Vitest.expect(fake.Expands.Count).toBe (1)
                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(fake.Opens.[1].PreparationTokenId).toEqual (None)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Pages |> Array.map pageParts).toEqual ([| [| "hunk:h1"; "gap:g1" |] |])
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "A stale preparation token after an encoding pick reopens with the chosen encoding and no token",
            fun () -> promise {
                let fake = FakeDiffClient()
                let token: PreparationTokenDto = { Id = "token-1" }

                let candidates: EncodingCandidateDto[] = [|
                    {
                        Encoding = "windows-1252"
                        Preview = "a"
                    }
                |]

                fake.OpenReply <-
                    fun request ->
                        match request.PreviousEncoding, request.PreparationTokenId with
                        | None, _ ->
                            succeeded (
                                ResumableOpenDto.Ready(
                                    OpenDiffResultDto.NotDiffable(
                                        DiffBlockerDto.EncodingRequired(DiffSideDto.Previous, token, candidates)
                                    )
                                )
                            )
                        | Some _, Some _ -> failedWith "preparation_mismatch" None
                        | Some _, None -> openedPage (diffPage "p1" None [| hunk "h1" |])

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation

                let! state =
                    run
                        fake
                        (diffMsg (GitDiffMsg.ChooseEncoding(generation, DiffSideDto.Previous, "windows-1252")))
                        state

                Vitest.expect(fake.Opens.Count).toBe (3)
                Vitest.expect(fake.Opens.[1].PreparationTokenId).toEqual (Some token.Id)
                Vitest.expect(fake.Opens.[2].PreparationTokenId).toEqual (None)
                Vitest.expect(fake.Opens.[2].PreviousEncoding).toEqual (Some "windows-1252")
                Vitest.expect(fake.Opens.[2].Continuation).toEqual (None)
                Vitest.expect((diffOf state).Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect((diffOf state).Handle).toEqual (Some handle)
            }
        )

        Vitest.test (
            "A reopen whose session closes again settles the page as failed without reopening a second time",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunkAt $"h{index}" (index * 10) |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        match request.HandleId, request.Cursor with
                        | "diff-1", "cursor-1" -> succeeded (ResumablePageDto.Ready(pageDto 2))
                        | _ -> failedWith "diff_session_closed" None

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)

                Vitest
                    .expect(fake.Reads |> Seq.map (fun read -> read.HandleId, read.Cursor) |> Seq.toArray)
                    .toEqual (
                        [|
                            "diff-1", "cursor-1"
                            "diff-1", "cursor-2"
                            "diff-2", "cursor-1"
                        |]
                    )

                Vitest.expect(isFailed page.Status).toBe (true)
                Vitest.expect(state.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "Leaving the diff while it reopens cancels the reopen and closes the handle it returns",
            fun () -> promise {
                let fake = FakeDiffClient()
                let first = diffPage "p1" None [| hunk "h1"; gap "g1" |]
                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) first
                fake.ExpandReply <- fun _ -> failedWith "diff_session_closed" None

                let! state = run fake (select "a.txt") runningState

                let expanding, expandCmd =
                    update
                        fake.Dependencies
                        fake.SetPageState
                        (diffMsg (GitDiffMsg.Expand((diffOf state).Generation, "g1", true)))
                        state

                let! expandMessages = collectMessages expandCmd
                let reopeningRef = ref expanding
                let reopenCmds = ResizeArray<Cmd<Msg>>()

                // The commands of the expansion answer hold the reopen and stay unrun until the user left.
                for message in expandMessages do
                    let next, cmd =
                        update fake.Dependencies fake.SetPageState message reopeningRef.Value

                    reopeningRef.Value <- next
                    reopenCmds.Add cmd

                let reopening = reopeningRef.Value
                let reopenId = (diffOf reopening).RunningOperations |> List.head
                Vitest.expect(reopening.DiffReopen.IsSome).toBe (true)

                let! left =
                    run fake (diffMsg (GitDiffMsg.PageStateObserved(Some(PageState.TextPage "notes")))) reopening

                let! lateMessages = collectMessages (Cmd.batch (List.ofSeq reopenCmds))
                let current = ref left

                for message in lateMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                Vitest.expect(fake.Opens.[1].OperationId).toBe (reopenId)
                Vitest.expect(fake.Cancels.Contains reopenId).toBe (true)
                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| "diff-1"; "diff-2" |])
                Vitest.expect(current.Value.DiffPage).toEqual (None)
            }
        )

        Vitest.test (
            "A reopen keeps the rows on screen and reads forward to the viewed page without evicting its neighborhood",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (if index < 10 then Some $"cursor-{index}" else None) [|
                        hunkAt $"h{index}" (index * 10)
                        gap $"g{index}"
                    |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.ExpandReply <- fun _ -> failedWith "diff_session_closed" None

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let current = ref state

                for _ in 2..10 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).Pages.Length).toBe (10)
                let statesBefore = fake.PageStates.Count

                // The user expands a gap on the sixth page after the session expired.
                let! state = run fake (diffMsg (GitDiffMsg.Expand(generation, "g6", true))) current.Value
                let page = diffOf state

                let shownWhileReopening =
                    fake.PageStates
                    |> Seq.skip statesBefore
                    |> Seq.choose (
                        function
                        | Some(PageState.GitDiffPage shown) ->
                            match shown.Status with
                            | GitDiffPageStatus.Reopening _ -> Some shown
                            | _ -> None
                        | _ -> None
                    )
                    |> Seq.toArray

                Vitest.expect(shownWhileReopening.Length).toBeGreaterThan (0)

                for shown in shownWhileReopening do
                    Vitest.expect(shown.RequestedPageIndex).toBe (5)
                    Vitest.expect(shown.Pages.Length).toBe (10)

                    Vitest
                        .expect([| for index in 4..6 -> shown.Pages.[index].IsEvicted |])
                        .toEqual ([| false; false; false |])

                Vitest.expect(fake.Replays.Count).toBe (0)
                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.RequestedPageIndex).toBe (5)
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| for index in 1..6 -> $"p{index}" |])
                Vitest.expect(pageParts page.Pages.[5]).toEqual ([| "hunk:h6"; "gap:g6" |])
                Vitest.expect(state.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "An abandoned Scanning open whose answer carries a handle closes that handle",
            fun () -> promise {
                let fake = FakeDiffClient()

                fake.OpenReply <-
                    fun request ->
                        match request.Continuation with
                        | None -> succeeded (ResumableOpenDto.Scanning(progress false, "open-continuation", None))
                        | Some _ -> openedPage (diffPage "p1" None [| hunk "h1" |])

                let opening, openCmd =
                    update fake.Dependencies fake.SetPageState (select "a.txt") runningState

                let! left = run fake (diffMsg (GitDiffMsg.PageStateObserved(Some(PageState.TextPage "notes")))) opening
                let! lateMessages = collectMessages openCmd
                let current = ref left

                for message in lateMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                do! Promise.sleep 0

                Vitest
                    .expect(
                        fake.Opens
                        |> Seq.filter (fun request -> request.Continuation = Some "open-continuation")
                        |> Seq.length
                    )
                    .toBe (1)

                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| handle.Id |])
                Vitest.expect(current.Value.DiffPage).toEqual (None)
            }
        )

        Vitest.test (
            "A diff answer that arrives after the user went to another page leaves that page shown",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" None [| hunk "h2" |]))

                let loading, loadCmd =
                    update
                        fake.Dependencies
                        fake.SetPageState
                        (diffMsg (GitDiffMsg.LoadNext (diffOf state).Generation))
                        state

                let! pageMessages = collectMessages loadCmd
                // Another part of the app shows a text page before the workflow observes it.
                fake.SetPageState(Some(PageState.TextPage "notes"))
                let current = ref loading

                for message in pageMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).Pages.Length).toBe (2)
                Vitest.expect(fake.CurrentPageState).toEqual (Some(PageState.TextPage "notes"))
            }
        )

        Vitest.test (
            "Two failed requests and a late page on the old handle during a reopen lead to one reopen without old parts",
            fun () -> promise {
                let fake = FakeDiffClient()

                let first =
                    diffPage "p1" (Some "cursor-1") [| hunk "h1"; gap "g1"; hunk "h2"; gap "g2" |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) first
                fake.ExpandReply <- fun _ -> failedWith "diff_session_closed" None
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" None [| hunk "old-h3" |]))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation

                let step msg state =
                    update fake.Dependencies fake.SetPageState (diffMsg msg) state

                let expandingFirst, firstCmd =
                    step (GitDiffMsg.Expand(generation, "g1", true)) state

                let expandingBoth, secondCmd =
                    step (GitDiffMsg.Expand(generation, "g2", true)) expandingFirst

                let loading, readCmd = step (GitDiffMsg.LoadNext generation) expandingBoth
                let! firstFailure = collectMessages firstCmd
                let! secondFailure = collectMessages secondCmd
                let! latePage = collectMessages readCmd

                // The first failure starts the reopen, whose open stays unanswered while the
                // other answers of the old handle arrive.
                let reopening, reopenCmd =
                    update fake.Dependencies fake.SetPageState firstFailure.[0] loading

                let current = ref reopening

                for message in Array.append secondFailure latePage do
                    let! next = run fake message current.Value
                    current.Value <- next

                let! reopenMessages = collectMessages reopenCmd

                for message in reopenMessages do
                    let! next = run fake message current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)

                Vitest
                    .expect(page.Pages |> Array.map pageParts)
                    .toEqual ([| [| "hunk:h1"; "gap:g1"; "hunk:h2"; "gap:g2" |] |])

                Vitest.expect(current.Value.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "An expired session during a replay reopens the diff up to the replayed page",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunk $"h{index}" |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.ReplayReply <- fun _ -> failedWith "diff_session_closed" None

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let current = ref state

                for _ in 2..9 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).RequestedPageIndex).toBe (8)
                Vitest.expect((diffOf current.Value).Pages.[0].IsEvicted).toBe (true)
                let readsBefore = fake.Reads.Count

                let! state = run fake (diffMsg (GitDiffMsg.Replay(generation, "p1", [ "p1" ]))) current.Value
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(fake.Reads.Count).toBe (readsBefore)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1" |])
                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "hunk:h1" |])
                Vitest.expect(page.RequestedPageIndex).toBe (0)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "The dependency problem lists the Git LFS setup only while git-lfs itself works",
            fun () ->
                let status
                    (name: string)
                    (installed: bool)
                    (compatible: bool)
                    (version: string option)
                    (remediation: string option)
                    : DependencyStatusDto =
                    {
                        Component = name
                        Installed = installed
                        Version = version
                        Compatible = compatible
                        Remediation = remediation
                    }

                let message =
                    Renderer.Components.LeftSidebar.Git.GitSidebarPanel.dependencyProblemMessage

                let lineCount (text: string option) =
                    text |> Option.map (fun value -> value.Split('\n').Length)

                let git = status "git" true true (Some "git version 2.50.0") None
                let working = status "git-lfs" true true (Some "git-lfs/3.7.0") None

                // The filter is not set up while git-lfs works. The line is about the setup and
                // carries the remediation, and no line says that a component was not found.
                let unconfigured =
                    message [|
                        git
                        working
                        status "git-lfs-configuration" false false None (Some "configure the filter")
                    |]

                Vitest.expect(lineCount unconfigured).toEqual (Some 1)
                Vitest.expect(unconfigured.Value.Contains "configure the filter").toBe (true)
                Vitest.expect(unconfigured.Value.Contains "git-lfs-configuration").toBe (false)

                // A missing git-lfs gets its own line, and the setup adds none.
                let missing =
                    message [|
                        git
                        status "git-lfs" false false None (Some "install it")
                        status "git-lfs-configuration" false false None (Some "configure later")
                    |]

                Vitest.expect(lineCount missing).toEqual (Some 1)
                Vitest.expect(missing.Value.Contains "install it").toBe (true)
                Vitest.expect(missing.Value.Contains "configure later").toBe (false)

                // So does a git-lfs that is too old, whatever the state of the filter.
                let tooOld =
                    message [|
                        git
                        status "git-lfs" true false (Some "git-lfs/3.4.0") (Some "upgrade it")
                        status "git-lfs-configuration" true false None (Some "configure later")
                    |]

                Vitest.expect(lineCount tooOld).toEqual (Some 1)
                Vitest.expect(tooOld.Value.Contains "3.4.0").toBe (true)
                Vitest.expect(tooOld.Value.Contains "configure later").toBe (false)

                Vitest
                    .expect(
                        message [|
                            git
                            working
                            status "git-lfs-configuration" true true None None
                        |]
                    )
                    .toEqual (None)
        )

        Vitest.test (
            "Selecting a conflicted change keeps the open diff and its handle until another page replaces it",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" None [| hunk "h1" |])

                let dependencies = {
                    fake.Dependencies with
                        getStatus = fun _ -> promise { return Error "no status" }
                }

                let conflicted = {
                    change "c.txt" with
                        IsConflicted = true
                }

                let requested, requestCmd =
                    update dependencies fake.SetPageState (SelectChangeRequested(conflicted, ignore)) state

                let! messages = collectMessages requestCmd

                // The diff stays while the page loads, so nothing on screen has lost its handle.
                Vitest.expect(requested.DiffPage.IsSome).toBe (true)
                Vitest.expect(fake.Closes.Count).toBe (0)

                // A load that fails leaves the diff as it was.
                let failed, failedCmd = update dependencies fake.SetPageState messages.[0] requested
                let! _ = collectMessages failedCmd
                Vitest.expect(failed.DiffPage.IsSome).toBe (true)
                Vitest.expect(fake.Closes.Count).toBe (0)

                // Once the page state shows another page, the diff leaves and its handle closes.
                let left, leftCmd =
                    update
                        dependencies
                        fake.SetPageState
                        (diffMsg (GitDiffMsg.PageStateObserved(Some(PageState.TextPage "other"))))
                        failed

                let! _ = collectMessages leftCmd
                Vitest.expect(left.DiffPage).toEqual (None)
                Vitest.expect(fake.Closes.Count).toBe (1)
            }
        )

        Vitest.test (
            "A page load of a closed ARC stays dropped after a refresh without an ARC and the opening of another ARC",
            fun () -> promise {
                let fake = FakeDiffClient()
                let shown = ResizeArray<PageState option>()

                let step (message: Msg) (state: GitState) =
                    update fake.Dependencies shown.Add message state

                let conflicted = {
                    change "c.txt" with
                        IsConflicted = true
                }

                // The first ARC starts loading the page of a conflicted change.
                let selecting, _ = step (SelectChangeRequested(conflicted, ignore)) runningState
                let loadId = selecting.PageLoadRequestId

                // That ARC closes, a refresh runs while no ARC is open, and another ARC opens.
                let closed, _ = step (ArcPathChanged None) selecting
                let refreshed, _ = step RefreshRequested closed
                let other, _ = step (ArcPathChanged(Some "C:/other")) refreshed

                // The load of the first ARC finishes late and must not count for the new ARC.
                let late, lateCmd =
                    step
                        (SelectChangeCompleted(
                            loadId,
                            "c.txt",
                            ignore,
                            Ok(GitPageChange.Set(PageState.TextPage "stale"))
                        ))
                        other

                let! _ = collectMessages lateCmd
                Vitest.expect(shown.Count).toBe (0)
                Vitest.expect(late.SelectedChangePath).toEqual (None)
            }
        )

        Vitest.test (
            "A request for the next page on a paused diff reopens it and lands on the last page read, not on the page asked for first",
            fun () -> promise {
                let fake = FakeDiffClient()

                // Page n shows the line 10 n. The first session closes when the indexing asks for the fourth page.
                let pageDto (index: int) =
                    diffPage $"p{index}" (if index < 6 then Some $"cursor-{index}" else None) [|
                        hunkAt $"h{index}" (index * 10)
                    |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" && request.Cursor = "cursor-3" then
                            failedWith "diff_session_closed" None
                        else
                            succeeded (
                                ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1))
                            )

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state

                // Three pages are read, and the page the user asked for last is still the first.
                Vitest.expect((diffOf state).Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2"; "p3" |])
                Vitest.expect((diffOf state).IndexingPaused).toBe (true)
                Vitest.expect((diffOf state).RequestedPageIndex).toBe (0)

                // The user scrolls to the end of the pages read, and the viewer asks for the next page.
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.RequestedPageIndex).toBe (2)

                Vitest
                    .expect(page.ScrollTarget |> Option.map (fun target -> target.Side, target.Line))
                    .toEqual (Some(Paged.PagedDiffSide.Previous, 30.0))
            }
        )

        Vitest.test (
            "A replay of the last page after the idle close reopens the diff and lands on the first line of that page",
            fun () -> promise {
                let fake = FakeDiffClient()
                let total = 40

                // Page n shows the line 10 n, so a page reaches a line only when it is at or past it.
                let pageDto (index: int) =
                    diffPage $"p{index}" (if index < total then Some $"cursor-{index}" else None) [|
                        hunkAt $"h{index}" (index * 10)
                    |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                // The first session is closed for good. A replay on it answers diff_session_closed.
                fake.ReplayReply <-
                    fun request ->
                        if request.HandleId = "diff-1" && request.PageId = $"p{total}" then
                            failedWith "diff_session_closed" None
                        else
                            succeeded (pageDto (int (request.PageId.Replace("p", ""))))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation

                // The diff is fully indexed, and the window of loaded pages holds the last eight.
                let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect((diffOf state).Pages.Length).toBe (total)
                Vitest.expect((diffOf state).NextCursor).toEqual (None)

                // The user read near the start, which evicted the last page.
                let! state = run fake (diffMsg (GitDiffMsg.Replay(generation, "p3", [ "p3" ]))) state
                Vitest.expect((diffOf state).Pages.[total - 1].IsEvicted).toBe (true)
                Vitest.expect(fake.Opens.Count).toBe (1)

                // End asks for the last page, and the session behind the handle is gone.
                let! state = run fake (diffMsg (GitDiffMsg.Replay(generation, $"p{total}", [ $"p{total}" ]))) state
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.Pages.Length).toBe (total)
                Vitest.expect(page.RequestedPageIndex).toBe (total - 1)
                Vitest.expect(page.Pages.[total - 1].IsEvicted).toBe (false)

                Vitest
                    .expect(page.ScrollTarget |> Option.map (fun target -> target.Side, target.Line))
                    .toEqual (Some(Paged.PagedDiffSide.Previous, float (total * 10)))

                Vitest.expect(state.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "An expired session during a line slice reopens the diff up to the page that shows the line",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto (index: int) =
                    let row = {
                        changedRow $"r{index}" with
                            Current =
                                Some {
                                    textLine (index * 10) "abc" with
                                        Slice = {
                                            OffsetUtf16 = "0"
                                            TotalUtf16 = None
                                            Text = "abc"
                                            Highlights = [||]
                                        }
                                }
                    }

                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunkWith $"h{index}" [| row |] |]

                fake.OpenReply <- fun _ -> openedOn (handleOfOpen fake.Opens.Count) (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.LineReply <- fun _ -> failedWith "diff_session_closed" None

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let current = ref state

                for _ in 2..4 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).RequestedPageIndex).toBe (3)

                // Line 20 is on the second page.
                let! state =
                    run
                        fake
                        (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 20.0, 3.0)))
                        current.Value

                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)

                Vitest
                    .expect(
                        fake.Reads
                        |> Seq.filter (fun read -> read.HandleId = "diff-2")
                        |> Seq.map _.Cursor
                        |> Seq.toArray
                    )
                    .toEqual ([| "cursor-1" |])

                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(page.RequestedPageIndex).toBe (1)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "Line slices merged past 8 MiB evict the page farthest from the line",
            fun () -> promise {
                let fake = FakeDiffClient()

                let longRow = {
                    changedRow "r1" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "0"
                                        TotalUtf16 = None
                                        Text = "abc"
                                        Highlights = [||]
                                    }
                            }
                }

                let! state = openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunkWith "h1" [| longRow |] |])
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(diffPage "p2" None [| hunk "h2" |]))
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf state).Generation)) state
                let generation = (diffOf state).Generation
                let sliceLength = 3 * 1024 * 1024

                fake.LineReply <-
                    fun request ->
                        succeeded (
                            ResumableLineDto.Ready {
                                Number = request.Line
                                Ending = LineEndingDto.LF
                                Slice = {
                                    OffsetUtf16 = request.OffsetUtf16
                                    TotalUtf16 = None
                                    Text = String.replicate sliceLength "y"
                                    Highlights = [||]
                                }
                            }
                        )

                let current = ref state

                for step in 0..2 do
                    let offset = 3.0 + float (step * sliceLength)

                    let! next =
                        run
                            fake
                            (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, offset)))
                            current.Value

                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect((currentLine page).Text.Length).toBe (3 + 3 * sliceLength)
                Vitest.expect(page.Pages |> Array.map _.IsEvicted).toEqual ([| false; true |])
            }
        )

        Vitest.test (
            "Expanding a second gap keeps the first one running, and a running gap is not requested again",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (diffPage "p1" None [| hunk "h1"; gap "g1"; hunk "h2"; gap "g2" |])
                let generation = (diffOf state).Generation

                fake.ExpandReply <-
                    fun request ->
                        succeeded (
                            ResumablePartsDto.Ready [|
                                DiffPartDto.ExpandedContext(request.GapId, [| changedRow $"{request.GapId}-row" |])
                            |]
                        )

                let step msg state =
                    update fake.Dependencies fake.SetPageState (diffMsg msg) state

                let expandingFirst, firstCmd =
                    step (GitDiffMsg.Expand(generation, "g1", true)) state

                let! _ = collectMessages firstCmd

                let expandingBoth, secondCmd =
                    step (GitDiffMsg.Expand(generation, "g2", false)) expandingFirst

                let! secondAnswer = collectMessages secondCmd

                let repeated, repeatedCmd =
                    step (GitDiffMsg.Expand(generation, "g1", false)) expandingBoth

                let! repeatedAnswer = collectMessages repeatedCmd

                Vitest.expect(fake.Expands |> Seq.map _.GapId |> Seq.toArray).toEqual ([| "g1"; "g2" |])
                Vitest.expect(repeatedAnswer.Length).toBe (0)
                Vitest.expect((diffOf repeated).ExpandingGaps |> List.sort).toEqual ([ "g1"; "g2" ])

                let current = ref repeated

                for message in secondAnswer do
                    let! next = run fake message current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).ExpandingGaps).toEqual ([ "g1" ])
            }
        )

        Vitest.test (
            "Replays of the pages in view never evict one another, so each visible page is replayed once",
            fun () -> promise {
                let fake = FakeDiffClient()

                let rows (index: int) = [|
                    for row in 0..2 ->
                        {
                            changedRow $"p{index}-r{row}" with
                                Current = Some(textLine (index * 3 + row) "new")
                        }
                |]

                let pageDto (index: int) =
                    diffPage $"p{index}" (if index < 20 then Some $"cursor-{index}" else None) [|
                        hunkWith $"h{index}" (rows index)
                    |]

                let! state = openFirstPage fake (pageDto 1)
                let generation = (diffOf state).Generation

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.ReplayReply <- fun request -> succeeded (pageDto (int (request.PageId.Replace("p", ""))))
                let current = ref state

                for _ in 2..20 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                // The viewport shows the first twelve pages, three rows each.
                let visible = [ for index in 1..12 -> $"p{index}" ]

                let firstVisibleEvicted () =
                    (diffOf current.Value).Pages |> Array.take 12 |> Array.tryFind _.IsEvicted

                Vitest.expect((firstVisibleEvicted ()).IsSome).toBe (true)
                let steps = ref 0

                while steps.Value < 40 && (firstVisibleEvicted ()).IsSome do
                    let pageId = (firstVisibleEvicted ()).Value.PageId
                    let! next = run fake (diffMsg (GitDiffMsg.Replay(generation, pageId, visible))) current.Value
                    current.Value <- next
                    steps.Value <- steps.Value + 1

                let replayed = fake.Replays |> Seq.map _.PageId |> Seq.toArray

                Vitest.expect(replayed.Length).toBeLessThanOrEqual (12)
                Vitest.expect(replayed |> Array.distinct).toEqual (replayed)
                Vitest.expect((firstVisibleEvicted ()).IsNone).toBe (true)

                // Once the requested page leaves the pages of the last replay request, they no
                // longer hold the window above its page limit.
                let loadedCount (page: GitDiffPageData) =
                    page.Pages
                    |> Array.filter (fun windowPage -> not windowPage.IsEvicted)
                    |> Array.length

                let settled = diffOf current.Value
                Vitest.expect(loadedCount settled).toBe (12)

                let movedAway =
                    GitDiffPageLoader.evict {
                        settled with
                            RequestedPageIndex = settled.Pages.Length - 1
                    }

                Vitest.expect(loadedCount movedAway).toBeLessThanOrEqual (GitDiffPageLoader.MaxLoadedPages)
            }
        )

        Vitest.test (
            "A diff of 2,000 pages of 1,000 rows renders at the capped scroll height, with the unloaded pages folded into one placeholder",
            TestOptions(timeout = 120000),
            fun () -> promise {
                let fake = FakeDiffClient()
                let rowsPerPage = 1000
                let pageCount = 2000

                let pageDto (index: int) =
                    let rows =
                        Array.init
                            rowsPerPage
                            (fun row -> {
                                changedRow $"p{index}-r{row}" with
                                    Current = Some(textLine (index * rowsPerPage + row) "new")
                            })

                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunkWith $"h{index}" rows |]

                let! state = openFirstPage fake (pageDto 1)

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                let current = ref state

                for _ in 2..pageCount do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext (diffOf current.Value).Generation)) current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(page.Pages.Length).toBe (pageCount)

                let rendered = renderTarget page
                let content = rendered.querySelector "[data-testid=\"renderer-git-diff-content\"]"

                let height =
                    System.Text.RegularExpressions.Regex
                        .Match(content.getAttribute "style", @"height:\s*([\d.]+)px")
                        .Groups.[1].Value
                    |> float

                // The rows are taller than the browser lays out, so the scroll content takes the cap.
                // Every page has a header row besides its rows.
                let displayRowsPerPage = rowsPerPage + 1
                Vitest.expect(float (pageCount * displayRowsPerPage * 28)).toBeGreaterThan (10000000.0)
                Vitest.expect(height).toBe (10000000.0)

                let folded = rendered.querySelector "[data-folded-side=\"earlier\"]"

                Vitest
                    .expect(folded.getAttribute "data-page-count")
                    .toBe (string (pageCount - GitDiffPageLoader.MaxLoadedPages))

                Vitest
                    .expect(folded.getAttribute "data-row-count")
                    .toBe (string ((pageCount - GitDiffPageLoader.MaxLoadedPages) * displayRowsPerPage))

                // Without a viewport the placeholder replays its first page.
                Vitest.expect(folded.getAttribute "data-next-page").toBe ("p1")
            }
        )

        Vitest.test (
            "Loading the text before a line slice reads up to the displayed start and puts it in front with shifted highlights",
            fun () -> promise {
                let fake = FakeDiffClient()

                let changed start length : HighlightDto = {
                    Start = start
                    Length = length
                    Kind = HighlightKindDto.ChangedText
                }

                let slicedRow = {
                    changedRow "r1" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "10000"
                                        TotalUtf16 = Some "10003"
                                        Text = "abc"
                                        Highlights = [| changed 1 1 |]
                                    }
                            }
                }

                let! state = openFirstPage fake (diffPage "p1" None [| hunkWith "h1" [| slicedRow |] |])

                // Every answer is as long as asked for, with a changed character at its start.
                fake.LineReply <-
                    fun request ->
                        succeeded (
                            ResumableLineDto.Ready {
                                Number = request.Line
                                Ending = LineEndingDto.LF
                                Slice = {
                                    OffsetUtf16 = request.OffsetUtf16
                                    TotalUtf16 = Some "10003"
                                    Text = String.replicate request.MaxUtf16 "x"
                                    Highlights = [| changed 0 1 |]
                                }
                            }
                        )

                let lineText (page: GitDiffPageData) =
                    (renderTarget page).querySelector "[data-testid=\"renderer-git-diff-line-text-current-0\"]"

                let beforeControl (page: GitDiffPageData) =
                    (renderTarget page).querySelector "[data-testid=\"renderer-git-diff-line-before-current-0\"]"

                Vitest.expect((lineText (diffOf state)).getAttribute "data-offset-utf16").toBe ("10000")
                Vitest.expect(isNull (beforeControl (diffOf state))).toBe (false)

                let generation = (diffOf state).Generation

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineBefore(generation, DiffSideDto.Current, 0.0, 10000.0))) state

                // The request overlaps the displayed text by one unit.
                Vitest.expect(fake.Lines.[0].OffsetUtf16).toBe ("1809")
                Vitest.expect(fake.Lines.[0].MaxUtf16).toBe (8192)

                let line = currentLine (diffOf state)
                Vitest.expect(line.OffsetUtf16).toBe (1809.0)
                Vitest.expect(line.Text).toBe (String.replicate 8191 "x" + "abc")

                Vitest
                    .expect(
                        line.Highlights
                        |> Array.map (fun highlight -> highlight.Start, highlight.Length)
                    )
                    .toEqual ([| 0, 1; 8192, 1 |])

                Vitest.expect((lineText (diffOf state)).getAttribute "data-offset-utf16").toBe ("1809")

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineBefore(generation, DiffSideDto.Current, 0.0, 1809.0))) state

                Vitest.expect(fake.Lines.[1].OffsetUtf16).toBe ("0")
                Vitest.expect(fake.Lines.[1].MaxUtf16).toBe (1810)

                let line = currentLine (diffOf state)
                Vitest.expect(line.Text.Length).toBe (10003)

                Vitest
                    .expect(line.Highlights |> Array.map (fun highlight -> highlight.Start))
                    .toEqual ([| 0; 1809; 10001 |])

                Vitest.expect((lineText (diffOf state)).getAttribute "data-offset-utf16").toBe ("0")
                Vitest.expect(isNull (beforeControl (diffOf state))).toBe (true)
            }
        )

        Vitest.test (
            "Loading the text before a slice reaches the line start when a request boundary falls inside a surrogate pair",
            fun () -> promise {
                let fake = FakeDiffClient()

                // One emoji takes the units 1807 and 1808, so a request from 1808 would split it.
                // The displayed slice starts with another emoji at 10000, so a request that ends
                // one unit after the displayed start splits that one.
                let lineText =
                    String.replicate 1807 "a"
                    + "\U0001F600"
                    + String.replicate 8191 "b"
                    + "\U0001F600"
                    + String.replicate 1998 "c"

                let isLowSurrogate (index: int) =
                    index > 0
                    && index < lineText.Length
                    && System.Char.IsLowSurrogate lineText.[index]

                let slice (offset: int) (text: string) : DiffLineDto = {
                    Number = "0"
                    Ending = LineEndingDto.LF
                    Slice = {
                        OffsetUtf16 = string offset
                        TotalUtf16 = Some(string lineText.Length)
                        Text = text
                        Highlights = [||]
                    }
                }

                let slicedRow = {
                    changedRow "r1" with
                        Current = Some(slice 10000 (lineText.Substring 10000))
                }

                let! state = openFirstPage fake (diffPage "p1" None [| hunkWith "h1" [| slicedRow |] |])

                // Like the library, a start inside a surrogate pair moves back by one unit with
                // the same length, and an end inside a pair moves back as well.
                fake.LineReply <-
                    fun request ->
                        let requested = int request.OffsetUtf16

                        let start =
                            if isLowSurrogate requested then
                                requested - 1
                            else
                                requested

                        let fullEnd = min lineText.Length (start + request.MaxUtf16)
                        let sliceEnd = if isLowSurrogate fullEnd then fullEnd - 1 else fullEnd
                        succeeded (ResumableLineDto.Ready(slice start (lineText.Substring(start, sliceEnd - start))))

                let generation = (diffOf state).Generation
                let current = ref state
                let requests = ref 0

                while (currentLine (diffOf current.Value)).OffsetUtf16 > 0.0 && requests.Value < 5 do
                    let displayedStart = (currentLine (diffOf current.Value)).OffsetUtf16

                    let! next =
                        run
                            fake
                            (diffMsg (GitDiffMsg.LoadLineBefore(generation, DiffSideDto.Current, 0.0, displayedStart)))
                            current.Value

                    current.Value <- next
                    requests.Value <- requests.Value + 1

                let line = currentLine (diffOf current.Value)
                Vitest.expect(line.OffsetUtf16).toBe (0.0)
                Vitest.expect(line.Text).toBe (lineText)
                Vitest.expect(requests.Value).toBe (2)
            }
        )

        Vitest.test (
            "A side that turns out not to be UTF-8 asks for its encoding without a token, and the choice reopens the diff with it",
            fun () -> promise {
                let fake = FakeDiffClient()

                let detail: DiffContentBlockedDto = {
                    Side = DiffSideDto.Current
                    Evidence = "byte 0x93 at 120"
                }

                fake.OpenReply <-
                    fun _ -> openedOn (handleOfOpen fake.Opens.Count) (diffPage "p1" (Some "cursor-1") [| hunk "h1" |])

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" then
                            failedWith "diff_encoding_mismatch" (Some detail)
                        else
                            succeeded (ResumablePageDto.Ready(diffPage "p2" None [| hunk "h2" |]))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state

                let candidates: EncodingCandidateDto[] = [|
                    { Encoding = "utf-8"; Preview = "" }
                    {
                        Encoding = "windows-1252"
                        Preview = ""
                    }
                |]

                Vitest
                    .expect((diffOf state).Status)
                    .toEqual (GitDiffPageStatus.EncodingChoice(DiffSideDto.Current, None, candidates))

                let! state =
                    run
                        fake
                        (diffMsg (GitDiffMsg.ChooseEncoding(generation, DiffSideDto.Current, "windows-1252")))
                        state

                let page = diffOf state
                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(fake.Opens.[1].CurrentEncoding).toEqual (Some "windows-1252")
                Vitest.expect(fake.Opens.[1].PreviousEncoding).toEqual (None)
                Vitest.expect(fake.Opens.[1].PreparationTokenId).toEqual (None)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(fake.Closes |> Seq.map _.HandleId |> Seq.toArray).toEqual ([| "diff-1" |])
            }
        )

        Vitest.test (
            "A next page that lands after the user went back and replayed an earlier page keeps that page loaded",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunk $"h{index}" |]

                let! state = openFirstPage fake (pageDto 1)
                let generation = (diffOf state).Generation

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.ReplayReply <- fun request -> succeeded (pageDto (int (request.PageId.Replace("p", ""))))
                let current = ref state

                for _ in 2..9 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).Pages.[0].IsEvicted).toBe (true)

                // The next page is asked for, and its answer waits while the user scrolls back up
                // and the first page is replayed.
                let loading, loadCmd =
                    update fake.Dependencies fake.SetPageState (diffMsg (GitDiffMsg.LoadNext generation)) current.Value

                let! heldPage = collectMessages loadCmd
                let! replayed = run fake (diffMsg (GitDiffMsg.Replay(generation, "p1", [ "p1"; "p2" ]))) loading
                Vitest.expect((diffOf replayed).Pages.[0].IsEvicted).toBe (false)
                current.Value <- replayed

                for message in heldPage do
                    let! next = run fake message current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| for index in 1..10 -> $"p{index}" |])
                Vitest.expect(page.Pages.[0].IsEvicted).toBe (false)
                Vitest.expect(page.Pages.[1].IsEvicted).toBe (false)
                Vitest.expect(page.RequestedPageIndex).toBe (0)
                Vitest.expect(page.NextCursor).toEqual (Some "cursor-10")
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "With small pages in a tall viewport, the reads and replays of the viewer stay bounded and settle",
            fun () -> promise {
                let fake = FakeDiffClient()
                let total = 40

                let pageDto index =
                    diffPage $"p{index}" (if index < total then Some $"cursor-{index}" else None) [| hunk $"h{index}" |]

                let! state = openFirstPage fake (pageDto 1)
                let generation = (diffOf state).Generation

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.ReplayReply <- fun request -> succeeded (pageDto (int (request.PageId.Replace("p", ""))))

                // Every row is in view. The viewer replays the first placeholder from the top,
                // the folded page next to the loaded rows first, and the continue row asks once
                // for each next page.
                let firstPlaceholder (page: GitDiffPageData) =
                    let loaded =
                        page.Pages
                        |> Array.indexed
                        |> Array.filter (fun (_, windowPage) -> not windowPage.IsEvicted)
                        |> Array.map fst

                    if loaded.Length = 0 then
                        None
                    else
                        let first = Array.head loaded
                        let last = Array.last loaded

                        let between =
                            page.Pages
                            |> Array.indexed
                            |> Array.tryFind (fun (index, windowPage) ->
                                windowPage.IsEvicted && index > first && index < last
                            )

                        match between with
                        | _ when first > 0 -> Some page.Pages.[first - 1].PageId
                        | Some(_, windowPage) -> Some windowPage.PageId
                        | None when last < page.Pages.Length - 1 -> Some page.Pages.[last + 1].PageId
                        | None -> None

                let current = ref state
                let requestedNext = ref None
                let settled = ref false
                let steps = ref 0

                while not settled.Value && steps.Value < 400 do
                    let page = diffOf current.Value

                    let visible =
                        page.Pages
                        |> Array.filter (fun windowPage -> not windowPage.IsEvicted)
                        |> Array.map _.PageId
                        |> List.ofArray

                    let replay = firstPlaceholder page
                    let wantsNext = page.NextCursor.IsSome && page.NextCursor <> requestedNext.Value

                    if replay.IsNone && not wantsNext then
                        settled.Value <- true
                    else
                        match replay with
                        | Some pageId ->
                            let! next =
                                run fake (diffMsg (GitDiffMsg.Replay(generation, pageId, visible))) current.Value

                            current.Value <- next
                        | None -> ()

                        if wantsNext then
                            requestedNext.Value <- page.NextCursor
                            let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                            current.Value <- next

                    steps.Value <- steps.Value + 1

                Vitest.expect(settled.Value).toBe (true)
                Vitest.expect(fake.Reads.Count).toBe (total - 1)
                Vitest.expect(fake.Replays.Count).toBeLessThanOrEqual (2 * total)
            }
        )

        Vitest.test (
            "A reopen reads forward to the page with the first line the user was reading when page boundaries differ",
            fun () -> promise {
                let fake = FakeDiffClient()

                // The first session reads pages of ten lines, the second one pages of five lines.
                let pageOf (handleId: string) (index: int) =
                    let size = if handleId = "diff-1" then 10 else 5
                    let first = (index - 1) * size

                    diffPage $"{handleId}-p{index}" (Some $"cursor-{index}") [|
                        hunkWith $"{handleId}-h{index}" [|
                            for line in first .. first + size - 1 -> rowAt $"{handleId}-r{line}" line
                        |]
                    |]

                fake.OpenReply <-
                    fun _ ->
                        let openHandle = handleOfOpen fake.Opens.Count
                        openedOn openHandle (pageOf openHandle.Id 1)

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" && request.Cursor = "cursor-3" then
                            failedWith "diff_session_closed" None
                        else
                            let index = int (request.Cursor.Replace("cursor-", "")) + 1
                            succeeded (ResumablePageDto.Ready(pageOf request.HandleId index))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let current = ref state

                // The third page shows the lines 20 to 29. Reading the fourth page finds the session closed.
                for _ in 1..3 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                let page = diffOf current.Value

                Vitest
                    .expect(
                        fake.Reads
                        |> Seq.filter (fun read -> read.HandleId = "diff-2")
                        |> Seq.map _.Cursor
                        |> Seq.toArray
                    )
                    .toEqual ([| "cursor-1"; "cursor-2"; "cursor-3"; "cursor-4" |])

                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| for index in 1..5 -> $"diff-2-p{index}" |])

                // The fifth page of the new session starts with line 20.
                Vitest.expect(page.RequestedPageIndex).toBe (4)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(current.Value.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "A failed expansion, line slice or replay marks its control and keeps the rows, and changed sources still end the diff",
            fun () -> promise {
                let fake = FakeDiffClient()

                let longRow = {
                    changedRow "long" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "0"
                                        TotalUtf16 = Some "9"
                                        Text = "abc"
                                        Highlights = [||]
                                    }
                            }
                }

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [|
                        hunkWith $"h{index}" [| { longRow with Id = $"long-{index}" } |]
                        gap $"g{index}"
                    |]

                let! state = openFirstPage fake (pageDto 1)
                let generation = (diffOf state).Generation

                fake.ExpandReply <-
                    fun request ->
                        if request.GapId = "g9" then
                            failedWith "source_changed" None
                        else
                            failedWith "git_failure" None

                fake.LineReply <- fun _ -> failedWith "git_failure" None
                fake.ReplayReply <- fun _ -> failedWith "git_failure" None

                let! state = run fake (diffMsg (GitDiffMsg.Expand(generation, "g1", true))) state

                let! state =
                    run fake (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 3.0))) state

                let page = diffOf state

                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(pageParts page.Pages.[0]).toEqual ([| "hunk:h1"; "gap:g1" |])
                Vitest.expect(page.FailedGaps).toEqual ([ "g1" ])

                Vitest
                    .expect(page.FailedLineSlices |> List.map (fun slice -> slice.Side, slice.Line))
                    .toEqual ([ Paged.PagedDiffSide.Current, 0.0 ])

                let rendered = renderTarget page

                Vitest
                    .expect(
                        (rendered.querySelector "[data-testid=\"renderer-git-diff-gap-expand-start-g1\"]").getAttribute
                            "data-failed"
                    )
                    .toBe ("true")

                Vitest
                    .expect(
                        (rendered.querySelector "[data-testid=\"renderer-git-diff-line-more-current-0\"]").getAttribute
                            "data-failed"
                    )
                    .toBe ("true")

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                let current = ref state

                for _ in 2..9 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).Pages.[0].IsEvicted).toBe (true)

                let! state = run fake (diffMsg (GitDiffMsg.Replay(generation, "p1", []))) current.Value
                let page = diffOf state
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.FailedReplays).toEqual ([ "p1" ])
                Vitest.expect(page.Pages.[0].IsEvicted).toBe (true)

                let! state = run fake (diffMsg (GitDiffMsg.Expand(generation, "g9", true))) state
                Vitest.expect((diffOf state).Status).toEqual (GitDiffPageStatus.SourceChanged)
            }
        )

        Vitest.test (
            "A reopen whose pages are much smaller than the old ones keeps the page with the target line loaded",
            fun () -> promise {
                let fake = FakeDiffClient()

                // The first session reads pages of ten lines, the second one pages of two lines.
                let pageOf (handleId: string) (index: int) =
                    let size = if handleId = "diff-1" then 10 else 2
                    let first = (index - 1) * size

                    diffPage $"{handleId}-p{index}" (Some $"cursor-{index}") [|
                        hunkWith $"{handleId}-h{index}" [|
                            for line in first .. first + size - 1 -> rowAt $"{handleId}-r{line}" line
                        |]
                    |]

                fake.OpenReply <-
                    fun _ ->
                        let openHandle = handleOfOpen fake.Opens.Count
                        openedOn openHandle (pageOf openHandle.Id 1)

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" && request.Cursor = "cursor-10" then
                            failedWith "diff_session_closed" None
                        else
                            let index = int (request.Cursor.Replace("cursor-", "")) + 1
                            succeeded (ResumablePageDto.Ready(pageOf request.HandleId index))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let current = ref state

                // The tenth page shows the lines 90 to 99. Reading the eleventh page finds the session closed.
                for _ in 1..10 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                let page = diffOf current.Value

                let shownWhileReopening =
                    fake.PageStates
                    |> Seq.choose (
                        function
                        | Some(PageState.GitDiffPage shown) ->
                            match shown.Status with
                            | GitDiffPageStatus.Reopening pagesRead -> Some(shown, pagesRead)
                            | _ -> None
                        | _ -> None
                    )
                    |> Seq.toArray

                Vitest.expect(shownWhileReopening.Length).toBeGreaterThan (0)

                // The old pages from the fourth to the tenth stay loaded until the new session
                // reads past them, and the requested page stays on the tenth.
                for shown, pagesRead in shownWhileReopening do
                    Vitest.expect(shown.RequestedPageIndex).toBe (9)

                    for index in max 3 pagesRead .. 9 do
                        Vitest.expect(shown.Pages.[index].PageId).toBe ($"diff-1-p{index + 1}")
                        Vitest.expect(shown.Pages.[index].IsEvicted).toBe (false)

                // The 46th page of the new session starts with line 90.
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(current.Value.DiffReopen).toEqual (None)
                Vitest.expect(page.Pages.Length).toBe (46)
                Vitest.expect(page.RequestedPageIndex).toBe (45)
                Vitest.expect(page.Pages.[45].PageId).toBe ("diff-2-p46")
                Vitest.expect(page.Pages.[45].IsEvicted).toBe (false)
                Vitest.expect(pageParts page.Pages.[45]).toEqual ([| "hunk:diff-2-h46" |])

                // Once the reopen lands, only pages that can share a window with the landing page
                // stay loaded, and the viewer is asked to scroll to the target line.
                Vitest
                    .expect(
                        page.Pages
                        |> Array.indexed
                        |> Array.filter (fun (index, windowPage) ->
                            not windowPage.IsEvicted && abs (index - 45) >= GitDiffPageLoader.MaxLoadedPages
                        )
                        |> Array.map fst
                    )
                    .toEqual ([||])

                Vitest
                    .expect(page.ScrollTarget)
                    .toEqual (
                        Some(
                            {
                                Side = Paged.PagedDiffSide.Previous
                                Line = 90.0
                                Token = 1
                            }
                            : Paged.PagedScrollTarget
                        )
                    )
            }
        )

        Vitest.test (
            "A next page that lands while a replay is still running keeps the pages on screen loaded",
            fun () -> promise {
                let fake = FakeDiffClient()

                let pageDto index =
                    diffPage $"p{index}" (Some $"cursor-{index}") [| hunk $"h{index}" |]

                let! state = openFirstPage fake (pageDto 1)
                let generation = (diffOf state).Generation

                fake.ReadReply <-
                    fun request ->
                        succeeded (ResumablePageDto.Ready(pageDto (int (request.Cursor.Replace("cursor-", "")) + 1)))

                fake.ReplayReply <- fun request -> succeeded (pageDto (int (request.PageId.Replace("p", ""))))
                let current = ref state

                for _ in 2..9 do
                    let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                    current.Value <- next

                Vitest.expect((diffOf current.Value).Pages.[0].IsEvicted).toBe (true)

                // The next page is asked for. The user scrolls back up to the second page, and the
                // first page is replayed. The next page lands before the replay answers.
                let loading, loadCmd =
                    update fake.Dependencies fake.SetPageState (diffMsg (GitDiffMsg.LoadNext generation)) current.Value

                let! heldPage = collectMessages loadCmd

                let replaying, replayCmd =
                    update
                        fake.Dependencies
                        fake.SetPageState
                        (diffMsg (GitDiffMsg.Replay(generation, "p1", [ "p1"; "p2" ])))
                        loading

                let! heldReplay = collectMessages replayCmd
                current.Value <- replaying

                for message in heldPage do
                    let! next = run fake message current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| for index in 1..10 -> $"p{index}" |])
                Vitest.expect(page.Pages.[1].IsEvicted).toBe (false)
                Vitest.expect(page.RequestedPageIndex).toBe (0)

                for message in heldReplay do
                    let! next = run fake message current.Value
                    current.Value <- next

                let page = diffOf current.Value
                Vitest.expect(page.Pages.[0].IsEvicted).toBe (false)
                Vitest.expect(page.Pages.[1].IsEvicted).toBe (false)
                Vitest.expect(page.RequestedPageIndex).toBe (0)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "An open continuation answered with a closed session reopens the diff",
            fun () -> promise {
                let fake = FakeDiffClient()

                // The first open scans, its continuation finds the session closed, and the reopen
                // opens the diff with a handle of its own.
                fake.OpenReply <-
                    fun request ->
                        match fake.Opens.Count, request.Continuation with
                        | 1, None -> succeeded (ResumableOpenDto.Scanning(progress false, "scan-1", None))
                        | 2, Some _ -> failedWith "diff_session_closed" None
                        | count, _ -> openedOn (handleOfOpen count) (diffPage "p1" None [| hunk "h1" |])

                let! state = run fake (select "a.txt") runningState
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (3)
                Vitest.expect(fake.Opens.[2].Continuation).toEqual (None)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 3))
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1" |])
                Vitest.expect(state.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "A closed session answering the first page read reopens the diff once",
            fun () -> promise {
                let scanningOn (openCount: int) =
                    succeeded (
                        ResumableOpenDto.Ready(
                            OpenDiffResultDto.Opened(
                                handleOfOpen openCount,
                                sourceInfo "a.txt",
                                sourceInfo "a.txt",
                                ResumablePageDto.Scanning(progress false, "scan", None),
                                DiffStorageDto.OnDisk
                            )
                        )
                    )

                // The pool closed the first session before its first page was read.
                let fake = FakeDiffClient()
                fake.OpenReply <- fun _ -> scanningOn fake.Opens.Count

                fake.ReadReply <-
                    fun request ->
                        if request.HandleId = "diff-1" then
                            failedWith "diff_session_closed" None
                        else
                            succeeded (ResumablePageDto.Ready(diffPage "p1" None [| hunk "h1" |]))

                let! state = run fake (select "a.txt") runningState
                let page = diffOf state

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Handle).toEqual (Some(handleOfOpen 2))
                Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1" |])
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(state.DiffReopen).toEqual (None)

                // The reopened session is closed as well, so the failure shows and nothing loops.
                let closedAgain = FakeDiffClient()
                closedAgain.OpenReply <- fun _ -> scanningOn closedAgain.Opens.Count
                closedAgain.ReadReply <- fun _ -> failedWith "diff_session_closed" None

                let! state = run closedAgain (select "a.txt") runningState

                Vitest.expect(closedAgain.Opens.Count).toBe (2)
                Vitest.expect(isFailed (diffOf state).Status).toBe (true)
            }
        )

        Vitest.test (
            "A background read sends the background flag and a user read does not",
            fun () -> promise {
                let fake = FakeDiffClient()
                fake.OpenReply <- fun _ -> openedPage (chainPage 1 3)

                fake.ReadReply <-
                    fun request -> succeeded (ResumablePageDto.Ready(chainPage (cursorIndex request.Cursor + 1) 3))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                Vitest.expect(fake.Reads |> Seq.map _.Background |> Seq.toArray).toEqual [| false |]

                let! _ = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect(fake.Reads |> Seq.map _.Background |> Seq.toArray).toEqual [| false; true |]
            }
        )

        Vitest.test (
            "A memory diff ignores the limit in MB while a disk diff stops at it",
            fun () -> promise {
                // Every page is a bit above 1 MiB of JSON, and the limit is 1 MiB.
                let bigPage (index: int) =
                    diffPage $"p{index}" (if index < 3 then Some $"cursor-{index}" else None) [|
                        hunkWith $"h{index}" [|
                            {
                                changedRow $"h{index}-row" with
                                    Current = Some(textLine index (String.replicate 1100000 "x"))
                            }
                        |]
                    |]

                let limited = {
                    runningState with
                        DiffIndexingLimitMb = 1
                }

                let indexedPages (storage: DiffStorageDto) = promise {
                    let fake = FakeDiffClient()
                    fake.OpenReply <- fun _ -> openedWithStorage storage (ResumablePageDto.Ready(bigPage 1))

                    fake.ReadReply <-
                        fun request -> succeeded (ResumablePageDto.Ready(bigPage (cursorIndex request.Cursor + 1)))

                    let! state = run fake (select "a.txt") limited
                    let! state = run fake (diffMsg (GitDiffMsg.Index (diffOf state).Generation)) state
                    return diffOf state
                }

                let! inMemory = indexedPages memoryStorage
                Vitest.expect(inMemory.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2"; "p3" |])
                Vitest.expect(GitDiffPageLoader.indexingLimitReached 1 inMemory).toBe (false)

                let! onDisk = indexedPages DiffStorageDto.OnDisk
                Vitest.expect(onDisk.Pages |> Array.map _.PageId).toEqual ([| "p1" |])
                Vitest.expect(GitDiffPageLoader.indexingLimitReached 1 onDisk).toBe (true)
            }
        )

        Vitest.test (
            "A background read refused with either code ends the indexing, keeps the rows and offers the continue button",
            fun () -> promise {
                let cases = [
                    "diff_temp_space_low", DiffStorageDto.OnDisk, GitDiffStop.TempSpaceLow, "stopped", true
                    "diff_memory_budget_reached", memoryStorage, GitDiffStop.MemoryShareUsed, "memory", false
                ]

                for code, storage, expected, bar, hasNote in cases do
                    let fake = FakeDiffClient()
                    fake.OpenReply <- fun _ -> openedWithStorage storage (ResumablePageDto.Ready(chainPage 1 5))

                    fake.ReadReply <-
                        fun request ->
                            if request.Cursor = "cursor-1" then
                                succeeded (ResumablePageDto.Ready(chainPage 2 5))
                            else
                                failedWith code None

                    let! state = run fake (select "a.txt") runningState
                    let generation = (diffOf state).Generation
                    let! state = run fake (diffMsg (GitDiffMsg.Index generation)) state
                    let page = diffOf state

                    Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                    Vitest.expect(page.Indexing).toBe (false)
                    Vitest.expect(page.NextFailed).toBe (false)
                    Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                    Vitest.expect(page.NextCursor).toEqual (Some "cursor-2")
                    Vitest.expect(page.Stop).toEqual (Some expected)
                    Vitest.expect(fake.Reads.[fake.Reads.Count - 1].Background).toBe (true)
                    Vitest.expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb page).toEqual (None)
                    Vitest.expect(barState page).toEqual (Some bar)
                    Vitest.expect(shows page "renderer-git-diff-continue-button").toBe (true)
                    Vitest.expect(shows page "renderer-git-diff-continue-note").toBe (hasNote)
            }
        )

        Vitest.test (
            "A user read refused with the memory budget ends the diff there, and one refused for low space can retry",
            fun () -> promise {
                let cases = [
                    "diff_memory_budget_reached", memoryStorage, GitDiffStop.MemoryBudget, false
                    "diff_temp_space_low", DiffStorageDto.OnDisk, GitDiffStop.TempSpaceLow, true
                ]

                for code, storage, expected, canRetry in cases do
                    let fake = FakeDiffClient()
                    fake.OpenReply <- fun _ -> openedWithStorage storage (ResumablePageDto.Ready(chainPage 1 5))
                    fake.ReadReply <- fun _ -> failedWith code None

                    let! state = run fake (select "a.txt") runningState
                    let generation = (diffOf state).Generation
                    let! state = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                    let page = diffOf state

                    Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "p1" |])
                    Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                    Vitest.expect(page.NextFailed).toBe (false)
                    Vitest.expect(page.NextCursor).toEqual (Some "cursor-1")
                    Vitest.expect(page.Stop).toEqual (Some expected)
                    Vitest.expect(barState page).toEqual (Some "stopped")
                    Vitest.expect(shows page "renderer-git-diff-continue-note").toBe (true)
                    Vitest.expect(shows page "renderer-git-diff-continue-button").toBe (canRetry)

                    // The continue button is the only way to read on, and the full budget offers none.
                    let! again = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                    Vitest.expect(fake.Reads.Count).toBe (if canRetry then 2 else 1)
                    Vitest.expect((diffOf again).Stop).toEqual (Some expected)
            }
        )

        Vitest.test (
            "A user read after a low-space stop clears it and resumes the indexing, and the memory budget stop stays",
            fun () -> promise {
                let fake = FakeDiffClient()
                fake.OpenReply <- fun _ -> openedPage (chainPage 1 5)
                let mutable code = Some "diff_temp_space_low"

                fake.ReadReply <-
                    fun request ->
                        match code with
                        | Some refused -> failedWith refused None
                        | None -> succeeded (ResumablePageDto.Ready(chainPage (cursorIndex request.Cursor + 1) 5))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! stopped = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                Vitest.expect((diffOf stopped).Stop).toEqual (Some GitDiffStop.TempSpaceLow)

                // The stop holds the indexing back, even when the page asks for it.
                Vitest
                    .expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb (diffOf stopped))
                    .toEqual (None)

                let! stillStopped = run fake (diffMsg (GitDiffMsg.Index generation)) stopped
                Vitest.expect((diffOf stillStopped).Indexing).toBe (false)

                code <- None
                let! resumed = run fake (diffMsg (GitDiffMsg.LoadNext generation)) stopped
                Vitest.expect((diffOf resumed).Stop).toEqual (None)
                Vitest.expect((diffOf resumed).Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])

                Vitest
                    .expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb (diffOf resumed))
                    .toEqual (Some(GitDiffMsg.Index generation))

                // A user read that goes through does not lift the stop of the full memory budget.
                let budget = {
                    diffOf resumed with
                        Stop = Some GitDiffStop.MemoryBudget
                }

                Vitest.expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb budget).toEqual (None)
            }
        )

        Vitest.test (
            "A reopen starts without the stop of the old session and takes the storage of the new one",
            fun () -> promise {
                let fake = FakeDiffClient()

                fake.OpenReply <-
                    fun _ ->
                        if fake.Opens.Count = 1 then
                            openedWithStorage memoryStorage (ResumablePageDto.Ready(chainPage 1 5))
                        else
                            openedOn (handleOfOpen fake.Opens.Count) (chainPage 1 5)

                fake.ReadReply <- fun _ -> failedWith "diff_memory_budget_reached" None
                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! stopped = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                Vitest.expect((diffOf stopped).Stop).toEqual (Some GitDiffStop.MemoryBudget)
                Vitest.expect((diffOf stopped).Storage).toEqual (Some memoryStorage)

                // The session closes later, and the next expansion reopens the diff.
                fake.ExpandReply <- fun _ -> failedWith "diff_session_closed" None
                let! reopened = run fake (diffMsg (GitDiffMsg.Expand(generation, "g1", true))) stopped
                let page = diffOf reopened

                Vitest.expect(fake.Opens.Count).toBe (2)
                Vitest.expect(page.Stop).toEqual (None)
                Vitest.expect(page.Storage).toEqual (Some DiffStorageDto.OnDisk)
                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
            }
        )

        Vitest.test (
            "A reopen answered with either code lands at the last page read, keeps the rows and sets the stop",
            fun () -> promise {
                let cases = [
                    "diff_temp_space_low", GitDiffStop.TempSpaceLow
                    "diff_memory_budget_reached", GitDiffStop.MemoryBudget
                ]

                for code, expected in cases do
                    let fake = FakeDiffClient()

                    let pageOf (handleId: string) (index: int) =
                        diffPage $"{handleId}-p{index}" (Some $"cursor-{index}") [|
                            hunkAt $"{handleId}-h{index}" (index * 10)
                        |]

                    fake.OpenReply <-
                        fun _ ->
                            let openHandle = handleOfOpen fake.Opens.Count
                            openedOn openHandle (pageOf openHandle.Id 1)

                    // The first session closes at its fourth page. The second one reads two pages and
                    // refuses the third.
                    fake.ReadReply <-
                        fun request ->
                            let index = cursorIndex request.Cursor + 1

                            if request.HandleId = "diff-1" && index = 4 then
                                failedWith "diff_session_closed" None
                            elif request.HandleId = "diff-2" && index = 3 then
                                failedWith code None
                            else
                                succeeded (ResumablePageDto.Ready(pageOf request.HandleId index))

                    let! state = run fake (select "a.txt") runningState
                    let generation = (diffOf state).Generation
                    let current = ref state

                    for _ in 1..3 do
                        let! next = run fake (diffMsg (GitDiffMsg.LoadNext generation)) current.Value
                        current.Value <- next

                    let page = diffOf current.Value

                    Vitest.expect(page.Pages |> Array.map _.PageId).toEqual ([| "diff-2-p1"; "diff-2-p2" |])
                    Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                    Vitest.expect(page.RequestedPageIndex).toBe (1)
                    Vitest.expect(page.NextCursor).toEqual (Some "cursor-2")
                    Vitest.expect(page.Stop).toEqual (Some expected)
                    Vitest.expect(current.Value.DiffReopen).toEqual (None)
            }
        )

        Vitest.test (
            "An open answered with either code shows a blocked page, and a blob that is too large for memory names its cause",
            fun () -> promise {
                let refusals = [
                    "diff_temp_space_low", GitDiffBlockReason.TempSpaceLow
                    "diff_memory_budget_reached", GitDiffBlockReason.MemoryBudgetReached
                ]

                for code, reason in refusals do
                    let fake = FakeDiffClient()
                    fake.OpenReply <- fun _ -> failedWith code None
                    let! state = run fake (select "a.txt") runningState
                    Vitest.expect((diffOf state).Status).toEqual (GitDiffPageStatus.Blocked(None, reason))
                    Vitest.expect((diffOf state).Handle).toEqual (None)

                let textOf (cause: MemoryCauseDto) = promise {
                    let fake = FakeDiffClient()

                    fake.OpenReply <-
                        fun _ ->
                            succeeded (
                                ResumableOpenDto.Ready(
                                    OpenDiffResultDto.NotDiffable(
                                        DiffBlockerDto.BlobTooLargeForMemory(
                                            DiffSideDto.Current,
                                            "9000000000",
                                            "3932160",
                                            cause
                                        )
                                    )
                                )
                            )

                    let! state = run fake (select "a.txt") runningState

                    Vitest
                        .expect((diffOf state).Status)
                        .toEqual (
                            GitDiffPageStatus.Blocked(
                                Some DiffSideDto.Current,
                                GitDiffBlockReason.TooLargeForMemory(9000000000.0, 3932160.0, cause)
                            )
                        )

                    return
                        match (diffOf state).Status with
                        | GitDiffPageStatus.Blocked(_, reason) -> Renderer.GitDiffPresentation.blockReasonText reason
                        | _ -> ""
                }

                let! bySetting = textOf MemoryCauseDto.BySetting
                let! byLowSpace = textOf MemoryCauseDto.ByLowSpace
                Vitest.expect((bySetting = byLowSpace)).toBe (false)
            }
        )

        Vitest.test (
            "An expansion or line slice refused with either code marks its control with the reason",
            fun () -> promise {
                let longRow = {
                    changedRow "long" with
                        Current =
                            Some {
                                textLine 0 "abc" with
                                    Slice = {
                                        OffsetUtf16 = "0"
                                        TotalUtf16 = Some "9"
                                        Text = "abc"
                                        Highlights = [||]
                                    }
                            }
                }

                let fake = FakeDiffClient()

                let! state =
                    openFirstPage fake (diffPage "p1" (Some "cursor-1") [| hunkWith "h1" [| longRow |]; gap "g1" |])

                let generation = (diffOf state).Generation

                let titleOf (page: GitDiffPageData) (testId: string) =
                    ((renderTarget page).querySelector $"[data-testid=\"{testId}\"]").getAttribute "title"

                for code, expected in
                    [
                        "diff_memory_budget_reached", GitDiffStop.MemoryBudget
                        "diff_temp_space_low", GitDiffStop.TempSpaceLow
                    ] do
                    fake.ExpandReply <- fun _ -> failedWith code None
                    fake.LineReply <- fun _ -> failedWith code None

                    let! refused = run fake (diffMsg (GitDiffMsg.Expand(generation, "g1", true))) state

                    let! refused =
                        run fake (diffMsg (GitDiffMsg.LoadLineSlice(generation, DiffSideDto.Current, 0.0, 3.0))) refused

                    let page = diffOf refused
                    Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                    Vitest.expect(page.FailedGaps).toEqual ([ "g1" ])
                    Vitest.expect(page.FailedLineSlices.Length).toBe (1)
                    Vitest.expect(page.FailureCause).toEqual (Some expected)

                    // The tooltip of a failed control carries the reason, so it differs from the one without.
                    let withoutReason = { page with FailureCause = None }
                    let gapId = "renderer-git-diff-gap-expand-start-g1"
                    let lineId = "renderer-git-diff-line-more-current-0"
                    Vitest.expect((titleOf page gapId = titleOf withoutReason gapId)).toBe (false)
                    Vitest.expect((titleOf page lineId = titleOf withoutReason lineId)).toBe (false)
            }
        )

        Vitest.test (
            "A failed read of the next page keeps its message, and the bar shows it",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (chainPage 1 5)
                let generation = (diffOf state).Generation
                fake.ReadReply <- fun _ -> failedWith "git_failure" None
                let! failed = run fake (diffMsg (GitDiffMsg.LoadNext generation)) state
                let page = diffOf failed

                Vitest.expect(page.Status).toEqual (GitDiffPageStatus.Ready)
                Vitest.expect(page.NextFailed).toBe (true)
                Vitest.expect(page.NextFailure.IsSome).toBe (true)
                Vitest.expect(barState page).toEqual (Some "failed")

                // The next successful read drops the message.
                fake.ReadReply <- fun _ -> succeeded (ResumablePageDto.Ready(chainPage 2 5))
                let! retried = run fake (diffMsg (GitDiffMsg.LoadNext generation)) failed
                Vitest.expect((diffOf retried).NextFailure).toEqual (None)
                Vitest.expect(barState (diffOf retried)).toEqual (None)
            }
        )

        Vitest.test (
            "The bar names memory mode and each stop",
            fun () -> promise {
                let fake = FakeDiffClient()
                let! state = openFirstPage fake (chainPage 1 5)
                let page = diffOf state

                let inMemory = {
                    page with
                        Storage = Some memoryStorage
                }

                // A memory diff with nothing running names its storage.
                Vitest.expect(barState inMemory).toEqual (Some "memory")
                Vitest.expect(barState { inMemory with Indexing = true }).toEqual (Some "memory")
                Vitest.expect(barState { page with Indexing = true }).toEqual (Some "indexing")

                Vitest
                    .expect(
                        barState {
                            inMemory with
                                Stop = Some GitDiffStop.MemoryShareUsed
                        }
                    )
                    .toEqual (Some "memory")

                Vitest
                    .expect(
                        barState {
                            inMemory with
                                Stop = Some GitDiffStop.MemoryBudget
                        }
                    )
                    .toEqual (Some "stopped")

                Vitest
                    .expect(
                        barState {
                            page with
                                Stop = Some GitDiffStop.TempSpaceLow
                        }
                    )
                    .toEqual (Some "stopped")

                // The pages read do not stop a memory diff at the limit of the context.
                Vitest
                    .expect(
                        barState {
                            inMemory with
                                JournalBytes = 1024.0 * 1024.0 * 1024.0
                        }
                    )
                    .toEqual (Some "memory")
            }
        )

        Vitest.test (
            "A memory diff with nothing running or stopped shows the memory state",
            fun () -> promise {
                let fake = FakeDiffClient()

                // The whole diff fits its first page.
                fake.OpenReply <- fun _ -> openedInMemory (chainPage 1 1)
                let! single = run fake (select "a.txt") runningState
                Vitest.expect((diffOf single).NextCursor).toEqual (None)
                Vitest.expect(barState (diffOf single)).toEqual (Some "memory")

                Vitest
                    .expect(Renderer.Components.MainContent.GitDiffTarget.backgroundReadingOf 1024 (diffOf single))
                    .toEqual (Some Renderer.Components.MainContent.GitDiffTarget.BackgroundReading.MemoryIdle)

                // The indexing read every page, and nothing is left to read.
                fake.OpenReply <- fun _ -> openedInMemory (chainPage 1 2)

                fake.ReadReply <-
                    fun request -> succeeded (ResumablePageDto.Ready(chainPage (cursorIndex request.Cursor + 1) 2))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! indexed = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect((diffOf indexed).Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect((diffOf indexed).NextCursor).toEqual (None)
                Vitest.expect(barState (diffOf indexed)).toEqual (Some "memory")

                Vitest
                    .expect(Renderer.Components.MainContent.GitDiffTarget.backgroundReadingOf 1024 (diffOf indexed))
                    .toEqual (Some Renderer.Components.MainContent.GitDiffTarget.BackgroundReading.MemoryIdle)

                // The background read stopped at its share, and the user read the rest. The bar no
                // longer says that the rest of the budget is kept.
                fake.ReadReply <-
                    fun request ->
                        if request.Background then
                            failedWith "diff_memory_budget_reached" None
                        else
                            succeeded (ResumablePageDto.Ready(chainPage (cursorIndex request.Cursor + 1) 2))

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! shared = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect((diffOf shared).Stop).toEqual (Some GitDiffStop.MemoryShareUsed)

                Vitest
                    .expect(Renderer.Components.MainContent.GitDiffTarget.backgroundReadingOf 1024 (diffOf shared))
                    .toEqual (Some Renderer.Components.MainContent.GitDiffTarget.BackgroundReading.MemoryShareKept)

                let! finished = run fake (diffMsg (GitDiffMsg.LoadNext generation)) shared
                Vitest.expect((diffOf finished).NextCursor).toEqual (None)
                Vitest.expect((diffOf finished).Stop).toEqual (Some GitDiffStop.MemoryShareUsed)
                Vitest.expect(barState (diffOf finished)).toEqual (Some "memory")

                Vitest
                    .expect(Renderer.Components.MainContent.GitDiffTarget.backgroundReadingOf 1024 (diffOf finished))
                    .toEqual (Some Renderer.Components.MainContent.GitDiffTarget.BackgroundReading.MemoryIdle)
            }
        )

        Vitest.test (
            "A user read after the background share stop works, and the next one hits the full budget and ends the diff",
            fun () -> promise {
                let fake = FakeDiffClient()
                fake.OpenReply <- fun _ -> openedInMemory (chainPage 1 5)
                let userReads = ref 0

                fake.ReadReply <-
                    fun request ->
                        if request.Background then
                            failedWith "diff_memory_budget_reached" None
                        else
                            userReads.Value <- userReads.Value + 1

                            if userReads.Value = 1 then
                                succeeded (ResumablePageDto.Ready(chainPage (cursorIndex request.Cursor + 1) 5))
                            else
                                failedWith "diff_memory_budget_reached" None

                let! state = run fake (select "a.txt") runningState
                let generation = (diffOf state).Generation
                let! shared = run fake (diffMsg (GitDiffMsg.Index generation)) state
                Vitest.expect((diffOf shared).Stop).toEqual (Some GitDiffStop.MemoryShareUsed)

                let! read = run fake (diffMsg (GitDiffMsg.LoadNext generation)) shared
                let readPage = diffOf read
                Vitest.expect(readPage.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(readPage.Stop).toEqual (Some GitDiffStop.MemoryShareUsed)
                Vitest.expect(GitDiffPageLoader.indexingRequest DefaultDiffIndexingLimitMb readPage).toEqual (None)

                let! refused = run fake (diffMsg (GitDiffMsg.LoadNext generation)) read
                let refusedPage = diffOf refused

                let note, canRequestNext =
                    Renderer.Components.MainContent.GitDiffTarget.endNoteOf refusedPage

                Vitest.expect(refusedPage.Pages |> Array.map _.PageId).toEqual ([| "p1"; "p2" |])
                Vitest.expect(refusedPage.Stop).toEqual (Some GitDiffStop.MemoryBudget)
                Vitest.expect(note.IsSome).toBe (true)
                Vitest.expect(canRequestNext).toBe (false)

                let readsBefore = fake.Reads.Count
                let! again = run fake (diffMsg (GitDiffMsg.LoadNext generation)) refused
                Vitest.expect(fake.Reads.Count).toBe (readsBefore)
                Vitest.expect((diffOf again).Stop).toEqual (Some GitDiffStop.MemoryBudget)
            }
        )
)
