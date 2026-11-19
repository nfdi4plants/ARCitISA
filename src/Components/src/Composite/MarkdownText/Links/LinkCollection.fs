[<RequireQualifiedAccess>]
module Swate.Components.Composite.MarkdownText.Links.LinkCollection

open Swate.Components.Composite.MarkdownText.JsBindings

/// A URL of a Markdown document with its range, as offsets into the text.
type CollectedLink = { Url: string; From: int; To: int }

/// The src of img elements and the href of a elements in raw HTML. The parser decodes entities and skips
/// comments, text and other attributes such as data-src.
let private htmlUrls (value: string) =
    let found = ResizeArray<string>()

    let rec walk (node: HastFromHtml.HastNode) =
        match node.``type``, node.tagName, node.properties with
        | "element", Some "img", Some properties -> properties.src |> Option.iter found.Add
        | "element", Some "a", Some properties -> properties.href |> Option.iter found.Add
        | _ -> ()

        match node.children with
        | Some children ->
            for child in children do
                walk child
        | None -> ()

    walk (HastFromHtml.fromHtml (value, {| fragment = true |}))
    found

/// The URLs of the links, images, definitions and raw HTML src and href attributes (each spans its whole html node) of a document, in document
/// order. A reference link has no URL of its own, its definition carries it.
let collect (text: string) : CollectedLink[] =
    let found = ResizeArray<CollectedLink>()

    let rec walk (node: Mdast.MdastNode) =
        match node.position with
        | Some position when not (isNull position.start) && not (isNull position.``end``) ->
            match node.``type`` with
            | "link"
            | "image"
            | "definition" ->
                match node.url with
                | Some url ->
                    found.Add {
                        Url = url
                        From = position.start.offset
                        To = position.``end``.offset
                    }
                | None -> ()
            | "html" ->
                match node.value with
                | Some value ->
                    for url in htmlUrls value do
                        found.Add {
                            Url = url
                            From = position.start.offset
                            To = position.``end``.offset
                        }
                | None -> ()
            | _ -> ()
        | _ -> ()

        match node.children with
        | Some children ->
            for child in children do
                walk child
        | None -> ()

    walk (Mdast.parse text)
    found.ToArray()
