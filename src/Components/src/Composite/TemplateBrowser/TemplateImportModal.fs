namespace Swate.Components.Composite.Template

open Fable.Core
open Feliz
open ARCtrl
open Swate.Components.Shared
open Swate.Components
open Swate.Components.Primitive.BaseModal
open Swate.Components.Composite.TableImport
open Swate.Components.Composite.TableImport.Types

module TableHelper = Swate.Components.Composite.TableImport.Helper

[<Erase; Mangle(false)>]
type TemplateImportModal =

    [<ReactComponent(true)>]
    static member TemplateImportModal
        (
            isOpen,
            selectedTemplates: Template[],
            currentArcFile: ArcFiles,
            activeTableIndex: int option,
            setIsOpen,
            submitImport: TableImportDecisions -> unit,
            ?importError: string
        ) =

        let state, setState = React.useStateWithUpdater TemplateActions.defaultDecisions

        let previousIds = React.useRef (selectedTemplates |> Array.map _.Id)

        // The dialog stays mounted while the selection changes, so the choices move with their templates.
        React.useEffect (
            (fun () ->
                let nextIds = selectedTemplates |> Array.map _.Id

                if nextIds <> previousIds.current then
                    let previous = previousIds.current
                    setState (TemplateActions.remapDecisions previous nextIds)
                    previousIds.current <- nextIds
            ),
            [| box selectedTemplates |]
        )

        let decisions = {
            state with
                ActiveTableIndex = activeTableIndex
        }

        let sources =
            React.useMemo ((fun () -> TemplateActions.importSources selectedTemplates), [| box selectedTemplates |])

        let names =
            TableHelper.newTableNames
                (currentArcFile.ArcTables().TableNames)
                sources
                (TableHelper.actionOf decisions)
                decisions.EnteredNames

        let canConfirmImport =
            (TableHelper.validate currentArcFile sources decisions).IsNone

        let submittedDecisions, setSubmittedDecisions = React.useState None

        let importError =
            importError |> Option.filter (fun _ -> submittedDecisions = Some decisions)

        let confirmImport () =
            setSubmittedDecisions (Some decisions)
            submitImport decisions

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
                        TableImportForm.TableImportForm(
                            sources,
                            decisions,
                            setState,
                            names,
                            emptyText = "No templates selected.",
                            previewTitle = $"Template preview ({selectedTemplates.Length} selected)"
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
