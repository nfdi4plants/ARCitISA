/// The rows of the location overview: one row per level above the current view, with the item that leads to the view.
module Swate.Components.Page.ProvenanceGraph.Overview

open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Shared.ProvenanceGraph.Queries
open Swate.Components.Page.ProvenanceGraph.Types

/// A collapsed row shows at most this many items.
let maxItems = 7

/// The part of a level that a row shows, as the start index and the item count. A level that fits is shown in
/// full. A longer level shows `maxCount` items around the selected one, as centered as the ends of the level
/// allow, or the first items when nothing is selected.
let windowAround (total: int) (selected: int option) (maxCount: int) : int * int =
    if total <= maxCount then
        0, total
    else
        match selected with
        | Some index -> max 0 (min (total - maxCount) (index - maxCount / 2)), maxCount
        | None -> 0, maxCount

/// The window of a level that was drawn before, with the start that it had. The window stays where it was as long
/// as the selected item lies inside it, so a click on a visible item does not move the items. The remembered start
/// is clamped to the level first, because the level may have shrunk. A selected item outside the window, or a level
/// without a remembered start, gets the window around the selected item.
let stableWindow (total: int) (selected: int option) (maxCount: int) (previousStart: int option) : int * int =
    if total <= maxCount then
        0, total
    else
        match previousStart with
        | Some remembered ->
            let start = max 0 (min (total - maxCount) remembered)

            match selected with
            | Some index when index < start || index >= start + maxCount -> windowAround total selected maxCount
            | _ -> start, maxCount
        | None -> windowAround total selected maxCount

/// A row for the items (id, label) of one level. `key` identifies the level. `targetOf` gives the view that a click
/// on an item opens. An id that is not among the items leaves the row without a selected item.
let rowOf
    (key: string)
    (heading: string)
    (items: (string * string)[])
    (selectedId: string)
    (targetOf: string -> ProvenanceView)
    : OverviewRow =
    let selected = items |> Array.tryFindIndex (fun (id, _) -> id = selectedId)
    let start, count = windowAround items.Length selected maxItems

    {
        Key = key
        Heading = heading
        Items = [|
            for id, label in items ->
                {
                    Id = id
                    Label = label
                    Selected = (id = selectedId)
                    Target = targetOf id
                }
        |]
        WindowStart = start
        WindowCount = count
        Extra = None
    }

/// The ids that lead to the view, from the dataset down. Each id selects one item of the level at the same position.
let private selectionOf (view: ProvenanceView) : string[] =
    match view with
    | ProvenanceView.Datasets -> [||]
    | ProvenanceView.Processes datasetId -> [| datasetId |]
    | ProvenanceView.Groups(datasetId, processKey) -> [| datasetId; processKey |]
    | ProvenanceView.Detail(datasetId, processKey, groupId) -> [| datasetId; processKey; groupId |]
    | ProvenanceView.Lineage(datasetId, processKey, groupId, nodeId) -> [| datasetId; processKey; groupId; nodeId |]

/// The items (id, label) of a level. The labels are the titles of the nodes in the graph of that level. The
/// level of the datasets has no parent, and the others take the id of the item that the level above selects.
/// The index has no result for an id it does not know. The datasets come from the index itself, because the
/// graph of the datasets also scans the rows of the whole ARC for its edges.
let private itemsAt (index: ProvenanceIndex) (selection: string[]) (level: int) : (string * string)[] option =
    match level with
    | 0 ->
        Some [|
            for dataset in index.Datasets -> dataset.Id, dataset.Identifier
        |]
    | 1 ->
        processGraph index selection.[0]
        |> Option.map (fun graph -> [| for node in graph.Nodes -> node.Key, node.Label |])
    | 2 ->
        processGroups index selection.[1]
        |> Option.map (fun graph -> [| for node in graph.Nodes -> node.Id, node.Label |])
    | _ ->
        processDetail index selection.[2]
        |> Option.map (fun graph -> [| for node in graph.Nodes -> node.Id, node.Id |])

/// The key of a level names the kind of its items and the item that owns the level. Two views that show the same
/// level give the same key.
let private keyAt (selection: string[]) (level: int) : string =
    match level with
    | 0 -> "datasets"
    | 1 -> $"processes:{selection.[0]}"
    | 2 -> $"groups:{selection.[1]}"
    | _ -> $"nodes:{selection.[2]}"

/// The heading of a row names the item that owns the level. That item is the crumb at the same position.
let private headingAt (crumbs: Crumb[]) (level: int) : string =
    match level with
    | 0 -> "Datasets"
    | 1 -> $"Processes of {crumbs.[1].Label}"
    | 2 -> $"Groups of {crumbs.[2].Label}"
    | _ -> $"Nodes of {crumbs.[3].Label}"

/// The view of a level, in which a click on one of its items opens the next level.
let private levelView (crumbs: Crumb[]) (level: int) : ProvenanceView =
    if level = 0 then
        ProvenanceView.Datasets
    else
        crumbs.[level].Target

/// The view that decides which queries the rows need. A lineage that centers on another node stays in the same
/// group, so it needs the same queries. The node id is empty here, and the nodes row has no selection yet.
let queryView (view: ProvenanceView) : ProvenanceView =
    match view with
    | ProvenanceView.Lineage(datasetId, processKey, groupId, _) ->
        ProvenanceView.Lineage(datasetId, processKey, groupId, "")
    | other -> other

/// One row per level above the view, from the datasets down. A level without a result in the index ends the list.
/// The queries behind the rows are the expensive part. A page that keeps the result for the same `queryView`
/// leaves them out when a lineage centers on another node, and `select` makes the rows of that node.
let baseRows (index: ProvenanceIndex) (view: ProvenanceView) : OverviewRow[] =
    let selection = selectionOf view
    let crumbs = ProvenanceView.crumbs index view

    let rec collect (level: int) (found: OverviewRow list) : OverviewRow list =
        if level >= selection.Length then
            found
        else
            match itemsAt index selection level with
            | Some items ->
                let targetOf (id: string) =
                    ProvenanceView.next index (levelView crumbs level) id

                collect
                    (level + 1)
                    (rowOf (keyAt selection level) (headingAt crumbs level) items selection.[level] targetOf
                     :: found)
            | None -> found

    collect 0 [] |> List.rev |> Array.ofList

/// Marks the node of a lineage in the nodes row, which `baseRows` left without a selection. The lineage
/// centers on any node of its graph, so the node is often not among the nodes of the group. Then the row gets the
/// node as an extra item. The other views and rows stay as they are.
let select (view: ProvenanceView) (rows: OverviewRow[]) : OverviewRow[] =
    match view with
    | ProvenanceView.Lineage(_, _, _, nodeId) when rows.Length = 4 ->
        let row = rows.[3]
        let selected = row.Items |> Array.tryFindIndex (fun item -> item.Id = nodeId)
        let start, count = windowAround row.Items.Length selected maxItems

        let marked = {
            row with
                Items =
                    row.Items
                    |> Array.map (fun item -> {
                        item with
                            Selected = (item.Id = nodeId)
                    })
                WindowStart = start
                WindowCount = count
                Extra =
                    if selected.IsNone then
                        Some {
                            Id = nodeId
                            Label = nodeId
                            Selected = true
                            Target = view
                        }
                    else
                        None
        }

        Array.append (Array.sub rows 0 3) [| marked |]
    | _ -> rows
