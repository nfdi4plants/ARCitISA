module internal Arcitisa.Components.Composite.MarkdownText.MarkdownTextEditorRtlTests

open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.MarkdownText

type private EditorViewLookup =
    /// The view of an editor element or of its content element.
    abstract findFromDOM: HTMLElement -> EditorView

[<Import("EditorView", "@codemirror/view")>]
let private editorViewLookup: EditorViewLookup = jsNative

let private editorView () =
    editorViewLookup.findFromDOM (Browser.Dom.document.querySelector ".cm-content" :?> HTMLElement)

let private insertAt (position: int) (text: string) =
    RTL.act (fun () -> promise {
        editorView().dispatch (TransactionSpec(changes = box {| from = position; insert = text |}))
    })

let private button (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name)) :?> HTMLButtonElement

[<Erase; Mangle(false)>]
type private MarkdownTextEditorHosts =

    /// Shows the value the host holds and can set it to "second".
    [<ReactComponent>]
    static member Resettable() =
        let value, setValue = React.useState "first"

        Html.div [
            Html.output [ prop.testId "host-value"; prop.text value ]
            Html.button [
                prop.type'.button
                prop.text "Reset"
                prop.onClick (fun _ -> setValue "second")
            ]
            MarkdownTextEditor.MarkdownTextEditor(value, setValue, 200)
        ]

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "an edit reaches setValue once the typing pauses",
    fun () -> promise {
        let received = ResizeArray<string>()

        RTL.render (MarkdownTextEditor.MarkdownTextEditor("# Notes", received.Add, 200))
        |> ignore

        do! insertAt 7 "!"

        do! RTL.waitFor (fun () -> Vitest.expect(received).toEqual (ResizeArray [ "# Notes!" ]))
    }
)

Vitest.test (
    "the echo of its own text keeps the undo history, and a new value from the host replaces the text",
    fun () -> promise {
        RTL.render (MarkdownTextEditorHosts.Resettable()) |> ignore

        do! insertAt 5 " edit"
        do! RTL.waitFor (fun () -> Vitest.expect(RTL.screen.getByTestId("host-value").textContent).toBe "first edit")
        Vitest.expect(editorView().state.doc.toString ()).toBe "first edit"
        Vitest.expect((button "Undo").disabled).toBe false

        RTL.fireEvent.click (button "Reset")

        do! RTL.waitFor (fun () -> Vitest.expect(editorView().state.doc.toString ()).toBe "second")
        Vitest.expect((button "Undo").disabled).toBe true
    }
)

Vitest.test (
    "a text the validator refuses stays with the field and shows the reason",
    fun () -> promise {
        let received = ResizeArray<string>()

        let validator (text: string) =
            if text.Contains "!" then
                Error "No exclamation marks."
            else
                Ok()

        RTL.render (MarkdownTextEditor.MarkdownTextEditor("# Notes", received.Add, 200, validator = validator))
        |> ignore

        do! insertAt 7 "!"

        do! RTL.waitFor (fun () -> Vitest.expect(RTL.screen.getByText "No exclamation marks.").toBeTruthy ())
        Vitest.expect(received.Count).toBe 0
    }
)

Vitest.test (
    "an empty field shows the placeholder",
    fun () -> promise {
        RTL.render (MarkdownTextEditor.MarkdownTextEditor("", ignore, 200, placeholder = "Write note markdown..."))
        |> ignore

        do! RTL.waitFor (fun () -> Vitest.expect(RTL.screen.getByText "Write note markdown...").toBeTruthy ())
    }
)

Vitest.test (
    "the preview disables the toolbar and keeps the edits of the hidden editor",
    fun () -> promise {
        RTL.render (MarkdownTextEditor.MarkdownTextEditor("# Notes", ignore, 200, mode = Types.PreviewMode.Edit))
        |> ignore

        do! insertAt 7 " and more"
        RTL.fireEvent.click (button "Preview")

        do!
            RTL.waitFor (fun () ->
                Vitest
                    .expect(RTL.screen.getByRole ("heading", ByRoleOptions(name = Text "Notes and more")))
                    .toBeTruthy ()
            )

        Vitest.expect((button "Bold").disabled).toBe true

        RTL.fireEvent.click (button "Edit")

        Vitest.expect((button "Bold").disabled).toBe false
        Vitest.expect(editorView().state.doc.toString ()).toBe "# Notes and more"
    }
)
