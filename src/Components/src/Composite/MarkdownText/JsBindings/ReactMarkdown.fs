namespace Swate.Components.Composite.MarkdownText.JsBindings

open Fable.Core
open Feliz

/// The allowed properties of the two tags whose list the preview extends. An entry is a property name, or an array
/// of a property name followed by the values it may have.
[<AllowNullLiteral>]
type ISanitizeAttributes =
    abstract member div: obj[] option with get, set
    abstract member p: obj[] option with get, set

/// The part of the rehype-sanitize schema that the preview changes. The schema has more fields, which stay as they are.
[<AllowNullLiteral>]
type ISanitizeSchema =
    abstract member tagNames: string[] with get, set
    abstract member attributes: ISanitizeAttributes with get, set

[<AllowNullLiteral>]
type IHastProperties =
    abstract member className: string[] option with get, set
    abstract member id: string option

/// A node of a hast tree, as the code renderer reads it and the preview's own rehype step changes it.
[<AllowNullLiteral>]
type IHastNode =
    abstract member ``type``: string
    abstract member tagName: string option
    /// The text of a text node.
    abstract member value: string option
    abstract member properties: IHastProperties option
    abstract member children: ResizeArray<IHastNode> option with get, set

/// react-markdown and the remark and rehype plugins of the Markdown preview.
[<Erase>]
type ReactMarkdown =

    /// GitHub Flavored Markdown: tables, task lists, strikethrough, autolinks and footnotes.
    [<ImportDefault("remark-gfm")>]
    static member remarkGfm: obj = jsNative

    /// GitHub alerts such as "> [!NOTE]". It marks them with the classes markdown-alert and markdown-alert-<kind>.
    [<Import("remarkAlert", "remark-github-blockquote-alert")>]
    static member remarkAlert: obj = jsNative

    [<ImportDefault("rehype-raw")>]
    static member rehypeRaw: obj = jsNative

    /// Gives every heading without an id one made from its text.
    [<ImportDefault("rehype-slug")>]
    static member rehypeSlug: obj = jsNative

    [<ImportDefault("rehype-sanitize")>]
    static member rehypeSanitize: obj = jsNative

    [<Import("defaultSchema", "rehype-sanitize")>]
    static member defaultSchema: ISanitizeSchema = jsNative

    /// Adds a link to its own id to every heading with an id.
    [<ImportDefault("rehype-autolink-headings")>]
    static member rehypeAutolinkHeadings: obj = jsNative

    /// Renders children, the Markdown source. components maps tag names to React components.
    [<ReactComponent("default", "react-markdown")>]
    static member Markdown(children: string, ?components: obj, ?remarkPlugins: obj[], ?rehypePlugins: obj[]) =
        React.Imported()
