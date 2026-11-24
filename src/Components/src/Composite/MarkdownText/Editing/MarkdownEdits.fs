module Swate.Components.Composite.MarkdownText.Editing.MarkdownEdits

open System.Text.RegularExpressions
open Swate.Components.Composite.TextEditor.Types

// Shared helpers

let private change (from: int) (until: int) (insert: string) : TextChange = {|
    from = from
    ``to`` = until
    insert = insert
|}

let private selectionOf (anchor: int) (head: int) : TextSelection = {| anchor = anchor; head = head |}

/// Applies the changes (their offsets refer to the original text) and returns the new text.
let applyToText (edit: TextEdit) (text: string) : string =
    let parts = ResizeArray<string>()
    let mutable pos = 0

    for c in edit.changes do
        parts.Add(text.Substring(pos, c.from - pos))
        parts.Add c.insert
        pos <- c.``to``

    parts.Add(text.Substring pos)
    System.String.Join("", parts)

/// A line with its start offset. The line break is not part of the text.
type private Line = {| start: int; text: string |}

let private splitLines (text: string) : Line[] =
    let lines = ResizeArray<Line>()
    let mutable start = 0

    for part in text.Split('\n') do
        lines.Add {| start = start; text = part |}
        start <- start + part.Length + 1

    lines.ToArray()

/// Index of the line that contains the offset. The offset after a line's last character belongs to that line.
let private lineIndexAt (lines: Line[]) (offset: int) : int =
    let mutable index = 0

    for i in 0 .. lines.Length - 1 do
        if lines.[i].start <= offset then
            index <- i

    index

let private isSpaceOrTab (c: char) = c = ' ' || c = '\t'

let private isBlank (s: string) = s |> Seq.forall isSpaceOrTab

let private indentLength (s: string) =
    let mutable n = 0

    while n < s.Length && isSpaceOrTab s.[n] do
        n <- n + 1

    n

let private bounds (state: TextEditState) =
    min state.selection.anchor state.selection.head, max state.selection.anchor state.selection.head

/// The lines the selection touches. A selection that ends at the start of a line does not touch that line.
let private touchedLines (state: TextEditState) (lines: Line[]) : Line[] =
    let lo, hi = bounds state
    let first = lineIndexAt lines lo
    let last = lineIndexAt lines hi

    let last =
        if hi > lo && last > first && lines.[last].start = hi then
            last - 1
        else
            last

    lines.[first..last]

/// The touched lines that a line command changes. Blank lines only count when the selection touches a single line.
let private countedLines (state: TextEditState) : Line[] =
    let touched = touchedLines state (splitLines state.text)

    if touched.Length > 1 then
        touched |> Array.filter (fun l -> not (isBlank l.text))
    else
        touched

/// Moves an offset of the old text to the new text. With stayAtInsertion, a pure insertion exactly at the offset
/// leaves it in front of the inserted text.
let private mapPosition (changes: TextChange[]) (stayAtInsertion: bool) (pos: int) : int =
    let mutable shift = 0
    let mutable inside = None

    for c in changes do
        if inside.IsNone then
            let d = c.insert.Length - (c.``to`` - c.from)

            if c.from = c.``to`` then
                if pos > c.from || (pos = c.from && not stayAtInsertion) then
                    shift <- shift + d
            elif pos >= c.``to`` then
                shift <- shift + d
            elif pos > c.from then
                inside <- Some(c.from + shift + c.insert.Length)

    defaultArg inside (pos + shift)

/// A selection keeps covering its whole lines. Its lower end stays in front of an insertion at that offset and its
/// upper end moves behind it. A cursor moves behind an insertion at its offset. Anchor and head keep their direction.
let private lineEdit (state: TextEditState) (changes: TextChange[]) : TextEdit =
    let anchor, head = state.selection.anchor, state.selection.head

    let selection =
        if anchor = head then
            let p = mapPosition changes false anchor
            selectionOf p p
        else
            let lo = mapPosition changes true (min anchor head)
            let hi = mapPosition changes false (max anchor head)

            if anchor < head then
                selectionOf lo hi
            else
                selectionOf hi lo

    {|
        changes = changes
        selection = selection
    |}

let private noLine = "Select a line with text."

/// Builds the changes for the counted lines. Refuses when no line counts.
let private onCountedLines (state: TextEditState) (build: Line[] -> TextChange[]) : Result<TextEdit, string> =
    let counted = countedLines state

    if counted.Length = 0 then
        Error noLine
    else
        Ok(lineEdit state (build counted))

// Headings

let private headingPattern = Regex(@"^ {0,3}(#{1,6})(?:[ \t]+|$)")

/// Level and prefix length of a heading line.
let private headingOf (line: string) : (int * int) option =
    let m = headingPattern.Match line

    if m.Success then
        Some(m.Groups.[1].Value.Length, m.Value.Length)
    else
        None

let setHeading (level: int) (state: TextEditState) : Result<TextEdit, string> =
    onCountedLines
        state
        (fun counted ->
            let headings = counted |> Array.map (fun l -> headingOf l.text)

            let allAtLevel =
                headings
                |> Array.forall (
                    function
                    | Some(k, _) -> k = level
                    | None -> false
                )

            let prefix = String.replicate level "#" + " "

            let changes =
                Array.zip counted headings
                |> Array.choose (fun (line, heading) ->
                    match heading with
                    | Some(_, length) when allAtLevel -> Some(change line.start (line.start + length) "")
                    | Some(k, _) when k = level -> None
                    | Some(_, length) -> Some(change line.start (line.start + length) prefix)
                    | None -> Some(change line.start line.start prefix)
                )

            changes
        )

// Quote

let toggleQuote (state: TextEditState) : Result<TextEdit, string> =
    onCountedLines
        state
        (fun counted ->
            let isQuoted (line: Line) =
                let indent = indentLength line.text
                indent < line.text.Length && line.text.[indent] = '>'

            let changes =
                if counted |> Array.forall isQuoted then
                    counted
                    |> Array.map (fun line ->
                        let indent = indentLength line.text
                        let at = line.start + indent
                        let hasSpace = indent + 1 < line.text.Length && line.text.[indent + 1] = ' '
                        change at (at + (if hasSpace then 2 else 1)) ""
                    )
                else
                    // Blank lines between the first and the last line get a bare ">" so the lines stay one block quote.
                    let first, last = counted.[0].start, counted.[counted.Length - 1].start

                    let blanks =
                        splitLines state.text
                        |> Array.filter (fun l -> l.start > first && l.start < last && isBlank l.text)
                        |> Array.map (fun l -> change l.start l.start ">")

                    counted
                    |> Array.filter (isQuoted >> not)
                    |> Array.map (fun line -> change line.start line.start "> ")
                    |> Array.append blanks
                    |> Array.sortBy (fun c -> c.from)

            changes
        )

// Lists

type private ListKind =
    | Bullet
    | Task
    | Numbered

let private taskPattern = Regex(@"^[-*+] \[[ xX]\] ")
let private bulletPattern = Regex(@"^[-*+] ")
let private numberPattern = Regex(@"^\d{1,9}[.)] ")

/// Kind and length of the list marker after the indentation, if the line has one.
let private markerOf (line: string) : (ListKind * int) option =
    let rest = line.Substring(indentLength line)
    let task = taskPattern.Match rest
    let bullet = bulletPattern.Match rest
    let number = numberPattern.Match rest

    if task.Success then Some(Task, task.Value.Length)
    elif bullet.Success then Some(Bullet, bullet.Value.Length)
    elif number.Success then Some(Numbered, number.Value.Length)
    else None

let private toggleList (kind: ListKind) (state: TextEditState) : Result<TextEdit, string> =
    onCountedLines
        state
        (fun counted ->
            let markers = counted |> Array.map (fun l -> markerOf l.text)

            let allOfKind =
                markers
                |> Array.forall (
                    function
                    | Some(k, _) -> k = kind
                    | None -> false
                )

            let changes =
                Array.zip counted markers
                |> Array.mapi (fun i (line, marker) ->
                    let at = line.start + indentLength line.text

                    if allOfKind then
                        let length = marker |> Option.map snd |> Option.defaultValue 0
                        Some(change at (at + length) "")
                    else
                        let wanted =
                            match kind with
                            | Bullet -> "- "
                            | Task -> "- [ ] "
                            | Numbered -> string (i + 1) + ". "

                        match marker with
                        | Some(k, _) when k = kind && kind <> Numbered -> None
                        | Some(Numbered, length) when
                            kind = Numbered && line.text.Substring(at - line.start, length) = wanted
                            ->
                            None
                        | Some(_, length) -> Some(change at (at + length) wanted)
                        | None -> Some(change at at wanted)
                )
                |> Array.choose id

            changes
        )

let toggleBulletList (state: TextEditState) : Result<TextEdit, string> = toggleList Bullet state
let toggleTaskList (state: TextEditState) : Result<TextEdit, string> = toggleList Task state
let toggleNumberedList (state: TextEditState) : Result<TextEdit, string> = toggleList Numbered state

// Code block, link, image, table

/// Lengths of the maximal backtick runs in the text.
let private backtickRuns (s: string) : int list =
    let runs = ResizeArray<int>()
    let mutable current = 0

    for c in s do
        if c = '`' then
            current <- current + 1
        else
            if current > 0 then
                runs.Add current

            current <- 0

    if current > 0 then
        runs.Add current

    List.ofSeq runs

let private longestRun (s: string) = backtickRuns s |> List.fold max 0

let insertCodeBlock (state: TextEditState) : TextEdit =
    let lines = splitLines state.text
    let touched = touchedLines state lines
    let first = touched.[0]
    let last = touched.[touched.Length - 1]
    let longest = touched |> Array.map (fun l -> longestRun l.text) |> Array.fold max 0
    let fence = String.replicate (if longest >= 3 then longest + 1 else 3) "`"
    let closeAt = last.start + last.text.Length

    let changes =
        if first.start = closeAt then
            [|
                change first.start first.start (fence + "\n\n" + fence)
            |]
        else
            [|
                change first.start first.start (fence + "\n")
                change closeAt closeAt ("\n" + fence)
            |]

    {|
        changes = changes
        selection = selectionOf (first.start + fence.Length) (first.start + fence.Length)
    |}

let private notOneLine = "Select text on one line."

/// Shared entry of the toggles: the bounds, or the error for a selection over several lines.
let private oneLineBounds (state: TextEditState) : Result<int * int, string> =
    let lo, hi = bounds state

    if state.text.Substring(lo, hi - lo).Contains "\n" then
        Error notOneLine
    else
        Ok(lo, hi)

let insertLink (state: TextEditState) : Result<TextEdit, string> =
    oneLineBounds state
    |> Result.map (fun (lo, hi) ->
        if lo < hi then
            {|
                changes = [| change lo lo "["; change hi hi "](url)" |]
                selection = selectionOf (hi + 3) (hi + 6)
            |}
        else
            {|
                changes = [| change lo lo "[text](url)" |]
                selection = selectionOf (lo + 1) (lo + 5)
            |}
    )

let insertImage (state: TextEditState) : Result<TextEdit, string> =
    oneLineBounds state
    |> Result.map (fun (lo, hi) ->
        if lo < hi then
            {|
                changes = [| change lo lo "!["; change hi hi "](url)" |]
                selection = selectionOf (hi + 4) (hi + 7)
            |}
        else
            {|
                changes = [| change lo lo "![alt text](url)" |]
                selection = selectionOf (lo + 2) (lo + 10)
            |}
    )

let private tableText = "| Column 1 | Column 2 |\n| --- | --- |\n|  |  |"

let insertTable (state: TextEditState) : TextEdit =
    let lines = splitLines state.text
    let _, hi = bounds state
    let index = lineIndexAt lines hi
    let line = lines.[index]

    let suffix =
        if index + 1 < lines.Length && not (isBlank lines.[index + 1].text) then
            "\n"
        else
            ""

    if isBlank line.text then
        let prefix =
            if index > 0 && not (isBlank lines.[index - 1].text) then
                "\n"
            else
                ""

        let anchor = line.start + prefix.Length + 2

        {|
            changes = [|
                change line.start (line.start + line.text.Length) (prefix + tableText + suffix)
            |]
            selection = selectionOf anchor (anchor + 8)
        |}
    else
        let at = line.start + line.text.Length
        let anchor = at + 4

        {|
            changes = [| change at at ("\n\n" + tableText + suffix) |]
            selection = selectionOf anchor (anchor + 8)
        |}

// Inline toggles

let private starMismatch = "The stars around the selection do not match."
let private tildeMismatch = "The tildes around the selection do not match."
let private backtickMismatch = "The backticks around the selection do not match."
let private severalParts = "The selection covers more than one formatted part."

/// Inserts open at a and close at b. Two insertions at one offset become one change.
let private wrapChanges (a: int) (openText: string) (b: int) (closeText: string) : TextChange[] =
    if a = b then
        [| change a a (openText + closeText) |]
    else
        [| change a a openText; change b b closeText |]

/// Substring that returns "" when the range leaves the text.
let private slice (text: string) (a: int) (b: int) =
    if a < 0 || b > text.Length || a > b then
        ""
    else
        text.Substring(a, b - a)

/// The selection without its leading and trailing spaces and tabs. Only whitespace gives an empty range at lo.
let private coreOf (text: string) (lo: int) (hi: int) : int * int =
    let mutable cs = lo

    while cs < hi && isSpaceOrTab text.[cs] do
        cs <- cs + 1

    if cs = hi then
        lo, lo
    else
        let mutable ce = hi

        while isSpaceOrTab text.[ce - 1] do
            ce <- ce - 1

        cs, ce

/// Keeps the direction of the input selection: an anchor behind the head stays behind it.
let private keepDirection (state: TextEditState) (edit: TextEdit) : TextEdit =
    if state.selection.anchor > state.selection.head then
        {|
            edit with
                selection = selectionOf edit.selection.head edit.selection.anchor
        |}
    else
        edit

let private inlineToggle (state: TextEditState) (f: int * int -> Result<TextEdit, string>) =
    oneLineBounds state |> Result.bind f |> Result.map (keepDirection state)

let private inlineEdit (changes: TextChange[]) (anchor: int) (head: int) : Result<TextEdit, string> =
    Ok {|
        changes = changes
        selection = selectionOf anchor head
    |}

let toggleStrikethrough (state: TextEditState) : Result<TextEdit, string> =
    inlineToggle
        state
        (fun (lo, hi) ->
            let text = state.text
            let cs, ce = coreOf text lo hi
            let core = text.Substring(cs, ce - cs)

            let tildes (from: int) (step: int) =
                let mutable n = 0

                while from + n * step >= 0
                      && from + n * step < text.Length
                      && text.[from + n * step] = '~' do
                    n <- n + 1

                n

            let ol = tildes (cs - 1) -1
            let orr = tildes ce 1

            if core.Length >= 4 && core.StartsWith "~~" && core.EndsWith "~~" then
                if (core.Substring(2, core.Length - 4)).Contains "~~" then
                    Error severalParts
                else
                    inlineEdit [| change cs (cs + 2) ""; change (ce - 2) ce "" |] cs (ce - 4)
            elif ol = 2 && orr = 2 then
                inlineEdit [| change (cs - 2) cs ""; change ce (ce + 2) "" |] (cs - 2) (ce - 2)
            elif ol > 0 || orr > 0 then
                Error tildeMismatch
            else
                inlineEdit (wrapChanges cs "~~" ce "~~") (cs + 2) (ce + 2)
        )

/// Bold (k = 2) and italic (k = 1) share the star character and count the runs of stars.
let private toggleStars (k: int) (state: TextEditState) : Result<TextEdit, string> =
    inlineToggle
        state
        (fun (lo, hi) ->
            let text = state.text
            let cs, ce = coreOf text lo hi
            let core = text.Substring(cs, ce - cs)

            let has n =
                if k = 1 then n = 1 || n = 3 else n = 2 || n = 3

            let countStars (from: int) (step: int) (limit: int) =
                let mutable n = 0

                while from + n * step >= 0
                      && from + n * step < text.Length
                      && n < limit
                      && text.[from + n * step] = '*' do
                    n <- n + 1

                n

            let onlyStars = core |> Seq.forall (fun c -> c = '*')
            let il = if onlyStars then 0 else countStars cs 1 core.Length

            let ir =
                if onlyStars then
                    0
                else
                    countStars (ce - 1) (-1) core.Length

            let ol = countStars (cs - 1) (-1) cs
            let orr = countStars ce 1 (text.Length - ce)
            let l = il + ol
            let r = ir + orr

            if l <> r || l > 3 then
                Error starMismatch
            elif il = ir && has il then
                let inner = core.Substring(il, core.Length - 2 * il)

                if inner.Contains "*" then
                    Error severalParts
                else
                    inlineEdit [| change cs (cs + k) ""; change (ce - k) ce "" |] cs (ce - 2 * k)
            elif ol = orr && has ol then
                inlineEdit [| change (cs - k) cs ""; change ce (ce + k) "" |] (cs - k) (ce - k)
            elif not (has l) then
                let stars = String.replicate k "*"
                inlineEdit (wrapChanges cs stars ce stars) (cs + k) (ce + k)
            else
                Error starMismatch
        )

let toggleBold (state: TextEditState) : Result<TextEdit, string> = toggleStars 2 state
let toggleItalic (state: TextEditState) : Result<TextEdit, string> = toggleStars 1 state

let toggleInlineCode (state: TextEditState) : Result<TextEdit, string> =
    inlineToggle
        state
        (fun (lo, hi) ->
            let text = state.text
            let s = text.Substring(lo, hi - lo)

            let runBefore (pos: int) =
                let mutable n = 0

                while pos - 1 - n >= 0 && text.[pos - 1 - n] = '`' do
                    n <- n + 1

                n

            let runAfter (pos: int) =
                let mutable n = 0

                while pos + n < text.Length && text.[pos + n] = '`' do
                    n <- n + 1

                n

            let runs = backtickRuns s
            let n = if s.StartsWith "`" then runs.Head else 0
            let trailing = if s.EndsWith "`" then List.last runs else 0

            let isInside =
                n >= 1
                && trailing = n
                && s.Length >= 2 * n
                && not (backtickRuns (s.Substring(n, s.Length - 2 * n)) |> List.contains n)

            let before = runBefore lo
            let after = runAfter hi

            if isInside then
                let content = s.Substring(n, s.Length - 2 * n)

                let pad =
                    if
                        content.Length >= 2
                        && content.StartsWith " "
                        && content.EndsWith " "
                        && content.Trim(' ') <> ""
                    then
                        1
                    else
                        0

                inlineEdit
                    [|
                        change lo (lo + n + pad) ""
                        change (hi - n - pad) hi ""
                    |]
                    lo
                    (lo + content.Length - 2 * pad)
            elif before >= 1 && before = after then
                inlineEdit [| change (lo - before) lo ""; change hi (hi + after) "" |] (lo - before) (hi - before)
            elif
                slice text (lo - 1) lo = " "
                && slice text hi (hi + 1) = " "
                && runBefore (lo - 1) >= 1
                && runBefore (lo - 1) = runAfter (hi + 1)
            then
                let run = runBefore (lo - 1)

                inlineEdit
                    [|
                        change (lo - 1 - run) lo ""
                        change hi (hi + 1 + run) ""
                    |]
                    (lo - 1 - run)
                    (hi - 1 - run)
            elif before > 0 || after > 0 then
                Error backtickMismatch
            else
                let ticks = String.replicate (longestRun s + 1) "`"

                let pad =
                    if s.StartsWith "`" || s.EndsWith "`" then
                        " "
                    elif s.Length >= 2 && s.StartsWith " " && s.EndsWith " " && s.Trim(' ') <> "" then
                        " "
                    else
                        ""

                let start = lo + ticks.Length + pad.Length
                inlineEdit (wrapChanges lo (ticks + pad) hi (pad + ticks)) start (start + s.Length)
        )
