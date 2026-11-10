module ElectronCore.ArcDeleteHelperTests

open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron.Main
open ARCtrl.Contract
open Main.ARCtrlExtensions
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultSync
open Main.ArcVaultLargeObjects
open Main.ArcVaultFileTree
open Main.ArcVaultFileWatcher
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.IPC.Delete
open Main.IPC.Rename
open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open ARCtrl.Spreadsheet
open Fable.Core
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.RenamePathRules
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

/// Puts the loaded ARC into a vault whose known disk state matches it.
let private createSyncVault arcPath (arc: ARC) = promise {
    let vault = ArcVault(testWindow ())
    vault.path <- Some arcPath
    vault.SetArc arc
    vault.RefreshHasUnsavedArcChangesFlag()
    do! TestHelpers.seedKnownDisk vault
    return vault
}

let private deleteEntityOrFail arcPath relativePath arc = promise {
    let! vault = createSyncVault arcPath arc

    match! ArcDeleteHelper.deleteArcEntityInVault vault relativePath (fun () -> promise { return () }) with
    | Error error -> return failwith error.Message
    | Ok() -> return vault.arc.Value
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
            "loads only the requested persisted DataMap parent and its adjacent file for every parent kind",
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
                                let dataMap = persistedArc.TryGetDataMap(parentInfo)
                                Vitest.expect(dataMap.IsSome).toBe (true)
                                Vitest.expect(dataMap.Value.DataContexts.Count).toBe (0)

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

                        match! ArcDeleteHelper.deleteArcEntityAsync arcPath "assays/MissingAssay" with
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

                        let! vault = createSyncVault arcPath loadedArc

                        match!
                            ArcDeleteHelper.deleteArcEntityInVault
                                vault
                                "assays/DeleteAssay"
                                (fun () -> promise { return () })
                        with
                        | Error deleteError -> failwith deleteError.Message
                        | Ok() ->
                            let! reloadedAfterDelete = loadArcAsync arcPath
                            Vitest.expect(reloadedAfterDelete.ContainsAssay("DeleteAssay")).toBe (false)
                            Vitest.expect(reloadedAfterDelete.GetAssay("KeepAssay").Title).toEqual (Some "Old title")

                            let inMemoryArc = vault.arc |> expectSome <| "Expected vault ARC."
                            Vitest.expect(inMemoryArc.ContainsAssay("DeleteAssay")).toBe (false)
                            Vitest.expect(inMemoryArc.GetAssay("KeepAssay").Title).toEqual (Some "Unsaved local title")
                            Vitest.expect(vault.hasUnsavedArcChanges).toBe (true)
                    })
        )

        Vitest.test (
            "a delete brings cross reference changes into memory",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddStudy(ArcStudy("StudyA"))
                        arc.AddAssay(ArcAssay("DeleteAssay"))
                        arc.AddAssay(ArcAssay("KeepAssay"))
                        arc.RegisterAssay("StudyA", "DeleteAssay")
                        arc.RegisterAssay("StudyA", "KeepAssay")
                    )
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        let! vault = createSyncVault arcPath loadedArc

                        match!
                            ArcDeleteHelper.deleteArcEntityInVault
                                vault
                                "assays/DeleteAssay"
                                (fun () -> promise { return () })
                        with
                        | Error deleteError -> failwith deleteError.Message
                        | Ok() ->
                            let! diskArc = loadArcAsync arcPath

                            let diskIdentifiers =
                                diskArc.GetStudy("StudyA").RegisteredAssayIdentifiers |> Seq.toArray

                            let memoryIdentifiers =
                                vault.arc.Value.GetStudy("StudyA").RegisteredAssayIdentifiers |> Seq.toArray

                            Vitest.expect(diskIdentifiers).toEqual [| "KeepAssay" |]
                            Vitest.expect(memoryIdentifiers).toEqual diskIdentifiers
                            Vitest.expect(vault.hasUnsavedArcChanges).toBe false
                    })
        )
)

[<Import("utimes", "fs/promises")>]
let private utimesAsync (path: string, atimeSeconds: float, mtimeSeconds: float) : JS.Promise<unit> = jsNative

/// A window that records every message that main sends to it.
let private recordingWindow () =
    let sent = ResizeArray<string>()

    let send: obj =
        emitJsExpr
            (fun (args: obj array) -> sent.Add(emitJsExpr args "JSON.stringify($0)": string))
            "((...args) => $0(args))"

    let window =
        createObj [
            "id" ==> 0
            "title" ==> ""
            "isDestroyed" ==> (fun () -> false)
            "webContents"
            ==> createObj [ "send" ==> send; "isDestroyed" ==> (fun () -> false) ]
        ]
        |> unbox<BrowserWindow>

    window, sent

let private finishedMessages (sent: ResizeArray<string>) =
    sent |> Seq.filter (fun text -> text.Contains "arcSyncFinished") |> Seq.toArray

let private replacedNothing (sent: ResizeArray<string>) =
    finishedMessages sent
    |> Array.forall (fun text -> text.Contains "\"replacedUnsaved\":[]")

/// A temp ARC with a loaded vault that records what main sends, with known disk versions set.
let private withRecordingVault
    (seed: ARC -> unit)
    (body: ArcVault -> ResizeArray<string> -> string -> JS.Promise<unit>)
    =
    withTempArc
        seed
        (fun arcPath -> promise {
            let! arc = loadArcAsync arcPath
            let window, sent = recordingWindow ()
            let vault = ArcVault(window)
            vault.path <- Some arcPath
            vault.SetArc arc
            vault.RefreshHasUnsavedArcChangesFlag()
            do! TestHelpers.seedKnownDisk vault
            do! body vault sent arcPath
        })

let private expectDeleted (result: Result<unit, exn>) =
    match result with
    | Ok() -> ()
    | Error error -> failwith error.Message

let private deleteOrFail (vault: ArcVault) arcPath relativePath = promise {
    let! result = ArcDeleteHelper.deleteArcEntityInVault vault relativePath (fun () -> promise { return () })
    expectDeleted result
}

let private nowMs () : float = emitJsExpr () "Date.now()"

let private seedStudyWithAssay (arc: ARC) =
    arc.AddStudy(ArcStudy("StudyS", title = "Old title"))
    arc.AddAssay(ArcAssay("AssayA"))
    arc.RegisterAssay("StudyS", "AssayA")

Vitest.describe (
    "ARC delete keeps unsaved edits",
    fun () ->
        Vitest.test (
            "deleting an assay keeps the unsaved edits of the study that registers it",
            fun () ->
                withRecordingVault
                    seedStudyWithAssay
                    (fun vault sent arcPath -> promise {
                        vault.arc.Value.GetStudy("StudyS").Title <- Some "Unsaved title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        let study = vault.arc.Value.GetStudy("StudyS")
                        Vitest.expect(study.Title).toEqual (Some "Unsaved title")
                        Vitest.expect(study.RegisteredAssayIdentifiers.Count).toBe 0
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe true

                        let! diskArc = loadArcAsync arcPath
                        Vitest.expect(diskArc.GetStudy("StudyS").RegisteredAssayIdentifiers.Count).toBe 0
                        Vitest.expect(diskArc.GetStudy("StudyS").Title).toEqual (Some "Old title")
                        Vitest.expect(replacedNothing sent).toBe true
                    })
        )

        Vitest.test (
            "those edits survive a later touch of the study's file",
            fun () ->
                withRecordingVault
                    seedStudyWithAssay
                    (fun vault _ arcPath -> promise {
                        vault.arc.Value.GetStudy("StudyS").Title <- Some "Unsaved title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        let later = nowMs () / 1000.0 + 10.0
                        do! utimesAsync (join [| arcPath; "studies/StudyS/isa.study.xlsx" |], later, later)
                        do! vault.RequestSync true

                        Vitest.expect(vault.arc.Value.GetStudy("StudyS").Title).toEqual (Some "Unsaved title")
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe true
                    })
        )

        Vitest.test (
            "deleting a study keeps the unsaved edits of the investigation",
            fun () ->
                withRecordingVault
                    (fun arc ->
                        arc.Title <- Some "Old investigation title"
                        arc.AddStudy(ArcStudy("StudyS"))
                        arc.RegisterStudy "StudyS"
                    )
                    (fun vault _ arcPath -> promise {
                        Vitest.expect(vault.arc.Value.RegisteredStudyIdentifiers |> Seq.contains "StudyS").toBe true
                        vault.arc.Value.Title <- Some "Unsaved investigation title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        do! deleteOrFail vault arcPath "studies/StudyS"

                        Vitest.expect(vault.arc.Value.Title).toEqual (Some "Unsaved investigation title")
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe true
                        Vitest.expect(vault.arc.Value.RegisteredStudyIdentifiers |> Seq.contains "StudyS").toBe false

                        let! diskArc = loadArcAsync arcPath
                        Vitest.expect(diskArc.RegisteredStudyIdentifiers |> Seq.contains "StudyS").toBe false
                    })
        )

        Vitest.test (
            "deleting an assay with unsaved edits names nothing as replaced",
            fun () ->
                withRecordingVault
                    seedStudyWithAssay
                    (fun vault sent arcPath -> promise {
                        vault.arc.Value.GetAssay("AssayA").Title <- Some "Unsaved assay title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        Vitest.expect(replacedNothing sent).toBe true
                    })
        )

        Vitest.test (
            "deleting an assay leaves a study that registers it without unsaved edits",
            fun () ->
                withRecordingVault
                    seedStudyWithAssay
                    (fun vault _ arcPath -> promise {
                        do! deleteOrFail vault arcPath "assays/AssayA"

                        Vitest.expect(vault.hasUnsavedArcChanges).toBe false

                        let! diskArc = loadArcAsync arcPath

                        Vitest
                            .expect(vault.arc.Value.GetStudy("StudyS").RegisteredAssayIdentifiers |> Seq.toArray)
                            .toEqual (diskArc.GetStudy("StudyS").RegisteredAssayIdentifiers |> Seq.toArray)
                    })
        )

        Vitest.test (
            "a page edit based on the old content is refused after the delete",
            fun () ->
                withRecordingVault
                    seedStudyWithAssay
                    (fun vault _ arcPath -> promise {
                        let oldRevision = vault.ArcRevision

                        let oldDto =
                            match FileContentDTO.fromArcByPath "studies/StudyS/isa.study.xlsx" vault.arc.Value with
                            | Some dto -> dto
                            | None -> failwith "Expected a study DTO."

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        let outcome =
                            match
                                vault.TryUpdateArcByFileContentDTO {|
                                    file = oldDto
                                    baseRevision = oldRevision
                                |}
                            with
                            | Ok outcome -> outcome
                            | Error error -> failwith error.Message

                        Vitest.expect(outcome).toBe SetArcFileOutcome.Refused
                        do! vault.RequestSync true
                        Vitest.expect(vault.arc.Value.GetStudy("StudyS").RegisteredAssayIdentifiers.Count).toBe 0
                    })
        )

        Vitest.test (
            "an open page of the study is told right after the delete, even when the sync fails",
            fun () ->
                withRecordingVault
                    seedStudyWithAssay
                    (fun vault sent arcPath -> promise {
                        // The delete reads the disk itself, so only the sync load fails.
                        vault.SnapshotLoad <- fun _ -> promise { return Error [| "The load failed." |] }

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        let told =
                            finishedMessages sent
                            |> Array.exists (fun text -> text.Contains "studies/StudyS/isa.study.xlsx")

                        Vitest.expect(told).toBe true
                    })
        )

        Vitest.test (
            "a registration only in memory goes away with the deleted assay and the page is told",
            fun () ->
                withRecordingVault
                    (fun arc ->
                        arc.AddStudy(ArcStudy("StudyS"))
                        arc.AddAssay(ArcAssay("AssayA"))
                    )
                    (fun vault sent arcPath -> promise {
                        vault.arc.Value.RegisterAssay("StudyS", "AssayA")
                        vault.RefreshHasUnsavedArcChangesFlag()

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        Vitest.expect(vault.arc.Value.GetStudy("StudyS").RegisteredAssayIdentifiers.Count).toBe 0

                        let told =
                            finishedMessages sent
                            |> Array.exists (fun text -> text.Contains "studies/StudyS/isa.study.xlsx")

                        Vitest.expect(told).toBe true
                    })
        )

        Vitest.test (
            "a deleted assay leaves the ARC's file list, and a similar name stays",
            fun () ->
                withRecordingVault
                    (fun arc ->
                        arc.AddAssay(ArcAssay("AssayA"))
                        arc.AddAssay(ArcAssay("AssayA2", title = "Old title"))
                    )
                    (fun vault _ arcPath -> promise {
                        vault.arc.Value.GetAssay("AssayA2").Title <- Some "Unsaved title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        let paths = vault.arc.Value.FileSystem.Tree.ToFilePaths()
                        Vitest.expect(paths |> Array.exists (fun path -> path.StartsWith "assays/AssayA/")).toBe false
                        Vitest.expect(paths |> Array.exists (fun path -> path.StartsWith "assays/AssayA2/")).toBe true
                    })
        )

        Vitest.test (
            "deleting an assay that memory does not hold yet succeeds",
            fun () ->
                withRecordingVault
                    (fun arc -> arc.AddAssay(ArcAssay("KeepAssay")))
                    (fun vault _ arcPath -> promise {
                        let! diskArc = loadArcAsync arcPath
                        // ARCtrl does not overwrite files in a full write, so only the new assay is written.
                        baselineArcStaticHashes diskArc
                        diskArc.AddAssay(ArcAssay("LateAssay"))
                        do! diskArc.UpdateAsync arcPath

                        do! deleteOrFail vault arcPath "assays/LateAssay"

                        Vitest.expect(vault.arc.Value.ContainsAssay "LateAssay").toBe false
                        Vitest.expect(vault.arc.Value.ContainsAssay "KeepAssay").toBe true
                    })
        )

        Vitest.test (
            "a second delete while one runs is refused and the first finishes",
            fun () ->
                withRecordingVault
                    (fun arc ->
                        arc.AddAssay(ArcAssay("AssayA"))
                        arc.AddAssay(ArcAssay("AssayB"))
                    )
                    (fun vault _ arcPath -> promise {
                        let first =
                            ArcDeleteHelper.deleteArcEntityInVault
                                vault
                                "assays/AssayA"
                                (fun () -> promise { return () })

                        let second =
                            ArcDeleteHelper.deleteArcEntityInVault
                                vault
                                "assays/AssayB"
                                (fun () -> promise { return () })

                        match! second with
                        | Ok() -> failwith "The second delete should be refused."
                        | Error error -> Vitest.expect(error.Message).toContain "is still running"

                        let! firstResult = first
                        expectDeleted firstResult
                        Vitest.expect(vault.arc.Value.ContainsAssay "AssayA").toBe false
                        Vitest.expect(vault.arc.Value.ContainsAssay "AssayB").toBe true
                    })
        )

        Vitest.test (
            "deleting an assay leaves the DataMap file of another assay as it is",
            fun () ->
                withRecordingVault
                    (fun arc ->
                        let dataMap = Datamap.init ()
                        dataMap.DataContexts.Add(DataContext(label = "Embedded row"))
                        let assayB = ArcAssay("AssayB")
                        assayB.Datamap <- Some dataMap
                        arc.AddAssay(ArcAssay("AssayA"))
                        arc.AddAssay assayB
                    )
                    (fun vault _ arcPath -> promise {
                        // A tool that writes only the DataMap file leaves the embedded copy in the workbook behind.
                        let newerDataMap = Datamap.init ()
                        newerDataMap.DataContexts.Add(DataContext(label = "Different row"))
                        let parentInfo = DatamapParentInfo.create "AssayB" DataMapParent.Assay
                        let contract = newerDataMap.ToCreateContract parentInfo

                        match! ContractExecutor.fulfillContractsSwateAsync arcPath [| contract |] with
                        | Error errors -> failwith (PathHelpers.formatContractErrors errors)
                        | Ok _ -> ()

                        let dataMapPath = join [| arcPath; "assays"; "AssayB"; "isa.datamap.xlsx" |]
                        let! before = readFileBase64Async dataMapPath

                        do! deleteOrFail vault arcPath "assays/AssayA"

                        let! after = readFileBase64Async dataMapPath
                        Vitest.expect(after).toBe before
                    })
        )

        Vitest.test (
            "a failed study folder delete leaves the investigation as it is",
            TestOptions(skip = (TestHelpers.nodePlatform () = "win32" || TestHelpers.runsAsRoot ())),
            fun () ->
                withRecordingVault
                    (fun arc ->
                        arc.AddStudy(ArcStudy("StudyS"))
                        arc.RegisterStudy "StudyS"
                    )
                    (fun vault _ arcPath -> promise {
                        vault.arc.Value.Title <- Some "Unsaved investigation title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        let studiesFolder = join [| arcPath; "studies" |]
                        do! TestHelpers.chmodAsync (studiesFolder, 0o555)

                        do!
                            TestHelpers.withAsyncCleanup
                                (fun () -> TestHelpers.chmodAsync (studiesFolder, 0o755))
                                (fun () -> promise {
                                    let! result =
                                        ArcDeleteHelper.deleteArcEntityInVault
                                            vault
                                            "studies/StudyS"
                                            (fun () -> promise { return () })

                                    Vitest.expect(result.IsError).toBe true

                                    let! diskArc = loadArcAsync arcPath

                                    Vitest.expect(diskArc.RegisteredStudyIdentifiers |> Seq.contains "StudyS").toBe
                                        true

                                    Vitest.expect(vault.arc.Value.Title).toEqual (Some "Unsaved investigation title")
                                    Vitest.expect(vault.hasUnsavedArcChanges).toBe true
                                })
                    })
        )
)

let private seedRegisteredStudy (arc: ARC) =
    arc.AddStudy(ArcStudy("StudyS"))
    arc.RegisterStudy "StudyS"

let private renameStudyOrFail (vault: ArcVault) = promise {
    match!
        ArcRenameHelper.renameArcEntityInVault vault {
            relativePath = "studies/StudyS"
            newName = "StudyT"
        }
    with
    | Ok() -> ()
    | Error error -> failwith error.Message
}

let private dtoOf path (vault: ArcVault) =
    match FileContentDTO.fromArcByPath path vault.arc.Value with
    | Some dto -> dto
    | None -> failwith $"Expected a DTO for {path}."

let private submitEditOn (vault: ArcVault) dto baseRevision =
    match
        vault.TryUpdateArcByFileContentDTO {|
            file = dto
            baseRevision = baseRevision
        |}
    with
    | Ok outcome -> outcome
    | Error error -> failwith error.Message

Vitest.describe (
    "ARC rename publishes its memory change",
    fun () ->
        Vitest.test (
            "an edit of the investigation made before a study rename is refused",
            fun () ->
                withRecordingVault
                    seedRegisteredStudy
                    (fun vault _ _ -> promise {
                        let revision = vault.ArcRevision
                        let oldDto = dtoOf "isa.investigation.xlsx" vault

                        do! renameStudyOrFail vault

                        Vitest.expect(submitEditOn vault oldDto revision).toBe SetArcFileOutcome.Refused

                        let registered = vault.arc.Value.RegisteredStudyIdentifiers |> Seq.toArray
                        Vitest.expect(registered).toEqual [| "StudyT" |]
                    })
        )

        Vitest.test (
            "an edit of the old workbook path sent before a rename is refused",
            fun () ->
                withRecordingVault
                    seedRegisteredStudy
                    (fun vault _ _ -> promise {
                        let revision = vault.ArcRevision
                        let oldDto = dtoOf "studies/StudyS/isa.study.xlsx" vault

                        do! renameStudyOrFail vault

                        Vitest.expect(submitEditOn vault oldDto revision).toBe SetArcFileOutcome.Refused
                        Vitest.expect(vault.arc.Value.ContainsStudy "StudyS").toBe false
                    })
        )

        Vitest.test (
            "a failed study folder rename leaves the investigation as it is",
            TestOptions(skip = (TestHelpers.nodePlatform () = "win32" || TestHelpers.runsAsRoot ())),
            fun () ->
                withRecordingVault
                    seedRegisteredStudy
                    (fun vault _ arcPath -> promise {
                        vault.arc.Value.Title <- Some "Unsaved investigation title"
                        vault.RefreshHasUnsavedArcChangesFlag()

                        let studiesFolder = join [| arcPath; "studies" |]
                        do! TestHelpers.chmodAsync (studiesFolder, 0o555)

                        do!
                            TestHelpers.withAsyncCleanup
                                (fun () -> TestHelpers.chmodAsync (studiesFolder, 0o755))
                                (fun () -> promise {
                                    let! result =
                                        ArcRenameHelper.renameArcEntityInVault vault {
                                            relativePath = "studies/StudyS"
                                            newName = "StudyT"
                                        }

                                    Vitest.expect(result.IsError).toBe true

                                    do!
                                        TestHelpers.waitForCondition
                                            "the vault is idle"
                                            (fun () -> vault.SyncMode = ArcSyncMode.Idle)

                                    let! diskArc = loadArcAsync arcPath
                                    let diskStudies = diskArc.RegisteredStudyIdentifiers |> Seq.toArray
                                    Vitest.expect(diskStudies).toEqual [| "StudyS" |]

                                    Vitest.expect(vault.arc.Value.Title).toEqual (Some "Unsaved investigation title")
                                    Vitest.expect(vault.hasUnsavedArcChanges).toBe true
                                })
                    })
        )
)

let private dataMapNames (dataMap: Datamap option) =
    dataMap.Value.DataContexts |> Seq.choose _.Name |> Seq.toArray

let private expectDataMapSheetAsync arcPath relativePath expected = promise {
    let! workbook = FsSpreadsheet.Js.Xlsx.fromXlsxFile (join [| arcPath; relativePath |])

    let hasDataMapSheet =
        workbook.GetWorksheets() |> Seq.exists (fun sheet -> sheet.Name = "isa_datamap")

    Vitest.expect(hasDataMapSheet).toBe expected
}

let private seedLegacyRegistrationDataMaps (arc: ARC) =
    let assay = ArcAssay("LegacyAssay")
    let assayDataMap = Datamap.init ()
    assayDataMap.DataContexts.Add(DataContext(name = "assay.csv"))
    assay.Datamap <- Some assayDataMap
    arc.AddAssay assay

    let study = ArcStudy("LegacyStudy")
    let studyDataMap = Datamap.init ()
    studyDataMap.DataContexts.Add(DataContext(name = "study.csv"))
    study.Datamap <- Some studyDataMap
    arc.AddStudy study
    arc.RegisterAssay("LegacyStudy", "LegacyAssay")

let private keepOnlyLegacyDataMapSheetsAsync arcPath = promise {
    // withTempArc uses ARCtrl's writer, which includes the legacy sheets as well as the adjacent files.
    for path in [| "assays/LegacyAssay"; "studies/LegacyStudy" |] do
        do! rmAsync (join [| arcPath; path; "isa.datamap.xlsx" |]) (RmOptions())

    do! expectDataMapSheetAsync arcPath "assays/LegacyAssay/isa.assay.xlsx" true
    do! expectDataMapSheetAsync arcPath "studies/LegacyStudy/isa.study.xlsx" true
}

Vitest.describe (
    "ARC rename and delete migrate legacy DataMaps",
    fun () ->
        Vitest.test (
            "deleting an assay migrates the DataMap of the study whose registration changes",
            fun () ->
                withTempArc
                    seedLegacyRegistrationDataMaps
                    (fun arcPath -> promise {
                        do! keepOnlyLegacyDataMapSheetsAsync arcPath
                        let! arc = loadSwateArcAsync arcPath
                        Vitest.expect(dataMapNames (arc.GetStudy("LegacyStudy").Datamap)).toEqual [| "study.csv" |]

                        let! _ = deleteEntityOrFail arcPath "assays/LegacyAssay" arc

                        let! dataMapExists =
                            pathExistsAsync (join [| arcPath; "studies/LegacyStudy/isa.datamap.xlsx" |])

                        Vitest.expect(dataMapExists).toBe true
                        do! expectDataMapSheetAsync arcPath "studies/LegacyStudy/isa.study.xlsx" false
                        let! reloaded = loadSwateArcAsync arcPath
                        Vitest.expect(reloaded.GetStudy("LegacyStudy").RegisteredAssayIdentifiers.Count).toBe 0

                        Vitest.expect(dataMapNames (reloaded.GetStudy("LegacyStudy").Datamap)).toEqual [|
                            "study.csv"
                        |]

                        do!
                            assertDeletedOnDisk
                                arcPath
                                [| "assays"; "LegacyAssay" |]
                                (fun arc -> arc.ContainsAssay "LegacyAssay")
                    })
        )

        Vitest.test (
            "renaming an assay migrates its DataMap and the DataMap of the study that registers it",
            fun () ->
                withTempArc
                    seedLegacyRegistrationDataMaps
                    (fun arcPath -> promise {
                        do! keepOnlyLegacyDataMapSheetsAsync arcPath
                        let! arc = loadSwateArcAsync arcPath

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                {
                                    relativePath = "assays/LegacyAssay"
                                    newName = "RenamedAssay"
                                }
                                (fun () -> Some arc)
                        with
                        | Error error -> failwith error.Message
                        | Ok _ -> ()

                        let! oldFolderExists = pathExistsAsync (join [| arcPath; "assays/LegacyAssay" |])
                        Vitest.expect(oldFolderExists).toBe false

                        for path in [| "assays/RenamedAssay"; "studies/LegacyStudy" |] do
                            let! dataMapExists = pathExistsAsync (join [| arcPath; path; "isa.datamap.xlsx" |])
                            Vitest.expect(dataMapExists).toBe true

                        do! expectDataMapSheetAsync arcPath "assays/RenamedAssay/isa.assay.xlsx" false
                        do! expectDataMapSheetAsync arcPath "studies/LegacyStudy/isa.study.xlsx" false
                        let! reloaded = loadSwateArcAsync arcPath

                        Vitest.expect(dataMapNames (reloaded.GetAssay("RenamedAssay").Datamap)).toEqual [|
                            "assay.csv"
                        |]

                        Vitest.expect(dataMapNames (reloaded.GetStudy("LegacyStudy").Datamap)).toEqual [|
                            "study.csv"
                        |]

                        Vitest
                            .expect(reloaded.GetStudy("LegacyStudy").RegisteredAssayIdentifiers |> Seq.toArray)
                            .toEqual
                            [| "RenamedAssay" |]
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
            "a partial delete removes the entity in memory and keeps unrelated unsaved edits",
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
                        with
                        | Error error -> return failwith error.Message
                        | Ok(Deleted _) -> return failwith "Expected a partial delete outcome."
                        | Ok(PartiallyDeleted(deletion, error)) ->
                            ArcDeleteHelper.applyEntityDeletion deletion arc |> ignore
                            Vitest.expect(arc.ContainsAssay "DeleteAssay").toBe false
                            Vitest.expect(arc.GetAssay("KeepAssay").Title).toEqual (Some "Unsaved local title")
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
            "a partial delete leaves the registration change unsaved, so a Save can retry it",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddStudy(ArcStudy("StudyA"))
                        seed arc
                        arc.RegisterAssay("StudyA", "DeleteAssay")
                        arc.RegisterAssay("StudyA", "KeepAssay")
                    )
                    (fun arcPath -> promise {
                        let! arc = loadSwateArcAsync arcPath
                        baselineArcStaticHashes arc

                        match!
                            ArcDeleteHelper.deleteArcEntityWithAsync
                                ARC.LoadAsyncSwate
                                executeDeleteThenFail
                                arcPath
                                "assays/DeleteAssay"
                        with
                        | Error error -> return failwith error.Message
                        | Ok(Deleted _) -> return failwith "Expected a partial delete outcome."
                        | Ok(PartiallyDeleted(deletion, error)) ->
                            ArcDeleteHelper.applyEntityDeletion deletion arc |> ignore

                            // The study on disk still registers the deleted assay, memory no longer does.
                            let! diskArc = loadSwateArcAsync arcPath

                            Vitest
                                .expect(diskArc.GetStudy("StudyA").RegisteredAssayIdentifiers |> Seq.toArray)
                                .toEqual [| "DeleteAssay"; "KeepAssay" |]

                            Vitest
                                .expect(arc.GetStudy("StudyA").RegisteredAssayIdentifiers |> Seq.toArray)
                                .toEqual [| "KeepAssay" |]

                            Vitest.expect(arc.GetStudy("StudyA").hasInMemoryChanges ()).toBe true
                            Vitest.expect(error.Message).toContain "Save the ARC to retry"
                    })
        )
)
