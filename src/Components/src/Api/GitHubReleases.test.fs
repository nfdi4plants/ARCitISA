module internal Arcitisa.Components.Tests.GitHubReleases

open Vitest
open Arcitisa.Components.Api.GitHubReleases

let private release tag prerelease draft = {
    tag_name = tag
    name = Some tag
    body = Some "Notes"
    prerelease = prerelease
    draft = draft
}

Vitest.describe (
    "GitHub release selection",
    fun () ->
        let releases = [|
            release "v1.9.0" false false
            release "v2.0.0-alpha.10" true false
            release "v1.10.0" false false
            release "v3.0.0" false true
            release "nightly" false false
        |]

        Vitest.test (
            "stable installations select the highest published stable SemVer",
            fun () -> Vitest.expect((tryFindUpdate "1.8.0" releases).Value.tag_name).toBe "v1.10.0"
        )

        Vitest.test (
            "preview installations can update to newer previews",
            fun () -> Vitest.expect((tryFindUpdate "2.0.0-alpha.9" releases).Value.tag_name).toBe "v2.0.0-alpha.10"
        )

        Vitest.test (
            "equal and older releases produce no update",
            fun () ->
                Vitest.expect((tryFindUpdate "1.10.0" releases).IsNone).toBe true
                Vitest.expect((tryFindUpdate "4.0.0" releases).IsNone).toBe true
        )

        Vitest.test (
            "matches installed versions to prefixed release tags",
            fun () -> Vitest.expect(matchesVersion "1.10.0" (release "v1.10.0" false false)).toBe true
        )

        Vitest.test (
            "sorts versions numerically and excludes drafts",
            fun () ->
                let sorted = sortByVersion releases
                Vitest.expect(sorted.Length).toBe 4
                Vitest.expect(sorted.[0].tag_name).toBe "v2.0.0-alpha.10"
                Vitest.expect(sorted.[1].tag_name).toBe "v1.10.0"
        )
)
