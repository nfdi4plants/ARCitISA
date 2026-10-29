module internal Swate.Components.Composite.TableImport.TableImportPreviewTests

open ARCtrl
open Browser.Types
open Vitest
open Swate.Components.Composite.TableImport
open Swate.Components.Composite.TableImport.Types

Vitest.afterEach (fun () -> RTL.cleanup ())

let private createSource (index: int) (name: string) =
    let table = ArcTable.init name
    table.AddColumn(CompositeHeader.Input IOType.Source)
    table.AddColumn(CompositeHeader.Output IOType.Sample)

    {
        Index = index
        Name = name
        Table = table
    }

let private callbacks
    (setImportAction: int -> TableImportAction -> unit)
    (isColumnSelected: int -> int -> bool)
    : TableImportPreviewCallbacks =
    {
        GetImportAction = fun _ -> TableImportAction.ImportAsNewTable
        SetImportAction = setImportAction
        IsColumnSelected = isColumnSelected
        ToggleColumnSelection = fun _ _ -> ()
        SelectAllColumns = ignore
        UnselectAllColumns = fun _ _ -> ()
        GetNewTableName = fun _ -> None
        SetNewTableName = fun _ _ -> ()
    }

Vitest.test (
    "sources with the same name keep independent action radios",
    fun () ->
        let calls = ResizeArray<int * TableImportAction>()
        let sources = [| createSource 0 "Same"; createSource 1 "Same" |]

        RTL.render (
            TableImportPreview.TableImportPreview(
                sources,
                callbacks (fun index action -> calls.Add(index, action)) (fun _ _ -> true)
            )
        )
        |> ignore

        Vitest.expect((RTL.screen.getAllByText "Same" |> Seq.length)).toBe 2
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-action-1-NoImport")
        Vitest.expect(calls.Count).toBe 1
        Vitest.expect(calls.[0]).toEqual (1, TableImportAction.NoImport)
)

Vitest.test (
    "append radios are disabled with a note when appending is not possible",
    fun () ->
        let sources = [| createSource 0 "First"; createSource 1 "Second" |]
        let note = "There is no active table."

        RTL.render (
            TableImportPreview.TableImportPreview(
                sources,
                callbacks (fun _ _ -> ()) (fun _ _ -> true),
                canAppend = false,
                appendDisabledNote = note
            )
        )
        |> ignore

        for index in 0..1 do
            let radio =
                RTL.screen.getByTestId $"table-import-action-{index}-AppendToActiveTable" :?> HTMLInputElement

            Vitest.expect(radio.disabled).toBe true

        Vitest.expect((RTL.screen.getAllByText note |> Seq.length)).toBe 2
)

Vitest.test (
    "a hint appears when no column is selected for an imported table",
    fun () ->
        RTL.render (
            TableImportPreview.TableImportPreview(
                [| createSource 0 "First" |],
                callbacks (fun _ _ -> ()) (fun _ _ -> false)
            )
        )
        |> ignore

        RTL.screen.getByTestId "table-import-no-columns-0" |> ignore

        RTL.screen.getByText "Select at least one column to import this table."
        |> ignore
)
