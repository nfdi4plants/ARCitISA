module Main.Notes.NoteScaffolding

open System
open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.Notes.NoteConstants
open Fable.Core.JsInterop

/// Ensures ARC notes scaffolding exists for the provided ARC root path.
let ensureNotesFolderAtArcPath (arcPath: string) (enabled: bool) : JS.Promise<Result<unit, exn>> = promise {
    try
        if not enabled then
            return Ok()
        elif String.IsNullOrWhiteSpace arcPath then
            return Error(exn "ARC path must not be empty.")
        else
            let resolvedArcPath = resolve [| arcPath |]
            let notesFolderPath = join [| resolvedArcPath; NotesRootFolderName |]
            let notesReadmePath = join [| notesFolderPath; NotesReadmeFileName |]

            let! result = mkdirAsync notesFolderPath (MkdirOptions(recursive = true))
            let notesFolderWasCreated = not (isNullOrUndefined result)

            if notesFolderWasCreated then
                let! _ = createTextFileExclusiveAsync notesReadmePath NotesReadmeContent
                ()
            else
                ()

            return Ok()
    with error ->
        return Error error
}
