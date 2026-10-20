namespace Swate.Components.Page.ProvenanceGrouping

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.JsBindings
open Swate.Components.Primitive.Buttons
open Swate.Components.Primitive.Dropdown
open Swate.Components.Primitive.Popover
open Swate.Components.Shared.ProvenanceGrouping.Types
open Swate.Components.Shared.ProvenanceGrouping.Grouping
open Swate.Components.Shared.ProvenanceGrouping.Edit
open Swate.Components.Shared.ProvenanceGrouping.Session
open Swate.Components.Page.ProvenanceGrouping.Types
open Swate.Components.Composite.TermSearch
open Swate.Components.Composite.TermSearch.Types

[<Erase; Mangle(false)>]
type Controls =

    [<ReactComponent>]
    static member ConnectionHandle
        (handle: ConnectionHandleRef, ?label: string, ?className: string, ?debug: bool, ?key: string)
        =
        let draggable =
            DndKit.useDraggable (
                {|
                    id = DragDrop.connectionHandleDragId handle
                |}
            )

        let droppable =
            DndKit.useDroppable (
                {|
                    id = DragDrop.connectionHandleDropId handle
                |}
            )

        // While a connection drag runs or a handle is armed, handles on the opposite
        // side surface themselves as valid targets instead of waiting for a hover.
        let interaction = React.useContext ConnectionDragHints.context
        let isArmed = interaction.Armed = Some handle

        let isEligibleTarget =
            match interaction.SourceSide with
            | Some sourceSide -> sourceSide <> handle.Side && not draggable.isDragging && not isArmed
            | None -> false

        let setNodeRef node =
            draggable.setNodeRef node
            droppable.setNodeRef node

        let referenceProps = [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.ref setNodeRef
        ]

        let interactionProps = [
            prop.role.button
            prop.tabIndex 0
            prop.ariaLabel (label |> Option.defaultValue "Connect")
            prop.title "Drag or click to connect"
            prop.custom ("aria-pressed", isArmed)
            prop.custom ("data-provenance-connection-node", DragDrop.connectionHandleNodeId handle)
            prop.custom ("data-provenance-connection-drop-id", DragDrop.connectionHandleDropId handle)
            // Click-to-connect: tapping arms this handle, tapping a valid opposite
            // handle completes the connection. Keyboard users get the same flow.
            prop.onClick (fun _ -> interaction.OnHandleTap handle)
            prop.onKeyDown (fun event ->
                if event.key = "Enter" || event.key = " " then
                    event.preventDefault ()
                    interaction.OnHandleTap handle
            )
            prop.className [
                // Opaque theme-mixed fills instead of element opacity: connector lines
                // end underneath the handle, and a translucent dot let them (and their
                // halo) bleed through as a blotchy overlap.
                "swt:inline-flex swt:size-3 swt:shrink-0 swt:cursor-crosshair swt:items-center swt:justify-center swt:rounded-full swt:border swt:connector-handle swt:align-middle swt:transition"
                "focus:swt:outline-none focus:swt:ring-2 focus:swt:ring-primary/40"
                if droppable.isOver then
                    "swt:connector-handle-strong swt:ring-2 swt:ring-primary"
                elif isEligibleTarget then
                    "swt:connector-handle-strong swt:scale-125 swt:ring-2 swt:ring-primary/50 swt:ring-offset-1 swt:ring-offset-base-100"
                if draggable.isDragging || isArmed then
                    "swt:connector-handle-strong swt:ring-2 swt:ring-primary swt:ring-offset-2 swt:ring-offset-base-100"
                match className with
                | Some className -> className
                | None -> ()
            ]
        ]

        // Keep fixed properties out of Fable's nested sequence callbacks so React
        // Compiler can process the drag listeners without expanding the whole list.
        Html.span (
            referenceProps
            @ (prop.spread (!!draggable.attributes) |> Array.toList)
            @ (prop.spread (!!draggable.listeners) |> Array.toList)
            @ interactionProps
            @ [
                if defaultArg debug false then
                    prop.testId $"provenance-connection-handle-{handle.Side}-{handle.Kind}"
            ]
        )

    /// Measurement-only connector endpoint: the overlay reads its position, but it is
    /// never draggable or droppable and never receives pointer events.
    [<ReactComponent>]
    static member ConnectionAnchor(handle: ConnectionHandleRef, className: string, ?debug: bool) =
        Html.span [
            prop.ariaHidden true
            prop.custom ("data-provenance-connection-node", DragDrop.connectionHandleNodeId handle)
            prop.className [
                "swt:pointer-events-none swt:absolute swt:size-px"
                className
            ]
            if defaultArg debug false then
                prop.testId $"provenance-connection-anchor-{handle.Side}-{handle.Kind}"
        ]

    /// Compact "how this editor works" popover: the four-step workflow plus a
    /// legend for the origin symbols and connector styles.
    [<ReactComponent>]
    static member HelpLegend(?debug: bool) =
        let step (index: int) (title: string) (text: string) =
            Html.li [
                prop.className "swt:flex swt:gap-2"
                prop.children [
                    Html.span [
                        prop.className
                            "swt:flex swt:size-5 swt:shrink-0 swt:items-center swt:justify-center swt:rounded-full swt:bg-primary swt:text-xs swt:font-semibold swt:text-primary-content"
                        prop.text (string index)
                    ]
                    Html.span [
                        prop.className "swt:text-sm"
                        prop.children [
                            Html.span [ prop.className "swt:font-medium"; prop.text $"{title}: " ]
                            Html.span text
                        ]
                    ]
                ]
            ]

        let legendRow (symbol: ReactElement) (text: string) =
            Html.li [
                prop.className "swt:flex swt:items-center swt:gap-2 swt:text-sm"
                prop.children [
                    Html.span [
                        prop.className "swt:flex swt:w-8 swt:shrink-0 swt:justify-center"
                        prop.children [ symbol ]
                    ]
                    Html.span text
                ]
            ]

        let lineSample dashed =
            Svg.svg [
                svg.viewBox (0, 0, 32, 8)
                svg.className "swt:h-2 swt:w-8"
                svg.children [
                    Svg.line [
                        svg.x1 1
                        svg.y1 4
                        svg.x2 31
                        svg.y2 4
                        svg.custom ("stroke", "currentColor")
                        svg.strokeWidth 2
                        if dashed then
                            svg.custom ("strokeDasharray", "4 4")
                    ]
                ]
            ]

        Popover.Simple(
            ?debug =
                (if defaultArg debug false then
                     Some "provenance-help"
                 else
                     None),
            trigger =
                Html.button [
                    prop.type'.button
                    prop.className "swt:btn swt:btn-ghost swt:btn-xs"
                    prop.title "How this editor works"
                    prop.ariaLabel "How this editor works"
                    if defaultArg debug false then
                        prop.testId "provenance-help-trigger"
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--question-circle-20-regular swt:size-4"
                        ]
                        Html.span "Help"
                    ]
                ],
            content =
                Html.div [
                    prop.className "swt:flex swt:w-80 swt:flex-col swt:gap-2 swt:p-1"
                    if defaultArg debug false then
                        prop.testId "provenance-help-content"
                    prop.children [
                        Html.h3 [
                            prop.className "swt:text-sm swt:font-semibold swt:text-primary"
                            prop.text "How this editor works"
                        ]
                        Html.ol [
                            prop.className "swt:flex swt:flex-col swt:gap-1.5"
                            prop.children [
                                step
                                    1
                                    "Group"
                                    "Click an annotation in a side rail to merge entities sharing its values into one card."
                                step
                                    2
                                    "Annotate"
                                    "Expand an annotation's values and drag a value chip onto a card to set it for every member at once."
                                step
                                    3
                                    "Connect"
                                    "Drag between the round handles on opposite card edges (or tap one, then the other) to link inputs to outputs."
                                step
                                    4
                                    "Continue"
                                    "Select cards with their checkboxes and add a layer; the selection carries over as the new layer's inputs."
                            ]
                        ]
                        Html.h4 [
                            prop.className "swt:pt-1 swt:text-sm swt:font-semibold swt:text-primary"
                            prop.text "Symbols"
                        ]
                        Html.ul [
                            prop.className "swt:flex swt:flex-col swt:gap-1"
                            prop.children [
                                legendRow (OriginSymbols.currentIcon "swt:size-4") "Value from the current table"
                                legendRow
                                    (OriginSymbols.upstreamIcon "swt:size-4")
                                    "Value inherited from an upstream table"
                                legendRow (lineSample false) "Input–output connection"
                                legendRow (lineSample true) "Where an annotation or value occurs"
                            ]
                        ]
                    ]
                ]
        )

    [<ReactComponent>]
    static member private LayerPageArrow
        (
            label: string,
            iconClass: string,
            target: ProvenanceLayerId option,
            onSelect: ProvenanceLayerId -> unit,
            testId: string,
            ?debug: bool
        ) =
        Html.button [
            prop.type'.button
            prop.className [
                "swt:btn swt:btn-sm swt:btn-outline swt:w-10 swt:px-0"
                if target.IsNone then
                    "swt:opacity-40"
            ]
            prop.title label
            prop.ariaLabel label
            prop.disabled target.IsNone
            if target.IsSome && defaultArg debug false then
                prop.testId testId
            prop.onClick (fun _ -> target |> Option.iter onSelect)
            prop.children [
                Html.i [ prop.className $"swt:iconify {iconClass} swt:size-4" ]
            ]
        ]

    [<ReactComponent>]
    static member private LayerPageButton
        (
            layerId: ProvenanceLayerId,
            name: string,
            isActive: bool,
            onSelect: ProvenanceLayerId -> unit,
            ?sourceColor: ProvenanceColor,
            ?debug: bool,
            ?key: string
        ) =
        Html.button [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.type'.button
            prop.title $"View provenance layer {name}"
            prop.ariaLabel $"View provenance layer {name}"
            prop.custom ("data-provenance-layer-page", layerId)
            prop.className [
                "swt:btn swt:btn-sm swt:min-w-0 swt:px-3"
                if isActive then
                    "swt:btn-primary swt:font-semibold"
                else
                    "swt:btn-outline swt:opacity-50 swt:hover:opacity-100"
            ]
            // Equal-width slots keep the bar from resizing as the window moves;
            // percent-based so embeds at any width scale with their container
            // instead of the viewport.
            prop.style [ style.custom ("width", "clamp(4.5rem, 18%, 7rem)") ]
            if defaultArg debug false then
                prop.testId $"provenance-layer-{layerId}"
                prop.custom ("data-provenance-layer-color", sourceColor |> Option.defaultValue "")
            prop.onClick (fun _ -> onSelect layerId)
            prop.children [
                match sourceColor with
                | Some color when color <> "" ->
                    Html.span [
                        prop.className "swt:size-2 swt:shrink-0 swt:rounded-full"
                        prop.style [ style.backgroundColor color ]
                    ]
                | _ -> Html.none
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate"
                    prop.text name
                ]
            ]
        ]

    [<ReactComponent>]
    static member private LayerJumpPopover
        (session: ProvenanceSession, onSelect: ProvenanceLayerId -> unit, ?debug: bool)
        =
        let isOpen, setIsOpen = React.useState false

        // The trigger doubles as the "layer x of n" indicator, so the window
        // of three page buttons never hides how long the chain actually is.
        let activeIndex =
            session.LayerOrder
            |> List.tryFindIndex (fun layerId -> layerId = session.ActiveLayerId)
            |> Option.defaultValue 0

        Popover.Popover(
            isOpen = isOpen,
            onOpenChange = setIsOpen,
            ?debug =
                (if defaultArg debug false then
                     Some "provenance-layer-jump-popover"
                 else
                     None),
            children =
                React.Fragment [
                    Popover.Trigger(
                        Html.button [
                            prop.title "Jump to layer"
                            prop.ariaLabel "Jump to layer"
                            prop.type'.button
                            prop.className
                                "swt:btn swt:btn-sm swt:btn-outline swt:min-w-10 swt:shrink-0 swt:whitespace-nowrap swt:px-2 swt:font-semibold"
                            if defaultArg debug false then
                                prop.testId "provenance-layer-jump"
                            prop.text $"{activeIndex + 1} / {session.LayerOrder.Length}"
                        ]
                    )
                    Popover.Content(
                        children =
                            Html.div [
                                prop.className "swt:flex swt:min-w-48 swt:flex-col swt:gap-2 swt:p-2"
                                prop.children [
                                    Html.label [ prop.className "swt:label"; prop.text "Jump to layer" ]
                                    Html.select [
                                        prop.className "swt:select swt:select-bordered swt:select-sm swt:w-full"
                                        prop.ariaLabel "Select layer"
                                        prop.title "Jump to a layer"
                                        if defaultArg debug false then
                                            prop.testId "provenance-layer-select"
                                        prop.value session.ActiveLayerId
                                        prop.onChange (fun (layerId: string) ->
                                            onSelect layerId
                                            setIsOpen false
                                        )
                                        prop.children [
                                            for layerId in session.LayerOrder do
                                                let layer = Session.layerById layerId session

                                                Html.option [
                                                    prop.key layerId
                                                    prop.value layerId
                                                    prop.text layer.Model.Source.Name
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                    )
                ]
        )

    [<ReactComponent>]
    static member private AddLayerPopover
        (session: ProvenanceSession, onAddLayer: ProvenanceSourceName -> unit, ?seedSummary: string, ?debug: bool)
        =
        let defaultNextLayerName (session: ProvenanceSession) =
            $"Layer {session.LayerOrder.Length + 1}"

        let isOpen, setIsOpen = React.useState false
        let layerName, setLayerName = React.useState (defaultNextLayerName session)

        React.useEffect ((fun () -> setLayerName (defaultNextLayerName session)), [| box session.LayerOrder.Length |])

        let submitLayerName () =
            let trimmed = if isNull layerName then "" else layerName.Trim()

            if not (String.IsNullOrWhiteSpace trimmed) then
                onAddLayer trimmed
                setLayerName (defaultNextLayerName session)
                setIsOpen false

        Popover.Popover(
            isOpen = isOpen,
            onOpenChange = setIsOpen,
            ?debug =
                (if defaultArg debug false then
                     Some "provenance-add-layer-popover"
                 else
                     None),
            children =
                React.Fragment [
                    Popover.Trigger(
                        Html.button [
                            prop.title "Add layer"
                            prop.type'.button
                            prop.className "swt:btn swt:btn-sm swt:btn-primary"
                            // Always-on anchor for the interactive tutorial's spotlight.
                            prop.custom ("data-tutorial", "provenance-add-layer")
                            if defaultArg debug false then
                                prop.testId "provenance-add-layer"
                            prop.children [
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--add-square-multiple-20-regular swt:size-4"
                                ]
                                Html.span "Layer"
                            ]
                        ]
                    )
                    Popover.Content(
                        children =
                            Html.form [
                                prop.className "swt:flex swt:min-w-56 swt:flex-col swt:gap-2 swt:p-2"
                                prop.onSubmit (fun event ->
                                    event.preventDefault ()
                                    submitLayerName ()
                                )
                                prop.children [
                                    // Announces which entities will seed the new layer's inputs, so
                                    // the effect of the current selection (or its absence) is visible
                                    // before the layer is created.
                                    match seedSummary with
                                    | Some summary ->
                                        Html.p [
                                            prop.className "swt:max-w-56 swt:text-xs swt:text-base-content/70"
                                            if defaultArg debug false then
                                                prop.testId "provenance-layer-seed-summary"
                                            prop.text summary
                                        ]
                                    | None -> ()
                                    Html.label [ prop.className "swt:label"; prop.text "Layer name" ]
                                    Html.input [
                                        prop.ariaLabel "Layer name"
                                        prop.className "swt:input swt:input-bordered swt:input-sm"
                                        prop.required true
                                        prop.value layerName
                                        prop.onChange setLayerName
                                    ]
                                    Html.button [
                                        prop.type'.submit
                                        prop.className "swt:btn swt:btn-primary swt:btn-sm"
                                        prop.text "Create layer"
                                    ]
                                ]
                            ]
                    )
                ]
        )

    [<ReactComponent>]
    static member LayerPagination
        (
            session: ProvenanceSession,
            onSelect: ProvenanceLayerId -> unit,
            onAddLayer: ProvenanceSourceName -> unit,
            ?debug: bool,
            ?sourceColors: Map<ProvenanceSourceId, ProvenanceColor>,
            ?onSetSourceColor: ProvenanceSourceId -> ProvenanceColor option -> unit,
            ?seedSummary: string
        ) =
        let activeIndex =
            session.LayerOrder
            |> List.tryFindIndex (fun layerId -> layerId = session.ActiveLayerId)
            |> Option.defaultValue 0

        // The active layer plus one neighbor on each side, clamped to the ends.
        let visibleLayerIds =
            let start = max 0 (min (activeIndex - 1) (session.LayerOrder.Length - 3))
            session.LayerOrder |> List.skip start |> List.truncate 3

        let sourceColorOf (layer: ProvenanceLayer) =
            sourceColors
            |> Option.bind (fun colors -> colors |> Map.tryFind layer.Model.Source.Id)

        let activeLayer = Session.layerById session.ActiveLayerId session

        Html.div [
            prop.className
                "swt:pointer-events-auto swt:flex swt:max-w-full swt:min-w-0 swt:items-center swt:justify-center swt:gap-2 swt:rounded-box swt:border swt:border-base-content/20 swt:bg-base-100 swt:px-3 swt:py-2 swt:shadow-md swt:ring-1 swt:ring-base-300"
            // A fixed bar width so the control does not shift as layer names
            // change; capped by the container, not the viewport, so embedded
            // editors size the bar to themselves.
            prop.style [ style.custom ("width", "min(40rem, 100%)") ]
            prop.custom ("role", "navigation")
            prop.ariaLabel "Layer pagination"
            prop.custom ("data-tutorial", "provenance-layer-pagination")
            if defaultArg debug false then
                prop.testId "provenance-layer-pagination"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:flex-1 swt:items-center swt:justify-center swt:gap-1"
                    if defaultArg debug false then
                        prop.testId "provenance-layer-pages"
                    prop.children [
                        Controls.LayerPageArrow(
                            "Previous layer",
                            "swt:fluent--chevron-left-20-regular",
                            session.LayerOrder |> List.tryItem (activeIndex - 1),
                            onSelect,
                            "provenance-layer-prev",
                            ?debug = debug
                        )
                        for layerId in visibleLayerIds do
                            let layer = Session.layerById layerId session

                            Controls.LayerPageButton(
                                layerId,
                                layer.Model.Source.Name,
                                (layerId = session.ActiveLayerId),
                                onSelect,
                                ?sourceColor = sourceColorOf layer,
                                ?debug = debug,
                                key = layerId
                            )
                        Controls.LayerPageArrow(
                            "Next layer",
                            "swt:fluent--chevron-right-20-regular",
                            session.LayerOrder |> List.tryItem (activeIndex + 1),
                            onSelect,
                            "provenance-layer-next",
                            ?debug = debug
                        )
                    ]
                ]
                Controls.LayerJumpPopover(session, onSelect, ?debug = debug)
                match onSetSourceColor with
                | Some setSourceColor ->
                    Controls.LayerColorButton(
                        activeLayer.Model.Source.Id,
                        sourceColorOf activeLayer,
                        setSourceColor activeLayer.Model.Source.Id,
                        triggerClassName = "swt:btn swt:btn-sm swt:btn-outline swt:min-h-8 swt:w-8 swt:px-0"
                    )
                | None -> Html.none
                Controls.AddLayerPopover(session, onAddLayer, ?seedSummary = seedSummary, ?debug = debug)
            ]
        ]

    [<ReactComponent>]
    static member LayerColorButton
        (
            sourceId: ProvenanceSourceId,
            currentColor: ProvenanceColor option,
            onSetColor: ProvenanceColor option -> unit,
            ?triggerClassName: string
        ) : ReactElement =
        let draftColor, setDraftColor =
            React.useState (ColorPicker.currentOrFallback currentColor)

        React.useEffect ((fun () -> setDraftColor (ColorPicker.currentOrFallback currentColor)), [| box currentColor |])

        let triggerClassName =
            defaultArg
                triggerClassName
                "swt:size-4 swt:shrink-0 swt:rounded swt:border swt:border-base-300 swt:cursor-pointer swt:align-middle"

        Popover.Simple(
            trigger =
                Html.button [
                    prop.type'.button
                    prop.className triggerClassName
                    match currentColor with
                    | Some c when c <> "" -> prop.style [ style.backgroundColor c ]
                    | _ -> ()
                    prop.ariaLabel $"Set color for source {sourceId}"
                ],
            content = ColorPicker.content $"Choose color for source {sourceId}" draftColor setDraftColor onSetColor
        )

    [<ReactComponent>]
    static member private PropertySwapButton
        (side: ProvenanceSide, property: ProvenancePropertyKey, onSwitch: ProvenancePropertyKey -> unit, ?debug: bool)
        =
        let header = property.Header
        let sideName = SideLabels.sideName side

        Html.button [
            prop.title $"Move {header.Category.Name} from {sideName}"
            prop.type'.button
            prop.className "swt:btn swt:btn-xs swt:btn-ghost swt:btn-square swt:btn-outline swt:z-10 swt:bg-base-100/90"
            prop.ariaLabel $"Move {header.Category.Name} from {sideName}"
            if defaultArg debug false then
                prop.testId $"provenance-property-drag-{side}-{header.Category.Name}"
            prop.onPointerUp (fun _ -> onSwitch property)
            prop.onClick (fun _ -> onSwitch property)
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--arrow-swap-20-regular swt:size-4"
                ]
            ]
        ]

    [<ReactComponent>]
    static member private PropertyRailItem
        (
            side: ProvenanceSide,
            activeSourceId: ProvenanceSourceId,
            property: ProvenancePropertyKey,
            propertyValues: ProvenancePropertyValue list,
            active: GroupingAssignment list,
            canSwitch: bool,
            expanded: bool,
            onToggleSide: ProvenancePropertyKey -> unit,
            onToggleBoth: ProvenancePropertyKey -> unit,
            onSwitch: ProvenancePropertyKey -> unit,
            onToggleExpanded: ProvenancePropertyKey -> unit,
            onAddValue: ProvenancePropertyKey -> ProvenanceValue -> ProvenanceTerm option -> unit,
            setIsValueChipDragging: bool -> unit,
            ?debug: bool,
            ?key: string,
            ?stats: PropertyStats,
            ?badge: PropertyCountBadge,
            ?color: ProvenanceColor,
            ?origins: Set<ProvenancePropertyOrigin>,
            ?onSetColor: ProvenanceColor option -> unit,
            ?sourceInfoForValue: ProvenancePropertyValue -> PropertyValueSourceInfo option,
            ?isUnassignedValue: ProvenancePropertyValue -> bool,
            ?onApplyValueToSelection: ProvenancePropertyValue -> unit,
            ?applySelectionLabel: string
        ) =
        let header = property.Header

        let draggable =
            DndKit.useDraggable (
                {|
                    id = DragDrop.propertyDragId side property
                    data = {| label = header.Category.Name |}
                |}
            )

        let density = React.useContext Density.context
        let controlsVisible, setControlsVisible = React.useState false

        let sideScope =
            match side with
            | ProvenanceSide.Input -> GroupingScope.Input
            | ProvenanceSide.Output -> GroupingScope.Output

        let sideSelected =
            active
            |> List.exists (fun assignment -> assignment.Key = property && assignment.Scope = sideScope)

        let bothSelected =
            active
            |> List.exists (fun assignment -> assignment.Key = property && assignment.Scope = GroupingScope.Both)

        let sideName = SideLabels.sideName side

        // Property headers are never draggable connection sources; the overlay derives
        // their connectors from model data and only needs this anchor to measure the
        // group-facing edge of the header button. The anchor lives inside the button so
        // it follows the button edge as the button resizes with its content.
        let propertyAnchor =
            Controls.ConnectionAnchor(
                {
                    Kind = ConnectionHandleKind.PropertyHeader
                    Side = side
                    Id = DragDrop.propertyKeyIdentity property
                    ParentGroupId = None
                },
                (match side with
                 | ProvenanceSide.Input -> "swt:top-1/2 swt:right-0 swt:translate-x-1/2 swt:-translate-y-1/2"
                 | ProvenanceSide.Output -> "swt:top-1/2 swt:left-0 swt:-translate-x-1/2 swt:-translate-y-1/2"),
                ?debug = debug
            )

        let propertyButton =
            Html.button [
                prop.type'.button
                prop.title (
                    if sideSelected then
                        $"Stop grouping by {header.Category.Name}"
                    else
                        $"Group {sideName} entities by {header.Category.Name}"
                )
                if canSwitch then
                    prop.ref draggable.setNodeRef
                    yield! prop.spread (!!draggable.attributes)
                    yield! prop.spread (!!draggable.listeners)
                prop.className [
                    // swt:shrink overrides the btn class's flex-shrink:0 so the button can
                    // give way (and truncate) when the rail row runs out of space.
                    "swt:btn swt:btn-sm swt:h-auto swt:relative swt:shrink swt:min-w-0 swt:max-w-[18rem] swt:justify-start "
                    "swt:@max-xs/provenancePanel:px-2 swt:@max-xs/provenancePanel:text-[0.7rem]"
                    match density with
                    | Density.EditorDensity.Compact -> "swt:min-h-6 swt:py-0.5 swt:text-[0.7rem]"
                    | _ -> "swt:min-h-8 swt:py-1"

                    match side with
                    | ProvenanceSide.Input -> "swt:rounded-l-none"
                    | ProvenanceSide.Output -> "swt:rounded-r-none"
                    if sideSelected then
                        "swt:btn-primary"
                    else
                        "swt:btn-outline"
                    if not sideSelected then
                        "swt:bg-base-100/90"
                    if canSwitch then
                        yield! Styles.draggableButtonClasses draggable.isDragging

                ]
                prop.custom ("data-provenance-resize-node", "true")
                // Always-on anchor so the tutorial can target this button
                // without coupling to its title copy.
                prop.custom ("data-tutorial-group-by", $"{side}:{header.Category.Name}")
                if defaultArg debug false then
                    prop.testId $"provenance-property-{side}-{header.Category.Name}"
                prop.custom ("data-provenance-property-origin", property.OriginSource.Id)
                prop.onClick (fun _ -> onToggleSide property)
                prop.children [
                    propertyAnchor
                    match color with
                    | Some c when c <> "" ->
                        Html.span [
                            prop.className "swt:size-2.5 swt:shrink-0 swt:rounded"
                            prop.style [ style.backgroundColor c ]
                        ]
                    | _ -> Html.none
                    Html.span [
                        prop.className "swt:min-w-0 swt:truncate swt:text-left"
                        prop.text header.Category.Name
                    ]
                    match badge with
                    | Some badge ->
                        match badge with
                        | PropertyCountBadge.Hide -> Html.none
                        | PropertyCountBadge.DistinctValues n ->
                            Html.span [
                                prop.className "swt:badge swt:badge-xs swt:shrink-0"
                                prop.text (string n)
                            ]
                        | PropertyCountBadge.Coverage(setsWithValue, total) ->
                            Html.span [
                                prop.className "swt:badge swt:badge-xs swt:badge-warning swt:shrink-0"
                                prop.text $"{setsWithValue}/{total}"
                            ]
                    | None -> Html.none
                    match origins with
                    | Some origins ->
                        let sourceOfOrigin =
                            function
                            | ProvenancePropertyOrigin.Real anchor
                            | ProvenancePropertyOrigin.Virtual anchor -> anchor.Source

                        let hasCurrent =
                            origins
                            |> Set.exists (fun origin -> (sourceOfOrigin origin).Id = activeSourceId)

                        let hasUpstream =
                            origins
                            |> Set.exists (fun origin -> (sourceOfOrigin origin).Id <> activeSourceId)

                        if hasCurrent && hasUpstream then
                            Html.span [
                                prop.className "swt:shrink-0 swt:text-base-content/60"
                                prop.title "Current and upstream"
                                prop.children [ OriginSymbols.bothIcons "swt:size-3" ]
                            ]
                        elif hasCurrent then
                            Html.span [
                                prop.className "swt:shrink-0 swt:text-base-content/60"
                                prop.title "Current"
                                prop.children [ OriginSymbols.currentIcon "swt:size-3" ]
                            ]
                        elif hasUpstream then
                            Html.span [
                                prop.className "swt:shrink-0 swt:text-base-content/60"
                                prop.title "Upstream"
                                prop.children [ OriginSymbols.upstreamIcon "swt:size-3" ]
                            ]
                        else
                            Html.none
                    | None -> Html.none
                ]
            ]

        let expandButton =
            Html.button [
                prop.type'.button
                prop.className [
                    "swt:btn swt:btn-xs swt:btn-square swt:z-10 swt:btn-outline swt:bg-base-100/90"

                    match side with
                    | ProvenanceSide.Input -> " swt:rounded-r-none swt:border-r-0"
                    | ProvenanceSide.Output -> "swt:rounded-l-none swt:border-l-0"
                    match density with
                    | Density.EditorDensity.Compact -> "swt:min-h-6 swt:py-0.5"
                    | _ -> "swt:min-h-8 swt:py-1"
                ]
                if defaultArg debug false then
                    prop.testId $"provenance-property-expand-{side}-{header.Category.Name}"
                // Always-on anchor so the tutorial can target the chevron
                // without coupling to its aria-label copy.
                prop.custom ("data-tutorial-expand-values", $"{side}:{header.Category.Name}")
                prop.ariaLabel (
                    if expanded then
                        $"Collapse {header.Category.Name} values"
                    else
                        $"Expand {header.Category.Name} values"
                )
                prop.onClick (fun _ -> onToggleExpanded property)
                prop.children [
                    Html.i [
                        prop.className [
                            "swt:iconify swt:size-4"
                            if expanded then
                                "swt:fluent--chevron-up-20-regular"
                            else
                                "swt:fluent--chevron-down-20-regular"
                        ]
                    ]
                ]
            ]

        let propertyButtonWithExpand =
            Html.div [
                prop.className "swt:flex swt:min-w-0"
                prop.children [
                    match side with
                    | ProvenanceSide.Input ->
                        expandButton
                        propertyButton
                    | ProvenanceSide.Output ->
                        propertyButton
                        expandButton
                ]
            ]

        let bothButton =
            Html.button [
                prop.type'.button
                prop.title
                    $"Group both sides by {header.Category.Name}. Connected inputs and outputs share their values for grouping."
                prop.className [
                    "swt:btn swt:btn-xs swt:btn-square swt:z-10"
                    if bothSelected then
                        "swt:btn-primary"
                    else
                        "swt:btn-outline"
                    if not bothSelected then
                        "swt:bg-base-100/90"
                ]
                if defaultArg debug false then
                    prop.testId $"provenance-property-both-{side}-{header.Category.Name}"
                prop.ariaLabel $"Group {header.Category.Name} on both sides"
                prop.onClick (fun _ -> onToggleBoth property)
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--link-multiple-20-regular swt:size-4"
                    ]
                ]
            ]

        let swapButton =
            if canSwitch then
                Controls.PropertySwapButton(side, property, onSwitch, ?debug = debug)
            else
                Html.button [
                    prop.type'.button
                    prop.disabled true
                    prop.className
                        "swt:btn swt:btn-xs swt:btn-ghost swt:btn-square swt:z-10 swt:btn-outline swt:border-white swt:bg-base-100/90"
                    prop.ariaLabel $"Move {header.Category.Name} from {sideName}"
                    if defaultArg debug false then
                        prop.testId $"provenance-property-drag-{side}-{header.Category.Name}"
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--arrow-swap-20-regular swt:size-4"
                        ]
                    ]
                ]

        let colorButton =
            match onSetColor with
            | Some setColor -> Controls.PropertyColorButton(header, color, setColor)
            | None -> Html.none

        // The secondary controls leave the layout entirely until their row is
        // hovered or holds focus, so idle rows are only as wide as their label.
        let rowControls =
            Html.span [
                prop.className [
                    "swt:flex swt:items-center swt:gap-0.5"
                    if not controlsVisible then
                        "swt:hidden"
                ]
                prop.children [
                    match side with
                    | ProvenanceSide.Input ->
                        colorButton
                        swapButton
                        bothButton

                    | ProvenanceSide.Output ->
                        bothButton
                        swapButton
                        colorButton
                ]
            ]

        Html.div [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.className "swt:flex swt:flex-col swt:gap-1"
            prop.children [
                Html.div [
                    prop.className [
                        "swt:flex swt:items-center swt:gap-1"
                        // The whole row hugs the rail-facing side, so the group-facing
                        // button edge (with its connector anchor) moves with the button's
                        // content size and frees the rest of the rail for connectors.
                        if side = ProvenanceSide.Output then
                            "swt:justify-end"
                    ]
                    prop.onMouseEnter (fun _ -> setControlsVisible true)
                    prop.onMouseLeave (fun _ -> setControlsVisible false)
                    // React maps onFocus/onBlur to focusin/focusout, so these bubble up
                    // from the row's buttons; blur only hides the controls when focus
                    // actually leaves the row.
                    prop.onFocus (fun _ -> setControlsVisible true)
                    prop.onBlur (fun event ->
                        let row: Browser.Types.HTMLElement = unbox event.currentTarget
                        let related: Browser.Types.HTMLElement = unbox event.relatedTarget

                        if isNull (box related) || not (row.contains related) then
                            setControlsVisible false
                    )
                    prop.children [
                        // The controls sit on the rail-facing side; the property text
                        // button faces the group cards so connectors attach directly to it.
                        match side with
                        | ProvenanceSide.Input ->
                            propertyButtonWithExpand
                            rowControls
                        | ProvenanceSide.Output ->
                            rowControls
                            propertyButtonWithExpand
                    ]
                ]
                if expanded then
                    Html.div [
                        // Always-on anchor so the tutorial can ring the expanded
                        // values without coupling to the debug test id.
                        prop.custom ("data-tutorial-property-values", $"{side}:{header.Category.Name}")
                        prop.className [
                            // fade-in only: the chips carry connector anchors, so a slide
                            // would put the measured positions off during entry.
                            "swt:flex swt:flex-col swt:gap-1 swt:z-20 swt:motion-fade-in"
                            match side with
                            | ProvenanceSide.Input -> "swt:items-start swt:pl-2"
                            | ProvenanceSide.Output -> "swt:items-end swt:pr-2"
                        ]
                        if defaultArg debug false then
                            prop.testId $"provenance-property-values-{side}-{header.Category.Name}"
                        prop.children [
                            for propertyValue in propertyValues do
                                let sourceInfo =
                                    sourceInfoForValue |> Option.bind (fun resolver -> resolver propertyValue)

                                let unassigned =
                                    isUnassignedValue
                                    |> Option.map (fun isUnassigned -> isUnassigned propertyValue)
                                    |> Option.defaultValue false

                                Controls.ValueChip(
                                    propertyValue,
                                    onDragChanged = setIsValueChipDragging,
                                    draggable = true,
                                    showHeader = false,
                                    anchorSide = side,
                                    ?debug = debug,
                                    ?sourceInfo = sourceInfo,
                                    unassigned = unassigned,
                                    ?onApplyToSelection =
                                        (onApplyValueToSelection
                                         |> Option.map (fun apply -> fun () -> apply propertyValue)),
                                    ?applySelectionLabel = applySelectionLabel,
                                    key = DragDrop.propertyValueIdentity propertyValue
                                )
                            Controls.AddValuePopover(
                                Some header,
                                (fun _ value unit -> onAddValue property value unit),
                                label = "Add value",
                                ?debug = debug
                            )
                        ]
                    ]
                elif propertyValues.IsEmpty then
                    Html.none
                else
                    Html.none
            ]
        ]

    [<ReactComponent>]
    static member PropertyRail
        (
            side: ProvenanceSide,
            activeSource: ProvenanceSourceRef,
            headers: ProvenancePropertyKey list,
            active: GroupingAssignment list,
            valuesForHeader: ProvenancePropertyKey -> ProvenancePropertyValue list,
            isExpanded: ProvenancePropertyKey -> bool,
            onToggleSide: ProvenancePropertyKey -> unit,
            onToggleBoth: ProvenancePropertyKey -> unit,
            onSwitch: ProvenancePropertyKey -> unit,
            onToggleExpanded: ProvenancePropertyKey -> unit,
            onAddValue: ProvenancePropertyKey -> ProvenanceValue -> ProvenanceTerm option -> unit,
            canSwitch: ProvenancePropertyKey -> bool,
            isDropRejected: bool,
            isDropAvailable: bool,
            setIsValueChipDragging: bool -> unit,
            statsForHeader: ProvenancePropertyKey -> PropertyStats option,
            badgeForHeader: ProvenancePropertyKey -> PropertyCountBadge option,
            colorForHeader: ProvenancePropertyKey -> ProvenanceColor option,
            originsForHeader: ProvenancePropertyKey -> Set<ProvenancePropertyOrigin> option,
            onSetColor: ProvenancePropertyKey -> ProvenanceColor option -> unit,
            sourceInfoForValue: ProvenancePropertyValue -> PropertyValueSourceInfo option,
            ?sideId: ProvenanceLayerSideId,
            ?isUnassignedValue: ProvenancePropertyValue -> bool,
            ?onApplyValueToSelection: ProvenancePropertyValue -> unit,
            ?applySelectionLabel: string,
            ?debug: bool
        ) =
        let droppable =
            DndKit.useDroppable (
                {|
                    id = DragDrop.propertyRailDropId side
                |}
            )

        let showAllHeaders, setShowAllHeaders = React.useState false

        // Long rails keep grouped/expanded properties visible and fold the rest
        // behind a toggle, so dense models do not bury the active properties.
        let collapseThreshold = 6

        let isPinned header =
            active |> List.exists (fun assignment -> assignment.Key = header)
            || isExpanded header

        let visibleHeaders =
            if showAllHeaders || headers.Length <= collapseThreshold then
                headers
            else
                let pinned = headers |> List.filter isPinned

                let filler =
                    headers
                    |> List.filter (isPinned >> not)
                    |> List.truncate (max 0 (collapseThreshold - pinned.Length))

                headers
                |> List.filter (fun header -> pinned |> List.contains header || filler |> List.contains header)

        let hiddenHeaderCount = headers.Length - visibleHeaders.Length

        let dropState =
            if droppable.isOver && isDropRejected then "rejecting"
            elif droppable.isOver then "over"
            else "idle"

        Html.aside [
            prop.ref droppable.setNodeRef
            prop.className [
                "swt:flex swt:min-h-[32rem] swt:min-w-0 swt:flex-col swt:gap-2 swt:rounded swt:border swt:border-dashed swt:border-base-content/25 swt:border-2 swt:p-3 swt:transition-colors"
                if dropState = "rejecting" then
                    "swt:border-warning swt:bg-warning/10 swt:ring-2 swt:ring-warning/30"
                elif droppable.isOver then
                    "swt:border-primary swt:bg-primary/10"
                // While a property drag is under way, the rails announce early whether
                // they would accept or reject the drop, before the pointer arrives.
                elif isDropRejected then
                    "swt:border-warning/50"
                elif isDropAvailable then
                    "swt:border-primary/50"
                if headers.IsEmpty then
                    "swt:items-center swt:justify-center"
            ]
            prop.custom ("data-provenance-drop-state", dropState)
            // Always-on anchor for the interactive tutorial's spotlight.
            prop.custom ("data-tutorial", $"provenance-rail-{side}")
            if defaultArg debug false then
                prop.testId $"provenance-property-rail-{side}"

                match sideId with
                | Some sideId -> prop.custom ("data-provenance-side-id", sideId)
                | None -> ()
            prop.children [
                if headers.IsEmpty then
                    Html.p [
                        prop.className "swt:text-sm swt:text-base-content/60 swt:text-center swt:py-8"
                        prop.text "Drag annotations here, then click one to group by it"
                    ]

                    Html.div [
                        prop.className "swt:w-fit"
                        prop.children [
                            Controls.AddValuePopover(
                                None,
                                (fun header value unit ->
                                    onAddValue
                                        {
                                            Header = header
                                            OriginSource = activeSource
                                        }
                                        value
                                        unit
                                ),
                                label = "Add annotation",
                                ?debug = debug
                            )
                        ]
                    ]
                else
                    Html.h3 [
                        prop.className [
                            "swt:text-sm swt:font-semibold swt:text-primary"
                            if side = ProvenanceSide.Output then
                                "swt:self-end"
                        ]
                        prop.title "Click an annotation to group this side's entities by its values"
                        prop.text "Group by"
                    ]

                    for header in visibleHeaders do
                        Controls.PropertyRailItem(
                            side,
                            activeSource.Id,
                            header,
                            valuesForHeader header,
                            active,
                            canSwitch header,
                            isExpanded header,
                            onToggleSide,
                            onToggleBoth,
                            onSwitch,
                            onToggleExpanded,
                            onAddValue,
                            setIsValueChipDragging,
                            ?stats = statsForHeader header,
                            ?badge = badgeForHeader header,
                            ?color = colorForHeader header,
                            ?origins = originsForHeader header,
                            onSetColor = onSetColor header,
                            sourceInfoForValue = sourceInfoForValue,
                            ?isUnassignedValue = isUnassignedValue,
                            ?onApplyValueToSelection = onApplyValueToSelection,
                            ?applySelectionLabel = applySelectionLabel,
                            debug = defaultArg debug false,
                            key = DragDrop.propertyKeyIdentity header
                        )

                    if hiddenHeaderCount > 0 || showAllHeaders && headers.Length > collapseThreshold then
                        Html.button [
                            prop.type'.button
                            prop.className [
                                "swt:btn swt:btn-ghost swt:btn-xs swt:w-fit"
                                if side = ProvenanceSide.Output then
                                    "swt:self-end"
                            ]
                            if defaultArg debug false then
                                prop.testId $"provenance-property-overflow-{side}"
                            prop.onClick (fun _ -> setShowAllHeaders (not showAllHeaders))
                            prop.text (
                                if showAllHeaders then
                                    "Show fewer"
                                else
                                    $"+{hiddenHeaderCount} more"
                            )
                        ]

                    Html.div [
                        prop.className [
                            "swt:w-fit"
                            if side = ProvenanceSide.Output then
                                "swt:self-end"
                        ]
                        prop.children [
                            Controls.AddValuePopover(
                                None,
                                (fun header value unit ->
                                    onAddValue
                                        {
                                            Header = header
                                            OriginSource = activeSource
                                        }
                                        value
                                        unit
                                ),
                                label = "Add annotation",
                                ?debug = debug
                            )
                        ]
                    ]
            ]
        ]

    [<ReactComponent>]
    static member AddValuePopover
        (
            header: ProvenancePropertyHeader option,
            onCreate: ProvenancePropertyHeader -> ProvenanceValue -> ProvenanceTerm option -> unit,
            ?label: string,
            ?debug: bool
        ) : ReactElement =
        let propertyKind =
            header
            |> Option.map (fun known -> known.Kind)
            |> Option.defaultValue KindNames.editorProperty

        let categoryTerm, setCategoryTerm =
            React.useState (header |> Option.map (fun known -> known.Category))

        let kind, setKind = React.useState DraftText
        let value, setValue = React.useState ""
        let term, setTerm = React.useState (None: ProvenanceTerm option)
        let unit', setUnit = React.useState (None: ProvenanceTerm option)

        let nextHeader =
            match header, categoryTerm with
            | Some known, _ -> Some known
            | None, Some category when not (String.IsNullOrWhiteSpace category.Name) ->
                Some {
                    Kind = propertyKind
                    Category = category
                }
            | _ -> None

        let category =
            nextHeader
            |> Option.map (fun next -> next.Category.Name)
            |> Option.defaultValue "Annotation"

        let nextValue = ValueDrafts.tryValue kind value term
        let canCreate = nextHeader.IsSome && nextValue.IsSome

        Popover.Simple(
            trigger =
                Html.button [
                    prop.type'.button
                    prop.className Styles.addPropertyValueButtonClasses
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--add-20-regular swt:size-5 swt:shrink-0"
                        ]
                        Html.span [
                            prop.text (
                                label
                                |> Option.defaultValue (if header.IsSome then "Add value" else "Add annotation")
                            )
                        ]
                    ]
                ],
            content =
                Html.form [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.onSubmit (fun event ->
                        event.preventDefault ()

                        match nextHeader, nextValue with
                        | Some createdHeader, Some created -> onCreate createdHeader created unit'
                        | _ -> ()
                    )
                    prop.children [
                        if header.IsNone then
                            Html.label [
                                prop.className "swt:label"
                                prop.text "Annotation category"
                            ]

                            TermSearch.TermSearch(
                                categoryTerm |> Option.map TermSearchMapping.toTermSearchTerm,
                                (fun next -> setCategoryTerm (next |> Option.bind TermSearchMapping.fromTermSearchTerm))
                            )
                        Html.label [ prop.className "swt:label"; prop.text "Value type" ]
                        Html.select [
                            prop.ariaLabel "Value type"
                            prop.className "swt:select swt:select-bordered swt:select-sm"
                            prop.value (ValueDrafts.kindName kind)
                            prop.onChange (ValueDrafts.kindFromName >> setKind)
                            prop.children [
                                Html.option [ prop.value "Text"; prop.text "Text" ]
                                Html.option [ prop.value "Integer"; prop.text "Integer" ]
                                Html.option [ prop.value "Float"; prop.text "Float" ]
                                Html.option [ prop.value "Term"; prop.text "Term" ]
                            ]
                        ]
                        Html.label [
                            prop.className "swt:label"
                            prop.text $"{category} value"
                        ]
                        match kind with
                        | DraftTerm ->
                            TermSearch.TermSearch(
                                term |> Option.map TermSearchMapping.toTermSearchTerm,
                                (fun next -> setTerm (next |> Option.bind TermSearchMapping.fromTermSearchTerm))
                            )
                        | _ ->
                            Html.input [
                                prop.ariaLabel $"{category} value"
                                prop.className "swt:input swt:input-bordered swt:input-sm"
                                prop.value value
                                prop.onChange setValue
                            ]
                        Html.label [ prop.className "swt:label"; prop.text "Unit" ]
                        TermSearch.TermSearch(
                            unit' |> Option.map TermSearchMapping.toTermSearchTerm,
                            (fun next -> setUnit (next |> Option.bind TermSearchMapping.fromTermSearchTerm))
                        )
                        if not canCreate then
                            Html.p [
                                prop.className "swt:text-xs swt:text-error"
                                prop.text "Enter a valid value."
                            ]
                        Html.button [
                            prop.type'.submit
                            prop.disabled (not canCreate)
                            prop.className "swt:btn swt:btn-primary swt:btn-sm"
                            prop.text (if header.IsSome then "Add value" else "Add annotation")
                        ]
                    ]
                ],
            ?debug =
                (if defaultArg debug false then
                     Some $"provenance-add-value-{category}"
                 else
                     None)
        )

    [<ReactComponent>]
    static member ValueLabel
        (propertyValue: ProvenancePropertyValue, ?debug: bool, ?key: string, ?sourceInfo: PropertyValueSourceInfo)
        : ReactElement =
        let label =
            $"{propertyValue.Header.Category.Name}: {Formatting.formatValue propertyValue.Value propertyValue.Unit}"

        let sourceTitle =
            match sourceInfo with
            | Some info ->
                let parts = [
                    match info.TableName with
                    | Some tn -> $"Table: {tn}"
                    | None -> ()
                    match info.ProcessName with
                    | Some pn -> $"Process: {pn}"
                    | None -> ()
                    if info.IsCurrentTable then
                        "Current table"
                ]

                if parts.IsEmpty then
                    None
                else
                    Some(System.String.Join("; ", parts))
            | _ -> None

        Html.div [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.className
                "swt:group swt:relative swt:flex swt:items-center swt:gap-1 swt:rounded swt:bg-base-200 swt:px-2 swt:py-1 swt:text-xs"
            match sourceTitle with
            | Some title -> prop.title title
            | None -> ()
            if defaultArg debug false then
                prop.testId $"provenance-value-{propertyValue.Id}"
            prop.children [
                Html.span [ prop.text label ]
                match sourceInfo with
                | Some info ->
                    Html.div [
                        prop.className
                            "swt:pointer-events-none swt:absolute swt:left-0 swt:top-full swt:z-30 swt:mt-1 swt:hidden swt:w-72 swt:rounded-md swt:border swt:border-base-300 swt:bg-base-100 swt:p-2 swt:shadow-lg swt:motion-pop-in group-hover:swt:block group-focus-within:swt:block"
                        prop.children [ Controls.SourceInfoPopover(Some info) ]
                    ]
                | None -> Html.none
            ]
        ]

    [<ReactComponent>]
    static member ValueChip
        (
            propertyValue: ProvenancePropertyValue,
            onDragChanged: bool -> unit,
            ?draggable: bool,
            ?showHeader: bool,
            ?anchorSide: ProvenanceSide,
            ?debug: bool,
            ?key: string,
            ?sourceInfo: PropertyValueSourceInfo,
            ?unassigned: bool,
            ?onApplyToSelection: unit -> unit,
            ?applySelectionLabel: string
        ) : ReactElement =
        let canDrag = defaultArg draggable true
        let showHeader = defaultArg showHeader true
        let unassigned = defaultArg unassigned false
        let density = React.useContext Density.context

        let drag =
            DndKit.useDraggable (
                {|
                    id = DragDrop.valueDragId propertyValue.Id
                |}
            )

        let wasDragging = React.useRef false

        React.useEffect (
            (fun () ->
                if drag.isDragging <> wasDragging.current then
                    wasDragging.current <- drag.isDragging
                    onDragChanged (drag.isDragging)
            ),
            [| box drag.isDragging |]
        )

        let text = Formatting.formatValue propertyValue.Value propertyValue.Unit

        let label =
            if showHeader then
                $"{propertyValue.Header.Category.Name}: {text}"
            else
                text

        let valueAnchor =
            anchorSide
            |> Option.map (fun side ->
                Controls.ConnectionAnchor(
                    {
                        Kind = ConnectionHandleKind.PropertyValue
                        Side = side
                        Id = propertyValue.Id
                        ParentGroupId = None
                    },
                    (match side with
                     | ProvenanceSide.Input -> "swt:top-1/2 swt:right-0 swt:translate-x-1/2 swt:-translate-y-1/2"
                     | ProvenanceSide.Output -> "swt:top-1/2 swt:left-0 swt:-translate-x-1/2 swt:-translate-y-1/2"),
                    ?debug = debug
                )
            )

        let identityProps = [
            match key with
            | Some key -> prop.key key
            | None -> ()
            prop.role.button
            prop.tabIndex 0
        ]

        let dragProps = [
            if canDrag then
                prop.ref drag.setNodeRef
                yield! prop.spread (!!drag.attributes)
                yield! prop.spread (!!drag.listeners)
        ]

        let appearanceProps = [
            prop.className [
                "swt:group swt:relative"
                yield! Styles.propertyValueButtonClasses density drag.isDragging
                if not canDrag then
                    "swt:cursor-default"
                // Values that exist only in the rail palette look tentative until
                // they are dropped onto a group.
                if unassigned then
                    "swt:border-dashed"
            ]
            prop.custom ("data-provenance-resize-node", "true")
        ]

        let debugProps = [
            if unassigned then
                prop.custom ("data-provenance-unassigned", "true")
            if defaultArg debug false then
                prop.testId $"provenance-value-{propertyValue.Id}"
            prop.ariaLabel $"Drag {propertyValue.Header.Category.Name} value"
        ]

        let titleProps = [
            if unassigned then
                prop.title "Not assigned to any entity yet — drag onto a group card to apply."
            else
                match sourceInfo with
                | Some info ->
                    let parts = [
                        match info.TableName with
                        | Some tn -> $"Table: {tn}"
                        | None -> ()
                        match info.ProcessName with
                        | Some pn -> $"Process: {pn}"
                        | None -> ()
                        if info.IsCurrentTable then
                            "Current table"
                    ]

                    if not parts.IsEmpty then
                        prop.title (System.String.Join("; ", parts))
                | _ -> ()
        ]

        let content =
            prop.children [
                match valueAnchor with
                | Some anchor -> anchor
                | None -> Html.none
                Html.span [
                    prop.className "swt:grow swt:min-w-0 swt:truncate swt:text-left"
                    prop.text label
                ]
                // Click alternative to dragging: applies this value to the groups
                // currently selected on the surface.
                match onApplyToSelection with
                | Some apply ->
                    let applyLabel = defaultArg applySelectionLabel "Apply to selected groups"

                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:btn-square swt:z-10 swt:shrink-0"
                        prop.title applyLabel
                        prop.ariaLabel applyLabel
                        if defaultArg debug false then
                            prop.testId $"provenance-value-apply-{propertyValue.Id}"
                        // The chip root carries the drag listeners; the button keeps its
                        // pointer events to itself so a click never starts a drag.
                        prop.onPointerDown (fun event -> event.stopPropagation ())
                        prop.onClick (fun event ->
                            event.stopPropagation ()
                            apply ()
                        )
                        prop.children [
                            Html.i [
                                prop.className "swt:iconify swt:fluent--checkmark-circle-20-regular swt:size-4"
                            ]
                        ]
                    ]
                | None -> Html.none
                match sourceInfo with
                | Some info ->
                    Html.div [
                        prop.className
                            "swt:pointer-events-none swt:absolute swt:left-0 swt:top-full swt:z-30 swt:mt-1 swt:hidden swt:w-72 swt:rounded-md swt:border swt:border-base-300 swt:bg-base-100 swt:p-2 swt:shadow-lg swt:motion-pop-in group-hover:swt:block group-focus-within:swt:block"
                        prop.children [ Controls.SourceInfoPopover(Some info) ]
                    ]
                | None -> Html.none
            ]

        Html.div (
            List.concat [
                identityProps
                dragProps
                appearanceProps
                debugProps
                titleProps
                [ content ]
            ]
        )

    [<ReactComponent>]
    static member ValueDragPreview
        (propertyValue: ProvenancePropertyValue, ?showHeader: bool, ?debug: bool)
        : ReactElement =
        let showHeader = defaultArg showHeader true
        let text = Formatting.formatValue propertyValue.Value propertyValue.Unit

        let label =
            if showHeader then
                $"{propertyValue.Header.Category.Name}: {text}"
            else
                text

        Html.div [
            prop.className Styles.propertyValueOverlayClasses
            if defaultArg debug false then
                prop.testId "provenance-drag-overlay-value"
            prop.children [
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate"
                    prop.text label
                ]
            ]
        ]

    [<ReactComponent>]
    static member AddEndpointPopover
        (
            side: ProvenanceSide,
            endpointKinds: ProvenanceKind list,
            existingEndpointNames: string list,
            onCreate: CreateLoadedSetCommand -> unit,
            ?debug: bool,
            ?key: string
        ) =
        let sideName = SideLabels.sideName side
        let isOpen, setIsOpen = React.useState false
        let name, setName = React.useState ""

        let availableKinds =
            if endpointKinds.IsEmpty then
                [ Endpoints.fallbackKind ]
            else
                endpointKinds

        let selectedKindId, setSelectedKindId = React.useState (availableKinds.Head.Id)

        let selectedKind =
            availableKinds
            |> List.tryFind (fun kind -> kind.Id = selectedKindId)
            |> Option.defaultValue availableKinds.Head

        let trimmedName = name.Trim()

        let duplicateName =
            existingEndpointNames
            |> List.exists (fun existing ->
                String.Equals(existing.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)
            )

        let nameError =
            if String.IsNullOrWhiteSpace trimmedName then
                Some "Enter an endpoint name."
            elif duplicateName then
                Some "This endpoint already exists in the current layer."
            else
                None

        let content =
            Html.form [
                prop.className "swt:flex swt:flex-col swt:gap-2"
                prop.onSubmit (fun event ->
                    event.preventDefault ()

                    onCreate {
                        Side = side
                        Header = selectedKind |> Endpoints.endpointHeader side
                        Name = trimmedName
                    }

                    setName ""
                    setSelectedKindId availableKinds.Head.Id
                    setIsOpen false
                )
                prop.children [
                    Html.label [ prop.className "swt:label"; prop.text "Endpoint name" ]
                    Html.input [
                        prop.ariaLabel "Endpoint name"
                        prop.className [
                            "swt:input swt:input-bordered swt:input-sm"
                            if nameError.IsSome then
                                "swt:input-error"
                        ]
                        prop.required true
                        prop.value name
                        prop.onChange setName
                    ]
                    match nameError with
                    | Some error ->
                        Html.p [
                            prop.className "swt:text-xs swt:text-error"
                            prop.text error
                        ]
                    | None -> Html.none
                    Html.label [ prop.className "swt:label"; prop.text "Endpoint kind" ]
                    Html.select [
                        prop.ariaLabel "Endpoint kind"
                        prop.className "swt:select swt:select-bordered swt:select-sm"
                        prop.value selectedKind.Id
                        prop.onChange setSelectedKindId
                        prop.children [
                            for kind in availableKinds do
                                Html.option [
                                    prop.value kind.Id
                                    prop.text (ProvenanceKind.displayName kind)
                                ]
                        ]
                    ]
                    Html.button [
                        prop.type'.submit
                        prop.className "swt:btn swt:btn-primary swt:btn-sm"
                        prop.disabled nameError.IsSome
                        if defaultArg debug false then
                            prop.testId $"provenance-create-{sideName}"
                        prop.text "Create endpoint"
                    ]
                ]
            ]

        Popover.Popover(
            isOpen = isOpen,
            onOpenChange = setIsOpen,
            ?debug =
                (if defaultArg debug false then
                     Some $"provenance-add-{sideName}"
                 else
                     None),
            children =
                React.Fragment [
                    Popover.Trigger(
                        Html.button [
                            prop.type'.button
                            prop.className Styles.addPropertyValueButtonClasses
                            if defaultArg debug false then
                                prop.testId $"provenance-add-{sideName}-trigger"
                            prop.ariaLabel $"Add {sideName}"
                            prop.children [
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--add-20-regular swt:size-5 swt:shrink-0"
                                ]
                                Html.span [ prop.text $"Add {sideName}" ]
                            ]
                        ]
                    )
                    Popover.Content(
                        children =
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.div [
                                        prop.className "swt:flex swt:items-start swt:justify-between swt:gap-2"
                                        prop.children [
                                            Html.div [ prop.className "swt:flex-1"; prop.children content ]
                                            Popover.Close()
                                        ]
                                    ]
                                ]
                            ]
                    )
                ]
        )

    /// The overall search over properties, values and groups. Debounced, so
    /// typing updates the input immediately but filters only settle afterwards.
    [<ReactComponent>]
    static member SearchBar(searchText: string, onSearch: string -> unit, ?debug: bool) =
        let searchDraft, setSearchDraft = React.useState searchText
        let latestSearchText = React.useRef searchText
        let latestOnSearch = React.useRef onSearch

        // useLayoutEffect (not a render-phase write) so a discarded render
        // under concurrent rendering / StrictMode never leaves these pointing
        // at a searchText/onSearch value that was never actually committed.
        React.useLayoutEffect (fun () ->
            latestSearchText.current <- searchText
            latestOnSearch.current <- onSearch
        )

        let debouncedSearchDraft = React.useDebounce (searchDraft, 300)

        React.useEffect ((fun () -> setSearchDraft searchText), [| box searchText |])

        React.useEffect (
            (fun () ->
                if debouncedSearchDraft <> latestSearchText.current then
                    latestOnSearch.current debouncedSearchDraft
            ),
            [| box debouncedSearchDraft |]
        )

        Html.div [
            prop.className "swt:relative swt:flex swt:w-40 swt:shrink-0 swt:items-center"
            // Always-on anchor for the interactive tutorial's spotlight.
            prop.custom ("data-tutorial", "provenance-search")
            if defaultArg debug false then
                prop.testId "provenance-search"
            prop.children [
                Html.i [
                    prop.className
                        "swt:iconify swt:fluent--search-20-regular swt:absolute swt:left-2 swt:size-4 swt:text-base-content/50"
                ]
                Html.input [
                    prop.className "swt:input swt:input-bordered swt:input-sm swt:w-full swt:pl-8"
                    prop.placeholder "Search annotations & values..."
                    prop.ariaLabel "Search annotations & values"
                    prop.value searchDraft
                    prop.onChange setSearchDraft
                ]
            ]
        ]

    [<ReactComponent>]
    static member FilterToolbar
        (
            filters: FilterState,
            onSearch: string -> unit,
            onPropertySort: PropertySort -> unit,
            onGroupSort: GroupSort -> unit,
            onValueCountFilter: PropertyValueCountFilter -> unit,
            onOriginFilter: PropertyOriginFilter -> unit,
            ?debug: bool
        ) =
        let sortOpen, setSortOpen = React.useState false
        let groupSortOpen, setGroupSortOpen = React.useState false

        let propertySortOption sort label =
            let active = filters.PropertySort = sort

            Html.li [
                Html.button [
                    prop.type'.button
                    prop.className [
                        "swt:btn swt:btn-sm swt:w-full swt:justify-start"
                        if active then "swt:btn-primary" else "swt:btn-ghost"
                    ]
                    prop.ariaLabel label
                    prop.onClick (fun _ -> onPropertySort sort)
                    prop.children [ Html.span label ]
                ]
            ]

        let groupSortOption sort label =
            let active = filters.GroupSort = sort

            Html.li [
                Html.button [
                    prop.type'.button
                    prop.className [
                        "swt:btn swt:btn-sm swt:w-full swt:justify-start"
                        if active then "swt:btn-primary" else "swt:btn-ghost"
                    ]
                    prop.ariaLabel label
                    prop.onClick (fun _ -> onGroupSort sort)
                    prop.children [ Html.span label ]
                ]
            ]

        let originActive filter =
            match filter, filters.OriginFilter with
            | PropertyOriginFilter.AnyOrigin, PropertyOriginFilter.AnyOrigin -> true
            | PropertyOriginFilter.CurrentOnly, PropertyOriginFilter.CurrentOnly -> true
            | PropertyOriginFilter.AnyUpstream, PropertyOriginFilter.AnyUpstream -> true
            | _ -> false

        let originButton filter label icon =
            Html.button [
                prop.type'.button
                prop.className [
                    "swt:btn swt:btn-sm swt:join-item swt:w-11 swt:px-0"
                    if originActive filter then
                        "swt:btn-primary"
                    else
                        "swt:btn-outline"
                ]
                prop.ariaLabel label
                prop.title label
                prop.onClick (fun _ -> onOriginFilter filter)
                prop.children [ icon ]
            ]

        Html.div [
            prop.className "swt:flex swt:min-w-0 swt:shrink swt:flex-wrap swt:items-center swt:gap-2"
            // Always-on anchor for the interactive tutorial's spotlight.
            prop.custom ("data-tutorial", "provenance-filter-toolbar")
            if defaultArg debug false then
                prop.testId "provenance-filter-toolbar"
            prop.children [
                Controls.SearchBar(filters.SearchText, onSearch, ?debug = debug)
                Dropdown.Main(
                    sortOpen,
                    setSortOpen,
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-sm swt:btn-outline"
                        prop.ariaLabel "Sort By"
                        prop.custom ("aria-expanded", sortOpen)
                        prop.onClick (fun _ -> setSortOpen (not sortOpen))
                        prop.children [
                            Html.i [
                                prop.className "swt:iconify swt:fluent--arrow-sort-20-regular swt:size-4"
                            ]
                            Html.span "Sort By"
                        ]
                    ],
                    React.Fragment [
                        propertySortOption PropertySort.ValueCountDesc "Annotation Value Count"
                        propertySortOption PropertySort.NameAsc "Name"
                        propertySortOption PropertySort.ConnectionCountDesc "Connection Count"
                    ],
                    contentClassName =
                        "swt:w-52 swt:max-w-none swt:menu swt:bg-base-200 swt:rounded-box swt:z-99 swt:p-2 swt:shadow-sm swt:top-110%"
                )
                Dropdown.Main(
                    groupSortOpen,
                    setGroupSortOpen,
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-sm swt:btn-outline"
                        prop.ariaLabel "Sort Groups"
                        prop.custom ("aria-expanded", groupSortOpen)
                        if defaultArg debug false then
                            prop.testId "provenance-group-sort"
                        prop.onClick (fun _ -> setGroupSortOpen (not groupSortOpen))
                        prop.children [
                            Html.i [
                                prop.className "swt:iconify swt:fluent--arrow-sort-20-regular swt:size-4"
                            ]
                            Html.span "Sort Groups"
                        ]
                    ],
                    React.Fragment [
                        groupSortOption GroupSort.NameAsc "Name"
                        groupSortOption GroupSort.MemberCountDesc "Member Count"
                        groupSortOption GroupSort.ConnectionCountDesc "Connection Count"
                    ],
                    contentClassName =
                        "swt:w-52 swt:max-w-none swt:menu swt:bg-base-200 swt:rounded-box swt:z-99 swt:p-2 swt:shadow-sm swt:top-110%"
                )
                Html.select [
                    prop.className "swt:select swt:select-bordered swt:select-sm swt:w-20 swt:shrink-0"
                    prop.ariaLabel "Filter by annotation value count"
                    prop.value (
                        match filters.ValueCountFilter with
                        | PropertyValueCountFilter.Any -> "Any"
                        | PropertyValueCountFilter.Singleton -> "Singleton"
                        | PropertyValueCountFilter.Multiple -> "Multiple"
                        | PropertyValueCountFilter.CoverageGap -> "CoverageGap"
                    )
                    prop.onChange (fun v ->
                        match v with
                        | "Any" -> onValueCountFilter PropertyValueCountFilter.Any
                        | "Singleton" -> onValueCountFilter PropertyValueCountFilter.Singleton
                        | "Multiple" -> onValueCountFilter PropertyValueCountFilter.Multiple
                        | "CoverageGap" -> onValueCountFilter PropertyValueCountFilter.CoverageGap
                        | _ -> ()
                    )
                    prop.children [
                        Html.option [ prop.value "Any"; prop.text "Any" ]
                        Html.option [ prop.value "Singleton"; prop.text "1 value" ]
                        Html.option [ prop.value "Multiple"; prop.text "2+ values" ]
                        Html.option [ prop.value "CoverageGap"; prop.text "Coverage gap" ]
                    ]
                ]
                Html.div [
                    prop.className "swt:join"
                    prop.children [
                        originButton
                            PropertyOriginFilter.AnyUpstream
                            "Show upstream annotations"
                            (OriginSymbols.upstreamIcon "swt:size-4")
                        originButton
                            PropertyOriginFilter.CurrentOnly
                            "Show current annotations"
                            (OriginSymbols.currentIcon "swt:size-4")
                        originButton
                            PropertyOriginFilter.AnyOrigin
                            "Show current and upstream annotations"
                            (OriginSymbols.bothIcons "swt:size-4")
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member PropertyColorButton
        (
            header: ProvenancePropertyHeader,
            currentColor: ProvenanceColor option,
            onSetColor: ProvenanceColor option -> unit
        ) =
        let draftColor, setDraftColor =
            React.useState (ColorPicker.currentOrFallback currentColor)

        React.useEffect ((fun () -> setDraftColor (ColorPicker.currentOrFallback currentColor)), [| box currentColor |])

        Popover.Simple(
            trigger =
                Html.button [
                    prop.type'.button
                    prop.className
                        "swt:btn swt:btn-xs swt:btn-square swt:z-10 swt:btn-outline swt:shrink-0 swt:bg-base-100/90"
                    match currentColor with
                    | Some c when c <> "" -> prop.style [ style.backgroundColor c ]
                    | _ -> ()
                    prop.ariaLabel $"Set color for annotation {header.Category.Name}"
                ],
            content =
                ColorPicker.content
                    $"Choose color for annotation {header.Category.Name}"
                    draftColor
                    setDraftColor
                    onSetColor,
            triggerClassName = "swt:relative swt:z-10 swt:shrink-0"
        )

    [<ReactComponent>]
    static member SourceInfoPopover(sourceInfo: PropertyValueSourceInfo option) =
        match sourceInfo with
        | None -> Html.none
        | Some info ->
            Html.div [
                prop.className "swt:text-xs swt:space-y-1 swt:p-1"
                prop.children [
                    if info.IsCurrentTable then
                        Html.span [
                            prop.className "swt:font-semibold"
                            prop.text "Current table"
                        ]
                    else
                        match info.TableName with
                        | Some tableName ->
                            Html.span [
                                prop.className "swt:font-semibold"
                                prop.text $"Table: {tableName}"
                            ]
                        | None -> Html.none

                    match info.ProcessName with
                    | Some processName -> Html.span [ prop.text $"Process: {processName}" ]
                    | None -> Html.none

                    if not info.InputNames.IsEmpty then
                        Html.div [
                            prop.children [
                                Html.span [ prop.className "swt:font-medium"; prop.text "Inputs:" ]
                                for inputName in info.InputNames do
                                    Html.span [ prop.text $" {inputName}" ]
                            ]
                        ]
                    else
                        Html.none

                    if not info.OutputNames.IsEmpty then
                        Html.div [
                            prop.children [
                                Html.span [ prop.className "swt:font-medium"; prop.text "Outputs:" ]
                                for outputName in info.OutputNames do
                                    Html.span [ prop.text $" {outputName}" ]
                            ]
                        ]
                    else
                        Html.none
                ]
            ]
