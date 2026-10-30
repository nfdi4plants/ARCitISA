namespace Swate.Components.Composite.Template

open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Primitive.BaseModal
open Swate.Components.Composite.TableImport
open Swate.Components.Composite.TableImport.Types
open Swate.Components.Composite.Template.Types

[<Erase; Mangle(false)>]
type TemplateImportModal =

    [<ReactComponent(true)>]
    static member TemplateImportModal
        (
            isOpen,
            importType: TableJoinOptions,
            selectedTemplates: Template[],
            setIsOpen,
            setImportType,
            submitImport: ImportModalConfirmPayload -> unit,
            ?existingTableNames: string[],
            ?importError: string
        ) =

        let existingTableNames = defaultArg existingTableNames [||]

        let templateImportDecisions, setTemplateImportDecisions =
            React.useStateWithUpdater (Map.empty<System.Guid, TableImportAction>)

        let deselectedTemplateColumns, setDeselectedTemplateColumns =
            React.useStateWithUpdater (Set.empty<System.Guid * int>)

        let deselectedTemplateRows, setDeselectedTemplateRows =
            React.useStateWithUpdater (Set.empty<System.Guid * int>)

        let enteredNewTableNames, setEnteredNewTableNames =
            React.useStateWithUpdater (Map.empty<System.Guid, string>)

        let selectedTemplateIds =
            React.useMemo (
                (fun () -> selectedTemplates |> Seq.map (fun template -> template.Id) |> Set.ofSeq),
                [| box selectedTemplates |]
            )

        React.useEffect (
            (fun () ->
                setTemplateImportDecisions (fun decisions ->
                    decisions
                    |> Map.filter (fun templateId _ -> selectedTemplateIds.Contains templateId)
                )
                |> ignore

                setDeselectedTemplateColumns (fun deselected ->
                    deselected
                    |> Set.filter (fun (templateId, _) -> selectedTemplateIds.Contains templateId)
                )
                |> ignore

                setDeselectedTemplateRows (fun deselected ->
                    deselected
                    |> Set.filter (fun (templateId, _) -> selectedTemplateIds.Contains templateId)
                )
                |> ignore

                setEnteredNewTableNames (fun names ->
                    names
                    |> Map.filter (fun templateId _ -> selectedTemplateIds.Contains templateId)
                )
                |> ignore
            ),
            [| box selectedTemplateIds |]
        )

        let selectedTemplatesForImport =
            TemplateActions.selectedTemplatesForImport selectedTemplates selectedTemplateIds templateImportDecisions

        let newTableNames =
            TemplateActions.newTableNames existingTableNames selectedTemplatesForImport enteredNewTableNames

        let importInputs =
            importType,
            selectedTemplatesForImport
            |> Array.map (fun (template, action) -> template.Id, action),
            deselectedTemplateColumns,
            deselectedTemplateRows,
            enteredNewTableNames

        let submittedImportInputs, setSubmittedImportInputs = React.useState None

        let importError =
            importError
            |> Option.filter (fun _ -> submittedImportInputs = Some importInputs)

        let canConfirmImport =
            selectedTemplatesForImport.Length > 0
            && newTableNames |> Map.forall (fun _ newTableName -> newTableName.Error.IsNone)
            && selectedTemplatesForImport
               |> Array.forall (fun (template, _) ->
                   let columnCount = template.Table.ColumnCount

                   columnCount = 0
                   || Seq.init columnCount id
                      |> Seq.exists (fun columnIndex ->
                          TemplateActions.isTemplateColumnSelected template.Id columnIndex deselectedTemplateColumns
                      )
               )
            && selectedTemplatesForImport
               |> Array.forall (fun (template, _) ->
                   let rowCount = template.Table.RowCount

                   importType = TableJoinOptions.Headers
                   || rowCount = 0
                   || Seq.init rowCount id
                      |> Seq.exists (fun rowIndex -> not (deselectedTemplateRows.Contains(template.Id, rowIndex)))
               )

        let sources =
            React.useMemo (
                (fun () ->
                    selectedTemplates
                    |> Array.mapi (fun index template -> {
                        Index = index
                        Name = template.Name
                        Table = template.Table
                    })
                ),
                [| box selectedTemplates |]
            )

        let templateAt (index: int) = selectedTemplates.[index]

        let previewCallbacks: TableImportPreviewCallbacks = {
            GetImportAction =
                fun index -> TemplateActions.getTemplateImportAction (templateAt index).Id templateImportDecisions
            SetImportAction =
                fun index action ->
                    TemplateActions.setTemplateImportAction (templateAt index).Id action setTemplateImportDecisions
            IsColumnSelected =
                fun index columnIndex ->
                    TemplateActions.isTemplateColumnSelected (templateAt index).Id columnIndex deselectedTemplateColumns
            ToggleColumnSelection =
                fun index columnIndex ->
                    TemplateActions.toggleTemplateColumnSelection
                        (templateAt index).Id
                        columnIndex
                        setDeselectedTemplateColumns
            SelectAllColumns =
                fun index -> TemplateActions.selectAllTemplateColumns (templateAt index).Id setDeselectedTemplateColumns
            UnselectAllColumns =
                fun index _ ->
                    TemplateActions.unselectAllTemplateColumns (templateAt index) setDeselectedTemplateColumns
            IsRowSelected = fun index rowIndex -> not (deselectedTemplateRows.Contains((templateAt index).Id, rowIndex))
            ToggleRowSelection =
                fun index rowIndex ->
                    TemplateActions.toggleTemplateRowSelection (templateAt index).Id rowIndex setDeselectedTemplateRows
            SelectAllRows =
                fun index -> TemplateActions.selectAllTemplateRows (templateAt index).Id setDeselectedTemplateRows
            UnselectAllRows =
                fun index _ -> TemplateActions.unselectAllTemplateRows (templateAt index) setDeselectedTemplateRows
            DeselectedRowCount =
                fun index -> TemplateActions.deselectedTemplateRowCount (templateAt index).Id deselectedTemplateRows
            GetNewTableName = fun index -> newTableNames.TryFind (templateAt index).Id
            SetNewTableName =
                fun index name ->
                    setEnteredNewTableNames (fun names -> names.Add((templateAt index).Id, name))
                    |> ignore
        }

        let confirmImport () =
            setSubmittedImportInputs (Some importInputs)

            submitImport {
                ImportType = importType
                SelectedTemplatesForImport = selectedTemplatesForImport
                DeselectedTemplateColumns = deselectedTemplateColumns
                DeselectedTemplateRows = deselectedTemplateRows
                NewTableNames = newTableNames |> Map.map (fun _ newTableName -> newTableName.Name)
            }

        BaseModal.Modal(
            debug = "template-import",
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = Html.text "Import templates",
            description = Html.text "Select an import mode before importing the selected templates.",
            children =
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        ImportModeSelect.ImportModeSelect(importType, setImportType)
                        Html.div [ prop.className "swt:divider swt:my-1" ]
                        Html.div [
                            prop.className "swt:text-xs swt:opacity-70"
                            prop.textf "Template preview (%d selected)" selectedTemplates.Length
                        ]
                        TableImportPreview.TableImportPreview(
                            sources,
                            previewCallbacks,
                            emptyText = "No templates selected.",
                            showRowSelection = (importType <> TableJoinOptions.Headers)
                        )
                        match importError with
                        | Some message ->
                            Html.div [
                                prop.role.alert
                                prop.className "swt:alert swt:alert-error swt:text-sm"
                                prop.text $"Import failed: {message}"
                            ]
                        | None -> Html.none
                    ]
                ],
            footer =
                Html.div [
                    prop.className "swt:flex swt:w-full swt:gap-2"
                    prop.children [
                        Html.button [
                            prop.testId "template-import-cancel"
                            prop.className "swt:btn swt:btn-outline"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> setIsOpen false)
                        ]
                        Html.button [
                            prop.testId "template-import-confirm"
                            prop.className "swt:btn swt:btn-primary swt:ml-auto"
                            prop.disabled (not canConfirmImport)
                            prop.text "Import"
                            prop.onClick (fun _ -> confirmImport ())
                        ]
                    ]
                ]
        )
