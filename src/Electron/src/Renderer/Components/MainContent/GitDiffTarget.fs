module Renderer.Components.MainContent.GitDiffTarget

open Fable.Core
open Feliz
open Renderer.Types
open Renderer.Context.GitWorkflow
open Arcitisa.Components.Page.GitComparison
open Arcitisa.Electron.Shared.VersionControlTypes

module Presentation = Renderer.GitDiffPresentation
module Paged = Arcitisa.Components.Page.GitComparison.GitPagedDiffTypes

[<AllowNullLiteral>]
type private IPerformance =
    /// Removes every User Timing measure of the page.
    abstract clearMeasures: unit -> unit

[<Global("performance")>]
let private performance: IPerformance = jsNative

let private clearPerformanceMeasures () : unit = performance.clearMeasures ()

/// What the diff does in the background while the user reads it.
[<RequireQualifiedAccess>]
type BackgroundReading =
    /// The loader reads the remaining pages on its own.
    | Indexing
    /// The pages read add up to the indexing limit, so the loader stopped.
    | LimitReached
    /// The worker session closed during a background read, and the next request continues.
    | Paused
    /// A memory diff reads the remaining pages on its own.
    | MemoryIndexing
    /// A memory diff has nothing running and no stop to report. The bar names where the diff lives.
    | MemoryIdle
    /// A memory diff stopped its background reading at most of its budget. The rest is kept for
    /// the actions of the user.
    | MemoryShareKept
    /// A read was refused because the temp drive is low on free space. Continue retries it.
    | TempSpaceStopped
    /// A read was refused because the memory budget is used up. Only a reopen reads on.
    | MemoryBudgetStopped
    /// The last read of the next page failed, and the continue button asks again.
    | ReadFailed of message: string

/// The background state of the diff, or None when there is nothing to tell: a disk diff whose
/// indexing finished or never started, or a diff that shows no rows. A memory diff always has a
/// state. A failed read shows its message only when the loader kept one.
let backgroundReadingOf (limitMb: int) (page: GitDiffPageData) : BackgroundReading option =
    let showsRows =
        match page.Status with
        | GitDiffPageStatus.Ready
        | GitDiffPageStatus.LoadingNext
        | GitDiffPageStatus.Reopening _ -> true
        | _ -> false

    let inMemory =
        match page.Storage with
        | Some(DiffStorageDto.InMemory _) -> true
        | _ -> false

    // A memory diff always names its storage when nothing more specific applies.
    let idle = if inMemory then Some BackgroundReading.MemoryIdle else None

    if not showsRows then
        None
    else
        match page.Stop with
        | Some GitDiffStop.MemoryBudget -> Some BackgroundReading.MemoryBudgetStopped
        | Some GitDiffStop.TempSpaceLow -> Some BackgroundReading.TempSpaceStopped
        | _ when page.NextCursor.IsNone -> idle
        | Some GitDiffStop.MemoryShareUsed -> Some BackgroundReading.MemoryShareKept
        | _ when page.NextFailed ->
            match page.NextFailure with
            | Some message -> Some(BackgroundReading.ReadFailed message)
            | None -> idle
        | _ when page.IndexingPaused -> Some BackgroundReading.Paused
        | _ when page.Indexing ->
            Some(
                if inMemory then
                    BackgroundReading.MemoryIndexing
                else
                    BackgroundReading.Indexing
            )
        | _ when GitDiffPageLoader.indexingLimitReached limitMb page -> Some BackgroundReading.LimitReached
        | _ -> idle

/// The note at the end of the pages read when a stop ends the reading, and whether the continue
/// button stays. A low-space stop can be retried. The full memory budget cannot.
let endNoteOf (page: GitDiffPageData) : string option * bool =
    match page.Stop with
    | Some GitDiffStop.TempSpaceLow -> Some "Reading stopped because the temp drive is low on free space.", true
    | Some GitDiffStop.MemoryBudget -> Some "Reading stopped because the memory budget of the diff is used up.", false
    | _ -> None, true

/// The reason a failed gap or line control names, when the library refused its request because
/// of where the diff keeps its data.
let failureNoteOf (page: GitDiffPageData) : string option =
    match page.FailureCause with
    | Some GitDiffStop.TempSpaceLow -> Some "The temp drive is low on free space. Free space on it, then try again."
    | Some GitDiffStop.MemoryBudget -> Some "The memory budget of the diff is used up. Reopen the diff to read more."
    | _ -> None

/// The key the viewer uses to ask for the next page at most once. It is the id of the last
/// loaded page, which changes when a new page joins or a reopen replaces the pages.
let nextPageKeyOf (page: GitDiffPageData) : string option =
    page.Pages |> Array.tryLast |> Option.map _.PageId

[<ReactComponent>]
let Main (page: GitDiffPageData) =
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let gitStateCtx = Renderer.Context.GitStateContext.useGitStateCtx ()
    let send = gitStateCtx.sendDiffMsg
    let generation = page.Generation
    let indexingLimitMb = gitStateCtx.state.DiffIndexingLimitMb

    // The development build of React records a User Timing measure for every component render
    // whose props changed, with the changed props copied into the measure. The browser keeps
    // these measures until they are cleared, outside the JavaScript heap. With thousands of diff
    // rows as props, each loaded page added megabytes that never came back. Clearing them when
    // the pages change and when the diff closes keeps that memory bounded. The production build
    // records no measures, and ARCitISA reads none.
    React.useEffect ((fun () -> FsReact.createDisposable clearPerformanceMeasures), [| box page.Pages |])

    // Once the first page is ready, the loader reads every other page in the background, so the
    // viewer knows the extent of the whole diff.
    React.useEffect (
        (fun () -> GitDiffPageLoader.indexingRequest indexingLimitMb page |> Option.iter send),
        [|
            box generation
            box page.Indexing
            box page.NextFailed
            box page.NextCursor
            box page.Status
            box page.Stop
            box indexingLimitMb
        |]
    )

    // The viewer rebuilds the rows of the parts only when the parts array changes.
    // The parts are collected only when the pages change.
    let parts =
        React.useMemo ((fun () -> page.Pages |> Array.collect _.Parts), [| box page.Pages |])

    // The part range the viewer reports for a replay maps back to page ids. A replay is rare, so
    // the page of each part is looked up when one is asked for.
    let visiblePages (firstPart: int) (lastPart: int) =
        if firstPart < 0 || lastPart < firstPart then
            []
        else
            let partPages =
                page.Pages
                |> Array.collect (fun windowPage -> windowPage.Parts |> Array.map (fun _ -> windowPage.PageId))

            [
                for index in firstPart .. min lastPart (partPages.Length - 1) -> partPages.[index]
            ]
            |> List.distinct

    let previousTitle, currentTitle =
        match page.SourceInfos with
        | Some infos -> Presentation.sourceTitle infos.Previous, Presentation.sourceTitle infos.Current
        | None -> page.PreviousPath |> Option.defaultValue page.Path, page.Path

    let endNote, canRequestNext = endNoteOf page

    // The bar shows the background state on the left, so the viewer leaves it out of its rows.
    let backgroundNote =
        match backgroundReadingOf indexingLimitMb page with
        | None -> Html.none
        | Some reading ->
            let progress =
                match page.Progress |> Option.map Presentation.progress with
                | Some value ->
                    $" {Paged.progressPercentage value}%% ({Paged.formatBytes value.ValidatedBytes} / {Paged.formatBytes value.TotalBytes})"
                | None -> ""

            // Why the diff lives in memory and how large its budget is.
            let memory =
                match page.Storage with
                | Some(DiffStorageDto.InMemory(budgetBytes, cause)) ->
                    let why =
                        match cause with
                        | MemoryCauseDto.BySetting -> "because of the indexing limit setting"
                        | MemoryCauseDto.ByLowSpace -> "because the temp drive is low on space"

                    $"The diff is kept in memory {why} (budget {Paged.formatBytes (Presentation.number budgetBytes)})."
                | _ -> "The diff is kept in memory."

            let state, icon, text =
                match reading with
                | BackgroundReading.Indexing ->
                    "indexing",
                    "swt:loading swt:loading-spinner swt:loading-xs",
                    $"Indexing the diff in the background{progress}. The scrollbar is not exact yet."
                | BackgroundReading.MemoryIndexing ->
                    "memory",
                    "swt:loading swt:loading-spinner swt:loading-xs",
                    $"{memory} Indexing the diff in the background{progress}. The scrollbar is not exact yet."
                | BackgroundReading.MemoryIdle -> "memory", "swt:iconify swt:fluent--info-24-regular swt:size-4", memory
                | BackgroundReading.MemoryShareKept ->
                    "memory",
                    "swt:iconify swt:fluent--info-24-regular swt:size-4",
                    $"{memory} Background reading stopped, and the rest of the budget is kept for your actions. The scrollbar covers the part read so far."
                | BackgroundReading.TempSpaceStopped ->
                    "stopped",
                    "swt:iconify swt:fluent--warning-24-regular swt:size-4",
                    "Reading stopped because the temp drive is low on free space. Free space on the temp drive, then press Continue."
                | BackgroundReading.MemoryBudgetStopped ->
                    let advice =
                        match page.Storage with
                        | Some(DiffStorageDto.InMemory(_, MemoryCauseDto.BySetting)) ->
                            "Raise the indexing limit to 64 or more to use temp files, then reopen the diff."
                        | Some(DiffStorageDto.InMemory(_, MemoryCauseDto.ByLowSpace)) ->
                            "Free space on the temp drive, then reopen the diff."
                        | _ -> "Reopen the diff to read on."

                    "stopped",
                    "swt:iconify swt:fluent--warning-24-regular swt:size-4",
                    $"Reading stopped because the memory budget is used up. {advice}"
                | BackgroundReading.ReadFailed message ->
                    "failed", "swt:iconify swt:fluent--error-circle-24-regular swt:size-4", message
                | BackgroundReading.LimitReached ->
                    "limit",
                    "swt:iconify swt:fluent--info-24-regular swt:size-4",
                    $"Background indexing stopped at the limit of {indexingLimitMb} MB. The scrollbar covers the part read so far."
                | BackgroundReading.Paused ->
                    "paused",
                    "swt:iconify swt:fluent--pause-24-regular swt:size-4",
                    "Background indexing paused. The scrollbar covers the part read so far. It continues when you scroll or press Continue."

            Html.div [
                prop.testId "renderer-git-diff-status"
                prop.custom ("data-state", state)
                prop.custom ("data-limit-mb", indexingLimitMb)
                prop.className "swt:flex swt:min-w-0 swt:items-center swt:gap-2 swt:text-xs swt:text-base-content/70"
                prop.children [
                    Html.span [ prop.className [ "swt:shrink-0"; icon ] ]
                    Html.span [
                        prop.className "swt:min-w-0 swt:truncate"
                        prop.text text
                    ]
                ]
            ]

    Html.div [
        prop.className "swt:flex swt:h-full swt:w-full swt:min-h-0 swt:min-w-0 swt:flex-col"
        prop.children [
            Html.div [
                prop.className
                    "swt:flex swt:items-center swt:justify-between swt:gap-3 swt:border-b swt:border-base-content/10 swt:bg-base-100 swt:px-4 swt:py-2"
                prop.children [
                    backgroundNote
                    Html.button [
                        prop.testId "renderer-git-diff-close"
                        prop.className
                            "swt:btn swt:btn-ghost swt:btn-sm swt:ml-auto swt:shrink-0 swt:gap-2 swt:normal-case"
                        prop.onClick (fun _ -> pageStateCtx.setState None)
                        prop.children [
                            Html.span [
                                prop.className "swt:iconify swt:fluent--dismiss-24-regular swt:size-4"
                            ]
                            Html.span "Close"
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "swt:min-h-0 swt:min-w-0 swt:flex-1 swt:p-4"
                prop.children [
                    GitPagedDiffViewer.Viewer(
                        parts = parts,
                        status = Presentation.status page.Status,
                        progress = (page.Progress |> Option.map Presentation.progress),
                        hasMore = page.NextCursor.IsSome,
                        indexing = (page.Indexing && not page.NextFailed),
                        hideIndexingStatus = true,
                        nextFailed = page.NextFailed,
                        outputComplete = page.OutputComplete,
                        ?pending = (page.Pending |> Option.map Presentation.pending),
                        ?requestNext =
                            (if canRequestNext then
                                 Some(fun () -> send (GitDiffMsg.LoadNext generation))
                             else
                                 None),
                        ?endNote = endNote,
                        ?failureNote = failureNoteOf page,
                        requestExpand = (fun gapId fromStart -> send (GitDiffMsg.Expand(generation, gapId, fromStart))),
                        expandingGaps = Array.ofList page.ExpandingGaps,
                        requestLineSlice =
                            (fun side line offsetUtf16 ->
                                send (
                                    GitDiffMsg.LoadLineSlice(generation, Presentation.sideDto side, line, offsetUtf16)
                                )
                            ),
                        requestLineBefore =
                            (fun side line displayedStart ->
                                send (
                                    GitDiffMsg.LoadLineBefore(
                                        generation,
                                        Presentation.sideDto side,
                                        line,
                                        displayedStart
                                    )
                                )
                            ),
                        pendingLineSlices = Array.ofList page.PendingLineSlices,
                        requestReplay =
                            (fun pageId firstPart lastPart ->
                                send (GitDiffMsg.Replay(generation, pageId, visiblePages firstPart lastPart))
                            ),
                        pendingReplays = Option.toArray page.PendingReplay,
                        chooseEncoding =
                            (fun side encoding ->
                                send (GitDiffMsg.ChooseEncoding(generation, Presentation.sideDto side, encoding))
                            ),
                        previousTitle = previousTitle,
                        currentTitle = currentTitle,
                        ?changeKind = page.ChangeKind,
                        testIdPrefix = "renderer-git-diff",
                        // Eviction and replay keep the last page, so the continue row does not read
                        // again. A Scanning answer adds no page either. The cursor does change with
                        // each Scanning answer, and a diff paused after one would show the viewer an
                        // unused cursor and read on its closed session.
                        ?nextPageKey = nextPageKeyOf page,
                        failedGaps = Array.ofList page.FailedGaps,
                        failedLineSlices = Array.ofList page.FailedLineSlices,
                        failedReplays = Array.ofList page.FailedReplays,
                        ?scrollTarget = page.ScrollTarget
                    )
                ]
            ]
        ]
    ]
