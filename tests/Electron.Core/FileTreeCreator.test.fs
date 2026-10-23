module ElectronCore.FileTreeCreatorTests

open System
open Fable.Core
open Fable.Core.JsInterop
open Main
open Swate.Components.Shared
open Main.Bindings.Path
open Main.VersionControl
open Swate.Components.Composite.Authentication.Types
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest
open ElectronCore.TestHelpers

module FileTreeCreator = Main.FileTreeCreator

let private fsPromisesDynamic: obj = importAll "fs/promises"
let private osDynamic: obj = importAll "os"
let private childProcessDynamic: obj = importAll "node:child_process"

let private fileTreeCreatorTestOptions = TestOptions(timeout = 20000)

let private normalizeSlashes (path: string) = path.Replace("\\", "/")

let private createFileEntry name path =
    ({
        name = name
        isDirectory = false
        path = path
        largeObject = None
    }
    : FileEntry)

let private createObjectState path =
    ({
        Path = path
        IsMaterialized = false
        IsLocallyAvailable = true
        SizeBytes = Some 10.0
        ObjectId = Some(String.replicate 64 "a")
    }
    : ObjectStateDto)

let private enrichFileEntries (objects: (string * ObjectStateDto) list) (entries: FileEntry[]) =
    FileTreeCreator.withFileEntriesLfsMetadata "/repo" (Map.ofList objects) entries

type private TempRepositoryContext = { RootPath: string; RepoPath: string }

let private createTempDirectoryAsync () : Fable.Core.JS.Promise<string> =
    let prefix =
        join [|
            osDynamic?tmpdir () |> unbox<string>
            "swate-electron-file-tree-"
        |]

    fsPromisesDynamic?mkdtemp (prefix) |> unbox<Fable.Core.JS.Promise<string>>

let private removeDirectoryAsync (path: string) : Fable.Core.JS.Promise<unit> = promise {
    let! _ =
        fsPromisesDynamic?rm (path, createObj [ "recursive" ==> true; "force" ==> true ])
        |> unbox<Fable.Core.JS.Promise<obj>>

    return ()
}

let private writeUtf8FileAsync (path: string) (content: string) : Fable.Core.JS.Promise<unit> = promise {
    let! _ =
        fsPromisesDynamic?writeFile (path, content, "utf8")
        |> unbox<Fable.Core.JS.Promise<obj>>

    return ()
}

let private runGitAsync (repoPath: string) (args: string[]) : Fable.Core.JS.Promise<string> = promise {
    let! output =
        Fable.Core.JS.Constructors.Promise.Create(fun resolve reject ->
            childProcessDynamic?execFile (
                "git",
                args,
                createObj [
                    "cwd" ==> repoPath
                    "encoding" ==> "utf8"
                    "shell" ==> false
                ],
                fun (error: obj) (stdout: obj) (stderr: obj) ->
                    if error |> Option.ofObj |> Option.isSome then
                        let stderrText =
                            stderr |> Option.ofObj |> Option.map string |> Option.defaultValue String.Empty

                        let argsText = String.concat " " args
                        reject (exn $"git {argsText} failed: {stderrText}")
                    else
                        stdout
                        |> Option.ofObj
                        |> Option.map string
                        |> Option.defaultValue String.Empty
                        |> resolve
            )
            |> ignore
        )

    return output
}

let private expectHexObjectId (largeObject: ObjectStateDto) =
    Vitest.expect(largeObject.ObjectId.IsSome).toBe (true)

    let objectId = largeObject.ObjectId |> Option.get
    Vitest.expect(objectId.Length).toBe (64)
    Vitest.expect(System.Text.RegularExpressions.Regex.IsMatch(objectId, "^[0-9a-fA-F]{64}$")).toBe (true)

let private withTempRepository
    (testBody: TempRepositoryContext -> Fable.Core.JS.Promise<unit>)
    : Fable.Core.JS.Promise<unit> =
    promise {
        let! rootPath = createTempDirectoryAsync ()

        try
            let repoPath = join [| rootPath; "repo" |]

            let runtime =
                createRuntime rootPath VersionControlService.LakeFs.LakeFsCredentials.unconfigured (memoryBindings ())

            let gitFactory =
                ProviderComposition.createGitFactory noAccounts WorkspaceSessionHost.windowOwnerOf

            let! initialized =
                gitFactory.Initialize
                    {
                        TargetPath = repoPath
                        Location = None
                    }
                    (OperationContext.detached "file-tree-init")
                |> Async.StartAsPromise

            let binding =
                match initialized with
                | Succeeded outcome -> outcome.Value
                | PartiallySucceeded(_, failure) ->
                    failwith $"git init partially succeeded ({failure.Code}): {failure.Message}"
                | Failed failure -> failwith $"git init failed ({failure.Code}): {failure.Message}"

            let host = WorkspaceSessionHost.WorkspaceSessionHost(runtime)
            WorkspaceSessionHost.initialize host

            try
                do!
                    testBody {
                        RootPath = rootPath
                        RepoPath = binding.WorkspaceRoot
                    }

                do! host.CloseAll() |> Async.StartAsPromise
                do! removeDirectoryAsync rootPath
            with error ->
                do! host.CloseAll() |> Async.StartAsPromise
                do! removeDirectoryAsync rootPath
                return raise error
        with error ->
            do! removeDirectoryAsync rootPath
            return raise error
    }

Vitest.describe (
    "FileTreeCreator LFS metadata",
    fun () ->
        Vitest.test (
            "getFileEntries annotates materialized and pointer large objects",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                do!
                    withTempRepository (fun context -> promise {
                        let pointerFilePath = join [| context.RepoPath; "pointer.psd" |]
                        let downloadedFilePath = join [| context.RepoPath; "downloaded.psd" |]

                        let! _ = runGitAsync context.RepoPath [| "lfs"; "install"; "--local" |]
                        let! _ = runGitAsync context.RepoPath [| "lfs"; "track"; "*.psd" |]
                        do! writeUtf8FileAsync pointerFilePath "Pointer-like content.\n"
                        do! writeUtf8FileAsync downloadedFilePath "Hydrated-like content.\n"

                        let! _ =
                            runGitAsync context.RepoPath [|
                                "add"
                                ".gitattributes"
                                "pointer.psd"
                                "downloaded.psd"
                            |]

                        ()

                        let! pointerContents =
                            runGitAsync context.RepoPath [| "lfs"; "pointer"; "--file"; pointerFilePath |]

                        do! writeUtf8FileAsync pointerFilePath pointerContents

                        let! entries = FileTreeCreator.getFileEntries context.RepoPath true

                        let pointerEntry =
                            entries
                            |> Microsoft.FSharp.Collections.Array.find (fun entry ->
                                normalizeSlashes entry.path = normalizeSlashes pointerFilePath
                            )

                        let downloadedEntry =
                            entries
                            |> Microsoft.FSharp.Collections.Array.find (fun entry ->
                                normalizeSlashes entry.path = normalizeSlashes downloadedFilePath
                            )

                        Vitest.expect(pointerEntry.largeObject.IsSome).toBe (true)
                        Vitest.expect(downloadedEntry.largeObject.IsSome).toBe (true)

                        let pointerLargeObject = pointerEntry.largeObject |> Option.get
                        let downloadedLargeObject = downloadedEntry.largeObject |> Option.get

                        Vitest.expect(pointerLargeObject.Path).toBe ("pointer.psd")
                        Vitest.expect(pointerLargeObject.SizeBytes |> Option.get).toBeGreaterThan (0)
                        Vitest.expect(pointerLargeObject.IsMaterialized).toBe (false)
                        Vitest.expect(pointerLargeObject.IsLocallyAvailable).toBe (true)
                        expectHexObjectId pointerLargeObject

                        Vitest.expect(downloadedLargeObject.Path).toBe ("downloaded.psd")
                        Vitest.expect(downloadedLargeObject.SizeBytes |> Option.get).toBeGreaterThan (0)
                        Vitest.expect(downloadedLargeObject.IsMaterialized).toBe (true)
                        Vitest.expect(downloadedLargeObject.IsLocallyAvailable).toBe (true)
                        expectHexObjectId downloadedLargeObject
                    })
            }
        )

        Vitest.test (
            "exact LFS metadata path match remains available",
            fun () ->
                let largeObject = createObjectState "data.csv"

                let entries =
                    enrichFileEntries [ "data.csv", largeObject ] [| createFileEntry "data.csv" "/repo/data.csv" |]

                Vitest.expect(entries.[0].largeObject).toEqual (Some largeObject)
        )

        Vitest.test (
            "case-only disk path difference finds LFS metadata",
            fun () ->
                let largeObject = createObjectState "data.csv"

                let entries =
                    enrichFileEntries [ "data.csv", largeObject ] [| createFileEntry "Data.csv" "/repo/Data.csv" |]

                Vitest.expect(entries.[0].largeObject).toEqual (Some largeObject)
        )

        Vitest.test (
            "NFD disk path finds NFC Git metadata",
            fun () ->
                let gitName = "Messung_\u00E4.csv"
                let diskName = "Messung_a\u0308.csv"
                let largeObject = createObjectState gitName

                let entries =
                    enrichFileEntries [ gitName, largeObject ] [| createFileEntry diskName $"/repo/{diskName}" |]

                Vitest.expect(entries.[0].largeObject).toEqual (Some largeObject)
        )

        Vitest.test (
            "ambiguous case keys only match exact Git paths",
            fun () ->
                let lowerCaseObject = createObjectState "data.csv"
                let upperCaseObject = createObjectState "Data.csv"

                let entries =
                    enrichFileEntries [ "data.csv", lowerCaseObject; "Data.csv", upperCaseObject ] [|
                        createFileEntry "data.csv" "/repo/data.csv"
                        createFileEntry "Data.csv" "/repo/Data.csv"
                        createFileEntry "DaTa.csv" "/repo/DaTa.csv"
                    |]

                Vitest.expect(entries.[0].largeObject).toEqual (Some lowerCaseObject)
                Vitest.expect(entries.[1].largeObject).toEqual (Some upperCaseObject)
                Vitest.expect(entries.[2].largeObject).toEqual (None)
        )

        Vitest.test (
            "getFileEntryWithLfsMetadata enriches a single staged LFS file",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                do!
                    withTempRepository (fun context -> promise {
                        let pointerFilePath = join [| context.RepoPath; "single-pointer.psd" |]

                        let! _ = runGitAsync context.RepoPath [| "lfs"; "install"; "--local" |]
                        let! _ = runGitAsync context.RepoPath [| "lfs"; "track"; "*.psd" |]
                        do! writeUtf8FileAsync pointerFilePath "Single tracked content.\n"
                        let! _ = runGitAsync context.RepoPath [| "add"; ".gitattributes"; "single-pointer.psd" |]
                        ()

                        let! enrichedEntry =
                            FileTreeCreator.getFileEntryWithLfsMetadata context.RepoPath pointerFilePath

                        Vitest.expect(enrichedEntry.largeObject.IsSome).toBe (true)
                        let largeObject = enrichedEntry.largeObject |> Option.get

                        Vitest.expect(largeObject.Path).toBe ("single-pointer.psd")
                        Vitest.expect(largeObject.SizeBytes |> Option.get).toBeGreaterThan (0)
                        Vitest.expect(largeObject.IsMaterialized).toBe (true)
                        Vitest.expect(largeObject.IsLocallyAvailable).toBe (true)
                        expectHexObjectId largeObject
                    })
            }
        )

        Vitest.test (
            "files absent from large-object listing keep metadata None",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                do!
                    withTempRepository (fun context -> promise {
                        let untrackedLfsPath = join [| context.RepoPath; "untracked.psd" |]

                        let! _ = runGitAsync context.RepoPath [| "lfs"; "install"; "--local" |]
                        let! _ = runGitAsync context.RepoPath [| "lfs"; "track"; "*.psd" |]
                        do! writeUtf8FileAsync untrackedLfsPath "Untracked file content.\n"
                        let! _ = runGitAsync context.RepoPath [| "add"; ".gitattributes" |]
                        ()

                        let! enrichedEntry =
                            FileTreeCreator.getFileEntryWithLfsMetadata context.RepoPath untrackedLfsPath

                        Vitest.expect(enrichedEntry.largeObject).toEqual (None)
                    })
            }
        )

        Vitest.test (
            "no large objects keeps entries without metadata",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                do!
                    withTempRepository (fun context -> promise {
                        let plainFilePath = join [| context.RepoPath; "plain.txt" |]
                        do! writeUtf8FileAsync plainFilePath "Plain text.\n"

                        let! entries = FileTreeCreator.getFileEntries context.RepoPath true

                        let plainEntry =
                            entries
                            |> Array.find (fun entry -> normalizeSlashes entry.path = normalizeSlashes plainFilePath)

                        Vitest.expect(plainEntry.largeObject).toEqual (None)
                    })
            }
        )
)

Vitest.describe (
    "path keys, ignore rules and the case sensitivity probe",
    fun () ->
        let ignored name relativePath isDirectory =
            Swate.Electron.Shared.FileIOHelper.isIgnoredInventoryEntry name relativePath isDirectory

        Vitest.test (
            "PathKey normalizes separators, slashes, Unicode form and case",
            fun () ->
                let key insensitive path =
                    PathKey.value (PathKey.ofPath insensitive path)

                Vitest.expect(key true "C:\Arc\Data\\").toBe ("c:/arc/data")
                Vitest.expect(key false "C:\Arc\Data\\").toBe ("C:/Arc/Data")
                Vitest.expect(key true "c:/ARC/data").toBe (key true "C:/arc/DATA")
                Vitest.expect(key false "c:/ARC/data").not.toBe (key false "C:/arc/DATA")
                Vitest.expect(key true "Data").toBe (key true "data")
                Vitest.expect(key false "Data").not.toBe (key false "data")
                Vitest.expect(key false "ä.txt").toBe (key false "ä.txt")
                Vitest.expect(key true "Ä.txt").toBe (key true "Ä.txt")
        )

        Vitest.test (
            "scan and watcher rules agree on shared inputs",
            fun () ->
                let hex = String.replicate 32 "a"

                let cases = [
                    ".git", true
                    ".git/config", true
                    "notes/.GIT/config", true
                    ".gitignore", false
                    ".gitattributes", false
                    "assays/a1/isa_datamap", true
                    "assays/a1/isa.datamap.xlsx", false
                    "assays/a1/.~$data.xlsx", true
                    $"assays/a1/data.bin.vcs-lfs-backup-{hex}", true
                    "assays/a1/data.bin", false
                ]

                for relativePath, expectedIgnored in cases do
                    let name = PathHelpers.getNameFromPath relativePath
                    Vitest.expect(ignored name relativePath false).toBe expectedIgnored
                    Vitest.expect(Main.ArcVaultHelper.isFileWatcherPathIgnored relativePath).toBe expectedIgnored
        )

        Vitest.test (
            "a .git file, a .git directory and LFS backup directories are ignored",
            fun () ->
                let hex = String.replicate 32 "b"
                Vitest.expect(ignored ".git" ".git" false).toBe (true)
                Vitest.expect(ignored ".git" "sub/.git" true).toBe (true)
                Vitest.expect(ignored $"x.vcs-lfs-backup-{hex}" $"x.vcs-lfs-backup-{hex}" true).toBe (true)
                Vitest.expect(ignored $"x.vcs-lfs-backup-{hex}" $"a/x.vcs-lfs-backup-{hex}" false).toBe (true)
                Vitest.expect(ignored ".~$x.xlsx" "a/.~$x.xlsx" false).toBe (true)
                Vitest.expect(ignored "~$x.xlsx" "a/~$x.xlsx" false).toBe (true)
                Vitest.expect(ignored "a~$x.xlsx" "a/a~$x.xlsx" false).toBe (false)
                Vitest.expect(ignored "isa_datamap" "a/isa_datamap" true).toBe (false)
        )

        Vitest.test (
            "the tree scan skips ignored entries and keeps import staging folders",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                let! rootPath = createTempDirectoryAsync ()

                try
                    let hex = String.replicate 32 "c"
                    let stagingName = $".swate-import-{hex}"
                    do! fsPromisesDynamic?mkdir (join [| rootPath; ".git" |]) |> unbox<JS.Promise<unit>>

                    do!
                        fsPromisesDynamic?mkdir (join [| rootPath; stagingName |])
                        |> unbox<JS.Promise<unit>>

                    do!
                        fsPromisesDynamic?mkdir (join [| rootPath; $"old.vcs-lfs-backup-{hex}" |])
                        |> unbox<JS.Promise<unit>>

                    do! writeUtf8FileAsync (join [| rootPath; ".git"; "config" |]) "x"
                    do! writeUtf8FileAsync (join [| rootPath; ".~$lock.xlsx" |]) "x"
                    do! writeUtf8FileAsync (join [| rootPath; "keep.txt" |]) "x"

                    let! entries = FileTreeCreator.getFileEntries rootPath false

                    let names = entries |> Array.map (fun entry -> entry.name) |> Array.sort

                    Vitest
                        .expect(names)
                        .toEqual (
                            Array.sort [|
                                basename (normalizeSlashes rootPath)
                                stagingName
                                "keep.txt"
                            |]
                        )

                    do! removeDirectoryAsync rootPath
                with error ->
                    do! removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "the case probe agrees with a direct case-swapped lookup",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                let! rootPath = createTempDirectoryAsync ()

                try
                    do! writeUtf8FileAsync (join [| rootPath; "Probe.txt" |]) "x"

                    let! detected = FileTreeCreator.detectCaseInsensitiveAsync rootPath

                    let swappedExists =
                        Main.Bindings.Filesystem.existsSync (join [| rootPath; "pROBE.TXT" |])

                    Vitest.expect(detected).toBe (swappedExists)
                    do! removeDirectoryAsync rootPath
                with error ->
                    do! removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "the case probe maps same identity, missing and unreadable stats",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                let listRoot (_: string) = promise { return [| "123"; "Data" |] }

                let probe defaultInsensitive swappedStat =
                    FileTreeCreator.detectCaseInsensitiveWithAsync
                        defaultInsensitive
                        listRoot
                        (fun path -> promise {
                            return
                                if (normalizeSlashes path).EndsWith "/Data" then
                                    FileTreeCreator.ProbeIdentity("1", "42")
                                else
                                    swappedStat
                        })
                        "/arc"

                let! sameIdentity = probe false (FileTreeCreator.ProbeIdentity("1", "42"))
                let! otherIdentity = probe true (FileTreeCreator.ProbeIdentity("1", "43"))
                let! missing = probe true FileTreeCreator.ProbeMissing
                let! unreadableKeepsInsensitive = probe true FileTreeCreator.ProbeUnreadable
                let! unreadableKeepsSensitive = probe false FileTreeCreator.ProbeUnreadable

                Vitest.expect(sameIdentity).toBe (true)
                Vitest.expect(otherIdentity).toBe (true)
                Vitest.expect(missing).toBe (false)
                Vitest.expect(unreadableKeepsInsensitive).toBe (true)
                Vitest.expect(unreadableKeepsSensitive).toBe (false)
            }
        )
)
