module ElectronCore.ArcMerge.AssayEventsTests

open ARCtrl
open Main.ArcMerge
open Vitest

let private assayEvent name id = {
    EventName = name
    Path = sprintf "assays/%s/isa.assay.xlsx" id
}

Vitest.describe (
    "assay events",
    fun () ->
        Vitest.test (
            "add event: new assay from disc appears in merged result",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                let newAssay = arcRemote.InitAssay("New Assay")
                let dataMap = Datamap.init ()
                dataMap.DataContexts.Add(DataContext(Label = Some "Disc label"))
                newAssay.Datamap <- Some dataMap

                let merged = ARC.merge arcLocal arcRemote [ assayEvent EventName.Add "New Assay" ]

                Vitest.expect(merged.ContainsAssay("New Assay")).toBe (true)
                Vitest.expect(merged.AssayCount).toBe (2)

                Vitest
                    .expect(merged.GetAssay("New Assay").Datamap.Value.DataContexts.[0].Label)
                    .toEqual (Some "Disc label")
        )

        Vitest.test (
            "change event: disc assay fully replaces local assay (disc wins)",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Title <- Some "User Title"
                arcRemote.Assays.[0].Title <- Some "Disc Title"

                let merged = ARC.merge arcLocal arcRemote [ assayEvent EventName.Change "My Assay" ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Title")
        )

        Vitest.test (
            "change event: in-memory DataMap is forwarded onto the disc entity",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                let dataMap = Datamap.init ()
                dataMap.DataContexts.Add(DataContext(Label = Some "Local label"))
                arcLocal.Assays.[0].Datamap <- Some dataMap
                arcRemote.Assays.[0].Title <- Some "Disc Title"

                let merged = ARC.merge arcLocal arcRemote [ assayEvent EventName.Change "My Assay" ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.Assays.[0].Datamap.IsSome).toBe (true)
                Vitest.expect(merged.Assays.[0].Datamap.Value.DataContexts.[0].Label).toEqual (Some "Local label")
        )

        Vitest.test (
            "unlink event: assay is removed from local",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.RemoveAssay("My Assay")

                let merged = ARC.merge arcLocal arcRemote [ assayEvent EventName.Unlink "My Assay" ]

                Vitest.expect(merged.ContainsAssay("My Assay")).toBe (false)
                Vitest.expect(merged.AssayCount).toBe (0)
        )

        Vitest.test (
            "change event: entity not touched by event keeps its in-memory state",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Assays.[0].Title <- Some "Disc Assay Title"

                let event = {
                    EventName = EventName.Change
                    Path = "isa.investigation.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (None)
        )
)
