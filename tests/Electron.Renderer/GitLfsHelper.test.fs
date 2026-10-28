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

let private failureOf (details: string[]) : OperationFailureDto = {
    failureWith details with
        AffectedPaths = details |> Array.map (fun detail -> detail.Split(':').[0])
}

let private linesOf (message: string) = message.Split('\n')

Vitest.describe (
    "toBatchResult",
    fun () ->
        Vitest.test (
            "maps a succeeded result to Ok",
            fun () -> Vitest.expect(toBatchResult (Ok(OperationResultDto.Succeeded outcome))).toEqual (Ok())
        )

        Vitest.test (
            "keeps every detail line and the number of failed paths of a failed result",
            fun () ->
                let details = [| "a.bin: offline"; "b.bin: no space" |]
                let result = toBatchResult (Ok(OperationResultDto.Failed(failureOf details)))

                match result with
                | Error failure ->
                    Vitest.expect(failure.Details).toEqual details
                    Vitest.expect(failure.FailedCount).toBe 2
                    Vitest.expect(failure.Message.Contains "Some files failed.").toBe true
                | Ok() -> failwith "expected an Error"
        )

        Vitest.test (
            "reports a partial result as an Error with the failed paths",
            fun () ->
                let result =
                    toBatchResult (
                        Ok(OperationResultDto.PartiallySucceeded(outcome, failureOf [| "b.bin: no space" |]))
                    )

                match result with
                | Error failure ->
                    Vitest.expect(failure.Details).toEqual [| "b.bin: no space" |]
                    Vitest.expect(failure.FailedCount).toBe 1
                | Ok() -> failwith "expected an Error"
        )

        Vitest.test (
            "turns a transport error into a failure without detail lines",
            fun () ->
                match toBatchResult (Error "service unavailable") with
                | Error failure ->
                    Vitest.expect(failure.Message).toBe "service unavailable"
                    Vitest.expect(failure.Details.Length).toBe 0
                | Ok() -> failwith "expected an Error"
        )
)

let private failureTextOf (details: string[]) (totalCount: int) =
    match toBatchResult (Ok(OperationResultDto.Failed(failureOf details))) with
    | Error failure -> folderFailureText "data" "downloaded" totalCount failure
    | Ok() -> failwith "expected an Error"

Vitest.describe (
    "folderFailureText",
    fun () ->
        Vitest.test (
            "lists the folder, the failed count of the total and each failed path",
            fun () ->
                let message, _ = failureTextOf (detailLines 3) 8

                Vitest.expect((linesOf message).Length).toBe 5
                Vitest.expect(message.Contains "'data'").toBe true
                Vitest.expect(message.Contains "3 of 8").toBe true

                for line in detailLines 3 do
                    Vitest.expect(message.Contains line).toBe true

                Vitest.expect(message.Contains "Some files failed.").toBe false
        )

        Vitest.test (
            "lists at most ten detail lines and counts the rest",
            fun () ->
                let ten, _ = failureTextOf (detailLines 10) 10
                let twelve, _ = failureTextOf (detailLines 12) 12

                // The folder line and the count line, then the listed entries.
                Vitest.expect((linesOf ten).Length).toBe 12
                Vitest.expect((linesOf twelve).Length).toBe 13
                Vitest.expect(twelve.Contains "file10.bin").toBe true
                Vitest.expect(twelve.Contains "file11.bin").toBe false
                Vitest.expect((linesOf twelve).[12].Contains "2").toBe true
        )

        Vitest.test (
            "shows the first line of each entry and keeps the whole entries in the technical details",
            fun () ->
                let details = [|
                    "a.bin: smudge failed\nerror: external filter failed\nfatal: a.bin: smudge filter lfs failed"
                    "b.bin: smudge failed\nerror: external filter failed"
                |]

                let message, technical = failureTextOf details 2

                Vitest.expect((linesOf message).Length).toBe 4
                Vitest.expect(message.Contains "a.bin: smudge failed").toBe true
                Vitest.expect(message.Contains "external filter failed").toBe false
                Vitest.expect(technical).toEqual (Some(String.concat "\n\n" details))
        )

        Vitest.test (
            "keeps the failure message and has no technical details when the failure lists no entries",
            fun () ->
                let failure = {
                    Message = "The service is unavailable. Try again."
                    FailedCount = 0
                    Details = [||]
                }

                let message, technical = folderFailureText "data" "freed" 4 failure

                Vitest.expect((linesOf message).Length).toBe 2
                Vitest.expect(message.Contains failure.Message).toBe true
                Vitest.expect(technical).toEqual None
        )
)

Vitest.describe (
    "FolderLfsAction",
    fun () ->
        let objects: Renderer.Context.LargeObjectStore.FolderLargeObjects = {
            NeedsDownload = [| "data/missing.bin" |]
            HasLocalCopy = [| "data/local.bin" |]
        }

        Vitest.test (
            "download takes the files that need a download",
            fun () -> Vitest.expect(FolderLfsAction.download.SelectPaths objects).toEqual objects.NeedsDownload
        )

        Vitest.test (
            "free takes the files that have a local copy",
            fun () -> Vitest.expect(FolderLfsAction.free.SelectPaths objects).toEqual objects.HasLocalCopy
        )
)
