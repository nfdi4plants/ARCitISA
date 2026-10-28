module internal Swate.Components.Page.ProvenanceGraph.OverviewTests

// The rules of the location overview: the window of items around the selected one, the rows of each view and the
// view that a click on an item opens. The rows come from the example ARC. Headings and labels are read from the
// data, never typed as literals.

open Vitest
open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Page.ProvenanceGraph.Types
open Swate.Components.Page.ProvenanceGraph.Overview

module Sample = Swate.Components.Page.ProvenanceGraphSample

let private dataset = "assays/Extraction/"
let private processKey = "assays/Extraction/::Extraction"
let private group = processKey + "#in0"

/// The rows of a view, composed the way the page composes them.
let private rows (index: ProvenanceIndex) (view: ProvenanceView) : OverviewRow[] =
    baseRows index (queryView view) |> select view

/// The ids of the selected items of a row, including the extra item.
let private selectedIds (row: OverviewRow) : string[] =
    let inLevel =
        row.Items
        |> Array.filter (fun item -> item.Selected)
        |> Array.map (fun item -> item.Id)

    let extra =
        row.Extra
        |> Option.filter (fun item -> item.Selected)
        |> Option.map (fun item -> item.Id)
        |> Option.toArray

    Array.append inLevel extra

let private siblingOf (row: OverviewRow) : OverviewItem =
    row.Items |> Array.find (fun item -> not item.Selected)

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
    "stableWindow",
    fun () ->
        Vitest.test (
            "shows the whole level when it fits",
            fun () ->
                Vitest.expect(stableWindow 5 (Some 2) 7 (Some 3)).toEqual ((0, 5))
                Vitest.expect(stableWindow 7 None 7 None).toEqual ((0, 7))
        )

        Vitest.test (
            "keeps the window while the selected item stays inside it",
            fun () ->
                // The window around item 10 starts at 7. Item 8 is inside it, so a click on item 8 keeps the start.
                Vitest.expect(stableWindow 20 (Some 8) 7 (Some 7)).toEqual ((7, 7))
                Vitest.expect(stableWindow 20 (Some 13) 7 (Some 7)).toEqual ((7, 7))
        )

        Vitest.test (
            "moves the window to the selected item when it leaves the window",
            fun () ->
                Vitest.expect(stableWindow 20 (Some 6) 7 (Some 7)).toEqual (windowAround 20 (Some 6) 7)
                Vitest.expect(stableWindow 20 (Some 14) 7 (Some 7)).toEqual (windowAround 20 (Some 14) 7)
        )

        Vitest.test (
            "clamps a remembered start that is out of range now",
            fun () ->
                // The level shrank from 30 to 12 items, so the last start is 5.
                Vitest.expect(stableWindow 12 (Some 8) 7 (Some 20)).toEqual ((5, 7))
                Vitest.expect(stableWindow 12 None 7 (Some -3)).toEqual ((0, 7))
        )

        Vitest.test (
            "keeps the window when nothing is selected",
            fun () -> Vitest.expect(stableWindow 20 None 7 (Some 9)).toEqual ((9, 7))
        )

        Vitest.test (
            "centers on the selected item when no start is remembered",
            fun () -> Vitest.expect(stableWindow 20 (Some 10) 7 None).toEqual (windowAround 20 (Some 10) 7)
        )
)

Vitest.describe (
    "rowOf",
    fun () ->
        Vitest.test (
            "keeps every item and puts the window around the selected one",
            fun () ->
                let items = [| for i in 0..19 -> $"id{i}", $"label{i}" |]
                let row = rowOf "level" "heading" items "id10" ProvenanceView.Processes

                Vitest.expect(row.Items.Length).toBe 20
                Vitest.expect(row.WindowCount).toBe maxItems
                Vitest.expect(row.WindowStart).toBe 7
                Vitest.expect(selectedIds row).toEqual [| "id10" |]
                Vitest.expect(row.Items.[row.WindowStart].Label).toBe "label7"
                Vitest.expect(row.Items.[3].Target = ProvenanceView.Processes "id3").toBe true
        )

        Vitest.test (
            "leaves the row without a selected item when the id is not among the items",
            fun () ->
                let row =
                    rowOf "level" "heading" [| "a", "A"; "b", "B" |] "c" (fun _ -> ProvenanceView.Datasets)

                Vitest.expect(selectedIds row).toEqual Array.empty<string>
                Vitest.expect(row.Items.Length).toBe 2
                Vitest.expect(row.WindowStart).toBe 0
                Vitest.expect(row.WindowCount).toBe 2
                Vitest.expect(row.Extra.IsNone).toBe true
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
            "opens the processes of a sibling dataset and the detail of a sibling group",
            fun () ->
                let processes = rows Sample.index (ProvenanceView.Processes dataset)
                let sibling = siblingOf processes.[0]

                Vitest.expect(sibling.Target = ProvenanceView.Processes sibling.Id).toBe true

                let detail = rows Sample.index (ProvenanceView.Detail(dataset, processKey, group))
                let siblingGroup = siblingOf detail.[2]

                Vitest.expect(siblingGroup.Target = ProvenanceView.Detail(dataset, processKey, siblingGroup.Id)).toBe
                    true
        )

        Vitest.test (
            "leads the selected items back to the next level and the deepest one to the view itself",
            fun () ->
                let view = ProvenanceView.Detail(dataset, processKey, group)
                let rows = rows Sample.index view

                let selected (row: OverviewRow) =
                    row.Items |> Array.find (fun item -> item.Selected)

                Vitest.expect((selected rows.[0]).Target = ProvenanceView.Processes dataset).toBe true
                Vitest.expect((selected rows.[1]).Target = ProvenanceView.Groups(dataset, processKey)).toBe true
                Vitest.expect((selected rows.[2]).Target = view).toBe true
        )

        Vitest.test (
            "selects a lineage node that the group contains",
            fun () ->
                let rows =
                    rows Sample.index (ProvenanceView.Lineage(dataset, processKey, group, "leaf-1"))

                Vitest.expect(rows.Length).toBe 4
                Vitest.expect(selectedIds rows.[3]).toEqual [| "leaf-1" |]
                Vitest.expect(rows.[3].Extra.IsNone).toBe true
        )

        Vitest.test (
            "appends a lineage node that the group does not contain as the selected extra item",
            fun () ->
                let view =
                    ProvenanceView.Lineage(dataset, processKey, group, "node-of-another-group")

                let rows = rows Sample.index view

                Vitest.expect(rows.Length).toBe 4
                Vitest.expect(selectedIds rows.[2]).toEqual [| group |]
                Vitest.expect(selectedIds rows.[3]).toEqual [| "node-of-another-group" |]
                Vitest.expect(rows.[3].Items |> Array.exists (fun item -> item.Selected)).toBe false

                let extra = rows.[3].Extra.Value
                Vitest.expect(extra.Label).toBe "node-of-another-group"
                Vitest.expect(extra.Target = view).toBe true
                Vitest.expect(rows.[3].Items.Length).toBeGreaterThan 0
        )

        Vitest.test (
            "reuses the rows of one lineage node for another node of the same group",
            fun () ->
                let first =
                    ProvenanceView.Lineage(dataset, processKey, group, "node-of-another-group")

                let second = ProvenanceView.Lineage(dataset, processKey, group, "leaf-1")

                Vitest
                    .expect(ProvenanceView.resetKey (queryView first))
                    .toBe (ProvenanceView.resetKey (queryView second))

                let shared = baseRows Sample.index (queryView first)
                let reused = select second shared

                Vitest.expect(selectedIds reused.[3]).toEqual [| "leaf-1" |]
                Vitest.expect(reused.[3].Extra.IsNone).toBe true
                Vitest.expect(obj.ReferenceEquals(reused.[1], shared.[1])).toBe true
        )
)
