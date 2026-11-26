namespace Arcitisa.Components.Page.ProvenanceGrouping

open System
open System.Globalization
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Composite.FolderedDraggableList
open Arcitisa.Components.Composite.FolderedDraggableList.Types
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

/// Resizable three-panel surface helpers.
module Splitter =

    type SplitterSide =
        | Left
        | Right

    let template (ratios: PanelRatios) =
        let left = ratios.Left.ToString(CultureInfo.InvariantCulture)
        let middle = ratios.Middle.ToString(CultureInfo.InvariantCulture)
        let right = ratios.Right.ToString(CultureInfo.InvariantCulture)
        // The splitter tracks double as connector gutters; their generous fixed width
        // keeps readable space between the rails and the cards their connectors attach to.
        $"minmax(10rem, {left}fr) 4rem minmax(28rem, {middle}fr) 4rem minmax(10rem, {right}fr)"

    /// Writes the grid template directly to the surface element for live drag
    /// preview frames; the ratios are committed to state when the drag ends.
    [<Emit("$0.style.setProperty('grid-template-columns', $1)")>]
    let applyTemplate (_surface: Browser.Types.HTMLElement) (_template: string) : unit = jsNative

    let testId side =
        match side with
        | Left -> "provenance-left-splitter"
        | Right -> "provenance-right-splitter"

    /// Keyboard resize step in whole percent of the surface width.
    let keyboardStep = 2

    let handle
        side
        (isActive: bool)
        (valueNow: int)
        (onPointerDown: Browser.Types.PointerEvent -> unit)
        (onNudge: int -> unit)
        (onReset: unit -> unit)
        debug
        =
        Html.div [
            prop.className [
                "swt:group swt:flex swt:min-h-full swt:cursor-col-resize swt:items-stretch swt:justify-center swt:rounded swt:outline-none hover:swt:bg-base-300/60 focus-visible:swt:bg-base-300/60"
                if isActive then
                    "swt:bg-base-300/60"
            ]
            prop.onPointerDown onPointerDown
            prop.onDoubleClick (fun _ -> onReset ())
            prop.onKeyDown (fun event ->
                match event.key with
                | "ArrowLeft" ->
                    event.preventDefault ()
                    onNudge (-keyboardStep)
                | "ArrowRight" ->
                    event.preventDefault ()
                    onNudge keyboardStep
                | "Enter" ->
                    event.preventDefault ()
                    onReset ()
                | _ -> ()
            )
            prop.style [ style.custom ("touchAction", "none") ]
            prop.tabIndex 0
            prop.custom ("role", "separator")
            prop.custom ("aria-orientation", "vertical")
            prop.custom ("aria-valuenow", valueNow)
            prop.custom ("aria-valuemin", 0)
            prop.custom ("aria-valuemax", 100)
            prop.ariaLabel "Resize provenance panels"
            prop.title "Drag or use arrow keys to resize; double-click to reset"
            if debug then
                prop.testId (testId side)
            prop.children [
                Html.div [
                    prop.className [
                        "swt:my-2 swt:w-1 swt:rounded-full swt:transition-colors group-hover:swt:bg-base-content/35"
                        if isActive then
                            "swt:bg-primary"
                        else
                            "swt:bg-base-content/15 group-focus-visible:swt:bg-primary/60"
                    ]
                ]
            ]
        ]

/// Width tiers of the editor root that drive the responsive layout variants.
[<RequireQualifiedAccess>]
type LayoutTier =
    | Wide
    | Medium
    | Narrow

module LayoutTier =

    let forWidth (width: float) =
        if width < 640. then LayoutTier.Narrow
        elif width < 1024. then LayoutTier.Medium
        else LayoutTier.Wide

/// Thin ResizeObserver binding used to derive the layout tier from the editor width.
module TierObserver =

    [<Emit("new ResizeObserver(() => $0())")>]
    let create (callback: unit -> unit) : obj = jsNative

    [<Emit("$0.observe($1)")>]
    let observe (observer: obj) (target: Browser.Types.HTMLElement) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let disconnect (observer: obj) : unit = jsNative
