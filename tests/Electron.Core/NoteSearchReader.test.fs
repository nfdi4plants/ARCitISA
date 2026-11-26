module ElectronCore.NoteSearchReaderTests

open Fable.Core
open Fable.Core.JsInterop
open Main.NoteSearchReader
open Arcitisa.Electron.Shared.FileIOTypes
open Vitest

let private fsPromisesDynamic: obj = importAll "fs/promises"
let private osDynamic: obj = importAll "os"
let private pathDynamic: obj = importAll "path"

let private noteMarkdown =
    """---
title: My note
date: 2026-04-27T00:00:00.0000000
tags:
  -
    annotationValue: Planning
    termSource: SWATE
    termAccession: SWATE:0001
  -
    annotationValue: Review
    termSource: SWATE
    termAccession: SWATE:0002
---

Body line 1
---
Body line 3
"""

let private markdownWithoutFrontmatter =
    """# Plain note

Date Created: 27_04_2026
Tags: Planning

---

Plain body
"""

let private noTagsMarkdown =
    """---
title: Untagged note
date: 2026-04-27T00:00:00.0000000
---

Body
"""

Vitest.describe (
    "NoteSearchReader.readNotes",
    fun () ->
        Vitest.test (
            "decodes YAML frontmatter into note metadata and preserves body text",
            fun () -> promise {
                let tmpDir = osDynamic?tmpdir () |> unbox<string>

                let! repoRoot =
                    fsPromisesDynamic?mkdtemp (pathDynamic?join (tmpDir, "arcitisa-notes-"))
                    |> unbox<JS.Promise<string>>

                let notesDir =
                    pathDynamic?join (repoRoot, "notes", "2026-04-27", "my_note") |> unbox<string>

                let notePath = pathDynamic?join (notesDir, "my_note.md") |> unbox<string>

                let! _ =
                    fsPromisesDynamic?mkdir (notesDir, createObj [ "recursive" ==> true ])
                    |> unbox<JS.Promise<obj>>

                let! _ =
                    fsPromisesDynamic?writeFile (notePath, noteMarkdown, "utf8")
                    |> unbox<JS.Promise<unit>>

                let! notes = readNotes repoRoot [| FileEntry.create ("my_note.md", notePath, false) |]

                Vitest.expect(notes.Length).toBe (1)
                let note = notes.[0]
                Vitest.expect(note.RelativePath).toBe ("notes/2026-04-27/my_note/my_note.md")
                Vitest.expect(note.Title).toBe ("My note")
                Vitest.expect(note.Date.Year).toBe (2026)
                Vitest.expect(note.Date.Month).toBe (4)
                Vitest.expect(note.Date.Day).toBe (27)
                Vitest.expect(note.Content).toBe ("Body line 1\n---\nBody line 3")

                match note.Tags with
                | Some tags ->
                    Vitest.expect(tags.Count).toBe (2)
                    Vitest.expect(tags.[0].Name).toEqual (Some "Planning")
                    Vitest.expect(tags.[0].TermSourceREF).toEqual (Some "SWATE")
                    Vitest.expect(tags.[0].TermAccessionNumber).toEqual (Some "SWATE:0001")
                | None -> failwith "Expected tags from YAML frontmatter."
            }
        )

        Vitest.test (
            "skips note files without YAML frontmatter",
            fun () -> promise {
                let tmpDir = osDynamic?tmpdir () |> unbox<string>

                let! repoRoot =
                    fsPromisesDynamic?mkdtemp (pathDynamic?join (tmpDir, "arcitisa-notes-"))
                    |> unbox<JS.Promise<string>>

                let notesDir =
                    pathDynamic?join (repoRoot, "notes", "2026-04-27", "plain_note")
                    |> unbox<string>

                let notePath = pathDynamic?join (notesDir, "plain_note.md") |> unbox<string>

                let! _ =
                    fsPromisesDynamic?mkdir (notesDir, createObj [ "recursive" ==> true ])
                    |> unbox<JS.Promise<obj>>

                let! _ =
                    fsPromisesDynamic?writeFile (notePath, markdownWithoutFrontmatter, "utf8")
                    |> unbox<JS.Promise<unit>>

                let! notes = readNotes repoRoot [| FileEntry.create ("plain_note.md", notePath, false) |]

                Vitest.expect(notes.Length).toBe (0)
            }
        )

        Vitest.test (
            "keeps absent YAML tag data as None",
            fun () -> promise {
                let tmpDir = osDynamic?tmpdir () |> unbox<string>

                let! repoRoot =
                    fsPromisesDynamic?mkdtemp (pathDynamic?join (tmpDir, "arcitisa-notes-"))
                    |> unbox<JS.Promise<string>>

                let notesDir =
                    pathDynamic?join (repoRoot, "notes", "2026-04-27", "untagged_note")
                    |> unbox<string>

                let notePath = pathDynamic?join (notesDir, "untagged_note.md") |> unbox<string>

                let! _ =
                    fsPromisesDynamic?mkdir (notesDir, createObj [ "recursive" ==> true ])
                    |> unbox<JS.Promise<obj>>

                let! _ =
                    fsPromisesDynamic?writeFile (notePath, noTagsMarkdown, "utf8")
                    |> unbox<JS.Promise<unit>>

                let! notes = readNotes repoRoot [| FileEntry.create ("untagged_note.md", notePath, false) |]

                Vitest.expect(notes.Length).toBe (1)

                Vitest.expect(notes.[0].Tags.IsNone).toBe (true)
            }
        )
)
