module OfficeInterop.ARCtrlHelper

open Fable.Core
open ExcelJS.Fable
open Excel

open Arcitisa.Components.Shared

open ARCtrl
open ARCtrl.Spreadsheet

/// <summary>
/// Check whether the selected column is a reference column or not
/// </summary>
/// <param name="name"></param>
let isReferenceColumn (name: string) =
    ARCtrl.Spreadsheet.ArcTable.helperColumnStrings
    |> Seq.exists (fun cName -> name.StartsWith cName)

/// <summary>
/// Group the columns to building blocks
/// </summary>
/// <param name="headers"></param>
let groupToBuildingBlocks (headers: string[]) =
    let ra: ResizeArray<ResizeArray<int * string>> = ResizeArray()

    headers
    |> Array.iteri (fun i header ->
        if isReferenceColumn header then
            ra.[ra.Count - 1].Add(i, header)
        else
            ra.Add(ResizeArray([ (i, header) ]))
    )

    ra

let isTopLevelMetadataSheet (worksheetName: string) =
    match worksheetName with
    | name when
        ArcAssay.isMetadataSheetName name
        || ArcInvestigation.isMetadataSheetName name
        || ArcStudy.isMetadataSheetName name
        || Template.isMetadataSheetName name
        ->
        true
    | _ -> false

/// <summary>
/// Get the associated CompositeColumn for the given column index. Returns raw column names and indices associated to all compartments of the CompositeColumn.
/// </summary>
/// <param name="table"></param>
/// <param name="columnIndex"></param>
/// <param name="context"></param>
let getCompositeColumnInfoByIndex (table: Table) (columnIndex: float) (context: RequestContext) = promise {
    let headerRange = table.getHeaderRowRange ()

    let _ =
        headerRange.load (U2.Case2(ResizeArray [| "columnIndex"; "values"; "columnCount" |]))
        |> ignore

    return!
        context
            .sync()
            .``then`` (fun _ ->
                let rebasedIndex = columnIndex - headerRange.columnIndex |> int

                if rebasedIndex < 0 || rebasedIndex >= (int headerRange.columnCount) then
                    failwith "Cannot select building block outside of annotation table!"

                let headers: string[] = [|
                    for v in headerRange.values.[0] do
                        v.Value :?> string
                |]

                let selectedHeader = rebasedIndex, headers.[rebasedIndex]
                let buildingBlockGroups = groupToBuildingBlocks headers

                let selectedBuildingBlock =
                    buildingBlockGroups.Find(fun bb -> bb.Contains selectedHeader)

                selectedBuildingBlock
            )
}

/// <summary>
/// Checks whether the string seqs are part of a valid arc table or not and returns potential errors and index of them in the annotation table
/// </summary>
/// <param name="headers"></param>
/// <param name="rows"></param>
let validate (headers: #seq<string>) (rows: #seq<#seq<string>>) =

    let columns = Seq.append [ headers ] rows |> Seq.transpose

    let columnsArray = columns |> Seq.toArray |> Array.map (Seq.toArray)

    let groupedColumns = columnsArray |> ArcTable.groupColumnsByHeader

    let indexedError =
        groupedColumns
        |> Array.mapi (fun i c ->
            try
                let _ = CompositeColumn.fromStringCellColumns c
                None
            with ex ->
                //The target index must be adapted depending on the position of the error column
                //because the columns were group based on potential main columns
                let hasMainColumn =
                    groupedColumns.[i]
                    |> Array.map (fun gc ->
                        CompositeHeader.Cases
                        |> Array.exists (fun (_, header) -> gc.[0].StartsWith(header))
                    )
                    |> Array.contains true

                if hasMainColumn then Some(ex, i) else Some(ex, i - 1)
        )
        |> Array.filter (fun i -> i.IsSome)

    let indexedError =
        if indexedError.Length > 0 then
            indexedError |> Array.map (fun i -> i.Value)
        else
            [||]

    let newHeaders = headers |> Array.ofSeq

    let errorIndices =
        indexedError
        |> Array.map (fun (ex, bi) ->
            ex,
            groupedColumns.[0..bi]
            |> Array.map (fun bb -> bb.Length)
            |> Array.sum
            |> (fun i -> newHeaders.[i])
        )

    errorIndices

/// <summary>
/// Validate whether the selected excel table is a valid ARC / ISA table
/// </summary>
/// <param name="excelTable"></param>
/// <param name="context"></param>
let validateExcelTable (excelTable: Table) (context: RequestContext) = promise {
    //Get headers and body
    let headerRange = excelTable.getHeaderRowRange ()
    let bodyRowRange = excelTable.getDataBodyRange ()

    let _ =
        headerRange.load (U2.Case2(ResizeArray [| "values" |])) |> ignore
        bodyRowRange.load (U2.Case2(ResizeArray [| "values" |])) |> ignore

    do! context.sync ()

    let inMemoryTable =

        let headers =
            headerRange.values.[0]
            |> Seq.map (fun item -> item |> Option.map string |> Option.defaultValue "" |> (fun s -> s.TrimEnd()))

        let bodyRows =
            bodyRowRange.values
            |> Seq.map (fun items ->
                items
                |> Seq.map (fun item -> item |> Option.map string |> Option.defaultValue "")
            )

        validate headers bodyRows

    return inMemoryTable
}

/// <summary>
/// Get output column of arc excel table
/// </summary>
/// <param name="context"></param>
let tryGetPrevTableOutput (prevArcTable: ArcTable option) = promise {

    if prevArcTable.IsSome then

        let outputColumns = prevArcTable.Value.TryGetOutputColumn()

        if (outputColumns.IsSome) then

            let outputValues =
                CompositeColumn.toStringCellColumns outputColumns.Value
                |> (fun lists -> lists.Head.Head :: lists.Head.Tail)
                |> Array.ofList

            if outputValues.Length > 0 then
                return Some outputValues
            else
                return None

        else

            return None

    else
        return None
}

[<AutoOpen>]
module ARCtrlExtensions =

    type ArcFiles with

        /// <summary>
        /// Output returns the expected sheetname and metadata values in string seqs form.
        /// </summary>
        member this.MetadataToExcelStringValues() =
            match this with
            | ArcFiles.Assay assay ->
                let metadataWorksheetName = ArcAssay.metadataSheetName
                let seqOfSeqs = ArcAssay.toMetadataCollection assay
                metadataWorksheetName, seqOfSeqs
            | ArcFiles.Investigation investigation ->
                let metadataWorksheetName = ArcInvestigation.metadataSheetName
                let seqOfSeqs = ArcInvestigation.toMetadataCollection investigation
                metadataWorksheetName, seqOfSeqs
            | ArcFiles.Study(study, assays) ->
                let metadataWorksheetName = ArcStudy.metadataSheetName

                let seqOfSeqs =
                    ArcStudy.toMetadataCollection study (Option.whereNot List.isEmpty assays)

                metadataWorksheetName, seqOfSeqs
            | ArcFiles.Template template ->
                let metadataWorksheetName = Template.metadataSheetName
                let seqOfSeqs = Template.toMetadataCollection template
                metadataWorksheetName, seqOfSeqs
            | ArcFiles.Workflow workflow ->
                let metadataWorksheetName = ArcWorkflow.metadataSheetName
                let seqOfSeqs = ArcWorkflow.toMetadataCollection workflow
                metadataWorksheetName, seqOfSeqs
            | ArcFiles.Run run ->
                let metadataWorksheetName = ArcRun.metadataSheetName
                let seqOfSeqs = ArcRun.toMetadataCollection run
                metadataWorksheetName, seqOfSeqs
            | ArcFiles.DataMap datamap -> failwith "DataMap does not have metadata representation"
