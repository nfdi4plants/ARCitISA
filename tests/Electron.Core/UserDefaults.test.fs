module ElectronCore.UserDefaultsTests

open Fable.Core
open Main
open Vitest

/// A store over a file held in memory. The counter tells how often the store wrote it.
type private MemoryFile = {
    Content: string option ref
    Writes: int ref
}

let private memoryFile (initial: string option) : MemoryFile = {
    Content = ref initial
    Writes = ref 0
}

let private storeWithWriter (file: MemoryFile) (write: string -> Result<unit, string>) =
    UserDefaults.create (fun () -> file.Content.Value) write

let private storeOver (file: MemoryFile) =
    storeWithWriter
        file
        (fun next ->
            file.Writes.Value <- file.Writes.Value + 1
            file.Content.Value <- Some next
            Ok()
        )

let private expectUpdated store values =
    match (store: UserDefaults.IUserDefaultsStore).Update values with
    | Ok effective -> effective
    | Error message -> failwith message

let private expectRefused store values =
    match (store: UserDefaults.IUserDefaultsStore).Update values with
    | Ok _ -> failwith "The values were stored although they are invalid."
    | Error message -> message

let private storedKeys (file: MemoryFile) =
    let content =
        file.Content.Value
        |> Option.defaultWith (fun () -> failwith "No file was written.")

    JS.Constructors.Object.keys (JS.JSON.parse content) |> Seq.sort |> Seq.toArray

let private newerSchemaFile =
    """{ "schemaVersion": 2, "lfsAutoTrackThresholdMb": 50 }"""

Vitest.describe (
    "User defaults store",
    fun () ->
        Vitest.test (
            "a profile without a file starts from the built-in values",
            fun () ->
                let store = storeOver (memoryFile None)

                Vitest.expect(store.Effective()).toEqual UserDefaults.builtIn
                Vitest.expect(UserDefaults.sessionDefaultsOf store).toEqual (UserDefaults.builtInSessionDefaults ())
                Vitest.expect(UserDefaults.builtIn.LfsAutoTrackThresholdMb).toBe 1
                Vitest.expect(UserDefaults.builtIn.DownloadLargeFiles).toBe false
                Vitest.expect(UserDefaults.builtIn.DiffIndexingLimitMb).toBe 1024
                Vitest.expect(UserDefaults.builtIn.UploadRightAway).toBe true
                Vitest.expect(UserDefaults.builtIn.AutoCreateNotesFolder).toBe true
        )

        Vitest.test (
            "a changed value is stored, and a new store over the same file reads it back",
            fun () ->
                let file = memoryFile None

                let changed = {
                    UserDefaults.builtIn with
                        LfsAutoTrackThresholdMb = 20
                        UploadRightAway = false
                }

                Vitest.expect(expectUpdated (storeOver file) changed).toEqual changed
                Vitest.expect(storeOver file |> fun reopened -> reopened.Effective()).toEqual changed

                Vitest.expect(storedKeys file).toEqual [|
                    "lfsAutoTrackThresholdMb"
                    "schemaVersion"
                    "uploadRightAway"
                |]
        )

        Vitest.test (
            "a value equal to the built-in value removes its override",
            fun () ->
                let file = memoryFile None
                let store = storeOver file

                expectUpdated store {
                    UserDefaults.builtIn with
                        DownloadLargeFiles = true
                }
                |> ignore

                Vitest.expect(storedKeys file).toEqual [| "downloadLargeFiles"; "schemaVersion" |]

                expectUpdated store UserDefaults.builtIn |> ignore
                Vitest.expect(storedKeys file).toEqual [| "schemaVersion" |]
                Vitest.expect(storeOver file |> fun reopened -> reopened.Effective()).toEqual UserDefaults.builtIn
        )

        Vitest.test (
            "a change that another process saved is seen by the next call",
            fun () ->
                let file = memoryFile None
                let store = storeOver file
                Vitest.expect(store.Effective()).toEqual UserDefaults.builtIn

                file.Content.Value <- Some """{ "schemaVersion": 1, "diffIndexingLimitMb": 2048 }"""
                Vitest.expect(store.Effective().DiffIndexingLimitMb).toBe 2048
        )

        Vitest.test (
            "a damaged file falls back to the built-in values and can be replaced",
            fun () ->
                for damaged in
                    [
                        "{ this is not json"
                        """{ "lfsAutoTrackThresholdMb": 7 }"""
                        ""
                    ] do
                    let file = memoryFile (Some damaged)
                    let store = storeOver file
                    Vitest.expect(store.Effective()).toEqual UserDefaults.builtIn

                    let changed = {
                        UserDefaults.builtIn with
                            AutoCreateNotesFolder = false
                    }

                    Vitest.expect(expectUpdated store changed).toEqual changed
                    Vitest.expect(storeOver file |> fun reopened -> reopened.Effective()).toEqual changed
        )

        Vitest.test (
            "a failed write returns the error and keeps the previous values",
            fun () ->
                let file = memoryFile None

                let previous = {
                    UserDefaults.builtIn with
                        UploadRightAway = false
                }

                expectUpdated (storeOver file) previous |> ignore

                let failingWriters = [
                    storeWithWriter file (fun _ -> Error "The disk is full.")
                    storeWithWriter file (fun _ -> failwith "The disk is gone.")
                ]

                for store in failingWriters do
                    expectRefused store {
                        UserDefaults.builtIn with
                            DownloadLargeFiles = true
                    }
                    |> ignore

                    Vitest.expect(store.Effective()).toEqual previous
        )

        Vitest.test (
            "a file of a newer schema is ignored and never overwritten",
            fun () ->
                let file = memoryFile (Some newerSchemaFile)
                let store = storeOver file

                Vitest.expect(store.Effective()).toEqual UserDefaults.builtIn

                expectRefused store {
                    UserDefaults.builtIn with
                        UploadRightAway = false
                }
                |> ignore

                Vitest.expect(file.Content.Value).toEqual (Some newerSchemaFile)
                Vitest.expect(file.Writes.Value).toBe 0
        )

        Vitest.test (
            "a newer schema that another process wrote after the first read is not overwritten",
            fun () ->
                let file = memoryFile (Some """{ "schemaVersion": 1, "uploadRightAway": false }""")
                let store = storeOver file
                Vitest.expect(store.Effective().UploadRightAway).toBe false

                file.Content.Value <- Some newerSchemaFile

                expectRefused store {
                    UserDefaults.builtIn with
                        DownloadLargeFiles = true
                }
                |> ignore

                Vitest.expect(file.Content.Value).toEqual (Some newerSchemaFile)
                Vitest.expect(file.Writes.Value).toBe 0
                Vitest.expect(store.Effective()).toEqual UserDefaults.builtIn
        )

        Vitest.test (
            "a stored value that is out of range or has the wrong type falls back on its own",
            fun () ->
                let file =
                    memoryFile (
                        Some
                            """{ "schemaVersion": 1, "lfsAutoTrackThresholdMb": 500, "diffIndexingLimitMb": 2048, "downloadLargeFiles": true, "uploadRightAway": "no" }"""
                    )

                Vitest.expect(storeOver file |> fun store -> store.Effective()).toEqual {
                    UserDefaults.builtIn with
                        DiffIndexingLimitMb = 2048
                        DownloadLargeFiles = true
                }
        )

        Vitest.test (
            "values outside the allowed ranges are refused and nothing is stored",
            fun () ->
                let file = memoryFile None
                let store = storeOver file

                let tooSmall = {
                    UserDefaults.builtIn with
                        LfsAutoTrackThresholdMb = 0
                }

                let tooLarge = {
                    UserDefaults.builtIn with
                        DiffIndexingLimitMb = 1048577
                }

                expectRefused store tooSmall |> ignore
                expectRefused store tooLarge |> ignore
                Vitest.expect(file.Writes.Value).toBe 0
                Vitest.expect(store.Effective()).toEqual UserDefaults.builtIn
        )
)

Vitest.describe (
    "User defaults Git executable path",
    fun () ->
        Vitest.test (
            "a profile without a choice uses Git from PATH",
            fun () ->
                Vitest.expect(UserDefaults.builtIn.GitExecutablePath).toEqual None

                Vitest.expect(storeOver (memoryFile None) |> fun store -> store.Effective().GitExecutablePath).toEqual
                    None
        )

        Vitest.test (
            "a chosen path is stored, read back by a new store and removed again by None",
            fun () ->
                let file = memoryFile None
                let store = storeOver file

                let chosen = {
                    UserDefaults.builtIn with
                        GitExecutablePath = Some(Main.Bindings.Path.resolve [| "tools"; "git" |])
                }

                Vitest.expect(expectUpdated store chosen).toEqual chosen
                Vitest.expect(storeOver file |> fun reopened -> reopened.Effective()).toEqual chosen
                Vitest.expect(storedKeys file).toEqual [| "gitExecutablePath"; "schemaVersion" |]

                expectUpdated store UserDefaults.builtIn |> ignore
                Vitest.expect(storedKeys file).toEqual [| "schemaVersion" |]
                Vitest.expect(store.Effective().GitExecutablePath).toEqual None
        )

        Vitest.test (
            "a stored value that is no string or is blank counts as absent and spares the other values",
            fun () ->
                for stored in [ "5"; "true"; "null"; "[]"; "\"\""; "\"   \"" ] do
                    let file =
                        memoryFile (
                            Some
                                $"""{{ "schemaVersion": 1, "gitExecutablePath": {stored}, "diffIndexingLimitMb": 2048 }}"""
                        )

                    Vitest.expect(storeOver file |> fun store -> store.Effective()).toEqual {
                        UserDefaults.builtIn with
                            DiffIndexingLimitMb = 2048
                    }
        )

        Vitest.test (
            "a stored path that no longer exists stays the choice",
            fun () ->
                let missing = Main.Bindings.Path.resolve [| "no-such-folder"; "git" |]

                let file =
                    memoryFile (
                        Some(
                            JS.JSON.stringify (
                                {|
                                    schemaVersion = 1
                                    gitExecutablePath = missing
                                |}
                            )
                        )
                    )

                Vitest.expect(storeOver file |> fun store -> store.Effective().GitExecutablePath).toEqual (Some missing)
        )

        Vitest.test (
            "a stored path that no longer exists survives the change of another setting",
            fun () ->
                let missing = Main.Bindings.Path.resolve [| "no-such-folder"; "git" |]

                let file =
                    memoryFile (
                        Some(
                            JS.JSON.stringify (
                                {|
                                    schemaVersion = 1
                                    gitExecutablePath = missing
                                |}
                            )
                        )
                    )

                let store = storeOver file

                let effective = store.Effective()

                let updated =
                    expectUpdated store {
                        effective with
                            UploadRightAway = false
                    }

                Vitest.expect(updated.GitExecutablePath).toEqual (Some missing)
                Vitest.expect(updated.UploadRightAway).toBe false
        )
)
