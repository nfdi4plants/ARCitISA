module internal Swate.Components.Composite.TextEditor.TextEditorTests

open Fable.Core
open Vitest
open Swate.Components.Composite.TextEditor.Types

let private diagnostic (message: string) : LintDiagnostic = {|
    from = 0
    ``to`` = 1
    severity = "error"
    message = message
    source = "test"
|}

let private messages (found: LintDiagnostic[]) = found |> Array.map (fun d -> d.message)

let private sync (_: string) = [| diagnostic "sync" |]

let private extra (_: string) = promise { return [| diagnostic "extra" |] }

Vitest.describe (
    "TextEditorHelpers.combineLint",
    fun () ->
        Vitest.test (
            "gives no lint when both are missing",
            fun () -> Vitest.expect(TextEditorHelpers.combineLint None None |> Option.isNone).toBe true
        )

        Vitest.test (
            "gives the sync result when only sync exists",
            fun () -> promise {
                let lint = (TextEditorHelpers.combineLint (Some sync) None).Value
                let! found = lint "text"
                Vitest.expect(messages found).toEqual [| "sync" |]
            }
        )

        Vitest.test (
            "gives the extra result when only extra exists",
            fun () -> promise {
                let lint = (TextEditorHelpers.combineLint None (Some extra)).Value
                let! found = lint "text"
                Vitest.expect(messages found).toEqual [| "extra" |]
            }
        )

        Vitest.test (
            "puts the sync results before the extra results",
            fun () -> promise {
                let lint = (TextEditorHelpers.combineLint (Some sync) (Some extra)).Value
                let! found = lint "text"
                Vitest.expect(messages found).toEqual [| "sync"; "extra" |]
            }
        )
)
