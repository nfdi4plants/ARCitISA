module internal Swate.Components.Composite.AnnotationTable.HelperTests

open ARCtrl
open Vitest
open Swate.Components.Composite.AnnotationTable.Helper

let private createTable () =
    let table = ArcTable.init "Duplication"

    table.AddColumn(
        CompositeHeader.Input IOType.Source,
        ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"source-{i}" ]
    )

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation("Temperature")),
        ResizeArray [
            for i in 1..3 -> CompositeCell.Unitized(string i, OntologyAnnotation("Celsius"))
        ]
    )

    table.AddColumn(
        CompositeHeader.FreeText "Notes",
        ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"note-{i}" ]
    )

    table

let private columnValues (table: ArcTable) column = [|
    for row in 0 .. table.RowCount - 1 -> table.GetCellAt(column, row).ToString()
|]

Vitest.test (
    "duplicates each selected row once immediately below its source without changing the original",
    fun () ->
        let table = createTable ()

        let result =
            duplicateRows table [
                {| x = 1; y = 1 |}
                {| x = 2; y = 1 |}
                {| x = 2; y = 3 |}
            ]

        Vitest.expect(columnValues result 0).toEqual [|
            "source-1"
            "source-1"
            "source-2"
            "source-3"
            "source-3"
        |]

        Vitest.expect(result.GetRow(1)).toEqual (table.GetRow(0))
        Vitest.expect(result.GetRow(4)).toEqual (table.GetRow(2))
        Vitest.expect(table.RowCount).toBe 3
        result.SetCellAt(2, 1, CompositeCell.FreeText "edited duplicate")
        Vitest.expect(result.GetCellAt(2, 0).AsFreeText).toBe "note-1"
        Vitest.expect(table.GetCellAt(2, 1).AsFreeText).toBe "note-2"
)

Vitest.test (
    "row duplication ignores the header and invalid rows",
    fun () ->
        let table = createTable ()

        let result =
            duplicateRows table [
                {| x = 0; y = 0 |}
                {| x = 0; y = 2 |}
                {| x = 1; y = 9 |}
            ]

        Vitest.expect(columnValues result 0).toEqual [| "source-1"; "source-2"; "source-2"; "source-3" |]
)

Vitest.test (
    "duplicates entire columns immediately to the right, including headers and units",
    fun () ->
        let table = createTable ()

        let result =
            duplicateColumns table [
                {| x = 2; y = 0 |}
                {| x = 2; y = 1 |}
                {| x = 3; y = 2 |}
            ]

        Vitest.expect(result.ColumnCount).toBe 5

        Vitest.expect(result.Headers |> Seq.map _.ToString() |> Seq.toArray).toEqual [|
            "Input [Source Name]"
            "Parameter [Temperature]"
            "Parameter [Temperature]"
            "Notes"
            "Notes"
        |]

        Vitest.expect(result.GetColumn(2)).toEqual (table.GetColumn(1))
        Vitest.expect(result.GetColumn(4)).toEqual (table.GetColumn(2))
        Vitest.expect(table.ColumnCount).toBe 3
        result.Headers.[2].TryGetTerm().Value.Name <- Some "Changed header"
        Vitest.expect(table.Headers.[1].TryGetTerm().Value.NameText).toBe "Temperature"
)

Vitest.test (
    "duplicates an empty column without adding a row",
    fun () ->
        let table = ArcTable.init "Empty"
        table.AddColumn(CompositeHeader.Parameter(OntologyAnnotation("Temperature")))
        let result = duplicateColumns table [ {| x = 1; y = 0 |} ]
        Vitest.expect(result.ColumnCount).toBe 2
        Vitest.expect(result.RowCount).toBe 0
        Vitest.expect(table.ColumnCount).toBe 1
)

Vitest.test (
    "does not offer duplication for unique headers or mixed selections containing one",
    fun () ->
        let table = createTable ()
        table.AddColumn(CompositeHeader.Comment "Comment", ResizeArray())

        for column in [ 1; 4 ] do
            let coordinates = [ {| x = column; y = 1 |}; {| x = 2; y = 1 |} ]
            Vitest.expect(canDuplicateColumns table coordinates).toBe false

            Vitest.expect(fun () -> duplicateColumns table coordinates |> ignore).toThrowError
                "Select columns whose headers can occur more than once."

        Vitest.expect(canDuplicateColumns table [ {| x = 2; y = 0 |} ]).toBe true
        Vitest.expect(canDuplicateColumns table [ {| x = 0; y = 1 |} ]).toBe false
        Vitest.expect(table.ColumnCount).toBe 4
)
