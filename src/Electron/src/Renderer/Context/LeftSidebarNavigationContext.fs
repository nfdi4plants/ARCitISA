module Renderer.Context.LeftSidebarNavigationContext

open Feliz
open Renderer.Types

/// Lets content outside the left sidebar bring one of its pages to the front, for example the File Explorer for a
/// folder link in a Markdown file.
type LeftSidebarNavigation = {
    /// Opens the sidebar and shows the page.
    showPage: LeftSidebarPage -> unit
}

let LeftSidebarNavigationCtx =
    React.createContext<LeftSidebarNavigation> ({ showPage = ignore })

[<Hook>]
let useLeftSidebarNavigationCtx () =
    React.useContext LeftSidebarNavigationCtx
