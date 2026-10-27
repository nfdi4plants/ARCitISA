module Swate.Components.Shared.DataMapCopyWorkaround

open ARCtrl

/// WORKAROUND: ARCtrl 3.0.0-beta.12 DataContext.Copy() omits DataContext.Label.
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
