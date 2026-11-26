/// Pure functions from a ProvenanceIndex to the graph data of each detail level.
/// None of them changes the index. An unknown id gives None.
///
/// Every ordered output follows the insertion order of explicit arrays. The
/// Dictionary and HashSet values only answer lookups by string key.
module Arcitisa.Components.Shared.ProvenanceGraph.Queries

open System.Collections.Generic
open Arcitisa.Components.Shared.ProvenanceGraph.Types

[<NoEquality; NoComparison>]
type private RowLink = {
    Row: ProvenanceRow
    From: string
    To: string
}

[<NoEquality; NoComparison>]
type private SharedEdgeBuilder = {
    Id: string
    From: string
    To: string
    Shared: ResizeArray<string>
}

/// The index lists a dataset for every row, so the Run fallback only covers a hand-built index.
let private datasetKindOf (index: ProvenanceIndex) (datasetId: string) : DatasetKind =
    match tryFind index.DatasetById datasetId with
    | Some dataset -> dataset.Kind
    | None -> DatasetKind.Run

/// Inputs and outputs without a node entry count as data.
let private ioKindOf (index: ProvenanceIndex) (nodeId: string) : IoKind =
    match tryFind index.NodeById nodeId with
    | Some node -> node.Kind
    | None -> IoKind.Data

/// The ids of all nodes that some row produces, in the order the rows first
/// name them. The ProducedBy lookup has no order of its own.
let private producedNodeIds (index: ProvenanceIndex) : string[] =
    index.Rows |> Array.collect (fun row -> row.Outputs) |> Array.distinct

/// Edges between the owners of producing and consuming rows, with the distinct nodes passed along.
/// Per node the distinct owners are collected first and crossed afterwards, so N producing rows
/// and N consuming rows cost about 2N lookups.
let private sharedNodeEdges
    (index: ProvenanceIndex)
    (ownerOf: ProvenanceRow -> string)
    (keep: string -> string -> bool)
    : SharedNodeEdge[] =
    let ownersOf (rowIds: string[]) : string[] =
        rowIds
        |> Array.choose (fun rowId -> tryFind index.RowById rowId |> Option.map ownerOf)
        |> Array.distinct

    let edgeById = Dictionary<string, SharedEdgeBuilder>()
    let edges = ResizeArray<SharedEdgeBuilder>()

    for nodeId in producedNodeIds index do
        let producerIds = tryFind index.ProducedBy nodeId |> Option.defaultValue [||]

        let consumers =
            ownersOf (tryFind index.ConsumedBy nodeId |> Option.defaultValue [||])

        for source in ownersOf producerIds do
            for target in consumers do
                if source <> target && keep source target then
                    let id = source + "->" + target

                    let edge =
                        match tryFind edgeById id with
                        | Some edge -> edge
                        | None ->
                            let edge: SharedEdgeBuilder = {
                                Id = id
                                From = source
                                To = target
                                Shared = ResizeArray<string>()
                            }

                            edgeById.[id] <- edge
                            edges.Add edge
                            edge

                    edge.Shared.Add nodeId

    edges
    |> Seq.map (fun edge ->
        let result: SharedNodeEdge = {
            Id = edge.Id
            From = edge.From
            To = edge.To
            SharedNodeIds = edge.Shared.ToArray()
        }

        result
    )
    |> Array.ofSeq

let datasetGraph (index: ProvenanceIndex) : DatasetGraph = {
    Nodes =
        index.Datasets
        |> Array.map (fun dataset ->
            let node: DatasetGraphNode = {
                Id = dataset.Id
                Kind = dataset.Kind
                Label = dataset.Identifier
            }

            node
        )
    Edges = sharedNodeEdges index (fun row -> row.DatasetId) (fun _ _ -> true)
}

let processGraph (index: ProvenanceIndex) (datasetId: string) : ProcessGraph option =
    match tryFind index.DatasetById datasetId with
    | None -> None
    | Some dataset ->
        let own = HashSet<string>()

        for p in index.Processes do
            if p.DatasetId = datasetId then
                own.Add p.Key |> ignore

        let edges =
            sharedNodeEdges
                index
                (fun row -> row.ProcessKey)
                (fun source target -> own.Contains source || own.Contains target)

        let connected = HashSet<string>()

        for edge in edges do
            connected.Add edge.From |> ignore
            connected.Add edge.To |> ignore

        let nodes =
            index.Processes
            |> Array.filter (fun p -> own.Contains p.Key || connected.Contains p.Key)
            |> Array.map (fun p ->
                let processDataset = tryFind index.DatasetById p.DatasetId

                let node: ProcessGraphNode = {
                    Key = p.Key
                    Label = p.Name
                    DatasetId = p.DatasetId
                    DatasetKind =
                        match processDataset with
                        | Some d -> d.Kind
                        | None -> DatasetKind.Run
                    DatasetLabel =
                        match processDataset with
                        | Some d -> d.Identifier
                        | None -> p.DatasetId
                    External = not (own.Contains p.Key)
                    RowCount = p.RowIds.Length
                }

                node
            )

        Some {
            Dataset = dataset
            Nodes = nodes
            Edges = edges
        }

/// Member counts per kind in the fixed order source, sample, material, data.
/// Kinds without members are left out.
let private kindCounts (index: ProvenanceIndex) (nodeIds: string[]) : (IoKind * int)[] =
    let counts = [| 0; 0; 0; 0 |]

    for nodeId in nodeIds do
        let slot =
            match ioKindOf index nodeId with
            | IoKind.Source -> 0
            | IoKind.Sample -> 1
            | IoKind.Material -> 2
            | IoKind.Data -> 3

        counts.[slot] <- counts.[slot] + 1

    [|
        (IoKind.Source, counts.[0])
        (IoKind.Sample, counts.[1])
        (IoKind.Material, counts.[2])
        (IoKind.Data, counts.[3])
    |]
    |> Array.filter (fun (_, count) -> count > 0)

let processGroups (index: ProvenanceIndex) (processKey: string) : GroupGraph option =
    match tryFind index.ProcessByKey processKey with
    | None -> None
    | Some p ->
        let signatures = Dictionary<string, ProcessSignature>()

        for signature in p.Signatures do
            signatures.[signature.Id] <- signature

        let nodes =
            Array.append p.InputGroups p.OutputGroups
            |> Array.map (fun group ->
                let node: GroupGraphNode = {
                    Id = group.Id
                    Side = group.Side
                    Label = group.Label
                    KindCounts = kindCounts index group.NodeIds
                }

                node
            )

        let edges =
            p.GroupEdges
            |> Array.map (fun edge ->
                let signature = tryFind signatures edge.SignatureId

                let differing =
                    match signature with
                    | Some s -> s.Distinguishing
                    | None -> [||]

                let differingKeys = HashSet<string>(differing |> Array.map annotationKey)

                let shared =
                    match signature with
                    | Some s ->
                        s.Annotations
                        |> Array.filter (fun a -> not (differingKeys.Contains(annotationKey a)))
                    | None -> [||]

                let result: GroupGraphEdge = {
                    Id = edge.Id
                    From = edge.From
                    To = edge.To
                    Differing = differing
                    Shared = shared
                }

                result
            )

        Some {
            Process = p
            DatasetKind = datasetKindOf index p.DatasetId
            Nodes = nodes
            Edges = edges
        }

/// Splits the step annotations of the edges of one process. An annotation is shared when every
/// list has it. The result has one entry per input list, in the same order, and keeps the
/// annotation order of each list. A single list is shared completely.
let splitSharedAnnotations (lists: Annotation[][]) : AnnotationSplit[] =
    if lists.Length = 0 then
        [||]
    else
        let keySets =
            lists
            |> Array.map (fun list -> HashSet<string>(list |> Array.map annotationKey))

        let common = HashSet<string>()

        for annotation in lists.[0] do
            let key = annotationKey annotation

            if keySets |> Array.forall (fun keys -> keys.Contains key) then
                common.Add key |> ignore

        lists
        |> Array.map (fun list ->
            let differing = ResizeArray<Annotation>()
            let shared = ResizeArray<Annotation>()

            for annotation in list do
                if common.Contains(annotationKey annotation) then
                    shared.Add annotation
                else
                    differing.Add annotation

            let split: AnnotationSplit = {
                Differing = differing.ToArray()
                Shared = shared.ToArray()
            }

            split
        )

/// One edge per link. The shared/differing split is computed per process within the given links.
let private toDetailEdges (index: ProvenanceIndex) (links: RowLink[]) : DetailEdge[] =
    let processKeys = ResizeArray<string>()
    let positionsByProcess = Dictionary<string, ResizeArray<int>>()

    links
    |> Array.iteri (fun position link ->
        match tryFind positionsByProcess link.Row.ProcessKey with
        | Some positions -> positions.Add position
        | None ->
            processKeys.Add link.Row.ProcessKey
            let positions = ResizeArray<int>()
            positions.Add position
            positionsByProcess.[link.Row.ProcessKey] <- positions
    )

    let emptySplit: AnnotationSplit = { Differing = [||]; Shared = [||] }

    let splits = Array.init links.Length (fun _ -> emptySplit)

    for processKey in processKeys do
        let positions = positionsByProcess.[processKey].ToArray()

        let result =
            splitSharedAnnotations (positions |> Array.map (fun position -> links.[position].Row.Annotations))

        positions |> Array.iteri (fun i position -> splits.[position] <- result.[i])

    links
    |> Array.mapi (fun position link ->
        let row = link.Row

        let edge: DetailEdge = {
            Id = row.Id + "|" + link.From + "|" + link.To
            From = link.From
            To = link.To
            ProcessKey = row.ProcessKey
            ProcessName =
                match tryFind index.ProcessByKey row.ProcessKey with
                | Some p -> p.Name
                | None -> row.ProcessKey
            DatasetKind = datasetKindOf index row.DatasetId
            Differing = splits.[position].Differing
            Shared = splits.[position].Shared
        }

        edge
    )

let private toDetailNode (index: ProvenanceIndex) (id: string) (emphasized: bool) : DetailNode =
    match tryFind index.NodeById id with
    | Some node -> {
        Id = id
        Kind = node.Kind
        Annotations = node.Annotations
        Emphasized = emphasized
      }
    | None -> {
        Id = id
        Kind = IoKind.Data
        Annotations = [||]
        Emphasized = emphasized
      }

/// IoGroup has no reference to its process, so the group is looked up through the processes.
let private processOfGroup (index: ProvenanceIndex) (groupId: string) : ProvenanceProcess option =
    index.Processes
    |> Array.tryFind (fun p ->
        p.InputGroups |> Array.exists (fun group -> group.Id = groupId)
        || p.OutputGroups |> Array.exists (fun group -> group.Id = groupId)
    )

let processDetail (index: ProvenanceIndex) (groupId: string) : ProcessDetailGraph option =
    match tryFind index.GroupById groupId, processOfGroup index groupId with
    | Some group, Some p ->
        let members = HashSet<string>(group.NodeIds)
        let memberSide = group.Side
        let links = ResizeArray<RowLink>()
        let counterpartSeen = HashSet<string>()
        let counterparts = ResizeArray<string>()

        for rowId in p.RowIds do
            match tryFind index.RowById rowId with
            | None -> ()
            | Some row ->
                let memberIds =
                    match memberSide with
                    | IoSide.Inputs -> row.Inputs
                    | IoSide.Outputs -> row.Outputs

                if memberIds |> Array.exists members.Contains then
                    // Only the member side is narrowed to the group. The other side keeps every node of the row.
                    let inputs =
                        match memberSide with
                        | IoSide.Inputs -> row.Inputs |> Array.filter members.Contains
                        | IoSide.Outputs -> row.Inputs

                    let outputs =
                        match memberSide with
                        | IoSide.Outputs -> row.Outputs |> Array.filter members.Contains
                        | IoSide.Inputs -> row.Outputs

                    let otherSide =
                        match memberSide with
                        | IoSide.Inputs -> row.Outputs
                        | IoSide.Outputs -> row.Inputs

                    for id in otherSide do
                        if counterpartSeen.Add id then
                            counterparts.Add id

                    for source in inputs do
                        for target in outputs do
                            if source <> target then
                                links.Add(
                                    {
                                        Row = row
                                        From = source
                                        To = target
                                    }
                                )

        let nodes =
            Array.append
                (group.NodeIds |> Array.map (fun id -> toDetailNode index id true))
                (counterparts.ToArray()
                 |> Array.filter (fun id -> not (members.Contains id))
                 |> Array.map (fun id -> toDetailNode index id false))

        Some {
            Process = p
            Group = group
            Nodes = nodes
            Edges = toDetailEdges index (links.ToArray())
        }
    | _ -> None

/// The nodes and rows reached by a walk, each in first-reached order.
[<NoEquality; NoComparison>]
type private Walked = {
    NodeIds: ResizeArray<string>
    Rows: ResizeArray<ProvenanceRow>
}

/// Walks from the start node along producing rows (upstream) or consuming rows (downstream).
/// The visited set stops the walk on cycles.
let private walk
    (index: ProvenanceIndex)
    (startId: string)
    (rowIdsByNode: Dictionary<string, string[]>)
    (farSide: IoSide)
    : Walked =
    let nodeIds = ResizeArray<string>()
    let visited = HashSet<string>()
    let rows = ResizeArray<ProvenanceRow>()
    let seenRows = HashSet<string>()
    nodeIds.Add startId
    visited.Add startId |> ignore
    let mutable i = 0

    // nodeIds doubles as the queue, so it grows while this loop runs.
    while i < nodeIds.Count do
        for rowId in tryFind rowIdsByNode nodeIds.[i] |> Option.defaultValue [||] do
            match tryFind index.RowById rowId with
            | None -> ()
            | Some row ->
                if seenRows.Add row.Id then
                    rows.Add row

                let farIds =
                    match farSide with
                    | IoSide.Inputs -> row.Inputs
                    | IoSide.Outputs -> row.Outputs

                for id in farIds do
                    if visited.Add id then
                        nodeIds.Add id

        i <- i + 1

    { NodeIds = nodeIds; Rows = rows }

/// The rows of the upstream walk, then the rows of the downstream walk, each row once.
let private walkedRows (upstream: Walked) (downstream: Walked) : ProvenanceRow[] =
    Array.append (upstream.Rows.ToArray()) (downstream.Rows.ToArray())
    |> Array.distinctBy (fun row -> row.Id)

/// The rows that the lineage of a node walks over, in the order the lineage uses them: first the rows that
/// produce the node and, further upstream, the rows that produce their inputs, then the rows that consume the
/// node and, further downstream, the rows that consume their outputs. A row that only shares a node with the
/// walk, such as another consumer of an upstream node, is not among them. A node that the index does not
/// know has no rows.
let lineageRows (index: ProvenanceIndex) (nodeId: string) : ProvenanceRow[] =
    walkedRows (walk index nodeId index.ProducedBy IoSide.Inputs) (walk index nodeId index.ConsumedBy IoSide.Outputs)

let lineage (index: ProvenanceIndex) (nodeId: string) : LineageGraph option =
    if not (index.NodeById.ContainsKey nodeId) then
        None
    else
        let upstream = walk index nodeId index.ProducedBy IoSide.Inputs
        let downstream = walk index nodeId index.ConsumedBy IoSide.Outputs

        // The start node leads, then the nodes upstream, then those only reached downstream.
        let nodeIds =
            Array.concat [
                [| nodeId |]
                upstream.NodeIds.ToArray()
                downstream.NodeIds.ToArray()
            ]
            |> Array.distinct

        let seenNodes = HashSet<string>(nodeIds)

        let links = ResizeArray<RowLink>()

        for row in walkedRows upstream downstream do
            for source in row.Inputs do
                if seenNodes.Contains source then
                    // A row whose input and output share a name would draw as a loop on one node.
                    for target in row.Outputs do
                        if target <> source && seenNodes.Contains target then
                            links.Add(
                                {
                                    Row = row
                                    From = source
                                    To = target
                                }
                            )

        Some {
            NodeId = nodeId
            Nodes = nodeIds |> Array.map (fun id -> toDetailNode index id (id = nodeId))
            Edges = toDetailEdges index (links.ToArray())
        }
