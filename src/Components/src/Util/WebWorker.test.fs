module internal Swate.Components.Util.WebWorkerTests

open Fable.Core
open Vitest
open Swate.Components.Util.WebWorker

// Node (unit project) and jsdom (Electron renderer tests) have no Worker.
let private neverCreated: unit -> WorkerInstance =
    fun () -> failwith "No worker should be created"

let private twice (n: int) : JS.Promise<int> = promise { return n * 2 }

[<Emit("globalThis.onmessage")>]
let private onMessage () : obj = jsNative

Vitest.describe (
    "WebWorker without a Worker",
    fun () ->
        Vitest.test (
            "createWorker registers nothing and returns a definition without a constructor",
            fun () ->
                let before = onMessage ()
                let def = createWorker neverCreated twice

                Vitest.expect(onMessage ()).toBe before
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

        Vitest.test (
            "start runs the handler on the main thread when the constructor throws",
            fun () -> promise {
                let client =
                    start {
                        Ctor = Some neverCreated
                        Handle = twice
                    }

                let! result = client.Dispatch 21

                Vitest.expect(result).toBe 42
                Vitest.expect(client.InWorker).toBe false
            }
        )
)
