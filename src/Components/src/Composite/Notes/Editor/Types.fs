namespace Arcitisa.Components.Composite.Notes.Editor

open ARCtrl
open Fable.Core
open Arcitisa.Components.Shared

[<RequireQualifiedAccess>]
type NotesTarget =
    | ExistingTarget of ExistingTargetRef
    | NewRootNote

type NotesDraft = {
    Title: string
    DateCreated: System.DateTime option
    Tags: ResizeArray<OntologyAnnotation>
    MainText: string
    SelectedExistingTarget: ExistingTargetRef option
} with

    static member init = {
        Title = ""
        DateCreated = Some System.DateTime.Today
        Tags = ResizeArray()
        MainText = ""
        SelectedExistingTarget = None
    }

type NotesUiState = {
    Error: string option
    IsSubmitting: bool
    ShowExistingTargetSelector: bool
} with

    static member init = {
        Error = None
        IsSubmitting = false
        ShowExistingTargetSelector = false
    }

type NotesProtocolIntent = {
    RelativePath: string
    Content: string
    Target: NotesTarget
}

type NotesSubmitPayload = {
    Intent: NotesProtocolIntent
    Title: string
    DateCreated: System.DateTime
    Tags: OntologyAnnotation list
}

[<Mangle(false)>]
module Exports =
    let createNotesDraft () = NotesDraft.init
    let createNotesUiState () = NotesUiState.init

    let createDemoExistingTargets () =
        ResizeArray [
            {
                Name = "MyStudy"
                Kind = NotesTargetKind.Study
            }
            {
                Name = "MyAssay"
                Kind = NotesTargetKind.Assay
            }
        ]
