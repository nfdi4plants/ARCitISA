module Swate.Electron.Shared.FileIOTypes

open System.Collections.Generic
open Fable.Core
open Swate.Electron.Shared.VersionControlTypes

type FileEntry = {
    name: string
    isDirectory: bool
    path: string
}

[<AutoOpen>]
module FileEntryExtensions =

    let createFileEntryTree (fileEntries: FileEntry[]) =
        let dic = Dictionary<string, FileEntry>()
        fileEntries |> Array.iter (fun fileEntry -> dic.Add(fileEntry.path, fileEntry))
        dic

    type FileEntry with

        static member create(name: string, path: string, isDirectory: bool) : FileEntry = {
            name = name
            path = path
            isDirectory = isDirectory
        }

[<RequireQualifiedAccess; StringEnum>]
type FileContentType =
    | JSON
    | YAML
    | CWL
    | PlainText
    | Markdown
    | PDF
    | PNG
    | JPG
    | SVG
    | GIF
    | Html
    | ISA_Investigation
    | ISA_Study
    | ISA_Assay
    | ISA_Run
    | ISA_Workflow
    | ISA_Datamap
    | CLI
    /// A file without text preview. The noPreview field of the DTO holds its size and the reason.
    | NoPreview

/// Why a file has no preview.
[<RequireQualifiedAccess; StringEnum>]
type NoPreviewReason =
    | TooLarge
    | Binary

type FileContentDTO = {|
    fileType: FileContentType
    content: string
    path: string
    noPreview:
        {|
            sizeBytes: float
            reason: NoPreviewReason
        |} option
|}

type RenamePathRequest = {
    relativePath: string
    newName: string
}

type MovePathRequest = {
    sourceRelativePath: string
    targetRelativePath: string
    overwrite: bool
}

[<RequireQualifiedAccess>]
type FileSystemItemKind =
    | File
    | Folder

type CreateFileSystemItemRequest = {
    parentPath: string
    name: string
    kind: FileSystemItemKind
}

type ImportExternalFilesRequest = {
    requestId: string
    targetRelativePath: string
    authorizationId: string
}

[<RequireQualifiedAccess>]
type ImportExternalFilesResult =
    | Completed
    | Cancelled

[<RequireQualifiedAccess>]
type FileImportPhase =
    | Copying
    | Finalizing

[<RequireQualifiedAccess>]
type FileImportStep =
    | Scanning
    | Copying
    | Publishing

type FileImportProgress = {
    step: FileImportStep
    completedItems: int
    totalItems: int
}

module FileImportProgress =

    let overallFraction (progress: FileImportProgress) =
        let fractionDone =
            if progress.totalItems <= 0 then
                0.0
            else
                float progress.completedItems / float progress.totalItems

        match progress.step with
        | FileImportStep.Scanning -> 0.0
        | FileImportStep.Copying -> 0.5 * fractionDone
        | FileImportStep.Publishing -> 0.5 + 0.5 * fractionDone

type ActiveFileImportState = {
    requestId: string
    phase: FileImportPhase
    progress: FileImportProgress
}
