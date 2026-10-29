module internal Swate.Components.Composite.TableImport.HelperTests

open ARCtrl
open ARCtrl.Json
open ARCtrl.Spreadsheet
open Swate.Components.Shared
open Swate.Components.Composite.TableImport.Types
open Swate.Components.Composite.TableImport.Helper
open Vitest

let private createBasicAssay () =
    let table = ArcTable.init "New Table 0"
    table.AddColumn(CompositeHeader.Input IOType.Source)
    table.AddColumn CompositeHeader.ProtocolUri
    table.AddColumn(CompositeHeader.Output IOType.Sample)
    table.AddRowsEmpty 3
    table.SetCellAt(0, 0, CompositeCell.FreeText "source-1")
    let assay = ArcAssay.init "TestAssay"
    assay.AddTable table
    ArcFiles.Assay assay

let private createTemplate rowCount =
    let table = ArcTable.init "Template"
    table.AddColumn(CompositeHeader.Input IOType.Source)
    table.AddColumn CompositeHeader.ProtocolUri
    table.AddColumn(CompositeHeader.Output IOType.Sample)

    for index in 0..6 do
        table.AddColumn(
            CompositeHeader.Parameter(OntologyAnnotation($"Parameter {index}")),
            ResizeArray [
                for _ in 1..rowCount ->
                    CompositeCell.Unitized("21", OntologyAnnotation("degree Celsius", "UO", "UO:0000027"))
            ]
        )

    // Together with Input, Output, ProtocolUri and Parameter above, cover every CompositeHeader case.
    let additionalHeaders = [|
        CompositeHeader.Component(OntologyAnnotation("Component"))
        CompositeHeader.Characteristic(OntologyAnnotation("Characteristic"))
        CompositeHeader.Factor(OntologyAnnotation("Factor"))
        CompositeHeader.ProtocolType
        CompositeHeader.ProtocolDescription
        CompositeHeader.ProtocolVersion
        CompositeHeader.ProtocolREF
        CompositeHeader.Performer
        CompositeHeader.Date
        CompositeHeader.FreeText "Custom column"
        CompositeHeader.Comment "Notes"
    |]

    for header in additionalHeaders do
        let cell =
            if header.IsTermColumn then
                CompositeCell.Term(OntologyAnnotation("Example term"))
            else
                CompositeCell.FreeText "Example text"

        table.AddColumn(header, ResizeArray [ for _ in 1..rowCount -> cell.Copy() ])

    table

let private importTemplate importType fullImport deselected source target =
    let config = {
        SelectiveImportConfig.init () with
            ImportType = importType
            ImportTables = [
                {
                    Index = 0
                    FullImport = fullImport
                    Name = None
                }
            ]
            DeselectedColumns = deselected
    }

    updateTables (ResizeArray [ source ]) config (Some 0) (Some target)

let private verifyReadableAndSerializable (file: ArcFiles) =
    // Materialize every column, including its cells, as the spreadsheet writer does.
    for table in file.Tables() do
        Vitest.expect(table.Columns.Count).toBe table.ColumnCount

        for column in table.Columns do
            Vitest.expect(column.Cells.Count).toBe table.RowCount

    match file with
    | ArcFiles.Assay assay -> ArcAssay.toFsWorkbook assay |> ignore
    | _ -> failwith "Expected an assay"

    for format in
        [
            JsonExportFormat.ARCtrl
            JsonExportFormat.ARCtrlCompressed
        ] do
        let _, json = Export.parseToJsonString (file, format)

        let restored =
            Generic.readFromJsonMap.[file.RelatedArcFilesDiscriminate, format] json

        for index in 0 .. file.Tables().Count - 1 do
            let expected = file.Tables().[index]
            let actual = restored.Tables().[index]
            Vitest.expect(actual.Headers).toEqual expected.Headers
            Vitest.expect(actual.RowCount).toBe expected.RowCount
            Vitest.expect(actual.Columns).toEqual expected.Columns

for sourceRows in [ 0; 2 ] do
    Vitest.test (
        $"header-only template with {sourceRows} rows leaves all header types readable and saveable",
        fun () ->
            let source = createTemplate sourceRows
            let target = createBasicAssay ()
            let updated = importTemplate TableJoinOptions.Headers false Set.empty source target
            let table = updated.Tables().[0]
            Vitest.expect(table.Headers).toEqual source.Headers
            Vitest.expect(table.RowCount).toBe 3
            Vitest.expect(table.GetCellAt(0, 0)).toEqual (CompositeCell.FreeText "source-1")

            for columnIndex in 3 .. table.ColumnCount - 1 do
                let expectedCell =
                    if table.Headers.[columnIndex].IsTermColumn then
                        CompositeCell.emptyTerm
                    else
                        CompositeCell.emptyFreeText

                Vitest.expect(table.GetCellAt(columnIndex, 0)).toEqual expectedCell

            Vitest.expect(source.RowCount).toBe sourceRows

            if sourceRows > 0 then
                Vitest.expect(source.GetCellAt(3, 0).AsUnitized |> fst).toBe "21"

            verifyReadableAndSerializable (ArcFiles.refreshRef updated)
    )

Vitest.test (
    "header-only imports honor deselected columns",
    fun () ->
        let source = createTemplate 2

        let updated =
            importTemplate TableJoinOptions.Headers false (Set.singleton (0, 4)) source (createBasicAssay ())

        let table = updated.Tables().[0]
        Vitest.expect(table.ColumnCount).toBe (source.ColumnCount - 1)
        Vitest.expect(table.Headers.Contains(CompositeHeader.Parameter(OntologyAnnotation("Parameter 1")))).toBe false
        verifyReadableAndSerializable updated
)

Vitest.test (
    "header-only templates imported as new tables stay readable after adding rows",
    fun () ->
        let source = createTemplate 2

        let updated =
            importTemplate TableJoinOptions.Headers true Set.empty source (createBasicAssay ())

        Vitest.expect(updated.Tables().Count).toBe 2
        let imported = updated.Tables().[1]
        Vitest.expect(imported.Headers).toEqual source.Headers
        Vitest.expect(imported.RowCount).toBe 0
        Vitest.expect(imported.Columns.Count).toBe (imported.ColumnCount)
        imported.AddRowsEmpty 3
        verifyReadableAndSerializable (ArcFiles.refreshRef updated)
)

for importType in [ TableJoinOptions.WithUnit; TableJoinOptions.WithValues ] do
    Vitest.test (
        $"{importType} template imports preserve units and the requested values",
        fun () ->
            let source = createTemplate 5

            let updated = importTemplate importType false Set.empty source (createBasicAssay ())

            let table = updated.Tables().[0]
            Vitest.expect(table.Headers).toEqual source.Headers
            Vitest.expect(table.GetCellAt(0, 0)).toEqual (CompositeCell.FreeText "source-1")
            let value, unit = table.GetCellAt(3, 0).AsUnitized
            Vitest.expect(unit.NameText).toBe "degree Celsius"

            let expectedValue =
                if importType = TableJoinOptions.WithValues then
                    "21"
                else
                    ""

            Vitest.expect(value).toBe expectedValue
            Vitest.expect(table.RowCount).toBe 5
            verifyReadableAndSerializable updated
    )

let private decisionsFor importType defaultAction = {
    ImportType = importType
    DefaultAction = defaultAction
    Actions = Map.empty
    DeselectedColumns = Set.empty
    EnteredNames = Map.empty
    ActiveTableIndex = Some 0
}

let private jsonDefaults = {
    decisionsFor TableJoinOptions.WithValues TableImportAction.ImportAsNewTable with
        ActiveTableIndex = None
}

let private sourceOf (index: int) (table: ArcTable) = {
    Index = index
    Name = table.Name
    Table = table
}

let private applied (result: Result<TableImportResult, exn>) =
    match result with
    | Ok result -> result
    | Error error -> failwith error.Message

let private expectError (message: string) (result: Result<TableImportResult, exn>) =
    match result with
    | Ok _ -> failwith "Expected an error"
    | Error error -> Vitest.expect(error.Message).toBe message

let private expectSameTable (actual: ArcTable) (expected: ArcTable) =
    Vitest.expect(actual.Headers).toEqual expected.Headers
    Vitest.expect(actual.RowCount).toBe expected.RowCount

    for columnIndex in 0 .. expected.ColumnCount - 1 do
        for rowIndex in 0 .. expected.RowCount - 1 do
            Vitest.expect(actual.GetCellAt(columnIndex, rowIndex)).toEqual (expected.GetCellAt(columnIndex, rowIndex))

let private degreeCelsius = OntologyAnnotation("degree Celsius", "UO", "UO:0000027")

let private createMixedSource () =
    let table = ArcTable.init "Mixed"

    table.AddColumn(
        CompositeHeader.Input IOType.Source,
        ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"s{i}" ]
    )

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation "Temperature"),
        ResizeArray [
            for i in 1..3 -> CompositeCell.Unitized($"{i}", degreeCelsius)
        ]
    )

    table.AddColumn(
        CompositeHeader.Characteristic(OntologyAnnotation "Organism"),
        ResizeArray [
            for _ in 1..3 -> CompositeCell.Term(OntologyAnnotation("Homo sapiens", "NCBI", "NCBI:9606"))
        ]
    )

    table.AddColumn(CompositeHeader.Comment "Empty", ResizeArray [ for _ in 1..3 -> CompositeCell.FreeText "" ])

    table.AddColumn(
        CompositeHeader.Output IOType.Data,
        ResizeArray [
            for i in 1..3 -> CompositeCell.Data(Data(name = $"file{i}.csv"))
        ]
    )

    table.AddColumn(CompositeHeader.FreeText "Notes", ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"n{i}" ])
    table

let private importedTables (result: TableImportResult) = result.ArcFile.Tables()

for importType in
    [
        TableJoinOptions.Headers
        TableJoinOptions.WithUnit
        TableJoinOptions.WithValues
    ] do
    Vitest.test (
        $"{importType} imports as a new table keep the headers and the requested cells",
        fun () ->
            let source = createTemplate 3

            let result =
                applyImport
                    (createBasicAssay ())
                    [| sourceOf 0 source |]
                    (decisionsFor importType TableImportAction.ImportAsNewTable)
                |> applied

            let table = (importedTables result).[1]
            Vitest.expect(table.Headers).toEqual source.Headers
            Vitest.expect(table.RowCount).toBe (if importType = TableJoinOptions.Headers then 0 else 3)

            if importType <> TableJoinOptions.Headers then
                let value, unit = table.GetCellAt(3, 0).AsUnitized
                Vitest.expect(unit.NameText).toBe "degree Celsius"

                Vitest
                    .expect(value)
                    .toBe (
                        if importType = TableJoinOptions.WithValues then
                            "21"
                        else
                            ""
                    )
    )

    Vitest.test (
        $"{importType} imports appended to the active table keep the headers and the requested cells",
        fun () ->
            let source = createTemplate 3

            let result =
                applyImport
                    (createBasicAssay ())
                    [| sourceOf 0 source |]
                    (decisionsFor importType TableImportAction.AppendToActiveTable)
                |> applied

            let table = (importedTables result).[0]
            Vitest.expect(table.Headers).toEqual source.Headers
            Vitest.expect(table.RowCount).toBe 3
            Vitest.expect(table.GetCellAt(0, 0)).toEqual (CompositeCell.FreeText "source-1")

            match importType with
            | TableJoinOptions.Headers -> Vitest.expect(table.GetCellAt(3, 0)).toEqual CompositeCell.emptyTerm
            | _ ->
                let value, unit = table.GetCellAt(3, 0).AsUnitized
                Vitest.expect(unit.NameText).toBe "degree Celsius"

                Vitest
                    .expect(value)
                    .toBe (
                        if importType = TableJoinOptions.WithValues then
                            "21"
                        else
                            ""
                    )
    )

Vitest.test (
    "the JSON defaults import every table as a copy of the source",
    fun () ->
        let source = createMixedSource ()

        let result =
            applyImport (createBasicAssay ()) [| sourceOf 0 source |] jsonDefaults
            |> applied

        expectSameTable (importedTables result).[1] (source.Copy())
        Vitest.expect(result.CreatedTableNames).toEqual [ "Mixed" ]
)

Vitest.test (
    "the JSON defaults import a table with headers only as a copy",
    fun () ->
        let source = ArcTable.init "Headers only"
        source.AddColumn(CompositeHeader.Input IOType.Source)
        source.AddColumn(CompositeHeader.Output IOType.Sample)

        let result =
            applyImport (createBasicAssay ()) [| sourceOf 0 source |] jsonDefaults
            |> applied

        expectSameTable (importedTables result).[1] (source.Copy())
)

Vitest.test (
    "the JSON defaults import a table with a repeated comment header as a copy",
    fun () ->
        // AddColumn refuses a second Comment "Notes", but a table decoded from JSON can contain one.
        let template = ArcTable.init "Comments"

        template.AddColumn(
            CompositeHeader.Comment "A",
            ResizeArray [ CompositeCell.FreeText "a0"; CompositeCell.FreeText "a1" ]
        )

        template.AddColumn(
            CompositeHeader.Comment "B",
            ResizeArray [ CompositeCell.FreeText "b0"; CompositeCell.FreeText "b1" ]
        )

        let source =
            ArcTable.fromArcTableValues (
                "Comments",
                ResizeArray [
                    CompositeHeader.Comment "Notes"
                    CompositeHeader.Comment "Notes"
                ],
                template.Copy().Values
            )

        let result =
            applyImport (createBasicAssay ()) [| sourceOf 0 source |] jsonDefaults
            |> applied

        expectSameTable (importedTables result).[1] (source.Copy())
)

Vitest.test (
    "the JSON defaults import a sparse column as a copy",
    fun () ->
        let source = ArcTable.init "Sparse"

        source.AddColumn(
            CompositeHeader.Input IOType.Source,
            ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"s{i}" ]
        )

        source.AddColumn(CompositeHeader.Parameter(OntologyAnnotation "Sparse"))
        source.SetCellAt(1, 1, CompositeCell.Term(OntologyAnnotation "set"))

        let result =
            applyImport (createBasicAssay ()) [| sourceOf 0 source |] jsonDefaults
            |> applied

        expectSameTable (importedTables result).[1] (source.Copy())
)

Vitest.test (
    "tables with the same name get distinct created names in source order",
    fun () ->
        let sources = [|
            for i in 0..2 ->
                let table = createTemplate 1
                table.Name <- "Same"
                sourceOf i table
        |]

        let result = applyImport (createBasicAssay ()) sources jsonDefaults |> applied
        Vitest.expect(result.CreatedTableNames).toEqual [ "Same"; "Same 1"; "Same 2" ]

        Vitest.expect(importedTables result |> Seq.map _.Name |> Array.ofSeq).toEqual [|
            "New Table 0"
            "Same"
            "Same 1"
            "Same 2"
        |]
)

Vitest.test (
    "appending a short source to a longer active table keeps the existing cells",
    fun () ->
        let current = createBasicAssay ()
        current.Tables().[0].AddRowsEmpty 1
        let source = createTemplate 2

        let result =
            applyImport
                current
                [| sourceOf 0 source |]
                (decisionsFor TableJoinOptions.WithValues TableImportAction.AppendToActiveTable)
            |> applied

        let table = (importedTables result).[0]
        Vitest.expect(table.RowCount).toBe 4
        Vitest.expect(table.GetCellAt(0, 0)).toEqual (CompositeCell.FreeText "source-1")
)

Vitest.test (
    "appending does not add a column whose header exists in the active table",
    fun () ->
        let source = createTemplate 3

        let result =
            applyImport
                (createBasicAssay ())
                [| sourceOf 0 source |]
                (decisionsFor TableJoinOptions.Headers TableImportAction.AppendToActiveTable)
            |> applied

        let headers = (importedTables result).[0].Headers |> Array.ofSeq
        Vitest.expect(headers.Length).toBe source.ColumnCount

        Vitest
            .expect(
                headers
                |> Array.filter (fun header -> header = CompositeHeader.ProtocolUri)
                |> Array.length
            )
            .toBe
            1
)

Vitest.test (
    "tables set to no import and deselected columns are not imported",
    fun () ->
        let sources = [| for i in 0..2 -> sourceOf i (createTemplate 1) |]

        let decisions = {
            jsonDefaults with
                Actions = Map [ 1, TableImportAction.NoImport ]
                DeselectedColumns = Set.singleton (0, 3)
        }

        let result = applyImport (createBasicAssay ()) sources decisions |> applied
        let tables = importedTables result
        Vitest.expect(tables.Count).toBe 3
        Vitest.expect(tables.[1].ColumnCount).toBe (sources.[0].Table.ColumnCount - 1)
        Vitest.expect(tables.[2].ColumnCount).toBe sources.[2].Table.ColumnCount
)

Vitest.test (
    "the current file is not changed by an import",
    fun () ->
        let current = createBasicAssay ()
        let source = createTemplate 2

        applyImport
            current
            [| sourceOf 0 source |]
            (decisionsFor TableJoinOptions.WithValues TableImportAction.AppendToActiveTable)
        |> applied
        |> ignore

        applyImport current [| sourceOf 0 source |] jsonDefaults |> applied |> ignore
        Vitest.expect(current.Tables().Count).toBe 1
        Vitest.expect(current.Tables().[0].ColumnCount).toBe 3
)

Vitest.test (
    "the summary lists created and modified tables",
    fun () ->
        let source = createTemplate 1

        let appended =
            applyImport
                (createBasicAssay ())
                [| sourceOf 0 source |]
                (decisionsFor TableJoinOptions.Headers TableImportAction.AppendToActiveTable)
            |> applied

        Vitest.expect(appended.ModifiedTableNames).toEqual [ "New Table 0" ]
        Vitest.expect(appended.CreatedTableNames).toEqual []

        let created =
            applyImport (createBasicAssay ()) [| sourceOf 0 source |] jsonDefaults
            |> applied

        Vitest.expect(created.ModifiedTableNames).toEqual []
        Vitest.expect(created.CreatedTableNames).toEqual [ "Template" ]
)

Vitest.test (
    "invalid import decisions return an error",
    fun () ->
        let source = createTemplate 1
        let sources = [| sourceOf 0 source |]
        let current = createBasicAssay ()

        applyImport current sources (decisionsFor TableJoinOptions.Headers TableImportAction.NoImport)
        |> expectError "Select at least one table to import."

        let noColumns = {
            jsonDefaults with
                DeselectedColumns = Set.ofList [ for column in 0 .. source.ColumnCount - 1 -> 0, column ]
        }

        applyImport current sources noColumns
        |> expectError "Select at least one column of \"Template\"."

        let taken = {
            jsonDefaults with
                EnteredNames = Map [ 0, "New Table 0" ]
        }

        applyImport current sources taken
        |> expectError "A table named \"New Table 0\" already exists."

        let noTarget = {
            decisionsFor TableJoinOptions.Headers TableImportAction.AppendToActiveTable with
                ActiveTableIndex = None
        }

        applyImport current sources noTarget
        |> expectError "Select a table in the editor to append columns to."
)
