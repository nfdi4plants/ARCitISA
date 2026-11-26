module Arcitisa.Components.Composite.MarkdownText.JsBindings.HastFromHtml

open Fable.Core

/// The properties of an element that the link collection reads.
[<AllowNullLiteral>]
type HastProperties =
    abstract member src: string option
    abstract member href: string option

[<AllowNullLiteral>]
type HastNode =
    abstract member ``type``: string
    abstract member tagName: string option
    abstract member properties: HastProperties option
    abstract member children: HastNode[] option

/// Parses HTML like a browser does. With fragment = true the result has no html, head and body wrappers.
[<Import("fromHtml", "hast-util-from-html")>]
let fromHtml (value: string, options: {| fragment: bool |}) : HastNode = jsNative
