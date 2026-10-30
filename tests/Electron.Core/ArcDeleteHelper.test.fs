module ElectronCore.ArcDeleteHelperTests

open Main.ARCtrlExtensions
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultLargeObjects
open Main.ArcVaultFileTree
open Main.ArcVaultFileWatcher
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.IPC.Delete
open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open ARCtrl.Spreadsheet
open Fable.Core
open Swate.Components.Shared
open Vitest

let private expectSome (value: 'T option) (message: string) : 'T =
    match value with
    | Some value -> value
    | None -> failwith message

let private pathExistsAsync = TestHelpers.pathExistsAsync
let private loadArcAsync = TestHelpers.loadArcAsync
let private testWindow = TestHelpers.testWindow

let private withTempArc =
    TestHelpers.withTempArcWith "swate-delete-arc-file-" "DeleteArcFileArc"

let private loadSwateArcAsync arcPath = promise {
    let! result = ARC.LoadAsyncSwate arcPath
    return TestHelpers.expectLoadedArc result
}

let private dataMap filePath =
    let dataMap = Datamap.init ()
    dataMap.DataContexts.Add(DataContext(name = filePath))
    dataMap

let private executeOrFail arcPath contracts = promise {
    match! ContractExecutor.fulfillContractsSwateAsync arcPath contracts with
    | Ok _ -> return ()
    | Error errors -> return failwith (PathHelpers.formatContractErrors errors)
}

/// Writes legacy parent data explicitly so the fixture does not depend on current save policy.
let private writeSurvivingDataMapFixtures arcPath = promise {
    let legacy =
        ArcAssay("LegacySurvivor", datamap = dataMap "dataset/legacy-survivor.csv")

    let legacyPath = Identifier.Assay.fileNameFromIdentifier legacy.Identifier
    let populatedInfo = DatamapParentInfo.create "PopulatedSurvivor" DataMapParent.Assay
    let emptyInfo = DatamapParentInfo.create "EmptySurvivor" DataMapParent.Assay

    do!
        executeOrFail arcPath [|
            Contract.createUpdate (legacyPath, DTOType.ISA_Assay, DTO.Spreadsheet(ArcAssay.toFsWorkbook legacy))
            DataMapContract.update populatedInfo (dataMap "dataset/populated-survivor.csv")
            DataMapContract.update emptyInfo (Datamap.init ())
        |]
}

let private survivorFileBytes arcPath = promise {
    let mutable files = []

    for identifier in [ "LegacySurvivor"; "PopulatedSurvivor"; "EmptySurvivor" ] do
        let parentPath = Identifier.Assay.fileNameFromIdentifier identifier
        let! parentBytes = readFileBase64Async (join [| arcPath; parentPath |])
        files <- (parentPath, parentBytes) :: files

        if identifier <> "LegacySurvivor" then
            let dataMapPath = Identifier.Assay.datamapFileNameFromIdentifier identifier
            let! dataMapBytes = readFileBase64Async (join [| arcPath; dataMapPath |])
            files <- (dataMapPath, dataMapBytes) :: files

    return files
}

let private assertSurvivorFilesUnchanged arcPath (before: (string * string) list) = promise {
    for relativePath, originalBytes in before do
        let! bytes = readFileBase64Async (join [| arcPath; relativePath |])
        Vitest.expect(bytes).toBe originalBytes

    let! legacySeparateExists =
        pathExistsAsync (
            join [|
                arcPath
                Identifier.Assay.datamapFileNameFromIdentifier "LegacySurvivor"
            |]
        )

    Vitest.expect(legacySeparateExists).toBe false
}

/// Simulates a metadata write failing only after the structural deletion completed.
let private executeDeleteThenFail arcPath (contracts: Contract[]) = promise {
    Vitest.expect(contracts.Length > 0).toBe true
    Vitest.expect(contracts.[0].Operation).toEqual Operation.DELETE

    match! ContractExecutor.fulfillContractsSwateAsync arcPath contracts.[0..0] with
    | Error errors -> return failwith (PathHelpers.formatContractErrors errors)
    | Ok _ -> return Error [| "Injected metadata update failure" |]
}

let private deleteEntityOrFail arcPath relativePath arc = promise {
    match! ArcDeleteHelper.deleteArcEntityAsync arcPath relativePath arc with
    | Error error -> return failwith error.Message
    | Ok(Deleted deletedArc) -> return deletedArc
    | Ok(PartiallyDeleted(_, error)) -> return failwith $"Expected a completed delete: {error.Message}"
}

let private assertDeletedOnDisk arcPath relativeParts containsEntity = promise {
    let folderPath = join (Array.append [| arcPath |] relativeParts)
    let! folderExists = pathExistsAsync folderPath
    Vitest.expect(folderExists).toBe (false)

    let! reloadedArc = loadArcAsync arcPath
    Vitest.expect(containsEntity reloadedArc).toBe (false)
}

Vitest.describe (
    "ARC delete helper",
    fun () ->
        Vitest.test (
            "deletes an assay and removes its folder",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DeleteAssay")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let! deletedArc = deleteEntityOrFail arcPath "assays/DeleteAssay" arc

                        Vitest.expect(deletedArc.ContainsAssay("DeleteAssay")).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "assays"; "DeleteAssay" |]
                                (fun arc -> arc.ContainsAssay("DeleteAssay"))
                    })
        )

        Vitest.test (
            "deletes an assay whose folder holds payload files",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("PayloadAssay")))
                    (fun arcPath -> promise {
                        let assayFolder = join [| arcPath; "assays"; "PayloadAssay" |]

                        let! _ =
                            mkdirAsync (join [| assayFolder; "dataset"; "nested" |]) (MkdirOptions(recursive = true))

                        do!
                            writeFileAsync
                                (join [| assayFolder; "dataset"; "raw.bin" |])
                                "raw payload"
                                TextEncoding.Utf8

                        do!
                            writeFileAsync
                                (join [| assayFolder; "dataset"; "nested"; "deep.txt" |])
                                "nested payload"
                                TextEncoding.Utf8

                        let! loadResult = ARC.LoadAsyncSwate arcPath
                        let arc = TestHelpers.expectLoadedArc loadResult
                        let! deletedArc = deleteEntityOrFail arcPath "assays/PayloadAssay" arc

                        Vitest.expect(deletedArc.ContainsAssay("PayloadAssay")).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "assays"; "PayloadAssay" |]
                                (fun arc -> arc.ContainsAssay("PayloadAssay"))
                    })
        )

        Vitest.test (
            "deletes an assay DataMap through a contract",
            fun () ->
                withTempArc
                    (fun arc ->
                        let assay = ArcAssay("DataMapAssay")
                        assay.Datamap <- Some(Datamap.init ())
                        arc.AddAssay assay
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetAssay("DataMapAssay").Datamap.IsNone).toBe (true)

                            let! dataMapExists =
                                pathExistsAsync (join [| arcPath; "assays"; "DataMapAssay"; "isa.datamap.xlsx" |])

                            Vitest.expect(dataMapExists).toBe (false)

                            let! persistedArc = loadArcAsync arcPath
                            Vitest.expect(persistedArc.GetAssay("DataMapAssay").Datamap.IsNone).toBe (true)
                    })
        )

        Vitest.test (
            "loads only the requested persisted DataMap parent for every parent kind",
            fun () ->
                withTempArc
                    (fun arc ->
                        let assay = ArcAssay("TargetAssay")
                        assay.Datamap <- Some(Datamap.init ())
                        arc.AddAssay assay

                        let study = ArcStudy("TargetStudy")
                        study.Datamap <- Some(Datamap.init ())
                        arc.AddStudy study

                        let run = ArcRun("TargetRun")
                        run.Datamap <- Some(Datamap.init ())
                        arc.AddRun run

                        let workflow = ArcWorkflow("TargetWorkflow")
                        workflow.Datamap <- Some(Datamap.init ())
                        arc.AddWorkflow workflow

                        arc.AddStudy(ArcStudy("UnreadableUnrelatedStudy"))
                    )
                    (fun arcPath -> promise {
                        let unreadableStudyPath =
                            join [|
                                arcPath
                                "studies"
                                "UnreadableUnrelatedStudy"
                                "isa.study.xlsx"
                            |]

                        do! writeFileAsync unreadableStudyPath "not an XLSX workbook" TextEncoding.Utf8

                        let parents = [|
                            DatamapParentInfo.create "TargetAssay" DataMapParent.Assay
                            DatamapParentInfo.create "TargetStudy" DataMapParent.Study
                            DatamapParentInfo.create "TargetRun" DataMapParent.Run
                            DatamapParentInfo.create "TargetWorkflow" DataMapParent.Workflow
                        |]

                        for parentInfo in parents do
                            match! loadPersistedDataMapParentAsync arcPath parentInfo with
                            | Error errors -> failwith (PathHelpers.formatContractErrors errors)
                            | Ok None -> failwith "Expected the requested persisted parent."
                            | Ok(Some persistedArc) ->
                                let containsRequestedParent =
                                    match parentInfo.Parent with
                                    | DataMapParent.Assay -> persistedArc.ContainsAssay(parentInfo.ParentId)
                                    | DataMapParent.Study -> persistedArc.ContainsStudy(parentInfo.ParentId)
                                    | DataMapParent.Run -> persistedArc.ContainsRun(parentInfo.ParentId)
                                    | DataMapParent.Workflow -> persistedArc.ContainsWorkflow(parentInfo.ParentId)

                                Vitest.expect(containsRequestedParent).toBe (true)
                                Vitest.expect(persistedArc.TryGetDataMap(parentInfo).IsNone).toBe (true)

                                let loadedParentCount =
                                    persistedArc.Assays.Count
                                    + persistedArc.Studies.Count
                                    + persistedArc.Runs.Count
                                    + persistedArc.Workflows.Count

                                Vitest.expect(loadedParentCount).toBe (1)
                    })
        )

        Vitest.test (
            "explains which ARC entity has no DataMap to delete",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddStudy(ArcStudy("StudyWithoutDataMap")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "StudyWithoutDataMap" DataMapParent.Study

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Ok() -> failwith "Expected deleting a missing DataMap to fail."
                        | Error error ->
                            Vitest
                                .expect(error.Message)
                                .toBe (
                                    "Parent 'studies/StudyWithoutDataMap' does not have a DataMap to delete. Refresh the File Explorer and try again."
                                )
                    })
        )

        Vitest.test (
            "deletes a study DataMap through a contract",
            fun () ->
                withTempArc
                    (fun arc ->
                        let study = ArcStudy("DataMapStudy")
                        study.Datamap <- Some(Datamap.init ())
                        arc.AddStudy study
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapStudy" DataMapParent.Study

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetStudy("DataMapStudy").Datamap.IsNone).toBe (true)

                            let! dataMapExists =
                                pathExistsAsync (join [| arcPath; "studies"; "DataMapStudy"; "isa.datamap.xlsx" |])

                            Vitest.expect(dataMapExists).toBe (false)
                    })
        )

        Vitest.test (
            "deletes a workflow DataMap through a contract",
            fun () ->
                withTempArc
                    (fun arc ->
                        let workflow = ArcWorkflow("DataMapWorkflow")
                        workflow.Datamap <- Some(Datamap.init ())
                        arc.AddWorkflow workflow
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapWorkflow" DataMapParent.Workflow

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetWorkflow("DataMapWorkflow").Datamap.IsNone).toBe (true)

                            let! dataMapExists =
                                pathExistsAsync (
                                    join [|
                                        arcPath
                                        "workflows"
                                        "DataMapWorkflow"
                                        "isa.datamap.xlsx"
                                    |]
                                )

                            Vitest.expect(dataMapExists).toBe (false)
                    })
        )

        Vitest.test (
            "deletes a run DataMap through a contract",
            fun () ->
                withTempArc
                    (fun arc ->
                        let run = ArcRun("DataMapRun")
                        run.Datamap <- Some(Datamap.init ())
                        arc.AddRun run
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapRun" DataMapParent.Run

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetRun("DataMapRun").Datamap.IsNone).toBe (true)

                            let! dataMapExists =
                                pathExistsAsync (join [| arcPath; "runs"; "DataMapRun"; "isa.datamap.xlsx" |])

                            Vitest.expect(dataMapExists).toBe (false)
                    })
        )

        Vitest.test (
            "deletes a clean assay without marking the ARC dirty",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DeleteAssay")))
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        baselineArcStaticHashes loadedArc
                        Vitest.expect(loadedArc.hasInMemoryChanges ()).toBe (false)

                        let! deletedArc = deleteEntityOrFail arcPath "assays/DeleteAssay" loadedArc

                        Vitest.expect(deletedArc.ContainsAssay("DeleteAssay")).toBe (false)
                        Vitest.expect(deletedArc.hasInMemoryChanges ()).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "assays"; "DeleteAssay" |]
                                (fun arc -> arc.ContainsAssay("DeleteAssay"))
                    })
        )

        Vitest.test (
            "deletes an entity when given the canonical workbook path",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DeleteAssay")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let! deletedArc = deleteEntityOrFail arcPath "assays/DeleteAssay/isa.assay.xlsx" arc

                        Vitest.expect(deletedArc.ContainsAssay("DeleteAssay")).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "assays"; "DeleteAssay" |]
                                (fun arc -> arc.ContainsAssay("DeleteAssay"))
                    })
        )

        Vitest.test (
            "deletes a study and removes its folder",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddStudy(ArcStudy("DeleteStudy")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let! deletedArc = deleteEntityOrFail arcPath "studies/DeleteStudy" arc

                        Vitest.expect(deletedArc.ContainsStudy("DeleteStudy")).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "studies"; "DeleteStudy" |]
                                (fun arc -> arc.ContainsStudy("DeleteStudy"))
                    })
        )

        Vitest.test (
            "deletes a run and removes its folder",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddRun(ArcRun("DeleteRun")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let! deletedArc = deleteEntityOrFail arcPath "runs/DeleteRun" arc

                        Vitest.expect(deletedArc.ContainsRun("DeleteRun")).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "runs"; "DeleteRun" |]
                                (fun arc -> arc.ContainsRun("DeleteRun"))
                    })
        )

        Vitest.test (
            "deletes a workflow and removes its folder",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddWorkflow(ArcWorkflow("DeleteWorkflow")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let! deletedArc = deleteEntityOrFail arcPath "workflows/DeleteWorkflow" arc

                        Vitest.expect(deletedArc.ContainsWorkflow("DeleteWorkflow")).toBe (false)

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "workflows"; "DeleteWorkflow" |]
                                (fun arc -> arc.ContainsWorkflow("DeleteWorkflow"))
                    })
        )

        Vitest.test (
            "rejects missing entity identifiers without mutating the in-memory ARC",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("KeepAssay")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        match! ArcDeleteHelper.deleteArcEntityAsync arcPath "assays/MissingAssay" arc with
                        | Ok _ -> failwith "Expected missing assay delete to fail."
                        | Error error ->
                            Vitest.expect(error.Message).toContain ("does not contain assay")
                            Vitest.expect(arc.ContainsAssay("KeepAssay")).toBe (true)
                    })
        )

        Vitest.test (
            "deletes through the scoped ARC path without persisting unrelated dirty in-memory edits",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddAssay(ArcAssay("DeleteAssay"))
                        arc.AddAssay(ArcAssay("KeepAssay", title = "Old title"))
                    )
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        loadedArc.GetAssay("KeepAssay").Title <- Some "Unsaved local title"

                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc
                        vault.RefreshHasUnsavedArcChangesFlag()

                        match! ArcDeleteHelper.deleteArcEntityAsync arcPath "assays/DeleteAssay" loadedArc with
                        | Error deleteError -> failwith deleteError.Message
                        | Ok(PartiallyDeleted(_, error)) -> failwith $"Expected a completed delete: {error.Message}"
                        | Ok(Deleted deletedArc) ->
                            vault.SetArc deletedArc
                            vault.RefreshHasUnsavedArcChangesFlag()

                            let! reloadedAfterDelete = loadArcAsync arcPath
                            Vitest.expect(reloadedAfterDelete.ContainsAssay("DeleteAssay")).toBe (false)
                            Vitest.expect(reloadedAfterDelete.GetAssay("KeepAssay").Title).toEqual (Some "Old title")

                            let inMemoryArc = vault.arc |> expectSome <| "Expected vault ARC."
                            Vitest.expect(inMemoryArc.ContainsAssay("DeleteAssay")).toBe (false)
                            Vitest.expect(inMemoryArc.GetAssay("KeepAssay").Title).toEqual (Some "Unsaved local title")
                            Vitest.expect(vault.hasUnsavedArcChanges).toBe (true)
                    })
        )
)

Vitest.describe (
    "ARC entity deletion with surviving datamaps",
    fun () ->
        let targets = [
            "assay",
            "assays/DeleteAssay",
            (fun (arc: ARC) -> arc.AddAssay(ArcAssay("DeleteAssay"))),
            (fun (arc: ARC) -> arc.ContainsAssay "DeleteAssay")
            "run",
            "runs/DeleteRun",
            (fun (arc: ARC) -> arc.AddRun(ArcRun("DeleteRun"))),
            (fun (arc: ARC) -> arc.ContainsRun "DeleteRun")
            "workflow",
            "workflows/DeleteWorkflow",
            (fun (arc: ARC) -> arc.AddWorkflow(ArcWorkflow("DeleteWorkflow"))),
            (fun (arc: ARC) -> arc.ContainsWorkflow "DeleteWorkflow")
            "study",
            "studies/DeleteStudy",
            (fun (arc: ARC) -> arc.AddStudy(ArcStudy("DeleteStudy"))),
            (fun (arc: ARC) -> arc.ContainsStudy "DeleteStudy")
        ]

        for kind, relativePath, addTarget, containsTarget in targets do
            Vitest.test (
                $"deletes a {kind} without touching surviving embedded-only, populated or empty datamaps",
                fun () ->
                    withTempArc
                        (fun arc ->
                            addTarget arc

                            for identifier in [ "LegacySurvivor"; "PopulatedSurvivor"; "EmptySurvivor" ] do
                                arc.AddAssay(ArcAssay(identifier))
                        )
                        (fun arcPath -> promise {
                            do! writeSurvivingDataMapFixtures arcPath
                            let! before = survivorFileBytes arcPath
                            let! arc = loadSwateArcAsync arcPath
                            let! deletedArc = deleteEntityOrFail arcPath relativePath arc
                            Vitest.expect(containsTarget deletedArc).toBe false
                            let! folderExists = pathExistsAsync (join [| arcPath; relativePath |])
                            Vitest.expect(folderExists).toBe false
                            do! assertSurvivorFilesUnchanged arcPath before
                            let! reloaded = loadSwateArcAsync arcPath
                            Vitest.expect(containsTarget reloaded).toBe false

                            Vitest
                                .expect(reloaded.GetAssay("LegacySurvivor").Datamap.Value.GetDataContext(0).Name)
                                .toEqual (Some "dataset/legacy-survivor.csv")

                            Vitest
                                .expect(reloaded.GetAssay("PopulatedSurvivor").Datamap.Value.GetDataContext(0).Name)
                                .toEqual (Some "dataset/populated-survivor.csv")

                            Vitest.expect(reloaded.GetAssay("EmptySurvivor").Datamap.Value.DataContexts.Count).toBe 0
                        })
            )
)

Vitest.describe (
    "partial ARC entity deletion",
    fun () ->
        let seed (arc: ARC) =
            arc.AddAssay(ArcAssay("DeleteAssay"))
            arc.AddAssay(ArcAssay("KeepAssay", title = "Persisted title"))

        Vitest.test (
            "returns the merged ARC and preserves unrelated unsaved edits after the folder was deleted",
            fun () ->
                withTempArc
                    seed
                    (fun arcPath -> promise {
                        let! arc = loadSwateArcAsync arcPath
                        baselineArcStaticHashes arc
                        arc.GetAssay("KeepAssay").Title <- Some "Unsaved local title"

                        match!
                            ArcDeleteHelper.deleteArcEntityWithAsync
                                ARC.LoadAsyncSwate
                                executeDeleteThenFail
                                arcPath
                                "assays/DeleteAssay"
                                arc
                        with
                        | Error error -> return failwith error.Message
                        | Ok(Deleted _) -> return failwith "Expected a partial delete outcome."
                        | Ok(PartiallyDeleted(deletedArc, error)) ->
                            Vitest.expect(deletedArc.ContainsAssay "DeleteAssay").toBe false
                            Vitest.expect(deletedArc.GetAssay("KeepAssay").Title).toEqual (Some "Unsaved local title")
                            Vitest.expect(error.Message).toContain "updating the ARC metadata failed"
                            Vitest.expect(error.Message).toContain "Injected metadata update failure"
                            let! folderExists = pathExistsAsync (join [| arcPath; "assays/DeleteAssay" |])
                            Vitest.expect(folderExists).toBe false
                    })
        )

        Vitest.test (
            "returns an error while the folder still exists when DELETE fails immediately",
            fun () ->
                withTempArc
                    seed
                    (fun arcPath -> promise {
                        let! arc = loadSwateArcAsync arcPath
                        let execute _ _ = promise { return Error [| "Injected DELETE failure" |] }

                        match!
                            ArcDeleteHelper.deleteArcEntityWithAsync
                                ARC.LoadAsyncSwate
                                execute
                                arcPath
                                "assays/DeleteAssay"
                                arc
                        with
                        | Ok _ -> return failwith "Expected the DELETE failure to return Error."
                        | Error error ->
                            Vitest.expect(error.Message).toContain "Injected DELETE failure"
                            Vitest.expect(arc.ContainsAssay "DeleteAssay").toBe true
                            let! folderExists = pathExistsAsync (join [| arcPath; "assays/DeleteAssay" |])
                            Vitest.expect(folderExists).toBe true
                    })
        )

        Vitest.test (
            "returns a dirty fallback preserving unrelated edits and both errors when metadata and reload fail",
            fun () ->
                withTempArc
                    seed
                    (fun arcPath -> promise {
                        let! arc = loadSwateArcAsync arcPath
                        baselineArcStaticHashes arc
                        arc.GetAssay("KeepAssay").Title <- Some "Unsaved local title"
                        let mutable loadCalls = 0

                        let load path = promise {
                            loadCalls <- loadCalls + 1

                            if loadCalls = 1 then
                                return! ARC.LoadAsyncSwate path
                            else
                                return Error [| "Injected reload failure" |]
                        }

                        match!
                            ArcDeleteHelper.deleteArcEntityWithAsync
                                load
                                executeDeleteThenFail
                                arcPath
                                "assays/DeleteAssay"
                                arc
                        with
                        | Error error -> return failwith error.Message
                        | Ok(Deleted _) -> return failwith "Expected a partial delete outcome."
                        | Ok(PartiallyDeleted(deletedArc, error)) ->
                            Vitest.expect(loadCalls).toBe 2
                            Vitest.expect(deletedArc.ContainsAssay "DeleteAssay").toBe false
                            Vitest.expect(deletedArc.hasInMemoryChanges ()).toBe true
                            Vitest.expect(deletedArc.GetAssay("KeepAssay").Title).toEqual (Some "Unsaved local title")
                            Vitest.expect(error.Message).toContain "Injected metadata update failure"
                            Vitest.expect(error.Message).toContain "Injected reload failure"
                            let! folderExists = pathExistsAsync (join [| arcPath; "assays/DeleteAssay" |])
                            Vitest.expect(folderExists).toBe false
                    })
        )
)
