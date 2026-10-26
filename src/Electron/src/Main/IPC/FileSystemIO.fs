module Main.IPC.FileSystemIO

open System
open Fable.Core
open Fable.Core.JsInterop
open Main.ArcVaultTypes
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

let tryResolveRealPathAsync (absolutePath: string) : JS.Promise<string> = promise {
    try
        return! realpathAsync absolutePath
    with _ ->
        return absolutePath
}

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

let tryGetNodeErrorCode (error: exn) : string option =
    try
        error?code |> unbox<string> |> Option.ofObj
    with _ ->
        None

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

    type private ExternalFileImportEntry = {
        SourcePath: string
        RelativePath: string
        IsDirectory: bool
        IsGeneratedGitKeep: bool
    }

    type private ExternalFileImportPlan = {
        TargetDirectory: string
        TemporaryDirectory: string
        Entries: ExternalFileImportEntry[]
    }

    let private isTopLevelImportEntry (entry: ExternalFileImportEntry) =
        PathHelpers.tryGetParentPath entry.RelativePath |> Option.isNone

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

    let private importEntry sourcePath relativePath isDirectory = {
        SourcePath = sourcePath
        RelativePath = relativePath
        IsDirectory = isDirectory
        IsGeneratedGitKeep = false
    }

    /// Turns the errors of a source path that cannot be read, such as a missing link target or a denied
    /// access, into import messages. Other errors propagate unchanged.
    let private toImportResolutionError (displayPath: string) (error: exn) =
        match tryGetNodeErrorCode error with
        | Some "ENOENT" ->
            exn $"Cannot import '{displayPath}' because it or the item its link points to no longer exists."
        | Some "ELOOP" -> exn $"Cannot import '{displayPath}' because it goes through too many symbolic links."
        | Some "EPERM"
        | Some "EACCES" -> exn $"Cannot import '{displayPath}' because access to it was denied."
        | _ -> error

    /// Runs a file system call on a source path and turns the errors `toImportResolutionError` knows into import messages.
    let private resolveImportSourceAsync (displayPath: string) (operation: unit -> JS.Promise<'T>) : JS.Promise<'T> = promise {
        try
            return! operation ()
        with error ->
            return raise (toImportResolutionError displayPath error)
    }

    let private whitespaceNameImportError (path: string) =
        exn $"Cannot import '{path}' because its name starts or ends with a space."

    /// Picked items and the items inside picked folders pass the same name check.
    let private validateImportedName (displayPath: string) (name: string) =
        match validateRenameName name with
        | Error validationError -> raise (exn $"Cannot import '{displayPath}': {validationError}")
        | Ok trimmed when trimmed <> name -> raise (whitespaceNameImportError displayPath)
        | Ok _ -> ()

    /// Lists an imported source folder and everything inside it, every folder before its contents.
    /// `folderChain` holds the real paths of the folders on the way down, starting with the picked folder.
    /// Only a symbolic link needs a real path lookup. An ordinary child folder extends its parent's real path.
    /// A link to a folder that is the import target or contains it fails the import, like a picked folder does.
    /// A link to a folder whose real path is on the chain or above an entry of it links back to a folder that
    /// contains it and fails the import. An ordinary child folder fails when its real path is already on the chain.
    /// Two links to the same folder in different branches are not a cycle and both import in full.
    /// Where a real path cannot be resolved, the walk uses the path as given.
    /// The walk follows symbolic links. A link keeps its own name and the copy step reads what it points to.
    /// The walk accepts only regular files and folders, judged by the resolved type.
    /// Names inside one folder that differ only by case fail, because they collide on Windows and macOS.
    /// Every nested name passes the same name check as a picked item, so a name with a backslash, which
    /// Linux and macOS allow, cannot become a phantom folder in the ARC. A name with leading or trailing
    /// white space fails too, because ARCitISA trims paths when it renames or deletes.
    let rec private collectExternalFolderEntries
        isCancellationRequested
        (realTargetDirectory: string)
        (folderChain: string list)
        (sourceDirectory: string)
        (relativeDirectory: string)
        =
        promise {
            let! dirents =
                resolveImportSourceAsync
                    relativeDirectory
                    (fun () -> readdirWithTypesAsync sourceDirectory (ReaddirOptions(withFileTypes = true)))

            let entries = ResizeArray [ importEntry sourceDirectory relativeDirectory true ]
            let seenNames = System.Collections.Generic.HashSet<string>()

            for dirent in dirents |> Array.sortBy _.name do
                if isCancellationRequested () then
                    raise ImportCancelledException

                let sourcePath = join [| sourceDirectory; dirent.name |]
                let relativePath = $"{relativeDirectory}/{dirent.name}"

                validateImportedName relativePath dirent.name

                if not (seenNames.Add(dirent.name.ToLowerInvariant())) then
                    raise (
                        exn
                            $"Cannot import '{relativePath}' because its folder already contains an item with the same name, ignoring case."
                    )

                let! isDirectory, isFile =
                    if dirent.isSymbolicLink () then
                        resolveImportSourceAsync relativePath (fun () -> statAsync sourcePath)
                        |> Promise.map (fun stats -> stats.isDirectory (), stats.isFile ())
                    else
                        Promise.lift (dirent.isDirectory (), dirent.isFile ())

                if isDirectory then
                    let! realPath =
                        if dirent.isSymbolicLink () then
                            promise {
                                let! linkRealPath = tryResolveRealPathAsync sourcePath

                                if
                                    PathHelpers.isSameOrDescendantPathForFsComparison realTargetDirectory linkRealPath
                                then
                                    raise (
                                        exn
                                            $"Cannot import '{relativePath}' because it links to the import target or a folder that contains it."
                                    )

                                return linkRealPath
                            }
                        else
                            Promise.lift (join [| List.head folderChain; dirent.name |])

                    if
                        folderChain |> List.contains realPath
                        || (dirent.isSymbolicLink ()
                            && folderChain
                               |> List.exists (fun chainEntry ->
                                   PathHelpers.isSameOrDescendantPathForFsComparison chainEntry realPath
                               ))
                    then
                        raise (
                            exn $"Cannot import '{relativePath}' because it links back to a folder that contains it."
                        )

                    let! folderEntries =
                        collectExternalFolderEntries
                            isCancellationRequested
                            realTargetDirectory
                            (realPath :: folderChain)
                            sourcePath
                            relativePath

                    entries.AddRange folderEntries
                elif isFile then
                    entries.Add(importEntry sourcePath relativePath false)
                else
                    raise (exn $"Cannot import '{relativePath}' because it is neither a file nor a folder.")

            return entries.ToArray()
        }

    let private isImportIntoItselfAsync targetDirectory sourceDirectory = promise {
        if PathHelpers.isSameOrDescendantPathForFsComparison targetDirectory sourceDirectory then
            return true
        else
            let! realTargetDirectory = tryResolveRealPathAsync targetDirectory
            let! realSourceDirectory = tryResolveRealPathAsync sourceDirectory
            return PathHelpers.isSameOrDescendantPathForFsComparison realTargetDirectory realSourceDirectory
    }

    /// Checks one picked file or folder and returns its entry, without looking inside a folder.
    /// The pick needs a valid name and must be a regular file or folder after following links. Its name must not
    /// match an item in the target folder, ignoring case. `existingNames` maps the lower-case name of every item in
    /// the target folder to its actual name. A picked folder must not be the target or contain it.
    let private inspectPickedItemAsync
        targetDirectory
        (existingNames: System.Collections.Generic.Dictionary<string, string>)
        sourcePath
        =
        promise {
            let absolutePath = resolveAbsolutePath sourcePath
            let name = basename absolutePath

            if System.String.IsNullOrWhiteSpace name then
                raise (exn "Cannot import a whole drive. Pick a folder on the drive instead.")

            let! sourceStats = resolveImportSourceAsync name (fun () -> statAsync absolutePath)
            let isDirectory = sourceStats.isDirectory ()

            validateImportedName name name

            match existingNames.TryGetValue(name.ToLowerInvariant()) with
            | true, existingName when existingName = name ->
                raise (exn $"Cannot import '{name}' because an item with that name already exists.")
            | true, existingName ->
                raise (
                    exn
                        $"Cannot import '{name}' because '{existingName}' already exists there, and upper and lower case count as the same."
                )
            | _ -> ()

            if not isDirectory && not (sourceStats.isFile ()) then
                return raise (exn $"Cannot import '{name}' because it is neither a file nor a folder.")
            elif not isDirectory then
                return importEntry absolutePath name false
            else
                let! isIntoItself = isImportIntoItselfAsync targetDirectory absolutePath

                if isIntoItself then
                    return raise (exn $"Cannot import folder '{name}' into itself.")
                else
                    return importEntry absolutePath name true
        }

    let private createExternalFileImportPlan isCancellationRequested targetDirectory (sourcePaths: string[]) = promise {
        let! targetNames = readdirAsync targetDirectory
        let existingNames = System.Collections.Generic.Dictionary<string, string>()

        for targetName in targetNames do
            existingNames.[targetName.ToLowerInvariant()] <- targetName

        let pickedEntries = ResizeArray<ExternalFileImportEntry>()

        for sourcePath in sourcePaths do
            let! pickedEntry = inspectPickedItemAsync targetDirectory existingNames sourcePath
            pickedEntries.Add pickedEntry

        let duplicateName =
            pickedEntries.ToArray()
            |> Array.groupBy (fun entry -> entry.RelativePath.ToLowerInvariant())
            |> Array.tryFind (fun (_, duplicates) -> duplicates.Length > 1)

        match duplicateName with
        | Some(_, duplicates) -> raise (exn $"Cannot import multiple items named '{duplicates.[0].RelativePath}'.")
        | None -> ()

        let entries = ResizeArray<ExternalFileImportEntry>()
        let! realTargetDirectory = tryResolveRealPathAsync targetDirectory

        for pickedEntry in pickedEntries do
            if pickedEntry.IsDirectory then
                let! pickedRealPath = tryResolveRealPathAsync pickedEntry.SourcePath

                let! folderEntries =
                    collectExternalFolderEntries
                        isCancellationRequested
                        realTargetDirectory
                        [ pickedRealPath ]
                        pickedEntry.SourcePath
                        pickedEntry.RelativePath

                entries.AddRange folderEntries
            else
                entries.Add pickedEntry

        // Git does not track empty folders, so the import writes an empty `.gitkeep` into an imported folder
        // that would stay empty, like ARCtrl does for its own folders. The entries come after all folders,
        // which keeps every parent folder ahead of its contents.
        // One pass collects every folder that holds an entry, so large imports stay linear.
        let foldersWithContent =
            System.Collections.Generic.HashSet<string>(
                entries
                |> Seq.choose (fun entry ->
                    match entry.RelativePath.LastIndexOf '/' with
                    | -1 -> None
                    | index -> Some(entry.RelativePath.Substring(0, index))
                )
            )

        let isInsideHiddenFolder (relativePath: string) =
            relativePath.Split '/' |> Array.exists (isHiddenFileTreeEntry true)

        let emptyFolders =
            entries
            |> Seq.filter (fun entry ->
                entry.IsDirectory
                && not (foldersWithContent.Contains entry.RelativePath)
                && not (isInsideHiddenFolder entry.RelativePath)
            )
            |> Seq.toArray

        for folder in emptyFolders do
            entries.Add {
                SourcePath = ""
                RelativePath = folder.RelativePath + "/.gitkeep"
                IsDirectory = false
                IsGeneratedGitKeep = true
            }

        return {
            TargetDirectory = targetDirectory
            // The watcher ignores this folder while the import has it registered.
            TemporaryDirectory = join [| targetDirectory; createTemporaryImportDirectoryName () |]
            Entries = entries.ToArray()
        }
    }

    let private importEntryPath rootDirectory (entry: ExternalFileImportEntry) =
        Array.append [| rootDirectory |] (entry.RelativePath.Split '/') |> join

    /// Creates a folder that must not exist yet, so an import never merges into an existing folder.
    let private mkdirExclusiveAsync absolutePath = promise {
        let! _ = Main.Bindings.Filesystem.mkdirAsync absolutePath (MkdirOptions(recursive = false))
        return ()
    }

    let private copyExternalFilesToTemporaryDirectory plan onProgress isCancellationRequested = promise {
        do! mkdirAsync plan.TemporaryDirectory

        onProgress {
            step = FileImportStep.Copying
            completedItems = 0
            totalItems = plan.Entries.Length
        }

        for sourceIndex, entry in plan.Entries |> Array.indexed do
            if isCancellationRequested () then
                raise ImportCancelledException

            let temporaryPath = importEntryPath plan.TemporaryDirectory entry

            if entry.IsDirectory then
                do! mkdirExclusiveAsync temporaryPath
            elif entry.IsGeneratedGitKeep then
                do! ARCtrl.FileSystemHelper.writeFileTextAsync temporaryPath ""
            else
                do! copyFileAsync entry.SourcePath temporaryPath 0

            onProgress {
                step = FileImportStep.Copying
                completedItems = sourceIndex + 1
                totalItems = plan.Entries.Length
            }
    }

    let private copyTemporaryFilesIntoTarget
        (plan: ExternalFileImportPlan)
        (createdTargetPaths: ResizeArray<string>)
        onProgress
        isCancellationRequested
        =
        promise {
            onProgress {
                step = FileImportStep.Publishing
                completedItems = 0
                totalItems = plan.Entries.Length
            }

            for entryIndex, entry in plan.Entries |> Array.indexed do
                if isCancellationRequested () then
                    raise ImportCancelledException

                let temporaryPath = importEntryPath plan.TemporaryDirectory entry
                let destinationPath = importEntryPath plan.TargetDirectory entry

                if entry.IsDirectory then
                    do! mkdirExclusiveAsync destinationPath
                else
                    try
                        do! linkAsync temporaryPath destinationPath
                    with _ ->
                        // Hard links are an optional fast path. COPYFILE_EXCL provides a portable fallback
                        // while preserving the no-overwrite contract if linking is unavailable or denied.
                        do! copyFileAsync temporaryPath destinationPath copyFileExclusiveFlag

                if isTopLevelImportEntry entry then
                    createdTargetPaths.Add destinationPath

                // A fallback copy cannot be interrupted. Re-check immediately afterward so a
                // cancellation requested during the copy rolls back the registered destination.
                if isCancellationRequested () then
                    raise ImportCancelledException

                onProgress {
                    step = FileImportStep.Publishing
                    completedItems = entryIndex + 1
                    totalItems = plan.Entries.Length
                }
        }

    let private cleanupExternalFileImport temporaryDirectory (createdTargetPaths: ResizeArray<string>) = promise {
        let cleanupErrors = ResizeArray<string>()

        for createdTargetPath in createdTargetPaths |> Seq.rev do
            match!
                removePathWithRetriesAsync
                    (fun path -> rmAsync path (RmOptions(recursive = true, force = true)))
                    createdTargetPath
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

    /// Registers the staging folder with registerStagingDirectory before the import creates it, so the file
    /// watcher never sees its first write. The returned release function runs once after the last cleanup
    /// attempt. Its argument tells whether the folder still exists, so the caller can list a leftover folder.
    let importExternalFilesOnDiskWithTemporaryCleanup
        (registerStagingDirectory: string -> (bool -> unit))
        (removeTemporaryDirectory: string -> JS.Promise<unit>)
        (logTemporaryCleanupError: exn -> unit)
        (arcPath: string)
        (targetRelativePath: string)
        (sourcePaths: string[])
        (onProgress: FileImportProgress -> unit)
        (isCancellationRequested: unit -> bool)
        (onPathsPlanned: PlannedImportPaths -> unit)
        (onPublished: string[] -> unit)
        (validateImportedFiles: unit -> JS.Promise<Result<unit, exn>>)
        : JS.Promise<Result<ImportExternalFilesResult, exn>> =
        promise {
            match! resolveImportTargetDirectory arcPath targetRelativePath with
            | Error pathError -> return Error pathError
            | Ok targetDirectory ->
                try
                    onProgress {
                        step = FileImportStep.Scanning
                        completedItems = 0
                        totalItems = 0
                    }

                    let! plan = createExternalFileImportPlan isCancellationRequested targetDirectory sourcePaths

                    let createdTargetPaths = ResizeArray<string>()
                    let release: (bool -> unit) option ref = ref None

                    onPathsPlanned {
                        FolderPaths =
                            plan.Entries
                            |> Array.filter (fun entry -> entry.IsDirectory)
                            |> Array.map _.RelativePath
                        FilePaths =
                            plan.Entries
                            |> Array.filter (fun entry -> not entry.IsDirectory)
                            |> Array.map _.RelativePath
                    }

                    let! result = promise {
                        try
                            release.Value <- Some(registerStagingDirectory plan.TemporaryDirectory)
                            do! copyExternalFilesToTemporaryDirectory plan onProgress isCancellationRequested
                            do! copyTemporaryFilesIntoTarget plan createdTargetPaths onProgress isCancellationRequested

                            // Reports the absolute paths of the imported top-level folders.
                            plan.Entries
                            |> Array.filter (fun entry -> entry.IsDirectory && isTopLevelImportEntry entry)
                            |> Array.map (importEntryPath plan.TargetDirectory)
                            |> onPublished

                            match! removePathWithRetriesAsync removeTemporaryDirectory plan.TemporaryDirectory with
                            | Ok() -> ()
                            | Error cleanupError -> logTemporaryCleanupError cleanupError

                            match! validateImportedFiles () with
                            | Error validationError -> raise validationError
                            | Ok() -> ()

                            return Ok ImportExternalFilesResult.Completed
                        with importError ->
                            return!
                                externalFileImportFailureResult plan.TemporaryDirectory createdTargetPaths importError
                    }

                    match release.Value with
                    | Some releaseStaging ->
                        let! stagingStillExists = pathExistsAsync plan.TemporaryDirectory

                        release.Value <- None
                        releaseStaging stagingStillExists
                    | None -> ()

                    return result
                with
                | ImportCancelledException -> return Ok ImportExternalFilesResult.Cancelled
                | planError -> return Error planError
        }

    let importExternalFilesOnDisk (registerStagingDirectory: string -> (bool -> unit)) =
        importExternalFilesOnDiskWithTemporaryCleanup
            registerStagingDirectory
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

    /// True when `relativePath` exists and is not a directory, which is how the file tree tells files from folders.
    /// A symbolic link counts by itself, so a link to a folder is a file here.
    let private isFileAsync (arcPath: string) (relativePath: string) : JS.Promise<bool> = promise {
        match tryResolveArcRelativePath arcPath (relativePath |> PathHelpers.normalizeCanonicalRelativePath) with
        | Ok absolutePath ->
            try
                let! stats = lstatAsync absolutePath
                return not (stats.isDirectory ())
            with _ ->
                return false
        | Error _ -> return false
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
        (isFile: bool)
        (isAddZoneRootFile: bool)
        (request: RenamePathRequest)
        : Result<GenericRenamePlan, exn> =
        let requestedRelativePath =
            request.relativePath |> PathHelpers.normalizeCanonicalRelativePath

        match
            tryBuildGenericFileSystemRenameTargetPath isFile isAddZoneRootFile requestedRelativePath request.newName
        with
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
            let! isFile = isFileAsync arcPath request.relativePath

            let isAddZoneRootFile =
                isFile
                && ArcEntityPathRules.isAddZoneRootChildPath (
                    request.relativePath |> PathHelpers.normalizeCanonicalRelativePath
                )

            match tryBuildGenericRenamePlan isFile isAddZoneRootFile request with
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
