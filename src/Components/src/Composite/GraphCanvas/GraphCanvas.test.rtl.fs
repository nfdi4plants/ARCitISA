module internal Swate.Components.Composite.GraphCanvas.GraphCanvasTests

// Ported from GraphCanvas.test.tsx of arc-portal-ui. Tests use the real ELK through the default layout, or
// the layout seam of the component where a test controls when a layout resolves or fails.
//
// Every behavior case of the PORTAL file is ported: node buttons, the name of the region, click and Enter on
// a node, nodes without button semantics, the labels of nodes and edges, the toggle and its expanded state,
// inline expansion and collapse, collapse on a new resetKey, focus on mount and after a new view, the alert
// and its focus, scroll reset, stale results, both centering cases, the pill list (open, Escape, Tab,
// Shift+Tab, click outside, second click, one list at a time, placement below and above, fit to a new
// height, closed when the box grows), relayout only while an edge is expanded, an expanded edge that does
// not fit, the portrait box cases and zoom.
//
// Changed on purpose: assertions on copy are dropped. These are the accessible names of toggles and lists,
// the text of the status line, the toggle labels and the zoom percentage. The class name check for the
// compact canvas became the data-compact attribute. PORTAL stubs ResizeObserver with vi.stubGlobal, these
// tests install a stub through Emit and remove it after each test.
//
// New: the navigation epoch (A, B, A before B resolves), the list that stays closed when the box returns to
// the width it was opened on, Space on a node, zoom bounds, zoom that survives a new view, and a new layout
// function identity that starts no layout.

open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components.JsBindings
open Swate.Components.Composite.GraphCanvas
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Composite.GraphCanvas.Layout

type private LayoutFn =
    CanvasNode[] -> CanvasEdge[] -> string[] -> MeasureText -> LayoutOptions -> JS.Promise<GraphLayout>

// jsdom has no ResizeObserver, no layout and no scroll geometry. These helpers install what a test needs on
// the global objects and take it away again after the test.

[<Emit("""(() => {
    globalThis.__graphResize = [];
    globalThis.ResizeObserver = class {
        constructor(callback) { this.callback = callback; this.connected = false; globalThis.__graphResize.push(this) }
        observe() { this.connected = true }
        unobserve() {}
        disconnect() { this.connected = false }
    }
})()""")>]
let private installResizeObserver () : unit = jsNative

[<Emit("(delete globalThis.ResizeObserver, delete globalThis.__graphResize)")>]
let private removeResizeObserver () : unit = jsNative

[<Emit("globalThis.__graphResize.filter(observer => observer.connected).forEach(observer => observer.callback())")>]
let private notifyResize () : unit = jsNative

[<Emit("""(() => {
    const store = (globalThis.__graphBox = { width: $0, height: $1, borderWidth: $2, borderHeight: $3, scrollWidth: 0, scrollHeight: 0, restore: [] });
    const define = (proto, name, read) => {
        const original = Object.getOwnPropertyDescriptor(proto, name);
        store.restore.push(() => (original ? Object.defineProperty(proto, name, original) : delete proto[name]));
        Object.defineProperty(proto, name, { configurable: true, get: read });
    };
    define(Element.prototype, 'clientWidth', () => store.width);
    define(Element.prototype, 'clientHeight', () => store.height);
    define(Element.prototype, 'scrollWidth', () => store.scrollWidth);
    define(Element.prototype, 'scrollHeight', () => store.scrollHeight);
    define(HTMLElement.prototype, 'offsetWidth', () => store.borderWidth);
    define(HTMLElement.prototype, 'offsetHeight', () => store.borderHeight);
})()""")>]
let private stubBox (width: float) (height: float) (borderWidth: float) (borderHeight: float) : unit = jsNative

[<Emit("Object.assign(globalThis.__graphBox, { width: $0, height: $1, borderWidth: $2, borderHeight: $3 })")>]
let private setBox (width: float) (height: float) (borderWidth: float) (borderHeight: float) : unit = jsNative

[<Emit("Object.assign(globalThis.__graphBox, { scrollWidth: $0, scrollHeight: $1 })")>]
let private setScrollSize (width: float) (height: float) : unit = jsNative

[<Emit("""(() => {
    const original = Element.prototype.getBoundingClientRect;
    const rect = (top, bottom, left, right) => ({ top, bottom, left, right, x: left, y: top, width: right - left, height: bottom - top, toJSON() {} });
    Element.prototype.getBoundingClientRect = function () {
        const id = this.getAttribute('data-testid') ?? '';
        if (id === 'graph-canvas') return rect(0, globalThis.__graphBox.height, 0, 500);
        if (id.startsWith('graph-edge-toggle-')) return rect($0, $0 + 18, 100, 160);
        if (id === 'graph-canvas-content') return rect(0, 300, 0, 500);
        return rect(0, 0, 0, 0);
    };
    globalThis.__graphRestoreRect = () => { Element.prototype.getBoundingClientRect = original };
})()""")>]
let private stubRects (anchorTop: float) : unit = jsNative

[<Emit("(globalThis.__graphRestoreRect?.(), delete globalThis.__graphRestoreRect)")>]
let private restoreRects () : unit = jsNative

[<Emit("(globalThis.__graphBox?.restore.forEach(restore => restore()), delete globalThis.__graphBox)")>]
let private restoreBox () : unit = jsNative

[<Emit("""(() => {
    globalThis.__graphErrors = 0;
    globalThis.__graphOriginalError = console.error;
    console.error = () => { globalThis.__graphErrors += 1 };
})()""")>]
let private silenceConsoleError () : unit = jsNative

[<Emit("(globalThis.__graphOriginalError && (console.error = globalThis.__graphOriginalError), delete globalThis.__graphOriginalError)")>]
let private restoreConsoleError () : unit = jsNative

[<Emit("globalThis.__graphErrors")>]
let private loggedErrors () : int = jsNative

[<Emit("$0.style.getPropertyValue($1)")>]
let private styleOf (el: HTMLElement) (name: string) : string = jsNative

[<Emit("$0.scrollLeft = $1")>]
let private setScrollLeft (el: HTMLElement) (value: float) : unit = jsNative

[<Emit("$0.scrollTop = $1")>]
let private setScrollTop (el: HTMLElement) (value: float) : unit = jsNative

[<Emit("$0.scrollLeft")>]
let private scrollLeftOf (el: HTMLElement) : float = jsNative

[<Emit("$0.scrollTop")>]
let private scrollTopOf (el: HTMLElement) : float = jsNative

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    removeResizeObserver ()
    restoreRects ()
    restoreBox ()
    restoreConsoleError ()
)

// Test data

let private property (key: string) (name: string) (value: string) (category: PillCategory) : CanvasPill = {
    Key = key
    Kind = PillKind.Property
    Name = Some name
    Value = Some value
    Unit = None
    Category = Some category
    Tooltip = None
}

let private count (key: string) (value: string) (tooltip: string option) : CanvasPill = {
    Key = key
    Kind = PillKind.Count
    Name = None
    Value = Some value
    Unit = None
    Category = None
    Tooltip = tooltip
}

let private node (id: string) (title: string) (color: CanvasColor) (ariaLabel: string) : CanvasNode = {
    Id = id
    Title = title
    Subtitle = None
    Color = color
    Dashed = false
    Mono = false
    Selected = false
    Emphasized = false
    Pills = [||]
    Tooltip = None
    AriaLabel = ariaLabel
    Clickable = true
}

let private edge
    (id: string)
    (source: string)
    (target: string)
    (pills: CanvasPill[])
    (morePills: CanvasPill[])
    : CanvasEdge =
    {
        Id = id
        From = source
        To = target
        Color = CanvasColor.Study
        Highlighted = false
        Pills = pills
        MorePills = morePills
        Tooltip = None
    }

let private nodes: CanvasNode[] = [|
    node "a" "Alpha" CanvasColor.Study "Alpha, select to open"
    node "b" "Beta" CanvasColor.Assay "Beta, select to open"
    {
        node "c" "Gamma" CanvasColor.Neutral "Gamma, fixed" with
            Clickable = false
    }
|]

let private edges: CanvasEdge[] = [|
    edge "ab" "a" "b" [|
        property "p1" "temperature" "20 C" PillCategory.Parameter
    |] [|
        property "p2" "duration" "2 h" PillCategory.Parameter
        property "p3" "volume" "5 ml" PillCategory.Parameter
    |]
    {
        edge "bc" "b" "c" [| count "count" "3" None |] [||] with
            Color = CanvasColor.Neutral
    }
|]

// Two edges that both hide a pill, for the case of one list at a time.
let private edgesWithMore: CanvasEdge[] =
    edges
    |> Array.map (fun e -> {
        e with
            MorePills = [| count $"{e.Id}-more" "9" None |]
    })

let private plainNode (id: string) : CanvasNode = node id id CanvasColor.Study id

type private Props = {
    Nodes: CanvasNode[]
    Edges: CanvasEdge[]
    ResetKey: string
    OnNodeClick: (string -> unit) option
    CenterNodeId: string option
    FocusOnMount: bool option
    ImageFileName: string option
    LayoutSeam: LayoutFn option
}

let private defaults: Props = {
    Nodes = nodes
    Edges = edges
    ResetKey = "one"
    OnNodeClick = Some ignore
    CenterNodeId = None
    FocusOnMount = None
    ImageFileName = None
    LayoutSeam = None
}

let private canvas (props: Props) : ReactElement =
    GraphCanvas.GraphCanvas(
        props.Nodes,
        props.Edges,
        "Test graph",
        props.ResetKey,
        ?onNodeClick = props.OnNodeClick,
        ?centerNodeId = props.CenterNodeId,
        ?focusOnMount = props.FocusOnMount,
        ?imageFileName = props.ImageFileName,
        ?layout = props.LayoutSeam
    )

// Layout seam. The script holds one behavior per call, in order. Calls after the script run the real layout.

type private Seam = {
    Fn: LayoutFn
    Calls: ResizeArray<LayoutOptions>
}

let private seamOf (script: LayoutFn list) : Seam =
    let pending = ResizeArray<LayoutFn>(script)
    let calls = ResizeArray<LayoutOptions>()

    let fn: LayoutFn =
        fun nodes edges expanded measure options ->
            calls.Add options

            if pending.Count > 0 then
                let next = pending.[0]
                pending.RemoveAt 0
                next nodes edges expanded measure options
            else
                layoutGraph nodes edges expanded measure options

    { Fn = fn; Calls = calls }

type private Deferred = {
    Promise: JS.Promise<GraphLayout>
    Resolve: GraphLayout -> unit
}

let private deferred () : Deferred =
    let resolver = ref (fun (_: GraphLayout) -> ())

    let promise = Promise.create (fun resolve _ -> resolver.Value <- resolve)

    {
        Promise = promise
        Resolve = fun layout -> resolver.Value layout
    }

let private realLayout (nodes: CanvasNode[]) (edges: CanvasEdge[]) (options: LayoutOptions) : JS.Promise<GraphLayout> =
    layoutGraph nodes edges [||] (fun text _ _ _ -> Dom.estimateTextWidth text) options

let private unlimited: LayoutOptions = { Width = 0.0; Portrait = false }

// Queries

let private eventually (check: unit -> unit) : JS.Promise<unit> =
    RTL.waitFor ((fun () -> check ()), RTLWaitForOptions(timeout = 8000))

let private region () : HTMLElement = RTL.screen.getByTestId "graph-canvas"

let private nodeEl (id: string) : HTMLElement =
    RTL.screen.getByTestId $"graph-node-{id}"

let private edgeEl (id: string) : HTMLElement =
    RTL.screen.getByTestId $"graph-edge-{id}"

let private toggleEl (id: string) : HTMLElement =
    RTL.screen.getByTestId $"graph-edge-toggle-{id}"

let private menuEl () : HTMLElement option =
    RTL.screen.queryByTestId "graph-pill-menu"

let private menuNow () : HTMLElement = (menuEl ()).Value
let private statusEl () : HTMLElement = RTL.screen.getByTestId "graph-status"
let private roleOf (el: HTMLElement) : string = el.getAttribute "role"
let private labelOf (el: HTMLElement) : string = el.getAttribute "aria-label"
let private focused () : obj = box document.activeElement

/// True when every part occurs in the text, each after the one before it.
let private appearInOrder (text: string) (parts: string list) : bool =
    let after (position: int) (part: string) : int =
        if position < 0 then
            -1
        else
            match text.IndexOf(part, position) with
            | -1 -> -1
            | found -> found + part.Length

    (List.fold after 0 parts) >= 0

let private drawn () : JS.Promise<unit> =
    eventually (fun () -> nodeEl "a" |> ignore)

let private hiddenPillShown () : bool =
    RTL.screen.queryAllByRole "img"
    |> Seq.exists (fun pill -> (labelOf pill).EndsWith "duration: 2 h")

let private listTexts (menu: HTMLElement) : string[] =
    let items = menu.querySelectorAll "li"

    [|
        for i in 0 .. items.length - 1 -> items.[i].textContent
    |]

let private click (el: HTMLElement) = RTL.fireEvent.click el

let private press (el: HTMLElement) (key: string) =
    RTL.fireEvent.custom ("keyDown", el, {| key = key |})

let private pointerDownOutside () =
    RTL.fireEvent.custom ("pointerDown", document.body)

let private centerOfGamma () : JS.Promise<float> = promise {
    let! laid = realLayout nodes edges unlimited
    let gamma = laid.LaidOutNodes |> Array.find (fun n -> n.Node.Id = "c")
    return gamma.X + gamma.Width / 2.0
}

let private settle () : JS.Promise<unit> =
    RTL.act (fun () -> promise { do! Promise.sleep 60 })

let private isDisabled (el: HTMLElement) : bool = (el :?> HTMLButtonElement).disabled

let private render (props: Props) : RenderResult = RTL.render (canvas props)

// Node rendering

Vitest.describe (
    "GraphCanvas nodes",
    fun () ->
        Vitest.test (
            "renders a button for every clickable node once the layout is done",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                Vitest.expect(roleOf (nodeEl "a")).toBe "button"
                Vitest.expect(roleOf (nodeEl "b")).toBe "button"
            }
        )

        Vitest.test (
            "gives the graph its name once, on the region",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                let named = RTL.screen.getAllByLabelText "Test graph"

                Vitest.expect(named.Count).toBe 1
                Vitest.expect(named.[0]).toBe (region ())
                Vitest.expect(roleOf (region ())).toBe "region"
            }
        )

        Vitest.test (
            "calls onNodeClick on click, Enter and Space",
            fun () -> promise {
                let clicked = ResizeArray<string>()

                render {
                    defaults with
                        OnNodeClick = Some clicked.Add
                }
                |> ignore

                do! drawn ()

                click (nodeEl "a")
                (nodeEl "b").focus ()
                press (nodeEl "b") "Enter"
                press (nodeEl "b") " "

                Vitest.expect(clicked.ToArray()).toEqual [| "a"; "b"; "b" |]
            }
        )

        Vitest.test (
            "renders a node with clickable false without button semantics",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                Vitest.expect(roleOf (nodeEl "c")).toBe "group"
                Vitest.expect(labelOf (nodeEl "c")).toBe "Gamma, fixed"
            }
        )

        Vitest.test (
            "renders no node as a button without an onNodeClick handler",
            fun () -> promise {
                render { defaults with OnNodeClick = None } |> ignore
                do! drawn ()

                Vitest.expect(roleOf (nodeEl "a")).toBe "group"
                Vitest.expect(roleOf (nodeEl "b")).toBe "group"
            }
        )

        Vitest.test (
            "names a node by its ariaLabel and pill texts, without the tooltip",
            fun () -> promise {
                let withPills = {
                    plainNode "a" with
                        Title = "Alpha"
                        AriaLabel = "Alpha, select to open"
                        Tooltip = Some "Alpha (study)"
                        Pills = [|
                            property "n1" "species" "Talinum" PillCategory.Characteristic
                        |]
                }

                render {
                    defaults with
                        Nodes = [| withPills |]
                        Edges = [||]
                }
                |> ignore

                do! drawn ()

                let label = labelOf (nodeEl "a")
                Vitest.expect(appearInOrder label [ "Alpha, select to open"; "species: Talinum" ]).toBe true
                Vitest.expect(label.Contains "Alpha (study)").toBe false
            }
        )

        Vitest.test (
            "names a count pill by its tooltip in the label of its edge",
            fun () -> promise {
                let counted =
                    edge "ab" "a" "b" [| count "count" "6" (Some "6 shared inputs and outputs") |] [||]

                render {
                    defaults with
                        Nodes = nodes.[0..1]
                        Edges = [| counted |]
                }
                |> ignore

                do! drawn ()

                Vitest
                    .expect(appearInOrder (labelOf (edgeEl "ab")) [ "Alpha"; "Beta"; "6 shared inputs and outputs" ])
                    .toBe
                    true
            }
        )

        Vitest.test (
            "groups the pills of an edge under a label with both ends and the pill texts",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                let collapsed = labelOf (edgeEl "ab")
                Vitest.expect(appearInOrder collapsed [ "Alpha"; "Beta"; "temperature: 20 C" ]).toBe true
                Vitest.expect(collapsed.Contains "duration: 2 h").toBe false

                click (toggleEl "ab")

                do!
                    eventually (fun () ->
                        Vitest
                            .expect(
                                appearInOrder (labelOf (edgeEl "ab")) [
                                    "Alpha"
                                    "Beta"
                                    "temperature: 20 C"
                                    "duration: 2 h"
                                    "volume: 5 ml"
                                ]
                            )
                            .toBe
                            true
                    )
            }
        )
)

// Expansion

Vitest.describe (
    "GraphCanvas expansion",
    fun () ->
        Vitest.test (
            "reports whether the toggle of an edge is expanded",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "false"

                click (toggleEl "ab")

                do! eventually (fun () -> Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "true")
            }
        )

        Vitest.test (
            "shows the hidden pills behind the toggle and hides them again",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()
                Vitest.expect(hiddenPillShown ()).toBe false

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe false)
            }
        )

        Vitest.test (
            "collapses expanded edges when resetKey changes",
            fun () -> promise {
                let result = render defaults
                do! drawn ()
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)

                result.rerender (canvas { defaults with ResetKey = "two" })

                do!
                    eventually (fun () ->
                        Vitest.expect(roleOf (nodeEl "a")).toBe "button"
                        Vitest.expect(hiddenPillShown ()).toBe false
                    )
            }
        )

        Vitest.test (
            "starts collapsed when it returns to a resetKey it left expanded",
            fun () -> promise {
                let result = render { defaults with ResetKey = "K1" }
                do! drawn ()
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)
                result.rerender (canvas { defaults with ResetKey = "K2" })
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe false)

                result.rerender (canvas { defaults with ResetKey = "K1" })

                do!
                    eventually (fun () ->
                        Vitest.expect(roleOf (nodeEl "a")).toBe "button"
                        Vitest.expect(hiddenPillShown ()).toBe false
                    )
            }
        )

        Vitest.test (
            "keeps a new layout function identity from starting another layout",
            fun () -> promise {
                let first = seamOf []
                let second = seamOf []

                let result =
                    render {
                        defaults with
                            LayoutSeam = Some first.Fn
                    }

                do! drawn ()
                let before = first.Calls.Count

                result.rerender (
                    canvas {
                        defaults with
                            LayoutSeam = Some second.Fn
                    }
                )

                do! settle ()

                Vitest.expect(first.Calls.Count).toBe before
                Vitest.expect(second.Calls.Count).toBe 0
            }
        )
)

// Focus, scroll and results

Vitest.describe (
    "GraphCanvas views",
    fun () ->
        Vitest.test (
            "leaves focus alone on the first mount",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                Vitest.expect(focused ()).toBe (box document.body)
            }
        )

        Vitest.test (
            "moves focus to the container once the layout of a new view is drawn",
            fun () -> promise {
                let result = render defaults
                do! drawn ()

                result.rerender (canvas { defaults with ResetKey = "two" })
                do! eventually (fun () -> Vitest.expect(focused ()).toBe (box (region ())))
            }
        )

        Vitest.test (
            "moves focus to the container after the first layout when focusOnMount is set",
            fun () -> promise {
                render {
                    defaults with
                        FocusOnMount = Some true
                }
                |> ignore

                Vitest.expect(focused ()).toBe (box document.body)

                do! eventually (fun () -> Vitest.expect(focused ()).toBe (box (region ())))
            }
        )

        Vitest.test (
            "shows an alert and logs the error when the layout fails",
            fun () -> promise {
                silenceConsoleError ()

                let seam =
                    seamOf [
                        fun _ _ _ _ _ -> Promise.reject (exn "elk failed")
                    ]

                render {
                    defaults with
                        LayoutSeam = Some seam.Fn
                }
                |> ignore

                do! eventually (fun () -> Vitest.expect(roleOf (statusEl ())).toBe "alert")
                Vitest.expect(loggedErrors ()).toBeGreaterThan 0
            }
        )

        Vitest.test (
            "names the reason of a failed layout in the alert",
            fun () -> promise {
                silenceConsoleError ()

                let seam =
                    seamOf [
                        fun _ _ _ _ _ -> Promise.reject (exn "elk failed")
                    ]

                render {
                    defaults with
                        LayoutSeam = Some seam.Fn
                }
                |> ignore

                do! eventually (fun () -> Vitest.expect(roleOf (statusEl ())).toBe "alert")

                Vitest.expect((RTL.screen.getByTestId "graph-status-reason").textContent.Contains "elk failed").toBe
                    true
            }
        )

        Vitest.test (
            "moves focus to the container that shows the alert when the layout of a new view fails",
            fun () -> promise {
                silenceConsoleError ()
                let seam = seamOf []

                let result =
                    render {
                        defaults with
                            LayoutSeam = Some seam.Fn
                    }

                do! drawn ()

                let failing =
                    seamOf [
                        fun _ _ _ _ _ -> Promise.reject (exn "elk failed")
                    ]

                result.rerender (
                    canvas {
                        defaults with
                            ResetKey = "two"
                            LayoutSeam = Some failing.Fn
                    }
                )

                do!
                    eventually (fun () ->
                        Vitest.expect(roleOf (statusEl ())).toBe "alert"
                        Vitest.expect(focused ()).toBe (box (region ()))
                    )
            }
        )

        Vitest.test (
            "scrolls back to the top left when the view changes",
            fun () -> promise {
                let result = render defaults
                do! drawn ()
                setScrollLeft (region ()) 40.0
                setScrollTop (region ()) 25.0

                result.rerender (canvas { defaults with ResetKey = "two" })
                do! eventually (fun () -> Vitest.expect(focused ()).toBe (box (region ())))

                Vitest.expect([| scrollLeftOf (region ()); scrollTopOf (region ()) |]).toEqual [| 0.0; 0.0 |]
            }
        )

        Vitest.test (
            "never draws the result of a layout from an earlier resetKey",
            fun () -> promise {
                let stale = deferred ()
                let fresh = deferred ()

                let seam =
                    seamOf [
                        (fun _ _ _ _ _ -> stale.Promise)
                        (fun _ _ _ _ _ -> fresh.Promise)
                    ]

                let oldNodes = [| plainNode "Old" |]
                let newNodes = [| plainNode "New" |]

                let result =
                    render {
                        defaults with
                            Nodes = oldNodes
                            Edges = [||]
                            LayoutSeam = Some seam.Fn
                    }

                do! eventually (fun () -> Vitest.expect(seam.Calls.Count).toBe 1)

                result.rerender (
                    canvas {
                        defaults with
                            Nodes = newNodes
                            Edges = [||]
                            ResetKey = "two"
                            LayoutSeam = Some seam.Fn
                    }
                )

                do! eventually (fun () -> Vitest.expect(seam.Calls.Count).toBe 2)

                let! oldLayout = realLayout oldNodes [||] unlimited
                do! RTL.act (fun () -> promise { stale.Resolve oldLayout })
                Vitest.expect(RTL.screen.queryByTestId("graph-node-Old").IsNone).toBe true
                Vitest.expect(roleOf (statusEl ())).toBe "status"

                let! newLayout = realLayout newNodes [||] unlimited
                do! RTL.act (fun () -> promise { fresh.Resolve newLayout })
                do! eventually (fun () -> nodeEl "New" |> ignore)
                Vitest.expect(RTL.screen.queryByTestId("graph-node-Old").IsNone).toBe true
            }
        )

        Vitest.test (
            "scrolls, centers and focuses again when it returns to a view before the other view resolved",
            fun () -> promise {
                stubBox 300.0 200.0 300.0 200.0
                setScrollSize 5000.0 200.0
                let! center = centerOfGamma ()
                // The scroll position that the test sets must differ from the centered one.
                Vitest.expect(center - 150.0).not.toBe 40.0

                let never = deferred ()

                let seam =
                    seamOf [
                        (fun n e x m o -> layoutGraph n e x m o)
                        (fun _ _ _ _ _ -> never.Promise)
                    ]

                let view (key: string) : Props = {
                    defaults with
                        ResetKey = key
                        CenterNodeId = Some "c"
                        LayoutSeam = Some seam.Fn
                }

                let result = render (view "A")

                do! drawn ()
                setScrollLeft (region ()) 40.0
                setScrollTop (region ()) 25.0

                result.rerender (canvas (view "B"))

                do! eventually (fun () -> Vitest.expect(roleOf (statusEl ())).toBe "status")

                result.rerender (canvas (view "A"))
                // The old drawing of A belongs to an earlier epoch, so the canvas says it lays out again.
                Vitest.expect(roleOf (statusEl ())).toBe "status"
                Vitest.expect(RTL.screen.queryByTestId("graph-node-a").IsNone).toBe true

                do! drawn ()
                do! eventually (fun () -> Vitest.expect(focused ()).toBe (box (region ())))
                // The scroll position is set again for the new epoch, here to the centered node.
                Vitest.expect([| scrollLeftOf (region ()); scrollTopOf (region ()) |]).toEqual [| center - 150.0; 0.0 |]
            }
        )
)

// Centering

Vitest.describe (
    "GraphCanvas centerNodeId",
    fun () ->
        Vitest.test (
            "scrolls so that the node is in the middle of the container",
            fun () -> promise {
                stubBox 300.0 200.0 300.0 200.0
                setScrollSize 5000.0 200.0
                let! center = centerOfGamma ()

                render {
                    defaults with
                        CenterNodeId = Some "c"
                }
                |> ignore

                do! drawn ()

                Vitest.expect(scrollLeftOf (region ())).toBe (center - 150.0)
            }
        )

        Vitest.test (
            "stays inside the scroll range",
            fun () -> promise {
                stubBox 300.0 200.0 300.0 200.0
                setScrollSize 400.0 200.0

                render {
                    defaults with
                        CenterNodeId = Some "c"
                }
                |> ignore

                do! drawn ()

                Vitest.expect([| scrollLeftOf (region ()); scrollTopOf (region ()) |]).toEqual [| 100.0; 0.0 |]
            }
        )
)

// A box with a size. The edge from Alpha to Beta needs a box of 543px to expand inline: its widest pill (143px),
// the 132px of the widest node on each side, 32px of label padding and 104px of ELK padding and spacing.

Vitest.describe (
    "GraphCanvas in a measured box",
    fun () ->
        let measureBox (width: float) (height: float) =
            stubBox width height width height
            installResizeObserver ()

        let resize (width: float) (height: float) : JS.Promise<unit> =
            setBox width height width height
            RTL.act (fun () -> promise { notifyResize () })

        let withSeam () =
            let seam = seamOf []

            seam,
            {
                defaults with
                    LayoutSeam = Some seam.Fn
            }

        Vitest.test (
            "expands the edge inline in a box that is wide enough",
            fun () -> promise {
                measureBox 1200.0 700.0
                let seam, props = withSeam ()
                render props |> ignore
                do! drawn ()

                click (toggleEl "ab")

                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)
                Vitest.expect(menuEl().IsNone).toBe true
                Vitest.expect(seam.Calls.[seam.Calls.Count - 1]).toEqual { Width = 1200.0; Portrait = false }
            }
        )

        Vitest.test (
            "opens a list of the hidden properties in a box that is too narrow, without a new layout",
            fun () -> promise {
                measureBox 500.0 300.0
                let seam, props = withSeam ()
                render props |> ignore
                do! drawn ()
                let before = seam.Calls.Count

                click (toggleEl "ab")

                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                Vitest.expect(roleOf (menuNow ())).toBe "dialog"
                Vitest.expect(listTexts (menuNow ())).toEqual [| "duration: 2 h"; "volume: 5 ml" |]
                Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "true"
                Vitest.expect(hiddenPillShown ()).toBe false
                Vitest.expect(seam.Calls.Count).toBe before
            }
        )

        Vitest.test (
            "moves the focus into the list and back to the toggle on Escape",
            fun () -> promise {
                measureBox 500.0 300.0
                render defaults |> ignore
                do! drawn ()
                let more = toggleEl "ab"
                more.focus ()

                press more "Enter"
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                Vitest.expect(focused ()).toBe (box ((menuNow ()).querySelector "ul"))

                press document.body "Escape"
                do! eventually (fun () -> Vitest.expect(menuEl().IsNone).toBe true)
                Vitest.expect(focused ()).toBe (box (toggleEl "ab"))
                Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "false"
            }
        )

        for name, shift in [ "Tab", false; "Shift+Tab", true ] do
            Vitest.test (
                $"closes the list on {name} and puts the focus back on the toggle",
                fun () -> promise {
                    measureBox 500.0 300.0
                    render defaults |> ignore
                    do! drawn ()
                    (toggleEl "ab").focus ()
                    press (toggleEl "ab") "Enter"
                    do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)

                    RTL.fireEvent.custom ("keyDown", menuNow (), {| key = "Tab"; shiftKey = shift |})

                    do! eventually (fun () -> Vitest.expect(menuEl().IsNone).toBe true)
                    Vitest.expect(focused ()).toBe (box (toggleEl "ab"))
                    Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "false"
                }
            )

        Vitest.test (
            "closes the list on a click outside and on a second click on the toggle",
            fun () -> promise {
                measureBox 500.0 300.0
                render defaults |> ignore
                do! drawn ()

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                pointerDownOutside ()
                do! eventually (fun () -> Vitest.expect(menuEl().IsNone).toBe true)

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsNone).toBe true)
            }
        )

        Vitest.test (
            "keeps one list open at a time",
            fun () -> promise {
                measureBox 300.0 200.0
                render { defaults with Edges = edgesWithMore } |> ignore
                do! drawn ()

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                click (toggleEl "bc")

                do!
                    eventually (fun () ->
                        Vitest.expect((toggleEl "bc").getAttribute "aria-expanded").toBe "true"
                        Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "false"
                    )

                Vitest.expect(RTL.screen.queryAllByRole("dialog").Count).toBe 1
            }
        )

        Vitest.test (
            "relays out on a resize only while an edge is expanded",
            fun () -> promise {
                measureBox 1200.0 700.0
                let seam, props = withSeam ()
                render props |> ignore
                do! drawn ()
                let collapsed = seam.Calls.Count

                do! resize 1000.0 700.0
                do! settle ()
                Vitest.expect(seam.Calls.Count).toBe collapsed

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)
                let expanded = seam.Calls.Count

                // A change below the noise limit keeps the old box.
                do! resize 1002.0 702.0
                do! settle ()
                Vitest.expect(seam.Calls.Count).toBe expanded

                do! resize 900.0 700.0
                do! eventually (fun () -> Vitest.expect(seam.Calls.Count).toBe (expanded + 1))
                Vitest.expect(seam.Calls.[seam.Calls.Count - 1]).toEqual { Width = 900.0; Portrait = false }
            }
        )

        Vitest.test (
            "draws an expanded edge collapsed while the box is too narrow and expands it again when the box grows",
            fun () -> promise {
                measureBox 1200.0 700.0
                render defaults |> ignore
                do! drawn ()
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)

                do! resize 400.0 300.0

                do!
                    eventually (fun () ->
                        Vitest.expect(hiddenPillShown ()).toBe false
                        Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "false"
                    )

                do! resize 1200.0 700.0
                do! eventually (fun () -> Vitest.expect(hiddenPillShown ()).toBe true)
            }
        )

        Vitest.test (
            "opens the list below the toggle when there is more room below and limits its height to that room",
            fun () -> promise {
                measureBox 500.0 300.0
                stubRects 40.0
                render defaults |> ignore
                do! drawn ()

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)

                let menu = menuNow ()
                // 300 visible, minus the bottom of the toggle (58), the 4px gap and the 8px margin.
                Vitest.expect(styleOf menu "max-height").toBe "230px"
                Vitest.expect(styleOf menu "top").toBe "62px"
                Vitest.expect(styleOf menu "bottom").toBe "auto"
                Vitest.expect(styleOf menu "max-width").toBe "484px"
            }
        )

        Vitest.test (
            "opens the list above the toggle when there is more room above",
            fun () -> promise {
                measureBox 500.0 300.0
                stubRects 240.0
                render defaults |> ignore
                do! drawn ()

                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)

                let menu = menuNow ()
                // 240 above the toggle, minus the gap and the margin.
                Vitest.expect(styleOf menu "max-height").toBe "228px"
                Vitest.expect(styleOf menu "top").toBe "auto"
                Vitest.expect(styleOf menu "bottom").toBe "64px"
            }
        )

        Vitest.test (
            "fits an open list to the new height of the box",
            fun () -> promise {
                measureBox 500.0 300.0
                stubRects 40.0
                render defaults |> ignore
                do! drawn ()
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                let menu = menuNow ()
                Vitest.expect(styleOf menu "max-height").toBe "230px"

                do! resize 500.0 200.0

                do! eventually (fun () -> Vitest.expect(styleOf menu "max-height").toBe "130px")
                Vitest.expect(menuNow ()).toBe menu
            }
        )

        Vitest.test (
            "closes an open list when the box grows",
            fun () -> promise {
                measureBox 500.0 300.0
                render defaults |> ignore
                do! drawn ()
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)

                do! resize 900.0 300.0

                do! eventually (fun () -> Vitest.expect(menuEl().IsNone).toBe true)
                Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "false"
            }
        )

        Vitest.test (
            "keeps the list closed when the box returns to the width it was opened on",
            fun () -> promise {
                measureBox 500.0 300.0
                render defaults |> ignore
                do! drawn ()
                click (toggleEl "ab")
                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)

                do! resize 900.0 300.0
                do! eventually (fun () -> Vitest.expect(menuEl().IsNone).toBe true)

                do! resize 500.0 300.0
                do! settle ()
                Vitest.expect(menuEl().IsNone).toBe true
            }
        )
)

// Portrait boxes

Vitest.describe (
    "GraphCanvas in a portrait box",
    fun () ->
        let measureBox (width: float) (height: float) (borderWidth: float) (borderHeight: float) =
            stubBox width height borderWidth borderHeight
            installResizeObserver ()

        let withSeam () =
            let seam = seamOf []

            seam,
            {
                defaults with
                    LayoutSeam = Some seam.Fn
            }

        let chipDrawn () : JS.Promise<unit> =
            eventually (fun () ->
                Vitest.expect((RTL.screen.getByTestId "graph-canvas-content").getAttribute "data-compact").toBe "true"
            )

        Vitest.test (
            "shows one chip per edge with several pills, keeps a single pill and lists every pill on the chip",
            fun () -> promise {
                measureBox 358.0 760.0 358.0 760.0
                let seam, props = withSeam ()
                render props |> ignore
                do! chipDrawn ()

                Vitest.expect(hiddenPillShown ()).toBe false
                Vitest.expect((edgeEl "bc").querySelectorAll("[role='img']").length).toBe 1
                // A portrait box takes the compact layout, which does not depend on the width.
                Vitest.expect(seam.Calls.[seam.Calls.Count - 1]).toEqual { Width = 0.0; Portrait = true }

                click (toggleEl "ab")

                do! eventually (fun () -> Vitest.expect(menuEl().IsSome).toBe true)
                Vitest.expect(roleOf (menuNow ())).toBe "dialog"

                Vitest.expect(listTexts (menuNow ())).toEqual [| "temperature: 20 C"; "duration: 2 h"; "volume: 5 ml" |]

                Vitest.expect((toggleEl "ab").getAttribute "aria-expanded").toBe "true"
            }
        )

        Vitest.test (
            "uses the compact layout in a portrait box even when the normal layout would fit",
            fun () -> promise {
                measureBox 1200.0 1500.0 1200.0 1500.0
                render defaults |> ignore

                do! chipDrawn ()
                Vitest.expect(roleOf (toggleEl "ab")).toBe "button"
            }
        )

        Vitest.test (
            "keeps the normal layout in a landscape box even when the graph overflows it",
            fun () -> promise {
                measureBox 300.0 200.0 300.0 200.0
                let seam, props = withSeam ()
                render props |> ignore
                do! drawn ()

                Vitest.expect((RTL.screen.getByTestId "graph-canvas-content").getAttribute "data-compact").toBe "false"
                Vitest.expect(seam.Calls.[seam.Calls.Count - 1]).toEqual { Width = 0.0; Portrait = false }
            }
        )

        Vitest.test (
            "does not lay out again when a portrait box changes its width",
            fun () -> promise {
                measureBox 358.0 760.0 358.0 760.0
                let seam, props = withSeam ()
                render props |> ignore
                do! chipDrawn ()
                let before = seam.Calls.Count

                setBox 500.0 760.0 500.0 760.0
                do! RTL.act (fun () -> promise { notifyResize () })
                do! settle ()

                Vitest.expect(seam.Calls.Count).toBe before
            }
        )

        Vitest.test (
            "starts the graph at the left edge of the box, where a normal graph is centered",
            fun () -> promise {
                measureBox 358.0 760.0 358.0 760.0
                let first = render defaults
                do! chipDrawn ()

                let content () =
                    RTL.screen.getByTestId "graph-canvas-content"

                Vitest.expect((content ()).getAttribute "data-compact").toBe "true"
                first.unmount ()
                restoreBox ()
                removeResizeObserver ()

                measureBox 1200.0 700.0 1200.0 700.0
                render defaults |> ignore
                do! drawn ()
                Vitest.expect((content ()).getAttribute "data-compact").toBe "false"
            }
        )

        Vitest.test (
            "keeps its mode when a scrollbar takes space off the client size of a box that is nearly square",
            fun () -> promise {
                // 740x745 is portrait. A horizontal scrollbar leaves 730 of the height, which is less than the width.
                measureBox 740.0 745.0 740.0 745.0
                let seam, props = withSeam ()
                render props |> ignore
                do! chipDrawn ()
                let before = seam.Calls.Count

                setBox 740.0 730.0 740.0 745.0
                do! RTL.act (fun () -> promise { notifyResize () })
                do! settle ()

                Vitest.expect((RTL.screen.getByTestId "graph-canvas-content").getAttribute "data-compact").toBe "true"
                Vitest.expect(seam.Calls.Count).toBe before
            }
        )

        Vitest.test (
            "keeps the normal layout in a landscape box whose client size is portrait",
            fun () -> promise {
                // The border box 740x735 is landscape. The stable scrollbar gutter leaves 725 of its width, which is
                // less than the height, so only the border box gives the right answer.
                measureBox 725.0 735.0 740.0 735.0
                render defaults |> ignore
                do! drawn ()

                Vitest.expect((RTL.screen.getByTestId "graph-canvas-content").getAttribute "data-compact").toBe "false"
            }
        )

        Vitest.test (
            "scrolls to a centered node only vertically",
            fun () -> promise {
                measureBox 200.0 300.0 200.0 300.0
                setScrollSize 5000.0 5000.0
                let! laid = realLayout nodes edges { Width = 0.0; Portrait = true }
                let target = laid.LaidOutNodes |> Array.find (fun n -> n.Node.Id = "c")

                render {
                    defaults with
                        CenterNodeId = Some "c"
                }
                |> ignore

                do! chipDrawn ()

                Vitest.expect(scrollLeftOf (region ())).toBe 0.0
                Vitest.expect(scrollTopOf (region ())).toBe (target.Y + target.Height / 2.0 - 150.0)
            }
        )
)

// Zoom

Vitest.describe (
    "GraphCanvas zoom",
    fun () ->
        let svgWidth () : float =
            float (((region ()).querySelector "svg").getAttribute "width")

        Vitest.test (
            "zooms the graph in and out while keeping nodes interactive",
            fun () -> promise {
                let clicked = ResizeArray<string>()

                render {
                    defaults with
                        OnNodeClick = Some clicked.Add
                }
                |> ignore

                do! drawn ()
                let original = svgWidth ()

                click (RTL.screen.getByTestId "graph-zoom-in")
                Vitest.expect(svgWidth ()).toBe (original * 1.25)

                click (nodeEl "a")
                Vitest.expect(clicked.ToArray()).toEqual [| "a" |]

                click (RTL.screen.getByTestId "graph-zoom-out")
                Vitest.expect(svgWidth ()).toBe original
            }
        )

        Vitest.test (
            "disables the zoom buttons at the bounds",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()
                let zoomIn () = RTL.screen.getByTestId "graph-zoom-in"
                let zoomOut () = RTL.screen.getByTestId "graph-zoom-out"

                for _ in 1..4 do
                    click (zoomIn ())

                Vitest.expect(isDisabled (zoomIn ())).toBe true
                Vitest.expect(isDisabled (zoomOut ())).toBe false

                for _ in 1..7 do
                    click (zoomOut ())

                Vitest.expect(isDisabled (zoomOut ())).toBe true
                Vitest.expect(isDisabled (zoomIn ())).toBe false
            }
        )

        Vitest.test (
            "keeps the zoom when the view changes",
            fun () -> promise {
                let result = render defaults
                do! drawn ()
                let original = svgWidth ()
                click (RTL.screen.getByTestId "graph-zoom-in")

                result.rerender (canvas { defaults with ResetKey = "two" })
                do! eventually (fun () -> Vitest.expect(focused ()).toBe (box (region ())))

                Vitest.expect(svgWidth ()).toBe (original * 1.25)
            }
        )
)

// Image export. jsdom has no canvas, so the tests do not click the buttons.

Vitest.describe (
    "GraphCanvas image export",
    fun () ->
        Vitest.test (
            "has no export buttons without an image file name",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()

                Vitest.expect((RTL.screen.queryByTestId "graph-image-download").IsNone).toBe true
                Vitest.expect((RTL.screen.queryByTestId "graph-image-copy").IsNone).toBe true
            }
        )

        Vitest.test (
            "has the download button once a layout is drawn when it gets an image file name",
            fun () -> promise {
                render {
                    defaults with
                        ImageFileName = Some "graph.png"
                }
                |> ignore

                Vitest.expect((RTL.screen.queryByTestId "graph-image-download").IsNone).toBe true

                do! drawn ()

                Vitest.expect((RTL.screen.queryByTestId "graph-image-download").IsSome).toBe true
            }
        )
)

// Minimap. The box shows 300x200 px of a scroll area of 5000x3000 px, so the graph overflows it.

// The root sits at the origin and the drawing 30px to the right of it and 20px below. The svg of the minimap sits at (100, 200).
[<Emit("""(() => {
    const original = Element.prototype.getBoundingClientRect;
    const rect = (top, left, width, height) => ({ top, left, bottom: top + height, right: left + width, x: left, y: top, width, height, toJSON() {} });
    Element.prototype.getBoundingClientRect = function () {
        if (this.getAttribute('data-testid') === 'graph-canvas-content') return rect(20, 30, 0, 0);
        if (this.parentElement?.getAttribute('data-testid') === 'graph-minimap') return rect(200, 100, Number(this.getAttribute('width')), Number(this.getAttribute('height')));
        return rect(0, 0, 0, 0);
    };
    globalThis.__graphRestoreRect = () => { Element.prototype.getBoundingClientRect = original };
})()""")>]
let private stubMinimapRects () : unit = jsNative

Vitest.describe (
    "GraphCanvas minimap",
    fun () ->
        let overflowBox () =
            stubBox 300.0 200.0 300.0 200.0
            setScrollSize 5000.0 3000.0

        let minimapEl () : HTMLElement option =
            RTL.screen.queryByTestId "graph-minimap"

        let toggleButton () : HTMLElement =
            RTL.screen.getByTestId "graph-minimap-toggle"

        let minimapShown () : JS.Promise<unit> =
            eventually (fun () -> Vitest.expect(minimapEl().IsSome).toBe true)

        Vitest.test (
            "draws a rect for every node and the visible part when the graph overflows the box",
            fun () -> promise {
                overflowBox ()
                render defaults |> ignore
                do! minimapShown ()
                do! eventually (fun () -> RTL.screen.getByTestId "graph-minimap-viewport" |> ignore)

                Vitest.expect(((minimapEl ()).Value).querySelectorAll("rect").length).toBe (nodes.Length + 1)
                Vitest.expect((RTL.screen.queryByTestId "graph-minimap-toggle").IsSome).toBe true
            }
        )

        Vitest.test (
            "shows neither the minimap nor its toggle when the graph fits the box",
            fun () -> promise {
                render defaults |> ignore
                do! drawn ()
                do! settle ()

                Vitest.expect(minimapEl().IsNone).toBe true
                Vitest.expect((RTL.screen.queryByTestId "graph-minimap-toggle").IsNone).toBe true
            }
        )

        Vitest.test (
            "hides and shows the minimap with the toggle, which reports its state",
            fun () -> promise {
                overflowBox ()
                render defaults |> ignore
                do! minimapShown ()
                Vitest.expect((toggleButton ()).getAttribute "aria-pressed").toBe "true"

                click (toggleButton ())
                Vitest.expect(minimapEl().IsNone).toBe true
                Vitest.expect((toggleButton ()).getAttribute "aria-pressed").toBe "false"

                click (toggleButton ())
                Vitest.expect(minimapEl().IsSome).toBe true
                Vitest.expect((toggleButton ()).getAttribute "aria-pressed").toBe "true"
            }
        )

        Vitest.test (
            "keeps the focus on the zoom button when its click makes the graph overflow",
            fun () -> promise {
                stubBox 300.0 200.0 300.0 200.0
                setScrollSize 300.0 200.0
                render defaults |> ignore
                do! drawn ()
                Vitest.expect((RTL.screen.queryByTestId "graph-minimap-toggle").IsNone).toBe true

                let zoomIn = RTL.screen.getByTestId "graph-zoom-in"
                zoomIn.focus ()
                setScrollSize 5000.0 3000.0
                click zoomIn

                do! minimapShown ()
                Vitest.expect(RTL.screen.getByTestId ("graph-zoom-in")).toBe zoomIn
                Vitest.expect(focused ()).toBe (box zoomIn)
            }
        )

        Vitest.test (
            "scrolls the canvas so that the pressed point of the minimap is in the middle",
            fun () -> promise {
                overflowBox ()
                stubMinimapRects ()
                let! laid = realLayout nodes edges unlimited
                render defaults |> ignore
                do! minimapShown ()

                let k = Minimap.scale laid.LayoutWidth laid.LayoutHeight
                let x = 0.75 * laid.LayoutWidth
                let y = 0.75 * laid.LayoutHeight

                let geometry: ScrollGeometry = {
                    ScrollLeft = 0.0
                    ScrollTop = 0.0
                    ClientWidth = 300.0
                    ClientHeight = 200.0
                    ScrollWidth = 5000.0
                    ScrollHeight = 3000.0
                    OffsetX = 30.0
                    OffsetY = 20.0
                    Zoom = 1.0
                }

                let expectedLeft, expectedTop = Minimap.centerOn geometry x y
                // The point is far enough from the origin that clamping does not hide a wrong conversion.
                Vitest.expect(expectedLeft).toBeGreaterThan 0.0

                RTL.fireEvent.custom (
                    "pointerDown",
                    unbox<HTMLElement> (((minimapEl ()).Value).querySelector "svg"),
                    {|
                        clientX = 100.0 + k * x
                        clientY = 200.0 + k * y
                        button = 0
                        buttons = 1
                        pointerId = 1
                        isPrimary = true
                        pointerType = "mouse"
                    |}
                )

                Vitest.expect(scrollLeftOf (region ())).toBeCloseTo expectedLeft
                Vitest.expect(scrollTopOf (region ())).toBeCloseTo expectedTop
            }
        )
)
