module ElectronRenderer.ArcSyncStateTests

open Renderer.Context.ArcSyncContext
open Vitest

Vitest.test (
    "a finished sync clears an earlier failure",
    fun () ->
        let state =
            ArcSyncState.init () |> ArcSyncState.failed "boom" |> ArcSyncState.finished [||]

        Vitest.expect(state.FailedMessage).toEqual None
        Vitest.expect(state.IsSyncing).toBe false
)

Vitest.test (
    "a finished sync with no replaced files keeps the earlier list until it is dismissed",
    fun () ->
        let state =
            ArcSyncState.init ()
            |> ArcSyncState.finished [| "assays/A/isa.assay.xlsx" |]
            |> ArcSyncState.finished [||]

        Vitest.expect(state.ReplacedUnsaved).toEqual [| "assays/A/isa.assay.xlsx" |]
)

Vitest.test (
    "a started sync clears the failure and shows it runs",
    fun () ->
        let state =
            ArcSyncState.init () |> ArcSyncState.failed "boom" |> ArcSyncState.started

        Vitest.expect(state.FailedMessage).toEqual None
        Vitest.expect(state.IsSyncing).toBe true
)

Vitest.test (
    "replaced lists add up without repeating a file",
    fun () ->
        let state =
            ArcSyncState.init ()
            |> ArcSyncState.finished [| "assays/A/isa.assay.xlsx" |]
            |> ArcSyncState.finished [| "assays/B/isa.assay.xlsx"; "assays/A/isa.assay.xlsx" |]

        Vitest.expect(state.ReplacedUnsaved).toEqual [| "assays/A/isa.assay.xlsx"; "assays/B/isa.assay.xlsx" |]
)
