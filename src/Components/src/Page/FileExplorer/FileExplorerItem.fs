namespace Swate.Components.Page.FileExplorer

open Fable.Core
open Feliz
open Swate.Components.Page.FileExplorer.Types

[<Mangle(false); Erase>]
type FileExplorerItem =

    [<ReactComponent>]
    static member private RowActionButton
        (label: string, icon: string, onClick: unit -> unit, ?className: string, ?disabled: bool, ?buttonKey: string)
        =
        let disabled = defaultArg disabled false

        Html.button [
            match buttonKey with
            | Some key -> prop.key key
            | None -> ()

            prop.type'.button
            prop.className [
                "swt:btn swt:btn-ghost swt:btn-square swt:btn-xs swt:shrink-0 swt:opacity-0 swt:transition-opacity swt:group-hover:opacity-100 swt:focus:opacity-100"

                match className with
                | Some className -> className
                | None -> ()

                if disabled then
                    "swt:opacity-50"
            ]
            prop.disabled disabled
            prop.ariaLabel label
            prop.title label
            prop.onClick (fun ev ->
                ev.preventDefault ()
                ev.stopPropagation ()

                if not disabled then
                    onClick ()
            )
            prop.children [
                Html.i [ prop.className $"swt:iconify {icon} swt:size-4" ]
            ]
        ]

    [<ReactComponent>]
    static member private ItemActionButton(item: FileItem, action: ContextMenuItem) =
        let label = $"{action.Label} {item.Name}"

        FileExplorerItem.RowActionButton(
            label,
            action.Icon,
            action.OnClick,
            ?className = action.ClassName,
            ?disabled = action.Disabled,
            buttonKey = $"{item.Id}-{action.Label}"
        )

    [<ReactComponent>]
    static member private StatusActionButton(item: FileItem, action: ContextMenuItem) =
        let label = $"{action.Label} {item.Name}"

        Html.button [
            prop.type'.button
            prop.className [
                "swt:badge swt:badge-sm swt:gap-1 swt:border swt:border-base-300 swt:bg-base-100 swt:text-base-content swt:transition-colors"

                match action.Disabled with
                | Some true -> "swt:cursor-not-allowed swt:opacity-70"
                | _ ->
                    "swt:cursor-pointer swt:hover:border-primary swt:focus-visible:outline swt:focus-visible:outline-2 swt:focus-visible:outline-offset-2 swt:focus-visible:outline-primary"
            ]
            prop.disabled (defaultArg action.Disabled false)
            prop.ariaLabel label
            prop.title label
            prop.onClick (fun ev ->
                ev.preventDefault ()
                ev.stopPropagation ()

                if action.Disabled <> Some true then
                    action.OnClick()
            )
            prop.children [
                Html.i [ prop.className $"swt:iconify {action.Icon} swt:size-3" ]
                Html.span [ prop.text action.Label ]
            ]
        ]

    [<ReactComponent>]
    static member private CreateItemButton
        (item: FileItem, onCreateItem: (FileItem -> unit) option, canCreateItem: FileItem -> bool)
        =
        if item.IsDirectory && canCreateItem item && onCreateItem.IsSome then
            FileExplorerItem.RowActionButton(
                $"Create new item in {item.Name}",
                "swt:fluent--add-24-regular",
                (fun () -> onCreateItem |> Option.iter (fun fn -> fn item))
            )
        else
            Html.none

    [<ReactComponent>]
    static member private DeleteItemButton
        (item: FileItem, onDeleteItem: (FileItem -> unit) option, canDeleteItem: FileItem -> bool)
        =
        if canDeleteItem item && onDeleteItem.IsSome then
            FileExplorerItem.RowActionButton(
                $"Delete {item.Name}",
                "swt:fluent--delete-24-regular",
                (fun () -> onDeleteItem |> Option.iter (fun fn -> fn item)),
                className = "swt:text-error"
            )
        else
            Html.none

    [<ReactComponent>]
    static member private LFSStatusPill(item: FileItem, ?statusAction: ContextMenuItem) =
        let isPointer = item.IsLFSPointer = Some true
        let isDownloaded = Helper.hasLocalLfsCopy item

        let currentStatusText =
            if isDownloaded then "LFS Downloaded"
            elif isPointer then "LFS Pointer"
            else "LFS Not Downloaded"

        let statusText = defaultArg item.LfsActivity currentStatusText

        let statusAccessibilityText =
            item.SizeFormatted
            |> Option.map (fun size -> $"{statusText} - {size}")
            |> Option.defaultValue statusText

        let isActionDisabled =
            item.LfsActivity.IsSome
            || (statusAction
                |> Option.bind (fun action -> action.Disabled)
                |> Option.defaultValue false)

        let pillAccessibilityText =
            match statusAction with
            | Some action -> $"{action.Label} {item.Name}. {statusAccessibilityText}"
            | None -> statusAccessibilityText

        let statusClassNames =
            if isDownloaded then
                [ "swt:badge-success" ]
            else
                [ "swt:bg-info"; "swt:text-info-content" ]

        let statusIconClassName =
            if isDownloaded then
                "swt:fluent--checkmark-circle-24-regular"
            else
                "swt:fluent--cloud-arrow-down-24-regular"

        // The activity label is wider than the idle label. Dropping the size segment while an action
        // runs keeps the pill from taking the row space of the file name. The size stays in the title.
        let visibleSize = if item.LfsActivity.IsSome then None else item.SizeFormatted

        let statusShapeClasses =
            if visibleSize.IsSome then
                [ "swt:rounded-none"; "swt:border-0" ]
            else
                [ "swt:rounded-full" ]

        let segmentCursorClass =
            match statusAction, isActionDisabled with
            | Some _, true -> "swt:cursor-not-allowed"
            | Some _, false -> "swt:cursor-pointer"
            | None, _ -> "swt:cursor-default"

        let statusBadge =
            Html.span [
                prop.className [
                    "swt:badge"
                    "swt:badge-sm"
                    "swt:gap-0.5"
                    segmentCursorClass
                    yield! statusClassNames
                    yield! statusShapeClasses
                ]
                prop.custom (
                    "data-lfs-download-status",
                    if isDownloaded then "downloaded"
                    elif isPointer then "pointer"
                    else "not-downloaded"
                )
                prop.children [
                    if item.LfsActivity.IsSome then
                        Html.span [
                            prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                            prop.ariaHidden true
                        ]
                    else
                        Html.i [
                            prop.className $"swt:iconify {statusIconClassName} swt:size-3"
                        ]

                    Html.span [
                        // Below this container width only the icon stays. The sr-only span below keeps the status readable.
                        prop.ariaHidden true
                        prop.className "swt:@max-[12rem]:hidden"
                        prop.text (defaultArg item.LfsActivity "LFS")
                    ]

                    // Screen readers skip aria-label on a span and the label above is display:none in narrow rows.
                    Html.span [
                        prop.className "swt:sr-only"
                        prop.text statusAccessibilityText
                    ]
                ]
            ]

        let badgeSegments = [
            statusBadge

            match visibleSize with
            | Some size ->
                Html.span [
                    // The sr-only status text already includes the size.
                    prop.ariaHidden true
                    prop.className [
                        "swt:badge"
                        "swt:badge-sm"
                        "swt:@max-[16rem]:hidden"
                        "swt:rounded-none"
                        "swt:border-0"
                        segmentCursorClass
                        "swt:bg-base-200"
                        "swt:text-base-content"
                    ]
                    prop.text size
                ]
            | None -> ()
        ]

        let commonProps (className: string list) = [
            prop.className className
            prop.ariaLabel pillAccessibilityText
            prop.title pillAccessibilityText
            prop.children badgeSegments
        ]

        let stopPillClick (e: Browser.Types.MouseEvent) =
            e.preventDefault ()
            e.stopPropagation ()

        match statusAction with
        | Some action ->
            Html.button [
                prop.type'.button
                prop.disabled isActionDisabled
                yield!
                    commonProps [
                        "swt:inline-flex swt:min-w-0 swt:m-0 swt:overflow-hidden swt:rounded-full swt:border swt:border-base-300 swt:bg-transparent swt:p-0 swt:align-middle swt:transition-colors"

                        if isActionDisabled then
                            "swt:cursor-not-allowed swt:opacity-70"
                        else
                            "swt:cursor-pointer swt:hover:border-primary swt:focus-visible:outline swt:focus-visible:outline-2 swt:focus-visible:outline-offset-2 swt:focus-visible:outline-primary"
                    ]
                prop.onClick (fun e ->
                    stopPillClick e

                    if not isActionDisabled then
                        action.OnClick()
                )
            ]
        | None ->
            Html.span [
                yield!
                    commonProps [
                        "swt:inline-flex swt:min-w-0 swt:overflow-hidden swt:rounded-full swt:border swt:border-base-300"
                    ]
                prop.onClick stopPillClick
            ]

    [<ReactComponent>]
    static member private StatusControl(item: FileItem, statusAction: ContextMenuItem option) =
        if Helper.isLfs item then
            FileExplorerItem.LFSStatusPill(item, ?statusAction = statusAction)
        else
            statusAction
            |> Option.map (fun action -> FileExplorerItem.StatusActionButton(item, action))
            |> Option.defaultValue Html.none

    [<ReactComponent>]
    static member DirectoryRow
        (
            item: FileItem,
            rowHighlightClass: string,
            selectedNameClass: string,
            isExpanded: bool,
            canExpandDirectory: bool,
            getItemIconClass: FileItem -> string option,
            onDirectorySelect: Browser.Types.MouseEvent -> unit,
            ?onCreateItem: FileItem -> unit,
            ?canCreateItem: FileItem -> bool,
            ?itemActions: ContextMenuItem list,
            ?onDeleteItem: FileItem -> unit,
            ?canDeleteItem: FileItem -> bool,
            ?statusAction: ContextMenuItem,
            ?truncateNames: bool
        ) =
        let truncateNames = defaultArg truncateNames false
        let canCreateItem = defaultArg canCreateItem (fun (_: FileItem) -> false)
        let itemActions = defaultArg itemActions []
        let canDeleteItem = defaultArg canDeleteItem (fun (_: FileItem) -> false)
        let canCreateFromDirectory = canCreateItem item && onCreateItem.IsSome
        let hasItemActions = itemActions |> List.isEmpty |> not
        let canDeleteFromDirectory = canDeleteItem item && onDeleteItem.IsSome
        let hasStatusControl = Helper.isLfs item || statusAction.IsSome

        let directoryToggleIconClass =
            if isExpanded then
                "swt:iconify swt:fluent--caret-down-24-filled swt:size-4 swt:shrink-0"
            else
                "swt:iconify swt:fluent--caret-right-24-filled swt:size-4 swt:shrink-0"

        Html.div [
            prop.custom ("data-file-item-id", item.Id)
            prop.className [
                "swt:group swt:w-full swt:px-2 swt:py-1 swt:cursor-default"

                if truncateNames then
                    "swt:@container"

                rowHighlightClass
            ]
            prop.style [ style.display.flex; style.width (length.percent 100) ]
            prop.onClick onDirectorySelect

            prop.children [
                Html.div [
                    prop.className "swt:flex swt:w-full swt:min-w-0 swt:items-center swt:gap-2"
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.className [
                                "swt:flex swt:flex-1 swt:items-center swt:gap-2 swt:bg-transparent swt:border-0 swt:p-0 swt:text-left swt:cursor-default"
                                if truncateNames then "swt:min-w-24" else "swt:min-w-0"
                            ]
                            prop.onClick onDirectorySelect

                            prop.children [
                                Html.i [
                                    prop.className (
                                        Swate.Components.Page.FileExplorer.Helper.iconClassName
                                            [ "swt:iconify"; "swt:shrink-0" ]
                                            item
                                            getItemIconClass
                                    )
                                ]
                                Html.span [
                                    prop.className [
                                        "swt:min-w-0"
                                        "swt:flex-1"
                                        "swt:truncate"
                                        selectedNameClass
                                    ]
                                    prop.text item.Name
                                ]
                            ]
                        ]

                        if
                            hasStatusControl
                            || canExpandDirectory
                            || canCreateFromDirectory
                            || hasItemActions
                            || canDeleteFromDirectory
                        then
                            Html.div [
                                prop.className [
                                    "swt:ml-auto swt:flex swt:items-center swt:gap-2"
                                    if truncateNames then
                                        "swt:shrink swt:min-w-0"
                                    else
                                        "swt:shrink-0"
                                ]
                                prop.children [
                                    FileExplorerItem.CreateItemButton(item, onCreateItem, canCreateItem)

                                    yield!
                                        itemActions
                                        |> List.map (fun action -> FileExplorerItem.ItemActionButton(item, action))

                                    FileExplorerItem.DeleteItemButton(item, onDeleteItem, canDeleteItem)

                                    if hasStatusControl then
                                        FileExplorerItem.StatusControl(item, statusAction)

                                    // Shows the state only. A click lands on the row, which toggles the folder.
                                    if canExpandDirectory then
                                        Html.i [ prop.className directoryToggleIconClass ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member FileRow
        (
            item: FileItem,
            rowHighlightClass: string,
            selectedNameClass: string,
            getItemIconClass: FileItem -> string option,
            onSelect: unit -> unit,
            ?itemActions: ContextMenuItem list,
            ?onDeleteItem: FileItem -> unit,
            ?canDeleteItem: FileItem -> bool,
            ?statusAction: ContextMenuItem,
            ?truncateNames: bool
        ) =
        let truncateNames = defaultArg truncateNames false
        let itemActions = defaultArg itemActions []
        let canDeleteItem = defaultArg canDeleteItem (fun (_: FileItem) -> false)
        let hasItemActions = itemActions |> List.isEmpty |> not
        let canDeleteFromFile = canDeleteItem item && onDeleteItem.IsSome
        let hasStatusControl = Helper.isLfs item || statusAction.IsSome

        Html.div [
            prop.custom ("data-file-item-id", item.Id)
            prop.className [
                "swt:group swt:px-2 swt:py-1 swt:flex swt:min-w-0 swt:items-center swt:justify-between swt:cursor-default"
                if truncateNames then
                    "swt:@container"
                rowHighlightClass
            ]
            prop.children [
                Html.button [
                    prop.type'.button
                    prop.custom ("data-file-item-id", item.Id)
                    prop.className [
                        "swt:flex swt:flex-1 swt:items-center swt:gap-2 swt:bg-transparent swt:border-0 swt:p-0 swt:text-left swt:cursor-default"
                        if truncateNames then "swt:min-w-24" else "swt:min-w-0"
                    ]
                    prop.onClick (fun _ -> onSelect ())
                    prop.children [
                        Html.i [
                            prop.className (
                                Swate.Components.Page.FileExplorer.Helper.iconClassName
                                    [ "swt:iconify"; "swt:shrink-0" ]
                                    item
                                    getItemIconClass
                            )
                        ]
                        Html.span [
                            prop.className [
                                "swt:min-w-0"
                                "swt:flex-1"
                                "swt:truncate"
                                selectedNameClass
                            ]
                            prop.text item.Name
                        ]
                    ]
                ]

                if hasStatusControl || hasItemActions || canDeleteFromFile then
                    Html.div [
                        prop.className [
                            "swt:ml-auto swt:flex swt:items-center swt:gap-2"
                            if truncateNames then
                                "swt:shrink swt:min-w-0"
                            else
                                "swt:shrink-0"
                        ]
                        prop.children [
                            yield!
                                itemActions
                                |> List.map (fun action -> FileExplorerItem.ItemActionButton(item, action))

                            FileExplorerItem.DeleteItemButton(item, onDeleteItem, canDeleteItem)

                            if hasStatusControl then
                                FileExplorerItem.StatusControl(item, statusAction)
                        ]
                    ]
            ]
        ]

module TreeRow =

    /// Compares every field of the item except the children. The row only needs to know whether
    /// the directory can expand, which the comparison checks separately.
    let private sameItem (a: FileItem) (b: FileItem) =
        obj.ReferenceEquals(a, b)
        || ({ a with Children = None } = { b with Children = None })

    let areEqual (a: TreeRowProps) (b: TreeRowProps) =
        sameItem a.Row.Item b.Row.Item
        && FileExplorerLogic.canExpandDirectory a.Row.Item = FileExplorerLogic.canExpandDirectory b.Row.Item
        && a.Row.Depth = b.Row.Depth
        && a.Row.SetSize = b.Row.SetSize
        && a.Row.PosInSet = b.Row.PosInSet
        && a.IsSelected = b.IsSelected
        && a.IsHighlighted = b.IsHighlighted
        && a.IsExpanded = b.IsExpanded
        && a.TruncateNames = b.TruncateNames
        && obj.ReferenceEquals(a.Handlers, b.Handlers)

    /// Height of one row in pixels. The virtualizer uses the same number as its size estimate.
    [<Literal>]
    let RowHeight = 32

    /// Pixels of indentation per tree level.
    [<Literal>]
    let IndentPerLevel = 16

    // A row skips its render when only the children of its item changed, and it compares the rest of the item by
    // value. A [<ReactMemoComponent>] static member compares props by reference, so it cannot express this areEqual.
    let Render =
        React.memo (
            (fun (props: TreeRowProps) ->
                let item = props.Row.Item
                let handlers = props.Handlers
                let canExpand = FileExplorerLogic.canExpandDirectory item

                let rowHighlightClass =
                    if props.IsHighlighted then
                        "swt:bg-base-300 swt:active:bg-base-300"
                    else
                        "swt:hover:bg-base-300 swt:active:bg-base-300"

                let selectedNameClass =
                    if props.IsSelected then
                        "swt:font-semibold swt:text-primary"
                    else
                        ""

                let itemActions = handlers.GetItemActions item
                let statusAction = handlers.GetItemStatusAction item

                let content =
                    if item.IsDirectory then
                        FileExplorerItem.DirectoryRow(
                            item,
                            rowHighlightClass,
                            selectedNameClass,
                            props.IsExpanded,
                            canExpand,
                            handlers.GetItemIconClass,
                            (fun ev ->
                                ev.preventDefault ()
                                ev.stopPropagation ()
                                handlers.OnRowClick item.Id
                            ),
                            ?onCreateItem = handlers.OnCreateItem,
                            canCreateItem = handlers.CanCreateItem,
                            itemActions = itemActions,
                            ?onDeleteItem = handlers.OnDeleteItem,
                            canDeleteItem = handlers.CanDeleteItem,
                            ?statusAction = statusAction,
                            truncateNames = props.TruncateNames
                        )
                    else
                        FileExplorerItem.FileRow(
                            item,
                            rowHighlightClass,
                            selectedNameClass,
                            handlers.GetItemIconClass,
                            (fun () -> handlers.OnRowClick item.Id),
                            itemActions = itemActions,
                            ?onDeleteItem = handlers.OnDeleteItem,
                            canDeleteItem = handlers.CanDeleteItem,
                            ?statusAction = statusAction,
                            truncateNames = props.TruncateNames
                        )

                Html.div [
                    prop.role "treeitem"
                    prop.custom ("data-file-item-id", item.Id)
                    prop.ariaLevel (props.Row.Depth + 1)
                    prop.ariaSetSize props.Row.SetSize
                    prop.ariaPosInSet props.Row.PosInSet
                    prop.ariaSelected props.IsSelected

                    if canExpand then
                        prop.ariaExpanded props.IsExpanded

                    prop.className "swt:w-full"

                    prop.style [
                        style.height RowHeight
                        style.paddingLeft (props.Row.Depth * IndentPerLevel)
                    ]

                    prop.children [ content ]
                ]
            ),
            areEqual = areEqual
        )
