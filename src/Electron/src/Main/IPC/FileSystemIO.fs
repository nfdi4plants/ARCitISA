module Main.IPC.FileSystemIO

open System
open Fable.Core
open Fable.Core.JsInterop
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.RenamePathRules
open Main.Bindings.Filesystem
open Main.Bindings.Path


let private fsPromisesDynamic: obj = importAll "fs/promises"
let private pathDynamic: obj = importAll "path"

[<RequireQualifiedAccess>]
module ArcPathValidation =

    let normalizePathForComparison (pathValue: string) =
        PathHelpers.normalizePathForFsComparison pathValue

    let containsTraversalSegments (pathValue: string) =
        PathHelpers.containsPathTraversalSegments pathValue

    let isSafeRelativePathCandidate (pathValue: string) =
        let normalizedPath = PathHelpers.normalizePath pathValue

        not (String.IsNullOrWhiteSpace normalizedPath)
        && normalizedPath <> "."
        && not (pathDynamic?isAbsolute (normalizedPath) |> unbox<bool>)
        && not (containsTraversalSegments normalizedPath)

    let isWithinRootPath (rootPath: string) (candidatePath: string) =
        let normalizedRootPath =
            pathDynamic?resolve (rootPath) |> unbox<string> |> normalizePathForComparison

        let normalizedCandidatePath =
            pathDynamic?resolve (candidatePath)
            |> unbox<string>
            |> normalizePathForComparison

        normalizedCandidatePath = normalizedRootPath
        || normalizedCandidatePath.StartsWith(normalizedRootPath + "/")

let resolveAbsolutePath (pathValue: string) =
    pathDynamic?resolve (pathValue) |> unbox<string>

let tryGetArcRelativePath (arcPath: string) (requestedAbsolutePath: string) =
    let arcRoot = resolveAbsolutePath arcPath
    let absolutePath = resolveAbsolutePath requestedAbsolutePath

    let relativePath =
        pathDynamic?relative (arcRoot, absolutePath)
        |> unbox<string>
        |> PathHelpers.normalizePath

    if String.IsNullOrWhiteSpace relativePath || relativePath = "." then
        Ok ""
    elif not (ArcPathValidation.isSafeRelativePathCandidate relativePath) then
        Error(exn $"Path '{requestedAbsolutePath}' is outside the active ARC root.")
    elif not (ArcPathValidation.isWithinRootPath arcRoot absolutePath) then
        Error(exn $"Path '{requestedAbsolutePath}' is outside the active ARC root.")
    else
        Ok relativePath

/// Resolves a relative path against the ARC root and rejects absolute or traversal-based escapes.
let tryResolveArcRelativePath (arcPath: string) (requestedRelativePath: string) =
    let relativePath = PathHelpers.normalizePath requestedRelativePath

    if String.IsNullOrWhiteSpace relativePath then
        Error(exn "RelativePath must not be empty.")
    elif not (ArcPathValidation.isSafeRelativePathCandidate relativePath) then
        if pathDynamic?isAbsolute (relativePath) |> unbox<bool> then
            Error(exn "RelativePath must not be absolute.")
        else
            Error(exn "RelativePath must not contain path traversal segments.")
    else
        let arcRoot = resolveAbsolutePath arcPath
        let absolutePath = pathDynamic?resolve (arcRoot, relativePath) |> unbox<string>

        if ArcPathValidation.isWithinRootPath arcRoot absolutePath then
            Ok absolutePath
        else
            Error(exn "RelativePath resolves outside the ARC root.")


let pathExistsAsync (absolutePath: string) : JS.Promise<bool> = promise {
    let! fileExists = ARCtrl.FileSystemHelper.fileExistsAsync absolutePath

    if fileExists then
        return true
    else
        return! ARCtrl.FileSystemHelper.directoryExistsAsync absolutePath
}

let mkdirAsync (directoryPath: string) : JS.Promise<unit> =
    ARCtrl.FileSystemHelper.createDirectoryAsync directoryPath

type private FileSystemRetryStrategy = {
    DelaysMs: int[]
    IsTransientErrorCode: string option -> bool
}

// Windows file APIs can briefly report lock/contention errors while external processes still hold handles.
let private renameRetryStrategy = {
    DelaysMs = [| 0; 75; 200; 500 |]
    IsTransientErrorCode =
        function
        | Some "EPERM"
        | Some "EACCES"
        | Some "EBUSY" -> true
        | _ -> false
}

// Recursive directory deletion can briefly report non-empty or locked paths while Git/file watchers finish writes.
let private removeRetryStrategy = {
    DelaysMs = [| 0; 75; 200; 500 |]
    IsTransientErrorCode =
        function
        | Some "ENOTEMPTY"
        | Some "EPERM"
        | Some "EACCES"
        | Some "EBUSY" -> true
        | _ -> false
}

let private renameWithRetriesAsync
    (sourceAbsolutePath: string)
    (targetAbsolutePath: string)
    : JS.Promise<Result<unit, exn>> =
    let rec attempt (attemptIndex: int) = promise {
        if attemptIndex > 0 then
            do! Promise.sleep renameRetryStrategy.DelaysMs.[attemptIndex]

        try
            let! _ =
                fsPromisesDynamic?rename (sourceAbsolutePath, targetAbsolutePath)
                |> unbox<JS.Promise<obj>>

            return Ok()
        with renameError ->
            let errorCode = tryGetNodeErrorCode renameError

            if
                attemptIndex < renameRetryStrategy.DelaysMs.Length - 1
                && renameRetryStrategy.IsTransientErrorCode errorCode
            then
                return! attempt (attemptIndex + 1)
            else
                return Error renameError
    }

    attempt 0

let removePathWithRetriesAsync
    (removePathAsync: string -> JS.Promise<unit>)
    (absolutePath: string)
    : JS.Promise<Result<unit, exn>> =
    let rec attempt (attemptIndex: int) = promise {
        if attemptIndex > 0 then
            do! Promise.sleep removeRetryStrategy.DelaysMs.[attemptIndex]

        try
            do! removePathAsync absolutePath
            return Ok()
        with removeError ->
            let errorCode = tryGetNodeErrorCode removeError

            if
                attemptIndex < removeRetryStrategy.DelaysMs.Length - 1
                && removeRetryStrategy.IsTransientErrorCode errorCode
            then
                return! attempt (attemptIndex + 1)
            else
                return Error removeError
    }

    attempt 0

let private removeGenericFileSystemItemAsync absolutePath = promise {
    let! _ =
        fsPromisesDynamic?rm (absolutePath, createObj [ "recursive" ==> true; "force" ==> false ])
        |> unbox<JS.Promise<obj>>

    return ()
}

let mapRenameDiskError (sourcePath: string) (targetPath: string) (renameError: exn) =
    match tryGetNodeErrorCode renameError with
    | Some "EPERM"
    | Some "EACCES" ->
        exn
            $"Cannot rename '{sourcePath}' to '{targetPath}'. Windows reported a permission or file-lock conflict. If the destination already exists, choose a different name and close apps that may be using these paths."
    | Some "ENOTEMPTY"
    | Some "EEXIST" -> exn $"Cannot rename '{sourcePath}' to '{targetPath}' because the destination already exists."
    | Some "ENOENT" -> exn $"Cannot rename '{sourcePath}' because the source path no longer exists on disk."
    | _ -> renameError

[<RequireQualifiedAccess>]
module ArcFileSystemHelper =

    exception private ImportCancelledException

    type private ExternalFileImportEntry = { SourcePath: string; FileName: string }

    type private ExternalFileImportPlan = {
        TargetDirectory: string
        TemporaryDirectory: string
        Entries: ExternalFileImportEntry[]
    }

    let createTemporaryImportDirectoryName () =
        let guid = Guid.NewGuid().ToString("N")
        $".swate-import-{guid}"

    let private resolveArcRootOrRelativePath arcPath relativePath =
        let normalizedPath = relativePath |> PathHelpers.normalizeCanonicalRelativePath

        if String.IsNullOrWhiteSpace normalizedPath then
            Ok(resolveAbsolutePath arcPath)
        else
            tryResolveArcRelativePath arcPath normalizedPath

    let private ensureDirectory absolutePath errorMessage = promise {
        let! isDirectory = ARCtrl.FileSystemHelper.directoryExistsAsync absolutePath

        if isDirectory then
            return Ok absolutePath
        else
            return Error(exn errorMessage)
    }

    let private ensureTargetDoesNotExist targetAbsolutePath errorMessage = promise {
        let! targetExists = pathExistsAsync targetAbsolutePath

        if targetExists then
            return Error(exn errorMessage)
        else
            return Ok()
    }

    let private resolveImportTargetDirectory arcPath targetRelativePath = promise {
        match resolveArcRootOrRelativePath arcPath targetRelativePath with
        | Error pathError -> return Error pathError
        | Ok targetDirectory ->
            return! ensureDirectory targetDirectory $"Cannot import because '{targetRelativePath}' is not a folder."
    }

    let private createExternalFileImportPlan targetDirectory sourcePaths = promise {
        let entries =
            sourcePaths
            |> Array.map (fun sourcePath ->
                let absolutePath = resolveAbsolutePath sourcePath

                {
                    SourcePath = absolutePath
                    FileName = basename absolutePath
                }
            )

        let duplicateName =
            entries
            |> Array.groupBy (fun entry -> entry.FileName.ToLowerInvariant())
            |> Array.tryFind (fun (_, duplicates) -> duplicates.Length > 1)

        match duplicateName with
        | Some(_, duplicates) -> raise (exn $"Cannot import multiple files named '{duplicates.[0].FileName}'.")
        | None -> ()

        for entry in entries do
            let destinationPath = join [| targetDirectory; entry.FileName |]

            match!
                ensureTargetDoesNotExist
                    destinationPath
                    $"Cannot import '{entry.FileName}' because a file with that name already exists."
            with
            | Ok() -> ()
            | Error targetError -> raise targetError

        return {
            TargetDirectory = targetDirectory
            // The watcher explicitly ignores this operation-owned directory and its contents.
            TemporaryDirectory = join [| targetDirectory; createTemporaryImportDirectoryName () |]
            Entries = entries
        }
    }

    let private copyExternalFilesToTemporaryDirectory plan onProgress isCancellationRequested = promise {
        do! mkdirAsync plan.TemporaryDirectory
        onProgress 0.0

        for sourceIndex, entry in plan.Entries |> Array.indexed do
            if isCancellationRequested () then
                raise ImportCancelledException

            let temporaryPath = join [| plan.TemporaryDirectory; entry.FileName |]
            do! copyFileAsync entry.SourcePath temporaryPath 0
            onProgress (float (sourceIndex + 1) / float plan.Entries.Length)
    }

    let private copyTemporaryFilesIntoTarget
        (plan: ExternalFileImportPlan)
        (createdTargetPaths: ResizeArray<string>)
        isCancellationRequested
        =
        promise {
            for entry in plan.Entries do
                if isCancellationRequested () then
                    raise ImportCancelledException

                let temporaryPath = join [| plan.TemporaryDirectory; entry.FileName |]
                let destinationPath = join [| plan.TargetDirectory; entry.FileName |]

                try
                    do! linkAsync temporaryPath destinationPath
                with _ ->
                    // Hard links are an optional fast path. COPYFILE_EXCL provides a portable fallback
                    // while preserving the no-overwrite contract if linking is unavailable or denied.
                    do! copyFileAsync temporaryPath destinationPath copyFileExclusiveFlag

                createdTargetPaths.Add destinationPath

                // A fallback copy cannot be interrupted. Re-check immediately afterward so a
                // cancellation requested during the copy rolls back the registered destination.
                if isCancellationRequested () then
                    raise ImportCancelledException
        }

    let private cleanupExternalFileImport temporaryDirectory (createdTargetPaths: ResizeArray<string>) = promise {
        let cleanupErrors = ResizeArray<string>()

        for createdTargetPath in createdTargetPaths |> Seq.rev do
            match!
                removePathWithRetriesAsync (fun path -> rmAsync path (RmOptions(force = true))) createdTargetPath
            with
            | Ok() -> ()
            | Error cleanupError -> cleanupErrors.Add($"Could not remove '{createdTargetPath}': {cleanupError.Message}")

        match!
            removePathWithRetriesAsync
                (fun path -> rmAsync path (RmOptions(recursive = true, force = true)))
                temporaryDirectory
        with
        | Ok() -> ()
        | Error cleanupError ->
            cleanupErrors.Add(
                $"Could not remove temporary import directory '{temporaryDirectory}': {cleanupError.Message}"
            )

        return cleanupErrors |> Seq.toArray
    }

    let private externalFileImportFailureResult
        temporaryDirectory
        (createdTargetPaths: ResizeArray<string>)
        (importError: exn)
        =
        promise {
            let! cleanupErrors = cleanupExternalFileImport temporaryDirectory createdTargetPaths

            if cleanupErrors.Length > 0 then
                let cleanupMessage = String.concat " " cleanupErrors
                return Error(exn $"Import failed: {importError.Message} Cleanup also failed: {cleanupMessage}")
            elif importError :? ImportCancelledException then
                return Ok ImportExternalFilesResult.Cancelled
            else
                return Error importError
        }

    let importExternalFilesOnDiskWithTemporaryCleanup
        (removeTemporaryDirectory: string -> JS.Promise<unit>)
        (logTemporaryCleanupError: exn -> unit)
        (arcPath: string)
        (targetRelativePath: string)
        (sourcePaths: string[])
        (onProgress: float -> unit)
        (isCancellationRequested: unit -> bool)
        (onFilesPublished: unit -> unit)
        (validateImportedFiles: unit -> JS.Promise<Result<unit, exn>>)
        : JS.Promise<Result<ImportExternalFilesResult, exn>> =
        promise {
            match! resolveImportTargetDirectory arcPath targetRelativePath with
            | Error pathError -> return Error pathError
            | Ok targetDirectory ->
                try
                    let! plan = createExternalFileImportPlan targetDirectory sourcePaths
                    let createdTargetPaths = ResizeArray<string>()

                    try
                        do! copyExternalFilesToTemporaryDirectory plan onProgress isCancellationRequested
                        do! copyTemporaryFilesIntoTarget plan createdTargetPaths isCancellationRequested
                        onFilesPublished ()

                        match! removePathWithRetriesAsync removeTemporaryDirectory plan.TemporaryDirectory with
                        | Ok() -> ()
                        | Error cleanupError -> logTemporaryCleanupError cleanupError

                        match! validateImportedFiles () with
                        | Error validationError -> raise validationError
                        | Ok() -> ()

                        return Ok ImportExternalFilesResult.Completed
                    with importError ->
                        return! externalFileImportFailureResult plan.TemporaryDirectory createdTargetPaths importError
                with planError ->
                    return Error planError
        }

    let importExternalFilesOnDisk =
        importExternalFilesOnDiskWithTemporaryCleanup
            (fun temporaryDirectory -> rmAsync temporaryDirectory (RmOptions(recursive = true, force = true)))
            (fun cleanupError -> printfn $"Unable to remove completed import staging directory: {cleanupError.Message}")

    type CreateFileSystemItemPlan = {
        ParentPath: string
        TargetPath: string
        Kind: FileSystemItemKind
    }

    type GenericRenamePlan = {
        SourcePath: string
        TargetPath: string
    }

    type GenericMovePlan = {
        SourcePath: string
        TargetPath: string
        Overwrite: bool
    }

    let private resolveArcRelativePathPair arcPath firstRelativePath secondRelativePath =
        match
            tryResolveArcRelativePath arcPath firstRelativePath, tryResolveArcRelativePath arcPath secondRelativePath
        with
        | Error pathError, _
        | _, Error pathError -> Error pathError
        | Ok firstAbsolutePath, Ok secondAbsolutePath -> Ok(firstAbsolutePath, secondAbsolutePath)

    let private resolveCreatePathPair arcPath parentRelativePath targetRelativePath =
        match
            resolveArcRootOrRelativePath arcPath parentRelativePath,
            tryResolveArcRelativePath arcPath targetRelativePath
        with
        | Error pathError, _
        | _, Error pathError -> Error pathError
        | Ok parentAbsolutePath, Ok targetAbsolutePath -> Ok(parentAbsolutePath, targetAbsolutePath)

    let private createTargetAsync kind targetAbsolutePath =
        match kind with
        | FileSystemItemKind.File -> ARCtrl.FileSystemHelper.writeFileTextAsync targetAbsolutePath ""
        | FileSystemItemKind.Folder -> mkdirAsync targetAbsolutePath

    let private renameResolvedPathOnDisk sourcePath targetPath sourceAbsolutePath targetAbsolutePath = promise {
        match! renameWithRetriesAsync sourceAbsolutePath targetAbsolutePath with
        | Ok() -> return Ok()
        | Error renameError -> return Error(mapRenameDiskError sourcePath targetPath renameError)
    }

    ///WIP must be simplified in future pr
    let private renameIgnoringErrorsAsync sourceAbsolutePath targetAbsolutePath = promise {
        try
            let! _ =
                fsPromisesDynamic?rename (sourceAbsolutePath, targetAbsolutePath)
                |> unbox<JS.Promise<obj>>

            return ()
        with _ ->
            return ()
    }

    let private moveFileIntoDescendantPathOnDisk sourcePath targetPath sourceAbsolutePath targetAbsolutePath = promise {
        let sourceParentAbsolutePath =
            pathDynamic?dirname (sourceAbsolutePath) |> unbox<string>

        let tempFileName = ".swate-move-" + Guid.NewGuid().ToString("N") + ".tmp"

        let tempAbsolutePath =
            pathDynamic?join (sourceParentAbsolutePath, tempFileName) |> unbox<string>

        match! renameWithRetriesAsync sourceAbsolutePath tempAbsolutePath with
        | Error renameError -> return Error(mapRenameDiskError sourcePath targetPath renameError)
        | Ok() ->
            let targetParentAbsolutePath =
                pathDynamic?dirname (targetAbsolutePath) |> unbox<string>

            try
                do! mkdirAsync targetParentAbsolutePath

                match! renameWithRetriesAsync tempAbsolutePath targetAbsolutePath with
                | Ok() -> return Ok()
                | Error moveError ->
                    do! renameIgnoringErrorsAsync tempAbsolutePath sourceAbsolutePath
                    return Error(mapRenameDiskError sourcePath targetPath moveError)
            with moveError ->
                do! renameIgnoringErrorsAsync tempAbsolutePath sourceAbsolutePath
                return Error moveError
    }

    let tryBuildCreateFileSystemItemPlan
        (request: CreateFileSystemItemRequest)
        : Result<CreateFileSystemItemPlan, exn> =
        match
            tryBuildGenericFileSystemChildPath (request.kind = FileSystemItemKind.File) request.parentPath request.name
        with
        | Error validationError -> Error(exn validationError)
        | Ok targetPath ->
            Ok {
                ParentPath = PathHelpers.normalizeCanonicalRelativePath request.parentPath
                TargetPath = targetPath
                Kind = request.kind
            }

    let createFileSystemItemOnDisk
        (arcPath: string)
        (request: CreateFileSystemItemRequest)
        : JS.Promise<Result<string, exn>> =
        promise {
            match tryBuildCreateFileSystemItemPlan request with
            | Error validationError -> return Error validationError
            | Ok plan ->
                match resolveCreatePathPair arcPath plan.ParentPath plan.TargetPath with
                | Error pathError -> return Error pathError
                | Ok(parentAbsolutePath, targetAbsolutePath) ->
                    try
                        match!
                            ensureDirectory
                                parentAbsolutePath
                                $"Cannot create item because '{plan.ParentPath}' is not a folder."
                        with
                        | Error directoryError -> return Error directoryError
                        | Ok _ ->
                            match!
                                ensureTargetDoesNotExist
                                    targetAbsolutePath
                                    $"A file or folder already exists at '{plan.TargetPath}'."
                            with
                            | Error targetError -> return Error targetError
                            | Ok() ->
                                do! createTargetAsync plan.Kind targetAbsolutePath
                                return Ok plan.TargetPath
                    with createError ->
                        return Error createError
        }

    /// True when `relativePath` names a file directly inside an add zone (for example `studies/notes.md`
    /// or `studies/.DS_Store`). Most such paths have the shape of entity folders, so the IPC layer routes
    /// them to generic file operations, and renames of them are checked with the file rules.
    let isAddZoneRootFileAsync (arcPath: string) (relativePath: string) : JS.Promise<bool> = promise {
        let normalizedRelativePath =
            relativePath |> PathHelpers.normalizeCanonicalRelativePath

        if ArcEntityPathRules.isAddZoneRootChildPath normalizedRelativePath then
            match tryResolveArcRelativePath arcPath normalizedRelativePath with
            | Ok absolutePath -> return! ARCtrl.FileSystemHelper.fileExistsAsync absolutePath
            | Error _ -> return false
        else
            return false
    }

    [<RequireQualifiedAccess>]
    type DeletePathRoute =
        | GenericItem of normalizedRelativePath: string
        | ArcEntity of normalizedRelativePath: string
        | DataMap of normalizedDataMapPath: string
        | Rejected of message: string

    let private genericDeleteRejected =
        DeletePathRoute.Rejected "Deletion is only allowed for safe non-ARC filesystem items inside the ARC."

    /// Chooses the delete operation for `relativePath`. Files directly inside an add zone have the
    /// path shape of entity folders, so the disk is checked before the ARC entity route is chosen.
    let resolveDeletePathRouteAsync (arcPath: string) (relativePath: string) : JS.Promise<DeletePathRoute> = promise {
        let normalizedRelativePath =
            relativePath |> PathHelpers.normalizeCanonicalRelativePath

        let! isAddZoneRootFile = isAddZoneRootFileAsync arcPath normalizedRelativePath

        match ArcEntityPathRules.classifyDeleteTarget normalizedRelativePath with
        | ArcEntityPathRules.DeletePathClassification.EntityFolderTarget _ when isAddZoneRootFile ->
            if ArcEntityPathRules.isGenericFileSystemFileTargetAllowed normalizedRelativePath then
                return DeletePathRoute.GenericItem normalizedRelativePath
            else
                return genericDeleteRejected
        | ArcEntityPathRules.DeletePathClassification.EntityFolderTarget _
        | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.EntityFile _,
                                                                          _) ->
            return DeletePathRoute.ArcEntity normalizedRelativePath
        | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.DataMapFile _,
                                                                          normalizedDataMapPath) ->
            return DeletePathRoute.DataMap normalizedDataMapPath
        | ArcEntityPathRules.DeletePathClassification.GenericTarget normalizedGenericPath
        | ArcEntityPathRules.DeletePathClassification.AddZoneDescendantTarget(_, normalizedGenericPath) ->
            if ArcEntityPathRules.isDeletePathAllowed normalizedGenericPath then
                return DeletePathRoute.GenericItem normalizedGenericPath
            else
                return genericDeleteRejected
        | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.InvestigationFile,
                                                                          _) ->
            return DeletePathRoute.Rejected "Deleting the investigation file is not supported."
        | ArcEntityPathRules.DeletePathClassification.ProtectedTarget _ ->
            return
                DeletePathRoute.Rejected "Deleting protected files (for example .gitkeep or readme.md) is not allowed."
        | ArcEntityPathRules.DeletePathClassification.DisallowedTarget _ -> return genericDeleteRejected
    }

    /// True when `relativePath` is renamed with the generic rename instead of the ARC entity rename,
    /// with the same add-zone file check as delete.
    let isGenericRenamePathAsync (arcPath: string) (relativePath: string) : JS.Promise<bool> = promise {
        match ArcEntityPathRules.classifyRenameTarget relativePath with
        | ArcEntityPathRules.RenamePathClassification.GenericTarget _ -> return true
        | ArcEntityPathRules.RenamePathClassification.EntityFolderTarget _ ->
            return! isAddZoneRootFileAsync arcPath relativePath
        | _ -> return false
    }

    let tryBuildGenericRenamePlan
        (isAddZoneRootFile: bool)
        (request: RenamePathRequest)
        : Result<GenericRenamePlan, exn> =
        let requestedRelativePath =
            request.relativePath |> PathHelpers.normalizeCanonicalRelativePath

        match tryBuildGenericFileSystemRenameTargetPath isAddZoneRootFile requestedRelativePath request.newName with
        | Error validationError -> Error(exn validationError)
        | Ok targetPath ->
            Ok {
                SourcePath = requestedRelativePath
                TargetPath = targetPath
            }

    let renameGenericFileSystemItemOnDisk
        (arcPath: string)
        (request: RenamePathRequest)
        : JS.Promise<Result<unit, exn>> =
        promise {
            let! isAddZoneRootFile = isAddZoneRootFileAsync arcPath request.relativePath

            match tryBuildGenericRenamePlan isAddZoneRootFile request with
            | Error validationError -> return Error validationError
            | Ok genericRenamePlan ->
                match resolveArcRelativePathPair arcPath genericRenamePlan.SourcePath genericRenamePlan.TargetPath with
                | Error pathError -> return Error pathError
                | Ok(sourceAbsolutePath, targetAbsolutePath) ->
                    let! targetExists = pathExistsAsync targetAbsolutePath

                    if targetExists then
                        return
                            Error(
                                exn
                                    $"Cannot rename '{genericRenamePlan.SourcePath}' to '{genericRenamePlan.TargetPath}' because the destination already exists."
                            )
                    else
                        return!
                            renameResolvedPathOnDisk
                                genericRenamePlan.SourcePath
                                genericRenamePlan.TargetPath
                                sourceAbsolutePath
                                targetAbsolutePath
        }

    let tryBuildGenericMovePlan (request: MovePathRequest) : Result<GenericMovePlan, exn> =
        let sourcePath =
            ArcEntityPathRules.tryNormalizeGenericFileSystemTarget
                "Generic filesystem move is only supported for safe non-entity source paths."
                request.sourceRelativePath

        let targetPath =
            ArcEntityPathRules.tryNormalizeGenericFileSystemTarget
                "Generic filesystem move targets must stay inside safe non-entity ARC paths."
                request.targetRelativePath

        match sourcePath, targetPath with
        | Error validationError, _
        | _, Error validationError -> Error(exn validationError)
        | Ok sourcePath, Ok targetPath when PathHelpers.pathsEqual sourcePath targetPath ->
            Error(exn "Move target is identical to the current path.")
        | Ok sourcePath, Ok targetPath ->
            Ok {
                SourcePath = sourcePath
                TargetPath = targetPath
                Overwrite = request.overwrite
            }

    let moveGenericFileSystemItemOnDisk (arcPath: string) (request: MovePathRequest) : JS.Promise<Result<unit, exn>> = promise {
        match tryBuildGenericMovePlan request with
        | Error validationError -> return Error validationError
        | Ok genericMovePlan ->
            match resolveArcRelativePathPair arcPath genericMovePlan.SourcePath genericMovePlan.TargetPath with
            | Error pathError -> return Error pathError
            | Ok(sourceAbsolutePath, targetAbsolutePath) ->
                let moveToTargetAsync () = promise {
                    let targetParentAbsolutePath =
                        pathDynamic?dirname (targetAbsolutePath) |> unbox<string>

                    do! mkdirAsync targetParentAbsolutePath

                    return!
                        renameResolvedPathOnDisk
                            genericMovePlan.SourcePath
                            genericMovePlan.TargetPath
                            sourceAbsolutePath
                            targetAbsolutePath
                }

                let! sourceExists = pathExistsAsync sourceAbsolutePath

                if sourceExists |> not then
                    return Error(exn $"Cannot move '{genericMovePlan.SourcePath}' because it does not exist.")
                else
                    let! sourceIsDirectory = ARCtrl.FileSystemHelper.directoryExistsAsync sourceAbsolutePath

                    if
                        sourceIsDirectory
                        && PathHelpers.isSameOrDescendantPath genericMovePlan.TargetPath genericMovePlan.SourcePath
                    then
                        return Error(exn "Move target must not be inside the source path.")
                    else
                        let! targetExists = pathExistsAsync targetAbsolutePath

                        match targetExists, genericMovePlan.Overwrite with
                        | true, false ->
                            return
                                Error(
                                    exn
                                        $"Cannot move '{genericMovePlan.SourcePath}' to '{genericMovePlan.TargetPath}' because the destination already exists."
                                )
                        | true, true ->
                            match! removePathWithRetriesAsync removeGenericFileSystemItemAsync targetAbsolutePath with
                            | Error removeError -> return Error removeError
                            | Ok() -> return! moveToTargetAsync ()
                        | false, _ when
                            PathHelpers.isSameOrDescendantPath genericMovePlan.TargetPath genericMovePlan.SourcePath
                            ->
                            return!
                                moveFileIntoDescendantPathOnDisk
                                    genericMovePlan.SourcePath
                                    genericMovePlan.TargetPath
                                    sourceAbsolutePath
                                    targetAbsolutePath
                        | false, _ -> return! moveToTargetAsync ()
    }

    /// Removes the item of a delete route from disk. Only routes built by `resolveDeletePathRouteAsync`
    /// are safe here, so this stays private and `deleteGenericFileSystemItemOnDisk` is the entry point.
    let private deleteGenericRouteOnDisk (arcPath: string) (route: DeletePathRoute) : JS.Promise<Result<unit, exn>> = promise {
        let genericDeletePath =
            match route with
            | DeletePathRoute.GenericItem normalizedRelativePath -> Ok normalizedRelativePath
            | DeletePathRoute.DataMap normalizedDataMapPath when
                ArcEntityPathRules.isDeletePathAllowed normalizedDataMapPath
                ->
                Ok normalizedDataMapPath
            | DeletePathRoute.Rejected message -> Error message
            | DeletePathRoute.DataMap _
            | DeletePathRoute.ArcEntity _ ->
                Error "Generic filesystem delete is only supported for safe non-entity paths."

        match genericDeletePath with
        | Error message -> return Error(exn message)
        | Ok normalizedRelativePath ->
            match tryResolveArcRelativePath arcPath normalizedRelativePath with
            | Error pathError -> return Error pathError
            | Ok absolutePath -> return! removePathWithRetriesAsync removeGenericFileSystemItemAsync absolutePath
    }

    let deleteGenericFileSystemItemOnDisk (arcPath: string) (relativePath: string) : JS.Promise<Result<unit, exn>> = promise {
        let! route = resolveDeletePathRouteAsync arcPath relativePath
        return! deleteGenericRouteOnDisk arcPath route
    }
