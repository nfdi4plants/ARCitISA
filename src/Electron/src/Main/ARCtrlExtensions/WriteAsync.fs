namespace Main.ARCtrlExtensions

open ARCtrl
open ARCtrl.Contract
open Fable.Core

[<AutoOpen>]
module ArcWriteExtensions =

    /// The distinct ARC-relative paths a contract batch touches. A rename also touches its target.
    let contractWrittenPaths (contracts: Contract[]) : string[] =
        contracts
        |> Array.collect (fun contract ->
            match contract.Operation, contract.DTO with
            | Operation.RENAME, Some(DTO.Text targetPath) -> [| contract.Path; targetPath |]
            | _ -> [| contract.Path |]
        )
        |> Array.distinct

    let private writeContracts (skipUpdateFS: bool option) (snapshot: ARC) =
        [|
            yield ARCtrl.Contract.Git.gitignoreContract
            yield! snapshot.GetWriteContracts(?skipUpdateFS = skipUpdateFS)
            yield! WorkflowPayloads.workflowPayloadContracts snapshot Operation.CREATE
        |]
        |> ContractPolicy.distinctContracts
        |> ContractPolicy.regenerateParentPayloads snapshot
        |> function
            | Ok contracts -> ContractPolicy.orderContracts contracts
            | Error message -> failwith message

    let private prepareSave (skipUpdateFS: bool option) (arc: ARC) =
        let snapshot = copyArcPreservingStaticHashes arc
        let changedPayloads = WorkflowPayloads.changedWorkflowPayloadContracts snapshot
        let isFullWrite = snapshot.StaticHash = 0
        let dirty = ContractPolicy.captureDirtyDatamapKeys snapshot

        let raw =
            Array.append (snapshot.GetUpdateContracts(?skipUpdateFS = skipUpdateFS)) changedPayloads

        // Freeze the content being saved before I/O. Later edits must remain dirty when this is committed.
        baselineArcStaticHashes snapshot
        snapshot, raw, dirty, isFullWrite

    /// The injectable executor lets callers verify failure and in-flight edits without changing save policy.
    let internal persistArcSaveWithAsync
        (execute: string -> Contract[] -> JS.Promise<Result<Contract[], string[]>>)
        (arcPath: string)
        (arc: ARC)
        =
        promise {
            let snapshot, raw, dirty, isFullWrite = prepareSave None arc

            match!
                ContractPolicy.prepareAsync {
                    ArcPath = arcPath
                    Snapshot = snapshot
                    RawContracts = raw
                    DirtyDatamapKeys = dirty
                    IsFullWrite = isFullWrite
                    DiskPathBeforeExecution = id
                }
            with
            | Error message -> return Error [| message |]
            | Ok contracts ->
                match! execute arcPath contracts with
                | Error errors -> return Error errors
                | Ok executed ->
                    syncArcStaticHashes snapshot arc
                    return Ok executed
        }

    type ARC with

        /// Generates targeted ARC files and create-only defaults without changing the live saved baseline.
        member this.GetWriteContractsSwate(?skipUpdateFS: bool) =
            copyArcPreservingStaticHashes this |> writeContracts skipUpdateFS

        /// Existing destinations are skipped. The creation lifecycle reloads disk as its clean baseline.
        member this.TryWriteAsyncSwate(arcPath: string) = promise {
            try
                return!
                    this.GetWriteContractsSwate()
                    |> ContractExecutor.fulfillContractsSwateAsync arcPath
            with error ->
                return Error [| error.Message |]
        }

        /// Model payloads are upserts; default scaffold files remain create-only.
        member this.PrepareUpdateContractsSwateAsync(arcPath: string) : JS.Promise<Result<Contract[], string[]>> = promise {
            let snapshot, raw, dirty, isFullWrite = prepareSave None this

            let! prepared =
                ContractPolicy.prepareAsync {
                    ArcPath = arcPath
                    Snapshot = snapshot
                    RawContracts = raw
                    DirtyDatamapKeys = dirty
                    IsFullWrite = isFullWrite
                    DiskPathBeforeExecution = id
                }

            return prepared |> Result.mapError Array.singleton
        }

        /// Commits captured saved baselines only after the complete batch succeeds.
        member this.TryUpdateAsyncSwate(arcPath: string) =
            persistArcSaveWithAsync ContractExecutor.fulfillContractsSwateAsync arcPath this
