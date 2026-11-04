module ElectronCore.ArcSaveTests

open ARCtrl
open ARCtrl.Contract
open ARCtrl.Spreadsheet
open Fable.Core
open Main.ARCtrlExtensions
open Main.ARCtrlExtensions.ContractExecutor
open Main.ArcMerge
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultFileWatcher
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open Vitest

let private cwl command =
    CWL.Decode.decodeCWLProcessingUnit
        $"cwlVersion: v1.2\nclass: CommandLineTool\nbaseCommand: {command}\ninputs: {{}}\noutputs: {{}}\n"

let private inputs value =
    CWL.DecodeParameters.decodeYAMLParameterFile $"message: {value}\n"

let private seedWorkflows (arc: ARC) =
    let workflow = ArcWorkflow("Workflow", cwlDescription = cwl "echo")
    let run = ArcRun("Run", cwlDescription = cwl "echo", cwlInput = inputs "before")
    arc.AddWorkflow workflow
    arc.AddRun run

let private expectOk =
    function
    | Ok value -> value
    | Error errors -> failwith (String.concat "\n" errors)

let private withArc seed body =
    TestHelpers.withTempArcWith "swate-save-contracts-" "SaveContractsArc" seed body

let private loadArc path = promise {
    let! result = ARC.LoadAsyncSwate path
    let arc = expectOk result
    baselineArcStaticHashes arc
    return arc
}

Vitest.describe (
    "Swate ARC save policy",
    fun () ->
        Vitest.test (
            "generating write or save contracts does not commit the live saved baseline",
            fun () ->
                withArc
                    ignore
                    (fun arcPath -> promise {
                        let arc = ARC("Unwritten")
                        seedWorkflows arc
                        let originalHash = arc.StaticHash
                        arc.GetWriteContractsSwate() |> ignore
                        let! prepared = arc.PrepareUpdateContractsSwateAsync arcPath
                        expectOk prepared |> ignore
                        Vitest.expect(arc.StaticHash).toBe originalHash
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe true
                    })
        )

        Vitest.test (
            "save emits model upserts and create-only defaults with one contract per path",
            fun () ->
                withArc
                    ignore
                    (fun arcPath -> promise {
                        let arc = ARC("NewModel")
                        arc.AddAssay(ArcAssay("Assay", datamap = Datamap.init ()))
                        arc.SetLicenseFulltext("license", path = "COPYING")
                        seedWorkflows arc
                        let! prepared = arc.PrepareUpdateContractsSwateAsync arcPath
                        let contracts = expectOk prepared
                        let modelContracts = contracts |> Array.filter (fun c -> c.DTO.IsSome)
                        let defaults = contracts |> Array.filter (fun c -> c.DTO.IsNone)

                        Vitest.expect(modelContracts |> Array.forall (fun c -> c.Operation = Operation.UPDATE)).toBe
                            true

                        Vitest.expect(defaults |> Array.forall (fun c -> c.Operation = Operation.CREATE)).toBe true

                        Vitest.expect(contracts |> Array.map _.Path |> Array.distinct |> Array.length).toBe
                            contracts.Length

                        Vitest.expect(contracts |> Array.exists (fun c -> c.Path = "COPYING")).toBe true
                        Vitest.expect(contracts |> Array.exists (fun c -> c.Path = "notes/README.md")).toBe false
                        Vitest.expect(contracts |> Array.exists (fun c -> c.Path = "runs/Run/run.yml")).toBe true
                    })
        )

        Vitest.test (
            "CREATE skips a conflicting model without marking it saved",
            fun () ->
                withArc
                    ignore
                    (fun arcPath -> promise {
                        let model = ARC("SaveContractsArc", title = "Unwritten title")
                        let! created = model.TryWriteAsyncSwate arcPath
                        let paths = expectOk created |> contractWrittenPaths
                        Vitest.expect(paths |> Array.contains "isa.investigation.xlsx").toBe false
                        Vitest.expect(model.StaticHash).toBe 0
                        Vitest.expect(model.hasInMemoryChanges ()).toBe true
                        let! persisted = loadArc arcPath
                        Vitest.expect(persisted.Title).toEqual None
                    })
        )

        Vitest.test (
            "creating a run includes YAML inputs and preserves an existing README",
            fun () ->
                withArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        let folder = join [| arcPath; "runs/AddedRun" |]
                        let! _ = mkdirAsync folder (MkdirOptions(recursive = true))
                        do! writeFileAsync (join [| folder; "README.md" |]) "Existing notes" TextEncoding.Utf8

                        let run =
                            ArcRun("AddedRun", cwlDescription = cwl "echo", cwlInput = inputs "created")

                        let! added = arc.TryAddArcFileAsync(arcPath, ArcFiles.Run run)
                        let paths = expectOk added |> contractWrittenPaths
                        Vitest.expect(paths |> Array.contains "runs/AddedRun/run.yml").toBe true
                        Vitest.expect(paths |> Array.contains "runs/AddedRun/README.md").toBe false
                        let! persisted = loadArc arcPath

                        Vitest
                            .expect(CWL.Encode.encodeYAMLParameterFile (persisted.GetRun("AddedRun").CWLInput))
                            .toBe (CWL.Encode.encodeYAMLParameterFile (inputs "created"))

                        let! readme = readFileAsync (join [| folder; "README.md" |]) TextEncoding.Utf8
                        Vitest.expect(readme).toBe "Existing notes"
                    })
        )

        Vitest.test (
            "fallback save preserves existing defaults and payload while filling emitted missing scaffolds",
            fun () ->
                withArc
                    (fun arc -> arc.AddAssay(ArcAssay("Assay")))
                    (fun arcPath -> promise {
                        let readme = join [| arcPath; "assays/Assay/README.md" |]
                        let gitkeep = join [| arcPath; "assays/.gitkeep" |]
                        let missing = join [| arcPath; "assays/Assay/protocols/.gitkeep" |]
                        let gitignore = join [| arcPath; ".gitignore" |]
                        let payload = join [| arcPath; "payload.txt" |]
                        do! writeFileAsync readme "custom README" TextEncoding.Utf8
                        do! writeFileAsync gitkeep "custom keep" TextEncoding.Utf8
                        do! writeFileAsync gitignore "custom ignore" TextEncoding.Utf8
                        do! writeFileAsync payload "unmanaged payload" TextEncoding.Utf8
                        do! rmAsync missing (RmOptions())
                        let! arc = loadArc arcPath
                        arc.StaticHash <- 0
                        arc.Title <- Some "Updated"
                        let! result = arc.TryUpdateAsyncSwate arcPath
                        let written = expectOk result |> contractWrittenPaths
                        Vitest.expect(written |> Array.contains "assays/Assay/README.md").toBe false
                        Vitest.expect(written |> Array.contains "assays/.gitkeep").toBe false
                        Vitest.expect(written |> Array.contains "assays/Assay/protocols/.gitkeep").toBe true
                        let! actualReadme = readFileAsync readme TextEncoding.Utf8
                        let! actualKeep = readFileAsync gitkeep TextEncoding.Utf8
                        let! actualIgnore = readFileAsync gitignore TextEncoding.Utf8
                        let! actualPayload = readFileAsync payload TextEncoding.Utf8
                        Vitest.expect(actualReadme).toBe "custom README"
                        Vitest.expect(actualKeep).toBe "custom keep"
                        Vitest.expect(actualIgnore).toBe "custom ignore"
                        Vitest.expect(actualPayload).toBe "unmanaged payload"
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                    })
        )

        Vitest.test (
            "workflow-only and run-input-only edits save all standard sidecars and reload correctly",
            fun () ->
                withArc
                    seedWorkflows
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        arc.GetWorkflow("Workflow").CWLDescription <- Some(cwl "printf")
                        arc.GetRun("Run").CWLDescription <- Some(cwl "cat")
                        arc.GetRun("Run").CWLInput <- inputs "after"
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe true
                        let! result = arc.TryUpdateAsyncSwate arcPath
                        let paths = expectOk result |> contractWrittenPaths |> Array.sort

                        Vitest
                            .expect(paths)
                            .toEqual (
                                [|
                                    "runs/Run/run.cwl"
                                    "runs/Run/run.yml"
                                    "workflows/Workflow/workflow.cwl"
                                |]
                            )

                        Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                        let! reloaded = loadArc arcPath

                        Vitest
                            .expect(
                                CWL.Encode.encodeProcessingUnit (reloaded.GetWorkflow("Workflow").CWLDescription.Value)
                            )
                            .toBe (CWL.Encode.encodeProcessingUnit (cwl "printf"))

                        Vitest
                            .expect(CWL.Encode.encodeProcessingUnit (reloaded.GetRun("Run").CWLDescription.Value))
                            .toBe (CWL.Encode.encodeProcessingUnit (cwl "cat"))

                        Vitest
                            .expect(CWL.Encode.encodeYAMLParameterFile (reloaded.GetRun("Run").CWLInput))
                            .toBe (CWL.Encode.encodeYAMLParameterFile (inputs "after"))

                        let! noop = arc.TryUpdateAsyncSwate arcPath
                        Vitest.expect(expectOk noop).toHaveLength 0
                    })
        )

        Vitest.test (
            "removing workflow content keeps existing sidecar files",
            fun () ->
                withArc
                    seedWorkflows
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        // The upstream seed writer omits run.yml; persist its model content once.
                        do!
                            writeFileAsync
                                (join [| arcPath; "runs/Run/run.yml" |])
                                (CWL.Encode.encodeYAMLParameterFile (inputs "before"))
                                TextEncoding.Utf8

                        let files = [|
                            "workflows/Workflow/workflow.cwl"
                            "runs/Run/run.cwl"
                            "runs/Run/run.yml"
                        |]

                        let! before =
                            files
                            |> Array.map (fun path -> readFileAsync (join [| arcPath; path |]) TextEncoding.Utf8)
                            |> Promise.all

                        arc.GetWorkflow("Workflow").CWLDescription <- None
                        arc.GetRun("Run").CWLDescription <- None
                        arc.GetRun("Run").CWLInput.Clear()
                        let! result = arc.TryUpdateAsyncSwate arcPath
                        Vitest.expect(expectOk result).toHaveLength 0

                        let! after =
                            files
                            |> Array.map (fun path -> readFileAsync (join [| arcPath; path |]) TextEncoding.Utf8)
                            |> Promise.all

                        Vitest.expect(after).toEqual before
                    })
        )

        Vitest.test (
            "a failed save keeps the live model dirty and a repaired destination can be retried",
            fun () ->
                withArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        let workbookPath = join [| arcPath; "isa.investigation.xlsx" |]
                        arc.Title <- Some "Retry title"
                        let savedHash = arc.StaticHash
                        do! rmAsync workbookPath (RmOptions())
                        let! _ = mkdirAsync workbookPath (MkdirOptions())
                        let! failed = arc.TryUpdateAsyncSwate arcPath
                        Vitest.expect(Result.isError failed).toBe true
                        Vitest.expect(arc.StaticHash).toBe savedHash
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe true
                        do! rmAsync workbookPath (RmOptions(recursive = true))
                        let! retried = arc.TryUpdateAsyncSwate arcPath
                        expectOk retried |> ignore
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                        let! persisted = loadArc arcPath
                        Vitest.expect(persisted.Title).toEqual (Some "Retry title")
                    })
        )

        Vitest.test (
            "edits during a save remain dirty and the captured content is what reaches disk",
            fun () ->
                withArc
                    seedWorkflows
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        arc.Title <- Some "Captured title"
                        arc.GetWorkflow("Workflow").CWLDescription <- Some(cwl "captured")
                        arc.GetRun("Run").CWLInput <- inputs "captured"
                        let mutable start = ignore
                        let mutable release = ignore

                        let started =
                            JS.Constructors.Promise.Create(fun resolve _ -> start <- fun () -> resolve ())

                        let gate =
                            JS.Constructors.Promise.Create(fun resolve _ -> release <- fun () -> resolve ())

                        let execute path contracts = promise {
                            start ()
                            do! gate
                            return! fulfillContractsSwateAsync path contracts
                        }

                        let saving = persistArcSaveWithAsync execute arcPath arc
                        do! started
                        arc.Title <- Some "Later title"
                        arc.GetWorkflow("Workflow").CWLDescription <- Some(cwl "later")
                        arc.GetRun("Run").CWLInput.[0] <- (inputs "later").[0]
                        release ()
                        let! saved = saving
                        expectOk saved |> ignore
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe true
                        let! persisted = loadArc arcPath
                        Vitest.expect(persisted.Title).toEqual (Some "Captured title")

                        Vitest
                            .expect(
                                CWL.Encode.encodeProcessingUnit (
                                    persisted.GetWorkflow("Workflow").CWLDescription.Value
                                )
                            )
                            .toBe (CWL.Encode.encodeProcessingUnit (cwl "captured"))

                        Vitest
                            .expect(CWL.Encode.encodeYAMLParameterFile (persisted.GetRun("Run").CWLInput))
                            .toBe (CWL.Encode.encodeYAMLParameterFile (inputs "captured"))

                        let! retried = arc.TryUpdateAsyncSwate arcPath
                        expectOk retried |> ignore
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                    })
        )

        Vitest.test (
            "no-op saves with empty datamaps do not write files or recreate missing scaffolds",
            fun () ->
                withArc
                    (fun arc -> arc.AddAssay(ArcAssay("Assay", datamap = Datamap.init ())))
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        let path = join [| arcPath; "assays/Assay/README.md" |]
                        do! rmAsync path (RmOptions())
                        let! saved = arc.TryUpdateAsyncSwate arcPath
                        Vitest.expect(expectOk saved).toHaveLength 0
                        let! exists = TestHelpers.pathExistsAsync path
                        Vitest.expect(exists).toBe false
                    })
        )
)

for enabled in [ false; true ] do
    Vitest.test (
        $"ARC creation applies the actual new ARC notes setting ({enabled}) before renderer follow-up",
        fun () -> promise {
            let! root = TestHelpers.createTempDirectoryAsync "swate-create-notes-setting-"
            let arcPath = join [| root; "arc" |]
            let vault = ArcVault(TestHelpers.testWindow ())

            let cleanup () = promise {
                do! vault.StopFileWatcher()
                Vitest.vi.unstubAllEnvs ()
                do! TestHelpers.removeDirectoryAsync root
            }

            try
                Vitest.vi.stubEnv ("SWATE_TEST_USER_DATA", Some root)
                let settings = Main.UserDefaults.production.Effective()

                match
                    Main.UserDefaults.production.Update(
                        {
                            settings with
                                AutoCreateNotesFolder = enabled
                        }
                    )
                with
                | Error error -> failwith error
                | Ok _ -> ()

                do! vault.CreateARC(arcPath, "CreatedWithNotesSetting")
                let! hasNotes = TestHelpers.pathExistsAsync (join [| arcPath; "notes" |])
                let! hasReadme = TestHelpers.pathExistsAsync (join [| arcPath; "notes/README.md" |])
                Vitest.expect(hasNotes).toBe enabled
                Vitest.expect(hasReadme).toBe enabled
                do! cleanup ()
            with error ->
                do! cleanup ()
                return raise error
        }
    )

let private saveDataMap filePath =
    let dataMap = Datamap.init ()
    dataMap.DataContexts.Add(DataContext(name = filePath))
    dataMap

let private saveParentPath (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> ARCtrl.Helper.Identifier.Assay.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Study -> ARCtrl.Helper.Identifier.Study.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Run -> ARCtrl.Helper.Identifier.Run.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Workflow -> ARCtrl.Helper.Identifier.Workflow.fileNameFromIdentifier parentInfo.ParentId

let private saveParentDataMap (arc: ARC) (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> arc.GetAssay(parentInfo.ParentId).Datamap.Value
    | DataMapParent.Study -> arc.GetStudy(parentInfo.ParentId).Datamap.Value
    | DataMapParent.Run -> arc.GetRun(parentInfo.ParentId).Datamap.Value
    | DataMapParent.Workflow -> arc.GetWorkflow(parentInfo.ParentId).Datamap.Value

let private saveParentDtoType (parentInfo: DatamapParentInfo) =
    match parentInfo.Parent with
    | DataMapParent.Assay -> DTOType.ISA_Assay
    | DataMapParent.Study -> DTOType.ISA_Study
    | DataMapParent.Run -> DTOType.ISA_Run
    | DataMapParent.Workflow -> DTOType.ISA_Workflow

let private saveParentArcFile parent identifier dm =
    match parent with
    | DataMapParent.Assay -> ArcFiles.Assay(ArcAssay(identifier, datamap = dm))
    | DataMapParent.Study -> ArcFiles.Study(ArcStudy(identifier, datamap = dm), [])
    | DataMapParent.Run -> ArcFiles.Run(ArcRun(identifier, datamap = dm))
    | DataMapParent.Workflow -> ArcFiles.Workflow(ArcWorkflow(identifier, datamap = dm))

let private assertSavedDataMap (arc: ARC) parentInfo expected =
    let actual = saveParentDataMap arc parentInfo
    Vitest.expect(actual.DataContexts.Count).toBe 1
    Vitest.expect(actual.DataContexts.[0].Name).toEqual (Some expected)

let private hasSavedEmbeddedDataMap arcPath parentInfo = promise {
    let contract =
        Contract.createRead (saveParentPath parentInfo, saveParentDtoType parentInfo)

    let! read = fulfillReadContractsSwateAsync arcPath [| contract |]
    let contracts = expectOk read
    let workbook = contracts.[0].DTO.Value.AsSpreadsheet()

    return
        workbook.GetWorksheets()
        |> Seq.exists (fun worksheet ->
            try
                (ARCtrl.Spreadsheet.DatamapTable.tryFromFsWorksheet worksheet).IsSome
            with _ ->
                false
        )
}

let private saveMapPath parentInfo dm =
    (DataMapContract.update parentInfo dm).Path

/// Explicit UPDATE payloads reproduce embedded-only legacy files independently of the save policy.
let private writeLegacyAssay arcPath identifier filePath separate = promise {
    let dm = saveDataMap filePath
    let assay = ArcAssay(identifier, datamap = dm)
    let info = DatamapParentInfo.create identifier DataMapParent.Assay

    let contract =
        Contract.createUpdate (saveParentPath info, DTOType.ISA_Assay, DTO.Spreadsheet(ArcAssay.toFsWorkbook assay))

    let! written = fulfillContractsSwateAsync arcPath [| contract |]
    expectOk written |> ignore

    if separate then
        let! exported = fulfillContractsSwateAsync arcPath [| DataMapContract.update info dm |]
        expectOk exported |> ignore
    else
        do! rmAsync (join [| arcPath; saveMapPath info dm |]) (RmOptions(force = true))
}

for parent in
    [
        DataMapParent.Assay
        DataMapParent.Study
        DataMapParent.Run
        DataMapParent.Workflow
    ] do
    Vitest.test (
        $"{parent} datamap-only edits survive saving and reloading",
        fun () ->
            withArc
                ignore
                (fun arcPath -> promise {
                    let! arc = loadArc arcPath
                    let info = DatamapParentInfo.create "MapParent" parent
                    let before = $"dataset/{parent}-before.csv"
                    let after = $"dataset/{parent}-after.csv"

                    let! added =
                        arc.TryAddArcFileAsync(arcPath, saveParentArcFile parent info.ParentId (saveDataMap before))

                    expectOk added |> ignore
                    let! arc = loadArc arcPath
                    saveParentDataMap arc info |> fun dm -> dm.DataContexts.[0].Name <- Some after
                    let! saved = arc.TryUpdateAsyncSwate arcPath
                    expectOk saved |> ignore
                    let! reloaded = loadArc arcPath
                    assertSavedDataMap reloaded info after
                    let! embedded = hasSavedEmbeddedDataMap arcPath info
                    Vitest.expect(embedded).toBe false
                    Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                })
    )

    Vitest.test (
        $"{parent} parent-only edits preserve separate datamap bytes",
        fun () ->
            withArc
                ignore
                (fun arcPath -> promise {
                    let! arc = loadArc arcPath
                    let info = DatamapParentInfo.create "ParentOnly" parent
                    let dm = saveDataMap $"dataset/{parent}-parent-only.csv"
                    let! added = arc.TryAddArcFileAsync(arcPath, saveParentArcFile parent info.ParentId dm)
                    expectOk added |> ignore
                    let! arc = loadArc arcPath
                    let absolutePath = join [| arcPath; saveMapPath info dm |]
                    let! before = readFileBase64Async absolutePath

                    match parent with
                    | DataMapParent.Assay -> arc.GetAssay(info.ParentId).Title <- Some "Changed metadata"
                    | DataMapParent.Study -> arc.GetStudy(info.ParentId).Title <- Some "Changed metadata"
                    | DataMapParent.Run -> arc.GetRun(info.ParentId).Title <- Some "Changed metadata"
                    | DataMapParent.Workflow -> arc.GetWorkflow(info.ParentId).Title <- Some "Changed metadata"

                    let! saved = arc.TryUpdateAsyncSwate arcPath
                    expectOk saved |> ignore
                    let! after = readFileBase64Async absolutePath
                    Vitest.expect(after).toBe before
                })
    )

    Vitest.test (
        $"creating a {parent} exports its datamap separately",
        fun () ->
            withArc
                ignore
                (fun arcPath -> promise {
                    let! arc = loadArc arcPath
                    let info = DatamapParentInfo.create "Created" parent
                    let filePath = $"dataset/{parent}-created.csv"
                    let dm = saveDataMap filePath
                    let! added = arc.TryAddArcFileAsync(arcPath, saveParentArcFile parent info.ParentId dm)
                    expectOk added |> ignore
                    let! exists = TestHelpers.pathExistsAsync (join [| arcPath; saveMapPath info dm |])
                    Vitest.expect(exists).toBe true
                    let! embedded = hasSavedEmbeddedDataMap arcPath info
                    Vitest.expect(embedded).toBe false
                    let! reloaded = loadArc arcPath
                    assertSavedDataMap reloaded info filePath
                })
    )

Vitest.describe (
    "separate datamap migration during saves",
    fun () ->
        Vitest.test (
            "only a touched embedded-only legacy assay migrates",
            fun () ->
                withArc
                    (fun arc ->
                        arc.AddAssay(ArcAssay("A"))
                        arc.AddAssay(ArcAssay("B"))
                    )
                    (fun arcPath -> promise {
                        do! writeLegacyAssay arcPath "A" "dataset/legacy-A.csv" false
                        do! writeLegacyAssay arcPath "B" "dataset/legacy-B.csv" false
                        let infoA = DatamapParentInfo.create "A" DataMapParent.Assay
                        let infoB = DatamapParentInfo.create "B" DataMapParent.Assay
                        let! beforeB = readFileBase64Async (join [| arcPath; saveParentPath infoB |])
                        let! arc = loadArc arcPath
                        arc.GetAssay("A").Title <- Some "Migrate A"
                        let! saved = arc.TryUpdateAsyncSwate arcPath
                        expectOk saved |> ignore
                        let! afterB = readFileBase64Async (join [| arcPath; saveParentPath infoB |])
                        Vitest.expect(afterB).toBe beforeB

                        let! bMapExists =
                            TestHelpers.pathExistsAsync (
                                join [|
                                    arcPath
                                    saveMapPath infoB (saveParentDataMap arc infoB)
                                |]
                            )

                        Vitest.expect(bMapExists).toBe false

                        let! aMapExists =
                            TestHelpers.pathExistsAsync (
                                join [|
                                    arcPath
                                    saveMapPath infoA (saveParentDataMap arc infoA)
                                |]
                            )

                        Vitest.expect(aMapExists).toBe true
                        let! embeddedA = hasSavedEmbeddedDataMap arcPath infoA
                        Vitest.expect(embeddedA).toBe false
                        let! reloaded = loadArc arcPath
                        assertSavedDataMap reloaded infoA "dataset/legacy-A.csv"
                        assertSavedDataMap reloaded infoB "dataset/legacy-B.csv"
                    })
        )

        Vitest.test (
            "a datamap-only save removes a legacy sheet once and preserves the parent on a second save",
            fun () ->
                withArc
                    (fun arc -> arc.AddAssay(ArcAssay("A")))
                    (fun arcPath -> promise {
                        do! writeLegacyAssay arcPath "A" "dataset/rule-B-original.csv" true
                        let info = DatamapParentInfo.create "A" DataMapParent.Assay
                        let! arc = loadArc arcPath
                        let dm = saveParentDataMap arc info
                        dm.DataContexts.[0].Name <- Some "dataset/rule-B-first.csv"
                        let! saved = arc.TryUpdateAsyncSwate arcPath
                        expectOk saved |> ignore
                        let! embedded = hasSavedEmbeddedDataMap arcPath info
                        Vitest.expect(embedded).toBe false
                        let! firstReload = loadArc arcPath
                        assertSavedDataMap firstReload info "dataset/rule-B-first.csv"
                        let! before = readFileBase64Async (join [| arcPath; saveParentPath info |])
                        dm.DataContexts.[0].Name <- Some "dataset/rule-B-second.csv"
                        let! secondSaved = arc.TryUpdateAsyncSwate arcPath
                        expectOk secondSaved |> ignore
                        let! after = readFileBase64Async (join [| arcPath; saveParentPath info |])
                        Vitest.expect(after).toBe before
                        let! secondReload = loadArc arcPath
                        assertSavedDataMap secondReload info "dataset/rule-B-second.csv"
                    })
        )

        Vitest.test (
            "a failed safety export preserves the legacy parent and can be retried",
            fun () ->
                withArc
                    (fun arc -> arc.AddAssay(ArcAssay("A")))
                    (fun arcPath -> promise {
                        do! writeLegacyAssay arcPath "A" "dataset/export-failure.csv" false
                        let info = DatamapParentInfo.create "A" DataMapParent.Assay
                        let! arc = loadArc arcPath
                        let parentPath = join [| arcPath; saveParentPath info |]
                        let! before = readFileBase64Async parentPath
                        arc.GetAssay("A").Title <- Some "Retry migration"
                        let savedHash = arc.GetAssay("A").StaticHash
                        let mutable rejectedExport = false

                        let execute _ (contracts: Contract[]) =
                            rejectedExport <- contracts.Length > 0 && contracts.[0].DTOType = Some DTOType.ISA_Datamap
                            JS.Constructors.Promise.resolve (Error [| "injected datamap export failure" |])

                        let! failed = persistArcSaveWithAsync execute arcPath arc
                        Vitest.expect(rejectedExport).toBe true
                        Vitest.expect(Result.isError failed).toBe true
                        Vitest.expect(arc.GetAssay("A").StaticHash).toBe savedHash
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe true
                        let! after = readFileBase64Async parentPath
                        Vitest.expect(after).toBe before
                        let! embedded = hasSavedEmbeddedDataMap arcPath info
                        Vitest.expect(embedded).toBe true
                        let! retried = arc.TryUpdateAsyncSwate arcPath
                        expectOk retried |> ignore
                        let! migrated = hasSavedEmbeddedDataMap arcPath info
                        Vitest.expect(migrated).toBe false
                        let! reloaded = loadArc arcPath
                        assertSavedDataMap reloaded info "dataset/export-failure.csv"
                        Vitest.expect(reloaded.GetAssay("A").Title).toEqual (Some "Retry migration")
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                    })
        )

        Vitest.test (
            "save captures datamap and metadata before asynchronous preparation",
            fun () ->
                withArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArc arcPath
                        let info = DatamapParentInfo.create "Captured" DataMapParent.Assay

                        let! added =
                            arc.TryAddArcFileAsync(
                                arcPath,
                                saveParentArcFile info.Parent info.ParentId (saveDataMap "dataset/capture-before.csv")
                            )

                        expectOk added |> ignore
                        let! arc = loadArc arcPath
                        let dm = saveParentDataMap arc info
                        dm.DataContexts.[0].Name <- Some "dataset/capture-saved.csv"
                        arc.GetAssay(info.ParentId).Title <- Some "Captured metadata"
                        let gate, release = TestHelpers.deferred ()

                        let execute path contracts = promise {
                            do! gate
                            return! fulfillContractsSwateAsync path contracts
                        }

                        let saving = persistArcSaveWithAsync execute arcPath arc
                        dm.DataContexts.[0].Name <- Some "dataset/capture-later.csv"
                        arc.GetAssay(info.ParentId).Title <- Some "Later metadata"
                        release ()
                        let! result = saving
                        expectOk result |> ignore
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe true
                        let! reloaded = loadArc arcPath
                        assertSavedDataMap reloaded info "dataset/capture-saved.csv"
                        Vitest.expect(reloaded.GetAssay(info.ParentId).Title).toEqual (Some "Captured metadata")
                        let! retried = arc.TryUpdateAsyncSwate arcPath
                        expectOk retried |> ignore
                        Vitest.expect(arc.hasInMemoryChanges ()).toBe false
                        let! finalReload = loadArc arcPath
                        assertSavedDataMap finalReload info "dataset/capture-later.csv"
                        Vitest.expect(finalReload.GetAssay(info.ParentId).Title).toEqual (Some "Later metadata")
                    })
        )

        Vitest.test (
            "full ARC writes export assay datamaps without an embedded sheet",
            fun () -> promise {
                let! root = TestHelpers.createTempDirectoryAsync "swate-full-write-datamap-"
                let arcPath = join [| root; "arc" |]

                try
                    let arc = ARC("FullWriteDataMap")
                    let info = DatamapParentInfo.create "FullWrite" DataMapParent.Assay
                    let dm = saveDataMap "dataset/full-write.csv"
                    arc.AddAssay(ArcAssay(info.ParentId, datamap = dm))
                    let! written = arc.TryWriteAsyncSwate arcPath
                    expectOk written |> ignore
                    let! exists = TestHelpers.pathExistsAsync (join [| arcPath; saveMapPath info dm |])
                    Vitest.expect(exists).toBe true
                    let! embedded = hasSavedEmbeddedDataMap arcPath info
                    Vitest.expect(embedded).toBe false
                    let! reloaded = loadArc arcPath
                    assertSavedDataMap reloaded info "dataset/full-write.csv"
                    do! TestHelpers.removeDirectoryAsync root
                with error ->
                    do! TestHelpers.removeDirectoryAsync root
                    return raise error
            }
        )
)
