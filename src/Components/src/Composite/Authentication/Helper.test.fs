module internal Swate.Components.Composite.Authentication.HelperTests

open Vitest
open Swate.Components.Composite.Authentication.Helper

let private supportedUrls (collection: DataHubCollection) =
    collection.Supported |> List.map _.Url |> Array.ofList

let private builtInSupportedUrls = supportedUrls Default_DataHub_Collection

Vitest.describe (
    "GitLabUrls.tryNormalize",
    fun () ->
        Vitest.test (
            "trims blanks and trailing slashes of an HTTPS address",
            fun () ->
                Vitest
                    .expect(GitLabUrls.tryNormalize "  https://gitlab.example.org//  ")
                    .toEqual (Some "https://gitlab.example.org")

                Vitest
                    .expect(GitLabUrls.tryNormalize "https://gitlab.example.org/group")
                    .toEqual (Some "https://gitlab.example.org/group")
        )

        Vitest.test (
            "gives spellings of one address the same form",
            fun () ->
                for spelling in
                    [
                        "HTTPS://GitLab.Example.org"
                        "https://gitlab.example.org:443/"
                    ] do
                    Vitest.expect(GitLabUrls.tryNormalize spelling).toEqual (Some "https://gitlab.example.org")

                Vitest.expect(GitLabUrls.tryNormalize "https://localhost:3443/").toEqual (Some "https://localhost:3443")
        )

        Vitest.test (
            "refuses blank, relative and non-HTTPS addresses",
            fun () ->
                for url in
                    [
                        ""
                        "   "
                        "https://"
                        "gitlab.example.org"
                        "http://gitlab.example.org"
                        "ftp://x.org"
                    ] do
                    Vitest.expect(GitLabUrls.tryNormalize url).toEqual None
        )
)

Vitest.describe (
    "DataHubInformation.FromUrl",
    fun () ->
        Vitest.test (
            "names a valid address without its scheme and keeps the address as typed",
            fun () ->
                let dataHub = DataHubInformation.FromUrl "https://gitlab.example.org/"

                Vitest.expect(dataHub.Name).toBe "gitlab.example.org"
                Vitest.expect(dataHub.Url).toBe "https://gitlab.example.org/"
        )

        Vitest.test (
            "keeps port and path in the name, so two DataHUBs on one host stay apart",
            fun () ->
                Vitest.expect((DataHubInformation.FromUrl "https://localhost:3443/").Name).toBe "localhost:3443"
                Vitest.expect((DataHubInformation.FromUrl "https://example.org/gitlab").Name).toBe "example.org/gitlab"
        )

        Vitest.test (
            "names an unfinished address by what was typed",
            fun () -> Vitest.expect((DataHubInformation.FromUrl "https://").Name).toBe "https://"
        )
)

Vitest.describe (
    "DataHubCollection",
    fun () ->
        Vitest.test (
            "offers its DataHUBs whether or not the address ends with a slash",
            fun () ->
                Vitest.expect(Default_DataHub_Collection.Offers "https://git.nfdi4plants.org").toBe true
                Vitest.expect(Default_DataHub_Collection.Offers "https://datahub.rz.rptu.de/").toBe true
                Vitest.expect(Default_DataHub_Collection.Offers "https://GIT.nfdi4plants.org").toBe true
                Vitest.expect(Default_DataHub_Collection.Offers "https://gitlab.example.org").toBe false
        )

        Vitest.test (
            "appends custom addresses after the supported DataHUBs and leaves out those it already offers",
            fun () ->
                let collection =
                    Default_DataHub_Collection.WithCustomUrls [
                        "https://gitlab.example.org"
                        "https://datahub.rz.rptu.de"
                        "https://gitlab.example.org/"
                        "https://other.example.org"
                    ]

                Vitest.expect(collection.Default).toEqual Default_DataHub_Collection.Default

                Vitest
                    .expect(supportedUrls collection)
                    .toEqual (
                        Array.append builtInSupportedUrls [|
                            "https://gitlab.example.org"
                            "https://other.example.org"
                        |]
                    )
        )

        Vitest.test (
            "adds a new address in normalized form",
            fun () ->
                Vitest
                    .expect(
                        Default_DataHub_Collection.TryAddCustomUrl(
                            " https://new.example.org/ ",
                            [| "https://gitlab.example.org" |]
                        )
                    )
                    .toEqual (
                        Some [|
                            "https://gitlab.example.org"
                            "https://new.example.org"
                        |]
                    )
        )

        Vitest.test (
            "adds nothing for a built-in, an already saved or an invalid address",
            fun () ->
                let saved = [| "https://gitlab.example.org" |]

                for url in
                    [
                        "https://git.nfdi4plants.org"
                        "https://gitlab.example.org/"
                        "http://new.example.org"
                        ""
                    ] do
                    Vitest.expect(Default_DataHub_Collection.TryAddCustomUrl(url, saved)).toEqual None
        )
)
