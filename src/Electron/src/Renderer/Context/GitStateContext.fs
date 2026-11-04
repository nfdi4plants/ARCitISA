module Renderer.Context.GitStateContext

open Browser.Dom
open Fable.Core
open Feliz
open Feliz.UseElmish

open Renderer.Types
open Swate.Components.Page.GitSidebarTypes
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.VersionControlTypes

open Renderer.Context.GitWorkflow

type GitStateController = {
    state: GitState
    refresh: unit -> unit
    sidebarVisibilityChanged: bool -> unit
    uploadRightAwayChanged: bool -> unit
    initRepository: unit -> unit
    fetch: unit -> unit
    push: unit -> unit
    cancelOperation: unit -> unit
    updateFromOnline: unit -> unit
    primarySaveSelection: GitSidebarCommitSelectionRequest -> unit
    primarySaveAll: string -> unit
    cloneRepository: CloneWorkspaceRequestDto -> JS.Promise<Result<string, string>>
    commitSelection: GitSidebarCommitSelectionRequest -> unit
    commitAll: string -> unit
    discardSelection: string[] -> unit
    confirmPendingRemoteAction: unit -> unit
    cancelPendingRemoteAction: unit -> unit
    submitPublishRename: GitSidebarPublishSubmit -> unit
    cancelPublishRename: unit -> unit
    saveLfsAutoTrackThreshold: int -> unit
    saveDiffIndexingLimit: int -> unit
    saveDownloadLargeFiles: bool -> unit
    createBranch: GitSidebarCreateBranchRequest -> unit
    switchBranch: string -> unit
    selectChange: GitSidebarChange -> JS.Promise<Result<unit, string>>
    confirmMergeResolution: GitMergeResolutionRequest -> unit
    abandonMerge: unit -> unit
    pruneLfsCache: unit -> unit
    dedupLfsStorage: unit -> unit
    /// Requests of the open diff page.
    sendDiffMsg: GitDiffMsg -> unit
}

module private Helper =

    /// The conflict page captures the handle and workspace token used to validate its resolution request.
    let loadConflictPage
        (conflict: ConflictSessionSummaryDto)
        (workspaceVersion: string)
        (requestedPath: string)
        : JS.Promise<Result<PageState, string>> =
        promise { return conflictPageFor conflict workspaceVersion requestedPath }

    let dependencies
        (reportError: GitErrorNotification -> unit)
        (hasUsableAccount: unit -> bool)
        (updatePageState: (PageState option -> PageState option) -> unit)
        : GitDependencies =
        {
            getSessionInfo = Renderer.VersionControlApiClient.getSessionInfo
            getStatus = Renderer.VersionControlApiClient.getStatus
            listRefs = Renderer.VersionControlApiClient.listRefs
            getRepositoryWebUrl = Renderer.VersionControlApiClient.getRepositoryWebUrl
            getStoragePolicySettings = Renderer.VersionControlApiClient.getStoragePolicySettings
            setStoragePolicySettings = Renderer.VersionControlApiClient.setStoragePolicySettings
            getUploadRightAway = Renderer.VersionControlApiClient.getUploadRightAway
            setUploadRightAway = Renderer.VersionControlApiClient.setUploadRightAway
            loadConflictPage = loadConflictPage
            updatePageState = updatePageState
            initializeWorkspace = Renderer.VersionControlApiClient.initializeWorkspace
            bindWorkspace = Renderer.VersionControlApiClient.bindWorkspace
            createRemoteProject = Api.ipcGitLabApi.createProject
            loadPublishOwners = Api.ipcGitLabApi.loadPublishOwners
            renameOpenArcRoot =
                fun newName -> promise {
                    let! result = Api.ipcArcVaultApi.renameOpenArcRoot newName
                    return result |> Result.mapError _.Message
                }
            checkDependencies = Renderer.VersionControlApiClient.checkDependencies
            installDependency = Renderer.VersionControlApiClient.installDependency
            refreshSynchronization = Renderer.VersionControlApiClient.refreshSynchronization
            synchronize = Renderer.VersionControlApiClient.synchronize
            cancelOperation = Renderer.VersionControlApiClient.cancelOperation
            cloneWorkspace = Renderer.VersionControlApiClient.cloneWorkspace
            createRef = Renderer.VersionControlApiClient.createRef
            preflightSwitchRef = Renderer.VersionControlApiClient.preflightSwitchRef
            switchRef = Renderer.VersionControlApiClient.switchRef
            createRevision = Renderer.VersionControlApiClient.createRevision
            restorePaths = Renderer.VersionControlApiClient.restorePaths
            resolveConflict = Renderer.VersionControlApiClient.resolveConflict
            finalizeConflict = Renderer.VersionControlApiClient.finalizeConflict
            cancelConflict = Renderer.VersionControlApiClient.cancelConflict
            listObjects = Renderer.VersionControlApiClient.listObjects
            materializeObject = Renderer.VersionControlApiClient.materializeObject
            materializeObjects = Renderer.VersionControlApiClient.materializeObjects
            pruneStorage = Renderer.VersionControlApiClient.pruneStorage
            deduplicateStorage = Renderer.VersionControlApiClient.deduplicateStorage
            clearStaleLock = Renderer.VersionControlApiClient.clearStaleLock
            textDiff = {
                openTextDiff = Renderer.VersionControlApiClient.openTextDiff
                readTextDiffPage = Renderer.VersionControlApiClient.readTextDiffPage
                replayTextDiffPage = Renderer.VersionControlApiClient.replayTextDiffPage
                expandTextDiff = Renderer.VersionControlApiClient.expandTextDiff
                readTextDiffLine = Renderer.VersionControlApiClient.readTextDiffLine
                closeTextDiff = Renderer.VersionControlApiClient.closeTextDiff
            }
            hasUsableAccount = hasUsableAccount
            delay = fun milliseconds -> Promise.sleep milliseconds
            newOperationId = Renderer.VersionControlApiClient.newOperationId
            confirmLfsPrune = fun message -> window.confirm message
            confirmInstall = fun message -> window.confirm message
            reportError = reportError
        }

let GitStateCtx =
    React.createContext<GitStateController> (
        {
            state = GitState.Empty
            refresh = fun () -> ()
            sidebarVisibilityChanged = fun _ -> ()
            uploadRightAwayChanged = fun _ -> ()
            initRepository = fun () -> ()
            fetch = fun () -> ()
            push = fun () -> ()
            cancelOperation = fun () -> ()
            updateFromOnline = fun () -> ()
            primarySaveSelection = fun _ -> ()
            primarySaveAll = fun _ -> ()
            cloneRepository = fun _ -> promise { return Ok "" }
            commitSelection = fun _ -> ()
            commitAll = fun _ -> ()
            discardSelection = fun _ -> ()
            confirmPendingRemoteAction = fun () -> ()
            cancelPendingRemoteAction = fun () -> ()
            submitPublishRename = fun _ -> ()
            cancelPublishRename = fun () -> ()
            saveLfsAutoTrackThreshold = fun _ -> ()
            saveDiffIndexingLimit = fun _ -> ()
            saveDownloadLargeFiles = fun _ -> ()
            createBranch = fun _ -> ()
            switchBranch = fun _ -> ()
            selectChange = fun _ -> promise { return Ok() }
            confirmMergeResolution = fun _ -> ()
            abandonMerge = fun () -> ()
            pruneLfsCache = fun () -> ()
            dedupLfsStorage = fun () -> ()
            sendDiffMsg = fun _ -> ()
        }
    )

[<Hook>]
let useGitStateCtx () = React.useContext GitStateCtx

[<ReactComponent>]
let GitStateCtxProvider (children: ReactElement) =

    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let updatePageStateRef = React.useRef ignore
    updatePageStateRef.current <- Renderer.Context.PageStateContext.usePageStateUpdateCtx ()
    let authStateCtx = Renderer.Context.AuthStateContext.useAuthStateCtx ()
    let usableAccountRef = React.useRef false
    usableAccountRef.current <- authStateCtx.UsableActiveUser().IsSome
    let errorModalCtx = useErrorModalCtx ()
    let errorModalCtxRef = React.useRef errorModalCtx
    errorModalCtxRef.current <- errorModalCtx

    let reportGitError =
        React.useCallback (
            (fun (notification: GitErrorNotification) ->
                errorModalCtxRef.current.enqueue (
                    ErrorModalRequest.create (
                        notification.Message,
                        title = notification.Title,
                        ?details = OperationLog.tryDetails notification.Log
                    )
                )
            ),
            [||]
        )

    let dependencies =
        React.useMemo (
            (fun _ ->
                Helper.dependencies
                    reportGitError
                    (fun () -> usableAccountRef.current)
                    (fun update -> updatePageStateRef.current update)
            ),
            [||]
        )

    let gitState, dispatch =
        React.useElmish ((fun () -> init ()), update dependencies pageStateCtx.setState, subscribe, [||])

    // The workflow seeds the git values of a state without a session from the user defaults.
    let userDefaults =
        Renderer.MainSyncedState.useMainSyncedState {
            initial = None
            load =
                fun () -> promise {
                    let! defaults = Api.ipcUserDefaultsApi.getUserDefaults ()
                    return Some defaults
                }
            subscribe =
                fun update ->
                    Renderer.IpcReceiver.subscribeProxyReceiver<IUserDefaultsRendererApi> {
                        userDefaultsChanged = Some >> update
                    }
            // The built-in values stay in place. Only the settings page reports the failed load, and only
            // once the user opens it.
            onError = fun error -> console.warn ("Could not load the user defaults: " + error.Message)
            dependencies = [||]
        }

    React.useEffect (
        (fun () ->
            userDefaults.state
            |> Option.iter (fun defaults -> dispatch (UserDefaultsLoaded defaults.Effective))
        ),
        [| box userDefaults.state |]
    )

    let refresh () = dispatch RefreshRequested

    let sidebarVisibilityChanged isVisible =
        dispatch (SidebarVisibilityChanged isVisible)

    let uploadRightAwayChanged (value: bool) = dispatch (UploadRightAwayChanged value)

    let initRepository () = dispatch InitRepositoryRequested

    let fetch () = dispatch FetchRequested

    let push () = dispatch PushRequested

    let cancelOperation () =
        dispatch CancelCurrentOperationRequested

    let updateFromOnline () = dispatch UpdateFromOnlineRequested

    let primarySaveSelection (request: GitSidebarCommitSelectionRequest) =
        dispatch (PrimarySaveSelectionRequested request)

    let primarySaveAll (message: string) =
        dispatch (PrimarySaveAllRequested message)

    let cloneRepository (request: CloneWorkspaceRequestDto) =
        Promise.create (fun resolve _reject -> dispatch (CloneRequested(request, resolve)))

    let commitSelection (request: GitSidebarCommitSelectionRequest) =
        dispatch (CommitSelectionRequested request)

    let commitAll (message: string) = dispatch (CommitAllRequested message)

    let discardSelection (paths: string[]) =
        dispatch (DiscardSelectionRequested paths)

    let confirmPendingRemoteAction () =
        dispatch ConfirmPendingRemoteActionRequested

    let cancelPendingRemoteAction () =
        dispatch CancelPendingRemoteActionRequested

    let submitPublishRename (submit: GitSidebarPublishSubmit) =
        dispatch (SubmitPublishRenameRequested submit)

    let cancelPublishRename () = dispatch CancelPublishRenameRequested

    let saveLfsAutoTrackThreshold (thresholdMb: int) =
        dispatch (SaveLfsAutoTrackThresholdRequested thresholdMb)

    let saveDiffIndexingLimit (limitMb: int) =
        dispatch (SaveDiffIndexingLimitRequested limitMb)

    let saveDownloadLargeFiles (downloadLargeFiles: bool) =
        dispatch (SaveDownloadLargeFilesRequested downloadLargeFiles)

    let createBranchFrom (request: GitSidebarCreateBranchRequest) =
        dispatch (CreateBranchRequested request)

    let switchBranchTo (branchName: string) =
        dispatch (SwitchBranchRequested branchName)

    let selectChange (change: GitSidebarChange) =
        Promise.create (fun resolve _reject -> dispatch (SelectChangeRequested(change, resolve)))

    let confirmMergeResolutionAction (request: GitMergeResolutionRequest) =
        dispatch (ConfirmMergeResolutionRequested request)

    let abandonMerge () = dispatch (WriteRequested AbandonMerge)

    let pruneLfsCache () = dispatch PruneLfsCacheRequested

    let dedupLfsStorage () = dispatch DedupLfsStorageRequested

    React.useEffect ((fun () -> dispatch (ArcPathChanged appStateCtx)), [| box appStateCtx |])

    // Other parts of the app replace the page directly, so the workflow learns here that the
    // user left the diff page and closes its handle.
    React.useEffect (
        (fun () -> dispatch (DiffPageMsg(GitDiffMsg.PageStateObserved pageStateCtx.state))),
        [| box pageStateCtx.state |]
    )

    let sendDiffMsg (msg: GitDiffMsg) = dispatch (DiffPageMsg msg)

    let gitStateController: GitStateController =
        React.useMemo (
            (fun _ -> {
                state = gitState
                refresh = refresh
                sidebarVisibilityChanged = sidebarVisibilityChanged
                uploadRightAwayChanged = uploadRightAwayChanged
                initRepository = initRepository
                fetch = fetch
                push = push
                cancelOperation = cancelOperation
                updateFromOnline = updateFromOnline
                primarySaveSelection = primarySaveSelection
                primarySaveAll = primarySaveAll
                cloneRepository = cloneRepository
                commitSelection = commitSelection
                commitAll = commitAll
                discardSelection = discardSelection
                confirmPendingRemoteAction = confirmPendingRemoteAction
                cancelPendingRemoteAction = cancelPendingRemoteAction
                submitPublishRename = submitPublishRename
                cancelPublishRename = cancelPublishRename
                saveLfsAutoTrackThreshold = saveLfsAutoTrackThreshold
                saveDiffIndexingLimit = saveDiffIndexingLimit
                saveDownloadLargeFiles = saveDownloadLargeFiles
                createBranch = createBranchFrom
                switchBranch = switchBranchTo
                selectChange = selectChange
                confirmMergeResolution = confirmMergeResolutionAction
                abandonMerge = abandonMerge
                pruneLfsCache = pruneLfsCache
                dedupLfsStorage = dedupLfsStorage
                sendDiffMsg = sendDiffMsg
            }),
            [| box gitState |]
        )

    GitStateCtx.Provider(gitStateController, children)
