namespace Main.ARCtrlExtensions

open ARCtrl.Contract
open Swate.Components.Shared

[<RequireQualifiedAccess>]
module ArcFileCreateContracts =

    let createContracts (withFolder: bool) (arcFile: ArcFiles) : Contract[] =
        match arcFile with
        | ArcFiles.Assay assay ->
            [|
                yield!
                    assay.ToCreateContract(withFolder)
                    |> Array.map (fun contract ->
                        if contract.DTOType = Some DTOType.ISA_Assay then
                            {
                                contract with
                                    DTO = Some(DTO.Spreadsheet(ContractPolicy.assayWorkbook assay))
                            }
                        else
                            contract
                    )
                match assay.Datamap with
                | Some dm ->
                    yield DataMapContract.create (DatamapParentInfo.create assay.Identifier DataMapParent.Assay) dm
                | None -> ()
            |]
            |> ContractPolicy.orderContracts
        | ArcFiles.Study(study, _) ->
            [|
                yield!
                    study.ToCreateContract(withFolder)
                    |> Array.map (fun contract ->
                        if contract.DTOType = Some DTOType.ISA_Study then
                            {
                                contract with
                                    DTO = Some(DTO.Spreadsheet(ContractPolicy.studyWorkbook study))
                            }
                        else
                            contract
                    )
                match study.Datamap with
                | Some dm ->
                    yield DataMapContract.create (DatamapParentInfo.create study.Identifier DataMapParent.Study) dm
                | None -> ()
            |]
            |> ContractPolicy.orderContracts
        | ArcFiles.Workflow workflow ->
            [|
                yield! workflow.ToCreateContract(withFolder)
                match workflow.Datamap with
                | Some dm ->
                    yield
                        DataMapContract.create (DatamapParentInfo.create workflow.Identifier DataMapParent.Workflow) dm
                | None -> ()
            |]
            |> ContractPolicy.orderContracts
        | ArcFiles.Run run ->
            [|
                yield! run.ToCreateContract(withFolder)
                yield! WorkflowPayloads.runContracts run Operation.CREATE
                match run.Datamap with
                | Some dm -> yield DataMapContract.create (DatamapParentInfo.create run.Identifier DataMapParent.Run) dm
                | None -> ()
            |]
            |> Array.distinctBy (fun contract -> ARCtrl.ArcPathHelper.normalizePathKey contract.Path)
            |> ContractPolicy.orderContracts
        | ArcFiles.DataMap(Some parentInfo, dataMap) -> DataMapContract.create parentInfo dataMap |> Array.singleton
        | unsupportedArcFile -> failwithf "Cannot create ARC file contracts for %A." unsupportedArcFile
