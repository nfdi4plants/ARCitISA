module internal Swate.Components.Tests.JsonImport

open ARCtrl
open Swate.Components.Shared
open Vitest

/// The file types that can hold annotation tables.
let private tableFileTypes: (string * (unit -> ArcFiles)) list = [
    "assay", (fun () -> ArcFiles.Assay(ArcAssay.init "Assay"))
    "study", (fun () -> ArcFiles.Study(ArcStudy.init "Study", []))
    "run", (fun () -> ArcFiles.Run(ArcRun.init "Run"))
]

let private withTables (createFile: unit -> ArcFiles) (names: string list) =
    let file = createFile ()

    for name in names do
        let table = ArcTable.init name
        table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText $"{name} source" ])
        file.ArcTables().AddTable table

    file

let private tableNames (arcFile: ArcFiles) =
    arcFile.Tables() |> Seq.map _.Name |> Array.ofSeq

for fileType, createFile in tableFileTypes do
    Vitest.describe (
        $"JSON import ({fileType})",
        fun () ->
            Vitest.test (
                "imported tables whose name is taken are renamed, ignoring case, and keep their content",
                fun () ->
                    let current = withTables createFile [ "Sugar" ]

                    let imported = withTables createFile [ "Sugar"; "sugar"; "Extraction (2)" ]

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
                    let current = withTables createFile [ "Extraction (2)" ]
                    let imported = withTables createFile [ "B"; "Extraction (2)" ]

                    match Json.Import.applyToCurrentArcFile (current, imported) with
                    | Ok result ->
                        Vitest.expect(tableNames result).toEqual [| "Extraction (2)"; "B"; "Extraction (2) 1" |]
                    | Error error -> failwith error.Message
            )

            Vitest.test (
                "an import with a table name that cannot be saved fails and leaves the open file unchanged",
                fun () ->
                    let current = withTables createFile [ "Sugar" ]
                    let imported = withTables createFile [ "B"; "Sugar/day 2" ]

                    match Json.Import.applyToCurrentArcFile (current, imported) with
                    | Ok result -> failwith $"Expected an error, got the tables %A{tableNames result}"
                    | Error error ->
                        Vitest.expect(error.Message).toBe
                            "Table \"Sugar/day 2\": A table name cannot contain * ? : \\ / [ ]."

                    Vitest.expect(tableNames current).toEqual [| "Sugar" |]
            )
    )
