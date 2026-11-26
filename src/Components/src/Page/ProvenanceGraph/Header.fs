/// The rules of the explorer header that need no DOM.
module Arcitisa.Components.Page.ProvenanceGraph.Header

open Arcitisa.Components.Page.ProvenanceGraph.Types

/// The header switches to its narrow layout when the full content does not fit on one line.
/// A fraction of a pixel counts against fitting, so rounding never pushes the full header past its line.
/// The answer depends on the two widths only, so the header cannot flip back and forth.
let isNarrow (naturalWidth: float) (availableWidth: float) : bool =
    ceil naturalWidth > floor availableWidth

/// With three or fewer crumbs nothing collapses. Otherwise the narrow header keeps the first crumb
/// and the last two, and the crumbs in between go into a menu.
let visibleCrumbs (crumbs: Crumb[]) (narrow: bool) : VisibleCrumbs =
    if narrow && crumbs.Length > 3 then
        {
            Visible = Array.append [| crumbs.[0] |] (Array.sub crumbs (crumbs.Length - 2) 2)
            Hidden = Array.sub crumbs 1 (crumbs.Length - 3)
        }
    else
        { Visible = crumbs; Hidden = [||] }
