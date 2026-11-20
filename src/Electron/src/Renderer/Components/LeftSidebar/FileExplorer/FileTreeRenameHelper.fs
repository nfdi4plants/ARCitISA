module Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameHelper

open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Renderer.Components.LeftSidebar.FileExplorer.Types
open Renderer.Components.LeftSidebar.FileExplorer.Helper
open Swate.Electron.Shared.RenamePathRules

let validateRenameDraftName (draft: ArcRenameDraft) (newName: string) =
    validateItemRenameName (not draft.Item.IsDirectory) draft.SourcePath newName

let tryBuildRenameDraft (item: FileItem) : Result<ArcRenameDraft, string> =
    match item.Path |> Option.map PathHelpers.normalizeCanonicalRelativePath with
    | None -> Error "Could not resolve the selected item path for rename."
    | Some sourcePath ->
        if canRenameFileItem item |> not then
            Error "Renaming this item is not allowed."
        else
            Ok {
                Item = item
                SourcePath = sourcePath
                InitialName = PathHelpers.getNameFromPath sourcePath
            }
