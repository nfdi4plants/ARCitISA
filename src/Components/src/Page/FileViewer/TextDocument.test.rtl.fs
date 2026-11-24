module internal Swate.Components.Page.FileViewer.TextDocumentRtlTests

open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror
open Swate.Components.Page.FileViewer
open Swate.Components.Page.FileViewer.Types

type private EditorViewLookup =
    /// The view of an editor element or of its content element.
    abstract findFromDOM: HTMLElement -> EditorView

[<Import("EditorView", "@codemirror/view")>]
let private editorViewLookup: EditorViewLookup = jsNative

type private HandleBox() =
    member val Value: TextDocumentHandle option = None with get, set

    interface IRefValue<TextDocumentHandle option> with
        member this.current
            with get () = this.Value
            and set v = this.Value <- v

[<Erase; Mangle(false)>]
type private TextDocumentHosts =

    /// Shows "Unsaved changes" while the editor holds edits, like a host does. Saves always succeed.
    [<ReactComponent>]
    static member Tracking
        (path: string, text: string, ?rendering: TextRendering, ?notices: TextDocumentNotice list, ?handle: HandleBox)
        =
        let dirty, setDirty = React.useState false

        TextDocument.TextDocument(
            path,
            "inmemory://tests/" + path,
            text,
            (if dirty then "Unsaved changes" else "Saved"),
            (fun _ -> Promise.lift true),
            ?rendering = rendering,
            dirty = dirty,
            ?notices = notices,
            onDirtyChange = setDirty,
            onClose = ignore,
            ?handleRef = (handle |> Option.map (fun h -> h :> IRefValue<TextDocumentHandle option>))
        )

Vitest.afterEach (fun () -> RTL.cleanup ())

let private editorView () =
    editorViewLookup.findFromDOM (Browser.Dom.document.querySelector ".cm-content" :?> HTMLElement)

let private insertAt (position: int) (text: string) =
    RTL.act (fun () -> promise {
        editorView().dispatch (TransactionSpec(changes = box {| from = position; insert = text |}))
    })

let private modeRadio (name: string) =
    RTL.screen.getByRole ("radio", ByRoleOptions(name = Text name))

let private button (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name)) :?> HTMLButtonElement

let private markdown (text: string) =
    TextDocumentHosts.Tracking("docs/guide.md", text, rendering = TextRendering.Markdown)

Vitest.test (
    "unsaved edits survive a switch to the preview and back",
    fun () -> promise {
        RTL.render (markdown "# Guide") |> ignore

        do! insertAt 0 "Unsaved line\n\n"
        RTL.fireEvent.click (modeRadio "Preview")

        // The editor stays mounted, only hidden, so the text is looked up in the preview, next to its heading.
        do!
            RTL.waitFor (fun () ->
                let heading = RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Guide"))
                Vitest.expect(heading.parentElement.textContent).toContain "Unsaved line"
            )

        RTL.fireEvent.click (modeRadio "Edit")

        Vitest.expect(editorView().state.doc.toString ()).toBe "Unsaved line\n\n# Guide"
        Vitest.expect(RTL.screen.getByText "Unsaved changes").toBeTruthy ()
    }
)

Vitest.test (
    "Undo and Redo in the toolbar are enabled only with a step to take and step the editor",
    fun () -> promise {
        RTL.render (markdown "# Guide") |> ignore

        Vitest.expect((button "Undo").disabled).toBe true
        Vitest.expect((button "Redo").disabled).toBe true

        do! insertAt 0 "New "

        Vitest.expect((button "Undo").disabled).toBe false

        RTL.fireEvent.click (button "Undo")

        Vitest.expect(editorView().state.doc.toString ()).toBe "# Guide"
        Vitest.expect((button "Undo").disabled).toBe true
        Vitest.expect((button "Redo").disabled).toBe false

        RTL.fireEvent.click (button "Redo")

        Vitest.expect(editorView().state.doc.toString ()).toBe "New # Guide"
    }
)

Vitest.test (
    "a YAML file gets Undo and Redo without the Markdown formatting",
    fun () -> promise {
        RTL.render (TextDocumentHosts.Tracking("workflows/config.yml", "title: Guide"))
        |> ignore

        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Bold")).IsNone).toBe true
        Vitest.expect((button "Undo").disabled).toBe true

        do! insertAt 0 "# "
        RTL.fireEvent.click (button "Undo")

        Vitest.expect(editorView().state.doc.toString ()).toBe "title: Guide"
        Vitest.expect((button "Redo").disabled).toBe false
    }
)

Vitest.test (
    "an HTML file opens as it looks, in the sandboxed frame, with a bar that only closes the file",
    fun () ->
        RTL.render (TextDocumentHosts.Tracking("site/index.html", "<p>Hello</p>", rendering = TextRendering.Html))
        |> ignore

        let frame = RTL.screen.getByTitle "HTML preview"
        Vitest.expect(frame.getAttribute "sandbox").toBe "allow-scripts"
        Vitest.expect(frame.getAttribute "srcdoc").toBe """<base href="about:srcdoc"><p>Hello</p>"""

        button "Close file" |> ignore
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Undo")).IsNone).toBe true
)

Vitest.test (
    "replaceText changes the open preview and the editor behind it, and the new text counts as saved",
    fun () -> promise {
        let handle = HandleBox()

        RTL.render (
            TextDocumentHosts.Tracking("docs/guide.md", "# Guide", rendering = TextRendering.Markdown, handle = handle)
        )
        |> ignore

        do! insertAt 7 " with edits"
        RTL.fireEvent.click (modeRadio "Preview")

        do!
            RTL.waitFor (fun () ->
                RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Guide with edits"))
                |> ignore
            )

        do! RTL.act (fun () -> promise { handle.Value.Value.replaceText "# Reloaded" })

        do!
            RTL.waitFor (fun () ->
                RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Reloaded"))
                |> ignore
            )

        Vitest.expect(handle.Value.Value.isDirty ()).toBe false
        Vitest.expect(RTL.screen.getByText "Saved").toBeTruthy ()

        RTL.fireEvent.click (modeRadio "Edit")
        Vitest.expect(editorView().state.doc.toString ()).toBe "# Reloaded"
    }
)

Vitest.test (
    "replaceText before the editor exists changes the rendered side",
    fun () -> promise {
        let handle = HandleBox()

        RTL.render (
            TextDocumentHosts.Tracking(
                "site/index.html",
                "<p>Hello</p>",
                rendering = TextRendering.Html,
                handle = handle
            )
        )
        |> ignore

        Vitest.expect(Browser.Dom.document.querySelector ".cm-content").toBeNull ()

        do! RTL.act (fun () -> promise { handle.Value.Value.replaceText "<p>Bye</p>" })

        Vitest.expect((RTL.screen.getByTitle "HTML preview").getAttribute "srcdoc").toContain "<p>Bye</p>"
    }
)

Vitest.test (
    "a notice of the host shows its message and runs its buttons",
    fun () ->
        let reloads = ref 0

        let notice = {
            Key = "disk"
            Message = "The file changed on disk since you opened it."
            Actions = [
                {
                    Label = "Reload from disk"
                    Run = (fun () -> reloads.Value <- reloads.Value + 1)
                    Disabled = false
                }
                {
                    Label = "Overwrite"
                    Run = ignore
                    Disabled = true
                }
            ]
        }

        RTL.render (
            TextDocumentHosts.Tracking(
                "docs/guide.md",
                "# Guide",
                rendering = TextRendering.Markdown,
                notices = [ notice ]
            )
        )
        |> ignore

        Vitest.expect(RTL.screen.getByRole("alert").textContent).toContain "The file changed on disk"
        Vitest.expect((button "Overwrite").disabled).toBe true

        RTL.fireEvent.click (button "Reload from disk")
        Vitest.expect(reloads.Value).toBe 1
)
