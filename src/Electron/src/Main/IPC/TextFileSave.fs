/// Writes a text file the renderer opened. The bytes go to a temporary file next to the target, and a rename
/// publishes them, so a failed save never leaves a half-written target. The module has no vault access.
/// A save replaces the file. Hard links, ACLs and Windows file attributes of the old file are not kept, and the
/// owner and group become those of the saving user. The permission bits of an existing file are kept on POSIX.
module Main.IPC.TextFileSave

open System.Text.RegularExpressions
open Fable.Core
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Main.Bindings
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.IPC.FileSystemIO

let private isWin32 = Node.processPlatform () = "win32"

let private renameRetryDelaysMs = [ 50; 100; 200; 400 ]

let private maxTempNameAttempts = 5

let private staleTempAgeMs = 10.0 * 60.0 * 1000.0

/// Folders with no save temporaries left after a cleanup in this run. They are not scanned again, so a temporary
/// that a failed save could not remove stays until the next run.
let private cleanedTempFolders = System.Collections.Generic.HashSet<string>()

/// Encodes the text the way the file was read. A leading U+FEFF in the content stays part of the text.
let encodeTextContent (encoding: TextFileEncoding) (content: string) : Result<byte[], string> =
    match encoding with
    | TextFileEncoding.Utf8 -> Ok(bufferFromString content "utf8")
    | TextFileEncoding.Utf8Bom ->
        Ok(
            bufferConcat [|
                [| 0xEFuy; 0xBBuy; 0xBFuy |]
                bufferFromString content "utf8"
            |]
        )
    | TextFileEncoding.Utf16LE ->
        Ok(
            bufferConcat [|
                [| 0xFFuy; 0xFEuy |]
                bufferFromString content "utf16le"
            |]
        )
    | TextFileEncoding.Utf16BE ->
        Ok(
            bufferConcat [|
                [| 0xFEuy; 0xFFuy |]
                bufferSwap16 (bufferFromString content "utf16le")
            |]
        )
    | TextFileEncoding.Windows1252 -> Error "This file is read-only because it is not valid UTF-8."

type private PublishResult =
    | Published
    | Refused of SaveTextFileOutcome
    | Failed of exn

/// What a save compares the target with right before the rename.
type private Baseline =
    | NewFile
    | ExistingFile of hash: string

/// What the path of a save or an open resolves to. The folder strings are the real paths of the parent folder.
type TargetState =
    | Missing of realParent: string
    | RegularFile of Stats * realParent: string
    | SymbolicLink
    | OutsideArc
    | NotRegularFile
    | ParentMissing

let private tryLstatAsync (path: string) : JS.Promise<Stats option> = promise {
    try
        let! stats = lstatAsync path
        return Some stats
    with error ->
        match tryGetNodeErrorCode error with
        | Some "ENOENT" -> return None
        | _ -> return raise error
}

/// Hashes the file with a stream and compares size and mtime before and after. The flag is false when they differ.
let private readStableAsync (path: string) : JS.Promise<string * Stats * bool> = promise {
    let! before = statAsync path
    let! hash = Crypto.hashFileAsync path
    let! after = statAsync path
    return hash, before, (before.size = after.size && before.mtimeMs = after.mtimeMs)
}

/// Compares the target with the baseline. None means no change. Some None means the target is gone, is a
/// symbolic link or is not a regular file. Some (Some hash) means a regular file with other content, which also
/// covers a file that changed while it was hashed, or a new file that appeared where none was expected.
let private tryFindTargetChangeAsync (target: string) (baseline: Baseline) : JS.Promise<string option option> = promise {
    match! tryLstatAsync target with
    | None ->
        match baseline with
        | NewFile -> return None
        | ExistingFile _ -> return Some None
    | Some stats when stats.isSymbolicLink () || not (stats.isFile ()) -> return Some None
    | Some _ ->
        try
            let! hash, _, stable = readStableAsync target

            match baseline with
            | ExistingFile expectedHash when stable && hash = expectedHash -> return None
            | _ -> return Some(Some hash)
        with error ->
            match tryGetNodeErrorCode error with
            | Some "ENOENT" -> return Some None
            | _ -> return raise error
}

let private tryOpenTempAsync (tempPath: string) (mode: int) : JS.Promise<FileHandle option> = promise {
    try
        let! handle = openWithModeAsync tempPath "wx" mode
        return Some handle
    with error ->
        match tryGetNodeErrorCode error with
        | Some "EEXIST" -> return None
        | _ -> return raise error
}

let private createTempAsync (folder: string) (mode: int) : JS.Promise<string * FileHandle> = promise {
    let result = ref None
    let attempt = ref 0

    while result.Value.IsNone && attempt.Value < maxTempNameAttempts do
        attempt.Value <- attempt.Value + 1

        let tempPath =
            join [|
                folder
                textSaveTempPrefix + System.Guid.NewGuid().ToString("N") + ".tmp"
            |]

        let! handle = tryOpenTempAsync tempPath mode
        result.Value <- handle |> Option.map (fun h -> tempPath, h)

    match result.Value with
    | Some created -> return created
    | None -> return raise (exn "Could not create a temporary file for the save.")
}

let private removeTempAsync (tempPath: string) : JS.Promise<unit> = promise {
    try
        do! rmAsync tempPath (RmOptions(force = true))
    with _ ->
        ()
}

/// The path with forward slashes and without a trailing one. Windows paths compare case-insensitively.
let private normalizeForContainment (path: string) =
    let normalized = path.Replace('\\', '/').TrimEnd('/')

    if isWin32 then
        normalized.ToLowerInvariant()
    else
        normalized

let private isSameOrInside (root: string) (candidate: string) =
    let root = normalizeForContainment root
    let candidate = normalizeForContainment candidate
    candidate = root || candidate.StartsWith(root + "/")

let private tryRealpathAsync (path: string) : JS.Promise<string option> = promise {
    try
        let! resolved = realpathAsync path
        return Some resolved
    with error ->
        match tryGetNodeErrorCode error with
        | Some "ENOENT" -> return None
        | _ -> return raise error
}

/// Resolves the real folder of the target and checks that it lies inside the real ARC root. The open and the
/// save use it, so both agree on what is writable.
let checkTargetAsync (arcRoot: string) (absoluteTarget: string) : JS.Promise<Result<TargetState, exn>> = promise {
    try
        let! realRoot = realpathAsync arcRoot
        let! realParent = tryRealpathAsync (dirname absoluteTarget)

        match realParent with
        | None -> return Ok ParentMissing
        | Some realParent when not (isSameOrInside realRoot realParent) -> return Ok OutsideArc
        | Some realParent ->
            let! stats = tryLstatAsync (join [| realParent; basename absoluteTarget |])

            match stats with
            | None -> return Ok(Missing realParent)
            | Some stats when stats.isSymbolicLink () -> return Ok SymbolicLink
            | Some stats when not (stats.isFile ()) -> return Ok NotRegularFile
            | Some stats -> return Ok(RegularFile(stats, realParent))
    with error ->
        return Error error
}

/// True for an existing file without the owner write bit. Node maps the Windows read-only attribute to it.
let isWriteProtected (stats: Stats) : bool = stats.mode &&& 0o200 = 0

/// Writes the bytes to the temporary file and closes its handle. The handle is closed on failure as well.
/// finalMode is the mode of the file that gets replaced, None for a new file, which keeps the mode it was created with.
let private writeTempAsync (handle: FileHandle) (bytes: byte[]) (finalMode: int option) : JS.Promise<unit> = promise {
    try
        do! handle.writeFile bytes

        match finalMode with
        | Some mode when not isWin32 ->
            let permissions = mode &&& 0o7777
            do! handle.chmod permissions
            let! written = handle.stat ()

            if (written.mode &&& 0o7777) <> permissions then
                raise (exn "Could not keep the file permissions.")
        | _ -> ()

        do! handle.sync ()
    with error ->
        try
            do! handle.close ()
        with _ ->
            ()

        return raise error

    do! handle.close ()
}

let private publishAsync (target: string) (tempPath: string) (recheck: Baseline option) : JS.Promise<PublishResult> =
    let rec attempt (delays: int list) : JS.Promise<PublishResult> = promise {
        let! change =
            match recheck with
            | Some baseline -> tryFindTargetChangeAsync target baseline
            | None -> promise { return None }

        match change with
        | Some currentHash -> return Refused(SaveTextFileOutcome.Conflict currentHash)
        | None ->
            let! renameError = promise {
                try
                    do! renameAsync tempPath target
                    return None
                with error ->
                    return Some error
            }

            match renameError with
            | None -> return Published
            | Some error ->
                match tryGetNodeErrorCode error, delays with
                | Some "EBUSY", delay :: rest ->
                    do! Promise.sleep delay
                    return! attempt rest
                | Some("EPERM" | "EACCES"), delay :: rest when isWin32 ->
                    do! Promise.sleep delay
                    return! attempt rest
                | Some("EPERM" | "EACCES" | "EBUSY" as code), _ ->
                    if isWin32 then
                        return
                            Failed(
                                exn $"The file is in use by another program. Close it there and save again. ({code})"
                            )
                    else
                        return Failed(exn $"The file could not be replaced ({code}). Check the folder permissions.")
                | _ -> return raise error
    }

    attempt renameRetryDelaysMs

let private syncDirectoryAsync (directory: string) : JS.Promise<unit> = promise {
    try
        let! handle = openAsync directory "r"

        try
            do! handle.sync ()
        with _ ->
            ()

        do! handle.close ()
    with _ ->
        ()
}

/// Deletes save temporary files that an earlier crash left behind. Only files older than ten minutes go, so a save
/// that runs right now is safe. True when no save temporary is left. An error counts as a temporary left behind.
let private removeStaleTempsAsync (folder: string) : JS.Promise<bool> = promise {
    let leftOver = ref false

    try
        let! names = readdirAsync folder
        let now = JS.Constructors.Date.now ()

        for name in names do
            if Regex.IsMatch(name, textSaveTempPattern) then
                try
                    let path = join [| folder; name |]
                    let! stats = lstatAsync path

                    if stats.isFile () then
                        if now - stats.mtimeMs > staleTempAgeMs then
                            do! rmAsync path (RmOptions(force = true))
                        else
                            leftOver.Value <- true
                with _ ->
                    leftOver.Value <- true

        return not leftOver.Value
    with _ ->
        return false
}

let private saveToResolvedTargetAsync
    (request: SaveTextFileRequest)
    (parent: string)
    (realTarget: string)
    (existingFile: bool)
    : JS.Promise<Result<SaveTextFileOutcome * string, exn>> =
    promise {
        let realParent = dirname realTarget

        // Ok None is a new file. Ok (hash, mode) is the file as read. Error holds the hash of a conflict.
        let! existing = promise {
            if not existingFile then
                return Ok None
            else
                let! hash, stats, stable = readStableAsync realTarget

                if not stable || (not request.overwrite && request.expectedHash <> Some hash) then
                    return Error(Some hash)
                else
                    return Ok(Some(hash, stats.mode))
        }

        match existing with
        | Error currentHash -> return Ok(SaveTextFileOutcome.Conflict currentHash, parent)
        | Ok existing ->
            match encodeTextContent request.encoding request.content with
            | Error message -> return Error(exn message)
            | Ok bytes ->
                // An existing file gets 0600 first and its own mode after the write. A new file gets 0666 and the umask decides.
                let! tempPath, handle = createTempAsync realParent (if existing.IsSome then 0o600 else 0o666)

                let recheck =
                    match existing with
                    | Some(hash, _) -> Some(ExistingFile hash)
                    | None when not request.overwrite -> Some NewFile
                    | None -> None

                let! published = promise {
                    try
                        do! writeTempAsync handle bytes (existing |> Option.map snd)
                        return! publishAsync realTarget tempPath recheck
                    with error ->
                        do! removeTempAsync tempPath
                        return raise error
                }

                match published with
                | Published ->
                    if not (cleanedTempFolders.Contains realParent) then
                        let! clean = removeStaleTempsAsync realParent

                        if clean then
                            cleanedTempFolders.Add realParent |> ignore

                    if not isWin32 then
                        do! syncDirectoryAsync realParent

                    let info =
                        Main.ArcVaultHelper.createTextDocumentInfo
                            request.encoding
                            (Crypto.sha256Hex bytes)
                            (float bytes.Length)
                            None

                    return Ok(SaveTextFileOutcome.Saved info, parent)
                | Refused outcome ->
                    do! removeTempAsync tempPath
                    return Ok(outcome, parent)
                | Failed error ->
                    do! removeTempAsync tempPath
                    return Error error
    }

/// Saves the text to the ARC-relative path. The string of the result is the absolute parent directory, for the caller's reconcile.
let saveTextFileAsync
    (arcRoot: string)
    (request: SaveTextFileRequest)
    : JS.Promise<Result<SaveTextFileOutcome * string, exn>> =
    promise {
        try
            let fileName =
                getNonEmptyPathParts request.path |> Array.tryLast |> Option.defaultValue ""

            if isIgnoredInventoryEntry fileName request.path false then
                return Error(exn "This path cannot be saved.")
            else
                match tryResolveArcRelativePath arcRoot request.path with
                | Error error -> return Error error
                | Ok target ->
                    let parent = dirname target

                    match! checkTargetAsync arcRoot target with
                    | Error error -> return Error error
                    | Ok ParentMissing -> return Error(exn "The folder of this file no longer exists.")
                    | Ok OutsideArc -> return Error(exn "The file is outside the open ARC.")
                    | Ok SymbolicLink
                    | Ok NotRegularFile -> return Error(exn "Only regular files can be saved.")
                    | Ok(Missing _) when request.expectedHash.IsSome && not request.overwrite ->
                        return Ok(SaveTextFileOutcome.Conflict None, parent)
                    | Ok(Missing realParent) ->
                        return! saveToResolvedTargetAsync request parent (join [| realParent; basename target |]) false
                    | Ok(RegularFile(stats, _)) when isWriteProtected stats ->
                        return Error(exn "This file is write-protected.")
                    | Ok(RegularFile(_, realParent)) ->
                        return! saveToResolvedTargetAsync request parent (join [| realParent; basename target |]) true
        with error ->
            return Error error
    }
