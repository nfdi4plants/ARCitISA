module internal Swate.Components.Page.ProvenanceGraph.OverviewTests

// The rules of the location overview: the window of items around the selected one and the rows of each view.
// The rows come from the example ARC. Headings and labels are read from the data, never typed as literals.

open Vitest
open Swate.Components.Page.ProvenanceGraph.Types
open Swate.Components.Page.ProvenanceGraph.Overview

module Sample = Swate.Components.Page.ProvenanceGraphSample

let private dataset = "assays/Extraction/"
let private processKey = "assays/Extraction/::Extraction"
let private group = processKey + "#in0"

let private selectedIds (row: OverviewRow) : string[] =
    row.Items
    |> Array.filter (fun item -> item.Selected)
    |> Array.map (fun item -> item.Id)

Vitest.describe (
    "windowAround",
    fun () ->
        Vitest.test (
            "shows the whole level when it fits",
            fun () ->
                Vitest.expect(windowAround 5 (Some 2) 7).toEqual ((0, 5))
                Vitest.expect(windowAround 7 (Some 6) 7).toEqual ((0, 7))
        )

        Vitest.test (
            "starts at the first item when the selected item is near the start",
            fun () -> Vitest.expect(windowAround 20 (Some 1) 7).toEqual ((0, 7))
        )

        Vitest.test (
            "centers the selected item in the middle of a long level",
            fun () -> Vitest.expect(windowAround 20 (Some 10) 7).toEqual ((7, 7))
        )

        Vitest.test (
            "ends at the last item when the selected item is near the end",
            fun () -> Vitest.expect(windowAround 20 (Some 19) 7).toEqual ((13, 7))
        )

        Vitest.test (
            "starts at the first item when nothing is selected",
            fun () -> Vitest.expect(windowAround 20 None 7).toEqual ((0, 7))
        )
)

Vitest.describe (
    "rowOf",
    fun () ->
        Vitest.test (
            "keeps the items around the selected one and counts the items on each side",
            fun () ->
                let items = [| for i in 0..19 -> $"id{i}", $"label{i}" |]
                let row = rowOf "heading" items "id10"

                Vitest.expect(row.Items.Length).toBe maxItems
                Vitest.expect(row.Before).toBe 7
                Vitest.expect(row.After).toBe 6
                Vitest.expect(selectedIds row).toEqual [| "id10" |]
                Vitest.expect(row.Items.[0].Label).toBe "label7"
        )

        Vitest.test (
            "leaves the row without a selected item when the id is not among the items",
            fun () ->
                let row = rowOf "heading" [| "a", "A"; "b", "B" |] "c"

                Vitest.expect(selectedIds row).toEqual Array.empty<string>
                Vitest.expect(row.Items.Length).toBe 2
                Vitest.expect(row.Before).toBe 0
                Vitest.expect(row.After).toBe 0
        )
)

Vitest.describe (
    "rows",
    fun () ->
        Vitest.test (
            "has no rows at the datasets level",
            fun () -> Vitest.expect((rows Sample.index ProvenanceView.Datasets).Length).toBe 0
        )

        Vitest.test (
            "has one row with the dataset selected at the processes level",
            fun () ->
                let rows = rows Sample.index (ProvenanceView.Processes dataset)

                Vitest.expect(rows.Length).toBe 1
                Vitest.expect(selectedIds rows.[0]).toEqual [| dataset |]
        )

        Vitest.test (
            "has a row per level above the detail with the ids of the view selected",
            fun () ->
                let view = ProvenanceView.Detail(dataset, processKey, group)
                let rows = rows Sample.index view
                let crumbs = ProvenanceView.crumbs Sample.index view

                Vitest.expect(rows.Length).toBe 3
                Vitest.expect(selectedIds rows.[0]).toEqual [| dataset |]
                Vitest.expect(selectedIds rows.[1]).toEqual [| processKey |]
                Vitest.expect(selectedIds rows.[2]).toEqual [| group |]
                // The heading of a row names the crumb that owns the level.
                Vitest.expect(rows.[1].Heading).toContain crumbs.[1].Label
                Vitest.expect(rows.[2].Heading).toContain crumbs.[2].Label
        )

        Vitest.test (
            "leaves the last row without a selected item for a node that the group does not contain",
            fun () ->
                let rows =
                    rows Sample.index (ProvenanceView.Lineage(dataset, processKey, group, "node-of-another-group"))

                Vitest.expect(rows.Length).toBe 4
                Vitest.expect(selectedIds rows.[2]).toEqual [| group |]
                Vitest.expect(selectedIds rows.[3]).toEqual Array.empty<string>
                Vitest.expect(rows.[3].Items.Length).toBeGreaterThan 0
        )
)
