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

        Vitest.test (
            "restoreArcStaticHashes gives an entity replaced under the same identifier the saved hash",
            fun () ->
                let arc = MockData.createCleanArc ()
                arc.Assays.[0].Title <- Some "Edited"
                let savedHash = arc.Assays.[0].StaticHash
                let saved = captureArcStaticHashes arc
                arc.GetUpdateContracts() |> ignore
                Vitest.expect(arc.hasInMemoryChanges ()).toBe false

                arc.Assays.[0] <- arc.Assays.[0].Copy()
                arc.Assays.[0].StaticHash <- 0
                restoreArcStaticHashes saved arc

                Vitest.expect(arc.Assays.[0].StaticHash).toBe savedHash
                Vitest.expect(arc.hasInMemoryChanges ()).toBe true
        )
)
