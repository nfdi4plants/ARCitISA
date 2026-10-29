module Renderer.Components.MainContent.ArcFilePreviewTargetHelper

open Fable.Core
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Composite.Widgets.JsonImport.Types
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
            return
                Error(
                    exn
                        $"Cannot import {unbox<string> nextType} JSON into the current {unbox<string> currentType} editor."
                )
        else
            return! publishAndPersistArcFile request.NextArcFile setArcFilePageState setArcFileInMemory
    }
