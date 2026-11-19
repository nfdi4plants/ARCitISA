namespace Swate.Components.Composite.TextEditor

open Fable.Core
open Feliz

open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TextEditor.LanguageDetection
open Swate.Components.Composite.TextEditor.SchemaValidation

module internal TextEditorHelpers =

    let registry = ofDescriptions languages

    /// Linting runs on the main thread until it moves into a Web Worker. A YAML pass takes about 50 ms
    /// per 500 KB in Chromium, so longer documents (measured when they open) get no lint for now.
    let maxLintLength = 1_000_000

    let firstLine (text: string) =
        let i = text.IndexOf '\n'
        (if i < 0 then text else text.Substring(0, i)).TrimEnd('\r')

    /// Joins the sync lint of the language and the async lint of the host. The diagnostics of the sync lint come first.
    let combineLint
        (sync: (string -> LintDiagnostic[]) option)
        (extra: (string -> JS.Promise<LintDiagnostic[]>) option)
        : (string -> JS.Promise<LintDiagnostic[]>) option =
        match sync, extra with
        | None, None -> None
        | Some sync, None -> Some(fun text -> promise { return sync text })
        | None, Some extra -> Some extra
        | Some sync, Some extra ->
            Some(fun text -> promise {
                let! found = extra text
                return Array.append (sync text) found
            })

[<Erase; Mangle(false)>]
type TextEditor =

    /// A CodeMirror 6 editor that finds the language of the document from its file name and first
    /// line, unless a language is given. JSON and YAML get syntax diagnostics. With a
    /// schemaValidator they also get diagnostics from the JSON Schema. extraLint adds async diagnostics of the host
    /// for any language. Pass the same function instance across renders, a new instance lints again. keyBindings run edit commands
    /// ahead of the editor's default keys. A read-only editor runs no binding, so its keys go to the default keys.
    [<ReactComponent(true)>]
    static member TextEditor
        (
            documentUri: string,
            initialValue: string,
            ?fileName: string,
            ?language: string,
            ?readOnly: bool,
            ?schemaValidator: Validator.SchemaValidator,
            ?extraLint: string -> JS.Promise<LintDiagnostic[]>,
            ?keyBindings: TextEditorKeyBinding[],
            ?onSave: TextEditorSnapshot -> JS.Promise<bool>,
            ?onDirtyChange: bool -> unit,
            ?onChange: unit -> unit,
            ?onDiagnostics: TextEditorDiagnostic[] -> unit,
            ?handleRef: IRefValue<TextEditorHandle option>,
            ?className: string
        ) =
        let name =
            match fileName with
            | Some n -> n
            | None -> documentUri.Split([| '/'; '\\' |]) |> Array.last

        // initialValue only matters for a new document, so the first line is read then.
        let lang =
            React.useMemo (
                (fun () ->
                    match language with
                    | Some l -> l
                    | None -> detectLanguage TextEditorHelpers.registry name (TextEditorHelpers.firstLine initialValue)
                ),
                [| box documentUri; box name; box language |]
            )

        let lint =
            React.useMemo (
                (fun () ->
                    if initialValue.Length > TextEditorHelpers.maxLintLength then
                        None
                    else
                        TextEditorHelpers.combineLint (Lint.forLanguage lang schemaValidator) extraLint
                ),
                [|
                    box documentUri
                    box lang
                    box schemaValidator
                    box extraLint
                |]
            )

        CodeMirrorEditor.CodeMirrorEditor(
            documentUri,
            initialValue,
            lang,
            ?readOnly = readOnly,
            prose = (lang = "markdown" || lang = "plaintext"),
            ?lint = lint,
            ?keyBindings = keyBindings,
            ?onSave = onSave,
            ?onDirtyChange = onDirtyChange,
            ?onChange = onChange,
            ?onDiagnostics = onDiagnostics,
            ?handleRef = handleRef,
            ?className = className,
            ariaLabel = $"Text editor for {name}"
        )
