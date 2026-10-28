module Main.IPC.Delete

open Fable.Core
open ARCtrl
open ARCtrl.Contract
open Main.ARCtrlExtensions
open Main.ArcMerge
open Swate.Components.Shared
open ARC

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

    let private removeFromDiskAsync fileType identifier arcPath (arc: ARC) =
        match fileType with
        | ArcFilesDiscriminate.Assay -> arc.TryRemoveAssayAsync(arcPath, identifier)
        | ArcFilesDiscriminate.Study -> arc.TryRemoveStudyAsync(arcPath, identifier)
        | ArcFilesDiscriminate.Workflow -> arc.TryRemoveWorkflowAsync(arcPath, identifier)
        | ArcFilesDiscriminate.Run -> arc.TryRemoveRunAsync(arcPath, identifier)
        | _ -> promise { return Error [| $"Deleting {fileType} is not supported." |] }

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

    let private mergeDeletedEntityFromDisk arcPath canonicalFilePath (arcLocal: ARC) = promise {
        match! ARC.LoadAsyncSwate arcPath with
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
    let deleteArcEntityAsync (arcPath: string) (relativePath: string) (arc: ARC) : JS.Promise<Result<ARC, exn>> = promise {
        match tryGetEntityDeleteTarget relativePath with
        | Error validationError -> return Error validationError
        | Ok(fileType, identifier, normalizedRelativePath, canonicalFilePath) ->
            try
                match! ARC.LoadAsyncSwate arcPath with
                | Error errors ->
                    return
                        Error(
                            exn
                                $"Could not load ARC from disk before deleting '{normalizedRelativePath}': {PathHelpers.formatContractErrors errors}"
                        )
                | Ok diskArc ->
                    match tryEnsureArcEntityResolved fileType identifier canonicalFilePath diskArc with
                    | Error resolutionError -> return Error resolutionError
                    | Ok() ->
                        match! removeFromDiskAsync fileType identifier arcPath diskArc with
                        | Error errors ->
                            return
                                Error(
                                    exn
                                        $"Could not delete ARC entity at '{normalizedRelativePath}': {PathHelpers.formatContractErrors errors}"
                                )
                        | Ok _ -> return! mergeDeletedEntityFromDisk arcPath canonicalFilePath arc
            with deleteError ->
                return Error(exn $"Could not delete ARC entity at '{normalizedRelativePath}': {deleteError.Message}")
    }
