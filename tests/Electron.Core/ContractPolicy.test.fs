module ElectronCore.ContractPolicyTests

open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open ARCtrl.Spreadsheet
open Fable.Core
open Main.ARCtrlExtensions
open Main.ARCtrlExtensions.ContractExecutor
open Main.ARCtrlExtensions.ContractPolicy
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open Vitest

let private key = ArcPathHelper.normalizePathKey

let private contractKeys (contracts: Contract[]) =
    contracts |> Array.map (fun contract -> key contract.Path)

let private expectPrepared (result: Result<Contract[], string>) =
    match result with
    | Ok contracts -> contracts
    | Error error -> failwith error

let private expectExecuted (result: Result<Contract[], string[]>) =
    match result with
    | Ok contracts -> contracts
    | Error errors -> failwith (String.concat "\n" errors)

let private withTempDirectory (testBody: string -> JS.Promise<unit>) = promise {
    let! arcPath = TestHelpers.createTempDirectoryAsync "swate-contract-policy-"

    try
        do! testBody arcPath
        do! TestHelpers.removeDirectoryAsync arcPath
    with error ->
        do! TestHelpers.removeDirectoryAsync arcPath
        return raise error
}

let private dataMap filePath =
    let dataMap = Datamap.init ()
    dataMap.DataContexts.Add(DataContext(name = filePath))
    dataMap

let private parentPath (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> Identifier.Assay.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Study -> Identifier.Study.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Run -> Identifier.Run.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Workflow -> Identifier.Workflow.fileNameFromIdentifier parentInfo.ParentId

let private dataMapPath (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> Identifier.Assay.datamapFileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Study -> Identifier.Study.datamapFileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Run -> Identifier.Run.datamapFileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Workflow -> Identifier.Workflow.datamapFileNameFromIdentifier parentInfo.ParentId

let private fixture parent identifier =
    let arc = ARC("ContractPolicy")
    let parentInfo = DatamapParentInfo.create identifier parent
    let dm = dataMap $"dataset/{identifier}.csv"

    match parent with
    | DataMapParent.Assay -> arc.AddAssay(ArcAssay(identifier, datamap = dm))
    | DataMapParent.Study -> arc.AddStudy(ArcStudy(identifier, datamap = dm))
    | DataMapParent.Run -> arc.AddRun(ArcRun(identifier, datamap = dm))
    | DataMapParent.Workflow -> arc.AddWorkflow(ArcWorkflow(identifier, datamap = dm))

    arc, parentInfo, dm

/// Builds both legacy embedded workbooks and current separate-datamap workbooks.
let private parentUpdate (arc: ARC) (parentInfo: DatamapParentInfo) embedded =
    let identifier = parentInfo.ParentId

    let dtoType, workbook =
        match parentInfo.Parent with
        | DataMapParent.Assay ->
            DTOType.ISA_Assay, ArcAssay.toFsWorkbook (arc.GetAssay identifier, datamapSheet = embedded)
        | DataMapParent.Study ->
            DTOType.ISA_Study, ArcStudy.toFsWorkbook (arc.GetStudy identifier, datamapSheet = embedded)
        | DataMapParent.Run ->
            let run = arc.GetRun identifier
            let workbook = ArcRun.toFsWorkbook run

            if embedded then
                workbook.AddWorksheet(DatamapTable.toFsWorksheet run.Datamap.Value)

            DTOType.ISA_Run, workbook
        | DataMapParent.Workflow ->
            let workflow = arc.GetWorkflow identifier
            let workbook = ArcWorkflow.toFsWorkbook workflow

            if embedded then
                workbook.AddWorksheet(DatamapTable.toFsWorksheet workflow.Datamap.Value)

            DTOType.ISA_Workflow, workbook

    Contract.createUpdate (parentPath parentInfo, dtoType, DTO.Spreadsheet workbook)

let private prepare arcPath arc contracts dirty diskPathBefore =
    prepareAsync {
        ArcPath = arcPath
        Snapshot = arc
        RawContracts = contracts
        DirtyDatamapKeys = dirty
        IsFullWrite = false
        DiskPathBeforeExecution = diskPathBefore
    }

let private hasEmbeddedDataMap (contract: Contract) =
    contract.DTO.Value.AsSpreadsheet().GetWorksheets()
    |> Seq.exists (fun worksheet ->
        try
            (DatamapTable.tryFromFsWorksheet worksheet).IsSome
        with _ ->
            false
    )

Vitest.describe (
    "ContractPolicy",
    fun () ->
        Vitest.test (
            "orders structural contracts, datamap writes and other contracts stably",
            fun () ->
                let dm = dataMap "dataset/ordering.csv"

                let contracts = [|
                    Contract.createUpdate ("notes/first.txt", DTOType.PlainText, DTO.Text "first")
                    dm.ToUpdateContractForAssay "A"
                    Contract.createDelete "assays/Removed"
                    Contract.createRead ("assays/A/isa.datamap.xlsx", DTOType.ISA_Datamap)
                    Contract.createRename ("runs/Old", "runs/New")
                    dm.ToCreateContractForStudy "S"
                    Contract.createCreate ("notes/last.txt", DTOType.PlainText, DTO.Text "last")
                    Contract.createDelete "workflows/Removed"
                |]

                let expected =
                    [| 2; 4; 7; 1; 5; 0; 3; 6 |] |> Array.map (fun index -> contracts.[index])

                Vitest.expect(orderContracts contracts).toEqual expected
        )

        for operation in [ Operation.CREATE; Operation.UPDATE ] do
            Vitest.test (
                $"regenerates assay and study {operation} payloads without embedded datamaps",
                fun () ->
                    let arc, assayInfo, _ = fixture DataMapParent.Assay "AssayPayload"

                    let study =
                        ArcStudy("StudyPayload", title = "Original study", datamap = dataMap "dataset/study.csv")

                    arc.AddStudy study
                    arc.GetAssay(assayInfo.ParentId).Title <- Some "Original assay"
                    let studyInfo = DatamapParentInfo.create study.Identifier DataMapParent.Study

                    let originals = [|
                        parentUpdate arc assayInfo true
                        parentUpdate arc studyInfo true
                    |]

                    let originals =
                        originals |> Array.map (fun contract -> { contract with Operation = operation })

                    Vitest.expect(originals |> Array.forall hasEmbeddedDataMap).toBe true
                    arc.GetAssay(assayInfo.ParentId).Title <- Some "Current assay"
                    study.Title <- Some "Current study"

                    let regenerated = regenerateParentPayloads arc originals |> expectPrepared

                    for index in 0 .. originals.Length - 1 do
                        Vitest.expect(regenerated.[index].Path).toBe originals.[index].Path
                        Vitest.expect(regenerated.[index].Operation).toBe operation
                        Vitest.expect(regenerated.[index].DTOType).toEqual originals.[index].DTOType

                    let assay = ArcAssay.fromFsWorkbook (regenerated.[0].DTO.Value.AsSpreadsheet())
                    let study, _ = ArcStudy.fromFsWorkbook (regenerated.[1].DTO.Value.AsSpreadsheet())
                    Vitest.expect(assay.Datamap).toEqual None
                    Vitest.expect(study.Datamap).toEqual None
                    Vitest.expect(assay.Title).toEqual (Some "Current assay")
                    Vitest.expect(study.Title).toEqual (Some "Current study")
            )

        for parent in [ DataMapParent.Assay; DataMapParent.Study ] do
            Vitest.test (
                $"rejects regeneration when the {parent} is missing from the snapshot",
                fun () ->
                    let source, parentInfo, _ = fixture parent "Missing"
                    let contract = parentUpdate source parentInfo true

                    match regenerateParentPayloads (ARC("Empty")) [| contract |] with
                    | Ok _ -> failwith "Expected the missing parent to prevent payload regeneration."
                    | Error error ->
                        Vitest.expect(error).toBe $"Could not find the assay/study for contract '{contract.Path}'."
            )

        Vitest.test (
            "remaps only complete folder path segments and normalizes resulting keys",
            fun () ->
                let keys =
                    [
                        "assays/A/isa.datamap.xlsx"
                        "assays/AB/isa.datamap.xlsx"
                        "studies/S/isa.datamap.xlsx"
                    ]
                    |> List.map key
                    |> Set.ofList

                let remapped = remapKeys "assays/A" "assays/B" keys

                let expected =
                    [
                        "assays/B/isa.datamap.xlsx"
                        "assays/AB/isa.datamap.xlsx"
                        "studies/S/isa.datamap.xlsx"
                    ]
                    |> List.map key
                    |> Set.ofList

                Vitest.expect(remapped |> Set.toArray).toEqual (expected |> Set.toArray)
        )

        for parent in
            [
                DataMapParent.Assay
                DataMapParent.Study
                DataMapParent.Run
                DataMapParent.Workflow
            ] do
            Vitest.test (
                $"Rule A exports an absent separate {parent} datamap before writing its parent",
                fun () ->
                    withTempDirectory (fun arcPath -> promise {
                        let arc, parentInfo, dm = fixture parent "Export"
                        let embedsDataMap = parent = DataMapParent.Assay || parent = DataMapParent.Study
                        let! result = prepare arcPath arc [| parentUpdate arc parentInfo embedsDataMap |] Set.empty id
                        let contracts = expectPrepared result

                        Vitest.expect(contractKeys contracts).toEqual [|
                            key (dataMapPath parentInfo)
                            key (parentPath parentInfo)
                        |]

                        Vitest.expect(contracts.[0].Operation).toBe Operation.UPDATE
                        Vitest.expect(contracts.[0].DTOType).toEqual (Some DTOType.ISA_Datamap)
                        let exported = Datamap.fromFsWorkbook (contracts.[0].DTO.Value.AsSpreadsheet())
                        Vitest.expect(exported.DataContexts.[0].Name).toEqual dm.DataContexts.[0].Name
                        Vitest.expect(hasEmbeddedDataMap contracts.[1]).toBe false
                    })
            )

            Vitest.test (
                $"Rule A leaves an existing separate {parent} datamap untouched",
                fun () ->
                    withTempDirectory (fun arcPath -> promise {
                        let arc, parentInfo, dm = fixture parent "Existing"
                        let separate = DataMapContract.update parentInfo dm
                        let! written = fulfillContractsSwateAsync arcPath [| separate |]
                        expectExecuted written |> ignore
                        let absolutePath = join [| arcPath; separate.Path |]
                        let! before = readFileBase64Async absolutePath
                        let! result = prepare arcPath arc [| parentUpdate arc parentInfo true |] Set.empty id
                        Vitest.expect(contractKeys (expectPrepared result)).toEqual [| key (parentPath parentInfo) |]
                        let! after = readFileBase64Async absolutePath
                        Vitest.expect(after).toBe before
                    })
            )

            for embedded in [ false; true ] do
                Vitest.test (
                    $"Rule B rewrites the {parent} parent only when its disk workbook embeds a datamap ({embedded})",
                    fun () ->
                        withTempDirectory (fun arcPath -> promise {
                            let arc, parentInfo, dm = fixture parent "Legacy"

                            let! written =
                                fulfillContractsSwateAsync arcPath [| parentUpdate arc parentInfo embedded |]

                            expectExecuted written |> ignore
                            let separate = DataMapContract.update parentInfo dm
                            let! result = prepare arcPath arc [| separate |] (Set.singleton (key separate.Path)) id
                            let contracts = expectPrepared result

                            let expected =
                                if embedded then
                                    [| key separate.Path; key (parentPath parentInfo) |]
                                else
                                    [| key separate.Path |]

                            Vitest.expect(contractKeys contracts).toEqual expected

                            if embedded then
                                Vitest.expect(contracts.[1].Operation).toBe Operation.UPDATE
                                Vitest.expect(hasEmbeddedDataMap contracts.[1]).toBe false
                        })
                )

            Vitest.test (
                $"uses the mapped pre-execution datamap path for {parent} Rule A",
                fun () ->
                    withTempDirectory (fun arcPath -> promise {
                        let arc, parentInfo, dm = fixture parent "AfterRename"
                        let oldInfo = DatamapParentInfo.create "BeforeRename" parent
                        let! written = fulfillContractsSwateAsync arcPath [| DataMapContract.update oldInfo dm |]
                        expectExecuted written |> ignore
                        let! targetExists = TestHelpers.pathExistsAsync (join [| arcPath; dataMapPath parentInfo |])
                        Vitest.expect(targetExists).toBe false

                        let diskPathBefore path =
                            PathHelpers.tryRemapPathPrefix
                                (PathHelpers.tryGetParentPath (parentPath parentInfo) |> Option.get)
                                (PathHelpers.tryGetParentPath (parentPath oldInfo) |> Option.get)
                                path
                            |> Option.defaultValue path

                        let! result =
                            prepare arcPath arc [| parentUpdate arc parentInfo true |] Set.empty diskPathBefore

                        Vitest.expect(contractKeys (expectPrepared result)).toEqual [| key (parentPath parentInfo) |]
                    })
            )

            Vitest.test (
                $"uses the mapped pre-execution parent path for {parent} Rule B",
                fun () ->
                    withTempDirectory (fun arcPath -> promise {
                        let oldArc, oldInfo, _ = fixture parent "BeforeRename"
                        let! written = fulfillContractsSwateAsync arcPath [| parentUpdate oldArc oldInfo true |]
                        expectExecuted written |> ignore
                        let arc, parentInfo, dm = fixture parent "AfterRename"
                        let! targetExists = TestHelpers.pathExistsAsync (join [| arcPath; parentPath parentInfo |])
                        Vitest.expect(targetExists).toBe false

                        let diskPathBefore path =
                            PathHelpers.tryRemapPathPrefix
                                (PathHelpers.tryGetParentPath (parentPath parentInfo) |> Option.get)
                                (PathHelpers.tryGetParentPath (parentPath oldInfo) |> Option.get)
                                path
                            |> Option.defaultValue path

                        let separate = DataMapContract.update parentInfo dm

                        let! result =
                            prepare arcPath arc [| separate |] (Set.singleton (key separate.Path)) diskPathBefore

                        let contracts = expectPrepared result

                        Vitest.expect(contractKeys contracts).toEqual [|
                            key separate.Path
                            key (parentPath parentInfo)
                        |]

                        Vitest.expect(hasEmbeddedDataMap contracts.[1]).toBe false
                    })
            )

        Vitest.test (
            "preparation preserves saved hashes and emits nothing for untouched entities",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let arc, parentInfo, dm = fixture DataMapParent.Assay "Touched"
                    let untouched = ArcAssay("Untouched", datamap = dataMap "dataset/untouched.csv")
                    arc.AddAssay untouched
                    arc.StaticHash <- 101
                    arc.GetAssay(parentInfo.ParentId).StaticHash <- 102
                    dm.StaticHash <- 103
                    untouched.StaticHash <- 104
                    untouched.Datamap.Value.StaticHash <- 105

                    let hashes () = [|
                        arc.StaticHash
                        arc.GetAssay(parentInfo.ParentId).StaticHash
                        dm.StaticHash
                        untouched.StaticHash
                        untouched.Datamap.Value.StaticHash
                    |]

                    let before = hashes ()
                    let! result = prepare arcPath arc [| parentUpdate arc parentInfo true |] Set.empty id

                    Vitest.expect(contractKeys (expectPrepared result)).toEqual [|
                        key (dataMapPath parentInfo)
                        key (parentPath parentInfo)
                    |]

                    Vitest.expect(hashes ()).toEqual before
                    let! noWrites = prepare arcPath arc [||] Set.empty id
                    Vitest.expect(expectPrepared noWrites).toHaveLength 0
                    Vitest.expect(hashes ()).toEqual before
                })
        )
)
