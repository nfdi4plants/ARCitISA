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

let private fileTreeCreatorTestOptions = TestOptions(timeout = 20000)

let private normalizeSlashes (path: string) = path.Replace("\\", "/")

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

let private scanEntries (path: string) = promise {
    let! tree = TestHelpers.loadWholeTreeAsync path false
    return tree.Entries |> Seq.toArray
}

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

                    let! entries = scanEntries rootPath

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

        Vitest.test (
            "lock files of open Office and LibreOffice documents are hidden",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                let! rootPath = createTempDirectoryAsync ()

                try
                    for name in
                        [
                            "Book1.xlsx"
                            "~$Book1.xlsx"
                            ".~$Book2.xlsx"
                            ".~lock.Book1.xlsx#"
                        ] do
                        do! writeUtf8FileAsync (join [| rootPath; name |]) "content"

                    let! entries = scanEntries rootPath
                    let names = entries |> Array.map _.name

                    Vitest.expect(names).toContain ("Book1.xlsx")
                    Vitest.expect(names).not.toContain ("~$Book1.xlsx")
                    Vitest.expect(names).not.toContain (".~$Book2.xlsx")
                    Vitest.expect(names).not.toContain (".~lock.Book1.xlsx#")
                    do! removeDirectoryAsync rootPath
                with error ->
                    do! removeDirectoryAsync rootPath
                    return raise error
            }
        )

        Vitest.test (
            "hides .git folders in any case and shows folders named like lock files",
            fileTreeCreatorTestOptions,
            fun () -> promise {
                let! rootPath = createTempDirectoryAsync ()

                try
                    for folder, file in
                        [
                            "data/.GIT", "config"
                            "data", "a.txt"
                            "~$archive.old", "b.txt"
                        ] do
                        let folderPath = join [| rootPath; folder |]

                        do!
                            Main.Bindings.Filesystem.mkdirAsync
                                folderPath
                                (Main.Bindings.Filesystem.MkdirOptions(recursive = true))
                            |> Promise.map ignore

                        do! writeUtf8FileAsync (join [| folderPath; file |]) "content"

                    let! entries = scanEntries rootPath
                    let names = entries |> Array.map _.name

                    for name in [ "data"; "a.txt"; "~$archive.old"; "b.txt" ] do
                        Vitest.expect(names).toContain (name)

                    for name in [ ".GIT"; "config" ] do
                        Vitest.expect(names).not.toContain (name)

                    do! removeDirectoryAsync rootPath
                with error ->
                    do! removeDirectoryAsync rootPath
                    return raise error
            }
        )
)
