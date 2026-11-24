# Test review: behavior vs. wording (feature/text-editor)

Scope: every test added or changed since `b73e2d66` (merge base with main), including Storybook `play` functions. About 550 tests in ~60 files.

**Short answer:** almost all new tests check real behavior. Only 11 tests check nothing but copy. About 120 more check real behavior but also compare the exact message text, so they break on a copy change for no reason. That pinning sits almost entirely in error/refusal paths and is mostly produced by four test helpers, so it can be fixed in a few places. Separately, 4 tests can pass while the rule they name is broken (section 1).

**Status (2026-11-24):** sections 1–4 applied. Exact copy is replaced by `toContain` on the values a message carries (paths, IDs, IRIs, keys, host errors); only messages without such a value are reduced to "refused". Left on purpose:
- section 5,
- the "Warning" alert title (:159) and the GitHubFlavored story,
- the partial duplicates `Authentication.stories.tsx` CancelRemoveFlow and `ArcFileEditor.stories.tsx` IntegratedNavbar,
- older exact-copy tests that predate this branch.

Categories:
- **WORDING**: only copy is asserted. Delete or rewrite.
- **MIXED**: real behavior plus exact copy. Keep the behavior, drop or loosen the copy.
- **WEAK**: duplicate, or cannot fail on what it claims.

---

## 1. Tests that can pass while the rule they name is broken (fix first)

| Test | Problem | Fix |
|---|---|---|
| `src/Components/src/Composite/TextEditor/SchemaValidation/Lint.test.fs:75` "rejects a schema without $id" | Root id is `"x"`. Without the no-$id check (`Validator.fs:72`), `Validator.fs:104` still fails with "No schema has the $id x.", which also contains `$id`. | Register one valid schema with `$id` plus one without, use the valid one as root, assert `Error`. |
| `src/Components/src/Composite/TextEditor/CodeHighlight.test.fs:25` "finds a language by an alias" | `"sh"` is an alias *and* an extension of Shell in `@codemirror/language-data`, so the extension fallback passes it even if alias lookup breaks. | Use `"zsh"` (alias only). |
| `tests/Electron.Renderer/TextDocumentState.test.fs:105` "the status puts read-only first, then a running save, ..." | Each state sets one condition only, so precedence is never tested. Asserts only the fixed labels. | Combine conditions: `status (readOnly + dirty) = status readOnly`, `status (saving + dirty) = status saving`, and check the error flag. |
| `src/Components/src/Composite/EditorToolbar/EditorToolbar.test.rtl.fs:22` "Undo and Redo are disabled while ..." (title at :31) | No `disabledReason` is passed, so the branch its comment describes (reason hidden when only the history disables the button) never runs. | Pass `disabled = false, disabledReason = "x"`, `canRedo = false`; assert the title is not `"x"`. |

Two more where the copy check is currently the *only* proof that the right branch fired. A plain `IsError` would pass for the wrong reason:
- `tests/Electron.Core/TextDocumentIO.test.fs:429` "refuses paths that are not part of the ARC inventory": the temp dir has no `.git/` or `sub/`, so the missing-folder check would also reject. Create those folders first, then assert `IsError` and that no file was written.
- `Lint.test.fs:153` "keeps only the errors of the oneOf branch with the right shape": a wrong fold also yields count 1. Assert `toContain "familyName"` instead of the full sentence.

## 2. WORDING: only copy is checked (11)

| Test | Suggestion |
|---|---|
| `tests/Electron.Renderer/MarkdownLinkContext.test.fs:22, :29, :42, :56` (`assetRefusalMessage` ArcNotOpen / OutsideArc / LfsPointer / no extension) | Delete. The function is a lookup table of fixed strings. |
| `src/Components/src/Composite/MarkdownText/MarkdownTextViewer.test.rtl.fs:215` "shows a notice for an image whose source the sanitizer removed" | Assert no `<img>` in the container and the notice element exists (`role="img"`). |
| `MarkdownTextViewer.test.rtl.fs:237` "an ARC image becomes a notice" | Same as above. |
| `src/Components/src/Composite/MarkdownText/MarkdownToolbar.test.rtl.fs:188` "the title of a command with a key shows the shortcut" | Delete. The derived loop at :213-214 already covers Bold. |
| `tests/Electron.Core/TextDocumentIO.test.fs:503` "refuses a path whose folder is missing" | `IsError` + `gone/` was not created. |
| `TextDocumentIO.test.fs:429` inventory test | See section 1. |
| `tests/Electron.Core/MarkdownResolverService.test.fs:630` "refuses a closed ARC" (ListFolder) | `IsError`, like the CheckPaths version at :383. |
| `tests/Electron.Core/RenamePathRules.test.fs:94` "validateNewItemPath rejects backslashes, ..." | `Result.isError` per case. Each input breaks exactly one rule. |

## 3. MIXED: fix the helpers, not each test

Most of the ~120 MIXED tests come from four helpers that fold the full error message into the expected value:

| Helper | Affected | Change |
|---|---|---|
| `OntologyReferences.test.fs:28` `outcome` (`"Error: " + message`) | 26 tests (:113-:355, :389-:555) | Return a fixed marker like `"<refused>"` on `Error _`. Add `toContain` for interpolated values (`http://e.org/other`, `urn:x:1`, `MS:1000031`, `boom`). |
| `MarkdownEdits.test.fs:24` `errorOf` | 10 tests (:80, :211, :240, :296, :303, :333, :365, :372, :423, :433) | `refuses cmd text a h = Result.isError (...)`. |
| `LinkResolution.test.fs` `reasonOf` + `.toBe "<reason>"` | :41 (quotes only), :49, :61, :69, :76 | `isUnsupported` helper like the existing `isOutside`. |
| `TextDocumentIO.test.fs:341` `expectError` | :442, :463, :484, the 8 generated cases at :566 (table :551-563), :585 | `IsError` + "bytes on disk unchanged". To keep the reason, compare to `TextFileSave.readOnlyErrorMessage reason` instead of a literal. |

**Trade-off:** `isError` alone no longer shows *which* refusal fired. That matters for functions with many refusal paths (`OntologyReferences.insert`, `saveTextFileAsync`). The robust fix is a refusal union in the source, mapped to copy only where it is displayed, so tests assert the case. If that's too much for now, pair `isError` with a "nothing changed" check and keep test inputs that can only trip one rule.

Other MIXED tests, grouped by file (keep the behavior, drop or loosen the copy):

- **MarkdownLinkContext.test.fs**: :36, :63, :70 → `toContain` path / `.tif` / `EACCES`. :49 → `toContain "50 MB"`. `linkActionOf` Fail tests :104, :112, :120, :128, :136 → match `Fail m` + `toContain` the data. `openArcPathWith` :285, :297, :325, :339: the fake `depsWith` (:207, :212) logs full messages. Log `"OnError"` / `"SetPage error"` and check messages separately.
- **LinkDiagnostics.test.fs**: :32 → `toContain "docs/gone.md"`. :68 → assert `severity = "error"` + range (the name says "as an error" but that's never checked). :104 → `toContain "host offline"`.
- **Lint.test.fs**: :105 → `toContain "\"active\""`. :115, :127, :163, :173 → delete the message line (count/span/source already prove it). :137 → `toContain "familyName"` + `"members[0]"`. :145 → `toContain` the branch titles. :199 → assert `source = "schema"`.
- **OntologyReferenceDialog.test.rtl.fs**: :190, :279, :344, :378 wait on domain copy → wait for the "Insert as plain text" button instead. :206, :279, :330, :344 assert alert text → assert the alert exists (or compare to the domain function's `Error`). :443 probes the `<dt>` label "CURIE" → probe a data value (the IRI).
- **MarkdownToolbar.test.rtl.fs**: :117, :127, :164 → derive the expected text from `MarkdownEdits.toggleBold state`. :154 → assert `role="status"` exists. :196 → delete :216-218 (the loop covers them) and use `Shortcuts.title` at :220. :223 → loop over `headings` with `Shortcuts.title`. :259, :305 detect the open dialog by header copy → `queryByRole "dialog"`. The :328 disabled check duplicates :59.
- **MarkdownTextViewer.test.rtl.fs**: :73, :226 `getByTitle "<static copy>"` → assert the text renders in a non-anchor element with a non-empty `title`. :159 alert title "Warning" is low priority (mirrors GitHub's labels).
- **NoteCreation.test.fs**: :71 → `isError`. :121 → `toContain` the path. :137 → `toContain "disk full"`.
- **TextDocumentState.test.fs**: :94 → `toContain "disk full"`.
- **TextDocumentSchemas.test.fs** (low priority): :45 → `toContain "\"name\""`. Drop :41. :53/:61 are fine, or loosen to the quoted key.
- **ArcFileSystemHelper.test.fs**: :371 → keep the disk checks, cut the fragments to the quoted names. :412 → `toContain "'docs'"`. :433 → `toContain "'kept', 'kept/deeper'"`.
- **MarkdownResolverService.test.fs**: :411 → match `PathCheck.Unreadable _`.
- **RenamePathRules.test.fs**: :114 → `expectErrorContaining "'..':"` for :116-118.
- **TextEditor.test.rtl.fs**: :152 aria label → `toContain "first.md"` / `"second.md"`.
- **EditorToolbar.test.rtl.fs**: :121-129 → `stringContains` each key label rather than the full `"Heading (k1, k2)"` format.
- **LineEndings.test.fs**: :11 → `isSome` + `toContain "CRLF"`. Add `"a\nb\r\n"` expecting `"LF"` and not `"CRLF"`, which is what the name promises.
- **SearchField.test.rtl.fs**: :98 → drop, or compare to `Shortcuts.label`.
- **Stories:**
  - `MarkdownToolbar.stories.tsx:60` (domain copy) → assert the status is non-empty.
  - `MarkdownTextViewer.stories.tsx` WithContext :29 and WithoutContext :40 (static copy) → `getByRole('img', {name})` that is not an `<img>`. WithoutContext never asserts what its docstring describes.
  - `TextEditor.stories.tsx:142-143` → drop (repeats Lint.test.fs).
  - **`TextEditor.stories.tsx:161` pins Ajv's own message "can't resolve reference"** (tests the library). Replace with `'person.schema.json'` or delete.
  - `ArcFileEditor.stories.tsx:43` → `getByRole('toolbar')` without a name.

## 4. Duplicates (WEAK)

- `EditorToolbar.stories.tsx:58-61` ReadOnly play = `EditorToolbar.test.rtl.fs:51-73`, and the sample hard-codes `disabled = true`. Drop the play, keep the story.
- `TextDocument.stories.tsx:69-70` Yaml play ⊂ `TextDocument.test.rtl.fs:121-122`. Drop the play.
- `SettingsPage.stories.tsx:273` OpensAtCategory = `SettingsPage.test.rtl.fs:431`. Drop one.
- `MarkdownToolbar.stories.tsx:129-134` (all disabled) = `MarkdownToolbar.test.rtl.fs:59-80`. Only :136-140 (Ctrl+B in read-only) is new.
- `LanguageDetection.test.fs:84` mirrors the static `markdownProse` table. `ProseSpellcheck.test.fs:47-77` covers the effect. Delete.
- `MarkdownResolverService.test.fs:378`: the 501-paths half repeats :388. Keep the closed-ARC half.
- `Authentication.stories.tsx:278` CancelRemoveFlow mostly repeats `AccountManager.test.rtl.fs:126`.

## 5. Side notes (not wording)

- `MarkdownToolbar.test.rtl.fs:164, :233, :242, :345` render nothing. They're unit tests and belong in a `.test.fs` (AGENTS.md). :345 tests `Util.Shortcuts`, not the toolbar.
- `tests/Electron.Renderer/ipcSenderBridges.setup.mts:5-16` hand-copies the sender list from `Renderer/Api.fs`. With it in place, the `vi.mock` of `Api.js` in `MarkdownLinkContext.test.fs:13-17` and `FileNavigation.test.fs:10-11` looks redundant (not verified by running).
- `SettingsPage.stories.tsx` NavigationBesidePage / NavigationAbovePageWhenNarrow only check `toBeVisible` and never check the beside/above placement their names claim.

## Clean files

PathCompletion, LinkCollection, TextEditor.test.fs, TextCompletion, ProseSpellcheck, TextDocument.test.rtl, MarkdownTextEditor.test.rtl, AuthService, TextDocumentHelper, UserDefaults, ArcVaultHelper, IndexedFileTree, FileNavigation, all Renderer FileTree*/Git* changes, AccountManager, Authentication/Helper, SettingsPage.test.rtl, FileExplorer, BaseModal, ContextMenu, ComboBox, Modals, ThemeSelector/Changelog stories. `IpcArchitectureReview.test.fs` only gained an `open`; there is no source-text testing anywhere in the branch.
