module Swate.Components.Shared.TableNamesTests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open ARCtrl
open Swate.Components.Shared

let private names (newTableNames: NewTableName[]) = newTableNames |> Array.map _.Name

let tests =
    testList "Table names" [
        testCase "a new table gets its preferred name, made unique ignoring case against existing and earlier tables"
        <| fun _ ->
            let result =
                resolveNewTableNames [ "SugarExtraction"; "New Table 0" ] [|
                    "SugarExtraction", None
                    "sugarextraction", None
                |]

            Expect.equal (names result) [| "SugarExtraction 1"; "sugarextraction 2" |] "names"
            Expect.isTrue (result |> Array.forall _.Error.IsNone) "no errors"

        testCase "a suggested name avoids names entered for other tables"
        <| fun _ ->
            let result =
                resolveNewTableNames [] [| "Extraction", None; "Other", Some "Extraction" |]

            Expect.equal (names result) [| "Extraction 1"; "Extraction" |] "names"

        testCase "a notice explains every suggested name that differs from the preferred name"
        <| fun _ ->
            let result =
                resolveNewTableNames [ "taken" ] [|
                    "Free", None
                    "Sugar", None
                    "Sugar", None
                    "Sugar", Some "Own name"
                    "Taken", None
                |]

            Expect.equal
                (result |> Array.map _.Notice)
                [|
                    None
                    None
                    Some "Another imported table is named \"Sugar\", so this one is renamed."
                    None
                    Some "\"Taken\" already exists, so the new table is renamed."
                |]
                "notices"

            Expect.equal result.[4].Name "Taken 1" "renamed"

        testCase "names that cannot be saved as a spreadsheet sheet or are taken ignoring case get an error"
        <| fun _ ->
            [
                "  ", Some "Enter a table name."
                "Sugar/day 2", Some "A table name cannot contain * ? : \\ / [ ]."
                "'Sugar", Some "A table name cannot start or end with '."
                "History", Some "\"History\" is reserved and cannot be a table name."
                String.replicate 32 "a", Some "A table name can have at most 31 characters."
                " sugarextraction ", Some "A table named \"sugarextraction\" already exists."
                "Extraction (2)", None
                "Su'gar", None
                "history", Some "\"History\" is reserved and cannot be a table name."
                String.replicate 31 "b", None
            ]
            |> List.iter (fun (name, expected) ->
                Expect.equal (tryTableNameError [ "SugarExtraction" ] name) expected name
            )

        testCase "names entered twice ignoring case get an error"
        <| fun _ ->
            let result = resolveNewTableNames [] [| "A", Some "Same"; "B", Some "same" |]

            Expect.equal
                (result |> Array.map _.Error)
                [|
                    Some "Another imported table is also named \"Same\"."
                    Some "Another imported table is also named \"same\"."
                |]
                "errors"

        testCase "a numbered name is shortened to fit the 31 characters a table name can have"
        <| fun _ ->
            let longName = String.replicate 30 "a"
            let result = resolveNewTableNames [ longName ] [| longName, None |]

            Expect.equal result.[0].Name (String.replicate 29 "a" + " 1") "name"
            Expect.isNone result.[0].Error "no error"

        testCase "a suggested name that cannot be saved as a spreadsheet sheet is changed so it can, with a notice"
        <| fun _ ->
            let result =
                resolveNewTableNames [] [|
                    "Sugar extraction protocol, day two", None
                    " 'Sugar' ", None
                    "history", None
                    "Sugar/day [2]", None
                    "''", None
                |]

            Expect.equal
                (names result)
                [|
                    "Sugar extraction protocol, day"
                    "Sugar"
                    "history 1"
                    "Sugar_day _2_"
                    "New Table"
                |]
                "names"

            Expect.isTrue (result |> Array.forall _.Error.IsNone) "no errors"

            Expect.equal
                (result |> Array.map _.Notice)
                [|
                    Some "A table name can have at most 31 characters. The new table is renamed."
                    Some "A table name cannot start or end with '. The new table is renamed."
                    Some "\"History\" is reserved and cannot be a table name. The new table is renamed."
                    Some "A table name cannot contain * ? : \\ / [ ]. The new table is renamed."
                    Some "A table name cannot start or end with '. The new table is renamed."
                |]
                "notices"

        testCase "a shortened suggested name is numbered when its shortened form is taken"
        <| fun _ ->
            let result =
                resolveNewTableNames [ "Sugar extraction protocol, day" ] [|
                    "Sugar extraction protocol, day two", None
                |]

            Expect.equal (names result) [| "Sugar extraction protocol, da 1" |] "names"
            Expect.isNone result.[0].Error "no error"

        testCase "the first table of a new file gets a name that can be saved, however long the identifier is"
        <| fun _ ->
            let tableName identifier =
                (ARCtrlHelper.ArcFileDefaults.createDefaultArcFile ArcFilesDiscriminate.Assay identifier)
                    .Tables()
                    .[0].Name

            Expect.equal (tableName "Sugar") "Sugar Table" "short identifier"

            Expect.equal
                (tableName "Sugar_extraction_protocol_day_2")
                "Sugar_extraction_protocol_day_2"
                "long identifier"

        testCase "a new default table name skips names taken ignoring case"
        <| fun _ -> Expect.equal (createNewTableName [ ArcTable.init "new table 0" ]) "New Table 1" "name"
    ]
