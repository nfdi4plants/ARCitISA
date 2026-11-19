namespace Swate.Components.Composite.MarkdownText

open Fable.Core
open Feliz

open Swate.Components.Composite.MarkdownText.JsBindings
open Swate.Components.Composite.MarkdownText.Links

[<Erase; Mangle(false)>]
type MarkdownView =

    /// The Markdown preview with sanitized raw HTML. With a link context, links and images of the ARC resolve
    /// against the context's base folder.
    [<ReactComponent>]
    static member MarkdownView(source: string, ?className: string, ?linkContext: IMarkdownLinkContext) =
        LinkContext.LinkCtx.Provider(
            linkContext,
            ReactMDEditor.MarkdownPreview(
                source,
                ?className = className,
                components = Preview.components,
                rehypePlugins = Preview.rehypePlugins
            )
        )
