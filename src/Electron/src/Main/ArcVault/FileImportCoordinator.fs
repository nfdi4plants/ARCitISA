module Main.FileImportCoordinator

open Fable.Core
open Fable.Electron.Main
open Main
open Main.Bindings.Abort
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc

type ActiveFileImport = {
    State: ActiveFileImportState
    AbortController: IAbortController
    Completion: JS.Promise<Result<ImportExternalFilesResult, exn>>
}

let publishState (window: BrowserWindow) activeImport =
    WindowSend.send<IFileImportRendererApi>
        window
        (fun sender -> sender.fileImportStateUpdate (activeImport |> Option.map _.State))

let run
    (window: BrowserWindow)
    requestId
    canStart
    getActiveImport
    setActiveImport
    (operation: IAbortSignal -> JS.Promise<Result<ImportExternalFilesResult, exn>>)
    =
    match canStart (), getActiveImport () with
    | Error error, _ -> JS.Constructors.Promise.resolve (Error error)
    | Ok(), Some _ ->
        JS.Constructors.Promise.resolve (Error(exn "Another file import is already running in this window."))
    | Ok(), None ->
        let abortController = AbortController.create ()

        let completion = promise {
            try
                return! operation abortController.signal
            finally
                setActiveImport None
                publishState window None
        }

        let activeImport = {
            State = {
                requestId = requestId
                phase = FileImportPhase.Copying
                progress = {
                    step = FileImportStep.Scanning
                    completedItems = 0
                    totalItems = 0
                }
            }
            AbortController = abortController
            Completion = completion
        }

        setActiveImport (Some activeImport)
        publishState window (Some activeImport)
        completion

type ThrottledProgressPublisher = {
    Report: FileImportProgress -> unit
    Stop: unit -> unit
}

/// Limits how often progress reaches the renderer and taskbar. A step change or a finished step publishes at once. Other reports
/// inside the interval are held back, and one timer publishes the latest of them when the interval is over.
let createThrottledProgressPublisher (intervalMs: int) (publish: FileImportProgress -> unit) =
    let lastPublishedAt = ref -infinity
    let lastPublishedStep = ref None
    let latest = ref None
    let timer = ref None

    let stop () =
        timer.Value |> Option.iter JS.clearTimeout
        timer.Value <- None
        latest.Value <- None

    let publishNow (progress: FileImportProgress) =
        lastPublishedAt.Value <- Main.Bindings.Node.performanceNow ()
        lastPublishedStep.Value <- Some progress.step
        publish progress

    let report (progress: FileImportProgress) =
        let elapsedMs = Main.Bindings.Node.performanceNow () - lastPublishedAt.Value

        if
            lastPublishedStep.Value <> Some progress.step
            || progress.completedItems >= progress.totalItems
            || elapsedMs >= float intervalMs
        then
            stop ()
            publishNow progress
        else
            latest.Value <- Some progress

            if timer.Value.IsNone then
                timer.Value <-
                    Some(
                        JS.setTimeout
                            (fun () ->
                                timer.Value <- None
                                let pending = latest.Value
                                latest.Value <- None
                                pending |> Option.iter publishNow
                            )
                            (int (max 0.0 (min (float intervalMs) (float intervalMs - elapsedMs))))
                    )

    { Report = report; Stop = stop }
