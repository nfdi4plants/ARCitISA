module internal Arcitisa.Components.Composite.MarkdownText.MarkdownToolbarTests

open Browser.Types
open Vitest
open Arcitisa.Components.Composite.TextEditor
open Arcitisa.Components.Util
open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.TermSearch.Types
open Arcitisa.Components.Composite.MarkdownText
open Arcitisa.Components.Composite.MarkdownText.Editing
open Arcitisa.Components.Composite.MarkdownText.Editing.Types
open Arcitisa.Components.Composite.MarkdownText.Editing.MarkdownCommands

type private FakeSurface(state: TextEditState) =
    member val Applied = ResizeArray<TextEdit>()
    member val Focused = 0 with get, set
    member val Accepts = true with get, set
    member val Steps = ResizeArray<string>()

    interface MarkdownSurface with
        member _.getState() = state

        member this.apply edit =
            this.Applied.Add edit
            this.Accepts

        member this.undo() =
            this.Steps.Add "undo"
            true

        member this.redo() =
            this.Steps.Add "redo"
            true

        member this.focus() = this.Focused <- this.Focused + 1

let private stateOf (text: string) (anchor: int) (head: int) : TextEditState = {|
    text = text
    selection = {| anchor = anchor; head = head |}
|}

let private history canUndo canRedo : TextEditorHistory = {|
    canUndo = canUndo
    canRedo = canRedo
|}

let private noHistory = history false false

let private button (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))

let private okEdit (result: Result<TextEdit, string>) =
    match result with
    | Ok edit -> edit
    | Error reason -> failwith reason

/// The reason of a command that refuses the state.
let private reasonOf (result: Result<TextEdit, string>) =
    match result with
    | Error reason -> reason
    | Ok _ -> failwith "The command must refuse the state."

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "a disabled toolbar disables every button and shows the reason",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)

        RTL.render (
            MarkdownToolbar.MarkdownToolbar(
                surface,
                noHistory,
                disabled = true,
                disabledReason = "This file is write-protected."
            )
        )
        |> ignore

        let buttons = RTL.screen.getAllByRole "button"
        Vitest.expect(buttons.Count > 0).toBe true

        for b in buttons do
            Vitest.expect((b :?> HTMLButtonElement).disabled).toBe true
            Vitest.expect(b.getAttribute "title").toBe "This file is write-protected."
)

Vitest.test (
    "a click on Bold applies the edit of toggleBold and focuses the surface",
    fun () ->
        let state = stateOf "hello" 0 5
        let surface = FakeSurface(state)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        RTL.fireEvent.click (button "Bold")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0]).toEqual (okEdit (MarkdownEdits.toggleBold state))
        Vitest.expect(surface.Focused).toBe 1
)

Vitest.test (
    "a click on Image applies the edit of insertImage",
    fun () ->
        let state = stateOf "hello" 0 5
        let surface = FakeSurface(state)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        RTL.fireEvent.click (button "Image")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0]).toEqual (okEdit (MarkdownEdits.insertImage state))
)

Vitest.test (
    "choosing Heading 2 applies setHeading 2",
    fun () ->
        let state = stateOf "hello" 0 5
        let surface = FakeSurface(state)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        RTL.fireEvent.click (button "Heading")
        RTL.fireEvent.click (button "Heading 2")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(surface.Applied.[0]).toEqual (okEdit (MarkdownEdits.setHeading 2 state))
)

Vitest.test (
    "a refused command applies nothing and shows the reason",
    fun () ->
        let state = stateOf "one\ntwo" 0 7
        let surface = FakeSurface(state)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        RTL.fireEvent.click (button "Bold")
        Vitest.expect(surface.Applied.Count).toBe 0
        Vitest.expect(RTL.screen.getByRole("status").textContent).toBe (reasonOf (MarkdownEdits.toggleBold state))
)

Vitest.test (
    "a refused command reports the reason through setMessage when the host owns the message",
    fun () ->
        let state = stateOf "one\ntwo" 0 7
        let surface = FakeSurface(state)
        let reported = ResizeArray<string option>()

        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory, setMessage = (fun m -> reported.Add m)))
        |> ignore

        RTL.fireEvent.click (button "Bold")
        Vitest.expect(reported.Count).toBe 1
        Vitest.expect(reported.[0]).toEqual (Some(reasonOf (MarkdownEdits.toggleBold state)))
)

Vitest.test (
    "the toolbar shows the message of the host",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)

        RTL.render (
            MarkdownToolbar.MarkdownToolbar(surface, noHistory, message = "Refused by key.", setMessage = ignore)
        )
        |> ignore

        Vitest.expect(RTL.screen.getByRole("status").textContent).toBe "Refused by key."
)

Vitest.test (
    "a surface that does not accept the edit shows a message",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)
        surface.Accepts <- false
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        RTL.fireEvent.click (button "Bold")
        Vitest.expect(surface.Applied.Count).toBe 1
        Vitest.expect(RTL.screen.queryByRole("status").IsSome).toBe true
)

Vitest.test (
    "keyBindingsWith reports the reason of a refused key and changes nothing",
    fun () ->
        let reported = ResizeArray<string option>()
        let bindings = keyBindingsWith (fun m -> reported.Add m) ignore
        let bold = bindings |> Array.find (fun b -> b.key = "Mod-b")

        let refused = stateOf "one\ntwo" 0 7

        match bold.run refused with
        | Some edit ->
            Vitest.expect(edit.changes.Length).toBe 0
            Vitest.expect(edit.selection).toEqual refused.selection
        | None -> failwith "The key must be consumed."

        Vitest.expect(reported.[0]).toEqual (Some(reasonOf (MarkdownEdits.toggleBold refused)))

        match bold.run (stateOf "hello" 0 5) with
        | Some edit -> Vitest.expect(edit.changes.Length).toBe 2
        | None -> failwith "The key must run the command."

        Vitest.expect(reported.[1]).toEqual None
)

Vitest.test (
    "every command and the ontology reference have a key of their own, and every button title shows it",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        let isMac = Shortcuts.isMac ()

        let keys = [|
            for cmd in all do
                match cmd.Key with
                | Some key -> key
                | None -> failwith $"{cmd.Label} has no key."
            ontologyReferenceKey
        |]

        Vitest.expect((Array.distinct keys).Length).toBe keys.Length

        for cmd in Array.concat groups do
            Vitest.expect((button cmd.Label).getAttribute "title").toBe (Shortcuts.title isMac cmd.Label cmd.Key.Value)

        Vitest
            .expect((button "Ontology reference").getAttribute "title")
            .toBe (Shortcuts.title isMac "Ontology reference" ontologyReferenceKey)
)

Vitest.test (
    "the heading menu and its items show the keys of the headings",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        let isMac = Shortcuts.isMac ()
        let menuTitle = (button "Heading").getAttribute "title"

        for cmd in headings do
            Vitest.expect(menuTitle).toContain (Shortcuts.label isMac cmd.Key.Value)

        RTL.fireEvent.click (button "Heading")

        for cmd in headings do
            Vitest.expect((button cmd.Label).getAttribute "title").toBe (Shortcuts.title isMac cmd.Label cmd.Key.Value)
)

Vitest.test (
    "a heading key applies setHeading",
    fun () ->
        let state = stateOf "hello" 0 5
        let bindings = keyBindingsWith ignore ignore
        let heading2 = bindings |> Array.find (fun b -> b.key = "Mod-2")
        Vitest.expect(heading2.run state).toEqual (Some(okEdit (MarkdownEdits.setHeading 2 state)))
)

Vitest.test (
    "the ontology reference key opens the dialog through openOntologyReference and changes nothing",
    fun () ->
        let opened = ref 0
        let bindings = keyBindingsWith ignore (fun () -> opened.Value <- opened.Value + 1)
        let reference = bindings |> Array.find (fun b -> b.key = ontologyReferenceKey)
        let state = stateOf "hello" 2 2

        match reference.run state with
        | Some edit ->
            Vitest.expect(edit.changes.Length).toBe 0
            Vitest.expect(edit.selection).toEqual state.selection
        | None -> failwith "The key must be consumed."

        Vitest.expect(opened.Value).toBe 1
)

Vitest.test (
    "a host that owns the dialog state opens the dialog and hears the button",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 0)
        let requested = ResizeArray<bool>()

        let queries =
            ResizeArray<string * SearchCall>(
                [
                    "stub", fun (_: string) -> promise { return ResizeArray<Term>() }
                ]
            )

        RTL.render (
            MarkdownToolbar.MarkdownToolbar(
                surface,
                noHistory,
                isOntologyReferenceOpen = false,
                setOntologyReferenceOpen = requested.Add,
                termSearchQueries = queries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        RTL.fireEvent.click (button "Ontology reference")
        Vitest.expect(requested.ToArray()).toEqual [| true |]
        // The host did not open it, so the dialog stays closed.
        Vitest.expect((RTL.screen.queryByRole "dialog").IsNone).toBe true
        RTL.cleanup ()

        RTL.render (
            MarkdownToolbar.MarkdownToolbar(
                surface,
                noHistory,
                isOntologyReferenceOpen = true,
                setOntologyReferenceOpen = requested.Add,
                termSearchQueries = queries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        Vitest.expect((RTL.screen.queryByRole "dialog").IsSome).toBe true
)

Vitest.test (
    "the Ontology reference button opens the dialog",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 0)

        let queries =
            ResizeArray<string * SearchCall>(
                [
                    "stub", fun (_: string) -> promise { return ResizeArray<Term>() }
                ]
            )

        RTL.render (
            MarkdownToolbar.MarkdownToolbar(
                surface,
                noHistory,
                termSearchQueries = queries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        Vitest.expect((RTL.screen.queryByRole "dialog").IsNone).toBe true
        RTL.fireEvent.click (button "Ontology reference")
        Vitest.expect((RTL.screen.queryByRole "dialog").IsSome).toBe true
)

Vitest.test (
    "Shortcuts.label writes the command symbol on a Mac and keeps the other modifiers",
    fun () ->
        Vitest.expect(Shortcuts.label true "Mod-b").toBe "⌘+B"
        Vitest.expect(Shortcuts.label false "Mod-Shift-o").toBe "Ctrl+Shift+O"
        Vitest.expect(Shortcuts.title true "Save" Shortcuts.saveKey).toBe "Save (⌘+S)"
)

Vitest.test (
    "Undo and Redo follow the history of the host",
    fun () ->
        RTL.render (MarkdownToolbar.MarkdownToolbar(FakeSurface(stateOf "hello" 0 0), history true false))
        |> ignore

        Vitest.expect((button "Undo" :?> HTMLButtonElement).disabled).toBe false
        Vitest.expect((button "Redo" :?> HTMLButtonElement).disabled).toBe true
)

Vitest.test (
    "Undo and Redo step the surface, clear the message and give the focus back to it",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)
        surface.Accepts <- false

        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, history true true))
        |> ignore

        RTL.fireEvent.click (button "Bold")
        Vitest.expect(RTL.screen.queryByRole("status").IsSome).toBe true
        RTL.fireEvent.click (button "Undo")
        Vitest.expect(RTL.screen.queryByRole("status").IsNone).toBe true
        RTL.fireEvent.click (button "Redo")

        Vitest.expect(surface.Steps.ToArray()).toEqual [| "undo"; "redo" |]
        // Bold, Undo and Redo each give the focus back.
        Vitest.expect(surface.Focused).toBe 3
)
