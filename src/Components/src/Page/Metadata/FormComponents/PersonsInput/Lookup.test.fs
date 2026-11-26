module internal Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.LookupTests

open ARCtrl
open Fable.Core
open Vitest
open Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.Types
open Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.Lookup

let private json<'T> text : 'T = JS.JSON.parse text |> unbox

let private emptyResult = {
    orcid = "0000-0002-1825-0097"
    firstName = None
    lastName = None
    displayName = None
    email = None
    affiliation = None
}

Vitest.describe (
    "Person ORCID lookup",
    fun () ->
        Vitest.test (
            "normalizes canonical ORCID identifiers and links including checksum X",
            fun () ->
                for query in
                    [
                        "0000-0002-1825-0097"
                        " 0000-0002-1825-0097 "
                        "https://orcid.org/0000-0002-1825-0097"
                        "http://orcid.org/0000-0002-1825-0097/?source=test#section"
                    ] do
                    Vitest.expect(normalizeOrcid query).toEqual (Ok "0000-0002-1825-0097")

                Vitest.expect(normalizeOrcid "0000-0002-1694-233x").toEqual (Ok "0000-0002-1694-233X")
        )

        Vitest.test (
            "rejects malformed identifiers, foreign URLs, DOI values and invalid checksums",
            fun () ->
                for query in
                    [
                        ""
                        "10.1000/example"
                        "0000000218250097"
                        "0000-0002-1825-0098"
                        "https://example.org/0000-0002-1825-0097"
                        "https://orcid.org.evil.test/0000-0002-1825-0097"
                    ] do
                    Vitest.expect(Result.isError (normalizeOrcid query)).toBe true
        )

        Vitest.test (
            "quotes and escapes name tokens without inferring first and last names",
            fun () ->
                Vitest.expect(nameQuery "  Ada   Lovelace  ").toBe "given-and-family-names:(\"Ada\" AND \"Lovelace\")"
                Vitest.expect(nameQuery "A\"B C\\D").toBe "given-and-family-names:(\"A\\\"B\" AND \"C\\\\D\")"

                Vitest.expect(nameQuery "Smith OR Jones").toBe
                    "given-and-family-names:(\"Smith\" AND \"OR\" AND \"Jones\")"

                Vitest
                    .expect(nameSearchUrl "Ada & Lovelace" 10)
                    .toBe (
                        "https://pub.orcid.org/v3.0/expanded-search/?q="
                        + JS.encodeURIComponent "given-and-family-names:(\"Ada\" AND \"&\" AND \"Lovelace\")"
                        + "&rows=10&start=10"
                    )
        )

        Vitest.test (
            "maps only returned public names, email and current employment affiliations",
            fun () ->
                let person =
                    json<OrcidPerson>
                        """{"name":{"given-names":{"value":" Ada "},"family-name":{"value":"Lovelace"},"credit-name":{"value":"A. Lovelace"}},"emails":{"email":[{"email":""},{"email":"ada@example.org"}]}}"""

                let employments =
                    json<OrcidEmployments>
                        """{"affiliation-group":[{"summaries":[{"employment-summary":{"organization":{"name":"Example University"},"department-name":"Computing","end-date":null}}]},{"summaries":[{"employment-summary":{"organization":{"name":"Old College"},"department-name":"Computing","end-date":{"year":{"value":"2010"}}}}]},{"summaries":[{"employment-summary":{"organization":{"name":"Fixed Term Lab"},"end-date":{"year":{"value":"2999"},"month":{"value":"06"},"day":{"value":"30"}}}}]},{"summaries":[{"employment-summary":{"organization":{"name":"Institute"}}},{"employment-summary":{"organization":{"name":"Institute"},"department-name":"Other source"}}]}]}"""

                let result = mapOrcidPerson emptyResult.orcid (Some person) (Some employments)
                Vitest.expect(result.orcid).toBe emptyResult.orcid
                Vitest.expect(result.firstName).toEqual (Some "Ada")
                Vitest.expect(result.lastName).toEqual (Some "Lovelace")
                Vitest.expect(result.email).toEqual (Some "ada@example.org")

                Vitest
                    .expect(result.affiliation)
                    .toEqual (Some "Computing, Example University; Fixed Term Lab; Institute")

                Vitest.expect(fullName result).toBe "Ada Lovelace"
        )

        Vitest.test (
            "handles private or missing record fields without inferring metadata",
            fun () ->
                let result =
                    mapOrcidPerson emptyResult.orcid (Some(json<OrcidPerson> "{}")) (Some(json<OrcidEmployments> "{}"))

                Vitest.expect((mapOrcidPerson emptyResult.orcid None None).affiliation.IsNone).toBe true
                Vitest.expect(result.firstName.IsNone).toBe true
                Vitest.expect(result.lastName.IsNone).toBe true
                Vitest.expect(result.email.IsNone).toBe true
                Vitest.expect(result.affiliation.IsNone).toBe true
                Vitest.expect(fullName result).toBe emptyResult.orcid

                Vitest
                    .expect(
                        fullName {
                            result with
                                firstName = Some "Ada"
                                lastName = Some "Lovelace"
                        }
                    )
                    .toBe
                    "Ada Lovelace"

                Vitest
                    .expect(
                        fullName {
                            result with
                                displayName = Some "A. Lovelace"
                        }
                    )
                    .toBe
                    "A. Lovelace"
        )

        Vitest.test (
            "maps expanded search metadata directly and skips missing or invalid identifiers",
            fun () ->
                let raw =
                    json<OrcidExpandedResult>
                        """{"orcid-id":"0000-0002-1825-0097","given-names":"Ada","family-names":"Lovelace","credit-name":"A. Lovelace","email":["","ada@example.org"],"institution-name":["Example University","Example University","Institute"]}"""

                let result = (mapExpandedResult raw).Value
                Vitest.expect(result.firstName).toEqual (Some "Ada")
                Vitest.expect(result.lastName).toEqual (Some "Lovelace")
                Vitest.expect(result.displayName).toEqual (Some "A. Lovelace")
                Vitest.expect(result.email).toEqual (Some "ada@example.org")
                Vitest.expect(result.affiliation).toEqual (Some "Example University; Institute")
                Vitest.expect((mapExpandedResult (json<OrcidExpandedResult> "{}")).IsNone).toBe true

                Vitest.expect((mapExpandedResult (json<OrcidExpandedResult> """{"orcid-id":"invalid"}""")).IsNone).toBe
                    true
        )

        Vitest.test (
            "paginates expanded search by raw result count and stops at the public search limit",
            fun () ->
                let rawResults =
                    Array.create 10 """{"orcid-id":"0000-0002-1825-0097"}""" |> String.concat ","

                let response =
                    json<OrcidExpandedResponse> ("""{"expanded-result":[""" + rawResults + """],"num-found":25}""")

                Vitest.expect((mapExpandedPage 0 response).nextStart).toEqual (Some 10)
                Vitest.expect((mapExpandedPage 10 response).nextStart).toEqual (Some 20)
                Vitest.expect((mapExpandedPage 20 response).nextStart.IsNone).toBe true
                let empty = mapExpandedPage 0 (json<OrcidExpandedResponse> "{}")
                Vitest.expect(empty.results.Length).toBe 0
                Vitest.expect(empty.nextStart.IsNone).toBe true

                let limitResponse =
                    json<OrcidExpandedResponse> ("""{"expanded-result":[""" + rawResults + """],"num-found":20000}""")

                Vitest.expect((mapExpandedPage 9990 limitResponse).nextStart.IsNone).toBe true
        )

        Vitest.test (
            "preserves manually entered fields, roles and comments when applying public metadata",
            fun () ->
                let person =
                    Person(
                        firstName = "Manual",
                        lastName = "Name",
                        midInitials = "M",
                        email = "manual@example.org",
                        affiliation = "Manual affiliation",
                        phone = "123",
                        fax = "456",
                        address = "Address",
                        roles = ResizeArray [ OntologyAnnotation("Researcher") ],
                        comments = ResizeArray [ Comment("Note", "Keep") ]
                    )

                let result = {
                    emptyResult with
                        firstName = Some "Ada"
                        lastName = Some " "
                        affiliation = Some "University"
                }

                let merged = mergePerson person result
                Vitest.expect(merged.FirstName).toEqual (Some "Ada")
                Vitest.expect(merged.LastName).toEqual person.LastName
                Vitest.expect(merged.ORCID).toEqual (Some emptyResult.orcid)
                Vitest.expect(merged.Affiliation).toEqual (Some "University")
                Vitest.expect(merged.EMail).toEqual person.EMail
                Vitest.expect(merged.MidInitials).toEqual person.MidInitials
                Vitest.expect(merged.Phone).toEqual person.Phone
                Vitest.expect(merged.Fax).toEqual person.Fax
                Vitest.expect(merged.Address).toEqual person.Address
                Vitest.expect(merged.Roles.[0]).toEqual person.Roles.[0]
                Vitest.expect(merged.Comments.[0].Value).toEqual (Some "Keep")
                merged.Comments.[0].Value <- Some "Changed"
                Vitest.expect(person.Comments.[0].Value).toEqual (Some "Keep")
                Vitest.expect(person.FirstName).toEqual (Some "Manual")
        )

        Vitest.test (
            "distinguishes blocked anonymous access and retry-later errors",
            fun () ->
                for status in [ 401; 403 ] do
                    Vitest.expect((httpError status).StartsWith("Blocked: ORCID")).toBe true
                    Vitest.expect((httpError status).Contains("requires an access token or login")).toBe true

                Vitest.expect((httpError 429).Contains("try again later")).toBe true
                Vitest.expect((httpError 500).Contains("HTTP 500")).toBe true
        )
)

Vitest.describe (
    "ORCID employment end dates",
    fun () ->
        Vitest.test (
            "an employment is current until its end date has passed",
            fun () ->
                let today = System.DateTime(2026, 10, 15)

                let endDate (text: string) = json<OrcidDate> text |> Some

                Vitest.expect(isCurrentEmployment today None).toBe true
                Vitest.expect(isCurrentEmployment today (endDate "{}")).toBe true

                Vitest
                    .expect(
                        isCurrentEmployment
                            today
                            (endDate """{"year":{"value":"2026"},"month":{"value":"10"},"day":{"value":"15"}}""")
                    )
                    .toBe
                    true

                Vitest
                    .expect(isCurrentEmployment today (endDate """{"year":{"value":"2026"},"month":{"value":"10"}}"""))
                    .toBe
                    true

                Vitest.expect(isCurrentEmployment today (endDate """{"year":{"value":"2026"}}""")).toBe true

                Vitest
                    .expect(
                        isCurrentEmployment
                            today
                            (endDate """{"year":{"value":"2026"},"month":{"value":"09"},"day":{"value":"30"}}""")
                    )
                    .toBe
                    false

                Vitest.expect(isCurrentEmployment today (endDate """{"year":{"value":"2025"}}""")).toBe false
        )
)
