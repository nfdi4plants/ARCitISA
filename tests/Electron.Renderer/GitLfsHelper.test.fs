module ElectronRenderer.GitLfsHelperTests

open Fable.Core.JsInterop
open Renderer.Components.Helper.GitLfsHelper
open Swate.Electron.Shared.VersionControlTypes
open Vitest

// The helper module pulls in the version control client, which reads its IPC bridge when it loads.
// The bridge only exists inside Electron, so the test replaces it with an empty object that no test calls.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () -> createObj [ "ipcVersionControlApi" ==> createObj [] ])
)
|> ignore

let private outcome: OperationOutcomeDto<unit> = {
    Value = ()
    Effect = OperationEffectDto.Performed
    Warnings = [||]
    AffectedPaths = [||]
    ResultingRevision = None
    ResultingWorkspaceVersion = None
    Publication = PublicationStateDto.NotApplicable
}

let private failureWith (details: string[]) : OperationFailureDto = {
    Category = FailureCategoryDto.ProviderError
    Code = "failed"
    Message = "Some files failed."
    StateChanged = false
    Retryable = false
    AffectedPaths = [||]
    RecoveryAction = None
    Details = details
    RevisionEvidence = [||]
    DiffDetail = None
}

let private detailLines (count: int) = [| for index in 1..count -> $"file{index}.bin: no space" |]

Vitest.describe (
    "toBatchResult",
    fun () ->
        Vitest.test (
            "maps a succeeded result to Ok",
            fun () -> Vitest.expect(toBatchResult (Ok(OperationResultDto.Succeeded outcome))).toEqual (Ok())
        )

        Vitest.test (
            "lists the detail lines of a failed result after the failure message",
            fun () ->
                let result =
                    toBatchResult (Ok(OperationResultDto.Failed(failureWith [| "a.bin: offline"; "b.bin: no space" |])))

                Vitest.expect(result).toEqual (Error "Some files failed.\na.bin: offline\nb.bin: no space")
        )

        Vitest.test (
            "reports a partial result as an Error with the failed paths",
            fun () ->
                let result =
                    toBatchResult (
                        Ok(OperationResultDto.PartiallySucceeded(outcome, failureWith [| "b.bin: no space" |]))
                    )

                Vitest.expect(result).toEqual (Error "Some files failed.\nb.bin: no space")
        )

        Vitest.test (
            "lists at most ten detail lines and counts the rest",
            fun () ->
                let ten =
                    toBatchResult (Ok(OperationResultDto.Failed(failureWith (detailLines 10))))

                let twelve =
                    toBatchResult (Ok(OperationResultDto.Failed(failureWith (detailLines 12))))

                Vitest.expect(ten).toEqual (Error("Some files failed.\n" + String.concat "\n" (detailLines 10)))

                Vitest
                    .expect(twelve)
                    .toEqual (
                        Error(
                            "Some files failed.\n"
                            + String.concat "\n" (detailLines 10)
                            + "\n... and 2 more."
                        )
                    )
        )

)
