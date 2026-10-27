module ElectronCore.ArcAddExtensionsTests

open Main.ArcMerge
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultLargeObjects
open Main.ArcVaultFileTree
open Main.ArcVaultFileWatcher
open Main.ARCtrlExtensions
open Main.Bindings.Path
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open ARCtrl
open Vitest

let private expectSome (value: 'T option) (message: string) : 'T =
    match value with
    | Some value -> value
    | None -> failwith message

let private pathExistsAsync = TestHelpers.pathExistsAsync

let private loadArcAsync arcPath = promise {
    match! ARC.LoadAsyncSwate arcPath with
    | Ok arc -> return arc
    | Error errors -> return failwith (PathHelpers.formatContractErrors errors)
}

let private testWindow = TestHelpers.testWindow

let private withTempArc =
    TestHelpers.withTempArcWith "swate-add-arc-file-" "AddArcFileArc"

let private expectBasicAnnotationTable (tables: ResizeArray<ArcTable>) (name: string) =
    Vitest.expect(tables.Count).toBe (1)
    let table = tables.[0]
    Vitest.expect(table.Name).toBe (name)
    Vitest.expect(table.ColumnCount).toBe (3)
    Vitest.expect(table.RowCount).toBe (ARCtrlHelper.ArcFileDefaults.BasicAnnotationTableRowCount)
    Vitest.expect(table.Headers.[0].ToString()).toBe ("Input [Source Name]")
    Vitest.expect(table.Headers.[1].ToString()).toBe ("Protocol Uri")
    Vitest.expect(table.Headers.[2].ToString()).toBe ("Output [Sample Name]")

Vitest.describe (
    "ARC AddArcFileAsync",
    fun () ->
        Vitest.test (
            "adds an assay and creates the canonical assay file",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        do!
                            arc.AddArcFileAsync(
                                arcPath,
                                Swate.Components.Shared.ARCtrlHelper.ArcFileDefaults.createDefaultArcFile
                                    ArcFilesDiscriminate.Assay
                                    "NewAssay"
                            )

                        Vitest.expect(arc.ContainsAssay("NewAssay")).toBe (true)
                        let addedAssay = arc.GetAssay("NewAssay")
                        expectBasicAnnotationTable addedAssay.Tables "NewAssay Table"

                        let canonicalPath = join [| arcPath; "assays"; "NewAssay"; "isa.assay.xlsx" |]
                        let! exists = pathExistsAsync canonicalPath
                        Vitest.expect(exists).toBe (true)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsAssay("NewAssay")).toBe (true)
                        let reloadedAssay = reloadedArc.GetAssay("NewAssay")
                        expectBasicAnnotationTable reloadedAssay.Tables "NewAssay Table"
                    })
        )

        Vitest.test (
            "a first table named after a long identifier, without the Table suffix, is found again after reloading",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        // " Table" does not fit after these identifiers, so each first table is named after the
                        // identifier alone; the last one is also longer than a table name may be.
                        let assayId = "Sugar_extraction_assay_day_2"
                        let studyId = "Sugar_extraction_study_day_2"
                        let runId = "Sugar_extraction_run_day_2_r1"
                        let longAssayId = "Sugar_extraction_assay_day_2_replicate_3"

                        let createDefaultArcFile =
                            Swate.Components.Shared.ARCtrlHelper.ArcFileDefaults.createDefaultArcFile

                        let! arc = loadArcAsync arcPath
                        do! arc.AddArcFileAsync(arcPath, createDefaultArcFile ArcFilesDiscriminate.Assay assayId)
                        do! arc.AddArcFileAsync(arcPath, createDefaultArcFile ArcFilesDiscriminate.Study studyId)
                        do! arc.AddArcFileAsync(arcPath, createDefaultArcFile ArcFilesDiscriminate.Run runId)
                        do! arc.AddArcFileAsync(arcPath, createDefaultArcFile ArcFilesDiscriminate.Assay longAssayId)

                        let! reloadedArc = loadArcAsync arcPath
                        expectBasicAnnotationTable (reloadedArc.GetAssay assayId).Tables assayId
                        expectBasicAnnotationTable (reloadedArc.GetStudy studyId).Tables studyId
                        expectBasicAnnotationTable (reloadedArc.GetRun runId).Tables runId

                        expectBasicAnnotationTable
                            (reloadedArc.GetAssay longAssayId).Tables
                            "Sugar_extraction_assay_day_2_re"
                    })
        )

        Vitest.test (
            "adds a tableless assay without synthesizing a table",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        do! arc.AddArcFileAsync(arcPath, ArcFiles.Assay(ArcAssay("TablelessAssay")))

                        Vitest.expect(arc.ContainsAssay("TablelessAssay")).toBe (true)
                        Vitest.expect(arc.GetAssay("TablelessAssay").Tables.Count).toBe (0)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsAssay("TablelessAssay")).toBe (true)
                        Vitest.expect(reloadedArc.GetAssay("TablelessAssay").Tables.Count).toBe (0)
                    })
        )

        Vitest.test (
            "adds a parent-backed DataMap",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DataMapAssay")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay
                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc arc
                        do! vault.EnqueueFileTreeScan()

                        let request =
                            FileContentDTO.fromArcFile (ArcFiles.DataMap(Some parentInfo, Datamap.init ()))
                            |> expectSome
                            <| "Expected DataMap DTO."

                        match! vault.AddArcFile request with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        Vitest.expect(arc.GetAssay("DataMapAssay").Datamap.IsSome).toBe (true)

                        let dataMapPath = join [| arcPath; "assays"; "DataMapAssay"; "isa.datamap.xlsx" |]
                        let! dataMapExists = pathExistsAsync dataMapPath
                        Vitest.expect(dataMapExists).toBe (true)

                        let dataMapIsLoadedInFileTree =
                            vault.FileTreeWriter.Tree.Entries
                            |> Seq.exists (fun entry -> PathHelpers.pathsEqual entry.path dataMapPath)

                        Vitest.expect(dataMapIsLoadedInFileTree).toBe (true)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.GetAssay("DataMapAssay").Datamap.IsSome).toBe (true)
                    })
        )

        Vitest.test (
            "recreating a deleted DataMap does not restore deleted data",
            fun () ->
                withTempArc
                    (fun arc ->
                        let dataMap = Datamap.init ()
                        dataMap.DataContexts.Add(DataContext(label = "Deleted data"))

                        let assay = ArcAssay("DataMapAssay")
                        assay.Datamap <- Some dataMap
                        arc.AddAssay assay
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay

                        match! arc.TryDeleteDataMapAsync(arcPath, parentInfo) with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        let emptyDataMap = Datamap.init ()
                        Vitest.expect(emptyDataMap.DataContexts.Count).toBe (0)

                        let request =
                            FileContentDTO.fromArcFile (ArcFiles.DataMap(Some parentInfo, emptyDataMap))
                            |> expectSome
                            <| "Expected DataMap DTO."

                        let requestedArcFile =
                            FileContentDTO.toArcFile request |> expectSome <| "Expected request."

                        let requestedDataMap =
                            requestedArcFile.TryGetDataMap() |> expectSome <| "Expected requested DataMap."

                        Vitest.expect(requestedDataMap.DataContexts.Count).toBe (0)

                        match! arc.TryAddArcFileAsync(arcPath, requestedArcFile, false) with
                        | Error errors -> failwith (PathHelpers.formatContractErrors errors)
                        | Ok _ -> ()

                        let recreatedDataMap =
                            arc.GetAssay("DataMapAssay").Datamap |> expectSome <| "Expected DataMap."

                        Vitest.expect(recreatedDataMap.DataContexts.Count).toBe (0)

                        let! reloadedArc = loadArcAsync arcPath

                        let persistedDataMap =
                            reloadedArc.GetAssay("DataMapAssay").Datamap |> expectSome
                            <| "Expected persisted DataMap."

                        let containsDeletedData =
                            persistedDataMap.DataContexts
                            |> Seq.exists (fun context -> context.Label = Some "Deleted data")

                        Vitest.expect(containsDeletedData).toBe (false)
                    })
        )

        Vitest.test (
            "adding a DataMap does not persist unrelated dirty in-memory changes",
            fun () ->
                withTempArc
                    (fun arc ->
                        arc.AddAssay(ArcAssay("DataMapAssay"))
                        arc.AddAssay(ArcAssay("DirtyAssay", title = "Persisted title"))
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        arc.GetAssay("DirtyAssay").Title <- Some "Unsaved title"

                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc arc
                        vault.RefreshHasUnsavedArcChangesFlag()

                        let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay

                        let request =
                            FileContentDTO.fromArcFile (ArcFiles.DataMap(Some parentInfo, Datamap.init ()))
                            |> expectSome
                            <| "Expected DataMap DTO."

                        match! vault.AddArcFile request with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.GetAssay("DataMapAssay").Datamap.IsSome).toBe (true)
                        Vitest.expect(reloadedArc.GetAssay("DirtyAssay").Title).toEqual (Some "Persisted title")

                        let inMemoryArc = vault.arc |> expectSome <| "Expected vault ARC."
                        Vitest.expect(inMemoryArc.GetAssay("DataMapAssay").Datamap.IsSome).toBe (true)
                        Vitest.expect(inMemoryArc.GetAssay("DirtyAssay").Title).toEqual (Some "Unsaved title")
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe (true)
                    })
        )

        Vitest.test (
            "adds the file explorer default assay via vault add path",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let arcFile =
                            Swate.Components.Shared.ARCtrlHelper.ArcFileDefaults.createDefaultArcFile
                                ArcFilesDiscriminate.Assay
                                "New Assay"

                        let tables = arcFile.Tables()
                        expectBasicAnnotationTable tables "New Assay Table"

                        let vault = ArcVault(testWindow ())
                        let! loadedArc = loadArcAsync arcPath
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let request =
                            FileContentDTO.fromArcFile arcFile |> expectSome
                            <| "Expected default assay DTO."

                        match! vault.AddArcFile request with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        let canonicalPath = join [| arcPath; "assays"; "New Assay"; "isa.assay.xlsx" |]
                        let! exists = pathExistsAsync canonicalPath
                        Vitest.expect(exists).toBe (true)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsAssay("New Assay")).toBe (true)
                        let reloadedAssay = reloadedArc.GetAssay("New Assay")
                        expectBasicAnnotationTable reloadedAssay.Tables "New Assay Table"

                        let inMemoryAssay = vault.arc.Value.GetAssay("New Assay")
                        expectBasicAnnotationTable inMemoryAssay.Tables "New Assay Table"

                        let openedDto =
                            FileContentDTO.fromArcByPath "assays/New Assay/isa.assay.xlsx" vault.arc.Value
                            |> expectSome
                            <| "Expected open-file DTO for newly added assay."

                        let openedArcFile =
                            FileContentDTO.toArcFile openedDto |> expectSome
                            <| "Expected open-file DTO to decode to an assay."

                        let openedAssayTables = openedArcFile.Tables()
                        expectBasicAnnotationTable openedAssayTables "New Assay Table"

                        Vitest.expect(vault.hasUnsavedArcChanges).toBe (false)
                        Vitest.expect(vault.arc.Value.hasInMemoryChanges ()).toBe (false)
                        Vitest.expect(vault.isBusyWriting).toBe (false)
                    })
        )

        Vitest.test (
            "vault add rejects duplicate entities",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("ExistingAssay")))
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let request =
                            FileContentDTO.fromArcFile (ArcFiles.Assay(ArcAssay("ExistingAssay")))
                            |> expectSome
                            <| "Expected duplicate assay DTO."

                        match! vault.AddArcFile request with
                        | Ok() -> failwith "Expected duplicate entity add to fail."
                        | Error error ->
                            Vitest.expect(error.Message).toContain ("already contains assay")
                            Vitest.expect(vault.isBusyWriting).toBe (false)
                    })
        )

        Vitest.test (
            "vault rejects a concurrent DataMap add while the first add is pending",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("DataMapAssay")))
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc

                        let parentInfo = DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay

                        let request () =
                            FileContentDTO.fromArcFile (ArcFiles.DataMap(Some parentInfo, Datamap.init ()))
                            |> expectSome
                            <| "Expected DataMap DTO."

                        let firstAdd = vault.AddArcFile(request ())
                        let secondAdd = vault.AddArcFile(request ())

                        match! secondAdd with
                        | Ok() -> failwith "Expected the concurrent DataMap add to be rejected."
                        | Error error ->
                            Vitest
                                .expect(error.Message)
                                .toBe ("ARCitISA is still saving another change. Please wait a moment and try again.")

                        match! firstAdd with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.GetAssay("DataMapAssay").Datamap.IsSome).toBe (true)
                        Vitest.expect(vault.isBusyWriting).toBe (false)
                    })
        )

        Vitest.test (
            "adds a study and creates the canonical study file",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        do!
                            arc.AddArcFileAsync(
                                arcPath,
                                Swate.Components.Shared.ARCtrlHelper.ArcFileDefaults.createDefaultArcFile
                                    ArcFilesDiscriminate.Study
                                    "NewStudy"
                            )

                        Vitest.expect(arc.ContainsStudy("NewStudy")).toBe (true)
                        let addedStudy = arc.GetStudy("NewStudy")
                        expectBasicAnnotationTable addedStudy.Tables "NewStudy Table"

                        let canonicalPath = join [| arcPath; "studies"; "NewStudy"; "isa.study.xlsx" |]
                        let! exists = pathExistsAsync canonicalPath
                        Vitest.expect(exists).toBe (true)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsStudy("NewStudy")).toBe (true)
                        let reloadedStudy = reloadedArc.GetStudy("NewStudy")
                        expectBasicAnnotationTable reloadedStudy.Tables "NewStudy Table"
                    })
        )

        Vitest.test (
            "adds a run and creates the canonical run file",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        do!
                            arc.AddArcFileAsync(
                                arcPath,
                                Swate.Components.Shared.ARCtrlHelper.ArcFileDefaults.createDefaultArcFile
                                    ArcFilesDiscriminate.Run
                                    "NewRun"
                            )

                        Vitest.expect(arc.ContainsRun("NewRun")).toBe (true)
                        let addedRun = arc.GetRun("NewRun")
                        expectBasicAnnotationTable addedRun.Tables "NewRun Table"

                        let canonicalPath = join [| arcPath; "runs"; "NewRun"; "isa.run.xlsx" |]
                        let! exists = pathExistsAsync canonicalPath
                        Vitest.expect(exists).toBe (true)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsRun("NewRun")).toBe (true)
                        let reloadedRun = reloadedArc.GetRun("NewRun")
                        expectBasicAnnotationTable reloadedRun.Tables "NewRun Table"
                    })
        )

        Vitest.test (
            "adds a workflow and creates the canonical workflow file",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        do! arc.AddArcFileAsync(arcPath, ArcFiles.Workflow(ArcWorkflow("NewWorkflow")))

                        let canonicalPath =
                            join [|
                                arcPath
                                "workflows"
                                "NewWorkflow"
                                "isa.workflow.xlsx"
                            |]

                        let! exists = pathExistsAsync canonicalPath
                        Vitest.expect(exists).toBe (true)

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsWorkflow("NewWorkflow")).toBe (true)
                    })
        )

        Vitest.test (
            "rejects duplicate entity identifiers",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("ExistingAssay")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        let! addResult = arc.TryAddArcFileAsync(arcPath, ArcFiles.Assay(ArcAssay("ExistingAssay")))

                        match addResult with
                        | Ok _ -> failwith "Expected duplicate assay add to fail."
                        | Error errors -> Vitest.expect(errors.[0]).toContain ("already contains assay")
                    })
        )

        Vitest.test (
            "explains which ARC entity prevents a DataMap from being added",
            fun () ->
                withTempArc
                    (fun arc ->
                        let assay = ArcAssay("ExistingAssay")
                        assay.Datamap <- Some(Datamap.init ())
                        arc.AddAssay assay
                    )
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let duplicateParent = DatamapParentInfo.create "ExistingAssay" DataMapParent.Assay

                        let! duplicateResult =
                            arc.TryAddArcFileAsync(arcPath, ArcFiles.DataMap(Some duplicateParent, Datamap.init ()))

                        match duplicateResult with
                        | Ok _ -> failwith "Expected duplicate DataMap add to fail."
                        | Error errors ->
                            Vitest
                                .expect(errors.[0])
                                .toBe (
                                    "Parent 'assays/ExistingAssay' already has a DataMap. Delete the existing DataMap before adding a new one."
                                )

                        let missingParent =
                            DatamapParentInfo.create "MissingWorkflow" DataMapParent.Workflow

                        let! missingResult =
                            arc.TryAddArcFileAsync(arcPath, ArcFiles.DataMap(Some missingParent, Datamap.init ()))

                        match missingResult with
                        | Ok _ -> failwith "Expected DataMap add for a missing workflow to fail."
                        | Error errors ->
                            Vitest
                                .expect(errors.[0])
                                .toBe (
                                    "Could not add the DataMap because parent 'workflows/MissingWorkflow' was not found in the current ARC. Refresh the File Explorer and try again."
                                )
                    })
        )

        Vitest.test (
            "rejects unsupported ARC file kinds",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath

                        let unsupportedArcFiles = [|
                            ArcFiles.Investigation(ArcInvestigation("Investigation")), "investigation"
                            ArcFiles.DataMap(None, Datamap.init ()), "which assay, study, run, or workflow"
                            ArcFiles.Template(Template.init "Template"), "template"
                        |]

                        for arcFile, expectedMessage in unsupportedArcFiles do
                            let! addResult = arc.TryAddArcFileAsync(arcPath, arcFile)

                            match addResult with
                            | Ok _ -> failwith $"Expected {expectedMessage} add to fail."
                            | Error errors -> Vitest.expect(errors.[0]).toContain (expectedMessage)
                    })
        )

        // Vitest.test("does not mutate the in-memory ARC when add contracts fail", fun () ->
        //     withTempArc ignore (fun arcPath -> promise {
        //         let! arc = loadArcAsync arcPath
        //         do! mkdirRecursiveAsync (join [| arcPath; "assays" |])
        //         do! writeUtf8FileAsync (join [| arcPath; "assays"; "NewAssay" |]) "conflicting file"

        //         let! addResult = arc.TryAddArcFileAsync(arcPath, ArcFiles.Assay(ArcAssay("NewAssay")))

        //         match addResult with
        //         | Ok _ -> failwith "Expected assay add to fail because the assay folder path is already a file."
        //         | Error _ -> ()

        //         Vitest.expect(arc.ContainsAssay("NewAssay")).toBe(false)
        //     }))

        Vitest.test (
            "keeps existing entities when adding a new one",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddStudy(ArcStudy("ExistingStudy")))
                    (fun arcPath -> promise {
                        let! arc = loadArcAsync arcPath
                        do! arc.AddArcFileAsync(arcPath, ArcFiles.Assay(ArcAssay("NewAssay")))

                        let! reloadedArc = loadArcAsync arcPath
                        Vitest.expect(reloadedArc.ContainsStudy("ExistingStudy")).toBe (true)
                        Vitest.expect(reloadedArc.ContainsAssay("NewAssay")).toBe (true)
                    })
        )

        Vitest.test (
            "AddArcFile uses the scoped ARC add path without persisting unrelated dirty in-memory edits",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("ExistingAssay", title = "Old title")))
                    (fun arcPath -> promise {
                        let! loadedArc = loadArcAsync arcPath
                        loadedArc.GetAssay("ExistingAssay").Title <- Some "Unsaved local title"

                        let vault = ArcVault(testWindow ())
                        vault.path <- Some arcPath
                        vault.SetArc loadedArc
                        vault.RefreshHasUnsavedArcChangesFlag()

                        let newAssayRequest =
                            FileContentDTO.fromArcFile (ArcFiles.Assay(ArcAssay("NewAssay"))) |> expectSome
                            <| "Expected new assay DTO."

                        match! vault.AddArcFile newAssayRequest with
                        | Error error -> failwith error.Message
                        | Ok() -> ()

                        let! reloadedAfterAdd = loadArcAsync arcPath
                        Vitest.expect(reloadedAfterAdd.ContainsAssay("NewAssay")).toBe (true)
                        Vitest.expect(reloadedAfterAdd.GetAssay("ExistingAssay").Title).toEqual (Some "Old title")

                        let inMemoryArc = vault.arc |> expectSome <| "Expected vault ARC."
                        Vitest.expect(inMemoryArc.ContainsAssay("NewAssay")).toBe (true)
                        Vitest.expect(inMemoryArc.GetAssay("ExistingAssay").Title).toEqual (Some "Unsaved local title")
                        Vitest.expect(inMemoryArc.GetAssay("NewAssay").StaticHash).not.toBe (0)
                        Vitest.expect(vault.hasUnsavedArcChanges).toBe (true)
                        Vitest.expect(vault.isBusyWriting).toBe (false)
                    })
        )
)
