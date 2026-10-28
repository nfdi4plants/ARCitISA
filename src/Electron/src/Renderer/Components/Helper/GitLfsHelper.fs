/// Large-object actions of the file explorer over the provider-neutral storage
/// policy and materialization services. The file explorer checks the DataHUB ruleset
/// (isa.*.xlsx never in large-object storage, dataset files always) before it calls
/// here, and the main process checks it again.
module Renderer.Components.Helper.GitLfsHelper

open Fable.Core
open Swate.Components.Shared
open Swate.Electron.Shared.VersionControlTypes

let private operationId () =
    Renderer.VersionControlApiClient.newOperationId ()

let private toUnitResult = Renderer.Context.GitWorkflow.toUnitResult

let runToggleLfsMark (relativePath: string) (markAsLfs: bool) : JS.Promise<Result<unit, string>> = promise {
    let! result =
        Renderer.VersionControlApiClient.setPathStoragePolicy {
            OperationId = operationId ()
            Path = PathHelpers.normalizeSeparators relativePath
            UseLargeObjectStorage = markAsLfs
        }

    return toUnitResult result
}

let runFreeLocalLfsCopy (relativePath: string) : JS.Promise<Result<unit, string>> = promise {
    let! result =
        Renderer.VersionControlApiClient.dematerializeObject {
            OperationId = operationId ()
            Path = PathHelpers.normalizeSeparators relativePath
            RefreshTree = None
        }

    return toUnitResult result
}

let runDownloadLfsFile (relativePath: string) : JS.Promise<Result<unit, string>> = promise {
    let! result =
        Renderer.VersionControlApiClient.materializeObject {
            OperationId = operationId ()
            Path = PathHelpers.normalizeSeparators relativePath
            RefreshTree = None
        }

    return toUnitResult result
}

/// The most Details lines a batch failure message lists. The rest collapse into one count line.
let private maxListedDetails = 10

/// A batch result is a success only when every path succeeded. A partial result stays an Error because the
/// failed paths would otherwise go unreported. The message is the failure message followed by the Details lines
/// (one per failed path), capped at maxListedDetails.
let toBatchResult (result: Result<OperationResultDto<unit>, string>) : Result<unit, string> =
    let describe (failure: OperationFailureDto) =
        let listed = failure.Details |> Array.truncate maxListedDetails

        let more =
            if failure.Details.Length > maxListedDetails then
                [|
                    $"... and {failure.Details.Length - maxListedDetails} more."
                |]
            else
                [||]

        Error(
            System.String.Join(
                "\n",
                Array.concat [
                    [| Renderer.Context.GitWorkflow.failureMessage failure |]
                    listed
                    more
                ]
            )
        )

    match result with
    | Error message -> Error message
    | Ok(OperationResultDto.Succeeded _) -> Ok()
    | Ok(OperationResultDto.PartiallySucceeded(_, failure)) -> describe failure
    | Ok(OperationResultDto.Failed failure) -> describe failure

/// Downloads all given files with one call. Paths that are already on disk count as done.
let runDownloadLfsFiles (relativePaths: string[]) : JS.Promise<Result<unit, string>> = promise {
    let! result =
        Renderer.VersionControlApiClient.materializeObjects {
            OperationId = operationId ()
            Paths = relativePaths |> Array.map PathHelpers.normalizeSeparators
        }

    return toBatchResult result
}

/// Frees the local copies of all given files with one call.
let runFreeLocalLfsCopies (relativePaths: string[]) : JS.Promise<Result<unit, string>> = promise {
    let! result =
        Renderer.VersionControlApiClient.dematerializeObjects {
            OperationId = operationId ()
            Paths = relativePaths |> Array.map PathHelpers.normalizeSeparators
        }

    return toBatchResult result
}
