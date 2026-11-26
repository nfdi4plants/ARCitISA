module ElectronCore.NoteConversionTests

open System
open ARCtrl
open Arcitisa.Components.Composite.Notes.Editor
open Arcitisa.Components.Shared
open Vitest

let private mkTag name source accession =
    OntologyAnnotation.create (name = name, tsr = source, tan = accession)

let private expectSome message value =
    match value with
    | Some v -> v
    | None -> failwith message

Vitest.describe (
    "NoteConversion",
    fun () ->
        Vitest.test (
            "formatMarkdown writes YAML frontmatter and preserves body text",
            fun () ->
                let tag =
                    OntologyAnnotation.create (
                        name = "Planning",
                        tsr = "SWATE",
                        tan = "SWATE:0001",
                        comments = ResizeArray([ Comment(name = "description", value = "Planning tag") ])
                    )

                let draft = {
                    NotesDraft.init with
                        Title = " My note "
                        DateCreated = Some(DateTime(2026, 4, 27))
                        Tags = ResizeArray([ tag; mkTag "Review" "SWATE" "SWATE:0002" ])
                        MainText = "Body line 1\n---\nBody line 3"
                }

                let markdown = NoteConversion.formatMarkdown draft

                Vitest.expect(markdown.StartsWith("---\n")).toBe (true)
                Vitest.expect(markdown.Contains("# My note")).toBe (false)
                Vitest.expect(markdown.Contains("Date Created:")).toBe (false)
                Vitest.expect(markdown.Contains("tags:")).toBe (true)

                let frontmatter, body =
                    NoteConversion.tryDecodeMarkdownFrontmatter markdown
                    |> expectSome "Expected YAML frontmatter to decode."

                let tags =
                    frontmatter.Tags |> expectSome "Expected YAML frontmatter tags to decode."

                Vitest.expect(frontmatter.Title).toBe ("My note")
                Vitest.expect(frontmatter.Date).toEqual (DateTime(2026, 4, 27))
                Vitest.expect(tags.Count).toBe (2)
                Vitest.expect(tags.[0].Name).toEqual (Some "Planning")
                Vitest.expect(tags.[0].TermSourceREF).toEqual (Some "SWATE")
                Vitest.expect(tags.[0].TermAccessionNumber).toEqual (Some "SWATE:0001")
                Vitest.expect(tags.[0].Comments.Count).toBe (1)
                Vitest.expect(body.Trim()).toBe ("Body line 1\n---\nBody line 3")
        )

        Vitest.test (
            "frontmatter decoder returns None when tags field is absent",
            fun () ->
                let markdown =
                    """---
title: Untagged note
date: 2026-04-27T00:00:00.0000000
---

Body
"""

                let frontmatter, body =
                    NoteConversion.tryDecodeMarkdownFrontmatter markdown
                    |> expectSome "Expected YAML frontmatter to decode."

                Vitest.expect(frontmatter.Title).toBe ("Untagged note")
                Vitest.expect(frontmatter.Date).toEqual (DateTime(2026, 4, 27))
                Vitest.expect(frontmatter.Tags.IsNone).toBe (true)
                Vitest.expect(body.Trim()).toBe ("Body")
        )

        Vitest.test (
            "formatMarkdown omits tags for drafts without tags",
            fun () ->
                let draft = {
                    NotesDraft.init with
                        Title = "Untagged note"
                        DateCreated = Some(DateTime(2026, 4, 27))
                        MainText = "Body"
                }

                let markdown = NoteConversion.formatMarkdown draft

                Vitest.expect(markdown.Contains("tags:")).toBe (false)

                let frontmatter, _ =
                    NoteConversion.tryDecodeMarkdownFrontmatter markdown
                    |> expectSome "Expected YAML frontmatter to decode."

                Vitest.expect(frontmatter.Tags.IsNone).toBe (true)
        )

        Vitest.test (
            "formatMarkdown rejects drafts without titles",
            fun () ->
                let draft = {
                    NotesDraft.init with
                        Title = "   "
                        DateCreated = Some(DateTime(2026, 4, 27))
                }

                let mutable didThrow = false

                try
                    NoteConversion.formatMarkdown draft |> ignore
                with ex ->
                    didThrow <- true
                    Vitest.expect(ex.Message.Contains("Note title is required.")).toBe (true)

                Vitest.expect(didThrow).toBe (true)
        )

        Vitest.test (
            "note path helpers use dated note folders and protocol folders",
            fun () ->
                let studyTarget: ExistingTargetRef = {
                    Kind = NotesTargetKind.Study
                    Name = "StudyA"
                }

                let assayTarget: ExistingTargetRef = {
                    Kind = NotesTargetKind.Assay
                    Name = "AssayA"
                }

                Vitest
                    .expect(NoteConversion.mkExistingTargetRelativePath studyTarget "Sampling_protocol")
                    .toEqual (Some "studies/StudyA/protocols/Sampling_protocol/Sampling_protocol.md")

                Vitest
                    .expect(NoteConversion.mkExistingTargetRelativePath assayTarget "Extraction_protocol")
                    .toEqual (Some "assays/AssayA/protocols/Extraction_protocol/Extraction_protocol.md")

                Vitest
                    .expect(NoteConversion.mkNewRootNoteRelativePath (DateTime(2026, 6, 15)) "Sampling_protocol")
                    .toEqual (Some "notes/2026-06-15/Sampling_protocol/Sampling_protocol.md")
        )
)
