module Arcitisa.Components.Composite.Widgets.Types

open Feliz
open ARCtrl
open Fable.Core
open Arcitisa.Components.Shared

[<RequireQualifiedAccess>]
type InsertTarget =
    | Table of index: int * selection: Arcitisa.Components.CellCoordinateRange
    | DataMap of selection: Arcitisa.Components.CellCoordinateRange

[<RequireQualifiedAccess>]
type WidgetHostView =
    | TableView
    | DataMapView
    | MetadataView
    | PreviewErrorView

type TemplateWidgetServices = {
    loadTemplates: unit -> Async<Result<Template[], string>>
}

/// <summary>
/// Is not only used to store position but also size.
/// </summary>
type Rect = {
    X: int
    Y: int
} with

    static member init() = { X = 0; Y = 0 }

type WidgetBlock = {
    prefix: string
    content: ReactElement
} with

    static member create prefix content : WidgetBlock = { prefix = prefix; content = content }
