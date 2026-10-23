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
                "swt:btn swt:btn-ghost swt:btn-square swt:btn-xs swt:opacity-0 swt:transition-opacity swt:group-hover:opacity-100 swt:focus:opacity-100"

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
                        // Below this container width only the icon stays. The aria-label of the pill keeps the status.
                        prop.className "swt:@max-[12rem]:hidden"
                        prop.text (defaultArg item.LfsActivity "LFS")
                    ]
                ]
            ]

        let badgeSegments = [
            statusBadge

            match visibleSize with
            | Some size ->
                Html.span [
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
                        "swt:inline-flex swt:m-0 swt:overflow-hidden swt:rounded-full swt:border swt:border-base-300 swt:bg-transparent swt:p-0 swt:align-middle swt:transition-colors"

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
                        "swt:inline-flex swt:overflow-hidden swt:rounded-full swt:border swt:border-base-300"
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
            ?children: ReactElement
        ) =
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

        let rowChildren = [
            Html.div [
                prop.custom ("data-file-item-id", item.Id)
                prop.className [
                    "swt:group swt:@container swt:w-full swt:px-2 swt:py-1 swt:cursor-default"
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
                                prop.className
                                    "swt:flex swt:min-w-16 swt:flex-1 swt:items-center swt:gap-2 swt:bg-transparent swt:border-0 swt:p-0 swt:text-left swt:cursor-default"
                                prop.onClick onDirectorySelect

                                if canExpandDirectory then
                                    prop.ariaExpanded isExpanded

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
                                    prop.className
                                        "swt:ml-auto swt:shrink swt:min-w-0 swt:flex swt:items-center swt:gap-2"
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

            match children with
            | Some childTree -> childTree
            | None -> Html.none
        ]

        Html.li [
            prop.key item.Id
            prop.custom ("data-file-item-id", item.Id)
            prop.className "swt:w-full"
            prop.children rowChildren
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
            ?statusAction: ContextMenuItem
        ) =
        let itemActions = defaultArg itemActions []
        let canDeleteItem = defaultArg canDeleteItem (fun (_: FileItem) -> false)
        let hasItemActions = itemActions |> List.isEmpty |> not
        let canDeleteFromFile = canDeleteItem item && onDeleteItem.IsSome
        let hasStatusControl = Helper.isLfs item || statusAction.IsSome

        Html.li [
            prop.key item.Id
            prop.custom ("data-file-item-id", item.Id)
            prop.children [
                Html.div [
                    prop.custom ("data-file-item-id", item.Id)
                    prop.className [
                        "swt:group swt:@container swt:px-2 swt:py-1 swt:flex swt:min-w-0 swt:items-center swt:justify-between swt:cursor-default"
                        rowHighlightClass
                    ]
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.custom ("data-file-item-id", item.Id)
                            prop.className
                                "swt:flex swt:min-w-16 swt:flex-1 swt:items-center swt:gap-2 swt:bg-transparent swt:border-0 swt:p-0 swt:text-left swt:cursor-default"
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
                                prop.className "swt:ml-auto swt:flex swt:shrink swt:min-w-0 swt:items-center swt:gap-2"
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
            ]
        ]
