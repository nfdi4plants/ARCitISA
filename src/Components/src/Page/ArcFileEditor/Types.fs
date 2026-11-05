module Swate.Components.Page.ArcFileEditor.Types

open ARCtrl
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.Widgets.Types

type AddRowsTarget =
    | Table of ArcTable
    | DataMap of Datamap

[<RequireQualifiedAccess>]
type ActiveView =
    | Table of index: int
    | DataMap
    | Metadata

    member this.ViewIndex =
        match this with
        | Table i -> i
        | DataMap -> -1
        | Metadata -> -2

    member this.TryTableIndex =
        match this with
        | Table i -> Some i
        | _ -> None

    member this.ToWidgetHostView() =
        match this with
        | Table _ -> WidgetHostView.TableView
        | DataMap -> WidgetHostView.DataMapView
        | Metadata -> WidgetHostView.MetadataView

    member this.IsAvailableOn(arcFile: ArcFiles) =
        match this with
        | Table i -> i >= 0 && i < arcFile.Tables().Count
        | DataMap -> arcFile.CanRenderDataMapView()
        | Metadata -> arcFile.HasMetadata()

    /// Normalizes the active view based on the content of the arc file.
    /// If the current active view is not available for the given arc file, it will return a valid active view based on the content of the arc file.
    static member Forward(arcFile: ArcFiles, current: ActiveView) =
        if current.IsAvailableOn(arcFile) then current
        elif arcFile.Tables().Count > 0 then ActiveView.Table 0
        elif arcFile.CanRenderDataMapView() then ActiveView.DataMap
        else ActiveView.Metadata

    member this.TryGetActiveTable(arcFile: ArcFiles) =

        match this with
        | Table i when i >= 0 && i < arcFile.Tables().Count ->
            let table = arcFile.Tables().[i]
            Some table
        | _ -> None

type ArcFileEditorHeaderProps = {
    arcFile: ArcFiles
    activeView: ActiveView
    setActiveView: ActiveView -> unit
}
