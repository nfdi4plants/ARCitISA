/// Compares the structural ARC files on disk with the file versions memory was last in sync with.
module Main.ArcDiskState

open Main.ARCtrlExtensions
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared

/// The stat data that tells two versions of a file apart. Reading it needs no workbook parsing.
type FileFingerprint = { SizeBytes: float; MtimeMs: float }

type ScannedFile = {
    /// The ARC-relative path as found on disk.
    RelativePath: string
    Fingerprint: FileFingerprint
}

/// The structural files of an ARC. The key makes paths that name the same file compare equal.
type ArcDiskSnapshot = Map<PathKey, ScannedFile>

type ArcDiskChange =
    | Added of relativePath: string
    | Changed of relativePath: string
    | Removed of relativePath: string

let private hasVanished (error: exn) =
    match tryGetNodeErrorCode error with
    | Some "ENOENT"
    | Some "ENOTDIR" -> true
    | _ -> false

let internal scanWithAsync
    (readEntries: string -> ReaddirOptions -> Fable.Core.JS.Promise<Dirent[]>)
    (statFile: string -> Fable.Core.JS.Promise<Stats>)
    (caseInsensitive: bool)
    (arcPath: string)
    : Fable.Core.JS.Promise<ArcDiskSnapshot> =
    promise {
        // A folder below the root that vanishes during the scan counts as missing. A missing root stays
        // an error, because an empty result would look like every file of the ARC was deleted.
        let readEntriesIfPresent (path: string) (options: ReaddirOptions) = promise {
            try
                return! readEntries path options
            with error ->
                if path <> arcPath && hasVanished error then
                    return [||]
                else
                    return raise error
        }

        let! relativePaths = discoverStructuralArcFilePathsWithAsync readEntriesIfPresent arcPath

        let! scannedFiles =
            relativePaths
            |> Array.map (fun relativePath -> promise {
                try
                    let! stats = statFile (join [| arcPath; relativePath |])

                    return
                        Some {
                            RelativePath = relativePath
                            Fingerprint = {
                                SizeBytes = stats.size
                                MtimeMs = stats.mtimeMs
                            }
                        }
                with error ->
                    if hasVanished error then
                        return None
                    else
                        return raise error
            })
            |> Promise.all

        return
            scannedFiles
            |> Array.choose id
            |> Array.map (fun file -> PathKey.ofPath caseInsensitive file.RelativePath, file)
            |> Map.ofArray
    }

/// Lists and stats the structural files of an ARC. It reads no workbook content.
let scan (caseInsensitive: bool) (arcPath: string) =
    scanWithAsync readdirWithTypesAsync statAsync caseInsensitive arcPath

/// The changes that lead from one snapshot to another.
let diff (known: ArcDiskSnapshot) (current: ArcDiskSnapshot) : ArcDiskChange list = [
    for KeyValue(key, file) in current do
        match known |> Map.tryFind key with
        | None -> yield Added file.RelativePath
        | Some knownFile when knownFile.Fingerprint <> file.Fingerprint -> yield Changed file.RelativePath
        | Some _ -> ()

    for KeyValue(key, file) in known do
        if not (current |> Map.containsKey key) then
            yield Removed file.RelativePath
]

/// The known snapshot after an own write. Only entries that differ between the scans around the write move,
/// so a file that already differed from the known snapshot before the write still loads in the next sync.
let acknowledge (known: ArcDiskSnapshot) (pre: ArcDiskSnapshot) (post: ArcDiskSnapshot) : ArcDiskSnapshot =
    let withWritten =
        post
        |> Map.fold
            (fun state key file ->
                match pre |> Map.tryFind key with
                | Some preFile when preFile.Fingerprint = file.Fingerprint -> state
                | _ -> state |> Map.add key file
            )
            known

    pre
    |> Map.fold
        (fun state key _ ->
            if post |> Map.containsKey key then
                state
            else
                state |> Map.remove key
        )
        withWritten
