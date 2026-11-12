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

    /// Adds a copy of the entity from the working ARC to the target ARC. The copy and its DataMap carry the hashes of saved content.
    /// baselinePayload marks the CWL payload that the add wrote as saved.
    let private addCleanCopy
        (tryGetSource: string -> 'entity option)
        (identifier: string)
        (copyEntity: 'entity -> 'entity)
        (getLightHash: 'entity -> int)
        (setStaticHash: 'entity -> int -> unit)
        (getDataMap: 'entity -> Datamap option)
        (baselinePayload: 'entity -> unit)
        (addToTarget: 'entity -> unit)
        =
        tryGetSource identifier
        |> Option.iter (fun source ->
            let copy = copyEntity source
            setStaticHash copy (getLightHash copy)
            baselinePayload copy

            getDataMap copy
            |> Option.iter (fun dataMap -> dataMap.StaticHash <- cleanDataMapStaticHash dataMap)

            addToTarget copy
        )

    let private commitAddedArcFile (targetArc: ARC) (workingArc: ARC) (arcFile: ArcFiles) =
        match arcFile with
        | ArcFiles.Assay assay ->
            addCleanCopy
                workingArc.TryGetAssay
                assay.Identifier
                (fun (source: ArcAssay) -> source.Copy())
                (fun (copy: ArcAssay) -> copy.GetLightHashCode())
                (fun (copy: ArcAssay) hash -> copy.StaticHash <- hash)
                (fun (copy: ArcAssay) -> copy.Datamap)
                ignore
                (fun copy -> targetArc.AddAssay(copy))
        | ArcFiles.Study(study, _) ->
            addCleanCopy
                workingArc.TryGetStudy
                study.Identifier
                (fun (source: ArcStudy) -> source.Copy())
                (fun (copy: ArcStudy) -> copy.GetLightHashCode())
                (fun (copy: ArcStudy) hash -> copy.StaticHash <- hash)
                (fun (copy: ArcStudy) -> copy.Datamap)
                ignore
                (fun copy -> targetArc.AddStudy(copy))
        | ArcFiles.Run run ->
            addCleanCopy
                workingArc.TryGetRun
                run.Identifier
                (fun (source: ArcRun) -> source.Copy())
                (fun (copy: ArcRun) -> copy.GetLightHashCode())
                (fun (copy: ArcRun) hash -> copy.StaticHash <- hash)
                (fun (copy: ArcRun) -> copy.Datamap)
                // The add wrote the CWL description and inputs.
                WorkflowPayloads.baselineRunPayload
                (fun copy -> targetArc.AddRun(copy))
        | ArcFiles.Workflow workflow ->
            addCleanCopy
                workingArc.TryGetWorkflow
                workflow.Identifier
                (fun (source: ArcWorkflow) -> source.Copy())
                (fun (copy: ArcWorkflow) -> copy.GetLightHashCode())
                (fun (copy: ArcWorkflow) hash -> copy.StaticHash <- hash)
                (fun (copy: ArcWorkflow) -> copy.Datamap)
                // The add wrote the CWL description.
                WorkflowPayloads.baselineWorkflowPayload
                (fun copy -> targetArc.AddWorkflow(copy))
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
