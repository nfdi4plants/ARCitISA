module ElectronCore.FileTreeCompactionTests

module FileTreeCreator = Main.FileTreeCreator

open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open Swate.Components.Shared
open Vitest

Vitest.describe (
    "FileIOHelper.createAvailableArcEntityTargets",
    fun () ->
        Vitest.test (
            "lists only solid study and assay entities with canonical ARC files",
            fun () ->
                let targets =
                    createAvailableArcEntityTargets [
                        (ARCtrl.ArcPathHelper.StudiesFolderName,
                         ARCtrl.ArcPathHelper.StudyFileName,
                         fun name -> {
                             Name = name
                             Kind = NotesTargetKind.Study
                         })
                        (ARCtrl.ArcPathHelper.AssaysFolderName,
                         ARCtrl.ArcPathHelper.AssayFileName,
                         fun name -> {
                             Name = name
                             Kind = NotesTargetKind.Assay
                         })
                    ] [
                        FileEntry.create ("arc", "", true)
                        FileEntry.create ("studies", "studies", true)
                        FileEntry.create (".gitkeep", "studies/.gitkeep", false)
                        FileEntry.create ("loose.txt", "studies/loose.txt", false)
                        FileEntry.create ("StudyA", "studies/StudyA", true)
                        FileEntry.create ("isa.study.xlsx", "studies/StudyA/isa.study.xlsx", false)
                        FileEntry.create ("protocol.md", "studies/StudyA/protocols/protocol.md", false)
                        FileEntry.create ("DraftOnly", "studies/DraftOnly", true)
                        FileEntry.create ("AssayA", "assays/AssayA", true)
                        FileEntry.create ("isa.assay.xlsx", "assays/AssayA/isa.assay.xlsx", false)
                        FileEntry.create ("NoCanonicalAssay", "assays/NoCanonicalAssay", true)
                    ]
                    |> Seq.map (fun target -> target.Kind, target.Name)
                    |> Seq.toArray

                Vitest
                    .expect(targets)
                    .toEqual (
                        [|
                            NotesTargetKind.Study, "StudyA"
                            NotesTargetKind.Assay, "AssayA"
                        |]
                    )
        )

        Vitest.test (
            "lists entities from caller supplied canonical file rules",
            fun () ->
                let targets =
                    createAvailableArcEntityTargets [
                        ("workflows", "isa.workflow.xlsx", fun name -> "workflow", name)
                        ("runs", "isa.run.xlsx", fun name -> "run", name)
                    ] [
                        FileEntry.create ("arc", "", true)
                        FileEntry.create ("WorkflowA", "workflows/WorkflowA", true)
                        FileEntry.create ("isa.workflow.xlsx", "workflows/WorkflowA/isa.workflow.xlsx", false)
                        FileEntry.create ("draft.md", "workflows/DraftOnly/draft.md", false)
                        FileEntry.create ("RunA", "runs/RunA", true)
                        FileEntry.create ("isa.run.xlsx", "runs/RunA/isa.run.xlsx", false)
                    ]
                    |> Seq.toArray

                Vitest.expect(targets).toEqual ([| "workflow", "WorkflowA"; "run", "RunA" |])
        )
)
