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
        | FreeLfsDialog of string

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

        let expansionCtx =
            Renderer.Context.FileTreeExpansionContext.useFileTreeExpansionCtx ()

        let snapshot = Renderer.Context.FileTreeHooks.useFileTreeSnapshot ()

        let selectionKnown =
            Renderer.Context.FileTreeHooks.useFileTreeKnows (
                fileStateCtx.state.Selection.TreePath |> Option.map PathHelpers.normalizePath
            )

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
        let tryFindLfs = Renderer.Context.LargeObjectStore.useLargeObjectLookup ()
        let lfsActivityByPath = lfsActivityCtx.activities

        let lfsActivePaths =
            React.useMemo ((fun () -> lfsActivityByPath |> Map.toList |> List.map fst), [| box lfsActivityByPath |])

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
                Renderer.Context.FileTreeStore.tryFind fileStateCtx.fileTreeStore.Snapshot relativePath

            lfsActivityCtx.run activity entry runAction relativePath

        let runDownloadLfsFile relativePath =
            runLfsActionWithActivity
                "Downloading"
                Renderer.Components.Helper.GitLfsHelper.runDownloadLfsFile
                relativePath

        let runFreeLocalLfsCopy relativePath =
            runLfsActionWithActivity "Freeing" Renderer.Components.Helper.GitLfsHelper.runFreeLocalLfsCopy relativePath

        React.useEffect (
            (fun () ->
                if FileExplorerStateReconciliation.isKnownAbsent selectionKnown then
                    fileStateCtx.setSelection ArcSelection.empty

                    if
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval pageStateCtx.state
                    then
                        pageStateCtx.setState None
            ),
            [|
                box selectionKnown
                box fileStateCtx.state.Selection.TreePath
                box pageStateCtx.state
            |]
        )

        let withBusy =
            Renderer.Context.LfsActivityContext.LfsActivityState.withBusyChildren lfsActivityByPath

        // Entries are immutable and keep their instance while unchanged, so the base row of an entry is built once.
        // A new large object snapshot gets a new cache, because the rows carry that state.
        let itemCache =
            React.useMemo (
                (fun () -> Renderer.ReferenceCache.ReferenceCache<FileEntry, FileItem>()),
                [| box tryFindLfs |]
            )

        let createFileItem (entry: FileEntry) =
            let baseItem = itemCache.GetOrAdd(entry, Helper.createItem tryFindLfs)

            // Only the entries with a running action get a copy.
            if lfsActivityByPath.IsEmpty then
                baseItem
            else
                match Map.tryFind entry.path lfsActivityByPath with
                | Some activity -> {
                    baseItem with
                        LfsActivity = Some activity.Label
                  }
                | None -> baseItem

        let rootEntry = Renderer.Context.FileTreeStore.tryFind snapshot ""

        let fileItem =
            React.useMemo (
                (fun () ->
                    rootEntry
                    |> Option.map (fun root -> {
                        createFileItem root with
                            Children =
                                Some(
                                    FileTreeMaterialization.buildVisibleItems
                                        createFileItem
                                        withBusy
                                        snapshot
                                        expansionCtx.expandedIds
                                )
                    })
                ),
                [|
                    box snapshot
                    box expansionCtx.expandedIds
                    box tryFindLfs
                    box lfsActivityByPath
                |]
            )

        // A folder shows its load: an ellipsis icon while it loads and the error tone after a failure until the
        // next expand retries it. The overlay copies the folder rows with a state and leaves every other row alone,
        // so a change of the load states does not rebuild the items.
        let fileItem =
            React.useMemo (
                (fun () ->
                    if expansionCtx.loadStates.IsEmpty then
                        fileItem
                    else
                        fileItem
                        |> Option.map (fun root -> {
                            root with
                                Children =
                                    root.Children
                                    |> Option.map (
                                        FileTreeMaterialization.overlayDirectories (fun item ->
                                            match item.Path with
                                            | None -> item
                                            | Some path ->
                                                match
                                                    Map.tryFind
                                                        (PathKey.ofPath snapshot.CaseInsensitive path)
                                                        expansionCtx.loadStates
                                                with
                                                | Some Renderer.Context.FileTreeExpansionContext.Loading -> {
                                                    item with
                                                        Icon = FileItemIcon.MoreHorizontal
                                                        IconTone = Some FileItemIconTone.Info
                                                  }
                                                | Some(Renderer.Context.FileTreeExpansionContext.Failed _) -> {
                                                    item with
                                                        IconTone = Some FileItemIconTone.Error
                                                  }
                                                | None -> item
                                        )
                                    )
                        })
                ),
                [|
                    box fileItem
                    box expansionCtx.loadStates
                    box snapshot.CaseInsensitive
                |]
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

        FileExplorerStateReconciliation.useReconcileSelectedPreview
            tryFindLfs
            fileStateCtx.state.Selection.TreePath
            pageStateCtx.state
            expansionCtx.contentStamp
            reloadPreviewAfterFileTreeUpdate
            (fun () -> pageStateCtx.setState None)

        let handleExpansionChange (item: FileItem) (willExpand: bool) =
            if willExpand then
                match item.Path with
                | Some path -> expansionCtx.expandDirectory path
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

        // The context menu runs Free through this request, which only opens the confirmation.
        let requestFreeLocalLfsCopy (relativePath: string) : JS.Promise<Result<unit, string>> =
            openDialog (FreeLfsDialog relativePath)
            promise { return Ok() }

        // The row getters below keep their identity until their inputs change. A new identity re-renders every row.
        // The dialog state setters never change, so the workflows that only open dialogs can be created once.
        let requestDeleteItem =
            React.useMemo (
                (fun () -> FileTreeDeleteWorkflow.requestDeleteItem (Option.iter (DeleteDialog >> openDialog))),
                [||]
            )

        let requestRenameItem =
            React.useMemo (
                (fun () ->
                    FileTreeRenameWorkflow.requestRenameItem
                        (Option.iter (RenameDialog >> openDialog))
                        errorModal.enqueue
                ),
                [| box errorModal.enqueue |]
            )

        let rootPath = rootEntry |> Option.map (fun root -> root.path)

        let canCreateFromItem =
            React.useCallback (
                (fun (item: FileItem) ->
                    match rootPath with
                    | Some path -> tryGetInlineArcCreateKind path item
                    | None -> None
                    |> Option.isSome
                ),
                [| box rootPath |]
            )

        let createFromItem =
            React.useCallback (
                (fun (item: FileItem) ->
                    match rootPath with
                    | Some path -> tryGetInlineArcCreateKind path item
                    | None -> None
                    |> Option.iter (fun kind -> openDialog (CreateDialog kind))
                ),
                [| box rootPath |]
            )

        let canDeleteItem =
            React.useCallback (
                (fun (item: FileItem) ->
                    not (FileTreeContextMenu.isLockedByLfsActivity lfsActivePaths item)
                    && canDeleteFileItem item
                ),
                [| box lfsActivePaths |]
            )

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
            | Some(FreeLfsDialog _)
            | None -> None, None, None, None

        let activeFreeLfsPath =
            match activeDialog with
            | Some(FreeLfsDialog path) -> Some path
            | _ -> None

        let confirmFreeLocalLfsCopy () =
            activeFreeLfsPath
            |> Option.iter (fun path ->
                closeDialog ()

                promise {
                    match! runFreeLocalLfsCopy path with
                    | Ok() -> ()
                    | Error msg ->
                        errorModal.enqueue (
                            ErrorModalRequest.create (
                                $"Git LFS cleanup failed for '{PathHelpers.getFileName path}': {msg}",
                                title = "Git LFS cleanup failed",
                                ?scopeId = arcScopeId
                            )
                        )
                }
                |> Promise.start
            )

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
                // The entity files sit directly in the loaded entity folders of the zone folder.
                let existingPaths =
                    let current = fileStateCtx.fileTreeStore.Snapshot

                    arcCreateKinds
                    |> List.tryFind (fun config -> config.Kind = kind)
                    |> Option.map (fun config ->
                        Renderer.Context.FileTreeStore.children current config.FolderName
                        |> Array.collect (fun entity -> Renderer.Context.FileTreeStore.children current entity.path)
                        |> Array.map _.path
                    )
                    |> Option.defaultValue [||]

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
                        (ArcFiles.DataMap(Some parentInfo, DataMap.init ()))
                        Api.ipcArcVaultApi.addArcFile
                with
                | Error exn -> applyCreateError exn.Message
                | Ok _ -> ()
            }
            |> Promise.catch (fun exn -> applyCreateError exn.Message)
            |> Promise.start

        let tryFindDataMapItemByPath path =
            Renderer.Context.FileTreeStore.tryFind fileStateCtx.fileTreeStore.Snapshot path
            |> Option.map (fun entry ->
                let item =
                    Swate.Components.Page.FileExplorer.Types.FileTree.createFile
                        entry.name
                        (Some entry.path)
                        FileItemIcon.Document

                { item with Id = entry.path }
            )

        let itemActions =
            React.useCallback (
                (fun (item: FileItem) -> [
                    yield!
                        rootFolderContextMenuItems
                            "notes"
                            "Create new item in"
                            "swt:fluent--note-add-24-regular"
                            (fun () -> pageStateCtx.setState (Some Renderer.Types.PageState.NotesDraftPage))
                            item
                    yield! FileTreeContextMenu.renameContextMenuItems lfsActivePaths requestRenameItem item
                ]),
                [|
                    box lfsActivePaths
                    box requestRenameItem
                    box pageStateCtx.setState
                |]
            )

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
            runFreeLocalLfsCopy = requestFreeLocalLfsCopy
            refreshDirectory = (fun (item: FileItem) -> item.Path |> Option.iter expansionCtx.refreshDirectory)
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

        // The pill action reads the download through a ref, so it keeps its identity across renders.
        let runDownloadRef = React.useRef runDownloadLfsFile
        runDownloadRef.current <- runDownloadLfsFile

        let getItemStatusAction =
            React.useMemo (
                (fun () ->
                    Renderer.Components.FileExplorerLfs.createLfsPillAction
                        errorModal.enqueue
                        arcScopeId
                        (fun path -> runDownloadRef.current path)
                ),
                [| box errorModal.enqueue; box arcScopeId |]
            )

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

        let freeLfsModal =
            FileTreeFreeLfsModal.Main(
                isOpen = activeFreeLfsPath.IsSome,
                itemName =
                    (activeFreeLfsPath
                     |> Option.map (fun path -> path.Split([| '/'; '\\' |]) |> Array.last)),
                close = closeDialog,
                submit = confirmFreeLocalLfsCopy
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
                    prop.className "swt:w-full swt:h-full swt:min-h-0"
                    prop.children [
                        Swate.Components.Page.FileExplorer.FileExplorer.FileExplorer(
                            initialItems = visibleItems,
                            onItemClick = openPreview false,
                            onDirectoryExpansionChange = handleExpansionChange,
                            expandedIds = expansionCtx.expandedIds,
                            onExpandedIdsChange = expansionCtx.setExpandedIds,
                            onContextMenu = createContextMenuItems,
                            getItemIconClass = getItemIconClass,
                            canCreateItem = canCreateFromItem,
                            onCreateItem = createFromItem,
                            getItemActions = itemActions,
                            getItemStatusAction = getItemStatusAction,
                            canDeleteItem = canDeleteItem,
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
                freeLfsModal
            ]
        | None ->
            React.Fragment [
                FileTree.EmptyFileTreePlaceholder()
                arcCreateModal
                fileSystemCreateModal
                renameModal
                deleteConfirmModal
                freeLfsModal
            ]
