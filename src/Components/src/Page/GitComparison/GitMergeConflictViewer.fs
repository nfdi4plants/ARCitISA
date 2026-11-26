namespace Arcitisa.Components.Page

open System.Globalization
open Browser.Dom
open Fable.Core
open Feliz

module private GitMergeConflictViewerInternal =

    type ConflictResolutionChoice =
        | Incoming
        | Current

    type UndoAvailability =
        | UndoAvailable
        | UndoUnavailable of string

    type ConflictResolutionState = {
        Choice: ConflictResolutionChoice
        CurrentText: string
        StartIndex: int
        UndoAvailability: UndoAvailability
    }

    type ConflictPaneState =
        | Unresolved of GitTextComparisonCore.MergeConflictBlock
        | Resolved of GitTextComparisonCore.MergeConflictBlock * ConflictResolutionState

    type ConflictPaneEntry = {
        EntryId: int
        State: ConflictPaneState
    }

    type ConflictPaneSession = {
        ResolvedText: string
        PaneEntries: ConflictPaneEntry list
        NextEntryId: int
    }

    type SelectionSnapshot = { StartIndex: int; EndIndex: int }

    type TextEdit = {
        StartIndex: int
        PreviousEndIndex: int
        NextEndIndex: int
        Delta: int
    }

    type TextChange =
        | NoTextChange
        | DocumentReplacement
        | IncrementalEdit of TextEdit

    let parseConflictBlocks (content: string) =
        GitTextComparisonCore.MergeConflicts.parseMergeConflictFragments content
        |> List.choose (
            function
            | GitTextComparisonCore.ConflictBlock block -> Some block
            | GitTextComparisonCore.PlainText _ -> None
        )

    let parsePaneEntries (content: string) =
        parseConflictBlocks content
        |> List.mapi (fun index block -> {
            EntryId = index + 1
            State = Unresolved block
        })

    let private nextEntryIdFromEntries (entries: ConflictPaneEntry list) =
        entries |> List.fold (fun current entry -> max current (entry.EntryId + 1)) 1

    let createSessionState (content: string) =
        let paneEntries = parsePaneEntries content

        {
            ResolvedText = content
            PaneEntries = paneEntries
            NextEntryId = nextEntryIdFromEntries paneEntries
        }

    let countUnresolvedEntries (entries: ConflictPaneEntry list) =
        entries
        |> List.sumBy (fun entry ->
            match entry.State with
            | Unresolved _ -> 1
            | Resolved _ -> 0
        )

    let createResolutionState choice startIndex resolvedText = {
        Choice = choice
        CurrentText = resolvedText
        StartIndex = startIndex
        UndoAvailability = UndoAvailable
    }

    let canUndoResolution (resolution: ConflictResolutionState) =
        match resolution.UndoAvailability with
        | UndoAvailable -> true
        | UndoUnavailable _ -> false

    let tryGetUndoUnavailableMessage (resolution: ConflictResolutionState) =
        match resolution.UndoAvailability with
        | UndoAvailable -> None
        | UndoUnavailable message -> Some message

    let private tryDescribeTextEdit (previousText: string) (nextText: string) =
        if previousText = nextText then
            None
        else
            let maxPrefixLength = min previousText.Length nextText.Length
            let mutable prefixLength = 0

            while prefixLength < maxPrefixLength
                  && previousText.[prefixLength] = nextText.[prefixLength] do
                prefixLength <- prefixLength + 1

            let maxSuffixLength =
                min (previousText.Length - prefixLength) (nextText.Length - prefixLength)

            let mutable suffixLength = 0

            while suffixLength < maxSuffixLength
                  && previousText.[previousText.Length - 1 - suffixLength] = nextText.[nextText.Length
                                                                                       - 1
                                                                                       - suffixLength] do
                suffixLength <- suffixLength + 1

            Some {
                StartIndex = prefixLength
                PreviousEndIndex = previousText.Length - suffixLength
                NextEndIndex = nextText.Length - suffixLength
                Delta = nextText.Length - previousText.Length
            }

    let private describeTextChange
        (previousText: string)
        (nextText: string)
        (selectionSnapshot: SelectionSnapshot option)
        =
        match tryDescribeTextEdit previousText nextText with
        | None -> NoTextChange
        | Some edit ->
            let unchangedPrefix = previousText.Substring(0, edit.StartIndex)
            let unchangedSuffix = previousText.Substring(edit.PreviousEndIndex)

            let unchangedTextIsTrivial (text: string) =
                text |> Seq.forall (fun character -> character = '\n')

            let selectedWholeDocument =
                selectionSnapshot
                |> Option.exists (fun selection -> selection.StartIndex = 0 && selection.EndIndex = previousText.Length)

            if
                selectedWholeDocument
                || (unchangedTextIsTrivial unchangedPrefix && unchangedTextIsTrivial unchangedSuffix)
            then
                DocumentReplacement
            else
                IncrementalEdit edit

    let private trySliceExclusive (content: string) (startIndex: int) (endIndexExclusive: int) =
        if
            startIndex < 0
            || endIndexExclusive < startIndex
            || endIndexExclusive > content.Length
        then
            None
        else
            Some(content.Substring(startIndex, endIndexExclusive - startIndex))

    let private mapBoundaryThroughEdit boundaryIndex replacementBoundaryIndex (edit: TextEdit) =
        if boundaryIndex <= edit.StartIndex then
            boundaryIndex
        elif boundaryIndex >= edit.PreviousEndIndex then
            boundaryIndex + edit.Delta
        else
            replacementBoundaryIndex

    let private boundaryEditUndoUnavailableMessage =
        "Undo is unavailable because a manual edit crossed this resolved conflict boundary."

    let private trackingUndoUnavailableMessage =
        "Undo is unavailable because the resolved conflict can no longer be tracked after manual edits."

    let private reconcileResolutionAfterTextEdit
        (nextText: string)
        (edit: TextEdit)
        (resolution: ConflictResolutionState)
        =
        let spanStart = resolution.StartIndex
        let spanEnd = resolution.StartIndex + resolution.CurrentText.Length
        let overlapsSpan = edit.StartIndex < spanEnd && edit.PreviousEndIndex > spanStart

        let editIsInsideSpan =
            edit.StartIndex >= spanStart && edit.PreviousEndIndex <= spanEnd

        let nextSpanStart = mapBoundaryThroughEdit spanStart edit.StartIndex edit
        let nextSpanEnd = mapBoundaryThroughEdit spanEnd edit.NextEndIndex edit

        match trySliceExclusive nextText nextSpanStart nextSpanEnd with
        | Some nextTrackedText ->
            let nextUndoAvailability =
                match resolution.UndoAvailability with
                | UndoUnavailable message -> UndoUnavailable message
                | UndoAvailable ->
                    if not overlapsSpan || editIsInsideSpan then
                        UndoAvailable
                    else
                        UndoUnavailable boundaryEditUndoUnavailableMessage

            {
                resolution with
                    CurrentText = nextTrackedText
                    StartIndex = nextSpanStart
                    UndoAvailability = nextUndoAvailability
            }
        | None ->
            let fallbackStartIndex = nextSpanStart |> max 0 |> min nextText.Length

            let nextUndoAvailability =
                match resolution.UndoAvailability with
                | UndoUnavailable message -> UndoUnavailable message
                | UndoAvailable -> UndoUnavailable trackingUndoUnavailableMessage

            {
                resolution with
                    StartIndex = fallbackStartIndex
                    UndoAvailability = nextUndoAvailability
            }

    let shiftResolvedEntryOffsetsAfter cutoffIndex delta (entries: ConflictPaneEntry list) =
        if delta = 0 then
            entries
        else
            entries
            |> List.map (fun entry ->
                match entry.State with
                | Resolved(block, resolution) when resolution.StartIndex > cutoffIndex -> {
                    entry with
                        State =
                            Resolved(
                                block,
                                {
                                    resolution with
                                        StartIndex = resolution.StartIndex + delta
                                }
                            )
                  }
                | _ -> entry
            )

    let private entryStartIndex (entry: ConflictPaneEntry) =
        match entry.State with
        | Unresolved block -> block.StartIndex
        | Resolved(_, resolution) -> resolution.StartIndex

    let normalizeSessionEntries (content: string) (nextEntryId: int) (entries: ConflictPaneEntry list) =
        let unresolvedCandidateIdsRev, resolvedEntriesRev =
            entries
            |> List.fold
                (fun (candidateIdsRev, resolvedEntriesRev) entry ->
                    match entry.State with
                    | Unresolved _ -> entry.EntryId :: candidateIdsRev, resolvedEntriesRev
                    | Resolved(block, resolution) when resolution.CurrentText = block.RawContent ->
                        entry.EntryId :: candidateIdsRev, resolvedEntriesRev
                    | Resolved _ -> candidateIdsRev, entry :: resolvedEntriesRev
                )
                ([], [])

        let unresolvedCandidateIds = unresolvedCandidateIdsRev |> List.rev |> List.toArray
        let unresolvedBlocks = parseConflictBlocks content
        let mutable unresolvedIndex = 0
        let mutable currentNextEntryId = nextEntryId

        let unresolvedEntries =
            unresolvedBlocks
            |> List.map (fun block ->
                let entryId =
                    if unresolvedIndex < unresolvedCandidateIds.Length then
                        let reusedEntryId = unresolvedCandidateIds.[unresolvedIndex]
                        unresolvedIndex <- unresolvedIndex + 1
                        reusedEntryId
                    else
                        let newEntryId = currentNextEntryId
                        currentNextEntryId <- currentNextEntryId + 1
                        newEntryId

                {
                    EntryId = entryId
                    State = Unresolved block
                }
            )

        {
            ResolvedText = content
            PaneEntries =
                (resolvedEntriesRev |> List.rev) @ unresolvedEntries
                |> List.sortBy (fun entry -> entryStartIndex entry, entry.EntryId)
            NextEntryId = currentNextEntryId
        }

    let reconcileSessionResolvedText
        (content: string)
        (selectionSnapshot: SelectionSnapshot option)
        (session: ConflictPaneSession)
        =
        match describeTextChange session.ResolvedText content selectionSnapshot with
        | NoTextChange -> session
        | DocumentReplacement -> createSessionState content
        | IncrementalEdit textEdit ->
            let nextEntries =
                session.PaneEntries
                |> List.map (fun entry ->
                    match entry.State with
                    | Unresolved _ -> entry
                    | Resolved(block, resolution) ->
                        let nextResolution = reconcileResolutionAfterTextEdit content textEdit resolution

                        if nextResolution.CurrentText = block.RawContent then
                            { entry with State = Unresolved block }
                        else
                            {
                                entry with
                                    State = Resolved(block, nextResolution)
                            }
                )

            normalizeSessionEntries content session.NextEntryId nextEntries

    [<ReactComponent>]
    let VerticalSplitHandle (onPointerDown: Browser.Types.PointerEvent -> unit, testId: string option) =
        Html.div [
            if testId.IsSome then
                prop.testId testId.Value
            prop.className
                "swt:group swt:flex swt:items-center swt:justify-center swt:cursor-row-resize swt:bg-base-200 hover:swt:bg-base-300"
            prop.onPointerDown onPointerDown
            prop.style [ style.custom ("touch-action", "none") ]
            prop.children [
                Html.div [
                    prop.className
                        "swt:h-1 swt:w-20 swt:rounded-full swt:bg-base-content/20 group-hover:swt:bg-base-content/35"
                ]
            ]
        ]

    [<ReactComponent>]
    let VerticalSplitPane (topPane: ReactElement, bottomPane: ReactElement, splitHandleTestId: string option) =
        let splitPaneRef = React.useElementRef ()
        let splitPercent, setSplitPercent = React.useState 50
        let splitPercentRef = React.useRef 50
        let isDragging = React.useRef false

        let commitSplitPercent nextSplitPercent =
            if splitPercentRef.current <> nextSplitPercent then
                splitPercentRef.current <- nextSplitPercent
                setSplitPercent nextSplitPercent

        let updateSplitPercentFromClientY (clientY: float) =
            match splitPaneRef.current with
            | Some container ->
                let rect = container.getBoundingClientRect ()
                let relativeY = clientY - rect.top

                let rawPercent =
                    if rect.height <= 0. then
                        float splitPercentRef.current
                    else
                        (relativeY / rect.height) * 100.

                let clampedPercent = rawPercent |> max 25. |> min 75. |> round |> int

                commitSplitPercent clampedPercent
            | None -> ()

        React.useEffectOnce (fun () ->
            let onMove =
                fun (moveEvent: Browser.Types.PointerEvent) ->
                    if isDragging.current then
                        updateSplitPercentFromClientY moveEvent.clientY
                    else
                        ()

            let stopDragging =
                fun (_: Browser.Types.PointerEvent) -> isDragging.current <- false

            document.addEventListener ("pointermove", unbox onMove)
            document.addEventListener ("pointerup", unbox stopDragging)
            document.addEventListener ("pointercancel", unbox stopDragging)

            FsReact.createDisposable (fun () ->
                document.removeEventListener ("pointermove", unbox onMove)
                document.removeEventListener ("pointerup", unbox stopDragging)
                document.removeEventListener ("pointercancel", unbox stopDragging)
            )
        )

        let startSplitDrag (event: Browser.Types.PointerEvent) =
            event.preventDefault ()
            isDragging.current <- true
            updateSplitPercentFromClientY event.clientY

        let topFractionText = splitPercent.ToString(CultureInfo.InvariantCulture)
        let bottomFractionText = (100 - splitPercent).ToString(CultureInfo.InvariantCulture)

        Html.div [
            prop.ref splitPaneRef
            prop.className "swt:grid swt:min-h-0 swt:flex-1"
            prop.style [
                style.custom (
                    "grid-template-rows",
                    $"minmax(0, {topFractionText}fr) 0.75rem minmax(0, {bottomFractionText}fr)"
                )
            ]
            prop.children [
                topPane
                VerticalSplitHandle(startSplitDrag, splitHandleTestId)
                bottomPane
            ]
        ]

    [<ReactComponent>]
    let RenderConflictBlock
        (
            entryId: int,
            block: GitTextComparisonCore.MergeConflictBlock,
            incomingTitle: string option,
            currentTitle: string option,
            applyResolution:
                ConflictResolutionChoice -> int -> GitTextComparisonCore.MergeConflictBlock -> string -> unit,
            comparisonScrollTestId: string option
        ) =
        let incomingHeaderLabel, currentHeaderLabel, rows, incomingLineCount, currentLineCount =
            React.useMemo (
                (fun () ->
                    let incomingHeaderLabel =
                        GitTextComparisonCore.Metadata.resolveHeaderLabel "Incoming" incomingTitle block.IncomingLabel

                    let currentHeaderLabel =
                        GitTextComparisonCore.Metadata.resolveHeaderLabel "Current" currentTitle block.CurrentLabel

                    let rows =
                        GitTextComparisonCore.Rows.buildRows block.IncomingContent block.CurrentContent
                        |> List.toArray

                    let incomingLineCount =
                        (GitTextComparisonCore.Text.splitContentToLines block.IncomingContent).Length

                    let currentLineCount =
                        (GitTextComparisonCore.Text.splitContentToLines block.CurrentContent).Length

                    incomingHeaderLabel, currentHeaderLabel, rows, incomingLineCount, currentLineCount
                ),
                [| box block; box incomingTitle; box currentTitle |]
            )

        let header =
            GitComparisonView.HeaderRow
                (GitComparisonView.TitleStack
                    (Html.h4 [
                        prop.className "swt:text-sm swt:font-semibold"
                        prop.text $"Merge conflict {entryId}"
                    ])
                    None
                    None)
                (Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                    prop.children [
                        Html.button [
                            prop.className "swt:btn swt:btn-sm swt:btn-outline"
                            prop.text "Take incoming"
                            prop.onClick (fun _ ->
                                applyResolution ConflictResolutionChoice.Incoming entryId block block.IncomingContent
                            )
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-sm swt:btn-outline"
                            prop.text "Take current"
                            prop.onClick (fun _ ->
                                applyResolution ConflictResolutionChoice.Current entryId block block.CurrentContent
                            )
                        ]
                    ]
                ])
                (Some "swt:border-b swt:border-base-content/10 swt:bg-base-100")

        let body =
            GitTextComparisonRendering.Rendering.ComparisonGrid(
                rows,
                ("Incoming", (incomingHeaderLabel, incomingLineCount)),
                ("Current", (currentHeaderLabel, currentLineCount)),
                None,
                None,
                comparisonScrollTestId,
                Some 260,
                Some GitTextComparisonRendering.Rendering.ChoiceTheme
            )

        GitComparisonView.SectionCard header body (Some $"merge-conflict-{entryId}") (Some "swt:overflow-hidden")

    [<ReactComponent>]
    let ResolvedConflictSummary
        (
            entryId: int,
            block: GitTextComparisonCore.MergeConflictBlock,
            resolution: ConflictResolutionState,
            undoResolution: int -> GitTextComparisonCore.MergeConflictBlock -> ConflictResolutionState -> unit
        ) =
        let resolutionLabel =
            match resolution.Choice with
            | ConflictResolutionChoice.Incoming -> "incoming"
            | ConflictResolutionChoice.Current -> "current"

        let canUndo = canUndoResolution resolution
        let undoUnavailableMessage = tryGetUndoUnavailableMessage resolution

        let header =
            GitComparisonView.HeaderRow
                (GitComparisonView.TitleStack
                    (Html.h4 [
                        prop.className "swt:text-sm swt:font-semibold"
                        prop.text $"Merge conflict {entryId}"
                    ])
                    (Some(
                        Html.p [
                            prop.className "swt:text-xs swt:text-base-content/60"
                            prop.text (
                                match undoUnavailableMessage with
                                | Some message -> $"Resolved with {resolutionLabel}. {message}"
                                | None ->
                                    $"Resolved with {resolutionLabel}. Expand again with Undo if you want to choose differently."
                            )
                        ]
                    ))
                    None)
                (Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                    prop.children [
                        Html.span [
                            prop.className "swt:badge swt:badge-ghost swt:badge-sm"
                            prop.text $"Using {resolutionLabel}"
                        ]
                        if not canUndo then
                            Html.span [
                                prop.className "swt:badge swt:badge-outline swt:badge-sm"
                                prop.text "Undo unavailable"
                            ]
                        Html.button [
                            prop.className "swt:btn swt:btn-sm"
                            prop.disabled (not canUndo)
                            prop.text "Undo"
                            prop.onClick (fun _ -> undoResolution entryId block resolution)
                        ]
                    ]
                ])
                None

        GitComparisonView.SectionCard header Html.none (Some $"merge-conflict-{entryId}") None

[<Erase; Mangle(false)>]
type GitMergeConflictViewer =

    [<ReactComponent>]
    static member Viewer
        (
            mergeConflictContent: string,
            ?resolvedContent: string,
            ?defaultResolvedContent: string,
            ?onResolvedContentChange: (string -> unit),
            ?onConfirmMerge: (string -> unit),
            ?confirmDisabled: bool,
            ?incomingTitle: string,
            ?currentTitle: string,
            ?resolvedTitle: string,
            ?heightPx: int,
            ?minHeightPx: int,
            ?className: string,
            ?testIdPrefix: string
        ) =
        let normalizedConflictContent =
            GitTextComparisonCore.Text.normalizeLineEndings mergeConflictContent

        let normalizedControlledResolvedText =
            resolvedContent |> Option.map GitTextComparisonCore.Text.normalizeLineEndings

        let normalizedDefaultResolvedText =
            defaultResolvedContent
            |> Option.map GitTextComparisonCore.Text.normalizeLineEndings

        let confirmDisabled = defaultArg confirmDisabled false

        match normalizedControlledResolvedText, normalizedDefaultResolvedText, onResolvedContentChange with
        | Some _, Some _, _ ->
            failwith "GitMergeConflictViewer: `resolvedContent` and `defaultResolvedContent` cannot be used together."
        | Some _, None, None -> failwith "GitMergeConflictViewer: `resolvedContent` requires `onResolvedContentChange`."
        | _ -> ()

        let isControlled = normalizedControlledResolvedText.IsSome

        let initialUncontrolledResolvedText =
            normalizedDefaultResolvedText |> Option.defaultValue normalizedConflictContent

        let internalResolvedText, setInternalResolvedText =
            React.useState initialUncontrolledResolvedText

        let lastKnownResolvedSelection =
            React.useRef<GitMergeConflictViewerInternal.SelectionSnapshot option> None

        let initialObservedResolvedText =
            normalizedControlledResolvedText
            |> Option.defaultValue initialUncontrolledResolvedText

        let sessionState, setSessionState =
            React.useStateWithUpdater (GitMergeConflictViewerInternal.createSessionState initialObservedResolvedText)

        let pendingResolvedText = React.useRef<string option> None
        let previousObservedResolvedText = React.useRef initialObservedResolvedText
        let previousConflictContent = React.useRef normalizedConflictContent
        let previousDefaultResolvedText = React.useRef normalizedDefaultResolvedText

        React.useEffect (
            (fun () ->
                if not isControlled then
                    let nextResolvedText =
                        normalizedDefaultResolvedText |> Option.defaultValue normalizedConflictContent

                    setInternalResolvedText nextResolvedText
            ),
            [|
                box normalizedConflictContent
                box normalizedDefaultResolvedText
                box isControlled
            |]
        )

        let observedResolvedText =
            normalizedControlledResolvedText |> Option.defaultValue internalResolvedText

        let resolvedText = sessionState.ResolvedText

        let captureResolvedSelection (textarea: Browser.Types.HTMLTextAreaElement) =
            lastKnownResolvedSelection.current <-
                Some {
                    StartIndex = textarea.selectionStart
                    EndIndex = textarea.selectionEnd
                }

        let trackResolvedSelectionFromTarget (target: obj) =
            let textarea = target :?> Browser.Types.HTMLTextAreaElement
            captureResolvedSelection textarea

        React.useEffect (
            (fun () ->
                let didConflictContentChange =
                    previousConflictContent.current <> normalizedConflictContent

                let didDefaultResolvedTextChange =
                    not isControlled
                    && previousDefaultResolvedText.current <> normalizedDefaultResolvedText

                let previousResolvedText = previousObservedResolvedText.current

                previousConflictContent.current <- normalizedConflictContent
                previousDefaultResolvedText.current <- normalizedDefaultResolvedText
                previousObservedResolvedText.current <- observedResolvedText

                if didConflictContentChange || didDefaultResolvedTextChange then
                    pendingResolvedText.current <- None
                    lastKnownResolvedSelection.current <- None

                    let nextSourceResolvedText =
                        if isControlled then
                            observedResolvedText
                        else
                            normalizedDefaultResolvedText |> Option.defaultValue normalizedConflictContent

                    setSessionState (fun _ -> GitMergeConflictViewerInternal.createSessionState nextSourceResolvedText)
                else
                    match pendingResolvedText.current with
                    | Some expectedResolvedText when expectedResolvedText = observedResolvedText ->
                        pendingResolvedText.current <- None
                    | Some _ when isControlled && observedResolvedText = previousResolvedText -> ()
                    | Some _ ->
                        pendingResolvedText.current <- None

                        setSessionState (fun current ->
                            if current.ResolvedText = observedResolvedText then
                                current
                            else
                                GitMergeConflictViewerInternal.reconcileSessionResolvedText
                                    observedResolvedText
                                    None
                                    current
                        )
                    | None ->
                        setSessionState (fun current ->
                            if current.ResolvedText = observedResolvedText then
                                current
                            else
                                GitMergeConflictViewerInternal.reconcileSessionResolvedText
                                    observedResolvedText
                                    None
                                    current
                        )
            ),
            [|
                box observedResolvedText
                box normalizedConflictContent
                box normalizedDefaultResolvedText
                box isControlled
            |]
        )

        let updateResolvedText nextValue =
            let normalizedValue = GitTextComparisonCore.Text.normalizeLineEndings nextValue

            if isControlled then
                onResolvedContentChange.Value normalizedValue
            else
                setInternalResolvedText normalizedValue

                onResolvedContentChange
                |> Option.iter (fun notifyResolvedTextChanged -> notifyResolvedTextChanged normalizedValue)

        let commitSessionAndResolvedText
            (nextSessionState: GitMergeConflictViewerInternal.ConflictPaneSession)
            (nextResolvedText: string)
            =
            pendingResolvedText.current <- Some nextResolvedText
            setSessionState (fun _ -> nextSessionState)
            updateResolvedText nextResolvedText

        let syncSessionToObservedResolvedText () =
            pendingResolvedText.current <- None

            setSessionState (fun current ->
                if current.ResolvedText = observedResolvedText then
                    current
                else
                    GitMergeConflictViewerInternal.reconcileSessionResolvedText observedResolvedText None current
            )

        let commitNormalizedSession nextResolvedText nextEntries =
            let nextSessionState =
                GitMergeConflictViewerInternal.normalizeSessionEntries
                    nextResolvedText
                    sessionState.NextEntryId
                    nextEntries

            commitSessionAndResolvedText nextSessionState nextResolvedText

        let handleResolvedTextChange (event: Browser.Types.Event) =
            let textarea = event.target :?> Browser.Types.HTMLTextAreaElement

            let normalizedNextValue =
                GitTextComparisonCore.Text.normalizeLineEndings textarea.value

            let selectionBeforeEdit = lastKnownResolvedSelection.current

            if normalizedNextValue <> resolvedText then
                let nextSessionState =
                    GitMergeConflictViewerInternal.reconcileSessionResolvedText
                        normalizedNextValue
                        selectionBeforeEdit
                        sessionState

                commitSessionAndResolvedText nextSessionState normalizedNextValue

            captureResolvedSelection textarea

        let rootTestId = testIdPrefix |> Option.map (fun prefix -> prefix + "-root")

        let resolvedEditorTestId =
            testIdPrefix |> Option.map (fun prefix -> prefix + "-resolved-editor")

        let confirmMergeButtonTestId =
            testIdPrefix |> Option.map (fun prefix -> prefix + "-confirm-merge")

        let conflictsPaneTestId =
            testIdPrefix |> Option.map (fun prefix -> prefix + "-conflicts-pane")

        let splitHandleTestId =
            testIdPrefix |> Option.map (fun prefix -> prefix + "-split-handle")

        let paneEntries = sessionState.PaneEntries

        let applyResolution
            (choice: GitMergeConflictViewerInternal.ConflictResolutionChoice)
            (entryId: int)
            (block: GitTextComparisonCore.MergeConflictBlock)
            (replacementText: string)
            =
            let normalizedReplacementText =
                GitTextComparisonCore.Text.normalizeLineEndings replacementText

            match
                GitTextComparisonCore.MergeConflicts.tryReplaceExactTextAt
                    block.StartIndex
                    block.RawContent
                    normalizedReplacementText
                    resolvedText
            with
            | Some nextResolvedText ->
                let resolution =
                    GitMergeConflictViewerInternal.createResolutionState
                        choice
                        block.StartIndex
                        normalizedReplacementText

                let delta = normalizedReplacementText.Length - block.RawContent.Length

                let nextEntries =
                    paneEntries
                    |> List.map (fun entry ->
                        if entry.EntryId = entryId then
                            {
                                entry with
                                    State = GitMergeConflictViewerInternal.Resolved(block, resolution)
                            }
                        else
                            entry
                    )
                    |> GitMergeConflictViewerInternal.shiftResolvedEntryOffsetsAfter block.StartIndex delta

                commitNormalizedSession nextResolvedText nextEntries
            | None -> syncSessionToObservedResolvedText ()

        let undoResolution
            (entryId: int)
            (block: GitTextComparisonCore.MergeConflictBlock)
            (resolution: GitMergeConflictViewerInternal.ConflictResolutionState)
            =
            match
                GitTextComparisonCore.MergeConflicts.tryReplaceExactTextAt
                    resolution.StartIndex
                    resolution.CurrentText
                    block.RawContent
                    resolvedText
            with
            | Some nextResolvedText ->
                let delta = block.RawContent.Length - resolution.CurrentText.Length

                let nextEntries =
                    paneEntries
                    |> List.map (fun entry ->
                        if entry.EntryId = entryId then
                            {
                                entry with
                                    State = GitMergeConflictViewerInternal.Unresolved block
                            }
                        else
                            entry
                    )
                    |> GitMergeConflictViewerInternal.shiftResolvedEntryOffsetsAfter resolution.StartIndex delta

                commitNormalizedSession nextResolvedText nextEntries
            | None -> syncSessionToObservedResolvedText ()

        let hasRemainingConflictMarkers =
            React.useMemo (
                (fun () -> GitTextComparisonCore.MergeConflicts.containsConflictMarkers resolvedText),
                [| box resolvedText |]
            )

        let unresolvedConflictCount =
            React.useMemo (
                (fun () -> GitMergeConflictViewerInternal.countUnresolvedEntries paneEntries),
                [| box paneEntries |]
            )

        let resolvedLineCount =
            React.useMemo (
                (fun () -> (GitTextComparisonCore.Text.splitContentToLines resolvedText).Length),
                [| box resolvedText |]
            )

        let canConfirmMerge =
            onConfirmMerge.IsSome && not hasRemainingConflictMarkers && not confirmDisabled

        let header =
            GitComparisonView.HeaderRow
                (GitComparisonView.TitleStack
                    (Html.h3 [
                        prop.className "swt:text-sm swt:font-semibold"
                        prop.text "Merge Conflicts"
                    ])
                    None
                    None)
                (Html.span [
                    prop.className "swt:badge swt:badge-outline swt:badge-sm"
                    prop.text $"{unresolvedConflictCount} conflicts"
                ])
                (Some "swt:border-b swt:border-base-content/10 swt:bg-base-100")

        let resolvedEditorHeader =
            GitComparisonView.HeaderRow
                (GitComparisonView.TitleStack
                    (Html.h4 [
                        prop.className "swt:text-sm swt:font-semibold"
                        prop.text (defaultArg resolvedTitle "Resolved file view")
                    ])
                    None
                    (Some "swt:gap-0.5"))
                (Html.span [
                    prop.className "swt:badge swt:badge-ghost swt:badge-sm"
                    prop.text $"{resolvedLineCount} lines"
                ])
                None

        let conflictsPane =
            Html.div [
                if conflictsPaneTestId.IsSome then
                    prop.testId conflictsPaneTestId.Value
                prop.className "swt:min-h-0 swt:border-b swt:border-base-content/10"
                prop.children [
                    if List.isEmpty paneEntries then
                        Html.div [
                            prop.className "swt:px-4 swt:py-3 swt:text-sm swt:text-base-content/70"
                            prop.text "No merge conflict markers were detected in the supplied content."
                        ]
                    else
                        Html.div [
                            prop.className "swt:flex swt:h-full swt:min-h-0 swt:flex-col"
                            prop.children [
                                Html.div [
                                    prop.className
                                        "swt:min-h-0 swt:flex-1 swt:overflow-y-auto swt:scrollbar-fade swt:p-4 swt:space-y-4"
                                    prop.children (
                                        paneEntries
                                        |> List.map (fun entry ->
                                            match entry.State with
                                            | GitMergeConflictViewerInternal.Resolved(block, resolution) ->
                                                GitMergeConflictViewerInternal.ResolvedConflictSummary(
                                                    entry.EntryId,
                                                    block,
                                                    resolution,
                                                    undoResolution
                                                )
                                            | GitMergeConflictViewerInternal.Unresolved block ->
                                                let comparisonScrollTestId =
                                                    testIdPrefix
                                                    |> Option.map (fun prefix ->
                                                        $"{prefix}-conflict-{entry.EntryId}-scroll"
                                                    )

                                                GitMergeConflictViewerInternal.RenderConflictBlock(
                                                    entry.EntryId,
                                                    block,
                                                    incomingTitle,
                                                    currentTitle,
                                                    applyResolution,
                                                    comparisonScrollTestId
                                                )
                                        )
                                    )
                                ]
                            ]
                        ]
                ]
            ]

        let resolvedEditorPane =
            Html.div [
                prop.className "swt:flex swt:min-h-0 swt:flex-col swt:px-4 swt:py-3"
                prop.children [
                    resolvedEditorHeader
                    Html.textarea [
                        if resolvedEditorTestId.IsSome then
                            prop.testId resolvedEditorTestId.Value
                        prop.className
                            "swt:mt-2 swt:min-h-0 swt:flex-1 swt:w-full swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:px-4 swt:py-3 swt:font-mono swt:text-xs swt:leading-6 swt:resize-none swt:focus:outline-hidden"
                        prop.value resolvedText
                        prop.onChange handleResolvedTextChange
                        prop.onSelect (fun (event: Browser.Types.UIEvent) ->
                            trackResolvedSelectionFromTarget event.target
                        )
                        prop.onClick (fun (event: Browser.Types.MouseEvent) ->
                            trackResolvedSelectionFromTarget event.target
                        )
                        prop.onKeyUp (fun (event: Browser.Types.KeyboardEvent) ->
                            trackResolvedSelectionFromTarget event.target
                        )
                    ]
                    Html.div [
                        prop.className "swt:mt-3 swt:flex swt:justify-end"
                        prop.children [
                            Html.button [
                                if confirmMergeButtonTestId.IsSome then
                                    prop.testId confirmMergeButtonTestId.Value
                                prop.className "swt:btn swt:btn-primary"
                                prop.disabled (confirmDisabled || not canConfirmMerge)
                                prop.text "Confirm Merge"
                                prop.onClick (fun _ ->
                                    if canConfirmMerge then
                                        onConfirmMerge |> Option.iter (fun confirm -> confirm resolvedText)
                                )
                            ]
                        ]
                    ]
                ]
            ]

        let splitPane =
            GitMergeConflictViewerInternal.VerticalSplitPane(conflictsPane, resolvedEditorPane, splitHandleTestId)

        GitComparisonView.PanelShell
            (React.Fragment [ header; splitPane ])
            rootTestId
            (Some(
                match className with
                | Some value -> $"swt:flex swt:h-full swt:min-h-0 swt:flex-col {value}"
                | None -> "swt:flex swt:h-full swt:min-h-0 swt:flex-col"
            ))
            (Some [
                style.minHeight 0

                if heightPx.IsSome then
                    style.height heightPx.Value

                if minHeightPx.IsSome then
                    style.minHeight minHeightPx.Value
            ])
