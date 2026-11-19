# Review of `feature/text-editor`

Scope: everything on `feature/text-editor` compared with `main` (13 commits), plus the uncommitted and untracked work as of 2026-11-19, which includes the shared `EditorToolbar`. Generated `*.fs.ts` files were not reviewed.

How it was reviewed:

- Two `/code-review` passes at xhigh, both looking for correctness bugs and rule breaks. The first covered the 6 newest commits and the working tree. The second covered the 7 older commits that are already on `origin/feature/text-editor`.
- One Opus reviewer looked only at three things: complex fixes that may turn brittle, robustness, and whether code is grouped by domain.

Each finding names its source and how sure we are. Findings that both review passes found on their own are marked "both passes".

**Status, 2026-11-19:** findings 1-28 are fixed on this branch, and the three folder interleavings this branch introduced are gone. The older compile-order cases listed near the end are left for a later branch.

## Fix first

These can lose data, hang the app or quietly break a feature.

| # | Area | Problem | Source |
|---|------|---------|--------|
| 1 | Text page | After "Reload from disk", the page keeps the old encoding and read-only state, so the next save can rewrite every non-ASCII byte. | Opus reviewer, confirmed |
| 2 | Text save (Main) | Main does not refuse saves for files over 5 MB, invalid UTF-16 or "changed while reading". Only the renderer's `readOnly` prop stops them. | Opus reviewer, confirmed |
| 3 | Link checks (Main) | A link to a named pipe can hang every file operation in the main process. | code review, confirmed |
| 4 | File explorer | The second Markdown folder link never scrolls to or focuses its row. | both passes, confirmed |
| 5 | File create (Main) | `Docs/guide.md` created inside an existing `docs` folder comes back with the typed case. The tree, the selection and the link base folder then point at a path that does not exist. | both passes |
| 6 | Modal focus | A dialog whose first control is hidden with CSS opens with the focus still behind it. The add-image prompt does this today. | both passes |
| 7 | Text save | Files with mixed line endings are normalized on the first save without any warning, which produces a diff of the whole file. | Opus reviewer, confirmed |

## Correctness

**1. The encoding and read-only state go stale after a reload.**
[TextDocumentTarget.fs:244-266](../src/Electron/src/Renderer/Components/MainContent/TextDocumentTarget.fs#L244-L266), together with lines 144-149 and 195.
- `onSave` and `readOnly` read the encoding and read-only state from `data.Info`, which comes from the props.
- `reloadFromDisk` updates only the refs (hash and last saved text). The "changed on disk" effect updates only the props.
- Scenario: a dirty UTF-8 file is rewritten outside the app as Windows-1252 or UTF-16, and the user clicks "Reload from disk". The editor stays editable. The next save sends the old encoding with the new hash, the hash check passes, and every non-ASCII byte changes. The NotUtf8 read-only rule exists to prevent exactly this.
- Second scenario: the file on disk is replaced by one that opens read-only. "Overwrite" then does nothing and shows no message, because `save` returns false for a read-only editor.
- Fix direction: keep the file info in one piece of state that reload, save and the effect all update, or let reload go through the page state.

**2. Main trusts the renderer for three read-only reasons.**
[TextFileSave.fs:323-353](../src/Electron/src/Main/IPC/TextFileSave.fs#L323-L353)
- The save re-checks symlinks, non-files, write protection and Windows-1252.
- It does not re-check TooLargeToEdit, InvalidUtf16 or ChangedWhileReading.
- With finding 1, or with a future second host that forgets `readOnly` (notes, metadata descriptions), Main writes a lossy decode without complaint.
- Fix direction: classify the bytes Main already reads for the hash, and refuse with that reason unless `overwrite` is set.

**3. A named pipe hangs the main process.**
[MarkdownResolverService.fs:206-213](../src/Electron/src/Main/Services/MarkdownResolverService/MarkdownResolverService.fs#L206-L213)
- The LFS pointer probe opens any target that is not a directory and is smaller than the probe size.
- A FIFO has size 0, so it qualifies, and `open` blocks until a writer appears.
- The lint runs on every edit, so a few keystrokes use up libuv's threadpool. After that, saves, opens and the file watcher all stall. This affects Linux and macOS.
- Fix direction: run the probe only when `stats.isFile()` is true.

**4. A Markdown folder link reveals its row only the first time.**
[FileTreeExpansionContext.fs:628](../src/Electron/src/Renderer/Context/FileTreeExpansionContext.fs#L628) and [FileExplorer.fs:489](../src/Components/src/Page/FileExplorer/FileExplorer.fs#L489)
- `revealAndFocus` derives the token from the current request. Once `completeFocusRequest` clears the request, the next one is Token 1 again.
- `handledRevealToken` still holds `Some 1`, so the explorer skips it, and the request stays pending.
- Fix direction: take tokens from a counter in a ref that only goes up.

**5. A nested create returns the typed case instead of the on-disk case.**
[FileSystemIO.fs:783-786](../src/Electron/src/Main/IPC/FileSystemIO.fs#L783-L786)
- On case-insensitive file systems, `planFoldersAsync` matches `Docs` against the existing `docs` folder and then keeps building the path from the typed name.
- Selection, reveal and own-write stamps then use a path that the tree does not have. Every relative link in the new file is flagged `CaseDiffers`.
- Fix direction: walk into the folder under its on-disk name, or refuse a match that differs only in case. Refusing is the simpler, explicit option.

**6. The modal's first focus can land on a hidden control.**
[BaseModal.fs:24-32](../src/Components/src/Primitive/BaseModal/BaseModal.fs#L24-L32)
- `ModalFocus.initialTarget` skips only elements inside `[hidden]`.
- It still picks `display:none` elements and `tabindex=-1` buttons. The hidden `<input type=file>` in `MarkdownPluginPromptModal` is one of them.
- `focus()` on such an element does nothing, so the focus stays on the trigger behind the overlay.
- Fix direction: skip elements that are not rendered or not tabbable.

**7. Link warnings are off by one character in files that start with a BOM.**
[LinkCollection.fs:68](../src/Components/src/Composite/MarkdownText/Links/LinkCollection.fs#L68)
- micromark skips a leading U+FEFF, but the editor document keeps it. `OntologyReferences.insert` already refuses BOM files for this reason.
- Fix direction: shift the offsets by one when the text starts with U+FEFF.

**8. An empty mark range throws inside the decorations.**
[CodeMirror.fs:122](../src/Components/src/Composite/TextEditor/JsBindings/CodeMirror.fs#L122) and [ProseSpellcheck.fs:63](../src/Components/src/Composite/TextEditor/ProseSpellcheck.fs#L63)
- The binding's comment says an empty mark draws nothing. `@codemirror/view` actually throws `RangeError`.
- `skipMark.range` has no `from < to` guard. One zero-length node stops the editor from redrawing.
- Fix direction: fix the comment and skip empty ranges.

## Robustness

**9. Mixed line endings are normalized without a word.**
[LanguageDetection.fs:128-137](../src/Components/src/Composite/TextEditor/LanguageDetection.fs#L128-L137) and [CodeMirrorEditor.fs:292](../src/Components/src/Composite/TextEditor/CodeMirrorEditor.fs#L292)
- The save joins all lines with the first line separator it found. A CRLF file with a few LF lines gets every line ending rewritten by a one-character edit.
- Fix direction: detect mixed endings when the file opens, and say so (a notice, or open read-only) instead of normalizing silently.

**10. A failed nested create leaves new folders behind.**
[FileSystemIO.fs:839-880](../src/Electron/src/Main/IPC/FileSystemIO.fs#L839-L880) and [IArcVaultsApi.fs:962-972](../src/Electron/src/Main/IPC/IArcVaultsApi.fs#L962-L972)
- The folders made before a failure stay on disk. The API skips the reconcile on `Error`, so the tree does not show them either.
- Fix direction: reconcile on `Error` too and name the leftovers in the error message. That is the explicit option. Removing them is the other.

**11. The dirty-flag push to Main swallows its errors.**
[LeaveGuard.fs:45-52](../src/Electron/src/Renderer/Components/LeaveGuard.fs#L45-L52)
- `pushDirty` ignores both a rejection and an `Error` result. Main's `hasUnsavedTextChanges` can then stay false while the editor holds unsaved edits.
- In that state, the window close is caught only by the `beforeunload` fallback, which shows a dialog worded for reloading.
- Fix direction: show the failure, or let Main ask the renderer when the window closes.

**12. A reveal request never expires.**
[FileExplorer.fs:489](../src/Components/src/Page/FileExplorer/FileExplorer.fs#L489)
- If the row is not there yet (slow or failed folder load, or the explorer was unmounted), the request stays in the context.
- When the row finally appears, the focus jumps to it, possibly while the user is typing elsewhere.
- Fix direction: drop the request when its folder load fails, or once the user has moved on.

## Brittle code

**13. Enter in the ontology dialog relies on the order of handler calls.**
[OntologyReferenceDialog.fs:131-142](../src/Components/src/Composite/MarkdownText/OntologyReferenceDialog.fs#L131-L142)
- Enter inserts the right term only because ComboBox calls `onSelect` before the host's `onKeyDown`, and because `chosenRef` is already updated while the render closure still holds the old values.
- Breaks if ComboBox ever calls `onKeyDown` first. Enter would then insert the previously resolved term.
- Fix direction: decide from explicit state, such as ComboBox reporting that it consumed Enter.

**14. The editor handle is built once, and every prop is mirrored into a ref by hand.**
[CodeMirrorEditor.fs:158-184](../src/Components/src/Composite/TextEditor/CodeMirrorEditor.fs#L158-L184)
- There are 9 mirror refs and 4 "applied" refs. `ariaLabel` is read directly at line 318, so `replaceContent` already uses the label from mount time.
- Breaks with the next prop that `createState` reads: it silently keeps its mount-time value after a reload.
- Fix direction: one latest-props ref behind a small controller object.

**15. The removal notice is a side effect of a check inside an effect.**
[FileTree.fs:70-76](../src/Electron/src/Renderer/Components/LeftSidebar/FileExplorer/FileTree.fs#L70-L76), together with lines 276-293
- `keepsRemovedDirtyDocument` reads like a test but sets `missingOnDiskRef` and the RemovedFromDisk notice.
- An extra re-run after a save recreated the file sends the next save without an expected hash, which comes back as a false conflict.
- Confidence: plausible.
- Fix direction: report "selected file removed" once, as an event, and keep the predicate pure.

**16. The modal's default focus depends on undocumented Floating UI timing.**
[BaseModal.fs:44-52](../src/Components/src/Primitive/BaseModal/BaseModal.fs#L44-L52)
- `InitialFocusTarget` passes an object whose `current` is a getter. That works only because `FloatingFocusManager` reads it late.
- A library update, or a menu that closes one tick later, sends the focus back to the trigger.
- Confidence: plausible.
- Fix direction: focus the target yourself in a layout effect.

**17. Reveal focus finds its button by DOM order.**
[FileExplorer.fs:36-45](../src/Components/src/Page/FileExplorer/FileExplorer.fs#L36-L45)
- It focuses the first `button` in the row. Any new leading control in the row, such as a checkbox or an expand toggle, takes the focus instead.
- Confidence: plausible.
- Fix direction: mark the name button with a data attribute and query that.

**18. The 500-path limit is defined twice, once per process.**
[LinkDiagnostics.fs:11](../src/Components/src/Composite/MarkdownText/Links/LinkDiagnostics.fs#L11) and [MarkdownResolverService.fs:44](../src/Electron/src/Main/Services/MarkdownResolverService/MarkdownResolverService.fs#L44)
- If only one of them changes, link checks on large documents fail with a vague message.
- Fix direction: one shared constant.

## Separation of concerns

**19. Nobody owns the text document's lifecycle.** This is the biggest structural risk on the branch.
- The same facts live in several places: CodeMirrorEditor refs, TextDocumentTarget props and refs, the LeaveGuard `dirtyRef`, Main's `hasUnsavedTextChanges`, FileTreeExpansion own-write stamps and FileTree effects.
- Each of them records some of: where the content came from, encoding, read-only state, dirty state, missing on disk, and own-write echo.
- Findings 1, 11 and 15 are these copies drifting apart already, and each fix so far added another ref.
- Fix direction: one explicit state machine behind one hook, for example `useTextDocumentSync`.

**20. `TextDocumentTarget` does too many jobs.**
[TextDocumentTarget.fs](../src/Electron/src/Renderer/Components/MainContent/TextDocumentTarget.fs) is a 560-line view that holds:
- the save and conflict protocol
- the disk sync effect
- the leave-guard registration
- the mode toggle
- three rendered views

Separately, the HTML sandbox exists twice, with different rules depending on file size:
- the `srcdoc` frame in this page, which has the `<base href="about:srcdoc">` fix
- the `data:` URL in [HtmlViewer.fs:17-26](../src/Components/src/Page/FileViewer/HtmlViewer.fs#L17-L26), which does not

Fix direction: move the sync logic into a hook, and move the rendered views into `src/Components/.../FileViewer`, so one sandbox rule exists.

**21. "Open an ARC file" is written three times.**
[FileTree.fs:449-478](../src/Electron/src/Renderer/Components/LeftSidebar/FileExplorer/FileTree.fs#L449-L478), [MarkdownLinkContext.fs:64-91](../src/Electron/src/Renderer/Components/Helper/MarkdownLinkContext.fs#L64-L91) and [App.fs:307-320](../src/Electron/src/Renderer/App.fs#L307-L320)
- Each copy does leave check, selection, page and error page. The copies handle LFS and errors differently.
- A fourth entry point (search, Git, provenance) is likely to get it wrong.
- Fix direction: one renderer service, `openArcFile path`.

**22. Key notation and OS detection live in the TextEditor folder, and older copies remain.**
[Shortcuts.fs](../src/Components/src/Composite/TextEditor/Shortcuts.fs)
- EditorToolbar, MarkdownText, the ARC editor and the Electron renderer all depend on TextEditor just for `Shortcuts`.
- Three older ⌘/Ctrl mappings remain, in [ContextMenu.fs:744](../src/Components/src/Composite/AnnotationTable/ContextMenu.fs#L744), [SearchField.fs:20-26](../src/Components/src/Page/ArcFileEditor/SearchField.fs#L20-L26) and [SettingsPage.fs:400](../src/Components/src/Page/SettingsPage/SettingsPage.fs#L400).
- SearchField now sits in the same `EditorToolbar` as Undo and Redo, so one bar formats shortcuts in two ways.
- Fix direction: move `Shortcuts` to `Swate.Components.Util`, next to `UserOS`, and migrate the copies.

**23. Markdown knowledge sits inside the generic TextEditor.**
[ProseSpellcheck.fs:11-23](../src/Components/src/Composite/TextEditor/ProseSpellcheck.fs#L11-L23) and [TextEditor.fs:103](../src/Components/src/Composite/TextEditor/TextEditor.fs#L103)
- Prose mode is chosen by comparing language-id strings. The spellcheck skip list is made of `@lezer/markdown` node names.
- A new prose language gets no skipping. A node rename in `@lezer/markdown` silently turns spellcheck back on for URLs and code.
- Fix direction: per-language skip rules supplied by the language or by the Markdown layer.

## Repo rule breaks

These break rules from [AGENTS.md](../AGENTS.md) and [ReactComponentDesign.md](ReactComponentDesign.md).

| # | Where | Rule |
|---|-------|------|
| 24 | [BaseModal.fs:9](../src/Components/src/Primitive/BaseModal/BaseModal.fs#L9) and line 49: `ModalFocus` and `InitialFocusTarget` are internal at namespace level | File-local types and helpers belong in a private module |
| 25 | [Preview.fs:255](../src/Components/src/Composite/MarkdownText/Preview.fs#L255) and line 331: `LinkRenderer` and `ImageRenderer` are `let`-bound components | Components must be static members of a class |
| 26 | [Preview.fs:199](../src/Components/src/Composite/MarkdownText/Preview.fs#L199): `IElementProps` is public but used only in that file | File-local types go in `private module PreviewTypes` |
| 27 | [Links/Types.fs:1](../src/Components/src/Composite/MarkdownText/Links/Types.fs#L1): declares a `namespace` | Non-component files use a `module` declaration |
| 28 | [ArcPathContainment.fs:29](../src/Electron/src/Main/Helper/ArcPathContainment.fs#L29), plus lines 34-78: calls `path` through `?` dynamic access | Use typed, reusable bindings |

## Compile order to fix in a later branch

Each folder's files, subfolders included, should form one contiguous block in the `.fsproj`. A file of one folder that sits between another folder's files usually means it belongs to another domain. This branch fixed the three cases it introduced: `TextEditor/SchemaValidation`, Main's `Services/MarkdownResolverService`, and Shared's `DTOs/MarkdownResolverDto`.

The cases below were already on `main`. The second column is the likely cause, read from the file order only. Check each one against its real dependencies before moving files. A small Node script that is not in the repo yet found these. It lists every folder whose compiled files are not contiguous.

| Project | Interleaving | Likely cause and direction |
|---------|--------------|----------------------------|
| Client | `SharedComponents/JsBindings/DndKit.fs` vs `JsBindings/Cytoscape.fs` | Two places for JS bindings. Merge them into one `JsBindings` folder. |
| Components | `JsBindings/Elk.fs` after `Util/WebWorker*.fs` | The ELK binding needs the web worker helper. Move `Util/WebWorker` ahead of `JsBindings`, or move `Elk.fs` to the graph layout code that uses it. |
| Components | `Util/SemVer*`, `Util/DateTimeExtensions.fs` vs `Api/GitHubReleases*` | `Api` sits inside the `Util` block. Finish `Util` first, then `Api`. |
| Components | `ARCtrl/Extensions/*`, `ARCtrl/Json.fs` vs `Composite/TableImport/Types.fs`, `Helper*.fs` | ARCtrl extensions depend on TableImport types, or TableImport helpers sit inside ARCtrl. Move the shared types down into the ARCtrl domain, or move the TableImport files out of the ARCtrl block. |
| Components | `ARCtrl/ClipboardCodec*.fs`, `ARCtrl/Extensions/ArcTable.fs` vs `ClipboardContract/*` | The clipboard codec belongs to the clipboard domain. Move `ARCtrl/ClipboardCodec*` into `ClipboardContract/`. |
| Components | `Composite/TermSearch/TermSearch.test.rtl.fs` placed inside the `Composite/AnnotationTable` block, after `Composite/Table/Types.fs` | The test depends on Table types. Place it after its dependencies but next to TermSearch, or move what it needs ahead of TermSearch. |
| Components | `Composite/TemplateBrowser/*` vs `Composite/TableImport/ImportModeSelect.fs` | TemplateBrowser uses ImportModeSelect. Compile the TableImport block before TemplateBrowser. |
| Components | `Page/GitSidebar/GitDefaults.fs`, `Types.fs` vs `Page/SettingsPage/Types.fs` | Settings and the Git sidebar share Git defaults. Move `GitDefaults` to a place both depend on. |
| Components | `Page/DataHubBrowser/sub/RepoListRow.test.rtl.fs` after `DataHubBrowser.fs` | Move the test next to `RepoListRow.fs`, unless it needs `DataHubBrowser`. In that case it belongs to the DataHubBrowser tests. |
| Components | `Page/ArcFileEditor/EmptyTableView/Main.sample.fs` after `Page/ArcFileEditor/Widgets.fs` | The sample uses the whole ArcFileEditor. Make it an ArcFileEditor sample instead of an EmptyTableView one. |
| Main | `Services/Settings/RecentArcStore.fs` ... `UserDefaults.fs` vs `ArcMerge/Types.fs` | `UserDefaults` comes after ArcMerge. Keep the Settings block together and check what `UserDefaults` needs from ArcMerge. |
| Main | `ArcMerge/*` vs `ARCtrlExtensions/WorkflowPayloads.fs` | ArcMerge extensions need the workflow payloads. Compile `ARCtrlExtensions` before ArcMerge. |
| Main | `VersionControl/VersionControlSettings.fs` ... `GitExecutableSetting.fs` vs `Services/Settings/UserDefaults.fs` | `GitExecutableSetting` needs `UserDefaults`. Compile Settings before VersionControl. |

The script reports 28 lines for these cases on `main` and on this branch, because a nested folder is counted once per folder level.

## What is in good shape

- The pure layers are well built and tested on their own: `MarkdownEdits`, the `Links` modules, `LanguageDetection`, `SchemaValidation`, `MarkdownAssetUrl`.
- The temp-file-and-rename save in `TextFileSave` is solid.
- `IMarkdownLinkContext` and `MarkdownSurface` keep app logic out of MarkdownText.
- `ArcPathContainment` gives open, save and asset serving a single containment rule.
