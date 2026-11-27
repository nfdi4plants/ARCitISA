module ElectronCore.UserDataMoveTests

open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.SettingsStore
open Vitest

/// Runs the test with the paths of the 3.x folder (Swate) and the 4.x folder (ARCitISA) in a new
/// temporary folder that stands in for %AppData%. Neither folder exists at the start.
let private withAppData (testBody: string -> string -> JS.Promise<unit>) = promise {
    let! appData = TestHelpers.createTempDirectoryAsync "arcitisa-user-data-"

    try
        do! testBody (join [| appData; "Swate" |]) (join [| appData; "ARCitISA" |])
        do! TestHelpers.removeDirectoryAsync appData
    with error ->
        do! TestHelpers.removeDirectoryAsync appData
        return raise error
}

let private writeFile (folder: string) (relativePath: string) (content: string) =
    let path = join [| folder; relativePath |]
    mkdirSync (dirname path) (MkdirOptions(recursive = true))
    writeFileSync path content TextEncoding.Utf8

let private readFile (folder: string) (relativePath: string) =
    readFileSync (join [| folder; relativePath |]) TextEncoding.Utf8

/// What the app does at start, outside a test profile and in a packaged app.
let private moveAsInstalledApp (legacyPath: string) (currentPath: string) =
    let move =
        readUserDataFolders legacyPath currentPath |> decideUserDataMove false true

    move, applyUserDataMove move legacyPath currentPath

let private folders legacy current settings = {
    LegacyExists = legacy
    CurrentExists = current
    CurrentHasSettings = settings
}

Vitest.describe (
    "User data move from Swate to ARCitISA",
    fun () ->
        Vitest.test (
            "decides by the two folders",
            fun () ->
                Vitest.expect(decideUserDataMove false true (folders false false false)).toEqual KeepAsIs
                Vitest.expect(decideUserDataMove false true (folders false true true)).toEqual KeepAsIs
                Vitest.expect(decideUserDataMove false true (folders true false false)).toEqual RenameLegacy
                Vitest.expect(decideUserDataMove false true (folders true true false)).toEqual ReplaceCurrentWithLegacy
                Vitest.expect(decideUserDataMove false true (folders true true true)).toEqual DeleteLegacy
        )

        Vitest.test (
            "a test profile and a development run keep both folders",
            fun () ->
                for state in
                    [
                        folders true false false
                        folders true true false
                        folders true true true
                    ] do
                    Vitest.expect(decideUserDataMove true true state).toEqual KeepAsIs
                    Vitest.expect(decideUserDataMove false false state).toEqual KeepAsIs
        )

        Vitest.test (
            "after a 3.x install, the folder it left in ARCitISA gives way to Swate",
            fun () ->
                withAppData (fun legacyPath currentPath -> promise {
                    writeFile legacyPath "Local State" "key of the stored accounts"
                    writeFile legacyPath "Settings/user-defaults.json" "settings of 3.x"
                    writeFile currentPath "Local State" "key of a 3.x Squirrel hook run"

                    let move, result = moveAsInstalledApp legacyPath currentPath

                    Vitest.expect(move).toEqual ReplaceCurrentWithLegacy
                    Vitest.expect(result).toEqual (Ok())
                    Vitest.expect(existsSync legacyPath).toBe false
                    Vitest.expect(readFile currentPath "Local State").toBe "key of the stored accounts"
                    Vitest.expect(readFile currentPath "Settings/user-defaults.json").toBe "settings of 3.x"
                })
        )

        Vitest.test (
            "only Swate: it is renamed to ARCitISA",
            fun () ->
                withAppData (fun legacyPath currentPath -> promise {
                    writeFile legacyPath "Settings/recent-arcs.json" "recent ARCs"

                    let move, result = moveAsInstalledApp legacyPath currentPath

                    Vitest.expect(move).toEqual RenameLegacy
                    Vitest.expect(result).toEqual (Ok())
                    Vitest.expect(existsSync legacyPath).toBe false
                    Vitest.expect(readFile currentPath "Settings/recent-arcs.json").toBe "recent ARCs"
                })
        )

        Vitest.test (
            "ARCitISA in use: Swate, written again by 3.x, is deleted",
            fun () ->
                withAppData (fun legacyPath currentPath -> promise {
                    writeFile currentPath "Settings/user-defaults.json" "settings of 4.x"
                    writeFile legacyPath "Settings/user-defaults.json" "settings of a later 3.x run"

                    let move, result = moveAsInstalledApp legacyPath currentPath

                    Vitest.expect(move).toEqual DeleteLegacy
                    Vitest.expect(result).toEqual (Ok())
                    Vitest.expect(existsSync legacyPath).toBe false
                    Vitest.expect(readFile currentPath "Settings/user-defaults.json").toBe "settings of 4.x"
                })
        )

        Vitest.test (
            "already moved or a fresh install: nothing changes",
            fun () ->
                withAppData (fun legacyPath currentPath -> promise {
                    let freshMove, freshResult = moveAsInstalledApp legacyPath currentPath
                    Vitest.expect(freshMove).toEqual KeepAsIs
                    Vitest.expect(freshResult).toEqual (Ok())
                    Vitest.expect(existsSync currentPath).toBe false

                    writeFile currentPath "Settings/user-defaults.json" "settings of 4.x"
                    let movedMove, movedResult = moveAsInstalledApp legacyPath currentPath
                    Vitest.expect(movedMove).toEqual KeepAsIs
                    Vitest.expect(movedResult).toEqual (Ok())
                    Vitest.expect(readFile currentPath "Settings/user-defaults.json").toBe "settings of 4.x"
                })
        )

        Vitest.test (
            "a failed step returns the error and leaves the other folder alone",
            fun () ->
                withAppData (fun legacyPath currentPath -> promise {
                    writeFile currentPath "Settings/user-defaults.json" "settings of 4.x"

                    // Swate does not exist, so the rename fails.
                    match applyUserDataMove RenameLegacy legacyPath currentPath with
                    | Ok() -> failwith "The rename of a missing folder succeeded."
                    | Error message -> Vitest.expect(message).toContain "ENOENT"

                    Vitest.expect(readFile currentPath "Settings/user-defaults.json").toBe "settings of 4.x"
                })
        )
)
