module Arcitisa.Electron.Shared.DTOs.NoteSearchDto

open ARCtrl
open ARCtrl.Json
open Arcitisa.Components.Composite.Notes.Types

type NoteSearchDto = {
    RelativePath: string
    Title: string
    Date: System.DateTime
    Tags: string[] option
    Content: string
}

[<RequireQualifiedAccess>]
module NoteSearchNoteDto =

    let private encodeTag (tag: OntologyAnnotation) : string =
        tag
        |> ARCtrl.Json.OntologyAnnotation.encoder
        |> Encode.toJsonString (Encode.defaultSpaces (Some 0))

    let private tryDecodeTag (tagJson: string) : OntologyAnnotation option =
        if System.String.IsNullOrWhiteSpace tagJson then
            None
        else
            try
                Some(Decode.fromJsonString ARCtrl.Json.OntologyAnnotation.decoder tagJson)
            with _ ->
                None

    let ofNote (note: Note) : NoteSearchDto = {
        RelativePath = note.RelativePath
        Title = note.Title
        Date = note.Date
        Tags = note.Tags |> Option.map (Seq.map encodeTag >> Seq.toArray)
        Content = note.Content
    }

    let toNote (dto: NoteSearchDto) : Note = {
        RelativePath = dto.RelativePath
        Title = dto.Title
        Date = dto.Date
        Tags =
            dto.Tags
            |> Option.map (fun tags -> tags |> Array.choose tryDecodeTag |> ResizeArray)
        Content = dto.Content
    }
