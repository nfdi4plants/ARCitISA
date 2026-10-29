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

let sourcesOf (file: ArcFiles) : ImportSource[] =
    file.Tables()
    |> Seq.mapi (fun index table -> {
        Index = index
        Name = table.Name
        Table = table
    })
    |> Array.ofSeq

/// A file that can hold tables gets the tables of the JSON file. The import replaces any other file.
let isTableImport (current: ArcFiles) = current.CanCreateTables()

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

/// Name of the file type in the texts the user reads.
let fileTypeDisplayName (file: ArcFiles) =
    match file with
    | ArcFiles.Investigation _ -> "investigation"
    | ArcFiles.Study _ -> "study"
    | ArcFiles.Assay _ -> "assay"
    | ArcFiles.Run _ -> "run"
    | ArcFiles.Workflow _ -> "workflow"
    | ArcFiles.Template _ -> "template"
    | ArcFiles.DataMap _ -> "DataMap"

/// Applies the import to a copy and leaves the current file as it is.
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
        (TableHelper.validate current (sourcesOf parsed.File) decisions).IsNone
