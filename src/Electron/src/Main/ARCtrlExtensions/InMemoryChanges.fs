namespace Main.ARCtrlExtensions

open ARCtrl
open Swate.Components.Shared

[<AutoOpen>]
module InMemoryChangesExtensions =

    // Used to handle initiated but empty datamaps that have a StaticHash of 0, which would otherwise be indistinguishable from unchanged datamaps.
    let private cleanEmptyDataMapStaticHash = System.Int32.MinValue

    let cleanDataMapStaticHash (dataMap: Datamap) =
        let hash = dataMap.GetHashCode()

        if hash = 0 then cleanEmptyDataMapStaticHash else hash

    let private baselineDataMapStaticHash (dataMap: Datamap option) =
        dataMap |> Option.iter (fun dm -> dm.StaticHash <- cleanDataMapStaticHash dm)

    /// Sets the current loaded ARC state as the clean in-memory baseline.
    let baselineArcStaticHashes (arc: ARC) : unit =
        WorkflowPayloads.baselineWorkflowPayloads arc

        arc.License
        |> Option.iter (fun license -> license.StaticHash <- license.GetHashCode())

        for study in arc.Studies do
            study.StaticHash <- study.GetLightHashCode()
            baselineDataMapStaticHash study.Datamap

        for assay in arc.Assays do
            assay.StaticHash <- assay.GetLightHashCode()
            baselineDataMapStaticHash assay.Datamap

        for workflow in arc.Workflows do
            workflow.StaticHash <- workflow.GetLightHashCode()
            baselineDataMapStaticHash workflow.Datamap

        for run in arc.Runs do
            run.StaticHash <- run.GetLightHashCode()
            baselineDataMapStaticHash run.Datamap

        arc.StaticHash <- arc.GetLightHashCode()

    type Datamap with
        member this.hasInMemoryChanges() : bool =
            if this.StaticHash = cleanEmptyDataMapStaticHash then
                this.GetHashCode() <> 0
            else
                this.StaticHash = 0 || this.StaticHash <> this.GetHashCode()

    let private hasStaticHashOrDataMapChanges staticHash lightHash (dataMap: Datamap option) =
        staticHash <> lightHash
        || (dataMap |> Option.exists (fun dm -> dm.hasInMemoryChanges ()))

    let private syncDataMapStaticHash (sourceDataMap: Datamap option) (targetDataMap: Datamap option) =
        match sourceDataMap, targetDataMap with
        | Some sourceDataMap, Some targetDataMap -> targetDataMap.StaticHash <- sourceDataMap.StaticHash
        | _ -> ()

    let private syncEntityStaticHashes
        (targetEntities: seq<'Entity>)
        (tryGetSource: string -> 'Entity option)
        (getIdentifier: 'Entity -> string)
        (syncEntityStaticHash: 'Entity -> 'Entity -> unit)
        =
        for targetEntity in targetEntities do
            targetEntity
            |> getIdentifier
            |> tryGetSource
            |> Option.iter (fun sourceEntity -> syncEntityStaticHash sourceEntity targetEntity)

    let private preserveEntityDataMapLabels
        (targetEntities: seq<'Entity>)
        (tryGetSource: string -> 'Entity option)
        (getIdentifier: 'Entity -> string)
        (getDataMap: 'Entity -> Datamap option)
        =
        for targetEntity in targetEntities do
            targetEntity
            |> getIdentifier
            |> tryGetSource
            |> Option.iter (fun sourceEntity ->
                match getDataMap sourceEntity, getDataMap targetEntity with
                | Some source, Some target -> DataMapCopyWorkaround.preserveLabels source target
                | _ -> ()
            )

    let private preserveArcDataMapLabels (source: ARC) (target: ARC) =
        preserveEntityDataMapLabels
            target.Assays
            source.TryGetAssay
            (fun (assay: ArcAssay) -> assay.Identifier)
            (fun assay -> assay.Datamap)

        preserveEntityDataMapLabels
            target.Studies
            source.TryGetStudy
            (fun (study: ArcStudy) -> study.Identifier)
            (fun study -> study.Datamap)

        preserveEntityDataMapLabels
            target.Runs
            source.TryGetRun
            (fun (run: ArcRun) -> run.Identifier)
            (fun run -> run.Datamap)

        preserveEntityDataMapLabels
            target.Workflows
            source.TryGetWorkflow
            (fun (workflow: ArcWorkflow) -> workflow.Identifier)
            (fun workflow -> workflow.Datamap)

    /// Syncs static hashes from source ARC to target ARC for matching entities.
    /// This keeps ARCtrl update contract generation scoped to actual changes.
    let syncArcStaticHashes (source: ARC) (target: ARC) : unit =
        target.StaticHash <- source.StaticHash
        WorkflowPayloads.syncWorkflowPayloadBaselines source target

        match source.License, target.License with
        | Some sourceLicense, Some targetLicense -> targetLicense.StaticHash <- sourceLicense.StaticHash
        | _ -> ()

        syncEntityStaticHashes
            target.Studies
            source.TryGetStudy
            (fun (study: ArcStudy) -> study.Identifier)
            (fun sourceStudy targetStudy ->
                targetStudy.StaticHash <- sourceStudy.StaticHash
                syncDataMapStaticHash sourceStudy.Datamap targetStudy.Datamap
            )

        syncEntityStaticHashes
            target.Assays
            source.TryGetAssay
            (fun (assay: ArcAssay) -> assay.Identifier)
            (fun sourceAssay targetAssay ->
                targetAssay.StaticHash <- sourceAssay.StaticHash
                syncDataMapStaticHash sourceAssay.Datamap targetAssay.Datamap
            )

        syncEntityStaticHashes
            target.Workflows
            source.TryGetWorkflow
            (fun (workflow: ArcWorkflow) -> workflow.Identifier)
            (fun sourceWorkflow targetWorkflow ->
                targetWorkflow.StaticHash <- sourceWorkflow.StaticHash
                syncDataMapStaticHash sourceWorkflow.Datamap targetWorkflow.Datamap
            )

        syncEntityStaticHashes
            target.Runs
            source.TryGetRun
            (fun (run: ArcRun) -> run.Identifier)
            (fun sourceRun targetRun ->
                targetRun.StaticHash <- sourceRun.StaticHash
                syncDataMapStaticHash sourceRun.Datamap targetRun.Datamap
            )

    /// The static hashes of an ARC by identifier, so they survive an edit that replaces an entity object.
    type ArcStaticHashes = {
        Arc: int
        License: int option
        Studies: Map<string, int * int option>
        Assays: Map<string, int * int option>
        Workflows: Map<string, int * int option>
        Runs: Map<string, int * int option>
    }

    let private captureEntityHashes
        (entities: seq<'Entity>)
        (getIdentifier: 'Entity -> string)
        (getStaticHash: 'Entity -> int)
        (getDataMap: 'Entity -> Datamap option)
        =
        entities
        |> Seq.map (fun entity ->
            getIdentifier entity, (getStaticHash entity, getDataMap entity |> Option.map (fun dm -> dm.StaticHash))
        )
        |> Map.ofSeq

    let captureArcStaticHashes (arc: ARC) : ArcStaticHashes = {
        Arc = arc.StaticHash
        License = arc.License |> Option.map (fun license -> license.StaticHash)
        Studies =
            captureEntityHashes
                arc.Studies
                (fun (e: ArcStudy) -> e.Identifier)
                (fun e -> e.StaticHash)
                (fun e -> e.Datamap)
        Assays =
            captureEntityHashes
                arc.Assays
                (fun (e: ArcAssay) -> e.Identifier)
                (fun e -> e.StaticHash)
                (fun e -> e.Datamap)
        Workflows =
            captureEntityHashes
                arc.Workflows
                (fun (e: ArcWorkflow) -> e.Identifier)
                (fun e -> e.StaticHash)
                (fun e -> e.Datamap)
        Runs =
            captureEntityHashes arc.Runs (fun (e: ArcRun) -> e.Identifier) (fun e -> e.StaticHash) (fun e -> e.Datamap)
    }

    let private restoreEntityHashes
        (entities: seq<'Entity>)
        (saved: Map<string, int * int option>)
        (getIdentifier: 'Entity -> string)
        (setStaticHash: 'Entity -> int -> unit)
        (getDataMap: 'Entity -> Datamap option)
        =
        for entity in entities do
            match saved |> Map.tryFind (getIdentifier entity) with
            | None -> ()
            | Some(hash, savedDataMapHash) ->
                setStaticHash entity hash

                match getDataMap entity, savedDataMapHash with
                | Some dataMap, Some dataMapHash -> dataMap.StaticHash <- dataMapHash
                | _ -> ()

    /// Restores the hashes captured before GetUpdateContracts moved them. An entity without a captured hash stays as it is.
    let restoreArcStaticHashes (saved: ArcStaticHashes) (arc: ARC) : unit =
        arc.StaticHash <- saved.Arc

        match arc.License, saved.License with
        | Some license, Some hash -> license.StaticHash <- hash
        | _ -> ()

        restoreEntityHashes
            arc.Studies
            saved.Studies
            (fun (e: ArcStudy) -> e.Identifier)
            (fun e hash -> e.StaticHash <- hash)
            (fun e -> e.Datamap)

        restoreEntityHashes
            arc.Assays
            saved.Assays
            (fun (e: ArcAssay) -> e.Identifier)
            (fun e hash -> e.StaticHash <- hash)
            (fun e -> e.Datamap)

        restoreEntityHashes
            arc.Workflows
            saved.Workflows
            (fun (e: ArcWorkflow) -> e.Identifier)
            (fun e hash -> e.StaticHash <- hash)
            (fun e -> e.Datamap)

        restoreEntityHashes
            arc.Runs
            saved.Runs
            (fun (e: ArcRun) -> e.Identifier)
            (fun e hash -> e.StaticHash <- hash)
            (fun e -> e.Datamap)

    /// Copies ARC and preserves static hashes so unchanged entities are not treated as newly created.
    let copyArcPreservingStaticHashes (arc: ARC) : ARC =
        let copiedArc = arc.Copy()

        // License.Copy currently resets a configured path to LICENSE.
        copiedArc.License <-
            arc.License
            |> Option.map (fun license -> License.initFulltext (license.Content, path = license.Path))

        preserveArcDataMapLabels arc copiedArc
        syncArcStaticHashes arc copiedArc
        copiedArc

    type ArcAssay with
        member this.hasInMemoryChanges() =
            hasStaticHashOrDataMapChanges this.StaticHash (this.GetLightHashCode()) this.Datamap

    type ArcStudy with
        member this.hasInMemoryChanges() =
            hasStaticHashOrDataMapChanges this.StaticHash (this.GetLightHashCode()) this.Datamap

    type ArcRun with
        member this.hasInMemoryChanges() =
            hasStaticHashOrDataMapChanges this.StaticHash (this.GetLightHashCode()) this.Datamap
            || WorkflowPayloads.runHasPayloadChanges this

    type ArcWorkflow with
        member this.hasInMemoryChanges() =
            hasStaticHashOrDataMapChanges this.StaticHash (this.GetLightHashCode()) this.Datamap
            || WorkflowPayloads.workflowHasPayloadChanges this

    type ARC with
        member this.hasInMemoryChanges() =
            let mutable isDirty = this.StaticHash <> this.GetLightHashCode()

            if not isDirty then
                for assay in this.Assays do
                    isDirty <- isDirty || assay.hasInMemoryChanges ()

            if not isDirty then
                for study in this.Studies do
                    isDirty <- isDirty || study.hasInMemoryChanges ()

            if not isDirty then
                for run in this.Runs do
                    isDirty <- isDirty || run.hasInMemoryChanges ()

            if not isDirty then
                for workflow in this.Workflows do
                    isDirty <- isDirty || workflow.hasInMemoryChanges ()

            isDirty
