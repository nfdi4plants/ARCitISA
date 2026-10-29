module ElectronCore.VersionControlProgressThrottleTests

open Main.IPC.IVersionControlApi
open Swate.Electron.Shared.VersionControlTypes
open Vitest

/// A throttle on a manual clock. Timers fire only when the test advances the time.
type private Harness() =
    let mutable time = 0.0
    let timers = ResizeArray<float * (unit -> unit) * bool ref>()
    let forwarded = ResizeArray<string * float>()

    let throttle =
        ProgressThrottle(
            (fun progress -> forwarded.Add((progress.PhaseCode, progress.Completed.Value))),
            (fun () -> time),
            (fun delay callback ->
                let cancelled = ref false
                timers.Add((time + float delay, callback, cancelled))
                fun () -> cancelled.Value <- true
            ),
            100
        )

    member _.Report(phase: string, completed: float) =
        throttle.Report {
            OperationId = "op-1"
            PhaseCode = phase
            Item = None
            Completed = Some completed
            Total = Some 100.0
            DisplayMessage = None
        }

    member _.Flush() = throttle.Flush()
    member _.Forwarded = forwarded.ToArray()

    member _.Advance(milliseconds: float) =
        time <- time + milliseconds

        for due, callback, cancelled in timers.ToArray() do
            if due <= time && not cancelled.Value then
                cancelled.Value <- true
                callback ()

Vitest.describe (
    "Version control progress throttle",
    fun () ->
        Vitest.test (
            "a burst of same-phase events inside the window forwards the first and the last",
            fun () ->
                let harness = Harness()

                harness.Report("lfs-upload", 1.0)
                harness.Advance 10.0
                harness.Report("lfs-upload", 2.0)
                harness.Advance 10.0
                harness.Report("lfs-upload", 3.0)
                harness.Advance 10.0
                harness.Report("lfs-upload", 4.0)

                Vitest.expect(harness.Forwarded).toEqual [| ("lfs-upload", 1.0) |]

                harness.Advance 100.0

                Vitest.expect(harness.Forwarded).toEqual [| ("lfs-upload", 1.0); ("lfs-upload", 4.0) |]
        )

        Vitest.test (
            "a phase change forwards at once and drops the older pending event",
            fun () ->
                let harness = Harness()

                harness.Report("lfs-list", 1.0)
                harness.Advance 10.0
                harness.Report("lfs-list", 2.0)
                harness.Advance 10.0
                harness.Report("lfs-upload", 3.0)

                Vitest.expect(harness.Forwarded).toEqual [| ("lfs-list", 1.0); ("lfs-upload", 3.0) |]

                harness.Advance 200.0

                Vitest.expect(harness.Forwarded).toEqual [| ("lfs-list", 1.0); ("lfs-upload", 3.0) |]
        )

        Vitest.test (
            "flush forwards the pending event once and no timer sends it again",
            fun () ->
                let harness = Harness()

                harness.Report("lfs-upload", 1.0)
                harness.Advance 10.0
                harness.Report("lfs-upload", 2.0)

                harness.Flush()

                Vitest.expect(harness.Forwarded).toEqual [| ("lfs-upload", 1.0); ("lfs-upload", 2.0) |]

                harness.Flush()
                harness.Advance 200.0

                Vitest.expect(harness.Forwarded).toEqual [| ("lfs-upload", 1.0); ("lfs-upload", 2.0) |]
        )
)
