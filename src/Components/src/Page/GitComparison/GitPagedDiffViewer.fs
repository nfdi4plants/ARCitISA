namespace Arcitisa.Components.Page.GitComparison

open System
open System.Collections.Generic
open Browser.Types
open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Page
open Arcitisa.Components.Page.GitComparison.GitPagedDiffTypes
open Arcitisa.Components.Primitive.SplitResizer

module internal GitPagedDiffDisplay =

    /// Where a run of folded pages sits relative to the loaded pages.
    type FoldSide =
        | Earlier
        | Later
        | Between

    type Content =
        | HunkHeader of string
        | AlignedRow of PagedRow * bool
        | Gap of string * PagedRange * PagedRange
        | UnalignedLabel of string * PagedRange * PagedRange
        | UnalignedLines of PagedLine option * PagedLine option
        | Evicted of string * int
        /// Several evicted pages shown as one placeholder as tall as all their rows together. The
        /// page ids are in diff order, and the numbers are the cumulative rows at the end of each
        /// page. Together they are the directory that maps an offset in the placeholder to a page.
        | Folded of FoldSide * string[] * int[]
        | Continue of PagedPending option

    type Row = { Key: string; Content: Content }

    type VirtualRow = {
        Key: string
        Index: int
        Start: int
        Size: int
    }

    type RowProps = {
        Row: Row
        Index: int
        Start: int
        /// The height of the row. A placeholder in a capped scroll range shows only the part the
        /// viewport reaches.
        Height: int
        MeasureElementRef: VirtualMeasureElementRef
        Prefix: string
        /// Gaps whose expansion is still running. Their controls stay disabled.
        ExpandingGaps: string[]
        LoadingNext: bool
        HasMore: bool
        /// The loader reads the remaining pages on its own, so the continue row shows the progress
        /// of that reading and no button.
        Indexing: bool
        /// The host shows the indexing state and the progress itself. The continue row keeps its
        /// button and the failure state only.
        HideIndexingStatus: bool
        /// The last read of the next page failed. The continue button shows the failure and asks again.
        NextFailed: bool
        Progress: PagedProgress option
        /// The page a folded placeholder replays from its button, the one at the top of the viewport.
        FoldedTarget: string
        RequestExpand: (string -> bool -> unit) option
        RequestLineSlice: (PagedDiffSide -> float -> float -> unit) option
        RequestLineBefore: (PagedDiffSide -> float -> float -> unit) option
        PendingLineSlices: PagedLineSliceRequest[]
        /// Evicted pages whose replay is still running. Their controls stay disabled.
        PendingReplays: string[]
        RequestReplay: (string -> unit) option
        RequestNext: (unit -> unit) option
        /// Gaps, line slices and evicted pages whose last request failed. Their controls show
        /// the failure and stay enabled, so the user can ask again.
        FailedGaps: string[]
        FailedLineSlices: PagedLineSliceRequest[]
        FailedReplays: string[]
        /// The reason the failed gap and line controls name in their tooltip.
        FailureNote: string option
        /// The continue row shows this note instead of the label. It keeps the button only when
        /// RequestNext is set.
        EndNote: string option
    }

    type LineProps = {
        Line: PagedLine option
        Side: PagedDiffSide
        Kind: PagedRowKind
        ForceContext: bool
        Prefix: string
        RequestLineSlice: (PagedDiffSide -> float -> float -> unit) option
        RequestLineBefore: (PagedDiffSide -> float -> float -> unit) option
        /// A slice of this line is still loading, so its controls stay disabled.
        SlicePending: bool
        /// The last slice request of this line failed.
        SliceFailed: bool
        /// The reason the failed slice controls name in their tooltip.
        FailureNote: string option
    }

    /// A row the scroll position can follow, with its start and its distance from the top of the
    /// viewport.
    type AnchorCandidate = {
        Key: string
        Start: float
        Offset: float
    }

    type AnchorSnapshot = {
        Keys: string[]
        Candidates: AnchorCandidate[]
    }

    /// Rows the virtualizer renders above and below the viewport.
    [<Literal>]
    let OverscanRows = 8

    let failedAttribute (failed: bool) =
        prop.custom ("data-failed", (if failed then "true" else "false"))

    /// The class of a control whose last request failed.
    let failedClass (failed: bool) = if failed then "swt:text-error" else ""

    /// Height of one display row. Every row of a page has this height, whether it shows lines, a
    /// hunk header or a gap, and the lines do not wrap. A placeholder of evicted pages is as tall
    /// as the rows it stands for, so evicting or replaying a page never moves the rows around it.
    [<Literal>]
    let RowHeightPx = 28

    /// The height of a row the virtualizer lays out. The continue row has no fixed height and
    /// counts as one row until it is measured.
    let rowExtent (content: Content) =
        match content with
        | Evicted(_, rowCount) -> RowHeightPx * rowCount
        | Folded(_, _, ends) -> RowHeightPx * Array.last ends
        | _ -> RowHeightPx

    /// Whether the content is a placeholder, which can be taller than the viewport.
    let isPlaceholder (content: Content) =
        match content with
        | Evicted _
        | Folded _ -> true
        | _ -> false

    /// The tallest scroll height the viewer asks the browser for. A diff taller than this maps the
    /// native scroll range to the logical offsets. Dragging the thumb, the wheel and the keys map
    /// exactly. A click on the scrollbar track or on a scrollbar arrow moves the native position by
    /// its usual step, which is several logical steps. Row counts and heights are 32-bit integers,
    /// so a diff above about 76 million display rows lays out wrong.
    [<Literal>]
    let DefaultMaxScrollHeightPx = 10000000

    /// The tallest scroll height the browser lays out for the pixel ratio, with a quarter of
    /// margin under 2^25 device pixels.
    let layoutLimitPx (devicePixelRatio: float) =
        let ratio = if devicePixelRatio >= 1.0 then devicePixelRatio else 1.0
        int (0.75 * 33554432.0 / ratio)

    /// The start of a page inside a placeholder, in rows from the top of the placeholder.
    let pageStartRows (ends: int[]) (pageIndex: int) =
        if pageIndex = 0 then 0 else ends.[pageIndex - 1]

    /// The height of the scroll content. A diff above the cap takes the cap.
    let physicalHeight (cap: float) (total: float) = min total cap

    /// Logical pixels per pixel of native scroll position. It is 1 for a diff within the cap.
    let scrollRatio (cap: float) (total: float) (view: float) =
        let maxPhysical = physicalHeight cap total - view

        if total <= cap || maxPhysical <= 0.0 then
            1.0
        else
            (total - view) / maxPhysical

    /// The logical offset of a native scroll position.
    let toLogical (cap: float) (total: float) (view: float) (physical: float) =
        if total <= cap then
            physical
        else
            max 0.0 (min (total - view) (physical * scrollRatio cap total view))

    /// The native scroll position of a logical offset.
    let toPhysical (cap: float) (total: float) (view: float) (logical: float) =
        if total <= cap then
            logical
        else
            max 0.0 (min (physicalHeight cap total - view) (logical / scrollRatio cap total view))

    /// The index of the page that holds the offset, found by binary search over the cumulative
    /// rows at the end of each page. An offset past the end belongs to the last page.
    let pageAtOffset (ends: int[]) (offsetPx: float) =
        let row = int (Math.Floor(offsetPx / float RowHeightPx))
        let mutable low = 0
        let mutable high = ends.Length - 1

        while low < high do
            let middle = (low + high) / 2

            if ends.[middle] > row then
                high <- middle
            else
                low <- middle + 1

        low

    /// The pages of a folded placeholder in the viewport, in the order they replay. The page at the
    /// top of the viewport comes first. At the end of the diff the last page comes first. The
    /// row top and the viewport edges are offsets in the scroll content.
    let foldedPagesInView (ends: int[]) (rowTop: float) (viewTop: float) (viewBottom: float) (atEnd: bool) =
        let visibleTop = max viewTop rowTop
        let visibleBottom = min viewBottom (rowTop + float (RowHeightPx * Array.last ends))

        if visibleBottom <= visibleTop then
            [||]
        else
            let first = pageAtOffset ends (visibleTop - rowTop)
            let last = pageAtOffset ends (visibleBottom - 1.0 - rowTop)
            let pages = [| first..last |]
            if atEnd then Array.rev pages else pages

    let private rangeStart (range: PagedRange) =
        if range.Count = 0.0 then range.Start else range.Start + 1.0

    let clamp (lower: int) (upper: int) (value: int) = min upper (max lower value)

    let numberText value = sprintf "%.0f" value

    let hunkHeader previous current =
        $"@@ -{numberText (rangeStart previous)},{numberText previous.Count} +{numberText (rangeStart current)},{numberText current.Count} @@"

    let buildPartRows part =
        match part with
        | PagedPart.HunkRows(hunkId, previous, current, startsHunk, _, rows) -> [|
            if startsHunk then
                {
                    Key = $"hunk:{hunkId}"
                    Content = HunkHeader(hunkHeader previous current)
                }

            for row in rows do
                {
                    Key = row.Id
                    Content = AlignedRow(row, false)
                }
          |]
        | PagedPart.UnalignedRegion(hunkId, previous, current, previousRange, currentRange) ->
            let rowCount = max previous.Length current.Length
            // A large hunk can be split into several unaligned fragments with the same hunk id.
            // The range starts differ per fragment and come from the source, so the keys stay
            // unique and survive rerenders and replays.
            let fragmentKey =
                $"unaligned:{hunkId}:{numberText previousRange.Start}:{numberText currentRange.Start}"

            [|
                {
                    Key = $"{fragmentKey}:label"
                    Content = UnalignedLabel(hunkId, previousRange, currentRange)
                }

                for index in 0 .. rowCount - 1 do
                    {
                        Key = $"{fragmentKey}:{index}"
                        Content =
                            UnalignedLines(
                                (if index < previous.Length then
                                     Some previous.[index]
                                 else
                                     None),
                                (if index < current.Length then
                                     Some current.[index]
                                 else
                                     None)
                            )
                    }
            |]
        | PagedPart.HiddenGap(gapId, previous, current) -> [|
            {
                Key = $"gap:{gapId}"
                Content = Gap(gapId, previous, current)
            }
          |]
        | PagedPart.ExpandedRows(_, rows) ->
            rows
            |> Array.map (fun row -> {
                Key = row.Id
                Content = AlignedRow(row, true)
            })
        | PagedPart.EvictedPage(pageId, rowCount) -> [|
            {
                Key = $"evicted:{pageId}"
                Content = Evicted(pageId, rowCount)
            }
          |]

    let private isEvictedPage part =
        match part with
        | PagedPart.EvictedPage _ -> true
        | _ -> false

    /// The runs of evicted pages that fold into one placeholder, as first part index, last part
    /// index and side. The evicted pages before the first loaded part fold into one run, and so do
    /// the pages after the last one. Each run between loaded parts folds as well. Without a loaded
    /// part nothing folds, since there is no loaded window to fold around.
    let foldRuns (parts: PagedPart[]) : (int * int * FoldSide)[] =
        match
            parts |> Array.tryFindIndex (isEvictedPage >> not), parts |> Array.tryFindIndexBack (isEvictedPage >> not)
        with
        | Some firstLoaded, Some lastLoaded ->
            let runs = ResizeArray<int * int * FoldSide>()
            let mutable index = 0

            while index < parts.Length do
                if isEvictedPage parts.[index] then
                    let first = index

                    while index + 1 < parts.Length && isEvictedPage parts.[index + 1] do
                        index <- index + 1

                    let side =
                        if first < firstLoaded then Earlier
                        elif first > lastLoaded then Later
                        else Between

                    runs.Add(first, index, side)

                index <- index + 1

            runs.ToArray()
        | _ -> [||]

    let foldedRow (parts: PagedPart[]) (first: int) (last: int) (side: FoldSide) : Row =
        // One pass over the pages, since a placeholder can stand for thousands of them.
        let pageIdList = ResizeArray<string>(last - first + 1)
        let endList = ResizeArray<int>(last - first + 1)
        let mutable rows = 0

        for index in first..last do
            match parts.[index] with
            | PagedPart.EvictedPage(pageId, rowCount) ->
                rows <- rows + rowCount
                pageIdList.Add pageId
                endList.Add rows
            | _ -> ()

        let pageIds = pageIdList.ToArray()
        let ends = endList.ToArray()

        {
            Key =
                match side with
                | Earlier -> "folded:earlier"
                | Later -> "folded:later"
                | Between -> $"folded:between:{pageIds.[0]}"
            Content = Folded(side, pageIds, ends)
        }

    let foldSideName side =
        match side with
        | Earlier -> "earlier"
        | Later -> "later"
        | Between -> "between"

    /// Whether the scroll position can follow the row when the rows change. The continue row
    /// changes its start when pages load. A placeholder is followed by the page at the top of the
    /// viewport and its offset, since the placeholder itself changes its start and size when pages
    /// around it load or evict.
    let isAnchorable (content: Content) =
        match content with
        | Continue _ -> false
        | _ -> true

    [<Literal>]
    let private PageAnchorPrefix = "page:"

    /// The key of the anchor that follows a page of a placeholder.
    let pageAnchorKey (pageId: string) = PageAnchorPrefix + pageId

    /// The page id of an anchor key made by pageAnchorKey, None for the key of a row.
    let pageIdOfAnchorKey (key: string) =
        if key.StartsWith PageAnchorPrefix then
            Some(key.Substring PageAnchorPrefix.Length)
        else
            None

    /// The line a row shows on the side, if it shows one.
    let lineOfRow (side: PagedDiffSide) (content: Content) =
        let numberOf (value: PagedLine option) = value |> Option.map _.Number

        match content, side with
        | AlignedRow(aligned, _), PagedDiffSide.Previous -> numberOf aligned.Previous
        | AlignedRow(aligned, _), PagedDiffSide.Current -> numberOf aligned.Current
        | UnalignedLines(previous, _), PagedDiffSide.Previous -> numberOf previous
        | UnalignedLines(_, current), PagedDiffSide.Current -> numberOf current
        | _ -> None

    /// The first row that shows the line or a later line of the side. A hidden gap counts with
    /// the lines it covers.
    let rowIndexOfLine (side: PagedDiffSide) (line: float) (rows: Row[]) =
        let reaches (value: PagedLine option) =
            value |> Option.exists (fun value -> value.Number >= line)

        let onSide (previous: 'T) (current: 'T) =
            match side with
            | PagedDiffSide.Previous -> previous
            | PagedDiffSide.Current -> current

        rows
        |> Array.tryFindIndex (fun row ->
            match row.Content with
            | AlignedRow(aligned, _) -> reaches (onSide aligned.Previous aligned.Current)
            | UnalignedLines(previous, current) -> reaches (onSide previous current)
            | Gap(_, previous, current) ->
                let range = onSide previous current
                range.Count > 0.0 && range.Start + range.Count - 1.0 >= line
            | _ -> false
        )

    let lineNumberText number = numberText (number + 1.0)

    /// The lines the rows show on both sides.
    let private rowLines (rows: Row[]) =
        rows
        |> Array.collect (fun row ->
            match row.Content with
            | AlignedRow(aligned, _) -> [| aligned.Previous; aligned.Current |]
            | UnalignedLines(previous, current) -> [| previous; current |]
            | _ -> [||]
        )
        |> Array.choose id

    /// The width of the line number column as a CSS length, wide enough for the longest line
    /// number of the rows and never narrower than 3.5rem. The cell has 0.75rem of padding on each
    /// side and a 1px border. Every row and both sides use the same width, so the columns line up.
    let private numberColumnWidthOf (lines: PagedLine[]) =
        // The widest number has the most digits, so only that one is formatted. This runs on
        // every page that arrives.
        let digits =
            if lines.Length = 0 then
                0
            else
                (lines
                 |> Array.fold (fun widest line -> max widest line.Number) 0.0
                 |> lineNumberText)
                    .Length

        $"max(3.5rem, calc({digits}ch + 1.5rem + 1px))"

    let numberColumnWidth (rows: Row[]) = numberColumnWidthOf (rowLines rows)

    /// The lines the rows show on one side.
    let private sideLines (side: PagedDiffSide) (rows: Row[]) =
        let pick previous current =
            if side = PagedDiffSide.Previous then previous else current

        rows
        |> Array.choose (fun row ->
            match row.Content with
            | AlignedRow(aligned, _) -> pick aligned.Previous aligned.Current
            | UnalignedLines(previous, current) -> pick previous current
            | _ -> None
        )

    /// The width of the text area of one side as a CSS length, wide enough for the longest line of
    /// that side. The rows use a monospace font, so the width counts characters. Controls beside a
    /// line need room of their own.
    let textWidth (side: PagedDiffSide) (rows: Row[]) =
        let lines = sideLines side rows

        let characters =
            lines |> Array.fold (fun widest line -> max widest line.Text.Length) 0

        let hasControls =
            lines
            |> Array.exists (fun line ->
                line.OffsetUtf16 > 0.0
                || line.TotalUtf16
                   |> Option.forall (fun total -> total > line.OffsetUtf16 + float line.Text.Length)
            )

        let controls = if hasControls then " + 14rem" else ""
        $"calc(1.5rem + {characters + 2}ch{controls})"

    /// Writes a custom property to the inline style of the element. Fable.Browser.Dom has no
    /// binding for the style declaration.
    [<Emit("$0.style.setProperty($1, $2)")>]
    let setStyleProperty (_element: HTMLElement) (_name: string) (_value: string) : unit = jsNative

    let endingText ending =
        match ending with
        | PagedLineEnding.NoEnding -> "No ending"
        | PagedLineEnding.LF -> "LF"
        | PagedLineEnding.CRLF -> "CRLF"
        | PagedLineEnding.CR -> "CR"

    let private changedSide side kind =
        match kind, side with
        | PagedRowKind.Added, PagedDiffSide.Current
        | PagedRowKind.Replaced, _
        | PagedRowKind.EndingChanged, PagedDiffSide.Previous -> true
        | PagedRowKind.Removed, PagedDiffSide.Previous -> true
        | PagedRowKind.EndingChanged, PagedDiffSide.Current -> true
        | _ -> false

    /// The class of a line or its number, picked from the theme of the side when the row changes it.
    let private changedOrContextClass
        side
        kind
        forceContext
        (pick: GitTextComparisonRendering.Rendering.ComparisonSideStyle -> string)
        =
        if forceContext || kind = PagedRowKind.Context || not (changedSide side kind) then
            "swt:text-base-content/45 swt:bg-base-100"
        else
            let theme = GitTextComparisonRendering.Rendering.DiffTheme

            pick (
                if side = PagedDiffSide.Previous then
                    theme.LeftChanged
                else
                    theme.RightChanged
            )

    let rowKindClass side kind forceContext =
        changedOrContextClass side kind forceContext _.ChangedLineClass

    let lineNumberClass side kind forceContext =
        changedOrContextClass side kind forceContext _.ChangedLineNumberClass

    let changedSegmentClass side =
        let theme = GitTextComparisonRendering.Rendering.DiffTheme

        if side = PagedDiffSide.Previous then
            theme.LeftChanged.ChangedSegmentClass
        else
            theme.RightChanged.ChangedSegmentClass

    /// An evicted page with display rows counts as content, since its rows come back when it is
    /// replayed. A page of hidden gaps only counts as well.
    let hasChanges (parts: PagedPart[]) =
        parts
        |> Array.exists (
            function
            | PagedPart.HunkRows _
            | PagedPart.UnalignedRegion _ -> true
            | PagedPart.EvictedPage(_, rowCount) -> rowCount > 0
            | _ -> false
        )

    let changeBadge (parts: PagedPart[]) noChanges =
        if noChanges then
            "No changes"
        else
            let kinds =
                parts
                |> Array.collect (
                    function
                    | PagedPart.HunkRows(_, _, _, _, _, rows)
                    | PagedPart.ExpandedRows(_, rows) -> rows |> Array.map (fun row -> row.Kind)
                    | _ -> [||]
                )

            let hasAdded = kinds |> Array.contains PagedRowKind.Added
            let hasRemoved = kinds |> Array.contains PagedRowKind.Removed

            match hasAdded, hasRemoved with
            | true, false -> "Added"
            | false, true -> "Deleted"
            | _ -> "Changed"

    let statusName status =
        match status with
        | PagedDiffStatus.Opening -> "opening"
        | PagedDiffStatus.Scanning -> "scanning"
        | PagedDiffStatus.EncodingChoice _ -> "encoding-choice"
        | PagedDiffStatus.Ready -> "ready"
        | PagedDiffStatus.LoadingNext -> "loading-next"
        | PagedDiffStatus.Reopening -> "reopening"
        | PagedDiffStatus.Blocked _ -> "blocked"
        | PagedDiffStatus.SourceChanged -> "source-changed"
        | PagedDiffStatus.WorkerFailed _ -> "worker-failed"
        | PagedDiffStatus.Failed _ -> "failed"

    let sideName side =
        match side with
        | PagedDiffSide.Previous -> "previous"
        | PagedDiffSide.Current -> "current"

    let kindName kind =
        match kind with
        | PagedRowKind.Context -> "context"
        | PagedRowKind.Added -> "added"
        | PagedRowKind.Removed -> "removed"
        | PagedRowKind.Replaced -> "replaced"
        | PagedRowKind.EndingChanged -> "ending-changed"

    let endingName ending =
        match ending with
        | PagedLineEnding.NoEnding -> "none"
        | PagedLineEnding.LF -> "lf"
        | PagedLineEnding.CRLF -> "crlf"
        | PagedLineEnding.CR -> "cr"

    let snippetEndName snippetEnd =
        match snippetEnd with
        | PagedSnippetEnd.Truncated -> "truncated"
        | PagedSnippetEnd.MoreTextPending -> "more-text-pending"
        | PagedSnippetEnd.LineEnd -> "line-end"
        | PagedSnippetEnd.EndOfFile -> "end-of-file"

[<Erase; Mangle(false)>]
type GitPagedDiffViewer =

    [<ReactComponent>]
    static member private LineContent(props: GitPagedDiffDisplay.LineProps) =
        let kind =
            if props.ForceContext then
                PagedRowKind.Context
            else
                props.Kind

        match props.Line with
        | None ->
            Html.div [
                prop.custom ("data-side", GitPagedDiffDisplay.sideName props.Side)
                prop.custom ("data-kind", GitPagedDiffDisplay.kindName kind)
                prop.className
                    "swt:grid swt:grid-cols-[var(--paged-number-column,3.5rem)_minmax(0,1fr)] swt:min-w-0 swt:overflow-hidden"
            ]
        | Some line ->
            let textLength = line.Text.Length
            let highlights = line.Highlights |> Array.sortBy (fun highlight -> highlight.Start)

            let segments = ResizeArray<ReactElement>()
            let mutable cursor = 0

            for index, highlight in highlights |> Array.indexed do
                let startIndex = GitPagedDiffDisplay.clamp cursor textLength highlight.Start

                let endIndex =
                    GitPagedDiffDisplay.clamp startIndex textLength (highlight.Start + highlight.Length)

                if startIndex > cursor then
                    segments.Add(
                        Html.span [
                            prop.key $"text-{index}"
                            prop.text (line.Text.Substring(cursor, startIndex - cursor))
                        ]
                    )

                if endIndex > startIndex then
                    segments.Add(
                        Html.span [
                            prop.key $"highlight-{index}"
                            prop.custom ("data-highlight", (if highlight.Changed then "changed" else "unchanged"))
                            if highlight.Changed then
                                prop.className (GitPagedDiffDisplay.changedSegmentClass props.Side)
                            prop.text (line.Text.Substring(startIndex, endIndex - startIndex))
                        ]
                    )

                cursor <- endIndex

            if cursor < textLength then
                segments.Add(Html.span [ prop.key "tail"; prop.text (line.Text.Substring(cursor)) ])

            let hasMore =
                line.TotalUtf16
                |> Option.map (fun total -> total > line.OffsetUtf16 + float textLength)
                |> Option.defaultValue true

            let lineStyle =
                GitPagedDiffDisplay.rowKindClass props.Side props.Kind props.ForceContext

            let lineNumber = GitPagedDiffDisplay.numberText line.Number
            let side = GitPagedDiffDisplay.sideName props.Side

            Html.div [
                prop.testId $"{props.Prefix}-line-{side}-{lineNumber}"
                prop.custom ("data-side", side)
                prop.custom ("data-kind", GitPagedDiffDisplay.kindName kind)
                prop.className
                    "swt:grid swt:grid-cols-[var(--paged-number-column,3.5rem)_minmax(0,1fr)] swt:min-w-0 swt:overflow-hidden"
                prop.children [
                    Html.div [
                        prop.className [
                            "swt:px-3 swt:py-1 swt:text-right swt:text-xs swt:leading-5 swt:select-none swt:border-r swt:border-base-content/10"
                            GitPagedDiffDisplay.lineNumberClass props.Side props.Kind props.ForceContext
                        ]
                        prop.text (GitPagedDiffDisplay.lineNumberText line.Number)
                    ]
                    // The text stays on one line, since every row has the same height. The text area
                    // is as wide as the longest line of its side, and the controls sit beside the
                    // text. The box clips the area, and the scroll bar of the side shifts it. A clip
                    // box is no scroll container, so focusing a control cannot scroll it.
                    Html.div [
                        prop.custom ("data-text-box", side)
                        prop.className "swt:min-w-0 swt:overflow-clip"
                        prop.children [
                            Html.div [
                                prop.testId $"{props.Prefix}-line-text-{side}-{lineNumber}"
                                prop.custom ("data-offset-utf16", GitPagedDiffDisplay.numberText line.OffsetUtf16)
                                prop.className [
                                    "swt:flex swt:min-w-0 swt:items-start swt:gap-2 swt:px-3 swt:py-1 swt:font-mono swt:text-xs swt:leading-5"
                                    lineStyle
                                ]
                                prop.style [
                                    style.custom ("width", $"max(100%%, var(--paged-text-{side}))")
                                    style.custom (
                                        "transform",
                                        $"translateX(calc(-1 * var(--paged-scroll-{side}, 0px)))"
                                    )
                                ]
                                prop.children [
                                    // The page shows a slice around the change. This control loads the
                                    // text in front of the slice, so the start of a long line can be read.
                                    if line.OffsetUtf16 > 0.0 then
                                        Html.button [
                                            prop.testId $"{props.Prefix}-line-before-{side}-{lineNumber}"
                                            GitPagedDiffDisplay.failedAttribute props.SliceFailed
                                            prop.className [
                                                "swt:btn swt:btn-ghost swt:btn-xs swt:shrink-0"
                                                GitPagedDiffDisplay.failedClass props.SliceFailed
                                            ]
                                            if props.SliceFailed && props.FailureNote.IsSome then
                                                prop.title props.FailureNote.Value
                                            prop.disabled (props.SlicePending || props.RequestLineBefore.IsNone)
                                            prop.onClick (fun _ ->
                                                props.RequestLineBefore
                                                |> Option.iter (fun callback ->
                                                    callback props.Side line.Number line.OffsetUtf16
                                                )
                                            )
                                            prop.text "Load earlier"
                                        ]

                                    Html.span [
                                        prop.className "swt:min-w-0 swt:flex-1"
                                        prop.style [ style.whitespace.pre ]
                                        prop.children [
                                            if line.OffsetUtf16 > 0.0 then
                                                Html.span [
                                                    prop.className "swt:mr-1 swt:text-base-content/45"
                                                    prop.text "…"
                                                ]

                                            if segments.Count = 0 then
                                                Html.span [ prop.text " " ]
                                            else
                                                React.Fragment(List.ofSeq segments)
                                        ]
                                    ]

                                    if props.Kind = PagedRowKind.EndingChanged && not props.ForceContext then
                                        Html.span [
                                            prop.testId $"{props.Prefix}-ending-{side}-{lineNumber}"
                                            prop.custom ("data-ending", GitPagedDiffDisplay.endingName line.Ending)
                                            prop.className "swt:badge swt:badge-outline swt:badge-xs swt:shrink-0"
                                            prop.text (GitPagedDiffDisplay.endingText line.Ending)
                                        ]

                                    if hasMore then
                                        Html.button [
                                            prop.testId $"{props.Prefix}-line-more-{side}-{lineNumber}"
                                            GitPagedDiffDisplay.failedAttribute props.SliceFailed
                                            prop.className [
                                                "swt:btn swt:btn-ghost swt:btn-xs swt:shrink-0"
                                                GitPagedDiffDisplay.failedClass props.SliceFailed
                                            ]
                                            if props.SliceFailed && props.FailureNote.IsSome then
                                                prop.title props.FailureNote.Value
                                            prop.disabled (props.SlicePending || props.RequestLineSlice.IsNone)
                                            prop.onClick (fun _ ->
                                                props.RequestLineSlice
                                                |> Option.iter (fun callback ->
                                                    callback
                                                        props.Side
                                                        line.Number
                                                        (line.OffsetUtf16 + float textLength)
                                                )
                                            )
                                            prop.text "Load more"
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

    [<ReactComponent>]
    static member private PendingPanel(prefix: string, pending: PagedPending) =
        let mismatchFor side =
            pending.Mismatch
            |> Option.map (fun (previous, current) -> if side = PagedDiffSide.Previous then previous else current)

        let renderSide (sideTestId: string) (label: string) (side: PagedPendingSide) (mismatchOffset: float option) =
            let mismatchOffset = mismatchOffset |> Option.map int

            Html.div [
                prop.testId $"{prefix}-pending-{sideTestId}"
                prop.className "swt:min-w-0 swt:rounded-box swt:border swt:border-base-content/10 swt:p-3"
                prop.children [
                    Html.div [
                        prop.className
                            "swt:mb-2 swt:text-[11px] swt:font-semibold swt:uppercase swt:tracking-wide swt:text-base-content/60"
                        prop.text label
                    ]
                    match side with
                    | PagedPendingSide.NoActiveLine ->
                        Html.span [
                            prop.className "swt:text-xs swt:text-base-content/55"
                            prop.text "No active line"
                        ]
                    | PagedPendingSide.Exhausted lineCount ->
                        Html.span [
                            prop.className "swt:text-xs swt:text-base-content/55"
                            prop.text $"No further line, {GitPagedDiffDisplay.numberText lineCount} lines read"
                        ]
                    | PagedPendingSide.Snippet(line, snippetOffset, text, endState) ->
                        // The mismatch offset counts from the start of the line, and the snippet
                        // can start further into the line.
                        let marker =
                            mismatchOffset
                            |> Option.map (fun offset ->
                                GitPagedDiffDisplay.clamp 0 text.Length (offset - int snippetOffset)
                            )

                        let startsMidLine = snippetOffset > 0.0

                        let textWithMarker = [
                            if startsMidLine then
                                Html.span [
                                    prop.testId $"{prefix}-pending-leading-{sideTestId}"
                                    prop.className "swt:text-base-content/45"
                                    prop.text "…"
                                ]
                            match marker with
                            | Some offset ->
                                Html.span [ prop.text (text.Substring(0, offset)) ]

                                Html.span [
                                    prop.testId $"{prefix}-pending-mismatch-{sideTestId}"
                                    prop.custom ("data-offset", offset)
                                    prop.className "swt:text-error swt:font-bold"
                                    prop.text "│"
                                ]

                                Html.span [ prop.text (text.Substring(offset)) ]
                            | None -> Html.span [ prop.text text ]
                        ]

                        Html.div [
                            prop.className "swt:flex swt:min-w-0 swt:flex-col swt:gap-1"
                            prop.children [
                                Html.span [
                                    prop.className "swt:text-xs swt:text-base-content/60"
                                    prop.text $"Line {GitPagedDiffDisplay.lineNumberText line}"
                                ]
                                Html.code [
                                    prop.testId $"{prefix}-pending-snippet-{sideTestId}"
                                    prop.custom ("data-starts-mid-line", (if startsMidLine then "true" else "false"))
                                    prop.className
                                        "swt:min-w-0 swt:whitespace-pre-wrap swt:break-all swt:font-mono swt:text-xs"
                                    prop.children textWithMarker
                                ]
                                Html.span [
                                    prop.testId $"{prefix}-pending-end-{sideTestId}"
                                    prop.custom ("data-end", GitPagedDiffDisplay.snippetEndName endState)
                                    prop.className "swt:text-[11px] swt:text-base-content/55"
                                    prop.text (
                                        match endState with
                                        | PagedSnippetEnd.MoreTextPending -> "Still reading"
                                        | PagedSnippetEnd.LineEnd -> "Line end"
                                        | PagedSnippetEnd.EndOfFile -> "End of file"
                                        | PagedSnippetEnd.Truncated -> "…"
                                    )
                                ]
                            ]
                        ]
                ]
            ]

        Html.div [
            prop.testId $"{prefix}-pending"
            prop.className
                "swt:col-span-2 swt:grid swt:grid-cols-2 swt:gap-3 swt:border-t swt:border-base-content/10 swt:p-3"
            prop.children [
                Html.div [
                    prop.className "swt:col-span-2 swt:text-xs swt:text-base-content/60"
                    prop.text "Alignment is pending"
                ]
                renderSide "previous" "Previous" pending.Previous (mismatchFor PagedDiffSide.Previous)
                renderSide "current" "Current" pending.Current (mismatchFor PagedDiffSide.Current)
            ]
        ]

    [<ReactComponent>]
    static member private RowView(props: GitPagedDiffDisplay.RowProps) =
        let fullWidth (className: string) (children: ReactElement list) : ReactElement =
            Html.div [
                prop.className $"swt:col-span-2 swt:flex swt:items-center swt:gap-3 swt:px-4 {className}"
                prop.children children
            ]

        let controlRow (className: string) (innerClassName: string) (children: ReactElement list) : ReactElement =
            Html.div [
                prop.className $"swt:col-span-2 {className} {innerClassName}"
                prop.children children
            ]

        // A header row takes exactly one display row.
        let headerRow (className: string) (children: ReactElement list) : ReactElement =
            fullWidth $"swt:h-7 {className}" children

        let cell (side: PagedDiffSide) (kind: PagedRowKind) (forceContext: bool) (line: PagedLine option) =
            GitPagedDiffViewer.LineContent {
                Line = line
                Side = side
                Kind = kind
                ForceContext = forceContext
                Prefix = props.Prefix
                RequestLineSlice = props.RequestLineSlice
                RequestLineBefore = props.RequestLineBefore
                SlicePending =
                    line
                    |> Option.exists (fun shown ->
                        props.PendingLineSlices
                        |> Array.exists (fun request -> request.Side = side && request.Line = shown.Number)
                    )
                SliceFailed =
                    line
                    |> Option.exists (fun shown ->
                        props.FailedLineSlices
                        |> Array.exists (fun request -> request.Side = side && request.Line = shown.Number)
                    )
                FailureNote = props.FailureNote
            }

        /// The placeholder of unloaded rows with its button, which replays the page it names.
        let replayPlaceholder
            (buttonTestId: string)
            (nextPage: string)
            (pageIds: string[])
            (height: int)
            (label: string)
            (attributes: IReactProperty list)
            =
            let replaying =
                props.PendingReplays
                |> Array.exists (fun pageId -> Array.contains pageId pageIds)

            let failed = props.FailedReplays |> Array.contains nextPage

            // The placeholder is as tall as the rows it stands for. Its label sticks to the top of
            // the viewport, so the loading state and the retry control sit where the user looks.
            Html.div [
                prop.className
                    "swt:col-span-2 swt:overflow-clip swt:bg-base-200/60 swt:text-xs swt:text-base-content/65 swt:font-sans"
                yield! attributes
                prop.custom ("data-loading", (if replaying then "true" else "false"))
                prop.style [ style.height height ]
                prop.children [
                    Html.div [
                        prop.className
                            "swt:sticky swt:top-0 swt:flex swt:h-7 swt:items-center swt:justify-center swt:gap-3 swt:px-4"
                        prop.children [
                            if replaying then
                                Html.span [
                                    prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                                ]
                            Html.span [ prop.text label ]
                            Html.button [
                                prop.testId buttonTestId
                                GitPagedDiffDisplay.failedAttribute failed
                                prop.className [
                                    "swt:btn swt:btn-ghost swt:btn-xs"
                                    GitPagedDiffDisplay.failedClass failed
                                ]
                                prop.disabled (replaying || props.RequestReplay.IsNone)
                                prop.onClick (fun _ ->
                                    props.RequestReplay |> Option.iter (fun callback -> callback nextPage)
                                )
                                prop.text "Reload rows"
                            ]
                        ]
                    ]
                ]
            ]

        let content =
            match props.Row.Content with
            | GitPagedDiffDisplay.HunkHeader header ->
                headerRow
                    "swt:border-y swt:border-base-content/10 swt:bg-base-200/80 swt:font-mono swt:text-xs swt:text-base-content/70"
                    [ Html.span [ prop.text header ] ]
            | GitPagedDiffDisplay.AlignedRow(row, forceContext) ->
                React.Fragment [
                    cell PagedDiffSide.Previous row.Kind forceContext row.Previous
                    cell PagedDiffSide.Current row.Kind forceContext row.Current
                ]
            | GitPagedDiffDisplay.Gap(gapId, previous, _) ->
                let isExpanding = props.ExpandingGaps |> Array.contains gapId
                let failed = props.FailedGaps |> Array.contains gapId

                // The control at the top of the gap sits below the previous hunk and reveals the
                // first hidden lines. The control at the bottom sits above the next hunk and
                // reveals the last hidden lines.
                let expandButton (fromStart: bool) =
                    let position, icon, label =
                        if fromStart then
                            "start", "swt:fluent--arrow-down-24-regular", "Show the first hidden lines"
                        else
                            "end", "swt:fluent--arrow-up-24-regular", "Show the last hidden lines"

                    Html.button [
                        prop.testId $"{props.Prefix}-gap-expand-{position}-{gapId}"
                        prop.custom ("data-from-start", (if fromStart then "true" else "false"))
                        GitPagedDiffDisplay.failedAttribute failed
                        prop.className [
                            "swt:btn swt:btn-ghost swt:btn-xs swt:gap-1"
                            GitPagedDiffDisplay.failedClass failed
                        ]
                        prop.title (
                            if failed then
                                props.FailureNote |> Option.defaultValue label
                            else
                                label
                        )
                        prop.ariaLabel label
                        prop.disabled (isExpanding || props.RequestExpand.IsNone)
                        prop.onClick (fun _ ->
                            props.RequestExpand |> Option.iter (fun callback -> callback gapId fromStart)
                        )
                        prop.children [
                            Html.span [ prop.className [ "swt:iconify"; icon; "swt:size-4" ] ]
                        ]
                    ]

                controlRow
                    "swt:h-7 swt:bg-base-200/45 swt:font-sans swt:text-xs swt:text-base-content/65"
                    "swt:flex swt:h-full swt:items-center swt:justify-center swt:gap-3 swt:px-4"
                    [
                        expandButton true
                        Html.span [
                            prop.className "swt:whitespace-nowrap"
                            prop.text $"{GitPagedDiffDisplay.numberText previous.Count} hidden lines"
                        ]
                        expandButton false
                    ]
            | GitPagedDiffDisplay.UnalignedLabel(_, previous, current) ->
                headerRow
                    "swt:border-y swt:border-base-content/10 swt:bg-warning/10 swt:font-mono swt:text-xs swt:text-base-content/70"
                    [
                        Html.span [
                            prop.text (GitPagedDiffDisplay.hunkHeader previous current)
                        ]
                    ]
            | GitPagedDiffDisplay.UnalignedLines(previous, current) ->
                React.Fragment [
                    cell PagedDiffSide.Previous PagedRowKind.Removed false previous
                    cell PagedDiffSide.Current PagedRowKind.Added false current
                ]
            | GitPagedDiffDisplay.Evicted(pageId, rowCount) ->
                replayPlaceholder
                    $"{props.Prefix}-evicted-{pageId}"
                    pageId
                    [| pageId |]
                    props.Height
                    $"{rowCount} rows unloaded"
                    [ prop.custom ("data-row-count", rowCount) ]
            | GitPagedDiffDisplay.Folded(side, pageIds, ends) ->
                let rowCount = Array.last ends
                // A failed page keeps the button, so the retry asks for the page that failed.
                let nextPage =
                    pageIds
                    |> Array.tryFind (fun pageId -> props.FailedReplays |> Array.contains pageId)
                    |> Option.defaultValue props.FoldedTarget

                let label =
                    match side with
                    | GitPagedDiffDisplay.Earlier -> $"{rowCount} earlier rows"
                    | GitPagedDiffDisplay.Later -> $"{rowCount} later rows"
                    | GitPagedDiffDisplay.Between -> $"{rowCount} rows unloaded"

                replayPlaceholder $"{props.Prefix}-folded-replay-{nextPage}" nextPage pageIds props.Height label [
                    prop.custom ("data-folded-side", GitPagedDiffDisplay.foldSideName side)
                    prop.custom ("data-page-count", pageIds.Length)
                    prop.custom ("data-row-count", rowCount)
                    prop.custom ("data-next-page", nextPage)
                ]
            | GitPagedDiffDisplay.Continue pending ->
                let busy = props.LoadingNext

                controlRow
                    "swt:font-sans swt:border-t swt:border-base-content/10 swt:bg-base-200/35"
                    "swt:flex swt:flex-col swt:items-stretch swt:gap-2 swt:px-4 swt:py-3"
                    [
                        match pending with
                        | Some value -> GitPagedDiffViewer.PendingPanel(props.Prefix, value)
                        | None -> ()

                        if props.HasMore then
                            Html.div [
                                prop.testId $"{props.Prefix}-continue"
                                prop.className "swt:flex swt:flex-wrap swt:items-center swt:justify-between swt:gap-3"
                                prop.children [
                                    match props.Progress with
                                    | Some value when not props.HideIndexingStatus ->
                                        Html.span [
                                            prop.testId $"{props.Prefix}-continue-progress"
                                            prop.className "swt:text-xs swt:text-base-content/60"
                                            prop.text
                                                $"{progressPercentage value}%% ({formatBytes value.ValidatedBytes} / {formatBytes value.TotalBytes})"
                                        ]
                                    | _ -> ()
                                    if props.Indexing then
                                        if not props.HideIndexingStatus then
                                            Html.span [
                                                prop.testId $"{props.Prefix}-indexing"
                                                prop.className
                                                    "swt:flex swt:items-center swt:gap-2 swt:text-xs swt:text-base-content/65"
                                                prop.children [
                                                    Html.span [
                                                        prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                                                    ]
                                                    Html.span [
                                                        prop.text "Indexing the diff, the scrollbar is not exact yet"
                                                    ]
                                                ]
                                            ]
                                    else
                                        match props.EndNote with
                                        | Some note ->
                                            Html.span [
                                                prop.testId $"{props.Prefix}-continue-note"
                                                prop.className "swt:text-xs swt:text-base-content/65"
                                                prop.text note
                                            ]
                                        // The host that shows the status itself leaves the label out, and the
                                        // row keeps the button.
                                        | None when not props.HideIndexingStatus ->
                                            Html.span [
                                                prop.testId $"{props.Prefix}-continue-label"
                                                prop.className "swt:text-xs swt:text-base-content/65"
                                                prop.text "More diff content is available"
                                            ]
                                        | None -> ()

                                        // A note that ends the diff leaves the button to the host.
                                        if props.EndNote.IsNone || props.RequestNext.IsSome then
                                            Html.button [
                                                prop.testId $"{props.Prefix}-continue-button"
                                                GitPagedDiffDisplay.failedAttribute props.NextFailed
                                                prop.className [
                                                    "swt:btn swt:btn-primary swt:btn-xs"
                                                    GitPagedDiffDisplay.failedClass props.NextFailed
                                                ]
                                                prop.disabled (busy || props.RequestNext.IsNone)
                                                prop.onClick (fun _ ->
                                                    props.RequestNext |> Option.iter (fun callback -> callback ())
                                                )
                                                prop.text (if busy then "Loading" else "Continue loading diff")
                                            ]
                                ]
                            ]
                    ]

        Html.div [
            prop.testId $"{props.Prefix}-row-{props.Row.Key}"
            prop.custom ("data-index", props.Index)
            prop.custom ("data-paged-diff-key", props.Row.Key)
            // Only the continue row has no fixed height, so only it is measured.
            prop.ref (fun element ->
                match props.Row.Content with
                | GitPagedDiffDisplay.Continue _ -> props.MeasureElementRef(Option.ofObj element)
                | _ -> ()
            )
            prop.className "swt:absolute swt:left-0 swt:grid swt:w-full swt:divide-x swt:divide-base-content/10"
            prop.style [
                style.top 0
                style.left 0
                style.custom ("transform", $"translateY({props.Start}px)")
                style.custom ("gridTemplateColumns", "minmax(0, var(--paged-split, 50%)) minmax(0, 1fr)")
                match props.Row.Content with
                | GitPagedDiffDisplay.Continue _ -> ()
                | _ -> style.height props.Height
            ]
            prop.children [ content ]
        ]

    [<ReactComponent>]
    static member private Grid
        (
            rows: GitPagedDiffDisplay.Row[],
            rowParts: int[],
            evictedIds: Lazy<JS.Set<string>>,
            previousText: string,
            currentText: string,
            numberColumn: string,
            previousTitle: string,
            currentTitle: string,
            prefix: string,
            nextKey: string option,
            hasMore: bool,
            indexing: bool,
            hideIndexingStatus: bool,
            nextFailed: bool,
            maxScrollHeight: int,
            progress: PagedProgress option,
            status: PagedDiffStatus,
            expandingGaps: string[],
            requestExpand: (string -> bool -> unit) option,
            requestLineSlice: (PagedDiffSide -> float -> float -> unit) option,
            requestLineBefore: (PagedDiffSide -> float -> float -> unit) option,
            pendingLineSlices: PagedLineSliceRequest[],
            pendingReplays: string[],
            requestReplay: (string -> int -> int -> unit) option,
            requestNext: (unit -> unit) option,
            failedGaps: string[],
            failedLineSlices: PagedLineSliceRequest[],
            failedReplays: string[],
            scrollTarget: PagedScrollTarget option,
            failureNote: string option,
            endNote: string option
        ) =
        let rowHeight = GitPagedDiffDisplay.RowHeightPx
        let gridRootRef: IRefValue<HTMLElement option> = React.useElementRef ()
        let overlayRef: IRefValue<HTMLElement option> = React.useElementRef ()
        // The share of the width the previous pane takes. The custom property --paged-split on the
        // grid root carries it to the layout, so dragging the separator re-renders no rows.
        let splitRatio = React.useRef 0.5
        let previousBarRef: IRefValue<HTMLElement option> = React.useElementRef ()
        let currentBarRef: IRefValue<HTMLElement option> = React.useElementRef ()
        // The range and the position of the shared bar at its last scroll event.
        let bothMax = React.useRef 0.0
        let bothLeft = React.useRef 0.0
        let bodyScrollRef: IRefValue<HTMLElement option> = React.useElementRef ()
        let bodyContentRef: IRefValue<HTMLElement option> = React.useElementRef ()
        let contentMeasureRef, contentRect = React.useMeasure<Element> ()
        let previousLayout = React.useRef<GitPagedDiffDisplay.AnchorSnapshot option> None
        let appliedScrollToken = React.useRef<int option> None
        // The user reached the end while the diff was still being indexed. The view then stays at
        // the end as the diff grows, until the user scrolls away or the indexing ends.
        let followEnd = React.useRef false
        // Whether the user holds the scrollbar. The state renders the viewer again when the user
        // lets go.
        let gesture, setGesture = React.useState false
        // The total height while the scrollbar is held. The thumb keeps its meaning, so the content
        // height and the mapping of the scroll range keep this total, while the rows change as
        // pages replay and arrive. The anchor still keeps the rows in view when the rows above change
        // height during the hold, and the thumb drag overwrites that write on its next move.
        let pinnedTotal = React.useRef<float option> None
        // The rows in the viewport while the diff reopens. The scroll target of the reopen names
        // the first line of a page, and the view lands on the line the user was reading.
        let readingRows = React.useRef<GitPagedDiffDisplay.Content[]> [||]
        // Changed after the scroll position was restored, so the rows render for the new position
        // before the browser paints. The updater form keeps two changes from the same version
        // from collapsing into one.
        let _, setLayoutVersion = React.useStateWithUpdater 0

        let renderLayout () =
            setLayoutVersion (fun version -> version + 1)

        // The native scroll position the rows were drawn for in the last render.
        let renderedPhysical = React.useRef -1.0
        // The element inside the scroll element that holds the focus, such as a button of a row.
        let focusedInside = React.useRef<HTMLElement option> None

        // The scroll range above the cap maps the native scroll position to a logical offset. The
        // rows lay out at the logical offsets, and the refs let the scroll handlers read the
        // latest values.
        let cap =
            float (min maxScrollHeight (GitPagedDiffDisplay.layoutLimitPx Browser.Dom.window.devicePixelRatio))

        let capRef = React.useRef cap
        capRef.current <- cap
        let totalRef = React.useRef 0.0
        // The logical scroll offset is the source of truth. The native position follows it.
        let logicalRef = React.useRef 0.0
        // The native scroll position of the last write or read, which logicalRef belongs to.
        let physicalRef = React.useRef -1.0
        // Whether the scroll range was capped when the scroll position was placed last.
        let wasCapped = React.useRef false
        // Brings the virtualizer to the logical offset, for the handlers that outlive a render.
        let syncRef = React.useRef ignore
        // Takes the anchor snapshot of the current position, for the same handlers.
        let anchorRef = React.useRef ignore

        let isCapped () = totalRef.current > capRef.current

        // The logical scroll offset. A native position that does not match the last one maps to a
        // new logical offset. A position that the viewer wrote keeps its exact logical offset,
        // since a native pixel stands for several logical pixels.
        let logicalTop () =
            match bodyScrollRef.current with
            | Some element when isCapped () ->
                let physical = element.scrollTop

                if abs (physical - physicalRef.current) >= 1.5 then
                    logicalRef.current <-
                        GitPagedDiffDisplay.toLogical capRef.current totalRef.current element.clientHeight physical

                    physicalRef.current <- physical

                logicalRef.current
            | Some element when wasCapped.current -> logicalRef.current
            | Some element ->
                logicalRef.current <- element.scrollTop
                physicalRef.current <- element.scrollTop
                element.scrollTop
            | None -> 0.0

        let setLogicalTop (logical: float) =
            match bodyScrollRef.current with
            | Some element ->
                let view = element.clientHeight
                let logical = max 0.0 (min (totalRef.current - view) logical)

                if isCapped () then
                    element.scrollTop <- GitPagedDiffDisplay.toPhysical capRef.current totalRef.current view logical
                    physicalRef.current <- element.scrollTop
                    logicalRef.current <- logical
                else
                    element.scrollTop <- logical
            | None -> ()

        // While the diff reopens, the rows stay on screen and take no requests.
        let interactive = status <> PagedDiffStatus.Reopening
        let requestExpand = requestExpand |> Option.filter (fun _ -> interactive)
        let requestLineSlice = requestLineSlice |> Option.filter (fun _ -> interactive)
        let requestLineBefore = requestLineBefore |> Option.filter (fun _ -> interactive)
        let requestReplay = requestReplay |> Option.filter (fun _ -> interactive)
        let requestNext = requestNext |> Option.filter (fun _ -> interactive)

        React.useEffect (
            (fun () ->
                contentMeasureRef (bodyContentRef.current |> Option.map unbox<Element>)
                FsReact.createDisposable (fun () -> contentMeasureRef None)
            ),
            [| box contentMeasureRef |]
        )

        React.useEffect (
            (fun () ->
                match bodyScrollRef.current with
                | Some bodyScroll ->
                    // The scrollbar belongs to the scroll element itself, so a press on it is the only
                    // press that targets the element and not a row.
                    let startGesture (event: Event) =
                        if obj.ReferenceEquals(event.target, bodyScroll) then
                            pinnedTotal.current <- Some totalRef.current
                            setGesture true

                    let endGesture (_: Event) =
                        pinnedTotal.current <- None
                        setGesture false

                    // A release outside the window can go unnoticed, so a move without a pressed
                    // button also ends the gesture.
                    let moveGesture (event: Event) =
                        if (unbox<MouseEvent> event).buttons = 0 then
                            endGesture event

                    // Above the cap a wheel step or a key press moves by its usual amount of logical
                    // pixels. The native step would move by the ratio of the two ranges. The rows
                    // render for the new offset even when the step is too small to move the native
                    // position. Such a step fires no scroll event, so the anchor snapshot is taken
                    // here, and a later change of the rows keeps the view where the step put it.
                    let moveLogical (delta: float) =
                        setLogicalTop (logicalTop () + delta)
                        syncRef.current ()
                        anchorRef.current ()

                    // A horizontal step moves the text of the pane under the pointer.
                    let scrollPane (wheel: WheelEvent) (amount: float) =
                        let bounds = bodyScroll.getBoundingClientRect ()
                        let width = float bodyScroll.clientWidth
                        let onPrevious = wheel.clientX < bounds.left + width * splitRatio.current

                        let scale =
                            match int wheel.deltaMode with
                            | 1 -> float GitPagedDiffDisplay.RowHeightPx
                            | 2 ->
                                width
                                * (if onPrevious then
                                       splitRatio.current
                                   else
                                       1.0 - splitRatio.current)
                            | _ -> 1.0

                        let bar = if onPrevious then previousBarRef else currentBarRef

                        bar.current
                        |> Option.iter (fun element -> element.scrollLeft <- element.scrollLeft + amount * scale)

                    let onWheel (event: Event) =
                        let wheel = unbox<WheelEvent> event

                        // Chromium sends a trackpad pinch and Ctrl+wheel as wheel events with ctrlKey set.
                        // They keep the default handling.
                        if
                            not wheel.ctrlKey
                            && (abs wheel.deltaX > abs wheel.deltaY || (wheel.shiftKey && wheel.deltaX = 0.0))
                        then
                            event.preventDefault ()
                            scrollPane wheel (if wheel.deltaX = 0.0 then wheel.deltaY else wheel.deltaX)
                        elif isCapped () && not wheel.ctrlKey && abs wheel.deltaY >= abs wheel.deltaX then
                            let scale =
                                match int wheel.deltaMode with
                                | 1 -> float GitPagedDiffDisplay.RowHeightPx
                                | 2 -> float bodyScroll.clientHeight
                                | _ -> 1.0

                            event.preventDefault ()

                            if wheel.deltaX <> 0.0 then
                                scrollPane wheel wheel.deltaX

                            moveLogical (wheel.deltaY * scale)
                        elif not wheel.ctrlKey && wheel.deltaX <> 0.0 then
                            // The vertical scroll goes on natively, and the side part moves the pane.
                            scrollPane wheel wheel.deltaX

                    let onKeyDown (event: Event) =
                        let keyboard = unbox<KeyboardEvent> event
                        let view = float bodyScroll.clientHeight

                        // Space on a button of a row presses the button.
                        let onScroller = obj.ReferenceEquals(event.target, bodyScroll)

                        if
                            onScroller
                            && (keyboard.key = "ArrowLeft" || keyboard.key = "ArrowRight")
                            && not (keyboard.ctrlKey || keyboard.altKey || keyboard.metaKey || keyboard.shiftKey)
                        then
                            // Each pane steps from its own position.
                            event.preventDefault ()

                            for bar in [ previousBarRef; currentBarRef ] do
                                bar.current
                                |> Option.iter (fun element ->
                                    element.scrollLeft <-
                                        element.scrollLeft + (if keyboard.key = "ArrowLeft" then -40.0 else 40.0)
                                )
                        elif isCapped () then
                            let modifierFree = not (keyboard.ctrlKey || keyboard.altKey || keyboard.metaKey)

                            // Chromium on macOS scrolls a page for Option+ArrowUp/Down. In a capped diff the
                            // native step moves several logical pages and skips rows.
                            let optionOnly = keyboard.altKey && not keyboard.ctrlKey && not keyboard.metaKey

                            let target =
                                match keyboard.key with
                                | "ArrowDown" when optionOnly -> Some(logicalTop () + view * 0.875)
                                | "ArrowUp" when optionOnly -> Some(logicalTop () - view * 0.875)
                                | _ when not modifierFree -> None
                                | "ArrowDown" -> Some(logicalTop () + 40.0)
                                | "ArrowUp" -> Some(logicalTop () - 40.0)
                                | "PageDown" -> Some(logicalTop () + view * 0.875)
                                | "PageUp" -> Some(logicalTop () - view * 0.875)
                                | " " when onScroller ->
                                    Some(logicalTop () + (if keyboard.shiftKey then -0.875 else 0.875) * view)
                                | "Home" -> Some 0.0
                                | "End" -> Some totalRef.current
                                | _ -> None

                            match target with
                            | Some value ->
                                event.preventDefault ()
                                setLogicalTop value
                                syncRef.current ()
                                anchorRef.current ()
                            | None -> ()
                        elif
                            onScroller
                            && not (keyboard.ctrlKey || keyboard.altKey || keyboard.metaKey || keyboard.shiftKey)
                        then
                            // Chromium scrolls to the end of a long diff smoothly, and the pages it passes
                            // on the way would each be replayed. The jump goes to the target at once.
                            match keyboard.key with
                            | "Home" ->
                                event.preventDefault ()
                                bodyScroll.scrollTop <- 0.0
                            | "End" ->
                                event.preventDefault ()
                                bodyScroll.scrollTop <- float bodyScroll.scrollHeight - view
                            | _ -> ()

                    let onFocusIn (event: Event) =
                        let target = unbox<HTMLElement> event.target

                        focusedInside.current <-
                            if obj.ReferenceEquals(target, bodyScroll) then
                                None
                            else
                                Some target

                        // A control outside the visible part of its text box gets its bar moved, so
                        // the user sees what has the focus.
                        match target.closest "[data-text-box]" |> Option.map unbox<HTMLElement> with
                        | Some box ->
                            let bar =
                                if
                                    box.getAttribute "data-text-box" = GitPagedDiffDisplay.sideName
                                        PagedDiffSide.Previous
                                then
                                    previousBarRef
                                else
                                    currentBarRef

                            let targetBounds = target.getBoundingClientRect ()
                            let boxBounds = box.getBoundingClientRect ()

                            bar.current
                            |> Option.iter (fun element ->
                                if targetBounds.right > boxBounds.right then
                                    element.scrollLeft <- element.scrollLeft + targetBounds.right - boxBounds.right
                                elif targetBounds.left < boxBounds.left then
                                    element.scrollLeft <- element.scrollLeft - (boxBounds.left - targetBounds.left)
                            )
                        | None -> ()

                    // A control that is removed or disabled loses the focus although the user did not move
                    // it. Any other focus change forgets the element.
                    let onFocusOut (event: Event) =
                        let target = unbox<HTMLElement> event.target

                        if
                            Browser.Dom.document.contains target
                            && not (unbox<HTMLButtonElement> target).disabled
                        then
                            focusedInside.current <- None

                    let window = Browser.Dom.window

                    bodyScroll.addEventListener (
                        "wheel",
                        onWheel,
                        Fable.Core.JsInterop.jsOptions<AddEventListenerOptions> (fun options ->
                            options.passive <- false
                        )
                    )

                    bodyScroll.addEventListener ("keydown", onKeyDown)
                    bodyScroll.addEventListener ("focusin", onFocusIn)
                    bodyScroll.addEventListener ("focusout", onFocusOut)
                    bodyScroll.addEventListener ("pointerdown", startGesture)
                    window.addEventListener ("pointerup", endGesture)
                    window.addEventListener ("pointercancel", endGesture)
                    window.addEventListener ("mouseup", endGesture)
                    window.addEventListener ("blur", endGesture)
                    window.addEventListener ("pointermove", moveGesture)

                    FsReact.createDisposable (fun () ->
                        bodyScroll.removeEventListener ("wheel", onWheel)
                        bodyScroll.removeEventListener ("keydown", onKeyDown)
                        bodyScroll.removeEventListener ("focusin", onFocusIn)
                        bodyScroll.removeEventListener ("focusout", onFocusOut)
                        bodyScroll.removeEventListener ("pointerdown", startGesture)
                        window.removeEventListener ("pointerup", endGesture)
                        window.removeEventListener ("pointercancel", endGesture)
                        window.removeEventListener ("mouseup", endGesture)
                        window.removeEventListener ("blur", endGesture)
                        window.removeEventListener ("pointermove", moveGesture)
                    )
                | _ -> FsReact.createDisposable (fun () -> ())
            ),
            [||]
        )

        let contentWidth = contentRect.width |> Option.map int |> Option.defaultValue 0

        // The header and the scroll bars are as wide as the rows, which excludes the vertical
        // scroll bar of the body.
        let stripWidth = if contentWidth > 0 then $"{contentWidth}px" else "100%"

        // The bars stay visible while they overflow, unlike the fading bar of the body.
        let barStyle = [
            style.custom ("scrollbarWidth", "thin")
            style.custom (
                "scrollbarColor",
                "color-mix(in oklch, var(--color-base-content) 40%, transparent) transparent"
            )
        ]

        // Reports the logical scroll offset to the virtualizer on every scroll event, and that the
        // scrolling ended once the events stop.
        let observeOffset =
            Func<obj, Action<float, bool>, Action>(fun _ callback ->
                match bodyScrollRef.current with
                | Some element ->
                    let timer = ref 0.0

                    let onScroll (_: Event) =
                        let offset = logicalTop ()
                        callback.Invoke(offset, true)

                        // Above the cap the rows are drawn relative to the native scroll position
                        // read during render, and the virtualizer renders again only when its range
                        // changes. Inside a long placeholder the range stays the same for a long
                        // way, so the render happens here, before the browser paints.
                        if isCapped () && element.scrollTop <> renderedPhysical.current then
                            ReactDomApi.flushSync renderLayout

                        Browser.Dom.window.clearTimeout timer.Value

                        timer.Value <-
                            Browser.Dom.window.setTimeout ((fun () -> callback.Invoke(logicalTop (), false)), 150)

                    element.addEventListener ("scroll", onScroll)

                    Action(fun () ->
                        Browser.Dom.window.clearTimeout timer.Value
                        element.removeEventListener ("scroll", onScroll)
                    )
                | None -> Action(fun () -> ())
            )

        let rowVirtualizer =
            Virtual.useVirtualizer (
                count = rows.Length,
                getScrollElement = (fun () -> bodyScrollRef.current),
                estimateSize = (fun index -> GitPagedDiffDisplay.rowExtent rows.[index].Content),
                getItemKey = (fun index -> rows.[index].Key),
                overscan = GitPagedDiffDisplay.OverscanRows,
                gap = 0,
                observeElementOffset = observeOffset
            )

        // Above the cap a size change must not move the native position, which does not equal the
        // logical offset the virtualizer would write.
        rowVirtualizer.shouldAdjustScrollPositionOnItemSizeChange <-
            (if isCapped () then
                 Some(Func<Virtual.VirtualItem, float, obj, bool>(fun _ _ _ -> false))
             else
                 None)

        totalRef.current <- defaultArg pinnedTotal.current (float (rowVirtualizer.getTotalSize ()))

        // Brings the virtualizer to the logical offset, and renders the rows for it.
        let syncVirtualizer () =
            let logical = logicalTop ()

            if abs (rowVirtualizer.scrollOffset - logical) >= 1.0 then
                rowVirtualizer.scrollOffset <- logical
                renderLayout ()

        syncRef.current <- syncVirtualizer

        let visibleItems = rowVirtualizer.getVirtualItems ()

        let virtualItems: GitPagedDiffDisplay.VirtualRow[] =
            if visibleItems.Length = 0 && rows.Length > 0 then
                let initialPosition =
                    bodyScrollRef.current
                    |> Option.map (fun _ -> logicalTop () = 0.0)
                    |> Option.defaultValue true

                if initialPosition then
                    let sizes =
                        [| 0 .. min (rows.Length - 1) 8 |]
                        |> Array.map (fun index -> GitPagedDiffDisplay.rowExtent rows.[index].Content)

                    sizes
                    |> Array.mapi (fun index size ->
                        ({
                            Key = rows.[index].Key
                            Index = index
                            Start = Array.sum sizes.[0 .. index - 1]
                            Size = size
                        }
                        : GitPagedDiffDisplay.VirtualRow)
                    )
                else
                    [||]
            else
                visibleItems
                |> Array.map (fun item ->
                    ({
                        Key = item.key
                        Index = item.index
                        Start = item.start
                        Size = item.size
                    }
                    : GitPagedDiffDisplay.VirtualRow)
                )

        // The rendered rows include the overscan. Replays only go to rows the user can see.
        let viewportItems () =
            match bodyScrollRef.current with
            | Some element ->
                let top = int (logicalTop ())
                let bottom = top + int element.clientHeight

                virtualItems
                |> Array.filter (fun item -> item.Start < bottom && item.Start + item.Size > top)
            | None -> [||]

        // The viewport as offsets in the scroll content, and whether it sits at the end of the
        // diff. A viewport that starts at the top does not count as the end.
        let viewState () =
            match bodyScrollRef.current with
            | Some element ->
                let top = logicalTop ()
                let atEnd = top > 1.0 && top >= totalRef.current - element.clientHeight - 1.0
                top, top + element.clientHeight, atEnd
            | None -> 0.0, 0.0, false

        // The pages the viewport shows of a placeholder, in the order they replay. A page is
        // found by the offset it covers, wherever the loaded pages are. At the end of the diff
        // the last page comes first.
        let placeholderPages (item: GitPagedDiffDisplay.VirtualRow) : string[] =
            let top, bottom, atEnd = viewState ()

            match rows.[item.Index].Content with
            | GitPagedDiffDisplay.Evicted(pageId, _) -> [| pageId |]
            | GitPagedDiffDisplay.Folded(_, pageIds, ends) ->
                GitPagedDiffDisplay.foldedPagesInView ends (float item.Start) top bottom atEnd
                |> Array.map (fun index -> pageIds.[index])
            | _ -> [||]

        // The first and the last part with a row in the viewport, or -1 for both without one.
        let visiblePartRange (items: GitPagedDiffDisplay.VirtualRow[]) =
            let indices =
                items
                |> Array.map (fun item -> rowParts.[item.Index])
                |> Array.filter (fun index -> index >= 0)

            if indices.Length = 0 then
                -1, -1
            else
                Array.min indices, Array.max indices

        // The rows in the viewport, taken from the scroll element and the measured row sizes. The
        // rendered rows can still belong to an earlier scroll position, because the virtualizer
        // learns of a scroll only from the scroll event.
        let rowsInViewport () : Virtual.VirtualItem[] =
            match bodyScrollRef.current with
            | Some element when rows.Length > 0 ->
                let top = logicalTop ()
                let bottom = top + element.clientHeight

                // Looking up the first row also brings the measurements up to date with the sizes
                // measured since the last render.
                match rowVirtualizer.getVirtualItemForOffset top with
                | Some first ->
                    let measurements = rowVirtualizer.measurementsCache
                    let last = (min rows.Length measurements.Length) - 1
                    let found = ResizeArray<Virtual.VirtualItem>()
                    let mutable index = first.index

                    while index <= last && float measurements.[index].start < bottom do
                        let item = measurements.[index]

                        if float item.``end`` > top then
                            found.Add item

                        index <- index + 1

                    found.ToArray()
                | None -> [||]
            | _ -> [||]

        let captureAnchor () : GitPagedDiffDisplay.AnchorSnapshot =
            let top = logicalTop ()

            // A row anchors by its key, and a placeholder by the page at the top of the viewport
            // and the offset of that page.
            let candidatesOf (items: GitPagedDiffDisplay.VirtualRow[]) =
                items
                |> Array.collect (fun item ->
                    let anchorAt key (start: float) = [|
                        ({
                            Key = key
                            Start = start
                            Offset = start - top
                        }
                        : GitPagedDiffDisplay.AnchorCandidate)
                    |]

                    match rows.[item.Index].Content with
                    | GitPagedDiffDisplay.Evicted(pageId, _) ->
                        anchorAt (GitPagedDiffDisplay.pageAnchorKey pageId) (float item.Start)
                    | GitPagedDiffDisplay.Folded(_, pageIds, ends) ->
                        let pageIndex =
                            GitPagedDiffDisplay.pageAtOffset ends (max top (float item.Start) - float item.Start)

                        // The neighbours anchor as well. When a replay turns the top page into rows in
                        // the same update that evicts a page above, a neighbour is still a placeholder.
                        [|
                            max 0 (pageIndex - 1) .. min (pageIds.Length - 1) (pageIndex + 1)
                        |]
                        |> Array.collect (fun index ->
                            anchorAt
                                (GitPagedDiffDisplay.pageAnchorKey pageIds.[index])
                                (float item.Start
                                 + float (
                                     GitPagedDiffDisplay.RowHeightPx * GitPagedDiffDisplay.pageStartRows ends index
                                 ))
                        )
                    | content when GitPagedDiffDisplay.isAnchorable content -> anchorAt item.Key (float item.Start)
                    | _ -> [||]
                )

            // The rows in the viewport anchor first. The rows rendered around the viewport follow,
            // so a placeholder that holds the whole viewport still has rows to anchor to.
            // The rows around the viewport are the rows the virtualizer renders as overscan.
            let toRow (item: Virtual.VirtualItem) : GitPagedDiffDisplay.VirtualRow = {
                Key = item.key
                Index = item.index
                Start = item.start
                Size = item.size
            }

            let inView = rowsInViewport () |> Array.map toRow

            let around =
                if inView.Length = 0 then
                    [||]
                else
                    let measurements = rowVirtualizer.measurementsCache
                    let limit = (min rows.Length measurements.Length) - 1
                    let first = inView.[0].Index
                    let last = inView.[inView.Length - 1].Index

                    Array.append [|
                        max 0 (first - GitPagedDiffDisplay.OverscanRows) .. first - 1
                    |] [|
                        last + 1 .. min limit (last + GitPagedDiffDisplay.OverscanRows)
                    |]
                    |> Array.map (fun index -> toRow measurements.[index])

            // Rows below the viewport come before the rows above it, since they move with every change above.
            let candidates =
                Array.append
                    (candidatesOf (inView |> Array.sortBy _.Start))
                    (candidatesOf (
                        around
                        |> Array.sortBy (fun item ->
                            (if float item.Start >= top then 0 else 1), abs (float item.Start - top)
                        )
                    ))

            ({
                Keys = rows |> Array.map (fun row -> row.Key)
                Candidates = candidates
            }
            : GitPagedDiffDisplay.AnchorSnapshot)

        anchorRef.current <- fun () -> previousLayout.current <- Some(captureAnchor ())

        let rowsSignature = rows |> Array.map (fun row -> row.Key) |> String.concat "\u001f"

        // When the rows above the view change their height, the first row that was in view and is
        // still there keeps its distance from the top of the viewport. The distance comes from the
        // snapshot, since the browser lowers the scroll top on its own when the rows above shrink.
        // A replay gives the rows the height of their placeholder, so the rows stay where they are
        // and the scroll position is left alone.
        React.useLayoutEffect (
            (fun () ->
                let keys = rows |> Array.map (fun row -> row.Key)

                match previousLayout.current, bodyScrollRef.current with
                | Some previous, Some scrollElement when previous.Keys <> keys ->
                    let indexOfKey = Dictionary<string, int>()
                    keys |> Array.iteri (fun index key -> indexOfKey.[key] <- index)

                    // The rows of the placeholders. A page anchor looks for its page only in these,
                    // so the lookup does not grow with the number of pages a placeholder holds.
                    let placeholderRows = [|
                        for index in 0 .. rows.Length - 1 do
                            if GitPagedDiffDisplay.isPlaceholder rows.[index].Content then
                                index
                    |]

                    // Asking for the total size brings the measurements up to date with the rows
                    // measured in this commit.
                    rowVirtualizer.getTotalSize () |> ignore

                    // The start of a page inside the placeholder that holds it.
                    let startOfPage (pageId: string) =
                        placeholderRows
                        |> Array.tryPick (fun index ->
                            let rowStart = float rowVirtualizer.measurementsCache.[index].start

                            match rows.[index].Content with
                            | GitPagedDiffDisplay.Evicted(evictedId, _) when evictedId = pageId -> Some rowStart
                            | GitPagedDiffDisplay.Folded(_, pageIds, ends) ->
                                pageIds
                                |> Array.tryFindIndex (fun foldedId -> foldedId = pageId)
                                |> Option.map (fun pageIndex ->
                                    rowStart
                                    + float (
                                        GitPagedDiffDisplay.RowHeightPx
                                        * GitPagedDiffDisplay.pageStartRows ends pageIndex
                                    )
                                )
                            | _ -> None
                        )

                    let startOf (key: string) =
                        match GitPagedDiffDisplay.pageIdOfAnchorKey key with
                        | Some pageId -> startOfPage pageId
                        | None ->
                            match indexOfKey.TryGetValue key with
                            | true, index -> Some(float rowVirtualizer.measurementsCache.[index].start)
                            | _ -> None

                    let anchor =
                        previous.Candidates
                        |> Array.tryPick (fun candidate ->
                            startOf candidate.Key |> Option.map (fun start -> candidate, start)
                        )

                    let target =
                        match anchor with
                        | Some(candidate, start) ->
                            if abs (start - candidate.Start) >= 1.0 then
                                Some(start - candidate.Offset)
                            else
                                None
                        | None -> None

                    match target with
                    | Some target ->
                        if abs (logicalTop () - target) >= 1.0 then
                            setLogicalTop target

                        // Until the scroll event arrives, the virtualizer would lay rows out for
                        // the old position and correct size changes against it, which moves the
                        // view back.
                        syncVirtualizer ()
                    | None -> ()
                | _ -> ()

                // A scroll target moves the view once, as soon as a row shows its line. It wins
                // over the row kept in place above.
                match scrollTarget, bodyScrollRef.current with
                | Some target, Some scrollElement when appliedScrollToken.current <> Some target.Token ->
                    let line =
                        readingRows.current
                        |> Array.tryPick (GitPagedDiffDisplay.lineOfRow target.Side)
                        |> Option.defaultValue target.Line

                    match GitPagedDiffDisplay.rowIndexOfLine target.Side line rows with
                    | Some index ->
                        appliedScrollToken.current <- Some target.Token
                        readingRows.current <- [||]
                        rowVirtualizer.getTotalSize () |> ignore
                        setLogicalTop (float rowVirtualizer.measurementsCache.[index].start)
                        syncVirtualizer ()
                    | None -> ()
                | _ -> ()

                previousLayout.current <- Some(captureAnchor ())
            ),
            [|
                box rowsSignature
                box (scrollTarget |> Option.map _.Token)
            |]
        )

        // The logical offset stays when the total, the cap or the view height change, and when the
        // scroll range crosses the cap in either direction. The native position follows it, so the
        // thumb keeps its fraction of the track.
        let viewHeight =
            match bodyScrollRef.current with
            | Some element -> element.clientHeight
            | None -> 0.0

        React.useLayoutEffect (
            (fun () ->
                let capped = isCapped ()

                if capped || wasCapped.current then
                    let before = bodyScrollRef.current |> Option.map _.scrollTop
                    setLogicalTop logicalRef.current
                    syncVirtualizer ()

                    // The rows were placed for the native position before it moved.
                    if before <> (bodyScrollRef.current |> Option.map _.scrollTop) then
                        renderLayout ()

                wasCapped.current <- capped
            ),
            [| box totalRef.current; box cap; box viewHeight |]
        )

        // An expansion replaces the row of the control the user pressed. The focus then falls to the
        // page, and the scroll keys no longer reach the viewer. The scroll element takes it over.
        React.useLayoutEffect (fun () ->
            match focusedInside.current, bodyScrollRef.current with
            | Some element, Some scrollElement ->
                let active = Browser.Dom.document.activeElement

                let focusLost =
                    obj.ReferenceEquals(active, null)
                    || obj.ReferenceEquals(active, Browser.Dom.document.body)

                if
                    focusLost
                    && (not (Browser.Dom.document.contains element)
                        || (unbox<HTMLButtonElement> element).disabled)
                then
                    focusedInside.current <- None
                    scrollElement.focus ()
            | _ -> ()
        )

        // The diff grows while it is indexed. A view at the end moves to the new end.
        let totalSize = rowVirtualizer.getTotalSize ()

        React.useLayoutEffect (
            (fun () ->
                match bodyScrollRef.current with
                | Some element when followEnd.current && pinnedTotal.current.IsNone ->
                    let target = float totalSize - element.clientHeight

                    if target > logicalTop () + 1.0 then
                        setLogicalTop target
                        syncVirtualizer ()

                    if not hasMore then
                        followEnd.current <- false
                | _ -> ()
            ),
            [| box totalSize; box gesture |]
        )

        let renderedKeys =
            virtualItems
            |> Array.map (fun item -> rows.[item.Index].Key)
            |> String.concat "\u001f"

        let viewportKeys =
            viewportItems ()
            |> Array.map (fun item -> rows.[item.Index].Key)
            |> String.concat "\u001f"

        React.useEffect (
            (fun () ->
                if status = PagedDiffStatus.Reopening then
                    readingRows.current <- rowsInViewport () |> Array.map (fun item -> rows.[item.index].Content)

                FsReact.createDisposable (fun () -> ())
            ),
            [| box status; box viewportKeys |]
        )

        let replaySignature = pendingReplays |> String.concat "\u001f"
        let requestedReplay = React.useRef (HashSet<string>())
        let lastReplaySignature = React.useRef replaySignature
        // Holds the key of the next page at the time of the last next page request, so the
        // continue row asks once for each next page.
        let requestedNext = React.useRef<string option option> None

        React.useEffect (
            (fun () ->
                // A replay the loader dropped while another one ran is asked for again once the
                // running replays change.
                if lastReplaySignature.current <> replaySignature then
                    lastReplaySignature.current <- replaySignature
                    requestedReplay.current.Clear()

                // A replayed page can be evicted again later. Forgetting pages that are no longer
                // placeholders lets the viewer ask for them once more. Only the few requested
                // pages are looked up in the set of the evicted pages.
                if requestedReplay.current.Count > 0 then
                    let evicted = evictedIds.Value

                    requestedReplay.current <-
                        HashSet<string>(requestedReplay.current |> Seq.filter (fun pageId -> evicted.has pageId))

                // One replay at a time, for the page at the top of the viewport, and then the
                // pages below it. At the end of the diff the page at the bottom comes first.
                match requestReplay with
                | Some callback when pendingReplays.Length = 0 ->
                    let viewport = viewportItems ()
                    let _, _, atEnd = viewState ()

                    // A page whose replay failed waits for the user to ask again.
                    let mayReplay pageId =
                        not (requestedReplay.current.Contains pageId)
                        && not (failedReplays |> Array.contains pageId)

                    (if atEnd then Array.rev viewport else viewport)
                    |> Array.collect placeholderPages
                    |> Array.tryFind mayReplay
                    |> Option.iter (fun pageId ->
                        requestedReplay.current.Add pageId |> ignore
                        let first, last = visiblePartRange viewport
                        callback pageId first last
                    )
                | _ -> ()

                // The continue row asks for the next page once it is within the rendered rows
                // around the viewport. Its position comes from the scroll element, since the
                // rendered rows can still belong to the position before a restore.
                let continueNear =
                    match bodyScrollRef.current with
                    | Some element when rows.Length > 0 ->
                        let measurements = rowVirtualizer.measurementsCache
                        let lastIndex = rows.Length - 1

                        match rows.[lastIndex].Content with
                        | GitPagedDiffDisplay.Continue _ when measurements.Length = rows.Length ->
                            let renderedBottom =
                                logicalTop ()
                                + element.clientHeight
                                + float (GitPagedDiffDisplay.OverscanRows * rowHeight)

                            float measurements.[lastIndex].start < renderedBottom
                        | _ -> false
                    | _ -> false

                // A note at the end of the diff says that reading on is the choice of the host.
                match requestNext with
                | Some callback when
                    continueNear
                    && endNote.IsNone
                    && status <> PagedDiffStatus.LoadingNext
                    && requestedNext.current <> Some nextKey
                    ->
                    requestedNext.current <- Some nextKey
                    callback ()
                | _ -> ()

                FsReact.createDisposable (fun () -> ())
            ),
            [|
                box renderedKeys
                box viewportKeys
                box status
                box requestReplay
                box requestNext
                box replaySignature
                box nextKey
                box endNote
            |]
        )

        let replayVisible =
            requestReplay
            |> Option.map (fun callback ->
                fun pageId ->
                    let first, last = visiblePartRange (viewportItems ())
                    callback pageId first last
            )

        // Above the cap a row is drawn at its distance from the viewport, counted from the native
        // scroll position, so rows keep their size. A placeholder taller than the viewport shows
        // only the part the viewport reaches.
        let viewportLogical = logicalTop ()

        let viewportPhysical =
            match bodyScrollRef.current with
            | Some element -> element.scrollTop
            | None -> 0.0

        renderedPhysical.current <- viewportPhysical

        let viewportHeight =
            match bodyScrollRef.current with
            | Some element -> element.clientHeight
            | None -> 0.0

        let displayStart (item: GitPagedDiffDisplay.VirtualRow) =
            if isCapped () then
                let start =
                    if GitPagedDiffDisplay.isPlaceholder rows.[item.Index].Content then
                        max (float item.Start) viewportLogical
                    else
                        float item.Start

                int (Math.Round(start - viewportLogical + viewportPhysical))
            else
                item.Start

        let displayHeight (item: GitPagedDiffDisplay.VirtualRow) =
            if isCapped () && GitPagedDiffDisplay.isPlaceholder rows.[item.Index].Content then
                let first = max (float item.Start) viewportLogical
                let last = min (float (item.Start + item.Size)) (viewportLogical + viewportHeight)
                max GitPagedDiffDisplay.RowHeightPx (int (last - first))
            else
                GitPagedDiffDisplay.rowExtent rows.[item.Index].Content

        Html.div [
            prop.ref gridRootRef
            prop.testId $"{prefix}-grid"
            prop.className "swt:relative swt:flex swt:min-h-0 swt:flex-1 swt:flex-col"
            // The rows and the scroll bars share these lengths. The scroll bars write the scroll
            // positions (--paged-scroll-previous and --paged-scroll-current) straight to this
            // element, so a render never resets them.
            prop.style [
                style.custom ("--paged-number-column", numberColumn)
                style.custom ("--paged-text-previous", previousText)
                style.custom ("--paged-text-current", currentText)
            ]
            prop.children [
                Html.div [
                    prop.className "swt:grid swt:divide-x swt:divide-base-content/10"
                    prop.style [
                        style.custom ("width", stripWidth)
                        style.custom ("gridTemplateColumns", "minmax(0, var(--paged-split, 50%)) minmax(0, 1fr)")
                    ]
                    prop.children [
                        for label, title in [ "Previous", previousTitle; "Current", currentTitle ] do
                            Html.div [
                                prop.className
                                    "swt:flex swt:min-w-0 swt:items-start swt:justify-between swt:gap-3 swt:px-4 swt:py-3 swt:bg-base-200 swt:border-b swt:border-base-content/10"
                                prop.children [
                                    Html.div [
                                        prop.className "swt:min-w-0 swt:flex swt:flex-col swt:gap-0.5"
                                        prop.children [
                                            Html.span [
                                                prop.className
                                                    "swt:text-[11px] swt:uppercase swt:tracking-wide swt:text-base-content/60"
                                                prop.text label
                                            ]
                                            Html.span [
                                                prop.className "swt:truncate swt:text-sm swt:font-semibold"
                                                prop.text title
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                    ]
                ]
                Html.div [
                    prop.ref bodyScrollRef
                    // The scroll element takes focus when a row is clicked, so the scroll keys reach it.
                    prop.tabIndex 0
                    prop.className
                        "swt:min-h-0 swt:flex-1 swt:overflow-x-hidden swt:overflow-y-auto swt:scrollbar-fade swt:focus-visible:outline swt:focus-visible:outline-2 swt:focus-visible:-outline-offset-2 swt:focus-visible:outline-primary"
                    prop.onScroll (fun event ->
                        previousLayout.current <- Some(captureAnchor ())
                        let element = event.currentTarget :?> HTMLElement
                        let top = logicalTop ()

                        followEnd.current <-
                            indexing
                            && hasMore
                            && top > 1.0
                            && top >= totalRef.current - element.clientHeight - 1.0
                    )
                    prop.children [
                        Html.div [
                            prop.ref bodyContentRef
                            prop.testId $"{prefix}-content"
                            prop.custom ("data-provisional", (if hasMore then "true" else "false"))
                            prop.className "swt:relative swt:w-full swt:font-mono swt:text-xs"
                            prop.style [
                                style.height (int (GitPagedDiffDisplay.physicalHeight cap totalRef.current))
                                // Rows laid out past the content height must not add to the scroll
                                // range. That happens above the cap and while the scrollbar is held.
                                if isCapped () || pinnedTotal.current.IsSome then
                                    style.custom ("overflow", "clip")
                            ]
                            prop.children [
                                for item in virtualItems do
                                    React.KeyedFragment(
                                        item.Key,
                                        [
                                            GitPagedDiffViewer.RowView {
                                                Row = rows.[item.Index]
                                                Index = item.Index
                                                Start = displayStart item
                                                Height = displayHeight item
                                                MeasureElementRef = rowVirtualizer.measureElement
                                                Prefix = prefix
                                                ExpandingGaps = expandingGaps
                                                LoadingNext = status = PagedDiffStatus.LoadingNext
                                                HasMore = hasMore
                                                Indexing = indexing
                                                HideIndexingStatus = hideIndexingStatus
                                                NextFailed = nextFailed
                                                Progress = progress
                                                FoldedTarget =
                                                    (match rows.[item.Index].Content with
                                                     | GitPagedDiffDisplay.Folded(_, pageIds, _) ->
                                                         placeholderPages item
                                                         |> Array.tryHead
                                                         |> Option.defaultValue pageIds.[0]
                                                     | _ -> "")
                                                RequestExpand = requestExpand
                                                RequestLineSlice = requestLineSlice
                                                RequestLineBefore = requestLineBefore
                                                PendingLineSlices = pendingLineSlices
                                                PendingReplays = pendingReplays
                                                RequestReplay = replayVisible
                                                RequestNext = requestNext
                                                FailedGaps = failedGaps
                                                FailedLineSlices = failedLineSlices
                                                FailedReplays = failedReplays
                                                FailureNote = failureNote
                                                EndNote = endNote
                                            }
                                        ]
                                    )
                            ]
                        ]
                    ]
                ]
                // Two bars scroll the text of the panes on their own. The third moves both. The
                // bars are as wide as the rows, and the font matches the rows, since the widths
                // count characters.
                Html.div [
                    prop.className "swt:shrink-0 swt:border-t swt:border-base-content/10 swt:font-mono swt:text-xs"
                    prop.style [ style.custom ("width", stripWidth) ]
                    prop.children [
                        Html.div [
                            prop.className "swt:grid"
                            prop.style [
                                style.custom (
                                    "gridTemplateColumns",
                                    "minmax(0, var(--paged-split, 50%)) minmax(0, 1fr)"
                                )
                            ]
                            prop.children [
                                for side, barRef in [ "previous", previousBarRef; "current", currentBarRef ] do
                                    Html.div [
                                        prop.ref barRef
                                        prop.testId $"{prefix}-hscroll-{side}"
                                        prop.className "swt:h-3 swt:min-w-0 swt:overflow-x-auto swt:overflow-y-hidden"
                                        prop.style barStyle
                                        prop.onScroll (fun event ->
                                            let bar = event.currentTarget :?> HTMLElement

                                            gridRootRef.current
                                            |> Option.iter (fun root ->
                                                GitPagedDiffDisplay.setStyleProperty
                                                    root
                                                    $"--paged-scroll-{side}"
                                                    $"{bar.scrollLeft}px"
                                            )
                                        )
                                        prop.children [
                                            Html.div [
                                                prop.style [
                                                    style.height 1
                                                    style.custom (
                                                        "width",
                                                        $"calc(var(--paged-number-column) + var(--paged-text-{side}))"
                                                    )
                                                ]
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                        // The range of this bar is twice the larger range of the two panes, so half of
                        // its position moves each pane to the same column. The percentages are shares of
                        // the bar, which is as wide as the strip.
                        Html.div [
                            prop.testId $"{prefix}-hscroll-both"
                            prop.className "swt:h-3 swt:overflow-x-auto swt:overflow-y-hidden"
                            prop.style barStyle
                            prop.onScroll (fun event ->
                                let bar = event.currentTarget :?> HTMLElement
                                let range = float bar.scrollWidth - float bar.clientWidth

                                // A range that shrinks under the bar clamps it back to the new end.
                                // The panes keep their positions then, since the user did not move
                                // the bar. A move of the user to the end goes forward.
                                let clamped =
                                    range < bothMax.current
                                    && bar.scrollLeft >= range - 1.0
                                    && bar.scrollLeft < bothLeft.current

                                bothMax.current <- range
                                bothLeft.current <- bar.scrollLeft

                                if not clamped then
                                    for barRef in [ previousBarRef; currentBarRef ] do
                                        barRef.current
                                        |> Option.iter (fun element -> element.scrollLeft <- bar.scrollLeft / 2.0)
                            )
                            prop.children [
                                Html.div [
                                    prop.style [
                                        style.height 1
                                        style.custom (
                                            "width",
                                            "calc(100% + 2 * max(0px, var(--paged-number-column) + var(--paged-text-previous) - var(--paged-split, 50%), var(--paged-number-column) + var(--paged-text-current) - (100% - var(--paged-split, 50%))))"
                                        )
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                // The overlay is as wide as the panes and ends above the shared bar. Only the
                // handle takes pointer events.
                Html.div [
                    prop.ref overlayRef
                    prop.className "swt:pointer-events-none swt:absolute swt:top-0 swt:left-0 swt:bottom-3"
                    prop.style [ style.custom ("width", stripWidth) ]
                    prop.children [
                        Html.div [
                            prop.className "swt:pointer-events-auto swt:absolute swt:top-0 swt:bottom-0"
                            // A press on the handle must not take the focus from the scroller, so the
                            // scroll keys keep working after a drag.
                            prop.onMouseDown (fun event -> event.preventDefault ())
                            prop.style [ style.custom ("left", "var(--paged-split, 50%)") ]
                            prop.children [
                                SplitResizer.SplitResizer(
                                    overlayRef,
                                    false,
                                    (fun ratio ->
                                        splitRatio.current <- ratio

                                        gridRootRef.current
                                        |> Option.iter (fun root ->
                                            GitPagedDiffDisplay.setStyleProperty
                                                root
                                                "--paged-split"
                                                $"{ratio * 100.0}%%"
                                        )
                                    ),
                                    testId = $"{prefix}-split-resizer"
                                )
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ProgressState
        (prefix: string, stateName: string, progress: PagedProgress option, pending: PagedPending option)
        =
        let progressContent =
            match progress with
            | None ->
                Html.span [
                    prop.className "swt:text-xs swt:text-base-content/60"
                    prop.text ""
                ]
            | Some value ->
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        Html.div [
                            prop.className
                                "swt:flex swt:items-center swt:justify-between swt:gap-3 swt:text-xs swt:text-base-content/70"
                            prop.children [
                                Html.span [ prop.text $"{progressPercentage value}%%" ]
                                Html.span [
                                    prop.text $"{formatBytes value.ValidatedBytes} / {formatBytes value.TotalBytes}"
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "swt:h-1.5 swt:overflow-hidden swt:rounded-full swt:bg-base-300"
                            prop.children [
                                Html.div [
                                    prop.className "swt:h-full swt:bg-primary"
                                    prop.style [ style.width (length.percent (progressPercentage value)) ]
                                ]
                            ]
                        ]
                    ]
                ]

        Html.div [
            prop.testId $"{prefix}-state-{stateName}"
            prop.className "swt:flex swt:min-h-0 swt:flex-1 swt:flex-col swt:gap-3 swt:p-4"
            prop.children [
                progressContent
                match pending with
                | Some value -> GitPagedDiffViewer.PendingPanel(prefix, value)
                | None -> ()
            ]
        ]

    [<ReactComponent>]
    static member private MessageState(prefix: string, stateName: string, heading: string, detail: string option) =
        Html.div [
            prop.testId $"{prefix}-state-{stateName}"
            prop.className
                "swt:flex swt:min-h-0 swt:flex-1 swt:flex-col swt:items-center swt:justify-center swt:gap-2 swt:p-8 swt:text-center"
            prop.children [
                Html.h4 [
                    prop.className "swt:text-sm swt:font-semibold"
                    prop.text heading
                ]
                match detail with
                | Some value ->
                    Html.p [
                        prop.className "swt:max-w-prose swt:text-xs swt:text-base-content/65"
                        prop.text value
                    ]
                | None -> ()
            ]
        ]

    [<ReactComponent>]
    static member private EncodingState
        (
            prefix: string,
            side: PagedDiffSide,
            sideTitle: string,
            candidates: PagedEncodingCandidate[],
            chooseEncoding: (PagedDiffSide -> string -> unit) option
        ) =
        let sideName = GitPagedDiffDisplay.sideName side

        Html.div [
            prop.testId $"{prefix}-state-encoding-choice"
            prop.className "swt:flex swt:min-h-0 swt:flex-1 swt:flex-col swt:gap-3 swt:p-4"
            prop.children [
                Html.div [
                    prop.testId $"{prefix}-encoding-side"
                    prop.custom ("data-side", sideName)
                    prop.className "swt:flex swt:min-w-0 swt:flex-col swt:gap-0.5"
                    prop.children [
                        Html.span [
                            prop.className "swt:text-sm swt:font-semibold"
                            prop.text $"Choose the encoding of the {sideName} version"
                        ]
                        Html.span [
                            prop.className "swt:truncate swt:text-xs swt:text-base-content/60"
                            prop.text sideTitle
                        ]
                    ]
                ]
                for candidate in candidates do
                    Html.div [
                        prop.className
                            "swt:flex swt:flex-col swt:gap-2 swt:rounded-box swt:border swt:border-base-300 swt:p-3"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:items-center swt:justify-between swt:gap-3"
                                prop.children [
                                    Html.span [
                                        prop.className "swt:text-sm swt:font-semibold"
                                        prop.text candidate.Encoding
                                    ]
                                    Html.button [
                                        prop.testId $"{prefix}-encoding-choice-{candidate.Encoding}"
                                        prop.className "swt:btn swt:btn-primary swt:btn-xs"
                                        prop.disabled chooseEncoding.IsNone
                                        prop.onClick (fun _ ->
                                            chooseEncoding
                                            |> Option.iter (fun callback -> callback side candidate.Encoding)
                                        )
                                        prop.text "Choose encoding"
                                    ]
                                ]
                            ]
                            // A side that turned out not to be UTF-8 while the diff was read comes
                            // with candidates that have no preview.
                            if candidate.Preview <> "" then
                                Html.pre [
                                    prop.className
                                        "swt:max-h-48 swt:overflow-auto swt:whitespace-pre-wrap swt:break-all swt:rounded-box swt:bg-base-200/60 swt:p-2 swt:font-mono swt:text-xs"
                                    prop.text candidate.Preview
                                ]
                        ]
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private ReopeningBar(prefix: string, progress: PagedProgress option) =
        let percentage = progress |> Option.map progressPercentage |> Option.defaultValue 0

        Html.div [
            prop.testId $"{prefix}-reopening"
            prop.custom ("data-progress", percentage)
            prop.className "swt:h-1 swt:w-full swt:shrink-0 swt:overflow-hidden swt:bg-base-300"
            prop.children [
                Html.div [
                    prop.className "swt:h-full swt:bg-primary"
                    prop.style [ style.width (length.percent percentage) ]
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member Viewer
        (
            parts: PagedPart[],
            status: PagedDiffStatus,
            progress: PagedProgress option,
            hasMore: bool,
            outputComplete: bool,
            ?pending: PagedPending,
            ?requestNext: unit -> unit,
            ?requestExpand: (string -> bool -> unit),
            ?expandingGaps: string[],
            ?requestLineSlice: (PagedDiffSide -> float -> float -> unit),
            // Asks for the text in front of a line slice. The numbers are the line number and the
            // UTF-16 offset where the displayed text starts.
            ?requestLineBefore: (PagedDiffSide -> float -> float -> unit),
            ?pendingLineSlices: PagedLineSliceRequest[],
            // Asks for the rows of an evicted page. The two numbers are the indices of the first
            // and the last part with a row in the viewport, or -1 for both when no part is visible.
            ?requestReplay: (string -> int -> int -> unit),
            ?pendingReplays: string[],
            ?chooseEncoding: (PagedDiffSide -> string -> unit),
            ?previousTitle: string,
            ?currentTitle: string,
            ?changeKind: GitDiffChangeKind,
            ?testIdPrefix: string,
            // Names the next page, such as its cursor. The continue row asks once for each key.
            // Without a key it asks again whenever the key of the last row of the last part changes.
            ?nextPageKey: string,
            // Gaps, line slices and evicted pages whose last request failed. Their controls show
            // the failure, and a failed page is not replayed again until the user asks.
            ?failedGaps: string[],
            ?failedLineSlices: PagedLineSliceRequest[],
            ?failedReplays: string[],
            // A source line to scroll to once a row shows it. The viewer scrolls once per token.
            ?scrollTarget: PagedScrollTarget,
            // The caller reads the remaining pages on its own. The scroll range below the pages
            // read so far is provisional and grows with each page. A view at the end stays there
            // as the diff grows.
            ?indexing: bool,
            // The host shows the indexing state and the progress itself, such as in a status bar.
            // The continue row then keeps its button and the failure state of the button only.
            ?hideIndexingStatus: bool,
            // The last read of the next page failed. The continue button shows it and asks again.
            ?nextFailed: bool,
            // The reason a gap or line control names in its tooltip while its last request failed.
            ?failureNote: string,
            // The continue row shows this note at the end of the pages read. The viewer then never asks
            // for the next page by itself, and the continue button shows only when requestNext is set.
            ?endNote: string,
            // The tallest scroll height the viewer asks the browser for. A diff above it maps the
            // native scroll range to the logical offsets.
            ?maxScrollHeight: int
        ) =
        let prefix = defaultArg testIdPrefix "git-paged-diff"
        let previousTitle = defaultArg previousTitle "Previous version"
        let currentTitle = defaultArg currentTitle "Current version"
        // The rows of the parts are built once for each change of the parts array. The key of the
        // last row of the last part stands in for the next page when the caller names none.
        let partRowsOnly, partRowParts, lastRowKey, previousText, currentText, numberColumn, evictedIds =
            React.useMemo (
                (fun () ->
                    let rows = ResizeArray<GitPagedDiffDisplay.Row>()
                    // The index of the part each row belongs to, -1 for rows outside the parts.
                    let rowParts = ResizeArray<int>()
                    let mutable lastRowKey = None
                    // The ids of the evicted pages as a native set. The replay effect asks for it only
                    // while a replay is requested, so the set is built on that first use.
                    let evictedIds =
                        lazy
                            (let ids = JS.Constructors.Set.Create<string>()

                             for part in parts do
                                 match part with
                                 | PagedPart.EvictedPage(pageId, _) -> ids.add pageId |> ignore
                                 | _ -> ()

                             ids)

                    // Evicted pages fold into one placeholder for each run, as tall as the rows of its
                    // pages together.
                    let foldRuns = GitPagedDiffDisplay.foldRuns parts
                    let mutable partIndex = 0
                    // The runs come in the order of the parts, so the next run is the only one a part can start.
                    let mutable runIndex = 0

                    while partIndex < parts.Length do
                        let startsRun =
                            runIndex < foldRuns.Length
                            && (let first, _, _ = foldRuns.[runIndex] in first = partIndex)

                        match (if startsRun then Some foldRuns.[runIndex] else None) with
                        | Some(first, last, side) ->
                            runIndex <- runIndex + 1
                            let folded = GitPagedDiffDisplay.foldedRow parts first last side
                            rows.Add folded
                            rowParts.Add -1
                            // The folded row's key stays the same while the run grows, so the key of the
                            // run's last page stands in for it.
                            lastRowKey <-
                                GitPagedDiffDisplay.buildPartRows parts.[last]
                                |> Array.tryLast
                                |> Option.map _.Key

                            partIndex <- last + 1
                        | None ->
                            let partRows = GitPagedDiffDisplay.buildPartRows parts.[partIndex]
                            rows.AddRange partRows

                            for _ in partRows do
                                rowParts.Add partIndex

                            lastRowKey <- partRows |> Array.tryLast |> Option.map _.Key
                            partIndex <- partIndex + 1

                    let rows = rows.ToArray()

                    rows,
                    rowParts.ToArray(),
                    lastRowKey,
                    GitPagedDiffDisplay.textWidth PagedDiffSide.Previous rows,
                    GitPagedDiffDisplay.textWidth PagedDiffSide.Current rows,
                    GitPagedDiffDisplay.numberColumnWidth rows,
                    evictedIds
                ),
                [| box parts |]
            )

        // The row after the parts shows the pending request, so it follows the pending record
        // on each render while the rows of the parts stay as they are.
        let rows, rowParts =
            if hasMore then
                Array.append partRowsOnly [|
                    {
                        Key = "continue"
                        Content = GitPagedDiffDisplay.Continue pending
                    }
                |],
                Array.append partRowParts [| -1 |]
            elif pending.IsSome then
                Array.append partRowsOnly [|
                    {
                        Key = "pending"
                        Content = GitPagedDiffDisplay.Continue pending
                    }
                |],
                Array.append partRowParts [| -1 |]
            else
                partRowsOnly, partRowParts

        let noChanges =
            progress |> Option.exists (fun value -> value.ScanComplete)
            && outputComplete
            && not (GitPagedDiffDisplay.hasChanges parts)

        let changeBadge =
            match changeKind with
            | Some GitDiffChangeKind.Added -> "Added"
            | Some GitDiffChangeKind.Deleted -> "Deleted"
            | Some GitDiffChangeKind.Modified -> "Changed"
            | None -> GitPagedDiffDisplay.changeBadge parts noChanges

        let content =
            match status with
            | PagedDiffStatus.Opening ->
                GitPagedDiffViewer.ProgressState(prefix, GitPagedDiffDisplay.statusName status, progress, pending)
            | PagedDiffStatus.Scanning ->
                GitPagedDiffViewer.ProgressState(prefix, GitPagedDiffDisplay.statusName status, progress, pending)
            | PagedDiffStatus.Reopening when parts.Length = 0 ->
                GitPagedDiffViewer.ProgressState(prefix, GitPagedDiffDisplay.statusName status, progress, pending)
            | PagedDiffStatus.EncodingChoice(side, candidates) ->
                let sideTitle =
                    match side with
                    | PagedDiffSide.Previous -> previousTitle
                    | PagedDiffSide.Current -> currentTitle

                GitPagedDiffViewer.EncodingState(prefix, side, sideTitle, candidates, chooseEncoding)
            | PagedDiffStatus.Blocked(side, reason) ->
                let sideText =
                    side
                    |> Option.map (
                        function
                        | PagedDiffSide.Previous -> "Previous"
                        | PagedDiffSide.Current -> "Current"
                    )

                let detail =
                    sideText
                    |> Option.map (fun value -> $"{value}: {reason}")
                    |> Option.orElse (Some reason)

                GitPagedDiffViewer.MessageState(prefix, "blocked", "Content is blocked", detail)
            | PagedDiffStatus.SourceChanged ->
                GitPagedDiffViewer.MessageState(prefix, "source-changed", "The source changed", None)
            | PagedDiffStatus.WorkerFailed message ->
                GitPagedDiffViewer.MessageState(prefix, "worker-failed", "The diff worker failed", Some message)
            | PagedDiffStatus.Failed message ->
                GitPagedDiffViewer.MessageState(prefix, "failed", "The diff could not be opened", Some message)
            | PagedDiffStatus.Ready
            | PagedDiffStatus.LoadingNext
            | PagedDiffStatus.Reopening ->
                if noChanges && status <> PagedDiffStatus.Reopening then
                    Html.div [
                        prop.testId $"{prefix}-no-changes"
                        prop.className
                            "swt:flex swt:min-h-0 swt:flex-1 swt:items-center swt:justify-center swt:p-8 swt:text-sm swt:text-base-content/60"
                        prop.text "No changes"
                    ]
                else
                    // Both statuses render the same two children, so the grid keeps its scroll
                    // position when a reopen starts or ends.
                    React.Fragment [
                        if status = PagedDiffStatus.Reopening then
                            GitPagedDiffViewer.ReopeningBar(prefix, progress)
                        else
                            Html.none
                        GitPagedDiffViewer.Grid(
                            rows,
                            rowParts,
                            evictedIds,
                            previousText,
                            currentText,
                            numberColumn,
                            previousTitle,
                            currentTitle,
                            prefix,
                            nextPageKey |> Option.orElse lastRowKey,
                            hasMore,
                            defaultArg indexing false,
                            defaultArg hideIndexingStatus false,
                            defaultArg nextFailed false,
                            defaultArg maxScrollHeight GitPagedDiffDisplay.DefaultMaxScrollHeightPx,
                            progress,
                            status,
                            defaultArg expandingGaps [||],
                            requestExpand,
                            requestLineSlice,
                            requestLineBefore,
                            defaultArg pendingLineSlices [||],
                            defaultArg pendingReplays [||],
                            requestReplay,
                            requestNext,
                            defaultArg failedGaps [||],
                            defaultArg failedLineSlices [||],
                            defaultArg failedReplays [||],
                            scrollTarget,
                            failureNote,
                            endNote
                        )
                    ]

        GitComparisonView.PanelShell
            (React.Fragment [
                GitComparisonView.HeaderRow
                    (GitComparisonView.TitleStack
                        (Html.h3 [
                            prop.className "swt:text-sm swt:font-semibold"
                            prop.text "Git Diff"
                        ])
                        None
                        None)
                    (Html.span [
                        prop.className "swt:badge swt:badge-outline swt:badge-sm"
                        prop.text changeBadge
                    ])
                    (Some "swt:border-b swt:border-base-content/10 swt:bg-base-100")
                content
            ])
            (Some $"{prefix}-root")
            (Some "swt:flex swt:h-full swt:w-full swt:min-h-0 swt:min-w-0 swt:flex-col")
            None
