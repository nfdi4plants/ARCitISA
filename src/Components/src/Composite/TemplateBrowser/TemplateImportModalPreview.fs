namespace Swate.Components.Composite.Template

open ARCtrl
open Fable.Core
open Feliz

module TemplateHelper = Swate.Components.Composite.Template.Helper
module TemplateTypes = Swate.Components.Composite.Template.Types

[<Erase; Mangle(false)>]
type TemplateImportModalPreview =

    [<ReactComponent>]
    static member ImportActionOption
        (id, action: TemplateTypes.TemplateImportAction, label: string, isActive: bool, setTemplateImportAction)
        =
        let radioGroupName = $"template-import-action-{id}"

        Html.label [
            prop.className "swt:inline-flex swt:items-center swt:gap-2 swt:cursor-pointer"
            prop.children [
                Html.input [
                    prop.testId $"template-import-action-{id}-{action}"
                    prop.type'.radio
                    prop.name radioGroupName
                    prop.className "swt:radio swt:radio-xs"
                    prop.isChecked isActive
                    prop.onChange (fun (_: bool) -> setTemplateImportAction id action)
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

    [<ReactComponent(true)>]
    static member TemplateImportModalPreview(templates: Template[], callbacks: TemplateTypes.TemplatePreviewCallbacks) =
        if templates.Length = 0 then
            Html.div [
                prop.className "swt:text-sm swt:opacity-70"
                prop.text "No templates selected."
            ]
        else
            Html.div [
                prop.className "swt:flex swt:flex-col swt:gap-2"
                prop.children [
                    for template in templates do
                        let importAction = callbacks.GetTemplateImportAction template.Id
                        let columns = template.Table.Columns |> Array.ofSeq

                        let selectedColumnsCount =
                            columns
                            |> Array.indexed
                            |> Array.sumBy (fun (columnIndex, _) ->
                                if callbacks.IsColumnSelected template.Id columnIndex then
                                    1
                                else
                                    0
                            )

                        let canEditColumns = importAction <> TemplateTypes.TemplateImportAction.NoImport

                        Html.div [
                            prop.key (string template.Id)
                            prop.className
                                "swt:border swt:border-base-300 swt:rounded-box swt:p-2 swt:flex swt:flex-col swt:gap-2"
                            prop.children [
                                Html.div [
                                    prop.className "swt:text-sm swt:font-medium swt:truncate"
                                    prop.title template.Name
                                    prop.text template.Name
                                ]
                                Html.div [
                                    prop.className "swt:flex swt:flex-col swt:gap-1"
                                    prop.children [
                                        TemplateImportModalPreview.ImportActionOption(
                                            template.Id,
                                            TemplateTypes.TemplateImportAction.ImportAsNewTable,
                                            "Import (new table)",
                                            (importAction = TemplateTypes.TemplateImportAction.ImportAsNewTable),
                                            setTemplateImportAction = callbacks.SetTemplateImportAction
                                        )
                                        TemplateImportModalPreview.ImportActionOption(
                                            template.Id,
                                            TemplateTypes.TemplateImportAction.AppendToActiveTable,
                                            "Append to active table",
                                            (importAction = TemplateTypes.TemplateImportAction.AppendToActiveTable),
                                            setTemplateImportAction = callbacks.SetTemplateImportAction
                                        )
                                        TemplateImportModalPreview.ImportActionOption(
                                            template.Id,
                                            TemplateTypes.TemplateImportAction.NoImport,
                                            "No import",
                                            (importAction = TemplateTypes.TemplateImportAction.NoImport),
                                            setTemplateImportAction = callbacks.SetTemplateImportAction
                                        )
                                    ]
                                ]
                                match callbacks.GetNewTableName template.Id with
                                | Some newTableName ->
                                    TemplateImportModalPreview.NewTableNameField(
                                        newTableName,
                                        callbacks.SetNewTableName template.Id
                                    )
                                | None -> Html.none
                                Html.details [
                                    prop.className "swt:border swt:border-base-300 swt:rounded-box swt:px-2 swt:py-1"
                                    prop.children [
                                        Html.summary [
                                            prop.className "swt:cursor-pointer swt:text-xs swt:font-medium"
                                            prop.textf "Columns: %d/%d selected" selectedColumnsCount columns.Length
                                        ]
                                        Html.div [
                                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:mt-2"
                                            prop.children [
                                                if columns.Length = 0 then
                                                    Html.div [
                                                        prop.className "swt:text-xs swt:opacity-70"
                                                        prop.text "Template has no columns."
                                                    ]
                                                else
                                                    React.Fragment [
                                                        Html.div [
                                                            prop.className "swt:flex swt:items-center swt:gap-2"
                                                            prop.children [
                                                                Html.button [
                                                                    prop.className
                                                                        "swt:btn swt:btn-ghost swt:btn-xs swt:ml-auto"
                                                                    prop.text "Select all"
                                                                    prop.disabled (not canEditColumns)
                                                                    prop.onClick (fun _ ->
                                                                        callbacks.SelectAllTemplateColumns template.Id
                                                                    )
                                                                ]
                                                                Html.button [
                                                                    prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                                                                    prop.text "Unselect all"
                                                                    prop.disabled (not canEditColumns)
                                                                    prop.onClick (fun _ ->
                                                                        callbacks.UnselectAllTemplateColumns template
                                                                    )
                                                                ]
                                                            ]
                                                        ]
                                                        Html.div [
                                                            prop.className "swt:overflow-x-auto"
                                                            prop.children [
                                                                Html.table [
                                                                    prop.className
                                                                        "swt:table swt:table-xs swt:table-fixed swt:w-max"
                                                                    prop.children [
                                                                        Html.tbody [
                                                                            Html.tr [
                                                                                prop.children [
                                                                                    for columnIndex in
                                                                                        0 .. columns.Length - 1 do
                                                                                        let header =
                                                                                            columns.[columnIndex].Header
                                                                                                .ToString()

                                                                                        Html.td [
                                                                                            prop.key
                                                                                                $"{template.Id}_{columnIndex}_header_card"
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
                                                                                                            prop
                                                                                                                .isChecked (
                                                                                                                    callbacks.IsColumnSelected
                                                                                                                        template.Id
                                                                                                                        columnIndex
                                                                                                                )
                                                                                                            prop
                                                                                                                .disabled (
                                                                                                                    not
                                                                                                                        canEditColumns
                                                                                                                )
                                                                                                            prop.onChange (fun
                                                                                                                               (_:
                                                                                                                                   bool) ->
                                                                                                                callbacks.ToggleColumnSelection
                                                                                                                    template.Id
                                                                                                                    columnIndex
                                                                                                            )
                                                                                                        ]
                                                                                                        Html.span [
                                                                                                            prop.className
                                                                                                                "swt:text-xs swt:font-medium swt:truncate"
                                                                                                            prop.title
                                                                                                                header
                                                                                                            prop.text
                                                                                                                header
                                                                                                        ]
                                                                                                    ]
                                                                                                ]
                                                                                            ]
                                                                                        ]
                                                                                ]
                                                                            ]
                                                                            Html.tr [
                                                                                prop.children [
                                                                                    for columnIndex in
                                                                                        0 .. columns.Length - 1 do
                                                                                        let valuePreviewText =
                                                                                            TemplateHelper.templateColumnValuePreview
                                                                                                template.Table
                                                                                                columnIndex

                                                                                        Html.td [
                                                                                            prop.key
                                                                                                $"{template.Id}_{columnIndex}_values_card"
                                                                                            prop.className
                                                                                                "swt:w-52 swt:min-w-52 swt:px-2 swt:pt-0 swt:pb-1"
                                                                                            prop.children [
                                                                                                Html.span [
                                                                                                    prop.className
                                                                                                        "swt:block swt:text-[10px] swt:opacity-70 swt:truncate"
                                                                                                    prop.title
                                                                                                        valuePreviewText
                                                                                                    prop.text
                                                                                                        valuePreviewText
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
                        ]
                ]
            ]
