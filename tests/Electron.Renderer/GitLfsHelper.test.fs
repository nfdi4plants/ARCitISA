module ElectronRenderer.GitLfsHelperTests

open Fable.Core
open Fable.Core.JsInterop
open Renderer.Components.Helper.GitLfsHelper
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

/// The requests the mocked main process received, one list per batch call.
let private materializeCalls = ResizeArray<ObjectPathsRequestDto>()
let private dematerializeCalls = ResizeArray<ObjectPathsRequestDto>()

let private succeeded
    (calls: ResizeArray<ObjectPathsRequestDto>)
    (request: ObjectPathsRequestDto)
    : JS.Promise<Result<OperationResultDto<unit>, exn>> =
    promise {
        calls.Add request
        return Ok(OperationResultDto.Succeeded outcome)
    }

// The helper module pulls in the version control client, which reads its IPC bridge when it loads.
// The bridge only exists inside Electron, so the test replaces the two batch calls with recorders.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcVersionControlApi"
            ==> createObj [
                "materializeObjects"
                ==> fun (request: ObjectPathsRequestDto) -> succeeded materializeCalls request
                "dematerializeObjects"
                ==> fun (request: ObjectPathsRequestDto) -> succeeded dematerializeCalls request
            ]
        ]
    )
)
|> ignore

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
                    Vitest.expect(failure.AffectedPaths.Length).toBe 2
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
                    Vitest.expect(failure.AffectedPaths.Length).toBe 1
                | Ok() -> failwith "expected an Error"
        )

        Vitest.test (
            "treats a partial result without failed paths as Ok, the way a fresh session open reports it",
            fun () ->
                let result =
                    toBatchResult (Ok(OperationResultDto.PartiallySucceeded(outcome, failureWith [||])))

                Vitest.expect(result).toEqual (Ok())
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

let private failureTextFor (failure: OperationFailureDto) (totalCount: int) =
    match toBatchResult (Ok(OperationResultDto.Failed failure)) with
    | Error batchFailure -> folderFailureText "data" "downloaded" totalCount batchFailure
    | Ok() -> failwith "expected an Error"

let private failureTextOf (details: string[]) (totalCount: int) =
    failureTextFor (failureOf details) totalCount

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
            "shows the path and the cause line of a git-lfs entry and keeps the whole entries in the technical details",
            fun () ->
                // One entry as the library sent it when the LFS server was unreachable, captured from an E2E run.
                let captured =
                    "assays/assayA/dataset/a[1].bin: Fetching reference refs/heads/main\nbatch response: Post \"https://[REDACTED]@localhost:3443/e2eadmin/arc-folderlfs.git/info/lfs/objects/batch\": EOF\nerror: failed to fetch some objects from 'https://[REDACTED]@localhost:3443/e2eadmin/arc-folderlfs.git/info/lfs'"

                let details = [| captured; "b.bin: no space" |]

                let message, technical = failureTextOf details 2
                let lines = linesOf message

                Vitest.expect(lines.Length).toBe 4
                Vitest.expect(lines.[2].Contains "batch response:").toBe true
                Vitest.expect(lines.[2].Contains "assays/assayA/dataset/a[1].bin").toBe true
                Vitest.expect(lines.[3]).toBe "b.bin: no space"
                Vitest.expect(message.Contains "Fetching reference").toBe false
                Vitest.expect(message.Contains "failed to fetch some objects").toBe false
                Vitest.expect(technical).toEqual (Some(String.concat "\n\n" details))
        )

        Vitest.test (
            "keeps the text before the noise of the first line and adds the cause line of a failed free",
            fun () ->
                // One entry as the library sent it for a failed free, captured from an E2E run.
                let captured =
                    "data/a.bin: Could not confirm that the remote has 'data/a.bin': Fetching reference refs/heads/master\nbatch response: Post \"http://127.0.0.1:1/info/lfs/objects/batch\": dial tcp 127.0.0.1:1: connectex: No connection could be made because the target machine actively refused it.\nerror: failed to fetch some objects from 'http://127.0.0.1:1/info/lfs'"

                let message, _ = failureTextOf [| captured |] 1
                let lines = linesOf message

                Vitest.expect(lines.Length).toBe 3
                Vitest.expect(lines.[2].Contains "Could not confirm that the remote has").toBe true
                Vitest.expect(lines.[2].Contains "batch response:").toBe true
                Vitest.expect(message.Contains "Fetching reference").toBe false
                Vitest.expect(message.Contains "failed to fetch some objects").toBe false
        )

        Vitest.test (
            "treats a first line with the fetch: prefix of older git-lfs as noise",
            fun () ->
                let details = [|
                    "a.bin: fetch: Fetching reference refs/heads/main\nbatch response: EOF"
                |]

                let lines = linesOf (fst (failureTextOf details 1))

                Vitest.expect(lines.[2]).toBe "a.bin: batch response: EOF"
        )

        Vitest.test (
            "keeps a path that contains a colon whole when the entries are parallel to the paths",
            fun () ->
                let failure = {
                    failureWith [|
                        "notes: a.bin: Fetching reference refs/heads/main\nbatch response: EOF"
                    |] with
                        AffectedPaths = [| "notes: a.bin" |]
                }

                let message, _ = failureTextFor failure 1

                Vitest.expect((linesOf message).[2]).toBe "notes: a.bin: batch response: EOF"
        )

        Vitest.test (
            "splits the path off at the first colon when the entries do not match the paths one to one",
            fun () ->
                let failure = {
                    failureWith [|
                        "a.bin: Fetching reference refs/heads/main\nbatch response: EOF"
                        "the index is locked"
                    |] with
                        AffectedPaths = [| "a.bin" |]
                }

                let lines = linesOf (fst (failureTextFor failure 3))

                Vitest.expect(lines.[2]).toBe "a.bin: batch response: EOF"
                Vitest.expect(lines.[3]).toBe "the index is locked"
        )

        Vitest.test (
            "keeps the failure message with its recovery text and the details when no path failed",
            fun () ->
                let details = [|
                    "Lock file: C:/arc/.git/index.lock"
                    "Lock file age: 3 s"
                |]

                let locked = {
                    failureWith details with
                        Code = VersionControlCodes.IndexLocked
                        RecoveryAction =
                            Some {
                                Code = VersionControlCodes.Recovery.ReconcileIndex
                                Instructions = Some "Close other Git programs."
                            }
                }

                match toBatchResult (Ok(OperationResultDto.Failed locked)) with
                | Ok() -> failwith "expected an Error"
                | Error failure ->
                    let message, technical = folderFailureText "data" "downloaded" 5 failure
                    let full = Renderer.Context.GitWorkflow.failureMessage locked

                    // The recovery text is part of the full message and the library message alone lacks it.
                    Vitest.expect(full.Length > locked.Message.Length).toBe true
                    Vitest.expect(failure.AffectedPaths.Length).toBe 0
                    Vitest.expect((linesOf message).Length).toBe 2
                    Vitest.expect(message.Contains full).toBe true
                    Vitest.expect(technical).toEqual (Some(String.concat "\n\n" details))
        )

        Vitest.test (
            "keeps the failure message and has no technical details when the failure lists no entries",
            fun () ->
                let failure = {
                    Message = "The service is unavailable. Try again."
                    AffectedPaths = [||]
                    Details = [||]
                }

                let message, technical = folderFailureText "data" "freed" 4 failure

                Vitest.expect((linesOf message).Length).toBe 2
                Vitest.expect(message.Contains failure.Message).toBe true
                Vitest.expect(technical).toEqual None
        )

        Vitest.test (
            "shows the failure message for an invalid path, which names the path and has no entries",
            fun () ->
                let invalid = {
                    failureWith [||] with
                        Category = FailureCategoryDto.Validation
                        Code = VersionControlCodes.InvalidPath
                        Message = "The path is not valid."
                        AffectedPaths = [| "data/a.bin" |]
                }

                let message, technical = failureTextFor invalid 4

                Vitest.expect((linesOf message).Length).toBe 2
                Vitest.expect(message.Contains(Renderer.Context.GitWorkflow.failureMessage invalid)).toBe true
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

        Vitest.test (
            "download sends the paths with normalized separators to materializeObjects only",
            fun () -> promise {
                materializeCalls.Clear()
                dematerializeCalls.Clear()

                let! result = FolderLfsAction.download.RunBatch "op-download" [| "data\\a.bin"; "data/b.bin" |]

                Vitest.expect(result).toEqual (Ok())
                Vitest.expect(materializeCalls.Count).toBe 1
                Vitest.expect(materializeCalls.[0].OperationId).toBe "op-download"
                Vitest.expect(materializeCalls.[0].Paths).toEqual [| "data/a.bin"; "data/b.bin" |]
                Vitest.expect(dematerializeCalls.Count).toBe 0
            }
        )

        Vitest.test (
            "free sends the paths with normalized separators to dematerializeObjects only",
            fun () -> promise {
                materializeCalls.Clear()
                dematerializeCalls.Clear()

                let! result = FolderLfsAction.free.RunBatch "op-free" [| "data\\a.bin"; "data/b.bin" |]

                Vitest.expect(result).toEqual (Ok())
                Vitest.expect(dematerializeCalls.Count).toBe 1
                Vitest.expect(dematerializeCalls.[0].OperationId).toBe "op-free"
                Vitest.expect(dematerializeCalls.[0].Paths).toEqual [| "data/a.bin"; "data/b.bin" |]
                Vitest.expect(materializeCalls.Count).toBe 0
            }
        )
)
