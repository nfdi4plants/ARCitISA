/// Stands in for the browser XMLHttpRequest, so a proxy call can be inspected without a server.
module Fable.Remoting.Client.Tests.FakeXmlHttpRequest

open Fable.Core
open Fable.Core.JsInterop
open Vitest

type SentRequest = {
    Method: string
    Url: string
    Body: string
}

let private sent = ResizeArray<SentRequest>()
let mutable private responseBody = "null"

/// Records every request and answers it with status 200 and the body given to respondWith.
[<AttachMembers>]
type FakeXmlHttpRequest() =
    let mutable method = ""
    let mutable url = ""

    member val readyState = 0 with get, set
    member val status = 0 with get, set
    member val responseText = "" with get, set
    member val withCredentials = false with get, set
    member val onreadystatechange: obj -> unit = ignore with get, set

    member _.``open``(httpMethod: string, requestUrl: string) =
        method <- httpMethod
        url <- requestUrl

    member _.setRequestHeader(_name: string, _value: string) = ()
    member _.abort() = ()

    member this.send(body: string) =
        sent.Add {
            Method = method
            Url = url
            Body = body
        }

        this.status <- 200
        this.responseText <- responseBody
        // XMLHttpRequest.DONE
        this.readyState <- 4
        this.onreadystatechange null

/// Replaces the global XMLHttpRequest for the current test. Every request is answered with the given JSON.
let respondWith (json: string) =
    sent.Clear()
    responseBody <- json
    Vitest.vi.stubGlobal ("XMLHttpRequest", jsConstructor<FakeXmlHttpRequest>)

/// The request sent since respondWith. Fails if there was not exactly one.
let singleRequest () =
    if sent.Count <> 1 then
        failwith $"Expected exactly one request, but {sent.Count} were sent"

    sent.[0]

/// Body of the request sent since respondWith, parsed into a plain object to compare it without key order.
let sentBody () : obj = JS.JSON.parse (singleRequest ()).Body
