module ElectronRenderer.FileTreePerformanceTests

open Fable.Core
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeMaterialization
open Renderer.Context.FileTreeStore
open Swate.Electron.Shared.FileIOTypes
open Vitest

/// Timings of a folder with 7,202 files. They run when SWATE_PERF_TESTS is set and print their numbers.
[<Emit("(typeof process !== 'undefined' && !!process.env.SWATE_PERF_TESTS)")>]
let private perfEnabled: bool = jsNative

[<Emit("performance.now()")>]
let private now () : float = jsNative

let private fileCount = 7202

let private files = [|
    for index in 1..fileCount ->
        let name = "sample_" + (string index).PadLeft(5, '0') + "_R1.fastq.gz"
        FileEntry.create (name, "big/" + name, false)
|]

let private rootSnapshot: Swate.Electron.Shared.IPCTypes.FileTreeSnapshotDto = {|
    treeVersion = 1
    caseInsensitive = true
    entries = [|
        FileEntry.create ("arc", "", true)
        FileEntry.create ("big", "big", true)
        FileEntry.create ("studies", "studies", true)
        FileEntry.create ("README.md", "README.md", false)
    |]
    loadedDirectories = [| "" |]
    fileWatcher = {| state = "running"; reason = "" |}
|}

let private timed (label: string) (action: unit -> unit) =
    let start = now ()
    action ()
    let elapsed = now () - start
    JS.console.log ("[perf] " + label + ": " + string (System.Math.Round(elapsed, 1)) + " ms")
    elapsed

Vitest.describe (
    "file tree performance with 7,202 files",
    fun () ->
        Vitest.test (
            "builds the visible items and applies the directory update",
            TestOptions(skip = not perfEnabled, timeout = 60000),
            fun () ->
                let store = FileTreeStore()
                store.BeginLoad()
                store.ApplySnapshot rootSnapshot

                timed
                    "ApplyDirectoryUpdate (7,202 sorted entries)"
                    (fun () ->
                        store.ApplyDirectoryUpdate {|
                            treeVersion = 2
                            directories = [|
                                {|
                                    directoryPath = "big"
                                    children = files
                                |}
                            |]
                        |}
                    )
                |> ignore

                let cache =
                    Renderer.ReferenceCache.ReferenceCache<FileEntry, Swate.Components.Page.FileExplorer.Types.FileItem>()

                let lookup (_: string) = None

                let build () =
                    buildVisibleItems
                        (fun entry ->
                            cache.GetOrAdd(
                                entry,
                                Renderer.Components.LeftSidebar.FileExplorer.Helper.createItem lookup
                            )
                        )
                        (fun _ entries -> entries)
                        store.Snapshot
                        (Set.ofList [ "big" ])
                    |> ignore

                let cold = timed "buildVisibleItems cold" build
                let warm = timed "buildVisibleItems warm" build
                Vitest.expect(warm < cold).toBe true
        )
)
