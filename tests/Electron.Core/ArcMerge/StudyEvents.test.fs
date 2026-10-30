module ElectronCore.ArcMerge.StudyEventsTests

open ARCtrl
open Main.ArcMerge
open Vitest

let private studyEvent name id = {
    EventName = name
    Path = sprintf "studies/%s/isa.study.xlsx" id
}

let private withStudy () =
    let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
    let study = ArcStudy("My Study")
    arcLocal.AddStudy(study)
    arcRemote.AddStudy(study.Copy())
    arcLocal.GetUpdateContracts() |> ignore
    arcRemote.GetUpdateContracts() |> ignore
    arcLocal, arcRemote

Vitest.describe (
    "study events",
    fun () ->
        Vitest.test (
            "add event: new disc study appears in merged result",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.AddStudy(ArcStudy("New Study"))

                let merged = ARC.merge arcLocal arcRemote [ studyEvent EventName.Add "New Study" ]

                Vitest.expect(merged.ContainsStudy("New Study")).toBe (true)
        )

        Vitest.test (
            "change event: disc study replaces local study",
            fun () ->
                let arcLocal, arcRemote = withStudy ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Studies.[0].Title <- Some "Disc Study Title"

                let merged = ARC.merge arcLocal arcRemote [ studyEvent EventName.Change "My Study" ]

                Vitest.expect(merged.Studies.[0].Title).toEqual (Some "Disc Study Title")
        )

        Vitest.test (
            "change event: in-memory DataMap is forwarded onto disc study when no datamap event exists",
            fun () ->
                let arcLocal, arcRemote = withStudy ()
                arcLocal.Studies.[0].Datamap <- Some(Datamap.init ())
                arcRemote.Studies.[0].Title <- Some "Disc Study Title"

                let merged = ARC.merge arcLocal arcRemote [ studyEvent EventName.Change "My Study" ]

                Vitest.expect(merged.Studies.[0].Title).toEqual (Some "Disc Study Title")
                Vitest.expect(merged.Studies.[0].Datamap.IsSome).toBe (true)
        )

        Vitest.test (
            "study not targeted by event keeps in-memory state",
            fun () ->
                let arcLocal, arcRemote = withStudy ()
                arcLocal.Studies.[0].Title <- Some "User Study Title"
                arcRemote.Studies.[0].Title <- Some "Disc Study Title"
                arcRemote.Title <- Some "Disc Investigation Title"

                let event = {
                    EventName = EventName.Change
                    Path = "isa.investigation.xlsx"
                }

                let merged = ARC.merge arcLocal arcRemote [ event ]

                Vitest.expect(merged.Studies.[0].Title).toEqual (Some "User Study Title")
        )

        Vitest.test (
            "unlink event: study is removed from local",
            fun () ->
                let arcLocal, arcRemote = withStudy ()
                arcLocal.Title <- Some "User Title"
                arcRemote.RemoveStudy("My Study")

                let merged = ARC.merge arcLocal arcRemote [ studyEvent EventName.Unlink "My Study" ]

                Vitest.expect(merged.ContainsStudy("My Study")).toBe (false)
        )
)
