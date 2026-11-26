module internal Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.RtlTests

open ARCtrl
open Browser.Types
open Fable.Core
open Feliz
open Vitest

module private Helpers =
    [<Emit("new Response(JSON.stringify($0), {status: $1, headers: {'Content-Type': 'application/json'}})")>]
    let response (_body: obj) (_status: int) : Fetch.Types.Response = jsNative

    [<Emit("new Response($0, {status: $1, headers: {'Content-Type': 'text/plain'}})")>]
    let textResponse (_body: string) (_status: int) : Fetch.Types.Response = jsNative

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

    let crossref title =
        box {|
            message = {|
                title = [| title |]
                DOI = "10.1234/plant.1"
                author = [| {| given = "Ada"; family = "Lovelace" |} |]
                ``container-title`` = [| "Plant Journal" |]
                published = {| ``date-parts`` = [| [| 2025 |] |] |}
            |}
        |}

    let europe title id = {|
        id = id
        source = "MED"
        pmid = id
        title = title
        authorString = "Lovelace A, Turing A"
        doi = "10.1234/plant.2"
        journalTitle = "Plant Journal"
        pubYear = "2025"
    |}

    let europePage results nextCursor hitCount =
        box {|
            resultList = {| result = results |}
            nextCursorMark = nextCursor
            hitCount = hitCount
        |}

    let stubFetch handler =
        Vitest.vi.stubGlobal ("fetch", box handler)

    let button label =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text label))

    let click label = RTL.fireEvent.click (button label)

    let dialog () =
        RTL.screen.getByRole ("dialog", ByRoleOptions(name = Text "Search publication"))

    let openSearch () = click "Search by DOI"

    let changeQuery label value =
        RTL.fireEvent.change (
            RTL.screen.getByRole ("textbox", ByRoleOptions(name = Text label)),
            {| target = {| value = value |} |}
        )

    let searchDoi query =
        changeQuery "DOI" query
        click "Search"

    let searchEurope query =
        changeQuery "Europe PMC query" query
        click "Search"

    let preview title = promise {
        let! result = RTL.screen.findByRole ("button", ByRoleOptions(name = Text $"Preview {title}"))
        RTL.fireEvent.click result

        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Autofill preview"))
        |> ignore
    }

    let singlePreview (title: string) = promise {
        let! _ = RTL.screen.findByRole ("heading", ByRoleOptions(name = Text "Autofill preview"))
        Vitest.expect(RTL.screen.getAllByText(title).Count).toBe 1
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text $"Preview {title}")).IsNone).toBe true
    }

    let expectClosed () =
        RTL.waitFor (fun () ->
            Vitest
                .expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Search publication")).IsNone)
                .toBe
                true
        )

[<Erase; Mangle(false)>]
type private Harness =
    [<ReactComponent>]
    static member Harness(initial: ResizeArray<Publication>, changed: ResizeArray<Publication> -> unit) =
        let values, setValues = React.useState initial

        PublicationsInput.PublicationsInput(
            values,
            (fun next ->
                changed next
                setValues (ResizeArray next)
            ),
            "Publications"
        )

let private render (publications: seq<Publication>) changed =
    RTL.render (Harness.Harness(ResizeArray publications, changed)) |> ignore

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.unstubAllGlobals ()
)

for label, initial, draft, queryLabel, mode in
    [
        "Search by DOI", "10.1234/stored", "10.1234/latest", "DOI", "DOI"
        "Search by PubMed ID", "123456", "789012", "Europe PMC query", "Europe PMC"
    ] do
    Vitest.test (
        $"{label} opens the correct mode with the latest typed value before the field debounce",
        fun () -> promise {
            let mutable requests = 0

            Helpers.stubFetch (fun (_: string) -> promise {
                requests <- requests + 1
                return Helpers.response (Helpers.europePage [||] "*" 0) 200
            })

            let publication =
                if mode = "DOI" then
                    Publication(doi = initial)
                else
                    Publication(pubMedID = initial)

            render [ publication ] ignore
            let input = RTL.screen.getByDisplayValue initial
            let trigger = Helpers.button label
            RTL.fireEvent.change (input, {| target = {| value = draft |} |})
            RTL.fireEvent.click trigger

            let query =
                RTL.screen.getByRole ("textbox", ByRoleOptions(name = Text queryLabel)) :?> HTMLInputElement

            Vitest.expect(query.value).toBe draft

            RTL.screen.getByRole ("button", ByRoleOptions(name = Text mode, pressed = true))
            |> ignore

            Vitest.expect(requests).toBe 0
        }
    )

Vitest.test (
    "PubMed ID field opens a prefilled Europe PMC search and its unique result can be confirmed directly",
    fun () -> promise {
        let original = Publication(pubMedID = "123456", title = "Manual title")
        let mutable calls = 0
        let mutable saved = original
        let mutable request = ""

        Helpers.stubFetch (fun (url: string) -> promise {
            request <- url
            return Helpers.response (Helpers.europePage [| Helpers.europe "Unique PubMed result" "123456" |] "*" 1) 200
        })

        render
            [ original ]
            (fun values ->
                calls <- calls + 1
                saved <- values.[0]
            )

        Helpers.click "Search by PubMed ID"

        let query =
            RTL.screen.getByRole ("textbox", ByRoleOptions(name = Text "Europe PMC query")) :?> HTMLInputElement

        Vitest.expect(query.value).toBe "123456"
        Helpers.click "Search"
        do! Helpers.singlePreview "Unique PubMed result"
        Vitest.expect(Helpers.queryParameter request "query").toBe "EXT_ID:123456 AND SRC:MED"
        Vitest.expect(calls).toBe 0
        Vitest.expect(original.Title).toEqual (Some "Manual title")
        Vitest.expect((Helpers.button "Apply publication" :?> HTMLButtonElement).disabled).toBe false
        Helpers.click "Apply publication"
        do! Helpers.expectClosed ()
        Vitest.expect(calls).toBe 1
        Vitest.expect(saved.Title).toEqual (Some "Unique PubMed result")
        Vitest.expect(saved.PubMedID).toEqual (Some "123456")
    }
)

Vitest.test (
    "DOI lookup previews before confirmation, updates the summary and retains autofill after a pending input debounce",
    fun () -> promise {
        let original = Publication(title = "Original title", pubMedID = "123")
        let mutable calls = 0
        let mutable saved = original
        let mutable request = ""

        Helpers.stubFetch (fun (url: string) -> promise {
            request <- url
            return Helpers.response (Helpers.crossref "A plant atlas") 200
        })

        render
            [ original ]
            (fun values ->
                calls <- calls + 1
                saved <- values.[0]
            )

        let titleInput = RTL.screen.getByDisplayValue "Original title"
        Helpers.openSearch ()
        Helpers.searchDoi "https://doi.org/10.1234/plant.1"
        do! Helpers.singlePreview "A plant atlas"
        Vitest.expect(request).toBe "https://api.crossref.org/works/10.1234%2Fplant.1"
        Vitest.expect(calls).toBe 0
        Vitest.expect(original.Title).toEqual (Some "Original title")
        let applyButton = Helpers.button "Apply publication"

        RTL.fireEvent.change (
            titleInput,
            {|
                target = {| value = "Pending manual title" |}
            |}
        )

        RTL.fireEvent.click applyButton
        do! Helpers.expectClosed ()
        do! RTL.act (fun () -> Helpers.delay 400)
        Vitest.expect(calls).toBe 1
        Vitest.expect(saved.Title).toEqual (Some "A plant atlas")
        Vitest.expect(saved.PubMedID).toEqual (Some "123")
        Vitest.expect(original.Title).toEqual (Some "Original title")

        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "A plant atlas"))
        |> ignore

        RTL.screen.getByDisplayValue "A plant atlas" |> ignore
        RTL.screen.getByDisplayValue "10.1234/plant.1" |> ignore
        RTL.screen.getByDisplayValue "Ada Lovelace" |> ignore
    }
)

Vitest.test (
    "Europe PMC text search loads another cursor page and applies the selected publication",
    fun () -> promise {
        let requests = ResizeArray<string>()
        let mutable saved = Publication()

        Helpers.stubFetch (fun (url: string) -> promise {
            requests.Add url

            if Helpers.queryParameter url "cursorMark" = "next-page" then
                return
                    Helpers.response
                        (Helpers.europePage [| Helpers.europe "Second article" "456" |] "last-page" 11)
                        200
            else
                let firstPage =
                    Array.init
                        10
                        (fun index ->
                            Helpers.europe
                                (if index = 0 then "First article" else $"Article {index}")
                                (string (123 + index))
                        )

                return Helpers.response (Helpers.europePage firstPage "next-page" 11) 200
        })

        render [ Publication() ] (fun values -> saved <- values.[0])
        Helpers.openSearch ()
        Helpers.click "Europe PMC"
        Helpers.searchEurope "plant atlas Lovelace"
        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview First article"))
        Vitest.expect((Helpers.button "Apply publication" :?> HTMLButtonElement).disabled).toBe true
        Vitest.expect(RTL.screen.queryByRole("heading", ByRoleOptions(name = Text "Autofill preview")).IsNone).toBe true
        Helpers.click "Load more"
        do! Helpers.preview "Second article"
        Vitest.expect(requests.Count).toBe 2
        Vitest.expect(Helpers.queryParameter requests.[0] "query").toBe "plant atlas Lovelace"
        Vitest.expect(Helpers.queryParameter requests.[0] "pageSize").toBe "10"
        Vitest.expect(Helpers.queryParameter requests.[1] "cursorMark").toBe "next-page"
        Helpers.click "Apply publication"
        do! Helpers.expectClosed ()
        Vitest.expect(saved.PubMedID).toEqual (Some "456")
        Vitest.expect(saved.DOI).toEqual (Some "10.1234/plant.2")
        RTL.screen.getByDisplayValue "Lovelace A, Turing A" |> ignore

        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Second article"))
        |> ignore
    }
)

Vitest.test (
    "Europe PMC translates PubMed and PMC identifiers into exact queries",
    fun () -> promise {
        let requests = ResizeArray<string>()

        Helpers.stubFetch (fun (url: string) -> promise {
            requests.Add url
            return Helpers.response (Helpers.europePage [||] "*" 0) 200
        })

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.click "Europe PMC"
        Helpers.searchEurope "123456"
        let! _ = RTL.screen.findByText "No publications found."
        Vitest.expect(Helpers.queryParameter requests.[0] "query").toBe "EXT_ID:123456 AND SRC:MED"
        Helpers.searchEurope "PMC123456"
        do! RTL.waitFor (fun () -> Vitest.expect(requests.Count).toBe 2)
        Vitest.expect(Helpers.queryParameter requests.[1] "query").toBe "PMCID:PMC123456"
    }
)

Vitest.test (
    "Cancel, Escape and outside dismissal discard selected publications without notifying the parent",
    fun () -> promise {
        let mutable calls = 0
        Helpers.stubFetch (fun (_: string) -> promise { return Helpers.response (Helpers.crossref "Discard me") 200 })
        render [ Publication(title = "Keep me") ] (fun _ -> calls <- calls + 1)

        for dismissal in [ "Cancel"; "Escape"; "outside" ] do
            Helpers.openSearch ()
            Helpers.searchDoi "10.1234/plant.1"
            do! Helpers.singlePreview "Discard me"

            match dismissal with
            | "Escape" -> RTL.fireEvent.custom ("keyDown", Helpers.dialog (), {| key = "Escape"; code = "Escape" |})
            | "outside" -> RTL.fireEvent.custom ("mouseDown", unbox Browser.Dom.document.body)
            | _ -> Helpers.click "Cancel"

            do! Helpers.expectClosed ()
            Vitest.expect(calls).toBe 0
            RTL.screen.getByDisplayValue "Keep me" |> ignore
    }
)

Vitest.test (
    "invalid DOI never fetches and a failed request can be retried",
    fun () -> promise {
        let mutable calls = 0

        Helpers.stubFetch (fun (_: string) -> promise {
            calls <- calls + 1
            return Helpers.response (Helpers.crossref "Recovered article") (if calls = 1 then 503 else 200)
        })

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.searchDoi "not a DOI"
        RTL.screen.getByRole "alert" |> ignore
        Vitest.expect(calls).toBe 0
        Helpers.searchDoi "10.1234/plant.1"
        let! _ = RTL.screen.findByRole "alert"
        Helpers.click "Search"
        do! Helpers.singlePreview "Recovered article"
        Vitest.expect(calls).toBe 2
    }
)

Vitest.test (
    "an unknown DOI shows no results and other HTTP errors show the provider message",
    fun () -> promise {
        let mutable status = 404

        // Crossref answers an unknown DOI with a plain-text body.
        Helpers.stubFetch (fun (_: string) -> promise { return Helpers.textResponse "Resource not found." status })

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.searchDoi "10.1234/unknown"
        let! _ = RTL.screen.findByText "No publications found."
        Vitest.expect(RTL.screen.queryByRole("alert").IsNone).toBe true
        status <- 500
        Helpers.click "Search"
        let! alert = RTL.screen.findByRole "alert"

        Vitest.expect(alert.textContent).toBe
            "Publication search failed: Crossref could not load this publication (HTTP 500). Please try again."

        status <- 200
        Helpers.click "Search"

        let! _ =
            RTL.screen.findByText
                "Publication search failed: ARCitISA could not read the response from Crossref. Please try again."

        ()
    }
)

Vitest.test (
    "changing the query invalidates an older response and searching remains explicit",
    fun () -> promise {
        let pending = Helpers.deferred ()
        let mutable calls = 0

        Helpers.stubFetch (fun (_: string) ->
            calls <- calls + 1

            if calls = 1 then
                pending.promise
            else
                promise { return Helpers.response (Helpers.crossref "Current article") 200 }
        )

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.searchDoi "10.1234/old"
        RTL.screen.getByText "Searching publications…" |> ignore
        Helpers.changeQuery "DOI" "10.1234/new"
        Vitest.expect(calls).toBe 1
        Helpers.click "Search"
        do! Helpers.singlePreview "Current article"
        do! RTL.act (fun () -> promise { pending.resolve (Helpers.response (Helpers.crossref "Stale article") 200) })

        Vitest.expect(RTL.screen.queryByText("Stale article").IsNone).toBe true
        RTL.screen.getByText "Current article" |> ignore
    }
)

Vitest.test (
    "changing modes retains independent queries and ignores the previous mode's pending request",
    fun () -> promise {
        let pending = Helpers.deferred ()

        Helpers.stubFetch (fun (url: string) ->
            if url.Contains "crossref" then
                pending.promise
            else
                promise {
                    return Helpers.response (Helpers.europePage [| Helpers.europe "Europe result" "123" |] "*" 1) 200
                }
        )

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.searchDoi "10.1234/plant.1"
        Helpers.click "Europe PMC"
        Helpers.searchEurope "plant atlas"
        do! Helpers.singlePreview "Europe result"
        do! RTL.act (fun () -> promise { pending.resolve (Helpers.response (Helpers.crossref "Old DOI result") 200) })

        Vitest.expect(RTL.screen.queryByText("Old DOI result").IsNone).toBe true
        RTL.screen.getByText "Europe result" |> ignore

        Helpers.click "DOI"
        RTL.screen.getByDisplayValue "10.1234/plant.1" |> ignore
        Helpers.click "Europe PMC"
        RTL.screen.getByDisplayValue "plant atlas" |> ignore
    }
)

Vitest.test (
    "reopening after cancelling a pending lookup shows a fresh dialog",
    fun () -> promise {
        let pending = Helpers.deferred ()
        let mutable calls = 0
        Helpers.stubFetch (fun (_: string) -> pending.promise)
        render [ Publication() ] (fun _ -> calls <- calls + 1)
        Helpers.openSearch ()
        Helpers.searchDoi "10.1234/plant.1"
        Helpers.click "Cancel"
        do! Helpers.expectClosed ()
        Helpers.openSearch ()
        Vitest.expect(RTL.screen.queryByText("Searching publications…").IsNone).toBe true
        Vitest.expect(calls).toBe 0
    }
)

Vitest.test (
    "confirmation changes only the publication row that opened the search",
    fun () -> promise {
        let mutable saved = ResizeArray<Publication>()

        Helpers.stubFetch (fun (_: string) -> promise {
            return Helpers.response (Helpers.crossref "Second row replacement") 200
        })

        render
            [
                Publication(title = "First row")
                Publication(title = "Second row")
            ]
            (fun values -> saved <- values)

        let searches =
            RTL.screen.getAllByRole ("button", ByRoleOptions(name = Text "Search by DOI"))

        RTL.fireEvent.click searches.[1]
        Helpers.searchDoi "10.1234/plant.1"
        do! Helpers.singlePreview "Second row replacement"
        Helpers.click "Apply publication"
        do! Helpers.expectClosed ()
        Vitest.expect(saved.[0].Title).toEqual (Some "First row")
        Vitest.expect(saved.[1].Title).toEqual (Some "Second row replacement")

        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "First row"))
        |> ignore

        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Second row replacement"))
        |> ignore
    }
)

Vitest.test (
    "dismissing the dialog stops its pending lookup while a host still keeps it mounted",
    fun () -> promise {
        let pending = Helpers.deferred ()
        let mutable fetches = 0
        let mutable applied = 0
        let mutable closed = 0

        Helpers.stubFetch (fun (_: string) ->
            fetches <- fetches + 1

            if fetches = 1 then
                promise { return Helpers.response (Helpers.crossref "First result") 200 }
            else
                pending.promise
        )

        // A host with an exit transition calls close but unmounts the dialog later.
        RTL.render (
            PublicationSearchDialog.PublicationSearchDialog(
                "10.1234/plant.1",
                "",
                true,
                (fun _ -> applied <- applied + 1),
                (fun () -> closed <- closed + 1)
            )
        )
        |> ignore

        Helpers.click "Search"
        do! Helpers.singlePreview "First result"
        Helpers.click "Search"
        RTL.screen.getByText "Searching publications…" |> ignore
        Helpers.click "Cancel"
        Vitest.expect(closed).toBe 1

        do!
            RTL.act (fun () -> promise {
                pending.resolve (Helpers.response (Helpers.crossref "Closed result") 200)
                do! Helpers.delay 50
            })

        Vitest.expect(RTL.screen.queryByText("Closed result").IsNone).toBe true
        Vitest.expect(applied).toBe 0
    }
)

Vitest.test (
    "Load more keeps the selected publication",
    fun () -> promise {
        Helpers.stubFetch (fun (url: string) -> promise {
            if Helpers.queryParameter url "cursorMark" = "*" then
                let results =
                    Array.init 10 (fun index -> Helpers.europe $"Result {index}" (string (index + 1)))

                return Helpers.response (Helpers.europePage results "next" 11) 200
            else
                return Helpers.response (Helpers.europePage [| Helpers.europe "Final result" "11" |] "next" 11) 200
        })

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.click "Europe PMC"
        Helpers.searchEurope "plant"
        let! first = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview Result 0"))
        RTL.fireEvent.click first
        Helpers.click "Load more"
        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview Final result"))

        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Preview Result 0", pressed = true))
        |> ignore

        Vitest.expect((Helpers.button "Apply publication" :?> HTMLButtonElement).disabled).toBe false
    }
)

Vitest.test (
    "network failures and Europe PMC HTTP errors show a message naming the provider",
    fun () -> promise {
        Helpers.stubFetch (fun (url: string) -> promise {
            if url.Contains "europepmc" then
                return Helpers.response (box {| error = "unavailable" |}) 500
            else
                return (failwith "Failed to fetch": Fetch.Types.Response)
        })

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.searchDoi "10.1234/plant.1"
        let! alert = RTL.screen.findByRole "alert"

        Vitest.expect(alert.textContent).toBe
            "Publication search failed: Crossref could not be reached. Please try again."

        Helpers.click "Europe PMC"
        Helpers.searchEurope "plant"
        let! alert = RTL.screen.findByRole "alert"

        Vitest.expect(alert.textContent).toBe
            "Publication search failed: Europe PMC could not search publications (HTTP 500). Please try again."
    }
)

Vitest.test (
    "a blank Europe PMC query never fetches",
    fun () -> promise {
        let mutable calls = 0

        Helpers.stubFetch (fun (_: string) -> promise {
            calls <- calls + 1
            return Helpers.response (Helpers.europePage [||] "*" 0) 200
        })

        render [ Publication() ] ignore
        Helpers.openSearch ()
        Helpers.click "Europe PMC"
        Helpers.searchEurope "   "
        let! alert = RTL.screen.findByRole "alert"
        Vitest.expect(alert.textContent).toBe "Enter a title, author, PubMed ID, or PMC ID."
        Vitest.expect(calls).toBe 0
    }
)
