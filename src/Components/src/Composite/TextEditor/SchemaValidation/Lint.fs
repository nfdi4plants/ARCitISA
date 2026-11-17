/// One lint pass over a JSON or YAML text: parse, validate, map errors to source spans.
/// Text in, diagnostics out, so a Web Worker can run it unchanged later.
module Swate.Components.Composite.TextEditor.SchemaValidation.Lint

open System.Text.RegularExpressions
open Fable.Core
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TextEditor.JsBindings.Ajv
open Swate.Components.Composite.TextEditor.JsBindings.Lezer
open Swate.Components.Composite.TextEditor.JsBindings.Yaml
open Swate.Components.Composite.TextEditor.SchemaValidation.Types
open Swate.Components.Composite.TextEditor.SchemaValidation.Validator

let private syntaxDiagnostic (text: string) (from: int) (``to``: int) (message: string) : LintDiagnostic = {|
    from = min from text.Length
    ``to`` = min text.Length (max ``to`` (from + 1))
    severity = "error"
    message = message
    source = "syntax"
|}

let private isSpace (c: char) =
    c = ' ' || c = '\t' || c = '\r' || c = '\n'

let private tryParseJson (text: string) : Result<obj, string> =
    try
        Ok(JS.JSON.parse text)
    with e ->
        Error e.Message

/// V8 reports "... in JSON at position N (line L column C)". The position is only a fallback,
/// Lezer's error node is usually closer to the cause.
let private jsonSyntax (text: string) (message: string) : LintDiagnostic =
    let v8Position =
        let m = Regex.Match(message, @"position (\d+)")
        if m.Success then Some(int m.Groups.[1].Value) else None

    let pos =
        JsonLocator.firstSyntaxError (jsonParser.parse text)
        |> Option.map (fun s -> s.From)
        |> Option.orElse v8Position
        |> Option.defaultValue 0
        |> min (max 0 (text.Length - 1))

    let mutable before = pos - 1

    while before >= 0 && isSpace text.[before] do
        before <- before - 1

    let mutable after = pos

    while after < text.Length && isSpace text.[after] do
        after <- after + 1

    // A comma right before a closing bracket: V8 says "Expected double-quoted property name" at the bracket.
    if
        before >= 0
        && text.[before] = ','
        && after < text.Length
        && (text.[after] = '}' || text.[after] = ']')
    then
        syntaxDiagnostic text before (before + 1) "JSON syntax error: Trailing comma."
    else
        let withoutPosition =
            Regex.Replace(message, @" in JSON at position \d+( \(line \d+ column \d+\))?", "")

        let shortened = Regex.Replace(withoutPosition, "^(Unexpected token .*?), .*$", "$1")

        let clean =
            shortened.Replace("Unexpected end of JSON input", "The text ends too early")

        syntaxDiagnostic text pos (pos + 1) $"JSON syntax error: {clean.TrimEnd('.')}."

/// Lints JSON. Without a validator only syntax errors are reported.
let lintJson (validator: SchemaValidator option) (text: string) : LintDiagnostic[] =
    match tryParseJson text with
    | Error message -> [| jsonSyntax text message |]
    | Ok data ->
        match validator with
        | None -> [||]
        | Some v ->
            match v.Validate data with
            | [||] -> [||]
            | errors ->
                Diagnostics.toDiagnostics errors (JsonLocator.create (jsonParser.parse text) text) text data v.Index

let private yamlError (text: string) (e: YamlError) : LintDiagnostic =
    let first = e.message.Split('\n').[0].TrimEnd('.', ':')

    let from, ``to`` =
        if isNull (box e.pos) || e.pos.Length < 2 then
            0, 1
        else
            e.pos.[0], e.pos.[1]

    // yaml often reports one character, such as the first letter of a duplicate key. The mark then covers the
    // word that starts there, so it is visible.
    let isWordChar (c: char) =
        System.Char.IsLetterOrDigit c || c = '_' || c = '-' || c = '.'

    let mutable wordEnd = from

    while ``to`` - from <= 1 && wordEnd < text.Length && isWordChar text.[wordEnd] do
        wordEnd <- wordEnd + 1

    syntaxDiagnostic text from (max ``to`` wordEnd) $"YAML syntax error: {first}."

/// Lints YAML (version 1.2, so "yes" is a string). Without a validator only syntax errors are reported.
let lintYaml (validator: SchemaValidator option) (text: string) : LintDiagnostic[] =
    let doc = parseDocument (text, {| prettyErrors = false |})

    if doc.errors.Length > 0 then
        doc.errors |> Array.map (yamlError text)
    else
        match validator with
        | None -> [||]
        | Some v ->
            match
                (try
                    Ok(doc.toJS {| maxAliasCount = 1000 |})
                 with e ->
                     Error e.Message)
            with
            | Error message -> [| syntaxDiagnostic text 0 1 $"YAML error: {message}" |]
            | Ok data ->
                match v.Validate data with
                | [||] -> [||]
                | errors -> Diagnostics.toDiagnostics errors (YamlLocator.create doc) text data v.Index

/// The lint function for an editor language, if it has one: JSON and YAML.
let forLanguage (language: string) (validator: SchemaValidator option) : (string -> LintDiagnostic[]) option =
    match language with
    | "json" -> Some(lintJson validator)
    | "yaml" -> Some(lintYaml validator)
    | _ -> None

/// Ajv's own errors for a text, before any shaping. For debugging schemas.
let rawErrors (validator: SchemaValidator) (language: string) (text: string) : Result<AjvError[], string> =
    let data =
        match language with
        | "yaml" ->
            let doc = parseDocument (text, {| prettyErrors = false |})

            if doc.errors.Length > 0 then
                Error "The YAML does not parse."
            else
                try
                    Ok(doc.toJS {| maxAliasCount = 1000 |})
                with e ->
                    Error e.Message
        | _ -> tryParseJson text |> Result.mapError (fun _ -> "The JSON does not parse.")

    data |> Result.map validator.Validate
