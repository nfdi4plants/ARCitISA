namespace Main.ArcMerge

open ARCtrl
open Main.ARCtrlExtensions
open Swate.Components.Shared

module ArcMergeHelper =

    type ParsedFileEvent = {
        EventName: EventName
        Path: string
        EntityRef: ArcEntityRef
    }

    type DataMapEventIndex = {
        Assays: Set<string>
        Studies: Set<string>
        Runs: Set<string>
        Workflows: Set<string>
    }

    let private emptyDataMapEventIndex = {
        Assays = Set.empty
        Studies = Set.empty
        Runs = Set.empty
        Workflows = Set.empty
    }

    let private cloneDataMapOption (dataMap: Datamap option) : Datamap option =
        dataMap
        |> Option.map (fun source ->
            let clone = DataMapCopyWorkaround.copy source
            clone.StaticHash <- source.StaticHash
            clone
        )

    let private cloneDataMapWithCleanHash (source: Datamap) : Datamap =
        let clone = DataMapCopyWorkaround.copy source
        clone.StaticHash <- cleanDataMapStaticHash source
        clone

    let private copyLicense (license: License) : License =
        let clone = License(license.Type, license.Content, license.Path)
        clone.StaticHash <- license.GetHashCode()
        clone

    let internal parseFileEvents (events: FileEvent list) : ParsedFileEvent list =
        events
        |> List.map (fun event -> {
            EventName = event.EventName
            Path = event.Path
            EntityRef = ArcEntityRef.fromPath event.Path
        })

    let internal buildDataMapEventIndex (events: ParsedFileEvent list) : DataMapEventIndex =
        events
        |> List.fold
            (fun state event ->
                match event.EntityRef with
                | ArcEntityRef.AssayDataMap id -> {
                    state with
                        Assays = Set.add id state.Assays
                  }
                | ArcEntityRef.StudyDataMap id -> {
                    state with
                        Studies = Set.add id state.Studies
                  }
                | ArcEntityRef.RunDataMap id -> {
                    state with
                        Runs = Set.add id state.Runs
                  }
                | ArcEntityRef.WorkflowDataMap id -> {
                    state with
                        Workflows = Set.add id state.Workflows
                  }
                | _ -> state
            )
            emptyDataMapEventIndex

    let private copyInvestigationFields (arcLocal: ARC) (arcRemote: ARC) : unit =
        let remoteSnapshot = arcRemote.Copy()
        arcLocal.Title <- remoteSnapshot.Title
        arcLocal.Description <- remoteSnapshot.Description
        arcLocal.SubmissionDate <- remoteSnapshot.SubmissionDate
        arcLocal.PublicReleaseDate <- remoteSnapshot.PublicReleaseDate
        arcLocal.OntologySourceReferences <- remoteSnapshot.OntologySourceReferences
        arcLocal.Publications <- remoteSnapshot.Publications
        arcLocal.Contacts <- remoteSnapshot.Contacts
        arcLocal.Comments <- remoteSnapshot.Comments
        arcLocal.Remarks <- remoteSnapshot.Remarks

    let private tryFindEntityIndex id getIdentifier entities =
        entities |> Seq.tryFindIndex (fun entity -> getIdentifier entity = id)

    /// Shared by assays, studies, runs, and workflows to keep their merge behavior consistent.
    /// The callback-heavy interface is intentionally retained here; replacing it with typed entity adapters
    /// would be a broader ArcMerge refactor than the DataMap correctness changes in this PR.
    let private applyEntityAddOrChange
        (id: string)
        (hasDataMapEvent: bool)
        (tryGetRemote: string -> 'entity option)
        (tryFindLocalIndex: string -> int option)
        (getLocal: int -> 'entity)
        (setLocal: int -> 'entity -> unit)
        (addLocal: 'entity -> unit)
        (copyEntity: 'entity -> 'entity)
        (getDataMap: 'entity -> Datamap option)
        (setDataMap: 'entity -> Datamap option -> unit)
        (getLightHash: 'entity -> int)
        (getStaticHash: 'entity -> int)
        (setStaticHash: 'entity -> int -> unit)
        =
        // The disk copy carries the hashes of the disk content, so it counts as saved.
        let copyEntityFromDisk sourceEntity =
            let targetEntity = copyEntity sourceEntity

            match getDataMap sourceEntity, getDataMap targetEntity with
            | Some source, Some target ->
                DataMapCopyWorkaround.preserveLabels source target
                target.StaticHash <- cleanDataMapStaticHash source
            | _ -> ()

            setStaticHash targetEntity (getLightHash sourceEntity)
            targetEntity

        match tryGetRemote id with
        | None -> ()
        | Some remoteEntity ->
            match tryFindLocalIndex id with
            | None -> addLocal (copyEntityFromDisk remoteEntity)
            | Some idx ->
                let localEntity = getLocal idx

                // A touched file with identical content keeps the local edits.
                if getLightHash remoteEntity <> getStaticHash localEntity then
                    let discCopy = copyEntityFromDisk remoteEntity

                    if not hasDataMapEvent then
                        let preservedDataMap = localEntity |> getDataMap |> cloneDataMapOption

                        setDataMap discCopy preservedDataMap

                    setLocal idx discCopy

    let private applyEntityUnlink (id: string) (containsLocal: string -> bool) (removeLocal: string -> unit) =
        if containsLocal id then
            removeLocal id

    let private applyDataMapAddOrChange
        (id: string)
        (tryGetLocal: string -> 'entity option)
        (tryGetRemote: string -> 'entity option)
        (getDataMap: 'entity -> Datamap option)
        (setDataMap: 'entity -> Datamap option -> unit)
        =
        match tryGetLocal id, tryGetRemote id with
        | Some localEntity, Some remoteEntity ->
            match getDataMap localEntity, getDataMap remoteEntity with
            | Some localDataMap, Some remoteDataMap when cleanDataMapStaticHash remoteDataMap = localDataMap.StaticHash ->
                ()
            | _, Some remoteDataMap -> setDataMap localEntity (Some(cloneDataMapWithCleanHash remoteDataMap))
            | _, None -> setDataMap localEntity None
        | _ -> ()

    let private applyDataMapUnlink
        (id: string)
        (tryGetLocal: string -> 'entity option)
        (setDataMap: 'entity -> Datamap option -> unit)
        =
        match tryGetLocal id with
        | None -> ()
        | Some localEntity -> setDataMap localEntity None

    let internal applyEntityEvent
        (arcLocal: ARC)
        (arcRemote: ARC)
        (dataMapEvents: DataMapEventIndex)
        (entityRef: ArcEntityRef)
        (eventName: EventName)
        : unit =
        match entityRef, eventName with
        | ArcEntityRef.Investigation, (EventName.Add | EventName.Change) ->
            if arcRemote.GetLightHashCode() <> arcLocal.StaticHash then
                copyInvestigationFields arcLocal arcRemote
                arcLocal.StaticHash <- arcRemote.GetLightHashCode()
        | ArcEntityRef.Investigation, EventName.Unlink -> ()
        | ArcEntityRef.License, _ -> arcLocal.License <- arcRemote.License |> Option.map copyLicense
        | ArcEntityRef.Assay id, (EventName.Add | EventName.Change) ->
            applyEntityAddOrChange
                id
                (Set.contains id dataMapEvents.Assays)
                (fun id -> arcRemote.TryGetAssay(id))
                (fun id ->
                    arcLocal.Assays
                    |> tryFindEntityIndex id (fun (assay: ArcAssay) -> assay.Identifier)
                )
                (fun idx -> arcLocal.Assays.[idx])
                (fun idx assay -> arcLocal.Assays.[idx] <- assay)
                (fun assay -> arcLocal.AddAssay(assay))
                (fun (assay: ArcAssay) -> assay.Copy())
                (fun (assay: ArcAssay) -> assay.Datamap)
                (fun (assay: ArcAssay) dataMap -> assay.Datamap <- dataMap)
                (fun (assay: ArcAssay) -> assay.GetLightHashCode())
                (fun (assay: ArcAssay) -> assay.StaticHash)
                (fun (assay: ArcAssay) hash -> assay.StaticHash <- hash)
        | ArcEntityRef.Assay id, EventName.Unlink ->
            applyEntityUnlink id (fun id -> arcLocal.ContainsAssay(id)) (fun id -> arcLocal.RemoveAssay(id))
        | ArcEntityRef.AssayDataMap id, (EventName.Add | EventName.Change) ->
            applyDataMapAddOrChange
                id
                (fun id -> arcLocal.TryGetAssay(id))
                (fun id -> arcRemote.TryGetAssay(id))
                (fun (assay: ArcAssay) -> assay.Datamap)
                (fun (assay: ArcAssay) dataMap -> assay.Datamap <- dataMap)
        | ArcEntityRef.AssayDataMap id, EventName.Unlink ->
            applyDataMapUnlink
                id
                (fun id -> arcLocal.TryGetAssay(id))
                (fun (assay: ArcAssay) dataMap -> assay.Datamap <- dataMap)
        | ArcEntityRef.Study id, (EventName.Add | EventName.Change) ->
            applyEntityAddOrChange
                id
                (Set.contains id dataMapEvents.Studies)
                (fun id -> arcRemote.TryGetStudy(id))
                (fun id ->
                    arcLocal.Studies
                    |> tryFindEntityIndex id (fun (study: ArcStudy) -> study.Identifier)
                )
                (fun idx -> arcLocal.Studies.[idx])
                (fun idx study -> arcLocal.Studies.[idx] <- study)
                (fun study -> arcLocal.AddStudy(study))
                (fun (study: ArcStudy) -> study.Copy())
                (fun (study: ArcStudy) -> study.Datamap)
                (fun (study: ArcStudy) dataMap -> study.Datamap <- dataMap)
                (fun (study: ArcStudy) -> study.GetLightHashCode())
                (fun (study: ArcStudy) -> study.StaticHash)
                (fun (study: ArcStudy) hash -> study.StaticHash <- hash)
        | ArcEntityRef.Study id, EventName.Unlink ->
            applyEntityUnlink id (fun id -> arcLocal.ContainsStudy(id)) (fun id -> arcLocal.RemoveStudy(id))
        | ArcEntityRef.StudyDataMap id, (EventName.Add | EventName.Change) ->
            applyDataMapAddOrChange
                id
                (fun id -> arcLocal.TryGetStudy(id))
                (fun id -> arcRemote.TryGetStudy(id))
                (fun (study: ArcStudy) -> study.Datamap)
                (fun (study: ArcStudy) dataMap -> study.Datamap <- dataMap)
        | ArcEntityRef.StudyDataMap id, EventName.Unlink ->
            applyDataMapUnlink
                id
                (fun id -> arcLocal.TryGetStudy(id))
                (fun (study: ArcStudy) dataMap -> study.Datamap <- dataMap)
        | ArcEntityRef.Run id, (EventName.Add | EventName.Change) ->
            applyEntityAddOrChange
                id
                (Set.contains id dataMapEvents.Runs)
                (fun id -> arcRemote.TryGetRun(id))
                (fun id -> arcLocal.Runs |> tryFindEntityIndex id (fun (run: ArcRun) -> run.Identifier))
                (fun idx -> arcLocal.Runs.[idx])
                (fun idx run -> arcLocal.Runs.[idx] <- run)
                (fun run -> arcLocal.AddRun(run))
                (fun (run: ArcRun) ->
                    let copiedRun = run.Copy()
                    WorkflowPayloads.syncRunPayloadBaseline run copiedRun
                    copiedRun
                )
                (fun (run: ArcRun) -> run.Datamap)
                (fun (run: ArcRun) dataMap -> run.Datamap <- dataMap)
                (fun (run: ArcRun) -> run.GetLightHashCode())
                (fun (run: ArcRun) -> run.StaticHash)
                (fun (run: ArcRun) hash -> run.StaticHash <- hash)
        | ArcEntityRef.Run id, EventName.Unlink ->
            applyEntityUnlink id (fun id -> arcLocal.ContainsRun(id)) (fun id -> arcLocal.DeleteRun(id))
        | ArcEntityRef.RunDataMap id, (EventName.Add | EventName.Change) ->
            applyDataMapAddOrChange
                id
                (fun id -> arcLocal.TryGetRun(id))
                (fun id -> arcRemote.TryGetRun(id))
                (fun (run: ArcRun) -> run.Datamap)
                (fun (run: ArcRun) dataMap -> run.Datamap <- dataMap)
        | ArcEntityRef.RunDataMap id, EventName.Unlink ->
            applyDataMapUnlink
                id
                (fun id -> arcLocal.TryGetRun(id))
                (fun (run: ArcRun) dataMap -> run.Datamap <- dataMap)
        | ArcEntityRef.Workflow id, (EventName.Add | EventName.Change) ->
            applyEntityAddOrChange
                id
                (Set.contains id dataMapEvents.Workflows)
                (fun id -> arcRemote.TryGetWorkflow(id))
                (fun id ->
                    arcLocal.Workflows
                    |> tryFindEntityIndex id (fun (workflow: ArcWorkflow) -> workflow.Identifier)
                )
                (fun idx -> arcLocal.Workflows.[idx])
                (fun idx workflow -> arcLocal.Workflows.[idx] <- workflow)
                (fun workflow -> arcLocal.AddWorkflow(workflow))
                (fun (workflow: ArcWorkflow) ->
                    let copiedWorkflow = workflow.Copy()
                    WorkflowPayloads.syncWorkflowPayloadBaseline workflow copiedWorkflow
                    copiedWorkflow
                )
                (fun (workflow: ArcWorkflow) -> workflow.Datamap)
                (fun (workflow: ArcWorkflow) dataMap -> workflow.Datamap <- dataMap)
                (fun (workflow: ArcWorkflow) -> workflow.GetLightHashCode())
                (fun (workflow: ArcWorkflow) -> workflow.StaticHash)
                (fun (workflow: ArcWorkflow) hash -> workflow.StaticHash <- hash)
        | ArcEntityRef.Workflow id, EventName.Unlink ->
            applyEntityUnlink id (fun id -> arcLocal.ContainsWorkflow(id)) (fun id -> arcLocal.DeleteWorkflow(id))
        | ArcEntityRef.WorkflowDataMap id, (EventName.Add | EventName.Change) ->
            applyDataMapAddOrChange
                id
                (fun id -> arcLocal.TryGetWorkflow(id))
                (fun id -> arcRemote.TryGetWorkflow(id))
                (fun (workflow: ArcWorkflow) -> workflow.Datamap)
                (fun (workflow: ArcWorkflow) dataMap -> workflow.Datamap <- dataMap)
        | ArcEntityRef.WorkflowDataMap id, EventName.Unlink ->
            applyDataMapUnlink
                id
                (fun id -> arcLocal.TryGetWorkflow(id))
                (fun (workflow: ArcWorkflow) dataMap -> workflow.Datamap <- dataMap)
        | ArcEntityRef.Unknown _, _ -> ()

[<AutoOpen>]
module ArcMergeLibraryExtensions =

    type ARC with
        static member merge (arcLocal: ARC) (arcRemote: ARC) (events: FileEvent list) : ARC =
            if not (arcLocal.hasInMemoryChanges ()) then
                copyArcPreservingStaticHashes arcRemote
            else
                let mergedArc = copyArcPreservingStaticHashes arcLocal
                let parsedEvents = ArcMergeHelper.parseFileEvents events
                let dataMapEvents = ArcMergeHelper.buildDataMapEventIndex parsedEvents

                for event in parsedEvents do
                    ArcMergeHelper.applyEntityEvent mergedArc arcRemote dataMapEvents event.EntityRef event.EventName

                WorkflowPayloads.mergeWorkflowPayloadsFromDisk arcLocal arcRemote mergedArc
                mergedArc
