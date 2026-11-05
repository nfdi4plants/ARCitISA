module ElectronCore.ArcMerge.RunEventsTests

open ARCtrl
open Main.ArcMerge
open Vitest

let private runEvent name id = {
    EventName = name
    Path = sprintf "runs/%s/isa.run.xlsx" id
}

let private withRun () =
    let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
    let run = ArcRun("My Run")
    arcLocal.AddRun(run)
    arcRemote.AddRun(run.Copy())
    arcLocal.GetUpdateContracts() |> ignore
    arcRemote.GetUpdateContracts() |> ignore
    arcLocal, arcRemote

Vitest.describe (
    "run events",
    fun () ->
        Vitest.test (
            "add event: new disc run appears in merged result",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.AddRun(ArcRun("New Run"))

                let merged = ARC.merge arcLocal arcRemote [ runEvent EventName.Add "New Run" ]

                Vitest.expect(merged.ContainsRun("New Run")).toBe (true)
        )

        Vitest.test (
            "change event: disc run replaces local run",
            fun () ->
                let arcLocal, arcRemote = withRun ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Runs.[0].Title <- Some "Disc Run Title"

                let merged = ARC.merge arcLocal arcRemote [ runEvent EventName.Change "My Run" ]

                Vitest.expect(merged.Runs.[0].Title).toEqual (Some "Disc Run Title")
        )

        Vitest.test (
            "change event: in-memory DataMap is forwarded onto disc run when no datamap event exists",
            fun () ->
                let arcLocal, arcRemote = withRun ()
                arcLocal.Runs.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Runs.[0].Title <- Some "Disc Run Title"

                let merged = ARC.merge arcLocal arcRemote [ runEvent EventName.Change "My Run" ]

                Vitest.expect(merged.Runs.[0].Title).toEqual (Some "Disc Run Title")
                Vitest.expect(merged.Runs.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "run not targeted by event keeps in-memory state",
            fun () ->
                let arcLocal, arcRemote = withRun ()
                arcLocal.Runs.[0].Title <- Some "User Run Title"
                arcRemote.Runs.[0].Title <- Some "Disc Run Title"
                arcRemote.Title <- Some "Disc Investigation Title"

                let event = {
                    EventName = EventName.Change
                    Path = "isa.investigation.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.Runs.[0].Title).toEqual (Some "User Run Title")
        )

        Vitest.test (
            "unlink event: run is removed from local",
            fun () ->
                let arcLocal, arcRemote = withRun ()
                arcLocal.Title <- Some "User Title"
                arcRemote.DeleteRun("My Run")

                let merged = ARC.merge arcLocal arcRemote [ runEvent EventName.Unlink "My Run" ]

                Vitest.expect(merged.ContainsRun("My Run")).toBe (false)
        )
)
