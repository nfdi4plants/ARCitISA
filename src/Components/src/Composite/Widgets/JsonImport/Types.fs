module Swate.Components.Composite.Widgets.JsonImport.Types

open Swate.Components.Shared

type JsonImportFile = {
    FileName: string option
    Content: string
}

type JsonImportRequest = {
    /// The parsed source file.
    ImportedFile: ArcFiles
    /// A copy of the current file with the import applied.
    NextArcFile: ArcFiles
    SourceFileName: string option
    JsonFormat: JsonExportFormat
}

type JsonImportError = {
    Title: string
    Message: string
    Details: string option
}

type ParsedJsonFile = {
    Id: int
    File: ArcFiles
    FileName: string option
    Format: JsonExportFormat
}

type JsonImportSummary = {
    SourceFileName: string option
    CreatedTableNames: string list
    ModifiedTableNames: string list
    ReplacedFile: bool
}

[<RequireQualifiedAccess>]
type JsonImportWidgetState =
    | SelectingFile
    | ReadingFile
    | Reviewing of ParsedJsonFile

[<RequireQualifiedAccess>]
type JsonImportModalPhase =
    | Configuring
    | Importing
    | Succeeded of JsonImportSummary
    | Failed of message: string * canRetry: bool
