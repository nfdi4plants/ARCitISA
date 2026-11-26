module Arcitisa.Components.Composite.MarkdownText.JsBindings.Mdast

open Fable.Core

[<AllowNullLiteral>]
type Point =
    abstract member offset: int

[<AllowNullLiteral>]
type Position =
    abstract member start: Point
    abstract member ``end``: Point

/// The parts of an mdast node that the editing code reads. identifier and url exist on definitions,
/// identifier also on link references.
[<AllowNullLiteral>]
type MdastNode =
    abstract member ``type``: string
    abstract member children: MdastNode[] option
    abstract member position: Position option
    abstract member identifier: string option
    abstract member url: string option
    /// The raw text of "html" nodes.
    abstract member value: string option

[<Import("fromMarkdown", "mdast-util-from-markdown")>]
let private fromMarkdown
    (
        text: string,
        options:
            {|
                extensions: obj[]
                mdastExtensions: obj[]
            |}
    ) : MdastNode =
    jsNative

[<Import("gfm", "micromark-extension-gfm")>]
let private gfm () : obj = jsNative

[<Import("gfmFromMarkdown", "mdast-util-gfm")>]
let private gfmFromMarkdown () : obj = jsNative

/// Parses Markdown with the same extensions the preview uses (CommonMark plus GFM).
let parse (text: string) : MdastNode =
    fromMarkdown (
        text,
        {|
            extensions = [| gfm () |]
            mdastExtensions = [| gfmFromMarkdown () |]
        |}
    )
