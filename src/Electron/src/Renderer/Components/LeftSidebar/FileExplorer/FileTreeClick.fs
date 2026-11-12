namespace Renderer.Components.LeftSidebar.FileExplorer

open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared

/// Decides what a click on a file tree item opens.
[<RequireQualifiedAccess>]
module FileTreeClick =

    type FileTreeClickTarget =
        | MissingPath
        | Folder
        | File of normalizedPath: string

    let targetOfClick (item: FileItem) : FileTreeClickTarget =
        match item.Path with
        | None -> MissingPath
        | Some _ when item.IsDirectory -> Folder
        | Some path -> File(PathHelpers.normalizePath path)
