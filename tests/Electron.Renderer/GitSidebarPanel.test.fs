module ElectronRenderer.GitSidebarPanelTests

open Fable.Core.JsInterop
open Renderer.Components.LeftSidebar.Git.GitSidebarPanel
open Renderer.Context.GitWorkflow
open Renderer.Context.LfsActivityContext
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

let private runningAction (kind: LfsActionKind) (target: string) (isFolder: bool) : LfsRunningAction = {
    OperationId = $"op-{target}"
    Kind = kind
    Target = target
    IsFolder = isFolder
    FileCount = 10
    Count = None
    StepPercent = None
    Log = Renderer.Context.OperationLog.empty
    StartedAt = 500.0
}

let private folderAction = {
    runningAction LfsActionKind.Download "data/raw-data" true with
        Count = Some(5, 10)
        StepPercent = Some 45.5
}

let private fileAction = runningAction LfsActionKind.Free "notes/big.bin" false

let private items = backgroundActivityItems [| folderAction; fileAction |]

Vitest.describe (
    "GitSidebarPanel background activities",
    fun () ->
        Vitest.test (
            "one item per running action names the kind and the file or folder name",
            fun () ->
                Vitest.expect(items.Length).toBe 2
                Vitest.expect(items.[0].Label.Contains "raw-data").toBe true
                Vitest.expect(items.[0].Label.Contains "data/").toBe false
                Vitest.expect(items.[1].Label.Contains "big.bin").toBe true
                Vitest.expect(items.[0].Label).not.toBe items.[1].Label
        )

        Vitest.test (
            "a folder item carries the progress text and the share of finished files",
            fun () ->
                Vitest.expect(items.[0].Detail).toEqual (LfsRunningAction.progressText folderAction)
                Vitest.expect(items.[0].ProgressPercent).toEqual (Some 50.0)
        )

        Vitest.test (
            "a single file item carries no detail and no percent, even with a step percent",
            fun () ->
                let metered = {
                    fileAction with
                        StepPercent = Some 45.5
                }

                let item = (backgroundActivityItems [| metered |]).[0]

                Vitest.expect(item.ProgressPercent).toEqual None
                Vitest.expect(item.Detail).toEqual None
        )

        Vitest.test (
            "a background item carries the lines of its action log and no log while the action has no output",
            fun () ->
                Vitest.expect(items.[0].Log).toEqual None

                let output: Swate.Electron.Shared.VersionControlTypes.VersionControlOutputDto = {
                    OperationId = folderAction.OperationId
                    Entries = [|
                        {
                            CommandId = "cmd-1"
                            Kind = "command"
                            Text = "git lfs pull"
                            IsPartial = false
                        }
                    |]
                }

                let withOutput = {
                    folderAction with
                        Log = Renderer.Context.OperationLog.append output.Entries folderAction.Log
                }

                let log = (backgroundActivityItems [| withOutput |]).[0].Log

                Vitest.expect(log.Value.Lines |> Array.map (fun line -> line.Kind, line.Text)).toEqual [|
                    "command", "git lfs pull"
                |]

                Vitest.expect(log.Value.Omitted).toBe 0
        )

        // The operation started at 1000. The actions above started at 500, so they hold the lock.
        let startedAt = Some 1000.0

        Vitest.test (
            "the waiting headline names the first earlier action while Save, Download Changes or Upload Changes is busy without events",
            fun () ->
                for operation in
                    [
                        GitBusyOperation.CommittingSelectedChanges
                        GitBusyOperation.CommittingAllChanges
                        GitBusyOperation.PullingFromRemote
                        GitBusyOperation.PushingToRemote
                    ] do
                    match waitingHeadline (Some operation) false startedAt [| folderAction; fileAction |] with
                    | Some headline ->
                        Vitest.expect(headline.Contains "raw-data").toBe true
                        Vitest.expect(headline.Contains "big.bin").toBe false
                    | None -> failwith $"Expected a waiting headline for {operation}."
        )

        Vitest.test (
            "the waiting headline stays unset when nothing is busy, no action runs, the operation reported or another operation runs",
            fun () ->
                let actions = [| folderAction; fileAction |]
                let saving = Some GitBusyOperation.CommittingAllChanges

                Vitest.expect(waitingHeadline None false startedAt actions).toEqual None
                Vitest.expect(waitingHeadline saving false startedAt [||]).toEqual None
                Vitest.expect(waitingHeadline saving true startedAt actions).toEqual None

                Vitest.expect(waitingHeadline (Some GitBusyOperation.SwitchingBranch) false startedAt actions).toEqual
                    None

                Vitest.expect(waitingHeadline saving false None actions).toEqual None
        )

        Vitest.test (
            "the waiting headline names only actions that started before the operation",
            fun () ->
                let later = { fileAction with StartedAt = 1500.0 }
                let saving = Some GitBusyOperation.CommittingAllChanges

                Vitest.expect(waitingHeadline saving false startedAt [| later |]).toEqual None

                match waitingHeadline saving false startedAt [| folderAction; later |] with
                | Some headline ->
                    Vitest.expect(headline.Contains "raw-data").toBe true
                    Vitest.expect(headline.Contains "big.bin").toBe false
                | None -> failwith "Expected a waiting headline for the earlier action."
        )
)
