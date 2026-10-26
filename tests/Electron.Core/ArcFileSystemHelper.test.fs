module ElectronCore.ArcFileSystemHelperTests

open System
open Fable.Core
open Fable.Core.JsInterop
open Main.Bindings.Path
open Main.IPC.FileSystemIO
open Main.ArcVaultTypes
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.RenamePathRules
open Vitest

let private fsPromisesDynamic: obj = importAll "fs/promises"
let private newGuidN () = Guid.NewGuid().ToString("N")

[<Emit("Object.assign(new Error($0), { code: $1 })")>]
let private nodeError (message: string) (code: string) : exn = jsNative

[<Emit("process.platform")>]
let private nodePlatform () : string = jsNative

[<Emit("typeof process.getuid === 'function' && process.getuid() === 0")>]
let private runsAsRoot () : bool = jsNative

type private ISpawnSyncResult =
    abstract error: exn option
    abstract status: int

[<Import("spawnSync", "node:child_process")>]
let private spawnSync (command: string, args: string[]) : ISpawnSyncResult = jsNative

[<Import("chmod", "fs/promises")>]
let private chmodAsync (path: string, mode: int) : JS.Promise<unit> = jsNative

[<Import("symlink", "fs/promises")>]
let private symlinkAsync (target: string, path: string) : JS.Promise<unit> = jsNative

let private hasMkfifo () =
    (spawnSync ("mkfifo", [| "--help" |])).error.IsNone

let private makeFifo (path: string) =
    (spawnSync ("mkfifo", [| path |])).status

/// File modes and special files behave differently on Windows, and root ignores file modes.
let private skipOnWindowsOrRoot () =
    nodePlatform () = "win32" || runsAsRoot ()

let private withAssayArc =
    TestHelpers.withTempArcWith
        "swate-generic-fs-"
        "GenericFileSystemArc"
        (fun arc -> arc.InitAssay("AssayA") |> ignore)

let private renameRequest relativePath newName = {
    relativePath = relativePath
    newName = newName
}

let private moveRequest sourcePath targetPath overwrite = {
    sourceRelativePath = sourcePath
    targetRelativePath = targetPath
    overwrite = overwrite
}

let private createItemRequest parentPath name kind = {
    parentPath = parentPath
    name = name
    kind = kind
}

let private absoluteArcPath arcPath relativePath =
    relativePath
    |> PathHelpers.normalizePath
    |> fun path -> path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.append [| arcPath |]
    |> join

let private renameItemOrFail arcPath request = promise {
    match! ArcFileSystemHelper.renameGenericFileSystemItemOnDisk arcPath request with
    | Error error -> return failwith error.Message
    | Ok() -> return ()
}

let private createItemOrFail arcPath request = promise {
    match! ArcFileSystemHelper.createFileSystemItemOnDisk arcPath request with
    | Error error -> return failwith error.Message
    | Ok createdPath -> return createdPath
}

let private moveItemOrFail arcPath request = promise {
    match! ArcFileSystemHelper.moveGenericFileSystemItemOnDisk arcPath request with
    | Error error -> return failwith error.Message
    | Ok() -> return ()
}

let private deleteItemOrFail arcPath relativePath = promise {
    match! ArcFileSystemHelper.deleteGenericFileSystemItemOnDisk arcPath relativePath with
    | Error error -> return failwith error.Message
    | Ok() -> return ()
}

let private expectRelativePathExists arcPath relativePath expected = promise {
    let! exists = TestHelpers.pathExistsAsync (absoluteArcPath arcPath relativePath)
    Vitest.expect(exists).toBe (expected)
}

let private expectPathExistsRequest arcPath relativePath expected = promise {
    match tryResolveArcRelativePath arcPath relativePath with
    | Error error -> return failwith error.Message
    | Ok absolutePath ->
        let! exists = pathExistsAsync absolutePath
        Vitest.expect(exists).toBe (expected)
}

let private writeRelativeFileAsync arcPath relativePath content = promise {
    let absolutePath = absoluteArcPath arcPath relativePath

    let! _ =
        fsPromisesDynamic?writeFile (absolutePath, content, "utf8")
        |> unbox<JS.Promise<obj>>

    return ()
}

let private createRelativeDirectoryAsync arcPath relativePath = promise {
    let absolutePath = absoluteArcPath arcPath relativePath

    let! _ =
        fsPromisesDynamic?mkdir (absolutePath, createObj [ "recursive" ==> true ])
        |> unbox<JS.Promise<obj>>

    return ()
}

let private acceptImportedFiles () = promise { return Ok() }

let private importWithoutCancellation arcPath targetRelativePath sourcePaths =
    ArcFileSystemHelper.importExternalFilesOnDisk
        (fun _ -> ignore)
        arcPath
        targetRelativePath
        sourcePaths
        ignore
        (fun () -> false)
        ignore
        ignore
        acceptImportedFiles

/// Imports without cancellation, fails unless the import completes, and returns the planned files.
let private importCollectingPlannedFiles arcPath targetRelativePath sourcePaths = promise {
    let plannedFiles = ResizeArray<string>()

    match!
        ArcFileSystemHelper.importExternalFilesOnDisk
            (fun _ -> ignore)
            arcPath
            targetRelativePath
            sourcePaths
            ignore
            (fun () -> false)
            (fun planned -> plannedFiles.AddRange planned.FilePaths)
            ignore
            acceptImportedFiles
    with
    | Error error -> return failwith error.Message
    | Ok ImportExternalFilesResult.Cancelled -> return failwith "Expected import to complete."
    | Ok ImportExternalFilesResult.Completed -> return plannedFiles.ToArray()
}

/// Runs a lifecycle import and records what the staging registration saw.
let private importRecordingStagingLifecycle
    arcPath
    sourcePath
    (removeTemporaryDirectory: string -> JS.Promise<unit>)
    (validate: unit -> JS.Promise<Result<unit, exn>>)
    =
    promise {
        let registered = ResizeArray<string * bool>()
        let released = ResizeArray<bool>()
        let mutable stagingDirectory = ""

        let! result =
            ArcFileSystemHelper.importExternalFilesOnDiskWithTemporaryCleanup
                (fun directory ->
                    stagingDirectory <- directory
                    registered.Add(directory, Main.Bindings.Filesystem.existsSync directory)
                    fun stillExists -> released.Add stillExists
                )
                removeTemporaryDirectory
                ignore
                arcPath
                "assays/AssayA"
                [| sourcePath |]
                ignore
                (fun () -> false)
                ignore
                ignore
                validate

        return result, registered, released, stagingDirectory
    }

let private removeDirectoryRecursively (directory: string) =
    Main.Bindings.Filesystem.rmAsync directory (Main.Bindings.Filesystem.RmOptions(recursive = true, force = true))

let private readRelativeFileAsync rootPath relativePath =
    Main.Bindings.Filesystem.readFileAsync
        (absoluteArcPath rootPath relativePath)
        Main.Bindings.Filesystem.TextEncoding.Utf8

let private expectNoTemporaryImportDirectory arcPath relativePath = promise {
    let! entries = Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath relativePath)
    Vitest.expect(entries |> Array.exists _.StartsWith(".swate-import-")).toBe (false)
}

/// Creates `<temp root>/external/Run1` with nested files and an empty folder, and returns its path.
let private createExternalRunFolderAsync arcPath = promise {
    let sourceRoot = dirname arcPath
    do! createRelativeDirectoryAsync sourceRoot "external/Run1/raw"
    do! createRelativeDirectoryAsync sourceRoot "external/Run1/empty"
    do! writeRelativeFileAsync sourceRoot "external/Run1/notes.txt" "run notes"
    do! writeRelativeFileAsync sourceRoot "external/Run1/raw/results.csv" "a,b"
    return absoluteArcPath sourceRoot "external/Run1"
}

type private RejectedImport = {
    Description: string
    /// Prepares the ARC and the source, and returns the source file or folder to import.
    PrepareSource: string -> JS.Promise<string>
    TargetRelativePath: string
    ExpectedError: string
    /// Exists only if the import wrote something.
    AbsentRelativePath: string
}

let private rejectedImports = [
    {
        Description = "a folder named like a main ARC folder that exists in the target"
        PrepareSource =
            fun arcPath -> promise {
                let sourceRoot = dirname arcPath
                do! createRelativeDirectoryAsync sourceRoot "external/assays/raw"
                return absoluteArcPath sourceRoot "external/assays"
            }
        TargetRelativePath = ""
        ExpectedError = "Cannot import 'assays' because an item with that name already exists."
        AbsentRelativePath = "assays/raw"
    }
    {
        Description = "a folder named like a managed assay folder that exists in the target"
        PrepareSource =
            fun arcPath -> promise {
                let sourceRoot = dirname arcPath
                do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                do! createRelativeDirectoryAsync sourceRoot "external/dataset/raw"
                return absoluteArcPath sourceRoot "external/dataset"
            }
        TargetRelativePath = "assays/AssayA"
        ExpectedError = "Cannot import 'dataset' because an item with that name already exists."
        AbsentRelativePath = "assays/AssayA/dataset/raw"
    }
    {
        Description = "a folder into itself"
        PrepareSource =
            fun arcPath -> promise {
                do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                return absoluteArcPath arcPath "assays/AssayA"
            }
        TargetRelativePath = "assays/AssayA/dataset"
        ExpectedError = "Cannot import folder 'AssayA' into itself."
        AbsentRelativePath = "assays/AssayA/dataset/AssayA"
    }
    {
        Description = "a folder over an existing folder with the same name"
        PrepareSource =
            fun arcPath -> promise {
                do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset/Run1"
                return! createExternalRunFolderAsync arcPath
            }
        TargetRelativePath = "assays/AssayA/dataset"
        ExpectedError = "Cannot import 'Run1' because an item with that name already exists."
        AbsentRelativePath = "assays/AssayA/dataset/Run1/raw"
    }
]

let private expectErrorContaining (expectedMessagePart: string) (result: Result<'T, exn>) =
    match result with
    | Ok value -> failwith $"Expected an error containing '{expectedMessagePart}', but the operation returned {value}."
    | Error error -> Vitest.expect(error.Message).toContain (expectedMessagePart)

let private expectRoute (relativePath: string) expected actual =
    if actual <> expected then
        failwith $"Expected '{relativePath}' to route to {expected}, but it routed to {actual}."

let private expectDeleteRoute arcPath relativePath expected = promise {
    let! route = ArcFileSystemHelper.resolveDeletePathRouteAsync arcPath relativePath
    expectRoute relativePath expected route
}

let private expectGenericRename arcPath relativePath expected = promise {
    let! isGenericRename = ArcFileSystemHelper.isGenericRenamePathAsync arcPath relativePath
    expectRoute relativePath expected isGenericRename
}

Vitest.describe (
    "ArcFileSystemHelper generic filesystem operations",
    fun () ->

        Vitest.test (
            "temporary import directory name is .swate-import- followed by 32 hex digits",
            fun () ->
                let directoryName = ArcFileSystemHelper.createTemporaryImportDirectoryName ()
                let exactPattern = """^\.swate-import-[0-9a-f]{32}$"""

                Vitest.expect(System.Text.RegularExpressions.Regex.IsMatch(directoryName, exactPattern)).toBe (true)
        )

        Vitest.test (
            "ignores LFS backup paths while accepting regular files",
            fun () ->
                let guidSuffix = String.replicate 32 "a"
                let backupPath = $"C:/arc/data.bin.vcs-lfs-backup-{guidSuffix}"

                Vitest.expect(Main.ArcVaultHelper.isFileWatcherPathIgnoredFor backupPath (Some false)).toBe (true)

                Vitest
                    .expect(Main.ArcVaultHelper.isFileWatcherPathIgnoredFor "C:/arc/data.bin" (Some false))
                    .toBe (false)
        )

        Vitest.test (
            "ignores the lock files of open Office and LibreOffice documents, but not regular files or folders",
            fun () ->
                // Excel writes `~$Book1.xlsx` next to an open `Book1.xlsx`; `.~$` is kept for older matches.
                for lockFilePath in
                    [
                        "C:/arc/~$Book1.xlsx"
                        "C:/arc/assays/AssayA/~$Report.docx"
                        "C:/arc/.~$Book1.xlsx"
                        "C:/arc/.~lock.Book1.xlsx#"
                    ] do
                    Vitest.expect(Main.ArcVaultHelper.isFileWatcherPathIgnoredFor lockFilePath (Some false)).toBe (true)

                for regularPath in
                    [
                        "C:/arc/Book1.xlsx"
                        "C:/arc/data~$1.xlsx"
                        "C:/arc/~$drafts"
                        "C:/arc/~$drafts/notes.md"
                    ] do
                    Vitest.expect(Main.ArcVaultHelper.isFileWatcherPathIgnoredFor regularPath (Some false)).toBe (false)
        )

        Vitest.test (
            "leaves files the watcher ignores out of the imported file events and claims staging-named user folders",
            fun () ->
                // The watcher never reports these files, so no event would match their claim. The claim would only
                // match a folder that another program later creates at that path, and that folder would skip its ARC
                // merge until the lease ends.
                let guidSuffix = String.replicate 32 "a"

                let events =
                    Main.WatcherHelpers.createImportedWatcherEvents "C:/arc" "assays/AssayA/dataset" {
                        FolderPaths = [| $"Run1/.swate-import-{guidSuffix}" |]
                        FilePaths = [|
                            "Run1/notes.txt"
                            "Run1/~$results.xlsx"
                            $"Run1/.swate-import-{guidSuffix}/raw.txt"
                            $"Run1/data.bin.vcs-lfs-backup-{guidSuffix}"
                        |]
                    }

                Vitest
                    .expect(events |> Array.map _.RelativePath)
                    .toEqual (
                        [|
                            $"assays/AssayA/dataset/Run1/.swate-import-{guidSuffix}"
                            "assays/AssayA/dataset/Run1/notes.txt"
                            $"assays/AssayA/dataset/Run1/.swate-import-{guidSuffix}/raw.txt"
                        |]
                    )
        )

        Vitest.test (
            "builds addDir events for planned folders and add events for planned files, below the target or at the ARC root",
            fun () ->
                let addDir = Main.Bindings.Chokidar.Events.AddDir.ToString()
                let add = Main.Bindings.Chokidar.Events.Add.ToString()

                let plannedPaths = {
                    FolderPaths = [| "Run1"; "Run1/raw" |]
                    FilePaths = [| "Run1/raw/results.csv" |]
                }

                let eventsFor target =
                    Main.WatcherHelpers.createImportedWatcherEvents "C:/arc" target plannedPaths
                    |> Array.map (fun event -> event.EventName, event.RelativePath)

                Vitest
                    .expect(eventsFor "assays/AssayA/dataset")
                    .toEqual (
                        [|
                            addDir, "assays/AssayA/dataset/Run1"
                            addDir, "assays/AssayA/dataset/Run1/raw"
                            add, "assays/AssayA/dataset/Run1/raw/results.csv"
                        |]
                    )

                Vitest
                    .expect(eventsFor "")
                    .toEqual (
                        [|
                            addDir, "Run1"
                            addDir, "Run1/raw"
                            add, "Run1/raw/results.csv"
                        |]
                    )
        )

        Vitest.test (
            "builds unfiltered absolute folder and file paths for planned paths, below the target or at the ARC root",
            fun () ->
                let plannedPaths = {
                    FolderPaths = [| "Run1"; "Run1/raw" |]
                    FilePaths = [| "Run1/raw/results.csv"; "Run1/~$tmp.xlsx" |]
                }

                let pathsFor target =
                    Main.WatcherHelpers.importedAbsolutePaths "C:/arc" target plannedPaths

                Vitest
                    .expect(pathsFor "assays/AssayA/dataset")
                    .toEqual (
                        [|
                            "C:/arc/assays/AssayA/dataset/Run1"
                            "C:/arc/assays/AssayA/dataset/Run1/raw"
                        |],
                        [|
                            "C:/arc/assays/AssayA/dataset/Run1/raw/results.csv"
                            "C:/arc/assays/AssayA/dataset/Run1/~$tmp.xlsx"
                        |]
                    )

                Vitest
                    .expect(pathsFor "")
                    .toEqual (
                        [| "C:/arc/Run1"; "C:/arc/Run1/raw" |],
                        [|
                            "C:/arc/Run1/raw/results.csv"
                            "C:/arc/Run1/~$tmp.xlsx"
                        |]
                    )
        )

        Vitest.test (
            "checks whether relative files and directories exist",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "notes/existing-note"
                    do! writeRelativeFileAsync arcPath "notes/existing-note/existing-note.md" "hello"

                    do! expectPathExistsRequest arcPath "notes/existing-note/existing-note.md" true
                    do! expectPathExistsRequest arcPath "notes/existing-note" true
                })
        )

        Vitest.test (
            "reports missing safe relative paths as absent",
            fun () ->
                withAssayArc (fun arcPath -> promise { do! expectPathExistsRequest arcPath "notes/missing-note" false })
        )

        Vitest.test (
            "rejects unsafe path existence requests",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    match tryResolveArcRelativePath arcPath "../outside" with
                    | Ok _ -> failwith "Expected traversal path to be rejected."
                    | Error error -> Vitest.expect(error.Message.Length > 0).toBe (true)

                    match tryResolveArcRelativePath arcPath arcPath with
                    | Ok _ -> failwith "Expected absolute path to be rejected."
                    | Error error -> Vitest.expect(error.Message.Length > 0).toBe (true)
                })
        )

        Vitest.test (
            "creates generic folders through the ARCtrl-backed create path",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    match!
                        ArcFileSystemHelper.createFileSystemItemOnDisk
                            arcPath
                            (createItemRequest "assays/AssayA" "attachments" FileSystemItemKind.Folder)
                    with
                    | Error error -> failwith error.Message
                    | Ok createdPath ->
                        Vitest.expect(createdPath).toBe ("assays/AssayA/attachments")

                        let! isDirectory =
                            ARCtrl.FileSystemHelper.directoryExistsAsync (absoluteArcPath arcPath createdPath)

                        Vitest.expect(isDirectory).toBe (true)
                })
        )

        Vitest.test (
            "imports an external file into an ARC folder",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourcePath = join [| dirname arcPath; "external.txt" |]
                    do! writeRelativeFileAsync (dirname arcPath) "external.txt" "imported content"

                    let progress = ResizeArray<FileImportProgress>()

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA"
                            [| sourcePath |]
                            progress.Add
                            (fun () -> false)
                            ignore
                            ignore
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Cancelled -> failwith "Expected import to complete."
                    | Ok ImportExternalFilesResult.Completed ->
                        let importedPath = absoluteArcPath arcPath "assays/AssayA/external.txt"

                        let! importedContent =
                            fsPromisesDynamic?readFile (importedPath, "utf8") |> unbox<JS.Promise<string>>

                        Vitest.expect(importedContent).toBe ("imported content")

                        Vitest
                            .expect(progress |> Seq.map _.step |> Seq.toArray)
                            .toEqual (
                                [|
                                    FileImportStep.Scanning
                                    FileImportStep.Copying
                                    FileImportStep.Copying
                                    FileImportStep.Publishing
                                    FileImportStep.Publishing
                                |]
                            )
                })
        )

        Vitest.test (
            "cancels a folder import while it is copied into the staging folder",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let mutable cancelRequested = false

                    // The first Copying report with a count comes after the first entry is staged.
                    let onProgress progress =
                        if progress.step = FileImportStep.Copying && progress.completedItems > 0 then
                            cancelRequested <- true

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            onProgress
                            (fun () -> cancelRequested)
                            ignore
                            ignore
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Completed -> failwith "Expected import cancellation."
                    | Ok ImportExternalFilesResult.Cancelled ->
                        do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                        do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "cancels an import without leaving temporary or imported files",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceDirectory = dirname arcPath
                    let firstName = $"cancel-first-{newGuidN ()}.txt"
                    let secondName = $"cancel-second-{newGuidN ()}.txt"
                    do! writeRelativeFileAsync sourceDirectory firstName "first"
                    do! writeRelativeFileAsync sourceDirectory secondName "second"

                    let mutable cancelRequested = false

                    let onProgress progress =
                        if progress.step = FileImportStep.Copying && progress.completedItems > 0 then
                            cancelRequested <- true

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA"
                            [|
                                join [| sourceDirectory; firstName |]
                                join [| sourceDirectory; secondName |]
                            |]
                            onProgress
                            (fun () -> cancelRequested)
                            ignore
                            ignore
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Completed -> failwith "Expected import cancellation."
                    | Ok ImportExternalFilesResult.Cancelled ->
                        let targetDirectory = absoluteArcPath arcPath "assays/AssayA"
                        let! firstExists = pathExistsAsync (join [| targetDirectory; firstName |])
                        let! secondExists = pathExistsAsync (join [| targetDirectory; secondName |])
                        let! targetEntries = fsPromisesDynamic?readdir targetDirectory |> unbox<JS.Promise<string[]>>

                        let! arcParentEntries =
                            fsPromisesDynamic?readdir (dirname arcPath) |> unbox<JS.Promise<string[]>>

                        Vitest.expect(firstExists).toBe (false)
                        Vitest.expect(secondExists).toBe (false)
                        Vitest.expect(targetEntries |> Array.exists _.StartsWith(".swate-import-")).toBe (false)
                        Vitest.expect(arcParentEntries |> Array.exists _.StartsWith(".swate-import-")).toBe (false)
                })
        )

        Vitest.test (
            "rolls back imported files when post-import validation fails",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceDirectory = dirname arcPath
                    let fileName = $"invalid-arc-import-{newGuidN ()}.xlsx"
                    let sourcePath = join [| sourceDirectory; fileName |]
                    do! writeRelativeFileAsync sourceDirectory fileName "malformed ARC workbook"

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA"
                            [| sourcePath |]
                            ignore
                            (fun () -> false)
                            ignore
                            ignore
                            (fun () -> promise { return Error(exn "ARC synchronization failed") })
                    with
                    | Ok outcome -> failwith $"Expected validation failure, got {outcome}."
                    | Error error ->
                        Vitest.expect(error.Message).toContain ("ARC synchronization failed")

                        let importedPath = absoluteArcPath arcPath $"assays/AssayA/{fileName}"
                        let! importedFileExists = pathExistsAsync importedPath
                        Vitest.expect(importedFileExists).toBe (false)
                })
        )

        Vitest.test (
            "rejects a picked file that no longer exists and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceDirectory = dirname arcPath
                    let firstName = $"error-first-{newGuidN ()}.txt"
                    let missingName = $"missing-{newGuidN ()}.txt"
                    do! writeRelativeFileAsync sourceDirectory firstName "first"

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA"
                            [|
                                join [| sourceDirectory; firstName |]
                                join [| sourceDirectory; missingName |]
                            |]
                            ignore
                            (fun () -> false)
                            ignore
                            ignore
                            acceptImportedFiles
                    with
                    | Ok outcome -> failwith $"Expected import failure, got {outcome}."
                    | Error error ->
                        Vitest
                            .expect(error.Message)
                            .toContain (
                                $"Cannot import '{missingName}' because it or the item its link points to no longer exists."
                            )

                        let targetDirectory = absoluteArcPath arcPath "assays/AssayA"
                        let! firstExists = pathExistsAsync (join [| targetDirectory; firstName |])
                        let! entries = fsPromisesDynamic?readdir targetDirectory |> unbox<JS.Promise<string[]>>

                        Vitest.expect(firstExists).toBe (false)
                        Vitest.expect(entries |> Array.exists _.StartsWith(".swate-import-")).toBe (false)
                })
        )

        Vitest.test (
            "keeps published files when only staging-directory cleanup fails",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceDirectory = dirname arcPath
                    let fileName = $"cleanup-failure-{newGuidN ()}.txt"
                    let sourcePath = join [| sourceDirectory; fileName |]
                    let mutable cleanupErrorWasLogged = false
                    let lifecycleOrder = ResizeArray<string>()
                    do! writeRelativeFileAsync sourceDirectory fileName "published content"

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDiskWithTemporaryCleanup
                            (fun _ -> ignore)
                            (fun _ ->
                                lifecycleOrder.Add "cleanup"
                                JS.Constructors.Promise.reject (nodeError "staging directory busy" "EBUSY")
                            )
                            (fun _ -> cleanupErrorWasLogged <- true)
                            arcPath
                            "assays/AssayA"
                            [| sourcePath |]
                            ignore
                            (fun () -> false)
                            ignore
                            (fun _ -> lifecycleOrder.Add "published")
                            (fun () -> promise {
                                lifecycleOrder.Add "validate"
                                return Ok()
                            })
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Cancelled -> failwith "Expected import to complete."
                    | Ok ImportExternalFilesResult.Completed ->
                        let importedPath = absoluteArcPath arcPath $"assays/AssayA/{fileName}"
                        let! importedFileExists = pathExistsAsync importedPath
                        Vitest.expect(importedFileExists).toBe (true)
                        Vitest.expect(cleanupErrorWasLogged).toBe (true)
                        Vitest.expect(lifecycleOrder.[0]).toBe ("published")
                        Vitest.expect(lifecycleOrder.[lifecycleOrder.Count - 1]).toBe ("validate")

                        Vitest
                            .expect(
                                lifecycleOrder
                                |> Seq.skip 1
                                |> Seq.take (lifecycleOrder.Count - 2)
                                |> Seq.forall ((=) "cleanup")
                            )
                            .toBe (true)
                })
        )

        Vitest.test (
            "registers the staging folder before it exists and releases it once after it is removed",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let fileName = $"lifecycle-{newGuidN ()}.txt"
                    do! writeRelativeFileAsync (dirname arcPath) fileName "content"

                    let! result, registered, released, _ =
                        importRecordingStagingLifecycle
                            arcPath
                            (join [| dirname arcPath; fileName |])
                            removeDirectoryRecursively
                            acceptImportedFiles

                    Vitest.expect(result).toEqual (Ok ImportExternalFilesResult.Completed)
                    Vitest.expect(registered.Count).toBe 1
                    Vitest.expect(snd registered.[0]).toBe false
                    Vitest.expect(released.Count).toBe 1
                    Vitest.expect(released.[0]).toBe false
                })
        )

        Vitest.test (
            "releases the staging folder once after a failed import is rolled back",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let fileName = $"lifecycle-{newGuidN ()}.txt"
                    do! writeRelativeFileAsync (dirname arcPath) fileName "content"

                    let! result, registered, released, _ =
                        importRecordingStagingLifecycle
                            arcPath
                            (join [| dirname arcPath; fileName |])
                            removeDirectoryRecursively
                            (fun () -> promise { return Error(exn "ARC synchronization failed") })

                    match result with
                    | Error _ -> ()
                    | Ok _ -> failwith "Expected the import to fail."

                    Vitest.expect(registered.Count).toBe 1
                    Vitest.expect(released.Count).toBe 1
                    Vitest.expect(released.[0]).toBe false
                })
        )

        Vitest.test (
            "releases the staging folder with stillExists true when its removal fails and the folder stays",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let fileName = $"lifecycle-{newGuidN ()}.txt"
                    do! writeRelativeFileAsync (dirname arcPath) fileName "content"

                    let! result, registered, released, stagingDirectory =
                        importRecordingStagingLifecycle
                            arcPath
                            (join [| dirname arcPath; fileName |])
                            (fun _ -> JS.Constructors.Promise.reject (nodeError "staging directory busy" "EBUSY"))
                            acceptImportedFiles

                    Vitest.expect(result).toEqual (Ok ImportExternalFilesResult.Completed)
                    Vitest.expect(registered.Count).toBe 1
                    Vitest.expect(released.Count).toBe 1
                    Vitest.expect(released.[0]).toBe true
                    Vitest.expect(Main.Bindings.Filesystem.existsSync stagingDirectory).toBe true
                })
        )

        Vitest.test (
            "does not overwrite an existing destination file",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let fileName = $"existing-{newGuidN ()}.txt"
                    let sourceDirectory = dirname arcPath
                    do! writeRelativeFileAsync sourceDirectory fileName "new content"
                    do! writeRelativeFileAsync arcPath $"assays/AssayA/{fileName}" "original content"

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA"
                            [| join [| sourceDirectory; fileName |] |]
                            ignore
                            (fun () -> false)
                            ignore
                            ignore
                            acceptImportedFiles
                    with
                    | Ok outcome -> failwith $"Expected destination conflict, got {outcome}."
                    | Error error ->
                        Vitest.expect(error.Message).toContain ("because an item with that name already exists")

                        let destinationPath = absoluteArcPath arcPath $"assays/AssayA/{fileName}"

                        let! content =
                            fsPromisesDynamic?readFile (destinationPath, "utf8")
                            |> unbox<JS.Promise<string>>

                        Vitest.expect(content).toBe ("original content")
                })
        )

        Vitest.test (
            "imports an external folder with its nested files and empty folders",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let mutable plannedFiles = [||]
                    let mutable plannedFolders = [||]
                    let mutable publishedFolders = [||]
                    let lifecycleOrder = ResizeArray<string>()

                    // The import handler registers watcher ownership in onPathsPlanned, so it must run
                    // before anything appears in the target.
                    let onPathsPlanned (planned: PlannedImportPaths) =
                        plannedFiles <- planned.FilePaths
                        plannedFolders <- planned.FolderPaths

                        if
                            Main.Bindings.Filesystem.existsSync (absoluteArcPath arcPath "assays/AssayA/dataset/Run1")
                        then
                            lifecycleOrder.Add "planned after publishing started"
                        else
                            lifecycleOrder.Add "planned"

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            ignore
                            (fun () -> false)
                            onPathsPlanned
                            (fun folders ->
                                publishedFolders <- folders
                                lifecycleOrder.Add "published"
                            )
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Cancelled -> failwith "Expected import to complete."
                    | Ok ImportExternalFilesResult.Completed ->
                        let! notes = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/notes.txt"
                        let! results = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/raw/results.csv"

                        let! emptyFolderExists =
                            ARCtrl.FileSystemHelper.directoryExistsAsync (
                                absoluteArcPath arcPath "assays/AssayA/dataset/Run1/empty"
                            )

                        let! generatedGitKeep =
                            readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/empty/.gitkeep"

                        Vitest
                            .expect(plannedFiles)
                            .toEqual (
                                [|
                                    "Run1/notes.txt"
                                    "Run1/raw/results.csv"
                                    "Run1/empty/.gitkeep"
                                |]
                            )

                        Vitest.expect(plannedFolders).toEqual ([| "Run1"; "Run1/empty"; "Run1/raw" |])
                        Vitest.expect(lifecycleOrder |> Seq.toArray).toEqual ([| "planned"; "published" |])

                        // Only the picked folder is reported, not the folders inside it.
                        Vitest
                            .expect(publishedFolders)
                            .toEqual ([| absoluteArcPath arcPath "assays/AssayA/dataset/Run1" |])

                        Vitest.expect(notes).toBe ("run notes")
                        Vitest.expect(results).toBe ("a,b")
                        Vitest.expect(emptyFolderExists).toBe (true)
                        Vitest.expect(generatedGitKeep).toBe ("")
                        do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "writes a .gitkeep into a picked empty folder",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    do! createRelativeDirectoryAsync (dirname arcPath) "emptyPick"

                    let! plannedFiles =
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [|
                            join [| dirname arcPath; "emptyPick" |]
                        |]

                    let! gitKeep = readRelativeFileAsync arcPath "assays/AssayA/dataset/emptyPick/.gitkeep"

                    Vitest.expect(plannedFiles).toEqual ([| "emptyPick/.gitkeep" |])
                    Vitest.expect(gitKeep).toBe ("")
                })
        )

        Vitest.test (
            "writes a .gitkeep only into the innermost empty folder of nested empty folders",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    do! createRelativeDirectoryAsync (dirname arcPath) "nestedPick/a/b"

                    let! plannedFiles =
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [|
                            join [| dirname arcPath; "nestedPick" |]
                        |]

                    Vitest.expect(plannedFiles).toEqual ([| "nestedPick/a/b/.gitkeep" |])
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/nestedPick/a/b/.gitkeep" true
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/nestedPick/a/.gitkeep" false
                })
        )

        Vitest.test (
            "does not write a .gitkeep into an imported folder that has content",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [| sourcePath |]
                        |> Promise.map ignore

                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1/.gitkeep" false
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1/raw/.gitkeep" false
                })
        )

        Vitest.test (
            "reports scanning, then copying and publishing progress for every item",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let updates = ResizeArray<FileImportProgress>()
                    let mutable plannedCount = 0

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            updates.Add
                            (fun () -> false)
                            (fun planned -> plannedCount <- planned.FolderPaths.Length + planned.FilePaths.Length)
                            ignore
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Cancelled -> failwith "Expected import to complete."
                    | Ok ImportExternalFilesResult.Completed ->
                        let expected = [|
                            {
                                step = FileImportStep.Scanning
                                completedItems = 0
                                totalItems = 0
                            }
                            for i in 0..plannedCount do
                                {
                                    step = FileImportStep.Copying
                                    completedItems = i
                                    totalItems = plannedCount
                                }
                            for i in 0..plannedCount do
                                {
                                    step = FileImportStep.Publishing
                                    completedItems = i
                                    totalItems = plannedCount
                                }
                        |]

                        Vitest.expect(plannedCount).toBe (6)
                        Vitest.expect(updates.ToArray()).toEqual (expected)
                })
        )

        Vitest.test (
            "rejects a picked drive root with a clear message and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! entriesBefore =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    let rec rootOf path =
                        let parent = dirname path
                        if parent = path then path else rootOf parent

                    match! importWithoutCancellation arcPath "assays/AssayA/dataset" [| rootOf arcPath |] with
                    | Error error ->
                        Vitest
                            .expect(error.Message)
                            .toBe ("Cannot import a whole drive. Pick a folder on the drive instead.")
                    | Ok _ -> failwith "Expected the drive root to be rejected."

                    let! targetEntries =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    Vitest.expect(targetEntries).toEqual entriesBefore
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA"
                })
        )

        Vitest.test (
            "imports folders named like lock files with their contents",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync sourcePath "~$archive.old"
                    do! writeRelativeFileAsync sourcePath "~$archive.old/data.csv" "a,b"
                    let backupFolder = "raw/y.vcs-lfs-backup-" + String.replicate 32 "a"
                    do! createRelativeDirectoryAsync sourcePath backupFolder
                    do! writeRelativeFileAsync sourcePath $"{backupFolder}/data.csv" "c,d"
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! plannedFiles = importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [| sourcePath |]

                    let! lockFolderContent =
                        readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/~$archive.old/data.csv"

                    let! backupFolderContent =
                        readRelativeFileAsync arcPath $"assays/AssayA/dataset/Run1/{backupFolder}/data.csv"

                    Vitest.expect(plannedFiles |> Array.contains "Run1/~$archive.old/data.csv").toBe (true)
                    Vitest.expect(lockFolderContent).toBe ("a,b")
                    Vitest.expect(backupFolderContent).toBe ("c,d")
                })
        )

        Vitest.test (
            "imports a picked folder named like a lock file",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    do! createRelativeDirectoryAsync (dirname arcPath) "~$archive.old"
                    do! writeRelativeFileAsync (dirname arcPath) "~$archive.old/data.csv" "a,b"

                    do! createRelativeDirectoryAsync (dirname arcPath) ".~lock.data.xlsx#"
                    do! writeRelativeFileAsync (dirname arcPath) ".~lock.data.xlsx#/inner.csv" "c,d"

                    let backupFolderName = "x.vcs-lfs-backup-" + String.replicate 32 "a"
                    do! createRelativeDirectoryAsync (dirname arcPath) backupFolderName
                    do! writeRelativeFileAsync (dirname arcPath) $"{backupFolderName}/data.csv" "e,f"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [|
                            join [| dirname arcPath; "~$archive.old" |]
                            join [| dirname arcPath; ".~lock.data.xlsx#" |]
                            join [| dirname arcPath; backupFolderName |]
                        |]
                        |> Promise.map ignore

                    let! content = readRelativeFileAsync arcPath "assays/AssayA/dataset/~$archive.old/data.csv"

                    let! libreOfficeContent =
                        readRelativeFileAsync arcPath "assays/AssayA/dataset/.~lock.data.xlsx#/inner.csv"

                    Vitest.expect(content).toBe ("a,b")

                    let! backupContent =
                        readRelativeFileAsync arcPath $"assays/AssayA/dataset/{backupFolderName}/data.csv"

                    Vitest.expect(libreOfficeContent).toBe ("c,d")
                    Vitest.expect(backupContent).toBe ("e,f")
                })
        )

        Vitest.test (
            "rolls back a folder import when a source file disappears while it is copied",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    // Planning has finished at this point, so copying into the staging folder is the first
                    // step that misses the file.
                    let removeSourceFile _ =
                        Main.Bindings.Filesystem.unlinkSync (join [| sourcePath; "raw"; "results.csv" |])

                    let! result =
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            ignore
                            (fun () -> false)
                            removeSourceFile
                            ignore
                            acceptImportedFiles

                    result |> expectErrorContaining "ENOENT"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "keeps an item that appears in the target after the checks and stops the import",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let conflictingFolder = absoluteArcPath arcPath "assays/AssayA/dataset/Run1"

                    // Another program creates `Run1` after the import checked that the name is free. The
                    // rollback only removes what the import created, so this folder must survive.
                    let createConflictingFolder _ =
                        Main.Bindings.Filesystem.mkdirSync
                            conflictingFolder
                            (Main.Bindings.Filesystem.MkdirOptions(recursive = true))

                        TestHelpers.writeText (join [| conflictingFolder; "mine.txt" |]) "my data"

                    let! result =
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            ignore
                            (fun () -> false)
                            createConflictingFolder
                            ignore
                            acceptImportedFiles

                    result |> expectErrorContaining "EEXIST"
                    let! mine = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/mine.txt"
                    Vitest.expect(mine).toBe ("my data")
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1/notes.txt" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "imports several folders picked together, even when their contents share names",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    let! firstSourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync sourceRoot "external/Run2"
                    // Only the picked items need unique names, so both folders may contain `notes.txt`.
                    do! writeRelativeFileAsync sourceRoot "external/Run2/notes.txt" "second run notes"
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! plannedFiles =
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [|
                            firstSourcePath
                            absoluteArcPath sourceRoot "external/Run2"
                        |]

                    let! firstNotes = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/notes.txt"
                    let! secondNotes = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run2/notes.txt"

                    Vitest
                        .expect(plannedFiles)
                        .toEqual (
                            [|
                                "Run1/notes.txt"
                                "Run1/raw/results.csv"
                                "Run2/notes.txt"
                                "Run1/empty/.gitkeep"
                            |]
                        )

                    Vitest.expect(firstNotes).toBe ("run notes")
                    Vitest.expect(secondNotes).toBe ("second run notes")
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects picking two items with the same name, ignoring case, and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    do! createRelativeDirectoryAsync sourceRoot "external/a/Run1"
                    do! writeRelativeFileAsync sourceRoot "external/a/Run1/notes.txt" "run notes"
                    let goneTargetPath = absoluteArcPath sourceRoot "external/gone"
                    do! createRelativeDirectoryAsync sourceRoot "external/gone"

                    do!
                        TestHelpers.createFolderLinkAsync
                            goneTargetPath
                            (absoluteArcPath sourceRoot "external/a/Run1/dangling")

                    do! TestHelpers.removeDirectoryAsync goneTargetPath
                    do! createRelativeDirectoryAsync sourceRoot "external/b"
                    do! writeRelativeFileAsync sourceRoot "external/b/run1" "a file named like the folder"
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! result =
                        importWithoutCancellation arcPath "assays/AssayA/dataset" [|
                            absoluteArcPath sourceRoot "external/a/Run1"
                            absoluteArcPath sourceRoot "external/b/run1"
                        |]

                    result |> expectErrorContaining "Cannot import multiple items named 'Run1'."
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a nested name with a backslash and writes nothing",
            TestOptions(skip = (nodePlatform () = "win32")),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath

                    let! _ =
                        fsPromisesDynamic?writeFile (join [| sourcePath; "raw"; "a\\b.csv" |], "x", "utf8")
                        |> unbox<JS.Promise<obj>>

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result |> expectErrorContaining "must not contain path separators"
                    result |> expectErrorContaining "Run1/raw/a\\b.csv"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a nested name Windows does not allow and writes nothing",
            TestOptions(skip = (nodePlatform () = "win32")),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath

                    let! _ =
                        fsPromisesDynamic?writeFile (join [| sourcePath; "raw"; "T12:30:00.csv" |], "x", "utf8")
                        |> unbox<JS.Promise<obj>>

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result |> expectErrorContaining "Cannot import 'Run1/raw/T12:30:00.csv'"
                    result |> expectErrorContaining "because Windows does not allow them"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a nested or picked name that ends with a space and writes nothing",
            TestOptions(skip = (nodePlatform () = "win32")),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath

                    let! _ =
                        fsPromisesDynamic?writeFile (join [| sourcePath; "raw"; "notes.txt " |], "x", "utf8")
                        |> unbox<JS.Promise<obj>>

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result |> expectErrorContaining "starts or ends with a space"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"

                    // A picked name goes through its own check before any folder is scanned.
                    let pickedPath = join [| dirname arcPath; "picked.txt " |]

                    let! _ = fsPromisesDynamic?writeFile (pickedPath, "x", "utf8") |> unbox<JS.Promise<obj>>
                    let! pickedResult = importWithoutCancellation arcPath "assays/AssayA/dataset" [| pickedPath |]

                    pickedResult
                    |> expectErrorContaining
                        "Cannot import 'picked.txt ' because its name starts or ends with a space."

                    // The relative path helpers trim the name, so the exact path is checked here.
                    Vitest
                        .expect(
                            Main.Bindings.Filesystem.existsSync (
                                join [| arcPath; "assays"; "AssayA"; "dataset"; "picked.txt " |]
                            )
                        )
                        .toBe (false)

                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects nested names that differ only by case and writes nothing",
            TestOptions(skip = (nodePlatform () <> "linux")),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! writeRelativeFileAsync sourcePath "raw/A.csv" "upper"
                    do! writeRelativeFileAsync sourcePath "raw/a.csv" "lower"
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result |> expectErrorContaining "same name, ignoring case"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "imports a folder with git files, ARC metadata names, lock files, README folders, LFS pointers and staging leftovers completely",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let stagingFolder = ".swate-import-" + String.replicate 32 "a"

                    let lfsPointer =
                        "version https://git-lfs.github.com/spec/v1\noid sha256:"
                        + String.replicate 64 "a"
                        + "\nsize 12\n"

                    let files = [
                        ".git/config", "git config"
                        "isa.assay.xlsx", "workbook"
                        "isa_datamap", "datamap"
                        "~$Book1.xlsx", "Excel lock file"
                        ".gitkeep", "source content"
                        "README.md/inner.txt", "inner"
                        "large.bin", lfsPointer
                        $"{stagingFolder}/staged.txt", "staged"
                    ]

                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync sourcePath ".git"
                    do! createRelativeDirectoryAsync sourcePath ".git/refs/tags"
                    do! createRelativeDirectoryAsync sourcePath "README.md"
                    do! createRelativeDirectoryAsync sourcePath stagingFolder

                    for relativePath, content in files do
                        do! writeRelativeFileAsync sourcePath relativePath content

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [| sourcePath |]
                        |> Promise.map ignore

                    for relativePath, content in files do
                        let! imported = readRelativeFileAsync arcPath $"assays/AssayA/dataset/Run1/{relativePath}"
                        Vitest.expect(imported).toBe (content)

                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1/.git/refs/tags" true
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1/.git/refs/tags/.gitkeep" false
                })
        )

        Vitest.test (
            "imports picked items that are named like a lock file, the legacy DataMap or a protected file",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset/raw"
                    do! writeRelativeFileAsync sourceRoot "~$Book1.xlsx" "Excel lock file"
                    do! writeRelativeFileAsync sourceRoot "isa_datamap" "datamap"
                    do! createRelativeDirectoryAsync sourceRoot ".gitkeep"
                    do! writeRelativeFileAsync sourceRoot ".gitkeep/inner.txt" "inner"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset/raw" [|
                            join [| sourceRoot; "~$Book1.xlsx" |]
                            join [| sourceRoot; "isa_datamap" |]
                            join [| sourceRoot; ".gitkeep" |]
                        |]
                        |> Promise.map ignore

                    let! lockFile = readRelativeFileAsync arcPath "assays/AssayA/dataset/raw/~$Book1.xlsx"
                    let! dataMap = readRelativeFileAsync arcPath "assays/AssayA/dataset/raw/isa_datamap"
                    let! inner = readRelativeFileAsync arcPath "assays/AssayA/dataset/raw/.gitkeep/inner.txt"

                    Vitest.expect(lockFile).toBe ("Excel lock file")
                    Vitest.expect(dataMap).toBe ("datamap")
                    Vitest.expect(inner).toBe ("inner")
                })
        )

        Vitest.test (
            "rejects a picked item that differs from an existing item only by case and leaves the disk unchanged",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    let targetPath = "assays/AssayA/dataset"
                    do! createRelativeDirectoryAsync arcPath $"{targetPath}/Data"
                    do! writeRelativeFileAsync arcPath $"{targetPath}/notes.md" "existing notes"
                    do! createRelativeDirectoryAsync sourceRoot "external/data"
                    do! writeRelativeFileAsync sourceRoot "external/data/x.txt" "x"
                    do! writeRelativeFileAsync sourceRoot "external/Notes.MD" "picked notes"
                    let! before = Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath targetPath)

                    let! folderResult =
                        importWithoutCancellation arcPath targetPath [| absoluteArcPath sourceRoot "external/data" |]

                    folderResult
                    |> expectErrorContaining
                        "Cannot import 'data' because 'Data' already exists there, and upper and lower case count as the same."

                    let! fileResult =
                        importWithoutCancellation arcPath targetPath [|
                            absoluteArcPath sourceRoot "external/Notes.MD"
                        |]

                    fileResult
                    |> expectErrorContaining
                        "Cannot import 'Notes.MD' because 'notes.md' already exists there, and upper and lower case count as the same."

                    let! after = Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath targetPath)
                    Vitest.expect(after).toEqual (before)
                    do! expectRelativePathExists arcPath $"{targetPath}/Data/x.txt" false
                    let! notes = readRelativeFileAsync arcPath $"{targetPath}/notes.md"
                    Vitest.expect(notes).toBe ("existing notes")
                    do! expectNoTemporaryImportDirectory arcPath targetPath
                })
        )

        Vitest.test (
            "imports nested file and folder links as ordinary files and folders with the content of their targets",
            TestOptions(skip = not (TestHelpers.canCreateFileLinks ())),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync sourceRoot "outside/dir"
                    do! writeRelativeFileAsync sourceRoot "outside/dir/inner.txt" "inner content"
                    do! writeRelativeFileAsync sourceRoot "outside/target.txt" "file content"

                    do!
                        TestHelpers.createFileLinkAsync
                            (absoluteArcPath sourceRoot "outside/target.txt")
                            (join [| sourcePath; "filelink.txt" |])

                    do!
                        TestHelpers.createFolderLinkAsync
                            (absoluteArcPath sourceRoot "outside/dir")
                            (join [| sourcePath; "folderlink" |])

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [| sourcePath |]
                        |> Promise.map ignore

                    let! fileContent = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/filelink.txt"

                    let! folderContent =
                        readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/folderlink/inner.txt"

                    let! fileStats =
                        Main.Bindings.Filesystem.lstatAsync (
                            absoluteArcPath arcPath "assays/AssayA/dataset/Run1/filelink.txt"
                        )

                    let! folderStats =
                        Main.Bindings.Filesystem.lstatAsync (
                            absoluteArcPath arcPath "assays/AssayA/dataset/Run1/folderlink"
                        )

                    Vitest.expect(fileContent).toBe ("file content")
                    Vitest.expect(folderContent).toBe ("inner content")
                    Vitest.expect(fileStats.isSymbolicLink ()).toBe (false)
                    Vitest.expect(folderStats.isSymbolicLink ()).toBe (false)
                    Vitest.expect(folderStats.isDirectory ()).toBe (true)
                })
        )

        Vitest.test (
            "rejects a nested folder link that points back at a folder containing it and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! TestHelpers.createFolderLinkAsync sourcePath (join [| sourcePath; "raw"; "back" |])
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! before =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result
                    |> expectErrorContaining
                        "Cannot import 'Run1/raw/back' because it links back to a folder that contains it."

                    let! after =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    Vitest.expect(after).toEqual (before)
                })
        )

        Vitest.test (
            "rejects a nested folder link to the folder containing the import target and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! TestHelpers.createFolderLinkAsync (dirname arcPath) (join [| sourcePath; "raw"; "toRoot" |])
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! before =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result
                    |> expectErrorContaining
                        "Cannot import 'Run1/raw/toRoot' because it links to the import target or a folder that contains it."

                    let! after =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    Vitest.expect(after).toEqual (before)
                })
        )

        Vitest.test (
            "rejects a nested folder without read access and writes nothing",
            TestOptions(skip = skipOnWindowsOrRoot ()),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    let lockedPath = join [| sourcePath; "raw"; "locked" |]
                    do! createRelativeDirectoryAsync sourcePath "raw/locked"
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    do! chmodAsync (lockedPath, 0)

                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]
                    do! chmodAsync (lockedPath, 0o755)

                    result |> expectErrorContaining "access to it was denied"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a nested self-referencing symbolic link and writes nothing",
            TestOptions(skip = (nodePlatform () = "win32")),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath

                    do! symlinkAsync ("a", join [| sourcePath; "raw"; "a" |])

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result |> expectErrorContaining "goes through too many symbolic links"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a nested named pipe and writes nothing",
            TestOptions(skip = (nodePlatform () = "win32" || not (hasMkfifo ()))),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    Vitest.expect(makeFifo (join [| sourcePath; "raw"; "pipe" |])).toBe 0
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result |> expectErrorContaining "neither a file nor a folder"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a nested folder link to the parent of the picked folder as a cycle and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! TestHelpers.createFolderLinkAsync (dirname sourcePath) (join [| sourcePath; "up" |])
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! before =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result
                    |> expectErrorContaining
                        "Cannot import 'Run1/up' because it links back to a folder that contains it."

                    let! after =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    Vitest.expect(after).toEqual (before)
                })
        )

        Vitest.test (
            "imports two nested links to the same folder in full",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync sourceRoot "outside/shared"
                    do! writeRelativeFileAsync sourceRoot "outside/shared/data.txt" "shared content"
                    let sharedPath = absoluteArcPath sourceRoot "outside/shared"
                    do! TestHelpers.createFolderLinkAsync sharedPath (join [| sourcePath; "linkA" |])
                    do! TestHelpers.createFolderLinkAsync sharedPath (join [| sourcePath; "raw"; "linkB" |])
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [| sourcePath |]
                        |> Promise.map ignore

                    let! first = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/linkA/data.txt"
                    let! second = readRelativeFileAsync arcPath "assays/AssayA/dataset/Run1/raw/linkB/data.txt"
                    Vitest.expect(first).toBe ("shared content")
                    Vitest.expect(second).toBe ("shared content")
                })
        )

        Vitest.test (
            "rejects a nested link whose target was deleted and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    let goneTargetPath = absoluteArcPath (dirname arcPath) "external/gone"
                    do! createRelativeDirectoryAsync (dirname arcPath) "external/gone"
                    do! TestHelpers.createFolderLinkAsync goneTargetPath (join [| sourcePath; "dangling" |])
                    do! TestHelpers.removeDirectoryAsync goneTargetPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| sourcePath |]

                    result
                    |> expectErrorContaining
                        "Cannot import 'Run1/dangling' because it or the item its link points to no longer exists."

                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rejects a picked folder link whose target contains the import target and writes nothing",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let linkPath = join [| dirname arcPath; "external"; "LinkToRoot" |]
                    do! createRelativeDirectoryAsync (dirname arcPath) "external"
                    do! TestHelpers.createFolderLinkAsync (dirname arcPath) linkPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! before =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    let! result = importWithoutCancellation arcPath "assays/AssayA/dataset" [| linkPath |]

                    result |> expectErrorContaining "Cannot import folder 'LinkToRoot' into itself."

                    let! after =
                        Main.Bindings.Filesystem.readdirAsync (absoluteArcPath arcPath "assays/AssayA/dataset")

                    Vitest.expect(after).toEqual (before)
                })
        )

        Vitest.test (
            "imports a picked folder link as an ordinary folder",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    let linkPath = join [| dirname arcPath; "external"; "LinkedRun" |]
                    do! TestHelpers.createFolderLinkAsync sourcePath linkPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    do!
                        importCollectingPlannedFiles arcPath "assays/AssayA/dataset" [| linkPath |]
                        |> Promise.map ignore

                    let! results = readRelativeFileAsync arcPath "assays/AssayA/dataset/LinkedRun/raw/results.csv"

                    let! stats =
                        Main.Bindings.Filesystem.lstatAsync (absoluteArcPath arcPath "assays/AssayA/dataset/LinkedRun")

                    Vitest.expect(results).toBe ("a,b")
                    Vitest.expect(stats.isSymbolicLink ()).toBe (false)
                })
        )

        for rejected in rejectedImports do
            Vitest.test (
                $"rejects importing {rejected.Description} and writes nothing",
                fun () ->
                    withAssayArc (fun arcPath -> promise {
                        let! sourcePath = rejected.PrepareSource arcPath
                        let! result = importWithoutCancellation arcPath rejected.TargetRelativePath [| sourcePath |]

                        result |> expectErrorContaining rejected.ExpectedError
                        do! expectRelativePathExists arcPath rejected.AbsentRelativePath false
                        do! expectNoTemporaryImportDirectory arcPath rejected.TargetRelativePath
                    })
            )

        Vitest.test (
            "rolls back every imported folder when post-import validation fails",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    let! firstSourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync sourceRoot "external/Run2/raw"
                    do! writeRelativeFileAsync sourceRoot "external/Run2/raw/data.csv" "c,d"
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! result =
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [|
                                firstSourcePath
                                absoluteArcPath sourceRoot "external/Run2"
                            |]
                            ignore
                            (fun () -> false)
                            ignore
                            ignore
                            (fun () -> promise { return Error(exn "ARC synchronization failed") })

                    result |> expectErrorContaining "ARC synchronization failed"
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                    do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run2" false
                    do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "cancels a folder import while the folder is still being scanned",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let mutable filesWerePlanned = false

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            ignore
                            (fun () -> true)
                            (fun _ -> filesWerePlanned <- true)
                            ignore
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Completed -> failwith "Expected import cancellation."
                    | Ok ImportExternalFilesResult.Cancelled ->
                        // The import handler registers watcher ownership when files are planned, so a scan
                        // that stops early must not plan anything.
                        Vitest.expect(filesWerePlanned).toBe (false)
                        do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                        do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "rolls back a folder import that is cancelled while its contents are published",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! sourcePath = createExternalRunFolderAsync arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    let mutable cancelledDuringPublishing = false

                    // Only the top-level folder is registered for rollback, so the nested items that
                    // were already published must be removed together with it.
                    let isCancellationRequested () =
                        if
                            Main.Bindings.Filesystem.existsSync (
                                absoluteArcPath arcPath "assays/AssayA/dataset/Run1/notes.txt"
                            )
                        then
                            cancelledDuringPublishing <- true

                        cancelledDuringPublishing

                    match!
                        ArcFileSystemHelper.importExternalFilesOnDisk
                            (fun _ -> ignore)
                            arcPath
                            "assays/AssayA/dataset"
                            [| sourcePath |]
                            ignore
                            isCancellationRequested
                            ignore
                            ignore
                            acceptImportedFiles
                    with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Completed -> failwith "Expected import cancellation."
                    | Ok ImportExternalFilesResult.Cancelled ->
                        Vitest.expect(cancelledDuringPublishing).toBe (true)
                        do! expectRelativePathExists arcPath "assays/AssayA/dataset/Run1" false
                        do! expectNoTemporaryImportDirectory arcPath "assays/AssayA/dataset"
                })
        )

        Vitest.test (
            "resolves a path through a link to its real path",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let linkedArcPath = join [| dirname arcPath; "linked-arc" |]
                    do! TestHelpers.createFolderLinkAsync arcPath linkedArcPath

                    let! realArcPath = tryResolveRealPathAsync arcPath
                    let! resolvedLinkedArcPath = tryResolveRealPathAsync linkedArcPath

                    Vitest
                        .expect(PathHelpers.normalizePathForFsComparison resolvedLinkedArcPath)
                        .toBe (PathHelpers.normalizePathForFsComparison realArcPath)
                })
        )

        Vitest.test (
            "returns a path that cannot be resolved as given",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let linkedArcPath = join [| dirname arcPath; "linked-arc" |]
                    do! TestHelpers.createFolderLinkAsync arcPath linkedArcPath

                    // Resolving a missing path fails like it does on drives that cannot resolve real paths.
                    let missingPath = join [| linkedArcPath; "missing" |]
                    let! resolvedPath = tryResolveRealPathAsync missingPath

                    Vitest.expect(resolvedPath).toBe (missingPath)
                })
        )

        Vitest.test (
            "rejects importing a folder into itself when the ARC is opened through a link",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "backup/inner"
                    let linkedArcPath = join [| dirname arcPath; "linked-arc" |]
                    do! TestHelpers.createFolderLinkAsync arcPath linkedArcPath

                    let! result =
                        importWithoutCancellation linkedArcPath "backup/inner" [| absoluteArcPath arcPath "backup" |]

                    result |> expectErrorContaining "Cannot import folder 'backup' into itself."
                    do! expectRelativePathExists arcPath "backup/inner/backup" false
                    do! expectNoTemporaryImportDirectory arcPath "backup/inner"
                })
        )

        Vitest.test (
            "imports the content of a picked file link",
            TestOptions(skip = not (TestHelpers.canCreateFileLinks ())),
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceRoot = dirname arcPath
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"
                    do! createRelativeDirectoryAsync sourceRoot "external"
                    do! writeRelativeFileAsync sourceRoot "external/notes.txt" "linked notes"

                    let linkPath = absoluteArcPath sourceRoot "external/linked-notes.txt"
                    do! TestHelpers.createFileLinkAsync (absoluteArcPath sourceRoot "external/notes.txt") linkPath

                    match! importWithoutCancellation arcPath "assays/AssayA/dataset" [| linkPath |] with
                    | Error error -> failwith error.Message
                    | Ok ImportExternalFilesResult.Cancelled -> failwith "Expected import to complete."
                    | Ok ImportExternalFilesResult.Completed ->
                        let! notes = readRelativeFileAsync arcPath "assays/AssayA/dataset/linked-notes.txt"
                        Vitest.expect(notes).toBe ("linked notes")
                })
        )

        Vitest.test (
            "protects structural entity child folders while allowing their contents",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    match!
                        ArcFileSystemHelper.createFileSystemItemOnDisk
                            arcPath
                            (createItemRequest "assays/AssayA" "dataset" FileSystemItemKind.Folder)
                    with
                    | Ok createdPath -> failwith $"Expected structural folder creation to be rejected: {createdPath}"
                    | Error error -> Vitest.expect(error.Message.Length).toBeGreaterThan (0)

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/dataset"

                    let! createdPath =
                        createItemOrFail
                            arcPath
                            (createItemRequest "assays/AssayA/dataset" "raw.txt" FileSystemItemKind.File)

                    Vitest.expect(createdPath).toBe ("assays/AssayA/dataset/raw.txt")
                    do! expectRelativePathExists arcPath createdPath true

                    match! ArcFileSystemHelper.deleteGenericFileSystemItemOnDisk arcPath "assays/AssayA/dataset" with
                    | Ok() -> failwith "Expected structural folder deletion to be rejected."
                    | Error error -> Vitest.expect(error.Message.Length).toBeGreaterThan (0)

                    match!
                        ArcFileSystemHelper.renameGenericFileSystemItemOnDisk
                            arcPath
                            (renameRequest "assays/AssayA/dataset" "raw-data")
                    with
                    | Ok() -> failwith "Expected structural folder rename to be rejected."
                    | Error error -> Vitest.expect(error.Message.Length).toBeGreaterThan (0)

                    do! deleteItemOrFail arcPath createdPath
                    do! expectRelativePathExists arcPath createdPath false
                })
        )

        Vitest.test (
            "creates generic files and folders at the ARC root",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! folderPath = createItemOrFail arcPath (createItemRequest "" "docs" FileSystemItemKind.Folder)

                    Vitest.expect(folderPath).toBe ("docs")

                    let! isDirectory =
                        ARCtrl.FileSystemHelper.directoryExistsAsync (absoluteArcPath arcPath folderPath)

                    Vitest.expect(isDirectory).toBe (true)

                    let! filePath = createItemOrFail arcPath (createItemRequest "" "notes.txt" FileSystemItemKind.File)

                    Vitest.expect(filePath).toBe ("notes.txt")

                    let! isFile = ARCtrl.FileSystemHelper.fileExistsAsync (absoluteArcPath arcPath filePath)

                    Vitest.expect(isFile).toBe (true)
                })
        )

        Vitest.test (
            "creates missing README files and keeps them protected",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! rootReadmePath =
                        createItemOrFail arcPath (createItemRequest "" "README.md" FileSystemItemKind.File)

                    Vitest.expect(rootReadmePath).toBe ("README.md")
                    do! expectRelativePathExists arcPath rootReadmePath true

                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/attachments"

                    let! assayReadmePath =
                        createItemOrFail
                            arcPath
                            (createItemRequest "assays/AssayA/attachments" "README.md" FileSystemItemKind.File)

                    Vitest.expect(assayReadmePath).toBe ("assays/AssayA/attachments/README.md")
                    do! expectRelativePathExists arcPath assayReadmePath true

                    for readmePath in [ rootReadmePath; assayReadmePath ] do
                        let! deleteResult = ArcFileSystemHelper.deleteGenericFileSystemItemOnDisk arcPath readmePath
                        expectErrorContaining "protected files" deleteResult
                        do! expectRelativePathExists arcPath readmePath true

                    let! existingReadmeResult =
                        ArcFileSystemHelper.createFileSystemItemOnDisk
                            arcPath
                            (createItemRequest "" "README.md" FileSystemItemKind.File)

                    expectErrorContaining "already exists" existingReadmeResult
                })
        )

        Vitest.test (
            "creates, renames, and deletes files directly inside an add zone without touching entities",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let! entityFolderResult =
                        ArcFileSystemHelper.createFileSystemItemOnDisk
                            arcPath
                            (createItemRequest "assays" "NewAssay" FileSystemItemKind.Folder)

                    expectErrorContaining "only allowed inside safe ARC directories" entityFolderResult
                    do! expectRelativePathExists arcPath "assays/NewAssay" false

                    let! canonicalFileResult =
                        ArcFileSystemHelper.createFileSystemItemOnDisk
                            arcPath
                            (createItemRequest "assays" "isa.assay.xlsx" FileSystemItemKind.File)

                    expectErrorContaining "is reserved for ARC metadata" canonicalFileResult
                    do! expectRelativePathExists arcPath "assays/isa.assay.xlsx" false

                    let! extensionlessFileResult =
                        ArcFileSystemHelper.createFileSystemItemOnDisk
                            arcPath
                            (createItemRequest "assays" "Assay1" FileSystemItemKind.File)

                    expectErrorContaining "file extension" extensionlessFileResult
                    do! expectRelativePathExists arcPath "assays/Assay1" false

                    let! createdPath =
                        createItemOrFail arcPath (createItemRequest "assays" "overview.md" FileSystemItemKind.File)

                    Vitest.expect(createdPath).toBe ("assays/overview.md")

                    let! isAddZoneRootFile = ArcFileSystemHelper.isAddZoneRootFileAsync arcPath createdPath
                    Vitest.expect(isAddZoneRootFile).toBe (true)

                    let! isEntityFolderFile = ArcFileSystemHelper.isAddZoneRootFileAsync arcPath "assays/AssayA"
                    Vitest.expect(isEntityFolderFile).toBe (false)

                    do! renameItemOrFail arcPath (renameRequest createdPath "summary.md")
                    do! expectRelativePathExists arcPath createdPath false
                    do! expectRelativePathExists arcPath "assays/summary.md" true

                    do! deleteItemOrFail arcPath "assays/summary.md"
                    do! expectRelativePathExists arcPath "assays/summary.md" false

                    let! entityDeleteResult =
                        ArcFileSystemHelper.deleteGenericFileSystemItemOnDisk arcPath "assays/AssayA"

                    expectErrorContaining "safe non-entity paths" entityDeleteResult
                    do! expectRelativePathExists arcPath "assays/AssayA" true

                    let! reloadedArc = TestHelpers.loadArcAsync arcPath
                    Vitest.expect(reloadedArc.ContainsAssay("AssayA")).toBe (true)
                })
        )

        Vitest.test (
            "routes deletes of files inside an add zone to the generic delete instead of the entity delete",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! writeRelativeFileAsync arcPath "assays/overview.md" "overview"
                    do! writeRelativeFileAsync arcPath "assays/AssayA/protocol.md" "protocol"

                    do!
                        expectDeleteRoute
                            arcPath
                            "assays/overview.md"
                            (ArcFileSystemHelper.DeletePathRoute.GenericItem "assays/overview.md")

                    do!
                        expectDeleteRoute
                            arcPath
                            "assays/AssayA"
                            (ArcFileSystemHelper.DeletePathRoute.ArcEntity "assays/AssayA")

                    // A missing path keeps the entity route, so only real files take the generic route.
                    do!
                        expectDeleteRoute
                            arcPath
                            "assays/MissingAssay"
                            (ArcFileSystemHelper.DeletePathRoute.ArcEntity "assays/MissingAssay")

                    do!
                        expectDeleteRoute
                            arcPath
                            "assays/AssayA/isa.assay.xlsx"
                            (ArcFileSystemHelper.DeletePathRoute.ArcEntity "assays/AssayA/isa.assay.xlsx")

                    do!
                        expectDeleteRoute
                            arcPath
                            "assays/AssayA/protocol.md"
                            (ArcFileSystemHelper.DeletePathRoute.GenericItem "assays/AssayA/protocol.md")

                    let! investigationRoute =
                        ArcFileSystemHelper.resolveDeletePathRouteAsync arcPath "isa.investigation.xlsx"

                    match investigationRoute with
                    | ArcFileSystemHelper.DeletePathRoute.Rejected _ -> ()
                    | route -> failwith $"Expected investigation delete to be rejected, but it routed to {route}."

                    let! readmeRoute =
                        ArcFileSystemHelper.resolveDeletePathRouteAsync arcPath "assays/AssayA/README.md"

                    match readmeRoute with
                    | ArcFileSystemHelper.DeletePathRoute.Rejected _ -> ()
                    | route -> failwith $"Expected README delete to be rejected, but it routed to {route}."

                    // A file with a canonical ARC name directly inside an add zone is neither an entity nor a generic file.
                    do! writeRelativeFileAsync arcPath "assays/isa.investigation.xlsx" ""

                    let! canonicalNameRoute =
                        ArcFileSystemHelper.resolveDeletePathRouteAsync arcPath "assays/isa.investigation.xlsx"

                    match canonicalNameRoute with
                    | ArcFileSystemHelper.DeletePathRoute.Rejected _ -> ()
                    | route ->
                        failwith $"Expected canonical-name file delete to be rejected, but it routed to {route}."
                })
        )

        Vitest.test (
            "routes renames of files inside an add zone to the generic rename instead of the entity rename",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! writeRelativeFileAsync arcPath "assays/overview.md" "overview"

                    do! expectGenericRename arcPath "assays/overview.md" true
                    do! expectGenericRename arcPath "assays/AssayA" false
                    do! expectGenericRename arcPath "assays/MissingAssay" false
                    do! expectGenericRename arcPath "assays/AssayA/protocol.md" true
                })
        )

        Vitest.test (
            "renames dot files directly inside an add zone to regular file names",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! writeRelativeFileAsync arcPath "assays/.DS_Store" ""

                    let! isAddZoneRootFile = ArcFileSystemHelper.isAddZoneRootFileAsync arcPath "assays/.DS_Store"
                    Vitest.expect(isAddZoneRootFile).toBe (true)

                    do! renameItemOrFail arcPath (renameRequest "assays/.DS_Store" "notes.md")
                    do! expectRelativePathExists arcPath "assays/.DS_Store" false
                    do! expectRelativePathExists arcPath "assays/notes.md" true
                })
        )

        Vitest.test (
            "handles files directly inside every add zone on disk",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    for zone in [ "studies"; "assays"; "workflows"; "runs" ] do
                        do! createRelativeDirectoryAsync arcPath zone

                        // New files need an extension and then use the generic operations.
                        let! extensionlessResult =
                            ArcFileSystemHelper.createFileSystemItemOnDisk
                                arcPath
                                (createItemRequest zone "Entity1" FileSystemItemKind.File)

                        expectErrorContaining "file extension" extensionlessResult
                        do! expectRelativePathExists arcPath $"{zone}/Entity1" false

                        let! overviewPath =
                            createItemOrFail arcPath (createItemRequest zone "overview.md" FileSystemItemKind.File)

                        do!
                            expectDeleteRoute
                                arcPath
                                overviewPath
                                (ArcFileSystemHelper.DeletePathRoute.GenericItem overviewPath)

                        do! expectGenericRename arcPath overviewPath true
                        do! renameItemOrFail arcPath (renameRequest overviewPath "summary.md")
                        do! deleteItemOrFail arcPath $"{zone}/summary.md"
                        do! expectRelativePathExists arcPath $"{zone}/summary.md" false

                        // An existing extensionless file can still be cleaned up.
                        do! writeRelativeFileAsync arcPath $"{zone}/LICENSE" "license"

                        do!
                            expectDeleteRoute
                                arcPath
                                $"{zone}/LICENSE"
                                (ArcFileSystemHelper.DeletePathRoute.GenericItem $"{zone}/LICENSE")

                        do! renameItemOrFail arcPath (renameRequest $"{zone}/LICENSE" "LICENSE.md")
                        do! deleteItemOrFail arcPath $"{zone}/LICENSE.md"
                        do! expectRelativePathExists arcPath $"{zone}/LICENSE.md" false

                        // A dot file can become a regular file but not an entity-shaped path.
                        do! writeRelativeFileAsync arcPath $"{zone}/.DS_Store" ""

                        let! entityShapedRenameResult =
                            ArcFileSystemHelper.renameGenericFileSystemItemOnDisk
                                arcPath
                                (renameRequest $"{zone}/.DS_Store" "Entity1")

                        expectErrorContaining "file extension" entityShapedRenameResult
                        do! expectRelativePathExists arcPath $"{zone}/.DS_Store" true
                        do! expectRelativePathExists arcPath $"{zone}/Entity1" false

                        do! renameItemOrFail arcPath (renameRequest $"{zone}/.DS_Store" "notes.md")
                        do! expectRelativePathExists arcPath $"{zone}/notes.md" true
                })
        )

        Vitest.test (
            "renames generic files and rejects destination conflicts",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! writeRelativeFileAsync arcPath "assays/AssayA/old.txt" "old"

                    do! renameItemOrFail arcPath (renameRequest "assays/AssayA/old.txt" "new.txt")

                    do! expectRelativePathExists arcPath "assays/AssayA/old.txt" false
                    do! expectRelativePathExists arcPath "assays/AssayA/new.txt" true

                    do! writeRelativeFileAsync arcPath "assays/AssayA/conflict.txt" "conflict"

                    match!
                        ArcFileSystemHelper.renameGenericFileSystemItemOnDisk
                            arcPath
                            (renameRequest "assays/AssayA/new.txt" "conflict.txt")
                    with
                    | Ok _ -> failwith "Expected generic rename conflict to fail."
                    | Error error -> Vitest.expect(error.Message).toContain ("destination already exists")
                })
        )

        Vitest.test (
            "renames a folder to a name that looks like a lock file",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    do! createRelativeDirectoryAsync arcPath "assays/AssayA/archive"

                    do! renameItemOrFail arcPath (renameRequest "assays/AssayA/archive" "~$archive.old")

                    do! expectRelativePathExists arcPath "assays/AssayA/archive" false
                    do! expectRelativePathExists arcPath "assays/AssayA/~$archive.old" true
                })
        )

        Vitest.test (
            "deletes generic files while leaving the ARC entity intact",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let createdFilePath = "assays/AssayA/protocol.md"
                    do! writeRelativeFileAsync arcPath createdFilePath "protocol"
                    do! expectRelativePathExists arcPath createdFilePath true

                    do! deleteItemOrFail arcPath createdFilePath
                    do! expectRelativePathExists arcPath createdFilePath false

                    let! reloadedArc = TestHelpers.loadArcAsync arcPath
                    Vitest.expect(reloadedArc.ContainsAssay("AssayA")).toBe (true)
                })
        )

        Vitest.test (
            "moves generic note folders with nested assets and requires overwrite for conflicts",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceFolder = "notes/2026-06-15/Field_observations"
                    let targetFolder = "assays/AssayA/protocols/Sampling_protocol"

                    do! createRelativeDirectoryAsync arcPath $"{sourceFolder}/assets"
                    do! writeRelativeFileAsync arcPath $"{sourceFolder}/Field_observations.md" "source"
                    do! writeRelativeFileAsync arcPath $"{sourceFolder}/assets/image.txt" "asset"

                    do! moveItemOrFail arcPath (moveRequest sourceFolder targetFolder false)

                    do! expectRelativePathExists arcPath sourceFolder false
                    do! expectRelativePathExists arcPath $"{targetFolder}/Field_observations.md" true
                    do! expectRelativePathExists arcPath $"{targetFolder}/assets/image.txt" true

                    let secondSourceFolder = "notes/2026-06-16/Field_observations"
                    do! createRelativeDirectoryAsync arcPath secondSourceFolder
                    do! writeRelativeFileAsync arcPath $"{secondSourceFolder}/Field_observations.md" "replacement"

                    match!
                        ArcFileSystemHelper.moveGenericFileSystemItemOnDisk
                            arcPath
                            (moveRequest secondSourceFolder targetFolder false)
                    with
                    | Ok() -> failwith "Expected move without overwrite to reject an existing target."
                    | Error error -> Vitest.expect(error.Message).toContain ("destination already exists")

                    do! moveItemOrFail arcPath (moveRequest secondSourceFolder targetFolder true)
                    do! expectRelativePathExists arcPath secondSourceFolder false
                    do! expectRelativePathExists arcPath $"{targetFolder}/Field_observations.md" true
                })
        )

        Vitest.test (
            "moves generic files into descendant paths",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceFile = "notes/raw"
                    let targetFile = "notes/raw/archive/raw.txt"

                    do! createRelativeDirectoryAsync arcPath "notes"
                    do! writeRelativeFileAsync arcPath sourceFile "raw"

                    do! moveItemOrFail arcPath (moveRequest sourceFile targetFile false)

                    let! sourceFileStillExists =
                        ARCtrl.FileSystemHelper.fileExistsAsync (absoluteArcPath arcPath sourceFile)

                    let! sourcePathIsDirectory =
                        ARCtrl.FileSystemHelper.directoryExistsAsync (absoluteArcPath arcPath sourceFile)

                    Vitest.expect(sourceFileStillExists).toBe (false)
                    Vitest.expect(sourcePathIsDirectory).toBe (true)
                    do! expectRelativePathExists arcPath targetFile true
                })
        )

        Vitest.test (
            "rejects moving generic folders into descendant paths",
            fun () ->
                withAssayArc (fun arcPath -> promise {
                    let sourceFolder = "notes/folder"
                    let targetFolder = "notes/folder/archive/folder"

                    do! createRelativeDirectoryAsync arcPath sourceFolder

                    match!
                        ArcFileSystemHelper.moveGenericFileSystemItemOnDisk
                            arcPath
                            (moveRequest sourceFolder targetFolder false)
                    with
                    | Ok() -> failwith "Expected moving a folder into itself to fail."
                    | Error error -> Vitest.expect(error.Message).toContain ("inside the source path")

                    do! expectRelativePathExists arcPath sourceFolder true
                    do! expectRelativePathExists arcPath targetFolder false
                })
        )

        Vitest.test (
            "retries transient recursive remove errors",
            fun () -> promise {
                let mutable attempts = 0
                let targetPath = "/tmp/swate-retry-target"

                let removePathAsync path = promise {
                    attempts <- attempts + 1
                    Vitest.expect(path).toBe (targetPath)

                    if attempts < 3 then
                        return raise (nodeError "directory is still changing" "ENOTEMPTY")
                }

                match! removePathWithRetriesAsync removePathAsync targetPath with
                | Error error -> return failwith error.Message
                | Ok() ->
                    Vitest.expect(attempts).toBe (3)
                    return ()
            }
        )
)
