module ElectronCore.EnsureNotesFolderTests

open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.Notes.NoteConstants
open Main.Notes.NoteScaffolding
open Vitest

let private withTempArcPath (testBody: string -> JS.Promise<unit>) = promise {
    let! rootPath = TestHelpers.createTempDirectoryAsync "swate-notes-folder-"

    try
        let arcPath = join [| rootPath; "arc" |]
        let! _ = mkdirAsync arcPath (MkdirOptions(recursive = true))
        do! testBody arcPath
        do! TestHelpers.removeDirectoryAsync rootPath
    with error ->
        do! TestHelpers.removeDirectoryAsync rootPath
        return raise error
}

let private assertEnsureSucceeded (result: Result<unit, exn>) =
    match result with
    | Ok() -> ()
    | Error error -> failwith error.Message

Vitest.describe (
    "ensureNotesFolderAtArcPath",
    fun () ->
        Vitest.test (
            "does no filesystem work when disabled",
            fun () ->
                withTempArcPath (fun arcPath -> promise {
                    let missingArcPath = join [| arcPath; "missing-arc" |]
                    let! result = ensureNotesFolderAtArcPath missingArcPath false
                    assertEnsureSucceeded result
                    let! exists = TestHelpers.pathExistsAsync missingArcPath
                    Vitest.expect(exists).toBe false

                    // Disabled scaffolding also ignores paths that cannot be directories.
                    let filePath = join [| arcPath; "file.txt" |]
                    do! writeFileAsync filePath "existing" TextEncoding.Utf8
                    let! fileResult = ensureNotesFolderAtArcPath filePath false
                    assertEnsureSucceeded fileResult
                    let! content = readFileAsync filePath TextEncoding.Utf8
                    Vitest.expect(content).toBe "existing"

                    let! emptyPathResult = ensureNotesFolderAtArcPath "" false
                    assertEnsureSucceeded emptyPathResult
                })
        )

        Vitest.test (
            "creates notes folder and README when enabled",
            fun () ->
                withTempArcPath (fun arcPath -> promise {
                    let notesFolderPath = join [| arcPath; NotesRootFolderName |]
                    let notesReadmePath = join [| notesFolderPath; NotesReadmeFileName |]
                    let! result = ensureNotesFolderAtArcPath arcPath true
                    assertEnsureSucceeded result

                    let! notesFolder = statAsync notesFolderPath
                    let! readmeContent = readFileAsync notesReadmePath TextEncoding.Utf8
                    Vitest.expect(notesFolder.isDirectory ()).toBe true
                    Vitest.expect(readmeContent).toBe NotesReadmeContent
                })
        )

        Vitest.test (
            "fills the missing README in an existing notes folder",
            fun () ->
                withTempArcPath (fun arcPath -> promise {
                    let notesFolderPath = join [| arcPath; NotesRootFolderName |]
                    let notesReadmePath = join [| notesFolderPath; NotesReadmeFileName |]
                    let! _ = mkdirAsync notesFolderPath (MkdirOptions(recursive = true))

                    let! result = ensureNotesFolderAtArcPath arcPath true
                    assertEnsureSucceeded result
                    let! readmeContent = readFileAsync notesReadmePath TextEncoding.Utf8
                    Vitest.expect(readmeContent).toBe NotesReadmeContent
                })
        )

        Vitest.test (
            "preserves an existing README on repeated and concurrent calls",
            fun () ->
                withTempArcPath (fun arcPath -> promise {
                    let notesFolderPath = join [| arcPath; NotesRootFolderName |]
                    let notesReadmePath = join [| notesFolderPath; NotesReadmeFileName |]
                    let! _ = mkdirAsync notesFolderPath (MkdirOptions(recursive = true))
                    let customContent = "custom readme content"
                    do! writeFileAsync notesReadmePath customContent TextEncoding.Utf8

                    let! firstResult = ensureNotesFolderAtArcPath arcPath true
                    assertEnsureSucceeded firstResult

                    let! concurrentResults =
                        [|
                            ensureNotesFolderAtArcPath arcPath true
                            ensureNotesFolderAtArcPath arcPath true
                        |]
                        |> Promise.all

                    concurrentResults |> Array.iter assertEnsureSucceeded
                    let! finalContent = readFileAsync notesReadmePath TextEncoding.Utf8
                    Vitest.expect(finalContent).toBe customContent
                })
        )

        Vitest.test (
            "returns filesystem failures when enabled",
            fun () ->
                withTempArcPath (fun arcPath -> promise {
                    let filePath = join [| arcPath; "file.txt" |]
                    do! writeFileAsync filePath "existing" TextEncoding.Utf8

                    match! ensureNotesFolderAtArcPath filePath true with
                    | Ok() -> failwith "Expected notes creation beneath a file to fail."
                    | Error error -> Vitest.expect(error.Message.Length > 0).toBe true

                    let! content = readFileAsync filePath TextEncoding.Utf8
                    Vitest.expect(content).toBe "existing"
                })
        )
)
