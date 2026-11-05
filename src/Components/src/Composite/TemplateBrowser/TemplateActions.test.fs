module internal Swate.Components.Composite.Template.TemplateActionsTests

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.TableImport.Types
open Swate.Components.Composite.Template.TemplateActions
open Swate.Components.Tests.TableFiles
open Vitest

/// A template whose table is named like the template.
let private createTemplate (tableName: string) =
    let template = Template.init tableName
    template.Table.AddColumn(CompositeHeader.Input IOType.Source)
    template

/// A file whose active (first) table has only an input column, next to a table named "Sugar".
let private createFile (createEmptyFile: unit -> ArcFiles) =
    let activeTable = ArcTable.init "New Table 0"
    activeTable.AddColumn(CompositeHeader.Input IOType.Source)
    withTables createEmptyFile [ activeTable; ArcTable.init "Sugar" ]

/// The first template is appended to the active table and the second becomes a new table.
let private appendAndImportAsNewTable (newTableName: string) =
    let appended = createTemplate "Appended"
    appended.Table.AddColumn(CompositeHeader.Parameter(OntologyAnnotation "temperature"))
    let imported = createTemplate "Imported"

    [| appended; imported |],
    {
        defaultDecisions with
            Actions = Map [ 1, TableImportAction.ImportAsNewTable ]
            EnteredNames = Map [ 1, newTableName ]
            ActiveTableIndex = Some 0
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
                    let templates, decisions = appendAndImportAsNewTable " Renamed "

                    let imported =
                        importIntoCopy arcFile templates decisions |> Result.defaultWith raise

                    Vitest.expect(imported.RelatedArcFilesDiscriminate).toBe arcFile.RelatedArcFilesDiscriminate
                    Vitest.expect(tableSummary imported).toEqual [| "New Table 0", 2; "Sugar", 0; "Renamed", 1 |]
                    Vitest.expect(tableSummary arcFile).toEqual [| "New Table 0", 1; "Sugar", 0 |]
                    Vitest.expect(templates.[1].Table.Name).toBe "Imported"
            )

            Vitest.test (
                "a failed import leaves the open file unchanged",
                fun () ->
                    let arcFile = createFile createEmptyFile

                    let templates, decisions = appendAndImportAsNewTable "Sugar"

                    match importIntoCopy arcFile templates decisions with
                    | Ok _ -> failwith "Expected an error"
                    | Error error -> Vitest.expect(error.Message).toBe "A table named \"Sugar\" already exists."

                    Vitest.expect(tableSummary arcFile).toEqual [| "New Table 0", 1; "Sugar", 0 |]
            )

            Vitest.test (
                "deselected template rows reach the template they are keyed to",
                fun () ->
                    let templateWithRows (name: string) =
                        let template = Template.init name

                        template.Table.AddColumn(
                            CompositeHeader.Input IOType.Source,
                            ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"{name}{i}" ]
                        )

                        template

                    let first = templateWithRows "First"
                    let second = templateWithRows "Second"

                    let decisions = {
                        defaultDecisions with
                            ImportType = TableJoinOptions.WithValues
                            DefaultAction = TableImportAction.ImportAsNewTable
                            DeselectedRows = Set.ofList [ 1, 0; 1, 2 ]
                            ActiveTableIndex = Some 0
                    }

                    let imported =
                        importIntoCopy (createFile createEmptyFile) [| first; second |] decisions
                        |> Result.defaultWith raise

                    let tables = imported.Tables()

                    Vitest.expect(tables.[2].RowCount).toBe 3
                    Vitest.expect(tables.[3].RowCount).toBe 1
                    Vitest.expect(tables.[3].GetCellAt(0, 0)).toEqual (CompositeCell.FreeText "Second2")
            )
    )

Vitest.test (
    "remapDecisions moves the choices of the templates that stay selected and drops the others",
    fun () ->
        let a, b, c = System.Guid.NewGuid(), System.Guid.NewGuid(), System.Guid.NewGuid()

        let decisions = {
            defaultDecisions with
                ImportType = TableJoinOptions.WithValues
                DefaultAction = TableImportAction.ImportAsNewTable
                Actions =
                    Map [
                        0, TableImportAction.NoImport
                        1, TableImportAction.AppendToActiveTable
                        2, TableImportAction.NoImport
                    ]
                DeselectedColumns = Set.ofList [ 0, 5; 1, 6; 2, 7 ]
                DeselectedRows = Set.ofList [ 0, 8; 1, 9; 2, 10 ]
                EnteredNames = Map [ 0, "A"; 1, "B"; 2, "C" ]
        }

        let remapped = remapDecisions [| a; b; c |] [| c; b |] decisions

        Vitest.expect(Map.toList remapped.Actions).toEqual [
            0, TableImportAction.NoImport
            1, TableImportAction.AppendToActiveTable
        ]

        Vitest.expect(Set.toList remapped.DeselectedColumns).toEqual [ 0, 7; 1, 6 ]
        Vitest.expect(Set.toList remapped.DeselectedRows).toEqual [ 0, 10; 1, 9 ]
        Vitest.expect(Map.toList remapped.EnteredNames).toEqual [ 0, "C"; 1, "B" ]
        Vitest.expect(remapped.ImportType).toEqual TableJoinOptions.WithValues
        Vitest.expect(remapped.DefaultAction).toEqual TableImportAction.ImportAsNewTable
)
