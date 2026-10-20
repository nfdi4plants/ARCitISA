module ElectronCore.ProvenanceGroupingReaderTests

open ARCtrl
open Fable.Core
open Fable.Core.JsInterop
open Main.Provenance
open Swate.Electron.Shared.DTOs.ProvenanceGroupingDto
open Swate.Components.Shared.ProvenanceGrouping.Types
open Vitest

[<Emit("structuredClone($0)")>]
let private structuredClone<'T> (value: 'T) : 'T = jsNative

let private oa name = OntologyAnnotation.create (name = name)
let private text value = CompositeCell.createFreeText value

let private term value =
    CompositeCell.createTermFromString (name = value)

let private table (name: string) (headers: CompositeHeader list) (rows: CompositeCell list list) =
    let rows: ResizeArray<ResizeArray<CompositeCell>> =
        rows
        |> List.map (fun (row: CompositeCell list) -> ResizeArray<CompositeCell>(row :> seq<CompositeCell>))
        |> ResizeArray

    ArcTable.createFromRows (name, ResizeArray<CompositeHeader>(headers :> seq<CompositeHeader>), rows)

let private assayTable () =
    table "assay-table" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Parameter(oa "Temperature")
        CompositeHeader.Output IOType.Sample
    ] [ [ text "sample-a"; term "22"; text "extract-a" ] ]

let private studyTable () =
    table "study-table" [
        CompositeHeader.Input IOType.Source
        CompositeHeader.Characteristic(oa "Organism")
        CompositeHeader.Output IOType.Sample
    ] [ [ text "source-a"; term "Plant"; text "sample-a" ] ]

let private runTable () =
    table "run-table" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Output IOType.Data
    ] [ [ text "extract-a"; text "raw-file-a" ] ]

let private arcFixture () =
    let study =
        ArcStudy.create (
            identifier = "study-1",
            tables = ResizeArray [ studyTable () ],
            registeredAssayIdentifiers = ResizeArray [ "assay-1" ]
        )

    let assay =
        ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ assayTable () ])

    let run = ArcRun("run-1")
    run.Tables.Add(runTable ())

    ARC(
        identifier = "arc-1",
        studies = ResizeArray [ study ],
        assays = ResizeArray [ assay ],
        runs = ResizeArray [ run ]
    )

// Deactivated for now: ProvenanceGrouping is outdated.
Vitest.describe (
    "ProvenanceGroupingReader",
    TestOptions(skip = true),
    fun () ->
        Vitest.test (
            "listTables returns study, assay, and run table selections",
            fun () ->
                let selections = ProvenanceGroupingReader.listTables (arcFixture ())
                let labels = selections |> Array.map _.DisplayLabel

                Vitest
                    .expect(labels)
                    .toEqual (
                        [|
                            "Study study-1 / study-table"
                            "Assay assay-1 / assay-table"
                            "Run run-1 / run-table"
                        |]
                    )

                Vitest.expect(selections.[0].Scope).toEqual (ProvenanceTableScopeDto.Study)
                Vitest.expect(selections.[1].Scope).toEqual (ProvenanceTableScopeDto.Assay)
                Vitest.expect(selections.[2].Scope).toEqual (ProvenanceTableScopeDto.Run)
        )

        Vitest.test (
            "loadTable converts the selected table with previous context enabled",
            fun () ->
                let arc = arcFixture ()

                let assaySelection = {
                    Scope = ProvenanceTableScopeDto.Assay
                    ParentIdentifier = "assay-1"
                    TableName = "assay-table"
                    DisplayLabel = "Assay assay-1 / assay-table"
                }

                let result = ProvenanceGroupingReader.loadTable assaySelection arc
                let model = ProvenanceModelDto.toModel result.Model

                let sampleA =
                    model.InputSets
                    |> Map.toSeq
                    |> Seq.map snd
                    |> Seq.find (fun set -> set.Name = "sample-a")

                let valueNames =
                    sampleA.PropertyValueIds
                    |> List.map (fun id -> model.PropertyValues.[id].Header.Category.Name)

                Vitest.expect(result.Selection).toEqual (assaySelection)
                Vitest.expect(model.Source.Id).toBe ("assay:assay-1:assay-table")
                Vitest.expect(model.Source.Name).toBe ("assay-table")
                Vitest.expect(valueNames |> List.contains "Organism").toBe (true)
        )

        Vitest.test (
            "loadTable result is safe to clone across Electron IPC",
            fun () ->
                let arc = arcFixture ()

                let assaySelection = {
                    Scope = ProvenanceTableScopeDto.Assay
                    ParentIdentifier = "assay-1"
                    TableName = "assay-table"
                    DisplayLabel = "Assay assay-1 / assay-table"
                }

                let result = ProvenanceGroupingReader.loadTable assaySelection arc
                let cloned = structuredClone result
                let model = ProvenanceModelDto.toModel cloned.Model

                Vitest.expect(cloned.Selection).toEqual (assaySelection)
                Vitest.expect(model.Source.Id).toBe ("assay:assay-1:assay-table")
                Vitest.expect(model.Source.Name).toBe ("assay-table")
        )

        Vitest.test (
            "loadTable DTO encodes model kinds and values as clone-safe records",
            fun () ->
                let arc = arcFixture ()

                let assaySelection = {
                    Scope = ProvenanceTableScopeDto.Assay
                    ParentIdentifier = "assay-1"
                    TableName = "assay-table"
                    DisplayLabel = "Assay assay-1 / assay-table"
                }

                let result = ProvenanceGroupingReader.loadTable assaySelection arc

                let propertyValue =
                    result.Model.PropertyValues
                    |> Array.map _.Value
                    |> Array.find (fun value -> value.Header.Category.Name = "Organism")

                Vitest.expect(propertyValue.Header.Kind.Id).toBe ("arc-isa:property:characteristic")
                Vitest.expect(propertyValue.Header.Kind.Label).toBe ("Characteristic")
                Vitest.expect((box propertyValue.Value)?Kind).toBe ("Term")

                let cloned = structuredClone result
                let model = ProvenanceModelDto.toModel cloned.Model

                let sampleA =
                    model.InputSets
                    |> Map.toSeq
                    |> Seq.map snd
                    |> Seq.find (fun set -> set.Name = "sample-a")

                let organism =
                    sampleA.PropertyValueIds
                    |> List.map (fun id -> model.PropertyValues.[id])
                    |> List.find (fun value -> value.Header.Category.Name = "Organism")

                match organism.Value with
                | ProvenanceValue.Term term -> Vitest.expect(term.Name).toBe ("Plant")
                | other -> failwithf "Expected organism value to round-trip as term, got %A" other

                match organism.Origin with
                | ProvenancePropertyOrigin.Real anchor ->
                    Vitest.expect(anchor.Source.Id).toBe ("study:study-1:study-table")
                    Vitest.expect(anchor.Source.Name).toBe ("study-table")
                | ProvenancePropertyOrigin.Virtual _ -> failwith "Expected converted DTO value to keep a real origin."
        )
)
