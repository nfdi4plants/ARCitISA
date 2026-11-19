module ElectronCore.MarkdownResolverServiceTests

open Fable.Core
open Fetch
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Main.Services.MarkdownResolverService
open Swate.Electron.Shared
open Swate.Electron.Shared.DTOs.MarkdownResolverDto
open Vitest

[<Import("writeFile", "fs/promises")>]
let private writeBytesAsync (path: string) (bytes: byte[]) : JS.Promise<unit> = jsNative

[<Import("mkdir", "fs/promises")>]
let private mkdirRecursiveAsync (path: string) (options: {| recursive: bool |}) : JS.Promise<obj> = jsNative

[<Import("symlink", "fs/promises")>]
let private symlinkAsync (target: string) (path: string) (kind: string) : JS.Promise<unit> = jsNative

[<Emit("$0.headers.get($1)")>]
let private headerOf (response: Response) (name: string) : string = jsNative

[<Emit("Array.from(new Uint8Array($0))")>]
let private bytesOfBuffer (buffer: JS.ArrayBuffer) : byte[] = jsNative

[<Emit("$0.status")>]
let private statusOf (response: Response) : int = jsNative

let private isWin32 () = TestHelpers.nodePlatform () = "win32"

let private utf8 (text: string) = bufferFromString text "utf8"

/// Windows creates a junction to a folder, which needs no privilege. Other systems create a file link.
let private canLink () =
    isWin32 () || TestHelpers.canCreateFileLinks ()

/// The ARC-relative path of the link that linkOutside creates.
let private linkedPath = if isWin32 () then "linked/o.png" else "linked.png"

/// Links the ARC folder to the outside folder, which holds o.png.
let private linkOutside (outside: string) (dir: string) : JS.Promise<unit> =
    if isWin32 () then
        TestHelpers.createFolderLinkAsync outside (join [| dir; "linked" |])
    else
        symlinkAsync (join [| outside; "o.png" |]) (join [| dir; "linked.png" |]) "file"

let private nonce = "0123456789abcdef0123456789abcdef"

let private authority = "w3-" + nonce

let private withDirectory (check: string -> JS.Promise<unit>) = promise {
    let! directoryPath = TestHelpers.createTempDirectoryAsync "swate-md-resolver-"

    try
        do! check directoryPath
    finally
        TestHelpers.removeDirectoryAsync directoryPath |> Promise.start
}

let private serviceFor (root: string) =
    MarkdownResolverService(7, (fun () -> Some root), 64.0)

let private pngBytes = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 1uy; 2uy; 3uy |]

Vitest.describe (
    "MarkdownAssetUrl",
    fun () ->
        let baseUrl = MarkdownAssetUrl.baseUrl 3 nonce

        Vitest.test (
            "build and tryParse round trip a path with spaces and special characters",
            fun () ->
                for path in [ "images/my image #1?.png"; "ä/ö.png" ] do
                    match MarkdownAssetUrl.tryParse (MarkdownAssetUrl.build baseUrl path) with
                    | Some p ->
                        Vitest.expect(p.ArcPath).toBe path
                        Vitest.expect(p.WindowId).toBe 3
                        Vitest.expect(p.Nonce).toBe nonce
                    | None -> failwith "Expected a parsed URL"
        )

        Vitest.test (
            "tryParse accepts an uppercase scheme and host",
            fun () ->
                let url = "ARC-ASSET://W3-" + nonce.ToUpperInvariant() + "/a.png"
                Vitest.expect((MarkdownAssetUrl.tryParse url).IsSome).toBe true
        )

        Vitest.test (
            "tryParse rejects other schemes, hosts with port or user info and malformed hosts",
            fun () ->
                for url in
                    [
                        "https://" + authority + "/a.png"
                        "arc-asset://" + authority + ":80/a.png"
                        "arc-asset://user@" + authority + "/a.png"
                        "arc-asset://w3-abc/a.png"
                        "arc-asset://w0-" + nonce + "/a.png"
                    ] do
                    Vitest.expect((MarkdownAssetUrl.tryParse url).IsNone).toBe true
        )

        Vitest.test (
            "tryParse rejects a missing path, a bad escape and a parent segment",
            fun () ->
                for url in
                    [
                        "arc-asset://" + authority
                        "arc-asset://" + authority + "/"
                        "arc-asset://" + authority + "/%zz.png"
                        "arc-asset://" + authority + "/../a.png"
                        "arc-asset://" + authority + "/a/%2e%2e/b.png"
                        "arc-asset://" + authority + "/a//b.png"
                    ] do
                    Vitest.expect((MarkdownAssetUrl.tryParse url).IsNone).toBe true
        )

        Vitest.test (
            "tryParse rejects an escaped separator or control character inside a segment",
            fun () ->
                for url in
                    [
                        "arc-asset://" + authority + "/a%2F..%2F..%2Fx.png"
                        "arc-asset://" + authority + "/a%5C..%5Cx.png"
                        "arc-asset://" + authority + "/%00.png"
                    ] do
                    Vitest.expect((MarkdownAssetUrl.tryParse url).IsNone).toBe true
        )
)

Vitest.describe (
    "AssetPolicy",
    fun () ->
        Vitest.test (
            "contentTypeOf maps the image extensions in any case",
            fun () ->
                for path, expected in
                    [
                        "a.png", "image/png"
                        "a.PNG", "image/png"
                        "a.jpg", "image/jpeg"
                        "a.jpeg", "image/jpeg"
                        "a.gif", "image/gif"
                        "a.svg", "image/svg+xml"
                        "a.webp", "image/webp"
                        "a.bmp", "image/bmp"
                    ] do
                    Vitest.expect((AssetPolicy.contentTypeOf path = Ok expected)).toBe true
        )

        Vitest.test (
            "contentTypeOf returns the extension of an unsupported type",
            fun () ->
                Vitest.expect((AssetPolicy.contentTypeOf "x/a.TIF" = Error ".tif")).toBe true
                Vitest.expect((AssetPolicy.contentTypeOf "x.y/a" = Error "")).toBe true
        )

        Vitest.test (
            "isLfsPointer detects the pointer header",
            fun () ->
                let pointer =
                    utf8 "version https://git-lfs.github.com/spec/v1\noid sha256:abc\nsize 10\n"

                Vitest.expect(AssetPolicy.isLfsPointer pointer pointer.Length).toBe true
                Vitest.expect(AssetPolicy.isLfsPointer pngBytes pngBytes.Length).toBe false
                Vitest.expect(AssetPolicy.isLfsPointer pointer 10).toBe false
        )

        Vitest.test (
            "statusOf maps every refusal",
            fun () ->
                for refusal, status in
                    [
                        AssetRefusal.ArcNotOpen, 403
                        AssetRefusal.OutsideArc, 403
                        AssetRefusal.NotFound, 404
                        AssetRefusal.LfsPointer, 409
                        AssetRefusal.TooLarge(2.0, 1.0), 413
                        AssetRefusal.UnsupportedType ".tif", 415
                        AssetRefusal.Unreadable "x", 500
                    ] do
                    Vitest.expect(AssetPolicy.statusOf refusal).toBe status
        )
)

Vitest.describe (
    "MarkdownResolverService.ServeAsset",
    fun () ->
        Vitest.test (
            "serves a small image with its content type and no-store",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.png" |]) pngBytes
                    let! response = (serviceFor dir).ServeAsset "a.png"
                    let! body = response.arrayBuffer ()

                    Vitest.expect(statusOf response).toBe 200
                    Vitest.expect(headerOf response "Content-Type").toBe "image/png"
                    Vitest.expect(headerOf response "Cache-Control").toBe "no-store"
                    Vitest.expect(headerOf response "X-Content-Type-Options").toBe "nosniff"
                    Vitest.expect((bytesOfBuffer body = pngBytes)).toBe true
                })
        )

        Vitest.test (
            "answers an empty image with 200",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "empty.png" |]) [||]
                    let! response = (serviceFor dir).ServeAsset "empty.png"
                    Vitest.expect(statusOf response).toBe 200
                })
        )

        Vitest.test (
            "refuses a missing file, a parent path, an oversize file, an LFS pointer and an unsupported type",
            fun () ->
                withDirectory (fun dir -> promise {
                    let pointer =
                        utf8 "version https://git-lfs.github.com/spec/v1\noid sha256:ab\nsize 5\n"

                    do! writeBytesAsync (join [| dir; "big.png" |]) (Array.zeroCreate 65)
                    do! writeBytesAsync (join [| dir; "lfs.png" |]) pointer
                    do! writeBytesAsync (join [| dir; "a.tif" |]) pngBytes
                    let service = serviceFor dir

                    for path, status in
                        [
                            "none.png", 404
                            "../x.png", 403
                            "big.png", 413
                            "lfs.png", 409
                            "a.tif", 415
                        ] do
                        let! response = service.ServeAsset path
                        Vitest.expect(statusOf response).toBe status
                        Vitest.expect(headerOf response "Cache-Control").toBe "no-store"
                })
        )

        Vitest.test (
            "refuses every image while no ARC is open",
            fun () -> promise {
                let service = MarkdownResolverService(7, (fun () -> None))
                let! response = service.ServeAsset "a.png"
                Vitest.expect(statusOf response).toBe 403
            }
        )

        Vitest.test (
            "refuses a link that points outside the ARC",
            TestOptions(skip = not (canLink ())),
            fun () ->
                withDirectory (fun outside ->
                    withDirectory (fun dir -> promise {
                        do! writeBytesAsync (join [| outside; "o.png" |]) pngBytes
                        do! linkOutside outside dir
                        let! response = (serviceFor dir).ServeAsset linkedPath
                        Vitest.expect(statusOf response).toBe 403
                    })
                )
        )

        Vitest.test (
            "answers 404 for a path through a file",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "data.csv" |]) (utf8 "x")
                    let! response = (serviceFor dir).ServeAsset "data.csv/x.png"
                    Vitest.expect(statusOf response).toBe 404
                })
        )

        Vitest.test (
            "sandboxes an SVG response and no other image",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.svg" |]) (utf8 "<svg/>")
                    do! writeBytesAsync (join [| dir; "a.png" |]) pngBytes
                    let service = serviceFor dir
                    let! svg = service.ServeAsset "a.svg"
                    let! png = service.ServeAsset "a.png"
                    Vitest.expect(headerOf svg "Content-Security-Policy").toBe "sandbox"
                    Vitest.expect(isNull (headerOf png "Content-Security-Policy")).toBe true
                })
        )

        Vitest.test (
            "DescribeAsset returns the refusal, or None for a served image",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.png" |]) pngBytes
                    let service = serviceFor dir
                    let! served = service.DescribeAsset "a.png"
                    let! missing = service.DescribeAsset "none.png"
                    let! unsupported = service.DescribeAsset "a.tif"
                    Vitest.expect(served.IsNone).toBe true
                    Vitest.expect((missing = Some AssetRefusal.NotFound)).toBe true
                    Vitest.expect((unsupported = Some(AssetRefusal.UnsupportedType ".tif"))).toBe true
                })
        )

        Vitest.test (
            "Renew changes the base URL and invalidates the old nonce",
            fun () ->
                let service = serviceFor "C:/unused"
                let before = service.AssetBaseUrl
                let oldNonce = (MarkdownAssetUrl.tryParse (before + "a.png")).Value.Nonce
                Vitest.expect(service.Authorizes oldNonce).toBe true

                service.Renew()

                Vitest.expect(service.AssetBaseUrl <> before).toBe true
                Vitest.expect(service.Authorizes oldNonce).toBe false
        )
)

Vitest.describe (
    "AssetProtocol.handleRequest",
    fun () ->
        Vitest.test (
            "answers by window, nonce, method and URL shape",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "a.png" |]) pngBytes
                    let service = serviceFor dir
                    let find id = if id = 7 then Some service else None
                    let url = service.AssetBaseUrl + "a.png"
                    let otherWindow = MarkdownAssetUrl.baseUrl 8 nonce + "a.png"
                    let wrongNonce = MarkdownAssetUrl.baseUrl 7 nonce + "a.png"

                    for method, requested, status in
                        [
                            "GET", url, 200
                            "get", url, 200
                            "POST", url, 403
                            "GET", otherWindow, 403
                            "GET", wrongNonce, 403
                            "GET", "arc-asset://nope/a.png", 400
                            "GET", service.AssetBaseUrl + "a%2F..%2F..%2Fx.png", 400
                            "GET", service.AssetBaseUrl + "a%5C..%5Cx.png", 400
                        ] do
                        let! response = AssetProtocol.handleRequest find method requested
                        Vitest.expect(statusOf response).toBe status
                })
        )
)

Vitest.describe (
    "MarkdownResolverService.CheckPaths",
    fun () ->
        Vitest.test (
            "classifies files, folders, missing paths and paths outside the ARC",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! _ = mkdirRecursiveAsync (join [| dir; "docs" |]) {| recursive = true |}
                    do! writeBytesAsync (join [| dir; "docs"; "a.md" |]) (utf8 "x")

                    let! result = (serviceFor dir).CheckPaths [| "docs/a.md"; "docs"; "docs/none.md"; "../x"; "" |]

                    match result with
                    | Ok checks ->
                        let expected = [|
                            PathCheck.File
                            PathCheck.Directory
                            PathCheck.Missing
                            PathCheck.OutsideArc
                            PathCheck.Directory
                        |]

                        Vitest.expect((checks = expected)).toBe true
                    | Error error -> failwith error.Message
                })
        )

        Vitest.test (
            "refuses more than 500 paths and a closed ARC",
            fun () -> promise {
                let! tooMany = (serviceFor "C:/unused").CheckPaths(Array.create 501 "a.md")
                let! closed = MarkdownResolverService(7, (fun () -> None)).CheckPaths [| "a.md" |]
                Vitest.expect(tooMany.IsError).toBe true
                Vitest.expect(closed.IsError).toBe true
            }
        )

        Vitest.test (
            "accepts as many paths as the link lint sends at most",
            fun () ->
                withDirectory (fun dir -> promise {
                    let limit =
                        Swate.Components.Composite.MarkdownText.Links.LinkDiagnostics.maxCheckedPaths

                    let! atLimit = (serviceFor dir).CheckPaths(Array.create limit "a.md")
                    let! aboveLimit = (serviceFor dir).CheckPaths(Array.create (limit + 1) "a.md")
                    Vitest.expect(atLimit.IsOk).toBe true
                    Vitest.expect(aboveLimit.IsError).toBe true
                })
        )

        Vitest.test (
            "probes only small regular files for an LFS pointer",
            fun () ->
                Vitest.expect(AssetPolicy.shouldProbeForLfsPointer true 100.0).toBe true
                Vitest.expect(AssetPolicy.shouldProbeForLfsPointer true 1024.0).toBe false
                // A FIFO, socket or device is not a regular file, whatever size it reports.
                Vitest.expect(AssetPolicy.shouldProbeForLfsPointer false 0.0).toBe false
        )

        Vitest.test (
            "reports a named pipe as unreadable without opening it",
            TestOptions(skip = (isWin32 () || not (TestHelpers.hasMkfifo ()))),
            fun () ->
                withDirectory (fun dir -> promise {
                    Vitest.expect(TestHelpers.makeFifo (join [| dir; "pipe" |])).toBe 0

                    let! result = (serviceFor dir).CheckPaths [| "pipe" |]

                    match result with
                    | Ok checks ->
                        Vitest
                            .expect(
                                (checks = [|
                                    PathCheck.Unreadable "This is not a regular file or a folder."
                                |])
                            )
                            .toBe
                            true
                    | Error error -> failwith error.Message
                })
        )

        Vitest.test (
            "recognizes a Git LFS pointer file",
            fun () ->
                withDirectory (fun dir -> promise {
                    let pointer =
                        utf8 "version https://git-lfs.github.com/spec/v1\noid sha256:ab\nsize 5\n"

                    do! writeBytesAsync (join [| dir; "data.bin" |]) pointer

                    let! result = (serviceFor dir).CheckPaths [| "data.bin" |]

                    match result with
                    | Ok checks -> Vitest.expect((checks = [| PathCheck.LfsPointer |])).toBe true
                    | Error error -> failwith error.Message
                })
        )

        Vitest.test (
            "reports a path through a file as missing",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! writeBytesAsync (join [| dir; "data.csv" |]) (utf8 "x")

                    let! result = (serviceFor dir).CheckPaths [| "data.csv/x.png" |]

                    match result with
                    | Ok checks -> Vitest.expect((checks = [| PathCheck.Missing |])).toBe true
                    | Error error -> failwith error.Message
                })
        )

        Vitest.test (
            "reports a link to a folder outside the ARC",
            TestOptions(skip = not (canLink ())),
            fun () ->
                withDirectory (fun outside ->
                    withDirectory (fun dir -> promise {
                        do! writeBytesAsync (join [| outside; "o.png" |]) pngBytes
                        do! linkOutside outside dir

                        let! result = (serviceFor dir).CheckPaths [| linkedPath |]

                        match result with
                        | Ok checks -> Vitest.expect((checks = [| PathCheck.OutsideArc |])).toBe true
                        | Error error -> failwith error.Message
                    })
                )
        )

        Vitest.test (
            "reports the case on disk when the folder ignores case",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! _ = mkdirRecursiveAsync (join [| dir; "docs" |]) {| recursive = true |}
                    do! writeBytesAsync (join [| dir; "docs"; "a.md" |]) (utf8 "x")

                    let! caseInsensitive = promise {
                        try
                            let! _ = statAsync (join [| dir; "DOCS"; "A.MD" |])
                            return true
                        with _ ->
                            return false
                    }

                    if caseInsensitive then
                        let! result = (serviceFor dir).CheckPaths [| "Docs/A.md" |]

                        match result with
                        | Ok checks ->
                            Vitest.expect((checks = [| PathCheck.CaseDiffers("docs/a.md", false) |])).toBe true
                        | Error error -> failwith error.Message
                })
        )
)

let private makeFolder (path: string) : JS.Promise<unit> =
    mkdirRecursiveAsync path {| recursive = true |} |> Promise.map ignore

/// The names and kinds of a listing and whether it was cut.
let private entriesOf (listing: Result<FolderListing, exn>) =
    match listing with
    | Ok(FolderListing.Listed(entries, truncated)) ->
        entries |> Array.map (fun entry -> entry.name, entry.isDirectory), truncated
    | Ok other -> failwith $"Expected a listing, got {other}"
    | Error error -> failwith error.Message

let private outcomeOf (listing: Result<FolderListing, exn>) =
    match listing with
    | Ok(FolderListing.Listed _) -> "listed"
    | Ok FolderListing.NotAFolder -> "not a folder"
    | Ok FolderListing.OutsideArc -> "outside"
    | Error error -> $"error: {error.Message}"

Vitest.describe (
    "MarkdownResolverService.ListFolder",
    fun () ->
        Vitest.test (
            "lists the files and folders whose names start with the prefix, folders first, ignoring case",
            fun () ->
                withDirectory (fun dir -> promise {
                    do! makeFolder (join [| dir; "docs" |])
                    do! makeFolder (join [| dir; "Data" |])
                    do! makeFolder (join [| dir; ".arc" |])
                    do! writeBytesAsync (join [| dir; "b.md" |]) (utf8 "x")
                    do! writeBytesAsync (join [| dir; "A.png" |]) pngBytes

                    let! all = (serviceFor dir).ListFolder("", "")
                    let! startingWithD = (serviceFor dir).ListFolder("/", "D")

                    Vitest
                        .expect(entriesOf all)
                        .toEqual (
                            ([|
                                (".arc", true)
                                ("Data", true)
                                ("docs", true)
                                ("A.png", false)
                                ("b.md", false)
                             |],
                             false)
                        )

                    Vitest.expect(entriesOf startingWithD).toEqual (([| ("Data", true); ("docs", true) |], false))
                })
        )

        Vitest.test (
            "leaves out Git metadata, lock files, save temporaries and links",
            fun () ->
                withDirectory (fun outside ->
                    withDirectory (fun dir -> promise {
                        do! makeFolder (join [| dir; ".git" |])
                        do! writeBytesAsync (join [| dir; "a.md" |]) (utf8 "x")
                        do! writeBytesAsync (join [| dir; "~$Book.xlsx" |]) (utf8 "x")

                        do!
                            writeBytesAsync
                                (join [|
                                    dir
                                    ".swate-save-0123456789abcdef0123456789abcdef.tmp"
                                |])
                                (utf8 "x")

                        do! writeBytesAsync (join [| outside; "o.png" |]) pngBytes
                        do! TestHelpers.createFolderLinkAsync outside (join [| dir; "linked" |])

                        let! listing = (serviceFor dir).ListFolder("", "")
                        Vitest.expect(entriesOf listing).toEqual (([| ("a.md", false) |], false))
                    })
                )
        )

        Vitest.test (
            "lists a subfolder and tells a file, a missing folder and a path outside the ARC apart",
            fun () ->
                withDirectory (fun outside ->
                    withDirectory (fun dir -> promise {
                        do! makeFolder (join [| dir; "docs" |])
                        do! writeBytesAsync (join [| dir; "docs"; "guide.md" |]) (utf8 "x")
                        do! writeBytesAsync (join [| outside; "o.png" |]) pngBytes
                        do! TestHelpers.createFolderLinkAsync outside (join [| dir; "linked" |])
                        let service = serviceFor dir

                        let! inDocs = service.ListFolder("docs", "")
                        let! onFile = service.ListFolder("docs/guide.md", "")
                        let! missing = service.ListFolder("gone", "")
                        let! above = service.ListFolder("../x", "")
                        let! throughLink = service.ListFolder("linked", "")

                        Vitest.expect(entriesOf inDocs).toEqual (([| ("guide.md", false) |], false))
                        Vitest.expect(outcomeOf onFile).toBe "not a folder"
                        Vitest.expect(outcomeOf missing).toBe "not a folder"
                        Vitest.expect(outcomeOf above).toBe "outside"
                        Vitest.expect(outcomeOf throughLink).toBe "outside"
                    })
                )
        )

        Vitest.test (
            "cuts the listing at the limit and says so",
            fun () ->
                withDirectory (fun dir -> promise {
                    let! _ =
                        [| "a.md"; "b.md"; "c.md" |]
                        |> Array.map (fun name -> writeBytesAsync (join [| dir; name |]) (utf8 "x"))
                        |> Promise.all

                    let service = MarkdownResolverService(7, (fun () -> Some dir), 64.0, 2)
                    let! cut = service.ListFolder("", "")
                    let! fits = service.ListFolder("", "b")

                    Vitest.expect(entriesOf cut).toEqual (([| ("a.md", false); ("b.md", false) |], true))
                    Vitest.expect(entriesOf fits).toEqual (([| ("b.md", false) |], false))
                })
        )

        Vitest.test (
            "refuses a closed ARC",
            fun () -> promise {
                let! closed = MarkdownResolverService(7, (fun () -> None)).ListFolder("", "")
                Vitest.expect(outcomeOf closed).toBe "error: No ARC is open."
            }
        )
)
