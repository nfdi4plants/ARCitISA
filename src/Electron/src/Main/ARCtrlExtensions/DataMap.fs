namespace Main.ARCtrlExtensions

open ARCtrl
open ARCtrl.Contract
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper


[<AutoOpen>]
module DataMapExtensions =

    let loadPersistedDataMapParentAsync (arcPath: string) (parentInfo: DatamapParentInfo) = promise {
        let parentFilePath =
            match parentInfo.Parent with
            | DataMapParent.Assay -> ARCtrl.Helper.Identifier.Assay.fileNameFromIdentifier parentInfo.ParentId
            | DataMapParent.Study -> ARCtrl.Helper.Identifier.Study.fileNameFromIdentifier parentInfo.ParentId
            | DataMapParent.Run -> ARCtrl.Helper.Identifier.Run.fileNameFromIdentifier parentInfo.ParentId
            | DataMapParent.Workflow -> ARCtrl.Helper.Identifier.Workflow.fileNameFromIdentifier parentInfo.ParentId

        if not (existsSync (join [| arcPath; parentFilePath |])) then
            return Ok None
        else
            let persistedArc = ARC.fromFilePaths [| parentFilePath |]
            let contracts = persistedArc.GetReadContracts()

            match! fullFillContractBatchAsync true arcPath contracts with
            | Error errors -> return Error errors
            | Ok fulfilledContracts ->
                persistedArc.SetISAFromContracts fulfilledContracts
                return Ok(Some persistedArc)
    }

    let private tryGetParentDataMapRemovalContract (arc: ARC) (parentInfo: DatamapParentInfo) =
        match parentInfo.Parent with
        | DataMapParent.Assay ->
            arc.TryGetAssay parentInfo.ParentId
            |> Option.map (fun assay ->
                assay.Datamap <- None
                assay.ToUpdateContract()
            )
        | DataMapParent.Study ->
            arc.TryGetStudy parentInfo.ParentId
            |> Option.map (fun study ->
                study.Datamap <- None
                study.ToUpdateContract()
            )
        | DataMapParent.Run ->
            arc.TryGetRun parentInfo.ParentId
            |> Option.map (fun run ->
                run.Datamap <- None
                run.ToUpdateContract()
            )
        | DataMapParent.Workflow ->
            arc.TryGetWorkflow parentInfo.ParentId
            |> Option.map (fun workflow ->
                workflow.Datamap <- None
                workflow.ToUpdateContract()
            )

    type Datamap with

        member this.ToCreateContract(parentInfo: DatamapParentInfo) : Contract = DataMapContract.create parentInfo this

        member this.ToDeleteContract(parentInfo: DatamapParentInfo) : Contract = DataMapContract.delete parentInfo this

    type ARC with

        /// Deletes a DataMap through its parent-specific ARCtrl contract and updates the in-memory ARC after success.
        member this.TryDeleteDataMapAsync(arcPath: string, parentInfo: DatamapParentInfo) = promise {
            match this.TryGetDataMap parentInfo with
            | None ->
                let parentPath = DatamapParentInfo.toFolderPath parentInfo

                return
                    Error(
                        exn
                            $"Parent '{parentPath}' does not have a DataMap to delete. Refresh the File Explorer and try again."
                    )
            | Some dataMap ->
                match! loadPersistedDataMapParentAsync arcPath parentInfo with
                | Error errors ->
                    return
                        Error(
                            exn
                                $"The DataMap could not be deleted because its persisted parent could not be loaded. {PathHelpers.formatContractErrors errors}"
                        )
                | Ok None ->
                    let parentPath = DatamapParentInfo.toFolderPath parentInfo

                    return
                        Error(
                            exn
                                $"The DataMap could not be deleted because parent '{parentPath}' was not found on disk. Refresh the File Explorer and try again."
                        )
                | Ok(Some persistedArc) ->
                    match tryGetParentDataMapRemovalContract persistedArc parentInfo with
                    | None ->
                        let parentPath = DatamapParentInfo.toFolderPath parentInfo

                        return
                            Error(
                                exn
                                    $"The DataMap could not be deleted because parent '{parentPath}' was not found on disk. Refresh the File Explorer and try again."
                            )
                    | Some parentUpdateContract ->
                        match! fullFillContractBatchAsync true arcPath [| parentUpdateContract |] with
                        | Error errors ->
                            return
                                Error(
                                    exn
                                        $"The DataMap could not be deleted because its parent could not be updated. {PathHelpers.formatContractErrors errors}"
                                )
                        | Ok _ ->
                            let deleteContract = dataMap.ToDeleteContract(parentInfo)

                            match! fullFillContractBatchAsync true arcPath [| deleteContract |] with
                            | Error errors ->
                                return
                                    Error(
                                        exn
                                            $"The DataMap could not be deleted from disk. {PathHelpers.formatContractErrors errors}"
                                    )
                            | Ok _ ->
                                this.TrySetDataMap(parentInfo, None) |> ignore
                                this.UpdateFileSystem()
                                return Ok()
        }
