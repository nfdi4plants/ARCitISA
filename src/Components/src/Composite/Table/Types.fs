module Arcitisa.Components.Composite.Table.Types

open Fable.Core
open Feliz
open Arcitisa.Components
open Browser.Types


type TableState
    (
        isActive: CellCoordinate -> bool,
        isOrigin: CellCoordinate -> bool,
        isSelected: CellCoordinate -> bool,
        onBlur: CellCoordinate -> FocusEvent -> unit,
        onKeyDown: CellCoordinate -> KeyboardEvent -> unit,
        onClick: CellCoordinate -> MouseEvent -> unit
    ) =
    member val isActive = isActive with get, set
    member val isOrigin = isOrigin with get, set
    member val isSelected = isSelected with get, set
    member val onBlur = onBlur with get, set
    member val onKeyDown = onKeyDown with get, set
    member val onClick = onClick with get, set

    static member init() =
        TableState(
            (fun _ ->
                console.warn "TableCtx default isActive"
                false
            ),
            (fun _ ->
                console.warn "TableCtx default isOrigin"
                false
            ),
            (fun _ ->
                console.warn "TableCtx default isSelected"
                false
            ),
            (fun _ _ -> console.warn "TableCtx default onBlur"),
            (fun _ _ -> console.warn "TableCtx default onKeyDown"),
            (fun _ _ -> console.warn "TableCtx default onClick")
        )

[<JS.Pojo>]
type SelectHandle
    (
        contains: CellCoordinate -> bool,
        selectAt: (CellCoordinate * bool) -> unit,
        clear: unit -> unit,
        getSelectedCellRange,
        getSelectedCells,
        getCount
    ) =
    member val contains: CellCoordinate -> bool = contains with get, set
    member val selectAt: (CellCoordinate * bool) -> unit = selectAt with get, set
    member val clear: unit -> unit = clear with get, set
    member val getSelectedCellRange: unit -> CellCoordinateRange option = getSelectedCellRange with get, set
    member val getSelectedCells: unit -> ResizeArray<CellCoordinate> = getSelectedCells with get, set
    member val getCount: unit -> int = getCount with get, set

[<JS.Pojo>]
type TableHandle(focus: unit -> unit, scrollTo: CellCoordinate -> unit, SelectHandle: SelectHandle) =
    member val focus: unit -> unit = focus with get, set
    member val scrollTo: CellCoordinate -> unit = scrollTo with get, set
    member val SelectHandle: SelectHandle = SelectHandle with get, set
