/// Wire format of the Swate API request DTOs. The generic option and list tests live with the
/// SimpleJson compatibility tests, see src/Fable.SimpleJson.Compatibility/README.md.
module internal Swate.Components.Tests.JsonWireFormat

open Fable.SimpleJson
open Swate.Components.Shared.DTOs
open Vitest

let private wire (query: TermQuery) : obj =
    query |> Json.serialize |> Fable.Core.JS.JSON.parse

Vitest.test (
    "serializes TermQuery optional fields as values and null",
    fun () ->
        Vitest.expect(TermQuery.create ("leco", 10) |> wire).toEqual {|
            query = "leco"
            limit = 10
            parentTermId = null
            ontologies = null
            searchMode = null
        |}
)

Vitest.test (
    "serializes TermQuery ontologies as a JSON array",
    fun () ->
        Vitest.expect(TermQuery.create ("leco", 10, ontologies = [ "MS"; "PO" ]) |> wire).toEqual {|
            query = "leco"
            limit = 10
            parentTermId = null
            ontologies = [| "MS"; "PO" |]
            searchMode = null
        |}
)
