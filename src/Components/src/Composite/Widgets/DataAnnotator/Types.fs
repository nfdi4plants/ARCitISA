module Arcitisa.Components.Composite.Widgets.DataAnnotator.Types

open System
open ARCtrl

type AnnotationInput = {
    Selectors: string[]
    FileName: string
    FileType: string
}

[<RequireQualifiedAccess>]
type DataTarget =
    | Cell of columnIndex: int * rowIndex: int
    | Row of int
    | Column of int

    member this.ToFragmentSelectorString(hasHeader: bool) =
        let rowOffset = if hasHeader then 2 else 1

        match this with
        | DataTarget.Row rowIndex -> sprintf "row=%i" (rowIndex + rowOffset)
        | DataTarget.Column columnIndex -> sprintf "col=%i" (columnIndex + 1)
        | DataTarget.Cell(columnIndex, rowIndex) -> sprintf "cell=%i,%i" (rowIndex + rowOffset) (columnIndex + 1)

    member this.ToReactKey() =
        match this with
        | DataTarget.Row rowIndex -> sprintf "row-%i" rowIndex
        | DataTarget.Column columnIndex -> sprintf "col-%i" columnIndex
        | DataTarget.Cell(columnIndex, rowIndex) -> sprintf "cell-%i-%i" rowIndex columnIndex

type DataFile = {
    DataFileName: string
    DataFileType: string
    DataContent: string
    DataSize: float
} with

    static member create(dataFileName, dataFileType, dataContent, dataSize) = {
        DataFileName = dataFileName
        DataFileType = dataFileType
        DataContent = dataContent
        DataSize = dataSize
    }

    member this.ExpectedSeparator =
        if this.DataFileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) then
            ","
        elif this.DataFileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) then
            "\t"
        elif this.DataFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) then
            "\t"
        else
            ","

    member this.SplitBySeparator(?separator: string) =
        let sep = defaultArg separator this.ExpectedSeparator

        let sanitizedSeparator =
            match sep with
            | "\\t" -> "\t"
            | "\\n" -> "\n"
            | "\\f" -> "\f"
            | "\\r" -> "\r"
            | "\\r\\n" -> "\r\n"
            | "\\v" -> "\v"
            | _ -> sep

        let rows =
            this.DataContent.Split(
                Arcitisa.Components.ClipboardContract.Contract.LineBreaks,
                StringSplitOptions.RemoveEmptyEntries
            )

        let splitRow (value: string) (separator: string) =
            if separator.Length = 1 then
                value.Split separator.[0]
            else
                value.Split([| separator |], StringSplitOptions.None)

        rows |> Array.map (fun row -> splitRow row sanitizedSeparator)

type DataFileParseConfig = {
    Separator: string
    HasHeader: bool
} with

    static member createDefault(?dataFile: DataFile) =
        let separator =
            match dataFile with
            | Some df -> df.ExpectedSeparator
            | None -> ","

        {
            Separator = separator
            HasHeader = true
        }

type ParsedDataFile = {
    HeaderRow: string[] option
    BodyRows: string[][]
} with

    static member fromFileBySeparator (separator: string) (file: DataFile) =
        let splitRow (value: string) (separator: string) =
            if separator.Length = 1 then
                value.Split separator.[0]
            else
                value.Split([| separator |], StringSplitOptions.None)

        let sanitizedSeparator =
            match separator with
            | "\\t" -> "\t"
            | "\\n" -> "\n"
            | "\\f" -> "\f"
            | "\\r" -> "\r"
            | "\\r\\n" -> "\r\n"
            | "\\v" -> "\v"
            | _ -> separator

        let rows =
            file.DataContent.Split(
                Arcitisa.Components.ClipboardContract.Contract.LineBreaks,
                StringSplitOptions.RemoveEmptyEntries
            )

        let splitRows = rows |> Array.map (fun row -> splitRow row sanitizedSeparator)

        if splitRows.Length > 1 then
            {
                HeaderRow = Some splitRows.[0]
                BodyRows = splitRows.[1..]
            }
        else
            {
                HeaderRow = None
                BodyRows = splitRows
            }

    member this.ToggleHeader() =
        match this.HeaderRow with
        | Some header -> {
            this with
                HeaderRow = None
                BodyRows = Array.insertAt 0 header this.BodyRows
          }
        | None when this.BodyRows.Length > 1 -> {
            this with
                HeaderRow = Some this.BodyRows.[0]
                BodyRows = this.BodyRows.[1..]
          }
        | _ -> this
