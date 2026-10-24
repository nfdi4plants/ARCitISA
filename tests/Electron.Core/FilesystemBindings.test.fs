module ElectronCore.FilesystemBindingsTests

open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
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
    "existsSyncWithExactName",
    fun () ->
        Vitest.test (
            "accepts only children that exist with exactly the checked name",
            fun () -> promise {
                let! directoryPath = TestHelpers.createTempDirectoryAsync "swate-exact-name-"

                try
                    do! writeFileAsync (join [| directoryPath; "Sample.csv" |]) "sample" TextEncoding.Utf8

                    let existsWithExactName = Main.WatcherHelpers.existsSyncWithExactName

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

let private withPreviewFile (name: string) (content: string) (check: string -> JS.Promise<unit>) = promise {
    let! directoryPath = TestHelpers.createTempDirectoryAsync "swate-preview-"

    try
        let filePath = join [| directoryPath; name |]
        do! writeFileAsync filePath content TextEncoding.Utf8
        do! check filePath
        do! TestHelpers.removeDirectoryAsync directoryPath
    with error ->
        do! TestHelpers.removeDirectoryAsync directoryPath
        return raise error
}

[<Import("truncate", "fs/promises")>]
let private truncateFileAsync (path: string) (length: float) : JS.Promise<unit> = jsNative

/// A sparse file of the given size. The bytes are zeros and the test never reads them.
let private withSizedPreviewFile (name: string) (sizeBytes: float) (check: string -> JS.Promise<unit>) =
    withPreviewFile
        name
        ""
        (fun path -> promise {
            do! truncateFileAsync path sizeBytes
            do! check path
        })

[<Import("writeFile", "fs/promises")>]
let private writeBytesAsync (path: string) (bytes: byte[]) : JS.Promise<unit> = jsNative

let private megabytes (count: int) = float count * 1024.0 * 1024.0

Vitest.describe (
    "readFileForPreviewAsync",
    fun () ->
        Vitest.test (
            "reports a text file above 20 MB as too large",
            fun () ->
                withSizedPreviewFile
                    "big.txt"
                    (megabytes 21)
                    (fun path -> promise {
                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "big.txt"

                        Vitest
                            .expect(dto.noPreview)
                            .toEqual (
                                Some {|
                                    sizeBytes = megabytes 21
                                    reason = NoPreviewReason.TooLarge
                                |}
                            )
                    })
        )

        Vitest.test (
            "reports a small file with a NUL byte as binary",
            fun () ->
                withPreviewFile
                    "data.bin"
                    ("ab" + string (char 0) + "cd")
                    (fun path -> promise {
                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "data.bin"

                        Vitest
                            .expect(dto.noPreview)
                            .toEqual (
                                Some {|
                                    sizeBytes = 5.0
                                    reason = NoPreviewReason.Binary
                                |}
                            )
                    })
        )

        Vitest.test (
            "returns the content of a small markdown file",
            fun () ->
                withPreviewFile
                    "note.md"
                    "# Title"
                    (fun path -> promise {
                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "note.md"
                        Vitest.expect(dto.fileType).toEqual FileContentType.Markdown
                        Vitest.expect(dto.content).toBe "# Title"
                    })
        )

        Vitest.test (
            "previews a small PDF that has NUL bytes in its first 8 KB",
            fun () ->
                withPreviewFile
                    "doc.pdf"
                    ("%PDF" + string (char 0) + "-1")
                    (fun path -> promise {
                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "doc.pdf"
                        Vitest.expect(dto.fileType).toEqual FileContentType.PDF
                        Vitest.expect(dto.content).toBe "JVBERgAtMQ=="
                    })
        )

        Vitest.test (
            "previews a UTF-16 LE text file with a BOM as text",
            fun () ->
                // FF FE is the BOM, the rest is "<a/>" in UTF-16 LE, so every second byte is NUL.
                withPreviewFile
                    "info.xml"
                    ""
                    (fun path -> promise {
                        do!
                            writeBytesAsync path [|
                                0xFFuy
                                0xFEuy
                                0x3Cuy
                                0uy
                                0x61uy
                                0uy
                                0x2Fuy
                                0uy
                                0x3Euy
                                0uy
                            |]

                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "info.xml"
                        Vitest.expect(dto.noPreview).toEqual None
                        Vitest.expect(dto.content).toBe "<a/>"
                    })
        )

        Vitest.test (
            "previews a zero-byte text file as empty text",
            fun () ->
                withPreviewFile
                    "empty.txt"
                    ""
                    (fun path -> promise {
                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "empty.txt"
                        Vitest.expect(dto.noPreview).toEqual None
                        Vitest.expect(dto.content).toBe ""
                    })
        )

        Vitest.test (
            "reports a PDF above 50 MB as too large",
            fun () ->
                withSizedPreviewFile
                    "big.pdf"
                    (megabytes 51)
                    (fun path -> promise {
                        let! dto = Main.ArcVaultHelper.readFileForPreviewAsync path "big.pdf"

                        Vitest
                            .expect(dto.noPreview)
                            .toEqual (
                                Some {|
                                    sizeBytes = megabytes 51
                                    reason = NoPreviewReason.TooLarge
                                |}
                            )
                    })
        )

        Vitest.test (
            "reports a directory as binary without opening it",
            fun () -> promise {
                let! directoryPath = TestHelpers.createTempDirectoryAsync "swate-preview-dir-"

                try
                    let! dto = Main.ArcVaultHelper.readFileForPreviewAsync directoryPath "folder"
                    Vitest.expect(dto.fileType).toEqual FileContentType.NoPreview
                    Vitest.expect(dto.noPreview.Value.reason).toEqual NoPreviewReason.Binary
                    do! TestHelpers.removeDirectoryAsync directoryPath
                with error ->
                    do! TestHelpers.removeDirectoryAsync directoryPath
                    return raise error
            }
        )
)
