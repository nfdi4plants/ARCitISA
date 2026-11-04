module ElectronCore.DependencyStateTests

open Fable.Core
open Main
open Main.VersionControl
open Main.VersionControl.DependencyState
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest

/// A check that runs only when the test says so. Every call to the check gets its own promise,
/// so a test can end the runs one after the other.
type private ControlledCheck() =
    let resolvers = ResizeArray<CheckReport -> unit>()
    let rejecters = ResizeArray<exn -> unit>()
    let mutable calls = 0

    member _.Calls = calls

    member _.Check(_cancellation: OperationCancellation) : JS.Promise<CheckReport> =
        calls <- calls + 1

        Promise.create (fun resolve reject ->
            resolvers.Add resolve
            rejecters.Add(fun error -> reject error)
        )

    /// Ends the call with the given index, counted from zero.
    member _.Finish(index: int, report: CheckReport) = resolvers[index]report

    member _.Fail(index: int, error: exn) = rejecters[index]error

let private status (name: string) (installed: bool) : DependencyStatusDto = {
    Component = name
    Installed = installed
    Version = None
    Compatible = installed
    Remediation = None
    ProbeExecutablePath = Some "/tools/git"
}

let private report (statuses: DependencyStatusDto[]) : CheckReport = { Statuses = statuses; Failure = None }

/// Lets the promise callbacks of the controller run.
let private settle () : JS.Promise<unit> = Promise.sleep 0

/// A deadline that no test reaches.
let private longDeadline = 60000.0

let private controllerWithDeadline
    (deadline: float)
    (check: OperationCancellation -> JS.Promise<CheckReport>)
    : DependencyStateController * ResizeArray<VersionControlDependencyStateDto> =
    let broadcasts = ResizeArray<VersionControlDependencyStateDto>()
    let mutable clock = 1000.0

    let controller =
        DependencyStateController(
            check,
            broadcasts.Add,
            (fun () ->
                clock <- clock + 1.0
                clock
            ),
            deadline
        )

    controller, broadcasts

let private controllerOver (check: ControlledCheck) =
    controllerWithDeadline longDeadline check.Check

/// A check that ends only when its cancellation fires. The result is the canceled failure that the
/// library reports for a killed process.
let private untilCanceled (cancellation: OperationCancellation) : JS.Promise<CheckReport> =
    Promise.create (fun resolve _ ->
        cancellation.Register(fun () ->
            resolve {
                Statuses = [||]
                Failure = Some "The dependency check was canceled."
            }
        )
    )

/// Waits longer than a deadline of a few milliseconds. The timers fire in the order of their expiry,
/// so the deadline has passed and its callbacks have run when this ends.
let private pastShortDeadline () : JS.Promise<unit> = Promise.sleep 60

Vitest.describe (
    "Dependency state",
    fun () ->
        Vitest.test (
            "a trigger starts one check, announces that it runs and announces its result",
            fun () -> promise {
                let check = ControlledCheck()
                let controller, broadcasts = controllerOver check

                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.CheckedAt).toEqual None

                let started = controller.Trigger()
                Vitest.expect(started.IsChecking).toBe true
                Vitest.expect(check.Calls).toBe 1
                Vitest.expect(broadcasts.Count).toBe 1
                Vitest.expect(broadcasts[0].IsChecking).toBe true

                let statuses = [| status "git" true; status "git-lfs" true |]
                check.Finish(0, report statuses)
                do! settle ()

                Vitest.expect(check.Calls).toBe 1
                Vitest.expect(broadcasts.Count).toBe 2
                Vitest.expect(broadcasts[1].IsChecking).toBe false
                Vitest.expect(broadcasts[1].Statuses).toEqual statuses
                Vitest.expect(broadcasts[1].Failure).toEqual None
                Vitest.expect(broadcasts[1].CheckedAt.IsSome).toBe true
                Vitest.expect(controller.Current).toEqual broadcasts[1]
            }
        )

        Vitest.test (
            "triggers during a run start no second check and queue exactly one more",
            fun () -> promise {
                let check = ControlledCheck()
                let controller, broadcasts = controllerOver check

                controller.Trigger() |> ignore
                controller.Trigger() |> ignore
                controller.Trigger() |> ignore
                Vitest.expect(check.Calls).toBe 1
                Vitest.expect(controller.Current.IsChecking).toBe true

                let first = [| status "git" false |]
                check.Finish(0, report first)
                do! settle ()

                // The queued run started. The result of the first run is announced while it runs.
                Vitest.expect(check.Calls).toBe 2
                Vitest.expect(controller.Current.IsChecking).toBe true
                Vitest.expect(controller.Current.Statuses).toEqual first
                Vitest.expect(broadcasts[broadcasts.Count - 1].IsChecking).toBe true

                controller.Trigger() |> ignore
                let second = [| status "git" true |]
                check.Finish(1, report second)
                do! settle ()

                // The trigger during the second run queued a third one.
                Vitest.expect(check.Calls).toBe 3
                check.Finish(2, report second)
                do! settle ()

                Vitest.expect(check.Calls).toBe 3
                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.Statuses).toEqual second
                Vitest.expect(broadcasts[broadcasts.Count - 1]).toEqual controller.Current
            }
        )

        Vitest.test (
            "the windows get every state in order and the last one equals the latest state",
            fun () -> promise {
                let check = ControlledCheck()
                let controller, broadcasts = controllerOver check

                controller.Trigger() |> ignore
                controller.Trigger() |> ignore
                check.Finish(0, report [| status "git" false |])
                do! settle ()
                check.Finish(1, report [| status "git" true |])
                do! settle ()

                let running = broadcasts |> Seq.map _.IsChecking |> Seq.toArray
                Vitest.expect(running).toEqual [| true; true; false |]
                Vitest.expect(broadcasts[0].CheckedAt).toEqual None
                Vitest.expect(broadcasts[2]).toEqual controller.Current
            }
        )

        Vitest.test (
            "a check that rejects or throws is a failed check and the next trigger runs again",
            fun () -> promise {
                let check = ControlledCheck()
                let controller, broadcasts = controllerOver check

                controller.Trigger() |> ignore
                check.Fail(0, exn "the probe broke")
                do! settle ()

                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.Statuses).toEqual [||]
                Vitest.expect(controller.Current.Failure.IsSome).toBe true
                Vitest.expect(controller.Current.Failure.Value.Contains "the probe broke").toBe true

                let throwing =
                    DependencyStateController(
                        (fun _ -> failwith "the probe threw"),
                        broadcasts.Add,
                        (fun () -> 5.0),
                        longDeadline
                    )

                throwing.Trigger() |> ignore
                do! settle ()
                Vitest.expect(throwing.Current.IsChecking).toBe false
                Vitest.expect(throwing.Current.Failure.IsSome).toBe true

                controller.Trigger() |> ignore
                Vitest.expect(check.Calls).toBe 2
                Vitest.expect(controller.Current.IsChecking).toBe true
            }
        )

        Vitest.test (
            "a check that never ends on its own is canceled at the deadline and ends as a failed check",
            fun () -> promise {
                let controller, broadcasts = controllerWithDeadline 10.0 untilCanceled

                let started = controller.Trigger()
                Vitest.expect(started.IsChecking).toBe true

                do! pastShortDeadline ()

                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.Statuses).toEqual [||]
                Vitest.expect(controller.Current.Failure.IsSome).toBe true
                // The message names the deadline, so it differs from the canceled result of the run.
                Vitest.expect(controller.Current.Failure.Value.Contains "0.01").toBe true
                Vitest.expect(controller.Current.CheckedAt.IsSome).toBe true
                Vitest.expect(broadcasts[broadcasts.Count - 1]).toEqual controller.Current
            }
        )

        Vitest.test (
            "a check that never returns, even after its cancellation, still ends at the deadline and its late result is ignored",
            fun () -> promise {
                let check = ControlledCheck()
                let controller, broadcasts = controllerWithDeadline 10.0 check.Check

                controller.Trigger() |> ignore
                do! pastShortDeadline ()

                // The first run never ended, yet the deadline settled it.
                Vitest.expect(check.Calls).toBe 1
                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.Statuses).toEqual [||]
                Vitest.expect(controller.Current.Failure.IsSome).toBe true
                Vitest.expect(controller.Current.CheckedAt.IsSome).toBe true

                // A result of the abandoned run changes nothing and starts nothing.
                let settled = controller.Current
                let announced = broadcasts.Count
                check.Finish(0, report [| status "git" true |])
                do! settle ()

                Vitest.expect(controller.Current).toEqual settled
                Vitest.expect(broadcasts.Count).toBe announced
                Vitest.expect(check.Calls).toBe 1

                // The next trigger starts a new run, and its result is kept.
                Vitest.expect(controller.Trigger().IsChecking).toBe true
                Vitest.expect(check.Calls).toBe 2

                let statuses = [| status "git" true |]
                check.Finish(1, report statuses)
                do! settle ()

                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.Failure).toEqual None
                Vitest.expect(controller.Current.Statuses).toEqual statuses
            }
        )

        Vitest.test (
            "a trigger queued behind a run that reaches its deadline starts the next run",
            fun () -> promise {
                let calls = ref 0

                let check (cancellation: OperationCancellation) =
                    calls.Value <- calls.Value + 1

                    if calls.Value = 1 then
                        untilCanceled cancellation
                    else
                        Promise.lift (report [| status "git" true |])

                let controller, _ = controllerWithDeadline 10.0 check

                controller.Trigger() |> ignore
                controller.Trigger() |> ignore
                do! pastShortDeadline ()

                // The deadline ended the first run, then the queued run found a working Git.
                Vitest.expect(calls.Value).toBe 2
                Vitest.expect(controller.Current.IsChecking).toBe false
                Vitest.expect(controller.Current.Failure).toEqual None
                Vitest.expect(controller.Current.Statuses.Length).toBe 1
            }
        )

        Vitest.test (
            "a check that ends before its deadline is not canceled and keeps its result",
            fun () -> promise {
                let canceled = ref false

                let check (cancellation: OperationCancellation) =
                    cancellation.Register(fun () -> canceled.Value <- true)
                    Promise.lift (report [| status "git" true |])

                let controller, _ = controllerWithDeadline 10.0 check

                controller.Trigger() |> ignore
                do! pastShortDeadline ()

                Vitest.expect(canceled.Value).toBe false
                Vitest.expect(controller.Current.Failure).toEqual None
                Vitest.expect(controller.Current.Statuses.Length).toBe 1
            }
        )

        Vitest.test (
            "a result with a provider failure keeps the components and names the failure",
            fun () ->
                let statuses = [|
                    {
                        Component = "git"
                        Installed = true
                        Version = Some "2.51.0"
                        Compatible = true
                        Remediation = None
                        ProbeExecutablePath = Some "/tools/git"
                    }
                |]

                let complete = DependencyState.reportOf (OperationResult.succeeded statuses)
                Vitest.expect(complete.Failure).toEqual None
                Vitest.expect(complete.Statuses.Length).toBe 1
                Vitest.expect(complete.Statuses[0].ProbeExecutablePath).toEqual (Some "/tools/git")

                let failure =
                    OperationFailure.create ProviderError "provider_broke" "The other provider broke."

                let partial =
                    DependencyState.reportOf (PartiallySucceeded(OperationOutcome.performed statuses, failure))

                Vitest.expect(partial.Statuses.Length).toBe 1
                Vitest.expect(partial.Failure).toEqual (Some "The other provider broke.")

                let failed = DependencyState.reportOf (Failed failure)
                Vitest.expect(failed.Statuses).toEqual [||]
                Vitest.expect(failed.Failure).toEqual (Some "The other provider broke.")
        )
)

/// Calls the install of a component through the version control api of a window. The only provider
/// answers with the given result. Returns the answer of the api and how many checks the call started.
let private installAndCountChecks (installResult: OperationResult<DependencyStatus>) = promise {
    let check = ControlledCheck()
    let controller, _ = controllerOver check

    let gitFactory =
        ProviderComposition.createGitFactory ElectronCore.TestHelpers.noAccounts WorkspaceSessionHost.windowOwnerOf

    let runtime: VersionControlRuntime.VersionControlRuntime = {
        Catalog =
            ProviderComposition.createCatalog [
                {
                    gitFactory with
                        InstallDependency = fun _ _ -> async { return installResult }
                }
            ]
        Bindings = ElectronCore.TestHelpers.memoryBindings ()
        PathCaseSensitivity = CaseInsensitive
        SessionDefaults = UserDefaults.builtInSessionDefaults
    }

    WorkspaceSessionHost.initialize (WorkspaceSessionHost.WorkspaceSessionHost(runtime))
    DependencyState.install controller

    try
        let api = Main.IPC.IVersionControlApi.api (ElectronCore.TestHelpers.ipcEvent 71)

        let! answer =
            api.installDependency {
                OperationId = "install-dependency"
                Component = "git"
            }

        let started = check.Calls

        // Ends the checks that the install started, so no deadline timer stays behind.
        for index in 0 .. started - 1 do
            check.Finish(index, report [||])

        do! settle ()
        return answer, started
    finally
        WorkspaceSessionHost.resetForTests ()
        DependencyState.resetForTests ()
}

let private installedGit: DependencyStatus = {
    Component = "git"
    Installed = true
    Version = Some "2.51.0"
    Compatible = true
    Remediation = None
    ProbeExecutablePath = None
}

let private installFailure =
    OperationFailure.create ProviderError "install_failed" "The fake install failed."

Vitest.describe (
    "Dependency check after an install",
    fun () ->
        Vitest.test (
            "a successful install starts a new check",
            fun () -> promise {
                let! answer, started = installAndCountChecks (OperationResult.succeeded installedGit)

                match answer with
                | Ok(OperationResultDto.Succeeded outcome) -> Vitest.expect(outcome.Value.Installed).toBe true
                | other -> failwith $"Expected a successful install, got {other}"

                Vitest.expect(started).toBe 1
            }
        )

        Vitest.test (
            "a partly successful install starts a new check",
            fun () -> promise {
                let! answer, started =
                    installAndCountChecks (PartiallySucceeded(OperationOutcome.performed installedGit, installFailure))

                match answer with
                | Ok(OperationResultDto.PartiallySucceeded(outcome, failure)) ->
                    Vitest.expect(outcome.Value.Installed).toBe true
                    Vitest.expect(failure.Code).toBe "install_failed"
                | other -> failwith $"Expected a partly successful install, got {other}"

                Vitest.expect(started).toBe 1
            }
        )

        Vitest.test (
            "a failed install starts no check",
            fun () -> promise {
                let! answer, started = installAndCountChecks (Failed installFailure)

                match answer with
                | Ok(OperationResultDto.Failed failure) -> Vitest.expect(failure.Code).toBe "install_failed"
                | other -> failwith $"Expected a failed install, got {other}"

                Vitest.expect(started).toBe 0
            }
        )
)
