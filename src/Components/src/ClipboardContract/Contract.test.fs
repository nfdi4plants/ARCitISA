module internal Swate.Components.ClipboardContract.ContractTests

open ARCtrl
open Swate.Components
open Swate.Components.ClipboardContract.Contract
open Vitest

for newline in [ "\n"; "\r\n"; "\r" ] do
    Vitest.test (
        sprintf "plain text preserves internal blank rows and cells with newline %A" newline,
        fun () ->
            let text = String.concat newline [ "A\t"; ""; "B\tC"; "" ]
            Vitest.expect(PlainText.parseRows text).toEqual [| [| "A"; "" |]; [| ""; "" |]; [| "B"; "C" |] |]
    )

Vitest.test (
    "capture sorts selected cells into grid order and keeps each visible value in one clipboard column",
    fun () ->
        let coordinates: CellCoordinate list = [
            {| x = 2; y = 2 |}
            {| x = 1; y = 1 |}
            {| x = 2; y = 1 |}
            {| x = 1; y = 2 |}
            {| x = 1; y = 1 |}
        ]

        let cells = [|
            [|
                CompositeCell.createDataFromString "file.txt#row=2"
                CompositeCell.createTermFromString ("term", "TST", "TST:1")
            |]
            [|
                CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                CompositeCell.FreeText "label"
            |]
        |]

        let captured = Capture.matrix (fun c -> cells.[c.y - 1].[c.x - 1]) coordinates
        Vitest.expect(captured).toEqual cells

        Vitest
            .expect(Capture.toPlainText captured)
            .toBe ("file.txt#row=2\tterm" + System.Environment.NewLine + "4 metre\tlabel")

        Vitest.expect(Capture.matrix (fun _ -> failwith "No cells should be read") []).toEqual [||]
)

Vitest.test (
    "mapping repeats a matrix across the selection independently of which selected cell opened the menu",
    fun () ->
        let source = [| [| "A"; "B" |]; [| "C"; "D" |] |]

        let selection: CellCoordinate[] = [|
            for y in 4..5 do
                for x in 3..5 -> {| x = x; y = y |}
        |]

        for anchor in selection do
            let mapped = Mapping.map source anchor (Array.rev selection)
            Vitest.expect(mapped |> Array.map _.Target).toEqual selection
            Vitest.expect(mapped |> Array.map _.Source).toEqual [| "A"; "B"; "A"; "C"; "D"; "C" |]
)

Vitest.test (
    "mapping expands a single-cell target to the source dimensions",
    fun () ->
        let anchor: CellCoordinate = {| x = 3; y = 4 |}
        let mapped = Mapping.map [| [| "A"; "B" |]; [| "C"; "D" |] |] anchor [| anchor |]

        Vitest.expect(mapped |> Array.map (fun cell -> cell.Target.x, cell.Target.y, cell.Source)).toEqual [|
            3, 4, "A"
            4, 4, "B"
            3, 5, "C"
            4, 5, "D"
        |]
)

for (source: string[][]) in [ [||]; [| [||] |]; [| [| "A" |]; [| "B"; "C" |] |] ] do
    Vitest.test (
        sprintf "mapping rejects a nonrectangular or empty source %A" source,
        fun () ->
            Vitest.expect(fun () -> Mapping.map source {| x = 1; y = 1 |} [||] |> ignore).toThrowError
                "The clipboard source must be a non-empty rectangular matrix."
    )
