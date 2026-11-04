/// User overrides of the defaults that every new ARC session starts from. The overrides live in
/// user-defaults.json in the settings folder. Only values that differ from the built-in ones are
/// stored, so a profile without overrides behaves exactly like a fresh install.
module Main.UserDefaults

open System
open Fable.Core
open Thoth.Json.Core
open Main.VersionControl.VersionControlSettings
open Swate.Electron.Shared.IPCTypes

[<Literal>]
let userDefaultsFileName = "user-defaults.json"

/// A new optional field needs no version bump. An older Swate ignores fields it does not know and
/// drops them when it saves. Bump the version only for a change that older versions must not read.
[<Literal>]
let CurrentSchemaVersion = 1

/// The values Swate ships with. The git values come from VersionControlSettings.defaults.
let builtIn: UserDefaultValuesDto = {
    LfsAutoTrackThresholdMb = VersionControlSettings.defaults.AutoTrackThresholdMb
    DownloadLargeFiles = VersionControlSettings.defaults.DownloadLargeFiles
    DiffIndexingLimitMb = VersionControlSettings.defaults.DiffIndexingLimitMb
    UploadRightAway = true
    AutoCreateNotesFolder = true
    GitExecutablePath = None
}

let toVersionControlSettings (values: UserDefaultValuesDto) : VersionControlSettings = {
    AutoTrackThresholdMb = values.LfsAutoTrackThresholdMb
    DownloadLargeFiles = values.DownloadLargeFiles
    DiffIndexingLimitMb = values.DiffIndexingLimitMb
}

/// One entry per value. None means the user has not changed it.
type UserDefaultOverrides = {
    LfsAutoTrackThresholdMb: int option
    DownloadLargeFiles: bool option
    DiffIndexingLimitMb: int option
    UploadRightAway: bool option
    AutoCreateNotesFolder: bool option
    /// The built-in value is None, so a Some here is the override itself.
    GitExecutablePath: string option
}

let private noOverrides: UserDefaultOverrides = {
    LfsAutoTrackThresholdMb = None
    DownloadLargeFiles = None
    DiffIndexingLimitMb = None
    UploadRightAway = None
    AutoCreateNotesFolder = None
    GitExecutablePath = None
}

let private thresholdInRange (value: int) =
    value >= VersionControlSettings.MinAutoTrackThresholdMb
    && value <= VersionControlSettings.MaxAutoTrackThresholdMb

let private diffLimitInRange (value: int) =
    value >= VersionControlSettings.MinDiffIndexingLimitMb
    && value <= VersionControlSettings.MaxDiffIndexingLimitMb

/// Ok when every value is within its allowed range. The message says in plain words which setting
/// is out of range, and it is shown on the settings page.
let validate (values: UserDefaultValuesDto) : Result<UserDefaultValuesDto, string> =
    match VersionControlSettings.validate (toVersionControlSettings values) with
    | Ok _ -> Ok values
    | Error(_, message) -> Error message

type private StoredDefaults =
    /// Written by a newer Swate. The file is ignored and never overwritten.
    | NewerSchema
    | Stored of UserDefaultOverrides

/// A value of the wrong type counts as absent, so one damaged entry does not hide the others.
let private tolerant (decoder: Decoder<'T>) : Decoder<'T option> =
    Decode.oneOf [ Decode.map Some decoder; Decode.succeed None ]

let private storedDecoder: Decoder<StoredDefaults> =
    Decode.field "schemaVersion" Decode.int
    |> Decode.andThen (fun version ->
        if version > CurrentSchemaVersion then
            Decode.succeed NewerSchema
        elif version < 1 then
            Decode.fail $"Unknown user defaults schema version {version}."
        else
            Decode.object (fun get ->
                // An out-of-range value falls back to the built-in one, field by field.
                Stored {
                    LfsAutoTrackThresholdMb =
                        get.Optional.Field "lfsAutoTrackThresholdMb" (tolerant Decode.int)
                        |> Option.bind id
                        |> Option.filter thresholdInRange
                    DownloadLargeFiles =
                        get.Optional.Field "downloadLargeFiles" (tolerant Decode.bool) |> Option.bind id
                    DiffIndexingLimitMb =
                        get.Optional.Field "diffIndexingLimitMb" (tolerant Decode.int)
                        |> Option.bind id
                        |> Option.filter diffLimitInRange
                    UploadRightAway = get.Optional.Field "uploadRightAway" (tolerant Decode.bool) |> Option.bind id
                    AutoCreateNotesFolder =
                        get.Optional.Field "autoCreateNotesFolder" (tolerant Decode.bool)
                        |> Option.bind id
                    // A blank path counts as absent. A path that no longer exists stays, so the
                    // dependency check can say that the chosen Git is gone.
                    GitExecutablePath =
                        get.Optional.Field "gitExecutablePath" (tolerant Decode.string)
                        |> Option.bind id
                        |> Option.filter (String.IsNullOrWhiteSpace >> not)
                }
            )
    )

/// Unreadable content yields the built-in values.
let private parse (content: string) : StoredDefaults =
    try
        if String.IsNullOrWhiteSpace content then
            Stored noOverrides
        else
            ARCtrl.Json.Decode.fromJsonString storedDecoder content
    with _ ->
        Stored noOverrides

let private serialize (overrides: UserDefaultOverrides) : string =
    JS.JSON.stringify (
        {|
            schemaVersion = CurrentSchemaVersion
            lfsAutoTrackThresholdMb = overrides.LfsAutoTrackThresholdMb
            downloadLargeFiles = overrides.DownloadLargeFiles
            diffIndexingLimitMb = overrides.DiffIndexingLimitMb
            uploadRightAway = overrides.UploadRightAway
            autoCreateNotesFolder = overrides.AutoCreateNotesFolder
            gitExecutablePath = overrides.GitExecutablePath
        |},
        space = 2
    )

let private effectiveOf (overrides: UserDefaultOverrides) : UserDefaultValuesDto = {
    LfsAutoTrackThresholdMb =
        overrides.LfsAutoTrackThresholdMb
        |> Option.defaultValue builtIn.LfsAutoTrackThresholdMb
    DownloadLargeFiles = overrides.DownloadLargeFiles |> Option.defaultValue builtIn.DownloadLargeFiles
    DiffIndexingLimitMb = overrides.DiffIndexingLimitMb |> Option.defaultValue builtIn.DiffIndexingLimitMb
    UploadRightAway = overrides.UploadRightAway |> Option.defaultValue builtIn.UploadRightAway
    AutoCreateNotesFolder =
        overrides.AutoCreateNotesFolder
        |> Option.defaultValue builtIn.AutoCreateNotesFolder
    GitExecutablePath = overrides.GitExecutablePath |> Option.orElse builtIn.GitExecutablePath
}

/// A value equal to the built-in one is no override.
let private overridesOf (values: UserDefaultValuesDto) : UserDefaultOverrides =
    let differing (value: 'T) (builtInValue: 'T) =
        if value = builtInValue then None else Some value

    {
        LfsAutoTrackThresholdMb = differing values.LfsAutoTrackThresholdMb builtIn.LfsAutoTrackThresholdMb
        DownloadLargeFiles = differing values.DownloadLargeFiles builtIn.DownloadLargeFiles
        DiffIndexingLimitMb = differing values.DiffIndexingLimitMb builtIn.DiffIndexingLimitMb
        UploadRightAway = differing values.UploadRightAway builtIn.UploadRightAway
        AutoCreateNotesFolder = differing values.AutoCreateNotesFolder builtIn.AutoCreateNotesFolder
        // The built-in path is None, so any chosen path is an override.
        GitExecutablePath = values.GitExecutablePath
    }

type IUserDefaultsStore =
    /// The values a new session starts from. Every call reads the file, so a change that another
    /// Swate process on the same profile saved is seen at once.
    abstract Effective: unit -> UserDefaultValuesDto
    /// Validates the values and stores those that differ from the built-in ones. The Error names
    /// the refused setting, or says why the file could not be written. Nothing changes on an Error.
    abstract Update: UserDefaultValuesDto -> Result<UserDefaultValuesDto, string>

/// A store over caller-supplied read and write functions, so tests and the settings folder share one
/// implementation. A throwing reader counts as a missing file and a throwing writer as a failed write.
let create (read: unit -> string option) (write: string -> Result<unit, string>) : IUserDefaultsStore =
    let load () =
        try
            match read () with
            | Some content -> parse content
            | None -> Stored noOverrides
        with _ ->
            Stored noOverrides

    { new IUserDefaultsStore with
        member _.Effective() =
            match load () with
            | NewerSchema -> effectiveOf noOverrides
            | Stored overrides -> effectiveOf overrides

        member _.Update values =
            match validate values with
            | Error message -> Error message
            | Ok values ->
                match load () with
                | NewerSchema ->
                    Error
                        "The saved defaults were written by a newer version of ARCitISA. They stay as they are until ARCitISA is updated."
                | Stored _ ->
                    let overrides = overridesOf values

                    let written =
                        try
                            write (serialize overrides)
                        with error ->
                            Error $"Could not write the user defaults: {error.Message}"

                    written |> Result.map (fun () -> effectiveOf overrides)
    }

/// The store in the application settings folder.
let createSettingsStore () : IUserDefaultsStore =
    create
        (fun () -> SettingsStore.tryReadSettingsFile userDefaultsFileName)
        (fun content -> SettingsStore.tryWriteSettingsFileAtomic userDefaultsFileName content)

/// The store of the running app. It reads the settings folder only when called, after the app is ready.
let production: IUserDefaultsStore = createSettingsStore ()

/// What a new session starts from.
type SessionDefaults = {
    InitialSettings: VersionControlSettings
    InitialUploadRightAway: bool
}

let sessionDefaultsOf (store: IUserDefaultsStore) : SessionDefaults =
    let values = store.Effective()

    {
        InitialSettings = toVersionControlSettings values
        InitialUploadRightAway = values.UploadRightAway
    }

/// The session defaults of a profile without overrides.
let builtInSessionDefaults () : SessionDefaults = {
    InitialSettings = toVersionControlSettings builtIn
    InitialUploadRightAway = builtIn.UploadRightAway
}
