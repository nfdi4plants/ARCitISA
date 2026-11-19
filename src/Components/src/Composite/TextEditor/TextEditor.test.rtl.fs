module internal Swate.Components.Composite.TextEditor.TextEditorRtlTests

open Fable.Core
open Feliz
open Vitest
open Swate.Components.Composite.TextEditor.Types

type private HandleBox() =
    member val Value: TextEditorHandle option = None with get, set

    interface IRefValue<TextEditorHandle option> with
        member this.current
            with get () = this.Value
            and set v = this.Value <- v

let private diagnostic (message: string) : LintDiagnostic = {|
    from = 0
    ``to`` = 1
    severity = "error"
    message = message
    source = "test"
|}

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "the result of a lint that a newer change made stale never reaches onDiagnostics",
    fun () -> promise {
        let resolvers = ResizeArray<LintDiagnostic[] -> unit>()

        let extraLint (_: string) =
            Promise.create (fun resolve _ -> resolvers.Add resolve)

        let reported = ResizeArray<string>()
        let handle = HandleBox()

        RTL.render (
            TextEditor.TextEditor(
                "notes.md",
                "# first",
                extraLint = extraLint,
                onDiagnostics =
                    (fun found ->
                        for d in found do
                            reported.Add d.message
                    ),
                handleRef = handle
            )
        )
        |> ignore

        do! RTL.waitFor (fun () -> Vitest.expect(resolvers.Count).toBe 1)
        handle.Value.Value.replaceContent "# second"
        resolvers.[0] [| diagnostic "stale" |]

        // The change schedules a second lint, whose result is the current one.
        do! RTL.waitFor (fun () -> Vitest.expect(resolvers.Count).toBe 2)
        resolvers.[1] [| diagnostic "current" |]
        do! RTL.waitFor (fun () -> Vitest.expect(reported).toEqual (ResizeArray [ "current" ]))
    }
)

Vitest.test (
    "a Markdown editor turns the spellcheck off on an HTML tag and keeps it on for the prose",
    fun () -> promise {
        let result =
            RTL.render (TextEditor.TextEditor("notes.md", "Some prose <img src=\"a.png\"> more prose"))

        let content = result.container.querySelector ".cm-content"
        Vitest.expect(content.getAttribute "spellcheck").toBe "true"

        // Highlighting can split a mark into several elements, so this joins the text of the outermost ones.
        let skippedText () =
            let marked = content.querySelectorAll "[spellcheck=false]"

            [
                for i in 0 .. int marked.length - 1 do
                    let element = marked.item i

                    if (element.parentElement.closest "[spellcheck=false]").IsNone then
                        element.textContent
            ]
            |> String.concat ""

        // The Markdown language loads on demand, and the tag gets its mark once the tree is there.
        do! RTL.waitFor (fun () -> Vitest.expect(skippedText ()).toBe "<img src=\"a.png\">")
    }
)

let private appendAt (offset: int) : TextEdit = {|
    changes = [|
        {|
            from = offset
            ``to`` = offset
            insert = "!"
        |}
    |]
    selection = {|
        anchor = offset + 1
        head = offset + 1
    |}
|}

Vitest.test (
    "a later render's readOnly and callbacks reach the editor, also after replaceContent",
    fun () -> promise {
        let handle = HandleBox()
        let first = ResizeArray<bool>()
        let second = ResizeArray<bool>()

        let editor (readOnly: bool) (onDirtyChange: bool -> unit) =
            TextEditor.TextEditor(
                "notes.md",
                "abc",
                readOnly = readOnly,
                onDirtyChange = onDirtyChange,
                handleRef = handle
            )

        let result = RTL.render (editor false first.Add)
        result.rerender (editor false second.Add)
        let mounted = handle.Value.Value

        Vitest.expect(mounted.applyEdit (appendAt 3)).toBe true
        Vitest.expect(first.Count).toBe 0
        Vitest.expect(second.ToArray()).toEqual [| true |]

        result.rerender (editor true second.Add)
        Vitest.expect(mounted.applyEdit (appendAt 4)).toBe false

        // replaceContent builds a new editor state. It must take the latest props, not the ones of the first render.
        mounted.replaceContent "new"
        Vitest.expect(mounted.applyEdit (appendAt 3)).toBe false
        Vitest.expect(mounted.getText ()).toBe "new"
        Vitest.expect(obj.ReferenceEquals(handle.Value.Value, mounted)).toBe true
    }
)

Vitest.test (
    "the aria label follows the file name, also after replaceContent",
    fun () -> promise {
        let handle = HandleBox()

        let editor (fileName: string) =
            TextEditor.TextEditor("inmemory://doc", "abc", fileName = fileName, handleRef = handle)

        let result = RTL.render (editor "first.md")

        let content () =
            result.container.querySelector ".cm-content"

        Vitest.expect(content().getAttribute "aria-label").toBe "Text editor for first.md"

        result.rerender (editor "second.md")
        Vitest.expect(content().getAttribute "aria-label").toBe "Text editor for second.md"

        handle.Value.Value.replaceContent "reloaded"
        Vitest.expect(content().getAttribute "aria-label").toBe "Text editor for second.md"
    }
)

Vitest.test (
    "onHistoryChange follows an edit, its undo and redo, and a replaced content",
    fun () -> promise {
        let reported = ResizeArray<TextEditorHistory>()
        let handle = HandleBox()

        RTL.render (TextEditor.TextEditor("notes.md", "abc", onHistoryChange = reported.Add, handleRef = handle))
        |> ignore

        let editor = handle.Value.Value

        let edit: TextEdit = {|
            changes = [| {| from = 3; ``to`` = 3; insert = "d" |} |]
            selection = {| anchor = 4; head = 4 |}
        |}

        Vitest.expect(editor.undo ()).toBe false
        Vitest.expect(editor.applyEdit edit).toBe true
        Vitest.expect(editor.undo ()).toBe true
        Vitest.expect(editor.getText ()).toBe "abc"
        Vitest.expect(editor.redo ()).toBe true
        Vitest.expect(editor.getText ()).toBe "abcd"
        editor.replaceContent "new"

        Vitest.expect(reported.ToArray()).toEqual [|
            {| canUndo = true; canRedo = false |}
            {| canUndo = false; canRedo = true |}
            {| canUndo = true; canRedo = false |}
            {| canUndo = false; canRedo = false |}
        |]
    }
)
