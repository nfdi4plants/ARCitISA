module ElectronRenderer.GitSidebarPanelTests

open Fable.Core.JsInterop
open Renderer.Components.LeftSidebar.Git.GitSidebarPanel
open Renderer.Context.GitWorkflow
open Renderer.Context.LfsActivityContext
open Swate.Electron.Shared.VersionControlTypes
open Vitest

// The panel module reaches the IPC bridges through the Git state context. The preload is absent in tests,
// so the bridges are replaced by empty objects that no test calls.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcGitLabApi" ==> createObj []
            "ipcVersionControlApi" ==> createObj []
            "ipcArcVaultApi" ==> createObj []
            "ipcAuthApi" ==> createObj []
            "ipcTemplateApi" ==> createObj []
            "ipcValidationPackageApi" ==> createObj []
        ]
    )
)
|> ignore

let private runningAction
    (kind: LfsActionKind)
    (target: string)
    (progress: VersionControlProgressDto option)
    : LfsRunningAction =
    {
        OperationId = $"op-{target}"
        Kind = kind
        Target = target
        IsFolder = false
        FileCount = 1
        Progress = progress
    }

let private progressOf completed total : VersionControlProgressDto = {
    OperationId = "op"
    PhaseCode = "lfs-download"
    Item = None
    Completed = Some completed
    Total = Some total
    DisplayMessage = None
}

let private items =
    backgroundActivityItems [|
        runningAction LfsActionKind.Download "data/raw-data" (Some(progressOf 5.0 10.0))
        runningAction LfsActionKind.Free "notes/big.bin" None
    |]

Vitest.describe (
    "GitSidebarPanel background activities",
    fun () ->
        Vitest.test (
            "one item per running action names the kind and the file or folder name and carries the percent",
            fun () ->
                Vitest.expect(items.Length).toBe 2
                Vitest.expect(items.[0].Label.Contains "raw-data").toBe true
                Vitest.expect(items.[0].Label.Contains "data/").toBe false
                Vitest.expect(items.[1].Label.Contains "big.bin").toBe true
                Vitest.expect(items.[0].Label).not.toBe items.[1].Label
                Vitest.expect(items.[0].ProgressPercent).toEqual (Some 50.0)
                Vitest.expect(items.[1].ProgressPercent).toEqual None
        )

        Vitest.test (
            "the waiting headline names the first activity while Save, Download Changes or Upload Changes is busy without progress",
            fun () ->
                for operation in
                    [
                        GitBusyOperation.CommittingSelectedChanges
                        GitBusyOperation.CommittingAllChanges
                        GitBusyOperation.PullingFromRemote
                        GitBusyOperation.PushingToRemote
                    ] do
                    match waitingHeadline (Some operation) false items with
                    | Some headline ->
                        Vitest.expect(headline.Contains "raw-data").toBe true
                        Vitest.expect(headline.Contains "big.bin").toBe false
                    | None -> failwith $"Expected a waiting headline for {operation}."
        )

        Vitest.test (
            "the waiting headline stays unset when nothing is busy, no activity runs, progress arrived or another operation runs",
            fun () ->
                Vitest.expect(waitingHeadline None false items).toEqual None
                Vitest.expect(waitingHeadline (Some GitBusyOperation.CommittingAllChanges) false [||]).toEqual None
                Vitest.expect(waitingHeadline (Some GitBusyOperation.CommittingAllChanges) true items).toEqual None
                Vitest.expect(waitingHeadline (Some GitBusyOperation.SwitchingBranch) false items).toEqual None
        )
)
