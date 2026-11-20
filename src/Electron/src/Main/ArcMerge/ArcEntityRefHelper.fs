namespace Main.ArcMerge

open ARCtrl
open Swate.Components.Shared

[<AutoOpen>]
module ArcEntityRefExtensions =

    type ArcEntityRef with
        static member fromPath(path: string) : ArcEntityRef =
            match ArcEntityPathRules.tryParseCanonicalArcFileTarget path with
            | Some ArcEntityPathRules.CanonicalArcFileTarget.InvestigationFile -> ArcEntityRef.Investigation
            | Some(ArcEntityPathRules.CanonicalArcFileTarget.EntityFile(zone, identifier)) ->
                match zone with
                | ArcEntityPathRules.AddZone.Assays -> ArcEntityRef.Assay identifier
                | ArcEntityPathRules.AddZone.Studies -> ArcEntityRef.Study identifier
                | ArcEntityPathRules.AddZone.Workflows -> ArcEntityRef.Workflow identifier
                | ArcEntityPathRules.AddZone.Runs -> ArcEntityRef.Run identifier
            | Some(ArcEntityPathRules.CanonicalArcFileTarget.DataMapFile(zone, identifier)) ->
                match zone with
                | ArcEntityPathRules.AddZone.Assays -> ArcEntityRef.AssayDataMap identifier
                | ArcEntityPathRules.AddZone.Studies -> ArcEntityRef.StudyDataMap identifier
                | ArcEntityPathRules.AddZone.Workflows -> ArcEntityRef.WorkflowDataMap identifier
                | ArcEntityPathRules.AddZone.Runs -> ArcEntityRef.RunDataMap identifier
            | None ->
                if PathHelpers.pathMatchesAny ArcEntityPathRules.licenseFileNames path then
                    ArcEntityRef.License
                else
                    ArcEntityRef.Unknown path
