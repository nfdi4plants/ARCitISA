module Swate.Components.Composite.TableImport.Types

open ARCtrl
open Swate.Components.Shared.ARCtrlHelper

[<RequireQualifiedAccess>]
type TableImportAction =
    | ImportAsNewTable
    | AppendToActiveTable
    | NoImport

/// A table offered for import. Index is the position among the offered tables and is the key everywhere.
type ImportSource = {
    Index: int
    Name: string
    Table: ArcTable
}

type TableImportPreviewCallbacks = {
    GetImportAction: int -> TableImportAction
    SetImportAction: int -> TableImportAction -> unit
    /// sourceIndex -> columnIndex -> selected
    IsColumnSelected: int -> int -> bool
    ToggleColumnSelection: int -> int -> unit
    SelectAllColumns: int -> unit
    /// sourceIndex -> columnCount
    UnselectAllColumns: int -> int -> unit
    GetNewTableName: int -> NewTableName option
    SetNewTableName: int -> string -> unit
}

type ImportTable = {
    Index: int
    FullImport: bool
    Name: string option
}

type SelectiveImportConfig = {
    ImportType: ARCtrl.TableJoinOptions
    ImportTables: ImportTable list
    DeselectedColumns: Set<int * int>
} with

    static member init() = {
        ImportType = ARCtrl.TableJoinOptions.Headers
        ImportTables = []
        DeselectedColumns = Set.empty
    }
