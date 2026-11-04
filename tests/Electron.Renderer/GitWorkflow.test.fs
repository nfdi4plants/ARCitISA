module ElectronRenderer.GitWorkflowTests

open System
open Browser.Dom
open Browser.Types
open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Renderer.Context.GitWorkflow
open Renderer.Types
open Swate.Components.Api.GitLabApi
open Swate.Components.Page.GitSidebarTypes
open Swate.Electron.Shared.VersionControlTypes
open Vitest

[<Import("renderToStaticMarkup", "react-dom/server")>]
let private renderToStaticMarkup (element: ReactElement) : string = jsNative

let private localBranch name current _ = {
    Name = name
    ProviderRef = $"git-local:{name}"
    Kind = RefKindDto.Local
    IsCurrent = current
}

let private remoteBranch name = {
    Name = $"origin/{name}"
    ProviderRef = $"git-remote:origin/{name}"
    Kind = RefKindDto.Remote
    IsCurrent = false
}

let private cleanStatus: WorkspaceStatusDto = {
    CurrentRef = Some(localBranch "main" true true)
    WorkspaceVersion = "v1"
    Changes = [||]
    ActiveConflictSession = None
    Synchronization =
        Some {
            BaseRevision = None
            WorkspaceRevision = Some "rev"
            TargetRevision = Some "target"
            TargetRef = Some(remoteBranch "main")
            LocalRevisionCount = Some 0
            TargetRevisionCount = Some 0
            RemoteChangedPaths = None
            Relationship = RevisionRelationshipDto.UpToDate
        }
}

let private statusForBranch name = {
    cleanStatus with
        CurrentRef = Some(localBranch name true true)
}

let private conflictedStatus paths = {
    cleanStatus with
        Changes =
            paths
            |> Array.map (fun path -> {
                Path = path
                OldPath = None
                Kind = FileChangeKindDto.Conflicted
            })
        ActiveConflictSession =
            Some {
                Handle = {
                    SessionId = "conflict-1"
                    Version = "1"
                }
                Items =
                    paths
                    |> Array.map (fun path -> {
                        Path = path
                        Candidates = [||]
                        CombinedPreview =
                            Some(ContentViewDto.Text "<<<<<<< HEAD\nA\n=======\nB\n>>>>>>> origin/main\n")
                        SupportsResolvedContent = true
                    })
            }
}

let private operationWithPublication publication value : OperationOutcomeDto<'T> = {
    Value = value
    Effect = OperationEffectDto.Performed
    Warnings = [||]
    AffectedPaths = [||]
    ResultingRevision = None
    ResultingWorkspaceVersion = None
    Publication = publication
}

let private operation value : OperationOutcomeDto<'T> =
    operationWithPublication PublicationStateDto.NotApplicable value

let private succeededWithWarnings warnings value : OperationResultDto<'T> =
    OperationResultDto.Succeeded {
        operation value with
            Warnings = warnings
    }

let private succeeded value : OperationResultDto<'T> =
    OperationResultDto.Succeeded(operation value)

let private succeededWithPublication publication value : OperationResultDto<'T> =
    OperationResultDto.Succeeded(operationWithPublication publication value)

let private makeFailure category code message recovery paths : OperationFailureDto = {
    Category = category
    Code = code
    Message = message
    StateChanged = false
    Retryable = false
    AffectedPaths = paths
    RecoveryAction = recovery
    Details = [||]
    RevisionEvidence = [||]
    DiffDetail = None
}

let private failed category code message : OperationResultDto<'T> =
    OperationResultDto.Failed(makeFailure category code message None [||])

let private staleTokenReply<'T> message : JS.Promise<Result<OperationResultDto<'T>, string>> = promise {
    return Ok(failed Concurrency VersionControlCodes.PreconditionFailed message)
}

let private lfsSettings n materialize : StoragePolicySettingsDto = {
    AutoPolicyThresholdMb = Some n
    MaterializeLargeObjects = materialize
    DiffIndexingLimitMb = None
}

let private sessionInfo: WorkspaceSessionInfoDto = {
    SessionId = "session-1"
    ProviderId = "git"
    WorkspaceRoot = "C:/arc"
    Location = None
    Services = {
        Synchronization = true
        TextDiff = true
        ConflictResolution = true
        ObjectMaterialization = true
        StoragePolicy = true
        Maintenance = true
        RepositoryBrowser = true
    }
}

let private refs = [|
    localBranch "main" true true
    localBranch "feature" false false
    remoteBranch "main"
|]

let private remoteProject: ExploreProjectDto = {
    id = 123
    name = "my-arc"
    path_with_namespace = "carol/my-arc"
    name_with_namespace = "carol / my-arc"
    description = None
    web_url = "https://gitlab.example/carol/my-arc"
    http_url_to_repo = "https://gitlab.example/carol/my-arc.git"
    ssh_url_to_repo = None
    avatar_url = None
    visibility = Some "private"
    star_count = 0
    created_at = None
    last_activity_at = None
    tag_list = [||]
    ``namespace`` = {
        id = 5
        name = "carol"
        kind = "user"
        full_path = "carol"
    }
}

let private changedFile path i w c : GitSidebarChange = {
    Path = path
    OriginalPath = None
    IndexStatus = i
    WorkingTreeStatus = w
    IsConflicted = c
}

let private sidebarLocalBranch name current tracking : GitSidebarBranchOption = {
    RefName = name
    DisplayLabel = name
    Kind = GitSidebarBranchKind.Local
    IsCurrent = current
    IsTracking = tracking
}

let private sidebarRemoteBranch name tracking : GitSidebarBranchOption = {
    RefName = name
    DisplayLabel = name
    Kind = GitSidebarBranchKind.Remote
    IsCurrent = false
    IsTracking = tracking
}

let private progressDto phaseCode displayMessage completed total : VersionControlProgressDto = {
    OperationId = "op-1"
    PhaseCode = phaseCode
    Item = None
    Completed = completed
    Total = total
    DisplayMessage = displayMessage
}

let private sidebarProgress stage percent =
    progressDto "git" (Some stage) (Some percent) (Some 100.0)

let private sidebarOutput output =
    progressDto "git" (Some output) None None

let private expectedSidebarProgress stage percent : GitSidebarProgress = {
    Method = None
    Stage = Some stage
    ProgressPercent = Some percent
}

let private outputOf operationId (lines: (string * string) list) : VersionControlOutputDto = {
    OperationId = operationId
    Entries =
        lines
        |> List.map (fun (kind, text) -> {
            CommandId = "cmd-1"
            Kind = kind
            Text = text
            IsPartial = false
        })
        |> List.toArray
}

let private finishedLog label : Renderer.Context.OperationLog.FinishedOperationLog = {
    Label = label
    ArcName = Some "earlier-arc"
    FinishedAt = 1.0
    Log =
        Renderer.Context.OperationLog.append
            (outputOf "earlier-1" [ "stdout", "earlier line" ]).Entries
            Renderer.Context.OperationLog.empty
}

let private sidebarStep label detail percent isCurrent : GitSidebarStep = {
    Label = label
    Detail = detail
    Percent = percent
    IsCurrent = isCurrent
}

[<Emit("new Event($0, { bubbles: true })")>]
let private createEvent (eventType: string) : Browser.Types.Event = jsNative

[<Emit("new MouseEvent($0, $1)")>]
let private createMouseEvent (eventType: string) (eventInit: obj) : Browser.Types.MouseEvent = jsNative

[<Emit("""
const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value").set;
setter.call($0, $1);
$0.dispatchEvent(new Event("input", { bubbles: true }));
""")>]
let private setTextAreaValue (element: HTMLTextAreaElement) (value: string) : unit = jsNative

[<Emit("""
const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
setter.call($0, $1);
$0.dispatchEvent(new Event("input", { bubbles: true }));
""")>]
let private setInputValue (element: HTMLInputElement) (value: string) : unit = jsNative

[<Emit("""
globalThis.__swateOriginalFetch = globalThis.fetch;
globalThis.__swateGitLabCreateProjectFetches = [];
globalThis.fetch = async (url, options) => {
  const body = options && options.body ? JSON.parse(options.body) : null;
  globalThis.__swateGitLabCreateProjectFetches.push({ url, options, body });
  return $0(body);
};
""")>]
let private installGitLabCreateProjectFetchSpy (response: obj -> obj) : unit = jsNative

[<Emit("globalThis.__swateGitLabCreateProjectFetches[globalThis.__swateGitLabCreateProjectFetches.length - 1].body")>]
let private lastGitLabCreateProjectBody () : obj = jsNative

[<Emit("Object.prototype.hasOwnProperty.call($0, $1)")>]
let private hasOwnProperty (target: obj) (propertyName: string) : bool = jsNative

[<Emit("$0.firstElementChild")>]
let private firstElementChild (target: HTMLElement) : HTMLElement = jsNative

[<Emit("Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent && button.textContent.includes($0))")>]
let private findBodyButtonContaining (text: string) : HTMLButtonElement option = jsNative

[<Emit("$0[$1]")>]
let private getProperty<'T> (target: obj) (propertyName: string) : 'T = jsNative

[<Emit("""
if (globalThis.__swateOriginalFetch === undefined) {
  delete globalThis.fetch;
} else {
  globalThis.fetch = globalThis.__swateOriginalFetch;
}
delete globalThis.__swateOriginalFetch;
delete globalThis.__swateGitLabCreateProjectFetches;
""")>]
let private cleanupGitLabCreateProjectFetchSpy () : unit = jsNative

let private manyChangedFiles count = [|
    for i in 0 .. count - 1 -> changedFile (sprintf "src/file-%03i.txt" i) "M" " " false
|]

let private buildSingleConflictDocument currentLines incomingLines =
    [
        "<<<<<<< HEAD"
        yield! currentLines
        "======="
        yield! incomingLines
        ">>>>>>> origin/main"
        ""
    ]
    |> String.concat "\n"

let private unexpectedPromise<'T> name : JS.Promise<Result<'T, string>> = promise { return failwith name }
let private unexpectedGitLab<'T> name : JS.Promise<Result<'T, CreateProjectError>> = promise { return failwith name }

let private conflictPage path =
    PageState.GitMergeConflictPage {
        Path = path
        ConflictContent = ""
        Handle = {
            SessionId = "conflict-session"
            Version = "1"
        }
        WorkspaceVersion = "1"
    }

let private defaultDependencies: GitDependencies = {
    getSessionInfo = fun _ -> promise { return Ok(succeeded sessionInfo) }
    getStatus = fun _ -> unexpectedPromise "getStatus"
    listRefs = fun _ -> unexpectedPromise "listRefs"
    getRepositoryWebUrl = fun _ -> promise { return Ok(succeeded None) }
    getStoragePolicySettings = fun _ -> unexpectedPromise "getStoragePolicySettings"
    setStoragePolicySettings = fun _ -> unexpectedPromise "setStoragePolicySettings"
    // The main process holds no stored value by default, so a refresh keeps the checkbox it has.
    getUploadRightAway =
        fun () -> promise { return Ok(failed NotFound VersionControlCodes.SessionUnavailable "no stored value") }
    setUploadRightAway = fun _ -> promise { return Ok(succeeded ()) }
    loadConflictPage = fun _ _ _ -> unexpectedPromise "loadConflictPage"
    updatePageState = ignore
    initializeWorkspace = fun _ -> unexpectedPromise "initializeWorkspace"
    bindWorkspace = fun _ -> unexpectedPromise "bindWorkspace"
    createRemoteProject = fun _ -> unexpectedGitLab "createRemoteProject"
    // An account without groups creates the repository in its personal namespace without a dialog.
    loadPublishOwners = fun () -> promise { return Ok { username = "carol"; groups = [||] } }
    renameOpenArcRoot = fun _ -> unexpectedPromise "renameOpenArcRoot"
    checkDependencies = fun _ -> unexpectedPromise "checkDependencies"
    installDependency = fun _ -> unexpectedPromise "installDependency"
    refreshSynchronization = fun _ -> unexpectedPromise "refreshSynchronization"
    synchronize = fun _ -> unexpectedPromise "synchronize"
    cancelOperation = fun _ -> unexpectedPromise "cancelOperation"
    cloneWorkspace = fun _ -> unexpectedPromise "cloneWorkspace"
    createRef = fun _ -> unexpectedPromise "createRef"
    preflightSwitchRef = fun _ -> unexpectedPromise "preflightSwitchRef"
    switchRef = fun _ -> unexpectedPromise "switchRef"
    createRevision = fun _ -> unexpectedPromise "createRevision"
    restorePaths = fun _ -> unexpectedPromise "restorePaths"
    resolveConflict = fun _ -> unexpectedPromise "resolveConflict"
    finalizeConflict = fun _ -> unexpectedPromise "finalizeConflict"
    cancelConflict = fun _ -> unexpectedPromise "cancelConflict"
    listObjects = fun _ -> unexpectedPromise "listObjects"
    materializeObjects = fun _ -> unexpectedPromise "materializeObjects"
    pruneStorage = fun _ -> unexpectedPromise "pruneStorage"
    deduplicateStorage = fun _ -> unexpectedPromise "deduplicateStorage"
    clearStaleLock = fun _ -> unexpectedPromise "clearStaleLock"
    textDiff = {
        openTextDiff = fun _ -> unexpectedPromise "openTextDiff"
        readTextDiffPage = fun _ -> unexpectedPromise "readTextDiffPage"
        replayTextDiffPage = fun _ -> unexpectedPromise "replayTextDiffPage"
        expandTextDiff = fun _ -> unexpectedPromise "expandTextDiff"
        readTextDiffLine = fun _ -> unexpectedPromise "readTextDiffLine"
        closeTextDiff = fun _ -> unexpectedPromise "closeTextDiff"
    }
    hasUsableAccount = fun () -> true
    delay = fun milliseconds -> Promise.sleep milliseconds
    newOperationId = fun () -> "op-1"
    confirmLfsPrune = fun _ -> false
    confirmInstall = fun _ -> false
    reportError = fun _ -> ()
}

let private withRefresh (status: WorkspaceStatusDto) (deps: GitDependencies) : GitDependencies = {
    deps with
        getStatus = fun _ -> promise { return Ok(succeeded status) }
        listRefs = fun _ -> promise { return Ok(succeeded refs) }
        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
}

let private renderToBody element = promise {
    let c = document.createElement ("div") :?> HTMLDivElement
    document.body.appendChild c |> ignore
    let r = ReactDOM.createRoot c
    r.render element
    do! Promise.sleep 0

    return
        c,
        (fun () ->
            r.unmount ()
            c.remove ()
        )
}

let private noopCallbacks: GitSidebarCallbacks = {
    OnRefresh = fun () -> ()
    OnFetch = fun () -> ()
    OnPush = fun () -> ()
    OnUpdateFromOnline = fun () -> ()
    OnPrimarySaveSelection = fun _ -> ()
    OnPrimarySaveAll = fun _ -> ()
    OnCommitSelection = fun _ -> ()
    OnCommitAll = fun _ -> ()
    OnDiscardSelection = fun _ -> ()
    OnConfirmPendingRemoteAction = fun () -> ()
    OnCancelPendingRemoteAction = fun () -> ()
    OnSaveDownloadLargeFiles = fun _ -> ()
    OnSaveLfsAutoTrackThreshold = fun _ -> ()
    OnSaveDiffIndexingLimit = fun _ -> ()
    OnCreateBranch = fun _ -> ()
    OnSwitchBranch = fun _ -> ()
    OnSelectChange = fun _ -> promise { return Ok() }
    OnPruneLfsCache = fun () -> ()
    OnDedupLfsStorage = fun () -> ()
    OnCancelOperation = fun () -> ()
}

Vitest.afterEach (fun () -> document.body.innerHTML <- "")

let private collectMessages cmd = promise {
    let messages = ResizeArray<Msg>()
    cmd |> List.iter (fun sub -> sub messages.Add)
    do! Promise.sleep 0
    return messages |> Seq.toArray
}

let private hasMessage predicate messages = Array.exists predicate messages

/// A primary save reports its phases before it completes. Tests that only care about the
/// completion collect through this helper.
let private collectWriteMessages cmd = promise {
    let! messages = collectMessages cmd

    return
        messages
        |> Array.filter (fun message ->
            match message with
            | WritePhaseChanged _ -> false
            | _ -> true
        )
}

let private runningState = {
    GitState.Empty with
        CurrentArcPath = Some "C:/arc"
        ArcSessionId = 1
        WorkspaceVersion = Some "v1"
}

let private applyProgressEvents (state: GitState) (events: VersionControlProgressDto list) =
    events
    |> List.fold
        (fun current event -> fst (update defaultDependencies ignore (SetCurrentProgress(event)) current))
        state

/// Every value differs from the built-in one.
let private changedUserDefaults = {
    builtInUserDefaults with
        LfsAutoTrackThresholdMb = 7
        DownloadLargeFiles = true
        DiffIndexingLimitMb = 2048
        UploadRightAway = false
        AutoCreateNotesFolder = false
}

let private noOwnerSelection: GitPublishOwnerSelection = { Owners = None; Choice = None }

let private publishGroup groupId path canCreate level : PublishGroupDto = {
    id = groupId
    name = path
    full_path = path
    project_creation_level = level
    can_create_projects = canCreate
}

let private publishOwnersDto groups : PublishOwnersDto = { username = "carol"; groups = groups }

/// Applies one message and collects the messages its command dispatches.
let private step deps model message = promise {
    let next, cmd = update deps ignore message model
    let! messages = collectMessages cmd
    return next, messages
}

/// Runs a push and applies its completion.
let private runPush deps model = promise {
    let! writing, messages = step deps model (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
    let! finished, _ = step deps writing messages[0]
    return finished
}

/// Runs the refresh a renamed ARC path starts. It holds the publish back until the workspace
/// token is loaded, then the publish runs.
let private runRefreshAndPublish deps model = promise {
    let! refreshing, refreshMessages = step deps model RefreshRequested
    let! refreshed, afterRefresh = step deps refreshing refreshMessages[0]

    Vitest
        .expect(afterRefresh)
        .toEqual (
            [|
                WriteRequested(Push GitUpdateAcceptance.RequirePreview)
            |]
        )

    return! runPush deps refreshed
}

/// Dependencies of a first publish. The synchronize reports the missing publish target until
/// the workspace is bound. The two calls under test are passed in.
let private firstPublishDeps loadOwners createProject : GitDependencies =
    let bindCalls = ResizeArray<string>()
    let sync = cleanStatus.Synchronization.Value

    {
        (withRefresh cleanStatus defaultDependencies) with
            synchronize =
                fun _ ->
                    if bindCalls.Count = 0 then
                        promise {
                            return
                                Ok(failed Validation VersionControlCodes.PublishTargetMissing "publish target missing")
                        }
                    else
                        promise { return Ok(succeededWithPublication PublicationStateDto.Published sync) }
            bindWorkspace =
                fun _ ->
                    bindCalls.Add "bindWorkspace"
                    promise { return Ok(succeeded sessionInfo) }
            loadPublishOwners = loadOwners
            createRemoteProject = createProject
    }

let private requestAndConfirmDiscard deps state paths =
    let pendingConfirmation, _ =
        update deps ignore (DiscardSelectionRequested paths) state

    update deps ignore ConfirmPendingRemoteActionRequested pendingConfirmation

let private completeOne deps model cmd = promise {
    let! messages = collectMessages cmd

    match messages with
    | [| message |] -> return update deps ignore message model
    | _ -> return failwith "Expected one command message."
}

/// The listing of a workspace without large objects.
let private noObjects (_: OperationRequestDto) = promise { return Ok(succeeded ([||]: ObjectStateDto[])) }

let private objectState path isMaterialized : ObjectStateDto = {
    Path = path
    IsMaterialized = isMaterialized
    IsLocallyAvailable = isMaterialized
    SizeBytes = Some 100.
    ObjectId = Some $"object-{path}"
}

/// The listing of a workspace with one large object that is not on disk.
let private oneMissingObject (_: OperationRequestDto) = promise {
    return Ok(succeeded [| objectState "missing.bin" false |])
}

let private statusWithChanges (changes: (string * FileChangeKindDto)[]) : WorkspaceStatusDto = {
    cleanStatus with
        Changes =
            changes
            |> Array.map (fun (path, kind) -> {
                Path = path
                OldPath = None
                Kind = kind
            })
}

/// Requests the lock removal and returns the busy state with the messages the write completes with.
let private runClearWrite deps = promise {
    let requested, requestCmd =
        update deps ignore (WriteRequested ClearStaleLock) runningState

    let! completion = collectMessages requestCmd
    return requested, completion
}

/// A large-file download that ended with a held index lock. The code tells the download
/// from other partial failures.
let private lockedPartial code =
    makeFailure
        ProviderError
        code
        "The large-file download did not finish."
        (Some {
            Code = VersionControlCodes.Recovery.RemoveIndexLock
            Instructions = None
        })
        [||]

/// Runs a Download Changes whose update succeeded and whose partial failure is the given one.
let private runPullWithPartial (partial: OperationFailureDto) = promise {
    let deps = {
        (withRefresh cleanStatus defaultDependencies) with
            listObjects = noObjects
            synchronize =
                fun _ -> promise {
                    return
                        Ok(OperationResultDto.PartiallySucceeded(operation cleanStatus.Synchronization.Value, partial))
                }
    }

    let requested, requestCmd =
        update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

    let! completion = collectMessages requestCmd

    match completion with
    | [| WriteCompleted(_, _, Pull _, Ok(Completed _)) |] ->
        let finished, finishCmd = update deps ignore completion[0] requested
        let! _ = collectMessages finishCmd
        return deps, finished
    | _ -> return failwith "Expected the download to complete with its partial failure."
}

/// Confirms the lock dialog of the state and finishes the lock removal with the given result.
/// Returns the finished state with the messages that its completion produced.
let private confirmLockRemovalWithMessages deps (lockedState: GitState) clearResult = promise {
    let deps = {
        deps with
            clearStaleLock = fun _ -> promise { return clearResult }
    }

    let confirmed, confirmCmd =
        update deps ignore ConfirmPendingRemoteActionRequested lockedState

    let! confirmMessages = collectMessages confirmCmd

    let lockRequested, lockRequestedCmd =
        update deps ignore confirmMessages[0] confirmed

    let! requestedMessages = collectMessages lockRequestedCmd
    let clearing, clearCmd = update deps ignore requestedMessages[0] lockRequested
    let! clearMessages = collectMessages clearCmd
    let finished, finishCmd = update deps ignore clearMessages[0] clearing
    let! finishMessages = collectMessages finishCmd
    return finished, finishMessages
}

let private confirmLockRemoval deps (lockedState: GitState) clearResult = promise {
    let! finished, _ = confirmLockRemovalWithMessages deps lockedState clearResult
    return finished
}

let private refreshed status = {
    Session = Ok sessionInfo
    Status = Ok status
    Refs = Ok refs
    LfsSettings = Ok(lfsSettings 5 true)
    OriginRemoteRepositoryWebUrl = None
}

Vitest.describe (
    "GitWorkflow wording",
    fun () ->
        Vitest.test (
            "progress uses a display message, then the current stage, then BusyNotice, and keeps its percent",
            fun () ->
                let busyState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        BusyNotice = Some "Pushing to remote"
                }

                let progressWithoutDisplay = {
                    OperationId = "op-1"
                    PhaseCode = "git"
                    Item = None
                    Completed = Some 3.0
                    Total = Some 4.0
                    DisplayMessage = None
                }

                let fallbackState, _ =
                    update defaultDependencies ignore (SetCurrentProgress(progressWithoutDisplay)) busyState

                Vitest.expect(fallbackState.CurrentProgress.Value.Stage).toEqual (Some "Pushing to remote")
                Vitest.expect(fallbackState.CurrentProgress.Value.Stage).not.toEqual (Some "git")
                Vitest.expect(fallbackState.CurrentProgress.Value.ProgressPercent).toEqual (Some 75.0)

                let progressWithDisplay = {
                    progressWithoutDisplay with
                        DisplayMessage = Some "Uploading objects"
                }

                let displayState, _ =
                    update defaultDependencies ignore (SetCurrentProgress(progressWithDisplay)) busyState

                Vitest.expect(displayState.CurrentProgress.Value.Stage).toEqual (Some "Uploading objects")

                let busyNoticeState, _ =
                    update defaultDependencies ignore (SetCurrentProgress(progressWithoutDisplay)) displayState

                // A later update without a display message keeps the stage it already shows.
                Vitest.expect(busyNoticeState.CurrentProgress.Value.Stage).toEqual (Some "Uploading objects")
        )

        Vitest.test (
            "progress starts a new stage and percent when the phase changes",
            fun () ->
                let busyState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        BusyNotice = Some "Pushing to remote"
                }

                let lfsListState, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(progressDto "lfs-list" None (Some 10.0) (Some 10.0)))
                        busyState

                Vitest.expect(lfsListState.CurrentProgress.Value.ProgressPercent).toEqual (Some 100.0)

                let lfsUploadState, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(
                            progressDto "lfs-upload" (Some "Uploading LFS objects (0/1)") (Some 0.0) (Some 100.0)
                        ))
                        lfsListState

                Vitest.expect(lfsUploadState.CurrentProgress.Value.ProgressPercent).toEqual (Some 0.0)
                Vitest.expect(lfsUploadState.CurrentProgress.Value.Stage).toEqual (Some "Uploading LFS objects (0/1)")
        )

        Vitest.test (
            "progress keeps values within a phase and clears them for the next phase",
            fun () ->
                let busyState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        BusyNotice = Some "Pushing to remote"
                }

                let firstState, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(

                            progressDto "lfs-upload" (Some "Uploading LFS objects (45/100)") (Some 45.0) (Some 100.0)

                        ))
                        busyState

                let samePhaseState, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(progressDto "lfs-upload" None None None))
                        firstState

                Vitest.expect(samePhaseState.CurrentProgress.Value.ProgressPercent).toEqual (Some 45.0)

                Vitest
                    .expect(samePhaseState.CurrentProgress.Value.Stage)
                    .toEqual (Some "Uploading LFS objects (45/100)")

                let nextPhaseState, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(progressDto "git" None None None))
                        samePhaseState

                Vitest.expect(nextPhaseState.CurrentProgress.Value.ProgressPercent).toEqual (None)

                // The meter started a step, so the reset keeps the headline on the label of that step.
                Vitest.expect(nextPhaseState.CurrentProgress.Value.Stage).toEqual (Some "Uploading LFS objects")
        )

        Vitest.test (
            "a reset that starts a phase keeps the headline on the current step",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                }

                let fetching =
                    applyProgressEvents pullState [
                        progressDto "fetch" (Some "Fetching online changes") None None
                        progressDto "fetch" (Some "Receiving objects (12/80)") (Some 15.0) (Some 100.0)
                    ]

                let reset =
                    applyProgressEvents fetching [ progressDto "refresh-state" None None None ]

                Vitest.expect(reset.CurrentProgress.Value.Stage).toEqual (Some "Fetching online changes")
                Vitest.expect(reset.CurrentProgress.Value.ProgressPercent).toEqual None
        )

        Vitest.test (
            "a reset that starts a phase shows the busy notice while the operation has no step",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                }

                let reset =
                    applyProgressEvents pullState [ progressDto "refresh-state" None None None ]

                Vitest.expect(reset.CurrentSteps.Length).toBe 0
                Vitest.expect(reset.CurrentProgress.Value.Stage).toEqual pullState.BusyNotice
        )

        Vitest.test (
            "a held index lock without a commit keeps the library's instructions",
            fun () ->
                let failure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.IndexLocked
                        "Another git process holds the repository index, or a previous one left its lock behind."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions =
                                Some
                                    "Make sure no git process is running on the repository, remove the lock file, then retry."
                        })
                        [||]

                Vitest
                    .expect(failureMessage failure)
                    .toBe (
                        "Another git process holds the repository index, or a previous one left its lock behind. Make sure no git process is running on the repository, remove the lock file, then retry."
                    )
        )

        Vitest.test (
            "index_reconciliation_failed uses Swate's recovery instructions",
            fun () ->
                let failure =
                    makeFailure
                        ProviderError
                        "index_reconciliation_failed"
                        "The selected changes were committed, but the index could not be reconciled."
                        (Some {
                            Code = VersionControlCodes.Recovery.ReconcileIndex
                            Instructions =
                                Some "Reconcile the affected paths in the index (for example with git reset)."
                        })
                        [||]

                Vitest
                    .expect(failureMessage failure)
                    .toBe (
                        "The selected changes were committed, but the index could not be reconciled. The changes are saved in a commit, but Git's file index is out of date. Close other Git programs for this ARC, then run \"git reset\" in the ARC folder."
                    )

                let attributesFailure =
                    makeFailure
                        ProviderError
                        "attributes_reconciliation_failed"
                        "The attributes update failed."
                        (Some {
                            Code = VersionControlCodes.Recovery.ReconcileIndex
                            Instructions = Some "Retry the attributes update."
                        })
                        [||]

                Vitest
                    .expect(failureMessage attributesFailure)
                    .toBe ("The attributes update failed. Retry the attributes update.")
        )

        Vitest.test (
            "hasRemote uses synchronization and listed refs without a web URL",
            fun () ->
                let synchronizedState = {
                    GitState.Empty with
                        Status = mapStatus cleanStatus
                }

                let listedRemoteState = {
                    GitState.Empty with
                        Refs = [| remoteBranch "main" |]
                        OriginRemoteRepositoryWebUrl = None
                }

                Vitest.expect(hasRemote synchronizedState).toBe (true)
                Vitest.expect(hasRemote listedRemoteState).toBe (true)
        )
)

Vitest.describe (
    "File-choice conflict helpers",
    fun () ->
        Vitest.test (
            "candidate conversion carries stored-object state and shortens revisions",
            fun () ->
                let withObject = {
                    CandidateId = "target"
                    Label = "Online"
                    Revision = Some "e8955bc3f9"
                    Preview = None
                    Object =
                        Some {
                            SizeBytes = Some 3250585.6
                            ObjectId = Some "sha256:abc"
                            IsLocallyAvailable = false
                        }
                }

                let withoutObject = {
                    CandidateId = "workspace"
                    Label = "Workspace"
                    Revision = None
                    Preview = None
                    Object = None
                }

                Vitest.expect(candidateToFileChoiceVersion withObject).toEqual {
                    CandidateId = "target"
                    SizeBytes = Some 3250585.6
                    IsDownloaded = Some false
                    Revision = Some "e8955bc"
                    IsDeleted = false
                }

                Vitest.expect(candidateToFileChoiceVersion withoutObject).toEqual {
                    CandidateId = "workspace"
                    SizeBytes = None
                    IsDownloaded = None
                    Revision = None
                    IsDeleted = true
                }
        )

        Vitest.test (
            "version lines omit unknown details and render deleted versions",
            fun () ->
                let yourVersion: FileChoiceVersion = {
                    CandidateId = "workspace"
                    SizeBytes = Some(3.1 * 1024.0 * 1024.0)
                    IsDownloaded = Some true
                    Revision = None
                    IsDeleted = false
                }

                let onlineVersion = {
                    yourVersion with
                        CandidateId = "target"
                        IsDownloaded = Some false
                        Revision = Some "e8955bc"
                }

                let unknownVersion: FileChoiceVersion = {
                    CandidateId = "workspace"
                    SizeBytes = None
                    IsDownloaded = None
                    Revision = None
                    IsDeleted = false
                }

                Vitest
                    .expect(versionLine "Your version" false yourVersion)
                    .toEqual (Some "Your version: 3.1 MB, downloaded")

                Vitest
                    .expect(versionLine "Online version" true onlineVersion)
                    .toEqual (Some "Online version: 3.1 MB, not downloaded, from e8955bc")

                Vitest.expect(versionLine "Your version" false unknownVersion).toEqual None

                Vitest
                    .expect(versionLine "Online version" true { unknownVersion with IsDeleted = true })
                    .toEqual (Some "Online version: deleted")
        )

        Vitest.test (
            "conflictPageFor selects file choice and merge pages",
            fun () ->
                let workspaceCandidate: ConflictCandidateDto = {
                    CandidateId = keepMineCandidateId
                    Label = "Workspace"
                    Revision = None
                    Preview = None
                    Object = None
                }

                let targetCandidate: ConflictCandidateDto = {
                    CandidateId = useOnlineCandidateId
                    Label = "Online"
                    Revision = Some "e8955bc"
                    Preview = None
                    Object = None
                }

                let fileChoiceConflict: ConflictSessionSummaryDto = {
                    Handle = {
                        SessionId = "conflict-1"
                        Version = "1"
                    }
                    Items = [|
                        {
                            Path = "data/file.bin"
                            Candidates = [| workspaceCandidate; targetCandidate |]
                            CombinedPreview = Some(ContentViewDto.Unsupported None)
                            SupportsResolvedContent = false
                        }
                    |]
                }

                match conflictPageFor fileChoiceConflict "v1" "data/file.bin" with
                | Ok(PageState.GitFileChoiceConflictPage page) ->
                    Vitest.expect(page.Mine.IsDeleted).toBe (true)
                    Vitest.expect(page.Online.IsDeleted).toBe (true)
                | _ -> failwith "Expected the file-choice conflict page."

                let modifiedWorkspaceCandidate = {
                    workspaceCandidate with
                        Preview = Some(ContentViewDto.Text "workspace")
                }

                let textModifyDeleteConflict = {
                    fileChoiceConflict with
                        Items = [|
                            {
                                fileChoiceConflict.Items[0] with
                                    Candidates = [| modifiedWorkspaceCandidate; targetCandidate |]
                                    CombinedPreview = Some(ContentViewDto.Text "combined")
                                    SupportsResolvedContent = true
                            }
                        |]
                }

                match conflictPageFor textModifyDeleteConflict "v1" "data/file.bin" with
                | Ok(PageState.GitFileChoiceConflictPage page) ->
                    Vitest.expect(page.Mine.IsDeleted).toBe (false)
                    Vitest.expect(page.Online.IsDeleted).toBe (true)
                | _ -> failwith "Expected the text modify/delete file-choice page."

                let textConflict = {
                    textModifyDeleteConflict with
                        Items = [|
                            {
                                textModifyDeleteConflict.Items[0] with
                                    Candidates = [|
                                        modifiedWorkspaceCandidate
                                        {
                                            targetCandidate with
                                                Preview = Some(ContentViewDto.Text "online")
                                        }
                                    |]
                            }
                        |]
                }

                match conflictPageFor textConflict "v1" "data/file.bin" with
                | Ok(PageState.GitMergeConflictPage page) -> Vitest.expect(page.ConflictContent).toBe "combined"
                | _ -> failwith "Expected the text merge conflict page."
        )

        Vitest.test (
            "pickRequestFor maps choices and carries the reviewed page context",
            fun () ->
                let page: VersionControlFileChoicePage = {
                    Path = "data/file.bin"
                    Handle = {
                        SessionId = "conflict-page"
                        Version = "5"
                    }
                    WorkspaceVersion = "workspace-v7"
                    Mine = {
                        CandidateId = "workspace"
                        SizeBytes = None
                        IsDownloaded = None
                        Revision = None
                        IsDeleted = false
                    }
                    Online = {
                        CandidateId = "target"
                        SizeBytes = None
                        IsDownloaded = None
                        Revision = None
                        IsDeleted = false
                    }
                }

                Vitest.expect(pickRequestFor page KeepMine).toEqual {
                    Path = "data/file.bin"
                    Handle = page.Handle
                    WorkspaceVersion = "workspace-v7"
                    Resolution = ConflictResolutionDto.PickCandidate "workspace"
                }

                Vitest.expect(pickRequestFor page UseOnline).toEqual {
                    Path = "data/file.bin"
                    Handle = page.Handle
                    WorkspaceVersion = "workspace-v7"
                    Resolution = ConflictResolutionDto.PickCandidate "target"
                }
        )
)

Vitest.describe (
    "GitWorkflow request preparation",
    fun () ->
        Vitest.test (
            "prepareCommitAll snapshots distinct changed paths from the current model",
            fun () ->
                let state = {
                    GitState.Empty with
                        ChangedFiles = [|
                            changedFile "a.txt" "M" " " false
                            changedFile "b.txt" "M" " " false
                            changedFile "a.txt" "M" " " false
                        |]
                }

                let prepared = prepareCommitAll state "  save everything  "

                Vitest.expect(prepared.NormalizedMessage).toBe ("save everything")
                Vitest.expect(prepared.PathsToCommit).toEqual ([| "a.txt"; "b.txt" |])
                Vitest.expect(prepared.BusyOperation).toEqual (GitBusyOperation.CommittingAllChanges)
        )

        Vitest.test (
            "prepareCommitSelection keeps the selected paths exact and drops duplicates and empty entries",
            fun () ->
                let prepared =
                    prepareCommitSelection {
                        Message = " save "
                        Paths = [| " leading-space.txt"; "b.txt"; ""; "b.txt" |]
                    }

                Vitest.expect(prepared.NormalizedMessage).toBe ("save")
                Vitest.expect(prepared.PathsToCommit).toEqual ([| " leading-space.txt"; "b.txt" |])
                Vitest.expect(prepared.BusyOperation).toEqual (GitBusyOperation.CommittingSelectedChanges)
        )

        Vitest.test (
            "buildUpdatedLfsSettings keeps untouched values from the current model snapshot",
            fun () ->
                let state = {
                    GitState.Empty with
                        LfsAutoTrackThresholdMb = 7
                        DownloadLargeFiles = true
                }

                Vitest
                    .expect(buildUpdatedLfsSettings state (Some 4) None None)
                    .toEqual (
                        {
                            AutoPolicyThresholdMb = Some 4
                            MaterializeLargeObjects = true
                            DiffIndexingLimitMb = Some 1024
                        }
                    )

                Vitest
                    .expect(buildUpdatedLfsSettings state None (Some false) None)
                    .toEqual (
                        {
                            AutoPolicyThresholdMb = Some 7
                            MaterializeLargeObjects = false
                            DiffIndexingLimitMb = Some 1024
                        }
                    )
        )

        Vitest.test (
            "GitLabApi.CreateProject lets GitLab generate the project path from the submitted name",
            fun () -> promise {
                installGitLabCreateProjectFetchSpy (fun body ->
                    createObj [
                        "ok" ==> true
                        "status" ==> 201
                        "headers" ==> createObj [ "get" ==> (fun _ -> null) ]
                        "json"
                        ==> (fun () -> promise {
                            return
                                createObj [
                                    "id" ==> 123
                                    "name" ==> getProperty<string> body "name"
                                    "path_with_namespace" ==> "carol/my-arc-project"
                                    "name_with_namespace" ==> "carol / " + getProperty<string> body "name"
                                    "description" ==> null
                                    "web_url" ==> "https://gitlab.example/carol/my-arc-project"
                                    "http_url_to_repo" ==> "https://gitlab.example/carol/my-arc-project.git"
                                    "ssh_url_to_repo" ==> null
                                    "avatar_url" ==> null
                                    "visibility" ==> "private"
                                    "star_count" ==> 0
                                    "created_at" ==> "2026-04-13T00:00:00Z"
                                    "last_activity_at" ==> "2026-04-13T00:00:00Z"
                                    "topics" ==> [||]
                                    "tag_list" ==> [||]
                                    "namespace"
                                    ==> createObj [
                                        "id" ==> 5
                                        "name" ==> "carol"
                                        "kind" ==> "user"
                                        "full_path" ==> "carol"
                                    ]
                                ]
                        })
                    ]
                )

                try
                    let! result =
                        GitLabApi.CreateProject(
                            "https://gitlab.example/",
                            "token-123",
                            {
                                Name = " My ARC Project "
                                NamespaceId = None
                            }
                        )

                    match result with
                    | Error _ -> failwith "Expected the project to be created."
                    | Ok project ->
                        Vitest.expect(project.name).toBe ("My ARC Project")
                        Vitest.expect(project.path_with_namespace).toBe ("carol/my-arc-project")

                    let body = lastGitLabCreateProjectBody ()

                    Vitest.expect(getProperty<string> body "name").toBe ("My ARC Project")
                    Vitest.expect(getProperty<bool> body "initialize_with_readme").toBe (false)
                    Vitest.expect(hasOwnProperty body "path").toBe (false)
                finally
                    cleanupGitLabCreateProjectFetchSpy ()
            }
        )

        Vitest.test (
            "GitLabApi.CreateProject reports GitLab's duplicate-name response as a refused name",
            fun () -> promise {
                let duplicateNameError =
                    createObj [
                        "message"
                        ==> createObj [
                            "name" ==> [| "has already been taken" |]
                            "path" ==> [| "has already been taken" |]
                        ]
                    ]

                installGitLabCreateProjectFetchSpy (fun _ ->
                    createObj [
                        "ok" ==> false
                        "status" ==> 400
                        "headers" ==> createObj [ "get" ==> (fun _ -> null) ]
                        "text"
                        ==> (fun () -> promise {
                            return
                                """{"message":{"name":["has already been taken"],"path":["has already been taken"]}}"""
                        })
                        "json" ==> (fun () -> promise { return duplicateNameError })
                    ]
                )

                try
                    let! result =
                        GitLabApi.CreateProject(
                            "https://gitlab.example/",
                            "token-123",
                            {
                                Name = "Existing ARC"
                                NamespaceId = None
                            }
                        )

                    match result with
                    | Ok _ -> failwith "Expected duplicate project creation to fail."
                    | Error(CreateProjectError.NameRefused details) ->
                        Vitest.expect(details |> Option.defaultValue "").toContain ("has already been taken")
                    | Error _ -> failwith "Expected NameRefused."
                finally
                    cleanupGitLabCreateProjectFetchSpy ()
            }
        )
)

Vitest.describe (
    "GitWorkflow LFS storage maintenance",
    fun () ->
        Vitest.test (
            "PruneLfsCacheRequested asks for confirmation before running prune",
            fun () -> promise {
                let mutable confirmedMessage = None

                let deps = {
                    defaultDependencies with
                        confirmLfsPrune =
                            fun message ->
                                confirmedMessage <- Some message
                                true
                        pruneStorage = fun _ -> promise { return Ok(succeeded "pruned") }
                }

                let model = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        ArcSessionId = 1
                }

                let nextModel, cmd = update deps ignore PruneLfsCacheRequested model
                let! _ = collectMessages cmd

                Vitest.expect(confirmedMessage.IsSome).toBe (true)
                Vitest.expect(nextModel.BusyOperation).toBe (None)
            }
        )

        Vitest.test (
            "DedupLfsStorageRequested starts dedup write operation",
            fun () -> promise {
                let model = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        ArcSessionId = 1
                }

                let nextModel, _ =
                    update defaultDependencies ignore (WriteRequested DedupLfsStorage) model

                Vitest.expect(nextModel.BusyOperation).toEqual (Some GitBusyOperation.DeduplicatingGitLfsStorage)
            }
        )
)

Vitest.describe (
    "GitWorkflow update command flow",
    fun () ->
        Vitest.test (
            "ArcPathChanged clears page state and schedules a refresh when switching repositories",
            fun () -> promise {
                let clearedPages = ResizeArray<PageState option>()

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        SelectedChangePath = Some "tracked.txt"
                        BusyOperation = Some GitBusyOperation.Refreshing
                }

                let nextState, cmd =
                    update defaultDependencies clearedPages.Add (ArcPathChanged(Some "C:/arc-b")) state

                let! messages = collectMessages cmd

                Vitest
                    .expect(nextState)
                    .toEqual (
                        {
                            GitState.Empty with
                                CurrentArcPath = Some "C:/arc-b"
                                ArcSessionId = 1
                                RefreshRequestId = 1
                                PageLoadRequestId = 1
                                ExternalRefreshGeneration = 1
                        }
                    )

                Vitest.expect(clearedPages |> Seq.toArray).toEqual ([| None |])
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "RefreshCompleted reports current failures through the Git error modal",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        RefreshRequestId = 2
                        RefreshState = GitRefreshState.Loading
                }

                let nextState, cmd =
                    update deps ignore (RefreshCompleted(2, Error "remote status failed")) state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.ErrorNotice).toEqual (Some "remote status failed")
                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(reportedErrors[0].Title).toBe ("Could not refresh Git state")
                Vitest.expect(reportedErrors[0].Message).toBe ("remote status failed")
            }
        )

        Vitest.test (
            "RefreshCompleted reports the output of the failed refresh as the log of the error",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let refreshing, _ = update defaultDependencies ignore RefreshRequested runningState

                let withOutput, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(
                            outputOf "op-1" [
                                "command", "git fetch"
                                "stderr", "fatal: unable to access the remote"
                            ]
                        ))
                        refreshing

                let _, cmd =
                    update
                        deps
                        ignore
                        (RefreshCompleted(withOutput.RefreshRequestId, Error "remote status failed"))
                        withOutput

                let! _ = collectMessages cmd

                Vitest.expect(reportedErrors.Count).toBe (1)

                Vitest.expect(reportedErrors[0].Log.Lines |> Array.map _.Text).toEqual [|
                    "git fetch"
                    "fatal: unable to access the remote"
                |]
            }
        )

        Vitest.test (
            "A silent refresh failure keeps the current notices and opens no modal",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()
                let pageStates = ResizeArray<PageState option>()

                let deps = {
                    defaultDependencies with
                        getSessionInfo = fun _ -> promise { return Error "silent refresh failed" }
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        ErrorNotice = Some "existing error"
                        WarningNotice = Some "existing warning"
                }

                let requestingState, requestCmd =
                    update deps pageStates.Add RefreshRequestedSilently state

                let! requestMessages = collectMessages requestCmd

                let nextState, nextCmd =
                    match requestMessages with
                    | [| (SilentRefreshCompleted _ as message) |] -> update deps pageStates.Add message requestingState
                    | _ -> failwith "Expected a silent refresh completion."

                let! nextMessages = collectMessages nextCmd

                Vitest.expect(nextState.ErrorNotice).toEqual (Some "existing error")
                Vitest.expect(nextState.WarningNotice).toEqual (Some "existing warning")
                Vitest.expect(reportedErrors.Count).toBe (0)
                Vitest.expect(pageStates |> Seq.toArray).toEqual ([||])
                Vitest.expect(nextMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "A failed settings read keeps the current threshold and download preference",
            fun () -> promise {
                let settingsFailure =
                    makeFailure ProviderError "settings_read_failed" "The settings read failed." None [||]

                let statusFailure =
                    makeFailure ProviderError "status_read_failed" "The status read failed." None [||]

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        RefreshRequestId = 1
                        RefreshState = GitRefreshState.Loading
                        LfsAutoTrackThresholdMb = 42
                        DownloadLargeFiles = true
                        PendingMaterializations = [ ("pending.bin", false) ]
                        PendingMaterializationSessionId = Some "conflict-1"
                }

                let settingsFailedRefresh = {
                    Session = Ok sessionInfo
                    Status = Ok cleanStatus
                    Refs = Ok refs
                    LfsSettings = Error settingsFailure
                    OriginRemoteRepositoryWebUrl = None
                }

                let firstState, firstCmd =
                    update defaultDependencies ignore (RefreshCompleted(1, Ok settingsFailedRefresh)) state

                let! _ = collectMessages firstCmd

                Vitest.expect(firstState.LfsAutoTrackThresholdMb).toBe (42)
                Vitest.expect(firstState.DownloadLargeFiles).toBe (true)
                Vitest.expect(firstState.ErrorNotice).toEqual (Some "The settings read failed.")

                let statusFailedRefresh = {
                    settingsFailedRefresh with
                        Status = Error statusFailure
                        LfsSettings = Ok(lfsSettings 9 false)
                }

                let secondState, secondCmd =
                    update defaultDependencies ignore (RefreshCompleted(1, Ok statusFailedRefresh)) state

                let! _ = collectMessages secondCmd

                Vitest.expect(secondState.LfsAutoTrackThresholdMb).toBe (9)
                Vitest.expect(secondState.DownloadLargeFiles).toBe (false)
                Vitest.expect(secondState.PendingMaterializations).toEqual [ ("pending.bin", false) ]
                Vitest.expect(secondState.PendingMaterializationSessionId).toEqual (Some "conflict-1")
            }
        )

        Vitest.test (
            "RefreshCompleted ignores stale responses without emitting follow-up callback work",
            fun () -> promise {
                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        RefreshRequestId = 2
                        Status = {
                            GitState.Empty.Status with
                                CurrentBranch = Some "feature/live"
                        }
                }

                let staleRefresh = {
                    Session = Ok sessionInfo
                    Status = Ok(statusForBranch "feature/stale")
                    Refs = Ok [| localBranch "feature/stale" true true |]
                    LfsSettings = Ok(lfsSettings 9 false)
                    OriginRemoteRepositoryWebUrl = Some "https://example.org/feature/stale"
                }

                let nextState, cmd =
                    update defaultDependencies ignore (RefreshCompleted(1, Ok staleRefresh)) state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.Status.CurrentBranch).toEqual (Some "feature/live")
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "RefreshCompleted does not report stale failures through the Git error modal",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        RefreshRequestId = 2
                        ErrorNotice = Some "keep current error"
                }

                let nextState, cmd =
                    update deps ignore (RefreshCompleted(1, Error "older refresh failed")) state

                let! messages = collectMessages cmd

                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(nextState.ErrorNotice).toEqual (Some "keep current error")
                Vitest.expect(reportedErrors.Count).toBe (0)
            }
        )

        Vitest.test (
            "RefreshCompleted ignores stale failures and keeps the current model untouched",
            fun () -> promise {
                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        RefreshRequestId = 2
                        ErrorNotice = Some "keep current error"
                        Status = {
                            GitState.Empty.Status with
                                CurrentBranch = Some "feature/live"
                        }
                }

                let nextState, cmd =
                    update defaultDependencies ignore (RefreshCompleted(1, Error "older refresh failed")) state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.Status.CurrentBranch).toEqual (Some "feature/live")
                Vitest.expect(nextState.ErrorNotice).toEqual (Some "keep current error")
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "RefreshRequested stores origin repository URL from refresh metadata",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 3 true)) }
                        getRepositoryWebUrl =
                            fun _ -> promise { return Ok(succeeded (Some "https://github.com/nfdi4plants/Swate")) }
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                }

                let requestingState, requestCmd = update deps ignore RefreshRequested state
                let! requestMessages = collectMessages requestCmd

                let nextState, nextCmd =
                    match requestMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message requestingState
                    | _ -> failwith "Expected refresh completion message."

                let! nextMessages = collectMessages nextCmd

                Vitest
                    .expect(nextState.OriginRemoteRepositoryWebUrl)
                    .toEqual (Some "https://github.com/nfdi4plants/Swate")

                Vitest.expect(nextMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "The refresh reads the settings for a session without a storage policy service",
            fun () -> promise {
                let sessionWithoutStoragePolicy = {
                    sessionInfo with
                        Services = {
                            sessionInfo.Services with
                                StoragePolicy = false
                        }
                }

                let deps = {
                    defaultDependencies with
                        getSessionInfo = fun _ -> promise { return Ok(succeeded sessionWithoutStoragePolicy) }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 3 true)) }
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                }

                let requestingState, requestCmd = update deps ignore RefreshRequested state
                let! requestMessages = collectMessages requestCmd

                let nextState, nextCmd =
                    match requestMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message requestingState
                    | _ -> failwith "Expected refresh completion message."

                let! nextMessages = collectMessages nextCmd

                Vitest.expect(nextState.LfsAutoTrackThresholdMb).toBe (3)
                Vitest.expect(nextState.DownloadLargeFiles).toBe (true)
                Vitest.expect(nextMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "RefreshRequested keeps refresh successful when origin repository URL lookup fails",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 2 false)) }
                        getRepositoryWebUrl = fun _ -> promise { return Error "origin lookup failed" }
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                }

                let requestingState, requestCmd = update deps ignore RefreshRequested state
                let! requestMessages = collectMessages requestCmd

                let nextState, nextCmd =
                    match requestMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message requestingState
                    | _ -> failwith "Expected refresh completion message."

                let! nextMessages = collectMessages nextCmd

                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(nextState.OriginRemoteRepositoryWebUrl).toEqual (None)
                Vitest.expect(nextMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "RefreshCompleted records missing-repository state instead of leaving the sidebar in generic error mode",
            fun () -> promise {
                let clearedPages = ResizeArray<PageState option>()

                let missing =
                    makeFailure
                        NotFound
                        VersionControlCodes.WorkspaceUnmanaged
                        "The selected ARC path is not a git repository."
                        None
                        [||]

                let refreshResult = {
                    Session = Ok sessionInfo
                    Status = Error missing
                    Refs = Error missing
                    LfsSettings = Error missing
                    OriginRemoteRepositoryWebUrl = None
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        RefreshRequestId = 1
                        RefreshState = GitRefreshState.Loading
                        BusyOperation = Some GitBusyOperation.Refreshing
                }

                let nextState, cmd =
                    update defaultDependencies clearedPages.Add (RefreshCompleted(1, Ok refreshResult)) state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.RepositoryAvailability).toEqual (GitRepositoryAvailability.MissingRepository)
                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(nextState.ChangedFiles).toEqual ([||])
                Vitest.expect(clearedPages |> Seq.toArray).toEqual ([| None |])
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "Opening the sidebar of an ARC already known to have no repository leaves the page alone",
            fun () -> promise {
                let setPages = ResizeArray<PageState option>()

                let missing =
                    makeFailure
                        NotFound
                        VersionControlCodes.WorkspaceUnmanaged
                        "The selected ARC path is not a git repository."
                        None
                        [||]

                let refreshResult = {
                    Session = Ok sessionInfo
                    Status = Error missing
                    Refs = Error missing
                    LfsSettings = Error missing
                    OriginRemoteRepositoryWebUrl = None
                }

                let state = {
                    runningState with
                        RepositoryAvailability = GitRepositoryAvailability.MissingRepository
                }

                let openedState, openCmd =
                    update defaultDependencies setPages.Add (SidebarVisibilityChanged true) state

                let! openMessages = collectMessages openCmd

                let requestingState, _ =
                    update defaultDependencies setPages.Add RefreshRequestedSilently openedState

                let nextState, nextCmd =
                    update
                        defaultDependencies
                        setPages.Add
                        (SilentRefreshCompleted(requestingState.RefreshRequestId, Ok refreshResult))
                        requestingState

                let! nextMessages = collectMessages nextCmd

                Vitest.expect(openMessages).toEqual ([| RefreshRequestedSilently |])
                Vitest.expect(nextState.RepositoryAvailability).toEqual (GitRepositoryAvailability.MissingRepository)
                Vitest.expect(setPages |> Seq.toArray).toEqual ([||])
                Vitest.expect(nextMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "SelectChangeCompleted leaves the current selection untouched when an older request finishes late",
            fun () -> promise {
                let mutable replyResult = None
                let reply result = replyResult <- Some result

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        PageLoadRequestId = 2
                        SelectedChangePath = Some "B.txt"
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (SelectChangeCompleted(1, "A.txt", reply, Ok(GitPageChange.Set(PageState.TextPage "late"))))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.SelectedChangePath).toEqual (Some "B.txt")
                Vitest.expect(replyResult).toEqual (Some(Ok()))
            }
        )

        Vitest.test (
            "SelectChangeCompleted ignores stale failures and keeps the current selection/error untouched",
            fun () -> promise {
                let mutable replyResult = None
                let reply result = replyResult <- Some result

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        PageLoadRequestId = 2
                        SelectedChangePath = Some "B.txt"
                        ErrorNotice = None
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (SelectChangeCompleted(1, "A.txt", reply, Error "older load failed"))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.SelectedChangePath).toEqual (Some "B.txt")
                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(replyResult).toEqual (Some(Ok()))
            }
        )

        Vitest.test (
            "ConfirmMergeResolutionCompleted refreshes git state after stale merge-conflict errors",
            fun () -> promise {
                let clearedPages = ResizeArray<PageState option>()

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        SelectedChangePath = Some "conflict-a.txt"
                        MergeResolutionPendingPath = Some "conflict-a.txt"
                        BusyOperation = Some(GitBusyOperation.ConfirmingMergeResolution "conflict-a.txt")
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        clearedPages.Add
                        (ConfirmMergeResolutionCompleted(
                            state.ArcSessionId,
                            Error(ConfirmMergeResolutionError.Stale("stale conflict handle", None, false, "conflict-1"))
                        ))
                        state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.SelectedChangePath).toEqual (None)
                Vitest.expect(nextState.MergeResolutionPendingPath).toEqual (None)
                Vitest.expect(clearedPages |> Seq.toArray).toEqual ([| None |])
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "ConfirmMergeResolutionCompleted ignores late results from the previous ARC",
            fun () -> promise {
                let pageStates = ResizeArray<PageState option>()
                let conflict = (conflictedStatus [| "conflict-a.txt" |]).ActiveConflictSession.Value

                let refreshedConflict =
                    (conflictedStatus [| "conflict-b.txt" |]).ActiveConflictSession.Value

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded {
                                            RefreshedHandle = refreshedConflict.Handle
                                            RemainingItems = refreshedConflict.Items
                                        }
                                    )
                            }
                        getStatus = fun _ -> promise { return Ok(succeeded (conflictedStatus [| "conflict-b.txt" |])) }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                }

                let request = {
                    Path = "conflict-a.txt"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.SupplyResolvedContent "resolved"
                }

                let initialState = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        WorkspaceVersion = Some "v1"
                        SelectedChangePath = Some "conflict-a.txt"
                }

                let stateAfterRequest, requestCmd =
                    update deps pageStates.Add (ConfirmMergeResolutionRequested request) initialState

                let switchedState, switchCmd =
                    update deps pageStates.Add (ArcPathChanged(Some "C:/arc-b")) stateAfterRequest

                let! _ = collectMessages switchCmd
                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| ConfirmMergeResolutionCompleted _ |] ->
                        update deps pageStates.Add completionMessages[0] switchedState
                    | _ -> failwith "Expected a merge-resolution completion message."

                let! _ = collectMessages finishCmd

                Vitest
                    .expect(nextState)
                    .toEqual (
                        {
                            GitState.Empty with
                                CurrentArcPath = Some "C:/arc-b"
                                ArcSessionId = 1
                                RefreshRequestId = 1
                                PageLoadRequestId = 1
                                ExternalRefreshGeneration = 1
                        }
                    )

                Vitest.expect(pageStates |> Seq.toArray).toEqual ([| None |])
            }
        )
)

Vitest.describe (
    "GitWorkflow write request flow",
    fun () ->
        Vitest.test (
            "Watcher batches refresh a visible sidebar once and defer hidden changes",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        delay = fun _ -> Promise.sleep 0
                }

                let visibleState, firstCmd =
                    update deps ignore ExternalChangesDetected {
                        runningState with
                            SidebarVisible = true
                    }

                let! firstMessages = collectMessages firstCmd

                let nextVisibleState, secondCmd =
                    update deps ignore ExternalChangesDetected visibleState

                let! secondMessages = collectMessages secondCmd

                let afterOldTimer, oldTimerCmd =
                    match firstMessages with
                    | [| ExternalRefreshDue generation |] ->
                        update deps ignore (ExternalRefreshDue generation) nextVisibleState
                    | _ -> failwith "Expected the first external refresh timer."

                let! oldTimerMessages = collectMessages oldTimerCmd

                let _, latestTimerCmd =
                    match secondMessages with
                    | [| ExternalRefreshDue generation |] ->
                        update deps ignore (ExternalRefreshDue generation) afterOldTimer
                    | _ -> failwith "Expected the latest external refresh timer."

                let! latestTimerMessages = collectMessages latestTimerCmd

                Vitest.expect(oldTimerMessages).toEqual ([||])
                Vitest.expect(latestTimerMessages).toEqual ([| RefreshRequestedSilently |])

                let hiddenState, hiddenCmd = update deps ignore ExternalChangesDetected runningState

                let! hiddenMessages = collectMessages hiddenCmd

                let revealedState, revealCmd =
                    update deps ignore (SidebarVisibilityChanged true) hiddenState

                let! revealMessages = collectMessages revealCmd

                Vitest.expect(hiddenMessages).toEqual ([||])
                Vitest.expect(hiddenState.ExternalChangesSeen).toBe (true)
                Vitest.expect(revealedState.ExternalChangesSeen).toBe (false)
                Vitest.expect(revealMessages).toEqual ([| RefreshRequestedSilently |])
            }
        )

        Vitest.test (
            "Hiding the sidebar invalidates a pending external refresh",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        delay = fun _ -> Promise.sleep 0
                }

                let pendingState, timerCmd =
                    update deps ignore ExternalChangesDetected {
                        runningState with
                            SidebarVisible = true
                    }

                let! timerMessages = collectMessages timerCmd

                let hiddenState, hideCmd =
                    update deps ignore (SidebarVisibilityChanged false) pendingState

                let! hideMessages = collectMessages hideCmd

                let _, dueCmd =
                    match timerMessages with
                    | [| ExternalRefreshDue generation |] ->
                        update deps ignore (ExternalRefreshDue generation) hiddenState
                    | _ -> failwith "Expected an external refresh timer."

                let! dueMessages = collectMessages dueCmd

                Vitest.expect(hiddenState.SidebarVisible).toBe (false)
                Vitest.expect(hideMessages).toEqual ([||])
                Vitest.expect(dueMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "Opening the sidebar refreshes the Git state silently",
            fun () -> promise {
                let openedState, openCmd =
                    update defaultDependencies ignore (SidebarVisibilityChanged true) runningState

                let! openMessages = collectMessages openCmd

                Vitest.expect(openedState.SidebarVisible).toBe (true)
                Vitest.expect(openMessages).toEqual ([| RefreshRequestedSilently |])
            }
        )

        Vitest.test (
            "UploadRightAwayChanged starts checked, stores the new value and hands it to the main process",
            fun () -> promise {
                let stored = ResizeArray<bool>()

                let deps = {
                    defaultDependencies with
                        setUploadRightAway =
                            fun value ->
                                stored.Add value
                                promise { return Ok(succeeded ()) }
                }

                let uncheckedState, uncheckedCmd =
                    update deps ignore (UploadRightAwayChanged false) runningState

                let rechecked, recheckedCmd =
                    update deps ignore (UploadRightAwayChanged true) uncheckedState

                let! uncheckedMessages = collectMessages uncheckedCmd
                let! recheckedMessages = collectMessages recheckedCmd

                Vitest.expect(GitState.Empty.UploadRightAway).toBe (true)
                Vitest.expect(uncheckedState.UploadRightAway).toBe (false)
                Vitest.expect(rechecked.UploadRightAway).toBe (true)
                Vitest.expect(uncheckedMessages).toEqual ([||])
                Vitest.expect(recheckedMessages).toEqual ([||])
                Vitest.expect(stored.ToArray()).toEqual ([| false; true |])
            }
        )

        Vitest.test (
            "The first refresh of an ARC reads the stored upload right away choice and applies it",
            fun () -> promise {
                let reads = ResizeArray<string>()

                let deps = {
                    withRefresh cleanStatus defaultDependencies with
                        getUploadRightAway =
                            fun () ->
                                reads.Add "read"
                                promise { return Ok(succeeded false) }
                }

                let! bound, bindMessages = step deps GitState.Empty (ArcPathChanged(Some "C:/arc"))
                let! refreshing, refreshMessages = step deps bound bindMessages[0]
                let! refreshed, loadMessages = step deps refreshing refreshMessages[0]

                Vitest.expect(refreshed.UploadRightAway).toBe (true)
                Vitest.expect(loadMessages).toEqual ([| UploadRightAwayLoaded(bound.ArcSessionId, false) |])

                let! loaded, _ = step deps refreshed loadMessages[0]

                Vitest.expect(loaded.UploadRightAway).toBe (false)
                Vitest.expect(reads.Count).toBe 1

                let! refreshingAgain, againMessages = step deps loaded RefreshRequested
                let! _, afterSecondRefresh = step deps refreshingAgain againMessages[0]

                Vitest.expect(afterSecondRefresh).toEqual ([||])
                Vitest.expect(reads.Count).toBe 1
            }
        )

        Vitest.test (
            "A stored upload right away choice that arrives after a local change is ignored",
            fun () -> promise {
                let pendingState = {
                    runningState with
                        UploadRightAwayLoad = UploadRightAwayLoad.Pending
                }

                let changedState, _ =
                    update defaultDependencies ignore (UploadRightAwayChanged false) pendingState

                let afterLoad, _ =
                    update
                        defaultDependencies
                        ignore
                        (UploadRightAwayLoaded(runningState.ArcSessionId, true))
                        changedState

                Vitest.expect(afterLoad.UploadRightAway).toBe (false)
            }
        )

        Vitest.test (
            "A stored upload right away choice of an earlier ARC session is ignored",
            fun () -> promise {
                let pendingState = {
                    runningState with
                        UploadRightAwayLoad = UploadRightAwayLoad.Pending
                }

                let nextArc, _ =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/arc-b")) pendingState

                // The new ARC has its own read in flight, so only the session id tells the two apart.
                let nextArcLoading = {
                    nextArc with
                        UploadRightAwayLoad = UploadRightAwayLoad.Pending
                }

                let afterLoad, _ =
                    update
                        defaultDependencies
                        ignore
                        (UploadRightAwayLoaded(runningState.ArcSessionId, false))
                        nextArcLoading

                Vitest.expect(nextArc.ArcSessionId).not.toBe runningState.ArcSessionId
                Vitest.expect(afterLoad.UploadRightAway).toBe (true)
                Vitest.expect(afterLoad.UploadRightAwayLoad).toEqual UploadRightAwayLoad.Pending
            }
        )

        Vitest.test (
            "Closing and reopening the sidebar keeps the upload right away choice",
            fun () -> promise {
                let uncheckedState, _ =
                    update defaultDependencies ignore (UploadRightAwayChanged false) runningState

                let closedState, _ =
                    update defaultDependencies ignore (SidebarVisibilityChanged false) uncheckedState

                let reopenedState, _ =
                    update defaultDependencies ignore (SidebarVisibilityChanged true) closedState

                Vitest.expect(closedState.UploadRightAway).toBe (false)
                Vitest.expect(reopenedState.UploadRightAway).toBe (false)
            }
        )

        Vitest.test (
            "ArcPathChanged resets the upload right away choice to checked",
            fun () -> promise {
                let uncheckedState, _ =
                    update defaultDependencies ignore (UploadRightAwayChanged false) runningState

                let nextState, _ =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/arc-b")) uncheckedState

                Vitest.expect(nextState.CurrentArcPath).toEqual (Some "C:/arc-b")
                Vitest.expect(nextState.UploadRightAway).toBe (true)
            }
        )

        Vitest.test (
            "Opening the sidebar during a running refresh does not replace it",
            fun () -> promise {
                let refreshingState, _ =
                    update defaultDependencies ignore RefreshRequested runningState

                let openedState, openCmd =
                    update defaultDependencies ignore (SidebarVisibilityChanged true) refreshingState

                let! openMessages = collectMessages openCmd

                Vitest.expect(openedState.SidebarVisible).toBe (true)
                Vitest.expect(openedState.RefreshRequestId).toEqual (refreshingState.RefreshRequestId)
                Vitest.expect(openMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "Opening the sidebar without an ARC keeps a running clone",
            fun () -> promise {
                let cloningState = {
                    GitState.Empty with
                        BusyOperation = Some(GitBusyOperation.CloningRepository "C:/clone")
                        WriteRequestId = 1
                        CurrentOperation = Some { OperationId = "clone-op" }
                }

                let openedState, openCmd =
                    update defaultDependencies ignore (SidebarVisibilityChanged true) cloningState

                let! openMessages = collectMessages openCmd

                Vitest
                    .expect(openedState)
                    .toEqual (
                        {
                            cloningState with
                                SidebarVisible = true
                        }
                    )

                Vitest.expect(openMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "A silent refresh disables the sidebar without notice text and a normal refresh shows it",
            fun () -> promise {
                let silentState, _ =
                    update defaultDependencies ignore RefreshRequestedSilently runningState

                let normalState, _ = update defaultDependencies ignore RefreshRequested runningState

                Vitest.expect(silentState.BusyOperation).toEqual (Some GitBusyOperation.Refreshing)
                Vitest.expect(silentState.BusyNotice).toEqual (None)
                Vitest.expect(currentRunStatus silentState).toEqual (Some GitSidebarRunStatus.BusyWithoutNotice)
                Vitest.expect(normalState.BusyNotice.IsSome).toBe (true)
            }
        )

        Vitest.test (
            "Primary save creates the revision with exactly the selected paths and never stages separately",
            fun () -> promise {
                let mutable captured = None

                let deps = {
                    defaultDependencies with
                        createRevision =
                            fun request ->
                                captured <- Some request
                                promise { return Ok(failed ProviderError "git_failure" "stop") }
                }

                let model, command =
                    update
                        deps
                        ignore
                        (PrimarySaveSelectionRequested {
                            Message = "save"
                            Paths = [| " leading-space.txt"; "b.txt"; "b.txt" |]
                        })
                        runningState

                let! messages = collectMessages command
                let _, writeCmd = update deps ignore messages[0] model
                let! _ = collectMessages writeCmd
                Vitest.expect(captured.Value.Paths).toEqual ([| " leading-space.txt"; "b.txt" |])
                Vitest.expect(captured.Value.ExpectedWorkspaceVersion).toBe ("v1")
            }
        )

        Vitest.test (
            "Synchronize never provisions when the target is unreachable",
            fun () -> promise {
                let mutable projectCalls = 0

                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    promise {
                                        return
                                            Ok(
                                                failed
                                                    Network
                                                    VersionControlCodes.TargetUnreachable
                                                    "target unreachable"
                                            )
                                    }
                                else
                                    unexpectedPromise "unexpected pull"
                        createRemoteProject =
                            fun _ ->
                                projectCalls <- projectCalls + 1
                                unexpectedGitLab "unexpected"
                }

                let model, command =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! messages = collectMessages command
                let next, finish = update deps ignore messages[0] model
                let! _ = collectMessages finish
                Vitest.expect(projectCalls).toBe (0)
                Vitest.expect(next.ErrorNotice).toEqual (Some "target unreachable")
            }
        )

        Vitest.test (
            "A duplicate remote project name opens the rename prompt",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    promise {
                                        return Ok(failed Validation VersionControlCodes.PublishTargetMissing "missing")
                                    }
                                else
                                    unexpectedPromise "unexpected pull"
                        createRemoteProject = fun _ -> promise { return Error(CreateProjectError.NameRefused None) }
                }

                let model, command =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) {
                        runningState with
                            CurrentArcPath = Some "C:/work/Existing ARC"
                    }

                let! messages = collectMessages command
                let next, finish = update deps ignore messages[0] model
                let! _ = collectMessages finish
                Vitest.expect(next.PendingPublishRename |> Option.map _.CurrentName).toEqual (Some "Existing ARC")

                Vitest
                    .expect(next.PendingPublishRename |> Option.map _.Message)
                    .toEqual (Some "A DataHUB repository with this name already exists. Enter a different name.")
            }
        )

        Vitest.test (
            "publishOwnersOf puts the personal owner first and disables groups that refuse new projects",
            fun () -> promise {
                let owners =
                    publishOwnersOf (
                        publishOwnersDto [|
                            publishGroup 3 "lab/team" true (Some "developer")
                            publishGroup 4 "lab/locked" false (Some "owner")
                            publishGroup 5 "lab/unknown" false None
                        |]
                    )

                Vitest.expect(owners |> Array.map _.NamespaceId).toEqual ([| None; Some 3; Some 4; Some 5 |])
                Vitest.expect(owners[0].Label).toContain ("carol")
                Vitest.expect(owners[1].Label).toBe ("lab/team")

                Vitest
                    .expect(owners |> Array.map (fun owner -> owner.DisabledReason.IsSome))
                    .toEqual ([| false; false; true; true |])
            }
        )

        Vitest.test (
            "A first publish with a group that accepts projects asks for the owner before creating anything",
            fun () -> promise {
                let createRequests = ResizeArray<CreateProjectRequest>()

                let deps =
                    firstPublishDeps
                        (fun () -> promise { return Ok(publishOwnersDto [| publishGroup 7 "lab/team" true None |]) })
                        (fun request ->
                            createRequests.Add request
                            unexpectedGitLab "createRemoteProject"
                        )

                let! next = runPush deps runningState
                let prompt = next.PendingPublishRename.Value

                Vitest.expect(createRequests.Count).toBe (0)
                Vitest.expect(prompt.Owners |> Array.map _.NamespaceId).toEqual ([| None; Some 7 |])
                Vitest.expect(prompt.SelectedNamespaceId).toEqual (None)
                Vitest.expect(next.PublishOwners.IsSome).toBe (true)
                Vitest.expect(next.PublishOwnerChoice).toEqual (None)
            }
        )

        Vitest.test (
            "A first publish without a group that accepts projects creates in the personal namespace without a dialog",
            fun () -> promise {
                let publishWith groups = promise {
                    let createRequests = ResizeArray<CreateProjectRequest>()

                    let deps =
                        firstPublishDeps
                            (fun () -> promise { return Ok(publishOwnersDto groups) })
                            (fun request ->
                                createRequests.Add request
                                promise { return Ok remoteProject }
                            )

                    let! next = runPush deps runningState
                    return createRequests |> Seq.toArray, next
                }

                let expectedRequest: CreateProjectRequest = { Name = "arc"; NamespaceId = None }

                let! lockedRequests, lockedState = publishWith [| publishGroup 7 "lab/locked" false (Some "owner") |]

                let! noGroupRequests, noGroupState = publishWith [||]

                Vitest.expect(lockedRequests).toEqual ([| expectedRequest |])
                Vitest.expect(noGroupRequests).toEqual ([| expectedRequest |])
                Vitest.expect(lockedState.PendingPublishRename).toEqual (None)
                Vitest.expect(noGroupState.PendingPublishRename).toEqual (None)
                Vitest.expect(noGroupState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "Submitting the dialog with the current folder name and a group publishes there without renaming",
            fun () -> promise {
                let createRequests = ResizeArray<CreateProjectRequest>()
                let loadCalls = ResizeArray<string>()
                let renameCalls = ResizeArray<string>()

                let deps = {
                    firstPublishDeps
                        (fun () ->
                            loadCalls.Add "loadPublishOwners"
                            promise { return Ok(publishOwnersDto [| publishGroup 7 "lab/team" true None |]) }
                        )
                        (fun request ->
                            createRequests.Add request
                            promise { return Ok remoteProject }
                        ) with
                        renameOpenArcRoot =
                            fun name ->
                                renameCalls.Add name
                                unexpectedPromise "renameOpenArcRoot"
                }

                let! prompted = runPush deps runningState

                let! submitted, publishMessages =
                    step deps prompted (SubmitPublishRenameRequested { Name = " ARC "; NamespaceId = Some 7 })

                Vitest.expect(submitted.PendingPublishRename).toEqual (None)

                Vitest
                    .expect(publishMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )

                let! _ = runPush deps submitted

                Vitest.expect(renameCalls.Count).toBe (0)
                Vitest.expect(loadCalls.Count).toBe (1)
                Vitest.expect(createRequests |> Seq.map _.NamespaceId |> Seq.toArray).toEqual ([| Some 7 |])
            }
        )

        Vitest.test (
            "The chosen owner survives a rename when the reply comes before the path change",
            fun () -> promise {
                let createRequests = ResizeArray<CreateProjectRequest>()

                let deps = {
                    firstPublishDeps
                        (fun () -> promise { return Ok(publishOwnersDto [| publishGroup 7 "lab/team" true None |]) })
                        (fun request ->
                            createRequests.Add request
                            promise { return Ok remoteProject }
                        ) with
                        renameOpenArcRoot = fun _ -> promise { return Ok "C:/work/Renamed ARC" }
                }

                let! prompted = runPush deps runningState

                let! submitted, renameMessages =
                    step
                        deps
                        prompted
                        (SubmitPublishRenameRequested {
                            Name = "Renamed ARC"
                            NamespaceId = Some 7
                        })

                let! renamed, _ = step deps submitted renameMessages[0]
                let! moved, _ = step deps renamed (ArcPathChanged(Some "C:/work/Renamed ARC"))
                let! _ = runRefreshAndPublish deps moved

                Vitest
                    .expect(
                        createRequests
                        |> Seq.toArray
                        |> Array.map (fun request -> request.Name, request.NamespaceId)
                    )
                    .toEqual ([| "Renamed ARC", Some 7 |])
            }
        )

        Vitest.test (
            "The chosen owner survives a rename when the path change comes before the reply",
            fun () -> promise {
                let createRequests = ResizeArray<CreateProjectRequest>()

                let deps = {
                    firstPublishDeps
                        (fun () -> promise { return Ok(publishOwnersDto [| publishGroup 7 "lab/team" true None |]) })
                        (fun request ->
                            createRequests.Add request
                            promise { return Ok remoteProject }
                        ) with
                        renameOpenArcRoot = fun _ -> promise { return Ok "C:/work/Renamed ARC" }
                }

                let! prompted = runPush deps runningState

                let! submitted, renameMessages =
                    step
                        deps
                        prompted
                        (SubmitPublishRenameRequested {
                            Name = "Renamed ARC"
                            NamespaceId = Some 7
                        })

                let! moved, _ = step deps submitted (ArcPathChanged(Some "C:/work/Renamed ARC"))
                let! replied, replyMessages = step deps moved renameMessages[0]

                Vitest.expect(replyMessages).toEqual ([| RefreshRequested |])

                let! _ = runRefreshAndPublish deps replied

                Vitest
                    .expect(
                        createRequests
                        |> Seq.toArray
                        |> Array.map (fun request -> request.Name, request.NamespaceId)
                    )
                    .toEqual ([| "Renamed ARC", Some 7 |])
            }
        )

        Vitest.test (
            "A refusal for the chosen group reopens the dialog with that group disabled and no owner selected",
            fun () -> promise {
                let owners =
                    publishOwnersOf (publishOwnersDto [| publishGroup 7 "lab/team" true None |])

                let deps =
                    firstPublishDeps
                        (fun () -> promise { return failwith "loadPublishOwners" })
                        (fun _ -> promise { return Error(CreateProjectError.NotAllowed(Some "limit reached")) })

                let state = {
                    runningState with
                        PublishOwners = Some owners
                        PublishOwnerChoice = Some owners[1]
                }

                let! next = runPush deps state
                let prompt = next.PendingPublishRename.Value

                Vitest.expect(prompt.Owners.Length).toBe (2)
                Vitest.expect(prompt.Owners[0].DisabledReason).toEqual (None)
                Vitest.expect(prompt.Owners[1].DisabledReason.IsSome).toBe (true)
                Vitest.expect(prompt.SelectedNamespaceId).toEqual (None)
                Vitest.expect(prompt.Message).toContain ("lab/team")
                Vitest.expect(prompt.Message).not.toContain ("limit reached")
                Vitest.expect(next.PublishOwners).toEqual (Some prompt.Owners)
                Vitest.expect(next.PublishOwnerChoice).toEqual (None)
                Vitest.expect(next.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A refusal for the personal namespace reopens the dialog with it disabled when a group is left",
            fun () -> promise {
                let owners =
                    publishOwnersOf (publishOwnersDto [| publishGroup 7 "lab/team" true None |])

                let deps =
                    firstPublishDeps
                        (fun () -> promise { return failwith "loadPublishOwners" })
                        (fun _ -> promise { return Error(CreateProjectError.NotAllowed(Some "limit reached")) })

                let state = {
                    runningState with
                        PublishOwners = Some owners
                        PublishOwnerChoice = Some owners[0]
                }

                let! next = runPush deps state
                let prompt = next.PendingPublishRename.Value

                Vitest.expect(prompt.Owners[0].DisabledReason.IsSome).toBe (true)
                Vitest.expect(prompt.Owners[1].DisabledReason).toEqual (None)
                Vitest.expect(prompt.Message).toContain ("limit reached")
                Vitest.expect(prompt.SelectedNamespaceId).toEqual (None)
                Vitest.expect(next.PublishOwnerChoice).toEqual (None)
                Vitest.expect(next.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A name refused inside the chosen group keeps the owners and the group in the dialog",
            fun () -> promise {
                let owners =
                    publishOwnersOf (publishOwnersDto [| publishGroup 7 "lab/team" true None |])

                let deps =
                    firstPublishDeps
                        (fun () -> promise { return failwith "loadPublishOwners" })
                        (fun _ -> promise { return Error(CreateProjectError.NameRefused None) })

                let state = {
                    runningState with
                        PublishOwners = Some owners
                        PublishOwnerChoice = Some owners[1]
                }

                let! next = runPush deps state
                let prompt = next.PendingPublishRename.Value

                Vitest.expect(prompt.Owners).toEqual (owners)
                Vitest.expect(prompt.SelectedNamespaceId).toEqual (Some 7)
                Vitest.expect(next.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A primary save with a group that accepts projects asks for the owner and creates nothing",
            fun () -> promise {
                let createRequests = ResizeArray<CreateProjectRequest>()

                let deps = {
                    firstPublishDeps
                        (fun () -> promise { return Ok(publishOwnersDto [| publishGroup 7 "lab/team" true None |]) })
                        (fun request ->
                            createRequests.Add request
                            unexpectedGitLab "createRemoteProject"
                        ) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let! requested, requestMessages = step deps state (PrimarySaveAllRequested "Publish")
                let writing, writeCmd = update deps ignore requestMessages[0] requested
                let! completionMessages = collectWriteMessages writeCmd

                match completionMessages with
                | [| WriteCompleted(_, _, PrimarySave _, Ok(RequiresRemoteProjectRename _)) |] -> ()
                | _ -> failwith "Expected the primary save to ask for the owner."

                let finished, _ = update deps ignore completionMessages[0] writing

                Vitest.expect(createRequests.Count).toBe (0)

                Vitest
                    .expect(
                        finished.PendingPublishRename
                        |> Option.map (fun prompt -> prompt.Owners |> Array.map _.NamespaceId)
                    )
                    .toEqual (Some [| None; Some 7 |])
            }
        )

        Vitest.test (
            "A completed write other than the owner prompt clears the owners and the owner choice",
            fun () -> promise {
                let owners =
                    publishOwnersOf (publishOwnersDto [| publishGroup 7 "lab/team" true None |])

                let state = {
                    runningState with
                        PublishOwners = Some owners
                        PublishOwnerChoice = Some owners[1]
                }

                let publishWith createResult = promise {
                    let deps =
                        firstPublishDeps
                            (fun () -> promise { return failwith "loadPublishOwners" })
                            (fun _ -> createResult)

                    return! runPush deps state
                }

                let! published = publishWith (promise { return Ok remoteProject })

                let! failed = publishWith (promise { return Error(CreateProjectError.Failed GitLabError.Forbidden) })

                for finished in [| published; failed |] do
                    Vitest.expect(finished.PendingPublishRename).toEqual (None)
                    Vitest.expect(finished.PublishOwners).toEqual (None)
                    Vitest.expect(finished.PublishOwnerChoice).toEqual (None)

                Vitest.expect(failed.ErrorNotice.IsSome).toBe (true)
            }
        )

        Vitest.test (
            "A refusal for the personal namespace without another group is an error and opens no dialog",
            fun () -> promise {
                let deps =
                    firstPublishDeps
                        (fun () -> promise { return Ok(publishOwnersDto [||]) })
                        (fun _ -> promise { return Error(CreateProjectError.NotAllowed None) })

                let! next = runPush deps runningState

                Vitest.expect(next.PendingPublishRename).toEqual (None)
                Vitest.expect(next.ErrorNotice.IsSome).toBe (true)
            }
        )

        Vitest.test (
            "A failed owner lookup stops the publish before anything is created and a later upload works",
            fun () -> promise {
                let createRequests = ResizeArray<CreateProjectRequest>()

                let deps =
                    firstPublishDeps
                        (fun () -> promise { return Error GitLabError.Forbidden })
                        (fun request ->
                            createRequests.Add request
                            unexpectedGitLab "createRemoteProject"
                        )

                let! next = runPush deps runningState

                Vitest.expect(createRequests.Count).toBe (0)
                Vitest.expect(next.PendingPublishRename).toEqual (None)
                Vitest.expect(next.ErrorNotice.Value).toContain (GitLabError.Forbidden.GitLabErrorToString)

                // Once the lookup works again, the next upload creates the repository.
                let retryDeps =
                    firstPublishDeps
                        (fun () -> promise { return Ok(publishOwnersDto [||]) })
                        (fun request ->
                            createRequests.Add request
                            promise { return Ok remoteProject }
                        )

                let! retried = runPush retryDeps next

                Vitest
                    .expect(createRequests |> Seq.toArray)
                    .toEqual (
                        [|
                            ({ Name = "arc"; NamespaceId = None }: CreateProjectRequest)
                        |]
                    )

                Vitest.expect(retried.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "Canceling the dialog clears the chosen owner so the next publish loads the owners again",
            fun () -> promise {
                let loadCalls = ResizeArray<string>()

                let deps =
                    firstPublishDeps
                        (fun () ->
                            loadCalls.Add "loadPublishOwners"
                            promise { return Ok(publishOwnersDto [| publishGroup 7 "lab/team" true None |]) }
                        )
                        (fun _ -> unexpectedGitLab "createRemoteProject")

                let! prompted = runPush deps runningState
                let groupOwner = prompted.PublishOwners.Value[1]

                let canceled, _ =
                    update deps ignore CancelPublishRenameRequested {
                        prompted with
                            PublishOwnerChoice = Some groupOwner
                    }

                Vitest.expect(canceled.PublishOwners).toEqual (None)
                Vitest.expect(canceled.PublishOwnerChoice).toEqual (None)

                let! _ = runPush deps canceled

                Vitest.expect(loadCalls.Count).toBe (2)
            }
        )

        Vitest.test (
            "Canceling the publish rename refreshes the committed status and keeps the saved locally notice",
            fun () -> promise {
                let deps = withRefresh cleanStatus defaultDependencies

                let initialState = {
                    runningState with
                        ChangedFiles = [| changedFile "saved.txt" "M" " " false |]
                        PendingPublishRename =
                            Some {
                                CurrentName = "Existing ARC"
                                Message = "A DataHUB repository with this name already exists. Enter a different name."
                                Owners = [||]
                                SelectedNamespaceId = None
                            }
                }

                let canceledState, cancelCmd =
                    update deps ignore CancelPublishRenameRequested initialState

                let! cancelMessages = collectMessages cancelCmd

                Vitest.expect(cancelMessages).toEqual ([| RefreshRequested |])
                Vitest.expect(canceledState.PendingPublishRename).toEqual (None)

                Vitest
                    .expect(canceledState.PendingRefreshWarningNotice)
                    .toEqual (Some "Changes were saved locally. Online sync is still pending.")

                let refreshingState, refreshCmd = update deps ignore cancelMessages[0] canceledState

                let! refreshMessages = collectMessages refreshCmd

                let refreshedState, afterRefreshCmd =
                    match refreshMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message refreshingState
                    | _ -> failwith "Expected the status refresh to complete."

                let! afterRefreshMessages = collectMessages afterRefreshCmd

                Vitest.expect(afterRefreshMessages).toEqual ([||])
                Vitest.expect(refreshedState.ChangedFiles).toEqual ([||])

                Vitest
                    .expect(refreshedState.WarningNotice)
                    .toEqual (Some "Changes were saved locally. Online sync is still pending.")

                Vitest.expect(refreshedState.PendingRefreshWarningNotice).toEqual (None)
            }
        )

        Vitest.test (
            "Cancel sends the key reported by the started event",
            fun () -> promise {
                let mutable captured = None

                let deps = {
                    defaultDependencies with
                        cancelOperation =
                            fun key ->
                                captured <- Some key
                                promise { return Ok true }
                }

                let model, _ =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let model, _ = update deps ignore (OperationStarted { OperationId = "op-1" }) model

                let _, command = update deps ignore CancelCurrentOperationRequested model
                let! _ = collectMessages command

                Vitest.expect(captured).toEqual (Some { OperationId = "op-1" })
            }
        )

        Vitest.test (
            "A started operation the workflow did not allocate does not become the cancel target",
            fun () -> promise {
                let requested, _ =
                    update defaultDependencies ignore (WriteRequested Fetch) runningState

                let afterUnrelated, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationStarted { OperationId = "explorer-materialize" })
                        requested

                let afterOwned, _ =
                    update defaultDependencies ignore (OperationStarted { OperationId = "op-1/2" }) afterUnrelated

                Vitest.expect(afterUnrelated.CurrentOperation).toEqual (Some { OperationId = "op-1" })

                Vitest.expect(afterOwned.CurrentOperation).toEqual (Some { OperationId = "op-1/2" })
            }
        )

        Vitest.test (
            "Storage settings are sent as one record with the threshold as Some",
            fun () -> promise {
                let mutable captured = None

                let deps = {
                    defaultDependencies with
                        setStoragePolicySettings =
                            fun request ->
                                captured <- Some request
                                promise { return Ok(succeeded ()) }
                }

                let model, command =
                    update deps ignore (SaveLfsAutoTrackThresholdRequested 4) runningState

                let! messages = collectMessages command
                let model, write = update deps ignore messages[0] model
                let! _ = collectMessages write

                Vitest
                    .expect(captured.Value.Settings)
                    .toEqual (
                        {
                            AutoPolicyThresholdMb = Some 4
                            MaterializeLargeObjects = false
                            DiffIndexingLimitMb = Some 1024
                        }
                    )
            }
        )

        Vitest.test (
            "Discard confirmation shows paths, cancels safely, and restores on confirmation",
            fun () -> promise {
                let mutable captured = None
                let pageStates = ResizeArray<PageState option>()

                let paths = [|
                    "a.txt"
                    "b.txt"
                    "c.txt"
                    "d.txt"
                    "e.txt"
                    "f.txt"
                    "g.txt"
                |]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        restorePaths =
                            fun request ->
                                captured <- Some request
                                promise { return Ok(succeeded ()) }
                }

                let singlePathState, singlePathCmd =
                    update deps pageStates.Add (DiscardSelectionRequested [| "single.txt" |]) runningState

                let! singlePathMessages = collectMessages singlePathCmd
                let singlePathDialog = singlePathState.PendingConfirmation.Value

                Vitest.expect(singlePathMessages).toEqual ([||])
                Vitest.expect(singlePathDialog.Title).toBe ("Discard changes?")

                Vitest
                    .expect(singlePathDialog.Message)
                    .toBe ("Discard your changes to 'single.txt'? This can't be undone.")

                Vitest.expect(singlePathDialog.ConfirmLabel).toBe ("Discard")
                Vitest.expect(singlePathDialog.CancelLabel).toBe ("Keep changes")

                Vitest
                    .expect(singlePathState.PendingRemoteAction)
                    .toEqual (GitPendingRemoteAction.DiscardSelection [| "single.txt" |])

                let pendingAgain, ignoredCmd =
                    update deps pageStates.Add (DiscardSelectionRequested [| "ignored.txt" |]) singlePathState

                let! ignoredMessages = collectMessages ignoredCmd

                Vitest.expect(ignoredMessages).toEqual ([||])
                Vitest.expect(pendingAgain.PendingConfirmation).toEqual (singlePathState.PendingConfirmation)
                Vitest.expect(pendingAgain.PendingRemoteAction).toEqual (singlePathState.PendingRemoteAction)

                let canceledState, cancelCmd =
                    update deps pageStates.Add CancelPendingRemoteActionRequested pendingAgain

                let! cancelMessages = collectMessages cancelCmd

                Vitest.expect(cancelMessages).toEqual ([||])
                Vitest.expect(canceledState.PendingConfirmation).toEqual (None)
                Vitest.expect(captured.IsNone).toBe (true)

                let manyPathState, manyPathCmd =
                    update deps pageStates.Add (DiscardSelectionRequested paths) canceledState

                let! manyPathMessages = collectMessages manyPathCmd

                Vitest.expect(manyPathMessages).toEqual ([||])

                Vitest
                    .expect(manyPathState.PendingConfirmation.Value.Message)
                    .toBe (
                        "Discard your changes to 7 files? This can't be undone.\na.txt\nb.txt\nc.txt\nd.txt\ne.txt\nand 2 more"
                    )

                let confirmingState, confirmCmd =
                    update deps pageStates.Add ConfirmPendingRemoteActionRequested manyPathState

                let! confirmMessages = collectMessages confirmCmd

                let requestedState, writeCmd =
                    match confirmMessages with
                    | [| WriteRequested(DiscardSelection confirmedPaths) |] ->
                        Vitest.expect(confirmedPaths).toEqual paths
                        update deps pageStates.Add confirmMessages[0] confirmingState
                    | _ -> failwith "Expected the confirmed discard write request."

                let! completionMessages = collectMessages writeCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, DiscardSelection _, Ok(Completed _)) |] ->
                        update deps pageStates.Add completionMessages[0] requestedState
                    | _ -> failwith "Expected discard to complete successfully."

                let! _ = collectMessages finishCmd
                Vitest.expect(captured.Value.Paths).toEqual paths
                Vitest.expect(captured.Value.ExpectedWorkspaceVersion).toBe ("v1")
                Vitest.expect(pageStates |> Seq.toArray).toEqual ([| None |])
                Vitest.expect(finalState.SelectedChangePath).toEqual (None)
            }
        )

        Vitest.test (
            "Switching to a branch sends its opaque provider ref",
            fun () -> promise {
                let mutable captured = None

                let deps = {
                    defaultDependencies with
                        preflightSwitchRef =
                            fun _ -> promise { return Ok(succeeded { PathsAtRisk = [||]; IsSafe = true }) }
                        switchRef =
                            fun request ->
                                captured <- Some request
                                promise { return Ok(succeeded cleanStatus) }
                }

                let model, command =
                    update deps ignore (SwitchBranchRequested "feature") {
                        runningState with
                            Refs = [| localBranch "feature" false false |]
                    }

                let! messages = collectMessages command
                let preflightState, writeRequest = update deps ignore messages[0] model
                let! writeMessages = collectMessages writeRequest
                let model, write = update deps ignore writeMessages[0] preflightState
                let! _ = collectMessages write
                Vitest.expect(captured.Value.TargetRef).toBe ("git-local:feature")
            }
        )

        Vitest.test (
            "An unsafe branch switch names the paths at risk and does not write",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        preflightSwitchRef =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded {
                                            PathsAtRisk = [| "data.txt"; "metadata.tsv" |]
                                            IsSafe = false
                                        }
                                    )
                            }
                        reportError = reportedErrors.Add
                }

                let model, command =
                    update deps ignore (SwitchBranchRequested "feature") {
                        runningState with
                            Refs = [| localBranch "feature" false false |]
                    }

                let! messages = collectMessages command
                let nextState, finishCmd = update deps ignore messages[0] model
                let! finishMessages = collectMessages finishCmd

                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(nextState.ErrorNotice.Value).toContain ("data.txt")
                Vitest.expect(nextState.ErrorNotice.Value).toContain ("metadata.tsv")
                Vitest.expect(nextState.ErrorNotice.Value).toContain ("Save or discard")
                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "A partial branch preflight does not switch",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let failure =
                    makeFailure ProviderError "preflight_incomplete" "The branch preflight did not finish." None [||]

                let model = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.SwitchingBranch
                        BusyNotice = Some "Switching branch"
                        CurrentOperation = Some { OperationId = "switch-op" }
                }

                let nextState, command =
                    update
                        deps
                        ignore
                        (SwitchBranchPreflightCompleted(
                            model.ArcSessionId,
                            "feature",
                            Ok(
                                OperationResultDto.PartiallySucceeded(
                                    operation { PathsAtRisk = [||]; IsSafe = true },
                                    failure
                                )
                            )
                        ))
                        model

                let! messages = collectMessages command

                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(nextState.ErrorNotice).toEqual (Some failure.Message)
                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(reportedErrors[0].Title).toBe ("Could not switch branch")
            }
        )

        Vitest.test (
            "A stale branch preflight reports the error and refreshes",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let failure = {
                    makeFailure Concurrency VersionControlCodes.PreconditionFailed "The workspace token is stale." None [||] with
                        StateChanged = false
                }

                let deps = {
                    defaultDependencies with
                        preflightSwitchRef = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                        reportError = reportedErrors.Add
                }

                let stateAfterRequest, preflightCmd =
                    update deps ignore (SwitchBranchRequested "feature") {
                        runningState with
                            Refs = [| localBranch "feature" false false |]
                    }

                let! preflightMessages = collectMessages preflightCmd

                let nextState, finishCmd =
                    match preflightMessages with
                    | [| SwitchBranchPreflightCompleted(_, _, Ok(OperationResultDto.Failed _)) |] ->
                        update deps ignore preflightMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the branch preflight to return a stale token failure."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(nextState.ErrorNotice).toEqual (Some(failureMessage failure))
                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(reportedErrors[0].Title).toBe ("Could not switch branch")
                Vitest.expect(finishMessages).toEqual ([| RefreshRequested |])

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | WriteRequested(SwitchBranch _) -> true
                            | _ -> false
                        )
                    )
                    .toBe (false)
            }
        )

        Vitest.test (
            "A stale branch preflight reply is ignored",
            fun () -> promise {
                let state = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.SwitchingBranch
                        CurrentOperation = Some { OperationId = "switch-op" }
                        Refs = [| localBranch "feature" false false |]
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (SwitchBranchPreflightCompleted(
                            state.ArcSessionId - 1,
                            "feature",
                            Ok(succeeded { PathsAtRisk = [||]; IsSafe = true })
                        ))
                        state

                let! messages = collectMessages cmd

                Vitest.expect(nextState).toEqual (state)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "WriteCompleted reports write failures through the Git error modal",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        WriteRequestId = 3
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                }

                let nextState, cmd =
                    update
                        deps
                        ignore
                        (WriteCompleted(
                            state.ArcSessionId,
                            3,
                            Push GitUpdateAcceptance.RequirePreview,
                            Error "remote rejected the push"
                        ))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.ErrorNotice).toEqual (Some "remote rejected the push")
                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(reportedErrors[0].Title).toBe ("Could not push changes")
                Vitest.expect(reportedErrors[0].Message).toBe ("remote rejected the push")
            }
        )

        Vitest.test (
            "WriteCompleted reports the output of the failed write as the log of the error",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let writeRequest = Push GitUpdateAcceptance.RequirePreview

                let started, _ =
                    update defaultDependencies ignore (WriteRequested writeRequest) runningState

                let withOutput, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(
                            outputOf "op-1" [
                                "command", "git push"
                                "stderr", "remote rejected the push"
                            ]
                        ))
                        started

                let _, cmd =
                    update
                        deps
                        ignore
                        (WriteCompleted(
                            withOutput.ArcSessionId,
                            withOutput.WriteRequestId,
                            writeRequest,
                            Error "remote rejected the push"
                        ))
                        withOutput

                let! _ = collectMessages cmd

                Vitest.expect(reportedErrors.Count).toBe (1)

                Vitest.expect(reportedErrors[0].Log.Lines |> Array.map _.Text).toEqual [|
                    "git push"
                    "remote rejected the push"
                |]
            }
        )

        Vitest.test (
            "an error that is no failure of the running operation reports no log",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let started, _ =
                    update
                        defaultDependencies
                        ignore
                        (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
                        runningState

                let withOutput, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "op-1" [ "command", "git push" ]))
                        started

                Vitest.expect(withOutput.CurrentLog.Lines.Length).toBe 1

                let _, cmd =
                    update
                        deps
                        ignore
                        (SelectChangeCompleted(
                            withOutput.PageLoadRequestId,
                            "A.txt",
                            ignore,
                            Error "The change could not be opened."
                        ))
                        withOutput

                let! _ = collectMessages cmd

                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(reportedErrors[0].Message).toBe ("The change could not be opened.")
                Vitest.expect(reportedErrors[0].Log.Lines.Length).toBe 0
            }
        )

        Vitest.test (
            "WriteCompleted with a cancellation failure shows a warning notice instead of an error",
            fun () -> promise {
                let canceled =
                    makeFailure Canceled VersionControlCodes.OperationCanceled "The pull was canceled." None [||]

                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    unexpectedPromise "unexpected push"
                                else
                                    promise { return Ok(OperationResultDto.Failed canceled) }
                }

                let state = { runningState with WriteRequestId = 0 }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) state

                let! completionMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(OperationCancelled _)) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected a canceled pull completion."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(stateAfterWrite.BusyOperation).toEqual (None)
                Vitest.expect(stateAfterWrite.WarningNotice).toEqual (Some "Git operation cancelled.")
                Vitest.expect(stateAfterWrite.ErrorNotice).toEqual (None)
                Vitest.expect(finishMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "WriteCompleted surfaces a genuine pull failure whose message ends in git's 'Aborting'",
            fun () -> promise {
                let overwrittenByMergeError =
                    "error: Your local changes would be overwritten by merge.\nAborting"

                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        WriteRequestId = 1
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                }

                let nextState, cmd =
                    update
                        deps
                        ignore
                        (WriteCompleted(
                            state.ArcSessionId,
                            1,
                            Pull GitUpdateAcceptance.RequirePreview,
                            Error overwrittenByMergeError
                        ))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.ErrorNotice).toEqual (Some overwrittenByMergeError)
                Vitest.expect(nextState.WarningNotice).toEqual (None)
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "WriteCompleted keeps the pull-completed context when LFS hydration is cancelled",
            fun () -> promise {
                let failure =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Large-file hydration was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [||]

                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    unexpectedPromise "unexpected push"
                                else
                                    promise {
                                        return
                                            Ok(
                                                OperationResultDto.PartiallySucceeded(
                                                    operation cleanStatus.Synchronization.Value,
                                                    failure
                                                )
                                            )
                                    }
                        getStatus = fun _ -> promise { return Ok(succeeded (statusForBranch "main")) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 9 true)) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the canceled hydration to complete with refreshed state."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(nextState.WarningNotice).toEqual (Some(failureMessage failure))

                Vitest
                    .expect(
                        nextState.PendingRecovery
                        |> Option.map (fun recovery ->
                            match recovery with
                            | GitPendingRecovery.RetryMaterialization _ -> true
                            | _ -> false
                        )
                    )
                    .toEqual (Some true)
            }
        )

        Vitest.test (
            "SetCurrentProgress ignores late Git progress after the busy operation has finished",
            fun () -> promise {
                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(sidebarProgress "Receiving objects" 72.))
                        GitState.Empty

                let! messages = collectMessages cmd

                Vitest.expect(nextState.CurrentProgress).toEqual (None)
                Vitest.expect(currentRunStatus nextState).toEqual (None)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "SetCurrentProgress ignores the progress of another operation id while a write runs",
            fun () -> promise {
                let shown = expectedSidebarProgress "Pushing objects" 10.

                let writeState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.CommittingAllChanges
                        BusyNotice = Some "Committing all changes"
                        CurrentOperation = Some { OperationId = "write-1" }
                        CurrentProgress = Some shown
                }

                let lfsProgress = {
                    sidebarProgress "Git LFS file dematerialized" 100. with
                        OperationId = "lfs-action-1"
                }

                let nextState, cmd =
                    update defaultDependencies ignore (SetCurrentProgress(lfsProgress)) writeState

                let! messages = collectMessages cmd

                Vitest.expect(nextState.CurrentProgress).toEqual (Some shown)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "SetCurrentProgress applies the progress of the write's own operation id and of its sub-operations",
            fun () -> promise {
                let writeState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.CommittingAllChanges
                        BusyNotice = Some "Committing all changes"
                        CurrentOperation = Some { OperationId = "write-1" }
                }

                let ownProgress = {
                    sidebarProgress "Writing objects" 40. with
                        OperationId = "write-1"
                }

                let ownState, _ =
                    update defaultDependencies ignore (SetCurrentProgress(ownProgress)) writeState

                Vitest.expect(ownState.CurrentProgress).toEqual (Some(expectedSidebarProgress "Writing objects" 40.))

                let subOperationProgress = {
                    sidebarProgress "Writing objects" 80. with
                        OperationId = "write-1/2"
                }

                let subState, _ =
                    update defaultDependencies ignore (SetCurrentProgress(subOperationProgress)) ownState

                Vitest.expect(subState.CurrentProgress).toEqual (Some(expectedSidebarProgress "Writing objects" 80.))
            }
        )

        Vitest.test (
            "SetCurrentProgress shows no progress during a silent refresh",
            fun () -> promise {
                let silentState, _ =
                    update defaultDependencies ignore RefreshRequestedSilently runningState

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(sidebarProgress "Receiving objects" 72.))
                        silentState

                let! messages = collectMessages cmd

                Vitest.expect(nextState.CurrentProgress).toEqual (None)
                Vitest.expect(currentRunStatus nextState).toEqual (Some GitSidebarRunStatus.BusyWithoutNotice)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "SetCurrentProgress ignores the progress of another operation id during a refresh and applies its own",
            fun () -> promise {
                let operationIds = ResizeArray<string>()

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        newOperationId = fun () -> "refresh-1"
                        getSessionInfo =
                            fun request ->
                                operationIds.Add request.OperationId
                                promise { return Ok(succeeded sessionInfo) }
                }

                let refreshState, refreshCmd = update deps ignore RefreshRequested runningState

                let foreign = {
                    sidebarProgress "Git LFS file dematerialized" 100. with
                        OperationId = "lfs-action-1"
                }

                let ignoredState, _ = update deps ignore (SetCurrentProgress(foreign)) refreshState

                Vitest.expect(ignoredState.CurrentProgress).toEqual (None)

                let own = {
                    sidebarProgress "Receiving objects" 40. with
                        OperationId = "refresh-1/2"
                }

                let appliedState, _ = update deps ignore (SetCurrentProgress(own)) refreshState

                Vitest
                    .expect(appliedState.CurrentProgress)
                    .toEqual (Some(expectedSidebarProgress "Receiving objects" 40.))

                // The calls of the refresh run under the id of the refresh, so its own progress passes the filter.
                let! _ = collectMessages refreshCmd
                Vitest.expect(operationIds.ToArray()).toEqual [| "refresh-1" |]
            }
        )

        Vitest.test (
            "SetCurrentProgress ignores the progress of another operation id while the repository is initialized",
            fun () -> promise {
                let operationIds = ResizeArray<string>()

                let deps = {
                    defaultDependencies with
                        newOperationId = fun () -> "init-1"
                        initializeWorkspace =
                            fun request ->
                                operationIds.Add request.OperationId
                                promise { return Ok(succeeded request.TargetPath) }
                }

                let initState, initCmd = update deps ignore InitRepositoryRequested runningState

                let foreign = {
                    sidebarProgress "Git LFS file dematerialized" 100. with
                        OperationId = "lfs-action-1"
                }

                let ignoredState, _ = update deps ignore (SetCurrentProgress(foreign)) initState

                Vitest.expect(ignoredState.CurrentProgress).toEqual (None)

                let own = {
                    sidebarProgress "Initializing" 40. with
                        OperationId = "init-1"
                }

                let appliedState, _ = update deps ignore (SetCurrentProgress(own)) initState

                Vitest.expect(appliedState.CurrentProgress).toEqual (Some(expectedSidebarProgress "Initializing" 40.))

                let! _ = collectMessages initCmd
                Vitest.expect(operationIds.ToArray()).toEqual [| "init-1" |]
            }
        )

        Vitest.test (
            "SetCurrentProgress ignores the progress of another operation id while a merge resolution is confirmed",
            fun () -> promise {
                let conflict = (conflictedStatus [| "conflict.txt" |]).ActiveConflictSession.Value

                let deps = {
                    defaultDependencies with
                        newOperationId = fun () -> "resolve-1"
                }

                let request = {
                    Path = "conflict.txt"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.SupplyResolvedContent "resolved"
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        SelectedChangePath = Some "conflict.txt"
                }

                let confirmingState, _ =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let foreign = {
                    sidebarProgress "Git LFS file dematerialized" 100. with
                        OperationId = "lfs-action-1"
                }

                let ignoredState, _ =
                    update deps ignore (SetCurrentProgress(foreign)) confirmingState

                Vitest.expect(ignoredState.CurrentProgress).toEqual (None)

                let own = {
                    sidebarProgress "Restoring" 40. with
                        OperationId = "resolve-1/3"
                }

                let appliedState, _ = update deps ignore (SetCurrentProgress(own)) confirmingState

                Vitest.expect(appliedState.CurrentProgress).toEqual (Some(expectedSidebarProgress "Restoring" 40.))
            }
        )

        Vitest.test (
            "SetCurrentProgress shows the display message as the stage",
            fun () -> promise {
                let current = expectedSidebarProgress "Receiving objects" 72.

                let state = {
                    GitState.Empty with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        CurrentProgress = Some current
                }

                let firstState, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(sidebarProgress "Receiving objects" 72.))
                        state

                let nextState, cmd =
                    update defaultDependencies ignore (SetCurrentProgress(sidebarOutput "Resolving deltas")) firstState

                let! messages = collectMessages cmd

                let expectedProgress = expectedSidebarProgress "Resolving deltas" 72.

                Vitest.expect(nextState.CurrentProgress).toEqual (Some expectedProgress)
                Vitest.expect(currentRunStatus nextState).toEqual (Some(GitSidebarRunStatus.Progress expectedProgress))
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "OperationOutputReceived appends the output of the operation that the workflow runs and of its sub-operations",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                        CurrentOperation = Some { OperationId = "pull-1" }
                }

                let first, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "pull-1" [ "command", "git fetch" ]))
                        pullState

                let second, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "pull-1/2" [ "stdout", "done" ]))
                        first

                Vitest.expect(second.CurrentLog.Lines |> Array.map _.Text).toEqual [| "git fetch"; "done" |]
        )

        Vitest.test (
            "OperationOutputReceived ignores the output of another operation id",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                        CurrentOperation = Some { OperationId = "pull-1" }
                }

                let nextState, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "lfs-action-1" [ "command", "git lfs pull" ]))
                        pullState

                Vitest.expect(nextState.CurrentLog.Lines.Length).toBe 0
        )

        Vitest.test (
            "OperationOutputReceived ignores output while nothing is busy",
            fun () ->
                let nextState, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "late-1" [ "stdout", "late" ]))
                        runningState

                Vitest.expect(nextState.CurrentLog.Lines.Length).toBe 0
        )

        Vitest.test (
            "the log and the steps reset when the operation ends",
            fun () ->
                let busyState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.Refreshing
                        BusyNotice = Some "Refreshing"
                }

                let withOutput, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "op-1" [ "stdout", "line" ]))
                        busyState

                let withSteps, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(progressDto "fetch" (Some "Fetching online changes") None None))
                        withOutput

                Vitest.expect(withSteps.CurrentLog.Lines.Length).toBe 1
                Vitest.expect(withSteps.CurrentSteps.Length).toBe 1

                let ended, _ =
                    update
                        defaultDependencies
                        ignore
                        (RefreshCompleted(withSteps.RefreshRequestId, Error "failed"))
                        withSteps

                Vitest.expect(ended.CurrentLog.Lines.Length).toBe 0
                Vitest.expect(ended.CurrentSteps.Length).toBe 0
                Vitest.expect(ended.CurrentStepPhases).toEqual [||]
        )

        Vitest.test (
            "a write labels its log with the busy notice of its operation",
            fun () ->
                let started, _ =
                    update
                        defaultDependencies
                        ignore
                        (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
                        runningState

                Vitest.expect(started.CurrentLogName.IsSome).toBe true

                Vitest
                    .expect(started.CurrentLogName |> Option.map _.Label)
                    .toEqual (busyNoticeFromOperation started.BusyOperation.Value)

                let committing, _ =
                    update
                        defaultDependencies
                        ignore
                        (WriteRequested(CommitAll(prepareCommitAll runningState "msg")))
                        runningState

                Vitest
                    .expect(committing.CurrentLogName |> Option.map _.Label)
                    .toEqual (busyNoticeFromOperation committing.BusyOperation.Value)
        )

        Vitest.test (
            "a save keeps the name of its log when its phase changes",
            fun () ->
                let started, _ =
                    update
                        defaultDependencies
                        ignore
                        (WriteRequested(PrimarySave(prepareCommitAll runningState "msg")))
                        runningState

                Vitest.expect(started.CurrentLogName.IsSome).toBe true

                let afterPhase, _ =
                    update
                        defaultDependencies
                        ignore
                        (WritePhaseChanged(
                            started.ArcSessionId,
                            started.WriteRequestId,
                            GitBusyOperation.PushingToRemote
                        ))
                        started

                Vitest.expect(afterPhase.BusyOperation).toEqual (Some GitBusyOperation.PushingToRemote)
                Vitest.expect(afterPhase.CurrentLogName.IsSome).toBe true
                Vitest.expect(afterPhase.CurrentLogName).toEqual started.CurrentLogName
        )

        Vitest.test (
            "a clone that ends after output keeps the folder of the clone target as the ARC name",
            fun () ->
                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/new-arc.git"
                    DisplayName = Some "new-arc"
                    TargetPath = "C:/data/new-arc"
                    TargetRef = None
                    MaterializeAllObjects = false
                }

                let writeRequest = Clone(cloneRequest, ignore)

                let cloning, _ =
                    update defaultDependencies ignore (WriteRequested writeRequest) runningState

                let withOutput, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "op-1" [ "stderr", "fatal: repository not found" ]))
                        cloning

                let ended, _ =
                    update
                        defaultDependencies
                        ignore
                        (WriteCompleted(
                            withOutput.ArcSessionId,
                            withOutput.WriteRequestId,
                            writeRequest,
                            Error "not found"
                        ))
                        withOutput

                Vitest.expect(ended.LastLog.IsSome).toBe true
                Vitest.expect(ended.LastLog.Value.ArcName).toEqual (Some "new-arc")
        )

        Vitest.test (
            "a write that ends after output keeps the output as the last log",
            fun () ->
                let apply state message =
                    fst (update defaultDependencies ignore message state)

                let before = JS.Constructors.Date.now ()
                let writeRequest = Push GitUpdateAcceptance.RequirePreview
                let started = apply runningState (WriteRequested writeRequest)

                let withOutput =
                    apply started (OperationOutputReceived(outputOf "op-1" [ "command", "git push"; "stdout", "done" ]))

                let ended =
                    apply
                        withOutput
                        (WriteCompleted(
                            withOutput.ArcSessionId,
                            withOutput.WriteRequestId,
                            writeRequest,
                            Error "rejected"
                        ))

                let last = ended.LastLog.Value

                Vitest.expect(last.Label).toBe started.CurrentLogName.Value.Label
                Vitest.expect(last.ArcName).toEqual (Some "arc")
                Vitest.expect(last.FinishedAt >= before).toBe true
                Vitest.expect(last.Log.Lines |> Array.map _.Text).toEqual [| "git push"; "done" |]
                Vitest.expect(ended.CurrentLog.Lines.Length).toBe 0
                Vitest.expect(ended.CurrentLogName).toEqual None
        )

        Vitest.test (
            "a write that succeeds after output keeps the output as the last log",
            fun () ->
                let apply state message =
                    fst (update defaultDependencies ignore message state)

                let writeRequest = Push GitUpdateAcceptance.RequirePreview
                let started = apply runningState (WriteRequested writeRequest)

                let withOutput =
                    apply started (OperationOutputReceived(outputOf "op-1" [ "command", "git push"; "stdout", "done" ]))

                let ended =
                    apply
                        withOutput
                        (WriteCompleted(
                            withOutput.ArcSessionId,
                            withOutput.WriteRequestId,
                            writeRequest,
                            Ok(
                                Completed(
                                    UnitSuccess {
                                        Refresh = refreshed cleanStatus
                                        PageChange = GitPageChange.NoChange
                                        SelectedChangePath = None
                                        Warning = None
                                        Partial = None
                                        Published = None
                                        ObjectsMissing = None
                                    }
                                )
                            )
                        ))

                Vitest.expect(ended.ErrorNotice).toEqual None
                Vitest.expect(ended.BusyOperation).toEqual None
                Vitest.expect(ended.LastLog.IsSome).toBe true
                Vitest.expect(ended.LastLog.Value.Label).toBe started.CurrentLogName.Value.Label
                Vitest.expect(ended.LastLog.Value.Log.Lines |> Array.map _.Text).toEqual [| "git push"; "done" |]
                Vitest.expect(ended.CurrentLog.Lines.Length).toBe 0
                Vitest.expect(ended.CurrentLogName).toEqual None
        )

        Vitest.test (
            "a refresh with output that ends leaves the last log unchanged",
            fun () ->
                let apply state message =
                    fst (update defaultDependencies ignore message state)

                let earlier = finishedLog "Earlier operation"

                let refreshing =
                    apply
                        {
                            runningState with
                                LastLog = Some earlier
                        }
                        RefreshRequested

                Vitest.expect(refreshing.CurrentLogName).toEqual None

                let withOutput =
                    apply refreshing (OperationOutputReceived(outputOf "op-1" [ "stdout", "line" ]))

                Vitest.expect(withOutput.CurrentLog.Lines.Length).toBe 1

                let ended =
                    apply withOutput (RefreshCompleted(withOutput.RefreshRequestId, Error "failed"))

                Vitest.expect(ended.LastLog).toEqual (Some earlier)
        )

        Vitest.test (
            "an operation that ends without output keeps the previous last log",
            fun () ->
                let apply state message =
                    fst (update defaultDependencies ignore message state)

                let earlier = finishedLog "Earlier operation"
                let writeRequest = Push GitUpdateAcceptance.RequirePreview

                let started =
                    apply
                        {
                            runningState with
                                LastLog = Some earlier
                        }
                        (WriteRequested writeRequest)

                let ended =
                    apply
                        started
                        (WriteCompleted(started.ArcSessionId, started.WriteRequestId, writeRequest, Error "rejected"))

                Vitest.expect(ended.LastLog).toEqual (Some earlier)
        )

        Vitest.test (
            "opening another ARC keeps the last log",
            fun () ->
                let earlier = finishedLog "Earlier operation"

                let next, _ =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/other-arc")) {
                        runningState with
                            LastLog = Some earlier
                    }

                Vitest.expect(next.CurrentArcPath).toEqual (Some "C:/other-arc")
                Vitest.expect(next.LastLog).toEqual (Some earlier)
        )

        Vitest.test (
            "opening another ARC while an operation runs keeps its output as the last log",
            fun () ->
                let busy = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.CommittingAllChanges
                        CurrentLogName =
                            busyNoticeFromOperation GitBusyOperation.CommittingAllChanges
                            |> Option.map (fun label ->
                                let name: Renderer.Context.OperationLog.OperationLogName = {
                                    Label = label
                                    ArcName = Some "arc"
                                }

                                name
                            )
                        CurrentOperation = Some { OperationId = "op-1" }
                        CurrentLog =
                            Renderer.Context.OperationLog.append
                                (outputOf "op-1" [ "stdout", "committing" ]).Entries
                                Renderer.Context.OperationLog.empty
                }

                let next, _ =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/other-arc")) busy

                Vitest.expect(next.CurrentArcPath).toEqual (Some "C:/other-arc")
                Vitest.expect(next.LastLog.IsSome).toBe true

                let lines = next.LastLog.Value.Log.Lines

                Vitest.expect(lines.Length).toBe 2
                Vitest.expect(lines[0].Text).toBe "committing"
                Vitest.expect(lines[1].Kind).toBe "notice"
        )

        Vitest.test (
            "failureMessage names the first five affected paths of a path_not_found failure",
            fun () ->
                let paths = [| for index in 1..7 -> $"data/file{index}.txt" |]
                let instructions = "Select existing files and try again."

                let failure =
                    makeFailure
                        NotFound
                        VersionControlCodes.PathNotFound
                        "Some selected paths do not exist."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = Some instructions
                        })
                        paths

                let message = failureMessage failure

                for path in paths[0..4] do
                    Vitest.expect(message).toContain ($"'{path}'")

                Vitest.expect(message).not.toContain (paths[5])
                Vitest.expect(message).not.toContain (paths[6])
                Vitest.expect(message).toContain ("2 more")
                Vitest.expect(message.StartsWith failure.Message).toBe true
                Vitest.expect(message.EndsWith instructions).toBe true
        )

        Vitest.test (
            "failureMessage ignores the affected paths of a failure with another code",
            fun () ->
                let instructions = "Retry the action."

                let failure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.PreconditionFailed
                        "The state changed."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = Some instructions
                        })
                        [| "data/file1.txt"; "data/file2.txt" |]

                Vitest.expect(failureMessage failure).toBe ($"{failure.Message} {instructions}")
        )

        Vitest.test (
            "a phase change of the write keeps the steps and the log and starts the progress over",
            fun () ->
                let busyState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.CommittingAllChanges
                        BusyNotice = Some "Committing all changes"
                        CurrentOperation = Some { OperationId = "op-1" }
                }

                let apply state message =
                    fst (update defaultDependencies ignore message state)

                let running =
                    [
                        SetCurrentProgress(progressDto "commit" (Some "Saving changes") None None)
                        OperationOutputReceived(outputOf "op-1" [ "command", "git commit"; "stdout", "done" ])
                        SetCurrentProgress(progressDto "push" (Some "Uploading changes") None None)
                        SetCurrentProgress(progressDto "push" (Some "Uploading (30/100)") (Some 30.0) (Some 100.0))
                    ]
                    |> List.fold apply busyState

                Vitest.expect(running.CurrentSteps.Length).toBe 2
                Vitest.expect(running.CurrentLog.Lines.Length).toBe 2
                Vitest.expect(running.CurrentProgress.IsSome).toBe true

                let afterPhase =
                    apply
                        running
                        (WritePhaseChanged(
                            running.ArcSessionId,
                            running.WriteRequestId,
                            GitBusyOperation.PushingToRemote
                        ))

                Vitest.expect(afterPhase.CurrentSteps).toEqual running.CurrentSteps
                Vitest.expect(afterPhase.CurrentLog.Lines.Length).toBe 2
                Vitest.expect(afterPhase.CurrentProgress).toEqual None
        )

        Vitest.test (
            "a progress event with a display message and a new phase code starts the next step",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                }

                let events = [
                    progressDto "fetch" (Some "Fetching online changes") None None
                    progressDto "merge" (Some "Merging online changes") None None
                    // A reset has no display message and starts no step.
                    progressDto "refresh-state" None None None
                    progressDto "lfs-pull" (Some "Downloading large files") None None
                ]

                let finalState =
                    events
                    |> List.fold
                        (fun state event -> fst (update defaultDependencies ignore (SetCurrentProgress(event)) state))
                        pullState

                Vitest
                    .expect(finalState.CurrentSteps)
                    .toEqual (
                        [|
                            sidebarStep "Fetching online changes" None None false
                            sidebarStep "Merging online changes" None None false
                            sidebarStep "Downloading large files" None None true
                        |]
                    )
        )

        Vitest.test (
            "progress events of the current phase update the detail and the percent of the current step",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                }

                let apply state event =
                    fst (update defaultDependencies ignore (SetCurrentProgress(event)) state)

                let started =
                    apply pullState (progressDto "merge" (Some "Merging online changes") None None)

                let metered =
                    apply
                        started
                        (progressDto "merge" (Some "Receiving objects (12/80), 1.2 MB") (Some 15.0) (Some 100.0))

                Vitest
                    .expect(metered.CurrentSteps)
                    .toEqual (
                        [|
                            sidebarStep
                                "Merging online changes"
                                (Some "Receiving objects (12/80), 1.2 MB")
                                (Some 15.0)
                                true
                        |]
                    )

                // An event without a message keeps the detail and moves the percent.
                let moved = apply metered (progressDto "merge" None (Some 40.0) (Some 100.0))

                Vitest
                    .expect(moved.CurrentSteps)
                    .toEqual (
                        [|
                            sidebarStep
                                "Merging online changes"
                                (Some "Receiving objects (12/80), 1.2 MB")
                                (Some 40.0)
                                true
                        |]
                    )

                // The label again as message means the step has no extra detail. An event without numbers clears the percent.
                let restated =
                    apply moved (progressDto "merge" (Some "Merging online changes") None None)

                Vitest.expect(restated.CurrentSteps).toEqual ([| sidebarStep "Merging online changes" None None true |])
        )

        Vitest.test (
            "the per-file events of a download for two files give one download step and one checkout step",
            fun () ->
                let deps = {
                    defaultDependencies with
                        newOperationId = fun () -> "op-1"
                }

                let running, _ =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let preparing size =
                    progressDto "lfs-materialize" (Some "Preparing Git LFS materialization") (Some 0.0) (Some size)

                let materialized size =
                    progressDto "lfs-materialize" (Some "Git LFS file materialized") (Some size) (Some size)

                let transferStart = progressDto "lfs-materialize-transfer" None None None

                let transferMeter =
                    progressDto "lfs-materialize-transfer" (Some "Downloading LFS objects (0/1), 1.2 MB") None None

                let transferDone =
                    progressDto
                        "lfs-materialize-transfer"
                        (Some "Downloading LFS objects (1/1), 1.2 MB")
                        (Some 100.0)
                        (Some 100.0)

                let checkoutMeter =
                    progressDto "lfs-materialize-checkout" (Some "Checking out LFS objects (0/1)") None None

                // The preparing event carries numbers but no meter, so it starts no step.
                let prepared = applyProgressEvents running [ preparing 2048.0 ]
                Vitest.expect(prepared.CurrentSteps.Length).toBe 0

                // The meter with numbers sets a percent that the second file must not keep.
                let firstFile =
                    applyProgressEvents prepared [
                        transferStart
                        transferMeter
                        transferDone
                        checkoutMeter
                        materialized 2048.0
                    ]

                Vitest.expect(firstFile.CurrentSteps |> Array.map _.Label).toEqual [|
                    "Downloading LFS objects"
                    "Checking out LFS objects"
                |]

                Vitest.expect(firstFile.CurrentSteps |> Array.map _.IsCurrent).toEqual [| false; true |]
                Vitest.expect(firstFile.CurrentSteps.[0].Percent).toEqual (Some 100.0)

                // The result of a file changes no step.
                let afterResult = applyProgressEvents firstFile [ materialized 4096.0 ]
                Vitest.expect(afterResult.CurrentSteps).toEqual firstFile.CurrentSteps

                // The second file reaches its transfer, which makes the download step current again.
                let secondStart = applyProgressEvents firstFile [ preparing 4096.0; transferStart ]

                Vitest.expect(secondStart.CurrentSteps.Length).toBe 2
                Vitest.expect(secondStart.CurrentSteps |> Array.map _.IsCurrent).toEqual [| true; false |]

                let secondFile =
                    applyProgressEvents secondStart [ transferMeter; checkoutMeter; materialized 4096.0 ]

                Vitest.expect(secondFile.CurrentSteps |> Array.map _.Label).toEqual [|
                    "Downloading LFS objects"
                    "Checking out LFS objects"
                |]

                Vitest.expect(secondFile.CurrentSteps |> Array.map _.IsCurrent).toEqual [| false; true |]
                Vitest.expect(secondFile.CurrentSteps |> Array.map _.Percent).toEqual [| None; None |]

                Vitest.expect(secondFile.CurrentSteps.[1].Detail).toEqual (Some "Checking out LFS objects (0/1)")
        )

        Vitest.test (
            "a meter with numbers starts a step labelled with the meter label when its phase has no step",
            fun () ->
                let pushState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        BusyNotice = Some "Pushing to remote"
                }

                let metered =
                    applyProgressEvents pushState [
                        progressDto "lfs-upload" (Some "Uploading LFS objects (45/100)") (Some 45.0) (Some 100.0)
                    ]

                Vitest
                    .expect(metered.CurrentSteps)
                    .toEqual (
                        [|
                            sidebarStep "Uploading LFS objects" (Some "Uploading LFS objects (45/100)") (Some 45.0) true
                        |]
                    )
        )

        Vitest.test (
            "stepLabelOf keeps the text before the first parenthesis of a meter and any other message as it is",
            fun () ->
                Vitest.expect(stepLabelOf "Receiving objects (12/80), 1.2 MB").toBe "Receiving objects"
                Vitest.expect(stepLabelOf "Merging online changes").toBe "Merging online changes"
        )

        Vitest.test (
            "a synchronize sequence shows one step per phase with the meter text as detail",
            fun () ->
                let pullState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PullingFromRemote
                        BusyNotice = Some "Pulling from remote"
                }

                let finalState =
                    applyProgressEvents pullState [
                        progressDto "fetch" (Some "Fetching online changes") None None
                        progressDto "fetch" (Some "Receiving objects (12/80)") (Some 15.0) (Some 100.0)
                        progressDto "merge" (Some "Merging online changes") None None
                        progressDto "lfs-pull" (Some "Downloading large files") None None
                        progressDto "lfs-pull" (Some "Downloading (3/10)") (Some 30.0) (Some 100.0)
                        progressDto "lfs-pull-checkout" (Some "Checking out large files") None None
                        progressDto "lfs-pull-checkout" (Some "Checking out (5/10)") (Some 50.0) (Some 100.0)
                    ]

                Vitest
                    .expect(finalState.CurrentSteps)
                    .toEqual (
                        [|
                            sidebarStep "Fetching online changes" (Some "Receiving objects (12/80)") (Some 15.0) false
                            sidebarStep "Merging online changes" None None false
                            sidebarStep "Downloading large files" (Some "Downloading (3/10)") (Some 30.0) false
                            sidebarStep "Checking out large files" (Some "Checking out (5/10)") (Some 50.0) true
                        |]
                    )
        )

        Vitest.test (
            "SetCurrentProgress returns the same model while nothing is busy",
            fun () ->
                let idle, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(sidebarProgress "Receiving objects" 72.))
                        runningState

                Vitest.expect(obj.ReferenceEquals(idle, runningState)).toBe true
        )

        Vitest.test (
            "SetCurrentProgress ignores the progress of a replaced refresh once a newer refresh has started",
            fun () ->
                let operationIds = ResizeArray<string>()

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        newOperationId =
                            fun () ->
                                let id = $"refresh-{operationIds.Count + 1}"
                                operationIds.Add id
                                id
                }

                let firstRefresh, _ = update deps ignore RefreshRequested runningState
                let secondRefresh, _ = update deps ignore RefreshRequested firstRefresh

                Vitest.expect(secondRefresh.RefreshOperationId).toEqual (Some "refresh-2")

                let replaced = {
                    sidebarProgress "Receiving objects" 40. with
                        OperationId = "refresh-1/2"
                }

                let ignoredState, _ =
                    update deps ignore (SetCurrentProgress(replaced)) secondRefresh

                Vitest.expect(ignoredState.CurrentProgress).toEqual (None)
                Vitest.expect(ignoredState.OperationReported).toBe false

                let current = {
                    sidebarProgress "Receiving objects" 60. with
                        OperationId = "refresh-2/2"
                }

                let appliedState, _ = update deps ignore (SetCurrentProgress(current)) secondRefresh

                Vitest
                    .expect(appliedState.CurrentProgress)
                    .toEqual (Some(expectedSidebarProgress "Receiving objects" 60.))
        )

        Vitest.test (
            "SetCurrentProgress ignores progress and output while the ARC folder is renamed",
            fun () ->
                let renaming = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.RenamingRepository
                        BusyNotice = Some "Renaming"
                }

                let afterProgress, _ =
                    update
                        defaultDependencies
                        ignore
                        (SetCurrentProgress(sidebarProgress "Receiving objects" 72.))
                        renaming

                Vitest.expect(afterProgress.CurrentProgress).toEqual (None)
                Vitest.expect(afterProgress.CurrentSteps.Length).toBe 0
                Vitest.expect(afterProgress.OperationReported).toBe false

                let afterOutput, _ =
                    update
                        defaultDependencies
                        ignore
                        (OperationOutputReceived(outputOf "op-1" [ "stdout", "line" ]))
                        renaming

                Vitest.expect(afterOutput.CurrentLog.Lines.Length).toBe 0
                Vitest.expect(afterOutput.OperationReported).toBe false
        )

        Vitest.test (
            "a write marks the operation as reported on its first own progress or output and not on other operations",
            fun () ->
                let deps = {
                    defaultDependencies with
                        newOperationId = fun () -> "op-1"
                }

                let started, _ =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                Vitest.expect(started.OperationReported).toBe false

                let foreignProgress = {
                    sidebarProgress "Git LFS file dematerialized" 100. with
                        OperationId = "lfs-action-1"
                }

                let afterForeign, _ =
                    update deps ignore (SetCurrentProgress(foreignProgress)) started

                let afterForeignOutput, _ =
                    update deps ignore (OperationOutputReceived(outputOf "lfs-action-1" [ "stdout", "x" ])) started

                Vitest.expect(afterForeign.OperationReported).toBe false
                Vitest.expect(afterForeignOutput.OperationReported).toBe false

                let afterProgress, _ =
                    update deps ignore (SetCurrentProgress(sidebarProgress "Pushing objects" 10.)) started

                let afterOutput, _ =
                    update deps ignore (OperationOutputReceived(outputOf "op-1/2" [ "stdout", "x" ])) started

                Vitest.expect(afterProgress.OperationReported).toBe true
                Vitest.expect(afterOutput.OperationReported).toBe true
        )

        Vitest.test (
            "the reported flag survives a write phase change and the flag and the start time reset when the operation ends",
            fun () ->
                let deps = {
                    defaultDependencies with
                        newOperationId = fun () -> "op-1"
                }

                let before = JS.Constructors.Date.now ()

                let started, _ =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let after = JS.Constructors.Date.now ()

                Vitest
                    .expect(
                        started.OperationStartedAt
                        |> Option.exists (fun startedAt -> startedAt >= before && startedAt <= after)
                    )
                    .toBe
                    true

                let reported, _ =
                    update deps ignore (SetCurrentProgress(sidebarProgress "Pushing objects" 10.)) started

                let nextPhase, _ =
                    update
                        deps
                        ignore
                        (WritePhaseChanged(
                            started.ArcSessionId,
                            started.WriteRequestId,
                            GitBusyOperation.PullingFromRemote
                        ))
                        reported

                // The bar starts over with the phase, while the flag and the start time stay.
                Vitest.expect(nextPhase.CurrentProgress).toEqual None
                Vitest.expect(nextPhase.OperationReported).toBe true
                Vitest.expect(nextPhase.OperationStartedAt).toEqual started.OperationStartedAt

                let ended, _ =
                    update
                        deps
                        ignore
                        (WriteCompleted(
                            started.ArcSessionId,
                            started.WriteRequestId,
                            Push GitUpdateAcceptance.RequirePreview,
                            Error "failed"
                        ))
                        nextPhase

                Vitest.expect(ended.BusyOperation).toEqual None
                Vitest.expect(ended.OperationReported).toBe false
                Vitest.expect(ended.OperationStartedAt).toEqual None
        )

        Vitest.test (
            "Dismissing the lock dialog resumes the publish that the download held back",
            fun () -> promise {
                let recovery = GitPendingRecovery.ClearStaleLock None

                let state = {
                    runningState with
                        PendingRecovery = Some recovery
                        PendingRemoteAction = GitPendingRemoteAction.Recover
                        RetryMaterializationAfterLockClear = Some "Retry materialization."
                        PendingPostMergePush = true
                }

                let nextState, cmd =
                    update defaultDependencies ignore DismissRecoveryRequested state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.PendingRecovery).toEqual None
                Vitest.expect(nextState.RetryMaterializationAfterLockClear).toEqual None
                Vitest.expect(nextState.PendingPostMergePush).toBe false

                Vitest
                    .expect(messages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "WriteRequested clears stale transfer progress before showing the new operation notice",
            fun () -> promise {
                let state = {
                    runningState with
                        BusyOperation = None
                        CurrentProgress = Some(expectedSidebarProgress "Receiving objects" 72.)
                }

                let nextState, _ =
                    update defaultDependencies ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) state

                Vitest.expect(nextState.CurrentProgress).toEqual (None)
                Vitest.expect(currentRunStatus nextState).toEqual (Some(GitSidebarRunStatus.Busy "Pushing to remote"))
            }
        )

        Vitest.test (
            "SaveDownloadLargeFilesRequested updates local state immediately when no ARC is loaded",
            fun () -> promise {
                let nextState, cmd =
                    update defaultDependencies ignore (SaveDownloadLargeFilesRequested true) GitState.Empty

                let! messages = collectMessages cmd

                Vitest.expect(nextState.DownloadLargeFiles).toBe (true)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "UserDefaultsLoaded seeds the git values of a state without an ARC and a reset keeps the seed",
            fun () -> promise {
                let seeded, cmd =
                    update defaultDependencies ignore (UserDefaultsLoaded changedUserDefaults) GitState.Empty

                let! messages = collectMessages cmd
                let reset, _ = update defaultDependencies ignore RefreshRequested seeded

                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(seeded.UserDefaults).toEqual (changedUserDefaults)
                Vitest.expect(seeded.LfsAutoTrackThresholdMb).toBe (7)
                Vitest.expect(seeded.DiffIndexingLimitMb).toBe (2048)
                Vitest.expect(seeded.UploadRightAway).toBe (false)
                // A clone without an ARC takes this value.
                Vitest.expect(seeded.DownloadLargeFiles).toBe (true)
                Vitest.expect(reset.UserDefaults).toEqual (changedUserDefaults)
                Vitest.expect(reset.DownloadLargeFiles).toBe (true)
                Vitest.expect(reset.LfsAutoTrackThresholdMb).toBe (7)
            }
        )

        Vitest.test (
            "UserDefaultsLoaded leaves the values of an open ARC alone and changes the seed",
            fun () -> promise {
                let nextState, cmd =
                    update defaultDependencies ignore (UserDefaultsLoaded changedUserDefaults) runningState

                let! messages = collectMessages cmd

                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(nextState.UserDefaults).toEqual (changedUserDefaults)
                Vitest.expect(nextState.LfsAutoTrackThresholdMb).toBe (runningState.LfsAutoTrackThresholdMb)
                Vitest.expect(nextState.DownloadLargeFiles).toBe (runningState.DownloadLargeFiles)
                Vitest.expect(nextState.DiffIndexingLimitMb).toBe (runningState.DiffIndexingLimitMb)
                Vitest.expect(nextState.UploadRightAway).toBe (runningState.UploadRightAway)
            }
        )

        Vitest.test (
            "The seed of UserDefaultsLoaded survives a change of the ARC and starts the new session",
            fun () -> promise {
                let seeded, _ =
                    update defaultDependencies ignore (UserDefaultsLoaded changedUserDefaults) runningState

                let opened, _ =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/arc-b")) seeded

                let closed, _ = update defaultDependencies ignore (ArcPathChanged None) opened
                let reset, _ = update defaultDependencies ignore ResetWorkflow opened

                for state in [| opened; closed; reset |] do
                    Vitest.expect(state.UserDefaults).toEqual (changedUserDefaults)
                    Vitest.expect(state.LfsAutoTrackThresholdMb).toBe (7)
                    Vitest.expect(state.DownloadLargeFiles).toBe (true)
                    Vitest.expect(state.DiffIndexingLimitMb).toBe (2048)
                    Vitest.expect(state.UploadRightAway).toBe (false)
                    Vitest.expect(state.UploadRightAwayLoad).toEqual (UploadRightAwayLoad.NotStarted)
            }
        )

        Vitest.test (
            "A refresh result overrides the values seeded from the user defaults",
            fun () -> promise {
                let deps = {
                    withRefresh cleanStatus defaultDependencies with
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 3 false)) }
                }

                let seeded, _ =
                    update deps ignore (UserDefaultsLoaded changedUserDefaults) GitState.Empty

                let! bound, bindMessages = step deps seeded (ArcPathChanged(Some "C:/arc"))
                let! refreshing, refreshMessages = step deps bound bindMessages[0]
                let! refreshed, _ = step deps refreshing refreshMessages[0]

                Vitest.expect(bound.LfsAutoTrackThresholdMb).toBe (7)
                Vitest.expect(bound.DownloadLargeFiles).toBe (true)
                Vitest.expect(refreshed.LfsAutoTrackThresholdMb).toBe (3)
                Vitest.expect(refreshed.DownloadLargeFiles).toBe (false)
                Vitest.expect(refreshed.UserDefaults).toEqual (changedUserDefaults)
            }
        )

        Vitest.test (
            "InitRepositoryCompleted clears the missing-repository state and schedules a refresh",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        initializeWorkspace = fun request -> promise { return Ok(succeeded request.TargetPath) }
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        RepositoryAvailability = GitRepositoryAvailability.MissingRepository
                }

                let stateAfterRequest, requestCmd = update deps ignore InitRepositoryRequested state
                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| InitRepositoryCompleted(_, Ok _) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected repository initialization to complete."

                let! messages = collectMessages finishCmd

                Vitest.expect(nextState.RepositoryAvailability).toEqual (GitRepositoryAvailability.Ready)
                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "WriteRequested allows clone requests when no ARC is loaded",
            fun () -> promise {
                let mutable replyResult = None
                let reply result = replyResult <- Some result

                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let deps = {
                    defaultDependencies with
                        cloneWorkspace = fun _ -> promise { return Ok(succeeded "C:/clone-target") }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Clone _, Ok(Completed(CloneSuccess root))) |] ->
                        Vitest.expect(root).toBe ("C:/clone-target")
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected clone to return the workspace root."

                let! _ = collectMessages finishCmd

                Vitest
                    .expect(stateAfterRequest.BusyOperation)
                    .toEqual (Some(GitBusyOperation.CloningRepository "C:/clone-target"))

                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(replyResult).toEqual (Some(Ok "C:/clone-target"))
            }
        )

        Vitest.test (
            "A failed clone that changed the target reports its own message without a refresh",
            fun () -> promise {
                let failure = {
                    makeFailure
                        ProviderError
                        "git_failure"
                        "The clone failed after changing the target."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveCloneTarget
                            Instructions = Some "Remove the target before retrying the clone."
                        })
                        [||] with
                        StateChanged = true
                }

                let mutable replyResult = None
                let reply result = replyResult <- Some result
                let refreshCalls = ResizeArray<string>()

                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let deps = {
                    defaultDependencies with
                        getSessionInfo =
                            fun _ ->
                                refreshCalls.Add("getSessionInfo")
                                promise { return Ok(succeeded sessionInfo) }
                        getStatus =
                            fun _ ->
                                refreshCalls.Add("getStatus")
                                promise { return Ok(succeeded cleanStatus) }
                        listRefs = fun _ -> promise { return Ok(succeeded refs) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                        cloneWorkspace = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let! completionMessages = collectMessages requestCmd

                let _, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Clone _, Error message) |] ->
                        Vitest.expect(message).toBe (failureMessage failure)
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the failed clone to report its operation message."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(refreshCalls |> Seq.toArray).toEqual ([||])
                Vitest.expect(replyResult).toEqual (Some(Error(failureMessage failure)))
            }
        )

        Vitest.test (
            "A partial clone reports the missing large files",
            fun () -> promise {
                let failure =
                    makeFailure
                        ProviderError
                        "lfs_pull_failed"
                        "The large-file download failed."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [||]

                let mutable replyResult = None
                let reply result = replyResult <- Some result

                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let deps = {
                    defaultDependencies with
                        cloneWorkspace =
                            fun _ -> promise {
                                return Ok(OperationResultDto.PartiallySucceeded(operation "C:/clone-target", failure))
                            }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let! completionMessages = collectMessages requestCmd

                let _, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Clone _, Error message) |] ->
                        Vitest
                            .expect(message)
                            .toBe (
                                "The ARC was cloned to 'C:/clone-target', but its large files could not be downloaded. Open the folder and use Download LFS file to get them."
                            )

                        update deps ignore completionMessages[0] stateAfterRequest
                    | [| WriteCompleted(_, _, Clone _, Ok(Completed(CloneSuccess _))) |] ->
                        failwith "A partial clone must not complete CloneSuccess."
                    | _ -> failwith "Expected a partial clone to report an error."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(finishMessages).toEqual ([||])

                Vitest
                    .expect(replyResult)
                    .toEqual (
                        Some(
                            Error(
                                "The ARC was cloned to 'C:/clone-target', but its large files could not be downloaded. Open the folder and use Download LFS file to get them."
                            )
                        )
                    )
            }
        )

        Vitest.test (
            "A binding persistence partial clone completes CloneSuccess",
            fun () -> promise {
                let failure =
                    makeFailure
                        ProviderError
                        VersionControlCodes.BindingNotPersisted
                        "The binding could not be saved."
                        (Some {
                            Code = VersionControlCodes.Recovery.ReopenWorkspace
                            Instructions = Some "Open the workspace again so it is bound."
                        })
                        [||]

                let failure = {
                    failure with
                        StateChanged = true
                        Retryable = true
                }

                let mutable replyResult = None
                let reply result = replyResult <- Some result

                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let deps = {
                    defaultDependencies with
                        cloneWorkspace =
                            fun _ -> promise {
                                return Ok(OperationResultDto.PartiallySucceeded(operation "C:/clone-target", failure))
                            }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let! completionMessages = collectMessages requestCmd

                let _, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Clone _, Ok(Completed(CloneSuccess root))) |] ->
                        Vitest.expect(root).toBe ("C:/clone-target")
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected a binding persistence partial clone to complete successfully."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(replyResult).toEqual (Some(Ok "C:/clone-target"))
            }
        )

        Vitest.test (
            "A clone runs under the operation id allocated for cancellation",
            fun () -> promise {
                let mutable capturedRequest = None
                let mutable canceledKey = None
                let reply (_: Result<string, string>) = ()

                let cloneRequest = {
                    OperationId = "caller-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let deps = {
                    defaultDependencies with
                        newOperationId = fun () -> "allocated-op"
                        cloneWorkspace =
                            fun request ->
                                capturedRequest <- Some request
                                promise { return Ok(succeeded "C:/clone-target") }
                        cancelOperation =
                            fun key ->
                                canceledKey <- Some key
                                promise { return Ok true }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let _, cancelCmd =
                    update deps ignore CancelCurrentOperationRequested stateAfterRequest

                let! _ = collectMessages cancelCmd
                let! completionMessages = collectMessages requestCmd

                match completionMessages with
                | [| WriteCompleted(_, _, Clone _, Ok(Completed(CloneSuccess root))) |] ->
                    Vitest.expect(root).toBe ("C:/clone-target")
                | _ -> failwith "Expected clone to complete successfully."

                Vitest.expect(capturedRequest.Value.OperationId).toBe ("allocated-op")
                Vitest.expect(capturedRequest.Value.OperationId).not.toBe (cloneRequest.OperationId)
                Vitest.expect(canceledKey.Value.OperationId).toBe (capturedRequest.Value.OperationId)
                Vitest.expect(canceledKey.Value.OperationId).not.toBe (cloneRequest.OperationId)
            }
        )

        Vitest.test (
            "clone progress updates the current run status while the start-page clone is busy",
            fun () -> promise {
                let reply (_: Result<string, string>) = ()

                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let stateAfterRequest, _ =
                    update defaultDependencies ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let progress = sidebarProgress "Receiving objects" 72.
                let expectedProgress = expectedSidebarProgress "Receiving objects" 72.

                let nextState, cmd =
                    update defaultDependencies ignore (SetCurrentProgress(progress)) stateAfterRequest

                let! messages = collectMessages cmd

                Vitest.expect(nextState.CurrentProgress).toEqual (Some expectedProgress)
                Vitest.expect(currentRunStatus nextState).toEqual (Some(GitSidebarRunStatus.Progress expectedProgress))
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "WriteCompleted applies the refreshed snapshot after push success",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    promise { return Ok(succeeded cleanStatus.Synchronization.Value) }
                                else
                                    unexpectedPromise "unexpected pull"
                        getStatus = fun _ -> promise { return Ok(succeeded (statusForBranch "feature/pushed")) }
                        listRefs =
                            fun _ -> promise { return Ok(succeeded [| localBranch "feature/pushed" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 9 true)) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected push to complete successfully."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.Status.CurrentBranch).toEqual (Some "feature/pushed")

                Vitest
                    .expect(
                        nextState.BranchOptions
                        |> Array.exists (fun branch -> branch.RefName = "feature/pushed")
                    )
                    .toBe (true)

                Vitest.expect(nextState.LfsAutoTrackThresholdMb).toBe (9)
                Vitest.expect(nextState.DownloadLargeFiles).toBe (true)
            }
        )

        Vitest.test (
            "SubmitPublishRenameRequested defers push until the renamed ARC path is refreshed",
            fun () -> promise {
                let renamedNames = ResizeArray<string>()

                let deps = {
                    defaultDependencies with
                        renameOpenArcRoot =
                            fun newName ->
                                renamedNames.Add newName
                                promise { return Ok "C:/work/Renamed ARC" }
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    promise { return Ok(succeeded cleanStatus.Synchronization.Value) }
                                else
                                    unexpectedPromise "unexpected pull"
                        getStatus = fun _ -> promise { return Ok(succeeded (statusForBranch "main")) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let initialState = {
                    runningState with
                        CurrentArcPath = Some "C:/work/Existing ARC"
                        PendingPublishRename =
                            Some {
                                CurrentName = "Existing ARC"
                                Message = "A DataHUB repository named 'Existing ARC' already exists."
                                Owners = [||]
                                SelectedNamespaceId = None
                            }
                }

                let stateAfterSubmit, renameCmd =
                    update
                        deps
                        ignore
                        (SubmitPublishRenameRequested {
                            Name = " Renamed ARC "
                            NamespaceId = None
                        })
                        initialState

                let! renameMessages = collectMessages renameCmd

                let stateAfterRename, completionCmd =
                    match renameMessages with
                    | [| PublishRenameCompleted(_, Ok "C:/work/Renamed ARC", _) |] ->
                        update deps ignore renameMessages[0] stateAfterSubmit
                    | _ -> failwith "Expected rename completion to be dispatched."

                let! completionMessages = collectMessages completionCmd

                let stateAfterPathChange, pathChangeCmd =
                    update deps ignore (ArcPathChanged(Some "C:/work/Renamed ARC")) stateAfterRename

                let! pathChangeMessages = collectMessages pathChangeCmd

                let refreshingState, refreshCmd =
                    match pathChangeMessages with
                    | [| RefreshRequested |] -> update deps ignore RefreshRequested stateAfterPathChange
                    | _ -> failwith "Expected the path change to start a refresh."

                let! refreshMessages = collectMessages refreshCmd

                let refreshedState, afterRefreshCmd =
                    match refreshMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message refreshingState
                    | _ -> failwith "Expected the refresh to complete."

                let! afterRefreshMessages = collectMessages afterRefreshCmd

                Vitest.expect(renamedNames |> Seq.toArray).toEqual ([| "Renamed ARC" |])
                Vitest.expect(stateAfterSubmit.BusyOperation).toEqual (Some GitBusyOperation.RenamingRepository)
                Vitest.expect(stateAfterRename.PendingPublishRename).toEqual (None)
                Vitest.expect(stateAfterRename.PendingPublishForPath).toEqual (Some "C:/work/Renamed ARC")
                Vitest.expect(completionMessages).toEqual ([||])
                Vitest.expect(stateAfterPathChange.PendingPublishAfterRefresh).toBe (true)
                Vitest.expect(stateAfterPathChange.PendingPublishForPath).toEqual (None)
                Vitest.expect(refreshedState.PendingPublishAfterRefresh).toBe (false)

                Vitest
                    .expect(afterRefreshMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )

                let stateAfterPush, pushCmd =
                    match afterRefreshMessages with
                    | [| WriteRequested(Push GitUpdateAcceptance.RequirePreview) |] ->
                        update deps ignore afterRefreshMessages[0] refreshedState
                    | _ -> failwith "Expected push to start after the refresh."

                let! pushMessages = collectMessages pushCmd

                let finalState, finishCmd =
                    match pushMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore pushMessages[0] stateAfterPush
                    | _ -> failwith "Expected retried push to complete successfully."

                let! _ = collectMessages finishCmd

                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "PublishRenameCompleted before the path change defers the publish until the refresh of the new path",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        getStatus = fun _ -> promise { return Ok(succeeded (statusForBranch "main")) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/work/Existing ARC"
                        ArcSessionId = 7
                        BusyOperation = Some GitBusyOperation.RenamingRepository
                        BusyNotice = Some "Renaming ARC"
                        PendingPublishRename =
                            Some {
                                CurrentName = "Existing ARC"
                                Message = "A DataHUB repository named 'Existing ARC' already exists."
                                Owners = [||]
                                SelectedNamespaceId = None
                            }
                }

                let afterRename, renameCmd =
                    update deps ignore (PublishRenameCompleted(7, Ok "C:/work/Renamed ARC", noOwnerSelection)) state

                let! renameMessages = collectMessages renameCmd

                Vitest.expect(renameMessages).toEqual ([||])
                Vitest.expect(afterRename.PendingPublishForPath).toEqual (Some "C:/work/Renamed ARC")

                let afterPathChange, pathChangeCmd =
                    update deps ignore (ArcPathChanged(Some "C:/work/Renamed ARC")) afterRename

                let! pathChangeMessages = collectMessages pathChangeCmd

                Vitest.expect(afterPathChange.PendingPublishAfterRefresh).toBe (true)
                Vitest.expect(afterPathChange.PendingPublishForPath).toEqual (None)
                Vitest.expect(pathChangeMessages).toEqual ([| RefreshRequested |])

                let refreshingState, refreshCmd =
                    update deps ignore pathChangeMessages[0] afterPathChange

                let! refreshMessages = collectMessages refreshCmd

                let refreshedState, afterRefreshCmd =
                    match refreshMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message refreshingState
                    | _ -> failwith "Expected the refresh to complete."

                let! afterRefreshMessages = collectMessages afterRefreshCmd

                Vitest.expect(refreshedState.PendingPublishAfterRefresh).toBe (false)

                Vitest
                    .expect(afterRefreshMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "PublishRenameCompleted still retries push when path-change update arrives first",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        getStatus = fun _ -> promise { return Ok(succeeded (statusForBranch "main")) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/work/Existing ARC"
                        ArcSessionId = 7
                        BusyOperation = Some GitBusyOperation.RenamingRepository
                        BusyNotice = Some "Renaming ARC"
                        PendingPublishRename =
                            Some {
                                CurrentName = "Existing ARC"
                                Message = "A DataHUB repository named 'Existing ARC' already exists."
                                Owners = [||]
                                SelectedNamespaceId = None
                            }
                }

                let stateAfterPathChange, pathChangeCmd =
                    update deps ignore (ArcPathChanged(Some "C:/work/Renamed ARC")) state

                let! pathChangeMessages = collectMessages pathChangeCmd

                let stateAfterRenameCompletion, retryCmd =
                    update
                        deps
                        ignore
                        (PublishRenameCompleted(7, Ok "C:/work/Renamed ARC", noOwnerSelection))
                        stateAfterPathChange

                let! retryMessages = collectMessages retryCmd

                // The refresh started by the path change loads the workspace token, and only
                // then does the publish run.
                let refreshingState, refreshCmd =
                    match pathChangeMessages with
                    | [| RefreshRequested |] -> update deps ignore RefreshRequested stateAfterRenameCompletion
                    | _ -> failwith "Expected the path change to start a refresh."

                let! refreshMessages = collectMessages refreshCmd

                let refreshedState, afterRefreshCmd =
                    match refreshMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message refreshingState
                    | _ -> failwith "Expected the refresh to complete."

                let! afterRefreshMessages = collectMessages afterRefreshCmd

                Vitest.expect(stateAfterPathChange.ArcSessionId).not.toBe (7)
                Vitest.expect(stateAfterRenameCompletion.CurrentArcPath).toEqual (Some "C:/work/Renamed ARC")
                Vitest.expect(stateAfterRenameCompletion.ErrorNotice).toEqual (None)
                Vitest.expect(retryMessages.Length).toBe (1)

                Vitest
                    .expect(
                        retryMessages
                        |> hasMessage (
                            function
                            | RefreshRequested -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)

                Vitest.expect(stateAfterRenameCompletion.PendingPublishAfterRefresh).toBe (true)
                Vitest.expect(refreshedState.PendingPublishAfterRefresh).toBe (false)

                Vitest
                    .expect(afterRefreshMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "WriteCompleted ignores late results from the previous ARC",
            fun () -> promise {
                let oldSessionState = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        WorkspaceVersion = Some "v1"
                }

                let stateAfterRequest, _ =
                    update
                        defaultDependencies
                        ignore
                        (WriteRequested(Push GitUpdateAcceptance.RequirePreview))
                        oldSessionState

                let switchedState, switchCmd =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/arc-b")) stateAfterRequest

                let! _ = collectMessages switchCmd

                let staleRefresh = {
                    Session = Ok sessionInfo
                    Status = Ok(statusForBranch "feature/old-arc")
                    Refs = Ok [| localBranch "feature/old-arc" true true |]
                    LfsSettings = Ok(lfsSettings 13 true)
                    OriginRemoteRepositoryWebUrl = None
                }

                let nextState, finishCmd =
                    update
                        defaultDependencies
                        ignore
                        (WriteCompleted(
                            oldSessionState.ArcSessionId,
                            stateAfterRequest.WriteRequestId,
                            Push GitUpdateAcceptance.RequirePreview,
                            Ok(
                                Completed(
                                    UnitSuccess {
                                        Refresh = staleRefresh
                                        PageChange = GitPageChange.NoChange
                                        SelectedChangePath = None
                                        Warning = None
                                        Partial = None
                                        Published = None
                                        ObjectsMissing = None
                                    }
                                )
                            )
                        ))
                        switchedState

                let! _ = collectMessages finishCmd

                Vitest
                    .expect(nextState)
                    .toEqual (
                        {
                            GitState.Empty with
                                CurrentArcPath = Some "C:/arc-b"
                                ArcSessionId = 1
                                RefreshRequestId = 1
                                PageLoadRequestId = 1
                                ExternalRefreshGeneration = 1
                        }
                    )
            }
        )

        Vitest.test (
            "WriteInstallPromptAnswered(false) clears progress and records the cancellation error",
            fun () -> promise {
                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        CurrentProgress = Some(expectedSidebarProgress "pushing" 50.)
                        InstallRetryState =
                            GitInstallRetryState.PromptingForInstall(
                                "Install Git LFS now?",
                                GitBusyOperation.PushingToRemote
                            )
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (WriteInstallPromptAnswered(
                            state.ArcSessionId,
                            Push GitUpdateAcceptance.RequirePreview,
                            "git-lfs-configuration",
                            false
                        ))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(nextState.CurrentProgress).toEqual (None)

                Vitest
                    .expect(nextState.ErrorNotice)
                    .toEqual (Some "The version control dependency 'git-lfs-configuration' is required to continue.")

                Vitest.expect(currentRunStatus nextState).toEqual (None)
            }
        )

        Vitest.test (
            "WriteInstallCompleted clears progress when the installer reports failure",
            fun () -> promise {
                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-a"
                        BusyOperation = Some(GitBusyOperation.InstallingDependency "git-lfs-configuration")
                        CurrentProgress = Some(expectedSidebarProgress "installing" 75.)
                        InstallRetryState = GitInstallRetryState.InstallingForRetry GitBusyOperation.PushingToRemote
                }

                let failure =
                    makeFailure ProviderError "dependency_install_failed" "Git LFS installation failed." None [||]

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (WriteInstallCompleted(
                            state.ArcSessionId,
                            Push GitUpdateAcceptance.RequirePreview,
                            Ok(OperationResultDto.Failed failure)
                        ))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.BusyOperation).toEqual (None)
                Vitest.expect(nextState.CurrentProgress).toEqual (None)
                Vitest.expect(nextState.ErrorNotice).toEqual (Some "Git LFS installation failed.")
                Vitest.expect(currentRunStatus nextState).toEqual (None)
            }
        )

        Vitest.test (
            "A stale-version completion of a superseded write is ignored",
            fun () -> promise {
                let model = {
                    runningState with
                        WriteRequestId = 2
                        BusyOperation = Some GitBusyOperation.DiscardingSelectedChanges
                        ErrorNotice = Some "keep this notice"
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (WriteCompleted(
                            model.ArcSessionId,
                            model.WriteRequestId - 1,
                            DiscardSelection [| "a.txt" |],
                            Ok(StaleWorkspaceVersion("old", false))
                        ))
                        model

                let! messages = collectMessages cmd

                Vitest.expect(nextState.BusyOperation).toEqual (model.BusyOperation)
                Vitest.expect(nextState.ErrorNotice).toEqual (model.ErrorNotice)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "WriteCompleted ignores stale non-clone writes without follow-up callback work",
            fun () -> promise {
                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc-b"
                        ArcSessionId = 2
                        WriteRequestId = 1
                }

                let nextState, cmd =
                    update defaultDependencies ignore (WriteCompleted(1, 1, Fetch, Error "stale fetch failed")) state

                let! messages = collectMessages cmd

                Vitest.expect(nextState).toEqual (state)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "Primary save commits locally and synchronizes once",
            fun () -> promise {
                let mutable revisionCalls = 0
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()
                let sync = cleanStatus.Synchronization.Value

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision =
                            fun _ ->
                                revisionCalls <- revisionCalls + 1
                                promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request
                                promise { return Ok(succeededWithPublication PublicationStateDto.Published sync) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Add polish") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request to enqueue a write request."

                let! completionMessages = collectWriteMessages finishCmd

                let finalState, finalCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the primary save flow to complete successfully."

                let! _ = collectMessages finalCmd

                Vitest.expect(revisionCalls).toBe (1)
                Vitest.expect(synchronizeRequests.Count).toBe (1)
                Vitest.expect(synchronizeRequests[0].ExpectedWorkspaceVersion).toBe ("v1")
                Vitest.expect(synchronizeRequests[0].ExpectedTargetRevision).toEqual (None)
                Vitest.expect(synchronizeRequests[0].AcceptUpdateRisks).toBe (false)
                Vitest.expect(synchronizeRequests[0].PublishLocalRevisions).toBe (true)
                Vitest.expect(finalState.Status.IsClean).toBe (true)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
                Vitest.expect(finalState.WarningNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A primary save exposes the cancellable synchronize phase",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value
                let mutable releaseSynchronize = None
                let mutable synchronizeRequest = None

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun request ->
                                synchronizeRequest <- Some request
                                Promise.create (fun resolve _reject -> releaseSynchronize <- Some resolve)
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "save") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    update deps ignore requestMessages[0] stateAfterRequest

                let pendingMessages = ResizeArray<Msg>()
                writeCmd |> List.iter (fun effect -> effect pendingMessages.Add)
                do! Promise.sleep 0

                let phaseState, _ = update deps ignore pendingMessages[0] stateAfterWrite

                Vitest
                    .expect(pendingMessages[0])
                    .toEqual (
                        WritePhaseChanged(
                            stateAfterWrite.ArcSessionId,
                            stateAfterWrite.WriteRequestId,
                            GitBusyOperation.PushingToRemote
                        )
                    )

                Vitest.expect(phaseState.BusyOperation).toEqual (Some GitBusyOperation.PushingToRemote)
                Vitest.expect(phaseState.CurrentOperation).toEqual (stateAfterWrite.CurrentOperation)
                Vitest.expect(synchronizeRequest.IsSome).toBe (true)

                let synchronizeKey = {
                    OperationId = synchronizeRequest.Value.OperationId
                }

                let startedState, _ =
                    update deps ignore (OperationStarted synchronizeKey) phaseState

                Vitest.expect(startedState.CurrentOperation).toEqual (Some synchronizeKey)

                releaseSynchronize.Value(Ok(succeededWithPublication PublicationStateDto.Published sync))
                do! Promise.sleep 0
                do! Promise.sleep 0

                Vitest
                    .expect(pendingMessages[1])
                    .toEqual (
                        WritePhaseChanged(
                            stateAfterWrite.ArcSessionId,
                            stateAfterWrite.WriteRequestId,
                            GitBusyOperation.Refreshing
                        )
                    )

                let mutable finalState = startedState

                for index in 1 .. pendingMessages.Count - 1 do
                    let nextState, _ = update deps ignore pendingMessages[index] finalState
                    finalState <- nextState

                Vitest.expect(finalState.BusyOperation).toEqual (None)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
                Vitest.expect(finalState.WarningNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A phase update from an older write is ignored",
            fun () -> promise {
                let state = {
                    runningState with
                        WriteRequestId = 4
                        BusyOperation = Some GitBusyOperation.CommittingAllChanges
                        BusyNotice = Some "Committing all changes"
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (WritePhaseChanged(state.ArcSessionId, 3, GitBusyOperation.PushingToRemote))
                        state

                let! messages = collectMessages cmd

                Vitest.expect(nextState).toEqual (state)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "Primary save synchronizes once when no upstream is configured yet",
            fun () -> promise {
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()

                let noUpstreamStatus = {
                    statusForBranch "feature/new-branch" with
                        Synchronization =
                            Some {
                                cleanStatus.Synchronization.Value with
                                    TargetRef = None
                                    TargetRevision = None
                                    Relationship = RevisionRelationshipDto.NoTarget
                            }
                }

                let sync = cleanStatus.Synchronization.Value

                let deps = {
                    defaultDependencies with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                promise { return Ok(succeededWithPublication PublicationStateDto.Published sync) }
                        getStatus = fun _ -> promise { return Ok(succeeded noUpstreamStatus) }
                        listRefs =
                            fun _ -> promise { return Ok(succeeded [| localBranch "feature/new-branch" true false |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Publish branch") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request to enqueue a write request."

                let! completionMessages = collectWriteMessages finishCmd

                let finalState, finalCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the primary save flow to publish the branch and finish."

                let! _ = collectMessages finalCmd

                Vitest.expect(synchronizeRequests.Count).toBe (1)
                Vitest.expect(synchronizeRequests[0].PublishLocalRevisions).toBe (true)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
                Vitest.expect(finalState.WarningNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A partial save offers materialization recovery and resumes the pending publish",
            fun () -> promise {
                let recovery =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Large-file hydration was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [||]

                let partialOutcome =
                    operationWithPublication PublicationStateDto.LocalOnly cleanStatus.Synchronization.Value

                let mutable materializeCalls = 0

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun _ -> promise {
                                return Ok(OperationResultDto.PartiallySucceeded(partialOutcome, recovery))
                            }
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            {
                                                Path = "large.bin"
                                                IsMaterialized = false
                                                IsLocallyAvailable = false
                                                SizeBytes = Some 100.
                                                ObjectId = Some "object-1"
                                            }
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun _ ->
                                materializeCalls <- materializeCalls + 1
                                promise { return Ok(succeeded ()) }
                }

                let saveState = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save") saveState

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages writeCmd

                let recoveryState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the partial save completion."

                let! _ = collectMessages completionCmd

                Vitest
                    .expect(recoveryState.WarningNotice)
                    .toEqual (Some "Changes were saved locally. Online sync is still pending.")

                Vitest.expect(recoveryState.PendingRecovery.IsSome).toBe (true)
                Vitest.expect(recoveryState.PendingPostMergePush).toBe (true)

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                let retryState, retryRequestCmd =
                    match confirmMessages with
                    | [| RetryMaterializationRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected materialization recovery to be confirmed."

                let! retryRequestMessages = collectMessages retryRequestCmd

                let retryRunningState, retryCmd =
                    match retryRequestMessages with
                    | [| WriteRequested RetryMaterialization |] -> update deps ignore retryRequestMessages[0] retryState
                    | _ -> failwith "Expected materialization retry to start."

                let! retryCompletionMessages = collectMessages retryCmd

                let finalState, finishCmd =
                    match retryCompletionMessages with
                    | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] ->
                        update deps ignore retryCompletionMessages[0] retryRunningState
                    | _ -> failwith "Expected materialization retry to complete."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(materializeCalls).toBe (1)
                Vitest.expect(finalState.PendingPostMergePush).toBe (false)

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | WriteRequested(Push GitUpdateAcceptance.RequirePreview) -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)
            }
        )

        Vitest.test (
            "A save whose download an index lock stopped resumes the pending publish after the lock removal and the download",
            fun () -> promise {
                let mutable materializeCalls = 0

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            operationWithPublication
                                                PublicationStateDto.LocalOnly
                                                cleanStatus.Synchronization.Value,
                                            lockedPartial VersionControlCodes.HydrationFailed
                                        )
                                    )
                            }
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            {
                                                Path = "large.bin"
                                                IsMaterialized = false
                                                IsLocallyAvailable = false
                                                SizeBytes = Some 100.
                                                ObjectId = Some "object-1"
                                            }
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun _ ->
                                materializeCalls <- materializeCalls + 1
                                promise { return Ok(succeeded ()) }
                }

                let saveState = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save") saveState

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages writeCmd

                let lockedState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the partial save completion."

                let! _ = collectMessages completionCmd

                Vitest.expect(lockedState.PendingRecovery).toEqual (Some(GitPendingRecovery.ClearStaleLock None))
                Vitest.expect(lockedState.PendingPostMergePush).toBe (true)

                let! downloadOfferState = confirmLockRemoval deps lockedState (Ok(succeeded cleanStatus))

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested downloadOfferState

                let! confirmMessages = collectMessages confirmCmd

                let retryState, retryRequestCmd =
                    match confirmMessages with
                    | [| RetryMaterializationRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected the download offer to be confirmed."

                let! retryRequestMessages = collectMessages retryRequestCmd

                let retryRunningState, retryCmd =
                    match retryRequestMessages with
                    | [| WriteRequested RetryMaterialization |] -> update deps ignore retryRequestMessages[0] retryState
                    | _ -> failwith "Expected the download to start."

                let! retryCompletionMessages = collectMessages retryCmd

                let finalState, finishCmd =
                    match retryCompletionMessages with
                    | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] ->
                        update deps ignore retryCompletionMessages[0] retryRunningState
                    | _ -> failwith "Expected the download to complete."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(materializeCalls).toBe (1)
                Vitest.expect(finalState.PendingPostMergePush).toBe (false)

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | WriteRequested(Push GitUpdateAcceptance.RequirePreview) -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)
            }
        )

        Vitest.test (
            "Dismissing materialization recovery resumes the pending publish",
            fun () -> promise {
                let recovery = GitPendingRecovery.RetryMaterialization "Retry materialization."

                let state = {
                    runningState with
                        PendingRecovery = Some recovery
                        PendingConfirmation =
                            Some {
                                Title = "Retry materialization"
                                Message = "Retry materialization."
                                ConfirmLabel = "Retry"
                                CancelLabel = "Dismiss"
                            }
                        PendingRemoteAction = GitPendingRemoteAction.Recover
                        PendingPostMergePush = true
                }

                let nextState, cmd =
                    update defaultDependencies ignore DismissRecoveryRequested state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.PendingRecovery).toEqual (None)
                Vitest.expect(nextState.PendingPostMergePush).toBe (false)

                Vitest
                    .expect(messages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "Local-only save never requests synchronization",
            fun () -> promise {
                let mutable synchronizeCalled = false

                let localOnly = {
                    sessionInfo with
                        Services = {
                            sessionInfo.Services with
                                Synchronization = false
                        }
                }

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        getSessionInfo = fun _ -> promise { return Ok(succeeded localOnly) }
                        synchronize =
                            fun _ ->
                                synchronizeCalled <- true
                                unexpectedPromise "unexpected synchronize"
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (CommitAllRequested "Local only") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(CommitAll _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the local commit request."

                let! completionMessages = collectMessages finishCmd

                let finalState, finalCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, CommitAll _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the local commit to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(synchronizeCalled).toBe (false)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "Primary save keeps a warning and pending confirmation when synchronize needs merge resolution",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value

                let failure = {
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldCreateConflictSession
                        "Updating from the target needs conflict resolution."
                        (Some {
                            Code = VersionControlCodes.Recovery.AcceptUpdateRisks
                            Instructions = None
                        })
                        [|
                            "isa.study.xlsx"
                            "a1.txt"
                            "a2.txt"
                            "a3.txt"
                            "a4.txt"
                            "a5.txt"
                            "a6.txt"
                        |] with
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-1"
                            }
                        |]
                }

                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                if synchronizeRequests.Count = 1 then
                                    promise { return Ok(OperationResultDto.Failed failure) }
                                else
                                    promise { return Ok(succeeded sync) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Add polish") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages finishCmd

                let nextState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        PrimarySave _,
                                        Ok(CompletedWithPendingRemoteConfirmation(_, _, pendingAction))) |] ->
                        Vitest
                            .expect(pendingAction)
                            .toEqual (
                                GitPendingRemoteAction.PublishAfterUpdate(
                                    GitUpdateAcceptance.Accepted(
                                        "target-1",
                                        synchronizeRequests[0].ExpectedWorkspaceVersion
                                    )
                                )
                            )

                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the primary save flow to request remote confirmation."

                let! _ = collectMessages completionCmd

                let stateAfterCancel, cancelCmd =
                    update deps ignore CancelPendingRemoteActionRequested nextState

                let! cancelMessages = collectMessages cancelCmd

                Vitest.expect(nextState.PendingConfirmation.IsSome).toBe (true)

                Vitest
                    .expect(nextState.PendingRemoteAction)
                    .toEqual (
                        GitPendingRemoteAction.PublishAfterUpdate(
                            GitUpdateAcceptance.Accepted("target-1", synchronizeRequests[0].ExpectedWorkspaceVersion)
                        )
                    )

                Vitest.expect(nextState.PendingConfirmation.Value.Title).toBe ("Merge resolution required")
                Vitest.expect(nextState.PendingConfirmation.Value.Message).toContain ("isa.study.xlsx")
                Vitest.expect(nextState.PendingConfirmation.Value.Message).toContain ("a4.txt and 2 more.")
                Vitest.expect(nextState.PendingConfirmation.Value.Message).not.toContain ("a5.txt")
                Vitest.expect(nextState.WarningNotice |> Option.defaultValue "").toContain ("saved locally")
                Vitest.expect(cancelMessages).toEqual ([||])
                Vitest.expect(stateAfterCancel.PendingConfirmation).toEqual (None)
                Vitest.expect(stateAfterCancel.WarningNotice |> Option.defaultValue "").toContain ("saved locally")

                let confirming, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested nextState

                let! confirmMessages = collectMessages confirmCmd

                let duringWrite, secondWriteCmd =
                    match confirmMessages with
                    | [| WriteRequested(Push(GitUpdateAcceptance.Accepted(target, version))) |] ->
                        Vitest.expect(target).toBe ("target-1")
                        Vitest.expect(version).toBe (synchronizeRequests[0].ExpectedWorkspaceVersion)
                        update deps ignore confirmMessages[0] confirming
                    | _ -> failwith "Expected the accepted publish request."

                Vitest.expect(duringWrite.PendingPostMergePush).toBe (true)
                let! _ = collectMessages secondWriteCmd
                Vitest.expect(synchronizeRequests.Count).toBe (2)
                Vitest.expect(synchronizeRequests[1].AcceptUpdateRisks).toBe (true)
                Vitest.expect(synchronizeRequests[1].ExpectedTargetRevision).toEqual (Some "target-1")

                Vitest
                    .expect(synchronizeRequests[1].ExpectedWorkspaceVersion)
                    .toBe (synchronizeRequests[0].ExpectedWorkspaceVersion)

                Vitest.expect(synchronizeRequests[1].PublishLocalRevisions).toBe (true)
            }
        )

        Vitest.test (
            "The save's acceptance dialog is built over the refreshed snapshot",
            fun () -> promise {
                let failure = {
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldCreateConflictSession
                        "Updating from the target needs conflict resolution."
                        (Some {
                            Code = VersionControlCodes.Recovery.AcceptUpdateRisks
                            Instructions = None
                        })
                        [| "isa.study.xlsx" |] with
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-1"
                            }
                        |]
                }

                let runCase (confirmationStatus: Result<OperationResultDto<WorkspaceStatusDto>, string>) = promise {
                    let mutable getStatusCalls = 0
                    let postCommitStatus = statusForBranch "post-commit"

                    let deps = {
                        defaultDependencies with
                            createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                            synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                            getStatus =
                                fun _ ->
                                    getStatusCalls <- getStatusCalls + 1

                                    promise {
                                        if getStatusCalls = 1 then
                                            return Ok(succeeded postCommitStatus)
                                        else
                                            return confirmationStatus
                                    }
                            listRefs = fun _ -> promise { return Ok(succeeded refs) }
                            getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                    }

                    let state = {
                        runningState with
                            ChangedFiles = [| changedFile "README.md" "M" " " false |]
                    }

                    let stateAfterRequest, requestCmd =
                        update deps ignore (PrimarySaveAllRequested "Add polish") state

                    let! requestMessages = collectMessages requestCmd

                    let stateAfterWrite, finishCmd =
                        match requestMessages with
                        | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                        | _ -> failwith "Expected the primary save request."

                    let! completionMessages = collectWriteMessages finishCmd

                    let finalState, completionCmd =
                        match completionMessages with
                        | [| WriteCompleted(_, _, PrimarySave _, Ok(CompletedWithPendingRemoteConfirmation(_, _, _))) |] ->
                            update deps ignore completionMessages[0] stateAfterWrite
                        | _ -> failwith "Expected the primary save flow to request remote confirmation."

                    let! _ = collectMessages completionCmd

                    Vitest.expect(getStatusCalls).toBe (2)
                    return finalState
                }

                let! refreshedState = runCase (Ok(succeeded (statusForBranch "refreshed")))

                Vitest.expect(refreshedState.Status.CurrentBranch).toEqual (Some "refreshed")

                let! failedRefreshState = runCase (Error "confirmation refresh failed")

                Vitest.expect(failedRefreshState.Status.CurrentBranch).toEqual (Some "post-commit")
                Vitest.expect(failedRefreshState.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(failedRefreshState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A recovery attached to a pending confirmation becomes a warning and is not parked",
            fun () -> promise {
                let partial =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Large-file hydration was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [||]

                let prepared = {
                    BusyOperation = GitBusyOperation.CommittingAllChanges
                    NormalizedMessage = "Save"
                    PathsToCommit = [| "README.md" |]
                }

                let dialog = {
                    Title = "Merge confirmation"
                    Message = "Confirm the merge."
                    ConfirmLabel = "Confirm"
                    CancelLabel = "Cancel"
                }

                let state = {
                    runningState with
                        BusyOperation = Some prepared.BusyOperation
                        WriteRequestId = 1
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (WriteCompleted(
                            state.ArcSessionId,
                            state.WriteRequestId,
                            PrimarySave prepared,
                            Ok(
                                CompletedWithPendingRemoteConfirmation(
                                    UnitSuccess {
                                        Refresh = refreshed cleanStatus
                                        PageChange = GitPageChange.NoChange
                                        SelectedChangePath = None
                                        Warning = None
                                        Partial = Some partial
                                        Published = None
                                        ObjectsMissing = None
                                    },
                                    dialog,
                                    GitPendingRemoteAction.FinalizeMerge
                                )
                            )
                        ))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.PendingRecovery).toEqual (None)
                Vitest.expect(nextState.PendingConfirmation).toEqual (Some dialog)
                Vitest.expect(nextState.WarningNotice).toEqual (Some(failureMessage partial))
            }
        )

        Vitest.test (
            "A primary save reconciliation failure reports a saved commit that needs attention",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()
                let mutable synchronizeCalls = 0

                let failure =
                    makeFailure
                        ProviderError
                        "index_reconciliation_failed"
                        "The index needs reconciliation."
                        (Some {
                            Code = VersionControlCodes.Recovery.ReconcileIndex
                            Instructions = None
                        })
                        [||]

                let expectedMessage =
                    $"{failure.Message} The changes are saved in a commit, but Git's file index is out of date. Close other Git programs for this ARC, then run \"git reset\" in the ARC folder."

                let partialOutcome =
                    operationWithPublication PublicationStateDto.NotApplicable "revision-1"

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision =
                            fun _ -> promise {
                                return Ok(OperationResultDto.PartiallySucceeded(partialOutcome, failure))
                            }
                        synchronize =
                            fun _ ->
                                synchronizeCalls <- synchronizeCalls + 1

                                promise {
                                    return
                                        Ok(
                                            succeededWithPublication
                                                PublicationStateDto.Published
                                                cleanStatus.Synchronization.Value
                                        )
                                }
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save locally first") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages writeCmd

                let finalState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(CompletedWithPendingRemoteFailure(_, message))) |] ->
                        Vitest.expect(message).toBe expectedMessage
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the saved commit to report index reconciliation."

                let! _ = collectMessages completionCmd

                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(reportedErrors[0].Title).toBe ("Changes saved, but Git's index needs attention")
                Vitest.expect(reportedErrors[0].Message).toBe (expectedMessage)
                Vitest.expect(finalState.WarningNotice).toEqual (None)
                Vitest.expect(synchronizeCalls).toBe (0)
            }
        )

        Vitest.test (
            "A RestorePaths failure with a locked index offers stale-lock recovery",
            fun () -> promise {
                let failure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.IndexLocked
                        "Git's index is locked."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = None
                        })
                        [||]

                let deps = {
                    defaultDependencies with
                        restorePaths = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                }

                let stateAfterRequest, requestCmd =
                    requestAndConfirmDiscard deps runningState [| "a.txt" |]

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(DiscardSelection _) |] ->
                        update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the discard request."

                let! completionMessages = collectMessages writeCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        DiscardSelection _,
                                        Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock _, _))) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected stale-lock recovery after RestorePaths failed."

                let! _ = collectMessages recoveryCmd

                Vitest.expect(recoveryState.PendingRecovery).toEqual (Some(GitPendingRecovery.ClearStaleLock None))

                Vitest
                    .expect(recoveryState.PendingConfirmation |> Option.map _.Message)
                    .toEqual (
                        Some
                            "Git's index is locked. Another program may be using the repository, or an interrupted operation left the lock behind. Close other Git programs for this ARC, then remove the lock."
                    )
            }
        )

        Vitest.test (
            "A CreateRevision failure with a locked index offers stale-lock recovery",
            fun () -> promise {
                let failure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.IndexLocked
                        "Git's index is locked."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = None
                        })
                        [||]

                let mutable createRevisionCalls = 0

                let deps = {
                    defaultDependencies with
                        createRevision =
                            fun _ ->
                                createRevisionCalls <- createRevisionCalls + 1
                                promise { return Ok(OperationResultDto.Failed failure) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (CommitAllRequested "Save") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(CommitAll _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the commit request."

                let! completionMessages = collectMessages writeCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, CommitAll _, Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock _, _))) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected stale-lock recovery after CreateRevision failed."

                let! _ = collectMessages recoveryCmd

                Vitest.expect(createRevisionCalls).toBe (1)
                Vitest.expect(recoveryState.PendingRecovery).toEqual (Some(GitPendingRecovery.ClearStaleLock None))
            }
        )

        Vitest.test (
            "Primary save preserves the local commit warning when synchronize fails after commit",
            fun () -> promise {
                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize = fun _ -> promise { return Error "Network unavailable during synchronize." }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save locally first") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages finishCmd

                let nextState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(CompletedWithPendingRemoteFailure(_, message))) |] ->
                        Vitest.expect(message).toBe ("Network unavailable during synchronize.")
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected a saved-locally outcome after preflight failed."

                let! finishMessages = collectMessages completionCmd

                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(nextState.WarningNotice |> Option.defaultValue "").toContain ("saved locally")
                Vitest.expect(nextState.ErrorNotice).toEqual (Some "Network unavailable during synchronize.")
                Vitest.expect(nextState.Status.IsClean).toBe (true)
            }
        )

        Vitest.test (
            "A primary save whose synchronize is canceled with a refresh recovery keeps the saved-locally notice",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Publish was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RefreshWorkspace
                            Instructions = None
                        })
                        [||]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    promise { return Ok(OperationResultDto.Failed canceled) }
                                else
                                    unexpectedPromise "unexpected pull"
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save locally first") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages finishCmd

                let nextState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed(UnitSuccess success))) |] when
                        success.Warning.IsSome && success.Partial.IsNone
                        ->
                        let warning = success.Warning.Value
                        Vitest.expect(warning).toBe ("Changes were saved locally. Online sync is still pending.")
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected a completed saved-locally outcome after publish was canceled."

                let! finishMessages = collectMessages completionCmd

                Vitest.expect(finishMessages).toEqual ([||])

                Vitest
                    .expect(nextState.WarningNotice)
                    .toEqual (Some "Changes were saved locally. Online sync is still pending.")

                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(reportedErrors.Count).toBe (0)
            }
        )

        Vitest.test (
            "A canceled primary save push shows the saved-locally warning without an error",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let canceled =
                    makeFailure Canceled VersionControlCodes.OperationCanceled "Publish was canceled." None [||]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun request ->
                                if request.PublishLocalRevisions then
                                    promise { return Ok(OperationResultDto.Failed canceled) }
                                else
                                    unexpectedPromise "unexpected pull"
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save locally first") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages finishCmd

                let nextState, completionCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed(UnitSuccess success))) |] when
                        success.Warning.IsSome && success.Partial.IsNone
                        ->
                        Vitest
                            .expect(success.Warning.Value)
                            .toBe ("Changes were saved locally. Online sync is still pending.")

                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected a completed saved-locally outcome after publish was canceled."

                let! finishMessages = collectMessages completionCmd

                Vitest.expect(finishMessages).toEqual ([||])

                Vitest
                    .expect(nextState.WarningNotice)
                    .toEqual (Some "Changes were saved locally. Online sync is still pending.")

                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(reportedErrors.Count).toBe (0)
            }
        )

        Vitest.test (
            "UpdateFromOnlineRequested opens a confirmation dialog when synchronize predicts merge resolution",
            fun () -> promise {
                let failure = {
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldCreateConflictSession
                        "Updating from the target needs conflict resolution."
                        (Some {
                            Code = VersionControlCodes.Recovery.AcceptUpdateRisks
                            Instructions = None
                        })
                        [| "isa.study.xlsx" |] with
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-1"
                            }
                        |]
                }

                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                if synchronizeRequests.Count = 1 then
                                    promise { return Ok(OperationResultDto.Failed failure) }
                                else
                                    promise { return Ok(succeeded cleanStatus.Synchronization.Value) }
                }

                let state = {
                    runningState with
                        BusyOperation = None
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore UpdateFromOnlineRequested state

                let! writeMessages = collectMessages requestCmd

                let writeState, writeCmd =
                    match writeMessages with
                    | [| WriteRequested(Pull GitUpdateAcceptance.RequirePreview) |] ->
                        update deps ignore writeMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the update write request."

                let! completionMessages = collectMessages writeCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(CompletedWithPendingRemoteConfirmation(_, dialog, action))) |] ->
                        Vitest.expect(dialog.Title).toBe ("Merge resolution required")

                        Vitest
                            .expect(action)
                            .toEqual (
                                GitPendingRemoteAction.UpdateFromOnline(GitUpdateAcceptance.Accepted("target-1", "v1"))
                            )

                        update deps ignore completionMessages[0] writeState
                    | _ -> failwith "Expected update confirmation to complete."

                let! messages = collectMessages finishCmd

                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(nextState.PendingConfirmation.Value.Title).toBe ("Merge resolution required")
                Vitest.expect(nextState.PendingConfirmation.Value.Message).toContain ("isa.study.xlsx")

                Vitest
                    .expect(nextState.PendingRemoteAction)
                    .toEqual (GitPendingRemoteAction.UpdateFromOnline(GitUpdateAcceptance.Accepted("target-1", "v1")))

                let confirmed, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested nextState

                let! confirmMessages = collectMessages confirmCmd

                let _, secondWriteCmd =
                    match confirmMessages with
                    | [| WriteRequested(Pull(GitUpdateAcceptance.Accepted(target, version))) |] ->
                        Vitest.expect(target).toBe ("target-1")
                        Vitest.expect(version).toBe ("v1")
                        update deps ignore confirmMessages[0] confirmed
                    | _ -> failwith "Expected the accepted pull request."

                let! _ = collectMessages secondWriteCmd
                Vitest.expect(synchronizeRequests.Count).toBe (2)
                Vitest.expect(synchronizeRequests[1].AcceptUpdateRisks).toBe (true)
                Vitest.expect(synchronizeRequests[1].ExpectedTargetRevision).toEqual (Some "target-1")
                Vitest.expect(synchronizeRequests[1].ExpectedWorkspaceVersion).toBe ("v1")
                Vitest.expect(synchronizeRequests[1].PublishLocalRevisions).toBe (false)
            }
        )

        Vitest.test (
            "A decision failure without an observed target is reported as an error",
            fun () -> promise {
                let failure =
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldCreateConflictSession
                        "Updating from the target would overwrite local changes."
                        (Some {
                            Code = VersionControlCodes.Recovery.AcceptUpdateRisks
                            Instructions = None
                        })
                        [| "isa.study.xlsx" |]

                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                        reportError = reportedErrors.Add
                }

                let requested, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Error message) |] ->
                        Vitest.expect(message).toBe (failureMessage failure)
                        update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected the decision failure to be reported as an error."

                let! _ = collectMessages finishCmd
                Vitest.expect(finalState.ErrorNotice).toEqual (Some(failureMessage failure))
                Vitest.expect(finalState.PendingConfirmation).toEqual (None)
                Vitest.expect(finalState.PendingRemoteAction).toEqual (GitPendingRemoteAction.None)
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "UpdateFromOnlineRequested does nothing when no ARC is loaded",
            fun () -> promise {
                let nextState, cmd =
                    update defaultDependencies ignore UpdateFromOnlineRequested GitState.Empty

                let! messages = collectMessages cmd

                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(nextState).toEqual (GitState.Empty)
            }
        )

        Vitest.test (
            "A synchronize that cannot preview the update is reported as an error",
            fun () -> promise {
                let failure = {
                    makeFailure
                        ProviderError
                        VersionControlCodes.PreviewIndeterminate
                        "Git pull synchronization could not be classified safely."
                        None
                        [||] with
                        Retryable = true
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-1"
                            }
                        |]
                }

                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        BusyOperation = None
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore UpdateFromOnlineRequested state

                let! writeMessages = collectMessages requestCmd

                let writeState, writeCmd =
                    match writeMessages with
                    | [| WriteRequested(Pull GitUpdateAcceptance.RequirePreview) |] ->
                        update deps ignore writeMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the update write request."

                let! completionMessages = collectMessages writeCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Error message) |] ->
                        Vitest.expect(message).toBe (failureMessage failure)
                        update deps ignore completionMessages[0] writeState
                    | _ -> failwith "Expected the indeterminate update to be reported as an error."

                let! messages = collectMessages finishCmd

                Vitest.expect(messages).toEqual ([||])
                Vitest.expect(nextState.ErrorNotice).toEqual (Some(failureMessage failure))
                Vitest.expect(nextState.PendingConfirmation).toEqual (None)
                Vitest.expect(nextState.PendingRemoteAction).toEqual (GitPendingRemoteAction.None)
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "An update over local changes is reported with the paths and without a dialog",
            fun () -> promise {
                let failure = {
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldOverwriteLocalChanges
                        "The library message is replaced by the renderer."
                        (Some {
                            Code = VersionControlCodes.Recovery.ResolveLocalChanges
                            Instructions = None
                        })
                        [| "assays/a/isa.assay.xlsx"; "runs/r/out.csv" |] with
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-1"
                            }
                        |]
                }

                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()

                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request
                                promise { return Ok(OperationResultDto.Failed failure) }
                }

                let requested, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Error _) |] -> update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected the local-change overwrite to be reported as an error."

                let! _ = collectMessages finishCmd
                let errorNotice = finalState.ErrorNotice |> Option.defaultValue ""

                Vitest.expect(errorNotice).toContain ("assays/a/isa.assay.xlsx")
                Vitest.expect(errorNotice).toContain ("runs/r/out.csv")
                Vitest.expect(errorNotice).toContain ("Save or discard")
                Vitest.expect(finalState.PendingConfirmation).toEqual (None)
                Vitest.expect(finalState.PendingRemoteAction).toEqual (GitPendingRemoteAction.None)
                Vitest.expect(synchronizeRequests.Count).toBe (1)
            }
        )

        Vitest.test (
            "ConfirmMergeResolutionCompleted dispatches Push when the pending primary-save push can resume",
            fun () -> promise {
                let state = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        ArcSessionId = 5
                        PendingPostMergePush = true
                        ActiveConflict = None
                        BusyOperation = Some(GitBusyOperation.ConfirmingMergeResolution "conflict.txt")
                        MergeResolutionPendingPath = Some "conflict.txt"
                        SelectedChangePath = Some "conflict.txt"
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (ConfirmMergeResolutionCompleted(
                            5,
                            Ok {
                                UpdatedStatus = cleanStatus
                                NextConflictedPath = None
                                PageChange = GitPageChange.Clear
                                Notice = None
                                PendingMaterializationPath = None
                                ResolutionSessionId = "session-1"
                                Finalized = true
                            }
                        ))
                        state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.PendingPostMergePush).toBe (false)

                Vitest
                    .expect(messages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "ArcPathChanged schedules a bare RefreshRequested when switching repositories",
            fun () -> promise {
                let nextState, cmd =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/arc-b")) GitState.Empty

                let! messages = collectMessages cmd

                Vitest.expect(nextState.CurrentArcPath).toEqual (Some "C:/arc-b")
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "Primary save stays local when its publish target is missing without a DataHUB account",
            fun () -> promise {
                let mutable createProjectCalls = 0
                let notice = "Saved locally. Sign in to a DataHUB account to publish this ARC."

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        failed
                                            Validation
                                            VersionControlCodes.PublishTargetMissing
                                            "publish target missing"
                                    )
                            }
                        hasUsableAccount = fun () -> false
                        createRemoteProject =
                            fun _ ->
                                createProjectCalls <- createProjectCalls + 1
                                unexpectedGitLab "createRemoteProject should not run without a usable account"
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let requested, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] requested
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages writeCmd

                let finalState, finalCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed(UnitSuccess success))) |] ->
                        Vitest.expect(success.Warning).toEqual (Some notice)
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the local save to finish with a publication notice."

                let! _ = collectMessages finalCmd

                Vitest.expect(createProjectCalls).toBe (0)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
                Vitest.expect(finalState.WarningNotice).toEqual (Some notice)
            }
        )

        Vitest.test (
            "Primary save provisions the remote project when the workspace has no target",
            fun () -> promise {
                let calls = ResizeArray<string>()
                let mutable boundLocation = None

                let noTargetStatus = {
                    cleanStatus with
                        Synchronization =
                            Some {
                                cleanStatus.Synchronization.Value with
                                    TargetRef = None
                                    TargetRevision = None
                                    Relationship = RevisionRelationshipDto.NoTarget
                            }
                }

                let sync = cleanStatus.Synchronization.Value
                let mutable synchronizeCalls = 0

                let deps = {
                    defaultDependencies with
                        createRevision =
                            fun _ ->
                                calls.Add "createRevision"
                                promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun _ ->
                                calls.Add "synchronize"
                                synchronizeCalls <- synchronizeCalls + 1

                                if synchronizeCalls = 1 then
                                    promise {
                                        return
                                            Ok(
                                                failed
                                                    Validation
                                                    VersionControlCodes.PublishTargetMissing
                                                    "publish target missing"
                                            )
                                    }
                                else
                                    promise { return Ok(succeededWithPublication PublicationStateDto.Published sync) }
                        createRemoteProject =
                            fun _ ->
                                calls.Add "createRemoteProject"
                                promise { return Ok remoteProject }
                        bindWorkspace =
                            fun request ->
                                calls.Add "bindWorkspace"
                                boundLocation <- Some request.ProviderLocation
                                promise { return Ok(succeeded sessionInfo) }
                        getStatus =
                            fun _ ->
                                calls.Add "getStatus"
                                promise { return Ok(succeeded noTargetStatus) }
                        listRefs =
                            fun _ ->
                                calls.Add "listRefs"
                                promise { return Ok(succeeded refs) }
                        getStoragePolicySettings =
                            fun _ ->
                                calls.Add "getStoragePolicySettings"
                                promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Publish") state

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, finishCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages finishCmd

                let finalState, finalCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected remote provisioning to complete the primary save."

                let! _ = collectMessages finalCmd

                Vitest
                    .expect(calls |> Seq.take 9 |> Seq.toArray)
                    .toEqual (
                        [|
                            "createRevision"
                            "getStatus"
                            "listRefs"
                            "getStoragePolicySettings"
                            "synchronize"
                            "createRemoteProject"
                            "bindWorkspace"
                            "getStatus"
                            "synchronize"
                        |]
                    )

                Vitest.expect(boundLocation).toEqual (Some "https://gitlab.example/carol/my-arc.git")
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
                Vitest.expect(finalState.WarningNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A provisioned bound remote threads acceptance through the second synchronize",
            fun () -> promise {
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()

                let decisionFailure = {
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldCreateConflictSession
                        "The update needs merge resolution."
                        (Some {
                            Code = VersionControlCodes.Recovery.AcceptUpdateRisks
                            Instructions = None
                        })
                        [| "conflict.txt" |] with
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-1"
                            }
                        |]
                }

                let decidingStatus = {
                    cleanStatus with
                        WorkspaceVersion = "v2"
                }

                let deps = {
                    (withRefresh decidingStatus defaultDependencies) with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                if synchronizeRequests.Count = 1 then
                                    promise { return Ok(OperationResultDto.Failed decisionFailure) }
                                else
                                    promise {
                                        return
                                            Ok(
                                                succeededWithPublication
                                                    PublicationStateDto.Published
                                                    cleanStatus.Synchronization.Value
                                            )
                                    }
                }

                let state = {
                    runningState with
                        ProvisionedRemote =
                            Some {
                                RemoteUrl = "https://gitlab.example/carol/my-arc.git"
                                ProjectName = "my-arc"
                                IsBound = true
                            }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) state

                let! completionMessages = collectMessages writeCmd

                let pendingState, pendingCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(CompletedWithPendingRemoteConfirmation(_, _, action))) |] ->
                        Vitest
                            .expect(action)
                            .toEqual (
                                GitPendingRemoteAction.PublishAfterUpdate(
                                    GitUpdateAcceptance.Accepted("target-1", "v2")
                                )
                            )

                        update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected the bound remote to request acceptance."

                let! _ = collectMessages pendingCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested pendingState

                let! confirmMessages = collectMessages confirmCmd

                let secondWriteState, secondWriteCmd =
                    match confirmMessages with
                    | [| WriteRequested(Push(GitUpdateAcceptance.Accepted(target, workspaceVersion))) |] ->
                        Vitest.expect(target).toBe ("target-1")
                        Vitest.expect(workspaceVersion).toBe ("v2")
                        update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected the accepted publish request."

                let! secondCompletionMessages = collectMessages secondWriteCmd

                let _, finishCmd =
                    match secondCompletionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore secondCompletionMessages[0] secondWriteState
                    | _ -> failwith "Expected the accepted publish to complete."

                let! _ = collectMessages finishCmd
                Vitest.expect(synchronizeRequests.Count).toBe (2)
                Vitest.expect(synchronizeRequests[1].AcceptUpdateRisks).toBe (true)
                Vitest.expect(synchronizeRequests[1].ExpectedTargetRevision).toEqual (Some "target-1")
            }
        )

        Vitest.test (
            "Acceptance after provisioning uses the token from the deciding synchronize",
            fun () -> promise {
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()

                let missingTarget =
                    makeFailure Validation VersionControlCodes.PublishTargetMissing "publish target missing" None [||]

                let decisionFailure = {
                    makeFailure
                        Conflict
                        VersionControlCodes.UpdateWouldCreateConflictSession
                        "The update needs merge resolution."
                        (Some {
                            Code = VersionControlCodes.Recovery.AcceptUpdateRisks
                            Instructions = None
                        })
                        [| "conflict.txt" |] with
                        RevisionEvidence = [|
                            {
                                Label = "observed_target"
                                Revision = "target-2"
                            }
                        |]
                }

                let decidingStatus = {
                    cleanStatus with
                        WorkspaceVersion = "v2"
                }

                let deps = {
                    (withRefresh decidingStatus defaultDependencies) with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                if synchronizeRequests.Count = 1 then
                                    promise { return Ok(OperationResultDto.Failed missingTarget) }
                                else
                                    promise { return Ok(OperationResultDto.Failed decisionFailure) }
                        createRemoteProject = fun _ -> promise { return Ok remoteProject }
                        bindWorkspace = fun _ -> promise { return Ok(succeeded sessionInfo) }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages writeCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(CompletedWithPendingRemoteConfirmation(_, _, action))) |] ->
                        let state, command = update deps ignore completionMessages[0] requested

                        Vitest
                            .expect(action)
                            .toEqual (
                                GitPendingRemoteAction.PublishAfterUpdate(
                                    GitUpdateAcceptance.Accepted("target-2", "v2")
                                )
                            )

                        state, command
                    | _ -> failwith "Expected acceptance after provisioning."

                let! _ = collectMessages finishCmd
                Vitest.expect(nextState.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(synchronizeRequests.Count).toBe (2)
            }
        )

        Vitest.test (
            "A push whose synchronize reports a missing target after an applied update provisions the project",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()
                let mutable createProjectCalls = 0
                let mutable bindCalls = 0
                let mutable statusCalls = 0

                let partialOutcome = {
                    operation sync with
                        Publication = PublicationStateDto.LocalOnly
                }

                let missingTarget = {
                    makeFailure
                        Validation
                        VersionControlCodes.PublishTargetMissing
                        "publish target missing after the update"
                        None
                        [||] with
                        StateChanged = true
                }

                let deps = {
                    defaultDependencies with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                if synchronizeRequests.Count = 1 then
                                    promise {
                                        return Ok(OperationResultDto.PartiallySucceeded(partialOutcome, missingTarget))
                                    }
                                else
                                    promise { return Ok(succeeded sync) }
                        createRemoteProject =
                            fun _ ->
                                createProjectCalls <- createProjectCalls + 1
                                promise { return Ok remoteProject }
                        bindWorkspace =
                            fun _ ->
                                bindCalls <- bindCalls + 1
                                promise { return Ok(succeeded sessionInfo) }
                        getStatus =
                            fun _ ->
                                statusCalls <- statusCalls + 1
                                promise { return Ok(succeeded cleanStatus) }
                        listRefs = fun _ -> promise { return Ok(succeeded refs) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages writeCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected provisioning after the applied update."

                let! _ = collectMessages finishCmd

                Vitest.expect(createProjectCalls).toBe (1)
                Vitest.expect(bindCalls).toBe (1)
                Vitest.expect(statusCalls).toBeGreaterThan (0)
                Vitest.expect(synchronizeRequests.Count).toBe (2)
                Vitest.expect(synchronizeRequests[0].PublishLocalRevisions).toBe (true)
                Vitest.expect(synchronizeRequests[1].PublishLocalRevisions).toBe (true)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A canceled update with restore_workspace offers to restore exactly the affected paths",
            fun () -> promise {
                let affectedPaths = [| "a.txt"; "b.txt" |]

                let canceled = {
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Update was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RestoreWorkspace
                            Instructions = Some "Restore the interrupted files."
                        })
                        affectedPaths with
                        StateChanged = true
                }

                let mutable restoredPaths = None

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                        restorePaths =
                            fun request ->
                                restoredPaths <- Some request.Paths
                                promise { return Ok(succeeded ()) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        Pull _,
                                        Ok(RequiresRecovery(GitPendingRecovery.RestoreInterruptedPaths _, _))) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected restore recovery after cancellation."

                let! _ = collectMessages recoveryCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                let restoreState, restoreCmd =
                    match confirmMessages with
                    | [| RestoreInterruptedPathsRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected restore confirmation to dispatch the restore request."

                let! restoreMessages = collectMessages restoreCmd

                let _, restoreExecutionCmd =
                    match restoreMessages with
                    | [| WriteRequested(RestoreInterruptedPaths _) |] ->
                        update deps ignore restoreMessages[0] restoreState
                    | _ -> failwith "Expected the restore write request."

                let! _ = collectMessages restoreExecutionCmd

                Vitest.expect(recoveryState.PendingRecovery.IsSome).toBe (true)
                Vitest.expect(recoveryState.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(restoredPaths).toEqual (Some affectedPaths)
            }
        )

        Vitest.test (
            "Clearing a stale lock reports the removed lock files",
            fun () -> promise {
                let lockA = "C:/arc/.git/index.lock"
                let lockB = "C:/arc/.git/HEAD.lock"
                let unrelated = "This warning must stay hidden."

                let warnings = [|
                    {
                        Code = VersionControlCodes.LockRemoved
                        Message = lockA
                    }
                    {
                        Code = VersionControlCodes.LockRemoved
                        Message = lockB
                    }
                    {
                        Code = "other_warning"
                        Message = unrelated
                    }
                |]

                let refreshDependencies = withRefresh cleanStatus defaultDependencies

                let runClear result = promise {
                    let deps = {
                        refreshDependencies with
                            listObjects = noObjects
                            clearStaleLock = fun _ -> promise { return result }
                    }

                    let requested, requestCmd =
                        update deps ignore (WriteRequested ClearStaleLock) runningState

                    let! completion = collectMessages requestCmd

                    match completion with
                    | [| WriteCompleted(_, _, ClearStaleLock, Ok(Completed _)) |] ->
                        let finalState, finishCmd = update deps ignore completion[0] requested
                        let! _ = collectMessages finishCmd
                        return finalState
                    | _ -> return failwith "Expected stale-lock clearing to complete."
                }

                let! pluralState = runClear (Ok(succeededWithWarnings warnings cleanStatus))
                let pluralNotice = pluralState.WarningNotice |> Option.defaultValue ""

                Vitest.expect(pluralNotice).toBe ($"Removed stale lock files: {lockA}, {lockB}.")
                Vitest.expect(pluralNotice).not.toContain (unrelated)
                Vitest.expect(pluralNotice).toContain (lockA)
                Vitest.expect(pluralNotice).toContain (lockB)

                let! singularState =
                    runClear (
                        Ok(
                            succeededWithWarnings
                                [|
                                    {
                                        Code = VersionControlCodes.LockRemoved
                                        Message = lockA
                                    }
                                |]
                                cleanStatus
                        )
                    )

                Vitest.expect(singularState.WarningNotice).toEqual (Some $"Removed stale lock file: {lockA}.")

                let! emptyState = runClear (Ok(succeeded cleanStatus))
                Vitest.expect(emptyState.WarningNotice).toEqual (None)

                let partialFailure =
                    makeFailure ProviderError "partial_lock_cleanup" "The remaining lock cleanup needs attention." None [||]

                let! partialState =
                    runClear (
                        Ok(
                            OperationResultDto.PartiallySucceeded(
                                operationWithPublication PublicationStateDto.NotApplicable cleanStatus
                                |> fun outcome -> {
                                    outcome with
                                        Warnings = [|
                                            {
                                                Code = VersionControlCodes.LockRemoved
                                                Message = lockA
                                            }
                                        |]
                                }
                                , partialFailure
                            )
                        )
                    )

                Vitest
                    .expect(partialState.WarningNotice)
                    .toEqual (Some $"Removed stale lock file: {lockA}. {partialFailure.Message}")
            }
        )

        Vitest.test (
            "A canceled update with remove_index_lock offers to clear the lock, and a cleared lock with an open merge abandons it",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Update was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = None
                        })
                        [||]

                let conflicted = conflictedStatus [| "conflict.txt" |]

                let deps = {
                    (withRefresh conflicted defaultDependencies) with
                        listObjects = noObjects
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded conflicted) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock _, _))) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected stale-lock recovery after cancellation."

                let! _ = collectMessages recoveryCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                let requestedState, requestedCmd =
                    match confirmMessages with
                    | [| ClearStaleLockRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected lock clearing to be dispatched."

                let! requestedMessages = collectMessages requestedCmd

                let clearState, clearCmd =
                    match requestedMessages with
                    | [| WriteRequested ClearStaleLock |] -> update deps ignore requestedMessages[0] requestedState
                    | _ -> failwith "Expected the stale-lock write to be requested."

                let! clearMessages = collectMessages clearCmd

                let finalState, finalCmd =
                    match clearMessages with
                    | [| WriteCompleted(_, _, ClearStaleLock, Ok(Completed _)) |] ->
                        update deps ignore clearMessages[0] clearState
                    | _ -> failwith "Expected stale-lock clearing to complete."

                let! finalMessages = collectMessages finalCmd

                Vitest.expect(finalState.ActiveConflict.IsSome).toBe (true)
                Vitest.expect(finalMessages).toEqual ([| WriteRequested AbandonMerge |])
            }
        )

        Vitest.test (
            "A canceled fetch that left an index lock offers the clear lock dialog",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Fetch was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = None
                        })
                        [||]

                let deps = {
                    defaultDependencies with
                        refreshSynchronization = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested Fetch) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Fetch, Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock _, _))) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected clear-lock recovery after the canceled fetch."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.PendingRecovery).toEqual (Some(GitPendingRecovery.ClearStaleLock None))
                Vitest.expect(nextState.PendingConfirmation.IsSome).toBe (true)
            }
        )

        Vitest.test (
            "A download that an index lock stopped offers the lock dialog and remembers the download",
            fun () -> promise {
                let partial = lockedPartial VersionControlCodes.HydrationFailed
                let! _, finished = runPullWithPartial partial

                Vitest.expect(finished.PendingRecovery).toEqual (Some(GitPendingRecovery.ClearStaleLock None))
                Vitest.expect(finished.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual (Some(failureMessage partial))
            }
        )

        Vitest.test (
            "A removed index lock offers the download of the large files",
            fun () -> promise {
                let partial = lockedPartial VersionControlCodes.HydrationFailed
                let! deps, locked = runPullWithPartial partial

                let! finished =
                    confirmLockRemoval
                        {
                            deps with
                                listObjects = oneMissingObject
                        }
                        locked
                        (Ok(succeeded cleanStatus))

                Vitest
                    .expect(finished.PendingRecovery)
                    .toEqual (Some(GitPendingRecovery.RetryMaterialization(failureMessage partial)))

                Vitest.expect(finished.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual (None)
            }
        )

        Vitest.test (
            "The download offer after a removed index lock leaves out the instructions for removing the lock",
            fun () -> promise {
                let lockInstructions =
                    "Make sure no git process runs on the repository, then remove the lock file."

                let partial =
                    makeFailure
                        ProviderError
                        VersionControlCodes.HydrationFailed
                        "The large-file download did not finish."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = Some lockInstructions
                        })
                        [||]

                let! deps, locked = runPullWithPartial partial

                Vitest.expect(locked.RetryMaterializationAfterLockClear).toEqual (Some partial.Message)

                let! finished =
                    confirmLockRemoval
                        {
                            deps with
                                listObjects = oneMissingObject
                        }
                        locked
                        (Ok(succeeded cleanStatus))

                Vitest
                    .expect(finished.PendingRecovery)
                    .toEqual (Some(GitPendingRecovery.RetryMaterialization partial.Message))

                Vitest.expect(finished.PendingConfirmation.Value.Message.Contains lockInstructions).toBe false
            }
        )

        Vitest.test (
            "A failed lock removal does not offer the download of the large files",
            fun () -> promise {
                let! deps, locked = runPullWithPartial (lockedPartial VersionControlCodes.HydrationFailed)

                let! finished =
                    confirmLockRemoval
                        deps
                        locked
                        (Ok(
                            failed ProviderError VersionControlCodes.LockRemovalRefused "The lock could not be removed."
                        ))

                Vitest.expect(finished.PendingRecovery).toEqual (None)
                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual (None)
            }
        )

        Vitest.test (
            "A dismissed lock dialog does not offer the download of the large files",
            fun () -> promise {
                let! deps, locked = runPullWithPartial (lockedPartial VersionControlCodes.HydrationFailed)

                let dismissed, dismissCmd =
                    update deps ignore CancelPendingRemoteActionRequested locked

                let! dismissMessages = collectMessages dismissCmd
                let finished, finishCmd = update deps ignore dismissMessages[0] dismissed
                let! _ = collectMessages finishCmd

                Vitest.expect(finished.PendingRecovery).toEqual (None)
                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual (None)
            }
        )

        Vitest.test (
            "A download retry that an index lock stopped offers the download again once the lock is removed",
            fun () -> promise {
                // The failure that a single Materialize returns for a held lock.
                let lockInstructions =
                    "Make sure no git process runs on the repository, then remove the lock file."

                let failure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.IndexLocked
                        "Another git process holds the repository index."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = Some lockInstructions
                        })
                        [||]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            {
                                                Path = "large.bin"
                                                IsMaterialized = false
                                                IsLocallyAvailable = false
                                                SizeBytes = Some 100.
                                                ObjectId = Some "object-1"
                                            }
                                        |]
                                    )
                            }
                        materializeObjects = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                }

                let requested, requestCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages requestCmd

                let locked, lockedCmd =
                    match completion with
                    | [| WriteCompleted(_,
                                        _,
                                        RetryMaterialization,
                                        Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock _, _))) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the retry to offer the lock removal."

                let! _ = collectMessages lockedCmd

                Vitest
                    .expect(locked.PendingRecovery)
                    .toEqual (Some(GitPendingRecovery.ClearStaleLock(Some lockInstructions)))

                Vitest.expect(locked.RetryMaterializationAfterLockClear.IsSome).toBe true

                let! finished = confirmLockRemoval deps locked (Ok(succeeded cleanStatus))

                // The library message says that another process holds the index, which is no longer true when the offer
                // opens. The offer also leaves out the instructions for removing the lock.
                match finished.PendingRecovery with
                | Some(GitPendingRecovery.RetryMaterialization offer) ->
                    Vitest.expect(offer.Contains failure.Message).toBe false
                    Vitest.expect(offer.Contains lockInstructions).toBe false
                | _ -> failwith "Expected the download offer after the lock removal."

                Vitest.expect(finished.PendingConfirmation.Value.Message.Contains failure.Message).toBe false
                Vitest.expect(finished.PendingConfirmation.Value.Message.Contains lockInstructions).toBe false
                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual None
            }
        )

        Vitest.test (
            "staleLfsPaths lists only materialized objects that the status shows as plain modifications",
            fun () ->
                let status =
                    statusWithChanges [|
                        "modified.bin", FileChangeKindDto.Modified
                        "added.bin", FileChangeKindDto.Added
                        "deleted.bin", FileChangeKindDto.Deleted
                        "conflicted.bin", FileChangeKindDto.Conflicted
                        "missing.bin", FileChangeKindDto.Modified
                    |]

                let renamed = {
                    Path = "renamed.bin"
                    OldPath = Some "old.bin"
                    Kind = FileChangeKindDto.Renamed
                }

                let objects = [|
                    objectState "modified.bin" true
                    objectState "added.bin" true
                    objectState "deleted.bin" true
                    objectState "conflicted.bin" true
                    objectState "renamed.bin" true
                    objectState "unchanged.bin" true
                    objectState "missing.bin" false
                |]

                let listed =
                    staleLfsPaths objects {
                        status with
                            Changes = Array.append status.Changes [| renamed |]
                    }

                Vitest.expect(listed).toEqual [| "modified.bin" |]
        )

        Vitest.test (
            "Removing a lock restages the large files that git-lfs had already written and skips a path with a staged change",
            fun () -> promise {
                let status =
                    statusWithChanges [|
                        "first.bin", FileChangeKindDto.Modified
                        "second.bin", FileChangeKindDto.Modified
                        "staged.bin", FileChangeKindDto.Added
                    |]

                let requests = ResizeArray<ObjectPathsRequestDto>()

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "first.bin" true
                                            objectState "second.bin" true
                                            objectState "staged.bin" true
                                            objectState "clean.bin" true
                                        |]
                                    )
                            }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded status) }
                        materializeObjects =
                            fun request ->
                                requests.Add request
                                promise { return Ok(succeeded ()) }
                }

                let! requested, completion = runClearWrite deps

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, ClearStaleLock, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the lock removal to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(requests.Count).toBe 1
                Vitest.expect(requests[0].Paths).toEqual [| "first.bin"; "second.bin" |]
                Vitest.expect(finalState.ErrorNotice).toEqual None
            }
        )

        Vitest.test (
            "A restage that the library refuses for a local edit shows no error after the lock removal",
            fun () -> promise {
                let status =
                    statusWithChanges [|
                        "stale.bin", FileChangeKindDto.Modified
                        "edited.bin", FileChangeKindDto.Modified
                    |]

                let refusal =
                    makeFailure ProviderError VersionControlCodes.LocalChanges "1 of 2 paths failed" None [|
                        "edited.bin"
                    |]

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "stale.bin" true
                                            objectState "edited.bin" true
                                        |]
                                    )
                            }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded status) }
                        materializeObjects =
                            fun _ -> promise { return Ok(OperationResultDto.PartiallySucceeded(operation (), refusal)) }
                }

                let! requested, completion = runClearWrite deps

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, ClearStaleLock, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the lock removal to complete without an error."

                let! _ = collectMessages finalCmd

                Vitest.expect(finalState.ErrorNotice).toEqual None
                Vitest.expect(finalState.PendingRecovery).toEqual None
            }
        )

        Vitest.test (
            "A lock that stops the restage after the lock removal is routed as usual",
            fun () -> promise {
                let status = statusWithChanges [| "stale.bin", FileChangeKindDto.Modified |]

                let lockFailure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.IndexLocked
                        "Another git process holds the repository index."
                        (Some {
                            Code = VersionControlCodes.Recovery.RemoveIndexLock
                            Instructions = None
                        })
                        [||]

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects = fun _ -> promise { return Ok(succeeded [| objectState "stale.bin" true |]) }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded status) }
                        materializeObjects = fun _ -> promise { return Ok(OperationResultDto.Failed lockFailure) }
                }

                let! _, completion = runClearWrite deps

                match completion with
                | [| WriteCompleted(_, _, ClearStaleLock, Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock _, _))) |] ->
                    ()
                | _ -> failwith "Expected the lock to be offered for removal again."
            }
        )

        Vitest.test (
            "A restage failure that only mentions local changes in its text is routed as an error",
            fun () -> promise {
                let status = statusWithChanges [| "stale.bin", FileChangeKindDto.Modified |]

                let failure =
                    makeFailure ProviderError "lfs_failed" "'stale.bin' has local changes." None [||]

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects = fun _ -> promise { return Ok(succeeded [| objectState "stale.bin" true |]) }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded status) }
                        materializeObjects = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                }

                let! _, completion = runClearWrite deps

                match completion with
                | [| WriteCompleted(_, _, ClearStaleLock, Error _) |] -> ()
                | _ -> failwith "Expected the restage failure to be reported."
            }
        )

        Vitest.test (
            "A listing that fails after the lock removal completes the write, refreshes and abandons an open merge",
            fun () -> promise {
                let conflicted = conflictedStatus [| "conflict.txt" |]
                let mutable statusReads = 0

                let deps = {
                    (withRefresh conflicted defaultDependencies) with
                        listObjects =
                            fun _ -> promise { return Ok(failed ProviderError "lfs_failed" "The listing failed.") }
                        getStatus =
                            fun _ ->
                                statusReads <- statusReads + 1
                                promise { return Ok(succeeded conflicted) }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded conflicted) }
                }

                let! requested, completion = runClearWrite deps

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, ClearStaleLock, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the lock removal to complete."

                let! finalMessages = collectMessages finalCmd

                Vitest.expect(statusReads > 0).toBe true
                Vitest.expect(finalState.ErrorNotice).toEqual None
                Vitest.expect(finalMessages).toEqual ([| WriteRequested AbandonMerge |])
            }
        )

        Vitest.test (
            "A status that fails while the restage looks for stale paths completes the write and refreshes",
            fun () -> promise {
                let status = statusWithChanges [| "stale.bin", FileChangeKindDto.Modified |]
                let mutable statusReads = 0

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects = fun _ -> promise { return Ok(succeeded [| objectState "stale.bin" true |]) }
                        getStatus =
                            fun _ ->
                                statusReads <- statusReads + 1

                                promise {
                                    return
                                        if statusReads = 1 then
                                            Ok(failed ProviderError "status_failed" "The status failed.")
                                        else
                                            Ok(succeeded status)
                                }
                        clearStaleLock = fun _ -> promise { return Ok(succeeded status) }
                }

                let! requested, completion = runClearWrite deps

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, ClearStaleLock, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the lock removal to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(statusReads > 1).toBe true
                Vitest.expect(finalState.ErrorNotice).toEqual None
            }
        )

        Vitest.test (
            "A removed lock whose restage leaves no large object missing offers no download",
            fun () -> promise {
                let status = statusWithChanges [| "stale.bin", FileChangeKindDto.Modified |]
                let restageRequests = ResizeArray<ObjectPathsRequestDto>()
                let! deps, locked = runPullWithPartial (lockedPartial VersionControlCodes.HydrationFailed)

                let staleDeps = {
                    (withRefresh status deps) with
                        listObjects = fun _ -> promise { return Ok(succeeded [| objectState "stale.bin" true |]) }
                        materializeObjects =
                            fun request ->
                                restageRequests.Add request
                                promise { return Ok(succeeded ()) }
                }

                let! finished = confirmLockRemoval staleDeps locked (Ok(succeeded status))

                Vitest.expect(restageRequests.Count).toBe 1
                Vitest.expect(finished.PendingRecovery).toEqual None
                Vitest.expect(finished.PendingConfirmation).toEqual None
                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual None
            }
        )

        Vitest.test (
            "A removed lock whose restage leaves a large object missing offers the download",
            fun () -> promise {
                let status = statusWithChanges [| "stale.bin", FileChangeKindDto.Modified |]
                let partial = lockedPartial VersionControlCodes.HydrationFailed
                let! deps, locked = runPullWithPartial partial

                let staleDeps = {
                    (withRefresh status deps) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "stale.bin" true
                                            objectState "missing.bin" false
                                        |]
                                    )
                            }
                        materializeObjects = fun _ -> promise { return Ok(succeeded ()) }
                }

                let! finished = confirmLockRemoval staleDeps locked (Ok(succeeded status))

                Vitest
                    .expect(finished.PendingRecovery)
                    .toEqual (Some(GitPendingRecovery.RetryMaterialization(failureMessage partial)))

                Vitest.expect(finished.RetryMaterializationAfterLockClear).toEqual None
            }
        )

        Vitest.test (
            "A removed lock with no large object missing resumes the publish that waited for the download",
            fun () -> promise {
                let! deps, locked = runPullWithPartial (lockedPartial VersionControlCodes.HydrationFailed)

                let waiting = {
                    locked with
                        PendingPostMergePush = true
                }

                let! finished, messages = confirmLockRemovalWithMessages deps waiting (Ok(succeeded cleanStatus))

                Vitest.expect(finished.PendingRecovery).toEqual None
                Vitest.expect(finished.PendingPostMergePush).toBe false

                Vitest
                    .expect(
                        messages
                        |> hasMessage (
                            function
                            | WriteRequested(Push GitUpdateAcceptance.RequirePreview) -> true
                            | _ -> false
                        )
                    )
                    .toBe
                    true
            }
        )

        Vitest.test (
            "Download now requests a path that git-lfs wrote while the lock stopped the checkout, next to the missing ones, in one call",
            fun () -> promise {
                let status = statusWithChanges [| "stale.bin", FileChangeKindDto.Modified |]
                let requests = ResizeArray<ObjectPathsRequestDto>()

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "missing.bin" false
                                            objectState "stale.bin" true
                                            objectState "clean.bin" true
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun request ->
                                requests.Add request
                                promise { return Ok(succeeded ()) }
                }

                let _, retryCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages retryCmd

                match completion with
                | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] -> ()
                | _ -> failwith "Expected the download to complete."

                Vitest.expect(requests.Count).toBe 1
                Vitest.expect(requests[0].Paths).toEqual [| "stale.bin"; "missing.bin" |]
                Vitest.expect(requests[0].AlwaysRefreshTree).toBe true
            }
        )

        Vitest.test (
            "Download now does not fail when the library refuses a stale candidate that holds a local edit",
            fun () -> promise {
                let status = statusWithChanges [| "edited.bin", FileChangeKindDto.Modified |]

                let refusal =
                    makeFailure ProviderError VersionControlCodes.LocalChanges "1 of 2 paths failed" None [|
                        "edited.bin"
                    |]

                let deps = {
                    (withRefresh status defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "missing.bin" false
                                            objectState "edited.bin" true
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun _ -> promise { return Ok(OperationResultDto.PartiallySucceeded(operation (), refusal)) }
                }

                let requested, retryCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages retryCmd

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the download to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(finalState.ErrorNotice).toEqual None
            }
        )

        Vitest.test (
            "Download now asks for every pending path in one call and skips the one that the library refuses for a local edit",
            fun () -> promise {
                let refusal =
                    makeFailure ProviderError VersionControlCodes.LocalChanges "1 of 2 paths failed" None [|
                        "edited.bin"
                    |]

                let requests = ResizeArray<ObjectPathsRequestDto>()

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "edited.bin" false
                                            objectState "missing.bin" false
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun request ->
                                requests.Add request
                                promise { return Ok(OperationResultDto.PartiallySucceeded(operation (), refusal)) }
                }

                let running, retryCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages retryCmd

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] ->
                        update deps ignore completion[0] running
                    | _ -> failwith "Expected the download to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(requests.Count).toBe 1
                Vitest.expect(requests[0].Paths).toEqual [| "edited.bin"; "missing.bin" |]
                Vitest.expect(finalState.ErrorNotice).toEqual None
            }
        )

        Vitest.test (
            "Download now treats a refusal of every requested path as a success",
            fun () -> promise {
                let refusal =
                    makeFailure ProviderError VersionControlCodes.LocalChanges "'edited.bin' has local changes." None [|
                        "edited.bin"
                    |]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        listObjects = fun _ -> promise { return Ok(succeeded [| objectState "edited.bin" false |]) }
                        materializeObjects = fun _ -> promise { return Ok(OperationResultDto.Failed refusal) }
                }

                let running, retryCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages retryCmd

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] ->
                        update deps ignore completion[0] running
                    | _ -> failwith "Expected the download to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(finalState.ErrorNotice).toEqual None
            }
        )

        Vitest.test (
            "Download now reports the failure of a partial result that is no refusal",
            fun () -> promise {
                let failure =
                    makeFailure ProviderError "lfs_pull_failed" "1 of 2 paths failed: no space" None [| "missing.bin" |]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            objectState "other.bin" false
                                            objectState "missing.bin" false
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun _ -> promise { return Ok(OperationResultDto.PartiallySucceeded(operation (), failure)) }
                }

                let _, retryCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages retryCmd

                match completion with
                | [| WriteCompleted(_, _, RetryMaterialization, Error _) |] -> ()
                | _ -> failwith "Expected the partial failure to be reported."
            }
        )

        Vitest.test (
            "Download now makes no call when nothing is missing or stale",
            fun () -> promise {
                let mutable materializeCalls = 0

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        listObjects = fun _ -> promise { return Ok(succeeded [| objectState "present.bin" true |]) }
                        materializeObjects =
                            fun _ ->
                                materializeCalls <- materializeCalls + 1
                                promise { return Ok(succeeded ()) }
                }

                let running, retryCmd =
                    update deps ignore (WriteRequested RetryMaterialization) runningState

                let! completion = collectMessages retryCmd

                let finalState, finalCmd =
                    match completion with
                    | [| WriteCompleted(_, _, RetryMaterialization, Ok(Completed _)) |] ->
                        update deps ignore completion[0] running
                    | _ -> failwith "Expected the download to complete."

                let! _ = collectMessages finalCmd

                Vitest.expect(materializeCalls).toBe 0
                Vitest.expect(finalState.ErrorNotice).toEqual None
            }
        )

        Vitest.test (
            "A partial with an index lock and another code offers only the lock removal",
            fun () -> promise {
                let! deps, locked = runPullWithPartial (lockedPartial "lfs_pull_failed")

                Vitest.expect(locked.PendingRecovery).toEqual (Some(GitPendingRecovery.ClearStaleLock None))
                Vitest.expect(locked.RetryMaterializationAfterLockClear).toEqual (None)

                let! finished = confirmLockRemoval deps locked (Ok(succeeded cleanStatus))

                Vitest.expect(finished.PendingRecovery).toEqual (None)
            }
        )

        Vitest.test (
            "A clone whose large-file download met an index lock returns an error",
            fun () -> promise {
                let mutable replyResult = None
                let reply result = replyResult <- Some result

                let cloneRequest = {
                    OperationId = "clone-op"
                    ProviderLocation = "https://gitlab.example/carol/my-arc.git"
                    DisplayName = Some "my-arc"
                    TargetPath = "C:/clone-target"
                    TargetRef = None
                    MaterializeAllObjects = true
                }

                let deps = {
                    defaultDependencies with
                        cloneWorkspace =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            operation "C:/clone-target",
                                            lockedPartial VersionControlCodes.HydrationFailed
                                        )
                                    )
                            }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Clone(cloneRequest, reply))) GitState.Empty

                let! completionMessages = collectMessages requestCmd

                match completionMessages with
                | [| WriteCompleted(_, _, Clone _, Error _) |] ->
                    let _, finishCmd = update deps ignore completionMessages[0] stateAfterRequest
                    let! _ = collectMessages finishCmd
                    ()
                | _ -> failwith "Expected a clone with a locked download to return an error."

                match replyResult with
                | Some(Error _) -> ()
                | _ -> failwith "Expected the clone reply to be an error."
            }
        )

        Vitest.test (
            "A canceled synchronize with a refresh recovery refreshes and keeps the warning",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Publish was canceled after changing the workspace."
                        (Some {
                            Code = VersionControlCodes.Recovery.RefreshWorkspace
                            Instructions = None
                        })
                        [||]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected refresh recovery after the canceled publish."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.WarningNotice).toEqual (Some(failureMessage canceled))
                Vitest.expect(nextState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A partial synchronize with retry_publish offers to publish now",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value

                let retryFailure = {
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "The update was applied. Synchronize again to publish."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryPublish
                            Instructions = Some "Publish the applied update now."
                        })
                        [||] with
                        StateChanged = true
                }

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            {
                                                operation sync with
                                                    Publication = PublicationStateDto.LocalOnly
                                            },
                                            retryFailure
                                        )
                                    )
                            }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages writeCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected the partial synchronize completion."

                let! _ = collectMessages recoveryCmd

                Vitest
                    .expect(recoveryState.PendingRecovery)
                    .toEqual (Some(GitPendingRecovery.RetryPublish(failureMessage retryFailure)))

                Vitest
                    .expect(recoveryState.PendingConfirmation |> Option.map _.Title)
                    .toEqual (Some "Online sync pending")

                let _, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                Vitest
                    .expect(confirmMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "Later on the publish offer does not publish",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value

                let retryFailure = {
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "The update was applied. Synchronize again to publish."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryPublish
                            Instructions = Some "Publish the applied update now."
                        })
                        [||] with
                        StateChanged = true
                }

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            {
                                                operation sync with
                                                    Publication = PublicationStateDto.LocalOnly
                                            },
                                            retryFailure
                                        )
                                    )
                            }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages writeCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected the partial synchronize completion."

                let! _ = collectMessages recoveryCmd

                let cancelingState, cancelCmd =
                    update deps ignore CancelPendingRemoteActionRequested recoveryState

                let! cancelMessages = collectMessages cancelCmd

                let dismissedState, dismissCmd =
                    match cancelMessages with
                    | [| DismissRecoveryRequested |] -> update deps ignore cancelMessages[0] cancelingState
                    | _ -> failwith "Expected the recovery dismissal."

                let! dismissMessages = collectMessages dismissCmd

                let dispatchedMessages = Array.append cancelMessages dismissMessages

                Vitest
                    .expect(
                        dispatchedMessages
                        |> hasMessage (
                            function
                            | WriteRequested(Push _) -> true
                            | _ -> false
                        )
                    )
                    .toBe (false)

                Vitest.expect(dismissedState.PendingPostMergePush).toBe (false)
                Vitest.expect(dismissedState.PendingRecovery).toEqual (None)
            }
        )

        Vitest.test (
            "A partial without a mapped recovery leaves no pending publish",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value

                let partialFailure = {
                    makeFailure
                        Concurrency
                        VersionControlCodes.PreconditionFailed
                        "The workspace changed while synchronizing."
                        None
                        [||] with
                        StateChanged = true
                }

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            {
                                                operation sync with
                                                    Publication = PublicationStateDto.LocalOnly
                                            },
                                            partialFailure
                                        )
                                    )
                            }
                }

                let saveState = {
                    runningState with
                        ChangedFiles = [| changedFile "README.md" "M" " " false |]
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (PrimarySaveAllRequested "Save") saveState

                let! requestMessages = collectMessages requestCmd

                let stateAfterWrite, writeCmd =
                    match requestMessages with
                    | [| WriteRequested(PrimarySave _) |] -> update deps ignore requestMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the primary save request."

                let! completionMessages = collectWriteMessages writeCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterWrite
                    | _ -> failwith "Expected the partial save completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(finalState.PendingPostMergePush).toBe (false)
                Vitest.expect(finalState.PendingRecovery).toEqual (None)

                Vitest
                    .expect(finalState.WarningNotice |> Option.defaultValue "")
                    .toContain (failureMessage partialFailure)
            }
        )

        Vitest.test (
            "Publish now clears the parked recovery",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value

                let retryFailure = {
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "The update was applied. Synchronize again to publish."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryPublish
                            Instructions = Some "Publish the applied update now."
                        })
                        [||] with
                        StateChanged = true
                }

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            {
                                                operation sync with
                                                    Publication = PublicationStateDto.LocalOnly
                                            },
                                            retryFailure
                                        )
                                    )
                            }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages writeCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] requested
                    | _ -> failwith "Expected the partial synchronize completion."

                let! _ = collectMessages recoveryCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                Vitest.expect(confirmedState.PendingRecovery).toEqual (None)

                let! confirmMessages = collectMessages confirmCmd

                Vitest
                    .expect(confirmMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "A canceled fetch that needs inspection reports it after a refresh",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Fetch was canceled while inspecting the workspace."
                        (Some {
                            Code = VersionControlCodes.Recovery.InspectWorkspace
                            Instructions = None
                        })
                        [||]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        refreshSynchronization = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested Fetch) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Fetch, Ok(CompletedWithPendingRemoteFailure(_, message))) |] ->
                        Vitest.expect(message).toBe (failureMessage canceled)
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected inspection failure after the canceled fetch."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.ErrorNotice).toEqual (Some(failureMessage canceled))
                Vitest.expect(nextState.PendingConfirmation).toEqual (None)
            }
        )

        Vitest.test (
            "A canceled update with refresh_workspace refreshes and reports the notice",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Update was canceled after changing the workspace."
                        (Some {
                            Code = VersionControlCodes.Recovery.RefreshWorkspace
                            Instructions = None
                        })
                        [||]

                let deps = {
                    defaultDependencies with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                        getStatus = fun _ -> promise { return Ok(succeeded (statusForBranch "main")) }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected refresh recovery to complete with refreshed state."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.ErrorNotice).toEqual (None)
                Vitest.expect(nextState.WarningNotice).toEqual (Some(failureMessage canceled))
                Vitest.expect(nextState.Status.CurrentBranch).toEqual (Some "main")
            }
        )

        Vitest.test (
            "A failed update that changed the workspace refreshes before it reports the error",
            fun () -> promise {
                let failure = {
                    makeFailure
                        Timeout
                        "inspection_timeout"
                        "The update changed the workspace, but inspection timed out."
                        (Some {
                            Code = VersionControlCodes.Recovery.RefreshWorkspace
                            Instructions = None
                        })
                        [||] with
                        StateChanged = true
                }

                let callOrder = ResizeArray<string>()
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let refreshedStatus = {
                    statusForBranch "feature/refreshed" with
                        WorkspaceVersion = "v2"
                }

                let deps = {
                    defaultDependencies with
                        getSessionInfo =
                            fun _ ->
                                callOrder.Add("getSessionInfo")
                                promise { return Ok(succeeded sessionInfo) }
                        synchronize =
                            fun _ ->
                                callOrder.Add("synchronize")
                                promise { return Ok(OperationResultDto.Failed failure) }
                        getStatus =
                            fun _ ->
                                callOrder.Add("getStatus")
                                promise { return Ok(succeeded refreshedStatus) }
                        listRefs =
                            fun _ -> promise { return Ok(succeeded [| localBranch "feature/refreshed" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                        reportError = reportedErrors.Add
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(CompletedWithPendingRemoteFailure(_, message))) |] ->
                        Vitest.expect(message).toBe (failureMessage failure)
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the failed update to refresh before reporting."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(callOrder.IndexOf("getStatus") > callOrder.IndexOf("synchronize")).toBe (true)
                Vitest.expect(nextState.Status.CurrentBranch).toEqual (Some "feature/refreshed")
                Vitest.expect(nextState.WorkspaceVersion).toEqual (Some "v2")
                Vitest.expect(nextState.ErrorNotice).toEqual (Some(failureMessage failure))
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "A failed update whose refresh fails keeps the operation's message",
            fun () -> promise {
                let failure = {
                    makeFailure
                        Timeout
                        "inspection_timeout"
                        "The update changed the workspace, but inspection timed out."
                        (Some {
                            Code = VersionControlCodes.Recovery.RefreshWorkspace
                            Instructions = None
                        })
                        [||] with
                        StateChanged = true
                }

                let refreshFailure =
                    makeFailure ProviderError "status_read_failed" "Reading refreshed workspace status failed." None [||]

                let callOrder = ResizeArray<string>()
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        getSessionInfo =
                            fun _ ->
                                callOrder.Add("getSessionInfo")
                                promise { return Ok(succeeded sessionInfo) }
                        synchronize =
                            fun _ ->
                                callOrder.Add("synchronize")
                                promise { return Ok(OperationResultDto.Failed failure) }
                        getStatus =
                            fun _ ->
                                callOrder.Add("getStatus")
                                promise { return Ok(OperationResultDto.Failed refreshFailure) }
                        listRefs = fun _ -> promise { return Ok(succeeded refs) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                        reportError = reportedErrors.Add
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Error _) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the failed update to keep its error after refresh failed."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(callOrder.IndexOf("getStatus") > callOrder.IndexOf("synchronize")).toBe (true)
                Vitest.expect(nextState.ErrorNotice.IsSome).toBe (true)
                Vitest.expect(reportedErrors.Count).toBe (1)

                Vitest
                    .expect(reportedErrors[0].Message.StartsWith(failureMessage failure, StringComparison.Ordinal))
                    .toBe (true)

                Vitest
                    .expect(reportedErrors[0].Message.Contains("Refreshing the workspace afterwards failed"))
                    .toBe (true)
            }
        )

        Vitest.test (
            "A partial update with retry_materialization keeps the pulled result and offers the download",
            fun () -> promise {
                let failure =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Large-file hydration was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [||]

                let mutable materializedPaths = ResizeArray<string>()
                let mutable refreshTreeValues = ResizeArray<bool>()

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            operation cleanStatus.Synchronization.Value,
                                            failure
                                        )
                                    )
                            }
                        listObjects =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded [|
                                            {
                                                Path = "large.bin"
                                                IsMaterialized = false
                                                IsLocallyAvailable = false
                                                SizeBytes = Some 100.
                                                ObjectId = Some "object-1"
                                            }
                                            {
                                                Path = "small.txt"
                                                IsMaterialized = false
                                                IsLocallyAvailable = false
                                                SizeBytes = None
                                                ObjectId = None
                                            }
                                        |]
                                    )
                            }
                        materializeObjects =
                            fun request ->
                                materializedPaths.AddRange request.Paths
                                refreshTreeValues.Add request.AlwaysRefreshTree
                                promise { return Ok(succeeded ()) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let recoveryState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected partial update completion."

                let! _ = collectMessages finishCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                let retryState, retryCmd =
                    match confirmMessages with
                    | [| RetryMaterializationRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected materialization retry to be dispatched."

                let! retryMessages = collectMessages retryCmd

                let! _ =
                    match retryMessages with
                    | [| WriteRequested RetryMaterialization |] -> promise { return () }
                    | _ -> promise { return () }

                let _, retryCompletionCmd = update deps ignore retryMessages[0] retryState
                let! _ = collectMessages retryCompletionCmd

                Vitest
                    .expect(
                        recoveryState.PendingRecovery
                        |> Option.map (fun value ->
                            match value with
                            | GitPendingRecovery.RetryMaterialization _ -> true
                            | _ -> false
                        )
                    )
                    .toEqual (Some true)

                Vitest.expect(recoveryState.Status.CurrentBranch).toEqual (Some "main")
                Vitest.expect(materializedPaths |> Seq.toArray).toEqual ([| "large.bin"; "small.txt" |])
                Vitest.expect(refreshTreeValues |> Seq.toArray).toEqual ([| true |])
            }
        )

        Vitest.test (
            "An update conflict opens the first conflicted item",
            fun () -> promise {
                let conflictStatus = conflictedStatus [| "a.txt"; "b.txt" |]
                let conflict = conflictStatus.ActiveConflictSession.Value

                let failure =
                    makeFailure
                        Conflict
                        VersionControlCodes.ConflictsDetected
                        "Conflicts detected."
                        (Some {
                            Code = VersionControlCodes.Recovery.ResolveConflictSession
                            Instructions = None
                        })
                        [||]

                let pageStates = ResizeArray<PageState option>()
                let mutable loaded = None

                let deps = {
                    (withRefresh conflictStatus defaultDependencies) with
                        synchronize =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            operation cleanStatus.Synchronization.Value,
                                            failure
                                        )
                                    )
                            }
                        loadConflictPage =
                            fun session version path ->
                                loaded <- Some(session, version, path)
                                promise { return Ok(conflictPage path) }
                }

                let stateAfterRequest, requestCmd =
                    update deps pageStates.Add (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(Completed _)) |] ->
                        update deps pageStates.Add completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected conflict update completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(nextState.SelectedChangePath).toEqual (Some "a.txt")
                Vitest.expect(pageStates.Count).toBe (1)

                Vitest
                    .expect(loaded |> Option.map (fun (_, version, path) -> version, path))
                    .toEqual (Some("v1", "a.txt"))

                Vitest.expect(nextState.ActiveConflict).toEqual (Some conflict)
            }
        )

        Vitest.test (
            "Confirming a resolution finalizes when nothing remains and resumes the pending publish",
            fun () -> promise {
                let conflict = (conflictedStatus [| "conflict.txt" |]).ActiveConflictSession.Value

                let refreshedHandle = {
                    SessionId = "conflict-2"
                    Version = "2"
                }

                let mutable resolveRequest = None
                let mutable finalizeRequest = None
                let mutable statusCalls = 0

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun request ->
                                resolveRequest <- Some request

                                promise {
                                    return
                                        Ok(
                                            succeeded {
                                                RefreshedHandle = refreshedHandle
                                                RemainingItems = [||]
                                            }
                                        )
                                }
                        getStatus =
                            fun _ ->
                                statusCalls <- statusCalls + 1
                                promise { return Ok(succeeded cleanStatus) }
                        finalizeConflict =
                            fun request ->
                                finalizeRequest <- Some request
                                promise { return Ok(succeeded (Some "rev")) }
                }

                let request = {
                    Path = "conflict.txt"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.SupplyResolvedContent "resolved"
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        PendingPostMergePush = true
                        SelectedChangePath = Some "conflict.txt"
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest.expect(outcome.Finalized).toBe (true)
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected merge resolution completion."

                let! messages = collectMessages finishCmd

                Vitest.expect(statusCalls).toBe (2)
                Vitest.expect(resolveRequest.Value.Handle).toEqual (request.Handle)
                Vitest.expect(resolveRequest.Value.ExpectedWorkspaceVersion).toBe ("v1")

                Vitest
                    .expect(resolveRequest.Value.Resolution)
                    .toEqual (ConflictResolutionDto.SupplyResolvedContent "resolved")

                Vitest.expect(finalizeRequest.Value.Handle).toEqual (refreshedHandle)
                Vitest.expect(finalizeRequest.Value.ExpectedWorkspaceVersion).toBe (cleanStatus.WorkspaceVersion)
                Vitest.expect(nextState.PendingPostMergePush).toBe (false)

                Vitest
                    .expect(messages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "Picking an unmaterialized candidate waits until the finalization succeeds",
            fun () -> promise {
                let conflict = (conflictedStatus [| "data/big.bin" |]).ActiveConflictSession.Value
                let mutable resolveRequest = None
                let mutable materializeRequest = None
                let mutable callOrder = []
                let mutable finalized = false

                let warning = {
                    Code = VersionControlCodes.ObjectNotMaterialized
                    Message = "The selected file holds a pointer."
                }

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun request ->
                                resolveRequest <- Some request
                                callOrder <- callOrder @ [ "resolve" ]

                                promise {
                                    return
                                        Ok(
                                            succeededWithWarnings [| warning |] {
                                                RefreshedHandle = conflict.Handle
                                                RemainingItems = [||]
                                            }
                                        )
                                }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        finalizeConflict =
                            fun _ ->
                                callOrder <- callOrder @ [ "finalize" ]
                                finalized <- true
                                promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun request ->
                                if not finalized then
                                    failwith "Materialization ran before conflict finalization."

                                materializeRequest <- Some request
                                callOrder <- callOrder @ [ "materialize" ]
                                promise { return Ok(succeeded ()) }
                }

                let request = {
                    Path = "data/big.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate "target"
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = true
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest.expect(outcome.Notice).toEqual None
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected merge resolution completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(resolveRequest.Value.Resolution).toEqual (ConflictResolutionDto.PickCandidate "target")
                Vitest.expect(materializeRequest.Value.Paths).toEqual [| "data/big.bin" |]
                Vitest.expect(materializeRequest.Value.AlwaysRefreshTree).toBe true
                Vitest.expect(callOrder).toEqual [ "resolve"; "finalize"; "materialize" ]
                Vitest.expect(nextState.WarningNotice).toEqual None
            }
        )

        Vitest.test (
            "Dialog finalization materializes a picked path after in-flow finalization fails",
            fun () -> promise {
                let conflictStatus = conflictedStatus [| "data/big.bin" |]
                let conflict = conflictStatus.ActiveConflictSession.Value

                let warning = {
                    Code = VersionControlCodes.ObjectNotMaterialized
                    Message = "The selected file holds a pointer."
                }

                let finalizeFailure =
                    makeFailure ProviderError "finalize_failed" "Could not finalize the conflict." None [||]

                let mutable finalizeCalls = 0
                let mutable finalized = false
                let materializedPaths = ResizeArray<string>()

                let deps = {
                    (withRefresh conflictStatus defaultDependencies) with
                        resolveConflict =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeededWithWarnings [| warning |] {
                                            RefreshedHandle = conflict.Handle
                                            RemainingItems = [||]
                                        }
                                    )
                            }
                        getStatus =
                            fun _ -> promise {
                                return Ok(succeeded (if finalized then cleanStatus else conflictStatus))
                            }
                        finalizeConflict =
                            fun _ ->
                                finalizeCalls <- finalizeCalls + 1

                                if finalizeCalls = 1 then
                                    promise { return Ok(OperationResultDto.Failed finalizeFailure) }
                                else
                                    finalized <- true
                                    promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun request ->
                                if not finalized then
                                    failwith "Materialization ran before conflict finalization."

                                materializedPaths.AddRange request.Paths
                                promise { return Ok(succeeded ()) }
                }

                let pickRequest = {
                    Path = "data/big.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = true
                }

                let stateAfterPickRequest, pickCmd =
                    update deps ignore (ConfirmMergeResolutionRequested pickRequest) state

                let! pickMessages = collectMessages pickCmd

                let stateAfterPickError, errorCmd =
                    match pickMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Error(ConfirmMergeResolutionError.Failed _)) |] ->
                        update deps ignore pickMessages[0] stateAfterPickRequest
                    | _ -> failwith "Expected the in-flow finalization to fail."

                let! errorMessages = collectMessages errorCmd

                let stateBeforeRefresh, refreshCmd =
                    match errorMessages with
                    | [| RefreshRequested |] -> update deps ignore RefreshRequested stateAfterPickError
                    | _ -> failwith "Expected the failed pick to request a refresh."

                let! refreshMessages = collectMessages refreshCmd

                let stateAfterRefresh, refreshFinishCmd =
                    match refreshMessages with
                    | [| (RefreshCompleted _ as message) |] -> update deps ignore message stateBeforeRefresh
                    | _ -> failwith "Expected the conflict refresh to complete."

                let! _ = collectMessages refreshFinishCmd
                Vitest.expect(stateAfterRefresh.PendingMaterializations).toEqual [ ("data/big.bin", false) ]

                let dialogFinalizeState, dialogFinalizeCmd =
                    update deps ignore (WriteRequested FinalizeMerge) stateAfterRefresh

                let! finalizeMessages = collectMessages dialogFinalizeCmd

                let finalState, finishCmd =
                    match finalizeMessages with
                    | [| WriteCompleted(_, _, FinalizeMerge, Ok(Completed _)) |] ->
                        update deps ignore finalizeMessages[0] dialogFinalizeState
                    | _ -> failwith "Expected dialog finalization to complete."

                let! _ = collectMessages finishCmd

                Vitest.expect(finalizeCalls).toBe 2
                Vitest.expect(materializedPaths.ToArray()).toEqual [| "data/big.bin" |]
                Vitest.expect(finalState.PendingMaterializations).toEqual []
            }
        )

        Vitest.test (
            "Two conflict picks materialize only the warned path after finalization",
            fun () -> promise {
                let conflict =
                    (conflictedStatus [| "first.bin"; "second.txt" |]).ActiveConflictSession.Value

                let refreshedHandle = {
                    SessionId = conflict.Handle.SessionId
                    Version = "2"
                }

                let mutable finalized = false
                let resolvedPaths = ResizeArray<string>()
                let materializedPaths = ResizeArray<string>()

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun request ->
                                resolvedPaths.Add request.Path

                                if request.Path = "first.bin" then
                                    promise {
                                        return
                                            Ok(
                                                succeededWithWarnings
                                                    [|
                                                        {
                                                            Code = VersionControlCodes.ObjectNotMaterialized
                                                            Message = "The selected file holds a pointer."
                                                        }
                                                    |]
                                                    {
                                                        RefreshedHandle = refreshedHandle
                                                        RemainingItems = [| conflict.Items[1] |]
                                                    }
                                            )
                                    }
                                else
                                    promise {
                                        return
                                            Ok(
                                                succeeded {
                                                    RefreshedHandle = {
                                                        SessionId = conflict.Handle.SessionId
                                                        Version = "3"
                                                    }
                                                    RemainingItems = [||]
                                                }
                                            )
                                    }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                        finalizeConflict =
                            fun _ ->
                                finalized <- true
                                promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun request ->
                                if not finalized then
                                    failwith "Materialization ran before conflict finalization."

                                materializedPaths.AddRange request.Paths
                                promise { return Error "The local cache is unavailable." }
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = true
                }

                let firstRequest = {
                    Path = "first.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let stateAfterFirst, firstCmd =
                    update deps ignore (ConfirmMergeResolutionRequested firstRequest) state

                let! firstMessages = collectMessages firstCmd

                let stateAfterFirst, firstFinishCmd =
                    match firstMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        let nextState, finishCmd = update deps ignore firstMessages[0] stateAfterFirst
                        Vitest.expect(outcome.Finalized).toBe (false)
                        nextState, finishCmd
                    | _ -> failwith "Expected the first conflict resolution to complete."

                let! _ = collectMessages firstFinishCmd
                Vitest.expect(stateAfterFirst.PendingMaterializations).toEqual [ ("first.bin", false) ]
                Vitest.expect(stateAfterFirst.PendingMaterializationSessionId).toEqual (Some conflict.Handle.SessionId)

                let secondRequest = {
                    Path = "second.txt"
                    Handle = refreshedHandle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let stateAfterSecond, secondCmd =
                    update deps ignore (ConfirmMergeResolutionRequested secondRequest) stateAfterFirst

                let! secondMessages = collectMessages secondCmd

                let finalState, secondFinishCmd =
                    match secondMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        let nextState, finishCmd = update deps ignore secondMessages[0] stateAfterSecond
                        Vitest.expect(outcome.Finalized).toBe (true)
                        nextState, finishCmd
                    | _ -> failwith "Expected the second conflict resolution to complete."

                let! _ = collectMessages secondFinishCmd

                Vitest.expect(resolvedPaths.ToArray()).toEqual [| "first.bin"; "second.txt" |]
                Vitest.expect(materializedPaths.ToArray()).toEqual [| "first.bin" |]
                Vitest.expect(finalState.PendingMaterializations).toEqual []
                Vitest.expect(finalState.PendingMaterializationSessionId).toEqual None

                Vitest
                    .expect(finalState.WarningNotice)
                    .toEqual (Some "Could not download 'first.bin'. Use Download LFS file to try again.")
            }
        )

        Vitest.test (
            "Finalization names the path that the library reports as failed, not the first requested one",
            fun () -> promise {
                let conflict =
                    (conflictedStatus [| "warning.bin"; "partial.bin" |]).ActiveConflictSession.Value

                let warningHandle = {
                    SessionId = conflict.Handle.SessionId
                    Version = "2"
                }

                let partialHandle = {
                    SessionId = conflict.Handle.SessionId
                    Version = "3"
                }

                let partialFailure =
                    makeFailure
                        Conflict
                        "object_materialization_failed"
                        "The selected object could not be restored."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [| "partial.bin" |]

                let downloadFailure =
                    makeFailure ProviderError "lfs_pull_failed" "1 of 2 paths failed: no space" None [| "partial.bin" |]

                let warning = {
                    Code = VersionControlCodes.ObjectNotMaterialized
                    Message = "The selected file holds a pointer."
                }

                let mutable finalized = false
                let materializeRequests = ResizeArray<ObjectPathsRequestDto>()

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun request ->
                                if request.Path = "warning.bin" then
                                    promise {
                                        return
                                            Ok(
                                                succeededWithWarnings [| warning |] {
                                                    RefreshedHandle = warningHandle
                                                    RemainingItems = [| conflict.Items[1] |]
                                                }
                                            )
                                    }
                                else
                                    promise {
                                        return
                                            Ok(
                                                OperationResultDto.PartiallySucceeded(
                                                    operation {
                                                        RefreshedHandle = partialHandle
                                                        RemainingItems = [||]
                                                    },
                                                    partialFailure
                                                )
                                            )
                                    }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                        finalizeConflict =
                            fun _ ->
                                finalized <- true
                                promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun request ->
                                if not finalized then
                                    failwith "Materialization ran before conflict finalization."

                                materializeRequests.Add request

                                promise {
                                    return Ok(OperationResultDto.PartiallySucceeded(operation (), downloadFailure))
                                }
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = true
                }

                let warningRequest = {
                    Path = "warning.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let stateAfterWarningRequest, warningCmd =
                    update deps ignore (ConfirmMergeResolutionRequested warningRequest) state

                let! warningMessages = collectMessages warningCmd

                let stateAfterWarning, warningFinishCmd =
                    match warningMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok _) |] ->
                        update deps ignore warningMessages[0] stateAfterWarningRequest
                    | _ -> failwith "Expected the first pick to complete."

                let! _ = collectMessages warningFinishCmd
                Vitest.expect(stateAfterWarning.PendingMaterializations).toEqual [ ("warning.bin", false) ]

                let partialRequest = {
                    Path = "partial.bin"
                    Handle = warningHandle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let stateAfterPartialRequest, partialCmd =
                    update deps ignore (ConfirmMergeResolutionRequested partialRequest) stateAfterWarning

                let! partialMessages = collectMessages partialCmd

                let finalState, partialFinishCmd =
                    match partialMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest.expect(outcome.Finalized).toBe true
                        update deps ignore partialMessages[0] stateAfterPartialRequest
                    | _ -> failwith "Expected the second pick to complete."

                let! _ = collectMessages partialFinishCmd

                Vitest.expect(materializeRequests.Count).toBe 1
                Vitest.expect(materializeRequests[0].Paths).toEqual [| "warning.bin"; "partial.bin" |]
                Vitest.expect(materializeRequests[0].AlwaysRefreshTree).toBe true

                Vitest
                    .expect(finalState.WarningNotice)
                    .toEqual (
                        Some "Could not restore 'partial.bin' from the local cache. Use Download LFS file to try again."
                    )
            }
        )

        Vitest.test (
            "A partial candidate resolve queues its path and explains the delayed download",
            fun () -> promise {
                let conflict =
                    (conflictedStatus [| "partial.bin"; "next.txt" |]).ActiveConflictSession.Value

                let refreshedHandle = {
                    SessionId = conflict.Handle.SessionId
                    Version = "2"
                }

                let failure =
                    makeFailure
                        Conflict
                        "object_materialization_failed"
                        "The selected object could not be restored."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [| "partial.bin" |]

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.PartiallySucceeded(
                                            operation {
                                                RefreshedHandle = refreshedHandle
                                                RemainingItems = [| conflict.Items[1] |]
                                            },
                                            failure
                                        )
                                    )
                            }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                }

                let request = {
                    Path = "partial.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = true
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let! messages = collectMessages requestCmd

                let nextState, finishCmd =
                    match messages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        let nextState, finishCmd = update deps ignore messages[0] stateAfterRequest
                        Vitest.expect(outcome.Finalized).toBe (false)

                        Vitest
                            .expect(outcome.Notice)
                            .toEqual (
                                Some
                                    "The file was resolved, but its content could not be restored from the local cache yet. ARCitISA restores it after the merge."
                            )

                        nextState, finishCmd
                    | _ -> failwith "Expected the partial conflict resolution to continue."

                let! _ = collectMessages finishCmd
                Vitest.expect(nextState.PendingMaterializations).toEqual [ ("partial.bin", true) ]

                Vitest
                    .expect(nextState.WarningNotice)
                    .toEqual (
                        Some
                            "The file was resolved, but its content could not be restored from the local cache yet. ARCitISA restores it after the merge."
                    )

                Vitest.expect(nextState.ActiveConflict |> Option.map _.Handle).toEqual (Some refreshedHandle)
            }
        )

        Vitest.test (
            "Finalization restores local partial picks when automatic downloads are off",
            fun () -> promise {
                let conflict =
                    (conflictedStatus [| "partial.bin"; "warning.bin" |]).ActiveConflictSession.Value

                let partialHandle = {
                    SessionId = conflict.Handle.SessionId
                    Version = "2"
                }

                let warningHandle = {
                    SessionId = conflict.Handle.SessionId
                    Version = "3"
                }

                let partialFailure =
                    makeFailure
                        Conflict
                        "object_materialization_failed"
                        "The selected object could not be restored."
                        (Some {
                            Code = VersionControlCodes.Recovery.RetryMaterialization
                            Instructions = None
                        })
                        [| "partial.bin" |]

                let warning = {
                    Code = VersionControlCodes.ObjectNotMaterialized
                    Message = "The selected file holds a pointer."
                }

                let mutable finalized = false
                let materializedPaths = ResizeArray<string>()

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun request ->
                                if request.Path = "partial.bin" then
                                    promise {
                                        return
                                            Ok(
                                                OperationResultDto.PartiallySucceeded(
                                                    operation {
                                                        RefreshedHandle = partialHandle
                                                        RemainingItems = [| conflict.Items[1] |]
                                                    },
                                                    partialFailure
                                                )
                                            )
                                    }
                                else
                                    promise {
                                        return
                                            Ok(
                                                succeededWithWarnings [| warning |] {
                                                    RefreshedHandle = warningHandle
                                                    RemainingItems = [||]
                                                }
                                            )
                                    }
                        getStatus =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded (
                                            if finalized then
                                                cleanStatus
                                            else
                                                conflictedStatus [| "partial.bin"; "warning.bin" |]
                                        )
                                    )
                            }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                        finalizeConflict =
                            fun _ ->
                                finalized <- true
                                promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun request ->
                                materializedPaths.AddRange request.Paths
                                promise { return Error "The local cache is unavailable." }
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = false
                }

                let partialRequest = {
                    Path = "partial.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let stateAfterPartialRequest, partialCmd =
                    update deps ignore (ConfirmMergeResolutionRequested partialRequest) state

                let! partialMessages = collectMessages partialCmd

                let stateAfterPartial, partialFinishCmd =
                    match partialMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest
                            .expect(outcome.Notice)
                            .toEqual (
                                Some
                                    "The file was resolved, but its content could not be restored from the local cache yet. ARCitISA restores it after the merge."
                            )

                        update deps ignore partialMessages[0] stateAfterPartialRequest
                    | _ -> failwith "Expected the partial pick to complete."

                let! _ = collectMessages partialFinishCmd
                Vitest.expect(stateAfterPartial.PendingMaterializations).toEqual [ ("partial.bin", true) ]

                let warningRequest = {
                    Path = "warning.bin"
                    Handle = partialHandle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let stateAfterWarningRequest, warningCmd =
                    update deps ignore (ConfirmMergeResolutionRequested warningRequest) stateAfterPartial

                let! warningMessages = collectMessages warningCmd

                let finalState, warningFinishCmd =
                    match warningMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest.expect(outcome.Finalized).toBe (true)
                        update deps ignore warningMessages[0] stateAfterWarningRequest
                    | _ -> failwith "Expected the warning pick to complete."

                let! _ = collectMessages warningFinishCmd

                Vitest.expect(materializedPaths.ToArray()).toEqual [| "partial.bin" |]
                Vitest.expect(finalState.PendingMaterializations).toEqual []
                Vitest.expect(finalState.PendingMaterializationSessionId).toEqual None

                Vitest
                    .expect(finalState.WarningNotice)
                    .toEqual (
                        Some "Could not restore 'partial.bin' from the local cache. Use Download LFS file to try again."
                    )
            }
        )

        Vitest.test (
            "Picking an unmaterialized object without automatic downloads shows a notice",
            fun () -> promise {
                let conflict = (conflictedStatus [| "data/big.bin" |]).ActiveConflictSession.Value
                let mutable materializeCalls = 0

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeededWithWarnings
                                            [|
                                                {
                                                    Code = VersionControlCodes.ObjectNotMaterialized
                                                    Message = "The selected file holds a pointer."
                                                }
                                            |]
                                            {
                                                RefreshedHandle = conflict.Handle
                                                RemainingItems = [||]
                                            }
                                    )
                            }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        finalizeConflict = fun _ -> promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun _ ->
                                materializeCalls <- materializeCalls + 1
                                promise { return Ok(succeeded ()) }
                }

                let request = {
                    Path = "data/big.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate "target"
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = false
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest
                            .expect(outcome.Notice)
                            .toEqual (
                                Some
                                    "The online version isn't downloaded yet. Use Download LFS file to get it after the merge."
                            )

                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected merge resolution completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(materializeCalls).toBe 0

                Vitest
                    .expect(nextState.WarningNotice)
                    .toEqual (
                        Some "The online version isn't downloaded yet. Use Download LFS file to get it after the merge."
                    )

                Vitest.expect(nextState.PendingMaterializations).toEqual []
            }
        )

        Vitest.test (
            "Picking an unmaterialized workspace version explains where to download it",
            fun () -> promise {
                let conflict = (conflictedStatus [| "workspace.bin" |]).ActiveConflictSession.Value

                let warning = {
                    Code = VersionControlCodes.ObjectNotMaterialized
                    Message = "The selected file holds a pointer."
                }

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeededWithWarnings [| warning |] {
                                            RefreshedHandle = conflict.Handle
                                            RemainingItems = [||]
                                        }
                                    )
                            }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        finalizeConflict = fun _ -> promise { return Ok(succeeded (Some "rev")) }
                }

                let request = {
                    Path = "workspace.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate keepMineCandidateId
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = false
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let! messages = collectMessages requestCmd

                let finalState, finishCmd =
                    match messages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest
                            .expect(outcome.Notice)
                            .toEqual (
                                Some
                                    "Your version is not in the local cache. Use Download LFS file to get it after the merge."
                            )

                        update deps ignore messages[0] stateAfterRequest
                    | _ -> failwith "Expected merge resolution completion."

                let! _ = collectMessages finishCmd

                Vitest
                    .expect(finalState.WarningNotice)
                    .toEqual (
                        Some "Your version is not in the local cache. Use Download LFS file to get it after the merge."
                    )
            }
        )

        Vitest.test (
            "Abandoning a merge clears queued materializations",
            fun () -> promise {
                let conflict = (conflictedStatus [| "pending.bin" |]).ActiveConflictSession.Value

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        cancelConflict = fun _ -> promise { return Ok(succeeded ()) }
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        PendingMaterializations = [ ("pending.bin", false) ]
                        PendingMaterializationSessionId = Some conflict.Handle.SessionId
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested AbandonMerge) state

                let! messages = collectMessages requestCmd

                let nextState, finishCmd =
                    match messages with
                    | [| WriteCompleted(_, _, AbandonMerge, Ok(Completed _)) |] ->
                        update deps ignore messages[0] stateAfterRequest
                    | _ -> failwith "Expected the merge abandonment to complete."

                let! _ = collectMessages finishCmd
                Vitest.expect(nextState.PendingMaterializations).toEqual []
                Vitest.expect(nextState.PendingMaterializationSessionId).toEqual None
            }
        )

        Vitest.test (
            "Picking a deleted candidate uses its id and skips materialization",
            fun () -> promise {
                let baseConflict =
                    (conflictedStatus [| "deleted.bin" |]).ActiveConflictSession.Value

                let deletedTarget: ConflictCandidateDto = {
                    CandidateId = useOnlineCandidateId
                    Label = "Online"
                    Revision = Some "abc1234"
                    Preview = None
                    Object = None
                }

                let item = {
                    baseConflict.Items[0] with
                        Candidates = [| deletedTarget |]
                        CombinedPreview = Some(ContentViewDto.Unsupported None)
                        SupportsResolvedContent = false
                }

                let conflict = { baseConflict with Items = [| item |] }
                let mutable resolveRequest = None
                let mutable materializeCalls = 0

                let warning = {
                    Code = VersionControlCodes.ObjectNotMaterialized
                    Message = "The selected file holds a pointer."
                }

                let deps = {
                    defaultDependencies with
                        resolveConflict =
                            fun request ->
                                resolveRequest <- Some request

                                promise {
                                    return
                                        Ok(
                                            succeededWithWarnings [| warning |] {
                                                RefreshedHandle = conflict.Handle
                                                RemainingItems = [||]
                                            }
                                        )
                                }
                        getStatus = fun _ -> promise { return Ok(succeeded cleanStatus) }
                        finalizeConflict = fun _ -> promise { return Ok(succeeded (Some "rev")) }
                        materializeObjects =
                            fun _ ->
                                materializeCalls <- materializeCalls + 1
                                promise { return Ok(succeeded ()) }
                }

                let request = {
                    Path = "deleted.bin"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.PickCandidate useOnlineCandidateId
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                        DownloadLargeFiles = true
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (ConfirmMergeResolutionRequested request) state

                let! messages = collectMessages requestCmd

                let _, finishCmd =
                    match messages with
                    | [| ConfirmMergeResolutionCompleted(_, Ok _) |] -> update deps ignore messages[0] stateAfterRequest
                    | _ -> failwith "Expected the deleted file resolution to complete."

                let! _ = collectMessages finishCmd

                Vitest.expect(candidateToFileChoiceVersion deletedTarget).toEqual {
                    CandidateId = useOnlineCandidateId
                    SizeBytes = None
                    IsDownloaded = None
                    Revision = Some "abc1234"
                    IsDeleted = true
                }

                Vitest
                    .expect(resolveRequest.Value.Resolution)
                    .toEqual (ConflictResolutionDto.PickCandidate useOnlineCandidateId)

                Vitest.expect(materializeCalls).toBe 0
            }
        )

        Vitest.test (
            "A stale conflict handle reloads the session instead of resolving",
            fun () -> promise {
                let conflict = (conflictedStatus [| "conflict.txt" |]).ActiveConflictSession.Value

                let failure =
                    makeFailure
                        Concurrency
                        VersionControlCodes.PreconditionFailed
                        "The conflict session is stale."
                        (Some {
                            Code = VersionControlCodes.Recovery.RefreshConflictSession
                            Instructions = None
                        })
                        [||]

                let pageStates = ResizeArray<PageState option>()

                let deps = {
                    defaultDependencies with
                        resolveConflict = fun _ -> promise { return Ok(OperationResultDto.Failed failure) }
                }

                let request = {
                    Path = "conflict.txt"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.SupplyResolvedContent "resolved"
                }

                let state = {
                    runningState with
                        SelectedChangePath = Some "conflict.txt"
                }

                let stateAfterRequest, requestCmd =
                    update deps pageStates.Add (ConfirmMergeResolutionRequested request) state

                let! completionMessages = collectMessages requestCmd

                let nextState, finishCmd =
                    match completionMessages with
                    | [| ConfirmMergeResolutionCompleted(_, Error(ConfirmMergeResolutionError.Stale _)) |] ->
                        update deps pageStates.Add completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected stale conflict completion."

                let! messages = collectMessages finishCmd

                Vitest.expect(nextState.SelectedChangePath).toEqual (None)
                Vitest.expect(pageStates |> Seq.toArray).toEqual ([| None |])
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "A missing dependency asks to configure it and retries once",
            fun () -> promise {
                let mutable promptCalls = 0
                let mutable installCalls = 0
                let mutable publishCalls = 0
                let mutable installedComponent = None
                let sync = cleanStatus.Synchronization.Value

                let dependencies = [|
                    {
                        Component = "git"
                        Installed = true
                        Version = Some "2.0"
                        Compatible = true
                        Remediation = None
                    }
                    {
                        Component = "git-lfs"
                        Installed = true
                        Version = Some "3.0"
                        Compatible = true
                        Remediation = None
                    }
                    {
                        Component = "git-lfs-configuration"
                        Installed = false
                        Version = None
                        Compatible = false
                        Remediation = Some "run git lfs install"
                    }
                |]

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun _ ->
                                publishCalls <- publishCalls + 1

                                if publishCalls = 1 then
                                    promise {
                                        return
                                            Ok(
                                                failed
                                                    DependencyMissing
                                                    "lfs_operation_failed"
                                                    "Git LFS is not configured."
                                            )
                                    }
                                else
                                    promise { return Ok(succeeded sync) }
                        checkDependencies = fun _ -> promise { return Ok(succeeded dependencies) }
                        confirmInstall =
                            fun _ ->
                                promptCalls <- promptCalls + 1
                                true
                        installDependency =
                            fun request ->
                                installedComponent <- Some request.Component
                                installCalls <- installCalls + 1
                                promise { return Ok(succeeded dependencies[2]) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let stateAfterPrompt, promptCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(RequiresDependencyInstall("git-lfs-configuration", _))) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the missing dependency prompt."

                let! promptMessages = collectMessages promptCmd

                let stateAfterAnswer, installCmd =
                    match promptMessages with
                    | [| WriteInstallPromptAnswered(_, Push _, "git-lfs-configuration", true) |] ->
                        update deps ignore promptMessages[0] stateAfterPrompt
                    | _ -> failwith "Expected an affirmative dependency-install answer."

                let! installMessages = collectMessages installCmd

                let stateAfterInstall, retryCmd =
                    match installMessages with
                    | [| WriteInstallCompleted(_, Push _, Ok _) |] ->
                        update deps ignore installMessages[0] stateAfterAnswer
                    | _ -> failwith "Expected dependency installation to complete."

                let! retryMessages = collectMessages retryCmd

                let finalState, finishCmd =
                    match retryMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore retryMessages[0] stateAfterInstall
                    | _ -> failwith "Expected the retried publish to complete."

                let! _ = collectMessages finishCmd

                Vitest.expect(promptCalls).toBe (1)
                Vitest.expect(installCalls).toBe (1)
                Vitest.expect(publishCalls).toBe (2)
                Vitest.expect(installedComponent).toEqual (Some "git-lfs-configuration")
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A stale workspace token refreshes and retries the write once",
            fun () -> promise {
                let requests = ResizeArray<CreateRevisionRequestDto>()

                let deps = {
                    (withRefresh
                        {
                            cleanStatus with
                                WorkspaceVersion = "v2"
                        }
                        defaultDependencies) with
                        createRevision =
                            fun request ->
                                requests.Add request

                                if requests.Count = 1 then
                                    promise {
                                        return
                                            Ok(
                                                failed
                                                    Concurrency
                                                    VersionControlCodes.PreconditionFailed
                                                    "workspace version is stale"
                                            )
                                    }
                                else
                                    promise { return Ok(succeeded "revision-2") }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = update deps ignore (CommitAllRequested "save") state
                let! messages = collectMessages command
                let requested, writeCmd = update deps ignore messages[0] model
                let! completion = collectMessages writeCmd

                let finalState, _ =
                    match completion with
                    | [| WriteCompleted(_, _, CommitAll _, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the retried commit to complete."

                Vitest.expect(requests.Count).toBe (2)
                Vitest.expect(requests[0].ExpectedWorkspaceVersion).toBe ("v1")
                Vitest.expect(requests[1].ExpectedWorkspaceVersion).toBe ("v2")
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A workspace token that is stale twice reports the failure",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()
                let mutable calls = 0

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        reportError = reportedErrors.Add
                        createRevision =
                            fun _ ->
                                calls <- calls + 1

                                promise {
                                    return Ok(failed Concurrency VersionControlCodes.PreconditionFailed "still stale")
                                }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = update deps ignore (CommitAllRequested "save") state
                let! messages = collectMessages command
                let requested, writeCmd = update deps ignore messages[0] model
                let! completion = collectMessages writeCmd
                let finalState, finishCmd = update deps ignore completion[0] requested
                let! _ = collectMessages finishCmd

                Vitest.expect(calls).toBe (2)
                Vitest.expect(finalState.BusyOperation).toEqual (None)
                Vitest.expect(finalState.ErrorNotice).toEqual (Some "still stale")
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "A precondition failure outside the concurrency category is an error, not a retry",
            fun () -> promise {
                let mutable calls = 0

                let deps = {
                    defaultDependencies with
                        createRevision =
                            fun _ ->
                                calls <- calls + 1
                                promise { return Ok(failed Validation VersionControlCodes.PreconditionFailed "bad") }
                        reportError = fun _ -> ()
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = update deps ignore (CommitAllRequested "save") state
                let! messages = collectMessages command

                let requested, writeCmd =
                    match messages with
                    | [| WriteRequested(CommitAll _) |] -> update deps ignore messages[0] model
                    | _ -> failwith "Expected the commit-all write request."

                let! completion = collectMessages writeCmd

                let finalState, finishCmd =
                    match completion with
                    | [| WriteCompleted(_, _, CommitAll _, Error "bad") |] -> update deps ignore completion[0] requested
                    | _ -> failwith "Expected the validation failure."

                let! _ = collectMessages finishCmd

                Vitest.expect(calls).toBe (1)
                Vitest.expect(finalState.ErrorNotice).toEqual (Some "bad")
            }
        )

        Vitest.test (
            "A stale discard is not repeated and reports the refreshed state",
            fun () -> promise {
                let restoreRequests = ResizeArray<RestorePathsRequestDto>()
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    (withRefresh
                        {
                            cleanStatus with
                                WorkspaceVersion = "v2"
                        }
                        defaultDependencies) with
                        restorePaths =
                            fun request ->
                                restoreRequests.Add request

                                staleTokenReply "workspace version is stale"
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = requestAndConfirmDiscard deps state [| "a.txt" |]

                let! messages = collectMessages command

                let requested, writeCmd =
                    match messages with
                    | [| WriteRequested(DiscardSelection [| "a.txt" |]) |] -> update deps ignore messages[0] model
                    | _ -> failwith "Expected the discard write request."

                let! completion = collectMessages writeCmd
                let requestedWithPending = { requested with RefreshPending = true }

                let finalState, finishCmd, completedMessage =
                    match completion with
                    | [| WriteCompleted(_, _, DiscardSelection _, Ok(StaleWorkspaceVersion(message, _))) |] ->
                        let nextState, command = update deps ignore completion[0] requestedWithPending
                        nextState, command, message
                    | _ -> failwith "Expected the stale discard completion."

                let! finishMessages = collectMessages finishCmd

                let refreshCount =
                    finishMessages
                    |> Array.filter (
                        function
                        | RefreshRequested -> true
                        | _ -> false
                    )
                    |> Array.length

                Vitest.expect(restoreRequests.Count).toBe (1)

                Vitest
                    .expect(completedMessage.StartsWith("The workspace changed since it was last refreshed"))
                    .toBe (true)

                Vitest.expect(finalState.BusyOperation).toEqual (None)
                Vitest.expect(finalState.ErrorNotice).toEqual (Some completedMessage)
                Vitest.expect(finalState.RefreshPending).toBe (false)
                Vitest.expect(reportedErrors.Count).toBe (1)
                Vitest.expect(refreshCount).toBe (1)
            }
        )

        Vitest.test (
            "A stale update is not repeated without a new synchronize decision",
            fun () -> promise {
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    (withRefresh
                        {
                            cleanStatus with
                                WorkspaceVersion = "v2"
                        }
                        defaultDependencies) with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                staleTokenReply "workspace version is stale"
                        reportError = reportedErrors.Add
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let finalState, finishCmd, completedMessage =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(StaleWorkspaceVersion(message, _))) |] ->
                        let nextState, command = update deps ignore completionMessages[0] stateAfterRequest
                        nextState, command, message
                    | _ -> failwith "Expected the stale update completion."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(synchronizeRequests.Count).toBe (1)

                Vitest
                    .expect(completedMessage.StartsWith("The workspace changed since it was last refreshed"))
                    .toBe (true)

                Vitest.expect(finalState.BusyOperation).toEqual (None)
                Vitest.expect(finalState.ErrorNotice).toEqual (Some completedMessage)
                Vitest.expect(reportedErrors.Count).toBe (1)

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | RefreshRequested -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)
            }
        )

        Vitest.test (
            "An accepted pull is not replayed when the target moved",
            fun () -> promise {
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    (withRefresh
                        {
                            cleanStatus with
                                WorkspaceVersion = "v2"
                        }
                        defaultDependencies) with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request

                                staleTokenReply "The target moved after the update was accepted."
                        reportError = reportedErrors.Add
                }

                let stateAfterRequest, requestCmd =
                    update
                        deps
                        ignore
                        (WriteRequested(Pull(GitUpdateAcceptance.Accepted("target-1", "v1"))))
                        runningState

                let! completionMessages = collectMessages requestCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Pull _, Ok(StaleWorkspaceVersion(message, _))) |] ->
                        let nextState, command = update deps ignore completionMessages[0] stateAfterRequest
                        nextState, command
                    | _ -> failwith "Expected the accepted pull to be rejected as stale."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(synchronizeRequests.Count).toBe (1)
                Vitest.expect(finalState.ErrorNotice.IsSome).toBe (true)
                Vitest.expect(reportedErrors.Count).toBe (1)

                let refreshCount =
                    finishMessages
                    |> Array.filter (
                        function
                        | RefreshRequested -> true
                        | _ -> false
                    )
                    |> Array.length

                Vitest.expect(refreshCount).toBe (1)

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | WriteRequested(Pull _) -> true
                            | _ -> false
                        )
                    )
                    .toBe (false)
            }
        )

        Vitest.test (
            "An accepted push is not replayed when the target moved",
            fun () -> promise {
                let synchronizeRequests = ResizeArray<SynchronizeRequestDto>()
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let staleFailure = {
                    makeFailure
                        Concurrency
                        VersionControlCodes.PreconditionFailed
                        "The target moved after the preview."
                        None
                        [||] with
                        RevisionEvidence = [|
                            {
                                Label = "expected_target"
                                Revision = "target-1"
                            }
                            {
                                Label = "observed_target"
                                Revision = "target-2"
                            }
                        |]
                }

                let deps = {
                    (withRefresh
                        {
                            cleanStatus with
                                WorkspaceVersion = "v2"
                        }
                        defaultDependencies) with
                        synchronize =
                            fun request ->
                                synchronizeRequests.Add request
                                promise { return Ok(OperationResultDto.Failed staleFailure) }
                        reportError = reportedErrors.Add
                }

                let stateAfterRequest, requestCmd =
                    update
                        deps
                        ignore
                        (WriteRequested(Push(GitUpdateAcceptance.Accepted("target-1", "v1"))))
                        runningState

                let! completionMessages = collectMessages requestCmd

                let finalState, finishCmd, message =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Push _, Ok(StaleWorkspaceVersion(message, _))) |] ->
                        let state, command = update deps ignore completionMessages[0] stateAfterRequest
                        state, command, message
                    | _ -> failwith "Expected the accepted push to be rejected as stale."

                let! finishMessages = collectMessages finishCmd

                let expectedMessage =
                    "The online copy changed since the preview. Review the current changes and try again."

                Vitest.expect(synchronizeRequests.Count).toBe (1)
                Vitest.expect(message).toBe (expectedMessage)
                Vitest.expect(finalState.ErrorNotice).toEqual (Some expectedMessage)
                Vitest.expect(reportedErrors.Count).toBe (1)

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | RefreshRequested -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)
            }
        )

        Vitest.test (
            "A stale finalize of a merge is not repeated",
            fun () -> promise {
                let mutable calls = 0
                let conflict = (conflictedStatus [| "conflict.txt" |]).ActiveConflictSession.Value

                let deps = {
                    defaultDependencies with
                        finalizeConflict =
                            fun _ ->
                                calls <- calls + 1

                                staleTokenReply "workspace version is stale"
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                }

                let requested, writeCmd = update deps ignore (WriteRequested FinalizeMerge) state

                let! completion = collectMessages writeCmd

                let finalState, finishCmd =
                    match completion with
                    | [| WriteCompleted(_, _, FinalizeMerge, Ok(StaleWorkspaceVersion(_, _))) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the stale finalize completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(calls).toBe (1)
                Vitest.expect(finalState.BusyOperation).toEqual (None)
            }
        )

        Vitest.test (
            "A refresh of the previous ARC is ignored after the path changed",
            fun () -> promise {
                let state = {
                    runningState with
                        CurrentArcPath = Some "C:/arc-a"
                        RefreshRequestId = 1
                }

                let afterPathChange, pathCmd =
                    update defaultDependencies ignore (ArcPathChanged(Some "C:/arc-b")) state

                let! pathMessages = collectMessages pathCmd

                let hasRefresh =
                    pathMessages
                    |> hasMessage (
                        function
                        | RefreshRequested -> true
                        | _ -> false
                    )

                if not hasRefresh then
                    failwith "Expected the path change to request a refresh."

                let refreshing, _ =
                    update defaultDependencies ignore RefreshRequested afterPathChange

                let finalState, _ =
                    update defaultDependencies ignore (RefreshCompleted(1, Error "old ARC failure")) refreshing

                Vitest.expect(finalState.ErrorNotice).toEqual (None)
                Vitest.expect(finalState.CurrentArcPath).toEqual (Some "C:/arc-b")
            }
        )

        Vitest.test (
            "A rename reply that arrives after the refresh finished still publishes",
            fun () -> promise {
                let state = {
                    runningState with
                        CurrentArcPath = Some "C:/renamed-arc"
                        ArcSessionId = runningState.ArcSessionId + 1
                        RefreshState = GitRefreshState.Idle
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (PublishRenameCompleted(runningState.ArcSessionId, Ok "C:/renamed-arc", noOwnerSelection))
                        state

                let! messages = collectMessages cmd

                Vitest.expect(nextState.PendingPublishAfterRefresh).toBe (true)

                Vitest
                    .expect(
                        messages
                        |> hasMessage (
                            function
                            | RefreshRequested -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)
            }
        )

        Vitest.test (
            "A rename reply for the current path under another spelling asks for a refresh",
            fun () -> promise {
                let model = {
                    runningState with
                        CurrentArcPath = Some "C:/arc"
                        BusyOperation = Some GitBusyOperation.RenamingRepository
                }

                let nextState, command =
                    update
                        defaultDependencies
                        ignore
                        (PublishRenameCompleted(model.ArcSessionId - 1, Ok "C:\\arc", noOwnerSelection))
                        model

                let! messages = collectMessages command

                Vitest.expect(nextState.PendingPublishAfterRefresh).toBe (true)
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "A stale rename reply does not clear a running write",
            fun () -> promise {
                let model = {
                    runningState with
                        ArcSessionId = runningState.ArcSessionId + 1
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        BusyNotice = Some "Pushing to remote"
                        CurrentOperation = Some { OperationId = "write-op" }
                }

                let nextState, command =
                    update
                        defaultDependencies
                        ignore
                        (PublishRenameCompleted(model.ArcSessionId - 1, Ok "C:\\arc", noOwnerSelection))
                        model

                let! messages = collectMessages command

                Vitest.expect(nextState.BusyOperation).toEqual (model.BusyOperation)
                Vitest.expect(nextState.BusyNotice).toEqual (model.BusyNotice)
                Vitest.expect(nextState.CurrentOperation).toEqual (model.CurrentOperation)
                Vitest.expect(nextState.PendingPublishAfterRefresh).toBe (true)
                Vitest.expect(messages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "A dependency install becomes the cancel target while it runs",
            fun () -> promise {
                let requests = ResizeArray<InstallDependencyRequestDto>()

                let deps = {
                    defaultDependencies with
                        installDependency =
                            fun request ->
                                requests.Add request

                                promise {
                                    return
                                        Ok(
                                            succeeded {
                                                Component = "git-lfs-configuration"
                                                Installed = true
                                                Version = None
                                                Compatible = true
                                                Remediation = None
                                            }
                                        )
                                }
                }

                let nextState, cmd =
                    update
                        deps
                        ignore
                        (WriteInstallPromptAnswered(
                            runningState.ArcSessionId,
                            Push GitUpdateAcceptance.RequirePreview,
                            "git-lfs-configuration",
                            true
                        ))
                        runningState

                let! _ = collectMessages cmd

                Vitest.expect(requests.Count).toBe (1)

                Vitest
                    .expect(nextState.CurrentOperation |> Option.map _.OperationId)
                    .toEqual (Some requests[0].OperationId)
            }
        )

        Vitest.test (
            "A stale abandon of a merge is not repeated",
            fun () -> promise {
                let mutable calls = 0
                let conflict = (conflictedStatus [| "conflict.txt" |]).ActiveConflictSession.Value

                let deps = {
                    defaultDependencies with
                        cancelConflict =
                            fun _ ->
                                calls <- calls + 1

                                staleTokenReply "workspace version is stale"
                }

                let state = {
                    runningState with
                        ActiveConflict = Some conflict
                }

                let requested, writeCmd = update deps ignore (WriteRequested AbandonMerge) state

                let! completion = collectMessages writeCmd

                let finalState, finishCmd =
                    match completion with
                    | [| WriteCompleted(_, _, AbandonMerge, Ok(StaleWorkspaceVersion(_, _))) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the stale abandon completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(calls).toBe (1)
                Vitest.expect(finalState.BusyOperation).toEqual (None)
            }
        )

        Vitest.test (
            "A stale restore of interrupted paths is not repeated",
            fun () -> promise {
                let affectedPaths = [| "a.txt" |]
                let mutable restoreCalls = 0

                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Update was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RestoreWorkspace
                            Instructions = Some "Restore the interrupted files."
                        })
                        affectedPaths

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                        restorePaths =
                            fun _ ->
                                restoreCalls <- restoreCalls + 1

                                staleTokenReply "workspace version is stale"
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completionMessages = collectMessages requestCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        Pull _,
                                        Ok(RequiresRecovery(GitPendingRecovery.RestoreInterruptedPaths _, _))) |] ->
                        update deps ignore completionMessages[0] stateAfterRequest
                    | _ -> failwith "Expected the restore recovery."

                let! _ = collectMessages recoveryCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                let restoreState, restoreCmd =
                    match confirmMessages with
                    | [| RestoreInterruptedPathsRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected the restore confirmation."

                let! restoreMessages = collectMessages restoreCmd

                let requestedState, writeCmd =
                    match restoreMessages with
                    | [| WriteRequested(RestoreInterruptedPaths [| "a.txt" |]) |] ->
                        update deps ignore restoreMessages[0] restoreState
                    | _ -> failwith "Expected the restore write request."

                let! completion = collectMessages writeCmd

                let finalState, finishCmd =
                    match completion with
                    | [| WriteCompleted(_, _, RestoreInterruptedPaths _, Ok(StaleWorkspaceVersion(_, _))) |] ->
                        update deps ignore completion[0] requestedState
                    | _ -> failwith "Expected the stale restore completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(restoreCalls).toBe (1)
                Vitest.expect(finalState.BusyOperation).toEqual (None)
            }
        )

        Vitest.test (
            "A stale token does not replay a branch switch",
            fun () -> promise {
                let requests = ResizeArray<SwitchRefRequestDto>()
                let mutable preflightCalls = 0

                let deps = {
                    (withRefresh
                        {
                            cleanStatus with
                                WorkspaceVersion = "v2"
                        }
                        defaultDependencies) with
                        preflightSwitchRef =
                            fun _ ->
                                preflightCalls <- preflightCalls + 1
                                promise { return Ok(succeeded { PathsAtRisk = [||]; IsSafe = true }) }
                        switchRef =
                            fun request ->
                                requests.Add request

                                if requests.Count = 1 then
                                    staleTokenReply "workspace version is stale"
                                else
                                    promise { return Ok(succeeded cleanStatus) }
                }

                let state = {
                    runningState with
                        Refs = [| localBranch "feature" false false |]
                }

                let model, command = update deps ignore (SwitchBranchRequested "feature") state
                let! messages = collectMessages command
                let preflightState, writeRequest = update deps ignore messages[0] model
                let! writeMessages = collectMessages writeRequest
                let requested, writeCmd = update deps ignore writeMessages[0] preflightState
                let! completion = collectMessages writeCmd

                let finalState, finishCmd =
                    match completion with
                    | [| WriteCompleted(_, _, SwitchBranch "feature", Ok(StaleWorkspaceVersion(message, false))) |] ->
                        Vitest
                            .expect(message)
                            .toBe (
                                "The workspace changed since it was last refreshed, so the action was not repeated. Review the current changes and try again."
                            )

                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the stale branch switch completion."

                let! _ = collectMessages finishCmd

                Vitest.expect(requests.Count).toBe (1)
                Vitest.expect(preflightCalls).toBe (1)

                Vitest
                    .expect(finalState.ErrorNotice)
                    .toEqual (
                        Some
                            "The workspace changed since it was last refreshed, so the action was not repeated. Review the current changes and try again."
                    )
            }
        )

        Vitest.test (
            "A conflict session reported by a commit opens the first conflicted item",
            fun () -> promise {
                let conflicted = conflictedStatus [| "conflict.txt" |]

                let deps = {
                    (withRefresh conflicted defaultDependencies) with
                        createRevision =
                            fun _ -> promise {
                                return
                                    Ok(
                                        failed
                                            Conflict
                                            VersionControlCodes.ConflictSessionActive
                                            "a conflict session is open"
                                    )
                            }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = update deps ignore (CommitAllRequested "save") state
                let! messages = collectMessages command
                let requested, writeCmd = update deps ignore messages[0] model
                let! completion = collectMessages writeCmd

                let finalState, _ =
                    match completion with
                    | [| WriteCompleted(_, _, CommitAll _, Ok(Completed(UnitSuccess success))) |] ->
                        match success.SelectedChangePath, success.PageChange with
                        | Some(Some "conflict.txt"), GitPageChange.Set _ -> update deps ignore completion[0] requested
                        | _ -> failwith "Expected the conflict page of the first conflicted item."
                    | _ -> failwith "Expected the conflict page of the first conflicted item."

                Vitest.expect(finalState.ActiveConflict.IsSome).toBe (true)
                Vitest.expect(finalState.SelectedChangePath).toEqual (Some "conflict.txt")
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A primary save whose synchronize opens a conflict session keeps the publish for after the merge",
            fun () -> promise {
                let conflicted = conflictedStatus [| "conflict.txt" |]
                let sync = cleanStatus.Synchronization.Value
                let mutable synchronized = false
                let mutable mergeFinalized = false
                let conflict = conflicted.ActiveConflictSession.Value

                let failure =
                    makeFailure
                        Conflict
                        VersionControlCodes.ConflictsDetected
                        "conflicts detected"
                        (Some {
                            Code = VersionControlCodes.Recovery.ResolveConflictSession
                            Instructions = None
                        })
                        [| "conflict.txt" |]

                let deps = {
                    defaultDependencies with
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                        synchronize =
                            fun _ ->
                                synchronized <- true
                                promise { return Ok(OperationResultDto.PartiallySucceeded(operation sync, failure)) }
                        getStatus =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded (
                                            if synchronized && not mergeFinalized then
                                                conflicted
                                            else
                                                statusForBranch "main"
                                        )
                                    )
                            }
                        listRefs = fun _ -> promise { return Ok(succeeded [| localBranch "main" true true |]) }
                        getStoragePolicySettings = fun _ -> promise { return Ok(succeeded (lfsSettings 5 true)) }
                        loadConflictPage = fun _ _ path -> promise { return Ok(conflictPage path) }
                        resolveConflict =
                            fun _ -> promise {
                                return
                                    Ok(
                                        succeeded {
                                            RefreshedHandle = {
                                                SessionId = "conflict-2"
                                                Version = "2"
                                            }
                                            RemainingItems = [||]
                                        }
                                    )
                            }
                        finalizeConflict =
                            fun _ ->
                                mergeFinalized <- true
                                promise { return Ok(succeeded (Some "rev")) }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = update deps ignore (PrimarySaveAllRequested "save") state
                let! messages = collectMessages command
                let requested, writeCmd = update deps ignore messages[0] model
                let! completion = collectWriteMessages writeCmd

                let finalState, finishCmd =
                    match completion with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed(UnitSuccess success))) |] ->
                        match success.PageChange with
                        | GitPageChange.Set _ -> update deps ignore completion[0] requested
                        | _ -> failwith "Expected the conflict page after the update inside the save."
                    | _ -> failwith "Expected the conflict page after the update inside the save."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(finalState.ActiveConflict.IsSome).toBe (true)
                Vitest.expect(finalState.PendingPostMergePush).toBe (true)

                Vitest
                    .expect(finalState.WarningNotice)
                    .toEqual (Some "Changes were saved locally. Online sync is still pending.")

                Vitest.expect(finishMessages).toEqual ([||])

                let mergeRequest = {
                    Path = "conflict.txt"
                    Handle = conflict.Handle
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.SupplyResolvedContent "resolved"
                }

                let mergeState, mergeCmd =
                    update deps ignore (ConfirmMergeResolutionRequested mergeRequest) finalState

                let! mergeCompletions = collectMessages mergeCmd

                let finalizedState, finalizeCmd =
                    match mergeCompletions with
                    | [| ConfirmMergeResolutionCompleted(_, Ok outcome) |] ->
                        Vitest.expect(outcome.Finalized).toBe (true)
                        update deps ignore mergeCompletions[0] mergeState
                    | _ -> failwith "Expected merge resolution to complete."

                let! finalMessages = collectMessages finalizeCmd
                Vitest.expect(finalizedState.PendingPostMergePush).toBe (false)

                Vitest
                    .expect(finalMessages)
                    .toEqual (
                        [|
                            WriteRequested(Push GitUpdateAcceptance.RequirePreview)
                        |]
                    )
            }
        )

        Vitest.test (
            "Cancel before the started event uses the id allocated at request time",
            fun () -> promise {
                let publishIds = ResizeArray<string>()
                let cancelKeys = ResizeArray<OperationRequestDto>()
                let mutable ids = 0

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        newOperationId =
                            fun () ->
                                ids <- ids + 1
                                $"op-{ids}"
                        synchronize =
                            fun request ->
                                publishIds.Add request.OperationId
                                promise { return Ok(succeeded cleanStatus.Synchronization.Value) }
                        cancelOperation =
                            fun key ->
                                cancelKeys.Add key
                                promise { return Ok true }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let canceling, cancelCmd =
                    update deps ignore CancelCurrentOperationRequested requested

                let! cancelMessages = collectMessages cancelCmd
                let! _ = collectMessages writeCmd

                Vitest.expect(requested.CurrentOperation).toEqual (Some { OperationId = "op-1" })
                Vitest.expect(canceling.WarningNotice).toEqual (None)
                Vitest.expect(cancelKeys |> Seq.toArray).toEqual ([| { OperationId = "op-1" } |])
                Vitest.expect(publishIds |> Seq.toArray).toEqual ([| "op-1" |])

                Vitest
                    .expect(cancelMessages)
                    .toEqual (
                        [|
                            CancelCurrentOperationCompleted(1, { OperationId = "op-1" }, Ok true)
                        |]
                    )
            }
        )

        Vitest.test (
            "A refused cancel leaves the current notices unchanged",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let state = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                        CurrentOperation = Some { OperationId = "op-1" }
                        WarningNotice = Some "existing warning"
                        ErrorNotice = Some "existing error"
                }

                let nextState, cmd =
                    update
                        deps
                        ignore
                        (CancelCurrentOperationCompleted(state.ArcSessionId, state.CurrentOperation.Value, Ok false))
                        state

                let! _ = collectMessages cmd

                Vitest.expect(nextState.WarningNotice).toEqual (Some "existing warning")
                Vitest.expect(nextState.ErrorNotice).toEqual (Some "existing error")
                Vitest.expect(reportedErrors.Count).toBe (0)
            }
        )

        Vitest.test (
            "A late cancel reply from another operation is ignored",
            fun () -> promise {
                let currentKey = { OperationId = "op-1/1" }

                let state = {
                    runningState with
                        CurrentOperation = Some currentKey
                        ErrorNotice = Some "current error"
                        WarningNotice = Some "current warning"
                }

                let nextState, cmd =
                    update
                        defaultDependencies
                        ignore
                        (CancelCurrentOperationCompleted(
                            state.ArcSessionId,
                            { OperationId = "op-2/1" },
                            Error "late failure"
                        ))
                        state

                let! messages = collectMessages cmd

                Vitest.expect(nextState).toEqual (state)
                Vitest.expect(messages).toEqual ([||])
            }
        )

        Vitest.test (
            "A cancel error reply survives the started event",
            fun () -> promise {
                let reportedErrors = ResizeArray<GitErrorNotification>()

                let deps = {
                    defaultDependencies with
                        reportError = reportedErrors.Add
                }

                let model = {
                    runningState with
                        CurrentOperation = Some { OperationId = "op-1/1" }
                }

                let nextState, command =
                    update
                        deps
                        ignore
                        (CancelCurrentOperationCompleted(model.ArcSessionId, { OperationId = "op-1" }, Error "boom"))
                        model

                let! _ = collectMessages command

                Vitest.expect(nextState.ErrorNotice).toEqual (Some "boom")
                Vitest.expect(reportedErrors.Count).toBe (1)
            }
        )

        Vitest.test (
            "Primary save without an online service stops after the local commit",
            fun () -> promise {
                let localOnly = {
                    sessionInfo with
                        Services = {
                            sessionInfo.Services with
                                Synchronization = false
                        }
                }

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        getSessionInfo = fun _ -> promise { return Ok(succeeded localOnly) }
                        createRevision = fun _ -> promise { return Ok(succeeded "revision-1") }
                }

                let state = {
                    runningState with
                        ChangedFiles = [| changedFile "a.txt" "M" " " false |]
                }

                let model, command = update deps ignore (PrimarySaveAllRequested "save") state
                let! messages = collectMessages command
                let requested, writeCmd = update deps ignore messages[0] model
                let! completion = collectMessages writeCmd

                let finalState, _ =
                    match completion with
                    | [| WriteCompleted(_, _, PrimarySave _, Ok(Completed _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the local save to complete."

                Vitest.expect(finalState.ErrorNotice).toEqual (None)

                Vitest
                    .expect(finalState.WarningNotice)
                    .toEqual (
                        Some
                            "Changes were saved locally. This workspace provider does not support online synchronization."
                    )
            }
        )

        Vitest.test (
            "A synchronize whose bind fails resumes without creating a second project",
            fun () -> promise {
                let sync = cleanStatus.Synchronization.Value
                let mutable projectCalls = 0
                let mutable bindCalls = 0
                let mutable publishCalls = 0

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        synchronize =
                            fun _ ->
                                publishCalls <- publishCalls + 1

                                if publishCalls = 1 then
                                    promise {
                                        return
                                            Ok(
                                                failed
                                                    Validation
                                                    VersionControlCodes.PublishTargetMissing
                                                    "publish target missing"
                                            )
                                    }
                                else
                                    promise { return Ok(succeeded sync) }
                        createRemoteProject =
                            fun _ ->
                                projectCalls <- projectCalls + 1
                                promise { return Ok remoteProject }
                        bindWorkspace =
                            fun _ ->
                                bindCalls <- bindCalls + 1

                                if bindCalls = 1 then
                                    promise { return Ok(failed ProviderError "bind_failed" "bind failed") }
                                else
                                    promise { return Ok(succeeded sessionInfo) }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) runningState

                let! completion = collectMessages writeCmd

                let incomplete, incompleteCmd =
                    match completion with
                    | [| WriteCompleted(_, _, Push _, Ok(ProvisioningIncomplete _)) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the failed bind to leave the provisioning incomplete."

                let! incompleteMessages = collectMessages incompleteCmd

                let refreshing, refreshCmd =
                    match incompleteMessages with
                    | [| RefreshRequested |] -> update deps ignore RefreshRequested incomplete
                    | _ -> failwith "Expected a refresh after the incomplete provisioning."

                let! refreshMessages = collectMessages refreshCmd
                let refreshed, _ = update deps ignore refreshMessages[0] refreshing

                let retried, retryCmd =
                    update deps ignore (WriteRequested(Push GitUpdateAcceptance.RequirePreview)) refreshed

                let! retryCompletion = collectMessages retryCmd

                let finalState, _ =
                    match retryCompletion with
                    | [| WriteCompleted(_, _, Push _, Ok(Completed _)) |] ->
                        update deps ignore retryCompletion[0] retried
                    | _ -> failwith "Expected the retried publish to complete."

                Vitest
                    .expect(incomplete.ProvisionedRemote)
                    .toEqual (
                        Some {
                            RemoteUrl = remoteProject.http_url_to_repo
                            ProjectName = "arc"
                            IsBound = false
                        }
                    )

                Vitest.expect(incomplete.ErrorNotice).toEqual (Some "bind failed")
                Vitest.expect(refreshed.WarningNotice).toEqual (Some "bind failed")
                Vitest.expect(projectCalls).toBe (1)
                Vitest.expect(bindCalls).toBe (2)
                Vitest.expect(publishCalls).toBe (2)
                Vitest.expect(finalState.ProvisionedRemote).toEqual (None)
                Vitest.expect(finalState.ErrorNotice).toEqual (None)
            }
        )

        Vitest.test (
            "A recovery offer does not start a refresh, dismissing it does",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "Update was canceled."
                        (Some {
                            Code = VersionControlCodes.Recovery.RestoreWorkspace
                            Instructions = None
                        })
                        [| "a.txt" |]

                let deps = {
                    defaultDependencies with
                        synchronize = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                }

                let requested, writeCmd =
                    update deps ignore (WriteRequested(Pull GitUpdateAcceptance.RequirePreview)) runningState

                let! completion = collectMessages writeCmd

                let offered, offerCmd =
                    match completion with
                    | [| WriteCompleted(_,
                                        _,
                                        Pull _,
                                        Ok(RequiresRecovery(GitPendingRecovery.RestoreInterruptedPaths _, _))) |] ->
                        update deps ignore completion[0] requested
                    | _ -> failwith "Expected the restore offer."

                let! offerMessages = collectMessages offerCmd

                let canceling, cancelCmd =
                    update deps ignore CancelPendingRemoteActionRequested offered

                let! cancelMessages = collectMessages cancelCmd

                let dismissed, dismissCmd =
                    match cancelMessages with
                    | [| DismissRecoveryRequested |] -> update deps ignore DismissRecoveryRequested canceling
                    | _ -> failwith "Expected the dismissal."

                let! dismissMessages = collectMessages dismissCmd

                Vitest.expect(offered.BusyOperation).toEqual (None)
                Vitest.expect(offered.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(offerMessages).toEqual ([||])
                Vitest.expect(dismissed.PendingRecovery).toEqual (None)
                Vitest.expect(dismissMessages).toEqual ([| RefreshRequested |])
            }
        )

        Vitest.test (
            "A refresh requested during a write does not swallow a confirmed recovery",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "restore"
                        (Some {
                            Code = VersionControlCodes.Recovery.RestoreWorkspace
                            Instructions = None
                        })
                        [| "a.txt" |]

                let mutable restoredPaths = None

                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        refreshSynchronization = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                        restorePaths =
                            fun request ->
                                restoredPaths <- Some request.Paths
                                promise { return Ok(succeeded ()) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested Fetch) runningState

                let pendingState, pendingCmd = update deps ignore RefreshRequested stateAfterRequest
                let! pendingMessages = collectMessages pendingCmd
                let! completionMessages = collectMessages requestCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        Fetch,
                                        Ok(RequiresRecovery(GitPendingRecovery.RestoreInterruptedPaths {
                                                                                                           AffectedPaths = [| "a.txt" |]
                                                                                                           Instructions = None
                                                                                                       },
                                                            "restore"))) |] ->
                        update deps ignore completionMessages[0] pendingState
                    | _ -> failwith "Expected restore recovery after the canceled fetch."

                let! recoveryMessages = collectMessages recoveryCmd

                let confirmedState, confirmCmd =
                    update deps ignore ConfirmPendingRemoteActionRequested recoveryState

                let! confirmMessages = collectMessages confirmCmd

                let intermediateState, intermediateCmd =
                    match confirmMessages with
                    | [| RestoreInterruptedPathsRequested |] -> update deps ignore confirmMessages[0] confirmedState
                    | _ -> failwith "Expected the confirmed recovery to dispatch the restore request."

                let! intermediateMessages = collectMessages intermediateCmd

                let restoreRequestedState, restoreWriteCmd =
                    match intermediateMessages with
                    | [| WriteRequested(RestoreInterruptedPaths [| "a.txt" |]); RefreshRequested |] ->
                        update deps ignore intermediateMessages[0] intermediateState
                    | _ -> failwith "Expected the restore write and its queued refresh."

                let heldState, heldRefreshCmd =
                    update deps ignore intermediateMessages[1] restoreRequestedState

                let! heldRefreshMessages = collectMessages heldRefreshCmd
                let! _ = collectMessages restoreWriteCmd

                Vitest.expect(pendingMessages).toEqual ([||])
                Vitest.expect(recoveryMessages).toEqual ([||])
                Vitest.expect(recoveryState.RefreshPending).toBe (true)
                Vitest.expect(recoveryState.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(heldRefreshMessages).toEqual ([||])
                Vitest.expect(heldState.BusyOperation).toEqual (Some GitBusyOperation.RestoringInterruptedPaths)
                Vitest.expect(heldState.RefreshPending).toBe (true)
                Vitest.expect(restoredPaths).toEqual (Some [| "a.txt" |])
            }
        )

        Vitest.test (
            "Dismissing a recovery offer with a pending refresh refreshes once",
            fun () -> promise {
                let canceled =
                    makeFailure
                        Canceled
                        VersionControlCodes.OperationCanceled
                        "restore"
                        (Some {
                            Code = VersionControlCodes.Recovery.RestoreWorkspace
                            Instructions = None
                        })
                        [| "a.txt" |]

                let deps = {
                    defaultDependencies with
                        refreshSynchronization = fun _ -> promise { return Ok(OperationResultDto.Failed canceled) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested Fetch) runningState

                let pendingState, pendingCmd = update deps ignore RefreshRequested stateAfterRequest
                let! pendingMessages = collectMessages pendingCmd
                let! completionMessages = collectMessages requestCmd

                let recoveryState, recoveryCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        Fetch,
                                        Ok(RequiresRecovery(GitPendingRecovery.RestoreInterruptedPaths _, "restore"))) |] ->
                        update deps ignore completionMessages[0] pendingState
                    | _ -> failwith "Expected restore recovery after the canceled fetch."

                let! recoveryMessages = collectMessages recoveryCmd

                let cancelingState, cancelCmd =
                    update deps ignore CancelPendingRemoteActionRequested recoveryState

                let! cancelMessages = collectMessages cancelCmd

                let dismissedState, dismissCmd =
                    match cancelMessages with
                    | [| DismissRecoveryRequested |] -> update deps ignore cancelMessages[0] cancelingState
                    | _ -> failwith "Expected dismissal to dispatch DismissRecoveryRequested."

                let! dismissMessages = collectMessages dismissCmd

                let refreshCount =
                    Array.append cancelMessages dismissMessages
                    |> Array.filter (
                        function
                        | RefreshRequested -> true
                        | _ -> false
                    )
                    |> Array.length

                Vitest.expect(pendingMessages).toEqual ([||])
                Vitest.expect(recoveryMessages).toEqual ([||])
                Vitest.expect(dismissedState.RefreshPending).toBe (false)
                Vitest.expect(refreshCount).toBe (1)
            }
        )

        Vitest.test (
            "A silent refresh requested during a write stays pending",
            fun () -> promise {
                let writingState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.PushingToRemote
                }

                let pendingState, pendingCmd =
                    update defaultDependencies ignore RefreshRequestedSilently writingState

                let! pendingMessages = collectMessages pendingCmd

                Vitest.expect(pendingState.RefreshPending).toBe (true)
                Vitest.expect(pendingState.RefreshPendingSilently).toBe (true)
                Vitest.expect(pendingMessages).toEqual ([||])
            }
        )

        Vitest.test (
            "A refresh requested during the save's refresh phase waits for the write",
            fun () -> promise {
                let writeKey = { OperationId = "save-op" }

                let savingState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.Refreshing
                        BusyNotice = Some "Refreshing"
                        CurrentOperation = Some writeKey
                }

                let pendingState, pendingCmd =
                    update defaultDependencies ignore RefreshRequested savingState

                let! pendingMessages = collectMessages pendingCmd

                Vitest.expect(pendingState.RefreshPending).toBe (true)
                Vitest.expect(pendingMessages).toEqual ([||])

                let deps = withRefresh cleanStatus defaultDependencies

                let standaloneState = {
                    runningState with
                        BusyOperation = Some GitBusyOperation.Refreshing
                        BusyNotice = Some "Refreshing"
                        CurrentOperation = None
                }

                let _, standaloneCmd = update deps ignore RefreshRequested standaloneState
                let! standaloneMessages = collectMessages standaloneCmd

                match standaloneMessages with
                | [| RefreshCompleted(_, Ok _) |] -> ()
                | _ -> failwith "Expected a standalone refresh to produce RefreshCompleted."
            }
        )

        Vitest.test (
            "A refresh requested during a write runs when the write completes",
            fun () -> promise {
                let deps = {
                    (withRefresh cleanStatus defaultDependencies) with
                        refreshSynchronization =
                            fun _ -> promise { return Ok(succeeded cleanStatus.Synchronization.Value) }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested Fetch) runningState

                let pendingState, pendingCmd = update deps ignore RefreshRequested stateAfterRequest
                let! pendingMessages = collectMessages pendingCmd
                let! completionMessages = collectMessages requestCmd

                let finalState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_, _, Fetch, Ok(Completed _)) |] ->
                        update deps ignore completionMessages[0] pendingState
                    | _ -> failwith "Expected the fetch to complete successfully."

                let! finishMessages = collectMessages finishCmd

                Vitest.expect(pendingMessages).toEqual ([||])
                Vitest.expect(pendingState.RefreshPending).toBe (true)
                Vitest.expect(finalState.RefreshPending).toBe (false)

                Vitest
                    .expect(
                        finishMessages
                        |> hasMessage (
                            function
                            | RefreshRequested -> true
                            | _ -> false
                        )
                    )
                    .toBe (true)
            }
        )

        Vitest.test (
            "A refresh requested during a write waits until the recovery offer is resolved",
            fun () -> promise {
                let deps = {
                    defaultDependencies with
                        refreshSynchronization =
                            fun _ -> promise {
                                return
                                    Ok(
                                        OperationResultDto.Failed(
                                            makeFailure
                                                Canceled
                                                VersionControlCodes.OperationCanceled
                                                "lock"
                                                (Some {
                                                    Code = VersionControlCodes.Recovery.RemoveIndexLock
                                                    Instructions = None
                                                })
                                                [||]
                                        )
                                    )
                            }
                }

                let stateAfterRequest, requestCmd =
                    update deps ignore (WriteRequested Fetch) runningState

                let pendingState, pendingCmd = update deps ignore RefreshRequested stateAfterRequest
                let! pendingMessages = collectMessages pendingCmd
                let! completionMessages = collectMessages requestCmd

                let recoveryState, finishCmd =
                    match completionMessages with
                    | [| WriteCompleted(_,
                                        _,
                                        Fetch,
                                        Ok(RequiresRecovery(GitPendingRecovery.ClearStaleLock None, "lock"))) |] ->
                        update deps ignore completionMessages[0] pendingState
                    | _ -> failwith "Expected the fetch to offer lock recovery."

                let! finishMessages = collectMessages finishCmd

                let cancelingState, cancelCmd =
                    update deps ignore CancelPendingRemoteActionRequested recoveryState

                let! cancelMessages = collectMessages cancelCmd

                let dismissedState, dismissCmd =
                    match cancelMessages with
                    | [| DismissRecoveryRequested |] -> update deps ignore cancelMessages[0] cancelingState
                    | _ -> failwith "Expected recovery dismissal."

                let! dismissMessages = collectMessages dismissCmd

                let refreshCount =
                    Array.append cancelMessages dismissMessages
                    |> Array.filter (
                        function
                        | RefreshRequested -> true
                        | _ -> false
                    )
                    |> Array.length

                Vitest.expect(pendingMessages).toEqual ([||])
                Vitest.expect(pendingState.RefreshPending).toBe (true)
                Vitest.expect(finishMessages).toEqual ([||])
                Vitest.expect(recoveryState.RefreshPending).toBe (true)
                Vitest.expect(recoveryState.PendingConfirmation.IsSome).toBe (true)
                Vitest.expect(dismissedState.RefreshPending).toBe (false)

                Vitest.expect(refreshCount).toBe (1)
            }
        )
)

Vitest.describe (
    "GitWorkflow renderer behavior",
    fun () ->
        let _ =
            Vitest.test (
                "applyStatus keeps the selected path clear when the viewed file disappears from changed files",
                fun () ->
                    let model = {
                        runningState with
                            SelectedChangePath = Some "x"
                            ChangedFiles = [| changedFile "x" "M" " " false |]
                    }

                    Vitest.expect((applyStatus cleanStatus model).SelectedChangePath).toEqual (None)
            )

        let _ =
            Vitest.test (
                "applyStatus clears pending materializations when no conflict session remains",
                fun () ->
                    let model = {
                        runningState with
                            PendingMaterializations = [ ("pending.bin", false) ]
                            PendingMaterializationSessionId = Some "conflict-1"
                    }

                    let nextState = applyStatus cleanStatus model

                    Vitest.expect(nextState.PendingMaterializations).toEqual []
                    Vitest.expect(nextState.PendingMaterializationSessionId).toEqual None
            )

        let _ =
            Vitest.test (
                "applyStatus clears pending materializations when the conflict session changes",
                fun () ->
                    let model = {
                        runningState with
                            PendingMaterializations = [ ("pending.bin", false) ]
                            PendingMaterializationSessionId = Some "conflict-1"
                    }

                    let matchingState = applyStatus (conflictedStatus [||]) model
                    Vitest.expect(matchingState.PendingMaterializations).toEqual [ ("pending.bin", false) ]
                    Vitest.expect(matchingState.PendingMaterializationSessionId).toEqual (Some "conflict-1")

                    let status = conflictedStatus [||]
                    let conflict = status.ActiveConflictSession.Value

                    let differentSessionStatus = {
                        status with
                            ActiveConflictSession =
                                Some {
                                    conflict with
                                        Handle = {
                                            conflict.Handle with
                                                SessionId = "conflict-2"
                                        }
                                }
                    }

                    let nextState = applyStatus differentSessionStatus matchingState

                    Vitest.expect(nextState.PendingMaterializations).toEqual []
                    Vitest.expect(nextState.PendingMaterializationSessionId).toEqual None
            )

        let _ =
            Vitest.test (
                "GitState.Empty defaults DownloadLargeFiles to false until settings load",
                fun () -> Vitest.expect(GitState.Empty.DownloadLargeFiles).toBe (false)
            )

        Vitest.test (
            "ConfirmMergeResolutionRequested ignores a second request while another conflict is still confirming",
            fun () -> promise {
                let request = {
                    Path = "conflict.txt"
                    Handle = {
                        SessionId = "conflict-1"
                        Version = "1"
                    }
                    WorkspaceVersion = "v1"
                    Resolution = ConflictResolutionDto.SupplyResolvedContent "resolved"
                }

                let state = {
                    runningState with
                        BusyOperation = Some(GitBusyOperation.ConfirmingMergeResolution "other.txt")
                        MergeResolutionPendingPath = Some "other.txt"
                }

                let nextState, cmd =
                    update defaultDependencies ignore (ConfirmMergeResolutionRequested request) state

                let! messages = collectMessages cmd

                Vitest.expect(nextState).toEqual (state)
                Vitest.expect(messages).toEqual ([||])
            }
        )
)

let private renderSaveSidebar (callbacks: GitSidebarCallbacks) =
    renderToBody (
        Swate.Components.Page.GitSidebar.Main(
            status = {
                CurrentBranch = Some "main"
                TrackingBranch = Some "origin/main"
                Ahead = 0
                Behind = 0
                IsClean = false
                IsMergeInProgress = false
            },
            changedFiles = manyChangedFiles 3,
            branchOptions = [| sidebarLocalBranch "main" true true |],
            callbacks = callbacks,
            downloadLargeFiles = true,
            lfsAutoTrackThresholdMb = 5,
            diffIndexingLimitMb = 1024
        )
    )

Vitest.describe (
    "GitWorkflow component behavior",
    fun () ->
        Vitest.test (
            "GitMergeConflictViewer disables Confirm Merge while confirmation is blocked",
            fun () -> promise {
                let mutable confirmCalls = 0

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitMergeConflictViewer.Viewer(
                            mergeConflictContent = "<<<<<<< HEAD\nA\n=======\nB\n>>>>>>> branch\n",
                            defaultResolvedContent = "resolved",
                            onConfirmMerge = (fun _ -> confirmCalls <- confirmCalls + 1),
                            confirmDisabled = true,
                            testIdPrefix = "renderer-workflow-test"
                        )
                    )

                let confirmButton =
                    container.querySelector ("[data-testid='renderer-workflow-test-confirm-merge']")
                    :?> HTMLButtonElement

                Vitest.expect(confirmButton.disabled).toBe (true)
                confirmButton.click ()
                Vitest.expect(confirmCalls).toBe (0)

                cleanup ()
            }
        )

        Vitest.test (
            "GitMergeConflictViewer virtualizes long conflict blocks instead of mounting every rendered row",
            fun () ->
                let currentLines = [|
                    for index in 0..239 -> $"Current renderer conflict line {index + 1}"
                |]

                let incomingLines = [|
                    for index in 0..239 -> $"Incoming renderer conflict line {index + 1}"
                |]

                let markup =
                    renderToStaticMarkup (
                        Html.div [
                            prop.style [ style.width 960; style.height 520 ]
                            prop.children [
                                Swate.Components.Page.GitMergeConflictViewer.Viewer(
                                    mergeConflictContent = buildSingleConflictDocument currentLines incomingLines,
                                    testIdPrefix = "renderer-large-merge"
                                )
                            ]
                        ]
                    )

                Vitest
                    .expect(markup.Contains("data-testid=\"renderer-large-merge-conflict-1-scroll-virtual-content\""))
                    .toBe (true)

                Vitest
                    .expect(markup.Contains("data-testid=\"renderer-large-merge-conflict-1-scroll-row-0\""))
                    .toBe (true)

                Vitest
                    .expect(markup.Contains("data-testid=\"renderer-large-merge-conflict-1-scroll-row-239\""))
                    .toBe (false)
        )

        Vitest.test (
            "GitSidebar shows when the current local branch has no upstream yet",
            fun () -> promise {
                let status: GitSidebarStatus = {
                    CurrentBranch = Some "feature/local-only"
                    TrackingBranch = None
                    Ahead = 0
                    Behind = 0
                    IsClean = true
                    IsMergeInProgress = false
                }

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = status,
                            changedFiles = [||],
                            branchOptions = [| sidebarLocalBranch "feature/local-only" true false |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                Vitest
                    .expect(
                        container.textContent.Contains(
                            "No upstream configured yet. Push will publish and track origin/feature/local-only."
                        )
                    )
                    .toBe (true)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebarEmptyState renders the bootstrap call-to-action",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Renderer.Components.LeftSidebar.Git.GitSidebarEmptyState.Main(
                            title = "Initialize Git for this ARC",
                            description = "The selected ARC folder is not a Git repository yet.",
                            primaryAction = {
                                Label = "Initialize Repository"
                                IconClassName = "swt:fluent--branch-fork-24-regular"
                                Disabled = false
                                OnClick = ignore
                            },
                            ?infoText = Some "Remote actions remain disabled until you sign in."
                        )
                    )

                Vitest.expect(container.textContent.Contains("Initialize Git for this ARC")).toBe (true)
                Vitest.expect(container.textContent.Contains("Initialize Repository")).toBe (true)
                Vitest.expect(container.textContent.Contains("Remote actions remain disabled")).toBe (true)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar disables remote actions and shows the requested warning when auth is unavailable",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = true
                                IsMergeInProgress = false
                            },
                            changedFiles = [||],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024,
                            remoteActionsEnabled = false,
                            remoteActionsWarning = "Sign in to a DataHUB account to use fetch, pull, push, or update."
                        )
                    )

                let updateButton =
                    container.querySelector ("[data-testid='GitSidebarPullButton']") :?> HTMLButtonElement

                Vitest.expect(updateButton.disabled).toBe (true)
                Vitest.expect(container.textContent.Contains("Sign in to a DataHUB account")).toBe (true)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar accepts unit callbacks for observable actions while keeping async file selection",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = true
                                IsMergeInProgress = false
                            },
                            changedFiles = [||],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                Vitest.expect(container.querySelector ("[data-testid='GitSidebar']")).not.toBeNull ()
                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar renders conflicted change rows alongside other changed files",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = true
                            },
                            changedFiles = [|
                                changedFile "README.md" "M" " " false
                                changedFile "assays/isa.assay.xlsx" "?" "?" false
                                changedFile "notes/protocol.md" "R" "M" false
                                changedFile "studies/s-study-01/protocol.md" "U" "U" true
                            |],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                Vitest
                    .expect(container.querySelector("[data-testid='GitSidebarChangeRow-3']").getAttribute ("title"))
                    .toBe ("studies/s-study-01/protocol.md")

                Vitest.expect(container.querySelector ("[data-testid='GitSidebarChangeStatusIcon-3']")).not.toBeNull ()

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar renders small changed-file sets through the virtualized list path",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Html.div [
                            prop.style [ style.width 340; style.height 760 ]
                            prop.children [
                                Swate.Components.Page.GitSidebar.Main(
                                    status = {
                                        CurrentBranch = Some "main"
                                        TrackingBranch = Some "origin/main"
                                        Ahead = 0
                                        Behind = 0
                                        IsClean = false
                                        IsMergeInProgress = false
                                    },
                                    changedFiles = manyChangedFiles 3,
                                    branchOptions = [| sidebarLocalBranch "main" true true |],
                                    callbacks = {
                                        OnRefresh = fun () -> ()
                                        OnFetch = fun () -> ()
                                        OnPush = fun () -> ()
                                        OnUpdateFromOnline = fun () -> ()
                                        OnPrimarySaveSelection = fun _ -> ()
                                        OnPrimarySaveAll = fun _ -> ()
                                        OnCommitSelection = fun _ -> ()
                                        OnCommitAll = fun _ -> ()
                                        OnDiscardSelection = fun _ -> ()
                                        OnConfirmPendingRemoteAction = fun () -> ()
                                        OnCancelPendingRemoteAction = fun () -> ()
                                        OnSaveDownloadLargeFiles = fun _ -> ()
                                        OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                        OnSaveDiffIndexingLimit = fun _ -> ()
                                        OnCreateBranch = fun _ -> ()
                                        OnSwitchBranch = fun _ -> ()
                                        OnSelectChange = fun _ -> promise { return Ok() }
                                        OnPruneLfsCache = fun () -> ()
                                        OnDedupLfsStorage = fun () -> ()
                                        OnCancelOperation = fun () -> ()
                                    },
                                    downloadLargeFiles = true,
                                    lfsAutoTrackThresholdMb = 5,
                                    diffIndexingLimitMb = 1024
                                )
                            ]
                        ]
                    )

                Vitest
                    .expect(container.querySelector ("[data-testid='GitSidebarChangedFilesScrollContainer']"))
                    .not.toBeNull ()

                Vitest
                    .expect(container.querySelector ("[data-testid='GitSidebarChangedFilesVirtualContent']"))
                    .not.toBeNull ()

                Vitest.expect(container.querySelectorAll("[data-testid^='GitSidebarChangeRow-']").length).toBe (3)

                Vitest
                    .expect(
                        container
                            .querySelector("[data-testid='GitSidebarChangedFilesScrollContainer']")
                            .getAttribute ("role")
                    )
                    .toBe ("region")

                Vitest
                    .expect(
                        container
                            .querySelector("[data-testid='GitSidebarChangedFilesScrollContainer']")
                            .getAttribute ("aria-label")
                    )
                    .toBe ("Changed files")

                Vitest
                    .expect(
                        container
                            .querySelector("[data-testid='GitSidebarChangedFilesVirtualContent']")
                            .getAttribute ("role")
                    )
                    .toBe ("list")

                Vitest.expect(container.querySelectorAll("[role='listitem']").length).toBe (3)

                Vitest
                    .expect(container.querySelector ("[role='listitem'] [data-testid='GitSidebarChangeRow-0']"))
                    .not.toBeNull ()

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar virtualizes long changed-file lists instead of mounting every item at once",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Html.div [
                            prop.style [ style.width 340; style.height 760 ]
                            prop.children [
                                Swate.Components.Page.GitSidebar.Main(
                                    status = {
                                        CurrentBranch = Some "main"
                                        TrackingBranch = Some "origin/main"
                                        Ahead = 0
                                        Behind = 0
                                        IsClean = false
                                        IsMergeInProgress = false
                                    },
                                    changedFiles = manyChangedFiles 400,
                                    branchOptions = [| sidebarLocalBranch "main" true true |],
                                    callbacks = {
                                        OnRefresh = fun () -> ()
                                        OnFetch = fun () -> ()
                                        OnPush = fun () -> ()
                                        OnUpdateFromOnline = fun () -> ()
                                        OnPrimarySaveSelection = fun _ -> ()
                                        OnPrimarySaveAll = fun _ -> ()
                                        OnCommitSelection = fun _ -> ()
                                        OnCommitAll = fun _ -> ()
                                        OnDiscardSelection = fun _ -> ()
                                        OnConfirmPendingRemoteAction = fun () -> ()
                                        OnCancelPendingRemoteAction = fun () -> ()
                                        OnSaveDownloadLargeFiles = fun _ -> ()
                                        OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                        OnSaveDiffIndexingLimit = fun _ -> ()
                                        OnCreateBranch = fun _ -> ()
                                        OnSwitchBranch = fun _ -> ()
                                        OnSelectChange = fun _ -> promise { return Ok() }
                                        OnPruneLfsCache = fun () -> ()
                                        OnDedupLfsStorage = fun () -> ()
                                        OnCancelOperation = fun () -> ()
                                    },
                                    downloadLargeFiles = true,
                                    lfsAutoTrackThresholdMb = 5,
                                    diffIndexingLimitMb = 1024
                                )
                            ]
                        ]
                    )

                Vitest.expect(container.textContent.Contains("400 files")).toBe (true)

                Vitest
                    .expect(container.querySelector ("[data-testid='GitSidebarChangedFilesVirtualContent']"))
                    .not.toBeNull ()

                Vitest.expect(container.querySelector ("[data-testid='GitSidebarChangeRow-0']")).not.toBeNull ()
                Vitest.expect(container.querySelector ("[data-testid='GitSidebarChangeRow-399']")).toBeNull ()

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar virtualizes changed files when nested inside an outer scrollable wrapper",
            fun () -> promise {
                // Reproduce the production layout: fixed-height sidebar panel -> outer scroll wrapper
                // -> Git host wrapper -> GitSidebar. The virtualizer only works when the host wrapper
                // gives GitSidebar a bounded height, so this test keeps the working height contract visible.
                let boundedWrapperClasses = [
                    "swt:box-border"
                    "swt:flex"
                    "swt:h-full"
                    "swt:min-h-0"
                    "swt:min-w-0"
                    "swt:max-w-full"
                    "swt:flex-col"
                    "swt:overflow-hidden"
                    "swt:p-4"
                ]

                let! container, cleanup =
                    renderToBody (
                        Html.div [
                            prop.style [ style.width 340; style.height 760; style.overflow.hidden ]
                            prop.children [
                                Html.div [
                                    prop.style [
                                        style.width (length.percent 100)
                                        style.height (length.percent 100)
                                        style.overflowY.auto
                                    ]
                                    prop.children [
                                        Html.div [
                                            prop.testId "GitSidebarBoundedHost"
                                            prop.className boundedWrapperClasses
                                            prop.style [ style.height (length.percent 100) ]
                                            prop.children [
                                                Swate.Components.Page.GitSidebar.Main(
                                                    status = {
                                                        CurrentBranch = Some "main"
                                                        TrackingBranch = Some "origin/main"
                                                        Ahead = 0
                                                        Behind = 0
                                                        IsClean = false
                                                        IsMergeInProgress = false
                                                    },
                                                    changedFiles = manyChangedFiles 200,
                                                    branchOptions = [| sidebarLocalBranch "main" true true |],
                                                    callbacks = {
                                                        OnRefresh = fun () -> ()
                                                        OnFetch = fun () -> ()
                                                        OnPush = fun () -> ()
                                                        OnUpdateFromOnline = fun () -> ()
                                                        OnPrimarySaveSelection = fun _ -> ()
                                                        OnPrimarySaveAll = fun _ -> ()
                                                        OnCommitSelection = fun _ -> ()
                                                        OnCommitAll = fun _ -> ()
                                                        OnDiscardSelection = fun _ -> ()
                                                        OnConfirmPendingRemoteAction = fun () -> ()
                                                        OnCancelPendingRemoteAction = fun () -> ()
                                                        OnSaveDownloadLargeFiles = fun _ -> ()
                                                        OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                                        OnSaveDiffIndexingLimit = fun _ -> ()
                                                        OnCreateBranch = fun _ -> ()
                                                        OnSwitchBranch = fun _ -> ()
                                                        OnSelectChange = fun _ -> promise { return Ok() }
                                                        OnPruneLfsCache = fun () -> ()
                                                        OnDedupLfsStorage = fun () -> ()
                                                        OnCancelOperation = fun () -> ()
                                                    },
                                                    downloadLargeFiles = true,
                                                    lfsAutoTrackThresholdMb = 5,
                                                    diffIndexingLimitMb = 1024
                                                )
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    )

                // With 200 items, only a subset should be rendered (visible + overscan).
                // If virtualization is broken, all 200 items would be in the DOM.
                let boundedHost =
                    container.querySelector ("[data-testid='GitSidebarBoundedHost']") :?> HTMLElement

                for expectedClass in boundedWrapperClasses do
                    Vitest.expect(boundedHost.classList.contains expectedClass).toBe (true)

                Vitest.expect(boundedHost.classList.contains "swt:w-full").toBe (false)

                let renderedRows =
                    container.querySelectorAll ("[data-testid^='GitSidebarChangeRow-']")

                Vitest.expect(renderedRows.length).toBeLessThan (200)
                Vitest.expect(container.querySelector ("[data-testid='GitSidebarChangeRow-0']")).not.toBeNull ()
                Vitest.expect(container.querySelector ("[data-testid='GitSidebarChangeRow-199']")).toBeNull ()

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar hides the inline git return text and exposes it through a popover trigger",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = [| changedFile "obsolete.md" "D" " " false |],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                Vitest.expect(container.textContent.Contains("git: D.")).toBe (false)
                Vitest.expect(container.textContent.Contains("Deleted")).toBe (false)
                Vitest.expect(container.querySelector ("[data-testid='GitSidebarChangeStatusIcon-0']")).not.toBeNull ()

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar keeps the change-status icon in a fixed right-edge slot",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Html.div [
                            prop.style [ style.width 340; style.height 760 ]
                            prop.children [
                                Swate.Components.Page.GitSidebar.Main(
                                    status = {
                                        CurrentBranch = Some "main"
                                        TrackingBranch = Some "origin/main"
                                        Ahead = 0
                                        Behind = 0
                                        IsClean = false
                                        IsMergeInProgress = false
                                    },
                                    changedFiles = [|
                                        changedFile
                                            "src/very/long/path/that/wraps/in/the/sidebar/and/needs/a/fixed/status/icon.txt"
                                            "M"
                                            " "
                                            false
                                    |],
                                    branchOptions = [| sidebarLocalBranch "main" true true |],
                                    callbacks = {
                                        OnRefresh = fun () -> ()
                                        OnFetch = fun () -> ()
                                        OnPush = fun () -> ()
                                        OnUpdateFromOnline = fun () -> ()
                                        OnPrimarySaveSelection = fun _ -> ()
                                        OnPrimarySaveAll = fun _ -> ()
                                        OnCommitSelection = fun _ -> ()
                                        OnCommitAll = fun _ -> ()
                                        OnDiscardSelection = fun _ -> ()
                                        OnConfirmPendingRemoteAction = fun () -> ()
                                        OnCancelPendingRemoteAction = fun () -> ()
                                        OnSaveDownloadLargeFiles = fun _ -> ()
                                        OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                        OnSaveDiffIndexingLimit = fun _ -> ()
                                        OnCreateBranch = fun _ -> ()
                                        OnSwitchBranch = fun _ -> ()
                                        OnSelectChange = fun _ -> promise { return Ok() }
                                        OnPruneLfsCache = fun () -> ()
                                        OnDedupLfsStorage = fun () -> ()
                                        OnCancelOperation = fun () -> ()
                                    },
                                    downloadLargeFiles = true,
                                    lfsAutoTrackThresholdMb = 5,
                                    diffIndexingLimitMb = 1024
                                )
                            ]
                        ]
                    )

                let statusSlot =
                    container.querySelector ("[data-testid='GitSidebarChangeStatusSlot-0']") :?> HTMLElement

                Vitest.expect(statusSlot.className.Contains("swt:ml-auto")).toBe (true)
                Vitest.expect(statusSlot.className.Contains("swt:shrink-0")).toBe (true)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar marks rows with Windows Explorer click semantics and shows two save buttons only while rows are marked",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = manyChangedFiles 3,
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let firstRow =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                let thirdRow =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-2']") :?> HTMLElement

                Vitest.expect(container.querySelectorAll("[data-testid='GitSidebarSaveAllButton']").length).toBe (1)

                Vitest
                    .expect(container.querySelectorAll("[data-testid='GitSidebarSaveSelectedButton']").length)
                    .toBe (0)

                firstRow.click ()
                do! Promise.sleep 0

                let shiftClick =
                    createMouseEvent "click" (createObj [ "bubbles" ==> true; "shiftKey" ==> true ])

                thirdRow.dispatchEvent shiftClick |> ignore
                do! Promise.sleep 0

                Vitest.expect(container.querySelectorAll("[data-testid='GitSidebarSaveAllButton']").length).toBe (1)

                Vitest
                    .expect(container.querySelectorAll("[data-testid='GitSidebarSaveSelectedButton']").length)
                    .toBe (1)

                Vitest
                    .expect(container.querySelectorAll("[data-testid^='GitSidebarCommitSelectionCheckbox-']").length)
                    .toBe (0)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar hover discard button discards the currently marked files",
            fun () -> promise {
                let mutable discardedPaths: string[] option = None

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = manyChangedFiles 3,
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                noopCallbacks with
                                    OnDiscardSelection = fun paths -> discardedPaths <- Some paths
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let row0 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                let row2 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-2']") :?> HTMLElement

                row0.click ()
                do! Promise.sleep 0

                let ctrlClick =
                    createMouseEvent "click" (createObj [ "bubbles" ==> true; "ctrlKey" ==> true ])

                row2.dispatchEvent ctrlClick |> ignore
                do! Promise.sleep 0

                let hoverEvent = createMouseEvent "mouseenter" (createObj [ "bubbles" ==> true ])

                row0.dispatchEvent hoverEvent |> ignore
                do! Promise.sleep 0

                let discardButton =
                    container.querySelector ("[data-testid='GitSidebarDiscardChangeButton-0']") :?> HTMLElement

                discardButton.click ()
                do! Promise.sleep 0

                Vitest.expect(discardedPaths).toEqual (Some [| "src/file-000.txt"; "src/file-002.txt" |])

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar plain click clears previous marks and ctrl-click toggles a row on and back off",
            fun () -> promise {
                let mutable capturedSelection: GitSidebarCommitSelectionRequest option = None

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = manyChangedFiles 3,
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun request -> capturedSelection <- Some request
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let row0 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                let row1 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-1']") :?> HTMLElement

                let row2 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-2']") :?> HTMLElement

                let messageInput =
                    container.querySelector ("[data-testid='GitSidebarCommitMessageInput']") :?> HTMLTextAreaElement

                row0.click ()
                do! Promise.sleep 0

                let ctrlClick =
                    createMouseEvent "click" (createObj [ "bubbles" ==> true; "ctrlKey" ==> true ])

                row2.dispatchEvent ctrlClick |> ignore
                do! Promise.sleep 0
                row2.dispatchEvent ctrlClick |> ignore
                do! Promise.sleep 0
                row1.click ()
                do! Promise.sleep 0

                setTextAreaValue messageInput "save one file"
                do! Promise.sleep 0

                let saveButton =
                    container.querySelector ("[data-testid='GitSidebarSaveSelectedButton']") :?> HTMLButtonElement

                Vitest.expect(saveButton.disabled).toBe (false)
                saveButton.click ()
                do! Promise.sleep 0

                Vitest.expect(capturedSelection |> Option.map _.Paths).toEqual (Some [| "src/file-001.txt" |])

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar ctrl-shift-click adds the anchor range to the existing marked set",
            fun () -> promise {
                let mutable capturedSelection: GitSidebarCommitSelectionRequest option = None

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = manyChangedFiles 3,
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun request -> capturedSelection <- Some request
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let row0 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                let row2 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-2']") :?> HTMLElement

                let messageInput =
                    container.querySelector ("[data-testid='GitSidebarCommitMessageInput']") :?> HTMLTextAreaElement

                row0.click ()
                do! Promise.sleep 0

                let ctrlShiftClick =
                    createMouseEvent
                        "click"
                        (createObj [
                            "bubbles" ==> true
                            "ctrlKey" ==> true
                            "shiftKey" ==> true
                        ])

                row2.dispatchEvent ctrlShiftClick |> ignore
                do! Promise.sleep 0

                setTextAreaValue messageInput "save range"
                do! Promise.sleep 0

                let saveButton =
                    container.querySelector ("[data-testid='GitSidebarSaveSelectedButton']") :?> HTMLButtonElement

                Vitest.expect(saveButton.disabled).toBe (false)
                saveButton.click ()
                do! Promise.sleep 0

                Vitest
                    .expect(capturedSelection |> Option.map _.Paths)
                    .toEqual (
                        Some [|
                            "src/file-000.txt"
                            "src/file-001.txt"
                            "src/file-002.txt"
                        |]
                    )

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar keeps marked rows selected even when diff opening fails",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = [|
                                changedFile "README.md" "M" " " false
                                changedFile "docs/guide.md" "M" " " false
                            |],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Error "Diff failed to load." }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let firstRow =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                firstRow.click ()
                do! Promise.sleep 0

                Vitest.expect(container.textContent.Contains("Save Selected Changes")).toBe (true)
                Vitest.expect(container.querySelector ("[data-testid='GitSidebarErrorNotice']")).not.toBeNull ()

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar publish rename prompt submits the edited repository name",
            fun () -> promise {
                let mutable submittedName: string option = None
                let mutable cancelCalls = 0

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = None
                                Ahead = 1
                                Behind = 0
                                IsClean = true
                                IsMergeInProgress = false
                            },
                            changedFiles = [||],
                            branchOptions = [| sidebarLocalBranch "main" true false |],
                            callbacks = noopCallbacks,
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024,
                            publishRenamePrompt = {
                                CurrentName = "Existing ARC"
                                Message = "A DataHUB repository named 'Existing ARC' already exists."
                                Owners = [||]
                                SelectedNamespaceId = None
                            },
                            onSubmitPublishRename = (fun submit -> submittedName <- Some submit.Name),
                            onCancelPublishRename = (fun () -> cancelCalls <- cancelCalls + 1)
                        )
                    )

                let input =
                    document.body.querySelector ("[data-testid='GitSidebarPublishRenameInput']") :?> HTMLInputElement

                Vitest.expect(input.value).toBe ("Existing ARC")

                let submitButton =
                    document.body.querySelector ("[data-testid='GitSidebarPublishRenameSubmit']") :?> HTMLButtonElement

                // The refused name can't be sent again unchanged.
                Vitest.expect(submitButton.disabled).toBe (true)

                setInputValue input "Renamed ARC"
                do! Promise.sleep 0

                Vitest.expect(submitButton.disabled).toBe (false)
                submitButton.click ()
                do! Promise.sleep 0

                Vitest.expect(submittedName).toEqual (Some "Renamed ARC")
                Vitest.expect(cancelCalls).toBe (0)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar Ctrl+click on a marked row deselects it and rows suppress native text selection",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = [|
                                changedFile "README.md" "M" " " false
                                changedFile "docs/guide.md" "M" " " false
                            |],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                OnRefresh = fun () -> ()
                                OnFetch = fun () -> ()
                                OnPush = fun () -> ()
                                OnUpdateFromOnline = fun () -> ()
                                OnPrimarySaveSelection = fun _ -> ()
                                OnPrimarySaveAll = fun _ -> ()
                                OnCommitSelection = fun _ -> ()
                                OnCommitAll = fun _ -> ()
                                OnDiscardSelection = fun _ -> ()
                                OnConfirmPendingRemoteAction = fun () -> ()
                                OnCancelPendingRemoteAction = fun () -> ()
                                OnSaveDownloadLargeFiles = fun _ -> ()
                                OnSaveLfsAutoTrackThreshold = fun _ -> ()
                                OnSaveDiffIndexingLimit = fun _ -> ()
                                OnCreateBranch = fun _ -> ()
                                OnSwitchBranch = fun _ -> ()
                                OnSelectChange = fun _ -> promise { return Ok() }
                                OnPruneLfsCache = fun () -> ()
                                OnDedupLfsStorage = fun () -> ()
                                OnCancelOperation = fun () -> ()
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let firstRow =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                firstRow.click ()
                do! Promise.sleep 0

                Vitest
                    .expect(container.querySelectorAll("[data-testid='GitSidebarSaveSelectedButton']").length)
                    .toBe (1)

                let ctrlClick =
                    createMouseEvent "click" (createObj [ "bubbles" ==> true; "ctrlKey" ==> true ])

                firstRow.dispatchEvent ctrlClick |> ignore
                do! Promise.sleep 0

                Vitest
                    .expect(container.querySelectorAll("[data-testid='GitSidebarSaveSelectedButton']").length)
                    .toBe (0)

                Vitest.expect(container.querySelectorAll("[data-testid='GitSidebarSaveAllButton']").length).toBe (1)

                let rowClass = firstRow.className
                Vitest.expect(rowClass.Contains("swt:select-none")).toBe (true)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar status icon click does not open the changed file row",
            fun () -> promise {
                let mutable selectCalls = 0

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = [| changedFile "README.md" "M" " " false |],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = {
                                noopCallbacks with
                                    OnSelectChange =
                                        fun _ ->
                                            selectCalls <- selectCalls + 1
                                            promise { return Ok() }
                            },
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let statusIcon =
                    container.querySelector ("[data-testid='GitSidebarChangeStatusIcon-0']") :?> HTMLElement

                let clickEvent = createMouseEvent "click" (createObj [ "bubbles" ==> true ])

                statusIcon.dispatchEvent clickEvent |> ignore
                do! Promise.sleep 0

                Vitest.expect(selectCalls).toBe (0)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar renders the save section without the old outer card wrapper",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = [| changedFile "README.md" "M" " " false |],
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = noopCallbacks,
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let saveSection =
                    container.querySelector ("[data-testid='GitSidebarCommitSection']") :?> HTMLElement

                let legacyCard = firstElementChild saveSection
                Vitest.expect(legacyCard.className.Contains("swt:rounded-box")).toBe (false)
                Vitest.expect(legacyCard.className.Contains("swt:border")).toBe (false)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar checks upload right away by default and Save All uses the primary save callback",
            fun () -> promise {
                let mutable primaryAllMessage: string option = None
                let mutable commitAllCalls = 0

                let! container, cleanup =
                    renderSaveSidebar {
                        noopCallbacks with
                            OnPrimarySaveAll = fun message -> primaryAllMessage <- Some message
                            OnCommitAll = fun _ -> commitAllCalls <- commitAllCalls + 1
                    }

                let uploadCheckbox =
                    container.querySelector ("[data-testid='GitSidebarUploadRightAwayCheckbox']") :?> HTMLInputElement

                let messageInput =
                    container.querySelector ("[data-testid='GitSidebarCommitMessageInput']") :?> HTMLTextAreaElement

                Vitest.expect(uploadCheckbox.``checked``).toBe (true)

                setTextAreaValue messageInput "save everything"
                do! Promise.sleep 0

                let saveAllButton =
                    container.querySelector ("[data-testid='GitSidebarSaveAllButton']") :?> HTMLButtonElement

                saveAllButton.click ()
                do! Promise.sleep 0

                Vitest.expect(primaryAllMessage).toEqual (Some "save everything")
                Vitest.expect(commitAllCalls).toBe (0)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar shows the controlled upload right away value and reports changes without storing them",
            fun () -> promise {
                let reported = ResizeArray<bool>()

                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = manyChangedFiles 3,
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = noopCallbacks,
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024,
                            uploadRightAway = false,
                            onUploadRightAwayChange = reported.Add
                        )
                    )

                let uploadCheckbox =
                    container.querySelector ("[data-testid='GitSidebarUploadRightAwayCheckbox']") :?> HTMLInputElement

                Vitest.expect(uploadCheckbox.``checked``).toBe (false)

                uploadCheckbox.click ()
                do! Promise.sleep 0

                Vitest.expect(reported |> Seq.toArray).toEqual ([| true |])
                Vitest.expect(uploadCheckbox.``checked``).toBe (false)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar Save All with rows marked sends only the message and no paths",
            fun () -> promise {
                let mutable primaryAllMessage: string option = None
                let mutable primarySelectionCalls = 0

                let! container, cleanup =
                    renderSaveSidebar {
                        noopCallbacks with
                            OnPrimarySaveAll = fun message -> primaryAllMessage <- Some message
                            OnPrimarySaveSelection = fun _ -> primarySelectionCalls <- primarySelectionCalls + 1
                    }

                let row0 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                let row2 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-2']") :?> HTMLElement

                let messageInput =
                    container.querySelector ("[data-testid='GitSidebarCommitMessageInput']") :?> HTMLTextAreaElement

                row0.click ()
                do! Promise.sleep 0

                let ctrlClick =
                    createMouseEvent "click" (createObj [ "bubbles" ==> true; "ctrlKey" ==> true ])

                row2.dispatchEvent ctrlClick |> ignore
                do! Promise.sleep 0

                Vitest
                    .expect(container.querySelectorAll("[data-testid='GitSidebarSaveSelectedButton']").length)
                    .toBe (1)

                setTextAreaValue messageInput "save all while marked"
                do! Promise.sleep 0

                let saveAllButton =
                    container.querySelector ("[data-testid='GitSidebarSaveAllButton']") :?> HTMLButtonElement

                saveAllButton.click ()
                do! Promise.sleep 0

                Vitest.expect(primaryAllMessage).toEqual (Some "save all while marked")
                Vitest.expect(primarySelectionCalls).toBe (0)

                Vitest
                    .expect(container.querySelectorAll("[data-testid='GitSidebarSaveSelectedButton']").length)
                    .toBe (1)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar routes both save buttons to the local commit callbacks when upload right away is unchecked and clears the message",
            fun () -> promise {
                let mutable commitSelection: GitSidebarCommitSelectionRequest option = None
                let mutable commitAllMessage: string option = None
                let mutable primaryCalls = 0

                let! container, cleanup =
                    renderSaveSidebar {
                        noopCallbacks with
                            OnPrimarySaveSelection = fun _ -> primaryCalls <- primaryCalls + 1
                            OnPrimarySaveAll = fun _ -> primaryCalls <- primaryCalls + 1
                            OnCommitSelection = fun request -> commitSelection <- Some request
                            OnCommitAll = fun message -> commitAllMessage <- Some message
                    }

                let uploadCheckbox =
                    container.querySelector ("[data-testid='GitSidebarUploadRightAwayCheckbox']") :?> HTMLInputElement

                let messageInput =
                    container.querySelector ("[data-testid='GitSidebarCommitMessageInput']") :?> HTMLTextAreaElement

                let row0 =
                    container.querySelector ("[data-testid='GitSidebarChangeRow-0']") :?> HTMLElement

                uploadCheckbox.click ()
                do! Promise.sleep 0

                Vitest.expect(uploadCheckbox.``checked``).toBe (false)

                row0.click ()
                do! Promise.sleep 0
                setTextAreaValue messageInput "local selected"
                do! Promise.sleep 0

                let saveSelectedButton =
                    container.querySelector ("[data-testid='GitSidebarSaveSelectedButton']") :?> HTMLButtonElement

                saveSelectedButton.click ()
                do! Promise.sleep 0

                Vitest
                    .expect(commitSelection |> Option.map (fun request -> request.Message, request.Paths))
                    .toEqual (Some("local selected", [| "src/file-000.txt" |]))

                Vitest.expect(messageInput.value).toBe ("")

                Vitest.expect(container.querySelector ("[data-testid='GitSidebarSaveSelectedButton']")).toBeNull ()

                setTextAreaValue messageInput "local all"
                do! Promise.sleep 0

                let saveAllButton =
                    container.querySelector ("[data-testid='GitSidebarSaveAllButton']") :?> HTMLButtonElement

                saveAllButton.click ()
                do! Promise.sleep 0

                Vitest.expect(commitAllMessage).toEqual (Some "local all")
                Vitest.expect(messageInput.value).toBe ("")
                Vitest.expect(primaryCalls).toBe (0)

                cleanup ()
            }
        )

        Vitest.test (
            "GitSidebar keeps the message and upload checkbox editable during a silent refresh but disables saving",
            fun () -> promise {
                let! container, cleanup =
                    renderToBody (
                        Swate.Components.Page.GitSidebar.Main(
                            status = {
                                CurrentBranch = Some "main"
                                TrackingBranch = Some "origin/main"
                                Ahead = 0
                                Behind = 0
                                IsClean = false
                                IsMergeInProgress = false
                            },
                            changedFiles = manyChangedFiles 3,
                            branchOptions = [| sidebarLocalBranch "main" true true |],
                            callbacks = noopCallbacks,
                            runStatus = GitSidebarRunStatus.BusyWithoutNotice,
                            downloadLargeFiles = true,
                            lfsAutoTrackThresholdMb = 5,
                            diffIndexingLimitMb = 1024
                        )
                    )

                let saveAllButton =
                    container.querySelector ("[data-testid='GitSidebarSaveAllButton']") :?> HTMLButtonElement

                let messageInput =
                    container.querySelector ("[data-testid='GitSidebarCommitMessageInput']") :?> HTMLTextAreaElement

                let uploadCheckbox =
                    container.querySelector ("[data-testid='GitSidebarUploadRightAwayCheckbox']") :?> HTMLInputElement

                Vitest.expect(container.querySelector ("[data-testid='GitSidebarProgressNotice']")).toBeNull ()
                Vitest.expect(saveAllButton.disabled).toBe (true)
                Vitest.expect(messageInput.disabled).toBe (false)
                Vitest.expect(uploadCheckbox.disabled).toBe (false)

                cleanup ()
            }
        )
)
