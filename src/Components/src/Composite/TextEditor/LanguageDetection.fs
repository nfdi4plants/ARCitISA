/// Picks the editor language of a file from its name and first line, and the line separator of a text.
/// The language registry comes in as data, so the tests need no CodeMirror.
module Swate.Components.Composite.TextEditor.LanguageDetection

open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror

/// id is the lowercased name of a @codemirror/language-data entry, such as "json", "shell" or "f#".
/// Extensions have no leading dot.
type LanguageEntry = {|
    id: string
    extensions: string[]
    matchesFileName: string -> bool
|}

let languageId (description: LanguageDescription) : string = description.name.ToLowerInvariant()

let ofDescriptions (descriptions: LanguageDescription[]) : LanguageEntry[] =
    descriptions
    |> Array.map (fun d -> {|
        id = languageId d
        extensions = d.extensions
        matchesFileName =
            fun name ->
                match d.filename with
                | Some pattern -> pattern.test name
                | None -> false
    |})

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
    |> Option.defaultValue "plaintext"

/// The first line break of the text decides: "\r\n", "\r" or "\n". A text without line breaks gets "\n".
/// Mixed line endings become this one on save.
let detectLineSeparator (text: string) : string =
    let i = text.IndexOfAny [| '\r'; '\n' |]

    if i < 0 || text.[i] = '\n' then "\n"
    elif i + 1 < text.Length && text.[i + 1] = '\n' then "\r\n"
    else "\r"
