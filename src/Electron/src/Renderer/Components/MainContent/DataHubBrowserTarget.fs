module Renderer.Components.MainContent.DataHubBrowserTarget

open System
open Feliz
open Renderer.Components.Helper.ArcVaultHelper
open Swate.Components
open Swate.Components.Page.DataHub
open Swate.Components.Page.DataHub.DataHubTypes
open Swate.Components.Api.GitLabApi
open Swate.Components.Composite.Actionbar.Types
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Electron.Shared.VersionControlTypes

module DataHubBrowserHelper =
    let isCancelError (error: exn) =
        String.Equals(error.Message, "Cancelled", StringComparison.OrdinalIgnoreCase)

    let toRepositoryFolderName (projectInfo: ExploreProjectDto) =
        projectInfo.path_with_namespace.Split('/')
        |> Array.tryLast
        |> Option.filter (fun segment -> not (String.IsNullOrWhiteSpace segment))
        |> Option.defaultValue projectInfo.name

    /// A clone follows the saved "Download large files" default. The session value belongs to the
    /// ARC that is open in this window.
    let toCloneRequest
        (gitState: Renderer.Context.GitWorkflow.GitState)
        (operationId: string)
        (projectInfo: ExploreProjectDto)
        (targetPath: string)
        : CloneWorkspaceRequestDto =
        {
            OperationId = operationId
            ProviderLocation = projectInfo.http_url_to_repo
            DisplayName = Some projectInfo.name
            TargetPath = targetPath
            TargetRef = None
            MaterializeAllObjects = gitState.UserDefaults.DownloadLargeFiles
        }

    let createActionBtns (onClone: ExploreProjectDto -> unit) (projectInfo: ExploreProjectDto) : ButtonInfo[] = [|
        ButtonInfo.create (
            "swt:fluent--arrow-download-24-regular swt:size-5",
            "Download repository",
            (fun _ -> onClone projectInfo)
        )
    |]


[<ReactComponent>]
let DataHubBrowserTarget () =
    let authCtx = Renderer.Context.AuthStateContext.useAuthStateCtx ()
    let pageCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let gitStateCtx = Renderer.Context.GitStateContext.useGitStateCtx ()
    let errorCtx = useErrorModalCtx ()
    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()

    let onArcError =
        createErrorModalCallback errorCtx.enqueue "Could not open ARC" appStateCtx

    let loaders: ExploreLoaders = {
        LoadAllRepos = Api.ipcGitLabApi.loadAllRepos
        LoadMostStarredRepos = Api.ipcGitLabApi.loadMostStarredRepos
        LoadUserRepos = Api.ipcGitLabApi.loadUserRepos
        LoadOrganisationGroups = Api.ipcGitLabApi.loadOrganisationGroups
        LoadOrganisationRepos = Api.ipcGitLabApi.loadOrganisationRepos
    }

    let isCloneBusy = gitStateCtx.state.BusyOperation.IsSome
    let runStatus = Renderer.Context.GitWorkflow.currentRunStatus gitStateCtx.state

    // The same last Git log as in the Git sidebar. It lets the user read the output of a clone that failed.
    let lastLog = Renderer.Context.LastGitLog.useLastGitLog ()

    let cloneAndOpenRepo (projectInfo: ExploreProjectDto) =
        promise {
            let! destinationResult = Api.ipcArcVaultApi.pickDirectory ()

            match destinationResult with
            | Error error when DataHubBrowserHelper.isCancelError error -> ()
            | Error error -> onArcError $"Could not pick download folder: {error.Message}"
            | Ok destinationFolder ->
                let targetPath =
                    ARCtrl.ArcPathHelper.combine
                        destinationFolder
                        (DataHubBrowserHelper.toRepositoryFolderName projectInfo)

                let cloneRequest =
                    DataHubBrowserHelper.toCloneRequest
                        gitStateCtx.state
                        (Renderer.VersionControlApiClient.newOperationId ())
                        projectInfo
                        targetPath

                match! gitStateCtx.cloneRepository cloneRequest with
                | Error _ -> ()
                | Ok clonedPath ->
                    let! wasOpened = Renderer.Components.Helper.ArcVaultHelper.openArcByPath onArcError clonedPath

                    if wasOpened then
                        pageCtx.setState None
        }
        |> Promise.start

    Html.div [
        prop.className "swt:size-full swt:flex swt:flex-col swt:overflow-hidden"
        prop.children [
            Swate.Components.Page.GitSidebar.OperationStatusNotice(
                ?runStatus = runStatus,
                ?errorNotice = gitStateCtx.state.ErrorNotice,
                ?warningNotice = gitStateCtx.state.WarningNotice,
                ?lastLog = lastLog,
                busyTestId = "DataHubCloneProgressNotice",
                errorTestId = "DataHubCloneErrorNotice",
                ?onCancelOperation =
                    (match gitStateCtx.state.BusyOperation with
                     | Some(Renderer.Context.GitWorkflow.GitBusyOperation.CloningRepository _) ->
                         Some gitStateCtx.cancelOperation
                     | _ -> None)
            )
            DataHubBrowser.ExplorePanel(
                accounts = authCtx,
                loaders = loaders,
                projectActionBtns = DataHubBrowserHelper.createActionBtns cloneAndOpenRepo,
                classNames = "swt:grow swt:flex swt:flex-col swt:gap-2 swt:p-2 swt:overflow-hidden",
                onClose = (fun _ -> pageCtx.setState None)
            )
        ]
    ]
