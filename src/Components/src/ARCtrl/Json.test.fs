module internal Swate.Components.Tests.JsonImport

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Tests.TableFiles
open Vitest

/// A file with one table per name, each holding a cell that names its table.
let private withNamedTables (createFile: unit -> ArcFiles) (names: string list) =
    withTables createFile [
        for name in names do
            let table = ArcTable.init name

            table.AddColumn(
                CompositeHeader.Input IOType.Source,
                ResizeArray [ CompositeCell.FreeText $"{name} source" ]
            )

            table
    ]

let private tableNames (arcFile: ArcFiles) =
    arcFile.Tables() |> Seq.map _.Name |> Array.ofSeq

for fileType, createFile in tableFileTypes do
    Vitest.describe (
        $"JSON import ({fileType})",
        fun () ->
            Vitest.test (
                "imported tables whose name is taken are renamed, ignoring case, and keep their content",
                fun () ->
                    let current = withNamedTables createFile [ "Sugar" ]

                    let imported = withNamedTables createFile [ "Sugar"; "sugar"; "Extraction (2)" ]

                    match Json.Import.applyToCurrentArcFile (current, imported) with
                    | Ok result ->
                        Vitest.expect(tableNames result).toEqual [| "Sugar"; "Sugar 1"; "sugar 2"; "Extraction (2)" |]

                        Vitest
                            .expect(result.Tables().[2].GetCellAt(0, 0))
                            .toEqual (CompositeCell.FreeText "sugar source")
                    | Error error -> failwith error.Message

                    Vitest.expect(tableNames current).toEqual [| "Sugar" |]
            )

            Vitest.test (
                "a taken name with characters ARCtrl identifiers do not allow is renamed",
                fun () ->
                    let current = withNamedTables createFile [ "Extraction (2)" ]
                    let imported = withNamedTables createFile [ "B"; "Extraction (2)" ]

                    match Json.Import.applyToCurrentArcFile (current, imported) with
                    | Ok result ->
                        Vitest.expect(tableNames result).toEqual [| "Extraction (2)"; "B"; "Extraction (2) 1" |]
                    | Error error -> failwith error.Message
            )

            Vitest.test (
                "imported table names that cannot be saved are changed so they can, and keep their content",
                fun () ->
                    let current = withNamedTables createFile [ "Sugar" ]

                    let imported =
                        withNamedTables createFile [ "Sugar/day 2"; "History"; "'B'"; String.replicate 40 "a" ]

                    match Json.Import.applyToCurrentArcFile (current, imported) with
                    | Ok result ->
                        Vitest.expect(tableNames result).toEqual [|
                            "Sugar"
                            "Sugar_day 2"
                            "History 1"
                            "B"
                            String.replicate 31 "a"
                        |]

                        Vitest
                            .expect(result.Tables().[1].GetCellAt(0, 0))
                            .toEqual (CompositeCell.FreeText "Sugar/day 2 source")
                    | Error error -> failwith error.Message

                    Vitest.expect(tableNames current).toEqual [| "Sugar" |]
            )
    )
