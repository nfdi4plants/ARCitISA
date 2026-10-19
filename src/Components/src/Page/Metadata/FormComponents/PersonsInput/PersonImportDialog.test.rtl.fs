module internal Swate.Components.Page.Metadata.FormComponents.PersonsInput.ImportRtlTests

open ARCtrl
open Browser.Types
open Fable.Core
open Vitest

module private Helpers =
    [<Emit("(() => { let resolve, reject; const promise = new Promise((r, j) => { resolve = r; reject = j; }); return {promise, resolve, reject}; })()")>]
    let deferred<'T>
        ()
        : {|
              promise: JS.Promise<'T>
              resolve: 'T -> unit
              reject: exn -> unit
          |}
        =
        jsNative

    let button name =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))

    let click name = RTL.fireEvent.click (button name)

    let checkbox name =
        RTL.screen.getByRole ("checkbox", ByRoleOptions(name = Text $"Import {name}"))

    let candidate name =
        RTL.screen.findByRole ("checkbox", ByRoleOptions(name = Text $"Import {name}"))

    let closed () =
        RTL.waitFor (fun () -> Vitest.expect(RTL.screen.queryByRole("dialog").IsNone).toBe true)

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "more info shows every person field without changing the import selection",
    fun () -> promise {
        let person =
            Person(
                firstName = "Ada",
                lastName = "Lovelace",
                midInitials = "A",
                orcid = "0000-0002-1825-0097",
                address = "12 Example Street\nLondon",
                affiliation = "Research Institute",
                email = "ada@example.org",
                phone = "+44 123",
                fax = "+44 456",
                roles =
                    ResizeArray [
                        OntologyAnnotation(
                            "Investigator",
                            tsr = "CREDIT",
                            tan = "CREDIT:0000001",
                            comments = ResizeArray [ Comment("Role note", "Primary role") ]
                        )
                    ],
                comments =
                    ResizeArray [
                        Comment("Contribution", "Study design")
                        Comment("Note", "Full details")
                    ]
            )

        let mutable saves = 0

        RTL.render (
            PersonsInput.PersonsInput(
                ResizeArray(),
                (fun _ -> saves <- saves + 1),
                onImportPersons = (fun () -> promise { return [| person |] })
            )
        )
        |> ignore

        Helpers.click "Import Persons"
        let! moreInfo = RTL.screen.findByTitle "More info about Ada A Lovelace"
        RTL.fireEvent.click moreInfo
        let! details = RTL.screen.findByRole ("dialog", ByRoleOptions(name = Text "Person details"))

        for text in
            [
                "First name"
                "Last name"
                "Mid initials"
                "ORCID"
                "Address"
                "Affiliation"
                "Email"
                "Phone"
                "Fax"
                "Roles"
                "Comments"
                "Ada"
                "Lovelace"
                "0000-0002-1825-0097"
                "12 Example Street\nLondon"
                "Research Institute"
                "ada@example.org"
                "+44 123"
                "+44 456"
                "Investigator"
                "CREDIT"
                "CREDIT:0000001"
                "Role note"
                "Primary role"
                "Contribution"
                "Study design"
                "Note"
                "Full details"
            ] do
            Vitest.expect(details.textContent).toContain text

        Vitest.expect((Helpers.checkbox "Ada A Lovelace" :?> HTMLInputElement).``checked``).toBe false
        Vitest.expect((Helpers.button "Import selected" :?> HTMLButtonElement).disabled).toBe true
        RTL.fireEvent.click (details.querySelector "button" :?> HTMLElement)

        do!
            RTL.waitFor (fun () ->
                Vitest
                    .expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Person details")).IsNone)
                    .toBe
                    true
            )

        RTL.screen.getByRole ("dialog", ByRoleOptions(name = Text "Import persons"))
        |> ignore

        Vitest.expect(saves).toBe 0
    }
)

Vitest.test (
    "already-added persons still have details and empty fields show a placeholder",
    fun () -> promise {
        let person = Person(firstName = "Existing")

        RTL.render (
            PersonsInput.PersonsInput(
                ResizeArray [ person ],
                ignore,
                onImportPersons = (fun () -> promise { return [| person.Copy() |] })
            )
        )
        |> ignore

        Helpers.click "Import Persons"
        let! moreInfo = RTL.screen.findByTitle "More info about Existing"
        Vitest.expect((Helpers.checkbox "Existing" :?> HTMLInputElement).disabled).toBe true
        Vitest.expect((moreInfo :?> HTMLButtonElement).disabled).toBe false
        RTL.fireEvent.click moreInfo
        let! details = RTL.screen.findByRole ("dialog", ByRoleOptions(name = Text "Person details"))
        Vitest.expect(details.textContent).toContain "Existing"
        Vitest.expect(details.textContent).toContain "—"
        Vitest.expect(details.textContent).toContain "Roles"
        Vitest.expect(details.textContent).toContain "Comments"
        Vitest.expect(details.textContent.Contains "undefined").toBe false
    }
)

Vitest.test (
    "import is hidden without a callback",
    fun () ->
        RTL.render (PersonsInput.PersonsInput(ResizeArray(), ignore)) |> ignore
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Import Persons")).IsNone).toBe true
)

Vitest.test (
    "opening fetches persons, selection excludes existing persons and imports independent copies",
    fun () -> promise {
        let pending = Helpers.deferred<Person[]> ()
        let existing = Person(firstName = "Existing")

        let candidate =
            Person(firstName = "Ada", lastName = "Lovelace", roles = ResizeArray [ OntologyAnnotation("Investigator") ])

        let original = ResizeArray [ existing ]
        let mutable saved = ResizeArray<Person>()
        let mutable requests = 0

        RTL.render (
            PersonsInput.PersonsInput(
                original,
                (fun persons -> saved <- persons),
                onImportPersons =
                    (fun () ->
                        requests <- requests + 1
                        pending.promise
                    )
            )
        )
        |> ignore

        Vitest.expect(requests).toBe 0
        Helpers.click "Import Persons"

        RTL.screen.getByRole ("dialog", ByRoleOptions(name = Text "Import persons"))
        |> ignore

        RTL.screen.getByText "Loading persons…" |> ignore
        Vitest.expect(requests).toBe 1
        Vitest.expect((Helpers.button "Import selected" :?> HTMLButtonElement).disabled).toBe true

        do!
            RTL.act (fun () -> promise {
                pending.resolve [|
                    existing.Copy()
                    candidate
                    candidate.Copy()
                    Person(firstName = "Unselected")
                |]
            })

        let! _ = Helpers.candidate "Ada Lovelace"
        Vitest.expect((Helpers.checkbox "Existing" :?> HTMLInputElement).disabled).toBe true

        Vitest.expect(RTL.screen.getAllByRole("checkbox", ByRoleOptions(name = Text "Import Ada Lovelace")).Count).toBe
            1

        Vitest.expect(saved.Count).toBe 0
        RTL.fireEvent.click (Helpers.checkbox "Ada Lovelace")
        Helpers.click "Import selected"
        do! Helpers.closed ()

        Vitest.expect(saved.Count).toBe 2
        Vitest.expect(saved.[0].FirstName).toEqual (Some "Existing")
        Vitest.expect(saved.[1].LastName).toEqual (Some "Lovelace")
        Vitest.expect(original.Count).toBe 1
        saved.[1].FirstName <- Some "Edited"
        saved.[1].Roles.[0].Name <- Some "Changed role"
        Vitest.expect(candidate.FirstName).toEqual (Some "Ada")
        Vitest.expect(candidate.Roles.[0].NameText).toBe "Investigator"
    }
)

Vitest.test (
    "request errors can be retried and an empty result cannot be imported",
    fun () -> promise {
        let mutable requests = 0
        let mutable saves = 0

        RTL.render (
            PersonsInput.PersonsInput(
                ResizeArray(),
                (fun _ -> saves <- saves + 1),
                onImportPersons =
                    (fun () -> promise {
                        requests <- requests + 1

                        if requests = 1 then
                            return raise (exn "ARC request failed")
                        else
                            return [||]
                    })
            )
        )
        |> ignore

        Helpers.click "Import Persons"
        let! alert = RTL.screen.findByRole "alert"
        Vitest.expect(alert.textContent).toBe "ARC request failed"
        Helpers.click "Retry"
        let! _ = RTL.screen.findByText "No persons available to import."
        Vitest.expect(requests).toBe 2
        Vitest.expect((Helpers.button "Import selected" :?> HTMLButtonElement).disabled).toBe true
        Helpers.click "Cancel"
        do! Helpers.closed ()
        Vitest.expect(saves).toBe 0
    }
)

Vitest.test (
    "canceling a pending request discards its results and reopening fetches current persons",
    fun () -> promise {
        let stale = Helpers.deferred<Person[]> ()
        let fresh = Helpers.deferred<Person[]> ()
        let mutable requests = 0
        let mutable saves = 0

        RTL.render (
            PersonsInput.PersonsInput(
                ResizeArray(),
                (fun _ -> saves <- saves + 1),
                onImportPersons =
                    (fun () ->
                        requests <- requests + 1
                        if requests = 1 then stale.promise else fresh.promise
                    )
            )
        )
        |> ignore

        Helpers.click "Import Persons"
        Helpers.click "Cancel"
        do! Helpers.closed ()
        Helpers.click "Import Persons"
        do! RTL.act (fun () -> promise { fresh.resolve [| Person(firstName = "Current") |] })
        let! _ = Helpers.candidate "Current"
        do! RTL.act (fun () -> promise { stale.resolve [| Person(firstName = "Stale") |] })
        Vitest.expect(RTL.screen.queryByText("Stale").IsNone).toBe true
        RTL.fireEvent.click (Helpers.checkbox "Current")
        Helpers.click "Cancel"
        do! Helpers.closed ()
        Vitest.expect(requests).toBe 2
        Vitest.expect(saves).toBe 0
    }
)
