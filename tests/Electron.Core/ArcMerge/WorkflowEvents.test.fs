module ElectronCore.ArcMerge.WorkflowEventsTests

open ARCtrl
open Main.ArcMerge
open Vitest

let private workflowEvent name id = {
    EventName = name
    Path = sprintf "workflows/%s/isa.workflow.xlsx" id
}

let private withWorkflow () =
    let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
    let workflow = ArcWorkflow("My Workflow")
    arcLocal.AddWorkflow(workflow)
    arcRemote.AddWorkflow(workflow.Copy())
    arcLocal.GetUpdateContracts() |> ignore
    arcRemote.GetUpdateContracts() |> ignore
    arcLocal, arcRemote

Vitest.describe (
    "workflow events",
    fun () ->
        Vitest.test (
            "add event: new disc workflow appears in merged result",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.AddWorkflow(ArcWorkflow("New Workflow"))

                let merged =
                    ARC.merge arcLocal arcRemote [ workflowEvent EventName.Add "New Workflow" ]

                Vitest.expect(merged.ContainsWorkflow("New Workflow")).toBe (true)
        )

        Vitest.test (
            "change event: disc workflow replaces local workflow",
            fun () ->
                let arcLocal, arcRemote = withWorkflow ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Workflows.[0].Title <- Some "Disc Workflow Title"

                let merged =
                    ARC.merge arcLocal arcRemote [ workflowEvent EventName.Change "My Workflow" ]

                Vitest.expect(merged.Workflows.[0].Title).toEqual (Some "Disc Workflow Title")
        )

        Vitest.test (
            "change event: in-memory DataMap is forwarded onto disc workflow when no datamap event exists",
            fun () ->
                let arcLocal, arcRemote = withWorkflow ()
                arcLocal.Workflows.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Workflows.[0].Title <- Some "Disc Workflow Title"

                let merged =
                    ARC.merge arcLocal arcRemote [ workflowEvent EventName.Change "My Workflow" ]

                Vitest.expect(merged.Workflows.[0].Title).toEqual (Some "Disc Workflow Title")
                Vitest.expect(merged.Workflows.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "workflow not targeted by event keeps in-memory state",
            fun () ->
                let arcLocal, arcRemote = withWorkflow ()
                arcLocal.Workflows.[0].Title <- Some "User Workflow Title"
                arcRemote.Workflows.[0].Title <- Some "Disc Workflow Title"
                arcRemote.Title <- Some "Disc Investigation Title"

                let event = {
                    EventName = EventName.Change
                    Path = "isa.investigation.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.Workflows.[0].Title).toEqual (Some "User Workflow Title")
        )

        Vitest.test (
            "unlink event: workflow is removed from local",
            fun () ->
                let arcLocal, arcRemote = withWorkflow ()
                arcLocal.Title <- Some "User Title"
                arcRemote.DeleteWorkflow("My Workflow")

                let merged =
                    ARC.merge arcLocal arcRemote [ workflowEvent EventName.Unlink "My Workflow" ]

                Vitest.expect(merged.ContainsWorkflow("My Workflow")).toBe (false)
        )
)
