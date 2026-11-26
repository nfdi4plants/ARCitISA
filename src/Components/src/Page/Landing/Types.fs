namespace Arcitisa.Components.Page.Landing

open ARCtrl
open Fable.Core
open Arcitisa.Components.Shared


[<RequireQualifiedAccess>]
type LandingTarget =
    | Study
    | Assay

type LandingDraft = {
    Identifier: string
    Title: string
    Description: string
    InvolvedPeople: ResizeArray<Person>
    Comments: ResizeArray<Comment>
    MainText: string
    Files: string list
    Publications: ResizeArray<Publication>
    SubmissionDate: string
    PublicReleaseDate: string
    StudyDesignDescriptors: ResizeArray<OntologyAnnotation>
    MeasurementType: OntologyAnnotation option
    TechnologyType: OntologyAnnotation option
    TechnologyPlatform: OntologyAnnotation option
} with

    static member init = {
        Identifier = ""
        Title = ""
        Description = ""
        InvolvedPeople = ResizeArray()
        Comments = ResizeArray()
        MainText = ""
        Files = []
        Publications = ResizeArray()
        SubmissionDate = ""
        PublicReleaseDate = ""
        StudyDesignDescriptors = ResizeArray()
        MeasurementType = None
        TechnologyType = None
        TechnologyPlatform = None
    }

type LandingUiState = {
    ShowQuestions: bool
    SelectedTarget: LandingTarget option
    Error: string option
    IsSubmitting: bool
} with

    static member init = {
        ShowQuestions = false
        SelectedTarget = None
        Error = None
        IsSubmitting = false
    }

type ProtocolIntent = {
    RelativePath: string
    Content: string
}

type SubmitPayload = {
    ArcFile: ArcFiles
    ProtocolIntent: ProtocolIntent option
    Target: LandingTarget
    Identifier: string
}

[<Mangle(false)>]
module Exports =
    let createLandingDraft () = LandingDraft.init
    let createLandingUiState () = LandingUiState.init
