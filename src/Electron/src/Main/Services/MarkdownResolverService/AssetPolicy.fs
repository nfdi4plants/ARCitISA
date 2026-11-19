module Main.Services.MarkdownResolverService.AssetPolicy

open Swate.Electron.Shared.DTOs.MarkdownResolverDto

let defaultMaxImageBytes = 50.0 * 1024.0 * 1024.0

/// The content type of an image extension, or the extension (lowercased with its dot, "" when missing) of a type
/// that is not served.
let contentTypeOf (arcPath: string) : Result<string, string> =
    let fileName = arcPath.Substring(arcPath.LastIndexOf '/' + 1)
    let dot = fileName.LastIndexOf '.'

    let extension =
        if dot < 0 then
            ""
        else
            fileName.Substring(dot).ToLowerInvariant()

    match extension with
    | ".png" -> Ok "image/png"
    | ".jpg"
    | ".jpeg" -> Ok "image/jpeg"
    | ".gif" -> Ok "image/gif"
    | ".svg" -> Ok "image/svg+xml"
    | ".webp" -> Ok "image/webp"
    | ".bmp" -> Ok "image/bmp"
    | _ -> Error extension

/// A Git LFS pointer file is a few lines of text. A file with at least this many bytes is never a pointer.
let lfsPointerProbeBytes = 1024

let private lfsPointerPrefix = "version https://git-lfs.github.com/spec/v1"

/// True when the first length bytes start with the LFS pointer header.
let isLfsPointer (bytes: byte[]) (length: int) : bool =
    length >= lfsPointerPrefix.Length
    && bytes.Length >= lfsPointerPrefix.Length
    && Seq.forall (fun i -> bytes[i] = byte lfsPointerPrefix[i]) (seq { 0 .. lfsPointerPrefix.Length - 1 })

/// The HTTP status of a refusal.
let statusOf (refusal: AssetRefusal) : int =
    match refusal with
    | AssetRefusal.ArcNotOpen -> 403
    | AssetRefusal.OutsideArc -> 403
    | AssetRefusal.NotFound -> 404
    | AssetRefusal.LfsPointer -> 409
    | AssetRefusal.TooLarge _ -> 413
    | AssetRefusal.UnsupportedType _ -> 415
    | AssetRefusal.Unreadable _ -> 500
