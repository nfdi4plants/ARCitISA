module ARC

open ARCtrl
open ARCtrl.ArcPathHelper
open ARCtrl.Contract
open ARCtrl.Helper
open Arcitisa.Components.Shared

type ARC with

    member this.TryArcFileByPath(path: string) =
        let normalizedPath = path |> PathHelpers.normalizeCanonicalRelativePath
        let splitPath = split normalizedPath

        match splitPath with
        | InvestigationPath _ -> ArcFiles.Investigation this |> Some
        | AssayPath p ->
            let identifier = (Identifier.Assay.identifierFromFileName p)
            let assay = this.TryGetAssay identifier
            assay |> Option.map ArcFiles.Assay
        | StudyPath p ->
            let identifier = (Identifier.Study.identifierFromFileName p)
            let study = this.TryGetStudy identifier

            study
            |> Option.map (fun s ->
                let assignedAssays =
                    s.RegisteredAssayIdentifiers |> Seq.choose this.TryGetAssay |> List.ofSeq

                ArcFiles.Study(s, assignedAssays)
            )
        | WorkflowPath p ->
            let identifier = (Identifier.Workflow.identifierFromFileName p)
            let workflow = this.TryGetWorkflow identifier
            workflow |> Option.map ArcFiles.Workflow
        | RunPath p ->
            let identifier = (Identifier.Run.identifierFromFileName p)
            let run = this.TryGetRun identifier
            run |> Option.map ArcFiles.Run
        | DatamapPath _ ->
            DatamapParentInfo.tryFromPath normalizedPath
            |> Option.bind (fun parentInfo ->
                this.TryGetDataMap parentInfo
                |> Option.map (fun dataMap -> ArcFiles.DataMap(Some parentInfo, dataMap))
            )
        | _ -> None
