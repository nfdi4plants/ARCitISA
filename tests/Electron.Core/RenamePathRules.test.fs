module ElectronCore.RenamePathRulesTests

open Swate.Electron.Shared.RenamePathRules
open Swate.Components.Shared
open Vitest

let private mainFolderMessage =
    "is one of the ARC's main folders, which ARCitISA manages itself."

let private reservedMetadataMessage = "is reserved for ARC metadata."

let private protectedNameMessage =
    "so it could not be renamed or deleted afterwards."

let private gitFolderMessage = "'.git' is not allowed inside an ARC."
let private extensionRequiredMessage = "file extension"

let private hex32 (digit: char) = String.replicate 32 (string digit)

let private addZones = [ "studies"; "assays"; "workflows"; "runs" ]

let private expectErrorContaining (expectedMessagePart: string) (result: Result<string, string>) =
    match result with
    | Ok targetPath -> failwith $"Expected an error containing '{expectedMessagePart}', but got '{targetPath}'."
    | Error errorMessage -> Vitest.expect(errorMessage).toContain (expectedMessagePart)

Vitest.describe (
    "RenamePathRules",
    fun () ->
        Vitest.test (
            "validateRenameName accepts valid names",
            fun () ->
                match validateRenameName "  New Assay  " with
                | Ok normalizedName -> Vitest.expect(normalizedName).toBe ("New Assay")
                | Error errorMessage -> failwith errorMessage
        )

        Vitest.test (
            "validateRenameName rejects path separators",
            fun () ->
                let result = validateRenameName "bad/name"

                Vitest.expect(result).toEqual (Error "The name must not contain path separators or null characters.")
        )

        Vitest.test (
            "validateRenameName rejects characters Windows does not allow",
            fun () ->
                [
                    "a<b"
                    "a>b"
                    "a:b"
                    "a\"b"
                    "a|b"
                    "a?b"
                    "a*b"
                    "a\u0001b"
                    "a\tb"
                ]
                |> List.iter (fun name ->
                    validateRenameName name
                    |> expectErrorContaining "must not contain < > : \" | ? * or control characters"
                )
        )

        Vitest.test (
            "validateRenameName rejects a trailing dot",
            fun () -> validateRenameName "trail." |> expectErrorContaining "must not end with a dot"
        )

        Vitest.test (
            "validateRenameName rejects Windows device names",
            fun () ->
                [
                    "aux", "aux"
                    "AUX.txt", "AUX"
                    "com1.csv", "com1"
                    "lpt9", "lpt9"
                    "con.txt.bak", "con"
                    "PRN", "PRN"
                    "nul.log", "nul"
                    "CONIN$", "CONIN$"
                    "conout$.txt", "conout$"
                    "COM\u00B9", "COM\u00B9"
                    "lpt\u00B3.txt", "lpt\u00B3"
                    "aux .txt", "aux"
                ]
                |> List.iter (fun (name, baseName) ->
                    validateRenameName name
                    |> expectErrorContaining
                        $"must not be '{baseName}' or start with '{baseName}.', because Windows reserves it for a device."
                )
        )

        Vitest.test (
            "validateRenameName accepts names that only resemble Windows restrictions",
            fun () ->
                [
                    "console.txt"
                    "auxiliary.txt"
                    "com10.csv"
                    "notes.v2.md"
                    "T12-30-00.csv"
                    "my notes. v2.txt"
                ]
                |> List.iter (fun name ->
                    match validateRenameName name with
                    | Ok normalizedName -> Vitest.expect(normalizedName).toBe (name)
                    | Error errorMessage -> failwith errorMessage
                )
        )

        Vitest.test (
            "New File and Rename refuse a timestamp name with colons",
            fun () ->
                tryBuildGenericFileSystemChildPath true "notes" "T12:30:00.csv"
                |> expectErrorContaining "because Windows does not allow them"

                tryBuildRenameTargetPath "notes/old.csv" "T12:30:00.csv"
                |> expectErrorContaining "because Windows does not allow them"
        )

        Vitest.test (
            "buildRenamedSiblingPath keeps parent path and replaces leaf",
            fun () ->
                let targetPath = buildRenamedSiblingPath "assays/OldAssay" "NewAssay"
                Vitest.expect(targetPath).toBe ("assays/NewAssay")
        )

        Vitest.test (
            "tryBuildRenameTargetPath rejects no-op rename",
            fun () ->
                let result = tryBuildRenameTargetPath "assays/OldAssay" "OldAssay"
                Vitest.expect(result).toEqual (Error "Rename target is identical to the current path.")
        )

        Vitest.test (
            "ArcEntityPathRules.isRenamePathAllowed allows entity folders and safe generic descendants",
            fun () ->
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/OldAssay").toBe (true)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/OldAssay/isa.assay.xlsx").toBe (false)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/OldAssay/notes/custom.txt").toBe (true)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/OldAssay/dataset").toBe (false)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/OldAssay/protocols").toBe (false)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "studies/StudyA/resources").toBe (false)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/OldAssay/dataset/raw.txt").toBe (true)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "notes").toBe (false)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "notes/2026-06-15/foo/foo.md").toBe (true)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "test.fsx").toBe (true)
        )

        Vitest.test (
            "native structural child matching is case-insensitive",
            fun () ->
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "assays/OldAssay/DataSet").toBe (false)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "studies/StudyA/Protocols").toBe (false)
                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "studies/StudyA/Resources").toBe (false)
        )

        Vitest.test (
            "generic filesystem targets are limited to safe non-canonical paths",
            fun () ->
                let allowedTargets = [
                    "assays/AssayA/protocols/protocol.md"
                    "assays/AssayA/dataset/raw.txt"
                    "assays/AssayA/protocol"
                    "workflows/WorkflowA/scripts/workflow.cwl"
                    "runs/RunA/data.txt"
                    "test.fsx"
                    "notes/custom.md"
                ]

                allowedTargets
                |> List.iter (fun path ->
                    Vitest.expect(ArcEntityPathRules.isGenericFileSystemTargetAllowed path).toBe (true)
                )

                let rejectedTargets = [
                    ""
                    "assays"
                    "assays/AssayA"
                    "assays/AssayA/dataset"
                    "assays/AssayA/protocols"
                    "studies/StudyA/protocols"
                    "studies/StudyA/resources"
                    "assays/AssayA/isa.assay.xlsx"
                    "assays/AssayA/isa.datamap.xlsx"
                    "assays/AssayA/.gitattributes"
                    "assays/AssayA/readme.md"
                    "assays/AssayA/.git/config"
                    "../assays/AssayA/custom.txt"
                    "isa.investigation.xlsx"
                ]

                rejectedTargets
                |> List.iter (fun path ->
                    Vitest.expect(ArcEntityPathRules.isGenericFileSystemTargetAllowed path).toBe (false)
                )
        )

        Vitest.test (
            "existing items with the legacy DataMap name can be renamed, moved, deleted and used as parents",
            fun () ->
                let existingFolder = "backup/isa_datamap"

                Vitest.expect(ArcEntityPathRules.isItemDeletePathAllowed false existingFolder).toBe (true)
                Vitest.expect(ArcEntityPathRules.isItemRenamePathAllowed false existingFolder).toBe (true)
                Vitest.expect(ArcEntityPathRules.isGenericFileSystemTargetAllowed existingFolder).toBe (true)
                Vitest.expect(ArcEntityPathRules.isGenericFileSystemParentAllowed existingFolder).toBe (true)

                match tryBuildGenericFileSystemRenameTargetPath false false existingFolder "old_maps" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("backup/old_maps")
                | Error errorMessage -> failwith errorMessage

                match tryBuildGenericFileSystemChildPath true existingFolder "notes.txt" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("backup/isa_datamap/notes.txt")
                | Error errorMessage -> failwith errorMessage
        )

        Vitest.test (
            "protected entity child folders can still be used as generic filesystem parents",
            fun () ->
                Vitest.expect(ArcEntityPathRules.isGenericFileSystemParentAllowed "assays/AssayA/dataset").toBe (true)

                Vitest
                    .expect(ArcEntityPathRules.isGenericFileSystemParentAllowed "studies/StudyA/protocols")
                    .toBe (true)
        )

        Vitest.test (
            "root-level generic filesystem child paths are allowed for safe targets only",
            fun () ->
                match tryBuildGenericFileSystemChildPath false "" "docs" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("docs")
                | Error errorMessage -> failwith errorMessage

                match tryBuildGenericFileSystemChildPath true "" "notes.txt" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("notes.txt")
                | Error errorMessage -> failwith errorMessage

                let rejectedRootTargets = [
                    yield! addZones |> List.map (fun zone -> zone, mainFolderMessage)
                    "isa.investigation.xlsx", reservedMetadataMessage
                    ".git", gitFolderMessage
                    ".gitattributes", protectedNameMessage
                    ".gitkeep", protectedNameMessage
                    "../escape", "path separators"
                ]

                rejectedRootTargets
                |> List.iter (fun (name, expectedMessagePart) ->
                    [ true; false ]
                    |> List.iter (fun isFile ->
                        tryBuildGenericFileSystemChildPath isFile "" name
                        |> expectErrorContaining expectedMessagePart
                    )
                )
        )

        Vitest.test (
            "New File and New Folder name the cause when a target is blocked",
            fun () ->
                tryBuildGenericFileSystemChildPath false "assays/AssayA" "dataset"
                |> expectErrorContaining "'dataset' is a folder that ARCitISA manages inside a study or assay."

                tryBuildGenericFileSystemChildPath false "" "assays"
                |> expectErrorContaining "'assays' is one of the ARC's main folders, which ARCitISA manages itself."

                tryBuildGenericFileSystemChildPath true "data" "isa.investigation.xlsx"
                |> expectErrorContaining "'isa.investigation.xlsx' is reserved for ARC metadata."

                tryBuildGenericFileSystemChildPath true "data" ".gitkeep"
                |> expectErrorContaining
                    "ARCitISA protects the name '.gitkeep', so it could not be renamed or deleted afterwards."

                tryBuildGenericFileSystemChildPath false "data" ".git"
                |> expectErrorContaining gitFolderMessage

                Vitest
                    .expect(ArcEntityPathRules.describeBlockedGenericCreateTarget false "assays/AssayB")
                    .toEqual (
                        Some
                            "'AssayB' is a study, assay, workflow or run folder, which ARCitISA creates through its own dialogs."
                    )

                Vitest.expect(ArcEntityPathRules.describeBlockedGenericCreateTarget true "data/notes.md").toEqual (None)
        )

        Vitest.test (
            "the create target check and its block reason agree for every path",
            fun () ->
                let paths = [
                    ""
                    "../x"
                    "isa.investigation.xlsx"
                    "assays/A/isa.assay.xlsx"
                    "assays"
                    "assays/AssayB"
                    "assays/AssayB/dataset"
                    "studies/S/protocols"
                    "data/.git"
                    "data/.git/config"
                    "data/.gitkeep"
                    "README.md"
                    "data/README.md"
                    "notes"
                    "data/notes.md"
                    "assays/AssayB/notes.md"
                ]

                for isFile in [ true; false ] do
                    for path in paths do
                        Vitest
                            .expect(ArcEntityPathRules.isGenericFileSystemCreateTargetAllowed isFile path)
                            .toBe ((ArcEntityPathRules.describeBlockedGenericCreateTarget isFile path).IsNone)
        )

        Vitest.test (
            "names the file tree hides and import staging names are accepted for new items and renames",
            fun () ->
                let stagingName = $".swate-import-{hex32 'a'}"
                let stagingFolder = $"data/{stagingName}"

                let acceptedNames = [
                    "~$notes.md"
                    "isa_datamap"
                    stagingName
                    $"data.bin.vcs-lfs-backup-{hex32 'a'}"
                ]

                for name in acceptedNames do
                    for isFile in [ true; false ] do
                        match tryBuildGenericFileSystemChildPath isFile "data" name with
                        | Ok targetPath -> Vitest.expect(targetPath).toBe ($"data/{name}")
                        | Error errorMessage -> failwith errorMessage

                for name in acceptedNames do
                    match tryBuildGenericFileSystemRenameTargetPath true false "data/old.txt" name with
                    | Ok targetPath -> Vitest.expect(targetPath).toBe ($"data/{name}")
                    | Error errorMessage -> failwith errorMessage

                    match tryBuildGenericFileSystemRenameTargetPath false false "data/oldFolder" name with
                    | Ok targetPath -> Vitest.expect(targetPath).toBe ($"data/{name}")
                    | Error errorMessage -> failwith errorMessage

                [ true; false ]
                |> List.iter (fun isFile ->
                    match tryBuildGenericFileSystemChildPath isFile stagingFolder "inner" with
                    | Ok targetPath -> Vitest.expect(targetPath).toBe ($"{stagingFolder}/inner")
                    | Error errorMessage -> failwith errorMessage
                )

                match
                    tryBuildGenericFileSystemRenameTargetPath true false $"{stagingFolder}/item.txt" "renamed.txt"
                with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ($"{stagingFolder}/renamed.txt")
                | Error errorMessage -> failwith errorMessage
        )

        Vitest.test (
            "rename targets get the same per-cause messages as new items",
            fun () ->
                tryBuildGenericFileSystemRenameTargetPath true false "assays/AssayA/notes.txt" "isa.assay.xlsx"
                |> expectErrorContaining "'isa.assay.xlsx' is reserved for ARC metadata."

                tryBuildGenericFileSystemRenameTargetPath false false "assays/AssayA/old" "dataset"
                |> expectErrorContaining "'dataset' is a folder that ARCitISA manages inside a study or assay."

                tryBuildGenericFileSystemRenameTargetPath false false "data/old" ".git"
                |> expectErrorContaining gitFolderMessage
        )

        Vitest.test (
            "README files can be created anywhere generic files are allowed",
            fun () ->
                let parents = [
                    ""
                    "test"
                    "assays/AssayA"
                    "assays/AssayA/dataset"
                    "studies"
                ]

                parents
                |> List.iter (fun parentPath ->
                    [ "README.md"; "readme.md" ]
                    |> List.iter (fun name ->
                        match tryBuildGenericFileSystemChildPath true parentPath name with
                        | Ok _ -> ()
                        | Error errorMessage ->
                            failwith $"Expected '{parentPath}/{name}' to be creatable: {errorMessage}"
                    )
                )

                tryBuildGenericFileSystemChildPath false "" "README.md"
                |> expectErrorContaining protectedNameMessage

                Vitest.expect(ArcEntityPathRules.isDeletePathAllowed "README.md").toBe (false)
                Vitest.expect(ArcEntityPathRules.isRenamePathAllowed "assays/AssayA/README.md").toBe (false)
        )

        Vitest.test (
            "add zone roots accept new files but not new folders",
            fun () ->
                addZones
                |> List.iter (fun zone ->
                    Vitest.expect(ArcEntityPathRules.isGenericFileSystemCreateParentAllowed true zone).toBe (true)
                    Vitest.expect(ArcEntityPathRules.isGenericFileSystemCreateParentAllowed false zone).toBe (false)

                    match tryBuildGenericFileSystemChildPath true zone "overview.md" with
                    | Ok targetPath -> Vitest.expect(targetPath).toBe ($"{zone}/overview.md")
                    | Error errorMessage -> failwith errorMessage

                    tryBuildGenericFileSystemChildPath false zone "NewEntity"
                    |> expectErrorContaining "only allowed inside safe ARC directories"

                    tryBuildGenericFileSystemChildPath true zone ".gitkeep"
                    |> expectErrorContaining protectedNameMessage
                )
        )

        Vitest.test (
            "item-aware delete and rename checks treat files directly inside add zones as generic files",
            fun () ->
                [
                    "studies", "isa.study.xlsx"
                    "assays", "isa.assay.xlsx"
                    "workflows", "isa.workflow.xlsx"
                    "runs", "isa.run.xlsx"
                ]
                |> List.iter (fun (zone, canonicalFileName) ->
                    [
                        $"{zone}/overview.md", true
                        $"{zone}/LICENSE", true
                        $"{zone}/.DS_Store", true
                        $"{zone}/{canonicalFileName}", false
                        $"{zone}/isa.datamap.xlsx", false
                        $"{zone}/readme.md", false
                    ]
                    |> List.iter (fun (path, expected) ->
                        Vitest.expect(ArcEntityPathRules.isItemDeletePathAllowed true path).toBe (expected)
                        Vitest.expect(ArcEntityPathRules.isItemRenamePathAllowed true path).toBe (expected)
                    )

                    Vitest.expect(ArcEntityPathRules.isItemDeletePathAllowed false $"{zone}/Entity1").toBe (true)
                    Vitest.expect(ArcEntityPathRules.isItemRenamePathAllowed false $"{zone}/Entity1").toBe (true)
                )

                Vitest.expect(ArcEntityPathRules.isItemDeletePathAllowed true "assays/AssayA/protocol.md").toBe (true)
        )

        Vitest.test (
            "files directly inside add zones are generic rename targets",
            fun () ->
                match tryBuildGenericFileSystemRenameTargetPath true true "studies/overview.md" "summary.md" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("studies/summary.md")
                | Error errorMessage -> failwith errorMessage

                tryBuildGenericFileSystemRenameTargetPath false false "studies/StudyA" "StudyB"
                |> expectErrorContaining "rename is only allowed for safe non-canonical paths"

                tryBuildGenericFileSystemRenameTargetPath true true "studies/overview.md" "README.md"
                |> expectErrorContaining protectedNameMessage
        )

        Vitest.test (
            "new files directly inside add zones need an extension so they cannot shadow entity identifiers",
            fun () ->
                addZones
                |> List.iter (fun zone ->
                    tryBuildGenericFileSystemChildPath true zone "Entity1"
                    |> expectErrorContaining extensionRequiredMessage

                    tryBuildGenericFileSystemRenameTargetPath true true $"{zone}/overview.md" "Entity1"
                    |> expectErrorContaining extensionRequiredMessage
                )

                // Existing extensionless files stay deletable and renamable to a name with an extension.
                Vitest.expect(ArcEntityPathRules.isItemDeletePathAllowed true "studies/LICENSE").toBe (true)

                match tryBuildGenericFileSystemRenameTargetPath true true "studies/LICENSE" "LICENSE.md" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("studies/LICENSE.md")
                | Error errorMessage -> failwith errorMessage

                // Deeper paths and the ARC root are unaffected.
                match tryBuildGenericFileSystemChildPath true "assays/AssayA" "Makefile" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("assays/AssayA/Makefile")
                | Error errorMessage -> failwith errorMessage

                match tryBuildGenericFileSystemChildPath true "" "LICENSE" with
                | Ok targetPath -> Vitest.expect(targetPath).toBe ("LICENSE")
                | Error errorMessage -> failwith errorMessage
        )

        Vitest.test (
            "dot files directly inside add zones are renamed with the file rules",
            fun () ->
                addZones
                |> List.iter (fun zone ->
                    Vitest.expect(ArcEntityPathRules.isAddZoneRootChildPath $"{zone}/.DS_Store").toBe (true)
                    Vitest.expect(ArcEntityPathRules.isAddZoneRootChildPath $"{zone}/Entity1/notes.md").toBe (false)

                    match tryBuildGenericFileSystemRenameTargetPath true true $"{zone}/.DS_Store" "notes.md" with
                    | Ok targetPath -> Vitest.expect(targetPath).toBe ($"{zone}/notes.md")
                    | Error errorMessage -> failwith errorMessage

                    // The file rules let a dot file become an entity-shaped path, so the extension rule
                    // is what keeps it from shadowing an entity identifier.
                    tryBuildGenericFileSystemRenameTargetPath true true $"{zone}/.DS_Store" "Entity1"
                    |> expectErrorContaining extensionRequiredMessage
                )
        )
)

Vitest.describe (
    "PathHelpers path relation helpers",
    fun () ->
        Vitest.test (
            "containsPathTraversalSegments detects traversal markers",
            fun () ->
                Vitest.expect(PathHelpers.containsPathTraversalSegments "../assays/MyAssay").toBe (true)
                Vitest.expect(PathHelpers.containsPathTraversalSegments "assays/./MyAssay").toBe (true)
                Vitest.expect(PathHelpers.containsPathTraversalSegments "assays/MyAssay").toBe (false)
        )

        Vitest.test (
            "isSameOrDescendantPathForFsComparison is case-insensitive and normalized",
            fun () ->
                Vitest
                    .expect(PathHelpers.isSameOrDescendantPathForFsComparison "C:/Repo/ARC/Assays/A" "c:\\repo\\arc")
                    .toBe (true)

                Vitest
                    .expect(PathHelpers.isSameOrDescendantPathForFsComparison "C:/Repo/Other/A" "c:/repo/arc")
                    .toBe (false)
        )
)
