module Main.Provenance.ProvenanceGroupingReader

open ARCtrl
open Arcitisa.Components.Shared.ProvenanceGrouping.ARCtrlConverter
open Arcitisa.Electron.Shared.DTOs.ProvenanceGroupingDto

let private selectionsFor scope parentIdentifier (tables: ResizeArray<ArcTable>) =
    tables
    |> Seq.map (fun table -> ProvenanceTableSelectionDto.create scope parentIdentifier table.Name)
    |> Seq.toArray

let listTables (arc: ARC) : ProvenanceTableSelectionDto[] = [|
    for study in arc.Studies do
        yield! selectionsFor ProvenanceTableScopeDto.Study study.Identifier study.Tables

    for assay in arc.Assays do
        yield! selectionsFor ProvenanceTableScopeDto.Assay assay.Identifier assay.Tables

    for run in arc.Runs do
        yield! selectionsFor ProvenanceTableScopeDto.Run run.Identifier run.Tables
|]

let loadTable (selection: ProvenanceTableSelectionDto) (arc: ARC) : ProvenanceLoadResultDto =
    let result =
        fromLoadedArc
            {
                LoadedTable = ProvenanceTableSelectionDto.toArcLocation selection
                IncludePreviousContext = true
            }
            arc

    {
        Selection = selection
        Model = result.Model |> ProvenanceModelDto.ofModel
        Warnings = result.Warnings |> List.toArray
    }
