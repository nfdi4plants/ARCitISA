module internal Swate.Components.Composite.Template.TemplateActionsTests

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.Template.Types
open Swate.Components.Composite.Template.TemplateActions
open Swate.Components.Tests.TableFiles
open Vitest

/// A template whose table is named like the template.
let private createTemplate (tableName: string) =
    let template = Template.init tableName
    template.Table.AddColumn(CompositeHeader.Input IOType.Source)
    template

let private asNewTable (template: Template) =
    template, TemplateImportAction.ImportAsNewTable

// The naming rules are tested with resolveNewTableNames in tests/Shared; this covers the template adapter.
Vitest.test (
    "names are resolved for the templates imported as new tables, keyed by template, keeping entered names",
    fun () ->
        let first = createTemplate "Sugar"
        let appended = createTemplate "Other"
        let second = createTemplate "Sugar"

        let names =
            newTableNames
                [ "Sugar" ]
                [|
                    asNewTable first
                    appended, TemplateImportAction.AppendToActiveTable
                    asNewTable second
                |]
                (Map [ second.Id, "Own name" ])

        Vitest
            .expect(names |> Map.map (fun _ newTableName -> newTableName.Name) |> Map.toArray)
            .toEqual (Map.toArray (Map [ first.Id, "Sugar 1"; second.Id, "Own name" ]))
)

/// A file whose active (first) table has only an input column, next to a table named "Sugar".
let private createFile (createEmptyFile: unit -> ArcFiles) =
    let activeTable = ArcTable.init "New Table 0"
    activeTable.AddColumn(CompositeHeader.Input IOType.Source)
    withTables createEmptyFile [ activeTable; ArcTable.init "Sugar" ]

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
