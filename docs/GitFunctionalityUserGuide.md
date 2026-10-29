# ARCitISA version control guide

This guide describes how ARCitISA Electron talks to a version control provider. It is for developers who call, extend, test or troubleshoot that functionality. Git is the provider ARCitISA provisions today. The code paths above the Electron composition root are provider neutral, so a second provider (lakeFS is compiled in without a configured connection) needs no change in the IPC or the renderer.

## 1. Architecture

Version control operations run in the Electron main process through the VersionControlService NuGet package and reach the renderer through the typed IPC bridge.

- Library: the `VersionControlService` package (Abstractions, Git and lakeFS providers). Its `WorkspaceSession` record exposes the core service plus optional services (synchronization, text diff, conflict resolution, object materialization, storage policy, maintenance, repository browser).
- Composition root: `src/Electron/src/Main/VersionControl/ProviderComposition.fs` builds the Git factory (credentials and commit identity from the DataHub accounts, see `DataHubStrategies.fs`) and the lakeFS factory, and `VersionControlRuntime.fs` holds the catalog, the binding store and the resolver. This folder is the only place that knows provider ids or provider types.
- Session host: `src/Electron/src/Main/VersionControl/WorkspaceSessionHost.fs` keeps one open session per vault root, adopts unbound repositories on first open, persists the workspace binding, owns the registry of running operations for cancellation, and holds the per-session settings.
- IPC handler: `src/Electron/src/Main/IPC/IVersionControlApi.fs` implements `IVersionControlApi` from `src/Electron/src/Swate.Electron.Shared/IPCTypes.fs` over the DTOs in `VersionControlTypes.fs`. `Mappings.fs` translates every library type to its DTO and validates paths, refs and revisions before a provider call.
- Renderer client: `src/Electron/src/Renderer/VersionControlApiClient.fs` wraps the bridge, generates operation ids and maps `Result<'T, exn>` to `Result<'T, string>`.
- Renderer workflow: `src/Electron/src/Renderer/Context/GitWorkflow.fs` is the Elmish state machine behind the Git sidebar, wired in `GitStateContext.fs`. Feature code reaches it through `GitWorkflow.GitDependencies`.

The DataHub specific pieces stay in ARCitISA: the GitLab API client (`Main/Auth/GitLabApi.fs`), the account store, and the Git LFS ruleset in `src/Shared/GitLfsRules.fs`.

## 2. Renderer API

Every function of `Renderer.VersionControlApiClient` takes a request DTO that carries an `OperationId`. The renderer allocates the id with `VersionControlApiClient.newOperationId ()` before the call, so the cancel button knows the key before the main process has started the operation.

```fsharp
getSessionInfo: OperationRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceSessionInfoDto>, string>>
cloneWorkspace: CloneWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
initializeWorkspace: InitializeWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<string>, string>>
bindWorkspace: BindWorkspaceRequestDto -> JS.Promise<Result<OperationResultDto<WorkspaceSessionInfoDto>, string>>
cancelOperation: OperationRequestDto -> JS.Promise<Result<bool, string>>
checkDependencies: OperationRequestDto -> JS.Promise<Result<OperationResultDto<DependencyStatusDto[]>, string>>
installDependency: InstallDependencyRequestDto -> JS.Promise<Result<OperationResultDto<DependencyStatusDto>, string>>
getStatus, listRefs, createRef, preflightSwitchRef, switchRef
createRevision, restorePaths
openTextDiff, readTextDiffPage, replayTextDiffPage, expandTextDiff, readTextDiffLine, getTextDiffSourceInfo, closeTextDiff
refreshSynchronization, synchronize
resolveConflict, finalizeConflict, cancelConflict
listObjects, materializeObject, dematerializeObject, materializeObjects, dematerializeObjects
getStoragePolicySettings, setStoragePolicySettings, setPathStoragePolicy, pruneStorage, deduplicateStorage
getRepositoryWebUrl, clearStaleLock
```

The main process sends events to the renderer (`MainToRendererIpc.IVersionControlRendererApi`): `versionControlOperationStarted` with the operation id the renderer sent as soon as the operation is registered, and `versionControlProgress` with float `Completed` and `Total` counters plus a phase code. A third event, `versionControlOutput`, carries a batch of raw output lines of an operation (section 11). Progress and output carry the id of their operation, and they go to the window that started it.

Do not call `Api.ipcVersionControlApi` from feature code. Go through the client, or through `GitWorkflow.GitDependencies` when the sidebar state has to follow.

## 3. Result shapes

Every call returns `Result<OperationResultDto<'T>, string>`. The outer `Error` is a transport failure (the IPC call itself failed). The inner value has three shapes:

```fsharp
type OperationResultDto<'T> =
    | Succeeded of OperationOutcomeDto<'T>
    | PartiallySucceeded of OperationOutcomeDto<'T> * OperationFailureDto
    | Failed of OperationFailureDto
```

`OperationOutcomeDto` carries the value, the effect (`Performed` or `NoOp` with a reason), warnings, affected paths and the resulting revision and workspace version. `OperationFailureDto` carries `Category`, `Code`, `Message`, `StateChanged`, `Retryable`, `AffectedPaths`, `RecoveryAction` (a code plus optional instructions), `Details` and `RevisionEvidence`.

Routing is structural. Code that decides what to do next keys on `Category`, `Code`, `RecoveryAction.Code`, `StateChanged` or `AffectedPaths`. Never classify a failure by its message text.

Categories: `Validation`, `NotFound`, `Concurrency`, `Authentication`, `Authorization`, `DependencyMissing`, `Network`, `Timeout`, `Canceled`, `Conflict`, `Unsupported`, `ProviderError`.

Codes the renderer handles are literals in `VersionControlCodes` (`VersionControlTypes.fs`). Library codes include `identity_missing`, `publish_target_missing`, `target_unreachable`, `precondition_failed`, `conflicts_detected`, `conflict_session_active`, `operation_in_progress` (a rebase, cherry-pick, revert, bisect or unmerged paths block the workspace), `publish_rejected` (the remote refused the push, the message carries its reason), `inspection_timeout` (the state read after an applied update exceeded its deadline), `target_not_empty` and `operation_canceled`. The three codes in the middle reach the user through the generic error modal with the library's message. ARCitISA host codes include `service_unavailable` (the provider has no such optional service), `session_unavailable`, `workspace_unmanaged`, `workspace_ambiguous`, `location_unsupported`, `lock_removal_refused`, `binding_not_persisted`, `transport_error` (the IPC call failed before a structured result existed) and `storage_policy_blocked` (the DataHub ruleset refused a manual storage policy change, see section 8). A threshold outside 1 to 100 MiB is refused with the library's `invalid_lfs_threshold`.

Recovery codes (`VersionControlCodes.Recovery`) tell the renderer which dialog to open after a canceled or partial operation: `remove_index_lock`, `restore_workspace`, `refresh_workspace`, `inspect_workspace`, `abort_merge`, `retry_materialization`, `resolve_conflict_session`, `refresh_conflict_session`, `remove_clone_target`.
When a failure that is not canceled reports a state change, the renderer refreshes the workspace before it shows the error. If that refresh fails too, the error names both failures and the sidebar keeps its old snapshot. A failed clone skips the refresh, because no workspace is open yet.

Helpers on `OperationResultDto` (`tryValue`) cover the common checks.

## 4. Common calls

Refresh the sidebar state:

```fsharp
promise {
    let operationRequest () : OperationRequestDto = { OperationId = VersionControlApiClient.newOperationId () }
    let! status = VersionControlApiClient.getStatus (operationRequest ())
    let! refs = VersionControlApiClient.listRefs (operationRequest ())
    let! settings = VersionControlApiClient.getStoragePolicySettings (operationRequest ())

    match status, refs, settings with
    | Ok(OperationResultDto.Succeeded status), Ok(OperationResultDto.Succeeded refs), Ok(OperationResultDto.Succeeded settings) ->
        Browser.Dom.console.log ($"Ref: {status.Value.CurrentRef.Name}")
        Browser.Dom.console.log ($"Changes: {status.Value.Changes.Length}")
        Browser.Dom.console.log ($"Refs: {refs.Value.Length}")
        Browser.Dom.console.log ($"Threshold: {settings.Value.AutoPolicyThresholdMb}")
    | _ -> Browser.Dom.console.warn ("Could not refresh the version control state.")
}
```

The status carries `WorkspaceVersion`, an opaque optimistic concurrency token. Mutations (`createRevision`, `restorePaths`, `synchronize`, `switchRef`) send it back as `ExpectedWorkspaceVersion`. A stale token fails with `precondition_failed` in the `Concurrency` category and `StateChanged = false`. The workflow then refreshes and runs the write once more for commits, saves, a push that carries no acceptance, branch creation, and settings. It never replays a discard, a restore of interrupted paths, a pull, an accepted synchronize, a branch switch, an abandoned merge, or merge finalization. These actions would act on content or a decision the user has not reviewed, and a branch switch must run its preflight again before it can be repeated. The workflow reports the stale state and refreshes instead.

Primary save in the sidebar is `createRevision` with the exact selected paths, a refresh and one `synchronize` with `PublishLocalRevisions = true`. The library refreshes, updates when the online copy is ahead and publishes. A `synchronize` that answers `publish_target_missing`, as a failure or as the partial result after an applied update, creates the project on the DataHub through `IGitLabApi.createProject`, binds the workspace with `bindWorkspace` and synchronizes again. A `target_unreachable` failure never triggers provisioning.

## 5. Main process structure

`src/Electron/src/Main/VersionControl`:

- `DataHubStrategies.fs`: the credential strategy (token of the account matching the target host, the active account when no host is known, `None` when nobody is signed in) and the identity strategy (commit name and email of that account). Strategies are cheap and side effect free.
- `WorkspaceBindingStore.fs`: the persisted `WorkspaceBinding` per vault root in the app settings, with tolerant decoding.
- `ProviderComposition.fs`: factories, catalog, provider ids, location parsing (`https://` and `ssh://` locations belong to Git, `lakefs://` to lakeFS), the stale lock paths, and `dataHubRevisionPolicy`, the DataHub ruleset as the library's revision policy handed to both factories.
- `VersionControlRuntime.fs`: the process wide runtime (catalog, binding store, resolver).
- `Mappings.fs`: library types to DTOs and back, plus path, ref and revision validation.
- `WorkspaceSessionHost.fs`: sessions, the operation registry, and the per-session settings (threshold and download preference) pushed into the provider when a session opens.
- `VersionControlSettings.fs`: the settings record, its defaults (1 MiB, no download) and the 1 to 100 MiB bound.

`src/Electron/src/Main/IPC/IVersionControlApi.fs` wraps every call: `withSession` registers the operation before the session is opened and sends the started event, `withMutatingSession` also marks the vault busy and refreshes the file tree when the result reports a change, `withService` returns `service_unavailable` when the provider has no such optional service.

## 6. Validation and security rules

Repository paths sent by the renderer are validated in `Mappings.tryRepositoryPath` before any provider call and must be repository relative. Empty values, absolute paths, traversal segments and null characters fail with a `Validation` failure naming the path.

Ref names and revisions are validated the same way. Provider refs are opaque: the sidebar works with names, and requests carry the ref the provider handed out in `listRefs`.

Repository locations are parsed in `ProviderComposition.tryCreateLocation`. Only full `https://`, `ssh://` and `lakefs://` locations are accepted. The Git provider rejects `file://`, `ext::`, `fd::`, protocol overrides such as `-c protocol...`, and SCP style SSH URLs such as `git@git.nfdi4plants.org:group/project.git`. Use `ssh://git@git.nfdi4plants.org/group/project.git` instead.

Credentials are injected per command by the library and are never persisted to repository config. Git runs with `GIT_TERMINAL_PROMPT=0`.

Keep validation in the main process even when the renderer already validates. Renderer validation is for UX, main process validation is the trust boundary.

## 7. Authentication

The Git factory receives a `GitCredentialStrategy` and a `GitIdentityStrategy` built over the DataHub accounts (`DataHubStrategies.fs`). A `RevisionIdentityRequest` names the target host and an optional connection profile. The account is selected by profile when one is present, otherwise by host with the active account preferred, otherwise the active account when no host is known. When no account matches, the strategy returns `None` and the library fails the operation with `identity_missing` or an `Authentication` failure. Tokens are read from `AuthService` at call time, so signing in or switching accounts needs no re-registration.

## 8. Large objects and Git LFS

The library exposes large objects through the object materialization and storage policy services. For Git that is Git LFS.

The threshold for automatic large-object storage and the preference to download large objects are settings of the Electron app, not of the repository. Every session starts from the defaults (1 MiB, no download). The main process keeps the current values in memory per open session and pushes them into the provider when the session opens, so for Git the library keys `versioncontrolservice.lfs.autotrackthresholdmb` and `versioncontrolservice.lfs.materializelargeobjects` in the local repository config only mirror the app values. The sidebar reads them through `getStoragePolicySettings` and changes them through `setStoragePolicySettings`. A change lasts for the open session and is gone when the ARC is opened again. The main process refuses a threshold below 1 MiB or above 100 MiB with `invalid_lfs_threshold`, the code the library uses for the same refusal. lakeFS has no storage policy service, so the values are held by the main process only and the download preference feeds the clone request. Repositories created by earlier ARCitISA versions may still carry `swate.lfs.autotrackthresholdmb` and `swate.lfs.downloadlargefiles` in their local config. ARCitISA does not read them any more.

Renderer state starts with `DownloadLargeFiles = false` until the settings are loaded.

Manual marking follows the DataHub ruleset in `src/Shared/GitLfsRules.fs`:

- `isa.*.xlsx` metadata files must never be tracked with Git LFS. They cannot be marked manually, and a save never turns them into pointers: the composition root hands every provider factory one `RevisionPolicyStrategy` built from the ruleset (`Inline` for metadata files, `LargeObject` for files below a `dataset` folder and for files above 25 MB, `Automatic` otherwise). The Git provider applies it inside its revision transaction and writes the attribute rules it needs. The lakeFS provider accepts the strategy and ignores it. A metadata file that still holds a Git LFS pointer is refused with `inline_content_not_materialized` and the recovery `retry_materialization` until it is downloaded.
- Files below a `dataset` folder must stay tracked and cannot be unmarked.
- Files larger than 25 MB must stay tracked and cannot be unmarked.

The file tree context menu disables blocked toggles, `GitLfsHelper` checks the rules again before calling `setPathStoragePolicy`, and the main process reads the file size itself and rejects a blocked request with `storage_policy_blocked`.

File actions of the explorer: "Download LFS file" calls `materializeObject`, "Free local LFS copy" calls `dematerializeObject`. Both need a clean file. "Clean LFS Cache" (`pruneStorage`) and "Reduce LFS Storage" (`deduplicateStorage`) need a clean working tree. Deduplication can fail on file systems without copy on write support, which is expected and shown to the user. The folder menu offers "Download all LFS files" and "Free all local LFS copies". They call `materializeObjects` with the files below the folder that are not on disk and `dematerializeObjects` with the files that have a local copy, and the session answers `service_unavailable` when its provider offers no batch service. The folder and the files the action takes show the busy state until the call returns, and both entries stay disabled on the folder and on every folder above a running action. Free asks for a confirmation first. When some files fail, the error dialog gives the count of failed files and one line naming the cause for each of the first ten failed files, and the technical details hold the full text.

Each file or folder action allocates its operation id with `newOperationId` before the call and registers itself with `LfsActivityContext.track` as an `LfsRunningAction` until the call settles. The record holds the operation id, the kind (download or free), the target path, whether the target is a folder, the file count, the latest `Count` (files done and files requested), the latest `StepPercent` and `StartedAt`, the `Date.now ()` value from the moment `track` listed the action. The context subscribes to `versionControlProgress` and `versionControlOutput` itself and applies only the events whose operation id belongs to a running action. The output of the actions lives apart from the records, in `LfsRunningLogsCtx`, a map from operation id to `OperationLog` that only the Git sidebar reads. `LfsRunningActionsCtx` changes when an action starts or ends and when its `Count` or `StepPercent` changes, so a batch of output does not render the file tree. `LfsRunningActionsState.forScope` hands back the previous array while the scoped records are the same ones. A folder download reads `Count` from the `lfs-materialize-files` events and `StepPercent` from the `lfs-materialize-transfer` and `lfs-materialize-checkout` meters. Any other event clears `StepPercent`, because the step that it measured is over. A folder free reads `Count` from the `lfs-dematerialize-files` events and has no step percent. The busy row of a folder shows "done/total" with the step percent appended, for example "3/40 45%". Before the first count event it reads 0 of the file count. The library sends count events only for batches of more than one path, so a folder action with exactly one path keeps reading "0/1" until it is done. The busy row of a single file shows no text. `FileTreeMaterialization.overlayBusyDetails` sets this text on the busy rows only, so a progress event does not rebuild the item cache or the path indexes.

Clone and synchronize hydrate large objects when `MaterializeAllObjects` (clone) or the materialize setting (synchronize) is on. A cancel during hydration keeps the update and reports a partial result with `retry_materialization`. The sidebar keeps the pulled state and offers the download again.

The large-file download of a synchronize runs `git lfs fetch` and then `git lfs checkout`, and a clone does the same. A cancel stops the fetch and no checkout starts, so it leaves no `.git/index.lock`. A cancel that arrives during the checkout takes effect when the checkout ends. When the checkout failed and a lock exists, or when Git names the lock in its failure text, the library reports the partial failure `hydration_failed` with the recovery `remove_index_lock`. A failed fetch keeps `retry_materialization` even when a lock exists, because the fetch takes no lock. The library never deletes the lock. When a write ends with that failure, the renderer keeps the message of the failure in `RetryMaterializationAfterLockClear` and opens the "Repository is locked" dialog. The kept message leaves out the lock instructions, because the lock is gone when the offer opens. "Remove lock" runs `clearStaleLock`. When the lock is gone, the renderer opens the "Large files not downloaded" offer from the stored message, and "Download now" runs the download again. The "Download now" retry (`RetryMaterialization`) stores a fixed sentence (`retryLockStoppedMessage`) when a lock stops it, because the library message of that failure says the lock is still held. The offer returns after "Remove lock". A checkout that a lock stopped can leave files that git-lfs had already written while the index entries stay stale. The listing marks such a file as downloaded, because it has the full size, and `git status` lists it as modified. "Remove lock" therefore does more than `clearStaleLock`. When the lock is gone, the workflow lists the objects and takes those that the listing marks as downloaded and the status lists as modified (`staleLfsPaths`). It calls `materializeObjects` with exactly these paths, refreshes, and only then opens the "Download now" offer if one is stored. The library restages the files that hold the committed object and refuses every other path with "has local changes". The workflow shows no error for that refusal, because it means a real edit. A lock or any other failure of the call is routed as usual. "Download now" requests the objects that are not on disk and the same stale paths, and it ignores the same refusal for a stale path. The status has no split between index and working tree. `staleLfsPaths` therefore takes every path with the change kind `Modified` and no old path, and leaves out added, deleted, renamed and conflicted paths. A lock that the preflight of Download Changes finds is not stored. Removing it ends without an offer, and the user runs Download Changes again. Any other end of the lock dialog drops the stored message, because the next Download Changes offers the download again. A Save or Upload Changes whose publish the download held back sets `PendingPostMergePush`, so the publish runs after the retried download. Closing the lock dialog, or the "Download now" offer, without the download also runs the held-back publish, because `DismissRecoveryRequested` resumes it. A clone has no open workspace to show a dialog in. It ends with an error that says the ARC was cloned and a lock blocked the download, and that points to "Download LFS file".

## 9. Refs and the update workflow

`listRefs` returns local and remote refs with their kind. Switching to a remote ref goes through `preflightSwitchRef` and `switchRef` with the opaque provider ref. An unsafe preflight ends the switch with an error that names the paths at risk, without a confirmation. A partial preflight is refused the same way. `createRef` creates and switches to a new local ref.

`synchronize` takes `ExpectedWorkspaceVersion`, `ExpectedTargetRevision`, `AcceptUpdateRisks` and `PublishLocalRevisions`. The pull button sends `PublishLocalRevisions = false`, the push button and the save send `true`. When the update would open a conflict session, the library stops with `update_would_create_conflict_session` (category `Conflict`, the overlapping paths in `AffectedPaths` when there are any, the target revision the preview used as `observed_target` evidence, recovery `accept_update_risks`) and the sidebar opens the merge resolution confirmation. Confirming repeats the write with `AcceptUpdateRisks = true` and the observed target, and the library refuses with `precondition_failed` when the target moved in between, which the sidebar reports without replaying. When the update would change files with local changes, the library stops with `update_would_overwrite_local_changes` (category `Conflict`, the paths in `AffectedPaths`, recovery `resolve_local_changes`). Acceptance does not apply there. The sidebar names the paths and asks the user to save or discard those changes first. A `preview_indeterminate` failure is a retryable provider failure and a decision code without `observed_target` evidence cannot be acted on, so the sidebar reports both as errors. A conflict returns `PartiallySucceeded` with `conflicts_detected` as before. When the publish fails after an applied update and reports no state change, the result is `PartiallySucceeded` with recovery `retry_publish`, and the sidebar offers to publish now. `Publication` is `LocalOnly` when the updated workspace is ahead of or diverged from the target and `PublicationNotApplicable` after a fast-forward. A canceled update returns `Failed` with category `Canceled` and one of the recovery codes `remove_index_lock`, `restore_workspace`, `refresh_workspace`, `inspect_workspace` or `abort_merge`, and the sidebar opens the matching dialog.

## 10. Diff and conflict resolution

The diff page is a paged text diff. `openTextDiff` opens it and returns a handle with the first page. While the library still prepares the sources it answers Scanning with a continuation, which the caller passes to the next `openTextDiff` call. `readTextDiffPage` reads the page after the last one by its cursor, and `replayTextDiffPage` returns an earlier page by its page id. `expandTextDiff` reveals lines of a hidden gap from its start or its end, and `readTextDiffLine` reads a further slice of a long line. `getTextDiffSourceInfo` returns the sizes and encodings of the two versions. `closeTextDiff` closes the handle and succeeds when it is already closed.

The diff runs in worker threads that all windows share (`Main/VersionControl/TextDiffWorkers.fs`). The pool has three workers, and each runs one diff. A fourth open diff closes the longest idle one and takes its slot, and a diff that sat idle for 15 minutes is closed. A call on a closed diff fails with `diff_session_closed`. The registry in `TextDiffHandles.fs` remembers which window opened each handle. It closes the handles of a window that reloads or closes, and the library answers a call on a handle of another window like a call on a closed one.

The renderer (`GitDiffPageLoader` in `GitWorkflow.fs`) keeps at most 8 loaded pages and 8 MiB of page JSON. The other pages are placeholders that keep their row count, and the viewer replays them when they scroll into view. A call that finds its session closed reopens the diff once. The new session reads forward from the first page until a page reaches the line the request needed, which is the first line of the replayed page, the requested line of a slice, or the first line of the last page read for a request of the next page. The view lands on that line.

Once the first page is ready, the renderer reads the remaining pages in the background, one read at a time, so the scrollbar covers the whole diff. The Git sidebar setting "Background indexing limit (MB)" stops that reading. It is 1024 by default, accepts 1 to 1048576 and is held in memory for each open ARC like the Git LFS threshold. From 64 up it is the indexing limit of a diff that uses temp files, as described next. From 1 to 63 it is the memory budget of a diff that stays in memory, as described after that. The renderer adds the UTF-8 size of the JSON of every page the session answered, and the reading stops when the sum reaches the limit. The sum is an upper bound of the temp data that the diff journal of the session keeps, so the limit bounds that journal. A committed version that the diff copies to the temp folder is not part of it. The working copy and Git LFS objects are read in place. The limit never holds back a read the user asks for: scrolling to the end, the continue button, a gap, a long line or a replay. Raising the limit resumes the reading. A background read that finds its session closed pauses the reading. The next request of the user reopens the diff. A status bar above the diff shows the progress while the background reading runs, and it says when the reading stopped at the limit or paused.

A diff keeps its data in memory in two cases. The limit is 1 to 63, and its value in MB is the memory budget. Or the limit is 64 or more, and at Open the free space of the temp drive is below about 1 GB plus 5 % (1024 MiB times 1.05). The budget is then 64 MB. ARCitISA keeps this free space on the temp drive as a policy floor. The check also counts the next page and the blobs still arriving, and several diffs opened together are not reserved collectively, so it is not an absolute guarantee. Open reports the choice in the storage of its result, with the cause. The cause shown for a memory diff comes from the settings at the time of each Open call, so a limit saved across 63 and 64 while an Open is still scanning can name the other remedy. The budget holds the committed file plus one 512 KiB page. A committed Git file in memory may take at most min(16 MiB, budget / 2, budget - 576 KiB). A larger one is blocked, and the blocked page names both sizes and the cause. Files above the Git LFS threshold are LFS objects and are read in place, so this rarely applies. The renderer ignores the indexing limit for a memory diff. A session never switches between disk and memory, so a reopen decides again. A disk diff that reopens while the temp drive is low on space becomes a memory diff. It lands at most at its budget and not at the old position. macOS counts no purgeable space, so memory mode can start while Finder shows more free space.

The renderer marks every background read with `Background` in `readTextDiffPage`. A memory diff refuses a background read once it holds three quarters of its budget, with `diff_memory_budget_reached`. The renderer ends the indexing there and keeps the rows with the continue button. The last quarter stays free for what the user asks for. At the full budget the library refuses every request that writes data, such as a new page or a gap expansion, with the same code. The end row shows a note and no continue button once a page read is refused at the full budget, and the viewer asks for no further page. A refused expansion or slice only marks its control. A reopen is the way to read on, after raising the indexing limit to 64 or more or after freeing space on the temp drive. A disk diff that is open refuses the same requests with `diff_temp_space_low` once the free space of the temp drive falls to that level. The end row shows a note and the continue button, and the first page the user asks for after the space is free clears the stop and resumes the indexing. A background read never clears it. Neither code closes the session, and replays keep working. A gap or a long line that was refused names the reason in the tooltip of its control. A reopen that gets one of the codes lands at the last page it read and keeps the rows. A memory diff at its full budget behaves differently when the replay of an evicted page starts the reopen. The view lands on the replayed page, and the reopen drops the pages after it. The status bar shows the "memory" state again, so reading on is possible until the budget is used up. When the new session refuses its Open or its first read, the diff shows the blocked page instead. That is only possible in a race, because the storage choice at Open leaves room for the first page. An Open that gets one of the codes shows a blocked page. The status bar shows the states "memory" and "stopped" for these cases. It shows "failed" with the message when a read of the next page fails.

The conflict page loads the combined preview of the conflicted item and carries the conflict handle and the workspace version the preview was taken with. Confirming calls `resolveConflict` with the resolved content, then `getStatus`, then `finalizeConflict` when no items remain. A stale handle fails with `refresh_conflict_session` and the page reloads. Abandoning calls `cancelConflict`.

## 11. Busy, progress and cancellation

Mutations run under the vault busy flag so the file watcher does not merge ARCitISA's own writes: `bindWorkspace`, `createRef`, `switchRef`, `createRevision`, `restorePaths`, `synchronize`, `resolveConflict`, `finalizeConflict`, `cancelConflict`, `materializeObject`, `dematerializeObject`, `materializeObjects`, `dematerializeObjects`, `setStoragePolicySettings`, `setPathStoragePolicy`, `pruneStorage`, `deduplicateStorage` and `clearStaleLock`. Nested busy scopes are counted per window, and the flag drops when the outermost scope ends. Read calls, `refreshSynchronization`, the provisioning calls and the dependency calls do not take the flag.

Progress arrives through `versionControlProgress` with float `Completed` and `Total` counters. Clone reports progress to the window that requested it.

The library reports more than counters. Live meters cover fetch, clone, the large-file download of a synchronize and the batch Git LFS download, under the phase codes `fetch`, `clone`, `lfs-pull`, `lfs-pull-checkout`, `clone-lfs-pull`, `clone-lfs-pull-checkout`, `lfs-materialize-transfer` and `lfs-materialize-checkout`. The start of each step arrives as an event with a `DisplayMessage`, such as "Fetching online changes", "Merging online changes", "Downloading large files" or "Checking out large files". Batch calls add the count events `lfs-materialize-files` and `lfs-dematerialize-files`.

The bridge coalesces progress per operation (`ProgressThrottle` in `Main/IPC/IVersionControlApi.fs`). An event goes out at once when its phase code differs from the last forwarded one or when at least 100 ms have passed since the last forward. When the phase changes, the pending event of the previous phase goes out first, because it is the final state of that phase. Any other event waits as the pending event, a newer one replaces it, and it goes out when the window ends. When the operation ends, `runTracked` sends the pending event before it returns the result, so the last state reaches the renderer first.

The Git workflow shows only the events of the operation it runs. `isForeignOperation` compares the root of the event's operation id with the root of `CurrentOperation` (a refresh that shows a notice uses `RefreshOperationId`). The sub-operations of a write have ids of the form `<id>/<n>` and share the root. The workflow drops events of other operations, for example the progress of a file tree LFS action in the same window. A silent refresh and the rename of the ARC folder show no events at all.

The workflow builds the step list of the sidebar from the progress events (`applyProgressToSteps`). A step belongs to one phase code, so the list never has more steps than the operation has phase codes. A batch that repeats its phases, such as the download of several files with one materialization per file, keeps the same few steps. An event with a `DisplayMessage` and neither `Completed` nor `Total` starts the step of its phase code. When the phase already has a step, that step becomes current again and keeps its label. An event with a `DisplayMessage` that is a meter, a label followed by " (k/n)" such as "Downloading LFS objects (0/1), 1.2 MB", starts the step of its phase code when the phase has none, with or without numbers. The step is labelled with the text before the first " (" (`stepLabelOf`), so one file shows the steps "Downloading LFS objects" and "Checking out LFS objects". Every other event changes only the step with the phase code of the event, and that step becomes current. Its detail is the display message when it differs from the label, and its percent is the one of the event, or none when the event has no numbers. A meter without numbers therefore clears the percent of an earlier meter. An event whose phase has no step and that starts none changes nothing. The "Preparing" and "materialized" events of a file are such events, because they carry numbers and no meter. The earlier steps count as done. The headline follows the same list. An event without a display message that starts a new phase, such as the "refresh-state" reset of a synchronize, leaves the headline on the label of the current step. The busy notice shows only while the operation has no step. The sidebar draws the list once it has two steps, because one step repeats the headline. Steps and log belong to the whole operation, while the progress bar starts over with each write phase.

`versionControlOutput` carries the `OperationId` and a batch of entries (`CommandId`, `Kind`, `Text`, `IsPartial`). `Kind` is `command`, `stdout`, `stderr` or `notice`, and `Text` is redacted. The library delivers output only to a caller that sets `OperationContext.ReportOutput`. The main process sets it and forwards each batch to the window that started the operation. The renderer keeps the output in an `OperationLog` (`Renderer/Context/OperationLog.fs`), one for the sidebar operation and one for each running file tree action. A log holds at most 2000 lines and 1,000,000 characters. The oldest lines go first and `Omitted` counts them. A partial entry is a snapshot of a line that the command is still rewriting, such as a progress meter, so an entry replaces the last line of its stream (the same `CommandId` and `Kind`) while that line is partial. The sidebar folds the log behind "Show logs". The button appears only when the log holds a line, so an operation that sent no output shows no button, which is the case for lakeFS sessions. The panel stays at its last line until the user scrolls up.

File tree LFS actions that run next to the sidebar show as background activity. `GitSidebarPanel` reads the running actions with `useLfsRunningActionsCtx` and their output with `useLfsRunningLogsCtx`, and maps each action to a `GitSidebarBackgroundActivity`. Its label reads "Downloading <name>" or "Freeing <name>". Its detail and percent are the text of the file tree row, and the output of the action becomes its log. The sidebar shows all of them in one info notice with one "Show logs" panel, where the lines of an action stand under its label. An action gets a bar when it has a percent. For a folder that is the percent of the current step while Git LFS reports one, and otherwise the share of finished files, so the bar moves with the detail text. A write of the sidebar that takes the mutation lock of the library and starts while such an action runs waits until the action ends. These writes are the commit of a Save, Download Changes, Upload Changes, the discard, the restore of interrupted files, creating and switching a branch, the steps of a merge resolution and "Download now" (`waitsForFileTreeActions` lists them). Refresh, fetch, clone, the LFS settings, the cache maintenance and the lock removal do not take the lock and do not wait. The buttons stay enabled and nothing refuses the request. While a write waits, `waitingHeadline` replaces the busy headline with "Waits for <label> to finish", with the first letter of the label in lower case. The label is the one of the first running action that started before the sidebar operation did, because only such an action can hold the lock. `LfsRunningAction.StartedAt` and `GitState.OperationStartedAt` hold the start times, both from `Date.now ()`. Without an earlier action the headline stays as it is. The headline covers the wait before the first report of the operation, which `GitState.OperationReported` records after any progress or output of its own. The flag survives the phase changes of a write and resets when a busy operation starts or ends. A Save that has reported its commit and then waits for an action that started later shows its own headline, and the background notice still names the action. After the first event the normal notice and the step list take over.

Every operation can be canceled with `cancelOperation` and the operation id from `versionControlOperationStarted`. The running-operation registry stores each operation id with its workspace root. Cancellation kills the underlying process. The library then restores a clean state where it can and reports what is left through the recovery code.

A stale `.git/index.lock` left by a killed process is removed by `clearStaleLock`, which refuses with `lock_removal_refused` while another operation runs in that workspace. After clearing, an open merge is abandoned through the normal path.

## 12. Extending the functionality

When adding an operation:

1. Add or reuse DTOs in `VersionControlTypes.fs`. No Git or lakeFS type may appear in a DTO.
2. Add the function to `IVersionControlApi` in `IPCTypes.fs` with a request DTO that carries `OperationId`.
3. Implement it in `src/Electron/src/Main/IPC/IVersionControlApi.fs` through `withSession` or `withMutatingSession`, and map through `Mappings.fs`.
4. Add the wrapper in `Renderer/VersionControlApiClient.fs`.
5. Wire it into `GitWorkflow.GitDependencies` when the sidebar state needs it.
6. Add tests in `tests/Electron.Core` (mappings, session host or IPC over a real repository) and `tests/Electron.Renderer` (workflow).
7. Update this guide.

Provider specific behavior belongs in the library or in the composition root. Do not branch on a provider id anywhere else.

## 13. Requirements and troubleshooting

The library requires Git 2.42 or newer and Git LFS 3.7 or newer. Git LFS 3.7 reads the index in `git lfs checkout` only with Git 2.42 or newer, which a conflict pick of an LFS file needs. `checkDependencies` reports every component with `Installed`, `Compatible` and a remediation text. The sidebar shows the remediation when a component is missing or too old. Only the Git LFS configuration component can be installed through `installDependency`, which the sidebar offers when an operation fails with category `DependencyMissing`.

`Authentication` or `Authorization` failures on synchronize:

- Confirm an account is signed in for the target host and its token is valid.
- Confirm the remote URL is `https://` or full form `ssh://`.

Clone fails although the repository is public:

- When a token is available, clone runs authenticated and does not retry without it.
- The target must be missing or an empty directory (`target_not_empty` otherwise).

`workspace_unmanaged` after opening a folder:

- The folder is not under version control. The sidebar offers to initialize a repository.

`workspace_ambiguous`:

- More than one provider claims the folder. Remove the state of the provider that is not wanted, or bind the workspace explicitly.

Path rejected:

- Use repository relative paths with `/`. Do not pass absolute paths, `.` or `..` segments, empty values or null characters.
