/// A worker that fails 300 ms after its first request arrives, while the handler is still waiting. The failure
/// is an uncaught error in the worker script, which fires the error event of the Worker object.
module internal Swate.Components.Util.WebWorkerCrashingWorker

open Fable.Core
open Swate.Components.Util.WebWorker

[<Emit("self.addEventListener('message', () => setTimeout(() => { throw new Error('worker crashed') }, 300), { once: true })")>]
let private crashAfterFirstRequest () : unit = jsNative

if isWorkerScope () then
    crashAfterFirstRequest ()

let CrashingWorker: WorkerDef<unit, {| inWorker: bool |}> =
    createWorker
        (selfWorker ("./WebWorker.test.worker.crashing" + Compiler.extension))
        (fun () -> promise {
            do! Promise.sleep 1000
            return {| inWorker = isWorkerScope () |}
        })
