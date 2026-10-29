module ARCtrl.Json

open Swate.Components.Shared
open Swate.Components.Composite.TableImport.Types

open ARCtrl
open System
open ARCtrl.Json

module Generic =

    let readFromJsonMap =
        Map [
            (ArcFilesDiscriminate.Investigation, JsonExportFormat.ARCtrl),
            fun json -> ArcInvestigation.fromJsonString json |> ArcFiles.Investigation
            (ArcFilesDiscriminate.Investigation, JsonExportFormat.ARCtrlCompressed),
            fun json -> ArcInvestigation.fromCompressedJsonString json |> ArcFiles.Investigation
            (ArcFilesDiscriminate.Investigation, JsonExportFormat.ISA),
            fun json -> ArcInvestigation.fromISAJsonString json |> ArcFiles.Investigation
            (ArcFilesDiscriminate.Investigation, JsonExportFormat.ROCrate),
            fun json -> ArcInvestigation.fromROCrateJsonString json |> ArcFiles.Investigation

            (ArcFilesDiscriminate.Study, JsonExportFormat.ARCtrl),
            fun json -> ArcStudy.fromJsonString json |> fun x -> ArcFiles.Study(x, [])
            (ArcFilesDiscriminate.Study, JsonExportFormat.ARCtrlCompressed),
            fun json -> ArcStudy.fromCompressedJsonString json |> fun x -> ArcFiles.Study(x, [])
            (ArcFilesDiscriminate.Study, JsonExportFormat.ISA),
            fun json -> ArcStudy.fromISAJsonString json |> ArcFiles.Study
            (ArcFilesDiscriminate.Study, JsonExportFormat.ROCrate),
            fun json -> ArcStudy.fromROCrateJsonString json |> ArcFiles.Study

            (ArcFilesDiscriminate.Assay, JsonExportFormat.ARCtrl),
            fun json -> ArcAssay.fromJsonString json |> ArcFiles.Assay
            (ArcFilesDiscriminate.Assay, JsonExportFormat.ARCtrlCompressed),
            fun json -> ArcAssay.fromCompressedJsonString json |> ArcFiles.Assay
            (ArcFilesDiscriminate.Assay, JsonExportFormat.ISA),
            fun json -> ArcAssay.fromISAJsonString json |> ArcFiles.Assay
            (ArcFilesDiscriminate.Assay, JsonExportFormat.ROCrate),
            fun json -> ArcAssay.fromROCrateJsonString json |> ArcFiles.Assay

            (ArcFilesDiscriminate.Template, JsonExportFormat.ARCtrl),
            fun json -> Template.fromJsonString json |> ArcFiles.Template
            (ArcFilesDiscriminate.Template, JsonExportFormat.ARCtrlCompressed),
            fun json -> Template.fromCompressedJsonString json |> ArcFiles.Template

            (ArcFilesDiscriminate.Run, JsonExportFormat.ARCtrl), fun json -> ArcRun.fromJsonString json |> ArcFiles.Run
            (ArcFilesDiscriminate.Run, JsonExportFormat.ARCtrlCompressed),
            fun json -> ArcRun.fromCompressedJsonString json |> ArcFiles.Run

            (ArcFilesDiscriminate.Workflow, JsonExportFormat.ARCtrl),
            fun json -> ArcWorkflow.fromJsonString json |> ArcFiles.Workflow
            (ArcFilesDiscriminate.Workflow, JsonExportFormat.ARCtrlCompressed),
            fun json -> ArcWorkflow.fromCompressedJsonString json |> ArcFiles.Workflow

            (ArcFilesDiscriminate.DataMap, JsonExportFormat.ARCtrl),
            fun json -> ArcFiles.DataMap(None, DataMap.fromJsonString json)
        ]

    let toFileName (id: string) (fileType: ArcFilesDiscriminate) (jsonType: JsonExportFormat) =
        let n = System.DateTime.Now.ToUniversalTime().ToString("yyyyMMdd_HHmmss")
        let formatString = jsonType.ToString()
        let fileTypeString = fileType.ToString()
        n + "_" + fileTypeString + "_" + id + "_" + formatString + ".json"

    let private formatOrder = [
        JsonExportFormat.ARCtrl
        JsonExportFormat.ARCtrlCompressed
        JsonExportFormat.ISA
        JsonExportFormat.ROCrate
    ]

    let private supportedFormatsFromReadMap (fileType: ArcFilesDiscriminate) =
        readFromJsonMap.Keys
        |> Seq.choose (fun (arcFileType, jsonFormat) -> if arcFileType = fileType then Some jsonFormat else None)
        |> Seq.distinct
        |> Seq.sortBy (fun jsonFormat ->
            formatOrder
            |> List.tryFindIndex ((=) jsonFormat)
            |> Option.defaultValue Int32.MaxValue
        )
        |> Seq.toList

    let private preferredFormat (formats: JsonExportFormat list) =
        if formats |> List.contains JsonExportFormat.ROCrate then
            Some JsonExportFormat.ROCrate
        elif formats |> List.contains JsonExportFormat.ARCtrl then
            Some JsonExportFormat.ARCtrl
        else
            formats |> List.tryHead

    let supportedExportFormats (fileType: ArcFilesDiscriminate) = supportedFormatsFromReadMap fileType

    let supportedImportFormats (fileType: ArcFilesDiscriminate) = supportedFormatsFromReadMap fileType

    let isExportFormatSupported (fileType: ArcFilesDiscriminate) (jsonFormat: JsonExportFormat) =
        supportedExportFormats fileType |> List.contains jsonFormat

    let isImportFormatSupported (fileType: ArcFilesDiscriminate) (jsonFormat: JsonExportFormat) =
        supportedImportFormats fileType |> List.contains jsonFormat

    let tryGetDefaultExportFormat (fileType: ArcFilesDiscriminate) =
        supportedExportFormats fileType |> preferredFormat

    let tryGetDefaultImportFormat (fileType: ArcFilesDiscriminate) =
        supportedImportFormats fileType |> preferredFormat

    let tryParseJsonFileName (fileName: string) =
        if fileName.EndsWith(".json") then
            let parts =
                fileName.Substring(0, fileName.Length - 5).Split([| "_" |], StringSplitOptions.RemoveEmptyEntries)

            let jsonFormat =
                parts |> Array.tryPick (fun part -> JsonExportFormat.tryFromString part)

            let arcfile =
                parts |> Array.tryPick (fun part -> ArcFilesDiscriminate.tryFromString part)

            match jsonFormat, arcfile with
            | Some jf, Some af -> Some(jf, af)
            | _ -> None
        else
            None

module Import =

    let private arcFileTypeLabel (fileType: ArcFilesDiscriminate) = fileType |> unbox<string>

    let applyToCurrentArcFile (currentArcFile: ArcFiles, importedArcFile: ArcFiles) =
        let currentFileType = currentArcFile.RelatedArcFilesDiscriminate
        let importedFileType = importedArcFile.RelatedArcFilesDiscriminate

        if currentFileType <> importedFileType then
            Error(
                exn
                    $"Cannot import {arcFileTypeLabel importedFileType} JSON into the current {arcFileTypeLabel currentFileType} editor."
            )
        else
            match currentArcFile, importedArcFile with
            | ArcFiles.DataMap(currentParentInfo, _), ArcFiles.DataMap(_, importedDataMap) ->
                Ok(ArcFiles.DataMap(currentParentInfo, importedDataMap))
            | _ when currentArcFile.CanCreateTables() ->
                let importedTables = importedArcFile.Tables() |> Array.ofSeq

                if importedTables.Length = 0 then
                    Error(exn $"Imported {arcFileTypeLabel importedFileType} JSON does not contain tables to append.")
                else
                    let sources =
                        importedTables
                        |> Array.mapi (fun index table -> {
                            Index = index
                            Name = table.Name
                            Table = table
                        })

                    let defaults: TableImportDecisions = {
                        ImportType = TableJoinOptions.WithValues
                        DefaultAction = TableImportAction.ImportAsNewTable
                        Actions = Map.empty
                        DeselectedColumns = Set.empty
                        EnteredNames = Map.empty
                        ActiveTableIndex = None
                    }

                    Swate.Components.Composite.TableImport.Helper.applyImport currentArcFile sources defaults
                    |> Result.map (fun result -> result.ArcFile)
            | _ -> Ok importedArcFile

    let tryParseFromJsonString
        (
            jsonString: string,
            jsonType: JsonExportFormat option,
            filetype: ArcFilesDiscriminate option,
            fileName: string option
        ) : ArcFiles option =
        let assumedJsonType =
            match jsonType, filetype with
            | Some jt, Some ft -> (jt, ft) |> Some
            | _, _ ->
                match fileName with
                | Some name ->
                    let resOpt = Generic.tryParseJsonFileName name

                    match resOpt with
                    | Some(jf, af) -> Some(jf, af)
                    | None -> None
                | None -> None

        match assumedJsonType with
        | Some(jsonFormat, arcfileType) ->
            match Generic.readFromJsonMap.TryFind(arcfileType, jsonFormat) with
            | Some arcFileFn ->
                try
                    arcFileFn jsonString |> Some
                with _ ->
                    None
            | None -> None
        | None -> None

    let parseFromJsonString
        (
            jsonString: string,
            jsonType: JsonExportFormat option,
            filetype: ArcFilesDiscriminate option,
            fileName: string option
        ) : ArcFiles =
        match tryParseFromJsonString (jsonString, jsonType, filetype, fileName) with
        | Some arcfile -> arcfile
        | None ->
            failwith
                "Error. Unable to find correct JSON format. This function relies on correct naming conventions for the file. We will improve this in the future. The file name must contain the json format, as well as the file type, separated by \"_\". Example: 'ARCtrlCompressed_Assay.json"

module Export =

    let parseToJsonString (arcfile: ArcFiles, jef: JsonExportFormat) =
        let name, jsonString =
            let nameFromId (id: string) =
                Generic.toFileName id arcfile.RelatedArcFilesDiscriminate jef

            match arcfile, jef with
            | ArcFiles.Investigation ai, JsonExportFormat.ARCtrl ->
                nameFromId ai.Identifier, ArcInvestigation.toJsonString 0 ai
            | ArcFiles.Investigation ai, JsonExportFormat.ARCtrlCompressed ->
                nameFromId ai.Identifier, ArcInvestigation.toCompressedJsonString 0 ai
            | ArcFiles.Investigation ai, JsonExportFormat.ISA ->
                nameFromId ai.Identifier, ArcInvestigation.toISAJsonString 0 ai
            | ArcFiles.Investigation ai, JsonExportFormat.ROCrate ->
                nameFromId ai.Identifier, ArcInvestigation.toROCrateJsonString 0 ai

            | ArcFiles.Study(as', _), JsonExportFormat.ARCtrl ->
                nameFromId as'.Identifier, ArcStudy.toJsonString 0 (as')
            | ArcFiles.Study(as', _), JsonExportFormat.ARCtrlCompressed ->
                nameFromId as'.Identifier, ArcStudy.toCompressedJsonString 0 (as')
            | ArcFiles.Study(as', aaList), JsonExportFormat.ISA ->
                nameFromId as'.Identifier, ArcStudy.toISAJsonString (aaList, 0) (as')
            | ArcFiles.Study(as', aaList), JsonExportFormat.ROCrate ->
                nameFromId as'.Identifier, ArcStudy.toROCrateJsonString (aaList, 0) (as')

            | ArcFiles.Assay aa, JsonExportFormat.ARCtrl -> nameFromId aa.Identifier, ArcAssay.toJsonString 0 aa
            | ArcFiles.Assay aa, JsonExportFormat.ARCtrlCompressed ->
                nameFromId aa.Identifier, ArcAssay.toCompressedJsonString 0 aa
            | ArcFiles.Assay aa, JsonExportFormat.ISA -> nameFromId aa.Identifier, ArcAssay.toISAJsonString 0 aa
            | ArcFiles.Assay aa, JsonExportFormat.ROCrate ->
                nameFromId aa.Identifier, ArcAssay.toROCrateJsonString () aa

            | ArcFiles.Template t, JsonExportFormat.ARCtrl ->
                nameFromId (t.Name.Replace(" ", "_") + ".xlsx"), Template.toJsonString 0 t
            | ArcFiles.Template t, JsonExportFormat.ARCtrlCompressed ->
                nameFromId (t.Name.Replace(" ", "_") + ".xlsx"), Template.toCompressedJsonString 0 t
            | ArcFiles.Template _, anyElse ->
                failwithf "Error. It is not intended to parse Template to %s format." (string anyElse)

            | ArcFiles.Run r, JsonExportFormat.ARCtrl -> nameFromId r.Identifier, ArcRun.toJsonString 0 r
            | ArcFiles.Run r, JsonExportFormat.ARCtrlCompressed ->
                nameFromId r.Identifier, ArcRun.toCompressedJsonString 0 r
            | ArcFiles.Run _, anyElse ->
                failwithf "Error. It is not intended to parse Run to %s format." (string anyElse)

            | ArcFiles.Workflow w, JsonExportFormat.ARCtrl -> nameFromId w.Identifier, ArcWorkflow.toJsonString 0 w
            | ArcFiles.Workflow w, JsonExportFormat.ARCtrlCompressed ->
                nameFromId w.Identifier, ArcWorkflow.toCompressedJsonString 0 w
            | ArcFiles.Workflow _, anyElse ->
                failwithf "Error. It is not intended to parse Workflow to %s format." (string anyElse)

            | ArcFiles.DataMap(_, d), JsonExportFormat.ARCtrl -> nameFromId "", DataMap.toJsonString 0 d
            | ArcFiles.DataMap(_, _), anyElse ->
                failwithf "Error. It is not intended to parse Datamap to %s format." (string anyElse)

        let jsonString =
            match jef with
            | JsonExportFormat.ARCtrl -> EmptyColumnJson.normalize false jsonString
            | JsonExportFormat.ARCtrlCompressed -> EmptyColumnJson.normalize true jsonString
            | _ -> jsonString

        name, jsonString

    let tryParseToJsonString (arcfile: ArcFiles, jef: JsonExportFormat) =
        if Generic.isExportFormatSupported arcfile.RelatedArcFilesDiscriminate jef then
            try
                parseToJsonString (arcfile, jef) |> Ok
            with exn ->
                Error exn
        else
            Error(
                exn $"JSON export format {jef.AsStringRdbl} is not supported for {arcfile.RelatedArcFilesDiscriminate}."
            )
