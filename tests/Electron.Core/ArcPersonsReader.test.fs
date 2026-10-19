module ElectronCore.ArcPersonsReaderTests

open ARCtrl
open ARCtrl.Json
open Fable.Core
open Main
open Vitest

[<Emit("structuredClone($0)")>]
let private structuredClone<'T> (value: 'T) : 'T = jsNative

let private person name = Person(firstName = name)

let private read arc =
    ArcPersonsReader.serialize arc |> Array.map Person.fromJsonString

Vitest.describe (
    "ArcPersonsReader",
    fun () ->
        Vitest.test (
            "includes investigation, study, assay, run and workflow persons",
            fun () ->
                let arc =
                    ARC(
                        "arc",
                        contacts = ResizeArray [ person "Investigation" ],
                        studies =
                            ResizeArray [
                                ArcStudy("study", contacts = ResizeArray [ person "Study" ])
                            ],
                        assays =
                            ResizeArray [
                                ArcAssay("assay", performers = ResizeArray [ person "Assay" ])
                            ],
                        runs = ResizeArray [ ArcRun("run", performers = ResizeArray [ person "Run" ]) ],
                        workflows =
                            ResizeArray [
                                ArcWorkflow("workflow", contacts = ResizeArray [ person "Workflow" ])
                            ]
                    )

                let names = read arc |> Array.choose _.FirstName |> Array.sort

                Vitest.expect(names).toEqual ([| "Assay"; "Investigation"; "Run"; "Study"; "Workflow" |])
        )

        Vitest.test (
            "returns no persons for metadata without contacts or performers",
            fun () -> Vitest.expect(ArcPersonsReader.serialize (ARC("empty"))).toEqual ([||]: string[])
        )

        Vitest.test (
            "deduplicates full person copies across entities while retaining differing details",
            fun () ->
                let shared =
                    Person(firstName = "Ada", lastName = "Lovelace", email = "ada@example.org")

                let differentDetails = shared.Copy()
                differentDetails.EMail <- Some "another-address@example.org"

                let arc =
                    ARC(
                        "arc",
                        contacts = ResizeArray [ shared ],
                        studies =
                            ResizeArray [
                                ArcStudy("study", contacts = ResizeArray [ shared.Copy() ])
                            ],
                        assays =
                            ResizeArray [
                                ArcAssay("assay", performers = ResizeArray [ shared.Copy() ])
                            ],
                        runs =
                            ResizeArray [
                                ArcRun("run", performers = ResizeArray [ shared.Copy() ])
                            ],
                        workflows =
                            ResizeArray [
                                ArcWorkflow("workflow", contacts = ResizeArray [ shared.Copy(); differentDetails ])
                            ]
                    )

                let emails = read arc |> Array.choose _.EMail |> Array.sort

                Vitest.expect(emails).toEqual ([| "ada@example.org"; "another-address@example.org" |])
        )

        Vitest.test (
            "reads additions and edits directly from the current in-memory ARC",
            fun () ->
                let existing = person "Before"
                let run = ArcRun("run")

                let arc =
                    ARC("arc", contacts = ResizeArray [ existing ], runs = ResizeArray [ run ])

                let initial = ArcPersonsReader.serialize arc

                existing.FirstName <- Some "Edited without saving"
                run.Performers.Add(person "Added without saving")

                let names = read arc |> Array.choose _.FirstName |> Array.sort

                Vitest.expect(names).toEqual ([| "Added without saving"; "Edited without saving" |])
                Vitest.expect((initial.[0] |> Person.fromJsonString).FirstName).toEqual (Some "Before")
                Vitest.expect(existing.FirstName).toEqual (Some "Edited without saving")
                Vitest.expect(run.Performers.Count).toBe (1)
        )

        Vitest.test (
            "JSON survives IPC cloning and restores person methods and all metadata details",
            fun () ->
                let role =
                    OntologyAnnotation(
                        name = "investigator",
                        tsr = "CREDIT",
                        tan = "CREDIT:0000001",
                        comments = ResizeArray [ Comment(name = "role note", value = "Primary role") ]
                    )

                let original =
                    Person(
                        firstName = "Ada",
                        midInitials = "A",
                        lastName = "Lovelace",
                        orcid = "0000-0001-2345-6789",
                        email = "ada@example.org",
                        phone = "+49 123",
                        fax = "+49 456",
                        address = "Example street",
                        affiliation = "Example University",
                        roles = ResizeArray [ role ],
                        comments = ResizeArray [ Comment(name = "note", value = "Keep this detail") ]
                    )

                let arc = ARC("arc", contacts = ResizeArray [ original ])

                let restored =
                    ArcPersonsReader.serialize arc
                    |> structuredClone
                    |> Array.exactlyOne
                    |> Person.fromJsonString

                Vitest.expect(Person.toJsonString () restored).toBe (Person.toJsonString () original)
                Vitest.expect(restored.Copy().FirstName).toEqual (Some "Ada")
                Vitest.expect(restored.Roles.[0].Copy().TermAccessionNumber).toEqual (Some "CREDIT:0000001")
                Vitest.expect(restored.Roles.[0].Comments.[0].Copy().Value).toEqual (Some "Primary role")
                Vitest.expect(restored.Comments.[0].Copy().Value).toEqual (Some "Keep this detail")
        )
)
