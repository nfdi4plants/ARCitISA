module ElectronCore.ArcFileMutationHelperTests

open ARCtrl
open Main.ARCtrlExtensions
open Main.ArcVaultHelper
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Vitest

let private expectOk (result: Result<'T, exn>) : 'T =
    match result with
    | Ok value -> value
    | Error err -> failwith err.Message

let private expectSome (value: 'T option) (message: string) : 'T =
    match value with
    | Some value -> value
    | None -> failwith message

Vitest.describe (
    "ArcFileMutationHelper",
    fun () ->
        Vitest.test (
            "normalizeArcFileRequestPath normalizes separators and trims trailing slash",
            fun () ->
                let request =
                    FileContentDTO.create
                        FileContentType.ISA_Assay
                        """{"dummy":"value"}"""
                        @"assays\assay_1\isa.assay.xlsx\"

                let normalized =
                    Swate.Electron.Shared.FileIOHelper.FileContentDTO.normalizeArcFileRequestPath request

                Vitest.expect(normalized.path).toBe ("assays/assay_1/isa.assay.xlsx")
        )

        Vitest.test (
            "inferTextFileTypeFromPath detects markdown by .md extension",
            fun () ->
                Vitest
                    .expect(FileContentDTO.inferTextFileTypeFromPath "notes/readme.md")
                    .toEqual (FileContentType.Markdown)

                Vitest
                    .expect(FileContentDTO.inferTextFileTypeFromPath "notes/README.MD")
                    .toEqual (FileContentType.Markdown)

                Vitest
                    .expect(FileContentDTO.inferTextFileTypeFromPath "assays/assay_1/notes.txt")
                    .toEqual (FileContentType.PlainText)
        )

        Vitest.test (
            "updateARCByFileContentDTO updates an existing ISA assay while preserving static hash",
            fun () ->
                let oldArc = ARC("test-arc")
                let oldAssay = ArcAssay("assay_1", title = "old-title")
                oldAssay.StaticHash <- 1337
                oldArc.AddAssay(oldAssay)

                let updatedAssay = ArcAssay("assay_1", title = "new-title")

                let request =
                    FileContentDTO.fromArcFile (ArcFiles.Assay updatedAssay) |> expectSome
                    <| "Expected assay request DTO."

                let updatedArc = updateARCByFileContentDTO oldArc request |> expectOk
                let resultingAssay = updatedArc.GetAssay("assay_1")

                Vitest.expect(resultingAssay.Title).toEqual (Some "new-title")
                Vitest.expect(resultingAssay.StaticHash).toBe (1337)
        )

        Vitest.test (
            "updateARCByFileContentDTO updates datamap by parent info and preserves static hash",
            fun () ->
                let oldArc = ARC("test-arc")

                let existingDataMap = Datamap.init ()
                existingDataMap.StaticHash <- 777

                let assay = ArcAssay("assay_1", datamap = existingDataMap)
                oldArc.AddAssay(assay)

                let incomingDataMap = Datamap.init ()
                incomingDataMap.StaticHash <- 5

                let datamapParent = DatamapParentInfo.create "assay_1" DataMapParent.Assay

                let request =
                    FileContentDTO.fromArcFile (ArcFiles.DataMap(Some datamapParent, incomingDataMap))
                    |> expectSome
                    <| "Expected datamap request DTO."

                let updatedArc = updateARCByFileContentDTO oldArc request |> expectOk

                let updatedDataMap =
                    updatedArc.TryGetAssay("assay_1") |> expectSome
                    <| "Expected assay to exist after datamap update."
                    |> fun updatedAssay -> updatedAssay.Datamap
                    |> expectSome
                    <| "Expected datamap to be present on assay."

                Vitest.expect(updatedDataMap.StaticHash).toBe (777)
        )

        Vitest.test (
            "fromArcByPath resolves ARC files through shared path lookup",
            fun () ->
                let arc = ARC("test-arc")

                let assay = ArcAssay("assay_1")
                assay.Datamap <- Some(Datamap.init ())
                arc.AddAssay assay

                let study = ArcStudy("study_1")
                study.Datamap <- Some(Datamap.init ())
                arc.AddStudy study

                let workflow = ArcWorkflow("workflow_1")
                workflow.Datamap <- Some(Datamap.init ())
                arc.AddWorkflow workflow

                let run = ArcRun("run_1")
                run.Datamap <- Some(Datamap.init ())
                arc.AddRun run

                let cases = [|
                    "isa.investigation.xlsx", FileContentType.ISA_Investigation
                    "assays/assay_1/isa.assay.xlsx", FileContentType.ISA_Assay
                    "studies/study_1/isa.study.xlsx", FileContentType.ISA_Study
                    "workflows/workflow_1/isa.workflow.xlsx", FileContentType.ISA_Workflow
                    "runs/run_1/isa.run.xlsx", FileContentType.ISA_Run
                    "assays/assay_1/isa.datamap.xlsx", FileContentType.ISA_Datamap
                |]

                for path, expectedFileType in cases do
                    let dto =
                        FileContentDTO.fromArcByPath path arc |> expectSome
                        <| $"Expected DTO for {path}."

                    Vitest.expect(dto.fileType).toEqual (expectedFileType)
                    Vitest.expect(dto.path).toBe (path)
        )

        Vitest.test (
            "updateARCByFileContentDTO accepts semantically identical ISA content",
            fun () ->
                let oldArc = ARC("test-arc")
                oldArc.AddAssay(ArcAssay("assay_1", title = "stable"))

                let request =
                    FileContentDTO.fromArcByPath "assays/assay_1/isa.assay.xlsx" oldArc
                    |> expectSome
                    <| "Expected current assay DTO from arc."

                let updatedArc = updateARCByFileContentDTO oldArc request |> expectOk
                let updatedAssay = updatedArc.GetAssay("assay_1")
                Vitest.expect(updatedAssay.Title).toEqual (Some "stable")
        )

        Vitest.test (
            "copyArcPreservingStaticHashes keeps static hashes for unchanged entities",
            fun () ->
                let sourceArc = ARC("test-arc")
                sourceArc.AddAssay(ArcAssay("assay_1", title = "stable"))

                sourceArc.GetWriteContracts() |> ignore

                let copiedArc = copyArcPreservingStaticHashes sourceArc

                Vitest.expect(copiedArc.StaticHash).toBe (sourceArc.StaticHash)
                Vitest.expect(copiedArc.GetAssay("assay_1").StaticHash).toBe (sourceArc.GetAssay("assay_1").StaticHash)

                let updateContracts = copiedArc.GetUpdateContracts()
                let contractPaths = updateContracts |> Array.map _.Path

                Vitest
                    .expect(contractPaths |> Array.exists (fun path -> path.StartsWith("assays/assay_1/")))
                    .toBe (false)
        )

        Vitest.test (
            "preserved-hash working copy only generates contracts for newly added entity subtree",
            fun () ->
                let oldArc = ARC("test-arc")
                oldArc.AddAssay(ArcAssay("assay_1", title = "stable"))
                oldArc.GetWriteContracts() |> ignore

                let workingArc = copyArcPreservingStaticHashes oldArc
                let newAssay = ArcAssay("assay_2", title = "new-title")

                let request =
                    FileContentDTO.fromArcFile (ArcFiles.Assay newAssay) |> expectSome
                    <| "Expected new assay request DTO."

                let updatedArc = updateARCByFileContentDTO workingArc request |> expectOk
                let updateContracts = updatedArc.GetUpdateContracts()
                let contractPaths = updateContracts |> Array.map _.Path

                Vitest.expect(updateContracts.Length).toBe (4)

                Vitest
                    .expect(
                        contractPaths
                        |> Array.exists (fun path -> path = "assays/assay_2/isa.assay.xlsx")
                    )
                    .toBe (true)

                Vitest
                    .expect(contractPaths |> Array.exists (fun path -> path.StartsWith("assays/assay_2/")))
                    .toBe (true)

                Vitest
                    .expect(contractPaths |> Array.exists (fun path -> path.StartsWith("assays/assay_1/")))
                    .toBe (false)

                Vitest.expect(contractPaths |> Array.exists (fun path -> path = "isa.investigation.xlsx")).toBe (false)
        )

        Vitest.test (
            "scoped add persistence does not flush unrelated in-memory entity edits",
            fun () ->
                let persistedArc = ARC("test-arc")
                persistedArc.AddAssay(ArcAssay("assay_1", title = "stable"))
                persistedArc.GetWriteContracts() |> ignore

                let localDirtyArc = copyArcPreservingStaticHashes persistedArc
                let dirtyAssay = ArcAssay("assay_1", title = "local-unsaved-change")

                let dirtyRequest =
                    FileContentDTO.fromArcFile (ArcFiles.Assay dirtyAssay) |> expectSome
                    <| "Expected local dirty assay request DTO."

                let localDirtyArc = updateARCByFileContentDTO localDirtyArc dirtyRequest |> expectOk

                let addRequest =
                    FileContentDTO.fromArcFile (ArcFiles.Assay(ArcAssay("assay_2", title = "new-assay")))
                    |> expectSome
                    <| "Expected add-assay request DTO."

                // Simulate scoped disk persistence: base the write on a clean reloaded ARC.
                let diskWorkingArc = copyArcPreservingStaticHashes persistedArc

                let updatedArcForDiskPersistence =
                    updateARCByFileContentDTO diskWorkingArc addRequest |> expectOk

                let diskContracts =
                    updatedArcForDiskPersistence.GetUpdateContracts() |> Array.map _.Path

                Vitest
                    .expect(diskContracts |> Array.exists (fun path -> path.StartsWith("assays/assay_2/")))
                    .toBe (true)

                Vitest
                    .expect(diskContracts |> Array.exists (fun path -> path.StartsWith("assays/assay_1/")))
                    .toBe (false)

                // Simulate post-save in-memory state: keep local edits, add the new entity, sync hash baseline from disk write.
                let localWorkingArc = copyArcPreservingStaticHashes localDirtyArc

                let updatedArcForInMemoryState =
                    updateARCByFileContentDTO localWorkingArc addRequest |> expectOk

                syncArcStaticHashes updatedArcForDiskPersistence updatedArcForInMemoryState

                let remainingUnsavedContracts =
                    updatedArcForInMemoryState.GetUpdateContracts() |> Array.map _.Path

                Vitest
                    .expect(
                        remainingUnsavedContracts
                        |> Array.exists (fun path -> path.StartsWith("assays/assay_1/"))
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        remainingUnsavedContracts
                        |> Array.exists (fun path -> path.StartsWith("assays/assay_2/"))
                    )
                    .toBe (false)
        )
)
