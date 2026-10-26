module Swate.Components.Composite.GraphCanvas.Minimap

open Swate.Components.Composite.GraphCanvas.Types

// The geometry of the minimap in plain numbers. Layout units are the units of the drawing before zoom.
// A point of the drawing at (x, y) sits at (OffsetX + x * Zoom, OffsetY + y * Zoom) in the scroll
// coordinates of the root.

let private clamp (value: float) (low: float) (high: float) : float = max low (min value high)

// NaN is not greater than zero, so this rejects NaN as well.
let private positiveFinite (value: float) : bool =
    value > 0.0 && not (System.Double.IsInfinity value)

/// The smallest box in px that gets a minimap. A smaller box would lose too much of the graph under it.
let minBoxWidth = 260.0
let minBoxHeight = 220.0

let fitsBox (boxWidth: float) (boxHeight: float) : bool =
    boxWidth >= minBoxWidth && boxHeight >= minBoxHeight

/// The largest size in px of the drawing in the minimap: 200 by 140, and at most 40 and 35 percent of the box.
let maxSize (boxWidth: float) (boxHeight: float) : float * float =
    min 200.0 (0.4 * boxWidth), min 140.0 (0.35 * boxHeight)

/// The shortest side in px of the frame around the drawing. A thin graph keeps a frame that is easy to hit.
let minFrameSide = 48.0

/// Px of the minimap per layout unit. The whole layout fits into the largest size for the box.
let scale (boxWidth: float) (boxHeight: float) (layoutWidth: float) (layoutHeight: float) : float =
    let maxWidth, maxHeight = maxSize boxWidth boxHeight

    if
        positiveFinite layoutWidth
        && positiveFinite layoutHeight
        && positiveFinite maxWidth
        && positiveFinite maxHeight
    then
        min (maxWidth / layoutWidth) (maxHeight / layoutHeight)
    else
        0.0

/// Width and height in px of the frame around the drawing. The drawing sits in the middle of the frame, and the
/// frame grows to the shortest side where the drawing is thinner, but never past the largest size for the box.
let frameSize (boxWidth: float) (boxHeight: float) (layoutWidth: float) (layoutHeight: float) : float * float =
    let k = scale boxWidth boxHeight layoutWidth layoutHeight
    let maxWidth, maxHeight = maxSize boxWidth boxHeight

    min maxWidth (max (k * layoutWidth) minFrameSide), min maxHeight (max (k * layoutHeight) minFrameSide)

/// The layout point under a pointer that is x and y px from the top left corner of the frame. A pointer in the
/// empty margin of the frame maps to the nearest point of the layout.
let pointerToLayout
    (boxWidth: float)
    (boxHeight: float)
    (layoutWidth: float)
    (layoutHeight: float)
    (x: float)
    (y: float)
    : float * float =
    let k = scale boxWidth boxHeight layoutWidth layoutHeight

    if k > 0.0 then
        let frameWidth, frameHeight = frameSize boxWidth boxHeight layoutWidth layoutHeight
        let marginX = (frameWidth - k * layoutWidth) / 2.0
        let marginY = (frameHeight - k * layoutHeight) / 2.0

        clamp ((x - marginX) / k) 0.0 layoutWidth, clamp ((y - marginY) / k) 0.0 layoutHeight
    else
        0.0, 0.0

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
