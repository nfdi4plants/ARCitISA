module ElectronRenderer.ToUnitResultTests

open Renderer.Context.GitWorkflow
open Swate.Electron.Shared.VersionControlTypes
open Vitest

let private outcome: OperationOutcomeDto<unit> = {
    Value = ()
    Effect = OperationEffectDto.Performed
    Warnings = [||]
    AffectedPaths = [||]
    ResultingRevision = None
    ResultingWorkspaceVersion = None
    Publication = PublicationStateDto.NotApplicable
}

let private failure: OperationFailureDto = {
    Category = FailureCategoryDto.ProviderError
    Code = "failed"
    Message = "The operation failed."
    StateChanged = false
    Retryable = false
    AffectedPaths = [||]
    RecoveryAction = None
    Details = [||]
    RevisionEvidence = [||]
    DiffDetail = None
}

let private expectOk result =
    match toUnitResult result with
    | Ok() -> ()
    | Error message -> failwith $"Expected Ok(), got Error: {message}."

let private expectError result =
    match toUnitResult result with
    | Error _ -> ()
    | Ok() -> failwith "Expected Error, got Ok()."

Vitest.describe (
    "GitWorkflow.toUnitResult",
    fun () ->
        Vitest.test (
            "maps operation and transport results to unit results",
            fun () ->
                expectOk (Ok(OperationResultDto.Succeeded outcome))
                expectOk (Ok(OperationResultDto.PartiallySucceeded(outcome, failure)))
                expectError (Ok(OperationResultDto.Failed failure))

                match toUnitResult (Error(exn "transport error")) with
                | Error message -> Vitest.expect(message).toBe ("transport error")
                | Ok() -> failwith "Expected transport Error, got Ok()."
        )
)
