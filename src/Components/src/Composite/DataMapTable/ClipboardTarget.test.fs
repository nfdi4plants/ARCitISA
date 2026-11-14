module internal Swate.Components.Composite.DataMapTable.ClipboardTargetTests

open ARCtrl
open Vitest
open Swate.Components.Composite.DataMapTable.ClipboardTarget

let private emptyMap () = Datamap(ResizeArray [ DataContext() ])

let private termValues (dataMap: Datamap) row = [|
    for column in 4..6 -> dataMap.GetCell(column, row).ToString()
|]

Vitest.test (
    "plain TSV keeps DataMap term cells separate and clips overflowing columns while growing rows",
    fun () ->
        let dataMap = emptyMap ()
        let anchor = {| x = 5; y = 1 |}
        dataMap.PasteTabText(anchor, [| anchor |], "foo\tbar\tbaz")
        Vitest.expect(termValues dataMap 0).toEqual [| "foo"; "bar"; "baz" |]
        Vitest.expect(dataMap.DataContexts.[0].Explication.Value.TermSourceREF).toEqual None

        let nextAnchor = {| x = 6; y = 2 |}
        dataMap.PasteTabText(nextAnchor, [| nextAnchor |], "one\ttwo\tthree\tfour")
        Vitest.expect(dataMap.RowCount).toBe 2
        Vitest.expect(termValues dataMap 1).toEqual [| ""; "one"; "two" |]
)

Vitest.test (
    "plain DataMap paste repeats a source matrix from the selection origin even when clicked at its end",
    fun () ->
        let dataMap = Datamap(ResizeArray [ DataContext(); DataContext() ])

        let selection = [|
            for row in 1..2 do
                for column in 5..7 -> {| x = column; y = row |}
        |]

        dataMap.PasteTabText(selection.[5], selection, "alpha\tbeta")

        for row in 0..1 do
            Vitest.expect(termValues dataMap row).toEqual [| "alpha"; "beta"; "alpha" |]
)

Vitest.test (
    "plain DataMap grid paste preserves selectors and internal empty rows while growing the map",
    fun () ->
        let dataMap = emptyMap ()
        let anchor = {| x = 1; y = 1 |}
        dataMap.PasteTabText(anchor, [| anchor |], "first.txt#row=2\tFirst\n\nsecond.txt#row=3\tSecond\n")
        Vitest.expect(dataMap.RowCount).toBe 3
        Vitest.expect(dataMap.DataContexts.[0].Selector).toEqual (Some "row=2")
        Vitest.expect(dataMap.GetCell(0, 1).ToString()).toBe ""
        Vitest.expect(dataMap.DataContexts.[2].FilePath).toEqual (Some "second.txt")
        Vitest.expect(dataMap.DataContexts.[2].Selector).toEqual (Some "row=3")
        Vitest.expect(dataMap.DataContexts.[2].Label).toEqual (Some "Second")
)

Vitest.test (
    "structured DataMap paste preserves term identity and extracts ontology metadata from unitized cells",
    fun () ->
        let dataMap = emptyMap ()
        let anchor = {| x = 5; y = 1 |}
        let unit = CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")

        dataMap.PasteStructuredCells(
            anchor,
            [| anchor |],
            [|
                [|
                    CompositeCell.createTermFromString ("explicit", "TST", "TST:1")
                    unit
                    unit
                |]
            |]
        )

        let term = dataMap.DataContexts.[0].Explication.Value
        Vitest.expect(term.NameText).toBe "explicit"
        Vitest.expect(term.TermSourceREF).toEqual (Some "TST")
        Vitest.expect(term.TermAccessionNumber).toEqual (Some "TST:1")

        for target in
            [
                dataMap.DataContexts.[0].Unit.Value
                dataMap.DataContexts.[0].ObjectType.Value
            ] do
            Vitest.expect(target.NameText).toBe "metre"
            Vitest.expect(target.TermSourceREF).toEqual (Some "UO")
            Vitest.expect(target.TermAccessionNumber).toEqual (Some "UO:0000008")
)

for column in 5..7 do
    for name, source, expected in
        [
            "text", CompositeCell.FreeText "replacement", "replacement"
            "data", CompositeCell.createDataFromString "file.txt#row=2", "file.txt#row=2"
            "unit", CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008"), "metre"
        ] do
        Vitest.test (
            $"structured {name} replaces DataMap ontology identity in term column {column}",
            fun () ->
                let oldTerm () =
                    OntologyAnnotation("old", "OLD", "OLD:1")

                let dataMap =
                    Datamap(
                        ResizeArray [
                            DataContext(explication = oldTerm (), unit = oldTerm (), objectType = oldTerm ())
                        ]
                    )

                let anchor = {| x = column; y = 1 |}
                dataMap.PasteStructuredCells(anchor, [| anchor |], [| [| source |] |])
                let term = dataMap.GetCell(column - 1, 0).AsTerm
                Vitest.expect(term.NameText).toBe expected
                Vitest.expect(term.TermSourceREF).toEqual (if name = "unit" then Some "UO" else None)
                Vitest.expect(term.TermAccessionNumber).toEqual (if name = "unit" then Some "UO:0000008" else None)
        )

Vitest.test (
    "structured unitized DataMap paste into free text keeps its complete visible value",
    fun () ->
        let dataMap = emptyMap ()
        let anchor = {| x = 2; y = 1 |}

        dataMap.PasteStructuredCells(
            anchor,
            [| anchor |],
            [|
                [|
                    CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                |]
            |]
        )

        Vitest.expect(dataMap.DataContexts.[0].Label).toEqual (Some "4 metre")
)

Vitest.test (
    "structured DataMap paste grows rows and preserves data selectors and formats",
    fun () ->
        let dataMap = emptyMap ()
        let anchor = {| x = 1; y = 2 |}

        let source =
            Data(name = "data.csv#row=2", format = "text/csv", selectorFormat = "text/csv")

        dataMap.PasteStructuredCells(anchor, [| anchor |], [| [| CompositeCell.Data source |] |])
        Vitest.expect(dataMap.RowCount).toBe 2
        Vitest.expect(dataMap.DataContexts.[1].FilePath).toEqual (Some "data.csv")
        Vitest.expect(dataMap.DataContexts.[1].Selector).toEqual (Some "row=2")
        Vitest.expect(dataMap.DataContexts.[1].Format).toEqual (Some "text/csv")
        Vitest.expect(dataMap.DataContexts.[1].SelectorFormat).toEqual (Some "text/csv")
)
