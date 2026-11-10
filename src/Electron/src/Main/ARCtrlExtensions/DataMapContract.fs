module Main.ARCtrlExtensions.DataMapContract

open ARCtrl
open ARCtrl.Contract
open Swate.Components.Shared

let create (parentInfo: DatamapParentInfo) (dataMap: Datamap) : Contract =
    match parentInfo.Parent with
    | DataMapParent.Assay -> dataMap.ToCreateContractForAssay(parentInfo.ParentId)
    | DataMapParent.Study -> dataMap.ToCreateContractForStudy(parentInfo.ParentId)
    | DataMapParent.Run -> dataMap.ToCreateContractForRun(parentInfo.ParentId)
    | DataMapParent.Workflow -> dataMap.ToCreateContractForWorkflow(parentInfo.ParentId)

let update (parentInfo: DatamapParentInfo) (dataMap: Datamap) : Contract =
    match parentInfo.Parent with
    | DataMapParent.Assay -> dataMap.ToUpdateContractForAssay(parentInfo.ParentId)
    | DataMapParent.Study -> dataMap.ToUpdateContractForStudy(parentInfo.ParentId)
    | DataMapParent.Run -> dataMap.ToUpdateContractForRun(parentInfo.ParentId)
    | DataMapParent.Workflow -> dataMap.ToUpdateContractForWorkflow(parentInfo.ParentId)

let delete (parentInfo: DatamapParentInfo) (dataMap: Datamap) : Contract =
    match parentInfo.Parent with
    | DataMapParent.Assay -> dataMap.ToDeleteContractForAssay(parentInfo.ParentId)
    | DataMapParent.Study -> dataMap.ToDeleteContractForStudy(parentInfo.ParentId)
    | DataMapParent.Run -> dataMap.ToDeleteContractForRun(parentInfo.ParentId)
    | DataMapParent.Workflow -> dataMap.ToDeleteContractForWorkflow(parentInfo.ParentId)

let parentWorkbookPath (parentInfo: DatamapParentInfo) : string =
    match parentInfo.Parent with
    | DataMapParent.Assay -> ARCtrl.Helper.Identifier.Assay.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Study -> ARCtrl.Helper.Identifier.Study.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Run -> ARCtrl.Helper.Identifier.Run.fileNameFromIdentifier parentInfo.ParentId
    | DataMapParent.Workflow -> ARCtrl.Helper.Identifier.Workflow.fileNameFromIdentifier parentInfo.ParentId
