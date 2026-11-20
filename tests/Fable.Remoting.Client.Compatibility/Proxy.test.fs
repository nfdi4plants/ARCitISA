/// Fable 5.1+ reflects option and list as unions. These tests call a proxy end to end and pin
/// the request body a Fable.Remoting server expects, and the decoding of its responses.
/// Option and list are covered by separate tests, so a failure points at the type that broke.
module Fable.Remoting.Client.Tests.Proxy

open Fable.Remoting.Client
open Fable.Remoting.Client.Tests.FakeXmlHttpRequest
open Vitest

type Query = {
    Text: string
    Limit: int option
    Tags: string list option
}

type Item = { Id: string; Parent: string option }

type IItemApi = {
    search: Query -> Async<Item[]>
    tryGet: string -> Async<Item option>
    tags: string -> Async<string list>
}

let private api () =
    Remoting.createApi () |> Remoting.buildProxy<IItemApi>

Vitest.afterEach (fun () -> Vitest.vi.unstubAllGlobals ())

Vitest.describe (
    "request body",
    fun () ->
        Vitest.test (
            "sends Some as the value and None as null",
            fun () -> async {
                respondWith "[]"

                let! _ =
                    api().search {
                        Text = "leco"
                        Limit = Some 10
                        Tags = None
                    }

                Vitest.expect(singleRequest().Method).toBe "POST"

                Vitest.expect(sentBody ()).toEqual [|
                    {|
                        Text = "leco"
                        Limit = 10
                        Tags = null
                    |}
                |]
            }
        )

        Vitest.test (
            "sends lists as JSON arrays",
            fun () -> async {
                respondWith "[]"

                let! _ =
                    api().search {
                        Text = "leco"
                        Limit = None
                        Tags = Some [ "MS"; "PO" ]
                    }

                Vitest.expect(sentBody ()).toEqual [|
                    {|
                        Text = "leco"
                        Limit = null
                        Tags = [| "MS"; "PO" |]
                    |}
                |]
            }
        )
)

Vitest.describe (
    "response",
    fun () ->
        Vitest.test (
            "decodes optional fields",
            fun () -> async {
                respondWith """[{"Id":"MS:1","Parent":null},{"Id":"MS:2","Parent":"MS:1"}]"""

                let! items =
                    api().search {
                        Text = "leco"
                        Limit = None
                        Tags = None
                    }

                Vitest.expect(items).toEqual [|
                    { Id = "MS:1"; Parent = None }
                    { Id = "MS:2"; Parent = Some "MS:1" }
                |]
            }
        )

        Vitest.test (
            "decodes an optional return value",
            fun () -> async {
                respondWith """{"Id":"MS:1","Parent":null}"""
                let! found = api().tryGet "MS:1"
                respondWith "null"
                let! missing = api().tryGet "MS:404"
                Vitest.expect(found).toEqual (Some { Id = "MS:1"; Parent = None })
                Vitest.expect(missing).toEqual None
            }
        )

        Vitest.test (
            "decodes a list return value",
            fun () -> async {
                respondWith """["a","b"]"""
                let! tags = api().tags "MS:1"
                respondWith "[]"
                let! none = api().tags "MS:404"
                Vitest.expect(tags).toEqual [ "a"; "b" ]
                Vitest.expect(none).toEqual []
            }
        )
)
