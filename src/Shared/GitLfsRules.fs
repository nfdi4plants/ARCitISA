namespace Arcitisa.Components.Shared

open System

/// DataHub ruleset for Git LFS tracking (nfdi4plants/ARCitISA#1316):
/// isa.*.xlsx metadata files must never be LFS, files inside a dataset folder
/// and files larger than 25 MB must stay LFS.
[<RequireQualifiedAccess>]
module GitLfsRules =

    [<Literal>]
    let MaxNonLfsFileSizeMb = 25

    let maxNonLfsFileSizeBytes = int64 MaxNonLfsFileSizeMb * 1024L * 1024L

    let private isaFileNamePrefix = "isa."
    let private isaFileNameSuffix = ".xlsx"

    /// Matches isa.*.xlsx metadata file names (e.g. isa.investigation.xlsx, isa.study.xlsx).
    let isIsaMetadataFile (relativePath: string) =
        if String.IsNullOrWhiteSpace relativePath then
            false
        else
            let fileName =
                relativePath |> PathHelpers.normalizeRelativePath |> PathHelpers.getFileName

            fileName.Length > isaFileNamePrefix.Length + isaFileNameSuffix.Length
            && fileName.StartsWith(isaFileNamePrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(isaFileNameSuffix, StringComparison.OrdinalIgnoreCase)

    /// Matches files below a dataset folder anywhere in the relative path.
    let isInDatasetFolder (relativePath: string) =
        if String.IsNullOrWhiteSpace relativePath then
            false
        else
            let segments =
                relativePath
                |> PathHelpers.normalizeRelativePath
                |> fun path -> path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)

            segments.Length > 1
            && segments.[.. segments.Length - 2]
               |> Array.exists (fun segment ->
                   PathHelpers.pathsEqual segment ARCtrl.ArcPathHelper.AssayDatasetFolderName
               )

    let exceedsNonLfsSizeLimit (sizeInBytes: int64 option) =
        sizeInBytes |> Option.exists (fun size -> size > maxNonLfsFileSizeBytes)

    /// Explains why a file must not be marked as Git LFS, if a rule forbids it.
    let tryGetMarkAsLfsBlockedReason (relativePath: string) =
        if isIsaMetadataFile relativePath then
            let fileName = PathHelpers.getFileName relativePath
            Some $"'{fileName}' is an ARC metadata file (isa.*.xlsx) and must not be tracked with Git LFS."
        else
            None

    /// Explains why a file must stay marked as Git LFS, if a rule forbids unmarking it.
    let tryGetUnmarkAsLfsBlockedReason (relativePath: string) (sizeInBytes: int64 option) =
        if isIsaMetadataFile relativePath then
            None
        elif isInDatasetFolder relativePath then
            let fileName = PathHelpers.getFileName relativePath
            Some $"'{fileName}' is inside a dataset folder and must stay tracked with Git LFS."
        elif exceedsNonLfsSizeLimit sizeInBytes then
            let fileName = PathHelpers.getFileName relativePath
            Some $"'{fileName}' is larger than {MaxNonLfsFileSizeMb} MB and must stay tracked with Git LFS."
        else
            None

    /// Explains why toggling the Git LFS mark in the given direction is not allowed, if any rule forbids it.
    let tryGetToggleBlockedReason (relativePath: string) (sizeInBytes: int64 option) (markAsLfs: bool) =
        if markAsLfs then
            tryGetMarkAsLfsBlockedReason relativePath
        else
            tryGetUnmarkAsLfsBlockedReason relativePath sizeInBytes
