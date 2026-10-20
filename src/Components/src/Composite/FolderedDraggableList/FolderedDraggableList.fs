namespace Swate.Components.Composite.FolderedDraggableList

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.JsBindings
open Swate.Components.Primitive.Popover
open Swate.Components.Composite.FolderedDraggableList.Types

module private FolderedDraggableListHelper =

    let fallbackColor = "#2563eb"

    let currentOrFallback color =
        match color with
        | Some c when c <> "" -> c
        | _ -> fallbackColor

    let effectiveColor (folder: FolderedDraggableFolder<'payload>) (item: FolderedDraggableItem<'payload>) =
        item.Color |> Option.orElse folder.Color

    let appendItemToFolder targetFolderId item folders =
        folders
        |> List.map (fun folder ->
            if folder.Id = targetFolderId then
                {
                    folder with
                        Items = folder.Items @ [ item ]
                }
            else
                folder
        )

    let matchesSearch (query: string) (item: FolderedDraggableItem<'payload>) =
        let query = query.Trim()

        query = "" || item.Label.ToLowerInvariant().Contains(query.ToLowerInvariant())

    let canAcceptExternalDrop
        (activeFolder: FolderedDraggableFolder<'payload> option)
        (tryCreateItemFromExternalDrop: FolderedDraggableExternalDropHandler<'payload> option)
        (onFoldersChange: (FolderedDraggableFolder<'payload> list -> unit) option)
        =
        activeFolder.IsSome
        && tryCreateItemFromExternalDrop.IsSome
        && onFoldersChange.IsSome

    let isShelfDrop shelfDropId (event: DndKit.IDndKitEvent) =
        not (isNull event.over) && string event.over.id = shelfDropId

    let activeId (event: DndKit.IDndKitEvent) =
        if isNull event.active then "" else string event.active.id

    let activeData (event: DndKit.IDndKitEvent) =
        if isNull event.active || isNull event.active.data then
            null
        else
            event.active.data.current

    let colorSwatch color =
        match color with
        | Some color when color <> "" ->
            Html.span [
                prop.className "swt:size-2.5 swt:shrink-0 swt:rounded-full swt:border swt:border-base-300"
                prop.custom ("data-foldered-color-swatch", "true")
                prop.style [ style.backgroundColor color ]
            ]
        | _ -> Html.none

    let colorPickerContent ariaLabel (draftColor: string) (setDraftColor: string -> unit) onSetColor =
        Html.div [
            prop.className "swt:flex swt:items-center swt:gap-2 swt:p-2"
            prop.children [
                Html.input [
                    prop.custom ("type", "color")
                    prop.className "swt:h-8 swt:w-10 swt:cursor-pointer swt:rounded swt:border swt:border-base-300"
                    prop.value draftColor
                    prop.ariaLabel ariaLabel
                    prop.onChange (fun (color: string) -> setDraftColor color)
                ]
                Html.button [
                    prop.type'.button
                    prop.className "swt:btn swt:btn-xs swt:btn-primary swt:min-h-0 swt:py-0"
                    prop.text "Select"
                    prop.onClick (fun _ -> onSetColor (Some draftColor))
                ]
                Html.button [
                    prop.type'.button
                    prop.className "swt:btn swt:btn-xs swt:btn-ghost swt:min-h-0 swt:py-0"
                    prop.text "Clear"
                    prop.onClick (fun _ ->
                        setDraftColor fallbackColor
                        onSetColor None
                    )
                ]
            ]
        ]

    let defaultItemContent (render: FolderedDraggableItemRender<'payload>) =
        React.Fragment [
            colorSwatch render.DragData.EffectiveColor
            Html.span [
                prop.className "swt:min-w-0 swt:truncate swt:text-left"
                prop.text render.Item.Label
            ]
            match render.Item.Badge with
            | Some badge when badge <> "" ->
                Html.span [
                    prop.className "swt:badge swt:badge-sm swt:shrink-0"
                    prop.text badge
                ]
            | _ -> Html.none
        ]

    let itemShellClass isDisabled isDragging = [
        "swt:btn swt:btn-sm swt:h-auto swt:min-h-10 swt:w-fit swt:max-w-56 swt:shrink-0 swt:min-w-0 swt:justify-start swt:gap-2 swt:overflow-hidden swt:px-3 swt:py-2 swt:text-sm swt:normal-case"
        if isDisabled then
            "swt:btn-disabled swt:cursor-not-allowed"
        else
            "swt:btn-outline swt:bg-base-100 swt:cursor-grab swt:shadow-sm active:swt:cursor-grabbing"
        if isDragging then
            "swt:pointer-events-none swt:opacity-0"
    ]

[<Erase; Mangle(false)>]
type FolderedDraggableList =

    [<ReactComponent>]
    static member private Item<'payload>
        (
            folder: FolderedDraggableFolder<'payload>,
            item: FolderedDraggableItem<'payload>,
            dragId: FolderedDraggableFolder<'payload> -> FolderedDraggableItem<'payload> -> string,
            renderItemContent: FolderedDraggableItemRenderFn<'payload>,
            onActiveDragChange: FolderedDraggableItemRender<'payload> option -> unit,
            ?debug: bool,
            ?key: string
        ) =
        let dragData: FolderedDraggableData<'payload> = {
            FolderId = folder.Id
            FolderName = folder.Name
            FolderColor = folder.Color
            ItemId = item.Id
            ItemLabel = item.Label
            ItemColor = item.Color
            EffectiveColor = FolderedDraggableListHelper.effectiveColor folder item
            Payload = item.Payload
        }

        let draggable =
            DndKit.useDraggable (
                {|
                    id = dragId folder item
                    data = dragData
                    disabled = item.Disabled
                |}
            )

        let renderProps: FolderedDraggableItemRender<'payload> = {
            Folder = folder
            Item = item
            DragData = dragData
            IsDragging = draggable.isDragging
        }

        React.useEffect (
            (fun () ->
                if draggable.isDragging then
                    onActiveDragChange (Some renderProps)
                else
                    onActiveDragChange None
            ),
            [| box draggable.isDragging |]
        )

        let identityProps = [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.type'.button
            prop.disabled item.Disabled
        ]

        let dragProps = [
            if not item.Disabled then
                prop.ref draggable.setNodeRef
                yield! prop.spread (!!draggable.attributes)
                yield! prop.spread (!!draggable.listeners)
        ]

        let appearanceProps = [
            prop.className [
                yield! FolderedDraggableListHelper.itemShellClass item.Disabled draggable.isDragging
                match item.Tooltip with
                | Some tooltip when tooltip <> "" -> "swt:tooltip swt:tooltip-right"
                | _ -> ()
            ]
            prop.ariaLabel $"Drag {item.Label}"
            // Stable hook for hosts (e.g. tutorials) that must find an item
            // without coupling to the aria-label copy.
            prop.custom ("data-foldered-item-label", item.Label)
        ]

        let stateProps = [
            if draggable.isDragging then
                prop.tabIndex -1
            match item.Tooltip with
            | Some tooltip when tooltip <> "" ->
                prop.custom ("data-tip", tooltip)
                prop.title tooltip
            | _ -> ()
            if defaultArg debug false && not draggable.isDragging then
                prop.testId $"foldered-draggable-item-{item.Id}"
        ]

        Html.button (
            List.concat [
                identityProps
                dragProps
                appearanceProps
                stateProps
                [ prop.children [ renderItemContent renderProps ] ]
            ]
        )

    [<ReactComponent>]
    static member private FolderColorButton<'payload>
        (folder: FolderedDraggableFolder<'payload>, onSetColor: string option -> unit, ?key: string)
        =
        let draftColor, setDraftColor =
            React.useState (FolderedDraggableListHelper.currentOrFallback folder.Color)

        React.useEffect (
            (fun () -> setDraftColor (FolderedDraggableListHelper.currentOrFallback folder.Color)),
            [| box folder.Color |]
        )

        let trigger =
            Html.button [
                match key with
                | Some key -> prop.key key
                | None -> ()
                prop.type'.button
                prop.custom ("data-folder-color-control", "true")
                prop.custom ("data-foldered-color-swatch", "true")
                prop.className
                    "swt:size-3 swt:shrink-0 swt:cursor-pointer swt:rounded-full swt:border swt:border-base-300 swt:bg-base-100 swt:p-0 swt:shadow-sm"
                match folder.Color with
                | Some color when color <> "" -> prop.style [ style.backgroundColor color ]
                | _ -> ()
                prop.ariaLabel $"Set color for folder {folder.Name}"
            ]

        let content =
            FolderedDraggableListHelper.colorPickerContent
                $"Choose color for folder {folder.Name}"
                draftColor
                setDraftColor
                onSetColor

        Popover.Popover(
            children =
                React.Fragment [
                    Popover.Trigger(
                        trigger,
                        className = "swt:inline-flex swt:shrink-0",
                        props = [ prop.custom ("data-folder-color-control", "true") ]
                    )
                    Popover.Content(
                        children =
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.div [
                                        prop.className "swt:flex swt:items-start swt:justify-between swt:gap-2"
                                        prop.children [
                                            Html.div [ prop.className "swt:flex-1"; prop.children content ]
                                            Popover.Close()
                                        ]
                                    ]
                                ]
                            ]
                    )
                ]
        )

    /// One index-card tab in the strip above the card body. The active tab
    /// stays selected until another one is clicked - there is no toggle.
    static member private FolderTab<'payload>
        (folder: FolderedDraggableFolder<'payload>, isActive: bool, onSelect: unit -> unit, ?debug: bool, ?key: string)
        =
        Html.button [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.type'.button
            prop.custom ("role", "tab")
            prop.custom ("aria-selected", isActive)
            prop.title folder.Name
            prop.ariaLabel $"Show {folder.Name}"
            // Stable hook for hosts (e.g. tutorials) that must find a folder
            // tab without coupling to the aria-label copy.
            prop.custom ("data-foldered-folder-label", folder.Name)
            match folder.Color with
            | Some color when color <> "" -> prop.custom ("data-foldered-folder-color", color)
            | _ -> ()
            if defaultArg debug false then
                prop.testId $"foldered-draggable-folder-{folder.Id}"
            prop.className [
                "swt:flex swt:cursor-pointer swt:items-center swt:gap-2 swt:rounded-t-lg swt:border swt:border-b-0 swt:px-3 swt:py-1.5 swt:text-sm swt:transition-colors"
                if isActive then
                    // Sits on the card: same background, shared border, and the
                    // strip's -mb-px pulls it over the card's top border. The
                    // active tab claims the room for its full name first - it
                    // only truncates once it alone exceeds the strip - while
                    // the inactive tabs give way down to a slim remnant.
                    "swt:min-w-0 swt:shrink swt:border-base-300 swt:bg-base-100 swt:font-semibold swt:text-primary"
                else
                    "swt:min-w-14 swt:max-w-48 swt:shrink-[9] swt:border-transparent swt:bg-base-200 swt:text-base-content/70 swt:hover:bg-base-300"
            ]
            prop.onClick (fun _ -> onSelect ())
            prop.children [
                match folder.Color with
                | Some color when color <> "" ->
                    Html.span [
                        prop.className "swt:size-2.5 swt:shrink-0 swt:rounded-full swt:border swt:border-base-300"
                        prop.custom ("data-foldered-color-swatch", "true")
                        prop.style [ style.backgroundColor color ]
                    ]
                | _ -> Html.none
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate"
                    prop.text folder.Name
                ]
                Html.span [
                    prop.className "swt:badge swt:badge-sm swt:shrink-0"
                    prop.text (string folder.Items.Length)
                ]
            ]
        ]

    [<ReactComponent>]
    static member FolderedDraggableList<'payload>
        (
            folders: FolderedDraggableFolder<'payload> list,
            dragId: FolderedDraggableFolder<'payload> -> FolderedDraggableItem<'payload> -> string,
            ?activeFolderId: string,
            ?defaultActiveFolderId: string,
            ?onActiveFolderIdChange: string -> unit,
            ?renderItemContent: FolderedDraggableItemRenderFn<'payload>,
            ?shelfDropId: string,
            ?tryCreateItemFromExternalDrop: FolderedDraggableExternalDropHandler<'payload>,
            ?onFoldersChange: FolderedDraggableFolder<'payload> list -> unit,
            ?onSetFolderColor: string -> string option -> unit,
            ?className: string,
            ?debug: bool
        ) =
        let debug = defaultArg debug false
        let generatedShelfDropId = React.useId ()

        let shelfDropId =
            defaultArg shelfDropId $"foldered-draggable-list-shelf-{generatedShelfDropId}"

        let localActive, setLocalActive =
            React.useState (defaultActiveFolderId: string option)

        // One search draft per folder, so switching tabs never loses or leaks
        // a card's search into another card.
        let searchByFolder, setSearchByFolder = React.useState Map.empty<string, string>

        // Folders can disappear when the host swaps the folder set; drop their
        // drafts so the map does not accumulate entries for dead folders.
        React.useEffect (
            (fun () ->
                let folderIds = folders |> List.map (fun folder -> folder.Id) |> Set.ofList

                let pruned =
                    searchByFolder |> Map.filter (fun folderId _ -> folderIds.Contains folderId)

                if pruned.Count <> searchByFolder.Count then
                    setSearchByFolder pruned
            ),
            [| box folders |]
        )

        let activeDrag, setActiveDrag =
            React.useState (None: FolderedDraggableItemRender<'payload> option)

        let requestedActiveId = activeFolderId |> Option.orElse localActive

        // A stale id (e.g. after the host swaps the folder set) falls back to
        // the first tab instead of leaving the card empty.
        let activeFolder =
            folders
            |> List.tryFind (fun folder -> Some folder.Id = requestedActiveId)
            |> Option.orElse (List.tryHead folders)

        let canAcceptExternalDrop =
            FolderedDraggableListHelper.canAcceptExternalDrop activeFolder tryCreateItemFromExternalDrop onFoldersChange

        let shelfDroppable =
            DndKit.useDroppable (
                {|
                    id = shelfDropId
                    disabled = not canAcceptExternalDrop
                |}
            )

        let setActive folderId =
            onActiveFolderIdChange |> Option.iter (fun fn -> fn folderId)

            if activeFolderId.IsNone then
                setLocalActive (Some folderId)

        let renderItemContent =
            renderItemContent
            |> Option.defaultValue FolderedDraggableListHelper.defaultItemContent

        DndKit.useDndMonitor (
            {|
                onDragCancel = fun (_: DndKit.IDndKitEvent) -> setActiveDrag None
                onDragEnd =
                    fun (event: DndKit.IDndKitEvent) ->
                        setActiveDrag None

                        match activeFolder, tryCreateItemFromExternalDrop, onFoldersChange with
                        | Some targetFolder, Some tryCreateItemFromExternalDrop, Some onFoldersChange when
                            FolderedDraggableListHelper.isShelfDrop shelfDropId event
                            ->
                            let drop: FolderedDraggableExternalDrop<'payload> = {
                                TargetFolder = targetFolder
                                ActiveId = FolderedDraggableListHelper.activeId event
                                ActiveData = FolderedDraggableListHelper.activeData event
                                Folders = folders
                            }

                            match tryCreateItemFromExternalDrop drop with
                            | Some item ->
                                folders
                                |> FolderedDraggableListHelper.appendItemToFolder targetFolder.Id item
                                |> onFoldersChange
                            | None -> ()
                        | _ -> ()
            |}
        )

        let content =
            prop.children [
                // The tab strip overlaps the card's top border so the active
                // tab reads as part of the card, index-card style. It must be
                // positioned so it paints above the card sibling - otherwise
                // the card's top border draws over the active tab's bottom
                // edge and visually cuts it off from the card.
                Html.div [
                    prop.custom ("role", "tablist")
                    prop.className
                        "swt:relative swt:z-10 swt:-mb-px swt:flex swt:min-w-0 swt:flex-row swt:flex-nowrap swt:items-end swt:gap-1 swt:overflow-x-auto swt:px-2"
                    prop.style [ style.scrollbarGutter.stable ]
                    if debug then
                        prop.testId "foldered-draggable-folder-row"
                    prop.children [
                        for folder in folders do
                            let isActive = activeFolder |> Option.exists (fun active -> active.Id = folder.Id)

                            FolderedDraggableList.FolderTab(
                                folder,
                                isActive,
                                (fun () -> setActive folder.Id),
                                debug = debug,
                                key = folder.Id
                            )
                    ]
                ]
                match activeFolder with
                | Some folder ->
                    let searchQuery = searchByFolder |> Map.tryFind folder.Id |> Option.defaultValue ""

                    let visibleItems =
                        folder.Items
                        |> List.filter (FolderedDraggableListHelper.matchesSearch searchQuery)

                    let cardContent =
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:min-w-0 swt:items-center swt:gap-2"
                                prop.children [
                                    Html.div [
                                        prop.className "swt:relative swt:flex swt:min-w-0 swt:items-center"
                                        prop.children [
                                            Html.i [
                                                prop.className
                                                    "swt:iconify swt:fluent--search-20-regular swt:absolute swt:left-2 swt:size-3.5 swt:text-base-content/50"
                                            ]
                                            Html.input [
                                                prop.className
                                                    "swt:input swt:input-bordered swt:input-xs swt:w-44 swt:pl-7"
                                                prop.placeholder $"Search in {folder.Name}..."
                                                prop.ariaLabel $"Search in {folder.Name}"
                                                if debug then
                                                    prop.testId "foldered-draggable-search"
                                                prop.value searchQuery
                                                prop.onChange (fun (query: string) ->
                                                    setSearchByFolder (searchByFolder |> Map.add folder.Id query)
                                                )
                                            ]
                                        ]
                                    ]
                                    Html.span [
                                        prop.className "swt:text-xs swt:text-base-content/70"
                                        prop.text (
                                            if searchQuery.Trim() <> "" then
                                                $"{visibleItems.Length} of {folder.Items.Length} items"
                                            elif folder.Items.Length = 1 then
                                                "1 item"
                                            else
                                                $"{folder.Items.Length} items"
                                        )
                                    ]
                                    match onSetFolderColor with
                                    | Some setFolderColor ->
                                        Html.div [
                                            prop.className "swt:ml-auto swt:flex swt:items-center"
                                            prop.children [
                                                FolderedDraggableList.FolderColorButton(
                                                    folder,
                                                    setFolderColor folder.Id,
                                                    key = $"{folder.Id}:color"
                                                )
                                            ]
                                        ]
                                    | None -> Html.none
                                ]
                            ]
                            let shelfContent =
                                prop.children [
                                    Html.div [
                                        prop.className
                                            "swt:relative swt:flex swt:min-h-16 swt:min-w-0 swt:flex-row swt:flex-nowrap swt:items-start swt:gap-2 swt:overflow-x-auto swt:overflow-y-hidden swt:pb-1"
                                        prop.style [ style.scrollbarGutter.stable ]
                                        if debug then
                                            prop.testId "foldered-draggable-item-row"
                                        prop.children [
                                            if visibleItems.IsEmpty && searchQuery.Trim() <> "" then
                                                Html.p [
                                                    prop.className "swt:px-1 swt:text-xs swt:text-base-content/60"
                                                    prop.text $"No items match \"{searchQuery.Trim()}\"."
                                                ]
                                            for item in visibleItems do
                                                FolderedDraggableList.Item(
                                                    folder,
                                                    item,
                                                    dragId,
                                                    renderItemContent,
                                                    setActiveDrag,
                                                    debug = debug,
                                                    key = $"{folder.Id}:{item.Id}"
                                                )
                                        ]
                                    ]
                                ]

                            Html.div [
                                prop.ref shelfDroppable.setNodeRef
                                prop.className [
                                    "swt:min-h-16 swt:min-w-0 swt:rounded-lg swt:border swt:border-dashed swt:p-2 swt:transition-colors"
                                    if canAcceptExternalDrop && shelfDroppable.isOver then
                                        "swt:border-primary swt:bg-primary/10"
                                    else
                                        "swt:border-base-300 swt:bg-base-200/40"
                                ]
                                if debug then
                                    prop.testId "foldered-draggable-item-shelf"
                                shelfContent
                            ]
                        ]

                    Html.div [
                        prop.className
                            "swt:flex swt:min-w-0 swt:flex-col swt:gap-2 swt:rounded-lg swt:border swt:border-base-300 swt:bg-base-100 swt:p-3"
                        prop.custom ("role", "tabpanel")
                        if debug then
                            prop.testId "foldered-draggable-card"
                        cardContent
                    ]
                | None -> Html.none
                DndKit.DragOverlay(
                    dropAnimation = {| duration = 0; easing = "linear" |},
                    children =
                        match activeDrag with
                        | Some render ->
                            Html.div [
                                prop.className [
                                    yield! FolderedDraggableListHelper.itemShellClass render.Item.Disabled false
                                    "swt:pointer-events-none swt:shadow-xl swt:ring-2 swt:ring-primary swt:ring-offset-2 swt:ring-offset-base-100"
                                ]
                                if debug then
                                    prop.testId "foldered-draggable-drag-overlay"
                                prop.children [ renderItemContent { render with IsDragging = true } ]
                            ]
                        | None -> Html.none
                )
            ]

        Html.section [
            prop.className [
                "swt:flex swt:min-w-0 swt:flex-col"
                match className with
                | Some className -> className
                | None -> ()
            ]
            if debug then
                prop.testId "foldered-draggable-list"
            content
        ]
