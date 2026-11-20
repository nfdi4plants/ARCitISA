module Swate.Components.Composite.GraphCanvas.Layout

open Fable.Core
open Swate.Components.JsBindings
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Composite.GraphCanvas.Pills

// Builds the ELK graph from the canvas data and runs ELK. The result holds everything a renderer needs
// as plain numbers. The decision helpers at the end (sameBox, isPortrait, ...) are the pure parts of
// the canvas component that react to the measured size of its box.

let private nodeMinWidth = 132.0
let private nodeTextInset = 15.0
// A node is at most 320px wide, which leaves 290px for a row of pills.
let private pillRowWidth = 290.0
let private titleLimit = 42
let private subtitleLimit = 48
// ELK spreads the ends of the edges evenly along a node side, so each end gets 28px.
let private edgeEndSpacing = 28.0
let private cornerRadius = 6.0

/// A measured size that changes by fewer pixels keeps its old value, so rounding cannot start a new layout.
let resizeNoise = 4.0

/// Space between the pill list and its toggle, and between the list and the edge of the visible area.
let menuGap = 4.0
let viewMargin = 8.0

// What ELK adds to the width of a two-layer graph besides the nodes and the label: its default 12px
// padding on both sides, and the 40px between each node layer and the label layer.
let private twoLayerOverhead = 2.0 * 12.0 + 2.0 * 40.0

let private elkOptionPairs: (string * string) list = [
    "org.eclipse.elk.algorithm", "layered"
    "org.eclipse.elk.direction", "RIGHT"
    "org.eclipse.elk.edgeRouting", "ORTHOGONAL"
    "org.eclipse.elk.layered.spacing.nodeNodeBetweenLayers", "40"
    "org.eclipse.elk.spacing.nodeNode", "24"
    "org.eclipse.elk.spacing.edgeNode", "12"
    "org.eclipse.elk.spacing.edgeEdge", "16"
    "org.eclipse.elk.layered.spacing.edgeNodeBetweenLayers", "12"
    "org.eclipse.elk.layered.spacing.edgeEdgeBetweenLayers", "12"
    "org.eclipse.elk.spacing.edgeLabel", "0"
    "org.eclipse.elk.layered.edgeLabels.centerLabelPlacementStrategy", "MEDIAN_LAYER"
    "org.eclipse.elk.layered.edgeLabels.sideSelection", "ALWAYS_DOWN"
    "org.eclipse.elk.randomSeed", "1"
    "org.eclipse.elk.layered.considerModelOrder.strategy", "NODES_AND_EDGES"
    // Separate components are packed into rows by size, so widening one label would move whole nodes between layers.
    "org.eclipse.elk.separateConnectedComponents", "false"
]

// ELK names the node alignments for the left to right layout. In the top to bottom layout RIGHTDOWN
// lines a parent up with its first child, so the main flow stays on the left and the other children
// fan out to the right. On random graphs it has the smallest mean x of the alignments.
let private compactOptionPairs: (string * string) list =
    (elkOptionPairs
     |> List.map (fun (key, value) ->
         if key = "org.eclipse.elk.direction" then
             key, "DOWN"
         else
             key, value
     ))
    @ [
        "org.eclipse.elk.layered.nodePlacement.bk.fixedAlignment", "RIGHTDOWN"
    ]

let private elkOptions = Elk.layoutOptions elkOptionPairs
let private compactElkOptions = Elk.layoutOptions compactOptionPairs

// A one-row pill run sits on its edge. The label is as high as a pill and the edge runs through its middle.
let private inlineLabelOptions =
    Elk.layoutOptions [
        "org.eclipse.elk.edgeLabels.placement", "CENTER"
        "org.eclipse.elk.edgeLabels.inline", "true"
    ]

// The rows below the first one hang under the edge. ALWAYS_DOWN in the graph options keeps this label below it.
let private belowLabelOptions =
    Elk.layoutOptions [
        "org.eclipse.elk.edgeLabels.placement", "CENTER"
        "org.eclipse.elk.edgeLabels.inline", "false"
    ]

let private shortText (text: string) (max: int) : string =
    if text.Length <= max then
        text
    else
        text.Substring(0, max - 1) + "…"

/// Pills wrap into rows inside the node. A pill wider than a row widens the node, because pills are never cut.
/// busiestSide is the number of edge ends on the side of the node that has the most of them.
let measureNode (measure: MeasureText) (busiestSide: int) (compact: bool) (node: CanvasNode) : NodeBox =
    let titleText = shortText node.Title titleLimit

    let subtitleText =
        match node.Subtitle with
        | Some subtitle when subtitle <> "" -> shortText subtitle subtitleLimit
        | _ -> ""

    let titleWidth = measure titleText 600.0 13.0 node.Mono

    let subtitleWidth =
        if subtitleText <> "" then
            measure subtitleText 400.0 12.0 false
        else
            0.0

    let textWidth = ceil (max titleWidth subtitleWidth + 34.0)

    let views = node.Pills |> Array.map (toPillView measure)

    let rows =
        wrapPillRows views (views |> Array.fold (fun widest view -> max widest view.Width) pillRowWidth)

    let widest = rows |> Array.fold (fun widest row -> max widest (rowWidth row)) 0.0

    let pillTop = if subtitleText <> "" then 62.0 else 46.0

    let pills =
        rows
        |> Array.mapi (fun r row ->
            let xs = row |> Array.scan (fun x view -> x + (view.Width + pillGap)) nodeTextInset

            row
            |> Array.mapi (fun i view ->
                ({
                    View = view
                    X = xs.[i]
                    Y = pillTop + float r * (pillHeight + pillGap)
                }
                : PlacedPill)
            )
        )
        |> Array.concat

    let contentHeight =
        if rows.Length > 0 then
            pillTop
            + float rows.Length * pillHeight
            + float (rows.Length - 1) * pillGap
            + 10.0
        elif subtitleText <> "" then
            58.0
        else
            42.0

    // The edge ends are spread along the side they use: the height in the left to right layout, the width in the compact one.
    let endsRoom =
        if busiestSide > 1 then
            edgeEndSpacing * float (busiestSide + 1)
        else
            0.0

    let height =
        if compact then
            contentHeight
        else
            max contentHeight endsRoom

    let pillsWidth = if rows.Length > 0 then ceil (widest + 30.0) else 0.0

    let endsWidth = if compact then endsRoom else 0.0

    {
        Width = List.max [ nodeMinWidth; textWidth; pillsWidth; endsWidth ]
        Height = height
        TitleText = titleText
        SubtitleText = subtitleText
        TitleY =
            if subtitleText <> "" || rows.Length > 0 then
                23.0
            else
                height / 2.0
        SubtitleY = 43.0
        Pills = pills
    }

let private countBy (ids: string[]) : Map<string, int> =
    ids
    |> Array.fold
        (fun counts id ->
            let current = counts |> Map.tryFind id |> Option.defaultValue 0
            counts |> Map.add id (current + 1)
        )
        Map.empty

let private toNumber (value: float) : float = floor (value * 100.0 + 0.5) / 100.0

let private at (point: Point) : string =
    $"{toNumber point.X} {toNumber point.Y}"

/// Orthogonal route with rounded corners. The radius shrinks on short segments.
let roundedPathWith (radius: float) (points: Point[]) : string =
    if points.Length < 2 then
        ""
    else
        let mutable d = "M " + at points.[0]

        for i in 1 .. points.Length - 2 do
            let a = points.[i - 1]
            let b = points.[i]
            let c = points.[i + 1]
            let before = sqrt ((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y))
            let after = sqrt ((c.X - b.X) * (c.X - b.X) + (c.Y - b.Y) * (c.Y - b.Y))
            let r = min radius (min (before / 2.0) (after / 2.0))

            if r < 0.5 then
                d <- d + " L " + at b
            else
                let start = {
                    X = b.X + (a.X - b.X) / before * r
                    Y = b.Y + (a.Y - b.Y) / before * r
                }

                let finish = {
                    X = b.X + (c.X - b.X) / after * r
                    Y = b.Y + (c.Y - b.Y) / after * r
                }

                d <- d + " L " + at start + " Q " + at b + " " + at finish

        d + " L " + at points.[points.Length - 1]

let roundedPath (points: Point[]) : string = roundedPathWith cornerRadius points

/// y of the horizontal route segment that runs under the label. When the route bends, ELK may
/// offer several horizontal segments, so the one nearest to y wins.
let runY (route: Point[]) (label: LabelBox) (y: float) : float =
    let left = label.LabelX
    let right = left + label.LabelWidth

    let best =
        seq { 1 .. route.Length - 1 }
        |> Seq.fold
            (fun (best: float option) i ->
                let a = route.[i - 1]
                let b = route.[i]

                if abs (a.Y - b.Y) > 0.5 then
                    best
                elif min a.X b.X > left + 1.0 || max a.X b.X < right - 1.0 then
                    best
                else
                    match best with
                    | Some current when not (abs (a.Y - y) < abs (current - y)) -> best
                    | _ -> Some a.Y
            )
            None

    defaultArg best y

/// x of the vertical route segment that runs through the label, the counterpart of runY for compact mode.
let runX (route: Point[]) (label: LabelBox) : float =
    let top = label.LabelY
    let bottom = top + label.LabelHeight
    let middle = label.LabelX + label.LabelWidth / 2.0

    let best =
        seq { 1 .. route.Length - 1 }
        |> Seq.fold
            (fun (best: float option) i ->
                let a = route.[i - 1]
                let b = route.[i]

                if abs (a.X - b.X) > 0.5 then
                    best
                elif min a.Y b.Y > top + 1.0 || max a.Y b.Y < bottom - 1.0 then
                    best
                else
                    match best with
                    | Some current when not (abs (a.X - middle) < abs (current - middle)) -> best
                    | _ -> Some a.X
            )
            None

    defaultArg best middle

/// An edge expands inline when its widest pill fits next to the widest nodes of both sides within
/// availableWidth. An unmeasured box (width 0, as in jsdom) counts as wide enough.
let fitsInline (minInlineWidth: float) (availableWidth: float) : bool =
    availableWidth <= 0.0 || availableWidth >= minInlineWidth

let private toPoint (point: Elk.ElkPoint) : Point = { X = point.x; Y = point.y }

/// An expanded edge wraps its pills so that a graph of two layers (the groups and detail views) still fits
/// opts.Width: the gap between the layers has to leave room for the widest node on each side. Deeper
/// graphs have more gaps, so they may scroll, and an edge that does not fit stays collapsed. Without a
/// width (0) the rows are as wide as the widest pill.
///
/// A portrait box gets the compact layout instead: top to bottom and left aligned, with one chip per
/// edge. Only the border box of the box decides, so no layout (and no scrollbar it brings) can make the
/// choice flip back and forth.
///
/// run executes the ELK graph. GraphCanvas passes a runner that works in a Web Worker, and layoutGraph runs ELK
/// on the calling thread.
let layoutGraphWith
    (run: Elk.ElkNode -> JS.Promise<Elk.ElkLayoutResult>)
    (nodes: CanvasNode[])
    (edges: CanvasEdge[])
    (expanded: string[])
    (measure: MeasureText)
    (opts: LayoutOptions)
    : JS.Promise<GraphLayout> =
    let availableWidth = opts.Width
    let compactBox = opts.Portrait
    let outgoing = countBy (edges |> Array.map (fun edge -> edge.From))
    let incoming = countBy (edges |> Array.map (fun edge -> edge.To))

    let busiestSide (id: string) =
        max (outgoing |> Map.tryFind id |> Option.defaultValue 0) (incoming |> Map.tryFind id |> Option.defaultValue 0)

    let boxes =
        nodes
        |> Array.map (fun node -> measureNode measure (busiestSide node.Id) compactBox node)

    let widthOf =
        Array.zip nodes boxes
        |> Array.fold (fun widths (node, nodeBox) -> widths |> Map.add node.Id nodeBox.Width) Map.empty

    let widest (ids: string[]) =
        ids
        |> Array.fold (fun widest id -> max widest (widthOf |> Map.tryFind id |> Option.defaultValue 0.0)) 0.0

    let sideWidths =
        widest (edges |> Array.map (fun edge -> edge.From))
        + widest (edges |> Array.map (fun edge -> edge.To))

    let labelSpace = sideWidths + twoLayerOverhead + 2.0 * labelPadding
    let rowCap = max 0.0 (availableWidth - labelSpace)

    let minInlineWidths =
        edges
        |> Array.map (fun edge ->
            let widestPill =
                edgePillViews measure edge true
                |> Array.fold (fun widest view -> max widest view.Width) 0.0

            if edge.MorePills.Length > 0 then
                labelSpace + widestPill
            else
                0.0
        )

    let place (compact: bool) (isExpanded: string -> bool) : JS.Promise<GraphLayout> =
        let blocks =
            edges
            |> Array.map (fun edge ->
                if compact then
                    let views = compactPillViews measure edge
                    pillBlock views None, views
                else
                    let open' = isExpanded edge.Id
                    let views = edgePillViews measure edge open'
                    pillBlock views (if open' then Some rowCap else None), views
            )

        // Each edge with pills gets one label as wide as its widest pill row. ELK widens only that gap, and
        // expanding an edge changes only its label. The first row sits on the edge. When rows follow, the
        // label hangs under the edge and is as high as those rows plus the lower half of the first.
        let graph =
            Elk.root
                "root"
                (if compact then compactElkOptions else elkOptions)
                (Array.map2
                    (fun (node: CanvasNode) (nodeBox: NodeBox) -> Elk.leaf node.Id nodeBox.Width nodeBox.Height)
                    nodes
                    boxes)
                (Array.map2
                    (fun (edge: CanvasEdge) ((block: PillBlock), (views: PillView[])) ->
                        if views.Length = 0 then
                            Elk.edge edge.Id edge.From edge.To [||]
                        else
                            let stacked = block.Rows.Length > 1

                            let label =
                                Elk.label
                                    (edge.Id + "-pills")
                                    "pills"
                                    block.Width
                                    (if stacked then
                                         block.Height - pillHeight / 2.0
                                     else
                                         pillHeight)
                                    (if stacked then belowLabelOptions else inlineLabelOptions)

                            Elk.edge edge.Id edge.From edge.To [| label |]
                    )
                    edges
                    blocks)

        promise {
            let! laid = run graph

            let positions =
                defaultArg laid.children [||]
                |> Array.fold (fun found child -> found |> Map.add child.id child) Map.empty

            let routes =
                defaultArg laid.edges [||]
                |> Array.fold (fun found edge -> found |> Map.add edge.id edge) Map.empty

            let laidNodes =
                Array.map2
                    (fun (node: CanvasNode) (nodeBox: NodeBox) ->
                        let position = positions |> Map.tryFind node.Id

                        let coordinate (read: Elk.ElkLaidNode -> float option) =
                            position |> Option.bind read |> Option.defaultValue 0.0

                        ({
                            Node = node
                            X = coordinate (fun p -> p.x)
                            Y = coordinate (fun p -> p.y)
                            Width = nodeBox.Width
                            Height = nodeBox.Height
                            TitleText = nodeBox.TitleText
                            SubtitleText = nodeBox.SubtitleText
                            TitleY = nodeBox.TitleY
                            SubtitleY = nodeBox.SubtitleY
                            Pills = nodeBox.Pills
                        }
                        : LaidOutNode)
                    )
                    nodes
                    boxes

            let laidEdges =
                Array.mapi
                    (fun i (edge: CanvasEdge) ->
                        let laidEdge = routes |> Map.tryFind edge.Id

                        let section =
                            laidEdge |> Option.bind (fun e -> e.sections) |> Option.bind Array.tryHead

                        let route =
                            match section with
                            | Some section ->
                                Array.concat [
                                    [| toPoint section.startPoint |]
                                    defaultArg section.bendPoints [||] |> Array.map toPoint
                                    [| toPoint section.endPoint |]
                                ]
                            | None -> [||]

                        let block, views = blocks.[i]
                        let pills = ResizeArray<PlacedPill>()

                        if views.Length > 0 then
                            let label =
                                match laidEdge |> Option.bind (fun e -> e.labels) |> Option.bind Array.tryHead with
                                | Some label -> label
                                | None -> failwith $"ELK returned no label for edge {edge.Id}"

                            let labelBox = {
                                LabelX = defaultArg label.x 0.0
                                LabelY = defaultArg label.y 0.0
                                LabelWidth = defaultArg label.width 0.0
                                LabelHeight = defaultArg label.height 0.0
                            }

                            if compact then
                                pills.Add(
                                    {
                                        View = views.[0]
                                        X = runX route labelBox - views.[0].Width / 2.0
                                        Y = labelBox.LabelY + (labelBox.LabelHeight - pillHeight) / 2.0
                                    }
                                )
                            else
                                let near =
                                    if block.Rows.Length > 1 then
                                        labelBox.LabelY
                                    else
                                        labelBox.LabelY + labelBox.LabelHeight / 2.0

                                let top = runY route labelBox near - pillHeight / 2.0

                                block.Rows
                                |> Array.iteri (fun r row ->
                                    let mutable x = labelBox.LabelX + labelPadding

                                    for view in row do
                                        pills.Add(
                                            {
                                                View = view
                                                X = x
                                                Y = top + float r * (pillHeight + pillGap)
                                            }
                                        )

                                        x <- x + (view.Width + pillGap)
                                )

                        ({
                            Edge = edge
                            Route = route
                            Path = roundedPath route
                            EdgePills = pills.ToArray()
                            MinInlineWidth = minInlineWidths.[i]
                        }
                        : LaidOutEdge)
                    )
                    edges

            return {
                LayoutWidth = ceil (defaultArg laid.width 0.0)
                LayoutHeight = ceil (defaultArg laid.height 0.0)
                Compact = compact
                LaidOutNodes = laidNodes
                LaidOutEdges = laidEdges
            }
        }

    if compactBox then
        place true (fun _ -> false)
    else
        let openIds =
            edges
            |> Array.mapi (fun i edge -> i, edge)
            |> Array.filter (fun (i, edge) ->
                Array.contains edge.Id expanded && fitsInline minInlineWidths.[i] availableWidth
            )
            |> Array.map (fun (_, edge) -> edge.Id)
            |> Set.ofArray

        place false (fun id -> Set.contains id openIds)

/// layoutGraphWith, with ELK running on the calling thread.
let layoutGraph
    (nodes: CanvasNode[])
    (edges: CanvasEdge[])
    (expanded: string[])
    (measure: MeasureText)
    (opts: LayoutOptions)
    : JS.Promise<GraphLayout> =
    layoutGraphWith Elk.layout nodes edges expanded measure opts

/// True when the new measurement is within the noise of the old one, so the old value stays.
let sameBox (current: MeasuredBox) (next: MeasuredBox) : bool =
    current.Portrait = next.Portrait
    && abs (next.ClientWidth - current.ClientWidth) < resizeNoise
    && abs (next.ClientHeight - current.ClientHeight) < resizeNoise

/// The box is portrait when it is measured (width above 0) and taller than it is wide, judged by its
/// border box so that a scrollbar does not change the shape.
let isPortrait (boxWidth: float) (offsetHeight: float) (offsetWidth: float) : bool =
    boxWidth > 0.0 && offsetHeight > offsetWidth

/// The width shapes the layout only through the rows of an expanded edge. A portrait box takes the
/// compact layout, which has no expanded edges and does not depend on the width.
let effectiveLayoutWidth (portrait: bool) (expandedCount: int) (boxWidth: float) : float =
    if not portrait && expandedCount > 0 then boxWidth else 0.0

/// Style values of the pill list. The list takes the visible area of the box as its limit: it opens on
/// the side of the toggle with more room and scrolls inside itself when it needs more. The width follows
/// the text up to the visible width.
///
/// The width of the list depends on the max width and max height that are applied to it. offsetWidthWith
/// applies both to the element and returns its offsetWidth.
let pillMenuPlacement (geometry: PillMenuGeometry) (offsetWidthWith: float -> float -> float) : PillMenuPlacement =
    let root = geometry.Root

    let visible = {
        Left = root.Left + geometry.RootClientLeft
        Top = root.Top + geometry.RootClientTop
        Right = root.Left + geometry.RootClientLeft + geometry.RootClientWidth
        Bottom = root.Top + geometry.RootClientTop + geometry.RootClientHeight
    }

    let anchor = geometry.Anchor
    let origin = geometry.Origin

    let maxWidth = max 0.0 (visible.Right - visible.Left - 2.0 * viewMargin)
    let below = visible.Bottom - anchor.Bottom - menuGap - viewMargin
    let above = anchor.Top - visible.Top - menuGap - viewMargin
    let openAbove = below < above

    let maxHeight = max 0.0 (if openAbove then above else below)

    let rightmost = visible.Right - viewMargin - offsetWidthWith maxWidth maxHeight

    {
        MaxWidth = maxWidth
        MaxHeight = maxHeight
        OpenAbove = openAbove
        MenuTop =
            if openAbove then
                None
            else
                Some(anchor.Bottom - origin.Top + menuGap)
        MenuBottom =
            if openAbove then
                Some(origin.Bottom - anchor.Top + menuGap)
            else
                None
        MenuLeft = max (visible.Left + viewMargin) (min anchor.Left rightmost) - origin.Left
    }
