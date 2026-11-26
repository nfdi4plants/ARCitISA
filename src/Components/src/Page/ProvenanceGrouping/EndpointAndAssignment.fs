namespace Arcitisa.Components.Page.ProvenanceGrouping

/// Derives endpoint defaults, identities, and display headers for empty-side creation.
module Endpoints =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types

    let fallbackKind: ProvenanceKind =
        ProvenanceKind.create "editor:endpoint" "Endpoint"

    let endpointKindOptions () : ProvenanceKind list = [
        ProvenanceKind.create "arc-isa:endpoint:source" "Source"
        ProvenanceKind.create "arc-isa:endpoint:sample" "Sample"
        ProvenanceKind.create "arc-isa:endpoint:material" "Material"
        ProvenanceKind.create "arc-isa:endpoint:data" "Data"
    ]

    let defaultEndpointKind () : ProvenanceKind =
        endpointKindOptions () |> List.tryHead |> Option.defaultValue fallbackKind

    let endpointKindIdentity (kind: ProvenanceKind) = kind.Id

    let endpointHeader side (kind: ProvenanceKind) =
        let prefix = if side = ProvenanceSide.Input then "Input" else "Output"
        let label = ProvenanceKind.displayName kind

        {
            Kind = kind
            Text = $"{prefix} [{label}]"
        }

/// Plans how dropped property values should be added or overwritten across a target group.
module ValueAssignment =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
    open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
    open Arcitisa.Components.Shared.ProvenanceGrouping.Session
    open Arcitisa.Components.Page.ProvenanceGrouping.Types

    let private targetForGroup side (group: DisplayGroup) =
        let ids = group.Members |> List.map (fun m -> m.SetId)

        match side with
        | ProvenanceSide.Input -> ProvenancePropertyTarget.InputSets ids
        | ProvenanceSide.Output -> ProvenancePropertyTarget.OutputSets ids

    let private memberValuesForProperty property (model: ProvenanceModel) (member': DisplayMember) =
        member'.PropertyValueIds
        |> List.distinct
        |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)
        |> List.filter (ProvenancePropertyValue.belongsTo property)

    let planPropertyValueDrop
        (source: ValueAssignmentSource)
        (group: DisplayGroup)
        (model: ProvenanceModel)
        : Result<ValueAssignmentPlan, ValueAssignmentError> =
        let memberValues =
            group.Members
            |> List.map (fun member' -> member'.SetId, memberValuesForProperty source.Property model member')

        if memberValues.IsEmpty then
            Error ValueAssignmentError.EmptyTarget
        else
            let allValues = memberValues |> List.collect snd

            let distinctAssignedValues =
                allValues
                |> List.map (fun propertyValue -> propertyValue.Value, propertyValue.Unit)
                |> List.distinct

            if memberValues |> List.forall (fun (_, values) -> values.IsEmpty) then
                if source.Property.OriginSource.Id = model.Source.Id then
                    Ok(
                        AddCurrent {
                            Target = targetForGroup group.Side group
                            CopiedFrom = source.CopiedFrom
                            Header = source.Property.Header
                            Value = source.Value
                            Unit = source.Unit
                        }
                    )
                else
                    Error(ValueAssignmentError.UpstreamPropertyNotAssigned source.Property)
            elif
                memberValues |> List.forall (fun (_, values) -> not values.IsEmpty)
                && distinctAssignedValues.Length = 1
            then
                Ok(
                    ConfirmOverwrite {
                        Target = targetForGroup group.Side group
                        ExistingValueIds =
                            memberValues
                            |> List.collect (fun (_, values) -> values |> List.map (fun value -> value.Id))
                            |> List.distinct
                        Header = source.Property.Header
                        Value = source.Value
                        Unit = source.Unit
                    }
                )
            elif memberValues |> List.exists (fun (_, values) -> values.IsEmpty) then
                Error(ValueAssignmentError.MixedPropertyValueCounts source.Property)
            else
                let assignedSetIds =
                    memberValues
                    |> List.choose (fun (setId, values) -> if values.IsEmpty then None else Some setId)

                Error(ValueAssignmentError.MultiplePropertyValues(source.Property, assignedSetIds))

    let private combineGroupsForAssignment (groups: DisplayGroup list) : DisplayGroup option =
        match groups with
        | [] -> None
        | head :: _ ->
            let allMembers =
                groups
                |> List.collect (fun g -> g.Members)
                |> List.distinctBy (fun m -> m.SetId)

            Some { head with Members = allMembers }

    let planPropertyValueDropToGroups
        (source: ValueAssignmentSource)
        (groups: DisplayGroup list)
        (model: ProvenanceModel)
        : Result<PropertyAssignmentBatch, ValueAssignmentError> =
        groups
        |> List.groupBy (fun group -> group.Side)
        |> List.fold
            (fun result (side, sideGroups) ->
                result
                |> Result.bind (fun (batch: PropertyAssignmentBatch) ->
                    match combineGroupsForAssignment sideGroups with
                    | None -> Ok batch
                    | Some combinedGroup ->
                        planPropertyValueDrop source combinedGroup model
                        |> Result.map (fun plan ->
                            match plan with
                            | AddCurrent command -> {
                                batch with
                                    Adds = batch.Adds @ [ command ]
                              }
                            | ConfirmOverwrite warning -> {
                                batch with
                                    Overwrites = batch.Overwrites @ [ warning ]
                              }
                        )
                )
            )
            (Ok { Adds = []; Overwrites = [] })

    let selectedTargetGroupsForDrop
        (layerId: ProvenanceLayerId)
        (dropSide: ProvenanceSide)
        (dropGroupId: string)
        (selectedInputs: Set<ProvenanceLayerId * string>)
        (selectedOutputs: Set<ProvenanceLayerId * string>)
        (findGroup: ProvenanceSide -> string -> DisplayGroup option)
        : DisplayGroup list =
        let idsForLayer selected =
            selected
            |> Set.filter (fun (currentLayerId, _) -> currentLayerId = layerId)
            |> Set.map snd

        let inputIds = idsForLayer selectedInputs
        let outputIds = idsForLayer selectedOutputs

        let dropIsSelected =
            match dropSide with
            | ProvenanceSide.Input -> inputIds.Contains dropGroupId
            | ProvenanceSide.Output -> outputIds.Contains dropGroupId

        if dropIsSelected && (not inputIds.IsEmpty || not outputIds.IsEmpty) then
            [
                yield! inputIds |> Set.toList |> List.choose (findGroup ProvenanceSide.Input)
                yield! outputIds |> Set.toList |> List.choose (findGroup ProvenanceSide.Output)
            ]
        else
            findGroup dropSide dropGroupId |> Option.toList
