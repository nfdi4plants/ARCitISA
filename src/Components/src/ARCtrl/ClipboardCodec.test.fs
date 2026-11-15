module internal Swate.Components.ClipboardCodecTests

open ARCtrl
open Swate.Components.ClipboardCodec
open Vitest

Vitest.test (
    "typed clipboard round-trips all cell kinds and their ontology and data metadata",
    fun () ->
        let cells = [|
            [|
                CompositeCell.FreeText "plain"
                CompositeCell.createTermFromString ("term", "TST", "TST:1")
                CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                CompositeCell.createDataFromString ("file.txt#row=2", format = "text/csv", selectorFormat = "fragment")
            |]
        |]

        let decoded = cells |> createPayload |> encode |> tryDecode |> Option.get
        Vitest.expect(decoded.Rows |> Array.map (Array.map toCompositeCell)).toEqual cells
)

for name, json in
    [
        "null cell", """{"Version":1,"Rows":[[null]]}"""
        "missing fields", """{"Version":1,"Rows":[[{"Kind":"term","Name":"incomplete"}]]}"""
        "unknown kind", """{"Version":1,"Rows":[[{"Kind":"unknown"}]]}"""
        "non-string field", """{"Version":1,"Rows":[[{"Kind":"freetext","Value":42}]]}"""
        "empty row", """{"Version":1,"Rows":[[{"Kind":"freetext","Value":"A"}],[]]}"""
        "ragged rows",
        """{"Version":1,"Rows":[[{"Kind":"freetext","Value":"A"}],[{"Kind":"freetext","Value":"B"},{"Kind":"freetext","Value":"C"}]]}"""
        "unsupported version", """{"Version":2,"Rows":[[{"Kind":"freetext","Value":"A"}]]}"""
    ] do
    Vitest.test ($"typed clipboard rejects {name}", fun () -> Vitest.expect(tryDecode json).toEqual None)
