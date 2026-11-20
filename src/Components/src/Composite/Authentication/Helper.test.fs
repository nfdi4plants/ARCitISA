module internal Swate.Components.Composite.Authentication.HelperTests

open Vitest
open Swate.Components.Composite.Authentication.Helper

let private supportedUrls (collection: DataHubCollection) =
    collection.Supported |> List.map _.Url |> Array.ofList

let private defaults = DataHubCollection.FromUrls Default_DataHub_Urls

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
                Vitest.expect(defaults.Offers "https://git.nfdi4plants.org").toBe true
                Vitest.expect(defaults.Offers "https://datahub.rz.rptu.de/").toBe true
                Vitest.expect(defaults.Offers "https://GIT.nfdi4plants.org").toBe true
                Vitest.expect(defaults.Offers "https://gitlab.example.org").toBe false
        )

        Vitest.test (
            "lists the official DataHUB first and each address once after it",
            fun () ->
                let collection =
                    DataHubCollection.FromUrls [
                        "https://gitlab.example.org"
                        "https://git.nfdi4plants.org"
                        "https://gitlab.example.org/"
                        "https://other.example.org"
                    ]

                Vitest.expect(collection.Default).toEqual Default_DataHub

                Vitest.expect(supportedUrls collection).toEqual [|
                    "https://gitlab.example.org"
                    "https://other.example.org"
                |]
        )

        Vitest.test (
            "offers only the official DataHUB once every other address is removed",
            fun () ->
                let collection = DataHubCollection.FromUrls []

                Vitest.expect(supportedUrls collection).toEqual [||]
                Vitest.expect(collection.Offers "https://git.nfdi4plants.org").toBe true
                Vitest.expect(collection.Offers "https://datahub.rz.rptu.de").toBe false
        )

        Vitest.test (
            "adds a new address in normalized form",
            fun () ->
                Vitest
                    .expect(
                        DataHubCollection.TryAddUrl(" https://new.example.org/ ", [| "https://gitlab.example.org" |])
                    )
                    .toEqual (
                        Some [|
                            "https://gitlab.example.org"
                            "https://new.example.org"
                        |]
                    )
        )

        Vitest.test (
            "adds nothing for the official, an already saved or an invalid address",
            fun () ->
                let saved = [| "https://gitlab.example.org" |]

                for url in
                    [
                        "https://git.nfdi4plants.org"
                        "https://gitlab.example.org/"
                        "http://new.example.org"
                        ""
                    ] do
                    Vitest.expect(DataHubCollection.TryAddUrl(url, saved)).toEqual None
        )
)
