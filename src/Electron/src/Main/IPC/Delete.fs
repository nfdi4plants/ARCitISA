module Main.IPC.Delete

open Fable.Core
open ARCtrl
open ARCtrl.Contract
open Main
open Main.ArcVaultState
open Main.ArcVaultSync
open Main.ARCtrlExtensions
open Main.ArcVaultHelper
open Main.ArcMerge
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open ARC

/// The light hash of each ARC file of an ARC.
type ArcFileHashes = {
    Investigation: int
    Assays: Map<string, int>
    Studies: Map<string, int>
    Workflows: Map<string, int>
    Runs: Map<string, int>
}

/// What a finished delete did on disk.
type ArcEntityDeletion = {
    FileType: ArcFilesDiscriminate
    Identifier: string
    /// ARC-relative path of the deleted entity's workbook.
    WorkbookPath: string
    /// ARC-relative path of the deleted entity's folder.
    FolderPath: string
    DiskBefore: ArcFileHashes
    DiskAfter: ArcFileHashes
}

/// PartiallyDeleted means the entity folder is gone, but updating the files that register the entity failed.
type ArcEntityDeleteOutcome =
    | Deleted of ArcEntityDeletion
    | PartiallyDeleted of ArcEntityDeletion * exn

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

    let captureArcFileHashes (arc: ARC) : ArcFileHashes =
        let hashesOf (entities: seq<'Entity>) (identifierOf: 'Entity -> string) (hashOf: 'Entity -> int) =
            entities
            |> Seq.map (fun entity -> identifierOf entity, hashOf entity)
            |> Map.ofSeq

        {
            Investigation = arc.GetLightHashCode()
            Assays = hashesOf arc.Assays (fun (e: ArcAssay) -> e.Identifier) (fun e -> e.GetLightHashCode())
            Studies = hashesOf arc.Studies (fun (e: ArcStudy) -> e.Identifier) (fun e -> e.GetLightHashCode())
            Workflows = hashesOf arc.Workflows (fun (e: ArcWorkflow) -> e.Identifier) (fun e -> e.GetLightHashCode())
            Runs = hashesOf arc.Runs (fun (e: ArcRun) -> e.Identifier) (fun e -> e.GetLightHashCode())
        }

    let private workbookPathOf zone identifier =
        ArcEntityPathRules.buildCanonicalEntityPaths zone identifier
        |> List.tryHead
        |> Option.defaultValue identifier

    /// ARC-relative workbook paths of the files that exist in both hash sets and differ.
    let changedWorkbookPaths (before: ArcFileHashes) (after: ArcFileHashes) : string[] =
        let changedIn zone (beforeHashes: Map<string, int>) (afterHashes: Map<string, int>) =
            afterHashes
            |> Map.toList
            |> List.choose (fun (identifier, hash) ->
                match Map.tryFind identifier beforeHashes with
                | Some previous when previous <> hash -> Some(workbookPathOf zone identifier)
                | _ -> None
            )

        [|
            yield! changedIn ArcEntityPathRules.AddZone.Assays before.Assays after.Assays
            yield! changedIn ArcEntityPathRules.AddZone.Studies before.Studies after.Studies
            yield! changedIn ArcEntityPathRules.AddZone.Workflows before.Workflows after.Workflows
            yield! changedIn ArcEntityPathRules.AddZone.Runs before.Runs after.Runs

            if before.Investigation <> after.Investigation then
                ARCtrl.ArcPathHelper.InvestigationFileName
        |]

    /// Applies a finished delete to memory and returns the ARC-relative workbook paths of the surviving files whose
    /// memory content changed.
    /// The sync after a delete treats a file as unchanged when its disk hash equals its baseline, so this moves the
    /// baselines that the delete itself moved on disk. That keeps unsaved edits in files the delete rewrote.
    let applyEntityDeletion (deletion: ArcEntityDeletion) (arcLocal: ARC) : string[] =
        let id = deletion.Identifier

        let holdsTarget =
            match deletion.FileType with
            | ArcFilesDiscriminate.Assay -> arcLocal.ContainsAssay id
            | ArcFilesDiscriminate.Study -> arcLocal.ContainsStudy id
            | ArcFilesDiscriminate.Workflow -> arcLocal.ContainsWorkflow id
            | ArcFilesDiscriminate.Run -> arcLocal.ContainsRun id
            | _ -> false

        // An entity that an unsynced external add put on disk is not in memory. The sync sorts it out.
        if not holdsTarget then
            [||]
        else
            let hashesBefore = captureArcFileHashes arcLocal

            match deletion.FileType with
            | ArcFilesDiscriminate.Assay -> arcLocal.RemoveAssay id
            | ArcFilesDiscriminate.Study -> arcLocal.RemoveStudy id
            | ArcFilesDiscriminate.Workflow -> arcLocal.DeleteWorkflow id
            | _ -> arcLocal.DeleteRun id

            let keptPaths =
                arcLocal.FileSystem.Tree.ToFilePaths()
                |> Array.filter (fun path -> not (PathHelpers.isSameOrDescendantPath path deletion.FolderPath))

            arcLocal.SetFilePaths keptPaths

            let rebase (identifier: string) (current: int) (before: Map<string, int>) (after: Map<string, int>) =
                match Map.tryFind identifier before, Map.tryFind identifier after with
                | Some b, Some a when b <> a && current = b -> Some a
                | _ -> None

            for assay in arcLocal.Assays do
                rebase assay.Identifier assay.StaticHash deletion.DiskBefore.Assays deletion.DiskAfter.Assays
                |> Option.iter (fun hash -> assay.StaticHash <- hash)

            for study in arcLocal.Studies do
                rebase study.Identifier study.StaticHash deletion.DiskBefore.Studies deletion.DiskAfter.Studies
                |> Option.iter (fun hash -> study.StaticHash <- hash)

            for workflow in arcLocal.Workflows do
                rebase
                    workflow.Identifier
                    workflow.StaticHash
                    deletion.DiskBefore.Workflows
                    deletion.DiskAfter.Workflows
                |> Option.iter (fun hash -> workflow.StaticHash <- hash)

            for run in arcLocal.Runs do
                rebase run.Identifier run.StaticHash deletion.DiskBefore.Runs deletion.DiskAfter.Runs
                |> Option.iter (fun hash -> run.StaticHash <- hash)

            if
                deletion.DiskBefore.Investigation <> deletion.DiskAfter.Investigation
                && arcLocal.StaticHash = deletion.DiskBefore.Investigation
            then
                arcLocal.StaticHash <- deletion.DiskAfter.Investigation

            changedWorkbookPaths hashesBefore (captureArcFileHashes arcLocal)

    /// Deletes ARC entities on disk through ARCtrl contracts. Memory catches up through applyEntityDeletion and the
    /// sync at the end of the caller's external operation scope.
    let internal deleteArcEntityWithAsync
        (load: string -> JS.Promise<Result<ARC, string[]>>)
        (execute: string -> Contract[] -> JS.Promise<Result<Contract[], string[]>>)
        (arcPath: string)
        (relativePath: string)
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
                        // The remove contracts also update other files. ARCtrl parses an embedded DataMap copy with hash 0,
                        // so an unbaselined copy would be written over the DataMap file of an entity the delete leaves alone.
                        baselineArcStaticHashes diskArc

                        match tryEnsureArcEntityResolved fileType identifier canonicalFilePath diskArc with
                        | Error resolutionError -> return Error resolutionError
                        | Ok() ->
                            // The contract generators change diskArc, so the hashes are read directly around the removal.
                            let diskBefore = captureArcFileHashes diskArc
                            let dirty = ContractPolicy.captureDirtyDatamapKeys diskArc
                            let raw = removeContracts fileType identifier diskArc

                            let deletion diskAfter = {
                                FileType = fileType
                                Identifier = identifier
                                WorkbookPath = canonicalFilePath
                                FolderPath = PathHelpers.tryGetParentPath canonicalFilePath |> Option.defaultValue ""
                                DiskBefore = diskBefore
                                DiskAfter = diskAfter
                            }

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
                                // The folder delete runs first and the executor stops at the first failure, so a failed
                                // folder delete leaves the files that register the entity untouched.
                                match! execute arcPath contracts with
                                | Ok _ -> return Ok(Deleted(deletion (captureArcFileHashes diskArc)))
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

                                        // The registering files kept their disk content, so their baselines stay and the
                                        // removal shows as an unsaved change in memory.
                                        return Ok(PartiallyDeleted(deletion diskBefore, exn metadataMessage))
                with deleteError ->
                    return
                        Error(exn $"Could not delete ARC entity at '{normalizedRelativePath}': {deleteError.Message}")
        }

    let deleteArcEntityAsync
        (arcPath: string)
        (relativePath: string)
        : JS.Promise<Result<ArcEntityDeleteOutcome, exn>> =
        deleteArcEntityWithAsync ARC.LoadAsyncSwate ContractExecutor.fulfillContractsSwateAsync arcPath relativePath

    /// Deletes an entity inside an exclusive external operation scope and applies the delete to memory before the sync
    /// at the end of the scope. afterDelete runs once memory has been committed. A partial delete is applied too and
    /// then reported as the error.
    let deleteArcEntityInVault
        (vault: ArcVault)
        (normalizedRelativePath: string)
        (afterDelete: unit -> JS.Promise<unit>)
        : JS.Promise<Result<unit, exn>> =
        // An external operation: the sync at the end of the scope loads what ARCtrl changed.
        // Files this delete rewrote, such as the registration in a study, keep their local
        // edits because applyEntityDeletion moves their baselines first. The scope is
        // exclusive, because a second delete would load the same study and write the first
        // registration back.
        vault.TryWithExclusiveExternalOperationScope(
            announce = false,
            operation =
                fun () -> promise {
                    // A sync can have replaced the ARC while this call waited for the vault.
                    match vault.path, vault.arc with
                    | Some arcPath, Some _ ->
                        match! deleteArcEntityAsync arcPath normalizedRelativePath with
                        | Error deleteError -> return Error deleteError
                        | Ok outcome ->
                            let deletion, partialError =
                                match outcome with
                                | Deleted deletion -> deletion, None
                                | PartiallyDeleted(deletion, error) -> deletion, Some error

                            // No await between the read and the commit.
                            match vault.arc with
                            | Some arcLocal ->
                                let changed = applyEntityDeletion deletion arcLocal

                                vault.SetArc arcLocal

                                vault.PublishMemoryChange(deletion.WorkbookPath, changed)

                                vault.RefreshHasUnsavedArcChangesFlag()
                            | None -> ()

                            // The watcher reports this late, so the zone folder is reconciled here.
                            do! afterDelete ()

                            match partialError with
                            | None -> return Ok()
                            | Some error -> return Error error
                    | _ -> return Error(arcNotOpenError ())
                }
        )

    /// Deletes a DataMap as an own write. It loads external changes first, because the delete rewrites the parent workbook from its disk copy, and a change the write rewrites would otherwise count as its own.
    let deleteDataMapInVault
        (vault: ArcVault)
        (normalizedDataMapPath: string)
        (parentInfo: DatamapParentInfo)
        : JS.Promise<Result<string option, exn>> =
        vault.RunOwnWrite(
            exclusive = false,
            syncFirst = true,
            operation =
                fun diskState -> promise {
                    match diskState with
                    | UnsyncedFiles(_, syncError) ->
                        return
                            Error(
                                exn
                                    $"Could not delete the DataMap because the ARC files could not be loaded: {syncError.Message}"
                            )
                    | NoUnsyncedFiles ->
                        // A sync can have replaced the ARC while this call waited for the vault.
                        match vault.path, vault.arc with
                        | Some arcPath, Some arc ->
                            match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                            | Error deleteError -> return Error deleteError
                            | Ok() ->
                                vault.RefreshHasUnsavedArcChangesFlag()

                                let absoluteDataMapPath =
                                    Main.Bindings.Path.join [| arcPath; normalizedDataMapPath |]

                                return Ok(PathHelpers.tryGetParentPath absoluteDataMapPath)
                        | _ -> return Error(arcNotOpenError ())
                }
        )
