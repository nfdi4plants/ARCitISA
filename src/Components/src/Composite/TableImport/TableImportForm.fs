namespace Swate.Components.Composite.TableImport

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components.Shared.ARCtrlHelper
open Swate.Components.Composite.TableImport.Types

[<Erase; Mangle(false)>]
type TableImportForm =

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
