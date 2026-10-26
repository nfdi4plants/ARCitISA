/// This module SHOULD only contain the exact IPC communication types.
module Swate.Electron.Shared.IPCTypes

open Fable.Core
open Swate.Components.Api.GitLabApi
open Swate.Components.Composite.Authentication.Types
open Swate.Components.Page.DataHub.DataHubTypes
open Swate.Components.Page.ValidationPackageBrowser.Types
open Swate.Components.Shared
open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Electron.Shared.DTOs.NoteSearchDto
open Swate.Electron.Shared.DTOs.ProvenanceGroupingDto
open Swate.Electron.Shared.DTOs.ArcNodesDto
open AuthTypes
open FileIOTypes
open VersionControlTypes

module IPCTypesHelper =

    [<RequireQualifiedAccess>]
    type SaveBeforeQuitDecision =
        | SaveAndClose
        | CloseWithoutSaving
        | CancelClose

open IPCTypesHelper

[<Literal>]
let swateReleaseRepository = "nfdi4plants/ARCitISA"

type AppVersionState = {
    CurrentVersion: string
    UpdateVersion: string option
    ChangelogVersion: string option
}

type IAppVersionApi = {
    getAppVersion: unit -> JS.Promise<AppVersionState>
    openUpdate: unit -> JS.Promise<Result<unit, exn>>
}

type CreateArcRequest = { identifier: string; initGit: bool }

/// Two Way Bridge: Renderer <-> Main
type IUiSettingsApi = {
    /// Persists a finite positive scale globally and applies it to all windows. 1.0 = 100%.
    setUiScale: float -> JS.Promise<Result<unit, exn>>
    getUiScale: unit -> JS.Promise<float>
}

/// The values a user can set as defaults for every newly opened ARC session.
type UserDefaultValuesDto = {
    /// Whole MiB from 1 to 100. Files at or above it use large-object storage automatically.
    LfsAutoTrackThresholdMb: int
    DownloadLargeFiles: bool
    /// Whole MiB from 1 to 1048576.
    DiffIndexingLimitMb: int
    UploadRightAway: bool
    AutoCreateNotesFolder: bool
}

/// The values new sessions start from, next to the values Swate ships with.
type UserDefaultsDto = {
    Effective: UserDefaultValuesDto
    BuiltIn: UserDefaultValuesDto
}

/// Two Way Bridge: Renderer <-> Main
type IUserDefaultsApi = {
    getUserDefaults: unit -> JS.Promise<UserDefaultsDto>
    /// Validates every field, stores the values that differ from the built-in ones and tells all windows.
    /// An Error leaves the stored values as they were. It names the refused field for an invalid value.
    /// A newer-schema file or a failed write gets a message without a field.
    setUserDefaults: UserDefaultValuesDto -> JS.Promise<Result<UserDefaultsDto, string>>
}

[<RequireQualifiedAccess>]
type CreateArcOutcome =
    | Created of path: string
    | FocusedExisting of path: string
    | Cancelled
    | CreatedButClosed of path: string

/// TEMPORARY DUCT-TAPE WORKAROUND: This IPC API only exists because template loading currently cannot run
/// through ARCtrl on the Swate .NET server or directly in the renderer due to GitHub CORS.
/// Remove it when ARCtrl provides a working .NET implementation or a CORS-enabled template source.
type ITemplateApi = {
    getTemplates: unit -> JS.Promise<Result<string, exn>>
}

/// Two Way Bridge: Renderer <-> Main
/// PackageContent is intentionally omitted from ValidationPackageDTO to avoid moving large payloads over IPC.
type IValidationPackageIPC = {
    getAllPackages: unit -> JS.Promise<Result<ValidationPackageDTO[], exn>>
}

[<RequireQualifiedAccess; StringEnum>]
type LargeObjectIndexStatus =
    | NotLoaded
    | Loading
    | Ready
    | Unavailable

/// State of the large object index of a vault. entries is empty unless the state is ready. caseInsensitive is the
/// flag the paths of the entries were keyed with.
type LargeObjectIndexSnapshot = {|
    version: int
    state: LargeObjectIndexStatus
    caseInsensitive: bool
    entries: ObjectStateDto[]
|}

[<RequireQualifiedAccess; StringEnum>]
type FileWatcherStatus =
    | Running
    | Degraded
    | Unavailable

/// Whether the structural file watcher works. reason is the error text of a degraded or unavailable watcher and
/// empty otherwise.
type FileWatcherStateDto = {|
    state: FileWatcherStatus
    reason: string
|}

/// Structural snapshot of the open ARC's file tree. Paths are ARC-relative and the root is "". loadedDirectories
/// lists the directories whose children are in entries. treeVersion orders snapshots and updates of one vault.
type FileTreeSnapshotDto = {|
    treeVersion: int
    entries: FileEntry[]
    loadedDirectories: string[]
    caseInsensitive: bool
    fileWatcher: FileWatcherStateDto
|}

/// The current children of one loaded directory.
type FileTreeDirectoryChildrenDto = {|
    directoryPath: string
    children: FileEntry[]
|}

/// The current children of the loaded directories that one writer job changed, parents first, stamped with the
/// tree version of the job. The renderer applies all directories in one step.
type FileTreeDirectoryUpdateDto = {|
    treeVersion: int
    directories: FileTreeDirectoryChildrenDto[]
|}

/// Size and modification time of one file, as stat reports them.
type FileStatDto = {| sizeBytes: float; mtimeMs: float |}

/// Two Way Bridge: Renderer <-> Main
type IArcVaultsApi = {
    /// Open ARC via folder dialog. Main decides: current window / new window / focus existing.
    openARC: unit -> JS.Promise<Result<string option, exn>>
    /// Open ARC at a known path (e.g. recent-ARC click). Main decides disposition.
    openARCByPath: string -> JS.Promise<Result<string, exn>>
    /// Create ARC via folder dialog. Main decides disposition.
    createARC: CreateArcRequest -> JS.Promise<Result<CreateArcOutcome, exn>>
    /// Ensure ARC notes scaffolding exists for the ARCVault root path.
    ensureNotesFolder: unit -> JS.Promise<Result<unit, exn>>
    closeARC: unit -> JS.Promise<Result<unit, exn>>
    getOpenPath: unit -> JS.Promise<string option>
    getRecentARCs: unit -> JS.Promise<ARCPointer[]>
    removeRecentARC: ARCPointer -> JS.Promise<Result<unit, exn>>

    pickArcPaths: unit -> JS.Promise<Result<string[], exn>>
    pickDirectory: unit -> JS.Promise<Result<string, exn>>
    pickAbsolutePaths: FileSystemItemKind -> JS.Promise<Result<string option, exn>>
    pickExternalTextFiles: unit -> JS.Promise<Result<ImportedTextFile[], exn>>
    tryImportExternalFiles: ImportExternalFilesRequest -> JS.Promise<Result<ImportExternalFilesResult, exn>>
    cancelImportExternalFiles: string -> JS.Promise<Result<unit, exn>>
    getActiveFileImport: unit -> JS.Promise<Result<ActiveFileImportState option, exn>>
    /// Pulls the structural file tree of the open ARC. Other directories load through loadDirectory.
    getFileTreeSnapshot: unit -> JS.Promise<Result<FileTreeSnapshotDto, exn>>
    /// Reconciles one directory (ARC-relative path), loads it when needed and answers with its current children.
    loadDirectory: string -> JS.Promise<Result<FileTreeDirectoryUpdateDto, exn>>
    /// Reconciles the given loaded directories and publishes the ones that changed. Unloaded ones are skipped.
    revalidateDirectories: string[] -> JS.Promise<Result<unit, exn>>
    /// Stats one ARC-relative file. None when it is missing or not a file.
    statFile: string -> JS.Promise<Result<FileStatDto option, exn>>
    /// Pulls the current large object index of the vault. A vault without an index answers notLoaded with no entries.
    getLargeObjectIndex: unit -> JS.Promise<Result<LargeObjectIndexSnapshot, exn>>
    pathExists: string -> JS.Promise<Result<bool, exn>>
    openFile: string -> JS.Promise<Result<FileContentDTO, exn>>
    openArcFolderInFileExplorer: unit -> JS.Promise<Result<unit, exn>>
    showPathInFileExplorer: string -> JS.Promise<Result<unit, exn>>
    openPathWithDefaultApplication: string -> JS.Promise<Result<unit, exn>>
    readNotes: unit -> JS.Promise<Result<NoteSearchDto[], exn>>
    /// Returns serialized persons from every entity in the active vault's in-memory ARC.
    getPersons: unit -> JS.Promise<Result<string[], exn>>
    /// Returns the input and output columns of every study, assay and run table in the active vault's in-memory ARC.
    getArcNodes: unit -> JS.Promise<Result<ArcNodeTableDto[], exn>>
    listProvenanceTables: unit -> JS.Promise<Result<ProvenanceTableSelectionDto[], exn>>
    loadProvenanceTable: ProvenanceTableSelectionDto -> JS.Promise<Result<ProvenanceLoadResultDto, exn>>
    /// Builds the provenance graph data of the active vault's in-memory ARC.
    loadProvenanceGraph: unit -> JS.Promise<Result<ProvenanceGraphData, exn>>
    /// Persists the active in-memory ARC scaffold to disk.
    saveArcFile: unit -> JS.Promise<Result<unit, exn>>
    /// Applies ARC file changes to the active vault's in-memory ARC without writing to disk.
    setArcFileInMemory: FileContentDTO -> JS.Promise<Result<unit, exn>>
    /// Adds a new ARC entity from the file tree. The file watcher performs the follow-up merge and file-tree update.
    addArcFile: FileContentDTO -> JS.Promise<Result<unit, exn>>
    /// Creates a generic file or folder inside a safe ARC directory.
    createFileSystemItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>
    /// Checks if there are unsaved changes in the in-memory ARC scaffold compared to the last saved state on disk. Does not trigger a save or write to disk.
    getHasUnsavedArcChanges: unit -> JS.Promise<Result<bool, exn>>
    deletePath: string -> JS.Promise<Result<unit, exn>>
    renamePath: RenamePathRequest -> JS.Promise<Result<unit, exn>>
    movePath: MovePathRequest -> JS.Promise<Result<unit, exn>>
    renameOpenArcRoot: string -> JS.Promise<Result<string, exn>>
    writeFile: FileContentDTO -> JS.Promise<Result<unit, exn>>
    resolveCloseRequest: SaveBeforeQuitDecision -> JS.Promise<Result<unit, exn>>
}

/// Two Way Bridge: Renderer <-> Main
/// Provider-neutral version control over the active vault. Every call names an
/// operation id so the renderer can cancel it, and every result keeps the library's
/// structured outcome.
type IVersionControlApi = {
    getSessionInfo: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceSessionInfoDto>, exn>>
    cloneWorkspace: CloneWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<string>, exn>>
    initializeWorkspace: InitializeWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<string>, exn>>
    bindWorkspace: BindWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceSessionInfoDto>, exn>>
    cancelOperation: OperationRequestDto -> JS.Promise<Result<bool, exn>>
    checkDependencies: OperationRequestDto -> JS.Promise<Result<OperationResultDto<DependencyStatusDto[]>, exn>>
    installDependency: InstallDependencyRequestDto -> JS.Promise<Result<OperationResultDto<DependencyStatusDto>, exn>>
    getStatus: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceStatusDto>, exn>>
    listRefs: OperationRequestDto -> JS.Promise<Result<OperationResultDto<LogicalRefDto[]>, exn>>
    createRef: CreateRefRequestDto -> JS.Promise<Result<OperationResultDto<LogicalRefDto>, exn>>
    preflightSwitchRef: SwitchRefRequestDto -> JS.Promise<Result<OperationResultDto<SwitchPreflightDto>, exn>>
    switchRef: SwitchRefRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceStatusDto>, exn>>
    createRevision: CreateRevisionRequestDto -> JS.Promise<Result<OperationResultDto<string>, exn>>
    restorePaths: RestorePathsRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    refreshSynchronization: OperationRequestDto -> JS.Promise<Result<OperationResultDto<SynchronizationStateDto>, exn>>
    synchronize: SynchronizeRequestDto -> JS.Promise<Result<OperationResultDto<SynchronizationStateDto>, exn>>
    resolveConflict:
        ResolveConflictRequestDto -> JS.Promise<Result<OperationResultDto<ConflictResolutionOutcomeDto>, exn>>
    finalizeConflict: FinalizeConflictRequestDto -> JS.Promise<Result<OperationResultDto<string option>, exn>>
    cancelConflict: CancelConflictRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    listObjects: OperationRequestDto -> JS.Promise<Result<OperationResultDto<ObjectStateDto[]>, exn>>
    materializeObject: ObjectPathRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    dematerializeObject: ObjectPathRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    getStoragePolicySettings:
        OperationRequestDto -> JS.Promise<Result<OperationResultDto<StoragePolicySettingsDto>, exn>>
    setStoragePolicySettings: StoragePolicySettingsRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    /// The "Upload right away" checkbox the open session of the calling window holds in memory.
    /// Fails when no session is open. The call is not a tracked operation and takes no lock.
    getUploadRightAway: unit -> JS.Promise<Result<OperationResultDto<bool>, exn>>
    /// Stores the checkbox value in the open session. Fails when no session is open.
    setUploadRightAway: bool -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    setPathStoragePolicy: PathStoragePolicyRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
    pruneStorage: OperationRequestDto -> JS.Promise<Result<OperationResultDto<string>, exn>>
    deduplicateStorage: OperationRequestDto -> JS.Promise<Result<OperationResultDto<string>, exn>>
    getRepositoryWebUrl: OperationRequestDto -> JS.Promise<Result<OperationResultDto<string option>, exn>>
    /// Removes a stale provider lock left by a killed process, only while no operation
    /// of this session runs, then refreshes and returns the status.
    clearStaleLock: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceStatusDto>, exn>>
    /// Opens a paged diff of one path. A Scanning result returns a continuation for the
    /// next openTextDiff call. The handle belongs to the calling window.
    openTextDiff: OpenTextDiffRequestDto -> JS.Promise<Result<OperationResultDto<ResumableOpenDto>, exn>>
    readTextDiffPage: ReadTextDiffPageRequestDto -> JS.Promise<Result<OperationResultDto<ResumablePageDto>, exn>>
    /// Returns an earlier page again by its page id.
    replayTextDiffPage: ReplayTextDiffPageRequestDto -> JS.Promise<Result<OperationResultDto<DiffPageDto>, exn>>
    /// Reveals lines of a hidden equal gap from its start or its end.
    expandTextDiff: ExpandTextDiffRequestDto -> JS.Promise<Result<OperationResultDto<ResumablePartsDto>, exn>>
    /// Reads a further UTF-16 slice of one long line.
    readTextDiffLine: ReadTextDiffLineRequestDto -> JS.Promise<Result<OperationResultDto<ResumableLineDto>, exn>>
    getTextDiffSourceInfo:
        TextDiffHandleRequestDto -> JS.Promise<Result<OperationResultDto<DiffSourceInfoPairDto>, exn>>
    /// Closes a diff handle. Closing a handle twice succeeds.
    closeTextDiff: TextDiffHandleRequestDto -> JS.Promise<Result<OperationResultDto<unit>, exn>>
}

/// Two Way Bridge: Renderer <-> Main
type IGitLabApi = {
    loadAllRepos: ExploreRepoQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    loadMostStarredRepos: ExploreMostStarredQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    loadUserRepos: ExploreRepoQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    loadOrganisationGroups: ExploreGroupsQuery -> JS.Promise<Result<PagedResponse<GroupDto>, GitLabError>>
    loadOrganisationRepos:
        ExploreGroupProjectsQuery -> JS.Promise<Result<PagedResponse<ExploreProjectDto>, GitLabError>>
    createProject: CreateProjectRequest -> JS.Promise<Result<ExploreProjectDto, CreateProjectError>>
    loadPublishOwners: unit -> JS.Promise<Result<PublishOwnersDto, GitLabError>>
}

/// Two Way Bridge: Renderer <-> Main
type IAuthApi = {
    signIn: AuthSignInRequest -> Fable.Core.JS.Promise<Result<AuthResult, exn>>
    getAuthState: unit -> Fable.Core.JS.Promise<Result<AuthStateDto, exn>>
    signOut: unit -> Fable.Core.JS.Promise<Result<unit, exn>>
    revalidate: unit -> Fable.Core.JS.Promise<Result<AuthResult, exn>>
    listAccounts: unit -> Fable.Core.JS.Promise<Result<AccountSummary array, exn>>
    rotatePersonalAccessToken: string -> Fable.Core.JS.Promise<Result<AuthStateDto, exn>>
    setActiveAccount: string -> Fable.Core.JS.Promise<Result<AuthStateDto, exn>>
    removeAccount: string -> Fable.Core.JS.Promise<Result<unit, exn>>
}

/// One Way Bridge: Main -> Renderer
module MainToRendererIpc =

    /// The new ARC root. isRename is true when the same ARC folder got a new name, so ARC-relative state such as
    /// expanded folders stays valid. The renderer pulls the file tree after it.
    type PathChangeDto = {| path: string; isRename: bool |}

    /// Tells the renderer that the open ARC root changed.
    type IPathChangeRendererApi = {
        pathChange: PathChangeDto option -> unit
    }

    type IRecentArcsRendererApi = {
        recentARCsUpdate: ARCPointer[] -> unit
    }

    type IAuthAccountsRendererApi = {
        authAccountsUpdate: AuthStateDto -> unit
    }

    type IFileTreeRendererApi = {
        fileTreeDirectoryUpdate: FileTreeDirectoryUpdateDto -> unit
        fileContentChanged:
            {|
                path: string
                sizeBytes: float
                mtimeMs: float
            |}
                -> unit
        /// The state of the structural file watcher changed. Only unavailable needs a notice.
        fileWatcherStateChanged: FileWatcherStateDto -> unit
        largeObjectIndexUpdate: LargeObjectIndexSnapshot -> unit
    }

    type IFileImportRendererApi = {
        fileImportStateUpdate: ActiveFileImportState option -> unit
    }

    type IGitRepositoryRendererApi = {
        gitRepositoryInitialized: string -> unit
    }

    type IVersionControlRendererApi = {
        versionControlProgress: VersionControlProgressDto -> unit
        versionControlOperationStarted: OperationRequestDto -> unit
    }

    type IHasUnsavedArcChangesRendererApi = {
        arcUnsavedChangesUpdate: bool -> unit
    }

    type IAppVersionRendererApi = {
        appVersionChanged: AppVersionState -> unit
    }

    type IReleaseNotesRendererApi = { showReleaseNotes: string -> unit }

    type IUiSettingsRendererApi = { uiScaleChanged: float -> unit }

    type IUserDefaultsRendererApi = {
        userDefaultsChanged: UserDefaultsDto -> unit
    }

// TODO: What should filewatcher do when detecting changes?
/// One Way Bridge: Main -> Renderer
type IArcFileWatcherApi = {
    /// This function is called when ARC is reloaded due to local file changes.
    IsLoadingChanges: bool -> unit
}

type IMainSaveBeforeQuitApi = { requestSaveBeforeQuit: unit -> unit }
