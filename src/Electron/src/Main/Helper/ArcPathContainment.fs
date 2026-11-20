module Main.Helper.ArcPathContainment

open System
open Fable.Core
open Swate.Components.Shared
open Main.Bindings
open Main.Bindings.Filesystem
open Main.Bindings.Path

let private isWin32 = Node.processPlatform () = "win32"

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
        && not (isAbsolute normalizedPath)
        && not (containsTraversalSegments normalizedPath)

    let isWithinRootPath (rootPath: string) (candidatePath: string) =
        let normalizedRootPath = resolve [| rootPath |] |> normalizePathForComparison

        let normalizedCandidatePath =
            resolve [| candidatePath |] |> normalizePathForComparison

        normalizedCandidatePath = normalizedRootPath
        || normalizedCandidatePath.StartsWith(normalizedRootPath + "/")

let resolveAbsolutePath (pathValue: string) = resolve [| pathValue |]

let tryGetArcRelativePath (arcPath: string) (requestedAbsolutePath: string) =
    let arcRoot = resolveAbsolutePath arcPath
    let absolutePath = resolveAbsolutePath requestedAbsolutePath

    let relativePath = relative arcRoot absolutePath |> PathHelpers.normalizePath

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
        if isAbsolute relativePath then
            Error(exn "RelativePath must not be absolute.")
        else
            Error(exn "RelativePath must not contain path traversal segments.")
    else
        let arcRoot = resolveAbsolutePath arcPath
        let absolutePath = resolve [| arcRoot; relativePath |]

        if ArcPathValidation.isWithinRootPath arcRoot absolutePath then
            Ok absolutePath
        else
            Error(exn "RelativePath resolves outside the ARC root.")

/// What the path of a save or an open resolves to. The folder strings are the real paths of the parent folder.
type TargetState =
    | Missing of realParent: string
    | RegularFile of Stats * realParent: string
    | SymbolicLink
    | OutsideArc
    | NotRegularFile
    | ParentMissing

let tryLstatAsync (path: string) : JS.Promise<Stats option> = promise {
    try
        let! stats = lstatAsync path
        return Some stats
    with error ->
        match tryGetNodeErrorCode error with
        | Some "ENOENT" -> return None
        | _ -> return raise error
}

/// The path with forward slashes and without a trailing one. Windows paths compare case-insensitively.
let private normalizeForContainment (path: string) =
    let normalized = path.Replace('\\', '/').TrimEnd('/')

    if isWin32 then
        normalized.ToLowerInvariant()
    else
        normalized

let isSameOrInside (root: string) (candidate: string) =
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
