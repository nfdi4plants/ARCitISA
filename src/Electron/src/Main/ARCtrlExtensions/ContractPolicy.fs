module Main.ARCtrlExtensions.ContractPolicy

open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open ARCtrl.Spreadsheet
open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared

let private key = ArcPathHelper.normalizePathKey

let internal assayWorkbook (assay: ArcAssay) =
    ArcAssay.toFsWorkbook (assay, datamapSheet = false)

let internal studyWorkbook (study: ArcStudy) =
    ArcStudy.toFsWorkbook (study, datamapSheet = false)

let private parentWorkbookPath (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> Identifier.Assay.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Study -> Identifier.Study.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Run -> Identifier.Run.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Workflow -> Identifier.Workflow.fileNameFromIdentifier parentInfo.ParentId

let private datamapPath (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> Identifier.Assay.datamapFileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Study -> Identifier.Study.datamapFileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Run -> Identifier.Run.datamapFileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Workflow -> Identifier.Workflow.datamapFileNameFromIdentifier parentInfo.ParentId

let private datamapParents (arc: ARC) : (DatamapParentInfo * Datamap) list = [
    for assay in arc.Assays do
        match assay.Datamap with
        | Some dm -> yield DatamapParentInfo.create assay.Identifier DataMapParent.Assay, dm
        | None -> ()
    for study in arc.Studies do
        match study.Datamap with
        | Some dm -> yield DatamapParentInfo.create study.Identifier DataMapParent.Study, dm
        | None -> ()
    for workflow in arc.Workflows do
        match workflow.Datamap with
        | Some dm -> yield DatamapParentInfo.create workflow.Identifier DataMapParent.Workflow, dm
        | None -> ()
    for run in arc.Runs do
        match run.Datamap with
        | Some dm -> yield DatamapParentInfo.create run.Identifier DataMapParent.Run, dm
        | None -> ()
]

let internal captureDirtyDatamapKeys (arc: ARC) : Set<string> =
    datamapParents arc
    |> List.choose (fun (parentInfo, dm) ->
        if dm.hasInMemoryChanges () then
            Some(key (datamapPath parentInfo))
        else
            None
    )
    |> Set.ofList

let internal remapKeys (oldFolder: string) (newFolder: string) (keys: Set<string>) : Set<string> =
    keys
    |> Set.map (fun k ->
        PathHelpers.tryRemapPathPrefix oldFolder newFolder k
        |> Option.defaultValue k
        |> key
    )

let internal distinctContracts (contracts: Contract[]) =
    contracts
    |> Array.groupBy (fun contract -> key contract.Path)
    |> Array.map (snd >> Array.last)

let private isModelContract (arc: ARC) (contract: Contract) =
    match contract.DTOType with
    | Some DTOType.ISA_Investigation
    | Some DTOType.ISA_Study
    | Some DTOType.ISA_Assay
    | Some DTOType.ISA_Workflow
    | Some DTOType.ISA_Run
    | Some DTOType.ISA_Datamap
    | Some DTOType.CWL
    | Some DTOType.YAML -> true
    | Some DTOType.PlainText ->
        arc.License
        |> Option.exists (fun license -> key license.Path = key contract.Path)
    | _ -> false

let private normalizeModelContracts
    (arc: ARC)
    (dirtyDatamapKeys: Set<string>)
    (isFullWrite: bool)
    (contracts: Contract[])
    =
    // Empty datamaps use a nonzero clean sentinel. Upstream otherwise emits them on every save.
    contracts
    |> Array.filter (fun contract ->
        isFullWrite
        || contract.DTOType <> Some DTOType.ISA_Datamap
        || Set.contains (key contract.Path) dirtyDatamapKeys
    )
    |> Array.map (fun contract ->
        if contract.Operation = Operation.CREATE && isModelContract arc contract then
            {
                contract with
                    Operation = Operation.UPDATE
            }
        else
            contract
    )
    |> distinctContracts

/// Regenerates assay and study payloads with the datamap stored only in its separate workbook.
let internal regenerateParentPayloads (arc: ARC) (contracts: Contract[]) : Result<Contract[], string> =
    let regenerated =
        contracts
        |> Array.map (fun contract ->
            match contract.Operation, contract.DTOType, contract.DTO with
            | (Operation.CREATE | Operation.UPDATE), Some DTOType.ISA_Assay, Some(DTO.Spreadsheet _) ->
                arc.Assays
                |> Seq.tryFind (fun assay ->
                    key (Identifier.Assay.fileNameFromIdentifier assay.Identifier) = key contract.Path
                )
                |> function
                    | Some assay ->
                        Ok {
                            contract with
                                DTO = Some(DTO.Spreadsheet(assayWorkbook assay))
                        }
                    | None -> Error $"Could not find the assay/study for contract '{contract.Path}'."
            | (Operation.CREATE | Operation.UPDATE), Some DTOType.ISA_Study, Some(DTO.Spreadsheet _) ->
                arc.Studies
                |> Seq.tryFind (fun study ->
                    key (Identifier.Study.fileNameFromIdentifier study.Identifier) = key contract.Path
                )
                |> function
                    | Some study ->
                        Ok {
                            contract with
                                DTO = Some(DTO.Spreadsheet(studyWorkbook study))
                        }
                    | None -> Error $"Could not find the assay/study for contract '{contract.Path}'."
            | _ -> Ok contract
        )

    match
        regenerated
        |> Array.tryPick (
            function
            | Error error -> Some error
            | Ok _ -> None
        )
    with
    | Some error -> Error error
    | None -> Ok(regenerated |> Array.choose Result.toOption)

let private isStructural (contract: Contract) =
    contract.Operation = Operation.DELETE || contract.Operation = Operation.RENAME

let private isDatamapWrite (contract: Contract) =
    contract.DTOType = Some DTOType.ISA_Datamap
    && (contract.Operation = Operation.CREATE || contract.Operation = Operation.UPDATE)

/// Folder changes precede datamap exports, which must succeed before a parent loses its embedded copy.
let internal orderContracts (contracts: Contract[]) : Contract[] =
    let structural, remaining = contracts |> Array.partition isStructural
    let datamaps, other = remaining |> Array.partition isDatamapWrite
    Array.concat [| structural; datamaps; other |]

let private parentHasEmbeddedDatamapAsync
    (arcPath: string)
    (parentInfo: DatamapParentInfo)
    (relativeParentPath: string)
    : JS.Promise<bool> =
    promise {
        try
            if not (existsSync (join [| arcPath; relativeParentPath |])) then
                return false
            else
                let dtoType =
                    match parentInfo.Parent with
                    | DataMapParent.Assay -> DTOType.ISA_Assay
                    | DataMapParent.Study -> DTOType.ISA_Study
                    | DataMapParent.Run -> DTOType.ISA_Run
                    | DataMapParent.Workflow -> DTOType.ISA_Workflow

                let contract = Contract.createRead (relativeParentPath, dtoType)

                match! ContractExecutor.fulfillReadContractsSwateAsync arcPath [| contract |] with
                | Ok [| { DTO = Some(DTO.Spreadsheet workbook) } |] ->
                    return
                        workbook.GetWorksheets()
                        |> Seq.exists (fun worksheet ->
                            try
                                (DatamapTable.tryFromFsWorksheet worksheet).IsSome
                            with _ ->
                                false
                        )
                | _ -> return false
        with _ ->
            return false
    }

let private parentUpdate (arc: ARC) (parentInfo: DatamapParentInfo) =
    let path = parentWorkbookPath parentInfo

    match parentInfo.Parent with
    | DataMapParent.Assay ->
        Contract.createUpdate (
            path,
            DTOType.ISA_Assay,
            DTO.Spreadsheet(assayWorkbook (arc.GetAssay parentInfo.ParentId))
        )
    | DataMapParent.Study ->
        Contract.createUpdate (
            path,
            DTOType.ISA_Study,
            DTO.Spreadsheet(studyWorkbook (arc.GetStudy parentInfo.ParentId))
        )
    | DataMapParent.Run -> (arc.GetRun parentInfo.ParentId).ToUpdateContract()
    | DataMapParent.Workflow -> (arc.GetWorkflow parentInfo.ParentId).ToUpdateContract()

type PreparationInput = {
    ArcPath: string
    Snapshot: ARC
    RawContracts: Contract[]
    DirtyDatamapKeys: Set<string>
    IsFullWrite: bool
    DiskPathBeforeExecution: string -> string
}

/// Prepares only touched parents and datamaps without changing the snapshot's saved hashes.
let internal prepareAsync (input: PreparationInput) : JS.Promise<Result<Contract[], string>> = promise {
    let normalized =
        normalizeModelContracts input.Snapshot input.DirtyDatamapKeys input.IsFullWrite input.RawContracts

    match regenerateParentPayloads input.Snapshot normalized with
    | Error error -> return Error error
    | Ok regenerated ->
        let writtenKeys =
            regenerated
            |> Array.filter (fun contract ->
                contract.Operation = Operation.CREATE || contract.Operation = Operation.UPDATE
            )
            |> Array.map (fun contract -> key contract.Path)
            |> Set.ofArray

        let additions = ResizeArray<Contract>()

        for parentInfo, dm in datamapParents input.Snapshot do
            let parentPath = parentWorkbookPath parentInfo
            let dmPath = datamapPath parentInfo
            let parentWritten = Set.contains (key parentPath) writtenKeys
            let datamapWritten = Set.contains (key dmPath) writtenKeys

            if parentWritten && not datamapWritten then
                if not (existsSync (join [| input.ArcPath; input.DiskPathBeforeExecution dmPath |])) then
                    additions.Add(DataMapContract.update parentInfo dm)
            elif datamapWritten && not parentWritten then
                let! hasEmbedded =
                    parentHasEmbeddedDatamapAsync input.ArcPath parentInfo (input.DiskPathBeforeExecution parentPath)

                if hasEmbedded then
                    additions.Add(parentUpdate input.Snapshot parentInfo)

        let result = Array.append regenerated (additions.ToArray()) |> orderContracts

        let duplicate =
            result
            |> Array.filter (isStructural >> not)
            |> Array.groupBy (fun contract -> key contract.Path)
            |> Array.tryFind (fun (_, matching) -> matching.Length > 1)

        match duplicate with
        | Some(path, _) -> return Error $"Multiple non-structural contracts target '{path}'."
        | None -> return Ok result
}
