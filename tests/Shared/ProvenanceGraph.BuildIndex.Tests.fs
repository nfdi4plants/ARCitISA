module ProvenanceGraphBuildIndexTests

// These tests port tests/arc/provenanceIndex.test.ts of arc-portal-ui. PORTAL runs
// most of its cases on a sample crate (the Talinum ARC). Here every case builds a
// small ARC in code that has the same shape, so the sample file itself is not read.
//
// Dropped PORTAL cases:
// - Every case that reads the sample crate is rebuilt with the small ARC below.
//   Only the sample specific numbers are dropped: the counts 11, 15, 139 and 102 of
//   "finds all datasets, processes, rows and nodes" and "fills the lookup maps"
//   have no meaning here, so those cases check the counts of the fixture.
// - "has no process for the main invocation of a run" is a detail of the crate
//   import. The in-code equivalent is a run without tables.
// - PORTAL has no performance or mutation case in this file.
//
// Records have no equality, so the tests compare projections (ids, toText values,
// annotation tuples and key strings).

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open ARCtrl
open Arcitisa.Components.Shared.ProvenanceGraph.Types
open Arcitisa.Components.Shared.ProvenanceGraph.BuildIndex

// Table helpers

let private oa name = OntologyAnnotation.create (name = name)

let private oaWithAccession name tsr tan =
    OntologyAnnotation.create (name = name, tsr = tsr, tan = tan)

let private text value = CompositeCell.createFreeText value

let private term value =
    CompositeCell.createTermFromString (name = value)

let private unitized value unitName =
    CompositeCell.createUnitizedFromString (value, name = unitName)

let private table (name: string) (headers: CompositeHeader list) (rows: CompositeCell list list) =
    let rows =
        rows |> List.map (fun row -> ResizeArray<CompositeCell>(row)) |> ResizeArray

    ArcTable.createFromRows (name, ResizeArray<CompositeHeader>(headers), rows)

let private investigation (studies: ArcStudy list) (assays: ArcAssay list) (runs: ArcRun list) =
    ArcInvestigation.create (
        identifier = "inv",
        studies = ResizeArray studies,
        assays = ResizeArray assays,
        runs = ResizeArray runs
    )

let private studyOf identifier (tables: ArcTable list) =
    ArcStudy.create (identifier = identifier, tables = ResizeArray tables)

let private assayOf identifier (tables: ArcTable list) =
    ArcAssay.create (identifier = identifier, tables = ResizeArray tables)

let private runOf identifier (tables: ArcTable list) =
    ArcRun.create (identifier = identifier, tables = ResizeArray tables)

// Fixture: a small ARC shaped like the Talinum sample of PORTAL.
//
// Study SamplesStudy: plant_material makes samples CAM_01, CAM_02, C3_01, C3_02 from
// sources, with two humidity values (two signatures).
// Assay GC: metabolite_extraction (one signature) and gas_chromatography (four
// signatures, two input groups, four output groups).
// Assay RNASeq: rna_extraction consumes CAM_01.
// Assay MassHunter: mh-quant-results (one signature).
// Run deseq2 has no tables. Run kallisto has kallisto-index and kallisto-quant.

let private humidityDay =
    oaWithAccession "humidity day" "DPBO" "http://purl.org/nfdi4plants/ontology/dpbo/DPBO_0000005"

let private plantMaterial () =
    let row source humidity sample factor = [
        text source
        term "Talinum fruticosum"
        text "plant_material.md"
        unitized humidity "percent"
        text sample
        term factor
    ]

    table "plant_material" [
        CompositeHeader.Input IOType.Source
        CompositeHeader.Characteristic(oa "organism")
        CompositeHeader.ProtocolREF
        CompositeHeader.Parameter humidityDay
        CompositeHeader.Output IOType.Sample
        CompositeHeader.Factor(oa "Photosynthesis mode")
    ] [
        row "DB_097" "37" "CAM_01" "CAM"
        row "DB_098" "37" "CAM_02" "CAM"
        row "DB_099" "50" "C3_01" "C3"
        row "DB_100" "50" "C3_02" "C3"
    ]

let private metaboliteExtraction () =
    table "metabolite_extraction" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.ProtocolREF
        CompositeHeader.Output IOType.Sample
    ] [
        [ text "CAM_01"; text "extract.md"; text "DB23" ]
        [ text "CAM_02"; text "extract.md"; text "DB24" ]
        [ text "C3_01"; text "extract.md"; text "DB25" ]
    ]

let private gasChromatography () =
    let row input mode sampleType output = [
        text input
        text "gc.md"
        term mode
        term sampleType
        text output
    ]

    table "gas_chromatography" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.ProtocolREF
        CompositeHeader.Parameter(oa "Chromatography injection mode")
        CompositeHeader.Parameter(oa "MS sample type")
        CompositeHeader.Output IOType.Data
    ] [
        row "blank 1" "split" "blank" "raw1.raw"
        row "blank 2" "split" "blank" "raw2.raw"
        row "blank 1" "splitless" "blank" "raw3.raw"
        row "blank 2" "splitless" "blank" "raw4.raw"
        row "DB23" "split" "sample" "raw5.raw"
        row "DB24" "split" "sample" "raw6.raw"
        row "DB23" "splitless" "sample" "raw7.raw"
        row "DB24" "splitless" "sample" "raw8.raw"
        row "DB23" "split" "sample" "raw9.raw"
    ]

let private rnaExtraction () =
    table "rna_extraction" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.ProtocolREF
        CompositeHeader.Output IOType.Sample
    ] [ [ text "CAM_01"; text "rna.md"; text "RNA_01" ] ]

let private quantResults () =
    table "mh-quant-results" [
        CompositeHeader.Input IOType.Data
        CompositeHeader.ProtocolREF
        CompositeHeader.Output IOType.Data
    ] [
        [ text "raw1.mzML"; text "quant.md"; text "quant1.csv" ]
        [ text "raw2.mzML"; text "quant.md"; text "quant2.csv" ]
    ]

let private kallistoIndex () =
    table "kallisto-index" [
        CompositeHeader.Input IOType.Data
        CompositeHeader.Output IOType.Data
    ] [ [ text "./transcripts.fa"; text "./index.idx" ] ]

let private kallistoQuant () =
    table "kallisto-quant" [
        CompositeHeader.Input IOType.Data
        CompositeHeader.Parameter(oa "bootstrap")
        CompositeHeader.Output IOType.Data
    ] [
        [
            text "./reads/a.fq"
            term "10"
            text "./results/abundance.h5"
        ]
        [
            text "./reads/b.fq"
            term "20"
            text "./results/abundance.h5"
        ]
    ]

let private fixtureTables () =
    plantMaterial (),
    metaboliteExtraction (),
    gasChromatography (),
    rnaExtraction (),
    quantResults (),
    kallistoIndex (),
    kallistoQuant ()

let private buildFixture (edit: ArcAssay -> ArcAssay -> unit) =
    let plant, extraction, gc, rna, quant, kIndex, kQuant = fixtureTables ()
    let gcAssay = assayOf "GC" [ extraction; gc ]
    let rnaAssay = assayOf "RNASeq" [ rna ]
    edit gcAssay rnaAssay

    investigation [ studyOf "SamplesStudy" [ plant ] ] [ gcAssay; rnaAssay; assayOf "MassHunter" [ quant ] ] [
        runOf "deseq2" []
        runOf "kallisto" [ kIndex; kQuant ]
    ]

let private fixtureArc () = buildFixture (fun _ _ -> ())

let private fixtureData () =
    ProvenanceGraphData.ofInvestigation (fixtureArc ())

// Projections

type private AnnotationView = string * string * string option * string option * string option

let private view (annotation: Annotation) : AnnotationView =
    AnnotationCategory.toText annotation.Category,
    annotation.Name,
    annotation.Value,
    annotation.Unit,
    annotation.Accession

let private views (annotations: Annotation[]) : AnnotationView[] = annotations |> Array.map view

let private names (annotations: Annotation[]) : string[] =
    annotations |> Array.map (fun annotation -> annotation.Name)

let private processNamed (data: ProvenanceGraphData) (name: string) =
    data.Processes |> Array.find (fun p -> p.Name = name)

let private rowWithId (data: ProvenanceGraphData) (id: string) =
    data.Rows |> Array.find (fun r -> r.Id = id)

let private nodeWithId (data: ProvenanceGraphData) (id: string) =
    data.Nodes |> Array.find (fun n -> n.Id = id)

let private sortedStrings (values: string[]) : string[] = Array.sort values

let private expectContains (items: 'a[]) (item: 'a) (message: string) : unit =
    Expect.isTrue (Array.contains item items) message

let private gcKey = "assays/GC/::gas_chromatography"

// Tests

let private datasetTests =
    testList "datasets and processes" [
        testCase "lists studies, assays and runs with their ids, kinds and identifiers"
        <| fun _ ->
            let data = fixtureData ()

            Expect.equal
                (data.Datasets |> Array.map (fun d -> d.Id))
                [|
                    "studies/SamplesStudy/"
                    "assays/GC/"
                    "assays/RNASeq/"
                    "assays/MassHunter/"
                    "runs/deseq2/"
                    "runs/kallisto/"
                |]
                "Studies come first, then assays, then runs, each in ARC order."

            Expect.equal
                (data.Datasets |> Array.map (fun d -> DatasetKind.toText d.Kind))
                [| "study"; "assay"; "assay"; "assay"; "run"; "run" |]
                "The kinds follow the ids."

            Expect.equal
                (data.Datasets |> Array.map (fun d -> d.Identifier))
                [|
                    "SamplesStudy"
                    "GC"
                    "RNASeq"
                    "MassHunter"
                    "deseq2"
                    "kallisto"
                |]
                "The identifier is the id of the study, assay or run."

        testCase "counts datasets, processes, rows and nodes and fills the lookups"
        <| fun _ ->
            let data = fixtureData ()
            let index = ProvenanceIndex.ofData data

            let groupCount =
                data.Processes
                |> Array.sumBy (fun p -> p.InputGroups.Length + p.OutputGroups.Length)

            Expect.equal data.Datasets.Length 6 "datasets"
            Expect.equal data.Processes.Length 7 "processes"
            Expect.equal data.Rows.Length 22 "rows"
            Expect.equal data.Nodes.Length 32 "nodes"
            Expect.equal index.DatasetById.Count 6 "datasetById"
            Expect.equal index.ProcessByKey.Count 7 "processByKey"
            Expect.equal index.RowById.Count 22 "rowById"
            Expect.equal index.NodeById.Count 32 "nodeById"
            Expect.equal index.GroupById.Count groupCount "groupById"
            Expect.equal index.Datasets.Length 6 "The index keeps the arrays."
            Expect.equal index.Datasets.[2].Id "assays/RNASeq/" "The index keeps the order of the data."

        testCase "keys and names the processes of each dataset after its tables"
        <| fun _ ->
            let data = fixtureData ()

            Expect.equal
                (data.Processes |> Array.map (fun p -> p.Key))
                [|
                    "studies/SamplesStudy/::plant_material"
                    "assays/GC/::metabolite_extraction"
                    "assays/GC/::gas_chromatography"
                    "assays/RNASeq/::rna_extraction"
                    "assays/MassHunter/::mh-quant-results"
                    "runs/kallisto/::kallisto-index"
                    "runs/kallisto/::kallisto-quant"
                |]
                "key is <dataset id>::<table name>"

            Expect.equal
                (data.Processes |> Array.map (fun p -> p.Name))
                [|
                    "plant_material"
                    "metabolite_extraction"
                    "gas_chromatography"
                    "rna_extraction"
                    "mh-quant-results"
                    "kallisto-index"
                    "kallisto-quant"
                |]
                "name is the table name"

            Expect.equal
                (data.Processes |> Array.map (fun p -> p.DatasetId))
                [|
                    "studies/SamplesStudy/"
                    "assays/GC/"
                    "assays/GC/"
                    "assays/RNASeq/"
                    "assays/MassHunter/"
                    "runs/kallisto/"
                    "runs/kallisto/"
                |]
                "datasetId is the id of the owner"

        testCase "creates no process for a run without tables"
        <| fun _ ->
            let data = fixtureData ()

            Expect.isTrue (data.Datasets |> Array.exists (fun d -> d.Id = "runs/deseq2/")) "The run is a dataset."

            Expect.isEmpty
                (data.Processes |> Array.filter (fun p -> p.DatasetId = "runs/deseq2/"))
                "A run without tables has no process."

            Expect.equal
                (data.Processes
                 |> Array.filter (fun p -> p.DatasetId = "runs/kallisto/")
                 |> Array.map (fun p -> p.Name))
                [| "kallisto-index"; "kallisto-quant" |]
                "A run with tables has one process per table."

        testCase "creates no process for a table without rows"
        <| fun _ ->
            let arc =
                buildFixture (fun _ rna -> rna.AddTable(ArcTable.init "empty_table") |> ignore)

            let data = ProvenanceGraphData.ofInvestigation arc

            Expect.equal data.Processes.Length 7 "The empty table adds no process."

            Expect.isFalse
                (data.Processes |> Array.exists (fun p -> p.Key = "assays/RNASeq/::empty_table"))
                "No process for the empty table."
    ]

let private rowTests =
    testList "rows" [
        testCase "numbers the rows of a table from 0 and names them after it"
        <| fun _ ->
            let data = fixtureData ()
            let proc = processNamed data "plant_material"

            Expect.equal proc.RowIds.Length 4 "four rows"
            Expect.equal proc.RowIds.[0] "studies/SamplesStudy/::plant_material#0" "first row id"
            Expect.equal proc.RowIds.[3] "studies/SamplesStudy/::plant_material#3" "last row id"

            let row = rowWithId data proc.RowIds.[0]
            Expect.equal row.Name "plant_material_0" "name"
            Expect.equal row.ProcessKey proc.Key "processKey"
            Expect.equal row.DatasetId "studies/SamplesStudy/" "datasetId"
            Expect.equal row.Inputs [| "DB_097" |] "inputs"
            Expect.equal row.Outputs [| "CAM_01" |] "outputs"

        testCase "reads protocol, parameter values, units and accessions of a row"
        <| fun _ ->
            let data = fixtureData ()
            let row = rowWithId data "studies/SamplesStudy/::plant_material#0"

            Expect.equal
                (views row.Annotations)
                [|
                    ("protocol", "Protocol", Some "plant_material.md", None, None)
                    ("parameter",
                     "humidity day",
                     Some "37",
                     Some "percent",
                     Some "http://purl.org/nfdi4plants/ontology/dpbo/DPBO_0000005")
                |]
                "The protocol comes first, then the parameter with unit and accession of the column term."

        testCase "turns a short column accession into the Ontobee url"
        <| fun _ ->
            let short = oaWithAccession "temperature" "XYZ" "XYZ:1000121"

            let arc =
                investigation [] [
                    assayOf "A" [
                        table "t" [
                            CompositeHeader.Input IOType.Sample
                            CompositeHeader.Parameter short
                            CompositeHeader.Output IOType.Sample
                        ] [ [ text "a"; term "20"; text "b" ] ]
                    ]
                ] []

            let row = (ProvenanceGraphData.ofInvestigation arc).Rows.[0]

            Expect.equal
                (views row.Annotations)
                [|
                    ("parameter", "temperature", Some "20", None, Some "http://purl.obolibrary.org/obo/XYZ_1000121")
                |]
                "A short accession becomes the Ontobee url, a url stays as it is."

        testCase "orders step annotations as protocol, parameters, components, performer, date, comments"
        <| fun _ ->
            let arc =
                investigation [] [
                    assayOf "A" [
                        table "t" [
                            CompositeHeader.Comment "lane"
                            CompositeHeader.Date
                            CompositeHeader.Performer
                            CompositeHeader.Component(oa "buffer")
                            CompositeHeader.Parameter(oa "temperature")
                            CompositeHeader.Parameter(oa "Humidity")
                            CompositeHeader.Parameter(oa "altitude")
                            CompositeHeader.ProtocolREF
                            CompositeHeader.Input IOType.Sample
                            CompositeHeader.Output IOType.Sample
                        ] [
                            [
                                text "L001"
                                text "2015-01-12"
                                text "Ada Lovelace"
                                term "PBS"
                                term "20"
                                term "40"
                                term "300"
                                text "p.md"
                                text "a"
                                text "b"
                            ]
                        ]
                    ]
                ] []

            let row = (ProvenanceGraphData.ofInvestigation arc).Rows.[0]

            Expect.equal
                (row.Annotations
                 |> Array.map (fun a -> AnnotationCategory.toText a.Category, a.Name))
                [|
                    ("protocol", "Protocol")
                    ("parameter", "Humidity")
                    ("parameter", "altitude")
                    ("parameter", "temperature")
                    ("component", "buffer")
                    ("performer", "Performer")
                    ("date", "Date")
                    ("comment", "lane")
                |]
                "Parameters sort by ordinal text (uppercase before lowercase)."

        testCase "drops the protocol detail columns and sorts only the comments of the signature"
        <| fun _ ->
            let arc =
                investigation [] [
                    assayOf "A" [
                        table "t" [
                            CompositeHeader.Input IOType.Sample
                            CompositeHeader.ProtocolType
                            CompositeHeader.ProtocolDescription
                            CompositeHeader.ProtocolUri
                            CompositeHeader.ProtocolVersion
                            CompositeHeader.Comment "b"
                            CompositeHeader.Comment "a"
                            CompositeHeader.Comment ""
                            CompositeHeader.Output IOType.Sample
                        ] [
                            [
                                text "in"
                                term "assay type"
                                text "describes the protocol"
                                text "https://example.org/protocol"
                                text "1.0"
                                text "value b"
                                text "value a"
                                text "value empty key"
                                text "out"
                            ]
                        ]
                    ]
                ] []

            let data = ProvenanceGraphData.ofInvestigation arc
            let row = data.Rows.[0]

            Expect.equal
                (views row.Annotations)
                [|
                    ("comment", "b", Some "value b", None, None)
                    ("comment", "a", Some "value a", None, None)
                    ("comment", "Comment", Some "value empty key", None, None)
                |]
                "The protocol detail columns give no annotation. The row keeps the column order and the empty key becomes Comment."

            Expect.equal
                (names data.Processes.[0].Signatures.[0].Annotations)
                [| "Comment"; "a"; "b" |]
                "The signature sorts the comments by ordinal key text."

        testCase "reads a Date column and lets it split a signature"
        <| fun _ ->
            let arc =
                buildFixture (fun gc _ ->
                    let t = gc.Tables.[1]

                    let dates =
                        ResizeArray [
                            for row in 0 .. t.RowCount - 1 -> text (if row = 0 then "2015-01-12" else "")
                        ]

                    t.AddColumn(CompositeHeader.Date, dates)
                )

            let data = ProvenanceGraphData.ofInvestigation arc
            let rowId = gcKey + "#0"
            let proc = processNamed data "gas_chromatography"

            let signature =
                proc.Signatures |> Array.find (fun s -> Array.contains rowId s.RowIds)

            expectContains
                (views (rowWithId data rowId).Annotations)
                ("date", "Date", Some "2015-01-12", None, None)
                "The dated row has a Date annotation."

            Expect.isFalse
                ((rowWithId data (gcKey + "#1")).Annotations
                 |> Array.exists (fun a -> a.Category = AnnotationCategory.Date))
                "An empty Date cell gives no annotation."

            Expect.equal proc.Signatures.Length 5 "The dated row has a signature of its own."
            Expect.equal signature.RowIds [| rowId |] "only the dated row"
            expectContains (names signature.Distinguishing) "Date" "Date distinguishes the signature."

        testCase "reads performer and comment columns as step annotations"
        <| fun _ ->
            let arc =
                buildFixture (fun _ rna ->
                    let t = rna.Tables.[0]

                    let cells (value: string) =
                        ResizeArray [ for _ in 1 .. t.RowCount -> text value ]

                    t.AddColumn(CompositeHeader.Performer, cells "Ada Lovelace")
                    t.AddColumn(CompositeHeader.Comment "lane", cells "L001")
                )

            let row =
                rowWithId (ProvenanceGraphData.ofInvestigation arc) "assays/RNASeq/::rna_extraction#0"

            expectContains
                (views row.Annotations)
                ("performer", "Performer", Some "Ada Lovelace", None, None)
                "performer"

            expectContains (views row.Annotations) ("comment", "lane", Some "L001", None, None) "comment"

        testCase "keeps the rows of a table whose inputs and outputs are removed"
        <| fun _ ->
            let arc =
                buildFixture (fun gc _ ->
                    let t = gc.Tables.[0]
                    let input = t.Headers.FindIndex(fun h -> h.isInput)
                    let output = t.Headers.FindIndex(fun h -> h.isOutput)
                    t.RemoveColumns [| input; output |]
                )

            let data = ProvenanceGraphData.ofInvestigation arc
            let proc = processNamed data "metabolite_extraction"

            Expect.equal data.Processes.Length 7 "The process stays."
            Expect.equal data.Rows.Length 22 "The rows stay."
            Expect.equal proc.RowIds.Length 3 "three rows"
            let first = rowWithId data proc.RowIds.[0]
            Expect.isEmpty first.Inputs "no inputs"
            Expect.isEmpty first.Outputs "no outputs"
            Expect.isEmpty proc.Signatures "Rows without inputs and outputs take no part in signatures."
            Expect.isEmpty proc.InputGroups "no input groups"
            Expect.isEmpty proc.OutputGroups "no output groups"
            Expect.isEmpty proc.GroupEdges "no group edges"

            let gas = processNamed data "gas_chromatography"
            Expect.equal gas.Signatures.Length 4 "Other processes keep their signatures."
    ]

let private signatureTests =
    testList "signatures, groups and edges" [
        testCase "splits gas_chromatography into four signatures by injection mode and sample type"
        <| fun _ ->
            let proc = processNamed (fixtureData ()) "gas_chromatography"

            Expect.equal proc.Signatures.Length 4 "four signatures"

            for signature in proc.Signatures do
                Expect.equal
                    (names signature.Distinguishing)
                    [| "Chromatography injection mode"; "MS sample type" |]
                    "Both parameters distinguish the signatures."

            Expect.equal
                (proc.Signatures |> Array.map (fun s -> s.Id))
                [| gcKey + "$0"; gcKey + "$1"; gcKey + "$2"; gcKey + "$3" |]
                "Signatures number in order of first appearance."

            Expect.equal (proc.Signatures |> Array.map (fun s -> s.RowIds.Length)) [| 2; 2; 3; 2 |] "Rows per signature"

        testCase "groups the gas_chromatography inputs and outputs by the signatures they feed"
        <| fun _ ->
            let proc = processNamed (fixtureData ()) "gas_chromatography"

            Expect.equal (proc.InputGroups |> Array.map (fun g -> g.Label)) [| "Inputs A"; "Inputs B" |] "input labels"

            Expect.equal
                (proc.InputGroups |> Array.map (fun g -> sortedStrings g.NodeIds))
                [| [| "blank 1"; "blank 2" |]; [| "DB23"; "DB24" |] |]
                "The blanks feed the same signatures, so they share a group."

            Expect.equal
                (proc.InputGroups |> Array.map (fun g -> g.SignatureIds))
                [|
                    [| gcKey + "$0"; gcKey + "$1" |]
                    [| gcKey + "$2"; gcKey + "$3" |]
                |]
                "signature ids in index order"

            Expect.equal
                (proc.InputGroups |> Array.map (fun g -> g.Id))
                [| gcKey + "#in0"; gcKey + "#in1" |]
                "input group ids"

            Expect.equal
                (proc.OutputGroups |> Array.map (fun g -> g.Label))
                [| "Outputs A"; "Outputs B"; "Outputs C"; "Outputs D" |]
                "output labels"

            Expect.equal
                (proc.OutputGroups |> Array.map (fun g -> g.Id))
                [|
                    gcKey + "#out0"
                    gcKey + "#out1"
                    gcKey + "#out2"
                    gcKey + "#out3"
                |]
                "output group ids"

            Expect.equal
                (proc.InputGroups |> Array.map (fun g -> IoSide.toText g.Side))
                [| "inputs"; "inputs" |]
                "input side"

            Expect.equal
                (proc.OutputGroups |> Array.map (fun g -> g.NodeIds))
                [|
                    [| "raw1.raw"; "raw2.raw" |]
                    [| "raw3.raw"; "raw4.raw" |]
                    [| "raw5.raw"; "raw6.raw"; "raw9.raw" |]
                    [| "raw7.raw"; "raw8.raw" |]
                |]
                "Nodes keep their first-seen order inside a group."

        testCase "aggregates the gas_chromatography rows into four group edges"
        <| fun _ ->
            let proc = processNamed (fixtureData ()) "gas_chromatography"

            Expect.equal proc.GroupEdges.Length 4 "four edges"

            Expect.equal
                (proc.GroupEdges |> Array.map (fun e -> e.RowIds.Length) |> Array.sort)
                [| 2; 2; 2; 3 |]
                "rows per edge"

            Expect.equal
                (proc.GroupEdges |> Array.map (fun e -> e.Id))
                [| gcKey + "@0"; gcKey + "@1"; gcKey + "@2"; gcKey + "@3" |]
                "edge ids"

            Expect.equal
                (proc.GroupEdges |> Array.map (fun e -> e.From, e.To, e.SignatureId))
                [|
                    (gcKey + "#in0", gcKey + "#out0", gcKey + "$0")
                    (gcKey + "#in0", gcKey + "#out1", gcKey + "$1")
                    (gcKey + "#in1", gcKey + "#out2", gcKey + "$2")
                    (gcKey + "#in1", gcKey + "#out3", gcKey + "$3")
                |]
                "An edge joins an input group to an output group for one signature."

        testCase "gives each process the expected numbers of signatures, groups and edges"
        <| fun _ ->
            let data = fixtureData ()

            let counts name =
                let p = processNamed data name
                p.Signatures.Length, p.InputGroups.Length, p.OutputGroups.Length, p.GroupEdges.Length

            Expect.equal (counts "plant_material") (2, 2, 2, 2) "two humidity values"
            Expect.equal (counts "kallisto-quant") (2, 2, 1, 2) "two inputs feed one output"
            Expect.equal (counts "mh-quant-results") (1, 1, 1, 1) "uniform rows"

        testCase "ignores the characteristics a sample has in other tables when it compares rows"
        <| fun _ ->
            let arc =
                buildFixture (fun _ rna ->
                    rna.AddTable(
                        table "storage_check" [
                            CompositeHeader.Input IOType.Sample
                            CompositeHeader.Characteristic(oa "storage")
                        ] [ [ text "DB23"; term "frozen" ] ]
                    )
                )

            let data = ProvenanceGraphData.ofInvestigation arc
            let gas = processNamed data "gas_chromatography"
            let check = processNamed data "storage_check"

            expectContains
                (views (nodeWithId data "DB23").Annotations)
                ("characteristic", "storage", Some "frozen", None, None)
                "The node keeps the characteristic from the other table."

            Expect.equal check.Signatures.Length 1 "one signature"

            expectContains
                (views check.Signatures.[0].Annotations)
                ("characteristic", "storage", Some "frozen", None, None)
                "The signature of the table that has the column contains it."

            Expect.equal gas.Signatures.Length 4 "still four signatures"

            for signature in gas.Signatures do
                Expect.isFalse (Array.contains "storage" (names signature.Annotations)) "no storage annotation"

                Expect.equal
                    (names signature.Distinguishing)
                    [| "Chromatography injection mode"; "MS sample type" |]
                    "same distinguishing annotations"

            Expect.equal
                (gas.InputGroups |> Array.map (fun g -> sortedStrings g.NodeIds))
                [| [| "blank 1"; "blank 2" |]; [| "DB23"; "DB24" |] |]
                "same input groups"

            Expect.equal
                (gas.GroupEdges |> Array.map (fun e -> e.RowIds.Length) |> Array.sort)
                [| 2; 2; 2; 3 |]
                "same edges"
    ]

let private nodeTests =
    testList "nodes" [
        testCase "keeps a sample in the datasets and processes that use it"
        <| fun _ ->
            let node = nodeWithId (fixtureData ()) "CAM_01"

            Expect.equal (IoKind.toText node.Kind) "sample" "kind"

            Expect.equal
                node.DatasetIds
                [|
                    "studies/SamplesStudy/"
                    "assays/GC/"
                    "assays/RNASeq/"
                |]
                "datasets in order of first use"

            Expect.equal
                node.ProcessKeys
                [|
                    "studies/SamplesStudy/::plant_material"
                    "assays/GC/::metabolite_extraction"
                    "assays/RNASeq/::rna_extraction"
                |]
                "processes in order of first use"

        testCase "merges the characteristics of a source"
        <| fun _ ->
            let node = nodeWithId (fixtureData ()) "DB_097"

            Expect.equal (IoKind.toText node.Kind) "source" "kind"

            Expect.equal
                (views node.Annotations)
                [|
                    ("characteristic", "organism", Some "Talinum fruticosum", None, None)
                |]
                "one characteristic"

        testCase "merges equal characteristics of one node over several rows once"
        <| fun _ ->
            let arc =
                investigation
                    [
                        studyOf "S" [
                            table "t" [
                                CompositeHeader.Input IOType.Source
                                CompositeHeader.Characteristic(oa "organism")
                                CompositeHeader.Output IOType.Sample
                            ] [
                                [ text "src"; term "Talinum"; text "a" ]
                                [ text "src"; term "Talinum"; text "b" ]
                                [ text "src"; term "Arabidopsis"; text "c" ]
                            ]
                        ]
                    ] [] []

            let node = nodeWithId (ProvenanceGraphData.ofInvestigation arc) "src"

            Expect.equal
                (views node.Annotations)
                [|
                    ("characteristic", "organism", Some "Talinum", None, None)
                    ("characteristic", "organism", Some "Arabidopsis", None, None)
                |]
                "Equal annotations count once, the others keep first-seen order."

        testCase "puts the factors of a row on its output sample"
        <| fun _ ->
            let node = nodeWithId (fixtureData ()) "CAM_01"

            Expect.equal
                (views node.Annotations)
                [|
                    ("factor", "Photosynthesis mode", Some "CAM", None, None)
                |]
                "one factor"

        testCase "names data nodes by their path without a leading ./"
        <| fun _ ->
            let data = fixtureData ()
            let node = nodeWithId data "results/abundance.h5"

            Expect.equal (IoKind.toText node.Kind) "data" "kind"

            Expect.isFalse (data.Nodes |> Array.exists (fun n -> n.Id.StartsWith "./")) "No node id starts with ./"

        testCase "maps the IO types to kinds and treats FreeText as data"
        <| fun _ ->
            let arc =
                investigation
                    [
                        studyOf "S" [
                            table "t" [
                                CompositeHeader.Input IOType.Source
                                CompositeHeader.Output(IOType.FreeText "Output [Thing]")
                            ] [ [ text "src"; text "thing" ] ]
                            table "u" [
                                CompositeHeader.Input IOType.Material
                                CompositeHeader.Output IOType.Sample
                            ] [ [ text "mat"; text "smp" ] ]
                        ]
                    ] [] []

            let data = ProvenanceGraphData.ofInvestigation arc
            let kind id = IoKind.toText (nodeWithId data id).Kind

            Expect.equal
                [| kind "src"; kind "thing"; kind "mat"; kind "smp" |]
                [| "source"; "data"; "material"; "sample" |]
                "Source, FreeText, Material and Sample"

        testCase "links a node to the rows that produce and consume it"
        <| fun _ ->
            let data = fixtureData ()
            let index = ProvenanceIndex.ofData data
            let processKeyOf (rowId: string) = (index.RowById.[rowId]).ProcessKey

            Expect.equal
                (index.ProducedBy.["CAM_01"] |> Array.map processKeyOf)
                [| "studies/SamplesStudy/::plant_material" |]
                "producedBy"

            Expect.equal
                (index.ConsumedBy.["CAM_01"] |> Array.map processKeyOf)
                [|
                    "assays/GC/::metabolite_extraction"
                    "assays/RNASeq/::rna_extraction"
                |]
                "consumedBy lists the rows in row order"
    ]

let private keyTests =
    let annotation category name value unit accession : Annotation = {
        Name = name
        Value = value
        Unit = unit
        Accession = accession
        Category = category
    }

    testList "annotation keys and kinds" [
        testCase "annotationKey lists the five fields in brackets"
        <| fun _ ->
            Expect.equal
                (annotationKey (annotation AnnotationCategory.Parameter "temperature" (Some "20") None None))
                """["temperature","20",null,null,"parameter"]"""
                "plain fields"

        testCase "annotationKey keeps fields apart"
        <| fun _ ->
            let key = annotationKey

            Expect.notEqual
                (key (annotation AnnotationCategory.Factor "n" None None None))
                (key (annotation AnnotationCategory.Factor "n" (Some "null") None None))
                "None and Some \"null\" differ."

            Expect.notEqual
                (key (annotation AnnotationCategory.Factor "a|b" (Some "c") None None))
                (key (annotation AnnotationCategory.Factor "a" (Some "b|c") None None))
                "A separator inside a field does not move the field boundary."

            Expect.notEqual
                (key (annotation AnnotationCategory.Factor "a\",\"b" None None None))
                (key (annotation AnnotationCategory.Factor "a" (Some "b") None None))
                "A JSON separator inside a field does not move the field boundary."

            Expect.equal
                (key (annotation AnnotationCategory.Factor "n" (Some "v") None None))
                (key (annotation AnnotationCategory.Factor "n" (Some "v") None None))
                "Equal annotations have equal keys."

        testCase "toText gives the literal of every kind"
        <| fun _ ->
            Expect.equal
                [|
                    DatasetKind.toText DatasetKind.Study
                    DatasetKind.toText DatasetKind.Assay
                    DatasetKind.toText DatasetKind.Run
                |]
                [| "study"; "assay"; "run" |]
                "DatasetKind"

            Expect.equal
                [|
                    IoKind.toText IoKind.Source
                    IoKind.toText IoKind.Sample
                    IoKind.toText IoKind.Material
                    IoKind.toText IoKind.Data
                |]
                [| "source"; "sample"; "material"; "data" |]
                "IoKind"

            Expect.equal
                ([|
                    AnnotationCategory.Characteristic
                    AnnotationCategory.Factor
                    AnnotationCategory.Parameter
                    AnnotationCategory.Component
                    AnnotationCategory.Protocol
                    AnnotationCategory.Performer
                    AnnotationCategory.Date
                    AnnotationCategory.Comment
                 |]
                 |> Array.map AnnotationCategory.toText)
                [|
                    "characteristic"
                    "factor"
                    "parameter"
                    "component"
                    "protocol"
                    "performer"
                    "date"
                    "comment"
                |]
                "AnnotationCategory"

            Expect.equal
                [|
                    IoSide.toText IoSide.Inputs
                    IoSide.toText IoSide.Outputs
                |]
                [| "inputs"; "outputs" |]
                "IoSide"

#if FABLE_COMPILER
        testCase "kinds are plain strings in JavaScript"
        <| fun _ ->
            Expect.equal (unbox<string> DatasetKind.Study) "study" "DatasetKind"
            Expect.equal (unbox<string> IoKind.Material) "material" "IoKind"
            Expect.equal (unbox<string> AnnotationCategory.Characteristic) "characteristic" "AnnotationCategory"
            Expect.equal (unbox<string> IoSide.Outputs) "outputs" "IoSide"
#endif
    ]

// Work in progress ARC: rows without an output, rows without an input, a table with an Input column only, a table
// whose headers have no cells filled, and a table with a protocol only. The same ARC serves the query tests.

/// One assay Draft with five tables of 12 rows. Every row is unfinished except the first of outputs_missing and the
/// second of inputs_missing.
let wipInvestigation () =
    let headers = [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.ProtocolREF
        CompositeHeader.Output IOType.Sample
    ]

    let row input output = [ text input; text "draft.md"; text output ]

    let outputsMissing =
        table "outputs_missing" headers [ row "a1" "b1"; row "a2" ""; row "a3" "" ]

    let inputsMissing = table "inputs_missing" headers [ row "" "c1"; row "d1" "d2" ]

    let onlyInput =
        table "only_input" [ CompositeHeader.Input IOType.Sample ] [ [ text "e1" ]; [ text "e2" ] ]

    let unfilled = ArcTable.init "unfilled"
    unfilled.AddColumn(CompositeHeader.Input IOType.Sample)
    unfilled.AddColumn(CompositeHeader.Output IOType.Sample)
    unfilled.AddRowsEmpty 3

    let protocolOnly =
        table "protocol_only" [ CompositeHeader.ProtocolREF ] [ [ text "plan.md" ]; [ text "plan.md" ] ]

    investigation [] [
        assayOf "Draft" [
            outputsMissing
            inputsMissing
            onlyInput
            unfilled
            protocolOnly
        ]
    ] []

let private wipData () =
    ProvenanceGraphData.ofInvestigation (wipInvestigation ())

let private draftKey (table: string) = $"assays/Draft/::{table}"

let private wipTests =
    testList "work in progress" [
        testCase "builds the data without an error and keeps a process for every table with rows"
        <| fun _ ->
            let data = wipData ()

            Expect.equal
                (data.Processes |> Array.map (fun p -> p.Name, p.RowIds.Length))
                [|
                    ("outputs_missing", 3)
                    ("inputs_missing", 2)
                    ("only_input", 2)
                    ("unfilled", 3)
                    ("protocol_only", 2)
                |]
                "Every table keeps its rows."

            Expect.equal data.Rows.Length 12 "all rows"

        testCase "keeps the side of an unfinished row that has a value"
        <| fun _ ->
            let data = wipData ()

            let ends (id: string) =
                (rowWithId data id).Inputs, (rowWithId data id).Outputs

            Expect.equal (ends (draftKey "outputs_missing" + "#0")) ([| "a1" |], [| "b1" |]) "finished row"
            Expect.equal (ends (draftKey "outputs_missing" + "#1")) ([| "a2" |], [||]) "no output"
            Expect.equal (ends (draftKey "inputs_missing" + "#0")) ([||], [| "c1" |]) "no input"
            Expect.equal (ends (draftKey "only_input" + "#1")) ([| "e2" |], [||]) "input column only"
            Expect.equal (ends (draftKey "unfilled" + "#2")) ([||], [||]) "headers without cells"
            Expect.equal (ends (draftKey "protocol_only" + "#0")) ([||], [||]) "protocol only"

        testCase "keeps the nodes of unfinished rows and makes no node of an empty cell"
        <| fun _ ->
            let data = wipData ()
            let ids = data.Nodes |> Array.map (fun n -> n.Id) |> Array.sort

            Expect.equal
                ids
                [| "a1"; "a2"; "a3"; "b1"; "c1"; "d1"; "d2"; "e1"; "e2" |]
                "Input and output nodes of unfinished rows stay, empty cells make no node."

        testCase "puts the unfinished rows into the groups and creates no edge for a missing end"
        <| fun _ ->
            let data = wipData ()
            let processOf name = processNamed data name

            let outputsMissing = processOf "outputs_missing"

            Expect.equal
                (outputsMissing.InputGroups |> Array.map (fun g -> sortedStrings g.NodeIds))
                [| [| "a1"; "a2"; "a3" |] |]
                "The inputs of the unfinished rows share the group of the finished row."

            Expect.equal (outputsMissing.OutputGroups |> Array.map (fun g -> g.NodeIds)) [| [| "b1" |] |] "outputs"

            Expect.equal
                (outputsMissing.GroupEdges |> Array.map (fun e -> e.RowIds))
                [| [| draftKey "outputs_missing" + "#0" |] |]
                "one edge"

            let inputsMissing = processOf "inputs_missing"
            Expect.equal (inputsMissing.InputGroups |> Array.map (fun g -> g.NodeIds)) [| [| "d1" |] |] "inputs"

            Expect.equal
                (inputsMissing.OutputGroups |> Array.map (fun g -> sortedStrings g.NodeIds))
                [| [| "c1"; "d2" |] |]
                "outputs"

            Expect.equal
                (inputsMissing.GroupEdges |> Array.map (fun e -> e.RowIds))
                [| [| draftKey "inputs_missing" + "#1" |] |]
                "one edge"

            let onlyInput = processOf "only_input"

            Expect.equal
                (onlyInput.InputGroups |> Array.map (fun g -> sortedStrings g.NodeIds))
                [| [| "e1"; "e2" |] |]
                "inputs"

            Expect.isEmpty onlyInput.OutputGroups "no output group"
            Expect.isEmpty onlyInput.GroupEdges "no edge"

            for name in [ "unfilled"; "protocol_only" ] do
                let p = processOf name
                Expect.isEmpty p.Signatures $"{name}: no signature"
                Expect.isEmpty p.InputGroups $"{name}: no input group"
                Expect.isEmpty p.OutputGroups $"{name}: no output group"
                Expect.isEmpty p.GroupEdges $"{name}: no edge"

            Expect.equal
                (data.Processes |> Array.sumBy (fun p -> p.GroupEdges.Length))
                2
                "Only the two finished rows make an edge."
    ]

let tests =
    testList "ProvenanceGraph BuildIndex" [
        datasetTests
        rowTests
        signatureTests
        nodeTests
        keyTests
        wipTests
    ]
