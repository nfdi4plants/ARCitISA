namespace Swate.Components.JsBindings

open Fable.Core
open Fable.Core.JsInterop
open Swate.Components.Util.WebWorker

/// Typed binding for elkjs (https://github.com/kieler/elkjs), the bundled build without web workers.
///
/// ElkWorker.fs (Composite/GraphCanvas) runs this module inside a Web Worker. elk.bundled.js takes a missing
/// `document` as the sign that it is itself the ELK worker, and then its constructor fails. The engine
/// creation sets an empty `document` in a worker scope first, so ELK runs its layout on the worker thread.
/// On the main thread nothing changes.
module Elk =

    /// Keys are ELK option ids such as "org.eclipse.elk.algorithm", so the object is built with createObj.
    type ElkLayoutOptions = obj

    // Input shapes. They are created with jsOptions, so the JS object holds exactly the fields that were set.

    [<AllowNullLiteral>]
    type ElkLabel =
        abstract id: string with get, set
        abstract text: string with get, set
        abstract width: float with get, set
        abstract height: float with get, set
        abstract layoutOptions: ElkLayoutOptions with get, set

    [<AllowNullLiteral>]
    type ElkEdge =
        abstract id: string with get, set
        abstract sources: string[] with get, set
        abstract targets: string[] with get, set
        abstract labels: ElkLabel[] with get, set

    [<AllowNullLiteral>]
    type ElkNode =
        abstract id: string with get, set
        abstract width: float with get, set
        abstract height: float with get, set
        abstract children: ElkNode[] with get, set
        abstract edges: ElkEdge[] with get, set
        abstract layoutOptions: ElkLayoutOptions with get, set

    // Output shapes. ELK types most of these fields as optional, so they are options here.

    [<AllowNullLiteral>]
    type ElkPoint =
        abstract x: float
        abstract y: float

    [<AllowNullLiteral>]
    type ElkSection =
        abstract startPoint: ElkPoint
        abstract endPoint: ElkPoint
        abstract bendPoints: ElkPoint[] option

    [<AllowNullLiteral>]
    type ElkLaidLabel =
        abstract x: float option
        abstract y: float option
        abstract width: float option
        abstract height: float option

    [<AllowNullLiteral>]
    type ElkLaidEdge =
        abstract id: string
        abstract sections: ElkSection[] option
        abstract labels: ElkLaidLabel[] option

    [<AllowNullLiteral>]
    type ElkLaidNode =
        abstract id: string
        abstract x: float option
        abstract y: float option
        abstract width: float option
        abstract height: float option

    [<AllowNullLiteral>]
    type ElkLayoutResult =
        abstract width: float option
        abstract height: float option
        abstract children: ElkLaidNode[] option
        abstract edges: ElkLaidEdge[] option

    [<ImportDefault("elkjs/lib/elk.bundled.js")>]
    type private ElkEngine() =
        member _.layout(graph: ElkNode) : JS.Promise<ElkLayoutResult> = jsNative

    [<Emit("if (typeof document === 'undefined') { self.document = {}; }")>]
    let private stubDocument () : unit = jsNative

    // The first layout creates the engine. Later layouts reuse it.
    let private engine =
        lazy
            (if isWorkerScope () then
                 stubDocument ()

             ElkEngine())

    /// Runs ELK. The returned object is the input graph, filled with positions and edge sections.
    let layout (graph: ElkNode) : JS.Promise<ElkLayoutResult> = engine.Value.layout graph

    /// Builds a layoutOptions object from option id and value pairs.
    let layoutOptions (pairs: (string * string) list) : ElkLayoutOptions =
        createObj [ for key, value in pairs -> key ==> value ]

    let label (id: string) (text: string) (width: float) (height: float) (options: ElkLayoutOptions) : ElkLabel =
        jsOptions<ElkLabel> (fun l ->
            l.id <- id
            l.text <- text
            l.width <- width
            l.height <- height
            l.layoutOptions <- options
        )

    /// An edge from one source to one target. The labels field is only present when labels is not empty.
    let edge (id: string) (source: string) (target: string) (labels: ElkLabel[]) : ElkEdge =
        // An `if` inside the jsOptions lambda was dropped from the generated code, so the two shapes are built separately.
        if labels.Length = 0 then
            jsOptions<ElkEdge> (fun e ->
                e.id <- id
                e.sources <- [| source |]
                e.targets <- [| target |]
            )
        else
            jsOptions<ElkEdge> (fun e ->
                e.id <- id
                e.sources <- [| source |]
                e.targets <- [| target |]
                e.labels <- labels
            )

    /// A child node with a fixed size.
    let leaf (id: string) (width: float) (height: float) : ElkNode =
        jsOptions<ElkNode> (fun n ->
            n.id <- id
            n.width <- width
            n.height <- height
        )

    /// The graph that is passed to layout.
    let root (id: string) (options: ElkLayoutOptions) (children: ElkNode[]) (edges: ElkEdge[]) : ElkNode =
        jsOptions<ElkNode> (fun n ->
            n.id <- id
            n.layoutOptions <- options
            n.children <- children
            n.edges <- edges
        )
