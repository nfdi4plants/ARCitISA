/// Creates a note file from a title. A note on a study or assay goes to the protocols folder of that entity, every
/// other note to notes/<date>/. The file opens in the text editor afterwards, where the user writes the note.
module Renderer.Components.Helper.NoteCreation

open System
open Fable.Core
open Swate.Components.Shared
open Swate.Components.Composite.Notes.Editor
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Renderer.Components.Helper.FileSystemHelper

/// Where a new note goes.
[<RequireQualifiedAccess>]
type NoteLocation =
    /// notes/<date>/<name>/<name>.md
    | NotesFolder
    /// <zone>/<entity>/protocols/<name>/<name>.md
    | Entity of ExistingTargetRef

/// The location for a note asked for on folderPath. A study or assay folder, or any folder inside one, takes the note
/// into its protocols folder. "" is the ARC root.
let locationOf (folderPath: string) : NoteLocation =
    match
        (PathHelpers.normalizeCanonicalRelativePath folderPath).Split('/')
        |> List.ofArray
    with
    | zone :: name :: _ when zone = ARCtrl.ArcPathHelper.StudiesFolderName && name <> "" ->
        NoteLocation.Entity {
            Name = name
            Kind = NotesTargetKind.Study
        }
    | zone :: name :: _ when zone = ARCtrl.ArcPathHelper.AssaysFolderName && name <> "" ->
        NoteLocation.Entity {
            Name = name
            Kind = NotesTargetKind.Assay
        }
    | _ -> NoteLocation.NotesFolder

/// Where the note goes, in words for the dialog.
let describe (location: NoteLocation) (date: DateTime) : string =
    match location with
    | NoteLocation.NotesFolder -> $"The note is saved in notes/{NoteConversion.formatDateFolder date}/."
    | NoteLocation.Entity target ->
        let kind =
            match target.Kind with
            | NotesTargetKind.Study -> "study"
            | NotesTargetKind.Assay -> "assay"

        $"The note is saved in the protocols folder of the {kind} '{target.Name}'."

/// The file and folder name come from the title, so it needs a letter or a digit. Returns the trimmed title.
let validateTitle (title: string) : Result<string, string> =
    let trimmed = title.Trim()

    if String.IsNullOrWhiteSpace trimmed then
        Error "A title is required."
    else
        match Validation.sanitizeProtocolName trimmed with
        | Some _ -> Ok trimmed
        | None -> Error "The title needs a letter or a digit, because the file name is made from it."

/// The ARC path of the note file for title.
let notePath (location: NoteLocation) (date: DateTime) (title: string) : Result<string, string> =
    match Validation.sanitizeProtocolName title with
    | None -> Error "The title needs a letter or a digit, because the file name is made from it."
    | Some name ->
        let path =
            match location with
            | NoteLocation.NotesFolder -> NoteConversion.mkNewRootNoteRelativePath date name
            | NoteLocation.Entity target -> NoteConversion.mkExistingTargetRelativePath target name

        match path with
        | Some path -> Ok path
        | None -> Error "Could not build a safe path for the note."

/// The text of a new note: its front matter with the title and date, and no body yet.
let noteContent (title: string) (date: DateTime) : string =
    NoteConversion.formatMarkdown {
        NotesDraft.init with
            Title = title
            DateCreated = Some date
    }

type NoteCreationApi = {
    pathExists: string -> JS.Promise<Result<bool, exn>>
    writeFile: FileContentDTO -> JS.Promise<Result<unit, exn>>
    createFileSystemItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>
}

/// Writes the note and returns its path. A note that exists already is not overwritten, the create is refused.
let create
    (api: NoteCreationApi)
    (location: NoteLocation)
    (date: DateTime)
    (title: string)
    : JS.Promise<Result<string, exn>> =
    promise {
        match notePath location date title with
        | Error message -> return Error(exn message)
        | Ok path ->
            match! checkTargetAvailability api.pathExists path with
            | Error error -> return Error(exn $"Could not check the note path: {error.Message}")
            | Ok TargetAvailability.Exists ->
                return Error(exn $"A note already exists at '{path}'. Choose another title.")
            | Ok TargetAvailability.Empty ->
                let request =
                    FileContentDTO.create FileContentType.Markdown (noteContent title date) path

                match!
                    writeFileWithEnsuredChildFolder
                        api.writeFile
                        api.createFileSystemItem
                        NoteConversion.tryGetNoteFolderRelativePath
                        NoteConversion.noteAssetsFolderName
                        request
                with
                | Error error -> return Error(exn $"The note could not be written: {error.Message}")
                | Ok() -> return Ok path
    }
