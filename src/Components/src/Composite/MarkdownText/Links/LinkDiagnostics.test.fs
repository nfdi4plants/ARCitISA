module internal Swate.Components.Composite.MarkdownText.Links.LinkDiagnosticsTests

open Fable.Core
open Vitest
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types

/// A fake host that answers with the given state for every path and records each call.
let private fakeHost (stateOf: string -> LinkTargetState) =
    let calls = ResizeArray<string[]>()

    let check (paths: string[]) : JS.Promise<LinkTargetState[]> =
        calls.Add paths
        Promise.lift (paths |> Array.map stateOf)

    calls, check

let private failingHost (calls: ResizeArray<string[]>) (paths: string[]) : JS.Promise<LinkTargetState[]> =
    calls.Add paths
    Promise.reject (exn "host offline")

Vitest.describe (
    "LinkDiagnostics.lint",
    fun () ->
        Vitest.test (
            "reports a missing target as an error on the range of the link",
            fun () -> promise {
                let calls, check = fakeHost (fun _ -> LinkTargetState.Missing)
                let! found = LinkDiagnostics.lint "docs" check "see [a](gone.md)"
                Vitest.expect(found.Length).toBe 1
                Vitest.expect(found.[0].severity).toBe "error"
                Vitest.expect(found.[0].message).toBe "Not found: docs/gone.md"
                Vitest.expect(found.[0].from).toBe 4
                Vitest.expect(found.[0].``to``).toBe 16
                Vitest.expect(found.[0].source).toBe "links"
                Vitest.expect(calls).toEqual (ResizeArray [ [| "docs/gone.md" |] ])
            }
        )

        Vitest.test (
            "reports a case mismatch as a warning with the path on disk",
            fun () -> promise {
                let _, check = fakeHost (fun _ -> LinkTargetState.CaseDiffers "docs/Read.md")
                let! found = LinkDiagnostics.lint "docs" check "[a](read.md)"
                Vitest.expect(found.Length).toBe 1
                Vitest.expect(found.[0].severity).toBe "warning"
                Vitest.expect(found.[0].message).toContain "docs/Read.md"
            }
        )

        Vitest.test (
            "reports an unsupported scheme as an error without asking the host",
            fun () -> promise {
                let calls, check = fakeHost (fun _ -> LinkTargetState.Exists)
                let! found = LinkDiagnostics.lint "docs" check "[x](javascript:alert(1))"
                Vitest.expect(found.Length).toBe 1
                Vitest.expect(found.[0].severity).toBe "error"
                Vitest.expect(calls.Count).toBe 0
            }
        )

        Vitest.test (
            "reports a path above the ARC root as an error",
            fun () -> promise {
                let _, check = fakeHost (fun _ -> LinkTargetState.Exists)
                let! found = LinkDiagnostics.lint "docs" check "[x](../../x.md)"
                Vitest.expect(found.Length).toBe 1
                Vitest.expect(found.[0].message).toBe "This link points outside the ARC."
            }
        )

        Vitest.test (
            "asks the host once for two links to the same path",
            fun () -> promise {
                let calls, check = fakeHost (fun _ -> LinkTargetState.Missing)
                let! found = LinkDiagnostics.lint "" check "[a](x.md) [b](x.md)"
                Vitest.expect(calls.Count).toBe 1
                Vitest.expect(calls.[0]).toEqual [| "x.md" |]
                Vitest.expect(found.Length).toBe 2
            }
        )

        Vitest.test (
            "gives external and fragment links no diagnostic and no host call",
            fun () -> promise {
                let calls, check = fakeHost (fun _ -> LinkTargetState.Missing)
                let! found = LinkDiagnostics.lint "" check "[a](https://example.org/) [b](#top)"
                Vitest.expect(found.Length).toBe 0
                Vitest.expect(calls.Count).toBe 0
            }
        )

        Vitest.test (
            "keeps the unchecked diagnostics and adds a warning when the host fails",
            fun () -> promise {
                let calls = ResizeArray<string[]>()
                let! found = LinkDiagnostics.lint "" (failingHost calls) "[a](x.md) [b](javascript:alert(1))"
                Vitest.expect(found.Length).toBe 2
                Vitest.expect(found |> Array.exists (fun d -> d.severity = "error")).toBe true

                let warning = found |> Array.find (fun d -> d.severity = "warning")
                Vitest.expect(warning.from).toBe 0
                Vitest.expect(warning.``to``).toBe 0
                Vitest.expect(warning.message).toContain "could not be checked"
            }
        )

        Vitest.test (
            "checks at most 500 distinct paths",
            fun () -> promise {
                let calls, check = fakeHost (fun _ -> LinkTargetState.Exists)
                let text = [| for i in 1..501 -> $"[a](f{i}.md)" |] |> String.concat "\n\n"
                let! _ = LinkDiagnostics.lint "" check text
                Vitest.expect(calls.[0].Length).toBe 500
            }
        )
)
