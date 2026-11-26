module Arcitisa.Components.Composite.TermSearch.Types

open Arcitisa.Components
open Arcitisa.Components.Shared.Extensions
open Fable.Core
open Feliz

[<JS.PojoAttribute>]
type Term
    (?name: string, ?id: string, ?description: string, ?source: string, ?href: string, ?isObsolete: bool, ?data: obj) =
    member val name: string option = jsNative with get, set
    member val id: string option = jsNative with get, set
    member val description: string option = jsNative with get, set
    member val source: string option = jsNative with get, set
    member val href: string option = jsNative with get, set
    member val isObsolete: bool option = jsNative with get, set
    member val data: obj option = jsNative with get, set

module Term =

    [<Emit("Object.assign({}, $0, $1)")>]
    let objectMerge (obj1: obj) (obj2: obj) = jsNative

    let joinLeft (t1: Term) (t2: Term) =
        let data =
            match t1.data, t2.data with
            | Some d1, None -> Some d1
            | None, Some d2 -> Some d2
            | None, None -> None
            | Some d1, Some d2 -> objectMerge d1 d2 |> Some

        Term(
            ?name = Option.orElse t2.name t1.name,
            ?id = Option.orElse t2.id t1.id,
            ?description = Option.orElse t2.description t1.description,
            ?source = Option.orElse t2.source t1.source,
            ?href = Option.orElse t2.href t1.href,
            ?isObsolete = Option.orElse t2.isObsolete t1.isObsolete,
            ?data = data
        )

    let joinRight (t1: Term) (t2: Term) =
        let data =
            match t1.data, t2.data with
            | Some d1, None -> Some d1
            | None, Some d2 -> Some d2
            | None, None -> None
            | Some d1, Some d2 -> objectMerge d1 d2 |> Some

        Term(
            ?name = Option.orElse t1.name t2.name,
            ?id = Option.orElse t1.id t2.id,
            ?description = Option.orElse t1.description t2.description,
            ?source = Option.orElse t1.source t2.source,
            ?href = Option.orElse t1.href t2.href,
            ?isObsolete = Option.orElse t1.isObsolete t2.isObsolete,
            ?data = data
        )

    module ConvertLiterals =
        [<Literal>]
        let Description = "description"

        [<Literal>]
        let Data = "data"

        [<Literal>]
        let IsObsolete = "isObsolete"

    open Arcitisa.Components.Shared
    open Fable.SimpleJson

    open ARCtrl

    let toOntologyAnnotation (term: Term) =
        let comments =
            ResizeArray [
                if term.description.IsSome then
                    Comment(ConvertLiterals.Description, JS.JSON.stringify term.description.Value)
                if term.data.IsSome then
                    Comment(ConvertLiterals.Data, JS.JSON.stringify term.data.Value)
                if term.isObsolete.IsSome then
                    Comment(ConvertLiterals.IsObsolete, JS.JSON.stringify term.isObsolete.Value)
            ]
            |> Option.whereNot Seq.isEmpty

        ARCtrl.OntologyAnnotation(?name = term.name, ?tsr = term.source, ?tan = term.id, ?comments = comments)

    let fromOntologyAnnotation (oa: ARCtrl.OntologyAnnotation) =

        let description =
            oa.Comments
            |> Seq.tryFind (fun c -> c.Name = Some ConvertLiterals.Description)
            |> Option.map (fun c -> c.Value.Value)

        let data =
            oa.Comments
            |> Seq.tryFind (fun c -> c.Name = Some ConvertLiterals.Data)
            |> Option.map (fun c -> Fable.Core.JS.JSON.parse c.Value.Value)

        let isObsolete =
            oa.Comments
            |> Seq.tryFind (fun c -> c.Name = Some ConvertLiterals.IsObsolete)
            |> Option.bind (fun c -> c.Value |> Option.map System.Boolean.Parse)

        Term(
            ?name = oa.Name,
            ?id = oa.TermAccessionNumber,
            ?description = description,
            ?source = oa.TermSourceREF,
            ?href = Option.whereNot System.String.IsNullOrWhiteSpace oa.TermAccessionOntobeeUrl,
            ?isObsolete = isObsolete,
            ?data = data
        )

[<JS.Pojo>]
type TermSearchStyle(?inputLabel: U2<string, string[]>) =
    member val inputLabel: U2<string, string[]> option = inputLabel with get, set

[<JS.Pojo>]
type AdvancedSearchOptions
    (setResults: (ResizeArray<Term> -> unit), formRef: IRefValue<unit -> JS.Promise<ResizeArray<Term>>>) =
    member val setResults = setResults with get, set
    member val formRef = formRef with get, set


///
/// A search function that resolves a list of terms.
/// @typedef {function(string): Promise<Term[]>} SearchCall
///
type SearchCall = string -> JS.Promise<ResizeArray<Term>>

//
// A parent search function that resolves a list of terms based on a parent ID and query.
// @typedef {function(string, string): Promise<Term[]>} ParentSearchCall
//
type ParentSearchCall = (string * string) -> JS.Promise<ResizeArray<Term>>

///
/// A function that fetches all child terms of a parent.
/// @typedef {function(string): Promise<Term[]>} AllChildrenSearchCall
///
type AllChildrenSearchCall = string -> JS.Promise<ResizeArray<Term>>

[<AutoOpen>]
module ARCtrlExtension =

    open ARCtrl

    type OntologyAnnotation with
        static member from(term: Term) =
            let comments =
                ResizeArray [
                    if term.description.IsSome then
                        Comment(OntologyAnnotationHelper.DescriptionCommentKey, term.description.Value)
                    if term.isObsolete.IsSome then
                        Comment(OntologyAnnotationHelper.IsObsoleteCommentKey, term.isObsolete.Value.ToString())
                ]

            OntologyAnnotation(?name = term.name, ?tsr = term.source, ?tan = term.id, comments = comments)

        member this.ToTerm() =
            let href =
                this.TermAccessionOntobeeUrl |> Option.whereNot System.String.IsNullOrWhiteSpace

            let description =
                this.Comments
                |> Seq.tryFind (fun c -> c.Name = Some OntologyAnnotationHelper.DescriptionCommentKey)
                |> Option.bind (fun c -> c.Value)

            let isObsolete =
                this.Comments
                |> Seq.tryFind (fun c -> c.Name = Some OntologyAnnotationHelper.IsObsoleteCommentKey)
                |> Option.bind (fun c -> c.Value)
                |> Option.map System.Boolean.Parse

            Term(
                ?name = this.Name,
                ?source = this.TermSourceREF,
                ?id = this.TermAccessionNumber,
                ?href = href,
                ?description = description,
                ?isObsolete = isObsolete
            )

[<AutoOpen>]
module TIBTypesExtensions =

    type Api.TIBApi.TIBTypes.SearchApi with
        /// This function is used to transform TIB term type into the ARCitISA compatible Term type.
        member this.ToMyTerm() =
            this.response.docs
            |> Array.map (fun t ->
                Term(
                    t.label,
                    t.obo_id,
                    t.description |> String.concat ";",
                    t.ontology_name,
                    t.iri,
                    t.is_obsolete |> Option.defaultValue false
                )
            )
