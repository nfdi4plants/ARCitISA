# Review of ArcFileSyncPlan

Reviewed on 2026-11-05 against the working copy of [ArcFileSyncPlan.md](ArcFileSyncPlan.md), which now marks phases 0 and 1 as implemented. This review concerns the remaining design and its interaction with the current code. The proposed sync implementation has not been exercised in the app; the interleavings below are design counterexamples, supported by source inspection and the runtime checks listed at the end.

The single sync path, per-file policy, and removal of time-based suppression address the reported problems. Before phase 2 proceeds, the plan needs to resolve the following seven issues. P1 findings can cause lost changes or block normal operations; P2 identifies incomplete coverage of a structural file type.

## 1. [P1] Acquire sync ownership before the first scan

**Plan location:** “The sync”, from the busy check through `mode <- Syncing`; removal of `ArcMergeQueue` and `WriteGeneration`.

`scan()` is asynchronous, but the pseudocode leaves the mode `Idle` while awaiting it. Two requests can both pass the busy check and scan concurrently. A Save can also acquire the write scope after the sync's busy check and before its scan completes. The first request then sets `Syncing` without checking that a writer or another sync already owns the vault.

This invalidates the mutual exclusion that is supposed to replace the existing queue and generation checks. In particular, a snapshot can be loaded while our own write is incomplete and later replace edits with an intermediate disk state. The current [write scope](../../src/Electron/src/Main/ArcVault/ArcVaultState.fs#L294) explicitly establishes its busy flag before yielding for this reason.

**Change requested:** reserve ownership synchronously before any scan, and hold it through scanning, loading, publication, and cleanup. The renderer notice can still wait until a nonempty diff is found. Put the initial scan inside the error-handling boundary as well: currently a discovery/stat failure before `try` bypasses `ArcSyncFailed`. Apply the same ownership rule to pre/post scans in write scopes.

**Regression cases:** gate the initial scan, request another sync and a Save, then release it. Assert that only one operation owns the vault and no snapshot overlaps a write. Reject the initial scan and verify that failure is reported and the next operation can run.

## 2. [P1] Protect external changes detected before an own write

**Plan location:** “Write scopes”, `WithOwnWriteScope` steps 1–3.

Keeping a preexisting difference out of `KnownDisk` does not protect its contents from the operation that follows. Consider a dirty assay in memory, followed by an external edit of the same workbook before the watcher event arrives. The Save pre-scan detects the difference, but the plan still runs Save. [WriteArc](../../src/Electron/src/Main/ArcVault/ArcVault.fs#L90) writes the dirty memory copy over that workbook. A subsequent sync can only load our replacement; the external version has already been lost.

This is outside the explicitly accepted limitation for external changes made *during* an own write: the external change was already present and detected before writing began. It contradicts “external change wins” even if a sync is guaranteed afterward.

**Change requested:** resolve or refuse an overwrite of a path with a preexisting external difference before writing that path. For example, Save can sync first and require a retry after the renderer refreshes. Preserve the scoped-add requirement: an unreadable unrelated workbook must not make adding a different entity fail. That may require distinguishing the paths an operation can overwrite rather than relying solely on global pre/post comparisons. A detected preexisting difference should also request reconciliation without depending on another watcher event.

**Regression case:** dirty assay A, externally change A, immediately Save before delivering its watcher event. Assert that the external contents survive on disk and reach memory. Keep a separate test for an external change to B while adding A.

## 3. [P1] Reload pages by their data dependencies, not one path

**Plan location:** “Renderer”, `ArcSyncFinished` reload rule and the claim that phase 4b makes an ARC page's selection always its file.

One ARC editor represents more than one disk file. [DataMap path resolution](../../src/Shared/PathHelpers.fs#L131) deliberately opens the parent workbook, and [the editor](../../src/Components/src/Page/ArcFileEditor/ArcFileEditor.fs#L192) edits its embedded DataMap. A workbook DTO includes that DataMap, and [updateARCByFileContentDTO](../../src/Electron/src/Main/ArcVault/ArcVaultHelper.fs#L97) replaces the parent entity with the DTO's whole entity.

If `isa.datamap.xlsx` changes while the parent workbook is selected, `changedPaths` contains the DataMap path, so the proposed exact-path reload is skipped. The page adopts the new revision with its old DataMap. Its next metadata or table edit can therefore overwrite the freshly merged DataMap in main, and the next Save writes that stale DataMap back to disk. Existing tree reconciliation detects DataMap presence mismatches, not edits to an already present DataMap.

The reverse selection also matters: clicking the DataMap selects its path but opens the parent entity. Phase 4b removes folder aliases; it does not remove this alias. A workbook change while that selection is active can be missed too.

**Change requested:** define the files represented by each open page, including the parent workbook and its DataMap, and reload when any of those dependencies changes. Use the page's actual entity identity rather than assuming selection and preview paths are identical. Preserve unsaved data belonging to unchanged files when constructing the refreshed page.

**Regression cases:** externally edit an existing DataMap with its parent workbook selected; externally edit the workbook with its DataMap selected. After refresh, make another editor change and verify that neither external update is reverted in memory or on Save.

## 4. [P1] Bind revisions to the content returned by reads

**Plan location:** “IPC” and “Renderer”, `baseRevision` and adoption after reload.

The protocol versions writes and sync notifications, but it does not version the content returned by `openFile`. Its current [response type](../../src/Electron/src/Swate.Electron.Shared/IPCTypes.fs#L185) contains only `FileContentDTO`. [Opening a preview](../../src/Electron/src/Renderer/Components/LeftSidebar/FileExplorer/FileTree.fs#L265) and [reloading one](../../src/Electron/src/Renderer/Components/LeftSidebar/FileExplorer/FileTree.fs#L285) publish asynchronous results without a request-generation check.

For example, a read of A at revision 0 can be pending when a sync updates A to revision 1. If A is not yet the active page when `ArcSyncFinished` is handled, the renderer can adopt revision 1 without reloading it. The earlier read can then install revision-0 content, which the next edit sends with revision 1. Two sync-triggered reloads resolving out of order create a similar problem. Merely waiting for each reload to finish does not establish which snapshot its result represents.

**Change requested:** return content and its revision together from main, and keep `baseRevision` with that page snapshot. Discard stale read/reload results using a request generation and page identity; prevent revision rollback between overlapping completions. Bootstrap current revision and sync status when the renderer connects or reloads, rather than depending entirely on notifications it may have missed. The existing [MainSyncedState hook](../../src/Electron/src/Renderer/MainSyncedState.fs#L34) illustrates the request and live-update guards already used elsewhere.

**Regression cases:** delay an ordinary open across a sync; resolve two refreshes in reverse order; navigate away while a refresh is pending; reconnect the renderer after the main revision has advanced. No stale snapshot should be editable with a newer revision.

## 5. [P1] Make own-write acknowledgment depend on successful memory commits

**Plan location:** “Write scopes”, acknowledgment of all pre/post differences; invariant 3; D7.

The scope does not define how failure changes its bookkeeping. ARCtrl contract batches are not transactions: successful contracts remain on disk when another contract fails. [TryAddArcFileAsync](../../src/Electron/src/Main/ArcMerge/ArcAddExtensions.fs#L158) commits the entity to memory only when the whole batch succeeds. If the workbook is written but another create contract fails, a scope that acknowledges every pre/post difference records that workbook in `KnownDisk` even though it is absent from memory. Its delayed watcher event then has an empty diff and cannot repair the divergence.

Save has a related existing hazard that the new invariant must address. [WriteArc](../../src/Electron/src/Main/ArcVault/ArcVault.fs#L98) generates update contracts on the live ARC before attempting disk writes. The consumed ARCtrl implementation changes `StaticHash` while generating those contracts. A failed write can therefore leave the entity marked clean; generating contracts for a retry then produces no write. A stat comparison alone cannot restore the correct saved baseline.

**Change requested:** specify success, partial failure, and exception behavior for both disk fingerprints and static hashes. Acknowledge only disk versions whose corresponding content has actually been committed to memory. Reconcile partial disk effects as changes requiring a sync. Generate contracts from a suitable snapshot or restore failed-write baselines so an unsuccessful Save remains retryable, while preserving edits made during the write.

**Regression cases:** fail a later add contract after its workbook succeeds, then deliver the watcher event; fail a Save write and retry it; partially succeed a multi-entity Save. Assert memory/disk agreement, accurate dirty flags, and that retry performs the missing writes.

## 6. [P1] Give ARC creation a separate initial baseline

**Plan location:** “Write scopes”, inclusion of ARC creation in the ordinary pre-scan flow.

The new ARC root normally does not exist before creation. [The create IPC handler](../../src/Electron/src/Main/IPC/IArcVaultsApi.fs#L280) combines a selected container directory with the requested identifier; [CreateARC](../../src/Electron/src/Main/ArcVault/ArcVault.fs#L305) relies on the write contracts to create that directory. The phase-1 [scan implementation](../../src/Electron/src/Main/ArcVault/ArcDiskState.fs#L47) intentionally propagates a missing-root error.

Putting that creation operation inside the specified `pre <- scan()` scope will fail before it writes anything. Treating all missing roots as empty would solve creation but would be unsafe for an already open ARC, where it could look like every entity was deleted.

**Change requested:** explicitly initialize a creation scope with an empty baseline for a validated new target, or otherwise establish the creation directory before its first scan. Keep missing-root errors for existing vaults. Describe when the created ARC's fingerprints become the initial known baseline.

**Regression cases:** create an ARC beneath an existing container when the target directory does not exist; separately remove an open ARC root and request sync. Creation must succeed, while the latter must report failure without removing in-memory entities.

## 7. [P2] Include license files in per-file merging

**Plan location:** “The sync”, structural-file discovery, and “Merge”, keeping the existing per-entity handling.

[Structural discovery includes license files](../../src/Shared/PathHelpers.fs#L269), so they participate in the proposed fingerprint diff. However, [ArcEntityRef.fromPath](../../src/Electron/src/Main/ArcMerge/ArcEntityRefHelper.fs#L9) classifies them as `Unknown`, which the dirty-ARC merge ignores. An external `LICENSE.md` change while another entity is dirty therefore leaves `ARC.License` stale. The proposed sync nevertheless acknowledges its fingerprint, so subsequent requests cannot detect the unapplied change.

There is also unwanted coupling in the other direction: [copyInvestigationFields](../../src/Electron/src/Main/ArcMerge/ArcMergeLibraryExtensions.fs#L65) copies `License` when the investigation workbook changes, although the license is a separate file. That does not implement D1's per-file boundary.

**Change requested:** give license changes explicit add/change/delete handling, with their own content and hash baseline, and stop copying license state solely because the investigation changed. If licenses are deliberately excluded from this feature, narrow both the scanner and the stated guarantees consistently rather than acknowledging changes that the merge ignores.

**Regression cases:** edit or delete the license externally while an assay has unsaved changes; change only the investigation workbook while license memory differs. Assert the intended per-file results and that unrelated dirty content survives.

## Verification and scope

Source inspection covered the watcher, write scopes, merge helpers, add/delete/rename paths, structural discovery, preview loading, and editor DTO flow. Small read-only runtime probes against the existing generated modules confirmed:

- A license-only event with dirty investigation memory retains the old license in the current merge.
- Generating an assay update contract marks it clean before fulfillment; generating contracts again without writing produces no retry contract.
- A parent workbook DTO round-trips its DataMap, and a DataMap selection resolves to its parent workbook preview.

These checks establish current behavior used by the findings. They do not validate the proposed implementation or replace the regression tests described above. No application build or full CI run was needed for this documentation-only review.

The accepted timestamp-resolution limitation, the explicitly accepted external edit during an own write, and the deferred optimization to load only changed workbooks are not additional findings here.
