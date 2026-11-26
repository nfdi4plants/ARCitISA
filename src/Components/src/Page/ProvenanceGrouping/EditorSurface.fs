namespace Arcitisa.Components.Page.ProvenanceGrouping

open System
open System.Globalization
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Composite.FolderedDraggableList
open Arcitisa.Components.Composite.FolderedDraggableList.Types
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

/// Render helpers for side rails, group columns, and drag overlays.
module EditorSurface =

    let dropZoneProjection layerId side uiState activeAssignments (projection: PropertyRails.RailProjection) =
        let activeHeaders =
            [
                yield!
                    activeAssignments
                    |> List.map (fun (assignment: GroupingAssignment) -> assignment.Key)
                yield!
                    uiState.PropertyRailPlacements
                    |> Map.toList
                    |> List.choose (fun ((currentLayerId, key), placedSide) ->
                        if currentLayerId = layerId && placedSide = side then
                            Some key
                        else
                            None
                    )
            ]
            |> Set.ofList

        let filterMap map =
            map |> Map.filter (fun header _ -> activeHeaders.Contains header)

        {
            projection with
                Headers = projection.Headers |> List.filter (fun header -> activeHeaders.Contains header)
                ValuesByHeader = projection.ValuesByHeader |> filterMap
                ExpandedHeaders =
                    projection.ExpandedHeaders
                    |> Set.filter (fun header -> activeHeaders.Contains header)
                CanSwitchHeaders =
                    projection.CanSwitchHeaders
                    |> Set.filter (fun header -> activeHeaders.Contains header)
                StatsByHeader = projection.StatsByHeader |> filterMap
                ConnectionCountByHeader = projection.ConnectionCountByHeader |> filterMap
                BadgeByHeader = projection.BadgeByHeader |> filterMap
                ColorByHeader = projection.ColorByHeader |> filterMap
                OriginByHeader = projection.OriginByHeader |> filterMap
        }

    let propertyRail
        side
        activeSource
        sideId
        (projection: PropertyRails.RailProjection)
        activeAssignments
        toggleSide
        toggleBoth
        move
        toggleExpanded
        addPaletteValue
        setPropertyColor
        sourceInfoForValue
        (isUnassignedValue: Arcitisa.Components.Shared.ProvenanceGrouping.Types.ProvenancePropertyValue -> bool)
        (onApplyValueToSelection:
            (Arcitisa.Components.Shared.ProvenanceGrouping.Types.ProvenancePropertyValue -> unit) option)
        (applySelectionLabel: string)
        isDropRejected
        isDropAvailable
        debug
        setIsValueChipDragging

        =
        Controls.PropertyRail(
            side,
            activeSource,
            projection.Headers,
            activeAssignments,
            (fun header -> projection.ValuesByHeader |> Map.tryFind header |> Option.defaultValue []),
            (fun header -> projection.ExpandedHeaders.Contains header),
            toggleSide,
            toggleBoth,
            move,
            toggleExpanded,
            addPaletteValue,
            (fun header -> projection.CanSwitchHeaders.Contains header),
            isDropRejected,
            isDropAvailable,
            setIsValueChipDragging,
            (fun header -> projection.StatsByHeader |> Map.tryFind header),
            (fun header -> projection.BadgeByHeader |> Map.tryFind header),
            (fun header -> projection.ColorByHeader |> Map.tryFind header |> Option.bind id),
            (fun header -> projection.OriginByHeader |> Map.tryFind header),
            setPropertyColor,
            sourceInfoForValue,
            sideId = sideId,
            isUnassignedValue = isUnassignedValue,
            ?onApplyValueToSelection = onApplyValueToSelection,
            applySelectionLabel = applySelectionLabel,
            debug = debug
        )

    let groupColumn
        side
        (layer: ProvenanceLayer)
        model
        (groups: DisplayGroup list)
        endpointKinds
        existingEndpointNames
        createSet
        uiState
        isExpanded
        toggleSelection
        toggleDetail
        (connectionCountFor: string -> int option)
        sourceInfoForValue
        debug
        isValueChipDragging
        =
        let keyPrefix =
            match side with
            | ProvenanceSide.Input -> "Input"
            | ProvenanceSide.Output -> "Output"

        let endpointKindsKey =
            endpointKinds |> List.map Endpoints.endpointKindIdentity |> String.concat "|"

        let columnClasses =
            [
                "swt:@container/provenancePanel swt:flex swt:min-w-0 swt:flex-col swt:gap-3"
                // Fit-content cards hug the column edge facing their property rail, so
                // the space between the two card columns stays free for group connectors.
                match side with
                | ProvenanceSide.Input -> "swt:items-start"
                | ProvenanceSide.Output -> "swt:items-end"
            ]
            |> String.concat " "

        // The FLIP wrapper animates cards to their new positions when grouping,
        // sorting, or membership changes rearrange the column.
        FlipColumn.View(
            columnClasses,
            "data-provenance-group-node",
            [
                Controls.AddEndpointPopover(
                    side,
                    endpointKinds,
                    existingEndpointNames,
                    createSet,
                    debug = debug,
                    key = $"{layer.Id}:{keyPrefix}:{endpointKindsKey}"
                )
                for group in groups do
                    GroupCard.Main(
                        side,
                        group,
                        model,
                        State.Selection.contains layer.Id side group.Id uiState,
                        isExpanded side group.Id,
                        (fun () -> toggleSelection side group.Id),
                        (fun () -> toggleDetail side group.Id),
                        isValueChipDragging,
                        ?connectionCount = connectionCountFor group.Id,
                        sourceInfoForValue = sourceInfoForValue,
                        debug = debug,
                        key = $"{keyPrefix}:{group.Id}"
                    )
                if groups.IsEmpty then
                    Html.p [
                        prop.className "swt:text-sm swt:text-base-content/60"
                        prop.text "No entries in this layer"
                    ]
            ]
        )

    let dragOverlay findPropertyValue debug (activeDrag: ActiveDrag option) =
        match activeDrag with
        | Some {
                   Payload = DragDrop.Payload.PropertyValue propertyValueId
               } ->
            match findPropertyValue propertyValueId with
            | Some propertyValue -> Controls.ValueDragPreview(propertyValue, showHeader = false, debug = debug)
            | None -> Html.none
        | Some {
                   Payload = DragDrop.Payload.PropertyHeader _
                   Label = label
               } ->
            Html.div [
                prop.className Styles.propertyValueOverlayClasses
                if debug then
                    prop.testId "provenance-drag-overlay-property"
                prop.children [
                    Html.span [
                        prop.className "swt:min-w-0 swt:truncate"
                        prop.text (label |> Option.defaultValue "Annotation")
                    ]
                ]
            ]
        | _ -> Html.none
