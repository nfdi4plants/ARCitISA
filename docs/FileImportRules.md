# File import and name rules

These rules cover the actions "Import files", "Import folders", "New File", "New Folder" and "Rename" in the ARCitISA file explorer. PR #1429 (branch feature/AddImportFolders) first added a large set of name and import rules. On 2026-10-23 the PR reduced them to a smaller set. This page lists the rules that stay active and keeps the dropped ones with the reason each existed and where its code lived. The last commit that contains every dropped rule is [13056ca0](https://github.com/nfdi4plants/ARCitISA/pull/1429/commits/13056ca04681d669299cec9aa655474c4e9c72da). The PR keeps it reachable after the branch is deleted. To read it locally, run `git fetch origin pull/1429/head` and then `git show 13056ca0:<path>`.

## Active rules

### Names that Windows does not allow

`validateRenameName` in `src/Electron/src/Swate.Electron.Shared/RenamePathRules.fs` rejects these names:

- an empty name
- "." and ".."
- a name with "/", "\" or the null character
- a name with one of < > : " | ? *, or a control character from 1 to 31
- a name that ends with a dot
- the Windows device names CON, PRN, AUX, NUL, COM1 to COM9 and LPT1 to LPT9
- the superscript forms COM¹, COM², COM³, LPT¹, LPT² and LPT³, plus CONIN$ and CONOUT$
- any of the device names with an extension, such as "AUX.txt"

The rule applies to New File, New Folder and Rename. It also applies to every imported name, which means the picked items and everything inside picked folders. The user sees a message with the reason and the action does not start.

The reason is that Windows and git for Windows cannot check such names out. A backslash is valid on Linux and macOS, but on Windows it would turn into a folder separator.

### Leading or trailing spaces in imported names

This rule applies to import only. An imported name that starts or ends with a space stops the import. New File, New Folder and Rename trim the name instead.

The reason is that Windows drops trailing spaces and ARCitISA trims names when it renames or deletes, so ARCitISA could not handle an untrimmed name afterwards.

### Items named .git

Import rejects any file or folder named ".git", ignoring case, as a picked item and inside picked folders. The user sees a message and the import writes nothing.

The reason is that git records a folder with its own ".git" inside the ARC as an embedded repository (a gitlink) without its files. None of those files reach the DataHUB, and the file tree hides ".git" folders, so the user cannot see why. A ".git" file, as git submodules and worktrees write, has the same effect when it points to an existing repository. A picked ".git" folder imported into the root of an ARC that has no ".git" folder yet would turn the ARC into that repository, with its history and remotes. The check is `ArcEntityPathRules.isDisallowedGenericPathSegment` in `src/Shared/PathHelpers.fs`, called from `validateImportedName` in `src/Electron/src/Main/IPC/FileSystemIO.fs`.

### Names that differ only in case or Unicode form

Import treats names that differ only in upper and lower case as the same name. This holds on every operating system. Import also treats names that differ only in Unicode form as the same name, such as "Café.csv" written as one "é" character (NFC) and as "e" plus a combining accent (NFD). The checks compare names after NFC normalization and lower-casing. Three cases stop the import:

- two picked items with such names
- two items in the same folder inside a picked folder
- a picked item whose name matches an item that already exists in the target folder

The reason is that Windows and macOS treat names that differ only in case as one, and macOS also treats the two Unicode forms as one. An ARC has to work on all systems. The checks live in `inspectPickedItemAsync`, `createExternalFileImportPlan` and `collectExternalFolderEntries` in `src/Electron/src/Main/IPC/FileSystemIO.fs`.

### No overwrite and no merge

The import never overwrites or merges. An existing item with the same name (see the previous rule) stops the import. The import creates folders exclusively and publishes files with an exclusive copy or hard link, so a race cannot overwrite either. The name check runs while the import plans. The exclusive publish prevents overwrites, but on a file system that keeps such names apart, an item created between planning and publishing can end up next to an imported item whose name differs only in case or Unicode form.

## Checks that are not name rules

- The import refuses a whole drive, because a drive has no name.
- The import refuses to copy a folder into itself or into a folder inside it, because the copy would never end.
- The import refuses an item that is neither a file nor a folder, for example a named pipe, because it cannot copy one.
- The target must be a folder.
- ARCitISA refuses to open the picker or start an import while it saves or imports, so two operations do not change the ARC at once.
- A picker result is valid for one import.

The import follows symbolic links, including Windows junctions, and copies what a link points to as an ordinary file or folder under the link's name. The import stops at a link that leads back to a folder that contains it or to a folder above it, because the walk would never end. The import refuses such a link at once. It also stops when a link's target no longer exists, or when the walk goes through too many links. Two links to the same folder in different places are both copied in full. A link inside a picked folder that leads to the import target, or to a folder that contains it, stops the import. When a folder cannot be read because access is denied, as with some Windows compatibility junctions such as "My Music" in Documents, the import stops with a message.

The import copies into a staging folder named ".swate-import-" plus 32 hex digits inside the target folder and removes it when it is done. The watcher ignores every path with such a folder name (`ArcVaultHelper.isFileWatcherPathIgnored`). This matters for an import directly into studies, assays, workflows or runs. There a metadata file inside the staging folder, such as "assays/.swate-import-<hex>/isa.assay.xlsx", would look like an entity file to the watcher. Inside data folders the watcher sees nothing anyway, because it watches only the ARC structure. When the import ends, it reconciles the target folder in the file tree (`tryImportExternalFiles` in `src/Electron/src/Main/IPC/IArcVaultsApi.fs`). If ARCitISA cannot remove the staging folder, the file tree lists it like any other folder.

## Rules kept from before this change

New File, New Folder and Rename keep the location rules that existed before PR #1429. They decide where the actions are offered (`canCreateFileSystemItemIn` in `FileExplorer/Helper.fs` and `isGenericFileSystemCreateParentAllowed` in `src/Shared/PathHelpers.fs`) and which names they refuse:

- ARC metadata names such as "isa.assay.xlsx"
- the folders ARCitISA manages: studies, assays, workflows, runs and their entity folders, "protocols" and "resources" inside a study, and "dataset" and "protocols" inside an assay
- ".git" in a path
- the protected names ".gitattributes", ".gitkeep" and "README.md". New Folder refuses all three names. New File refuses ".gitattributes" and ".gitkeep" and can create "README.md". Rename refuses these names as source and as target
- the extension a file needs directly inside studies, assays, workflows or runs

This PR only changed their messages (`describeBlockedGenericCreateTarget`), so the dialogs say why they refuse a name. The import menu entries appear where New File and New Folder do. Apart from the ".git" rule above, the imported items no longer go through these rules.

Delete has its own permission rules. Files and folders named ".gitkeep", ".gitattributes" and "README.md" can be deleted, including generated empty-folder markers. Directly inside studies, assays, workflows and runs, ordinary files and folders use filesystem deletion. A folder containing entity or datamap metadata uses ARC deletion, with the same metadata names and file-kind checks as ARC loading. A folder whose name starts with a dot always uses filesystem deletion, because ARCtrl cannot read such a name as an entity identifier. This covers a leftover ".swate-import-" folder of an interrupted import. ARC-managed folders and the investigation file remain protected.

## Dropped rules

All code paths below are at commit [13056ca0](https://github.com/nfdi4plants/ARCitISA/pull/1429/commits/13056ca04681d669299cec9aa655474c4e9c72da). `FileSystemIO.fs` is `src/Electron/src/Main/IPC/FileSystemIO.fs`, `RenamePathRules.fs` is `src/Electron/src/Swate.Electron.Shared/RenamePathRules.fs` and `PathHelpers.fs` is `src/Shared/PathHelpers.fs`.

### ARC metadata file names

Import rejected the names isa.investigation.xlsx, isa.study.xlsx, isa.assay.xlsx, isa.workflow.xlsx, isa.run.xlsx and isa.datamap.xlsx, for files and folders, anywhere inside an imported folder and as a picked item. The reason is that the ARC merge reads a copy such as backup/assays/A/isa.assay.xlsx as the real assay A. The code was `ArcEntityPathRules.isCanonicalArcFileName` in `PathHelpers.fs`, called from `collectExternalFolderEntries` and `inspectPickedItemAsync`.

### Outdated DataMap name

Import, New File, New Folder and Rename rejected the name "isa_datamap". The reason is that loading the ARC renames such a file to isa.datamap.xlsx, and the file tree hides it. The code was `ArcEntityPathRules.legacyDataMapFileName` and `isLegacyDataMapFileName` in `PathHelpers.fs`, the message `legacyDataMapNameError` and the check in `validateNewItemName` in `RenamePathRules.fs`, and the calls in `collectExternalFolderEntries` and `inspectPickedItemAsync`.

ARC loading discovers only structural paths. An imported file named "isa_datamap" is renamed to isa.datamap.xlsx only when it sits directly inside the entity folder. If isa.datamap.xlsx already exists in that folder, the legacy file stays and the file tree hides it. When an imported file is deeper in a data folder, it is not discovered during ARC loading and is never passed to the migration. It keeps its name after a reload and stays hidden in the file tree.

### Folders named like a protected file

Import rejected a folder named ".gitkeep", ".gitattributes" or "README.md", picked or nested. The reason is that ARCitISA could never rename or delete it afterwards. The code was `ArcEntityPathRules.isProtectedName` in `PathHelpers.fs` (names from `protectedDeleteTargetNames`), called from `collectExternalFolderEntries` and `inspectPickedItemAsync` (error `protectedFolderImportError`).

### Symbolic links

Import refused nested symbolic links and picked folder links. It followed a picked file link. The reason is that following a link could copy data from outside the picked folder or loop forever. The code was the `isLink` branches in `collectExternalFolderEntries` and `inspectPickedItemAsync` in `FileSystemIO.fs` (error `symbolicLinkImportError`).

### Git LFS pointer files

Import rejected Git LFS pointer files. The reason is that a pointer only stands in for content that was never downloaded. The check used the strict pointer parser `TextDiffLfsPointer.tryParseStrict`, so only the exact layout git-lfs writes counted. The code was `isGitLfsPointerFileAsync`, `rejectGitLfsPointerFileAsync` and `gitLfsPointerImportError` in `FileSystemIO.fs`. `createExternalFileImportPlan` ran the check for files inside picked folders through `mapWithConcurrencyAsync` with a limit of 16 (`gitLfsPointerCheckConcurrency`), and `inspectPickedItemAsync` ran it for picked files.

### Names the import left out or refused

Inside picked folders the import skipped these names silently. As picked items it refused them:

- lock files of open Office or LibreOffice documents ("~$Book1.xlsx", ".~lock.notes.odt#")
- Git LFS backups (".vcs-lfs-backup-" plus 32 hex digits)
- the ARC's own git files (.gitkeep, .gitattributes)
- leftover import staging folders (".swate-import-" plus 32 hex digits)
- files named like a staging folder

The reason is that the file tree and the file watcher hide these names and the ARC manages its own git files. A staging folder is a leftover copy of an interrupted import. The code was `isLeftOutOfImport` in `FileSystemIO.fs`. For files it used `ArcVaultHelper.isFileWatcherPathIgnored`, which also matched staging-folder names, and `ArcEntityPathRules.isArcManagedGitFileName` (`PathHelpers.fs`). For folders it used `isTemporaryImportPath`. `inspectPickedItemAsync` turned each case into its own error.

These items are now imported. The file tree still hides files named like lock files or LFS backups. A folder with such a name stays visible.

### New File and New Folder rules for imported items

Imported items followed the New File and New Folder rules of the target folder: ARC metadata names, managed folders such as "dataset" in an assay or "assays" at the root, ".git", protected names and the extension rule directly inside studies, assays, workflows or runs. The reason is the same as for New File. The code was the call to `tryBuildGenericFileSystemChildPath` in `inspectPickedItemAsync`. The check ran after the "already exists" check (`ensureTargetDoesNotExist`), because a managed name such as "assays" usually exists at the target. The same call also checked the target folder, so an import could only add an item where New File or New Folder could. Now only the right-click menu decides where the import entries appear. The main process accepts any folder inside the ARC as the target. The ".git" part is active again as its own rule, see "Items named .git".

### Hidden file names in New File and Rename

New File and Rename rejected file names that ARCitISA hides: lock-file names such as "~$notes.md" and LFS-backup names. The reason is that the file would disappear from the file tree. The code was `validateNewItemName` in `RenamePathRules.fs` with `isOfficeLockFilePath`, `isLfsBackupPath`, `lockFileNameError` and `lfsBackupNameError`.

### Staging-folder names

New File, New Folder and Rename rejected names like ".swate-import-" plus 32 hex digits. The reason is that the watcher ignores staging folders, so changes inside would not show up. The code was `isTemporaryImportPath`, `stagingFolderNameError` and `stagingFileNameError` in `validateNewItemName` in `RenamePathRules.fs`.

The watcher still ignores folders with such a name. Directly inside studies, assays, workflows or runs, this keeps a metadata file in such a folder from turning into an entity. Everywhere else such a folder is a data folder, which the watcher does not watch anyway. The file tree lists it, and changes that other programs make inside it appear when the ARCitISA window gets focus or after "Refresh folder".

### Items inside a leftover staging folder

New File, New Folder, Import and Rename refused new items inside a leftover staging folder, and Rename refused to rename items inside one. Both the main process and the right-click menu enforced this. The reason is that the watcher ignores staging folders. Users could still rename or delete the folder itself. The code was the `isTemporaryImportPath` check on the parent in `tryBuildGenericFileSystemChildPath` (`stagingParentError`) and `tryBuildGenericFileSystemRenameTargetPath` (`stagingRenameParentError`) in `RenamePathRules.fs`, plus `canCreateFileSystemItemIn` and `canRenameFileItem` in `src/Electron/src/Renderer/Components/LeftSidebar/FileExplorer/Helper.fs`.

See the previous section for the current watcher behavior.

### Renaming the open ARC to a staging-folder name

Rename refused a staging-folder name for the open ARC's folder. The reason is that the watcher would ignore the whole ARC. This is harmless now, because the watcher checks its ignore rule on the path relative to the ARC folder, so the name of the ARC folder itself does not count (`ArcVaultHelper.isStructuralWatcherPathOfKind`). The code was the `isTemporaryImportPath` check in `tryBuildOpenArcRootRenamePlan` in `src/Electron/src/Main/ArcVault/ArcVaultHelper.fs`.
