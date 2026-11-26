namespace Arcitisa.Components.Shared

open System
open Database

[<AutoOpen>]
module Regex =

    open System.Text.RegularExpressions

    /// (|Regex|_|) pattern input
    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some(m) else None

module Route =

    let builder typeName methodName =
        let prefix =
            // Why the #if...#else...#endif?
            // -> We want to keep fable-remoting's flexible for development and production.
            //      When we publish the components, we want to use the production URL.
            //      Both for npm and nuget packages.
#if ARCITISA_ENVIRONMENT || !FABLE_COMPILER
            ""
#else
            URLs.PRODUCTION_URL
#endif

        sprintf "%s/api/%s/%s" prefix typeName methodName

module SorensenDice =

    let inline calculateDistance (x: Set<'T>) (y: Set<'T>) =
        match (x.Count, y.Count) with
        | (0, 0) -> 1.
        | (xCount, yCount) -> (2. * (Set.intersect x y |> Set.count |> float)) / ((xCount + yCount) |> float)

    let createBigrams (s: string) =
        s.ToUpperInvariant().ToCharArray()
        |> Array.windowed 2
        |> Array.map (fun inner -> sprintf "%c%c" inner.[0] inner.[1])
        |> set

    let sortBySimilarity (searchStr: string) (f: 'a -> string) (arrayToSort: 'a[]) =
        let searchSet = searchStr |> createBigrams

        arrayToSort
        |> Array.sortByDescending (fun result ->
            let resultSet = f result |> createBigrams
            calculateDistance resultSet searchSet
        )

open Arcitisa.Components.Shared.DTOs

type IOntologyAPIv3 = {
    // Development
    getTestNumber: unit -> Async<int>
    searchTerm: TermQuery -> Async<Term[]>
    searchTerms: TermQuery[] -> Async<TermQueryResults[]>
    getTermById: string -> Async<Term option>
    searchChildTerms: ParentTermQuery -> Async<ParentTermQueryResults>
    searchTermAdvanced: AdvancedSearchQuery -> Async<Term[]>
}

/// Development api
type ITestAPI = {
    test: unit -> Async<string * string>
    postTest: string -> Async<string * string>
}

type IServiceAPIv1 = { getAppVersion: unit -> Async<string> }


type ITemplateAPIv1 = {
    // must return template as string, fable remoting cannot do conversion automatically
    getTemplates: unit -> Async<string>
    getTemplateById: string -> Async<string>
}


/// This module is obsolete, use ARCtrl instead. I only did not add the Obsolete attribute to the module to avoid annoying warnings everywhere.
[<RequireQualifiedAccess>]
module ArcitisaObsolete =

    /// This module is obsolete, use ARCtrl instead. I only did not add the Obsolete attribute to the module to avoid annoying warnings everywhere.
    [<RequireQualifiedAccess>]
    module Regex =

        module Pattern =

            module MatchGroups =

                [<Literal>]
                let numberFormat = "numberFormat"

                [<Literal>]
                let localID = "localid"

                [<Literal>]
                let idspace = "idspace"

                [<Literal>]
                let iotype = "iotype"

            // Checks

            //Source Name
            //Sample Name
            //Characteristics [Sample type]
            //Characteristics [biological replicate]
            //Factor [Sample type#2]
            //Parameter [biological replicate#2]
            //Data File Name
            //Term Source REF (NFDI4PSO:0000064)
            //Term Source REF (NFDI4PSO:0000064#2)
            //Term Accession Number (MS:1001809)
            //Term Accession Number (MS:1001809#2)
            //Unit
            //Unit (#3)
            //"http://purl.obolibrary.org/obo/NFDI4PSO_0000064"

            [<LiteralAttribute>]
            let IdPattern = "#\d+" //  @"(?<=#)\d+(?=[\)\]])" <- Cannot be used in IE11

            [<LiteralAttribute>]
            // <summary> This pattern captures characters between squared brackets, without id: Parameter [biological replicate#2] -> biological replicate</summary>
            let SquaredBracketsTermNamePattern = "\[.*\]" //  @"(?<= \[)[^#\]]*(?=[\]#])" <- Cannot be used in IE11

            [<LiteralAttribute>]
            // Used to get unit name from Excel numberFormat: 0.00 "degree Celsius"
            let DoubleQuotesPattern = "\"(.*?)\""

            [<LiteralAttribute>]
            // This pattern captures all input coming before an opening square bracket or normal bracket (with whitespace).
            let CoreNamePattern = "^[^[(]*"

            //// Hits term accession, without id, NEEDS brackets before and after: ENVO:01001831
            //[<LiteralAttribute>]
            //let TermAccessionPattern = @"(?<=\()\S+[:_][^;)#]*(?=[\)\#])" //"[a-zA-Z0-9]+?[:_][a-zA-Z0-9]+"

            /// Hits term accession, without id: ENVO:01001831
            let TermAnnotationShortPattern =
                $@"(?<{MatchGroups.idspace}>\w+?):(?<{MatchGroups.localID}>\w+)" //prev: @"[\w]+?:[\d]+"

            // https://obofoundry.org/id-policy.html#mapping-of-owl-ids-to-obo-format-ids
            /// <summary>Regex pattern is designed to hit only Foundry-compliant URIs.</summary>
            let TermAnnotationURIPattern =
                $@".*\/(?<{MatchGroups.idspace}>\w+?)[:_](?<{MatchGroups.localID}>\w+)"

        open Pattern
        open System
        open System.Text.RegularExpressions

        let parseSquaredTermNameBrackets (headerStr: string) =
            match headerStr with
            | Regex SquaredBracketsTermNamePattern value ->
                // trim whitespace AND remove brackets
                value.Value.Trim().[1 .. value.Length - 2]
                // remove #id pattern
                |> fun str -> Regex.Replace(str, IdPattern, "")
                |> Some
            | _ -> None

        let parseCoreName (headerStr: string) =
            match headerStr with
            | Regex CoreNamePattern value -> value.Value.Trim() |> Some
            | _ -> None

        //let parseTermAccession (headerStr:string) =
        //    printfn "parse.."
        //    match headerStr with
        //    | Regex TermAccessionPattern value ->
        //        value.Trim().Replace('_',':')
        //        |> Some
        //    | _ ->
        //        None

        /// <summary>This function can be used to extract `IDSPACE:LOCALID` (or: `Term Accession` from ARCitISA header strings or obofoundry conform URI strings.</summary>
        let parseTermAccession (headerStr: string) =
            match headerStr.Trim() with
            | Regex TermAnnotationShortPattern value -> value.Value.Trim() |> Some
            | Regex TermAnnotationURIPattern value ->
                let idspace = value.Groups.["idspace"].Value
                let localid = value.Groups.["localid"].Value
                idspace + ":" + localid |> Some
            | _ -> None

        let parseDoubleQuotes (headerStr: string) =
            match headerStr with
            | Regex DoubleQuotesPattern value ->
                // remove quotes at beginning and end of matched string
                value.Value.[1 .. value.Length - 2].Trim() |> Some
            | _ -> None

        let getId (headerStr: string) =
            match headerStr with
            | Regex IdPattern value -> value.Value.Trim().[1..] |> Some
            | _ -> None


    type TermMinimal = {
        Name: string
        /// This is the Ontology Term Accession 'XX:aaaaaa'
        TermAccession: string
    } with

        static member create name tan = { Name = name; TermAccession = tan }

    type TermSearchable = {
        // Contains information about the term to search itself. If term accession is known, search result is 100% correct.
        Term: TermMinimal
        // If ParentTerm isSome, then the term name is first searched in a is_a directed search
        ParentTerm: TermMinimal option
        // Is term ist used as unit, unit ontology is searched first.
        IsUnit: bool
        // ColIndex in table
        ColIndex: int
        // RowIndex in table
        RowIndices: int[]
        // Search result
        SearchResultTerm: Term option
    }

/// <summary>Deprecated</summary>
type IOntologyAPIv1 = {
    // Development
    getTestNumber: unit -> Async<int>

    // Ontology related requests
    getAllOntologies: unit -> Async<Ontology[]>

    // Term related requests
    ///
    getTermSuggestions: (int * string) -> Async<Term[]>
    /// (nOfReturnedResults*queryString*parentOntology). If parentOntology = "" then isNull -> Error.
    getTermSuggestionsByParentTerm: (int * string * ArcitisaObsolete.TermMinimal) -> Async<Term[]>
    getAllTermsByParentTerm: ArcitisaObsolete.TermMinimal -> Async<Term[]>
    /// (nOfReturnedResults*queryString*parentOntology). If parentOntology = "" then isNull -> Error.
    getTermSuggestionsByChildTerm: (int * string * ArcitisaObsolete.TermMinimal) -> Async<Term[]>
    getAllTermsByChildTerm: ArcitisaObsolete.TermMinimal -> Async<Term[]>
    getTermsForAdvancedSearch: (AdvancedSearchQuery) -> Async<Term[]>
    getUnitTermSuggestions: (int * string) -> Async<Term[]>
    getTermsByNames: ArcitisaObsolete.TermSearchable[] -> Async<ArcitisaObsolete.TermSearchable[]>

    // Tree related requests
    getTreeByAccession: string -> Async<TreeTypes.Tree>
}


/// <summary>
/// This is used for MIAPPE Wizard external tool. Before removing this contact them.
/// </summary>
type IOntologyAPIv2 = {
    // Development
    getTestNumber: unit -> Async<int>

    // Ontology related requests
    getAllOntologies: unit -> Async<Ontology[]>

    // Term related requests
    ///
    getTermSuggestions:
        {|
            n: int
            query: string
            ontology: string option
        |}
            -> Async<Term[]>
    /// (nOfReturnedResults*queryString*parentOntology). If parentOntology = "" then isNull -> Error.
    getTermSuggestionsByParentTerm:
        {|
            n: int
            query: string
            parent_term: ArcitisaObsolete.TermMinimal
        |}
            -> Async<Term[]>
    getAllTermsByParentTerm: ArcitisaObsolete.TermMinimal -> Async<Term[]>
    /// (nOfReturnedResults*queryString*parentOntology). If parentOntology = "" then isNull -> Error.
    getTermSuggestionsByChildTerm:
        {|
            n: int
            query: string
            child_term: ArcitisaObsolete.TermMinimal
        |}
            -> Async<Term[]>
    getAllTermsByChildTerm: ArcitisaObsolete.TermMinimal -> Async<Term[]>
    getTermsForAdvancedSearch: (AdvancedSearchQuery) -> Async<Term[]>
    getUnitTermSuggestions: {| n: int; query: string |} -> Async<Term[]>
    getTermsByNames: ArcitisaObsolete.TermSearchable[] -> Async<ArcitisaObsolete.TermSearchable[]>

    // Tree related requests
    getTreeByAccession: string -> Async<TreeTypes.Tree>
}
