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
    /// The paths the library named as failed. When Details has as many entries, entry i belongs to path i.
    AffectedPaths: string[]
    /// One entry per failed path, complete. An entry can span several lines, for example the raw git-lfs output.
    Details: string[]
}

/// A batch result is a success only when every path succeeded. A partial result that names failed paths stays an
/// Error because those paths would otherwise go unreported. A partial result without paths is the host reporting a
/// successful call on a freshly opened session, which GitWorkflow.toUnitResult also treats as a success.
let toBatchResult (result: Result<OperationResultDto<unit>, string>) : Result<unit, BatchFailure> =
    let describe (failure: OperationFailureDto) =
        Error {
            Message = Renderer.Context.GitWorkflow.failureMessage failure
            AffectedPaths = failure.AffectedPaths
            Details = failure.Details
        }

    match result with
    | Error message ->
        Error {
            Message = message
            AffectedPaths = [||]
            Details = [||]
        }
    | Ok(OperationResultDto.Succeeded _) -> Ok()
    | Ok(OperationResultDto.PartiallySucceeded(_, failure)) when failure.AffectedPaths.Length = 0 -> Ok()
    | Ok(OperationResultDto.PartiallySucceeded(_, failure)) -> describe failure
    | Ok(OperationResultDto.Failed failure) -> describe failure

/// The path and the message of entry i. The library writes one "<path>: <message>" entry per failed path in the
/// order of the paths, so equal lengths give the path exactly, even when it contains ": ". With different lengths
/// the path is the text before the first ": " of the first line, if the entry has one.
let private splitEntry (affectedPaths: string[]) (details: string[]) (index: int) : string option * string =
    let entry = details.[index]

    if affectedPaths.Length = details.Length then
        let path = affectedPaths.[index]
        let prefix = path + ": "

        let message =
            if entry.StartsWith(prefix, System.StringComparison.Ordinal) then
                entry.Substring prefix.Length
            else
                entry

        Some path, message
    else
        let separator = entry.IndexOf ": "
        let lineEnd = entry.IndexOf '\n'

        if separator >= 0 && (lineEnd < 0 || separator < lineEnd) then
            Some(entry.Substring(0, separator)), entry.Substring(separator + 2)
        else
            None, entry

/// The progress text of git-lfs and its generic trailer. Neither names the cause of the failure. Older git-lfs
/// versions put "fetch: " in front of the progress text.
let private isGitLfsNoise (text: string) =
    let text =
        if text.StartsWith("fetch: ", System.StringComparison.Ordinal) then
            text.Substring 7
        else
            text

    text.StartsWith("Fetching reference", System.StringComparison.Ordinal)
    || text.StartsWith("error: failed to fetch some objects", System.StringComparison.Ordinal)

/// The text before the noise when a line ends in ": " and a noise segment, as in
/// "Could not confirm that the remote has 'a.bin': Fetching reference refs/heads/main".
let private textBeforeNoiseTail (line: string) : string option =
    let rec search (offset: int) =
        let separator = line.Substring(offset).IndexOf ": "

        if separator < 0 then
            None
        else
            let position = offset + separator

            if isGitLfsNoise (line.Substring(position + 2)) then
                Some(line.Substring(0, position))
            else
                search (position + 2)

    search 0

/// One entry of the failure text, the path followed by one line of its message. A one-line message stays as it is.
/// A multi-line message (the raw git-lfs output of a path) shows its first line that is not git-lfs noise, and its
/// last non-empty line when only noise remains. When the first line ends in noise, the text before the noise stays
/// in front of the cause line.
let private entrySummary (affectedPaths: string[]) (details: string[]) (index: int) =
    let path, message = splitEntry affectedPaths details index

    let lines =
        message.Split('\n')
        |> Array.map (fun line -> line.TrimEnd('\r'))
        |> Array.filter (System.String.IsNullOrWhiteSpace >> not)

    let shown =
        if lines.Length = 0 then
            message
        elif lines.Length = 1 then
            lines.[0]
        else
            let prefix =
                if isGitLfsNoise lines.[0] then
                    None
                else
                    textBeforeNoiseTail lines.[0]

            let withPrefix (text: string) =
                match prefix with
                | Some prefix -> $"{prefix}: {text}"
                | None -> text

            // The prefix already holds what the first line says before its noise tail, so the cause comes from the later lines.
            let candidates = if prefix.IsSome then Array.skip 1 lines else lines

            match candidates |> Array.tryFind (isGitLfsNoise >> not) with
            | Some cause -> withPrefix cause
            | None -> withPrefix lines.[lines.Length - 1]

    match path with
    | Some path -> $"{path}: {shown}"
    | None -> shown

/// The message and the technical details of the modal for a failed folder action. A failure with failed paths and
/// details gets a count line and one summary line per entry, capped, and the technical details hold every entry in
/// full. A failure without either shows the header and the failure's own message, which holds the cause and the
/// recovery text. Any entries it carries go into the technical details.
let folderFailureText
    (folderName: string)
    (outcome: string)
    (totalCount: int)
    (failure: BatchFailure)
    : string * string option =
    let header = $"Not every file in '{folderName}' was processed."

    // An entry can span several lines, so a blank line separates the entries.
    let technical =
        if failure.Details.Length = 0 then
            None
        else
            Some(System.String.Join("\n\n", failure.Details))

    if failure.AffectedPaths.Length > 0 && failure.Details.Length > 0 then
        let listed = [|
            for index in 0 .. (min failure.Details.Length maxListedDetails) - 1 ->
                entrySummary failure.AffectedPaths failure.Details index
        |]

        let more =
            if failure.Details.Length > maxListedDetails then
                [|
                    $"... and {failure.Details.Length - maxListedDetails} more."
                |]
            else
                [||]

        let count =
            $"{failure.AffectedPaths.Length} of {totalCount} files could not be {outcome}."

        System.String.Join("\n", Array.concat [ [| header; count |]; listed; more ]), technical
    else
        $"{header}\n{failure.Message}", technical

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
