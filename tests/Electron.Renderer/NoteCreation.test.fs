module ElectronRenderer.NoteCreationTests

open System
open Renderer.Components.Helper.NoteCreation
open Swate.Components.Shared
open Swate.Components.Composite.Notes.Editor
open Swate.Electron.Shared.FileIOTypes
open Vitest

let private date = DateTime(2026, 11, 19)

/// Records the calls of a note create. pathExists answers with exists.
type private FakeApi(exists: bool, ?writeError: string) =
    member val Written = ResizeArray<FileContentDTO>()
    member val Created = ResizeArray<CreateFileSystemItemRequest>()

    member this.Api: NoteCreationApi = {
        pathExists = fun _ -> promise { return Ok exists }
        writeFile =
            fun request -> promise {
                this.Written.Add request

                match writeError with
                | Some message -> return Error(exn message)
                | None -> return Ok()
            }
        createFileSystemItem =
            fun request -> promise {
                this.Created.Add request
                return Ok $"{request.parentPath}/{request.name}"
            }
    }

let private study name =
    NoteLocation.Entity {
        Name = name
        Kind = NotesTargetKind.Study
    }

let private assay name =
    NoteLocation.Entity {
        Name = name
        Kind = NotesTargetKind.Assay
    }

Vitest.describe (
    "NoteCreation",
    fun () ->
        Vitest.test (
            "a note on a study or assay folder, or on a folder inside one, goes to that entity",
            fun () ->
                Vitest.expect(locationOf "studies/S1").toEqual (study "S1")
                Vitest.expect(locationOf "assays/A1/dataset/raw").toEqual (assay "A1")
        )

        Vitest.test (
            "a note anywhere else goes to the notes folder",
            fun () ->
                for folder in
                    [
                        ""
                        "notes"
                        "studies"
                        "assays/"
                        "workflows/W1"
                        "docs"
                    ] do
                    Vitest.expect(locationOf folder).toEqual NoteLocation.NotesFolder
        )

        Vitest.test (
            "validateTitle trims the title and refuses one without a letter or digit",
            fun () ->
                Vitest.expect(validateTitle "  My first note!  ").toEqual (Ok "My first note!")
                Vitest.expect(Result.isError (validateTitle "   ")).toBe true
                Vitest.expect(Result.isError (validateTitle "!!!")).toBe true
        )

        Vitest.test (
            "notePath puts the note in a dated folder or in the protocols folder of the entity",
            fun () ->
                Vitest
                    .expect(notePath NoteLocation.NotesFolder date "My first note!")
                    .toEqual (Ok "notes/2026-11-19/My_first_note/My_first_note.md")

                Vitest
                    .expect(notePath (study "S1") date "My first note!")
                    .toEqual (Ok "studies/S1/protocols/My_first_note/My_first_note.md")
        )

        Vitest.test (
            "noteContent holds front matter that reads back as the title and date",
            fun () ->
                match NoteConversion.tryDecodeMarkdownFrontmatter (noteContent "My first note!" date) with
                | Some(frontmatter, body) ->
                    Vitest.expect(frontmatter.Title).toBe "My first note!"
                    Vitest.expect(frontmatter.Date.ToString "yyyy-MM-dd").toBe "2026-11-19"
                    Vitest.expect(body.Trim()).toBe ""
                | None -> failwith "Expected front matter in a new note."
        )

        Vitest.test (
            "create writes the note with its assets folder and returns its path",
            fun () -> promise {
                let fake = FakeApi(exists = false)
                let! result = create fake.Api (assay "A1") date "Lab day"

                Vitest.expect(result |> Result.mapError _.Message).toEqual (Ok "assays/A1/protocols/Lab_day/Lab_day.md")
                Vitest.expect(fake.Written.Count).toBe 1
                Vitest.expect(fake.Written.[0].path).toBe "assays/A1/protocols/Lab_day/Lab_day.md"
                Vitest.expect(fake.Written.[0].content).toBe (noteContent "Lab day" date)
                Vitest.expect(fake.Created.Count).toBe 1
                Vitest.expect(fake.Created.[0].parentPath).toBe "assays/A1/protocols/Lab_day"
                Vitest.expect(fake.Created.[0].name).toBe NoteConversion.noteAssetsFolderName
            }
        )

        Vitest.test (
            "create refuses a note that exists and writes nothing",
            fun () -> promise {
                let fake = FakeApi(exists = true)
                let! result = create fake.Api NoteLocation.NotesFolder date "Lab day"

                match result with
                | Error error -> Vitest.expect(error.Message).toContain "notes/2026-11-19/Lab_day/Lab_day.md"
                | Ok path -> failwith $"Expected a refusal, got {path}."

                Vitest.expect(fake.Written.Count).toBe 0
            }
        )

        Vitest.test (
            "create reports a failed write",
            fun () -> promise {
                let fake = FakeApi(exists = false, writeError = "disk full")
                let! result = create fake.Api NoteLocation.NotesFolder date "Lab day"

                match result with
                | Error error -> Vitest.expect(error.Message).toContain "disk full"
                | Ok path -> failwith $"Expected a failure, got {path}."
            }
        )
)
