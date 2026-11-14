module internal Swate.Components.Composite.AnnotationTable.ModalTests

open ARCtrl
open Browser.Types
open Feliz
open Swate.Components
open Swate.Components.Composite.Table.Types
open Vitest

let private createTable () =
    let table = ArcTable.init "Reorder"

    for name in [ "First"; "Second"; "Third"; "Fourth" ] do
        table.AddColumn(CompositeHeader.FreeText name, ResizeArray [ CompositeCell.FreeText $"{name} value" ])

    table

[<ReactComponent>]
let private Modal
    (table: ArcTable, selectedRange: CellCoordinateRange option, submit: ArcTable -> unit, close: unit -> unit)
    =
    let selectHandle =
        SelectHandle(
            (fun _ -> selectedRange.IsSome),
            ignore,
            ignore,
            (fun () -> selectedRange),
            (fun () -> ResizeArray()),
            (fun () -> 0)
        )

    let tableRef = React.useRef (TableHandle(ignore, ignore, selectHandle))

    ContextMenuModals.MoveColumnModal(
        table,
        submit,
        {| x = 2; y = 0 |},
        {| x = 2; y = 0 |},
        (fun _ -> close ()),
        tableRef
    )

let private click label =
    RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text label)))

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "sortable column moves stay pending until Submit and retain all column values",
    fun () ->
        let table = createTable ()
        let mutable updated = None
        let mutable closed = false

        RTL.render (Modal(table, None, (fun next -> updated <- Some next), (fun () -> closed <- true)))
        |> ignore

        Vitest.expect(RTL.screen.queryByRole("spinbutton").IsNone).toBe true
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Remove Second")).IsNone).toBe true
        click "Move Second down"
        click "Move Second down"
        Vitest.expect(updated).toEqual None
        Vitest.expect(table.Headers.[1].ToString()).toBe "Second"
        click "Submit"

        let result = updated.Value

        for newIndex, oldIndex in [ 0, 0; 1, 2; 2, 3; 3, 1 ] do
            Vitest.expect(result.GetColumn(newIndex)).toEqual (table.GetColumn(oldIndex))

        Vitest.expect(result.Name).toBe "Reorder"
        Vitest.expect(closed).toBe true
)

Vitest.test (
    "Cancel discards pending column moves",
    fun () ->
        let table = createTable ()
        let mutable submitted = false
        let mutable closed = false

        RTL.render (Modal(table, None, (fun _ -> submitted <- true), (fun () -> closed <- true)))
        |> ignore

        click "Move Second up"
        click "Cancel"
        Vitest.expect(submitted).toBe false
        Vitest.expect(closed).toBe true

        Vitest.expect(table.Headers |> Seq.map _.ToString() |> Seq.toArray).toEqual [|
            "First"
            "Second"
            "Third"
            "Fourth"
        |]
)

Vitest.test (
    "selected columns move together and keep their internal order",
    fun () ->
        let table = createTable ()
        let mutable updated = None

        let range =
            Some {|
                xStart = 2
                xEnd = 3
                yStart = 0
                yEnd = 1
            |}

        RTL.render (Modal(table, range, (fun next -> updated <- Some next), ignore))
        |> ignore

        click "Move Second, Third up"
        click "Submit"

        for newIndex, oldIndex in [ 0, 1; 1, 2; 2, 0; 3, 3 ] do
            Vitest.expect(updated.Value.GetColumn(newIndex)).toEqual (table.GetColumn(oldIndex))
)

Vitest.test (
    "submitting without a move preserves the original order",
    fun () ->
        let table = createTable ()
        let mutable updated = None

        RTL.render (Modal(table, None, (fun next -> updated <- Some next), ignore))
        |> ignore

        click "Submit"

        for index in 0 .. table.ColumnCount - 1 do
            Vitest.expect(updated.Value.GetColumn(index)).toEqual (table.GetColumn(index))
)

Vitest.test (
    "columns with identical headers remain distinct when reordered",
    fun () ->
        let table = createTable ()
        table.AddColumn(CompositeHeader.FreeText "Second", ResizeArray [ CompositeCell.FreeText "other value" ])
        let mutable updated = None

        RTL.render (Modal(table, None, (fun next -> updated <- Some next), ignore))
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-up-column-4")
        click "Submit"
        Vitest.expect(updated.Value.ColumnCount).toBe 5
        Vitest.expect(updated.Value.GetColumn(1)).toEqual (table.GetColumn(1))
        Vitest.expect(updated.Value.GetColumn(3)).toEqual (table.GetColumn(4))
)

Vitest.test (
    "selecting every column keeps one group with disabled movement controls",
    fun () ->
        let table = createTable ()

        let range =
            Some {|
                xStart = 1
                xEnd = 4
                yStart = 0
                yEnd = 1
            |}

        let mutable updated = None

        RTL.render (Modal(table, range, (fun next -> updated <- Some next), ignore))
        |> ignore

        for direction in [ "up"; "down" ] do
            let control =
                RTL.screen.getByTestId $"sortable-list-{direction}-column-0" :?> HTMLButtonElement

            Vitest.expect(control.disabled).toBe true

        click "Submit"

        for index in 0 .. table.ColumnCount - 1 do
            Vitest.expect(updated.Value.GetColumn(index)).toEqual (table.GetColumn(index))
)
