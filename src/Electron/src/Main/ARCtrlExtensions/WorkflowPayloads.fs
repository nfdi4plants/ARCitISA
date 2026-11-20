namespace Main.ARCtrlExtensions

open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open Fable.Core

/// Saved payload text is kept separately because ARCtrl's light hashes omit CWL and run inputs.
[<RequireQualifiedAccess>]
module WorkflowPayloads =

    type private WorkflowSnapshot = { Description: string option }

    type private RunSnapshot = {
        Description: string option
        Input: string option
    }

    // Weak keys follow the lifetime of the entity. Only immutable serialized strings enter the cache:
    // ARCtrl copies can share mutable CWL models with the live ARC while a save is in progress.
    let private workflowSnapshots =
        JS.Constructors.WeakMap.Create<ArcWorkflow, WorkflowSnapshot>()

    let private runSnapshots = JS.Constructors.WeakMap.Create<ArcRun, RunSnapshot>()

    let private workflowSnapshot (workflow: ArcWorkflow) : WorkflowSnapshot = {
        Description = workflow.CWLDescription |> Option.map CWL.Encode.encodeProcessingUnit
    }

    let private runSnapshot (run: ArcRun) : RunSnapshot = {
        Description = run.CWLDescription |> Option.map CWL.Encode.encodeProcessingUnit
        Input =
            if run.CWLInput.Count = 0 then
                None
            else
                Some(CWL.Encode.encodeYAMLParameterFile run.CWLInput)
    }

    let private savedWorkflowSnapshot workflow : WorkflowSnapshot =
        if workflowSnapshots.has workflow then
            workflowSnapshots.get workflow
        else
            { Description = None }

    let private savedRunSnapshot run : RunSnapshot =
        if runSnapshots.has run then
            runSnapshots.get run
        else
            { Description = None; Input = None }

    let private changedText saved current =
        current |> Option.filter (fun text -> Some text <> saved)

    let private textContract operation path dtoType text =
        Contract.create (operation, path, dtoType, DTO.Text text)

    /// Captures the current workflow payload as saved, without changing upstream static hashes.
    let baselineWorkflowPayload (workflow: ArcWorkflow) : unit =
        workflowSnapshots.set (workflow, workflowSnapshot workflow) |> ignore

    /// Captures the current run payloads as saved, without retaining mutable CWL objects.
    let baselineRunPayload (run: ArcRun) : unit =
        runSnapshots.set (run, runSnapshot run) |> ignore

    /// Transfers the saved snapshot, rather than treating the source's current payload as saved.
    let syncWorkflowPayloadBaseline (source: ArcWorkflow) (target: ArcWorkflow) : unit =
        workflowSnapshots.set (target, savedWorkflowSnapshot source) |> ignore

    /// Transfers the saved snapshot, including the absence of a previously captured baseline.
    let syncRunPayloadBaseline (source: ArcRun) (target: ArcRun) : unit =
        runSnapshots.set (target, savedRunSnapshot source) |> ignore

    let baselineWorkflowPayloads (arc: ARC) : unit =
        arc.Workflows |> Seq.iter baselineWorkflowPayload
        arc.Runs |> Seq.iter baselineRunPayload

    /// Copies saved snapshots for matching identifiers; current mutable payload values are never read.
    let syncWorkflowPayloadBaselines (source: ARC) (target: ARC) : unit =
        for workflow in target.Workflows do
            source.TryGetWorkflow workflow.Identifier
            |> Option.iter (fun sourceWorkflow -> syncWorkflowPayloadBaseline sourceWorkflow workflow)

        for run in target.Runs do
            source.TryGetRun run.Identifier
            |> Option.iter (fun sourceRun -> syncRunPayloadBaseline sourceRun run)

    /// Resolves sidecars independently of metadata events. Only a present locally edited payload
    /// wins over disk; otherwise the reloaded value (including absence) becomes the merged value.
    let mergeWorkflowPayloadsFromDisk (arcLocal: ARC) (arcRemote: ARC) (merged: ARC) : unit =
        for workflow in merged.Workflows do
            match arcRemote.TryGetWorkflow workflow.Identifier with
            | None -> ()
            | Some remoteWorkflow ->
                workflow.CWLDescription <-
                    match arcLocal.TryGetWorkflow workflow.Identifier with
                    | Some localWorkflow ->
                        let current = workflowSnapshot localWorkflow
                        let saved = savedWorkflowSnapshot localWorkflow

                        if changedText saved.Description current.Description |> Option.isSome then
                            localWorkflow.CWLDescription
                        else
                            remoteWorkflow.CWLDescription
                    | None -> remoteWorkflow.CWLDescription

                syncWorkflowPayloadBaseline remoteWorkflow workflow

        for run in merged.Runs do
            match arcRemote.TryGetRun run.Identifier with
            | None -> ()
            | Some remoteRun ->
                match arcLocal.TryGetRun run.Identifier with
                | Some localRun ->
                    let current = runSnapshot localRun
                    let saved = savedRunSnapshot localRun

                    run.CWLDescription <-
                        if changedText saved.Description current.Description |> Option.isSome then
                            localRun.CWLDescription
                        else
                            remoteRun.CWLDescription

                    run.CWLInput <-
                        if changedText saved.Input current.Input |> Option.isSome then
                            localRun.CWLInput
                        else
                            remoteRun.CWLInput
                | None ->
                    run.CWLDescription <- remoteRun.CWLDescription
                    run.CWLInput <- remoteRun.CWLInput

                // The disk baseline keeps adopted fields clean and retained local edits dirty.
                syncRunPayloadBaseline remoteRun run

    let workflowHasPayloadChanges (workflow: ArcWorkflow) : bool =
        let current = workflowSnapshot workflow
        let saved = savedWorkflowSnapshot workflow
        changedText saved.Description current.Description |> Option.isSome

    let runHasPayloadChanges (run: ArcRun) : bool =
        let current = runSnapshot run
        let saved = savedRunSnapshot run

        (changedText saved.Description current.Description |> Option.isSome)
        || (changedText saved.Input current.Input |> Option.isSome)

    let workflowContracts (workflow: ArcWorkflow) (operation: Operation) : Contract[] = [|
        let current = workflowSnapshot workflow

        match current.Description with
        | Some text ->
            yield
                textContract
                    operation
                    (Identifier.Workflow.cwlFileNameFromIdentifier workflow.Identifier)
                    DTOType.CWL
                    text
        | None -> ()
    |]

    let runContracts (run: ArcRun) (operation: Operation) : Contract[] = [|
        let current = runSnapshot run

        match current.Description with
        | Some text ->
            yield textContract operation (Identifier.Run.cwlFileNameFromIdentifier run.Identifier) DTOType.CWL text
        | None -> ()

        match current.Input with
        | Some text ->
            yield textContract operation (Identifier.Run.ymlFileNameFromIdentifier run.Identifier) DTOType.YAML text
        | None -> ()
    |]

    /// Present payloads produce managed text contracts. Absence means keep any existing file.
    let workflowPayloadContracts (arc: ARC) (operation: Operation) : Contract[] = [|
        for workflow in arc.Workflows do
            yield! workflowContracts workflow operation

        for run in arc.Runs do
            yield! runContracts run operation
    |]

    /// Emits only changed present payloads. Missing baselines count as changes; clearing a payload does not.
    let changedWorkflowPayloadContracts (arc: ARC) : Contract[] = [|
        for workflow in arc.Workflows do
            let current = workflowSnapshot workflow
            let saved = savedWorkflowSnapshot workflow

            match changedText saved.Description current.Description with
            | Some text ->
                yield
                    textContract
                        Operation.UPDATE
                        (Identifier.Workflow.cwlFileNameFromIdentifier workflow.Identifier)
                        DTOType.CWL
                        text
            | None -> ()

        for run in arc.Runs do
            let current = runSnapshot run
            let saved = savedRunSnapshot run

            match changedText saved.Description current.Description with
            | Some text ->
                yield
                    textContract
                        Operation.UPDATE
                        (Identifier.Run.cwlFileNameFromIdentifier run.Identifier)
                        DTOType.CWL
                        text
            | None -> ()

            match changedText saved.Input current.Input with
            | Some text ->
                yield
                    textContract
                        Operation.UPDATE
                        (Identifier.Run.ymlFileNameFromIdentifier run.Identifier)
                        DTOType.YAML
                        text
            | None -> ()
    |]
