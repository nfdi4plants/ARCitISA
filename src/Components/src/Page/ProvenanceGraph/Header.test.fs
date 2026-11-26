module internal Arcitisa.Components.Page.ProvenanceGraph.HeaderTests

// Ported from the header cases of ProvenanceExplorer.test.tsx of arc-portal-ui, as far as they test a
// rule. The widths are the pairs of setHeaderWidths(available, natural) in that file.
//
// Dropped PORTAL cases, because they render the React header and check the DOM: the query name next to
// the crumbs, the aria label and the click on the menu button, the menu items and the navigation to a
// chosen item, the title attributes of the shrinking crumbs, aria-current, and the ResizeObserver
// wiring. ProvenanceGraph.test.rtl.fs renders the header and covers these cases. The target of a crumb
// is checked in ToCanvas.test.fs.

open Vitest
open Arcitisa.Components.Page.ProvenanceGraph.Types
open Arcitisa.Components.Page.ProvenanceGraph.Header

let private crumbsOf (labels: string list) : Crumb[] =
    labels
    |> List.map (fun label -> {
        Label = label
        Target = ProvenanceView.Datasets
    })
    |> Array.ofList

let private labels (crumbs: Crumb[]) = crumbs |> Array.map (fun c -> c.Label)

Vitest.describe (
    "isNarrow",
    fun () ->
        Vitest.test ("keeps the full header when it fits", fun () -> Vitest.expect(isNarrow 640.0 900.0).toBe false)

        Vitest.test (
            "keeps the full header when it fits to the pixel and switches when it is a fraction wider",
            fun () ->
                // Available 640.4 and natural 640 fit. Available 640 and natural 640.2 do not.
                Vitest.expect(isNarrow 640.0 640.4).toBe false
                Vitest.expect(isNarrow 640.2 640.0).toBe true
        )

        Vitest.test (
            "counts a fraction of the natural width as a whole pixel and a fraction of the available width as nothing",
            fun () ->
                Vitest.expect(isNarrow 640.0 640.0).toBe false
                Vitest.expect(isNarrow 640.01 640.0).toBe true
                Vitest.expect(isNarrow 640.0 639.99).toBe true
                Vitest.expect(isNarrow 639.0 639.9).toBe false
        )

        Vitest.test (
            "switches to the narrow header when the box shrinks and back when it grows",
            fun () ->
                Vitest.expect(isNarrow 640.0 330.0).toBe true
                Vitest.expect(isNarrow 640.0 900.0).toBe false
        )

        Vitest.test (
            "brings the full header back when the path gets shorter",
            fun () ->
                Vitest.expect(isNarrow 640.0 330.0).toBe true
                Vitest.expect(isNarrow 250.0 330.0).toBe false
        )

        Vitest.test (
            "decides by the two widths only, so repeating a measurement gives the same answer",
            fun () ->
                let answers = [| for _ in 1..4 -> isNarrow 640.2 640.0 |]
                Vitest.expect(answers).toEqual [| true; true; true; true |]
        )

        Vitest.test (
            "starts with the full header while the box width is unknown",
            fun () -> Vitest.expect(isNarrow 640.0 infinity).toBe false
        )
)

Vitest.describe (
    "visibleCrumbs",
    fun () ->
        let five = crumbsOf [ "Datasets"; "dataset"; "process"; "group"; "node" ]

        Vitest.test (
            "shows the first crumb and the last two and hides the rest when the header is narrow",
            fun () ->
                let layout = visibleCrumbs five true
                Vitest.expect(labels layout.Visible).toEqual [| "Datasets"; "group"; "node" |]
                Vitest.expect(labels layout.Hidden).toEqual [| "dataset"; "process" |]
        )

        Vitest.test (
            "shows all crumbs when the header is not narrow",
            fun () ->
                let layout = visibleCrumbs five false
                Vitest.expect(labels layout.Visible).toEqual (labels five)
                Vitest.expect(layout.Hidden.Length).toBe 0
        )

        Vitest.test (
            "collapses nothing with three crumbs",
            fun () ->
                let three = crumbsOf [ "Datasets"; "dataset"; "process" ]
                let layout = visibleCrumbs three true
                Vitest.expect(labels layout.Visible).toEqual (labels three)
                Vitest.expect(layout.Hidden.Length).toBe 0
        )

        Vitest.test (
            "collapses the one crumb in the middle of four",
            fun () ->
                let four = crumbsOf [ "Datasets"; "process"; "group"; "node" ]
                let layout = visibleCrumbs four true
                Vitest.expect(labels layout.Visible).toEqual [| "Datasets"; "group"; "node" |]
                Vitest.expect(labels layout.Hidden).toEqual [| "process" |]
        )

        Vitest.test (
            "leaves one and two crumbs alone",
            fun () ->
                let one = crumbsOf [ "Datasets" ]
                let two = crumbsOf [ "Datasets"; "dataset" ]
                Vitest.expect((visibleCrumbs one true).Hidden.Length).toBe 0
                Vitest.expect(labels (visibleCrumbs two true).Visible).toEqual [| "Datasets"; "dataset" |]
        )

        Vitest.test (
            "keeps the targets of the visible and of the hidden crumbs",
            fun () ->
                let crumbs: Crumb[] = [|
                    {
                        Label = "Datasets"
                        Target = ProvenanceView.Datasets
                    }
                    {
                        Label = "d"
                        Target = ProvenanceView.Processes "d"
                    }
                    {
                        Label = "p"
                        Target = ProvenanceView.Groups("d", "p")
                    }
                    {
                        Label = "g"
                        Target = ProvenanceView.Detail("d", "p", "g")
                    }
                    {
                        Label = "n"
                        Target = ProvenanceView.Lineage("d", "p", "g", "n")
                    }
                |]

                let layout = visibleCrumbs crumbs true

                Vitest.expect(layout.Hidden |> Array.map (fun c -> c.Target)).toEqual [|
                    ProvenanceView.Processes "d"
                    ProvenanceView.Groups("d", "p")
                |]

                Vitest.expect((Array.last layout.Visible).Target).toEqual (ProvenanceView.Lineage("d", "p", "g", "n"))
        )
)
