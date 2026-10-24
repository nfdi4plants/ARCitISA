module Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization

open System
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Renderer.Context.FileTreeStore

let private rootItemSortKey (entry: FileEntry) =
    match entry.name.ToLowerInvariant() with
    | "notes" -> 0, 0, ""
    | "readme.md" -> 0, 1, ""
    | "isa.investigation.xlsx" -> 0, 2, ""
    | "studies" -> 0, 3, ""
    | "assays" -> 0, 4, ""
    | "workflows" -> 0, 5, ""
    | "runs" -> 0, 6, ""
    | _ -> 1, Int32.MaxValue, entry.name.ToLowerInvariant()

/// Follows a chain of single same-named subdirectories (a/a/a) down to its deepest directory. A level only joins
/// the chain when both it and the child are loaded, because an unloaded directory may hold more than one child.
let rec private deepestOfSameNameChain (snapshot: FileTreeSnapshot) (entry: FileEntry) : FileEntry =
    if
        entry.isDirectory
        && isLoaded snapshot entry.path
        && childCount snapshot entry.path = 1
    then
        match children snapshot entry.path with
        | [| only |] when
            only.isDirectory
            && isLoaded snapshot only.path
            && String.Equals(entry.name, only.name, StringComparison.OrdinalIgnoreCase)
            ->
            deepestOfSameNameChain snapshot only
        | _ -> entry
    else
        entry

let rec private buildChildren
    (createItem: FileEntry -> FileItem)
    (withBusy: string -> FileEntry[] -> FileEntry[])
    (snapshot: FileTreeSnapshot)
    (expandedIds: Set<string>)
    (isRoot: bool)
    (directoryPath: string)
    : FileItem list =
    let entries = children snapshot directoryPath |> withBusy directoryPath

    let ordered =
        if isRoot then
            entries |> Seq.sortBy rootItemSortKey |> Seq.toArray
        else
            entries

    ordered
    |> Array.map (buildItem createItem withBusy snapshot expandedIds)
    |> List.ofArray

and private buildItem
    (createItem: FileEntry -> FileItem)
    (withBusy: string -> FileEntry[] -> FileEntry[])
    (snapshot: FileTreeSnapshot)
    (expandedIds: Set<string>)
    (entry: FileEntry)
    : FileItem =
    if not entry.isDirectory then
        createItem entry
    else
        // The collapsed row keeps its own label and acts on the deepest directory.
        let deepest = deepestOfSameNameChain snapshot entry
        let item = createItem { deepest with name = entry.name }
        let path = deepest.path

        let itemChildren =
            if not (isLoaded snapshot path) then
                None
            elif expandedIds.Contains path then
                Some(buildChildren createItem withBusy snapshot expandedIds false path)
            elif childCount snapshot path = 0 then
                Some []
            else
                None

        { item with Children = itemChildren }

/// Builds the items of the root's children. Only expanded directories are walked, so the cost follows the visible
/// rows. An unloaded directory gets no children (it can expand), a loaded empty one gets an empty list.
/// withBusy adds entries of running large-file actions that the listing misses.
let buildVisibleItems
    (createItem: FileEntry -> FileItem)
    (withBusy: string -> FileEntry[] -> FileEntry[])
    (snapshot: FileTreeSnapshot)
    (expandedIds: Set<string>)
    : FileItem list =
    buildChildren createItem withBusy snapshot expandedIds true ""
