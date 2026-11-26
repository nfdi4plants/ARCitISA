namespace Arcitisa.Components.Page.Landing

open ARCtrl
open Arcitisa.Components.Shared


[<RequireQualifiedAccess>]
module Conversion =

    let private applyCommonFieldsToStudy (draft: LandingDraft) (study: ArcStudy) =
        study.Title <- Some(draft.Title.Trim())
        study.Description <- Some(draft.Description.Trim())
        study.Contacts <- ResizeArray draft.InvolvedPeople
        study.Comments <- ResizeArray draft.Comments

    let private applyCommonFieldsToAssay (draft: LandingDraft) (assay: ArcAssay) =
        assay.Title <- Some(draft.Title.Trim())
        assay.Description <- Some(draft.Description.Trim())
        assay.Performers <- ResizeArray draft.InvolvedPeople
        assay.Comments <- ResizeArray draft.Comments

    let private toStudy (draft: LandingDraft) (identifier: string) =
        let study =
            match ArcFileDefaults.createDefaultArcFile ArcFilesDiscriminate.Study identifier with
            | ArcFiles.Study(study, _) -> study
            | _ -> failwith "Expected default study ArcFile."

        applyCommonFieldsToStudy draft study
        study.Publications <- ResizeArray draft.Publications
        study.SubmissionDate <- Validation.toOptionalString draft.SubmissionDate
        study.PublicReleaseDate <- Validation.toOptionalString draft.PublicReleaseDate
        study.StudyDesignDescriptors <- ResizeArray draft.StudyDesignDescriptors

        study

    let private toAssay (draft: LandingDraft) (identifier: string) =
        let assay =
            match ArcFileDefaults.createDefaultArcFile ArcFilesDiscriminate.Assay identifier with
            | ArcFiles.Assay assay -> assay
            | _ -> failwith "Expected default assay ArcFile."

        applyCommonFieldsToAssay draft assay
        assay.MeasurementType <- draft.MeasurementType
        assay.TechnologyType <- draft.TechnologyType
        assay.TechnologyPlatform <- draft.TechnologyPlatform

        assay

    let private toProtocolIntent (draft: LandingDraft) (target: LandingTarget) (identifier: string) =
        if PathHelpers.isSafePathSegment identifier |> not then
            None
        else
            match Validation.toOptionalString draft.MainText with
            | None -> None
            | Some content ->
                let folder =
                    match target with
                    | LandingTarget.Study -> "studies"
                    | LandingTarget.Assay -> "assays"

                Some {
                    RelativePath = $"{folder}/{identifier}/protocols/{identifier}_protocol.md"
                    Content = content
                }

    let toArcFile (draft: LandingDraft) (target: LandingTarget) =
        let identifier = Validation.resolveIdentifier draft

        let arcFile =
            match target with
            | LandingTarget.Study ->
                let study = toStudy draft identifier
                ArcFiles.Study(study, [])
            | LandingTarget.Assay ->
                let assay = toAssay draft identifier
                ArcFiles.Assay assay

        identifier, arcFile

    let toSubmitPayload (draft: LandingDraft) (target: LandingTarget) =
        let identifier, arcFile = toArcFile draft target
        let protocolIntent = toProtocolIntent draft target identifier

        {
            ArcFile = arcFile
            ProtocolIntent = protocolIntent
            Target = target
            Identifier = identifier
        }
