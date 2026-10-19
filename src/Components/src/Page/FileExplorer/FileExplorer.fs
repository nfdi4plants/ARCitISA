namespace Swate.Components.Page.FileExplorer

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
        : Swate.Components.Page.FileExplorer.Types.ContextMenuItem list =
        let canExpandDirectory =
            match item.Children with
            | Some children -> not (List.isEmpty children)
            | None -> true

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

            if item.IsDirectory && canExpandDirectory then
                ContextMenuItem.create
                    (if isExpanded then "Collapse" else "Expand")
                    (if isExpanded then
                         "swt:fluent--folder-open-24-regular"
                     else
                         "swt:fluent--folder-24-regular")
                    (fun () -> setExpanded item (not isExpanded))
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
        =
        let defaultItems =
            if includeDefaultContextMenuItems then
                defaultContextMenuItems item isExpanded selectItem getCopyPath getCopyRelativePath setExpanded
            else
                []

        let customItems =
            onContextMenu |> Option.map (fun fn -> fn item) |> Option.defaultValue []

        defaultItems @ customItems

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
            ?includeDefaultContextMenuItems: bool
        ) =
        let reducer model msg = FileExplorerLogic.update msg model

        let initialModel = FileExplorerLogic.init (defaultArg initialItems [])

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

        let model, dispatch = React.useReducer (reducer, initialModel)
        let containerRef = React.useElementRef ()

        let onDirectoryExpansionChange =
            onDirectoryExpansionChange |> Option.orElse onExpansionChange

        let scrollContainerClassName =
            if truncateOverflowingItemNames then
                "swt:w-full swt:min-w-0 swt:overflow-x-hidden"
            elif delegateHorizontalScrollToParent then
                "swt:w-max swt:min-w-full"
            else
                "swt:w-full swt:overflow-x-auto"

        let listClassName =
            if truncateOverflowingItemNames then
                "swt:w-full swt:min-w-0 swt:list-none swt:m-0 swt:p-0"
            else
                "swt:w-full swt:min-w-max swt:list-none swt:m-0 swt:p-0"

        React.useEffect (
            (fun () -> dispatch (FileExplorerLogic.UpdateItems(defaultArg initialItems [], selectedItemId))),
            [| box initialItems; box selectedItemId |]
        )

        let setExpanded (item: FileItem) (willExpand: bool) =
            let isExpanded = model.ExpandedIds.Contains item.Id

            if isExpanded <> willExpand then
                dispatch (FileExplorerLogic.SetExpanded(item.Id, willExpand))
                onDirectoryExpansionChange |> Option.iter (fun fn -> fn item willExpand)

        let selectItem (item: FileItem) =
            if selectedItemId.IsNone then
                dispatch (FileExplorerLogic.SelectItem item.Id)

            onItemClick |> Option.iter (fun fn -> fn item)

        let handleDirectorySelection (item: FileItem) (canExpand: bool) (ev: Browser.Types.MouseEvent) =
            ev.preventDefault ()
            ev.stopPropagation ()

            if canExpand then
                setExpanded item (not (model.ExpandedIds.Contains item.Id))

            selectItem item

        let contextMenu =
            Swate.Components.Primitive.ContextMenu.ContextMenu.ContextMenu(
                (fun data ->
                    let item = data |> unbox<FileItem>
                    let isExpanded = model.ExpandedIds.Contains item.Id

                    FileExplorerHelper.getContextMenuItems
                        item
                        isExpanded
                        selectItem
                        onContextMenu
                        getCopyPath
                        getCopyRelativePath
                        includeDefaultContextMenuItems
                        setExpanded
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

                            match FileTree.findItem itemId model.Items with
                            | Some item ->
                                let menuItems =
                                    FileExplorerHelper.getContextMenuItems
                                        item
                                        (model.ExpandedIds.Contains item.Id)
                                        selectItem
                                        onContextMenu
                                        getCopyPath
                                        getCopyRelativePath
                                        includeDefaultContextMenuItems
                                        setExpanded

                                if List.isEmpty menuItems then None else Some(box item)
                            | None -> None
                        | _ -> None
                    )
            )

        let selectedPathIds = model.SelectedPath |> List.map _.Id |> Set.ofList

        let rec renderItem item =
            let isSelected = model.SelectedId = Some item.Id
            let isInSelectedPath = selectedPathIds.Contains item.Id
            let isHighlighted = isSelected || isInSelectedPath

            let rowHighlightClass =
                if isHighlighted then
                    "swt:bg-base-300 swt:active:bg-base-300"
                else
                    "swt:hover:bg-base-300 swt:active:bg-base-300"

            let selectedNameClass =
                if isSelected then
                    "swt:font-semibold swt:text-primary"
                else
                    ""

            let isExpanded = model.ExpandedIds.Contains item.Id

            let canExpand =
                match item.Children with
                | Some children -> not (List.isEmpty children)
                | None -> true

            let itemActions = getItemActions item
            let statusAction = getItemStatusAction item

            if item.IsDirectory then
                let childrenTree =
                    if isExpanded then
                        match item.Children with
                        | Some children ->
                            Some(
                                Html.ul [
                                    prop.className "swt:ml-4"
                                    prop.children (children |> List.map renderItem)
                                ]
                            )
                        | None -> None
                    else
                        None

                FileExplorerItem.DirectoryRow(
                    item,
                    rowHighlightClass,
                    selectedNameClass,
                    isExpanded,
                    canExpand,
                    getItemIconClass,
                    handleDirectorySelection item canExpand,
                    ?onCreateItem = onCreateItem,
                    canCreateItem = canCreateItem,
                    itemActions = itemActions,
                    ?onDeleteItem = onDeleteItem,
                    canDeleteItem = canDeleteItem,
                    ?statusAction = statusAction,
                    ?children = childrenTree
                )
            else
                FileExplorerItem.FileRow(
                    item,
                    rowHighlightClass,
                    selectedNameClass,
                    getItemIconClass,
                    (fun () -> selectItem item),
                    itemActions = itemActions,
                    ?onDeleteItem = onDeleteItem,
                    canDeleteItem = canDeleteItem,
                    ?statusAction = statusAction
                )

        Html.div [
            prop.ref containerRef
            prop.className "swt:w-full"
            prop.children [
                Html.div [
                    prop.testId "file-explorer-scroll-container"
                    prop.className scrollContainerClassName
                    prop.children [
                        Html.ul [
                            prop.testId "file-explorer-container"
                            prop.className listClassName
                            prop.children (model.Items |> List.map renderItem)
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
