module internal Arcitisa.Components.Page.Changelog.Tests

open Browser.Types
open Fable.Core
open Vitest
open Arcitisa.Components.Api.GitHubReleases

module private Helpers =
    [<Emit("Array.from($0.querySelectorAll('button')).map(button => button.textContent)")>]
    let buttonLabels (_nav: HTMLElement) : string[] = jsNative

    [<Emit("Array.from($0.parentElement.querySelectorAll(':scope > ul button')).map(button => button.textContent)")>]
    let childButtonLabels (_button: HTMLElement) : string[] = jsNative

    let expectChildren name expected =
        let button = RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))
        Vitest.expect(childButtonLabels button).toEqual expected

    [<Emit("$0.scrollIntoView = $1")>]
    let onScroll (_target: HTMLElement) (_callback: unit -> unit) : unit = jsNative

    let release tag body : Release = {
        tag_name = tag
        name = None
        body = body
        draft = false
        prerelease = false
    }

    let render releases =
        RTL.render (Changelog.Changelog((fun () -> promise { return releases }), "v2.0.0"))
        |> ignore

    let expectSections expected = promise {
        let! nav = RTL.screen.findByRole ("navigation", ByRoleOptions(name = Text "Release note sections"))
        do! RTL.waitFor (fun () -> Vitest.expect(buttonLabels nav).toEqual expected)
    }

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "counts categories, nested lists and subsections without counting fenced Markdown",
    fun () -> promise {
        Helpers.render [|
            Helpers.release
                "v2.0.0"
                (Some
                    "# Release\n\n## Added\n- One\n  - Nested\n- Two\n\n### Details\n1. Detail\n\n## Fixed\n- Bug\n\n## Changed\nNo changes.\n\n```md\n## Fake\n- Fake item\n```")
        |]

        do! Helpers.expectSections [| "Release5"; "Added4"; "Details1"; "Fixed1"; "Changed0" |]
        Helpers.expectChildren "Release 5" [| "Added4"; "Details1"; "Fixed1"; "Changed0" |]
        Helpers.expectChildren "Added 4" [| "Details1" |]
        Helpers.expectChildren "Changed 0" [||]

        Vitest.expect((RTL.screen.getByText "Counts include subsections.").textContent).toBe
            "Counts include subsections."
    }
)

Vitest.test (
    "skipped heading levels nest under the nearest shallower heading and overview stays separate",
    fun () -> promise {
        Helpers.render [|
            Helpers.release
                "v2.0.0"
                (Some
                    "- Intro\n\n### Early\n- Early item\n\n## Added\n- Direct\n\n#### Detail\n- Nested\n\n### More\n- Another\n\n## Fixed\n- Bug")
        |]

        do!
            Helpers.expectSections [|
                "Overview1"
                "Early1"
                "Added3"
                "Detail1"
                "More1"
                "Fixed1"
            |]

        Helpers.expectChildren "Overview 1" [||]
        Helpers.expectChildren "Early 1" [||]
        Helpers.expectChildren "Added 3" [| "Detail1"; "More1" |]
        Helpers.expectChildren "Detail 1" [||]
        Helpers.expectChildren "Fixed 1" [||]
    }
)

Vitest.test (
    "supports legacy setext headings, preambles, inline formatting and repeated headings",
    fun () -> promise {
        Helpers.render [|
            Helpers.release
                "v2.0.0"
                (Some
                    "- Intro\n\nOld **news**\n-----------\n\n1. First\n2. Second\n\n## Old news\n- Third\n\n##\n- Unnamed")
        |]

        do!
            Helpers.expectSections [|
                "Overview1"
                "Old news2"
                "Old news1"
                "Untitled section1"
            |]

        let headings =
            RTL.screen.getAllByRole ("heading", ByRoleOptions(name = Text "Old news"))

        let mutable scrolled = -1

        headings
        |> Seq.iteri (fun index heading -> Helpers.onScroll heading (fun () -> scrolled <- index))

        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Old news 1")))
        Vitest.expect(scrolled).toBe 1
    }
)

Vitest.test (
    "heading-free notes retain all content and count list items",
    fun () -> promise {
        Helpers.render [|
            Helpers.release "v2.0.0" (Some "Legacy notes\n\n- First\n- Second")
        |]

        do! Helpers.expectSections [| "Release notes2" |]
        Vitest.expect((RTL.screen.getByText "Legacy notes").textContent).toBe "Legacy notes"
    }
)

Vitest.test (
    "missing and blank notes have a zero-count fallback and navigation updates between releases",
    fun () -> promise {
        Helpers.render [|
            Helpers.release "v2.0.0" (Some "## Added\n- New")
            Helpers.release "v1.0.0" None
            Helpers.release "v0.9.0" (Some "  \n ")
        |]

        do! Helpers.expectSections [| "Added1" |]
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Next")))
        do! Helpers.expectSections [| "Release notes0" |]
        Vitest.expect((RTL.screen.getByText "No release notes provided.").textContent).toBe "No release notes provided."
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Next")))
        do! Helpers.expectSections [| "Release notes0" |]
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Previous")))
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Previous")))
        do! Helpers.expectSections [| "Added1" |]
    }
)
