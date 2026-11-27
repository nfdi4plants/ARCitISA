module Routing.Tests

open Fable.Core.JsInterop
open Fable.Mocha
open Routing

/// The parts of a browser location that the route parser reads.
let private locationOf (search: string) : Browser.Types.Location =
    createObj [ "pathname" ==> "/"; "search" ==> search; "hash" ==> "" ] |> unbox

let private hostOf (search: string) =
    match Routing.parsePath (locationOf search) with
    | Some(Route.Home host) -> host
    | None -> failwith $"The route parser rejected the URL with '{search}'."

let Main =
    testList "Routing" [
        testCase "reads the host from is_arcitisahost"
        <| fun _ -> Expect.equal (hostOf "?is_arcitisahost=2") (Some 2) "The host of the 4.x add-in manifest."

        testCase "reads the host from is_swatehost, which add-ins from an older manifest send"
        <| fun _ -> Expect.equal (hostOf "?is_swatehost=2") (Some 2) "The host of the 3.x add-in manifest."

        testCase "prefers is_arcitisahost when both are given"
        <| fun _ -> Expect.equal (hostOf "?is_swatehost=1&is_arcitisahost=2") (Some 2) "The new name wins."

        testCase "has no host without either parameter"
        <| fun _ -> Expect.equal (hostOf "") None "A plain browser."
    ]
