module Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization

open Swate.Components.Shared
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes

type MaterializedState = {
    ArcScopeId: string option
    Paths: Set<string>
}

let empty = { ArcScopeId = None; Paths = Set.empty }

let materialize path state = {
    state with
        Paths = state.Paths.Add(PathHelpers.normalizePath path)
}

let rec private collectDirectoryPaths (node: FileTreeNode) (directoryPaths: Set<string>) =
    if node.isDirectory then
        node.children.Values
        |> Seq.fold
            (fun state child -> collectDirectoryPaths child state)
            (Set.add (PathHelpers.normalizePath node.path) directoryPaths)
    else
        directoryPaths

let reconcileMaterializedState
    (arcScopeId: string option)
    (selectedTreeItemPath: string option)
    (root: FileTreeNode option)
    (current: MaterializedState)
    =
    match root with
    | None -> {
        ArcScopeId = arcScopeId
        Paths = Set.empty
      }
    | Some root ->
        let validDirectoryPaths = collectDirectoryPaths root Set.empty

        let requiredPaths =
            selectedTreeItemPath
            |> Option.map (fun selectedPath ->
                validDirectoryPaths
                |> Set.filter (fun directoryPath -> PathHelpers.isSameOrDescendantPath selectedPath directoryPath)
            )
            |> Option.defaultValue Set.empty
            |> fun paths ->
                if root.isDirectory then
                    paths.Add(PathHelpers.normalizePath root.path)
                else
                    paths

        let persistedPaths =
            if current.ArcScopeId = arcScopeId then
                Set.intersect current.Paths validDirectoryPaths
            else
                Set.empty

        {
            ArcScopeId = arcScopeId
            Paths = Set.union persistedPaths requiredPaths
        }

let private rootItemSortKey (node: FileTreeNode) =
    match node.name.ToLowerInvariant() with
    | "notes" -> 0, 0, ""
    | "readme.md" -> 0, 1, ""
    | "isa.investigation.xlsx" -> 0, 2, ""
    | "studies" -> 0, 3, ""
    | "assays" -> 0, 4, ""
    | "workflows" -> 0, 5, ""
    | "runs" -> 0, 6, ""
    | _ -> 1, System.Int32.MaxValue, node.name.ToLowerInvariant()

/// isLoaded tells whether the tree has loaded the children of a directory path. An unloaded directory gets no
/// children, so the explorer shows it as expandable. A loaded directory without children is known to be empty.
let rec toMaterializedFileItemTree
    (createItem: FileTreeNode -> FileItem)
    (isLoaded: string -> bool)
    (materializedDirectoryPaths: Set<string>)
    (parent: FileTreeNode)
    (isRoot: bool)
    =
    if parent.isDirectory then
        let normalizedParentPath = PathHelpers.normalizePath parent.path

        let isDirectoryMaterialized =
            materializedDirectoryPaths.Contains normalizedParentPath

        let children =
            if not (isLoaded normalizedParentPath) then
                None
            elif isDirectoryMaterialized then
                let childNodes =
                    if isRoot then
                        parent.children.Values |> Seq.sortBy rootItemSortKey
                    else
                        parent.children.Values :> seq<FileTreeNode>

                childNodes
                |> Seq.map (fun parent ->
                    toMaterializedFileItemTree createItem isLoaded materializedDirectoryPaths parent false
                )
                |> List.ofSeq
                |> Some

            elif parent.children.Count = 0 then
                Some []
            else
                None

        {
            createItem parent with
                Children = children
        }
    else
        createItem parent
