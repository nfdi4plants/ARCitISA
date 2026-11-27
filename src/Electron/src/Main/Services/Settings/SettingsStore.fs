[<AutoOpen>]
module Main.SettingsStore

open Main.Bindings.Filesystem
open Main.Bindings.Path


[<Literal>]
let appSettingsFolderName = "Settings"

// If appSettingsFolderName is changed in a future release, migrate or delete the old
// folder inside app.getPath("userData") to avoid stale or split settings.

open Fable.Electron.Main

/// The user data folders at start: the one of 3.x (Swate) and Electron's default one, which is
/// named after the product (ARCitISA). Only ARCitISA writes a Settings folder, so a 4.x folder
/// without one holds nothing of the user: a 3.x Squirrel install leaves Chromium's Local State there.
type UserDataFolders = {
    LegacyExists: bool
    CurrentExists: bool
    CurrentHasSettings: bool
}

/// What has to happen to the user data folders before the app may use them.
type UserDataMove =
    /// Nothing to move: a fresh install, an earlier move, a test profile or a development run.
    | KeepAsIs
    /// Rename the 3.x folder to the 4.x name.
    | RenameLegacy
    /// Delete the 4.x folder (only what a 3.x install left there), then rename the 3.x folder.
    | ReplaceCurrentWithLegacy
    /// The 4.x folder is in use, so the 3.x folder is left from going back to 3.x: delete it.
    | DeleteLegacy

/// Decides the move. A user-data-dir switch (a test profile) and a development run, which is not
/// packaged, keep everything as it is: a development machine has its own ARCitISA folder, and
/// deciding by it would delete the Swate folder of the installed 3.x.
let decideUserDataMove (hasUserDataDirSwitch: bool) (isPackaged: bool) (folders: UserDataFolders) : UserDataMove =
    if hasUserDataDirSwitch || not isPackaged || not folders.LegacyExists then
        KeepAsIs
    elif not folders.CurrentExists then
        RenameLegacy
    elif folders.CurrentHasSettings then
        DeleteLegacy
    else
        ReplaceCurrentWithLegacy

let readUserDataFolders (legacyPath: string) (currentPath: string) : UserDataFolders = {
    LegacyExists = existsSync legacyPath
    CurrentExists = existsSync currentPath
    CurrentHasSettings = existsSync (join [| currentPath; appSettingsFolderName |])
}

/// Moves the folders as a whole, never file by file: on Windows, Local State holds the key that
/// encrypts the stored account tokens. No retry: a failed step is safe to repeat at the next start,
/// and the error names what went wrong.
let applyUserDataMove (move: UserDataMove) (legacyPath: string) (currentPath: string) : Result<unit, string> =
    try
        match move with
        | KeepAsIs -> ()
        | RenameLegacy -> renameSync legacyPath currentPath
        | ReplaceCurrentWithLegacy ->
            rmSync currentPath (RmOptions(recursive = true, force = true))
            renameSync legacyPath currentPath
        | DeleteLegacy -> rmSync legacyPath (RmOptions(recursive = true, force = true))

        Ok()
    with error ->
        Error error.Message

/// The user data folder of ARCitISA up to 3.x, from the time the app was called Swate.
[<Literal>]
let private legacyUserDataFolderName = "Swate"

/// Moves the user data of 3.x (Swate) to Electron's default folder (ARCitISA). It has to run before
/// the app is ready and before anything reads the user data folder. The error text names both
/// folders, for the message the app shows before it quits.
let moveLegacyUserDataFolder () : Result<unit, string> =
    let legacyPath =
        join [|
            app.getPath Enums.App.GetPath.Name.AppData
            legacyUserDataFolderName
        |]

    let currentPath = app.getPath Enums.App.GetPath.Name.UserData

    let move =
        readUserDataFolders legacyPath currentPath
        |> decideUserDataMove (app.commandLine.hasSwitch "user-data-dir") app.isPackaged

    applyUserDataMove move legacyPath currentPath
    |> Result.mapError (fun message ->
        $"Old settings folder: {legacyPath}\nNew settings folder: {currentPath}\n\n{message}\n\nClose all ARCitISA windows, including older versions, and start ARCitISA again."
    )

let getSettingsRootPath () =
    let userDataPath = app.getPath Enums.App.GetPath.Name.UserData
    /// Something like: C:\Users\Kevin\AppData\Roaming\ARCitISA\Settings
    let settingsRootPath = join [| userDataPath; appSettingsFolderName |]

    mkdirSync settingsRootPath (MkdirOptions(recursive = true))
    settingsRootPath

let getSettingsFilePath (fileName: string) =
    let settingsRootPath = getSettingsRootPath ()
    join [| settingsRootPath; fileName |]

let tryReadSettingsFile (fileName: string) =
    try
        let filePath = getSettingsFilePath fileName
        let exists = existsSync filePath

        if not exists then
            None
        else
            Some(readFileSync filePath TextEncoding.Utf8)
    with _ ->
        None

/// Writes through a temp file and rename. The result names the failure instead of
/// hiding it, for callers that have to know whether the file was written.
let tryWriteSettingsFileAtomic (fileName: string) (content: string) : Result<unit, string> =
    try
        let filePath = getSettingsFilePath fileName
        let tempPath = filePath + ".tmp"
        writeFileSync tempPath content TextEncoding.Utf8
        renameSync tempPath filePath
        Ok()
    with error ->
        Error $"Could not write settings file '{fileName}': {error.Message}"

let writeSettingsFileAtomic (fileName: string) (content: string) =
    tryWriteSettingsFileAtomic fileName content |> ignore
