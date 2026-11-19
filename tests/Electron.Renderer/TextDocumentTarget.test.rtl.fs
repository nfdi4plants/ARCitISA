module ElectronRenderer.TextDocumentTargetRtlTests

open Browser.Types
open Fable.Core
open Vitest
open Renderer.Types
open Renderer.Components.MainContent.TextDocument.TextDocumentTargetView
open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror
open Swate.Electron.Shared.FileIOTypes

type private EditorViewLookup =
    /// The view of an editor element or of its content element.
    abstract findFromDOM: HTMLElement -> EditorView

[<Import("EditorView", "@codemirror/view")>]
let private editorViewLookup: EditorViewLookup = jsNative

Vitest.afterEach (fun () -> RTL.cleanup ())

let private textPage (path: string) (fileType: FileContentType) (content: string) : TextDocumentPageData = {
    Path = path
    Content = content
    FileType = fileType
    Info =
        Some {|
            encoding = TextFileEncoding.Utf8
            contentHash = "hash"
            sizeBytes = float content.Length
            readOnly = None
        |}
}

let private markdownPage = textPage "docs/guide.md" FileContentType.Markdown

let private editorView () =
    editorViewLookup.findFromDOM (Browser.Dom.document.querySelector ".cm-content" :?> HTMLElement)

let private modeRadio (name: string) =
    RTL.screen.getByRole ("radio", ByRoleOptions(name = Text name))

Vitest.test (
    "unsaved edits survive a switch to the preview and back",
    fun () -> promise {
        RTL.render (TextDocumentTarget(markdownPage "# Guide")) |> ignore

        do!
            RTL.act (fun () -> promise {
                editorView()
                    .dispatch (
                        TransactionSpec(
                            changes =
                                box {|
                                    from = 0
                                    insert = "Unsaved line\n\n"
                                |}
                        )
                    )
            })

        RTL.fireEvent.click (modeRadio "Preview")

        // The editor stays mounted, only hidden, so the text is looked up in the preview.
        do!
            RTL.waitFor (fun () ->
                let preview = Browser.Dom.document.querySelector ".wmde-markdown-var"
                Vitest.expect(preview.textContent).toContain "Unsaved line"
            )

        RTL.fireEvent.click (modeRadio "Edit")

        Vitest.expect(editorView().state.doc.toString ()).toBe "Unsaved line\n\n# Guide"
        Vitest.expect(RTL.screen.getByText "Unsaved changes").toBeTruthy ()
    }
)

let private toolbarButton (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name)) :?> HTMLButtonElement

Vitest.test (
    "Undo and Redo in the toolbar are enabled only with a step to take and step the editor",
    fun () -> promise {
        RTL.render (TextDocumentTarget(markdownPage "# Guide")) |> ignore

        Vitest.expect((toolbarButton "Undo").disabled).toBe true
        Vitest.expect((toolbarButton "Redo").disabled).toBe true

        do!
            RTL.act (fun () -> promise {
                editorView().dispatch (TransactionSpec(changes = box {| from = 0; insert = "New " |}))
            })

        Vitest.expect((toolbarButton "Undo").disabled).toBe false

        RTL.fireEvent.click (toolbarButton "Undo")

        Vitest.expect(editorView().state.doc.toString ()).toBe "# Guide"
        Vitest.expect((toolbarButton "Undo").disabled).toBe true
        Vitest.expect((toolbarButton "Redo").disabled).toBe false

        RTL.fireEvent.click (toolbarButton "Redo")

        Vitest.expect(editorView().state.doc.toString ()).toBe "New # Guide"
    }
)

Vitest.test (
    "a YAML file gets Undo and Redo without the Markdown formatting",
    fun () -> promise {
        RTL.render (TextDocumentTarget(textPage "workflows/config.yml" FileContentType.YAML "title: Guide"))
        |> ignore

        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Bold")).IsNone).toBe true
        Vitest.expect((toolbarButton "Undo").disabled).toBe true

        do!
            RTL.act (fun () -> promise {
                editorView().dispatch (TransactionSpec(changes = box {| from = 0; insert = "# " |}))
            })

        RTL.fireEvent.click (toolbarButton "Undo")

        Vitest.expect(editorView().state.doc.toString ()).toBe "title: Guide"
        Vitest.expect((toolbarButton "Redo").disabled).toBe false
    }
)

Vitest.test (
    "an HTML file opens as it looks, in the sandboxed frame, with a bar that only closes the file",
    fun () ->
        RTL.render (TextDocumentTarget(textPage "site/index.html" FileContentType.Html "<p>Hello</p>"))
        |> ignore

        let frame = RTL.screen.getByTitle "HTML preview"
        Vitest.expect(frame.getAttribute "sandbox").toBe "allow-scripts"
        Vitest.expect(frame.getAttribute "srcdoc").toBe """<base href="about:srcdoc"><p>Hello</p>"""

        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Close file"))
        |> ignore

        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Undo")).IsNone).toBe true
)
