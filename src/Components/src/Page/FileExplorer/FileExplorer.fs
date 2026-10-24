namespace Swate.Components.Page.FileExplorer

open Swate.Components
open Swate.Components.Page.FileExplorer.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz


module private FileExplorerHelper =

    let tryGetEventTargetElement (e: Browser.Types.Event) : Browser.Types.Element option =
        let targetObj: obj = box e.target

        if isNullOrUndefined targetObj then
            None
        elif isNullOrUndefined targetObj?closest then
            let parentElement: obj = targetObj?parentElement

            if isNullOrUndefined parentElement then
                None
            else
                Some(unbox<Browser.Types.Element> parentElement)
        else
            Some(unbox<Browser.Types.Element> targetObj)

    let private copyPathToClipboard (path: string) =
        promise {
            try
                let windowObj: obj = Browser.Dom.window
                do! windowObj?navigator?clipboard?writeText (path)
            with ex ->
                Browser.Dom.console.warn ($"Could not copy file path: {path}", ex)
        }
        |> Promise.start

    let private defaultContextMenuItems
        (item: FileItem)
        (isExpanded: bool)
        (selectItem: FileItem -> unit)
        (getCopyPath: FileItem -> string option)
        (getCopyRelativePath: FileItem -> string option)
        (setExpanded: FileItem -> bool -> unit)
        (onRefreshDirectory: (FileItem -> unit) option)
        : Swate.Components.Page.FileExplorer.Types.ContextMenuItem list =
        [
            if not item.IsDirectory then
                ContextMenuItem.create "Open" "swt:fluent--open-24-regular" (fun () -> selectItem item)

            match item.Path with
            | Some _ ->
                match getCopyPath item with
                | Some path ->
                    ContextMenuItem.create
                        "Copy Path"
                        "swt:fluent--copy-24-regular"
                        (fun () -> copyPathToClipboard path)
                | None -> ()

                match getCopyRelativePath item with
                | Some path ->
                    ContextMenuItem.create
                        "Copy Relative Path"
                        "swt:fluent--copy-24-regular"
                        (fun () -> copyPathToClipboard path)
                | None -> ()
            | None -> ()

            if FileExplorerLogic.canExpandDirectory item then
                ContextMenuItem.create
                    (if isExpanded then "Collapse" else "Expand")
                    (if isExpanded then
                         "swt:fluent--folder-open-24-regular"
                     else
                         "swt:fluent--folder-24-regular")
                    (fun () -> setExpanded item (not isExpanded))

            if item.IsDirectory then
                match onRefreshDirectory with
                | Some refresh ->
                    ContextMenuItem.create "Refresh folder" "swt:fluent--arrow-sync-24-regular" (fun () -> refresh item)
                | None -> ()
        ]

    let getContextMenuItems
        (item: FileItem)
        (isExpanded: bool)
        (selectItem: FileItem -> unit)
        (onContextMenu: (FileItem -> Swate.Components.Page.FileExplorer.Types.ContextMenuItem list) option)
        (getCopyPath: FileItem -> string option)
        (getCopyRelativePath: FileItem -> string option)
        (includeDefaultContextMenuItems: bool)
        (setExpanded: FileItem -> bool -> unit)
        (onRefreshDirectory: (FileItem -> unit) option)
        =
        let defaultItems =
            if includeDefaultContextMenuItems then
                defaultContextMenuItems
                    item
                    isExpanded
                    selectItem
                    getCopyPath
                    getCopyRelativePath
                    setExpanded
                    onRefreshDirectory
            else
                []

        let customItems =
            onContextMenu |> Option.map (fun fn -> fn item) |> Option.defaultValue []

        defaultItems @ customItems

    /// The props the stable row handlers read at call time. The component refreshes it on every render.
    type Latest = {
        Rows: FileExplorerLogic.VisibleRow[]
        ExpandedIds: Set<string>
        IsExpansionControlled: bool
        IsSelectionControlled: bool
        OnItemClick: (FileItem -> unit) option
        OnDirectoryExpansionChange: (FileItem -> bool -> unit) option
        OnExpandedIdsChange: (Set<string> -> unit) option
        GetItemIconClass: FileItem -> string option
        GetItemActions: FileItem -> Swate.Components.Page.FileExplorer.Types.ContextMenuItem list
        GetItemStatusAction: FileItem -> Swate.Components.Page.FileExplorer.Types.ContextMenuItem option
        CanCreateItem: FileItem -> bool
        OnCreateItem: (FileItem -> unit) option
        CanDeleteItem: FileItem -> bool
        OnDeleteItem: (FileItem -> unit) option
    }

// ---------------------------------------------------------------------------
[<Mangle(false); Erase>]
type FileExplorer =

    [<ReactComponent(true)>]
    static member FileExplorer
        (
            ?initialItems: FileItem list,
            ?onItemClick: FileItem -> unit,
            ?onContextMenu: FileItem -> Swate.Components.Page.FileExplorer.Types.ContextMenuItem list,
            ?canCreateItem: FileItem -> bool,
            ?onCreateItem: FileItem -> unit,
            ?getItemActions: FileItem -> Swate.Components.Page.FileExplorer.Types.ContextMenuItem list,
            ?getItemStatusAction: FileItem -> Swate.Components.Page.FileExplorer.Types.ContextMenuItem option,
            ?canDeleteItem: FileItem -> bool,
            ?onDeleteItem: FileItem -> unit,
            ?selectedItemId: string option,
            ?onDirectoryExpansionChange: FileItem -> bool -> unit,
            ?onExpansionChange: FileItem -> bool -> unit,
            ?delegateHorizontalScrollToParent: bool,
            ?truncateOverflowingItemNames: bool,
            ?getItemIconClass: FileItem -> string option,
            ?getCopyPath: FileItem -> string option,
            ?getCopyRelativePath: FileItem -> string option,
            ?includeDefaultContextMenuItems: bool,
            ?expandedIds: Set<string>,
            ?onExpandedIdsChange: Set<string> -> unit,
            ?onRefreshDirectory: FileItem -> unit,
            ?ariaLabel: string
        ) =
        // A row re-renders when the parent supplies new row functions. The identities are taken before the defaults are applied.
        let rowFunctionIdentities = [|
            box getItemActions
            box getItemStatusAction
            box getItemIconClass
            box canCreateItem
            box canDeleteItem
            box onCreateItem
            box onDeleteItem
        |]

        let items = defaultArg initialItems []
        let ariaLabel = defaultArg ariaLabel "File explorer"

        let delegateHorizontalScrollToParent =
            defaultArg delegateHorizontalScrollToParent false

        let truncateOverflowingItemNames = defaultArg truncateOverflowingItemNames false

        let getItemIconClass = defaultArg getItemIconClass (fun _ -> None)
        let getCopyPath = defaultArg getCopyPath (fun item -> item.Path)
        let getCopyRelativePath = defaultArg getCopyRelativePath (fun _ -> None)
        let includeDefaultContextMenuItems = defaultArg includeDefaultContextMenuItems true
        let canCreateItem = defaultArg canCreateItem (fun (_: FileItem) -> false)
        let getItemActions = defaultArg getItemActions (fun (_: FileItem) -> [])
        let getItemStatusAction = defaultArg getItemStatusAction (fun (_: FileItem) -> None)
        let canDeleteItem = defaultArg canDeleteItem (fun (_: FileItem) -> false)

        let onDirectoryExpansionChange =
            onDirectoryExpansionChange |> Option.orElse onExpansionChange

        // The uncontrolled state starts from the IsExpanded hints of the first items.
        let initialModel = React.useMemo ((fun () -> FileExplorerLogic.init items), [||])

        let model, dispatch =
            React.useReducer ((fun model msg -> FileExplorerLogic.update msg model), initialModel)

        let containerRef = React.useElementRef ()
        let focusedId, setFocusedId = React.useState (None: string option)
        let treeRef = React.useElementRef ()
        let scrollRef = React.useElementRef ()

        let isExpansionControlled = expandedIds.IsSome
        let expandedIds = defaultArg expandedIds model.ExpandedIds
        let selectedPropId = selectedItemId |> Option.bind id

        let selectedId =
            if selectedItemId.IsSome then
                selectedPropId
            else
                model.SelectedId

        let rows =
            React.useMemo (
                (fun () -> FileExplorerLogic.flattenVisibleRows expandedIds items),
                [| box initialItems; box expandedIds |]
            )

        let selectedPath =
            React.useMemo (
                (fun () ->
                    selectedId
                    |> Option.bind (fun id -> FileTree.getPath id items [])
                    |> Option.defaultValue []
                ),
                [| box initialItems; box selectedId |]
            )

        let highlightedIds =
            React.useMemo ((fun () -> selectedPath |> List.map _.Id |> Set.ofList), [| box selectedPath |])

        // Selecting an item reveals it: the directories above it with loaded children expand.
        React.useEffect (
            (fun () ->
                if not isExpansionControlled && not selectedPath.IsEmpty then
                    let ancestorIds =
                        selectedPath
                        |> List.take (selectedPath.Length - 1)
                        |> List.choose (fun item -> if item.Children.IsSome then Some item.Id else None)
                        |> Set.ofList

                    if not ancestorIds.IsEmpty then
                        dispatch (FileExplorerLogic.ExpandMany ancestorIds)
            ),
            [| box selectedId; box (not selectedPath.IsEmpty) |]
        )

        let currentLatest: FileExplorerHelper.Latest = {
            Rows = rows
            ExpandedIds = expandedIds
            IsExpansionControlled = isExpansionControlled
            IsSelectionControlled = selectedItemId.IsSome
            OnItemClick = onItemClick
            OnDirectoryExpansionChange = onDirectoryExpansionChange
            OnExpandedIdsChange = onExpandedIdsChange
            GetItemIconClass = getItemIconClass
            GetItemActions = getItemActions
            GetItemStatusAction = getItemStatusAction
            CanCreateItem = canCreateItem
            OnCreateItem = onCreateItem
            CanDeleteItem = canDeleteItem
            OnDeleteItem = onDeleteItem
        }

        // Handlers read the props of the latest committed render. The ref is updated after commit,
        // never during render, so a discarded render cannot leak into an event handler.
        let latest = React.useRef currentLatest
        React.useLayoutEffect (fun () -> latest.current <- currentLatest)

        let findRowItem (itemId: string) =
            latest.current.Rows
            |> Array.tryFind (fun row -> row.Id = itemId)
            |> Option.map _.Item

        let setExpandedById =
            React.useCallback (
                (fun (itemId: string) (willExpand: bool) ->
                    let current = latest.current
                    let isExpanded = current.ExpandedIds.Contains itemId

                    if isExpanded <> willExpand then
                        let next =
                            if willExpand then
                                current.ExpandedIds.Add itemId
                            else
                                current.ExpandedIds.Remove itemId

                        if not current.IsExpansionControlled then
                            dispatch (FileExplorerLogic.SetExpanded(itemId, willExpand))

                        current.OnExpandedIdsChange |> Option.iter (fun fn -> fn next)

                        match findRowItem itemId, current.OnDirectoryExpansionChange with
                        | Some item, Some fn -> fn item willExpand
                        | _ -> ()
                ),
                [||]
            )

        let selectItemById =
            React.useCallback (
                (fun (itemId: string) ->
                    match findRowItem itemId with
                    | Some item ->
                        if not latest.current.IsSelectionControlled then
                            dispatch (FileExplorerLogic.SelectItem itemId)

                        latest.current.OnItemClick |> Option.iter (fun fn -> fn item)
                    | None -> ()
                ),
                [||]
            )

        let onRowClick =
            React.useCallback (
                (fun (itemId: string) ->
                    match findRowItem itemId with
                    | Some item ->
                        if FileExplorerLogic.canExpandDirectory item then
                            setExpandedById itemId (not (latest.current.ExpandedIds.Contains itemId))

                        selectItemById itemId
                    | None -> ()
                ),
                [||]
            )

        let hasCreateHandler = onCreateItem.IsSome
        let hasDeleteHandler = onDeleteItem.IsSome

        let rowHandlers: TreeRowHandlers =
            React.useMemo (
                (fun () -> {
                    GetItemIconClass = fun item -> latest.current.GetItemIconClass item
                    GetItemActions = fun item -> latest.current.GetItemActions item
                    GetItemStatusAction = fun item -> latest.current.GetItemStatusAction item
                    CanCreateItem = fun item -> latest.current.CanCreateItem item
                    OnCreateItem =
                        if hasCreateHandler then
                            Some(fun item -> latest.current.OnCreateItem |> Option.iter (fun fn -> fn item))
                        else
                            None
                    CanDeleteItem = fun item -> latest.current.CanDeleteItem item
                    OnDeleteItem =
                        if hasDeleteHandler then
                            Some(fun item -> latest.current.OnDeleteItem |> Option.iter (fun fn -> fn item))
                        else
                            None
                    OnRowClick = onRowClick
                }),
                [|
                    box hasCreateHandler
                    box hasDeleteHandler
                    yield! rowFunctionIdentities
                |]
            )

        let virtualizer =
            Virtual.useVirtualizer (
                count = rows.Length,
                getScrollElement = (fun () -> scrollRef.current),
                estimateSize = (fun _ -> TreeRow.RowHeight),
                overscan = 12,
                getItemKey =
                    (fun index ->
                        if index < rows.Length then
                            rows.[index].Id
                        else
                            string index
                    )
            )

        // The first render has no measured items yet. Rendering the first rows then keeps the tree
        // visible before the scroll element reports its size.
        let virtualRanges =
            let measured = virtualizer.getVirtualItems ()

            if measured.Length = 0 && rows.Length > 0 then
                let isAtTop =
                    scrollRef.current
                    |> Option.map (fun element -> element.scrollTop = 0.0)
                    |> Option.defaultValue true

                if isAtTop then
                    [| 0 .. min (rows.Length - 1) 12 |]
                    |> Array.map (fun index -> index, index * TreeRow.RowHeight)
                else
                    [||]
            else
                measured |> Array.map (fun item -> item.index, item.start)

        // A focused row that leaves the virtual window unmounts, and the browser would drop the focus to the body.
        // The tree element takes the focus instead, so keyboard users stay inside the tree.
        React.useEffect (
            (fun () ->
                match focusedId with
                | Some id when
                    not (
                        virtualRanges
                        |> Array.exists (fun (index, _) -> index < rows.Length && rows.[index].Id = id)
                    )
                    ->
                    treeRef.current |> Option.iter (fun element -> element.focus ())
                    setFocusedId None
                | _ -> ()
            ),
            [| box virtualRanges; box focusedId |]
        )

        let spacerTop =
            if virtualRanges.Length = 0 then
                0
            else
                snd virtualRanges.[0]

        let spacerBottom =
            if virtualRanges.Length = 0 then
                0
            else
                let lastEnd = snd virtualRanges.[virtualRanges.Length - 1] + TreeRow.RowHeight
                max 0 (virtualizer.getTotalSize () - lastEnd)

        let selectedIndex =
            React.useMemo (
                (fun () ->
                    selectedPropId
                    |> Option.bind (fun id -> rows |> Array.tryFindIndex (fun row -> row.Id = id))
                    |> Option.defaultValue -1
                ),
                [| box rows; box selectedPropId |]
            )

        // A changed selectedItemId scrolls its row into view once the row is part of the visible rows.
        let lastScrolledId = React.useRef (None: string option)

        React.useEffect (
            (fun () ->
                match selectedPropId with
                | Some id when selectedIndex >= 0 && lastScrolledId.current <> Some id ->
                    lastScrolledId.current <- Some id
                    virtualizer.scrollToIndex selectedIndex
                | Some _ -> ()
                | None -> lastScrolledId.current <- None
            ),
            [| box selectedPropId; box selectedIndex |]
        )

        let scrollContainerClassName =
            if truncateOverflowingItemNames then
                "swt:w-full swt:min-w-0 swt:overflow-x-hidden"
            elif delegateHorizontalScrollToParent then
                "swt:w-max swt:min-w-full"
            else
                "swt:w-full swt:overflow-x-auto"

        let listClassName =
            if truncateOverflowingItemNames then
                "swt:w-full swt:min-w-0"
            else
                "swt:w-full swt:min-w-max"

        let menuItemsFor (item: FileItem) =
            FileExplorerHelper.getContextMenuItems
                item
                (expandedIds.Contains item.Id)
                (fun item -> selectItemById item.Id)
                onContextMenu
                getCopyPath
                getCopyRelativePath
                includeDefaultContextMenuItems
                (fun item willExpand -> setExpandedById item.Id willExpand)
                onRefreshDirectory

        let contextMenu =
            Swate.Components.Primitive.ContextMenu.ContextMenu.ContextMenu(
                (fun data ->
                    data
                    |> unbox<FileItem>
                    |> menuItemsFor
                    |> List.map (fun x -> x.ToPrimitiveContextMenuItem())
                ),
                ref = containerRef,
                onSpawn =
                    (fun e ->
                        let trigger =
                            e
                            |> FileExplorerHelper.tryGetEventTargetElement
                            |> Option.bind (fun target -> target.closest ("[data-file-item-id]"))

                        match trigger, containerRef.current with
                        | Some trigger, Some container when container.contains (trigger) ->
                            let trigger = trigger :?> Browser.Types.HTMLElement
                            let itemId: string = !!trigger?dataset?fileItemId

                            match findRowItem itemId with
                            | Some item ->
                                if List.isEmpty (menuItemsFor item) then
                                    None
                                else
                                    Some(box item)
                            | None -> None
                        | _ -> None
                    )
            )

        Html.div [
            prop.ref containerRef
            prop.className "swt:w-full swt:h-full swt:min-h-0"
            prop.children [
                Html.div [
                    prop.ref scrollRef
                    prop.testId "file-explorer-scroll-container"
                    prop.className [
                        scrollContainerClassName
                        "swt:h-full swt:min-h-0 swt:overflow-y-auto"
                    ]
                    prop.children [
                        Html.div [
                            prop.testId "file-explorer-container"
                            prop.ref treeRef
                            prop.role "tree"
                            prop.tabIndex -1
                            prop.ariaLabel ariaLabel

                            prop.onFocus (fun event ->
                                let target = event.target :?> Browser.Types.HTMLElement

                                match target.closest ("[data-file-item-id]") with
                                | Some row ->
                                    let itemId: string = !!(row :?> Browser.Types.HTMLElement)?dataset?fileItemId
                                    setFocusedId (Some itemId)
                                | None -> ()
                            )

                            prop.onBlur (fun event ->
                                let next = event.relatedTarget

                                let stillInside =
                                    not (isNull next)
                                    && (
                                        match containerRef.current with
                                        | Some container -> container.contains (next :?> Browser.Types.Node)
                                        | None -> false
                                    )

                                if not stillInside then
                                    setFocusedId None
                            )
                            prop.className listClassName
                            prop.style [
                                style.paddingTop spacerTop
                                style.paddingBottom spacerBottom
                            ]
                            prop.children [
                                for (index, _) in virtualRanges do
                                    if index < rows.Length then
                                        let row = rows.[index]

                                        React.memoRender (
                                            TreeRow.Render,
                                            {
                                                Row = row
                                                IsSelected = (selectedId = Some row.Id)
                                                IsHighlighted = highlightedIds.Contains row.Id
                                                IsExpanded = expandedIds.Contains row.Id
                                                Handlers = rowHandlers
                                                TruncateNames = truncateOverflowingItemNames
                                            },
                                            withKey = (fun props -> props.Row.Id)
                                        )
                            ]
                        ]
                    ]
                ]
                contextMenu
            ]
        ]




module FileExplorerExample =
    [<ReactComponent>]
    let Example () =
        let initialItems: FileItem list = [
            FileTree.createFile "resume.pdf" None FileItemIcon.Document
            {
                FileTree.createFolder "My Files" None FileItemIcon.Folder with
                    IsExpanded = false
                    IsLFS = Some true
                    Downloaded = Some false
                    SizeFormatted = Some "2 KB"
                    Children =
                        Some [
                            FileTree.createFile "Project-final.psd" None FileItemIcon.Document
                            |> fun file -> {
                                file with
                                    IsLFS = Some true
                                    Downloaded = Some true
                                    SizeFormatted = Some "6 MB"
                            }
                            {
                                FileTree.createFolder "Subfolder" None FileItemIcon.Folder with
                                    IsExpanded = false
                                    Children =
                                        Some [
                                            FileTree.createFile "nested-file-1.txt" None FileItemIcon.Document
                                            FileTree.createFile "nested-file-2.md" None FileItemIcon.Document
                                            {
                                                FileTree.createFolder "NestedFolder" None FileItemIcon.Folder with
                                                    IsExpanded = false
                                                    Children =
                                                        Some [
                                                            FileTree.createFile
                                                                "Project-2-final.psd"
                                                                None
                                                                FileItemIcon.Document
                                                            FileTree.createFile
                                                                "Project-3-final.psd"
                                                                None
                                                                FileItemIcon.Document
                                                        ]
                                            }
                                        ]
                            }
                        ]
            }
            {
                FileTree.createFolder "Empty Folder" None FileItemIcon.Folder with
                    IsExpanded = false
                    Children = Some []
            }
            FileTree.createFile "notes.txt" None FileItemIcon.Document
        ]

        let handleItemClick (item: FileItem) =
            Browser.Dom.console.log ("Clicked:", item.Name)

        let handleContextMenu (item: FileItem) = [
            ContextMenuItem.create
                "Rename"
                "swt:fluent--rename-24-regular"
                (fun () -> Browser.Dom.console.log ("Rename", item.Name))
            ContextMenuItem.create
                "Delete"
                "swt:fluent--delete-24-regular"
                (fun () -> Browser.Dom.console.log ("Delete", item.Name))
        ]

        Html.div [
            prop.className "swt:p-4"
            prop.children [
                Html.h2 [
                    prop.className "swt:text-2xl swt:font-bold swt:mb-4"
                    prop.text "File Explorer Demo"
                ]
                FileExplorer.FileExplorer(
                    initialItems = initialItems,
                    onItemClick = handleItemClick,
                    onContextMenu = handleContextMenu
                )
            ]
        ]
