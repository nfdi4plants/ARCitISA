module ElectronCore.ProvenanceGraphReaderTests

open ARCtrl
open Fable.Core
open Arcitisa.Components.Shared.ProvenanceGraph.Types
open Arcitisa.Components.Shared.ProvenanceGraph.BuildIndex
open Arcitisa.Components.Shared.ProvenanceGraph.Queries
open Vitest

// The main process builds ProvenanceGraphData and sends it over IPC. The renderer
// receives a structuredClone, which loses every prototype, and runs the queries on it.

[<Emit("structuredClone($0)")>]
let private structuredClone<'T> (value: 'T) : 'T = jsNative

[<Emit("JSON.stringify($0)")>]
let private stringify<'T> (value: 'T) : string = jsNative

[<Emit("Object.getPrototypeOf($0) === Object.prototype")>]
let private isPlainObject<'T> (value: 'T) : bool = jsNative

let private oa name = OntologyAnnotation.create (name = name)

let private oaWithAccession name tsr tan =
    OntologyAnnotation.create (name = name, tsr = tsr, tan = tan)

let private text value = CompositeCell.createFreeText value

let private term value =
    CompositeCell.createTermFromString (name = value)

let private unitized value unitName =
    CompositeCell.createUnitizedFromString (value, name = unitName)

let private table (name: string) (headers: CompositeHeader list) (rows: CompositeCell list list) =
    let rows: ResizeArray<ResizeArray<CompositeCell>> =
        rows
        |> List.map (fun (row: CompositeCell list) -> ResizeArray<CompositeCell>(row :> seq<CompositeCell>))
        |> ResizeArray

    ArcTable.createFromRows (name, ResizeArray<CompositeHeader>(headers :> seq<CompositeHeader>), rows)

// Study study-1 makes sample-a and sample-b from two sources. Its table has a
// characteristic and a unitized parameter whose column term has an accession, so the
// node annotations and the Unit and Accession options are not empty. Assay assay-1 reads
// both samples. Its assay-table has two signatures (22 and 23 degrees) and its
// analysis-table continues from the extracts to the raw files, so the lineage of
// raw-a.raw crosses three tables and two datasets.
let private investigationFixture () =
    let studyTable =
        table "study-table" [
            CompositeHeader.Input IOType.Source
            CompositeHeader.Characteristic(oa "organism")
            CompositeHeader.Parameter(oa "Temperature")
            CompositeHeader.Parameter(oaWithAccession "Light intensity" "XYZ" "XYZ:1000121")
            CompositeHeader.Output IOType.Sample
        ] [
            [
                text "source-a"
                term "Arabidopsis thaliana"
                term "22"
                unitized "100" "lux"
                text "sample-a"
            ]
            [
                text "source-b"
                term "Zea mays"
                term "23"
                unitized "200" "lux"
                text "sample-b"
            ]
        ]

    let assayTable =
        table "assay-table" [
            CompositeHeader.Input IOType.Sample
            CompositeHeader.Parameter(oa "Temperature")
            CompositeHeader.Output IOType.Sample
        ] [
            [ text "sample-a"; term "22"; text "extract-a" ]
            [ text "sample-b"; term "23"; text "extract-b" ]
        ]

    let analysisTable =
        table "analysis-table" [
            CompositeHeader.Input IOType.Sample
            CompositeHeader.Output IOType.Data
        ] [
            [ text "extract-a"; text "./raw-a.raw" ]
            [ text "extract-b"; text "./raw-b.raw" ]
        ]

    ArcInvestigation.create (
        identifier = "arc-1",
        studies =
            ResizeArray [
                ArcStudy.create (identifier = "study-1", tables = ResizeArray [ studyTable ])
            ],
        assays =
            ResizeArray [
                ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ assayTable; analysisTable ])
            ]
    )

// A work in progress ARC: rows without an output, rows without an input, a table with an Input column only,
// a table whose headers have no cells filled, and a table with a protocol only.
let private wipInvestigation () =
    let headers = [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.ProtocolREF
        CompositeHeader.Output IOType.Sample
    ]

    let row input output = [ text input; text "draft.md"; text output ]

    let unfilled = ArcTable.init "unfilled"
    unfilled.AddColumn(CompositeHeader.Input IOType.Sample)
    unfilled.AddColumn(CompositeHeader.Output IOType.Sample)
    unfilled.AddRowsEmpty 3

    let tables = [
        table "outputs_missing" headers [ row "a1" "b1"; row "a2" ""; row "a3" "" ]
        table "inputs_missing" headers [ row "" "c1"; row "d1" "d2" ]
        table "only_input" [ CompositeHeader.Input IOType.Sample ] [ [ text "e1" ]; [ text "e2" ] ]
        unfilled
        table "protocol_only" [ CompositeHeader.ProtocolREF ] [ [ text "plan.md" ]; [ text "plan.md" ] ]
    ]

    ArcInvestigation.create (
        identifier = "arc-wip",
        assays =
            ResizeArray [
                ArcAssay.create (identifier = "Draft", tables = ResizeArray tables)
            ]
    )

let private requireSome (what: string) (value: 'T option) : 'T =
    match value with
    | Some value -> value
    | None -> failwith $"Expected a result for {what}."

/// Runs every query for every id of the data and returns the results as JSON text.
let private runAllQueries (data: ProvenanceGraphData) : string[] =
    let index = ProvenanceIndex.ofData data

    let groupIds =
        index.Processes
        |> Array.collect (fun p -> Array.append p.InputGroups p.OutputGroups)
        |> Array.map (fun g -> g.Id)

    [|
        yield stringify (datasetGraph index)

        for dataset in index.Datasets do
            yield stringify (requireSome dataset.Id (processGraph index dataset.Id))

        for p in index.Processes do
            yield stringify (requireSome p.Key (processGroups index p.Key))

        for groupId in groupIds do
            yield stringify (requireSome groupId (processDetail index groupId))

        for node in index.Nodes do
            yield stringify (requireSome node.Id (lineage index node.Id))
    |]

Vitest.describe (
    "ProvenanceGraphReader",
    fun () ->
        Vitest.test (
            "builds data with shared nodes, two signatures and a lineage across tables",
            fun () ->
                let data = ProvenanceGraphData.ofInvestigation (investigationFixture ())
                let index = ProvenanceIndex.ofData data
                let assayTable = data.Processes |> Array.find (fun p -> p.Name = "assay-table")
                let lineageOfRaw = requireSome "raw-a.raw" (lineage index "raw-a.raw")

                Vitest
                    .expect(data.Datasets |> Array.map (fun d -> d.Id))
                    .toEqual ([| "studies/study-1/"; "assays/assay-1/" |])

                Vitest.expect(assayTable.Signatures.Length).toBe (2)
                Vitest.expect((datasetGraph index).Edges.Length).toBe (1)

                Vitest
                    .expect(lineageOfRaw.Nodes |> Array.map (fun n -> n.Id))
                    .toEqual ([| "raw-a.raw"; "extract-a"; "sample-a"; "source-a" |])

                Vitest.expect(lineageOfRaw.Edges.Length).toBe (3)
        )

        Vitest.test (
            "queries on a structuredClone of the data give the results of the original",
            fun () ->
                let data = ProvenanceGraphData.ofInvestigation (investigationFixture ())
                let cloned = structuredClone data

                let expectedCount =
                    let index = ProvenanceIndex.ofData data

                    let groupCount =
                        index.Processes
                        |> Array.sumBy (fun p -> p.InputGroups.Length + p.OutputGroups.Length)

                    1
                    + index.Datasets.Length
                    + index.Processes.Length
                    + groupCount
                    + index.Nodes.Length

                let original = runAllQueries data
                let fromClone = runAllQueries cloned

                Vitest.expect(fromClone.Length).toBe (expectedCount)
                Vitest.expect(fromClone |> Array.forall (fun result -> result.Length > 2)).toBe (true)
                Vitest.expect(fromClone).toEqual (original)
        )

        Vitest.test (
            "queries on a structuredClone of a work in progress ARC give the results of the original",
            fun () ->
                let data = ProvenanceGraphData.ofInvestigation (wipInvestigation ())
                let cloned = structuredClone data

                let original = runAllQueries data
                let fromClone = runAllQueries cloned

                Vitest.expect(fromClone.Length).toBeGreaterThan (10)
                Vitest.expect(fromClone |> Array.forall (fun result -> result.Length > 2)).toBe (true)
                Vitest.expect(fromClone).toEqual (original)

                // The unfinished rows keep their nodes on the clone, and no edge joins a missing end.
                let index = ProvenanceIndex.ofData cloned
                let noOutput = requireSome "a2" (lineage index "a2")
                let noInput = requireSome "c1" (lineage index "c1")

                Vitest.expect(noOutput.Nodes |> Array.map (fun n -> n.Id)).toEqual ([| "a2" |])
                Vitest.expect(noOutput.Edges.Length).toBe (0)
                Vitest.expect(noInput.Nodes |> Array.map (fun n -> n.Id)).toEqual ([| "c1" |])
                Vitest.expect(noInput.Edges.Length).toBe (0)

                let edgeCount = index.Processes |> Array.sumBy (fun p -> p.GroupEdges.Length)

                Vitest.expect(edgeCount).toBe (2)
        )

        Vitest.test (
            "the clone has lost the record prototypes but still answers the queries",
            fun () ->
                let data = ProvenanceGraphData.ofInvestigation (investigationFixture ())
                let cloned = structuredClone data

                Vitest.expect(isPlainObject data.Datasets.[0]).toBe (false)
                Vitest.expect(isPlainObject cloned.Datasets.[0]).toBe (true)

                let index = ProvenanceIndex.ofData cloned
                let lineageOfRaw = requireSome "raw-a.raw" (lineage index "raw-a.raw")
                let datasetEdges = (datasetGraph index).Edges

                let light =
                    cloned.Rows.[0].Annotations |> Array.find (fun a -> a.Name = "Light intensity")

                let sourceA = cloned.Nodes |> Array.find (fun n -> n.Id = "source-a")

                Vitest.expect(sourceA.Annotations.Length).toBe (1)
                Vitest.expect(light.Unit).toBe (Some "lux")
                Vitest.expect(Option.isSome light.Accession).toBe (true)

                Vitest
                    .expect(datasetEdges |> Array.map (fun e -> e.Id))
                    .toEqual ([| "studies/study-1/->assays/assay-1/" |])

                Vitest.expect(datasetEdges.[0].SharedNodeIds).toEqual ([| "sample-a"; "sample-b" |])
                Vitest.expect(lineageOfRaw.Edges.Length).toBe (3)
        )
)
