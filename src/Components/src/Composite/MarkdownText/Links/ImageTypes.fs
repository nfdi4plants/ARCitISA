/// The image types that the Markdown preview shows. The main process serves only these, and the path completion of
/// image targets offers only these. Plain functions without imports, so the main process can use them.
[<RequireQualifiedAccess>]
module Swate.Components.Composite.MarkdownText.Links.ImageTypes

/// The extension of the last path segment, lowercased with its dot, or "" when it has none.
let extensionOf (path: string) : string =
    let name = path.Substring(path.LastIndexOf '/' + 1)
    let dot = name.LastIndexOf '.'

    if dot < 0 then
        ""
    else
        name.Substring(dot).ToLowerInvariant()

/// The content type of an extension as extensionOf gives it, or None for a type the preview does not show.
let contentTypeOf (extension: string) : string option =
    match extension with
    | ".png" -> Some "image/png"
    | ".jpg"
    | ".jpeg" -> Some "image/jpeg"
    | ".gif" -> Some "image/gif"
    | ".svg" -> Some "image/svg+xml"
    | ".webp" -> Some "image/webp"
    | ".bmp" -> Some "image/bmp"
    | _ -> None

/// True when the preview shows the file at path as an image.
let isImage (path: string) : bool =
    (contentTypeOf (extensionOf path)).IsSome
