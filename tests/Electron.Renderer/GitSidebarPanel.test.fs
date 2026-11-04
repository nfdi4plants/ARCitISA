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
    StartedAt = 500.0
}

let private folderAction = {
    runningAction LfsActionKind.Download "data/raw-data" true with
        Count = Some(5, 10)
        StepPercent = Some 45.5
}

let private fileAction = runningAction LfsActionKind.Free "notes/big.bin" false

let private items =
    backgroundActivityItems [| folderAction; fileAction |] LfsRunningLogs.empty

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
            "a folder item carries the progress text and the step percent of its action",
            fun () ->
                Vitest.expect(items.[0].Detail).toEqual (LfsRunningAction.progressText folderAction)
                Vitest.expect(items.[0].ProgressPercent).toEqual (Some 45.5)
        )

        Vitest.test (
            "a single file item carries no detail and no percent, even with a step percent",
            fun () ->
                let metered = {
                    fileAction with
                        StepPercent = Some 45.5
                }

                let item = (backgroundActivityItems [| metered |] LfsRunningLogs.empty).[0]

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
                            Text = "git lfs fetch"
                            IsPartial = false
                        }
                    |]
                }

                // Each action takes the log of its own operation id.
                let otherOutput = {
                    output with
                        OperationId = fileAction.OperationId
                        Entries = [|
                            {
                                CommandId = "cmd-2"
                                Kind = "command"
                                Text = "git lfs checkout"
                                IsPartial = false
                            }
                        |]
                }

                let logs =
                    LfsRunningLogs.empty
                    |> LfsRunningLogs.append output
                    |> LfsRunningLogs.append otherOutput

                let withLogs = backgroundActivityItems [| folderAction; fileAction |] logs

                let linesOf (item: Swate.Components.Page.GitSidebarTypes.GitSidebarBackgroundActivity) =
                    item.Log.Value.Lines |> Array.map (fun line -> line.Kind, line.Text)

                Vitest.expect(linesOf withLogs.[0]).toEqual [| "command", "git lfs fetch" |]
                Vitest.expect(linesOf withLogs.[1]).toEqual [| "command", "git lfs checkout" |]
                Vitest.expect(withLogs.[0].Log.Value.Omitted).toBe 0
        )

        // The operation started at 1000. The actions above started at 500, so they hold the lock.
        let startedAt = Some 1000.0

        Vitest.test (
            "the waiting headline names the first earlier action while a write that takes the mutation lock is busy without events",
            fun () ->
                for operation in
                    [
                        GitBusyOperation.CommittingSelectedChanges
                        GitBusyOperation.CommittingAllChanges
                        GitBusyOperation.PullingFromRemote
                        GitBusyOperation.PushingToRemote
                        GitBusyOperation.DiscardingSelectedChanges
                        GitBusyOperation.CreatingBranch
                        GitBusyOperation.SwitchingBranch
                        GitBusyOperation.ConfirmingMergeResolution "notes/a.md"
                        GitBusyOperation.FinalizingMerge
                        GitBusyOperation.AbandoningMerge
                        GitBusyOperation.RestoringInterruptedPaths
                        GitBusyOperation.RetryingMaterialization
                    ] do
                    match waitingHeadline (Some operation) false startedAt [| folderAction; fileAction |] with
                    | Some headline ->
                        Vitest.expect(headline.Contains "raw-data").toBe true
                        Vitest.expect(headline.Contains "big.bin").toBe false
                    | None -> failwith $"Expected a waiting headline for {operation}."
        )

        Vitest.test (
            "the waiting headline stays unset when nothing is busy, no action runs or the operation reported",
            fun () ->
                let actions = [| folderAction; fileAction |]
                let saving = Some GitBusyOperation.CommittingAllChanges

                Vitest.expect(waitingHeadline None false startedAt actions).toEqual None
                Vitest.expect(waitingHeadline saving false startedAt [||]).toEqual None
                Vitest.expect(waitingHeadline saving true startedAt actions).toEqual None
                Vitest.expect(waitingHeadline saving false None actions).toEqual None
        )

        Vitest.test (
            "the waiting headline stays unset for busy operations that do not take the mutation lock",
            fun () ->
                for operation in
                    [
                        GitBusyOperation.Refreshing
                        GitBusyOperation.FetchingFromRemote
                        GitBusyOperation.CloningRepository "C:/arcs/clone"
                        GitBusyOperation.InitializingRepository
                        GitBusyOperation.SavingGitLfsThreshold
                        GitBusyOperation.SavingDiffIndexingLimit
                        GitBusyOperation.SavingGitLfsDownloadPreference
                        GitBusyOperation.PruningGitLfsCache
                        GitBusyOperation.DeduplicatingGitLfsStorage
                        GitBusyOperation.InstallingDependency "git-lfs"
                        GitBusyOperation.RenamingRepository
                        GitBusyOperation.ClearingStaleLock
                    ] do
                    Vitest
                        .expect(waitingHeadline (Some operation) false startedAt [| folderAction; fileAction |])
                        .toEqual
                        None
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

let private finishedLog
    (arcName: string option)
    (texts: string[])
    : Renderer.Context.OperationLog.FinishedOperationLog =
    let entries: Swate.Electron.Shared.VersionControlTypes.VersionControlOutputEntryDto[] =
        texts
        |> Array.map (fun text -> {
            CommandId = "cmd-1"
            Kind = "stdout"
            Text = text
            IsPartial = false
        })

    {
        Label = "Saving changes"
        ArcName = arcName
        FinishedAt = 1700000000000.0
        Log = Renderer.Context.OperationLog.append entries Renderer.Context.OperationLog.empty
    }

Vitest.describe (
    "GitSidebarPanel last log",
    fun () ->
        Vitest.test (
            "a finished log with lines gets a title with the label and the ARC name and keeps its lines",
            fun () ->
                let mapped =
                    sidebarLastLog (finishedLog (Some "my-arc") [| "first line"; "second line" |])

                Vitest.expect(mapped.IsSome).toBe true
                Vitest.expect(mapped.Value.Title.StartsWith "Saving changes").toBe true
                Vitest.expect(mapped.Value.Title.Contains "my-arc").toBe true
                Vitest.expect(mapped.Value.Log.Lines |> Array.map _.Text).toEqual [| "first line"; "second line" |]
        )

        Vitest.test (
            "a finished log without an ARC name has no ARC part in its title",
            fun () ->
                let mapped = sidebarLastLog (finishedLog None [| "first line" |])

                Vitest.expect(mapped.Value.Title.Split([| " · " |], System.StringSplitOptions.None).Length).toBe 2
                Vitest.expect(mapped.Value.Title.StartsWith "Saving changes").toBe true
        )

        Vitest.test (
            "the title ends with the local time of the end as hours and minutes",
            fun () ->
                let mapped = sidebarLastLog (finishedLog (Some "my-arc") [| "first line" |])

                Vitest
                    .expect(System.Text.RegularExpressions.Regex.IsMatch(mapped.Value.Title, @"^.* · \d{2}:\d{2}$"))
                    .toBe
                    true
        )

        Vitest.test (
            "a finished log without lines maps to None",
            fun () -> Vitest.expect(sidebarLastLog (finishedLog (Some "my-arc") [||])).toEqual None
        )
)
