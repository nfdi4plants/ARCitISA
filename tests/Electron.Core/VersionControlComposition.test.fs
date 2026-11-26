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
    WorkspaceRoot = "C:/arcs/demo"
    TargetHost = host
    ConnectionProfileId = profile
}

let private gitProviderId =
    ProviderId.tryCreate "git" |> Result.defaultWith failwith

let private lakeFsProviderId =
    ProviderId.tryCreate "lakefs" |> Result.defaultWith failwith

let private bindingFor (providerId: ProviderId) (root: string) : WorkspaceBinding = {
    SchemaVersion = WorkspaceBinding.CurrentSchemaVersion
    ProviderId = providerId
    WorkspaceRoot = root
    ProviderStateRef = Some "state-1"
    Location = {
        ProviderId = providerId
        DisplayName = Some "Demo"
        ProviderLocation = "https://git.nfdi4plants.org/carol/demo.git"
        ConnectionProfileId = None
    }
    ConnectionProfileId = Some "acc-gitlab"
}

let private memoryStoreWithState (sensitivity: PathCaseSensitivity) =
    let mutable content: string option = None

    let store =
        WorkspaceBindingStore.create
            sensitivity
            (fun () -> content)
            (fun next ->
                content <- Some next
                Ok()
            )

    let storedBindings () =
        content
        |> Option.map WorkspaceBindingStore.deserialize
        |> Option.defaultValue [||]

    store, storedBindings

let private memoryStore (sensitivity: PathCaseSensitivity) = memoryStoreWithState sensitivity |> fst

let private failingStore () =
    WorkspaceBindingStore.create CaseInsensitive (fun () -> None) (fun _ -> Error "disk full")

let private fakeFactory (id: string) (root: string option) : ProviderFactory =
    let providerId = ProviderId.tryCreate id |> Result.defaultWith failwith

    let unsupported () = async { return Failed(OperationFailure.create Unsupported "operation_not_supported" "fake") }

    {
        Id = providerId
        Probe =
            fun _ -> async {
                return
                    match root with
                    | Some detected -> Detected(detected, 50, None)
                    | None -> NotDetected
            }
        VerifyLocation = fun _ _ -> unsupported ()
        Initialize = fun _ _ -> unsupported ()
        Clone = fun _ _ -> unsupported ()
        Adopt = fun _ _ -> unsupported ()
        Bind = fun _ _ -> unsupported ()
        Open = fun _ _ -> unsupported ()
        CheckDependencies = fun _ -> async { return OperationResult.succeeded [||] }
        InstallDependency = fun _ _ -> unsupported ()
    }

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
    "Workspace binding store",
    fun () ->
        Vitest.test (
            "stores the binding verbatim and finds it under a differently cased root",
            fun () ->
                let store = memoryStore CaseInsensitive
                let binding = bindingFor gitProviderId "C:\\Users\\Carol\\ARCs\\Demo"

                match store.Save binding with
                | Ok() -> ()
                | Error message -> failwith message

                let found = store.TryFind "c:/users/carol/arcs/demo/"
                Vitest.expect(found).toEqual (Some binding)
                Vitest.expect(found |> Option.map _.WorkspaceRoot).toEqual (Some "C:\\Users\\Carol\\ARCs\\Demo")
        )

        Vitest.test (
            "case sensitive stores do not match a differently cased root",
            fun () ->
                let store = memoryStore CaseSensitive
                store.Save(bindingFor gitProviderId "/home/carol/arcs/demo") |> ignore

                Vitest.expect(store.TryFind "/home/carol/arcs/Demo").toEqual None

                Vitest
                    .expect(store.TryFind "/home/carol/arcs/demo")
                    .toEqual (Some(bindingFor gitProviderId "/home/carol/arcs/demo"))
        )

        Vitest.test (
            "saving a binding for the same root replaces the previous one and remove drops it",
            fun () ->
                let store, storedBindings = memoryStoreWithState CaseInsensitive
                store.Save(bindingFor gitProviderId "C:/arcs/demo") |> ignore
                store.Save(bindingFor lakeFsProviderId "c:/ARCS/demo") |> ignore

                Vitest.expect(storedBindings().Length).toBe 1
                Vitest.expect(store.TryFind "C:/arcs/demo" |> Option.map _.ProviderId).toEqual (Some lakeFsProviderId)

                Vitest.expect(store.Remove "C:/arcs/demo").toEqual (Ok())
                Vitest.expect(storedBindings().Length).toBe 0
        )

        Vitest.test (
            "the serialized form round trips every field",
            fun () ->
                let binding = bindingFor lakeFsProviderId "C:/arcs/demo"

                let decoded =
                    WorkspaceBindingStore.deserialize (WorkspaceBindingStore.serialize [| binding |])

                Vitest.expect(decoded).toEqual [| binding |]
        )

        Vitest.test (
            "unreadable content yields an empty store instead of an exception",
            fun () -> Vitest.expect(WorkspaceBindingStore.deserialize "{ not json").toEqual [||]
        )

        Vitest.test (
            "a write failure is reported by Save, and Remove does not write when nothing matched",
            fun () ->
                let store = failingStore ()
                Vitest.expect(store.Save(bindingFor gitProviderId "C:/arcs/demo")).toEqual (Error "disk full")
                Vitest.expect(store.Remove "C:/arcs/demo").toEqual (Ok())
        )

        Vitest.test (
            "an entry from a newer schema version is skipped",
            fun () ->
                let binding = bindingFor gitProviderId "C:/arcs/demo"
                let serialized = WorkspaceBindingStore.serialize [| binding |]
                let newer = serialized.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")
                Vitest.expect(WorkspaceBindingStore.deserialize newer).toEqual [||]
        )

        Vitest.test (
            "a damaged entry is skipped and the others survive",
            fun () ->
                let binding = bindingFor gitProviderId "C:/arcs/demo"
                let serialized = WorkspaceBindingStore.serialize [| binding |]

                let damaged =
                    serialized.Replace("\"bindings\": [", "\"bindings\": [ { \"schemaVersion\": 1 },")

                Vitest.expect(WorkspaceBindingStore.deserialize damaged).toEqual [| binding |]
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
        let catalog () =
            ProviderComposition.createCatalog [
                ProviderComposition.createGitFactory (source AuthStateDto.Empty []) WorkspaceSessionHost.windowOwnerOf
                ProviderComposition.createLakeFsFactory
                    (ProviderComposition.lakeFsOptions "C:/settings" CaseInsensitive)
                    VersionControlService.LakeFs.LakeFsCredentials.unconfigured
            ]

        Vitest.test (
            "the catalog registers exactly the git and lakefs providers",
            fun () ->
                let ids =
                    ProviderResolver.factories (catalog ())
                    |> Array.map (fun factory -> ProviderId.value factory.Id)
                    |> Array.sort

                Vitest.expect(ids).toEqual [| "git"; "lakefs" |]
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
            "the runtime catalog serves its registered providers",
            fun () ->
                let runtime: VersionControlRuntime.VersionControlRuntime = {
                    Catalog = catalog ()
                    Bindings = memoryStore CaseInsensitive
                    PathCaseSensitivity = CaseInsensitive
                    SessionDefaults = Main.UserDefaults.builtInSessionDefaults
                }

                Vitest.expect(ProviderResolver.factories runtime.Catalog |> Array.length).toBe 2
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

                let providers =
                    ProviderComposition.createCatalog [
                        fakeFactory "fake.one" (Some root)
                        fakeFactory "fake.two" (Some root)
                    ]

                let! resolution =
                    ProviderComposition.resolveVault providers (memoryStore CaseInsensitive) CaseInsensitive root
                    |> Async.StartAsPromise

                match resolution with
                | ProviderComposition.AmbiguousVault candidates ->
                    Vitest
                        .expect(candidates |> Array.map (fun c -> ProviderId.value c.ProviderId) |> Array.sort)
                        .toEqual
                        [| "fake.one"; "fake.two" |]
                | other -> failwith $"Expected an ambiguous vault, got {other}"
            }
        )

        Vitest.test (
            "a persisted binding selects its factory without probing",
            fun () -> promise {
                let store = memoryStore CaseInsensitive
                store.Save(bindingFor lakeFsProviderId "C:/arcs/not-on-disk") |> ignore

                let! resolution =
                    ProviderComposition.resolveVault (catalog ()) store CaseInsensitive "c:/ARCS/not-on-disk"
                    |> Async.StartAsPromise

                match resolution with
                | ProviderComposition.BoundVault(binding, factory) ->
                    Vitest.expect(ProviderId.value factory.Id).toBe "lakefs"
                    Vitest.expect(binding.WorkspaceRoot).toBe "C:/arcs/not-on-disk"
                | other -> failwith $"Expected a bound vault, got {other}"
            }
        )

        Vitest.test (
            "an unbound git repository is adoptable and a plain folder is unmanaged",
            fun () -> promise {
                let! root = createTempDirectoryAsync "arcitisa-vc-composition-"

                try
                    let repoRoot = Main.Bindings.Path.join [| root; "repo" |]
                    let plainRoot = Main.Bindings.Path.join [| root; "plain" |]

                    Main.Bindings.Filesystem.mkdirSync
                        plainRoot
                        (Main.Bindings.Filesystem.MkdirOptions(recursive = true))

                    let store = memoryStore CaseInsensitive
                    let providers = catalog ()

                    let gitFactory =
                        ProviderResolver.tryGetFactory providers gitProviderId
                        |> Option.defaultWith (fun () -> failwith "git factory missing")

                    let! initialized =
                        gitFactory.Initialize
                            {
                                TargetPath = repoRoot
                                Location = None
                            }
                            (OperationContext.detached "composition-init")
                        |> Async.StartAsPromise

                    match initialized with
                    | Succeeded _ -> ()
                    | PartiallySucceeded(_, failure)
                    | Failed failure -> failwith $"Initialize failed ({failure.Code}): {failure.Message}"

                    let! repoResolution =
                        ProviderComposition.resolveVault providers store CaseInsensitive repoRoot
                        |> Async.StartAsPromise

                    match repoResolution with
                    | ProviderComposition.AdoptableVault(factory, _) ->
                        Vitest.expect(ProviderId.value factory.Id).toBe "git"
                    | other -> failwith $"Expected an adoptable vault, got {other}"

                    let! plainResolution =
                        ProviderComposition.resolveVault providers store CaseInsensitive plainRoot
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
