/// The rows of the location overview: one row per level above the current view, with the item that leads to the view.
module Swate.Components.Page.ProvenanceGraph.Overview

open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Shared.ProvenanceGraph.Queries
open Swate.Components.Page.ProvenanceGraph.Types

/// A row shows at most this many items.
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

/// A row for the items (id, label) of one level. An id that is not among the items leaves the row without a
/// selected item.
let rowOf (heading: string) (items: (string * string)[]) (selectedId: string) : OverviewRow =
    let selected = items |> Array.tryFindIndex (fun (id, _) -> id = selectedId)
    let start, count = windowAround items.Length selected maxItems

    {
        Heading = heading
        Items = [|
            for id, label in Array.sub items start count ->
                {
                    Id = id
                    Label = label
                    Selected = (id = selectedId)
                }
        |]
        Before = start
        After = items.Length - start - count
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
/// The index has no result for an id it does not know.
let private itemsAt (index: ProvenanceIndex) (selection: string[]) (level: int) : (string * string)[] option =
    match level with
    | 0 ->
        Some [|
            for node in (datasetGraph index).Nodes -> node.Id, node.Label
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

/// The heading of a row names the item that owns the level. That item is the crumb at the same position.
let private headingAt (crumbs: Crumb[]) (level: int) : string =
    match level with
    | 0 -> "Datasets"
    | 1 -> $"Processes of {crumbs.[1].Label}"
    | 2 -> $"Groups of {crumbs.[2].Label}"
    | _ -> $"Nodes of {crumbs.[3].Label}"

/// One row per level above the view, from the datasets down. A level without a result in the index ends the list.
let rows (index: ProvenanceIndex) (view: ProvenanceView) : OverviewRow[] =
    let selection = selectionOf view
    let crumbs = ProvenanceView.crumbs index view

    let rec collect (level: int) (found: OverviewRow list) : OverviewRow list =
        if level >= selection.Length then
            found
        else
            match itemsAt index selection level with
            | Some items -> collect (level + 1) (rowOf (headingAt crumbs level) items selection.[level] :: found)
            | None -> found

    collect 0 [] |> List.rev |> Array.ofList
