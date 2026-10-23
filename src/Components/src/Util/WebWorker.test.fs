module internal Swate.Components.Util.WebWorkerTests

open Fable.Core
open Vitest
open Swate.Components.Util.WebWorker

// The unit project runs in Node, which has no Worker.
let private neverCreated: unit -> WorkerInstance =
    fun () -> failwith "No worker should be created"

let private twice (n: int) : JS.Promise<int> = promise { return n * 2 }

[<Emit("typeof globalThis.onmessage")>]
let private onMessageType () : string = jsNative

Vitest.describe (
    "WebWorker without a Worker",
    fun () ->
        Vitest.test (
            "createWorker registers nothing and returns a definition without a constructor",
            fun () ->
                let def = createWorker neverCreated twice

                Vitest.expect(onMessageType ()).toBe "undefined"
                Vitest.expect(def.Ctor.IsNone).toBe true
        )

        Vitest.test (
            "start runs the handler on the main thread",
            fun () -> promise {
                let client = start (createWorker neverCreated twice)
                let! result = client.Dispatch 21

                Vitest.expect(result).toBe 42
                Vitest.expect(client.InWorker).toBe false
            }
        )
)
