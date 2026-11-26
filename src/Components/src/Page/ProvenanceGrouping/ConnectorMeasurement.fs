namespace Arcitisa.Components.Page.ProvenanceGrouping

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Browser.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Primitive.ContextMenu
open Arcitisa.Components.Primitive.ContextMenu.Types
open Arcitisa.Components.Page.ProvenanceGrouping.Types

module private ConnectorDom =

    [<Emit("Array.from($0.querySelectorAll($1))")>]
    let querySelectorAll (_container: HTMLElement) (_selector: string) : HTMLElement[] = jsNative

    let connectionNodes (container: HTMLElement) =
        querySelectorAll container "[data-provenance-connection-node]"
        |> Array.choose (fun node ->
            let id = node.getAttribute "data-provenance-connection-node"
            if isNull id then None else Some(id, node)
        )
        |> Map.ofArray

    let groupNodes (container: HTMLElement) =
        querySelectorAll container "[data-provenance-group-node]"
        |> Array.choose (fun node ->
            let id = node.getAttribute "data-provenance-group-node"
            if isNull id then None else Some(id, node)
        )
        |> Map.ofArray

    let memberNodes (container: HTMLElement) =
        querySelectorAll container "[data-provenance-member-node]"
        |> Array.choose (fun node ->
            let id = node.getAttribute "data-provenance-member-node"
            if isNull id then None else Some(id, node)
        )
        |> Map.ofArray

/// Measures connection handles and builds SVG connector paths between them.
module ConnectorMeasure =

    let createContext container = {
        Container = container
        Origin = container.getBoundingClientRect ()
        Nodes = ConnectorDom.connectionNodes container
        GroupNodes = ConnectorDom.groupNodes container
        MemberNodes = ConnectorDom.memberNodes container
    }

    let private tryHandle (context: ConnectorMeasureContext) handle =
        context.Nodes |> Map.tryFind (DragDrop.connectionHandleNodeId handle)

    let private center (context: ConnectorMeasureContext) (node: HTMLElement) =
        let origin = context.Origin
        let rect = node.getBoundingClientRect ()

        {
            X = rect.left - origin.left + float context.Container.scrollLeft + rect.width / 2.
            Y = rect.top - origin.top + float context.Container.scrollTop + rect.height / 2.
        }

    let private controlXs (startX: float) (finishX: float) =
        let deltaX = finishX - startX
        let direction = if deltaX >= 0. then 1. else -1.
        // The bend scales with the horizontal span, so connectors between nearby
        // endpoints stay short stubs instead of looping past their targets.
        let bend = max 8. (abs deltaX / 2.)
        startX + direction * bend, finishX - direction * bend

    let pathBetweenPoints start finish =
        let firstControlX, secondControlX = controlXs start.X finish.X
        Some $"M {start.X} {start.Y} C {firstControlX} {start.Y}, {secondControlX} {finish.Y}, {finish.X} {finish.Y}"

    /// Closed sankey-band outline between two card-edge segments: the top and
    /// bottom curves share the horizontal bend of the centerline bezier, so a
    /// band tapers smoothly between edge shares of different heights.
    let private sankeyRibbonPath
        (sourceX: float)
        (sourceTop: float, sourceBottom: float)
        (targetX: float)
        (targetTop: float, targetBottom: float)
        =
        let firstControlX, secondControlX = controlXs sourceX targetX

        $"M {sourceX} {sourceTop} "
        + $"C {firstControlX} {sourceTop}, {secondControlX} {targetTop}, {targetX} {targetTop} "
        + $"L {targetX} {targetBottom} "
        + $"C {secondControlX} {targetBottom}, {firstControlX} {sourceBottom}, {sourceX} {sourceBottom} Z"

    /// Cubic-bezier midpoint of the same control points pathBetweenPoints uses.
    let midpointBetweenPoints start finish =
        let firstControlX, secondControlX = controlXs start.X finish.X

        {
            X = (start.X + 3. * firstControlX + 3. * secondControlX + finish.X) / 8.
            Y = (start.Y + finish.Y) / 2.
        }

    let pathWithMidpointBetweenHandles context source target =
        match tryHandle context source, tryHandle context target with
        | Some sourceNode, Some targetNode ->
            let start = center context sourceNode
            let finish = center context targetNode

            pathBetweenPoints start finish
            |> Option.map (fun path -> path, midpointBetweenPoints start finish)
        | _ -> None

    /// One sankey ribbon to lay out: the connection key, its endpoints (a
    /// collapsed group card or an expanded card's member row each), and the
    /// weight deciding its share of each endpoint edge.
    type SankeyRibbonRequest = {
        Key: string
        Source: ConnectionHandleRef
        Target: ConnectionHandleRef
        Weight: float
    }

    /// Centerline, ribbon outline and midpoint of one laid-out sankey ribbon.
    type SankeyRibbon = {
        Path: string
        RibbonPath: string
        Midpoint: ConnectionPoint
    }

    type private SankeyRect = {
        Left: float
        Right: float
        Top: float
        Bottom: float
    }

    type private ResolvedRibbon = {
        Request: SankeyRibbonRequest
        SourceNodeId: string
        TargetNodeId: string
        SourceRect: SankeyRect
        TargetRect: SankeyRect
    }

    let private cardRect (context: ConnectorMeasureContext) (node: HTMLElement) : SankeyRect =
        let origin = context.Origin
        let rect = node.getBoundingClientRect ()
        let left = rect.left - origin.left + float context.Container.scrollLeft
        let top = rect.top - origin.top + float context.Container.scrollTop

        {
            Left = left
            Right = left + rect.width
            Top = top
            Bottom = top + rect.height
        }

    /// A ribbon endpoint is the whole card for collapsed groups and the single
    /// member row for expanded ones, so member-level ribbons split row edges
    /// exactly like group ribbons split card edges.
    let private tryRibbonNode (context: ConnectorMeasureContext) (handle: ConnectionHandleRef) =
        match handle.Kind with
        | ConnectionHandleKind.GroupCard ->
            let nodeId = DragDrop.groupNodeId handle.Side handle.Id

            context.GroupNodes.TryFind nodeId |> Option.map (fun node -> nodeId, node)
        | ConnectionHandleKind.GroupMember ->
            handle.ParentGroupId
            |> Option.bind (fun groupId ->
                let nodeId = DragDrop.memberNodeId handle.Side groupId handle.Id

                context.MemberNodes.TryFind nodeId |> Option.map (fun node -> nodeId, node)
            )
        | _ -> None

    /// Lays out group- and member-connection ribbons like a sankey chart: the
    /// ribbons attached to an endpoint (card or member row) split its facing
    /// edge proportionally to their weights, so together they cover the whole
    /// side and follow its size; each ribbon then tapers between its two edge
    /// shares. Ribbons on one edge stack in the vertical order of their
    /// opposite endpoints, keeping bundles from crossing right where they
    /// leave a card.
    let measureSankeyRibbons
        (context: ConnectorMeasureContext)
        (requests: SankeyRibbonRequest list)
        : Map<string, SankeyRibbon> =
        // This runs once per animation frame while scrolling or dragging, and
        // several ribbons usually share a card - read each card rect once.
        let rectCache = System.Collections.Generic.Dictionary<string, SankeyRect>()

        let cachedCardRect nodeId node =
            match rectCache.TryGetValue nodeId with
            | true, rect -> rect
            | _ ->
                let rect = cardRect context node
                rectCache.[nodeId] <- rect
                rect

        let resolved =
            requests
            |> List.choose (fun request ->
                match tryRibbonNode context request.Source, tryRibbonNode context request.Target with
                | Some(sourceNodeId, sourceNode), Some(targetNodeId, targetNode) ->
                    Some {
                        Request = request
                        SourceNodeId = sourceNodeId
                        TargetNodeId = targetNodeId
                        SourceRect = cachedCardRect sourceNodeId sourceNode
                        TargetRect = cachedCardRect targetNodeId targetNode
                    }
                | _ -> None
            )

        let segments nodeIdOf (rectOf: ResolvedRibbon -> SankeyRect) (oppositeRectOf: ResolvedRibbon -> SankeyRect) =
            resolved
            |> List.groupBy nodeIdOf
            |> List.collect (fun (_, ribbons) ->
                let rect = rectOf ribbons.Head
                let totalWeight = ribbons |> List.sumBy (fun ribbon -> max 1. ribbon.Request.Weight)
                let height = rect.Bottom - rect.Top

                ribbons
                |> List.sortBy (fun ribbon ->
                    let opposite = oppositeRectOf ribbon
                    (opposite.Top + opposite.Bottom) / 2.
                )
                |> List.fold
                    (fun (offset, allocated) ribbon ->
                        let share = height * (max 1. ribbon.Request.Weight) / totalWeight

                        offset + share,
                        (ribbon.Request.Key, (rect.Top + offset, rect.Top + offset + share))
                        :: allocated
                    )
                    (0., [])
                |> snd
            )
            |> Map.ofList

        let sourceSegments =
            segments
                (fun ribbon -> ribbon.SourceNodeId)
                (fun ribbon -> ribbon.SourceRect)
                (fun ribbon -> ribbon.TargetRect)

        let targetSegments =
            segments
                (fun ribbon -> ribbon.TargetNodeId)
                (fun ribbon -> ribbon.TargetRect)
                (fun ribbon -> ribbon.SourceRect)

        resolved
        |> List.choose (fun ribbon ->
            match sourceSegments.TryFind ribbon.Request.Key, targetSegments.TryFind ribbon.Request.Key with
            | Some sourceSegment, Some targetSegment ->
                // Ribbons run between whichever card edges face each other.
                let sourceX, targetX =
                    if ribbon.TargetRect.Left >= ribbon.SourceRect.Right then
                        ribbon.SourceRect.Right, ribbon.TargetRect.Left
                    else
                        ribbon.SourceRect.Left, ribbon.TargetRect.Right

                let start = {
                    X = sourceX
                    Y = (fst sourceSegment + snd sourceSegment) / 2.
                }

                let finish = {
                    X = targetX
                    Y = (fst targetSegment + snd targetSegment) / 2.
                }

                pathBetweenPoints start finish
                |> Option.map (fun path ->
                    ribbon.Request.Key,
                    {
                        Path = path
                        RibbonPath = sankeyRibbonPath sourceX sourceSegment targetX targetSegment
                        Midpoint = midpointBetweenPoints start finish
                    }
                )
            | _ -> None
        )
        |> Map.ofList

    /// Rail connectors shorter than this are skipped entirely so dense layouts do not
    /// fill the rail gutters with overlapping stubs.
    let minimumConnectorDistance = 24.

    let private distanceBetween start finish =
        let deltaX = finish.X - start.X
        let deltaY = finish.Y - start.Y
        sqrt (deltaX * deltaX + deltaY * deltaY)

    let pathBetweenDistantHandles context source target =
        match tryHandle context source, tryHandle context target with
        | Some sourceNode, Some targetNode ->
            let start = center context sourceNode
            let finish = center context targetNode

            if distanceBetween start finish < minimumConnectorDistance then
                None
            else
                pathBetweenPoints start finish
        | _ -> None

/// Builds handle references used by overlay measurements.
module ConnectorHandles =

    let group side groupId : ConnectionHandleRef = {
        Kind = ConnectionHandleKind.GroupCard
        Side = side
        Id = groupId
        ParentGroupId = None
    }

    let member' side groupId setId : ConnectionHandleRef = {
        Kind = ConnectionHandleKind.GroupMember
        Side = side
        Id = setId
        ParentGroupId = Some groupId
    }

    let memberPropertyAnchor side groupId setId : ConnectionHandleRef = {
        Kind = ConnectionHandleKind.GroupMemberPropertyAnchor
        Side = side
        Id = setId
        ParentGroupId = Some groupId
    }

    let propertyHeader side property : ConnectionHandleRef = {
        Kind = ConnectionHandleKind.PropertyHeader
        Side = side
        Id = DragDrop.propertyKeyIdentity property
        ParentGroupId = None
    }

    let propertyValue side propertyValueId : ConnectionHandleRef = {
        Kind = ConnectionHandleKind.PropertyValue
        Side = side
        Id = propertyValueId
        ParentGroupId = None
    }

    /// Measurement-only anchor on the property-facing card edge; rail connectors end here
    /// instead of at the draggable group handle on the opposite edge.
    let propertyAnchor side groupId : ConnectionHandleRef = {
        Kind = ConnectionHandleKind.GroupPropertyAnchor
        Side = side
        Id = groupId
        ParentGroupId = None
    }
