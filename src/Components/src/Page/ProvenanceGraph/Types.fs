module Swate.Components.Page.ProvenanceGraph.Types

open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Composite.GraphCanvas.Types

/// The level of the explorer together with the selections that lead to it.
/// Each deeper level keeps the ids of the shallower ones, so a view alone says where the user is.
[<RequireQualifiedAccess>]
type ProvenanceView =
    | Datasets
    | Processes of datasetId: string
    | Groups of datasetId: string * processKey: string
    | Detail of datasetId: string * processKey: string * groupId: string
    | Lineage of datasetId: string * processKey: string * groupId: string * nodeId: string

/// One step of the path from the datasets down to the current view. Choosing it returns to `Target`,
/// and the deeper selections are gone with the deeper levels.
type Crumb = {
    Label: string
    Target: ProvenanceView
}

/// The crumbs the narrow header shows, and the ones it moves into a menu.
type VisibleCrumbs = {
    Visible: Crumb[]
    /// The crumbs between the first one and the last two.
    Hidden: Crumb[]
}

/// The nodes and edges of one view as the generic canvas takes them.
type CanvasGraph = {
    Nodes: CanvasNode[]
    Edges: CanvasEdge[]
}

/// The lineage view scrolls to the selected node, so it carries that id along.
type LineageCanvas = {
    Nodes: CanvasNode[]
    Edges: CanvasEdge[]
    CenterNodeId: string
}

/// A lineage graph cut down to the nodes nearest to the selected one, and the node count before the cut.
type LimitedLineage = { Graph: LineageGraph; Total: int }

/// What a legend item draws in front of its label.
[<RequireQualifiedAccess>]
type LegendSwatch =
    /// The color of a dataset kind.
    | Color of CanvasColor
    /// A dashed outline, for processes of another dataset.
    | Dashed
    /// The color of an annotation category.
    | Category of PillCategory
    /// No swatch, for every other category.
    | Plain

type LegendItem = { Label: string; Swatch: LegendSwatch }

/// The rows of a view that are not finished yet: rows without an output and rows without an input.
/// A row with neither counts in both. Work in progress ARCs have such rows, and the page says so.
type IncompleteRows = {
    MissingOutputs: int
    MissingInputs: int
}

/// One item of an overview row. Selected marks the item that leads to the current view.
type OverviewItem = {
    Id: string
    Label: string
    Selected: bool
}

/// One level above the current view. Items is the part of the level that the row shows. Before and After count
/// the items that the row leaves out on each side.
type OverviewRow = {
    Heading: string
    Items: OverviewItem[]
    Before: int
    After: int
}

module ProvenanceView =

    /// The text of the level, without its ids.
    let kindText (view: ProvenanceView) : string =
        match view with
        | ProvenanceView.Datasets -> "datasets"
        | ProvenanceView.Processes _ -> "processes"
        | ProvenanceView.Groups _ -> "groups"
        | ProvenanceView.Detail _ -> "detail"
        | ProvenanceView.Lineage _ -> "lineage"

    /// A new key tells the canvas that it shows another view, so it resets expanded edges
    /// and the scroll position. The key lists the level and its ids as a JSON array. The level
    /// fixes how many ids follow, so two different views never give the same key.
    let resetKey (view: ProvenanceView) : string =
        let ids =
            match view with
            | ProvenanceView.Datasets -> []
            | ProvenanceView.Processes datasetId -> [ datasetId ]
            | ProvenanceView.Groups(datasetId, processKey) -> [ datasetId; processKey ]
            | ProvenanceView.Detail(datasetId, processKey, groupId) -> [ datasetId; processKey; groupId ]
            | ProvenanceView.Lineage(datasetId, processKey, groupId, nodeId) -> [
                datasetId
                processKey
                groupId
                nodeId
              ]

        jsonStringArray (kindText view :: ids)

    let private tryFind (lookup: System.Collections.Generic.Dictionary<string, 'T>) (key: string) : 'T option =
        match lookup.TryGetValue key with
        | true, value -> Some value
        | _ -> None

    /// The view that a click on a node of `view` opens. A lineage click re-centers the lineage.
    /// An external process of the processes view belongs to another dataset, and the groups view
    /// switches to that dataset. An id the index does not know leads to a view without a result.
    let next (index: ProvenanceIndex) (view: ProvenanceView) (clickedId: string) : ProvenanceView =
        match view with
        | ProvenanceView.Datasets -> ProvenanceView.Processes clickedId
        | ProvenanceView.Processes datasetId ->
            let owner =
                match tryFind index.ProcessByKey clickedId with
                | Some p -> p.DatasetId
                | None -> datasetId

            ProvenanceView.Groups(owner, clickedId)
        | ProvenanceView.Groups(datasetId, processKey) -> ProvenanceView.Detail(datasetId, processKey, clickedId)
        | ProvenanceView.Detail(datasetId, processKey, groupId)
        | ProvenanceView.Lineage(datasetId, processKey, groupId, _) ->
            ProvenanceView.Lineage(datasetId, processKey, groupId, clickedId)

    /// The path from the datasets to `view`. The labels come from the index, and an id
    /// the index does not know is its own label. Only the first crumb has a fixed text.
    let crumbs (index: ProvenanceIndex) (view: ProvenanceView) : Crumb[] =
        let root: Crumb = {
            Label = "Datasets"
            Target = ProvenanceView.Datasets
        }

        let datasetCrumb datasetId : Crumb = {
            Label =
                match tryFind index.DatasetById datasetId with
                | Some dataset -> dataset.Identifier
                | None -> datasetId
            Target = ProvenanceView.Processes datasetId
        }

        let processCrumb datasetId processKey : Crumb = {
            Label =
                match tryFind index.ProcessByKey processKey with
                | Some p -> p.Name
                | None -> processKey
            Target = ProvenanceView.Groups(datasetId, processKey)
        }

        let groupCrumb datasetId processKey groupId : Crumb = {
            Label =
                match tryFind index.GroupById groupId with
                | Some group -> group.Label
                | None -> groupId
            Target = ProvenanceView.Detail(datasetId, processKey, groupId)
        }

        match view with
        | ProvenanceView.Datasets -> [| root |]
        | ProvenanceView.Processes datasetId -> [| root; datasetCrumb datasetId |]
        | ProvenanceView.Groups(datasetId, processKey) -> [|
            root
            datasetCrumb datasetId
            processCrumb datasetId processKey
          |]
        | ProvenanceView.Detail(datasetId, processKey, groupId) -> [|
            root
            datasetCrumb datasetId
            processCrumb datasetId processKey
            groupCrumb datasetId processKey groupId
          |]
        | ProvenanceView.Lineage(datasetId, processKey, groupId, nodeId) -> [|
            root
            datasetCrumb datasetId
            processCrumb datasetId processKey
            groupCrumb datasetId processKey groupId
            { Label = nodeId; Target = view }
          |]
