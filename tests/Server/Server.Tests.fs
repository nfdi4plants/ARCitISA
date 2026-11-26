module Server.Tests

open Expecto

open Arcitisa.Components.Shared
open Server

let server =
    testList "Server" [
        testCase "Message returned correctly"
        <| fun _ ->
            let expectedResult = "Hello from SAFE!"
            let result = Server.getMessage ()
            Expect.equal result expectedResult "Result should be ok"
    ]

let all = testList "All" [ Tests.shared; server ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all
