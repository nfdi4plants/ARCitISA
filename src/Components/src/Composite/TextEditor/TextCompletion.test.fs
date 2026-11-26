module internal Arcitisa.Components.Composite.TextEditor.TextCompletionTests

open Fable.Core
open Vitest
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.JsBindings.Autocomplete
open Arcitisa.Components.Composite.TextEditor.Types

let private stateWith (doc: string) (extension: Extension) =
    EditorStateApi.create {| doc = doc; extensions = extension |}

/// Asks the sources at pos the way the autocompletion does: from the language data of the state.
let private complete (state: EditorState) (pos: int) : JS.Promise<CompletionResult option[]> =
    state.languageDataAt<CompletionSource> ("autocomplete", pos)
    |> Array.map (fun source -> source (CompletionContextApi.Create(state, pos, false)))
    |> Promise.all

let private option (label: string) (iconClass: string option) (reopen: bool) (boost: int) : TextCompletionOption = {|
    label = label
    iconClass = iconClass
    reopen = reopen
    boost = boost
|}

Vitest.describe (
    "TextCompletion.extension",
    fun () ->
        Vitest.test (
            "gives the source the line around the cursor, the position and how completion started",
            fun () -> promise {
                let seen = ResizeArray<TextCompletionContext>()

                let source (context: TextCompletionContext) = promise {
                    seen.Add context
                    return None
                }

                let doc = "first\n[a](./do" + "cs)\nlast"
                let pos = "first\n[a](./do".Length
                let! _ = complete (stateWith doc (TextCompletion.extension source)) pos

                Vitest.expect(seen.Count).toBe 1
                Vitest.expect(seen.[0].position).toBe pos
                Vitest.expect(seen.[0].lineBefore).toBe "[a](./do"
                Vitest.expect(seen.[0].lineAfter).toBe "cs)"
                Vitest.expect(seen.[0].explicit).toBe false
            }
        )

        Vitest.test (
            "hands the range, the entries and the validity check of the result to the editor",
            fun () -> promise {
                let validFor (text: string) = text = "ok"

                let source (_: TextCompletionContext) = promise {
                    return
                        Some {|
                            from = 2
                            ``to`` = 4
                            options = [|
                                option "docs/" (Some "icon-folder") true 1
                                option "a.md" None false 0
                            |]
                            validFor = Some validFor
                        |}
                }

                let! results = complete (stateWith "abcdef" (TextCompletion.extension source)) 4

                match results with
                | [| Some result |] ->
                    Vitest.expect(result.from).toBe 2
                    Vitest.expect(result.``to``).toEqual (Some 4)
                    Vitest.expect(result.options |> Array.map _.label).toEqual [| "docs/"; "a.md" |]
                    // An entry with its own icon gets the type whose CodeMirror icon the theme hides.
                    Vitest.expect(result.options.[0].``type``).toEqual (Some TextCompletion.IconType)
                    Vitest.expect(result.options.[0].swtIcon).toEqual (Some "icon-folder")
                    Vitest.expect(result.options.[0].swtReopen).toEqual (Some true)
                    Vitest.expect(result.options.[0].boost).toEqual (Some 1)
                    Vitest.expect(result.options.[1].``type``).toEqual None
                    Vitest.expect(result.options.[1].swtReopen).toEqual None
                    Vitest.expect(result.validFor.Value "ok").toBe true
                    Vitest.expect(result.validFor.Value "no").toBe false
                | other -> failwith $"Expected one result, got {other.Length}"
            }
        )

        Vitest.test (
            "gives a read-only editor no completions without asking the source",
            fun () -> promise {
                let asked = ref false

                let source (_: TextCompletionContext) = promise {
                    asked.Value <- true
                    return None
                }

                let state =
                    stateWith
                        "[a](./"
                        (extensions [|
                            TextCompletion.extension source
                            EditorStateApi.readOnly.``of`` true
                        |])

                let! results = complete state 6
                Vitest.expect(results).toEqual [| None |]
                Vitest.expect(asked.Value).toBe false
            }
        )
)
