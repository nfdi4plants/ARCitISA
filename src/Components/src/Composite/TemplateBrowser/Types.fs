module Swate.Components.Composite.Template.Types

open Fable.Core

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
