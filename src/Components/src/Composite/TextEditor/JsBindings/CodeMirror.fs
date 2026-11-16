/// Typed bindings for the parts of CodeMirror 6 that the TextEditor uses.
module Swate.Components.Composite.TextEditor.JsBindings.CodeMirror

open Browser.Types
open Fable.Core

/// A CM6 extension. Arrays of extensions are extensions too, see `extensions`.
type Extension = interface end

/// An effect for a transaction, such as a compartment reconfiguration.
type StateEffect = interface end

/// A facet takes values through `of`.
type Facet<'T> =
    abstract ``of``: 'T -> Extension

/// CM6 accepts nested arrays wherever it takes one extension.
let inline extensions (items: Extension[]) : Extension = unbox items

let noExtension: Extension = unbox [||]

[<AllowNullLiteral>]
type Line =
    abstract number: int
    abstract from: int
    abstract ``to``: int

/// The immutable document of a state.
[<AllowNullLiteral>]
type Text =
    abstract length: int
    abstract toString: unit -> string
    /// Compares the content. Shared tree parts make this cheap after an undo back to an earlier version.
    abstract eq: Text -> bool
    abstract lineAt: pos: int -> Line

[<AllowNullLiteral>]
type EditorState =
    abstract doc: Text

[<AllowNullLiteral>]
type Compartment =
    abstract ``of``: Extension -> Extension
    abstract reconfigure: Extension -> StateEffect

type CompartmentStatic =
    [<EmitConstructor>]
    abstract Create: unit -> Compartment

type EditorStateConfig = {| doc: string; extensions: Extension |}

type EditorStateStatic =
    abstract create: config: EditorStateConfig -> EditorState
    abstract readOnly: Facet<bool>
    /// Only this separator splits lines, and doc.toString() joins with it, so CRLF files stay CRLF.
    abstract lineSeparator: Facet<string>

/// Compiles to a plain object. Unset fields stay out of the object.
[<Global>]
type TransactionSpec
    [<ParamObject; Emit("$0")>]
    (?effects: StateEffect[], ?changes: obj, ?selection: obj, ?scrollIntoView: bool) =
    member val effects: StateEffect[] option = jsNative with get, set
    member val changes: obj option = jsNative with get, set
    member val selection: obj option = jsNative with get, set
    member val scrollIntoView: bool option = jsNative with get, set

[<AllowNullLiteral>]
type EditorView =
    abstract state: EditorState
    abstract dom: HTMLElement
    abstract contentDOM: HTMLElement
    abstract dispatch: TransactionSpec -> unit
    abstract setState: EditorState -> unit
    abstract focus: unit -> unit
    abstract destroy: unit -> unit

[<AllowNullLiteral>]
type ViewUpdate =
    abstract docChanged: bool
    abstract state: EditorState
    abstract view: EditorView

type EditorViewConfig = {|
    state: EditorState
    parent: HTMLElement
|}

type EditorViewStatic =
    [<EmitConstructor>]
    abstract Create: config: EditorViewConfig -> EditorView

    /// Keys are CSS selectors, "&" is the editor itself. Values are style objects.
    abstract theme: spec: obj -> Extension
    abstract lineWrapping: Extension
    abstract updateListener: Facet<ViewUpdate -> unit>
    abstract contentAttributes: Facet<obj>
    abstract editable: Facet<bool>

type KeyBinding = {|
    key: string
    run: EditorView -> bool
    preventDefault: bool
|}

type PrecStatic =
    abstract highest: Extension -> Extension

[<Import("EditorState", "@codemirror/state")>]
let EditorStateApi: EditorStateStatic = jsNative

[<Import("Compartment", "@codemirror/state")>]
let CompartmentApi: CompartmentStatic = jsNative

[<Import("Prec", "@codemirror/state")>]
let Prec: PrecStatic = jsNative

[<Import("EditorView", "@codemirror/view")>]
let EditorViewApi: EditorViewStatic = jsNative

[<Import("keymap", "@codemirror/view")>]
let keymap: Facet<KeyBinding[]> = jsNative

/// Line numbers, history, folding, bracket matching, search, autocompletion and their key bindings.
[<Import("basicSetup", "codemirror")>]
let basicSetup: Extension = jsNative

/// A key binding object from @codemirror/commands, passed to keymap as it is.
type CommandBinding = interface end

[<Import("indentWithTab", "@codemirror/commands")>]
let indentWithTab: CommandBinding = jsNative

// Languages and highlighting

[<AllowNullLiteral>]
type RegExpLike =
    abstract test: string -> bool

/// One entry of @codemirror/language-data. load() imports the language package on demand.
[<AllowNullLiteral>]
type LanguageDescription =
    abstract name: string
    abstract alias: string[]
    abstract extensions: string[]
    /// Undefined for most languages.
    abstract filename: RegExpLike option
    abstract load: unit -> JS.Promise<Extension>

[<Import("languages", "@codemirror/language-data")>]
let languages: LanguageDescription[] = jsNative

/// A highlighting tag of @lezer/highlight.
type Tag = interface end

type Tags =
    abstract propertyName: Tag
    abstract definition: Tag -> Tag
    abstract string: Tag
    abstract special: Tag -> Tag
    abstract number: Tag
    abstract integer: Tag
    abstract float: Tag
    abstract bool: Tag
    abstract ``null``: Tag
    abstract atom: Tag
    abstract keyword: Tag
    abstract comment: Tag
    abstract lineComment: Tag
    abstract blockComment: Tag
    abstract punctuation: Tag
    abstract separator: Tag
    abstract brace: Tag
    abstract squareBracket: Tag
    abstract meta: Tag
    abstract labelName: Tag
    abstract typeName: Tag
    abstract variableName: Tag
    abstract operator: Tag
    abstract heading: Tag
    abstract emphasis: Tag
    abstract strong: Tag
    abstract link: Tag
    abstract url: Tag
    abstract monospace: Tag
    abstract quote: Tag
    abstract invalid: Tag

[<Import("tags", "@lezer/highlight")>]
let tags: Tags = jsNative

/// One rule of a highlight style. Compiles to a plain object, unset fields stay out.
[<Global>]
type TagStyle
    [<ParamObject; Emit("$0")>]
    (tag: Tag[], ?color: string, ?fontStyle: string, ?fontWeight: string, ?textDecoration: string) =
    member val tag: Tag[] = jsNative with get, set
    member val color: string option = jsNative with get, set
    member val fontStyle: string option = jsNative with get, set
    member val fontWeight: string option = jsNative with get, set
    member val textDecoration: string option = jsNative with get, set

type HighlightStyle = interface end

type HighlightStyleStatic =
    abstract define: TagStyle[] -> HighlightStyle

[<Import("HighlightStyle", "@codemirror/language")>]
let HighlightStyleApi: HighlightStyleStatic = jsNative

[<Import("syntaxHighlighting", "@codemirror/language")>]
let syntaxHighlighting (style: HighlightStyle) : Extension = jsNative

// Linting

/// The diagnostic shape of @codemirror/lint. Severity is "error", "warning", "info" or "hint".
type Diagnostic = {|
    from: int
    ``to``: int
    severity: string
    message: string
    source: string
|}

type LintConfig = {| delay: int |}

[<Import("linter", "@codemirror/lint")>]
let linter (source: EditorView -> JS.Promise<Diagnostic[]>, config: LintConfig) : Extension = jsNative

[<Import("lintGutter", "@codemirror/lint")>]
let lintGutter () : Extension = jsNative
