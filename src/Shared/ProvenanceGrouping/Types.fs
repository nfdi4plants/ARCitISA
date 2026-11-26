module Arcitisa.Components.Shared.ProvenanceGrouping.Types

/// Name of a study, assay, or run table as known by the caller.
type ProvenanceTableName = string

type ProvenanceSourceId = string
type ProvenanceSourceName = string

type ProvenanceSourceRef = {
    Id: ProvenanceSourceId
    Name: ProvenanceSourceName
}

/// Optional process name from the source model.
/// This is display/logical metadata, not a unique process ID.
type ProvenanceProcessName = string

/// Optional stable source-model process identity for disambiguation/writeback.
/// Adapters should keep generated row or object IDs here instead of appending them to `ProvenanceProcessName`.
type ProvenanceProcessId = string

/// ARCitISA-local stable ID for one loaded input or output endpoint.
/// The actual user-facing input/output name lives on `ProvenanceSet.Name`.
type ProvenanceSetId = string

/// ARCitISA-local stable ID for one loaded input-to-output connection.
type ProvenanceConnectionId = string

/// ARCitISA-local stable ID for one normalized editable property value in the model.
/// Adapters may collapse exact duplicate source occurrences and track multiplicity in source-specific sidecars.
type ProvenancePropertyValueId = string

/// Which collection or display side a projection/helper should use.
/// This is not stored on `ProvenanceSet`; loaded role is implied by `InputSets`/`OutputSets`.
[<RequireQualifiedAccess>]
type ProvenanceSide =
    /// Select loaded input endpoints or render an input-side display group.
    | Input
    /// Select loaded output endpoints or render an output-side display group.
    | Output

/// Source-provided endpoint or property role.
/// Adapters choose stable IDs for their own source model, for example ARC/ISA or another process format.
type ProvenanceKind = {
    /// Stable role identifier owned by the adapter that created the model.
    Id: string
    /// Human-readable label for display and fallback header creation.
    Label: string
}

module ProvenanceKind =

    let create id label : ProvenanceKind = { Id = id; Label = label }

    let displayName (kind: ProvenanceKind) =
        if System.String.IsNullOrWhiteSpace kind.Label then
            kind.Id
        else
            kind.Label

/// Header metadata for a loaded input/output endpoint.
type ProvenanceIOHeader = {
    /// Adapter-provided endpoint role.
    Kind: ProvenanceKind
    /// Original or display-ready header text, such as `Input [Sample Name]`.
    Text: string
}

/// Small ontology term projection used for property categories, units, and term values.
type ProvenanceTerm = {
    /// Human-readable term name.
    Name: string
    /// Optional ontology source name/reference.
    TermSource: string option
    /// Optional ontology accession/IRI.
    TermAccession: string option
}

/// Value projection that is simple to serialize and use from Fable.
[<RequireQualifiedAccess>]
type ProvenanceValue =
    /// Plain string or free text value.
    | Text of string
    /// Integer value.
    | Integer of int
    /// Floating point value.
    | Float of float
    /// Ontology-backed term value.
    | Term of ProvenanceTerm

/// Property key used for grouping, editing, and writeback.
type ProvenancePropertyHeader = {
    /// Adapter-provided property role.
    Kind: ProvenanceKind
    /// Category term, such as Species, Temperature, or Replicate.
    Category: ProvenanceTerm
}

/// Identity of one logical property throughout the provenance editor.
/// The overall source is the origin; process and occurrence metadata are not.
type ProvenancePropertyKey = {
    Header: ProvenancePropertyHeader
    OriginSource: ProvenanceSourceRef
}

/// Source metadata needed to update an existing property value in its source model.
/// This is a writeback anchor, not a graph edge and not an ARCtrl object reference.
type ProvenanceWritebackAnchor = {
    /// Source containing the property value occurrence.
    Source: ProvenanceSourceRef
    /// Optional source process ID when the adapter needs a unique process identity.
    ProcessId: ProvenanceProcessId option
    /// Optional source process name when the adapter can provide a logical/display name.
    ProcessName: ProvenanceProcessName option
    /// Property header to locate the source column/value family.
    Header: ProvenancePropertyHeader
    /// Source input names that identify the owning context.
    InputNames: string list
    /// Source output names that identify the owning context.
    OutputNames: string list
}

[<RequireQualifiedAccess>]
type ProvenancePropertyOrigin =
    | Real of ProvenanceWritebackAnchor
    | Virtual of ProvenanceWritebackAnchor

module ProvenancePropertyOrigin =

    let anchor origin =
        match origin with
        | ProvenancePropertyOrigin.Real anchor
        | ProvenancePropertyOrigin.Virtual anchor -> anchor

    let source origin = (anchor origin).Source

/// One concrete editable key/value in the normalized model.
/// Distinct values must remain distinct; exact duplicate source occurrences may collapse when they are not meaningfully distinguishable in the source-agnostic core model.
type ProvenancePropertyValue = {
    /// Stable ARCitISA-local ID for this occurrence.
    Id: ProvenancePropertyValueId
    /// Property key/category for grouping and writeback.
    Header: ProvenancePropertyHeader
    /// Stored property value.
    Value: ProvenanceValue
    /// Optional unit term.
    Unit: ProvenanceTerm option
    /// Required origin and writeback anchor metadata.
    Origin: ProvenancePropertyOrigin
}

module ProvenancePropertyValue =

    let propertyKey (propertyValue: ProvenancePropertyValue) : ProvenancePropertyKey = {
        Header = propertyValue.Header
        OriginSource = ProvenancePropertyOrigin.source propertyValue.Origin
    }

    let belongsTo (key: ProvenancePropertyKey) (propertyValue: ProvenancePropertyValue) =
        propertyValue.Header = key.Header
        && (ProvenancePropertyOrigin.source propertyValue.Origin).Id = key.OriginSource.Id

/// One actual loaded input or output endpoint.
/// This is the first-class UI item before grouping; it is not a collapsed graph node.
type ProvenanceSet = {
    /// ARCitISA-local stable endpoint ID used by connections and display groups.
    Id: ProvenanceSetId
    /// Source this endpoint belongs to.
    Source: ProvenanceSourceRef
    /// Loaded input/output header this endpoint came from.
    Header: ProvenanceIOHeader
    /// Actual loaded input/output name from the table cell or source adapter.
    Name: string
    /// Property value occurrences attached to this loaded endpoint.
    PropertyValueIds: ProvenancePropertyValueId list
    /// Property value occurrences inherited through loaded connections, keyed by connection ID.
    /// These pointers are connection-specific so removing a connection can remove the inherited values.
    InheritedPropertyValueIds: Map<ProvenanceConnectionId, ProvenancePropertyValueId list>
}

module ProvenanceSet =

    let private distinct values = values |> List.distinct

    let inheritedPropertyValueIds (set: ProvenanceSet) =
        set.InheritedPropertyValueIds
        |> Map.toList
        |> List.sortBy fst
        |> List.collect snd
        |> distinct

    let effectivePropertyValueIds (set: ProvenanceSet) =
        [
            yield! set.PropertyValueIds
            yield! inheritedPropertyValueIds set
        ]
        |> distinct

    let inheritPropertyValueIds connectionId propertyValueIds (set: ProvenanceSet) =
        let propertyValueIds = propertyValueIds |> distinct

        let inherited =
            if propertyValueIds.IsEmpty then
                set.InheritedPropertyValueIds |> Map.remove connectionId
            else
                set.InheritedPropertyValueIds |> Map.add connectionId propertyValueIds

        {
            set with
                InheritedPropertyValueIds = inherited
        }

    let removeInheritedPropertyValueIds connectionId (set: ProvenanceSet) = {
        set with
            InheritedPropertyValueIds = set.InheritedPropertyValueIds |> Map.remove connectionId
    }

/// One exact connection between a loaded input endpoint and a loaded output endpoint.
/// Previous-table connections are intentionally not represented here.
type ProvenanceConnection = {
    /// ARCitISA-local stable connection ID.
    Id: ProvenanceConnectionId
    /// Source containing this editable connection.
    Source: ProvenanceSourceRef
    /// Optional process ID associated with this connection.
    ProcessId: ProvenanceProcessId option
    /// Optional process name associated with this connection.
    ProcessName: ProvenanceProcessName option
    /// Loaded input endpoint ID.
    InputSetId: ProvenanceSetId
    /// Loaded output endpoint ID.
    OutputSetId: ProvenanceSetId
}

/// Complete provenance projection for one loaded table session.
/// The model stores loaded endpoints, their property value pointers, and loaded connections.
type ProvenanceModel = {
    /// The source currently opened and editable in the viewer.
    Source: ProvenanceSourceRef
    /// Shared property value occurrence store.
    PropertyValues: Map<ProvenancePropertyValueId, ProvenancePropertyValue>
    /// First-class loaded input endpoints, keyed by `ProvenanceSet.Id`.
    /// May be empty when the loaded table currently has only outputs.
    InputSets: Map<ProvenanceSetId, ProvenanceSet>
    /// First-class loaded output endpoints, keyed by `ProvenanceSet.Id`.
    /// May be empty when the loaded table currently has only inputs.
    OutputSets: Map<ProvenanceSetId, ProvenanceSet>
    /// Editable loaded-table connections.
    /// May be empty for one-sided loaded tables.
    Connections: Map<ProvenanceConnectionId, ProvenanceConnection>
}

module ProvenanceModel =

    /// Recomputes same-layer property inheritance in both directions: a value
    /// on any endpoint spreads through this model's connections to every
    /// transitively connected endpoint, regardless of side. Entries stay keyed
    /// by connection ID so removing a connection retracts them; entries keyed
    /// by connections of other models (carried in from upstream layers) are
    /// preserved untouched. A set never inherits its own values back.
    let refreshInheritedProperties (model: ProvenanceModel) =
        let localConnections =
            model.Connections
            |> Map.toList
            |> List.filter (fun (_, connection) ->
                connection.Source.Id = model.Source.Id
                && model.InputSets.ContainsKey connection.InputSetId
                && model.OutputSets.ContainsKey connection.OutputSetId
            )

        // Entries keyed by connections this model does not own were carried in
        // from an upstream layer; they seed inheritance but are never rebuilt.
        let carriedEntries (set: ProvenanceSet) =
            set.InheritedPropertyValueIds
            |> Map.filter (fun connectionId _ -> not (model.Connections.ContainsKey connectionId))

        let seedIds (set: ProvenanceSet) =
            [
                yield! set.PropertyValueIds
                yield! carriedEntries set |> Map.toList |> List.sortBy fst |> List.collect snd
            ]
            |> List.distinct

        let inputSeeds = model.InputSets |> Map.map (fun _ set -> seedIds set)
        let outputSeeds = model.OutputSets |> Map.map (fun _ set -> seedIds set)

        let entriesExcept
            connectionId
            setId
            (entries: Map<ProvenanceSetId, Map<ProvenanceConnectionId, ProvenancePropertyValueId list>>)
            =
            entries
            |> Map.tryFind setId
            |> Option.defaultValue Map.empty
            |> Map.toList
            |> List.filter (fun (entryConnectionId, _) -> entryConnectionId <> connectionId)
            |> List.sortBy fst
            |> List.collect snd

        let setEntry connectionId setId propertyValueIds entries =
            if List.isEmpty propertyValueIds then
                entries
            else
                let current = entries |> Map.tryFind setId |> Option.defaultValue Map.empty
                entries |> Map.add setId (current |> Map.add connectionId propertyValueIds)

        // One hop of inheritance per pass, repeated to a fixpoint so values
        // spread through chains (output -> shared input -> sibling output).
        // Excluding the entry that arrived through the connection itself keeps
        // a value from echoing straight back to its origin.
        let step (inputEntries, outputEntries) =
            localConnections
            |> List.fold
                (fun (inputEntries, outputEntries) (connectionId, connection) ->
                    let inputSeed = inputSeeds.[connection.InputSetId]
                    let outputSeed = outputSeeds.[connection.OutputSetId]

                    let toOutput =
                        [
                            yield! inputSeed
                            yield! entriesExcept connectionId connection.InputSetId inputEntries
                        ]
                        |> List.distinct
                        |> List.filter (fun id -> not (List.contains id outputSeed))

                    let toInput =
                        [
                            yield! outputSeed
                            yield! entriesExcept connectionId connection.OutputSetId outputEntries
                        ]
                        |> List.distinct
                        |> List.filter (fun id -> not (List.contains id inputSeed))

                    setEntry connectionId connection.InputSetId toInput inputEntries,
                    setEntry connectionId connection.OutputSetId toOutput outputEntries
                )
                (inputEntries, outputEntries)

        let rec fixpoint state =
            let next = step state

            if next = state then state else fixpoint next

        let inputEntries, outputEntries = fixpoint (Map.empty, Map.empty)

        let withRefreshedEntries localEntries (set: ProvenanceSet) = {
            set with
                InheritedPropertyValueIds =
                    localEntries
                    |> Map.tryFind set.Id
                    |> Option.defaultValue Map.empty
                    |> Map.fold
                        (fun state connectionId propertyValueIds -> state |> Map.add connectionId propertyValueIds)
                        (carriedEntries set)
        }

        {
            model with
                InputSets = model.InputSets |> Map.map (fun _ set -> withRefreshedEntries inputEntries set)
                OutputSets =
                    model.OutputSets
                    |> Map.map (fun _ set -> withRefreshedEntries outputEntries set)
        }
