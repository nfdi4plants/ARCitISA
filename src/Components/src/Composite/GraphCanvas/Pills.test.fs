module internal Arcitisa.Components.Composite.GraphCanvas.PillsTests

// Ported from graphCanvasPills.test.ts of arc-portal-ui. Every case is ported. Two assertions are
// left out because they pin fixed UI copy: the "(empty)" value text in the null value case and the
// "Show the 3 details" tooltip of the compact chip. The remaining assertions keep their values.
//
// The tests measure with the 7px estimate, which ignores the weight, size and mono arguments, as the
// TypeScript tests do. The toPillView case is new and checks the arguments that reach the measure.

open Vitest
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Composite.GraphCanvas.Types
open Arcitisa.Components.Composite.GraphCanvas.Pills

let private property (name: string) (value: string option) (unit: string option) : CanvasPill = {
    Key = name
    Kind = PillKind.Property
    Name = Some name
    Value = value
    Unit = unit
    Category = Some PillCategory.Parameter
    Tooltip = None
}

let private shownText (pill: CanvasPill) : string =
    let text = describePill pill
    text.NamePart + text.ValuePart

let private sized (width: float) (key: string) : PillView = {
    Key = key
    Kind = PillViewKind.Property
    Category = None
    NamePart = ""
    ValuePart = ""
    Empty = false
    FullText = ""
    Tooltip = ""
    Toggle = None
    Width = width
}

let private keys (rows: PillView[][]) : string[][] =
    rows |> Array.map (Array.map (fun view -> view.Key))

let private estimate: MeasureText = fun text _ _ _ -> Dom.estimateTextWidth text

Vitest.describe (
    "describePill",
    fun () ->
        Vitest.test (
            "cuts the name when the value is short",
            fun () ->
                Vitest.expect(shownText (property "Chromatography injection mode" (Some "splitless") None)).toBe
                    "Chromatography injection…: splitless"
        )

        Vitest.test (
            "cuts a value over 24 characters to 23 and shrinks the name to the rest of the budget",
            fun () ->
                Vitest
                    .expect(
                        shownText (
                            property
                                "Chromatography autosampler model"
                                (Some "GERSTEL MultiPurpose Sampler (MPS) Dual Head")
                                None
                        )
                    )
                    .toBe
                    "Chromatog…: GERSTEL MultiPurpose Sa…"

                Vitest
                    .expect(shownText (property "watering exposure" (Some "12 days drought + 2 days rewatered") None))
                    .toBe
                    "watering…: 12 days drought + 2 day…"
        )

        Vitest.test (
            "shows a null value as empty, never cuts it and lets the name give way",
            fun () ->
                let text = describePill (property "Chromatography autosampler model" None None)

                Vitest.expect(text.NamePart).toBe "Chromatography autosampler…: "
                Vitest.expect(text.Empty).toBe true
        )

        Vitest.test (
            "cuts a process name to 28 characters and keeps it whole in the tooltip",
            fun () ->
                let name = "Gas chromatography with mass spectrometry"

                let pill: CanvasPill = {
                    Key = "process"
                    Kind = PillKind.Process
                    Name = None
                    Value = Some name
                    Unit = None
                    Category = None
                    Tooltip = None
                }

                let text = describePill pill

                Vitest.expect(text.ValuePart).toBe "Gas chromatography with mas…"
                Vitest.expect(text.ValuePart.Length).toBe 28
                Vitest.expect(text.Tooltip).toBe name
        )

        Vitest.test (
            "takes the tooltip of a count pill from the pill and falls back to its text",
            fun () ->
                let count: CanvasPill = {
                    Key = "count"
                    Kind = PillKind.Count
                    Name = None
                    Value = Some "3"
                    Unit = None
                    Category = None
                    Tooltip = None
                }

                Vitest.expect((describePill count).Tooltip).toBe "3"

                Vitest
                    .expect(
                        (describePill {
                            count with
                                Tooltip = Some "3 shared nodes"
                        })
                            .Tooltip
                    )
                    .toBe
                    "3 shared nodes"
        )

        Vitest.test (
            "keeps the full text with its category in the tooltip",
            fun () ->
                let pill =
                    property
                        "Chromatography autosampler model"
                        (Some "GERSTEL MultiPurpose Sampler (MPS) Dual Head")
                        (Some "x")

                Vitest.expect((describePill pill).Tooltip).toBe
                    "Parameter · Chromatography autosampler model: GERSTEL MultiPurpose Sampler (MPS) Dual Head x"
        )
)

Vitest.describe (
    "wrapPillRows",
    fun () ->
        Vitest.test (
            "fills a row from the left until the next pill does not fit",
            fun () ->
                let rows =
                    wrapPillRows
                        [|
                            sized 100.0 "a"
                            sized 100.0 "b"
                            sized 100.0 "c"
                            sized 40.0 "d"
                        |]
                        212.0

                Vitest.expect(keys rows).toEqual [| [| "a"; "b" |]; [| "c"; "d" |] |]
                Vitest.expect(rows |> Array.map rowWidth).toEqual [| 206.0; 146.0 |]
        )

        Vitest.test (
            "counts a gap between two pills as part of the row",
            fun () ->
                let two = [| sized 100.0 "a"; sized 100.0 "b" |]

                Vitest.expect((wrapPillRows two (100.0 + pillGap + 99.0)).Length).toBe 2
                Vitest.expect((wrapPillRows two (100.0 + pillGap + 100.0)).Length).toBe 1
        )

        Vitest.test (
            "gives a pill wider than the cap a row of its own and cuts nothing",
            fun () ->
                let rows =
                    wrapPillRows
                        [|
                            sized 50.0 "a"
                            sized 300.0 "wide"
                            sized 50.0 "b"
                            sized 50.0 "c"
                        |]
                        120.0

                Vitest.expect(keys rows).toEqual [| [| "a" |]; [| "wide" |]; [| "b"; "c" |] |]
        )

        Vitest.test ("returns no row for no pill", fun () -> Vitest.expect((wrapPillRows [||] 100.0).Length).toBe 0)
)

Vitest.describe (
    "pillBlock",
    fun () ->
        let views = [| sized 100.0 "a"; sized 100.0 "b"; sized 100.0 "c" |]

        Vitest.test (
            "keeps a collapsed edge in one row however wide it is",
            fun () ->
                let block = pillBlock views None

                Vitest.expect(block.Rows).toEqual [| views |]
                Vitest.expect(block.Width).toBe (3.0 * 100.0 + 2.0 * pillGap + 32.0)
                Vitest.expect(block.Height).toBe pillHeight
        )

        Vitest.test (
            "wraps an expanded edge at the row cap and measures all rows",
            fun () ->
                let block = pillBlock views (Some 210.0)

                Vitest.expect(block.Rows |> Array.map Array.length).toEqual [| 2; 1 |]
                Vitest.expect(block.Width).toBe (2.0 * 100.0 + pillGap + 32.0)
                Vitest.expect(block.Height).toBe (2.0 * pillHeight + pillGap)
        )

        Vitest.test (
            "never wraps narrower than the widest pill",
            fun () ->
                let block =
                    pillBlock
                        [|
                            sized 80.0 "a"
                            sized 150.0 "b"
                            sized 60.0 "c"
                            sized 60.0 "d"
                        |]
                        (Some 10.0)

                Vitest.expect(keys block.Rows).toEqual [| [| "a" |]; [| "b" |]; [| "c"; "d" |] |]
                Vitest.expect(block.Width).toBe (150.0 + 32.0)
        )
)

Vitest.describe (
    "compactPillViews",
    fun () ->
        let measure = estimate

        let parameter: CanvasPill = {
            Key = "p"
            Kind = PillKind.Property
            Name = Some "temperature"
            Value = Some "20"
            Unit = None
            Category = Some PillCategory.Parameter
            Tooltip = None
        }

        let factor: CanvasPill = {
            parameter with
                Key = "f"
                Name = Some "light"
                Value = Some "dark"
                Category = Some PillCategory.Factor
        }

        let count: CanvasPill = {
            Key = "c"
            Kind = PillKind.Count
            Name = None
            Value = Some "6"
            Unit = None
            Category = None
            Tooltip = None
        }

        let edge (pills: CanvasPill[]) (morePills: CanvasPill[]) : CanvasEdge = {
            Id = "e"
            From = "a"
            To = "b"
            Color = CanvasColor.Study
            Highlighted = false
            Pills = pills
            MorePills = morePills
            Tooltip = None
        }

        Vitest.test (
            "keeps a lone pill and shows nothing for an edge without pills",
            fun () ->
                let lone = compactPillViews measure (edge [| count |] [||])

                Vitest.expect(lone |> Array.map (fun view -> view.Key, view.Toggle)).toEqual [| "c", None |]

                Vitest
                    .expect(
                        compactPillViews measure (edge [||] [| count |])
                        |> Array.map (fun view -> view.Key)
                    )
                    .toEqual
                    [| "c" |]

                Vitest.expect((compactPillViews measure (edge [||] [||])).Length).toBe 0
        )

        Vitest.test (
            "turns several pills, the hidden ones included, into one chip with the first property category",
            fun () ->
                let views = compactPillViews measure (edge [| count; factor |] [| parameter |])
                let chip = views.[0]

                Vitest.expect(views.Length).toBe 1
                Vitest.expect(chip.Kind).toBe PillViewKind.Toggle
                Vitest.expect(chip.Toggle).toEqual (Some ToggleKind.More)
                Vitest.expect(chip.ValuePart).toBe "3"
                Vitest.expect(chip.Category).toEqual (Some PillCategory.Factor)
                // The dot takes 10px more than a chip without one.
                let withoutDot = (compactPillViews measure (edge [| count; count |] [||])).[0]
                Vitest.expect(chip.Width).toBe (withoutDot.Width + 10.0 + 7.0 * float ("3".Length - "2".Length))
        )

        Vitest.test (
            "shows no dot when no pill is a property",
            fun () -> Vitest.expect((compactPillViews measure (edge [| count |] [| count |])).[0].Category).toEqual None
        )
)

Vitest.describe (
    "pillListText",
    fun () ->
        Vitest.test (
            "writes a property in full and any other pill by its tooltip",
            fun () ->
                let property: CanvasPill = {
                    Key = "p"
                    Kind = PillKind.Property
                    Name = Some "Chromatography injection mode"
                    Value = Some "splitless"
                    Unit = Some "x"
                    Category = None
                    Tooltip = None
                }

                let count: CanvasPill = {
                    Key = "c"
                    Kind = PillKind.Count
                    Name = None
                    Value = Some "3"
                    Unit = None
                    Category = None
                    Tooltip = Some "3 shared nodes"
                }

                let processPill: CanvasPill = {
                    Key = "o"
                    Kind = PillKind.Process
                    Name = None
                    Value = Some "Gas chromatography with mass spectrometry"
                    Unit = None
                    Category = None
                    Tooltip = None
                }

                Vitest.expect(pillListText property).toBe "Chromatography injection mode: splitless x"
                Vitest.expect(pillListText count).toBe "3 shared nodes"
                Vitest.expect(pillListText processPill).toBe "Gas chromatography with mass spectrometry"
        )
)

Vitest.describe (
    "pill measuring",
    fun () ->
        Vitest.test (
            "measures process pills at weight 600, other pills and toggles at weight 400, all at 11px",
            fun () ->
                let calls = ResizeArray<string * float * float * bool>()

                let recording: MeasureText =
                    fun text weight size mono ->
                        calls.Add((text, weight, size, mono))
                        Dom.estimateTextWidth text

                let pill (kind: PillKind) (value: string) : CanvasPill = {
                    Key = value
                    Kind = kind
                    Name = None
                    Value = Some value
                    Unit = None
                    Category = None
                    Tooltip = None
                }

                toPillView recording (pill PillKind.Process "step") |> ignore
                toPillView recording (pill PillKind.Count "3") |> ignore

                let edge: CanvasEdge = {
                    Id = "e"
                    From = "a"
                    To = "b"
                    Color = CanvasColor.Study
                    Highlighted = false
                    Pills = [||]
                    MorePills = [| pill PillKind.Count "4" |]
                    Tooltip = None
                }

                edgePillViews recording edge false |> ignore

                Vitest.expect(calls.ToArray()).toEqual [|
                    ("step", 600.0, 11.0, false)
                    ("3", 400.0, 11.0, false)
                    ("+1", 400.0, 11.0, false)
                |]
        )
)
