module Swate.Components.Composite.GraphCanvas.Types

open Fable.Core

// Input of the graph canvas and the result of its layout. A color picks the palette entry of a node or
// edge, a pill category picks the color of a pill. Everything else is plain data.

/// Width in px of a text: text, font weight, font size in px, monospace. A real measure sets the font on a
/// canvas, a test measure can ignore the last three arguments.
type MeasureText = string -> float -> float -> bool -> float

[<StringEnum; RequireQualifiedAccess>]
type CanvasColor =
    | Study
    | Assay
    | Run
    | Neutral

/// Property pills are "name: value unit" with a category color, process pills name a process,
/// count pills show a bare number or text.
[<StringEnum; RequireQualifiedAccess>]
type PillKind =
    | Property
    | Process
    | Count

[<StringEnum; RequireQualifiedAccess>]
type PillCategory =
    | Characteristic
    | Factor
    | Parameter
    | Component
    | Protocol
    | Performer
    | Date
    | Comment

type CanvasPill = {
    Key: string
    Kind: PillKind
    Name: string option
    /// None is an empty value, shown as "(empty)".
    Value: string option
    Unit: string option
    Category: PillCategory option
    /// Hover text and accessible name of process and count pills.
    Tooltip: string option
}

type CanvasNode = {
    Id: string
    Title: string
    Subtitle: string option
    Color: CanvasColor
    Dashed: bool
    Mono: bool
    Selected: bool
    Emphasized: bool
    Pills: CanvasPill[]
    Tooltip: string option
    AriaLabel: string
    /// False renders the node without button semantics.
    Clickable: bool
}

type CanvasEdge = {
    Id: string
    From: string
    To: string
    Color: CanvasColor
    Highlighted: bool
    /// Always visible on the edge.
    Pills: CanvasPill[]
    /// Hidden behind a "+N" pill until the user expands the edge.
    MorePills: CanvasPill[]
    Tooltip: string option
}

/// The visible text parts of one pill.
type PillText = {
    /// "name: " (empty for pills without a name), shown in the muted color.
    NamePart: string
    ValuePart: string
    Empty: bool
    /// "name: value unit" in full, without a category. Screen reader labels of nodes and edges use it.
    FullText: string
    /// Hover text and accessible name. Property pills add their category. Process and count pills
    /// use the tooltip of the pill and fall back to the full text.
    Tooltip: string
}

[<StringEnum; RequireQualifiedAccess>]
type PillViewKind =
    | Property
    | Process
    | Count
    | Toggle

/// The "+N" and "-" buttons of an edge are toggle pills.
[<StringEnum; RequireQualifiedAccess>]
type ToggleKind =
    | More
    | Less

/// A pill ready to be sized and drawn.
type PillView = {
    Key: string
    Kind: PillViewKind
    Category: PillCategory option
    NamePart: string
    ValuePart: string
    Empty: bool
    FullText: string
    Tooltip: string
    Toggle: ToggleKind option
    Width: float
}

type PillBlock = {
    Rows: PillView[][]
    /// Widest row plus the label padding on both sides, which is what ELK reserves.
    Width: float
    /// All rows with the gaps between them.
    Height: float
}

type Point = { X: float; Y: float }

/// A pill with the position of its top left corner.
type PlacedPill = { View: PillView; X: float; Y: float }

/// Size and inner positions of a node. Positions are relative to the node origin.
type NodeBox = {
    Width: float
    Height: float
    TitleText: string
    SubtitleText: string
    TitleY: float
    SubtitleY: float
    Pills: PlacedPill[]
}

type LaidOutNode = {
    Node: CanvasNode
    X: float
    Y: float
    Width: float
    Height: float
    TitleText: string
    SubtitleText: string
    TitleY: float
    SubtitleY: float
    Pills: PlacedPill[]
}

type LaidOutEdge = {
    Edge: CanvasEdge
    Route: Point[]
    Path: string
    /// Positions relative to the canvas origin. An expanded edge has several rows, the first one on the route.
    EdgePills: PlacedPill[]
    /// Smallest available width at which this edge can expand inline, see Layout.fitsInline.
    MinInlineWidth: float
}

type GraphLayout = {
    LayoutWidth: float
    LayoutHeight: float
    /// Top to bottom with one chip per edge.
    Compact: bool
    LaidOutNodes: LaidOutNode[]
    LaidOutEdges: LaidOutEdge[]
}

/// Width is the visible width of the box in px, 0 while unknown (before the first measurement, and in jsdom).
/// Portrait selects the compact layout.
type LayoutOptions = { Width: float; Portrait: bool }

/// The size of the box, as read from the DOM. Portrait is the result of Layout.isPortrait, so it is false
/// while the box is unmeasured. It follows the border box, so a scrollbar does not change it.
type MeasuredBox = {
    ClientWidth: float
    ClientHeight: float
    Portrait: bool
}

/// The box of an ELK label.
type LabelBox = {
    LabelX: float
    LabelY: float
    LabelWidth: float
    LabelHeight: float
}

/// A DOMRect as plain numbers.
type Rect = {
    Left: float
    Top: float
    Right: float
    Bottom: float
}

/// What the scrolling root reports (scroll position, client size and scroll size), plus the origin of the
/// drawing in scroll coordinates and the zoom. The minimap computes with these numbers only.
type ScrollGeometry = {
    ScrollLeft: float
    ScrollTop: float
    ClientWidth: float
    ClientHeight: float
    ScrollWidth: float
    ScrollHeight: float
    /// Left edge of the drawing in the scroll coordinates of the root, in px.
    OffsetX: float
    /// Top edge of the drawing in the scroll coordinates of the root, in px.
    OffsetY: float
    Zoom: float
}

/// Everything the placement of the pill list reads from the DOM.
type PillMenuGeometry = {
    /// getBoundingClientRect of the scrolling root.
    Root: Rect
    RootClientLeft: float
    RootClientTop: float
    RootClientWidth: float
    RootClientHeight: float
    /// getBoundingClientRect of the toggle that opened the list.
    Anchor: Rect
    /// getBoundingClientRect of the element the list is positioned in.
    Origin: Rect
}

/// Style values of the pill list in px. None for top or bottom is CSS "auto".
type PillMenuPlacement = {
    MaxWidth: float
    MaxHeight: float
    OpenAbove: bool
    MenuTop: float option
    MenuBottom: float option
    MenuLeft: float
}

/// The custom properties the canvas colors read. Set them as inline style on the root of the canvas.
let palette: (string * string) list = [
    "--prov-study", "color-mix(in oklch, oklch(0.62 0.17 255) 65%, var(--color-base-content))"
    "--prov-assay", "color-mix(in oklch, oklch(0.62 0.17 300) 65%, var(--color-base-content))"
    "--prov-run", "color-mix(in oklch, oklch(0.62 0.13 195) 65%, var(--color-base-content))"
    "--prov-characteristic", "color-mix(in oklch, oklch(0.62 0.15 145) 65%, var(--color-base-content))"
    "--prov-factor", "color-mix(in oklch, oklch(0.68 0.15 75) 65%, var(--color-base-content))"
    "--prov-parameter", "color-mix(in oklch, oklch(0.62 0.18 30) 65%, var(--color-base-content))"
    "--prov-component", "color-mix(in oklch, oklch(0.62 0.17 350) 65%, var(--color-base-content))"
    "--prov-other", "color-mix(in oklch, var(--color-base-content) 60%, transparent)"
]

/// Border of neutral nodes, pills, the pill list and the zoom controls. Base content at 60 percent reaches
/// a contrast of 3 to 1 on base-100 and base-200 in the five app themes (finster, sunrise, planti, viola, amaze).
let neutralBorder =
    "color-mix(in oklch, var(--color-base-content) 60%, transparent)"

/// Stroke and border of selected nodes, highlighted edges, the accent arrow and highlighted process pills.
/// Primary mixed with base content reaches more contrast than primary alone, 3 to 1 or more in the five themes.
let accentColor =
    "color-mix(in oklch, var(--color-primary) 50%, var(--color-base-content))"

/// Color of text in the accent, such as the links of the breadcrumbs. Text needs 4.5 to 1, which the
/// accent of the strokes does not reach in the amaze theme, so this mix keeps less of the primary color.
let accentTextColor =
    "color-mix(in oklch, var(--color-primary) 35%, var(--color-base-content))"

/// Stroke of neutral edges and their arrows. Same share of base content as the borders.
let neutralEdge = "color-mix(in oklch, var(--color-base-content) 60%, transparent)"

/// Secondary text, such as the names of pills and the subtitles of nodes. Base content at 80 percent reaches
/// a contrast of 4.5 to 1 on base-100 and base-200 in the five themes.
let mutedTextColor =
    "color-mix(in oklch, var(--color-base-content) 80%, transparent)"

let colorVar (color: CanvasColor) : string =
    match color with
    | CanvasColor.Study -> "var(--prov-study)"
    | CanvasColor.Assay -> "var(--prov-assay)"
    | CanvasColor.Run -> "var(--prov-run)"
    | CanvasColor.Neutral -> neutralEdge

let categoryVar (category: PillCategory) : string =
    match category with
    | PillCategory.Characteristic -> "var(--prov-characteristic)"
    | PillCategory.Factor -> "var(--prov-factor)"
    | PillCategory.Parameter -> "var(--prov-parameter)"
    | PillCategory.Component -> "var(--prov-component)"
    | PillCategory.Protocol
    | PillCategory.Performer
    | PillCategory.Date
    | PillCategory.Comment -> "var(--prov-other)"
