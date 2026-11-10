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

    /// The ARC with the investigation part of the disk copy and the local entities. Identifier has no public setter, so a new ARC holds both.
    let internal withInvestigationFromDisk (arcLocal: ARC) (arcRemote: ARC) : ARC =
        let remoteHash = arcRemote.GetLightHashCode()

        // A touched file with identical content keeps the local edits.
        if remoteHash = arcLocal.StaticHash then
            arcLocal
        else
            let investigation = arcRemote.Copy()
            investigation.Assays <- arcLocal.Assays
            investigation.Studies <- arcLocal.Studies
            investigation.Runs <- arcLocal.Runs
            investigation.Workflows <- arcLocal.Workflows

            let merged =
                ARC.fromArcInvestigation (investigation, arcLocal.FileSystem, ?license = arcLocal.License)

            merged.StaticHash <- remoteHash
            merged

    let private tryFindEntityIndex id getIdentifier entities =
        entities |> Seq.tryFindIndex (fun entity -> getIdentifier entity = id)

    /// Shared by assays, studies, runs, and workflows to keep their merge behavior consistent.
    /// The callback-heavy interface is intentionally retained here; replacing it with typed entity adapters
    /// would be a broader ArcMerge refactor than the DataMap correctness changes in this PR.
    let private applyEntityAddOrChange
        (id: string)
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

                    // A DataMap event alone replaces the DataMap, an entity event keeps the local one.
                    setDataMap discCopy (localEntity |> getDataMap |> cloneDataMapOption)
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
        (entityRef: ArcEntityRef)
        (eventName: EventName)
        : unit =
        match entityRef, eventName with
        | ArcEntityRef.Investigation, _ -> ()
        | ArcEntityRef.License, _ -> arcLocal.License <- arcRemote.License |> Option.map copyLicense
        | ArcEntityRef.Assay id, (EventName.Add | EventName.Change) ->
            applyEntityAddOrChange
                id
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
            applyEntityUnlink id (fun id -> arcLocal.ContainsAssay(id)) (fun id -> arcLocal.DeleteAssay(id))
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
            applyEntityUnlink id (fun id -> arcLocal.ContainsStudy(id)) (fun id -> arcLocal.DeleteStudy(id))
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

    type private EntityFacts = {
        StaticHash: int
        LightHash: int
        DataMap: Datamap option
    }

    let private tryEntityFacts (arc: ARC) (entityRef: ArcEntityRef) : EntityFacts option =
        let facts staticHash lightHash dataMap =
            Some {
                StaticHash = staticHash
                LightHash = lightHash
                DataMap = dataMap
            }

        match entityRef with
        | ArcEntityRef.Assay id
        | ArcEntityRef.AssayDataMap id ->
            arc.TryGetAssay(id)
            |> Option.bind (fun e -> facts e.StaticHash (e.GetLightHashCode()) e.Datamap)
        | ArcEntityRef.Study id
        | ArcEntityRef.StudyDataMap id ->
            arc.TryGetStudy(id)
            |> Option.bind (fun e -> facts e.StaticHash (e.GetLightHashCode()) e.Datamap)
        | ArcEntityRef.Run id
        | ArcEntityRef.RunDataMap id ->
            arc.TryGetRun(id)
            |> Option.bind (fun e -> facts e.StaticHash (e.GetLightHashCode()) e.Datamap)
        | ArcEntityRef.Workflow id
        | ArcEntityRef.WorkflowDataMap id ->
            arc.TryGetWorkflow(id)
            |> Option.bind (fun e -> facts e.StaticHash (e.GetLightHashCode()) e.Datamap)
        | _ -> None

    /// The ARC-relative paths of the events whose disk version replaces unsaved edits of arcLocal.
    /// It applies the same hash checks as the merge, so a touched file with unchanged content replaces nothing.
    /// arcRemote must carry the disk hashes, which GetLightHashCode computes itself.
    let replacedUnsavedFiles (arcLocal: ARC) (arcRemote: ARC) (events: FileEvent list) : string[] =
        let replacesUnsaved (event: ParsedFileEvent) =
            let isUnlink = event.EventName = EventName.Unlink

            match event.EntityRef with
            | ArcEntityRef.Investigation ->
                not isUnlink
                && arcLocal.StaticHash <> arcLocal.GetLightHashCode()
                && arcRemote.GetLightHashCode() <> arcLocal.StaticHash
            | ArcEntityRef.Assay _
            | ArcEntityRef.Study _
            | ArcEntityRef.Run _
            | ArcEntityRef.Workflow _ ->
                match tryEntityFacts arcLocal event.EntityRef with
                | Some local when local.StaticHash <> local.LightHash ->
                    isUnlink
                    || tryEntityFacts arcRemote event.EntityRef
                       |> Option.exists (fun remote -> remote.LightHash <> local.StaticHash)
                | _ -> false
            | ArcEntityRef.AssayDataMap _
            | ArcEntityRef.StudyDataMap _
            | ArcEntityRef.RunDataMap _
            | ArcEntityRef.WorkflowDataMap _ ->
                match tryEntityFacts arcLocal event.EntityRef |> Option.bind _.DataMap with
                | Some localDataMap when localDataMap.hasInMemoryChanges () ->
                    isUnlink
                    || match tryEntityFacts arcRemote event.EntityRef |> Option.bind _.DataMap with
                       | None -> true
                       | Some remoteDataMap -> cleanDataMapStaticHash remoteDataMap <> localDataMap.StaticHash
                | _ -> false
            | ArcEntityRef.License
            | ArcEntityRef.Unknown _ -> false

        events
        |> parseFileEvents
        |> List.filter replacesUnsaved
        |> List.map _.Path
        |> List.distinct
        |> List.toArray

[<AutoOpen>]
module ArcMergeLibraryExtensions =

    type ARC with
        static member merge (arcLocal: ARC) (arcRemote: ARC) (events: FileEvent list) : ARC =
            if not (arcLocal.hasInMemoryChanges ()) then
                copyArcPreservingStaticHashes arcRemote
            else
                let mutable mergedArc = copyArcPreservingStaticHashes arcLocal

                for event in ArcMergeHelper.parseFileEvents events do
                    match event.EntityRef, event.EventName with
                    | ArcEntityRef.Investigation, (EventName.Add | EventName.Change) ->
                        mergedArc <- ArcMergeHelper.withInvestigationFromDisk mergedArc arcRemote
                    | entityRef, eventName -> ArcMergeHelper.applyEntityEvent mergedArc arcRemote entityRef eventName

                WorkflowPayloads.mergeWorkflowPayloadsFromDisk arcLocal arcRemote mergedArc
                mergedArc
