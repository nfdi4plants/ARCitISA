module internal Arcitisa.Components.Composite.TextEditor.ProseSpellcheckTests

open Fable.Core
open Vitest
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.JsBindings.Lezer
open Arcitisa.Components.Composite.TextEditor.LanguageDetection

let private markdownRules = (proseOf registry "markdown").Value

let private slice (text: string) from ``to`` = text.Substring(from, ``to`` - from)

/// The texts the spellcheck skips in all of text, parsed by the editor's Markdown language.
let private skippedTexts (text: string) = promise {
    let description = languages |> Array.find (fun d -> d.name = "Markdown")
    let! loaded = description.load ()
    let tree = (unbox<LanguageSupport> loaded).language.parser.parse text

    return
        ProseSpellcheck.skippedRanges markdownRules tree.topNode (slice text) 0 text.Length
        |> Array.map (fun (from, ``to``) -> slice text from ``to``)
}

/// A syntax node with its first child and next sibling, null when there is none.
let private fakeNode
    (name: string)
    (from: int)
    (``to``: int)
    (firstChild: SyntaxNode)
    (nextSibling: SyntaxNode)
    : SyntaxNode =
    { new SyntaxNode with
        member _.name = name
        member _.from = from
        member _.``to`` = ``to``
        member _.``type`` = null
        member _.firstChild = firstChild
        member _.lastChild = null
        member _.nextSibling = nextSibling
        member _.prevSibling = null
        member _.parent = null
    }

Vitest.describe (
    "ProseSpellcheck.skippedRanges",
    fun () ->
        Vitest.test (
            "skips an inline HTML tag and inline code but not the prose around them",
            fun () -> promise {
                let! skipped = skippedTexts "Text with <img src=\"a.png\" width=\"80\"> and `codeword` here."
                Vitest.expect(skipped).toEqual [| "<img src=\"a.png\" width=\"80\">"; "`codeword`" |]
            }
        )

        Vitest.test (
            "skips the URL of a link and an autolink but not the link text",
            fun () -> promise {
                let! skipped = skippedTexts "[link text](../images/ok.png) and <https://example.org>"
                Vitest.expect(skipped).toEqual [| "../images/ok.png"; "<https://example.org>" |]
            }
        )

        Vitest.test (
            "skips the tags of an HTML block but not the prose between them",
            fun () -> promise {
                let! skipped = skippedTexts "<div class=\"note\">\nSome prose inside.\n</div>\n"
                Vitest.expect(skipped).toEqual [| "<div class=\"note\">"; "</div>" |]
            }
        )

        Vitest.test (
            "skips a fenced code block and an HTML comment",
            fun () -> promise {
                let! skipped = skippedTexts "<!-- a note -->\n\n```fsharp\nlet mispeled = 1\n```\n"
                Vitest.expect(skipped).toEqual [| "<!-- a note -->"; "```fsharp\nlet mispeled = 1\n```" |]
            }
        )

        Vitest.test (
            "skips nothing in plain prose",
            fun () -> promise {
                let! skipped = skippedTexts "# Heading\n\nJust *some* prose."
                Vitest.expect(skipped).toEqual [||]
            }
        )

        Vitest.test (
            "gives no range for an empty node, on which a mark would throw",
            fun () ->
                let url = fakeNode "URL" 4 8 null null
                let emptyCode = fakeNode "InlineCode" 3 3 null url
                let top = fakeNode "Document" 0 10 emptyCode null

                Vitest.expect(ProseSpellcheck.skippedRanges markdownRules top (slice "0123456789") 0 10).toEqual [|
                    (4, 8)
                |]
        )

        Vitest.test (
            "skips only the nodes that the rules of the language name",
            fun () ->
                let text = "Some `code` here"
                let code = fakeNode "InlineCode" 5 11 null null
                let top = fakeNode "Document" 0 text.Length code null
                let plainTextRules = (proseOf registry "plaintext").Value

                Vitest.expect(ProseSpellcheck.skippedRanges plainTextRules top (slice text) 0 text.Length).toEqual [||]
        )
)
