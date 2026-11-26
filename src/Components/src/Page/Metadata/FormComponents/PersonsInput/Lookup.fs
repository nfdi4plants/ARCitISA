module Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.Lookup

open System
open System.Text.RegularExpressions
open ARCtrl
open Fable.Core
open Fetch
open Arcitisa.Components.Page.Metadata.FormComponents.Helpers
open Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.Types

let private joinWith (separator: string) (values: string option[]) =
    values
    |> Array.choose nonblank
    |> Array.distinct
    |> String.concat separator
    |> Some
    |> nonblank

let normalizeOrcid (query: string) : Result<string, string> =
    let value = if isNull query then "" else query.Trim()

    let url =
        Regex.Match(value, @"^https?://orcid\.org/([^/?#]+)/?(?:[?#].*)?$", RegexOptions.IgnoreCase)

    let identifier =
        (if url.Success then url.Groups.[1].Value else value).ToUpperInvariant()

    if not (Regex.IsMatch(identifier, @"^\d{4}-\d{4}-\d{4}-\d{3}[\dX]$")) then
        Error "Enter an ORCID iD such as 0000-0002-1825-0097, or an orcid.org link."
    else
        let compact = identifier.Replace("-", "")

        let total =
            compact.Substring(0, 15)
            |> Seq.fold (fun total digit -> (total + int digit - int '0') * 2) 0

        let checksum = (12 - total % 11) % 11
        let expected = if checksum = 10 then "X" else string checksum

        if compact.Substring(15) = expected then
            Ok identifier
        else
            Error "This ORCID iD has an invalid checksum. Check the identifier and try again."

/// Quote every token so name input cannot introduce Solr fields or boolean operators.
let nameQuery (query: string) =
    let tokens =
        Regex.Split(query.Trim(), @"\s+")
        |> Array.filter (String.IsNullOrWhiteSpace >> not)

    let escaped =
        tokens
        |> Array.map (fun token -> "\"" + token.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
        |> String.concat " AND "

    "given-and-family-names:(" + escaped + ")"

let nameSearchUrl query start =
    "https://pub.orcid.org/v3.0/expanded-search/?q="
    + JS.encodeURIComponent (nameQuery query)
    + "&rows=10&start="
    + string (max 0 start)

/// Labels a result button. It prefers the given and family names, then the credit name, then the iD.
let fullName (result: PersonResult) =
    [ result.firstName; result.lastName ]
    |> List.choose nonblank
    |> String.concat " "
    |> Some
    |> nonblank
    |> Option.orElse (nonblank result.displayName)
    |> Option.defaultValue result.orcid

/// ORCID accepts end dates in the future, so an employment that ends today or later is still current.
/// A missing month or day counts as the end of that year or month.
let isCurrentEmployment (today: DateTime) (endDate: OrcidDate option) =
    let part (value: OrcidValue option) =
        value
        |> Option.bind (fun field -> field.value)
        |> Option.bind (fun text ->
            match Int32.TryParse text with
            | true, number -> Some number
            | _ -> None
        )

    match endDate |> Option.bind (fun date -> part date.year) with
    | None -> true
    | Some year ->
        let month =
            endDate
            |> Option.bind (fun date -> part date.month)
            |> Option.filter (fun month -> month >= 1 && month <= 12)
            |> Option.defaultValue 12

        let lastDay = DateTime.DaysInMonth(year, month)

        let day =
            endDate
            |> Option.bind (fun date -> part date.day)
            |> Option.filter (fun day -> day >= 1 && day <= lastDay)
            |> Option.defaultValue lastDay

        DateTime(year, month, day) >= today.Date

let mapOrcidPerson orcid (person: OrcidPerson option) (employments: OrcidEmployments option) : PersonResult =
    let name = person |> Option.bind (fun person -> person.name)

    let nameValue (selector: OrcidName -> OrcidValue option) =
        name
        |> Option.bind selector
        |> Option.bind (fun field -> field.value)
        |> nonblank

    let emails =
        person
        |> Option.bind (fun person -> person.emails)
        |> Option.bind (fun emails -> emails.email)
        |> Option.defaultValue [||]

    // A group holds the same employment as reported by different sources. Using its first summary keeps each employer once.
    // Only current employments describe the affiliation. Each one reads "Department, Organization".
    let affiliation =
        employments
        |> Option.bind (fun employments -> employments.``affiliation-group``)
        |> Option.defaultValue [||]
        |> Array.choose (fun group -> group.summaries |> Option.bind Array.tryHead)
        |> Array.choose (fun summary -> summary.``employment-summary``)
        |> Array.filter (fun summary -> isCurrentEmployment DateTime.Today summary.``end-date``)
        |> Array.map (fun summary ->
            [|
                summary.``department-name``
                summary.organization |> Option.bind (fun organization -> organization.name)
            |]
            |> joinWith ", "
        )
        |> joinWith "; "

    {
        orcid = orcid
        firstName = nameValue (fun name -> name.``given-names``)
        lastName = nameValue (fun name -> name.``family-name``)
        displayName = nameValue (fun name -> name.``credit-name``)
        email = emails |> Array.tryPick (fun email -> nonblank email.email)
        affiliation = affiliation
    }

let mapExpandedResult (result: OrcidExpandedResult) : PersonResult option =
    result.``orcid-id``
    |> nonblank
    |> Option.bind (fun identifier ->
        match normalizeOrcid identifier with
        | Error _ -> None
        | Ok orcid ->
            Some {
                orcid = orcid
                firstName = nonblank result.``given-names``
                lastName = nonblank result.``family-names``
                displayName = nonblank result.``credit-name``
                email = result.email |> Option.defaultValue [||] |> Array.tryPick (Some >> nonblank)
                affiliation =
                    result.``institution-name``
                    |> Option.defaultValue [||]
                    |> Array.map Some
                    |> joinWith "; "
            }
    )

let mapExpandedPage start (response: OrcidExpandedResponse) : SearchPage =
    let rawResults = response.``expanded-result`` |> Option.defaultValue [||]
    let next = max 0 start + rawResults.Length

    let nextStart =
        response.``num-found``
        |> Option.bind (fun count ->
            if rawResults.Length = 10 && next < min count 10000 then
                Some next
            else
                None
        )

    {
        results = rawResults |> Array.choose mapExpandedResult
        nextStart = nextStart
    }

let httpError status =
    match status with
    | 401
    | 403 ->
        "Blocked: ORCID requires an access token or login for this request. This lookup is unavailable without credentials."
    | 429 -> "ORCID is receiving too many requests. Please try again later."
    | _ -> $"ORCID could not load people (HTTP {status}). Please try again."

let private requestOptions = [
    requestHeaders [ HttpRequestHeaders.Accept "application/vnd.orcid+json" ]
    Credentials RequestCredentials.Omit
]

let private isBlocked (response: Response) =
    response.Status = 401 || response.Status = 403

/// Expects an ORCID iD that normalizeOrcid already accepted.
/// The full /record also lists every work and can be several megabytes, so this loads only the two sections it needs.
let getRecord (orcid: string) : JS.Promise<SearchPage> = promise {
    let baseUrl = "https://pub.orcid.org/v3.0/" + JS.encodeURIComponent orcid

    let! responses =
        Promise.all [|
            fetchUnsafe (baseUrl + "/person") requestOptions
            fetchUnsafe (baseUrl + "/employments") requestOptions
        |]

    let person = responses.[0]
    let employments = responses.[1]

    if person.Status = 404 then
        return { results = [||]; nextStart = None }
    elif isBlocked person || isBlocked employments then
        let status =
            if isBlocked person then
                person.Status
            else
                employments.Status

        return raise (LookupBlocked(httpError status))
    elif not person.Ok then
        return failwith (httpError person.Status)
    elif not employments.Ok && employments.Status <> 404 then
        return failwith (httpError employments.Status)
    else
        let! personPayload = person.json<OrcidPerson> ()

        let! employmentsPayload =
            if employments.Ok then
                employments.json<OrcidEmployments> () |> Promise.map Some
            else
                Promise.lift None

        return {
            results = [|
                mapOrcidPerson orcid (Some personPayload) employmentsPayload
            |]
            nextStart = None
        }
}

/// Expects a non-blank query.
let searchByName query start : JS.Promise<SearchPage> = promise {
    let! response = fetchUnsafe (nameSearchUrl query start) requestOptions

    if response.Status = 404 then
        return { results = [||]; nextStart = None }
    elif isBlocked response then
        return raise (LookupBlocked(httpError response.Status))
    elif not response.Ok then
        return failwith (httpError response.Status)
    else
        let! payload = response.json<OrcidExpandedResponse> ()
        return mapExpandedPage start payload
}

let mergePerson (person: Person) (result: PersonResult) =
    let merged = person.Copy()
    merged.ORCID <- Some result.orcid
    merged.FirstName <- nonblank result.firstName |> Option.orElse person.FirstName
    merged.LastName <- nonblank result.lastName |> Option.orElse person.LastName
    merged.EMail <- nonblank result.email |> Option.orElse person.EMail
    merged.Affiliation <- nonblank result.affiliation |> Option.orElse person.Affiliation
    merged
