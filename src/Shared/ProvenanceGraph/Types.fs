/// Plain data describing the provenance graph of one ARC. It is built from the
/// parsed ARC and never holds ARCtrl objects.
///
/// The code runs on .NET and as Fable JavaScript in the Electron main and renderer
/// processes. The renderer gets the data through structuredClone, which drops all
/// prototypes. Every record therefore forbids F# equality and comparison, and every
/// kind is a StringEnum whose JavaScript value is the literal in `toText`.
/// Convert a kind to text with `toText` only. The built-in conversions (`string`,
/// `ToString()`, `%A` and string interpolation) differ per runtime: .NET prints
/// "Study" and Fable prints "study".
module Arcitisa.Components.Shared.ProvenanceGraph.Types

open System.Collections.Generic
open System.Text
open Fable.Core

[<StringEnum; RequireQualifiedAccess>]
type DatasetKind =
    | [<CompiledName "study">] Study
    | [<CompiledName "assay">] Assay
    | [<CompiledName "run">] Run

module DatasetKind =

    let toText (kind: DatasetKind) =
        match kind with
        | DatasetKind.Study -> "study"
        | DatasetKind.Assay -> "assay"
        | DatasetKind.Run -> "run"

[<StringEnum; RequireQualifiedAccess>]
type IoKind =
    | [<CompiledName "source">] Source
    | [<CompiledName "sample">] Sample
    | [<CompiledName "material">] Material
    | [<CompiledName "data">] Data

module IoKind =

    let toText (kind: IoKind) =
        match kind with
        | IoKind.Source -> "source"
        | IoKind.Sample -> "sample"
        | IoKind.Material -> "material"
        | IoKind.Data -> "data"

[<StringEnum; RequireQualifiedAccess>]
type AnnotationCategory =
    | [<CompiledName "characteristic">] Characteristic
    | [<CompiledName "factor">] Factor
    | [<CompiledName "parameter">] Parameter
    | [<CompiledName "component">] Component
    | [<CompiledName "protocol">] Protocol
    | [<CompiledName "performer">] Performer
    | [<CompiledName "date">] Date
    | [<CompiledName "comment">] Comment

module AnnotationCategory =

    let toText (category: AnnotationCategory) =
        match category with
        | AnnotationCategory.Characteristic -> "characteristic"
        | AnnotationCategory.Factor -> "factor"
        | AnnotationCategory.Parameter -> "parameter"
        | AnnotationCategory.Component -> "component"
        | AnnotationCategory.Protocol -> "protocol"
        | AnnotationCategory.Performer -> "performer"
        | AnnotationCategory.Date -> "date"
        | AnnotationCategory.Comment -> "comment"

[<StringEnum; RequireQualifiedAccess>]
type IoSide =
    | [<CompiledName "inputs">] Inputs
    | [<CompiledName "outputs">] Outputs

module IoSide =

    let toText (side: IoSide) =
        match side with
        | IoSide.Inputs -> "inputs"
        | IoSide.Outputs -> "outputs"

[<NoEquality; NoComparison>]
type Annotation = {
    Name: string
    Value: string option
    Unit: string option
    Accession: string option
    Category: AnnotationCategory
}

[<NoEquality; NoComparison>]
type ProvenanceDataset = {
    Id: string
    Kind: DatasetKind
    Identifier: string
}

/// One input or output. Inputs and outputs connect across the whole ARC by
/// their name (a leading "./" is removed), so `Id` is that normalized name.
[<NoEquality; NoComparison>]
type ProvenanceNode = {
    Id: string
    Kind: IoKind
    /// Characteristics and factors of the node, merged over all its occurrences.
    Annotations: Annotation[]
    DatasetIds: string[]
    ProcessKeys: string[]
}

/// One row of an annotation table.
[<NoEquality; NoComparison>]
type ProvenanceRow = {
    Id: string
    Name: string
    ProcessKey: string
    DatasetId: string
    Inputs: string[]
    Outputs: string[]
    /// Annotations of the step itself: protocol, parameters, components,
    /// performer, date and comments.
    Annotations: Annotation[]
}

/// Rows of one process with identical annotations on their own step
/// (step annotations plus input characteristics and output factors).
[<NoEquality; NoComparison>]
type ProcessSignature = {
    Id: string
    RowIds: string[]
    Annotations: Annotation[]
    /// Annotations whose values differ between the signatures of the process.
    Distinguishing: Annotation[]
}

/// Inputs (or outputs) of one process that take part in exactly the same set
/// of signatures.
[<NoEquality; NoComparison>]
type IoGroup = {
    Id: string
    Side: IoSide
    Label: string
    NodeIds: string[]
    SignatureIds: string[]
}

[<NoEquality; NoComparison>]
type GroupEdge = {
    Id: string
    From: string
    To: string
    SignatureId: string
    RowIds: string[]
}

/// One annotation table of a dataset.
[<NoEquality; NoComparison>]
type ProvenanceProcess = {
    Key: string
    Name: string
    DatasetId: string
    RowIds: string[]
    Signatures: ProcessSignature[]
    InputGroups: IoGroup[]
    OutputGroups: IoGroup[]
    GroupEdges: GroupEdge[]
}

/// The payload that travels from the Electron main process to the renderer.
[<NoEquality; NoComparison>]
type ProvenanceGraphData = {
    Datasets: ProvenanceDataset[]
    Processes: ProvenanceProcess[]
    Rows: ProvenanceRow[]
    Nodes: ProvenanceNode[]
}

/// The data plus string-keyed lookups. The lookups are never enumerated for
/// output order, the arrays keep that order.
[<NoEquality; NoComparison>]
type ProvenanceIndex = {
    Datasets: ProvenanceDataset[]
    Processes: ProvenanceProcess[]
    Rows: ProvenanceRow[]
    Nodes: ProvenanceNode[]
    DatasetById: Dictionary<string, ProvenanceDataset>
    ProcessByKey: Dictionary<string, ProvenanceProcess>
    RowById: Dictionary<string, ProvenanceRow>
    NodeById: Dictionary<string, ProvenanceNode>
    GroupById: Dictionary<string, IoGroup>
    /// node id -> ids of the rows that produce it
    ProducedBy: Dictionary<string, string[]>
    /// node id -> ids of the rows that consume it
    ConsumedBy: Dictionary<string, string[]>
}

// Results of the query functions, one per detail level. They hold plain data
// only (no Dictionary or HashSet).

[<NoEquality; NoComparison>]
type DatasetGraphNode = {
    Id: string
    Kind: DatasetKind
    Label: string
}

[<NoEquality; NoComparison>]
type SharedNodeEdge = {
    Id: string
    From: string
    To: string
    SharedNodeIds: string[]
}

[<NoEquality; NoComparison>]
type DatasetGraph = {
    Nodes: DatasetGraphNode[]
    Edges: SharedNodeEdge[]
}

[<NoEquality; NoComparison>]
type ProcessGraphNode = {
    Key: string
    Label: string
    DatasetId: string
    DatasetKind: DatasetKind
    DatasetLabel: string
    External: bool
    RowCount: int
}

[<NoEquality; NoComparison>]
type ProcessGraph = {
    Dataset: ProvenanceDataset
    Nodes: ProcessGraphNode[]
    Edges: SharedNodeEdge[]
}

[<NoEquality; NoComparison>]
type GroupGraphNode = {
    Id: string
    Side: IoSide
    Label: string
    /// Member counts per kind in the fixed order source, sample, material, data.
    /// Kinds without members are left out.
    KindCounts: (IoKind * int)[]
}

/// `Differing` are the distinguishing annotations of the signature, `Shared` the rest.
[<NoEquality; NoComparison>]
type GroupGraphEdge = {
    Id: string
    From: string
    To: string
    Differing: Annotation[]
    Shared: Annotation[]
}

[<NoEquality; NoComparison>]
type GroupGraph = {
    Process: ProvenanceProcess
    DatasetKind: DatasetKind
    Nodes: GroupGraphNode[]
    Edges: GroupGraphEdge[]
}

[<NoEquality; NoComparison>]
type DetailNode = {
    Id: string
    Kind: IoKind
    Annotations: Annotation[]
    /// True for members of the selected group (process detail) or the selected node (lineage).
    Emphasized: bool
}

/// One row as an edge. `Differing` holds the step annotations that vary
/// between the edges of the same process in this graph, `Shared` the rest.
[<NoEquality; NoComparison>]
type DetailEdge = {
    Id: string
    From: string
    To: string
    ProcessKey: string
    ProcessName: string
    DatasetKind: DatasetKind
    Differing: Annotation[]
    Shared: Annotation[]
}

[<NoEquality; NoComparison>]
type ProcessDetailGraph = {
    Process: ProvenanceProcess
    Group: IoGroup
    Nodes: DetailNode[]
    Edges: DetailEdge[]
}

/// All paths through one node: everything upstream plus everything downstream.
[<NoEquality; NoComparison>]
type LineageGraph = {
    NodeId: string
    Nodes: DetailNode[]
    Edges: DetailEdge[]
}

/// Result of `splitSharedAnnotations` for one list.
[<NoEquality; NoComparison>]
type AnnotationSplit = {
    Differing: Annotation[]
    Shared: Annotation[]
}

/// The value stored under a key, or None.
let internal tryFind (lookup: Dictionary<string, 'T>) (key: string) : 'T option =
    match lookup.TryGetValue key with
    | true, value -> Some value
    | _ -> None

// Keys

/// The text in quotes, with the quote and the backslash escaped, so the encoding
/// stays injective.
let private jsonString (text: string) : string =
    let builder = StringBuilder()
    builder.Append('"') |> ignore

    for c in text do
        match c with
        | '"' -> builder.Append("\\\"") |> ignore
        | '\\' -> builder.Append("\\\\") |> ignore
        | c -> builder.Append(c) |> ignore

    builder.Append('"') |> ignore
    builder.ToString()

/// The strings, each quoted by `jsonString`, in square brackets separated by
/// commas. Compound keys use it, so two different lists never give the same key.
let jsonStringArray (items: seq<string>) : string =
    "[" + (items |> Seq.map jsonString |> String.concat ",") + "]"

let private jsonOption (text: string option) : string =
    match text with
    | Some text -> jsonString text
    | None -> "null"

/// Two annotations are the same when all five fields match. The key lists the
/// five fields like `jsonStringArray` does. `None` is a bare null and
/// `Some "null"` is the quoted string, so the two keys differ.
let annotationKey (annotation: Annotation) : string =
    "["
    + jsonString annotation.Name
    + ","
    + jsonOption annotation.Value
    + ","
    + jsonOption annotation.Unit
    + ","
    + jsonOption annotation.Accession
    + ","
    + jsonString (AnnotationCategory.toText annotation.Category)
    + "]"
