module Swate.Components.Composite.GraphCanvas.ImageExport

open Browser.Dom
open Browser.Types
open Fable.Core
open Swate.Components
open Swate.Components.JsBindings

// Turns the drawn graph into a PNG of the whole layout. The markup is a self-contained copy of the live SVG.
// Colors come from custom properties and color-mix() in inline styles, which an image cannot resolve on its own.
// The copy therefore holds the computed values of every element, read while the original is attached.

let private usable (value: float) : bool =
    value > 0.0 && not (System.Double.IsInfinity value)

/// Pixels of the PNG per layout unit, at most 2. A side stays within 16384 px and the area within 16 Mi px, so one
/// RGBA surface stays near 64 MiB, below what Chromium refuses.
let scale (layoutWidth: float) (layoutHeight: float) : float =
    if usable layoutWidth && usable layoutHeight then
        min
            (min 2.0 (min (16384.0 / layoutWidth) (16384.0 / layoutHeight)))
            (sqrt (16777216.0 / (layoutWidth * layoutHeight)))
    else
        0.0

/// The CSS properties that the copy takes from the computed style of each element.
let styleProperties: string[] = [|
    "color"
    "fill"
    "fill-opacity"
    "fill-rule"
    "stroke"
    "stroke-opacity"
    "stroke-width"
    "stroke-dasharray"
    "stroke-dashoffset"
    "stroke-linecap"
    "stroke-linejoin"
    "stroke-miterlimit"
    "opacity"
    "visibility"
    "font-family"
    "font-size"
    "font-weight"
    "font-style"
    "letter-spacing"
    "word-spacing"
    "text-anchor"
    "dominant-baseline"
    "alignment-baseline"
    "baseline-shift"
    "text-rendering"
    "shape-rendering"
    "paint-order"
    "vector-effect"
|]

let private svgNamespace = "http://www.w3.org/2000/svg"

// The element and all its descendants in document order.
let private withDescendants (root: Element) : Element[] =
    let found = root.querySelectorAll "*"

    Array.append [| root |] [| for i in 0 .. found.length - 1 -> found.[i] |]

// An image has no use for the attributes that serve the interactive page. Ids, transforms, geometry and marker
// attributes stay, because the drawing needs them.
let private dropInteractionAttributes (el: Element) : unit =
    for name in Dom.attributeNames el do
        if
            name = "class"
            || name = "tabindex"
            || name = "role"
            || name = "data-testid"
            || name.StartsWith "aria-"
        then
            el.removeAttribute name

// The color of the page behind the graph. A probe element resolves the custom property of the theme.
let private backgroundColor (themeRoot: HTMLElement) : string =
    let probe = document.createElement "span"
    Dom.setElementStyleProperty probe "color" "var(--color-base-100)"
    themeRoot.appendChild probe |> ignore
    let color = (Dom.computedStyle probe).getPropertyValue "color"
    themeRoot.removeChild probe |> ignore
    color

/// The SVG of the graph as a standalone document of the size of the layout, on the background color of the page.
/// Call it while svg is attached, because the styles are read from the live elements.
let svgMarkup (svg: Element) (themeRoot: HTMLElement) (layoutWidth: float) (layoutHeight: float) : string =
    let clone = svg.cloneNode true :?> Element
    let originals = withDescendants svg
    let clones = withDescendants clone

    for i in 0 .. originals.Length - 1 do
        let computed = Dom.computedStyle originals.[i]
        let copy = clones.[i]
        // The inline styles of the original may hold var() values. Only computed values stay.
        copy.removeAttribute "style"

        for name in styleProperties do
            let value = computed.getPropertyValue name

            if value <> "" then
                Dom.setElementStyleProperty copy name value

        dropInteractionAttributes copy

    let titles = clone.querySelectorAll "title"

    for i in 0 .. titles.length - 1 do
        titles.[i].parentNode.removeChild titles.[i] |> ignore

    clone.setAttribute ("xmlns", svgNamespace)
    clone.setAttribute ("width", string layoutWidth)
    clone.setAttribute ("height", string layoutHeight)
    clone.setAttribute ("viewBox", $"0 0 {layoutWidth} {layoutHeight}")
    Dom.removeElementStyleProperty clone "width"
    Dom.removeElementStyleProperty clone "height"

    let background = document.createElementNS (svgNamespace, "rect")
    background.setAttribute ("x", "0")
    background.setAttribute ("y", "0")
    background.setAttribute ("width", string layoutWidth)
    background.setAttribute ("height", string layoutHeight)
    background.setAttribute ("fill", backgroundColor themeRoot)
    clone.insertBefore (background, clone.firstChild) |> ignore

    Dom.serializeToString clone

let private toPngBlob (canvas: HTMLCanvasElement) : JS.Promise<Blob> =
    Promise.create (fun resolve reject ->
        canvas.toBlob (
            (fun blob ->
                if isNull (box blob) then
                    reject (exn "The browser could not encode the image.")
                else
                    resolve blob
            ),
            "image/png"
        )
    )

/// Resolves at the next animation frame. A click handler waits for it before the heavy work, so the browser
/// paints the busy state first.
let nextFrame () : JS.Promise<unit> =
    Promise.create (fun resolve _ -> window.requestAnimationFrame (fun _ -> resolve ()) |> ignore)

/// Draws the markup into a canvas of scale times the layout size and encodes it as PNG.
let renderPng (markup: string) (layoutWidth: float) (layoutHeight: float) : JS.Promise<Blob> = promise {
    let factor = scale layoutWidth layoutHeight

    if factor = 0.0 then
        failwith "The graph has no size to draw."

    let url =
        Dom.createObjectUrl (ClipboardBindings.createBlob markup "image/svg+xml;charset=utf-8")

    try
        let image = document.createElement "img" :?> HTMLImageElement
        image.src <- url
        do! Dom.decodeImage image

        let canvas = document.createElement "canvas" :?> HTMLCanvasElement
        canvas.width <- max 1.0 (floor (layoutWidth * factor))
        canvas.height <- max 1.0 (floor (layoutHeight * factor))

        let context = canvas.getContext_2d ()

        if isNull (box context) then
            failwith "The browser gave no canvas to draw on."

        context.drawImage (U3.Case1 image, 0.0, 0.0, canvas.width, canvas.height)
        return! toPngBlob canvas
    finally
        Dom.revokeObjectUrl url
}

/// Saves the blob under the file name through the download of the browser. The blob URL lives for a minute,
/// because the save dialog of Electron reads it after the click.
let downloadBlob (blob: Blob) (fileName: string) : unit =
    let url = Dom.createObjectUrl blob
    let anchor = document.createElement "a" :?> HTMLAnchorElement
    anchor.href <- url
    anchor.setAttribute ("download", fileName)
    document.body.appendChild anchor |> ignore
    anchor.click ()
    document.body.removeChild anchor |> ignore
    window.setTimeout ((fun () -> Dom.revokeObjectUrl url), 60000) |> ignore

/// True where the browser can write an image to the clipboard.
let canCopy () : bool =
    Dom.isSecureContext ()
    && ClipboardBindings.isWriteAvailable ()
    && ClipboardBindings.isItemAvailable ()

/// Puts the PNG on the clipboard. Call it inside the click handler, before the PNG exists: the clipboard waits
/// for the promise, so the click still counts as the user action that allows the write.
let copyPng (png: JS.Promise<Blob>) : JS.Promise<unit> =
    navigator.clipboard.write [|
        ClipboardBindings.createItemWithPendingBlob "image/png" png
    |]
