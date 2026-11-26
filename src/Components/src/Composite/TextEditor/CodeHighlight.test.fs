module internal Arcitisa.Components.Composite.TextEditor.CodeHighlightTests

open Fable.Core
open Vitest
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.CodeHighlight

let private nameOf (fenceName: string) =
    findLanguage languages fenceName |> Option.map _.name |> Option.defaultValue ""

let private colorOf (segments: Segment[]) (text: string) =
    segments
    |> Array.tryFind (fun s -> s.text = text)
    |> Option.bind _.style
    |> Option.bind _.color

Vitest.describe (
    "findLanguage",
    fun () ->
        Vitest.test (
            "finds a language by its name, ignoring case",
            fun () -> Vitest.expect(nameOf "PYTHON").toBe "Python"
        )

        // zsh is only an alias. sh is also an extension of Shell, so it would pass without the alias lookup.
        Vitest.test ("finds a language by an alias", fun () -> Vitest.expect(nameOf "zsh").toBe "Shell")
        Vitest.test ("finds a language by a file extension", fun () -> Vitest.expect(nameOf "py").toBe "Python")
        Vitest.test ("finds nothing for an unknown name", fun () -> Vitest.expect(nameOf "no-such-language").toBe "")
        Vitest.test ("finds nothing for an empty name", fun () -> Vitest.expect(nameOf " ").toBe "")
)

Vitest.describe (
    "highlight",
    fun () ->
        Vitest.test (
            "keeps the text and line breaks of the code",
            fun () -> promise {
                let code = "{\n  \"name\": \"a\",\n  \"count\": 1\n}"
                let! result = highlight "json" code
                let segments = result |> Option.defaultValue [||]
                Vitest.expect(segments |> Array.map _.text |> String.concat "").toBe code
            }
        )

        Vitest.test (
            "colors different kinds of tokens differently",
            fun () -> promise {
                let! result = highlight "json" "{\"count\": 1}"
                let segments = result |> Option.defaultValue [||]
                let key = colorOf segments "\"count\""
                let number = colorOf segments "1"
                Vitest.expect(key.IsSome && number.IsSome).toBe true
                Vitest.expect(key <> number).toBe true
            }
        )

        Vitest.test (
            "leaves code in an unknown language unhighlighted",
            fun () -> promise {
                let! result = highlight "no-such-language" "x = 1"
                Vitest.expect(result.IsNone).toBe true
            }
        )
)
