module ElectronRenderer.NoteSearchInteropTests

open System
open ARCtrl
open ARCtrl.Json
open Arcitisa.Components.Composite.Notes.Types
open Arcitisa.Electron.Shared.DTOs.NoteSearchDto
open Vitest

let private buildNoteDto (tags: string[] option) : NoteSearchDto = {
    RelativePath = "notes/test-note.md"
    Title = "Test note"
    Date = DateTime(2026, 4, 27)
    Tags = tags
    Content = "Body"
}

let private serializeTag (tag: OntologyAnnotation) = OntologyAnnotation.toJsonString 0 tag

let private expectTag
    (tag: OntologyAnnotation)
    (expectedName: string)
    (expectedSource: string option)
    (expectedAccession: string option)
    =
    Vitest.expect(tag.NameText).toBe (expectedName)
    Vitest.expect(tag.TermSourceREF).toEqual (expectedSource)
    Vitest.expect(tag.TermAccessionNumber).toEqual (expectedAccession)

let private expectTagsCount (count: int) (note: Note) =
    match note.Tags with
    | Some tags -> Vitest.expect(tags.Count).toBe (count)
    | None -> failwith "Expected tags to be present."

Vitest.describe (
    "NoteSearchInterop.toDomainNote",
    fun () ->
        Vitest.test (
            "deserializes valid serialized ontology tags",
            fun () ->
                let dto =
                    buildNoteDto (
                        Some [|
                            OntologyAnnotation.create (name = "Planning", tsr = "SWATE", tan = "SWATE:0001")
                            |> serializeTag
                            OntologyAnnotation.create (name = "Execution", tsr = "MS", tan = "MS:1000121")
                            |> serializeTag
                        |]
                    )

                let note = NoteSearchNoteDto.toNote dto

                match note.Tags with
                | Some tags ->
                    Vitest.expect(tags.Count).toBe (2)
                    expectTag tags.[0] "Planning" (Some "SWATE") (Some "SWATE:0001")
                    expectTag tags.[1] "Execution" (Some "MS") (Some "MS:1000121")
                | None -> failwith "Expected note tags to be present."
        )

        Vitest.test (
            "drops undecodable serialized tags while preserving decodable order",
            fun () ->
                let dto =
                    buildNoteDto (
                        Some [|
                            OntologyAnnotation.create (name = "Planning", tsr = "SRC1", tan = "ACC1")
                            |> serializeTag
                            "{ \"annotationValue\": 42 }"
                            ""
                            "not-json"
                            OntologyAnnotation.create (name = "Review", tsr = "SRC2", tan = "ACC2")
                            |> serializeTag
                        |]
                    )

                let note = NoteSearchNoteDto.toNote dto

                match note.Tags with
                | Some tags ->
                    Vitest.expect(tags.Count).toBe (3)
                    expectTag tags.[0] "Planning" (Some "SRC1") (Some "ACC1")
                    expectTag tags.[1] "42" None None
                    expectTag tags.[2] "Review" (Some "SRC2") (Some "ACC2")
                | None -> failwith "Expected note tags to be present."
        )

        Vitest.test (
            "preserves None for missing tags",
            fun () ->
                let dto = buildNoteDto None
                let note = NoteSearchNoteDto.toNote dto
                Vitest.expect(note.Tags.IsNone).toBe (true)
        )

        Vitest.test (
            "preserves Some [] when all serialized tags are invalid",
            fun () ->
                let dto = buildNoteDto (Some [| ""; "   "; "invalid-json" |])
                let note = NoteSearchNoteDto.toNote dto
                expectTagsCount 0 note
        )

        Vitest.test (
            "preserves Some [] for empty tag arrays",
            fun () ->
                let dto = buildNoteDto (Some [||])
                let note = NoteSearchNoteDto.toNote dto
                expectTagsCount 0 note
        )
)

Vitest.describe (
    "NoteSearchInterop.fromDomainNote",
    fun () ->
        Vitest.test (
            "round-trips note DTO metadata and ontology tags",
            fun () ->
                let note: Note = {
                    RelativePath = "notes/2026-04-27/test-note/test-note.md"
                    Title = "Test note"
                    Date = DateTime(2026, 4, 27)
                    Tags =
                        Some(
                            ResizeArray [
                                OntologyAnnotation.create (name = "Planning", tsr = "SWATE", tan = "SWATE:0001")
                                OntologyAnnotation.create (name = "Review", tsr = "SWATE", tan = "SWATE:0002")
                            ]
                        )
                    Content = "Body"
                }

                let dto = NoteSearchNoteDto.ofNote note

                Vitest.expect(dto.RelativePath).toBe (note.RelativePath)
                Vitest.expect(dto.Title).toBe (note.Title)
                Vitest.expect(dto.Date.Year).toBe (2026)
                Vitest.expect(dto.Date.Month).toBe (4)
                Vitest.expect(dto.Date.Day).toBe (27)
                Vitest.expect(dto.Content).toBe (note.Content)

                match dto.Tags with
                | Some tags -> Vitest.expect(tags.Length).toBe (2)
                | None -> failwith "Expected DTO tags to be present."

                let roundTripped = NoteSearchNoteDto.toNote dto

                Vitest.expect(roundTripped.RelativePath).toBe (note.RelativePath)
                Vitest.expect(roundTripped.Title).toBe (note.Title)
                Vitest.expect(roundTripped.Date.Year).toBe (2026)
                Vitest.expect(roundTripped.Date.Month).toBe (4)
                Vitest.expect(roundTripped.Date.Day).toBe (27)
                Vitest.expect(roundTripped.Content).toBe (note.Content)

                match roundTripped.Tags with
                | Some tags ->
                    Vitest.expect(tags.Count).toBe (2)
                    expectTag tags.[0] "Planning" (Some "SWATE") (Some "SWATE:0001")
                    expectTag tags.[1] "Review" (Some "SWATE") (Some "SWATE:0002")
                | None -> failwith "Expected note tags to be present."
        )
)
