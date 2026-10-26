module ElectronCore.VersionControlTextDiffTests

open Fable.Core
open Fable.Core.JsInterop
open Main
open Main.ArcVaultState
open Main.VersionControl
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest

module TextDiffProtocol = VersionControlService.Git.TextDiff.TextDiffProtocol

/// The worker limit of a page or an expansion reply, measured on the worker message.
[<Literal>]
let private PageLimitBytes = 524288

/// The worker limit of a line slice reply.
[<Literal>]
let private LineLimitBytes = 65536

[<Literal>]
let private IpcMarginBytes = 8192

[<Emit("Buffer.byteLength(JSON.stringify($0), 'utf8')")>]
let private jsonBytes (_value: obj) : int = jsNative

[<Emit("structuredClone($0)")>]
let private structuredCloneOf (_value: obj) : obj = jsNative

/// UTF-8 bytes of the JSON of an IPC reply. Electron sends a structured clone, which
/// drops the toJSON of unions, so both shapes are measured.
let private ipcPayloadBytes (payload: Result<OperationResultDto<'T>, exn>) : int =
    max (jsonBytes (box payload)) (jsonBytes (structuredCloneOf (box payload)))

/// The policy the mappings of a test read the cause of a memory diff from. A disk policy that
/// ended in memory is the low-space fallback, a memory policy is the indexing limit setting.
let private diskPolicy = DiffStoragePolicy.PreferDisk(1073741824L, 67108864L)

let private memoryPolicy = DiffStoragePolicy.MemoryOnly 5242880L

let private reply (mapValue: 'T -> 'U) (value: 'T) : Result<OperationResultDto<'U>, exn> =
    Ok(Mappings.textDiffResult mapValue (OperationResult.succeeded value))

let private workerBytes (payload: TextDiffProtocol.ResultPayload) : int =
    TextDiffProtocol.TextDiffMessage.Result("0f8fad5b-d9cb-469f-a165-70867728950e", 1, payload)
    |> TextDiffProtocol.encode
    |> TextDiffProtocol.envelopeByteLength

/// The largest count of repeated units whose worker message stays within the limit. The
/// linear estimate can overshoot (separators, growing digit counts), so it steps back.
let private fillTo (limit: int) (sizeWith: int -> int) : int =
    let empty = sizeWith 0
    let perUnit = sizeWith 1 - empty
    let mutable count = max 0 ((limit - empty) / perUnit)

    while count > 0 && sizeWith count > limit do
        count <- count - 1

    count

// Long line numbers and the pending preview leave room for fewer rows than the row limit
// of 1,000 before the byte limit is reached.
[<Literal>]
let private PageRows = 600

// Line numbers above 2^53 make every int64 as long as it gets in both encodings.
let private largeNumber = 9_000_000_000_000_000_000L

// JSON escapes a control character as six bytes, the most one UTF-16 unit can cost.
let private expensiveText (length: int) = String.replicate length "\u0001"

let private token (prefix: string) =
    prefix + String.replicate (64 - prefix.Length) "0"

let private highlights (count: int) : Highlight[] =
    Array.init
        count
        (fun index -> {
            Start = index * 2
            Length = 1
            Kind =
                if index % 2 = 0 then
                    HighlightKind.ChangedText
                else
                    HighlightKind.UnchangedText
        })

let private line (number: int64) (text: string) (highlightCount: int) : DiffLine = {
    Number = number
    Ending = LineEnding.CRLF
    Slice = {
        OffsetUtf16 = largeNumber
        TotalUtf16 = Some largeNumber
        Text = text
        Highlights = highlights highlightCount
    }
}

let private range: LineRange = {
    Start = largeNumber
    Count = largeNumber
}

let private progress: ScanProgress = {
    ValidatedBytes = largeNumber
    TotalBytes = largeNumber
    ScanComplete = false
}

let private maximumPreview: PendingPreview = {
    Previous =
        PendingSide.Snippet {
            Line = largeNumber
            OffsetUtf16 = largeNumber
            Text = expensiveText 2048
            End = SnippetEnd.MoreTextPending
        }
    Current =
        PendingSide.Snippet {
            Line = largeNumber
            OffsetUtf16 = largeNumber
            Text = expensiveText 2048
            End = SnippetEnd.Truncated
        }
    Mismatch =
        Some {
            PreviousOffsetUtf16 = largeNumber
            CurrentOffsetUtf16 = largeNumber
        }
}

/// A partial page of 600 rows in hunk fragments of 32 rows, with every line text grown until
/// the worker message reaches its byte limit.
let private pageWith (textLength: int) : DiffPage =
    let rows =
        Array.init
            PageRows
            (fun index -> {
                Id = "row-" + (string index).PadLeft(6, '0')
                Kind = DiffRowKind.Replaced
                Previous = Some(line (largeNumber + int64 index) (expensiveText textLength) 1)
                Current = Some(line (largeNumber + int64 index) (expensiveText textLength) 1)
            })

    {
        PageId = token "page-"
        NextCursor = Some(token "cursor-")
        Parts =
            rows
            |> Array.chunkBySize 32
            |> Array.mapi (fun index chunk ->
                DiffPart.Hunk {
                    HunkId = "hunk-" + (string index).PadLeft(6, '0')
                    PreviousRange = range
                    CurrentRange = range
                    StartsHunk = true
                    EndsHunk = false
                    Body = HunkBody.AlignedRows chunk
                }
            )
        Progress = progress
        OutputComplete = false
        Pending = Some maximumPreview
    }

/// An expansion of the largest line count with line texts grown to the byte limit.
let private expansionWith (textLength: int) : DiffPart[] = [|
    DiffPart.ExpandedContext(
        token "gap-",
        Array.init
            100
            (fun index -> {
                Id = "row-" + (string index).PadLeft(6, '0')
                Kind = DiffRowKind.Context
                Previous = Some(line (largeNumber + int64 index) (expensiveText textLength) 0)
                Current = Some(line (largeNumber + int64 index) (expensiveText textLength) 0)
            })
    )
|]

/// A slice of the largest length with as many highlights as the byte limit allows.
let private sliceWith (highlightCount: int) : DiffLine =
    line largeNumber (expensiveText 8192) highlightCount

let private failureWith (message: string) (evidence: string) : OperationFailure = {
    OperationFailure.create Validation TextDiffFailureCodes.ContentNotText message with
        Details = [| "detail line" |]
        DiffDetail =
            Some {
                Side = DiffSide.Current
                Evidence = evidence
                InvalidSequenceOffset = None
            }
}

let private boundFailure (failure: OperationFailure) : OperationFailureDto =
    match Mappings.textDiffResult id (Failed failure: OperationResult<unit>) with
    | OperationResultDto.Failed mapped -> mapped
    | other -> failwith $"Expected a failure, got {other}"

let private utf8Bytes (text: string) = jsonBytes (box text) - 2

let private lineRequest
    (operationId: string)
    (handleId: string)
    (lineText: string)
    (offset: string)
    : ReadTextDiffLineRequestDto =
    {
        OperationId = operationId
        HandleId = handleId
        HandleVersion = "v1"
        Side = DiffSideDto.Current
        Line = lineText
        OffsetUtf16 = offset
        MaxUtf16 = 8192
        Continuation = None
    }

let private expectInvalid (result: Result<'T, OperationFailure>) =
    match result with
    | Error failure -> Vitest.expect(failure.Code).toBe VersionControlCodes.InvalidDiffRequest
    | Ok value -> failwith $"Expected a rejected request, got {value}"

/// Fails with a message that names the awaited event when it does not happen in time.
let private within (milliseconds: int) (description: string) (pending: JS.Promise<unit>) : JS.Promise<unit> =
    Promise.race [
        pending
        promise {
            do! Promise.sleep milliseconds
            return failwith $"{description} did not happen within {milliseconds} ms."
        }
    ]

let private unexpected (name: string) : Async<OperationResult<'T>> = async {
    return failwith $"{name} was not expected."
}

/// A text diff service whose calls all fail the test. Each test replaces the calls it expects.
let private unexpectedService: TextDiffService = {
    Open = fun _ _ -> unexpected "Open"
    ReadPage = fun _ _ -> unexpected "ReadPage"
    ReplayPage = fun _ _ -> unexpected "ReplayPage"
    Expand = fun _ _ -> unexpected "Expand"
    ReadLine = fun _ _ -> unexpected "ReadLine"
    GetSourceInfo = fun _ _ -> unexpected "GetSourceInfo"
    Close = fun _ _ -> unexpected "Close"
}

/// A runtime with one provider that owns the workspace root and opens a core-only session
/// carrying the given text diff service. Opening the session waits for the gate.
let private textDiffRuntime
    (workspaceRoot: string)
    (sessionGate: JS.Promise<unit>)
    (service: TextDiffService)
    : VersionControlRuntime.VersionControlRuntime =
    let providerId = ProviderId.tryCreate "git" |> Result.defaultWith failwith

    let unsupported () = async { return Failed(OperationFailure.create Unsupported "operation_not_supported" "fake") }

    let binding root : WorkspaceBinding = {
        SchemaVersion = WorkspaceBinding.CurrentSchemaVersion
        ProviderId = providerId
        WorkspaceRoot = root
        ProviderStateRef = None
        Location = {
            ProviderId = providerId
            DisplayName = None
            ProviderLocation = root
            ConnectionProfileId = None
        }
        ConnectionProfileId = None
    }

    let core: CoreVersionControl = {
        GetStatus = fun _ -> unsupported ()
        ListRefs = fun _ -> unsupported ()
        CreateRef = fun _ _ -> unsupported ()
        PreflightSwitchRef = fun _ _ -> unsupported ()
        SwitchRef = fun _ _ -> unsupported ()
        CreateRevision = fun _ _ -> unsupported ()
        RestorePaths = fun _ _ -> unsupported ()
        GetDiffSummary = fun _ -> unsupported ()
    }

    let factory: ProviderFactory = {
        Id = providerId
        Probe =
            fun path -> async {
                let detected = WorkspaceBindingStore.rootsEqual CaseInsensitive path workspaceRoot

                return
                    if detected then
                        Detected(workspaceRoot, 50, None)
                    else
                        NotDetected
            }
        VerifyLocation = fun _ _ -> unsupported ()
        Initialize = fun _ _ -> unsupported ()
        Clone = fun _ _ -> unsupported ()
        Adopt = fun request _ -> async { return OperationResult.succeeded (binding request.WorkspaceRoot) }
        Bind = fun _ _ -> unsupported ()
        Open =
            fun opened _ -> async {
                do! Async.AwaitPromise sessionGate

                let session =
                    WorkspaceSession.createCoreOnly
                        {
                            ProviderId = opened.ProviderId
                            WorkspaceRoot = opened.WorkspaceRoot
                            Location = Some opened.Location
                        }
                        core

                return OperationResult.succeeded { session with TextDiff = Some service }
            }
        CheckDependencies = fun _ -> async { return OperationResult.succeeded [||] }
        InstallDependency = fun _ _ -> unsupported ()
    }

    {
        Catalog = ProviderComposition.createCatalog [ factory ]
        Bindings = TestHelpers.memoryBindings ()
        PathCaseSensitivity = CaseInsensitive
        SessionDefaults = UserDefaults.builtInSessionDefaults
    }

/// Registers a vault at the workspace root for each window and lets every window's web
/// contents resolve to its window.
let private registerWindows (workspaceRoot: string) (windowIds: int list) =
    let windows =
        windowIds
        |> List.map (fun windowId ->
            let window = TestHelpers.testWindow ()
            window?id <- windowId
            let vault = Main.ArcVaultState.ArcVault(window)
            vault.path <- Some workspaceRoot
            Main.ArcVault.ARC_VAULTS.Vaults.[windowId] <- vault
            windowId, window
        )

    TestHelpers.electronMock?setBrowserWindowFromWebContents (fun (webContents: obj) ->
        windows
        |> List.tryFind (fun (windowId, _) -> windowId = unbox<int> webContents?id)
        |> Option.map (snd >> box)
        |> Option.defaultValue null
    )
    |> ignore

let private sourceInfo (path: string) : DiffSourceInfo = {
    Path = RepositoryPath.tryCreate path |> Result.defaultWith failwith
    Revision = None
    IsAbsent = false
    ByteLength = 1L
    LineCount = Some 1L
    Encoding = Some "utf-8"
    EncodingWasChosen = false
    HasBom = false
}

let private openedDiffWith (storage: DiffStorage) (handleId: string) (path: string) : OpenDiffResult =
    OpenDiffResult.Opened(
        { Id = handleId; Version = "v1" },
        sourceInfo path,
        sourceInfo path,
        Resumable.Ready {
            PageId = "page-1"
            NextCursor = None
            Parts = [||]
            Progress = { progress with ScanComplete = true }
            OutputComplete = true
            Pending = None
        },
        storage
    )

let private openedDiff (handleId: string) (path: string) : OpenDiffResult =
    openedDiffWith DiffStorage.OnDisk handleId path

let private openRequest (operationId: string) (path: string) : OpenTextDiffRequestDto = {
    OperationId = operationId
    Path = path
    PreviousPath = None
    PreparationTokenId = None
    PreviousEncoding = None
    CurrentEncoding = None
    ContextLines = 3
    Continuation = None
}

let private failureCodeOf (result: Result<OperationResultDto<'T>, exn>) =
    match result with
    | Ok(OperationResultDto.Failed failure) -> failure.Code
    | other -> failwith $"Expected a failed call, got {other}"

Vitest.describe (
    "Text diff IPC payload size",
    fun () ->
        Vitest.test (
            "a partial page at the worker limit with a maximum pending preview stays within the limit plus 8 KiB",
            fun () ->
                let sizeOf textLength =
                    workerBytes (TextDiffProtocol.ResultPayload.ReadPage(Resumable.Ready(pageWith textLength)))

                let page = pageWith (fillTo PageLimitBytes sizeOf)

                let workerSize =
                    workerBytes (TextDiffProtocol.ResultPayload.ReadPage(Resumable.Ready page))

                Vitest.expect(workerSize).toBeLessThanOrEqual PageLimitBytes
                Vitest.expect(workerSize).toBeGreaterThan (PageLimitBytes - 2 * PageRows * 6)

                Vitest
                    .expect(ipcPayloadBytes (reply Mappings.resumablePage (Resumable.Ready page)))
                    .toBeLessThanOrEqual (PageLimitBytes + IpcMarginBytes)

                Vitest
                    .expect(ipcPayloadBytes (reply Mappings.diffPage page))
                    .toBeLessThanOrEqual (PageLimitBytes + IpcMarginBytes)
        )

        Vitest.test (
            "a maximum expansion stays within the worker limit plus 8 KiB",
            fun () ->
                let sizeOf textLength =
                    workerBytes (TextDiffProtocol.ResultPayload.Expand(Resumable.Ready(expansionWith textLength)))

                let parts = expansionWith (fillTo PageLimitBytes sizeOf)

                let workerSize =
                    workerBytes (TextDiffProtocol.ResultPayload.Expand(Resumable.Ready parts))

                Vitest.expect(workerSize).toBeLessThanOrEqual PageLimitBytes
                Vitest.expect(workerSize).toBeGreaterThan (PageLimitBytes - 2 * 100 * 6 * 2)

                Vitest
                    .expect(ipcPayloadBytes (reply Mappings.resumableParts (Resumable.Ready parts)))
                    .toBeLessThanOrEqual (PageLimitBytes + IpcMarginBytes)
        )

        Vitest.test (
            "a maximum line slice with highlights stays within the worker limit plus 8 KiB",
            fun () ->
                let sizeOf highlightCount =
                    workerBytes (TextDiffProtocol.ResultPayload.ReadLine(Resumable.Ready(sliceWith highlightCount)))

                let highlightCount = fillTo LineLimitBytes sizeOf
                let slice = sliceWith highlightCount

                Vitest.expect(highlightCount).toBeGreaterThan 0
                Vitest.expect(slice.Slice.Text.Length).toBe 8192

                Vitest
                    .expect(workerBytes (TextDiffProtocol.ResultPayload.ReadLine(Resumable.Ready slice)))
                    .toBeLessThanOrEqual
                    LineLimitBytes

                Vitest
                    .expect(ipcPayloadBytes (reply Mappings.resumableLine (Resumable.Ready slice)))
                    .toBeLessThanOrEqual (LineLimitBytes + IpcMarginBytes)
        )

        Vitest.test (
            "a scanning result with a maximum pending preview stays within the worker limit plus 8 KiB",
            fun () ->
                let scanning: Resumable<OpenDiffResult> =
                    Resumable.Scanning(progress, token "continuation-", Some maximumPreview)

                Vitest
                    .expect(ipcPayloadBytes (reply (Mappings.resumableOpen diskPolicy) scanning))
                    .toBeLessThanOrEqual (PageLimitBytes + IpcMarginBytes)
        )
)

Vitest.describe (
    "Text diff union mapping",
    fun () ->
        Vitest.test (
            "sides map to their DTO and back",
            fun () ->
                for side in [ DiffSide.Previous; DiffSide.Current ] do
                    Vitest.expect(Mappings.diffSideFromDto (Mappings.diffSide side)).toEqual side

                Vitest.expect(box (Mappings.diffSide DiffSide.Previous)).toBe "Previous"
                Vitest.expect(box (Mappings.diffSide DiffSide.Current)).toBe "Current"
        )

        Vitest.test (
            "field-less cases travel as their case names",
            fun () ->
                let endings =
                    [
                        LineEnding.NoEnding
                        LineEnding.LF
                        LineEnding.CRLF
                        LineEnding.CR
                    ]
                    |> List.map (Mappings.lineEnding >> box)

                let highlightKinds =
                    [ HighlightKind.UnchangedText; HighlightKind.ChangedText ]
                    |> List.map (fun kind -> box (Mappings.highlight { Start = 0; Length = 1; Kind = kind }).Kind)

                let rowKinds =
                    [
                        DiffRowKind.Context
                        DiffRowKind.Added
                        DiffRowKind.Removed
                        DiffRowKind.Replaced
                        DiffRowKind.EndingChanged
                    ]
                    |> List.map (Mappings.diffRowKind >> box)

                let snippetEnds =
                    [
                        SnippetEnd.Truncated
                        SnippetEnd.MoreTextPending
                        SnippetEnd.LineEnd
                        SnippetEnd.EndOfFile
                    ]
                    |> List.map (Mappings.snippetEnd >> box)

                Vitest.expect(endings).toEqual [ box "NoEnding"; box "LF"; box "CRLF"; box "CR" ]
                Vitest.expect(highlightKinds).toEqual [ box "UnchangedText"; box "ChangedText" ]

                Vitest.expect(rowKinds).toEqual [
                    box "Context"
                    box "Added"
                    box "Removed"
                    box "Replaced"
                    box "EndingChanged"
                ]

                Vitest.expect(snippetEnds).toEqual [
                    box "Truncated"
                    box "MoreTextPending"
                    box "LineEnd"
                    box "EndOfFile"
                ]
        )

        Vitest.test (
            "hunk bodies and diff parts keep their case and payload",
            fun () ->
                let previous = line 1L "a" 0
                let current = line 2L "b" 0

                let row: DiffRow = {
                    Id = "row-1"
                    Kind = DiffRowKind.Replaced
                    Previous = Some previous
                    Current = Some current
                }

                let fragment body : HunkFragment = {
                    HunkId = "hunk-1"
                    PreviousRange = { Start = 1L; Count = 1L }
                    CurrentRange = { Start = 2L; Count = 1L }
                    StartsHunk = true
                    EndsHunk = false
                    Body = body
                }

                let gap: EqualGap = {
                    GapId = "gap-1"
                    PreviousRange = { Start = 3L; Count = 4L }
                    CurrentRange = { Start = 5L; Count = 4L }
                }

                let rowDto = Mappings.diffRow row

                Vitest.expect(rowDto.Previous).toEqual (Some(Mappings.diffLine previous))
                Vitest.expect(rowDto.Current |> Option.map _.Number).toEqual (Some "2")

                match Mappings.diffPart (DiffPart.Hunk(fragment (HunkBody.AlignedRows [| row |]))) with
                | DiffPartDto.Hunk mapped ->
                    Vitest.expect(mapped.Body).toEqual (HunkBodyDto.AlignedRows [| rowDto |])

                    Vitest.expect(mapped.CurrentRange).toEqual {
                        LineRangeDto.Start = "2"
                        Count = "1"
                    }

                    Vitest.expect(mapped.StartsHunk).toBe true
                    Vitest.expect(mapped.EndsHunk).toBe false
                | other -> failwith $"Expected a hunk, got {other}"

                match
                    Mappings.diffPart (DiffPart.Hunk(fragment (HunkBody.UnalignedSides([| previous |], [| current |]))))
                with
                | DiffPartDto.Hunk mapped ->
                    Vitest
                        .expect(mapped.Body)
                        .toEqual (
                            HunkBodyDto.UnalignedSides(
                                [| Mappings.diffLine previous |],
                                [| Mappings.diffLine current |]
                            )
                        )
                | other -> failwith $"Expected a hunk, got {other}"

                Vitest
                    .expect(Mappings.diffPart (DiffPart.HiddenEqual gap))
                    .toEqual (
                        DiffPartDto.HiddenEqual {
                            GapId = "gap-1"
                            PreviousRange = { Start = "3"; Count = "4" }
                            CurrentRange = { Start = "5"; Count = "4" }
                        }
                    )

                Vitest
                    .expect(Mappings.diffPart (DiffPart.ExpandedContext("gap-1", [| row |])))
                    .toEqual (DiffPartDto.ExpandedContext("gap-1", [| rowDto |]))
        )

        Vitest.test (
            "pending sides keep their case and payload",
            fun () ->
                let snippet = {
                    Line = 7L
                    OffsetUtf16 = 12L
                    Text = "pending"
                    End = SnippetEnd.LineEnd
                }

                Vitest.expect(Mappings.pendingSide PendingSide.NoActiveLine).toEqual PendingSideDto.NoActiveLine

                Vitest
                    .expect(Mappings.pendingSide (PendingSide.Snippet snippet))
                    .toEqual (
                        PendingSideDto.Snippet {
                            Line = "7"
                            OffsetUtf16 = "12"
                            Text = "pending"
                            End = SnippetEndDto.LineEnd
                        }
                    )

                Vitest.expect(Mappings.pendingSide (PendingSide.Exhausted 42L)).toEqual (PendingSideDto.Exhausted "42")
        )

        Vitest.test (
            "blockers keep their case, side and payload",
            fun () ->
                let candidates = [|
                    {
                        Encoding = "windows-1252"
                        Preview = "café"
                    }
                |]

                Vitest
                    .expect(Mappings.diffBlocker diskPolicy (DiffBlocker.Binary(DiffSide.Previous, "NUL at 12")))
                    .toEqual (DiffBlockerDto.Binary(DiffSideDto.Previous, "NUL at 12"))

                Vitest
                    .expect(
                        Mappings.diffBlocker
                            diskPolicy
                            (DiffBlocker.LocalContentUnavailable(DiffSide.Current, Some "oid"))
                    )
                    .toEqual (DiffBlockerDto.LocalContentUnavailable(DiffSideDto.Current, Some "oid"))

                Vitest
                    .expect(
                        Mappings.diffBlocker
                            diskPolicy
                            (DiffBlocker.EncodingRequired(DiffSide.Current, { Id = "token-1" }, candidates))
                    )
                    .toEqual (
                        DiffBlockerDto.EncodingRequired(
                            DiffSideDto.Current,
                            { PreparationTokenDto.Id = "token-1" },
                            [|
                                {
                                    EncodingCandidateDto.Encoding = "windows-1252"
                                    Preview = "café"
                                }
                            |]
                        )
                    )

                Vitest
                    .expect(Mappings.diffBlocker diskPolicy (DiffBlocker.NotRegularFile DiffSide.Previous))
                    .toEqual (DiffBlockerDto.NotRegularFile DiffSideDto.Previous)

                Vitest.expect(Mappings.diffBlocker diskPolicy DiffBlocker.ProviderUnsupported).toEqual
                    DiffBlockerDto.ProviderUnsupported
        )

        Vitest.test (
            "the storage of an opened diff and the memory blocker map with the cause of the storage policy",
            fun () ->
                let storageOf (policy: DiffStoragePolicy) (storage: DiffStorage) = Mappings.diffStorage policy storage

                Vitest.expect(storageOf diskPolicy DiffStorage.OnDisk).toEqual DiffStorageDto.OnDisk

                Vitest
                    .expect(storageOf memoryPolicy (DiffStorage.InMemory 5242880L))
                    .toEqual (DiffStorageDto.InMemory("5242880", MemoryCauseDto.BySetting))

                Vitest
                    .expect(storageOf diskPolicy (DiffStorage.InMemory 67108864L))
                    .toEqual (DiffStorageDto.InMemory("67108864", MemoryCauseDto.ByLowSpace))

                let blocker =
                    DiffBlocker.BlobTooLargeForMemory(DiffSide.Current, 9000000000L, 3932160L)

                Vitest
                    .expect(Mappings.diffBlocker memoryPolicy blocker)
                    .toEqual (
                        DiffBlockerDto.BlobTooLargeForMemory(
                            DiffSideDto.Current,
                            "9000000000",
                            "3932160",
                            MemoryCauseDto.BySetting
                        )
                    )

                Vitest
                    .expect(Mappings.diffBlocker diskPolicy blocker)
                    .toEqual (
                        DiffBlockerDto.BlobTooLargeForMemory(
                            DiffSideDto.Current,
                            "9000000000",
                            "3932160",
                            MemoryCauseDto.ByLowSpace
                        )
                    )

                match
                    Mappings.openDiffResult
                        memoryPolicy
                        (openedDiffWith (DiffStorage.InMemory 5242880L) "h" "data/a.txt")
                with
                | OpenDiffResultDto.Opened(_, _, _, _, storage) ->
                    Vitest.expect(storage).toEqual (DiffStorageDto.InMemory("5242880", MemoryCauseDto.BySetting))
                | other -> failwith $"Expected an opened diff, got {other}"
        )

        Vitest.test (
            "a page request carries the background flag of the renderer, and an open request the policy it is given",
            fun () ->
                let readPage background : ReadTextDiffPageRequestDto = {
                    OperationId = "op"
                    HandleId = "handle"
                    HandleVersion = "v1"
                    Cursor = "cursor"
                    Background = background
                }

                for background in [ true; false ] do
                    match Mappings.tryReadPageRequest (readPage background) with
                    | Ok request -> Vitest.expect(request.Background).toBe background
                    | Error failure -> failwith failure.Message

                match Mappings.tryOpenDiffRequest memoryPolicy (openRequest "op" "data/a.txt") with
                | Ok request -> Vitest.expect(request.Storage).toEqual memoryPolicy
                | Error failure -> failwith failure.Message
        )

        Vitest.test (
            "open results and resumable results keep their case and payload",
            fun () ->
                let page: DiffPage = {
                    PageId = "page-1"
                    NextCursor = None
                    Parts = [||]
                    Progress = { progress with ScanComplete = true }
                    OutputComplete = true
                    Pending = None
                }

                let info (revision: string option) : DiffSourceInfo = {
                    Path = RepositoryPath.tryCreate "data/a.txt" |> Result.defaultWith failwith
                    Revision = revision |> Option.map (RevisionId.tryCreate >> Result.defaultWith failwith)
                    IsAbsent = false
                    ByteLength = 10L
                    LineCount = Some 2L
                    Encoding = Some "utf-8"
                    EncodingWasChosen = false
                    HasBom = false
                }

                let pageDto = Mappings.diffPage page
                let progressDto = Mappings.scanProgress progress
                let previewDto = Mappings.pendingPreview maximumPreview

                Vitest
                    .expect(
                        Mappings.resumableOpen
                            diskPolicy
                            (Resumable.Ready(OpenDiffResult.NotDiffable DiffBlocker.ProviderUnsupported))
                    )
                    .toEqual (ResumableOpenDto.Ready(OpenDiffResultDto.NotDiffable DiffBlockerDto.ProviderUnsupported))

                match
                    Mappings.resumableOpen
                        diskPolicy
                        (Resumable.Ready(
                            OpenDiffResult.Opened(
                                { Id = "h"; Version = "v" },
                                info (Some "abc"),
                                info None,
                                Resumable.Ready page,
                                DiffStorage.OnDisk
                            )
                        ))
                with
                | ResumableOpenDto.Ready(OpenDiffResultDto.Opened(handle, previous, current, first, storage)) ->
                    Vitest.expect(storage).toEqual DiffStorageDto.OnDisk

                    Vitest.expect(handle).toEqual {
                        DiffHandleDto.Id = "h"
                        Version = "v"
                    }

                    Vitest.expect(previous.Path).toBe "data/a.txt"
                    Vitest.expect(previous.Revision).toEqual (Some "abc")
                    Vitest.expect(current.Revision).toEqual None
                    Vitest.expect(current.LineCount).toEqual (Some "2")
                    Vitest.expect(first).toEqual (ResumablePageDto.Ready pageDto)
                | other -> failwith $"Expected an opened diff, got {other}"

                Vitest
                    .expect(
                        Mappings.resumableOpen diskPolicy (Resumable.Scanning(progress, "c-open", Some maximumPreview))
                    )
                    .toEqual (ResumableOpenDto.Scanning(progressDto, "c-open", Some previewDto))

                Vitest
                    .expect(Mappings.resumablePage (Resumable.Scanning(progress, "c-page", None)))
                    .toEqual (ResumablePageDto.Scanning(progressDto, "c-page", None))

                Vitest
                    .expect(Mappings.resumableParts (Resumable.Ready [| DiffPart.ExpandedContext("gap", [||]) |]))
                    .toEqual (ResumablePartsDto.Ready [| DiffPartDto.ExpandedContext("gap", [||]) |])

                Vitest
                    .expect(Mappings.resumableParts (Resumable.Scanning(progress, "c-expand", None)))
                    .toEqual (ResumablePartsDto.Scanning(progressDto, "c-expand", None))

                Vitest
                    .expect(Mappings.resumableLine (Resumable.Ready(line 3L "text" 1)))
                    .toEqual (ResumableLineDto.Ready(Mappings.diffLine (line 3L "text" 1)))

                Vitest
                    .expect(Mappings.resumableLine (Resumable.Scanning(progress, "c-line", Some maximumPreview)))
                    .toEqual (ResumableLineDto.Scanning(progressDto, "c-line", Some previewDto))

                Vitest.expect(Mappings.diffSourceInfoPair (info (Some "abc"), info None)).toEqual {
                    Previous = Mappings.diffSourceInfo (info (Some "abc"))
                    Current = Mappings.diffSourceInfo (info None)
                }
        )

        Vitest.test (
            "int64 values above 2^53 travel as exact decimal strings",
            fun () ->
                let mapped =
                    Mappings.lineRange {
                        Start = 9007199254740993L
                        Count = 9223372036854775807L
                    }

                Vitest.expect(mapped).toEqual {
                    LineRangeDto.Start = "9007199254740993"
                    Count = "9223372036854775807"
                }

                Vitest.expect(Mappings.tryNonNegativeInt64 "line" mapped.Start).toEqual (Ok 9007199254740993L)
                Vitest.expect(Mappings.tryNonNegativeInt64 "line" mapped.Count).toEqual (Ok 9223372036854775807L)
        )
)

Vitest.describe (
    "Text diff IPC envelope",
    fun () ->
        Vitest.test (
            "a long failure message is cut to 4 KiB of UTF-8 without splitting a character",
            fun () ->
                let message = "a" + String.replicate 3000 "é\U0001F600"
                let mapped = boundFailure (failureWith message "evidence")
                let lastCode = int mapped.Message[mapped.Message.Length - 1]

                Vitest.expect(utf8Bytes mapped.Message).toBeLessThanOrEqual Mappings.MaxTextDiffMessageBytes
                Vitest.expect(utf8Bytes mapped.Message).toBeGreaterThan (Mappings.MaxTextDiffMessageBytes - 4)
                Vitest.expect(message.StartsWith mapped.Message).toBe true
                Vitest.expect(lastCode >= 0xD800 && lastCode <= 0xDBFF).toBe false
                Vitest.expect(mapped.Details).toEqual [| "detail line" |]
                Vitest.expect(mapped.Code).toBe TextDiffFailureCodes.ContentNotText
        )

        Vitest.test (
            "a short failure message and evidence pass unchanged",
            fun () ->
                let mapped = boundFailure (failureWith "The source is not text." "NUL at 3")

                Vitest.expect(mapped.Message).toBe "The source is not text."

                Vitest
                    .expect(mapped.DiffDetail)
                    .toEqual (
                        Some {
                            Side = DiffSideDto.Current
                            Evidence = "NUL at 3"
                        }
                    )
        )

        Vitest.test (
            "diff detail evidence is cut to 256 characters",
            fun () ->
                let mapped = boundFailure (failureWith "not text" (String.replicate 300 "x"))

                Vitest.expect(mapped.DiffDetail |> Option.map _.Evidence.Length).toEqual (Some 256)
                Vitest.expect(mapped.DiffDetail |> Option.map _.Side).toEqual (Some DiffSideDto.Current)
        )

        Vitest.test (
            "evidence whose surrogate pair starts at unit 255 is cut before the pair",
            fun () ->
                let kept = String.replicate 255 "x"
                let mapped = boundFailure (failureWith "not text" (kept + "\U0001F600"))

                Vitest.expect(mapped.DiffDetail |> Option.map _.Evidence).toEqual (Some kept)
        )

        Vitest.test (
            "details lines and affected paths are cut to 256 characters and 4 KiB in total, and only the first warning is kept, cut to 4 KiB",
            fun () ->
                let lines (first: string) =
                    Array.append
                        [| String.replicate 300 first |]
                        (Array.init 20 (fun index -> (string index).PadLeft(2, '0') + String.replicate 248 "x"))

                let details = lines "d"
                let paths = lines "p"

                let failure = {
                    failureWith "not text" "evidence" with
                        Details = details
                        AffectedPaths = paths
                }

                let outcome =
                    match OperationResult.succeeded () with
                    | Succeeded outcome -> {
                        outcome with
                            Warnings = [|
                                {
                                    Code = "long_warning"
                                    Message = String.replicate 5000 "w"
                                }
                                {
                                    Code = "second_warning"
                                    Message = "dropped"
                                }
                            |]
                      }
                    | other -> failwith $"Expected a success, got {other}"

                let mappedFailure = boundFailure failure

                let mapped =
                    match Mappings.textDiffResult id (Succeeded outcome) with
                    | OperationResultDto.Succeeded mapped -> mapped
                    | other -> failwith $"Expected a success, got {other}"

                for bounded, original in
                    [
                        mappedFailure.Details, details
                        mappedFailure.AffectedPaths, paths
                    ] do
                    Vitest.expect(bounded.Length).toBe 16
                    Vitest.expect(bounded[0]).toBe (original[0].Substring(0, 256))

                    Vitest
                        .expect(
                            bounded
                            |> Array.forall (fun line -> line.Length <= Mappings.MaxTextDiffDetailLength)
                        )
                        .toBe
                        true

                    Vitest.expect(bounded |> Array.sumBy utf8Bytes).toBeLessThanOrEqual Mappings.MaxTextDiffDetailsBytes
                    Vitest.expect(bounded[1..]).toEqual original[1..15]

                Vitest.expect(mapped.Warnings |> Array.map _.Message.Length).toEqual [|
                    Mappings.MaxTextDiffWarningBytes
                |]

                Vitest.expect(mapped.Warnings |> Array.map _.Code).toEqual [| "long_warning" |]
        )

        Vitest.test (
            "a partly successful page reply at the worker limit keeps one warning and a failure without lists, within the limit plus their bounded text",
            fun () ->
                let sizeOf textLength =
                    workerBytes (TextDiffProtocol.ResultPayload.ReadPage(Resumable.Ready(pageWith textLength)))

                let page = pageWith (fillTo PageLimitBytes sizeOf)

                let outcome =
                    match OperationResult.succeeded (Resumable.Ready page) with
                    | Succeeded outcome -> {
                        outcome with
                            Warnings =
                                Array.init
                                    20
                                    (fun index -> {
                                        Code = $"warning_{index}"
                                        Message = expensiveText 4096
                                    })
                      }
                    | other -> failwith $"Expected a success, got {other}"

                let failure = {
                    failureWith (expensiveText 4096) (expensiveText 256) with
                        Details = Array.replicate 20 (expensiveText 256)
                        AffectedPaths = Array.replicate 20 (expensiveText 256)
                }

                let payload: Result<OperationResultDto<ResumablePageDto>, exn> =
                    Ok(Mappings.textDiffResult Mappings.resumablePage (PartiallySucceeded(outcome, failure)))

                // One warning, the failure message and the evidence, each at its bound, with every
                // unit escaped to six bytes in JSON, plus a margin for the object keys.
                let extrasBytes =
                    6
                    * (Mappings.MaxTextDiffWarningBytes
                       + Mappings.MaxTextDiffMessageBytes
                       + Mappings.MaxDiffEvidenceLength)
                    + IpcMarginBytes

                Vitest.expect(ipcPayloadBytes payload).toBeLessThanOrEqual (PageLimitBytes + extrasBytes)

                match payload with
                | Ok(OperationResultDto.PartiallySucceeded(mapped, mappedFailure)) ->
                    Vitest.expect(mapped.Value).toEqual (Mappings.resumablePage (Resumable.Ready page))
                    Vitest.expect(mapped.Warnings |> Array.map _.Code).toEqual [| "warning_0" |]
                    Vitest.expect(mappedFailure.Message.Length).toBeLessThanOrEqual Mappings.MaxTextDiffMessageBytes
                    Vitest.expect(mappedFailure.Details).toEqual [||]
                    Vitest.expect(mappedFailure.AffectedPaths).toEqual [||]
                    Vitest.expect(mappedFailure.Code).toBe TextDiffFailureCodes.ContentNotText
                | other -> failwith $"Expected a partial success, got {other}"
        )

        Vitest.test (
            "handle versions, cursors, page ids, gap ids, continuations, preparation tokens and encoding names have no length limit, and a missing one is rejected",
            fun () ->
                let long = String.replicate 10_000 "t"
                let missing = Unchecked.defaultof<string>

                let readPage version cursor : ReadTextDiffPageRequestDto = {
                    OperationId = "op"
                    HandleId = "handle"
                    HandleVersion = version
                    Cursor = cursor
                    Background = false
                }

                let replay pageId : ReplayTextDiffPageRequestDto = {
                    OperationId = "op"
                    HandleId = "handle"
                    HandleVersion = "v1"
                    PageId = pageId
                }

                let expand gapId continuation : ExpandTextDiffRequestDto = {
                    OperationId = "op"
                    HandleId = "handle"
                    HandleVersion = "v1"
                    GapId = gapId
                    FromStart = true
                    Count = 100
                    Continuation = continuation
                }

                let opening = openRequest "op" "data/a.txt"

                match Mappings.tryReadPageRequest (readPage long long) with
                | Ok request ->
                    Vitest.expect(request.Handle.Version).toBe long
                    Vitest.expect(request.Cursor).toBe long
                | Error failure -> failwith failure.Message

                match Mappings.tryExpandRequest (expand long (Some long)) with
                | Ok request -> Vitest.expect(request.Continuation).toEqual (Some long)
                | Error failure -> failwith failure.Message

                match Mappings.tryReplayPageRequest (replay long) with
                | Ok request -> Vitest.expect(request.PageId).toBe long
                | Error failure -> failwith failure.Message

                match
                    Mappings.tryOpenDiffRequest diskPolicy {
                        opening with
                            PreparationTokenId = Some long
                            PreviousEncoding = Some long
                            CurrentEncoding = Some long
                            Continuation = Some long
                    }
                with
                | Ok request ->
                    Vitest.expect(request.Preparation).toEqual (Some { PreparationToken.Id = long })
                    Vitest.expect(request.PreviousEncoding).toEqual (Some long)
                    Vitest.expect(request.CurrentEncoding).toEqual (Some long)
                    Vitest.expect(request.Continuation).toEqual (Some long)
                | Error failure -> failwith failure.Message

                expectInvalid (Mappings.tryReadPageRequest (readPage missing "cursor"))
                expectInvalid (Mappings.tryReadPageRequest (readPage "v1" missing))
                expectInvalid (Mappings.tryReplayPageRequest (replay missing))
                expectInvalid (Mappings.tryExpandRequest (expand missing None))

                expectInvalid (
                    Mappings.tryDiffHandle {
                        OperationId = "op"
                        HandleId = "handle"
                        HandleVersion = missing
                    }
                )
        )

        Vitest.test (
            "operation ids and handle ids longer than 128 characters are rejected",
            fun () ->
                let atLimit = String.replicate 128 "a"
                let overLimit = String.replicate 129 "a"

                match Mappings.tryReadLineRequest (lineRequest atLimit atLimit "0" "0") with
                | Ok request -> Vitest.expect(request.Handle.Id).toBe atLimit
                | Error failure -> failwith failure.Message

                expectInvalid (Mappings.tryReadLineRequest (lineRequest overLimit "handle" "0" "0"))
                expectInvalid (Mappings.tryReadLineRequest (lineRequest "op" overLimit "0" "0"))

                expectInvalid (
                    Mappings.tryDiffHandle {
                        OperationId = "op"
                        HandleId = overLimit
                        HandleVersion = "v1"
                    }
                )

                expectInvalid (
                    Mappings.tryOpenDiffRequest diskPolicy {
                        OperationId = overLimit
                        Path = "data/a.txt"
                        PreviousPath = None
                        PreparationTokenId = None
                        PreviousEncoding = None
                        CurrentEncoding = None
                        ContextLines = 3
                        Continuation = None
                    }
                )
        )

        Vitest.test (
            "line numbers and offsets must be non-negative decimal integers",
            fun () ->
                for text in [ "-1"; "abc"; "1.5"; ""; " 1"; "99999999999999999999" ] do
                    expectInvalid (Mappings.tryReadLineRequest (lineRequest "op" "handle" text "0"))
                    expectInvalid (Mappings.tryReadLineRequest (lineRequest "op" "handle" "0" text))

                match Mappings.tryReadLineRequest (lineRequest "op" "handle" "12" "8192") with
                | Ok request ->
                    Vitest.expect(request.Line).toEqual 12L
                    Vitest.expect(request.OffsetUtf16).toEqual 8192L
                    Vitest.expect(request.Side).toEqual DiffSide.Current
                | Error failure -> failwith failure.Message
        )
)

Vitest.describe (
    "Text diff handle registry",
    fun () ->
        Vitest.test (
            "a handle arriving after its window closed is closed at once, and one arriving after a reload is recorded under the same window",
            fun () -> promise {
                let! root = TestHelpers.createTempDirectoryAsync "swate-text-diff-registry-"
                let originalIsWindowAlive = TextDiffHandles.isWindowAlive
                let closedWindow = 7
                let reloadedWindow = 5
                TextDiffHandles.isWindowAlive <- fun windowId -> windowId <> closedWindow

                let closes = ResizeArray<string * int option>()
                let closed, resolveClosed = TestHelpers.deferred ()
                let bothClosed, resolveBothClosed = TestHelpers.deferred ()

                let unexpected (name: string) : Async<OperationResult<'T>> = async {
                    return failwith $"{name} was not expected."
                }

                // The close records the window the host assigns to its operation, which is
                // the owner the library checks.
                let service: TextDiffService = {
                    Open = fun _ _ -> unexpected "Open"
                    ReadPage = fun _ _ -> unexpected "ReadPage"
                    ReplayPage = fun _ _ -> unexpected "ReplayPage"
                    Expand = fun _ _ -> unexpected "Expand"
                    ReadLine = fun _ _ -> unexpected "ReadLine"
                    GetSourceInfo = fun _ _ -> unexpected "GetSourceInfo"
                    Close =
                        fun handle context -> async {
                            closes.Add(
                                handle.Id,
                                WorkspaceSessionHost.get().TryGetOperationWindowId context.OperationId
                            )

                            resolveClosed ()

                            if closes.Count = 2 then
                                resolveBothClosed ()

                            return OperationResult.succeeded ()
                        }
                }

                // The registry closes the handles of a window through the open session of the workspace.
                let host =
                    WorkspaceSessionHost.WorkspaceSessionHost(textDiffRuntime root (Promise.lift ()) service)

                WorkspaceSessionHost.initialize host

                let handle (id: string) : DiffHandle = { Id = id; Version = "v1" }

                try
                    let! opened =
                        host.OpenSession(root, TestHelpers.detached "open-registry-session")
                        |> Async.StartAsPromise

                    let workspaceRoot =
                        (TestHelpers.expectValue "session open" opened).Binding.WorkspaceRoot

                    TextDiffHandles.recordOrClose (Some closedWindow) workspaceRoot service (handle "closed-window")
                    TextDiffHandles.windowReloaded reloadedWindow
                    TextDiffHandles.recordOrClose (Some reloadedWindow) workspaceRoot service (handle "after-reload")

                    do! within 2000 "Closing the handle of the closed window" closed

                    Vitest.expect(closes |> Seq.toArray).toEqual [| "closed-window", Some closedWindow |]

                    // The handle of the closed window was closed on arrival and never recorded. The one
                    // of the reloaded window is recorded, so closing both windows closes only that one.
                    TextDiffHandles.windowClosed closedWindow
                    TextDiffHandles.windowReloaded reloadedWindow
                    do! within 2000 "Closing the recorded handle" bothClosed

                    Vitest.expect(closes |> Seq.toArray).toEqual [|
                        "closed-window", Some closedWindow
                        "after-reload", Some reloadedWindow
                    |]
                finally
                    TextDiffHandles.isWindowAlive <- originalIsWindowAlive
                    TextDiffHandles.windowClosed reloadedWindow
                    WorkspaceSessionHost.resetForTests ()

                do! host.CloseAll() |> Async.StartAsPromise
                do! TestHelpers.removeDirectoryAsync root
            }
        )

        Vitest.test (
            "reloading and closing a window close its recorded handles as operations of that window",
            fun () -> promise {
                let! root = TestHelpers.createTempDirectoryAsync "swate-text-diff-window-close-"
                let reloadedWindow = 5
                let closedWindow = 7
                let originalIsWindowAlive = TextDiffHandles.isWindowAlive
                TextDiffHandles.isWindowAlive <- fun _ -> true
                let closes = ResizeArray<string * int option>()
                let allClosed, resolveAllClosed = TestHelpers.deferred ()
                let sentinelClosed, resolveSentinelClosed = TestHelpers.deferred ()

                let service = {
                    unexpectedService with
                        Close =
                            fun handle context -> async {
                                closes.Add(
                                    handle.Id,
                                    WorkspaceSessionHost.get().TryGetOperationWindowId context.OperationId
                                )

                                if closes.Count = 3 then
                                    resolveAllClosed ()

                                if handle.Id = "sentinel" then
                                    resolveSentinelClosed ()

                                return OperationResult.succeeded ()
                            }
                }

                let host =
                    WorkspaceSessionHost.WorkspaceSessionHost(textDiffRuntime root (Promise.lift ()) service)

                WorkspaceSessionHost.initialize host

                try
                    let! opened =
                        host.OpenSession(root, TestHelpers.detached "open-session")
                        |> Async.StartAsPromise

                    let hosted = TestHelpers.expectValue "session open" opened

                    let record windowId handleId =
                        TextDiffHandles.recordOrClose (Some windowId) hosted.Binding.WorkspaceRoot service {
                            Id = handleId
                            Version = "v1"
                        }

                    record reloadedWindow "reloaded-1"
                    record reloadedWindow "reloaded-2"
                    record closedWindow "closed-1"

                    Vitest.expect(closes.Count).toBe 0

                    TextDiffHandles.windowReloaded reloadedWindow
                    TextDiffHandles.windowClosed closedWindow
                    do! within 2000 "Closing the three handles" allClosed

                    Vitest.expect(closes |> Seq.sort |> Seq.toArray).toEqual [|
                        "closed-1", Some closedWindow
                        "reloaded-1", Some reloadedWindow
                        "reloaded-2", Some reloadedWindow
                    |]

                    // The closed handles left the registry. A second round of closes finds only the
                    // sentinel, which is recorded after the first round.
                    record reloadedWindow "sentinel"
                    TextDiffHandles.windowReloaded reloadedWindow
                    TextDiffHandles.windowClosed closedWindow
                    do! within 2000 "Closing the sentinel" sentinelClosed

                    Vitest.expect(closes |> Seq.sort |> Seq.toArray).toEqual [|
                        "closed-1", Some closedWindow
                        "reloaded-1", Some reloadedWindow
                        "reloaded-2", Some reloadedWindow
                        "sentinel", Some reloadedWindow
                    |]
                finally
                    TextDiffHandles.isWindowAlive <- originalIsWindowAlive
                    TextDiffHandles.windowClosed reloadedWindow
                    WorkspaceSessionHost.resetForTests ()

                do! host.CloseAll() |> Async.StartAsPromise
                do! TestHelpers.removeDirectoryAsync root
            }
        )

        Vitest.test (
            "the text diff IPC handlers run every call as an operation of the calling window, and the service answers another window's handle like a closed one",
            fun () -> promise {
                let! root = TestHelpers.createTempDirectoryAsync "swate-text-diff-ipc-owner-"
                let windowA = 81
                let windowB = 82
                let originalIsWindowAlive = TextDiffHandles.isWindowAlive
                TextDiffHandles.isWindowAlive <- fun _ -> true
                let sessionGate, releaseSession = TestHelpers.deferred ()
                let closes = ResizeArray<string * int option>()
                let reloadHandleClosed, resolveReloadHandleClosed = TestHelpers.deferred ()
                let reads = ResizeArray<string>()

                // The stub keeps the contract the library documents on TextDiffService. A handle
                // belongs to the owner of the operation that opened it, other owners get
                // diff_session_closed, and Close of another owner's handle changes nothing.
                let owners = System.Collections.Generic.Dictionary<string, string>()

                let ownedBy (handle: DiffHandle) (context: OperationContext) =
                    match owners.TryGetValue handle.Id with
                    | true, owner -> owner = WorkspaceSessionHost.windowOwnerOf context
                    | _ -> false

                let closedSession () : OperationResult<'T> =
                    Failed(
                        OperationFailure.create
                            Validation
                            TextDiffFailureCodes.SessionClosed
                            "The diff session is closed."
                    )

                let owned (handle: DiffHandle) (context: OperationContext) (call: unit -> Async<OperationResult<'T>>) =
                    if ownedBy handle context then
                        call ()
                    else
                        async { return closedSession () }

                let service = {
                    unexpectedService with
                        Open =
                            fun request context -> async {
                                let path = RepositoryPath.value request.Path
                                let handleId = if path = "reload.txt" then "h-reload" else "h-a"
                                owners[handleId] <- WorkspaceSessionHost.windowOwnerOf context
                                return OperationResult.succeeded (Resumable.Ready(openedDiff handleId path))
                            }
                        ReadPage =
                            fun request context ->
                                owned
                                    request.Handle
                                    context
                                    (fun () -> async {
                                        reads.Add request.Handle.Id
                                        return! unexpected "ReadPage"
                                    })
                        ReplayPage =
                            fun request context -> owned request.Handle context (fun () -> unexpected "ReplayPage")
                        Expand = fun request context -> owned request.Handle context (fun () -> unexpected "Expand")
                        ReadLine = fun request context -> owned request.Handle context (fun () -> unexpected "ReadLine")
                        GetSourceInfo =
                            fun request context -> owned request.Handle context (fun () -> unexpected "GetSourceInfo")
                        Close =
                            fun handle context -> async {
                                if ownedBy handle context then
                                    closes.Add(
                                        handle.Id,
                                        WorkspaceSessionHost.get().TryGetOperationWindowId context.OperationId
                                    )

                                    if handle.Id = "h-reload" then
                                        resolveReloadHandleClosed ()

                                return OperationResult.succeeded ()
                            }
                }

                let host =
                    WorkspaceSessionHost.WorkspaceSessionHost(textDiffRuntime root sessionGate service)

                WorkspaceSessionHost.initialize host
                registerWindows root [ windowA; windowB ]
                let apiA = Main.IPC.IVersionControlApi.api (TestHelpers.ipcEvent windowA)
                let apiB = Main.IPC.IVersionControlApi.api (TestHelpers.ipcEvent windowB)

                try
                    // The window reloads while the session of the open is still opening.
                    let reloadOpen = apiA.openTextDiff (openRequest "open-reload" "reload.txt")
                    TextDiffHandles.windowReloaded windowA
                    releaseSession ()
                    let! _ = reloadOpen

                    // The handle that arrives after the reload is recorded under the same window, and
                    // nothing closes it yet.
                    Vitest.expect(closes.Count).toBe 0

                    let! opened = apiA.openTextDiff (openRequest "open-a" "a.txt")
                    TestHelpers.expectDtoValue "open from window A" opened |> ignore

                    let! readFromB =
                        apiB.readTextDiffPage {
                            OperationId = "read-b"
                            HandleId = "h-a"
                            HandleVersion = "v1"
                            Cursor = "cursor"
                            Background = false
                        }

                    Vitest.expect(failureCodeOf readFromB).toBe TextDiffFailureCodes.SessionClosed
                    Vitest.expect(reads.Count).toBe 0

                    let handleRequest operationId : TextDiffHandleRequestDto = {
                        OperationId = operationId
                        HandleId = "h-a"
                        HandleVersion = "v1"
                    }

                    let! replayFromB =
                        apiB.replayTextDiffPage {
                            OperationId = "replay-b"
                            HandleId = "h-a"
                            HandleVersion = "v1"
                            PageId = "page"
                        }

                    let! expandFromB =
                        apiB.expandTextDiff {
                            OperationId = "expand-b"
                            HandleId = "h-a"
                            HandleVersion = "v1"
                            GapId = "gap"
                            FromStart = true
                            Count = 5
                            Continuation = None
                        }

                    let! lineFromB =
                        apiB.readTextDiffLine {
                            OperationId = "line-b"
                            HandleId = "h-a"
                            HandleVersion = "v1"
                            Side = DiffSideDto.Previous
                            Line = "1"
                            OffsetUtf16 = "0"
                            MaxUtf16 = 100
                            Continuation = None
                        }

                    let! infoFromB = apiB.getTextDiffSourceInfo (handleRequest "info-b")

                    Vitest.expect(failureCodeOf replayFromB).toBe TextDiffFailureCodes.SessionClosed
                    Vitest.expect(failureCodeOf expandFromB).toBe TextDiffFailureCodes.SessionClosed
                    Vitest.expect(failureCodeOf lineFromB).toBe TextDiffFailureCodes.SessionClosed
                    Vitest.expect(failureCodeOf infoFromB).toBe TextDiffFailureCodes.SessionClosed

                    let! closeFromB = apiB.closeTextDiff (handleRequest "close-b")
                    TestHelpers.expectDtoValue "close from window B" closeFromB |> ignore
                    Vitest.expect(closes.Count).toBe 0

                    let! closeFromA = apiA.closeTextDiff (handleRequest "close-a")
                    TestHelpers.expectDtoValue "close from window A" closeFromA |> ignore
                    Vitest.expect(closes |> Seq.toArray).toEqual [| "h-a", Some windowA |]

                    // The close removed h-a from the registry, and h-reload is still recorded. Closing
                    // window A closes only h-reload.
                    TextDiffHandles.windowClosed windowA
                    do! within 2000 "Closing the handle that arrived after the reload" reloadHandleClosed

                    Vitest.expect(closes |> Seq.toArray).toEqual [| "h-a", Some windowA; "h-reload", Some windowA |]
                finally
                    TextDiffHandles.isWindowAlive <- originalIsWindowAlive
                    TextDiffHandles.windowClosed windowA
                    TextDiffHandles.windowClosed windowB
                    Main.ArcVault.ARC_VAULTS.Vaults.Clear()
                    TestHelpers.electronMock?reset () |> ignore
                    WorkspaceSessionHost.resetForTests ()

                do! host.CloseAll() |> Async.StartAsPromise
                do! TestHelpers.removeDirectoryAsync root
            }
        )

        Vitest.test (
            "the open of a diff sends the storage policy of the session settings and answers where the diff lives",
            fun () -> promise {
                let! root = TestHelpers.createTempDirectoryAsync "swate-text-diff-storage-"
                let window = 93
                let originalIsWindowAlive = TextDiffHandles.isWindowAlive
                TextDiffHandles.isWindowAlive <- fun _ -> true
                let policies = ResizeArray<DiffStoragePolicy>()
                let mutable answer = DiffStorage.OnDisk

                let service = {
                    unexpectedService with
                        Open =
                            fun request _ -> async {
                                policies.Add request.Storage
                                let path = RepositoryPath.value request.Path

                                return
                                    OperationResult.succeeded (Resumable.Ready(openedDiffWith answer "h-storage" path))
                            }
                        Close = fun _ _ -> async { return OperationResult.succeeded () }
                }

                let host =
                    WorkspaceSessionHost.WorkspaceSessionHost(textDiffRuntime root (Promise.lift ()) service)

                WorkspaceSessionHost.initialize host
                registerWindows root [ window ]
                let api = Main.IPC.IVersionControlApi.api (TestHelpers.ipcEvent window)

                let setSettings (operationId: string) (limitMb: int) =
                    api.setStoragePolicySettings {
                        OperationId = operationId
                        Settings = {
                            AutoPolicyThresholdMb = None
                            MaterializeLargeObjects = false
                            DiffIndexingLimitMb = Some limitMb
                        }
                    }

                let storageOf (opened: Result<OperationResultDto<ResumableOpenDto>, exn>) =
                    match (TestHelpers.expectDtoValue "open" opened).Value with
                    | ResumableOpenDto.Ready(OpenDiffResultDto.Opened(_, _, _, _, storage)) -> storage
                    | other -> failwith $"Expected an opened diff, got {other}"

                try
                    let! settings = setSettings "storage-settings-1" 5
                    TestHelpers.expectDtoValue "set the limit" settings |> ignore
                    answer <- DiffStorage.InMemory 5242880L
                    let! inMemory = api.openTextDiff (openRequest "open-memory" "a.txt")

                    Vitest.expect(policies.[0]).toEqual (DiffStoragePolicy.MemoryOnly 5242880L)

                    Vitest
                        .expect(storageOf inMemory)
                        .toEqual (DiffStorageDto.InMemory("5242880", MemoryCauseDto.BySetting))

                    let! settings = setSettings "storage-settings-2" 2048
                    TestHelpers.expectDtoValue "set the limit again" settings |> ignore
                    answer <- DiffStorage.OnDisk
                    let! onDisk = api.openTextDiff (openRequest "open-disk" "a.txt")

                    Vitest.expect(policies.[1]).toEqual (DiffStoragePolicy.PreferDisk(1127428916L, 67108864L))
                    Vitest.expect(storageOf onDisk).toEqual DiffStorageDto.OnDisk

                    // The library chose memory for a policy that preferred disk.
                    answer <- DiffStorage.InMemory 67108864L
                    let! lowSpace = api.openTextDiff (openRequest "open-low-space" "a.txt")

                    Vitest
                        .expect(storageOf lowSpace)
                        .toEqual (DiffStorageDto.InMemory("67108864", MemoryCauseDto.ByLowSpace))
                finally
                    TextDiffHandles.isWindowAlive <- originalIsWindowAlive
                    TextDiffHandles.windowClosed window
                    Main.ArcVault.ARC_VAULTS.Vaults.Clear()
                    TestHelpers.electronMock?reset () |> ignore
                    WorkspaceSessionHost.resetForTests ()

                do! host.CloseAll() |> Async.StartAsPromise
                do! TestHelpers.removeDirectoryAsync root
            }
        )

        Vitest.test (
            "an operation id in use is refused, and a window cancels only its own operations",
            fun () -> promise {
                let! root = TestHelpers.createTempDirectoryAsync "swate-text-diff-operation-ids-"
                let windowA = 91
                let windowB = 92
                let originalIsWindowAlive = TextDiffHandles.isWindowAlive
                TextDiffHandles.isWindowAlive <- fun _ -> true
                let sessionGate, releaseSession = TestHelpers.deferred ()

                let service = {
                    unexpectedService with
                        Open =
                            fun request _ -> async {
                                let path = RepositoryPath.value request.Path
                                return OperationResult.succeeded (Resumable.Ready(openedDiff "h-ids" path))
                            }
                        Close = fun _ _ -> async { return OperationResult.succeeded () }
                }

                let host =
                    WorkspaceSessionHost.WorkspaceSessionHost(textDiffRuntime root sessionGate service)

                WorkspaceSessionHost.initialize host
                registerWindows root [ windowA; windowB ]
                let apiA = Main.IPC.IVersionControlApi.api (TestHelpers.ipcEvent windowA)
                let apiB = Main.IPC.IVersionControlApi.api (TestHelpers.ipcEvent windowB)

                try
                    // The open of window A waits for its session, so its operation stays registered.
                    let openFromA = apiA.openTextDiff (openRequest "shared-id" "a.txt")
                    let! openFromB = apiB.openTextDiff (openRequest "shared-id" "b.txt")

                    Vitest.expect(failureCodeOf openFromB).toBe VersionControlCodes.OperationIdInUse
                    Vitest.expect(host.TryGetOperationWindowId "shared-id").toEqual (Some windowA)

                    let! canceledByB = apiB.cancelOperation { OperationId = "shared-id" }
                    Vitest.expect(canceledByB).toEqual (Ok false)

                    let! canceledByA = apiA.cancelOperation { OperationId = "shared-id" }
                    Vitest.expect(canceledByA).toEqual (Ok true)

                    releaseSession ()
                    let! _ = openFromA
                    Vitest.expect(host.TryGetOperationWindowId "shared-id").toEqual None
                finally
                    TextDiffHandles.isWindowAlive <- originalIsWindowAlive
                    TextDiffHandles.windowClosed windowA
                    TextDiffHandles.windowClosed windowB
                    Main.ArcVault.ARC_VAULTS.Vaults.Clear()
                    TestHelpers.electronMock?reset () |> ignore
                    WorkspaceSessionHost.resetForTests ()

                do! host.CloseAll() |> Async.StartAsPromise
                do! TestHelpers.removeDirectoryAsync root
            }
        )
)
