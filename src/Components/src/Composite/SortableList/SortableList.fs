namespace Swate.Components.Composite.SortableList

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.JsBindings
open Types

[<Erase; Mangle(false)>]
type SortableList =
    [<ReactComponent>]
    static member private Row
        (
            item: SortableListItem<'A>,
            index: int,
            count: int,
            move: int -> int -> unit,
            remove: string -> unit,
            renderRow: SortableListRowRender<'A> -> ReactElement,
            ?rowProps: SortableListItem<'A> -> IReactProperty list,
            ?key: string
        ) =
        let sortable = DndKit.useSortable {| id = item.id |}

        let properties =
            rowProps |> Option.map (fun getProps -> getProps item) |> Option.defaultValue []

        let customStyle =
            properties
            |> List.rev
            |> List.tryPick (fun property ->
                let name, value = unbox<string * obj> property
                if name = "style" then Some value else None
            )

        let rowStyle =
            Object.merge (defaultArg customStyle null) {|
                transform = DndKit.CSS.Transform.toString sortable.transform
                transition = sortable.transition
            |}

        let row: SortableListRowRender<'A> = {|
            item = item
            index = index
            isFirst = index = 0
            isLast = index = count - 1
            dragHandleProps = [
                yield! prop.spread sortable.attributes
                yield! prop.spread sortable.listeners
            ]
            moveUp = fun () -> move index (index - 1)
            moveDown = fun () -> move index (index + 1)
            remove = fun () -> remove item.id
        |}

        Html.tr [
            yield! properties
            prop.testId $"sortable-list-row-{item.id}"
            prop.ref sortable.setNodeRef
            prop.style rowStyle
            prop.children [ renderRow row ]
        ]

    /// The parent owns items. Reorders and removals emit a new array without mutating the input.
    /// renderRow supplies table cells; rowProps customizes the surrounding sortable row.
    /// Inline row styles are preserved, with transform and transition reserved for sorting.
    [<ReactComponent(true)>]
    static member SortableList
        (
            items: SortableListItem<'A>[],
            onItemsChange: SortableListItem<'A>[] -> unit,
            ?selectedIds: string list,
            ?setSelectedIds: (string list -> string list) -> unit,
            ?renderRow: SortableListRowRender<'A> -> ReactElement,
            ?rowProps: SortableListItem<'A> -> IReactProperty list,
            ?className: string
        ) =

        let lastInteraction, setLastInteraction = React.useState (None: string option)

        let move oldIndex newIndex =
            if
                oldIndex <> newIndex
                && oldIndex >= 0
                && newIndex >= 0
                && oldIndex < items.Length
                && newIndex < items.Length
            then
                let itemId = items.[oldIndex].id

                DndKit.arrayMove (ResizeArray items, oldIndex, newIndex)
                |> Seq.toArray
                |> onItemsChange

                setLastInteraction (Some itemId)

        let rowProps: (SortableListItem<'A> -> IReactProperty list) option =
            let withOnSelect =
                match setSelectedIds, selectedIds with
                | Some setSelectedIds, Some selectedIds ->
                    let defFn = rowProps |> Option.defaultValue (fun _ -> [])

                    fun item ->
                        defFn item
                        @ [
                            prop.className [
                                "swt:cursor-pointer swt:table-auto"
                                if List.contains item.id selectedIds then
                                    "swt:bg-base-300"
                                if lastInteraction.IsSome && item.id = lastInteraction.Value then
                                    "swt:outline swt:outline-primary"
                            ]
                            prop.onClick (fun event ->
                                let selectRange () =
                                    match lastInteraction with
                                    | Some lastInteractionId when event.shiftKey ->
                                        let lastIndex =
                                            items |> Array.tryFindIndex (fun item -> item.id = lastInteractionId)

                                        let currentIndex =
                                            items |> Array.tryFindIndex (fun currentItem -> currentItem.id = item.id)

                                        match lastIndex, currentIndex with
                                        | Some lastIndex, Some currentIndex ->
                                            let firstIndex = min lastIndex currentIndex
                                            let lastIndex = max lastIndex currentIndex

                                            let rangeIds = items.[firstIndex..lastIndex] |> Array.map _.id

                                            setSelectedIds (fun current ->
                                                rangeIds
                                                |> Array.fold
                                                    (fun selected id ->
                                                        if List.contains id selected then
                                                            selected
                                                        else
                                                            id :: selected
                                                    )
                                                    current
                                            )

                                            true
                                        | _ -> false
                                    | _ -> false

                                if not (selectRange ()) then
                                    setSelectedIds (fun current ->
                                        if List.contains item.id current then
                                            List.filter ((<>) item.id) current
                                        else
                                            item.id :: current
                                    )

                                setLastInteraction (Some item.id)
                            )
                        ]
                    |> Some
                | _, _ -> rowProps

            withOnSelect

        let remove id =
            items |> Array.filter (fun item -> item.id <> id) |> onItemsChange

        let pointerSensor =
            DndKit.useSensor (
                DndKit.PointerSensor,
                {|
                    activationConstraint = {| distance = 6 |}
                |}
            )

        let sensors = DndKit.useSensors [| pointerSensor |]

        let handleDragEnd (event: DndKit.IDndKitEvent) =
            if not (isNull event.over) then
                let oldIndex =
                    items |> Array.tryFindIndex (fun item -> item.id = string event.active.id)

                let newIndex =
                    items |> Array.tryFindIndex (fun item -> item.id = string event.over.id)

                match oldIndex, newIndex with
                | Some oldIndex, Some newIndex ->
                    move oldIndex newIndex
                    setLastInteraction (Some(string event.active.id))
                | _ -> ()

        let itemIds =
            React.useMemo ((fun () -> ResizeArray(items |> Array.map _.id)), [| box items |])

        let renderRow =
            fun row ->
                match renderRow with
                | Some render -> render row
                | None when setSelectedIds.IsSome && selectedIds.IsSome ->
                    let isSelected = selectedIds.Value |> List.contains row.item.id
                    RowComponents.DefaultToggleRow(row, isSelected)
                | None -> RowComponents.DefaultRow(row)

        DndKit.DndContext(
            sensors = sensors,
            collisionDetection = DndKit.pointerWithin,
            onDragEnd = handleDragEnd,
            accessibility = {|
                screenReaderInstructions = {|
                    draggable =
                        "Drag to reorder, or use the Move up and Move down buttons. Use the Remove button to remove an item."
                |}
            |},
            children =
                DndKit.SortableContext(
                    items = itemIds,
                    strategy = DndKit.verticalListSortingStrategy,
                    children =
                        Html.div [
                            prop.testId "sortable-list"
                            prop.className [
                                "swt:overflow-visible swt:border swt:border-base-300 swt:rounded-box"
                                defaultArg className ""
                            ]
                            prop.children [
                                Html.table [
                                    prop.className "swt:table swt:table-xs swt:table-fixed swt:min-w-full"
                                    prop.children [
                                        Html.tbody [
                                            match items with
                                            | [||] ->
                                                Html.tr [
                                                    Html.td [
                                                        prop.colSpan 3
                                                        prop.children [
                                                            Html.div [
                                                                prop.className "swt:text-center swt:opacity-50"
                                                                prop.text "No items available"
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            | _ ->
                                                for index in 0 .. items.Length - 1 do
                                                    let item = items.[index]

                                                    SortableList.Row(
                                                        item,
                                                        index,
                                                        items.Length,
                                                        move,
                                                        remove,
                                                        renderRow = renderRow,
                                                        ?rowProps = rowProps,
                                                        key = item.id
                                                    )
                                        ]
                                    ]
                                ]
                            ]
                        ]
                )
        )
