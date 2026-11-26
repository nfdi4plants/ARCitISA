module internal Arcitisa.Components.Composite.GraphCanvas.LayoutTests

// Ported from graphCanvasLayout.test.ts of arc-portal-ui. Every case is ported and runs the real ELK.
// Differences from the TypeScript version:
// - The "Show the N details" tooltip assertion of the chip case is left out because it pins fixed UI copy.
// - The landscape case lists the boxes (width 400) and (width 0). The TypeScript version lists an
//   omitted portrait flag and an explicit false separately, which are the same box here.
// - The tests measure with the 7px estimate, which ignores the weight, size and mono arguments, as the
//   TypeScript tests do.
//
// The tests for measureNode, sameBox, isPortrait, effectiveLayoutWidth and pillMenuPlacement are new.
// The last four cover decisions that the TypeScript version makes inside GraphCanvas.tsx.

open Fable.Core
open Vitest
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Composite.GraphCanvas.Types
open Arcitisa.Components.Composite.GraphCanvas.Pills
open Arcitisa.Components.Composite.GraphCanvas.Layout

let private measure: MeasureText = fun text _ _ _ -> Dom.estimateTextWidth text

let private node (id: string) : CanvasNode = {
    Id = id
    Title = id
    Subtitle = None
    Color = CanvasColor.Study
    Dashed = false
    Mono = false
    Selected = false
    Emphasized = false
    Pills = [||]
    Tooltip = None
    AriaLabel = id
    Clickable = true
}

let private titled (id: string) (title: string) : CanvasNode = { node id with Title = title }

let private property (key: string) (name: string) (value: string) : CanvasPill = {
    Key = key
    Kind = PillKind.Property
    Name = Some name
    Value = Some value
    Unit = None
    Category = Some PillCategory.Parameter
    Tooltip = None
}

let private edge
    (id: string)
    (source: string)
    (target: string)
    (pills: CanvasPill[])
    (morePills: CanvasPill[])
    : CanvasEdge =
    {
        Id = id
        From = source
        To = target
        Color = CanvasColor.Study
        Highlighted = false
        Pills = pills
        MorePills = morePills
        Tooltip = None
    }

let private inBox (width: float) : LayoutOptions = { Width = width; Portrait = false }
let private portraitBox (width: float) : LayoutOptions = { Width = width; Portrait = true }

// A box so wide that an expanded edge keeps all its pills in one row.
let private wide = inBox 3000.0
let private unknownBox = inBox 0.0

let private layout nodes edges (expanded: string[]) options =
    layoutGraph nodes edges expanded measure options

// A chain a > b > c > d. The middle edge hides two pills.
let private chainNodes = [| "a"; "b"; "c"; "d" |] |> Array.map node

let private chainEdges = [|
    edge "ab" "a" "b" [|
        property "p1" "temperature" "20 C"
        property "p2" "light" "dark"
    |] [||]
    edge "bc" "b" "c" [| property "p3" "duration" "2 h" |] [| property "p4" "volume" "5 ml"; property "p5" "pH" "7" |]
    edge "cd" "c" "d" [| property "p6" "instrument" "GC-MS" |] [||]
|]

let private gaps (result: GraphLayout) : float[] =
    result.LaidOutNodes
    |> Array.pairwise
    |> Array.map (fun (previous, next) -> next.X - (previous.X + previous.Width))

// Three disconnected components: A has 4 targets, B has 2 and C has 2. Every edge hides two pills.
let private componentSources = [| "A"; "B"; "C" |]
let private componentTargets = [| "t1"; "t2"; "t3"; "t4"; "t5"; "t6"; "t7"; "t8" |]

let private componentNodes =
    Array.append componentSources componentTargets |> Array.map node

let private componentEdges =
    componentTargets
    |> Array.mapi (fun i target ->
        let source =
            if i < 4 then componentSources.[0]
            elif i < 6 then componentSources.[1]
            else componentSources.[2]

        edge $"e{i}" source target [| property "p1" "temperature" "20 C" |] [|
            property "p2" "duration" "2 h"
            property "p3" "volume" "5 ml"
        |]
    )

// Two layers like the groups and detail views: four sources, five targets, six edges. The edge e3 hides ten pills.
let private many (prefix: string) (count: int) : CanvasPill[] =
    Array.init count (fun i -> property $"{prefix}{i}" $"property {i}" $"value {i}")

let private sources = [| "s0"; "s1"; "s2"; "s3" |]
let private targets = [| "t0"; "t1"; "t2"; "t3"; "t4" |]

let private layerNodes = [|
    titled "s0" "Input group of the study"
    titled "s1" "Inputs B"
    titled "s2" "Inputs C"
    titled "s3" "Inputs D"
    titled "t0" "Outputs A"
    titled "t1" "Outputs B with a long name"
    titled "t2" "Outputs C"
    titled "t3" "Outputs D"
    titled "t4" "Outputs E"
|]

let private sharedNodes: CanvasPill = {
    Key = "count"
    Kind = PillKind.Count
    Name = None
    Value = Some "6"
    Unit = None
    Category = None
    Tooltip = Some "6 shared nodes"
}

let private layerEdges = [|
    edge "e0" "s0" "t0" (many "a" 2) (many "b" 3)
    edge "e1" "s0" "t1" (many "c" 1) (many "d" 2)
    edge "e2" "s1" "t1" (many "e" 2) (many "f" 2)
    edge "e3" "s1" "t2" (many "g" 2) (many "h" 10)
    edge "e4" "s2" "t3" (many "i" 1) (many "j" 2)
    edge "e5" "s3" "t4" [| sharedNodes |] [||]
|]

let private widestPill =
    edgePillViews measure layerEdges.[3] true
    |> Array.map (fun view -> view.Width)
    |> Array.max

let private edgeOf (result: GraphLayout) (id: string) : LaidOutEdge =
    result.LaidOutEdges |> Array.find (fun laid -> laid.Edge.Id = id)

let private nodeOf (result: GraphLayout) (id: string) : LaidOutNode =
    result.LaidOutNodes |> Array.find (fun laid -> laid.Node.Id = id)

// x, y, width, height
type private Area = float * float * float * float

let private pillArea (pill: PlacedPill) : Area =
    pill.X, pill.Y, pill.View.Width, pillHeight

let private overlaps ((ax, ay, aw, ah): Area) ((bx, by, bw, bh): Area) : bool =
    ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah

let private rowsOf (pills: PlacedPill[]) : PlacedPill[][] =
    pills
    |> Array.map (fun pill -> pill.Y)
    |> Array.distinct
    |> Array.sort
    |> Array.map (fun y -> pills |> Array.filter (fun pill -> pill.Y = y))

let private rowSpan (row: PlacedPill[]) : float =
    let last = row.[row.Length - 1]
    last.X + last.View.Width - row.[0].X

// Which node ids share an x, left to right.
let private layers (result: GraphLayout) : string[][] =
    result.LaidOutNodes
    |> Array.map (fun laid -> laid.X)
    |> Array.distinct
    |> Array.sort
    |> Array.map (fun x ->
        result.LaidOutNodes
        |> Array.filter (fun laid -> laid.X = x)
        |> Array.map (fun laid -> laid.Node.Id)
    )

// y, from x, to x of every horizontal segment of a route.
let private horizontalSegments (route: Point[]) : (float * float * float)[] =
    route
    |> Array.pairwise
    |> Array.choose (fun (start, finish) ->
        if abs (start.Y - finish.Y) < 0.5 then
            Some(start.Y, min start.X finish.X, max start.X finish.X)
        else
            None
    )

// x, from y, to y of every vertical segment of a route.
let private verticalSegments (route: Point[]) : (float * float * float)[] =
    route
    |> Array.pairwise
    |> Array.choose (fun (start, finish) ->
        if abs (start.X - finish.X) < 0.5 then
            Some(start.X, min start.Y finish.Y, max start.Y finish.Y)
        else
            None
    )

// y of the route segment that carries the first row of pills, with the 16px of padding on both sides.
let private lineUnder (laid: LaidOutEdge) : float option =
    let first = (rowsOf laid.EdgePills).[0]
    let last = first.[first.Length - 1]
    let left = first.[0].X - 16.0
    let right = last.X + last.View.Width + 16.0

    horizontalSegments laid.Route
    |> Array.tryPick (fun (y, from, until) ->
        if from <= left + 1.0 && until >= right - 1.0 then
            Some y
        else
            None
    )

let private xOf (result: GraphLayout) (ids: string[]) : float[] =
    result.LaidOutNodes
    |> Array.filter (fun laid -> Array.contains laid.Node.Id ids)
    |> Array.map (fun laid -> laid.X)
    |> Array.distinct

Vitest.describe (
    "layoutGraph",
    fun () ->
        Vitest.test (
            "widens only the gap of an expanded edge",
            fun () -> promise {
                let! collapsed = layout chainNodes chainEdges [||] wide
                let! expanded = layout chainNodes chainEdges [| "bc" |] wide

                let before = gaps collapsed
                let after = gaps expanded
                Vitest.expect(after.[1]).toBeGreaterThan before.[1]
                Vitest.expect(after.[0]).toBe before.[0]
                Vitest.expect(after.[2]).toBe before.[2]
            }
        )

        Vitest.test (
            "keeps every node on its row and layer while one edge of disconnected components expands",
            fun () -> promise {
                let! basis = layout componentNodes componentEdges [||] wide
                Vitest.expect((xOf basis componentSources).Length).toBe 1
                Vitest.expect((xOf basis componentTargets).Length).toBe 1

                for laid in componentEdges do
                    let! expanded = layout componentNodes componentEdges [| laid.Id |] wide

                    Vitest
                        .expect(expanded.LaidOutNodes |> Array.map (fun n -> n.Y))
                        .toEqual (basis.LaidOutNodes |> Array.map (fun n -> n.Y))

                    Vitest.expect(xOf expanded componentSources).toEqual (xOf basis componentSources)
                    Vitest.expect((xOf expanded componentTargets).Length).toBe 1
                    Vitest.expect((xOf expanded componentTargets).[0]).toBeGreaterThan (xOf basis componentTargets).[0]
            }
        )

        Vitest.test (
            "places the pill run on the horizontal segment of its edge",
            fun () -> promise {
                let! result = layout chainNodes chainEdges [| "bc" |] wide

                for laid in result.LaidOutEdges do
                    let first = laid.EdgePills.[0]
                    let last = laid.EdgePills.[laid.EdgePills.Length - 1]
                    let left = first.X - 16.0
                    let right = last.X + last.View.Width + 16.0

                    let under =
                        horizontalSegments laid.Route
                        |> Array.tryFind (fun (_, from, until) -> from <= left + 1.0 && until >= right - 1.0)

                    Vitest.expect(under.IsSome).toBe true

                    match under with
                    | Some(y, _, _) -> Vitest.expect(abs (first.Y + 9.0 - y)).toBeLessThanOrEqual 1.0
                    | None -> ()
            }
        )
)

Vitest.describe (
    "layoutGraph with an expanded edge in a box",
    fun () ->
        for width in [ 1400.0; 1000.0 ] do
            Vitest.test (
                $"wraps the pills into rows that keep the graph inside a box of {width}px",
                fun () -> promise {
                    let! collapsed = layout layerNodes layerEdges [||] (inBox width)
                    let! expanded = layout layerNodes layerEdges [| "e3" |] (inBox width)
                    let pills = (edgeOf expanded "e3").EdgePills
                    let rows = rowsOf pills

                    Vitest.expect(rows.Length).toBeGreaterThan 1

                    Vitest
                        .expect(pills |> Array.map (fun pill -> pill.View.Key))
                        .toEqual (edgePillViews measure layerEdges.[3] true |> Array.map (fun view -> view.Key))

                    // Every row sits between the layers, left-aligned with the first one, pillGap below the row before.
                    let sourcesRight =
                        sources
                        |> Array.map (fun id -> (nodeOf expanded id).X + (nodeOf expanded id).Width)
                        |> Array.max

                    let targetsLeft = (nodeOf expanded "t0").X

                    rows
                    |> Array.iteri (fun r row ->
                        Vitest.expect(row.[0].X).toBe rows.[0].[0].X
                        Vitest.expect(row.[0].Y).toBe (rows.[0].[0].Y + float r * (pillHeight + pillGap))
                        Vitest.expect(row.[0].X).toBeGreaterThanOrEqual sourcesRight
                        Vitest.expect(row.[0].X + rowSpan row).toBeLessThanOrEqual targetsLeft
                    )

                    // The "-" toggle ends the last row.
                    let last = pills.[pills.Length - 1]
                    Vitest.expect(last.View.Toggle).toEqual (Some ToggleKind.Less)
                    Vitest.expect(Array.last (Array.last rows)).toBe last

                    // The graph stays inside the box. The one-line run would be wider than the box alone.
                    Vitest.expect(collapsed.LayoutWidth).toBeLessThanOrEqual width
                    Vitest.expect(expanded.LayoutWidth).toBeLessThanOrEqual width

                    Vitest.expect((pillBlock (edgePillViews measure layerEdges.[3] true) None).Width).toBeGreaterThan
                        width
                }
            )

        Vitest.test (
            "puts one pill in a row when the box leaves room for little more than the widest pill",
            fun () -> promise {
                let! collapsed = layout layerNodes layerEdges [||] (inBox 800.0)
                let! expanded = layout layerNodes layerEdges [| "e3" |] (inBox 800.0)
                let pills = (edgeOf expanded "e3").EdgePills
                let rows = rowsOf pills

                // Only the small "-" toggle fits next to a pill, at the end of the last row.
                Vitest
                    .expect(rows |> Array.take (rows.Length - 1) |> Array.map Array.length)
                    .toEqual (Array.create (rows.Length - 1) 1)

                Vitest.expect(rows.Length).toBe (pills.Length - 1)
                // The collapsed runs of the other edges are wider than this box already, so the graph does not grow.
                Vitest.expect(expanded.LayoutWidth).toBeLessThanOrEqual collapsed.LayoutWidth
            }
        )

        Vitest.test (
            "puts the first row on the edge line like a collapsed run",
            fun () -> promise {
                let! collapsed = layout layerNodes layerEdges [||] (inBox 1400.0)
                let! expanded = layout layerNodes layerEdges [| "e3" |] (inBox 1400.0)

                for result in [ collapsed; expanded ] do
                    let laid = edgeOf result "e3"
                    let line = lineUnder laid
                    Vitest.expect(line.IsSome).toBe true

                    Vitest
                        .expect(abs (laid.EdgePills.[0].Y + pillHeight / 2.0 - defaultArg line infinity))
                        .toBeLessThanOrEqual
                        1.0
            }
        )

        Vitest.test (
            "keeps every node on its layer and the other pills inside the box",
            fun () -> promise {
                let! collapsed = layout layerNodes layerEdges [||] (inBox 1400.0)

                for opened in layerEdges |> Array.filter (fun e -> e.MorePills.Length > 0) do
                    let! expanded = layout layerNodes layerEdges [| opened.Id |] (inBox 1400.0)

                    Vitest.expect(layers expanded).toEqual (layers collapsed)

                    for laid in expanded.LaidOutEdges |> Array.filter (fun other -> other.Edge.Id <> opened.Id) do
                        for pill in laid.EdgePills do
                            Vitest.expect(pill.X + pill.View.Width).toBeLessThanOrEqual expanded.LayoutWidth
            }
        )

        Vitest.test (
            "moves the pills of the other edges by at most half of the growth of the gap",
            fun () -> promise {
                let! collapsed = layout layerNodes layerEdges [||] (inBox 1400.0)
                let! expanded = layout layerNodes layerEdges [| "e3" |] (inBox 1400.0)
                let growth = expanded.LayoutWidth - collapsed.LayoutWidth

                Vitest.expect(growth).toBeGreaterThan 0.0

                for laid in expanded.LaidOutEdges |> Array.filter (fun other -> other.Edge.Id <> "e3") do
                    laid.EdgePills
                    |> Array.iteri (fun i pill ->
                        let before = (edgeOf collapsed laid.Edge.Id).EdgePills.[i]
                        Vitest.expect(pill.X - before.X).toBeGreaterThanOrEqual (-1.0)
                        Vitest.expect(pill.X - before.X).toBeLessThanOrEqual (growth / 2.0 + 1.0)
                    )
            }
        )

        Vitest.test (
            "reserves the space of the whole block",
            fun () -> promise {
                for width in [ 1400.0; 800.0 ] do
                    let! result = layout layerNodes layerEdges [| "e3" |] (inBox width)
                    let block = (edgeOf result "e3").EdgePills |> Array.map pillArea

                    for laid in result.LaidOutNodes do
                        for area in block do
                            Vitest.expect(overlaps area (laid.X, laid.Y, laid.Width, laid.Height)).toBe false

                    for laid in result.LaidOutEdges |> Array.filter (fun other -> other.Edge.Id <> "e3") do
                        for area in block do
                            for other in laid.EdgePills do
                                Vitest.expect(overlaps area (pillArea other)).toBe false

                            // No other route crosses the block either.
                            for start, finish in Array.pairwise laid.Route do
                                let spanX = abs (start.X - finish.X)
                                let spanY = abs (start.Y - finish.Y)

                                let segment: Area =
                                    min start.X finish.X,
                                    min start.Y finish.Y,
                                    (if spanX = 0.0 then 0.1 else spanX),
                                    (if spanY = 0.0 then 0.1 else spanY)

                                Vitest.expect(overlaps area segment).toBe false
            }
        )

        Vitest.test (
            "leaves an edge collapsed when its widest pill does not fit next to the nodes",
            fun () -> promise {
                let! collapsed = layout layerNodes layerEdges [||] (inBox 600.0)
                let! expanded = layout layerNodes layerEdges [| "e3" |] (inBox 600.0)

                let side (ids: string[]) =
                    ids |> Array.map (fun id -> (nodeOf collapsed id).Width) |> Array.max

                // The widest nodes of both sides, the label padding, ELK's padding and the 40px on both sides of the label.
                Vitest
                    .expect((edgeOf collapsed "e3").MinInlineWidth)
                    .toBe (side sources + side targets + 2.0 * 16.0 + 2.0 * 12.0 + 2.0 * 40.0 + widestPill)

                Vitest.expect((edgeOf collapsed "e3").MinInlineWidth).toBeGreaterThan 600.0

                let placed (result: GraphLayout) =
                    result.LaidOutEdges
                    |> Array.map (fun laid -> laid.EdgePills |> Array.map (fun pill -> pill.View.Key, pill.X, pill.Y))

                Vitest.expect(placed expanded).toEqual (placed collapsed)
            }
        )

        Vitest.test (
            "wraps at the widest pill while the width is unknown",
            fun () -> promise {
                let! expanded = layout layerNodes layerEdges [| "e3" |] unknownBox
                let rows = rowsOf (edgeOf expanded "e3").EdgePills

                Vitest.expect(rows.Length).toBeGreaterThan 1

                for row in rows do
                    Vitest.expect(rowSpan row).toBeLessThanOrEqual widestPill
            }
        )
)

// A parent with four children, each with a chain below. The first chain is the longest.
let private fan = [| 5; 3; 3; 2 |]

let private fanNodes =
    Array.append
        [| titled "P" "Parent" |]
        (fan
         |> Array.mapi (fun i length -> Array.init length (fun j -> titled $"c{i}_{j}" $"child {i}.{j}"))
         |> Array.concat)

let private fanEdges =
    fan
    |> Array.mapi (fun i length ->
        Array.init
            length
            (fun j ->
                let counter: CanvasPill = {
                    Key = $"n{i}_{j}"
                    Kind = PillKind.Count
                    Name = None
                    Value = Some "1"
                    Unit = None
                    Category = None
                    Tooltip = None
                }

                edge $"f{i}_{j}" (if j = 0 then "P" else $"c{i}_{j - 1}") $"c{i}_{j}" [| counter |] [||]
            )
    )
    |> Array.concat

Vitest.describe (
    "layoutGraph in a portrait box",
    fun () ->
        Vitest.test (
            "lays out top to bottom in a portrait box, even when the normal layout would fit",
            fun () -> promise {
                let! normal = layout layerNodes layerEdges [||] unknownBox
                let! compact = layout layerNodes layerEdges [||] (portraitBox (normal.LayoutWidth + 500.0))

                Vitest.expect(normal.Compact).toBe false
                Vitest.expect(compact.Compact).toBe true

                for source in sources do
                    for target in targets do
                        let upper = nodeOf compact source
                        Vitest.expect((nodeOf compact target).Y).toBeGreaterThan (upper.Y + upper.Height)
            }
        )

        Vitest.test (
            "keeps the normal layout, left to right, in a landscape box even when the graph overflows it, and in an unmeasured box",
            fun () -> promise {
                for width in [ 400.0; 0.0 ] do
                    let! result = layout layerNodes layerEdges [||] (inBox width)
                    let source = nodeOf result "s0"

                    Vitest.expect(result.Compact).toBe false
                    Vitest.expect((nodeOf result "t0").X).toBeGreaterThan (source.X + source.Width)
            }
        )

        Vitest.test (
            "does not depend on the width and ignores an expanded edge",
            fun () -> promise {
                let! plain = layout layerNodes layerEdges [||] (portraitBox 0.0)
                let! expanded = layout layerNodes layerEdges [| "e3" |] (portraitBox 300.0)

                Vitest.expect(expanded.Compact).toBe true

                Vitest
                    .expect(expanded.LaidOutNodes |> Array.map (fun n -> n.X, n.Y))
                    .toEqual (plain.LaidOutNodes |> Array.map (fun n -> n.X, n.Y))

                Vitest.expect((edgeOf expanded "e3").EdgePills.Length).toBe 1
            }
        )

        Vitest.test (
            "keeps the parent and the longest chain at the left of their layers, and the graph at the left edge",
            fun () -> promise {
                let! result = layout fanNodes fanEdges [||] (portraitBox 0.0)

                let layerOf (id: string) =
                    result.LaidOutNodes |> Array.filter (fun n -> n.Y = (nodeOf result id).Y)

                let chain = [| "c0_0"; "c0_1"; "c0_2"; "c0_3"; "c0_4" |]

                Vitest.expect(result.Compact).toBe true

                for id in Array.append [| "P" |] chain do
                    Vitest.expect((nodeOf result id).X).toBe (layerOf id |> Array.map (fun n -> n.X) |> Array.min)

                // The other children fan out to the right of the first one, in the order of the edges.
                let firstChildren =
                    [| "c0_0"; "c1_0"; "c2_0"; "c3_0" |]
                    |> Array.map (fun id -> (nodeOf result id).X)

                Vitest.expect(firstChildren).toEqual (Array.sort firstChildren)
                Vitest.expect((Array.distinct firstChildren).Length).toBe 4
                // ELK pads the graph by 12px, so the first column starts there.
                Vitest.expect(result.LaidOutNodes |> Array.map (fun n -> n.X) |> Array.min).toBe 12.0
                Vitest.expect((nodeOf result "c0_0").X).toBe 12.0
            }
        )

        Vitest.test (
            "spreads the edge ends over the width of a node in the compact layout, and over the height in the normal one",
            fun () -> promise {
                let! compact = layout fanNodes fanEdges [||] (portraitBox 0.0)
                let! normal = layout fanNodes fanEdges [||] unknownBox

                // The parent has four edge ends on one side, 28px each with 28px at the border.
                Vitest
                    .expect(
                        [|
                            (nodeOf compact "P").Width
                            (nodeOf compact "P").Height
                        |]
                    )
                    .toEqual
                    [| 140.0; 42.0 |]

                Vitest.expect([| (nodeOf normal "P").Width; (nodeOf normal "P").Height |]).toEqual [| 132.0; 140.0 |]
            }
        )

        Vitest.test (
            "shows one chip on every edge with several pills and keeps a single pill",
            fun () -> promise {
                let! result = layout layerNodes layerEdges [| "e3" |] (portraitBox 400.0)

                for laid in result.LaidOutEdges |> Array.filter (fun other -> other.Edge.Id <> "e5") do
                    let total = laid.Edge.Pills.Length + laid.Edge.MorePills.Length
                    Vitest.expect(laid.EdgePills.Length).toBe 1
                    Vitest.expect(laid.EdgePills.[0].View.Toggle).toEqual (Some ToggleKind.More)
                    Vitest.expect(laid.EdgePills.[0].View.ValuePart).toBe $"{total}"
                    Vitest.expect(laid.EdgePills.[0].View.Category).toEqual (Some PillCategory.Parameter)

                let single = (edgeOf result "e5").EdgePills
                Vitest.expect(single.Length).toBe 1
                Vitest.expect(single.[0].View.Kind).toBe PillViewKind.Count
                Vitest.expect(single.[0].View.Toggle).toEqual None
            }
        )

        Vitest.test (
            "puts every chip on a vertical segment of its edge",
            fun () -> promise {
                let! result = layout layerNodes layerEdges [||] (portraitBox 400.0)

                for laid in result.LaidOutEdges do
                    let pill = laid.EdgePills.[0]
                    let middle = pill.X + pill.View.Width / 2.0

                    let onSegment =
                        verticalSegments laid.Route
                        |> Array.exists (fun (x, top, bottom) ->
                            abs (x - middle) <= 1.0
                            && top <= pill.Y + 1.0
                            && bottom >= pill.Y + pillHeight - 1.0
                        )

                    Vitest.expect(onSegment).toBe true
            }
        )
)

Vitest.describe (
    "sameBox",
    fun () ->
        let measured (width: float) (height: float) (portrait: bool) : MeasuredBox = {
            ClientWidth = width
            ClientHeight = height
            Portrait = portrait
        }

        Vitest.test (
            "keeps the old box when both sizes change by less than the noise",
            fun () ->
                Vitest.expect(sameBox (measured 800.0 600.0 false) (measured 803.0 597.5 false)).toBe true
                Vitest.expect(sameBox (measured 800.0 600.0 false) (measured 804.0 600.0 false)).toBe false
                Vitest.expect(sameBox (measured 800.0 600.0 false) (measured 800.0 596.0 false)).toBe false
        )

        Vitest.test (
            "takes the new box when the shape changes, however small the size change",
            fun () -> Vitest.expect(sameBox (measured 800.0 600.0 false) (measured 800.0 600.0 true)).toBe false
        )
)

Vitest.describe (
    "isPortrait and effectiveLayoutWidth",
    fun () ->
        Vitest.test (
            "an unmeasured box is never portrait, a measured one is when it is taller than wide",
            fun () ->
                Vitest.expect(isPortrait 0.0 900.0 600.0).toBe false
                Vitest.expect(isPortrait 585.0 900.0 600.0).toBe true
                Vitest.expect(isPortrait 585.0 600.0 600.0).toBe false
                Vitest.expect(isPortrait 585.0 500.0 600.0).toBe false
        )

        Vitest.test (
            "the width reaches the layout only for an expanded edge in a landscape box",
            fun () ->
                Vitest.expect(effectiveLayoutWidth false 1 585.0).toBe 585.0
                Vitest.expect(effectiveLayoutWidth false 0 585.0).toBe 0.0
                Vitest.expect(effectiveLayoutWidth true 2 585.0).toBe 0.0
        )
)

Vitest.describe (
    "pillMenuPlacement",
    fun () ->
        // The visible area is x 101 to 701 and y 51 to 451: the root at (100, 50) plus a 1px border,
        // 600 by 400 inside.
        let geometry (anchor: Rect) : PillMenuGeometry = {
            Root = {
                Left = 100.0
                Top = 50.0
                Right = 702.0
                Bottom = 452.0
            }
            RootClientLeft = 1.0
            RootClientTop = 1.0
            RootClientWidth = 600.0
            RootClientHeight = 400.0
            Anchor = anchor
            Origin = {
                Left = 90.0
                Top = 40.0
                Right = 900.0
                Bottom = 900.0
            }
        }

        Vitest.test (
            "opens below the toggle when there is more room below",
            fun () ->
                let anchor = {
                    Left = 300.0
                    Top = 100.0
                    Right = 330.0
                    Bottom = 118.0
                }

                let placement = pillMenuPlacement (geometry anchor) (fun _ _ -> 200.0)

                Vitest.expect(placement.OpenAbove).toBe false
                Vitest.expect(placement.MaxWidth).toBe 584.0
                Vitest.expect(placement.MaxHeight).toBe 321.0
                Vitest.expect(placement.MenuTop).toEqual (Some 82.0)
                Vitest.expect(placement.MenuBottom).toEqual None
                Vitest.expect(placement.MenuLeft).toBe 210.0
        )

        Vitest.test (
            "opens above the toggle when there is more room above, and keeps the list inside the right edge",
            fun () ->
                let anchor = {
                    Left = 650.0
                    Top = 380.0
                    Right = 680.0
                    Bottom = 398.0
                }

                let placement = pillMenuPlacement (geometry anchor) (fun _ _ -> 200.0)

                Vitest.expect(placement.OpenAbove).toBe true
                Vitest.expect(placement.MaxHeight).toBe 317.0
                Vitest.expect(placement.MenuTop).toEqual None
                Vitest.expect(placement.MenuBottom).toEqual (Some 524.0)
                Vitest.expect(placement.MenuLeft).toBe 403.0
        )

        Vitest.test (
            "reads the width of the list after the max sizes are applied",
            fun () ->
                let anchor = {
                    Left = 300.0
                    Top = 100.0
                    Right = 330.0
                    Bottom = 118.0
                }

                let applied = ResizeArray<float * float>()

                // A list that wants 700px is cut to the max width, so it starts at the left margin.
                let placement =
                    pillMenuPlacement
                        (geometry anchor)
                        (fun maxWidth maxHeight ->
                            applied.Add((maxWidth, maxHeight))
                            min 700.0 maxWidth
                        )

                Vitest.expect(applied.ToArray()).toEqual [| 584.0, 321.0 |]
                Vitest.expect(placement.MenuLeft).toBe 19.0
        )
)

Vitest.describe (
    "measureNode",
    fun () ->
        Vitest.test (
            "measures the title at weight 600 and 13px in the mono flag of the node, and the subtitle at weight 400 and 12px",
            fun () ->
                let calls = ResizeArray<string * float * float * bool>()

                let recording: MeasureText =
                    fun text weight size mono ->
                        calls.Add((text, weight, size, mono))
                        Dom.estimateTextWidth text

                let named = {
                    titled "n" "Name" with
                        Subtitle = Some "Sub"
                        Mono = true
                }

                measureNode recording 0 false named |> ignore

                Vitest.expect(calls.ToArray()).toEqual [|
                    ("Name", 600.0, 13.0, true)
                    ("Sub", 400.0, 12.0, false)
                |]
        )
)
