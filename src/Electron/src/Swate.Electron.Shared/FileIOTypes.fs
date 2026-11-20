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

/// The encoding a text file was read in. The renderer sends it back unchanged with a save.
[<RequireQualifiedAccess; StringEnum>]
type TextFileEncoding =
    | Utf8
    | Utf8Bom
    | Utf16LE
    | Utf16BE
    /// Bytes that are not valid UTF-8, shown as Windows-1252. Such a file is read-only.
    | Windows1252

[<RequireQualifiedAccess; StringEnum>]
type TextReadOnlyReason =
    | NotUtf8
    | TooLargeToEdit
    /// A UTF-16 file with unpaired surrogates or a truncated last unit. A save could not reproduce its bytes.
    | InvalidUtf16
    /// The file grew while it was read, so the content may be incomplete.
    | ChangedWhileReading
    /// The path is a symbolic link. A save would replace the link with a file.
    | SymbolicLink
    /// The file or its folder resolves to a place outside the open ARC.
    | OutsideArc
    /// The file has no owner write permission.
    | WriteProtected

/// What the main process knows about a text file it read. contentHash is the lowercase hex SHA-256 of the raw bytes on disk, BOM included.
type TextDocumentInfo = {|
    encoding: TextFileEncoding
    contentHash: string
    sizeBytes: float
    readOnly: TextReadOnlyReason option
|}

/// expectedHash is the contentHash the renderer loaded, None when it expects the file not to exist. A missing file is created
/// when expectedHash is None or overwrite is set. An existing file is replaced when overwrite is set or the hash matches.
/// overwrite skips the hash comparison. It does not skip the read-only rules: Main refuses an existing file that the
/// editor would open read-only, whatever the renderer sends.
type SaveTextFileRequest = {|
    path: string
    content: string
    encoding: TextFileEncoding
    expectedHash: string option
    overwrite: bool
|}

[<RequireQualifiedAccess>]
type SaveTextFileOutcome =
    | Saved of TextDocumentInfo
    /// The save was refused because the file on disk is not the one the renderer loaded. This happens when the hash differs,
    /// when expectedHash is None but the file exists, when expectedHash is set but the file is missing, and when it was changed
    /// or replaced right before the rename. currentHash is None when the file is missing.
    | Conflict of currentHash: string option

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
    | Completed of importedFolderPaths: string[]
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
