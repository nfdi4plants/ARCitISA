/// One pure mapping per view, from the result of a provenance query to the generic canvas input.
module Arcitisa.Components.Page.ProvenanceGraph.ToCanvas

open System
open System.Collections.Generic
open Arcitisa.Components.Shared.ProvenanceGraph.Types
open Arcitisa.Components.Shared.ProvenanceGraph.Queries
open Arcitisa.Components.Composite.GraphCanvas.Types
open Arcitisa.Components.Page.ProvenanceGraph.Types

[<Literal>]
let LineageNodeLimit = 80

let private ioKindOrder = [|
    IoKind.Source
    IoKind.Sample
    IoKind.Material
    IoKind.Data
|]

let private plural (count: int) (word: string) : string =
    let suffix = if count = 1 then "" else "s"
    $"{count} {word}{suffix}"

let private nonEmpty (text: string option) : string option =
    match text with
    | Some text when not (String.IsNullOrEmpty text) -> Some text
    | _ -> None

let private valueText (annotation: Annotation) : string =
    let text =
        [ nonEmpty annotation.Value; nonEmpty annotation.Unit ]
        |> List.choose id
        |> String.concat " "

    if text = "" then "(empty)" else text

/// The color of a dataset kind.
let colorOfKind (kind: DatasetKind) : CanvasColor =
    match kind with
    | DatasetKind.Study -> CanvasColor.Study
    | DatasetKind.Assay -> CanvasColor.Assay
    | DatasetKind.Run -> CanvasColor.Run

/// The pill category has the same text as the annotation category.
let pillCategoryOf (category: AnnotationCategory) : PillCategory =
    match category with
    | AnnotationCategory.Characteristic -> PillCategory.Characteristic
    | AnnotationCategory.Factor -> PillCategory.Factor
    | AnnotationCategory.Parameter -> PillCategory.Parameter
    | AnnotationCategory.Component -> PillCategory.Component
    | AnnotationCategory.Protocol -> PillCategory.Protocol
    | AnnotationCategory.Performer -> PillCategory.Performer
    | AnnotationCategory.Date -> PillCategory.Date
    | AnnotationCategory.Comment -> PillCategory.Comment

let private baseNode (id: string) (title: string) (color: CanvasColor) (ariaLabel: string) : CanvasNode = {
    Id = id
    Title = title
    Subtitle = None
    Color = color
    Dashed = false
    Mono = false
    Selected = false
    Emphasized = false
    Pills = [||]
    Tooltip = None
    AriaLabel = ariaLabel
    Clickable = true
}

let private baseEdge (id: string) (from: string) (target: string) (color: CanvasColor) : CanvasEdge = {
    Id = id
    From = from
    To = target
    Color = color
    Highlighted = false
    Pills = [||]
    MorePills = [||]
    Tooltip = None
}

/// One property pill per annotation. The key holds the position, so equal annotations get different keys.
let propertyPills (annotations: Annotation[]) (prefix: string) : CanvasPill[] =
    annotations
    |> Array.mapi (fun index annotation ->
        let pill: CanvasPill = {
            Key = $"{prefix}{index}:{AnnotationCategory.toText annotation.Category}:{annotation.Name}"
            Kind = PillKind.Property
            Name = Some annotation.Name
            Value = annotation.Value
            Unit = annotation.Unit
            Category = Some(pillCategoryOf annotation.Category)
            Tooltip = None
        }

        pill
    )

/// Datasets and processes connect through shared nodes, and the edge shows how many.
let private countEdges (edges: SharedNodeEdge[]) : CanvasEdge[] =
    edges
    |> Array.map (fun edge ->
        let count = edge.SharedNodeIds.Length

        let tooltip =
            if count = 1 then
                "1 shared input or output"
            else
                $"{count} shared inputs and outputs"

        let pill: CanvasPill = {
            Key = "count"
            Kind = PillKind.Count
            Name = None
            Value = Some(string count)
            Unit = None
            Category = None
            Tooltip = Some tooltip
        }

        {
            baseEdge edge.Id edge.From edge.To CanvasColor.Neutral with
                Pills = [| pill |]
        }
    )

let datasetsToCanvas (graph: DatasetGraph) : CanvasGraph = {
    Nodes =
        graph.Nodes
        |> Array.map (fun node ->
            let kind = DatasetKind.toText node.Kind

            {
                baseNode
                    node.Id
                    node.Label
                    (colorOfKind node.Kind)
                    $"{node.Label}, {kind}, select to show its processes" with
                    Tooltip = Some $"{node.Label} ({kind})"
            }
        )
    Edges = countEdges graph.Edges
}

let processesToCanvas (graph: ProcessGraph) : CanvasGraph = {
    Nodes =
        graph.Nodes
        |> Array.map (fun node ->
            let baseOfNode ariaLabel = {
                baseNode node.Key node.Label (colorOfKind node.DatasetKind) ariaLabel with
                    Tooltip = Some $"{node.Label} ({node.DatasetLabel})"
            }

            if node.External then
                {
                    baseOfNode $"{node.Label}, {node.DatasetLabel}, other dataset, select to show its groups" with
                        Subtitle = Some node.DatasetLabel
                        Dashed = true
                }
            else
                let rows = plural node.RowCount "row"

                {
                    baseOfNode $"{node.Label}, {node.DatasetLabel}, {rows}, select to show its groups" with
                        Subtitle = Some rows
                }
        )
    Edges = countEdges graph.Edges
}

/// "3 samples · 2 data". Only the word "data" has no plural form.
let private kindSummary (counts: (IoKind * int)[]) : string =
    ioKindOrder
    |> Array.choose (fun kind ->
        let count = counts |> Array.sumBy (fun (k, n) -> if k = kind then n else 0)

        if count <= 0 then None
        elif kind = IoKind.Data then Some $"{count} data"
        else Some(plural count (IoKind.toText kind))
    )
    |> String.concat " · "

let groupsToCanvas (graph: GroupGraph) : CanvasGraph =
    let color = colorOfKind graph.DatasetKind

    {
        Nodes =
            graph.Nodes
            |> Array.map (fun node ->
                let summary = kindSummary node.KindCounts
                let hasSummary = summary <> ""

                let named =
                    if hasSummary then
                        node.Label + ", " + summary
                    else
                        node.Label

                let ariaLabel = $"{named}, select to list its nodes"

                {
                    baseNode node.Id node.Label color ariaLabel with
                        Subtitle = if hasSummary then Some summary else None
                        Tooltip =
                            Some(
                                if hasSummary then
                                    $"{node.Label} ({summary})"
                                else
                                    node.Label
                            )
                }
            )
        Edges =
            graph.Edges
            |> Array.map (fun edge -> {
                baseEdge edge.Id edge.From edge.To color with
                    Pills = propertyPills edge.Differing "d"
                    MorePills = propertyPills edge.Shared "s"
            })
    }

/// Category, name, value and unit make two annotations the same here. The accession is not part
/// of the key. The prefix "s:" separates a given text from a missing one, so a missing value
/// never equals an empty value or the text "n".
let private canvasAnnotationKey (annotation: Annotation) : string =
    let field (text: string option) =
        match text with
        | Some text -> "s:" + text
        | None -> "n"

    jsonStringArray [
        AnnotationCategory.toText annotation.Category
        annotation.Name
        field annotation.Value
        field annotation.Unit
    ]

/// Annotations that every node of the graph carries identically. A single node has nothing to
/// compare against, so all of its annotations stay as pills.
let commonAnnotationKeys (nodes: DetailNode[]) : HashSet<string> =
    let common = HashSet<string>()

    if nodes.Length >= 2 then
        let keySets =
            nodes
            |> Array.map (fun node -> HashSet<string>(node.Annotations |> Array.map canvasAnnotationKey))

        for annotation in nodes.[0].Annotations do
            let key = canvasAnnotationKey annotation

            if keySets |> Array.forall (fun keys -> keys.Contains key) then
                common.Add key |> ignore

    common

let private categoryLabel (annotation: Annotation) : string =
    let text = AnnotationCategory.toText annotation.Category
    text.Substring(0, 1).ToUpperInvariant() + text.Substring 1

/// `describe` adds the part of the aria label that depends on the view.
let detailNodes
    (nodes: DetailNode[])
    (color: CanvasColor)
    (describe: DetailNode -> string)
    (selectedId: string option)
    : CanvasNode[] =
    let common = commonAnnotationKeys nodes
    // A node shows its kind only in a graph where the kinds differ.
    let kinds = HashSet<string>()

    for node in nodes do
        kinds.Add(IoKind.toText node.Kind) |> ignore

    let showKind = kinds.Count > 1

    nodes
    |> Array.map (fun node ->
        let isCommon annotation =
            common.Contains(canvasAnnotationKey annotation)

        let moved = node.Annotations |> Array.filter isCommon
        let own = node.Annotations |> Array.filter (isCommon >> not)

        let movedText =
            moved
            |> Array.map (fun a -> $"{categoryLabel a} · {a.Name}: {valueText a}")
            |> String.concat " · "

        let kind = IoKind.toText node.Kind
        let tooltip = $"{node.Id} ({kind})"

        let described =
            if movedText <> "" then
                $"{node.Id}, {kind}, {movedText}"
            else
                $"{node.Id}, {kind}"

        {
            baseNode node.Id node.Id color (described + describe node) with
                Subtitle = if showKind then Some kind else None
                Mono = true
                Selected =
                    (match selectedId with
                     | Some selected -> node.Id = selected
                     | None -> false)
                Emphasized = node.Emphasized
                Pills = propertyPills own "p"
                Tooltip =
                    Some(
                        if movedText <> "" then
                            $"{tooltip}: {movedText}"
                        else
                            tooltip
                    )
        }
    )

let private detailEdges (edges: DetailEdge[]) (showProcess: bool) (isHighlighted: DetailEdge -> bool) : CanvasEdge[] =
    edges
    |> Array.map (fun edge ->
        let processPill: CanvasPill[] =
            if showProcess then
                [|
                    {
                        Key = "process"
                        Kind = PillKind.Process
                        Name = None
                        Value = Some edge.ProcessName
                        Unit = None
                        Category = None
                        Tooltip = Some $"Process {edge.ProcessName}"
                    }
                |]
            else
                [||]

        {
            baseEdge edge.Id edge.From edge.To (colorOfKind edge.DatasetKind) with
                Highlighted = isHighlighted edge
                Pills = Array.append processPill (propertyPills edge.Differing "d")
                MorePills = propertyPills edge.Shared "s"
        }
    )

let detailToCanvas (graph: ProcessDetailGraph) : CanvasGraph =
    // All edges of one process belong to one dataset, so the first edge gives the nodes their color.
    let color =
        match Array.tryHead graph.Edges with
        | Some edge -> colorOfKind edge.DatasetKind
        | None -> CanvasColor.Neutral

    {
        Nodes =
            detailNodes
                graph.Nodes
                color
                (fun node ->
                    (if node.Emphasized then
                         $", member of {graph.Group.Label}"
                     else
                         "")
                    + ", select to show its lineage"
                )
                None
        Edges = detailEdges graph.Edges false (fun _ -> false)
    }

let lineageToCanvas (graph: LineageGraph) (selectedProcess: string option) : LineageCanvas =
    let processKeys = HashSet<string>()

    for edge in graph.Edges do
        processKeys.Add edge.ProcessKey |> ignore

    let showProcess = processKeys.Count > 1

    {
        Nodes =
            detailNodes
                graph.Nodes
                CanvasColor.Neutral
                (fun node ->
                    if node.Id = graph.NodeId then
                        ", selected lineage node"
                    else
                        ", select to center the lineage on it"
                )
                (Some graph.NodeId)
        Edges =
            detailEdges
                graph.Edges
                showProcess
                (fun edge ->
                    match selectedProcess with
                    | Some selected -> edge.ProcessKey = selected
                    | None -> false
                )
        CenterNodeId = graph.NodeId
    }

/// Keeps the nodes closest to the selected one. The distance ignores edge direction, and nodes
/// at equal distance keep the order of the graph.
let limitLineageTo (max: int) (graph: LineageGraph) : LimitedLineage =
    let total = graph.Nodes.Length

    if total <= max then
        { Graph = graph; Total = total }
    else
        let neighbors = Dictionary<string, ResizeArray<string>>()

        let link (a: string) (b: string) =
            match neighbors.TryGetValue a with
            | true, list -> list.Add b
            | _ ->
                let list = ResizeArray<string>()
                list.Add b
                neighbors.[a] <- list

        for edge in graph.Edges do
            link edge.From edge.To
            link edge.To edge.From

        // The queue doubles as the visiting order, so it grows while the loop runs.
        let distance = Dictionary<string, int>()
        distance.[graph.NodeId] <- 0
        let queue = ResizeArray<string>()
        queue.Add graph.NodeId
        let mutable i = 0

        while i < queue.Count do
            let current = queue.[i]

            match neighbors.TryGetValue current with
            | true, list ->
                for next in list do
                    if not (distance.ContainsKey next) then
                        distance.[next] <- distance.[current] + 1
                        queue.Add next
            | _ -> ()

            i <- i + 1

        let kept = HashSet<string>()

        graph.Nodes
        |> Array.mapi (fun order node ->
            let d =
                match distance.TryGetValue node.Id with
                | true, d -> d
                | _ -> Int32.MaxValue

            node.Id, order, d
        )
        |> Array.sortWith (fun (_, orderA, a) (_, orderB, b) -> if a <> b then compare a b else compare orderA orderB)
        |> Array.truncate max
        |> Array.iter (fun (id, _, _) -> kept.Add id |> ignore)

        {
            Total = total
            Graph = {
                graph with
                    Nodes = graph.Nodes |> Array.filter (fun node -> kept.Contains node.Id)
                    Edges =
                        graph.Edges
                        |> Array.filter (fun edge -> kept.Contains edge.From && kept.Contains edge.To)
            }
        }

let limitLineage (graph: LineageGraph) : LimitedLineage = limitLineageTo LineageNodeLimit graph

// Aria labels of the whole canvas, one per view.

let datasetsAriaLabel = "Dataset provenance graph"
let processesAriaLabel = "Process provenance graph"

let groupsAriaLabel (graph: GroupGraph) : string =
    $"Input groups and output groups of {graph.Process.Name}"

let detailAriaLabel (graph: ProcessDetailGraph) : string =
    $"Nodes connected to {graph.Group.Label} of {graph.Process.Name}"

let lineageAriaLabel (graph: LineageGraph) : string = $"Lineage graph for {graph.NodeId}"

// Rows that are not finished

/// Counts the rows without an output and the rows without an input. A row with neither counts in both.
let countIncomplete (rows: seq<ProvenanceRow>) : IncompleteRows =
    let mutable missingOutputs = 0
    let mutable missingInputs = 0

    for row in rows do
        if row.Outputs.Length = 0 then
            missingOutputs <- missingOutputs + 1

        if row.Inputs.Length = 0 then
            missingInputs <- missingInputs + 1

    {
        MissingOutputs = missingOutputs
        MissingInputs = missingInputs
    }

/// The incomplete rows that belong to a view: all rows in the datasets view, the rows of the dataset in the
/// processes view, and the rows of the process in the groups view. The detail view counts the rows that the
/// detail graph shows, the rows of the process that have a member of the selected group on the side of the group.
/// The lineage view counts the rows of the walk from the node of the lineage graph that touch a node of that
/// graph. The caller has the graph after the cut. An id that the index does not know counts no row.
let incompleteRows
    (index: ProvenanceIndex)
    (view: ProvenanceView)
    (lineageGraph: LineageGraph option)
    : IncompleteRows =
    let rowOf (rowId: string) : ProvenanceRow option =
        match index.RowById.TryGetValue rowId with
        | true, row -> Some row
        | _ -> None

    match view with
    | ProvenanceView.Datasets -> countIncomplete index.Rows
    | ProvenanceView.Processes datasetId ->
        index.Rows
        |> Array.filter (fun row -> row.DatasetId = datasetId)
        |> countIncomplete
    | ProvenanceView.Groups(_, processKey) ->
        match index.ProcessByKey.TryGetValue processKey with
        | true, p -> p.RowIds |> Array.choose rowOf |> countIncomplete
        | _ -> countIncomplete []
    | ProvenanceView.Detail(_, processKey, groupId) ->
        match index.ProcessByKey.TryGetValue processKey, index.GroupById.TryGetValue groupId with
        | (true, p), (true, group) ->
            let members = HashSet<string>(group.NodeIds)

            let idsOnSide (row: ProvenanceRow) =
                match group.Side with
                | IoSide.Inputs -> row.Inputs
                | IoSide.Outputs -> row.Outputs

            p.RowIds
            |> Array.choose rowOf
            |> Array.filter (fun row -> idsOnSide row |> Array.exists members.Contains)
            |> countIncomplete
        | _ -> countIncomplete []
    | ProvenanceView.Lineage _ ->
        match lineageGraph with
        | None -> countIncomplete []
        | Some graph ->
            let shown = HashSet<string>(graph.Nodes |> Array.map (fun node -> node.Id))

            lineageRows index graph.NodeId
            |> Array.filter (fun row ->
                Array.exists shown.Contains row.Inputs
                || Array.exists shown.Contains row.Outputs
            )
            |> countIncomplete

/// The note for unfinished rows, such as "3 rows have no output yet". Empty when no row is unfinished.
let incompleteNote (counts: IncompleteRows) : string =
    let sentence (count: int) (missing: string) : string option =
        if count = 0 then None
        elif count = 1 then Some $"1 row has no {missing} yet."
        else Some $"{count} rows have no {missing} yet."

    [
        sentence counts.MissingOutputs "output"
        sentence counts.MissingInputs "input"
    ]
    |> List.choose id
    |> String.concat " "

// Legend

let private legendItem (label: string) (swatch: LegendSwatch) : LegendItem = { Label = label; Swatch = swatch }

let private datasetKindItems = [|
    legendItem "Study" (LegendSwatch.Color CanvasColor.Study)
    legendItem "Assay" (LegendSwatch.Color CanvasColor.Assay)
    legendItem "Run" (LegendSwatch.Color CanvasColor.Run)
|]

let private categoryItems = [|
    legendItem "Characteristic" (LegendSwatch.Category PillCategory.Characteristic)
    legendItem "Factor" (LegendSwatch.Category PillCategory.Factor)
    legendItem "Parameter" (LegendSwatch.Category PillCategory.Parameter)
    legendItem "Component" (LegendSwatch.Category PillCategory.Component)
    legendItem "Protocol and other" LegendSwatch.Plain
|]

/// The footer shows the whole scale that can occur in a view, whatever the graph holds.
let legendItems (view: ProvenanceView) : LegendItem[] =
    match view with
    | ProvenanceView.Datasets -> datasetKindItems
    | ProvenanceView.Processes _ -> Array.append datasetKindItems [| legendItem "Other dataset" LegendSwatch.Dashed |]
    | ProvenanceView.Groups _
    | ProvenanceView.Detail _
    | ProvenanceView.Lineage _ -> Array.append datasetKindItems categoryItems
