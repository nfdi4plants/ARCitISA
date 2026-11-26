module Arcitisa.Components.Shared.ProvenanceGrouping.Grouping

open Arcitisa.Components.Shared.ProvenanceGrouping.Types

type GroupingKey = ProvenancePropertyKey

[<RequireQualifiedAccess>]
type GroupingScope =
    | Input
    | Output
    | Both

type GroupingAssignment = {
    Key: GroupingKey
    Scope: GroupingScope
}

type DisplayMember = {
    SetId: ProvenanceSetId
    Name: string
    PropertyValueIds: ProvenancePropertyValueId list
}

type DisplayGroupingValue = {
    Key: GroupingKey
    Value: ProvenanceValue
    Unit: ProvenanceTerm option
}

type DisplayGroup = {
    Id: string
    TableName: ProvenanceTableName
    Side: ProvenanceSide
    GroupingValues: DisplayGroupingValue list
    Members: DisplayMember list
}

type DisplayConnection = {
    Id: string
    SourceGroupId: string
    TargetGroupId: string
    ConnectionIds: ProvenanceConnectionId list
}

let private mapValues map = map |> Map.toList |> List.map snd

let valueText (value: ProvenanceValue) (unit: ProvenanceTerm option) =
    let text =
        match value with
        | ProvenanceValue.Text value -> value
        | ProvenanceValue.Integer value -> string value
        | ProvenanceValue.Float value -> string value
        | ProvenanceValue.Term term -> term.Name

    match unit with
    | Some unit -> $"{text} {unit.Name}"
    | None -> text

let private groupingKeySortText (key: GroupingKey) =
    sprintf "%s:%s:%s" key.Header.Kind.Id key.Header.Category.Name key.OriginSource.Id

let private groupingValuesText duplicateHeaders keyValueSeparator keySeparator values =
    values
    |> List.groupBy (fun value -> value.Key)
    |> List.sortBy (fun (key, _) -> groupingKeySortText key)
    |> List.map (fun (key, groupedValues) ->
        let valuesText =
            groupedValues
            |> List.sortBy (fun groupingValue -> valueText groupingValue.Value groupingValue.Unit)
            |> List.map (fun groupingValue -> valueText groupingValue.Value groupingValue.Unit)
            |> String.concat " | "

        let keyText =
            if duplicateHeaders |> Set.contains key.Header then
                $"{key.Header.Category.Name}@{key.OriginSource.Id}"
            else
                key.Header.Category.Name

        sprintf "%s%s%s" keyText keyValueSeparator valuesText
    )
    |> String.concat keySeparator

let private sideText side =
    match side with
    | ProvenanceSide.Input -> "input"
    | ProvenanceSide.Output -> "output"

let private loadedSets (model: ProvenanceModel) side =
    let source =
        match side with
        | ProvenanceSide.Input -> model.InputSets
        | ProvenanceSide.Output -> model.OutputSets

    source
    |> mapValues
    |> List.filter (fun set -> set.Source.Id = model.Source.Id)
    |> List.sortBy (fun set -> set.Name, set.Id)

let private setPropertyValues (model: ProvenanceModel) (set: ProvenanceSet) =
    ProvenanceSet.effectivePropertyValueIds set
    |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)

type private GroupingValue = GroupingKey * ProvenanceValue * ProvenanceTerm option * ProvenancePropertyValueId list

let private valueSetForKey key propertyValues : GroupingValue list list =
    let values =
        propertyValues
        |> List.filter (ProvenancePropertyValue.belongsTo key)
        |> List.groupBy (fun propertyValue -> propertyValue.Value, propertyValue.Unit)
        |> List.map (fun ((value, unit), propertyValues) ->
            key,
            value,
            unit,
            propertyValues
            |> List.map (fun propertyValue -> propertyValue.Id)
            |> List.distinct
            |> List.sort
        )
        |> List.sortBy (fun (_, value, unit, _) -> valueText value unit)

    if values.IsEmpty then [] else [ values ]

let private valuesForKey model set key =
    setPropertyValues model set |> valueSetForKey key

let private scopeApplies side scope =
    match side, scope with
    | ProvenanceSide.Input, GroupingScope.Input
    | ProvenanceSide.Input, GroupingScope.Both
    | ProvenanceSide.Output, GroupingScope.Output
    | ProvenanceSide.Output, GroupingScope.Both -> true
    | _ -> false

let scopeForSide side =
    match side with
    | ProvenanceSide.Input -> GroupingScope.Input
    | ProvenanceSide.Output -> GroupingScope.Output

let private normalizeAssignments side assignments =
    assignments
    |> List.filter (fun assignment -> scopeApplies side assignment.Scope)
    |> List.groupBy (fun assignment -> assignment.Key)
    |> List.map (fun (key, grouped) ->
        if grouped |> List.exists (fun assignment -> assignment.Scope = GroupingScope.Both) then
            {
                Key = key
                Scope = GroupingScope.Both
            }
        else
            grouped.Head
    )

let private combinations values =
    let rec loop collected remaining =
        match remaining with
        | [] -> [ List.rev collected ]
        | head :: tail -> head |> List.collect (fun value -> loop (value :: collected) tail)

    loop [] values

let private displayMember (set: ProvenanceSet) propertyValueIds = {
    SetId = set.Id
    Name = set.Name
    PropertyValueIds = propertyValueIds |> List.distinct
}

let private groupId duplicateHeaders side (values: DisplayGroupingValue list) fallbackSetId =
    match values with
    | [] -> sprintf "%s:%s" (sideText side) fallbackSetId
    | _ ->
        values
        |> groupingValuesText duplicateHeaders "=" "|"
        |> sprintf "%s:%s" (sideText side)

let displayGroupsForAssignments (model: ProvenanceModel) side assignments =
    let sets = loadedSets model side
    let assignments = normalizeAssignments side assignments

    let duplicateHeaders =
        assignments
        |> List.countBy (fun assignment -> assignment.Key.Header)
        |> List.choose (fun (header, count) -> if count > 1 then Some header else None)
        |> Set.ofList

    match assignments with
    | [] ->
        sets
        |> List.map (fun set -> {
            Id = groupId duplicateHeaders side [] set.Id
            TableName = set.Source.Name
            Side = side
            GroupingValues = []
            Members = [
                displayMember set (ProvenanceSet.effectivePropertyValueIds set)
            ]
        })
    | activeAssignments ->
        let grouped = [
            for set in sets do
                let keyValues =
                    activeAssignments
                    |> List.map (fun assignment -> valuesForKey model set assignment.Key)
                    |> List.filter (fun values -> values |> List.isEmpty |> not)

                if keyValues.IsEmpty then
                    yield
                        groupId duplicateHeaders side [] set.Id,
                        set.Source.Name,
                        [],
                        displayMember set (ProvenanceSet.effectivePropertyValueIds set)
                else
                    for combination in combinations keyValues do
                        let groupingValues =
                            combination
                            |> List.collect id
                            |> List.map (fun (key, value, unit, _) -> {
                                Key = key
                                Value = value
                                Unit = unit
                            })

                        yield
                            groupId duplicateHeaders side groupingValues set.Id,
                            set.Source.Name,
                            groupingValues,
                            displayMember set (ProvenanceSet.effectivePropertyValueIds set)
        ]

        grouped
        |> List.groupBy (fun (groupId, _, _, _) -> groupId)
        |> List.map (fun (id, groupedMembers) ->
            let _, tableName, groupingValues, _ = groupedMembers.Head

            {
                Id = id
                TableName = tableName
                Side = side
                GroupingValues = groupingValues
                Members =
                    groupedMembers
                    |> List.map (fun (_, _, _, member') -> member')
                    |> List.sortBy (fun member' -> member'.Name, member'.SetId)
            }
        )
        |> List.sortBy (fun group -> group.Id)

let displayGroups (model: ProvenanceModel) side groupingKeys =
    groupingKeys
    |> List.map (fun key -> { Key = key; Scope = scopeForSide side })
    |> displayGroupsForAssignments model side

let private groupIdsBySetId groups =
    groups
    |> List.collect (fun group -> group.Members |> List.map (fun member' -> member'.SetId, group.Id))
    |> List.groupBy fst
    |> List.map (fun (setId, grouped) -> setId, grouped |> List.map snd)
    |> Map.ofList

let displayConnections (model: ProvenanceModel) inputGroups outputGroups =
    if List.isEmpty inputGroups || List.isEmpty outputGroups then
        []
    else
        let inputGroupBySetId = groupIdsBySetId inputGroups
        let outputGroupBySetId = groupIdsBySetId outputGroups

        model.Connections
        |> mapValues
        |> List.filter (fun connection -> connection.Source.Id = model.Source.Id)
        |> List.collect (fun connection ->
            match
                inputGroupBySetId.TryFind connection.InputSetId, outputGroupBySetId.TryFind connection.OutputSetId
            with
            | Some inputGroupIds, Some outputGroupIds -> [
                for inputGroupId in inputGroupIds do
                    for outputGroupId in outputGroupIds do
                        yield (inputGroupId, outputGroupId), connection.Id
              ]
            | _ -> []
        )
        |> List.groupBy fst
        |> List.map (fun ((inputGroupId, outputGroupId), grouped) -> {
            Id = sprintf "%s-to-%s" inputGroupId outputGroupId
            SourceGroupId = inputGroupId
            TargetGroupId = outputGroupId
            ConnectionIds = grouped |> List.map snd |> List.distinct |> List.sort
        })
        |> List.sortBy (fun connection -> connection.Id)
