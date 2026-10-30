namespace Swate.Components.Composite.Widgets.JsonImport

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components.Shared
open Swate.Components.Primitive
open Swate.Components.Primitive.BaseModal
open Swate.Components.Primitive.LoadingSpinner
open Swate.Components.Composite.TableImport
open Swate.Components.Composite.TableImport.Types
open Swate.Components.Composite.Widgets.JsonImport.Types

module TableHelper = Swate.Components.Composite.TableImport.Helper
module JsonHelper = Swate.Components.Composite.Widgets.JsonImport.Helper

module private JsonImportModalHelper =

    let fileName (parsed: ParsedJsonFile) =
        parsed.FileName |> Option.defaultValue "the file"

    let modeLabel (importType: TableJoinOptions) =
        TableHelper.ImportMode.options
        |> Array.tryPick (fun (mode, _, label, _) -> if mode = importType then Some label else None)
        |> Option.defaultValue ""

    let description (parsed: ParsedJsonFile) =
        match parsed.FileName with
        | Some name -> $"{name}, {parsed.Format.AsStringRdbl}"
        | None -> parsed.Format.AsStringRdbl

    let isImporting (phase: JsonImportModalPhase) =
        match phase with
        | JsonImportModalPhase.Importing -> true
        | _ -> false

    let namesList (testId: string) (names: string list) =
        Html.ul [
            prop.testId testId
            prop.className "swt:list-disc swt:ml-6"
            prop.children [
                for name in names do
                    Html.li [ prop.key name; prop.text name ]
            ]
        ]

    let alertClass (kind: string) =
        $"swt:alert swt:alert-{kind} swt:text-sm"

open JsonImportModalHelper

[<Erase; Mangle(false)>]
type JsonImportModal =

    [<ReactComponent>]
    static member private TableConfiguration
        (
            parsed: ParsedJsonFile,
            currentArcFile: ArcFiles,
            decisions: TableImportDecisions,
            setDecisions: (TableImportDecisions -> TableImportDecisions) -> unit,
            importing: bool
        ) =
        let sources =
            React.useMemo ((fun () -> JsonHelper.sourcesOf parsed.File), [| box parsed |])

        let actionOf = TableHelper.actionOf decisions

        let names =
            TableHelper.newTableNames (currentArcFile.ArcTables().TableNames) sources actionOf decisions.EnteredNames

        let appendTarget =
            TableHelper.tryGetAppendTarget currentArcFile decisions.ActiveTableIndex

        let callbacks: TableImportPreviewCallbacks = {
            GetImportAction = actionOf
            SetImportAction =
                fun index action ->
                    setDecisions (fun d -> {
                        d with
                            Actions = d.Actions.Add(index, action)
                    })
            IsColumnSelected = TableHelper.isColumnSelected decisions
            ToggleColumnSelection =
                fun index columnIndex ->
                    setDecisions (fun d -> {
                        d with
                            DeselectedColumns =
                                if d.DeselectedColumns.Contains(index, columnIndex) then
                                    d.DeselectedColumns.Remove(index, columnIndex)
                                else
                                    d.DeselectedColumns.Add(index, columnIndex)
                    })
            SelectAllColumns =
                fun index ->
                    setDecisions (fun d -> {
                        d with
                            DeselectedColumns = d.DeselectedColumns |> Set.filter (fun (i, _) -> i <> index)
                    })
            UnselectAllColumns =
                fun index columnCount ->
                    setDecisions (fun d -> {
                        d with
                            DeselectedColumns =
                                Set.union
                                    d.DeselectedColumns
                                    (Set.ofList [ for c in 0 .. columnCount - 1 -> index, c ])
                    })
            IsRowSelected = TableHelper.isRowSelected decisions
            DeselectedRowCount = TableHelper.deselectedRowCount decisions
            ToggleRowSelection =
                fun index rowIndex ->
                    setDecisions (fun d -> {
                        d with
                            DeselectedRows =
                                if d.DeselectedRows.Contains(index, rowIndex) then
                                    d.DeselectedRows.Remove(index, rowIndex)
                                else
                                    d.DeselectedRows.Add(index, rowIndex)
                    })
            SelectAllRows =
                fun index ->
                    setDecisions (fun d -> {
                        d with
                            DeselectedRows = d.DeselectedRows |> Set.filter (fun (i, _) -> i <> index)
                    })
            UnselectAllRows =
                fun index rowCount ->
                    setDecisions (fun d -> {
                        d with
                            DeselectedRows =
                                Set.union d.DeselectedRows (Set.ofList [ for r in 0 .. rowCount - 1 -> index, r ])
                    })
            GetNewTableName = fun index -> names.TryFind index
            SetNewTableName =
                fun index name ->
                    setDecisions (fun d -> {
                        d with
                            EnteredNames = d.EnteredNames.Add(index, name)
                    })
        }

        let showRowSelection = decisions.ImportType <> TableJoinOptions.Headers

        // Names the kept rows when the user left out rows and the mode imports values.
        let rowsText (source: ImportSource) =
            let total = source.Table.RowCount

            let kept = max 0 (total - TableHelper.deselectedRowCount decisions source.Index)

            if showRowSelection && kept < total then
                $" ({kept} of {total} rows)"
            else
                ""

        let summaryLines =
            sources
            |> Array.choose (fun source ->
                match actionOf source.Index with
                | TableImportAction.NoImport -> None
                | TableImportAction.ImportAsNewTable ->
                    let name =
                        names.TryFind source.Index
                        |> Option.map (fun n -> n.Name.Trim())
                        |> Option.defaultValue source.Name

                    Some(source.Index, $"{source.Name}: new table \"{name}\"{rowsText source}")
                | TableImportAction.AppendToActiveTable ->
                    appendTarget
                    |> Option.map (fun (_, table) ->
                        source.Index, $"{source.Name}: append to \"{table.Name}\"{rowsText source}"
                    )
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                ImportModeSelect.ImportModeSelect(
                    decisions.ImportType,
                    (fun importType -> setDecisions (fun d -> { d with ImportType = importType })),
                    disabled = importing
                )
                TableImportPreview.TableImportPreview(
                    sources,
                    callbacks,
                    canAppend = appendTarget.IsSome,
                    appendDisabledNote = "Select a table in the editor first to append columns to it.",
                    showRowSelection = showRowSelection
                )
                Html.div [
                    prop.testId "json-import-summary"
                    prop.className "swt:text-sm swt:flex swt:flex-col"
                    prop.children [
                        for (index, text) in summaryLines do
                            Html.div [ prop.key index; prop.text text ]
                        Html.div [ prop.text $"Content: {modeLabel decisions.ImportType}" ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ReplacementConfiguration(parsed: ParsedJsonFile, currentArcFile: ArcFiles) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.div [
                    prop.className "swt:text-sm"
                    prop.children [
                        Html.div [ prop.text $"File: {fileName parsed}" ]
                        Html.div [ prop.text $"Format: {parsed.Format.AsStringRdbl}" ]
                    ]
                ]
                Html.div [
                    prop.testId "json-import-replace-warning"
                    prop.role.alert
                    prop.className (alertClass "warning")
                    prop.text
                        $"The current content of this {JsonHelper.fileTypeDisplayName currentArcFile} is replaced by the content of the file."
                ]
            ]
        ]

    [<ReactComponent>]
    static member private Success(parsed: ParsedJsonFile, summary: JsonImportSummary, currentArcFile: ArcFiles) =
        Html.div [
            prop.testId "json-import-success"
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:text-sm"
            prop.children [
                Html.div [ prop.text $"Imported {fileName parsed}." ]
                if summary.ReplacedFile then
                    Html.div [
                        prop.text $"The {JsonHelper.fileTypeDisplayName currentArcFile} was replaced."
                    ]
                else
                    Html.div [
                        prop.text $"New tables: {summary.CreatedTableNames.Length}"
                    ]

                    if not summary.CreatedTableNames.IsEmpty then
                        namesList "json-import-created-tables" summary.CreatedTableNames

                    Html.div [
                        prop.text $"Modified tables: {summary.ModifiedTableNames.Length}"
                    ]

                    if not summary.ModifiedTableNames.IsEmpty then
                        namesList "json-import-modified-tables" summary.ModifiedTableNames

                Html.div [
                    prop.className "swt:opacity-70"
                    prop.text "The changes are in the open ARC. Save the ARC to keep them."
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member JsonImportModal
        (
            parsed: ParsedJsonFile,
            currentArcFile: ArcFiles,
            activeTableIndex: int option,
            importJson: JsonImportRequest -> JS.Promise<Result<unit, exn>>,
            onBack: unit -> unit,
            onCancel: unit -> unit,
            onDone: unit -> unit
        ) =
        let phase, setPhase = React.useState JsonImportModalPhase.Configuring

        let decisions, setDecisions =
            React.useStateWithUpdater {
                TableHelper.defaultJsonDecisions with
                    ActiveTableIndex = activeTableIndex
            }

        let submitting = React.useRef false

        let tableImport = JsonHelper.isTableImport currentArcFile
        let importing = isImporting phase

        let confirm () =
            if not submitting.current then
                submitting.current <- true
                setPhase JsonImportModalPhase.Importing

                // The deferral lets React render the importing state before the apply step blocks the thread.
                promise {
                    do! Promise.sleep 0

                    match JsonHelper.applyImport currentArcFile parsed decisions with
                    | Error e ->
                        setPhase (JsonImportModalPhase.Failed(e.Message, true))
                        submitting.current <- false
                    | Ok(next, summary) ->
                        let! result =
                            promise {
                                return!
                                    importJson {
                                        ImportedFile = parsed.File
                                        NextArcFile = next
                                        SourceFileName = parsed.FileName
                                        JsonFormat = parsed.Format
                                    }
                            }
                            |> Promise.catch (fun e -> Error e)

                        submitting.current <- false

                        match result with
                        | Ok() -> setPhase (JsonImportModalPhase.Succeeded summary)
                        | Error e -> setPhase (JsonImportModalPhase.Failed(e.Message, false))
                }
                // Only the apply step can throw here, before the host sees the import, so a retry is safe.
                |> Promise.catch (fun e ->
                    submitting.current <- false
                    setPhase (JsonImportModalPhase.Failed(e.Message, true))
                )
                |> Promise.start

        let setIsOpen (isOpen: bool) =
            if not isOpen then
                match phase with
                | JsonImportModalPhase.Importing -> ()
                | JsonImportModalPhase.Succeeded _ -> onDone ()
                | _ -> onCancel ()

        let configuration () =
            if tableImport then
                JsonImportModal.TableConfiguration(parsed, currentArcFile, decisions, setDecisions, importing)
            else
                JsonImportModal.ReplacementConfiguration(parsed, currentArcFile)

        let body =
            match phase with
            | JsonImportModalPhase.Configuring -> configuration ()
            | JsonImportModalPhase.Importing ->
                Html.div [
                    prop.testId "json-import-progress"
                    prop.children [
                        LoadingSpinner.LoadingSpinner(
                            text = "Importing JSON data. Please do not close this dialog.",
                            size = DaisyuiSize.MD
                        )
                    ]
                ]
            | JsonImportModalPhase.Succeeded summary -> JsonImportModal.Success(parsed, summary, currentArcFile)
            | JsonImportModalPhase.Failed(message, canRetry) ->
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        Html.div [
                            prop.testId "json-import-error"
                            prop.role.alert
                            prop.className (alertClass "error")
                            prop.text $"Import failed: {message}"
                        ]
                        if canRetry then
                            configuration ()
                        else
                            Html.div [
                                prop.className "swt:text-sm"
                                prop.text "The editor may already show the imported data. Check it before you save."
                            ]
                    ]
                ]

        let footerButton (testId: string) (className: string) (text: string) (disabled: bool) (onClick: unit -> unit) =
            Html.button [
                prop.testId testId
                prop.type'.button
                prop.className className
                prop.disabled disabled
                prop.text text
                prop.onClick (fun _ -> onClick ())
            ]

        let footer =
            match phase with
            | JsonImportModalPhase.Succeeded _ ->
                Html.div [
                    prop.className "swt:flex swt:w-full swt:gap-2"
                    prop.children [
                        footerButton "json-import-done" "swt:btn swt:btn-primary swt:ml-auto" "Done" false onDone
                    ]
                ]
            | JsonImportModalPhase.Failed(_, false) ->
                Html.div [
                    prop.className "swt:flex swt:w-full swt:gap-2"
                    prop.children [
                        footerButton "json-import-close" "swt:btn swt:btn-primary swt:ml-auto" "Close" false onCancel
                    ]
                ]
            | _ ->
                let canImport = JsonHelper.canImport phase currentArcFile parsed decisions

                Html.div [
                    prop.className "swt:flex swt:w-full swt:gap-2"
                    prop.children [
                        footerButton "json-import-back" "swt:btn swt:btn-outline" "Back" importing onBack
                        footerButton "json-import-cancel" "swt:btn swt:btn-ghost" "Cancel" importing onCancel
                        footerButton
                            "json-import-confirm"
                            "swt:btn swt:btn-primary swt:ml-auto"
                            (if tableImport then "Import" else "Replace")
                            (importing || not canImport)
                            confirm
                    ]
                ]

        BaseModal.Modal(
            debug = "json-import",
            isOpen = true,
            dismissible = not importing,
            setIsOpen = setIsOpen,
            header = Html.text "Import JSON",
            description = Html.text (description parsed),
            children = body,
            footer = footer
        )
