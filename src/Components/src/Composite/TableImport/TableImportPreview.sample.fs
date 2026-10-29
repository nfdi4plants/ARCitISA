module internal Swate.Components.Composite.TableImport.TableImportPreviewSample

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components.Composite.TableImport.Types

let private cells (rows: int) (cell: int -> CompositeCell) =
    ResizeArray [ for row in 0 .. rows - 1 -> cell row ]

/// A table with a free text, a term, a unitized, a data and a comment column.
let buildTable (name: string) (rows: int) =
    let table = ArcTable.init name

    table.AddColumn(
        CompositeHeader.Input IOType.Source,
        cells rows (fun row -> CompositeCell.createFreeText $"Sample {row + 1}")
    )

    table.AddColumn(
        CompositeHeader.Characteristic(OntologyAnnotation("organism", "OBI", "OBI:0100026")),
        cells rows (fun _ -> CompositeCell.createTermFromString ("Arabidopsis thaliana", "NCBITaxon", "NCBITaxon:3702"))
    )

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation("temperature", "PATO", "PATO:0000146")),
        cells
            rows
            (fun row ->
                CompositeCell.createUnitizedFromString (string (20 + row % 5), "degree Celsius", "UO", "UO:0000027")
            )
    )

    table.AddColumn(
        CompositeHeader.Output IOType.Data,
        cells
            rows
            (fun row ->
                let data = Data.empty
                data.FilePath <- Some $"raw/sample-{row + 1}.raw"
                CompositeCell.createData data
            )
    )

    table.AddColumn(
        CompositeHeader.Comment "Notes",
        cells rows (fun row -> CompositeCell.createFreeText (if row % 2 = 0 then "grown in the dark" else ""))
    )

    table

/// Two sources share a name, so the review has to tell them apart.
let sampleSources: ImportSource[] = [|
    {
        Index = 0
        Name = "Measurement"
        Table = buildTable "Measurement" 20
    }
    {
        Index = 1
        Name = "Measurement"
        Table = buildTable "Measurement" 20
    }
    {
        Index = 2
        Name = "Growth"
        Table = buildTable "Growth" 20
    }
|]

let private toggle (item: int * int) (set: Set<int * int>) =
    if set.Contains item then set.Remove item else set.Add item

[<Erase; Mangle(false)>]
type TableImportPreviewSample =

    [<ReactComponent>]
    static member private Preview(canAppend: bool) =
        let actions, setActions = React.useState Map.empty<int, TableImportAction>
        let deselected, setDeselected = React.useState Set.empty<int * int>
        let entered, setEntered = React.useState Map.empty<int, string>

        let actionOf index =
            actions.TryFind index |> Option.defaultValue TableImportAction.ImportAsNewTable

        let names = Helper.newTableNames [] sampleSources actionOf entered

        let callbacks: TableImportPreviewCallbacks = {
            GetImportAction = actionOf
            SetImportAction = fun index action -> setActions (actions.Add(index, action))
            IsColumnSelected = fun index column -> not (deselected.Contains(index, column))
            ToggleColumnSelection = fun index column -> setDeselected (toggle (index, column) deselected)
            SelectAllColumns = fun index -> setDeselected (deselected |> Set.filter (fun (i, _) -> i <> index))
            UnselectAllColumns =
                fun index count ->
                    setDeselected (Set.union deselected (Set.ofList [ for column in 0 .. count - 1 -> index, column ]))
            GetNewTableName = fun index -> names.TryFind index
            SetNewTableName = fun index name -> setEntered (entered.Add(index, name))
        }

        Html.div [
            prop.className "swt:max-w-2xl"
            prop.children [
                TableImportPreview.TableImportPreview(
                    sampleSources,
                    callbacks,
                    canAppend = canAppend,
                    appendDisabledNote = "Select a table in the editor first to append columns to it."
                )
            ]
        ]

    [<ReactComponent>]
    static member Sample() = TableImportPreviewSample.Preview(true)

    [<ReactComponent>]
    static member AppendDisabledSample() = TableImportPreviewSample.Preview(false)
