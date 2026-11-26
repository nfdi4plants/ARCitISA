module Arcitisa.Components.Shared.ProvenanceGrouping.Session

open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit

type ProvenanceLayerId = string
// Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
type ProvenancePairId = ProvenanceLayerId
type ProvenanceLayerSideId = string

type PropertyValueSourceInfo = {
    TableName: ProvenanceTableName option
    ProcessId: ProvenanceProcessId option
    ProcessName: ProvenanceProcessName option
    InputNames: string list
    OutputNames: string list
    IsCurrentTable: bool
}

type ProvenanceLayer = {
    Id: ProvenanceLayerId
    Label: string
    InputSideId: ProvenanceLayerSideId
    OutputSideId: ProvenanceLayerSideId
    Model: ProvenanceModel
} with

    // Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
    member this.LeftLayerId = this.InputSideId
    member this.RightLayerId = this.OutputSideId

// Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
type ProvenanceLayerPair = ProvenanceLayer

type ProvenanceSetReference = {
    LayerId: ProvenanceLayerId
    Side: ProvenanceSide
    SetId: ProvenanceSetId
} with

    // Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
    member this.PairId = this.LayerId

type ProvenanceReferenceLink = {
    Source: ProvenanceSetReference
    Target: ProvenanceSetReference
} with

    member this.Previous = this.Source
    member this.Next = this.Target

// Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
type ProvenanceBoundaryLink = ProvenanceReferenceLink

type ProvenanceSession = {
    Layers: ProvenanceLayer list
    LayerOrder: ProvenanceLayerId list
    ActiveLayerId: ProvenanceLayerId
    ReferenceLinks: ProvenanceReferenceLink list
    DirtyPropertyValueIds: Set<ProvenancePropertyValueId>
    /// Ordered log of every writeback patch produced since the session was
    /// created. This - not the per-change delta returned alongside a
    /// SessionResult - is the source of truth for the future "apply" button:
    /// it lives on the session, so undo (which restores a session snapshot)
    /// retracts already-emitted patches for free.
    PatchLog: ProvenanceTablePatch list
} with
    // Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
    member this.Pairs =
        this.Layers |> List.map (fun layer -> layer.Id, layer) |> Map.ofList

    member this.PairOrder = this.LayerOrder
    member this.ActivePairId = this.ActiveLayerId
    member this.BoundaryLinks = this.ReferenceLinks

[<RequireQualifiedAccess>]
type SessionError =
    | LayerNotFound of ProvenanceLayerId
    | SetNotFound of ProvenanceSetReference
    | EditFailed of EditError

type AddLayerCommand = {
    Name: ProvenanceSourceName
    SelectedSets: (ProvenanceSide * ProvenanceSetId) list
}

type SessionResult = Result<ProvenanceSession * ProvenanceTablePatch list, SessionError>

module Session =

    let private sideId layerId side =
        match side with
        | ProvenanceSide.Input -> $"{layerId}-input"
        | ProvenanceSide.Output -> $"{layerId}-output"

    let init (model: ProvenanceModel) =
        let layerId = "layer-1"

        let layer = {
            Id = layerId
            Label = model.Source.Name
            InputSideId = sideId layerId ProvenanceSide.Input
            OutputSideId = sideId layerId ProvenanceSide.Output
            Model = model
        }

        {
            Layers = [ layer ]
            LayerOrder = [ layer.Id ]
            ActiveLayerId = layer.Id
            ReferenceLinks = []
            DirtyPropertyValueIds = Set.empty
            PatchLog = []
        }

    let tryLayer layerId session =
        session.Layers |> List.tryFind (fun layer -> layer.Id = layerId)

    let layerById layerId session =
        tryLayer layerId session
        |> Option.defaultWith (fun () -> failwithf "Unknown provenance layer '%s'." layerId)

    let activeLayer session = layerById session.ActiveLayerId session

    // Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
    let activePair session = activeLayer session

    let private values map = map |> Map.toList |> List.map snd

    let private setAt side setId pair =
        match side with
        | ProvenanceSide.Input -> pair.Model.InputSets.TryFind setId
        | ProvenanceSide.Output -> pair.Model.OutputSets.TryFind setId

    let private nextIndex session = session.LayerOrder.Length + 1

    let private nextInputSetId layerId side index setId =
        let sideText =
            match side with
            | ProvenanceSide.Input -> "input"
            | ProvenanceSide.Output -> "output"

        $"{layerId}-from-{sideText}-{index}-{setId}"

    let addLayer command session : SessionResult =
        let current = activeLayer session

        let selectedSets =
            match command.SelectedSets with
            | [] ->
                current.Model.OutputSets
                |> values
                |> List.sortBy (fun set -> set.Name, set.Id)
                |> List.map (fun set -> ProvenanceSide.Output, set.Id)
            | selected -> selected

        let missing =
            selectedSets
            |> List.tryPick (fun (side, setId) ->
                if setAt side setId current |> Option.isNone then
                    Some {
                        LayerId = current.Id
                        Side = side
                        SetId = setId
                    }
                else
                    None
            )

        match missing with
        | Some setRef -> Error(SessionError.SetNotFound setRef)
        | None ->
            let layerIndex = nextIndex session
            let layerId = $"layer-{layerIndex}"

            // Id is namespaced with layerId (unique per session) so two layers
            // added with the same entered name never collide: everything keyed
            // by Source.Id (colors, "current table" detection, and the PG-1
            // property-value id namespace) would otherwise conflate them.
            let source = {
                Id = $"{layerId}:{command.Name}"
                Name = command.Name
            }

            let inputs, links =
                selectedSets
                |> List.mapi (fun seedIndex (side, setId) ->
                    let selectedSet = (setAt side setId current).Value
                    let nextId = nextInputSetId layerId side seedIndex setId

                    let projected = {
                        selectedSet with
                            Id = nextId
                            Source = source
                    }

                    let link = {
                        Source = {
                            LayerId = current.Id
                            Side = side
                            SetId = setId
                        }
                        Target = {
                            LayerId = layerId
                            Side = ProvenanceSide.Input
                            SetId = nextId
                        }
                    }

                    projected.Id, projected, link
                )
                |> List.fold
                    (fun (sets, links) (id, projected, link) -> Map.add id projected sets, link :: links)
                    (Map.empty, [])

            let projectedPropertyValueIds =
                inputs
                |> Map.toList
                |> List.collect (fun (_, set) -> ProvenanceSet.effectivePropertyValueIds set)
                |> Set.ofList

            let projectedPropertyValues =
                current.Model.PropertyValues
                |> Map.filter (fun id _ -> projectedPropertyValueIds.Contains id)

            let pair = {
                Id = layerId
                Label = source.Name
                InputSideId = sideId layerId ProvenanceSide.Input
                OutputSideId = sideId layerId ProvenanceSide.Output
                Model = {
                    Source = source
                    PropertyValues = projectedPropertyValues
                    InputSets = inputs
                    OutputSets = Map.empty
                    Connections = Map.empty
                }
            }

            Ok(
                {
                    session with
                        Layers = session.Layers @ [ pair ]
                        LayerOrder = session.LayerOrder @ [ pair.Id ]
                        ActiveLayerId = pair.Id
                        ReferenceLinks = session.ReferenceLinks @ List.rev links
                },
                []
            )

    let private replaceLayer layer session = {
        session with
            Layers =
                session.Layers
                |> List.map (fun current -> if current.Id = layer.Id then layer else current)
    }

    let private mapEditError = Result.mapError SessionError.EditFailed

    /// Appends a leaf edit's emitted patches to the session's authoritative
    /// PatchLog. Applied only at the six leaf edit functions - composites
    /// (updatePropertyValues, removeConnections, copyPropertyValueToLoadedTarget,
    /// EditorActions batch helpers) call those leaves and must not wrap again,
    /// or patches would be logged twice.
    let private withLoggedPatches (result: SessionResult) : SessionResult =
        result
        |> Result.map (fun (next, patches) ->
            {
                next with
                    PatchLog = next.PatchLog @ patches
            },
            patches
        )

    let private updateLayerModel layerId model session =
        match tryLayer layerId session with
        | None -> Error(SessionError.LayerNotFound layerId)
        | Some layer -> Ok(replaceLayer { layer with Model = model } session)

    let private layerAtRef reference session = layerById reference.LayerId session

    let private setAtRef reference session =
        let layer = layerAtRef reference session
        setAt reference.Side reference.SetId layer

    let private referencesForLayer layerId session =
        let layer = layerById layerId session

        [
            for setId, _ in layer.Model.InputSets |> Map.toList do
                yield {
                    LayerId = layerId
                    Side = ProvenanceSide.Input
                    SetId = setId
                }
            for setId, _ in layer.Model.OutputSets |> Map.toList do
                yield {
                    LayerId = layerId
                    Side = ProvenanceSide.Output
                    SetId = setId
                }
        ]

    let private adjacentReferences reference session =
        session.ReferenceLinks
        |> List.collect (fun link -> [
            if link.Source = reference then
                yield link.Target
            if link.Target = reference then
                yield link.Source
        ])

    let private referenceComponent seedRefs session =
        let rec loop pending seen =
            match pending with
            | [] -> seen
            | current :: rest when seen |> Set.contains current -> loop rest seen
            | current :: rest ->
                let next = adjacentReferences current session
                loop (next @ rest) (seen |> Set.add current)

        loop seedRefs Set.empty

    let private referenceContainsPropertyValue propertyValueId reference session =
        match setAtRef reference session with
        | None -> false
        | Some set -> ProvenanceSet.effectivePropertyValueIds set |> List.contains propertyValueId

    let private dirtyReferencesInLayer layerId session =
        referencesForLayer layerId session
        |> List.filter (fun reference ->
            session.DirtyPropertyValueIds
            |> Set.exists (fun propertyValueId -> referenceContainsPropertyValue propertyValueId reference session)
        )

    let private affectedComponent previousLayerId nextLayerId session =
        [
            yield! dirtyReferencesInLayer previousLayerId session
            yield! referencesForLayer nextLayerId session
        ]
        |> List.distinct
        |> fun refs -> referenceComponent refs session

    let private referencedPropertyValues set model =
        ProvenanceSet.effectivePropertyValueIds set
        |> List.choose (fun id -> model.PropertyValues.TryFind id |> Option.map (fun value -> id, value))

    let private localOwnPropertyValueIds sourceLayer targetSet =
        let sourcePropertyValueIds =
            sourceLayer.Model.PropertyValues |> Map.toList |> List.map fst |> Set.ofList

        targetSet.PropertyValueIds
        |> List.filter (fun propertyValueId -> not (sourcePropertyValueIds.Contains propertyValueId))

    let private copySetData sourceRef targetRef session =
        let sourceLayer = layerAtRef sourceRef session
        let targetLayer = layerAtRef targetRef session
        let sourceSet = (setAtRef sourceRef session).Value
        let targetSet = (setAtRef targetRef session).Value

        let targetSet =
            let inheritedPropertyValueIds =
                match targetRef.Side with
                | ProvenanceSide.Input -> sourceSet.InheritedPropertyValueIds
                | ProvenanceSide.Output -> targetSet.InheritedPropertyValueIds

            {
                targetSet with
                    PropertyValueIds =
                        [
                            yield! sourceSet.PropertyValueIds
                            yield! localOwnPropertyValueIds sourceLayer targetSet
                        ]
                        |> List.distinct
                    InheritedPropertyValueIds = inheritedPropertyValueIds
            }

        let propertyValues =
            referencedPropertyValues sourceSet sourceLayer.Model
            |> List.fold (fun state (id, value) -> Map.add id value state) targetLayer.Model.PropertyValues

        let targetModel =
            match targetRef.Side with
            | ProvenanceSide.Input -> {
                targetLayer.Model with
                    InputSets = targetLayer.Model.InputSets |> Map.add targetSet.Id targetSet
                    PropertyValues = propertyValues
              }
            | ProvenanceSide.Output -> {
                targetLayer.Model with
                    OutputSets = targetLayer.Model.OutputSets |> Map.add targetSet.Id targetSet
                    PropertyValues = propertyValues
              }
            |> ProvenanceModel.refreshInheritedProperties

        replaceLayer { targetLayer with Model = targetModel } session

    let private layerIndex (session: ProvenanceSession) (layerId: ProvenanceLayerId) =
        session.LayerOrder
        |> List.tryFindIndex ((=) layerId)
        |> Option.defaultValue System.Int32.MaxValue

    let private propertyValueFromLayer
        (layerId: ProvenanceLayerId)
        (propertyValueId: ProvenancePropertyValueId)
        (session: ProvenanceSession)
        =
        (layerById layerId session).Model.PropertyValues.TryFind propertyValueId

    let private applyPropertyValueToLayer
        (layerId: ProvenanceLayerId)
        (propertyValue: ProvenancePropertyValue)
        (session: ProvenanceSession)
        =
        let layer = layerById layerId session

        if layer.Model.PropertyValues.ContainsKey propertyValue.Id then
            replaceLayer
                {
                    layer with
                        Model = {
                            layer.Model with
                                PropertyValues = layer.Model.PropertyValues |> Map.add propertyValue.Id propertyValue
                        }
                }
                session
        else
            session

    let private applyPropertyValueToReference
        (propertyValue: ProvenancePropertyValue)
        (reference: ProvenanceSetReference)
        (session: ProvenanceSession)
        =
        if referenceContainsPropertyValue propertyValue.Id reference session then
            applyPropertyValueToLayer reference.LayerId propertyValue session
        else
            session

    let private refreshDirtyProperties
        previousLayerId
        (referenceSet: Set<ProvenanceSetReference>)
        (session: ProvenanceSession)
        =
        session.DirtyPropertyValueIds
        |> Set.fold
            (fun state propertyValueId ->
                match propertyValueFromLayer previousLayerId propertyValueId state with
                | None -> state
                | Some propertyValue ->
                    referenceSet
                    |> Set.toList
                    |> List.fold
                        (fun current reference -> applyPropertyValueToReference propertyValue reference current)
                        state
            )
            session

    let private refreshStructuralLinks (referenceSet: Set<ProvenanceSetReference>) session =
        session.ReferenceLinks
        |> List.filter (fun link -> referenceSet.Contains link.Source && referenceSet.Contains link.Target)
        |> List.sortBy (fun link -> layerIndex session link.Source.LayerId, layerIndex session link.Target.LayerId)
        |> List.fold (fun state link -> copySetData link.Source link.Target state) session

    let private refreshForFocusChange previousLayerId nextLayerId session =
        let referenceSet = affectedComponent previousLayerId nextLayerId session

        let withPropertyEdits = refreshDirtyProperties previousLayerId referenceSet session

        let withStructuralRefresh = refreshStructuralLinks referenceSet withPropertyEdits

        {
            withStructuralRefresh with
                DirtyPropertyValueIds = Set.empty
        }

    let selectLayer layerId session : SessionResult =
        match tryLayer layerId session with
        | Some _ ->
            let refreshed = refreshForFocusChange session.ActiveLayerId layerId session

            Ok(
                {
                    refreshed with
                        ActiveLayerId = layerId
                },
                []
            )
        | None -> Error(SessionError.LayerNotFound layerId)

    // Temporary adapter for component migration. Remove after downstream callers stop using pair terminology.
    let selectPair layerId session : SessionResult = selectLayer layerId session

    let updatePropertyValue propertyValueId value unit session : SessionResult =
        let layer = activeLayer session

        Edit.updatePropertyValue propertyValueId value unit layer.Model
        |> mapEditError
        |> Result.bind (fun (model, patches) ->
            updateLayerModel layer.Id model session
            |> Result.map (fun next ->
                {
                    next with
                        DirtyPropertyValueIds = next.DirtyPropertyValueIds |> Set.add propertyValueId
                },
                patches
            )
        )
        |> withLoggedPatches

    let private validatePropertyValueUpdate propertyValueId value unit session =
        let layer = activeLayer session

        Edit.updatePropertyValue propertyValueId value unit layer.Model
        |> mapEditError
        |> Result.map ignore

    let updatePropertyValues
        (updates: (ProvenancePropertyValueId * ProvenanceValue * ProvenanceTerm option) list)
        session
        : SessionResult =
        let updates =
            updates |> List.distinctBy (fun (propertyValueId, _, _) -> propertyValueId)

        let rec validate remaining =
            match remaining with
            | [] -> Ok()
            | (propertyValueId, value, unit) :: tail ->
                validatePropertyValueUpdate propertyValueId value unit session
                |> Result.bind (fun () -> validate tail)

        validate updates
        |> Result.bind (fun () ->
            updates
            |> List.fold
                (fun result (propertyValueId, value, unit) ->
                    result
                    |> Result.bind (fun (state, patches) ->
                        updatePropertyValue propertyValueId value unit state
                        |> Result.map (fun (next, addedPatches) -> next, patches @ addedPatches)
                    )
                )
                (Ok(session, []))
        )

    let createCurrentLoadedPropertyValue (command: CreateLoadedPropertyValueCommand) session : SessionResult =
        let layer = activeLayer session

        Edit.createLoadedPropertyValue command layer.Model
        |> mapEditError
        |> Result.bind (fun (model, patches) ->
            updateLayerModel layer.Id model session
            |> Result.map (fun next -> next, patches)
        )
        |> withLoggedPatches

    let createLoadedPropertyValue (command: CreateLoadedPropertyValueCommand) session : SessionResult =
        let layer = activeLayer session

        Edit.createLoadedPropertyValue command layer.Model
        |> mapEditError
        |> Result.bind (fun (model, patches) ->
            updateLayerModel layer.Id model session
            |> Result.map (fun next -> next, patches)
        )
        |> withLoggedPatches

    let copyPropertyValueToLoadedTarget propertyValueId (target: ProvenancePropertyTarget) session : SessionResult =
        let layer = activeLayer session

        match layer.Model.PropertyValues.TryFind propertyValueId with
        | None -> Error(SessionError.EditFailed(EditError.PropertyNotFound propertyValueId))
        | Some propertyValue ->
            createLoadedPropertyValue
                {
                    Target = target
                    CopiedFrom = Some propertyValueId
                    Header = propertyValue.Header
                    Value = propertyValue.Value
                    Unit = propertyValue.Unit
                }
                session

    let createLoadedSet command session : SessionResult =
        let layer = activeLayer session

        Edit.createLoadedSet command layer.Model
        |> mapEditError
        |> Result.bind (fun (model, patches) ->
            updateLayerModel layer.Id model session
            |> Result.map (fun next -> next, patches)
        )
        |> withLoggedPatches

    let connectSets inputSetId outputSetId processName session : SessionResult =
        let layer = activeLayer session

        Edit.connectSets inputSetId outputSetId processName layer.Model
        |> mapEditError
        |> Result.bind (fun (model, patches) ->
            updateLayerModel layer.Id model session
            |> Result.map (fun next -> next, patches)
        )
        |> withLoggedPatches

    let removeConnection connectionId session : SessionResult =
        let layer = activeLayer session

        Edit.removeConnection connectionId layer.Model
        |> mapEditError
        |> Result.bind (fun (model, patches) ->
            updateLayerModel layer.Id model session
            |> Result.map (fun next -> next, patches)
        )
        |> withLoggedPatches

    let removeConnections connectionIds session : SessionResult =
        connectionIds
        |> List.distinct
        |> List.fold
            (fun result connectionId ->
                result
                |> Result.bind (fun (current, patches) ->
                    removeConnection connectionId current
                    |> Result.map (fun (next, added) -> next, patches @ added)
                )
            )
            (Ok(session, []))

    let private sourceInfoFromAnchor
        (currentSource: ProvenanceSourceRef)
        (source: ProvenanceWritebackAnchor)
        : PropertyValueSourceInfo =
        {
            TableName = Some source.Source.Name
            ProcessId = source.ProcessId
            ProcessName = source.ProcessName
            InputNames = source.InputNames
            OutputNames = source.OutputNames
            IsCurrentTable = source.Source.Id = currentSource.Id
        }

    let propertyValueSourceInfo
        (layer: ProvenanceLayer)
        (propertyValue: ProvenancePropertyValue)
        : PropertyValueSourceInfo option =
        match propertyValue.Origin with
        | ProvenancePropertyOrigin.Real source
        | ProvenancePropertyOrigin.Virtual source -> Some(sourceInfoFromAnchor layer.Model.Source source)

    let propertyValueOriginInSession
        (layerId: ProvenanceLayerId)
        (_side: ProvenanceSide)
        (propertyValueId: ProvenancePropertyValueId)
        (session: ProvenanceSession)
        : ProvenancePropertyOrigin option =
        tryLayer layerId session
        |> Option.bind (fun layer ->
            layer.Model.PropertyValues.TryFind propertyValueId
            |> Option.map (fun propertyValue -> propertyValue.Origin)
        )
