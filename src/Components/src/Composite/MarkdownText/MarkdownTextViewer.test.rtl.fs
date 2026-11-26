module internal Arcitisa.Components.Composite.MarkdownText.MarkdownTextViewerTests

open Fable.Core
open Browser.Types
open Vitest
open Arcitisa.Components.Composite.MarkdownText
open Arcitisa.Components.Composite.MarkdownText.Links.Types

type private FakeContext() =
    member val OpenedArcPaths = ResizeArray<string>()
    member val OpenedUrls = ResizeArray<string>()
    member val Described = ResizeArray<string>()

    interface IMarkdownLinkContext with
        member _.baseFolder = "docs"
        member _.assetUrl p = Some("test-asset://" + p)

        member this.describeAsset p =
            this.Described.Add p
            Promise.lift (Some("Image not found: " + p))

        member this.openArcPath p = this.OpenedArcPaths.Add p
        member this.openExternal u = this.OpenedUrls.Add u

/// Replaces scrollIntoView, which jsdom does not implement, with a recorder of the ids it was called on.
[<Emit("(() => { const calls = []; Element.prototype.scrollIntoView = function () { calls.push(this.id); }; return calls; })()")>]
let private spyOnScrollIntoView () : ResizeArray<string> = jsNative

[<Emit("delete Element.prototype.scrollIntoView")>]
let private restoreScrollIntoView () : unit = jsNative

let private render (context: FakeContext option) (source: string) =
    RTL.render (
        MarkdownTextViewer.MarkdownTextViewer(
            source,
            ?linkContext = (context |> Option.map (fun c -> c :> IMarkdownLinkContext))
        )
    )

let private first (container: HTMLElement) (selector: string) =
    container.querySelector selector :?> HTMLElement

let private link (name: string) =
    RTL.screen.getByRole ("link", ByRoleOptions(name = Text name))

/// The text of a link the viewer cannot follow stays, outside an anchor, with a tooltip that gives the reason.
let private expectDeadLink (text: string) =
    let element = RTL.screen.getByText text
    Vitest.expect(element.tagName).not.toBe "A"
    Vitest.expect(System.String.IsNullOrWhiteSpace(element.getAttribute "title")).toBe false

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    restoreScrollIntoView ()
)

Vitest.describe (
    "MarkdownTextViewer with a link context",
    fun () ->
        Vitest.test (
            "a click on an ARC link opens the path relative to the base folder",
            fun () ->
                let context = FakeContext()
                render (Some context) "[o](./other.md) [r](/README.md)" |> ignore
                RTL.fireEvent.click (link "o")
                RTL.fireEvent.click (link "r")
                Vitest.expect(context.OpenedArcPaths).toEqual (ResizeArray [ "docs/other.md"; "README.md" ])
        )

        Vitest.test (
            "a click on an external link goes to the host",
            fun () ->
                let context = FakeContext()
                render (Some context) "[e](https://example.org/)" |> ignore
                RTL.fireEvent.click (link "e")
                Vitest.expect(context.OpenedUrls).toEqual (ResizeArray [ "https://example.org/" ])
        )

        Vitest.test (
            "a link outside the ARC is no link and explains itself",
            fun () ->
                let context = FakeContext()
                render (Some context) "[x](../../x.md)" |> ignore
                Vitest.expect(RTL.screen.queryAllByRole("link").Count).toBe 0
                expectDeadLink "x"
        )

        Vitest.test (
            "a Markdown image loads from the asset URL",
            fun () ->
                let rendered = render (Some(FakeContext())) "![A](img.png)"
                let img = first rendered.container "img"
                Vitest.expect(img.getAttribute "src").toBe "test-asset://docs/img.png"
                Vitest.expect(img.getAttribute "loading").toBe "lazy"
        )

        Vitest.test (
            "a raw HTML image loads from the asset URL and keeps its width",
            fun () ->
                let rendered =
                    render (Some(FakeContext())) "<img src=\"img.png\" width=\"200\" alt=\"R\">"

                let img = first rendered.container "img"
                Vitest.expect(img.getAttribute "src").toBe "test-asset://docs/img.png"
                Vitest.expect(img.getAttribute "width").toBe "200"
        )

        Vitest.test (
            "a failed image asks the host why and shows the answer",
            fun () -> promise {
                let context = FakeContext()
                let rendered = render (Some context) "<img src=\"img.png\" alt=\"R\">"
                RTL.fireEvent.custom ("error", first rendered.container "img")

                do!
                    RTL.waitFor (fun () ->
                        Vitest.expect(RTL.screen.getByText "Image not found: docs/img.png").toBeTruthy ()
                    )

                Vitest.expect(context.Described).toEqual (ResizeArray [ "docs/img.png" ])
            }
        )

        Vitest.test (
            "a click on a fragment link scrolls to the prefixed heading id",
            fun () ->
                let calls = spyOnScrollIntoView ()
                render (Some(FakeContext())) "## My Heading\n\n[go](#my-heading)" |> ignore
                RTL.fireEvent.click (link "go")
                Vitest.expect(calls).toEqual (ResizeArray [ "user-content-my-heading" ])
        )

        Vitest.test (
            "a click on a fragment link scrolls to a raw HTML anchor with an id and no href",
            fun () ->
                let calls = spyOnScrollIntoView ()
                render (Some(FakeContext())) "<a id=\"setup\"></a>\n\n[go](#setup)" |> ignore
                RTL.fireEvent.click (link "go")
                Vitest.expect(calls).toEqual (ResizeArray [ "user-content-setup" ])
        )

        Vitest.test (
            "the link icon of a heading scrolls to the heading",
            fun () ->
                let calls = spyOnScrollIntoView ()
                let rendered = render (Some(FakeContext())) "## My Heading"
                RTL.fireEvent.click (first rendered.container "h2 a")
                Vitest.expect(calls).toEqual (ResizeArray [ "user-content-my-heading" ])
        )

        Vitest.test (
            "a footnote reference renders and scrolls to its note",
            fun () ->
                let calls = spyOnScrollIntoView ()
                render (Some(FakeContext())) "Text[^1].\n\n[^1]: The note." |> ignore
                RTL.fireEvent.click (link "1")
                Vitest.expect(RTL.screen.getByText ("The note.", ByTextOptions(exact = false))).toBeTruthy ()
                Vitest.expect(calls).toEqual (ResizeArray [ "user-content-user-content-fn-1" ])
        )
)

Vitest.describe (
    "MarkdownTextViewer GitHub alerts",
    fun () ->
        Vitest.test (
            "an alert shows its kind as the title instead of the marker",
            fun () ->
                render None "> [!WARNING]\n> Mind the gap." |> ignore
                Vitest.expect(RTL.screen.getByText "Warning").toBeTruthy ()
                Vitest.expect(RTL.screen.getByText "Mind the gap.").toBeTruthy ()
                Vitest.expect((RTL.screen.queryByText ("[!WARNING]", ByTextOptions(exact = false))).IsNone).toBe true
        )

        Vitest.test (
            "a quote without a marker stays a quote",
            fun () ->
                let rendered = render None "> Just a quote."
                Vitest.expect((first rendered.container "blockquote").textContent.Trim()).toBe "Just a quote."
        )
)

Vitest.describe (
    "MarkdownTextViewer raw HTML",
    fun () ->
        Vitest.test (
            "drops event handler attributes",
            fun () ->
                let rendered =
                    render None "<img src=\"https://example.org/x.png\" onerror=\"alert(1)\">"

                Vitest.expect((first rendered.container "img").hasAttribute "onerror").toBe false
        )

        Vitest.test (
            "drops script elements",
            fun () ->
                let rendered = render None "<script>window.__x = 1</script>"
                Vitest.expect(isNull (rendered.container.querySelector "script")).toBe true
        )

        Vitest.test (
            "drops picture, source and srcset",
            fun () ->
                let rendered =
                    render
                        None
                        "<picture><source srcset=\"arc-asset://author/x 1x\"><img src=\"https://example.org/a.png\"></picture>"

                Vitest.expect(isNull (rendered.container.querySelector "picture")).toBe true
                Vitest.expect(isNull (rendered.container.querySelector "source")).toBe true
                Vitest.expect(isNull (rendered.container.querySelector "[srcset]")).toBe true
        )

        Vitest.test (
            "keeps details and summary",
            fun () ->
                let rendered = render None "<details><summary>S</summary>B</details>"
                Vitest.expect(isNull (rendered.container.querySelector "details")).toBe false
        )

        Vitest.test (
            "shows a notice for an image whose source the sanitizer removed",
            fun () ->
                let rendered = render None "<img src=\"arc-asset://author/x\">"
                Vitest.expect(isNull (rendered.container.querySelector "img")).toBe true
                Vitest.expect(RTL.screen.getByRole "img").toBeTruthy ()
        )
)

Vitest.describe (
    "MarkdownTextViewer without a link context",
    fun () ->
        Vitest.test (
            "an ARC link is no link and names the reason",
            fun () ->
                render None "[x](a.md)" |> ignore
                Vitest.expect(RTL.screen.queryAllByRole("link").Count).toBe 0
                expectDeadLink "x"
        )

        Vitest.test (
            "an ARC image becomes a notice",
            fun () ->
                let rendered = render None "![a](a.png)"
                Vitest.expect(isNull (rendered.container.querySelector "img")).toBe true
                Vitest.expect(RTL.screen.getByRole ("img", ByRoleOptions(name = Text "a"))).toBeTruthy ()
        )

        Vitest.test (
            "an external link keeps its href",
            fun () ->
                render None "[e](https://example.org/)" |> ignore
                Vitest.expect((link "e").getAttribute "href").toBe "https://example.org/"
        )
)
