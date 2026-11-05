module Main.IPC.Delete

open Fable.Core
open ARCtrl
open ARCtrl.Contract
open Main.ARCtrlExtensions
open Main.ArcMerge
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open ARC

type ArcEntityDeleteOutcome =
    | Deleted of ARC
    | PartiallyDeleted of ARC * exn

[<RequireQualifiedAccess>]
module ArcDeleteHelper =

    let arcFileTypeForZone =
        function
        | ArcEntityPathRules.AddZone.Assays -> ArcFilesDiscriminate.Assay
        | ArcEntityPathRules.AddZone.Studies -> ArcFilesDiscriminate.Study
        | ArcEntityPathRules.AddZone.Workflows -> ArcFilesDiscriminate.Workflow
        | ArcEntityPathRules.AddZone.Runs -> ArcFilesDiscriminate.Run

    let private entityKindForFileType =
        function
        | ArcFilesDiscriminate.Assay -> "assay"
        | ArcFilesDiscriminate.Study -> "study"
        | ArcFilesDiscriminate.Workflow -> "workflow"
        | ArcFilesDiscriminate.Run -> "run"
        | fileType -> string fileType

    let private removeContracts fileType identifier (diskArc: ARC) : Contract[] =
        match fileType with
        | ArcFilesDiscriminate.Assay -> diskArc.GetAssayRemoveContracts identifier
        | ArcFilesDiscriminate.Study -> diskArc.GetStudyRemoveContracts identifier
        | ArcFilesDiscriminate.Workflow -> diskArc.GetWorkflowRemoveContracts identifier
        | ArcFilesDiscriminate.Run -> diskArc.GetRunRemoveContracts identifier
        | _ -> failwith $"Deleting {fileType} is not supported."

    let private arcFileMatchesEntity fileType identifier =
        function
        | ArcFiles.Assay assay ->
            fileType = ArcFilesDiscriminate.Assay
            && PathHelpers.pathsEqual assay.Identifier identifier
        | ArcFiles.Study(study, _) ->
            fileType = ArcFilesDiscriminate.Study
            && PathHelpers.pathsEqual study.Identifier identifier
        | ArcFiles.Workflow workflow ->
            fileType = ArcFilesDiscriminate.Workflow
            && PathHelpers.pathsEqual workflow.Identifier identifier
        | ArcFiles.Run run ->
            fileType = ArcFilesDiscriminate.Run
            && PathHelpers.pathsEqual run.Identifier identifier
        | ArcFiles.Investigation _
        | ArcFiles.DataMap _
        | ArcFiles.Template _ -> false

    let tryEnsureArcEntityResolved fileType identifier relativePath (arc: ARC) =
        match arc.TryArcFileByPath(relativePath) with
        | Some arcFile when arcFileMatchesEntity fileType identifier arcFile -> Ok()
        | _ -> Error(exn $"ARC does not contain {entityKindForFileType fileType} with identifier '{identifier}'.")

    let tryCanonicalEntityFilePath zone identifier =
        match ArcEntityPathRules.buildCanonicalEntityPaths zone identifier |> List.tryHead with
        | Some path -> Ok path
        | None -> Error(exn $"Could not determine the canonical ARC entity file for '{identifier}'.")

    let private mergeDeletedEntityFromDisk
        (load: string -> JS.Promise<Result<ARC, string[]>>)
        arcPath
        canonicalFilePath
        (arcLocal: ARC)
        =
        promise {
            match! load arcPath with
            | Error errors ->
                return
                    Error(
                        exn
                            $"Deleted ARC entity, but could not reload the ARC from disk: {PathHelpers.formatContractErrors errors}"
                    )
            | Ok diskArc ->
                baselineArcStaticHashes diskArc
                // The IPC delete path suppresses watcher ARC merges, so apply the same unlink event explicitly.
                let mergedArc =
                    ARC.merge arcLocal diskArc [
                        {
                            EventName = EventName.Unlink
                            Path = canonicalFilePath
                        }
                    ]

                syncArcStaticHashes diskArc mergedArc
                return Ok mergedArc
        }

    /// Keeps the local edits and leaves metadata dirty when the deleted entity cannot be reloaded.
    let private fallbackArc fileType identifier canonicalFilePath (arcLocal: ARC) : ARC =
        let copiedArc = copyArcPreservingStaticHashes arcLocal

        match fileType with
        | ArcFilesDiscriminate.Assay when copiedArc.ContainsAssay identifier -> copiedArc.RemoveAssay identifier
        | ArcFilesDiscriminate.Study when copiedArc.ContainsStudy identifier -> copiedArc.RemoveStudy identifier
        | ArcFilesDiscriminate.Workflow when copiedArc.ContainsWorkflow identifier ->
            copiedArc.DeleteWorkflow identifier
        | ArcFilesDiscriminate.Run when copiedArc.ContainsRun identifier -> copiedArc.DeleteRun identifier
        | _ -> ()

        let entityFolder = PathHelpers.tryGetParentPath canonicalFilePath |> Option.get

        copiedArc.SetFilePaths(
            copiedArc.FileSystem.Tree.ToFilePaths()
            |> Array.filter (fun path -> not (PathHelpers.isSameOrDescendantPath path entityFolder))
        )

        copiedArc

    let private tryGetEntityDeleteTarget relativePath =
        match ArcEntityPathRules.classifyDeleteTarget relativePath with
        | ArcEntityPathRules.DeletePathClassification.EntityFolderTarget(zone, identifier, normalizedRelativePath) ->
            tryCanonicalEntityFilePath zone identifier
            |> Result.map (fun canonicalFilePath ->
                arcFileTypeForZone zone, identifier, normalizedRelativePath, canonicalFilePath
            )
        | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.EntityFile(zone,
                                                                                                                               identifier),
                                                                          normalizedRelativePath) ->
            Ok(arcFileTypeForZone zone, identifier, normalizedRelativePath, normalizedRelativePath)
        | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.DataMapFile _,
                                                                          _)
        | ArcEntityPathRules.DeletePathClassification.AddZoneDescendantTarget _
        | ArcEntityPathRules.DeletePathClassification.GenericTarget _ ->
            Error(exn "Generic filesystem delete paths do not use ARC entity delete contracts.")
        | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.InvestigationFile,
                                                                          _) ->
            Error(exn "Deleting the investigation file is not supported.")
        | ArcEntityPathRules.DeletePathClassification.ProtectedTarget _ ->
            Error(exn "Deleting ARC-managed folders (for example dataset or protocols) is not allowed.")
        | ArcEntityPathRules.DeletePathClassification.DisallowedTarget _ ->
            Error(exn "Deletion is not allowed for this path.")

    /// Deletes ARC entities through ARCtrl contracts while preserving unrelated dirty in-memory edits.
    let internal deleteArcEntityWithAsync
        (load: string -> JS.Promise<Result<ARC, string[]>>)
        (execute: string -> Contract[] -> JS.Promise<Result<Contract[], string[]>>)
        (arcPath: string)
        (relativePath: string)
        (arc: ARC)
        : JS.Promise<Result<ArcEntityDeleteOutcome, exn>> =
        promise {
            match tryGetEntityDeleteTarget relativePath with
            | Error validationError -> return Error validationError
            | Ok(fileType, identifier, normalizedRelativePath, canonicalFilePath) ->
                try
                    match! load arcPath with
                    | Error errors ->
                        return
                            Error(
                                exn
                                    $"Could not load ARC from disk before deleting '{normalizedRelativePath}': {PathHelpers.formatContractErrors errors}"
                            )
                    | Ok diskArc ->
                        baselineArcStaticHashes diskArc

                        match tryEnsureArcEntityResolved fileType identifier canonicalFilePath diskArc with
                        | Error resolutionError -> return Error resolutionError
                        | Ok() ->
                            let dirty = ContractPolicy.captureDirtyDatamapKeys diskArc
                            let raw = removeContracts fileType identifier diskArc

                            match!
                                ContractPolicy.prepareAsync {
                                    ArcPath = arcPath
                                    Snapshot = diskArc
                                    RawContracts = raw
                                    DirtyDatamapKeys = dirty
                                    IsFullWrite = false
                                    DiskPathBeforeExecution = id
                                }
                            with
                            | Error message ->
                                return
                                    Error(exn $"Could not delete ARC entity at '{normalizedRelativePath}': {message}")
                            | Ok contracts ->
                                match! execute arcPath contracts with
                                | Ok _ ->
                                    match! mergeDeletedEntityFromDisk load arcPath canonicalFilePath arc with
                                    | Ok merged -> return Ok(Deleted merged)
                                    | Error error ->
                                        let message =
                                            $"Deleted '{normalizedRelativePath}', but could not reload the ARC from disk: {error.Message}"

                                        return
                                            Ok(
                                                PartiallyDeleted(
                                                    fallbackArc fileType identifier canonicalFilePath arc,
                                                    exn message
                                                )
                                            )
                                | Error errors ->
                                    let entityFolder = PathHelpers.tryGetParentPath canonicalFilePath |> Option.get

                                    if existsSync (join [| arcPath; entityFolder |]) then
                                        return
                                            Error(
                                                exn
                                                    $"Could not delete ARC entity at '{normalizedRelativePath}': {PathHelpers.formatContractErrors errors}"
                                            )
                                    else
                                        let metadataMessage =
                                            $"Deleted '{normalizedRelativePath}', but updating the ARC metadata failed: {PathHelpers.formatContractErrors errors}. Save the ARC to retry."

                                        match! mergeDeletedEntityFromDisk load arcPath canonicalFilePath arc with
                                        | Ok merged -> return Ok(PartiallyDeleted(merged, exn metadataMessage))
                                        | Error error ->
                                            let reloadMessage =
                                                $"Deleted '{normalizedRelativePath}', but could not reload the ARC from disk: {error.Message}"

                                            return
                                                Ok(
                                                    PartiallyDeleted(
                                                        fallbackArc fileType identifier canonicalFilePath arc,
                                                        exn $"{metadataMessage}\n{reloadMessage}"
                                                    )
                                                )
                with deleteError ->
                    return
                        Error(exn $"Could not delete ARC entity at '{normalizedRelativePath}': {deleteError.Message}")
        }

    let deleteArcEntityAsync arcPath relativePath arc : JS.Promise<Result<ArcEntityDeleteOutcome, exn>> =
        deleteArcEntityWithAsync ARC.LoadAsyncSwate ContractExecutor.fulfillContractsSwateAsync arcPath relativePath arc
