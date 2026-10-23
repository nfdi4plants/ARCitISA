module ElectronCore.FilesystemBindingsTests

open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Vitest

Vitest.describe (
    "tryGetNodeErrorCode",
    fun () ->
        Vitest.test (
            "returns ENOENT for a failed stat of a missing path",
            fun () -> promise {
                let! directoryPath = TestHelpers.createTempDirectoryAsync "swate-error-code-"

                try
                    try
                        let! _ = statAsync (join [| directoryPath; "missing.txt" |])
                        failwith "stat of a missing path did not fail"
                    with error ->
                        Vitest.expect(tryGetNodeErrorCode error).toEqual (Some "ENOENT")

                    do! TestHelpers.removeDirectoryAsync directoryPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync directoryPath
                    return raise error
            }
        )

        Vitest.test (
            "returns None for an error without a code",
            fun () -> Vitest.expect(tryGetNodeErrorCode (exn "plain")).toEqual None
        )
)

Vitest.describe (
    "existsSyncWithExactNameIn",
    fun () ->
        Vitest.test (
            "accepts only children that exist with exactly the checked name",
            fun () -> promise {
                let! directoryPath = TestHelpers.createTempDirectoryAsync "swate-exact-name-"

                try
                    do! writeFileAsync (join [| directoryPath; "Sample.csv" |]) "sample" TextEncoding.Utf8

                    let existsWithExactName =
                        Main.WatcherHelpers.existsSyncWithExactNameIn directoryPath

                    Vitest.expect(existsWithExactName (join [| directoryPath; "Sample.csv" |])).toBe true
                    // A case-only difference counts as gone, also on case-insensitive file systems.
                    Vitest.expect(existsWithExactName (join [| directoryPath; "sample.csv" |])).toBe false
                    Vitest.expect(existsWithExactName (join [| directoryPath; "missing.csv" |])).toBe false
                    do! TestHelpers.removeDirectoryAsync directoryPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync directoryPath
                    return raise error
            }
        )
)
