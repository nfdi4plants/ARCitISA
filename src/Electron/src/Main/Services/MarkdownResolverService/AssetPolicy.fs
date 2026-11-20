module Main.Services.MarkdownResolverService.AssetPolicy

open Swate.Components.Composite.MarkdownText.Links
open Swate.Electron.Shared.DTOs.MarkdownResolverDto

let defaultMaxImageBytes = 50.0 * 1024.0 * 1024.0

/// The content type of an image extension, or the extension (lowercased with its dot, "" when missing) of a type
/// that is not served. The types are those the path completion offers for images.
let contentTypeOf (arcPath: string) : Result<string, string> =
    let extension = ImageTypes.extensionOf arcPath

    match ImageTypes.contentTypeOf extension with
    | Some contentType -> Ok contentType
    | None -> Error extension

/// A Git LFS pointer file is a few lines of text. A file with at least this many bytes is never a pointer.
let lfsPointerProbeBytes = 1024

let private lfsPointerPrefix = "version https://git-lfs.github.com/spec/v1"

/// True when a link check opens the target to look for an LFS pointer. Only a regular file below the probe size
/// qualifies. A FIFO also reports a small size, and opening it would block until a writer appears.
let shouldProbeForLfsPointer (isRegularFile: bool) (sizeBytes: float) : bool =
    isRegularFile && sizeBytes < float lfsPointerProbeBytes

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
