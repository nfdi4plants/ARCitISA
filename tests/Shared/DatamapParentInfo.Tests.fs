module Swate.Components.Shared.DatamapParentInfoTests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open Swate.Components.Shared
open ARCtrl

let tests =
    testList "DatamapParentInfo" [
        testCase "creates parent info from supported folder paths"
        <| fun _ ->
            [
                "assays/Assay A", DataMapParent.Assay, "Assay A"
                "studies/Study A", DataMapParent.Study, "Study A"
                "runs/Run A", DataMapParent.Run, "Run A"
                "workflows/Workflow A", DataMapParent.Workflow, "Workflow A"
            ]
            |> List.iter (fun (path, expectedParent, expectedId) ->
                let actual = DatamapParentInfo.tryFromFolderPath path

                Expect.equal
                    actual
                    (Some(DatamapParentInfo.create expectedId expectedParent))
                    $"Expected {path} to identify its DataMap parent"
            )

        testCase "rejects paths that are not DataMap parent folders"
        <| fun _ ->
            [
                "assays"
                "assays/Assay A/isa.datamap.xlsx"
                "investigation"
                "notes/Note A"
            ]
            |> List.iter (fun path ->
                Expect.isNone
                    (DatamapParentInfo.tryFromFolderPath path)
                    $"Expected {path} not to identify a DataMap parent"
            )

        testCase "gets, sets, and removes DataMaps for every supported parent type"
        <| fun _ ->
            let cases = [
                DataMapParent.Assay, (fun (arc: ARC) -> arc.AddAssay(ArcAssay("parent")))
                DataMapParent.Study, (fun arc -> arc.AddStudy(ArcStudy("parent")))
                DataMapParent.Run, (fun arc -> arc.AddRun(ArcRun("parent")))
                DataMapParent.Workflow, (fun arc -> arc.AddWorkflow(ArcWorkflow("parent")))
            ]

            cases
            |> List.iter (fun (parentType, addParent) ->
                let arc = ARC("test-arc")
                let parentInfo = DatamapParentInfo.create "parent" parentType
                let dataMap = Datamap.init ()

                Expect.isFalse
                    (arc.TrySetDataMap(parentInfo, Some dataMap))
                    $"Expected setting a DataMap on a missing {parentType} to fail"

                addParent arc
                Expect.isNone (arc.TryGetDataMap parentInfo) $"Expected the {parentType} to start without a DataMap"

                Expect.isTrue
                    (arc.TrySetDataMap(parentInfo, Some dataMap))
                    $"Expected setting the {parentType} DataMap"

                Expect.isSome (arc.TryGetDataMap parentInfo) $"Expected to retrieve the {parentType} DataMap"
                Expect.isTrue (arc.TrySetDataMap(parentInfo, None)) $"Expected removing the {parentType} DataMap"
                Expect.isNone (arc.TryGetDataMap parentInfo) $"Expected the {parentType} DataMap to be removed"
            )
    ]
