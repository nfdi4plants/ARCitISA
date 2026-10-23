module Swate.Electron.Shared.FileIOTypes

open System.Collections.Generic
open Fable.Core
open Swate.Electron.Shared.VersionControlTypes

type FileEntry = {
    name: string
    isDirectory: bool
    path: string
    largeObject: ObjectStateDto option
}

[<AutoOpen>]
module FileEntryExtensions =

    let createFileEntryTree (fileEntries: FileEntry[]) =
        let dic = Dictionary<string, FileEntry>()
        fileEntries |> Array.iter (fun fileEntry -> dic.Add(fileEntry.path, fileEntry))
        dic

    type FileEntry with

        static member create
            (name: string, path: string, isDirectory: bool, ?largeObject: ObjectStateDto option)
            : FileEntry =
            {
                name = name
                path = path
                isDirectory = isDirectory
                largeObject = defaultArg largeObject None
            }

type FileTreeNode = {
    name: string
    isDirectory: bool
    path: string
    largeObject: ObjectStateDto option
    children: Dictionary<string, FileTreeNode>
} with

    static member create
        (
            name: string,
            isDirectory: bool,
            path: string,
            children: Dictionary<string, FileTreeNode>,
            ?largeObject: ObjectStateDto option
        ) =
        {
            name = name
            isDirectory = isDirectory
            path = path
            largeObject = defaultArg largeObject None
            children = children
        }

[<RequireQualifiedAccess; StringEnum>]
type FileContentType =
    | JSON
    | YAML
    | CWL
    | PlainText
    | Markdown
    | PDF
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

type ActiveFileImportState = {
    requestId: string
    phase: FileImportPhase
}
