namespace Arcitisa.Components

open Fable.Core
open Browser.Types
open Feliz

// TanStack's measureElement is a React callback ref, not an IRefValue object.
type VirtualMeasureElementRef = Element option -> unit

module Virtual =

    [<Literal>]
    let ImportPath = "@tanstack/react-virtual"

    [<ImportMember(ImportPath)>]
    type Range = interface end

    [<ImportMember(ImportPath)>]
    type VirtualItem =
        member this.key: string = jsNative
        member this.index: int = jsNative
        member this.start: int = jsNative
        member this.``end``: int = jsNative
        member this.size: int = jsNative

    [<StringEnum(CaseRules.LowerFirst); Global>]
    type AlignOption =
        | Auto
        | Start
        | Center
        | End

    [<StringEnum(CaseRules.LowerFirst)>]
    type ScrollBehavior =
        | Auto
        | Smooth

    [<ImportMember(ImportPath)>]
    type Virtualizer<'A, 'B> =
        member this.getVirtualItems() : VirtualItem[] = jsNative
        member this.getVirtualIndexes() : int[] = jsNative
        /// The items as the virtualizer measured them last. They change only when the virtualizer
        /// measures again, which getTotalSize, getVirtualItems and getVirtualItemForOffset do.
        member this.measurementsCache: VirtualItem[] = jsNative
        member this.getTotalSize() : int = jsNative

        [<ParamObject(1)>]
        member this.scrollToIndex(index: int, ?align: AlignOption, ?behavior: ScrollBehavior) : unit = jsNative

        [<ParamObject>]
        member this.scrollToEnd(?behavior: ScrollBehavior option) : unit = jsNative

        [<ParamObject(1)>]
        member this.scrollBy(delta: int, ?behavior: ScrollBehavior option) : unit = jsNative

        [<ParamObject(1)>]
        member this.scrollToOffset(offset: int, ?align: AlignOption, ?behavior: ScrollBehavior) : unit = jsNative

        /// The item at the offset, measured with the current sizes. None while there is no item.
        member this.getVirtualItemForOffset(offset: float) : VirtualItem option = jsNative

        member this.scrollRect: {| height: int; width: int |} = jsNative

        /// The scroll offset the virtualizer lays rows out for. It follows the scroll events of
        /// the scroll element.
        member this.scrollOffset
            with get (): float = jsNative
            and set (_: float) = jsNative

        member this.measureElement: VirtualMeasureElementRef = jsNative

        /// Decides whether a size change of a measured item moves the scroll position. Without a
        /// predicate, the virtualizer moves the scroll position when the item lies above the offset.
        member this.shouldAdjustScrollPositionOnItemSizeChange
            with get (): System.Func<VirtualItem, float, obj, bool> option = jsNative
            and set (_: System.Func<VirtualItem, float, obj, bool> option) = jsNative

/// The part of react-dom the virtualized lists need. TanStack renders its own updates through the
/// same function.
[<Erase>]
type ReactDomApi =

    /// Renders the updates the callback makes before it returns, so the DOM is current when the
    /// browser paints.
    [<ImportMember("react-dom")>]
    static member flushSync(callback: unit -> unit) : unit = jsNative

[<Erase>]
type Virtual =

    [<ImportMember(Virtual.ImportPath)>]
    static member defaultRangeExtractor(range: Virtual.Range) : int[] = jsNative

    [<ImportMember(Virtual.ImportPath)>]
    [<NamedParamsAttribute>]
    static member useVirtualizer
        (
            // required
            count: int,
            getScrollElement: unit -> option<Browser.Types.HTMLElement>,
            estimateSize: int -> int,
            // optional
            ?getItemKey: int -> string,
            ?scrollMargin: float,
            ?scrollPaddingStart: float,
            ?scrollPaddingEnd: float,
            ?overscan: int,
            ?rangeExtractor: Virtual.Range -> int[],
            ?debug: bool,
            ?onChange: (Virtual.Virtualizer<_, _> * bool) -> unit,
            ?horizontal: bool,
            ?paddingStart: int,
            ?paddingEnd: int,
            ?gap: int,
            ?lanes: int,
            ?scrollEndThreshold: int,
            // Reports the scroll offset of the scroll element to the callback and returns the
            // function that stops the reporting.
            ?observeElementOffset: System.Func<obj, System.Action<float, bool>, System.Action>
        ) : Virtual.Virtualizer<obj, obj> =
        jsNative
