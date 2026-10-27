module ElectronCore.ArcMerge.HasInMemoryChangesTests

open ARCtrl
open Main.ARCtrlExtensions
open Vitest

Vitest.describe (
    "_.hasInMemoryChanges",
    fun () ->
        Vitest.test (
            "Ensure GetUpdateContracts sets isDirty `false`",
            fun () ->
                let arc = MockData.createArc ()
                Vitest.expect(arc.hasInMemoryChanges ()).toBe (true)
                arc.GetUpdateContracts() |> ignore
                Vitest.expect(arc.hasInMemoryChanges ()).toBe (false)
        )

        Vitest.test (
            "adding a DataMap to a clean assay makes the ARC dirty",
            fun () ->
                let arc = MockData.createCleanArc ()
                Vitest.expect(arc.hasInMemoryChanges ()).toBe (false)
                arc.Assays.[0].Datamap <- Some(Datamap.init ())
                Vitest.expect(arc.hasInMemoryChanges ()).toBe (true)
        )
)
