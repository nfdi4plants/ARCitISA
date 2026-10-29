module ElectronCore.VersionControlMappingsTests

open Main.VersionControl
open Swate.Electron.Shared.VersionControlTypes
open VersionControlService.Abstractions
open Vitest

let private revision (value: string) =
    RevisionId.tryCreate value |> Result.defaultWith failwith

let private path (value: string) =
    RepositoryPath.tryCreate value |> Result.defaultWith failwith

let private sampleFailure: OperationFailure = {
    Category = Concurrency
    Code = "precondition_failed"
    Message = "The workspace changed."
    StateChanged = true
    Retryable = true
    AffectedPaths = [| "assays/a/isa.assay.xlsx" |]
    RecoveryAction =
        Some {
            Code = "refresh_workspace"
            Instructions = Some "Refresh and retry."
        }
    Details = [| "detail line" |]
    DiffDetail = None
    RevisionEvidence = [|
        "expected_target", revision "abc"
        "observed_target", revision "def"
    |]
}

Vitest.describe (
    "Version control failure mapping",
    fun () ->
        Vitest.test (
            "keeps category, code, state flags, paths, recovery and evidence",
            fun () ->
                let mapped = Mappings.failure sampleFailure

                Vitest.expect(mapped.Category).toEqual FailureCategoryDto.Concurrency
                Vitest.expect(mapped.Code).toBe "precondition_failed"
                Vitest.expect(mapped.Message).toBe "The workspace changed."
                Vitest.expect(mapped.StateChanged).toBe true
                Vitest.expect(mapped.Retryable).toBe true
                Vitest.expect(mapped.AffectedPaths).toEqual [| "assays/a/isa.assay.xlsx" |]
                Vitest.expect(mapped.RecoveryAction |> Option.map _.Code).toEqual (Some "refresh_workspace")
                Vitest.expect(mapped.Details).toEqual [| "detail line" |]

                Vitest.expect(mapped.RevisionEvidence).toEqual [|
                    {
                        Label = "expected_target"
                        Revision = "abc"
                    }
                    {
                        Label = "observed_target"
                        Revision = "def"
                    }
                |]
        )

        Vitest.test (
            "every failure category has a distinct DTO value",
            fun () ->
                let categories = [
                    Validation
                    NotFound
                    Concurrency
                    Authentication
                    Authorization
                    DependencyMissing
                    Network
                    Timeout
                    Canceled
                    Conflict
                    Unsupported
                    ProviderError
                ]

                let mapped = categories |> List.map Mappings.failureCategory |> List.distinct
                Vitest.expect(mapped.Length).toBe categories.Length
                Vitest.expect(Mappings.failureCategory Canceled).toEqual FailureCategoryDto.Canceled
        )
)

Vitest.describe (
    "Version control result mapping",
    fun () ->
        Vitest.test (
            "a success keeps its outcome fields and maps the value",
            fun () ->
                let outcome: OperationOutcome<RevisionId> = {
                    Value = revision "abc"
                    Effect = Performed
                    Warnings = [|
                        {
                            Code = "lfs_skipped"
                            Message = "Large objects were not hydrated."
                        }
                    |]
                    AffectedPaths = [| "a.txt" |]
                    ResultingRevision = Some(revision "abc")
                    ResultingWorkspaceVersion = Some "v2"
                    Publication = LocalOnly
                }

                match Mappings.result RevisionId.value (Succeeded outcome) with
                | OperationResultDto.Succeeded mapped ->
                    Vitest.expect(mapped.Value).toBe "abc"
                    Vitest.expect(mapped.Effect).toEqual OperationEffectDto.Performed
                    Vitest.expect(mapped.Warnings |> Array.map _.Code).toEqual [| "lfs_skipped" |]
                    Vitest.expect(mapped.AffectedPaths).toEqual [| "a.txt" |]
                    Vitest.expect(mapped.ResultingRevision).toEqual (Some "abc")
                    Vitest.expect(mapped.ResultingWorkspaceVersion).toEqual (Some "v2")
                    Vitest.expect(mapped.Publication).toEqual PublicationStateDto.LocalOnly
                | other -> failwith $"Expected a success, got {other}"
        )

        Vitest.test (
            "a partial success carries both the outcome and the failure",
            fun () ->
                let result =
                    OperationResult.partiallySucceeded
                        (OperationOutcome.performed ())
                        (OperationFailure.create Canceled "operation_canceled" "Hydration was canceled.")
                        {
                            Code = "retry_materialization"
                            Instructions = None
                        }

                match Mappings.result id result with
                | OperationResultDto.PartiallySucceeded(outcome, failure) ->
                    Vitest.expect(outcome.Effect).toEqual OperationEffectDto.Performed
                    Vitest.expect(failure.Category).toEqual FailureCategoryDto.Canceled
                    Vitest.expect(failure.StateChanged).toBe true
                    Vitest.expect(failure.RecoveryAction |> Option.map _.Code).toEqual (Some "retry_materialization")
                | other -> failwith $"Expected a partial success, got {other}"
        )

        Vitest.test (
            "a no-op keeps its reason",
            fun () ->
                match Mappings.result id (OperationResult.noOp (Some "nothing to publish") ()) with
                | OperationResultDto.Succeeded outcome ->
                    Vitest.expect(outcome.Effect).toEqual (OperationEffectDto.NoOp(Some "nothing to publish"))
                | other -> failwith $"Expected a success, got {other}"
        )

        Vitest.test (
            "progress keeps float counters",
            fun () ->
                let mapped =
                    Mappings.progress "op-1" {
                        PhaseCode = "transfer"
                        Item = Some "data/big.bin"
                        Completed = Some 3221225472.0
                        Total = Some 6442450944.0
                        DisplayMessage = None
                    }

                Vitest.expect(mapped.OperationId).toBe "op-1"
                Vitest.expect(mapped.Completed).toEqual (Some 3221225472.0)
                Vitest.expect(mapped.Total).toEqual (Some 6442450944.0)
        )

        Vitest.test (
            "output keeps the operation id and maps the four kinds",
            fun () ->
                let entry kind : OperationOutput = {
                    CommandId = "git-1"
                    Kind = kind
                    Text = "line"
                    IsPartial = false
                }

                let mapped =
                    Mappings.output "op-1" [|
                        entry OperationOutputKind.Command
                        entry OperationOutputKind.StdOut
                        entry OperationOutputKind.StdErr
                        {
                            entry OperationOutputKind.Notice with
                                IsPartial = true
                        }
                    |]

                Vitest.expect(mapped.OperationId).toBe "op-1"
                Vitest.expect(mapped.Entries |> Array.map _.Kind).toEqual [| "command"; "stdout"; "stderr"; "notice" |]
                Vitest.expect(mapped.Entries[0].CommandId).toBe "git-1"
                Vitest.expect(mapped.Entries[0].Text).toBe "line"
                Vitest.expect(mapped.Entries[3].IsPartial).toBe true
        )
)

Vitest.describe (
    "Version control result helpers",
    fun () ->
        let canceled: OperationFailureDto = {
            Category = FailureCategoryDto.Canceled
            Code = "operation_canceled"
            Message = "canceled"
            StateChanged = true
            Retryable = false
            AffectedPaths = [||]
            RecoveryAction =
                Some {
                    Code = "retry_materialization"
                    Instructions = None
                }
            Details = [||]
            RevisionEvidence = [||]
            DiffDetail = None
        }

        let outcome: OperationOutcomeDto<int> = {
            Value = 1
            Effect = OperationEffectDto.Performed
            Warnings = [||]
            AffectedPaths = [||]
            ResultingRevision = None
            ResultingWorkspaceVersion = None
            Publication = PublicationStateDto.NotApplicable
        }

        Vitest.test (
            "resultChangedState is true for a performed success, a partial success and a state-changing failure only",
            fun () ->
                let changed = Main.IPC.IVersionControlApi.resultChangedState

                Vitest.expect(changed (Ok(OperationResultDto.Succeeded outcome))).toBe true

                Vitest
                    .expect(
                        changed (
                            Ok(
                                OperationResultDto.Succeeded {
                                    outcome with
                                        Effect = OperationEffectDto.NoOp None
                                }
                            )
                        )
                    )
                    .toBe
                    false

                Vitest.expect(changed (Ok(OperationResultDto.PartiallySucceeded(outcome, canceled)))).toBe true
                Vitest.expect(changed (Ok(OperationResultDto.Failed canceled))).toBe true
                Vitest.expect(changed (Ok(OperationResultDto.Failed { canceled with StateChanged = false }))).toBe false
                Vitest.expect(changed (Error(exn "transport"))).toBe false
        )

        Vitest.test (
            "tryValue follows the three shapes",
            fun () ->
                let partial = OperationResultDto.PartiallySucceeded(outcome, canceled)

                Vitest.expect(OperationResultDto.tryValue (OperationResultDto.Succeeded outcome)).toEqual (Some 1)
                Vitest.expect(OperationResultDto.tryValue partial).toEqual (Some 1)
                Vitest.expect(OperationResultDto.tryValue (OperationResultDto.Failed canceled)).toEqual None
        )
)

Vitest.describe (
    "Version control status mapping",
    fun () ->
        Vitest.test (
            "status, refs, conflicts and synchronization map structurally",
            fun () ->
                let providerRef =
                    ProviderRef.tryCreate "refs/heads/main" |> Result.defaultWith failwith

                let status: WorkspaceStatus = {
                    CurrentRef =
                        Some {
                            Name = "main"
                            ProviderRef = providerRef
                            Kind = LocalRef
                            IsCurrent = true
                        }
                    WorkspaceVersion = "v1"
                    Changes = [|
                        {
                            Path = path "b.txt"
                            OldPath = Some(path "a.txt")
                            Kind = RenamedChange
                        }
                    |]
                    ActiveConflictSession =
                        Some {
                            Handle = {
                                SessionId = "conflict-1"
                                Version = "3"
                            }
                            Items = [|
                                {
                                    Path = path "c.txt"
                                    Candidates = [|
                                        {
                                            CandidateId = "ours"
                                            Label = "Local"
                                            Revision = Some(revision "abc")
                                            Preview = Some(TextPreview "local")
                                            Object =
                                                Some {
                                                    SizeBytes = Some 3158016.0
                                                    ObjectId = Some "sha256:abc"
                                                    IsLocallyAvailable = true
                                                }
                                        }
                                        {
                                            CandidateId = "theirs"
                                            Label = "Online"
                                            Revision = Some(revision "def")
                                            Preview = None
                                            Object = None
                                        }
                                    |]
                                    CombinedPreview =
                                        Some(UnsupportedPreview(Some "Stored object preview unavailable."))
                                    SupportsResolvedContent = false
                                }
                            |]
                        }
                    Synchronization =
                        Some {
                            BaseRevision = None
                            WorkspaceRevision = Some(revision "abc")
                            TargetRevision = None
                            TargetRef = None
                            LocalRevisionCount = Some 1
                            TargetRevisionCount = None
                            RemoteChangedPaths = Some [| path "d.txt" |]
                            Relationship = NoTarget
                        }
                }

                let mapped = Mappings.workspaceStatus status

                Vitest.expect(mapped.CurrentRef |> Option.map _.ProviderRef).toEqual (Some "refs/heads/main")
                Vitest.expect(mapped.CurrentRef |> Option.map _.Kind).toEqual (Some RefKindDto.Local)
                Vitest.expect(mapped.WorkspaceVersion).toBe "v1"

                Vitest.expect(mapped.Changes).toEqual [|
                    {
                        Path = "b.txt"
                        OldPath = Some "a.txt"
                        Kind = FileChangeKindDto.Renamed
                    }
                |]

                let conflict =
                    mapped.ActiveConflictSession
                    |> Option.defaultWith (fun () -> failwith "conflict session missing")

                Vitest.expect(conflict.Handle).toEqual {
                    SessionId = "conflict-1"
                    Version = "3"
                }

                Vitest.expect(conflict.Items.[0].Path).toBe "c.txt"
                Vitest.expect(conflict.Items.[0].Candidates.[0].Revision).toEqual (Some "abc")
                Vitest.expect(conflict.Items.[0].Candidates.[0].Preview).toEqual (Some(ContentViewDto.Text "local"))

                Vitest
                    .expect(conflict.Items.[0].Candidates.[0].Object)
                    .toEqual (
                        Some {
                            SizeBytes = Some 3158016.0
                            ObjectId = Some "sha256:abc"
                            IsLocallyAvailable = true
                        }
                    )

                Vitest.expect(conflict.Items.[0].Candidates.[1].Object).toEqual None

                Vitest
                    .expect(conflict.Items.[0].CombinedPreview)
                    .toEqual (Some(ContentViewDto.Unsupported(Some "Stored object preview unavailable.")))

                let synchronization =
                    mapped.Synchronization |> Option.defaultWith (fun () -> failwith "sync missing")

                Vitest.expect(synchronization.Relationship).toEqual RevisionRelationshipDto.NoTarget
                Vitest.expect(synchronization.WorkspaceRevision).toEqual (Some "abc")
                Vitest.expect(synchronization.RemoteChangedPaths).toEqual (Some [| "d.txt" |])
        )

        Vitest.test (
            "object state and dependency status map field by field",
            fun () ->
                let objectState =
                    Mappings.objectState {
                        Path = path "data/big.bin"
                        IsMaterialized = false
                        IsLocallyAvailable = true
                        SizeBytes = Some 42.0
                        ObjectId = Some "sha256:abc"
                    }

                Vitest.expect(objectState).toEqual {
                    Path = "data/big.bin"
                    IsMaterialized = false
                    IsLocallyAvailable = true
                    SizeBytes = Some 42.0
                    ObjectId = Some "sha256:abc"
                }

                Vitest
                    .expect(
                        Mappings.dependencyStatus {
                            Component = "git"
                            Installed = true
                            Version = Some "2.51.0"
                            Compatible = true
                            Remediation = None
                        }
                    )
                    .toEqual
                    {
                        Component = "git"
                        Installed = true
                        Version = Some "2.51.0"
                        Compatible = true
                        Remediation = None
                    }
        )
)

Vitest.describe (
    "Version control request path validation",
    fun () ->
        Vitest.test (
            "an invalid path is refused before the provider is called and names the path",
            fun () ->
                match Mappings.tryRepositoryPaths [| "assays/a/isa.assay.xlsx"; "assays\\b\\isa.assay.xlsx" |] with
                | Ok _ -> failwith "Expected the backslash path to be refused."
                | Error failure ->
                    Vitest.expect(failure.Category).toEqual Validation
                    Vitest.expect(failure.Code).toBe VersionControlCodes.InvalidPath
                    Vitest.expect(failure.AffectedPaths).toEqual [| "assays\\b\\isa.assay.xlsx" |]
        )

        Vitest.test (
            "valid paths convert in order",
            fun () ->
                match Mappings.tryRepositoryPaths [| "a.txt"; "dir/b.txt" |] with
                | Ok paths -> Vitest.expect(paths |> Array.map RepositoryPath.value).toEqual [| "a.txt"; "dir/b.txt" |]
                | Error failure -> failwith failure.Message
        )
)
