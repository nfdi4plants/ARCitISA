module internal Swate.Components.Composite.MarkdownText.Links.LinkResolutionTests

open Vitest
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types

let private guide = "docs/guide"

let private reasonOf (baseFolder: string) (raw: string) =
    match LinkResolution.resolve baseFolder raw with
    | LinkTarget.Unsupported(_, reason) -> reason
    | other -> failwith $"Expected Unsupported for \"{raw}\", got {other}"

/// True when the link is refused. The reason is text for the user, so the tests leave it alone.
let private isUnsupported (baseFolder: string) (raw: string) =
    match LinkResolution.resolve baseFolder raw with
    | LinkTarget.Unsupported _ -> true
    | _ -> false

let private pathOf (baseFolder: string) (raw: string) =
    match LinkResolution.resolve baseFolder raw with
    | LinkTarget.ArcPath(path, fragment) -> path, fragment
    | other -> failwith $"Expected ArcPath for \"{raw}\", got {other}"

let private isOutside (baseFolder: string) (raw: string) =
    match LinkResolution.resolve baseFolder raw with
    | LinkTarget.OutsideArc _ -> true
    | _ -> false

Vitest.describe (
    "LinkResolution.resolve",
    fun () ->
        Vitest.test (
            "keeps http, https and mailto URLs as external, whatever the case",
            fun () ->
                for raw in
                    [
                        "https://example.org/x"
                        "HTTP://Example.org"
                        "mailto:a@b.c"
                    ] do
                    match LinkResolution.resolve guide raw with
                    | LinkTarget.External url -> Vitest.expect(url).toBe raw
                    | other -> failwith $"Expected External for \"{raw}\", got {other}"
        )

        Vitest.test (
            "rejects other schemes and names the scheme",
            fun () ->
                Vitest.expect(reasonOf guide "javascript:alert(1)").toContain "javascript:"
                Vitest.expect(reasonOf guide "file:///C:/x.png").toContain "file:"
                Vitest.expect(reasonOf guide "C:/x.png").toContain "c:"
        )

        Vitest.test (
            "rejects protocol-relative URLs",
            fun () -> Vitest.expect(isUnsupported guide "//cdn.example.org/a.png").toBe true
        )

        Vitest.test (
            "resolves a fragment link and decodes the id",
            fun () ->
                Vitest.expect(LinkResolution.resolve guide "#intro" = LinkTarget.Fragment "intro").toBe true
                Vitest.expect(LinkResolution.resolve guide "#my%20heading" = LinkTarget.Fragment "my heading").toBe true
        )

        Vitest.test (
            "rejects empty links and links without a path",
            fun () ->
                Vitest.expect(isUnsupported guide "").toBe true
                Vitest.expect(isUnsupported guide "   ").toBe true
                Vitest.expect(isUnsupported guide "?x=1").toBe true
        )

        Vitest.test (
            "rejects backslashes, also as percent escape",
            fun () ->
                Vitest.expect(isUnsupported guide "images\\a.png").toBe true
                Vitest.expect(isUnsupported guide "images%5Ca.png").toBe true
        )

        Vitest.test (
            "rejects invalid escapes and control characters",
            fun () ->
                Vitest.expect(isUnsupported guide "a%zz.png").toBe true
                Vitest.expect(isUnsupported guide "a%00.png").toBe true
        )

        Vitest.test (
            "resolves relative paths against the base folder",
            fun () ->
                Vitest.expect(pathOf guide "img.png").toEqual ("docs/guide/img.png", None)
                Vitest.expect(pathOf guide "./img.png").toEqual ("docs/guide/img.png", None)
                Vitest.expect(pathOf guide "../img.png").toEqual ("docs/img.png", None)
                Vitest.expect(pathOf guide "../../img.png").toEqual ("img.png", None)
        )

        Vitest.test (
            "reports paths that climb above the ARC root",
            fun () ->
                Vitest.expect(isOutside guide "../../../img.png").toBe true
                Vitest.expect(isOutside "" "../a.md").toBe true
        )

        Vitest.test (
            "resolves a path with a leading slash from the ARC root",
            fun () ->
                Vitest.expect(pathOf guide "/img.png").toEqual ("img.png", None)
                Vitest.expect(pathOf guide "/assays/a1/README.md#setup").toEqual ("assays/a1/README.md", Some "setup")
                Vitest.expect(pathOf guide "/").toEqual ("", None)
        )

        Vitest.test (
            "decodes the path and drops the query and an empty fragment",
            fun () ->
                Vitest.expect(pathOf guide "my%20image.png").toEqual ("docs/guide/my image.png", None)
                Vitest.expect(pathOf guide "img.png?raw=true").toEqual ("docs/guide/img.png", None)
                Vitest.expect(pathOf guide "x.md#").toEqual ("docs/guide/x.md", None)
        )

        Vitest.test (
            "normalizes dot segments and empty segments",
            fun () ->
                Vitest.expect(pathOf guide "sub/./x/../y.md").toEqual ("docs/guide/sub/y.md", None)
                Vitest.expect(pathOf guide "sub//y.md").toEqual ("docs/guide/sub/y.md", None)
        )

        Vitest.test (
            "normalizes the base folder",
            fun () ->
                Vitest.expect(pathOf "" "a.md").toEqual ("a.md", None)
                Vitest.expect(pathOf "/docs/" "a.md").toEqual ("docs/a.md", None)
                Vitest.expect(pathOf "docs/../guide" "a.md").toEqual ("guide/a.md", None)
        )
)

Vitest.describe (
    "LinkResolution.baseFolderOf",
    fun () ->
        Vitest.test (
            "returns the folder of a file path",
            fun () ->
                Vitest.expect(LinkResolution.baseFolderOf "a/b/c.md").toBe "a/b"
                Vitest.expect(LinkResolution.baseFolderOf "c.md").toBe ""
                Vitest.expect(LinkResolution.baseFolderOf "/a/c.md").toBe "a"
        )
)
