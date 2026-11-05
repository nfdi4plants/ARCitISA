# Plan: keep ARC files and memory in sync

**Status:** proposed. Nothing in this plan is implemented yet.
**Base:** `feature/AddNewFileFocus` at `33a3b1530`. All line numbers refer to that commit.
**Delete this file before the branch merges.**

## Working rules for this branch

- Commits are allowed on `feature/RobustArcFileSync`, without `Co-Authored-By` or any other attribution line. The maintainer granted this exception to the Git rule in `AGENTS.md`. It does not cover pushing, so ask before pushing.
- The branch stacks on #1458, which is still open. Rebase it onto `main` once #1458 merges.
- Do not stage `.github/PULL_REQUEST_TEMPLATE/default.md`. That uncommitted deletion is not part of this work.
- **Slimming rule:** keep workarounds for ARCtrl bugs. Remove any other sync code this design makes unnecessary.
- `AGENTS.md` conventions apply: unit tests in `.test.fs`, React Testing Library tests in `.test.rtl.fs`, no tests that search source files, no edits to transpiled `.fs.js` files, `swt:fluent--...` icons, typed bindings instead of `?`.
- Stage only the files of the current phase. The `electron` CI job must pass at the end of each phase.

## Goal

1. An external change to a file wins over unsaved memory for that file.
2. Our own writes are never treated as external changes.
3. While external changes load, edits are rejected and a notice says so.
4. The renderer shows the result as soon as the change is in memory.

## What is wrong today

| # | Problem | Evidence | Checked by |
|---|---|---|---|
| 1 | Our own writes come back as external changes. Suppression lasts 500 ms (`ArcVaultState.fs:100`), but chokidar holds events until a file is stable for 2000 ms (`ArcVaultHelper.fs:398`). No other filter exists. The merge then replaces the entity with the disk copy, even with local edits (`ArcMergeLibraryExtensions.fs:118`). | Own-write events arrived 2.08–2.33 s after the write, with polling and with native events (chokidar 3.6.0, app settings, plain files). | Timing measured. Edit loss read in code, **not reproduced in the app**. |
| 2 | External events that arrive during a write or the 500 ms after it are dropped from the merge with no retry. They only update the tree (`ArcVaultFileWatcher.fs:522-531`, `queueFileWatcherEvent`). Memory stays stale with no signal. | Code | Read |
| 3 | A stale page can overwrite an external change. Each edit sends the whole entity (`ArcFilePreviewTarget.fs:26-38`), and main replaces its copy (`updateARCByFileContentDTO`). Pages only refresh on `change` events (`ArcVaultFileTree.fs:208`) whose path equals the selection (`FileTreeExpansionContext.fs:550-555`), but an entity page opened from its folder has the folder as its selection. The next edit puts the old content back, and the next Save writes it to disk. | Code | Read, **not reproduced** |
| 4 | `AddArcFile` reloads the whole ARC after writing (`ArcVault.fs:130`). One unreadable workbook anywhere makes it return an error although the file is written and committed to memory. The tree update is skipped, and a retry reports that the entity already exists. | Code, plus the batch fold in ARCtrl `ContractIO` | Read |
| 5 | `syncArcStaticHashes` moves every entity's hash without its content (`InMemoryChanges.fs:114`). Combined with problem 2, a missed external edit shows as unsaved and the next Save overwrites it. | Code, plus `GetUpdateContracts` | Read |
| 6 | Git operations run in the same busy scope as our writes (`IVersionControlApi.fs:233`, `:577`). Events arriving during a long operation fall under problem 2. | Code. Which Git operations change the working tree still has to be listed. | Read |
| 7 | Merge failures are only logged (`ArcVaultFileWatcher.fs:441`). The user never learns that memory is behind. | Code | Read |
| 8 | Writes are not blocked during a merge. The merge notices afterwards and retries, which needs a generation counter, an epoch, a deferral counter with a limit and a test barrier. | Code | Read |

## Decisions

The first column is what this plan assumes. Items marked **confirm** differ from or refine the maintainer's first thoughts.

| # | Decision | Why |
|---|---|---|
| D1 | Disk wins **per file**, not for the whole ARC. **Confirm.** | An external edit to study S should not throw away unsaved edits to assay A. `ARC.merge` already works this way, and merging inside a file stays out of scope. |
| D2 | Own writes are recognized by **comparing files**, not by time. **Confirm.** | "Drop events during a write" cannot catch own events, because they arrive more than 2 s later. A longer window would drop real external edits for longer. Comparing gives the intended result: files changed during our write count as ours. |
| D3 | Reject **all** edits during a sync, as requested, but start a sync only when files really differ. | Without the check, every Save would flash "external changes detected". |
| D4 | A file that was touched but has identical content keeps local edits. **Confirm.** | Sync tools and `git checkout` can change the modified time without changing the content. Discarding unsaved work then would be a pure loss. |
| D5 | Git operations and imports are **not** own writes. They hold the lock, then trigger a normal sync. | Their changes must reach memory. |
| D6 | When disk replaces an entity with unsaved edits, the notice names it. | Otherwise work disappears silently. |
| D7 | Keep writing first and committing to memory afterwards, as `TryAddArcFileAsync` does. | A failed write then leaves memory clean. |

## Design

### State on `ArcVault`

```fsharp
type FileFingerprint = { SizeBytes: float; MtimeMs: float }
type ArcSyncMode =
    | Idle
    | Writing of depth: int
    | Syncing

// The file versions that memory was last in sync with.
KnownDisk: Map<PathKey, FileFingerprint>
// Grows each time a sync changes memory.
ArcRevision: int
// A sync was asked for while it could not run.
SyncRequested: bool
```

### Invariants

1. **One at a time.** `Writing` and `Syncing` exclude each other. A write requested during a sync is refused with the existing "still saving" style of error, and a sync requested during a write runs when the write ends.
2. **`KnownDisk` changes only** at ARC load, at the end of an own write, and at the end of a successful sync.
3. **A static hash moves only together with content.** Replacing an entity from disk takes the disk hash. Writing an entity takes the memory hash. No other entity's hash moves.
4. **Every edit names its revision.** Main refuses edits based on an older revision.

### The sync

There is one entry point: `RequestSync(announce)`. The watcher, window focus, a watcher restart, Git, import and delete all call it, and they no longer pass event lists.

```text
RequestSync:
  if Writing or Syncing: SyncRequested <- true; return
  before <- scan()
  if diff(KnownDisk, before) is empty: return          // own writes end here, with no notice
  mode <- Syncing; if announce: send ArcSyncStarted
  try
    snapshot <- ARC.LoadAsyncSwate
    after <- scan()
    if after <> before: retry the load (at most 3 times)
    events <- diff(KnownDisk, after) as FileEvents
    memory <- ARC.merge memory snapshot events
    KnownDisk <- after; ArcRevision += 1
    send ArcSyncFinished { revision, changedPaths, replacedUnsaved }
  with error:
    // KnownDisk is unchanged, so the next request tries again
    send ArcSyncFailed { message }
  finally
    mode <- Idle; if SyncRequested: run again
```

`scan()` lists structural files with `discoverStructuralArcFilePathsWithAsync` and stats each one. It reads no workbooks. A path that disappears during the scan counts as missing.

If the vault's root changes during a sync (root rename, close), the sync discards its result. Use one token for this in place of `WatcherEpoch` and `WriteGeneration`.

### Write scopes

`WithBusyWritingScope` splits in two:

- **`WithOwnWriteScope`** for Save, add, rename and ARC creation:
  1. `pre <- scan()`, run the operation, `post <- scan()`.
  2. Add to `KnownDisk` only the entries that differ between `pre` and `post`.
  3. Anything that already differed from `KnownDisk` before the write stays external and loads in the next sync.
- **`WithExternalOperationScope`** for Git, LFS and import: holds the lock and does not touch `KnownDisk`, then calls `RequestSync` at the end.

Both end with `RequestSync` when `SyncRequested` is set. Writers need no knowledge of which files they touch, which is what lets the ARCtrl-driven delete and rename fit the same code.

**Known limit:** a file someone else changes while our write runs counts as ours. That is the "drop during write" rule, limited to the duration of the write. It stays until that file next changes.

### Watcher

The watcher becomes a doorbell. On each debounced batch:

1. Call `RequestSync(announce = true)`.
2. Publish the tree events, as now, through `ApplyWatcherFileTreeEvents`. If a write is running, publish them immediately. A file then visible before its entity is merged opens through the disk fallback of `openFile`, which is already accepted behaviour (`ArcVaultFileWatcher.fs:472-478`).

Add a `RequestSync` on window focus, next to the existing focus revalidation. It costs only a stat scan, and it covers missed events and a watcher that is down.

**Keep `IsLoadingChanges` as it is.** `GitWorkflow.fs:6335` uses it to refresh Git state, and Git status has to update after our own Save too. Do not reuse it for the new notice.

### Merge

Keep `ARC.merge` and its per-entity handling. It already implements disk wins, and it preserves DataMaps when only the entity file changed. Changes:

- Its events now come from the file diff.
- D4: skip the replacement when the disk entity's hash equals the local `StaticHash`.
- Invariant 3: replace `syncArcStaticHashes snapshot mergedArc` in `PublishWatcherSnapshot` with hash copies for replaced entities only. Copying hashes in `copyArcPreservingStaticHashes` stays.
- D6: report the entities that had `hasInMemoryChanges` when replaced.

### IPC

| Direction | Message | Purpose |
|---|---|---|
| Main to renderer | `ArcSyncStarted` | Show the notice and block input |
| Main to renderer | `ArcSyncFinished { revision; changedPaths; replacedUnsaved }` | Reload affected pages and unblock input |
| Main to renderer | `ArcSyncFailed { message }` | Unblock input and show that memory is behind |
| Renderer to main | `setArcFileInMemory(request, baseRevision)` | Refused when `baseRevision` is old or a sync is running |

### Renderer

- **Notice:** "External changes detected, waiting…", styled like `FileWatcherNotice`.
- **Input:** make the main content `inert` during a sync, with a typed binding. This blocks every editor without touching each one.
- **Refused edit:** the page already shows the edit, so always reload it from main and tell the user the edit was not applied. Otherwise the next accepted edit would resend it inside the whole entity.
- **`ArcSyncFinished`:** reload the open page if its file is in `changedPaths`, compared on the workbook path and not on the selection, which fixes problem 3. Adopt the new revision only **after** that reload finishes, so a stale page can never send an accepted edit.
- **Double reloads:** remove ARC metadata files from `PublishFileContentChanges`. Otherwise a Save still reloads the page through `useReconcileSelectedPreview`, which can drop an edit that is still on its way to main.

Notes and other text files keep their existing content stamp path.

## What stays and what goes

**Keep (ARCtrl workarounds):**

- `TryWriteAsyncSwate`, `GetWriteContractsSwate`, `GetUpdateContractsSwate` (#618, `WriteAsync.fs`)
- `LoadAsyncSwate` (#619) and `LoadAsyncSwateZeroByteRepair` (#620, `LoadAsync.fs`)
- `DataMapCopyWorkaround`
- `cleanEmptyDataMapStaticHash`, because ARCtrl treats a hash of 0 as new
- `ArcFileCreateContracts` and the scoped add contracts

**Keep (not sync logic):** `migrateLegacyDataMapPathsAsync`, `FileTreeWriter` and tree reconciliation.

**Remove:**

| Code | Where | Replaced by |
|---|---|---|
| Own-write suppression timer, `IsFileWatcherArcMergeEligible` | `ArcVaultState.fs` | File comparison |
| `writeGeneration`, `watcherDeferralCount`, `watcherEpoch` | `ArcVaultState.fs` | Mutual exclusion and one root token |
| `WatcherMergeOutcome`, `TryApplyWatcherArcMergeIfEligible`, `WatcherMergeBarrier` | `ArcVaultFileWatcher.fs:30-36`, `:133-196` | `RequestSync` |
| Deferral handling in `_FileEventController` | `ArcVaultFileWatcher.fs:349-539` | A short doorbell |
| Import marks and leases | `ArcVaultFileWatcher.fs:198-347` | File comparison and a sync after import |
| Synthetic events in `EnqueueWatcherResync` | `ArcVaultFileWatcher.fs:810-859` | `RequestSync`, keeping the tree revalidation |
| `fileWatcherPendingArcMergeEvents`, `toArcMergeEvents`, `normalizeAgainstDisk`, `ArcMergeQueue` | `WatcherHelpers.fs`, `ArcVaultHelper.fs:146` | File diff |
| `TryTriggerArcInMemoryMergeOnFileWatcherEvents` | `ArcVault.fs:54-70` | `RequestSync` |
| Reload and sync calls in `AddArcFile`, `syncAddedArcFileFromPersisted` | `ArcVault.fs:130-135`, `ArcVaultHelper.fs:45` | Memory hash on commit |
| `mergeDeletedEntityFromDisk` | `Delete.fs:65` | Silent `RequestSync` |
| Second reload in rename | `Rename.fs:55` | See phase 4 |

Check each removal for other callers before deleting it. Startup buffering (`BufferedFileWatcherEvents`) stays for now, because the tree still needs it.

## Phases

Each phase leaves the app working and ends in a commit.

### Phase 0: pin the bugs

Write failing tests first, using the temp ARC and fake watcher in `tests/Electron.Core/ArcVaultHelper.test.fs`:

- Save, edit in memory, then deliver the own-write event: the edit survives.
- Add with an unreadable workbook elsewhere in the ARC: the add succeeds.
- External edit whose event arrives during a write: it loads after the write.

If the maintainer's manual check shows that typing after a Save is **not** lost, find out why before phase 2, because problem 1 would be misunderstood.

### Phase 1: file comparison

- New pure module `ArcVault/ArcDiskState.fs`, placed before `ArcVaultState.fs` in the fsproj: `FileFingerprint`, `scan`, `diff`.
- Unit tests in `tests/Electron.Core/ArcDiskState.test.fs` for add, change, unlink, case-insensitive keys and a file vanishing during the scan.
- **Measure `scan` on a large ARC.** If 1000 entities take more than about 100 ms, stop and report before continuing, since each own write scans twice.

### Phase 2: one sync path

- Add `ArcSyncMode`, `KnownDisk`, `RequestSync` and the two write scopes, and seed `KnownDisk` from a scan taken **before** the initial load.
- Move every busy scope caller to the right scope. List which Git operations change the working tree, and treat any doubtful one as external.
- Turn `_FileEventController` into the doorbell.
- Apply the merge changes for D4 and invariant 3.
- Remove the first group of the table above and rewrite the affected tests. The deferral tests (`ArcVaultHelper.test.fs:1257-1563`) go away with the code they cover.
- Phase 0 tests one and three now pass.

### Phase 3: `AddArcFile`

- Remove the reload and the three sync calls. Keep the tree reconcile.
- In `commitAddedArcFile`, set `StaticHash` from the memory copy, as the DataMap case already does (`ArcAddExtensions.fs:120`).
- Leave the ARC-level hash alone. It covers registered identifiers and not the entity lists, and the add path calls `AddAssay` and friends without registering. The test `adds the file explorer default assay via vault add path` catches a false unsaved state if this is wrong.
- Remove the helper and its test (`ArcVaultHelper.test.fs:1906`).
- Phase 0 test two now passes.

### Phase 4: delete and rename

- **Keep the first reload in both** (`Delete.fs:118`, `Rename.fs:95`). ARCtrl generates its contracts from the ARC it is given, so running it on the disk state stops unrelated unsaved edits from being written (`17e70e46b`).
- **Delete:** replace `mergeDeletedEntityFromDisk` with a silent `RequestSync`. Files ARCtrl updated for cross references then load through the same path.
- **Rename:** cannot use the sync, because that would lose unsaved edits on the renamed entity. Keep `applyInMemoryRename` and `remapArcFileSystemPaths`, run under `WithOwnWriteScope`, and replace the second reload: if the entity had no unsaved edits, set its hash from memory. If it had, leave it unsaved.
- **Check cross references first.** Rename and delete can change other files on disk. Test that memory matches disk for a study that references a renamed or deleted assay. If it does not, keep the rename reload and record why.

### Phase 5: renderer

- Add the IPC messages and the `baseRevision` check.
- Add the notice, `inert`, page reload, refused-edit handling and the `PublishFileContentChanges` filter.
- Add React Testing Library tests in `.test.rtl.fs` for: a notice during a sync, no notice for an empty diff, a folder-selected entity page refreshing, a refused edit reverting the page, and the naming of replaced unsaved edits.
- **Ship this with phase 2 if the branch will be used in between.** Until it lands, problem 3 can still put stale content back.

### Phase 6: Git and import

- Move import to `WithExternalOperationScope`, then remove the import marks and leases.
- Tests: a Git operation that changes a workbook reaches memory, and import of structure files reaches memory with no notice beyond its own.

### Phase 7: finish

- Remove remaining dead code.
- Manual check, in the app, with the running build:
  - Save, then type.
  - Add a study, then type.
  - Edit an open assay in Excel and save.
  - Edit with unsaved changes in another entity.
  - `git pull` with a metadata change.
  - Save a broken workbook externally, then fix it.
- Add a user-facing line to `CHANGELOG.md` under `[Unreleased]`, stating that external changes now replace unsaved edits to that file.
- Delete this plan.

## Test list

Test observable behaviour only.

| Scenario | Expected |
|---|---|
| Own Save, event 2 s later | No load, no notice, edits kept, not marked saved |
| External change to entity with unsaved edits | Disk version in memory, not unsaved, entity named in the notice |
| External change to another entity | Local edits kept and still unsaved |
| File touched, content identical | Local edits kept |
| External edit made before a write, event during it | Loaded after the write |
| External delete and external add | Entity removed or added |
| Unreadable workbook | Memory unchanged, failure shown, retried and recovered once fixed |
| Add with unreadable workbook elsewhere | Add succeeds |
| Edit with an old revision or during a sync | Refused and the page reverts |
| Watcher down, window focus | External change loads |
| Root rename during a sync | Result discarded |
| Save during a sync | Refused with a clear message |

## Risks

- **Modified time granularity.** On FAT and some network drives, a same-size edit within the time resolution is missed until the next change. Accepted.
- **ARCtrl writes finishing late.** If a file changes after the write promise resolves, it counts as external and disk wins. Phase 0 test one covers the normal case. Watch for this on network drives.
- **No more echo after writes.** Today the own-write merge also replaces memory with the disk form. After this change, memory stays in its pre-round-trip form until the next full replacement, and the two can differ slightly, for example in long table names. Check that a page looks the same after a reopen.
- **One bad workbook blocks every sync.** `LoadAsyncSwate` fails as a whole. This is the case today too, and it is now visible to the user. Loading only changed files could remove it later and is out of scope here.
- **Full load per external change.** Kept for simplicity. It now runs only for real external changes.
- **Long external operations delay sync.** A long Git or LFS operation holds the lock, and memory catches up when it ends. The tree keeps updating meanwhile.
