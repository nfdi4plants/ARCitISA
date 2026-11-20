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

let private createClassTable () =
    let table = ArcTable.init "Classes"
    table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "in" ])

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation("A")),
        ResizeArray [ CompositeCell.Term(OntologyAnnotation("a")) ]
    )

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation("B")),
        ResizeArray [ CompositeCell.Term(OntologyAnnotation("b")) ]
    )

    table.AddColumn(CompositeHeader.Output IOType.Sample, ResizeArray [ CompositeCell.FreeText "out" ])
    table

let private isDisabled (id: string) =
    (RTL.screen.getByTestId id :?> HTMLButtonElement).disabled

Vitest.test (
    "columns cannot move across column classes",
    fun () ->
        RTL.render (Modal(createClassTable (), None, ignore, ignore)) |> ignore

        for id in
            [
                "up-column-3"
                "up-column-1"
                "down-column-0"
                "down-column-2"
            ] do
            Vitest.expect(isDisabled $"sortable-list-{id}").toBe true

        for id in [ "down-column-1"; "up-column-2" ] do
            Vitest.expect(isDisabled $"sortable-list-{id}").toBe false
)

Vitest.test (
    "two parameter columns swap and Submit applies it",
    fun () ->
        let table = createClassTable ()
        let mutable updated = None

        RTL.render (Modal(table, None, (fun next -> updated <- Some next), ignore))
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-down-column-1")
        click "Submit"

        for newIndex, oldIndex in [ 0, 0; 1, 2; 2, 1; 3, 3 ] do
            Vitest.expect(updated.Value.GetColumn(newIndex)).toEqual (table.GetColumn(oldIndex))
)

Vitest.test (
    "an unsorted table starts in the order the file stores and Submit applies it",
    fun () ->
        let table = ArcTable.init "Unsorted"

        table.AddColumn(
            CompositeHeader.Parameter(OntologyAnnotation("A")),
            ResizeArray [ CompositeCell.Term(OntologyAnnotation("a")) ]
        )

        table.AddColumn(CompositeHeader.Output IOType.Sample, ResizeArray [ CompositeCell.FreeText "out" ])
        table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "in" ])
        let mutable updated = None

        RTL.render (Modal(table, None, (fun next -> updated <- Some next), ignore))
        |> ignore

        click "Submit"

        for newIndex, oldIndex in [ 0, 2; 1, 0; 2, 1 ] do
            Vitest.expect(updated.Value.GetColumn(newIndex)).toEqual (table.GetColumn(oldIndex))
)

for pattern, replacement, expected in
    [
        "^kos1", "M_kos1", [| "M_kos1/kos1"; "M_kos1/kos1_1"; "M_kos1/kos1_2" |]
        "kos1$", "kos1_M", [| "kos1/kos1_M"; "kos1_2/kos1_M"; "other" |]
        "kos[12]", "sample", [| "sample/sample_1"; "sample/sample_2"; "other" |]
        "^", "M_", [| "M_kos1/kos1"; "M_kos1/kos1_1"; "M_kos1/kos1_2" |]
        "$", "_M", [| "kos1/kos1_M"; "kos1/kos1_1_M"; "kos1/kos1_2_M" |]
    ] do
    Vitest.test (
        $"Update Rows respects regex {pattern} in preview and submission",
        fun () ->
            let values =
                if pattern = "kos1$" then
                    [| "kos1/kos1"; "kos1_2/kos1"; "other" |]
                elif pattern = "kos[12]" then
                    [| "kos1/kos2_1"; "kos2/kos1_2"; "other" |]
                else
                    [| "kos1/kos1"; "kos1/kos1_1"; "kos1/kos1_2" |]

            let table = ArcTable.init "Samples"
            let header = CompositeHeader.Output IOType.Sample
            let cells = values |> Array.map CompositeCell.FreeText |> ResizeArray
            table.AddColumn(header, cells)
            let original = table.Copy().GetColumn(0)
            let mutable updated: CompositeColumn option = None
            let mutable closed = false

            RTL.render (
                UpdateColumnModal.UpdateColumnModal(
                    0,
                    table,
                    (fun column -> updated <- Some column),
                    (fun () -> closed <- true)
                )
            )
            |> ignore

            let inputs = RTL.screen.getAllByRole "textbox"
            RTL.fireEvent.change (inputs.[0], {| target = {| value = pattern |} |})
            RTL.fireEvent.change (inputs.[1], {| target = {| value = replacement |} |})

            let rows = RTL.screen.getAllByRole "row"

            for i in 0 .. expected.Length - 1 do
                Vitest.expect(rows.[i + 1].textContent).toBe ($"{i}{values.[i]}{expected.[i]}")

            Vitest.expect(updated).toEqual None
            Vitest.expect(table.GetColumn(0)).toEqual original
            click "Submit"

            Vitest
                .expect(updated.Value)
                .toEqual (CompositeColumn.create (header, expected |> Array.map CompositeCell.FreeText |> ResizeArray))

            Vitest.expect(table.GetColumn(0)).toEqual original
            Vitest.expect(closed).toBe true
    )
