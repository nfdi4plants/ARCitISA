module Swate.Components.Composite.TableImport.Types

open ARCtrl
open Swate.Components.Shared.ARCtrlHelper

[<RequireQualifiedAccess>]
type TableImportAction =
    | ImportAsNewTable
    | AppendToActiveTable
    | NoImport

/// One import mode choice as the dialogs show it. Key is used for test ids.
type ImportModeOption = {
    Mode: TableJoinOptions
    Key: string
    Label: string
    Description: string
}

/// A table offered for import. Index is the position among the offered tables and is the key everywhere.
/// Name is the label shown to the user. A new table is named after Table.Name.
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
    /// sourceIndex -> rowIndex -> selected
    IsRowSelected: int -> int -> bool
    ToggleRowSelection: int -> int -> unit
    SelectAllRows: int -> unit
    /// sourceIndex -> rowCount
    UnselectAllRows: int -> int -> unit
    /// sourceIndex -> number of deselected rows of that source
    DeselectedRowCount: int -> int
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

type TableImportDecisions = {
    ImportType: TableJoinOptions
    /// Used for a source index missing from Actions.
    DefaultAction: TableImportAction
    /// By source index.
    Actions: Map<int, TableImportAction>
    /// (source index, column index)
    DeselectedColumns: Set<int * int>
    /// (source index, row index)
    DeselectedRows: Set<int * int>
    /// By source index, the raw text the user typed.
    EnteredNames: Map<int, string>
    /// Index of the table in the current file to append to.
    ActiveTableIndex: int option
}

type TableImportResult = {
    ArcFile: ArcFiles
    CreatedTableNames: string list
    ModifiedTableNames: string list
}
