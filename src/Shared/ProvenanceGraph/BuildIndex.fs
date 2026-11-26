/// Builds the provenance data of one investigation from its annotation tables,
/// and the lookups the queries need.
///
/// Every ordered output comes from explicit insertion-ordered arrays. A Dictionary
/// or HashSet only answers lookups by string key and is never enumerated for order.
module Arcitisa.Components.Shared.ProvenanceGraph.BuildIndex

open System
open System.Collections.Generic
open ARCtrl
open Arcitisa.Components.Shared.ProvenanceGraph.Types

[<NoEquality; NoComparison>]
type private DatasetEntry = {
    Dataset: ProvenanceDataset
    Tables: ResizeArray<ArcTable>
}

[<NoEquality; NoComparison>]
type private AnnotationSet = {
    Seen: HashSet<string>
    Items: ResizeArray<Annotation>
}

[<NoEquality; NoComparison>]
type private NodeInfo = {
    Kind: IoKind
    Characteristics: AnnotationSet
    Factors: AnnotationSet
}

/// A process before its signatures and groups exist.
[<NoEquality; NoComparison>]
type private ProcessShell = {
    Key: string
    Name: string
    DatasetId: string
    RowIds: string[]
}

[<NoEquality; NoComparison>]
type private ReadResult = {
    Shells: ResizeArray<ProcessShell>
    Rows: ResizeArray<ProvenanceRow>
    Infos: Dictionary<string, NodeInfo>
    OwnIo: Dictionary<string, Annotation[]>
}

[<NoEquality; NoComparison>]
type private SignatureBuilder = {
    Id: string
    Annotations: Annotation[]
    RowIds: ResizeArray<string>
}

/// Annotations of one signature that share a category and a name, in first-seen order.
[<NoEquality; NoComparison>]
type private NamedAnnotations = {
    Keys: ResizeArray<string>
    ByKey: Dictionary<string, ResizeArray<Annotation>>
}

[<NoEquality; NoComparison>]
type private GroupBuilder = {
    Id: string
    Side: IoSide
    Label: string
    NodeIds: ResizeArray<string>
    SignatureIds: string[]
}

[<NoEquality; NoComparison>]
type private EdgeBuilder = {
    Id: string
    From: string
    To: string
    SignatureId: string
    RowIds: ResizeArray<string>
}

[<NoEquality; NoComparison>]
type private NodeBuilder = {
    Id: string
    Kind: IoKind
    Annotations: Annotation[]
    DatasetIds: ResizeArray<string>
    ProcessKeys: ResizeArray<string>
}

// Helpers

let private nonEmpty (text: string) : string option =
    if String.IsNullOrEmpty text then None else Some text

// Annotations

let private compareText (a: string) (b: string) : int = String.CompareOrdinal(a, b)

let private orElse (next: unit -> int) (current: int) : int =
    if current = 0 then next () else current

let private compareAnnotations (a: Annotation) (b: Annotation) : int =
    compareText a.Name b.Name
    |> orElse (fun () -> compareText (Option.defaultValue "" a.Value) (Option.defaultValue "" b.Value))
    |> orElse (fun () -> compareText (Option.defaultValue "" a.Unit) (Option.defaultValue "" b.Unit))
    |> orElse (fun () -> compareText (Option.defaultValue "" a.Accession) (Option.defaultValue "" b.Accession))
    |> orElse (fun () -> compareText (AnnotationCategory.toText a.Category) (AnnotationCategory.toText b.Category))

/// A stable sort, so the JavaScript and the .NET result agree.
let private sortAnnotations (annotations: Annotation[]) : Annotation[] =
    annotations |> List.ofArray |> List.sortWith compareAnnotations |> Array.ofList

let private single (category: AnnotationCategory) (name: string) (value: string) : Annotation = {
    Name = name
    Value = Some value
    Unit = None
    Accession = None
    Category = category
}

// Cells

/// The term name, the value of a unitized cell, the data name or the free text.
/// An empty cell gives None.
let private cellText (cell: CompositeCell) : string option =
    match cell with
    | CompositeCell.Term term -> term.Name |> Option.bind nonEmpty
    | CompositeCell.Unitized(value, _) -> nonEmpty value
    | CompositeCell.Data data -> data.Name |> Option.bind nonEmpty
    | CompositeCell.FreeText text -> nonEmpty text

/// Value and unit come from the cell. The accession comes from the column term,
/// which is where the crate keeps it (propertyID), so a cell term adds none.
let private termAnnotation (header: CompositeHeader) (cell: CompositeCell) (category: AnnotationCategory) : Annotation =
    let term = header.ToTerm()

    {
        Name = term.NameText
        Value = cellText cell
        Unit =
            match cell with
            | CompositeCell.Unitized(_, unit) -> unit.Name |> Option.bind nonEmpty
            | _ -> None
        Accession =
            match term.TANInfo with
            | Some _ -> Some term.TermAccessionAndOntobeeUrlIfShort
            | None -> None
        Category = category
    }

let private stepText (category: AnnotationCategory) (name: string) (cell: CompositeCell) : Annotation option =
    cellText cell |> Option.map (single category name)

// Nodes

/// Data and FreeText both give Data.
let private ioKindOf (io: IOType) : IoKind =
    match io with
    | IOType.Source -> IoKind.Source
    | IOType.Sample -> IoKind.Sample
    | IOType.Material -> IoKind.Material
    | IOType.Data
    | IOType.FreeText _ -> IoKind.Data

/// Inputs and outputs connect across the whole ARC by their name without a leading "./".
let private nodeIdOf (cell: CompositeCell) : string option =
    cellText cell
    |> Option.bind (fun text ->
        if text.Length >= 2 && text.[0] = '.' && text.[1] = '/' then
            nonEmpty (text.Substring 2)
        else
            nonEmpty text
    )

let private newAnnotationSet () : AnnotationSet = {
    Seen = HashSet<string>()
    Items = ResizeArray<Annotation>()
}

/// Merges the characteristics and factors of all rows that mention one node.
/// Annotations that agree in every field count once.
let private mergeAnnotations (target: AnnotationSet) (annotations: seq<Annotation>) : unit =
    for annotation in annotations do
        if target.Seen.Add(annotationKey annotation) then
            target.Items.Add annotation

let private nodeInfoFor (infos: Dictionary<string, NodeInfo>) (id: string) (kind: IoKind) : NodeInfo =
    match tryFind infos id with
    | Some info -> info
    | None ->
        let info: NodeInfo = {
            Kind = kind
            Characteristics = newAnnotationSet ()
            Factors = newAnnotationSet ()
        }

        infos.[id] <- info
        info

// Datasets and rows

/// Studies come first, runs come last and the assays sit between them. Each kind
/// keeps the order of the investigation.
let private readDatasets (inv: ArcInvestigation) : DatasetEntry[] =
    let entry
        (kind: DatasetKind)
        (directory: string)
        (identifier: string)
        (tables: ResizeArray<ArcTable>)
        : DatasetEntry =
        {
            Dataset = {
                Id = $"{directory}/{identifier}/"
                Kind = kind
                Identifier = identifier
            }
            Tables = tables
        }

    [|
        for study in inv.Studies -> entry DatasetKind.Study "studies" study.Identifier study.Tables
        for assay in inv.Assays -> entry DatasetKind.Assay "assays" assay.Identifier assay.Tables
        for run in inv.Runs -> entry DatasetKind.Run "runs" run.Identifier run.Tables
    |]

/// Rows without inputs and outputs are kept, so that their process exists and
/// lists them. They take no part in signatures and groups. The characteristics
/// of a row go to its input node and the factors to its output node. The other
/// columns become step annotations in the order protocol, parameters,
/// components, performer, date, comments. The protocol type, description, URI
/// and version columns give no annotation.
///
/// `ownIo` gets the characteristics and factors of this row alone, sorted, under
/// the id of the row. Signatures compare these. The annotations of a node merge
/// over every table that mentions it, so a Characteristic column in another
/// table would otherwise split the signatures of this one.
let private readRow
    (table: ArcTable)
    (rowIndex: int)
    (processKey: string)
    (datasetId: string)
    (infos: Dictionary<string, NodeInfo>)
    (ownIo: Dictionary<string, Annotation[]>)
    : ProvenanceRow =
    let rowId = $"{processKey}#{rowIndex}"
    let cells = table.GetRow(rowIndex, true)
    let mutable input: (string * IoKind) option = None
    let mutable output: (string * IoKind) option = None
    let mutable protocol: Annotation option = None
    let mutable performer: Annotation option = None
    let mutable date: Annotation option = None
    let characteristics = ResizeArray<Annotation>()
    let factors = ResizeArray<Annotation>()
    let parameters = ResizeArray<Annotation>()
    let components = ResizeArray<Annotation>()
    let comments = ResizeArray<Annotation>()

    for column in 0 .. table.Headers.Count - 1 do
        let cell = cells.[column]

        match table.Headers.[column] with
        | CompositeHeader.Input io ->
            match nodeIdOf cell with
            | Some id -> input <- Some(id, ioKindOf io)
            | None -> ()
        | CompositeHeader.Output io ->
            match nodeIdOf cell with
            | Some id -> output <- Some(id, ioKindOf io)
            | None -> ()
        | CompositeHeader.Characteristic _ as header ->
            characteristics.Add(termAnnotation header cell AnnotationCategory.Characteristic)
        | CompositeHeader.Factor _ as header -> factors.Add(termAnnotation header cell AnnotationCategory.Factor)
        | CompositeHeader.Parameter _ as header ->
            parameters.Add(termAnnotation header cell AnnotationCategory.Parameter)
        | CompositeHeader.Component _ as header ->
            components.Add(termAnnotation header cell AnnotationCategory.Component)
        | CompositeHeader.ProtocolREF -> protocol <- stepText AnnotationCategory.Protocol "Protocol" cell
        | CompositeHeader.Performer -> performer <- stepText AnnotationCategory.Performer "Performer" cell
        | CompositeHeader.Date -> date <- stepText AnnotationCategory.Date "Date" cell
        | CompositeHeader.Comment key ->
            // The only field of a Comment header is its key.
            let name = if String.IsNullOrEmpty key then "Comment" else key

            match stepText AnnotationCategory.Comment name cell with
            | Some comment -> comments.Add comment
            | None -> ()
        | _ -> ()

    match input with
    | Some(id, kind) -> mergeAnnotations (nodeInfoFor infos id kind).Characteristics characteristics
    | None -> ()

    match output with
    | Some(id, kind) -> mergeAnnotations (nodeInfoFor infos id kind).Factors factors
    | None -> ()

    ownIo.[rowId] <-
        Array.append
            (if input.IsSome then
                 sortAnnotations (characteristics.ToArray() |> Array.distinctBy annotationKey)
             else
                 [||])
            (if output.IsSome then
                 sortAnnotations (factors.ToArray() |> Array.distinctBy annotationKey)
             else
                 [||])

    let annotations = ResizeArray<Annotation>()
    Option.iter annotations.Add protocol
    annotations.AddRange(sortAnnotations (parameters.ToArray()))
    annotations.AddRange(sortAnnotations (components.ToArray()))
    Option.iter annotations.Add performer
    Option.iter annotations.Add date
    annotations.AddRange comments

    {
        Id = rowId
        Name = $"{table.Name}_{rowIndex}"
        ProcessKey = processKey
        DatasetId = datasetId
        Inputs =
            match input with
            | Some(id, _) -> [| id |]
            | None -> [||]
        Outputs =
            match output with
            | Some(id, _) -> [| id |]
            | None -> [||]
        Annotations = annotations.ToArray()
    }

/// One process per table with at least one row, in the order of the investigation.
/// The processes come back without signatures and groups.
let private readProcesses (datasets: DatasetEntry[]) : ReadResult =
    let shells = ResizeArray<ProcessShell>()
    let rows = ResizeArray<ProvenanceRow>()
    let infos = Dictionary<string, NodeInfo>()
    let ownIo = Dictionary<string, Annotation[]>()

    for entry in datasets do
        for table in entry.Tables do
            if table.RowCount > 0 then
                let key = $"{entry.Dataset.Id}::{table.Name}"
                let rowIds = ResizeArray<string>()

                for rowIndex in 0 .. table.RowCount - 1 do
                    let row = readRow table rowIndex key entry.Dataset.Id infos ownIo
                    rowIds.Add row.Id
                    rows.Add row

                shells.Add {
                    Key = key
                    Name = table.Name
                    DatasetId = entry.Dataset.Id
                    RowIds = rowIds.ToArray()
                }

    {
        Shells = shells
        Rows = rows
        Infos = infos
        OwnIo = ownIo
    }

// Signatures, groups and edges

/// "A" to "Z", then "AA", "AB", and so on.
let private letters (index: int) : string =
    let mutable label = ""
    let mutable n = index + 1

    while n > 0 do
        label <- string (char (65 + (n - 1) % 26)) + label
        n <- (n - 1) / 26

    label

/// Sorting the comments gives two rows with the same comments in a different
/// order the same signature.
let private sortedStep (annotations: Annotation[]) : Annotation[] =
    Array.append
        (annotations
         |> Array.filter (fun annotation -> annotation.Category <> AnnotationCategory.Comment))
        (annotations
         |> Array.filter (fun annotation -> annotation.Category = AnnotationCategory.Comment)
         |> sortAnnotations)

/// Annotations that share a category and a name, e.g. all values of one parameter.
let private groupByCategoryAndName (annotations: Annotation[]) : NamedAnnotations =
    let keys = ResizeArray<string>()
    let byKey = Dictionary<string, ResizeArray<Annotation>>()

    for annotation in annotations do
        let key =
            jsonStringArray [
                AnnotationCategory.toText annotation.Category
                annotation.Name
            ]

        match tryFind byKey key with
        | Some list -> list.Add annotation
        | None ->
            keys.Add key
            let list = ResizeArray<Annotation>()
            list.Add annotation
            byKey.[key] <- list

    { Keys = keys; ByKey = byKey }

/// Rows share a signature when their annotations are identical. These are the
/// step annotations plus the characteristics and factors from the cells of the
/// row itself (`ownIo`).
let private buildSignatures
    (processKey: string)
    (processRows: ProvenanceRow[])
    (ownIo: Dictionary<string, Annotation[]>)
    : ProcessSignature[] * Dictionary<string, int> =
    let builders = ResizeArray<SignatureBuilder>()
    let indexByKey = Dictionary<string, int>()
    let signatureOfRow = Dictionary<string, int>()

    for row in processRows do
        let own = tryFind ownIo row.Id |> Option.defaultValue [||]
        let annotations = Array.append (sortedStep row.Annotations) own
        let key = jsonStringArray (annotations |> Array.map annotationKey)

        let index =
            match tryFind indexByKey key with
            | Some index -> index
            | None ->
                let index = builders.Count
                indexByKey.[key] <- index

                let builder: SignatureBuilder = {
                    Id = processKey + "$" + string index
                    Annotations = annotations
                    RowIds = ResizeArray<string>()
                }

                builders.Add builder
                index

        builders.[index].RowIds.Add row.Id
        signatureOfRow.[row.Id] <- index

    // An annotation distinguishes when its category and name occur with different
    // values across the signatures.
    let grouped =
        builders
        |> Seq.map (fun builder -> groupByCategoryAndName builder.Annotations)
        |> Array.ofSeq

    let valuesOf (named: NamedAnnotations) (key: string) : string =
        match tryFind named.ByKey key with
        | Some list -> jsonStringArray (list |> Seq.map annotationKey)
        | None -> jsonStringArray []

    let differing =
        grouped
        |> Array.collect (fun named -> named.Keys.ToArray())
        |> Array.distinct
        |> Array.filter (fun key ->
            let distinctValues = HashSet<string>()

            for named in grouped do
                distinctValues.Add(valuesOf named key) |> ignore

            distinctValues.Count > 1
        )

    let signatures =
        builders
        |> Seq.mapi (fun index builder ->
            let signature: ProcessSignature = {
                Id = builder.Id
                RowIds = builder.RowIds.ToArray()
                Annotations = builder.Annotations
                Distinguishing =
                    differing
                    |> Array.collect (fun key ->
                        match tryFind grouped.[index].ByKey key with
                        | Some list -> list.ToArray()
                        | None -> [||]
                    )
            }

            signature
        )
        |> Array.ofSeq

    signatures, signatureOfRow

/// Nodes that take part in exactly the same signatures form one group, so a node
/// that feeds several signatures appears once. `nodeOrder` fixes the order of the
/// nodes and so the order of the groups.
let private buildGroups
    (nodeOrder: ResizeArray<string>)
    (signaturesOfNode: Dictionary<string, HashSet<int>>)
    (processKey: string)
    (side: IoSide)
    (signatures: ProcessSignature[])
    : IoGroup[] =
    let builders = ResizeArray<GroupBuilder>()
    let bySignatures = Dictionary<string, GroupBuilder>()
    let isInputs = (side = IoSide.Inputs)

    for nodeId in nodeOrder do
        let sorted = signaturesOfNode.[nodeId] |> Array.ofSeq |> Array.sort
        let key = sorted |> Array.map string |> String.concat ","

        let group =
            match tryFind bySignatures key with
            | Some group -> group
            | None ->
                let group: GroupBuilder = {
                    Id = processKey + "#" + (if isInputs then "in" else "out") + string builders.Count
                    Side = side
                    Label = (if isInputs then "Inputs" else "Outputs") + " " + letters builders.Count
                    NodeIds = ResizeArray<string>()
                    SignatureIds = sorted |> Array.map (fun index -> signatures.[index].Id)
                }

                bySignatures.[key] <- group
                builders.Add group
                group

        group.NodeIds.Add nodeId

    builders
    |> Seq.map (fun builder ->
        let group: IoGroup = {
            Id = builder.Id
            Side = builder.Side
            Label = builder.Label
            NodeIds = builder.NodeIds.ToArray()
            SignatureIds = builder.SignatureIds
        }

        group
    )
    |> Array.ofSeq

let private fillProcess
    (shell: ProcessShell)
    (allRows: ProvenanceRow[])
    (ownIo: Dictionary<string, Annotation[]>)
    : ProvenanceProcess =
    let processRows =
        allRows
        |> Array.filter (fun row -> row.Inputs.Length > 0 || row.Outputs.Length > 0)

    let signatures, signatureOfRow = buildSignatures shell.Key processRows ownIo

    // The first-seen order of the nodes fixes the order of the groups.
    let inputOrder = ResizeArray<string>()
    let outputOrder = ResizeArray<string>()
    let signaturesOfInput = Dictionary<string, HashSet<int>>()
    let signaturesOfOutput = Dictionary<string, HashSet<int>>()

    let record
        (order: ResizeArray<string>)
        (target: Dictionary<string, HashSet<int>>)
        (id: string)
        (signatureIndex: int)
        =
        match tryFind target id with
        | Some indexes -> indexes.Add signatureIndex |> ignore
        | None ->
            order.Add id
            let indexes = HashSet<int>()
            indexes.Add signatureIndex |> ignore
            target.[id] <- indexes

    for row in processRows do
        let signatureIndex = signatureOfRow.[row.Id]

        for id in row.Inputs do
            record inputOrder signaturesOfInput id signatureIndex

        for id in row.Outputs do
            record outputOrder signaturesOfOutput id signatureIndex

    let inputGroups =
        buildGroups inputOrder signaturesOfInput shell.Key IoSide.Inputs signatures

    let outputGroups =
        buildGroups outputOrder signaturesOfOutput shell.Key IoSide.Outputs signatures

    let inputGroupOf = Dictionary<string, string>()
    let outputGroupOf = Dictionary<string, string>()

    for group in inputGroups do
        for id in group.NodeIds do
            inputGroupOf.[id] <- group.Id

    for group in outputGroups do
        for id in group.NodeIds do
            outputGroupOf.[id] <- group.Id

    // An edge joins an input group to an output group for one signature and lists
    // the rows behind it.
    let edgeByKey = Dictionary<string, EdgeBuilder>()
    let edgeBuilders = ResizeArray<EdgeBuilder>()

    for row in processRows do
        let signature = signatures.[signatureOfRow.[row.Id]]

        for fromGroup in (row.Inputs |> Array.map (fun id -> inputGroupOf.[id]) |> Array.distinct) do
            for toGroup in (row.Outputs |> Array.map (fun id -> outputGroupOf.[id]) |> Array.distinct) do
                let key = jsonStringArray [ fromGroup; toGroup; signature.Id ]

                let edge =
                    match tryFind edgeByKey key with
                    | Some edge -> edge
                    | None ->
                        let edge: EdgeBuilder = {
                            Id = shell.Key + "@" + string edgeBuilders.Count
                            From = fromGroup
                            To = toGroup
                            SignatureId = signature.Id
                            RowIds = ResizeArray<string>()
                        }

                        edgeByKey.[key] <- edge
                        edgeBuilders.Add edge
                        edge

                edge.RowIds.Add row.Id

    let groupEdges =
        edgeBuilders
        |> Seq.map (fun builder ->
            let edge: GroupEdge = {
                Id = builder.Id
                From = builder.From
                To = builder.To
                SignatureId = builder.SignatureId
                RowIds = builder.RowIds.ToArray()
            }

            edge
        )
        |> Array.ofSeq

    {
        Key = shell.Key
        Name = shell.Name
        DatasetId = shell.DatasetId
        RowIds = shell.RowIds
        Signatures = signatures
        InputGroups = inputGroups
        OutputGroups = outputGroups
        GroupEdges = groupEdges
    }

let private readNodes (rows: seq<ProvenanceRow>) (infos: Dictionary<string, NodeInfo>) : ProvenanceNode[] =
    let nodeById = Dictionary<string, NodeBuilder>()
    let ordered = ResizeArray<NodeBuilder>()

    let ensure (id: string) : NodeBuilder =
        match tryFind nodeById id with
        | Some node -> node
        | None ->
            let node: NodeBuilder =
                match tryFind infos id with
                | Some info -> {
                    Id = id
                    Kind = info.Kind
                    Annotations = Array.append (info.Characteristics.Items.ToArray()) (info.Factors.Items.ToArray())
                    DatasetIds = ResizeArray<string>()
                    ProcessKeys = ResizeArray<string>()
                  }
                | None -> {
                    Id = id
                    Kind = IoKind.Data
                    Annotations = [||]
                    DatasetIds = ResizeArray<string>()
                    ProcessKeys = ResizeArray<string>()
                  }

            nodeById.[id] <- node
            ordered.Add node
            node

    let pushUnique (target: ResizeArray<string>) (value: string) =
        if not (target.Contains value) then
            target.Add value

    for row in rows do
        for id in Array.append row.Inputs row.Outputs do
            let node = ensure id
            pushUnique node.DatasetIds row.DatasetId
            pushUnique node.ProcessKeys row.ProcessKey

    ordered
    |> Seq.map (fun builder ->
        let node: ProvenanceNode = {
            Id = builder.Id
            Kind = builder.Kind
            Annotations = builder.Annotations
            DatasetIds = builder.DatasetIds.ToArray()
            ProcessKeys = builder.ProcessKeys.ToArray()
        }

        node
    )
    |> Array.ofSeq

module ProvenanceGraphData =

    /// Builds the provenance data of one investigation. The result is plain data
    /// and holds no ARCtrl objects.
    let ofInvestigation (inv: ArcInvestigation) : ProvenanceGraphData =
        let datasets = readDatasets inv
        let read = readProcesses datasets
        let nodes = readNodes read.Rows read.Infos

        let rowsByProcess = Dictionary<string, ResizeArray<ProvenanceRow>>()

        for row in read.Rows do
            match tryFind rowsByProcess row.ProcessKey with
            | Some list -> list.Add row
            | None ->
                let list = ResizeArray<ProvenanceRow>()
                list.Add row
                rowsByProcess.[row.ProcessKey] <- list

        let processes =
            read.Shells
            |> Seq.map (fun shell ->
                let rows =
                    match tryFind rowsByProcess shell.Key with
                    | Some list -> list.ToArray()
                    | None -> [||]

                fillProcess shell rows read.OwnIo
            )
            |> Array.ofSeq

        {
            Datasets = datasets |> Array.map (fun entry -> entry.Dataset)
            Processes = processes
            Rows = read.Rows.ToArray()
            Nodes = nodes
        }

module ProvenanceIndex =

    let private dictionaryOf (items: 'T[]) (keyOf: 'T -> string) : Dictionary<string, 'T> =
        let result = Dictionary<string, 'T>()

        for item in items do
            result.[keyOf item] <- item

        result

    /// Node id -> ids of the rows that list it on the given side, in row order.
    let private indexRowsByNode
        (rows: ProvenanceRow[])
        (idsOf: ProvenanceRow -> string[])
        : Dictionary<string, string[]> =
        let byNode = Dictionary<string, ResizeArray<string>>()

        for row in rows do
            for id in idsOf row do
                match tryFind byNode id with
                | Some list -> list.Add row.Id
                | None ->
                    let list = ResizeArray<string>()
                    list.Add row.Id
                    byNode.[id] <- list

        let result = Dictionary<string, string[]>()

        for pair in byNode do
            result.[pair.Key] <- pair.Value.ToArray()

        result

    /// Adds the string-keyed lookups the queries need.
    let ofData (data: ProvenanceGraphData) : ProvenanceIndex =
        let groups =
            data.Processes
            |> Array.collect (fun p -> Array.append p.InputGroups p.OutputGroups)

        {
            Datasets = data.Datasets
            Processes = data.Processes
            Rows = data.Rows
            Nodes = data.Nodes
            DatasetById = dictionaryOf data.Datasets (fun d -> d.Id)
            ProcessByKey = dictionaryOf data.Processes (fun p -> p.Key)
            RowById = dictionaryOf data.Rows (fun r -> r.Id)
            NodeById = dictionaryOf data.Nodes (fun n -> n.Id)
            GroupById = dictionaryOf groups (fun g -> g.Id)
            ProducedBy = indexRowsByNode data.Rows (fun r -> r.Outputs)
            ConsumedBy = indexRowsByNode data.Rows (fun r -> r.Inputs)
        }
