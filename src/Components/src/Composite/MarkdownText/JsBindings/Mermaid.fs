namespace Arcitisa.Components.Composite.MarkdownText.JsBindings

open Fable.Core

[<AllowNullLiteral>]
type IMermaidRenderResult =
    abstract member svg: string
    abstract member bindFunctions: obj

[<AllowNullLiteral>]
type IMermaid =
    abstract member initialize: obj -> unit

    /// Mermaid draws the diagram into a temporary element to measure it and removes that element afterwards. The
    /// element goes into svgContainingElement, which Mermaid empties first, or at the end of document.body without one.
    abstract member render:
        id: string * text: string * ?svgContainingElement: Browser.Types.HTMLElement -> JS.Promise<IMermaidRenderResult>

[<Erase>]
type Mermaid =

    [<ImportDefault("mermaid")>]
    static member instance: IMermaid = jsNative
