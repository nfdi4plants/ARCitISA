module internal Swate.Components.Page.Metadata.FormComponents.PersonsInput.RtlTests

open ARCtrl
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components.Page.Metadata.FormComponents.PersonsInput.Types

module private Helpers =
    let orcid = "0000-0002-1825-0097"

    [<Emit("new Response(JSON.stringify($0), {status: $1, headers: {'Content-Type': 'application/json'}})")>]
    let response (_body: obj) (_status: int) : Fetch.Types.Response = jsNative

    [<Emit("new Promise(resolve => setTimeout(resolve, $0))")>]
    let delay (_milliseconds: int) : JS.Promise<unit> = jsNative

    [<Emit("(() => { let resolve; const promise = new Promise(r => { resolve = r; }); return {promise, resolve}; })()")>]
    let deferred
        ()
        : {|
              promise: JS.Promise<Fetch.Types.Response>
              resolve: Fetch.Types.Response -> unit
          |}
        =
        jsNative

    [<Emit("new URL($0).searchParams.get($1)")>]
    let queryParameter (_url: string) (_name: string) : string = jsNative

    [<Emit("({credentials: $0.credentials, hasAuthorization: new Headers($0.headers).has('Authorization')})")>]
    let requestDetails
        (_options: obj)
        : {|
              credentials: string
              hasAuthorization: bool
          |}
        =
        jsNative

    [<Emit("new Response(null, {status: 404})")>]
    let notFound () : Fetch.Types.Response = jsNative

    // One body answers both the /person and the /employments request of an ORCID iD lookup.
    let record firstName lastName =
        box {|
            name = {|
                ``given-names`` = {| value = firstName |}
                ``family-name`` = {| value = lastName |}
            |}
            emails = {| email = ([||]: obj[]) |}
            ``affiliation-group`` = ([||]: obj[])
        |}

    let searchResult id first last = {|
        ``orcid-id`` = id
        ``given-names`` = first
        ``family-names`` = last
        ``credit-name`` = (null: string)
        email = ([||]: string[])
        ``institution-name`` = ([||]: string[])
    |}

    let searchPage results count =
        box {|
            ``expanded-result`` = results
            ``num-found`` = count
        |}

    let fixtureOrcid index =
        let digits = (string index).PadLeft(15, '0')

        let total =
            digits |> Seq.fold (fun total digit -> (total + int digit - int '0') * 2) 0

        let remainder = (12 - total % 11) % 11
        let checksum = if remainder = 10 then "X" else string remainder
        let compact = digits + checksum

        String.concat "-" [
            for start in [ 0; 4; 8; 12 ] -> compact.Substring(start, 4)
        ]

    let stubFetch handler =
        Vitest.vi.stubGlobal ("fetch", box handler)

    let button label =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text label))

    let click label = RTL.fireEvent.click (button label)

    let dialog () =
        RTL.screen.getByRole ("dialog", ByRoleOptions(name = Text "Search person"))

    let openSearch () = click "Search by ORCID"

    let changeQuery label value =
        RTL.fireEvent.change (
            RTL.screen.getByRole ("textbox", ByRoleOptions(name = Text label)),
            {| target = {| value = value |} |}
        )

    let searchOrcid query =
        changeQuery "ORCID iD or URL" query
        click "Search"

    let searchName query =
        changeQuery "Person name" query
        click "Search"

    let singlePreview (name: string) = promise {
        let! _ = RTL.screen.findByRole ("heading", ByRoleOptions(name = Text "Autofill preview"))
        Vitest.expect(RTL.screen.getAllByText(name).Count).toBe 1
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text $"Preview {name}")).IsNone).toBe true
    }

    let expectClosed () =
        RTL.waitFor (fun () ->
            Vitest.expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Search person")).IsNone).toBe
                true
        )

[<Erase; Mangle(false)>]
type private Harness =
    [<ReactComponent>]
    static member Harness(initial: ResizeArray<Person>, changed: ResizeArray<Person> -> unit) =
        let values, setValues = React.useState initial

        PersonsInput.PersonsInput(
            values,
            (fun next ->
                changed next
                setValues (ResizeArray next)
            ),
            label = "Persons"
        )

let private render (persons: seq<Person>) changed =
    RTL.render (Harness.Harness(ResizeArray persons, changed)) |> ignore

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.unstubAllGlobals ()
)

for query in [ Helpers.orcid; "https://orcid.org/" + Helpers.orcid ] do
    Vitest.test (
        $"ORCID lookup accepts {query} and waits for confirmation",
        fun () -> promise {
            let requests = ResizeArray<string>()
            let mutable calls = 0

            Helpers.stubFetch (fun (url: string) -> promise {
                requests.Add url
                return Helpers.response (Helpers.record "Jane" "Smith") 200
            })

            render [ Person() ] (fun _ -> calls <- calls + 1)
            Helpers.openSearch ()
            Helpers.searchOrcid query
            do! Helpers.singlePreview "Jane Smith"

            Vitest.expect(requests |> Seq.sort |> Array.ofSeq).toEqual [|
                $"https://pub.orcid.org/v3.0/{Helpers.orcid}/employments"
                $"https://pub.orcid.org/v3.0/{Helpers.orcid}/person"
            |]

            Vitest.expect(calls).toBe 0
            Helpers.click "Cancel"
            do! Helpers.expectClosed ()
            Vitest.expect(calls).toBe 0
        }
    )

for label, initial, draft, queryLabel, mode, expected in
    [
        "Search by ORCID",
        "0000-0001-5109-3700",
        "https://orcid.org/0000-0002-1825-0097",
        "ORCID iD or URL",
        "ORCID",
        "https://orcid.org/0000-0002-1825-0097"
        "Search by name", "Stored", "Latest", "Person name", "Name", "Jane Latest"
    ] do
    Vitest.test (
        $"{label} prefills its search mode from the latest field draft before debounce",
        fun () -> promise {
            let mutable requests = 0

            Helpers.stubFetch (fun (_: string) -> promise {
                requests <- requests + 1
                return Helpers.response (Helpers.record "Jane" "Smith") 200
            })

            let person =
                if mode = "ORCID" then
                    Person(orcid = initial)
                else
                    Person(firstName = "Jane", lastName = initial)

            render [ person ] ignore
            let input = RTL.screen.getByDisplayValue initial
            let trigger = Helpers.button label
            RTL.fireEvent.change (input, {| target = {| value = draft |} |})
            RTL.fireEvent.click trigger

            let query =
                RTL.screen.getByRole ("textbox", ByRoleOptions(name = Text queryLabel)) :?> HTMLInputElement

            Vitest.expect(query.value).toBe expected

            RTL.screen.getByRole ("button", ByRoleOptions(name = Text mode, pressed = true))
            |> ignore

            Vitest.expect(requests).toBe 0
        }
    )

Vitest.test (
    "confirmed ORCID autofill copies the person, preserves manual fields roles and comments, and survives pending debounce",
    fun () -> promise {
        let original =
            Person(
                firstName = "Manual",
                lastName = "Existing",
                midInitials = "M",
                email = "keep@example.org",
                affiliation = "Keep institute",
                phone = "123",
                roles = ResizeArray [ OntologyAnnotation("Researcher") ],
                comments = ResizeArray [ Comment("Note", "Keep me") ]
            )

        let mutable calls = 0
        let mutable saved = original
        Helpers.stubFetch (fun (_: string) -> promise { return Helpers.response (Helpers.record "Jane" "Smith") 200 })

        render
            [ original ]
            (fun values ->
                calls <- calls + 1
                saved <- values.[0]
            )

        let firstName = RTL.screen.getByDisplayValue "Manual"
        Helpers.openSearch ()
        Helpers.searchOrcid Helpers.orcid
        do! Helpers.singlePreview "Jane Smith"
        Vitest.expect(calls).toBe 0
        Vitest.expect(original.FirstName).toEqual (Some "Manual")
        let applyButton = Helpers.button "Apply person"

        RTL.fireEvent.change (
            firstName,
            {|
                target = {| value = "Pending manual edit" |}
            |}
        )

        RTL.fireEvent.click applyButton
        do! Helpers.expectClosed ()
        do! RTL.act (fun () -> Helpers.delay 400)
        Vitest.expect(calls).toBe 1
        Vitest.expect(saved.FirstName).toEqual (Some "Jane")
        Vitest.expect(saved.LastName).toEqual (Some "Smith")
        Vitest.expect(saved.ORCID).toEqual (Some Helpers.orcid)
        Vitest.expect(saved.EMail).toEqual original.EMail
        Vitest.expect(saved.Affiliation).toEqual original.Affiliation
        Vitest.expect(saved.MidInitials).toEqual original.MidInitials
        Vitest.expect(saved.Phone).toEqual original.Phone
        Vitest.expect(saved.Roles).toEqual original.Roles
        Vitest.expect(saved.Comments).toEqual original.Comments
        Vitest.expect(original.FirstName).toEqual (Some "Manual")
        RTL.screen.getByDisplayValue "Jane" |> ignore
        RTL.screen.getByDisplayValue "Smith" |> ignore

        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Jane M Smith"))
        |> ignore
    }
)

Vitest.test (
    "a unique name result previews automatically and applies only after confirmation",
    fun () -> promise {
        let mutable requests = 0
        let mutable saved = Person()

        Helpers.stubFetch (fun (_: string) -> promise {
            requests <- requests + 1
            return Helpers.response (Helpers.searchPage [| Helpers.searchResult Helpers.orcid "Jane" "Smith" |] 1) 200
        })

        let mutable calls = 0

        render
            [ Person() ]
            (fun values ->
                calls <- calls + 1
                saved <- values.[0]
            )

        Helpers.click "Search by name"
        Helpers.searchName "Jane Smith"
        do! Helpers.singlePreview "Jane Smith"
        Vitest.expect(calls).toBe 0
        Helpers.click "Apply person"
        do! Helpers.expectClosed ()
        Vitest.expect(calls).toBe 1
        Vitest.expect(requests).toBe 1
        Vitest.expect(saved.FirstName).toEqual (Some "Jane")
        Vitest.expect(saved.LastName).toEqual (Some "Smith")
        Vitest.expect(saved.ORCID).toEqual (Some Helpers.orcid)
    }
)

Vitest.test (
    "name search pages ten results and requires selection when several people match",
    fun () -> promise {
        let requests = ResizeArray<string>()
        let mutable saved = Person()

        Helpers.stubFetch (fun (url: string) -> promise {
            requests.Add url

            let results =
                if Helpers.queryParameter url "start" = "10" then
                    [| Helpers.searchResult Helpers.orcid "Final" "Author" |]
                else
                    Array.init
                        10
                        (fun index -> Helpers.searchResult (Helpers.fixtureOrcid (index + 1)) "Jane" $"Smith {index}")

            return Helpers.response (Helpers.searchPage results 11) 200
        })

        render [ Person() ] (fun values -> saved <- values.[0])
        Helpers.click "Search by name"
        Helpers.searchName "Jane Smith"
        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview Jane Smith 0"))
        Vitest.expect((Helpers.button "Apply person" :?> HTMLButtonElement).disabled).toBe true
        Vitest.expect(RTL.screen.queryByRole("heading", ByRoleOptions(name = Text "Autofill preview")).IsNone).toBe true
        Helpers.click "Load more"
        let! lastResult = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview Final Author"))
        RTL.fireEvent.click lastResult
        Vitest.expect(Helpers.queryParameter requests.[0] "q").toBe "given-and-family-names:(\"Jane\" AND \"Smith\")"
        Vitest.expect(Helpers.queryParameter requests.[0] "rows").toBe "10"
        Vitest.expect(Helpers.queryParameter requests.[0] "start").toBe "0"
        Vitest.expect(Helpers.queryParameter requests.[1] "start").toBe "10"
        Helpers.click "Apply person"
        do! Helpers.expectClosed ()
        Vitest.expect(saved.FirstName).toEqual (Some "Final")
        Vitest.expect(saved.LastName).toEqual (Some "Author")
    }
)

Vitest.test (
    "invalid ORCID does not fetch and Escape dismisses an automatic preview unchanged",
    fun () -> promise {
        let mutable requests = 0
        let mutable calls = 0

        Helpers.stubFetch (fun (_: string) -> promise {
            requests <- requests + 1
            return Helpers.response (Helpers.record "Jane" "Smith") 200
        })

        render [ Person(firstName = "Keep") ] (fun _ -> calls <- calls + 1)
        Helpers.openSearch ()
        Helpers.searchOrcid "not-an-orcid"
        RTL.screen.getByRole "alert" |> ignore
        Vitest.expect(requests).toBe 0
        Helpers.searchOrcid Helpers.orcid
        do! Helpers.singlePreview "Jane Smith"
        RTL.fireEvent.custom ("keyDown", Helpers.dialog (), {| key = "Escape"; code = "Escape" |})
        do! Helpers.expectClosed ()
        Vitest.expect(calls).toBe 0
        RTL.screen.getByDisplayValue "Keep" |> ignore
    }
)

for status, mode in
    [
        for status in [ 401; 403 ] do
            for mode in [ "ORCID"; "Name" ] do
                yield status, mode
    ] do
    Vitest.test (
        $"{mode} search HTTP {status} reports blocked access without authentication or fallback requests",
        fun () -> promise {
            let mutable requests = 0
            let mutable details = None

            Helpers.stubFetch (
                System.Func<string, obj, JS.Promise<Fetch.Types.Response>>(fun _ options -> promise {
                    requests <- requests + 1
                    details <- Some(Helpers.requestDetails options)
                    return Helpers.response (box {| message = "Restricted" |}) status
                })
            )

            let mutable calls = 0
            render [ Person(firstName = "Manual", lastName = "Person") ] (fun _ -> calls <- calls + 1)

            if mode = "ORCID" then
                Helpers.openSearch ()
                Helpers.searchOrcid Helpers.orcid
            else
                Helpers.click "Search by name"
                Helpers.searchName "Jane Smith"

            let! alert = RTL.screen.findByRole "alert"
            Vitest.expect(alert.textContent).toContain "Blocked:"
            Vitest.expect(alert.textContent).toContain "token or login"
            Vitest.expect((Helpers.button "Search" :?> HTMLButtonElement).disabled).toBe true
            Vitest.expect((Helpers.button "Apply person" :?> HTMLButtonElement).disabled).toBe true
            Helpers.click "Search"
            // An ORCID iD lookup loads /person and /employments together.
            Vitest.expect(requests).toBe (if mode = "ORCID" then 2 else 1)
            Vitest.expect(details.Value.credentials).toBe "omit"
            Vitest.expect(details.Value.hasAuthorization).toBe false
            Vitest.expect(calls).toBe 0
        }
    )

Vitest.test (
    "an ORCID record with HTTP 404 and an empty body reports no people found",
    fun () -> promise {
        let mutable calls = 0
        Helpers.stubFetch (fun (_: string) -> promise { return Helpers.notFound () })
        render [ Person() ] (fun _ -> calls <- calls + 1)
        Helpers.openSearch ()
        Helpers.searchOrcid Helpers.orcid
        let! _ = RTL.screen.findByText "No people found."
        Vitest.expect(RTL.screen.queryByRole("alert").IsNone).toBe true
        Vitest.expect((Helpers.button "Apply person" :?> HTMLButtonElement).disabled).toBe true
        Vitest.expect(calls).toBe 0
    }
)

Vitest.test (
    "an ORCID HTTP 503 error leaves search enabled for an explicit successful retry",
    fun () -> promise {
        let mutable lookups = 0

        Helpers.stubFetch (fun (url: string) -> promise {
            if url.EndsWith "/person" then
                lookups <- lookups + 1

            if lookups = 1 then
                return Helpers.response (box {| message = "Unavailable" |}) 503
            else
                return Helpers.response (Helpers.record "Recovered" "Person") 200
        })

        render [ Person() ] ignore
        Helpers.openSearch ()
        Helpers.searchOrcid Helpers.orcid
        let! alert = RTL.screen.findByRole "alert"
        Vitest.expect(alert.textContent.Contains "503").toBe true
        Vitest.expect((Helpers.button "Search" :?> HTMLButtonElement).disabled).toBe false
        Helpers.click "Search"
        do! Helpers.singlePreview "Recovered Person"
        Vitest.expect(lookups).toBe 2
        Vitest.expect(RTL.screen.queryByRole("alert").IsNone).toBe true
    }
)

Vitest.test (
    "changing the ORCID query ignores an older request and does not search until submitted",
    fun () -> promise {
        let pending = Helpers.deferred ()
        let mutable requests = 0

        // Only the stale /person waits, so each request reads its own response body.
        Helpers.stubFetch (fun (url: string) ->
            requests <- requests + 1

            if url.EndsWith $"{Helpers.orcid}/person" then
                pending.promise
            elif url.Contains Helpers.orcid then
                promise { return Helpers.response (Helpers.record "Stale" "Person") 200 }
            else
                promise { return Helpers.response (Helpers.record "Current" "Person") 200 }
        )

        render [ Person() ] ignore
        Helpers.openSearch ()
        Helpers.searchOrcid Helpers.orcid
        Helpers.changeQuery "ORCID iD or URL" "0000-0001-5109-3700"
        Vitest.expect(requests).toBe 2
        Helpers.click "Search"
        do! Helpers.singlePreview "Current Person"
        do! RTL.act (fun () -> promise { pending.resolve (Helpers.response (Helpers.record "Stale" "Person") 200) })
        Vitest.expect(RTL.screen.queryByText("Stale Person").IsNone).toBe true
        Vitest.expect(RTL.screen.queryByRole("alert").IsNone).toBe true
        RTL.screen.getByText "Current Person" |> ignore
    }
)

Vitest.test (
    "switching modes invalidates a pending response and reopening after cancel shows a fresh dialog",
    fun () -> promise {
        let pending = Helpers.deferred ()

        // Only /person waits, so each request reads its own response body.
        Helpers.stubFetch (fun (url: string) ->
            if url.EndsWith "/person" then
                pending.promise
            else
                promise { return Helpers.response (Helpers.record "Stale" "Person") 200 }
        )

        let mutable calls = 0
        render [ Person() ] (fun _ -> calls <- calls + 1)
        Helpers.openSearch ()
        Helpers.searchOrcid Helpers.orcid
        Helpers.click "Name"
        do! RTL.act (fun () -> promise { pending.resolve (Helpers.response (Helpers.record "Stale" "Person") 200) })
        Vitest.expect(RTL.screen.queryByText("Stale Person").IsNone).toBe true
        Helpers.click "Cancel"
        do! Helpers.expectClosed ()
        // This lookup never answers. The dismiss guard itself is covered by the direct-mount test.
        let closed = Helpers.deferred ()
        Helpers.stubFetch (fun (_: string) -> closed.promise)
        Helpers.openSearch ()
        Helpers.searchOrcid Helpers.orcid
        Helpers.click "Cancel"
        do! Helpers.expectClosed ()
        Helpers.openSearch ()
        Vitest.expect(RTL.screen.queryByText("Searching people…").IsNone).toBe true
        Vitest.expect(calls).toBe 0
    }
)

Vitest.test (
    "the import dialog appends selected persons only after confirmation",
    fun () -> promise {
        let mutable saved = ResizeArray<Person>()

        RTL.render (
            PersonsInput.PersonsInput(
                ResizeArray [ Person(firstName = "Existing") ],
                (fun values -> saved <- values),
                onImportPersons = (fun () -> promise { return [| Person(firstName = "Imported") |] })
            )
        )
        |> ignore

        Helpers.click "Import Persons"
        let! candidate = RTL.screen.findByRole ("checkbox", ByRoleOptions(name = Text "Import Imported"))
        Vitest.expect(saved.Count).toBe 0
        RTL.fireEvent.click candidate
        Helpers.click "Import selected"
        Vitest.expect(saved.Count).toBe 2
        Vitest.expect(saved.[0].FirstName).toEqual (Some "Existing")
        Vitest.expect(saved.[1].FirstName).toEqual (Some "Imported")
    }
)

Vitest.test (
    "dismissing the dialog stops its pending lookup while a host still keeps it mounted",
    fun () -> promise {
        let pending = Helpers.deferred ()
        let mutable lookups = 0
        let mutable applied = 0
        let mutable closed = 0

        Helpers.stubFetch (fun (url: string) ->
            if url.EndsWith "/person" then
                lookups <- lookups + 1

            if lookups = 2 && url.EndsWith "/person" then
                pending.promise
            else
                promise { return Helpers.response (Helpers.record "First" "Person") 200 }
        )

        // A host with an exit transition calls close but unmounts the dialog later.
        RTL.render (
            PersonSearchDialog.PersonSearchDialog(
                Helpers.orcid,
                "",
                SearchMode.Orcid,
                (fun _ -> applied <- applied + 1),
                (fun () -> closed <- closed + 1)
            )
        )
        |> ignore

        Helpers.click "Search"
        do! Helpers.singlePreview "First Person"
        Helpers.click "Search"
        RTL.screen.getByText "Searching people…" |> ignore
        Helpers.click "Cancel"
        Vitest.expect(closed).toBe 1

        do!
            RTL.act (fun () -> promise {
                pending.resolve (Helpers.response (Helpers.record "Closed" "Person") 200)
                do! Helpers.delay 50
            })

        Vitest.expect(RTL.screen.queryByText("Closed Person").IsNone).toBe true
        Vitest.expect(applied).toBe 0
    }
)

Vitest.test (
    "Load more keeps the selected person",
    fun () -> promise {
        Helpers.stubFetch (fun (url: string) -> promise {
            let results =
                if Helpers.queryParameter url "start" = "10" then
                    [| Helpers.searchResult Helpers.orcid "Final" "Author" |]
                else
                    Array.init
                        10
                        (fun index -> Helpers.searchResult (Helpers.fixtureOrcid (index + 1)) "Jane" $"Smith {index}")

            return Helpers.response (Helpers.searchPage results 11) 200
        })

        render [ Person() ] ignore
        Helpers.click "Search by name"
        Helpers.searchName "Jane Smith"
        let! first = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview Jane Smith 0"))
        RTL.fireEvent.click first
        Helpers.click "Load more"
        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview Final Author"))

        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Preview Jane Smith 0", pressed = true))
        |> ignore

        Vitest.expect((Helpers.button "Apply person" :?> HTMLButtonElement).disabled).toBe false
    }
)

Vitest.test (
    "both search actions prefill every pending draft before the field debounce",
    fun () -> promise {
        let mutable requests = 0

        Helpers.stubFetch (fun (_: string) -> promise {
            requests <- requests + 1
            return Helpers.response (Helpers.record "Jane" "Smith") 200
        })

        render
            [
                Person(firstName = "Manual", lastName = "Stored", orcid = "0000-0001-5109-3700")
            ]
            ignore

        RTL.fireEvent.change (RTL.screen.getByDisplayValue "Manual", {| target = {| value = "Ada" |} |})
        RTL.fireEvent.change (RTL.screen.getByDisplayValue "Stored", {| target = {| value = "Lovelace" |} |})

        RTL.fireEvent.change (
            RTL.screen.getByDisplayValue "0000-0001-5109-3700",
            {|
                target = {| value = Helpers.orcid |}
            |}
        )

        Helpers.click "Search by name"

        let queryValue label =
            (RTL.screen.getByRole ("textbox", ByRoleOptions(name = Text label)) :?> HTMLInputElement).value

        let nameQuery () = queryValue "Person name"

        Vitest.expect(nameQuery ()).toBe "Ada Lovelace"
        Helpers.click "ORCID"
        Vitest.expect(queryValue "ORCID iD or URL").toBe Helpers.orcid
        Helpers.click "Cancel"
        do! Helpers.expectClosed ()
        Helpers.openSearch ()
        Helpers.click "Name"
        Vitest.expect(nameQuery ()).toBe "Ada Lovelace"
        Vitest.expect(requests).toBe 0
    }
)

Vitest.test (
    "the preview shows a credit name apart from the names Apply writes",
    fun () -> promise {
        Helpers.stubFetch (fun (_: string) -> promise {
            let result = {|
                Helpers.searchResult Helpers.orcid "" "" with
                    ``credit-name`` = "A. Lovelace"
            |}

            return Helpers.response (Helpers.searchPage [| result |] 1) 200
        })

        render [ Person() ] ignore
        Helpers.click "Search by name"
        Helpers.searchName "Lovelace"
        let! _ = RTL.screen.findByRole ("heading", ByRoleOptions(name = Text "Autofill preview"))
        RTL.screen.getByText "Credit name" |> ignore
        RTL.screen.getByText "A. Lovelace" |> ignore
        // The only "Name" left is the search mode tab.
        Vitest.expect(RTL.screen.getAllByText("Name").Count).toBe 1
    }
)

Vitest.test (
    "a blank person name never fetches",
    fun () -> promise {
        let mutable calls = 0

        Helpers.stubFetch (fun (_: string) -> promise {
            calls <- calls + 1
            return Helpers.response (Helpers.searchPage [||] 0) 200
        })

        render [ Person() ] ignore
        Helpers.click "Search by name"
        Helpers.searchName "   "
        let! alert = RTL.screen.findByRole "alert"
        Vitest.expect(alert.textContent).toBe "Enter a person's name."
        Vitest.expect(calls).toBe 0
    }
)

Vitest.test (
    "the preview hides a credit name that repeats the applied name",
    fun () -> promise {
        Helpers.stubFetch (fun (_: string) -> promise {
            let result = {|
                Helpers.searchResult Helpers.orcid "Ada" "Lovelace" with
                    ``credit-name`` = "Ada Lovelace"
            |}

            return Helpers.response (Helpers.searchPage [| result |] 1) 200
        })

        render [ Person() ] ignore
        Helpers.click "Search by name"
        Helpers.searchName "Ada Lovelace"
        let! _ = RTL.screen.findByRole ("heading", ByRoleOptions(name = Text "Autofill preview"))
        Vitest.expect(RTL.screen.queryByText("Credit name").IsNone).toBe true
        Vitest.expect(RTL.screen.getAllByText("Ada Lovelace").Count).toBe 1
    }
)
