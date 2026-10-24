module ElectronCore.LargeObjectIndexVaultTests

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron.Main
open Main.ArcVault
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
    member val Pending = ResizeArray<Result<ObjectStateDto[], string> -> unit>()

    member this.List (_root: string) (context: OperationContext) : JS.Promise<Result<ObjectStateDto[], string>> =
        this.Count <- this.Count + 1
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
    vault.GetLargeObjectIndexSnapshot().state = "ready"

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
                    Vitest.expect(vault.GetLargeObjectIndexSnapshot().state).toBe "notLoaded"
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
                        Vitest.expect(snapshot.state).toBe "ready"
                        Vitest.expect(snapshot.caseInsensitive).toBe true
                        Vitest.expect(snapshot.version).toBe 1

                        Vitest.expect(snapshot.entries |> Array.map (fun dto -> dto.Path) |> Array.sort).toEqual [|
                            "data/a.bin"
                            "data/b.bin"
                        |]

                    Vitest.expect(sent |> Seq.exists isIndexPush).toBe true
                })
        )
)
