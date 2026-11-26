namespace Arcitisa.Components.Shared


open ARCtrl

[<RequireQualifiedAccess>]
type NotesTargetKind =
    | Study
    | Assay

type ExistingTargetRef = { Name: string; Kind: NotesTargetKind }

type NoteSearch = {
    RelativePath: string
    Title: string
    Date: System.DateTime
    Tags: ResizeArray<OntologyAnnotation> option
    Content: string
}

type ARCPointer = {
    name: string
    path: string
    isActive: bool
} with

    static member create(name: string, path: string, isActive: bool) = {
        name = name
        path = path
        isActive = isActive
    }

type ImportedTextFile = { Name: string; Content: string }
