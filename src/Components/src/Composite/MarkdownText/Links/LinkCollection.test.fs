module internal Swate.Components.Composite.MarkdownText.Links.LinkCollectionTests

open Vitest
open Swate.Components.Composite.MarkdownText.Links

let private urls (text: string) =
    LinkCollection.collect text |> Array.map (fun l -> l.Url)

let private slice (text: string) (link: LinkCollection.CollectedLink) =
    text.Substring(link.From, link.To - link.From)

Vitest.describe (
    "LinkCollection.collect",
    fun () ->
        Vitest.test (
            "collects an inline link with the range of the whole link",
            fun () ->
                let text = "see [a](b.md) now"
                let links = LinkCollection.collect text
                Vitest.expect(links.Length).toBe 1
                Vitest.expect(links.[0].Url).toBe "b.md"
                Vitest.expect(slice text links.[0]).toBe "[a](b.md)"
        )

        Vitest.test ("collects an image", fun () -> Vitest.expect(urls "![alt](img.png)").toEqual [| "img.png" |])

        Vitest.test (
            "collects the definition of a reference link and not the reference",
            fun () ->
                let text = "[x][id]\n\n[id]: target.md"
                Vitest.expect(urls text).toEqual [| "target.md" |]
        )

        Vitest.test (
            "collects an image inside a link as two entries",
            fun () -> Vitest.expect(urls "[![alt](img.png)](page.md)").toEqual [| "page.md"; "img.png" |]
        )

        Vitest.test (
            "collects src and href of raw HTML",
            fun () -> Vitest.expect(urls "<img src=\"a.png\">\n\n<a href='b.md'>x</a>").toEqual [| "a.png"; "b.md" |]
        )

        Vitest.test (
            "decodes entities in raw HTML attributes",
            fun () -> Vitest.expect(urls "<img src=\"a&amp;b.png\">").toEqual [| "a&b.png" |]
        )

        Vitest.test ("ignores data-src", fun () -> Vitest.expect(urls "<img data-src=\"x.png\">").toEqual [||])

        Vitest.test (
            "ignores raw HTML inside a comment",
            fun () -> Vitest.expect(urls "<!-- <img src=\"c.png\"> -->").toEqual [||]
        )

        Vitest.test (
            "ignores src text outside a tag",
            fun () -> Vitest.expect(urls "<p>src=\"d.png\"</p>").toEqual [||]
        )

        Vitest.test (
            "gives a raw HTML link the range of its html node",
            fun () ->
                let text = "> quote\n>\n> <img src=\"a.png\">"
                let links = LinkCollection.collect text
                Vitest.expect(links.Length).toBe 1
                Vitest.expect(slice text links.[0]).toBe "<img src=\"a.png\">"
        )

        Vitest.test (
            "gives offsets into the text when it starts with a byte order mark",
            fun () ->
                let text = "\uFEFFsee [a](b.md)\n\n<img src=\"c.png\">"
                let links = LinkCollection.collect text
                Vitest.expect(links |> Array.map (slice text)).toEqual [| "[a](b.md)"; "<img src=\"c.png\">" |]
        )

        Vitest.test (
            "keeps document order",
            fun () ->
                Vitest.expect(urls "![i](1.png)\n\n[l](2.md)\n\n<img src=3.png>").toEqual [| "1.png"; "2.md"; "3.png" |]
        )
)
