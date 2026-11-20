module internal Swate.Components.Composite.MarkdownText.OntologyReferenceDialogTests

open Fable.Core
open Browser.Types
open Vitest
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types

type private FakeSurface(state: TextEditState) =
    member val Applied = ResizeArray<TextEdit>()
    member val Focused = 0 with get, set
    member val Accepts = true with get, set

    interface MarkdownSurface with
        member _.getState() = state

        member this.apply edit =
            this.Applied.Add edit
            this.Accepts

        member _.undo() = false
        member _.redo() = false

        member this.focus() = this.Focused <- this.Focused + 1

let private stateOf (text: string) (anchor: int) (head: int) : TextEditState = {|
    text = text
    selection = {| anchor = anchor; head = head |}
|}

let private instrumentModel =
    Term(
        name = "instrument model",
        id = "MS:1000031",
        source = "MS",
        href = "http://purl.obolibrary.org/obo/MS_1000031"
    )

let private data = Term(name = "data", id = "EDAM:0006", source = "EDAM")

let private plantPart =
    Term(name = "plant part", id = "NFDI4PSO:0000064", source = "NFDI4PSO")

let private termSearchQueries =
    ResizeArray<string * SearchCall>(
        [
            "stub", fun (_: string) -> promise { return ResizeArray [ instrumentModel; data; plantPart ] }
        ]
    )

/// Resolves the CURIEs of the stub terms and records every call.
let private lookupCalls = ResizeArray<string>()

let private lookupIri (id: string) : JS.Promise<string option> = promise {
    lookupCalls.Add id

    return
        match id with
        | "EDAM:0006" -> Some "http://edamontology.org/data_0006"
        | _ -> None
}

let private button (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))

let private okEdit (result: Result<TextEdit, string>) =
    match result with
    | Ok edit -> edit
    | Error reason -> failwith reason

let private eventually (check: unit -> unit) : JS.Promise<unit> =
    RTL.waitFor ((fun () -> check ()), RTLWaitForOptions(timeout = 3000))

/// Renders the open dialog, searches and chooses the result with this name.
let private renderAndChoose (surface: FakeSurface) (openStates: ResizeArray<bool>) (name: string) = promise {
    RTL.render (
        OntologyReferenceDialog.OntologyReferenceDialog(
            true,
            openStates.Add,
            surface,
            lookupIri = lookupIri,
            termSearchQueries = termSearchQueries,
            disableDefaultSearch = true
        )
    )
    |> ignore

    RTL.fireEvent.change (RTL.screen.getByTestId "term-search-input", {| target = {| value = "x" |} |})
    do! eventually (fun () -> RTL.screen.getByText name |> ignore)
    RTL.fireEvent.click (RTL.screen.getByText name)
}

/// Opens the details modal with F2, sets the name and the id in its first two inputs and submits.
let private submitThroughF2 (name: string) (id: string) = promise {
    RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "term-search-input", {| key = "F2"; code = "F2" |})
    do! eventually (fun () -> RTL.screen.getByTestId "modal_termsearch_details_modal" |> ignore)
    let modal = RTL.screen.getByTestId "modal_termsearch_details_modal"
    let inputs = modal.querySelectorAll "input"
    RTL.fireEvent.change (inputs.[0] :?> HTMLElement, {| target = {| value = name |} |})
    RTL.fireEvent.change (inputs.[1] :?> HTMLElement, {| target = {| value = id |} |})

    let buttonNamed (text: string) =
        let buttons = modal.querySelectorAll "button"

        [ 0 .. buttons.length - 1 ]
        |> List.map (fun i -> buttons.[i] :?> HTMLElement)
        |> List.find (fun b -> b.textContent = text)

    RTL.fireEvent.click (buttonNamed "Submit")

    do!
        eventually (fun () ->
            Vitest.expect((RTL.screen.getByTestId "term-search-input" :?> HTMLInputElement).value).toBe name
        )

    RTL.fireEvent.click (buttonNamed "Close")
}

/// Opens the details modal with F2, sets only the Source Link input (the fifth) and submits.
let private submitSourceLinkThroughF2 (href: string) = promise {
    RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "term-search-input", {| key = "F2"; code = "F2" |})
    do! eventually (fun () -> RTL.screen.getByTestId "modal_termsearch_details_modal" |> ignore)
    let modal = RTL.screen.getByTestId "modal_termsearch_details_modal"
    let inputs = modal.querySelectorAll "input"
    RTL.fireEvent.change (inputs.[4] :?> HTMLElement, {| target = {| value = href |} |})

    let buttonNamed (text: string) =
        let buttons = modal.querySelectorAll "button"

        [ 0 .. buttons.length - 1 ]
        |> List.map (fun i -> buttons.[i] :?> HTMLElement)
        |> List.find (fun b -> b.textContent = text)

    RTL.fireEvent.click (buttonNamed "Submit")
    do! Promise.sleep 100
    RTL.fireEvent.click (buttonNamed "Close")
}

let private isDisabled (name: string) =
    (button name :?> HTMLButtonElement).disabled

Vitest.beforeEach (fun () -> lookupCalls.Clear())

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "inserting a term with a link writes the reference at the cursor and closes without a lookup",
    fun () -> promise {
        let state = stateOf "measured with an " 17 17
        let surface = FakeSurface(state)
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "instrument model"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        RTL.fireEvent.click (button "Insert")

        let expected =
            OntologyReferences.insert
                (OntologyReferences.referenceOf instrumentModel "http://purl.obolibrary.org/obo/MS_1000031")
                state
            |> okEdit

        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0]).toEqual expected

        Vitest.expect(surface.Applied.[0].selection).toEqual {|
            anchor = 17 + "[instrument model][MS:1000031]".Length
            head = 17 + "[instrument model][MS:1000031]".Length
        |}

        Vitest.expect(openStates.ToArray()).toEqual [| false |]
        Vitest.expect(surface.Focused).toBe 1
        Vitest.expect(lookupCalls.Count).toBe 0
    }
)

Vitest.test (
    "a term without a link is looked up by its CURIE and can be inserted",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        do! renderAndChoose surface (ResizeArray<bool>()) "data"
        do! eventually (fun () -> RTL.screen.getByText "http://edamontology.org/data_0006" |> ignore)
        Vitest.expect(lookupCalls.ToArray()).toEqual [| "EDAM:0006" |]
        Vitest.expect(isDisabled "Insert").toBe false
    }
)

Vitest.test (
    "a term without an IRI shows the reason, disables Insert and offers plain text",
    fun () -> promise {
        let state = stateOf "" 0 0
        let surface = FakeSurface(state)
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "plant part"
        do! eventually (fun () -> RTL.screen.getByText "No IRI found for NFDI4PSO:0000064." |> ignore)
        Vitest.expect(isDisabled "Insert").toBe true
        RTL.fireEvent.click (button "Insert as plain text")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0].changes.[0].insert).toBe "plant part (NFDI4PSO:0000064)"
        Vitest.expect(openStates.ToArray()).toEqual [| false |]
    }
)

Vitest.test (
    "a label that already links elsewhere shows the error and keeps the dialog open",
    fun () -> promise {
        let surface = FakeSurface(stateOf "x\n\n[MS:1000031]: http://e.org/other\n" 1 1)
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "instrument model"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        RTL.fireEvent.click (button "Insert")

        Vitest.expect(RTL.screen.getByRole("alert").textContent).toBe
            "[MS:1000031] already links to http://e.org/other."

        Vitest.expect(surface.Applied.Count).toBe 0
        Vitest.expect(openStates.Count).toBe 0
    }
)

Vitest.test (
    "Cancel closes the dialog, focuses the surface and applies nothing",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 0)
        let openStates = ResizeArray<bool>()

        RTL.render (
            OntologyReferenceDialog.OntologyReferenceDialog(
                true,
                openStates.Add,
                surface,
                lookupIri = lookupIri,
                termSearchQueries = termSearchQueries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        RTL.fireEvent.click (button "Cancel")
        Vitest.expect(openStates.ToArray()).toEqual [| false |]
        Vitest.expect(surface.Focused).toBe 1
        Vitest.expect(surface.Applied.Count).toBe 0
)

Vitest.test (
    "a term set through the details modal replaces the earlier choice",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        do! renderAndChoose surface (ResizeArray<bool>()) "instrument model"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        do! submitThroughF2 "other term" "MS:9999999"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        RTL.fireEvent.click (button "Insert")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0].changes.[0].insert).toContain "[other term][MS:9999999]"
    }
)

Vitest.test (
    "renaming the chosen term in the details modal keeps the IRI and inserts the new name",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        do! renderAndChoose surface (ResizeArray<bool>()) "data"
        do! eventually (fun () -> RTL.screen.getByText "http://edamontology.org/data_0006" |> ignore)
        Vitest.expect(lookupCalls.Count).toBe 1
        do! submitThroughF2 "my data" "EDAM:0006"
        do! eventually (fun () -> RTL.screen.getByText "my data" |> ignore)
        Vitest.expect(isDisabled "Insert").toBe false
        Vitest.expect(lookupCalls.Count).toBe 1
        RTL.fireEvent.click (button "Insert")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0].changes.[0].insert).toContain "[my data][EDAM:0006]"
        Vitest.expect(surface.Applied.[0].changes.[0].insert).toContain "\"my data\""
    }
)

Vitest.test (
    "a term with neither a name nor a CURIE cannot be inserted as plain text",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)

        RTL.render (
            OntologyReferenceDialog.OntologyReferenceDialog(
                true,
                ignore,
                surface,
                lookupIri = lookupIri,
                termSearchQueries = termSearchQueries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        do! submitSourceLinkThroughF2 "urn:x:1"
        do! eventually (fun () -> RTL.screen.getByText "The IRI urn:x:1 is not a web address." |> ignore)
        RTL.fireEvent.click (button "Insert as plain text")
        Vitest.expect(surface.Applied.Count).toBe 0

        Vitest.expect(RTL.screen.getByRole("alert").textContent).toBe
            "This term has neither a name nor a CURIE to insert as text."
    }
)

Vitest.test (
    "a term set through the details modal without an earlier choice enables Insert",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)

        RTL.render (
            OntologyReferenceDialog.OntologyReferenceDialog(
                true,
                ignore,
                surface,
                lookupIri = lookupIri,
                termSearchQueries = termSearchQueries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        do! submitThroughF2 "data" "EDAM:0006"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        RTL.fireEvent.click (button "Insert")
        Vitest.expect(surface.Applied.[0].changes.[0].insert).toContain "[data][EDAM:0006]"
    }
)

Vitest.test (
    "a surface that refuses the edit keeps the dialog open with a message",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        surface.Accepts <- false
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "instrument model"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        RTL.fireEvent.click (button "Insert")
        Vitest.expect(RTL.screen.getByRole("alert").textContent).toBe "The editor did not accept the change."
        Vitest.expect(openStates.Count).toBe 0
    }
)

Vitest.test (
    "a surface that refuses the plain text edit keeps the dialog open with a message",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        surface.Accepts <- false
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "plant part"
        do! eventually (fun () -> RTL.screen.getByText "No IRI found for NFDI4PSO:0000064." |> ignore)
        RTL.fireEvent.click (button "Insert as plain text")
        Vitest.expect(RTL.screen.getByRole("alert").textContent).toBe "The editor did not accept the change."
        Vitest.expect(openStates.Count).toBe 0
    }
)

Vitest.test (
    "typing in the search removes the conflict message",
    fun () -> promise {
        let surface = FakeSurface(stateOf "x\n\n[MS:1000031]: http://e.org/other\n" 1 1)
        do! renderAndChoose surface (ResizeArray<bool>()) "instrument model"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        RTL.fireEvent.click (button "Insert")
        RTL.screen.getByRole "alert" |> ignore

        RTL.fireEvent.change (
            RTL.screen.getByTestId "term-search-input",
            {|
                target = {| value = "new search" |}
            |}
        )

        do! eventually (fun () -> Vitest.expect((RTL.screen.queryByRole "alert").IsNone).toBe true)
    }
)

Vitest.test (
    "an IRI that cannot be linked shows the reason and offers plain text",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)

        let urnLookup (_: string) : JS.Promise<string option> = promise { return Some "urn:x:1" }

        RTL.render (
            OntologyReferenceDialog.OntologyReferenceDialog(
                true,
                ignore,
                surface,
                lookupIri = urnLookup,
                termSearchQueries = termSearchQueries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        RTL.fireEvent.change (RTL.screen.getByTestId "term-search-input", {| target = {| value = "x" |} |})
        do! eventually (fun () -> RTL.screen.getByText "data" |> ignore)
        RTL.fireEvent.click (RTL.screen.getByText "data")
        do! eventually (fun () -> RTL.screen.getByText "The IRI urn:x:1 is not a web address." |> ignore)
        Vitest.expect(isDisabled "Insert").toBe true
        button "Insert as plain text" |> ignore
    }
)

Vitest.test (
    "a slow lookup of an earlier choice does not overwrite a later choice",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        let release = ref (fun (_: string option) -> ())

        let slowLookup (_: string) : JS.Promise<string option> =
            Promise.create (fun resolve _ -> release.Value <- resolve)

        RTL.render (
            OntologyReferenceDialog.OntologyReferenceDialog(
                true,
                ignore,
                surface,
                lookupIri = slowLookup,
                termSearchQueries = termSearchQueries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        let search (name: string) = promise {
            RTL.fireEvent.change (RTL.screen.getByTestId "term-search-input", {| target = {| value = name |} |})
            do! eventually (fun () -> RTL.screen.getByText name |> ignore)
            RTL.fireEvent.click (RTL.screen.getByText name)
        }

        do! search "data"
        do! search "instrument model"
        do! eventually (fun () -> RTL.screen.getByText "http://purl.obolibrary.org/obo/MS_1000031" |> ignore)
        release.Value(Some "http://edamontology.org/data_0006")
        do! Promise.sleep 50
        Vitest.expect((RTL.screen.queryByText "http://edamontology.org/data_0006").IsNone).toBe true
        RTL.screen.getByText "http://purl.obolibrary.org/obo/MS_1000031" |> ignore
    }
)

Vitest.test (
    "typing after a choice clears the details",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        do! renderAndChoose surface (ResizeArray<bool>()) "instrument model"
        do! eventually (fun () -> RTL.screen.getByText "CURIE" |> ignore)
        RTL.fireEvent.change (RTL.screen.getByTestId "term-search-input", {| target = {| value = "abc" |} |})
        do! eventually (fun () -> Vitest.expect((RTL.screen.queryByText "CURIE").IsNone).toBe true)

        let insert =
            RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Insert", hidden = true))

        Vitest.expect((insert :?> HTMLButtonElement).disabled).toBe true
    }
)

let private pressInSearch (key: string) =
    RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "term-search-input", {| key = key; code = key |})

Vitest.test (
    "Enter in the search inserts the chosen term once its IRI is known and gives the focus back",
    fun () -> promise {
        let state = stateOf "measured with an " 17 17
        let surface = FakeSurface(state)
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "instrument model"
        do! eventually (fun () -> Vitest.expect(isDisabled "Insert").toBe false)
        pressInSearch "Enter"

        let expected =
            OntologyReferences.insert
                (OntologyReferences.referenceOf instrumentModel "http://purl.obolibrary.org/obo/MS_1000031")
                state
            |> okEdit

        Vitest.expect(surface.Applied.ToArray()).toEqual [| expected |]
        Vitest.expect(openStates.ToArray()).toEqual [| false |]
        Vitest.expect(surface.Focused).toBe 1
    }
)

Vitest.test (
    "Enter while the IRI is still looked up inserts nothing",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)

        RTL.render (
            OntologyReferenceDialog.OntologyReferenceDialog(
                true,
                ignore,
                surface,
                lookupIri = (fun _ -> Promise.create (fun _ _ -> ())),
                termSearchQueries = termSearchQueries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        RTL.fireEvent.change (RTL.screen.getByTestId "term-search-input", {| target = {| value = "x" |} |})
        do! eventually (fun () -> RTL.screen.getByText "data" |> ignore)
        RTL.fireEvent.click (RTL.screen.getByText "data")
        do! eventually (fun () -> RTL.screen.getByText "Looking up the IRI..." |> ignore)
        pressInSearch "Enter"
        Vitest.expect(surface.Applied.Count).toBe 0
    }
)

Vitest.test (
    "the Enter that picks another hit does not insert the earlier choice",
    fun () -> promise {
        let surface = FakeSurface(stateOf "" 0 0)
        let openStates = ResizeArray<bool>()
        do! renderAndChoose surface openStates "data"
        do! eventually (fun () -> RTL.screen.getByText "http://edamontology.org/data_0006" |> ignore)

        // A double click opens the hits again for the text in the search. The first hit is instrument model.
        RTL.fireEvent.custom ("dblClick", RTL.screen.getByTestId "term-search-input")
        do! eventually (fun () -> RTL.screen.getByText "instrument model" |> ignore)
        pressInSearch "ArrowDown"
        pressInSearch "Enter"
        Vitest.expect(surface.Applied.Count).toBe 0
        Vitest.expect(openStates.Count).toBe 0

        do! eventually (fun () -> RTL.screen.getByText "http://purl.obolibrary.org/obo/MS_1000031" |> ignore)
        pressInSearch "Enter"
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0].changes.[0].insert).toContain "[instrument model][MS:1000031]"
    }
)
