/// Typed bindings for the Lezer syntax tree and the JSON parser of @lezer/json.
module Swate.Components.Composite.TextEditor.JsBindings.Lezer

open Fable.Core

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
