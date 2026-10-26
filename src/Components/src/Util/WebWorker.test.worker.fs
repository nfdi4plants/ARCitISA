/// Worker definitions for the WebWorker tests and stories. The file stays free of React and DOM code, like a
/// real worker file, because it is the entry of the worker bundle.
module internal Swate.Components.Util.WebWorkerTestWorker

open Fable.Core
open Swate.Components.Util.WebWorker

[<Emit("() => new Worker('/does-not-exist.js', { type: 'module' })")>]
let private missingScript: unit -> WorkerInstance = jsNative

/// Reports whether the handler ran in a worker.
let ProbeWorker: WorkerDef<unit, {| inWorker: bool |}> =
    createWorker
        (selfWorker ("./WebWorker.test.worker" + Compiler.extension))
        (fun () -> promise { return {| inWorker = isWorkerScope () |} })

/// The same function behind a worker script that cannot load.
let BrokenWorker = {
    ProbeWorker with
        Ctor = Some missingScript
}
