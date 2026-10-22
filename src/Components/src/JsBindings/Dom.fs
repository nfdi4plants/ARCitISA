namespace Swate.Components.JsBindings

open Browser.Types
open Fable.Core

/// Small typed bindings for browser APIs that Fable.Browser.Dom does not cover.
/// requestAnimationFrame and cancelAnimationFrame are not repeated here, Browser.Dom.window has them.
module Dom =

    [<AllowNullLiteral>]
    type ResizeObserverContentRect =
        abstract width: float
        abstract height: float

    [<AllowNullLiteral>]
    type ResizeObserverEntry =
        abstract target: Element
        abstract contentRect: ResizeObserverContentRect

    [<Global>]
    type ResizeObserver(callback: ResizeObserverEntry[] -> unit) =
        /// False where the browser has no ResizeObserver, as in jsdom.
        [<Emit("typeof ResizeObserver !== 'undefined'")>]
        static member isAvailable: bool = jsNative

        member _.observe(target: Element) : unit = jsNative
        member _.disconnect() : unit = jsNative

    [<AllowNullLiteral>]
    type private TextMetrics =
        abstract width: float

    [<AllowNullLiteral>]
    type private MeasureContext =
        abstract font: string with get, set
        abstract measureText: string -> TextMetrics

    // Null without OffscreenCanvas (jsdom, node) and where the browser gives no 2d context.
    [<Emit("typeof OffscreenCanvas === 'undefined' ? null : new OffscreenCanvas(1, 1).getContext('2d')")>]
    let private tryCreateMeasureContext () : MeasureContext = jsNative

    /// Width of a text without a canvas. Every character counts 7 px, which keeps layouts deterministic.
    let estimateTextWidth (text: string) : float = float text.Length * 7.0

    /// The monospace font stack of createTextMeasure. Elements that draw mono text use it too, so the
    /// drawn text has the width that was measured.
    let monoFamily =
        "ui-monospace, SFMono-Regular, \"SF Mono\", Menlo, Consolas, monospace"

    /// Creates a text measure: text, font weight, font size in px and monospace, to the width in px.
    /// The font is sansFamily, or a monospace stack for mono text. OffscreenCanvas needs no DOM element,
    /// and jsdom has none, so tests get estimateTextWidth without jsdom reporting an unimplemented canvas.
    let createTextMeasure (sansFamily: string) : string -> float -> float -> bool -> float =
        let context = tryCreateMeasureContext ()

        if isNull context then
            fun text _ _ _ -> estimateTextWidth text
        else
            fun text weight size mono ->
                let family = if mono then monoFamily else sansFamily
                context.font <- $"{weight} {size}px {family}"
                (context.measureText text).width

    [<Emit("getComputedStyle($0).fontFamily")>]
    let computedFontFamily (el: Element) : string = jsNative

    /// The computed CSS color-scheme of an element, such as "light" or "dark".
    [<Emit("getComputedStyle($0).colorScheme")>]
    let computedColorScheme (el: Element) : string = jsNative

    [<Emit("$0.isConnected")>]
    let isConnected (el: Element) : bool = jsNative

    /// Sets one inline style property by its CSS name, such as "max-width".
    [<Emit("$0.style.setProperty($1, $2)")>]
    let setStyleProperty (el: HTMLElement) (name: string) (value: string) : unit = jsNative

    [<Emit("$0.focus({ preventScroll: true })")>]
    let focusWithoutScroll (el: HTMLElement) : unit = jsNative

    /// The computed style of an element, such as getComputedStyle returns it.
    [<AllowNullLiteral>]
    type ComputedStyle =
        abstract getPropertyValue: name: string -> string

    [<Emit("getComputedStyle($0)")>]
    let computedStyle (el: Element) : ComputedStyle = jsNative

    /// Sets one inline style property of any element, SVG elements included.
    [<Emit("$0.style.setProperty($1, $2)")>]
    let setElementStyleProperty (el: Element) (name: string) (value: string) : unit = jsNative

    [<Emit("$0.style.removeProperty($1)")>]
    let removeElementStyleProperty (el: Element) (name: string) : unit = jsNative

    [<Emit("$0.getAttributeNames()")>]
    let attributeNames (el: Element) : string[] = jsNative

    /// The XML text of a node, such as an SVG element.
    [<Emit("new XMLSerializer().serializeToString($0)")>]
    let serializeToString (node: Node) : string = jsNative

    [<Emit("new Blob([$0], { type: $1 })")>]
    let textBlob (text: string) (mimeType: string) : Blob = jsNative

    [<Emit("URL.createObjectURL($0)")>]
    let createObjectUrl (blob: Blob) : string = jsNative

    [<Emit("URL.revokeObjectURL($0)")>]
    let revokeObjectUrl (url: string) : unit = jsNative

    [<Emit("new Image()")>]
    let createImage () : HTMLImageElement = jsNative

    /// Resolves when the image is loaded and can be drawn. Rejects when the source cannot be decoded.
    [<Emit("$0.decode()")>]
    let decodeImage (image: HTMLImageElement) : JS.Promise<unit> = jsNative

    /// The 2d context of a canvas, null where the browser gives none.
    [<Emit("$0.getContext('2d')")>]
    let context2d (canvas: HTMLCanvasElement) : CanvasRenderingContext2D = jsNative

    /// Draws the whole image into the rectangle from the origin with the given size.
    [<Emit("$0.drawImage($1, 0, 0, $2, $3)")>]
    let drawImage (context: CanvasRenderingContext2D) (image: HTMLImageElement) (width: float) (height: float) : unit =
        jsNative

    [<Emit("window.isSecureContext === true")>]
    let isSecureContext () : bool = jsNative

    /// The name of a thrown or rejected error, such as "NotAllowedError". Empty for values without a name.
    [<Emit("($0 != null && typeof $0.name === 'string' ? $0.name : '')")>]
    let errorName (value: obj) : string = jsNative
