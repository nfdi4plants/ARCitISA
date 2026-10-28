module VersionControlLakeFsTests

open Fable.Core
open Fable.Core.JsInterop
open Fable.Electron
open Main
open Main.Bindings.Path
open Main.VersionControl
open Swate.Components.Composite.Authentication.Types
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest
open ElectronCore.TestHelpers

module LakeFsApi = VersionControlService.LakeFs.LakeFsApi
module LakeFsCredentials = VersionControlService.LakeFs.LakeFsCredentials
module LakeFsTypes = VersionControlService.LakeFs.LakeFsTypes
module LakeFsWorkspaceSession = VersionControlService.LakeFs.LakeFsWorkspaceSession
module RuntimeNodeInterop = VersionControlService.Runtime.Node.Interop

[<Emit("process.env[$0] ?? null")>]
let private getEnvironmentVariable (_name: string) : string = jsNative

[<Emit("Promise.race($0)")>]
let private promiseRace (_promises: JS.Promise<'T>[]) : JS.Promise<'T> = jsNative

let private integrationEnabled () =
    getEnvironmentVariable "LAKEFS_INTEGRATION" = "1"

if not (integrationEnabled ()) then
    printfn "lakeFS integration skipped: set LAKEFS_INTEGRATION=1"

let private lakeFsConnection () : LakeFsTypes.LakeFsConnection = {
    Endpoint =
        getEnvironmentVariable "LAKEFS_INTEGRATION_ENDPOINT"
        |> Option.ofObj
        |> Option.defaultValue "http://127.0.0.1:8000"
    AccessKeyId =
        getEnvironmentVariable "LAKEFS_INTEGRATION_ACCESS_KEY_ID"
        |> Option.ofObj
        |> Option.defaultValue "integration-access"
    SecretAccessKey =
        getEnvironmentVariable "LAKEFS_INTEGRATION_SECRET_ACCESS_KEY"
        |> Option.ofObj
        |> Option.defaultValue "integration-secret"
}

let private createRuntimeWithFactory
    (bindings: WorkspaceBindingStore.IWorkspaceBindingStore)
    (lakeFsFactory: ProviderFactory)
    : VersionControlRuntime.VersionControlRuntime =
    {
        Catalog =
            ProviderComposition.createCatalog [
                ProviderComposition.createGitFactory noAccounts WorkspaceSessionHost.windowOwnerOf
                lakeFsFactory
            ]
        Bindings = bindings
        PathCaseSensitivity = CaseInsensitive
        SessionDefaults = UserDefaults.builtInSessionDefaults
    }

let private createRuntimeWithHooks
    (settingsRoot: string)
    (connection: LakeFsTypes.LakeFsConnection)
    (bindings: WorkspaceBindingStore.IWorkspaceBindingStore)
    (hooks: LakeFsWorkspaceSession.LakeFsSessionHooks)
    : VersionControlRuntime.VersionControlRuntime =
    createRuntimeWithFactory
        bindings
        // The hooks constructor is the one place this file bypasses ProviderComposition.
        // The composition root exposes no hooks overload for this fixture.
        (LakeFsWorkspaceSession.createFactoryWithHooksAndPolicy
            (ProviderComposition.lakeFsOptions settingsRoot CaseInsensitive)
            hooks
            (LakeFsCredentials.fixedConnection connection)
            ProviderComposition.dataHubRevisionPolicy)

let private expectDtoSucceeded (operation: string) (result: Result<OperationResultDto<'T>, exn>) =
    match result with
    | Ok(OperationResultDto.Succeeded outcome) -> outcome
    | Ok(OperationResultDto.PartiallySucceeded(_, failure)) ->
        failwith $"{operation} partially succeeded ({failure.Code}): {failure.Message}"
    | Ok(OperationResultDto.Failed failure) -> failwith $"{operation} failed ({failure.Code}): {failure.Message}"
    | Error error -> failwith $"{operation} threw: {error.Message}"

let private expectDtoFailureOrPartial (operation: string) (result: Result<OperationResultDto<'T>, exn>) =
    match result with
    | Ok(OperationResultDto.Failed failure)
    | Ok(OperationResultDto.PartiallySucceeded(_, failure)) -> failure
    | Ok _ -> failwith $"{operation} unexpectedly succeeded."
    | Error error -> failwith $"{operation} threw: {error.Message}"

let private expectLakeFs (operation: string) (result: Result<'T, OperationFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failwith $"{operation} failed ({failure.Code}): {failure.Message}"

let private expectServiceUnavailable operation result =
    let failure = expectDtoFailure operation result
    Vitest.expect(failure.Category).toEqual FailureCategoryDto.Unsupported
    Vitest.expect(failure.Code).toBe VersionControlCodes.ServiceUnavailable

type private Fixture = {
    Root: string
    Workspace: string
    Scratch: string
    Settings: string
    Repository: string
    Bindings: WorkspaceBindingStore.IWorkspaceBindingStore
    Runtime: VersionControlRuntime.VersionControlRuntime
    Host: WorkspaceSessionHost.WorkspaceSessionHost
}

let private createRepository (connection: LakeFsTypes.LakeFsConnection) (repository: string) = promise {
    let payload =
        JS.JSON.stringify (
            createObj [
                "name" ==> repository
                "storage_namespace" ==> $"local://{repository}"
                "default_branch" ==> "main"
            ]
        )

    let! result =
        LakeFsApi.requestChecked
            connection
            "POST"
            "/repositories"
            (Some("application/json", payload))
            (OperationContext.detached $"create-repository-{repository}")
        |> Async.StartAsPromise

    expectLakeFs "create repository" result |> ignore
    // The generated name identifies the repository for post-mortem inspection on the container.
    printfn "lakeFS repository: %s" repository
}

let private createFixtureWithRuntime
    (makeRuntime:
        string
            -> LakeFsTypes.LakeFsConnection
            -> WorkspaceBindingStore.IWorkspaceBindingStore
            -> VersionControlRuntime.VersionControlRuntime)
    =
    promise {
        let! root = createTempDirectoryAsync "swate-vc-lakefs-"
        let workspace = join [| root; "workspace" |]
        let scratch = join [| root; "scratch" |]
        let settings = join [| root; "settings" |]
        let connection = lakeFsConnection ()

        for folder in [ workspace; scratch; settings ] do
            Main.Bindings.Filesystem.mkdirSync folder (Main.Bindings.Filesystem.MkdirOptions(recursive = true))

        let bindings = memoryBindings ()
        let runtime = makeRuntime settings connection bindings
        let host = WorkspaceSessionHost.WorkspaceSessionHost(runtime)

        let repositoryToken =
            RuntimeNodeInterop.randomUuid().Replace("-", "").ToLowerInvariant()

        let repository = $"swate-{repositoryToken}"

        try
            do! createRepository connection repository

            WorkspaceSessionHost.initialize host

            return {
                Root = root
                Workspace = workspace
                Scratch = scratch
                Settings = settings
                Repository = repository
                Bindings = bindings
                Runtime = runtime
                Host = host
            }
        with error ->
            do! removeDirectoryAsync root
            return raise error
    }

let private createFixture () =
    createFixtureWithRuntime (fun settings connection bindings ->
        ElectronCore.TestHelpers.createRuntime settings (LakeFsCredentials.fixedConnection connection) bindings
    )

let private createHookFixture hooks =
    createFixtureWithRuntime (fun settings connection bindings ->
        createRuntimeWithHooks settings connection bindings hooks
    )

let private deleteRepository (fixture: Fixture) = promise {
    let! result =
        LakeFsApi.requestChecked
            (lakeFsConnection ())
            "DELETE"
            $"/repositories/{fixture.Repository}"
            None
            (detached $"delete-repository-{fixture.Repository}")
        |> Async.StartAsPromise

    match result with
    | Ok _ -> ()
    | Error _ -> ()
}

let private cleanupFixture (fixture: Fixture) = promise {
    do! fixture.Host.CloseAll() |> Async.StartAsPromise

    try
        do! deleteRepository fixture
    with _ ->
        ()

    do! removeDirectoryAsync fixture.Root
}

let private withFixtureFrom (create: unit -> JS.Promise<Fixture>) (body: Fixture -> JS.Promise<unit>) = promise {
    let! fixture = create ()
    let mutable cleanedUp = false

    let cleanup () = promise {
        if not cleanedUp then
            cleanedUp <- true
            do! cleanupFixture fixture
    }

    try
        do! body fixture
        do! cleanup ()
    with error ->
        // Preserve the operation failure if cleanup itself encounters a transient
        // filesystem or lakeFS error. The cleanup is still attempted exactly once.
        try
            do! cleanup ()
        with _ ->
            ()

        return raise error
}

let private withFixture body = withFixtureFrom createFixture body

let private withHookFixture hooks body =
    withFixtureFrom (fun () -> createHookFixture hooks) body

let private bindWorkspaceViaClone
    (fixture: Fixture)
    (windowId: int)
    (operationId: string)
    : JS.Promise<IVersionControlApi> =
    promise {
        registerVault windowId fixture.Workspace |> ignore
        let api = Main.IPC.IVersionControlApi.api (ipcEvent windowId)

        let! cloned =
            api.cloneWorkspace {
                OperationId = operationId
                ProviderLocation = $"lakefs://{fixture.Repository}/main"
                DisplayName = Some fixture.Repository
                TargetPath = fixture.Workspace
                TargetRef = None
                MaterializeAllObjects = false
            }

        expectDtoSucceeded "clone lakeFS workspace" cloned |> ignore
        return api
    }

let private uploadTextObject
    (fixture: Fixture)
    (connection: LakeFsTypes.LakeFsConnection)
    (branch: string)
    (path: string)
    (content: string)
    (operationId: string)
    =
    promise {
        let sourcePath =
            join [|
                fixture.Scratch
                $"lakefs-upload-{RuntimeNodeInterop.randomUuid ()}.txt"
            |]

        writeText sourcePath content

        let! uploaded =
            LakeFsApi.uploadObjectFromFile connection fixture.Repository branch path sourcePath (detached operationId)
            |> Async.StartAsPromise

        expectLakeFs $"upload {path}" uploaded |> ignore
    }

let private commitServer
    (fixture: Fixture)
    (connection: LakeFsTypes.LakeFsConnection)
    (branch: string)
    (message: string)
    (operationId: string)
    =
    promise {
        let! committed =
            LakeFsApi.commit connection fixture.Repository branch message (detached operationId)
            |> Async.StartAsPromise

        expectLakeFs "commit server changes" committed |> ignore
    }

let private serverObjects
    (fixture: Fixture)
    (connection: LakeFsTypes.LakeFsConnection)
    (reference: string)
    (operationId: string)
    =
    promise {
        let! listed =
            LakeFsApi.listObjects connection fixture.Repository reference "" (detached operationId)
            |> Async.StartAsPromise

        return expectLakeFs "list server objects" listed
    }

let private downloadServerText
    (fixture: Fixture)
    (connection: LakeFsTypes.LakeFsConnection)
    (reference: string)
    (path: string)
    (operationId: string)
    =
    promise {
        let targetPath =
            join [|
                fixture.Scratch
                $"lakefs-download-{RuntimeNodeInterop.randomUuid ()}.txt"
            |]

        let! downloaded =
            LakeFsApi.downloadObjectToFile
                connection
                fixture.Repository
                reference
                path
                targetPath
                (detached operationId)
            |> Async.StartAsPromise

        expectLakeFs $"download {path}" downloaded |> ignore
        return Main.Bindings.Filesystem.readFileSync targetPath Main.Bindings.Filesystem.TextEncoding.Utf8
    }

let private commitAndPublish
    (fixture: Fixture)
    (api: IVersionControlApi)
    (path: string)
    (content: string)
    (operationPrefix: string)
    =
    promise {
        writeText (join [| fixture.Workspace; path |]) content

        let! statusResult = api.getStatus (request $"{operationPrefix}-status")
        let status = (expectDtoValue "status before revision" statusResult).Value

        let! created =
            api.createRevision {
                OperationId = $"{operationPrefix}-revision"
                Message = $"Add {path}"
                Paths = [| path |]
                ExpectedWorkspaceVersion = status.WorkspaceVersion
            }

        // A local revision can carry recoverable warnings while still supplying the revision for publish.
        expectDtoValue "create local revision" created |> ignore

        let! afterCommit = api.getStatus (request $"{operationPrefix}-status-after")
        let afterCommitStatus = (expectDtoValue "status after revision" afterCommit).Value

        let! published =
            api.synchronize {
                OperationId = $"{operationPrefix}-synchronize"
                ExpectedWorkspaceVersion = afterCommitStatus.WorkspaceVersion
                ExpectedTargetRevision = None
                AcceptUpdateRisks = false
                PublishLocalRevisions = true
            }

        return (expectDtoSucceeded "synchronize local revision" published).Value
    }

Vitest.describe (
    "Workspace session host",
    fun () ->
        Vitest.afterEach (fun () ->
            electronMock?reset () |> ignore
            ARC_VAULTS.Vaults.Clear()
        )

        Vitest.test (
            "initializes a lakeFS workspace, persists the binding and reopens the same session",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! _ = bindWorkspaceViaClone fixture 61 "lakefs-clone"

                        let persisted =
                            fixture.Runtime.Bindings.TryFind fixture.Workspace
                            |> Option.defaultWith (fun () -> failwith "Binding was not persisted.")

                        Vitest.expect(ProviderId.value persisted.ProviderId).toBe "lakefs"

                        let! first =
                            fixture.Host.OpenSession(fixture.Workspace, detached "lakefs-open-1")
                            |> Async.StartAsPromise

                        let hosted = expectValue "first lakeFS open" first
                        Vitest.expect(ProviderId.value hosted.Session.Descriptor.ProviderId).toBe "lakefs"

                        let! second =
                            fixture.Host.OpenSession(fixture.Workspace, detached "lakefs-open-2")
                            |> Async.StartAsPromise

                        Vitest.expect((expectValue "second lakeFS open" second).SessionId).toBe hosted.SessionId
                    })
            }
        )
)

Vitest.describe (
    "Version control IPC over a lakeFS vault",
    fun () ->
        Vitest.afterEach (fun () ->
            electronMock?reset () |> ignore
            ARC_VAULTS.Vaults.Clear()
        )

        Vitest.test (
            "advertises synchronization and conflict resolution only",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! api = bindWorkspaceViaClone fixture 62 "lakefs-services-clone"
                        let! info = api.getSessionInfo (request "lakefs-services-info")
                        let session = (expectDtoValue "lakeFS session info" info).Value
                        let services = session.Services

                        Vitest.expect(session.ProviderId).toBe "lakefs"

                        Vitest
                            .expect(session.WorkspaceRoot.Replace('\\', '/'))
                            .toBe (fixture.Workspace.Replace('\\', '/'))

                        Vitest.expect(services.Synchronization).toBe true
                        Vitest.expect(services.ConflictResolution).toBe true
                        Vitest.expect(services.TextDiff).toBe false
                        Vitest.expect(services.ObjectMaterialization).toBe false
                        Vitest.expect(services.StoragePolicy).toBe false
                        Vitest.expect(services.Maintenance).toBe false
                        Vitest.expect(services.RepositoryBrowser).toBe false

                        let! objects = api.listObjects (request "lakefs-list-objects")

                        let! materialize =
                            api.materializeObject {
                                OperationId = "lakefs-materialize"
                                Path = "data.txt"
                                RefreshTree = None
                            }

                        let! dematerialize =
                            api.dematerializeObject {
                                OperationId = "lakefs-dematerialize"
                                Path = "data.txt"
                                RefreshTree = None
                            }

                        let! getPolicy = api.getStoragePolicySettings (request "lakefs-get-policy")

                        let! setPolicy =
                            api.setStoragePolicySettings {
                                OperationId = "lakefs-set-policy"
                                Settings = {
                                    AutoPolicyThresholdMb = Some 2
                                    MaterializeLargeObjects = false
                                    DiffIndexingLimitMb = None
                                }
                            }

                        let! setPathPolicy =
                            api.setPathStoragePolicy {
                                OperationId = "lakefs-set-path-policy"
                                Path = "data.txt"
                                UseLargeObjectStorage = false
                            }

                        let! prune = api.pruneStorage (request "lakefs-prune")
                        let! deduplicate = api.deduplicateStorage (request "lakefs-deduplicate")
                        let! repositoryUrl = api.getRepositoryWebUrl (request "lakefs-repository-url")

                        expectServiceUnavailable "listObjects" objects
                        expectServiceUnavailable "materializeObject" materialize
                        expectServiceUnavailable "dematerializeObject" dematerialize

                        // These calls use the session host settings, so lakeFS answers them without a provider storage-policy service.
                        let policy = expectDtoValue "getStoragePolicySettings" getPolicy
                        Vitest.expect(policy.Value.AutoPolicyThresholdMb).toEqual (Some 1)
                        Vitest.expect(policy.Value.MaterializeLargeObjects).toBe false

                        let changedPolicy = expectDtoSucceeded "setStoragePolicySettings" setPolicy
                        Vitest.expect(changedPolicy.Effect).toEqual OperationEffectDto.Performed

                        let! roundTrip = api.getStoragePolicySettings (request "lakefs-get-policy-after-set")
                        let roundTripPolicy = expectDtoValue "getStoragePolicySettings after set" roundTrip
                        Vitest.expect(roundTripPolicy.Value.AutoPolicyThresholdMb).toEqual (Some 2)
                        Vitest.expect(roundTripPolicy.Value.MaterializeLargeObjects).toBe false

                        do! fixture.Host.CloseSession fixture.Workspace |> Async.StartAsPromise
                        let! reopenedInfo = api.getSessionInfo (request "lakefs-services-reopen")
                        expectDtoSucceeded "reopen lakeFS session" reopenedInfo |> ignore
                        let! reopenedPolicy = api.getStoragePolicySettings (request "lakefs-get-policy-after-reopen")
                        let reopened = expectDtoValue "getStoragePolicySettings after reopen" reopenedPolicy
                        Vitest.expect(reopened.Value.AutoPolicyThresholdMb).toEqual (Some 1)
                        Vitest.expect(reopened.Value.MaterializeLargeObjects).toBe false

                        expectServiceUnavailable "setPathStoragePolicy" setPathPolicy
                        expectServiceUnavailable "pruneStorage" prune
                        expectServiceUnavailable "deduplicateStorage" deduplicate
                        expectServiceUnavailable "getRepositoryWebUrl" repositoryUrl
                    })
            }
        )

        Vitest.test (
            "clearStaleLock on a lakeFS vault answers without a git lock",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! api = bindWorkspaceViaClone fixture 69 "lakefs-stale-lock-clone"
                        let! cleared = api.clearStaleLock (request "lakefs-clear-stale-lock")
                        let outcome = expectDtoSucceeded "clear lakeFS stale lock" cleared
                        Vitest.expect(outcome.Effect).toEqual (OperationEffectDto.NoOp(Some "no stale lock"))
                        Vitest.expect(outcome.Warnings).toEqual [||]
                        Vitest.expect(outcome.AffectedPaths).toEqual [||]
                        Vitest.expect(outcome.Value.ActiveConflictSession).toEqual None
                    })
            }
        )

        Vitest.test (
            "batch materialization on a lakeFS vault answers service_unavailable",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! api = bindWorkspaceViaClone fixture 68 "lakefs-batch-materialize-clone"

                        let! materialize =
                            api.materializeObjects {
                                OperationId = "lakefs-materialize-objects"
                                Paths = [| "data.txt" |]
                            }

                        let! dematerialize =
                            api.dematerializeObjects {
                                OperationId = "lakefs-dematerialize-objects"
                                Paths = [| "data.txt" |]
                            }

                        expectServiceUnavailable "materializeObjects" materialize
                        expectServiceUnavailable "dematerializeObjects" dematerialize
                    })
            }
        )

        Vitest.test (
            "commits selected paths and synchronizes them to the repository",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! api = bindWorkspaceViaClone fixture 63 "lakefs-commit-clone"
                        writeText (join [| fixture.Workspace; "data.txt" |]) "local data\n"

                        let! before = api.getStatus (request "lakefs-commit-status-before")
                        let beforeValue = (expectDtoValue "status before commit" before).Value
                        Vitest.expect(beforeValue.Changes |> Array.map _.Path).toEqual [| "data.txt" |]

                        let! created =
                            api.createRevision {
                                OperationId = "lakefs-create-revision"
                                Message = "Add data"
                                Paths = [| "data.txt" |]
                                ExpectedWorkspaceVersion = beforeValue.WorkspaceVersion
                            }

                        // A partial local revision still supplies the state needed by the following status check.
                        expectDtoValue "create revision" created |> ignore

                        let! afterCommit = api.getStatus (request "lakefs-commit-status-after")
                        let afterCommitValue = (expectDtoValue "status after commit" afterCommit).Value
                        Vitest.expect(afterCommitValue.Changes).toEqual [||]

                        let! published =
                            api.synchronize {
                                OperationId = "lakefs-synchronize"
                                ExpectedWorkspaceVersion = afterCommitValue.WorkspaceVersion
                                ExpectedTargetRevision = None
                                AcceptUpdateRisks = false
                                PublishLocalRevisions = true
                            }

                        let synchronizedValue = (expectDtoValue "synchronize" published).Value
                        Vitest.expect(synchronizedValue.Relationship).toEqual RevisionRelationshipDto.UpToDate
                        Vitest.expect(synchronizedValue.LocalRevisionCount).toEqual None

                        let! objects = serverObjects fixture (lakeFsConnection ()) "main" "lakefs-list-main"
                        Vitest.expect(objects |> Array.exists (fun item -> item.Path = "data.txt")).toBe true
                    })
            }
        )

        Vitest.test (
            "synchronizes a server revision into the workspace",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! api = bindWorkspaceViaClone fixture 64 "lakefs-update-clone"
                        let! _ = commitAndPublish fixture api "data.txt" "local data\n" "lakefs-update-base"

                        do!
                            uploadTextObject
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "remote.txt"
                                "remote content\n"
                                "lakefs-update-upload"

                        do!
                            commitServer
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "Add remote file"
                                "lakefs-update-remote-commit"

                        let! status = api.getStatus (request "lakefs-update-status")
                        let statusValue = (expectDtoValue "status before synchronize" status).Value

                        let! synchronized =
                            api.synchronize {
                                OperationId = "lakefs-synchronize-update"
                                ExpectedWorkspaceVersion = statusValue.WorkspaceVersion
                                ExpectedTargetRevision = None
                                AcceptUpdateRisks = false
                                PublishLocalRevisions = false
                            }

                        let outcome = expectDtoSucceeded "synchronize update" synchronized
                        Vitest.expect(outcome.Effect).toEqual OperationEffectDto.Performed

                        Vitest
                            .expect(
                                Main.Bindings.Filesystem.readFileSync
                                    (join [| fixture.Workspace; "remote.txt" |])
                                    Main.Bindings.Filesystem.TextEncoding.Utf8
                            )
                            .toBe
                            "remote content\n"

                        let! after = api.getStatus (request "lakefs-after-synchronize")
                        let afterValue = (expectDtoValue "status after synchronize" after).Value
                        Vitest.expect(afterValue.ActiveConflictSession).toEqual None
                    })
            }
        )

        Vitest.test (
            "opens a conflict session on a diverged file, finalizes the resolution, and abandons a second one",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! api = bindWorkspaceViaClone fixture 65 "lakefs-conflict-clone"
                        let! _ = commitAndPublish fixture api "shared.txt" "base" "lakefs-conflict-base"

                        do!
                            uploadTextObject
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "shared.txt"
                                "server"
                                "lakefs-conflict-server-upload"

                        do!
                            commitServer
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "Server shared change"
                                "lakefs-conflict-server-commit"

                        writeText (join [| fixture.Workspace; "shared.txt" |]) "local"
                        let! localStatus = api.getStatus (request "lakefs-conflict-local-status")
                        let localStatusValue = (expectDtoValue "conflict local status" localStatus).Value

                        let! localRevision =
                            api.createRevision {
                                OperationId = "lakefs-conflict-local-revision"
                                Message = "Local shared change"
                                Paths = [| "shared.txt" |]
                                ExpectedWorkspaceVersion = localStatusValue.WorkspaceVersion
                            }

                        // A partial local revision still provides the revision used by the conflict update.
                        expectDtoValue "create local conflict revision" localRevision |> ignore

                        let! beforeUpdate = api.getStatus (request "lakefs-conflict-update-status")

                        let beforeUpdateValue =
                            (expectDtoValue "status before conflict update" beforeUpdate).Value

                        // A diverged file needs the acceptance round trip: the first synchronize
                        // stops with the decision, the accepted one opens the conflict session.
                        let! decisionResult =
                            api.synchronize {
                                OperationId = "lakefs-conflict-synchronize-decision"
                                ExpectedWorkspaceVersion = beforeUpdateValue.WorkspaceVersion
                                ExpectedTargetRevision = None
                                AcceptUpdateRisks = false
                                PublishLocalRevisions = false
                            }

                        let decisionFailure = expectDtoFailureOrPartial "conflict decision" decisionResult
                        Vitest.expect(decisionFailure.Category).toEqual FailureCategoryDto.Conflict
                        Vitest.expect(decisionFailure.Code).toBe VersionControlCodes.UpdateWouldCreateConflictSession

                        let observedTarget =
                            decisionFailure.RevisionEvidence
                            |> Array.tryFind (fun evidence -> evidence.Label = "observed_target")
                            |> Option.map _.Revision

                        Vitest.expect(observedTarget.IsSome).toBe true

                        let! updateResult =
                            api.synchronize {
                                OperationId = "lakefs-conflict-synchronize"
                                ExpectedWorkspaceVersion = beforeUpdateValue.WorkspaceVersion
                                ExpectedTargetRevision = observedTarget
                                AcceptUpdateRisks = true
                                PublishLocalRevisions = false
                            }

                        let updateFailure = expectDtoFailureOrPartial "conflict update" updateResult
                        Vitest.expect(updateFailure.Category).toEqual FailureCategoryDto.Conflict
                        Vitest.expect(updateFailure.Code).toBe VersionControlCodes.ConflictsDetected

                        let! conflictStatus = api.getStatus (request "lakefs-conflict-status")

                        let conflictStatusValue =
                            (expectDtoValue "status with conflict" conflictStatus).Value

                        let conflict =
                            conflictStatusValue.ActiveConflictSession
                            |> Option.defaultWith (fun () -> failwith "Expected an active lakeFS conflict session.")

                        Vitest.expect(conflict.Items |> Array.exists (fun item -> item.Path = "shared.txt")).toBe true

                        let conflictItem =
                            conflict.Items |> Array.find (fun item -> item.Path = "shared.txt")

                        Vitest.expect(conflictItem.SupportsResolvedContent).toBe true

                        let! resolved =
                            api.resolveConflict {
                                OperationId = "lakefs-resolve-conflict"
                                Handle = conflict.Handle
                                ExpectedWorkspaceVersion = conflictStatusValue.WorkspaceVersion
                                Path = "shared.txt"
                                Resolution = ConflictResolutionDto.SupplyResolvedContent "merged"
                            }

                        let resolvedValue = (expectDtoValue "resolve conflict" resolved).Value
                        Vitest.expect(resolvedValue.RemainingItems).toEqual [||]

                        let! afterResolve = api.getStatus (request "lakefs-conflict-finalize-status")
                        let afterResolveValue = (expectDtoValue "status before finalize" afterResolve).Value

                        let! finalized =
                            api.finalizeConflict {
                                OperationId = "lakefs-finalize-conflict"
                                Handle = resolvedValue.RefreshedHandle
                                ExpectedWorkspaceVersion = afterResolveValue.WorkspaceVersion
                                Message = Some "Finalize merged shared file"
                            }

                        expectDtoSucceeded "finalize conflict" finalized |> ignore

                        let! afterFinalize = api.getStatus (request "lakefs-conflict-publish-status")

                        let afterFinalizeValue =
                            (expectDtoValue "status before conflict publish" afterFinalize).Value

                        Vitest.expect(afterFinalizeValue.ActiveConflictSession).toEqual None

                        let! published =
                            api.synchronize {
                                OperationId = "lakefs-synchronize-conflict"
                                ExpectedWorkspaceVersion = afterFinalizeValue.WorkspaceVersion
                                ExpectedTargetRevision = None
                                AcceptUpdateRisks = false
                                PublishLocalRevisions = true
                            }

                        expectDtoSucceeded "synchronize resolved conflict" published |> ignore

                        let! serverContent =
                            downloadServerText
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "shared.txt"
                                "lakefs-conflict-download"

                        Vitest.expect(serverContent).toBe "merged"

                        do!
                            uploadTextObject
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "shared.txt"
                                "server-cancel"
                                "lakefs-conflict-cancel-server-upload"

                        do!
                            commitServer
                                fixture
                                (lakeFsConnection ())
                                "main"
                                "Server change for canceled conflict"
                                "lakefs-conflict-cancel-server-commit"

                        writeText (join [| fixture.Workspace; "shared.txt" |]) "local-cancel"
                        let! cancelLocalStatus = api.getStatus (request "lakefs-conflict-cancel-local-status")

                        let cancelLocalStatusValue =
                            (expectDtoValue "cancel conflict local status" cancelLocalStatus).Value

                        let! cancelRevision =
                            api.createRevision {
                                OperationId = "lakefs-conflict-cancel-local-revision"
                                Message = "Local change for canceled conflict"
                                Paths = [| "shared.txt" |]
                                ExpectedWorkspaceVersion = cancelLocalStatusValue.WorkspaceVersion
                            }

                        // A partial local revision still provides the revision used by the conflict update.
                        expectDtoValue "create canceled conflict revision" cancelRevision |> ignore

                        let! cancelBeforeUpdate = api.getStatus (request "lakefs-conflict-cancel-update-status")

                        let cancelBeforeUpdateValue =
                            (expectDtoValue "status before canceled conflict update" cancelBeforeUpdate).Value

                        let! cancelDecision =
                            api.synchronize {
                                OperationId = "lakefs-conflict-cancel-synchronize-decision"
                                ExpectedWorkspaceVersion = cancelBeforeUpdateValue.WorkspaceVersion
                                ExpectedTargetRevision = None
                                AcceptUpdateRisks = false
                                PublishLocalRevisions = false
                            }

                        let cancelDecisionFailure =
                            expectDtoFailureOrPartial "canceled conflict decision" cancelDecision

                        Vitest.expect(cancelDecisionFailure.Code).toBe
                            VersionControlCodes.UpdateWouldCreateConflictSession

                        let cancelObservedTarget =
                            cancelDecisionFailure.RevisionEvidence
                            |> Array.tryFind (fun evidence -> evidence.Label = "observed_target")
                            |> Option.map _.Revision

                        let! cancelUpdate =
                            api.synchronize {
                                OperationId = "lakefs-conflict-cancel-synchronize"
                                ExpectedWorkspaceVersion = cancelBeforeUpdateValue.WorkspaceVersion
                                ExpectedTargetRevision = cancelObservedTarget
                                AcceptUpdateRisks = true
                                PublishLocalRevisions = false
                            }

                        let cancelFailure =
                            expectDtoFailureOrPartial "canceled conflict update" cancelUpdate

                        Vitest.expect(cancelFailure.Category).toEqual FailureCategoryDto.Conflict
                        Vitest.expect(cancelFailure.Code).toBe VersionControlCodes.ConflictsDetected

                        let! cancelConflictStatus = api.getStatus (request "lakefs-conflict-cancel-status")

                        let cancelConflict =
                            (expectDtoValue "status with canceled conflict" cancelConflictStatus)
                                .Value.ActiveConflictSession
                            |> Option.defaultWith (fun () -> failwith "Expected a fresh lakeFS conflict session.")

                        let! canceledConflict =
                            api.cancelConflict {
                                OperationId = "lakefs-cancel-conflict"
                                Handle = cancelConflict.Handle
                                ExpectedWorkspaceVersion =
                                    (expectDtoValue "status before cancel conflict" cancelConflictStatus)
                                        .Value.WorkspaceVersion
                            }

                        expectDtoSucceeded "cancelConflict" canceledConflict |> ignore

                        let! afterCancelConflict = api.getStatus (request "lakefs-conflict-after-cancel-status")

                        let afterCancelConflictValue =
                            (expectDtoValue "status after cancel conflict" afterCancelConflict).Value

                        Vitest.expect(afterCancelConflictValue.ActiveConflictSession).toEqual None
                    })
            }
        )

        Vitest.test (
            "reports an unreachable endpoint with a stable failure code",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                return!
                    withFixture (fun fixture -> promise {
                        let! _ = bindWorkspaceViaClone fixture 66 "lakefs-unreachable-clone"
                        do! fixture.Host.CloseAll() |> Async.StartAsPromise

                        let badConnection = {
                            lakeFsConnection () with
                                Endpoint = "http://127.0.0.1:9"
                        }

                        let badRuntime =
                            createRuntime
                                fixture.Settings
                                (LakeFsCredentials.fixedConnection badConnection)
                                fixture.Bindings

                        let badHost = WorkspaceSessionHost.WorkspaceSessionHost(badRuntime)
                        WorkspaceSessionHost.initialize badHost

                        // Restore the process-global host so later fixture cleanup uses the live server connection.
                        try
                            registerVault 66 fixture.Workspace |> ignore
                            let api = Main.IPC.IVersionControlApi.api (ipcEvent 66)
                            let! info = api.getSessionInfo (request "lakefs-unreachable-info")
                            let failure = expectDtoFailure "unreachable session info" info
                            Vitest.expect(failure.Category).toEqual FailureCategoryDto.Network
                            Vitest.expect(failure.Code).toBe VersionControlCodes.NetworkFailure
                            Vitest.expect(failure.StateChanged).toBe false
                            Vitest.expect(failure.Retryable).toBe true
                            do! badHost.CloseAll() |> Async.StartAsPromise
                            WorkspaceSessionHost.initialize fixture.Host
                        with error ->
                            do! badHost.CloseAll() |> Async.StartAsPromise
                            WorkspaceSessionHost.initialize fixture.Host
                            return raise error
                    })
            }
        )

        Vitest.test (
            "cancels a running operation through its operation key",
            TestOptions(timeout = 120000, skip = not (integrationEnabled ())),
            fun () -> promise {
                let entered, signalEntered = deferred ()
                let gate, releaseGate = deferred ()
                let mutable barrierHits = 0

                let hooks: LakeFsWorkspaceSession.LakeFsSessionHooks = {
                    Barrier =
                        Some(fun _ point context -> async {
                            // Only the publish under test waits at the barrier. The publish that
                            // proves the session is still usable afterwards passes through.
                            if point = "publish-connect" && context.OperationId = "lakefs-cancel-publish" then
                                barrierHits <- barrierHits + 1
                                signalEntered ()
                                do! Async.AwaitPromise gate
                        })
                }

                return!
                    withHookFixture
                        hooks
                        (fun fixture -> promise {
                            let! api = bindWorkspaceViaClone fixture 67 "lakefs-cancel-clone"
                            writeText (join [| fixture.Workspace; "data.txt" |]) "local data\n"
                            let! setupStatus = api.getStatus (request "lakefs-cancel-setup-status")
                            let setupStatusValue = (expectDtoValue "cancel setup status" setupStatus).Value

                            let! created =
                                api.createRevision {
                                    OperationId = "lakefs-cancel-setup-revision"
                                    Message = "Add data for cancellation"
                                    Paths = [| "data.txt" |]
                                    ExpectedWorkspaceVersion = setupStatusValue.WorkspaceVersion
                                }

                            // The setup revision is usable when it reports recoverable warnings.
                            expectDtoValue "cancel setup revision" created |> ignore
                            let! status = api.getStatus (request "lakefs-cancel-status")
                            let statusValue = (expectDtoValue "cancel status" status).Value

                            let! beforeBranch =
                                LakeFsApi.getBranch
                                    (lakeFsConnection ())
                                    fixture.Repository
                                    "main"
                                    (detached "lakefs-cancel-before-publish")
                                |> Async.StartAsPromise

                            let beforeHead =
                                (expectLakeFs "lakeFS head before canceled publish" beforeBranch).CommitId

                            let running =
                                api.synchronize {
                                    OperationId = "lakefs-cancel-publish"
                                    ExpectedWorkspaceVersion = statusValue.WorkspaceVersion
                                    ExpectedTargetRevision = None
                                    AcceptUpdateRisks = false
                                    PublishLocalRevisions = true
                                }

                            let enteredRace = promise {
                                let! () = entered
                                return None
                            }

                            let runningRace = promise {
                                let! result = running
                                return Some result
                            }

                            try
                                let! raced = promiseRace [| enteredRace; runningRace |]

                                match raced with
                                | Some result ->
                                    let failure = expectDtoFailureOrPartial "canceled lakeFS publish" result

                                    return
                                        failwith
                                            $"Canceled lakeFS publish completed before publish-connect ({failure.Code}): {failure.Message}"
                                | None ->
                                    let! canceled =
                                        api.cancelOperation {
                                            OperationId = "lakefs-cancel-publish"
                                        }

                                    Vitest.expect(canceled).toEqual (Ok true)
                                    releaseGate ()

                                    let! result = running
                                    let failure = expectDtoFailureOrPartial "canceled lakeFS publish" result
                                    Vitest.expect(failure.Category).toEqual FailureCategoryDto.Canceled
                                    Vitest.expect(failure.Code).toBe VersionControlCodes.OperationCanceled
                                    Vitest.expect(failure.StateChanged).toBe false
                                    Vitest.expect(barrierHits).toBe 1

                                    let! afterBranch =
                                        LakeFsApi.getBranch
                                            (lakeFsConnection ())
                                            fixture.Repository
                                            "main"
                                            (detached "lakefs-cancel-after-publish")
                                        |> Async.StartAsPromise

                                    let afterHead =
                                        (expectLakeFs "lakeFS head after canceled publish" afterBranch).CommitId

                                    Vitest.expect(afterHead).toBe beforeHead

                                    let! retryStatus = api.getStatus (request "lakefs-cancel-retry-status")
                                    let retryStatusValue = (expectDtoValue "cancel retry status" retryStatus).Value

                                    let! republished =
                                        api.synchronize {
                                            OperationId = "lakefs-cancel-retry-synchronize"
                                            ExpectedWorkspaceVersion = retryStatusValue.WorkspaceVersion
                                            ExpectedTargetRevision = None
                                            AcceptUpdateRisks = false
                                            PublishLocalRevisions = true
                                        }

                                    expectDtoSucceeded "synchronize after cancellation" republished |> ignore

                                    let! objects =
                                        serverObjects fixture (lakeFsConnection ()) "main" "lakefs-cancel-list-main"

                                    Vitest.expect(objects |> Array.exists (fun item -> item.Path = "data.txt")).toBe
                                        true
                            with error ->
                                releaseGate ()
                                return raise error
                        })
            }
        )
)
