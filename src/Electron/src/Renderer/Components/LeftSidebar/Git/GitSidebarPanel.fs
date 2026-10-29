module Renderer.Components.LeftSidebar.Git.GitSidebarPanel

open Browser.Dom
open Feliz
open Renderer.Components.Helper.ArcVaultHelper
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Electron.Shared.VersionControlTypes

let mutable private dependencyCheckStarted = false

/// The message shown when a version control dependency is missing or too old. The
/// library decides what is required (Git 2.42, Git LFS 3.7 and the LFS filter
/// configuration) and says how to fix it. Its remediation text starts with the cause when a
/// version check failed.
let dependencyProblemMessage (statuses: DependencyStatusDto[]) : string option =
    // The library reports the filter configuration as installed when the filter is set up, whatever
    // the state of git-lfs itself. A problem of the configuration is only worth a line of its own
    // while git-lfs works. Otherwise the line of git-lfs says what to do first.
    let gitLfsWorks =
        statuses
        |> Array.tryFind (fun status -> status.Component = "git-lfs")
        |> Option.forall (fun status -> status.Installed && status.Compatible)

    let problems =
        statuses
        |> Array.filter (fun status -> not status.Installed || not status.Compatible)
        |> Array.choose (fun status ->
            let remediation =
                status.Remediation
                |> Option.map (fun text -> $" {text}")
                |> Option.defaultValue ""

            if status.Component = "git-lfs-configuration" then
                if gitLfsWorks then
                    Some $"Git LFS is installed but not set up.{remediation}"
                else
                    None
            elif not status.Installed then
                Some $"{status.Component} was not found or could not be run.{remediation}"
            else
                match status.Version with
                | Some version -> Some $"{version} is not supported.{remediation}"
                | None -> Some $"{status.Component} is not supported.{remediation}"
        )

    if problems.Length = 0 then
        None
    else
        Some(String.concat "\n" problems)

/// Remote transfer operations run cancellable git processes, so the cancel button is offered for all of them.
let private isCancellableBusyOperation (busyOperation: Renderer.Context.GitWorkflow.GitBusyOperation) =
    match busyOperation with
    | Renderer.Context.GitWorkflow.GitBusyOperation.FetchingFromRemote
    | Renderer.Context.GitWorkflow.GitBusyOperation.PullingFromRemote
    | Renderer.Context.GitWorkflow.GitBusyOperation.PushingToRemote
    | Renderer.Context.GitWorkflow.GitBusyOperation.CloningRepository _ -> true
    | _ -> false

let private activityVerb (kind: Renderer.Context.LfsActivityContext.LfsActionKind) =
    match kind with
    | Renderer.Context.LfsActivityContext.LfsActionKind.Download -> "Downloading"
    | Renderer.Context.LfsActivityContext.LfsActionKind.Free -> "Freeing"

/// The log for the sidebar. A log without lines is None, so the notice offers no "Show logs" button for it.
let sidebarLog
    (log: Renderer.Context.OperationLog.OperationLog)
    : Swate.Components.Page.GitSidebarTypes.GitSidebarLog option =
    if log.Lines.Length = 0 then
        None
    else
        let lines =
            log.Lines
            |> Array.map (fun line ->
                let mapped: Swate.Components.Page.GitSidebarTypes.GitSidebarLogLine = {
                    Kind = line.Kind
                    Text = line.Text
                }

                mapped
            )

        let mapped: Swate.Components.Page.GitSidebarTypes.GitSidebarLog = { Lines = lines; Omitted = log.Omitted }

        Some mapped

let private activityLabel (action: Renderer.Context.LfsActivityContext.LfsRunningAction) =
    $"{activityVerb action.Kind} {Swate.Components.Shared.PathHelpers.getFileName action.Target}"

/// One background item per running file tree action. The label names the kind and the folder or file. The detail
/// and the percent come from the progress that the action reported, the same text as on the file tree row. The
/// log is the output of the action.
let backgroundActivityItems
    (actions: Renderer.Context.LfsActivityContext.LfsRunningAction[])
    : Swate.Components.Page.GitSidebarTypes.GitSidebarBackgroundActivity[] =
    actions
    |> Array.map (fun action ->
        let item: Swate.Components.Page.GitSidebarTypes.GitSidebarBackgroundActivity = {
            Label = activityLabel action
            Detail = Renderer.Context.LfsActivityContext.LfsRunningAction.progressText action
            ProgressPercent = Renderer.Context.LfsActivityContext.LfsRunningAction.progressPercent action
            Log = sidebarLog action.Log
        }

        item
    )

/// The Save action and the remote transfers of Download Changes and Upload Changes. A file tree action holds the
/// session lock of the library, so these wait for it before they start.
let private waitsForFileTreeActions (busyOperation: Renderer.Context.GitWorkflow.GitBusyOperation) =
    match busyOperation with
    | Renderer.Context.GitWorkflow.GitBusyOperation.CommittingSelectedChanges
    | Renderer.Context.GitWorkflow.GitBusyOperation.CommittingAllChanges
    | Renderer.Context.GitWorkflow.GitBusyOperation.PullingFromRemote
    | Renderer.Context.GitWorkflow.GitBusyOperation.PushingToRemote -> true
    | _ -> false

/// The headline of a busy sidebar operation that waits for a file tree action. It names the first action that
/// started before the operation did, because only such an action can hold the session lock of the library. The
/// headline stays unset once the operation has sent any progress or output of its own, because it has the lock
/// then. Without an earlier action it stays unset too.
let waitingHeadline
    (busyOperation: Renderer.Context.GitWorkflow.GitBusyOperation option)
    (operationReported: bool)
    (operationStartedAt: float option)
    (actions: Renderer.Context.LfsActivityContext.LfsRunningAction[])
    : string option =
    match busyOperation, operationStartedAt with
    | Some operation, Some startedAt when waitsForFileTreeActions operation && not operationReported ->
        actions
        |> Array.tryFind (fun action -> action.StartedAt < startedAt)
        |> Option.map (fun action ->
            let label = activityLabel action
            $"Waits for {label.Substring(0, 1).ToLowerInvariant()}{label.Substring 1} to finish"
        )
    | _ -> None

[<ReactComponent>]
let Main () =

    let gitStateCtx = Renderer.Context.GitStateContext.useGitStateCtx ()

    let leftSidebarCtx =
        Swate.Components.Composite.Layout.LeftSidebarContext.useLeftSidebarCtx ()

    let authState = Renderer.Context.AuthStateContext.useAuthStateCtx ()
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let runStatus = Renderer.Context.GitWorkflow.currentRunStatus gitStateCtx.state
    let errorCtx = useErrorModalCtx ()
    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()

    let runningLfsActions =
        Renderer.Context.LfsActivityContext.useLfsRunningActionsCtx ()

    let backgroundActivities =
        React.useMemo ((fun () -> backgroundActivityItems runningLfsActions), [| box runningLfsActions |])

    // The log changes with every batch of output. A sidebar render for something else keeps the mapped lines.
    let operationLog =
        React.useMemo ((fun () -> sidebarLog gitStateCtx.state.CurrentLog), [| box gitStateCtx.state.CurrentLog |])

    // The dialog resets its name and owner state when the prompt object changes. The model keeps
    // the same prompt object between unrelated updates, so a re-render of the panel keeps them.
    let publishRenamePrompt =
        React.useMemo (
            (fun () ->
                gitStateCtx.state.PendingPublishRename
                |> Option.map (fun prompt ->
                    let mapped: Swate.Components.Page.GitSidebarTypes.GitSidebarPublishRenamePrompt = {
                        CurrentName = prompt.CurrentName
                        Message = prompt.Message
                        Owners =
                            prompt.Owners
                            |> Array.map (fun owner -> {
                                NamespaceId = owner.NamespaceId
                                Label = owner.Label
                                DisabledReason = owner.DisabledReason
                            })
                        SelectedNamespaceId = prompt.SelectedNamespaceId
                    }

                    mapped
                )
            ),
            [| box gitStateCtx.state.PendingPublishRename |]
        )

    React.useEffect ((fun () -> fun () -> gitStateCtx.sidebarVisibilityChanged false), [||])

    React.useEffect (
        (fun () -> gitStateCtx.sidebarVisibilityChanged leftSidebarCtx.state),
        [| box leftSidebarCtx.state |]
    )

    let onOpenArcError =
        createErrorModalCallback errorCtx.enqueue "Error opening ARC" appStateCtx

    React.useEffectOnce (fun () ->
        if not dependencyCheckStarted then
            dependencyCheckStarted <- true

            Renderer.VersionControlApiClient.checkDependencies {
                OperationId = Renderer.VersionControlApiClient.newOperationId ()
            }
            |> Promise.map (fun result ->
                let statuses =
                    match result with
                    | Ok(OperationResultDto.Succeeded outcome)
                    | Ok(OperationResultDto.PartiallySucceeded(outcome, _)) -> outcome.Value
                    | _ -> [||]

                match result, dependencyProblemMessage statuses with
                | _, Some message ->
                    errorCtx.enqueue (ErrorModalRequest.create (message, title = "Version control dependencies"))
                | Ok(OperationResultDto.Failed failure), None
                | Ok(OperationResultDto.PartiallySucceeded(_, failure)), None ->
                    errorCtx.enqueue (
                        ErrorModalRequest.create (
                            Renderer.Context.GitWorkflow.failureMessage failure,
                            title = "Could not verify Git installation"
                        )
                    )
                | Error message, None ->
                    errorCtx.enqueue (ErrorModalRequest.create (message, title = "Could not verify Git installation"))
                | _ -> ()
            )
            |> ignore
    )

    let remoteActionsEnabled = authState.UsableActiveUser().IsSome

    let remoteActionsWarning =
        if remoteActionsEnabled then
            None
        else
            Some "Sign in to a DataHUB account to use fetch, pull, push, update, and remote bootstrap."

    let openArc =
        fun _ ->
            Renderer.Components.Helper.ArcVaultHelper.openArc onOpenArcError
            |> Promise.start

    match gitStateCtx.state.CurrentArcPath with
    | None ->
        Renderer.Components.LeftSidebar.Git.GitSidebarEmptyState.Main(
            title = "Open an ARC to use Git features",
            description = "Source control becomes available after you open or download an ARC.",
            iconClassName = "swt:fluent--folder-open-24-regular",
            primaryAction = {
                Label = "Open ARC"
                IconClassName = "swt:fluent--folder-open-24-regular"
                Disabled = false
                OnClick = openArc
            },
            secondaryAction = {
                Label = "Download ARC"
                IconClassName = "swt:fluent--cloud-arrow-down-24-regular"
                Disabled = false
                OnClick = (fun () -> pageStateCtx.setState (Some Renderer.Types.PageState.DataHubBrowser))
            }
        )
    | Some _ when
        gitStateCtx.state.RepositoryAvailability = Renderer.Context.GitWorkflow.GitRepositoryAvailability.MissingRepository
        ->
        Renderer.Components.LeftSidebar.Git.GitSidebarEmptyState.Main(
            title = "Initialize Git for this ARC",
            description = "The selected ARC folder is not a Git repository yet.",
            iconClassName = "swt:fluent--branch-fork-24-regular",
            primaryAction = {
                Label =
                    if
                        gitStateCtx.state.BusyOperation = Some
                            Renderer.Context.GitWorkflow.GitBusyOperation.InitializingRepository
                    then
                        "Initializing..."
                    else
                        "Initialize Repository"
                IconClassName = "swt:fluent--branch-fork-24-regular"
                Disabled = gitStateCtx.state.BusyOperation.IsSome
                OnClick = fun () -> gitStateCtx.initRepository ()
            }
        )
    | Some _ ->
        let canCancelOperation =
            gitStateCtx.state.BusyOperation |> Option.exists isCancellableBusyOperation

        Swate.Components.Page.GitSidebar.Main(
            status = gitStateCtx.state.Status,
            changedFiles = gitStateCtx.state.ChangedFiles,
            branchOptions = gitStateCtx.state.BranchOptions,
            ?runStatus = runStatus,
            backgroundActivities = backgroundActivities,
            operationSteps = gitStateCtx.state.CurrentSteps,
            ?operationLog = operationLog,
            ?busyHeadlineOverride =
                waitingHeadline
                    gitStateCtx.state.BusyOperation
                    gitStateCtx.state.OperationReported
                    gitStateCtx.state.OperationStartedAt
                    runningLfsActions,
            ?hasRemote = Some(Renderer.Context.GitWorkflow.hasRemote gitStateCtx.state),
            ?selectedFile = gitStateCtx.state.SelectedChangePath,
            ?errorNotice = gitStateCtx.state.ErrorNotice,
            ?warningNotice = gitStateCtx.state.WarningNotice,
            ?pendingConfirmation = gitStateCtx.state.PendingConfirmation,
            ?publishRenamePrompt = publishRenamePrompt,
            callbacks = {
                OnRefresh = gitStateCtx.refresh
                OnFetch = gitStateCtx.fetch
                OnPush = gitStateCtx.push
                OnUpdateFromOnline = gitStateCtx.updateFromOnline
                OnPrimarySaveSelection = gitStateCtx.primarySaveSelection
                OnPrimarySaveAll = gitStateCtx.primarySaveAll
                OnCommitSelection = gitStateCtx.commitSelection
                OnCommitAll = gitStateCtx.commitAll
                OnDiscardSelection =
                    fun paths ->
                        let conflictedPaths =
                            gitStateCtx.state.ChangedFiles
                            |> Array.filter _.IsConflicted
                            |> Array.map _.Path
                            |> Set.ofArray

                        let discardablePaths =
                            paths |> Array.filter (fun path -> not (Set.contains path conflictedPaths))

                        if discardablePaths.Length > 0 then
                            gitStateCtx.discardSelection discardablePaths
                OnConfirmPendingRemoteAction = gitStateCtx.confirmPendingRemoteAction
                OnCancelPendingRemoteAction = gitStateCtx.cancelPendingRemoteAction
                OnSaveDownloadLargeFiles = gitStateCtx.saveDownloadLargeFiles
                OnSaveLfsAutoTrackThreshold = gitStateCtx.saveLfsAutoTrackThreshold
                OnSaveDiffIndexingLimit = gitStateCtx.saveDiffIndexingLimit
                OnCreateBranch = gitStateCtx.createBranch
                OnSwitchBranch = gitStateCtx.switchBranch
                OnSelectChange = gitStateCtx.selectChange
                OnPruneLfsCache = gitStateCtx.pruneLfsCache
                OnDedupLfsStorage = gitStateCtx.dedupLfsStorage
                OnCancelOperation = gitStateCtx.cancelOperation
            },
            downloadLargeFiles = gitStateCtx.state.DownloadLargeFiles,
            lfsAutoTrackThresholdMb = gitStateCtx.state.LfsAutoTrackThresholdMb,
            diffIndexingLimitMb = gitStateCtx.state.DiffIndexingLimitMb,
            uploadRightAway = gitStateCtx.state.UploadRightAway,
            onUploadRightAwayChange = gitStateCtx.uploadRightAwayChanged,
            remoteActionsEnabled = remoteActionsEnabled,
            canCancelOperation = canCancelOperation,
            canOpenRemoteRepository = gitStateCtx.state.OriginRemoteRepositoryWebUrl.IsSome,
            onSubmitPublishRename = gitStateCtx.submitPublishRename,
            onCancelPublishRename = gitStateCtx.cancelPublishRename,
            onOpenRemoteRepository =
                (fun () ->
                    match gitStateCtx.state.OriginRemoteRepositoryWebUrl with
                    | Some repositoryWebUrl -> window.``open`` (repositoryWebUrl, "_blank") |> ignore
                    | None -> ()
                ),
            ?remoteActionsWarning = remoteActionsWarning
        )
