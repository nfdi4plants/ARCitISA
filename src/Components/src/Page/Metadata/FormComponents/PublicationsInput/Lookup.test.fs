module internal Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.LookupTests

open ARCtrl
open Fable.Core
open Vitest
open Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.Types
open Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.Lookup

let private json<'T> text : 'T = JS.JSON.parse text |> unbox

let private emptyResult = {
    key = "test"
    title = None
    authors = None
    doi = None
    pubMedId = None
    pmcId = None
    journal = None
    year = None
}

Vitest.describe (
    "Publication lookup",
    fun () ->
        Vitest.test (
            "normalizes bare, prefixed, and URL DOIs",
            fun () ->
                for value in
                    [
                        " 10.1000/example "
                        "DOI: 10.1000/example"
                        "https://doi.org/10.1000%2Fexample"
                        "http://dx.doi.org/10.1000/example?tracking=true"
                    ] do
                    Vitest.expect(normalizeDoi value).toEqual (Ok "10.1000/example")

                for value in
                    [
                        ""
                        "example"
                        "10.1000/with space"
                        "https://doi.org/10.1000/%ZZ"
                    ] do
                    Vitest.expect(Result.isError (normalizeDoi value)).toBe true
        )

        Vitest.test (
            "encodes DOI and Europe PMC query and cursor values",
            fun () ->
                Vitest.expect(crossrefUrl "10.1000/a&b").toBe "https://api.crossref.org/works/10.1000%2Fa%26b"

                Vitest
                    .expect(europePmcUrl "plant & cell" (Some "next+=/"))
                    .toBe (
                        "https://www.ebi.ac.uk/europepmc/webservices/rest/search?format=json&resultType=lite&pageSize=10&query=plant%20%26%20cell&cursorMark=next%2B%3D%2F"
                    )
        )

        Vitest.test (
            "translates PubMed and PMC identifiers while preserving text queries",
            fun () ->
                for query in [ "123456"; "PMID: 123456"; "pmid:123456" ] do
                    Vitest.expect(europePmcQuery query).toBe "EXT_ID:123456 AND SRC:MED"

                for query in [ "PMC123456"; "PMCID: PMC123456"; "pmc123456" ] do
                    Vitest.expect(europePmcQuery query).toBe "PMCID:PMC123456"

                Vitest.expect(europePmcQuery "  Smith plant biology  ").toBe "Smith plant biology"
        )

        Vitest.test (
            "maps Crossref metadata and formats individual and group authors",
            fun () ->
                let result =
                    json<CrossrefWork>
                        """{"DOI":"10.1000/example","title":[""," Example title "],"author":[{"given":"Ada","family":"Lovelace"},{"family":"Smith"},{"name":"Research consortium"},{}],"container-title":["Example journal"],"published":{"date-parts":[[2024,3,2]]}}"""
                    |> mapCrossrefWork

                Vitest.expect(result.title).toEqual (Some "Example title")
                Vitest.expect(result.authors).toEqual (Some "Ada Lovelace; Smith; Research consortium")
                Vitest.expect(result.doi).toEqual (Some "10.1000/example")
                Vitest.expect(result.journal).toEqual (Some "Example journal")
                Vitest.expect(result.year).toEqual (Some "2024")
                Vitest.expect(result.pubMedId.IsNone).toBe true
        )

        Vitest.test (
            "handles incomplete Crossref records and date fallbacks",
            fun () ->
                let empty = json<CrossrefWork> "{}" |> mapCrossrefWork
                Vitest.expect(empty.title.IsNone).toBe true
                Vitest.expect(empty.authors.IsNone).toBe true
                Vitest.expect(empty.year.IsNone).toBe true

                let fallback =
                    json<CrossrefWork>
                        """{"published":{"date-parts":[[]]},"published-online":{"date-parts":[[2023]]}}"""
                    |> mapCrossrefWork

                Vitest.expect(fallback.year).toEqual (Some "2023")

                let undated =
                    json<CrossrefWork> """{"issued":{"date-parts":[[null]]}}""" |> mapCrossrefWork

                Vitest.expect(undated.year.IsNone).toBe true

                let nullFallback =
                    json<CrossrefWork> """{"published":{"date-parts":[[null]]},"issued":{"date-parts":[[2022]]}}"""
                    |> mapCrossrefWork

                Vitest.expect(nullFallback.year).toEqual (Some "2022")
        )

        Vitest.test (
            "removes title markup and decodes entities without touching comparison signs",
            fun () ->
                for input, expected in
                    [
                        "Virulence Factors of Uropathogenic <i>Escherichia coli</i>",
                        "Virulence Factors of Uropathogenic Escherichia coli"
                        "<jats:title>Growth of <jats:italic toggle=\"yes\">Arabidopsis</jats:italic></jats:title>",
                        "Growth of Arabidopsis"
                        "H<sub>2</sub>O<sub>2</sub> signalling &amp; CO<sub>2</sub> uptake",
                        "H2O2 signalling & CO2 uptake"
                        "Effect of&#160;TNF-&#x3b1; on cells&#8217; survival", "Effect of TNF-α on cells’ survival"
                        "  Significance\twhen p < 0.05 and\u00A0n>3 ", "Significance when p < 0.05 and n>3"
                        "Study &lt;in vivo&gt; of &unknown; markers <br/>", "Study <in vivo> of &unknown; markers"
                        "Title<br/>Subtitle", "Title Subtitle"
                        "<B>Bold</B> and <span class=\"x\">span</span>", "Bold and span"
                        "if a<b and c>d", "if a<b and c>d"
                        "Vec<T> layout", "Vec<T> layout"
                    ] do
                    Vitest.expect(cleanTitle input).toBe expected

                let crossref =
                    json<CrossrefWork> """{"title":["<i>E. coli</i> growth"]}""" |> mapCrossrefWork

                Vitest.expect(crossref.title).toEqual (Some "E. coli growth")

                let europe =
                    json<EuropePmcResult> """{"title":"Motility in <i>E. coli</i>."}"""
                    |> mapEuropePmcResult

                Vitest.expect(europe.title).toEqual (Some "Motility in E. coli.")
        )

        Vitest.test (
            "only identifies explicit PubMed IDs and MED source identifiers as PubMed IDs",
            fun () ->
                let med =
                    json<EuropePmcResult> """{"id":"123456","source":"MED","title":"Example"}"""
                    |> mapEuropePmcResult

                Vitest.expect(med.pubMedId).toEqual (Some "123456")

                let pmc =
                    json<EuropePmcResult> """{"id":"PMC123","source":"PMC","pmcid":"PMC123"}"""
                    |> mapEuropePmcResult

                Vitest.expect(pmc.pubMedId.IsNone).toBe true
                Vitest.expect(pmc.pmcId).toEqual (Some "PMC123")

                let explicit =
                    json<EuropePmcResult> """{"id":"PMC123","source":"PMC","pmid":"456"}"""
                    |> mapEuropePmcResult

                Vitest.expect(explicit.pubMedId).toEqual (Some "456")

                let other =
                    json<EuropePmcResult> """{"id":"123","source":"AGR"}""" |> mapEuropePmcResult

                Vitest.expect(other.pubMedId.IsNone).toBe true
        )

        Vitest.test (
            "maps missing Europe PMC results and stops pagination on short or repeated pages",
            fun () ->
                let missing = json<EuropePmcResponse> "{}" |> mapEuropePmcPage None
                Vitest.expect(missing.results.Length).toBe 0
                Vitest.expect(missing.nextCursor.IsNone).toBe true

                let pageJson =
                    """{"resultList":{"result":["""
                    + (Array.create 10 "{}" |> String.concat ",")
                    + """ ]},"nextCursorMark":"next"}"""

                let full = json<EuropePmcResponse> pageJson |> mapEuropePmcPage None
                Vitest.expect(full.results.Length).toBe 10
                Vitest.expect(full.nextCursor).toEqual (Some "next")
                let repeated = json<EuropePmcResponse> pageJson |> mapEuropePmcPage (Some "next")
                Vitest.expect(repeated.nextCursor.IsNone).toBe true
                let initialCursorJson = pageJson.Replace("\"next\"", "\"*\"")

                let repeatedInitial =
                    json<EuropePmcResponse> initialCursorJson |> mapEuropePmcPage None

                Vitest.expect(repeatedInitial.nextCursor.IsNone).toBe true

                let short =
                    json<EuropePmcResponse> """{"resultList":{"result":[{}]},"nextCursorMark":"next"}"""
                    |> mapEuropePmcPage None

                Vitest.expect(short.nextCursor.IsNone).toBe true
        )

        Vitest.test (
            "merges nonblank metadata into a copy and preserves status and comments",
            fun () ->
                let original =
                    Publication(
                        title = "Manual title",
                        authors = "Manual authors",
                        doi = "10.1000/manual",
                        pubMedID = "123",
                        status = OntologyAnnotation("Published"),
                        comments = ResizeArray [ Comment("Note", "Keep me") ]
                    )

                let result = {
                    emptyResult with
                        title = Some " Found title "
                        authors = Some " "
                        doi = Some "10.1000/found"
                        pmcId = Some "PMC999"
                }

                let merged = mergePublication original result
                Vitest.expect(merged.Title).toEqual (Some "Found title")
                Vitest.expect(merged.DOI).toEqual (Some "10.1000/found")
                Vitest.expect(merged.Authors).toEqual original.Authors
                Vitest.expect(merged.PubMedID).toEqual original.PubMedID
                Vitest.expect(merged.Status).toEqual original.Status
                Vitest.expect(merged.Comments.[0].Value).toEqual (Some "Keep me")
                merged.Comments.[0].Value <- Some "Changed copy"
                Vitest.expect(original.Comments.[0].Value).toEqual (Some "Keep me")
                Vitest.expect(original.Title).toEqual (Some "Manual title")

                let withPmid =
                    mergePublication original {
                        emptyResult with
                            pubMedId = Some "456"
                    }

                Vitest.expect(withPmid.PubMedID).toEqual (Some "456")
        )
)
