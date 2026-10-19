module Swate.Electron.Shared.DTOs.ArcNodesDto

open ARCtrl
open ARCtrl.Json
open Swate.Components.Page.ArcFileEditor.EmptyTableView.Types
open Swate.Electron.Shared.DTOs.ProvenanceGroupingDto

/// One ARC table on its way over IPC. IPC copies plain data only, so the table travels as ARCtrl JSON
/// and is turned back into a working `ArcTable` in the renderer.
type ArcNodeTableDto = {
    Selection: ProvenanceTableSelectionDto
    TableJson: string
}

[<RequireQualifiedAccess>]
module ArcNodeTableDto =

    let create scope parentIdentifier (table: ArcTable) = {
        Selection = ProvenanceTableSelectionDto.create scope parentIdentifier table.Name
        TableJson = table.ToCompressedJsonString()
    }

    let toArcNodeTable (dto: ArcNodeTableDto) : ArcNodeTable = {
        Location = ProvenanceTableSelectionDto.toArcLocation dto.Selection
        Table = ArcTable.fromCompressedJsonString dto.TableJson
    }
