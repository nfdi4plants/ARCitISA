module internal Swate.Components.Composite.TermSearch.RtlTests

open Fable.Core
open Feliz
open Vitest
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.TermSearch.Types

// These tests replace the TermSearch and AnnotationTable stories that call the live services (the Swate API and
// the TIB terminology API). Both are stubbed here, so a test run never leaves the machine.

type private Reply = { status: int; body: string }

let private ok (body: obj) = {
    status = 200
    body = JS.JSON.stringify body
}

/// Fable.Remoting sends the arguments of an API call as a JSON array. For the calls used here, the first element
/// is a TermQuery or a ParentTermQuery.
type private SwateRequest =
    abstract query: string
    abstract parentTermId: string
    abstract limit: int

module private Fixtures =
    let private term accession name description ontology =
        Database.Term.createTerm (accession, name, description, false, ontology)

    let parentId = "MS:1000031"

    let terms = [|
        term parentId "instrument model" "\"Instrument model name not including the vendor's name.\" [PSI:MS]" "MS"
        term
            "NCIT:C177610"
            "Instrument Model"
            "\"The name and/or number associated with a specific device style or design.\" []"
            "NCIT"
        term
            "MS:1000121"
            "SCIEX instrument model"
            "\"The brand of instruments from the joint venture between Applied Biosystems and MDS Analytical Technologies.\" [PSI:MS]"
            "MS"
        term
            "MS:1000690"
            "SCIEX software"
            "\"SCIEX or Applied Biosystems software for data acquisition and analysis.\" [PSI:MS]"
            "MS"
        term "MS:1001798" "LECO software" "\"LECO software for data acquisition and analysis.\" [PSI:MS]" "MS"
        term "MS:1001800" "LECO instrument model" "\"LECO instrument model.\" [PSI:MS]" "MS"
        term
            "NCIT:C25206"
            "Temperature"
            "\"A measure of the average kinetic energy of a system of particles.\" []"
            "NCIT"
        term "PATO:0000146" "temperature" "\"A physical quality of the thermal energy of a system.\" [PATOC:GVG]" "PATO"
    |]

    /// The terms below a parent term.
    let childrenOf (parent: string) =
        let children =
            Map [ parentId, [| "MS:1000121"; "MS:1001800" |] ]
            |> Map.tryFind parent
            |> Option.defaultValue [||]

        terms |> Array.filter (fun t -> Array.contains t.Accession children)

    /// A search needs at least one word, and a term matches when its name contains every word.
    let matching (query: string) (candidates: Database.Term[]) =
        let words =
            query.ToLower().Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)

        candidates
        |> Array.filter (fun t ->
            words.Length > 0
            && words |> Array.forall (fun word -> t.Name.ToLower().Contains word)
        )

module private SwateApi =
    let private prefix = $"{URLs.PRODUCTION_URL}/api/IOntologyAPIv3/"

    /// The reply of the Swate API, or None when the URL is not one of the calls TermSearch makes.
    let answer (url: string) (body: string) : Reply option =
        if not (url.StartsWith prefix) then
            None
        else
            let request = (JS.JSON.parse body |> unbox<SwateRequest[]>).[0]

            match url.Substring prefix.Length with
            | "searchTerm" ->
                let candidates =
                    if System.String.IsNullOrEmpty request.parentTermId then
                        Fixtures.terms
                    else
                        Fixtures.childrenOf request.parentTermId

                Fixtures.matching request.query candidates
                |> Array.truncate request.limit
                |> ok
                |> Some
            | "searchChildTerms" ->
                {|
                    query = request
                    results = Fixtures.childrenOf request.parentTermId |> Array.truncate request.limit
                |}
                |> ok
                |> Some
            | _ -> None

module private Tib =
    let private oboIri (accession: string) =
        $"http://purl.obolibrary.org/obo/{accession.Replace(':', '_')}"

    let private doc (t: Database.Term) = {|
        iri = oboIri t.Accession
        ontology_name = t.FK_Ontology.ToLower()
        ontology_prefix = t.FK_Ontology
        short_form = t.Accession.Replace(':', '_')
        description = [| t.Description |]
        label = t.Name
        obo_id = t.Accession
        ``type`` = "class"
    |}

    [<Emit("new URL($0).pathname")>]
    let private pathname (_url: string) : string = jsNative

    [<Emit("new URL($0).searchParams.get($1)")>]
    let queryParameter (_url: string) (_name: string) : string = jsNative

    /// The reply of the TIB API, or None when the URL is not one of the calls TIBApi makes.
    let answer (url: string) : Reply option =
        if not (url.StartsWith Api.TIBApi.TIBTypes.BaseAPIUrl) then
            None
        else
            match pathname url with
            | "/api/terms" ->
                let oboId = queryParameter url "obo_id"

                match Fixtures.terms |> Array.tryFind (fun t -> t.Accession = oboId) with
                | Some t ->
                    ok {|
                        _embedded = {|
                            terms = [|
                                {|
                                    obo_id = t.Accession
                                    iri = oboIri t.Accession
                                |}
                            |]
                        |}
                    |}
                    |> Some
                | None -> ok {| |} |> Some
            | "/api/search" ->
                let query = queryParameter url "q"
                let parentIri = queryParameter url "childrenOf"

                let candidates =
                    if System.String.IsNullOrEmpty parentIri then
                        Fixtures.terms
                    else
                        match Fixtures.terms |> Array.tryFind (fun t -> oboIri t.Accession = parentIri) with
                        | Some parent -> Fixtures.childrenOf parent.Accession
                        | None -> [||]

                // The API lists nothing for an empty query, and "*" lists everything.
                let hits =
                    if query = "*" then
                        candidates
                    else
                        Fixtures.matching query candidates

                let docs = hits |> Array.truncate (int (queryParameter url "rows")) |> Array.map doc

                ok {|
                    response = {|
                        numFound = docs.Length
                        start = 0
                        docs = docs
                    |}
                |}
                |> Some
            | _ -> None

    // The same queries as the TIBSearch story.
    let private toTerms (search: JS.Promise<Api.TIBApi.TIBTypes.SearchApi option>) : JS.Promise<ResizeArray<Term>> =
        search
        |> Promise.map (fun result ->
            match result with
            | Some api -> ResizeArray(api.ToMyTerm())
            | None -> ResizeArray()
        )

    let termSearchQueries =
        ResizeArray<string * SearchCall>(
            [
                "tib_search", fun query -> Api.TIBApi.TIBApi.defaultSearch (query, 10, "DataPLANT") |> toTerms
            ]
        )

    let parentSearchQueries =
        ResizeArray<string * ParentSearchCall>(
            [
                "tib_search",
                fun (parentId, query) ->
                    Api.TIBApi.TIBApi.searchChildrenOf (query, parentId, 10, "DataPLANT") |> toTerms
            ]
        )

    let allChildrenSearchQueries =
        ResizeArray<string * AllChildrenSearchCall>(
            [
                "tib_search",
                fun parentId -> Api.TIBApi.TIBApi.searchAllChildrenOf (parentId, 500, "DataPLANT") |> toTerms
            ]
        )

module private Network =
    /// The URLs that no fixture answers. Every test fails when this list is not empty after it ran.
    let unexpected = ResizeArray<string>()

    [<Emit("new Response($0, {status: $1, headers: {'Content-Type': 'application/json'}})")>]
    let private response (_body: string) (_status: int) : Fetch.Types.Response = jsNative

    // Fable.Remoting talks to the Swate API through XMLHttpRequest. The class answers every request from the handler.
    [<Emit("""(handler => class {
        open(method, url) { this.requestUrl = url; }
        setRequestHeader() {}
        abort() {}
        send(body) {
            const reply = handler(this.requestUrl, body);
            setTimeout(() => {
                this.status = reply.status;
                this.responseText = reply.body;
                this.readyState = 4;
                this.onreadystatechange();
            });
        }
    })($0)""")>]
    let private xhrClass (_handler: System.Func<string, string, Reply>) : obj = jsNative

    /// Replaces XMLHttpRequest and fetch, and returns the list of requested URLs.
    let install () =
        let requests = ResizeArray<string>()
        unexpected.Clear()

        Vitest.vi.stubGlobal (
            "XMLHttpRequest",
            xhrClass (
                System.Func<string, string, Reply>(fun url body ->
                    requests.Add url

                    match SwateApi.answer url body with
                    | Some reply -> reply
                    | None ->
                        unexpected.Add url
                        { status = 599; body = "" }
                )
            )
        )

        Vitest.vi.stubGlobal (
            "fetch",
            box (fun (url: string) -> promise {
                requests.Add url

                match Tib.answer url with
                | Some reply -> return response reply.body reply.status
                | None ->
                    unexpected.Add url
                    return failwith $"Unexpected request: {url}"
            })
        )

        requests

module private Helpers =
    [<Emit("new Promise(resolve => setTimeout(resolve, $0))")>]
    let private delay (_milliseconds: int) : JS.Promise<unit> = jsNative

    let input () =
        RTL.screen.getByTestId "term-search-input"

    let search (text: string) =
        RTL.fireEvent.change (input (), {| target = {| value = text |} |})

    let press (key: string) =
        RTL.fireEvent.custom ("keyDown", input (), {| key = key; code = key |})

    let resultCount () =
        int ((input ()).getAttribute "data-debugresultcount")

    /// A directed result carries a title. It is the only titled element in a row of a non-obsolete term.
    let isDirected (name: string) =
        let row = ((RTL.screen.getByText name).closest "li").Value
        not (isNull (box (row.querySelector "[title]")))

    /// The debounce makes the first results arrive after 500 ms.
    let eventually (check: unit -> unit) : JS.Promise<unit> =
        RTL.waitFor ((fun () -> check ()), RTLWaitForOptions(timeout = 3000))

    /// TermSearch does not cancel its pending debounced searches on unmount. A test waits for them, so they
    /// run while the stubs are still in place.
    let drain () : JS.Promise<unit> = RTL.act (fun () -> delay 600)

[<Erase; Mangle(false)>]
type private Harness =
    /// Holds the term like the stories do.
    [<ReactComponent>]
    static member Harness(changed: Term option -> unit, ?parentId: string, ?tib: bool) =
        let term, setTerm = React.useState (None: Term option)

        let onTermChange =
            fun next ->
                setTerm next
                changed next

        if defaultArg tib false then
            TermSearch.TermSearch(
                term,
                onTermChange,
                ?parentId = parentId,
                disableDefaultSearch = true,
                disableDefaultParentSearch = true,
                disableDefaultAllChildrenSearch = true,
                termSearchQueries = Tib.termSearchQueries,
                parentSearchQueries = Tib.parentSearchQueries,
                allChildrenSearchQueries = Tib.allChildrenSearchQueries
            )
        else
            TermSearch.TermSearch(term, onTermChange, ?parentId = parentId)

module private TableFixtures =
    open ARCtrl

    [<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
    let private mockHTMLElementGeometry (vi: Vi) (propertyName: string) (value: int) : unit = jsNative

    /// jsdom has no layout, so the virtualized table needs a size to render cells.
    let installTableGeometry () =
        mockHTMLElementGeometry Vitest.vi "offsetWidth" 800
        mockHTMLElementGeometry Vitest.vi "offsetHeight" 600

    let createTable () =
        let table = ArcTable.init "Annotations"

        table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "Source 0" ])

        table.AddColumn(
            CompositeHeader.Component(OntologyAnnotation("instrument model", "MS", "MS:1000031")),
            ResizeArray [
                CompositeCell.Term(OntologyAnnotation("SCIEX instrument model", "MS", "MS:1000121"))
            ]
        )

        table

[<Erase; Mangle(false)>]
type private TableHarness =
    [<ReactComponent>]
    static member TableHarness() =
        let table, setTable = React.useState TableFixtures.createTable
        AnnotationTable.AnnotationTable(table, setTable, height = 600, debug = true)

let private render parentId changed =
    RTL.render (Harness.Harness(changed, ?parentId = parentId)) |> ignore

let private renderTib parentId changed =
    RTL.render (Harness.Harness(changed, ?parentId = parentId, tib = true))
    |> ignore

let private selected (changes: ResizeArray<Term option>) = changes.[changes.Count - 1].Value

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
    Vitest.vi.unstubAllGlobals ()
    Vitest.expect(Network.unexpected.ToArray()).toEqual [||]
)

Vitest.test (
    "a parent search marks the children of the parent as directed results",
    fun () -> promise {
        Network.install () |> ignore
        render (Some Fixtures.parentId) ignore
        Helpers.search "instrument"

        do!
            Helpers.eventually (fun () ->
                Vitest.expect(Helpers.isDirected "SCIEX instrument model").toBe true
                // The parent itself is no child of the parent.
                Vitest.expect(Helpers.isDirected "instrument model").toBe false
            )
    }
)

Vitest.test (
    "a TIB backed search marks children as directed results and lists all children of the parent",
    fun () -> promise {
        let requests = Network.install ()
        renderTib (Some Fixtures.parentId) ignore
        Helpers.search "SCIEX instrument"

        do! Helpers.eventually (fun () -> Vitest.expect(Helpers.isDirected "SCIEX instrument model").toBe true)

        // Clearing the input and opening the list again searches for all children of the parent.
        Helpers.search ""
        Helpers.press "Escape"
        Helpers.press "ArrowDown"
        do! Helpers.eventually (fun () -> Vitest.expect(Helpers.resultCount ()).toBeGreaterThan 0)
        Vitest.expect(requests |> Seq.exists (fun url -> Tib.queryParameter url "q" = "*")).toBe true
        do! Helpers.drain ()
    }
)

Vitest.test (
    "ArrowDown and Enter select the highlighted result",
    fun () -> promise {
        Network.install () |> ignore
        let changes = ResizeArray<Term option>()
        render None changes.Add
        Helpers.search "leco instrument"
        do! Helpers.eventually (fun () -> RTL.screen.getByText "LECO instrument model" |> ignore)
        Helpers.press "ArrowDown"
        Helpers.press "Enter"
        Vitest.expect((selected changes).id).toEqual (Some "MS:1001800")
        Vitest.expect((selected changes).name).toEqual (Some "LECO instrument model")
    }
)

Vitest.test (
    "clicking a result selects it",
    fun () -> promise {
        Network.install () |> ignore
        let changes = ResizeArray<Term option>()
        render None changes.Add
        Helpers.search "leco"
        do! Helpers.eventually (fun () -> RTL.screen.getByText "LECO software" |> ignore)
        RTL.fireEvent.click (RTL.screen.getByText "LECO instrument model")
        Vitest.expect((selected changes).id).toEqual (Some "MS:1001800")
        Vitest.expect((selected changes).name).toEqual (Some "LECO instrument model")
    }
)

Vitest.test (
    "a search lists the matching terms",
    fun () -> promise {
        Network.install () |> ignore
        render None ignore
        Helpers.search "temperature"
        do! Helpers.eventually (fun () -> RTL.screen.getByText "Temperature" |> ignore)
    }
)

Vitest.test (
    "editing a term cell stores the term that ArrowDown and Enter select in the term search",
    fun () -> promise {
        TableFixtures.installTableGeometry ()
        Network.install () |> ignore
        RTL.render (TableHarness.TableHarness()) |> ignore
        let! cell = RTL.screen.findByTestId "cell-1-2"
        // A second click on a cell activates it.
        RTL.fireEvent.click (cell, {| detail = 2 |})
        let! _ = RTL.screen.findByTestId "term-search-input"
        Helpers.search "leco instrument"
        do! Helpers.eventually (fun () -> RTL.screen.getByText "LECO instrument model" |> ignore)
        Helpers.press "ArrowDown"
        Helpers.press "Enter"

        do!
            Helpers.eventually (fun () ->
                Vitest.expect((RTL.screen.getByTestId "cell-1-2").textContent).toContain "LECO instrument model"
            )
    }
)

Vitest.test (
    "Enter without a highlighted result stores the typed text in a term cell",
    fun () -> promise {
        TableFixtures.installTableGeometry ()
        Network.install () |> ignore
        RTL.render (TableHarness.TableHarness()) |> ignore
        let! cell = RTL.screen.findByTestId "cell-1-2"
        RTL.fireEvent.click (cell, {| detail = 2 |})
        let! _ = RTL.screen.findByTestId "term-search-input"
        Helpers.search "leco"
        do! Helpers.eventually (fun () -> Vitest.expect(Helpers.resultCount ()).toBeGreaterThan 0)
        Helpers.press "Enter"

        do!
            Helpers.eventually (fun () ->
                let text = (RTL.screen.getByTestId "cell-1-2").textContent
                Vitest.expect(text).toContain "leco"
                Vitest.expect(text).``not``.toContain "LECO"
            )
    }
)
