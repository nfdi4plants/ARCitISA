namespace Arcitisa.Components.JsBindings

open Browser.Types
open Fable.Core
open Feliz

open Fable.Core.JsInterop

type IObject =

    [<Emit("$0[$1]")>]
    member this.get(fieldName: string) : obj = jsNative


module Object =

    [<Emit("Object.keys($0)")>]
    let keys (o: IObject) = jsNative

    /// Shallowly merges objects without mutating either input; right-hand fields win.
    [<Emit("Object.assign({}, $0, $1)")>]
    let merge (left: obj) (right: obj) : obj = jsNative

module DndKit =

    type ScreenReaderInstructions = {| draggable: string |}

    type Accessibility = {|
        screenReaderInstructions: ScreenReaderInstructions
    |}

    type ISensor = obj

    type ITransform =
        abstract member toString: obj -> string

    type ICSS =
        abstract member Transform: ITransform

    type ISortable =
        abstract member attributes: IObject
        abstract member listeners: IObject
        abstract member setNodeRef: obj -> unit
        abstract member transform: obj
        abstract member transition: obj
        abstract member isDragging: bool

    type IDroppable =
        abstract member setNodeRef: obj -> unit
        abstract member isOver: bool

    type IDraggable =
        abstract member attributes: IObject
        abstract member listeners: IObject
        abstract member setNodeRef: obj -> unit
        abstract member transform: obj
        abstract member isDragging: bool

    type ICoordinates =
        abstract member x: float
        abstract member y: float

    [<AllowNullLiteral>]
    type IDndKitActiveData =
        abstract member current: obj

    [<AllowNullLiteral>]
    type IDndKitActive =
        abstract member id: obj
        abstract member data: IDndKitActiveData

    type IDndKitEvent =
        abstract member active: IDndKitActive
        /// DnD Kit returns null when the drag ends outside every droppable.
        /// Consumers must null-check before reading over.id or over.data.
        abstract member over: IDndKitActive
        abstract member delta: ICoordinates

    type IDndKitMoveEvent =
        inherit IDndKitEvent

    [<Import("closestCenter", "@dnd-kit/core")>]
    let closestCenter: obj = jsNative

    [<Import("pointerWithin", "@dnd-kit/core")>]
    let pointerWithin: obj = jsNative

    [<Import("KeyboardSensor", "@dnd-kit/core")>]
    let KeyboardSensor: obj = jsNative

    [<Import("PointerSensor", "@dnd-kit/core")>]
    let PointerSensor: obj = jsNative

    [<Import("arrayMove", "@dnd-kit/sortable")>]
    let arrayMove (items: ResizeArray<'a>, oldIndex: int, newIndex: int) : ResizeArray<'a> = jsNative

    [<Import("sortableKeyboardCoordinates", "@dnd-kit/sortable")>]
    let sortableKeyboardCoordinates: obj = jsNative

    [<Import("verticalListSortingStrategy", "@dnd-kit/sortable")>]
    let verticalListSortingStrategy: obj = jsNative

    [<Import("horizontalListSortingStrategy", "@dnd-kit/sortable")>]
    let horizontalListSortingStrategy: obj = jsNative

open DndKit

[<Erase>]
type DndKit =

    [<Import("CSS", "@dnd-kit/utilities")>]
    static member CSS: ICSS = jsNative

    [<Import("useSortable", "@dnd-kit/sortable")>]
    static member useSortable(props: obj) : ISortable = jsNative

    [<Import("useDroppable", "@dnd-kit/core")>]
    static member useDroppable(props: obj) : IDroppable = jsNative

    [<Import("useDraggable", "@dnd-kit/core")>]
    static member useDraggable(props: obj) : IDraggable = jsNative

    [<Import("useDndMonitor", "@dnd-kit/core")>]
    static member useDndMonitor(props: obj) : unit = jsNative

    [<Import("useSensor", "@dnd-kit/core")>]
    static member useSensor(sensor, ?props) : ISensor = jsNative

    [<Import("useSensors", "@dnd-kit/core")>]
    static member useSensors([<ParamSeqAttribute>] sensors: ISensor[]) : obj = jsNative

    [<ReactComponent("DndContext", "@dnd-kit/core")>]
    static member DndContext
        (
            ?onDragStart,
            ?onDragMove,
            ?onDragCancel,
            ?onDragEnd,
            ?sensors,
            ?collisionDetection,
            ?children: ReactElement,
            ?key,
            ?accessibility: Accessibility
        ) =
        React.Imported()

    [<ReactComponent("DragOverlay", "@dnd-kit/core")>]
    static member DragOverlay(?children: ReactElement, ?dropAnimation: obj, ?key) = React.Imported()

    [<ReactComponent("SortableContext", "@dnd-kit/sortable")>]
    static member SortableContext(items: ResizeArray<string>, strategy, children: ReactElement) = React.Imported()

    [<ReactComponent("SortableContext", "@dnd-kit/sortable")>]
    static member SortableContext(items: ResizeArray<System.Guid>, strategy, children: ReactElement) = React.Imported()
