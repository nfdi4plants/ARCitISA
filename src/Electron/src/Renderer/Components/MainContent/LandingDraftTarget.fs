module Renderer.Components.MainContent.LandingDraftTarget

open Feliz
open Swate.Components.Page.Landing
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Renderer
open Renderer.Components.Helper

// [<ReactComponent>]
// let LandingDraftTarget () =

//     let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
//     let fileStateCtx = Renderer.Context.FileStateContext.useFileStateCtx ()
//     let landingDraft, setLandingDraft = React.useState LandingDraft.init
//     let landingUiState, setLandingUiState = React.useState LandingUiState.init

//     let onSubmit =
//         fun (payload: SubmitPayload) ->

//             let finishSuccess (response: FileContentDTO) =
//                 let selectedPath = PathHelpers.normalizePath response.path

//                 fileStateCtx.setSelection (ArcSelection.forTreePath (Some selectedPath))

//                 let pageState = Renderer.Types.PageState.fromFileContentDTO response
//                 pageStateCtx.setState (Some pageState)

//                 setLandingDraft LandingDraft.init
//                 setLandingUiState LandingUiState.init

//             promise {
//                 setLandingUiState {
//                     landingUiState with
//                         IsSubmitting = true
//                         Error = None
//                 }

//                 let! saveResult = ArcFileApiHelper.addArcFileAndOpen payload.ArcFile

//                 match saveResult with
//                 | Result.Error message ->
//                     setLandingUiState {
//                         landingUiState with
//                             IsSubmitting = false
//                             Error = Some message.Message
//                     }
//                 | Ok previewData ->
//                     match payload.ProtocolIntent with
//                     | None -> finishSuccess previewData
//                     | Some protocolIntent ->
//                         let requestFileType =
//                             FileContentDTO.inferTextFileTypeFromPath protocolIntent.RelativePath

//                         let request: FileContentDTO =
//                             FileContentDTO.create requestFileType protocolIntent.Content protocolIntent.RelativePath

//                         let! writeResult = Api.ipcArcVaultApi.writeFile request

//                         match writeResult with
//                         | Ok() -> finishSuccess previewData
//                         | Result.Error exn ->
//                             setLandingUiState {
//                                 landingUiState with
//                                     IsSubmitting = false
//                                     Error = Some $"Saved ARC metadata but failed to write protocol file: {exn.Message}"
//                             }
//             }
//             |> Promise.start

//     Landing.Wizard(
//         landingDraft,
//         setLandingDraft,
//         landingUiState,
//         setLandingUiState,
//         onSubmit,
//         onImportPersons = Api.importPersons
//     )
let x = 0
