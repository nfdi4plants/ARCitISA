module internal Swate.Components.Composite.MarkdownText.Editing.OntologyReferencesTests

open Fable.Core
open Vitest
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText.JsBindings.Mdast
open Swate.Components.Composite.MarkdownText.Editing.MarkdownEdits
open Swate.Components.Composite.MarkdownText.Editing.OntologyReferences

let private instrumentModel: OntologyReference = {|
    name = "instrument model"
    curie = "MS:1000031"
    iri = "http://purl.obolibrary.org/obo/MS_1000031"
|}

let private cursorAt (text: string) (offset: int) : TextEditState = {|
    text = text
    selection = {| anchor = offset; head = offset |}
|}

let private selecting (text: string) (anchor: int) (head: int) : TextEditState = {|
    text = text
    selection = {| anchor = anchor; head = head |}
|}

/// The resulting text, or the error prefixed with "Error: ".
let private outcome (reference: OntologyReference) (state: TextEditState) =
    match insert reference state with
    | Ok edit -> applyToText edit state.text
    | Error message -> "Error: " + message

let private cursorOf (reference: OntologyReference) (state: TextEditState) =
    match insert reference state with
    | Ok edit -> edit.selection.head
    | Error _ -> -1

let private changeCount (reference: OntologyReference) (state: TextEditState) =
    match insert reference state with
    | Ok edit -> edit.changes.Length
    | Error _ -> -1

/// Makes toLocaleLowerCase and toLocaleUpperCase default to the Turkish locale. Returns a function that restores them.
[<Emit("(() => { const lower = String.prototype.toLocaleLowerCase, upper = String.prototype.toLocaleUpperCase; String.prototype.toLocaleLowerCase = function (l) { return lower.call(this, l ?? 'tr') }; String.prototype.toLocaleUpperCase = function (l) { return upper.call(this, l ?? 'tr') }; return () => { String.prototype.toLocaleLowerCase = lower; String.prototype.toLocaleUpperCase = upper } })()")>]
let private useTurkishLocale () : unit -> unit = jsNative

let private msDefinition =
    "[MS:1000031]: http://purl.obolibrary.org/obo/MS_1000031 \"instrument model\"\n"

Vitest.describe (
    "insert",
    fun () ->
        let sentence = "The sample was measured with an "

        Vitest.test (
            "adds the inline link and the definition at the end of the file in one change",
            fun () ->
                let state = cursorAt sentence sentence.Length

                Vitest
                    .expect(outcome instrumentModel state)
                    .toBe (sentence + "[instrument model][MS:1000031]\n\n" + msDefinition)

                Vitest.expect(changeCount instrumentModel state).toBe 1

                Vitest
                    .expect(cursorOf instrumentModel state)
                    .toBe (sentence.Length + "[instrument model][MS:1000031]".Length)
        )

        Vitest.test (
            "puts the definition after one blank line when the text continues after the cursor",
            fun () ->
                let text = "Intro text here.\nMore text.\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text 6))
                    .toBe ("Intro [instrument model][MS:1000031]text here.\nMore text.\n\n" + msDefinition)
        )

        Vitest.test (
            "uses the selected text as the link text",
            fun () ->
                let text = "We used Orbitrap here.\n"

                Vitest
                    .expect(outcome instrumentModel (selecting text 8 16))
                    .toBe ("We used [Orbitrap][MS:1000031] here.\n\n" + msDefinition)
        )

        Vitest.test (
            "joins an existing definition block at the end of the file",
            fun () ->
                let text = "Intro\n\n[x]: http://e.org/x\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text 5))
                    .toBe ("Intro[instrument model][MS:1000031]\n\n[x]: http://e.org/x\n" + msDefinition)
        )

        Vitest.test (
            "inserts only the inline link when the definition exists with another case",
            fun () ->
                let text = "See \n\n[ms:1000031]: http://purl.obolibrary.org/obo/MS_1000031\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text 4))
                    .toBe (
                        "See [instrument model][MS:1000031]\n\n[ms:1000031]: http://purl.obolibrary.org/obo/MS_1000031\n"
                    )
        )

        Vitest.test (
            "refuses a definition with another URL",
            fun () ->
                let text = "See \n\n[MS:1000031]: http://e.org/other\n"

                Vitest.expect(outcome instrumentModel (cursorAt text 4)).toBe
                    "Error: [MS:1000031] already links to http://e.org/other."
        )

        Vitest.test (
            "counts a definition inside a list item",
            fun () ->
                let text = "See \n\n- [MS:1000031]: http://e.org/other\n"

                Vitest.expect(outcome instrumentModel (cursorAt text 4)).toBe
                    "Error: [MS:1000031] already links to http://e.org/other."
        )

        Vitest.test (
            "escapes the link text and the title separately",
            fun () ->
                let reference = {|
                    instrumentModel with
                        name = "a*b [c]"
                        curie = "MS:1"
                        iri = "http://e.org/x"
                |}

                Vitest
                    .expect(outcome reference (cursorAt "" 0))
                    .toBe ("[a\\*b \\[c\\]][MS:1]\n\n[MS:1]: http://e.org/x \"a*b [c]\"\n")
        )

        Vitest.test (
            "escapes quotes in the title",
            fun () ->
                let reference = {|
                    instrumentModel with
                        name = "say \"hi\""
                        curie = "MS:1"
                        iri = "http://e.org/x"
                |}

                Vitest
                    .expect(outcome reference (cursorAt "" 0))
                    .toBe ("[say \"hi\"][MS:1]\n\n[MS:1]: http://e.org/x \"say \\\"hi\\\"\"\n")
        )

        Vitest.test (
            "refuses an IRI that is not a web address",
            fun () ->
                let reference = {|
                    instrumentModel with
                        iri = "urn:x:1"
                |}

                Vitest.expect(outcome reference (cursorAt "" 0)).toBe "Error: The IRI urn:x:1 is not a web address."
        )

        Vitest.test (
            "writes an IRI with a space in angle brackets",
            fun () ->
                let reference = {|
                    instrumentModel with
                        curie = "MS:2"
                        iri = "http://e.org/a b"
                |}

                Vitest
                    .expect(outcome reference (cursorAt "" 0))
                    .toBe ("[instrument model][MS:2]\n\n[MS:2]: <http://e.org/a b> \"instrument model\"\n")
        )

        Vitest.test (
            "refuses a cursor inside a fenced code block",
            fun () ->
                let text = "```\ncode\n```\n"

                Vitest.expect(outcome instrumentModel (cursorAt text 5)).toBe "Error: A link cannot be inserted here."
        )

        Vitest.test (
            "refuses when an unclosed fence swallows the definition",
            fun () ->
                let text = "Intro\n\n```\ncode\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text 5))
                    .toBe ("Error: The link definition for [MS:1000031] would not take effect at the end of this file.")
        )
)

Vitest.describe (
    "insert refusals and escapes",
    fun () ->
        Vitest.test (
            "refuses a cursor right after a defined shortcut reference",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "See [x] now\n\n[x]: http://e.org/x\n" 7))
                    .toBe (
                        "Error: Inserting here would break a link, image, footnote or link definition next to the cursor."
                    )
        )

        Vitest.test (
            "refuses a cursor inside the text of a link",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "[foo](http://x) z" 4))
                    .toBe (
                        "Error: Inserting here would break a link, image, footnote or link definition next to the cursor."
                    )
        )

        Vitest.test (
            "refuses a blank line above a definition",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "Text [a][MS:1].\n\n[MS:1]: http://e.org/a \"a\"\n" 16))
                    .toBe ("Error: Put the cursor on a line with text.")
        )

        Vitest.test (
            "refuses a blank line above a thematic break",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "Intro\n\n---\n\nEnd\n" 6))
                    .toBe ("Error: Put the cursor on a line with text.")
        )

        Vitest.test (
            "refuses a text with a byte order mark",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "\uFEFFHello " 7))
                    .toBe ("Error: The file starts with a byte order mark (U+FEFF). Remove it to insert a link.")
        )

        Vitest.test (
            "refuses a selection over several lines",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (selecting "One\n\nTwo" 0 8))
                    .toBe ("Error: Select text on one line.")
        )

        Vitest.test (
            "refuses a selection that ends after a line break",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (selecting "One\nTwo" 0 4))
                    .toBe ("Error: Select text on one line.")
        )

        Vitest.test (
            "refuses a cursor next to a footnote reference",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "a[^1] b\n\n[^1]: note\n" 3))
                    .toBe (
                        "Error: Inserting here would break a link, image, footnote or link definition next to the cursor."
                    )
        )

        Vitest.test (
            "refuses a cursor inside an unused definition that is not at the end",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "Some text\n\n[x]: ./a.csv\n\nMore text\n" 13))
                    .toBe (
                        "Error: Inserting here would break a link, image, footnote or link definition next to the cursor."
                    )
        )

        Vitest.test (
            "refuses when the label already appears as text elsewhere",
            fun () ->
                let text = "Earlier [MS:1000031] mention.\nNow "

                Vitest
                    .expect(outcome instrumentModel (cursorAt text text.Length))
                    .toBe (
                        "Error: [MS:1000031] already appears as text in the file and would become a link or image too."
                    )
        )

        Vitest.test (
            "refuses when the label already appears as an image reference",
            fun () ->
                let text = "Earlier ![x][MS:1000031] mention.\nNow "

                Vitest
                    .expect(outcome instrumentModel (cursorAt text text.Length))
                    .toBe (
                        "Error: [MS:1000031] already appears as text in the file and would become a link or image too."
                    )
        )

        Vitest.test (
            "refuses a cursor right after an exclamation mark",
            fun () ->
                Vitest
                    .expect(outcome instrumentModel (cursorAt "Wow!" 4))
                    .toBe (
                        "Error: A \"!\" right before the cursor would turn the link into an image. Add a space first."
                    )
        )

        Vitest.test (
            "refuses a cursor at the start of a trailing definition",
            fun () ->
                let text = "Intro\n\n[x]: http://e.org/x\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text 7))
                    .toBe ("Error: Move the cursor out of the link definitions at the end of the file.")
        )

        Vitest.test (
            "refuses a cursor at the very end after the definitions",
            fun () ->
                let text = "Intro\n\n[x]: http://e.org/x\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text text.Length))
                    .toBe ("Error: Move the cursor out of the link definitions at the end of the file.")
        )

        Vitest.test (
            "escapes the special characters of the label",
            fun () -> Vitest.expect(labelOf "a[b]^c|d\\e" "http://e.org/x").toEqual (Ok "a\\[b\\]\\^c\\|d\\\\e")
        )

        Vitest.test (
            "refuses a label longer than 999 characters",
            fun () ->
                let reference = {|
                    instrumentModel with
                        curie = String.replicate 1000 "a"
                |}

                Vitest.expect(outcome reference (cursorAt "" 0)).toBe "Error: The link label is too long."
        )

        Vitest.test (
            "escapes backslashes and ampersands in the destination",
            fun () ->
                let reference = {|
                    instrumentModel with
                        curie = "MS:3"
                        iri = "http://e.org/a\\b&c"
                |}

                Vitest
                    .expect(outcome reference (cursorAt "" 0))
                    .toBe ("[instrument model][MS:3]\n\n[MS:3]: <http://e.org/a\\\\b\\&c> \"instrument model\"\n")
        )

        Vitest.test (
            "keeps the first definition when a label is defined twice",
            fun () ->
                let text =
                    "See \n\n[MS:1000031]: http://purl.obolibrary.org/obo/MS_1000031\n[MS:1000031]: http://e.org/other\n"

                Vitest
                    .expect(outcome instrumentModel (cursorAt text 4))
                    .toBe (
                        "See [instrument model][MS:1000031]\n\n[MS:1000031]: http://purl.obolibrary.org/obo/MS_1000031\n[MS:1000031]: http://e.org/other\n"
                    )
        )
)

Vitest.describe (
    "check",
    fun () ->
        Vitest.test (
            "reports an IRI that is not a web address",
            fun () ->
                Vitest
                    .expect(
                        check {|
                            instrumentModel with
                                iri = "urn:x:1"
                        |}
                    )
                    .toEqual (Error "The IRI urn:x:1 is not a web address.")
        )

        Vitest.test (
            "refuses a DEL character in the IRI",
            fun () ->
                Vitest
                    .expect(
                        check {|
                            instrumentModel with
                                iri = "http://e.org/a\u007fb"
                        |}
                    )
                    .toEqual (Error "The IRI http://e.org/a\u007fb cannot be written as a Markdown link.")
        )

        Vitest.test ("accepts a usable term", fun () -> Vitest.expect(check instrumentModel).toEqual (Ok()))
)

Vitest.describe (
    "labelOf",
    fun () ->
        Vitest.test (
            "takes the last segment of the IRI without a CURIE",
            fun () -> Vitest.expect(labelOf "" "http://edamontology.org/data_0006").toEqual (Ok "data_0006")
        )

        Vitest.test (
            "fails when the IRI ends in a slash",
            fun () ->
                Vitest
                    .expect(labelOf " " "http://e.org/x/")
                    .toEqual (Error "No link label can be made from http://e.org/x/.")
        )
)

Vitest.describe (
    "identifierOf",
    fun () ->
        Vitest.test (
            "equals the identifier the parser reports",
            fun () ->
                let root = parse "[MS:1000031]: http://e.org/x"
                let parsed = root.children.Value.[0].identifier.Value
                Vitest.expect(identifierOf "MS:1000031").toBe parsed
        )

        Vitest.test (
            "matches the parser for upper-case letters",
            fun () ->
                let restore = useTurkishLocale ()

                try
                    Vitest.expect("I".ToLower()).toBe "\u0131"
                    let root = parse "[CHEBI:15377]: http://e.org/x"
                    Vitest.expect(identifierOf "CHEBI:15377").toBe root.children.Value.[0].identifier.Value
                finally
                    restore ()
        )
)

Vitest.describe (
    "insertPlainText",
    fun () ->
        let apply (curie: string) =
            let state = cursorAt "A " 2
            let edit = insertPlainText "a*b" curie state
            applyToText edit state.text, edit.selection.head

        Vitest.test (
            "writes the escaped name and the CURIE",
            fun () ->
                let text, cursor = apply "MS:1"
                Vitest.expect(text).toBe "A a\\*b (MS:1)"
                Vitest.expect(cursor).toBe 13
        )

        Vitest.test (
            "escapes the CURIE so it cannot become an image",
            fun () ->
                let text, _ = apply "![](https://e.org/p.png)"
                Vitest.expect(text).toBe "A a\\*b (!\\[\\](https://e.org/p.png))"
        )

        Vitest.test (
            "writes only the CURIE without a name",
            fun () ->
                let edit = insertPlainText "" "X:1" (cursorAt "" 0)
                Vitest.expect(applyToText edit "").toBe "X:1"
        )

        Vitest.test (
            "writes only the escaped name without a CURIE",
            fun () ->
                let text, cursor = apply ""
                Vitest.expect(text).toBe "A a\\*b"
                Vitest.expect(cursor).toBe 6
        )
)

Vitest.describe (
    "resolveIri",
    fun () ->
        let lookupFrom (result: JS.Promise<string option>) (calls: int ref) =
            fun (_: string) ->
                calls.Value <- calls.Value + 1
                result

        Vitest.test (
            "prefers the href and does not call the lookup",
            fun () -> promise {
                let calls = ref 0

                let! result =
                    resolveIri
                        (lookupFrom (Promise.lift (Some "http://x")) calls)
                        (Term(id = "MS:1000031", href = " http://e.org/h "))

                Vitest.expect(result).toEqual (Ok "http://e.org/h")
                Vitest.expect(calls.Value).toBe 0
            }
        )

        Vitest.test (
            "uses the lookup without an href",
            fun () -> promise {
                let calls = ref 0

                let! result =
                    resolveIri (lookupFrom (Promise.lift (Some "http://e.org/l")) calls) (Term(id = "MS:1000031"))

                Vitest.expect(result).toEqual (Ok "http://e.org/l")
                Vitest.expect(calls.Value).toBe 1
            }
        )

        Vitest.test (
            "reports an empty lookup result",
            fun () -> promise {
                let! result = resolveIri (lookupFrom (Promise.lift None) (ref 0)) (Term(id = "MS:1000031"))
                Vitest.expect(result).toEqual (Error "No IRI found for MS:1000031.")
            }
        )

        Vitest.test (
            "reports a rejected lookup",
            fun () -> promise {
                let! result = resolveIri (lookupFrom (Promise.reject (exn "boom")) (ref 0)) (Term(id = "MS:1000031"))
                Vitest.expect(result).toEqual (Error "Looking up the IRI for MS:1000031 failed: boom")
            }
        )

        Vitest.test (
            "fails without an href and a CURIE",
            fun () -> promise {
                let! result = resolveIri (lookupFrom (Promise.lift None) (ref 0)) (Term(name = "x"))
                Vitest.expect(result).toEqual (Error "The term has neither an IRI nor a CURIE.")
            }
        )
)
