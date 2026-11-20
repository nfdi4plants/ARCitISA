module internal Swate.Components.Util.WebWorkerRtlTests

open Vitest
open Swate.Components.Util.WebWorkerSample

Vitest.afterEach (fun () -> RTL.cleanup ())

// jsdom has no Worker, so the hook dispatches to the handler on the main thread.
Vitest.test (
    "useWebWorker answers a request on the main thread when there is no Worker",
    fun () -> promise {
        RTL.render (Sample()) |> ignore

        RTL.fireEvent.click (RTL.screen.getByTestId "worker-run")

        do!
            RTL.waitFor (fun () ->
                Vitest.expect((RTL.screen.getByTestId "worker-result").getAttribute "data-in-worker").toBe "false"
            )
    }
)
