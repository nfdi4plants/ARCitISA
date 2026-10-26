namespace Main.ARCtrlExtensions

open System
open ARCtrl
open ARCtrl.Contract
open Main.Bindings.Path
open Main.Bindings.Filesystem
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper

[<AutoOpen>]
module ArcLoadExtensions =

    /// Returns true when a path addresses Git's private repository metadata.
    /// `.gitignore`, `.gitattributes`, and similarly named files remain ordinary ARC payload.
    let isGitMetadataPath (pathValue: string) =
        pathValue
        |> getNonEmptyPathParts
        |> Array.exists (fun segment -> String.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase))

    type private StructuralEntityDirectory = {
        Zone: ArcEntityPathRules.AddZone
        ZoneName: string
        ZonePath: string
        EntityName: string
    }

    let internal discoverStructuralArcFilePathsWithAsync
        (readEntries: string -> ReaddirOptions -> Fable.Core.JS.Promise<Dirent[]>)
        (arcPath: string)
        =
        promise {
            // Sorting every listing by name makes the result independent of readdir and completion order.
            let readDirectory path options = promise {
                let! entries = readEntries path options

                return
                    entries
                    |> Array.sortWith (fun left right -> PathOrder.compareNames left.name right.name)
            }

            // The root snapshot is authoritative: zones absent here are optional and are never read.
            let! rootEntries = readDirectory arcPath (ReaddirOptions(withFileTypes = true))

            let structuralRootFilePaths =
                rootEntries
                |> Array.choose (fun entry ->
                    if
                        entry.isFile ()
                        && PathHelpers.pathMatchesAny ArcEntityPathRules.structuralRootFileNames entry.name
                    then
                        Some entry.name
                    else
                        None
                )

            let existingZones =
                ArcEntityPathRules.allAddZones
                |> List.choose (fun zone ->
                    let zoneFolder = ArcEntityPathRules.zoneFolderName zone

                    rootEntries
                    |> Array.tryFind (fun entry ->
                        entry.isDirectory () && PathHelpers.pathsEqual entry.name zoneFolder
                    )
                    |> Option.map (fun zoneEntry -> zone, zoneEntry.name, join [| arcPath; zoneEntry.name |])
                )
                |> List.toArray

            // Node's thread pool bounds how many of these reads run at once.
            let! zoneEntries =
                existingZones
                |> Array.map (fun (zone, zoneName, zonePath) -> promise {
                    let! entries = readDirectory zonePath (ReaddirOptions(withFileTypes = true))
                    return zone, zoneName, zonePath, entries
                })
                |> Promise.all

            // Zone reads reveal only the entity directories. Discovery never descends into payload folders.
            let entityDirectories =
                zoneEntries
                |> Array.collect (fun (zone, zoneName, zonePath, entries) ->
                    entries
                    |> Array.choose (fun entry ->
                        if entry.isDirectory () then
                            Some {
                                Zone = zone
                                ZoneName = zoneName
                                ZonePath = zonePath
                                EntityName = entry.name
                            }
                        else
                            None
                    )
                )

            let! entityMetadataPaths =
                entityDirectories
                |> Array.map (fun entity -> promise {
                    let entityPath = join [| entity.ZonePath; entity.EntityName |]
                    // An entity folder that vanished after the zone listing counts as absent.
                    let! metadataEntries = promise {
                        try
                            return! readDirectory entityPath (ReaddirOptions(withFileTypes = true))
                        with readError ->
                            match tryGetNodeErrorCode readError with
                            | Some "ENOENT"
                            | Some "ENOTDIR" -> return [||]
                            | _ -> return raise readError
                    }

                    let allowedFileNames = [|
                        ArcEntityPathRules.zoneEntityFileName entity.Zone
                        ArcPathHelper.DataMapFileName
                        ArcEntityPathRules.legacyDataMapFileName
                    |]

                    return
                        metadataEntries
                        |> Array.choose (fun metadataEntry ->
                            if
                                metadataEntry.isFile ()
                                && allowedFileNames |> Array.exists (PathHelpers.pathsEqual metadataEntry.name)
                            then
                                Some $"{entity.ZoneName}/{entity.EntityName}/{metadataEntry.name}"
                            else
                                None
                        )
                })
                |> Promise.all

            // Root files come first, then zones in fixed order, each level sorted by name.
            return
                Array.append structuralRootFilePaths (Array.concat entityMetadataPaths)
                |> Array.map PathHelpers.normalizePath
        }

    let migrateLegacyDataMapPathsAsync (arcPath: string) (paths: string[]) = promise {
        let migratedPaths = ResizeArray<string>()

        for relativePath in paths do
            if isLegacyDataMapPath relativePath then
                let parentPath = dirname relativePath

                let canonicalRelativePath =
                    if parentPath = "." then
                        ArcPathHelper.DataMapFileName
                    else
                        join [| parentPath; ArcPathHelper.DataMapFileName |]
                    |> PathHelpers.normalizePath

                let legacyAbsolutePath = join [| arcPath; relativePath |]
                let canonicalAbsolutePath = join [| arcPath; canonicalRelativePath |]

                if existsSync canonicalAbsolutePath then
                    Browser.Dom.console.warn (
                        $"Outdated DataMap file '{relativePath}' was ignored because '{canonicalRelativePath}' already exists."
                    )
                else
                    do! renameAsync legacyAbsolutePath canonicalAbsolutePath

                    Browser.Dom.console.warn (
                        $"Outdated DataMap file '{relativePath}' was migrated to '{canonicalRelativePath}'."
                    )

                    migratedPaths.Add canonicalRelativePath
            else
                migratedPaths.Add relativePath

        return migratedPaths.ToArray()
    }

    type private CanonicalArcFileRepairSpec = {
        CollectionFolder: string
        FileName: string
        CreateContracts: string -> Contract[]
    }

    let private createDefaultArcFileContracts (fileType: ArcFilesDiscriminate) (identifier: string) =
        ARCtrlHelper.ArcFileDefaults.createDefaultArcFile fileType identifier
        |> ArcFileCreateContracts.createContracts false

    let private canonicalArcFileRepairSpecs = [|
        {
            CollectionFolder = "assays"
            FileName = "isa.assay.xlsx"
            CreateContracts = createDefaultArcFileContracts ArcFilesDiscriminate.Assay
        }
        {
            CollectionFolder = "studies"
            FileName = "isa.study.xlsx"
            CreateContracts = createDefaultArcFileContracts ArcFilesDiscriminate.Study
        }
        {
            CollectionFolder = "workflows"
            FileName = "isa.workflow.xlsx"
            CreateContracts = createDefaultArcFileContracts ArcFilesDiscriminate.Workflow
        }
        {
            CollectionFolder = "runs"
            FileName = "isa.run.xlsx"
            CreateContracts = createDefaultArcFileContracts ArcFilesDiscriminate.Run
        }
    |]

    let private isZeroByteZipReadError (errors: string[]) =
        errors
        |> Array.exists (fun error ->
            let normalizedError = error.ToLowerInvariant()

            normalizedError.Contains("error reading contract")
            && normalizedError.Contains("data length = 0")
        )

    let private tryReadDirectoryAsync (directoryPath: string) = promise {
        try
            return! readdirAsync directoryPath
        with _ ->
            return [||]
    }

    let private tryGetFileSizeAsync (filePath: string) = promise {
        try
            let! stats = statAsync filePath
            return Some stats.size
        with _ ->
            return None
    }

    let private repairZeroByteCanonicalArcFile
        (arcPath: string)
        (spec: CanonicalArcFileRepairSpec)
        (identifier: string)
        =
        promise {
            let absolutePath =
                join [|
                    arcPath
                    spec.CollectionFolder
                    identifier
                    spec.FileName
                |]

            let! fileSize = tryGetFileSizeAsync absolutePath

            match fileSize with
            | Some size when size = 0.0 ->
                match! fullFillContractBatchAsync arcPath (spec.CreateContracts identifier) with
                | Ok _ -> return true
                | Error _ -> return false
            | _ -> return false
        }

    let private repairZeroByteCanonicalArcFiles (arcPath: string) = promise {
        let mutable repairedAny = false

        for spec in canonicalArcFileRepairSpecs do
            let collectionPath = join [| arcPath; spec.CollectionFolder |]
            let! identifiers = tryReadDirectoryAsync collectionPath

            for identifier in identifiers do
                let! repaired = repairZeroByteCanonicalArcFile arcPath spec identifier
                repairedAny <- repairedAny || repaired

        return repairedAny
    }

    type ARC with

        /// Hotfix for #619, not fixed in the consumed ARCtrl 3.0.0-beta.12.
        /// Mirrors ARC.tryLoadAsync. It lists the root, the zone folders and the entity folders to find the
        /// metadata files, and never reads payload folders.
        static member LoadAsyncSwate(arcPath: string) = promise {
            let! discoveredPaths = discoverStructuralArcFilePathsWithAsync readdirWithTypesAsync arcPath
            let! paths = migrateLegacyDataMapPathsAsync arcPath discoveredPaths
            let arc = ARC.fromFilePaths paths
            let contracts = arc.GetReadContracts()

            match! fullFillContractBatchAsync arcPath contracts with
            | Ok fulfilledContracts ->
                arc.SetISAFromContracts fulfilledContracts
                return Ok arc
            | Error errors -> return Error errors
        }

        /// Hotfix for #620, not fixed in the consumed ARCtrl 3.0.0-beta.12.
        /// Repairs only zero-byte canonical workbooks left by interrupted creates, then retries LoadAsyncSwate.
        static member LoadAsyncSwateZeroByteRepair(arcPath: string) = promise {
            match! ARC.LoadAsyncSwate arcPath with
            | Ok arc ->
                baselineArcStaticHashes arc
                return Ok arc
            | Error errors when isZeroByteZipReadError errors ->
                let! repairedAny = repairZeroByteCanonicalArcFiles arcPath

                if repairedAny then
                    match! ARC.LoadAsyncSwate arcPath with
                    | Ok arc ->
                        baselineArcStaticHashes arc
                        return Ok arc
                    | Error errors -> return Error errors
                else
                    return Error errors
            | Error errors -> return Error errors
        }
