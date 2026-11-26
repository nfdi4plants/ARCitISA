/// The editor languages: which language a file has, from its name and first line, and how the editor treats it.
/// Also the line separator of a text. The detection takes the registry as data, so the tests pass their own.
module Arcitisa.Components.Composite.TextEditor.LanguageDetection

open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror

/// A language whose text is prose. Its lines wrap and the browser's spellcheck runs on it, except on the text of
/// these syntax nodes, which is code, markup or an address. The names are node names of the language's parser.
type ProseRules = {|
    skippedNodes: string[]
    skippedTagsIn: string[]
|}

/// id is the lowercased name of a @codemirror/language-data entry, such as "json", "shell" or "f#".
/// Extensions have no leading dot. prose is None for a code language.
type LanguageEntry = {|
    id: string
    extensions: string[]
    matchesFileName: string -> bool
    prose: ProseRules option
|}

/// The node names of @lezer/markdown, which @codemirror/lang-markdown parses with.
let private markdownProse: ProseRules = {|
    skippedNodes = [|
        "HTMLTag"
        "Comment"
        "CommentBlock"
        "ProcessingInstruction"
        "ProcessingInstructionBlock"
        "InlineCode"
        "FencedCode"
        "CodeBlock"
        "URL"
        "Autolink"
    |]
    skippedTagsIn = [| "HTMLBlock" |]
|}

/// The prose languages of @codemirror/language-data by id. All others are code.
let private proseLanguages = [ ("markdown", markdownProse) ]

/// The language of a file that no other language matches. It has no syntax tree, so the spellcheck covers all of it.
let plainText: LanguageEntry = {|
    id = "plaintext"
    extensions = [||]
    matchesFileName = fun _ -> false
    prose =
        Some {|
            skippedNodes = [||]
            skippedTagsIn = [||]
        |}
|}

let languageId (description: LanguageDescription) : string = description.name.ToLowerInvariant()

let ofDescriptions (descriptions: LanguageDescription[]) : LanguageEntry[] =
    descriptions
    |> Array.map (fun d ->
        let id = languageId d

        {|
            id = id
            extensions = d.extensions
            matchesFileName =
                fun name ->
                    match d.filename with
                    | Some pattern -> pattern.test name
                    | None -> false
            prose = proseLanguages |> List.tryFind (fun (p, _) -> p = id) |> Option.map snd
        |}
    )

/// The languages of @codemirror/language-data and plain text.
let registry: LanguageEntry[] =
    Array.append (ofDescriptions languages) [| plainText |]

/// The prose rules of a language, None for a code language and for an id the registry does not have.
let proseOf (languages: LanguageEntry[]) (id: string) : ProseRules option =
    languages
    |> Array.tryFind (fun l -> l.id = id)
    |> Option.bind (fun l -> l.prose)

let private overrides: (string * string) list = [
    "cwl", "yaml"
    "yml", "yaml"
    "yaml", "yaml"
    "md", "markdown"
    "markdown", "markdown"
    "json", "json"
    "jsonld", "json"
    "jsonc", "json"
    "fsx", "f#"
    "txt", "plaintext"
    "log", "plaintext"
]

let private markdownNames = [ "readme"; "changelog"; "contributing" ]

let private lastSegment (path: string) : string =
    path.Split([| '/'; '\\' |]) |> Array.last

let private extensionOf (name: string) : string option =
    let i = name.LastIndexOf '.'

    if i >= 0 && i < name.Length - 1 then
        Some(name.Substring(i + 1).ToLowerInvariant())
    else
        None

let private interpreterLanguage (interpreter: string) : string option =
    if interpreter.StartsWith "python" then
        Some "python"
    else
        match interpreter with
        | "sh"
        | "bash"
        | "zsh" -> Some "shell"
        | "Rscript" -> Some "r"
        | "perl" -> Some "perl"
        | "ruby" -> Some "ruby"
        | "node" -> Some "javascript"
        | "pwsh" -> Some "powershell"
        | _ -> None

/// Handles "#!/usr/bin/env X" and "#!/bin/X", with arguments after X.
let private shebangLanguage (firstLine: string) : string option =
    if isNull firstLine || not (firstLine.StartsWith "#!") then
        None
    else
        let tokens =
            firstLine.Substring(2).Split([| ' '; '\t' |], System.StringSplitOptions.RemoveEmptyEntries)

        match Array.toList tokens with
        | [] -> None
        | program :: rest ->
            let name = lastSegment program

            let interpreter =
                if name = "env" then
                    rest |> List.tryFind (fun t -> not (t.StartsWith "-"))
                else
                    Some name

            interpreter |> Option.map lastSegment |> Option.bind interpreterLanguage

/// The order is: extension overrides, file name patterns of the registry, extensions of the registry,
/// README-like names, shebang, then "plaintext".
let detectLanguage (languages: LanguageEntry[]) (fileName: string) (firstLine: string) : string =
    let name = lastSegment fileName
    let ext = extensionOf name

    let registryHas id =
        languages |> Array.exists (fun l -> l.id = id)

    let overrideHit =
        ext
        |> Option.bind (fun e -> overrides |> List.tryFind (fun (k, _) -> k = e) |> Option.map snd)

    let nameHit =
        languages
        |> Array.tryFind (fun l -> l.matchesFileName name)
        |> Option.map (fun l -> l.id)

    let extHit =
        ext
        |> Option.bind (fun e ->
            languages
            |> Array.tryFind (fun l -> l.extensions |> Array.exists (fun x -> x.ToLowerInvariant() = e))
            |> Option.map (fun l -> l.id)
        )

    let readmeHit =
        if List.contains (name.ToLowerInvariant()) markdownNames then
            Some "markdown"
        else
            None

    let shebangHit = shebangLanguage firstLine |> Option.filter registryHas

    [ overrideHit; nameHit; extHit; readmeHit; shebangHit ]
    |> List.tryPick id
    |> Option.defaultValue plainText.id

/// The first line break of the text decides: "\r\n", "\r" or "\n". A text without line breaks gets "\n".
/// Mixed line endings become this one on save.
let detectLineSeparator (text: string) : string =
    let i = text.IndexOfAny [| '\r'; '\n' |]

    if i < 0 || text.[i] = '\n' then "\n"
    elif i + 1 < text.Length && text.[i + 1] = '\n' then "\r\n"
    else "\r"
