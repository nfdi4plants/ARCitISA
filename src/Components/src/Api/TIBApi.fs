/// https://api.terminology.tib.eu/swagger-ui/index.html
module Arcitisa.Components.Api.TIBApi


open Arcitisa.Components
open Arcitisa.Components.Shared
open Fable.Core
open Fable.Core.JsInterop
open Fable.Remoting.Client
open Fetch
open Arcitisa.Components.Api.Helper


[<RequireQualifiedAccess>]
module TIBTypes =

    [<Literal>]
    let BaseAPIUrl = "https://api.terminology.tib.eu/api"

    type Term =
        abstract iri: string
        abstract lang: string
        abstract description: string[]
        abstract synonyms: string[]

        abstract label: string
        abstract ontology_name: string
        abstract ontology_prefix: string option
        abstract ontology_iri: string

        abstract is_obsolete: bool option
        abstract term_replaced_by: string option

        abstract short_form: string
        abstract obo_id: string

        abstract hasChildren: bool option

    type TermArray =
        abstract terms: Term[]

    type TermApi =
        abstract _embedded: TermArray option

    type SearchResults =
        abstract numFound: int
        abstract start: int
        abstract docs: Term[]

    type SearchApi =
        abstract response: SearchResults

    type SchemaValuesApi =
        abstract content: string[]
        abstract numberOfElements: int


[<AttachMembers>]
type TIBApi =
    static member tryGetIRIFromOboId(oboId: string) =
        fetch (appendQueryParams $"{TIBTypes.BaseAPIUrl}/terms" [ "obo_id", oboId ]) [
            RequestProperties.Method HttpMethod.GET
            requestHeaders [ HttpRequestHeaders.Accept "application/json" ]
        ]
        |> Promise.bind (fun response ->
            response.json<TIBTypes.TermApi> ()
            |> Promise.map (fun termApi ->
                if termApi._embedded.IsNone then
                    None
                else
                    termApi._embedded.Value.terms
                    |> Array.tryFind (fun term -> term.obo_id = oboId)
                    |> Option.map (fun term -> term.iri)
            )
        )

    // TODO: Maybe we should use allChildrenOf instead of childrenOf?
    // The latter only uses subclassOf/is_a, whereas the other one uses all relationships
    static member search
        (
            q: string,
            ?rows: int,
            ?obsoletes: bool,
            ?queryFields: ResizeArray<string>,
            ?childrenOf: string,
            ?collection: string
        ) =
        promise {
            let baseUrl = $"{TIBTypes.BaseAPIUrl}/search"
            let mutable childrenOf_ = None

            if childrenOf.IsSome then
                let! parentIri = TIBApi.tryGetIRIFromOboId childrenOf.Value
                childrenOf_ <- parentIri

            if childrenOf.IsSome && childrenOf_.IsNone then
                // exit condition should we not find the parent IRI
                return None
            else
                let queryParams: (string * obj) list = [
                    "q", q
                    if rows.IsSome then
                        "rows", rows.Value
                    if obsoletes.IsSome then
                        "obsoletes", obsoletes.Value
                    if queryFields.IsSome then
                        "queryFields", (queryFields.Value |> String.concat "," |> box)
                    if childrenOf_.IsSome then
                        "childrenOf", childrenOf_.Value
                    if collection.IsSome then
                        "schema", "collection"
                        "classification", collection.Value
                ]

                let url = appendQueryParams baseUrl queryParams

                return!
                    fetch url [
                        RequestProperties.Method HttpMethod.GET
                        requestHeaders [ HttpRequestHeaders.Accept "application/json" ]
                    ]
                    |> Promise.bind (fun response -> response.json<TIBTypes.SearchApi> ())
                    |> Promise.map Some
        }

    static member defaultSearch(q: string, ?rows: int, ?collection: string) =
        let rows = defaultArg rows 10

        TIBApi.search (
            q,
            rows = rows,
            obsoletes = false,
            queryFields = ResizeArray [ "label" ],
            ?collection = collection
        )

    static member searchChildrenOf(q: string, parentOboId: string, ?rows: int, ?collection: string) =
        let rows = defaultArg rows 10
        TIBApi.search (q, rows = rows, childrenOf = parentOboId, ?collection = collection)

    static member searchAllChildrenOf(parentOboId: string, ?rows: int, ?collection: string) =
        let rows = defaultArg rows 500
        TIBApi.search ("*", rows = rows, childrenOf = parentOboId, ?collection = collection)

    static member getCollections() =
        let url = $"{TIBTypes.BaseAPIUrl}/ontologies/schemavalues?schema=collection&lang=en"

        fetch url [
            RequestProperties.Method HttpMethod.GET
            requestHeaders [ HttpRequestHeaders.Accept "application/json" ]
        ]
        |> Promise.bind (fun response -> response.json<TIBTypes.SchemaValuesApi> ())
