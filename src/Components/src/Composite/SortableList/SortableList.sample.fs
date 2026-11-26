module internal Arcitisa.Components.Composite.SortableListSample

open Fable.Core
open Feliz
open Arcitisa.Components.Composite.SortableList
open Arcitisa.Components.Composite.SortableList.Types

let sampleItems: SortableListItem<{| description: string |}>[] = [|
    {|
        id = "alpha"
        label = "Alpha"
        data = Some {| description = "First sample" |}
    |}
    {|
        id = "beta"
        label = "Beta"
        data = Some {| description = "Second sample" |}
    |}
    {|
        id = "gamma"
        label = "Gamma"
        data = None
    |}
|]

[<Erase; Mangle(false)>]
type SortableListSample =

    [<ReactComponent(true)>]
    static member Sample() =
        let items, setItems = React.useState sampleItems
        let order = String.concat ", " (items |> Array.map _.label)

        let add =
            fun () ->
                setItems (
                    Array.append items [|
                        {|
                            id = System.Guid.NewGuid().ToString()
                            label = "New Item"
                            data = None
                        |}
                    |]
                )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-4 swt:w-full"
            prop.children [
                Html.div [
                    Html.h3 "Sortable List Order"
                    Html.p [ Html.text $"Current order: "; Html.b order ]
                ]
                Html.div [
                    Html.h3 "Sortable List Order"
                    Html.div [
                        Html.button [
                            prop.className "swt:btn swt:btn-primary"
                            prop.onClick (fun _ -> add ())
                            prop.text "Add Item"
                        ]
                    ]
                ]
                SortableList.SortableList(items, setItems)
            ]
        ]

    [<ReactComponent>]
    static member private CustomRow(row: SortableListRowRender<{| description: string |}>) =
        React.Fragment [
            Html.td [
                prop.className "swt:w-10"
                prop.children [ RowComponents.DragHandle(row) ]
            ]
            Html.td [
                prop.testId $"sortable-list-custom-{row.item.id}"
                prop.children [
                    Html.strong row.item.label
                    Html.p [
                        prop.className "swt:text-xs swt:opacity-70"
                        prop.text (row.item.data |> Option.map _.description |> Option.defaultValue "No extra data")
                    ]
                ]
            ]
            Html.td [
                prop.className "swt:w-32"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:gap-2 swt:justify-end"
                        prop.children [
                            Html.div [
                                prop.className "swt:join"
                                prop.children [
                                    RowComponents.MoveUpButton(row)
                                    RowComponents.MoveDownButton(row)
                                ]
                            ]
                            RowComponents.RemoveButton(row)
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member CustomRowSample() =
        let items, setItems = React.useState sampleItems
        SortableList.SortableList(items, setItems, renderRow = (fun row -> SortableListSample.CustomRow(row)))

    [<ReactComponent>]
    static member EmptySample() =
        SortableList.SortableList(([||]: SortableListItem<unit>[]), ignore)
