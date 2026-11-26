module Arcitisa.Components.Page.ProvenanceGrouping.State

open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

/// Shared key helpers used by the UI state maps and sets.
module Keys =

    let groupingKey (property: ProvenancePropertyKey) : GroupingKey = property

    let propertyKey originSource header : ProvenancePropertyKey = {
        Header = header
        OriginSource = originSource
    }

    let propertySlot layerId side property = layerId, side, groupingKey property

    let paletteKey layerId side = layerId, side

    let selectedGroup layerId groupId = layerId, groupId

/// Stores and clamps the three-panel editor layout ratios.
module PanelLayout =

    let minimumSide = 15
    let minimumMiddle = 30

    let defaultRatios = { Left = 20; Middle = 60; Right = 20 }

    let private clamp left right =
        let left = left |> max minimumSide |> min (100 - minimumMiddle - minimumSide)

        let right = right |> max minimumSide |> min (100 - minimumMiddle - left)

        {
            Left = left
            Middle = 100 - left - right
            Right = right
        }

    /// Clamps raw percentages with the same rules the stored ratios use, so live
    /// drag previews land exactly where the committed state will.
    let clamped left right = clamp left right

    let get layerId state =
        state.PanelRatios |> Map.tryFind layerId |> Option.defaultValue defaultRatios

    let set layerId ratios state = {
        state with
            PanelRatios = state.PanelRatios |> Map.add layerId (clamp ratios.Left ratios.Right)
    }

    let setLeft layerId left state =
        let current = get layerId state
        set layerId { current with Left = left } state

    let setRight layerId right state =
        let current = get layerId state
        set layerId { current with Right = right } state

/// Tracks the in-app prompt and expansion state for mismatched group connections.
module MemberResolution =

    let request (pending: PendingMemberResolution) state = {
        state with
            PendingMemberResolution = Some pending
            Error = None
    }

    let clearPending state = {
        state with
            PendingMemberResolution = None
    }

    let chooseManual (pending: PendingMemberResolution) state = {
        state with
            PendingMemberResolution = None
            // Exactly the two cards that were about to be connected open, so the
            // member handles the user needs next are the ones on screen.
            ExpandedGroups =
                Set.ofList [
                    ProvenanceSide.Input, pending.InputGroupId
                    ProvenanceSide.Output, pending.OutputGroupId
                ]
            Detail = None
            Hint =
                Some
                    "Drag from a member's connection handle to a member or group on the other side (or tap both handles) to connect them individually."
    }

/// One-line follow-up guidance shown after actions that need a next step.
module Hint =

    let clear state = { state with Hint = None }

module PropertyColors =

    let palette = [|
        "#2563eb"
        "#16a34a"
        "#d97706"
        "#dc2626"
        "#7c3aed"
        "#0891b2"
        "#be185d"
    |]

    let empty = {
        ManualPropertyColors = Map.empty
        SourceColors = Map.empty
        SourceColorSetOrder = Map.empty
        NextSourceColorSetOrder = 0
    }

    let private automaticColorForLayer layerIndex = palette.[layerIndex % palette.Length]

    let private layerOrderIndex session layerId =
        session.LayerOrder
        |> List.tryFindIndex ((=) layerId)
        |> Option.defaultValue System.Int32.MaxValue

    let visibleColorContextForLayer (session: ProvenanceSession) (layer: ProvenanceLayer) =
        let incomingByTargetLayer =
            session.ReferenceLinks
            |> List.groupBy (fun link -> link.Target.LayerId)
            |> Map.ofList

        let rec rootLayerId currentLayerId visited =
            if visited |> Set.contains currentLayerId then
                currentLayerId
            else
                match incomingByTargetLayer |> Map.tryFind currentLayerId with
                | None -> currentLayerId
                | Some links ->
                    links
                    |> List.map (fun link -> link.Source.LayerId)
                    |> List.distinct
                    |> List.sortBy (layerOrderIndex session)
                    |> List.tryHead
                    |> Option.map (fun sourceLayerId -> rootLayerId sourceLayerId (visited |> Set.add currentLayerId))
                    |> Option.defaultValue currentLayerId

        let rootId = rootLayerId layer.Id Set.empty
        let rootLayer = Session.layerById rootId session

        {
            Id = rootLayer.Id
            DefaultSourceId = rootLayer.Model.Source.Id
        }

    let private visiblePropertyColorKey contextId property = {
        ContextId = contextId
        Property = property
    }

    let setColor contextId (property: ProvenancePropertyKey) color state =
        let key = visiblePropertyColorKey contextId property

        {
            state with
                PropertyColors = {
                    state.PropertyColors with
                        ManualPropertyColors = state.PropertyColors.ManualPropertyColors |> Map.add key color
                }
        }

    let clearColor contextId (property: ProvenancePropertyKey) state =
        let key = visiblePropertyColorKey contextId property

        {
            state with
                PropertyColors = {
                    state.PropertyColors with
                        ManualPropertyColors = state.PropertyColors.ManualPropertyColors |> Map.remove key
                }
        }

    let setSourceColor (sourceId: ProvenanceSourceId) color state =
        let setOrder = state.PropertyColors.NextSourceColorSetOrder

        {
            state with
                PropertyColors = {
                    state.PropertyColors with
                        SourceColors = state.PropertyColors.SourceColors |> Map.add sourceId color
                        SourceColorSetOrder = state.PropertyColors.SourceColorSetOrder |> Map.add sourceId setOrder
                        NextSourceColorSetOrder = setOrder + 1
                }
        }

    let clearSourceColor (sourceId: ProvenanceSourceId) state = {
        state with
            PropertyColors = {
                state.PropertyColors with
                    SourceColors = state.PropertyColors.SourceColors |> Map.remove sourceId
                    SourceColorSetOrder = state.PropertyColors.SourceColorSetOrder |> Map.remove sourceId
            }
    }

    let private anchorOfOrigin =
        function
        | ProvenancePropertyOrigin.Real anchor
        | ProvenancePropertyOrigin.Virtual anchor -> anchor

    let private sourceIdOfPropertyValue propertyValue =
        (anchorOfOrigin propertyValue.Origin).Source.Id

    let ensureSourceColors (session: ProvenanceSession) (state: UiState) : PropertyColorSettings =
        let orderedSourceIds =
            [
                for layer in session.Layers do
                    yield layer.Model.Source.Id

                    yield!
                        layer.Model.PropertyValues
                        |> Map.toList
                        |> List.map (snd >> sourceIdOfPropertyValue)

                yield!
                    state.PaletteValues
                    |> Map.toList
                    |> List.collect snd
                    |> List.map sourceIdOfPropertyValue
            ]
            |> List.distinct

        let liveSources = orderedSourceIds |> Set.ofList

        let retained =
            state.PropertyColors.SourceColors
            |> Map.filter (fun sourceId _ -> liveSources.Contains sourceId)

        let retainedSetOrder =
            state.PropertyColors.SourceColorSetOrder
            |> Map.filter (fun sourceId _ -> liveSources.Contains sourceId)

        let withMissingDefaults =
            orderedSourceIds
            |> List.mapi (fun index sourceId -> sourceId, automaticColorForLayer index)
            |> List.fold
                (fun (colors: Map<ProvenanceSourceId, ProvenanceColor>) (sourceId, color) ->
                    if colors.ContainsKey sourceId then
                        colors
                    else
                        colors |> Map.add sourceId color
                )
                retained

        {
            state.PropertyColors with
                SourceColors = withMissingDefaults
                SourceColorSetOrder = retainedSetOrder
        }

module Filters =

    let defaultState = {
        SearchText = ""
        PropertySort = PropertySort.ValueCountDesc
        GroupSort = GroupSort.NameAsc
        ValueCountFilter = PropertyValueCountFilter.Any
        OriginFilter = PropertyOriginFilter.AnyOrigin
    }

    let setSearch text state = {
        state with
            Filters = { state.Filters with SearchText = text }
    }

    let setValueCountFilter filter state = {
        state with
            Filters = {
                state.Filters with
                    ValueCountFilter = filter
            }
    }

    let setOriginFilter filter state = {
        state with
            Filters = {
                state.Filters with
                    OriginFilter = filter
            }
    }

    let setPropertySort sort state = {
        state with
            Filters = {
                state.Filters with
                    PropertySort = sort
            }
    }

    let setGroupSort sort state = {
        state with
            Filters = { state.Filters with GroupSort = sort }
    }

/// Creates, finds, and synchronizes side-local state with the current session.
module Sides =

    let empty = { GroupingAssignments = [] }

    let private sideIdsForSession session =
        session.LayerOrder
        |> List.collect (fun layerId ->
            let layer = Session.layerById layerId session
            [ layer.InputSideId; layer.OutputSideId ]
        )

    let get sideId state =
        state.SideStates |> Map.tryFind sideId |> Option.defaultValue empty

    let private retainCurrentSides session state =
        let currentIds = sideIdsForSession session |> Set.ofList

        let retained: Map<ProvenanceLayerSideId, SideViewState> =
            state.SideStates |> Map.filter (fun id _ -> currentIds.Contains id)

        sideIdsForSession session
        |> List.fold
            (fun (map: Map<ProvenanceLayerSideId, SideViewState>) sideId ->
                if map.ContainsKey sideId then
                    map
                else
                    map |> Map.add sideId empty
            )
            retained

    let ensure (session: ProvenanceSession) state =
        let currentLayerIds = session.LayerOrder |> Set.ofList
        let sideStates = retainCurrentSides session state

        let propertyRailPlacements =
            state.PropertyRailPlacements
            |> Map.filter (fun (layerId, _) _ -> currentLayerIds.Contains layerId)

        let propertyRailOrders =
            state.PropertyRailOrders
            |> Map.filter (fun (layerId, _) _ -> currentLayerIds.Contains layerId)

        let panelRatios =
            state.PanelRatios
            |> Map.filter (fun layerId _ -> currentLayerIds.Contains layerId)

        let expandedProperties =
            state.ExpandedProperties
            |> Set.filter (fun (layerId, _, _) -> currentLayerIds.Contains layerId)

        let paletteValues =
            state.PaletteValues
            |> Map.filter (fun (layerId, _) _ -> currentLayerIds.Contains layerId)

        let pendingMemberResolution =
            state.PendingMemberResolution
            |> Option.filter (fun pending -> currentLayerIds.Contains pending.LayerId)

        let propertyColors = PropertyColors.ensureSourceColors session state

        if
            sideStates = state.SideStates
            && propertyRailPlacements = state.PropertyRailPlacements
            && propertyRailOrders = state.PropertyRailOrders
            && panelRatios = state.PanelRatios
            && expandedProperties = state.ExpandedProperties
            && paletteValues = state.PaletteValues
            && pendingMemberResolution = state.PendingMemberResolution
            && propertyColors = state.PropertyColors
        then
            state
        else
            {
                state with
                    SideStates = sideStates
                    PropertyRailPlacements = propertyRailPlacements
                    PropertyRailOrders = propertyRailOrders
                    PanelRatios = panelRatios
                    ExpandedProperties = expandedProperties
                    PaletteValues = paletteValues
                    PendingMemberResolution = pendingMemberResolution
                    PropertyColors = propertyColors
            }

    let update sideId updateSide state =
        let current = get sideId state
        let next = updateSide current

        {
            state with
                SideStates = state.SideStates |> Map.add sideId next
        }

/// Pure success/error state transitions shared by every session-publishing
/// action (publishResult, removeDisplayConnection, undoLast). Extracted so the
/// error path is unit-testable and guaranteed to clear pending prompts exactly
/// like the success path does - previously the error branch only set `Error`,
/// leaving a stale confirmation prompt (e.g. an overwrite batch) mounted after
/// a failed publish.
module Publish =

    let onSuccess (nextSession: ProvenanceSession) state = {
        Sides.ensure nextSession state with
            Error = None
            Hint = None
            PendingAssignmentBatch = None
            PendingMemberResolution = None
    }

    let onError (message: string) state = {
        state with
            Error = Some message
            PendingAssignmentBatch = None
            PendingMemberResolution = None
    }

/// Stores visual rail order separately from filtering and writeback state.
module RailOrder =

    let private key layerId side = layerId, side

    let private distinctHeaders headers = headers |> List.distinct

    let tryGet layerId side state =
        state.PropertyRailOrders |> Map.tryFind (key layerId side)

    let get layerId side state =
        tryGet layerId side state |> Option.defaultValue []

    let apply (order: ProvenancePropertyKey list) (headers: ProvenancePropertyKey list) =
        let headerSet = headers |> Set.ofList
        let ordered = order |> List.filter (fun header -> headerSet.Contains header)
        let orderedSet = ordered |> Set.ofList

        let appended =
            headers |> List.filter (fun header -> not (orderedSet.Contains header))

        ordered @ appended

    let set layerId side headers state =
        let next = distinctHeaders headers

        {
            state with
                PropertyRailOrders = state.PropertyRailOrders |> Map.add (key layerId side) next
        }

    let ensure layerId side headers state =
        let next =
            match tryGet layerId side state with
            | Some current -> apply current headers
            | None -> distinctHeaders headers

        if tryGet layerId side state = Some next then
            state
        else
            set layerId side next state

    let reorderVisible layerId side sortedVisibleHeaders state =
        let visibleSet = sortedVisibleHeaders |> Set.ofList

        let retainedHidden =
            get layerId side state
            |> List.filter (fun header -> not (visibleSet.Contains header))

        set layerId side (sortedVisibleHeaders @ retainedHidden) state

    let removeHeader layerId side header state =
        let next = get layerId side state |> List.filter ((<>) header)

        set layerId side next state

    let appendHeader layerId side header state =
        let next =
            get layerId side state
            |> List.filter ((<>) header)
            |> fun headers -> headers @ [ header ]

        set layerId side next state

/// Tracks explicit side drop-zone placement without changing grouping selection.
module PropertyPlacement =

    let place layerId side property state =
        let key = Keys.groupingKey property

        {
            state with
                PropertyRailPlacements = state.PropertyRailPlacements |> Map.add (layerId, key) side
                Error = None
        }
        |> RailOrder.appendHeader layerId side property

/// Consolidates a hidden side's switchable annotations onto the still-visible rail.
module SideVisibility =

    /// Real, permanent move (revealing the side does not send them back): every
    /// annotation on the hidden side that the model lets switch sides is relocated
    /// to the visible rail, following the cards that stay on screen. A solo grouping
    /// the header held on the now-hidden side is dropped, since it can no longer be
    /// seen or managed; group-both assignments are cross-side and left intact.
    let consolidateToVisible
        layerId
        (hiddenSide: ProvenanceSide)
        (hiddenSideId: ProvenanceLayerSideId)
        (canSwitch: ProvenancePropertyKey -> bool)
        state
        =
        let visibleSide =
            match hiddenSide with
            | ProvenanceSide.Input -> ProvenanceSide.Output
            | ProvenanceSide.Output -> ProvenanceSide.Input

        let hiddenScope = scopeForSide hiddenSide

        let placedHeaders =
            state.PropertyRailPlacements
            |> Map.toList
            |> List.choose (fun ((placementLayerId, key), side) ->
                if placementLayerId = layerId && side = hiddenSide then
                    Some key
                else
                    None
            )

        let soloGroupedHeaders =
            (Sides.get hiddenSideId state).GroupingAssignments
            |> List.filter (fun assignment -> assignment.Scope = hiddenScope)
            |> List.map (fun assignment -> assignment.Key)

        let headers =
            [ yield! placedHeaders; yield! soloGroupedHeaders ]
            |> List.distinct
            |> List.filter canSwitch

        headers
        |> List.fold
            (fun state property ->
                let key = Keys.groupingKey property

                let withoutSolo =
                    Sides.update
                        hiddenSideId
                        (fun current -> {
                            current with
                                GroupingAssignments =
                                    current.GroupingAssignments
                                    |> List.filter (fun assignment ->
                                        not (assignment.Key = key && assignment.Scope = hiddenScope)
                                    )
                        })
                        state

                {
                    withoutSolo with
                        PropertyRailPlacements =
                            withoutSolo.PropertyRailPlacements |> Map.add (layerId, key) visibleSide
                }
                |> RailOrder.removeHeader layerId hiddenSide property
                |> RailOrder.appendHeader layerId visibleSide property
            )
            state

/// Tracks expanded property value panels on the side rails.
module PropertyExpansion =

    let isExpanded layerId side property state =
        state.ExpandedProperties
        |> Set.contains (Keys.propertySlot layerId side property)

    let toggle layerId side property state =
        let slot = Keys.propertySlot layerId side property

        let expanded =
            if state.ExpandedProperties.Contains slot then
                state.ExpandedProperties.Remove slot
            else
                state.ExpandedProperties.Add slot

        {
            state with
                ExpandedProperties = expanded
        }

/// Stores property values that exist only in the rail until they are applied to a group.
module Palette =

    let valuesForSide layerId side state =
        state.PaletteValues
        |> Map.tryFind (Keys.paletteKey layerId side)
        |> Option.defaultValue []

    let valuesForProperty layerId side property state =
        valuesForSide layerId side state
        |> List.filter (ProvenancePropertyValue.belongsTo property)

    let propertiesForSide layerId side state =
        valuesForSide layerId side state
        |> List.map ProvenancePropertyValue.propertyKey
        |> List.distinct
        |> List.sortBy (fun property -> property.Header.Category.Name, property.OriginSource.Id)

    let tryFindValue propertyValueId state =
        state.PaletteValues
        |> Map.toList
        |> List.collect snd
        |> List.tryFind (fun propertyValue -> propertyValue.Id = propertyValueId)

    let private nextValueId layerId side state =
        let sideText =
            match side with
            | ProvenanceSide.Input -> "input"
            | ProvenanceSide.Output -> "output"

        let existing =
            state.PaletteValues
            |> Map.toList
            |> List.collect snd
            |> List.map (fun propertyValue -> propertyValue.Id)
            |> Set.ofList

        let rec loop index =
            let id = $"palette-{layerId}-{sideText}-{index}"
            if existing.Contains id then loop (index + 1) else id

        loop (existing.Count + 1)

    let addValue layerId side property value unit state =
        let key = Keys.paletteKey layerId side

        let anchor = {
            Source = property.OriginSource
            ProcessId = None
            ProcessName = None
            Header = property.Header
            InputNames = []
            OutputNames = []
        }

        let propertyValue: ProvenancePropertyValue = {
            Id = nextValueId layerId side state
            Header = property.Header
            Value = value
            Unit = unit
            Origin = ProvenancePropertyOrigin.Virtual anchor
        }

        let nextValues = valuesForSide layerId side state @ [ propertyValue ]

        {
            state with
                PaletteValues = state.PaletteValues |> Map.add key nextValues
                PropertyRailPlacements =
                    state.PropertyRailPlacements
                    |> Map.add (layerId, Keys.groupingKey property) side
                ExpandedProperties = state.ExpandedProperties |> Set.add (Keys.propertySlot layerId side property)
                Error = None
        }
        |> RailOrder.appendHeader layerId side property

/// Manages batch assignment confirmation state for dropped property values.
module AssignmentBatch =

    let set (batch: PendingAssignmentBatch) state = {
        state with
            PendingAssignmentBatch = Some batch
            Error = None
    }

    let clear state = {
        state with
            PendingAssignmentBatch = None
    }

/// Updates grouping assignments and side placement for properties.
module GroupingAssignments =

    let private removeProperty property (assignments: GroupingAssignment list) : GroupingAssignment list =
        let key = Keys.groupingKey property
        assignments |> List.filter (fun assignment -> assignment.Key <> key)

    let private removePropertyScope property scope (assignments: GroupingAssignment list) : GroupingAssignment list =
        let key = Keys.groupingKey property

        assignments
        |> List.filter (fun assignment -> assignment.Key <> key || assignment.Scope <> scope)

    let private upsert
        (assignment: GroupingAssignment)
        (assignments: GroupingAssignment list)
        : GroupingAssignment list =
        assignments
        |> List.filter (fun current -> current.Key <> assignment.Key)
        |> fun retained -> retained @ [ assignment ]

    let toggleSide sideId side property state =
        Sides.update
            sideId
            (fun current ->
                let key = Keys.groupingKey property
                let scope = scopeForSide side
                let assignment: GroupingAssignment = { Key = key; Scope = scope }

                let isSelected =
                    current.GroupingAssignments
                    |> List.exists (fun current -> current.Key = key && current.Scope = scope)

                let nextAssignments =
                    if isSelected then
                        removePropertyScope property scope current.GroupingAssignments
                    else
                        upsert assignment current.GroupingAssignments

                {
                    current with
                        GroupingAssignments = nextAssignments
                }
            )
            state

    let toggleBoth inputSideId outputSideId property state =
        let key = Keys.groupingKey property

        let isSelected =
            [ inputSideId; outputSideId ]
            |> List.exists (fun sideId ->
                (Sides.get sideId state).GroupingAssignments
                |> List.exists (fun assignment -> assignment.Key = key && assignment.Scope = GroupingScope.Both)
            )

        let setSide state sideId =
            Sides.update
                sideId
                (fun current ->
                    let nextAssignments =
                        if isSelected then
                            removePropertyScope property GroupingScope.Both current.GroupingAssignments
                        else
                            upsert
                                ({
                                    Key = key
                                    Scope = GroupingScope.Both
                                }
                                : GroupingAssignment)
                                current.GroupingAssignments

                    {
                        current with
                            GroupingAssignments = nextAssignments
                    }
                )
                state

        let withInput = setSide state inputSideId
        setSide withInput outputSideId

    let move layerId sourceSideId targetSideId targetSide property state =
        let key = Keys.groupingKey property

        let sourceSide =
            match targetSide with
            | ProvenanceSide.Input -> ProvenanceSide.Output
            | ProvenanceSide.Output -> ProvenanceSide.Input

        // Switching sides moves the rail control; grouping state travels with the
        // header instead of being force-enabled on the target side.
        let wasGrouped =
            (Sides.get sourceSideId state).GroupingAssignments
            |> List.exists (fun assignment -> assignment.Key = key)

        let withoutSource =
            Sides.update
                sourceSideId
                (fun current -> {
                    current with
                        GroupingAssignments = removeProperty property current.GroupingAssignments
                })
                state

        let withTarget =
            if wasGrouped then
                let targetAssignment: GroupingAssignment = {
                    Key = key
                    Scope = scopeForSide targetSide
                }

                Sides.update
                    targetSideId
                    (fun current -> {
                        current with
                            GroupingAssignments = upsert targetAssignment current.GroupingAssignments
                    })
                    withoutSource
            else
                withoutSource

        {
            withTarget with
                PropertyRailPlacements = withTarget.PropertyRailPlacements |> Map.add (layerId, key) targetSide
        }
        |> RailOrder.removeHeader layerId sourceSide property
        |> RailOrder.appendHeader layerId targetSide property

/// Tracks selected input/output groups for layer creation.
module Selection =

    let clearLayer layerId state =
        let retain (selected: Set<ProvenanceLayerId * string>) =
            selected |> Set.filter (fun (currentLayerId, _) -> currentLayerId <> layerId)

        {
            state with
                SelectedInputs = retain state.SelectedInputs
                SelectedOutputs = retain state.SelectedOutputs
        }

    let contains layerId side groupId state =
        let identity = Keys.selectedGroup layerId groupId

        match side with
        | ProvenanceSide.Input -> state.SelectedInputs.Contains identity
        | ProvenanceSide.Output -> state.SelectedOutputs.Contains identity

    let toggle layerId side groupId state =
        let identity = Keys.selectedGroup layerId groupId

        match side with
        | ProvenanceSide.Input ->
            let selected =
                if state.SelectedInputs.Contains identity then
                    state.SelectedInputs.Remove identity
                else
                    state.SelectedInputs.Add identity

            { state with SelectedInputs = selected }
        | ProvenanceSide.Output ->
            let selected =
                if state.SelectedOutputs.Contains identity then
                    state.SelectedOutputs.Remove identity
                else
                    state.SelectedOutputs.Add identity

            {
                state with
                    SelectedOutputs = selected
            }

/// Tracks the details panel target shown beneath the editor surface.
module Detail =

    let isGroupExpanded side groupId state =
        state.ExpandedGroups |> Set.contains (side, groupId)

    let toggleGroup side groupId state =
        // Collapsing removes just this card; expanding replaces the set so manual
        // toggling keeps the familiar one-open-card behavior.
        let next =
            if isGroupExpanded side groupId state then
                state.ExpandedGroups |> Set.remove (side, groupId)
            else
                Set.singleton (side, groupId)

        {
            state with
                ExpandedGroups = next
                Detail = None
        }

    let showConnection connectionId state = {
        state with
            Detail = Some(ProvenanceDetail.Connection connectionId)
    }

/// Creates the complete UI state for a provenance session.
let init (session: ProvenanceSession) = {
    SideStates =
        session.LayerOrder
        |> List.collect (fun layerId ->
            let layer = Session.layerById layerId session

            [
                layer.InputSideId, Sides.empty
                layer.OutputSideId, Sides.empty
            ]
        )
        |> Map.ofList
    PropertyRailPlacements = Map.empty
    PropertyRailOrders = Map.empty
    ExpandedProperties = Set.empty
    PaletteValues = Map.empty
    PendingAssignmentBatch = None
    PanelRatios = Map.empty
    PendingMemberResolution = None
    SelectedInputs = Set.empty
    SelectedOutputs = Set.empty
    ExpandedGroups = Set.empty
    Detail = None
    Error = None
    Hint = None
    PropertyColors = PropertyColors.empty
    Filters = Filters.defaultState
}
