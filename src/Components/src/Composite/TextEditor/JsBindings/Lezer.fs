/// Typed bindings for the Lezer syntax tree, the JSON parser of @lezer/json and the highlighting of @lezer/highlight.
module Swate.Components.Composite.TextEditor.JsBindings.Lezer

open Fable.Core
open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror

[<AllowNullLiteral>]
type NodeType =
    abstract name: string
    abstract isError: bool

/// Child and sibling accessors return null at the end.
[<AllowNullLiteral>]
type SyntaxNode =
    abstract name: string
    abstract from: int
    abstract ``to``: int
    abstract ``type``: NodeType
    abstract firstChild: SyntaxNode
    abstract lastChild: SyntaxNode
    abstract nextSibling: SyntaxNode
    abstract prevSibling: SyntaxNode

[<AllowNullLiteral>]
type Tree =
    abstract topNode: SyntaxNode

type Parser =
    abstract parse: string -> Tree

/// The parser that @codemirror/lang-json uses, so its node names match the editor's tree.
[<Import("parser", "@lezer/json")>]
let jsonParser: Parser = jsNative

/// The language of a LanguageSupport, the value that LanguageDescription.load resolves to.
type Language =
    abstract parser: Parser

/// The value that LanguageDescription.load resolves to. It is the same object as the editor's Extension.
type LanguageSupport =
    abstract language: Language

/// Gives the class names for the highlighting tags of a syntax node.
type Highlighter = interface end

/// One rule of tagHighlighter. Compiles to a plain object.
[<Global>]
type TagClass [<ParamObject; Emit("$0")>] (tag: Tag[], ``class``: string) =
    member val tag: Tag[] = jsNative with get, set
    member val ``class``: string = jsNative with get, set

/// A highlighter that gives each node the class of the rule matching its tag. A node with several tags gets the
/// classes of all of them, separated by spaces.
[<Import("tagHighlighter", "@lezer/highlight")>]
let tagHighlighter (rules: TagClass[]) : Highlighter = jsNative

/// Walks the highlighted tree of code. putText gets each run of text with its classes, "" for plain text, and
/// putBreak stands for each line break.
[<Import("highlightCode", "@lezer/highlight")>]
let highlightCode
    (code: string, tree: Tree, highlighter: Highlighter, putText: System.Action<string, string>, putBreak: System.Action) : unit =
    jsNative
