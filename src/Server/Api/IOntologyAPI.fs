module API.IOntologyAPI

open Arcitisa.Components.Shared
open Database

open Arcitisa.Components.Shared.DTOs
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open ARCtrl

module Helper =

    let getSearchModeFromQuery (query: string) =
        let searchTextLength = query.Length

        let searchmode =
            if searchTextLength < 3 then
                Database.FullTextSearch.Exact
            else
                Database.FullTextSearch.PerformanceComplete

        searchmode

    let getOntologiesModeFromList (ontologiesList: string list) =
        if ontologiesList.IsEmpty then
            None
        elif ontologiesList.Length = 1 then
            Term.AnyOfOntology.Single ontologiesList.Head |> Some
        else
            Term.AnyOfOntology.Multiples ontologiesList |> Some

    module V3 =
        open ARCtrl.Helper.Regex.ActivePatterns

        let searchSingleTerm credentials (content: TermQuery) = async {
            let dbSearchRes =
                match content.query.Trim() with
                | TermAnnotationShort taninfo ->
                    Term.Term(credentials).getByAccession $"{taninfo.IDSpace}:{taninfo.LocalID}"
                // This suggests we search for a term name
                | notAnAccession ->
                    let searchmode =
                        content.searchMode
                        |> Option.defaultWith (fun () -> getSearchModeFromQuery content.query)

                    let ontologies =
                        content.ontologies |> Option.defaultValue [] |> getOntologiesModeFromList

                    if content.parentTermId.IsSome then
                        Term
                            .Term(credentials)
                            .searchByParentStepwise (
                                notAnAccession,
                                content.parentTermId.Value,
                                searchmode,
                                ?limit = content.limit
                            )
                    elif searchmode = Database.FullTextSearch.Exact then
                        Term.Term(credentials).getByName (notAnAccession)
                    else
                        Term
                            .Term(credentials)
                            .searchByName (
                                notAnAccession,
                                searchmode,
                                ?limit = content.limit,
                                ?sourceOntologyName = ontologies
                            )
                |> Array.ofSeq

            return dbSearchRes
        }

        let searchChildTerms credentials (content: ParentTermQuery) = async {
            let dbSearchRes =
                Term.Term(credentials).findAllChildTerms (content.parentTermId, ?limit = content.limit)
                |> Array.ofSeq

            return dbSearchRes
        }

open Helper

[<RequireQualifiedAccess>]
module V3 =

    open Fable.Remoting.OpenAPI

    let docs =
        Fable.Remoting.OpenAPI.OpenApi.options
        |> OpenApi.withTitle "ARCitISA Ontology API"
        |> OpenApi.withServers [
            {
                Url = Arcitisa.Components.Shared.URLs.PRODUCTION_URL
                Description = Some "Production server"
            }
            {
                Url = Arcitisa.Components.Shared.URLs.PRODUCTION_URL_ALPHA
                Description = Some "Nightly server"
            }
            {
                Url = "/"
                Description = Some "Local development server"
            }
        ]
        |> OpenApi.withDescription "API for searching and retrieving ontology terms and trees in ARCitISA."
        |> OpenApi.withVersion "3.0.0"
        |> OpenApi.withEndpointDocsFor<IOntologyAPIv3, unit -> Async<int>> <@ fun api -> api.getTestNumber @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "Development health check"
                Description =
                    Some
                        "This endpoint returns a static integer value and is intended for development diagnostics only."
        }
        |> OpenApi.withEndpointDocsFor<IOntologyAPIv3, TermQuery -> Async<Term[]>> <@ fun api -> api.searchTerm @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "Search terms"
                Description =
                    Some
                        "Searches ontology terms by accession or name. If the query matches accession notation (for example `MS:1000031`), an accession lookup is performed. Otherwise, name-based search is used. If `searchMode` is omitted, the server uses `Exact` for queries shorter than 3 characters and `PerformanceComplete` otherwise. If `parentTermId` is supplied, the search is constrained to descendants of that parent."
        }
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, TermQuery, Term[]>
            <@ fun api -> api.searchTerm @>
            {
                Name = "Accession example"
                Summary = Some "Direct accession lookup"
                Description = Some "Looks up the term with accession `MS:1000031`."
                ExternalValue = None
            }
            (TermQuery.create ("MS:1000031"))
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, TermQuery, Term[]>
            <@ fun api -> api.searchTerm @>
            {
                Name = "Parent-scoped search example"
                Summary = Some "Name search constrained by parent"
                Description = Some "Searches for terms matching `leaf` under parent `PO:0025034` with a custom limit."
                ExternalValue = None
            }
            (TermQuery.create ("leaf", limit = 10, parentTermId = "PO:0025034"))
        |> OpenApi.withEndpointDocsFor<IOntologyAPIv3, TermQuery[] -> Async<TermQueryResults[]>>
            <@ fun api -> api.searchTerms @>
            {
                OpenApiDefaults.endpointDocumentation with
                    Summary = Some "Batch search terms"
                    Description =
                        Some
                            "Searches multiple term queries in one request. Each query is resolved independently, and the response preserves input order by returning one `TermQueryResults` item per input query."
            }
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, TermQuery[], TermQueryResults[]>
            <@ fun api -> api.searchTerms @>
            {
                Name = "Batch request example"
                Summary = Some "Multiple exact searches"
                Description = Some "Executes a batch of query objects and returns one result collection per query."
                ExternalValue = None
            }
            [|
                TermQuery.create ("MS:1000031", searchMode = Database.FullTextSearch.Exact)
                TermQuery.create ("Arabidopsis", searchMode = Database.FullTextSearch.Exact)
            |]
        |> OpenApi.withEndpointDocsFor<IOntologyAPIv3, string -> Async<Term option>> <@ fun api -> api.getTermById @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "Get a term by its accession"
                Description =
                    Some
                        "This endpoint retrieves a term from the database based on its unique accession identifier. The response will contain the term details if found, or null if no term with the given accession exists. If multiple terms are found for the same accession, the endpoint fails because accession values are expected to be unique."
        }
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, string, Term option>
            <@ fun api -> api.getTermById @>
            {
                Name = "Simple example"
                Summary = Some "Access by `:` notation"
                Description = Some "Returns the term with the given accession `MS:1000031`."
                ExternalValue = None
            }
            "MS:1000031"
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, string, Term option>
            <@ fun api -> api.getTermById @>
            {
                Name = "Not found example"
                Summary = Some "Non-existent accession"
                Description = Some "Returns null for a non-existent accession `RND:09090909`."
                ExternalValue = None
            }
            "RND:09090909"
        |> OpenApi.withEndpointDocsFor<IOntologyAPIv3, ParentTermQuery -> Async<ParentTermQueryResults>>
            <@ fun api -> api.searchChildTerms @>
            {
                OpenApiDefaults.endpointDocumentation with
                    Summary = Some "Find child terms"
                    Description =
                        Some
                            "Returns child terms for a given parent term accession. The response wraps the original query and the matching result terms. If no `limit` is supplied, the database default limit is applied."
            }
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, ParentTermQuery, ParentTermQueryResults>
            <@ fun api -> api.searchChildTerms @>
            {
                Name = "Child terms example"
                Summary = Some "List descendants for a parent"
                Description = Some "Returns child terms for parent accession `PO:0025034` with an explicit limit."
                ExternalValue = None
            }
            (ParentTermQuery.create ("PO:0025034", limit = 300))
        |> OpenApi.withEndpointDocsFor<IOntologyAPIv3, AdvancedSearchQuery -> Async<Term[]>>
            <@ fun api -> api.searchTermAdvanced @>
            {
                OpenApiDefaults.endpointDocumentation with
                    Summary = Some "Advanced term search"
                    Description =
                        Some
                            "Performs advanced full text search over term names and/or definitions. At least one of `TermName` or `TermDefinition` must be non-empty. Optionally filter by ontology via `OntologyName` and include obsolete terms via `KeepObsolete`."
            }
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, AdvancedSearchQuery, Term[]>
            <@ fun api -> api.searchTermAdvanced @>
            {
                Name = "Name + ontology filter example"
                Summary = Some "Search by name in one ontology"
                Description = Some "Searches for terms matching `instrument` in ontology `ms`."
                ExternalValue = None
            }
            {
                AdvancedSearchQuery.init () with
                    OntologyName = Some "ms"
                    TermName = "instrument"
            }
        |> OpenApi.withEndpointRequestNamedExampleFor<IOntologyAPIv3, AdvancedSearchQuery, Term[]>
            <@ fun api -> api.searchTermAdvanced @>
            {
                Name = "Definition example"
                Summary = Some "Search by definition text"
                Description = Some "Searches by term definition and includes obsolete terms."
                ExternalValue = None
            }
            {
                AdvancedSearchQuery.init () with
                    TermDefinition = "liquid chromatography"
                    KeepObsolete = true
            }

    let ontologyApi (credentials: Helper.Neo4JCredentials) : IOntologyAPIv3 = {
        //Development
        getTestNumber = fun () -> async { return 42 }
        searchTerm =
            fun content -> async {
                let! results = Helper.V3.searchSingleTerm credentials content
                return results
            }
        searchTerms =
            fun queries -> async {
                let asyncQueries = [
                    for query in queries do
                        Helper.V3.searchSingleTerm credentials query
                ]

                let! results = asyncQueries |> Async.Parallel
                let zipped = Array.map2 (fun a b -> TermQueryResults.create (a, b)) queries results
                return zipped
            }
        getTermById =
            fun id -> async {
                let dbSearchRes = Term.Term(credentials).getByAccession id

                return
                    match Seq.length dbSearchRes with
                    | 1 -> dbSearchRes |> Seq.head |> Some
                    | 0 -> None
                    | _ -> failwith $"Found multiple terms with the same accession: {id}" // must be multiples as negative cannot exist for length
            }
        searchChildTerms =
            fun (dto: ParentTermQuery) -> async {
                let! results = Helper.V3.searchChildTerms credentials dto
                let zipped = ParentTermQueryResults.create (dto, results)
                return zipped
            }
        searchTermAdvanced =
            fun (dto: AdvancedSearchQuery) -> async {
                let results = Term.Term(credentials).getByAdvancedTermSearch (dto) |> Array.ofSeq
                return results
            }
    }

    let createIOntologyApi credentials =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue (ontologyApi credentials)
        |> Remoting.withDiagnosticsLogger (printfn "%A")
        |> Remoting.withErrorHandler Helper.errorHandler
        |> Remoting.OpenAPI.withDocs docs

open Arcitisa.Components.Shared

[<RequireQualifiedAccess>]
module V1 =

    /// <summary>Deprecated</summary>
    let ontologyApi (credentials: Helper.Neo4JCredentials) : IOntologyAPIv1 =
        /// We use sorensen dice to avoid scoring mutliple occassions of the same word (Issue https://github.com/nfdi4plants/ARCitISA/issues/247)
        let sorensenDiceSortTerms (searchStr: string) (terms: Term[]) =
            terms |> SorensenDice.sortBySimilarity searchStr (fun term -> term.Name)

        {
            //Development

            getTestNumber = fun () -> async { return 42 }

            //Ontology related requests

            getAllOntologies =
                fun () -> async {
                    let results = Ontology.Ontology(credentials).getAll () |> Array.ofSeq
                    return results
                }

            // Term related requests

            getTermSuggestions =
                fun (max: int, typedSoFar: string) -> async {
                    let dbSearchRes =
                        match typedSoFar with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Term.Term(credentials).getByAccession foundAccession.Value
                        // This suggests we search for a term name
                        | notAnAccession ->
                            let searchTextLength = typedSoFar.Length

                            let searchmode =
                                if searchTextLength < 3 then
                                    Database.FullTextSearch.Exact
                                else
                                    Database.FullTextSearch.PerformanceComplete

                            Term.Term(credentials).searchByName (notAnAccession, searchmode)
                        |> Array.ofSeq

                    let arr =
                        if dbSearchRes.Length <= max then
                            dbSearchRes
                        else
                            Array.take max dbSearchRes

                    return arr
                }

            getTermSuggestionsByParentTerm =
                fun (max: int, typedSoFar: string, parentTerm: ArcitisaObsolete.TermMinimal) -> async {
                    let dbSearchRes =
                        match typedSoFar with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Database.Term.Term(credentials).getByAccession foundAccession.Value
                        | _ ->
                            let searchmode =
                                if typedSoFar.Length < 3 then
                                    Database.FullTextSearch.Exact
                                else
                                    Database.FullTextSearch.PerformanceComplete

                            if parentTerm.TermAccession = "" then
                                Term.Term(credentials).getByNameAndParent_Name (typedSoFar, parentTerm.Name, searchmode)
                            else
                                Term.Term(credentials).getByNameAndParent (typedSoFar, parentTerm, searchmode)
                        |> Array.ofSeq

                    let arr =
                        if dbSearchRes.Length <= max then
                            dbSearchRes
                        else
                            Array.take max dbSearchRes

                    return arr
                }

            getAllTermsByParentTerm =
                fun (parentTerm: ArcitisaObsolete.TermMinimal) -> async {
                    let searchRes =
                        Database.Term.Term(credentials).getAllByParent (parentTerm, limit = 500)
                        |> Array.ofSeq

                    return searchRes
                }

            getTermSuggestionsByChildTerm =
                fun (max: int, typedSoFar: string, childTerm: ArcitisaObsolete.TermMinimal) -> async {

                    let dbSearchRes =
                        match typedSoFar with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Term.Term(credentials).getByAccession foundAccession.Value
                        | _ ->
                            if childTerm.TermAccession = "" then
                                Term
                                    .Term(credentials)
                                    .getByNameAndChild_Name (
                                        typedSoFar,
                                        childTerm.Name,
                                        FullTextSearch.PerformanceComplete
                                    )
                            else
                                Term
                                    .Term(credentials)
                                    .getByNameAndChild (
                                        typedSoFar,
                                        childTerm.TermAccession,
                                        FullTextSearch.PerformanceComplete
                                    )
                        |> Array.ofSeq
                    //|> sorensenDiceSortTerms typedSoFar
                    let res =
                        if dbSearchRes.Length <= max then
                            dbSearchRes
                        else
                            Array.take max dbSearchRes

                    return res
                }

            getAllTermsByChildTerm =
                fun (childTerm: ArcitisaObsolete.TermMinimal) -> async {
                    let searchRes = Term.Term(credentials).getAllByChild (childTerm) |> Array.ofSeq
                    return searchRes
                }

            getTermsForAdvancedSearch =
                fun advancedSearchOption -> async {
                    let result =
                        Term.Term(credentials).getByAdvancedTermSearch (advancedSearchOption)
                        |> Array.ofSeq

                    let filteredResult =
                        if advancedSearchOption.KeepObsolete then
                            result
                        else
                            result |> Array.filter (fun x -> x.IsObsolete |> not)

                    return filteredResult
                }

            getUnitTermSuggestions =
                fun (max: int, typedSoFar: string) -> async {
                    let dbSearchRes =
                        match typedSoFar with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Term.Term(credentials).getByAccession foundAccession.Value
                        | notAnAccession ->
                            Term
                                .Term(credentials)
                                .searchByName (notAnAccession, sourceOntologyName = Term.AnyOfOntology.Single "uo")
                        |> Array.ofSeq
                    //|> sorensenDiceSortTerms typedSoFar
                    let res =
                        if dbSearchRes.Length <= max then
                            dbSearchRes
                        else
                            Array.take max dbSearchRes

                    return res
                }

            getTermsByNames =
                fun queryArr -> async {
                    // check if search string is empty. This case should delete TAN- and TSR- values in table
                    let filteredQueries =
                        queryArr
                        |> Array.filter (fun x -> x.Term.Name <> "" || x.Term.TermAccession <> "")

                    let queries =
                        filteredQueries
                        |> Array.map (fun searchTerm ->
                            // check if term accession was found. If so search also by this as it is unique
                            if searchTerm.Term.TermAccession <> "" then
                                Term.TermQuery.getByAccession searchTerm.Term.TermAccession
                            // if term is a unit it should be contained inside the unit ontology, if not it is most likely free text input.
                            elif searchTerm.IsUnit then
                                Term.TermQuery.getByName (
                                    searchTerm.Term.Name,
                                    searchType = FullTextSearch.Exact,
                                    sourceOntologyName = Term.AnyOfOntology.Single "uo"
                                )
                            // if none of the above apply we do a standard term search
                            else
                                Term.TermQuery.getByName (searchTerm.Term.Name, searchType = FullTextSearch.Exact)
                        )

                    let result =
                        Helper.Neo4j.runQueries (queries, credentials)
                        |> Array.map2
                            (fun (termSearchable: ArcitisaObsolete.TermSearchable) dbResults ->
                                // replicate if..elif..else conditions from 'queries'
                                if termSearchable.Term.TermAccession <> "" then
                                    let result =
                                        if Array.isEmpty dbResults then
                                            None
                                        else
                                            // search by accession must be unique, and has unique restriction in database, so there can only be 0 or 1 result
                                            let r = dbResults |> Array.exactlyOne

                                            if r.Name <> termSearchable.Term.Name then
                                                failwith
                                                    $"""Found mismatch between Term Accession and Term Name. Term name "{termSearchable.Term.Name}" and term accession "{termSearchable.Term.TermAccession}",
                                            but accession belongs to name "{r.Name}" (ontology: {r.FK_Ontology})"""

                                            Some r

                                    {
                                        termSearchable with
                                            SearchResultTerm = result
                                    }
                                // search is done by name and only in the unit ontology. Therefore unit term must be unique.
                                // This might need future work, as we might want to support types of unit outside of the unit ontology
                                elif termSearchable.IsUnit then
                                    {
                                        termSearchable with
                                            SearchResultTerm =
                                                if dbResults |> Array.isEmpty then
                                                    None
                                                else
                                                    Some dbResults.[0]
                                    }
                                else
                                    {
                                        termSearchable with
                                            SearchResultTerm =
                                                if dbResults |> Array.isEmpty then
                                                    None
                                                else
                                                    Some dbResults.[0]
                                    }
                            )
                            filteredQueries

                    return result
                }

            // Tree related requests
            getTreeByAccession =
                fun accession -> async {
                    let tree = Database.TreeSearch.Tree(credentials).getByAccession (accession)
                    return tree
                }
        }

    open Helper

    let createIOntologyApi credentials =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue (ontologyApi credentials)
        |> Remoting.withDiagnosticsLogger (printfn "%A")
        |> Remoting.withErrorHandler errorHandler
        |> Remoting.OpenAPI.withDocs Fable.Remoting.OpenAPI.OpenApi.options

[<RequireQualifiedAccess>]
module V2 =

    let ontologyApi (credentials: Helper.Neo4JCredentials) : IOntologyAPIv2 =
        /// <summary>We use sorensen dice to avoid scoring mutliple occassions of the same word (Issue https://github.com/nfdi4plants/ARCitISA/issues/247)</summary>
        let sorensenDiceSortTerms (searchStr: string) (terms: Term[]) =
            terms |> SorensenDice.sortBySimilarity searchStr (fun term -> term.Name)

        {
            //Development

            getTestNumber = fun () -> async { return 42 }

            //Ontology related requests

            getAllOntologies =
                fun () -> async {
                    let results = Ontology.Ontology(credentials).getAll () |> Array.ofSeq
                    return results
                }

            // Term related requests

            getTermSuggestions =
                fun inp -> async {
                    let dbSearchRes =
                        match inp.query with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Term.Term(credentials).getByAccession foundAccession.Value
                        // This suggests we search for a term name
                        | notAnAccession ->
                            let searchTextLength = inp.query.Length

                            let searchmode =
                                if searchTextLength < 3 then
                                    Database.FullTextSearch.Exact
                                else
                                    Database.FullTextSearch.PerformanceComplete

                            Term
                                .Term(credentials)
                                .searchByName (
                                    notAnAccession,
                                    searchmode,
                                    ?sourceOntologyName = Option.map Term.AnyOfOntology.Single inp.ontology
                                )
                        |> Array.ofSeq
                    //|> sorensenDiceSortTerms typedSoFar
                    let arr =
                        if dbSearchRes.Length <= inp.n then
                            dbSearchRes
                        else
                            Array.take inp.n dbSearchRes

                    let arrSorted = sorensenDiceSortTerms inp.query arr
                    return arrSorted
                }

            getTermSuggestionsByParentTerm =
                fun inp ->
                    printfn "[getTermSuggestionsByParentTerm] start"

                    async {
                        let dbSearchRes =
                            match inp.query with
                            | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                                Database.Term.Term(credentials).getByAccession foundAccession.Value
                            | _ ->
                                printfn "[getTermSuggestionsByParentTerm] Hit default search"

                                let searchmode =
                                    if inp.query.Length < 3 then
                                        Database.FullTextSearch.Exact
                                    else
                                        Database.FullTextSearch.PerformanceComplete

                                printfn "[getTermSuggestionsByParentTerm] searchmode: %A" searchmode

                                if inp.parent_term.TermAccession = "" then
                                    printfn "[getTermSuggestionsByParentTerm] 1"

                                    Term
                                        .Term(credentials)
                                        .getByNameAndParent_Name (inp.query, inp.parent_term.Name, searchmode)
                                else
                                    printfn "[getTermSuggestionsByParentTerm] 2"
                                    Term.Term(credentials).getByNameAndParent (inp.query, inp.parent_term, searchmode)
                            |> Array.ofSeq
                        //|> sorensenDiceSortTerms inp.query
                        let arr =
                            if dbSearchRes.Length <= inp.n then
                                dbSearchRes
                            else
                                Array.take inp.n dbSearchRes

                        let arrSorted = sorensenDiceSortTerms inp.query arr
                        return arrSorted
                    }

            getAllTermsByParentTerm =
                fun (parentTerm: ArcitisaObsolete.TermMinimal) -> async {
                    let searchRes =
                        Database.Term.Term(credentials).getAllByParent (parentTerm, limit = 500)
                        |> Array.ofSeq

                    return searchRes
                }

            getTermSuggestionsByChildTerm =
                fun inp -> async {

                    let dbSearchRes =
                        match inp.query with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Term.Term(credentials).getByAccession foundAccession.Value
                        | _ ->
                            let searchmode =
                                if inp.query.Length < 3 then
                                    Database.FullTextSearch.Exact
                                else
                                    Database.FullTextSearch.PerformanceComplete

                            if inp.child_term.TermAccession = "" then
                                Term
                                    .Term(credentials)
                                    .getByNameAndChild_Name (inp.query, inp.child_term.Name, searchmode)
                            else
                                Term
                                    .Term(credentials)
                                    .getByNameAndChild (inp.query, inp.child_term.TermAccession, searchmode)
                        |> Array.ofSeq
                    //|> sorensenDiceSortTerms inp.query
                    let arr =
                        if dbSearchRes.Length <= inp.n then
                            dbSearchRes
                        else
                            Array.take inp.n dbSearchRes

                    let arrSorted = sorensenDiceSortTerms inp.query arr
                    return arrSorted
                }

            getAllTermsByChildTerm =
                fun (childTerm: ArcitisaObsolete.TermMinimal) -> async {
                    let searchRes = Term.Term(credentials).getAllByChild (childTerm) |> Array.ofSeq
                    return searchRes
                }

            getTermsForAdvancedSearch =
                fun advancedSearchOption -> async {
                    let result =
                        Term.Term(credentials).getByAdvancedTermSearch (advancedSearchOption)
                        |> Array.ofSeq

                    let filteredResult =
                        if advancedSearchOption.KeepObsolete then
                            result
                        else
                            result |> Array.filter (fun x -> x.IsObsolete |> not)

                    return filteredResult
                }

            getUnitTermSuggestions =
                fun inp (*(max:int,typedSoFar:string, unit:UnitSearchRequest)*) -> async {
                    let dbSearchRes =
                        match inp.query with
                        | Regex Regex.Pattern.TermAnnotationShortPattern foundAccession ->
                            Term.Term(credentials).getByAccession foundAccession.Value
                        | notAnAccession ->
                            Term
                                .Term(credentials)
                                .searchByName (
                                    notAnAccession,
                                    sourceOntologyName = Term.AnyOfOntology.Multiples [ "uo"; "dpbo" ]
                                )
                        |> Array.ofSeq
                    //|> sorensenDiceSortTerms typedSoFar
                    let res =
                        if dbSearchRes.Length <= inp.n then
                            dbSearchRes
                        else
                            Array.take inp.n dbSearchRes

                    return (res)
                }

            getTermsByNames =
                fun (queryArr) -> async {
                    // check if search string is empty. This case should delete TAN- and TSR- values in table
                    let filteredQueries =
                        queryArr
                        |> Array.filter (fun x -> x.Term.Name <> "" || x.Term.TermAccession <> "")

                    let queries =
                        filteredQueries
                        |> Array.map (fun searchTerm ->
                            // check if term accession was found. If so search also by this as it is unique
                            if searchTerm.Term.TermAccession <> "" then
                                Term.TermQuery.getByAccession searchTerm.Term.TermAccession
                            // if term is a unit it should be contained inside the unit ontology, if not it is most likely free text input.
                            elif searchTerm.IsUnit then
                                Term.TermQuery.getByName (
                                    searchTerm.Term.Name,
                                    searchType = FullTextSearch.Exact,
                                    sourceOntologyName = Term.AnyOfOntology.Multiples [ "uo"; "dpbo" ]
                                )
                            // if none of the above apply we do a standard term search
                            else
                                Term.TermQuery.getByName (searchTerm.Term.Name, searchType = FullTextSearch.Exact)
                        )

                    let result =
                        Helper.Neo4j.runQueries (queries, credentials)
                        |> Array.map2
                            (fun (termSearchable: ArcitisaObsolete.TermSearchable) dbResults ->
                                // replicate if..elif..else conditions from 'queries'
                                if termSearchable.Term.TermAccession <> "" then
                                    let result =
                                        if Array.isEmpty dbResults then
                                            None
                                        else
                                            // search by accession must be unique, and has unique restriction in database, so there can only be 0 or 1 result
                                            let r = dbResults |> Array.exactlyOne

                                            if
                                                termSearchable.Term.Name <> "" && r.Name <> termSearchable.Term.Name
                                            then
                                                failwith
                                                    $"""Found mismatch between Term Accession and Term Name. Term name "{termSearchable.Term.Name}" and term accession "{termSearchable.Term.TermAccession}",
                                            but accession belongs to name "{r.Name}" (ontology: {r.FK_Ontology})"""

                                            Some r

                                    {
                                        termSearchable with
                                            SearchResultTerm = result
                                    }
                                // search is done by name and only in the unit ontology. Therefore unit term must be unique.
                                // This might need future work, as we might want to support types of unit outside of the unit ontology
                                elif termSearchable.IsUnit then
                                    {
                                        termSearchable with
                                            SearchResultTerm =
                                                if dbResults |> Array.isEmpty then
                                                    None
                                                else
                                                    Some dbResults.[0]
                                    }
                                else
                                    {
                                        termSearchable with
                                            SearchResultTerm =
                                                if dbResults |> Array.isEmpty then
                                                    None
                                                else
                                                    Some dbResults.[0]
                                    }
                            )
                            filteredQueries

                    return result
                }

            // Tree related requests
            getTreeByAccession =
                fun accession -> async {
                    let tree = Database.TreeSearch.Tree(credentials).getByAccession (accession)
                    return tree
                }
        }

    open Helper

    let createIOntologyApi credentials =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue (ontologyApi credentials)
        |> Remoting.withDiagnosticsLogger (printfn "%A")
        |> Remoting.withErrorHandler errorHandler
        |> Remoting.OpenAPI.withDocs Fable.Remoting.OpenAPI.OpenApi.options
