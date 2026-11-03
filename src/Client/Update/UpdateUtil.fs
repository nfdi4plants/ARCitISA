module Update.UpdateUtil

open ARCtrl
open Swate.Components.Shared
open Fable.Remoting.Client
open OfficeInterop.Core

let download (filename, bytes: byte[]) = bytes.SaveFileAs(filename)

let downloadFromString (filename, content: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(content)
    bytes.SaveFileAs(filename)

// module JsonHelper =

//     open ARCtrl.Json

//     open Thoth.Json
//     open Thoth.Json.Core

//     let wholeDatamapEncoder (parentId: string) (parent: ARCtrl.ARCtrlHelper.DataMapParent) (datamap: ARCtrl.Datamap) =
//         Encode.object [
//             "ParentId", Encode.string parentId
//             "Parent", Encode.string (parent.ToString())
//             "Datamap", Datamap.encoder datamap
//         ]

//     let wholeDatamapDecoder =
//         Decode.object (fun get ->
//             let parentId = get.Required.Field "ParentId" Decode.string

//             let parent =
//                 let temp = get.Required.Field "Parent" Decode.string
//                 DataMapParent.tryFromString (temp)

//             let datamapParent = createDataMapParentInfo parentId parent
//             let datamap = get.Required.Field "Datamap" Datamap.decoder
//             datamapParent, datamap
//         )

module JsonImportHelper =

    open ARCtrl
    open FileImport

    /// <summary>
    ///
    /// </summary>
    /// <param name="arcTables"></param>
    /// <param name="state"></param>
    /// <param name="deselectedColumns"></param>
    /// <param name="fullImport"></param>
    let createUpdatedTables
        (arcTables: ResizeArray<ArcTable>)
        (state: SelectiveImportConfig)
        (deselectedColumns: Set<int * int>)
        fullImport
        =
        [
            for importTable in state.ImportTables do
                let fullImport = defaultArg fullImport importTable.FullImport

                if importTable.FullImport = fullImport then
                    let deselectedColumnIndices =
                        getDeselectedTableColumnIndices deselectedColumns importTable.Index

                    let sourceTable = arcTables.[importTable.Index]
                    let appliedTable = ArcTable.init (sourceTable.Name)

                    let finalTable =
                        Table.selectiveTablePrepare appliedTable sourceTable deselectedColumnIndices

                    appliedTable.Join(finalTable, joinOptions = state.ImportType)
                    appliedTable
        ]
        |> ResizeArray

    /// <summary>
    ///
    /// </summary>
    /// <param name="uploadedFile"></param>
    /// <param name="state"></param>
    /// <param name="deselectedColumns"></param>
    let updateWithMetadata (uploadedFile: ArcFiles) (state: SelectiveImportConfig) (deselectedColumns: Set<int * int>) =
        if not state.ImportMetadata then
            failwith "Metadata must be imported"

        /// This updates the existing tables based on import config (joinOptions)
        let arcFile =
            match uploadedFile with
            | ArcFiles.Assay a as arcFile ->
                let tables = createUpdatedTables a.Tables state deselectedColumns None
                a.Tables <- tables
                arcFile
            | ArcFiles.Run r as arcFile ->
                let tables = createUpdatedTables r.Tables state deselectedColumns None
                r.Tables <- tables
                arcFile
            | ArcFiles.Study(s, _) as arcFile ->
                let tables = createUpdatedTables s.Tables state deselectedColumns None
                s.Tables <- tables
                arcFile
            | ArcFiles.Template t as arcFile ->
                let table = createUpdatedTables (ResizeArray[t.Table]) state deselectedColumns None
                t.Table <- table.[0]
                arcFile
            | ArcFiles.Investigation _
            | ArcFiles.Workflow _
            | ArcFiles.DataMap _ as arcFile -> arcFile

        arcFile

    /// <summary>
    ///
    /// </summary>
    /// <param name="importTables"></param>
    /// <param name="importState"></param>
    /// <param name="activeTableIndex"></param>
    /// <param name="existingOpt"></param>
    /// <param name="deselectedColumns"></param>
    let updateTables
        (importTables: ResizeArray<ArcTable>)
        (importConfig: SelectiveImportConfig)
        (activeTableIndex: int option)
        (existingOpt: ArcFiles option)
        =
        let deselectedColumns = importConfig.DeselectedColumns

        match existingOpt with
        | Some existing ->
            let existingTables = existing.Tables()

            let appendTables =
                // only append if the active table exists (this is to handle join call on investigations)
                match activeTableIndex with
                | Some i when i >= 0 && i < existingTables.Count ->
                    let activeTable = existingTables.[i]

                    let selectedColumnTables =
                        createUpdatedTables importTables importConfig deselectedColumns (Some false)
                        |> Array.ofSeq
                        |> Array.rev

                    /// Everything will be appended against this table, which in the end will be appended to the main table
                    // Remove duplicate unique columns Input & Output
                    // Keep input & output columns of active table or first table that had them when appending
                    let tempTable = activeTable.Copy()

                    for table in selectedColumnTables do

                        if table.RowCount = 0 then
                            let cells =
                                table.Columns
                                |> Array.ofSeq
                                |> Array.map (fun column ->
                                    match column.Header with
                                    | CompositeHeader.Factor _
                                    | CompositeHeader.Component _
                                    | CompositeHeader.Parameter _
                                    | CompositeHeader.Characteristic _ ->
                                        match importConfig.ImportType with
                                        | TableJoinOptions.WithUnit ->
                                            CompositeCell.Unitized("", (OntologyAnnotation.empty ()))
                                        | _ -> CompositeCell.Term(OntologyAnnotation.empty ())
                                    | _ -> CompositeCell.FreeText ""
                                )
                                |> ResizeArray

                            let rows = Array.create tempTable.RowCount cells
                            table.AddRows(ResizeArray rows)

                        elif table.RowCount < tempTable.RowCount then
                            table.AddRowsEmpty(tempTable.RowCount - table.RowCount)

                        let preparedTemplate = Table.distinctByHeader tempTable table
                        tempTable.Join(preparedTemplate, joinOptions = importConfig.ImportType)

                    existingTables.[i] <- tempTable
                | _ -> ()

            let addTables =
                let selectedColumnTables =
                    createUpdatedTables importTables importConfig deselectedColumns (Some true)
                    |> Array.ofSeq
                    |> Array.rev

                selectedColumnTables
                |> Seq.map (fun table -> // update tables based on joinOptions
                    let nTable = ArcTable.init (table.Name)
                    nTable.Join(table, joinOptions = importConfig.ImportType)
                    nTable
                )
                |> Seq.rev // https://github.com/nfdi4plants/Swate/issues/577
                |> Seq.iter (fun table -> existingTables.Add table)

            existing
        | None -> //
            failwith "Error! Can only append information if metadata sheet exists!"

    /// <summary>
    ///
    /// </summary>
    /// <param name="import"></param>
    /// <param name="importState"></param>
    /// <param name="activeTableIndex"></param>
    /// <param name="existingOpt"></param>
    /// <param name="deselectedColumns"></param>
    let updateArcFileTables
        (import: ArcFiles)
        (importState: SelectiveImportConfig)
        (activeTableIndex: int option)
        (existingOpt: ArcFiles option)
        =
        let importTables =
            match import with
            | ArcFiles.Assay a -> a.Tables
            | ArcFiles.Run r -> r.Tables
            | ArcFiles.Study(s, _) -> s.Tables
            | ArcFiles.Template t -> ResizeArray([ t.Table ])
            | ArcFiles.DataMap _
            | ArcFiles.Workflow _
            | ArcFiles.Investigation _ -> ResizeArray()

        updateTables importTables importState activeTableIndex existingOpt
