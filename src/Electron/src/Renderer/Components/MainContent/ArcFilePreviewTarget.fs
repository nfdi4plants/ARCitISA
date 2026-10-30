module Renderer.Components.MainContent.ArcFilePreviewTarget

open Feliz
open Renderer.Components.MainContent
open Renderer.Components.MainContent.ArcFilePreviewTargetHelper
open Renderer.Components.Helper
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Composite.Widgets.JsonImport.Types
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Components.Primitive.ErrorModal.Types
open ARCtrl
open ARCtrl.Json
open Swate.Components.Page.ArcFileEditor.EmptyTableView.Types
open Swate.Electron.Shared.DTOs.ArcNodesDto

[<ReactComponent>]
let ArcFilePreviewTarget (arcFile: ArcFiles, requestedView: ActiveView option) =
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let errorModal = useErrorModalCtx ()

    let setArcFilePageState nextRequestedView (nextArcFile: ArcFiles) =
        pageStateCtx.setState (Some(Renderer.Types.PageState.ArcFilePage(nextArcFile, nextRequestedView)))

    let setArcFileInMemoryWithErrorModal (nextArcFile: ArcFiles) =
        promise {
            match! ArcFileApiHelper.withArcFileRequest nextArcFile Api.ipcArcVaultApi.setArcFileInMemory with
            | Ok() -> ()
            | Error exn ->
                errorModal.enqueue (ErrorModalRequest.create (exn.Message, title = "Could not update ARC in memory"))
        }
        |> Promise.start

    let setArcFile nextArcFile =
        setArcFilePageState requestedView nextArcFile
        setArcFileInMemoryWithErrorModal nextArcFile

    let runDataMapMutation (errorTitle: string) (operation: Fable.Core.JS.Promise<Result<unit, exn>>) =
        promise {
            match! operation with
            | Ok() -> ()
            | Error exn -> errorModal.enqueue (ErrorModalRequest.create (exn.Message, title = errorTitle))
        }
        |> Promise.catch (fun exn -> errorModal.enqueue (ErrorModalRequest.create (exn.Message, title = errorTitle)))
        |> Promise.start

    let addDataMap () =
        match arcFile.TryGetDataMapParentInfo() with
        | None -> ()
        | Some parentInfo ->
            ArcFileApiHelper.withArcFileRequest
                (ArcFiles.DataMap(Some parentInfo, ARCtrl.Datamap.init ()))
                Api.ipcArcVaultApi.addArcFile
            |> runDataMapMutation "DataMap could not be added"

    let deleteDataMap () =
        match arcFile.TryGetDataMapParentInfo() |> Option.map DatamapParentInfo.toPath with
        | None -> ()
        | Some path ->
            Api.ipcArcVaultApi.deletePath path
            |> runDataMapMutation "DataMap could not be deleted"

    let importPersons () : Fable.Core.JS.Promise<Person[]> = promise {
        match! Api.ipcArcVaultApi.getPersons () with
        | Ok persons -> return persons |> Array.map Person.fromJsonString
        | Error error -> return raise error
    }

    let fetchArcNodes () : Fable.Core.JS.Promise<ArcNodeTable[]> = promise {
        match! Api.ipcArcVaultApi.getArcNodes () with
        | Ok tables -> return tables |> Array.map ArcNodeTableDto.toArcNodeTable
        | Error error -> return raise error
    }

    let pickFilePaths =
        React.useCallback (
            (fun () -> promise {
                match! Api.ipcArcVaultApi.pickArcPaths () with
                | Ok paths -> return paths
                | Error exn ->
                    errorModal.enqueue (ErrorModalRequest.create (exn.Message, title = "Could not pick files"))

                    return [||]
            }),
            [| errorModal |]

        )

    let importJson =
        React.useCallback (
            (fun (request: JsonImportRequest) -> promise {
                return!
                    importJsonRequestIntoCurrentTarget
                        arcFile
                        request
                        (setArcFilePageState requestedView)
                        (fun arcfile ->
                            ArcFileApiHelper.withArcFileRequest arcfile Api.ipcArcVaultApi.setArcFileInMemory
                        )
            }),
            [| box arcFile; box pageStateCtx |]
        )

    Html.div [
        prop.key (string (editorKey arcFile requestedView))
        prop.className "swt:contents"
        prop.children [
            Swate.Components.Page.ArcFileEditor.Main.ArcFileEditor(
                arcFile,
                setArcFile,
                pickFilePaths,
                addDataMap,
                deleteDataMap,
                startingActiveView = (requestedView |> Option.defaultValue ActiveView.Metadata),
                onImportJson = importJson,
                onImportPersons = importPersons,
                fetchArcNodes = fetchArcNodes,
                onError =
                    (fun message ->
                        errorModal.enqueue (
                            ErrorModalRequest.create (message, title = "Could not update ARC file editor")
                        )
                    )
            )
        ]
    ]
