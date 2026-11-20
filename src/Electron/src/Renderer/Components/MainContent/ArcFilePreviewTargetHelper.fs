module Renderer.Components.MainContent.ArcFilePreviewTargetHelper

open Fable.Core
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Composite.Widgets.JsonImport.Types
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Shared
open ARCtrl

let editorKey (arcFile: ArcFiles) (requestedView: ActiveView option) =
    arcFile.TryGetRelativePath()
    |> Option.defaultValue (string arcFile.RelatedArcFilesDiscriminate),
    requestedView |> Option.map _.ViewIndex

let private publishAndPersistArcFile
    (nextArcFile: ArcFiles)
    (publishArcFile: ArcFiles -> unit)
    (persistArcFile: ArcFiles -> JS.Promise<Result<unit, exn>>)
    =
    promise {
        publishArcFile nextArcFile
        return! persistArcFile nextArcFile
    }

let importJsonRequestIntoCurrentTarget
    (currentArcFile: ArcFiles)
    (request: JsonImportRequest)
    (setArcFilePageState: ArcFiles -> unit)
    (setArcFileInMemory: ArcFiles -> JS.Promise<Result<unit, exn>>)
    =
    promise {
        let currentType = currentArcFile.RelatedArcFilesDiscriminate
        let nextType = request.NextArcFile.RelatedArcFilesDiscriminate

        if nextType <> currentType then
            return Error(exn $"Cannot import {nextType} JSON into the current {currentType} editor.")
        else
            return! publishAndPersistArcFile request.NextArcFile setArcFilePageState setArcFileInMemory
    }

let jsonImportErrorModalRequest (error: JsonImportError) : ErrorModalRequest =
    ErrorModalRequest.create (error.Message, title = error.Title, ?details = error.Details)

/// A refused edit means a sync is running or changed the file after the page was read. The page loads the current content again
/// and the user gets told, because the last edit is gone.
let handleSetArcFileOutcome
    (reloadPage: unit -> unit)
    (showModal: string -> string -> unit)
    (outcome: Swate.Electron.Shared.IPCTypes.SetArcFileOutcome)
    =
    match outcome with
    | Swate.Electron.Shared.IPCTypes.SetArcFileOutcome.Applied -> ()
    | Swate.Electron.Shared.IPCTypes.SetArcFileOutcome.Refused ->
        reloadPage ()

        showModal
            "Edit not applied"
            "ARCitISA is loading changes from disk, or this file changed after the page loaded it, so the last edit was not applied. The page now shows the current content."
