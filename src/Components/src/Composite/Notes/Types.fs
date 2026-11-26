module Arcitisa.Components.Composite.Notes.Types

open ARCtrl

type Note = {
    RelativePath: string
    Title: string
    Date: System.DateTime
    Tags: ResizeArray<OntologyAnnotation> option
    Content: string
}
