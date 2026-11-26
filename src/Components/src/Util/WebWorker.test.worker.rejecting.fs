/// A worker whose handler rejects with a plain string, which has no message field. Like every worker file,
/// it stays free of React and DOM code.
module internal Arcitisa.Components.Util.WebWorkerRejectingWorker

open Fable.Core
open Arcitisa.Components.Util.WebWorker

let message = "handler rejected"

[<Emit("Promise.reject($0)")>]
let private rejectWith (value: string) : JS.Promise<{| inWorker: bool |}> = jsNative

let RejectingWorker: WorkerDef<unit, {| inWorker: bool |}> =
    createWorker (selfWorker ("./WebWorker.test.worker.rejecting" + Compiler.extension)) (fun () -> rejectWith message)
