namespace Main.ArcMerge

open ARCtrl
open ARCtrl.Contract
open CrossAsync
open Main.ARCtrlExtensions
open Swate.Components.Shared

[<AutoOpen>]
module ArcAddExtensions =

    let private failIfEntityExists (entityKind: string) (identifier: string) (exists: bool) =
        if exists then
            failwith $"ARC already contains {entityKind} with identifier '{identifier}'."

    let private prepareEntityAddContracts
        (sourceArc: ARC)
        (entityKind: string)
        (identifier: string)
        (exists: bool)
        (addToArc: ARC -> unit)
        (arcFileContracts: unit -> Contract[])
        =
        failIfEntityExists entityKind identifier exists
        let workingArc = copyArcPreservingStaticHashes sourceArc
        addToArc workingArc
        workingArc.UpdateFileSystem()

        let contracts = arcFileContracts ()

        workingArc, contracts

    let private prepareAddContracts (sourceArc: ARC) (arcFile: ArcFiles) =
        match arcFile with
        | ArcFiles.Assay assay ->
            let assayCopy = assay.Copy()

            prepareEntityAddContracts
                sourceArc
                "assay"
                assay.Identifier
                (sourceArc.ContainsAssay assay.Identifier)
                (fun arc -> arc.AddAssay assayCopy)
                (fun () -> ArcFileCreateContracts.createContracts true (ArcFiles.Assay assayCopy))
        | ArcFiles.Study(study, _) ->
            let studyCopy = study.Copy()

            prepareEntityAddContracts
                sourceArc
                "study"
                study.Identifier
                (sourceArc.ContainsStudy study.Identifier)
                (fun arc -> arc.AddStudy studyCopy)
                (fun () -> ArcFileCreateContracts.createContracts true (ArcFiles.Study(studyCopy, [])))
        | ArcFiles.Run run ->
            let runCopy = run.Copy()

            prepareEntityAddContracts
                sourceArc
                "run"
                run.Identifier
                (sourceArc.ContainsRun run.Identifier)
                (fun arc -> arc.AddRun runCopy)
                (fun () -> ArcFileCreateContracts.createContracts true (ArcFiles.Run runCopy))
        | ArcFiles.Workflow workflow ->
            let workflowCopy = workflow.Copy()

            prepareEntityAddContracts
                sourceArc
                "workflow"
                workflow.Identifier
                (sourceArc.ContainsWorkflow workflow.Identifier)
                (fun arc -> arc.AddWorkflow workflowCopy)
                (fun () -> ArcFileCreateContracts.createContracts true (ArcFiles.Workflow workflowCopy))
        | ArcFiles.Investigation _ -> failwith "Adding investigation files is not supported."
        | ArcFiles.DataMap(None, _) ->
            failwith "ARCitISA could not determine which assay, study, run, or workflow this DataMap belongs to."
        | ArcFiles.DataMap(Some parentInfo, dataMap) ->
            let workingArc = copyArcPreservingStaticHashes sourceArc
            let parentPath = DatamapParentInfo.toFolderPath parentInfo

            match workingArc.TryGetDataMap parentInfo with
            | Some _ ->
                failwith
                    $"Parent '{parentPath}' already has a DataMap. Delete the existing DataMap before adding a new one."
            | None ->
                if not (workingArc.TrySetDataMap(parentInfo, Some dataMap)) then
                    failwith
                        $"Could not add the DataMap because parent '{parentPath}' was not found in the current ARC. Refresh the File Explorer and try again."

                workingArc.UpdateFileSystem()
                workingArc, ArcFileCreateContracts.createContracts false arcFile
        | ArcFiles.Template _ -> failwith "Adding template files is not supported."

    let private commitAddedArcFile (targetArc: ARC) (workingArc: ARC) (arcFile: ArcFiles) =
        match arcFile with
        | ArcFiles.Assay assay ->
            workingArc.TryGetAssay assay.Identifier
            |> Option.iter (fun sourceAssay -> targetArc.AddAssay(sourceAssay.Copy()))
        | ArcFiles.Study(study, _) ->
            workingArc.TryGetStudy study.Identifier
            |> Option.iter (fun sourceStudy -> targetArc.AddStudy(sourceStudy.Copy()))
        | ArcFiles.Run run ->
            workingArc.TryGetRun run.Identifier
            |> Option.iter (fun sourceRun -> targetArc.AddRun(sourceRun.Copy()))
        | ArcFiles.Workflow workflow ->
            workingArc.TryGetWorkflow workflow.Identifier
            |> Option.iter (fun sourceWorkflow -> targetArc.AddWorkflow(sourceWorkflow.Copy()))
        | ArcFiles.DataMap(Some parentInfo, _) ->
            workingArc.TryGetDataMap parentInfo
            |> Option.iter (fun dataMap ->
                dataMap.StaticHash <- cleanDataMapStaticHash dataMap

                targetArc.TrySetDataMap(parentInfo, Some dataMap) |> ignore
            )
        | _ -> ()

        targetArc.UpdateFileSystem()

    type ARC with

        member this.GetAssayAddContracts(assay: ArcAssay) =
            let _, contracts = prepareAddContracts this (ArcFiles.Assay assay)

            contracts

        member this.GetStudyAddContracts(study: ArcStudy) =
            let _, contracts = prepareAddContracts this (ArcFiles.Study(study, []))

            contracts

        member this.GetRunAddContracts(run: ArcRun) =
            let _, contracts = prepareAddContracts this (ArcFiles.Run run)

            contracts

        member this.GetWorkflowAddContracts(workflow: ArcWorkflow) =
            let _, contracts = prepareAddContracts this (ArcFiles.Workflow workflow)

            contracts

        member this.GetAddContracts(arcFile: ArcFiles) =
            let _, contracts = prepareAddContracts this arcFile
            contracts

        member this.TryAddArcFileAsync(arcPath: string, arcFile: ArcFiles) = crossAsync {
            try
                let workingArc, contracts = prepareAddContracts this arcFile

                let! result = ContractExecutor.fulfillContractsSwateAsync arcPath contracts

                match result with
                | Ok _ -> commitAddedArcFile this workingArc arcFile
                | Error _ -> ()

                return result
            with error ->
                return Error [| error.Message |]
        }

        member this.AddArcFileAsync(arcPath: string, arcFile: ArcFiles) = crossAsync {
            let! result = this.TryAddArcFileAsync(arcPath, arcFile)

            match result with
            | Ok _ -> ()
            | Error errors ->
                failwithf
                    "Could not add ARC file, failed with the following errors %s"
                    (PathHelpers.formatContractErrors errors)
        }
