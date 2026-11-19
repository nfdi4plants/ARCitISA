namespace Main.Services.MarkdownResolverService

open System
open Fable.Core
open Fetch
open Main.Bindings
open Main.Bindings.Filesystem
open Main.Helper
open Swate.Components.Shared
open Swate.Electron.Shared
open Swate.Electron.Shared.DTOs.MarkdownResolverDto

module internal ResponseFactory =

    [<Emit("new Response($0, { status: $1, headers: $2 })")>]
    let private create (body: obj) (status: int) (headers: (string * string)[]) : Response = jsNative

    /// A refusal has an empty body. The no-store header keeps the page from caching a refusal that a later save fixes.
    let refusal (status: int) : Response =
        create null status [| "Cache-Control", "no-store" |]

    /// An SVG can carry scripts. The sandbox policy disables them if the user opens the image URL as a document.
    let content (status: int) (contentType: string) (body: obj) : Response =
        let headers = [|
            "Content-Type", contentType
            "Cache-Control", "no-store"
            "X-Content-Type-Options", "nosniff"
        |]

        let headers =
            if contentType = "image/svg+xml" then
                Array.append headers [| "Content-Security-Policy", "sandbox" |]
            else
                headers

        create body status headers

/// One per window. Serves the images of the open ARC to Markdown documents and checks their link targets.
type MarkdownResolverService(windowId: int, getArcRoot: unit -> string option, ?maxImageBytes: float) =

    let maxBytes = defaultArg maxImageBytes AssetPolicy.defaultMaxImageBytes
    let mutable nonce = Crypto.randomHex 16

    let maxCheckedPaths = 500

    let refuseAndClose (handle: FileHandle) (refusal: AssetRefusal) = promise {
        try
            do! handle.close ()
        with _ ->
            ()

        return Error refusal
    }

    let refusalOfError (error: exn) =
        match tryGetNodeErrorCode error with
        | Some "ENOENT"
        | Some "ENOTDIR" -> AssetRefusal.NotFound
        | _ -> AssetRefusal.Unreadable error.Message

    /// Opens the file when it is an image that the window may see. The caller closes the handle.
    let inspectAsync (arcPath: string) : JS.Promise<Result<FileHandle * float * string, AssetRefusal>> = promise {
        match getArcRoot () with
        | None -> return Error AssetRefusal.ArcNotOpen
        | Some root ->
            match AssetPolicy.contentTypeOf arcPath with
            | Error extension -> return Error(AssetRefusal.UnsupportedType extension)
            | Ok contentType ->
                match ArcPathContainment.tryResolveArcRelativePath root arcPath with
                | Error _ -> return Error AssetRefusal.OutsideArc
                | Ok absolutePath ->
                    match! ArcPathContainment.checkTargetAsync root absolutePath with
                    | Error error -> return Error(refusalOfError error)
                    | Ok ArcPathContainment.ParentMissing
                    | Ok(ArcPathContainment.Missing _) -> return Error AssetRefusal.NotFound
                    | Ok ArcPathContainment.OutsideArc
                    | Ok ArcPathContainment.SymbolicLink -> return Error AssetRefusal.OutsideArc
                    | Ok ArcPathContainment.NotRegularFile -> return Error AssetRefusal.NotFound
                    | Ok(ArcPathContainment.RegularFile(stats, realParent)) ->
                        if stats.size > maxBytes then
                            return Error(AssetRefusal.TooLarge(stats.size, maxBytes))
                        else
                            let realPath = Path.join [| realParent; Path.basename absolutePath |]

                            let! opened = promise {
                                try
                                    let! handle = openAsync realPath "r"
                                    return Ok handle
                                with error ->
                                    return Error(refusalOfError error)
                            }

                            match opened with
                            | Error refusal -> return Error refusal
                            | Ok handle ->
                                try
                                    let! openStats = handle.stat ()

                                    if not (openStats.isFile ()) then
                                        return! refuseAndClose handle AssetRefusal.NotFound
                                    elif openStats.size > maxBytes then
                                        return! refuseAndClose handle (AssetRefusal.TooLarge(openStats.size, maxBytes))
                                    elif
                                        openStats.size > 0.0 && openStats.size < float AssetPolicy.lfsPointerProbeBytes
                                    then
                                        let buffer = allocBuffer AssetPolicy.lfsPointerProbeBytes
                                        let! read = handle.read (buffer, 0, int openStats.size, 0)

                                        if AssetPolicy.isLfsPointer buffer read.bytesRead then
                                            return! refuseAndClose handle AssetRefusal.LfsPointer
                                        else
                                            return Ok(handle, openStats.size, contentType)
                                    else
                                        return Ok(handle, openStats.size, contentType)
                                with error ->
                                    return! refuseAndClose handle (refusalOfError error)
    }

    /// The URL prefix of this window's images. It changes with the nonce.
    member _.AssetBaseUrl: string = MarkdownAssetUrl.baseUrl windowId nonce

    /// Invalidates every URL handed out so far. The vault calls it when the window opens another ARC or none.
    member _.Renew() : unit = nonce <- Crypto.randomHex 16

    member _.Authorizes(candidate: string) : bool = candidate = nonce

    /// Why the image is not served, or None when ServeAsset would serve it.
    member _.DescribeAsset(arcPath: string) : JS.Promise<AssetRefusal option> = promise {
        match! inspectAsync arcPath with
        | Error refusal -> return Some refusal
        | Ok(handle, _, _) ->
            try
                do! handle.close ()
            with _ ->
                ()

            return None
    }

    member _.ServeAsset(arcPath: string) : JS.Promise<Response> = promise {
        match! inspectAsync arcPath with
        | Error refusal -> return ResponseFactory.refusal (AssetPolicy.statusOf refusal)
        | Ok(handle, size, contentType) ->
            if size = 0.0 then
                try
                    do! handle.close ()
                with _ ->
                    ()

                return ResponseFactory.content 200 contentType null
            else
                let mutable stream: NodeReadStream option = None

                try
                    let created =
                        handle.createReadStream {|
                            start = 0.0
                            ``end`` = size - 1.0
                            autoClose = true
                        |}

                    stream <- Some created
                    return ResponseFactory.content 200 contentType (readableToWeb created)
                with _ ->
                    match stream with
                    | Some created -> created.destroy ()
                    | None ->
                        try
                            do! handle.close ()
                        with _ ->
                            ()

                    return ResponseFactory.refusal 500
    }

    /// One result per path, in the order of the input. Links of the Markdown documents use it to mark broken targets.
    member _.CheckPaths(arcPaths: string[]) : JS.Promise<Result<PathCheck[], exn>> = promise {
        match getArcRoot () with
        | _ when arcPaths.Length > maxCheckedPaths -> return Error(exn "At most 500 paths can be checked at once.")
        | None -> return Error(exn "No ARC is open.")
        | Some root ->
            try
                let! realRoot = realpathAsync root
                let realRootPrefixLength = realRoot.Replace('\\', '/').TrimEnd('/').Length

                let checkOne (requested: string) : JS.Promise<PathCheck> = promise {
                    let normalized = (PathHelpers.normalizePath requested).Trim('/')

                    if normalized = "" then
                        return PathCheck.Directory
                    else
                        match ArcPathContainment.tryResolveArcRelativePath root normalized with
                        | Error _ -> return PathCheck.OutsideArc
                        | Ok absolutePath ->
                            try
                                let! realPath = realpathAsync absolutePath

                                if not (ArcPathContainment.isSameOrInside realRoot realPath) then
                                    return PathCheck.OutsideArc
                                else
                                    let! stats = statAsync realPath
                                    let isDirectory = stats.isDirectory ()

                                    let onDisk = realPath.Replace('\\', '/').Substring(realRootPrefixLength).Trim('/')

                                    let! isPointer = promise {
                                        if isDirectory || stats.size >= float AssetPolicy.lfsPointerProbeBytes then
                                            return false
                                        else
                                            let! handle = openAsync realPath "r"

                                            let! outcome = promise {
                                                try
                                                    let buffer = allocBuffer AssetPolicy.lfsPointerProbeBytes
                                                    let! read = handle.read (buffer, 0, int stats.size, 0)
                                                    return Ok(AssetPolicy.isLfsPointer buffer read.bytesRead)
                                                with error ->
                                                    return Error error
                                            }

                                            // The handle closes on both paths before a read error reaches the mapping below.
                                            try
                                                do! handle.close ()
                                            with _ ->
                                                ()

                                            match outcome with
                                            | Ok isPointer -> return isPointer
                                            | Error error -> return raise error
                                    }

                                    if isPointer then
                                        return PathCheck.LfsPointer
                                    elif
                                        onDisk <> normalized
                                        && String.Equals(onDisk, normalized, StringComparison.OrdinalIgnoreCase)
                                    then
                                        return PathCheck.CaseDiffers(onDisk, isDirectory)
                                    elif isDirectory then
                                        return PathCheck.Directory
                                    else
                                        return PathCheck.File
                            with error ->
                                match tryGetNodeErrorCode error with
                                | Some "ENOENT"
                                | Some "ENOTDIR" -> return PathCheck.Missing
                                | _ -> return PathCheck.Unreadable error.Message
                }

                let! results = arcPaths |> Array.map checkOne |> Promise.all
                return Ok results
            with error ->
                return Error error
    }
