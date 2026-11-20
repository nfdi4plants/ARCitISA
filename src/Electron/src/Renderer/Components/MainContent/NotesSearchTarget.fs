module Renderer.Components.MainContent.NotesSearchTarget

open Feliz
open Swate.Components.Composite
open Swate.Components.Composite.Notes.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.DTOs.NoteSearchDto
open Renderer

[<ReactComponent>]
let NotesSearchTarget () =

    let pageCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let fileTreeCtx = Renderer.Context.FileStateContext.useFileStateCtx ()
    let notes, setNotes = React.useState ([]: Note list)

    let isLoading, setIsLoading = React.useState true
    let error, setError = React.useState (None: string option)

    React.useEffect (
        (fun () ->
            let mutable isDisposed = false

            setIsLoading true
            setError None

            promise {
                let! result = Api.ipcArcVaultApi.readNotes ()

                if not isDisposed then
                    match result with
                    | Ok nextNotes ->
                        setNotes (nextNotes |> Array.map NoteSearchNoteDto.toNote |> Array.toList)
                        setIsLoading false
                    | Result.Error exn ->
                        setNotes []
                        setError (Some $"Failed to load notes: {exn.Message}")
                        setIsLoading false
            }
            |> Promise.start

            fun () -> isDisposed <- true
        ),
        [||]
    )

    let openNote (relativePath: string) =
        promise {

            let! result = Api.ipcArcVaultApi.openFile relativePath

            match result with
            | Ok dto ->
                let selectedPath = PathHelpers.normalizePath relativePath
                fileTreeCtx.setSelection (ArcSelection.forTreePath (Some selectedPath))

                let pageState =
                    Renderer.Types.PageState.fromFileContentDTO (dto.content, dto.revision)

                pageCtx.setState (Some pageState)
            | Result.Error exn ->
                fileTreeCtx.setSelection (ArcSelection.clearExplorerNode fileTreeCtx.state.Selection)

                let errorPage =
                    Renderer.Types.PageState.ErrorPage $"Could not open note: {exn.Message}"

                pageCtx.setState (Some errorPage)
        }
        |> Promise.start

    SearchComponent.Main(notes, isLoading, error, openNote)
