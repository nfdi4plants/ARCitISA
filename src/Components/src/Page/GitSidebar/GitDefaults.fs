namespace Arcitisa.Components.Page.GitSidebarTypes

/// The git settings a user chooses once and every ARC opened afterwards starts with.
type SettingsGitDefaults = {
    LfsAutoTrackThresholdMb: int
    DownloadLargeFiles: bool
    DiffIndexingLimitMb: int
    UploadRightAway: bool
}

/// The allowed range of the numeric git settings in MB. The git sidebar and the settings page use it.
/// The ranges mirror VersionControlSettings in the main process, which keeps its own copy.
module GitSettingRanges =

    [<Literal>]
    let MinLfsAutoTrackThresholdMb = 1

    [<Literal>]
    let MaxLfsAutoTrackThresholdMb = 100

    /// A limit below 64 is the memory budget of a diff held in memory.
    [<Literal>]
    let MinDiffIndexingLimitMb = 1

    [<Literal>]
    let MaxDiffIndexingLimitMb = 1048576
