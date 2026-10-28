module Swate.Components.Shared.DataMapCopyWorkaround

open ARCtrl

/// WORKAROUND: ARCtrl 3.2.3 DataContext.Copy() omits DataContext.Label.
/// Remove this module after upgrading to an ARCtrl version that preserves labels.
let preserveLabels (source: Datamap) (target: Datamap) =
    Seq.iter2
        (fun (source: DataContext) (target: DataContext) -> target.Label <- source.Label)
        source.DataContexts
        target.DataContexts

/// WORKAROUND: Uses ARCtrl's copy and repairs labels omitted by DataContext.Copy().
/// Remove this module after upgrading to an ARCtrl version that preserves labels.
let copy (source: Datamap) =
    let target = source.Copy()
    preserveLabels source target
    target

/// Copies an investigation and repairs labels in all nested datamaps.
let copyInvestigation (source: ArcInvestigation) =
    let target = source.Copy()

    let preserveNestedLabels getDatamap source target =
        Seq.iter2
            (fun source target ->
                match getDatamap source, getDatamap target with
                | Some source, Some target -> preserveLabels source target
                | _ -> ()
            )
            source
            target

    preserveNestedLabels (fun (assay: ArcAssay) -> assay.Datamap) source.Assays target.Assays
    preserveNestedLabels (fun (study: ArcStudy) -> study.Datamap) source.Studies target.Studies
    preserveNestedLabels (fun (run: ArcRun) -> run.Datamap) source.Runs target.Runs
    preserveNestedLabels (fun (workflow: ArcWorkflow) -> workflow.Datamap) source.Workflows target.Workflows
    target
