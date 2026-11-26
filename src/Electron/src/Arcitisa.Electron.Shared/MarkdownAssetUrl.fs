/// The URL format of ARC images in Markdown documents. The main process serves them, and the renderer builds them.
module Arcitisa.Electron.Shared.MarkdownAssetUrl

open System
open System.Text.RegularExpressions
open Fable.Core

[<Literal>]
let Scheme = "arc-asset"

/// The base URL of one window. The nonce changes whenever the window opens another ARC.
let baseUrl (windowId: int) (nonce: string) : string = $"{Scheme}://w{windowId}-{nonce}/"

/// Appends the ARC-relative path to the base URL. Each segment is percent-encoded on its own.
let build (baseUrl: string) (arcPath: string) : string =
    let segments = arcPath.Split('/') |> Array.map JS.encodeURIComponent
    baseUrl + String.Join("/", segments)

type ParsedAssetUrl = {
    WindowId: int
    Nonce: string
    ArcPath: string
}

/// Parses the host by hand. A parser that follows the URL standard accepts ports and user info, and the host check
/// has to reject both.
let tryParse (url: string) : ParsedAssetUrl option =
    let prefix = Scheme + "://"

    if
        url.Length <= prefix.Length
        || not (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    then
        None
    else
        let rest = url.Substring prefix.Length
        let slash = rest.IndexOf '/'

        if slash < 0 then
            None
        else
            let authority = rest.Substring(0, slash).ToLowerInvariant()
            let m = Regex.Match(authority, "^w([1-9][0-9]{0,8})-([0-9a-f]{32})$")

            if not m.Success then
                None
            else
                let tail = rest.Substring(slash + 1)
                let cut = tail.IndexOfAny [| '?'; '#' |]
                let pathPart = if cut >= 0 then tail.Substring(0, cut) else tail

                if pathPart = "" then
                    None
                else
                    try
                        let segments = pathPart.Split('/') |> Array.map JS.decodeURIComponent

                        // An escaped separator or control character would otherwise survive the decoding inside one segment.
                        let isUnsafe (s: string) =
                            s = ""
                            || s = "."
                            || s = ".."
                            || s.IndexOfAny [| '/'; '\\' |] >= 0
                            || s |> Seq.exists (fun c -> c < ' ')

                        if segments |> Array.exists isUnsafe then
                            None
                        else
                            Some {
                                WindowId = int m.Groups.[1].Value
                                Nonce = m.Groups.[2].Value
                                ArcPath = String.Join("/", segments)
                            }
                    with _ ->
                        None
