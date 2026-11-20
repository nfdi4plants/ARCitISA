/// Fable 5.1+ reflects option and list as unions. These tests pin the JSON wire format
/// that Fable.Remoting servers expect for both, and that ordinary unions keep theirs.
/// Each fixture covers one of these, so a failure points at the type that broke.
module Fable.SimpleJson.Tests.Converter

open System
open Fable.Core
open Fable.SimpleJson
open Vitest

type Annotation = { Name: string; Parent: string option }

type Term = {
    Accession: string
    Synonyms: string list
}

type SavedSearch =
    | Search of query: string * limit: int option
    | Recent of string list
    | Empty

/// Parses JSON into a plain object, so the wire format is compared without key order.
let private wire (json: string) : obj = JS.JSON.parse json

Vitest.describe (
    "option",
    fun () ->
        Vitest.test (
            "serializes Some as the value and None as null",
            fun () ->
                let present = { Name = "leaf"; Parent = Some "plant" }
                let absent = { Name = "leaf"; Parent = None }
                Vitest.expect(present |> Json.serialize |> wire).toEqual {| Name = "leaf"; Parent = "plant" |}
                Vitest.expect(absent |> Json.serialize |> wire).toEqual {| Name = "leaf"; Parent = null |}
        )

        Vitest.test (
            "parses values as Some and null as None",
            fun () ->
                let present = Json.parseAs<Annotation> """{"Name":"leaf","Parent":"plant"}"""
                let absent = Json.parseAs<Annotation> """{"Name":"leaf","Parent":null}"""
                Vitest.expect(present.Parent).toEqual (Some "plant")
                Vitest.expect(absent.Parent).toEqual None
        )
)

Vitest.describe (
    "list",
    fun () ->
        Vitest.test (
            "serializes as a JSON array",
            fun () ->
                let term = {
                    Accession = "MS:1"
                    Synonyms = [ "a"; "b" ]
                }

                Vitest.expect(term |> Json.serialize |> wire).toEqual {|
                    Accession = "MS:1"
                    Synonyms = [| "a"; "b" |]
                |}
        )

        Vitest.test (
            "parses from a JSON array",
            fun () ->
                let term = Json.parseAs<Term> """{"Accession":"MS:1","Synonyms":["a","b"]}"""
                Vitest.expect(term.Synonyms).toEqual [ "a"; "b" ]
        )

        Vitest.test (
            "round-trips a top-level list",
            fun () ->
                let ids = [ Guid.NewGuid(); Guid.NewGuid() ]
                Vitest.expect(ids |> Json.serialize |> Json.parseAs<Guid list>).toEqual ids
                Vitest.expect(([]: Guid list) |> Json.serialize |> Json.parseAs<Guid list>).toEqual []
        )

        Vitest.test (
            "round-trips an optional list",
            fun () ->
                for original in [ Some [ "MS"; "PO" ]; Some []; None ] do
                    Vitest.expect(original |> Json.serialize |> Json.parseAs<string list option>).toEqual original
        )

        Vitest.test (
            "round-trips inside a union case",
            fun () ->
                let original = Recent [ "a"; "b" ]
                Vitest.expect(original |> Json.serialize |> wire).toEqual {| Recent = [| "a"; "b" |] |}
                Vitest.expect(original |> Json.serialize |> Json.parseAs<SavedSearch>).toEqual original
        )
)

Vitest.describe (
    "union",
    fun () ->
        Vitest.test (
            "keeps the case-name wire format",
            fun () ->
                Vitest.expect(Search("leco", Some 10) |> Json.serialize |> wire).toEqual {|
                    Search = [| box "leco"; box 10 |]
                |}

                Vitest.expect(Search("leco", None) |> Json.serialize |> wire).toEqual {|
                    Search = [| box "leco"; null |]
                |}

                Vitest.expect(Empty |> Json.serialize |> wire).toEqual "Empty"
        )

        Vitest.test (
            "round-trips cases containing options",
            fun () ->
                for original in
                    [
                        Search("leco", Some 10)
                        Search("temperature", None)
                        Empty
                    ] do
                    Vitest.expect(original |> Json.serialize |> Json.parseAs<SavedSearch>).toEqual original
        )
)
