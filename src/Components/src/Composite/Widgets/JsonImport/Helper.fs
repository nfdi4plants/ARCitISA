module Swate.Components.Composite.Widgets.JsonImport.Helper

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.TableImport.Types
open Swate.Components.Composite.Widgets.JsonImport.Types

module TableHelper = Swate.Components.Composite.TableImport.Helper

let formatDescription (format: JsonExportFormat) =
    match format with
    | JsonExportFormat.ARCtrl -> "The ARCtrl JSON format, as written by ARCtrl."
    | JsonExportFormat.ARCtrlCompressed -> "ARCtrl JSON in compressed form. Same content, smaller file."
    | JsonExportFormat.ISA -> "ISA-JSON, the exchange format of the ISA tools."
    | JsonExportFormat.ROCrate -> "RO-Crate JSON-LD metadata, as found in ARC RO-Crate exports."

/// The caller sets ActiveTableIndex.
let defaultDecisions: TableImportDecisions = {
    ImportType = TableJoinOptions.WithValues
    DefaultAction = TableImportAction.ImportAsNewTable
    Actions = Map.empty
    DeselectedColumns = Set.empty
    EnteredNames = Map.empty
    ActiveTableIndex = None
}

let sourcesOf (file: ArcFiles) : ImportSource[] =
    file.Tables()
    |> Seq.mapi (fun index table -> {
        Index = index
        Name = table.Name
        Table = table
    })
    |> Array.ofSeq

/// Assay, Study and Run files take the tables of the JSON file. All other files are replaced.
let isTableImport (current: ArcFiles) = current.CanCreateTables()

/// Lower-case file type name, as used in the import error messages of Json.fs.
let fileTypeLabel (file: ArcFiles) =
    Json.Import.arcFileTypeLabel file.RelatedArcFilesDiscriminate

let private fileMessage (verb: string) (fileName: string option) =
    match fileName with
    | Some name -> $"Could not {verb} \"{name}\"."
    | None -> $"Could not {verb} the selected file."

let parseError (fileName: string option) (exn: exn) : JsonImportError = {
    Title = "Could not import JSON"
    Message = fileMessage "read" fileName
    Details = Some exn.Message
}

let readError (fileName: string option) (exn: exn) : JsonImportError = {
    Title = "Could not import JSON"
    Message = fileMessage "open" fileName
    Details = Some exn.Message
}

/// Applies the import to a copy. The current file is never changed.
let applyImport
    (current: ArcFiles)
    (parsed: ParsedJsonFile)
    (decisions: TableImportDecisions)
    : Result<ArcFiles * JsonImportSummary, exn> =
    if isTableImport current then
        TableHelper.applyImport current (sourcesOf parsed.File) decisions
        |> Result.map (fun result ->
            result.ArcFile,
            {
                CreatedTableNames = result.CreatedTableNames
                ModifiedTableNames = result.ModifiedTableNames
                ReplacedFile = false
            }
        )
    else
        Json.Import.applyToCurrentArcFile (current, parsed.File)
        |> Result.map (fun next ->
            next,
            {
                CreatedTableNames = []
                ModifiedTableNames = []
                ReplacedFile = true
            }
        )

/// True when the user may start the import in the given phase with the given decisions.
let canImport
    (phase: JsonImportModalPhase)
    (current: ArcFiles)
    (parsed: ParsedJsonFile)
    (decisions: TableImportDecisions)
    =
    let phaseAllows =
        match phase with
        | JsonImportModalPhase.Configuring -> true
        | JsonImportModalPhase.Failed(_, canRetry) -> canRetry
        | JsonImportModalPhase.Importing
        | JsonImportModalPhase.Succeeded _ -> false

    if not phaseAllows then
        false
    elif not (isTableImport current) then
        true
    else
        let sources = sourcesOf parsed.File
        let actionOf = TableHelper.actionOf decisions

        let selected =
            sources
            |> Array.filter (fun source -> actionOf source.Index <> TableImportAction.NoImport)

        let names =
            TableHelper.newTableNames (current.ArcTables().TableNames) sources actionOf decisions.EnteredNames

        let hasSelectedColumn (source: ImportSource) =
            source.Table.ColumnCount = 0
            || Seq.init source.Table.ColumnCount id
               |> Seq.exists (TableHelper.isColumnSelected decisions source.Index)

        let appends =
            selected
            |> Array.exists (fun source -> actionOf source.Index = TableImportAction.AppendToActiveTable)

        selected.Length > 0
        && selected |> Array.forall hasSelectedColumn
        && names |> Map.forall (fun _ name -> name.Error.IsNone)
        && (not appends
            || (TableHelper.tryGetAppendTarget current decisions.ActiveTableIndex).IsSome)
