module Swate.Electron.Shared.FileIOHelper

open System
open System.Collections.Generic
open ARCtrl
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes

let getNonEmptyPathParts (path: string) =
    PathHelpers.normalizePath path
    |> fun p -> p.Split('/', StringSplitOptions.RemoveEmptyEntries)

let getPathDepth (path: string) =
    path |> getNonEmptyPathParts |> Array.length

let pathsEqual (left: string) (right: string) =
    PathHelpers.normalizePath left = PathHelpers.normalizePath right

let isLegacyDataMapPath (path: string) =
    getNonEmptyPathParts path
    |> Array.tryLast
    |> Option.exists (fun fileName -> String.Equals(fileName, "isa_datamap", StringComparison.OrdinalIgnoreCase))

let isRootFolderPath (rootFolderName: string) (candidateRelativePath: string) =
    match getNonEmptyPathParts candidateRelativePath with
    | [| candidateRoot |] -> pathsEqual rootFolderName candidateRoot
    | _ -> false

let isRootFolderForTargetPath
    (createTargetPath: 'Target -> string option)
    (target: 'Target)
    (candidateRelativePath: string)
    =
    match
        createTargetPath target |> Option.bind (getNonEmptyPathParts >> Array.tryHead),
        getNonEmptyPathParts candidateRelativePath
    with
    | Some targetRoot, [| candidateRoot |] -> pathsEqual targetRoot candidateRoot
    | _ -> false

let private tryGetRepoRelativePathCore (repoRoot: string) (absolutePath: string) (allowRoot: bool) =
    let normalizedRoot = PathHelpers.normalizePath repoRoot
    let normalizedAbsolutePath = PathHelpers.normalizePath absolutePath

    if
        String.IsNullOrWhiteSpace normalizedRoot
        || String.IsNullOrWhiteSpace normalizedAbsolutePath
    then
        None
    elif pathsEqual normalizedAbsolutePath normalizedRoot then
        if allowRoot then Some "" else None
    elif PathHelpers.isSameOrDescendantPath normalizedAbsolutePath normalizedRoot then
        let prefix = normalizedRoot + "/"

        if normalizedAbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
            let relativePath = normalizedAbsolutePath.Substring(prefix.Length)

            if
                String.IsNullOrWhiteSpace relativePath
                || PathHelpers.containsPathTraversalSegments relativePath
            then
                None
            else
                Some relativePath
        else
            None
    else
        None

/// Tries to convert an absolute repository path to a repository-relative path.
/// Returns None for the repository root and unsafe traversal-like paths.
let tryGetRepoRelativePath (repoRoot: string) (absolutePath: string) =
    tryGetRepoRelativePathCore repoRoot absolutePath false

/// Tries to convert an absolute repository path to a repository-relative path.
/// Returns Some "" for the repository root and None for unsafe traversal-like paths.
let tryGetRepoRelativePathOrRoot (repoRoot: string) (absolutePath: string) =
    tryGetRepoRelativePathCore repoRoot absolutePath true

let tryGetPathSegmentAfterFolder (folderName: string) (path: string) =
    let segments = getNonEmptyPathParts path

    match
        segments
        |> Array.tryFindIndex (fun segment -> String.Equals(segment, folderName, StringComparison.OrdinalIgnoreCase))
    with
    | Some index when index + 1 < segments.Length ->
        let name = segments.[index + 1].Trim()

        if String.IsNullOrWhiteSpace name then None else Some name
    | _ -> None

let resolveArcPreviewPath (path: string) =
    Swate.Components.Shared.PathHelpers.resolveArcViewPath path

let private insertFileTreeEntry (root: FileTreeNode) (rootPath: string) (entry: FileEntry) =
    let parts = getNonEmptyPathParts entry.path
    let rootParts = getNonEmptyPathParts rootPath

    if parts.Length > rootParts.Length then
        let rec loop (node: FileTreeNode) index =
            let part = parts[index]
            let isLast = index = parts.Length - 1

            let child =
                match node.children.TryGetValue(part) with
                | true, existing when ((not isLast) || entry.isDirectory) && not existing.isDirectory ->
                    // A node may first appear via a file path segment; upgrade it to a directory when needed.
                    let upgraded = { existing with isDirectory = true }
                    node.children.[part] <- upgraded
                    upgraded
                | true, existing -> existing
                | false, _ ->
                    let newPath = parts.[0..index] |> String.concat "/"

                    let newNode =
                        FileTreeNode.create (
                            part,
                            (if isLast then entry.isDirectory else true),
                            newPath,
                            Dictionary(),
                            entry.largeObject
                        )

                    node.children.Add(part, newNode)
                    newNode

            if isLast then ignore child else loop child (index + 1)

        loop root rootParts.Length

let toFileTreeNode (fileEntries: FileEntry[]) =

    if fileEntries.Length = 0 then
        failwith "toFileTreeNode requires at least one file entry to determine the root path."

    let normalizedPaths =
        fileEntries
        |> Array.map (fun fileEntry -> PathHelpers.normalizePath fileEntry.path)

    let rootPath =
        normalizedPaths
        |> Array.distinct
        |> Array.sortBy (fun path -> getPathDepth path, path)
        |> Array.head

    let adaptedFileEntries =
        fileEntries
        |> Array.filter (fun fileEntry -> PathHelpers.normalizePath fileEntry.path <> rootPath)
        // Deterministic order avoids creating parents from file entries before their directory entries.
        |> Array.sortBy (fun fileEntry ->
            let depth = getPathDepth fileEntry.path
            depth, (if fileEntry.isDirectory then 0 else 1), PathHelpers.normalizePath fileEntry.path
        )

    let rootElement =
        let rootEntry =
            fileEntries
            |> Array.find (fun fileEntry -> PathHelpers.normalizePath fileEntry.path = rootPath)

        FileTreeNode.create (rootEntry.name, rootEntry.isDirectory, rootPath, Dictionary(), rootEntry.largeObject)

    adaptedFileEntries
    |> Array.iter (fun fileEntry -> insertFileTreeEntry rootElement rootPath fileEntry)

    rootElement

let rec collapseSingleChildSameName (node: FileTreeNode) : FileTreeNode =
    let collapsedChildren =
        node.children.Values |> Seq.map collapseSingleChildSameName |> Seq.toList

    let childrenByName = Dictionary<string, FileTreeNode>()

    collapsedChildren
    |> List.iter (fun (child: FileTreeNode) -> childrenByName.[child.name] <- child)

    let nodeWithCollapsedChildren = { node with children = childrenByName }

    if
        nodeWithCollapsedChildren.isDirectory
        && nodeWithCollapsedChildren.children.Count = 1
    then
        let onlyChild = nodeWithCollapsedChildren.children.Values |> Seq.exactlyOne

        if
            onlyChild.isDirectory
            && String.Equals(nodeWithCollapsedChildren.name, onlyChild.name, StringComparison.OrdinalIgnoreCase)
        then
            // Preserve the displayed label while routing interactions to the deepest merged directory path.
            {
                onlyChild with
                    name = nodeWithCollapsedChildren.name
            }
        else
            nodeWithCollapsedChildren
    else
        nodeWithCollapsedChildren

let private tryGetExistingArcEntityTargetMatch
    (targetSources: seq<string * string * (string -> 'Target)>)
    (path: string)
    =
    match getNonEmptyPathParts path with
    | [| folderName; name; fileName |] when PathHelpers.isSafePathSegment name ->
        let targetName = name.Trim()

        targetSources
        |> Seq.indexed
        |> Seq.tryPick (fun (sortOrder, (targetFolderName, targetFileName, createTarget)) ->
            if
                PathHelpers.pathsEqual folderName targetFolderName
                && PathHelpers.pathsEqual fileName targetFileName
            then
                Some(createTarget targetName, targetName, sortOrder)
            else
                None
        )
    | _ -> None

let tryGetExistingArcEntityTargetRef (targetSources: seq<string * string * (string -> 'Target)>) (path: string) =
    tryGetExistingArcEntityTargetMatch targetSources path
    |> Option.map (fun (target, _, _) -> target)

let createAvailableArcEntityTargets
    (targetSources: seq<string * string * (string -> 'Target)>)
    (fileEntries: seq<FileEntry>)
    =
    fileEntries
    |> Seq.choose (fun entry ->
        if entry.isDirectory then
            None
        else
            tryGetExistingArcEntityTargetMatch targetSources entry.path
    )
    |> Seq.distinctBy (fun (target, _, _) -> target)
    |> Seq.sortBy (fun (_, name, sortOrder) -> sortOrder, name.ToLowerInvariant())
    |> Seq.map (fun (target, _, _) -> target)
    |> ResizeArray

let combineMany = ARCtrl.ArcPathHelper.combineMany

let tryGetArcFilePath (arcRootPath: ArcRootPath) (arcFile: ArcFiles) =
    let arcRootPath = defaultArg arcRootPath ""
    let root = PathHelpers.normalizePath arcRootPath

    arcFile.TryGetRelativePath() |> Option.map (fun p -> combineMany [| root; p |])


[<RequireQualifiedAccess>]
module FileContentType =

    open ARCtrl.Contract

    /// This function checks if the given file content type is one of the plain text variants.
    let isPlainTextVariant (fileType: FileContentType) =
        match fileType with
        | FileContentType.JSON
        | FileContentType.YAML
        | FileContentType.CWL
        | FileContentType.PlainText
        | FileContentType.Markdown -> true
        | _ -> false

    /// This function checks if the given file content type is one of the ISA file variants.
    let isISAFileVariant (fileType: FileContentType) =
        match fileType with
        | FileContentType.ISA_Investigation
        | FileContentType.ISA_Study
        | FileContentType.ISA_Assay
        | FileContentType.ISA_Run
        | FileContentType.ISA_Workflow
        | FileContentType.ISA_Datamap -> true
        | _ -> false

    /// Maps custom file content types to ARCtrl DTOType when a direct ARC contract counterpart exists.
    let tryToArcContractDTOType (fileType: FileContentType) : DTOType option =
        match fileType with
        | FileContentType.JSON -> Some DTOType.JSON
        | FileContentType.YAML -> Some DTOType.YAML
        | FileContentType.CWL -> Some DTOType.CWL
        | FileContentType.PlainText -> Some DTOType.PlainText
        | FileContentType.ISA_Investigation -> Some DTOType.ISA_Investigation
        | FileContentType.ISA_Study -> Some DTOType.ISA_Study
        | FileContentType.ISA_Assay -> Some DTOType.ISA_Assay
        | FileContentType.ISA_Run -> Some DTOType.ISA_Run
        | FileContentType.ISA_Workflow -> Some DTOType.ISA_Workflow
        | FileContentType.ISA_Datamap -> Some DTOType.ISA_Datamap
        | FileContentType.CLI -> Some DTOType.CLI
        | FileContentType.Markdown -> None
        | FileContentType.PDF -> None
        | FileContentType.NoPreview -> None
        | FileContentType.PNGImage -> None
        | FileContentType.JPGImage -> None
        | FileContentType.SVGImage -> None

    /// Maps ARCtrl DTOType values to custom file content types.
    let ofArcContractDTOType (dtoType: DTOType) : FileContentType =
        match dtoType with
        | DTOType.JSON -> FileContentType.JSON
        | DTOType.YAML -> FileContentType.YAML
        | DTOType.CWL -> FileContentType.CWL
        | DTOType.PlainText -> FileContentType.PlainText
        | DTOType.ISA_Investigation -> FileContentType.ISA_Investigation
        | DTOType.ISA_Study -> FileContentType.ISA_Study
        | DTOType.ISA_Assay -> FileContentType.ISA_Assay
        | DTOType.ISA_Run -> FileContentType.ISA_Run
        | DTOType.ISA_Workflow -> FileContentType.ISA_Workflow
        | DTOType.ISA_Datamap -> FileContentType.ISA_Datamap
        | DTOType.CLI -> FileContentType.CLI
        | _ -> FileContentType.PlainText

    /// Active pattern for matching all plain text variants.
    let (|FileContentTypeIsPlainTextVariant|_|) (fileType: FileContentType) =
        if isPlainTextVariant fileType then Some() else None

    /// Active pattern for matching all ISA file variants.
    let (|FileContentTypeIsISAFileVariant|_|) (fileType: FileContentType) =
        if isISAFileVariant fileType then Some() else None

[<RequireQualifiedAccess>]
module FileContentDTO =

    open ARCtrl.Helper
    open ARCtrl.Contract
    open ARCtrl.ArcPathHelper
    open ARC

    let DEFAULT_JSON_EXPORT_FORMAT = JsonExportFormat.ARCtrl

    let create fileType content path : FileContentDTO = {|
        fileType = fileType
        content = content
        path = path
        noPreview = None
    |}

    /// Describes a file that is shown without content, with its size in bytes and the reason.
    let createNoPreview (path: string) (sizeBytes: float) (reason: NoPreviewReason) : FileContentDTO = {|
        fileType = FileContentType.NoPreview
        content = ""
        path = path
        noPreview =
            Some {|
                sizeBytes = sizeBytes
                reason = reason
            |}
    |}

    let inferTextFileTypeFromPath (path: string) : FileContentType =
        let normalizedPath = PathHelpers.normalizePath path

        match normalizedPath with
        | _ when normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) -> FileContentType.Markdown
        | _ when normalizedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) -> FileContentType.PDF
        | _ when normalizedPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) -> FileContentType.SVGImage
        | _ when normalizedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) -> FileContentType.PNGImage
        | _ when normalizedPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) -> FileContentType.JPGImage
        | _ when normalizedPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) -> FileContentType.JPGImage
        | _ -> FileContentType.PlainText

    let normalizeArcFileRequestPath (request: FileContentDTO) : FileContentDTO =
        let normalizedPath = PathHelpers.normalizePath request.path

        if normalizedPath = request.path then
            request
        else
            {| request with path = normalizedPath |}

    let toArcFile (dto: FileContentDTO) : ArcFiles option =

        let exportFormat = JsonExportFormat.ARCtrl

        let afd =
            match FileContentType.tryToArcContractDTOType dto.fileType with
            | Some DTOType.ISA_Investigation -> Some ArcFilesDiscriminate.Investigation
            | Some DTOType.ISA_Study -> Some ArcFilesDiscriminate.Study
            | Some DTOType.ISA_Assay -> Some ArcFilesDiscriminate.Assay
            | Some DTOType.ISA_Run -> Some ArcFilesDiscriminate.Run
            | Some DTOType.ISA_Workflow -> Some ArcFilesDiscriminate.Workflow
            | Some DTOType.ISA_Datamap -> Some ArcFilesDiscriminate.DataMap
            | _ -> None

        match afd with
        | Some afd ->
            match Json.Generic.readFromJsonMap.TryGetValue((afd, exportFormat)) with
            | true, fn ->
                match fn dto.content with
                | ArcFiles.DataMap(None, dm) ->
                    let dmpi = DatamapParentInfo.tryFromPath dto.path
                    ArcFiles.DataMap(dmpi, dm)
                | anyElse -> anyElse
                |> Some
            | _ -> None
        | None -> None

    let fromArcFile (arcFile: ArcFiles) : FileContentDTO option =
        let exportFormat = JsonExportFormat.ARCtrl

        let dtoTypeOpt =
            match arcFile with
            | ArcFiles.Investigation _ ->
                Some {|
                    fileType = FileContentType.ISA_Investigation
                    path = ARCtrl.ArcPathHelper.InvestigationFileName
                |}
            | ArcFiles.Study(s, _) ->
                Some {|
                    fileType = FileContentType.ISA_Study
                    path = ARCtrl.Helper.Identifier.Study.fileNameFromIdentifier s.Identifier
                |}
            | ArcFiles.Assay(a) ->
                Some {|
                    fileType = FileContentType.ISA_Assay
                    path = ARCtrl.Helper.Identifier.Assay.fileNameFromIdentifier a.Identifier
                |}
            | ArcFiles.Run r ->
                Some {|
                    fileType = FileContentType.ISA_Run
                    path = ARCtrl.Helper.Identifier.Run.fileNameFromIdentifier r.Identifier
                |}
            | ArcFiles.Workflow w ->
                Some {|
                    fileType = FileContentType.ISA_Workflow
                    path = ARCtrl.Helper.Identifier.Workflow.fileNameFromIdentifier w.Identifier
                |}
            | ArcFiles.DataMap(Some dmpi, dm) ->
                Some {|
                    fileType = FileContentType.ISA_Datamap
                    path = DatamapParentInfo.toPath dmpi
                |}
            | _ -> None

        match dtoTypeOpt with
        | Some dtoType ->
            let _, json = Json.Export.parseToJsonString (arcFile, exportFormat)
            create dtoType.fileType json dtoType.path |> Some
        | None -> None

    let fromArcByPath (path: string) (arc: ARC) =
        let normalizedPath = PathHelpers.normalizePath path

        arc.TryArcFileByPath(normalizedPath)
        |> Option.bind fromArcFile
        |> Option.map (fun dto -> {| dto with path = normalizedPath |})
