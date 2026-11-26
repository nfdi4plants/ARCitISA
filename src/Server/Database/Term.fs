module Database.Term

open Neo4j.Driver
open Arcitisa.Components.Shared.Database
open Helper
open System.Text
open System
open System.Collections.Generic
open Arcitisa.Components.Shared.DTOs
open Arcitisa.Components.Shared

/// <summary> This type is used to allow searching through only one ontology or multiple ontologies </summary>
[<RequireQualifiedAccess>]
type AnyOfOntology =
    | Single of string
    | Multiples of string list

    member this.toParamTuple =
        match this with
        | AnyOfOntology.Single str -> "OntologyName", str |> box
        | AnyOfOntology.Multiples strList -> "OntologyNames", strList |> box

type Queries =

    static member OntologyFilter(filter: AnyOfOntology, nodeName: string) =
        let FilterSourceOntology (nodeName: string) =
            $"""    WITH {nodeName}
    MATCH ({nodeName})-[:CONTAINED_IN]->(:Ontology {{name: $OntologyName/}}) """

        let FilterSourceOntologies (nodeName: string) =
            $"""  WITH {nodeName}
    MATCH ({nodeName})-[:CONTAINED_IN]->(o:Ontology WHERE o.name in $OntologyNames) """

        match filter with
        | AnyOfOntology.Single _ -> FilterSourceOntology nodeName
        | AnyOfOntology.Multiples _ -> FilterSourceOntologies nodeName

    static member TermReturn(nodeName: string) =
        $"""RETURN {nodeName}.accession, {nodeName}.name, {nodeName}.definition, {nodeName}.is_obsolete"""

    static member Limit = """LIMIT $Limit"""

    static member NameQueryExact(nodeName: string, ?ontologyFilter: AnyOfOntology, ?limit: bool) =
        let sb = new StringBuilder()
        sb.Append $"""MATCH ({nodeName}:Term {{name: $Name}})""" |> ignore

        if ontologyFilter.IsSome then
            sb.AppendLine(Queries.OntologyFilter(ontologyFilter.Value, nodeName)) |> ignore

        sb.AppendLine(Queries.TermReturn nodeName) |> ignore

        if limit.IsSome && limit.Value then
            sb.AppendLine(Queries.Limit) |> ignore

        sb.ToString()

    static member NameQueryFullText(nodeName: string, ?ontologyFilter: AnyOfOntology, ?limit: bool) =
        let sb = new StringBuilder()

        sb.AppendLine
            $"""CALL db.index.fulltext.queryNodes("TermName",$Query)
YIELD {nodeName}"""
        |> ignore

        if ontologyFilter.IsSome then
            sb.AppendLine(Queries.OntologyFilter(ontologyFilter.Value, nodeName)) |> ignore

        sb.AppendLine(Queries.TermReturn nodeName) |> ignore

        sb.AppendLine($"""ORDER BY apoc.text.distance(toLower({nodeName}.name), toLower($Name))""")
        |> ignore

        if limit.IsSome && limit.Value then
            sb.AppendLine(Queries.Limit) |> ignore

        sb.ToString()


type Term(?credentials: Neo4JCredentials, ?session: IAsyncSession) =

    static member private parseAccessionToOntologyName(accession: string) =
        let i = accession.IndexOfAny [| ':'; '_' |]

        if i > 0 then
            accession.[.. i - 1]
        else
            failwith $"Cannot parse accession: '{accession}' to ontology name."

    /// "termParamName": the query function tries to map properties of the "termParamName" to this function so depending on how the node was called in the query this needs to adapt.
    static member asTerm(termParamName) =
        fun (record: IRecord) ->
            let accession = record.[$"{termParamName}.accession"].As<string>()

            let (term: Arcitisa.Components.Shared.Database.Term) = {
                Accession = accession
                FK_Ontology = Term.parseAccessionToOntologyName accession
                Name = record.[$"{termParamName}.name"] |> defaultOutputWith<string> ""
                Description = record.[$"{termParamName}.definition"] |> defaultOutputWith<string> ""
                IsObsolete = record.[$"{termParamName}.is_obsolete"] |> defaultOutputWith<bool> false
            }

            term

    /// Searchtype defaults to "get term suggestions with auto complete".
    member this.searchByName
        (termName: string, ?searchType: FullTextSearch, ?sourceOntologyName: AnyOfOntology, ?limit: int)
        =
        let nodeName = "node"

        let escaped, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.PerformanceComplete.ofQueryString termName

        let query =
            Queries.NameQueryFullText(nodeName, ?ontologyFilter = sourceOntologyName, limit = limit.IsSome)

        let parameters =
            Map [
                "Query", fulltextSearchStr |> box
                "Name", escaped |> box
                if sourceOntologyName.IsSome then
                    sourceOntologyName.Value.toParamTuple
                if limit.IsSome then
                    "Limit", box limit.Value
            ]
            |> Some

        Neo4j.runQuery (query, parameters, (Term.asTerm (nodeName)), ?session = session, ?credentials = credentials)

    //member this.getByName

    /// <summary>
    /// This is a more complete implementation, which should be abstracted more later.
    /// </summary>
    member this.searchByParentStepwise(query: string, parentId: string, ?searchType: FullTextSearch, ?limit: int) =
        let limit = defaultArg limit 5
        let searchNameQuery = Queries.NameQueryFullText("node", limit = true)

        let searchTreeQuery =
            """MATCH (node:Term)
    WHERE node.accession IN $AccessionList
    MATCH (endNode:Term {accession: $Parent})
    MATCH (node)
    WHERE EXISTS (
        (endNode)<-[:is_a*]-(node)
    )
    RETURN node.accession, node.name, node.definition, node.is_obsolete"""
        // These two examples can be used to check function for efficiency coming from both node directions.
        // Some searches are better optimized starting from child and checking for parent. For other queries it is the other way around,
        // it depends on the relationship complexity of the parent and/or child node.
        // parent: "DPBO:1000161"  raw_query: "growth prot"
        // parent: "OBI:0100026" raw_query: "arabidopsis"
        //     (node)-[*]->(endNode)
        let escaped, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString query
            else
                FullTextSearch.PerformanceComplete.ofQueryString query

        use session =
            if session.IsSome then
                session.Value
            else
                Neo4j.establishConnection (credentials.Value)

        let main = task {
            let config =
                Action<TransactionConfigBuilder>(fun (config: TransactionConfigBuilder) ->
                    config.WithTimeout(TimeSpan.FromSeconds(0.5)) |> ignore
                )

            let! names_query =
                session.RunAsync(
                    searchNameQuery,
                    System.Collections.Generic.Dictionary<string, obj>(
                        [
                            KeyValuePair("Name", box escaped)
                            KeyValuePair("Query", box fulltextSearchStr)
                            // KeyValuePair("Accession", box parentId);
                            KeyValuePair("Limit", box limit)
                        ]
                    ),
                    config
                )

            let! names_records = names_query.ToListAsync()

            let names_results = [|
                for record in names_records do
                    let id = record.["node.accession"].As<string>()
                    yield id
            |]

            let! tree_query =
                let parameters =
                    System.Collections.Generic.Dictionary<string, obj>(
                        [
                            KeyValuePair("Parent", box parentId)
                            KeyValuePair("AccessionList", box names_results)
                            KeyValuePair("Limit", box limit)
                        ]
                    )

                session.RunAsync(searchTreeQuery, parameters, config)

            let! tree_records = tree_query.ToListAsync()
            // The following code was an idea to implement an alternative query in case the first one time outs.
            // So we switch to the other search direction (parent -> child to child -> parent)
            // async {
            //   let! run =
            //     async {
            //       let! r = session.RunAsync(searchTreeQuery, parameters, action=config) |> Async.AwaitTask
            //       return! r.ToListAsync() |> Async.AwaitTask
            //     }
            // |> Async.Catch
            // return
            //   run
            // match run with
            // | Choice1Of2 terms ->
            //   terms
            // | Choice2Of2 exn ->
            //   session.RunAsync(searchTreeQuery_alternative,parameters, action=config).Result
            //   |> _.ToListAsync()
            //   |> _.Result
            // }
            let tree_results = [|
                for record in tree_records do
                    yield Term.asTerm ("node") record
            |]

            return tree_results
        }

        try
            main.Result
        with exn ->
            printfn "%s" exn.Message
            [||]

    /// <summary>
    /// Find all child terms of a given parent
    /// </summary>
    /// <param name="parentId"></param>
    /// <param name="limit"></param>
    member this.findAllChildTerms(parentId: string, ?limit: int) =
        let limit = defaultArg limit 5

        let query =
            """MATCH (child)-[:is_a*]->(:Term {accession: $Parent})
            RETURN child.accession, child.name, child.definition, child.is_obsolete
            LIMIT $Limit"""

        let config =
            Action<TransactionConfigBuilder>(fun (config: TransactionConfigBuilder) ->
                config.WithTimeout(TimeSpan.FromSeconds(0.5)) |> ignore
            )

        use session =
            if session.IsSome then
                session.Value
            else
                Neo4j.establishConnection (credentials.Value)

        let main = task {
            let! tree_query =
                let parameters =
                    System.Collections.Generic.Dictionary<string, obj>(
                        [
                            KeyValuePair("Parent", box parentId)
                            KeyValuePair("Limit", box limit)
                        ]
                    )

                session.RunAsync(query, parameters, config)

            let! tree_records = tree_query.ToListAsync()

            let tree_results = [|
                for record in tree_records do
                    yield Term.asTerm ("child") record
            |]

            return tree_results
        }

        try
            main.Result
        with exn ->
            printfn "%s" exn.Message
            [||]

    //Neo4j.runQuery(query, param, (Term.asTerm("child")), ?session=session, ?credentials=credentials)

    /// This function will allow for raw apache lucene input. It is possible to search either term name or description or both.
    /// The function will error if both term name and term description are None.
    member this.getByAdvancedTermSearch(advancedSearchOptions: AdvancedSearchQuery) =
        let termName =
            if advancedSearchOptions.TermName = "" then
                None
            else
                Some advancedSearchOptions.TermName

        let termDescription =
            if advancedSearchOptions.TermDefinition = "" then
                None
            else
                Some advancedSearchOptions.TermDefinition

        let indexName, queryInsert =
            match termName, termDescription with
            | None, None -> failwith "Cannot execute term search without any term name or term description."
            | Some _, None -> "TermName", termName.Value
            | Some name, Some desc -> "TermNameAndDefinition", sprintf """name: "%s", definition: "%s" """ name desc
            | None, Some _ -> "TermDefinition", termDescription.Value

        let query =
            if advancedSearchOptions.OntologyName.IsSome then
                sprintf
                    """CALL db.index.fulltext.queryNodes("%s", $Query)
                    YIELD node
                    WHERE EXISTS((:Term {accession: node.accession})-[:CONTAINED_IN]->(:Ontology {name: $OntologyName}))
                    RETURN node.accession, node.name, node.definition, node.is_obsolete"""
                    indexName
            else
                sprintf
                    """CALL db.index.fulltext.queryNodes("%s", $Query) YIELD node
                    RETURN node.accession, node.name, node.definition, node.is_obsolete"""
                    indexName

        let param =
            Map [
                if advancedSearchOptions.OntologyName.IsSome then
                    "OntologyName", box advancedSearchOptions.OntologyName.Value
                "Query", box queryInsert
            ]
            |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), credentials.Value)

    /// Exact match for unique identifier term accession.
    member this.getByName(name: string) =
        let query =
            """MATCH (term:Term {name: $Name})
            RETURN term.accession, term.name, term.definition, term.is_obsolete"""

        let param = Map [ "Name", box name ] |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("term")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("term")), credentials.Value)

    /// Exact match for unique identifier term accession.
    member this.getByAccession(termAccession: string) =
        let query =
            """MATCH (term:Term {accession: $Accession})
            RETURN term.accession, term.name, term.definition, term.is_obsolete"""

        let param = Map [ "Accession", box termAccession ] |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("term")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("term")), credentials.Value)

    member this.getAllByParent(parentAccession: string, ?limit: int) =
        let query =
            sprintf
                """MATCH (n:Term {accession: $Accession})
                CALL apoc.path.subgraphNodes(n, {minLevel: 1, relationshipFilter:'<', labelFilter: "+Term"})
                YIELD node as parent
                WHERE parent.accession is not null
                RETURN parent.accession, parent.name, parent.definition, parent.is_obsolete
                %s"""
                (if limit.IsSome then "LIMIT $Limit" else "")

        let param =
            Map [
                // need to box values, because limit.Value will error if parsed as string
                "Accession", box parentAccession
                if limit.IsSome then
                    "Limit", box limit.Value
            ]
            |> Some

        Neo4j.runQuery (query, param, (Term.asTerm ("child")), ?session = session, ?credentials = credentials)

    /// This function uses only the parent term accession
    member this.getAllByParent(parentAccession: ArcitisaObsolete.TermMinimal, ?limit: int) =
        let query =
            sprintf
                """MATCH (child)-[*1..]->(:Term {accession: $Accession})
                RETURN child.accession, child.name, child.definition, child.is_obsolete
                %s"""
                (if limit.IsSome then "LIMIT $Limit" else "")

        let param =
            Map [
                // need to box values, because limit.Value will error if parsed as string
                "Accession", box parentAccession.TermAccession
                if limit.IsSome then
                    "Limit", box limit.Value
            ]
            |> Some

        Neo4j.runQuery (query, param, (Term.asTerm ("child")), ?session = session, ?credentials = credentials)

    static member private byParentQuery_Accession =
        // 1. Search for fitting terms first (db.index.fulltext.queryNodes, TermName)
        // 2. Limit search results by 10. If there are too many results it will reduce kill searchspeed when checking for relationship per searchresult (example: "growth", parent: "NFDI4PSO:1000161")
        // 2.5 It is possible to do (:Term {accession: $Accession})<-[*1..]-(node) but this will die whenever there are too many relationships (example: "arabidopsis", parent: "OBI:0100026")
        // - (example: "neural", parent: "NCIT:C16275")
        // Therefore i follow the assumption to limit n of searchresult, as they are sorted by best fit already, so whenever the user is close to the result he will get the desired search results.
        """CALL db.index.fulltext.queryNodes("TermName", $Search)
        YIELD node
        WITH node LIMIT 50
        WHERE EXISTS ( (node)-[*1..]->(:Term {accession: $Accession})  )
        RETURN node.accession, node.name, node.definition, node.is_obsolete"""

    /// Searchtype defaults to "get child term suggestions with auto complete"
    member this.getByNameAndParent(termName: string, parentAccession: string, ?searchType: FullTextSearch) =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.Complete.ofQueryString termName

        let param =
            Map [
                "Accession", box parentAccession
                "Search", box fulltextSearchStr
            ]
            |> Some

        Neo4j.runQuery (
            Term.byParentQuery_Accession,
            param,
            (Term.asTerm ("node")),
            ?session = session,
            ?credentials = credentials
        )

    /// Searchtype defaults to "get child term suggestions with auto complete"
    member this.getByNameAndParent
        (term: ArcitisaObsolete.TermMinimal, parent: ArcitisaObsolete.TermMinimal, ?searchType: FullTextSearch)
        =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString term.Name
            else
                FullTextSearch.Complete.ofQueryString term.Name

        let param =
            Map [
                "Accession", box parent.TermAccession
                "Search", box fulltextSearchStr
            ]
            |> Some

        if session.IsSome then
            Neo4j.runQuery (Term.byParentQuery_Accession, param, (Term.asTerm ("node")), session = session.Value)
        else
            Neo4j.runQuery (Term.byParentQuery_Accession, param, (Term.asTerm ("node")), credentials.Value)

    // Searchtype defaults to "get child term suggestions with auto complete"
    member this.getByNameAndParent
        (termName: string, parentAccession: ArcitisaObsolete.TermMinimal, ?searchType: FullTextSearch)
        =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.Complete.ofQueryString termName

        let param =
            Map [
                "Accession", box parentAccession.TermAccession
                "Search", box fulltextSearchStr
            ]
            |> Some

        Neo4j.runQuery (
            Term.byParentQuery_Accession,
            param,
            (Term.asTerm ("node")),
            ?credentials = credentials,
            ?session = session
        )

    member this.getByNameAndParent_Name(termName: string, parentName: string, ?searchType: FullTextSearch) =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.Complete.ofQueryString termName

        let query =
            """CALL db.index.fulltext.queryNodes("TermName", $Search)
            YIELD node
            WITH node LIMIT 50
            WHERE EXISTS ( (:Term {name: $Name})<-[*1..]-(node) )
            RETURN node.accession, node.name, node.definition, node.is_obsolete"""

        let param = Map [ "Name", box parentName; "Search", box fulltextSearchStr ] |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), credentials.Value)

    /// This function uses only the parent term accession
    member this.getAllByChild(childAccession: ArcitisaObsolete.TermMinimal) =
        let query =
            """MATCH (n:Term {accession: $Accession})
            CALL apoc.path.subgraphNodes(n, {minLevel: 1, relationshipFilter:'>', labelFilter: "+Term"}) YIELD node as parent
            WHERE parent.accession is not null
            RETURN parent.accession, parent.name, parent.definition, parent.is_obsolete
            """

        let param = Map [ "Accession", box childAccession.TermAccession ] |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("parent")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("parent")), credentials.Value)

    /// Searchtype defaults to "get child term suggestions with auto complete"
    member this.getByNameAndChild(termName: string, childAccession: string, ?searchType: FullTextSearch) =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.Complete.ofQueryString termName

        let query =
            """CALL db.index.fulltext.queryNodes("TermName", $Search)
            YIELD node
            WITH node LIMIT 50
            WHERE EXISTS ( (:Term {accession: $Accession})-[*1..]->(node) )
            RETURN node.accession, node.name, node.definition, node.is_obsolete"""

        let param =
            Map [
                "Accession", box childAccession
                "Search", box fulltextSearchStr
            ]
            |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), credentials.Value)

    /// Searchtype defaults to "get child term suggestions with auto complete"
    member this.getByNameAndChild_Name(termName: string, childName: string, ?searchType: FullTextSearch) =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.Complete.ofQueryString termName

        let query =
            """CALL db.index.fulltext.queryNodes("TermName", $Search)
            YIELD node
            WITH node LIMIT 50
            WHERE EXISTS ( (:Term {name: $Name})-[*1..]->(node) )
            RETURN node.accession, node.name, node.definition, node.is_obsolete"""

        let param = Map [ "Name", box childName; "Search", box fulltextSearchStr ] |> Some

        if session.IsSome then
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), session = session.Value)
        else
            Neo4j.runQuery (query, param, (Term.asTerm ("node")), credentials.Value)

/// No idea what the idea behind this was
type TermQuery =

    static member getByName(termName: string, ?searchType: FullTextSearch, ?sourceOntologyName: AnyOfOntology) =
        let _, fulltextSearchStr =
            if searchType.IsSome then
                searchType.Value.ofQueryString termName
            else
                FullTextSearch.Complete.ofQueryString termName

        let query =
            //let sourceOntologyText = """ WHERE EXISTS((:Term {accession: node.accession})-[:CONTAINED_IN]->(:Ontology {name: $OntologyName})) """
            if searchType.IsSome && searchType.Value = FullTextSearch.Exact then
                Queries.NameQueryExact("node", ?ontologyFilter = sourceOntologyName)
            else
                Queries.NameQueryFullText("node", ?ontologyFilter = sourceOntologyName)

        let param =
            Map [
                "Name", box fulltextSearchStr
                if sourceOntologyName.IsSome then
                    sourceOntologyName.Value.toParamTuple
            ]
            |> Some

        query, param, Term.asTerm ("node")

    /// Exact match for unique identifier term accession.
    static member getByAccession(termAccession: string) =
        let query =
            """MATCH (term:Term {accession: $Accession})
            RETURN term.accession, term.name, term.definition, term.is_obsolete"""

        let param = Map [ "Accession", box termAccession ] |> Some
        query, param, Term.asTerm ("term")
