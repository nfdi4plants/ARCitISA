module internal Swate.Components.Page.ProvenanceGraph.ToCanvasTests

// Ported from toCanvas.test.ts of arc-portal-ui and from the navigation and legend code of
// ProvenanceExplorer.tsx and Legend.tsx. Every case of toCanvas.test.ts is ported.
//
// Dropped PORTAL cases (ProvenanceExplorer.test.tsx): all cases render the React explorer in jsdom
// (clicking nodes, focus handling, the empty messages, the footer, the "Showing 80 of 100 nodes"
// note). The pure parts of them are covered here: the drill-down is `next`, the breadcrumbs are
// `crumbs`, the 80 node limit is `limitLineage`. ProvenanceGraph.test.rtl.fs renders the page and
// covers the rest.
//
// Assertions on fixed copy are left out: the suffixes of the aria labels ("select to show its
// processes" and the like), the tooltip templates of the count pills, the "(empty)" value text and
// the labels of the legend. The assertions keep the text built from data (labels, kinds, counts).

open Vitest
open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Shared.ProvenanceGraph.BuildIndex
open Swate.Components.Shared.ProvenanceGraph.Queries
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Page.ProvenanceGraph.Types
open Swate.Components.Page.ProvenanceGraph.ToCanvas

module Sample = Swate.Components.Page.ProvenanceGraphSample

// Builders

let private ann
    (category: AnnotationCategory)
    (name: string)
    (value: string option)
    (unit: string option)
    : Annotation =
    {
        Name = name
        Value = value
        Unit = unit
        Accession = None
        Category = category
    }

let private detailNode (id: string) (kind: IoKind) (annotations: Annotation[]) (emphasized: bool) : DetailNode = {
    Id = id
    Kind = kind
    Annotations = annotations
    Emphasized = emphasized
}

let private detailEdge
    (id: string)
    (source: string)
    (target: string)
    (processKey: string)
    (differing: Annotation[])
    (shared: Annotation[])
    : DetailEdge =
    {
        Id = id
        From = source
        To = target
        ProcessKey = processKey
        ProcessName = processKey
        DatasetKind = DatasetKind.Assay
        Differing = differing
        Shared = shared
    }

let private datasetNode (id: string) (kind: DatasetKind) (label: string) : DatasetGraphNode = {
    Id = id
    Kind = kind
    Label = label
}

let private sharedEdge (id: string) (source: string) (target: string) (nodeIds: string[]) : SharedNodeEdge = {
    Id = id
    From = source
    To = target
    SharedNodeIds = nodeIds
}

let private stepProcess: ProvenanceProcess = {
    Key = "assays/a/::step"
    Name = "step"
    DatasetId = "assays/a/"
    RowIds = [||]
    Signatures = [||]
    InputGroups = [||]
    OutputGroups = [||]
    GroupEdges = [||]
}

let private inputGroup: IoGroup = {
    Id = "g1"
    Side = IoSide.Inputs
    Label = "Inputs A"
    NodeIds = [||]
    SignatureIds = [||]
}

let private param (name: string) (value: string) =
    ann AnnotationCategory.Parameter name (Some value) None

let private protocol (value: string) =
    ann AnnotationCategory.Protocol "Protocol" (Some value) None

let private species =
    ann AnnotationCategory.Characteristic "species" (Some "Talinum") None

let private pillSummary (pill: CanvasPill) =
    pill.Name, pill.Value, pill.Unit, pill.Category

let private ids (nodes: CanvasNode[]) = nodes |> Array.map (fun n -> n.Id)

// datasetsToCanvas

Vitest.describe (
    "datasetsToCanvas",
    fun () ->
        let graph (count: int) : DatasetGraph = {
            Nodes = [|
                datasetNode "studies/s/" DatasetKind.Study "s"
                datasetNode "assays/a/" DatasetKind.Assay "a"
            |]
            Edges = [|
                sharedEdge "e1" "studies/s/" "assays/a/" (Array.init count (fun i -> $"x{i}"))
            |]
        }

        Vitest.test (
            "shows the identifier, colors by kind and counts the shared nodes on the edge",
            fun () ->
                let canvas = datasetsToCanvas (graph 3)
                let node = canvas.Nodes.[0]

                Vitest.expect(node.Id).toBe "studies/s/"
                Vitest.expect(node.Title).toBe "s"
                Vitest.expect(node.Color).toBe CanvasColor.Study
                Vitest.expect(node.Tooltip).toBe (Some "s (study)")
                Vitest.expect(node.Subtitle).toBe None
                Vitest.expect(node.AriaLabel.Contains "study").toBe true
                Vitest.expect(node.AriaLabel.Contains "s,").toBe true
                Vitest.expect(canvas.Nodes.[1].Color).toBe CanvasColor.Assay

                let edge = canvas.Edges.[0]
                Vitest.expect(edge.Id).toBe "e1"
                Vitest.expect(edge.From).toBe "studies/s/"
                Vitest.expect(edge.To).toBe "assays/a/"
                Vitest.expect(edge.Color).toBe CanvasColor.Neutral
                Vitest.expect(edge.MorePills.Length).toBe 0
                Vitest.expect(edge.Pills.Length).toBe 1

                let pill = edge.Pills.[0]
                Vitest.expect(pill.Key).toBe "count"
                Vitest.expect(pill.Kind).toBe PillKind.Count
                Vitest.expect(pill.Value).toBe (Some "3")
                Vitest.expect(pill.Tooltip.Value.Contains "3").toBe true
        )

        Vitest.test (
            "words the tooltip of the count pill by the count",
            fun () ->
                let tooltipOf count =
                    (datasetsToCanvas (graph count)).Edges.[0].Pills.[0].Tooltip

                Vitest.expect(tooltipOf 1 = tooltipOf 3).toBe false
                Vitest.expect(tooltipOf 3 = tooltipOf 4).toBe false
        )
)

// processesToCanvas

Vitest.describe (
    "processesToCanvas",
    fun () ->
        Vitest.test (
            "writes the row count on own processes and the dataset on external ones",
            fun () ->
                let node key label datasetId kind datasetLabel external rowCount : ProcessGraphNode = {
                    Key = key
                    Label = label
                    DatasetId = datasetId
                    DatasetKind = kind
                    DatasetLabel = datasetLabel
                    External = external
                    RowCount = rowCount
                }

                let graph: ProcessGraph = {
                    Dataset = {
                        Id = "assays/a/"
                        Kind = DatasetKind.Assay
                        Identifier = "a"
                    }
                    Nodes = [|
                        node "k1" "step" "assays/a/" DatasetKind.Assay "a" false 1
                        node "k2" "other" "assays/a/" DatasetKind.Assay "a" false 6
                        node "k3" "far" "runs/r/" DatasetKind.Run "r" true 2
                    |]
                    Edges = [| sharedEdge "e1" "k1" "k3" [| "x" |] |]
                }

                let canvas = processesToCanvas graph

                Vitest.expect(canvas.Nodes |> Array.map (fun n -> n.Subtitle)).toEqual [|
                    Some "1 row"
                    Some "6 rows"
                    Some "r"
                |]

                Vitest.expect(canvas.Nodes |> Array.map (fun n -> n.Dashed)).toEqual [| false; false; true |]
                Vitest.expect(canvas.Nodes.[2].Title).toBe "far"
                Vitest.expect(canvas.Nodes.[2].Color).toBe CanvasColor.Run
                Vitest.expect(canvas.Nodes.[2].Tooltip).toBe (Some "far (r)")
                Vitest.expect(canvas.Nodes.[2].AriaLabel.Contains "far").toBe true
                Vitest.expect(canvas.Nodes.[2].AriaLabel.Contains "r").toBe true
                Vitest.expect(canvas.Edges.[0].Color).toBe CanvasColor.Neutral
                Vitest.expect(canvas.Edges.[0].Pills.[0].Kind).toBe PillKind.Count
                Vitest.expect(canvas.Edges.[0].Pills.[0].Value).toBe (Some "1")
        )
)

// groupsToCanvas

Vitest.describe (
    "groupsToCanvas",
    fun () ->
        let groupNode id side label counts : GroupGraphNode = {
            Id = id
            Side = side
            Label = label
            KindCounts = counts
        }

        let graph: GroupGraph = {
            Process = stepProcess
            DatasetKind = DatasetKind.Assay
            Nodes = [|
                groupNode "g1" IoSide.Inputs "Inputs A" [| (IoKind.Sample, 3); (IoKind.Data, 2) |]
                groupNode "g2" IoSide.Outputs "Outputs A" [| (IoKind.Sample, 1) |]
                groupNode "g3" IoSide.Outputs "Outputs B" [|
                    (IoKind.Source, 2)
                    (IoKind.Material, 1)
                    (IoKind.Data, 1)
                |]
            |]
            Edges = [|
                {
                    Id = "e1"
                    From = "g1"
                    To = "g2"
                    Differing = [|
                        ann AnnotationCategory.Parameter "temp" (Some "40") (Some "C")
                    |]
                    Shared = [| protocol "p1" |]
                }
            |]
        }

        Vitest.test (
            "lists the kinds in IoKind order and pluralizes all but data",
            fun () ->
                let canvas = groupsToCanvas graph

                Vitest.expect(canvas.Nodes |> Array.map (fun n -> n.Title)).toEqual [|
                    "Inputs A"
                    "Outputs A"
                    "Outputs B"
                |]

                Vitest.expect(canvas.Nodes |> Array.map (fun n -> n.Subtitle)).toEqual [|
                    Some "3 samples · 2 data"
                    Some "1 sample"
                    Some "2 sources · 1 material · 1 data"
                |]

                Vitest.expect(canvas.Nodes.[0].Color).toBe CanvasColor.Assay
                Vitest.expect(canvas.Nodes.[0].Tooltip).toBe (Some "Inputs A (3 samples · 2 data)")
        )

        Vitest.test (
            "gives a group without members no subtitle",
            fun () ->
                let empty: GroupGraph = {
                    graph with
                        Nodes = [| groupNode "g1" IoSide.Inputs "Inputs A" [||] |]
                }

                let node = (groupsToCanvas empty).Nodes.[0]
                Vitest.expect(node.Subtitle).toBe None
                Vitest.expect(node.Tooltip).toBe (Some "Inputs A")
                Vitest.expect(node.AriaLabel.Contains "Inputs A").toBe true
        )

        Vitest.test (
            "puts the differing annotations on the edge and the shared ones behind the more pill",
            fun () ->
                let edge = (groupsToCanvas graph).Edges.[0]

                Vitest.expect(edge.Color).toBe CanvasColor.Assay
                Vitest.expect(edge.Pills.Length).toBe 1
                Vitest.expect(edge.Pills.[0].Kind).toBe PillKind.Property

                Vitest
                    .expect(pillSummary edge.Pills.[0])
                    .toEqual (Some "temp", Some "40", Some "C", Some PillCategory.Parameter)

                Vitest.expect(edge.MorePills.Length).toBe 1
                Vitest.expect(edge.MorePills.[0].Kind).toBe PillKind.Property

                Vitest
                    .expect(pillSummary edge.MorePills.[0])
                    .toEqual (Some "Protocol", Some "p1", None, Some PillCategory.Protocol)
        )

        Vitest.test (
            "numbers the pill keys by position, so equal annotations keep distinct keys",
            fun () ->
                let pills = propertyPills [| param "t" "1"; param "t" "1" |] "d"
                Vitest.expect(pills |> Array.map (fun p -> p.Key)).toEqual [| "d0:parameter:t"; "d1:parameter:t" |]
        )
)

// Category mapping

Vitest.describe (
    "pillCategoryOf",
    fun () ->
        Vitest.test (
            "maps every annotation category to the pill category with the same name",
            fun () ->
                let pairs = [|
                    AnnotationCategory.Characteristic, PillCategory.Characteristic
                    AnnotationCategory.Factor, PillCategory.Factor
                    AnnotationCategory.Parameter, PillCategory.Parameter
                    AnnotationCategory.Component, PillCategory.Component
                    AnnotationCategory.Protocol, PillCategory.Protocol
                    AnnotationCategory.Performer, PillCategory.Performer
                    AnnotationCategory.Date, PillCategory.Date
                    AnnotationCategory.Comment, PillCategory.Comment
                |]

                for (category, expected) in pairs do
                    Vitest.expect(pillCategoryOf category).toBe expected

                // The StringEnum text of the pill category is the toText of the annotation category.
                for (category, expected) in pairs do
                    Vitest.expect(AnnotationCategory.toText category).toBe (unbox<string> (box expected))
        )
)

// detailToCanvas

Vitest.describe (
    "detailToCanvas",
    fun () ->
        let detail (nodes: DetailNode[]) (edges: DetailEdge[]) : ProcessDetailGraph = {
            Process = stepProcess
            Group = inputGroup
            Nodes = nodes
            Edges = edges
        }

        Vitest.test (
            "shows the kind only in a graph with several kinds",
            fun () ->
                let mixed =
                    detailToCanvas (
                        detail [|
                            detailNode "a" IoKind.Sample [||] false
                            detailNode "b" IoKind.Data [||] false
                        |] [||]
                    )

                let single =
                    detailToCanvas (
                        detail [|
                            detailNode "a" IoKind.Sample [||] false
                            detailNode "b" IoKind.Sample [||] false
                        |] [||]
                    )

                Vitest.expect(mixed.Nodes |> Array.map (fun n -> n.Subtitle)).toEqual [| Some "sample"; Some "data" |]
                Vitest.expect(single.Nodes |> Array.map (fun n -> n.Subtitle)).toEqual [| None; None |]
        )

        Vitest.test (
            "moves annotations that every node carries into the tooltips",
            fun () ->
                let tissue (value: string) =
                    ann AnnotationCategory.Characteristic "tissue" (Some value) None

                let canvas =
                    detailToCanvas (
                        detail [|
                            detailNode "a" IoKind.Sample [| species; tissue "leaf" |] false
                            detailNode "b" IoKind.Sample [| species; tissue "root" |] false
                        |] [||]
                    )

                let a, b = canvas.Nodes.[0], canvas.Nodes.[1]

                Vitest.expect(a.Pills |> Array.map pillSummary).toEqual [|
                    (Some "tissue", Some "leaf", None, Some PillCategory.Characteristic)
                |]

                Vitest.expect(b.Pills |> Array.map pillSummary).toEqual [|
                    (Some "tissue", Some "root", None, Some PillCategory.Characteristic)
                |]

                Vitest.expect(a.Pills.[0].Kind).toBe PillKind.Property
                Vitest.expect(a.Tooltip).toBe (Some "a (sample): Characteristic · species: Talinum")
                Vitest.expect(b.Tooltip).toBe (Some "b (sample): Characteristic · species: Talinum")
                Vitest.expect(a.AriaLabel.Contains "a, sample, Characteristic · species: Talinum").toBe true
        )

        Vitest.test (
            "compares four fields, so a different accession alone does not keep an annotation on the pills",
            fun () ->
                let withAccession = {
                    species with
                        Accession = Some "NCIT:C1"
                }

                let nodes = [|
                    detailNode "a" IoKind.Sample [| species |] false
                    detailNode "b" IoKind.Sample [| withAccession |] false
                |]

                Vitest.expect((commonAnnotationKeys nodes).Count).toBe 1
                Vitest.expect(detailToCanvas(detail nodes [||]).Nodes.[0].Pills.Length).toBe 0
        )

        Vitest.test (
            "tells a missing value from an empty value and from the text null",
            fun () ->
                let withValue (value: string option) =
                    ann AnnotationCategory.Factor "dose" value None

                let common (a: string option) (b: string option) =
                    (commonAnnotationKeys [|
                        detailNode "a" IoKind.Sample [| withValue a |] false
                        detailNode "b" IoKind.Sample [| withValue b |] false
                    |])
                        .Count

                Vitest.expect(common None None).toBe 1
                Vitest.expect(common None (Some "")).toBe 0
                Vitest.expect(common None (Some "n")).toBe 0
                Vitest.expect(common (Some "n") (Some "s:n")).toBe 0
                Vitest.expect(common (Some "x") (Some "x")).toBe 1
        )

        Vitest.test (
            "keeps the annotations of a single node as pills",
            fun () ->
                let canvas =
                    detailToCanvas (detail [| detailNode "a" IoKind.Sample [| species |] false |] [||])

                Vitest.expect(canvas.Nodes.[0].Pills |> Array.map pillSummary).toEqual [|
                    (Some "species", Some "Talinum", None, Some PillCategory.Characteristic)
                |]

                Vitest.expect(canvas.Nodes.[0].Tooltip).toBe (Some "a (sample)")
        )

        Vitest.test (
            "marks the emphasized nodes, sets monospace titles and adds no process pill",
            fun () ->
                let canvas =
                    detailToCanvas (
                        detail [|
                            detailNode "a" IoKind.Sample [||] true
                            detailNode "b" IoKind.Sample [||] false
                        |] [|
                            detailEdge "e1" "a" "b" "p1" [| param "temp" "40" |] [| protocol "x" |]
                        |]
                    )

                Vitest.expect(canvas.Nodes |> Array.map (fun n -> n.Mono, n.Emphasized)).toEqual [|
                    (true, true)
                    (true, false)
                |]

                Vitest.expect(canvas.Edges.[0].Color).toBe CanvasColor.Assay
                Vitest.expect(canvas.Edges.[0].Pills.Length).toBe 1
                Vitest.expect(canvas.Edges.[0].Pills.[0].Kind).toBe PillKind.Property
                Vitest.expect(canvas.Edges.[0].MorePills.Length).toBe 1
        )

        Vitest.test (
            "gives the nodes the neutral color when the graph has no edge and names the group in the label",
            fun () ->
                let canvas =
                    detailToCanvas (detail [| detailNode "a" IoKind.Sample [||] true |] [||])

                Vitest.expect(canvas.Nodes.[0].Color).toBe CanvasColor.Neutral
                Vitest.expect(canvas.Nodes.[0].AriaLabel.Contains inputGroup.Label).toBe true
        )
)

// lineageToCanvas

Vitest.describe (
    "lineageToCanvas",
    fun () ->
        let lineage (edges: DetailEdge[]) : LineageGraph = {
            NodeId = "b"
            Nodes = [|
                detailNode "a" IoKind.Sample [||] false
                detailNode "b" IoKind.Sample [||] true
                detailNode "c" IoKind.Sample [||] false
            |]
            Edges = edges
        }

        Vitest.test (
            "puts a process pill first on every edge only when several processes appear",
            fun () ->
                let differing = [| param "temp" "40" |]

                let several =
                    lineageToCanvas
                        (lineage [|
                            detailEdge "e1" "a" "b" "p1" differing [||]
                            detailEdge "e2" "b" "c" "p2" [||] [||]
                        |])
                        None

                let one =
                    lineageToCanvas
                        (lineage [|
                            detailEdge "e1" "a" "b" "p1" differing [||]
                            detailEdge "e2" "b" "c" "p1" [||] [||]
                        |])
                        None

                Vitest.expect(several.Edges.[0].Pills |> Array.map (fun p -> p.Kind)).toEqual [|
                    PillKind.Process
                    PillKind.Property
                |]

                Vitest.expect(several.Edges.[0].Pills.[0].Value).toBe (Some "p1")
                Vitest.expect(several.Edges.[0].Pills.[0].Tooltip.Value.Contains "p1").toBe true
                Vitest.expect(several.Edges.[1].Pills |> Array.map (fun p -> p.Kind)).toEqual [| PillKind.Process |]

                Vitest.expect(one.Edges |> Array.collect (fun e -> e.Pills |> Array.map (fun p -> p.Kind))).toEqual [|
                    PillKind.Property
                |]
        )

        Vitest.test (
            "highlights the edges of the selected process and marks the selected node",
            fun () ->
                let graph =
                    lineage [|
                        detailEdge "e1" "a" "b" "p1" [||] [||]
                        detailEdge "e2" "b" "c" "p2" [||] [||]
                    |]

                let canvas = lineageToCanvas graph (Some "p2")

                Vitest.expect(canvas.Edges |> Array.map (fun e -> e.Highlighted)).toEqual [| false; true |]
                Vitest.expect(canvas.Nodes |> Array.map (fun n -> n.Selected)).toEqual [| false; true; false |]
                Vitest.expect(canvas.CenterNodeId).toBe "b"

                let none = lineageToCanvas graph None
                Vitest.expect(none.Edges |> Array.map (fun e -> e.Highlighted)).toEqual [| false; false |]
        )
)

// limitLineage

Vitest.describe (
    "limitLineage",
    fun () ->
        // A chain n0 -> n1 -> ... -> n(length - 1) with the selected node somewhere in it.
        let chain (length: int) (nodeId: string) : LineageGraph = {
            NodeId = nodeId
            Nodes = Array.init length (fun i -> detailNode $"n{i}" IoKind.Sample [||] ($"n{i}" = nodeId))
            Edges = Array.init (length - 1) (fun i -> detailEdge $"e{i}" $"n{i}" $"n{i + 1}" "p1" [||] [||])
        }

        let nodeIds (graph: LineageGraph) =
            graph.Nodes |> Array.map (fun n -> n.Id)

        Vitest.test (
            "keeps a graph within the limit unchanged",
            fun () ->
                let graph = chain 80 "n0"
                let result = limitLineage graph

                Vitest.expect(obj.ReferenceEquals(result.Graph, graph)).toBe true
                Vitest.expect(result.Total).toBe 80
        )

        Vitest.test (
            "keeps the 80 nodes nearest to the selected node and the edges between them",
            fun () ->
                let result = limitLineage (chain 120 "n60")

                Vitest.expect(result.Total).toBe 120
                Vitest.expect(result.Graph.Nodes.Length).toBe 80

                // n20 and n100 are equally far from n60, and the earlier one in the graph order stays.
                Vitest
                    .expect(nodeIds result.Graph |> Array.sort)
                    .toEqual (Array.init 80 (fun i -> $"n{i + 20}") |> Array.sort)

                Vitest.expect(result.Graph.Edges.Length).toBe 79

                let kept = nodeIds result.Graph

                Vitest
                    .expect(
                        result.Graph.Edges
                        |> Array.forall (fun e -> Array.contains e.From kept && Array.contains e.To kept)
                    )
                    .toBe
                    true
        )

        Vitest.test (
            "keeps the nodes in the order of the graph",
            fun () ->
                let result = limitLineageTo 5 (chain 9 "n4")
                Vitest.expect(nodeIds result.Graph).toEqual [| "n2"; "n3"; "n4"; "n5"; "n6" |]
        )

        Vitest.test (
            "lets nodes at equal distance keep the graph order when the limit cuts through them",
            fun () ->
                // The selected node c has four neighbors l0 to l3 and every one is one step away.
                let nodes = [|
                    detailNode "l2" IoKind.Sample [||] false
                    detailNode "l0" IoKind.Sample [||] false
                    detailNode "c" IoKind.Sample [||] true
                    detailNode "l3" IoKind.Sample [||] false
                    detailNode "l1" IoKind.Sample [||] false
                |]

                let graph: LineageGraph = {
                    NodeId = "c"
                    Nodes = nodes
                    // Edge directions differ, because the distance ignores them.
                    Edges = [|
                        detailEdge "e0" "l0" "c" "p1" [||] [||]
                        detailEdge "e1" "c" "l1" "p1" [||] [||]
                        detailEdge "e2" "c" "l2" "p1" [||] [||]
                        detailEdge "e3" "l3" "c" "p1" [||] [||]
                    |]
                }

                let result = limitLineageTo 3 graph
                Vitest.expect(result.Total).toBe 5
                Vitest.expect(nodeIds result.Graph).toEqual [| "l2"; "l0"; "c" |]
                Vitest.expect(result.Graph.Edges |> Array.map (fun e -> e.Id)).toEqual [| "e0"; "e2" |]
        )

        Vitest.test (
            "drops nodes that no edge reaches before it drops reachable ones",
            fun () ->
                let graph: LineageGraph = {
                    NodeId = "c"
                    Nodes = [|
                        detailNode "island" IoKind.Sample [||] false
                        detailNode "c" IoKind.Sample [||] true
                        detailNode "far" IoKind.Sample [||] false
                        detailNode "near" IoKind.Sample [||] false
                    |]
                    Edges = [|
                        detailEdge "e1" "c" "near" "p1" [||] [||]
                        detailEdge "e2" "near" "far" "p1" [||] [||]
                    |]
                }

                Vitest.expect(nodeIds (limitLineageTo 3 graph).Graph).toEqual [| "c"; "far"; "near" |]
                Vitest.expect(nodeIds (limitLineageTo 2 graph).Graph).toEqual [| "c"; "near" |]
        )
)

// Navigation over the example ARC

Vitest.describe (
    "ProvenanceView.next",
    fun () ->
        let index = Sample.index

        let extraction = "assays/Extraction/"
        let extractionProcess = "assays/Extraction/::Extraction"
        let inputGroupId = extractionProcess + "#in0"

        Vitest.test (
            "opens the processes of a clicked dataset",
            fun () ->
                Vitest
                    .expect(ProvenanceView.next index ProvenanceView.Datasets extraction)
                    .toEqual (ProvenanceView.Processes extraction)
        )

        Vitest.test (
            "opens the groups of a clicked own process and keeps the dataset",
            fun () ->
                Vitest
                    .expect(ProvenanceView.next index (ProvenanceView.Processes extraction) extractionProcess)
                    .toEqual (ProvenanceView.Groups(extraction, extractionProcess))
        )

        Vitest.test (
            "switches to the dataset of a clicked external process",
            fun () ->
                let measurement = "assays/Measurement/"

                // The extracts connect the two assays, so the extraction shows up as an external process.
                let shown = (processGraph index measurement).Value.Nodes
                let external = shown |> Array.find (fun n -> n.Key = extractionProcess)
                Vitest.expect(external.External).toBe true

                Vitest
                    .expect(ProvenanceView.next index (ProvenanceView.Processes measurement) extractionProcess)
                    .toEqual (ProvenanceView.Groups(extraction, extractionProcess))
        )

        Vitest.test (
            "opens the detail of a clicked group, then the lineage of a clicked node, then re-centers it",
            fun () ->
                let groups = ProvenanceView.Groups(extraction, extractionProcess)
                let detail = ProvenanceView.next index groups inputGroupId

                Vitest.expect(detail).toEqual (ProvenanceView.Detail(extraction, extractionProcess, inputGroupId))

                let lineageView = ProvenanceView.next index detail "leaf-1"

                Vitest
                    .expect(lineageView)
                    .toEqual (ProvenanceView.Lineage(extraction, extractionProcess, inputGroupId, "leaf-1"))

                Vitest
                    .expect(ProvenanceView.next index lineageView "extract-1")
                    .toEqual (ProvenanceView.Lineage(extraction, extractionProcess, inputGroupId, "extract-1"))
        )

        Vitest.test (
            "does not throw for ids the index does not know and leads to views without a result",
            fun () ->
                let unknownProcess =
                    ProvenanceView.next index (ProvenanceView.Processes extraction) "nope"

                Vitest.expect(unknownProcess).toEqual (ProvenanceView.Groups(extraction, "nope"))
                Vitest.expect((processGroups index "nope").IsNone).toBe true

                let unknownDataset = ProvenanceView.next index ProvenanceView.Datasets "nope"
                Vitest.expect(unknownDataset).toEqual (ProvenanceView.Processes "nope")
                Vitest.expect((processGraph index "nope").IsNone).toBe true

                let unknownGroup =
                    ProvenanceView.next index (ProvenanceView.Groups("nope", "nope")) "nope"

                Vitest.expect(unknownGroup).toEqual (ProvenanceView.Detail("nope", "nope", "nope"))
                Vitest.expect((processDetail index "nope").IsNone).toBe true

                let unknownNode = ProvenanceView.next index unknownGroup "nope"
                Vitest.expect(unknownNode).toEqual (ProvenanceView.Lineage("nope", "nope", "nope", "nope"))
                Vitest.expect((lineage index "nope").IsNone).toBe true
        )

        Vitest.test (
            "works on an index without datasets",
            fun () ->
                Vitest
                    .expect(ProvenanceView.next Sample.emptyIndex (ProvenanceView.Processes "x") "y")
                    .toEqual (ProvenanceView.Groups("x", "y"))
        )
)

Vitest.describe (
    "ProvenanceView.crumbs",
    fun () ->
        let index = Sample.index
        let extraction = "assays/Extraction/"
        let extractionProcess = "assays/Extraction/::Extraction"
        let inputGroupId = extractionProcess + "#in0"

        let lineageView =
            ProvenanceView.Lineage(extraction, extractionProcess, inputGroupId, "leaf-1")

        let kindsOf (crumbs: Crumb[]) =
            crumbs |> Array.map (fun c -> ProvenanceView.kindText c.Target)

        Vitest.test (
            "starts with the datasets root",
            fun () ->
                let crumbs = ProvenanceView.crumbs index ProvenanceView.Datasets
                Vitest.expect(crumbs.Length).toBe 1
                Vitest.expect(crumbs.[0].Target).toEqual ProvenanceView.Datasets
        )

        Vitest.test (
            "takes the labels from the data and one crumb per level",
            fun () ->
                let crumbs = ProvenanceView.crumbs index lineageView

                Vitest.expect(kindsOf crumbs).toEqual [| "datasets"; "processes"; "groups"; "detail"; "lineage" |]

                Vitest.expect(crumbs |> Array.skip 1 |> Array.map (fun c -> c.Label)).toEqual [|
                    "Extraction"
                    "Extraction"
                    "Inputs A"
                    "leaf-1"
                |]

                Vitest.expect(crumbs.[1].Label).toBe (index.DatasetById.[extraction].Identifier)
                Vitest.expect(crumbs.[2].Label).toBe (index.ProcessByKey.[extractionProcess].Name)
                Vitest.expect(crumbs.[3].Label).toBe (index.GroupById.[inputGroupId].Label)
        )

        Vitest.test (
            "returns to a level and drops the deeper selections",
            fun () ->
                let crumbs = ProvenanceView.crumbs index lineageView

                Vitest.expect(crumbs.[1].Target).toEqual (ProvenanceView.Processes extraction)

                Vitest.expect(crumbs.[2].Target).toEqual (ProvenanceView.Groups(extraction, extractionProcess))

                Vitest
                    .expect(crumbs.[3].Target)
                    .toEqual (ProvenanceView.Detail(extraction, extractionProcess, inputGroupId))

                Vitest.expect(crumbs.[4].Target).toEqual lineageView

                // The crumbs of the earlier level are the start of the crumbs of the deeper one.
                let groupsCrumbs = ProvenanceView.crumbs index (crumbs.[2].Target)

                Vitest
                    .expect(groupsCrumbs |> Array.map (fun c -> c.Label))
                    .toEqual (crumbs |> Array.take 3 |> Array.map (fun c -> c.Label))
        )

        Vitest.test (
            "ends with a crumb that targets the view itself on every level",
            fun () ->
                let views = [|
                    ProvenanceView.Datasets
                    ProvenanceView.Processes extraction
                    ProvenanceView.Groups(extraction, extractionProcess)
                    ProvenanceView.Detail(extraction, extractionProcess, inputGroupId)
                    lineageView
                |]

                for view in views do
                    let crumbs = ProvenanceView.crumbs index view
                    Vitest.expect((Array.last crumbs).Target).toEqual view
        )

        Vitest.test (
            "shows the dataset of an external process after the switch",
            fun () ->
                let afterClick =
                    ProvenanceView.next index (ProvenanceView.Processes "assays/Measurement/") extractionProcess

                let crumbs = ProvenanceView.crumbs index afterClick
                Vitest.expect(crumbs.[1].Label).toBe "Extraction"
                Vitest.expect(crumbs.[1].Target).toEqual (ProvenanceView.Processes extraction)
        )

        Vitest.test (
            "falls back to the ids for unknown ids and does not throw",
            fun () ->
                let crumbs =
                    ProvenanceView.crumbs index (ProvenanceView.Lineage("d?", "p?", "g?", "n?"))

                Vitest.expect(crumbs |> Array.skip 1 |> Array.map (fun c -> c.Label)).toEqual [|
                    "d?"
                    "p?"
                    "g?"
                    "n?"
                |]

                Vitest.expect((ProvenanceView.crumbs Sample.emptyIndex (ProvenanceView.Processes "x")).[1].Label).toBe
                    "x"
        )
)

Vitest.describe (
    "ProvenanceView.imageFileName",
    fun () ->
        let nameOf (view: ProvenanceView) =
            ProvenanceView.imageFileName (ProvenanceView.crumbs Sample.index view)

        let withLabels (labels: string list) : Crumb[] = [|
            for label in "Datasets" :: labels ->
                {
                    Label = label
                    Target = ProvenanceView.Datasets
                }
        |]

        Vitest.test (
            "names the datasets level after the datasets",
            fun () -> Vitest.expect(nameOf ProvenanceView.Datasets).toBe "provenance-datasets.png"
        )

        Vitest.test (
            "joins the labels after the root and replaces spaces, slashes and colons",
            fun () ->
                let name = nameOf (ProvenanceView.Groups("assays/A/", "assays/A/::p 1"))

                Vitest.expect(name).toBe "provenance-assays_A_-assays_A_p_1.png"
                Vitest.expect(System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9._-]+$")).toBe true
        )

        Vitest.test (
            "cuts the labels to 100 characters",
            fun () ->
                let name = ProvenanceView.imageFileName (withLabels [ String.replicate 150 "a" ])

                Vitest.expect(name).toBe ("provenance-" + String.replicate 100 "a" + ".png")
        )

        Vitest.test (
            "trims separators at the ends and falls back to the datasets when nothing is left",
            fun () ->
                Vitest.expect(ProvenanceView.imageFileName (withLabels [ "-x-"; "?" ])).toBe "provenance-x.png"
                Vitest.expect(ProvenanceView.imageFileName (withLabels [ "///"; ":" ])).toBe "provenance-datasets.png"
        )
)

Vitest.describe (
    "ProvenanceView.resetKey",
    fun () ->
        Vitest.test (
            "lists the level and its ids as a JSON array",
            fun () ->
                Vitest.expect(ProvenanceView.resetKey ProvenanceView.Datasets).toBe """["datasets"]"""
                Vitest.expect(ProvenanceView.resetKey (ProvenanceView.Processes "d")).toBe """["processes","d"]"""

                Vitest.expect(ProvenanceView.resetKey (ProvenanceView.Lineage("d", "p", "g", "n"))).toBe
                    """["lineage","d","p","g","n"]"""
        )

        Vitest.test (
            "gives different views different keys, also when ids contain separators or quotes",
            fun () ->
                let views = [|
                    ProvenanceView.Datasets
                    ProvenanceView.Processes "a"
                    ProvenanceView.Processes "a\",\"b"
                    ProvenanceView.Groups("a", "b")
                    ProvenanceView.Groups("a,b", "c")
                    ProvenanceView.Groups("a", "b,c")
                    ProvenanceView.Groups("a\\", "b")
                    ProvenanceView.Detail("a", "b", "c")
                    ProvenanceView.Detail("a", "b", "c,")
                    ProvenanceView.Lineage("a", "b", "c", "d")
                    ProvenanceView.Lineage("a", "b", "c", "")
                |]

                let keys = views |> Array.map ProvenanceView.resetKey
                Vitest.expect(keys |> Array.distinct |> Array.length).toBe views.Length
        )
)

// Legend

Vitest.describe (
    "legendItems",
    fun () ->
        let swatches (view: ProvenanceView) =
            legendItems view |> Array.map (fun item -> item.Swatch)

        let kindSwatches = [|
            LegendSwatch.Color CanvasColor.Study
            LegendSwatch.Color CanvasColor.Assay
            LegendSwatch.Color CanvasColor.Run
        |]

        Vitest.test (
            "shows the dataset kinds in the datasets view",
            fun () -> Vitest.expect(swatches ProvenanceView.Datasets = kindSwatches).toBe true
        )

        Vitest.test (
            "adds the dashed outline of other datasets in the processes view",
            fun () ->
                Vitest
                    .expect(
                        swatches (ProvenanceView.Processes "d") = Array.append kindSwatches [| LegendSwatch.Dashed |]
                    )
                    .toBe
                    true
        )

        Vitest.test (
            "shows the dataset kinds and the annotation categories in the deeper views",
            fun () ->
                let expected =
                    Array.append kindSwatches [|
                        LegendSwatch.Category PillCategory.Characteristic
                        LegendSwatch.Category PillCategory.Factor
                        LegendSwatch.Category PillCategory.Parameter
                        LegendSwatch.Category PillCategory.Component
                        LegendSwatch.Plain
                    |]

                for view in
                    [|
                        ProvenanceView.Groups("d", "p")
                        ProvenanceView.Detail("d", "p", "g")
                        ProvenanceView.Lineage("d", "p", "g", "n")
                    |] do
                    Vitest.expect(swatches view = expected).toBe true

                Vitest
                    .expect(
                        legendItems (ProvenanceView.Groups("d", "p"))
                        |> Array.forall (fun i -> i.Label <> "")
                    )
                    .toBe
                    true
        )
)

// The example ARC

Vitest.describe (
    "the example ARC",
    fun () ->
        Vitest.test (
            "has the shape the stories rely on",
            fun () ->
                let data = Sample.data

                let extractionProcess =
                    data.Processes |> Array.find (fun p -> p.Name = "Extraction")

                Vitest.expect(data.Datasets |> Array.map (fun d -> d.Id)).toEqual [|
                    "studies/Plants/"
                    "assays/Extraction/"
                    "assays/Measurement/"
                    "assays/Empty/"
                    "runs/Analysis/"
                |]

                Vitest.expect(data.Rows.Length).toBeGreaterThanOrEqual 12
                Vitest.expect(data.Rows.Length).toBeLessThanOrEqual 20
                Vitest.expect(extractionProcess.Signatures.Length).toBe 2
                Vitest.expect(extractionProcess.InputGroups.Length).toBe 2
                Vitest.expect(extractionProcess.OutputGroups.Length).toBe 2
                Vitest.expect(data.Processes |> Array.exists (fun p -> p.DatasetId = "assays/Empty/")).toBe false
                Vitest.expect(data.Nodes |> Array.exists (fun n -> n.Id = "data/m1.raw")).toBe true
                Vitest.expect(Sample.emptyIndex.Datasets.Length).toBe 0
        )

        Vitest.test (
            "gives every level a canvas with nodes except the empty assay",
            fun () ->
                let index = Sample.index
                Vitest.expect((datasetsToCanvas (datasetGraph index)).Nodes.Length).toBe 5

                let processesOf id =
                    (processesToCanvas (processGraph index id).Value).Nodes.Length

                Vitest.expect(processesOf "assays/Extraction/").toBeGreaterThan 1
                Vitest.expect(processesOf "assays/Empty/").toBe 0

                let groups = (processGroups index "assays/Extraction/::Extraction").Value
                Vitest.expect((groupsToCanvas groups).Nodes.Length).toBe 4

                let detail = (processDetail index "assays/Extraction/::Extraction#in0").Value

                Vitest.expect((detailToCanvas detail).Nodes |> ids |> Array.sort).toEqual [|
                    "extract-1"
                    "extract-2"
                    "leaf-1"
                    "leaf-2"
                |]

                let limited = limitLineage (lineage index "leaf-1").Value
                Vitest.expect(limited.Total).toBe limited.Graph.Nodes.Length

                Vitest.expect((lineageToCanvas limited.Graph (Some "assays/Extraction/::Extraction")).CenterNodeId).toBe
                    "leaf-1"
        )
)

// Unfinished rows

Vitest.describe (
    "incompleteRows",
    fun () ->
        let wip = Sample.wipIndex
        let draft = "assays/Draft/"
        let extractionOfDraft = "assays/Draft/::Extraction"
        let plannedOfDraft = "assays/Draft/::Planned"

        let counts (c: IncompleteRows) = c.MissingOutputs, c.MissingInputs

        // The extraction table of the draft has four rows: one finished, two without an output and one without
        // an input. The planned table has two rows with a protocol and nothing else, so they count in both.

        Vitest.test (
            "counts the rows of the whole ARC in the datasets view",
            fun () -> Vitest.expect(counts (incompleteRows wip ProvenanceView.Datasets None)).toEqual (4, 3)
        )

        Vitest.test (
            "counts the rows of the dataset in the processes view",
            fun () ->
                Vitest.expect(counts (incompleteRows wip (ProvenanceView.Processes draft) None)).toEqual (4, 3)
                Vitest.expect(counts (incompleteRows wip (ProvenanceView.Processes "assays/Nope/") None)).toEqual (0, 0)
        )

        Vitest.test (
            "counts the rows of the process in the groups view and the rows of the group in the detail view",
            fun () ->
                let groups = ProvenanceView.Groups(draft, extractionOfDraft)

                let detail =
                    ProvenanceView.Detail(draft, extractionOfDraft, extractionOfDraft + "#in0")

                let outputDetail =
                    ProvenanceView.Detail(draft, extractionOfDraft, extractionOfDraft + "#out0")

                let planned = ProvenanceView.Groups(draft, plannedOfDraft)

                Vitest.expect(counts (incompleteRows wip groups None)).toEqual (2, 1)

                // The input group holds leaf-1 to leaf-3, so its detail shows the rows of those inputs. The row
                // without an input is not among them, and no shown row lacks an input.
                Vitest.expect(counts (incompleteRows wip detail None)).toEqual (2, 0)

                // The output group holds extract-1 and extract-4. The rows without an output are not shown.
                Vitest.expect(counts (incompleteRows wip outputDetail None)).toEqual (0, 1)
                Vitest.expect(counts (incompleteRows wip planned None)).toEqual (2, 2)
                Vitest.expect(counts (incompleteRows wip (ProvenanceView.Groups(draft, "nope")) None)).toEqual (0, 0)
        )

        Vitest.test (
            "counts only the rows of the selected group in the detail view",
            fun () ->
                let dataset = "assays/Extraction/"
                let extractionProcess = "assays/Extraction/::Extraction"
                let proc = Sample.index.ProcessByKey.[extractionProcess]

                // The example process has two input groups, each in a signature of its own. A row of the first
                // signature loses its output.
                let first = proc.InputGroups.[0]
                let signatureId = first.SignatureIds.[0]
                let signature = proc.Signatures |> Array.find (fun s -> s.Id = signatureId)

                let second =
                    proc.InputGroups
                    |> Array.find (fun group -> not (Array.contains signatureId group.SignatureIds))

                let rowById =
                    System.Collections.Generic.Dictionary<string, ProvenanceRow>(Sample.index.RowById)

                rowById.[signature.RowIds.[0]] <- {
                    rowById.[signature.RowIds.[0]] with
                        Outputs = [||]
                }

                let unfinished = { Sample.index with RowById = rowById }

                let detailOf (group: IoGroup) =
                    ProvenanceView.Detail(dataset, extractionProcess, group.Id)

                Vitest.expect(counts (incompleteRows unfinished (detailOf first) None)).toEqual (1, 0)
                Vitest.expect(counts (incompleteRows unfinished (detailOf second) None)).toEqual (0, 0)

                // The groups view counts the whole process.
                Vitest
                    .expect(counts (incompleteRows unfinished (ProvenanceView.Groups(dataset, extractionProcess)) None))
                    .toEqual (1, 0)

                Vitest
                    .expect(
                        counts (
                            incompleteRows unfinished (ProvenanceView.Detail(dataset, extractionProcess, "nope")) None
                        )
                    )
                    .toEqual (0, 0)
        )

        Vitest.test (
            "counts the rows that touch a node of the lineage graph",
            fun () ->
                let view =
                    ProvenanceView.Lineage(draft, extractionOfDraft, extractionOfDraft + "#in0", "leaf-2")

                let graph = (lineage wip "leaf-2").Value

                // The only row of leaf-2 has no output.
                Vitest.expect(counts (incompleteRows wip view (Some graph))).toEqual (1, 0)
                Vitest.expect(counts (incompleteRows wip view None)).toEqual (0, 0)

                // The node extract-4 has a row without an input.
                let other = (lineage wip "extract-4").Value
                Vitest.expect(counts (incompleteRows wip view (Some other))).toEqual (0, 1)

                // The finished row connects leaf-1 and extract-1 and counts nothing.
                let finished = (lineage wip "leaf-1").Value
                Vitest.expect(counts (incompleteRows wip view (Some finished))).toEqual (0, 0)
        )

        Vitest.test (
            "counts only the rows that the lineage walk reaches",
            fun () ->
                let dataset = "assays/Extraction/"
                let extractionProcess = "assays/Extraction/::Extraction"

                // Another row consumes leaf-1 and has no output. The lineage of extract-1 passes leaf-1 upstream.
                let sibling: ProvenanceRow = {
                    Id = "sibling"
                    Name = "sibling"
                    ProcessKey = extractionProcess
                    DatasetId = dataset
                    Inputs = [| "leaf-1" |]
                    Outputs = [||]
                    Annotations = [||]
                }

                let rowById =
                    System.Collections.Generic.Dictionary<string, ProvenanceRow>(Sample.index.RowById)

                rowById.[sibling.Id] <- sibling

                let consumedBy =
                    System.Collections.Generic.Dictionary<string, string[]>(Sample.index.ConsumedBy)

                consumedBy.["leaf-1"] <- Array.append consumedBy.["leaf-1"] [| sibling.Id |]

                let unfinished = {
                    Sample.index with
                        RowById = rowById
                        ConsumedBy = consumedBy
                }

                let viewOf (nodeId: string) =
                    ProvenanceView.Lineage(dataset, extractionProcess, extractionProcess + "#in0", nodeId)

                // The walk upstream follows the rows that produce a node, so the sibling is not part of it.
                let upstream = (lineage unfinished "extract-1").Value
                Vitest.expect(counts (incompleteRows unfinished (viewOf "extract-1") (Some upstream))).toEqual (0, 0)

                // The walk downstream from leaf-1 follows the rows that consume it, and the sibling is one of them.
                let downstream = (lineage unfinished "leaf-1").Value
                Vitest.expect(counts (incompleteRows unfinished (viewOf "leaf-1") (Some downstream))).toEqual (1, 0)
        )

        Vitest.test (
            "counts no row of a finished ARC",
            fun () ->
                let index = Sample.index
                let extractionProcess = "assays/Extraction/::Extraction"

                for view in
                    [|
                        ProvenanceView.Datasets
                        ProvenanceView.Processes "assays/Extraction/"
                        ProvenanceView.Groups("assays/Extraction/", extractionProcess)
                        ProvenanceView.Detail("assays/Extraction/", extractionProcess, extractionProcess + "#in0")
                    |] do
                    Vitest.expect(counts (incompleteRows index view None)).toEqual (0, 0)

                let graph = (lineage index "leaf-1").Value

                let lineageView =
                    ProvenanceView.Lineage(
                        "assays/Extraction/",
                        extractionProcess,
                        extractionProcess + "#in0",
                        "leaf-1"
                    )

                Vitest.expect(counts (incompleteRows index lineageView (Some graph))).toEqual (0, 0)
        )

        Vitest.test (
            "counts a row without input and output once for each side",
            fun () ->
                let row: ProvenanceRow = {
                    Id = "r"
                    Name = "r"
                    ProcessKey = "p"
                    DatasetId = "d"
                    Inputs = [||]
                    Outputs = [||]
                    Annotations = [||]
                }

                Vitest.expect(counts (countIncomplete [ row ])).toEqual (1, 1)
                Vitest.expect(counts (countIncomplete [])).toEqual (0, 0)
        )
)

Vitest.describe (
    "incompleteNote",
    fun () ->
        let note (outputs: int) (inputs: int) : string =
            incompleteNote {
                MissingOutputs = outputs
                MissingInputs = inputs
            }

        Vitest.test ("says nothing when no row is unfinished", fun () -> Vitest.expect(note 0 0).toBe "")

        Vitest.test (
            "names the counts and tells one row from several",
            fun () ->
                Vitest.expect((note 3 0).Contains "3").toBe true
                Vitest.expect((note 0 5).Contains "5").toBe true
                Vitest.expect(note 1 0 = note 3 0).toBe false
                Vitest.expect((note 2 7).Contains "2").toBe true
                Vitest.expect((note 2 7).Contains "7").toBe true
                // Both sentences stand in one note, and a side without rows does not appear.
                Vitest.expect((note 2 7).Length > (note 2 0).Length).toBe true
                Vitest.expect((note 0 5).Length < (note 2 5).Length).toBe true
        )
)
