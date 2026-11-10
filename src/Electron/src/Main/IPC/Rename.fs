module Main.IPC.Rename

open System
open Fable.Core
open ARCtrl
open ARCtrl.Contract
open Main
open Main.ArcVaultState
open Main.ArcVaultSync
open Main.ARCtrlExtensions
open Main.ArcMerge
open Main.ArcVaultHelper
open Main.IPC.Delete
open Main.IPC.FileSystemIO
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.RenamePathRules

/// What a finished rename produced.
type ArcEntityRename = {
    Arc: ARC
    /// ARC-relative path of the renamed entity's workbook before the rename.
    OldWorkbookPath: string
    /// ARC-relative workbook paths of the files whose memory content the rename changed.
    ChangedPaths: string[]
}

[<RequireQualifiedAccess>]
module ArcRenameHelper =

    let private applyInMemoryRename fileType oldIdentifier newIdentifier (arc: ARC) =
        match fileType with
        | ArcFilesDiscriminate.Assay -> arc.RenameAssay(oldIdentifier, newIdentifier)
        | ArcFilesDiscriminate.Study -> arc.RenameStudy(oldIdentifier, newIdentifier)
        | ArcFilesDiscriminate.Workflow -> arc.RenameWorkflow(oldIdentifier, newIdentifier)
        | ArcFilesDiscriminate.Run -> arc.RenameRun(oldIdentifier, newIdentifier)
        | fileType -> failwith $"Renaming {fileType} is not supported."

    let private remapArcFileSystemPaths sourcePath targetPath (arc: ARC) =
        let remappedPaths =
            arc.FileSystem.Tree.ToFilePaths()
            |> Array.map (fun path ->
                PathHelpers.tryRemapPathPrefix sourcePath targetPath path
                |> Option.defaultWith (fun () -> PathHelpers.normalizeCanonicalRelativePath path)
            )
            |> Array.distinctBy PathHelpers.normalizeForComparison

        arc.SetFilePaths(remappedPaths)

    /// Applies the rename to a copy of the local ARC. Entities without unsaved edits take their hash from memory, because
    /// the disk rename wrote the same change. An entity with unsaved edits stays unsaved.
    let private applyRenameInMemory sourcePath targetPath fileType oldIdentifier newIdentifier (arcLocal: ARC) : ARC =
        let renamedArc = copyArcPreservingStaticHashes arcLocal

        let identifierAfterRename kind (identifier: string) =
            if fileType = kind && identifier = oldIdentifier then
                newIdentifier
            else
                identifier

        let cleanIdentifiers (entities: seq<'Entity>) getIdentifier hasChanges =
            entities
            |> Seq.filter (hasChanges >> not)
            |> Seq.map getIdentifier
            |> Seq.toArray

        // Captured before the rename, because the rename changes the hashes of every entity that it touches.
        let arcWasClean = renamedArc.StaticHash = renamedArc.GetLightHashCode()

        let cleanAssays =
            cleanIdentifiers renamedArc.Assays (fun (e: ArcAssay) -> e.Identifier) (fun e -> e.hasInMemoryChanges ())

        let cleanStudies =
            cleanIdentifiers renamedArc.Studies (fun (e: ArcStudy) -> e.Identifier) (fun e -> e.hasInMemoryChanges ())

        let cleanWorkflows =
            cleanIdentifiers
                renamedArc.Workflows
                (fun (e: ArcWorkflow) -> e.Identifier)
                (fun e -> e.hasInMemoryChanges ())

        let cleanRuns =
            cleanIdentifiers renamedArc.Runs (fun (e: ArcRun) -> e.Identifier) (fun e -> e.hasInMemoryChanges ())

        applyInMemoryRename fileType oldIdentifier newIdentifier renamedArc
        remapArcFileSystemPaths sourcePath targetPath renamedArc

        let rebaselineDataMap (dataMap: Datamap option) =
            dataMap |> Option.iter (fun dm -> dm.StaticHash <- cleanDataMapStaticHash dm)

        for identifier in cleanAssays do
            renamedArc.TryGetAssay(identifierAfterRename ArcFilesDiscriminate.Assay identifier)
            |> Option.iter (fun e ->
                e.StaticHash <- e.GetLightHashCode()
                rebaselineDataMap e.Datamap
            )

        for identifier in cleanStudies do
            renamedArc.TryGetStudy(identifierAfterRename ArcFilesDiscriminate.Study identifier)
            |> Option.iter (fun e ->
                e.StaticHash <- e.GetLightHashCode()
                rebaselineDataMap e.Datamap
            )

        for identifier in cleanWorkflows do
            renamedArc.TryGetWorkflow(identifierAfterRename ArcFilesDiscriminate.Workflow identifier)
            |> Option.iter (fun e ->
                e.StaticHash <- e.GetLightHashCode()
                rebaselineDataMap e.Datamap
            )

        for identifier in cleanRuns do
            renamedArc.TryGetRun(identifierAfterRename ArcFilesDiscriminate.Run identifier)
            |> Option.iter (fun e ->
                e.StaticHash <- e.GetLightHashCode()
                rebaselineDataMap e.Datamap
            )

        if arcWasClean then
            renamedArc.StaticHash <- renamedArc.GetLightHashCode()

        renamedArc

    let private renameResolvedArcEntityAsync
        (arcPath: string)
        sourcePath
        targetPath
        canonicalSourcePath
        fileType
        oldIdentifier
        newIdentifier
        (getArcLocal: unit -> ARC option)
        : JS.Promise<Result<ArcEntityRename, exn>> =
        promise {
            try
                match tryResolveArcRelativePath arcPath targetPath with
                | Error pathError -> return Error pathError
                | Ok targetAbsolutePath ->
                    let! targetExists = pathExistsAsync targetAbsolutePath

                    if targetExists then
                        return
                            Error(
                                exn
                                    $"Cannot rename '{sourcePath}' to '{targetPath}' because the destination already exists."
                            )
                    else
                        match! ARC.LoadAsyncSwate arcPath with
                        | Error errors ->
                            return
                                Error(
                                    exn
                                        $"Could not load ARC from disk before renaming '{sourcePath}': {PathHelpers.formatContractErrors errors}"
                                )
                        | Ok diskArc ->
                            baselineArcStaticHashes diskArc

                            match
                                ArcDeleteHelper.tryEnsureArcEntityResolved
                                    fileType
                                    oldIdentifier
                                    canonicalSourcePath
                                    diskArc
                            with
                            | Error resolutionError -> return Error resolutionError
                            | Ok() ->
                                let dirtyBefore = ContractPolicy.captureDirtyDatamapKeys diskArc

                                let raw =
                                    match fileType with
                                    | ArcFilesDiscriminate.Assay ->
                                        diskArc.GetAssayRenameContracts(oldIdentifier, newIdentifier)
                                    | ArcFilesDiscriminate.Study ->
                                        diskArc.GetStudyRenameContracts(oldIdentifier, newIdentifier)
                                    | ArcFilesDiscriminate.Workflow ->
                                        diskArc.GetWorkflowRenameContracts(oldIdentifier, newIdentifier)
                                    | ArcFilesDiscriminate.Run ->
                                        diskArc.GetRunRenameContracts(oldIdentifier, newIdentifier)
                                    | _ -> failwith $"Renaming {fileType} is not supported."

                                let dirty = ContractPolicy.remapKeys sourcePath targetPath dirtyBefore

                                let diskPathBefore path =
                                    PathHelpers.tryRemapPathPrefix targetPath sourcePath path
                                    |> Option.defaultValue path

                                match!
                                    ContractPolicy.prepareAsync {
                                        ArcPath = arcPath
                                        Snapshot = diskArc
                                        RawContracts = raw
                                        DirtyDatamapKeys = dirty
                                        IsFullWrite = false
                                        DiskPathBeforeExecution = diskPathBefore
                                    }
                                with
                                | Error message ->
                                    return
                                        Error(
                                            exn
                                                $"Could not rename ARC entity from '{sourcePath}' to '{targetPath}': {message}"
                                        )
                                | Ok contracts ->
                                    match! ContractExecutor.fulfillContractsSwateAsync arcPath contracts with
                                    | Error errors ->
                                        return
                                            Error(
                                                exn
                                                    $"Could not rename ARC entity from '{sourcePath}' to '{targetPath}': {PathHelpers.formatContractErrors errors}"
                                            )
                                    | Ok _ ->
                                        // Read after the disk rename: an edit that arrived during it is in this ARC.
                                        match getArcLocal () with
                                        | None -> return Error(arcNotOpenError ())
                                        | Some arcLocal ->
                                            let renamedArc =
                                                applyRenameInMemory
                                                    sourcePath
                                                    targetPath
                                                    fileType
                                                    oldIdentifier
                                                    newIdentifier
                                                    arcLocal

                                            return
                                                Ok {
                                                    Arc = renamedArc
                                                    OldWorkbookPath = canonicalSourcePath
                                                    ChangedPaths =
                                                        ArcDeleteHelper.changedWorkbookPaths
                                                            (ArcDeleteHelper.captureArcFileHashes arcLocal)
                                                            (ArcDeleteHelper.captureArcFileHashes renamedArc)
                                                }
            with renameError ->
                let mappedError = mapRenameDiskError sourcePath targetPath renameError

                return
                    Error(
                        exn $"Could not rename ARC entity from '{sourcePath}' to '{targetPath}': {mappedError.Message}"
                    )
        }

    let private validateEntityRenameSourceClassification (classification: ArcEntityPathRules.RenamePathClassification) =
        match classification with
        | ArcEntityPathRules.RenamePathClassification.EntityFolderTarget(zone, identifier, normalizedRelativePath) ->
            Ok(zone, identifier, normalizedRelativePath)
        | ArcEntityPathRules.RenamePathClassification.RootTarget -> Error(exn "Renaming the ARC root is not allowed.")
        | ArcEntityPathRules.RenamePathClassification.DisallowedTarget _ ->
            Error(exn "Rename path must not contain path traversal segments.")
        | ArcEntityPathRules.RenamePathClassification.ProtectedTarget _ ->
            Error(exn "Renaming protected files (for example .gitkeep or readme.md) is not allowed.")
        | ArcEntityPathRules.RenamePathClassification.InvestigationFileTarget _ ->
            Error(exn "Renaming the investigation file is not supported.")
        | ArcEntityPathRules.RenamePathClassification.AddZoneRootTarget _ ->
            Error(exn "Renaming add-zone root folders (studies/, assays/, workflows/, runs/) is not allowed.")
        | ArcEntityPathRules.RenamePathClassification.CanonicalEntityFileTarget _
        | ArcEntityPathRules.RenamePathClassification.CanonicalDataMapFileTarget _ ->
            Error(exn "Renaming canonical ARC files is not supported. Rename the containing ARC entity folder instead.")
        | ArcEntityPathRules.RenamePathClassification.GenericTarget _ ->
            Error(exn "Renaming generic files or folders uses the generic filesystem rename path.")

    /// Performs the ARCtrl entity rename contract for an entity-folder rename request.
    let renameArcEntityAsync
        (arcPath: string)
        (request: RenamePathRequest)
        (getArcLocal: unit -> ARC option)
        : JS.Promise<Result<ArcEntityRename, exn>> =
        promise {
            let requestedRelativePath =
                request.relativePath |> PathHelpers.normalizeCanonicalRelativePath

            let sourceClassification =
                ArcEntityPathRules.classifyRenameTarget requestedRelativePath

            match validateEntityRenameSourceClassification sourceClassification with
            | Error validationError -> return Error validationError
            | Ok(sourceZone, sourceIdentifier, sourcePath) ->
                let sourceFileType = ArcDeleteHelper.arcFileTypeForZone sourceZone

                match ArcDeleteHelper.tryCanonicalEntityFilePath sourceZone sourceIdentifier with
                | Error canonicalPathError -> return Error canonicalPathError
                | Ok canonicalSourcePath ->
                    match
                        getArcLocal ()
                        |> Option.map (
                            ArcDeleteHelper.tryEnsureArcEntityResolved
                                sourceFileType
                                sourceIdentifier
                                canonicalSourcePath
                        )
                    with
                    | None -> return Error(arcNotOpenError ())
                    | Some(Error resolutionError) -> return Error resolutionError
                    | Some(Ok()) ->
                        match tryBuildRenameTargetPath sourcePath request.newName with
                        | Error targetPathError -> return Error(exn targetPathError)
                        | Ok targetPath ->
                            let targetIdentifier = PathHelpers.getNameFromPath targetPath

                            return!
                                renameResolvedArcEntityAsync
                                    arcPath
                                    sourcePath
                                    targetPath
                                    canonicalSourcePath
                                    sourceFileType
                                    sourceIdentifier
                                    targetIdentifier
                                    getArcLocal
        }

    /// Renames an entity inside an own write and publishes the memory change, as deleteArcEntityInVault does for a
    /// delete.
    let renameArcEntityInVault (vault: ArcVault) (request: RenamePathRequest) : JS.Promise<Result<unit, exn>> =
        vault.RunOwnWrite(
            exclusive = false,
            syncFirst = true,
            operation =
                fun diskState -> promise {
                    match diskState with
                    | UnsyncedFiles(_, syncError) ->
                        return
                            Error(
                                exn $"Could not rename because the ARC files could not be loaded: {syncError.Message}"
                            )
                    | NoUnsyncedFiles ->
                        // A sync can have replaced the ARC while this call waited for the vault.
                        match vault.path, vault.arc with
                        | Some arcPath, Some _ ->
                            match! renameArcEntityAsync arcPath request (fun () -> vault.arc) with
                            | Error renameError -> return Error renameError
                            | Ok rename ->
                                vault.SetArc rename.Arc
                                vault.PublishMemoryChange(rename.OldWorkbookPath, rename.ChangedPaths)
                                vault.RefreshHasUnsavedArcChangesFlag()

                                return Ok()
                        | _ -> return Error(arcNotOpenError ())
                }
        )
