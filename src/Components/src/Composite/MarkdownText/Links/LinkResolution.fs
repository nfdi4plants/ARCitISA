[<RequireQualifiedAccess>]
module Swate.Components.Composite.MarkdownText.Links.LinkResolution

open System
open System.Text.RegularExpressions
open Swate.Components.Composite.MarkdownText.Links.Types

let private schemeRegex = Regex(@"^[A-Za-z][A-Za-z0-9+.-]*:")

let private splitSegments (path: string) =
    path.Split('/') |> Array.filter (fun s -> s <> "" && s <> ".")

/// The folder of an ARC-relative file path, with / separators and no leading or trailing slash.
let baseFolderOf (documentPath: string) : string =
    let segments = splitSegments (documentPath.Replace("\\", "/"))

    if segments.Length <= 1 then
        ""
    else
        String.Join("/", segments.[0 .. segments.Length - 2])

/// Resolves the raw URL of a link or image against the ARC-relative folder of the open file.
let resolve (baseFolder: string) (raw: string) : LinkTarget =
    let unsupported reason = LinkTarget.Unsupported(raw, reason)
    let invalidEscape = "The link contains an invalid percent escape."
    let backslash = "Use / to separate folders."
    let url = raw.Trim()

    let decode (s: string) =
        try
            Some(Fable.Core.JS.decodeURIComponent s)
        with _ ->
            None

    if url = "" then
        unsupported "The link is empty."
    elif url.StartsWith "//" then
        unsupported "Use a full https:// URL."
    else
        let schemeMatch = schemeRegex.Match url

        if schemeMatch.Success then
            let scheme = schemeMatch.Value.TrimEnd(':').ToLowerInvariant()

            match scheme with
            | "http"
            | "https"
            | "mailto" -> LinkTarget.External raw
            | _ -> unsupported $"Links with the scheme \"{scheme}:\" cannot be opened."
        elif url.StartsWith "#" then
            match decode (url.Substring 1) with
            | Some id -> LinkTarget.Fragment id
            | None -> unsupported invalidEscape
        elif url.Contains "\\" then
            unsupported backslash
        else
            let hashIndex = url.IndexOf '#'

            let beforeHash, fragmentRaw =
                if hashIndex < 0 then
                    url, ""
                else
                    url.Substring(0, hashIndex), url.Substring(hashIndex + 1)

            let queryIndex = beforeHash.IndexOf '?'

            let pathRaw =
                if queryIndex < 0 then
                    beforeHash
                else
                    beforeHash.Substring(0, queryIndex)

            match decode pathRaw, decode fragmentRaw with
            | Some path, Some fragment ->
                if path.Contains "\\" then
                    unsupported backslash
                elif path |> Seq.exists (fun c -> c < ' ') then
                    unsupported "The link contains a control character."
                elif path = "" then
                    unsupported "The link has no path."
                else
                    let stack = ResizeArray<string>()
                    let outside = ref false

                    let walk (segments: string[]) =
                        for segment in segments do
                            if segment = ".." then
                                if stack.Count = 0 then
                                    outside.Value <- true
                                else
                                    stack.RemoveAt(stack.Count - 1)
                            else
                                stack.Add segment

                    if not (path.StartsWith "/") then
                        walk (splitSegments baseFolder)

                    walk (splitSegments path)

                    if outside.Value then
                        LinkTarget.OutsideArc raw
                    else
                        LinkTarget.ArcPath(String.Join("/", stack), (if fragment = "" then None else Some fragment))
            | _ -> unsupported invalidEscape
