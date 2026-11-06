module ElectronCore.ArcMerge.InvestigationEventsTests

open ARCtrl
open Main.ArcMerge
open Vitest

let private investigationEvent name = {
    EventName = name
    Path = "isa.investigation.xlsx"
}

Vitest.describe (
    "investigation events",
    fun () ->
        Vitest.test (
            "add event copies all investigation fields from disc",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Title <- Some "Disc Title"
                arcRemote.Description <- Some "Disc Desc"

                let merged = ARC.merge arcLocal arcRemote [ investigationEvent EventName.Add ]

                Vitest.expect(merged.Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.Description).toEqual (Some "Disc Desc")
        )

        Vitest.test (
            "change event copies all investigation fields from disc",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Title <- Some "Disc Title"
                arcRemote.Contacts <- ResizeArray([ Person(lastName = "Smith") ])

                let merged = ARC.merge arcLocal arcRemote [ investigationEvent EventName.Change ]

                Vitest.expect(merged.Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.Contacts.Count).toBe (1)
        )

        Vitest.test (
            "unlink event is a no-op (cannot delete investigation without destroying ARC)",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.Title <- Some "Disc Title"

                let merged = ARC.merge arcLocal arcRemote [ investigationEvent EventName.Unlink ]

                Vitest.expect(merged.Title).toEqual (Some "User Title")
        )

        Vitest.test (
            "investigation change event does not pull in new disc entities",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"
                arcRemote.InitAssay("Extra Disc Assay") |> ignore

                let merged = ARC.merge arcLocal arcRemote [ investigationEvent EventName.Change ]

                Vitest.expect(merged.AssayCount).toBe (1)
                Vitest.expect(merged.ContainsAssay("Extra Disc Assay")).toBe (false)
        )

        Vitest.test (
            "change event: a touched file with the content of the local baseline keeps the local edits",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.Title <- Some "User Title"

                let merged = ARC.merge arcLocal arcRemote [ investigationEvent EventName.Change ]

                Vitest.expect(merged.Title).toEqual (Some "User Title")
        )

        Vitest.test (
            "license event replaces the license and keeps the local investigation edits",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.SetLicenseFulltext("old license")
                arcRemote.SetLicenseFulltext("new license")
                arcLocal.Title <- Some "User Title"

                let merged =
                    ARC.merge arcLocal arcRemote [
                        {
                            EventName = EventName.Change
                            Path = "LICENSE"
                        }
                    ]

                Vitest.expect(merged.License.Value.Content).toBe "new license"
                Vitest.expect(merged.Title).toEqual (Some "User Title")
        )

        Vitest.test (
            "an investigation event does not replace the license",
            fun () ->
                let arcLocal, arcRemote = MockData.createTwoCleanCopies ()
                arcLocal.SetLicenseFulltext("old license")
                arcRemote.SetLicenseFulltext("new license")
                arcLocal.Title <- Some "User Title"
                arcRemote.Title <- Some "Disc Title"

                let merged = ARC.merge arcLocal arcRemote [ investigationEvent EventName.Change ]

                Vitest.expect(merged.Title).toEqual (Some "Disc Title")
                Vitest.expect(merged.License.Value.Content).toBe "old license"
        )
)
