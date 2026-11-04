namespace Swate.Components.Composite.TableImport

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components.Shared.ARCtrlHelper
open Swate.Components.Composite.TableImport.Types

[<Erase; Mangle(false)>]
type TableImportForm =

    /// Says why the import cannot start. It goes next to the confirm button, because the hints of a table sit in its collapsed details.
    [<ReactComponent>]
    static member BlockedReason(reason: string) =
        Html.div [
            prop.testId "table-import-blocked-reason"
            prop.role.status
            prop.className "swt:flex swt:min-w-0 swt:items-center swt:gap-1 swt:text-xs swt:text-warning"
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--warning-12-filled swt:shrink-0"
                ]
                Html.span reason
            ]
        ]

    /// The import mode select and the table preview. The decisions change through setDecisions.
    [<ReactComponent>]
    static member TableImportForm
        (
            sources: ImportSource[],
            decisions: TableImportDecisions,
            setDecisions: (TableImportDecisions -> TableImportDecisions) -> unit,
            names: Map<int, NewTableName>,
            ?canAppend: bool,
            ?appendDisabledNote: string,
            ?emptyText: string,
            ?previewTitle: string,
            ?disabled: bool
        ) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                ImportModeSelect.ImportModeSelect(
                    decisions.ImportType,
                    (fun importType -> setDecisions (fun d -> { d with ImportType = importType })),
                    ?disabled = disabled
                )
                match previewTitle with
                | Some title ->
                    Html.div [ prop.className "swt:divider swt:my-1" ]

                    Html.div [
                        prop.className "swt:text-xs swt:opacity-70"
                        prop.text title
                    ]
                | None -> ()
                TableImportPreview.TableImportPreview(
                    sources,
                    Helper.previewCallbacks decisions setDecisions names,
                    ?canAppend = canAppend,
                    ?appendDisabledNote = appendDisabledNote,
                    ?emptyText = emptyText,
                    showRowSelection = (decisions.ImportType <> TableJoinOptions.Headers)
                )
            ]
        ]
