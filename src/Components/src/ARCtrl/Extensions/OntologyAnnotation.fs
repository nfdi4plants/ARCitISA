[<AutoOpen>]
module ARCtrl.OntologyAnnotationExtensions

open ARCtrl
open Arcitisa.Components

module OntologyAnnotationHelper =

    [<Literal>]
    let DescriptionCommentKey = "description"

    [<Literal>]
    let IsObsoleteCommentKey = "isObsolete"

type OntologyAnnotation with
    static member empty() = OntologyAnnotation.create ()

    static member from(term: Shared.Database.Term) =
        let comments =
            ResizeArray [
                if System.String.IsNullOrWhiteSpace term.Description |> not then
                    Comment(OntologyAnnotationHelper.DescriptionCommentKey, term.Description)
                if term.IsObsolete then
                    Comment(OntologyAnnotationHelper.IsObsoleteCommentKey, term.IsObsolete.ToString())
            ]

        OntologyAnnotation(term.Name, term.FK_Ontology, term.Accession, comments)
