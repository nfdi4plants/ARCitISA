module ElectronCore.IpcArchitectureReviewTests

open Main.Bindings.Path
open Main.Bindings.Filesystem
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultLargeObjects
open Main.ArcVaultFileTree
open Main.ArcVaultFileWatcher
open Main.ArcVaultTypes
open Main.FileImportAuthorization
open Main.IPC.FileSystemIO
open Main.IPC.Rename
open Main.ARCtrlExtensions
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open ARCtrl.Spreadsheet
open Vitest

let private loadArcAsync = TestHelpers.loadArcAsync
let private pathExistsAsync = TestHelpers.pathExistsAsync
let private testWindow = TestHelpers.testWindow

let private withTempArc =
    TestHelpers.withTempArcWith "swate-ipc-rename-sync-" "RenameSyncArc"

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

let private assertAssayHasNoEmbeddedDatamap arcPath identifier = promise {
    let parentPath = Identifier.Assay.fileNameFromIdentifier identifier

    match!
        ContractExecutor.fulfillReadContractsSwateAsync arcPath [|
            Contract.createRead (parentPath, DTOType.ISA_Assay)
        |]
    with
    | Ok [| { DTO = Some(DTO.Spreadsheet workbook) } |] ->
        Vitest.expect((ArcAssay.fromFsWorkbook workbook).Datamap.IsNone).toBe true
    | Ok _ -> return failwith "Expected the persisted assay workbook."
    | Error errors -> return failwith (PathHelpers.formatContractErrors errors)
}

let private watcherEvent arcPath eventName relativePath : ArcVaultFileSystemEvent = {
    EventName = eventName
    RelativePath = relativePath
    AbsolutePath = join [| arcPath; relativePath |]
}

let private renameRequest relativePath newName : RenamePathRequest = {
    relativePath = relativePath
    newName = newName
}

Vitest.describe (
    "external file picker authorization",
    fun () ->
        Vitest.test (
            "returns picker-owned paths once to the window that selected them",
            fun () ->
                let authorizationId = issue 7101 [| "C:/selected/one.txt"; "C:/selected/two.csv" |]

                match consume 7102 authorizationId with
                | Ok _ -> failwith "A different renderer window must not consume the selection."
                | Error _ -> ()

                match consume 7101 authorizationId with
                | Error error -> failwith error.Message
                | Ok paths -> Vitest.expect(paths).toEqual ([| "C:/selected/one.txt"; "C:/selected/two.csv" |])

                match consume 7101 authorizationId with
                | Ok _ -> failwith "A picker authorization must not be reusable."
                | Error error -> Vitest.expect(error.Message).toContain ("not authorized")
        )

        Vitest.test (
            "a new picker result invalidates the window's earlier selection",
            fun () ->
                let earlier = issue 7103 [| "C:/selected/earlier.txt" |]
                let latest = issue 7103 [| "C:/selected/latest.txt" |]

                match consume 7103 earlier with
                | Ok _ -> failwith "The replaced picker authorization must be rejected."
                | Error _ -> ()

                match consume 7103 latest with
                | Error error -> failwith error.Message
                | Ok paths -> Vitest.expect(paths).toEqual ([| "C:/selected/latest.txt" |])
        )
)

Vitest.describe (
    "IPC architecture review fixes",
    fun () ->

        Vitest.test (
            "watcher ARC merge handles add, change, and unlink events while preserving dirty local state",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("ExistingAssay", title = "Initial title")))
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        loadedArc.Title <- Some "Unsaved local investigation title"

                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc
                        vault.RefreshHasUnsavedArcChangesFlag()

                        let! diskArcForAdd = loadArcAsync arcPath
                        diskArcForAdd.AddAssay(ArcAssay("DiskAssay", title = "Added on disk"))
                        do! diskArcForAdd.UpdateAsync arcPath

                        do!
                            vault.TriggerArcInMemoryMergeOnFileWatcherEvents [
                                watcherEvent arcPath "add" "assays/DiskAssay/isa.assay.xlsx"
                            ]

                        let afterAdd = vault.arc.Value
                        Vitest.expect(afterAdd.ContainsAssay("DiskAssay")).toBe (true)
                        Vitest.expect(afterAdd.Title).toEqual (Some "Unsaved local investigation title")

                        let! diskArcForChange = loadArcAsync arcPath
                        diskArcForChange.GetAssay("DiskAssay").Title <- Some "Changed on disk"
                        do! diskArcForChange.UpdateAsync arcPath

                        do!
                            vault.TriggerArcInMemoryMergeOnFileWatcherEvents [
                                watcherEvent arcPath "change" "assays/DiskAssay/isa.assay.xlsx"
                            ]

                        let afterChange = vault.arc.Value
                        Vitest.expect(afterChange.GetAssay("DiskAssay").Title).toEqual (Some "Changed on disk")
                        Vitest.expect(afterChange.Title).toEqual (Some "Unsaved local investigation title")

                        let! diskArcForDelete = loadArcAsync arcPath
                        diskArcForDelete.RemoveAssay("ExistingAssay")
                        do! diskArcForDelete.UpdateAsync arcPath

                        do!
                            vault.TriggerArcInMemoryMergeOnFileWatcherEvents [
                                watcherEvent arcPath "unlink" "assays/ExistingAssay/isa.assay.xlsx"
                            ]

                        let afterDelete = vault.arc.Value
                        Vitest.expect(afterDelete.ContainsAssay("ExistingAssay")).toBe (false)
                        Vitest.expect(afterDelete.Title).toEqual (Some "Unsaved local investigation title")
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe (true)
                    })
        )

        Vitest.test (
            "explicit ARC merge reports reload failures and preserves the in-memory ARC",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        loadedArc.Title <- Some "Unsaved in-memory title"

                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let investigationPath = join [| arcPath; "isa.investigation.xlsx" |]
                        do! writeFileAsync investigationPath "not an XLSX workbook" TextEncoding.Utf8

                        match!
                            vault.TryTriggerArcInMemoryMergeOnFileWatcherEvents [
                                watcherEvent arcPath "change" "isa.investigation.xlsx"
                            ]
                        with
                        | Ok() -> return failwith "Expected malformed ARC content to fail synchronization."
                        | Error error ->
                            Vitest.expect(error.Message.Length > 0).toBe (true)
                            Vitest.expect(vault.arc.Value.Title).toEqual (Some "Unsaved in-memory title")
                    })
        )

        Vitest.test (
            "ARCtrl try rename updates assay references while keeping dirty edits local-only",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddStudy(ArcStudy("StudyA"))
                        arc.AddAssay(ArcAssay("OldAssay", title = "Persisted renamed assay title"))
                        arc.AddAssay(ArcAssay("KeepAssay", title = "Persisted keep assay title"))
                        arc.RegisterAssay("StudyA", "OldAssay")
                    )
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        loadedArc.GetAssay("OldAssay").Title <- Some "Unsaved renamed assay title"
                        loadedArc.GetAssay("KeepAssay").Title <- Some "Unsaved keep assay title"

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/OldAssay" "NewAssay")
                                loadedArc
                        with
                        | Error renameError -> return failwith renameError.Message
                        | Ok renamedArc ->
                            Vitest.expect(renamedArc.ContainsAssay("OldAssay")).toBe (false)
                            Vitest.expect(renamedArc.ContainsAssay("NewAssay")).toBe (true)

                            Vitest
                                .expect(renamedArc.GetAssay("NewAssay").Title)
                                .toEqual (Some "Unsaved renamed assay title")

                            Vitest
                                .expect(renamedArc.GetAssay("KeepAssay").Title)
                                .toEqual (Some "Unsaved keep assay title")

                            let localStudy = renamedArc.GetStudy("StudyA")

                            Vitest
                                .expect(localStudy.RegisteredAssayIdentifiers |> Seq.contains "OldAssay")
                                .toBe (false)

                            Vitest.expect(localStudy.RegisteredAssayIdentifiers |> Seq.contains "NewAssay").toBe (true)
                            Vitest.expect(renamedArc.hasInMemoryChanges ()).toBe (true)

                            let! oldFolderExists = pathExistsAsync (join [| arcPath; "assays"; "OldAssay" |])
                            let! newFolderExists = pathExistsAsync (join [| arcPath; "assays"; "NewAssay" |])
                            Vitest.expect(oldFolderExists).toBe (false)
                            Vitest.expect(newFolderExists).toBe (true)

                            let! reloadedArc = loadArcAsync arcPath
                            Vitest.expect(reloadedArc.ContainsAssay("OldAssay")).toBe (false)
                            Vitest.expect(reloadedArc.ContainsAssay("NewAssay")).toBe (true)

                            Vitest
                                .expect(reloadedArc.GetAssay("NewAssay").Title)
                                .toEqual (Some "Persisted renamed assay title")

                            Vitest
                                .expect(reloadedArc.GetAssay("KeepAssay").Title)
                                .toEqual (Some "Persisted keep assay title")

                            let diskStudy = reloadedArc.GetStudy("StudyA")
                            Vitest.expect(diskStudy.RegisteredAssayIdentifiers |> Seq.contains "OldAssay").toBe (false)
                            Vitest.expect(diskStudy.RegisteredAssayIdentifiers |> Seq.contains "NewAssay").toBe (true)
                    })
        )

        Vitest.test (
            "ARCtrl try rename moves payload files with the entity folder",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("PayloadAssay", title = "Persisted payload title")))
                    (fun arcPath -> promise {
                        let oldFolder = join [| arcPath; "assays"; "PayloadAssay" |]

                        let! _ =
                            Main.Bindings.Filesystem.mkdirAsync
                                (join [| oldFolder; "dataset"; "nested" |])
                                (MkdirOptions(recursive = true))

                        do! writeFileAsync (join [| oldFolder; "dataset"; "raw.bin" |]) "raw payload" TextEncoding.Utf8

                        do!
                            writeFileAsync
                                (join [| oldFolder; "dataset"; "nested"; "deep.txt" |])
                                "nested payload"
                                TextEncoding.Utf8

                        let! loadResult = ARC.LoadAsyncSwate arcPath
                        let loadedArc = TestHelpers.expectLoadedArc loadResult

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/PayloadAssay" "MovedAssay")
                                loadedArc
                        with
                        | Error renameError -> return failwith renameError.Message
                        | Ok _ ->
                            let newFolder = join [| arcPath; "assays"; "MovedAssay" |]
                            let! oldFolderExists = pathExistsAsync oldFolder
                            Vitest.expect(oldFolderExists).toBe (false)

                            let! rawContent =
                                readFileAsync (join [| newFolder; "dataset"; "raw.bin" |]) TextEncoding.Utf8

                            let! deepContent =
                                readFileAsync (join [| newFolder; "dataset"; "nested"; "deep.txt" |]) TextEncoding.Utf8

                            Vitest.expect(rawContent).toBe "raw payload"
                            Vitest.expect(deepContent).toBe "nested payload"

                            let! reloadedArc = loadArcAsync arcPath
                            Vitest.expect(reloadedArc.ContainsAssay("PayloadAssay")).toBe (false)

                            Vitest
                                .expect(reloadedArc.GetAssay("MovedAssay").Title)
                                .toEqual (Some "Persisted payload title")
                    })
        )

        Vitest.test (
            "ARCtrl try rename validates source against disk before mutating local ARC",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("PersistedAssay")))
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        loadedArc.AddAssay(ArcAssay("LocalOnlyAssay"))

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/LocalOnlyAssay" "RenamedLocalOnlyAssay")
                                loadedArc
                        with
                        | Ok _ -> return failwith "Expected missing disk source rename to fail."
                        | Error renameError ->
                            Vitest.expect(renameError.Message).toContain ("does not contain assay")
                            Vitest.expect(loadedArc.ContainsAssay("LocalOnlyAssay")).toBe (true)
                            Vitest.expect(loadedArc.ContainsAssay("RenamedLocalOnlyAssay")).toBe (false)

                            let! reloadedArc = loadArcAsync arcPath
                            Vitest.expect(reloadedArc.ContainsAssay("LocalOnlyAssay")).toBe (false)
                            Vitest.expect(reloadedArc.ContainsAssay("PersistedAssay")).toBe (true)
                    })
        )

        Vitest.test (
            "ARCtrl try rename rejects existing targets without mutating local ARC",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddAssay(ArcAssay("OldAssay"))
                        arc.AddAssay(ArcAssay("ExistingAssay"))
                    )
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/OldAssay" "ExistingAssay")
                                loadedArc
                        with
                        | Ok _ -> return failwith "Expected conflicting rename target to fail."
                        | Error renameError ->
                            Vitest.expect(renameError.Message).toContain ("destination already exists")
                            Vitest.expect(renameError.Message).toContain ("assays/OldAssay")
                            Vitest.expect(renameError.Message).toContain ("assays/ExistingAssay")
                            Vitest.expect(loadedArc.ContainsAssay("OldAssay")).toBe (true)
                            Vitest.expect(loadedArc.ContainsAssay("ExistingAssay")).toBe (true)

                            let! reloadedArc = loadArcAsync arcPath
                            Vitest.expect(reloadedArc.ContainsAssay("OldAssay")).toBe (true)
                            Vitest.expect(reloadedArc.ContainsAssay("ExistingAssay")).toBe (true)
                    })
        )

        Vitest.test (
            "ArcOpenDisposition exposes the returned created ARC path",
            fun () ->
                Vitest
                    .expect((ArcOpenDisposition.CreatedInCurrent "C:/picked/ReturnedArc").CreatedArcPath)
                    .toEqual (Some "C:/picked/ReturnedArc")

                Vitest
                    .expect((ArcOpenDisposition.CreatedInNewWindow "C:/picked/ReturnedArc").CreatedArcPath)
                    .toEqual (Some "C:/picked/ReturnedArc")
        )

        Vitest.test (
            "ArcOpenDisposition omits created ARC path for non-create outcomes",
            fun () ->
                Vitest.expect((ArcOpenDisposition.FocusedExisting "C:/existing/Arc").CreatedArcPath).toEqual (None)
                Vitest.expect((ArcOpenDisposition.OpenedInCurrent "C:/opened/Arc").CreatedArcPath).toEqual (None)
                Vitest.expect((ArcOpenDisposition.OpenedInNewWindow "C:/opened/Arc").CreatedArcPath).toEqual (None)
        )

)

Vitest.describe (
    "ARC delete and rename validation",
    fun () ->
        Vitest.test (
            "ArcPathValidation.isWithinRootPath rejects out-of-root paths",
            fun () ->
                Vitest.expect(ArcPathValidation.isWithinRootPath "C:/arc" "C:/arc/assays/a.txt").toBe (true)
                Vitest.expect(ArcPathValidation.isWithinRootPath "C:/arc" "C:/other/place.txt").toBe (false)
        )

        Vitest.test (
            "ArcPathValidation.isSafeRelativePathCandidate rejects absolute and traversal paths",
            fun () ->
                Vitest.expect(ArcPathValidation.isSafeRelativePathCandidate "assays/A/file.txt").toBe (true)
                Vitest.expect(ArcPathValidation.isSafeRelativePathCandidate "../outside.txt").toBe (false)
                Vitest.expect(ArcPathValidation.isSafeRelativePathCandidate "/outside.txt").toBe (false)
                Vitest.expect(ArcPathValidation.isSafeRelativePathCandidate "").toBe (false)
        )

        Vitest.test (
            "isDeletePathAllowed permits safe non-ARC filesystem targets",
            fun () ->
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "studies/StudyA/isa.study.xlsx").toBe (true)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "test.fsx").toBe (true)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "studies").toBe (false)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "studies/StudyA/resources").toBe (false)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "studies/StudyA/dataset/raw.tsv").toBe (true)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "README.md").toBe (false)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "../studies/StudyA/isa.study.xlsx").toBe (false)
        )

        Vitest.test (
            "classifyDeleteTarget identifies entity folder delete paths",
            fun () ->
                match ArcEntityPathRules.classifyDeleteTarget "assays/OldAssay" with
                | ArcEntityPathRules.DeletePathClassification.EntityFolderTarget(zone, identifier, normalizedPath) ->
                    Vitest.expect(zone).toEqual (ArcEntityPathRules.AddZone.Assays)
                    Vitest.expect(identifier).toBe ("OldAssay")
                    Vitest.expect(normalizedPath).toBe ("assays/OldAssay")
                | other -> failwith $"Expected entity folder target, got {other}."
        )

        Vitest.test (
            "classifyDeleteTarget identifies canonical entity files",
            fun () ->
                match ArcEntityPathRules.classifyDeleteTarget "workflows/MyWorkflow/isa.workflow.xlsx" with
                | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.EntityFile(zone,
                                                                                                                                       identifier),
                                                                                  normalizedPath) ->
                    Vitest.expect(zone).toEqual (ArcEntityPathRules.AddZone.Workflows)
                    Vitest.expect(identifier).toBe ("MyWorkflow")
                    Vitest.expect(normalizedPath).toBe ("workflows/MyWorkflow/isa.workflow.xlsx")
                | other -> failwith $"Expected canonical entity file target, got {other}."
        )

        Vitest.test (
            "classifyDeleteTarget separates generic and datamap delete paths from entity deletes",
            fun () ->
                match ArcEntityPathRules.classifyDeleteTarget "assays/My Assay/notes/info.md" with
                | ArcEntityPathRules.DeletePathClassification.AddZoneDescendantTarget(zone, normalizedPath) ->
                    Vitest.expect(zone).toEqual (ArcEntityPathRules.AddZone.Assays)
                    Vitest.expect(normalizedPath).toBe ("assays/My Assay/notes/info.md")
                | other -> failwith $"Expected add-zone descendant target, got {other}."

                match ArcEntityPathRules.classifyDeleteTarget "assays/My Assay/isa.datamap.xlsx" with
                | ArcEntityPathRules.DeletePathClassification.CanonicalFileTarget(ArcEntityPathRules.CanonicalArcFileTarget.DataMapFile(zone,
                                                                                                                                        identifier),
                                                                                  normalizedPath) ->
                    Vitest.expect(zone).toEqual (ArcEntityPathRules.AddZone.Assays)
                    Vitest.expect(identifier).toBe ("My Assay")
                    Vitest.expect(normalizedPath).toBe ("assays/My Assay/isa.datamap.xlsx")
                | other -> failwith $"Expected canonical datamap file target, got {other}."
        )

        Vitest.test (
            "directory delete fallback paths remain available for watcher unlink-dir events",
            fun () ->
                let fallbackPaths =
                    ArcEntityPathRules.buildFallbackUnlinkPaths "workflows/MyWorkflow"

                Vitest
                    .expect(fallbackPaths)
                    .toEqual (
                        [
                            "workflows/MyWorkflow/isa.workflow.xlsx"
                            "workflows/MyWorkflow/isa.datamap.xlsx"
                        ]
                    )
        )

        Vitest.test (
            "renameArcEntityAsync rejects non-entity rename paths without disk access",
            fun () -> promise {
                let arc = ARC("test-arc")

                match!
                    ArcRenameHelper.renameArcEntityAsync
                        "unused-arc-path"
                        (renameRequest "assays/StudyA/notes/info.md" "renamed.md")
                        arc
                with
                | Ok _ -> return failwith "Expected non-entity rename path classification to be rejected."
                | Error error -> Vitest.expect(error.Message.Contains "generic files or folders").toBe (true)
            }
        )

        Vitest.test (
            "renameArcEntityAsync rejects canonical ARC file rename paths without disk access",
            fun () -> promise {
                let arc = ARC("test-arc")

                match!
                    ArcRenameHelper.renameArcEntityAsync
                        "unused-arc-path"
                        (renameRequest "assays/OldAssay/isa.assay.xlsx" "NewAssay")
                        arc
                with
                | Ok _ -> return failwith "Expected canonical ARC file rename path to be rejected."
                | Error error ->
                    Vitest.expect(error.Message.Contains "Rename the containing ARC entity folder instead").toBe (true)
            }
        )

)

Vitest.describe (
    "ARC rename datamap contracts",
    fun () ->
        Vitest.test (
            "exports a legacy embedded-only assay datamap before rewriting its renamed parent",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("OldAssay")))
                    (fun arcPath -> promise {
                        let legacy = ArcAssay("OldAssay", datamap = dataMap "dataset/legacy-rename.csv")

                        do!
                            executeOrFail arcPath [|
                                Contract.createUpdate (
                                    Identifier.Assay.fileNameFromIdentifier legacy.Identifier,
                                    DTOType.ISA_Assay,
                                    DTO.Spreadsheet(ArcAssay.toFsWorkbook legacy)
                                )
                            |]

                        let! oldSeparateExists =
                            pathExistsAsync (
                                join [|
                                    arcPath
                                    Identifier.Assay.datamapFileNameFromIdentifier "OldAssay"
                                |]
                            )

                        Vitest.expect(oldSeparateExists).toBe false
                        let! arc = loadSwateArcAsync arcPath

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/OldAssay" "NewAssay")
                                arc
                        with
                        | Error error -> return failwith error.Message
                        | Ok renamedArc ->
                            Vitest.expect(renamedArc.ContainsAssay "NewAssay").toBe true
                            let! oldFolderExists = pathExistsAsync (join [| arcPath; "assays/OldAssay" |])
                            Vitest.expect(oldFolderExists).toBe false

                            let! newSeparateExists =
                                pathExistsAsync (
                                    join [|
                                        arcPath
                                        Identifier.Assay.datamapFileNameFromIdentifier "NewAssay"
                                    |]
                                )

                            Vitest.expect(newSeparateExists).toBe true
                            do! assertAssayHasNoEmbeddedDatamap arcPath "NewAssay"
                            let! reloaded = loadSwateArcAsync arcPath

                            Vitest
                                .expect(reloaded.GetAssay("NewAssay").Datamap.Value.GetDataContext(0).Name)
                                .toEqual (Some "dataset/legacy-rename.csv")
                    })
        )

        Vitest.test (
            "renames an assay with only a separate datamap without exporting or rewriting that datamap",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("OldAssay")))
                    (fun arcPath -> promise {
                        let parentInfo = DatamapParentInfo.create "OldAssay" DataMapParent.Assay

                        do!
                            executeOrFail arcPath [|
                                DataMapContract.update parentInfo (dataMap "dataset/separate-rename.csv")
                            |]

                        let! before =
                            readFileBase64Async (
                                join [|
                                    arcPath
                                    Identifier.Assay.datamapFileNameFromIdentifier "OldAssay"
                                |]
                            )

                        let! contractArc = loadSwateArcAsync arcPath
                        baselineArcStaticHashes contractArc
                        let dirtyBefore = ContractPolicy.captureDirtyDatamapKeys contractArc
                        let raw = contractArc.GetAssayRenameContracts("OldAssay", "NewAssay")
                        let dirty = ContractPolicy.remapKeys "assays/OldAssay" "assays/NewAssay" dirtyBefore

                        let diskPathBefore path =
                            PathHelpers.tryRemapPathPrefix "assays/NewAssay" "assays/OldAssay" path
                            |> Option.defaultValue path

                        match!
                            ContractPolicy.prepareAsync {
                                ArcPath = arcPath
                                Snapshot = contractArc
                                RawContracts = raw
                                DirtyDatamapKeys = dirty
                                IsFullWrite = false
                                DiskPathBeforeExecution = diskPathBefore
                            }
                        with
                        | Error error -> return failwith error
                        | Ok contracts ->
                            Vitest
                                .expect(
                                    contracts
                                    |> Array.exists (fun contract -> contract.DTOType = Some DTOType.ISA_Datamap)
                                )
                                .toBe
                                false

                        let! arc = loadSwateArcAsync arcPath

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/OldAssay" "NewAssay")
                                arc
                        with
                        | Error error -> return failwith error.Message
                        | Ok _ ->
                            let! after =
                                readFileBase64Async (
                                    join [|
                                        arcPath
                                        Identifier.Assay.datamapFileNameFromIdentifier "NewAssay"
                                    |]
                                )

                            Vitest.expect(after).toBe before
                            let! oldFolderExists = pathExistsAsync (join [| arcPath; "assays/OldAssay" |])
                            Vitest.expect(oldFolderExists).toBe false
                            let! reloaded = loadSwateArcAsync arcPath

                            Vitest
                                .expect(reloaded.GetAssay("NewAssay").Datamap.Value.GetDataContext(0).Name)
                                .toEqual (Some "dataset/separate-rename.csv")
                    })
        )

        Vitest.test (
            "renames an assay while other assays have empty separate datamaps without touching their bytes",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddAssay(ArcAssay("OldAssay"))
                        arc.AddAssay(ArcAssay("EmptyAssay"))
                    )
                    (fun arcPath -> promise {
                        let parentInfo = DatamapParentInfo.create "EmptyAssay" DataMapParent.Assay
                        do! executeOrFail arcPath [| DataMapContract.update parentInfo (Datamap.init ()) |]

                        let dataMapPath =
                            join [|
                                arcPath
                                Identifier.Assay.datamapFileNameFromIdentifier "EmptyAssay"
                            |]

                        let parentPath =
                            join [|
                                arcPath
                                Identifier.Assay.fileNameFromIdentifier "EmptyAssay"
                            |]

                        let! beforeDataMap = readFileBase64Async dataMapPath
                        let! beforeParent = readFileBase64Async parentPath
                        let! arc = loadSwateArcAsync arcPath

                        match!
                            ArcRenameHelper.renameArcEntityAsync
                                arcPath
                                (renameRequest "assays/OldAssay" "NewAssay")
                                arc
                        with
                        | Error error -> return failwith error.Message
                        | Ok renamedArc ->
                            Vitest.expect(renamedArc.ContainsAssay "NewAssay").toBe true
                            let! afterDataMap = readFileBase64Async dataMapPath
                            let! afterParent = readFileBase64Async parentPath
                            Vitest.expect(afterDataMap).toBe beforeDataMap
                            Vitest.expect(afterParent).toBe beforeParent
                            let! reloaded = loadSwateArcAsync arcPath
                            Vitest.expect(reloaded.GetAssay("EmptyAssay").Datamap.Value.DataContexts.Count).toBe 0
                    })
        )
)
