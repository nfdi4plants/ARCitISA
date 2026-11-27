module ElectronCore.VersionControlCompositionTests

open Fable.Core
open Main.VersionControl
open Arcitisa.Components.Composite.Authentication.Types
open VersionControlService.Abstractions
open VersionControlService.Git.GitCredentialStrategy
open Vitest
open ElectronCore.TestHelpers

let private account
    (localId: string)
    (hub: string)
    (username: string)
    (email: string)
    (commitEmail: string option)
    : AccountSummary =
    {
        User = {
            Id = 1
            LocalArcitisaAccountId = localId
            Username = username
            Name = "Display Name"
            Email = email
            CommitEmail = commitEmail
            AvatarUrl = ""
            TargetDataHub = hub
        }
        DateAdded = "2026-09-18T00:00:00Z"
        TokenStatus = TokenStatus.Ok
        TokenExpiresOn = None
    }

let private gitLab =
    account
        "acc-gitlab"
        "https://git.nfdi4plants.org"
        "carol"
        "carol@example.org"
        (Some "123-carol@noreply.example.org")

let private other =
    account "acc-other" "https://gitlab.example.com/" "other" "other@example.com" None

let private state (active: AccountSummary option) (stored: AccountSummary list) : AuthStateDto = {
    ActiveAccount = active
    StoredAccounts = List.toArray stored
}

/// Mirrors AuthService.tryGetTokenForHost: the active account wins when its host
/// matches, then any stored account for the host, compared without regard to case.
let private source (authState: AuthStateDto) (tokens: (string * string) list) : DataHubStrategies.DataHubAccountSource =
    let tokenOf (candidate: AccountSummary) =
        tokens
        |> List.tryFind (fun (id, _) -> id = candidate.User.LocalArcitisaAccountId)
        |> Option.map snd

    let hostMatches (host: string) (candidate: AccountSummary) =
        System.String.Equals(
            DataHubStrategies.hostOfDataHub candidate.User.TargetDataHub,
            host,
            System.StringComparison.OrdinalIgnoreCase
        )

    {
        GetState = fun () -> authState
        TryGetTokenForAccount = fun localId -> tokens |> List.tryFind (fun (id, _) -> id = localId) |> Option.map snd
        TryGetTokenForHost =
            fun host ->
                authState.ActiveAccount
                |> Option.filter (hostMatches host)
                |> Option.bind tokenOf
                |> Option.orElseWith (fun () ->
                    authState.StoredAccounts
                    |> Array.tryFind (hostMatches host)
                    |> Option.bind tokenOf
                )
    }

let private identityRequest (host: string option) (profile: string option) : RevisionIdentityRequest = {
    RepositoryRoot = "C:/arcs/demo"
    TargetHost = host
    ConnectionProfileId = profile
}

let private gitProviderId =
    ProviderId.tryCreate "git" |> Result.defaultWith failwith

let private lakeFsProviderId =
    ProviderId.tryCreate "lakefs" |> Result.defaultWith failwith

/// A fake provider that detects `root` as its repository root, or detects nothing.
let private fakeOwner (id: string) (root: string option) : ProviderComposition.AppProvider =
    fakeProvider
        (ProviderId.tryCreate id |> Result.defaultWith failwith)
        (fun _ -> async {
            return
                match root with
                | Some detected -> Detected(detected, 50, None)
                | None -> NotDetected
        })
        (fun _ -> async { return () })
        missingFunctions

Vitest.describe (
    "DataHUB identity strategy",
    fun () ->
        Vitest.test (
            "the target host selects the stored account for that hub over the active one",
            fun () ->
                let identity =
                    DataHubStrategies.resolveIdentity
                        (source (state (Some other) [ gitLab; other ]) [])
                        (identityRequest (Some "git.nfdi4plants.org") None)

                Vitest
                    .expect(identity)
                    .toEqual (
                        Some {
                            Name = "carol"
                            Email = "123-carol@noreply.example.org"
                        }
                    )
        )

        Vitest.test (
            "no target host falls back to the active account",
            fun () ->
                let identity =
                    DataHubStrategies.resolveIdentity
                        (source (state (Some other) [ gitLab; other ]) [])
                        (identityRequest None None)

                Vitest
                    .expect(identity)
                    .toEqual (
                        Some {
                            Name = "other"
                            Email = "other@example.com"
                        }
                    )
        )

        Vitest.test (
            "a host without a matching account yields no identity",
            fun () ->
                let identity =
                    DataHubStrategies.resolveIdentity
                        (source (state (Some gitLab) [ gitLab ]) [])
                        (identityRequest (Some "github.com") None)

                Vitest.expect(identity).toEqual None
        )

        Vitest.test (
            "nobody signed in yields no identity",
            fun () ->
                let identity =
                    DataHubStrategies.resolveIdentity (source AuthStateDto.Empty []) (identityRequest None None)

                Vitest.expect(identity).toEqual None
        )

        Vitest.test (
            "the connection profile selects its account when the host matches and is ignored otherwise",
            fun () ->
                let authState = state (Some gitLab) [ gitLab; other ]

                let matching =
                    DataHubStrategies.resolveIdentity
                        (source authState [])
                        (identityRequest (Some "gitlab.example.com") (Some "acc-other"))

                let mismatched =
                    DataHubStrategies.resolveIdentity
                        (source authState [])
                        (identityRequest (Some "git.nfdi4plants.org") (Some "acc-other"))

                Vitest.expect(matching |> Option.map _.Name).toEqual (Some "other")
                Vitest.expect(mismatched |> Option.map _.Name).toEqual (Some "carol")
        )

        Vitest.test (
            "a throwing account source yields None instead of an exception",
            fun () ->
                let broken: DataHubStrategies.DataHubAccountSource = {
                    GetState = fun () -> failwith "store unavailable"
                    TryGetTokenForAccount = fun _ -> failwith "store unavailable"
                    TryGetTokenForHost = fun _ -> failwith "store unavailable"
                }

                Vitest.expect(DataHubStrategies.resolveIdentity broken (identityRequest None None)).toEqual None
                Vitest.expect(DataHubStrategies.resolveCredential broken "git.nfdi4plants.org" None).toEqual None
        )
)

Vitest.describe (
    "DataHUB credential strategy",
    fun () ->
        Vitest.test (
            "returns the oauth2 credential for the account of the requested host",
            fun () ->
                let credential =
                    DataHubStrategies.resolveCredential
                        (source (state (Some other) [ gitLab; other ]) [
                            "acc-gitlab", "glpat-1"
                            "acc-other", "glpat-2"
                        ])
                        "GIT.nfdi4plants.org"
                        None

                Vitest
                    .expect(credential)
                    .toEqual (
                        Some {
                            Username = "oauth2"
                            Secret = "glpat-1"
                        }
                    )
        )

        Vitest.test (
            "returns None for a host without a usable token",
            fun () ->
                let credential =
                    DataHubStrategies.resolveCredential
                        (source (state (Some gitLab) [ gitLab ]) [])
                        "git.nfdi4plants.org"
                        None

                Vitest.expect(credential).toEqual None
        )

        Vitest.test (
            "two accounts on one host: the active account supplies the credential",
            fun () ->
                let secondGitLab =
                    account "acc-gitlab-2" "https://GIT.nfdi4plants.org/" "second" "second@example.org" None

                let tokens = [ "acc-gitlab", "glpat-1"; "acc-gitlab-2", "glpat-2" ]

                let credential =
                    DataHubStrategies.resolveCredential
                        (source (state (Some secondGitLab) [ gitLab; secondGitLab ]) tokens)
                        "git.nfdi4plants.org"
                        None

                Vitest.expect(credential |> Option.map _.Secret).toEqual (Some "glpat-2")
        )

        Vitest.test (
            "the connection profile is only used for its own host",
            fun () ->
                let tokens = [ "acc-gitlab", "glpat-1"; "acc-other", "glpat-2" ]
                let authState = state (Some gitLab) [ gitLab; other ]

                let ownHost =
                    DataHubStrategies.resolveCredential
                        (source authState tokens)
                        "gitlab.example.com"
                        (Some "acc-other")

                let foreignHost =
                    DataHubStrategies.resolveCredential
                        (source authState tokens)
                        "git.nfdi4plants.org"
                        (Some "acc-other")

                Vitest.expect(ownHost |> Option.map _.Secret).toEqual (Some "glpat-2")
                Vitest.expect(foreignHost |> Option.map _.Secret).toEqual (Some "glpat-1")
        )
)

Vitest.describe (
    "DataHUB revision policy",
    fun () ->
        Vitest.test (
            "a metadata workbook is inline whatever its size",
            fun () ->
                let largeMetadataPath =
                    RepositoryPath.tryCreate "studies/s1/isa.study.xlsx"
                    |> Result.defaultWith failwith

                let smallMetadataPath =
                    RepositoryPath.tryCreate "isa.investigation.xlsx" |> Result.defaultWith failwith

                let largeMetadataPolicy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy {
                        Path = largeMetadataPath
                        SizeInBytes = 3.0e8
                    }

                let smallMetadataPolicy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy {
                        Path = smallMetadataPath
                        SizeInBytes = 10.0
                    }

                Vitest.expect(largeMetadataPolicy).toEqual RevisionPathPolicy.Inline
                Vitest.expect(smallMetadataPolicy).toEqual RevisionPathPolicy.Inline
        )

        Vitest.test (
            "a file below a dataset folder is a large object whatever its size",
            fun () ->
                let path =
                    RepositoryPath.tryCreate "assays/a1/dataset/raw.csv"
                    |> Result.defaultWith failwith

                let policy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy { Path = path; SizeInBytes = 10.0 }

                Vitest.expect(policy).toEqual RevisionPathPolicy.LargeObject
        )

        Vitest.test (
            "a file above 25 MB is a large object",
            fun () ->
                let path =
                    RepositoryPath.tryCreate "assays/a1/notes.bin" |> Result.defaultWith failwith

                let aboveThresholdPolicy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy {
                        Path = path
                        SizeInBytes = float Arcitisa.Components.Shared.GitLfsRules.maxNonLfsFileSizeBytes + 1.0
                    }

                let atThresholdPolicy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy {
                        Path = path
                        SizeInBytes = float Arcitisa.Components.Shared.GitLfsRules.maxNonLfsFileSizeBytes
                    }

                Vitest.expect(aboveThresholdPolicy).toEqual RevisionPathPolicy.LargeObject
                Vitest.expect(atThresholdPolicy).toEqual RevisionPathPolicy.Automatic
        )

        Vitest.test (
            "metadata wins over the dataset folder",
            fun () ->
                let path =
                    RepositoryPath.tryCreate "assays/a1/dataset/isa.assay.xlsx"
                    |> Result.defaultWith failwith

                let policy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy { Path = path; SizeInBytes = 3.0e8 }

                Vitest.expect(policy).toEqual RevisionPathPolicy.Inline
        )

        Vitest.test (
            "everything else is automatic",
            fun () ->
                let path = RepositoryPath.tryCreate "README.md" |> Result.defaultWith failwith

                let policy =
                    ProviderComposition.dataHubRevisionPolicy.ResolvePathPolicy { Path = path; SizeInBytes = 100.0 }

                Vitest.expect(policy).toEqual RevisionPathPolicy.Automatic
        )
)

Vitest.describe (
    "Text diff workers",
    fun () ->
        Vitest.test (
            "development builds load the worker next to the main bundle, packaged builds the unpacked copy",
            fun () ->
                let development =
                    TextDiffWorkers.workerPath false "C:/ARCitISA/resources" "C:/repo/.vite/build"

                let packaged =
                    TextDiffWorkers.workerPath true "C:/ARCitISA/resources" "C:/repo/.vite/build"

                Vitest.expect(development.Replace('\\', '/')).toBe "C:/repo/.vite/build/text-diff-worker.cjs"

                Vitest.expect(packaged.Replace('\\', '/')).toBe
                    "C:/ARCitISA/resources/app.asar.unpacked/.vite/build/text-diff-worker.cjs"
        )

        Vitest.test (
            "spools go to the Electron temp folder on Windows and macOS",
            fun () ->
                let tempRoot platform =
                    TextDiffWorkers.tempRootFor
                        platform
                        (Some "/xdg/cache")
                        "/home/carol"
                        (fun () -> "/electron/temp")
                        "ARCitISA"

                Vitest.expect(tempRoot "win32").toBe "/electron/temp"
                Vitest.expect(tempRoot "darwin").toBe "/electron/temp"
        )

        Vitest.test (
            "spools go to the XDG cache folder on Linux, with ~/.cache as the fallback",
            fun () ->
                let tempRoot xdgCacheHome =
                    TextDiffWorkers.tempRootFor
                        "linux"
                        xdgCacheHome
                        "/home/carol"
                        (fun () -> failwith "Linux does not use the Electron temp folder.")
                        "ARCitISA"

                let homeCache = Main.Bindings.Path.join [| "/home/carol"; ".cache"; "ARCitISA" |]

                Vitest
                    .expect(tempRoot (Some "/xdg/cache"))
                    .toBe (Main.Bindings.Path.join [| "/xdg/cache"; "ARCitISA" |])

                Vitest.expect(tempRoot None).toBe homeCache
                Vitest.expect(tempRoot (Some "relative/cache")).toBe homeCache
                Vitest.expect(tempRoot (Some "")).toBe homeCache
        )
)

Vitest.describe (
    "Provider composition",
    fun () ->
        let providers () = [|
            ProviderComposition.createGitProvider (source AuthStateDto.Empty []) WorkspaceSessionHost.windowOwnerOf
            ProviderComposition.createLakeFsProvider (ProviderComposition.lakeFsOptions "C:/settings" CaseInsensitive)
        |]

        Vitest.test (
            "the provider table registers exactly the git and lakefs providers",
            fun () ->
                let table = providers ()

                let ids =
                    table |> Array.map (fun provider -> ProviderId.value provider.Id) |> Array.sort

                Vitest.expect(ids).toEqual [| "git"; "lakefs" |]

                for provider in table do
                    Vitest
                        .expect(ProviderComposition.tryFindProvider table provider.Id |> Option.map _.Id)
                        .toEqual (Some provider.Id)
        )

        Vitest.test (
            "lakeFS state lives below the settings root",
            fun () ->
                let options = ProviderComposition.lakeFsOptions "C:/settings" CaseSensitive
                Vitest.expect(options.StateRoot.Replace('\\', '/')).toBe "C:/settings/VersionControlState"
                Vitest.expect(options.PathCaseSensitivity).toEqual CaseSensitive
        )

        Vitest.test (
            "path case sensitivity follows the platform",
            fun () ->
                Vitest.expect(ProviderComposition.pathCaseSensitivityForPlatform "win32").toEqual CaseInsensitive
                Vitest.expect(ProviderComposition.pathCaseSensitivityForPlatform "darwin").toEqual CaseInsensitive
                Vitest.expect(ProviderComposition.pathCaseSensitivityForPlatform "linux").toEqual CaseSensitive
        )

        Vitest.test (
            "roots compare without regard to separators, and to case only when the platform ignores it",
            fun () ->
                Vitest
                    .expect(
                        ProviderComposition.rootsEqual
                            CaseInsensitive
                            "C:\\Users\\Carol\\ARCs\\Demo"
                            "c:/users/carol/arcs/demo/"
                    )
                    .toBe
                    true

                Vitest
                    .expect(
                        ProviderComposition.rootsEqual CaseSensitive "/home/carol/arcs/demo" "/home/carol/arcs/Demo"
                    )
                    .toBe
                    false

                Vitest
                    .expect(
                        ProviderComposition.rootsEqual CaseSensitive "/home/carol/arcs/demo" "/home/carol/arcs/demo"
                    )
                    .toBe
                    true
        )

        Vitest.test (
            "locations map to the provider that understands them",
            fun () ->
                let ok location =
                    match ProviderComposition.tryCreateLocation location (Some "Demo") with
                    | Ok created -> ProviderId.value created.ProviderId, created.ProviderLocation
                    | Error message -> failwith message

                Vitest
                    .expect(ok " https://git.nfdi4plants.org/carol/demo.git ")
                    .toEqual ("git", "https://git.nfdi4plants.org/carol/demo.git")

                Vitest.expect(ok "ssh://git@git.nfdi4plants.org/carol/demo.git" |> fst).toBe "git"
                Vitest.expect(ok "lakefs://repo/main" |> fst).toBe "lakefs"

                for refused in
                    [
                        ""
                        "http://insecure.example/repo.git"
                        "file:///tmp/repo"
                        "git@host:repo.git"
                    ] do
                    Vitest.expect((ProviderComposition.tryCreateLocation refused None).IsOk).toBe false
        )

        Vitest.test (
            "stale lock paths exist only for a plain git directory",
            fun () -> promise {
                let! root = createTempDirectoryAsync "arcitisa-vc-locks-"

                try
                    let plainRepo = Main.Bindings.Path.join [| root; "plain" |]
                    let worktree = Main.Bindings.Path.join [| root; "worktree" |]

                    let mkdir (path: string) =
                        Main.Bindings.Filesystem.mkdirSync
                            path
                            (Main.Bindings.Filesystem.MkdirOptions(recursive = true))

                    mkdir (Main.Bindings.Path.join [| plainRepo; ".git" |])
                    mkdir worktree

                    Main.Bindings.Filesystem.writeFileSync
                        (Main.Bindings.Path.join [| worktree; ".git" |])
                        "gitdir: elsewhere"
                        Main.Bindings.Filesystem.TextEncoding.Utf8

                    let expectedLock = Main.Bindings.Path.join [| plainRepo; ".git"; "index.lock" |]
                    Vitest.expect(ProviderComposition.staleLockPaths gitProviderId plainRepo).toEqual [| expectedLock |]
                    Vitest.expect(ProviderComposition.staleLockPaths gitProviderId worktree).toEqual [||]
                    Vitest.expect(ProviderComposition.staleLockPaths lakeFsProviderId plainRepo).toEqual [||]
                    do! removeDirectoryAsync root
                with error ->
                    do! removeDirectoryAsync root
                    return raise error
            }
        )

        Vitest.test (
            "two providers claiming the same root make the vault ambiguous",
            fun () -> promise {
                let root = "C:/arcs/ambiguous"

                let providers = [|
                    fakeOwner "fake.one" (Some root)
                    fakeOwner "fake.two" (Some root)
                |]

                let! resolution =
                    ProviderComposition.resolveVault providers CaseInsensitive root
                    |> Async.StartAsPromise

                match resolution with
                | ProviderComposition.AmbiguousVault candidates ->
                    Vitest.expect(candidates |> Array.map ProviderId.value |> Array.sort).toEqual [|
                        "fake.one"
                        "fake.two"
                    |]
                | other -> failwith $"Expected an ambiguous vault, got {other}"
            }
        )

        Vitest.test (
            "a folder inside a repository of a provider is no repository",
            fun () -> promise {
                let providers = [| fakeOwner "fake.one" (Some "C:/arcs/parent") |]

                let! resolution =
                    ProviderComposition.resolveVault providers CaseInsensitive "C:/arcs/parent/child"
                    |> Async.StartAsPromise

                match resolution with
                | ProviderComposition.UnmanagedVault diagnostics -> Vitest.expect(diagnostics).toEqual [||]
                | other -> failwith $"Expected an unmanaged vault, got {other}"
            }
        )

        Vitest.test (
            "a failed probe reaches the caller as a diagnostic",
            fun () -> promise {
                let failure =
                    OperationFailure.create ProviderError "probe_test_failure" "The fake probe failed."

                let providers = [|
                    fakeProvider
                        (ProviderId.tryCreate "fake.one" |> Result.defaultWith failwith)
                        (fun _ -> async { return ProbeFailed failure })
                        (fun _ -> async { return () })
                        missingFunctions
                |]

                let! resolution =
                    ProviderComposition.resolveVault providers CaseInsensitive "C:/arcs/failed"
                    |> Async.StartAsPromise

                match resolution with
                | ProviderComposition.UnmanagedVault diagnostics ->
                    Vitest.expect(diagnostics |> Array.map _.Code).toEqual [| "probe_test_failure" |]
                | other -> failwith $"Expected an unmanaged vault, got {other}"
            }
        )

        Vitest.test (
            "a git repository is owned by the git provider and a plain folder is unmanaged",
            fun () -> promise {
                let! root = createTempDirectoryAsync "arcitisa-vc-composition-"

                try
                    let repoRoot = Main.Bindings.Path.join [| root; "repo" |]
                    let plainRoot = Main.Bindings.Path.join [| root; "plain" |]

                    Main.Bindings.Filesystem.mkdirSync
                        plainRoot
                        (Main.Bindings.Filesystem.MkdirOptions(recursive = true))

                    let providers = providers ()

                    let gitProvider =
                        ProviderComposition.tryFindProvider providers gitProviderId
                        |> Option.defaultWith (fun () -> failwith "git provider missing")

                    let! initialized =
                        ProviderComposition.callFunction
                            gitProvider.Functions.Initialize
                            "Initialize"
                            (fun initialize ->
                                initialize
                                    {
                                        TargetPath = repoRoot
                                        Location = None
                                    }
                                    (OperationContext.detached "composition-init")
                            )
                        |> Async.StartAsPromise

                    match initialized with
                    | Succeeded outcome -> do! gitProvider.Provider.Close outcome.Value |> Async.StartAsPromise
                    | PartiallySucceeded(_, failure)
                    | Failed failure -> failwith $"Initialize failed ({failure.Code}): {failure.Message}"

                    let! repoResolution =
                        ProviderComposition.resolveVault providers CaseInsensitive repoRoot
                        |> Async.StartAsPromise

                    match repoResolution with
                    | ProviderComposition.OwnedVault provider -> Vitest.expect(ProviderId.value provider.Id).toBe "git"
                    | other -> failwith $"Expected an owned vault, got {other}"

                    let! plainResolution =
                        ProviderComposition.resolveVault providers CaseInsensitive plainRoot
                        |> Async.StartAsPromise

                    match plainResolution with
                    | ProviderComposition.UnmanagedVault _ -> ()
                    | other -> failwith $"Expected an unmanaged vault, got {other}"

                    do! removeDirectoryAsync root
                with error ->
                    do! removeDirectoryAsync root
                    return raise error
            }
        )
)
