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

let private deleteEntityOrFail arcPath relativePath arc = promise {
    match! ArcDeleteHelper.deleteArcEntityAsync arcPath relativePath arc with
    | Error error -> return failwith error.Message
    | Ok deletedArc -> return deletedArc
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
                        assay.DataMap <- Some(DataMap.init ())
                        arc.AddAssay assay
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetAssay("DataMapAssay").DataMap.IsNone).toBe (true)

                            let! dataMapExists =
                                pathExistsAsync (join [| arcPath; "assays"; "DataMapAssay"; "isa.datamap.xlsx" |])

                            Vitest.expect(dataMapExists).toBe (false)

                            let! persistedArc = loadArcAsync arcPath
                            Vitest.expect(persistedArc.GetAssay("DataMapAssay").DataMap.IsNone).toBe (true)
                    })
        )

        Vitest.test (
            "loads only the requested persisted DataMap parent for every parent kind",
            fun () ->
                withTempArc
                    (fun arc ->
                        let assay = ArcAssay("TargetAssay")
                        assay.DataMap <- Some(DataMap.init ())
                        arc.AddAssay assay

                        let study = ArcStudy("TargetStudy")
                        study.DataMap <- Some(DataMap.init ())
                        arc.AddStudy study

                        let run = ArcRun("TargetRun")
                        run.DataMap <- Some(DataMap.init ())
                        arc.AddRun run

                        let workflow = ArcWorkflow("TargetWorkflow")
                        workflow.DataMap <- Some(DataMap.init ())
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
                        study.DataMap <- Some(DataMap.init ())
                        arc.AddStudy study
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapStudy" DataMapParent.Study

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetStudy("DataMapStudy").DataMap.IsNone).toBe (true)

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
                        workflow.DataMap <- Some(DataMap.init ())
                        arc.AddWorkflow workflow
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapWorkflow" DataMapParent.Workflow

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetWorkflow("DataMapWorkflow").DataMap.IsNone).toBe (true)

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
                        run.DataMap <- Some(DataMap.init ())
                        arc.AddRun run
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let parentInfo = DatamapParentInfo.create "DataMapRun" DataMapParent.Run

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() ->
                            Vitest.expect(arc.GetRun("DataMapRun").DataMap.IsNone).toBe (true)

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
                        | Ok deletedArc ->
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
