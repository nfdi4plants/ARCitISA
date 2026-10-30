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

        let isTemplateColumnSelected (templateId: System.Guid) (columnIndex: int) =
            TemplateActions.isTemplateColumnSelected templateId columnIndex deselectedTemplateColumns

        let isTemplateRowSelected (templateId: System.Guid) (rowIndex: int) =
            TemplateActions.isTemplateRowSelected templateId rowIndex deselectedTemplateRows

        let getTemplateImportAction (templateId: System.Guid) =
            TemplateActions.getTemplateImportAction templateId templateImportDecisions

        let setTemplateImportAction (templateId: System.Guid) (importAction: TableImportAction) =
            TemplateActions.setTemplateImportAction templateId importAction setTemplateImportDecisions

        let toggleTemplateColumnSelection (templateId: System.Guid) (columnIndex: int) =
            TemplateActions.toggleTemplateColumnSelection templateId columnIndex setDeselectedTemplateColumns

        let selectAllTemplateColumns (templateId: System.Guid) =
            TemplateActions.selectAllTemplateColumns templateId setDeselectedTemplateColumns

        let unselectAllTemplateColumns (template: Template) =
            TemplateActions.unselectAllTemplateColumns template setDeselectedTemplateColumns

        let toggleTemplateRowSelection (templateId: System.Guid) (rowIndex: int) =
            TemplateActions.toggleTemplateRowSelection templateId rowIndex setDeselectedTemplateRows

        let selectAllTemplateRows (templateId: System.Guid) =
            TemplateActions.selectAllTemplateRows templateId setDeselectedTemplateRows

        let unselectAllTemplateRows (template: Template) =
            TemplateActions.unselectAllTemplateRows template setDeselectedTemplateRows

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
                      |> Seq.exists (fun columnIndex -> isTemplateColumnSelected template.Id columnIndex)
               )
            && selectedTemplatesForImport
               |> Array.forall (fun (template, _) ->
                   let rowCount = template.Table.RowCount

                   importType = TableJoinOptions.Headers
                   || rowCount = 0
                   || Seq.init rowCount id
                      |> Seq.exists (fun rowIndex -> isTemplateRowSelected template.Id rowIndex)
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
            GetImportAction = fun index -> getTemplateImportAction (templateAt index).Id
            SetImportAction = fun index action -> setTemplateImportAction (templateAt index).Id action
            IsColumnSelected = fun index columnIndex -> isTemplateColumnSelected (templateAt index).Id columnIndex
            ToggleColumnSelection =
                fun index columnIndex -> toggleTemplateColumnSelection (templateAt index).Id columnIndex
            SelectAllColumns = fun index -> selectAllTemplateColumns (templateAt index).Id
            UnselectAllColumns = fun index _ -> unselectAllTemplateColumns (templateAt index)
            IsRowSelected = fun index rowIndex -> isTemplateRowSelected (templateAt index).Id rowIndex
            ToggleRowSelection = fun index rowIndex -> toggleTemplateRowSelection (templateAt index).Id rowIndex
            SelectAllRows = fun index -> selectAllTemplateRows (templateAt index).Id
            UnselectAllRows = fun index _ -> unselectAllTemplateRows (templateAt index)
            DeselectedRowCount =
                fun index ->
                    let id = (templateAt index).Id

                    deselectedTemplateRows
                    |> Seq.filter (fun (templateId, _) -> templateId = id)
                    |> Seq.length
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
