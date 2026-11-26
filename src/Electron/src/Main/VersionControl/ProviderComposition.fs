/// The one place in ARCitISA that knows which version control providers exist. It builds
/// the Git factory over the DataHUB account store and the lakeFS factory over an
/// application-owned state root, and it resolves the factory for a vault from the
/// persisted binding. Everything above this module works with the neutral SPI only.
module Main.VersionControl.ProviderComposition

open Main.Bindings.Path
open Fable.Core
open VersionControlService.Abstractions

module GitWorkspaceSession = VersionControlService.Git.GitWorkspaceSession
module LakeFsCredentials = VersionControlService.LakeFs.LakeFsCredentials
module LakeFsProviderOptions = VersionControlService.LakeFs.LakeFsProviderOptions
module LakeFsWorkspaceSession = VersionControlService.LakeFs.LakeFsWorkspaceSession

/// Folder below the application settings root that holds provider state which must
/// never live inside a vault (lakeFS indexes and transactions).
[<Literal>]
let providerStateFolderName = "VersionControlState"

/// Windows and macOS compare paths without regard to case, Linux does not.
let pathCaseSensitivityForPlatform (platform: string) : PathCaseSensitivity =
    match platform with
    | "win32"
    | "darwin" -> CaseInsensitive
    | _ -> CaseSensitive

let currentPathCaseSensitivity () =
    pathCaseSensitivityForPlatform (Main.Bindings.Node.processPlatform ())

let dataHubAccountSource: DataHubStrategies.DataHubAccountSource = {
    GetState = Main.Auth.AuthService.peekState
    TryGetTokenForAccount = Main.Auth.AuthService.tryGetTokenForAccount
    TryGetTokenForHost = Main.Auth.AuthService.tryGetTokenForHost
}

/// The DataHUB ruleset as the library's revision policy: metadata workbooks stay plain
/// content, dataset files and files above 25 MB are large objects, everything else
/// follows the automatic threshold. Metadata wins over the other two rules.
let dataHubRevisionPolicy: RevisionPolicyStrategy = {
    ResolvePathPolicy =
        fun request ->
            let path = RepositoryPath.value request.Path

            if Arcitisa.Components.Shared.GitLfsRules.isIsaMetadataFile path then
                RevisionPathPolicy.Inline
            elif
                Arcitisa.Components.Shared.GitLfsRules.isInDatasetFolder path
                || request.SizeInBytes > float Arcitisa.Components.Shared.GitLfsRules.maxNonLfsFileSizeBytes
            then
                RevisionPathPolicy.LargeObject
            else
                RevisionPathPolicy.Automatic
}

/// The Git factory whose sessions diff text in the shared worker pool. A session asks for the
/// pool on each diff Open until the pool is set up, so a failed setup fails only that Open
/// with diff_worker_failed and the next Open tries the setup again. `windowOwnerOf` names the
/// window that started an operation, and the pool keeps each window's diff handles apart.
let createGitFactory
    (source: DataHubStrategies.DataHubAccountSource)
    (windowOwnerOf: OperationContext -> string)
    : ProviderFactory =
    GitWorkspaceSession.createFactoryWithOptions
        {
            Hooks = GitWorkspaceSession.GitSessionHooks.none
            TextDiff =
                Some {
                    Pool = TextDiffWorkers.pool
                    WindowOwnerOf = windowOwnerOf
                }
        }
        (DataHubStrategies.createCredentialStrategy source)
        (DataHubStrategies.createIdentityStrategy source)
        dataHubRevisionPolicy

let lakeFsOptions
    (settingsRoot: string)
    (sensitivity: PathCaseSensitivity)
    : LakeFsProviderOptions.LakeFsProviderOptions =
    {
        StateRoot = join [| settingsRoot; providerStateFolderName |]
        PathCaseSensitivity = sensitivity
    }

let createLakeFsFactory
    (options: LakeFsProviderOptions.LakeFsProviderOptions)
    (credentials: LakeFsCredentials.LakeFsCredentialStrategy)
    : ProviderFactory =
    LakeFsWorkspaceSession.createFactoryWithPolicy options credentials dataHubRevisionPolicy

let createCatalog (factories: ProviderFactory list) : ProviderResolver.ProviderCatalog =
    match ProviderResolver.tryCreateCatalog factories with
    | Ok catalog -> catalog
    | Error message -> failwith message

/// The production catalog: Git over the DataHUB accounts, lakeFS without any
/// configured connection until lakeFS accounts exist in ARCitISA. The caller passes the
/// path case sensitivity it also hands to the resolver and the binding store.
let createProductionCatalog
    (sensitivity: PathCaseSensitivity)
    (windowOwnerOf: OperationContext -> string)
    : ProviderResolver.ProviderCatalog =
    let settingsRoot = Main.SettingsStore.getSettingsRootPath ()

    createCatalog [
        createGitFactory dataHubAccountSource windowOwnerOf
        createLakeFsFactory (lakeFsOptions settingsRoot sensitivity) LakeFsCredentials.unconfigured
    ]

let gitProviderId =
    match ProviderId.tryCreate WellKnownProviderIds.Git with
    | Ok providerId -> providerId
    | Error message -> failwith message

let lakeFsProviderId =
    match ProviderId.tryCreate WellKnownProviderIds.LakeFs with
    | Ok providerId -> providerId
    | Error message -> failwith message

/// The provider new vaults are initialized with. ARCitISA only provisions Git today.
let defaultProviderId = gitProviderId

/// Turns the location text the renderer sends into a repository location for the
/// provider that understands it. Git takes https and ssh URLs, lakeFS takes its own
/// scheme. Anything else is refused here, before any provider runs.
let tryCreateLocation (providerLocation: string) (displayName: string option) : Result<RepositoryLocation, string> =
    let trimmed = (providerLocation |> Option.ofObj |> Option.defaultValue "").Trim()

    let location providerId = {
        ProviderId = providerId
        DisplayName = displayName
        ProviderLocation = trimmed
        ConnectionProfileId = None
    }

    if trimmed.Length = 0 then
        Error "The repository location is empty."
    elif
        trimmed.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("ssh://", System.StringComparison.OrdinalIgnoreCase)
    then
        Ok(location gitProviderId)
    elif trimmed.StartsWith("lakefs://", System.StringComparison.OrdinalIgnoreCase) then
        Ok(location lakeFsProviderId)
    else
        Error "The repository location must be an https://, ssh:// or lakefs:// address."

[<Literal>]
let private staleLockMinimumAgeSeconds = 10.0

[<Emit("Date.now()")>]
let private currentTimeMilliseconds () : float = jsNative

let private indexLockPaths (providerId: ProviderId) (workspaceRoot: string) : string[] =
    let gitDirectory = join [| workspaceRoot; ".git" |]

    let isPlainGitDirectory () =
        VersionControlService.Runtime.Node.FileSystem.tryLstatSync gitDirectory
        |> Option.exists (fun stats -> stats.isDirectory ())

    if providerId = gitProviderId && isPlainGitDirectory () then
        [| join [| gitDirectory; "index.lock" |] |]
    else
        [||]

let private indexLockAgeSeconds (path: string) =
    VersionControlService.Runtime.Node.FileSystem.tryLstatSync path
    |> Option.map (fun stats ->
        let ageSeconds = (currentTimeMilliseconds () - stats.mtimeMs) / 1000.0

        if ageSeconds < 0.0 then
            staleLockMinimumAgeSeconds
        else
            ageSeconds
    )

let indexLockWaitMilliseconds (path: string) =
    indexLockAgeSeconds path
    |> Option.map (fun ageSeconds ->
        System.Math.Ceiling(max 0.0 (staleLockMinimumAgeSeconds - ageSeconds) * 1000.0)
        |> int
    )
    |> Option.defaultValue 0

/// Lock paths old enough for cleanup in a plain Git repository. Any Git process can
/// leave an index lock behind, including a killed ARCitISA process or another tool. ARCitISA
/// removes a lock only after its workspace operations are idle and it is old enough. The
/// clear operation waits out a young lock. Linked worktrees and submodules store `.git`
/// as a file, so this returns no path for them.
let staleLockPaths (providerId: ProviderId) (workspaceRoot: string) : string[] =
    indexLockPaths providerId workspaceRoot
    |> Array.filter (fun path ->
        indexLockAgeSeconds path
        |> Option.forall (fun ageSeconds -> ageSeconds >= staleLockMinimumAgeSeconds)
    )

let recentLockPaths (providerId: ProviderId) (workspaceRoot: string) : string[] =
    indexLockPaths providerId workspaceRoot
    |> Array.filter (fun path ->
        indexLockAgeSeconds path
        |> Option.exists (fun ageSeconds -> ageSeconds < staleLockMinimumAgeSeconds)
    )

/// Resolution outcome for one vault root, as the session host consumes it.
type VaultResolution =
    /// A persisted binding selected its factory.
    | BoundVault of WorkspaceBinding * ProviderFactory
    /// No binding is stored, but exactly one provider owns the root and can adopt it.
    | AdoptableVault of ProviderFactory * ProviderResolver.DetectionCandidate
    /// Several providers claim the root. The host has to bind explicitly.
    | AmbiguousVault of ProviderResolver.DetectionCandidate[]
    /// No provider owns the root.
    | UnmanagedVault of diagnostics: OperationFailure[]

let resolveVault
    (catalog: ProviderResolver.ProviderCatalog)
    (bindings: WorkspaceBindingStore.IWorkspaceBindingStore)
    (sensitivity: PathCaseSensitivity)
    (workspaceRoot: string)
    : Async<VaultResolution> =
    async {
        let! report =
            ProviderResolver.resolve catalog {
                WorkspacePath = workspaceRoot
                ExplicitBinding = bindings.TryFind workspaceRoot
                PathCaseSensitivity = sensitivity
            }

        return
            match report.Resolution with
            | ProviderResolver.Bound(binding, factory) -> BoundVault(binding, factory)
            | ProviderResolver.ProbedWorkspace(factory, candidate) -> AdoptableVault(factory, candidate)
            | ProviderResolver.AmbiguousWorkspace candidates -> AmbiguousVault candidates
            | ProviderResolver.UnmanagedWorkspace -> UnmanagedVault report.Diagnostics
    }
