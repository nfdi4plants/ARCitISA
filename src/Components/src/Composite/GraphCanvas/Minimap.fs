module Swate.Components.Composite.GraphCanvas.Minimap

open Swate.Components.Composite.GraphCanvas.Types

// The geometry of the minimap in plain numbers. Layout units are the units of the drawing before zoom.
// A point of the drawing at (x, y) sits at (OffsetX + x * Zoom, OffsetY + y * Zoom) in the scroll
// coordinates of the root.

/// The largest size in px of the drawing in the minimap.
let maxWidth = 200.0
let maxHeight = 140.0

let private clamp (value: float) (low: float) (high: float) : float = max low (min value high)

// NaN is not greater than zero, so this rejects NaN as well.
let private positiveFinite (value: float) : bool =
    value > 0.0 && not (System.Double.IsInfinity value)

/// Px of the minimap per layout unit. The whole layout fits into maxWidth by maxHeight.
let scale (layoutWidth: float) (layoutHeight: float) : float =
    if positiveFinite layoutWidth && positiveFinite layoutHeight then
        min (maxWidth / layoutWidth) (maxHeight / layoutHeight)
    else
        0.0

/// True when the root has something to scroll.
let overflows (g: ScrollGeometry) : bool =
    g.ClientWidth > 0.0
    && g.ClientHeight > 0.0
    && (g.ScrollWidth > g.ClientWidth || g.ScrollHeight > g.ClientHeight)

/// The part of the layout that the root shows, in layout units. Both ends are clamped to the layout before
/// they are subtracted, so a visible area that reaches past the drawing shrinks to the drawing.
let viewport (g: ScrollGeometry) (layoutWidth: float) (layoutHeight: float) : Rect option =
    if
        positiveFinite layoutWidth
        && positiveFinite layoutHeight
        && positiveFinite g.Zoom
        && positiveFinite g.ClientWidth
        && positiveFinite g.ClientHeight
    then
        Some {
            Left = clamp ((g.ScrollLeft - g.OffsetX) / g.Zoom) 0.0 layoutWidth
            Top = clamp ((g.ScrollTop - g.OffsetY) / g.Zoom) 0.0 layoutHeight
            Right = clamp ((g.ScrollLeft + g.ClientWidth - g.OffsetX) / g.Zoom) 0.0 layoutWidth
            Bottom = clamp ((g.ScrollTop + g.ClientHeight - g.OffsetY) / g.Zoom) 0.0 layoutHeight
        }
    else
        None

/// The scroll position (left, top) that puts the layout point (x, y) in the middle of the visible area,
/// kept inside the scroll range of the root.
let centerOn (g: ScrollGeometry) (x: float) (y: float) : float * float =
    let maxLeft = max 0.0 (g.ScrollWidth - g.ClientWidth)
    let maxTop = max 0.0 (g.ScrollHeight - g.ClientHeight)

    clamp (g.OffsetX + x * g.Zoom - g.ClientWidth / 2.0) 0.0 maxLeft,
    clamp (g.OffsetY + y * g.Zoom - g.ClientHeight / 2.0) 0.0 maxTop
