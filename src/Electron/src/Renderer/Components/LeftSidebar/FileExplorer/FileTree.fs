namespace Renderer.Components.LeftSidebar.FileExplorer

open Renderer.Components.Helper
open Renderer.Components.Helper.ArcViewHelper
open Renderer.Components.LeftSidebar.FileExplorer.Modals
open Swate.Components
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Feliz
open Fable.Core
open ARCtrl
open Types
open Helper
open FileTreeMaterialization
open Renderer
open Renderer.Components.LeftSidebar.FileExplorer.Types

module private FileTreeHelper =

    type FileTreeDialog =
        | CreateDialog of ArcFilesDiscriminate
        | FileSystemCreateDialog of FileSystemCreateDraft
        | RenameDialog of ArcRenameDraft
        | DeleteDialog of FileItem

open FileTreeHelper

[<Erase; Mangle(false)>]
type FileTree =

    [<ReactComponent>]
    static member private EmptyFileTreePlaceholder() =
        Html.div [
            prop.className "swt:p-4 swt:text-center swt:text-gray-500"
            prop.text "No files found."
        ]

    [<ReactComponent>]
    static member FileTree(rootContextMenuRef: IRefValue<Browser.Types.HTMLElement option>) =

        let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
        let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()
        let fileStateCtx = Renderer.Context.FileStateContext.useFileStateCtx ()
        let gitStateCtx = Renderer.Context.GitStateContext.useGitStateCtx ()

        let errorModal = useErrorModalCtx ()

        let arcScopeId =
            appStateCtx
            |> Option.map PathHelpers.normalizePath
            |> Option.bind (fun path ->
                if System.String.IsNullOrWhiteSpace path then
                    None
                else
                    Some path
            )

        let activeDialog, setActiveDialog = React.useState<FileTreeDialog option> None
        let isDialogBusy, setIsDialogBusy = React.useState false

        let lfsActivityCtx = Renderer.Context.LfsActivityContext.useLfsActivityCtx ()
        let lfsActivityByPath = lfsActivityCtx.activities
        let lfsActivePaths = lfsActivityByPath |> Map.toList |> List.map fst
        // The delete confirmation reads the paths through this ref, so it sees the actions that
        // started after the modal opened.
        let lfsActivePathsRef = React.useRef lfsActivePaths
        lfsActivePathsRef.current <- lfsActivePaths

        let runLfsActionWithActivity
            (activity: string)
            (runAction: string -> JS.Promise<Result<unit, string>>)
            (relativePath: string)
            : JS.Promise<Result<unit, string>> =
            let entry =
                fileStateCtx.state.FileTree
                |> Array.tryFind (fun entry -> PathHelpers.pathsEqual entry.path relativePath)

            lfsActivityCtx.run activity entry runAction relativePath

        let runDownloadLfsFile relativePath =
            runLfsActionWithActivity
                "Downloading"
                Renderer.Components.Helper.GitLfsHelper.runDownloadLfsFile
                relativePath

        let runFreeLocalLfsCopy relativePath =
            runLfsActionWithActivity "Freeing" Renderer.Components.Helper.GitLfsHelper.runFreeLocalLfsCopy relativePath

        // The file watcher emits the initial tree too; only later tree updates should refresh open previews.
        let hasObservedFileTreeUpdateRef = React.useRef false

        React.useEffect (
            (fun () ->
                let filePaths = fileStateCtx.state.FileTree |> Array.map (fun entry -> entry.path)

                if
                    FileExplorerStateReconciliation.isSelectionMissing filePaths fileStateCtx.state.Selection.TreePath
                then
                    fileStateCtx.setSelection ArcSelection.empty

                    if
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval pageStateCtx.state
                    then
                        pageStateCtx.setState None
            ),
            [|
                box fileStateCtx.state.FileTree
                box fileStateCtx.state.Selection.TreePath
                box pageStateCtx.state
            |]
        )

        let treeEntries =
            React.useMemo (
                (fun () ->
                    Renderer.Context.LfsActivityContext.LfsActivityState.withBusyEntries
                        lfsActivityByPath
                        fileStateCtx.state.FileTree
                ),
                [| box fileStateCtx.state.FileTree; box lfsActivityByPath |]
            )

        let fileTree: FileTreeNode option =
            React.useMemo (
                (fun () ->
                    match treeEntries with
                    | [||] -> None
                    | _ -> treeEntries |> toFileTreeNode |> collapseSingleChildSameName |> Some
                ),
                [| box treeEntries |]
            )

        let materializedState, setMaterializedState =
            React.useStateWithUpdater FileTreeMaterialization.empty

        let reconciledMaterializedState =
            reconcileMaterializedState arcScopeId fileStateCtx.state.Selection.TreePath fileTree materializedState

        React.useEffect (
            (fun () ->
                setMaterializedState (fun current ->
                    if reconciledMaterializedState = current then
                        current
                    else
                        reconciledMaterializedState
                )
            ),
            [|
                box arcScopeId
                box fileTree
                box fileStateCtx.state.Selection.TreePath
            |]
        )

        let fileItem =
            fileTree
            |> Option.map (fun parent ->
                FileTreeMaterialization.toMaterializedFileItemTree
                    (fun node ->
                        let item = Helper.createItem node

                        {
                            item with
                                LfsActivity =
                                    item.Path
                                    |> Option.bind (fun path -> Map.tryFind path lfsActivityByPath)
                                    |> Option.map _.Label
                        }
                    )
                    reconciledMaterializedState.Paths
                    parent
                    true
            )

        let applyPreviewResult itemName result =
            match result with
            | Ok pageState -> pageStateCtx.setState (Some pageState)
            | Error errorMessage ->
                let fullErrorMessage = $"Could not open preview for '{itemName}': {errorMessage}"
                console.log ($"[Renderer] Error: {fullErrorMessage}")
                pageStateCtx.setState (Some(Renderer.Types.PageState.ErrorPage fullErrorMessage))

        let withStartingView activeView =
            function
            | Renderer.Types.PageState.ArcFilePage(arcFile, _) ->
                Renderer.Types.PageState.ArcFilePage(arcFile, Some activeView)
            | pageState -> pageState

        let openPreview (alwaysOpen: bool) (item: FileItem) =
            promise {
                match item.Path with
                | None ->
                    errorModal.enqueue (
                        ErrorModalRequest.create ($"File '{item.Name}' has no path.", title = "Preview failed")
                    )
                | Some path when item.IsDirectory ->
                    let selectedPath = PathHelpers.normalizePath path

                    let isAlreadyOpen =
                        Helper.isArcEntityFolderViewOpen
                            pageStateCtx.state
                            fileStateCtx.state.Selection.TreePath
                            selectedPath

                    // Only folders with ARC metadata open a view, and only if it is not on screen yet. Otherwise the
                    // click just expands or collapses the folder and keeps the selection, because the open view, such
                    // as the Markdown editor, saves to the selected path.
                    match Helper.tryGetArcEntityWorkbookName selectedPath with
                    | Some _ when isAlreadyOpen && not alwaysOpen -> ()
                    | Some workbookName ->
                        fileStateCtx.setSelection (ArcSelection.forTreePath (Some selectedPath))
                        let! result = openView $"{selectedPath}/{workbookName}"

                        result
                        |> Result.map (withStartingView Swate.Components.Page.ArcFileEditor.Types.ActiveView.Metadata)
                        |> applyPreviewResult item.Name
                    | None -> ()
                | Some path ->
                    let selectedPath = PathHelpers.normalizePath path
                    fileStateCtx.setSelection (ArcSelection.forTreePath (Some selectedPath))

                    if Swate.Components.Page.FileExplorer.Helper.needsLfsDownload item then
                        pageStateCtx.setState None
                    else
                        let! result = openView selectedPath
                        applyPreviewResult item.Name result
            }
            |> Promise.start

        let reloadPreviewAfterFileTreeUpdate path transformPageState =
            let applyReloadError details =
                pageStateCtx.setState (
                    Some(
                        Renderer.Types.PageState.ErrorPage
                            $"The preview could not be refreshed after the File Explorer changed. Select the file again. Details: {details}"
                    )
                )

            promise {
                match! openView path with
                | Ok pageState -> pageStateCtx.setState (Some(transformPageState pageState))
                | Error errorMessage -> applyReloadError errorMessage
            }
            |> Promise.catch (fun exn -> applyReloadError exn.Message)
            |> Promise.start

        React.useEffect (
            (fun () ->
                if hasObservedFileTreeUpdateRef.current then
                    match
                        FileExplorerStateReconciliation.tryGetDataMapMismatchReload
                            fileStateCtx.state.FileTree
                            pageStateCtx.state
                    with
                    | Some(parentPath, requestedView) ->
                        reloadPreviewAfterFileTreeUpdate
                            parentPath
                            (function
                            | Renderer.Types.PageState.ArcFilePage(nextArcFile, _) ->
                                Renderer.Types.PageState.ArcFilePage(nextArcFile, requestedView)
                            | pageState -> pageState
                            )
                    | None when
                        FileExplorerStateReconciliation.shouldClearPageStateForLfsPointerSelection
                            fileStateCtx.state.FileTree
                            fileStateCtx.state.Selection.TreePath
                            pageStateCtx.state
                        ->
                        pageStateCtx.setState None
                    | None ->
                        match
                            FileExplorerStateReconciliation.tryGetReloadableSelectedFilePath
                                fileStateCtx.state.FileTree
                                fileStateCtx.state.Selection.TreePath
                                pageStateCtx.state
                        with
                        | None -> ()
                        | Some selectedPath -> reloadPreviewAfterFileTreeUpdate selectedPath id
                else
                    hasObservedFileTreeUpdateRef.current <- true
            ),
            [| box fileStateCtx.state.FileTree |]
        )

        let handleExpansionChange (item: FileItem) (willExpand: bool) =
            if willExpand then
                match item.Path with
                | Some path -> setMaterializedState (fun _ -> materialize path reconciledMaterializedState)
                | None -> ()

        let openDialog dialog =
            setIsDialogBusy false
            setActiveDialog (Some dialog)

        let closeDialog () =
            setIsDialogBusy false
            setActiveDialog None

        let openFileSystemCreateModal kind (item: FileItem) =
            if canCreateFileSystemItemIn kind item then
                openDialog (FileSystemCreateDialog { Parent = item; Kind = kind })

        let requestDeleteItem =
            FileTreeDeleteWorkflow.requestDeleteItem (Option.iter (DeleteDialog >> openDialog))

        let requestRenameItem =
            FileTreeRenameWorkflow.requestRenameItem (Option.iter (RenameDialog >> openDialog)) errorModal.enqueue

        let rootPath = fileTree |> Option.map (fun (tree: FileTreeNode) -> tree.path)

        let canCreateFromItem path item =
            match path with
            | Some path -> tryGetInlineArcCreateKind path item
            | None -> None
            |> Option.isSome

        let createFromItem path item =
            match path with
            | Some path -> tryGetInlineArcCreateKind path item
            | None -> None
            |> Option.iter (fun kind -> openDialog (CreateDialog kind))

        let applyCreateError errorMessage =
            errorModal.enqueue (ErrorModalRequest.create (errorMessage, title = "Could not create ARC file"))

        let applyFileSystemCreateError errorMessage =
            errorModal.enqueue (ErrorModalRequest.create (errorMessage, title = "Could not create file or folder"))

        let reloadPreviewByPath (path: string) : JS.Promise<Result<unit, string>> = promise {
            let! openResult = openView path

            match openResult with
            | Ok pageState ->
                pageStateCtx.setState (Some pageState)
                return Ok()
            | Error errorMessage -> return Error errorMessage
        }

        let (activeCreateKind, activeFileSystemCreateDraft, activeRenameDraft, activeDeleteItem) =
            match activeDialog with
            | Some(CreateDialog kind) -> Some kind, None, None, None
            | Some(FileSystemCreateDialog draft) -> None, Some draft, None, None
            | Some(RenameDialog renameDraft) -> None, None, Some renameDraft, None
            | Some(DeleteDialog item) -> None, None, None, Some item
            | None -> None, None, None, None

        let confirmDeleteItem () =
            if not isDialogBusy then
                FileTreeDeleteWorkflow.confirmDeleteItem {
                    pendingDeleteItem = activeDeleteItem
                    closeDeleteModal = closeDialog
                    setIsDeleting = setIsDialogBusy
                    enqueueError = errorModal.enqueue
                    getLfsActivePaths = fun () -> lfsActivePathsRef.current
                    deletePath = Api.ipcArcVaultApi.deletePath
                }

        let createArcEntry kind (identifier: string) =
            if not isDialogBusy then
                let existingPaths =
                    fileStateCtx.state.FileTree |> Array.map (fun entry -> entry.path)

                match tryBuildArcCreateDraft kind identifier existingPaths with
                | Error errorMessage -> applyCreateError errorMessage
                | Ok draft ->
                    setIsDialogBusy true

                    promise {
                        let! createResult = ArcFileApiHelper.addArcFileAndOpen draft.ArcFile

                        match createResult with
                        | Error exn ->
                            setIsDialogBusy false
                            applyCreateError exn.Message
                        | Ok createdArcFileDto ->
                            let selectedPath = PathHelpers.normalizePath createdArcFileDto.path
                            fileStateCtx.setSelection (ArcSelection.forTreePath (Some selectedPath))

                            let pageState = Renderer.Types.PageState.fromFileContentDTO createdArcFileDto
                            pageStateCtx.setState (Some pageState)

                            closeDialog ()
                    }
                    |> Promise.catch (fun exn ->
                        setIsDialogBusy false
                        applyCreateError exn.Message
                    )
                    |> Promise.start

        let createFileSystemItem (name: string) =
            if not isDialogBusy then
                match activeFileSystemCreateDraft with
                | None -> closeDialog ()
                | Some draft ->
                    match draft.Parent.Path |> Option.map PathHelpers.normalizeCanonicalRelativePath with
                    | None -> applyFileSystemCreateError "Could not resolve the selected folder path."
                    | Some parentPath ->
                        setIsDialogBusy true

                        promise {
                            let! createResult =
                                Api.ipcArcVaultApi.createFileSystemItem {
                                    parentPath = parentPath
                                    name = name
                                    kind = draft.Kind
                                }

                            match createResult with
                            | Error exn -> applyFileSystemCreateError exn.Message
                            | Ok createdPath ->
                                let selectedPath = PathHelpers.normalizePath createdPath
                                fileStateCtx.setSelection (ArcSelection.forTreePath (Some selectedPath))

                                match draft.Kind with
                                | FileSystemItemKind.File ->
                                    let! openResult = Api.ipcArcVaultApi.openFile selectedPath

                                    match openResult with
                                    | Ok dto ->
                                        let pageState = Renderer.Types.PageState.fromFileContentDTO dto
                                        pageStateCtx.setState (Some pageState)
                                    | Error _ ->
                                        let dto = FileContentDTO.create FileContentType.PlainText "" selectedPath

                                        let pageState = Renderer.Types.PageState.fromFileContentDTO dto
                                        pageStateCtx.setState (Some pageState)
                                | FileSystemItemKind.Folder -> pageStateCtx.setState None

                                closeDialog ()
                        }
                        |> Promise.catch (fun exn -> applyFileSystemCreateError exn.Message)
                        |> Promise.map (fun _ -> setIsDialogBusy false)
                        |> Promise.start

        let createDataMap (parentInfo: DatamapParentInfo) =
            promise {
                match!
                    ArcFileApiHelper.withArcFileRequest
                        (ArcFiles.DataMap(Some parentInfo, Datamap.init ()))
                        Api.ipcArcVaultApi.addArcFile
                with
                | Error exn -> applyCreateError exn.Message
                | Ok _ -> ()
            }
            |> Promise.catch (fun exn -> applyCreateError exn.Message)
            |> Promise.start

        let tryFindDataMapItemByPath path =
            fileStateCtx.state.FileTree
            |> Array.tryFind (fun entry -> PathHelpers.pathsEqual entry.path path)
            |> Option.map (fun entry ->
                let item =
                    Swate.Components.Page.FileExplorer.Types.FileTree.createFile
                        entry.name
                        (Some entry.path)
                        FileItemIcon.Document

                { item with Id = entry.path }
            )

        let itemActions item = [
            yield!
                rootFolderContextMenuItems
                    "notes"
                    "Create new item in"
                    "swt:fluent--note-add-24-regular"
                    (fun () -> pageStateCtx.setState (Some Renderer.Types.PageState.NotesDraftPage))
                    item
            yield! FileTreeContextMenu.renameContextMenuItems lfsActivePaths requestRenameItem item
        ]

        let runToggleLfsMark (relativePath: string) (markAsLfs: bool) = promise {
            let! result = Renderer.Components.Helper.GitLfsHelper.runToggleLfsMark relativePath markAsLfs
            gitStateCtx.refresh ()

            match result with
            | Ok() -> return Ok()
            | Error errorMessage -> return Error errorMessage
        }

        let contextMenuConfig: ContextMenuConfig = {
            openItem = openPreview true
            arcRootPath = appStateCtx
            openCreateModal = (fun kind -> openDialog (CreateDialog kind))
            openNoteDraft = (fun () -> pageStateCtx.setState (Some Renderer.Types.PageState.NotesDraftPage))
            createDataMap = createDataMap
            tryFindDataMapItemByPath = tryFindDataMapItemByPath
            openFileSystemCreateModal = openFileSystemCreateModal
            requestRenameItem = requestRenameItem
            requestDeleteItem = requestDeleteItem
            pathActionConfig = {
                openPathInFileExplorer = Api.ipcArcVaultApi.showPathInFileExplorer
                openPathWithDefaultApplication = Api.ipcArcVaultApi.openPathWithDefaultApplication
                importExternalFiles = fileStateCtx.importExternalFiles
                enqueueError = errorModal.enqueue
            }
            enqueueError = errorModal.enqueue
            runToggleLfsMark = runToggleLfsMark
            runDownloadLfsFile = runDownloadLfsFile
            runFreeLocalLfsCopy = runFreeLocalLfsCopy
            lfsActivePaths = lfsActivePaths
        }

        let createContextMenuItems =
            FileTreeContextMenu.createContextMenuItems contextMenuConfig arcScopeId

        let rootContextMenu rootItem =
            let rootMenuItem = {
                rootItem with
                    Path = Some ""
                    IsDirectory = true
            }

            Swate.Components.Primitive.ContextMenu.ContextMenu.ContextMenu(
                (fun _ ->
                    FileTreeContextMenu.rootContextMenuItems contextMenuConfig rootMenuItem
                    |> List.map (fun item -> item.ToPrimitiveContextMenuItem())
                ),
                ref = rootContextMenuRef,
                onSpawn = (fun _ -> Some(box ()))
            )

        let getItemStatusAction =
            Renderer.Components.FileExplorerLfs.createLfsPillAction
                errorModal.enqueue
                arcScopeId
                runDownloadLfsFile
                runFreeLocalLfsCopy

        let confirmRenameItem (newName: string) =
            if not isDialogBusy then
                FileTreeRenameWorkflow.confirmRenameItem
                    {
                        pendingRenameDraft = activeRenameDraft
                        selectedTreePath = fileStateCtx.state.Selection.TreePath
                        pageState = pageStateCtx.state
                        closeRenameModal = closeDialog
                        setIsRenaming = setIsDialogBusy
                        setSelection = fileStateCtx.setSelection
                        refreshGitStatus = gitStateCtx.refresh
                        reloadPreviewByPath = reloadPreviewByPath
                        renamePath = Api.ipcArcVaultApi.renamePath
                        enqueueError = errorModal.enqueue
                    }
                    newName

        let arcCreateModal =
            CreateArcFileModal.Main(
                isOpen = activeCreateKind.IsSome,
                kind = (activeCreateKind |> Option.defaultValue ArcFilesDiscriminate.Study),
                close = closeDialog,
                submit = createArcEntry,
                isCreating = isDialogBusy
            )

        let activeFileSystemCreateKind =
            activeFileSystemCreateDraft
            |> Option.map _.Kind
            |> Option.defaultValue FileSystemItemKind.File

        let fileSystemCreateModal =
            CreateFileSystemItemModal.Main(
                isOpen = activeFileSystemCreateDraft.IsSome,
                kind = activeFileSystemCreateKind,
                parentName = (activeFileSystemCreateDraft |> Option.map _.Parent.Name),
                close = closeDialog,
                submit = createFileSystemItem,
                isCreating = isDialogBusy
            )

        let deleteConfirmModal =
            FileTreeDeleteModal.Main(
                isOpen = activeDeleteItem.IsSome,
                itemName = (activeDeleteItem |> Option.map _.Name),
                close = closeDialog,
                submit = confirmDeleteItem,
                isDeleting = isDialogBusy
            )

        let renameModal =
            FileTreeRenameModal.Main(
                isOpen = activeRenameDraft.IsSome,
                itemName = (activeRenameDraft |> Option.map (fun draft -> draft.Item.Name)),
                initialName = (activeRenameDraft |> Option.map _.InitialName),
                close = closeDialog,
                submit = confirmRenameItem,
                isRenaming = isDialogBusy
            )

        match fileItem with
        | Some rootItem ->
            let visibleItems = rootItem.Children |> Option.defaultValue []

            React.Fragment [
                Html.div [
                    prop.className "swt:w-full"
                    prop.children [
                        Swate.Components.Page.FileExplorer.FileExplorer.FileExplorer(
                            initialItems = visibleItems,
                            onItemClick = openPreview false,
                            onDirectoryExpansionChange = handleExpansionChange,
                            onContextMenu = createContextMenuItems,
                            getItemIconClass = getItemIconClass,
                            canCreateItem = canCreateFromItem rootPath,
                            onCreateItem = createFromItem rootPath,
                            getItemActions = itemActions,
                            getItemStatusAction = getItemStatusAction,
                            canDeleteItem =
                                (fun (item: FileItem) ->
                                    not (FileTreeContextMenu.isLockedByLfsActivity lfsActivePaths item)
                                    && canDeleteFileItem item
                                ),
                            onDeleteItem = requestDeleteItem,
                            selectedItemId = fileStateCtx.state.Selection.TreePath,
                            includeDefaultContextMenuItems = false,
                            delegateHorizontalScrollToParent = true,
                            truncateOverflowingItemNames = true
                        )
                    ]
                ]
                rootContextMenu rootItem
                arcCreateModal
                fileSystemCreateModal
                renameModal
                deleteConfirmModal
            ]
        | None ->
            React.Fragment [
                FileTree.EmptyFileTreePlaceholder()
                arcCreateModal
                fileSystemCreateModal
                renameModal
                deleteConfirmModal
            ]
