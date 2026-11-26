/// The one place in ARCitISA that knows which version control providers exist. It builds
/// the Git provider over the DataHUB account store and the lakeFS provider over an
/// application-owned state root, and subscribes to everything each of them has. It also finds
/// the provider that owns a vault folder. Everything above this module works with the
/// neutral interface only.
module Main.VersionControl.ProviderComposition

open Main.Bindings.Path
open Fable.Core
open Arcitisa.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions

module GitProvider = VersionControlService.Git.GitProvider
module GitWorkspaceSession = VersionControlService.Git.GitWorkspaceSession
module LakeFsCredentials = VersionControlService.LakeFs.LakeFsCredentials
module LakeFsProvider = VersionControlService.LakeFs.LakeFsProvider
module LakeFsProviderOptions = VersionControlService.LakeFs.LakeFsProviderOptions

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

/// Whether two vault roots name the same folder, ignoring the spelling of the separators.
let rootsEqual (sensitivity: PathCaseSensitivity) (left: string) (right: string) =
    let comparison =
        match sensitivity with
        | CaseSensitive -> System.StringComparison.Ordinal
        | CaseInsensitive -> System.StringComparison.OrdinalIgnoreCase

    System.String.Equals(ProviderResolver.normalizePath left, ProviderResolver.normalizePath right, comparison)

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

/// One provider of the app with what the app subscribed to on it.
type AppProvider = {
    Id: ProviderId
    /// For Probe and Close, which are not entries of the list.
    Provider: IVersionControl
    /// Everything the provider has, from Subscribe.all.
    Functions: ProviderFunctions
}

/// Subscribes to everything the provider has. The Git and lakeFS providers always allow it, so
/// an error stops the start of the app.
let createAppProvider (id: ProviderId) (provider: IVersionControl) : AppProvider =
    match Subscribe.all provider with
    | Ok functions -> {
        Id = id
        Provider = provider
        Functions = functions
      }
    | Error message -> failwith message

/// The provider with this id, if the app has one.
let tryFindProvider (providers: AppProvider[]) (providerId: ProviderId) : AppProvider option =
    providers |> Array.tryFind (fun provider -> provider.Id = providerId)

/// The provider that creates the repository of a new ARC. The dependency check and the install ask it too.
let defaultProvider (providers: AppProvider[]) : AppProvider =
    tryFindProvider providers defaultProviderId
    |> Option.defaultWith (fun () -> failwith "The default version control provider is not registered.")

/// The answer for a function the provider does not have.
let private serviceUnavailable (service: string) : OperationResult<'T> =
    Failed(
        OperationFailure.create
            Unsupported
            VersionControlCodes.ServiceUnavailable
            $"The workspace provider does not offer {service}."
    )

/// Calls an entry of the subscribed functions. An entry that is `Missing` gets the failure the
/// renderer knows for a function the provider does not have, with the service name in the message.
let callFunction
    (entry: Entry<'call>)
    (serviceName: string)
    (call: 'call -> Async<OperationResult<'T>>)
    : Async<OperationResult<'T>> =
    match entry with
    | Implemented implemented -> call implemented
    | Missing _ -> async { return serviceUnavailable serviceName }

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

/// The Git provider, whose opened repositories diff text in the shared worker pool. A repository
/// asks for the pool on each diff Open until the pool is set up, so a failed setup fails only that
/// Open with diff_worker_failed and the next Open tries the setup again. `windowOwnerOf` names the
/// window that started an operation, and the pool keeps each window's diff handles apart.
let createGitProvider
    (source: DataHubStrategies.DataHubAccountSource)
    (windowOwnerOf: OperationContext -> string)
    : AppProvider =
    let options: VersionControlService.Git.GitProviderOptions = {
        Hooks = GitWorkspaceSession.GitSessionHooks.none
        Credentials = DataHubStrategies.createCredentialStrategy source
        RevisionIdentity = DataHubStrategies.createIdentityStrategy source
        RevisionPolicy = dataHubRevisionPolicy
        TextDiff =
            Some {
                Pool = TextDiffWorkers.pool
                WindowOwnerOf = windowOwnerOf
            }
    }

    createAppProvider gitProviderId (GitProvider.create options)

let lakeFsOptions
    (settingsRoot: string)
    (sensitivity: PathCaseSensitivity)
    : LakeFsProviderOptions.LakeFsProviderOptions =
    {
        StateRoot = join [| settingsRoot; providerStateFolderName |]
        PathCaseSensitivity = sensitivity
        Hooks = LakeFsProviderOptions.LakeFsSessionHooks.none
        Credentials = LakeFsCredentials.unconfigured
    }

let createLakeFsProvider (options: LakeFsProviderOptions.LakeFsProviderOptions) : AppProvider =
    createAppProvider lakeFsProviderId (LakeFsProvider.create options)

/// The production providers: Git over the DataHUB accounts, lakeFS without any
/// configured connection until lakeFS accounts exist in ARCitISA. The caller passes the
/// path case sensitivity it also hands to the session host.
let createProductionProviders
    (sensitivity: PathCaseSensitivity)
    (windowOwnerOf: OperationContext -> string)
    : AppProvider[] =
    let settingsRoot = Main.SettingsStore.getSettingsRootPath ()

    [|
        createGitProvider dataHubAccountSource windowOwnerOf
        createLakeFsProvider (lakeFsOptions settingsRoot sensitivity)
    |]

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

/// What the providers answered about a vault folder.
type VaultResolution =
    /// Exactly one provider detects the folder as its repository root.
    | OwnedVault of AppProvider
    /// Several providers detect the folder as their repository root.
    | AmbiguousVault of ProviderId[]
    /// No provider detects the folder as its repository root. The diagnostics are the failures of
    /// the probes that did not answer.
    | UnmanagedVault of diagnostics: OperationFailure[]

/// Asks Probe of every provider about the folder. A probe that answers ProbeFailed or
/// Invalid adds a diagnostic and detects nothing. Only a detection whose root is the folder itself
/// counts, so a folder that lies inside another repository is not opened.
let resolveVault
    (providers: AppProvider[])
    (sensitivity: PathCaseSensitivity)
    (folder: string)
    : Async<VaultResolution> =
    async {
        let path = ProviderResolver.normalizePath folder
        let owners = ResizeArray<AppProvider>()
        let diagnostics = ResizeArray<OperationFailure>()

        for provider in providers do
            let providerName = ProviderId.value provider.Id

            let! probed = provider.Provider.Probe path

            match probed with
            | NotDetected -> ()
            | Detected(root, _, _) ->
                if rootsEqual sensitivity root path then
                    owners.Add provider
            | Invalid message ->
                diagnostics.Add(
                    OperationFailure.createRedacted
                        Validation
                        "probe_invalid"
                        $"Provider '{providerName}' reported an invalid workspace: {message}"
                )
            | ProbeFailed failure -> diagnostics.Add failure

        return
            match owners.Count with
            | 0 -> UnmanagedVault(diagnostics.ToArray())
            | 1 -> OwnedVault owners[0]
            | _ -> AmbiguousVault(owners |> Seq.map _.Id |> Seq.toArray)
    }
