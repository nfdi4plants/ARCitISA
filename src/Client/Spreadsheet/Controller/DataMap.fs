module Spreadsheet.Controller.DataMap


open ARCtrl
open Swate.Components
open Swate.Components.Shared


let updateDatamap (dataMapOpt: Datamap option) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let nextArcFile =
        match state.ArcFile with
        | Some(ArcFiles.Assay a) ->
            a.Datamap <- dataMapOpt
            Some(ArcFiles.Assay a)
        | Some(ArcFiles.Study(s, _)) ->
            s.Datamap <- dataMapOpt
            Some(ArcFiles.Study(s, []))
        | Some(ArcFiles.DataMap(p, d)) ->
            if dataMapOpt.IsSome then
                Some(ArcFiles.DataMap(p, dataMapOpt.Value))
            else
                Some(ArcFiles.DataMap(p, d))
        | _ ->
            console.warn "[WARNING] updateDatamap: No Assay, Study or Datamap found in ArcFile"
            state.ArcFile

    match dataMapOpt with
    | None when state.ActiveView = Spreadsheet.ActiveView.DataMap -> {
        state with
            ArcFile = nextArcFile
            ActiveView = Spreadsheet.ActiveView.Metadata
      }
    | _ -> { state with ArcFile = nextArcFile }

let updateDataMapDataContextAt (dtx) (index) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let ensureIndexExists (dtm: Datamap) =
        if index >= dtm.DataContexts.Count then
            failwithf
                "DataMap does not contain the an item at index: %i. Only %i items exist."
                index
                dtm.DataContexts.Count

    let nextArcFile =
        match state.ArcFile with
        | Some(ArcFiles.Assay a) when a.Datamap.IsSome ->
            ensureIndexExists a.Datamap.Value
            a.Datamap.Value.DataContexts.[index] <- dtx
            Some(ArcFiles.Assay a)
        | Some(ArcFiles.Study(s, _)) when s.Datamap.IsSome ->
            ensureIndexExists s.Datamap.Value
            s.Datamap.Value.DataContexts.[index] <- dtx
            Some(ArcFiles.Study(s, []))
        | _ ->
            console.warn "[WARNING] updateDatamap: No Assay or Study found in ArcFile"
            state.ArcFile

    { state with ArcFile = nextArcFile }

let addRows (n: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    let rows = Array.init n (fun _ -> DataContext())

    state.DataMapOrDefault.DataContexts.AddRange(rows)

    { state with ArcFile = state.ArcFile }

let deleteRow (n: int) (state: Spreadsheet.Model) : Spreadsheet.Model =
    state.DataMapOrDefault.DataContexts.RemoveAt n

    { state with ArcFile = state.ArcFile }

let deleteRows (rows: int[]) (state: Spreadsheet.Model) : Spreadsheet.Model =
    rows
    |> Array.sortDescending
    |> Array.iter (fun n -> state.DataMapOrDefault.DataContexts.RemoveAt n)

    { state with ArcFile = state.ArcFile }

let addDataAnnotation
    (data:
        {|
            fragmentSelectors: string[]
            fileName: string
            fileType: string
            targetColumn: DataAnnotator.TargetColumn
        |})
    (state: Spreadsheet.Model)
    : Spreadsheet.Model =
    let inputLength = data.fragmentSelectors.Length
    // Append rows if needed
    if inputLength > state.DataMapOrDefault.DataContexts.Count then
        let rows =
            Array.init (inputLength - state.DataMapOrDefault.DataContexts.Count) (fun _ -> DataContext())

        state.DataMapOrDefault.DataContexts.AddRange(rows)

    for i in 0 .. inputLength - 1 do
        let selector = data.fragmentSelectors.[i]
        let dtx = state.DataMapOrDefault.DataContexts.[i]
        dtx.FilePath <- Some data.fileName
        dtx.Selector <- Some selector
        dtx.Format <- Some data.fileType
        dtx.SelectorFormat <- Some Swate.Components.Shared.URLs.Data.SelectorFormat.csv

    { state with ArcFile = state.ArcFile }
