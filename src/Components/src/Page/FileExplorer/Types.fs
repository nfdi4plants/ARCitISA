module Swate.Components.Page.FileExplorer.Types

open System
open Feliz

[<RequireQualifiedAccess>]
type FileItemIconTone =
    | BaseMuted
    | BaseSubtle
    | Secondary
    | Success
    | Primary
    | Warning
    | Info
    | Accent
    | Error

[<RequireQualifiedAccess>]
module FileItemIconTone =

    let className =
        function
        | FileItemIconTone.BaseMuted -> "swt:text-base-content/70"
        | FileItemIconTone.BaseSubtle -> "swt:text-base-content/60"
        | FileItemIconTone.Secondary -> "swt:text-secondary"
        | FileItemIconTone.Success -> "swt:text-success"
        | FileItemIconTone.Primary -> "swt:text-primary"
        | FileItemIconTone.Warning -> "swt:text-warning"
        | FileItemIconTone.Info -> "swt:text-info"
        | FileItemIconTone.Accent -> "swt:text-accent"
        | FileItemIconTone.Error -> "swt:text-error"

[<RequireQualifiedAccess>]
type FileItemIcon =
    | Folder
    | Document
    | BookOpen
    | Table
    | Map
    | Database
    | Tag
    | Block
    | Study
    | Assay
    | Workflow
    | Run
    | Notebook
    | Note
    | MoreHorizontal

[<RequireQualifiedAccess>]
module FileItemIcon =

    let className =
        function
        | FileItemIcon.Folder -> "swt:fluent--folder-24-regular"
        | FileItemIcon.Document -> "swt:fluent--document-24-regular"
        | FileItemIcon.BookOpen -> "swt:fluent--book-open-24-regular"
        | FileItemIcon.Table -> "swt:fluent--table-24-regular"
        | FileItemIcon.Map -> "swt:fluent--map-16-regular"
        | FileItemIcon.Database -> "swt:fluent--database-24-regular"
        | FileItemIcon.Tag -> "swt:fluent--tag-24-regular"
        | FileItemIcon.Block -> "swt:fluent--prohibited-24-regular"
        | FileItemIcon.Study -> "swt:fluent--document-table-24-regular"
        | FileItemIcon.Assay -> "swt:fluent--beaker-24-regular"
        | FileItemIcon.Workflow -> "swt:fluent--flowchart-24-regular"
        | FileItemIcon.Run -> "swt:fluent--play-24-regular"
        | FileItemIcon.Notebook -> "swt:fluent--notebook-24-regular"
        | FileItemIcon.Note -> "swt:fluent--note-24-regular"
        | FileItemIcon.MoreHorizontal -> "swt:fluent--more-horizontal-24-regular"

type FileItem = {
    Id: string
    Name: string
    Icon: FileItemIcon
    IconTone: FileItemIconTone option
    IsExpanded: bool
    Children: FileItem list option
    IdRel: string option
    IsDirectory: bool
    IsLFS: bool option
    IsLFSPointer: bool option
    Checkout: string option
    Downloaded: bool option
    LfsActivity: string option
    Size: int64 option
    SizeFormatted: string option
    ItemType: string
    Label: string option
    Selectable: bool
    Path: string option
}

// Helper type for file tree creation using a predefined config
type FileItemConfig = {
    Name: string
    Icon: FileItemIcon
    IconTone: FileItemIconTone option
    IsDirectory: bool
    ItemType: string
}

// ============================================================================
// FILE TREE OPERATIONS
// ============================================================================
module FileTree =
    let private generateId () = Guid.NewGuid().ToString()

    let formatSize (size: int64) : string =
        let log = Math.Floor(Math.Log(float size) / Math.Log 1024.0)
        let log = Math.Max(0.0, Math.Min(4.0, log)) |> int
        let suffixes = [| "B"; "KB"; "MB"; "GB"; "TB" |]
        let divisor = Math.Pow(1024.0, float log)
        sprintf "%.0f %s" (float size / divisor) suffixes.[log]

    let createFile (name: string) path (icon: FileItemIcon) : FileItem = {
        Id = generateId ()
        Name = name
        Icon = icon
        IconTone = None
        IsExpanded = false
        Children = None
        IdRel = None
        IsDirectory = false
        IsLFS = None
        IsLFSPointer = None
        Checkout = None
        Downloaded = None
        LfsActivity = None
        Size = None
        SizeFormatted = None
        ItemType = "node"
        Label = Some name
        Selectable = false
        Path = path
    }

    let createFolder (name: string) path (icon: FileItemIcon) : FileItem = {
        Id = generateId ()
        Name = name
        Icon = icon
        IconTone = None
        IsExpanded = false
        Children = Some []
        IdRel = None
        IsDirectory = true
        IsLFS = None
        IsLFSPointer = None
        Checkout = None
        Downloaded = None
        LfsActivity = None
        Size = None
        SizeFormatted = None
        ItemType = "node"
        Label = Some name
        Selectable = false
        Path = path
    }

    let createFromConfig (config: FileItemConfig) path (id: string) : FileItem = {
        Id = id
        Name = config.Name
        Icon = config.Icon
        IconTone = config.IconTone
        IsExpanded = false
        Children = if config.IsDirectory then Some [] else None
        IdRel = None
        IsDirectory = config.IsDirectory
        IsLFS = None
        IsLFSPointer = None
        Checkout = None
        Downloaded = None
        LfsActivity = None
        Size = None
        SizeFormatted = None
        ItemType = config.ItemType
        Label = Some config.Name
        Selectable = false
        Path = path
    }

    let rec getPath itemId items currentPath =
        items
        |> List.tryPick (fun i ->
            if i.Id = itemId then
                Some(currentPath @ [ i ])
            else
                match i.Children with
                | Some children -> getPath itemId children (currentPath @ [ i ])
                | None -> None
        )

    let rec findItem itemId items =
        items
        |> List.tryPick (fun item ->
            if item.Id = itemId then
                Some item
            else
                match item.Children with
                | Some children -> findItem itemId children
                | None -> None
        )

    let sortItems (items: FileItem list) (enforcedOrder: string list) =
        items
        |> List.sortWith (fun a b ->
            let aIdx = enforcedOrder |> List.tryFindIndex ((=) a.Name)
            let bIdx = enforcedOrder |> List.tryFindIndex ((=) b.Name)

            match aIdx, bIdx with
            | Some ai, Some bi -> compare ai bi
            | Some _, None -> -1
            | None, Some _ -> 1
            | None, None ->
                match a.IsDirectory, b.IsDirectory with
                | true, false -> -1
                | false, true -> 1
                | _ -> compare a.Name b.Name
        )

    let createEmptyPlaceholder (parentId: string) : FileItem = {
        Id = sprintf "%s/empty$%s" parentId (generateId ())
        Name = "empty"
        Icon = FileItemIcon.Block
        IconTone = None
        IsExpanded = false
        Children = None
        IdRel = None
        IsDirectory = false
        IsLFS = None
        IsLFSPointer = None
        Checkout = None
        Downloaded = None
        LfsActivity = None
        Size = None
        SizeFormatted = None
        ItemType = "empty"
        Label = Some "empty"
        Selectable = false
        Path = None
    }

type ContextMenuItem = {
    Label: string
    Icon: string
    OnClick: unit -> unit
    Disabled: bool option
    ClassName: string option
    IsDivider: bool option
} with

    member this.ToPrimitiveContextMenuItem() =
        if defaultArg this.IsDivider false then
            Swate.Components.Primitive.ContextMenu.Types.ContextMenuItem(isDivider = true)
        else
            let isDisabled = defaultArg this.Disabled false

            let className =
                [
                    this.ClassName

                    if isDisabled then
                        Some "swt:opacity-50"
                ]
                |> List.choose id
                |> String.concat " "

            Swate.Components.Primitive.ContextMenu.Types.ContextMenuItem(
                text = Html.span [ prop.className className; prop.text this.Label ],
                icon =
                    Html.i [
                        prop.className [
                            "swt:iconify " + this.Icon

                            if not (System.String.IsNullOrWhiteSpace className) then
                                className
                        ]
                    ],
                onClick =
                    (fun _ ->
                        if not isDisabled then
                            this.OnClick()
                    )
            )

[<RequireQualifiedAccess>]
module ContextMenuItem =

    let create (label: string) (icon: string) (onClick: unit -> unit) : ContextMenuItem = {
        Label = label
        Icon = icon
        OnClick = onClick
        Disabled = None
        ClassName = None
        IsDivider = None
    }

    let styled (label: string) (icon: string) (className: string) (onClick: unit -> unit) : ContextMenuItem = {
        (create label icon onClick) with
            ClassName = Some className
    }

    let disabled (label: string) (icon: string) : ContextMenuItem = {
        (create label icon ignore) with
            Disabled = Some true
    }

    let divider: ContextMenuItem = {
        (disabled "" "") with
            IsDivider = Some true
    }

    let forItem label icon onClick item =
        create label icon (fun () -> onClick item)

    let whenItem predicate label icon onClick item =
        if predicate item then
            [ forItem label icon onClick item ]
        else
            []

module FileExplorerLogic =

    let private expandedIdsFromPath itemId items =
        let pathItems =
            match FileTree.getPath itemId items [] with
            | Some pathItems -> pathItems
            | None -> []

        pathItems
        |> List.take (max 0 (List.length pathItems - 1))
        |> List.choose (fun item -> if item.Children.IsSome then Some item.Id else None)
        |> Set.ofList

    /// The folders to open so the item can be seen: its parent folders, and the item itself when it is a folder.
    let private revealedIdsFromPath itemId items =
        let ownId =
            match FileTree.findItem itemId items with
            | Some item when item.IsDirectory && item.Children.IsSome -> Set.singleton item.Id
            | _ -> Set.empty

        expandedIdsFromPath itemId items |> Set.union ownId

    let rec private collectExpandedIds acc items =
        items
        |> List.fold
            (fun accIds item ->
                let newAcc = if item.IsExpanded then Set.add item.Id accIds else accIds

                match item.Children with
                | Some children -> collectExpandedIds newAcc children
                | None -> newAcc
            )
            acc

    /// A directory can expand when its children are not loaded yet (None) or when it has children.
    /// Some [] is a known empty directory.
    let canExpandDirectory (item: FileItem) =
        item.IsDirectory
        && (
            match item.Children with
            | Some children -> not (List.isEmpty children)
            | None -> true
        )

    /// One visible row of the tree in display order.
    type VisibleRow = {
        Id: string
        Item: FileItem
        Depth: int
        SetSize: int
        PosInSet: int
    }

    /// Rows that are visible with the given expanded directories, in display order. Children of
    /// collapsed directories and of directories without loaded children are not visible.
    let flattenVisibleRows (expandedIds: Set<string>) (items: FileItem list) : VisibleRow[] =
        let rows = ResizeArray<VisibleRow>()

        let rec walk depth (siblings: FileItem list) =
            let setSize = List.length siblings

            siblings
            |> List.iteri (fun index item ->
                rows.Add {
                    Id = item.Id
                    Item = item
                    Depth = depth
                    SetSize = setSize
                    PosInSet = index + 1
                }

                if item.IsDirectory && expandedIds.Contains item.Id then
                    match item.Children with
                    | Some children -> walk (depth + 1) children
                    | None -> ()
            )

        walk 0 items
        rows.ToArray()

    /// State the component keeps when the parent does not control expansion or selection.
    type Model = {
        SelectedId: string option
        ExpandedIds: Set<string>
        /// The reveal id whose folders are already open. It stays None while the current id waits for its item.
        LastRevealId: string option
    }

    type Msg =
        | SetExpanded of itemId: string * isExpanded: bool
        | ExpandMany of Set<string>
        | SelectItem of string
        | RevealItem of revealItemId: string option * items: FileItem list

    let init items = {
        SelectedId = None
        ExpandedIds = collectExpandedIds Set.empty items
        LastRevealId = None
    }

    let update msg model =
        match msg with
        | SetExpanded(itemId, isExpanded) ->
            let expandedIds =
                if isExpanded then
                    model.ExpandedIds.Add itemId
                else
                    model.ExpandedIds.Remove itemId

            { model with ExpandedIds = expandedIds }

        | ExpandMany itemIds -> {
            model with
                ExpandedIds = Set.union model.ExpandedIds itemIds
          }

        | SelectItem itemId -> { model with SelectedId = Some itemId }

        | RevealItem(revealItemId, items) ->
            match revealItemId with
            | Some itemId when model.LastRevealId = Some itemId -> model
            | Some itemId when (FileTree.findItem itemId items).IsSome -> {
                model with
                    LastRevealId = Some itemId
                    ExpandedIds = Set.union model.ExpandedIds (revealedIdsFromPath itemId items)
              }
            | _ when model.LastRevealId.IsNone -> model
            | _ -> { model with LastRevealId = None }

/// The stable callbacks of a tree row. The rows read them through the latest props of the explorer,
/// so a memoized row calls the current functions even when its own props did not change.
type TreeRowHandlers = {
    GetItemIconClass: FileItem -> string option
    GetItemActions: FileItem -> ContextMenuItem list
    GetItemStatusAction: FileItem -> ContextMenuItem option
    CanCreateItem: FileItem -> bool
    OnCreateItem: (FileItem -> unit) option
    CanDeleteItem: FileItem -> bool
    OnDeleteItem: (FileItem -> unit) option
    /// Selects the file or toggles the directory with the given row id.
    OnRowClick: string -> unit
}

type TreeRowProps = {
    Row: FileExplorerLogic.VisibleRow
    IsSelected: bool
    /// Selected rows and the ancestors of the selected row.
    IsHighlighted: bool
    IsExpanded: bool
    Handlers: TreeRowHandlers
    /// Mirrors truncateOverflowingItemNames of the explorer.
    TruncateNames: bool
}
