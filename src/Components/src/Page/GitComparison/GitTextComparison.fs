namespace Arcitisa.Components.Page

open Browser.Types
open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.ReactHooks

module internal GitTextComparisonRendering =

    open GitTextComparisonCore

    module Rendering =

        type ComparisonSideStyle = {
            ChangedLineNumberClass: string
            ChangedLineClass: string
            ChangedSegmentClass: string
        }

        type ComparisonTheme = {
            LeftChanged: ComparisonSideStyle
            RightChanged: ComparisonSideStyle
        }

        type private ComparisonGridRowProps = {
            Row: DiffRow
            Index: int
            VirtualStart: int
            MeasureElementRef: VirtualMeasureElementRef
            Theme: ComparisonTheme
            RowTestId: string option
        }

        type private ComparisonVirtualItem = { Key: string; Index: int; Start: int }

        let DiffTheme = {
            LeftChanged = {
                ChangedLineNumberClass = "swt:text-base-content/60 swt:bg-error/8"
                ChangedLineClass = "swt:bg-error/12"
                ChangedSegmentClass = "swt:bg-error/22"
            }
            RightChanged = {
                ChangedLineNumberClass = "swt:text-base-content/60 swt:bg-success/8"
                ChangedLineClass = "swt:bg-success/12"
                ChangedSegmentClass = "swt:bg-success/22"
            }
        }

        let ChoiceTheme = {
            LeftChanged = {
                ChangedLineNumberClass = "swt:text-base-content/60 swt:bg-info/10"
                ChangedLineClass = "swt:bg-info/12"
                ChangedSegmentClass = "swt:bg-info/22"
            }
            RightChanged = {
                ChangedLineNumberClass = "swt:text-base-content/60 swt:bg-secondary/12"
                ChangedLineClass = "swt:bg-secondary/12"
                ChangedSegmentClass = "swt:bg-secondary/20"
            }
        }

        [<ReactComponent>]
        let private ComparisonSegments (segments: InlineSegment list, changedSegmentClass: string) =
            if List.isEmpty segments then
                Html.span [ prop.text " " ]
            else
                segments
                |> List.mapi (fun index segment ->
                    Html.span [
                        prop.key $"segment-{index}-{segment.Kind}"

                        match segment.Kind with
                        | LineChangeKind.Neutral -> ()
                        | _ -> prop.className changedSegmentClass

                        prop.text segment.Text
                    ]
                )
                |> React.Fragment

        [<ReactComponent>]
        let private ComparisonSide (side: SideLine, changedStyle: ComparisonSideStyle) =
            let lineNumberClassName =
                match side.Kind with
                | LineChangeKind.Neutral -> "swt:text-base-content/45 swt:bg-base-100"
                | _ -> changedStyle.ChangedLineNumberClass

            let lineClassName =
                match side.Kind with
                | LineChangeKind.Neutral -> "swt:bg-base-100"
                | _ -> changedStyle.ChangedLineClass

            Html.div [
                prop.className "swt:grid swt:grid-cols-[3.5rem_minmax(0,1fr)] swt:min-w-0"
                prop.children [
                    Html.div [
                        prop.className [
                            "swt:px-3 swt:py-1 swt:text-right swt:text-xs swt:leading-5 swt:select-none swt:border-r swt:border-base-content/10"
                            lineNumberClassName
                        ]
                        prop.text (side.Number |> Option.map string |> Option.defaultValue "")
                    ]
                    Html.div [
                        prop.className [
                            "swt:px-3 swt:py-1 swt:min-w-0 swt:font-mono swt:text-xs swt:leading-5"
                            lineClassName
                        ]
                        prop.style [ style.whitespace.pre; style.overflowWrap.anywhere ]
                        prop.children [
                            ComparisonSegments(side.Segments, changedStyle.ChangedSegmentClass)
                        ]
                    ]
                ]
            ]

        [<ReactComponent>]
        let private ComparisonHeader (sideLabel: string, title: string, lineCount: int, testId: string option) =
            Html.div [
                if testId.IsSome then
                    prop.testId testId.Value
                prop.className
                    "swt:flex swt:items-start swt:justify-between swt:gap-3 swt:px-4 swt:py-3 swt:bg-base-200 swt:border-b swt:border-base-content/10"
                prop.children [
                    Html.div [
                        prop.className "swt:min-w-0 swt:flex swt:flex-col swt:gap-0.5"
                        prop.children [
                            Html.span [
                                prop.className
                                    "swt:text-[11px] swt:uppercase swt:tracking-wide swt:text-base-content/60"
                                prop.text sideLabel
                            ]
                            Html.span [
                                prop.className "swt:truncate swt:text-sm swt:font-semibold"
                                prop.text title
                            ]
                        ]
                    ]
                    Html.span [
                        prop.className "swt:badge swt:badge-ghost swt:badge-sm swt:shrink-0"
                        prop.text $"{lineCount} lines"
                    ]
                ]
            ]

        [<ReactComponent>]
        let private ComparisonGridRow (props: ComparisonGridRowProps) =
            Html.div [
                if props.RowTestId.IsSome then
                    prop.testId props.RowTestId.Value
                prop.custom ("data-index", props.Index)
                prop.ref (fun element -> props.MeasureElementRef(Option.ofObj element))
                prop.className
                    "swt:absolute swt:left-0 swt:grid swt:w-full swt:min-w-232 swt:grid-cols-2 swt:divide-x swt:divide-base-content/10"
                prop.style [
                    style.top 0
                    style.left 0
                    style.custom ("transform", $"translateY({props.VirtualStart}px)")
                ]
                prop.children [
                    ComparisonSide(props.Row.Left, props.Theme.LeftChanged)
                    ComparisonSide(props.Row.Right, props.Theme.RightChanged)
                ]
            ]

        [<ReactMemoComponent(AreEqualFn.FsEquals)>]
        let ComparisonGrid
            (
                rows: DiffRow[],
                leftHeader: string * (string * int),
                rightHeader: string * (string * int),
                leftHeaderTestId: string option,
                rightHeaderTestId: string option,
                scrollTestId: string option,
                maxHeightPx: int option,
                theme: ComparisonTheme option
            ) =
            let rows = if rows.Length = 0 then [| Rows.emptyRow () |] else rows
            let theme = defaultArg theme DiffTheme
            let rowEstimatePx = 28
            let overscan = 8
            let headerScrollRef: IRefValue<HTMLElement option> = React.useElementRef ()
            let bodyScrollRef: IRefValue<HTMLElement option> = React.useElementRef ()
            let bodyContentRef: IRefValue<HTMLElement option> = React.useElementRef ()
            let contentMeasureRef, contentRect = React.useMeasure<Element> ()

            React.useEffect (
                (fun () ->
                    contentMeasureRef (bodyContentRef.current |> Option.map unbox<Element>)

                    FsReact.createDisposable (fun () -> contentMeasureRef None)
                ),
                [| box contentMeasureRef |]
            )

            React.useEffect (
                (fun () ->
                    match headerScrollRef.current, bodyScrollRef.current with
                    | Some headerScroll, Some bodyScroll ->
                        let syncHeaderToBody (_: Event) =
                            headerScroll.scrollLeft <- bodyScroll.scrollLeft

                        bodyScroll.addEventListener ("scroll", syncHeaderToBody)

                        syncHeaderToBody (unbox null)

                        FsReact.createDisposable (fun () ->
                            bodyScroll.removeEventListener ("scroll", syncHeaderToBody)
                        )
                    | _ -> FsReact.createDisposable (fun () -> ())
                ),
                [||]
            )

            let comparisonContentWidthPx =
                contentRect.width |> Option.map int |> Option.defaultValue 0

            let headerContentWidth =
                if comparisonContentWidthPx > 0 then
                    $"max(58rem, {comparisonContentWidthPx}px)"
                else
                    "max(58rem, 100%)"

            let rowVirtualizer =
                Virtual.useVirtualizer (
                    count = rows.Length,
                    getScrollElement = (fun () -> bodyScrollRef.current),
                    estimateSize = (fun _ -> rowEstimatePx),
                    overscan = overscan,
                    gap = 0
                )

            let virtualItems =
                let measuredItems = rowVirtualizer.getVirtualItems ()

                if measuredItems.Length = 0 && rows.Length > 0 then
                    let isInitialScrollPosition =
                        bodyScrollRef.current
                        |> Option.map (fun element -> element.scrollTop = 0.0)
                        |> Option.defaultValue true

                    if not isInitialScrollPosition then
                        [||]
                    else
                        [| 0 .. min (rows.Length - 1) overscan |]
                        |> Array.map (fun index -> {
                            Key = $"comparison-row-{index}"
                            Index = index
                            Start = index * rowEstimatePx
                        })
                else
                    measuredItems
                    |> Array.map (fun item -> {
                        Key = item.key
                        Index = item.index
                        Start = item.start
                    })

            let virtualContentTestId =
                scrollTestId |> Option.map (fun value -> $"{value}-virtual-content")

            Html.div [
                prop.className "swt:min-h-0 swt:flex swt:flex-1 swt:flex-col"
                if maxHeightPx.IsSome then
                    prop.style [ style.maxHeight maxHeightPx.Value ]
                prop.children [
                    Html.div [
                        prop.ref headerScrollRef
                        prop.className "swt:overflow-hidden"
                        prop.children [
                            Html.div [
                                prop.className "swt:min-w-232"
                                prop.style [ style.custom ("width", headerContentWidth) ]
                                prop.children [
                                    Html.div [
                                        prop.className
                                            "swt:grid swt:grid-cols-2 swt:divide-x swt:divide-base-content/10"
                                        prop.children [
                                            ComparisonHeader(
                                                fst leftHeader,
                                                (snd leftHeader |> fst),
                                                (snd leftHeader |> snd),
                                                leftHeaderTestId
                                            )
                                            ComparisonHeader(
                                                fst rightHeader,
                                                (snd rightHeader |> fst),
                                                (snd rightHeader |> snd),
                                                rightHeaderTestId
                                            )
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        if scrollTestId.IsSome then
                            prop.testId scrollTestId.Value
                        prop.ref bodyScrollRef
                        prop.className "swt:min-h-0 swt:flex-1 swt:overflow-auto swt:scrollbar-fade"
                        prop.children [
                            Html.div [
                                if virtualContentTestId.IsSome then
                                    prop.testId virtualContentTestId.Value
                                prop.ref bodyContentRef
                                prop.className "swt:relative swt:w-full swt:min-w-232"
                                prop.style [ style.height (rowVirtualizer.getTotalSize ()) ]
                                prop.children [
                                    for virtualItem in virtualItems do
                                        React.KeyedFragment(
                                            virtualItem.Key,
                                            [
                                                ComparisonGridRow(
                                                    {
                                                        Row = rows.[virtualItem.Index]
                                                        Index = virtualItem.Index
                                                        VirtualStart = virtualItem.Start
                                                        MeasureElementRef = rowVirtualizer.measureElement
                                                        Theme = theme
                                                        RowTestId =
                                                            scrollTestId
                                                            |> Option.map (fun value ->
                                                                $"{value}-row-{virtualItem.Index}"
                                                            )
                                                    }
                                                )
                                            ]
                                        )
                                ]
                            ]
                        ]
                    ]
                ]
            ]
