module internal Swate.Components.Util.WebWorkerSample

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Util.WebWorker
open Swate.Components.Util.WebWorkerTestWorker

// The result is read from data-in-worker, which is empty until a request has been answered.
let private probe (def: WorkerDef<unit, {| inWorker: bool |}>) =
    let _, dispatch = useWebWorker def
    let answer, setAnswer = React.useState ""

    Html.div [
        Html.button [
            prop.testId "worker-run"
            prop.text "Run"
            prop.onClick (fun _ -> dispatch () |> Promise.iter (fun result -> setAnswer (string result.inWorker)))
        ]
        Html.output [
            prop.testId "worker-result"
            prop.custom ("data-in-worker", answer)
        ]
    ]

/// Dispatches to the probe worker.
[<ReactComponent>]
let Sample () = probe ProbeWorker

/// Dispatches to a probe worker whose script cannot load.
[<ReactComponent>]
let FallbackSample () = probe BrokenWorker

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
