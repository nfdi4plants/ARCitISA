module Swate.Components.Composite.Template.Types

open Fable.Core
open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.TableImport.Types

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


type ImportModalConfirmPayload = {
    ImportType: TableJoinOptions
    SelectedTemplatesForImport: (Template * TableImportAction)[]
    DeselectedTemplateColumns: Set<System.Guid * int>
    NewTableNames: Map<System.Guid, string>
}
