module Arcitisa.Components.Shared.ProvenanceGrouping.Edit

open Arcitisa.Components.Shared.ProvenanceGrouping.Types

[<RequireQualifiedAccess>]
type EditError =
    | PropertyNotFound of ProvenancePropertyValueId
    | SetNotFound of ProvenanceSetId
    | ConnectionNotFound of ProvenanceConnectionId
    | TableNotLoaded of ProvenanceTableName
    | MissingSourceAnchor of ProvenancePropertyValueId
    | DuplicateHeader of tableName: ProvenanceTableName * header: ProvenancePropertyHeader
    | PreviousContextCreationNotAllowed of tableName: ProvenanceTableName
    | PreviousContextConnectionCreationNotAllowed of tableName: ProvenanceTableName

[<RequireQualifiedAccess>]
type ProvenancePropertyTarget =
    | InputSets of ProvenanceSetId list
    | OutputSets of ProvenanceSetId list
    | Connections of ProvenanceConnectionId list

[<RequireQualifiedAccess>]
type ProvenanceTablePatch =
    | UpdatePropertyValue of
        propertyValueId: ProvenancePropertyValueId *
        source: ProvenanceWritebackAnchor *
        oldValue: ProvenanceValue *
        newValue: ProvenanceValue *
        unit: ProvenanceTerm option
    | AddLoadedSet of side: ProvenanceSide * tableName: ProvenanceTableName * header: ProvenanceIOHeader * name: string
    | AddLoadedPropertyValue of
        target: ProvenancePropertyTarget *
        copiedFrom: ProvenancePropertyValueId option *
        header: ProvenancePropertyHeader *
        value: ProvenanceValue *
        unit: ProvenanceTerm option
    | AddLoadedConnection of
        tableName: ProvenanceTableName *
        processId: ProvenanceProcessId option *
        processName: ProvenanceProcessName option *
        inputSetId: ProvenanceSetId *
        outputSetId: ProvenanceSetId
    | RemoveLoadedConnection of
        tableName: ProvenanceTableName *
        processId: ProvenanceProcessId option *
        processName: ProvenanceProcessName option *
        inputSetId: ProvenanceSetId *
        outputSetId: ProvenanceSetId

type CreateLoadedPropertyValueCommand = {
    Target: ProvenancePropertyTarget
    CopiedFrom: ProvenancePropertyValueId option
    Header: ProvenancePropertyHeader
    Value: ProvenanceValue
    Unit: ProvenanceTerm option
}

type CreateLoadedSetCommand = {
    Side: ProvenanceSide
    Header: ProvenanceIOHeader
    Name: string
}

type EditResult = Result<ProvenanceModel * ProvenanceTablePatch list, EditError>

// IDs are namespaced with the owning model's Source.Id because propagation
// (Session.refreshDirtyProperties / copySetData) treats these IDs as globally
// unique identities across every layer in a session, not just this model.
let private nextPropertyValueId (model: ProvenanceModel) =
    let rec loop index =
        let id = sprintf "%s::property-value-%i" model.Source.Id index

        if model.PropertyValues.ContainsKey id then
            loop (index + 1)
        else
            id

    loop (model.PropertyValues.Count + 1)

let private nextConnectionId (model: ProvenanceModel) =
    let rec loop index =
        let id = sprintf "%s::connection-%i" model.Source.Id index

        if model.Connections.ContainsKey id then
            loop (index + 1)
        else
            id

    loop (model.Connections.Count + 1)

let private nextSetId side (model: ProvenanceModel) =
    let prefix, sets =
        match side with
        | ProvenanceSide.Input -> "input-set", model.InputSets
        | ProvenanceSide.Output -> "output-set", model.OutputSets

    let rec loop index =
        let id = $"{model.Source.Id}::{prefix}-{index}"
        if sets.ContainsKey id then loop (index + 1) else id

    loop 1

let private loadedSet (model: ProvenanceModel) (set: ProvenanceSet) =
    if set.Source.Id = model.Source.Id then
        Ok set
    else
        Error(EditError.PreviousContextCreationNotAllowed set.Source.Name)

let private addPropertyValueId propertyValueId (set: ProvenanceSet) =
    if set.PropertyValueIds |> List.contains propertyValueId then
        set
    else
        {
            set with
                PropertyValueIds = set.PropertyValueIds @ [ propertyValueId ]
        }

let private updateSets propertyValueId targetSetIds sets =
    targetSetIds
    |> List.fold (fun state setId -> state |> Map.change setId (Option.map (addPropertyValueId propertyValueId))) sets

let private chooseInputSet (model: ProvenanceModel) setId =
    match model.InputSets.TryFind setId with
    | Some set -> loadedSet model set
    | None -> Error(EditError.SetNotFound setId)

let private chooseOutputSet (model: ProvenanceModel) setId =
    match model.OutputSets.TryFind setId with
    | Some set -> loadedSet model set
    | None -> Error(EditError.SetNotFound setId)

let private chooseConnection (model: ProvenanceModel) connectionId =
    match model.Connections.TryFind connectionId with
    | Some connection when connection.Source.Id = model.Source.Id -> Ok connection
    | Some connection -> Error(EditError.PreviousContextConnectionCreationNotAllowed connection.Source.Name)
    | None -> Error(EditError.ConnectionNotFound connectionId)

let private collectResults results =
    let rec loop collected remaining =
        match remaining with
        | [] -> Ok(List.rev collected)
        | Ok value :: tail -> loop (value :: collected) tail
        | Error error :: _ -> Error error

    loop [] results

type private ResolvedPropertyTarget = {
    InputSets: ProvenanceSet list
    OutputSets: ProvenanceSet list
}

let private targetSets model target =
    match target with
    | ProvenancePropertyTarget.InputSets setIds ->
        match setIds |> List.map (chooseInputSet model) |> collectResults with
        | Ok inputSets ->
            Ok {
                InputSets = inputSets
                OutputSets = []
            }
        | Error error -> Error error
    | ProvenancePropertyTarget.OutputSets setIds ->
        match setIds |> List.map (chooseOutputSet model) |> collectResults with
        | Ok outputSets ->
            Ok {
                InputSets = []
                OutputSets = outputSets
            }
        | Error error -> Error error
    | ProvenancePropertyTarget.Connections connectionIds ->
        match connectionIds |> List.map (chooseConnection model) |> collectResults with
        | Error error -> Error error
        | Ok connections ->
            let inputResults =
                connections
                |> List.map (fun connection -> chooseInputSet model connection.InputSetId)

            let outputResults =
                connections
                |> List.map (fun connection -> chooseOutputSet model connection.OutputSetId)

            match collectResults inputResults, collectResults outputResults with
            | Ok inputSets, Ok outputSets ->
                Ok {
                    InputSets = inputSets
                    OutputSets = outputSets
                }
            | Error error, _ -> Error error
            | _, Error error -> Error error

let private sourceFromTarget
    (model: ProvenanceModel)
    (header: ProvenancePropertyHeader)
    (target: ProvenancePropertyTarget)
    (resolvedTarget: ResolvedPropertyTarget)
    : ProvenanceWritebackAnchor =
    let processName =
        match target with
        | ProvenancePropertyTarget.Connections connectionIds ->
            connectionIds
            |> List.tryPick (fun connectionId ->
                model.Connections.TryFind connectionId
                |> Option.bind (fun connection -> connection.ProcessName)
            )
        | _ -> None

    let processId =
        match target with
        | ProvenancePropertyTarget.Connections connectionIds ->
            connectionIds
            |> List.tryPick (fun connectionId ->
                model.Connections.TryFind connectionId
                |> Option.bind (fun connection -> connection.ProcessId)
            )
        | _ -> None

    {
        Source = model.Source
        ProcessId = processId
        ProcessName = processName
        Header = header
        InputNames = resolvedTarget.InputSets |> List.map (fun set -> set.Name) |> List.distinct
        OutputNames = resolvedTarget.OutputSets |> List.map (fun set -> set.Name) |> List.distinct
    }

let private processNameCompatible (left: ProvenanceProcessName option) (right: ProvenanceProcessName option) =
    left = right || left.IsNone || right.IsNone

let private processIdCompatible (left: ProvenanceProcessId option) (right: ProvenanceProcessId option) =
    left = right || left.IsNone || right.IsNone

let private anchorOfOrigin =
    function
    | ProvenancePropertyOrigin.Real anchor
    | ProvenancePropertyOrigin.Virtual anchor -> anchor

let private sourceContextMatches (left: ProvenanceWritebackAnchor) (right: ProvenanceWritebackAnchor) =
    left.Source.Id = right.Source.Id
    && left.Header = right.Header
    && left.InputNames = right.InputNames
    && left.OutputNames = right.OutputNames
    && processIdCompatible left.ProcessId right.ProcessId
    && processNameCompatible left.ProcessName right.ProcessName

let private tryFindEquivalentLoadedPropertyValue
    (model: ProvenanceModel)
    (header: ProvenancePropertyHeader)
    (value: ProvenanceValue)
    (unit: ProvenanceTerm option)
    (source: ProvenanceWritebackAnchor)
    =
    model.PropertyValues
    |> Map.toList
    |> List.tryPick (fun (propertyValueId, propertyValue) ->
        let resolvedAnchor = anchorOfOrigin propertyValue.Origin

        if
            propertyValue.Header = header
            && propertyValue.Value = value
            && propertyValue.Unit = unit
            && sourceContextMatches resolvedAnchor source
        then
            Some propertyValueId
        else
            None
    )

let createLoadedSet (command: CreateLoadedSetCommand) (model: ProvenanceModel) : EditResult =
    let existing =
        match command.Side with
        | ProvenanceSide.Input ->
            model.InputSets
            |> Map.tryFindKey (fun _ set ->
                set.Source.Id = model.Source.Id
                && set.Header = command.Header
                && set.Name = command.Name
            )
        | ProvenanceSide.Output ->
            model.OutputSets
            |> Map.tryFindKey (fun _ set ->
                set.Source.Id = model.Source.Id
                && set.Header = command.Header
                && set.Name = command.Name
            )

    match existing with
    | Some _ -> Ok(model, [])
    | None ->
        let id = nextSetId command.Side model

        let loadedSet: ProvenanceSet = {
            Id = id
            Source = model.Source
            Header = command.Header
            Name = command.Name
            PropertyValueIds = []
            InheritedPropertyValueIds = Map.empty
        }

        let nextModel =
            match command.Side with
            | ProvenanceSide.Input -> {
                model with
                    InputSets = model.InputSets |> Map.add id loadedSet
              }
            | ProvenanceSide.Output -> {
                model with
                    OutputSets = model.OutputSets |> Map.add id loadedSet
              }

        Ok(
            nextModel,
            [
                ProvenanceTablePatch.AddLoadedSet(command.Side, model.Source.Name, command.Header, command.Name)
            ]
        )

let updatePropertyValue propertyValueId newValue newUnit (model: ProvenanceModel) : EditResult =
    match model.PropertyValues.TryFind propertyValueId with
    | None -> Error(EditError.PropertyNotFound propertyValueId)
    | Some propertyValue ->
        // Virtual (editor-created) values used to emit no patch here, on the
        // assumption the writeback log only needed the AddLoadedPropertyValue
        // that created them. But a later drop can overwrite that value before
        // any writeback happens, and the log would then still say "add X"
        // while the model says Y - silent data loss for editor-created values.
        // Real and Virtual anchors carry the same Source/Header/InputNames/
        // OutputNames writeback context, so both can emit the same patch shape.
        let anchor = anchorOfOrigin propertyValue.Origin

        let nextPropertyValue: ProvenancePropertyValue = {
            propertyValue with
                Value = newValue
                Unit = newUnit
        }

        let nextModel = {
            model with
                PropertyValues = model.PropertyValues |> Map.add propertyValueId nextPropertyValue
        }

        Ok(
            nextModel,
            [
                ProvenanceTablePatch.UpdatePropertyValue(
                    propertyValueId,
                    anchor,
                    propertyValue.Value,
                    newValue,
                    newUnit
                )
            ]
        )

let createLoadedPropertyValue (command: CreateLoadedPropertyValueCommand) (model: ProvenanceModel) : EditResult =
    match targetSets model command.Target with
    | Error error -> Error error
    | Ok resolvedTarget ->
        let anchor = sourceFromTarget model command.Header command.Target resolvedTarget
        let origin = ProvenancePropertyOrigin.Virtual anchor

        let inputSetIds = resolvedTarget.InputSets |> List.map (fun set -> set.Id)

        let outputSetIds = resolvedTarget.OutputSets |> List.map (fun set -> set.Id)

        match tryFindEquivalentLoadedPropertyValue model command.Header command.Value command.Unit anchor with
        | Some propertyValueId ->
            let nextModel =
                {
                    model with
                        InputSets = model.InputSets |> updateSets propertyValueId inputSetIds
                        OutputSets = model.OutputSets |> updateSets propertyValueId outputSetIds
                }
                |> ProvenanceModel.refreshInheritedProperties

            if nextModel = model then
                Ok(model, [])
            else
                Ok(
                    nextModel,
                    [
                        ProvenanceTablePatch.AddLoadedPropertyValue(
                            command.Target,
                            command.CopiedFrom,
                            command.Header,
                            command.Value,
                            command.Unit
                        )
                    ]
                )
        | None ->
            let propertyValueId = nextPropertyValueId model

            let propertyValue: ProvenancePropertyValue = {
                Id = propertyValueId
                Header = command.Header
                Value = command.Value
                Unit = command.Unit
                Origin = origin
            }

            let nextModel =
                {
                    model with
                        PropertyValues = model.PropertyValues |> Map.add propertyValueId propertyValue
                        InputSets = model.InputSets |> updateSets propertyValueId inputSetIds
                        OutputSets = model.OutputSets |> updateSets propertyValueId outputSetIds
                }
                |> ProvenanceModel.refreshInheritedProperties

            Ok(
                nextModel,
                [
                    ProvenanceTablePatch.AddLoadedPropertyValue(
                        command.Target,
                        command.CopiedFrom,
                        command.Header,
                        command.Value,
                        command.Unit
                    )
                ]
            )

let copyPropertyValueToLoadedTarget propertyValueId target (model: ProvenanceModel) : EditResult =
    match model.PropertyValues.TryFind propertyValueId with
    | None -> Error(EditError.PropertyNotFound propertyValueId)
    | Some propertyValue ->
        createLoadedPropertyValue
            {
                Target = target
                CopiedFrom = Some propertyValueId
                Header = propertyValue.Header
                Value = propertyValue.Value
                Unit = propertyValue.Unit
            }
            model

let removeConnection (connectionId: ProvenanceConnectionId) (model: ProvenanceModel) : EditResult =
    match chooseConnection model connectionId with
    | Error error -> Error error
    | Ok connection ->
        // Strip the removed connection's inherited entries explicitly: once the
        // connection is gone from the map, the refresh would treat them as
        // carried-in upstream entries and preserve them.
        let dropInherited setId sets =
            sets
            |> Map.change setId (Option.map (ProvenanceSet.removeInheritedPropertyValueIds connectionId))

        let nextModel =
            {
                model with
                    Connections = model.Connections |> Map.remove connectionId
                    InputSets = model.InputSets |> dropInherited connection.InputSetId
                    OutputSets = model.OutputSets |> dropInherited connection.OutputSetId
            }
            |> ProvenanceModel.refreshInheritedProperties

        Ok(
            nextModel,
            [
                ProvenanceTablePatch.RemoveLoadedConnection(
                    connection.Source.Name,
                    connection.ProcessId,
                    connection.ProcessName,
                    connection.InputSetId,
                    connection.OutputSetId
                )
            ]
        )

let connectSets inputSetId outputSetId processName (model: ProvenanceModel) : EditResult =
    match chooseInputSet model inputSetId, chooseOutputSet model outputSetId with
    | Error error, _ -> Error error
    | _, Error error -> Error error
    | Ok _, Ok _ when
        model.Connections
        |> Map.exists (fun _ connection ->
            connection.Source.Id = model.Source.Id
            && connection.InputSetId = inputSetId
            && connection.OutputSetId = outputSetId
        )
        ->
        // Same shape as the createLoadedSet duplicate guard above: a no-op
        // Ok keeps composite folds (connectSetPairs, all-to-all) working
        // unchanged instead of needing to special-case an empty patch list.
        Ok(model, [])
    | Ok _, Ok _ ->
        let connectionId = nextConnectionId model

        let connection: ProvenanceConnection = {
            Id = connectionId
            Source = model.Source
            ProcessId = None
            ProcessName = processName
            InputSetId = inputSetId
            OutputSetId = outputSetId
        }

        let nextModel =
            {
                model with
                    Connections = model.Connections |> Map.add connectionId connection
            }
            |> ProvenanceModel.refreshInheritedProperties

        Ok(
            nextModel,
            [
                ProvenanceTablePatch.AddLoadedConnection(model.Source.Name, None, processName, inputSetId, outputSetId)
            ]
        )
