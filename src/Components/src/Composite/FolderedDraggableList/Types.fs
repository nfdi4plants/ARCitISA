module Arcitisa.Components.Composite.FolderedDraggableList.Types

open Feliz

type FolderedDraggableItem<'payload> = {
    Id: string
    Label: string
    Payload: 'payload
    Color: string option
    Badge: string option
    Tooltip: string option
    Disabled: bool
}

type FolderedDraggableFolder<'payload> = {
    Id: string
    Name: string
    Color: string option
    Items: FolderedDraggableItem<'payload> list
}

type FolderedDraggableData<'payload> = {
    FolderId: string
    FolderName: string
    FolderColor: string option
    ItemId: string
    ItemLabel: string
    ItemColor: string option
    EffectiveColor: string option
    Payload: 'payload
}

type FolderedDraggableExternalDrop<'payload> = {
    TargetFolder: FolderedDraggableFolder<'payload>
    ActiveId: string
    ActiveData: obj
    Folders: FolderedDraggableFolder<'payload> list
}

type FolderedDraggableItemRender<'payload> = {
    Folder: FolderedDraggableFolder<'payload>
    Item: FolderedDraggableItem<'payload>
    DragData: FolderedDraggableData<'payload>
    IsDragging: bool
}

type FolderedDraggableItemRenderFn<'payload> = FolderedDraggableItemRender<'payload> -> ReactElement

type FolderedDraggableExternalDropHandler<'payload> =
    FolderedDraggableExternalDrop<'payload> -> FolderedDraggableItem<'payload> option
