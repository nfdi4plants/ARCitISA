module Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter

open System
open ARCtrl
open ARCtrl.Process
open ARCtrl.Process.ColumnIndex
open ARCtrl.Process.Conversion
open Swate.Components.Shared.ProvenanceGrouping.Types

/// ARC table container that owns the selected or source table.
/// The converter uses this with `ParentIdentifier` and `TableName` because table names alone are not unique across a full ARC.
[<RequireQualifiedAccess>]
type ArcTableScope =
    /// Table belongs to `ARC.Studies`.
    | Study
    /// Table belongs to `ARC.Assays`.
    | Assay
    /// Table belongs to `ARC.Runs`.
    | Run

/// Stable, serializable location of an ARCtrl table inside a loaded ARC.
/// This is the adapter-side replacement for holding an ARCtrl object pointer.
type ArcTableLocation = {
    /// Whether the table is inside a study, assay, or run collection.
    Scope: ArcTableScope
    /// Identifier of the owning study, assay, or run.
    ParentIdentifier: string
    /// `ArcTable.Name` of the source table.
    TableName: string
}

/// Options for converting one loaded table from a loaded `ARC`.
type ArcProvenanceConverterOptions = {
    /// The study, assay, or run table currently loaded into the provenance editor.
    LoadedTable: ArcTableLocation
    /// When true, previous tables that point into loaded inputs are collapsed into attached property values.
    IncludePreviousContext: bool
}

/// ARCtrl location of a loaded endpoint.
/// The endpoint role is resolved by checking whether the ID lives in `model.InputSets` or `model.OutputSets`.
type ArcEndpointLocation = {
    /// Table containing the endpoint cell.
    Table: ArcTableLocation
    /// Converted input/output header.
    Header: ProvenanceIOHeader
    /// Actual input/output name from the loaded table cell.
    Name: string
}

/// ARCtrl location of a property value occurrence for later writeback.
/// This mirrors `ProvenanceWritebackAnchor`, adds full ARC table location, and preserves column identity when ARCtrl provides it.
type ArcWritebackLocation = {
    /// Study, assay, or run table containing this property value.
    Table: ArcTableLocation
    /// Source-model process identity when ARCtrl exposes one.
    ProcessId: ProvenanceProcessId option
    /// Logical process name derived from ARCtrl table semantics.
    ProcessName: ProvenanceProcessName option
    /// Converted property header.
    Header: ProvenancePropertyHeader
    /// Input names that identify the row/process context.
    InputNames: string list
    /// Output names that identify the row/process context.
    OutputNames: string list
    /// Adapter-side column identity when ARCtrl preserves it.
    ColumnIndex: int option
}

/// ARCtrl location of a loaded input-to-output connection.
/// Connections exist only for the selected loaded table, not for collapsed previous context.
type ArcConnectionLocation = {
    /// Loaded table containing this input/output connection.
    Table: ArcTableLocation
    /// Source-model process identity when ARCtrl exposes one.
    ProcessId: ProvenanceProcessId option
    /// Logical process name derived from the row that produced this connection.
    ProcessName: ProvenanceProcessName option
    /// Loaded input set ID in the core model.
    InputSetId: ProvenanceSetId
    /// Loaded output set ID in the core model.
    OutputSetId: ProvenanceSetId
    /// Actual loaded input name.
    InputName: string
    /// Actual loaded output name.
    OutputName: string
}

/// ARCtrl-specific lookup data returned beside the source-agnostic core model.
/// It contains enough information for a later writeback adapter to find source tables and contexts again.
type ArcProvenanceIndex = {
    /// Location of the selected loaded table.
    LoadedTable: ArcTableLocation
    /// Locations of first-class loaded inputs and outputs.
    EndpointLocations: Map<ProvenanceSetId, ArcEndpointLocation>
    /// Locations of loaded and collapsed property value occurrences.
    PropertyValueLocations: Map<ProvenancePropertyValueId, ArcWritebackLocation>
    /// Locations of loaded-table input/output connections.
    ConnectionLocations: Map<ProvenanceConnectionId, ArcConnectionLocation>
}

/// Successful conversion output.
type ArcProvenanceConversionResult = {
    /// Source-agnostic provenance model consumed by grouping, editing, and display modules.
    Model: ProvenanceModel
    /// ARCtrl-specific lookup data for later writeback.
    Index: ArcProvenanceIndex
    /// Non-fatal conversion notes, such as skipped empty endpoint cells.
    Warnings: string list
}

type internal TableRef = {
    Location: ArcTableLocation
    Table: ArcTable
}

type internal ProcessPairContext = {
    Location: ArcTableLocation
    ProcessId: ProvenanceProcessId option
    ProcessName: ProvenanceProcessName option
    InputName: string option
    OutputName: string option
}

type internal CandidatePropertyValue = {
    Header: ProvenancePropertyHeader
    Value: ProvenanceValue
    Unit: ProvenanceTerm option
    Origin: ProvenancePropertyOrigin
    TargetInputSetIds: ProvenanceSetId list
    TargetOutputSetIds: ProvenanceSetId list
    WritebackLocation: ArcWritebackLocation
}

type internal ConvertState = {
    PropertyValues: Map<ProvenancePropertyValueId, ProvenancePropertyValue>
    InputSets: Map<ProvenanceSetId, ProvenanceSet>
    OutputSets: Map<ProvenanceSetId, ProvenanceSet>
    Connections: Map<ProvenanceConnectionId, ProvenanceConnection>
    EndpointLocations: Map<ProvenanceSetId, ArcEndpointLocation>
    PropertyValueLocations: Map<ProvenancePropertyValueId, ArcWritebackLocation>
    ConnectionLocations: Map<ProvenanceConnectionId, ArcConnectionLocation>
    Warnings: string list
}

module internal TableLookup =

    let getTable (location: ArcTableLocation) (arc: ArcInvestigation) : ArcTable =
        match location.Scope with
        | ArcTableScope.Study -> arc.GetStudy(location.ParentIdentifier).GetTable(location.TableName)
        | ArcTableScope.Assay -> arc.GetAssay(location.ParentIdentifier).GetTable(location.TableName)
        | ArcTableScope.Run -> arc.GetRun(location.ParentIdentifier).GetTable(location.TableName)

    let allTables (arc: ArcInvestigation) = [
        for study in arc.Studies do
            for table in study.Tables do
                yield {
                    Location = {
                        Scope = ArcTableScope.Study
                        ParentIdentifier = study.Identifier
                        TableName = table.Name
                    }
                    Table = table
                }

        for assay in arc.Assays do
            for table in assay.Tables do
                yield {
                    Location = {
                        Scope = ArcTableScope.Assay
                        ParentIdentifier = assay.Identifier
                        TableName = table.Name
                    }
                    Table = table
                }

        for run in arc.Runs do
            for table in run.Tables do
                yield {
                    Location = {
                        Scope = ArcTableScope.Run
                        ParentIdentifier = run.Identifier
                        TableName = table.Name
                    }
                    Table = table
                }
    ]

module internal Normalize =

    module ArcKinds =

        let private endpoint id label =
            ProvenanceKind.create $"arc-isa:endpoint:{id}" label

        let private property id label =
            ProvenanceKind.create $"arc-isa:property:{id}" label

        let sourceEndpoint = endpoint "source" "Source"
        let sampleEndpoint = endpoint "sample" "Sample"
        let dataEndpoint = endpoint "data" "Data"
        let materialEndpoint = endpoint "material" "Material"
        let freeTextEndpoint text = endpoint $"free-text:{text}" text
        let characteristicProperty = property "characteristic" "Characteristic"
        let factorProperty = property "factor" "Factor"
        let parameterProperty = property "parameter" "Parameter"
        let componentProperty = property "component" "Component"

    let trimToOption (value: string) =
        if isNull value then
            None
        else
            let trimmed = value.Trim()

            if String.IsNullOrWhiteSpace trimmed then
                None
            else
                Some trimmed

    let optionText (value: string option) = value |> Option.bind trimToOption

    let slug (value: string) =
        value.ToLowerInvariant()
        |> Seq.map (fun ch -> if Char.IsLetterOrDigit ch then ch else '-')
        |> Seq.toArray
        |> String
        |> fun text -> text.Trim('-')

    let stableId parts =
        parts |> List.choose trimToOption |> List.map slug |> String.concat "--"

    let scopeText scope =
        match scope with
        | ArcTableScope.Study -> "study"
        | ArcTableScope.Assay -> "assay"
        | ArcTableScope.Run -> "run"

    let sourceRef (location: ArcTableLocation) : ProvenanceSourceRef = {
        Id = $"{scopeText location.Scope}:{location.ParentIdentifier}:{location.TableName}"
        Name = location.TableName
    }

    let locationParts location = [
        scopeText location.Scope
        location.ParentIdentifier
        location.TableName
    ]

    let termFromOntology (oa: OntologyAnnotation) : ProvenanceTerm = {
        Name = oa.NameText
        TermSource = optionText oa.TermSourceREF
        TermAccession = optionText oa.TermAccessionNumber
    }

    let termIsEmpty (term: ProvenanceTerm) =
        String.IsNullOrWhiteSpace term.Name
        && term.TermSource.IsNone
        && term.TermAccession.IsNone

    let termOptionFromOntology (oa: OntologyAnnotation option) =
        oa |> Option.map termFromOntology |> Option.filter (termIsEmpty >> not)

    let ioKindFromARCtrl (ioType: IOType) =
        match ioType with
        | IOType.Source -> ArcKinds.sourceEndpoint
        | IOType.Sample -> ArcKinds.sampleEndpoint
        | IOType.Data -> ArcKinds.dataEndpoint
        | IOType.Material -> ArcKinds.materialEndpoint
        | IOType.FreeText text -> ArcKinds.freeTextEndpoint text

    let ioHeaderFromHeader (header: CompositeHeader) : ProvenanceIOHeader option =
        match header with
        | CompositeHeader.Input ioType
        | CompositeHeader.Output ioType ->
            Some {
                Kind = ioKindFromARCtrl ioType
                Text = header.ToString()
            }
        | _ -> None

    let private propertyHeader kind (category: OntologyAnnotation option) =
        category
        |> termOptionFromOntology
        |> Option.map (fun category -> { Kind = kind; Category = category })

    let characteristicHeader (value: MaterialAttributeValue) =
        value.Category
        |> Option.bind (fun category -> category.CharacteristicType)
        |> propertyHeader ArcKinds.characteristicProperty

    let factorHeader (value: FactorValue) =
        value.Category
        |> Option.bind (fun category -> category.FactorType)
        |> propertyHeader ArcKinds.factorProperty

    let parameterHeader (value: ProcessParameterValue) =
        value.Category
        |> Option.bind (fun category -> category.ParameterName)
        |> propertyHeader ArcKinds.parameterProperty

    let componentHeader (value: Component) =
        value.ComponentType |> propertyHeader ArcKinds.componentProperty

    let normalizeUnit unit = unit |> termOptionFromOntology

    let provenanceValue
        (value: ScalarValue option)
        (unit: OntologyAnnotation option)
        : (ProvenanceValue * ProvenanceTerm option) option =
        let normalizedUnit = normalizeUnit unit

        match value with
        | Some(ScalarValue.Name text) ->
            trimToOption text
            |> Option.map (fun text -> ProvenanceValue.Text text, normalizedUnit)
        | Some(ScalarValue.Int value) -> Some(ProvenanceValue.Integer value, normalizedUnit)
        | Some(ScalarValue.Float value) -> Some(ProvenanceValue.Float value, normalizedUnit)
        | Some(ScalarValue.Ontology term) ->
            let normalized = termFromOntology term

            if termIsEmpty normalized then
                None
            else
                Some(ProvenanceValue.Term normalized, None)
        | None -> None

    let pairInputNames (pair: ProcessPairContext) = pair.InputName |> Option.toList

    let pairOutputNames (pair: ProcessPairContext) = pair.OutputName |> Option.toList

    let sourceAnchor (pair: ProcessPairContext) header : ProvenanceWritebackAnchor = {
        Source = sourceRef pair.Location
        ProcessId = pair.ProcessId
        ProcessName = pair.ProcessName
        Header = header
        InputNames = pairInputNames pair
        OutputNames = pairOutputNames pair
    }

    let writebackLocation (pair: ProcessPairContext) header columnIndex : ArcWritebackLocation = {
        Table = pair.Location
        ProcessId = pair.ProcessId
        ProcessName = pair.ProcessName
        Header = header
        InputNames = pairInputNames pair
        OutputNames = pairOutputNames pair
        ColumnIndex = columnIndex
    }

    let valueIdentityText value unit =
        let baseText =
            match value with
            | ProvenanceValue.Text value -> $"text:{value}"
            | ProvenanceValue.Integer value -> $"int:{value}"
            | ProvenanceValue.Float value -> $"float:{value}"
            | ProvenanceValue.Term term ->
                let termSource = defaultArg term.TermSource ""
                let termAccession = defaultArg term.TermAccession ""
                $"term:{term.Name}:{termSource}:{termAccession}"

        match unit with
        | Some unit ->
            let termSource = defaultArg unit.TermSource ""
            let termAccession = defaultArg unit.TermAccession ""
            $"{baseText}:unit:{unit.Name}:{termSource}:{termAccession}"
        | None -> baseText

module internal RealColumns =

    type RealTableShape = {
        InputHeader: ProvenanceIOHeader option
        OutputHeader: ProvenanceIOHeader option
    }

    let shape (table: ArcTable) = {
        InputHeader =
            table.TryGetInputColumn()
            |> Option.bind (fun column -> Normalize.ioHeaderFromHeader column.Header)
        OutputHeader =
            table.TryGetOutputColumn()
            |> Option.bind (fun column -> Normalize.ioHeaderFromHeader column.Header)
    }

module internal Dedup =

    let emptyState: ConvertState = {
        PropertyValues = Map.empty
        InputSets = Map.empty
        OutputSets = Map.empty
        Connections = Map.empty
        EndpointLocations = Map.empty
        PropertyValueLocations = Map.empty
        ConnectionLocations = Map.empty
        Warnings = []
    }

    let private appendWarning warning (state: ConvertState) = {
        state with
            Warnings = warning :: state.Warnings
    }

    let private setId role location header name =
        Normalize.stableId ([ "set"; role ] @ Normalize.locationParts location @ [ header.Text; name ])

    let ensureInputSet (location: ArcTableLocation) (header: ProvenanceIOHeader) name (state: ConvertState) =
        let id = setId "input" location header name

        let existing =
            state.InputSets
            |> Map.tryFind id
            |> Option.defaultValue {
                Id = id
                Source = Normalize.sourceRef location
                Header = header
                Name = name
                PropertyValueIds = []
                InheritedPropertyValueIds = Map.empty
            }

        let endpointLocation: ArcEndpointLocation = {
            Table = location
            Header = header
            Name = name
        }

        id,
        {
            state with
                InputSets = state.InputSets |> Map.add id existing
                EndpointLocations = state.EndpointLocations |> Map.add id endpointLocation
        }

    let ensureOutputSet (location: ArcTableLocation) (header: ProvenanceIOHeader) name (state: ConvertState) =
        let id = setId "output" location header name

        let existing =
            state.OutputSets
            |> Map.tryFind id
            |> Option.defaultValue {
                Id = id
                Source = Normalize.sourceRef location
                Header = header
                Name = name
                PropertyValueIds = []
                InheritedPropertyValueIds = Map.empty
            }

        let endpointLocation: ArcEndpointLocation = {
            Table = location
            Header = header
            Name = name
        }

        id,
        {
            state with
                OutputSets = state.OutputSets |> Map.add id existing
                EndpointLocations = state.EndpointLocations |> Map.add id endpointLocation
        }

    let addConnection
        (location: ArcTableLocation)
        processId
        processName
        inputSetId
        inputName
        outputSetId
        outputName
        (state: ConvertState)
        =
        let id =
            Normalize.stableId (
                [ "connection" ]
                @ Normalize.locationParts location
                @ [
                    processId |> Option.defaultValue ""
                    inputSetId
                    outputSetId
                ]
            )

        let connection: ProvenanceConnection = {
            Id = id
            Source = Normalize.sourceRef location
            ProcessId = processId
            ProcessName = processName
            InputSetId = inputSetId
            OutputSetId = outputSetId
        }

        let connectionLocation: ArcConnectionLocation = {
            Table = location
            ProcessId = processId
            ProcessName = processName
            InputSetId = inputSetId
            OutputSetId = outputSetId
            InputName = inputName
            OutputName = outputName
        }

        {
            state with
                Connections = state.Connections |> Map.add id connection
                ConnectionLocations = state.ConnectionLocations |> Map.add id connectionLocation
        }

    let private attachPropertyValueId propertyValueId (set: ProvenanceSet) =
        if set.PropertyValueIds |> List.contains propertyValueId then
            set
        else
            {
                set with
                    PropertyValueIds = set.PropertyValueIds @ [ propertyValueId ]
            }

    let private attachToSetMap propertyValueId setIds sets =
        setIds
        |> List.distinct
        |> List.fold
            (fun currentSets setId ->
                currentSets
                |> Map.change setId (Option.map (attachPropertyValueId propertyValueId))
            )
            sets

    let private propertyValueId (candidate: CandidatePropertyValue) =
        Normalize.stableId (
            [ "property" ]
            @ Normalize.locationParts candidate.WritebackLocation.Table
            @ [
                candidate.WritebackLocation.ProcessName |> Option.defaultValue ""
                candidate.Header.Kind.Id
                candidate.Header.Category.Name
                Normalize.valueIdentityText candidate.Value candidate.Unit
            ]
        )

    let private addPropertyLocation propertyValueId location map =
        match map |> Map.tryFind propertyValueId with
        | Some _ -> map
        | None -> map |> Map.add propertyValueId location

    let private mergeDistinct left right = left @ right |> List.distinct

    let private keepExisting existing incoming =
        match existing with
        | Some _ -> existing
        | None -> incoming

    let private mergeAnchor (left: ProvenanceWritebackAnchor) (right: ProvenanceWritebackAnchor) = {
        left with
            ProcessId = keepExisting left.ProcessId right.ProcessId
            ProcessName = keepExisting left.ProcessName right.ProcessName
            InputNames = mergeDistinct left.InputNames right.InputNames
            OutputNames = mergeDistinct left.OutputNames right.OutputNames
    }

    let private mergeOrigin existing incoming =
        match existing, incoming with
        | ProvenancePropertyOrigin.Real left, ProvenancePropertyOrigin.Real right ->
            ProvenancePropertyOrigin.Real(mergeAnchor left right)
        | ProvenancePropertyOrigin.Virtual left, ProvenancePropertyOrigin.Virtual right ->
            ProvenancePropertyOrigin.Virtual(mergeAnchor left right)
        | ProvenancePropertyOrigin.Real _, ProvenancePropertyOrigin.Virtual _
        | ProvenancePropertyOrigin.Virtual _, ProvenancePropertyOrigin.Real _ -> existing

    let addCandidateProperty (candidate: CandidatePropertyValue) (state: ConvertState) =
        let targetInputSetIds = candidate.TargetInputSetIds |> List.distinct
        let targetOutputSetIds = candidate.TargetOutputSetIds |> List.distinct

        match targetInputSetIds, targetOutputSetIds with
        | [], [] ->
            appendWarning
                $"Skipped property '{candidate.Header.Category.Name}' because no loaded endpoint target was available."
                state
        | _ ->
            let id = propertyValueId candidate

            let propertyValue: ProvenancePropertyValue =
                match state.PropertyValues |> Map.tryFind id with
                | Some existing -> {
                    existing with
                        Origin = mergeOrigin existing.Origin candidate.Origin
                  }
                | None -> {
                    Id = id
                    Header = candidate.Header
                    Value = candidate.Value
                    Unit = candidate.Unit
                    Origin = candidate.Origin
                  }

            {
                state with
                    PropertyValues = state.PropertyValues |> Map.add id propertyValue
                    InputSets = state.InputSets |> attachToSetMap id targetInputSetIds
                    OutputSets = state.OutputSets |> attachToSetMap id targetOutputSetIds
                    PropertyValueLocations =
                        state.PropertyValueLocations
                        |> addPropertyLocation id candidate.WritebackLocation
            }

    let toResult (loadedTable: ArcTableLocation) (state: ConvertState) : ArcProvenanceConversionResult = {
        Model =
            ({
                Source = Normalize.sourceRef loadedTable
                PropertyValues = state.PropertyValues
                InputSets = state.InputSets
                OutputSets = state.OutputSets
                Connections = state.Connections
             }
             |> ProvenanceModel.refreshInheritedProperties)
        Index = {
            LoadedTable = loadedTable
            EndpointLocations = state.EndpointLocations
            PropertyValueLocations = state.PropertyValueLocations
            ConnectionLocations = state.ConnectionLocations
        }
        Warnings = List.rev state.Warnings
    }

module internal LoadedTable =

    let processPairs (proc: Process) =
        let inputs = proc.Inputs |> Option.defaultValue []
        let outputs = proc.Outputs |> Option.defaultValue []
        let pairCount = min inputs.Length outputs.Length

        List.zip (inputs |> List.truncate pairCount) (outputs |> List.truncate pairCount)

    let pairContext
        (location: ArcTableLocation)
        (proc: Process)
        (input: ProcessInput)
        (output: ProcessOutput)
        : ProcessPairContext =
        {
            Location = location
            ProcessId = proc.Name
            ProcessName = Normalize.trimToOption location.TableName
            InputName = Normalize.trimToOption input.Name
            OutputName = Normalize.trimToOption output.Name
        }

    let private candidate
        pair
        targetInputSetIds
        targetOutputSetIds
        header
        value
        unit
        columnIndex
        : CandidatePropertyValue =
        {
            Header = header
            Value = value
            Unit = unit
            Origin = ProvenancePropertyOrigin.Real(Normalize.sourceAnchor pair header)
            TargetInputSetIds = targetInputSetIds
            TargetOutputSetIds = targetOutputSetIds
            WritebackLocation = Normalize.writebackLocation pair header columnIndex
        }

    let inputCharacteristicCandidates pair targetInputSetIds (input: ProcessInput) =
        input
        |> ProcessInput.tryGetCharacteristicValues
        |> Option.defaultValue []
        |> List.choose (fun value ->
            Normalize.characteristicHeader value
            |> Option.bind (fun header ->
                Normalize.provenanceValue value.Value value.Unit
                |> Option.map (fun (propertyValue, unit) ->
                    candidate
                        pair
                        targetInputSetIds
                        []
                        header
                        propertyValue
                        unit
                        (tryGetCharacteristicColumnIndex value)
                )
            )
        )

    let private outputCharacteristicCandidatesForTargets
        pair
        targetInputSetIds
        targetOutputSetIds
        (output: ProcessOutput)
        =
        output
        |> ProcessOutput.tryGetCharacteristicValues
        |> Option.defaultValue []
        |> List.choose (fun value ->
            Normalize.characteristicHeader value
            |> Option.bind (fun header ->
                Normalize.provenanceValue value.Value value.Unit
                |> Option.map (fun (propertyValue, unit) ->
                    candidate
                        pair
                        targetInputSetIds
                        targetOutputSetIds
                        header
                        propertyValue
                        unit
                        (tryGetCharacteristicColumnIndex value)
                )
            )
        )

    let outputCharacteristicCandidates pair targetOutputSetIds output =
        outputCharacteristicCandidatesForTargets pair [] targetOutputSetIds output

    let previousOutputCharacteristicCandidates pair targetInputSetIds output =
        outputCharacteristicCandidatesForTargets pair targetInputSetIds [] output

    let private outputFactorCandidatesForTargets pair targetInputSetIds targetOutputSetIds (output: ProcessOutput) =
        output
        |> ProcessOutput.tryGetFactorValues
        |> Option.defaultValue []
        |> List.choose (fun value ->
            Normalize.factorHeader value
            |> Option.bind (fun header ->
                Normalize.provenanceValue value.Value value.Unit
                |> Option.map (fun (propertyValue, unit) ->
                    candidate
                        pair
                        targetInputSetIds
                        targetOutputSetIds
                        header
                        propertyValue
                        unit
                        (tryGetFactorColumnIndex value)
                )
            )
        )

    let previousOutputFactorCandidates pair targetInputSetIds output =
        outputFactorCandidatesForTargets pair targetInputSetIds [] output

    let processParameterCandidates pair targetInputSetIds targetOutputSetIds (proc: Process) =
        proc.ParameterValues
        |> Option.defaultValue []
        |> List.choose (fun value ->
            Normalize.parameterHeader value
            |> Option.bind (fun header ->
                Normalize.provenanceValue value.Value value.Unit
                |> Option.map (fun (propertyValue, unit) ->
                    candidate
                        pair
                        targetInputSetIds
                        targetOutputSetIds
                        header
                        propertyValue
                        unit
                        (tryGetParameterColumnIndex value)
                )
            )
        )

    let processComponentCandidates pair targetInputSetIds targetOutputSetIds (proc: Process) =
        proc.ExecutesProtocol
        |> Option.bind (fun protocol -> protocol.Components)
        |> Option.defaultValue []
        |> List.choose (fun value ->
            Normalize.componentHeader value
            |> Option.bind (fun header ->
                Normalize.provenanceValue value.ComponentValue value.ComponentUnit
                |> Option.map (fun (propertyValue, unit) ->
                    candidate
                        pair
                        targetInputSetIds
                        targetOutputSetIds
                        header
                        propertyValue
                        unit
                        (tryGetComponentIndex value)
                )
            )
        )

    let private pairCandidates
        pair
        inputSetId
        outputSetId
        (proc: Process)
        (input: ProcessInput)
        (output: ProcessOutput)
        =
        let targetInputSetIds = inputSetId |> Option.toList
        let targetOutputSetIds = outputSetId |> Option.toList

        let inputValueTargetIds =
            if List.isEmpty targetInputSetIds then
                targetOutputSetIds
            else
                targetInputSetIds

        let outputValueTargetIds =
            if List.isEmpty targetOutputSetIds then
                targetInputSetIds
            else
                targetOutputSetIds

        let parameterInputTargetIds, parameterOutputTargetIds =
            match targetInputSetIds, targetOutputSetIds with
            | [], outputs -> [], outputs
            | inputs, [] -> inputs, []
            | inputs, outputs -> inputs, outputs

        inputCharacteristicCandidates pair inputValueTargetIds input
        @ outputCharacteristicCandidates pair outputValueTargetIds output
        @ outputFactorCandidatesForTargets pair parameterInputTargetIds parameterOutputTargetIds output
        @ processParameterCandidates pair parameterInputTargetIds parameterOutputTargetIds proc
        @ processComponentCandidates pair parameterInputTargetIds parameterOutputTargetIds proc

    let convert (location: ArcTableLocation) (table: ArcTable) : ConvertState =
        let shape = RealColumns.shape table

        table.GetProcesses()
        |> List.fold
            (fun (currentState: ConvertState) proc ->
                processPairs proc
                |> List.fold
                    (fun pairState (input, output) ->
                        let pair = pairContext location proc input output

                        let inputSetId, pairState =
                            match shape.InputHeader, pair.InputName with
                            | Some inputHeader, Some inputName ->
                                let inputSetId, pairState =
                                    Dedup.ensureInputSet location inputHeader inputName pairState

                                Some inputSetId, pairState
                            | _ -> None, pairState

                        let outputSetId, pairState =
                            match shape.OutputHeader, pair.OutputName with
                            | Some outputHeader, Some outputName ->
                                let outputSetId, pairState =
                                    Dedup.ensureOutputSet location outputHeader outputName pairState

                                Some outputSetId, pairState
                            | _ -> None, pairState

                        let pairState =
                            match inputSetId, pair.InputName, outputSetId, pair.OutputName with
                            | Some inputSetId, Some inputName, Some outputSetId, Some outputName ->
                                Dedup.addConnection
                                    location
                                    pair.ProcessId
                                    pair.ProcessName
                                    inputSetId
                                    inputName
                                    outputSetId
                                    outputName
                                    pairState
                            | _ -> pairState

                        pairCandidates pair inputSetId outputSetId proc input output
                        |> List.fold
                            (fun candidateState candidate -> Dedup.addCandidateProperty candidate candidateState)
                            pairState
                    )
                    currentState
            )
            Dedup.emptyState

module internal PreviousContext =

    let private loadedInputFrontier (state: ConvertState) =
        state.InputSets
        |> Map.toList
        |> List.groupBy (fun (_, set) -> set.Name)
        |> List.map (fun (name, sets) -> name, sets |> List.map fst |> Set.ofList)
        |> Map.ofList

    let attach (loadedLocation: ArcTableLocation) (arc: ArcInvestigation) (state: ConvertState) =
        if state.InputSets.IsEmpty then
            state
        else
            let pairs =
                TableLookup.allTables arc
                |> List.filter (fun tableRef -> tableRef.Location <> loadedLocation)
                |> List.collect (fun tableRef ->
                    tableRef.Table.GetProcesses()
                    |> List.collect (fun proc ->
                        LoadedTable.processPairs proc
                        |> List.map (fun (input, output) ->
                            LoadedTable.pairContext tableRef.Location proc input output, proc, input, output
                        )
                    )
                )

            let rec walk frontier visited currentState =
                let matches =
                    pairs
                    |> List.choose (fun (pair, proc, input, output) ->
                        let pairKey =
                            Normalize.stableId (
                                Normalize.locationParts pair.Location
                                @ [
                                    pair.ProcessName |> Option.defaultValue ""
                                    pair.ProcessId |> Option.defaultValue ""
                                    pair.InputName |> Option.defaultValue ""
                                    pair.OutputName |> Option.defaultValue ""
                                ]
                            )

                        match pair.OutputName with
                        | Some outputName when not (Set.contains pairKey visited) ->
                            frontier
                            |> Map.tryFind outputName
                            |> Option.map (fun targetSetIds -> pairKey, pair, proc, input, output, targetSetIds)
                        | _ -> None
                    )

                match matches with
                | [] -> currentState
                | _ ->
                    let nextState, nextFrontier, nextVisited =
                        matches
                        |> List.fold
                            (fun
                                (foldState, foldFrontier, foldVisited)
                                (pairKey, pair, proc, input, output, targetSetIds) ->
                                let targetInputSetIds = targetSetIds |> Set.toList

                                let foldState =
                                    LoadedTable.inputCharacteristicCandidates pair targetInputSetIds input
                                    @ LoadedTable.previousOutputCharacteristicCandidates pair targetInputSetIds output
                                    @ LoadedTable.previousOutputFactorCandidates pair targetInputSetIds output
                                    @ LoadedTable.processParameterCandidates pair targetInputSetIds [] proc
                                    @ LoadedTable.processComponentCandidates pair targetInputSetIds [] proc
                                    |> List.fold
                                        (fun candidateState candidate ->
                                            Dedup.addCandidateProperty candidate candidateState
                                        )
                                        foldState

                                let foldFrontier =
                                    match pair.InputName with
                                    | Some inputName ->
                                        let existing =
                                            foldFrontier |> Map.tryFind inputName |> Option.defaultValue Set.empty

                                        foldFrontier |> Map.add inputName (Set.union existing targetSetIds)
                                    | None -> foldFrontier

                                foldState, foldFrontier, Set.add pairKey foldVisited
                            )
                            (currentState, frontier, visited)

                    walk nextFrontier nextVisited nextState

            walk (loadedInputFrontier state) Set.empty state

let fromLoadedArc (options: ArcProvenanceConverterOptions) (arc: ArcInvestigation) : ArcProvenanceConversionResult =
    let loadedTable = TableLookup.getTable options.LoadedTable arc
    let state = LoadedTable.convert options.LoadedTable loadedTable

    let state =
        if options.IncludePreviousContext then
            PreviousContext.attach options.LoadedTable arc state
        else
            state

    Dedup.toResult options.LoadedTable state
