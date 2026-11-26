module ProvenanceGraphQueriesTests

// These tests port tests/arc/provenanceQueries.test.ts of arc-portal-ui. The index
// comes from hand-built data through ProvenanceIndex.ofData, like the fixture of PORTAL.
//
// Dropped PORTAL cases:
// - "yields one edge for a node that 300 rows of one dataset produce and 300 rows
//   of another consume" is a performance case.
// - "leave the index unchanged" checks in JavaScript that the functions do not
//   mutate the index. tests/Electron.Core/ProvenanceGraphReader.test.fs runs every
//   query on a structuredClone of the data instead.
//
// Added cases: the fixed kind order of kindCounts and the edge order of the shared
// node edges, because the F# port builds both from explicit arrays.
//
// Records have no equality, so the tests compare projections (ids, toText values,
// annotation tuples and tuples of strings).

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open Arcitisa.Components.Shared.ProvenanceGraph.Types
open Arcitisa.Components.Shared.ProvenanceGraph.BuildIndex
open Arcitisa.Components.Shared.ProvenanceGraph.Queries

// Builders

let private ann category name value : Annotation = {
    Name = name
    Value = value
    Unit = None
    Accession = None
    Category = category
}

let private node id kind annotations : ProvenanceNode = {
    Id = id
    Kind = kind
    Annotations = annotations
    // The queries read neither DatasetIds nor ProcessKeys.
    DatasetIds = [||]
    ProcessKeys = [||]
}

let private row id processKey datasetId inputs outputs annotations : ProvenanceRow = {
    Id = id
    Name = id
    ProcessKey = processKey
    DatasetId = datasetId
    Inputs = inputs
    Outputs = outputs
    Annotations = annotations
}

let private group id side label nodeIds signatureIds : IoGroup = {
    Id = id
    Side = side
    Label = label
    NodeIds = nodeIds
    SignatureIds = signatureIds
}

let private dataset id kind : ProvenanceDataset = {
    Id = id
    Kind = kind
    Identifier = id
}

/// A process whose rows all share one signature: one input group, one output group, one group edge.
let private uniformProcess key datasetId (rows: ProvenanceRow[]) : ProvenanceProcess =
    let signatureId = key + "#sig"
    let rowIds = rows |> Array.map (fun r -> r.Id)

    let inputGroup =
        group (key + "#in") IoSide.Inputs "Inputs A" (rows |> Array.collect (fun r -> r.Inputs) |> Array.distinct) [|
            signatureId
        |]

    let outputGroup =
        group (key + "#out") IoSide.Outputs "Outputs A" (rows |> Array.collect (fun r -> r.Outputs) |> Array.distinct) [|
            signatureId
        |]

    {
        Key = key
        Name =
            match key.Split ':' with
            | [| _; name |] -> name
            | _ -> key
        DatasetId = datasetId
        RowIds = rowIds
        Signatures = [|
            {
                Id = signatureId
                RowIds = rowIds
                Annotations = rows.[0].Annotations
                Distinguishing = [||]
            }
        |]
        InputGroups = [| inputGroup |]
        OutputGroups = [| outputGroup |]
        GroupEdges = [|
            {
                Id = key + "#e1"
                From = inputGroup.Id
                To = outputGroup.Id
                SignatureId = signatureId
                RowIds = rowIds
            }
        |]
    }

/// Fills every lookup from the arrays, like the real index builder does.
let private buildIndex datasets processes rows nodes : ProvenanceIndex =
    let data: ProvenanceGraphData = {
        Datasets = datasets
        Processes = processes
        Rows = rows
        Nodes = nodes
    }

    ProvenanceIndex.ofData data

// Projections

type private AnnotationView = string * string * string option

let private view (annotation: Annotation) : AnnotationView =
    AnnotationCategory.toText annotation.Category, annotation.Name, annotation.Value

let private views (annotations: Annotation[]) : AnnotationView[] = annotations |> Array.map view

let private getSome (value: 'a option) : 'a =
    match value with
    | Some value -> value
    | None -> failwith "Expected Some, got None."

let private sharedEdgeViews (edges: SharedNodeEdge[]) =
    edges |> Array.map (fun e -> e.Id, e.From, e.To, e.SharedNodeIds)

let private nodeIds (nodes: DetailNode[]) = nodes |> Array.map (fun n -> n.Id)
let private edgeIds (edges: DetailEdge[]) = edges |> Array.map (fun e -> e.Id)

// Fixture
//
// Study S: sampling makes samples S1, S2 from sources A, B.
// Assay As: extraction S1 -> E1, S2 -> E2, then measurement E1 -> F1 (split),
// E1 -> F2 (splitless), E2 -> F3 (split).

let private protocol value =
    ann AnnotationCategory.Protocol "protocol" (Some value)

let private split = ann AnnotationCategory.Parameter "mode" (Some "split")
let private splitless = ann AnnotationCategory.Parameter "mode" (Some "splitless")

let private organism =
    ann AnnotationCategory.Characteristic "organism" (Some "Talinum")

let private fixture () : ProvenanceIndex =
    let sampling = [|
        row "s1" "S:sampling" "S" [| "A" |] [| "S1" |] [| protocol "collect" |]
        row "s2" "S:sampling" "S" [| "B" |] [| "S2" |] [| protocol "collect" |]
    |]

    let extraction = [|
        row "x1" "As:extraction" "As" [| "S1" |] [| "E1" |] [| protocol "extract" |]
        row "x2" "As:extraction" "As" [| "S2" |] [| "E2" |] [| protocol "extract" |]
    |]

    let measurement = [|
        row "m1" "As:measurement" "As" [| "E1" |] [| "F1" |] [| protocol "GC-MS"; split |]
        row "m2" "As:measurement" "As" [| "E1" |] [| "F2" |] [| protocol "GC-MS"; splitless |]
        row "m3" "As:measurement" "As" [| "E2" |] [| "F3" |] [| protocol "GC-MS"; split |]
    |]

    let sigSplit = "As:measurement#split"
    let sigSplitless = "As:measurement#splitless"

    let measurementProcess: ProvenanceProcess = {
        Key = "As:measurement"
        Name = "measurement"
        DatasetId = "As"
        RowIds = [| "m1"; "m2"; "m3" |]
        Signatures = [|
            {
                Id = sigSplit
                RowIds = [| "m1"; "m3" |]
                Annotations = [| protocol "GC-MS"; split |]
                Distinguishing = [| split |]
            }
            {
                Id = sigSplitless
                RowIds = [| "m2" |]
                Annotations = [| protocol "GC-MS"; splitless |]
                Distinguishing = [| splitless |]
            }
        |]
        InputGroups = [|
            group "As:measurement#in1" IoSide.Inputs "Inputs A" [| "E1" |] [| sigSplit; sigSplitless |]
            group "As:measurement#in2" IoSide.Inputs "Inputs B" [| "E2" |] [| sigSplit |]
        |]
        OutputGroups = [|
            group "As:measurement#out1" IoSide.Outputs "Outputs A" [| "F1"; "F3" |] [| sigSplit |]
            group "As:measurement#out2" IoSide.Outputs "Outputs B" [| "F2" |] [| sigSplitless |]
        |]
        GroupEdges = [|
            {
                Id = "As:measurement#e1"
                From = "As:measurement#in1"
                To = "As:measurement#out1"
                SignatureId = sigSplit
                RowIds = [| "m1" |]
            }
            {
                Id = "As:measurement#e2"
                From = "As:measurement#in2"
                To = "As:measurement#out1"
                SignatureId = sigSplit
                RowIds = [| "m3" |]
            }
            {
                Id = "As:measurement#e3"
                From = "As:measurement#in1"
                To = "As:measurement#out2"
                SignatureId = sigSplitless
                RowIds = [| "m2" |]
            }
        |]
    }

    buildIndex
        [|
            dataset "S" DatasetKind.Study
            dataset "As" DatasetKind.Assay
        |]
        [|
            uniformProcess "S:sampling" "S" sampling
            uniformProcess "As:extraction" "As" extraction
            measurementProcess
        |]
        (Array.concat [ sampling; extraction; measurement ])
        [|
            node "A" IoKind.Source [| organism |]
            node "B" IoKind.Source [||]
            node "S1" IoKind.Sample [||]
            node "S2" IoKind.Sample [||]
            node "E1" IoKind.Material [||]
            node "E2" IoKind.Material [||]
            node "F1" IoKind.Data [||]
            node "F2" IoKind.Data [||]
            node "F3" IoKind.Data [||]
        |]

// Tests

let private datasetGraphTests =
    testList "datasetGraph" [
        testCase "connects the study to the assay through the samples the assay consumes"
        <| fun _ ->
            let graph = datasetGraph (fixture ())

            Expect.equal
                (graph.Nodes |> Array.map (fun n -> n.Id, DatasetKind.toText n.Kind, n.Label))
                [| ("S", "study", "S"); ("As", "assay", "As") |]
                "One node per dataset."

            Expect.equal
                (sharedEdgeViews graph.Edges)
                [| ("S->As", "S", "As", [| "S1"; "S2" |]) |]
                "Edges inside one dataset are left out."

        testCase "orders the edges by the order in which rows first produce the nodes"
        <| fun _ ->
            // Node order is p, q, x, z, w. The produced nodes appear as q, p, z, w.
            let rows = [|
                row "r1" "D1:p" "D1" [| "p" |] [| "q" |] [||]
                row "r2" "D2:p" "D2" [| "x" |] [| "p" |] [||]
                row "r3" "D3:p" "D3" [| "q" |] [| "z" |] [||]
                row "r4" "D3:p" "D3" [| "p" |] [| "w" |] [||]
            |]

            let index =
                buildIndex
                    [|
                        dataset "D1" DatasetKind.Assay
                        dataset "D2" DatasetKind.Assay
                        dataset "D3" DatasetKind.Assay
                    |]
                    [||]
                    rows
                    [|
                        node "p" IoKind.Data [||]
                        node "q" IoKind.Data [||]
                        node "x" IoKind.Data [||]
                        node "z" IoKind.Data [||]
                        node "w" IoKind.Data [||]
                    |]

            Expect.equal
                (sharedEdgeViews (datasetGraph index).Edges)
                [|
                    ("D1->D3", "D1", "D3", [| "q" |])
                    ("D2->D1", "D2", "D1", [| "p" |])
                    ("D2->D3", "D2", "D3", [| "p" |])
                |]
                "The first produced node (q) decides the first edge."
    ]

let private processGraphTests =
    testList "processGraph" [
        testCase "adds the external study process and links extraction to measurement inside the assay"
        <| fun _ ->
            let graph = getSome (processGraph (fixture ()) "As")

            Expect.equal graph.Dataset.Identifier "As" "dataset"

            Expect.equal
                (graph.Nodes
                 |> Array.map (fun n ->
                     n.Key,
                     n.Label,
                     n.DatasetId,
                     DatasetKind.toText n.DatasetKind,
                     n.DatasetLabel,
                     n.External,
                     n.RowCount
                 ))
                [|
                    ("S:sampling", "sampling", "S", "study", "S", true, 2)
                    ("As:extraction", "extraction", "As", "assay", "As", false, 2)
                    ("As:measurement", "measurement", "As", "assay", "As", false, 3)
                |]
                "nodes"

            Expect.equal
                (sharedEdgeViews graph.Edges)
                [|
                    ("S:sampling->As:extraction", "S:sampling", "As:extraction", [| "S1"; "S2" |])
                    ("As:extraction->As:measurement", "As:extraction", "As:measurement", [| "E1"; "E2" |])
                |]
                "edges"

        testCase "leaves out edges and processes that do not touch the selected dataset"
        <| fun _ ->
            let graph = getSome (processGraph (fixture ()) "S")

            Expect.equal
                (graph.Nodes |> Array.map (fun n -> n.Key, n.External))
                [| ("S:sampling", false); ("As:extraction", true) |]
                "nodes"

            Expect.equal (graph.Edges |> Array.map (fun e -> e.Id)) [| "S:sampling->As:extraction" |] "edges"
    ]

let private processGroupsTests =
    let graph () =
        getSome (processGroups (fixture ()) "As:measurement")

    testList "processGroups" [
        testCase "lists input groups before output groups with the kinds of their members"
        <| fun _ ->
            let graph = graph ()
            Expect.equal (DatasetKind.toText graph.DatasetKind) "assay" "datasetKind"

            Expect.equal
                (graph.Nodes
                 |> Array.map (fun n ->
                     n.Id,
                     IoSide.toText n.Side,
                     n.Label,
                     n.KindCounts |> Array.map (fun (kind, count) -> IoKind.toText kind, count)
                 ))
                [|
                    ("As:measurement#in1", "inputs", "Inputs A", [| ("material", 1) |])
                    ("As:measurement#in2", "inputs", "Inputs B", [| ("material", 1) |])
                    ("As:measurement#out1", "outputs", "Outputs A", [| ("data", 2) |])
                    ("As:measurement#out2", "outputs", "Outputs B", [| ("data", 1) |])
                |]
                "nodes"

        testCase "splits each edge into the distinguishing annotations and the rest"
        <| fun _ ->
            let graph = graph ()

            Expect.equal
                (graph.Edges
                 |> Array.map (fun e -> e.Id, e.From, e.To, views e.Differing, views e.Shared))
                [|
                    ("As:measurement#e1",
                     "As:measurement#in1",
                     "As:measurement#out1",
                     views [| split |],
                     views [| protocol "GC-MS" |])
                    ("As:measurement#e2",
                     "As:measurement#in2",
                     "As:measurement#out1",
                     views [| split |],
                     views [| protocol "GC-MS" |])
                    ("As:measurement#e3",
                     "As:measurement#in1",
                     "As:measurement#out2",
                     views [| splitless |],
                     views [| protocol "GC-MS" |])
                |]
                "edges"

        testCase "counts the kinds in the fixed order source, sample, material, data and omits zero counts"
        <| fun _ ->
            let rows = [| row "r1" "D:p" "D" [||] [||] [||] |]

            let proc = uniformProcess "D:p" "D" rows

            let proc = {
                proc with
                    InputGroups = [|
                        group "D:p#in" IoSide.Inputs "Inputs A" [| "d1"; "m1"; "smp"; "d2"; "src" |] [||]
                    |]
                    OutputGroups = [||]
            }

            let index =
                buildIndex [| dataset "D" DatasetKind.Run |] [| proc |] rows [|
                    node "d1" IoKind.Data [||]
                    node "m1" IoKind.Material [||]
                    node "smp" IoKind.Sample [||]
                    node "d2" IoKind.Data [||]
                    node "src" IoKind.Source [||]
                |]

            let graph = getSome (processGroups index "D:p")

            Expect.equal
                (graph.Nodes.[0].KindCounts
                 |> Array.map (fun (kind, count) -> IoKind.toText kind, count))
                [|
                    ("source", 1)
                    ("sample", 1)
                    ("material", 1)
                    ("data", 2)
                |]
                "Fixed order, whatever the order of the members."

            let withoutSample = {
                proc with
                    InputGroups = [|
                        group "D:p#in" IoSide.Inputs "Inputs A" [| "d1"; "missing" |] [||]
                    |]
            }

            let graph =
                getSome (
                    processGroups
                        (buildIndex [| dataset "D" DatasetKind.Run |] [| withoutSample |] rows [|
                            node "d1" IoKind.Data [||]
                        |])
                        "D:p"
                )

            Expect.equal
                (graph.Nodes.[0].KindCounts
                 |> Array.map (fun (kind, count) -> IoKind.toText kind, count))
                [| ("data", 2) |]
                "A member without a node entry counts as data, zero counts are left out."
    ]

let private processDetailTests =
    testList "processDetail" [
        testCase "emphasizes the group members and adds the output nodes of the rows that consume them"
        <| fun _ ->
            let detail = getSome (processDetail (fixture ()) "As:measurement#in1")

            Expect.equal detail.Process.Key "As:measurement" "process"
            Expect.equal detail.Group.Id "As:measurement#in1" "group"

            Expect.equal
                (detail.Nodes |> Array.map (fun n -> n.Id, n.Emphasized))
                [| ("E1", true); ("F1", false); ("F2", false) |]
                "nodes"

            Expect.equal
                (detail.Edges |> Array.map (fun e -> e.Id, e.From, e.To))
                [| ("m1|E1|F1", "E1", "F1"); ("m2|E1|F2", "E1", "F2") |]
                "edges"

        testCase "puts the varying parameter in differing and the protocol in shared"
        <| fun _ ->
            let edges = (getSome (processDetail (fixture ()) "As:measurement#in1")).Edges

            Expect.equal edges.[0].ProcessKey "As:measurement" "processKey"
            Expect.equal edges.[0].ProcessName "measurement" "processName"
            Expect.equal (DatasetKind.toText edges.[0].DatasetKind) "assay" "datasetKind"
            Expect.equal (views edges.[0].Differing) (views [| split |]) "differing of the first edge"
            Expect.equal (views edges.[0].Shared) (views [| protocol "GC-MS" |]) "shared of the first edge"
            Expect.equal (views edges.[1].Differing) (views [| splitless |]) "differing of the second edge"
            Expect.equal (views edges.[1].Shared) (views [| protocol "GC-MS" |]) "shared of the second edge"

        testCase "adds the input nodes of the rows that produce the members of an output group"
        <| fun _ ->
            let detail = getSome (processDetail (fixture ()) "As:measurement#out1")

            Expect.equal
                (detail.Nodes |> Array.map (fun n -> n.Id, n.Emphasized))
                [|
                    ("F1", true)
                    ("F3", true)
                    ("E1", false)
                    ("E2", false)
                |]
                "nodes"

            Expect.equal (edgeIds detail.Edges) [| "m1|E1|F1"; "m3|E2|F3" |] "edges"

            Expect.equal
                (detail.Edges |> Array.map (fun e -> e.Differing.Length))
                [| 0; 0 |]
                "Both edges carry the same annotations, so nothing differs."

        testCase "shares every annotation when the process has a single edge in the graph"
        <| fun _ ->
            let edges = (getSome (processDetail (fixture ()) "As:measurement#in2")).Edges

            Expect.equal edges.Length 1 "one edge"
            Expect.isEmpty edges.[0].Differing "nothing differs"
            Expect.equal (views edges.[0].Shared) (views [| protocol "GC-MS"; split |]) "everything is shared"

        testCase "connects only the rows whose input is in the group when two groups share an output"
        <| fun _ ->
            let rows = [|
                row "r1" "D:p" "D" [| "a" |] [| "c" |] [||]
                row "r2" "D:p" "D" [| "b" |] [| "c" |] [||]
            |]

            let proc = {
                uniformProcess "D:p" "D" rows with
                    InputGroups = [|
                        group "g-a" IoSide.Inputs "Inputs A" [| "a" |] [||]
                        group "g-b" IoSide.Inputs "Inputs B" [| "b" |] [||]
                    |]
            }

            let index =
                buildIndex [| dataset "D" DatasetKind.Run |] [| proc |] rows [|
                    node "a" IoKind.Data [||]
                    node "b" IoKind.Data [||]
                    node "c" IoKind.Data [||]
                |]

            let detail = getSome (processDetail index "g-a")
            Expect.equal (nodeIds detail.Nodes) [| "a"; "c" |] "nodes"
            Expect.equal (edgeIds detail.Edges) [| "r1|a|c" |] "edges"
    ]

let private lineageTests =
    testList "lineage" [
        testCase "follows S1 upstream to its source and downstream to the measurements, without sibling branches"
        <| fun _ ->
            let graph = getSome (lineage (fixture ()) "S1")

            Expect.equal graph.NodeId "S1" "nodeId"
            Expect.equal (nodeIds graph.Nodes |> Array.sort) [| "A"; "E1"; "F1"; "F2"; "S1" |] "nodes"

            Expect.equal
                (graph.Nodes |> Array.filter (fun n -> n.Emphasized) |> Array.map (fun n -> n.Id))
                [| "S1" |]
                "Only the selected node is emphasized."

            Expect.equal
                (views (graph.Nodes |> Array.find (fun n -> n.Id = "A")).Annotations)
                (views [| organism |])
                "The nodes carry their annotations."

            Expect.equal (edgeIds graph.Edges |> Array.sort) [| "m1|E1|F1"; "m2|E1|F2"; "s1|A|S1"; "x1|S1|E1" |] "edges"

        testCase "orders the nodes with the selected node first, then upstream, then downstream"
        <| fun _ ->
            let graph = getSome (lineage (fixture ()) "S1")
            Expect.equal (nodeIds graph.Nodes) [| "S1"; "A"; "E1"; "F1"; "F2" |] "node order"

        testCase "lists the rows of the walk with the upstream rows first and leaves out the row of a sibling"
        <| fun _ ->
            let rowIds (nodeId: string) =
                lineageRows (fixture ()) nodeId |> Array.map (fun r -> r.Id)

            Expect.equal (rowIds "S1") [| "s1"; "x1"; "m1"; "m2" |] "upstream rows, then downstream rows"
            Expect.equal (rowIds "F1") [| "m1"; "x1"; "s1" |] "The row m2 of the sibling F2 is not walked."
            Expect.isEmpty (rowIds "nope") "An unknown node has no rows."

        testCase "keeps only ancestors of F1 and leaves out the branch of its sibling F2"
        <| fun _ ->
            let graph = getSome (lineage (fixture ()) "F1")
            Expect.equal (nodeIds graph.Nodes |> Array.sort) [| "A"; "E1"; "F1"; "S1" |] "nodes"
            Expect.equal (edgeIds graph.Edges |> Array.sort) [| "m1|E1|F1"; "s1|A|S1"; "x1|S1|E1" |] "edges"

        testCase "leaves out a second sample that the same source feeds through another row"
        <| fun _ ->
            let rows = [|
                row "t1" "D:split" "D" [| "src" |] [| "a" |] [||]
                row "t2" "D:split" "D" [| "src" |] [| "b" |] [||]
            |]

            let index =
                buildIndex [| dataset "D" DatasetKind.Study |] [| uniformProcess "D:split" "D" rows |] rows [|
                    node "src" IoKind.Source [||]
                    node "a" IoKind.Sample [||]
                    node "b" IoKind.Sample [||]
                |]

            let graph = getSome (lineage index "a")
            Expect.equal (nodeIds graph.Nodes |> Array.sort) [| "a"; "src" |] "nodes"
            Expect.equal (edgeIds graph.Edges) [| "t1|src|a" |] "edges"

        testCase "computes differing and shared per process within the lineage graph"
        <| fun _ ->
            let edges = (getSome (lineage (fixture ()) "S1")).Edges

            let byId id =
                edges |> Array.find (fun e -> e.Id = id)

            Expect.equal (views (byId "m1|E1|F1").Differing) (views [| split |]) "m1 differing"
            Expect.equal (views (byId "m1|E1|F1").Shared) (views [| protocol "GC-MS" |]) "m1 shared"
            Expect.equal (views (byId "m2|E1|F2").Differing) (views [| splitless |]) "m2 differing"
            Expect.equal (views (byId "m2|E1|F2").Shared) (views [| protocol "GC-MS" |]) "m2 shared"
            Expect.isEmpty (byId "x1|S1|E1").Differing "x1 differing"
            Expect.equal (views (byId "x1|S1|E1").Shared) (views [| protocol "extract" |]) "x1 shared"
            Expect.equal (byId "x1|S1|E1").ProcessName "extraction" "x1 process name"

        testCase "stops on a cycle"
        <| fun _ ->
            let rows = [|
                row "r1" "D:loop" "D" [| "X" |] [| "Y" |] [||]
                row "r2" "D:loop" "D" [| "Y" |] [| "X" |] [||]
            |]

            let index =
                buildIndex [| dataset "D" DatasetKind.Run |] [| uniformProcess "D:loop" "D" rows |] rows [|
                    node "X" IoKind.Data [||]
                    node "Y" IoKind.Data [||]
                |]

            let graph = getSome (lineage index "X")
            Expect.equal (nodeIds graph.Nodes |> Array.sort) [| "X"; "Y" |] "nodes"
            Expect.equal (edgeIds graph.Edges |> Array.sort) [| "r1|X|Y"; "r2|Y|X" |] "edges"
    ]

let private sameNameTests =
    // r1 and r2 read and write x and y. r3 sends x on to c.
    let rows = [|
        row "r1" "D:p" "D" [| "x" |] [| "x" |] [||]
        row "r2" "D:p" "D" [| "y" |] [| "y" |] [||]
        row "r3" "D:p" "D" [| "x" |] [| "c" |] [||]
    |]

    let index () =
        buildIndex
            [| dataset "D" DatasetKind.Run |]
            [|
                {
                    uniformProcess "D:p" "D" rows with
                        InputGroups = [|
                            group "D:p#in" IoSide.Inputs "Inputs A" [| "x"; "y" |] [||]
                        |]
                }
            |]
            rows
            [|
                node "x" IoKind.Data [||]
                node "y" IoKind.Data [||]
                node "c" IoKind.Data [||]
            |]

    testList "a row whose input and output share a name" [
        testCase "produces no loop edge in processDetail"
        <| fun _ ->
            let detail = getSome (processDetail (index ()) "D:p#in")
            Expect.equal (edgeIds detail.Edges) [| "r3|x|c" |] "edges"
            Expect.equal (nodeIds detail.Nodes) [| "x"; "y"; "c" |] "nodes"

        testCase "produces no loop edge in lineage"
        <| fun _ ->
            Expect.equal (edgeIds (getSome (lineage (index ()) "x")).Edges) [| "r3|x|c" |] "x"
            Expect.isEmpty (getSome (lineage (index ()) "y")).Edges "y"
    ]

let private splitTests =
    testList "splitSharedAnnotations" [
        testCase "compares name, value, unit, accession and category"
        <| fun _ ->
            let baseline = ann AnnotationCategory.Parameter "temperature" (Some "20")
            let withUnit = { baseline with Unit = Some "C" }
            let withAccession = { baseline with Accession = Some "X:1" }

            let result =
                splitSharedAnnotations [|
                    [| baseline; protocol "p" |]
                    [| withUnit; protocol "p" |]
                |]

            Expect.equal result.Length 2 "one entry per list"
            Expect.equal (views result.[0].Differing) (views [| baseline |]) "first differing"
            Expect.equal (views result.[0].Shared) (views [| protocol "p" |]) "first shared"
            Expect.equal (result.[1].Differing |> Array.map (fun a -> a.Unit)) [| Some "C" |] "second differing"
            Expect.equal (views result.[1].Shared) (views [| protocol "p" |]) "second shared"

            let accessionResult =
                splitSharedAnnotations [| [| baseline |]; [| withAccession |] |]

            Expect.equal accessionResult.[0].Differing.Length 1 "The accession alone makes annotations differ."

        testCase "shares a single list completely and gives an empty result for no list"
        <| fun _ ->
            let single = splitSharedAnnotations [| [| protocol "p"; split |] |]
            Expect.isEmpty single.[0].Differing "single list: nothing differs"

            Expect.equal
                (views single.[0].Shared)
                (views [| protocol "p"; split |])
                "single list: all shared, same order"

            Expect.isEmpty (splitSharedAnnotations [||]) "no list"
    ]

let private unknownIdTests =
    testList "unknown ids" [
        testCase "return None for unknown ids"
        <| fun _ ->
            let index = fixture ()
            Expect.isNone (processGraph index "nope") "processGraph"
            Expect.isNone (processGroups index "nope") "processGroups"
            Expect.isNone (processDetail index "nope") "processDetail"
            Expect.isNone (lineage index "nope") "lineage"
    ]

// The ARC of ProvenanceGraphBuildIndexTests.wipInvestigation has rows without an output or an input. Every query
// has to answer for every id of it, and unfinished rows show as nodes without a connection.
let private wipTests =
    let index () =
        ProvenanceIndex.ofData (
            ProvenanceGraphData.ofInvestigation (ProvenanceGraphBuildIndexTests.wipInvestigation ())
        )

    let key (table: string) = $"assays/Draft/::{table}"

    testList "work in progress" [
        testCase "answers every query for every id"
        <| fun _ ->
            let index = index ()

            let datasets = datasetGraph index
            Expect.equal datasets.Nodes.Length 1 "one dataset"
            Expect.isEmpty datasets.Edges "A dataset has no edge to itself."

            for dataset in index.Datasets do
                Expect.isSome (processGraph index dataset.Id) $"processGraph {dataset.Id}"

            for p in index.Processes do
                Expect.isSome (processGroups index p.Key) $"processGroups {p.Key}"

                for group in Array.append p.InputGroups p.OutputGroups do
                    Expect.isSome (processDetail index group.Id) $"processDetail {group.Id}"

            for n in index.Nodes do
                Expect.isSome (lineage index n.Id) $"lineage {n.Id}"

        testCase "shows the processes of the draft, also the ones without groups"
        <| fun _ ->
            let graph = (processGraph (index ()) "assays/Draft/").Value

            Expect.equal
                (graph.Nodes |> Array.map (fun n -> n.Label, n.RowCount))
                [|
                    ("outputs_missing", 3)
                    ("inputs_missing", 2)
                    ("only_input", 2)
                    ("unfilled", 3)
                    ("protocol_only", 2)
                |]
                "All five processes are own processes with their rows."

            Expect.isEmpty graph.Edges "No row connects two processes."

        testCase "gives the groups of unfinished rows and no edge for a missing end"
        <| fun _ ->
            let index = index ()
            let groups = (processGroups index (key "outputs_missing")).Value

            Expect.equal
                (groups.Nodes |> Array.map (fun n -> IoSide.toText n.Side, n.Label))
                [| ("inputs", "Inputs A"); ("outputs", "Outputs A") |]
                "groups"

            Expect.equal
                (groups.Edges |> Array.map (fun e -> e.Id))
                [| key "outputs_missing" + "@0" |]
                "The finished row makes the only edge."

            let planned = (processGroups index (key "protocol_only")).Value
            Expect.isEmpty planned.Nodes "A table with a protocol only has no group."
            Expect.isEmpty planned.Edges "and no edge."

        testCase "shows the inputs of unfinished rows in the detail view without an edge"
        <| fun _ ->
            let index = index ()
            let detail = (processDetail index (key "outputs_missing" + "#in0")).Value

            Expect.equal (nodeIds detail.Nodes) [| "a1"; "a2"; "a3"; "b1" |] "The unfinished inputs are nodes."

            Expect.equal
                (detail.Nodes |> Array.map (fun n -> n.Emphasized))
                [| true; true; true; false |]
                "Members are emphasized."

            Expect.equal
                (edgeIds detail.Edges)
                [| key "outputs_missing" + "#0|a1|b1" |]
                "Only the finished row has an edge."

            let onlyInput = (processDetail index (key "only_input" + "#in0")).Value
            Expect.equal (nodeIds onlyInput.Nodes) [| "e1"; "e2" |] "inputs of the table with an input column only"
            Expect.isEmpty onlyInput.Edges "no edge"

            let outputsOnly = (processDetail index (key "inputs_missing" + "#out0")).Value
            Expect.equal (nodeIds outputsOnly.Nodes) [| "c1"; "d2"; "d1" |] "An output without an input stays a node."

            Expect.equal
                (edgeIds outputsOnly.Edges)
                [| key "inputs_missing" + "#1|d1|d2" |]
                "Only the finished row has an edge."

        testCase "gives the lineage of a node of an unfinished row"
        <| fun _ ->
            let index = index ()

            let noOutput = (lineage index "a2").Value
            Expect.equal (nodeIds noOutput.Nodes) [| "a2" |] "The node stands alone."
            Expect.isEmpty noOutput.Edges "no edge"

            let noInput = (lineage index "c1").Value
            Expect.equal (nodeIds noInput.Nodes) [| "c1" |] "The node stands alone."
            Expect.isEmpty noInput.Edges "no edge"

            let finished = (lineage index "b1").Value
            Expect.equal (nodeIds finished.Nodes |> Array.sort) [| "a1"; "b1" |] "A finished row still connects."
    ]

let tests =
    testList "ProvenanceGraph Queries" [
        datasetGraphTests
        processGraphTests
        processGroupsTests
        processDetailTests
        lineageTests
        sameNameTests
        splitTests
        unknownIdTests
        wipTests
    ]
