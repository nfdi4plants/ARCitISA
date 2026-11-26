module Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.Lookup

open System
open System.Text.RegularExpressions
open ARCtrl
open Fable.Core
open Fetch
open Arcitisa.Components.Page.Metadata.FormComponents.Helpers
open Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.Types

let normalizeDoi (query: string) : Result<string, string> =
    let value = if isNull query then "" else query.Trim()

    let urlPrefix =
        [
            "https://doi.org/"
            "http://doi.org/"
            "https://dx.doi.org/"
            "http://dx.doi.org/"
        ]
        |> List.tryFind (fun prefix -> value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

    try
        let doi =
            match urlPrefix with
            | Some prefix ->
                value.Substring(prefix.Length).Split([| '?'; '#' |]).[0]
                |> JS.decodeURIComponent
                |> fun text -> text.Trim()
            | None when value.StartsWith("doi:", StringComparison.OrdinalIgnoreCase) -> value.Substring(4).Trim()
            | None -> value

        if Regex.IsMatch(doi, @"^10\.\d{4,9}/\S+$") then
            Ok doi
        else
            Error "Enter a DOI such as 10.1000/example, or a doi.org link."
    with _ ->
        Error "This DOI link contains an invalid encoded character."

/// Expects a DOI that normalizeDoi already accepted.
let crossrefUrl (doi: string) =
    "https://api.crossref.org/works/" + JS.encodeURIComponent doi

/// Preserve free-text search syntax, but make recognizable identifiers exact searches.
let europePmcQuery (query: string) =
    let trimmed = query.Trim()
    let pmid = Regex.Match(trimmed, @"^(?:PMID\s*:\s*)?(\d+)$", RegexOptions.IgnoreCase)

    let pmcid =
        Regex.Match(trimmed, @"^(?:PMCID\s*:\s*)?(PMC\d+)$", RegexOptions.IgnoreCase)

    if pmid.Success then
        $"EXT_ID:{pmid.Groups.[1].Value} AND SRC:MED"
    elif pmcid.Success then
        "PMCID:" + pmcid.Groups.[1].Value.ToUpperInvariant()
    else
        trimmed

let europePmcUrl query cursor =
    let query = europePmcQuery query
    let cursor = cursor |> nonblank |> Option.defaultValue "*"

    "https://www.ebi.ac.uk/europepmc/webservices/rest/search?format=json&resultType=lite&pageSize=10&query="
    + JS.encodeURIComponent query
    + "&cursorMark="
    + JS.encodeURIComponent cursor

let formatCrossrefAuthor (author: CrossrefAuthor) =
    let personalName =
        [ author.given; author.family ]
        |> List.choose nonblank
        |> String.concat " "
        |> Some
        |> nonblank

    personalName |> Option.orElse (nonblank author.name)

let private firstText values =
    values |> Option.defaultValue [||] |> Array.tryPick (Some >> nonblank)

[<Emit("String.fromCodePoint($0)")>]
let private fromCodePoint (_codePoint: int) : string = jsNative

let private namedEntities =
    Map.ofList [
        "amp", "&"
        "lt", "<"
        "gt", ">"
        "quot", "\""
        "apos", "'"
        "nbsp", "\u00A0"
    ]

let private decodeEntity (entity: Match) =
    let hex = entity.Groups.[1].Value
    let dec = entity.Groups.[2].Value

    let codePoint =
        try
            if hex <> "" then Some(Convert.ToInt32(hex, 16))
            elif dec <> "" then Some(int dec)
            else None
        with _ ->
            None

    match codePoint with
    | Some value when value > 0 && value <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF) -> fromCodePoint value
    | Some _ -> entity.Value
    | None ->
        Map.tryFind entity.Groups.[3].Value namedEntities
        |> Option.defaultValue entity.Value

let private lineBreakTag = Regex(@"<br\s*\/?>", RegexOptions.IgnoreCase)

// Titles only use these inline tags. Any other "<" is text, as in "a<b and c>d" or "Vec<T>".
let private markupTag =
    Regex(
        @"<\/?(?:(?:jats|mml):[A-Za-z][\w.-]*|i|b|em|strong|sub|sup|sc|scp|u|span|small|italic|bold|a|p)(?:\s+[\w:.-]+\s*=\s*(?:""[^""]*""|'[^']*'))*\s*\/?>",
        RegexOptions.IgnoreCase
    )

/// Crossref titles carry JATS markup and Europe PMC titles carry HTML, but ARC titles are plain text.
/// It strips tags before it decodes entities, so an escaped "&lt;i&gt;" stays visible as text.
let cleanTitle (value: string) =
    if isNull value then
        ""
    else
        let withoutTags = markupTag.Replace(lineBreakTag.Replace(value, " "), "")

        let decoded =
            Regex.Replace(
                withoutTags,
                @"&(?:#[xX]([0-9A-Fa-f]{1,6})|#([0-9]{1,7})|([A-Za-z][A-Za-z0-9]{1,31}));",
                MatchEvaluator decodeEntity
            )

        Regex.Replace(decoded, @"\s+", " ").Trim()

let private crossrefYear (date: CrossrefDate option) =
    date
    |> Option.bind (fun value -> value.``date-parts``)
    |> Option.defaultValue [||]
    |> Array.tryPick (fun parts ->
        if isNull parts || parts.Length = 0 then
            None
        else
            // Crossref sends [[null]] for works without a known date.
            parts.[0] |> Option.map string
    )

let mapCrossrefWork (work: CrossrefWork) : SearchResult =
    let doi = nonblank work.DOI

    {
        key = "crossref:" + (doi |> Option.defaultValue "publication")
        title = firstText work.title |> Option.map cleanTitle |> nonblank
        authors =
            work.author
            |> Option.defaultValue [||]
            |> Array.choose formatCrossrefAuthor
            |> String.concat "; "
            |> Some
            |> nonblank
        doi = doi
        pubMedId = None
        pmcId = None
        journal = firstText work.``container-title``
        year =
            [
                work.published
                work.``published-print``
                work.``published-online``
                work.issued
            ]
            |> List.tryPick crossrefYear
    }

let mapEuropePmcResult (result: EuropePmcResult) : SearchResult =
    let source = nonblank result.source
    let id = nonblank result.id

    let pmid =
        nonblank result.pmid
        |> Option.orElseWith (fun () ->
            if source = Some "MED" then
                id |> Option.filter (fun value -> Regex.IsMatch(value, @"^\d+$"))
            else
                None
        )

    {
        key =
            (source |> Option.defaultValue "europepmc")
            + ":"
            + (id |> Option.defaultValue "publication")
        title = result.title |> Option.map cleanTitle |> nonblank
        authors = nonblank result.authorString
        doi = nonblank result.doi
        pubMedId = pmid
        pmcId = nonblank result.pmcid
        journal = nonblank result.journalTitle
        year = nonblank result.pubYear
    }

let mapEuropePmcPage cursor (response: EuropePmcResponse) : SearchPage =
    let requestedCursor = cursor |> nonblank |> Option.defaultValue "*"

    let results =
        response.resultList
        |> Option.bind (fun value -> value.result)
        |> Option.defaultValue [||]
        |> Array.map mapEuropePmcResult

    let nextCursor =
        response.nextCursorMark
        |> nonblank
        |> Option.filter (fun next -> results.Length = 10 && next <> requestedCursor)

    {
        results = results
        nextCursor = nextCursor
    }

// Network and parse failures reject with browser-specific text, so these messages name the provider.
let private unreachable provider =
    $"{provider} could not be reached. Please try again."

let private unreadable provider =
    $"ARCitISA could not read the response from {provider}. Please try again."

/// Expects a DOI that normalizeDoi already accepted.
let searchDoi doi : JS.Promise<SearchPage> = promise {
    // fetchUnsafe keeps non-2xx responses, so a 404 for an unknown DOI becomes an empty result.
    let! response =
        fetchUnsafe (crossrefUrl doi) [
            requestHeaders [ HttpRequestHeaders.Accept "application/json" ]
        ]
        |> Promise.catch (fun _ -> failwith (unreachable "Crossref"))

    if response.Status = 404 then
        return { results = [||]; nextCursor = None }
    elif not response.Ok then
        return failwith $"Crossref could not load this publication (HTTP {response.Status}). Please try again."
    else
        let! payload =
            response.json<CrossrefResponse> ()
            |> Promise.catch (fun _ -> failwith (unreadable "Crossref"))

        return {
            results =
                payload.message
                |> Option.map (mapCrossrefWork >> Array.singleton)
                |> Option.defaultValue [||]
            nextCursor = None
        }
}

/// Expects a non-blank query.
let searchEuropePmc query cursor : JS.Promise<SearchPage> = promise {
    let! response =
        fetchUnsafe (europePmcUrl query cursor) [
            requestHeaders [ HttpRequestHeaders.Accept "application/json" ]
        ]
        |> Promise.catch (fun _ -> failwith (unreachable "Europe PMC"))

    if not response.Ok then
        return failwith $"Europe PMC could not search publications (HTTP {response.Status}). Please try again."
    else
        let! payload =
            response.json<EuropePmcResponse> ()
            |> Promise.catch (fun _ -> failwith (unreadable "Europe PMC"))

        return mapEuropePmcPage cursor payload
}

/// Apply only returned metadata to a copy, retaining manual metadata and annotations.
let mergePublication (publication: Publication) (result: SearchResult) =
    let merged = publication.Copy()
    merged.Title <- nonblank result.title |> Option.orElse publication.Title
    merged.Authors <- nonblank result.authors |> Option.orElse publication.Authors
    merged.DOI <- nonblank result.doi |> Option.orElse publication.DOI
    merged.PubMedID <- nonblank result.pubMedId |> Option.orElse publication.PubMedID
    merged
