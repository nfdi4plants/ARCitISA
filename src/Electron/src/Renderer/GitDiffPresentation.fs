/// Maps the paged text diff DTOs to the presentation types of the Components viewer.
/// The Components package knows nothing about the IPC DTOs, so the renderer does this step.
module Renderer.GitDiffPresentation

open System
open Arcitisa.Electron.Shared.VersionControlTypes
open Arcitisa.Components.Page.GitComparison.GitPagedDiffTypes

/// The library sends every int64 as a decimal string. A float holds it exactly up to 2^53.
let number (value: string) : float =
    match Double.TryParse value with
    | true, parsed -> parsed
    | _ -> 0.0

/// The decimal string a request expects for a number that came from `number`.
let decimalText (value: float) : string = sprintf "%.0f" value

let side (value: DiffSideDto) : PagedDiffSide =
    match value with
    | DiffSideDto.Previous -> PagedDiffSide.Previous
    | DiffSideDto.Current -> PagedDiffSide.Current

let sideDto (value: PagedDiffSide) : DiffSideDto =
    match value with
    | PagedDiffSide.Previous -> DiffSideDto.Previous
    | PagedDiffSide.Current -> DiffSideDto.Current

let private ending (value: LineEndingDto) : PagedLineEnding =
    match value with
    | LineEndingDto.NoEnding -> PagedLineEnding.NoEnding
    | LineEndingDto.LF -> PagedLineEnding.LF
    | LineEndingDto.CRLF -> PagedLineEnding.CRLF
    | LineEndingDto.CR -> PagedLineEnding.CR

let private highlight (value: HighlightDto) : PagedHighlight = {
    Start = value.Start
    Length = value.Length
    Changed = value.Kind = HighlightKindDto.ChangedText
}

let line (value: DiffLineDto) : PagedLine = {
    Number = number value.Number
    Ending = ending value.Ending
    OffsetUtf16 = number value.Slice.OffsetUtf16
    TotalUtf16 = value.Slice.TotalUtf16 |> Option.map number
    Text = value.Slice.Text
    Highlights = value.Slice.Highlights |> Array.map highlight
}

let private rowKind (value: DiffRowKindDto) : PagedRowKind =
    match value with
    | DiffRowKindDto.Context -> PagedRowKind.Context
    | DiffRowKindDto.Added -> PagedRowKind.Added
    | DiffRowKindDto.Removed -> PagedRowKind.Removed
    | DiffRowKindDto.Replaced -> PagedRowKind.Replaced
    | DiffRowKindDto.EndingChanged -> PagedRowKind.EndingChanged

let row (value: DiffRowDto) : PagedRow = {
    Id = value.Id
    Kind = rowKind value.Kind
    Previous = value.Previous |> Option.map line
    Current = value.Current |> Option.map line
}

let private range (value: LineRangeDto) : PagedRange = {
    Start = number value.Start
    Count = number value.Count
}

let part (value: DiffPartDto) : PagedPart =
    match value with
    | DiffPartDto.Hunk fragment ->
        match fragment.Body with
        | HunkBodyDto.AlignedRows rows ->
            PagedPart.HunkRows(
                fragment.HunkId,
                range fragment.PreviousRange,
                range fragment.CurrentRange,
                fragment.StartsHunk,
                fragment.EndsHunk,
                rows |> Array.map row
            )
        | HunkBodyDto.UnalignedSides(previous, current) ->
            PagedPart.UnalignedRegion(
                fragment.HunkId,
                previous |> Array.map line,
                current |> Array.map line,
                range fragment.PreviousRange,
                range fragment.CurrentRange
            )
    | DiffPartDto.HiddenEqual gap -> PagedPart.HiddenGap(gap.GapId, range gap.PreviousRange, range gap.CurrentRange)
    | DiffPartDto.ExpandedContext(gapId, rows) -> PagedPart.ExpandedRows(gapId, rows |> Array.map row)

let progress (value: ScanProgressDto) : PagedProgress = {
    ValidatedBytes = number value.ValidatedBytes
    TotalBytes = number value.TotalBytes
    ScanComplete = value.ScanComplete
}

let private snippetEnd (value: SnippetEndDto) : PagedSnippetEnd =
    match value with
    | SnippetEndDto.Truncated -> PagedSnippetEnd.Truncated
    | SnippetEndDto.MoreTextPending -> PagedSnippetEnd.MoreTextPending
    | SnippetEndDto.LineEnd -> PagedSnippetEnd.LineEnd
    | SnippetEndDto.EndOfFile -> PagedSnippetEnd.EndOfFile

let private pendingSide (value: PendingSideDto) : PagedPendingSide =
    match value with
    | PendingSideDto.NoActiveLine -> PagedPendingSide.NoActiveLine
    | PendingSideDto.Snippet snippet ->
        PagedPendingSide.Snippet(number snippet.Line, number snippet.OffsetUtf16, snippet.Text, snippetEnd snippet.End)
    | PendingSideDto.Exhausted lineCount -> PagedPendingSide.Exhausted(number lineCount)

let pending (value: PendingPreviewDto) : PagedPending = {
    Previous = pendingSide value.Previous
    Current = pendingSide value.Current
    Mismatch =
        value.Mismatch
        |> Option.map (fun marker -> number marker.PreviousOffsetUtf16, number marker.CurrentOffsetUtf16)
}

let blockReasonText (reason: Renderer.Types.GitDiffBlockReason) =
    match reason with
    | Renderer.Types.GitDiffBlockReason.Binary evidence -> $"The content is binary ({evidence})."
    | Renderer.Types.GitDiffBlockReason.LocalContentUnavailable(Some objectId) ->
        $"The content is not available locally (object {objectId})."
    | Renderer.Types.GitDiffBlockReason.LocalContentUnavailable None -> "The content is not available locally."
    | Renderer.Types.GitDiffBlockReason.NotRegularFile -> "The path is not a regular file."
    | Renderer.Types.GitDiffBlockReason.ProviderUnsupported -> "The version control provider cannot compare this file."
    | Renderer.Types.GitDiffBlockReason.NotText evidence -> $"The content is not text ({evidence})."
    | Renderer.Types.GitDiffBlockReason.TooLargeForMemory(blobBytes, limitBytes, cause) ->
        let sizes =
            $"The committed file is {formatBytes blobBytes} and the memory budget allows {formatBytes limitBytes} for it."

        match cause with
        | MemoryCauseDto.BySetting ->
            $"{sizes} The indexing limit setting keeps the diff in memory. Raise the indexing limit to 64 or more to use temp files."
        | MemoryCauseDto.ByLowSpace ->
            $"{sizes} The temp drive is low on free space, so the diff is kept in memory. Free space on the temp drive."
    | Renderer.Types.GitDiffBlockReason.TempSpaceLow ->
        "The temp drive is low on free space. Free space on the temp drive, then open the diff again."
    | Renderer.Types.GitDiffBlockReason.MemoryBudgetReached ->
        "The memory budget of the diff is used up. Raise the indexing limit to 64 or more to use temp files, or free space on the temp drive, then open the diff again."

let status (value: Renderer.Types.GitDiffPageStatus) : PagedDiffStatus =
    match value with
    | Renderer.Types.GitDiffPageStatus.Opening -> PagedDiffStatus.Opening
    | Renderer.Types.GitDiffPageStatus.Scanning -> PagedDiffStatus.Scanning
    | Renderer.Types.GitDiffPageStatus.EncodingChoice(diffSide, _, candidates) ->
        PagedDiffStatus.EncodingChoice(
            side diffSide,
            candidates
            |> Array.map (fun candidate -> {
                Encoding = candidate.Encoding
                Preview = candidate.Preview
            })
        )
    | Renderer.Types.GitDiffPageStatus.Ready -> PagedDiffStatus.Ready
    | Renderer.Types.GitDiffPageStatus.LoadingNext -> PagedDiffStatus.LoadingNext
    | Renderer.Types.GitDiffPageStatus.Reopening _ -> PagedDiffStatus.Reopening
    | Renderer.Types.GitDiffPageStatus.Blocked(diffSide, reason) ->
        PagedDiffStatus.Blocked(diffSide |> Option.map side, blockReasonText reason)
    | Renderer.Types.GitDiffPageStatus.SourceChanged -> PagedDiffStatus.SourceChanged
    | Renderer.Types.GitDiffPageStatus.WorkerFailed message -> PagedDiffStatus.WorkerFailed message
    | Renderer.Types.GitDiffPageStatus.Failed message -> PagedDiffStatus.Failed message

/// The path of the side, with the short revision when the side comes from a commit.
let sourceTitle (info: DiffSourceInfoDto) =
    match info.Revision with
    | Some revision when revision.Length > 0 -> $"{info.Path} @ {revision.Substring(0, min 7 revision.Length)}"
    | _ -> info.Path

/// Puts the part of the slice before the displayed start in front of the displayed text. The
/// highlights of the displayed text move by the length of the prepended text. A slice that
/// starts at or after the displayed start adds nothing, and so does a slice that ends before it,
/// since prepending it would leave a hole in the text.
let private prependSlice (displayed: PagedLine) (slice: PagedLine) : PagedLine =
    let sliceEnd = slice.OffsetUtf16 + float slice.Text.Length

    if slice.OffsetUtf16 >= displayed.OffsetUtf16 || sliceEnd < displayed.OffsetUtf16 then
        displayed
    else
        let prefixLength = int (displayed.OffsetUtf16 - slice.OffsetUtf16)

        let prefixHighlights =
            slice.Highlights
            |> Array.choose (fun highlight ->
                let length = min (highlight.Start + highlight.Length) prefixLength - highlight.Start

                if length > 0 then
                    Some { highlight with Length = length }
                else
                    None
            )

        let shifted =
            displayed.Highlights
            |> Array.map (fun highlight -> {
                highlight with
                    Start = highlight.Start + prefixLength
            })

        {
            displayed with
                OffsetUtf16 = slice.OffsetUtf16
                Text = slice.Text.Substring(0, prefixLength) + displayed.Text
                TotalUtf16 = slice.TotalUtf16 |> Option.orElse displayed.TotalUtf16
                Highlights = Array.append prefixHighlights shifted
        }

/// Merges a slice by its position in the line. The displayed text stays. The part of the slice
/// before the displayed start goes in front of it, and the part beyond the displayed end is
/// appended. A slice that ends within the displayed text adds nothing at the end, and a slice
/// that starts after the displayed end is dropped, since appending it would leave a hole in the
/// text.
let mergeSlice (displayed: PagedLine) (slice: PagedLine) : PagedLine =
    let displayed = prependSlice displayed slice

    let displayedEnd = displayed.OffsetUtf16 + float displayed.Text.Length
    let sliceEnd = slice.OffsetUtf16 + float slice.Text.Length

    if slice.OffsetUtf16 > displayedEnd || sliceEnd <= displayedEnd then
        displayed
    else
        // Number of slice characters the displayed text already holds.
        let covered = int (displayedEnd - slice.OffsetUtf16)
        let shift = displayed.Text.Length - covered

        let appended =
            slice.Highlights
            |> Array.choose (fun highlight ->
                let start = max highlight.Start covered
                let length = highlight.Start + highlight.Length - start

                if length > 0 then
                    Some {
                        highlight with
                            Start = start + shift
                            Length = length
                    }
                else
                    None
            )

        {
            displayed with
                Text = displayed.Text + slice.Text.Substring covered
                TotalUtf16 = slice.TotalUtf16 |> Option.orElse displayed.TotalUtf16
                Highlights = Array.append displayed.Highlights appended
        }

let private mergeInLine (lineNumber: float) (slice: PagedLine) (current: PagedLine option) =
    match current with
    | Some displayed when displayed.Number = lineNumber -> Some(mergeSlice displayed slice), true
    | other -> other, false

let private mergeInRow (diffSide: PagedDiffSide) (slice: PagedLine) (row: PagedRow) =
    match diffSide with
    | PagedDiffSide.Previous ->
        let merged, changed = mergeInLine slice.Number slice row.Previous
        (if changed then { row with Previous = merged } else row), changed
    | PagedDiffSide.Current ->
        let merged, changed = mergeInLine slice.Number slice row.Current
        (if changed then { row with Current = merged } else row), changed

let private mergeInRows diffSide slice (rows: PagedRow[]) =
    let merged = rows |> Array.map (mergeInRow diffSide slice)

    if merged |> Array.exists snd then
        Some(merged |> Array.map fst)
    else
        None

let private mergeInLines (slice: PagedLine) (lines: PagedLine[]) =
    if lines |> Array.exists (fun displayed -> displayed.Number = slice.Number) then
        Some(
            lines
            |> Array.map (fun displayed ->
                if displayed.Number = slice.Number then
                    mergeSlice displayed slice
                else
                    displayed
            )
        )
    else
        None

/// Merges a line slice into the part that shows that line. None when the part does not show it,
/// so untouched parts keep their identity.
let mergeSliceInPart (diffSide: PagedDiffSide) (slice: PagedLine) (value: PagedPart) : PagedPart option =
    match value with
    | PagedPart.HunkRows(hunkId, previous, current, startsHunk, endsHunk, rows) ->
        mergeInRows diffSide slice rows
        |> Option.map (fun merged -> PagedPart.HunkRows(hunkId, previous, current, startsHunk, endsHunk, merged))
    | PagedPart.ExpandedRows(gapId, rows) ->
        mergeInRows diffSide slice rows
        |> Option.map (fun merged -> PagedPart.ExpandedRows(gapId, merged))
    | PagedPart.UnalignedRegion(hunkId, previous, current, previousRange, currentRange) ->
        match diffSide with
        | PagedDiffSide.Previous ->
            mergeInLines slice previous
            |> Option.map (fun merged ->
                PagedPart.UnalignedRegion(hunkId, merged, current, previousRange, currentRange)
            )
        | PagedDiffSide.Current ->
            mergeInLines slice current
            |> Option.map (fun merged ->
                PagedPart.UnalignedRegion(hunkId, previous, merged, previousRange, currentRange)
            )
    | PagedPart.HiddenGap _
    | PagedPart.EvictedPage _ -> None
