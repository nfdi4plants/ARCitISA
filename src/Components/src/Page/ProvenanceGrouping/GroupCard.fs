namespace Arcitisa.Components.Page.ProvenanceGrouping

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

/// Maps loaded endpoint kinds (Source, Sample, Data, ...) to a display label and icon.
/// The kind id carries an adapter prefix (`arc-isa:endpoint:sample`, `fixture:endpoint:data`, ...),
/// so we classify on the role suffix to stay source-agnostic.
module private EntityType =

    type Descriptor = { Label: string; Icon: string }

    let descriptor (kind: ProvenanceKind) : Descriptor =
        let id = kind.Id.ToLowerInvariant()
        let contains (token: string) = id.Contains token

        if contains "endpoint:source" then
            {
                Label = "Source"
                Icon = "swt:fluent--branch-fork-20-regular"
            }
        elif contains "endpoint:sample" then
            {
                Label = "Sample"
                Icon = "swt:fluent--beaker-20-regular"
            }
        elif contains "endpoint:data" then
            {
                Label = "File"
                Icon = "swt:fluent--document-20-regular"
            }
        elif contains "endpoint:material" then
            {
                Label = "Material"
                Icon = "swt:fluent--cube-20-regular"
            }
        elif contains "endpoint:free-text" then
            {
                Label = ProvenanceKind.displayName kind
                Icon = "swt:fluent--text-field-20-regular"
            }
        else
            {
                Label = ProvenanceKind.displayName kind
                Icon = "swt:fluent--tag-20-regular"
            }

    /// Small type line shown above an entity name.
    let line (descriptor: Descriptor) =
        Html.span [
            prop.className
                "swt:inline-flex swt:items-center swt:gap-1 swt:text-xs swt:font-medium swt:uppercase swt:tracking-wide swt:text-base-content/60"
            prop.children [
                Html.i [
                    prop.className $"swt:iconify {descriptor.Icon} swt:size-3 swt:shrink-0"
                    prop.ariaHidden true
                ]
                Html.span [ prop.text descriptor.Label ]
            ]
        ]

/// Derives display text and property chips for one provenance group card.
/// Public so the detail panels can reuse the same title derivation.
module GroupCardData =

    /// Loaded endpoint kind for one member, resolved from the side-specific set map.
    let endpointKind side (model: ProvenanceModel) (setId: ProvenanceSetId) =
        let sets =
            match side with
            | ProvenanceSide.Input -> model.InputSets
            | ProvenanceSide.Output -> model.OutputSets

        sets.TryFind setId |> Option.map (fun set -> set.Header.Kind)

    let memberValues (member': DisplayMember) (model: ProvenanceModel) =
        member'.PropertyValueIds
        |> List.distinct
        |> List.choose (fun id -> model.PropertyValues.TryFind id)

    /// One organizer tab per grouping value, ordered by category then value text.
    let tabs (group: DisplayGroup) =
        group.GroupingValues
        |> List.sortBy (fun value ->
            $"{value.Key.Header.Kind.Id}:{value.Key.Header.Category.Name}",
            Formatting.formatValue value.Value value.Unit
        )

    let title (group: DisplayGroup) =
        match tabs group with
        | [] ->
            group.Members
            |> List.tryHead
            |> Option.map (fun member' -> member'.Name)
            |> Option.defaultValue group.Id
        | tabs ->
            tabs
            |> List.map (fun value ->
                $"{value.Key.Header.Category.Name}: {Formatting.formatValue value.Value value.Unit}"
            )
            |> String.concat ", "

/// Thin ResizeObserver binding used to re-check whether a tab's header still fits.
module private TabObserver =

    [<Emit("new ResizeObserver(() => $0())")>]
    let create (callback: unit -> unit) : obj = jsNative

    [<Emit("$0.observe($1)")>]
    let observe (observer: obj) (target: Browser.Types.HTMLElement) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let disconnect (observer: obj) : unit = jsNative

module private TabText =

    let fullLabel category valueText = $"{category}: {valueText}"

    let splitInitial (text: string) =
        let trimmed = text.Trim()

        if trimmed.Length = 0 then "", ""
        elif trimmed.Length = 1 then trimmed, ""
        else trimmed.Substring(0, 1), trimmed.Substring(1)

module private SelectionSurface =

    [<Emit("$0.closest($1)")>]
    let private closest (_element: Browser.Types.Element) (_selector: string) : Browser.Types.Element = jsNative

    /// True when the click hit the card body itself rather than one of the
    /// interactive controls (buttons, checkboxes, handles) hosted on it.
    let shouldActivate (event: Browser.Types.MouseEvent) =
        let targetObj: obj = box event.target

        if isNull targetObj then
            false
        else
            let target = unbox<Browser.Types.Element> targetObj
            isNull (closest target "button,[role='button'],a,input,select,textarea")

type private OrganizerTabMode =
    | Responsive
    | Focused
    | Collapsed

[<Erase; Mangle(false)>]
type GroupCard =

    /// One colored organizer tab showing "Category: Value". The category header is
    /// all-or-nothing: as soon as the full text stops fitting the header is dropped
    /// entirely and the value truncates on its own.
    [<ReactComponent>]
    static member private OrganizerTab
        (
            category: string,
            valueText: string,
            valueIdentity: string,
            paletteClasses: string,
            isHighlighted: bool,
            setHighlighted: bool -> unit,
            mode: OrganizerTabMode,
            onToggleFocus: unit -> unit,
            ?testId: string,
            ?key: string
        ) =
        let tabRef = React.useRef<Browser.Types.HTMLElement option> None
        let fullLabelRef = React.useRef<Browser.Types.HTMLElement option> None
        let showHeader, setShowHeader = React.useState true
        let label = TabText.fullLabel category valueText
        let collapsedInitial, collapsedRemainder = TabText.splitInitial label
        let isFocused = mode = Focused
        let isCollapsed = mode = Collapsed

        let measure () =
            match fullLabelRef.current with
            | Some fullLabel -> setShowHeader (fullLabel.scrollWidth <= fullLabel.clientWidth + 1.0)
            | _ -> ()

        React.useEffectOnce (fun () ->
            measure ()
            let observer = TabObserver.create measure

            match tabRef.current with
            | Some element -> TabObserver.observe observer element
            | None -> ()

            FsReact.createDisposable (fun () -> TabObserver.disconnect observer)
        )

        Html.span [
            prop.ref (fun element -> tabRef.current <- (if isNull element then None else Some(unbox element)))
            prop.role.button
            prop.tabIndex 0
            prop.title label
            prop.ariaLabel label
            prop.custom ("aria-pressed", isFocused)
            prop.custom ("data-hovered", isHighlighted)
            // Lets the drop feedback find and flash the tab a dropped value created.
            prop.custom ("data-provenance-grouping-value", valueIdentity)
            match testId with
            | Some testId -> prop.testId testId
            | None -> ()
            prop.onClick (fun _ -> onToggleFocus ())
            prop.onKeyDown (fun (event: Browser.Types.KeyboardEvent) ->
                if event.key = "Enter" || event.key = " " then
                    event.preventDefault ()
                    onToggleFocus ()
            )
            prop.onMouseEnter (fun _ -> setHighlighted true)
            prop.onMouseLeave (fun _ -> setHighlighted false)
            prop.onFocus (fun _ -> setHighlighted true)
            prop.onBlur (fun _ -> setHighlighted false)
            prop.className [
                "swt:relative swt:flex swt:min-w-0 swt:cursor-default swt:overflow-hidden swt:whitespace-nowrap swt:rounded-t-md swt:text-xs swt:outline-none swt:transition-all"
                paletteClasses
                if isFocused then
                    "swt:flex-[999_1_0%]"
                elif isCollapsed then
                    "swt:min-w-7 swt:flex-1 swt:basis-0"
                if isHighlighted then
                    "swt:opacity-100 swt:shadow-md"
                else
                    "swt:opacity-75"
            ]
            prop.children [
                // Invisible in-flow copy: gives the tab its natural full width.
                Html.span [
                    prop.ariaHidden true
                    prop.className "swt:invisible swt:px-3 swt:py-1"
                    prop.text label
                ]
                // Measurement overlay: checks whether the full untruncated label
                // fits in the actual visible tab width.
                Html.span [
                    prop.ref (fun element ->
                        fullLabelRef.current <- (if isNull element then None else Some(unbox element))
                    )
                    prop.ariaHidden true
                    prop.className
                        "swt:pointer-events-none swt:absolute swt:inset-0 swt:invisible swt:flex swt:items-baseline swt:overflow-visible swt:px-3 swt:py-1"
                    prop.children [
                        Html.span [
                            prop.className "swt:mr-1 swt:shrink-0"
                            prop.text $"{category}:"
                        ]
                        Html.span [
                            prop.className "swt:shrink-0 swt:font-medium"
                            prop.text valueText
                        ]
                    ]
                ]
                // Visible overlay: drops the header entirely once the tab shrinks.
                Html.span [
                    prop.className [
                        "swt:absolute swt:inset-0 swt:flex swt:items-baseline swt:py-1"
                        if isCollapsed then "swt:px-2" else "swt:px-3"
                    ]
                    prop.children [
                        if isCollapsed then
                            Html.span [
                                prop.className "swt:shrink-0 swt:font-medium"
                                prop.text collapsedInitial
                            ]

                            if collapsedRemainder.Length > 0 then
                                Html.span [
                                    prop.className "swt:min-w-0 swt:truncate swt:font-medium"
                                    prop.text collapsedRemainder
                                ]
                        else
                            if showHeader then
                                Html.span [
                                    prop.className "swt:mr-1 swt:shrink-0"
                                    prop.text $"{category}:"
                                ]

                            Html.span [
                                prop.className [
                                    "swt:font-medium"
                                    if showHeader then
                                        "swt:shrink-0"
                                    else
                                        "swt:min-w-0 swt:truncate"
                                ]
                                prop.text valueText
                            ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member Main
        (
            side: ProvenanceSide,
            group: DisplayGroup,
            model: ProvenanceModel,
            selected: bool,
            expanded: bool,
            onSelect: unit -> unit,
            onExpand: unit -> unit,
            isValueChipDragging: bool,
            ?connectionCount: int,
            ?sourceInfoForValue: ProvenancePropertyValue -> PropertyValueSourceInfo option,
            ?debug: bool,
            ?key: string
        ) =
        let hoveredMemberId, setHoveredMemberId =
            React.useState<ProvenanceSetId option> None

        let hoveredTabIndex, setHoveredTabIndex = React.useState<int option> None
        let focusedTabIndex, setFocusedTabIndex = React.useStateWithUpdater<int option> None
        let articleRef = React.useElementRef ()
        let density = React.useContext Density.context
        let hoverStore = React.useContext HoverHighlight.context
        let connectionInteraction = React.useContext ConnectionDragHints.context

        // Hovering a card lights up its connectors and the connected opposite cards.
        // Suppressed while connecting so the highlight cannot fight drop feedback.
        let publishHover () =
            if connectionInteraction.SourceSide.IsNone && not isValueChipDragging then
                HoverHighlight.set { Side = side; GroupId = group.Id } hoverStore

        let clearHover () = HoverHighlight.clear hoverStore

        React.useEffectOnce (fun () -> FsReact.createDisposable (fun () -> HoverHighlight.clear hoverStore))

        let droppable =
            DndKit.useDroppable (
                {|
                    id = DragDrop.groupDropId side group.Id
                |}
            )

        let title = GroupCardData.title group
        let tabs = GroupCardData.tabs group

        React.useListener.onClickAway (articleRef, fun _ -> setFocusedTabIndex (fun _ -> None))

        let toggleFocusedTab index =
            setFocusedTabIndex (fun current ->
                match current with
                | Some focused when focused = index -> None
                | _ -> Some index
            )

        let setArticleRef element =
            articleRef.current <- (if isNull element then None else Some(unbox element))
            droppable.setNodeRef element

        // Two anchors at opposite card edges: the group-facing edge carries the draggable
        // group connection handle, the property-facing edge is measurement-only and is
        // where property/value connectors from the same-side rail attach.
        let groupHandleEdge, propertyAnchorEdge =
            match side with
            | ProvenanceSide.Input ->
                "swt:absolute swt:top-1/2 swt:right-0 swt:translate-x-1/2 swt:-translate-y-1/2 swt:z-10",
                "swt:top-1/2 swt:left-0 swt:-translate-x-1/2 swt:-translate-y-1/2"
            | ProvenanceSide.Output ->
                "swt:absolute swt:top-1/2 swt:left-0 swt:-translate-x-1/2 swt:-translate-y-1/2 swt:z-10",
                "swt:top-1/2 swt:right-0 swt:translate-x-1/2 swt:-translate-y-1/2"

        let groupHandle: ConnectionHandleRef = {
            Kind = ConnectionHandleKind.GroupCard
            Side = side
            Id = group.Id
            ParentGroupId = None
        }

        let propertyAnchor: ConnectionHandleRef = {
            Kind = ConnectionHandleKind.GroupPropertyAnchor
            Side = side
            Id = group.Id
            ParentGroupId = None
        }

        let memberDetailsPosition =
            match side with
            | ProvenanceSide.Input -> "swt:left-full swt:ml-2"
            | ProvenanceSide.Output -> "swt:right-full swt:mr-2"

        let memberPropertyAnchorEdge =
            match side with
            | ProvenanceSide.Input -> "swt:top-1/2 swt:left-0 swt:-translate-x-1/2 swt:-translate-y-1/2"
            | ProvenanceSide.Output -> "swt:top-1/2 swt:right-0 swt:translate-x-1/2 swt:-translate-y-1/2"

        // The card body is the expand surface; selection lives on an explicit
        // checkbox so the most common click (open the group) is the default one.
        let handleExpandClick (event: Browser.Types.MouseEvent) =
            if SelectionSurface.shouldActivate event then
                onExpand ()

        let selectionCheckbox =
            Html.input [
                prop.type'.checkbox
                prop.className "swt:checkbox swt:checkbox-xs swt:checkbox-primary swt:shrink-0"
                prop.isChecked selected
                prop.onChange (fun (_: bool) -> onSelect ())
                prop.ariaLabel (
                    if selected then
                        $"Deselect group {title}"
                    else
                        $"Select group {title}"
                )
                prop.title "Select this group: dropped values and new layers apply to all selected groups"
                if defaultArg debug false then
                    prop.testId $"provenance-group-select-{side}-{group.Id}"
            ]

        Html.article [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.ref setArticleRef
            prop.custom ("data-provenance-group-node", DragDrop.groupNodeId side group.Id)
            prop.custom ("data-provenance-group-drop-id", DragDrop.groupDropId side group.Id)
            // Member count as an attribute so hosts (e.g. the tutorial) can
            // single out cards whose connections resolve without a member
            // pairing prompt (1x1 connects publish directly).
            prop.custom ("data-provenance-card-members", string group.Members.Length)
            prop.onMouseEnter (fun _ -> publishHover ())
            prop.onMouseLeave (fun _ -> clearHover ())
            prop.className [
                // Cards size to their content (the column aligns them toward their rail),
                // so the gap between the two card columns grows for group connectors. The
                // edge handles are positioned on the card border and move with its width.
                "swt:relative swt:flex swt:w-fit swt:max-w-full swt:flex-col swt:rounded-box swt:border swt:bg-base-100 swt:shadow-sm"
                "swt:transition-shadow swt:duration-150 hover:swt:shadow-md"
                // Cards connected to the hovered opposite card are marked imperatively
                // through this data attribute; see the hover-highlight store.
                "data-[provenance-related=true]:swt:ring-2 data-[provenance-related=true]:swt:ring-primary/35"
                match density with
                | Density.EditorDensity.Compact -> "swt:gap-1 swt:p-1.5"
                | _ -> "swt:gap-1.5 swt:p-2.5"
                if selected then
                    "swt:border-primary swt:bg-primary/5"
                else
                    "swt:border-base-300"
                if droppable.isOver && isValueChipDragging then
                    "swt:ring-2 swt:ring-primary"
                // While a value chip is in flight every card is a legal target, so
                // they all pick up a faint ring instead of staying inert until hover.
                elif isValueChipDragging then
                    "swt:ring-1 swt:ring-primary/25"
            ]
            if defaultArg debug false then
                prop.testId $"provenance-group-{side}-{group.Id}"
            prop.children [
                Controls.ConnectionAnchor(propertyAnchor, propertyAnchorEdge, ?debug = debug)
                if not expanded then
                    Controls.ConnectionHandle(
                        groupHandle,
                        label = "Connect group",
                        className = groupHandleEdge,
                        ?debug = debug
                    )
                // Stacked layouts hide the connector overlay; this badge keeps
                // the connection information visible on the card itself.
                let connectionBadge =
                    match connectionCount with
                    | Some count when count > 0 ->
                        Html.span [
                            prop.className "swt:badge swt:badge-primary swt:badge-sm swt:shrink-0"
                            prop.title $"{count} connections"
                            prop.ariaLabel $"{count} connections"
                            prop.text $"⇄ {count}"
                        ]
                    | _ -> Html.none

                // Every card is drawn as a file organizer: one tab per grouping value
                // sits on top of a folder body that holds the members' type symbols.
                // A card without grouping values is always a single loaded endpoint;
                // it shares the same silhouette and gets one neutral name tab instead.
                // Hovering or focusing a grouping-value tab highlights that value.
                let symbolIcon (descriptor: EntityType.Descriptor) =
                    Html.i [
                        prop.className $"swt:iconify {descriptor.Icon} swt:size-4 swt:shrink-0"
                    ]

                let countLabel count =
                    Html.span [
                        prop.className "swt:text-xs swt:font-semibold"
                        prop.text (string count)
                    ]

                // When few enough to fit, every member contributes one symbol
                // side by side; otherwise the preview collapses to the dominant type
                // symbol with a count.
                let memberDescriptors =
                    group.Members
                    |> List.choose (fun member' ->
                        GroupCardData.endpointKind side model member'.SetId
                        |> Option.map EntityType.descriptor
                    )

                let maxInlineSymbols = 4

                // The tab bar escapes the card padding so the tabs sit flush on the
                // card's top edge, like register tabs on an archive folder.
                let tabBarMargins =
                    match density with
                    | Density.EditorDensity.Compact -> "swt:-mx-1.5 swt:-mt-1.5"
                    | _ -> "swt:-mx-2.5 swt:-mt-2.5"

                // Each grouping-value tab gets its own color, like the colored index
                // tabs of an expanding file organizer.
                let tabPalette = [|
                    "swt:bg-primary swt:text-primary-content"
                    "swt:bg-secondary swt:text-secondary-content"
                    "swt:bg-accent swt:text-accent-content"
                    "swt:bg-info swt:text-info-content"
                    "swt:bg-success swt:text-success-content"
                    "swt:bg-warning swt:text-warning-content"
                    "swt:bg-error swt:text-error-content"
                |]

                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:cursor-pointer swt:flex-col swt:gap-2"
                    prop.onClick handleExpandClick
                    if defaultArg debug false then
                        prop.testId $"provenance-group-expand-surface-{side}-{group.Id}"
                    prop.children [
                        Html.div [
                            prop.className [
                                "swt:flex swt:min-w-0 swt:items-end swt:gap-1 swt:border-b swt:border-base-300 swt:px-3 swt:pt-1"
                                tabBarMargins
                            ]
                            prop.children [
                                match tabs with
                                | [] ->
                                    // Neutral label tab: same silhouette as a grouping-value
                                    // tab but muted and inert, so the endpoint name cannot
                                    // be mistaken for a grouping value.
                                    Html.span [
                                        prop.className
                                            "swt:min-w-0 swt:grow swt:truncate swt:rounded-t-md swt:bg-base-300 swt:px-3 swt:py-1 swt:text-xs swt:font-semibold swt:text-base-content/80"
                                        prop.title title
                                        prop.text title
                                    ]
                                | tabs ->
                                    for index, groupingValue in List.indexed tabs do
                                        let category = groupingValue.Key.Header.Category.Name

                                        let valueText = Formatting.formatValue groupingValue.Value groupingValue.Unit

                                        let tabMode =
                                            match focusedTabIndex with
                                            | Some focused when focused = index -> Focused
                                            | Some _ -> Collapsed
                                            | None -> Responsive

                                        GroupCard.OrganizerTab(
                                            category,
                                            valueText,
                                            DragDrop.groupingValueIdentity
                                                groupingValue.Key
                                                groupingValue.Value
                                                groupingValue.Unit,
                                            tabPalette.[index % tabPalette.Length],
                                            (hoveredTabIndex = Some index || focusedTabIndex = Some index),
                                            (fun highlighted ->
                                                setHoveredTabIndex (if highlighted then Some index else None)
                                            ),
                                            tabMode,
                                            (fun () -> toggleFocusedTab index),
                                            ?testId =
                                                (if defaultArg debug false then
                                                     Some $"provenance-group-tab-{side}-{group.Id}-{index}"
                                                 else
                                                     None),
                                            key = $"{index}:{category}={valueText}"
                                        )
                            ]
                        ]
                        Html.div [
                            prop.className "swt:flex swt:items-center swt:gap-2"
                            prop.children [
                                selectionCheckbox
                                // The expand trigger is drawn as a folder: a clipped back
                                // panel with its own index tab, the members' type symbols
                                // resting inside, and a front pocket they tuck behind.
                                Html.button [
                                    prop.type'.button
                                    prop.ariaLabel "Show members"
                                    prop.title "Show members"
                                    // Always-on anchor for the interactive tutorial's spotlight.
                                    prop.custom ("data-tutorial", "provenance-show-members")
                                    prop.onClick (fun _ -> onExpand ())
                                    prop.className "swt:group swt:relative swt:w-fit swt:max-w-full swt:cursor-pointer"
                                    prop.children [
                                        Html.span [
                                            prop.ariaHidden true
                                            prop.className
                                                "swt:absolute swt:inset-0 swt:rounded-md swt:bg-base-300 swt:transition-colors swt:group-hover:bg-primary/30 swt:[clip-path:polygon(0_0,2.5rem_0,3.25rem_0.5rem,100%_0.5rem,100%_100%,0_100%)]"
                                        ]
                                        Html.span [
                                            prop.ariaHidden true
                                            prop.className
                                                "swt:relative swt:flex swt:min-h-9 swt:items-center swt:gap-1 swt:px-3 swt:pb-2.5 swt:pt-3.5 swt:text-base-content/80"
                                            if defaultArg debug false then
                                                prop.testId $"provenance-group-symbols-{side}-{group.Id}"
                                            prop.children [
                                                match memberDescriptors with
                                                | [] -> countLabel group.Members.Length
                                                | descriptors when descriptors.Length <= maxInlineSymbols ->
                                                    for index, descriptor in List.indexed descriptors do
                                                        Html.span [
                                                            prop.key (string index)
                                                            prop.children [ symbolIcon descriptor ]
                                                        ]
                                                | descriptors ->
                                                    let dominant =
                                                        descriptors
                                                        |> List.countBy (fun descriptor -> descriptor.Label)
                                                        |> List.maxBy snd
                                                        |> fst

                                                    let icon =
                                                        descriptors
                                                        |> List.find (fun descriptor -> descriptor.Label = dominant)

                                                    symbolIcon icon
                                                    countLabel descriptors.Length
                                            ]
                                        ]
                                        Html.span [
                                            prop.ariaHidden true
                                            prop.className
                                                "swt:absolute swt:inset-x-0 swt:bottom-0 swt:h-[35%] swt:rounded-b-md swt:bg-base-200 swt:transition-colors swt:group-hover:bg-primary/20"
                                        ]
                                    ]
                                ]
                                connectionBadge
                            ]
                        ]
                    ]
                ]

                if expanded then
                    Html.ul [
                        // Always-on anchor for the interactive tutorial's spotlight.
                        prop.custom ("data-tutorial", "provenance-group-members")
                        prop.className [
                            // fade-in only: the member rows carry connector anchors, so a
                            // slide would put the measured positions off during entry.
                            "swt:space-y-1 swt:border-t swt:border-base-300 swt:pt-2 swt:motion-fade-in"
                            match density with
                            | Density.EditorDensity.Compact -> "swt:text-xs"
                            | _ -> "swt:text-sm"
                        ]
                        prop.children [
                            for member' in group.Members do
                                let memberValues = GroupCardData.memberValues member' model
                                let isHovered = hoveredMemberId = Some member'.SetId

                                let memberHandle: ConnectionHandleRef = {
                                    Kind = ConnectionHandleKind.GroupMember
                                    Side = side
                                    Id = member'.SetId
                                    ParentGroupId = Some group.Id
                                }

                                let memberPropertyAnchor: ConnectionHandleRef = {
                                    Kind = ConnectionHandleKind.GroupMemberPropertyAnchor
                                    Side = side
                                    Id = member'.SetId
                                    ParentGroupId = Some group.Id
                                }

                                Html.li [
                                    prop.className "swt:relative"
                                    // Sankey ribbons for member-level connections span this
                                    // row's facing edge, the way group ribbons span card edges.
                                    prop.custom (
                                        "data-provenance-member-node",
                                        DragDrop.memberNodeId side group.Id member'.SetId
                                    )
                                    prop.children [
                                        Controls.ConnectionAnchor(
                                            memberPropertyAnchor,
                                            memberPropertyAnchorEdge,
                                            ?debug = debug
                                        )
                                        Html.div [
                                            prop.className "swt:flex swt:items-center swt:gap-2"
                                            prop.children [
                                                if side = ProvenanceSide.Output then
                                                    Controls.ConnectionHandle(
                                                        memberHandle,
                                                        label = $"Connect {member'.Name}",
                                                        ?debug = debug
                                                    )
                                                Html.div [
                                                    prop.tabIndex 0
                                                    prop.ariaLabel $"Show values for {member'.Name}"
                                                    prop.className
                                                        "swt:flex swt:min-w-0 swt:grow swt:flex-col swt:gap-0.5 swt:rounded-md swt:px-2 swt:py-1 swt:outline-none swt:transition-colors hover:swt:bg-base-200 focus:swt:bg-base-200 focus:swt:ring-2 focus:swt:ring-primary/40"
                                                    if defaultArg debug false then
                                                        prop.testId $"provenance-group-member-{side}-{member'.SetId}"
                                                    prop.onMouseEnter (fun _ -> setHoveredMemberId (Some member'.SetId))
                                                    prop.onMouseLeave (fun _ -> setHoveredMemberId None)
                                                    prop.onFocus (fun _ -> setHoveredMemberId (Some member'.SetId))
                                                    prop.onBlur (fun _ -> setHoveredMemberId None)
                                                    prop.children [
                                                        match GroupCardData.endpointKind side model member'.SetId with
                                                        | Some kind -> EntityType.line (EntityType.descriptor kind)
                                                        | None -> Html.none
                                                        Html.span [
                                                            prop.className "swt:min-w-0 swt:truncate"
                                                            prop.text member'.Name
                                                        ]
                                                    ]
                                                ]
                                                if side = ProvenanceSide.Input then
                                                    Controls.ConnectionHandle(
                                                        memberHandle,
                                                        label = $"Connect {member'.Name}",
                                                        ?debug = debug
                                                    )
                                            ]
                                        ]

                                        if isHovered then
                                            Html.div [
                                                prop.className [
                                                    // The viewport cap keeps the popover readable when the
                                                    // editor itself is narrower than the preferred width.
                                                    "swt:absolute swt:top-0 swt:z-30 swt:w-72 swt:max-w-[60vw] swt:rounded-md swt:border swt:border-base-300 swt:bg-base-100 swt:p-2 swt:shadow-lg swt:motion-pop-in"
                                                    memberDetailsPosition
                                                ]
                                                if defaultArg debug false then
                                                    prop.testId $"provenance-member-values-{side}-{member'.SetId}"
                                                prop.children [
                                                    if memberValues.IsEmpty then
                                                        Html.p [
                                                            prop.className "swt:text-xs swt:text-base-content/60"
                                                            prop.text "No annotation values"
                                                        ]
                                                    else
                                                        Html.div [
                                                            prop.className "swt:flex swt:flex-wrap swt:gap-1"
                                                            prop.children [
                                                                for value in memberValues do
                                                                    let sourceInfo =
                                                                        sourceInfoForValue
                                                                        |> Option.bind (fun resolver -> resolver value)

                                                                    Controls.ValueLabel(
                                                                        value,
                                                                        ?sourceInfo = sourceInfo,
                                                                        key =
                                                                            $"member:{member'.SetId}:{DragDrop.propertyValueIdentity value}"
                                                                    )
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
