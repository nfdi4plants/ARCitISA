module Swate.Components.Shared.Tests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

let example_tests =
    testList "example" [ testCase "One" <| fun _ -> Expect.equal 1 1 "" ]

let shared =
    testList "Shared" [
        example_tests
        LandingTests.tests
        PathHelpersTests.tests
        DatamapParentInfoTests.tests
        TableNamesTests.tests
        GitLfsRulesTests.tests
        ProvenanceGroupingTests.tests
        ProvenanceGroupingARCtrlConverterTests.tests
        ProvenanceGraphBuildIndexTests.tests
        ProvenanceGraphQueriesTests.tests
    ]
