namespace Swate.Components.Shared

open System
open Fable.Core


[<RequireQualifiedAccess>]
module PathHelpers =

    let formatContractErrors (errors: string[]) =
        errors |> Array.map string |> String.concat "\n"

    /// Normalizes path separators by replacing backslashes with forward slashes.
    let normalizeSeparators (path: string) = path.Replace("\\", "/")

    /// Normalizes separators, trims surrounding whitespace, and removes trailing slashes.
    let normalizePath (path: string) =
        normalizeSeparators path |> fun normalized -> normalized.Trim().TrimEnd('/')

    /// Normalizes separators and removes trailing slashes without trimming whitespace, because a folder name can
    /// end in whitespace on Linux and macOS. The file tree uses it for the paths it keys and sends.
    let normalizePathKeepingWhitespace (path: string) = (normalizeSeparators path).TrimEnd('/')

    /// Normalizes separators and trims surrounding whitespace and leading or trailing slashes.
    let normalizeRelativePath (path: string) =
        normalizeSeparators path |> fun normalized -> normalized.Trim('/').Trim()

    /// Produces a canonical relative path by normalizing separators, whitespace, and boundary slashes.
    let normalizeCanonicalRelativePath (path: string) =
        path |> normalizeRelativePath |> normalizePath

    /// Normalizes separators, whitespace, and trailing slashes, then lowercases the path for comparison.
    let normalizeForComparison (path: string) =
        normalizeSeparators path
        |> fun normalized -> normalized.Trim().TrimEnd('/').ToLowerInvariant()

    /// Composes a string to Unicode NFC, so an NFD name from macOS readdir equals git's NFC form.
    let normalizeToNfc (value: string) =
        value.Normalize(System.Text.NormalizationForm.FormC)

    let normalizeForUnicodeComparison (path: string) =
        normalizeSeparators path
        |> normalizeToNfc
        |> fun normalized -> normalized.ToLowerInvariant()

    /// Produces a normalized, case-insensitive path suitable for filesystem comparisons.
    let normalizePathForFsComparison (path: string) =
        path |> normalizePath |> normalizeForComparison

    let isSameOrDescendantPath (path: string) (ancestorPath: string) =
        let normalizedPath = normalizePath path
        let normalizedAncestorPath = normalizePath ancestorPath

        String.IsNullOrWhiteSpace normalizedAncestorPath
        || normalizedPath = normalizedAncestorPath
        || normalizedPath.StartsWith(normalizedAncestorPath + "/", StringComparison.OrdinalIgnoreCase)

    let isSameOrDescendantPathForFsComparison (path: string) (ancestorPath: string) =
        let normalizedPath = normalizePathForFsComparison path
        let normalizedAncestorPath = normalizePathForFsComparison ancestorPath

        not (String.IsNullOrWhiteSpace normalizedPath)
        && not (String.IsNullOrWhiteSpace normalizedAncestorPath)
        && (normalizedPath = normalizedAncestorPath
            || normalizedPath.StartsWith(normalizedAncestorPath + "/"))

    let tryRemapPathPrefix (sourcePath: string) (targetPath: string) (path: string) =
        let normalizedSourcePath = normalizeCanonicalRelativePath sourcePath
        let normalizedTargetPath = normalizeCanonicalRelativePath targetPath
        let normalizedPath = normalizeCanonicalRelativePath path

        if normalizeForComparison normalizedPath = normalizeForComparison normalizedSourcePath then
            Some normalizedTargetPath
        elif isSameOrDescendantPath normalizedPath normalizedSourcePath then
            Some(normalizedTargetPath + normalizedPath.Substring(normalizedSourcePath.Length))
        else
            None

    let containsPathTraversalSegments (path: string) =
        normalizeSeparators path
        |> fun normalized -> normalized.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.exists (fun segment -> segment = "." || segment = "..")

    let isSafePathSegment (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && not (value.Contains "/")
        && not (value.Contains "\\")
        && not (value.Contains "..")

    let pathsEqual (left: string) (right: string) =
        normalizeForComparison left = normalizeForComparison right

    let pathMatchesAny (candidates: string seq) (path: string) =
        candidates |> Seq.exists (fun candidate -> pathsEqual candidate path)

    let getNameFromPath (path: string) =
        normalizePath path |> (fun normalized -> normalized.Split('/')) |> Array.last

    let tryGetParentPath (path: string) =
        let normalizedPath = normalizePath path
        let separatorIndex = normalizedPath.LastIndexOf('/')

        if separatorIndex < 0 then
            None
        else
            Some(normalizedPath.Substring(0, separatorIndex))

    /// The parent of a path like tryGetParentPath, without trimming whitespace.
    let tryGetParentPathKeepingWhitespace (path: string) =
        let normalizedPath = normalizePathKeepingWhitespace path
        let separatorIndex = normalizedPath.LastIndexOf('/')

        if separatorIndex < 0 then
            None
        else
            Some(normalizedPath.Substring(0, separatorIndex))

    let private tryResolveDatamapPreviewPath (normalizedPath: string) (folderName: string) (targetFileName: string) =
        let pathSegments =
            normalizedPath.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)

        match pathSegments with
        | [| firstSegment; _; lastSegment |] when
            String.Equals(firstSegment, folderName, StringComparison.OrdinalIgnoreCase)
            && String.Equals(lastSegment, "isa.datamap.xlsx", StringComparison.OrdinalIgnoreCase)
            ->
            tryGetParentPath normalizedPath
            |> Option.map (fun folderPath -> $"{folderPath}/{targetFileName}")
        | _ -> None

    let resolveArcViewPath (path: string) =
        let normalizedPath = normalizePath path

        [
            tryResolveDatamapPreviewPath normalizedPath "assays" "isa.assay.xlsx"
            tryResolveDatamapPreviewPath normalizedPath "studies" "isa.study.xlsx"
            tryResolveDatamapPreviewPath normalizedPath "workflows" "isa.workflow.xlsx"
            tryResolveDatamapPreviewPath normalizedPath "runs" "isa.run.xlsx"
        ]
        |> List.tryPick id
        |> Option.defaultValue normalizedPath

    /// normalizes the path and splits it into parts
    let getPathParts (path: string) =
        normalizePath path |> fun p -> p.Split('/')

    let getFileName (path: string) = path |> getPathParts |> Array.last

    let isProtectedDeleteTarget protectedDeleteTargetNames (normalizedRelativePath: string) =
        normalizedRelativePath
        |> getFileName
        |> pathMatchesAny protectedDeleteTargetNames

    let pathExistsInSnapshot (existingPaths: string seq) (targetPath: string) =
        existingPaths
        |> Seq.exists (fun existingPath -> pathsEqual existingPath targetPath)

/// Shared ordering for directory entry names, so listings do not depend on readdir order.
[<RequireQualifiedAccess>]
module PathOrder =

    /// Orders names ordinally ignoring case, then ordinally so names differing only in case stay distinct.
    let compareNames (left: string) (right: string) =
        match String.Compare(left, right, StringComparison.OrdinalIgnoreCase) with
        | 0 -> String.CompareOrdinal(left, right)
        | result -> result

[<RequireQualifiedAccess>]
module ArcEntityPathRules =

    type AddZone =
        | Studies
        | Assays
        | Workflows
        | Runs

    type CanonicalArcFileTarget =
        | InvestigationFile
        | EntityFile of zone: AddZone * identifier: string
        | DataMapFile of zone: AddZone * identifier: string

    type DeletePathClassification =
        | ProtectedTarget of normalizedRelativePath: string
        | CanonicalFileTarget of target: CanonicalArcFileTarget * normalizedRelativePath: string
        | EntityFolderTarget of zone: AddZone * identifier: string * normalizedRelativePath: string
        | AddZoneDescendantTarget of zone: AddZone * normalizedRelativePath: string
        | GenericTarget of normalizedRelativePath: string
        | DisallowedTarget of normalizedRelativePath: string

    type RenamePathClassification =
        | RootTarget
        | DisallowedTarget of normalizedRelativePath: string
        | ProtectedTarget of normalizedRelativePath: string
        | InvestigationFileTarget of normalizedRelativePath: string
        | AddZoneRootTarget of zone: AddZone * normalizedRelativePath: string
        | EntityFolderTarget of zone: AddZone * identifier: string * normalizedRelativePath: string
        | CanonicalEntityFileTarget of zone: AddZone * identifier: string * normalizedRelativePath: string
        | CanonicalDataMapFileTarget of zone: AddZone * identifier: string * normalizedRelativePath: string
        | GenericTarget of normalizedRelativePath: string

    type private StructuralArcPath =
        | AddZoneRoot
        | EntityFolder
        | ProtectedEntityChildFolder
        | OtherPath

    let private protectedDeleteTargetNames = [ ".gitattributes"; ".gitkeep"; "readme.md" ]
    let private creatableProtectedFileNames = [ "readme.md" ]
    let private protectedRenameRootFolderNames = [ "notes" ]
    let private disallowedGenericPathSegments = [ ".git" ]

    let private normalizeRelativePath (path: string) =
        path |> PathHelpers.normalizeCanonicalRelativePath

    let private splitPathSegments (path: string) =
        path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)

    let allAddZones = [
        AddZone.Studies
        AddZone.Assays
        AddZone.Workflows
        AddZone.Runs
    ]

    let zoneFolderName =
        function
        | AddZone.Studies -> ARCtrl.ArcPathHelper.StudiesFolderName
        | AddZone.Assays -> ARCtrl.ArcPathHelper.AssaysFolderName
        | AddZone.Workflows -> ARCtrl.ArcPathHelper.WorkflowsFolderName
        | AddZone.Runs -> ARCtrl.ArcPathHelper.RunsFolderName

    let zoneEntityFileName =
        function
        | AddZone.Studies -> ARCtrl.ArcPathHelper.StudyFileName
        | AddZone.Assays -> ARCtrl.ArcPathHelper.AssayFileName
        | AddZone.Workflows -> ARCtrl.ArcPathHelper.WorkflowFileName
        | AddZone.Runs -> ARCtrl.ArcPathHelper.RunFileName

    let private nativeEntityChildFolderNames =
        function
        | AddZone.Studies -> [
            ARCtrl.ArcPathHelper.StudiesProtocolsFolderName
            ARCtrl.ArcPathHelper.StudiesResourcesFolderName
          ]
        | AddZone.Assays -> [
            ARCtrl.ArcPathHelper.AssayDatasetFolderName
            ARCtrl.ArcPathHelper.AssayProtocolsFolderName
          ]
        | AddZone.Workflows
        | AddZone.Runs -> []

    let private tryParseZone (segment: string) =
        if PathHelpers.pathsEqual segment ARCtrl.ArcPathHelper.StudiesFolderName then
            Some AddZone.Studies
        elif PathHelpers.pathsEqual segment ARCtrl.ArcPathHelper.AssaysFolderName then
            Some AddZone.Assays
        elif PathHelpers.pathsEqual segment ARCtrl.ArcPathHelper.WorkflowsFolderName then
            Some AddZone.Workflows
        elif PathHelpers.pathsEqual segment ARCtrl.ArcPathHelper.RunsFolderName then
            Some AddZone.Runs
        else
            None

    let private isAddZoneSegment segment = (tryParseZone segment).IsSome

    /// Root-level license files that ARCtrl reads into ARC.License.
    let licenseFileNames =
        ARCtrl.ArcPathHelper.LICENSEFileName
        :: ARCtrl.ArcPathHelper.alternativeLICENSEFileNames

    /// Root-level files that ARCtrl reads into the ARC model.
    let structuralRootFileNames =
        ARCtrl.ArcPathHelper.InvestigationFileName :: licenseFileNames

    let private classifyStructuralArcPath =
        function
        | [| zoneSegment |] when isAddZoneSegment zoneSegment -> StructuralArcPath.AddZoneRoot
        | [| zoneSegment; name |] when
            isAddZoneSegment zoneSegment
            && not (name.StartsWith(".", StringComparison.Ordinal))
            ->
            StructuralArcPath.EntityFolder
        | [| zoneSegment; _; childFolderName |] ->
            match tryParseZone zoneSegment with
            | Some zone when PathHelpers.pathMatchesAny (nativeEntityChildFolderNames zone) childFolderName ->
                StructuralArcPath.ProtectedEntityChildFolder
            | _ -> StructuralArcPath.OtherPath
        | _ -> StructuralArcPath.OtherPath

    let private blocksGenericFileSystemTarget =
        function
        | StructuralArcPath.AddZoneRoot
        | StructuralArcPath.EntityFolder
        | StructuralArcPath.ProtectedEntityChildFolder -> true
        | StructuralArcPath.OtherPath -> false

    let private isProtectedTarget normalizedRelativePath =
        function
        | StructuralArcPath.ProtectedEntityChildFolder -> true
        | _ -> PathHelpers.isProtectedDeleteTarget protectedDeleteTargetNames normalizedRelativePath

    let private tryParseCanonicalArcFileTargetFromSegments (segments: string[]) =
        if segments.Length = 0 then
            None
        elif PathHelpers.pathsEqual segments.[segments.Length - 1] ARCtrl.ArcPathHelper.InvestigationFileName then
            Some CanonicalArcFileTarget.InvestigationFile
        elif segments.Length >= 3 then
            let fileName = segments.[segments.Length - 1]
            let identifier = segments.[segments.Length - 2]
            let folder = segments.[segments.Length - 3]

            match tryParseZone folder with
            | None -> None
            | Some zone when PathHelpers.pathsEqual fileName (zoneEntityFileName zone) ->
                Some(CanonicalArcFileTarget.EntityFile(zone, identifier))
            | Some zone when PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.DatamapFileName ->
                Some(CanonicalArcFileTarget.DataMapFile(zone, identifier))
            | Some _ -> None
        else
            None

    let private isCanonicalArcFileName (fileName: string) =
        PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.InvestigationFileName
        || PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.StudyFileName
        || PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.AssayFileName
        || PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.WorkflowFileName
        || PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.RunFileName
        || PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.DatamapFileName

    let private containsDisallowedGenericPathSegment (segments: string[]) =
        segments
        |> Array.exists (PathHelpers.pathMatchesAny disallowedGenericPathSegments)

    /// Parses canonical ARC file targets from the tail of a path and supports absolute paths.
    let tryParseCanonicalArcFileTarget (path: string) =
        path
        |> PathHelpers.normalizePath
        |> splitPathSegments
        |> tryParseCanonicalArcFileTargetFromSegments

    let private platformJunkFileNames = [ ".ds_store"; ".directory"; "thumbs.db"; "desktop.ini" ]

    /// True for the files that operating systems and file managers add to any folder.
    let isPlatformJunkName (name: string) =
        name.StartsWith("._", StringComparison.Ordinal)
        || List.contains (name.ToLowerInvariant()) platformJunkFileNames

    /// True for the paths the permanent watcher observes: the ARC root, root metadata files and `.gitattributes`,
    /// zone folders, entity folders, and the canonical entity file and datamap of each entity. Nothing deeper is
    /// structural, so payload folders are never descended into. A missing directory hint keeps the paths whose
    /// shape allows either kind, because chokidar asks before it has stat data.
    let isStructuralWatcherPath (relativePath: string) (isDirectory: bool option) =
        let segments = relativePath |> normalizeRelativePath |> splitPathSegments

        if segments |> Array.exists isPlatformJunkName then
            false
        else
            let mayBeFile = isDirectory <> Some true
            let mayBeFolder = isDirectory <> Some false

            match segments with
            | [||] -> true
            | [| name |] when isAddZoneSegment name -> mayBeFolder
            | [| name |] ->
                mayBeFile
                && (PathHelpers.pathMatchesAny structuralRootFileNames name
                    || PathHelpers.pathsEqual name ".gitattributes")
            | [| zoneSegment; name |] ->
                mayBeFolder
                && isAddZoneSegment zoneSegment
                && not (name.StartsWith(".", StringComparison.Ordinal))
            | [| zoneSegment; _; fileName |] ->
                match tryParseZone zoneSegment with
                | Some zone ->
                    mayBeFile
                    && (PathHelpers.pathsEqual fileName (zoneEntityFileName zone)
                        || PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.DatamapFileName)
                | None -> false
            | _ -> false

    let classifyDeleteTarget (relativePath: string) =
        let normalizedRelativePath = normalizeRelativePath relativePath
        let segments = normalizedRelativePath |> splitPathSegments
        let structuralPath = classifyStructuralArcPath segments

        if String.IsNullOrWhiteSpace normalizedRelativePath then
            DeletePathClassification.DisallowedTarget normalizedRelativePath
        elif isProtectedTarget normalizedRelativePath structuralPath then
            DeletePathClassification.ProtectedTarget normalizedRelativePath
        else
            match segments with
            | [| singleSegment |] ->
                match tryParseZone singleSegment with
                | Some _ -> DeletePathClassification.DisallowedTarget normalizedRelativePath
                | None when PathHelpers.pathsEqual singleSegment ARCtrl.ArcPathHelper.InvestigationFileName ->
                    DeletePathClassification.CanonicalFileTarget(
                        CanonicalArcFileTarget.InvestigationFile,
                        normalizedRelativePath
                    )
                | None -> DeletePathClassification.GenericTarget normalizedRelativePath
            | [| zoneSegment; identifier |] ->
                match tryParseZone zoneSegment with
                | Some zone when identifier.StartsWith(".", StringComparison.Ordinal) ->
                    DeletePathClassification.AddZoneDescendantTarget(zone, normalizedRelativePath)
                | Some zone -> DeletePathClassification.EntityFolderTarget(zone, identifier, normalizedRelativePath)
                | None -> DeletePathClassification.GenericTarget normalizedRelativePath
            | [| zoneSegment; identifier; fileName |] ->
                match tryParseZone zoneSegment with
                | Some zone when PathHelpers.pathsEqual fileName (zoneEntityFileName zone) ->
                    DeletePathClassification.CanonicalFileTarget(
                        CanonicalArcFileTarget.EntityFile(zone, identifier),
                        normalizedRelativePath
                    )
                | Some zone when PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.DatamapFileName ->
                    DeletePathClassification.CanonicalFileTarget(
                        CanonicalArcFileTarget.DataMapFile(zone, identifier),
                        normalizedRelativePath
                    )
                | Some zone -> DeletePathClassification.AddZoneDescendantTarget(zone, normalizedRelativePath)
                | None ->
                    if PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.InvestigationFileName then
                        DeletePathClassification.CanonicalFileTarget(
                            CanonicalArcFileTarget.InvestigationFile,
                            normalizedRelativePath
                        )
                    else
                        DeletePathClassification.GenericTarget normalizedRelativePath
            | _ ->
                if segments.Length >= 2 then
                    match tryParseZone segments.[0] with
                    | Some zone -> DeletePathClassification.AddZoneDescendantTarget(zone, normalizedRelativePath)
                    | None -> DeletePathClassification.GenericTarget normalizedRelativePath
                else
                    match tryParseCanonicalArcFileTargetFromSegments segments with
                    | Some target -> DeletePathClassification.CanonicalFileTarget(target, normalizedRelativePath)
                    | None -> DeletePathClassification.DisallowedTarget normalizedRelativePath

    let private isGenericFileSystemPathAllowed (isCreate: bool) (isFile: bool) (relativePath: string) =
        let normalizedRelativePath = normalizeRelativePath relativePath
        let segments = normalizedRelativePath |> splitPathSegments
        let structuralPath = classifyStructuralArcPath segments

        let isCreatableProtectedFile =
            isCreate
            && isFile
            && PathHelpers.isProtectedDeleteTarget creatableProtectedFileNames normalizedRelativePath

        let isBlockedStructuralPath =
            match structuralPath with
            | StructuralArcPath.EntityFolder when isFile -> false
            | _ -> blocksGenericFileSystemTarget structuralPath

        if
            String.IsNullOrWhiteSpace normalizedRelativePath
            || PathHelpers.containsPathTraversalSegments normalizedRelativePath
            || (isProtectedTarget normalizedRelativePath structuralPath
                && not isCreatableProtectedFile)
        then
            false
        else
            segments.Length >= 1
            && (segments |> containsDisallowedGenericPathSegment |> not)
            && (PathHelpers.getFileName normalizedRelativePath |> isCanonicalArcFileName |> not)
            && not isBlockedStructuralPath

    /// True when `relativePath` names an item directly inside an add zone (for example `studies/notes.md`
    /// or `studies/.DS_Store`), whatever its classification.
    let isAddZoneRootChildPath (relativePath: string) =
        match normalizeRelativePath relativePath |> splitPathSegments with
        | [| zoneSegment; _ |] -> isAddZoneSegment zoneSegment
        | _ -> false

    /// Checks the name of a new file or rename target. ARC identifiers only allow letters, digits,
    /// `_`, `-` and whitespace, so a file directly inside an add zone must contain a `.` (an extension).
    /// Otherwise a file such as `assays/Assay1` would block a later "Add Assay" with that identifier.
    let isNewGenericFileSystemNameAllowed (relativePath: string) =
        match
            normalizeRelativePath relativePath
            |> splitPathSegments
            |> classifyStructuralArcPath
        with
        | StructuralArcPath.EntityFolder -> (PathHelpers.getFileName relativePath).Contains "."
        | _ -> true

    let isGenericFileSystemTargetAllowed (relativePath: string) =
        isGenericFileSystemPathAllowed false false relativePath

    /// Like `isGenericFileSystemTargetAllowed` for a path that is known to be a file.
    /// Files directly inside an add zone (for example `studies/notes.md`) are generic files.
    let isGenericFileSystemFileTargetAllowed (relativePath: string) =
        isGenericFileSystemPathAllowed false true relativePath

    /// Checks a new generic file or folder path. Unlike rename and delete targets, a missing
    /// protected file such as `readme.md` may be created.
    let isGenericFileSystemCreateTargetAllowed (isFile: bool) (relativePath: string) =
        isGenericFileSystemPathAllowed true isFile relativePath

    let tryNormalizeGenericFileSystemTarget (errorMessage: string) (relativePath: string) : Result<string, string> =
        let normalizedRelativePath = normalizeRelativePath relativePath

        if isGenericFileSystemTargetAllowed normalizedRelativePath then
            Ok normalizedRelativePath
        else
            Error errorMessage

    let isDeletePathAllowed (relativePath: string) =
        match classifyDeleteTarget relativePath with
        | DeletePathClassification.CanonicalFileTarget(CanonicalArcFileTarget.EntityFile _, _)
        | DeletePathClassification.CanonicalFileTarget(CanonicalArcFileTarget.DataMapFile _, _)
        | DeletePathClassification.EntityFolderTarget _ -> true
        | DeletePathClassification.GenericTarget normalizedRelativePath
        | DeletePathClassification.AddZoneDescendantTarget(_, normalizedRelativePath) ->
            isGenericFileSystemTargetAllowed normalizedRelativePath
        | _ -> false

    /// Like `isDeletePathAllowed` when the caller knows whether the item is a file. A file directly
    /// inside an add zone has the path shape of an entity folder but is deleted as a generic file.
    let isItemDeletePathAllowed (isFile: bool) (relativePath: string) =
        match classifyDeleteTarget relativePath with
        | DeletePathClassification.EntityFolderTarget(_, _, normalizedRelativePath) when isFile ->
            isGenericFileSystemFileTargetAllowed normalizedRelativePath
        | _ -> isDeletePathAllowed relativePath

    /// The path that a delete of `relativePath` removes from disk. Deleting a canonical entity
    /// workbook removes the whole entity folder, so the scope of the workbook is its parent folder.
    let deleteScopePath (relativePath: string) =
        match classifyDeleteTarget relativePath with
        | DeletePathClassification.CanonicalFileTarget(CanonicalArcFileTarget.EntityFile _, normalizedRelativePath) ->
            PathHelpers.tryGetParentPath normalizedRelativePath
            |> Option.defaultValue normalizedRelativePath
        | _ -> normalizeRelativePath relativePath

    let private canonicalEntityFilePath zone identifier =
        let zoneFolder = zoneFolderName zone
        let entityFileName = zoneEntityFileName zone
        $"{zoneFolder}/{identifier}/{entityFileName}"

    let private canonicalDataMapFilePath zone identifier =
        let zoneFolder = zoneFolderName zone
        $"{zoneFolder}/{identifier}/{ARCtrl.ArcPathHelper.DatamapFileName}"

    let private canonicalEntityFolderPath zone identifier =
        let zoneFolder = zoneFolderName zone
        $"{zoneFolder}/{identifier}"

    let buildFallbackUnlinkPaths (relativePath: string) =
        let fallbackPaths =
            match classifyDeleteTarget relativePath with
            | DeletePathClassification.CanonicalFileTarget(CanonicalArcFileTarget.EntityFile _, normalizedRelativePath)
            | DeletePathClassification.CanonicalFileTarget(CanonicalArcFileTarget.DataMapFile _, normalizedRelativePath) -> [
                normalizedRelativePath
              ]
            | DeletePathClassification.EntityFolderTarget(zone, identifier, _) -> [
                canonicalEntityFilePath zone identifier
                canonicalDataMapFilePath zone identifier
              ]
            | _ -> []

        fallbackPaths |> Seq.distinctBy PathHelpers.normalizeForComparison |> Seq.toList

    let classifyRenameTarget (relativePath: string) =
        let normalizedRelativePath = normalizeRelativePath relativePath

        if String.IsNullOrWhiteSpace normalizedRelativePath then
            RenamePathClassification.RootTarget
        else
            let segments = normalizedRelativePath |> splitPathSegments
            let structuralPath = classifyStructuralArcPath segments

            if PathHelpers.containsPathTraversalSegments normalizedRelativePath then
                RenamePathClassification.DisallowedTarget normalizedRelativePath
            elif isProtectedTarget normalizedRelativePath structuralPath then
                RenamePathClassification.ProtectedTarget normalizedRelativePath
            else
                match segments with
                | [| singleSegment |] ->
                    if PathHelpers.pathMatchesAny protectedRenameRootFolderNames singleSegment then
                        RenamePathClassification.ProtectedTarget normalizedRelativePath
                    else
                        match tryParseZone singleSegment with
                        | Some zone -> RenamePathClassification.AddZoneRootTarget(zone, normalizedRelativePath)
                        | None when PathHelpers.pathsEqual singleSegment ARCtrl.ArcPathHelper.InvestigationFileName ->
                            RenamePathClassification.InvestigationFileTarget normalizedRelativePath
                        | None -> RenamePathClassification.GenericTarget normalizedRelativePath
                | [| zoneSegment; identifier |] ->
                    match tryParseZone zoneSegment with
                    | Some _ when identifier.StartsWith(".", StringComparison.Ordinal) ->
                        RenamePathClassification.GenericTarget normalizedRelativePath
                    | Some zone -> RenamePathClassification.EntityFolderTarget(zone, identifier, normalizedRelativePath)
                    | None -> RenamePathClassification.GenericTarget normalizedRelativePath
                | [| zoneSegment; identifier; fileName |] ->
                    match tryParseZone zoneSegment with
                    | Some zone when PathHelpers.pathsEqual fileName (zoneEntityFileName zone) ->
                        RenamePathClassification.CanonicalEntityFileTarget(zone, identifier, normalizedRelativePath)
                    | Some zone when PathHelpers.pathsEqual fileName ARCtrl.ArcPathHelper.DatamapFileName ->
                        RenamePathClassification.CanonicalDataMapFileTarget(zone, identifier, normalizedRelativePath)
                    | _ -> RenamePathClassification.GenericTarget normalizedRelativePath
                | _ ->
                    if PathHelpers.pathsEqual normalizedRelativePath ARCtrl.ArcPathHelper.InvestigationFileName then
                        RenamePathClassification.InvestigationFileTarget normalizedRelativePath
                    else
                        RenamePathClassification.GenericTarget normalizedRelativePath

    let isRenamePathAllowed (relativePath: string) =
        match classifyRenameTarget relativePath with
        | RenamePathClassification.EntityFolderTarget _ -> true
        | RenamePathClassification.GenericTarget normalizedRelativePath ->
            isGenericFileSystemTargetAllowed normalizedRelativePath
        | _ -> false

    /// Like `isRenamePathAllowed` when the caller knows whether the item is a file. A file directly
    /// inside an add zone has the path shape of an entity folder but is renamed as a generic file.
    let isItemRenamePathAllowed (isFile: bool) (relativePath: string) =
        match classifyRenameTarget relativePath with
        | RenamePathClassification.EntityFolderTarget(_, _, normalizedRelativePath) when isFile ->
            isGenericFileSystemFileTargetAllowed normalizedRelativePath
        | _ -> isRenamePathAllowed relativePath

    let isGenericFileSystemParentAllowed (relativePath: string) =
        let normalizedRelativePath = normalizeRelativePath relativePath

        if
            String.IsNullOrWhiteSpace normalizedRelativePath
            || PathHelpers.containsPathTraversalSegments normalizedRelativePath
            || PathHelpers.isProtectedDeleteTarget protectedDeleteTargetNames normalizedRelativePath
        then
            false
        else
            let segments = normalizedRelativePath |> splitPathSegments
            let structuralPath = classifyStructuralArcPath segments

            structuralPath <> StructuralArcPath.AddZoneRoot
            && (segments |> containsDisallowedGenericPathSegment |> not)
            && (PathHelpers.getFileName normalizedRelativePath |> isCanonicalArcFileName |> not)

    /// Checks whether a new generic file or folder may be created inside `relativePath`.
    /// Folders directly inside an add zone are ARC entities, but files may be created there.
    let isGenericFileSystemCreateParentAllowed (isFile: bool) (relativePath: string) =
        isGenericFileSystemParentAllowed relativePath
        || (isFile
            && (normalizeRelativePath relativePath
                |> splitPathSegments
                |> classifyStructuralArcPath) = StructuralArcPath.AddZoneRoot)

    let resolveRenameSourcePath (relativePath: string) =
        match classifyRenameTarget relativePath with
        | RenamePathClassification.CanonicalEntityFileTarget(zone, identifier, _)
        | RenamePathClassification.CanonicalDataMapFileTarget(zone, identifier, _) ->
            canonicalEntityFolderPath zone identifier
        | RenamePathClassification.EntityFolderTarget(_, _, normalizedRelativePath)
        | RenamePathClassification.GenericTarget normalizedRelativePath
        | RenamePathClassification.AddZoneRootTarget(_, normalizedRelativePath)
        | RenamePathClassification.InvestigationFileTarget normalizedRelativePath
        | RenamePathClassification.DisallowedTarget normalizedRelativePath
        | RenamePathClassification.ProtectedTarget normalizedRelativePath -> normalizedRelativePath
        | RenamePathClassification.RootTarget -> ""

    let tryGetRenameEntityFolderTarget (relativePath: string) =
        match classifyRenameTarget relativePath with
        | RenamePathClassification.EntityFolderTarget(zone, identifier, _)
        | RenamePathClassification.CanonicalEntityFileTarget(zone, identifier, _)
        | RenamePathClassification.CanonicalDataMapFileTarget(zone, identifier, _) -> Some(zone, identifier)
        | _ -> None

    let buildCanonicalEntityPaths zone identifier = [
        canonicalEntityFilePath zone identifier
        canonicalDataMapFilePath zone identifier
    ]

/// A comparison key for a path. Display paths stay as scanned, and keys decide which paths name the same entry.
[<Erase>]
type PathKey = PathKey of string

module PathKey =

    /// True for a path that needs no separator change, no trailing slash trim and no Unicode composition.
    let private isPlainAscii (path: string) =
        let mutable plain = path.Length = 0 || path.[path.Length - 1] <> '/'
        let mutable index = 0

        while plain && index < path.Length do
            let code = int path.[index]

            if code = 92 || code > 127 then
                plain <- false

            index <- index + 1

        plain

    /// Separators and trailing slash, then Unicode NFC, then lower case when the file system ignores case.
    let ofPath (caseInsensitive: bool) (path: string) : PathKey =
        // Whitespace is part of a file name, so the key keeps it. normalizePath would trim it.
        // Most paths are plain ASCII with forward slashes, which the normalization steps would leave as they are.
        let composed =
            if isPlainAscii path then
                path
            else
                (PathHelpers.normalizeSeparators path).TrimEnd('/')
                |> PathHelpers.normalizeToNfc

        PathKey(
            if caseInsensitive then
                composed.ToLowerInvariant()
            else
                composed
        )

    let value (PathKey key) : string = key
