module ElectronCore.WorkflowPayloadsTests

open ARCtrl
open ARCtrl.Contract
open ARCtrl.CWL
open Main.ARCtrlExtensions
open Main.ArcMerge
open Main.ArcVaultHelper
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Vitest

let private description label =
    CWLProcessingUnit.CommandLineTool(CWLToolDescription(ResizeArray(), label = label))

let private setLabel label =
    function
    | CWLProcessingUnit.CommandLineTool tool -> tool.Label <- Some label
    | _ -> failwith "Expected a command line tool."

let private createArc () =
    let arc = ARC("Workflow Payloads")
    let workflow = ArcWorkflow("Analysis", cwlDescription = description "Workflow")
    let run = ArcRun("Execution", cwlDescription = description "Run")
    run.CWLInput.Add(CWLParameterReference("message", value = CWLParameterValue.String "hello"))
    arc.AddWorkflow workflow
    arc.AddRun run
    arc, workflow, run

let private contractPaths (contracts: Contract[]) = contracts |> Array.map _.Path

let private contractText (contract: Contract) = contract.DTO.Value.AsText()

Vitest.describe (
    "workflow payload contracts and saved baselines",
    fun () ->
        Vitest.test (
            "present workflow CWL, run CWL and inputs use canonical paths and ARCtrl serialization",
            fun () ->
                let arc, workflow, run = createArc ()
                let contracts = WorkflowPayloads.workflowPayloadContracts arc Operation.CREATE

                Vitest.expect(contractPaths contracts).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                    "runs/Execution/run.cwl"
                    "runs/Execution/run.yml"
                |]

                Vitest.expect(contracts |> Array.map _.Operation).toEqual [|
                    Operation.CREATE
                    Operation.CREATE
                    Operation.CREATE
                |]

                Vitest.expect(contracts |> Array.map _.DTOType).toEqual [|
                    Some DTOType.CWL
                    Some DTOType.CWL
                    Some DTOType.YAML
                |]

                Vitest.expect(contracts |> Array.map contractText).toEqual [|
                    CWL.Encode.encodeProcessingUnit workflow.CWLDescription.Value
                    CWL.Encode.encodeProcessingUnit run.CWLDescription.Value
                    CWL.Encode.encodeYAMLParameterFile run.CWLInput
                |]
        )

        Vitest.test (
            "per-entity generation includes inputs without a run description",
            fun () ->
                let _, workflow, run = createArc ()
                run.CWLDescription <- None

                Vitest.expect(WorkflowPayloads.workflowContracts workflow Operation.UPDATE |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                |]

                Vitest.expect(WorkflowPayloads.runContracts run Operation.UPDATE |> contractPaths).toEqual [|
                    "runs/Execution/run.yml"
                |]
        )

        Vitest.test (
            "present payloads without saved snapshots produce updates",
            fun () ->
                let arc, workflow, run = createArc ()
                let contracts = WorkflowPayloads.changedWorkflowPayloadContracts arc
                Vitest.expect(contracts.Length).toBe 3

                Vitest
                    .expect(
                        contracts
                        |> Array.forall (fun contract -> contract.Operation = Operation.UPDATE)
                    )
                    .toBe
                    true

                Vitest.expect(WorkflowPayloads.workflowHasPayloadChanges workflow).toBe true
                Vitest.expect(WorkflowPayloads.runHasPayloadChanges run).toBe true
        )

        Vitest.test (
            "baselining records current payload text and repeated checks are a no-op",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc

                for _ in 1..2 do
                    Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc).toEqual [||]
                    Vitest.expect(workflow.hasInMemoryChanges ()).toBe false
                    Vitest.expect(run.hasInMemoryChanges ()).toBe false
                    Vitest.expect(arc.hasInMemoryChanges ()).toBe false

                // Equivalent replacement objects still serialize to the saved value.
                workflow.CWLDescription <- Some(description "Workflow")
                run.CWLInput.[0] <- CWLParameterReference("message", value = CWLParameterValue.String "hello")
                Vitest.expect(arc.hasInMemoryChanges ()).toBe false
        )

        Vitest.test (
            "workflow-only nested CWL mutation dirties the entity and ARC",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc
                let staticHash = workflow.StaticHash
                setLabel "Changed workflow" workflow.CWLDescription.Value

                Vitest.expect(workflow.GetLightHashCode()).toBe staticHash
                Vitest.expect(workflow.hasInMemoryChanges ()).toBe true
                Vitest.expect(run.hasInMemoryChanges ()).toBe false
                Vitest.expect(arc.hasInMemoryChanges ()).toBe true

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                |]
        )

        Vitest.test (
            "run CWL and input-only mutations produce their respective updates",
            fun () ->
                let arc, _, run = createArc ()
                baselineArcStaticHashes arc
                setLabel "Changed run" run.CWLDescription.Value
                run.CWLInput.[0].Value <- Some(CWLParameterValue.String "changed input")

                Vitest.expect(run.GetLightHashCode()).toBe run.StaticHash
                Vitest.expect(run.hasInMemoryChanges ()).toBe true
                Vitest.expect(arc.hasInMemoryChanges ()).toBe true

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc |> contractPaths).toEqual [|
                    "runs/Execution/run.cwl"
                    "runs/Execution/run.yml"
                |]
        )

        Vitest.test (
            "nested mutable input values cannot mutate the saved snapshot",
            fun () ->
                let arc, _, run = createArc ()
                let field = CWLParameterRecordField("threshold", CWLParameterValue.String "before")
                let values = ResizeArray [ CWLParameterValue.Record(ResizeArray [ field ]) ]
                run.CWLInput.[0].Value <- Some(CWLParameterValue.Array values)
                baselineArcStaticHashes arc
                field.Value <- CWLParameterValue.String "after"

                let contracts = WorkflowPayloads.changedWorkflowPayloadContracts arc
                Vitest.expect(contractPaths contracts).toEqual [| "runs/Execution/run.yml" |]
                Vitest.expect(contractText contracts.[0]).toBe (CWL.Encode.encodeYAMLParameterFile run.CWLInput)
                Vitest.expect(run.hasInMemoryChanges ()).toBe true

                field.Value <- CWLParameterValue.String "before"
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc).toEqual [||]
                Vitest.expect(arc.hasInMemoryChanges ()).toBe false
        )

        Vitest.test (
            "absent descriptions and cleared inputs leave existing files untouched and do not dirty the ARC",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc
                workflow.CWLDescription <- None
                run.CWLDescription <- None
                run.CWLInput.Clear()

                Vitest.expect(WorkflowPayloads.workflowPayloadContracts arc Operation.CREATE).toEqual [||]
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc).toEqual [||]
                Vitest.expect(arc.hasInMemoryChanges ()).toBe false

                workflow.CWLDescription <- Some(description "Workflow")
                run.CWLDescription <- Some(description "Run")
                run.CWLInput.Add(CWLParameterReference("message", value = CWLParameterValue.String "hello"))
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc).toEqual [||]
                Vitest.expect(arc.hasInMemoryChanges ()).toBe false
        )

        Vitest.test (
            "new payloads added after an empty baseline are changes",
            fun () ->
                let arc = ARC("Empty payload baseline")
                let workflow = ArcWorkflow("Analysis")
                let run = ArcRun("Execution")
                arc.AddWorkflow workflow
                arc.AddRun run
                baselineArcStaticHashes arc
                workflow.CWLDescription <- Some(description "New workflow")
                run.CWLInput.Add(CWLParameterReference("message", value = CWLParameterValue.String "hello"))

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                    "runs/Execution/run.yml"
                |]

                Vitest.expect(arc.hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "hash-preserving copies retain the saved baseline of dirty payloads",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc
                setLabel "Changed workflow" workflow.CWLDescription.Value
                run.CWLInput.[0].Value <- Some(CWLParameterValue.String "changed input")
                let copied = copyArcPreservingStaticHashes arc

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts copied |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                    "runs/Execution/run.yml"
                |]

                Vitest.expect(copied.hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "syncing a captured save snapshot preserves later mutations in shared CWL models as dirty",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc
                setLabel "Saved workflow" workflow.CWLDescription.Value
                run.CWLInput.[0].Value <- Some(CWLParameterValue.String "saved input")
                let saved = copyArcPreservingStaticHashes arc
                let capturedContracts = WorkflowPayloads.changedWorkflowPayloadContracts saved
                let capturedText = capturedContracts |> Array.map contractText
                baselineArcStaticHashes saved

                // These nested objects may also be referenced by the copied ARC.
                setLabel "Later workflow" workflow.CWLDescription.Value
                run.CWLInput.[0].Value <- Some(CWLParameterValue.String "later input")
                syncArcStaticHashes saved arc

                Vitest.expect(capturedContracts |> Array.map contractText).toEqual capturedText

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                    "runs/Execution/run.yml"
                |]

                Vitest.expect(arc.hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "per-entity transfer carries saved values instead of treating edited payloads as saved",
            fun () ->
                let _, workflow, run = createArc ()
                WorkflowPayloads.baselineWorkflowPayload workflow
                WorkflowPayloads.baselineRunPayload run
                setLabel "Edited workflow" workflow.CWLDescription.Value
                run.CWLInput.[0].Value <- Some(CWLParameterValue.String "edited input")
                let workflowReplacement = workflow.Copy()
                let runReplacement = run.Copy()
                WorkflowPayloads.syncWorkflowPayloadBaseline workflow workflowReplacement
                WorkflowPayloads.syncRunPayloadBaseline run runReplacement

                Vitest.expect(WorkflowPayloads.workflowHasPayloadChanges workflowReplacement).toBe true
                Vitest.expect(WorkflowPayloads.runHasPayloadChanges runReplacement).toBe true
        )

        Vitest.test (
            "syncing a missing source baseline does not manufacture a clean target baseline",
            fun () ->
                let source, _, _ = createArc ()
                let target, _, _ = createArc ()
                baselineArcStaticHashes target
                WorkflowPayloads.syncWorkflowPayloadBaselines source target

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts target).toHaveLength 3
        )

        Vitest.test (
            "merge copies preserve clean remote workflow and run payload snapshots",
            fun () ->
                let local, _, _ = createArc ()
                let remote, _, _ = createArc ()
                baselineArcStaticHashes local
                baselineArcStaticHashes remote
                local.Title <- Some "Local edit"

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Change
                            Path = "workflows/Analysis/isa.workflow.xlsx"
                        }
                        {
                            EventName = EventName.Change
                            Path = "runs/Execution/isa.run.xlsx"
                        }
                    ]

                Vitest.expect(WorkflowPayloads.workflowHasPayloadChanges merged.Workflows.[0]).toBe false
                Vitest.expect(WorkflowPayloads.runHasPayloadChanges merged.Runs.[0]).toBe false
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged).toEqual [||]
        )

        Vitest.test (
            "unrelated local metadata edits adopt external workflow sidecar changes as clean",
            fun () ->
                let local, localWorkflow, _ = createArc ()
                let remote, remoteWorkflow, _ = createArc ()
                baselineArcStaticHashes local
                local.Title <- Some "Unsaved investigation title"
                setLabel "External workflow" remoteWorkflow.CWLDescription.Value
                baselineArcStaticHashes remote

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Change
                            Path = "workflows/Analysis/workflow.cwl"
                        }
                    ]

                syncArcStaticHashes remote merged
                Vitest.expect(merged.Title).toEqual (Some "Unsaved investigation title")
                Vitest.expect(merged.Workflows.[0].CWLDescription).toEqual remoteWorkflow.CWLDescription
                Vitest.expect(merged.Workflows.[0].hasInMemoryChanges ()).toBe false
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged).toEqual [||]
                Vitest.expect(merged.hasInMemoryChanges ()).toBe true
                Vitest.expect(localWorkflow.CWLDescription).toEqual (Some(description "Workflow"))
        )

        Vitest.test (
            "local workflow edits survive a concurrent external sidecar change and stay dirty",
            fun () ->
                let local, localWorkflow, _ = createArc ()
                let remote, remoteWorkflow, _ = createArc ()
                baselineArcStaticHashes local
                setLabel "Local workflow" localWorkflow.CWLDescription.Value
                setLabel "External workflow" remoteWorkflow.CWLDescription.Value
                baselineArcStaticHashes remote

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Change
                            Path = "workflows/Analysis/workflow.cwl"
                        }
                    ]

                syncArcStaticHashes remote merged
                Vitest.expect(merged.Workflows.[0].CWLDescription).toEqual localWorkflow.CWLDescription

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                |]

                Vitest.expect(merged.Workflows.[0].hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "run merging retains local description edits while adopting external input changes",
            fun () ->
                let local, _, localRun = createArc ()
                let remote, _, remoteRun = createArc ()
                baselineArcStaticHashes local
                setLabel "Local run description" localRun.CWLDescription.Value
                remoteRun.CWLInput.[0].Value <- Some(CWLParameterValue.String "external input")
                baselineArcStaticHashes remote

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Change
                            Path = "runs/Execution/run.yml"
                        }
                    ]

                syncArcStaticHashes remote merged
                Vitest.expect(merged.Runs.[0].CWLDescription).toEqual localRun.CWLDescription
                Vitest.expect(merged.Runs.[0].CWLInput).toEqual remoteRun.CWLInput

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged |> contractPaths).toEqual [|
                    "runs/Execution/run.cwl"
                |]
        )

        Vitest.test (
            "run merging retains local input edits while adopting external description changes",
            fun () ->
                let local, _, localRun = createArc ()
                let remote, _, remoteRun = createArc ()
                baselineArcStaticHashes local
                localRun.CWLInput.[0].Value <- Some(CWLParameterValue.String "local input")
                setLabel "External run description" remoteRun.CWLDescription.Value
                baselineArcStaticHashes remote

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Change
                            Path = "runs/Execution/run.cwl"
                        }
                    ]

                syncArcStaticHashes remote merged
                Vitest.expect(merged.Runs.[0].CWLDescription).toEqual remoteRun.CWLDescription
                Vitest.expect(merged.Runs.[0].CWLInput).toEqual localRun.CWLInput

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged |> contractPaths).toEqual [|
                    "runs/Execution/run.yml"
                |]
        )

        Vitest.test (
            "canonical metadata events retain locally edited workflow sidecars",
            fun () ->
                let local, localWorkflow, _ = createArc ()
                let remote, remoteWorkflow, _ = createArc ()
                baselineArcStaticHashes local
                setLabel "Local workflow sidecar" localWorkflow.CWLDescription.Value
                localWorkflow.Title <- Some "Local workflow metadata"
                remoteWorkflow.Title <- Some "External workflow metadata"
                setLabel "External workflow sidecar" remoteWorkflow.CWLDescription.Value
                baselineArcStaticHashes remote

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Change
                            Path = "workflows/Analysis/isa.workflow.xlsx"
                        }
                    ]

                syncArcStaticHashes remote merged
                Vitest.expect(merged.Workflows.[0].Title).toEqual (Some "External workflow metadata")
                Vitest.expect(merged.Workflows.[0].CWLDescription).toEqual localWorkflow.CWLDescription

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                |]

                Vitest.expect(localWorkflow.Title).toEqual (Some "Local workflow metadata")
        )

        Vitest.test (
            "disk absence replaces unchanged sidecars without creating deletion contracts",
            fun () ->
                let local, _, _ = createArc ()
                let remote, remoteWorkflow, remoteRun = createArc ()
                baselineArcStaticHashes local
                local.Title <- Some "Local investigation metadata"
                remoteWorkflow.CWLDescription <- None
                remoteRun.CWLDescription <- None
                remoteRun.CWLInput.Clear()
                baselineArcStaticHashes remote

                let merged =
                    ARC.merge local remote [
                        {
                            EventName = EventName.Unlink
                            Path = "workflows/Analysis/workflow.cwl"
                        }
                        {
                            EventName = EventName.Unlink
                            Path = "runs/Execution/run.cwl"
                        }
                        {
                            EventName = EventName.Unlink
                            Path = "runs/Execution/run.yml"
                        }
                    ]

                syncArcStaticHashes remote merged
                Vitest.expect(merged.Workflows.[0].CWLDescription).toEqual None
                Vitest.expect(merged.Runs.[0].CWLDescription).toEqual None
                Vitest.expect(merged.Runs.[0].CWLInput).toHaveLength 0
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged).toEqual [||]
                Vitest.expect(merged.hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "cleared local payloads adopt disk values even without canonical events",
            fun () ->
                let local, localWorkflow, localRun = createArc ()
                let remote, remoteWorkflow, remoteRun = createArc ()
                baselineArcStaticHashes local
                local.Title <- Some "Local investigation metadata"
                localWorkflow.CWLDescription <- None
                localRun.CWLDescription <- None
                localRun.CWLInput.Clear()
                setLabel "External workflow" remoteWorkflow.CWLDescription.Value
                setLabel "External run" remoteRun.CWLDescription.Value
                remoteRun.CWLInput.[0].Value <- Some(CWLParameterValue.String "external input")
                baselineArcStaticHashes remote

                let merged = ARC.merge local remote []

                Vitest.expect(merged.Workflows.[0].CWLDescription).toEqual remoteWorkflow.CWLDescription
                Vitest.expect(merged.Runs.[0].CWLDescription).toEqual remoteRun.CWLDescription
                Vitest.expect(merged.Runs.[0].CWLInput).toEqual remoteRun.CWLInput
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts merged).toEqual [||]
                Vitest.expect(merged.Title).toEqual (Some "Local investigation metadata")
        )

        Vitest.test (
            "metadata DTO replacement retains loaded sidecar content and its saved baseline",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc

                let originalPayloads =
                    WorkflowPayloads.workflowPayloadContracts arc Operation.CREATE
                    |> Array.map contractText

                let editedWorkflow =
                    ArcWorkflow(workflow.Identifier, title = "Edited workflow metadata")

                let editedRun = ArcRun(run.Identifier, title = "Edited run metadata")

                let workflowDto =
                    FileContentDTO.fromArcFile (ArcFiles.Workflow editedWorkflow) |> Option.get

                let runDto = FileContentDTO.fromArcFile (ArcFiles.Run editedRun) |> Option.get

                match updateARCByFileContentDTO arc workflowDto, updateARCByFileContentDTO arc runDto with
                | Ok _, Ok _ -> ()
                | _ -> failwith "Expected metadata updates to succeed."

                let payloads =
                    WorkflowPayloads.workflowPayloadContracts arc Operation.CREATE
                    |> Array.map contractText

                Vitest.expect(payloads).toEqual originalPayloads
                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc).toEqual [||]
                Vitest.expect(arc.GetWorkflow(workflow.Identifier).Title).toEqual (Some "Edited workflow metadata")
                Vitest.expect(arc.GetRun(run.Identifier).Title).toEqual (Some "Edited run metadata")
                Vitest.expect(arc.hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "metadata DTO replacement preserves unsaved sidecar edits as dirty",
            fun () ->
                let arc, workflow, run = createArc ()
                baselineArcStaticHashes arc
                setLabel "Unsaved workflow" workflow.CWLDescription.Value
                run.CWLInput.[0].Value <- Some(CWLParameterValue.String "unsaved input")

                let workflowDto =
                    FileContentDTO.fromArcFile (ArcFiles.Workflow workflow) |> Option.get

                let runDto = FileContentDTO.fromArcFile (ArcFiles.Run run) |> Option.get

                match updateARCByFileContentDTO arc workflowDto, updateARCByFileContentDTO arc runDto with
                | Ok _, Ok _ -> ()
                | _ -> failwith "Expected metadata updates to succeed."

                Vitest.expect(WorkflowPayloads.changedWorkflowPayloadContracts arc |> contractPaths).toEqual [|
                    "workflows/Analysis/workflow.cwl"
                    "runs/Execution/run.yml"
                |]

                Vitest.expect(arc.hasInMemoryChanges ()).toBe true
        )

        Vitest.test (
            "hash-preserving copies retain configured license paths and saved hashes",
            fun () ->
                let arc, _, _ = createArc ()
                arc.License <- Some(License.initFulltext ("License text", path = "LICENSE.md"))
                baselineArcStaticHashes arc
                let savedHash = arc.License.Value.StaticHash
                arc.License.Value.Content <- "Edited license text"
                let copied = copyArcPreservingStaticHashes arc

                Vitest.expect(copied.License.Value.Path).toBe "LICENSE.md"
                Vitest.expect(copied.License.Value.Content).toBe "Edited license text"
                Vitest.expect(copied.License.Value.StaticHash).toBe savedHash
        )
)
