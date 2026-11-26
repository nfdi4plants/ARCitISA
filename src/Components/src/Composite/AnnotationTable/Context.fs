module Arcitisa.Components.Composite.AnnotationTable.Context

open Feliz
open Arcitisa.Components
open Arcitisa.Components
open Arcitisa.Components.Primitive


type AnnotationTableContext = {
    SelectedCells: CellCoordinateRange option
} with

    static member init(?selectedCells) = { SelectedCells = selectedCells }

/// Selections of the mounted grids, keyed by table name (or `DataMapTable.SelectionContextKey`).
/// Writers set and clear their own key, so a grid that is not on screen leaves no selection behind.
type AnnotationTableSelections = {
    state: Map<string, AnnotationTableContext>
    setSelection: string -> CellCoordinateRange option -> unit
    clearSelection: string -> unit
}

let AnnotationTableStateCtx =
    React.createContext<AnnotationTableSelections> (
        {
            state = Map.empty
            setSelection = fun _ _ -> ()
            clearSelection = ignore
        }
    )

[<Hook>]
let useAnnotationTableStateCtx () =
    React.useContext AnnotationTableStateCtx
