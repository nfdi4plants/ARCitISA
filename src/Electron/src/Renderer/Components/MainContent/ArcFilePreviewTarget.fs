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
let ArcFilePreviewTarget (arcFile: ArcFiles, requestedView: ActiveView option, revision: int) =
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let errorModal = useErrorModalCtx ()

    let syncCtx = Renderer.Context.ArcSyncContext.useArcSyncCtx ()
    let fileStateCtx = Renderer.Context.FileStateContext.useFileStateCtx ()

    let setArcFilePageState nextRequestedView (nextArcFile: ArcFiles) =
        pageStateCtx.setState (Some(Renderer.Types.PageState.ArcFilePage(nextArcFile, nextRequestedView, revision)))

    // A reload that ends after the user opened another page must not bring this page back, so it applies only while this path is open.
    let openPathRef = React.useRef (None: string option)
    openPathRef.current <- arcFile.TryGetRelativePath()

    React.useEffect (
        (fun () ->
            openPathRef.current <- arcFile.TryGetRelativePath()

            { new System.IDisposable with
                member _.Dispose() = openPathRef.current <- None
            }
        ),
        [||]
    )

    // Loads the current content of the open file from main. Used after a sync changed the file and after a refused edit.
    let reloadPage () =
        match arcFile.TryGetRelativePath() with
        | None -> ()
        | Some path ->
            let stillOpen () = openPathRef.current = Some path

            let applyReloadError (details: string) =
                if stillOpen () then
                    pageStateCtx.setState (
                        Some(
                            Renderer.Types.PageState.ErrorPage
                                $"The preview could not be refreshed. Select the file again. Details: {details}"
                        )
                    )

            promise {
                match! ArcViewHelper.openView path with
                | Ok _ when not (stillOpen ()) -> ()
                | Ok(Renderer.Types.PageState.ArcFilePage(next, _, nextRevision)) ->
                    let nextView =
                        match requestedView with
                        | Some ActiveView.DataMap when next.TryGetDataMap().IsNone -> Some ActiveView.Metadata
                        | view -> view

                    pageStateCtx.setState (Some(Renderer.Types.PageState.ArcFilePage(next, nextView, nextRevision)))
                | Ok other -> pageStateCtx.setState (Some other)
                | Error details -> applyReloadError details
            }
            |> Promise.catch (fun exn -> applyReloadError exn.Message)
            |> Promise.start

    let showEditRefusedModal (title: string) (message: string) =
        errorModal.enqueue (ErrorModalRequest.create (message, title = title))

    // The listener reads the latest render through this ref, so the subscription stays stable and never sees old values.
    let onSyncFinishedRef = React.useRef (fun (_: string[]) -> ())

    onSyncFinishedRef.current <-
        fun changedPaths ->
            let caseInsensitive = fileStateCtx.fileTreeStore.Snapshot.CaseInsensitive

            let pageState =
                Some(Renderer.Types.PageState.ArcFilePage(arcFile, requestedView, revision))

            if (FileExplorerStateReconciliation.syncChangedOpenPage caseInsensitive changedPaths pageState).IsSome then
                reloadPage ()

    React.useEffect (
        (fun () -> syncCtx.subscribeSyncFinished (fun changedPaths -> onSyncFinishedRef.current changedPaths)),
        [| box syncCtx.subscribeSyncFinished |]
    )

    let setArcFileInMemoryWithErrorModal (nextArcFile: ArcFiles) =
        promise {
            match! ArcFileApiHelper.setArcFileInMemory nextArcFile revision with
            | Ok outcome -> handleSetArcFileOutcome reloadPage showEditRefusedModal outcome
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
                        (fun arcfile -> promise {
                            match! ArcFileApiHelper.setArcFileInMemory arcfile revision with
                            | Ok outcome ->
                                handleSetArcFileOutcome reloadPage showEditRefusedModal outcome
                                return Ok()
                            | Error exn -> return Error exn
                        })
            }),
            [|
                box arcFile
                box requestedView
                box pageStateCtx
                box revision
            |]
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
                onJsonImportError = (fun error -> errorModal.enqueue (jsonImportErrorModalRequest error)),
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
