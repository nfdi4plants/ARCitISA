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

type EditorLookups = {
    FindGroup: ProvenanceSide -> string -> DisplayGroup option
    FindProperty: string -> ProvenancePropertyKey option
    FindPropertyValue: ProvenancePropertyValueId -> ProvenancePropertyValue option
    SourceForValue: ProvenancePropertyValueId -> ProvenancePropertyValue -> ValueAssignmentSource
}

type DragContext = {
    Session: ProvenanceSession
    Layer: ProvenanceLayer
    UiState: UiState
    GetUiState: unit -> UiState
    Publish: SessionResult -> unit
    SetUiState: UiState -> unit
    Lookups: EditorLookups
    ConnectSetPairs: (ProvenanceSetId * ProvenanceSetId) list -> unit
}

type ActiveDrag = {
    Payload: DragDrop.Payload
    Label: string option
}

type PropertyShelfItemPayload = {
    Property: ProvenancePropertyKey
    SourceSide: ProvenanceSide
}

/// Resolves drag payload ids and display ids against the active layer and UI palette.
module EditorLookups =

    let create layer uiState inputGroups outputGroups =
        let findGroup side groupId =
            let groups: DisplayGroup list =
                if side = ProvenanceSide.Input then
                    inputGroups
                else
                    outputGroups

            groups |> List.tryFind (fun (group: DisplayGroup) -> group.Id = groupId)

        // Built lazily and at most once per lookups instance; drag handlers call
        // FindHeader repeatedly and must not rescan the whole model each time.
        let knownProperties =
            lazy
                ([
                    yield! PropertyRails.headersForModel layer.Model
                    yield! State.Palette.propertiesForSide layer.Id ProvenanceSide.Input uiState
                    yield! State.Palette.propertiesForSide layer.Id ProvenanceSide.Output uiState
                 ]
                 |> List.distinct)

        let findProperty propertyId =
            knownProperties.Value
            |> List.tryFind (fun property -> DragDrop.propertyKeyIdentity property = propertyId)

        let findPropertyValue propertyValueId =
            layer.Model.PropertyValues.TryFind propertyValueId
            |> Option.orElseWith (fun () -> State.Palette.tryFindValue propertyValueId uiState)

        let sourceForValue propertyValueId (propertyValue: ProvenancePropertyValue) : ValueAssignmentSource = {
            CopiedFrom =
                if layer.Model.PropertyValues.ContainsKey propertyValueId then
                    Some propertyValueId
                else
                    None
            Property = ProvenancePropertyValue.propertyKey propertyValue
            Value = propertyValue.Value
            Unit = propertyValue.Unit
        }

        {
            FindGroup = findGroup
            FindProperty = findProperty
            FindPropertyValue = findPropertyValue
            SourceForValue = sourceForValue
        }

/// User-facing messages for session and edit failures shown in the error alert,
/// instead of raw union case dumps.
module SessionErrors =

    let private editText error =
        match error with
        | EditError.PropertyNotFound _ ->
            "This annotation value no longer exists. It may have been changed by a newer edit."
        | EditError.SetNotFound setId -> $"The entity '{setId}' no longer exists in this layer."
        | EditError.ConnectionNotFound _ -> "This connection no longer exists."
        | EditError.TableNotLoaded tableName -> $"The table '{tableName}' is not loaded."
        | EditError.MissingSourceAnchor _ ->
            "This value cannot be edited because its location in the source table is unknown."
        | EditError.DuplicateHeader(tableName, header) ->
            $"'{header.Category.Name}' already exists in table '{tableName}'."
        | EditError.PreviousContextCreationNotAllowed tableName ->
            $"Entries from the upstream table '{tableName}' are read-only here; edit them in their own table."
        | EditError.PreviousContextConnectionCreationNotAllowed tableName ->
            $"Connections cannot be created on the upstream table '{tableName}' from here."

    let text error =
        match error with
        | SessionError.LayerNotFound layerId -> $"The layer '{layerId}' no longer exists."
        | SessionError.SetNotFound reference -> $"The entity '{reference.SetId}' no longer exists in this layer."
        | SessionError.EditFailed editError -> editText editError

/// User-facing messages for value assignment planning failures.
module AssignmentErrors =

    let text error =
        match error with
        | ValueAssignmentError.EmptyTarget -> "Drop a value onto a group with at least one entity."
        | ValueAssignmentError.MixedPropertyValueCounts property ->
            $"Cannot assign {property.Header.Category.Name}: every target must either have no value or exactly one value for this annotation."
        | ValueAssignmentError.MultiplePropertyValues(property, setIds) ->
            let targets = setIds |> String.concat ", "
            $"Cannot overwrite {property.Header.Category.Name}: {targets} hold more than one distinct value for this annotation, so no single value can be replaced."
        | ValueAssignmentError.UpstreamPropertyNotAssigned property ->
            $"Cannot assign {property.Header.Category.Name} to a new entity in this layer. This annotation originated in '{property.OriginSource.Name}' and can only replace an existing assignment."

/// Session-changing actions that publish patches back to the host component.
module EditorActions =

    let addLayer session layerId inputGroups outputGroups uiState name publish =
        Display.layerCommand name layerId inputGroups outputGroups uiState
        |> fun command -> Session.addLayer command session
        |> publish

    let createSet session publish command =
        Session.createLoadedSet command session |> publish

    let private applyOverwrite result warning =
        warning.ExistingValueIds
        |> List.distinct
        |> List.fold
            (fun result propertyValueId ->
                result
                |> Result.bind (fun (currentSession, patches) ->
                    Session.updatePropertyValue propertyValueId warning.Value warning.Unit currentSession
                    |> Result.map (fun (next, added) -> next, patches @ added)
                )
            )
            result

    let private applyAdd result command =
        result
        |> Result.bind (fun (currentSession, patches) ->
            Session.createLoadedPropertyValue command currentSession
            |> Result.map (fun (next, added) -> next, patches @ added)
        )

    let applyAssignmentBatch session publish batch =
        batch.Overwrites
        |> List.fold applyOverwrite (Ok(session, []))
        |> Result.bind (fun afterOverwrites -> batch.Adds |> List.fold applyAdd (Ok afterOverwrites))
        |> publish

    let connectSetPairs session publish pairs =
        pairs
        |> List.distinct
        |> List.fold
            (fun (result: SessionResult) (inputId, outputId) ->
                result
                |> Result.bind (fun (current, patches) ->
                    Session.connectSets inputId outputId None current
                    |> Result.map (fun (next, added) -> next, patches @ added)
                )
            )
            (Ok(session, []))
        |> publish

    let orderedMemberPairs (inputGroup: DisplayGroup) (outputGroup: DisplayGroup) =
        if inputGroup.Members.Length = outputGroup.Members.Length then
            List.zip inputGroup.Members outputGroup.Members
            |> List.map (fun (input, output) -> input.SetId, output.SetId)
            |> Some
        else
            None

    let allMemberPairs (inputGroup: DisplayGroup) (outputGroup: DisplayGroup) = [
        for input in inputGroup.Members do
            for output in outputGroup.Members do
                input.SetId, output.SetId
    ]

/// Reads the current DOM position of a connection handle relative to the editor surface.
module HandleMeasure =

    let private tryDocumentNode (handle: ConnectionHandleRef) =
        let node: Browser.Types.HTMLElement =
            !!Browser.Dom.document.querySelector($"[data-provenance-connection-node='{DragDrop.connectionHandleNodeId handle}']")

        if isNull node then None else Some node

    let tryCenter (surface: Browser.Types.HTMLElement) (handle: ConnectionHandleRef) =
        match tryDocumentNode handle with
        | Some node ->
            let origin = surface.getBoundingClientRect ()
            let rect = node.getBoundingClientRect ()

            Some {
                X = rect.left - origin.left + float surface.scrollLeft + rect.width / 2.
                Y = rect.top - origin.top + float surface.scrollTop + rect.height / 2.
            }
        | None -> None

    let tryViewportCenter (handle: ConnectionHandleRef) =
        tryDocumentNode handle
        |> Option.map (fun node ->
            let rect = node.getBoundingClientRect ()

            {
                X = rect.left + rect.width / 2.
                Y = rect.top + rect.height / 2.
            }
        )

/// Resolves destination handles from final pointer coordinates when DnD reports
/// the active handle as its own drop target.
module DropHitTesting =

    [<Emit("document.elementsFromPoint($0, $1)")>]
    let private elementsFromPoint (_x: float) (_y: float) : Browser.Types.HTMLElement[] = jsNative

    [<Emit("$0.closest($1)")>]
    let private closest (_element: Browser.Types.HTMLElement) (_selector: string) : Browser.Types.HTMLElement = jsNative

    let private attribute name (element: Browser.Types.HTMLElement) =
        let value = element.getAttribute name
        if isNull value then None else Some value

    let private closestAttribute selector attributeName (element: Browser.Types.HTMLElement) =
        let node = closest element selector
        if isNull node then None else attribute attributeName node

    let private endpoint source (event: DndKit.IDndKitEvent) =
        HandleMeasure.tryViewportCenter source
        |> Option.map (fun start -> {
            X = start.X + event.delta.x
            Y = start.Y + event.delta.y
        })

    let private targetHandleAt point source =
        elementsFromPoint point.X point.Y
        |> Array.tryPick (fun element ->
            closestAttribute "[data-provenance-connection-drop-id]" "data-provenance-connection-drop-id" element
            |> Option.bind DragDrop.tryConnectionDropId
            |> Option.bind (fun target -> if target = source then None else Some target)
        )

    let connectionTarget source event =
        endpoint source event |> Option.bind (fun point -> targetHandleAt point source)

/// DnD event handlers that translate library events into session or UI state changes.
module DragHandlers =

    let private activeLabel (event: DndKit.IDndKitEvent) =
        if
            isNull event.active
            || isNull event.active.data
            || isNull event.active.data.current
        then
            None
        else
            let labelObj: obj = event.active.data.current?label

            if isNull labelObj then
                None
            else
                let label = string labelObj

                if String.IsNullOrWhiteSpace label || label = "undefined" then
                    None
                else
                    Some label

    let handleStart
        (surfaceRef: IRefValue<Browser.Types.HTMLElement option>)
        setActiveDrag
        (liveDragStore: LiveDrag.Store)
        (event: DndKit.IDndKitEvent)
        =
        let payload = DragDrop.tryDragId (string event.active.id)

        setActiveDrag (
            payload
            |> Option.map (fun payload -> {
                Payload = payload
                Label = activeLabel event
            })
        )

        match payload, surfaceRef.current with
        | Some(DragDrop.Payload.ConnectionHandle handle), Some surface ->
            HandleMeasure.tryCenter surface handle
            |> Option.iter (fun point -> LiveDrag.start handle point liveDragStore)
        | _ -> ()

    let handleMove (liveDragStore: LiveDrag.Store) (event: DndKit.IDndKitMoveEvent) =
        match liveDragStore.Current with
        | Some live ->
            LiveDrag.moveTo
                {
                    X = live.Start.X + event.delta.x
                    Y = live.Start.Y + event.delta.y
                }
                liveDragStore
        | None -> ()

    let private layerIdForSide (layer: ProvenanceLayer) side =
        match side with
        | ProvenanceSide.Input -> layer.InputSideId
        | ProvenanceSide.Output -> layer.OutputSideId

    /// Pulses the card a value was just dropped on and flashes the organizer tab the
    /// value produced. Scheduled one frame after the publish so it targets the
    /// re-rendered DOM. Dropping a grouping value usually moves the members into a
    /// differently-keyed card, so when the original card is gone the tab lookup finds
    /// (and pulses) their new home instead.
    let private pulseDropTarget side groupId (propertyValue: ProvenancePropertyValue) =
        Motion.requestFrame (fun () ->
            let cardNode: Browser.Types.HTMLElement =
                !!
                    Browser.Dom.document.querySelector
                    ($"[data-provenance-group-node='{DragDrop.groupNodeId side groupId}']")

            let mutable pulsedCard = false

            if not (isNull cardNode) then
                Motion.pulse cardNode
                pulsedCard <- true

            let identity =
                DragDrop.groupingValueIdentity
                    (ProvenancePropertyValue.propertyKey propertyValue)
                    propertyValue.Value
                    propertyValue.Unit

            let sidePrefix = $"provenance-node::{side}::"

            let tabs =
                Motion.queryAll Browser.Dom.document.body ($"[data-provenance-grouping-value='{identity}']")

            for tab in tabs do
                let card = Motion.closest tab "[data-provenance-group-node]"

                if
                    not (isNull card)
                    && (card.getAttribute "data-provenance-group-node").StartsWith sidePrefix
                then
                    Motion.flash tab

                    if not pulsedCard then
                        Motion.pulse (unbox card)
                        pulsedCard <- true
        )
        |> ignore

    /// Plans and applies one property value against explicit target groups; drops
    /// and click-to-apply share this path so both get the same confirmations.
    let private applyPropertyValueToGroups
        context
        (propertyValue: ProvenancePropertyValue)
        propertyValueId
        (targetGroups: DisplayGroup list)
        (pulseTarget: (ProvenanceSide * string) option)
        =
        let uiState = context.GetUiState()

        match
            ValueAssignment.planPropertyValueDropToGroups
                (context.Lookups.SourceForValue propertyValueId propertyValue)
                targetGroups
                context.Layer.Model
        with
        | Ok batch ->
            let affectedValueCount =
                batch.Overwrites |> List.sumBy (fun w -> w.ExistingValueIds.Length)

            let sideForTarget target =
                match target with
                | ProvenancePropertyTarget.InputSets _ -> Some ProvenanceSide.Input
                | ProvenancePropertyTarget.OutputSets _ -> Some ProvenanceSide.Output
                | ProvenancePropertyTarget.Connections _ -> None

            let setIdsForTarget target =
                match target with
                | ProvenancePropertyTarget.InputSets ids -> ids |> List.map (fun id -> ProvenanceSide.Input, id)
                | ProvenancePropertyTarget.OutputSets ids -> ids |> List.map (fun id -> ProvenanceSide.Output, id)
                | ProvenancePropertyTarget.Connections _ -> []

            let affectedSideCount =
                [
                    yield! batch.Adds |> List.choose (fun command -> sideForTarget command.Target)
                    yield! batch.Overwrites |> List.choose (fun warning -> sideForTarget warning.Target)
                ]
                |> List.distinct
                |> List.length

            let affectedEntityCount =
                [
                    yield! batch.Adds |> List.collect (fun command -> setIdsForTarget command.Target)
                    yield! batch.Overwrites |> List.collect (fun warning -> setIdsForTarget warning.Target)
                ]
                |> List.distinct
                |> List.length

            let pendingBatch = {
                Batch = batch
                AffectedSideCount = affectedSideCount
                AffectedValueCount = affectedValueCount
                AffectedGroupCount = targetGroups.Length
                AffectedEntityCount = affectedEntityCount
            }

            // A drop that fans out beyond the card under the pointer (via selected
            // groups) is confirmed first, even when nothing would be overwritten.
            if batch.Overwrites.IsEmpty && targetGroups.Length <= 1 then
                if not batch.Adds.IsEmpty then
                    let result =
                        batch.Adds
                        |> List.fold
                            (fun (result: SessionResult) cmd ->
                                match result with
                                | Ok(current, patches) ->
                                    Session.createCurrentLoadedPropertyValue cmd current
                                    |> Result.map (fun (next, added) -> next, patches @ added)
                                | Error _ -> result
                            )
                            (Ok(context.Session, []))

                    context.Publish result

                    match result, pulseTarget with
                    | Ok _, Some(side, groupId) -> pulseDropTarget side groupId propertyValue
                    | _ -> ()
            else
                State.AssignmentBatch.set pendingBatch uiState |> context.SetUiState
        | Error error ->
            context.SetUiState {
                uiState with
                    Error = Some(AssignmentErrors.text error)
            }

    let private routePropertyValueDrop context side groupId propertyValueId =
        match context.Lookups.FindPropertyValue propertyValueId with
        | Some propertyValue ->
            let uiState = context.GetUiState()

            let targetGroups =
                ValueAssignment.selectedTargetGroupsForDrop
                    context.Layer.Id
                    side
                    groupId
                    uiState.SelectedInputs
                    uiState.SelectedOutputs
                    context.Lookups.FindGroup

            applyPropertyValueToGroups context propertyValue propertyValueId targetGroups (Some(side, groupId))
        | _ -> ()

    /// Click-to-apply alternative to dropping a value chip: applies the value to
    /// every group currently selected on the active layer.
    let applyPropertyValueToSelection context propertyValueId =
        match context.Lookups.FindPropertyValue propertyValueId with
        | Some propertyValue ->
            let uiState = context.GetUiState()
            let layerId = context.Layer.Id

            let groupsFor side (selected: Set<ProvenanceLayerId * string>) =
                selected
                |> Set.toList
                |> List.choose (fun (currentLayerId, id) ->
                    if currentLayerId = layerId then
                        context.Lookups.FindGroup side id
                    else
                        None
                )

            let targetGroups = [
                yield! groupsFor ProvenanceSide.Input uiState.SelectedInputs
                yield! groupsFor ProvenanceSide.Output uiState.SelectedOutputs
            ]

            if not targetGroups.IsEmpty then
                applyPropertyValueToGroups context propertyValue propertyValueId targetGroups None
        | None -> ()

    let private routeGroupConnection context inputGroupId outputGroupId =
        match
            context.Lookups.FindGroup ProvenanceSide.Input inputGroupId,
            context.Lookups.FindGroup ProvenanceSide.Output outputGroupId
        with
        | Some inputGroup, Some outputGroup ->
            // Only a 1×1 connection is unambiguous. Equal counts could be paired by
            // order, but that is a guess about intent, so the user picks the mapping.
            if inputGroup.Members.Length = 1 && outputGroup.Members.Length = 1 then
                match EditorActions.orderedMemberPairs inputGroup outputGroup with
                | Some pairs -> context.ConnectSetPairs pairs
                | None -> ()
            else
                State.MemberResolution.request
                    {
                        LayerId = context.Layer.Id
                        InputGroupId = inputGroup.Id
                        OutputGroupId = outputGroup.Id
                        InputMemberCount = inputGroup.Members.Length
                        OutputMemberCount = outputGroup.Members.Length
                    }
                    (context.GetUiState())
                |> context.SetUiState
        | _ -> ()

    let private routeMemberToGroupConnection context inputGroupId outputGroupId memberSetId memberSide =
        match
            context.Lookups.FindGroup ProvenanceSide.Input inputGroupId,
            context.Lookups.FindGroup ProvenanceSide.Output outputGroupId
        with
        | Some inputGroup, Some outputGroup ->
            let pairs =
                match memberSide with
                | ProvenanceSide.Input -> outputGroup.Members |> List.map (fun output -> memberSetId, output.SetId)
                | ProvenanceSide.Output -> inputGroup.Members |> List.map (fun input -> input.SetId, memberSetId)

            context.ConnectSetPairs pairs
        | _ -> ()

    /// Routes a source/target handle pair to the connection action it implies; also
    /// used by click-to-connect, which pairs handles without a drag.
    let routeConnectionHandle context source target =
        match ConnectionRouting.action source target with
        | Some(ConnectionRouting.ConnectionAction.ConnectGroups(inputGroupId, outputGroupId)) ->
            routeGroupConnection context inputGroupId outputGroupId
        | Some(ConnectionRouting.ConnectionAction.ConnectMembers(_, _, inputSetId, outputSetId)) ->
            context.ConnectSetPairs [ inputSetId, outputSetId ]
        | Some(ConnectionRouting.ConnectionAction.ConnectMemberToGroup(inputGroupId,
                                                                       outputGroupId,
                                                                       memberSetId,
                                                                       memberSide)) ->
            routeMemberToGroupConnection context inputGroupId outputGroupId memberSetId memberSide
        | None -> ()

    let private routeExistingValueAndPropertyDrags context dragPayload groupDrop propertyDrop =
        match dragPayload, groupDrop, propertyDrop with
        | Some(DragDrop.Payload.PropertyValue propertyValueId), Some(side, groupId), _ ->
            routePropertyValueDrop context side groupId propertyValueId
        | Some(DragDrop.Payload.FolderPropertyHeader(sourceSide, headerId)), _, Some targetSide ->
            match context.Lookups.FindProperty headerId with
            | Some property when
                sourceSide = targetSide
                || PropertyRails.canSwitchHeader property context.Layer.Model
                ->
                context.GetUiState()
                |> State.PropertyPlacement.place context.Layer.Id targetSide property
                |> context.SetUiState
            | _ -> ()
        | Some(DragDrop.Payload.PropertyHeader(sourceSide, headerId)), _, Some targetSide when sourceSide <> targetSide ->
            match context.Lookups.FindProperty headerId with
            | Some property when PropertyRails.canSwitchHeader property context.Layer.Model ->
                State.GroupingAssignments.move
                    context.Layer.Id
                    (layerIdForSide context.Layer sourceSide)
                    (layerIdForSide context.Layer targetSide)
                    targetSide
                    property
                    (context.GetUiState())
                |> context.SetUiState
            | _ -> ()
        | _ -> ()

    let handleEnd context (event: DndKit.IDndKitEvent) =
        let dragPayload = DragDrop.tryDragId (string event.active.id)

        let groupDrop, propertyDrop, connectionDrop =
            if isNull event.over then
                None, None, None
            else
                DragDrop.tryDropId (string event.over.id),
                DragDrop.tryPropertyDropId (string event.over.id),
                DragDrop.tryConnectionDropId (string event.over.id)

        match dragPayload, connectionDrop with
        | Some(DragDrop.Payload.ConnectionHandle source), Some target ->
            let resolvedTarget =
                if target = source then
                    DropHitTesting.connectionTarget source event
                else
                    Some target

            resolvedTarget |> Option.iter (routeConnectionHandle context source)
        | Some(DragDrop.Payload.ConnectionHandle source), None ->
            DropHitTesting.connectionTarget source event
            |> Option.iter (routeConnectionHandle context source)
        | _ -> routeExistingValueAndPropertyDrags context dragPayload groupDrop propertyDrop
