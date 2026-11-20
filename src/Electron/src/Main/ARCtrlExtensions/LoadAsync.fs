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

                    return
                        metadataEntries
                        |> Array.choose (fun metadataEntry ->
                            if
                                metadataEntry.isFile ()
                                && ArcEntityPathRules.isEntityMetadataFileName entity.Zone metadataEntry.name
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
                        ArcPathHelper.DatamapFileName
                    else
                        join [| parentPath; ArcPathHelper.DatamapFileName |]
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
                let repairs =
                    spec.CreateContracts identifier
                    |> Array.map (fun contract -> {
                        contract with
                            Operation = Operation.UPDATE
                    })

                match! ContractExecutor.fulfillContractsSwateAsync arcPath repairs with
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

    /// Separate datamap workbooks take precedence over legacy copies embedded in parent workbooks.
    let internal applySeparateDatamaps (arc: ARC) (fulfilled: Contract[]) : unit =
        for assay in arc.Assays do
            fulfilled
            |> Array.tryPick (Datamap.tryFromReadContractForAssay assay.Identifier)
            |> Option.iter (fun dm -> assay.Datamap <- Some dm)

        for study in arc.Studies do
            fulfilled
            |> Array.tryPick (Datamap.tryFromReadContractForStudy study.Identifier)
            |> Option.iter (fun dm -> study.Datamap <- Some dm)

        for workflow in arc.Workflows do
            fulfilled
            |> Array.tryPick (Datamap.tryFromReadContractForWorkflow workflow.Identifier)
            |> Option.iter (fun dm -> workflow.Datamap <- Some dm)

        for run in arc.Runs do
            fulfilled
            |> Array.tryPick (Datamap.tryFromReadContractForRun run.Identifier)
            |> Option.iter (fun dm -> run.Datamap <- Some dm)

    /// A CWL or YAML payload file that the load could not decode and left out.
    type PayloadLoadIssue = { Path: string; Message: string }

    /// Splits off CWL and YAML READ contracts that ARCtrl cannot decode. SetISAFromContracts decodes every CWL
    /// contract and the run.yml of each loaded run, and throws on the first failure. It accepts only contracts,
    /// so it decodes each kept payload again. An entity without a payload leaves its file untouched on save.
    let internal partitionDecodablePayloadContracts (contracts: Contract[]) : Contract[] * PayloadLoadIssue[] =
        // Fable erases Some around a string, so an option would read a missing message as success.
        let tryDecode (decode: string -> 'T) (text: string) : Result<unit, string> =
            try
                decode text |> ignore
                Ok()
            with error ->
                Error(
                    if String.IsNullOrEmpty error.Message then
                        string error
                    else
                        error.Message
                )

        let decodable = ResizeArray<Contract>()
        let issues = ResizeArray<PayloadLoadIssue>()

        for contract in contracts do
            let decoded =
                match contract with
                | {
                      Operation = Operation.READ
                      DTOType = Some DTOType.CWL
                      DTO = Some(DTO.Text text)
                  } -> tryDecode CWL.Decode.decodeCWLProcessingUnit text
                | {
                      Operation = Operation.READ
                      DTOType = Some DTOType.YAML
                      DTO = Some(DTO.Text text)
                  } -> tryDecode CWL.DecodeParameters.decodeYAMLParameterFile text
                | _ -> Ok()

            match decoded with
            | Ok() -> decodable.Add contract
            | Error message ->
                issues.Add {
                    Path = contract.Path
                    Message = message
                }

        decodable.ToArray(), issues.ToArray()

    type ARC with

        /// Loads ARC metadata without traversing payload folders and migrates legacy `isa_datamap` paths.
        /// The load skips undecodable CWL and YAML payloads and returns them as issues. It leaves their files
        /// on disk unchanged.
        static member LoadAsyncSwateWithIssues(arcPath: string) = promise {
            let! discoveredPaths = discoverStructuralArcFilePathsWithAsync readdirWithTypesAsync arcPath
            let! paths = migrateLegacyDataMapPathsAsync arcPath discoveredPaths
            let arc = ARC.fromFilePaths paths
            let contracts = arc.GetReadContracts()

            match! ContractExecutor.fulfillReadContractsSwateAsync arcPath contracts with
            | Ok fulfilledContracts ->
                let decodableContracts, issues =
                    partitionDecodablePayloadContracts fulfilledContracts

                arc.SetISAFromContracts decodableContracts
                applySeparateDatamaps arc decodableContracts
                return Ok(arc, issues)
            | Error errors -> return Error errors
        }

        /// Loads ARC metadata like LoadAsyncSwateWithIssues and logs each skipped payload as a warning.
        static member LoadAsyncSwate(arcPath: string) = promise {
            match! ARC.LoadAsyncSwateWithIssues arcPath with
            | Ok(arc, issues) ->
                for issue in issues do
                    Browser.Dom.console.warn (
                        $"Payload '{issue.Path}' could not be decoded and was not loaded: {issue.Message}"
                    )

                return Ok arc
            | Error errors -> return Error errors
        }

        /// Repairs only zero-byte canonical workbooks left by interrupted creates, then retries LoadAsyncSwate.
        /// ARCtrl #620 was fixed in 3.2.3; this wrapper retains recovery because LoadAsyncSwate bypasses
        /// ARC.tryLoadAsync and its upstream recovery path.
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
