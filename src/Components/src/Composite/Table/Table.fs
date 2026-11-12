namespace Swate.Components.Composite.Table

open System
open Swate.Components
open Swate.Components.Composite.Table.Context
open Swate.Components.Composite.Table.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz

module private TableHelper =

    let (|ActiveTrigger|Default|) (e: Browser.Types.KeyboardEvent) =

        let isControl = e.ctrlKey || e.metaKey

        let eventCode = e.code
        let lower = eventCode.ToLower()

        let isTriggerKey =
            (lower.StartsWith("key")
             || lower.StartsWith("digit")
             || lower.StartsWith("numpad"))
            || lower.StartsWith("backspace")

        if isTriggerKey && not isControl then
            ActiveTrigger
        else
            Default


    let keyDownController
        (e: Browser.Types.KeyboardEvent)
        (selectedCells: GridSelect.GridSelectHandle)
        (activeCellIndex: CellCoordinate option, setActiveCellIndex)
        onKeydown
        =
        if activeCellIndex.IsNone then
            let nav = selectedCells.selectBy e

            if not nav && selectedCells.count > 0 then
                onKeydown
                |> Option.iter (fun onKeydown -> onKeydown (e, selectedCells, activeCellIndex))

                match e with
                | ActiveTrigger ->
                    selectedCells.SelectOrigin
                    |> Option.defaultValue selectedCells.selectedCellsReducedSet.MinimumElement
                    |> Some
                    |> setActiveCellIndex
                | Default -> ()

[<Mangle(false); Erase>]
type Table =

    static member TableCellStyle =
        """swt:border-1 swt:border-base-content/30 swt:data-[selected=true]:text-secondary-content swt:data-[selected=true]:bg-secondary
swt:data-[is-append-origin=true]:border swt:data-[is-append-origin=true]:border-base-content
swt:data-[active=true]:!bg-base-200/50 swt:data-[active=true]:!text-base-content
swt:cursor-pointer
swt:select-none
swt:p-0"""

    [<ReactComponent(true)>]
    static member Table
        (
            rowCount: int,
            columnCount: int,
            renderCell: CellCoordinate -> ReactElement,
            renderActiveCell: CellCoordinate -> ReactElement,
            ?ref: IRefValue<TableHandle>,
            ?height: int,
            ?width: int,
            ?onSelect: GridSelect.OnSelect,
            ?onKeydown: (Browser.Types.KeyboardEvent * GridSelect.GridSelectHandle * CellCoordinate option) -> unit,
            ?enableColumnHeaderSelect: bool,
            ?defaultStyleSelect: bool,
            ?debug: bool
        ) =

        let WhiteSpacePX = 800

        // check for navigator agent if it is safari
        let isSafari =
            let userAgent = Browser.Dom.window?navigator?userAgent |> string
            userAgent.Contains "Safari" && not (userAgent.Contains "Chrome")

        let debug = defaultArg debug false
        let enableColumnHeaderSelect = defaultArg enableColumnHeaderSelect false
        let defaultStyleSelect = defaultArg defaultStyleSelect true

        let scrollContainerRef = React.useElementRef ()

        let rowVirtualizer =
            Virtual.useVirtualizer (
                count = rowCount,
                getScrollElement = (fun () -> scrollContainerRef.current),
                estimateSize = (fun _ -> Constants.Table.DefaultRowHeight),
                overscan = 2,
                gap = 0,
                scrollPaddingStart = 1.5 * float Constants.Table.DefaultRowHeight,
                scrollPaddingEnd = 1.5 * float Constants.Table.DefaultRowHeight,
                paddingEnd = WhiteSpacePX, // extra space to improve UX with rightmost columns
                rangeExtractor =
                    (fun range ->
                        let next = set [ 0; yield! Virtual.defaultRangeExtractor range ]
                        Set.toArray next
                    )
            )

        let columnVirtualizer =
            Virtual.useVirtualizer (
                count = columnCount,
                getScrollElement = (fun () -> scrollContainerRef.current),
                estimateSize = (fun _ -> Constants.Table.DefaultColumnWidth),
                overscan = 2,
                gap = 0,
                paddingEnd = WhiteSpacePX, // extra space to improve UX with rightmost columns
                horizontal = true,
                rangeExtractor =
                    (fun range ->
                        let next = set [ 0; yield! Virtual.defaultRangeExtractor range ]
                        Set.toArray next
                    )
            )

        /// ⚠️ There is bug with the ``scrollToIndex`` function and the ``paddingEnd`` option for ``useVirtualizer``.
        /// It seems like ``scrollToIndex(lastIndex)`` does not actually scroll to the element, but instead scrolls to the end of the scroll container.
        /// Using ``align = center`` is a workaround for this issue, but it is not ideal.
        /// This is a issue with the library and is being tracked here: https://github.com/TanStack/virtual/issues/1257
        let scrollTo =
            fun (coordinate: CellCoordinate) ->
                if coordinate.y = rowCount - 1 then
                    rowVirtualizer.scrollToIndex (coordinate.y, align = Virtual.AlignOption.Center)
                else
                    rowVirtualizer.scrollToIndex (coordinate.y)

                if coordinate.x = columnCount - 1 then
                    columnVirtualizer.scrollToIndex (coordinate.x, align = Virtual.AlignOption.Center)
                else
                    columnVirtualizer.scrollToIndex (coordinate.x)

        let onSelect =
            fun cell newCellRange ->
                onSelect |> Option.iter (fun onSelect -> onSelect cell newCellRange)

                if newCellRange.IsSome then
                    scrollTo (cell)

        let activeCellIndex, setActiveCellIndex =
            React.useState (None: CellCoordinate option)

        let GridSelect =
            React.useGridSelect (
                rowCount = rowCount,
                columnCount = columnCount,
                minRow = (if enableColumnHeaderSelect then 0 else 1),
                minCol = 1,
                onSelect = onSelect
            )

        React.useImperativeHandle (
            !!ref,
            (fun () ->
                TableHandle(
                    focus = (fun () -> scrollContainerRef.current.Value.focus ()),
                    scrollTo = scrollTo,
                    SelectHandle =
                        SelectHandle(
                            contains = GridSelect.contains,
                            selectAt = GridSelect.selectAt,
                            clear = GridSelect.clear,
                            getSelectedCells =
                                (fun () ->
                                    match GridSelect.selectedCells with
                                    | Some range -> CellCoordinateRange.toArray range
                                    | None -> ResizeArray()
                                ),
                            getSelectedCellRange = (fun () -> GridSelect.selectedCells),
                            getCount = (fun () -> GridSelect.count)
                        )
                )
            ),
            [| GridSelect.selectedCells |]
        )

        let isSelected = fun (index: CellCoordinate) -> GridSelect.contains index

        let isOrigin =
            fun (index: CellCoordinate) -> GridSelect.SelectOrigin |> Option.exists (fun origin -> origin = index)

        let isActive = fun (index: CellCoordinate) -> (activeCellIndex = Some index)

        let ctx =

            let onKeyDown =
                fun (index: CellCoordinate) (e: Browser.Types.KeyboardEvent) ->
                    match e.code with
                    | kbdEventCode.enter ->
                        if GridSelect.count > 1 then
                            if GridSelect.contains ({| index with y = index.y + 1 |}) then
                                setActiveCellIndex (Some({| index with y = index.y + 1 |}))
                            elif
                                GridSelect.contains (
                                    {|
                                        x = index.x + 1
                                        y = GridSelect.selectedCells.Value.yStart
                                    |}
                                )
                            then
                                setActiveCellIndex (
                                    Some {|
                                        x = index.x + 1
                                        y = GridSelect.selectedCells.Value.yStart
                                    |}
                                )
                            else
                                let restartIndex: CellCoordinate = {|
                                    x = GridSelect.selectedCells.Value.xStart
                                    y = GridSelect.selectedCells.Value.yStart
                                |}

                                setActiveCellIndex (Some restartIndex)
                        else
                            let newIndex: CellCoordinate = {| x = index.x; y = index.y + 1 |}
                            setActiveCellIndex None
                            GridSelect.selectAt (newIndex, false)
                            scrollContainerRef.current.Value.focus ()
                    | kbdEventCode.escape ->
                        setActiveCellIndex (None)
                        scrollContainerRef.current.Value.focus ()
                        GridSelect.clear ()
                    | _ -> ()

            let onBlur =
                fun (index: CellCoordinate) (_: Browser.Types.FocusEvent) ->
                    if activeCellIndex = Some index then
                        setActiveCellIndex (None)
                        scrollContainerRef.current.Value.focus ()

            let onClick =
                fun (index: CellCoordinate) (e: Browser.Types.MouseEvent) ->
                    if isActive (index) |> not then
                        if e.detail >= 2 then
                            setActiveCellIndex (Some index)

                            if GridSelect.count = 0 then
                                GridSelect.selectAt (index, false)
                        else
                            let nextSet = Set.singleton (index)

                            if GridSelect.selectedCellsReducedSet = nextSet then
                                GridSelect.clear ()
                            else
                                GridSelect.selectAt (index, e.shiftKey)

            TableState(
                isActive = isActive,
                isOrigin = isOrigin,
                isSelected = isSelected,
                onBlur = onBlur,
                onKeyDown = onKeyDown,
                onClick = onClick
            )

        TableStateCtx.Provider(
            ctx,
            React.Fragment [
                Html.div [
                    prop.key "scroll-container"
                    prop.ref scrollContainerRef
                    prop.onKeyDown (fun e ->
                        TableHelper.keyDownController e GridSelect (activeCellIndex, setActiveCellIndex) onKeydown
                    )
                    prop.tabIndex 0
                    prop.style [
                        if height.IsSome then
                            style.height height.Value
                        if width.IsSome then
                            style.width width.Value

                        style.minHeight 0
                        style.minWidth 0
                    ]
                    prop.className
                        "swt:overflow-auto swt:grow swt:h-full swt:w-full swt:border swt:border-primary swt:rounded-sm swt:bg-base-100"
                    if debug then
                        prop.testId "virtualized-table"
                    prop.children [
                        Html.div [
                            prop.key "table-container"
                            prop.style [
                                if isSafari then
                                    style.custom ("willChange", "transform")
                                    style.custom ("minHeight", $"{rowVirtualizer.getTotalSize ()}px")
                                    style.minWidth (columnVirtualizer.getTotalSize ())
                                    style.custom ("contain", "size layout paint")
                                else
                                    style.height (rowVirtualizer.getTotalSize ())
                                    style.width (columnVirtualizer.getTotalSize ())

                                style.position.relative
                            ]
                            prop.children [
                                Html.table [
                                    prop.key "table"
                                    prop.className "swt:w-full swt:h-full"
                                    prop.children [
                                        Html.thead [
                                            prop.key "table-thead"
                                            prop.children [
                                                Html.tr [
                                                    prop.key "virtualHeaderRow"
                                                    prop.className
                                                        "swt:sticky swt:top-0 swt:left-0 swt:z-10 swt:bg-base-100 swt:text-left"
                                                    prop.style [ style.height Constants.Table.DefaultRowHeight ]
                                                    prop.children [
                                                        for virtualColumn in columnVirtualizer.getVirtualItems () do
                                                            let index = {| x = virtualColumn.index; y = 0 |}

                                                            let isActive = isActive index

                                                            Html.th [

                                                                prop.ref (fun element ->
                                                                    columnVirtualizer.measureElement (
                                                                        Option.ofObj element
                                                                    )
                                                                )

                                                                prop.custom ("data-index", virtualColumn.index)
                                                                prop.key $"virtualHeaderCell-{virtualColumn.key}--1"
                                                                prop.className [
                                                                    if virtualColumn.index <> 0 then
                                                                        "swt:min-w-32"
                                                                    else
                                                                        "swt:min-w-min"
                                                                    "swt:h-full swt:resize-x swt:overflow-hidden"
                                                                    if defaultStyleSelect then
                                                                        Table.TableCellStyle
                                                                ]
                                                                prop.dataRow 0
                                                                prop.dataColumn virtualColumn.index
                                                                prop.style [
                                                                    style.position.absolute
                                                                    style.top 0
                                                                    style.left 0
                                                                    style.custom (
                                                                        "transform",
                                                                        $"translateX({virtualColumn.start}px)"
                                                                    )
                                                                ]
                                                                if isActive then
                                                                    prop.custom ("data-active", true)
                                                                if isSelected (index) then
                                                                    prop.custom ("data-selected", true)
                                                                if isOrigin (index) then
                                                                    prop.custom ("data-is-append-origin", true)
                                                                prop.children [
                                                                    if virtualColumn.index = 0 then
                                                                        TableCell.BaseCell(
                                                                            index.y,
                                                                            index.x,
                                                                            Html.text (
                                                                                let i =
                                                                                    rowVirtualizer.getVirtualIndexes ()

                                                                                if i.Length > 0 then
                                                                                    i |> Seq.last
                                                                                else
                                                                                    0
                                                                            ),
                                                                            className =
                                                                                "swt:px-2 swt:py-2 swt:flex swt:items-center swt:cursor-not-allowed swt:w-full swt:h-full swt:min-w-8 swt:bg-base-200 swt:text-transparent",
                                                                            debug = debug
                                                                        )
                                                                    elif isActive then
                                                                        renderActiveCell index
                                                                    else
                                                                        renderCell index
                                                                ]
                                                            ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                        Html.tbody [
                                            prop.key "body"
                                            prop.style [ style.marginTop Constants.Table.DefaultRowHeight ]
                                            prop.children [
                                                for virtualRow in rowVirtualizer.getVirtualItems () do
                                                    let rowStart = virtualRow.start

                                                    if virtualRow.index = 0 then
                                                        Html.none // skip header row, is part of thead
                                                    else
                                                        Html.tr [
                                                            prop.key (sprintf "virtualRow-%s" virtualRow.key)
                                                            prop.style [
                                                                style.position.absolute
                                                                style.top 0
                                                                style.left 0
                                                                style.custom ("transform", $"translateY({rowStart}px)")
                                                                style.height virtualRow.size
                                                            ]
                                                            prop.className "swt:w-full"
                                                            prop.children [
                                                                for virtualColumn in
                                                                    columnVirtualizer.getVirtualItems () do
                                                                    let index = {|
                                                                        x = virtualColumn.index
                                                                        y = virtualRow.index
                                                                    |}

                                                                    let isActive = isActive index

                                                                    Html.td [
                                                                        prop.key (
                                                                            sprintf
                                                                                "virtualCell-%s-%s"
                                                                                virtualRow.key
                                                                                virtualColumn.key
                                                                        )
                                                                        prop.dataRow virtualRow.index
                                                                        prop.dataColumn virtualColumn.index
                                                                        prop.className [
                                                                            if defaultStyleSelect then
                                                                                Table.TableCellStyle
                                                                        ]
                                                                        if isActive then
                                                                            prop.custom ("data-active", true)
                                                                        if isSelected index then
                                                                            prop.custom ("data-selected", true)
                                                                        if isOrigin index then
                                                                            prop.custom ("data-is-append-origin", true)
                                                                        prop.style [
                                                                            // if virtualColumn.index = 0 then
                                                                            //     style.position.sticky
                                                                            //     style.zIndex 10
                                                                            // else
                                                                            style.position.absolute
                                                                            style.width virtualColumn.size
                                                                            style.height virtualRow.size
                                                                            style.top 0
                                                                            style.left 0
                                                                            style.custom (
                                                                                "transform",
                                                                                $"translateX({virtualColumn.start}px)"
                                                                            )
                                                                        ]
                                                                        prop.children [
                                                                            if isActive then
                                                                                renderActiveCell index
                                                                            else
                                                                                renderCell index
                                                                        ]
                                                                    ]
                                                            ]
                                                        ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        )

    [<ReactComponent>]
    static member private EntryInactiveCell(index: CellCoordinate, data: string) =

        TableCell.InactiveCell(index, data |> Html.text)

    [<ReactComponent>]
    static member private EntryActiveCell(index: CellCoordinate, data: string, setData: (string -> unit)) =

        TableCell.StringActiveCell(index, data, setData)

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    static member private RenderInactive(index: CellCoordinate, data: string) = Table.EntryInactiveCell(index, data)

    [<ReactMemoComponent(AreEqualFn.FsEquals)>]
    static member private RenderActive(index: CellCoordinate, data: string, setData: (string -> unit)) =
        Table.EntryActiveCell(index, data, setData)

    [<ReactComponent>]
    static member Entry() =
        let TableHandler = React.useRef<TableHandle> (null)
        let rowCount = 1_000
        let columnCount = 1_000

        let data, setData =
            React.useState (
                [|
                    for i in 0 .. rowCount - 1 do
                        [|
                            for j in 0 .. columnCount - 1 do
                                yield $"Row {i}, Column {j}"
                        |]
                |]
            )

        let getDataItem = (fun (index: CellCoordinate) -> data.[index.y].[index.x])

        let setData =
            (fun (index: CellCoordinate) newData ->
                data.[index.y].[index.x] <- newData
                setData data
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-4"
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.text "scroll to 500, 500"
                    prop.onClick (fun _ ->
                        TableHandler.current.SelectHandle.selectAt ({| x = 500; y = 500 |}, false)
                        TableHandler.current.scrollTo ({| x = 500; y = 500 |})
                    )
                ]
                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.text "scroll to 999, 999"
                    prop.onClick (fun _ ->
                        TableHandler.current.SelectHandle.selectAt ({| x = 999; y = 999 |}, false)
                        TableHandler.current.scrollTo ({| x = 999; y = 999 |})
                    )
                ]
                Table.Table(
                    rowCount = rowCount,
                    columnCount = columnCount,
                    renderCell =
                        (fun index ->
                            let data = getDataItem index
                            Table.RenderInactive(index, data)
                        ),
                    renderActiveCell =
                        (fun index ->
                            let dataItem = getDataItem index
                            Table.RenderActive(index, dataItem, setData index)
                        ),
                    ref = TableHandler,
                    height = 400
                )
            ]
        ]
