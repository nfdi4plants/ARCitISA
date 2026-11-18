module internal Swate.Components.Composite.MarkdownText.Editing.MarkdownEditsTests

open Vitest
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.MarkdownText.Editing.MarkdownEdits

let private state (text: string) (anchor: int) (head: int) : TextEditState = {|
    text = text
    selection = {| anchor = anchor; head = head |}
|}

let private result (text: string) (edit: TextEdit) =
    applyToText edit text, edit.selection.anchor, edit.selection.head

/// Runs a command that always succeeds and returns the new text with the new selection.
let private run (cmd: TextEditState -> TextEdit) (text: string) (anchor: int) (head: int) =
    result text (cmd (state text anchor head))

/// Runs a command that returns a Result. The test fails when the command refuses.
let private runInline (cmd: TextEditState -> Result<TextEdit, string>) (text: string) (anchor: int) (head: int) =
    match cmd (state text anchor head) with
    | Ok edit -> result text edit
    | Error message -> failwith message

let private errorOf (cmd: TextEditState -> Result<TextEdit, string>) (text: string) (anchor: int) (head: int) =
    match cmd (state text anchor head) with
    | Ok _ -> ""
    | Error message -> message

Vitest.describe (
    "applyToText",
    fun () ->
        Vitest.test (
            "applies several changes with offsets from the original text",
            fun () ->
                let edit: TextEdit = {|
                    changes = [|
                        {|
                            from = 0
                            ``to`` = 1
                            insert = "XY"
                        |}
                        {| from = 2; ``to`` = 2; insert = "-" |}
                    |]
                    selection = {| anchor = 0; head = 0 |}
                |}

                Vitest.expect(applyToText edit "abcd").toBe "XYb-cd"
        )
)

Vitest.describe (
    "setHeading",
    fun () ->
        Vitest.test (
            "adds a heading and keeps the cursor in the content",
            fun () -> Vitest.expect(runInline (setHeading 2) "hello" 3 3).toEqual ("## hello", 6, 6)
        )

        Vitest.test (
            "removes a heading of the same level",
            fun () -> Vitest.expect(runInline (setHeading 2) "## hello" 5 5).toEqual ("hello", 2, 2)
        )

        Vitest.test (
            "replaces the prefix of another level",
            fun () -> Vitest.expect(runInline (setHeading 1) "### hello" 6 6).toEqual ("# hello", 4, 4)
        )

        Vitest.test (
            "gives every selected line the heading",
            fun () -> Vitest.expect(runInline (setHeading 2) "a\n# b\n## c" 0 10).toEqual ("## a\n## b\n## c", 0, 14)
        )

        Vitest.test (
            "puts the cursor of an empty line after the prefix",
            fun () -> Vitest.expect(runInline (setHeading 2) "" 0 0).toEqual ("## ", 3, 3)
        )

        Vitest.test (
            "refuses a selection of blank lines",
            fun () -> Vitest.expect(errorOf (setHeading 2) "a\n\n\nb" 2 4).toBe "Select a line with text."
        )
)

Vitest.describe (
    "toggleQuote",
    fun () ->
        Vitest.test (
            "adds a quote to two lines",
            fun () -> Vitest.expect(runInline toggleQuote "a\nb" 0 3).toEqual ("> a\n> b", 0, 7)
        )

        Vitest.test (
            "removes the quote from two lines",
            fun () -> Vitest.expect(runInline toggleQuote "> a\n>b" 0 6).toEqual ("a\nb", 0, 3)
        )

        Vitest.test (
            "quotes only the unquoted lines of a mixed selection",
            fun () -> Vitest.expect(runInline toggleQuote "> a\nb" 0 5).toEqual ("> a\n> b", 0, 7)
        )

        Vitest.test (
            "quotes a blank line between two lines as part of one block and removes it again",
            fun () ->
                Vitest.expect(runInline toggleQuote "a\n\nb" 0 4).toEqual ("> a\n>\n> b", 0, 9)
                Vitest.expect(runInline toggleQuote "> a\n>\n> b" 0 9).toEqual ("a\n\nb", 0, 4)
        )
)

Vitest.describe (
    "toggleBulletList",
    fun () ->
        Vitest.test (
            "adds bullets and leaves a blank line untouched",
            fun () -> Vitest.expect(runInline toggleBulletList "a\n\nb\nc" 0 6).toEqual ("- a\n\n- b\n- c", 0, 12)
        )

        Vitest.test (
            "removes the bullets",
            fun () -> Vitest.expect(runInline toggleBulletList "- a\n* b" 0 7).toEqual ("a\nb", 0, 3)
        )

        Vitest.test (
            "converts a numbered line to a bullet",
            fun () -> Vitest.expect(runInline toggleBulletList "1. a\nb" 0 6).toEqual ("- a\n- b", 0, 7)
        )

        Vitest.test (
            "does not touch the line where the selection ends at column 0",
            fun () -> Vitest.expect(runInline toggleBulletList "a\nb" 0 2).toEqual ("- a\nb", 0, 4)
        )

        Vitest.test (
            "keeps the indentation before the marker",
            fun () -> Vitest.expect(runInline toggleBulletList "  a" 3 3).toEqual ("  - a", 5, 5)
        )
)

Vitest.describe (
    "toggleNumberedList",
    fun () ->
        Vitest.test (
            "numbers three lines from 1 to 3",
            fun () -> Vitest.expect(runInline toggleNumberedList "a\nb\nc" 0 5).toEqual ("1. a\n2. b\n3. c", 0, 14)
        )

        Vitest.test (
            "removes the numbers when every line has one",
            fun () -> Vitest.expect(runInline toggleNumberedList "1. a\n2. b" 0 9).toEqual ("a\nb", 0, 3)
        )

        Vitest.test (
            "renumbers a mixed selection from 1",
            fun () -> Vitest.expect(runInline toggleNumberedList "a\n3. b" 0 6).toEqual ("1. a\n2. b", 0, 9)
        )
)

Vitest.describe (
    "toggleTaskList",
    fun () ->
        Vitest.test (
            "adds task markers",
            fun () -> Vitest.expect(runInline toggleTaskList "a\nb" 0 3).toEqual ("- [ ] a\n- [ ] b", 0, 15)
        )

        Vitest.test (
            "removes task markers",
            fun () -> Vitest.expect(runInline toggleTaskList "- [ ] a\n* [x] b" 0 15).toEqual ("a\nb", 0, 3)
        )

        Vitest.test (
            "converts a bullet to a task",
            fun () -> Vitest.expect(runInline toggleTaskList "- a" 3 3).toEqual ("- [ ] a", 7, 7)
        )
)

Vitest.describe (
    "insertCodeBlock",
    fun () ->
        Vitest.test (
            "fences two lines and puts the cursor after the opening fence",
            fun () -> Vitest.expect(run insertCodeBlock "a\nb" 0 3).toEqual ("```\na\nb\n```", 3, 3)
        )

        Vitest.test (
            "fences an empty line",
            fun () -> Vitest.expect(run insertCodeBlock "" 0 0).toEqual ("```\n\n```", 3, 3)
        )

        Vitest.test (
            "uses a longer fence when a line contains one",
            fun () -> Vitest.expect(run insertCodeBlock "```" 0 0).toEqual ("````\n```\n````", 4, 4)
        )
)

Vitest.describe (
    "insertLink",
    fun () ->
        Vitest.test (
            "selects the url when text is selected",
            fun () -> Vitest.expect(runInline insertLink "a word b" 2 6).toEqual ("a [word](url) b", 9, 12)
        )

        Vitest.test (
            "selects the text when nothing is selected",
            fun () -> Vitest.expect(runInline insertLink "ab" 1 1).toEqual ("a[text](url)b", 2, 6)
        )

        Vitest.test (
            "refuses a selection over several lines",
            fun () -> Vitest.expect(errorOf insertLink "a\nb" 0 3).toBe "Select text on one line."
        )
)

Vitest.describe (
    "insertTable",
    fun () ->
        let table = "| Column 1 | Column 2 |\n| --- | --- |\n|  |  |"

        Vitest.test (
            "fills an empty line and selects the first column name",
            fun () -> Vitest.expect(run insertTable "" 0 0).toEqual (table, 2, 10)
        )

        Vitest.test (
            "starts after a blank line when the line above is not empty",
            fun () -> Vitest.expect(run insertTable "text" 4 4).toEqual ("text\n\n" + table, 8, 16)
        )
)

Vitest.describe (
    "toggleBold",
    fun () ->
        Vitest.test (
            "adds bold to italic text and removes it again",
            fun () ->
                Vitest.expect(runInline toggleBold "*x*" 0 3).toEqual ("***x***", 2, 5)
                Vitest.expect(runInline toggleBold "***x***" 2 5).toEqual ("*x*", 0, 3)
        )

        Vitest.test (
            "removes bold from inside the selection and from outside",
            fun () ->
                Vitest.expect(runInline toggleBold "***x***" 3 4).toEqual ("*x*", 1, 2)
                Vitest.expect(runInline toggleBold "*x*" 1 2).toEqual ("***x***", 3, 4)
        )

        Vitest.test (
            "keeps trailing spaces of the selection outside",
            fun () -> Vitest.expect(runInline toggleBold "a word b" 2 7).toEqual ("a **word** b", 4, 8)
        )

        Vitest.test (
            "wraps an empty selection and removes the pair on the next press",
            fun () ->
                Vitest.expect(runInline toggleBold "" 0 0).toEqual ("****", 2, 2)
                Vitest.expect(runInline toggleBold "****" 2 2).toEqual ("", 0, 0)
        )

        Vitest.test (
            "keeps the direction of a backward selection",
            fun () -> Vitest.expect(runInline toggleBold "a word b" 6 2).toEqual ("a **word** b", 8, 4)
        )

        Vitest.test (
            "fails on a selection over several formatted parts",
            fun () ->
                Vitest.expect(errorOf toggleBold "**a** and **b**" 0 15).toBe
                    "The selection covers more than one formatted part."
        )

        Vitest.test (
            "fails on a selection over several lines",
            fun () -> Vitest.expect(errorOf toggleBold "a\nb" 0 3).toBe "Select text on one line."
        )
)

Vitest.describe (
    "toggleItalic",
    fun () ->
        Vitest.test (
            "adds italic to bold text and removes it again",
            fun () ->
                Vitest.expect(runInline toggleItalic "**x**" 0 5).toEqual ("***x***", 1, 6)
                Vitest.expect(runInline toggleItalic "***x***" 1 6).toEqual ("**x**", 0, 5)
        )

        Vitest.test (
            "toggles a cursor between stars",
            fun () ->
                Vitest.expect(runInline toggleItalic "****" 2 2).toEqual ("******", 3, 3)
                Vitest.expect(runInline toggleItalic "******" 3 3).toEqual ("****", 2, 2)
        )

        Vitest.test (
            "removes italic from the inner text and adds it back",
            fun () ->
                Vitest.expect(runInline toggleItalic "***x***" 3 4).toEqual ("**x**", 2, 3)
                Vitest.expect(runInline toggleItalic "**x**" 2 3).toEqual ("***x***", 3, 4)
        )

        Vitest.test (
            "fails when only one side has stars",
            fun () ->
                Vitest.expect(errorOf toggleItalic "**bold**" 8 8).toBe "The stars around the selection do not match."
        )
)

Vitest.describe (
    "toggleStrikethrough",
    fun () ->
        Vitest.test (
            "wraps the selection",
            fun () -> Vitest.expect(runInline toggleStrikethrough "a b c" 2 3).toEqual ("a ~~b~~ c", 4, 5)
        )

        Vitest.test (
            "unwraps from inside the selection",
            fun () -> Vitest.expect(runInline toggleStrikethrough "a ~~b~~ c" 2 7).toEqual ("a b c", 2, 3)
        )

        Vitest.test (
            "unwraps from outside the selection",
            fun () -> Vitest.expect(runInline toggleStrikethrough "a ~~b~~ c" 4 5).toEqual ("a b c", 2, 3)
        )

        Vitest.test (
            "wraps a cursor and removes the pair on the next press",
            fun () ->
                Vitest.expect(runInline toggleStrikethrough "" 0 0).toEqual ("~~~~", 2, 2)
                Vitest.expect(runInline toggleStrikethrough "~~~~" 2 2).toEqual ("", 0, 0)
        )

        Vitest.test (
            "fails when only one side has tildes",
            fun () ->
                Vitest.expect(errorOf toggleStrikethrough "~~a~~" 3 3).toBe
                    "The tildes around the selection do not match."
        )

        Vitest.test (
            "fails on a selection over several formatted parts",
            fun () ->
                Vitest.expect(errorOf toggleStrikethrough "~~a~~ and ~~b~~" 0 15).toBe
                    "The selection covers more than one formatted part."
        )
)

Vitest.describe (
    "toggleInlineCode",
    fun () ->
        Vitest.test (
            "wraps the selection",
            fun () -> Vitest.expect(runInline toggleInlineCode "x a y" 2 3).toEqual ("x `a` y", 3, 4)
        )

        // The padding rule only applies when the text starts or ends with a backtick.
        Vitest.test (
            "uses a longer run when the text contains a backtick",
            fun () -> Vitest.expect(runInline toggleInlineCode "a`b" 0 3).toEqual ("``a`b``", 2, 5)
        )

        Vitest.test (
            "pads the run when the text starts with a backtick",
            fun () -> Vitest.expect(runInline toggleInlineCode "`a" 0 2).toEqual ("`` `a ``", 3, 5)
        )

        Vitest.test (
            "unwraps from inside the selection",
            fun () -> Vitest.expect(runInline toggleInlineCode "x `a` y" 2 5).toEqual ("x a y", 2, 3)
        )

        Vitest.test (
            "unwraps from outside the selection",
            fun () -> Vitest.expect(runInline toggleInlineCode "x `a` y" 3 4).toEqual ("x a y", 2, 3)
        )

        Vitest.test (
            "unwraps a padded run from inside and from outside",
            fun () ->
                Vitest.expect(runInline toggleInlineCode "`` a`b ``" 0 9).toEqual ("a`b", 0, 3)
                Vitest.expect(runInline toggleInlineCode "`` a`b ``" 3 6).toEqual ("a`b", 0, 3)
        )

        Vitest.test (
            "wraps an empty selection and removes the pair on the next press",
            fun () ->
                Vitest.expect(runInline toggleInlineCode "" 0 0).toEqual ("``", 1, 1)
                Vitest.expect(runInline toggleInlineCode "``" 1 1).toEqual ("", 0, 0)
        )

        Vitest.test (
            "fails when only one side has a backtick",
            fun () ->
                Vitest.expect(errorOf toggleInlineCode "`code`" 5 5).toBe
                    "The backticks around the selection do not match."

                Vitest.expect(errorOf toggleInlineCode "x `code` y" 7 7).toBe
                    "The backticks around the selection do not match."
        )

        Vitest.test (
            "fails on a selection over several lines",
            fun () -> Vitest.expect(errorOf toggleInlineCode "a\nb" 0 3).toBe "Select text on one line."
        )
)
