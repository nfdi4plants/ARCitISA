namespace Arcitisa.Components.Page.ProvenanceGrouping

/// Stable keys for the property shelf folders.
module PropertyFolders =

    open System
    open Arcitisa.Components.Shared.ProvenanceGrouping.Types

    let private slug (value: string) =
        let text = if isNull value then "" else value.Trim()

        let chars =
            text
            |> Seq.map (fun character ->
                if Char.IsLetterOrDigit character || character = '-' || character = '_' then
                    Char.ToLowerInvariant character
                else
                    '-'
            )
            |> Seq.toArray

        let compact =
            String(chars).Split([| '-' |], StringSplitOptions.RemoveEmptyEntries)
            |> String.concat "-"

        if String.IsNullOrWhiteSpace compact then
            "unknown"
        else
            compact

    let sourceFolderId (source: ProvenanceSourceRef) = $"source-{slug source.Id}"

    let unknownFolderId = "unknown-origin"

/// Builds property rail headers and values from the persistent model plus UI-only palette state.
module PropertyRails =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
    open Arcitisa.Components.Shared.ProvenanceGrouping.Session
    open Arcitisa.Components.Page.ProvenanceGrouping.Types

    type RailProjection = {
        Headers: ProvenancePropertyKey list
        ValuesByHeader: Map<ProvenancePropertyKey, ProvenancePropertyValue list>
        ExpandedHeaders: Set<ProvenancePropertyKey>
        CanSwitchHeaders: Set<ProvenancePropertyKey>
        StatsByHeader: Map<ProvenancePropertyKey, PropertyStats>
        ConnectionCountByHeader: Map<ProvenancePropertyKey, int>
        BadgeByHeader: Map<ProvenancePropertyKey, PropertyCountBadge>
        ColorByHeader: Map<ProvenancePropertyKey, ProvenanceColor option>
        OriginByHeader: Map<ProvenancePropertyKey, Set<ProvenancePropertyOrigin>>
        OriginFilterOptions: PropertyOriginFilter list
    }

    let setsForSide side (model: ProvenanceModel) =
        if side = ProvenanceSide.Input then
            model.InputSets
        else
            model.OutputSets

    let private headersFromSets
        (propertyValueIds: ProvenanceSet -> ProvenancePropertyValueId list)
        side
        (model: ProvenanceModel)
        =
        setsForSide side model
        |> Map.toList
        |> List.collect (fun (_, set) ->
            propertyValueIds set
            |> List.choose (fun id -> model.PropertyValues.TryFind id)
            |> List.map ProvenancePropertyValue.propertyKey
        )
        |> List.distinct
        |> List.sortBy (fun property -> property.Header.Category.Name, property.OriginSource.Id)

    let headersForSide side (model: ProvenanceModel) =
        headersFromSets ProvenanceSet.effectivePropertyValueIds side model

    let headersForModel (model: ProvenanceModel) =
        [
            yield! headersForSide ProvenanceSide.Input model
            yield! headersForSide ProvenanceSide.Output model
        ]
        |> List.distinct
        |> List.sortBy (fun property -> property.Header.Category.Name, property.OriginSource.Id)

    let hasHeaderForSide side property (model: ProvenanceModel) =
        headersForSide side model |> List.contains property

    let canSwitchHeader property (model: ProvenanceModel) =
        hasHeaderForSide ProvenanceSide.Input property model
        && hasHeaderForSide ProvenanceSide.Output property model

    let private placedHeadersForSide layerId side (uiState: UiState) =
        uiState.PropertyRailPlacements
        |> Map.toList
        |> List.choose (fun ((currentLayerId, key), targetSide) ->
            if currentLayerId = layerId && targetSide = side then
                Some key
            else
                None
        )

    let private railPlacement layerId property (uiState: UiState) =
        uiState.PropertyRailPlacements |> Map.tryFind (layerId, property)

    /// One-pass caches over a side's sets. Rail projection asks per-header questions
    /// (values, stats, origins, connection counts) for every header, so the shared
    /// scans are hoisted here instead of re-reading all sets per header.
    type SideIndex = {
        SetCount: int
        /// Id-distinct property values for the side, in set iteration order.
        Values: ProvenancePropertyValue list
        ValuesByHeader: Map<ProvenancePropertyKey, ProvenancePropertyValue list>
        HeaderSet: Set<ProvenancePropertyKey>
        DistinctValueCountByHeader: Map<ProvenancePropertyKey, int>
        SetsWithValueCountByHeader: Map<ProvenancePropertyKey, int>
        HeadersBySetId: Map<ProvenanceSetId, Set<ProvenancePropertyKey>>
    }

    let buildSideIndex side (model: ProvenanceModel) : SideIndex =
        let sets = setsForSide side model |> Map.toList

        let resolvedBySet =
            sets
            |> List.map (fun (setId, set) ->
                setId,
                ProvenanceSet.effectivePropertyValueIds set
                |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)
            )

        let values =
            sets
            |> List.collect (fun (_, set) -> ProvenanceSet.effectivePropertyValueIds set)
            |> List.distinct
            |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)

        let valuesByHeader =
            values |> List.groupBy ProvenancePropertyValue.propertyKey |> Map.ofList

        let distinctValueCountByHeader =
            resolvedBySet
            |> List.collect (fun (_, resolved) ->
                resolved
                |> List.map (fun propertyValue ->
                    ProvenancePropertyValue.propertyKey propertyValue, (propertyValue.Value, propertyValue.Unit)
                )
            )
            |> List.distinct
            |> List.countBy fst
            |> Map.ofList

        let setsWithValueCountByHeader =
            resolvedBySet
            |> List.collect (fun (_, resolved) ->
                resolved |> List.map ProvenancePropertyValue.propertyKey |> List.distinct
            )
            |> List.countBy id
            |> Map.ofList

        let headersBySetId =
            resolvedBySet
            |> List.map (fun (setId, resolved) ->
                setId, resolved |> List.map ProvenancePropertyValue.propertyKey |> Set.ofList
            )
            |> Map.ofList

        {
            SetCount = sets.Length
            Values = values
            ValuesByHeader = valuesByHeader
            HeaderSet = valuesByHeader |> Map.toList |> List.map fst |> Set.ofList
            DistinctValueCountByHeader = distinctValueCountByHeader
            SetsWithValueCountByHeader = setsWithValueCountByHeader
            HeadersBySetId = headersBySetId
        }

    /// Same result as headersForSide, read from a prebuilt index.
    let headersFromIndex (index: SideIndex) =
        index.Values
        |> List.map ProvenancePropertyValue.propertyKey
        |> List.distinct
        |> List.sortBy (fun property -> property.Header.Category.Name, property.OriginSource.Id)

    /// Same rail-side selection as before, but every per-header question is answered
    /// from the prebuilt side indexes instead of rescanning the model.
    let propertyRailHeadersFromIndexes
        (inputIndex: SideIndex)
        (outputIndex: SideIndex)
        layerId
        side
        (model: ProvenanceModel)
        uiState
        =
        let modelHeaders =
            [
                yield! headersFromIndex inputIndex
                yield! headersFromIndex outputIndex
            ]
            |> List.distinct
            |> List.sortBy (fun property -> property.Header.Category.Name, property.OriginSource.Id)

        let modelHeaderSet = Set.ofList modelHeaders

        let paletteInputSet =
            State.Palette.propertiesForSide layerId ProvenanceSide.Input uiState
            |> Set.ofList

        let paletteOutputSet =
            State.Palette.propertiesForSide layerId ProvenanceSide.Output uiState
            |> Set.ofList

        let paletteHeaders = State.Palette.propertiesForSide layerId side uiState

        let paletteSetFor paletteSide =
            if paletteSide = ProvenanceSide.Input then
                paletteInputSet
            else
                paletteOutputSet

        let hasPalette property =
            paletteInputSet.Contains property || paletteOutputSet.Contains property

        let currentSourceId = model.Source.Id

        let hasPreviousOriginIndexed property =
            property.OriginSource.Id <> currentSourceId

        let defaultSideForHeader property =
            if hasPreviousOriginIndexed property then
                Some ProvenanceSide.Input
            elif outputIndex.HeaderSet.Contains property then
                Some ProvenanceSide.Output
            elif inputIndex.HeaderSet.Contains property then
                Some ProvenanceSide.Input
            else
                None

        let knownHeaders =
            [ yield! modelHeaders; yield! paletteHeaders ] |> List.distinct |> Set.ofList

        let isValidRailSide header =
            modelHeaderSet.Contains header || hasPalette header

        [
            yield! modelHeaders
            yield! placedHeadersForSide layerId side uiState
            yield! paletteHeaders
        ]
        |> List.distinct
        |> List.filter (fun header -> knownHeaders.Contains header)
        |> List.filter (fun header ->
            match railPlacement layerId header uiState with
            | Some targetSide when isValidRailSide header -> targetSide = side
            | Some _ -> defaultSideForHeader header = Some side || (paletteSetFor side).Contains header
            | None ->
                defaultSideForHeader header = Some side
                || (defaultSideForHeader header).IsNone && (paletteSetFor side).Contains header
        )
        |> List.sortBy (fun property -> property.Header.Category.Name, property.OriginSource.Id)

    let propertyRailHeadersForSideInSession _session layerId side model uiState =
        propertyRailHeadersFromIndexes
            (buildSideIndex ProvenanceSide.Input model)
            (buildSideIndex ProvenanceSide.Output model)
            layerId
            side
            model
            uiState

    let propertyValuesForSideHeader layerId side property (model: ProvenanceModel) uiState =
        let modelValues =
            setsForSide side model
            |> Map.toList
            |> List.collect (fun (_, set) -> ProvenanceSet.effectivePropertyValueIds set)
            |> List.distinct
            |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)
            |> List.filter (ProvenancePropertyValue.belongsTo property)

        [
            yield! modelValues
            yield! State.Palette.valuesForProperty layerId side property uiState
        ]
        |> List.groupBy (fun propertyValue -> propertyValue.Value, propertyValue.Unit)
        |> List.map (fun (_, values) -> values |> List.sortBy (fun value -> value.Id) |> List.head)
        |> List.sortBy (fun propertyValue -> Formatting.formatValue propertyValue.Value propertyValue.Unit)

    let propertyOriginsForSideHeader layerId side property (model: ProvenanceModel) uiState =
        [
            yield!
                setsForSide side model
                |> Map.toList
                |> List.collect (fun (_, set) -> ProvenanceSet.effectivePropertyValueIds set)
                |> List.distinct
                |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)
                |> List.filter (ProvenancePropertyValue.belongsTo property)
                |> List.map (fun propertyValue -> propertyValue.Origin)
            yield!
                State.Palette.valuesForProperty layerId side property uiState
                |> List.map (fun propertyValue -> propertyValue.Origin)
        ]
        |> Set.ofList

module Search =

    let contains (needle: string) (haystack: string) =
        let needle = if isNull needle then "" else needle.Trim()

        if System.String.IsNullOrWhiteSpace needle then
            true
        elif isNull haystack then
            false
        else
            haystack.ToLowerInvariant().Contains(needle.ToLowerInvariant())

module PropertyProjection =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Shared.ProvenanceGrouping.Session
    open Arcitisa.Components.Page.ProvenanceGrouping.Types

    let anchorOfOrigin =
        function
        | ProvenancePropertyOrigin.Real anchor -> anchor
        | ProvenancePropertyOrigin.Virtual anchor -> anchor

    let sourceOfOrigin origin = (anchorOfOrigin origin).Source

    let originIsCurrent (model: ProvenanceModel) origin =
        (sourceOfOrigin origin).Id = model.Source.Id

    let propertyStatsForSide
        (side: ProvenanceSide)
        (property: ProvenancePropertyKey)
        (model: ProvenanceModel)
        : PropertyStats =
        let sets = PropertyRails.setsForSide side model
        let setCount = sets.Count

        let distinctValues =
            sets
            |> Map.toList
            |> List.collect (fun (_, set) ->
                ProvenanceSet.effectivePropertyValueIds set
                |> List.choose (fun id -> model.PropertyValues.TryFind id)
                |> List.filter (ProvenancePropertyValue.belongsTo property)
                |> List.map (fun pv -> pv.Value, pv.Unit)
            )
            |> List.distinct

        let setsWithValue =
            sets
            |> Map.toList
            |> List.filter (fun (_, set) ->
                ProvenanceSet.effectivePropertyValueIds set
                |> List.exists (fun id ->
                    model.PropertyValues.TryFind id
                    |> Option.exists (ProvenancePropertyValue.belongsTo property)
                )
            )
            |> List.length

        {
            Property = property
            DistinctValueCount = distinctValues.Length
            SetsWithValueCount = setsWithValue
            TotalSetCount = setCount
        }

    let badgeForStats (stats: PropertyStats) : PropertyCountBadge =
        if stats.DistinctValueCount = 1 && stats.SetsWithValueCount = stats.TotalSetCount then
            Hide
        elif stats.DistinctValueCount = 1 && stats.SetsWithValueCount < stats.TotalSetCount then
            Coverage(stats.SetsWithValueCount, stats.TotalSetCount)
        else
            DistinctValues stats.DistinctValueCount

    let headerMatchesProjectedValues
        (searchText: string)
        (property: ProvenancePropertyKey)
        (projectedValues: ProvenancePropertyValue list)
        =
        Search.contains searchText property.Header.Category.Name
        || Search.contains searchText property.Header.Kind.Id
        || Search.contains searchText (ProvenanceKind.displayName property.Header.Kind)
        || projectedValues
           |> List.exists (fun propertyValue ->
               Search.contains searchText (Formatting.formatValue propertyValue.Value propertyValue.Unit)
           )

    let valueCountFilterMatches (filter: PropertyValueCountFilter) (badge: PropertyCountBadge) =
        match filter, badge with
        | PropertyValueCountFilter.Any, _ -> true
        | PropertyValueCountFilter.Singleton, PropertyCountBadge.Hide -> true
        | PropertyValueCountFilter.Singleton, PropertyCountBadge.Coverage _ -> true
        | PropertyValueCountFilter.Multiple, PropertyCountBadge.DistinctValues n -> n > 1
        | PropertyValueCountFilter.CoverageGap, PropertyCountBadge.Coverage _ -> true
        | _ -> false

    let originFilterMatches
        (model: ProvenanceModel)
        (filter: PropertyOriginFilter)
        (origins: Set<ProvenancePropertyOrigin>)
        =
        match filter with
        | PropertyOriginFilter.AnyOrigin -> true
        | PropertyOriginFilter.CurrentOnly -> origins |> Set.exists (originIsCurrent model)
        | PropertyOriginFilter.AnyUpstream -> origins |> Set.exists (originIsCurrent model >> not)
        | PropertyOriginFilter.Source sourceId ->
            origins |> Set.exists (fun origin -> (sourceOfOrigin origin).Id = sourceId)

    let private headerNameSortKey (property: ProvenancePropertyKey) =
        property.Header.Category.Name.Trim().ToLowerInvariant(),
        property.Header.Category.Name,
        property.Header.Kind.Id,
        property.OriginSource.Id

    let private setHasHeader property model (set: ProvenanceSet) =
        ProvenanceSet.effectivePropertyValueIds set
        |> List.exists (fun propertyValueId ->
            model.PropertyValues.TryFind propertyValueId
            |> Option.exists (ProvenancePropertyValue.belongsTo property)
        )

    let connectionCountForSideHeader (side: ProvenanceSide) (property: ProvenancePropertyKey) (model: ProvenanceModel) =
        let sets = PropertyRails.setsForSide side model

        model.Connections
        |> Map.toList
        |> List.sumBy (fun (_, connection) ->
            if connection.Source.Id <> model.Source.Id then
                0
            else
                let setId =
                    match side with
                    | ProvenanceSide.Input -> connection.InputSetId
                    | ProvenanceSide.Output -> connection.OutputSetId

                match sets.TryFind setId with
                | Some set when setHasHeader property model set -> 1
                | _ -> 0
        )

    let originForProjectedValue
        (_layerId: ProvenanceLayerId)
        (_side: ProvenanceSide)
        (_session: ProvenanceSession)
        (_uiState: UiState)
        (propertyValue: ProvenancePropertyValue)
        =
        Some propertyValue.Origin

    let sortHeaders
        (sort: PropertySort)
        (statsByHeader: Map<ProvenancePropertyKey, PropertyStats>)
        (connectionCountsByHeader: Map<ProvenancePropertyKey, int>)
        (headers: ProvenancePropertyKey list)
        =
        match sort with
        | PropertySort.ValueCountDesc ->
            headers
            |> List.sortBy (fun header ->
                let count =
                    statsByHeader.TryFind header
                    |> Option.map (fun stats -> stats.DistinctValueCount)
                    |> Option.defaultValue 0

                let name, rawName, kindId, originId = headerNameSortKey header

                -count, name, rawName, kindId, originId
            )
        | PropertySort.NameAsc -> headers |> List.sortBy headerNameSortKey
        | PropertySort.ConnectionCountDesc ->
            headers
            |> List.sortBy (fun header ->
                let count = connectionCountsByHeader.TryFind header |> Option.defaultValue 0

                let name, rawName, kindId, originId = headerNameSortKey header

                -count, name, rawName, kindId, originId
            )

    let originFilterOptions
        (_originByHeader: Map<ProvenancePropertyKey, Set<ProvenancePropertyOrigin>>)
        : PropertyOriginFilter list =
        [
            PropertyOriginFilter.AnyOrigin
            PropertyOriginFilter.CurrentOnly
            PropertyOriginFilter.AnyUpstream
        ]

    let railProjectionWithFilters
        (session: ProvenanceSession)
        (layerId: ProvenanceLayerId)
        (side: ProvenanceSide)
        (model: ProvenanceModel)
        (uiState: UiState)
        : PropertyRails.RailProjection =
        let filters = uiState.Filters

        let inputIndex = PropertyRails.buildSideIndex ProvenanceSide.Input model
        let outputIndex = PropertyRails.buildSideIndex ProvenanceSide.Output model

        let sideIndex =
            if side = ProvenanceSide.Input then
                inputIndex
            else
                outputIndex

        let headers =
            PropertyRails.propertyRailHeadersFromIndexes inputIndex outputIndex layerId side model uiState

        let modelValuesForHeader header =
            sideIndex.ValuesByHeader |> Map.tryFind header |> Option.defaultValue []

        let paletteValuesForHeader property =
            State.Palette.valuesForProperty layerId side property uiState

        let valuesByHeader =
            headers
            |> List.map (fun property ->
                let merged = [
                    yield! modelValuesForHeader property
                    yield! paletteValuesForHeader property
                ]

                property,
                merged
                |> List.groupBy (fun propertyValue -> propertyValue.Value, propertyValue.Unit)
                |> List.map (fun (_, values) -> values |> List.sortBy (fun value -> value.Id) |> List.head)
                |> List.sortBy (fun propertyValue -> Formatting.formatValue propertyValue.Value propertyValue.Unit)
            )
            |> Map.ofList

        let statsByHeader =
            headers
            |> List.map (fun property ->
                property,
                {
                    Property = property
                    DistinctValueCount =
                        sideIndex.DistinctValueCountByHeader
                        |> Map.tryFind property
                        |> Option.defaultValue 0
                    SetsWithValueCount =
                        sideIndex.SetsWithValueCountByHeader
                        |> Map.tryFind property
                        |> Option.defaultValue 0
                    TotalSetCount = sideIndex.SetCount
                }
            )
            |> Map.ofList

        let connectionCountsByHeader =
            let countedHeaders =
                model.Connections
                |> Map.toList
                |> List.collect (fun (_, connection) ->
                    if connection.Source.Id <> model.Source.Id then
                        []
                    else
                        let setId =
                            match side with
                            | ProvenanceSide.Input -> connection.InputSetId
                            | ProvenanceSide.Output -> connection.OutputSetId

                        sideIndex.HeadersBySetId
                        |> Map.tryFind setId
                        |> Option.map Set.toList
                        |> Option.defaultValue []
                )
                |> List.countBy id
                |> Map.ofList

            headers
            |> List.map (fun property -> property, countedHeaders |> Map.tryFind property |> Option.defaultValue 0)
            |> Map.ofList

        let originByHeader =
            headers
            |> List.map (fun property ->
                let origins = [
                    yield!
                        modelValuesForHeader property
                        |> List.map (fun propertyValue -> propertyValue.Origin)
                    yield!
                        paletteValuesForHeader property
                        |> List.map (fun propertyValue -> propertyValue.Origin)
                ]

                property, Set.ofList origins
            )
            |> Map.ofList

        let badgeByHeader = statsByHeader |> Map.map (fun _ stats -> badgeForStats stats)

        let colorContext =
            State.PropertyColors.visibleColorContextForLayer session (Session.layerById layerId session)

        let visibleColorKey property = {
            ContextId = colorContext.Id
            Property = property
        }

        let latestExplicitSourceColor sourceIds =
            sourceIds
            |> List.choose (fun sourceId ->
                uiState.PropertyColors.SourceColorSetOrder
                |> Map.tryFind sourceId
                |> Option.map (fun order -> order, sourceId)
            )
            |> List.sortByDescending fst
            |> List.tryHead
            |> Option.bind (fun (_, sourceId) -> uiState.PropertyColors.SourceColors |> Map.tryFind sourceId)

        let resolvedColorForHeader property origins =
            match
                uiState.PropertyColors.ManualPropertyColors
                |> Map.tryFind (visibleColorKey property)
            with
            | Some color -> Some color
            | None ->
                let sourceIds =
                    origins
                    |> Set.toList
                    |> List.map (sourceOfOrigin >> fun source -> source.Id)
                    |> List.distinct

                match sourceIds with
                | [ sourceId ] -> uiState.PropertyColors.SourceColors |> Map.tryFind sourceId
                | _ ->
                    latestExplicitSourceColor sourceIds
                    |> Option.orElseWith (fun () ->
                        uiState.PropertyColors.SourceColors |> Map.tryFind colorContext.DefaultSourceId
                    )

        let colorByHeader =
            headers
            |> List.map (fun property -> property, resolvedColorForHeader property originByHeader.[property])
            |> Map.ofList

        let filtered =
            headers
            |> List.filter (fun property ->
                let badge = badgeByHeader.[property]
                let values = valuesByHeader.[property]
                let origins = originByHeader.[property]

                headerMatchesProjectedValues filters.SearchText property values
                && valueCountFilterMatches filters.ValueCountFilter badge
                && originFilterMatches model filters.OriginFilter origins
            )

        let defaultSorted =
            sortHeaders filters.PropertySort statsByHeader connectionCountsByHeader filtered

        let sorted =
            match State.RailOrder.tryGet layerId side uiState with
            | Some order -> State.RailOrder.apply order defaultSorted
            | None -> defaultSorted

        let expandedHeaders =
            sorted
            |> List.filter (fun property -> State.PropertyExpansion.isExpanded layerId side property uiState)
            |> Set.ofList

        let canSwitchHeaders =
            sorted
            |> List.filter (fun header -> inputIndex.HeaderSet.Contains header && outputIndex.HeaderSet.Contains header)
            |> Set.ofList

        {
            Headers = sorted
            ValuesByHeader =
                sorted
                |> List.map (fun property -> property, valuesByHeader.[property])
                |> Map.ofList
            StatsByHeader = statsByHeader
            ConnectionCountByHeader = connectionCountsByHeader
            BadgeByHeader = badgeByHeader
            ColorByHeader =
                sorted
                |> List.map (fun property -> property, colorByHeader.[property])
                |> Map.ofList
            OriginByHeader = originByHeader
            OriginFilterOptions = originFilterOptions originByHeader
            ExpandedHeaders = expandedHeaders
            CanSwitchHeaders = canSwitchHeaders
        }

/// Projects the active session layer into renderable groups, connections, and layer commands.
module Display =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
    open Arcitisa.Components.Shared.ProvenanceGrouping.Session
    open Arcitisa.Components.Page.ProvenanceGrouping.Types

    let displayLayer session uiState =
        let layer = Session.activeLayer session
        let inputState = State.Sides.get layer.InputSideId uiState
        let outputState = State.Sides.get layer.OutputSideId uiState

        let assignments =
            [
                yield! inputState.GroupingAssignments
                yield! outputState.GroupingAssignments
            ]
            |> List.distinct

        let inputs =
            displayGroupsForAssignments layer.Model ProvenanceSide.Input assignments

        let outputs =
            displayGroupsForAssignments layer.Model ProvenanceSide.Output assignments

        layer, inputs, outputs, displayConnections layer.Model inputs outputs

    let setsInGroups layerId (groups: DisplayGroup list) selectedIds =
        groups
        |> List.filter (fun (group: DisplayGroup) -> selectedIds |> Set.contains (layerId, group.Id))
        |> List.collect (fun (group: DisplayGroup) ->
            group.Members |> List.map (fun (member': DisplayMember) -> member'.SetId)
        )
        |> List.distinct

    let layerCommand name layerId inputGroups outputGroups uiState =
        let inputs =
            setsInGroups layerId inputGroups uiState.SelectedInputs
            |> List.map (fun id -> ProvenanceSide.Input, id)

        let outputs =
            setsInGroups layerId outputGroups uiState.SelectedOutputs
            |> List.map (fun id -> ProvenanceSide.Output, id)

        {
            AddLayerCommand.Name = name
            AddLayerCommand.SelectedSets = inputs @ outputs
        }

    let sortGroups (sort: GroupSort) (connections: DisplayConnection list) (groups: DisplayGroup list) =
        match sort with
        | GroupSort.NameAsc -> groups |> List.sortBy (fun group -> group.Id)
        | GroupSort.MemberCountDesc -> groups |> List.sortByDescending (fun group -> group.Members.Length)
        | GroupSort.ConnectionCountDesc ->
            groups
            |> List.sortByDescending (fun group ->
                connections
                |> List.sumBy (fun connection ->
                    if connection.SourceGroupId = group.Id || connection.TargetGroupId = group.Id then
                        connection.ConnectionIds.Length
                    else
                        0
                )
            )
