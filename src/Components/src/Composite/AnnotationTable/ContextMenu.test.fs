module internal Swate.Components.Composite.AnnotationTable.ContextMenuUnitTests

open ARCtrl
open Vitest
open Swate.Components
open Swate.Components.Composite.Table.Types
open Swate.Components.Composite.AnnotationTable.Types.AnnotationTableContextMenu

let private coordinate x y : CellCoordinate = {| x = x; y = y |}

let private selection (cells: CellCoordinate[]) =
    SelectHandle(
        (fun cell -> Array.contains cell cells),
        ignore,
        ignore,
        (fun () -> None),
        (fun () -> ResizeArray cells),
        (fun () -> cells.Length)
    )

let private targetUnit () =
    OntologyAnnotation("degree Celsius", "UO", "UO:0000027")

let private table () =
    let table = ArcTable.init "Paste target"

    table.AddColumn(
        CompositeHeader.FreeText "Label",
        ResizeArray [ CompositeCell.FreeText "A"; CompositeCell.FreeText "B" ]
    )

    table.AddColumn(
        CompositeHeader.Characteristic(OntologyAnnotation "Instrument"),
        ResizeArray [
            CompositeCell.Term(OntologyAnnotation "old")
            CompositeCell.Term(OntologyAnnotation "old")
        ]
    )

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation "Temperature"),
        ResizeArray [
            CompositeCell.createUnitized ("1", targetUnit ())
            CompositeCell.Term(targetUnit ())
        ]
    )

    table

let private pastePlain anchor selected text target =
    let mutable result = None

    AnnotationTableContextMenuUtil.applyPlainTextCells (
        anchor,
        target,
        selection selected,
        text,
        (fun next -> result <- Some next)
    )

    result.Value

let private pasteStructured anchor selected cells target =
    let mutable result = None

    AnnotationTableContextMenuUtil.applyStructuredCells (
        anchor,
        target,
        selection selected,
        cells,
        (fun next -> result <- Some next)
    )

    result.Value

for unitized in [ false; true ] do
    let kind = if unitized then "unit" else "term"

    Vitest.test (
        $"paste prediction recognizes a complete {kind} column",
        fun () ->
            let anchor = coordinate 2 1

            let expected =
                if unitized then
                    CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                else
                    CompositeCell.createTermFromString ("instrument", "MS", "MS:1000031")

            let source = ArcTable.init "Copied column"
            source.AddColumn(CompositeHeader.Component(OntologyAnnotation "Instrument"), ResizeArray [ expected ])
            let data = source.ToStringSeqs()

            match
                AnnotationTableContextMenuUtil.predictPasteBehaviour (anchor, table (), selection [| anchor |], data)
            with
            | AddColumns prediction ->
                Vitest.expect(prediction.coordinate).toEqual anchor
                Vitest.expect(prediction.coordinates).toEqual [| [| anchor |] |]
                Vitest.expect(prediction.data.Count).toBe 1

                Vitest
                    .expect(prediction.data.[0].Header)
                    .toEqual (CompositeHeader.Component(OntologyAnnotation "Instrument"))

                Vitest.expect(prediction.data.[0].Cells.Count).toBe 1
                // Spreadsheet export canonicalizes accession URLs; ARCtrl equality compares ontology identity.
                Vitest.expect(prediction.data.[0].Cells.[0] = expected).toBe true
            | _ -> failwith "Expected an add-column prediction"
    )

Vitest.test (
    "paste prediction reports empty body data as unknown",
    fun () ->
        let target = table ()
        let anchor = coordinate 2 1
        let data = [| [| "" |] |]

        match AnnotationTableContextMenuUtil.predictPasteBehaviour (anchor, target, selection [| anchor |], data) with
        | Unknown prediction ->
            Vitest.expect(prediction.data).toEqual data
            Vitest.expect(prediction.headers).toEqual [| target.Headers.[1] |]
        | _ -> failwith "Expected an unknown paste prediction"
)

for column, data, expected in
    [
        2, [| "instrument"; "MS"; "MS:1000031" |], CompositeCell.createTermFromString ("instrument", "MS", "MS:1000031")
        3,
        [| "4"; "metre"; "UO"; "UO:0000008" |],
        CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
    ] do
    Vitest.test (
        $"paste prediction uses clicked column {column} outside a stale selection",
        fun () ->
            let target = table ()
            let anchor = coordinate column 1

            match
                AnnotationTableContextMenuUtil.predictPasteBehaviour (
                    anchor,
                    target,
                    selection [| coordinate 1 1 |],
                    [| data |]
                )
            with
            | PasteCells prediction ->
                Vitest.expect(prediction.coordinates).toEqual [| [| anchor |] |]
                Vitest.expect(prediction.data.Count).toBe 1
                Vitest.expect(prediction.data.[0].Header).toEqual target.Headers.[column - 1]
                Vitest.expect(prediction.data.[0].Cells |> Seq.toArray).toEqual [| expected |]
            | _ -> failwith "Expected a body-cell paste prediction"
    )

for structured in [ false; true ] do
    Vitest.test (
        $"numeric paste preserves target unit metadata (structured={structured})",
        fun () ->
            let target = table ()
            let anchor = coordinate 3 1
            let selected = [| anchor; coordinate 3 2 |]

            let next =
                if structured then
                    pasteStructured anchor selected [| [| CompositeCell.createUnitizedFromString "4" |] |] target
                else
                    pastePlain anchor selected "4" target

            for row in 0..1 do
                let value, unit = next.GetCellAt(2, row).AsUnitized
                Vitest.expect(value).toBe "4"
                Vitest.expect(unit).toEqual (targetUnit ())

            Vitest.expect(target.GetCellAt(2, 0).AsUnitized |> fst).toBe "1"
    )

Vitest.test (
    "structured paste preserves ontology identity and complete unitized free text",
    fun () ->
        let target = table ()
        let term = CompositeCell.createTermFromString ("instrument", "MS", "MS:1000031")

        let unitized =
            CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")

        let anchor = coordinate 1 1

        let next =
            pasteStructured anchor [| anchor |] [| [| unitized; term; unitized |] |] target

        Vitest.expect(next.GetCellAt(0, 0).AsFreeText).toBe "4 metre"
        Vitest.expect(next.GetCellAt(1, 0)).toEqual term
        Vitest.expect(next.GetCellAt(2, 0)).toEqual unitized
        Vitest.expect(target.GetCellAt(0, 0).AsFreeText).toBe "A"
)

for structured in [ false; true ] do
    Vitest.test (
        $"paste clips right overflow and extends bottom rows (structured={structured})",
        fun () ->
            let target = table ()
            let anchor = coordinate 3 2

            let next =
                if structured then
                    pasteStructured
                        anchor
                        [| anchor |]
                        [|
                            [|
                                CompositeCell.FreeText "A"
                                CompositeCell.FreeText "clipped"
                            |]
                            [|
                                CompositeCell.FreeText "C"
                                CompositeCell.FreeText "clipped"
                            |]
                        |]
                        target
                else
                    pastePlain anchor [| anchor |] "A\tclipped\nC\tclipped" target

            Vitest.expect(next.ColumnCount).toBe 3
            Vitest.expect(next.RowCount).toBe 3
            Vitest.expect(next.GetCellAt(2, 1).ToString()).toBe "A"
            Vitest.expect(next.GetCellAt(2, 2).ToString()).toBe "C"
            Vitest.expect(target.RowCount).toBe 2
    )

Vitest.test (
    "plain TSV repeats across the full selection relative to its upper left corner",
    fun () ->
        let target = table ()

        let selected = [|
            for y in 1..2 do
                for x in 1..3 -> coordinate x y
        |]

        let next = pastePlain (coordinate 3 2) selected "A\tB" target

        for row in 0..1 do
            Vitest
                .expect(
                    [|
                        for column in 0..2 -> next.GetCellAt(column, row).ToString()
                    |]
                )
                .toEqual
                [| "A"; "B"; "A" |]
)

Vitest.test (
    "plain paste retains internal blank rows and ignores a terminal line break",
    fun () ->
        let target = table ()
        let anchor = coordinate 1 1
        let next = pastePlain anchor [| anchor |] "A\n\nB\n" target
        Vitest.expect(next.RowCount).toBe 3
        Vitest.expect([| for row in 0..2 -> next.GetCellAt(0, row).AsFreeText |]).toEqual [| "A"; ""; "B" |]
)
