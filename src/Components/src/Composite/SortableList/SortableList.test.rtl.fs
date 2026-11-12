module internal Swate.Components.Composite.SortableList.Tests

open System.Text.RegularExpressions
open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Vitest
open Swate.Components.Composite.SortableListSample
open Types

[<Import("installRowGeometry", "./SortableList.test.helpers.ts")>]
let private installRowGeometry () : unit = jsNative

[<Import("restoreGeometry", "./SortableList.test.helpers.ts")>]
let private restoreGeometry () : unit = jsNative

[<Import("readRowStyle", "./SortableList.test.helpers.ts")>]
let private readRowStyle
    (row: HTMLElement)
    : {|
          backgroundColor: string
          height: string
      |}
    =
    jsNative

let private order () =
    RTL.screen.getAllByTestId (System.Text.RegularExpressions.Regex "^sortable-list-row-")
    |> Seq.map (fun row -> row.getAttribute "data-testid")
    |> Seq.toArray

let private expectOrder ids =
    Vitest.expect(order ()).toEqual (ids |> Array.map (fun id -> $"sortable-list-row-{id}"))

let private click (id: string) =
    RTL.fireEvent.click (RTL.screen.getByTestId id)

let private isDisabled (id: string) =
    (RTL.screen.getByTestId id :?> HTMLButtonElement).disabled

let private renderSample () =
    RTL.render (SortableListSample.Sample()) |> ignore

let private selectableItems =
    Array.append sampleItems [|
        {|
            id = "delta"
            label = "Delta"
            data = None
        |}
    |]

[<ReactComponent>]
let private SelectableSample (initialSelectedIds: string list) =
    let selectedIds, setSelectedIds = React.useStateWithUpdater initialSelectedIds

    React.Fragment [
        SortableList.SortableList(selectableItems, ignore, selectedIds = selectedIds, setSelectedIds = setSelectedIds)
        Html.output [
            prop.testId "selected-ids"
            prop.text (String.concat "," selectedIds)
        ]
    ]

let private shiftClick (id: string) =
    RTL.fireEvent.click (RTL.screen.getByTestId id, {| shiftKey = true |})

Vitest.test (
    "default selection checkboxes follow row clicks and toggle their row",
    fun () ->
        RTL.render (SelectableSample []) |> ignore

        let checkbox () =
            RTL.screen.getByRole ("checkbox", ByRoleOptions(name = Text "Select Alpha")) :?> HTMLInputElement

        Vitest.expect((checkbox ()).``checked``).toBe false
        click "sortable-list-row-alpha"
        Vitest.expect((checkbox ()).``checked``).toBe true
        RTL.fireEvent.click (checkbox ())
        Vitest.expect((checkbox ()).``checked``).toBe false
        Vitest.expect((RTL.screen.getByTestId "selected-ids").textContent).toBe ""
)

Vitest.beforeEach (fun () -> installRowGeometry ())

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    restoreGeometry ()
)

Vitest.test (
    "arrow down moves the item and preserves the others",
    fun () ->
        renderSample ()
        click "sortable-list-down-alpha"
        expectOrder [| "beta"; "alpha"; "gamma" |]
)

Vitest.test (
    "arrow up moves the item and updates the boundary controls",
    fun () ->
        renderSample ()
        click "sortable-list-up-beta"
        expectOrder [| "beta"; "alpha"; "gamma" |]
        Vitest.expect(isDisabled "sortable-list-up-beta").toBe true
        Vitest.expect(isDisabled "sortable-list-up-alpha").toBe false
)

Vitest.test (
    "first up and last down are disabled and cannot reorder",
    fun () ->
        renderSample ()
        Vitest.expect(isDisabled "sortable-list-up-alpha").toBe true
        Vitest.expect(isDisabled "sortable-list-down-gamma").toBe true
        Vitest.expect(isDisabled "sortable-list-down-alpha").toBe false
        Vitest.expect(isDisabled "sortable-list-up-gamma").toBe false
        click "sortable-list-up-alpha"
        click "sortable-list-down-gamma"
        expectOrder [| "alpha"; "beta"; "gamma" |]
)

Vitest.test (
    "remove deletes only its item and updates the remaining controls",
    fun () ->
        renderSample ()
        click "sortable-list-remove-beta"
        expectOrder [| "alpha"; "gamma" |]
        Vitest.expect(RTL.screen.queryByTestId("sortable-list-row-beta").IsNone).toBe true
        click "sortable-list-remove-alpha"
        expectOrder [| "gamma" |]
        Vitest.expect(isDisabled "sortable-list-up-gamma").toBe true
        Vitest.expect(isDisabled "sortable-list-down-gamma").toBe true
        click "sortable-list-remove-gamma"

        Vitest
            .expect(RTL.screen.queryAllByTestId(System.Text.RegularExpressions.Regex "^sortable-list-row-").Count)
            .toBe
            0
)

let private pointerEvent name (target: HTMLElement) x y buttons =
    RTL.fireEvent.custom (
        name,
        target,
        {|
            clientX = x
            clientY = y
            button = 0
            buttons = buttons
            pointerId = 1
            isPrimary = true
            pointerType = "mouse"
        |}
    )

let private dragTo y = promise {
    let handle = RTL.screen.getByTestId "sortable-list-drag-alpha"
    pointerEvent "pointerDown" handle 20 60 1
    // First cross the sensor's activation threshold, then move to the target.
    do! RTL.act (fun () -> promise { pointerEvent "pointerMove" (unbox document) 20 70 1 })
    do! RTL.act (fun () -> promise { pointerEvent "pointerMove" (unbox document) 20 y 1 })
    do! RTL.act (fun () -> promise { pointerEvent "pointerUp" (unbox document) 20 y 0 })
}

Vitest.test (
    "pointer drag drops an item at the target position",
    fun () -> promise {
        renderSample ()
        do! dragTo 140
        expectOrder [| "beta"; "gamma"; "alpha" |]
    }
)

Vitest.test (
    "dropping outside the list keeps the order",
    fun () -> promise {
        renderSample ()
        do! dragTo 500
        expectOrder [| "alpha"; "beta"; "gamma" |]
    }
)

Vitest.test (
    "parent owns the items and receives the reordered payloads",
    fun () ->
        let mutable changed: SortableListItem<{| description: string |}>[] option = None

        let rendered =
            RTL.render (SortableList.SortableList(sampleItems, fun items -> changed <- Some items))

        click "sortable-list-down-alpha"
        // A notification must not change the rendered order until the parent supplies it.
        expectOrder [| "alpha"; "beta"; "gamma" |]
        Vitest.expect(changed |> Option.map (Array.map _.id)).toEqual (Some [| "beta"; "alpha"; "gamma" |])
        Vitest.expect(changed.Value.[1].data).toEqual (Some {| description = "First sample" |})
        rendered.rerender (SortableList.SortableList(changed.Value, ignore))
        expectOrder [| "beta"; "alpha"; "gamma" |]
)

Vitest.test (
    "duplicate labels have independent identities",
    fun () ->
        let mutable changed = [||]

        let items =
            sampleItems |> Array.map (fun item -> {| item with label = "Same label" |})

        RTL.render (SortableList.SortableList(items, fun items -> changed <- items))
        |> ignore

        click "sortable-list-remove-beta"
        Vitest.expect(changed |> Array.map _.id).toEqual [| "alpha"; "gamma" |]
)

Vitest.test (
    "shift-click selects the inclusive range after the last interaction",
    fun () ->
        RTL.render (SelectableSample []) |> ignore

        click "sortable-list-row-alpha"
        shiftClick "sortable-list-row-gamma"

        Vitest.expect((RTL.screen.getByTestId "sortable-list-row-alpha").classList.contains "swt:bg-base-300").toBe true

        Vitest.expect((RTL.screen.getByTestId "sortable-list-row-beta").classList.contains "swt:bg-base-300").toBe true

        Vitest.expect((RTL.screen.getByTestId "sortable-list-row-gamma").classList.contains "swt:bg-base-300").toBe true
)

Vitest.test (
    "reverse shift-click preserves selected items outside the range without duplicates",
    fun () ->
        RTL.render (SelectableSample [ "alpha" ]) |> ignore

        click "sortable-list-row-delta"
        shiftClick "sortable-list-row-beta"

        Vitest.expect((RTL.screen.getByTestId "selected-ids").textContent.Split(',') |> Array.sort).toEqual [|
            "alpha"
            "beta"
            "delta"
            "gamma"
        |]
)

Vitest.test (
    "custom row controls reorder and remove without triggering the row click",
    fun () ->
        let mutable clicked = false
        let mutable changed = [||]

        RTL.render (
            SortableList.SortableList(
                sampleItems,
                (fun items -> changed <- items),
                rowProps = (fun _ -> [ prop.onClick (fun _ -> clicked <- true) ]),
                renderRow =
                    (fun row ->
                        Html.td [
                            prop.children [
                                Html.span [
                                    prop.testId $"custom-label-{row.item.id}"
                                    prop.text row.item.label
                                ]
                                RowComponents.DragHandle(row)
                                RowComponents.MoveUpButton(row)
                                RowComponents.MoveDownButton(row)
                                RowComponents.RemoveButton(row)
                            ]
                        ]
                    )
            )
        )
        |> ignore

        click "sortable-list-down-alpha"
        Vitest.expect(changed |> Array.map _.id).toEqual [| "beta"; "alpha"; "gamma" |]
        click "sortable-list-up-beta"
        Vitest.expect(changed |> Array.map _.id).toEqual [| "beta"; "alpha"; "gamma" |]
        click "sortable-list-remove-beta"
        Vitest.expect(changed |> Array.map _.id).toEqual [| "alpha"; "gamma" |]
        click "sortable-list-drag-alpha"
        Vitest.expect(clicked).toBe false
        click "custom-label-alpha"
        Vitest.expect(clicked).toBe true
)

Vitest.test (
    "custom rows retain pointer drag behavior and optional data",
    fun () -> promise {
        RTL.render (SortableListSample.CustomRowSample()) |> ignore
        Vitest.expect((RTL.screen.getByTestId "sortable-list-custom-alpha").textContent).toBe "AlphaFirst sample"
        Vitest.expect((RTL.screen.getByTestId "sortable-list-custom-gamma").textContent).toBe "GammaNo extra data"
        do! dragTo 140
        expectOrder [| "beta"; "gamma"; "alpha" |]
    }
)

Vitest.test (
    "custom inline row styles survive sortable transforms",
    fun () ->
        RTL.render (
            SortableList.SortableList(
                sampleItems,
                ignore,
                rowProps =
                    fun _ -> [
                        prop.style [ style.backgroundColor "red"; style.height 80 ]
                    ]
            )
        )
        |> ignore

        let row = RTL.screen.getByTestId "sortable-list-row-alpha"
        let rowStyle = readRowStyle row
        Vitest.expect(rowStyle.backgroundColor).toBe "rgb(255, 0, 0)"
        Vitest.expect(rowStyle.height).toBe "80px"
)
