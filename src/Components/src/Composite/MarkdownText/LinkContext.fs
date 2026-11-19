module Swate.Components.Composite.MarkdownText.LinkContext

open Feliz

open Swate.Components.Composite.MarkdownText.Links

/// The host context of the preview. None means the host has no ARC.
let LinkCtx = React.createContext<IMarkdownLinkContext option> (None)

[<Hook>]
let useLinkCtx () = React.useContext LinkCtx
