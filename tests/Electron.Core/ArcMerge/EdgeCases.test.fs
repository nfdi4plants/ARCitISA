module ElectronCore.ArcMerge.EdgeCasesTests

open ARCtrl
open Main.ArcMerge
open Vitest

Vitest.describe (
    "edge cases",
    fun () ->
        Vitest.test (
            "multiple events are all applied in sequence",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Title <- Some "Disc Title"
                arcRemote.Assays.[0].Title <- Some "Disc Assay Title"

                let events = [
                    {
                        EventName = EventName.Change
                        Path = "isa.investigation.xlsx"
                    }
                    {
                        EventName = EventName.Change
                        Path = "assays/My Assay/isa.assay.xlsx"
                    }
                ]

                let merged = ARC.merge arcLocal arcRemote events

                Vitest.expect(merged.Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Assay Title")
        )

        Vitest.test (
            "unknown path in event list is silently skipped",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"

                let event = {
                    EventName = EventName.Change
                    Path = "some/random/file.txt"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.Title).toEqual (Some "User Title")
        )

        Vitest.test (
            "add/change event for entity not present in arcRemote is a no-op",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"

                let event = {
                    EventName = EventName.Change
                    Path = "assays/Ghost Assay/isa.assay.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.AssayCount).toBe (1)
                Vitest.expect(merged.ContainsAssay("Ghost Assay")).toBe (false)
        )

        Vitest.test (
            "unlink event for entity not present in arcLocal is a no-op",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"

                let event = {
                    EventName = EventName.Unlink
                    Path = "assays/Ghost Assay/isa.assay.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.AssayCount).toBe (1)
        )

        Vitest.test (
            "datamap event for entity not in local ARC is a no-op",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"

                let event = {
                    EventName = EventName.Change
                    Path = "assays/Ghost Assay/isa.datamap.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.AssayCount).toBe (1)
        )

        Vitest.test (
            "datamap change then entity change for same assay keeps disc DataMap",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Assays.[0].Datamap <- None
                arcRemote.Assays.[0].Title <- Some "Disc Assay Title"

                let events = [
                    {
                        EventName = EventName.Change
                        Path = "assays/My Assay/isa.datamap.xlsx"
                    }
                    {
                        EventName = EventName.Change
                        Path = "assays/My Assay/isa.assay.xlsx"
                    }
                ]

                let merged = ARC.merge arcLocal arcRemote events

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Assay Title")
                Vitest.expect(merged.Assays.[0].Datamap.IsNone).toBe (true)
                Vitest.expect(arcLocal.Assays.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "entity change then datamap change for same assay keeps disc DataMap",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Assays.[0].Datamap <- None
                arcRemote.Assays.[0].Title <- Some "Disc Assay Title"

                let events = [
                    {
                        EventName = EventName.Change
                        Path = "assays/My Assay/isa.assay.xlsx"
                    }
                    {
                        EventName = EventName.Change
                        Path = "assays/My Assay/isa.datamap.xlsx"
                    }
                ]

                let merged = ARC.merge arcLocal arcRemote events

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Assay Title")
                Vitest.expect(merged.Assays.[0].Datamap.IsNone).toBe (true)
                Vitest.expect(arcLocal.Assays.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "entity change updates merged copy without mutating arcLocal",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Title <- Some "User Assay Title"
                arcRemote.Assays.[0].Title <- Some "Disc Assay Title"

                let event = {
                    EventName = EventName.Change
                    Path = "assays/My Assay/isa.assay.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Assay Title")
                Vitest.expect(arcLocal.Assays.[0].Title).toEqual (Some "User Assay Title")
        )

        Vitest.test (
            "DataMap Add before entity Add for a new assay: disc DataMap is present",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.InitAssay("New Assay") |> ignore
                arcRemote.Assays.[1].Datamap <- Some(Datamap.init ())

                let events = [
                    {
                        EventName = EventName.Add
                        Path = "assays/New Assay/isa.datamap.xlsx"
                    }
                    {
                        EventName = EventName.Add
                        Path = "assays/New Assay/isa.assay.xlsx"
                    }
                ]

                let merged = ARC.merge arcLocal arcRemote events

                Vitest.expect(merged.ContainsAssay("New Assay")).toBe (true)
                Vitest.expect(merged.Assays.[1].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "duplicate Change events for the same assay are idempotent",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Assays.[0].Title <- Some "Disc Title"

                let event = {
                    EventName = EventName.Change
                    Path = "assays/My Assay/isa.assay.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event; event ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.AssayCount).toBe (1)
        )
)
