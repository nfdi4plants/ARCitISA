module Renderer.Components.Helper.FileSystemHelper

open Fable.Core
open Arcitisa.Components.Shared
open Arcitisa.Electron.Shared.FileIOTypes

type TargetAvailability =
    | Empty
    | Exists

let private isAlreadyExistsError (error: exn) =
    error.Message.ToLowerInvariant().Contains("already exists")

let checkTargetAvailability (pathExists: string -> JS.Promise<Result<bool, exn>>) (targetPath: string) = promise {
    let normalizedTargetPath = PathHelpers.normalizePath targetPath

    match! pathExists normalizedTargetPath with
    | Ok true -> return Ok TargetAvailability.Exists
    | Ok false -> return Ok TargetAvailability.Empty
    | Error error -> return Error error
}

let ensureChildFolder
    (createFileSystemItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>)
    (parentPath: string)
    (folderName: string)
    =
    promise {
        match!
            createFileSystemItem {
                parentPath = parentPath
                name = folderName
                kind = FileSystemItemKind.Folder
            }
        with
        | Ok _ -> return Ok()
        | Error error when isAlreadyExistsError error -> return Ok()
        | Error error -> return Error error
    }

let ensureChildFolderForPath
    (createFileSystemItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>)
    (tryGetParentPath: string -> string option)
    (folderName: string)
    (path: string)
    =
    promise {
        match tryGetParentPath path with
        | None -> return Ok()
        | Some parentPath -> return! ensureChildFolder createFileSystemItem parentPath folderName
    }

let writeFileWithEnsuredChildFolder
    (writeFile: FileContentDTO -> JS.Promise<Result<unit, exn>>)
    (createFileSystemItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>)
    (tryGetParentPath: string -> string option)
    (folderName: string)
    (request: FileContentDTO)
    =
    promise {
        match! writeFile request with
        | Error error -> return Error error
        | Ok() -> return! ensureChildFolderForPath createFileSystemItem tryGetParentPath folderName request.path
    }
