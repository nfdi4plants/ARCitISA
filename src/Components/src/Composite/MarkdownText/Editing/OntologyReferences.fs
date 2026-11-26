module Arcitisa.Components.Composite.MarkdownText.Editing.OntologyReferences

open System
open System.Text.RegularExpressions
open Fable.Core
open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.TermSearch.Types
open Arcitisa.Components.Composite.MarkdownText.JsBindings.Mdast
open Arcitisa.Components.Composite.MarkdownText.Editing.MarkdownEdits

/// A term to link. curie can be empty. iri is what the link definition points to.
type OntologyReference = {|
    name: string
    curie: string
    iri: string
|}

/// The identifier the parser gives a label with this spelling. Like micromark, it does not decode
/// backslash escapes.
let identifierOf (label: string) : string =
    // micromark lower-cases, then upper-cases the label, and mdast-util-from-markdown lower-cases the result.
    // The invariant variants keep the result independent of the browser locale.
    Regex
        .Replace(Regex.Replace(label, "[\t\n\r ]+", " "), "^ | $", "")
        .ToLowerInvariant()
        .ToUpperInvariant()
        .ToLowerInvariant()

let private escapeChars (chars: string) (text: string) =
    text.ToCharArray()
    |> Array.map (fun c -> if chars.IndexOf c >= 0 then "\\" + string c else string c)
    |> String.concat ""

let private singleLine (text: string) =
    text.Replace("\r", " ").Replace("\n", " ")

let private escapeName (name: string) =
    escapeChars "\\`*_[]<&~|" (singleLine name)

/// The link label for a term: its CURIE, or the last segment of its IRI.
let labelOf (curie: string) (iri: string) : Result<string, string> =
    let raw =
        if not (String.IsNullOrWhiteSpace curie) then
            curie.Trim()
        else
            let cut = max (iri.LastIndexOf '#') (iri.LastIndexOf '/')
            iri.Substring(cut + 1)

    if String.IsNullOrWhiteSpace raw then
        Error $"No link label can be made from {iri}."
    else
        let label = escapeChars "\\[]^|" raw

        if label.Length > 999 then
            Error "The link label is too long."
        else
            Ok label

let private destinationOf (iri: string) : Result<string, string> =
    let lower = iri.ToLowerInvariant()

    if String.IsNullOrWhiteSpace iri then
        Error "The term has no IRI."
    elif not (lower.StartsWith "http://" || lower.StartsWith "https://") then
        Error $"The IRI {iri} is not a web address."
    elif iri |> Seq.exists (fun c -> c = '<' || c = '>' || c < ' ' || c = '\u007f') then
        Error $"The IRI {iri} cannot be written as a Markdown link."
    elif
        iri
        |> Seq.exists (fun c -> c = ' ' || c = '(' || c = ')' || c = '\\' || c = '&')
    then
        Ok("<" + iri.Replace("\\", "\\\\").Replace("&", "\\&") + ">")
    else
        Ok iri

let private titleOf (name: string) =
    "\""
    + (singleLine name).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("&", "\\&")
    + "\""

let rec private collect (pick: MdastNode -> bool) (node: MdastNode) : MdastNode list =
    let own = if pick node then [ node ] else []

    let nested =
        node.children
        |> Option.defaultValue [||]
        |> Seq.toList
        |> List.collect (collect pick)

    own @ nested

let private firstDefinition (identifier: string) (root: MdastNode) =
    collect (fun n -> n.``type`` = "definition" && n.identifier = Some identifier) root
    |> List.tryHead

let private startOffset (node: MdastNode) =
    node.position |> Option.map (fun p -> p.start.offset)

let private endOffset (node: MdastNode) =
    node.position |> Option.map (fun p -> p.``end``.offset)

let private change (from: int) (``to``: int) (insert: string) : TextChange = {|
    from = from
    ``to`` = ``to``
    insert = insert
|}

/// Runs the label and destination rules of insert without touching any text.
let check (reference: OntologyReference) : Result<unit, string> =
    match labelOf reference.curie reference.iri, destinationOf reference.iri with
    | Error e, _
    | _, Error e -> Error e
    | Ok _, Ok _ -> Ok()

/// What an insert has to leave alone, sorted: the source text of every link, image and footnote reference, the
/// label and URL of every link definition, and the label of every footnote definition (its text may take the new
/// reference).
let private protectedParts (text: string) (root: MdastNode) : string list =
    root
    |> collect (fun n ->
        match n.``type`` with
        | "link"
        | "linkReference"
        | "image"
        | "imageReference"
        | "footnoteReference"
        | "definition"
        | "footnoteDefinition" -> true
        | _ -> false
    )
    |> List.choose (fun n ->
        match n.``type`` with
        | "definition" ->
            n.identifier
            |> Option.map (fun id -> "definition " + id + " " + (n.url |> Option.defaultValue ""))
        | "footnoteDefinition" -> n.identifier |> Option.map (fun id -> "footnoteDefinition " + id)
        | _ ->
            match startOffset n, endOffset n with
            | Some s, Some e -> Some(text.Substring(s, e - s))
            | _ -> None
    )
    |> List.sort

/// True when the line that holds the offset is blank and the next line has text.
let private isBlankLineAboveText (text: string) (offset: int) : bool =
    let lineStart = text.Substring(0, offset).LastIndexOf '\n' + 1
    let rest = text.Substring offset
    let newline = rest.IndexOf '\n'

    if newline < 0 then
        false
    else
        let line = text.Substring(lineStart, offset + newline - lineStart)
        let after = rest.Substring(newline + 1)
        let nextEnd = after.IndexOf '\n'
        let next = if nextEnd < 0 then after else after.Substring(0, nextEnd)
        Regex.IsMatch(line, "^[ \t]*$") && not (String.IsNullOrWhiteSpace next)

/// The start of the run of definitions that ends the file, when only whitespace follows it.
let private trailingDefinitionsStart (text: string) (root: MdastNode) : int option =
    let children = root.children |> Option.defaultValue [||]

    let run =
        children |> Array.rev |> Array.takeWhile (fun n -> n.``type`` = "definition")

    if run.Length = 0 then
        None
    else
        match endOffset run.[0], startOffset run.[run.Length - 1] with
        | Some e, Some start when String.IsNullOrWhiteSpace(text.Substring e) -> Some start
        | _ -> None

/// Inserts "[text][label]" at the selection and, unless the label already has a definition, the definition
/// "[label]: iri "name"" at the end of the file. Refuses with a reason when the file starts with U+FEFF, when the
/// label links elsewhere, when the cursor follows a "!", sits on a blank line above text or in the definitions at
/// the end, when the selection spans lines, or when the result would not parse as the intended link or would change
/// other links, footnotes or definitions.
let insert (reference: OntologyReference) (state: TextEditState) : Result<TextEdit, string> =
    let text = state.text
    let from = min state.selection.anchor state.selection.head
    let ``to`` = max state.selection.anchor state.selection.head
    let root = parse text

    match labelOf reference.curie reference.iri, destinationOf reference.iri with
    | Error e, _
    | _, Error e -> Error e
    | Ok _, Ok _ when text.StartsWith "\uFEFF" ->
        Error "The file starts with a byte order mark (U+FEFF). Remove it to insert a link."
    | Ok _, Ok _ when from > 0 && text.[from - 1] = '!' ->
        Error "A \"!\" right before the cursor would turn the link into an image. Add a space first."
    | Ok _, Ok _ when trailingDefinitionsStart text root |> Option.exists (fun start -> from >= start) ->
        Error "Move the cursor out of the link definitions at the end of the file."
    | Ok _, Ok _ when isBlankLineAboveText text from -> Error "Put the cursor on a line with text."
    | Ok _, Ok _ when from <> ``to`` && text.Substring(from, ``to`` - from).Contains "\n" ->
        Error "Select text on one line."
    | Ok label, Ok destination ->
        let linkText =
            if from <> ``to`` then
                text.Substring(from, ``to`` - from)
            else
                escapeName reference.name

        let inlineText = "[" + linkText + "][" + label + "]"
        let identifier = identifierOf label

        let definitionLine =
            "[" + label + "]: " + destination + " " + titleOf reference.name

        let changesResult =
            match firstDefinition identifier root with
            | Some existing ->
                let existingUrl = existing.url |> Option.defaultValue ""

                if existingUrl = reference.iri then
                    Ok [| change from ``to`` inlineText |]
                else
                    Error $"[{label}] already links to {existingUrl}."
            | None ->
                let lastDefinitionEnd =
                    root.children
                    |> Option.bind Array.tryLast
                    |> Option.filter (fun n -> n.``type`` = "definition")
                    |> Option.bind endOffset
                    |> Option.filter (fun e -> String.IsNullOrWhiteSpace(text.Substring e) && ``to`` <= e)

                match lastDefinitionEnd with
                | Some e ->
                    Ok [|
                        change from ``to`` inlineText
                        change e e ("\n" + definitionLine)
                    |]
                | None ->
                    let before = text.Substring(0, from) + inlineText + text.Substring ``to``

                    let separator =
                        if before = "" || before.EndsWith "\n\n" then ""
                        elif before.EndsWith "\n" then "\n"
                        else "\n\n"

                    let appended = separator + definitionLine + "\n"

                    if ``to`` = text.Length then
                        Ok [| change from ``to`` (inlineText + appended) |]
                    else
                        Ok [|
                            change from ``to`` inlineText
                            change text.Length text.Length appended
                        |]

        changesResult
        |> Result.bind (fun changes ->
            let cursor = from + inlineText.Length

            let edit: TextEdit = {|
                changes = changes
                selection = {| anchor = cursor; head = cursor |}
            |}

            let edited = applyToText edit text
            let parsed = parse edited

            let definitionTakesEffect =
                match firstDefinition identifier parsed with
                | Some d -> d.url = Some reference.iri
                | None -> false

            let referenceFound =
                parsed
                |> collect (fun n ->
                    n.``type`` = "linkReference"
                    && n.identifier = Some identifier
                    && startOffset n = Some from
                )
                |> List.isEmpty
                |> not

            let addsDefinition = firstDefinition identifier root |> Option.isNone

            let expected =
                List.sort (
                    inlineText
                    :: ((if addsDefinition then
                             [ "definition " + identifier + " " + reference.iri ]
                         else
                             [])
                        @ protectedParts text root)
                )

            let othersIntact = protectedParts edited parsed = expected

            let otherTextBecomesLink =
                addsDefinition
                && (parsed
                    |> collect (fun n ->
                        n.identifier = Some identifier
                        && ((n.``type`` = "linkReference" && startOffset n <> Some from)
                            || n.``type`` = "imageReference")
                    )
                    |> List.isEmpty
                    |> not)

            if not definitionTakesEffect then
                Error $"The link definition for [{label}] would not take effect at the end of this file."
            elif not referenceFound then
                Error "A link cannot be inserted here."
            elif otherTextBecomesLink then
                Error $"[{label}] already appears as text in the file and would become a link or image too."
            elif not othersIntact then
                Error "Inserting here would break a link, image, footnote or link definition next to the cursor."
            else
                Ok edit
        )

/// Replaces the selection with the term name and its CURIE, for places where no link fits.
let insertPlainText (name: string) (curie: string) (state: TextEditState) : TextEdit =
    let from = min state.selection.anchor state.selection.head
    let ``to`` = max state.selection.anchor state.selection.head

    let inserted =
        if String.IsNullOrWhiteSpace name then escapeName curie
        elif String.IsNullOrWhiteSpace curie then escapeName name
        else escapeName name + " (" + escapeName curie + ")"

    let cursor = from + inserted.Length

    {|
        changes = [| change from ``to`` inserted |]
        selection = {| anchor = cursor; head = cursor |}
    |}

let private present (value: string option) =
    value
    |> Option.filter (fun v -> not (String.IsNullOrWhiteSpace v))
    |> Option.map (fun v -> v.Trim())

/// The IRI of a search hit: its href, else the lookup of its CURIE.
let resolveIri (lookup: string -> JS.Promise<string option>) (term: Term) : JS.Promise<Result<string, string>> =
    match present term.href, present term.id with
    | Some href, _ -> Promise.lift (Ok href)
    | None, Some id -> promise {
        try
            let! found = lookup id

            return
                match present found with
                | Some iri -> Ok iri
                | None -> Error $"No IRI found for {id}."
        with ex ->
            return Error $"Looking up the IRI for {id} failed: {ex.Message}"
      }
    | None, None -> Promise.lift (Error "The term has neither an IRI nor a CURIE.")

let referenceOf (term: Term) (iri: string) : OntologyReference =
    let nonBlank (value: string option) =
        value |> Option.filter (fun v -> not (String.IsNullOrWhiteSpace v))

    {|
        name =
            nonBlank term.name
            |> Option.orElse (nonBlank term.id)
            |> Option.defaultValue iri
        curie = term.id |> Option.defaultValue ""
        iri = iri
    |}
