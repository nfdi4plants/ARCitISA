namespace Swate.Components.Composite.TableImport

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Composite.TableImport.Types

[<Erase; Mangle(false)>]
type TableImportPreview =

    [<ReactComponent>]
    static member private ImportActionOption
        (index: int, action: TableImportAction, label: string, isActive: bool, setImportAction, ?disabled: bool)
        =
        let disabled = defaultArg disabled false

        Html.label [
            prop.className [
                "swt:inline-flex swt:items-center swt:gap-2"
                if disabled then "swt:opacity-50" else "swt:cursor-pointer"
            ]
            prop.children [
                Html.input [
                    prop.testId $"table-import-action-{index}-{action}"
                    prop.type'.radio
                    prop.name $"table-import-action-{index}"
                    prop.className "swt:radio swt:radio-xs"
                    prop.isChecked isActive
                    prop.disabled disabled
                    prop.onChange (fun (_: bool) -> setImportAction index action)
                ]
                Html.span [ prop.className "swt:text-xs"; prop.text label ]
            ]
        ]

    [<ReactComponent>]
    static member private NewTableNameField
        (newTableName: Swate.Components.Shared.ARCtrlHelper.NewTableName, setName: string -> unit)
        =
        let message (colorClass: string) (iconClass: string) (text: string) =
            Html.span [
                prop.className [
                    "swt:flex swt:items-center swt:gap-1 swt:text-xs"
                    colorClass
                ]
                prop.children [
                    Html.i [ prop.className [ "swt:iconify"; iconClass ] ]
                    Html.span text
                ]
            ]

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-1"
            prop.children [
                Html.label [
                    prop.className "swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        Html.span [ prop.className "swt:text-xs"; prop.text "New table name" ]
                        Html.input [
                            prop.type'.text
                            prop.className [
                                "swt:input swt:input-xs swt:w-full"
                                if newTableName.Error.IsSome then
                                    "swt:input-error"
                            ]
                            prop.value newTableName.Name
                            prop.onChange setName
                        ]
                    ]
                ]
                match newTableName.Error, newTableName.Notice with
                | Some error, _ -> message "swt:text-error" "swt:fluent--warning-12-filled" error
                | None, Some notice -> message "swt:text-info" "swt:fluent--info-12-regular" notice
                | None, None -> Html.none
            ]
        ]

    [<ReactComponent>]
    static member private RowGrid
        (source: ImportSource, callbacks: TableImportPreviewCallbacks, canEdit: bool, showRowSelection: bool)
        =
        let table = source.Table
        let index = source.Index
        let columnCount = table.ColumnCount
        let rowCount = table.RowCount
        let rowHeight = 28
        let headers = table.Headers |> Seq.map string |> Array.ofSeq
        let scrollRef = React.useElementRef ()

        let virtualizer =
            Virtual.useVirtualizer (
                count = rowCount,
                getScrollElement = (fun () -> scrollRef.current),
                estimateSize = (fun _ -> rowHeight),
                overscan = 8
            )

        // The first render has no measured items before the scroll element reports its size.
        let visibleRows =
            let measured = virtualizer.getVirtualItems ()

            if measured.Length = 0 && rowCount > 0 then
                [| 0 .. min (rowCount - 1) 12 |] |> Array.map (fun row -> row, row * rowHeight)
            else
                measured |> Array.map (fun item -> item.index, item.start)

        let dimmed (selected: bool) =
            if selected then "" else "swt:opacity-50"

        // The row number column sticks to the left edge, so the row checkboxes stay visible while scrolling sideways.
        // The corner cell of the header sits above the sticky column of the rows and above the scrolling headers.
        let rowNumberCell (isHeader: bool) (children: ReactElement list) =
            Html.div [
                if isHeader then
                    prop.role.columnHeader
                else
                    prop.custom ("role", "cell")
                prop.className [
                    "swt:sticky swt:left-0 swt:bg-base-100 swt:w-14 swt:min-w-14 swt:shrink-0 swt:px-2 swt:flex swt:items-center swt:gap-1"
                    if isHeader then "swt:z-20" else "swt:z-[5]"
                ]
                prop.children children
            ]

        Html.div [
            prop.testId $"table-import-grid-{index}"
            prop.ref scrollRef
            prop.custom ("role", "table")
            prop.className "swt:max-h-64 swt:overflow-auto swt:relative"
            prop.children [
                Html.div [
                    prop.className "swt:w-max swt:min-w-full"
                    prop.children [
                        Html.div [
                            prop.role.row
                            prop.className "swt:sticky swt:top-0 swt:z-10 swt:bg-base-100 swt:flex"
                            prop.children [
                                rowNumberCell true [
                                    Html.span [
                                        prop.className "swt:text-xs swt:opacity-70"
                                        prop.text "#"
                                    ]
                                ]
                                for columnIndex in 0 .. columnCount - 1 do
                                    let header = headers.[columnIndex]
                                    let selected = callbacks.IsColumnSelected index columnIndex

                                    Html.div [
                                        prop.key $"{index}_{columnIndex}_header_card"
                                        prop.role.columnHeader
                                        prop.className "swt:w-52 swt:min-w-52 swt:shrink-0 swt:px-2 swt:py-1"
                                        prop.children [
                                            Html.label [
                                                prop.className [
                                                    "swt:flex swt:items-center swt:gap-2 swt:cursor-pointer swt:border swt:border-base-300 swt:rounded-box swt:px-2 swt:py-1"
                                                    dimmed selected
                                                ]
                                                prop.children [
                                                    Html.input [
                                                        prop.testId $"table-import-column-{index}-{columnIndex}"
                                                        prop.type'.checkbox
                                                        prop.className "swt:checkbox swt:checkbox-xs"
                                                        prop.isChecked selected
                                                        prop.disabled (not canEdit)
                                                        prop.onChange (fun (_: bool) ->
                                                            callbacks.ToggleColumnSelection index columnIndex
                                                        )
                                                    ]
                                                    Html.span [
                                                        prop.className "swt:text-xs swt:font-medium swt:truncate"
                                                        prop.title header
                                                        prop.text header
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                        if rowCount = 0 then
                            Html.div [
                                prop.className "swt:text-xs swt:opacity-70 swt:px-2 swt:py-1"
                                prop.text "This table has no rows."
                            ]
                        else
                            Html.div [
                                prop.className "swt:relative"
                                prop.style [ style.height (virtualizer.getTotalSize ()) ]
                                prop.children [
                                    for (row, start) in visibleRows do
                                        let rowSelected = callbacks.IsRowSelected index row

                                        Html.div [
                                            prop.key $"{index}_{row}"
                                            prop.role.row
                                            prop.className [
                                                "swt:flex swt:items-center swt:absolute swt:left-0 swt:top-0 swt:border-t swt:border-base-300"
                                                dimmed (rowSelected || not showRowSelection)
                                            ]
                                            prop.style [ style.top start; style.height rowHeight ]
                                            prop.children [
                                                rowNumberCell false [
                                                    if showRowSelection then
                                                        Html.input [
                                                            prop.testId $"table-import-row-{index}-{row}"
                                                            prop.ariaLabel $"Row {row + 1} of {source.Name}"
                                                            prop.type'.checkbox
                                                            prop.className "swt:checkbox swt:checkbox-xs"
                                                            prop.isChecked rowSelected
                                                            prop.disabled (not canEdit)
                                                            prop.onChange (fun (_: bool) ->
                                                                callbacks.ToggleRowSelection index row
                                                            )
                                                        ]
                                                    Html.span [
                                                        prop.className "swt:text-xs swt:opacity-70"
                                                        prop.text (row + 1)
                                                    ]
                                                ]
                                                for columnIndex in 0 .. columnCount - 1 do
                                                    let text =
                                                        match table.TryGetCellAt(columnIndex, row) with
                                                        | Some cell -> Helper.cellText cell
                                                        | None -> ""

                                                    Html.div [
                                                        prop.key $"{index}_{row}_{columnIndex}"
                                                        prop.custom ("role", "cell")
                                                        prop.className [
                                                            "swt:w-52 swt:min-w-52 swt:shrink-0 swt:px-2 swt:text-xs swt:truncate"
                                                            dimmed (callbacks.IsColumnSelected index columnIndex)
                                                        ]
                                                        prop.title text
                                                        prop.text text
                                                    ]
                                            ]
                                        ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private SourceCard
        (
            source: ImportSource,
            callbacks: TableImportPreviewCallbacks,
            canAppend: bool,
            appendDisabledNote: string option,
            showRowSelection: bool
        ) =
        let table = source.Table
        let index = source.Index
        let columnCount = table.ColumnCount
        let rowCount = table.RowCount

        let importAction = callbacks.GetImportAction index
        let hasBeenOpened, setHasBeenOpened = React.useState false

        let selectedColumnsCount =
            Seq.init columnCount id
            |> Seq.filter (callbacks.IsColumnSelected index)
            |> Seq.length

        let selectedRowsCount = max 0 (rowCount - callbacks.DeselectedRowCount index)

        let canEdit = importAction <> TableImportAction.NoImport
        let hasRowSelection = showRowSelection && rowCount > 0

        let plural (count: int) (singular: string) =
            if count = 1 then
                $"1 {singular}"
            else
                $"{count} {singular}s"

        let rowText = plural rowCount "row"
        let columnText = plural columnCount "column"
        let sizeText = $"{rowText}, {columnText}"

        let summaryText =
            let columns = $"Columns: {selectedColumnsCount}/{columnCount} selected"

            if hasRowSelection then
                $"{columns}, rows: {selectedRowsCount}/{rowCount} selected"
            else
                columns

        let hint (testId: string) (text: string) =
            Html.div [
                prop.testId testId
                prop.className "swt:flex swt:items-center swt:gap-1 swt:text-xs swt:text-warning"
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--warning-12-filled"
                    ]
                    Html.span text
                ]
            ]

        let toolbarButton (testId: string option) (text: string) (onClick: unit -> unit) =
            Html.button [
                match testId with
                | Some testId -> prop.testId testId
                | None -> ()
                prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                prop.text text
                prop.disabled (not canEdit)
                prop.onClick (fun _ -> onClick ())
            ]

        Html.div [
            prop.className "swt:border swt:border-base-300 swt:rounded-box swt:p-2 swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-col"
                    prop.children [
                        Html.div [
                            prop.className "swt:text-sm swt:font-medium swt:truncate"
                            prop.title source.Name
                            prop.text source.Name
                        ]
                        Html.div [
                            prop.className "swt:text-xs swt:opacity-70"
                            prop.text sizeText
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        TableImportPreview.ImportActionOption(
                            index,
                            TableImportAction.ImportAsNewTable,
                            "Create new table",
                            (importAction = TableImportAction.ImportAsNewTable),
                            callbacks.SetImportAction
                        )
                        TableImportPreview.ImportActionOption(
                            index,
                            TableImportAction.AppendToActiveTable,
                            "Append to active table",
                            (importAction = TableImportAction.AppendToActiveTable),
                            callbacks.SetImportAction,
                            disabled = not canAppend
                        )
                        TableImportPreview.ImportActionOption(
                            index,
                            TableImportAction.NoImport,
                            "Do not import",
                            (importAction = TableImportAction.NoImport),
                            callbacks.SetImportAction
                        )
                        match appendDisabledNote with
                        | Some note when not canAppend ->
                            Html.div [
                                prop.className "swt:text-xs swt:opacity-70"
                                prop.text note
                            ]
                        | _ -> Html.none
                    ]
                ]
                match callbacks.GetNewTableName index with
                | Some newTableName ->
                    TableImportPreview.NewTableNameField(newTableName, callbacks.SetNewTableName index)
                | None -> Html.none
                Html.details [
                    prop.className "swt:border swt:border-base-300 swt:rounded-box swt:px-2 swt:py-1"
                    prop.onToggle (fun e ->
                        if (e.currentTarget :?> Browser.Types.HTMLElement).hasAttribute "open" then
                            setHasBeenOpened true
                    )
                    prop.children [
                        Html.summary [
                            prop.className "swt:cursor-pointer swt:text-xs swt:font-medium"
                            prop.text summaryText
                        ]
                        if canEdit && columnCount > 0 && selectedColumnsCount = 0 then
                            hint $"table-import-no-columns-{index}" "Select at least one column to import this table."
                        if hasRowSelection && canEdit && selectedRowsCount = 0 then
                            hint $"table-import-no-rows-{index}" "Select at least one row to import this table."
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:mt-2"
                            prop.children [
                                if columnCount = 0 then
                                    Html.div [
                                        prop.className "swt:text-xs swt:opacity-70"
                                        prop.text "This table has no columns."
                                    ]
                                else
                                    Html.div [
                                        prop.className "swt:flex swt:items-center swt:gap-2"
                                        prop.children [
                                            Html.div [
                                                prop.className "swt:ml-auto swt:flex swt:flex-wrap swt:gap-1"
                                                prop.children [
                                                    toolbarButton
                                                        None
                                                        "Select all columns"
                                                        (fun () -> callbacks.SelectAllColumns index)
                                                    toolbarButton
                                                        None
                                                        "Unselect all columns"
                                                        (fun () -> callbacks.UnselectAllColumns index columnCount)
                                                    if hasRowSelection then
                                                        toolbarButton
                                                            (Some $"table-import-select-all-rows-{index}")
                                                            "Select all rows"
                                                            (fun () -> callbacks.SelectAllRows index)

                                                        toolbarButton
                                                            (Some $"table-import-unselect-all-rows-{index}")
                                                            "Unselect all rows"
                                                            (fun () -> callbacks.UnselectAllRows index rowCount)
                                                ]
                                            ]
                                        ]
                                    ]

                                    // Tables that were never opened skip the grid and its virtualizer. An opened grid stays mounted so it keeps its scroll position.
                                    if hasBeenOpened then
                                        TableImportPreview.RowGrid(source, callbacks, canEdit, showRowSelection)
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member TableImportPreview
        (
            sources: ImportSource[],
            callbacks: TableImportPreviewCallbacks,
            ?canAppend: bool,
            ?appendDisabledNote: string,
            ?emptyText: string,
            ?showRowSelection: bool
        ) =
        let canAppend = defaultArg canAppend true
        let showRowSelection = defaultArg showRowSelection true

        if sources.Length = 0 then
            Html.div [
                prop.className "swt:text-sm swt:opacity-70"
                prop.text (defaultArg emptyText "No tables selected.")
            ]
        else
            Html.div [
                prop.className "swt:flex swt:flex-col swt:gap-2"
                prop.children [
                    for source in sources do
                        React.KeyedFragment(
                            source.Index,
                            [
                                TableImportPreview.SourceCard(
                                    source,
                                    callbacks,
                                    canAppend,
                                    appendDisabledNote,
                                    showRowSelection
                                )
                            ]
                        )
                ]
            ]
