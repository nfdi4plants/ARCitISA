namespace Swate.Components.Composite.SortableList

open Fable.Core
open Feliz
open Types

/// Controls can be composed inside custom table-cell renderers.
[<Erase; Mangle(false)>]
type RowComponents =

    [<ReactComponent>]
    static member DragHandle(row: SortableListRowRender<'A>) =
        Html.button [
            yield! row.dragHandleProps
            prop.testId $"sortable-list-drag-{row.item.id}"
            prop.type'.button
            prop.ariaLabel $"Drag {row.item.label}"
            prop.title $"Drag to reorder {row.item.label}"
            prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:cursor-grab"
            prop.style [ style.custom ("touchAction", "none") ]
            prop.onClick (fun e -> e.stopPropagation ())
            prop.children [
                Html.span [
                    prop.className "swt:iconify swt:fluent--arrow-sort-16-filled swt:size-4"
                ]
            ]
        ]

    [<ReactComponent>]
    static member MoveUpButton(row: SortableListRowRender<'A>) =
        Html.button [
            prop.testId $"sortable-list-up-{row.item.id}"
            prop.type'.button
            prop.ariaLabel $"Move {row.item.label} up"
            prop.title $"Move {row.item.label} up"
            prop.disabled row.isFirst
            prop.className "swt:btn swt:btn-xs swt:join-item"
            prop.onClick (fun e ->
                e.stopPropagation ()
                row.moveUp ()
            )
            prop.children [
                Html.span [
                    prop.className "swt:iconify swt:fluent--arrow-sort-up-16-filled swt:size-4"
                ]
            ]
        ]

    [<ReactComponent>]
    static member MoveDownButton(row: SortableListRowRender<'A>) =
        Html.button [
            prop.testId $"sortable-list-down-{row.item.id}"
            prop.type'.button
            prop.ariaLabel $"Move {row.item.label} down"
            prop.title $"Move {row.item.label} down"
            prop.disabled row.isLast
            prop.className "swt:btn swt:btn-xs swt:join-item"
            prop.onClick (fun e ->
                e.stopPropagation ()
                row.moveDown ()
            )
            prop.children [
                Html.span [
                    prop.className "swt:iconify swt:fluent--arrow-sort-down-16-filled swt:size-4"
                ]
            ]
        ]

    [<ReactComponent>]
    static member RemoveButton(row: SortableListRowRender<'A>) =
        Html.button [
            prop.testId $"sortable-list-remove-{row.item.id}"
            prop.type'.button
            prop.ariaLabel $"Remove {row.item.label}"
            prop.title $"Remove {row.item.label}"
            prop.className "swt:btn swt:btn-xs swt:btn-error swt:btn-outline"
            prop.onClick (fun e ->
                e.stopPropagation ()
                row.remove ()
            )
            prop.children [
                Html.span [
                    prop.className "swt:iconify swt:fluent--delete-24-filled swt:size-4"
                ]
            ]
        ]

    [<ReactComponent>]
    static member IsSelectedToggle(isSelected: bool) =
        Html.input [
            prop.className "swt:checkbox swt:checkbox-primary swt:checkbox-xs"
            prop.type'.checkbox
            prop.isChecked isSelected
            prop.readOnly true
        ]

    [<ReactComponent>]
    static member DefaultRow(row: SortableListRowRender<'A>, ?label: ReactElement) =
        React.Fragment [
            Html.td [
                prop.className "swt:w-10"
                prop.children [ RowComponents.DragHandle(row) ]
            ]
            Html.td [
                prop.className "swt:max-w-md swt:truncate"
                prop.title row.item.label
                prop.children [
                    defaultArg label (Html.span [ prop.className "swt:font-mono"; prop.text row.item.label ])
                ]
            ]
            Html.td [
                prop.className "swt:w-20"
                prop.children [
                    Html.div [
                        prop.className "swt:join"
                        prop.children [
                            RowComponents.MoveUpButton(row)
                            RowComponents.MoveDownButton(row)
                        ]
                    ]
                ]
            ]
            Html.td [
                prop.className "swt:w-14 swt:text-right"
                prop.children [ RowComponents.RemoveButton(row) ]
            ]
        ]

    [<ReactComponent>]
    static member DefaultToggleRow(row: SortableListRowRender<'A>, isSelected: bool, ?label: ReactElement) =
        React.Fragment [
            Html.td [
                prop.className "swt:w-20"
                prop.children [
                    Html.div [
                        RowComponents.IsSelectedToggle(isSelected)
                        RowComponents.DragHandle(row)
                    ]
                ]
            ]
            Html.td [
                prop.className "swt:max-w-md swt:truncate"
                prop.title row.item.label
                prop.children [
                    defaultArg label (Html.span [ prop.className "swt:font-mono"; prop.text row.item.label ])
                ]
            ]
            Html.td [
                prop.className "swt:w-20"
                prop.children [
                    Html.div [
                        prop.className "swt:join"
                        prop.children [
                            RowComponents.MoveUpButton(row)
                            RowComponents.MoveDownButton(row)
                        ]
                    ]
                ]
            ]
            Html.td [
                prop.className "swt:w-14 swt:text-right"
                prop.children [ RowComponents.RemoveButton(row) ]
            ]
        ]
