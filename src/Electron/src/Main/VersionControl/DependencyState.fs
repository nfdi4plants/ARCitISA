/// The main process owns the version control dependency check, so every window sees one answer.
/// A check runs when the app starts and whenever a window asks for one. The in-app install of a
/// component starts another one when it succeeds. The module keeps the latest result and tells every
/// window when it changes.
module Main.VersionControl.DependencyState

open System
open Fable.Core
open Fable.Electron.Main
open Main.Bindings.PromiseRace
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions

/// Asks every registered provider for its components. A provider whose check fails does not hide
/// the others: its failure rides along as the partial failure. The renderer's install flow and the
/// state of this module share the function.
let checkDependencies
    (catalog: ProviderResolver.ProviderCatalog)
    (context: OperationContext)
    : Async<OperationResult<DependencyStatus[]>> =
    async {
        let factories = ProviderResolver.factories catalog
        let mutable statuses: DependencyStatus[] = [||]
        let mutable failure: OperationFailure option = None

        for factory in factories do
            if not (context.Cancellation.IsCancellationRequested()) then
                let! reported = factory.CheckDependencies context

                match reported with
                | Succeeded outcome
                | PartiallySucceeded(outcome, _) -> statuses <- Array.append statuses outcome.Value
                | Failed error when error.Category = Canceled -> failure <- Some error
                | Failed error ->
                    if failure.IsNone then
                        failure <- Some error

        return
            match failure with
            | Some error when error.Category = Canceled -> Failed error
            | _ when context.Cancellation.IsCancellationRequested() ->
                OperationResult.canceled "The dependency check was canceled."
            | Some error ->
                PartiallySucceeded(
                    OperationOutcome.performed statuses,
                    {
                        error with
                            StateChanged = false
                            RecoveryAction =
                                Some {
                                    Code = VersionControlCodes.Recovery.CheckDependencies
                                    Instructions = Some "One provider could not report its dependencies."
                                }
                    }
                )
            | None -> OperationResult.succeeded statuses
    }

/// What one finished check reports.
type CheckReport = {
    Statuses: DependencyStatusDto[]
    Failure: string option
}

/// A partial result keeps the components that were reported and names the provider failure.
let reportOf (result: OperationResult<DependencyStatus[]>) : CheckReport =
    let mapped (statuses: DependencyStatus[]) =
        Array.map Mappings.dependencyStatus statuses

    match result with
    | Succeeded outcome -> {
        Statuses = mapped outcome.Value
        Failure = None
      }
    | PartiallySucceeded(outcome, failure) -> {
        Statuses = mapped outcome.Value
        Failure = Some failure.Message
      }
    | Failed failure -> {
        Statuses = [||]
        Failure = Some failure.Message
      }

let private notChecked: VersionControlDependencyStateDto = {
    Statuses = [||]
    Failure = None
    IsChecking = false
    CheckedAt = None
}

/// A check that throws or rejects is a failed check, so the flag that marks a running check always clears.
let private safeCheck (check: unit -> JS.Promise<CheckReport>) : JS.Promise<CheckReport> = promise {
    try
        return! check ()
    with error ->
        return {
            Statuses = [||]
            Failure = Some error.Message
        }
}

/// What a run reports when it reached its deadline.
let private timedOutReport (deadlineMilliseconds: float) : CheckReport = {
    Statuses = [||]
    Failure = Some $"The Git check did not finish within {deadlineMilliseconds / 1000.0} seconds."
}

/// Keeps the latest result of the check and whether one runs. A trigger while a check runs queues
/// exactly one more run for when the first ends. At the deadline the run is canceled and ends as a
/// failed check at once, even when the check never returns, so the flag that marks a running check
/// clears. A result that arrives after that is ignored. Every change of the state goes to `broadcast`,
/// which carries it to the windows.
type DependencyStateController
    (
        check: OperationCancellation -> JS.Promise<CheckReport>,
        broadcast: VersionControlDependencyStateDto -> unit,
        now: unit -> float,
        deadlineMilliseconds: float
    ) =
    let mutable state = notChecked
    let mutable running = false
    let mutable rerunRequested = false

    let rec launch () =
        let source = OperationCancellation.Source()
        let timer = JS.setTimeout (fun () -> source.Cancel()) (int deadlineMilliseconds)

        // Settles when the run is canceled. Killing a launcher process can leave a child holding its
        // pipes, so the check may never return after the cancellation.
        let deadline: JS.Promise<CheckReport> =
            Promise.create (fun resolve _ ->
                source.Cancellation.Register(fun () -> resolve (timedOutReport deadlineMilliseconds))
            )

        // The deadline registers before the check starts, so it settles first when the cancellation
        // also ends the check.
        race [|
            safeCheck (fun () -> check source.Cancellation)
            deadline
        |]
        |> Promise.iter (fun report ->
            JS.clearTimeout timer
            settle report
        )

    /// Keeps the result, then starts the queued run or reports the end. The state is set before
    /// anything is sent, so a failing broadcast leaves the controller ready for the next trigger.
    and settle (report: CheckReport) =
        let queued = rerunRequested
        rerunRequested <- false
        running <- queued

        state <- {
            Statuses = report.Statuses
            Failure = report.Failure
            IsChecking = queued
            CheckedAt = Some(now ())
        }

        let announced = state

        if queued then
            launch ()

        broadcast announced

    member _.Current: VersionControlDependencyStateDto = state

    /// Starts a check, or queues one more run when a check runs. Answers with the state right away.
    member _.Trigger() : VersionControlDependencyStateDto =
        if running then
            rerunRequested <- true
        else
            running <- true
            state <- { state with IsChecking = true }
            let announced = state
            launch ()
            broadcast announced

        state

let private broadcastToWindows (state: VersionControlDependencyStateDto) =
    BrowserWindow.getAllWindows ()
    |> Array.iter (fun window ->
        Main.WindowSend.send<IVersionControlDependencyRendererApi>
            window
            (fun api -> api.versionControlDependenciesChanged state)
    )

let private nowMilliseconds () : float = JS.Constructors.Date.now ()

/// How long one run may take. A run that takes longer is canceled, which kills its Git process.
let private checkDeadlineMilliseconds = 30000.0

/// One check without a window. No window owns it, so it needs no entry in the operation registry of
/// the session host. Only the deadline of the controller cancels it.
let private checkWith
    (catalog: ProviderResolver.ProviderCatalog)
    (cancellation: OperationCancellation)
    : JS.Promise<CheckReport> =
    promise {
        let context =
            OperationContext.create (Guid.NewGuid().ToString()) cancellation ignore

        let! result = checkDependencies catalog context |> Async.StartAsPromise
        return reportOf result
    }

let mutable private installed: DependencyStateController option = None

/// Makes the controller the state of the process. Tests install one over a fake check.
let install (controller: DependencyStateController) : unit = installed <- Some controller

/// Clears the controller between tests.
let resetForTests () : unit = installed <- None

/// Creates the controller over the real check and runs the first check. Call it once the runtime exists.
let initialize (catalog: ProviderResolver.ProviderCatalog) : unit =
    let controller =
        DependencyStateController(checkWith catalog, broadcastToWindows, nowMilliseconds, checkDeadlineMilliseconds)

    install controller
    controller.Trigger() |> ignore

/// Checks again, or queues one more run behind the running check. Does nothing before `initialize`.
let trigger () : unit =
    installed |> Option.iter (fun controller -> controller.Trigger() |> ignore)

/// The state a window gets when version control did not start, so no check can run.
let private unavailable () : VersionControlDependencyStateDto = {
    Statuses = [||]
    Failure = Some "Version control did not start, so its dependencies cannot be checked."
    IsChecking = false
    CheckedAt = Some(nowMilliseconds ())
}

let getState () : VersionControlDependencyStateDto =
    match installed with
    | Some controller -> controller.Current
    | None -> unavailable ()

let recheck () : VersionControlDependencyStateDto =
    match installed with
    | Some controller -> controller.Trigger()
    | None -> unavailable ()
