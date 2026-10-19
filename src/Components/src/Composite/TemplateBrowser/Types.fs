module Swate.Components.Composite.Template.Types

open Fable.Core
open ARCtrl
open Swate.Components.Shared

/// This is a fable StringEnum and can be replaced by any `unbox` string
[<StringEnum>]
type FilterTokenType =
    | Tag
    | Repository
    | Name
    | Author
    | ORCID

type FilterToken = {|
    Type: FilterTokenType
    NameText: string
    Id: string
    Payload: obj option
|}


[<RequireQualifiedAccess>]
type TemplateImportAction =
    | ImportAsNewTable
    | AppendToActiveTable
    | NoImport

type TemplatePreviewCallbacks = {
    GetTemplateImportAction: System.Guid -> TemplateImportAction
    SetTemplateImportAction: System.Guid -> TemplateImportAction -> unit
    IsColumnSelected: System.Guid -> int -> bool
    ToggleColumnSelection: System.Guid -> int -> unit
    SelectAllTemplateColumns: System.Guid -> unit
    UnselectAllTemplateColumns: Template -> unit
    /// Some for templates imported as a new table.
    GetNewTableName: System.Guid -> NewTableName option
    SetNewTableName: System.Guid -> string -> unit
}

type ImportTable = {
    Index: int
    FullImport: bool
    Name: string option
}

type SelectiveImportConfig = {
    ImportType: ARCtrl.TableJoinOptions
    ImportMetadata: bool
    ImportTables: ImportTable list
    DeselectedColumns: Set<int * int>
    TemplateName: string option
} with

    static member init() = {
        ImportType = ARCtrl.TableJoinOptions.Headers
        ImportMetadata = false
        ImportTables = []
        DeselectedColumns = Set.empty
        TemplateName = None
    }

type ImportModalConfirmPayload = {
    ImportType: TableJoinOptions
    SelectedTemplatesForImport: (Template * TemplateImportAction)[]
    DeselectedTemplateColumns: Set<System.Guid * int>
    NewTableNames: Map<System.Guid, string>
}
