module internal Swate.Components.Util.WebWorkerSample

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Util.WebWorker
open Swate.Components.Util.WebWorkerTestWorker
open Swate.Components.Util.WebWorkerRejectingWorker
open Swate.Components.Util.WebWorkerCrashingWorker

/// What the rejecting worker rejects with.
let rejectionMessage = WebWorkerRejectingWorker.message

// The result is read from data-in-worker and data-error, which are empty until a request has been answered.
let private probe (def: WorkerDef<unit, {| inWorker: bool |}>) =
    let _, dispatch = useWebWorker def
    let answer, setAnswer = React.useState ""
    let failure, setFailure = React.useState ""

    Html.div [
        Html.button [
            prop.testId "worker-run"
            prop.text "Run"
            prop.onClick (fun _ ->
                dispatch ()
                |> Promise.map (fun result -> setAnswer (string result.inWorker))
                |> Promise.catchEnd (fun error -> setFailure error.Message)
            )
        ]
        Html.output [
            prop.testId "worker-result"
            prop.custom ("data-in-worker", answer)
            prop.custom ("data-error", failure)
        ]
    ]

/// Dispatches to the probe worker.
[<ReactComponent>]
let Sample () = probe ProbeWorker

/// Dispatches to a probe worker whose script cannot load.
[<ReactComponent>]
let FallbackSample () = probe BrokenWorker

/// Dispatches to a worker whose handler rejects.
[<ReactComponent>]
let RejectSample () = probe RejectingWorker

/// Dispatches to a worker that fails while the request is pending.
[<ReactComponent>]
let CrashSample () = probe CrashingWorker

/// A Sample that a button mounts and unmounts.
[<ReactComponent>]
let MountSample () =
    let mounted, setMounted = React.useState true

    Html.div [
        Html.button [
            prop.testId "worker-toggle"
            prop.text "Toggle"
            prop.onClick (fun _ -> setMounted (not mounted))
        ]
        if mounted then
            Sample()
    ]
