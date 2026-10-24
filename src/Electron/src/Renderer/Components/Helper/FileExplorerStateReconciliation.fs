module Renderer.Components.Helper.FileExplorerStateReconciliation

open Feliz
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open Renderer.Types
open Renderer.Context.FileStateContext
open Renderer.Context.FileTreeHooks
open Renderer.Context.FileTreeStore
open Swate.Components.Page.ArcFileEditor.Types

/// A selection is missing only when its parent directory is loaded and does not list it. A path below a directory
/// that is not loaded is unknown, and the selection stays.
let isKnownAbsent (known: KnownState) = known = Absent

let isSelectionMissing (snapshot: FileTreeSnapshot) (selectionPath: string option) =
    selectionPath
    |> Option.map PathHelpers.normalizePath
    |> Option.exists (fun selectedPath -> isKnownAbsent (knows snapshot selectedPath))

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

/// treeHasDataMap tells whether the tree lists the DataMap file of the entity that the open page belongs to.
let tryGetDataMapMismatchReload (treeHasDataMap: bool) (pageState: PageState option) =
    match pageState with
    | Some(PageState.ArcFilePage(ArcFiles.DataMap _, _)) -> None
    | Some(PageState.ArcFilePage(arcFile, requestedView)) ->
        match arcFile.TryGetDataMapParentInfo() with
        | Some parentInfo ->
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

let private asFileEntry (selected: FileEntry option) =
    selected |> Option.filter (fun entry -> not entry.isDirectory)

let shouldClearPageStateForLfsPointerSelection
    (tryFindLfs: string -> ObjectStateDto option)
    (selected: FileEntry option)
    (pageState: PageState option)
    =
    pageState |> Option.exists resetsWhenSelectionIsRemoved
    && (asFileEntry selected |> Option.exists (isPointerLfsFile tryFindLfs))

let tryGetReloadableSelectedFilePath
    (tryFindLfs: string -> ObjectStateDto option)
    (selected: FileEntry option)
    (pageState: PageState option)
    =
    asFileEntry selected
    |> Option.bind (fun entry ->
        if shouldReloadSelectedFile tryFindLfs pageState entry then
            Some(PathHelpers.normalizePath entry.path)
        else
            None
    )

type private PreviewRun = {
    Selection: string option
    Entry: FileEntry option
    DataMapPresent: bool
    LfsKey: string
    StampKey: string
}

/// Reloads or clears the open preview after something it shows changed. The effect depends on the selected entry,
/// the DataMap file of the open entity, the large object state of the selected file and its content stamp, so a
/// directory update for other paths does not reload the preview.
[<Hook>]
let useReconcileSelectedPreview
    (tryFindLfs: string -> ObjectStateDto option)
    (selectionPath: string option)
    (pageState: PageState option)
    (contentStamp: FileContentStamp option)
    (reloadPreview: string -> (PageState -> PageState) -> unit)
    (clearPageState: unit -> unit)
    =
    let normalizedSelection = selectionPath |> Option.map PathHelpers.normalizePath
    let selectedEntry = useFileTreeEntry normalizedSelection

    let dataMapPath =
        match pageState with
        | Some(PageState.ArcFilePage(arcFile, _)) ->
            arcFile.TryGetDataMapParentInfo()
            |> Option.map (DatamapParentInfo.toPath >> PathHelpers.normalizePath)
        | _ -> None

    let dataMapPresent =
        match useFileTreeKnows dataMapPath with
        | Present _ -> true
        | _ -> false

    let lastRunRef = React.useRef<PreviewRun option> None

    let selectedLfsKey =
        selectionPath
        |> Option.bind (PathHelpers.normalizePath >> tryFindLfs)
        |> Option.map (fun state -> $"{state.IsMaterialized}/{state.IsLocallyAvailable}")
        |> Option.defaultValue ""

    // A stamp that the app recorded after its own write is the new baseline and does not reload the preview.
    let stampIsOwnWrite =
        contentStamp
        |> Option.exists (fun stamp -> stamp.OwnWrite && Some stamp.Path = normalizedSelection)

    let stampKey =
        contentStamp
        |> Option.filter (fun stamp -> Some stamp.Path = normalizedSelection)
        |> Option.map (fun stamp -> $"{stamp.SizeBytes}/{stamp.MtimeMs}")
        |> Option.defaultValue ""

    React.useEffect (
        (fun () ->
            let previous = lastRunRef.current

            lastRunRef.current <-
                Some {
                    Selection = selectionPath
                    Entry = selectedEntry
                    DataMapPresent = dataMapPresent
                    LfsKey = selectedLfsKey
                    StampKey = stampKey
                }

            // The first run only records. A selection change records too, because the click opens that file itself.
            // The first stamp of a selection is the baseline, only a later stamp that differs counts as a change.
            let shouldAct =
                match previous with
                | None -> false
                | Some run ->
                    run.Selection = selectionPath
                    && (not (
                            match run.Entry, selectedEntry with
                            | None, None -> true
                            | Some a, Some b -> obj.ReferenceEquals(a, b)
                            | _ -> false
                        )
                        || run.DataMapPresent <> dataMapPresent
                        || run.LfsKey <> selectedLfsKey
                        || (run.StampKey <> "" && run.StampKey <> stampKey && not stampIsOwnWrite))

            if shouldAct then
                match tryGetDataMapMismatchReload dataMapPresent pageState with
                | Some(parentPath, requestedView) ->
                    reloadPreview
                        parentPath
                        (function
                        | PageState.ArcFilePage(nextArcFile, _) -> PageState.ArcFilePage(nextArcFile, requestedView)
                        | other -> other
                        )
                | None when shouldClearPageStateForLfsPointerSelection tryFindLfs selectedEntry pageState ->
                    clearPageState ()
                | None ->
                    match tryGetReloadableSelectedFilePath tryFindLfs selectedEntry pageState with
                    | None -> ()
                    | Some selectedPath -> reloadPreview selectedPath id
        ),
        [|
            box selectionPath
            box selectedEntry
            box dataMapPresent
            box selectedLfsKey
            box stampKey
        |]
    )
