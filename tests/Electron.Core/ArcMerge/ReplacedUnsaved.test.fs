module ElectronCore.ArcMerge.ReplacedUnsavedTests

open ARCtrl
open Main.ArcMerge
open Vitest

let private assayEvent name = {
    EventName = name
    Path = "assays/My Assay/isa.assay.xlsx"
}

let private datamapEvent name = {
    EventName = name
    Path = "assays/My Assay/isa.datamap.xlsx"
}

let private investigationEvent name = {
    EventName = name
    Path = "isa.investigation.xlsx"
}

let private replaced local remote events =
    ArcMergeHelper.replacedUnsavedFiles local remote events

Vitest.describe (
    "replacedUnsavedFiles",
    fun () ->
        Vitest.test (
            "names an assay with unsaved edits that the disk version replaces",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Title <- Some "Local edit"
                arcRemote.Assays.[0].Title <- Some "Disk title"

                Vitest.expect(replaced arcLocal arcRemote [ assayEvent EventName.Change ]).toEqual [|
                    "assays/My Assay/isa.assay.xlsx"
                |]
        )

        Vitest.test (
            "names nothing when the assay has no unsaved edits",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcRemote.Assays.[0].Title <- Some "Disk title"

                Vitest.expect(replaced arcLocal arcRemote [ assayEvent EventName.Change ]).toEqual [||]
        )

        Vitest.test (
            "names nothing when the touched file has the content the edits are based on",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Title <- Some "Local edit"

                Vitest.expect(replaced arcLocal arcRemote [ assayEvent EventName.Change ]).toEqual [||]
        )

        Vitest.test (
            "names an assay with unsaved edits when its file is removed",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Title <- Some "Local edit"

                Vitest.expect(replaced arcLocal arcRemote [ assayEvent EventName.Unlink ]).toEqual [|
                    "assays/My Assay/isa.assay.xlsx"
                |]
        )

        Vitest.test (
            "names a datamap with unsaved edits that the disk version replaces",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Assays.[0].Datamap <- None

                Vitest.expect(replaced arcLocal arcRemote [ datamapEvent EventName.Change ]).toEqual [|
                    "assays/My Assay/isa.datamap.xlsx"
                |]
        )

        Vitest.test (
            "names the investigation file when its unsaved edits are replaced",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "Local title"
                arcRemote.Title <- Some "Disk title"

                Vitest.expect(replaced arcLocal arcRemote [ investigationEvent EventName.Change ]).toEqual [|
                    "isa.investigation.xlsx"
                |]
        )
)
