namespace Arcitisa.Components.Composite.MarkdownText

open Fable.Core
open Feliz

open Arcitisa.Components.Composite.MarkdownText.JsBindings
open Arcitisa.Components.Composite.MarkdownText.Helper
open Arcitisa.Components.Composite.MarkdownText.Links.Types

[<Erase; Mangle(false)>]
type MarkdownTextViewer =

    /// Markdown with GitHub Flavored Markdown, GitHub alerts, links on the headings and sanitized raw HTML. With a
    /// link context, links and images of the ARC resolve against the context's base folder. className goes to the
    /// prose container, for example for padding.
    [<ReactComponent>]
    static member MarkdownTextViewer(source: string, ?className: string, ?linkContext: IMarkdownLinkContext) =
        LinkContext.LinkCtx.Provider(
            linkContext,
            Html.div [
                prop.className [
                    ViewerRendering.proseClassName
                    match className with
                    | Some className -> className
                    | None -> ()
                ]
                prop.children [
                    ReactMarkdown.Markdown(
                        source,
                        components = ViewerRendering.components,
                        remarkPlugins = ViewerRendering.remarkPlugins,
                        rehypePlugins = ViewerRendering.rehypePlugins
                    )
                ]
            ]
        )
