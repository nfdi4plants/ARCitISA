/// The ELK layout as a Web Worker function. GraphCanvas starts it with `useWebWorker ElkWorker.Worker`.
///
/// This file is the entry of the worker bundle, so it imports only the ELK binding and the WebWorker module.
/// Anything that pulls in React or DOM code would end up in the worker.
module Swate.Components.Composite.GraphCanvas.ElkWorker

open Fable.Core
open Swate.Components.JsBindings
open Swate.Components.Util.WebWorker

/// The ELK graph goes in and the laid out graph comes back. Without a Worker (jsdom, Node) the layout runs on the
/// calling thread.
let Worker: WorkerDef<Elk.ElkNode, Elk.ElkLayoutResult> =
    createWorker (selfWorker ("./ElkWorker" + Compiler.extension)) (fun graph -> Elk.layout graph)
