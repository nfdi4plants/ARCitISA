module ElectronCore.LargeObjectIndexVaultTests

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron.Main
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultLargeObjects
open Main.ArcVaultFileTree
open Main.ArcVaultFileWatcher
open Fable.Electron
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest

[<Emit("((...args) => $0.push(args))")>]
let private recorder (sink: ResizeArray<obj[]>) : obj = jsNative

let private entry (path: string) : ObjectStateDto = {
    Path = path
    IsMaterialized = false
    IsLocallyAvailable = false
    SizeBytes = None
    ObjectId = None
}

/// Listing that answers at once, or waits for the test to resolve it when gated.
type private FakeListing() =
    member val Count = 0 with get, set
    member val Gated = false with get, set
    member val Entries: ObjectStateDto[] = [||] with get, set
    member val Contexts = ResizeArray<OperationContext>()
    member val Roots = ResizeArray<string>()
    member val Pending = ResizeArray<Result<ObjectStateDto[], string> -> unit>()

    member this.List (root: string) (context: OperationContext) : JS.Promise<Result<ObjectStateDto[], string>> =
        this.Count <- this.Count + 1
        this.Roots.Add root
        this.Contexts.Add context

        if this.Gated then
            Promise.create (fun resolve _ -> this.Pending.Add resolve)
        else
            promise { return Ok this.Entries }

let private recordingWindow (sent: ResizeArray<obj[]>) =
    createObj [
        "id" ==> 4242
        "title" ==> ""
        "isDestroyed" ==> (fun () -> false)
        "webContents"
        ==> createObj [
            "send" ==> recorder sent
            "isDestroyed" ==> (fun () -> false)
        ]
    ]
    |> unbox<BrowserWindow>

let private isIndexPush (args: obj[]) =
    args.Length > 0 && (string args.[0]).Contains("largeObjectIndexUpdate")

let private waitUntil (what: string) (condition: unit -> bool) = promise {
    let mutable waited = 0

    while not (condition ()) && waited < 3000 do
        do! Promise.sleep 10
        waited <- waited + 10

    if not (condition ()) then
        failwith $"Timed out waiting for {what}."
}

/// Long enough for the 20 ms debounce and a listing to finish.
let private settle () = Promise.sleep 150

let private isReady (vault: ArcVault) =
    vault.GetLargeObjectIndexSnapshot().state = LargeObjectIndexStatus.Ready

/// Starts the index of a vault bound to an existing directory and waits for its first build.
let private startVaultWith (caseInsensitive: bool) (root: string) (fake: FakeListing) = promise {
    let sent = ResizeArray<obj[]>()
    let vault = ArcVault(recordingWindow sent)
    vault.path <- Some root
    vault.isCaseInsensitive <- caseInsensitive
    vault.LargeObjectListing <- fake.List
    vault.LargeObjectInvalidationDebounceMs <- 20
    vault.StartLargeObjectIndex()

    do! waitUntil "the first listing" (fun () -> fake.Count >= 1)

    if not fake.Gated then
        do! waitUntil "the first build" (fun () -> isReady vault)

    return vault, sent
}

let private startVault = startVaultWith false

/// A stat for the content check that records the paths it reads.
let private countingStat (stats: ResizeArray<string>) (path: string) : JS.Promise<float option> = promise {
    stats.Add path

    try
        let! info = statAsync path
        return Some info.size
    with _ ->
        return None
}

let private withTempDirectory (testBody: string -> JS.Promise<unit>) = promise {
    let! root = TestHelpers.createTempDirectoryAsync "swate-lfs-vault-"

    try
        do! testBody root
    finally
        TestHelpers.removeDirectoryAsync root |> Promise.start
}

/// A repository with a .git folder and the two files the focus check reads.
let private writeGitDir (gitDir: string) =
    mkdirSync gitDir (MkdirOptions(recursive = true))
    TestHelpers.writeText (join [| gitDir; "HEAD" |]) "ref: refs/heads/main\n"
    TestHelpers.writeText (join [| gitDir; "index" |]) "index"

let private watcherEvent (root: string) (relativePath: string) : Main.ArcVaultTypes.ArcVaultFileSystemEvent = {
    EventName = "change"
    RelativePath = relativePath
    AbsolutePath = join [| root; relativePath |]
}

Vitest.describe (
    "ArcVault large object index",
    fun () ->
        Vitest.afterEach (fun () -> TestHelpers.electronMock?reset () |> ignore)

        Vitest.test (
            "three invalidations within the debounce window cause one listing",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake
                    Vitest.expect(fake.Count).toBe 1

                    for _ in 1..3 do
                        vault.InvalidateLargeObjectIndex()
                        do! Promise.sleep 5

                    do! settle ()
                    Vitest.expect(fake.Count).toBe 2
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a watcher batch causes a listing only when it contains the root .gitattributes",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    do!
                        vault.ApplyWatcherFileTreeEvents [
                            watcherEvent root "investigation.xlsx"
                            watcherEvent root "assays/A1/isa.assay.xlsx"
                        ]
                        |> Promise.map ignore

                    do! settle ()
                    Vitest.expect(fake.Count).toBe 1

                    do!
                        vault.ApplyWatcherFileTreeEvents [
                            watcherEvent root "investigation.xlsx"
                            watcherEvent root ".gitattributes"
                        ]
                        |> Promise.map ignore

                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 2
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a focus check with the Git state of the listing start causes no listing",
            fun () ->
                withTempDirectory (fun root -> promise {
                    writeGitDir (join [| root; ".git" |])
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    do! vault.CheckGitStateOnFocus()
                    do! vault.CheckGitStateOnFocus()
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 1
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "the first focus check records a baseline when the Git state was unreadable at the listing start",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake
                    writeGitDir (join [| root; ".git" |])

                    do! vault.CheckGitStateOnFocus()
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 1

                    TestHelpers.writeText
                        (join [| root; ".git"; "HEAD" |])
                        "ref: refs/heads/other
"

                    do! vault.CheckGitStateOnFocus()
                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a focus check after a HEAD change that followed the build causes one listing",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let gitDir = join [| root; ".git" |]
                    writeGitDir gitDir
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    TestHelpers.writeText (join [| gitDir; "HEAD" |]) "ref: refs/heads/other\n"
                    do! vault.CheckGitStateOnFocus()

                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 2
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a focus check after an index mtime change causes one listing",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let gitDir = join [| root; ".git" |]
                    writeGitDir gitDir
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    let later = DateTime.UtcNow.AddSeconds 30.0
                    VersionControlService.Runtime.Node.FileSystem.utimesSync (join [| gitDir; "index" |]) later later
                    do! vault.CheckGitStateOnFocus()

                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 2
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a .git file with a relative gitdir line names the git directory",
            fun () ->
                withTempDirectory (fun parent -> promise {
                    let root = join [| parent; "arc" |]
                    let gitDir = join [| parent; "worktrees"; "arc-git" |]
                    mkdirSync root (MkdirOptions(recursive = true))
                    writeGitDir gitDir
                    TestHelpers.writeText (join [| root; ".git" |]) "gitdir: ../worktrees/arc-git\n"
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    TestHelpers.writeText (join [| gitDir; "HEAD" |]) "ref: refs/heads/other\n"
                    do! vault.CheckGitStateOnFocus()

                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a restarted index starts above the last version of the disposed one",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    let! vault, sent = startVault root fake
                    let lastOldVersion = vault.GetLargeObjectIndexSnapshot().version

                    vault.DisposeLargeObjectIndex()
                    sent.Clear()
                    vault.StartLargeObjectIndex()
                    do! waitUntil "the first push of the new index" (fun () -> sent |> Seq.exists isIndexPush)

                    Vitest.expect(vault.GetLargeObjectIndexSnapshot().version).toBeGreaterThan lastOldVersion
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "closing the vault cancels a running listing and its result is not pushed",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    fake.Gated <- true
                    let! vault, sent = startVault root fake
                    ARC_VAULTS.Vaults.[4242] <- vault

                    Vitest.expect(fake.Count).toBe 1
                    let pushesBeforeClose = sent |> Seq.filter isIndexPush |> Seq.length
                    Vitest.expect(pushesBeforeClose).toBe 1

                    ARC_VAULTS.DisposeVault 4242
                    Vitest.expect(fake.Contexts.[0].Cancellation.IsCancellationRequested()).toBe true
                    Vitest.expect(vault.LargeObjectIndex.IsNone).toBe true

                    for resolve in fake.Pending do
                        resolve (Ok [| entry "late.bin" |])

                    do! settle ()
                    Vitest.expect(sent |> Seq.filter isIndexPush |> Seq.length).toBe pushesBeforeClose
                    Vitest.expect(vault.GetLargeObjectIndexSnapshot().state).toBe LargeObjectIndexStatus.NotLoaded
                })
        )

        Vitest.test (
            "the pull returns the ready snapshot with its entries and case flag",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    fake.Entries <- [| entry "data/a.bin"; entry "data/b.bin" |]
                    let! vault, sent = startVaultWith true root fake
                    ARC_VAULTS.Vaults.[4242] <- vault

                    TestHelpers.electronMock?setBrowserWindowFromWebContents (fun (webContents: obj) ->
                        if unbox<int> webContents?id = 4242 then
                            box vault.window
                        else
                            null
                    )
                    |> ignore

                    let event =
                        createObj [ "sender" ==> createObj [ "id" ==> 4242 ] ]
                        |> unbox<IpcMainInvokeEvent>

                    let api = Main.IPC.ArcVaultsApi.api event
                    let! pulled = api.getLargeObjectIndex ()
                    ARC_VAULTS.Vaults.Remove 4242 |> ignore
                    vault.DisposeLargeObjectIndex()

                    match pulled with
                    | Error error -> failwith error.Message
                    | Ok snapshot ->
                        Vitest.expect(snapshot.state).toBe LargeObjectIndexStatus.Ready
                        Vitest.expect(snapshot.caseInsensitive).toBe true
                        Vitest.expect(snapshot.version).toBe 1

                        Vitest.expect(snapshot.entries |> Array.map (fun dto -> dto.Path) |> Array.sort).toEqual [|
                            "data/a.bin"
                            "data/b.bin"
                        |]

                    Vitest.expect(sent |> Seq.exists isIndexPush).toBe true
                })
        )

        Vitest.test (
            "a successful rename lists again for the new root",
            fun () ->
                withTempDirectory (fun parent -> promise {
                    let root = join [| parent; "arcA" |]
                    mkdirSync root (MkdirOptions(recursive = true))
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    match! vault.RenameOpenArcRoot "arcB" with
                    | Error error -> failwith error.Message
                    | Ok renamedPath ->
                        do! waitUntil "the listing for the new root" (fun () -> fake.Roots.Count >= 2)
                        Vitest.expect(fake.Roots.[fake.Roots.Count - 1]).toBe renamedPath
                        Vitest.expect(vault.path).toEqual (Some renamedPath)

                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a revalidation finds a pointer file that now has its content and lists again once",
            fun () ->
                withTempDirectory (fun root -> promise {
                    mkdirSync (join [| root; "data" |]) (MkdirOptions(recursive = true))
                    // The index lists a pointer of 5000 bytes, and git lfs checkout wrote the full content.
                    TestHelpers.writeText (join [| root; "data"; "big.bin" |]) (String.replicate 5000 "x")
                    let fake = FakeListing()

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 5000.0
                        }
                    |]

                    let! vault, _ = startVault root fake
                    let stats = ResizeArray<string>()
                    vault.LargeObjectContentStat <- countingStat stats

                    do! vault.RevalidateDirectories [| "data" |]
                    do! waitUntil "the second listing" (fun () -> fake.Count = 2)
                    do! settle ()

                    Vitest.expect(fake.Count).toBe 2
                    Vitest.expect(stats.Count).toBe 1
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a revalidation with files that match the index causes no listing",
            fun () ->
                withTempDirectory (fun root -> promise {
                    mkdirSync (join [| root; "data" |]) (MkdirOptions(recursive = true))

                    TestHelpers.writeText
                        (join [| root; "data"; "big.bin" |])
                        "version https://git-lfs.github.com/spec/v1
"

                    let fake = FakeListing()

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 5000.0
                        }
                    |]

                    let! vault, _ = startVault root fake
                    let stats = ResizeArray<string>()
                    vault.LargeObjectContentStat <- countingStat stats

                    do! vault.RevalidateDirectories [| "data" |]
                    do! waitUntil "the content check" (fun () -> stats.Count = 1)
                    do! settle ()

                    Vitest.expect(fake.Count).toBe 1
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a revalidation of a directory without large objects stats no file",
            fun () ->
                withTempDirectory (fun root -> promise {
                    mkdirSync (join [| root; "data" |]) (MkdirOptions(recursive = true))
                    mkdirSync (join [| root; "other" |]) (MkdirOptions(recursive = true))
                    TestHelpers.writeText (join [| root; "other"; "plain.txt" |]) "plain"
                    let fake = FakeListing()

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 5000.0
                        }
                    |]

                    let! vault, _ = startVault root fake
                    let stats = ResizeArray<string>()
                    vault.LargeObjectContentStat <- countingStat stats

                    do! vault.RevalidateDirectories [| "other"; "" |]
                    do! settle ()

                    Vitest.expect(stats.Count).toBe 0
                    Vitest.expect(fake.Count).toBe 1
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a locally modified file that the index keeps listing as a pointer causes no listing on any focus",
            fun () ->
                withTempDirectory (fun root -> promise {
                    mkdirSync (join [| root; "data" |]) (MkdirOptions(recursive = true))
                    // The object has 2048 bytes. The working file was overwritten with 3000 bytes.
                    TestHelpers.writeText (join [| root; "data"; "big.bin" |]) (String.replicate 3000 "x")
                    let fake = FakeListing()

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 2048.0
                        }
                    |]

                    let! vault, _ = startVault root fake
                    let stats = ResizeArray<string>()
                    vault.LargeObjectContentStat <- countingStat stats

                    do! vault.RevalidateDirectories [| "data" |]
                    do! waitUntil "the first content check" (fun () -> stats.Count = 1)
                    do! settle ()
                    do! vault.RevalidateDirectories [| "data" |]
                    do! waitUntil "the second content check" (fun () -> stats.Count = 2)
                    do! settle ()

                    Vitest.expect(fake.Count).toBe 1
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a file that reaches its object size causes one listing and then settles",
            fun () ->
                withTempDirectory (fun root -> promise {
                    mkdirSync (join [| root; "data" |]) (MkdirOptions(recursive = true))
                    let file = join [| root; "data"; "big.bin" |]

                    TestHelpers.writeText
                        file
                        "version https://git-lfs.github.com/spec/v1
"

                    let fake = FakeListing()

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 3000.0
                        }
                    |]

                    let! vault, _ = startVault root fake
                    let stats = ResizeArray<string>()
                    vault.LargeObjectContentStat <- countingStat stats

                    do! vault.RevalidateDirectories [| "data" |]
                    do! waitUntil "the first check" (fun () -> stats.Count = 1)
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 1

                    // git lfs checkout writes the content, and the next listing reports the file as checked out.
                    TestHelpers.writeText file (String.replicate 3000 "x")

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 3000.0
                                IsMaterialized = true
                        }
                    |]

                    do! vault.RevalidateDirectories [| "data" |]
                    do! waitUntil "the second listing" (fun () -> fake.Count = 2)
                    do! settle ()
                    do! vault.RevalidateDirectories [| "data" |]
                    do! settle ()

                    Vitest.expect(fake.Count).toBe 2
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a mismatch found while a rebuild is already listing adds no second listing",
            fun () ->
                withTempDirectory (fun root -> promise {
                    mkdirSync (join [| root; "data" |]) (MkdirOptions(recursive = true))
                    TestHelpers.writeText (join [| root; "data"; "big.bin" |]) (String.replicate 5000 "x")
                    let fake = FakeListing()

                    fake.Entries <- [|
                        {
                            entry "data/big.bin" with
                                SizeBytes = Some 5000.0
                        }
                    |]

                    let! vault, _ = startVault root fake
                    let stats = ResizeArray<string>()
                    vault.LargeObjectContentStat <- countingStat stats

                    // The Git state check of the same focus started a rebuild, and its listing is still under way.
                    fake.Gated <- true
                    vault.InvalidateLargeObjectIndex()
                    do! waitUntil "the rebuild listing" (fun () -> fake.Count = 2)

                    do! vault.RevalidateDirectories [| "data" |]
                    do! settle ()
                    fake.Pending.[0](Ok fake.Entries)
                    do! settle ()

                    Vitest.expect(fake.Count).toBe 2
                    Vitest.expect(stats.Count).toBe 0
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "the content rule compares materialized files with the object size and pointers with the pointer size",
            fun () ->
                Vitest.expect(lfsContentMismatch true (Some 5000.0) 5000.0).toBe false
                Vitest.expect(lfsContentMismatch true (Some 5000.0) 130.0).toBe true
                Vitest.expect(lfsContentMismatch false (Some 5000.0) 130.0).toBe false
                Vitest.expect(lfsContentMismatch false (Some 5000.0) 5000.0).toBe true
                // A locally modified file keeps the state Git LFS reports for it, whatever its size is.
                Vitest.expect(lfsContentMismatch false (Some 2048.0) 3000.0).toBe false
                Vitest.expect(lfsContentMismatch false None 4096.0).toBe false
                Vitest.expect(lfsContentMismatch true None 10.0).toBe false
        )

        Vitest.test (
            "a successful rename keeps the ready state visible while the replacement lists again",
            fun () ->
                withTempDirectory (fun parent -> promise {
                    let root = join [| parent; "arcA" |]
                    mkdirSync root (MkdirOptions(recursive = true))
                    let fake = FakeListing()
                    fake.Entries <- [| entry "a.bin" |]
                    let! vault, sent = startVault root fake
                    let versionBefore = vault.GetLargeObjectIndexSnapshot().version
                    let pushesBefore = sent.Count

                    match! vault.RenameOpenArcRoot "arcB" with
                    | Error error -> failwith error.Message
                    | Ok _ -> ()

                    let seeded = vault.GetLargeObjectIndexSnapshot()
                    Vitest.expect(seeded.state).toBe LargeObjectIndexStatus.Ready
                    Vitest.expect(seeded.entries.Length).toBe 1
                    Vitest.expect(seeded.version).toBe versionBefore

                    do! waitUntil "the replacement listing" (fun () -> fake.Count = 2)
                    do! settle ()

                    // No push after the rename shows the index as not loaded or loading.
                    let states =
                        sent
                        |> Seq.skip pushesBefore
                        |> Seq.filter isIndexPush
                        |> Seq.map (fun args -> emitJsExpr args "JSON.stringify($0)": string)
                        |> Seq.toArray

                    Vitest
                        .expect(
                            states
                            |> Array.exists (fun text -> text.Contains "notLoaded" || text.Contains "loading")
                        )
                        .toBe
                        false

                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a failed rename keeps the ready state visible while the replacement lists again",
            fun () ->
                withTempDirectory (fun parent -> promise {
                    let root = join [| parent; "arcA" |]
                    mkdirSync root (MkdirOptions(recursive = true))
                    mkdirSync (join [| parent; "arcB" |]) (MkdirOptions(recursive = true))
                    let fake = FakeListing()
                    fake.Entries <- [| entry "a.bin" |]
                    let! vault, _ = startVault root fake
                    let versionBefore = vault.GetLargeObjectIndexSnapshot().version

                    match! vault.RenameOpenArcRoot "arcB" with
                    | Ok _ -> failwith "The rename onto an existing folder should fail."
                    | Error _ -> ()

                    let seeded = vault.GetLargeObjectIndexSnapshot()
                    Vitest.expect(seeded.state).toBe LargeObjectIndexStatus.Ready
                    Vitest.expect(seeded.entries.Length).toBe 1
                    Vitest.expect(seeded.version).toBe versionBefore

                    let states = ResizeArray<LargeObjectIndexStatus>()

                    vault.LargeObjectIndex.Value.Subscribe(fun () ->
                        states.Add(vault.GetLargeObjectIndexSnapshot().state)
                    )
                    |> ignore

                    do! waitUntil "the replacement listing" (fun () -> fake.Count = 2)
                    do! settle ()
                    Vitest.expect(states.Contains LargeObjectIndexStatus.Loading).toBe false
                    Vitest.expect(vault.GetLargeObjectIndexSnapshot().version).toBe (versionBefore + 1)
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a rename waits until the running listing call has returned",
            fun () ->
                withTempDirectory (fun parent -> promise {
                    let root = join [| parent; "arcA" |]
                    mkdirSync root (MkdirOptions(recursive = true))
                    let fake = FakeListing()
                    fake.Gated <- true
                    let! vault, _ = startVault root fake
                    let finished = ref false

                    let renaming =
                        vault.RenameOpenArcRoot "arcB"
                        |> Promise.map (fun result ->
                            finished.Value <- true
                            result
                        )

                    do! Promise.sleep 100
                    Vitest.expect(finished.Value).toBe false
                    Vitest.expect(fake.Contexts.[0].Cancellation.IsCancellationRequested()).toBe true

                    for resolve in fake.Pending do
                        resolve (Error "canceled")

                    let! result = renaming
                    Vitest.expect(finished.Value).toBe true
                    Vitest.expect(result.IsOk).toBe true
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a failed initialization disposes the index",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    do! vault.RestoreEmptyVaultAfterFailedInitialization()

                    Vitest.expect(vault.LargeObjectIndex.IsNone).toBe true
                })
        )


        Vitest.test (
            "a focus check sees a branch that moved to another commit through its loose ref",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let gitDir = join [| root; ".git" |]
                    writeGitDir gitDir
                    mkdirSync (join [| gitDir; "refs"; "heads" |]) (MkdirOptions(recursive = true))

                    TestHelpers.writeText
                        (join [| gitDir; "refs"; "heads"; "main" |])
                        "aaaa
"

                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    do! vault.CheckGitStateOnFocus()
                    do! settle ()
                    Vitest.expect(fake.Count).toBe 1

                    TestHelpers.writeText
                        (join [| gitDir; "refs"; "heads"; "main" |])
                        "bbbb
"

                    do! vault.CheckGitStateOnFocus()
                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    vault.DisposeLargeObjectIndex()
                })
        )

        Vitest.test (
            "a focus check sees a branch that moved to another commit through packed-refs",
            fun () ->
                withTempDirectory (fun root -> promise {
                    let gitDir = join [| root; ".git" |]
                    writeGitDir gitDir

                    TestHelpers.writeText
                        (join [| gitDir; "packed-refs" |])
                        "# pack-refs
aaaa refs/heads/main
"

                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    TestHelpers.writeText
                        (join [| gitDir; "packed-refs" |])
                        "# pack-refs
bbbb refs/heads/main
"

                    do! vault.CheckGitStateOnFocus()
                    do! waitUntil "the rebuild" (fun () -> fake.Count = 2)
                    vault.DisposeLargeObjectIndex()
                })
        )


        Vitest.test (
            "a write that starts while the writer queue drains makes the rename fail as busy",
            fun () ->
                withTempDirectory (fun parent -> promise {
                    let root = join [| parent; "arcA" |]
                    mkdirSync root (MkdirOptions(recursive = true))
                    let fake = FakeListing()
                    let! vault, _ = startVault root fake

                    let release = ref (fun () -> ())

                    let drain =
                        vault.FileTreeWriter.Enqueue(fun _ ->
                            Promise.create (fun resolve _ -> release.Value <- fun () -> resolve [||])
                        )

                    let renaming = vault.RenameOpenArcRoot "arcB"
                    do! Promise.sleep 50

                    let finishWrite = ref (fun () -> ())

                    let writing =
                        vault.WithBusyWritingScope(fun () ->
                            Promise.create (fun resolve _ -> finishWrite.Value <- fun () -> resolve ())
                        )

                    release.Value()
                    do! drain
                    let! result = renaming

                    match result with
                    | Ok _ -> failwith "The rename should fail while a write runs."
                    | Error error -> Vitest.expect(error.Message).toContain "still writing"

                    finishWrite.Value()
                    do! writing
                    vault.DisposeLargeObjectIndex()
                })
        )

)
