# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**Types of changes**

- ✨ `Added` for new features.
- 🔄 `Changed` for changes in existing functionality.
- 🗑️ `Deprecated` for soon-to-be removed features.
- 🔥 `Removed` for now removed features.
- 🐛 `Fixed` for any bug fixes.
- 🔒 `Security` in case of vulnerabilities.

## [Unreleased]

### ✨ Added

- Linux releases include an Arch Linux package (`.pkg.tar.zst`), an `.AppImage` and a `.zip` of the unpacked app, for distributions that cannot install the `.deb` or `.rpm`. The `.deb`, `.rpm` and `.zip` packages are now also provided for `arm64`. #1433 (by @caroott)

### 🔄 Changed

- Clicking a folder or its arrow in the file tree expands or collapses it. A study, assay, workflow or run folder also opens its metadata, while other folders keep the current view open. (by @Etschbeijer)
- The right-click menu offers "Open" only for files and for folders that have metadata. (by @Etschbeijer)

### 🐛 Fixed

- Importing a template as a new table no longer fails silently or adds a broken table when a table with that name already exists. The import dialog renames the new table to a free name, tells you why, lets you change the name, and warns you while the name is already taken. (by @Etschbeijer)
- Importing tables from a JSON file no longer leaves part of the import in the open file when it fails. A table whose name is already taken is renamed to a free name, and a table name that cannot be saved is changed so it can: it is shortened to 31 characters, loses apostrophes at its start and end, gets _ in place of * ? : \ / [ ], and "History" or a name the file keeps for its own sheets, such as isa_assay or isa_datamap, gets a number. (by @Etschbeijer)
- Renaming a table shows why a name cannot be used, and no longer accepts names that cannot be saved: names that differ from another table's name only in case, contain * ? : \ / [ ], start or end with an apostrophe, are "History" or a name the file keeps for its own sheets, such as isa_assay or isa_datamap, in any spelling, or are longer than 31 characters. (by @Etschbeijer)
- A new study, assay or run with a long name no longer gets a first table whose name changes when the file is saved. When its name followed by " Table" would be longer than 31 characters, the table is named after the study, assay or run alone, shortened to 31 characters if needed. (by @Etschbeijer)

## 3.0.0 - 2026-10-16

### ✨ Added

- The settings page has a "Git defaults" section where you choose the git sidebar settings that every ARC opened afterwards starts with, and ARCitISA keeps them across restarts and updates. #1424 (by @caroott)

### 🔄 Changed

- Swate is now called ARCitISA and has a new logo, designed by @Freymaurer. #1425 (by @caroott)

## 2.6.1 - 2026-10-16

### 🔄 Changed

- When files are marked in the git sidebar, a "Save Selected Changes" button appears next to "Save All Changes". #1392, #1423 (by @caroott)
- Saving without uploading is now the "Upload right away" checkbox under the save buttons, checked by default. #1423 (by @caroott)
- The git sidebar refreshes itself whenever you open it. #1423 (by @caroott)
- The git sidebar shows file names before their folder and uses the usual arrows for ahead, behind, download and upload. #1423 (by @caroott)

### 🐛 Fixed

- Markdown files and the release notes follow the Swate theme instead of the system's light or dark mode. #1418, #1423 (by @caroott)

## 2.6.0 - 2026-10-15

### ✨ Added

- The first upload of an ARC to DataHUB asks whether the repository goes to your own account or to one of your groups. The question only appears when at least one of your groups accepts new repositories. Groups where you can't create repositories are listed but disabled, with the reason next to them. #1404 (by @caroott)
- Add search options for DOI and Europe PMC for publications. #387, #1400 (by @Freymaurer)
- Add search options for ORCID as well as name for authors. #501, #763 (by @Freymaurer)
- Enable the creation of a missing README.md file in the root of an ARC, and in any folder where you can create files. Once created, a README.md can't be renamed or deleted in Swate. #1396, #1406 (by @Etschbeijer)
- Added that right-clicking the studies, assays, workflows or runs folder offers "New File". Also, files there need a file extension now, for example "notes.md", so Swate can't mistake them for a study, assay, workflow or run. #1396, #1406 (by @Etschbeijer)
- The new "Provenance Graph" button in the left sidebar shows how samples and data flow through the studies, assays and runs of the open ARC, down to the lineage of a single sample. #1412 (by @caroott)

### 🐛 Fixed

- Files placed directly in the studies, assays, workflows or runs folder, such as "studies/notes.md", can be renamed and deleted like any other file. Hidden files such as ".DS_Store" in these folders can be renamed too. #1406 (by @Etschbeijer)
- The right-click menu no longer offers Rename or Delete for files that can't be renamed or deleted there, such as a stray "isa.study.xlsx" directly in the studies folder. #1406 (by @Etschbeijer)

- When DataHUB refuses a new repository because of missing permissions, Swate says so and no longer asks for a different name. #1404 (by @caroott)
- The tabs on the "Download ARC from DataHUB" page are shown as tabs again. #1404 (by @caroott)
- The import mode options in the template import dialog no longer look disabled. #1401 (by @Freymaurer)
- Swate app installation sometimes showed a startup error. This has been fixed (hopefully 😶). #1398 (by @Freymaurer)

## 2.5.2 - 2026-10-15

### 🐛 Fixed

- Adding multiple columns at once with the "Only Headers" option now works correctly. Before this fix, users were unable to save any changes done to ARC after for example "Adding a template". [#1397](https://github.com/nfdi4plants/Swate/issues/1397) (by @Freymaurer)

## 2.5.1 - 2026-10-12

### 🐛 Fixed

- The diff view keeps both versions in view next to each other, with a draggable separator and a horizontal scrollbar for each side and one for both, so a long line no longer hides the current version. #1391 (by @caroott)

## 2.5.0 - 2026-10-09

### ✨ Added

- Check for new Swate versions on App startup. Shows a "Update" Button (next to the authentication Avatar) if a new version is available. #1207 (by @Freymaurer)
- After update to new version display release notes for the new version. #1207 (by @Freymaurer)
- Add app window tab "Help", with "Check for updates ...", "Release Notes" and "About". (by @Freymaurer)
- Add setting to adjust UI scaling. This can also be done using the keyboard shortcuts "Ctrl + +" or "Ctrl + -" (or "⌘ + +" / "⌘ + -" on MacOS). #1165 (by @Freymaurer)
- Add display of PDFs (read-only) in Swate using the built-in browser compatibility of Electron. #1326 (by @Rookabu)
- A new Git sidebar setting, "Background indexing limit (MB)", decides how much of a large diff Swate reads ahead while you look at it. With a value from 1 to 63 the diff stays in memory and writes no temporary files. #1388 (by @caroott)
- Swate keeps about 1 GB free on the drive that holds its temporary files. On a nearly full drive a new diff stays in memory, and an open diff pauses until you free some space and press Continue. #1388 (by @caroott)
- When loading more of a diff fails, the status bar above the diff says why. #1388 (by @caroott)

### 🔄 Changed

- The diff of a changed file opens page by page and loads more lines as you scroll. Files of several gigabytes now open quickly, and Swate stays responsive while it reads the rest in the background. Files with many repeated lines, such as tables with repeating rows, show each edited line on its own. #1388 (by @caroott)
- Swate needs Git 2.42 or newer. #1388 (by @caroott)
- The Git sidebar shows "Download Changes" (formerly "Update ARC from Online") and "Upload Changes" side by side, with More Git Actions below. #1388 (by @caroott)

### 🐛 Fixed

- With "Download Large Files" on, "Download Changes" also downloads missing large files when there are no online changes. #1388 (by @caroott)

## 2.4.0 - 2026-10-07

### ✨ Added

- Conflicts in files without editable text (binary files, `isa.*.xlsx` workbooks, Git LFS files) get a file-choice panel with "Keep my version" and "Use online version". It shows each version's size, object id, whether it is downloaded and the online commit.
- The conflict page can abandon the merge, after a confirmation.
- A modify/delete conflict can be resolved by accepting the deletion.
- A stale `.git/index.lock` opens a dialog with "Remove lock" at Save, Discard, Download Changes and when downloading a Git LFS file. A lock younger than a few seconds is waited for first.
- Closing a window while a version control operation runs asks whether to keep the window open or cancel the operation and close.
- The Git sidebar follows file changes made outside Swate without pressing Refresh.
- The merge confirmation names up to five paths that would conflict.
- Discard asks for confirmation.
- The cancel button also covers the upload step of a save. Canceling it keeps the saved-locally notice and opens no error dialog.
- The main process logs the start and end of every version control operation with its result.
- Added a new colour scheme for Swate (@paulineHans GitHub)
- Added guidance for generating and using GitLab Personal Access Tokens. #1267 (by @Freymaurer)
- Rework DataAnnotator. DataAnnotator now uses the same list interface for insertion into selected cells as the FilePicker. No more forced "insert-all" into some obscure location! [#1335](https://github.com/nfdi4plants/Swate/issues/1335) (by @Freymaurer)

### 🔄 Changed

- All Git operations of the Electron app run through the VersionControlService library (0.1.0 on nuget.org), which offers one provider-neutral interface for Git and lakeFS. Swate uses its Git provider. The old built-in Git implementation is removed.
- Save, Download Changes and Upload Changes run one synchronize operation: refresh, update when the online copy is ahead, then publish. It asks first only when the update needs merge resolution, and it refuses an update or a branch switch that would overwrite local changes and names the files.
- A push the online copy refuses (a protected branch, a declined hook) shows the remote's reason, and the save stays local.
- The Git LFS threshold and the download preference are held in memory for each open ARC. Every ARC starts at 1 MiB and no download, and the `swate.lfs.*` keys of earlier versions are no longer read.
- Swate requires Git 2.38 or newer and Git LFS 3.7 or newer, with the Git LFS filter configured.
- Merges Swate creates are titled "Merge online changes".
- The busy notice shows the operation and git's progress lines. The raw "Git output" log is removed.
- The tooltips of Update ARC from Online, Download Changes and Clean LFS Cache list the git commands that run now.
- Replace git file status icons with shorthand letters (A, M, D, R, N, C) to reduce confusion with "+" icon. #1199 (by @Freymaurer)
- Allow custom input for "Add Rows" input in Annotation Table. You can now completely remove the numbers and just type away. [#1351](https://github.com/nfdi4plants/Swate/issues/1351) (by @Freymaurer)

### 🔥 Removed

- Removed the "Add Datamap" button from the ArcFileEditor taskbar. (by @Freymaurer)

### 🐛 Fixed

- Root-level ARC items in the file tree follow the ISA standard order, and the `isa.investigation.xlsx` workbook has a distinctive violet icon.
- The diff of a changed, downloaded Git LFS text file shows the changed lines instead of the pointer text, without downloading anything.
- Mark and Unmark Git LFS take effect on the next save, so the file's storage changes. Unmark is refused when the file's object is not local.
- Discarding a downloaded Git LFS file keeps it downloaded.
- Git LFS conflicts are no longer shown as editable pointer text.
- The DataHUB storage rules apply to every save: `isa.*.xlsx` workbooks are never stored as Git LFS pointers, and dataset files and files above 25 MB always are.
- Saving more than about 1000 files at once works on Windows, and a save starts a fixed number of git processes whatever the file count.
- Clean LFS Cache works after a discarded Git LFS edit.
- A clone whose Git LFS download fails keeps the clone, opens it and offers to download the large files.
- "Open ARC in DataHUB" keeps the remote's port.
- The busy notice resets its progress when an operation moves to its next step, and the bar follows the percentage git reports.
- On macOS, a file whose name is stored decomposed on disk keeps its Git LFS badge and actions.
- Closing a window during an operation no longer crashes the main process with "Object has been destroyed". Every message to a window checks that the window still exists.
- The file watcher no longer overwrites the in-memory ARC in the middle of one of Swate's own writes.
- A Git operation that fails after it already changed the workspace refreshes the sidebar before it shows the error.
- Added fallback support for broken repository image links in DataHUB browser. #1180 (by @Freymaurer)
- Show a native error message instead of crashing or silently failing when a selected folder is not a valid ARC.
- Fixed non-centered authentication button layout. (by @Freymaurer)

## 2.3.1 - 2026-09-16

### 🐛 Fixed

- Let the File Picker insert paths into the Input and Output cells of a table created from a template import, instead of failing silently because those cells were never stored.
- Keep the unit of unitized columns when adding rows with **Add Rows**, so values typed into new rows of a template imported **With Units** no longer need the unit set through the Details view.

## 2.3.0 - 2026-09-16

### ✨ Added

- Add Validation Package Browser for managing and browsing validation packages. It can be opened from the the sidebar (by @Freymaurer)

## 2.2.0 - 2026-09-16

### ✨ Added

- Add external file importing to the Electron FileTree, including cancellation and cleanup of temporary or partially imported files on cancellation or failure.
- Allow canceling in-flight Git network operations (ARC download/clone, fetch, update preview, pull, push, and Git LFS transfers) from the Git sidebar and the DataHUB download view. Cancellation kills the underlying git process and restores a clean repository state: a canceled pull aborts any half-applied merge or rebase, and a canceled ARC download removes the partially cloned folder #1306.
- Keep open ARC editors synchronized when DataMaps are added or deleted through the File Explorer or by external filesystem changes.
- Show **Delete DataMap**, rather than **Add DataMap**, in the context menu of a collapsed ARC entity folder that already contains a DataMap.
- Prevent newly added DataMaps from incorrectly appearing as unsaved after they have been written to disk.
- Show the current Swate release version before the active ARC name in the Electron window title.

### 🔄 Changed

- Rotate the Import JSON icon by 180 degrees in all navbars.
- Restore the **Download ARC from DataHUB** action to the Electron ARC selector.

### 🐛 Fixed

- Keep the ARC selector at a stable width and truncate long ARC names, and close its dropdown only after direct or overflow actions are invoked without dismissing the overflow menu prematurely.
- Recognize annotation-table column headers correctly when pasting tabular data.
- Paste DataMap values into the selected column instead of shifting them one column to the right.
- Keep pasted TSV values in their intended DataMap cells instead of interpreting three- or four-cell rows as ontology metadata.
- Preserve ontology term and unit metadata when copying and pasting within Swate in browsers that do not support custom clipboard formats.
- Fall back to plain text instead of failing when pasted clipboard data contains invalid Swate metadata.
- Preserve the row-and-column layout of multi-cell selections when copying between Swate, Excel, Google Sheets, and LibreOffice, including when applications prefer HTML clipboard content.
- Apply multi-cell paste consistently in annotation tables and DataMaps, repeating copied cells across larger selections and growing DataMaps when additional rows are needed.
- Preserve ontology metadata when pasting terms and units into supported table or DataMap columns, while preventing unrelated ontology identifiers from carrying over when values are replaced with plain text.
- Route annotation-table selections containing column headers through the header-aware paste flow, so pasted headers continue to add or update columns correctly.
- Clear cells during Cut only after the clipboard has been updated successfully, preventing data loss when browser clipboard access fails.
- Handle Windows, Unix, and legacy Mac line breaks consistently when displaying errors and importing data in the Data Annotator.

## 2.1.0 - 2026-08-19

### ✨ Added

- Add separate FileTree context-menu actions to open a selected folder and reveal a selected file or folder in its parent location #1228.
- Keep expanded FileTree parent folders visible while scrolling through nested content #1231.
- Add annotation-table actions for assigning a unit to a cell and removing a unit while retaining its value #1229.
- Enforce the DataHUB Git LFS tracking ruleset: `isa.*.xlsx` metadata files are never tracked with Git LFS (skipped by automatic tracking, exempt from the commit size policy, and blocked from manual marking), while files inside a `dataset` folder or larger than 25 MB can no longer be unmarked #1316.
- Add an editor button and a File Explorer action for adding DataMaps to assays, studies, runs, and workflows. The button remains visible but disabled when a DataMap cannot be added.
- Add the ability to remove DataMaps from their editor-tab context menu or through the File Explorer delete action.

### 🔄 Changed

- Write DataMap additions and deletions to the ARC on disk immediately. Newly created DataMaps are loaded into the FileTree before their editor tab becomes available, while the currently selected editor tab remains active.
- Simplify Electron FileTree navigation so ARC editors initialize the requested Metadata, table, or DataMap view directly, and show the DataHUB download action only in the sidebar.
- Consolidate Electron ARC editor page state, safely resolve canonical entity workbooks, and reuse shared path normalization for ARC-root-relative references.
- Harden canonical entity path resolution for Electron rename and delete operations, and document the behavior of the shared path-normalization helpers.
- Keep table deletion in the existing footer-tab context menu instead of exposing a second, inconsistent navbar action.
- Isolate requested ARC view selection from eager Electron IPC proxy initialization as a temporary testability workaround; a follow-up should inject the file-opening dependency and keep `openView` as a thin Electron adapter.

### 🐛 Fixed

- Preserve unit semantics when pasting multiple value-unit rows without headers into non-unit columns, and avoid inheriting metadata from the overwritten target cell for compact value-unit clipboard data #1227.
- Show immediate, consistently described navbar tooltips above tables, DataMaps, and sidebars #1289.
- Prevent simultaneous Swate Electron window shutdowns from sending recent-ARC updates to already destroyed windows.
- Show the error modal when a recently used ARC no longer exists at its saved path instead of opening an Electron system error.
- Prevent deleted ARC table tabs from reappearing by applying rename, delete, and add operations to a fresh copy of the current editor state without mutating refs during React rendering.
- Keep active-view ownership inside the reusable ARC editor, preserve valid table selections across immutable ARC updates, and remount it for Electron sidebar Metadata, table, and DataMap selections.
- Preserve stable table identifiers across rerenders and reorder operations, use one shared prefix for drag-ID generation and parsing, and normalize the active view after table deletion so drag-and-drop and tab state remain valid.
- Create DataMaps from a copied ARC value, publish the same value to the editor and Electron persistence, and keep the requested remount view aligned with the visible DataMap tab.
- Keep table-tab keys unambiguous by rejecting duplicate table names.
- Append imported templates to a fresh table model when the active table is empty.
- Allow hidden filesystem files such as `.DS_Store` to be deleted from ARC add-zone roots without treating them as ARC entities.
- Sign commits created in Swate with the stored DataHUB account matching the ARC's remote host (using the active account when no remote is configured yet, and leaving the user's own git config untouched for hubs without a stored account) instead of git's OS-derived fallback identity, so commits link to the account on the hub they are pushed to #1304.
- Report a missing git identity as its own failure with setup guidance instead of passing git's raw "Please tell me who you are" output to the user #1305.
- Respect GitLab's "use a private email in commits" setting by signing commits with the account's commit email instead of its primary email.
- Fix Template bug "You've hit dummy code" #1266, #1264 (by @Freymaurer)
- Fix overscroll issue on last table column on smaller screens (by @Freymaurer)

## 2.0.7 - 2026-08-06

### ✨ Added

- Add an Electron ARC file editor navbar action for creating and immediately saving DataMaps on assays, studies, runs, and workflows. The action remains visible and is disabled when a DataMap already exists.

## 2.0.6 - 2026-08-05

### 🔄 Changed

- Add Download ARC from the ARC selector into the sidebar below Git.
- Update FileTree ARC entity navigation: selecting an entity name opens Metadata, expansion and collapse are controlled only by the arrow, and table/DataMap files open their corresponding tabs.
- Store File Picker and Data Annotator references with an explicit `./` ARC-root-relative path in both table and DataMap views.

### 🐛 Fixed

- Load templates through ARCtrl's JavaScript web API in Electron's main process, avoiding incompatible .NET/Fable server bindings and browser CORS restrictions.
- Show Actionbar overflow options whenever the button count exceeds the configured visible-button limit.
- Use consistent clipboard formatting across normal table and DataMap views, preserving data selectors after `#` when copying single or multiple cells.
- Ensure switching between FileTree ARC files refreshes the selected editor tab instead of retaining the previous entity's tab.

## 2.0.5 - 2026-08-05

### 🐛 Fixed

- Preserve ARC static hash baselines during file watcher merges so saving metadata no longer overwrites unchanged XLSX files.
- Support Ctrl/Cmd+C, Ctrl/Cmd+X, and Ctrl/Cmd+V for single and multi-cell DataMap selections, preserve selectors after `#`, omit trailing whitespace when copying, and grow rows when pasting.
- Preserve pasted and edited DataMap Label values when rerendering the ARC file.
- Add table-consistent Copy, Cut, Paste, Clear, Fill Column, Clear Column, and Delete Selected Rows actions to the DataMap context menu.
- Rerender DataMap cells immediately after clearing them with the Delete key.
- Enable Add Rows and File Picker insertion in DataMap views.
- Store File Picker and Data Annotator references relative to the ARC root for assay, study, run, and workflow DataMaps, and rerender immediately after insertion.
- Migrate the outdated `isa_datamap` file to `isa.datamap.xlsx` during ARC loading, while preserving an existing canonical workbook.
- Fix the DataMap delete-row regression test to use the strongly typed `CellCoordinate` API.

## 2.0.4 - 2026-07-20

### 🐛 Fixed

- Added missing `type` field to contributors in `.zenodo.json`

## 2.0.3 - 2026-07-20

### ✨ Added

- Added `.zenodo.json` metadata file for zenodo release and related DOI generation (by @Freymaurer)

## 2.0.2 - 2026-07-15

### 🔄 Changed

- Provenance editor: a property's identity is now its header **plus** its source table, so same-named properties from different tables (e.g. "Temperature" from Growth vs. Cultivation) get their own rail row, color, drag identity and connector lines instead of collapsing into one property #1255 (by @Caroott)

### 🐛 Fixed

- Fix connection annotation inheritance in the provenance editor: values spread bidirectionally through a layer's connections to all transitively connected endpoints, removing a connection retracts exactly the values it carried, and upstream values are left untouched #1255 (by @Caroott)
- Group overwrites in the provenance editor now respect the property's origin: only assignments of the exact property are replaced (all writeback occurrences of a value together), upstream-originated properties can't be attached to entities that never had them, and drops on entities without a single shared existing value are rejected with a clear error instead of silently creating or mixing assignments #1255 (by @Caroott)

## 2.0.1 - 2026-07-14

### 🐛 Fixed

- Add failsafe for local storage migration from v1.0.7 to v2.0.0 in `ConfigProvider.fs` to prevent errors when the old field `aktiveKeys` is present in local storage key `swate-termsearchconfig-ctx`. This error broke the app on load. (by @Freymaurer)

## 2.0.0 - 2026-07-14

### ✨ Added

- Workspace component (by @Freymaurer)
- Add ARC-Selector that enables selection of recently used ARCs without searching in the folder structure
- Implement foundation for filetree and combine it with chokidar filewatcher
- Implement Javascript bindings for chockidar and simple git
- Add basic layout for ARCitect, implementing Electron

### 🔄 Changed

- Update to ARCtrl 3.0 and updated dependencies
- Keep FilePicker upload buttons visible so users can overwrite previously selected files without clearing the list (by @Caroott)
- Refactor publishing for swate-components npm library. keep individual files intact, add source maps, update barrel index file. (by @Freymaurer)

## 1.0.7 - 2025-11-21

### ✨ Added

- Use predictive url for term search links where no url is given by db search (by @Freymaurer)

### 🐛 Fixed

- Fix falsy pagination in advanced term search (by @Freymaurer)

## 1.0.6 - 2025-11-20

### 🐛 Fixed

- Fix links in term search results not clickable (by @Freymaurer)

## 1.0.5 - 2025-11-20

### 🐛 Fixed

- Fix too many events triggered by metadata forms #900 (by @Freymaurer)
- Fix missing validation on Investigation identifier (by @Freymaurer)

## 1.0.4 - 2025-11-19

### 🔄 Changed

- Deselect Swate templates after import #923 (by @Freymaurer)

### 🐛 Fixed

- FilePicker no longer inserts all rows and makes them undeletable #921 (by @Freymaurer)
- Fixed issue where "Edit table name"-input would close immediately #925 (by @Freymaurer)

## 1.0.3 - 2025-11-17

### 🐛 Fixed

- Fixed table rendering for Safari #834, by @Gitarackur

## 1.0.2 - 2025-10-29

### 🔄 Changed

- Further unification of modal designs

### 🐛 Fixed

- Update ARCtrl version to fix faulty template import with unit :bug: #905
- Fixed missing "Object Type" column in Datamap #908

## 1.0.1 - 2025-10-29

### 🐛 Fixed

- Added missing field for "Data Format" for Data cells.

## 1.0.0 - 2025-10-29

### 🐛 Fixed

- Added missing version update to docker release pipeline step.

## 1.0.0-rc.12 - 2025-10-28

### Fixed

- Fixes missing FSharp.Core package reference in .NET Server causing runtime errors in docker 🐋.

## 1.0.0-rc.11 - 2025-10-27

### Added

- ARCitect communication for Datamap, Workflow and Run files.

### Deprecated

- Remove datamap tab on assays, studies.

### Fixed

- Release process for npm had a hard exit on check if package exists, which is now removed.

## 1.0.0-rc.10 - 2025-10-27

### Added

- Full support for isa.run.xlsx files
- Full support for isa.workflow.xlsx files
- Full support for separate datamap.xlsx files
- Drag and drop support for file import #635
    - Supports json with specific name restrictions: #532
        - MUST contain json format ("ARCtrl", "ARCtrlCompressed", "ISA", "ROCrate") not case sensitive.
        - MUST contain file type ("Assay", "Study", "Investigation", "DataMap", "Workflow", "Run", "Template") not case sensitive.
        - MUST be separated by an underscore ("\_")
        - MUST have the ".json" file extension
- Term search can be triggered by double clicking a into the input #888

### Changed

- JSON import uses a more direct Select component to choose file type and json format.
- JSON export now checks compatibility of selected file type and json format before displaying options.
- Assay metadata sheet now has fields for: "Title" and "Description".

### Deprecated

- Remove in file support for DataMaps

### Removed

- Restriction of Sidebar to table views.

### Fixed

- Context menu "Fill Column" indexing issue fixed. #891
- Duplicate Comment keys on top level metadata are not validated until any export is triggered (ARCitect, download .xlsx, etc.) #883
- Added whitespace to table components to improve UX when rightmost columns are in use. #859

## 1.0.0-rc.9 - 2025-10-10

### Added

- Last release before start of Changelog

### Changed

- Release workflow
- RELEASE_NOTES.md to CHANGELOG.md

## 1.0.0+41a16f8 - 2024-01-15

### Added

- [[#33e305a](https://github.com/nfdi4plants/Swate/commit/33e305aff9ed93fb78eac8855cdcfd70abed5a3a)] Finish minimal communication. All changes made from Swate should be correctly propagated to ARCitect.
- [[#0694be8](https://github.com/nfdi4plants/Swate/commit/0694be8da5a68c7df6861efe3ee758738dc4305a)] Setup minimal message handler FROM arcitect :sparkles:
- [[#9498472](https://github.com/nfdi4plants/Swate/commit/94984720ea1551230487181e2abb167b4218e7f8)] Allow read in from template xlsx
- [[#bb19ae1](https://github.com/nfdi4plants/Swate/commit/bb19ae16f1075f8f2d620aea464eb863e4c67591)] Interface ARCitect host msgs to swate spreadsheet view
- [[#02b0f05](https://github.com/nfdi4plants/Swate/commit/02b0f05d86389594fd7d9339d386a83e580096b0)] increase split window bar stability
- [[#4eee5ba](https://github.com/nfdi4plants/Swate/commit/4eee5ba80a7a7c398e8a794214a4d1f118b3818b)] Add ARCitect message logic :construction:
- [[#2756826](https://github.com/nfdi4plants/Swate/commit/27568263a2a7211235a689c7217eff14a74ed8e0)] Add arcitect mode by query parameter :sparkles:
- [[#3a345ca](https://github.com/nfdi4plants/Swate/commit/3a345ca2aa38d52e804bdfa5d012d7fb651cac75)] Add default error handling to spreadsheetMsg update
- [[#d508dea](https://github.com/nfdi4plants/Swate/commit/d508deaef2b83c0c615be182eab18e36ab0b524f)] Finish template form :sparkles:
- [[#35d7091](https://github.com/nfdi4plants/Swate/commit/35d7091ef810506ee4d872c3c248128c44683a71)] start working on templates
- [[#8734ae4](https://github.com/nfdi4plants/Swate/commit/8734ae42405e9311fa865a04d54042fbe582c3a3)] Add investigation metadata form :sparkles:
- [[#9c70213](https://github.com/nfdi4plants/Swate/commit/9c70213b497bd10f730fa7c588b1177d254d5cf1)] Update dependency to newest ARCtrl and change json state to arcjson
- [[#4823624](https://github.com/nfdi4plants/Swate/commit/4823624012bba7bbe74e7ffa1ee1dd30bd24ebf1)] improve reactivity of form components :sparkles::art:
- [[#36c6aa2](https://github.com/nfdi4plants/Swate/commit/36c6aa24e969f485a613413529d1c7f6486def8c)] Further develop study metadata sheet.
- [[#e7b4107](https://github.com/nfdi4plants/Swate/commit/e7b4107341908626950d564f71b8774536a74eb1)] Improve oa input robustness
- [[#a015edf](https://github.com/nfdi4plants/Swate/commit/a015edf363b4611a28786a3a4740b32dbf600451)] Greatly improve form input robustness against sim input.
- [[#28b2382](https://github.com/nfdi4plants/Swate/commit/28b23821c8474562472ac80e247fe6fc489c0182)] Start working on metadata input forms :construction:
- [[#d356540](https://github.com/nfdi4plants/Swate/commit/d3565406c7f9392a8b4e8a731b067b9971244005)] Store resize window preference in local storage
- [[#a55a1fb](https://github.com/nfdi4plants/Swate/commit/a55a1fb07d392ead93890c224fe1a8636d97a666)] Reenable reset modal
- [[#5be91a1](https://github.com/nfdi4plants/Swate/commit/5be91a152f4b46651476f2816d9de10426b867c0)] Restore local history :sparkles:
- [[#27aec23](https://github.com/nfdi4plants/Swate/commit/27aec234d30c3c15333437f359f5787453cae52f)] restore basic header functionality :sparkles:
- [[#015de0f](https://github.com/nfdi4plants/Swate/commit/015de0f1ae1d759e46f96d63a66399cdf291a505)] Basic functionality to body cells restored :sparkles:
- [[#0b3543e](https://github.com/nfdi4plants/Swate/commit/0b3543e26e7fd5887966f9a86cf973d70c794074)] improve behavior for switching between column types.
- [[#49981cd](https://github.com/nfdi4plants/Swate/commit/49981cd74e47c572044ff4ebc6a37b215a133c37)] Finish up building block logic :sparkles:
- [[#fad0b26](https://github.com/nfdi4plants/Swate/commit/fad0b2624aa005ddbcfe3a36028e03022806027a)] Adjust main view footer to new model :sparkles:
- [[#e2b32af](https://github.com/nfdi4plants/Swate/commit/e2b32af66862d241173e0c886f3f31482076083c)] Update to .NET 8 and rmv ISADotNet
- [[#9e266e9](https://github.com/nfdi4plants/Swate/commit/9e266e913fb31b8bc7bdbdad21d50a0c4b3b908d)] Make styling passable :art:
- [[#eb8f0ef](https://github.com/nfdi4plants/Swate/commit/eb8f0eff77d388ce7f05e2df7015366268e4605b)] Set up context based styling
- [[#028354b](https://github.com/nfdi4plants/Swate/commit/028354bdaf098933a99a2b05a0fd79f4d64c6976)] Make Swate run again :sparkles:
- [[#b6ebaca](https://github.com/nfdi4plants/Swate/commit/b6ebacafc927953888aec85bc76a591b1bd01f84)] Start updating dependencies, rmv Fulma :fire: rmv ISADotNet :fire:
- [[#fa3d11d](https://github.com/nfdi4plants/Swate/commit/fa3d11d9deb6bc1ea00e19fd2c3830e5b79ae09d)] Update SAFE dependencies, Fable 4
- [[#177348a](https://github.com/nfdi4plants/Swate/commit/177348ab6b27f51e6aee43c007c7396063b9b52a)] Create term modal #296
- [[#6310db2](https://github.com/nfdi4plants/Swate/commit/6310db29ca2daca4059d1f03dbee23a737fa403f)] improve cytoscape term view
- [[#6e1beb8](https://github.com/nfdi4plants/Swate/commit/6e1beb82078a00e0aed4fd0b4e7aee31f8fdae4a)] Apply prerelease version

### Removed

- [[#4135549](https://github.com/nfdi4plants/Swate/commit/41355499bd4a455e38d9d776edc5e507c078ebc3)] hide sidebar shortcut icons outside excel hosting.

### Fixed

- [[#41a16f8](https://github.com/nfdi4plants/Swate/commit/41a16f89f521ca104cfc037579beba7695a3e63d)] fix arcitect json interop
- [[#1e75c76](https://github.com/nfdi4plants/Swate/commit/1e75c7644cdd8b209b5ab32e18da033d3f1e2a7c)] Fix displaying wrong preadsheet elements without active arc file :bug:
- [[#376d2a9](https://github.com/nfdi4plants/Swate/commit/376d2a9427ecb4560ca7deb5346278060513453a)] Fix template request json parsing bug in ARCtrl :bug:
- [[#b7a1141](https://github.com/nfdi4plants/Swate/commit/b7a11418c0696cd6e70da9be355e718d2b28991a)] fix split window resize handler ui :bug:
- [[#a3f7e30](https://github.com/nfdi4plants/Swate/commit/a3f7e30e70cb62990c16ec2f8c7c0d5b57dddf20)] Fix spreadsheet control flow :bug:
- [[#1b551c5](https://github.com/nfdi4plants/Swate/commit/1b551c573c0ab3e8f12ea99382b3b70bb5729a8d)] Fix body term search not triggering
- [[#4d2c5a1](https://github.com/nfdi4plants/Swate/commit/4d2c5a1788025dea42104452f8beab93102dfb64)] fix term-unit switch :bug:
- [[#2827877](https://github.com/nfdi4plants/Swate/commit/28278775f96b7c71a082dafc78d6ed6cf36a7a8a)] fix protocol type for building block :bug:
- [[#a8a6c93](https://github.com/nfdi4plants/Swate/commit/a8a6c932d5ebc8b9e07b8a92414a4c5500893ba1)] Fix dead links :bug:
- [[#d1cf1f5](https://github.com/nfdi4plants/Swate/commit/d1cf1f521dd802b456fbf5dec185af4d1455c44f)] Fix dead installation link :books:

## 0.8.0+6aeb417 - 2023-03-10

### Added

- [[#ffed6aa](https://github.com/nfdi4plants/Swate/commit/ffed6aa3c4a88a1cc9767c9fba69381e11ee7d63)] Add colors to input & output columns #297
- [[#21843de](https://github.com/nfdi4plants/Swate/commit/21843de1fb1217fc30cffa067908fdb81da804a4)] Update activity logging
- [[#1ad927f](https://github.com/nfdi4plants/Swate/commit/1ad927fca4818c2ae1597cc2379c4ea383437bc5)] Add placeholders for empty definitions #277
- [[#5d5a1dd](https://github.com/nfdi4plants/Swate/commit/5d5a1ddede80a6e0558166ac7e120ddef0e8863d)] Disable msg trigger on "Download as Xlsx" when empty #264
- [[#11a5688](https://github.com/nfdi4plants/Swate/commit/11a568808a300d2e90d6dc0d0402d6f45b6142bf)] Redesign add building block function :sparkles:#268
- [[#f7c67ad](https://github.com/nfdi4plants/Swate/commit/f7c67ad7ed5c2a62398736efceb1c7017a1d142d)] Improve search speed in specific ontologies :racehorse: and search units in DPBO (#239, #240)

### Fixed

- [[#83e45cf](https://github.com/nfdi4plants/Swate/commit/83e45cf0572d4382b07cceff181df13c93bb87c9)] Change css from visibility to display :bug:#270
- [[#6aeb417](https://github.com/nfdi4plants/Swate/commit/6aeb4171240c63c86ec55fd17a60d1f24d50c366)] Enable import of protocol type column in templates. :bug:#263
- [[#aba0a53](https://github.com/nfdi4plants/Swate/commit/aba0a536cdf97731b225bb894d0917a2e65684d0)] Fix ctrl + click on quick access buttons on mac #273
- [[#752a631](https://github.com/nfdi4plants/Swate/commit/752a631a9d4cd0bd4f465f3330ed56213b5dd6dc)] Fix strange search result order (#247)

## 0.7.1+a5a7d62 - 2022-12-14

### Added

- [[#a5a7d62](https://github.com/nfdi4plants/Swate/commit/a5a7d62a24ef1545d416e32c1b8be47944d0ed02)] Enable api search by source ontology :sparkles: :books:

### Fixed

- [[#bfae9a8](https://github.com/nfdi4plants/Swate/commit/bfae9a87886bfdf18612c46bb1416d35aaf258b8)] Enable CORS

## 0.7.0+d648b9c - 2022-12-13

### Added

- [[#e9e9c80](https://github.com/nfdi4plants/Swate/commit/e9e9c80f02e3e24a1c7497c5cd5c4420d371039a)] Allow adding multiple building blocks of the same type (#243).
- [[#5ba5744](https://github.com/nfdi4plants/Swate/commit/5ba57448149846952f562614019df1eed22052c2)] Add tests for new indexing :white_check_mark:
- [[#92cbb31](https://github.com/nfdi4plants/Swate/commit/92cbb31504270c77a3188bff559078a07b384261)] Refactor protocol search view :art:
- [[#a8584b0](https://github.com/nfdi4plants/Swate/commit/a8584b04658a0d1f0ca0f453364ca66b08bd8fe9)] Add template search options (#244).
- [[#d648b9c](https://github.com/nfdi4plants/Swate/commit/d648b9c24ad8f0fb4309c1de0a24e4ccbbee54d8)] Enable CORS

### Fixed

- [[#a5ff2cd](https://github.com/nfdi4plants/Swate/commit/a5ff2cd16ea47d6779e4cea38adf636962f9e375)] Fix runtests build target
- [[#50b4e98](https://github.com/nfdi4plants/Swate/commit/50b4e9861ded58f59d9dc7f6f7c557db4089679d)] Fix issue with duplicate columns and non term columns :bug:

## 0.6.3+b859e8e - 2022-12-01

### Added

- [[#b859e8e](https://github.com/nfdi4plants/Swate/commit/b859e8ed05e57c9ff418bb125d4ab6b4aa0576af)] Link open api docs in info tab
- [[#0f56988](https://github.com/nfdi4plants/Swate/commit/0f56988862fe0f37326116ea6b98d54eac31e342)] Update IOntologyAPIv2 to client
- [[#c89d554](https://github.com/nfdi4plants/Swate/commit/c89d5542c92d8c1c15b3e2fdc3e3e2320f774b4c)] Add documentation for IOntologyAPIv2 :books:
- [[#8989a05](https://github.com/nfdi4plants/Swate/commit/8989a05d1039bdc549faba13575ab6d4b0bc2185)] Set up SwaggerUI docs logic
- [[#275fdcc](https://github.com/nfdi4plants/Swate/commit/275fdcc1439682d7809c6ce31312a92017d8c8fe)] Improve search performance for all children/parents :racehorse:

### Fixed

- [[#14b2feb](https://github.com/nfdi4plants/Swate/commit/14b2feb8e403a8da16f3dac785c0f081123e7cf4)] Fix whitespace in username install issue :bug:

## 0.6.2+6a4f5b0 - 2022-10-19

### Added

- [[#b0cd88f](https://github.com/nfdi4plants/Swate/commit/b0cd88f082e20200e1e1f8bf7661f849f7c2f54c)] Update docker-compose
- [[#4c9a625](https://github.com/nfdi4plants/Swate/commit/4c9a625f51128808d33371cf8dd3d49bb147a9ca)] Improve relationship search performance (Issue #228).
- [[#13720b4](https://github.com/nfdi4plants/Swate/commit/13720b43da382f55a1f47265b4378070111f317b)] Update Protocol Type column to DPBO.

### Fixed

- [[#28f5bc8](https://github.com/nfdi4plants/Swate/commit/28f5bc8dfe3bd46c10bcbb80b8d8f40a1a735f4e)] Fix wrong shift of ref cols (Issue #231) and change default cell type to "text".
- [[#2a910e1](https://github.com/nfdi4plants/Swate/commit/2a910e1dfd7176265b7eda9f7ff72deebf865efc)] Fix duplicate results and further improve search performance (Issue #230, #228) :bug::racehorse:
- [[#b09058d](https://github.com/nfdi4plants/Swate/commit/b09058da8aeef94d38d0f47654590e6254f9d2f1)] Remove min length constraint on term search (Issue #226) :bug:
- [[#807e643](https://github.com/nfdi4plants/Swate/commit/807e643e59eb2739da1520f5e51d8186482eaa04)] Fix term search field size when parent term name too long (Issue #225) :bug:

## 0.6.1+a1732c7 - 2022-09-22

### Added

- [[#a1732c7](https://github.com/nfdi4plants/Swate/commit/a1732c775644081211e079bc708a3fc7e52cc283)] Redesign advanced term search (#222).
- [[#37a09cf](https://github.com/nfdi4plants/Swate/commit/37a09cfcc3ddc93e49023fbdab746d3b6e554877)] Simplify Json-Import :sparkles:
- [[#f624141](https://github.com/nfdi4plants/Swate/commit/f624141367ec2573e8f1e39066f2cbeedc51edf0)] Merge branch 'kevinf-patch-0.6' of https://github.com/nfdi4plants/Swate into kevinf-patch-0.6

## 0.6.0+0ba730b - 2022-09-07

### Added

- [[#d0ce176](https://github.com/nfdi4plants/Swate/commit/d0ce1769f4982f146e469d738f0023f61193f499)] Update to new url: https://swate.nfdi4plants.org
- [[#1ccef83](https://github.com/nfdi4plants/Swate/commit/1ccef839ce3b556d06d6b1b381ec919decd8ece2)] Update building block descriptions.
- [[#5a2595a](https://github.com/nfdi4plants/Swate/commit/5a2595a4a296997d48ca63f9a0d0ef3fecdf0b2c)] Add protocol type functionality
- [[#c6d3836](https://github.com/nfdi4plants/Swate/commit/c6d38366b1bbae50c519fbe2d1667962c0abe6d8)] Adjust changes according to Swobup naming
- [[#bd46fff](https://github.com/nfdi4plants/Swate/commit/bd46fffb2fb9e60b20fbfd7dd6e4cd2d5e3b5efa)] Support official ISADotNet syntax for column headers.
- [[#9368483](https://github.com/nfdi4plants/Swate/commit/93684832de507a52144e8706563d5ef8ba6486f2)] Separate #id calculation of main col and ref cols.
- [[#ba7bad8](https://github.com/nfdi4plants/Swate/commit/ba7bad810b2562d3a348543c889ae8139d04df95)] Make input/output columns cambiable (Issue #203) + improve office logging :sparkles:
- [[#f3be11c](https://github.com/nfdi4plants/Swate/commit/f3be11c6955e512e8b0e7acfc3b13a2b2144f88c)] Update Data File output column + deprecation checks (Issue #201).
- [[#1f5f888](https://github.com/nfdi4plants/Swate/commit/1f5f888739227f5e2603de5e6063edbd21903bc3)] Update Source column description.
- [[#63a6a29](https://github.com/nfdi4plants/Swate/commit/63a6a298c07e7fcc56b80583442406692a1183f2)] Add Protocol REF (Issue #212).
- [[#4711417](https://github.com/nfdi4plants/Swate/commit/47114172cb6f8c70745e1daad0f00cc7e4b170b9)] Add unit tests (Issue #212) :white_check_mark:
- [[#30ba1cd](https://github.com/nfdi4plants/Swate/commit/30ba1cd12122ee876f3567c1e39d7e864d891853)] Add Component building block :sparkles::white_check_mark:
- [[#71e633f](https://github.com/nfdi4plants/Swate/commit/71e633f5be4a93b2a8031433644452e30d6c0a0d)] Unify autocomplete term search design (Issue #211).
- [[#3f9bff1](https://github.com/nfdi4plants/Swate/commit/3f9bff14ee68bdb727fc03df8177c14c7b0ad489)] Redesign search results
- [[#8522dea](https://github.com/nfdi4plants/Swate/commit/231c649f140d251bd8cfac7e3595540e00cae03c)] Clean up first showcase of tree term structure #209
- [[#8dfa9e2](https://github.com/nfdi4plants/Swate/commit/8dfa9e2f0dfa9e5a42e8030adac041581d549913)] Heavily improve term insert feedback in annotation table (Issue #149, #161)
- [[#82b896c](https://github.com/nfdi4plants/Swate/commit/82b896c152eee2b411ef8ab506b7932694144f92)] Clean up readme :shower:
- [[#18641cd](https://github.com/nfdi4plants/Swate/commit/18641cd50a5d7854aaf2a39976479d4cda2d12e7)] Update fill reference column logic, to provide consistent results.
- [[#2b2413f](https://github.com/nfdi4plants/Swate/commit/2b2413f3f051b096dde8d8322bbafaed6d4ac616)] Implement and/or slider for template tag filter (Issue #195) :sparkles:.
- [[#bcf077c](https://github.com/nfdi4plants/Swate/commit/bcf077c6b910d9c8c2bdd8402e8953f2f34af657)] Update info page :lipstick: and contact links (Issue #196).
- [[#f776367](https://github.com/nfdi4plants/Swate/commit/f7763677a81e3913468d2a8c9304cc7da604d5d9)] Update project for docker build automation :whale:
- [[#432c14b](https://github.com/nfdi4plants/Swate/commit/432c14b33dee96bfa1074d0078e5dda04b4b6b9b)] Improve parent-child search performance (Issue #117,#193) :racehorse:.
- [[#7b360e8](https://github.com/nfdi4plants/Swate/commit/7b360e862b19e42eddca47f4ca8245577296c24d)] Update Advanced term search for cleaner input.
- [[#bfec630](https://github.com/nfdi4plants/Swate/commit/bfec63094cbc78cb28644d53df8d654cde087eb4)] Persist active tabs over subpages (Issue #191) :lipstick:.
- [[#92d973c](https://github.com/nfdi4plants/Swate/commit/92d973cd6a33144c2eafe52eb84b7b52c1fbea15)] Update navbar burger menu to always be visible (Issue #194).
- [[#bd8e3fb](https://github.com/nfdi4plants/Swate/commit/bd8e3fb946cbe956a730de58508b3a94ae2c27d4)] Separate ER tags from other tags and add curated vs community templates badge (#187, #186).

### Fixed

- [[#55fa82c](https://github.com/nfdi4plants/Swate/commit/55fa82cc5df5a4f73acf20707e45578f6552602c)] Improve AddBuildingBlock unit logic :bug::hammer:
- [[#0b5be6d](https://github.com/nfdi4plants/Swate/commit/0b5be6d8bced276643ebab21de0c6962a5a0b695)] Fixed strict parsing on empty fields (Issue #214) :bug:
- [[#0dd0c91](https://github.com/nfdi4plants/Swate/commit/0dd0c9157e1e3cad213bcb3a68b4393318ff7585)] Fix clipping issue in building block info :bug:
- [[#5e45ac8](https://github.com/nfdi4plants/Swate/commit/5e45ac8cbb8a6a031bafeaf49c407b7fd8ee3ba7)] Fix propagation of unit to new building blocks (Issue #183) :bug:.

## 0.5.3+0eaa644 - 2021-12-16

### Added

- latest commit #0eaa644

### Fixed

- [[#85c47e0](https://github.com/nfdi4plants/Swate/commit/85c47e0cefe40e038fbcd93ec360f92e5ec6efb2)] Update installer (Issue #181).
- [[#0eaa644](https://github.com/nfdi4plants/Swate/commit/0eaa6446e70d858720a1ea3428083cd3b379360a)] Fix accidental value insert from template db.
- [[#2039287](https://github.com/nfdi4plants/Swate/commit/20392878a667a6c563a389078629f3526766c4a3)] Fix templates only inserting as characteristic.

## 0.5.2+abcc754 - 2021-12-14

### Added

- latest commit #abcc754
- [[#681d9c9](https://github.com/nfdi4plants/Swate/commit/681d9c9ef7ac4afb12f912ad99f1b5f0f288c26f)] Add new installer using shared folders! This allows Swate installation without the microsoft store and any dependencies.
- [[#9154dd5](https://github.com/nfdi4plants/Swate/commit/9154dd5eacc20cc5e6be78b554f1226abc298996)] Split Swate.Core and Experts into different add-ins :tada:

### Fixed

- [[#abcc754](https://github.com/nfdi4plants/Swate/commit/abcc754fddc57b9f9785670569ef352fb5548e56)] **Try** parse number format (fix Issue #180).

## 0.5.1+0c90a83 - 2021-12-08

### Added

- latest commit #0c90a83
- Add support for Excel 2019 :tada:
- [[#48d8d61](https://github.com/nfdi4plants/Swate/commit/48d8d61409d1404b9bd254241e54742ced36b357)] Replace drag and drop for filepicker with simplified table sorting element
- [[#e16c861](https://github.com/nfdi4plants/Swate/commit/e16c8613acde31738ecf0af09bee503feea7246a)] Add whitespace between authors.
- [[#7b0884f](https://github.com/nfdi4plants/Swate/commit/7b0884f9d2c76bae5d59148acecd7b4165739587)] add pure json return for common api

### Fixed

- [[#2a09a55](https://github.com/nfdi4plants/Swate/commit/2a09a55120261db8527b1c80e63a50cb0ecfe4e3)] Split template tags by "," instead of ";"
- [[#c82dd4f](https://github.com/nfdi4plants/Swate/commit/c82dd4f3ef72a21af8f0896ea0cb103811f2c198)] Fix server port error for production.

## 0.5.0+70632951 - 2021-10-22

### Added

- latest commit #70632951
- Column headers slimmed, only term accession number in brackets
- Unit section slimmed from three columns to one
- Units tracked with number section in Excel
- Only terms from unit ontology (UO) allowed
- Protocol insert is now called template insert and more generalized
- Building blocks can only be inserted once
- When trying to insert duplicate template no error is thrown, but no building block duplicates are inserted.
- [[#6c62234](https://github.com/nfdi4plants/Swate/commit/6c62234aeda2d232a6054b278fff4785d8902aba)] Add visualization for source-protocol-sample chains :sparkles:
- [[#d080748](https://github.com/nfdi4plants/Swate/commit/d080748dd90510f676aefc43aec6905bd3539830)] Add import from json to **multiple** annotation tables :sparkles:
- [[#16ec188](https://github.com/nfdi4plants/Swate/commit/16ec1880b99d1d0b102d965f413c29ef47fe1669)] Improve server side error feedback.
- [[#e63bffc](https://github.com/nfdi4plants/Swate/commit/e63bffc767f61939e09b7f2f4f4cfac950babe21)] Make protocol preview table scrollable :lipstick:
- [[#8d83591](https://github.com/nfdi4plants/Swate/commit/8d83591f25ad20f9497db166072e4b1bc8e37ddf)] Improve template insert performance.
- [[#f6ebef3](https://github.com/nfdi4plants/Swate/commit/f6ebef39294b76a762bc15426fde657e4cb25e40)] Allow each building block only once per sheet.
- [[#2cbbaef](https://github.com/nfdi4plants/Swate/commit/2cbbaef34888a8d7db6bde06ef30eaa4ff8705bd)] Allow only one output column type per table.
- [[#1e0a3f4](https://github.com/nfdi4plants/Swate/commit/1e0a3f41103082408ebec21e2782437fcd46d0ba)] Update table name generator.
- [[#6138c65](https://github.com/nfdi4plants/Swate/commit/6138c654002b16f40225747929be39108dcab011)] Update visuals :sparkles: (Issue #162).
- [[#cc81c47](https://github.com/nfdi4plants/Swate/commit/cc81c476c1d9e722d189f1392433a5b60b688097)] Update name for protocol templates (Issue #153).
- [[#4187c99](https://github.com/nfdi4plants/Swate/commit/4187c993f08f461c7df8210886c2f55d96e8b586)] Improve building block info (Issue #160).
- [[#25e0253](https://github.com/nfdi4plants/Swate/commit/25e02539f944ba93ab094b6f1c488b19b811e13a)] Refactor Settings :hammer:
- [[#ff96e2a](https://github.com/nfdi4plants/Swate/commit/ff96e2a58e0ec8f9252f07acb284e7a23d3474f3)] Improve client logging.
- [[#c885c0c](https://github.com/nfdi4plants/Swate/commit/c885c0c14d85bfc7424d4f800cb490300af816e5)] Track template metadata with worksheet and provide ease-of-access function.
- [[#8df5246](https://github.com/nfdi4plants/Swate/commit/8df52468cd903273fcc81c676431122764a77857)] Add hide-reference-columns option to autofit table :sparkles:.
- [[#db9b9e1](https://github.com/nfdi4plants/Swate/commit/db9b9e12787e672bd3ec88553118aa2ca6acb30f)] Add annotationTable create with prev output auto-insert (Issue #168).
- [[#b07aca5](https://github.com/nfdi4plants/Swate/commit/b07aca5783dde95009853a9aaad00d4c679f9763)] Add option to export Swate tables as json files.
- [[#c020fea](https://github.com/nfdi4plants/Swate/commit/c020fea3722d08bde5a2aeea2fe52277fd3fd2ee)] Add json export from external xlsx files :sparkles:
- [[#b7f9920](https://github.com/nfdi4plants/Swate/commit/b7f9920dfe7653a482ee5af44687288eb7c9287d)] Add Common API to backend.
- [[#e14c648](https://github.com/nfdi4plants/Swate/commit/e14c6482488c3755254f038015c6b2aba2b55d05)] Update Protocol search and filter functionality.
- [[#7194b96](https://github.com/nfdi4plants/Swate/commit/7194b96c386b665c7fd3ef254258b48d5a96d8f7)] Start updating unit to ISA conformity :fire:
- [[#6ba34f2](https://github.com/nfdi4plants/Swate/commit/6ba34f2268267e4c7686f547dce707e8b4b93700)] Improve performance with update to SAFE stack v3.

### Removed

- [[#8302630](https://github.com/nfdi4plants/Swate/commit/830263002d8429ec5016fefc91ec100228e9c496)] Remove definition field from ontologies.

### Fixed

- [[#8f33b3b](https://github.com/nfdi4plants/Swate/commit/8f33b3b6e1a62f7d94e023cc791bd7e332369f1a)] Fix index error in json exporting rows :bug:
- [[#0fb73e2](https://github.com/nfdi4plants/Swate/commit/0fb73e2b2786e2b9dfbf12ed17d983809b7cbabf)] Update navbar to stay fixed.
- [[#6afcd91](https://github.com/nfdi4plants/Swate/commit/6afcd9102bd3e858c46621dda3e6fc804abc6afe)] Fix context update issues in interop functions.
- [[#4fb63e9](https://github.com/nfdi4plants/Swate/commit/4fb63e9767da0fe82479c42cbd13b9f0c89b0209)] Fix warning modal when just entering validation subpage :bug:

## 0.4.8+7960150 - 2021-09-08

### Added

- latest commit #7960150
- [[#4796598](https://github.com/nfdi4plants/Swate/commit/47965986e914f30b3b438bc44ede81308dc16d39)] Update SQL db dump for development.
- [[#78458db](https://github.com/nfdi4plants/Swate/commit/78458db2338863907ce258111511a7c8ad9232bb)] Merge pull request #144 from nfdi4plants/kevinf-patch-0.4.7
- [[#5b46ded](https://github.com/nfdi4plants/Swate/commit/5b46deda34e38fbd04f0f6f5693aa1a0bd9cfca6)] Update README.md
- [[#ef96958](https://github.com/nfdi4plants/Swate/commit/ef96958b4d0929f80273ddc7b90659274bdbcc91)] Update for demo server

## 0.4.7+23edde8 - 2021-05-12

### Added

- latest commit #23edde8
- [[#23edde8](https://github.com/nfdi4plants/Swate/commit/23edde88061148ee37a038a44324f338d3d4ee88)] Add redo search on double click to building block input elements.
- [[#b10131b](https://github.com/nfdi4plants/Swate/commit/b10131b5a9957824f8957227ff8b192434adc62e)] Update database, add part_of Term relationships

## 0.4.6+89aa7bc - 2021-03-12

### Added

- latest commit #89aa7bc
- [[#89aa7bc](https://github.com/nfdi4plants/Swate/commit/89aa7bc8a2e31b8598e0e0b916733f211035fa4c)] Add Spawn API endpoints.

## 0.4.5+b360273 - 2021-03-12

### Added

- latest commit #b360273
- [[#d630a76](https://github.com/nfdi4plants/Swate/commit/d630a76445f1845bb93de9ae89bf1e983f81c51f)] Save darkmode as cookie and improve darkmode (Issue #134).

### Fixed

- [[#b360273](https://github.com/nfdi4plants/Swate/commit/b36027369f60a612e2660ec3b8bfefb09cbe5664)] Fix bug, removing protocol groups if one column is not an ontology.
- [[#402a220](https://github.com/nfdi4plants/Swate/commit/402a220824ee69ebd1cb5e6f4ecd2ba4cf1627f7)] Fix pointer .json generator (Issue #139).

## 0.4.4+4ae3198 - 2021-03-11

### Added

- latest commit #4ae3198
- [[#4ae3198](https://github.com/nfdi4plants/Swate/commit/4ae31986bc4b1a24ccead037afbde4ced9ed7a44)] Update DB protocol.

## 0.4.3+9b7d7fe - 2021-03-09

### Added

- latest commit #9b7d7fe

### Fixed

- [[#9b7d7fe](https://github.com/nfdi4plants/Swate/commit/9b7d7fe5ca562fee346227430c1fc1aebffa3b10)] Remove bugs with protocol update :bug:

## 0.4.2+ae04aa5 - 2021-03-09

### Added

- latest commit #ae04aa5

### Fixed

- [[#ae04aa5](https://github.com/nfdi4plants/Swate/commit/ae04aa51261e614d0c422f03d646e1a76d664501)] Stabilize protocol insert against bugs :bug:

## 0.4.1+d75743c - 2021-03-08

### Added

- latest commit #d75743c
- [[#0d9c945](https://github.com/nfdi4plants/Swate/commit/0d9c94558052d7f13e9707da6202e3a3f34440b9)] Add links to template repository.
- [[#6b5a56f](https://github.com/nfdi4plants/Swate/commit/6b5a56f5786eb356703438ecffcb768a6444abcb)] Improve darkmode (Issue #25).
- [[#37503a5](https://github.com/nfdi4plants/Swate/commit/37503a50786536ba88a72d591e2cf51fdfd113dc)] Enable term search without present annotation table (Issue #132).
- [[#05a69b3](https://github.com/nfdi4plants/Swate/commit/05a69b323db6325d1309b7fbd5cf5b7f4279308e)] Increase responsiveness for copy to clipboard.
- [[#44a75d1](https://github.com/nfdi4plants/Swate/commit/44a75d12c1583e138ed2cc328146922d14752d4f)] Add warnings to advanced setting functions.
- [[#7d4060b](https://github.com/nfdi4plants/Swate/commit/7d4060b15def48f17b64dd42d0e1da207a3285cd)] Add function to update used protocols. :sparkles:
- [[#088335f](https://github.com/nfdi4plants/Swate/commit/088335f811d41026269e2489337986331534c4a6)] Add option to update raw custom xml (Issue #123).
- [[#a3286eb](https://github.com/nfdi4plants/Swate/commit/a3286ebcefe217bbc4354c9e19fe79004d7afb6d)] Add checksum content type (Issue #127, Issue #131).
- [[#97407d4](https://github.com/nfdi4plants/Swate/commit/97407d45c5139ded3234824f46c530e68e0556a1)] Changed DateTime to use UTC (Issue #126).
- [[#137cc54](https://github.com/nfdi4plants/Swate/commit/137cc542db62fecb52fad77177bb6de1a72c1965)] Add more info for existing building blocks (Issue #124).
- [[#66fb577](https://github.com/nfdi4plants/Swate/commit/66fb5771c55632c4cc0bf229996d8fa4cd304a69)] Add option to create pointer json template (Issue #129).

### Removed

- [[#84d71ee](https://github.com/nfdi4plants/Swate/commit/84d71eef62c1d55bb2130143926d47e8b462fdeb)] Remove 'decimal' validation type.

### Fixed

- [[#d75743c](https://github.com/nfdi4plants/Swate/commit/d75743cc4597ab9ba557ea9522e6beea091db209)] Add minor fixes
- [[#bd13cbf](https://github.com/nfdi4plants/Swate/commit/bd13cbf39f013277381b04bb9f30577d2a929f42)] Fix drag n drop problems in filepicker.
- [[#33695f4](https://github.com/nfdi4plants/Swate/commit/33695f429ac6aa76e8638b9d5b375921b3d856bd)] Fix protocol grouping bug.
- [[#f4d08e8](https://github.com/nfdi4plants/Swate/commit/f4d08e8f1f41c712ff787ce231e4c085795eef2a)] Fix protocol xml not correctly removed bug.

## 0.4.0+a0e04f3 - 2021-03-01

### Added

- latest commit #a0e04f3
- [[#24950d1](https://github.com/nfdi4plants/Swate/commit/24950d160548a04e080b7bd283a699b611a116a6)] Minor visual updates
- [[#183a80c](https://github.com/nfdi4plants/Swate/commit/183a80c31f823ad56706459dacc631fd2da0becb)] Update dropdown navbar quick access.
- [[#4b818db](https://github.com/nfdi4plants/Swate/commit/4b818db47d9662964be0515075945a3fa4b3261c)] Add Advanced custom xml settings (Issue #111).
- [[#d7cce09](https://github.com/nfdi4plants/Swate/commit/d7cce0939cfdd1212e8ea1e4f12863d77280d50e)] Add link to nfdi4pso issues (Issue #99).
- [[#848acf7](https://github.com/nfdi4plants/Swate/commit/848acf7092daf70a9f8ae6f129b58751cfe14191)] Add "Update unit" functionality (Issue #110).
- [[#5118778](https://github.com/nfdi4plants/Swate/commit/5118778e38a95a1e71e7a80488fd2a5e9fd63715)] Rename validation to checklist
- [[#58b58a4](https://github.com/nfdi4plants/Swate/commit/58b58a42eef1ee08f63059ae2e971e74f8d29b15)] Add drop down for quick access icons
- [[#3778ebc](https://github.com/nfdi4plants/Swate/commit/3778ebc951857295234d2c6d12bacce27bf29fd6)] Add copy to clipboard to term search for vertical term insert (Issue #118).
- [[#eff46ae](https://github.com/nfdi4plants/Swate/commit/eff46aec41e4f0eb529d7f37ac789f352c85f5b4)] Restructure CustomXml :hammer::boom:
- [[#44d9277](https://github.com/nfdi4plants/Swate/commit/44d9277901a8cb617bca1b78be679c12b4fc362b)] Add option to show all child terms (Issue #114).
- [[#746ecf4](https://github.com/nfdi4plants/Swate/commit/746ecf4c3036f3c68b92d4d74c37118e491f83c1)] Redo autocomplete search on double click.
- [[#454ccd7](https://github.com/nfdi4plants/Swate/commit/454ccd7368e61ff5b669b197c5cd3d0ade7b1c6b)] Add database template logic (Issue #10, #107) :sparkles:
- [[#19a2f73](https://github.com/nfdi4plants/Swate/commit/19a2f739688a4819cf3275de348d41afa1351fc3)] Add button to display building block information (Issue #96).
- [[#71801ff](https://github.com/nfdi4plants/Swate/commit/71801ff2e558834ed5bb413d8b62d2f7eea48419)] Add 'Remove Building Block' button (Issue #102).
- [[#80c6235](https://github.com/nfdi4plants/Swate/commit/80c6235759e13263316e4c8f60b9d0f5eb7bc947)] Improve term search search speed.
- [[#7760257](https://github.com/nfdi4plants/Swate/commit/7760257839839a03641ba5172d46fe77d97353dc)] Improve addition of validation importance (Issue #113).

### Fixed

- [[#ba4f238](https://github.com/nfdi4plants/Swate/commit/ba4f2389644d5f5adcb0460ab498eeccea96a84c)] Fix bug not finding the correct selected building block (Issue #121).
- [[#41e298d](https://github.com/nfdi4plants/Swate/commit/41e298d4369d320331b50c7d08494567daac7004)] Fix updating protocol group header bug if split too often (Issue #120).
- [[#01d5cf5](https://github.com/nfdi4plants/Swate/commit/01d5cf5db6152ca16d8fa765c80fd9034d8c9f8e)] Fix protocol group headers not correctly removed bug (Issue #119).

## 0.3.1+cbc655c - 2021-02-12

### Added

- latest commit #cbc655c

### Fixed

- [[#cbc655c](https://github.com/nfdi4plants/Swate/commit/cbc655cd6c9692480ac46c894127831aaea5b713)] Fix protocol headers shifted if not placed in row B (Issue #108) :bug:

## 0.3.0+0d31c43 - 2021-02-11

### Added

- latest commit #0d31c43
- [[#4bf33cb](https://github.com/nfdi4plants/Swate/commit/4bf33cb478861250a3f1794140821460115e3173)] Add ontology accession number as tag in ref columns (Issue #100).
- [[#262dae3](https://github.com/nfdi4plants/Swate/commit/262dae32acef085d3bfff46c1194f80698278387)] Add option to write process.json to Swate annotation table (Issue #84). :sparkles:
- [[#09467d9](https://github.com/nfdi4plants/Swate/commit/09467d97813b708ce8bee58935b0b5830aea15f7)] Visually group building blocks to protocols (Issues 101#, #103, #104) :sparkles:
- [[#0516353](https://github.com/nfdi4plants/Swate/commit/05163533c6832023301e588ccc59b34af5b18f88)] Add Logos and visually update Swate (Issue #59).
- [[#5c2e56a](https://github.com/nfdi4plants/Swate/commit/5c2e56a46b57fa627c5b37b7e8307ab633a4e12b)] Add option to add unit cols to existing building block (Issue #94).
- [[#9987184](https://github.com/nfdi4plants/Swate/commit/99871849bc83cfa4bd4fe7760c2f43dae524d76b)] Add sorensen dice sorting to advanced term search (Issue #95).
- [[#9158bb7](https://github.com/nfdi4plants/Swate/commit/9158bb75696399492050109ebb0d04be59eeb9b6)] Update unit search to only search UO ontology (Issue #93).
- [[#4e0d0c9](https://github.com/nfdi4plants/Swate/commit/4e0d0c9e32c5be606542f9ca0f05b74be6626e1d)] Add easy to access navigation option to advanced search (Issue #91).
- [[#374e326](https://github.com/nfdi4plants/Swate/commit/374e326f2123a2f61825f281bc4886b109d5261d)] Add features from #68 to Update Reference Columns (Issue 87#).

### Removed

- [[#9da9c55](https://github.com/nfdi4plants/Swate/commit/9da9c55a23d737aa05ff7c12759446ce5387902f)] Remove event handlers (input assist, #87).

### Fixed

- [[#0d31c43](https://github.com/nfdi4plants/Swate/commit/0d31c43ff55961b7eed6d91183c4c91c85356c59)] Fix bug not opening "File Picker" upload window on click.
- [[#8606b12](https://github.com/nfdi4plants/Swate/commit/8606b12fe5497c2fbea2659cafd123c9a22dfe34)] Add Protocol Xml logic and fix ISADotNet dependency.
- [[#51928c0](https://github.com/nfdi4plants/Swate/commit/51928c0af9b5d5a39dbc54abbadc3aa81e8580f8)] Fix minor routing icon mismatch.
- [[#fdcb58c](https://github.com/nfdi4plants/Swate/commit/fdcb58c71ce624879448c4e86e2119b72bc877ae)] Fix minor white/lightgrey mix ups in filepicker view.
- [[#cc26e81](https://github.com/nfdi4plants/Swate/commit/cc26e81e895d7ec7fc7abef885e5d7afb4c0a7c2)] Fix bug overloading computers when creating an annotation table for whole rows (Issue #63).
- [[#1e5eb3d](https://github.com/nfdi4plants/Swate/commit/1e5eb3d6c2b0f2f0527d4843c8bc0addabdb0b04)] Fix reset of unit search input when unchecking (Issue #92).
- [[#474cf73](https://github.com/nfdi4plants/Swate/commit/474cf73cb48227d58e88d239e6e9e50e8676c78a)] Fix bug creating wrong TAN with insertTerm.

## 0.2.0+899b535 - 2021-01-11

### Added

- latest commit #899b535
- [[#1182030](https://github.com/nfdi4plants/Swate/commit/1182030d57695643e9b333f0bfbdfe11e64ceab2)] Add Setting Page
- [[#d4a36f1](https://github.com/nfdi4plants/Swate/commit/d4a36f1e3417f5e49c184392e30d95d353f54a07)] Provide validation information via XML metadata (Issue #45). :christmas_tree: :fireworks:
- [[#f3a11f0](https://github.com/nfdi4plants/Swate/commit/f3a11f0257f5d7d25a67dfdb85700903573d9ec1)] Update FilePicker with reordering functionality (Issue #13).
- [[#f6564d6](https://github.com/nfdi4plants/Swate/commit/f6564d65c9985c82cbad3b482792e94379a7b34b)] Add search term search by accession number (Issue #71).
- [[#bdba3ae](https://github.com/nfdi4plants/Swate/commit/bdba3ae061d4c0aa473eef19ab2c55586582c462)] Properly Document Office interop functions (Issue #75).
- [[#aa870f1](https://github.com/nfdi4plants/Swate/commit/aa870f1c2d40a20f6dc71bb6fcc0a7d4ace49847)] Update README.md
- [[#e958024](https://github.com/nfdi4plants/Swate/commit/e958024d7ac0f804107eaf55fb66e74e966acd63)] Improve readme :book:

### Fixed

- [[#9c07338](https://github.com/nfdi4plants/Swate/commit/9c07338a624240d1f3119cee243164186c5203b2)] Fix input assistance is not added when first table is created (Issue #82).
- [[#889b86c](https://github.com/nfdi4plants/Swate/commit/889b86c466c454e736daf950ac0df4f77dcb6355)] Fix file picker not uploading reoccuring file names (Issue #80).

## 0.1.3+c6ad5b7 - 2020-12-07

### Added

- latest commit #c6ad5b7
- [[#c6ad5b7](https://github.com/nfdi4plants/Swate/commit/c6ad5b7271ea5ff4ccc51f7022722bfc95b7b116)] Add error modal (Issue #73).
- [[#6cb6ebf](https://github.com/nfdi4plants/Swate/commit/6cb6ebf718b6184a769b7eaa67e341331cb4c1b5)] Add core functionality to File Picker (Issue #13).
- [[#e04e953](https://github.com/nfdi4plants/Swate/commit/e04e95345d473fd6b69b196c5e4b11939aa3b6df)] Add link to ontobee page for ontology in term search suggestions (Issue #69).
- [[#13f3639](https://github.com/nfdi4plants/Swate/commit/13f3639c7181292ccc3764e2b31d5ae91f1f4dcf)] Create issue templates
- [[#5abee29](https://github.com/nfdi4plants/Swate/commit/5abee298f349005b609ebc21eaf70a0e76e5c5d8)] Add Unit Column when selecting a unit for a term (Issue #48).
- [[#5abee29](https://github.com/nfdi4plants/Swate/commit/5abee298f349005b609ebc21eaf70a0e76e5c5d8)] Add option to fill hidden cols according to main column (Issue #67).
- [[#5abee29](https://github.com/nfdi4plants/Swate/commit/5abee298f349005b609ebc21eaf70a0e76e5c5d8)] Add input assist to delete hidden col cells onChange of main col (Issue #68).

### Fixed

- [[#0a9ac89](https://github.com/nfdi4plants/Swate/commit/0a9ac899d5e5d1609b0dfdc0bd01a46e6e829735)] Fix bug of term input field not indicating change after using autocomplete suggestion.
- [[#c4befec](https://github.com/nfdi4plants/Swate/commit/c4befecbf4141066ce9aebb8b6014b1f81eaddea)] Fix bug where auto fill would delete some rows of first column.
- [[#2498c0b](https://github.com/nfdi4plants/Swate/commit/2498c0beee9433e5a7a71cb7661956a6f0b6a609)] Fix file picker button view.
- [[#eb104fe](https://github.com/nfdi4plants/Swate/commit/eb104fe72f8b253e8afdb24378836dfec55e0d6c)] Fix bug where search results from "Advanced Search" are not selectable (Issue #70).
- [[#63aa8ea](https://github.com/nfdi4plants/Swate/commit/63aa8ea3c0c36e2a821e92429331146a2a054b44)] Fix bug where cursor jumps to the end of search input field (Issue #66).
- [[#1847bf5](https://github.com/nfdi4plants/Swate/commit/1847bf5097bc8e87861ab431e05826c19f8fd3f4)] Fix (visual) pagination components for advanced term search (Issue #65).
- [[#d207770](https://github.com/nfdi4plants/Swate/commit/d207770a880261a2e293c721b90fc69925abc48a)] Fix not shown `No Ontology` option in advanced term search (Issue #64).
- [[#1fd3f67](https://github.com/nfdi4plants/Swate/commit/1fd3f6716b2a1f3fc5a1bdbefb7728ef862d4627)] Fix minor bug in release notes creation.

## 0.1.2+af67a92 - 2020-11-26

### Added

- latest commit #af67a92
- [[#af67a92](https://github.com/nfdi4plants/Swate/commit/af67a924a0ec5593573e1f7a5a830f0beb7cf0cd)] Replace footer placeholder.
- [[#6a423b3](https://github.com/nfdi4plants/Swate/commit/6a423b385b9b1590bd0bb97cb76afc6dedd4873d)] Add button to create a new annotation table.
- [[#9a3ea60](https://github.com/nfdi4plants/Swate/commit/9a3ea60476baccdf49d0bd7c4839b00b6b52627f)] Add automated Versioning and release note creation (Issue #44).
- [[#40000ef5](https://github.com/nfdi4plants/Swate/commit/ffd82de928528179f05ba88e5a45a55894af66ac)] Add fake target to draft github release from RELEASE_NOTES.md (Issue #44).

### Fixed

- [[#648f8b6](https://github.com/nfdi4plants/Swate/commit/648f8b63526e16f4e155833d0504e0b415f666c5)] Fix multiple worksheets/annotation tables bug (Issue #58).
- [[#05f4c39](https://github.com/nfdi4plants/Swate/commit/05f4c39a4eb19c19159d9782d56e9afad43f4286)] Fix font, as the correct scss was not loaded correctly.
- [[#c6e543b](https://github.com/nfdi4plants/Swate/commit/c6e543bf3844b165f77f272aa6b38f6894da88cb)] Fix inconsistencies in building block has-unit functioniality.
- [[#fadbea8](https://github.com/nfdi4plants/Swate/commit/fadbea8337eb6ae304085f6f1fbbd4df99d8003f)] Fix disappearing checkboxes (Issue #54).
- [[#c402c70](https://github.com/nfdi4plants/Swate/commit/c402c7022bf120ae6b6a25822799aec8d80b2e7b)] Fix api docs not showing examples with DateTime (Issue #55).

## 0.1.1+7c567fd - 2020-11-18

### Added

- #7c567fd
- Allow for multiples of the same column.
- Implement basic validation system for current worksheet. (WIP)
- Add info page with social media links and contact.
- Add extensive api docs.

### Fixed

- Unit Term Search broke due to a change in naming conventions in the stored procedures. Fixed it!

## 0.1.1-beta - 2020-11-05

- Release of [Minimal POC milestone](https://github.com/nfdi4plants/Swate/milestone/1?closed=1). Rough feature set:
- Update advanced term search to use stored procedure introduced in 0.0.2-alpha.

### Fixed

-               * Responsive design should now render immediatly upon window size change
-               * Add-in should not reload after navigating to a new tab for the first time.
- Term search input field no longer looses focus after clicking into it.

## 0.0.2-alpha - 2020-10-29

- First step on the way to the Minimal POC milestone. Rough feature set:
- Add fulltext searches for advanced and simple search queries.
- Upgrade simple search to use a "is_a directed search". This means the search used subterms to already chosen building blocks as default field of search.
- AddBuildingBlock automatically adds 2 additional hidden terms in which "Term Source REF" and "Term Accession Number" are automatically inserted.
- For Developers: We added a docker-compose file to generate a local docker mysql database with adminer for an easier developing process. This feature is initialized as part of `dotnet fake build -t OfficeDebug`

## 0.0.1-alpha - 2020-07-27

- First open alpha release of Swate. Rough feature set:
- Create annotation tables from existing data
- Create annotation columns that are conform with our [Annotation Principles draft](https://nfdi4plants.github.io/AnnotationPrinciples/)
- Autocomplete search for ontology terms. You can use these to either fill cells or annotate column headers
- Automatic cell formatting for columns that have a unit annotation
- File picker: open a dialog box to select local files and use their names in the annotation table.
