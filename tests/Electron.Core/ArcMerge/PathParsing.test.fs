module ElectronCore.ArcMerge.PathParsingTests

open Main.ArcMerge
open Vitest

Vitest.describe (
    "ArcEntityRef.fromPath",
    fun () ->
        Vitest.test (
            "investigation file at root",
            fun () -> Vitest.expect(ArcEntityRef.fromPath "isa.investigation.xlsx").toEqual (ArcEntityRef.Investigation)
        )

        Vitest.test (
            "investigation file with leading folder",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "my-arc/isa.investigation.xlsx")
                    .toEqual (ArcEntityRef.Investigation)
        )

        Vitest.test (
            "assay file (forward slashes)",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "assays/My Assay/isa.assay.xlsx")
                    .toEqual (ArcEntityRef.Assay "My Assay")
        )

        Vitest.test (
            "assay file (backslashes)",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath @"assays\My Assay\isa.assay.xlsx")
                    .toEqual (ArcEntityRef.Assay "My Assay")
        )

        Vitest.test (
            "assay file (absolute path)",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "/home/user/arc/assays/Assay1/isa.assay.xlsx")
                    .toEqual (ArcEntityRef.Assay "Assay1")
        )

        Vitest.test (
            "assay file (Windows absolute path with drive letter)",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath @"C:\Users\arc\assays\My Assay\isa.assay.xlsx")
                    .toEqual (ArcEntityRef.Assay "My Assay")
        )

        Vitest.test (
            "assay datamap file",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "assays/My Assay/isa.datamap.xlsx")
                    .toEqual (ArcEntityRef.AssayDataMap "My Assay")
        )

        Vitest.test (
            "study file",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "studies/My Study/isa.study.xlsx")
                    .toEqual (ArcEntityRef.Study "My Study")
        )

        Vitest.test (
            "study datamap file",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "studies/My Study/isa.datamap.xlsx")
                    .toEqual (ArcEntityRef.StudyDataMap "My Study")
        )

        Vitest.test (
            "run file",
            fun () ->
                Vitest.expect(ArcEntityRef.fromPath "runs/My Run/isa.run.xlsx").toEqual (ArcEntityRef.Run "My Run")
        )

        Vitest.test (
            "run datamap file",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "runs/My Run/isa.datamap.xlsx")
                    .toEqual (ArcEntityRef.RunDataMap "My Run")
        )

        Vitest.test (
            "workflow file",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "workflows/My Workflow/isa.workflow.xlsx")
                    .toEqual (ArcEntityRef.Workflow "My Workflow")
        )

        Vitest.test (
            "workflow datamap file",
            fun () ->
                Vitest
                    .expect(ArcEntityRef.fromPath "workflows/My Workflow/isa.datamap.xlsx")
                    .toEqual (ArcEntityRef.WorkflowDataMap "My Workflow")
        )

        Vitest.test (
            "unknown path returns Unknown",
            fun () ->
                let path = "some/other/file.txt"
                Vitest.expect(ArcEntityRef.fromPath path).toEqual (ArcEntityRef.Unknown path)
        )

        Vitest.test (
            "empty string returns Unknown",
            fun () ->
                let path = ""
                Vitest.expect(ArcEntityRef.fromPath path).toEqual (ArcEntityRef.Unknown path)
        )
)

Vitest.describe (
    "EventName.parse",
    fun () ->
        Vitest.test ("parses 'add' (lowercase)", fun () -> Vitest.expect(EventName.parse "add").toEqual (EventName.Add))

        Vitest.test (
            "parses 'change' (lowercase)",
            fun () -> Vitest.expect(EventName.parse "change").toEqual (EventName.Change)
        )

        Vitest.test (
            "parses 'unlink' (lowercase)",
            fun () -> Vitest.expect(EventName.parse "unlink").toEqual (EventName.Unlink)
        )

        Vitest.test (
            "case-insensitive: 'Change' is accepted",
            fun () -> Vitest.expect(EventName.parse "Change").toEqual (EventName.Change)
        )

        Vitest.test (
            "unknown event name throws",
            fun () ->
                let mutable didThrow = false

                try
                    EventName.parse "rename" |> ignore
                with _ ->
                    didThrow <- true

                Vitest.expect(didThrow).toBe (true)
        )

        Vitest.test (
            "license file at root",
            fun () -> Vitest.expect(ArcEntityRef.fromPath "LICENSE").toEqual (ArcEntityRef.License)
        )

        Vitest.test (
            "license file name in a folder stays unknown",
            fun () -> Vitest.expect(ArcEntityRef.fromPath "foo/LICENSE").toEqual (ArcEntityRef.Unknown "foo/LICENSE")
        )
)
