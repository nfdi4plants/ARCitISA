module internal Arcitisa.Components.Tests.EmptyColumnJson

open ARCtrl
open ARCtrl.Json
open Arcitisa.Components.Shared
open Vitest

let private makeAssay withData =
    let table = ArcTable.init "Empty columns"
    table.AddColumn(CompositeHeader.Input IOType.Sample, ResizeArray())
    table.AddColumn(CompositeHeader.Comment "Notes", ResizeArray())

    if withData then
        table.AddColumn(CompositeHeader.Output IOType.Data, ResizeArray [ CompositeCell.emptyData ])

    let assay = ArcAssay.init "empty-column-test"
    assay.AddTable table
    assay, table

let private roundTrip format arcFile =
    let _, json = Export.parseToJsonString (arcFile, format)
    Generic.readFromJsonMap.[arcFile.RelatedArcFilesDiscriminate, format] json

for format in
    [
        JsonExportFormat.ARCtrl
        JsonExportFormat.ARCtrlCompressed
    ] do
    Vitest.describe (
        $"Empty sparse columns ({format})",
        fun () ->
            Vitest.test (
                "round-trips empty columns followed by a data column without mutating the source",
                fun () ->
                    let assay, table = makeAssay true
                    let before = ArcAssay.toJsonString 0 assay
                    let restored = roundTrip format (ArcFiles.Assay assay)
                    let restoredTable = restored.Tables().[0]

                    Vitest.expect(restoredTable.RowCount).toBe 1
                    Vitest.expect(restoredTable.Headers |> Seq.toArray).toEqual (table.Headers |> Seq.toArray)
                    Vitest.expect(restoredTable.GetCellAt(0, 0)).toEqual CompositeCell.emptyFreeText
                    Vitest.expect(restoredTable.GetCellAt(2, 0)).toEqual CompositeCell.emptyData
                    Vitest.expect(ArcAssay.toJsonString 0 assay).toBe before
            )

            Vitest.test (
                "preserves zero-row tables",
                fun () ->
                    let assay, _ = makeAssay false
                    let restored = roundTrip format (ArcFiles.Assay assay)
                    Vitest.expect(restored.Tables().[0].RowCount).toBe 0
                    Vitest.expect(restored.Tables().[0].ColumnCount).toBe 2
            )

            Vitest.test (
                "preserves populated sparse cells alongside an empty column",
                fun () ->
                    let assay, table = makeAssay true
                    table.SetCellAt(0, 0, CompositeCell.createFreeText "sample-1")
                    table.SetCellAt(0, 1, CompositeCell.createFreeText "sample-2")
                    let restored = roundTrip format (ArcFiles.Assay assay)
                    let restoredTable = restored.Tables().[0]
                    Vitest.expect(restoredTable.RowCount).toBe 2
                    Vitest.expect(restoredTable.GetCellAt(0, 0)).toEqual (CompositeCell.createFreeText "sample-1")
                    Vitest.expect(restoredTable.GetCellAt(0, 1)).toEqual (CompositeCell.createFreeText "sample-2")
            )

            Vitest.test (
                "round-trips template tables",
                fun () ->
                    let _, table = makeAssay true

                    let template =
                        Template.create (System.Guid.NewGuid(), table, name = "Empty columns")

                    match roundTrip format (ArcFiles.Template template) with
                    | ArcFiles.Template restored ->
                        Vitest.expect(restored.Table.RowCount).toBe 1
                        Vitest.expect(restored.Table.ColumnCount).toBe 3
                        Vitest.expect(restored.Table.GetCellAt(0, 0)).toEqual CompositeCell.emptyFreeText
                    | _ -> failwith "Expected a template"
            )

            Vitest.test (
                "repairs tables nested in an investigation",
                fun () ->
                    let assay, _ = makeAssay true
                    let investigation = ArcInvestigation.init "nested"
                    investigation.AddAssay assay

                    match roundTrip format (ArcFiles.Investigation investigation) with
                    | ArcFiles.Investigation restored ->
                        Vitest.expect(restored.Assays.[0].Tables.[0].RowCount).toBe 1

                        Vitest.expect(restored.Assays.[0].Tables.[0].GetCellAt(0, 0)).toEqual
                            CompositeCell.emptyFreeText
                    | _ -> failwith "Expected an investigation"
            )
    )

Vitest.test (
    "normalization leaves unrelated nulls and JSON-looking strings untouched",
    fun () ->
        let json =
            """{"columns":[[0,null]],"c":[[0,null]],"other":null,"text":"\"columns\":[[0,null]]"}"""

        Vitest.expect(EmptyColumnJson.normalize false json).toBe json
        Vitest.expect(EmptyColumnJson.normalize true json).toBe json
)

Vitest.test (
    "repairs template tables without altering unrelated null metadata",
    fun () ->
        let json =
            """{"table":{"headers":[],"rowCount":1,"columns":[[0,null]]},"other":null}"""

        let expected =
            """{"table":{"headers":[],"rowCount":1,"columns":[[0,[]]]},"other":null}"""

        Vitest.expect(EmptyColumnJson.normalize false json).toBe expected
)
