module Renderer.Components.Helper.FileExplorerStateReconciliation

open Feliz
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open Renderer.Types
open Swate.Components.Page.ArcFileEditor.Types

let containsPath (paths: string seq) (relativePath: string) =
    let normalizedTargetPath = PathHelpers.normalizePath relativePath

    paths
    |> Seq.exists (fun path -> PathHelpers.pathsEqual (PathHelpers.normalizePath path) normalizedTargetPath)

let isSelectionMissing (paths: string seq) (selectionPath: string option) =
    selectionPath
    |> Option.map PathHelpers.normalizePath
    |> Option.exists (fun selectedPath -> containsPath paths selectedPath |> not)

let private resetsWhenSelectionIsRemoved =
    function
    | PageState.ArcFilePage _
    | PageState.MarkdownPage _
    | PageState.PDFPage _
    | PageState.PNGImage _
    | PageState.JPGImage _
    | PageState.SVGImage _
    | PageState.TextPage _
    | PageState.UnknownPage
    | PageState.LargeFilePage _
    | PageState.ErrorPage _ -> true
    | _ -> false

let shouldResetPageStateAfterSelectionRemoval (pageState: PageState option) =
    pageState |> Option.exists resetsWhenSelectionIsRemoved

let tryGetDataMapMismatchReload (fileTree: FileEntry[]) (pageState: PageState option) =
    match pageState with
    | Some(PageState.ArcFilePage(ArcFiles.DataMap _, _)) -> None
    | Some(PageState.ArcFilePage(arcFile, requestedView)) ->
        match arcFile.TryGetDataMapParentInfo() with
        | Some parentInfo ->
            let treeHasDataMap =
                containsPath (fileTree |> Array.map _.path) (DatamapParentInfo.toPath parentInfo)

            let pageHasDataMap = arcFile.TryGetDataMap().IsSome

            if treeHasDataMap = pageHasDataMap then
                None
            else
                arcFile.TryGetRelativePath()
                |> Option.map (fun parentPath ->
                    let nextRequestedView =
                        match treeHasDataMap, requestedView with
                        | false, Some ActiveView.DataMap -> Some ActiveView.Metadata
                        | _ -> requestedView

                    PathHelpers.normalizePath parentPath, nextRequestedView
                )
        | None -> None
    | _ -> None

let private reloadsWhenSelectedFileChanges =
    function
    | PageState.MarkdownPage _
    | PageState.PDFPage _
    | PageState.PNGImage _
    | PageState.JPGImage _
    | PageState.SVGImage _
    | PageState.TextPage _
    | PageState.UnknownPage
    | PageState.LargeFilePage _
    | PageState.ErrorPage _ -> true
    | _ -> false

let private isCheckedOutLfsFile (tryFindLfs: string -> ObjectStateDto option) (entry: FileEntry) =
    tryFindLfs entry.path |> Option.exists (fun state -> state.IsMaterialized)

let private isPointerLfsFile (tryFindLfs: string -> ObjectStateDto option) (entry: FileEntry) =
    tryFindLfs entry.path |> Option.exists (fun state -> not state.IsMaterialized)

let private shouldReloadSelectedFile tryFindLfs pageState entry =
    if isPointerLfsFile tryFindLfs entry then
        false
    else
        match pageState with
        | Some state -> reloadsWhenSelectedFileChanges state
        | None -> isCheckedOutLfsFile tryFindLfs entry

let private tryFindSelectedFileEntry (fileTree: FileEntry[]) (selectionPath: string option) =
    selectionPath
    |> Option.map PathHelpers.normalizePath
    |> Option.bind (fun selectedPath ->
        fileTree
        |> Array.tryFind (fun entry ->
            not entry.isDirectory
            && PathHelpers.pathsEqual (PathHelpers.normalizePath entry.path) selectedPath
        )
    )

let shouldClearPageStateForLfsPointerSelection
    (tryFindLfs: string -> ObjectStateDto option)
    (fileTree: FileEntry[])
    (selectionPath: string option)
    (pageState: PageState option)
    =
    pageState |> Option.exists resetsWhenSelectionIsRemoved
    && (tryFindSelectedFileEntry fileTree selectionPath
        |> Option.exists (isPointerLfsFile tryFindLfs))

let tryGetReloadableSelectedFilePath
    (tryFindLfs: string -> ObjectStateDto option)
    (fileTree: FileEntry[])
    (selectionPath: string option)
    (pageState: PageState option)
    =
    tryFindSelectedFileEntry fileTree selectionPath
    |> Option.bind (fun entry ->
        if shouldReloadSelectedFile tryFindLfs pageState entry then
            Some(PathHelpers.normalizePath entry.path)
        else
            None
    )

/// Reloads or clears the open preview after the file tree changed. The effect depends on the tree and on the large
/// object state of the selected file only, so a push for other files does not reload the preview.
[<Hook>]
let useReconcileSelectedPreview
    (tryFindLfs: string -> ObjectStateDto option)
    (fileTree: FileEntry[])
    (selectionPath: string option)
    (pageState: PageState option)
    (reloadPreview: string -> (PageState -> PageState) -> unit)
    (clearPageState: unit -> unit)
    =
    // The watcher emits the initial tree too, so the first run only records. Later runs compare with the previous one.
    let lastRunRef = React.useRef<(FileEntry[] * string option) option> None

    let selectedLfsKey =
        selectionPath
        |> Option.bind (PathHelpers.normalizePath >> tryFindLfs)
        |> Option.map (fun state -> $"{state.IsMaterialized}/{state.IsLocallyAvailable}")
        |> Option.defaultValue ""

    React.useEffect (
        (fun () ->
            let previous = lastRunRef.current
            lastRunRef.current <- Some(fileTree, selectionPath)

            // A changed key alone belongs to a selection change when the selection moved. The click opens that file itself.
            let shouldAct =
                match previous with
                | None -> false
                | Some(previousTree, previousSelection) ->
                    not (obj.ReferenceEquals(previousTree, fileTree))
                    || previousSelection = selectionPath

            if shouldAct then
                match tryGetDataMapMismatchReload fileTree pageState with
                | Some(parentPath, requestedView) ->
                    reloadPreview
                        parentPath
                        (function
                        | PageState.ArcFilePage(nextArcFile, _) -> PageState.ArcFilePage(nextArcFile, requestedView)
                        | other -> other
                        )
                | None when shouldClearPageStateForLfsPointerSelection tryFindLfs fileTree selectionPath pageState ->
                    clearPageState ()
                | None ->
                    match tryGetReloadableSelectedFilePath tryFindLfs fileTree selectionPath pageState with
                    | None -> ()
                    | Some selectedPath -> reloadPreview selectedPath id
        ),
        [| box fileTree; box selectedLfsKey |]
    )
