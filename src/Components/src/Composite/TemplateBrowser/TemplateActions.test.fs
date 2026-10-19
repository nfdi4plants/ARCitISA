module internal Swate.Components.Composite.Template.TemplateActionsTests

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.Template.Types
open Swate.Components.Composite.Template.TemplateActions
open Vitest

/// A template whose table is named like the template.
let private createTemplate (tableName: string) =
    let template = Template.init tableName
    template.Table.AddColumn(CompositeHeader.Input IOType.Source)
    template

let private asNewTable (template: Template) =
    template, TemplateImportAction.ImportAsNewTable

let private namesOf (names: Map<System.Guid, NewTableName>) =
    names |> Map.map (fun _ newTableName -> newTableName.Name) |> Map.toArray

/// The templates whose name has an error, sorted.
let private withError (names: Map<System.Guid, NewTableName>) =
    names
    |> Map.filter (fun _ newTableName -> newTableName.Error.IsSome)
    |> Map.toSeq
    |> Seq.map fst
    |> Seq.sort
    |> Array.ofSeq

Vitest.test (
    "new tables get the template table name, made unique against existing tables and each other",
    fun () ->
        let first = createTemplate "SugarExtraction"
        let second = createTemplate "SugarExtraction"
        let appended = createTemplate "Other"

        let names =
            newTableNames
                [ "SugarExtraction"; "New Table 0" ]
                [|
                    asNewTable first
                    asNewTable second
                    appended, TemplateImportAction.AppendToActiveTable
                |]
                Map.empty

        Vitest
            .expect(namesOf names)
            .toEqual (
                Map.toArray (
                    Map [
                        first.Id, "SugarExtraction 1"
                        second.Id, "SugarExtraction 2"
                    ]
                )
            )

        Vitest.expect(withError names).toEqual [||]
)

Vitest.test (
    "a suggested name avoids names the user entered for other templates",
    fun () ->
        let first = createTemplate "Extraction"
        let second = createTemplate "Other"

        let names =
            newTableNames [] [| asNewTable first; asNewTable second |] (Map [ second.Id, "Extraction" ])

        Vitest.expect(namesOf names).toEqual (Map.toArray (Map [ first.Id, "Extraction 1"; second.Id, "Extraction" ]))
)

Vitest.test (
    "a notice explains every suggested name that differs from the template table name",
    fun () ->
        let free = createTemplate "Free"
        let first = createTemplate "SugarExtraction"
        let second = createTemplate "SugarExtraction"
        let entered = createTemplate "SugarExtraction"
        let taken = createTemplate "Taken"

        let names =
            newTableNames
                [ "taken" ]
                [|
                    asNewTable free
                    asNewTable first
                    asNewTable second
                    asNewTable entered
                    asNewTable taken
                |]
                (Map [ entered.Id, "Own name" ])

        let notices =
            names
            |> Map.toArray
            |> Array.choose (fun (id, name) -> name.Notice |> Option.map (fun n -> id, n))

        Vitest
            .expect(notices |> Array.sort)
            .toEqual (
                Array.sort [|
                    second.Id, "Another imported table is named \"SugarExtraction\", so this one is renamed."
                    taken.Id, "\"Taken\" already exists, so the new table is renamed."
                |]
            )

        Vitest.expect(names.[taken.Id].Name).toBe "Taken 1"
)

Vitest.test (
    "entered names that cannot be saved, are taken ignoring case or are used twice get an error",
    fun () ->
        let blank = createTemplate "A"
        let slash = createTemplate "B"
        let tooLong = createTemplate "C"
        let otherCase = createTemplate "D"
        let twiceFirst = createTemplate "E"
        let twiceSecond = createTemplate "F"
        let parentheses = createTemplate "G"
        let thirtyOne = createTemplate "H"
        let leadingQuote = createTemplate "I"
        let history = createTemplate "J"
        let innerQuote = createTemplate "K"

        let enteredNames =
            Map [
                blank.Id, "  "
                slash.Id, "Sugar/day 2"
                tooLong.Id, String.replicate 32 "a"
                otherCase.Id, " sugarextraction "
                twiceFirst.Id, "Same"
                twiceSecond.Id, "same"
                parentheses.Id, "Extraction (2)"
                thirtyOne.Id, String.replicate 31 "b"
                leadingQuote.Id, "'Sugar"
                history.Id, "History"
                innerQuote.Id, "Su'gar"
            ]

        let selected =
            [|
                blank
                slash
                tooLong
                otherCase
                twiceFirst
                twiceSecond
                parentheses
                thirtyOne
                leadingQuote
                history
                innerQuote
            |]
            |> Array.map asNewTable

        let names = newTableNames [ "SugarExtraction" ] selected enteredNames

        Vitest
            .expect(withError names)
            .toEqual (
                Array.sort [|
                    blank.Id
                    slash.Id
                    tooLong.Id
                    otherCase.Id
                    twiceFirst.Id
                    twiceSecond.Id
                    leadingQuote.Id
                    history.Id
                |]
            )

        Vitest.expect(names.[slash.Id].Error).toEqual (Some "A table name cannot contain * ? : \\ / [ ].")
        Vitest.expect(names.[tooLong.Id].Error).toEqual (Some "A table name can have at most 31 characters.")
        Vitest.expect(names.[otherCase.Id].Error).toEqual (Some "A table named \"sugarextraction\" already exists.")
        Vitest.expect(names.[twiceFirst.Id].Error).toEqual (Some "Another imported table is also named \"Same\".")
        Vitest.expect(names.[leadingQuote.Id].Error).toEqual (Some "A table name cannot start or end with '.")
        Vitest.expect(names.[history.Id].Error).toEqual (Some "\"History\" is reserved and cannot be a table name.")
        // Only this exact spelling is reserved.
        Vitest.expect((tryTableNameError [] "history").IsNone).toBe true
)

Vitest.test (
    "a numbered name is shortened to fit the 31 characters a table name can have",
    fun () ->
        let longName = String.replicate 30 "a"
        let template = createTemplate longName

        let names = newTableNames [ longName ] [| asNewTable template |] Map.empty

        Vitest.expect(names.[template.Id].Name).toBe (String.replicate 29 "a" + " 1")
        Vitest.expect(names.[template.Id].Error.IsNone).toBe true
)

/// The file types that can hold annotation tables.
let private tableFileTypes: (string * (unit -> ArcFiles)) list = [
    "assay", (fun () -> ArcFiles.Assay(ArcAssay.init "Assay"))
    "study", (fun () -> ArcFiles.Study(ArcStudy.init "Study", []))
    "run", (fun () -> ArcFiles.Run(ArcRun.init "Run"))
]

/// A file whose active (first) table has only an input column, next to a table named "Sugar".
let private createFile (createEmptyFile: unit -> ArcFiles) =
    let activeTable = ArcTable.init "New Table 0"
    activeTable.AddColumn(CompositeHeader.Input IOType.Source)
    let file = createEmptyFile ()
    file.ArcTables().AddTables [ activeTable; ArcTable.init "Sugar" ]
    file

let private appendAndImportAsNewTable (newTableName: string) =
    let appended = createTemplate "Appended"
    appended.Table.AddColumn(CompositeHeader.Parameter(OntologyAnnotation "temperature"))
    let imported = createTemplate "Imported"

    {
        ImportType = TableJoinOptions.Headers
        SelectedTemplatesForImport = [|
            appended, TemplateImportAction.AppendToActiveTable
            asNewTable imported
        |]
        DeselectedTemplateColumns = Set.empty
        NewTableNames = Map [ imported.Id, newTableName ]
    }

let private tableSummary (arcFile: ArcFiles) =
    arcFile.Tables()
    |> Seq.map (fun table -> table.Name, table.ColumnCount)
    |> Array.ofSeq

for fileType, createEmptyFile in tableFileTypes do
    Vitest.describe (
        $"Template import ({fileType})",
        fun () ->
            Vitest.test (
                "an import changes only the returned copy of the file and names the new table as chosen",
                fun () ->
                    let arcFile = createFile createEmptyFile
                    let payload = appendAndImportAsNewTable " Renamed "

                    let imported = importIntoCopy arcFile (Some 0) payload

                    Vitest.expect(imported.RelatedArcFilesDiscriminate).toBe arcFile.RelatedArcFilesDiscriminate
                    Vitest.expect(tableSummary imported).toEqual [| "New Table 0", 2; "Sugar", 0; "Renamed", 1 |]
                    Vitest.expect(tableSummary arcFile).toEqual [| "New Table 0", 1; "Sugar", 0 |]
                    Vitest.expect((fst payload.SelectedTemplatesForImport.[1]).Table.Name).toBe "Imported"
            )

            Vitest.test (
                "a failed import leaves the open file unchanged",
                fun () ->
                    let arcFile = createFile createEmptyFile

                    Vitest
                        .expect(fun () -> importIntoCopy arcFile (Some 0) (appendAndImportAsNewTable "Sugar") |> ignore)
                        .toThrowError
                        "table names must be unique"

                    Vitest.expect(tableSummary arcFile).toEqual [| "New Table 0", 1; "Sugar", 0 |]
            )
    )
