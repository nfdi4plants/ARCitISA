namespace Arcitisa.Components.Page.ProvenanceGrouping

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Browser.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Primitive.ContextMenu
open Arcitisa.Components.Primitive.ContextMenu.Types
open Arcitisa.Components.Page.ProvenanceGrouping.Types

module ConnectorSvg =

    let debugAttributes debug (measured: MeasuredConnector) = [
        if debug then
            svg.custom ("data-testid", measured.TestId)
            svg.custom ("data-provenance-connection-key", measured.Key)
    ]

    /// Paths are also written as the CSS `d` property so discrete layout changes can
    /// morph the curve instead of snapping; continuous tracking (scroll, drags) keeps
    /// `animate = false` and snaps per frame. Browsers without CSS `d` fall back to
    /// the attribute and simply skip the morph.
    let pathStyle (path: string) (animate: bool) =
        createObj [
            "d" ==> $"path('{path}')"
            "transition"
            ==> (if animate then
                     "d 160ms ease, stroke-width 120ms ease, stroke-opacity 120ms ease, fill-opacity 120ms ease"
                 else
                     "stroke-width 120ms ease, stroke-opacity 120ms ease, fill-opacity 120ms ease")
        ]

    /// Fill opacity for sankey ribbons, derived from the caller's stroke
    /// opacity scale (1.0 emphasized / 0.85 default / 0.3 receded). Ribbons
    /// stay translucent on purpose: overlapping bands compound and read as a
    /// denser flow instead of hiding each other.
    let private ribbonFillOpacity strokeOpacity =
        if strokeOpacity >= 1.0 then 0.7
        elif strokeOpacity <= 0.3 then 0.12
        else 0.4

    /// Sankey band: one translucent fill, no halo - crossings are meant to
    /// stack visually. A draw-in dash animation cannot work on a fill, so new
    /// ribbons fade in.
    let private ribbonElements
        (measured: MeasuredConnector)
        (ribbonPath: string)
        fillColor
        fillOpacity
        animate
        isNew
        debug
        =
        [
            Svg.path [
                svg.d ribbonPath
                svg.custom ("style", pathStyle ribbonPath animate)
                svg.fill fillColor
                svg.custom ("fillOpacity", fillOpacity)
                svg.stroke "none"
                svg.className (measured.ClassName + (if isNew then " swt:connector-fade-in" else ""))
                match measured.Color with
                | Some color -> svg.custom ("data-provenance-color", color)
                | None -> ()
                if measured.InteractiveConnection.IsNone then
                    yield! debugAttributes debug measured
            ]
        ]

    let private lineElements (measured: MeasuredConnector) strokeColor strokeWidth strokeOpacity animate isNew debug =
        // Freshly created solid connectors draw themselves in along the curve; new
        // dashed rail connectors fade in instead, because a dash offset animation
        // would fight their dash pattern.
        let isSolid = measured.StrokeDasharray.IsNone

        let entranceClass =
            if isNew then
                if isSolid then
                    " swt:connector-draw-in"
                else
                    " swt:connector-fade-in"
            else
                ""

        let entranceAttributes = [
            if isNew && isSolid then
                // Normalizes the path length so the draw-in dash math needs no
                // getTotalLength measurement.
                svg.custom ("pathLength", 1)
        ]

        [
            // A surface-colored halo keeps crossing connectors readable.
            Svg.path [
                svg.d measured.Path
                svg.custom ("style", pathStyle measured.Path animate)
                svg.fill "none"
                svg.stroke "currentColor"
                svg.strokeWidth (strokeWidth + 2.5)
                svg.strokeLineCap "round"
                svg.className ("swt:text-base-200" + entranceClass)
                yield! entranceAttributes
                match measured.StrokeDasharray with
                | Some dash -> svg.custom ("strokeDasharray", dash)
                | None -> ()
            ]
            Svg.path [
                svg.d measured.Path
                svg.custom ("style", pathStyle measured.Path animate)
                svg.fill "none"
                svg.stroke strokeColor
                svg.strokeWidth strokeWidth
                svg.strokeLineCap "round"
                svg.custom ("strokeOpacity", strokeOpacity)
                svg.className (measured.ClassName + entranceClass)
                yield! entranceAttributes
                match measured.StrokeDasharray with
                | Some dash -> svg.custom ("strokeDasharray", dash)
                | None -> ()
                match measured.Color with
                | Some color -> svg.custom ("data-provenance-color", color)
                | None -> ()
                if measured.InteractiveConnection.IsNone then
                    yield! debugAttributes debug measured
            ]
        ]

    let strokeElements (measured: MeasuredConnector) strokeWidth strokeOpacity animate isNew debug =
        let paintColor = measured.Color |> Option.defaultValue "currentColor"

        match measured.RibbonPath with
        | Some ribbonPath ->
            ribbonElements measured ribbonPath paintColor (ribbonFillOpacity strokeOpacity) animate isNew debug
        | None -> lineElements measured paintColor strokeWidth strokeOpacity animate isNew debug

module ConnectorContextMenu =

    let connectionKeyAttribute = "data-provenance-interactive-connection-key"

    let private tryTargetElement (event: Browser.Types.Event) : Browser.Types.Element option =
        let targetObj: obj = box event.target

        if isNullOrUndefined targetObj || isNullOrUndefined targetObj?closest then
            None
        else
            Some(unbox<Browser.Types.Element> targetObj)

    let private interactiveConnectionKey (event: Browser.Types.MouseEvent) =
        event
        |> tryTargetElement
        |> Option.bind (fun target ->
            let node: Browser.Types.Element = !!target?closest($"[{connectionKeyAttribute}]")

            if isNull node then
                None
            else
                Some(node.getAttribute connectionKeyAttribute)
        )
        |> Option.bind (fun key -> if isNull key then None else Some key)

    let spawnData (paths: MeasuredConnector list) event =
        interactiveConnectionKey event
        |> Option.bind (fun key ->
            paths
            |> List.tryFind (fun path -> path.Key = key)
            |> Option.bind (fun path -> path.InteractiveConnection)
        )
        |> Option.map box

    let items remove (data: obj) =
        let connection = data |> unbox<DisplayConnection>

        [
            ContextMenuItem(
                text = Html.span "Delete",
                icon =
                    Html.i [
                        prop.className "swt:iconify swt:fluent--delete-20-regular swt:size-4"
                    ],
                onClick =
                    (fun event ->
                        event.buttonEvent.stopPropagation ()
                        remove connection
                    )
            )
        ]

/// Thin ResizeObserver bindings used to remeasure connector paths after layout changes.
module ConnectorObserver =

    [<Emit("new ResizeObserver(() => $0())")>]
    let create (callback: unit -> unit) : obj = jsNative

    [<Emit("$0.observe($1)")>]
    let observeNode (observer: obj) (target: HTMLElement) : unit = jsNative

    [<Emit("$0.querySelectorAll($1).forEach(node => $2.observe(node))")>]
    let observeMatching (container: HTMLElement) (selector: string) (observer: obj) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let disconnect (observer: obj) : unit = jsNative

module ConnectorMutationObserver =

    [<Emit("new MutationObserver(() => $0())")>]
    let create (callback: unit -> unit) : obj = jsNative

    [<Emit("$0.observe($1, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'style'] })")>]
    let observe (observer: obj) (target: HTMLElement) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let disconnect (observer: obj) : unit = jsNative

module AnimationFrame =

    [<Emit("requestAnimationFrame($0)")>]
    let request (_callback: unit -> unit) : float = jsNative

    [<Emit("cancelAnimationFrame($0)")>]
    let cancel (_handle: float) : unit = jsNative
