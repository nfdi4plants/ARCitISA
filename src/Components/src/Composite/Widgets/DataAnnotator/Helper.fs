module Arcitisa.Components.Composite.Widgets.DataAnnotator.Helper

open System
open ARCtrl
open Arcitisa.Components
open Arcitisa.Components.Shared
open Arcitisa.Components.Composite.Widgets.DataAnnotator.Types
open Arcitisa.Components.Composite.Widgets.Types
open Arcitisa.Components.Composite.DataMapTable.ClipboardTarget

let private compareTargets (left: DataTarget) (right: DataTarget) =
    let key =
        function
        | DataTarget.Cell(columnIndex, rowIndex) -> 0, rowIndex, columnIndex
        | DataTarget.Column columnIndex -> 1, 0, columnIndex
        | DataTarget.Row rowIndex -> 2, rowIndex, 0

    compare (key left) (key right)

let selectorsFromTargets (hasHeader: bool) (targets: Set<DataTarget>) =
    targets
    |> Seq.sortWith compareTargets
    |> Seq.map (fun target -> target.ToFragmentSelectorString(hasHeader))
    |> Array.ofSeq

let tryParseDataFile (separator: string) (file: DataFile) =
    try
        let parsed = ParsedDataFile.fromFileBySeparator separator file

        if parsed.BodyRows.Length = 0 then
            Error "Parsed file does not contain any data rows."
        else
            Ok parsed
    with exceptionValue ->
        Error exceptionValue.Message

let private setDataContextFields (fileName: string) (fileType: string) (selector: string) (data: Data) =
    data.FilePath <- Some fileName
    data.Selector <- Some selector
    data.Format <- Some fileType
    data.SelectorFormat <- Some URLs.Data.SelectorFormat.csv
    data

let appendAnnotation (current: AnnotationInput option) (next: AnnotationInput) =
    match current with
    | Some current when current.FileName = next.FileName && current.FileType = next.FileType -> {
        next with
            Selectors = Array.append current.Selectors next.Selectors |> Array.distinct
      }
    | _ -> {
        next with
            Selectors = Array.distinct next.Selectors
      }

let insertAnnotationIntoSelectedCells
    (arcFile: ArcFiles)
    (setArcFile: ArcFiles -> unit)
    (input: AnnotationInput)
    (target: InsertTarget option)
    =
    try
        if input.Selectors.Length = 0 then
            Error "Select at least one selector to insert."
        else
            let nextArcFile = ArcFiles.refreshRef arcFile
            let fileName = toArcRootRelativeFilePath arcFile input.FileName

            let cells =
                input.Selectors
                |> Array.map (fun selector ->
                    Data()
                    |> setDataContextFields fileName input.FileType selector
                    |> CompositeCell.createData
                )

            match target with
            | Some(InsertTarget.Table(tableIndex, selection)) ->
                match nextArcFile.TryGetActiveTable(Some tableIndex) with
                | Some(_, table) when
                    selection.xStart >= 0
                    && selection.xStart < table.ColumnCount
                    && selection.yStart >= 0
                    && selection.yStart < table.RowCount
                    ->
                    let cellsToInsert =
                        cells
                        |> Array.truncate (table.RowCount - selection.yStart)
                        |> Array.mapi (fun offset source ->
                            let coordinate: CellCoordinate = {|
                                x = selection.xStart
                                y = selection.yStart + offset
                            |}

                            let current = table.GetCellAt(coordinate.x, coordinate.y)

                            let next =
                                match current with
                                | CompositeCell.Data _ -> source
                                | _ -> current.UpdateMainField(source.ToVisibleString())

                            coordinate, next
                        )

                    table.SetCellsAt cellsToInsert
                    setArcFile nextArcFile
                    Ok cellsToInsert.Length
                | _ -> Error "Select a table cell to insert selectors."
            | Some(InsertTarget.DataMap selection) ->
                match nextArcFile.TryGetDataMap() with
                | Some dataMap when
                    selection.xStart > 0
                    && selection.xStart <= dataMap.ColumnCount
                    && selection.yStart > 0
                    ->
                    let anchor: CellCoordinate = {|
                        x = selection.xStart
                        y = selection.yStart
                    |}

                    dataMap.PasteStructuredCells(anchor, [| anchor |], cells |> Array.map Array.singleton)
                    setArcFile nextArcFile
                    Ok cells.Length
                | _ -> Error "Select a DataMap cell to insert selectors."
            | None -> Error "Select table or DataMap cells before inserting selectors."
    with ex ->
        Error ex.Message

let DefaultSeparatorOptions: (string * string)[] = [|
    "\\t", "Tab (\\t)"
    ",", "Comma (,)"
    ";", "Semicolon (;)"
    "|", "Pipe (|)"
|]

let fileTypeFromName (fileName: string) =
    if fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) then
        "text/csv"
    elif fileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) then
        "text/tab-separated-values"
    elif fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) then
        "text/plain"
    else
        "text/plain"

let separatorToInput (separator: string) =
    match separator with
    | "\t" -> "\\t"
    | "\n" -> "\\n"
    | "\r" -> "\\r"
    | "\r\n" -> "\\r\\n"
    | "\f" -> "\\f"
    | "\v" -> "\\v"
    | _ -> separator

let parseDataFileBySeparator (separator: string) (dataFile: DataFile) =
    match tryParseDataFile separator dataFile with
    | Ok parsed -> Ok parsed
    | Error errorMessage ->
        let fallbackSeparator = dataFile.ExpectedSeparator

        if separator <> fallbackSeparator then
            tryParseDataFile fallbackSeparator dataFile
        else
            Error errorMessage
