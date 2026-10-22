namespace Swate.Components.Page.ProvenanceGraph

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.JsBindings
open Swate.Components.Primitive.ErrorBoundary
open Swate.Components.Composite.GraphCanvas
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Shared.ProvenanceGraph.Queries
open Swate.Components.Page.ProvenanceGraph.Types
open Swate.Components.Page.ProvenanceGraph.ToCanvas
open Swate.Components.Page.ProvenanceGraph.Header

module private ProvenanceGraphTypes =

    /// What the canvas of a view shows. Cap holds the shown and the total node count of a lineage that was cut.
    /// Incomplete counts the rows of the view that have no output or no input yet.
    type Shown = {
        Nodes: CanvasNode[]
        Edges: CanvasEdge[]
        AriaLabel: string
        CenterNodeId: string option
        Cap: (int * int) option
        Incomplete: IncompleteRows
    }

    /// What a view has to show. NoResult means that the index has no result for the selection. Failed holds the
    /// message of an error that the query or the mapping threw.
    [<RequireQualifiedAccess>]
    type Content =
        | NoResult
        | Graph of Shown
        | Failed of string

    /// The views that have no graph to draw. Only the first three levels can come up empty.
    /// A group always has a member and a lineage always contains its own node.
    [<RequireQualifiedAccess>]
    type EmptyKind =
        | NoDatasets
        | NoProcesses
        | NoGroups
        | NotInArc

module private ProvenanceGraphHelper =

    open ProvenanceGraphTypes

    let crumbMaxLength = 28

    let cutLabel (label: string) : string =
        if label.Length > crumbMaxLength then
            label.Substring(0, crumbMaxLength - 1) + "…"
        else
            label

    let toShown (graph: CanvasGraph) (ariaLabel: string) (incomplete: IncompleteRows) : Shown = {
        Nodes = graph.Nodes
        Edges = graph.Edges
        AriaLabel = ariaLabel
        CenterNodeId = None
        Cap = None
        Incomplete = incomplete
    }

    /// The canvas input of a view. None means that the index has no result for the selection.
    let shownOf (index: ProvenanceIndex) (view: ProvenanceView) : Shown option =
        let incomplete = incompleteRows index view None

        match view with
        | ProvenanceView.Datasets -> Some(toShown (datasetsToCanvas (datasetGraph index)) datasetsAriaLabel incomplete)
        | ProvenanceView.Processes datasetId ->
            processGraph index datasetId
            |> Option.map (fun graph -> toShown (processesToCanvas graph) processesAriaLabel incomplete)
        | ProvenanceView.Groups(_, processKey) ->
            processGroups index processKey
            |> Option.map (fun graph -> toShown (groupsToCanvas graph) (groupsAriaLabel graph) incomplete)
        | ProvenanceView.Detail(_, _, groupId) ->
            processDetail index groupId
            |> Option.map (fun graph -> toShown (detailToCanvas graph) (detailAriaLabel graph) incomplete)
        | ProvenanceView.Lineage(_, processKey, _, nodeId) ->
            lineage index nodeId
            |> Option.map (fun full ->
                let limited = limitLineage full
                let canvas = lineageToCanvas limited.Graph (Some processKey)
                let shownCount = limited.Graph.Nodes.Length

                {
                    Nodes = canvas.Nodes
                    Edges = canvas.Edges
                    AriaLabel = lineageAriaLabel limited.Graph
                    CenterNodeId = Some canvas.CenterNodeId
                    Cap =
                        if shownCount < limited.Total then
                            Some(shownCount, limited.Total)
                        else
                            None
                    Incomplete = incompleteRows index view (Some limited.Graph)
                }
            )

    /// The query and the mapping of a view. An exception becomes a Failed content, so a broken index or a bug in
    /// the mapping shows the message in the page and never reaches React as a thrown error.
    let contentOf (index: ProvenanceIndex) (view: ProvenanceView) : Content =
        try
            match shownOf index view with
            | Some shown -> Content.Graph shown
            | None -> Content.NoResult
        with error ->
            Browser.Dom.console.error ("Could not compute the provenance view", error)
            Content.Failed(Errors.messageOf error)

    let emptyKindOf (view: ProvenanceView) (content: Content) : EmptyKind option =
        match content with
        | Content.NoResult -> Some EmptyKind.NotInArc
        | Content.Failed _ -> None
        | Content.Graph shown when shown.Nodes.Length = 0 ->
            match view with
            | ProvenanceView.Datasets -> Some EmptyKind.NoDatasets
            | ProvenanceView.Processes _ -> Some EmptyKind.NoProcesses
            | ProvenanceView.Groups _ -> Some EmptyKind.NoGroups
            | ProvenanceView.Detail _
            | ProvenanceView.Lineage _ -> None
        | Content.Graph _ -> None

    let emptyKindText (kind: EmptyKind) : string =
        match kind with
        | EmptyKind.NoDatasets -> "no-datasets"
        | EmptyKind.NoProcesses -> "no-processes"
        | EmptyKind.NoGroups -> "no-groups"
        | EmptyKind.NotInArc -> "not-in-arc"

    let emptyMessage (kind: EmptyKind) : string =
        match kind with
        | EmptyKind.NoDatasets -> "This ARC has no datasets."
        | EmptyKind.NoProcesses -> "This dataset has no annotated processes."
        | EmptyKind.NoGroups -> "This process has no inputs or outputs."
        | EmptyKind.NotInArc -> "This selection is not part of the loaded ARC."

open ProvenanceGraphTypes
open ProvenanceGraphHelper

/// Shows the provenance of an ARC level by level: datasets, the processes of a dataset, the input and output
/// groups of a process, the nodes around a group and the lineage of a node. A click on a node opens the next
/// level, and the breadcrumbs lead back. The parent builds the index once for each ARC. A new index object
/// returns the page to the datasets. The page fills its parent, which needs a height.
/// A view that cannot be shown says so with the message of the error, and the breadcrumbs stay usable.
[<Erase; Mangle(false)>]
type ProvenanceGraph =

    [<ReactComponent>]
    static member private Legend(view: ProvenanceView) =
        let items = legendItems view

        Html.ul [
            prop.testId "provenance-legend"
            prop.ariaLabel "Legend"
            prop.className
                "swt:m-0 swt:flex swt:list-none swt:flex-wrap swt:gap-x-3 swt:gap-y-1 swt:p-0 swt:text-xs swt:text-base-content/80"
            prop.children [
                for item in items ->
                    Html.li [
                        prop.key item.Label
                        prop.className "swt:inline-flex swt:items-center swt:gap-1.5"
                        prop.children [
                            Html.i [
                                prop.testId "provenance-legend-swatch"
                                prop.ariaHidden true
                                match item.Swatch with
                                | LegendSwatch.Dashed ->
                                    prop.className
                                        "swt:inline-block swt:h-2.5 swt:w-3.5 swt:rounded-xs swt:border-[1.5px] swt:border-dashed"

                                    prop.style [ style.custom ("borderColor", "var(--prov-other)") ]
                                | LegendSwatch.Color color ->
                                    prop.className "swt:inline-block swt:size-[9px] swt:rounded-full"
                                    prop.style [ style.custom ("backgroundColor", colorVar color) ]
                                | LegendSwatch.Category category ->
                                    prop.className "swt:inline-block swt:size-[9px] swt:rounded-full"
                                    prop.style [ style.custom ("backgroundColor", categoryVar category) ]
                                | LegendSwatch.Plain ->
                                    prop.className "swt:inline-block swt:size-[9px] swt:rounded-full"
                                    prop.style [ style.custom ("backgroundColor", "var(--prov-other)") ]
                            ]
                            Html.text item.Label
                        ]
                    ]
            ]
        ]

    /// The alert of a view that could not be shown. The message comes from the error. The page shows the ARC as it
    /// was when it opened, so the hint sends the user to close the graph and open it again after a fix. After a
    /// navigation the alert takes the focus, because the clicked node or crumb is gone.
    [<ReactComponent>]
    static member private ErrorAlert(message: string, ?focusRef: Element -> unit) =
        Html.div [
            prop.testId "provenance-error"
            prop.role.alert
            prop.className "swt:alert swt:alert-error swt:m-3 swt:flex-none"
            prop.tabIndex -1

            match focusRef with
            | Some focus -> prop.ref focus
            | None -> ()

            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--error-circle-24-regular swt:size-5"
                ]
                Html.div [
                    Html.p [
                        prop.className "swt:m-0"
                        prop.text "This view could not be shown."
                    ]
                    Html.p [
                        prop.testId "provenance-error-message"
                        prop.className "swt:m-0 swt:break-words swt:text-xs"
                        prop.text message
                    ]
                    Html.p [
                        prop.className "swt:m-0 swt:text-xs"
                        prop.text
                            "The graph shows the ARC as it was when this page opened. After you fix the ARC, close and open the graph again."
                    ]
                ]
            ]
        ]

    /// The collapsed crumbs of the narrow header. The list opens under the button, and it closes on Escape,
    /// on a click outside and after a choice. The Dropdown primitive holds the list only while it is open.
    [<ReactComponent>]
    static member private HiddenCrumbs(hidden: (int * Crumb)[], onNavigate: ProvenanceView -> unit) =
        let isOpen, setIsOpen = React.useState false
        let toggleRef = React.useRef<HTMLElement option> None

        let toggle =
            Html.button [
                prop.ref toggleRef
                prop.type'.button
                prop.testId "provenance-crumb-menu"
                prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                prop.ariaLabel (
                    if hidden.Length = 1 then
                        "Show 1 more level"
                    else
                        $"Show {hidden.Length} more levels"
                )
                prop.custom ("aria-expanded", isOpen)
                prop.onClick (fun _ -> setIsOpen (not isOpen))
                prop.text "…"
            ]

        let items =
            React.Fragment [
                for fullIndex, crumb in hidden ->
                    Html.li [
                        prop.key fullIndex
                        prop.children [
                            Html.button [
                                prop.type'.button
                                prop.testId $"provenance-crumb-menu-item-{fullIndex}"
                                prop.onClick (fun _ -> onNavigate crumb.Target)
                                prop.text (cutLabel crumb.Label)
                            ]
                        ]
                    ]
            ]

        Html.div [
            prop.className "swt:inline-flex"
            prop.onKeyDown (fun (event: KeyboardEvent) ->
                if isOpen && event.key = "Escape" then
                    event.stopPropagation ()
                    setIsOpen false
                    toggleRef.current |> Option.iter (fun button -> button.focus ())
            )
            prop.children [
                Swate.Components.Primitive.Dropdown.Dropdown.Main(isOpen, setIsOpen, toggle, items)
            ]
        ]

    /// The breadcrumbs and the invisible copy of them that decides between the full and the narrow layout.
    /// The layout switches when the full crumbs do not fit on one line. A ResizeObserver reports two widths:
    /// the space next to the up button (the box) and the natural width of the full crumbs (the ruler). Both are
    /// independent of the layout on screen, because the header is as wide as the page and the ruler always
    /// holds all crumbs. The up button has the same width in either layout. The answer is therefore the same
    /// in either layout, so the header cannot flip back and forth. The observed elements keep their size in
    /// both layouts, so a layout switch does not fire the observer again.
    [<ReactComponent>]
    static member private Header(crumbs: Crumb[], onNavigate: ProvenanceView -> unit) =
        let boxRef = React.useRef<HTMLElement option> None
        let fullRef = React.useRef<HTMLElement option> None
        let narrow, setNarrow = React.useState false

        ReactHooks.useLayoutEffectWithCleanup (
            (fun () ->
                match boxRef.current, fullRef.current with
                | Some measuredBox, Some full when Dom.ResizeObserver.isAvailable ->
                    let available = ref infinity
                    let natural = ref 0.0
                    let frame = ref 0.0

                    let observer =
                        Dom.ResizeObserver(fun entries ->
                            for entry in entries do
                                if obj.ReferenceEquals(entry.target, measuredBox) then
                                    available.Value <- entry.contentRect.width
                                else
                                    natural.Value <- entry.contentRect.width

                            // The switch waits for the next frame. Changing the layout inside the callback
                            // would resize elements other observers already measured, and the browser reports
                            // that as a ResizeObserver loop.
                            Browser.Dom.window.cancelAnimationFrame frame.Value

                            frame.Value <-
                                Browser.Dom.window.requestAnimationFrame (fun _ ->
                                    setNarrow (isNarrow natural.Value available.Value)
                                )
                        )

                    observer.observe measuredBox
                    observer.observe full

                    box (fun () ->
                        Browser.Dom.window.cancelAnimationFrame frame.Value
                        observer.disconnect ()
                    )
                | _ -> box (fun () -> ())
            ),
            [||]
        )

        let layout = visibleCrumbs crumbs narrow
        let hiddenCount = layout.Hidden.Length
        let lastPosition = layout.Visible.Length - 1

        let fullIndexOf (crumb: Crumb) =
            crumbs |> Array.findIndex (fun other -> obj.ReferenceEquals(other, crumb))

        let crumbItem (position: int) (crumb: Crumb) : ReactElement =
            let fullIndex = fullIndexOf crumb
            let isLast = position = lastPosition
            // In the narrow header the crumb before the current one gives way first, then the first one, and the
            // current one last. Every crumb keeps a small width and shows an ellipsis when it is cut.
            let givesWay = narrow && position > 0 && position = lastPosition - 1
            let shown = cutLabel crumb.Label
            // The CSS can cut the crumbs of the narrow header, so they always carry their full text.
            let title =
                if shown = crumb.Label && not narrow then
                    None
                else
                    Some crumb.Label

            // A crumb after the first one starts with its separator, which takes about 26px of the item.
            // The shrink factors set the order in which the items give way. Their sum stays at 1 or more for as long
            // as an item can shrink, because a sum below 1 makes the browser take back only that part of the
            // overflow, and the crumbs would stick out of the header.
            let itemClass =
                if not narrow then ""
                elif isLast then "swt:min-w-9 swt:shrink-[1]"
                elif givesWay then "swt:min-w-12 swt:shrink-[1000]"
                else "swt:min-w-7 swt:shrink-[10]"

            Html.li [
                prop.key $"crumb-{fullIndex}"
                prop.className itemClass
                prop.children [
                    if isLast then
                        Html.span [
                            prop.testId $"provenance-crumb-{fullIndex}"
                            prop.custom ("aria-current", "page")
                            // daisyUI makes the children of a crumb flex boxes, which show no ellipsis. A block does.
                            prop.className (
                                if narrow then
                                    "swt:block swt:min-w-0 swt:cursor-default swt:truncate swt:font-semibold swt:hover:no-underline"
                                else
                                    "swt:cursor-default swt:font-semibold swt:hover:no-underline"
                            )
                            match title with
                            | Some title -> prop.title title
                            | None -> ()
                            prop.text shown
                        ]
                    else
                        Html.button [
                            prop.type'.button
                            prop.testId $"provenance-crumb-{fullIndex}"
                            prop.className (
                                if narrow then
                                    "swt:link swt:link-hover swt:block swt:min-w-0 swt:truncate"
                                else
                                    "swt:link swt:link-hover"
                            )
                            // Link text needs a contrast of 4.5 to 1, which the primary color alone does not reach.
                            prop.style [ style.custom ("color", accentTextColor) ]
                            match title with
                            | Some title -> prop.title title
                            | None -> ()
                            prop.onClick (fun _ -> onNavigate crumb.Target)
                            prop.text shown
                        ]
                ]
            ]

        // The collapsed crumbs sit in a menu between the first crumb and the last two.
        let menuItem: ReactElement =
            Html.li [
                prop.key "crumb-menu"
                prop.className "swt:shrink-0"
                prop.children [
                    ProvenanceGraph.HiddenCrumbs(
                        [| for crumb in layout.Hidden -> fullIndexOf crumb, crumb |],
                        onNavigate
                    )
                ]
            ]

        // The button goes one level up, like the crumb before the current one. It keeps its width, and its label
        // is cut at the maximum width.
        let upButton (parent: Crumb) : ReactElement =
            Html.button [
                prop.type'.button
                prop.testId "provenance-up"
                prop.className
                    "swt:btn swt:btn-sm swt:btn-outline swt:flex-none swt:max-w-56 swt:gap-1.5 swt:font-normal"
                prop.ariaLabel $"Back to {parent.Label}"
                prop.title $"Back to {parent.Label}"
                prop.onClick (fun _ -> onNavigate parent.Target)
                prop.children [
                    Html.i [
                        prop.ariaHidden true
                        prop.className "swt:iconify swt:fluent--arrow-left-24-regular swt:size-4 swt:flex-none"
                    ]
                    Html.span [
                        prop.className "swt:min-w-0 swt:truncate"
                        prop.text (cutLabel parent.Label)
                    ]
                ]
            ]

        Html.header [
            prop.className
                "swt:relative swt:flex swt:flex-none swt:items-center swt:justify-between swt:gap-x-3.5 swt:border-b swt:border-base-300 swt:bg-base-200 swt:px-3.5 swt:py-2.5"
            prop.children [
                if crumbs.Length > 1 then
                    upButton crumbs.[crumbs.Length - 2]

                Html.div [
                    prop.className "swt:relative swt:flex swt:min-w-0 swt:flex-1 swt:items-center"
                    prop.children [
                        Html.nav [
                            prop.ariaLabel "Breadcrumb"
                            // Without the menu nothing has to stick out of the nav, so what does not fit is cut. The list
                            // of the menu needs the room below the nav.
                            prop.className (
                                if narrow && hiddenCount = 0 then
                                    "swt:breadcrumbs swt:min-w-0 swt:overflow-hidden swt:p-0 swt:text-sm"
                                else
                                    "swt:breadcrumbs swt:min-w-0 swt:overflow-visible swt:p-0 swt:text-sm"
                            )
                            prop.children [
                                Html.ol [
                                    prop.children [
                                        for position, crumb in Array.indexed layout.Visible do
                                            if position = 1 && hiddenCount > 0 then
                                                menuItem

                                            crumbItem position crumb
                                    ]
                                ]
                            ]
                        ]
                        // The ruler is the invisible copy of all crumbs. Its box has the width of the space next to
                        // the up button and no height. The ruler inside has its natural width and draws its text with
                        // CSS content, so assistive technology reads each label once.
                        Html.div [
                            prop.ref boxRef
                            prop.ariaHidden true
                            prop.className
                                "swt:pointer-events-none swt:invisible swt:absolute swt:top-0 swt:right-0 swt:left-0 swt:h-0 swt:overflow-hidden"
                            prop.children [
                                Html.div [
                                    prop.ref fullRef
                                    prop.className
                                        "swt:breadcrumbs swt:w-max swt:max-w-none swt:overflow-visible swt:p-0 swt:text-sm swt:whitespace-nowrap"
                                    prop.children [
                                        Html.ol [
                                            prop.children [
                                                for position, crumb in Array.indexed crumbs ->
                                                    Html.li [
                                                        prop.key position
                                                        prop.children [
                                                            Html.span [
                                                                prop.className (
                                                                    if position = crumbs.Length - 1 then
                                                                        "swt:font-semibold swt:before:content-[attr(data-text)]"
                                                                    else
                                                                        "swt:before:content-[attr(data-text)]"
                                                                )
                                                                prop.custom ("data-text", cutLabel crumb.Label)
                                                            ]
                                                        ]
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    /// `layout` replaces the ELK layout of the canvas. It is a seam for tests that count or delay the layouts.
    [<ReactComponent>]
    static member ProvenanceGraph
        (
            index: ProvenanceIndex,
            ?layout: CanvasNode[] -> CanvasEdge[] -> string[] -> MeasureText -> LayoutOptions -> JS.Promise<GraphLayout>
        ) =
        let view, setView = React.useState ProvenanceView.Datasets
        // After the first navigation, whatever replaces the view takes focus, because the clicked node
        // or crumb is gone.
        let navigated, setNavigated = React.useState false
        // A new index is another ARC, so the selection of the old one must not outlive it. React lets a
        // component reset state while it renders, so nothing of the old selection is drawn.
        // The parent keeps the index identity stable while the ARC stays the same.
        let seenIndex, setSeenIndex = React.useState index
        let sameIndex = LanguagePrimitives.PhysicalEquality seenIndex index

        if not sameIndex then
            setSeenIndex index
            setView ProvenanceView.Datasets
            setNavigated false

        let view = if sameIndex then view else ProvenanceView.Datasets
        let navigated = sameIndex && navigated
        let viewKey = ProvenanceView.resetKey view

        let navigate =
            React.useCallback (
                (fun (target: ProvenanceView) ->
                    setNavigated true
                    setView target
                ),
                [||]
            )

        // A level is only computed for the current view, and the arrays keep their identity until the index or
        // the view changes, because the canvas lays out again for new arrays.
        let content =
            React.useMemo ((fun () -> contentOf index view), [| box index; box viewKey |])

        let crumbs =
            React.useMemo ((fun () -> ProvenanceView.crumbs index view), [| box index; box viewKey |])

        let empty = emptyKindOf view content

        let failed =
            match content with
            | Content.Failed _ -> true
            | _ -> false

        // A ref callback needs a stable identity. React runs a new function again on every commit, and the
        // message would take the focus back each time the page renders.
        let focusRef =
            React.useCallback (
                (fun (element: Element) ->
                    if not (isNull element) then
                        (element :?> HTMLElement).focus ()
                ),
                [||]
            )

        // The alert of a failed view takes the focus after a navigation, like the empty message does.
        let alertFocus = if navigated then Some focusRef else None

        // The children carry keys, so the canvas keeps its place when a note or the hint comes and goes. Without
        // them the position of the canvas shifts, React mounts it again and the zoom is lost.
        let body =
            Html.div [
                prop.className "swt:flex swt:min-h-0 swt:min-w-0 swt:flex-1 swt:flex-col"
                prop.children [
                    match content with
                    | Content.Graph { Cap = Some(shownCount, total) } ->
                        Html.p [
                            prop.key "cap"
                            prop.testId "provenance-lineage-cap"
                            prop.role.status
                            prop.className
                                "swt:m-0 swt:flex-none swt:border-b swt:border-base-300 swt:px-3.5 swt:py-1.5 swt:text-xs swt:text-base-content/80"
                            prop.text $"Showing {shownCount} of {total} nodes."
                        ]
                    | _ -> ()

                    match empty, content with
                    | _, Content.Failed message ->
                        React.KeyedFragment(
                            "error",
                            [
                                ProvenanceGraph.ErrorAlert(message, ?focusRef = alertFocus)
                            ]
                        )
                    | None, Content.Graph shown ->
                        Html.div [
                            prop.key "canvas"
                            prop.className
                                "swt:relative swt:flex swt:min-h-0 swt:min-w-0 swt:flex-1 swt:flex-col swt:overflow-hidden"
                            prop.children [
                                GraphCanvas.GraphCanvas(
                                    shown.Nodes,
                                    shown.Edges,
                                    shown.AriaLabel,
                                    viewKey,
                                    onNodeClick = (fun id -> navigate (ProvenanceView.next index view id)),
                                    ?centerNodeId = shown.CenterNodeId,
                                    focusOnMount = navigated,
                                    ?layout = layout
                                )
                            ]
                        ]
                    | Some kind, _ ->
                        Html.p [
                            prop.key "empty"
                            prop.testId "provenance-empty"
                            prop.custom ("data-empty", emptyKindText kind)
                            prop.className
                                "swt:m-auto swt:px-3.5 swt:py-[18px] swt:text-center swt:text-base-content/80"
                            prop.tabIndex -1
                            if navigated then
                                prop.ref focusRef
                            prop.text (emptyMessage kind)
                        ]
                    | None, _ -> ()

                    match content with
                    | Content.Graph shown when shown.Incomplete.MissingOutputs > 0 || shown.Incomplete.MissingInputs > 0 ->
                        Html.p [
                            prop.key "incomplete"
                            prop.testId "provenance-incomplete"
                            prop.custom ("data-missing-outputs", shown.Incomplete.MissingOutputs)
                            prop.custom ("data-missing-inputs", shown.Incomplete.MissingInputs)
                            prop.role.status
                            prop.className
                                "swt:m-0 swt:flex-none swt:border-t swt:border-base-300 swt:px-3.5 swt:py-1.5 swt:text-xs swt:text-base-content/80"
                            prop.text (incompleteNote shown.Incomplete)
                        ]
                    | _ -> ()

                    match view with
                    | ProvenanceView.Detail _ when empty.IsNone && not failed ->
                        Html.p [
                            prop.key "hint"
                            prop.testId "provenance-hint"
                            prop.className
                                "swt:m-0 swt:flex-none swt:border-t swt:border-base-300 swt:px-3.5 swt:py-1.5 swt:text-xs swt:text-base-content/80"
                            prop.text "Select a node to see its lineage."
                        ]
                    | _ -> ()
                ]
            ]

        Html.section [
            prop.testId "provenance-graph"
            prop.ariaLabel "Provenance graph"
            prop.className
                "swt:flex swt:size-full swt:min-h-0 swt:flex-col swt:overflow-hidden swt:bg-base-100 swt:text-base-content"
            prop.style [ for name, value in palette -> style.custom (name, value) ]
            prop.children [
                ProvenanceGraph.Header(crumbs, navigate)
                // An error while the body renders shows the alert in its place and leaves the header alone. A new
                // view clears the error.
                ErrorBoundary.ErrorBoundary(
                    body,
                    (fun error -> ProvenanceGraph.ErrorAlert(error.Message, ?focusRef = alertFocus)),
                    resetKey = viewKey
                )
                Html.div [
                    prop.className "swt:flex-none swt:border-t swt:border-base-300 swt:bg-base-200 swt:px-3.5 swt:py-2"
                    prop.children [ ProvenanceGraph.Legend view ]
                ]
            ]
        ]
