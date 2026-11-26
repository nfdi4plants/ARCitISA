module ElectronCore.TextDocumentIOTests

open Fable.Core
open Fable.Core.JsInterop
open Main.ArcVaultHelper
open Main.ArcVault
open Main.ArcVaultState
open Main.ArcVaultSync
open Main.Bindings.Crypto
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.IPC.TextFileSave
open Arcitisa.Electron.Shared.FileIOHelper
open Arcitisa.Electron.Shared.FileIOTypes
open Vitest

[<Import("writeFile", "fs/promises")>]
let private writeBytesAsync (path: string) (bytes: byte[]) : JS.Promise<unit> = jsNative

[<Import("readFile", "fs/promises")>]
let private readBytesAsync (path: string) : JS.Promise<byte[]> = jsNative

[<Import("symlink", "fs/promises")>]
let private symlinkAsync (target: string) (path: string) (kind: string) : JS.Promise<unit> = jsNative

[<Import("utimes", "fs/promises")>]
let private utimesAsync (path: string) (atime: float) (mtime: float) : JS.Promise<unit> = jsNative

let private isWin32 () = TestHelpers.nodePlatform () = "win32"

let private toArray (bytes: byte[]) =
    Array.init bytes.Length (fun i -> bytes[i])

let private utf8 (text: string) = bufferFromString text "utf8"

let private withDirectory (check: string -> JS.Promise<unit>) = promise {
    let! directoryPath = TestHelpers.createTempDirectoryAsync "arcitisa-text-io-"

    try
        do! check directoryPath
        do! TestHelpers.removeDirectoryAsync directoryPath
    with error ->
        do! TestHelpers.removeDirectoryAsync directoryPath
        return raise error
}

let private readText (directory: string) (name: string) = promise {
    let! dto, info = readTextDocumentAsync (join [| directory; name |]) name
    return dto, info.Value
}

let private saveRequest
    (path: string)
    (content: string)
    (encoding: TextFileEncoding)
    (expectedHash: string option)
    (overwrite: bool)
    : SaveTextFileRequest =
    {|
        path = path
        content = content
        encoding = encoding
        expectedHash = expectedHash
        overwrite = overwrite
    |}

let private saveOutcome (directory: string) (request: SaveTextFileRequest) = promise {
    match! saveTextFileAsync directory request with
    | Ok(outcome, _) -> return outcome
    | Error error -> return failwith $"The save failed: {error.Message}"
}

Vitest.describe (
    "readTextDocumentAsync",
    fun () ->
        Vitest.test (
            "reads plain UTF-8 with the hash of the file bytes",
            fun () ->
                withDirectory (fun dir -> promise {
                    let bytes = utf8 "héllo"
                    do! writeBytesAsync (join [| dir; "a.txt" |]) bytes
                    let! dto, info = readText dir "a.txt"
                    Vitest.expect(dto.content).toBe "héllo"
                    Vitest.expect(info.encoding).toEqual TextFileEncoding.Utf8
                    Vitest.expect(info.readOnly).toEqual None
                    Vitest.expect(info.contentHash).toBe (sha256Hex bytes)
                    Vitest.expect(info.sizeBytes).toBe (float bytes.Length)
                })
        )

        Vitest.test (
            "reads UTF-8 with a BOM without the BOM in the content",
            fun () ->
                withDirectory (fun dir -> promise {
                    let bytes = bufferConcat [| [| 0xEFuy; 0xBBuy; 0xBFuy |]; utf8 "abc" |]
                    do! writeBytesAsync (join [| dir; "a.txt" |]) bytes
                    let! dto, info = readText dir "a.txt"
                    Vitest.expect(dto.content).toBe "abc"
                    Vitest.expect(info.encoding).toEqual TextFileEncoding.Utf8Bom
                    Vitest.expect(info.contentHash).toBe (sha256Hex bytes)
                })
        )

        Vitest.test (
            "reads UTF-16 LE with a BOM",
            fun () ->
                withDirectory (fun dir -> promise {
                    let bytes =
                        bufferConcat [|
                            [| 0xFFuy; 0xFEuy |]
                            bufferFromString "<a/>" "utf16le"
                        |]

                    do! writeBytesAsync (join [| dir; "a.xml" |]) bytes
                    let! dto, info = readText dir "a.xml"
                    Vitest.expect(dto.content).toBe "<a/>"
                    Vitest.expect(info.encoding).toEqual TextFileEncoding.Utf16LE
                })
        )

        Vitest.test (
            "reads UTF-16 BE with a BOM",
            fun () ->
                withDirectory (fun dir -> promise {
                    let swapped = bufferSwap16 (bufferFromString "<a/>" "utf16le")
                    do! writeBytesAsync (join [| dir; "a.xml" |]) (bufferConcat [| [| 0xFEuy; 0xFFuy |]; swapped |])
                    let! dto, info = readText dir "a.xml"
                    Vitest.expect(dto.content).toBe "<a/>"
                    Vitest.expect(info.encoding).toEqual TextFileEncoding.Utf16BE
                })
        )

        Vitest.test (
            "reads bytes that are not UTF-8 as read-only Windows-1252",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.txt" |]) [| 0x63uy; 0x61uy; 0x66uy; 0xE9uy |]
                    let! dto, info = readText dir "a.txt"
                    Vitest.expect(dto.content).toBe "café"
                    Vitest.expect(info.encoding).toEqual TextFileEncoding.Windows1252
                    Vitest.expect(info.readOnly).toEqual (Some TextReadOnlyReason.NotUtf8)
                })
        )

        Vitest.test (
            "marks a 6 MiB file as too large to edit",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "big.txt" |]) (utf8 (String.replicate (6 * 1024 * 1024) "a"))
                    let! _, info = readText dir "big.txt"
                    Vitest.expect(info.readOnly).toEqual (Some TextReadOnlyReason.TooLargeToEdit)
                })
        )

        Vitest.test (
            "reports a UTF-8 BOM file with a NUL byte as binary without info",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.txt" |]) [| 0xEFuy; 0xBBuy; 0xBFuy; 0x61uy; 0uy; 0x62uy |]
                    let! dto, info = readTextDocumentAsync (join [| dir; "a.txt" |]) "a.txt"
                    Vitest.expect(dto.noPreview.Value.reason).toEqual NoPreviewReason.Binary
                    Vitest.expect(info).toEqual None
                })
        )

        Vitest.test (
            "reports a file with a NUL byte as binary without info",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.bin" |]) [| 0x61uy; 0uy; 0x62uy |]
                    let! dto, info = readTextDocumentAsync (join [| dir; "a.bin" |]) "a.bin"
                    Vitest.expect(dto.noPreview.Value.reason).toEqual NoPreviewReason.Binary
                    Vitest.expect(info).toEqual None
                })
        )
)

Vitest.describe (
    "encodeTextContent",
    fun () ->
        let encoded (encoding: TextFileEncoding) =
            match encodeTextContent encoding "a" with
            | Ok bytes -> toArray bytes
            | Error message -> failwith message

        Vitest.test ("Utf8 has no BOM", fun () -> Vitest.expect(encoded TextFileEncoding.Utf8).toEqual [| 0x61uy |])

        Vitest.test (
            "Utf8Bom starts with EF BB BF",
            fun () -> Vitest.expect(encoded TextFileEncoding.Utf8Bom).toEqual [| 0xEFuy; 0xBBuy; 0xBFuy; 0x61uy |]
        )

        Vitest.test (
            "Utf16LE starts with FF FE",
            fun () -> Vitest.expect(encoded TextFileEncoding.Utf16LE).toEqual [| 0xFFuy; 0xFEuy; 0x61uy; 0uy |]
        )

        Vitest.test (
            "Utf16BE starts with FE FF",
            fun () -> Vitest.expect(encoded TextFileEncoding.Utf16BE).toEqual [| 0xFEuy; 0xFFuy; 0uy; 0x61uy |]
        )

        Vitest.test (
            "Windows1252 is refused",
            fun () -> Vitest.expect((encodeTextContent TextFileEncoding.Windows1252 "a").IsError).toBe true
        )
)

Vitest.describe (
    "saveTextFileAsync",
    fun () ->
        for encoding in
            [
                TextFileEncoding.Utf8
                TextFileEncoding.Utf8Bom
                TextFileEncoding.Utf16LE
                TextFileEncoding.Utf16BE
            ] do
            Vitest.test (
                $"round-trips {encoding}",
                fun () ->
                    withDirectory (fun dir -> promise {
                        let path = join [| dir; "a.txt" |]

                        let encoded (content: string) =
                            match encodeTextContent encoding content with
                            | Ok bytes -> bytes
                            | Error message -> failwith message

                        // The file on disk has the encoding of the request, as when the editor opened it.
                        do! writeBytesAsync path (encoded "old")
                        let hash = sha256Hex (encoded "old")
                        let! outcome = saveOutcome dir (saveRequest "a.txt" "new ä" encoding (Some hash) false)
                        let expected = encoded "new ä"

                        let! onDisk = readBytesAsync path
                        Vitest.expect(sha256Hex onDisk).toBe (sha256Hex expected)
                        let! dto, info = readText dir "a.txt"
                        Vitest.expect(dto.content).toBe "new ä"
                        Vitest.expect(info.encoding).toEqual encoding

                        match outcome with
                        | SaveTextFileOutcome.Saved saved -> Vitest.expect(saved.contentHash).toBe info.contentHash
                        | SaveTextFileOutcome.Conflict _ -> failwith "Expected Saved"
                    })
            )

        Vitest.test (
            "returns a conflict on a hash mismatch and keeps the file",
            fun () ->
                withDirectory (fun dir -> promise {
                    let path = join [| dir; "a.txt" |]
                    do! writeBytesAsync path (utf8 "disk")

                    let! outcome =
                        saveOutcome dir (saveRequest "a.txt" "mine" TextFileEncoding.Utf8 (Some "stale") false)

                    match outcome with
                    | SaveTextFileOutcome.Conflict hash -> Vitest.expect(hash).toEqual (Some(sha256Hex (utf8 "disk")))
                    | SaveTextFileOutcome.Saved _ -> failwith "Expected Conflict"

                    let! onDisk = readBytesAsync path
                    Vitest.expect(sha256Hex onDisk).toBe (sha256Hex (utf8 "disk"))
                })
        )

        Vitest.test (
            "saves despite a mismatch when overwrite is set",
            fun () ->
                withDirectory (fun dir -> promise {
                    let path = join [| dir; "a.txt" |]
                    do! writeBytesAsync path (utf8 "disk")
                    let! _ = saveOutcome dir (saveRequest "a.txt" "mine" TextFileEncoding.Utf8 (Some "stale") true)
                    let! onDisk = readBytesAsync path
                    Vitest.expect(sha256Hex onDisk).toBe (sha256Hex (utf8 "mine"))
                })
        )

        Vitest.test (
            "returns Conflict None for a missing file when a hash was expected",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! outcome = saveOutcome dir (saveRequest "a.txt" "x" TextFileEncoding.Utf8 (Some "abc") false)

                    match outcome with
                    | SaveTextFileOutcome.Conflict hash -> Vitest.expect(hash).toEqual None
                    | SaveTextFileOutcome.Saved _ -> failwith "Expected Conflict"
                })
        )

        Vitest.test (
            "creates a missing file when overwrite is set",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! _ = saveOutcome dir (saveRequest "a.txt" "x" TextFileEncoding.Utf8 None true)
                    let! onDisk = readBytesAsync (join [| dir; "a.txt" |])
                    Vitest.expect(sha256Hex onDisk).toBe (sha256Hex (utf8 "x"))
                })
        )

        Vitest.test (
            "keeps an executable mode",
            fun () ->
                if TestHelpers.nodePlatform () = "win32" then
                    promise { return () }
                else
                    withDirectory (fun dir -> promise {
                        let path = join [| dir; "run.sh" |]
                        do! writeBytesAsync path (utf8 "old")
                        do! TestHelpers.chmodAsync (path, 0o755)
                        let hash = sha256Hex (utf8 "old")
                        let! _ = saveOutcome dir (saveRequest "run.sh" "new" TextFileEncoding.Utf8 (Some hash) false)
                        let! stats = statAsync path
                        Vitest.expect(stats.mode &&& 0o777).toBe 0o755
                    })
        )

        Vitest.test (
            "rejects a path with a traversal segment",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! result = saveTextFileAsync dir (saveRequest "../a.txt" "x" TextFileEncoding.Utf8 None true)
                    Vitest.expect(result.IsError).toBe true
                })
        )

        Vitest.test (
            "leaves no temporary file after a save and after a hash conflict",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.txt" |]) (utf8 "old")
                    let hash = sha256Hex (utf8 "old")
                    let! _ = saveOutcome dir (saveRequest "a.txt" "new" TextFileEncoding.Utf8 (Some hash) false)
                    let! _ = saveOutcome dir (saveRequest "a.txt" "newer" TextFileEncoding.Utf8 (Some hash) false)
                    let! names = readdirAsync dir
                    Vitest.expect(names |> Array.filter (fun n -> n.StartsWith textSaveTempPrefix)).toEqual [||]
                })
        )
)

/// The save was refused. The message is only shown to the user, so it is not checked.
let private expectRefused (result: Result<SaveTextFileOutcome * string, exn>) =
    match result with
    | Error _ -> ()
    | Ok _ -> failwith "Expected an error"

/// The save was refused for the reason the editor would open the file read-only with.
let private expectRefusedWith (reason: TextReadOnlyReason) (result: Result<SaveTextFileOutcome * string, exn>) =
    match result with
    | Error error -> Vitest.expect(error.Message).toBe (readOnlyErrorMessage reason)
    | Ok _ -> failwith $"Expected an error for {reason}"

Vitest.describe (
    "saveTextFileAsync without an expected hash",
    fun () ->
        Vitest.test (
            "creates a missing file and refuses an existing one",
            fun () ->
                withDirectory (fun dir -> promise {
                    let path = join [| dir; "a.txt" |]
                    let! created = saveOutcome dir (saveRequest "a.txt" "first" TextFileEncoding.Utf8 None false)

                    match created with
                    | SaveTextFileOutcome.Saved _ -> ()
                    | SaveTextFileOutcome.Conflict _ -> failwith "Expected Saved"

                    let! again = saveOutcome dir (saveRequest "a.txt" "second" TextFileEncoding.Utf8 None false)

                    match again with
                    | SaveTextFileOutcome.Conflict hash -> Vitest.expect(hash).toEqual (Some(sha256Hex (utf8 "first")))
                    | SaveTextFileOutcome.Saved _ -> failwith "Expected Conflict"

                    let! onDisk = readBytesAsync path
                    Vitest.expect(sha256Hex onDisk).toBe (sha256Hex (utf8 "first"))
                })
        )
)

Vitest.describe (
    "UTF-16 and BOM fidelity",
    fun () ->
        Vitest.test (
            "opens UTF-16 with a lone surrogate as read-only",
            fun () ->
                withDirectory (fun dir -> promise {
                    do!
                        writeBytesAsync (join [| dir; "a.txt" |]) [|
                            0xFFuy
                            0xFEuy
                            0x61uy
                            0uy
                            0uy
                            0xD8uy
                            0x62uy
                            0uy
                        |]

                    let! _, info = readText dir "a.txt"
                    Vitest.expect(info.encoding).toEqual TextFileEncoding.Utf16LE
                    Vitest.expect(info.readOnly).toEqual (Some TextReadOnlyReason.InvalidUtf16)
                })
        )

        for name, original in
            [
                "UTF-16 LE",
                bufferConcat [|
                    [| 0xFFuy; 0xFEuy |]
                    bufferFromString "a\r\nb ä" "utf16le"
                |]
                "UTF-8 with BOM", bufferConcat [| [| 0xEFuy; 0xBBuy; 0xBFuy |]; utf8 "a\r\nb ä" |]
            ] do
            Vitest.test (
                $"saves a {name} file back to identical bytes",
                fun () ->
                    withDirectory (fun dir -> promise {
                        let path = join [| dir; "a.txt" |]
                        do! writeBytesAsync path original
                        let! dto, info = readText dir "a.txt"

                        let! _ =
                            saveOutcome
                                dir
                                (saveRequest "a.txt" dto.content info.encoding (Some info.contentHash) false)

                        let! onDisk = readBytesAsync path
                        Vitest.expect(sha256Hex onDisk).toBe (sha256Hex original)
                    })
            )
)

Vitest.describe (
    "saveTextFileAsync target checks",
    fun () ->
        Vitest.test (
            "refuses paths that are not part of the ARC inventory",
            fun () ->
                withDirectory (fun dir -> promise {
                    // Both folders exist, so only the inventory rule can refuse these saves.
                    let! _ = mkdirAsync (join [| dir; ".git" |]) (MkdirOptions(recursive = true))
                    let! _ = mkdirAsync (join [| dir; "sub" |]) (MkdirOptions(recursive = true))

                    let! git = saveTextFileAsync dir (saveRequest ".git/config" "x" TextFileEncoding.Utf8 None true)
                    expectRefused git
                    let! gitWritten = TestHelpers.pathExistsAsync (join [| dir; ".git"; "config" |])
                    Vitest.expect(gitWritten).toBe false

                    let tempName = "sub/.swate-save-0123456789abcdef0123456789abcdef.tmp"
                    let! temp = saveTextFileAsync dir (saveRequest tempName "x" TextFileEncoding.Utf8 None true)
                    expectRefused temp
                    let! tempWritten = TestHelpers.pathExistsAsync (join [| dir; tempName |])
                    Vitest.expect(tempWritten).toBe false
                })
        )

        Vitest.test (
            "refuses a symbolic link",
            fun () ->
                if isWin32 () then
                    promise { return () }
                else
                    withDirectory (fun dir -> promise {
                        do! writeBytesAsync (join [| dir; "real.txt" |]) (utf8 "real")
                        do! symlinkAsync (join [| dir; "real.txt" |]) (join [| dir; "link.txt" |]) "file"

                        let! result =
                            saveTextFileAsync dir (saveRequest "link.txt" "x" TextFileEncoding.Utf8 None true)

                        // The save path has its own text for this case, so only the refusal and the untouched target are checked.
                        expectRefused result
                        let! onDisk = readBytesAsync (join [| dir; "real.txt" |])
                        Vitest.expect(sha256Hex onDisk).toBe (sha256Hex (utf8 "real"))

                        match! Main.Helper.ArcPathContainment.checkTargetAsync dir (join [| dir; "link.txt" |]) with
                        | Ok Main.Helper.ArcPathContainment.SymbolicLink -> ()
                        | _ -> failwith "Expected SymbolicLink"
                    })
        )

        Vitest.test (
            "refuses a folder link that points outside the ARC",
            fun () ->
                withDirectory (fun outside ->
                    withDirectory (fun dir -> promise {
                        do! TestHelpers.createFolderLinkAsync outside (join [| dir; "linked" |])

                        let! result =
                            saveTextFileAsync dir (saveRequest "linked/a.txt" "x" TextFileEncoding.Utf8 None true)

                        expectRefusedWith TextReadOnlyReason.OutsideArc result

                        match!
                            Main.Helper.ArcPathContainment.checkTargetAsync dir (join [| dir; "linked"; "a.txt" |])
                        with
                        | Ok Main.Helper.ArcPathContainment.OutsideArc -> ()
                        | _ -> failwith "Expected OutsideArc"
                    })
                )
        )

        Vitest.test (
            "refuses a write-protected file",
            fun () ->
                withDirectory (fun dir -> promise {
                    let path = join [| dir; "a.txt" |]
                    do! writeBytesAsync path (utf8 "old")
                    do! TestHelpers.chmodAsync (path, 0o444)
                    let! result = saveTextFileAsync dir (saveRequest "a.txt" "x" TextFileEncoding.Utf8 None true)
                    let! state = Main.Helper.ArcPathContainment.checkTargetAsync dir path
                    do! TestHelpers.chmodAsync (path, 0o644)
                    expectRefusedWith TextReadOnlyReason.WriteProtected result

                    match state with
                    | Ok(Main.Helper.ArcPathContainment.RegularFile(stats, _)) ->
                        Vitest.expect(Main.IPC.TextFileSave.isWriteProtected stats).toBe true
                    | _ -> failwith "Expected RegularFile"
                })
        )

        Vitest.test (
            "refuses a path whose folder is missing",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! keep = saveTextFileAsync dir (saveRequest "gone/a.txt" "x" TextFileEncoding.Utf8 None false)
                    expectRefused keep
                    let! createdByKeep = TestHelpers.pathExistsAsync (join [| dir; "gone" |])
                    Vitest.expect(createdByKeep).toBe false

                    let! over =
                        saveTextFileAsync dir (saveRequest "gone/a.txt" "x" TextFileEncoding.Utf8 (Some "h") true)

                    expectRefused over
                    let! createdByOverwrite = TestHelpers.pathExistsAsync (join [| dir; "gone" |])
                    Vitest.expect(createdByOverwrite).toBe false
                })
        )

        Vitest.test (
            "removes save temporary files older than ten minutes after a save",
            fun () ->
                withDirectory (fun dir -> promise {
                    let freshName = ".swate-save-fedcba9876543210fedcba9876543210.tmp"

                    let stale =
                        join [|
                            dir
                            ".swate-save-0123456789abcdef0123456789abcdef.tmp"
                        |]

                    do! writeBytesAsync stale (utf8 "x")
                    do! writeBytesAsync (join [| dir; freshName |]) (utf8 "x")
                    let anHourAgo = JS.Constructors.Date.now () / 1000.0 - 3600.0
                    do! utimesAsync stale anHourAgo anHourAgo
                    let! _ = saveOutcome dir (saveRequest "a.txt" "x" TextFileEncoding.Utf8 None false)
                    let! names = readdirAsync dir
                    Vitest.expect(names |> Array.sort).toEqual [| freshName; "a.txt" |]
                })
        )
)

Vitest.describe (
    "saveTextFileAsync read-only rules",
    fun () ->
        let lowSurrogateFile = [| 0xFFuy; 0xFEuy; 0x61uy; 0uy; 0uy; 0xD8uy; 0x62uy; 0uy |]

        // Each file opens read-only or not at all. The request claims the encoding and hash of an editable file,
        // as a renderer with stale state would send them. The reason is None for a file that does not open.
        for name, bytes, encoding, reason in
            [
                "a file that is not valid UTF-8",
                [| 0x63uy; 0x61uy; 0x66uy; 0xE9uy |],
                TextFileEncoding.Utf8,
                Some TextReadOnlyReason.NotUtf8
                "a UTF-16 file with a lone surrogate",
                lowSurrogateFile,
                TextFileEncoding.Utf16LE,
                Some TextReadOnlyReason.InvalidUtf16
                "a file larger than 5 MB",
                utf8 (String.replicate (6 * 1024 * 1024) "a"),
                TextFileEncoding.Utf8,
                Some TextReadOnlyReason.TooLargeToEdit
                "a file with binary content", [| 0x61uy; 0uy; 0x62uy |], TextFileEncoding.Utf8, None
            ] do
            for overwrite in [ false; true ] do
                Vitest.test (
                    $"refuses {name} with overwrite {overwrite} and keeps it",
                    fun () ->
                        withDirectory (fun dir -> promise {
                            let path = join [| dir; "a.txt" |]
                            do! writeBytesAsync path bytes

                            let! result =
                                saveTextFileAsync
                                    dir
                                    (saveRequest "a.txt" "mine" encoding (Some(sha256Hex bytes)) overwrite)

                            match reason with
                            | Some reason -> expectRefusedWith reason result
                            | None -> expectRefused result

                            let! onDisk = readBytesAsync path
                            Vitest.expect(sha256Hex onDisk).toBe (sha256Hex bytes)
                        })
                )

        Vitest.test (
            "refuses an encoding other than the one on disk unless overwrite is set",
            fun () ->
                withDirectory (fun dir -> promise {
                    let path = join [| dir; "a.txt" |]

                    let original =
                        bufferConcat [| [| 0xFFuy; 0xFEuy |]; bufferFromString "old" "utf16le" |]

                    do! writeBytesAsync path original

                    let request =
                        saveRequest "a.txt" "mine" TextFileEncoding.Utf8 (Some(sha256Hex original))

                    let! refused = saveTextFileAsync dir (request false)
                    expectRefused refused
                    let! unchanged = readBytesAsync path
                    Vitest.expect(sha256Hex unchanged).toBe (sha256Hex original)

                    match! saveOutcome dir (request true) with
                    | SaveTextFileOutcome.Saved _ -> ()
                    | SaveTextFileOutcome.Conflict _ -> failwith "Expected Saved"

                    let! replaced = readBytesAsync path
                    Vitest.expect(sha256Hex replaced).toBe (sha256Hex (utf8 "mine"))
                })
        )

        Vitest.test (
            "describeTextBytes marks a read that saw a change as read-only, over the other reasons",
            fun () ->
                let reasonOf (bytes: byte[]) (changed: bool) =
                    describeTextBytes bytes bytes.Length changed
                    |> Option.bind (fun (_, info) -> info.readOnly)

                Vitest.expect(reasonOf (utf8 "abc") false).toEqual None
                Vitest.expect(reasonOf (utf8 "abc") true).toEqual (Some TextReadOnlyReason.ChangedWhileReading)
                Vitest.expect(reasonOf lowSurrogateFile false).toEqual (Some TextReadOnlyReason.InvalidUtf16)
                Vitest.expect(reasonOf lowSurrogateFile true).toEqual (Some TextReadOnlyReason.ChangedWhileReading)
                Vitest.expect(describeTextBytes [| 0x61uy; 0uy |] 2 true).toEqual None
        )
)

Vitest.describe (
    "vault busy handling",
    fun () ->
        Vitest.test (
            "isVaultBusyError is true while another exclusive scope holds the vault",
            fun () -> promise {
                let vault = ArcVault(TestHelpers.testWindow ())
                let gate, release = TestHelpers.deferred ()

                let hold =
                    vault.TryWithExclusiveExternalOperationScope(
                        false,
                        fun () -> promise {
                            do! gate
                            return Ok()
                        }
                    )

                do! Promise.sleep 20

                match! vault.TryWithExclusiveExternalOperationScope(false, fun () -> promise { return Ok() }) with
                | Ok() -> failwith "Expected the scope to be refused."
                | Error error -> Vitest.expect(Main.ArcVaultSync.isVaultBusyError error).toBe true

                Vitest.expect(Main.ArcVaultSync.isVaultBusyError (exn "x")).toBe false
                release ()
                let! _ = hold
                ()
            }
        )

        Vitest.test (
            "saveTextFile waits for a busy vault and then saves",
            fun () ->
                withDirectory (fun dir -> promise {
                    let windowId = 9731
                    let vault = TestHelpers.registerVault windowId dir

                    try
                        do! writeBytesAsync (join [| dir; "a.txt" |]) (utf8 "old")
                        let gate, release = TestHelpers.deferred ()

                        let hold =
                            vault.TryWithExclusiveExternalOperationScope(
                                false,
                                fun () -> promise {
                                    do! gate
                                    return Ok()
                                }
                            )

                        do! Promise.sleep 20
                        let api = Main.IPC.ArcVaultsApi.api (TestHelpers.ipcEvent windowId)

                        let save =
                            api.saveTextFile (
                                saveRequest "a.txt" "new" TextFileEncoding.Utf8 (Some(sha256Hex (utf8 "old"))) false
                            )

                        do! Promise.sleep 300
                        release ()
                        let! _ = hold

                        match! save with
                        | Ok(SaveTextFileOutcome.Saved _) -> ()
                        | _ -> failwith "Expected Saved"
                    finally
                        ARC_VAULTS.Vaults.Remove windowId |> ignore
                        TestHelpers.electronMock?reset () |> ignore
                })
        )
)

Vitest.describe (
    "isIgnoredInventoryEntry",
    fun () ->
        let tempName = ".swate-save-0123456789abcdef0123456789abcdef.tmp"

        Vitest.test (
            "ignores a save temporary file as a file",
            fun () -> Vitest.expect(isIgnoredInventoryEntry tempName $"assays/{tempName}" false).toBe true
        )

        Vitest.test (
            "keeps a folder with that name",
            fun () -> Vitest.expect(isIgnoredInventoryEntry tempName $"assays/{tempName}" true).toBe false
        )
)
