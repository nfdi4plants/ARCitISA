module internal Swate.Components.Composite.MarkdownText.MarkdownToolbarTests

open Browser.Types
open Vitest
open Swate.Components.Composite.TextEditor
open Swate.Components.Util
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Editing.MarkdownCommands

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
        let surface = FakeSurface(stateOf "one\ntwo" 0 7)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        RTL.fireEvent.click (button "Bold")
        Vitest.expect(surface.Applied.Count).toBe 0
        Vitest.expect(RTL.screen.getByRole("status").textContent).toBe "Select text on one line."
)

Vitest.test (
    "a refused command reports the reason through setMessage when the host owns the message",
    fun () ->
        let surface = FakeSurface(stateOf "one\ntwo" 0 7)
        let reported = ResizeArray<string option>()

        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory, setMessage = (fun m -> reported.Add m)))
        |> ignore

        RTL.fireEvent.click (button "Bold")
        Vitest.expect(reported.Count).toBe 1
        Vitest.expect(reported.[0]).toEqual (Some "Select text on one line.")
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
        Vitest.expect(RTL.screen.getByRole("status").textContent).toBe "The editor did not accept the change."
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

        Vitest.expect(reported.[0]).toEqual (Some "Select text on one line.")

        match bold.run (stateOf "hello" 0 5) with
        | Some edit -> Vitest.expect(edit.changes.Length).toBe 2
        | None -> failwith "The key must run the command."

        Vitest.expect(reported.[1]).toEqual None
)

Vitest.test (
    "the title of a command with a key shows the shortcut",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        Vitest.expect((button "Bold").getAttribute "title").toBe "Bold (Ctrl+B)"
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

        Vitest.expect((button "Strikethrough").getAttribute "title").toBe "Strikethrough (Ctrl+Shift+X)"
        Vitest.expect((button "Quote").getAttribute "title").toBe "Quote (Ctrl+Shift+.)"

        Vitest.expect((button "Ontology reference").getAttribute "title").toBe "Ontology reference (Ctrl+Shift+O)"
)

Vitest.test (
    "the heading menu and its items show the keys of the headings",
    fun () ->
        let surface = FakeSurface(stateOf "hello" 0 5)
        RTL.render (MarkdownToolbar.MarkdownToolbar(surface, noHistory)) |> ignore
        Vitest.expect((button "Heading").getAttribute "title").toBe "Heading (Ctrl+1, Ctrl+2, Ctrl+3)"
        RTL.fireEvent.click (button "Heading")
        Vitest.expect((button "Heading 2").getAttribute "title").toBe "Heading 2 (Ctrl+2)"
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
        Vitest.expect((RTL.screen.queryByText "Ontology reference").IsNone).toBe true
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

        RTL.screen.getByText "Ontology reference" |> ignore
)

Vitest.test (
    "the Ontology reference button opens the dialog and is disabled with the toolbar",
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
                disabled = true,
                termSearchQueries = queries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        Vitest.expect((button "Ontology reference" :?> HTMLButtonElement).disabled).toBe true
        RTL.cleanup ()

        RTL.render (
            MarkdownToolbar.MarkdownToolbar(
                surface,
                noHistory,
                termSearchQueries = queries,
                disableDefaultSearch = true
            )
        )
        |> ignore

        RTL.fireEvent.click (button "Ontology reference")
        Vitest.expect(RTL.screen.getByText("Ontology reference").textContent).toBe "Ontology reference"
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
