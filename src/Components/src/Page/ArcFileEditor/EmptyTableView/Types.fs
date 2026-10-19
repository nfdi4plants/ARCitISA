module Swate.Components.Page.ArcFileEditor.EmptyTableView.Types

open ARCtrl
open Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter

/// A table from anywhere in the ARC whose input and output columns can start a new table.
type ArcNodeTable = {
    /// Study, assay or run that owns the table.
    Location: ArcTableLocation
    /// The table. Only its input and output columns are used.
    Table: ArcTable
}

[<RequireQualifiedAccess>]
type ArcNodeKind =
    | Input
    | Output

/// One input or output column that can become the input column of the active table.
type ArcNodeOption = {
    /// Unique among all options shown at once.
    Key: string
    /// Owner of the source table, e.g. "Assay MyAssay".
    Group: string
    TableName: string
    Kind: ArcNodeKind
    /// The source column as it is in its table.
    Column: CompositeColumn
}
