module internal Swate.Components.Composite.DataMapTable.HelperTests

open ARCtrl
open Vitest
open Swate.Components.Composite.DataMapTable.Helper

Vitest.test (
    "duplicates selected DataMap rows below each original and preserves all context metadata",
    fun () ->
        let dataMap = Datamap.init ()

        for i in 1..3 do
            dataMap.DataContexts.Add(
                DataContext(
                    name = $"data.csv#row={i}",
                    format = "text/csv",
                    selectorFormat = "text/csv",
                    label = $"Label {i}",
                    description = $"Description {i}",
                    generatedBy = "analysis",
                    explication = OntologyAnnotation("Measurement"),
                    unit = OntologyAnnotation("Celsius"),
                    objectType = OntologyAnnotation("Number"),
                    comments = ResizeArray [ Comment("Note", "original") ]
                )
            )

        let mutable result = None

        duplicateRows
            dataMap
            [
                {| x = 1; y = 3 |}
                {| x = 2; y = 1 |}
                {| x = 3; y = 1 |}
            ]
            (fun next -> result <- Some next)

        let result = result.Value

        Vitest.expect(result.RowCount).toBe 5

        Vitest.expect(result.DataContexts |> Seq.map _.Label |> Seq.toArray).toEqual [|
            Some "Label 1"
            Some "Label 1"
            Some "Label 2"
            Some "Label 3"
            Some "Label 3"
        |]

        for originalIndex, duplicateIndex in [ 0, 1; 2, 4 ] do
            let source = dataMap.DataContexts.[originalIndex]
            let duplicate = result.DataContexts.[duplicateIndex]
            Vitest.expect(duplicate).toEqual source
            Vitest.expect(duplicate).not.toBe source
            duplicate.Explication.Value.Name <- Some "Edited explication"
            duplicate.Unit.Value.Name <- Some "Edited unit"
            duplicate.ObjectType.Value.Name <- Some "Edited type"
            duplicate.Comments.[0].Value <- Some "Edited comment"
            Vitest.expect(source.Explication.Value.NameText).toBe "Measurement"
            Vitest.expect(source.Unit.Value.NameText).toBe "Celsius"
            Vitest.expect(source.ObjectType.Value.NameText).toBe "Number"
            Vitest.expect(source.Comments.[0].Value).toEqual (Some "original")

        Vitest.expect(dataMap.RowCount).toBe 3
)

Vitest.test (
    "ignores header and invalid row coordinates when duplicating a DataMap row",
    fun () ->
        let dataMap =
            Datamap(ResizeArray [ DataContext(name = "data.csv", label = "Data") ])

        let mutable result = None

        duplicateRows
            dataMap
            [
                {| x = 0; y = 0 |}
                {| x = 1; y = 1 |}
                {| x = 1; y = 9 |}
            ]
            (fun next -> result <- Some next)

        Vitest.expect(result.Value.RowCount).toBe 2
        Vitest.expect(result.Value.DataContexts.[1].Label).toEqual (Some "Data")
        Vitest.expect(result.Value.DataContexts.[1].Unit).toEqual None
        Vitest.expect(dataMap.RowCount).toBe 1
)
