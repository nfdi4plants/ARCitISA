namespace Swate.Components.Composite.TableImport

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components.Composite.TableImport.Types

module PreviewHelper = Swate.Components.Composite.TableImport.Helper

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
    static member private SourceCard
        (
            source: ImportSource,
            callbacks: TableImportPreviewCallbacks,
            canAppend: bool,
            appendDisabledNote: string option
        ) =
        let table = source.Table
        let index = source.Index
        let columnCount = table.ColumnCount
        let rowCount = table.RowCount

        // Scanning a table for previews is linear in its size, so it must not rerun on every checkbox toggle.
        let valuePreviews =
            React.useMemo (
                (fun () -> Array.init table.ColumnCount (PreviewHelper.columnValuePreview table)),
                [| box table |]
            )

        let importAction = callbacks.GetImportAction index
        let headers = table.Headers |> Seq.map string |> Array.ofSeq

        let selectedColumnsCount =
            Seq.init columnCount id
            |> Seq.filter (callbacks.IsColumnSelected index)
            |> Seq.length

        let canEditColumns = importAction <> TableImportAction.NoImport

        let plural (count: int) (singular: string) =
            if count = 1 then
                $"1 {singular}"
            else
                $"{count} {singular}s"

        let rowText = plural rowCount "row"
        let columnText = plural columnCount "column"
        let sizeText = $"{rowText}, {columnText}"

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
                    prop.children [
                        Html.summary [
                            prop.className "swt:cursor-pointer swt:text-xs swt:font-medium"
                            prop.textf "Columns: %d/%d selected" selectedColumnsCount columnCount
                        ]
                        if canEditColumns && columnCount > 0 && selectedColumnsCount = 0 then
                            Html.div [
                                prop.testId $"table-import-no-columns-{index}"
                                prop.className "swt:flex swt:items-center swt:gap-1 swt:text-xs swt:text-warning"
                                prop.children [
                                    Html.i [
                                        prop.className "swt:iconify swt:fluent--warning-12-filled"
                                    ]
                                    Html.span "Select at least one column to import this table."
                                ]
                            ]
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
                                            Html.button [
                                                prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:ml-auto"
                                                prop.text "Select all"
                                                prop.disabled (not canEditColumns)
                                                prop.onClick (fun _ -> callbacks.SelectAllColumns index)
                                            ]
                                            Html.button [
                                                prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                                                prop.text "Unselect all"
                                                prop.disabled (not canEditColumns)
                                                prop.onClick (fun _ -> callbacks.UnselectAllColumns index columnCount)
                                            ]
                                        ]
                                    ]

                                    Html.div [
                                        prop.className "swt:overflow-x-auto"
                                        prop.children [
                                            Html.table [
                                                prop.className "swt:table swt:table-xs swt:table-fixed swt:w-max"
                                                prop.children [
                                                    Html.tbody [
                                                        Html.tr [
                                                            prop.children [
                                                                for columnIndex in 0 .. columnCount - 1 do
                                                                    let header = headers.[columnIndex]

                                                                    Html.td [
                                                                        prop.key $"{index}_{columnIndex}_header_card"
                                                                        prop.className
                                                                            "swt:w-52 swt:min-w-52 swt:px-2 swt:py-1"
                                                                        prop.children [
                                                                            Html.label [
                                                                                prop.className
                                                                                    "swt:flex swt:items-center swt:gap-2 swt:cursor-pointer swt:border swt:border-base-300 swt:rounded-box swt:px-2 swt:py-1"
                                                                                prop.children [
                                                                                    Html.input [
                                                                                        prop.type'.checkbox
                                                                                        prop.className
                                                                                            "swt:checkbox swt:checkbox-xs"
                                                                                        prop.isChecked (
                                                                                            callbacks.IsColumnSelected
                                                                                                index
                                                                                                columnIndex
                                                                                        )
                                                                                        prop.disabled (
                                                                                            not canEditColumns
                                                                                        )
                                                                                        prop.onChange (fun (_: bool) ->
                                                                                            callbacks.ToggleColumnSelection
                                                                                                index
                                                                                                columnIndex
                                                                                        )
                                                                                    ]
                                                                                    Html.span [
                                                                                        prop.className
                                                                                            "swt:text-xs swt:font-medium swt:truncate"
                                                                                        prop.title header
                                                                                        prop.text header
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                            ]
                                                        ]
                                                        Html.tr [
                                                            prop.children [
                                                                for columnIndex in 0 .. columnCount - 1 do
                                                                    let valuePreviewText = valuePreviews.[columnIndex]

                                                                    Html.td [
                                                                        prop.key $"{index}_{columnIndex}_values_card"
                                                                        prop.className
                                                                            "swt:w-52 swt:min-w-52 swt:px-2 swt:pt-0 swt:pb-1"
                                                                        prop.children [
                                                                            Html.span [
                                                                                prop.className
                                                                                    "swt:block swt:text-[10px] swt:opacity-70 swt:truncate"
                                                                                prop.title valuePreviewText
                                                                                prop.text valuePreviewText
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
            ?emptyText: string
        ) =
        let canAppend = defaultArg canAppend true

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
                                TableImportPreview.SourceCard(source, callbacks, canAppend, appendDisabledNote)
                            ]
                        )
                ]
            ]
