module Renderer.Components.LeftSidebar.FileExplorer.Helper

open System
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open ARCtrl
open Types

let private normalizeNodePath (path: string) = PathHelpers.normalizePath path

let private pathSegments (path: string) =
    path |> normalizeNodePath |> getNonEmptyPathParts

let private lowerInvariant (value: string) = value.ToLowerInvariant()

let private iconForArcCollectionFolder =
    function
    | "studies" -> Some FileItemIcon.Study
    | "assays" -> Some FileItemIcon.Assay
    | "workflows" -> Some FileItemIcon.Workflow
    | "runs" -> Some FileItemIcon.Run
    | "notes" -> Some FileItemIcon.Notebook
    | _ -> None

let private colorClassForArcCollectionFolder =
    function
    | "studies" -> Some "swt:text-amber-500"
    | "assays" -> Some "swt:text-lime-500"
    | "workflows" -> Some "swt:text-emerald-500"
    | "runs" -> Some "swt:text-cyan-500"
    | _ -> None

let private iconForArcWorkbookFile =
    function
    | "isa.investigation.xlsx" -> Some FileItemIcon.BookOpen
    | "isa.study.xlsx" -> Some FileItemIcon.Study
    | "isa.assay.xlsx" -> Some FileItemIcon.Assay
    | "isa.workflow.xlsx" -> Some FileItemIcon.Workflow
    | "isa.run.xlsx" -> Some FileItemIcon.Run
    | _ -> None

let private colorClassForArcWorkbookFile =
    function
    | "isa.investigation.xlsx" -> Some "swt:text-violet-400"
    | "isa.study.xlsx" -> Some "swt:text-amber-300"
    | "isa.assay.xlsx" -> Some "swt:text-lime-300"
    | "isa.workflow.xlsx" -> Some "swt:text-emerald-300"
    | "isa.run.xlsx" -> Some "swt:text-cyan-300"
    | _ -> None

let private colorClassForDatamapPath (path: string) =
    match DatamapParentInfo.tryFromPath path with
    | Some dmpi ->
        match dmpi.Parent with
        | DataMapParent.Study -> Some "swt:text-amber-700"
        | DataMapParent.Assay -> Some "swt:text-lime-700"
        | DataMapParent.Workflow -> Some "swt:text-emerald-700"
        | DataMapParent.Run -> Some "swt:text-cyan-700"
    | None -> None

let private folderIcon (path: string) =
    let segments = pathSegments path

    match segments |> Array.tryHead |> Option.map lowerInvariant, segments.Length with
    | Some rootSegment, 1 ->
        iconForArcCollectionFolder rootSegment
        |> Option.defaultValue FileItemIcon.Folder
    | Some "studies", 2 -> FileItemIcon.Study
    | Some "assays", 2 -> FileItemIcon.Assay
    | Some "workflows", 2 -> FileItemIcon.Workflow
    | Some "runs", 2 -> FileItemIcon.Run
    | Some "notes", _ -> FileItemIcon.Notebook
    | _ -> FileItemIcon.Folder

let private fileIcon (path: string) =
    let normalizedPath = normalizeNodePath path
    let segments = pathSegments normalizedPath
    let fileName = PathHelpers.getFileName normalizedPath |> lowerInvariant

    match colorClassForDatamapPath normalizedPath with
    | Some _ -> FileItemIcon.Map
    | None ->
        match iconForArcWorkbookFile fileName with
        | Some icon -> icon
        | None when
            (segments
             |> Array.tryHead
             |> Option.exists (fun segment -> String.Equals(segment, "notes", StringComparison.OrdinalIgnoreCase)))
            && fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ->
            FileItemIcon.Note
        | None when fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) -> FileItemIcon.Table
        | None -> FileItemIcon.Document

let getItemIconClass (item: FileItem) =
    match item.Path with
    | None -> None
    | Some path when item.IsDirectory ->
        let segments = pathSegments path

        match segments |> Array.tryHead |> Option.map lowerInvariant, segments.Length with
        | Some rootSegment, 1 -> colorClassForArcCollectionFolder rootSegment
        | Some "studies", 2 -> Some "swt:text-amber-500"
        | Some "assays", 2 -> Some "swt:text-lime-500"
        | Some "workflows", 2 -> Some "swt:text-emerald-500"
        | Some "runs", 2 -> Some "swt:text-cyan-500"
        | _ -> None
    | Some path ->
        let normalizedPath = normalizeNodePath path
        let fileName = PathHelpers.getFileName normalizedPath |> lowerInvariant

        match colorClassForDatamapPath normalizedPath with
        | Some colorClass -> Some colorClass
        | None -> colorClassForArcWorkbookFile fileName

let rootFolderContextMenuItems
    (rootFolderName: string)
    (label: string)
    (icon: string)
    (onClick: unit -> unit)
    (item: FileItem)
    =
    let isMatchingRootFolder (item: FileItem) =
        item.IsDirectory
        && (item.Path
            |> Option.map PathHelpers.normalizeCanonicalRelativePath
            |> Option.exists (isRootFolderPath rootFolderName))

    ContextMenuItem.whenItem isMatchingRootFolder label icon (fun _ -> onClick ()) item

let fileSystemCreateKinds = [ FileSystemItemKind.File; FileSystemItemKind.Folder ]

let fileSystemCreateKindLabel =
    function
    | FileSystemItemKind.File -> "File"
    | FileSystemItemKind.Folder -> "Folder"

let fileSystemCreateKindIcon =
    function
    | FileSystemItemKind.File -> "swt:fluent--document-add-24-regular"
    | FileSystemItemKind.Folder -> "swt:fluent--folder-add-24-regular"

let canCreateFileSystemItemIn (kind: FileSystemItemKind) (item: FileItem) =
    item.IsDirectory
    && (item.Path
        |> Option.map PathHelpers.normalizeCanonicalRelativePath
        |> Option.exists (fun path ->
            String.IsNullOrWhiteSpace path
            || ArcEntityPathRules.isGenericFileSystemCreateParentAllowed (kind = FileSystemItemKind.File) path
        ))

let canDeleteFileItem (item: FileItem) =
    item.Path
    |> Option.map PathHelpers.normalizeCanonicalRelativePath
    |> Option.exists (ArcEntityPathRules.isItemDeletePathAllowed (not item.IsDirectory))

let canRenameFileItem (item: FileItem) =
    item.Path
    |> Option.map PathHelpers.normalizeCanonicalRelativePath
    |> Option.exists (ArcEntityPathRules.isItemRenamePathAllowed (not item.IsDirectory))

let tryGetArcEntityWorkbookName (folderPath: string) =
    ArcEntityPathRules.tryGetRenameEntityFolderTarget (PathHelpers.normalizePath folderPath)
    |> Option.bind (fun (zone, identifier) ->
        ArcEntityPathRules.buildCanonicalEntityPaths zone identifier
        |> List.tryHead
        |> Option.map PathHelpers.getFileName
    )

let isArcEntityFolderViewOpen
    (pageState: Renderer.Types.PageState option)
    (selectedTreePath: string option)
    (folderPath: string)
    =
    match pageState, selectedTreePath, tryGetArcEntityWorkbookName folderPath with
    | Some(Renderer.Types.PageState.ArcFilePage _), Some selectedPath, Some workbookName ->
        PathHelpers.pathsEqual selectedPath folderPath
        || PathHelpers.pathsEqual selectedPath $"{PathHelpers.normalizePath folderPath}/{workbookName}"
    | _ -> false

let createItem (node: FileTreeNode) : FileItem =
    let item =
        if node.isDirectory then
            FileTree.createFolder node.name (Some node.path) (folderIcon node.path)
        else
            FileTree.createFile node.name (Some node.path) (fileIcon node.path)

    { item with Id = node.path }
    |> Renderer.Components.FileExplorerLfs.withFileTreeNodeLfsState node

let arcCreateKinds = [
    {
        Kind = ArcFilesDiscriminate.Study
        Label = "Study"
        FolderName = "studies"
        Icon = "swt:fluent--document-table-24-regular"
    }
    {
        Kind = ArcFilesDiscriminate.Assay
        Label = "Assay"
        FolderName = "assays"
        Icon = "swt:fluent--beaker-24-regular"
    }
    {
        Kind = ArcFilesDiscriminate.Workflow
        Label = "Workflow"
        FolderName = "workflows"
        Icon = "swt:fluent--flowchart-24-regular"
    }
    {
        Kind = ArcFilesDiscriminate.Run
        Label = "Run"
        FolderName = "runs"
        Icon = "swt:fluent--play-24-regular"
    }
]

let isArcCreateIdentifierValid (identifier: string) =
    let identifier = identifier.Trim()

    (System.String.IsNullOrWhiteSpace identifier |> not)
    && ARCtrl.Helper.Identifier.tryCheckValidCharacters identifier

let arcCreateIdentifierError =
    "Identifier is required and may only contain letters, digits, spaces, underscores, or dashes."

let tryGetInlineArcCreateKind (rootPath: string) (item: FileItem) =
    if not item.IsDirectory then
        None
    else
        match item.Path with
        | Some path when getPathDepth path = getPathDepth rootPath + 1 ->
            let folderName = PathHelpers.getNameFromPath path

            arcCreateKinds
            |> List.tryFind (fun config ->
                config.FolderName.Equals(folderName, System.StringComparison.OrdinalIgnoreCase)
            )
            |> Option.map _.Kind
        | _ -> None

let tryBuildArcCreateDraft kind (identifier: string) (existingPaths: string seq) =
    let identifier = identifier.Trim()

    if isArcCreateIdentifierValid identifier |> not then
        Error arcCreateIdentifierError
    else
        match arcCreateKinds |> List.tryFind (fun config -> config.Kind = kind) with
        | None -> Error $"Creating {kind} files is not supported from the file explorer."
        | Some config ->
            let arcFile = ArcFileDefaults.createDefaultArcFile kind identifier

            match FileContentDTO.fromArcFile arcFile with
            | None -> Error $"Creating {config.Label} files is not supported in Electron yet."
            | Some request ->
                let requestedPath = PathHelpers.normalizePath request.path

                let alreadyExists =
                    existingPaths
                    |> Seq.exists (fun path -> PathHelpers.pathsEqual (PathHelpers.normalizePath path) requestedPath)

                if alreadyExists then
                    Error $"{config.Label} '{identifier}' already exists."
                else
                    Ok {
                        ArcFile = arcFile
                        Path = requestedPath
                    }
