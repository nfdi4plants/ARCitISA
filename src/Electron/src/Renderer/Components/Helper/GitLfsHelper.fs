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

/// Why a batch did not finish for every path.
type BatchFailure = {
    /// The failure message with its recovery text. It carries the whole story when the failure names no paths.
    Message: string
    /// The number of paths the library named as failed.
    FailedCount: int
    /// One entry per failed path, complete. An entry can span several lines, for example the raw git-lfs output.
    Details: string[]
}

/// A batch result is a success only when every path succeeded. A partial result stays an Error because the
/// failed paths would otherwise go unreported.
let toBatchResult (result: Result<OperationResultDto<unit>, string>) : Result<unit, BatchFailure> =
    let describe (failure: OperationFailureDto) =
        Error {
            Message = Renderer.Context.GitWorkflow.failureMessage failure
            FailedCount = failure.AffectedPaths.Length
            Details = failure.Details
        }

    match result with
    | Error message ->
        Error {
            Message = message
            FailedCount = 0
            Details = [||]
        }
    | Ok(OperationResultDto.Succeeded _) -> Ok()
    | Ok(OperationResultDto.PartiallySucceeded(_, failure)) -> describe failure
    | Ok(OperationResultDto.Failed failure) -> describe failure

/// The message and the technical details of the modal for a failed folder action. A failure that names paths gets
/// a count line and the first line of each entry, capped, and the technical details hold every entry in full.
/// A failure without entries (the service is unavailable, a path is invalid) keeps its own message.
let folderFailureText
    (folderName: string)
    (outcome: string)
    (totalCount: int)
    (failure: BatchFailure)
    : string * string option =
    let header = $"Not every file in '{folderName}' was processed."

    if failure.Details.Length = 0 then
        $"{header}\n{failure.Message}", None
    else
        let firstLine (entry: string) = entry.Split('\n').[0].TrimEnd('\r')

        let listed =
            failure.Details |> Array.truncate maxListedDetails |> Array.map firstLine

        let more =
            if failure.Details.Length > maxListedDetails then
                [|
                    $"... and {failure.Details.Length - maxListedDetails} more."
                |]
            else
                [||]

        let count = $"{failure.FailedCount} of {totalCount} files could not be {outcome}."

        System.String.Join("\n", Array.concat [ [| header; count |]; listed; more ]),
        // An entry can span several lines, so a blank line separates the entries.
        Some(System.String.Join("\n\n", failure.Details))

/// Downloads all given files with one call. Paths that are already on disk count as done.
let runDownloadLfsFiles (relativePaths: string[]) : JS.Promise<Result<unit, BatchFailure>> = promise {
    let! result =
        Renderer.VersionControlApiClient.materializeObjects {
            OperationId = operationId ()
            Paths = relativePaths |> Array.map PathHelpers.normalizeSeparators
        }

    return toBatchResult result
}

/// Frees the local copies of all given files with one call.
let runFreeLocalLfsCopies (relativePaths: string[]) : JS.Promise<Result<unit, BatchFailure>> = promise {
    let! result =
        Renderer.VersionControlApiClient.dematerializeObjects {
            OperationId = operationId ()
            Paths = relativePaths |> Array.map PathHelpers.normalizeSeparators
        }

    return toBatchResult result
}

/// One of the two bulk actions on the Git LFS files of a folder.
type FolderLfsAction = {
    /// The busy label of the rows while the action runs.
    Activity: string
    /// The title of the modal that reports a failure.
    FailureTitle: string
    /// The word for the count line of the failure text, as in "3 of 9 files could not be downloaded".
    Outcome: string
    /// The files of the folder that this action takes.
    SelectPaths: Renderer.Context.LargeObjectStore.FolderLargeObjects -> string[]
    RunBatch: string[] -> JS.Promise<Result<unit, BatchFailure>>
}

module FolderLfsAction =

    /// Downloads the files whose content is not on disk.
    let download: FolderLfsAction = {
        Activity = "Downloading"
        FailureTitle = "Git LFS download failed"
        Outcome = "downloaded"
        SelectPaths = _.NeedsDownload
        RunBatch = runDownloadLfsFiles
    }

    /// Frees the local copies of the files whose content is on disk.
    let free: FolderLfsAction = {
        Activity = "Freeing"
        FailureTitle = "Git LFS cleanup failed"
        Outcome = "freed"
        SelectPaths = _.HasLocalCopy
        RunBatch = runFreeLocalLfsCopies
    }
