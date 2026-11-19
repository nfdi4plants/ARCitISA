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
