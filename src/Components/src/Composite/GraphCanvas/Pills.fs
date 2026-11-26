module Arcitisa.Components.Composite.GraphCanvas.Pills

open Arcitisa.Components.Composite.GraphCanvas.Types

// Pure text and size logic of the pills. Nothing here touches the DOM. Widths come from a measure
// function that maps a text to its width in px.

let pillHeight = 18.0
let pillGap = 6.0

/// Room the edge label keeps free on each side of the pills.
let labelPadding = 16.0

// Characters that "name: value unit" of a property pill may use before the name gives way.
let private textBudget = 36
let private valueLimit = 24
let private minNameBudget = 8
// Process names are the longest pill text, so they get their own limit.
let private processLimit = 28

let categoryLabel (category: PillCategory) : string =
    match category with
    | PillCategory.Characteristic -> "Characteristic"
    | PillCategory.Factor -> "Factor"
    | PillCategory.Parameter -> "Parameter"
    | PillCategory.Component -> "Component"
    | PillCategory.Protocol -> "Protocol"
    | PillCategory.Performer -> "Performer"
    | PillCategory.Date -> "Date"
    | PillCategory.Comment -> "Comment"

let private slice (text: string) (length: int) : string =
    if text.Length <= length then
        text
    else
        text.Substring(0, length)

let private cut (text: string) (length: int) : string = (slice text length).TrimEnd() + "…"

/// The value keeps up to 24 characters and the name gives way first, down to 8. Returns name and value.
let truncateProperty (name: string) (valueText: string) : string * string =
    let value =
        if valueText.Length > valueLimit then
            cut valueText (valueLimit - 1)
        else
            valueText

    let nameBudget = max minNameBudget (textBudget - 2 - value.Length)

    let shownName =
        if name.Length > nameBudget then
            cut name (nameBudget - 1)
        else
            name

    shownName, value

let describePill (pill: CanvasPill) : PillText =
    let empty =
        match pill.Value with
        | None
        | Some "" -> true
        | Some _ -> false

    let valueText =
        match pill.Value with
        | Some value when not empty ->
            match pill.Unit with
            | Some unit when unit <> "" -> value + " " + unit
            | _ -> value
        | _ -> "(empty)"

    if pill.Kind <> PillKind.Property then
        let shown =
            if pill.Kind = PillKind.Process && valueText.Length > processLimit then
                cut valueText (processLimit - 1)
            else
                valueText

        {
            NamePart = ""
            ValuePart = shown
            Empty = empty
            FullText = valueText
            Tooltip = defaultArg pill.Tooltip valueText
        }
    else
        let name = defaultArg pill.Name ""

        let fullText = if name <> "" then name + ": " + valueText else valueText

        let shownName, shownValue = truncateProperty name valueText

        {
            NamePart = if shownName <> "" then shownName + ": " else ""
            ValuePart = shownValue
            Empty = empty
            FullText = fullText
            Tooltip =
                match pill.Category with
                | Some category -> categoryLabel category + " · " + fullText
                | None -> fullText
        }

let private viewKind (kind: PillKind) : PillViewKind =
    match kind with
    | PillKind.Property -> PillViewKind.Property
    | PillKind.Process -> PillViewKind.Process
    | PillKind.Count -> PillViewKind.Count

/// Width of a pill: its text at 11px, 7 px of padding on each side and 10 px for the category dot.
let private pillWidth (measure: MeasureText) (weight: float) (text: string) (category: PillCategory option) : float =
    ceil (measure text weight 11.0 false)
    + 14.0
    + (if category.IsSome then 10.0 else 0.0)

let toPillView (measure: MeasureText) (pill: CanvasPill) : PillView =
    let text = describePill pill

    let category =
        if pill.Kind = PillKind.Property then
            pill.Category
        else
            None

    // Process names are set in a heavier weight than the other pills.
    let weight = if pill.Kind = PillKind.Process then 600.0 else 400.0

    {
        Key = pill.Key
        Kind = viewKind pill.Kind
        Category = category
        NamePart = text.NamePart
        ValuePart = text.ValuePart
        Empty = text.Empty
        FullText = text.FullText
        Tooltip = text.Tooltip
        Toggle = None
        Width = pillWidth measure weight (text.NamePart + text.ValuePart) category
    }

let private toggleView
    (measure: MeasureText)
    (key: string)
    (toggle: ToggleKind)
    (text: string)
    (label: string)
    (category: PillCategory option)
    : PillView =
    {
        Key = key
        Kind = PillViewKind.Toggle
        Category = category
        NamePart = ""
        ValuePart = text
        Empty = false
        FullText = label
        Tooltip = label
        Toggle = Some toggle
        Width = pillWidth measure 400.0 text category
    }

/// The pills an edge shows: its own, then either a "+N" pill or, when expanded, the hidden ones and a "−" pill.
let edgePillViews (measure: MeasureText) (edge: CanvasEdge) (expanded: bool) : PillView[] =
    let views = edge.Pills |> Array.map (toPillView measure)

    if edge.MorePills.Length = 0 then
        views
    else
        let toggleKey = edge.Id + ":toggle"

        if expanded then
            Array.concat [
                views
                edge.MorePills |> Array.map (toPillView measure)
                [|
                    toggleView measure toggleKey ToggleKind.Less "−" "Show fewer properties" None
                |]
            ]
        else
            let count = edge.MorePills.Length

            Array.append views [|
                toggleView measure toggleKey ToggleKind.More $"+{count}" $"Show {count} more properties" None
            |]

/// The pills an edge shows in compact mode. A lone pill stays on the edge. Several pills, hidden ones
/// included, become one chip with the number of pills and the category dot of the first property.
let compactPillViews (measure: MeasureText) (edge: CanvasEdge) : PillView[] =
    let all = Array.append edge.Pills edge.MorePills

    if all.Length <= 1 then
        all |> Array.map (toPillView measure)
    else
        let category =
            all
            |> Array.tryFind (fun pill -> pill.Kind = PillKind.Property)
            |> Option.bind (fun pill -> pill.Category)

        [|
            toggleView
                measure
                (edge.Id + ":toggle")
                ToggleKind.More
                $"{all.Length}"
                $"Show the {all.Length} details"
                category
        |]

/// Plain text of a pill for lists and screen readers: a property in full, any other pill by its tooltip.
let pillListText (pill: CanvasPill) : string =
    let text = describePill pill

    if pill.Kind = PillKind.Property then
        text.FullText
    else
        text.Tooltip

let rowWidth (row: PillView[]) : float =
    (row |> Array.sumBy (fun view -> view.Width)) + float (row.Length - 1) * pillGap

/// Fills the rows from left to right. A pill that does not fit starts the next row, and a pill wider
/// than maxWidth stands alone in its row, because pills are never cut.
let wrapPillRows (views: PillView[]) (maxWidth: float) : PillView[][] =
    let rows = ResizeArray<ResizeArray<PillView>>()
    let mutable used = 0.0

    for view in views do
        if rows.Count > 0 && used + pillGap + view.Width <= maxWidth then
            rows.[rows.Count - 1].Add view
            used <- used + pillGap + view.Width
        else
            rows.Add(ResizeArray [ view ])
            used <- view.Width

    rows |> Seq.map Seq.toArray |> Seq.toArray

/// A collapsed edge (no rowCap) keeps its pills in one row however wide it is. An expanded edge wraps
/// at rowCap, but never below its widest pill, so the block is at least as wide as that pill needs.
let pillBlock (views: PillView[]) (rowCap: float option) : PillBlock =
    let rows =
        match rowCap with
        | None -> [| views |]
        | Some cap -> wrapPillRows views (views |> Array.fold (fun widest view -> max widest view.Width) cap)

    {
        Rows = rows
        Width =
            (rows |> Array.fold (fun widest row -> max widest (rowWidth row)) -infinity)
            + 2.0 * labelPadding
        Height = float rows.Length * pillHeight + float (rows.Length - 1) * pillGap
    }
