module ElectronCore.ArcMerge.DataMapEventsTests

open ARCtrl
open Main.ArcMerge
open Main.ARCtrlExtensions
open Vitest

let private datamapEvent name folder id = {
    EventName = name
    Path = sprintf "%s/%s/isa.datamap.xlsx" folder id
}

let private assayFileEvent name id = {
    EventName = name
    Path = sprintf "assays/%s/isa.assay.xlsx" id
}

let private withStudy () =
    let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
    let study = ArcStudy("My Study")
    arcLocal.AddStudy(study)
    arcRemote.AddStudy(study.Copy())
    arcLocal.GetUpdateContracts() |> ignore
    arcRemote.GetUpdateContracts() |> ignore
    arcLocal, arcRemote

let private withRun () =
    let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
    let run = ArcRun("My Run")
    arcLocal.AddRun(run)
    arcRemote.AddRun(run.Copy())
    arcLocal.GetUpdateContracts() |> ignore
    arcRemote.GetUpdateContracts() |> ignore
    arcLocal, arcRemote

let private withWorkflow () =
    let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
    let workflow = ArcWorkflow("My Workflow")
    arcLocal.AddWorkflow(workflow)
    arcRemote.AddWorkflow(workflow.Copy())
    arcLocal.GetUpdateContracts() |> ignore
    arcRemote.GetUpdateContracts() |> ignore
    arcLocal, arcRemote

Vitest.describe (
    "datamap events",
    fun () ->
        Vitest.test (
            "add event on assay datamap: disc DataMap replaces local DataMap",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                let dataMap = Datamap.init ()
                dataMap.DataContexts.Add(DataContext(Label = Some "Disc label"))
                arcRemote.Assays.[0].Datamap <- Some dataMap

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Add "assays" "My Assay" ]

                Vitest.expect(merged.Assays.[0].Datamap.IsSome).toBe (true)
                Vitest.expect(merged.Assays.[0].Datamap.Value.DataContexts.[0].Label).toEqual (Some "Disc label")
        )

        Vitest.test (
            "change event on assay datamap: disc DataMap replaces local DataMap",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcLocal.Assays.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Assays.[0].Datamap <- None

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Change "assays" "My Assay" ]

                Vitest.expect(merged.Assays.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "unlink event on assay datamap: local DataMap set to None",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcLocal.Assays.[0].Datamap <- Some(Datamap.init ())

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Unlink "assays" "My Assay" ]

                Vitest.expect(merged.Assays.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "datamap event does not affect other assay fields",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Assays.[0].Title <- Some "User Assay Title"
                arcRemote.Assays.[0].Datamap <- Some(Datamap.init ())

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Change "assays" "My Assay" ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "User Assay Title")
                Vitest.expect(merged.Assays.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "change event on study datamap: disc DataMap replaces local DataMap",
            fun () ->
                let arcLocal, arcRemote = withStudy ()
                arcLocal.Studies.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Studies.[0].Datamap <- None

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Change "studies" "My Study" ]

                Vitest.expect(merged.Studies.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "unlink event on study datamap: local DataMap set to None",
            fun () ->
                let arcLocal, _ = withStudy ()
                arcLocal.Studies.[0].Datamap <- Some(Datamap.init ())

                let merged =
                    ARC.merge arcLocal (arcLocal.Copy()) [ datamapEvent EventName.Unlink "studies" "My Study" ]

                Vitest.expect(merged.Studies.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "add event on run datamap: disc DataMap replaces local DataMap",
            fun () ->
                let arcLocal, arcRemote = withRun ()
                arcRemote.Runs.[0].Datamap <- Some(Datamap.init ())

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Add "runs" "My Run" ]

                Vitest.expect(merged.Runs.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "unlink event on run datamap: local DataMap set to None",
            fun () ->
                let arcLocal, _ = withRun ()
                arcLocal.Runs.[0].Datamap <- Some(Datamap.init ())

                let merged =
                    ARC.merge arcLocal (arcLocal.Copy()) [ datamapEvent EventName.Unlink "runs" "My Run" ]

                Vitest.expect(merged.Runs.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "change event on workflow datamap: disc DataMap replaces local DataMap",
            fun () ->
                let arcLocal, arcRemote = withWorkflow ()
                arcLocal.Workflows.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Workflows.[0].Datamap <- None

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Change "workflows" "My Workflow" ]

                Vitest.expect(merged.Workflows.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "unlink event on workflow datamap: local DataMap set to None",
            fun () ->
                let arcLocal, _ = withWorkflow ()
                arcLocal.Workflows.[0].Datamap <- Some(Datamap.init ())

                let merged =
                    ARC.merge arcLocal (arcLocal.Copy()) [ datamapEvent EventName.Unlink "workflows" "My Workflow" ]

                Vitest.expect(merged.Workflows.[0].Datamap.IsNone).toBe (true)
        )

        Vitest.test (
            "change event on assay datamap: a touched file with the content of the local baseline keeps the local DataMap",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                let remoteDataMap = Datamap.init ()
                remoteDataMap.DataContexts.Add(DataContext(Label = Some "Base label"))
                arcRemote.Assays.[0].Datamap <- Some remoteDataMap
                let localDataMap = Datamap.init ()
                localDataMap.DataContexts.Add(DataContext(Label = Some "Base label"))
                localDataMap.StaticHash <- cleanDataMapStaticHash remoteDataMap
                arcLocal.Assays.[0].Datamap <- Some localDataMap
                arcLocal.Assays.[0].Datamap.Value.DataContexts.[0].Label <- Some "Local label"

                let merged =
                    ARC.merge arcLocal arcRemote [ datamapEvent EventName.Change "assays" "My Assay" ]

                Vitest.expect(merged.Assays.[0].Datamap.Value.DataContexts.[0].Label).toEqual (Some "Local label")
        )

        Vitest.test (
            "both files of an assay changed with an identical DataMap keeps the DataMap edits",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                let remoteDataMap = Datamap.init ()
                remoteDataMap.DataContexts.Add(DataContext(Label = Some "Base label"))
                arcRemote.Assays.[0].Datamap <- Some remoteDataMap
                arcRemote.Assays.[0].Title <- Some "Disc Title"
                let localDataMap = Datamap.init ()
                localDataMap.DataContexts.Add(DataContext(Label = Some "Base label"))
                localDataMap.StaticHash <- cleanDataMapStaticHash remoteDataMap
                arcLocal.Assays.[0].Datamap <- Some localDataMap
                arcLocal.Assays.[0].Datamap.Value.DataContexts.[0].Label <- Some "Local label"

                let merged =
                    ARC.merge arcLocal arcRemote [
                        assayFileEvent EventName.Change "My Assay"
                        datamapEvent EventName.Change "assays" "My Assay"
                    ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.Assays.[0].Datamap.Value.DataContexts.[0].Label).toEqual (Some "Local label")
        )

        Vitest.test (
            "both files of an assay changed with a changed DataMap gives the disc DataMap",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                let baseDataMap = Datamap.init ()
                baseDataMap.DataContexts.Add(DataContext(Label = Some "Base label"))
                let remoteDataMap = Datamap.init ()
                remoteDataMap.DataContexts.Add(DataContext(Label = Some "Disc label"))
                arcRemote.Assays.[0].Datamap <- Some remoteDataMap
                arcRemote.Assays.[0].Title <- Some "Disc Title"
                let localDataMap = Datamap.init ()
                localDataMap.DataContexts.Add(DataContext(Label = Some "Base label"))
                localDataMap.StaticHash <- cleanDataMapStaticHash baseDataMap
                arcLocal.Assays.[0].Datamap <- Some localDataMap
                arcLocal.Assays.[0].Datamap.Value.DataContexts.[0].Label <- Some "Local label"

                let merged =
                    ARC.merge arcLocal arcRemote [
                        assayFileEvent EventName.Change "My Assay"
                        datamapEvent EventName.Change "assays" "My Assay"
                    ]

                Vitest.expect(merged.Assays.[0].Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.Assays.[0].Datamap.Value.DataContexts.[0].Label).toEqual (Some "Disc label")
        )
)
