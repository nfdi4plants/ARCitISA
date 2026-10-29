module internal Swate.Components.Composite.TableImport.TableImportPreviewTests

open ARCtrl
open Browser.Types
open Fable.Core
open Vitest
open Swate.Components.Composite.TableImport
open Swate.Components.Composite.TableImport.Types

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

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
        IsRowSelected = fun _ _ -> true
        ToggleRowSelection = fun _ _ -> ()
        SelectAllRows = ignore
        UnselectAllRows = fun _ _ -> ()
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

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockHTMLElementGeometry (vi: Vi) (propertyName: string) (value: int) : unit = jsNative

[<Emit("$0.scrollTop = $1")>]
let private setScrollTop (el: HTMLElement) (value: float) : unit = jsNative

[<Emit("$0.dispatchEvent(new Event('scroll'))")>]
let private dispatchScroll (el: HTMLElement) : unit = jsNative

/// Without layout the grid renders its first rows through a fallback. The size lets the virtualizer measure,
/// so rows past the fallback render and a scroll moves the rendered window.
let private installGeometry () =
    mockHTMLElementGeometry Vitest.vi "offsetWidth" 800
    mockHTMLElementGeometry Vitest.vi "offsetHeight" 600

let private sourceWithRows (rowCount: int) =
    let table = ArcTable.init "Rows"

    table.AddColumn(
        CompositeHeader.Input IOType.Source,
        ResizeArray [
            for i in 1..rowCount -> CompositeCell.FreeText $"Sample {i}"
        ]
    )

    table.AddColumn(
        CompositeHeader.Characteristic(OntologyAnnotation "organism"),
        ResizeArray [
            for _ in 1..rowCount -> CompositeCell.Term(OntologyAnnotation "Arabidopsis")
        ]
    )

    {
        Index = 0
        Name = "Rows"
        Table = table
    }

let private rowCheckboxes () =
    RTL.screen.queryAllByRole "checkbox"
    |> Seq.filter (fun el -> el.getAttribute("data-testid").StartsWith "table-import-row-")
    |> Seq.length

Vitest.test (
    "a large table renders only the visible rows with their cell texts",
    fun () ->
        installGeometry ()

        RTL.render (
            TableImportPreview.TableImportPreview(
                [| sourceWithRows 5000 |],
                callbacks (fun _ _ -> ()) (fun _ _ -> true)
            )
        )
        |> ignore

        Vitest.expect(rowCheckboxes () < 100).toBe true
        Vitest.expect(rowCheckboxes () > 0).toBe true
        // Row 21 is past the fallback of 13 rows, so the virtualizer measured the 600px grid.
        RTL.screen.getByTestId "table-import-row-0-20" |> ignore
        RTL.screen.getByText "Sample 1" |> ignore
        Vitest.expect((RTL.screen.getAllByText "Arabidopsis").Count > 0).toBe true
        RTL.screen.getByText "Columns: 2/2 selected, rows: 5000/5000 selected" |> ignore
)

Vitest.test (
    "the row checkbox and the row buttons call the row callbacks",
    fun () ->
        installGeometry ()
        let toggled = ResizeArray<int * int>()
        let selectedAll = ResizeArray<int>()
        let unselectedAll = ResizeArray<int * int>()

        let rowCallbacks = {
            callbacks (fun _ _ -> ()) (fun _ _ -> true) with
                ToggleRowSelection = fun index row -> toggled.Add(index, row)
                SelectAllRows = selectedAll.Add
                UnselectAllRows = fun index count -> unselectedAll.Add(index, count)
        }

        RTL.render (TableImportPreview.TableImportPreview([| sourceWithRows 5000 |], rowCallbacks))
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-row-0-2")
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-select-all-rows-0")
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-unselect-all-rows-0")

        Vitest.expect(List.ofSeq toggled).toEqual [ 0, 2 ]
        Vitest.expect(List.ofSeq selectedAll).toEqual [ 0 ]
        Vitest.expect(List.ofSeq unselectedAll).toEqual [ 0, 5000 ]
)

Vitest.test (
    "without row selection the row checkboxes and buttons are hidden and the cell texts stay",
    fun () ->
        installGeometry ()

        RTL.render (
            TableImportPreview.TableImportPreview(
                [| sourceWithRows 5 |],
                callbacks (fun _ _ -> ()) (fun _ _ -> true),
                showRowSelection = false
            )
        )
        |> ignore

        Vitest.expect(rowCheckboxes ()).toBe 0
        Vitest.expect(RTL.screen.queryByTestId("table-import-select-all-rows-0").IsNone).toBe true
        Vitest.expect(RTL.screen.queryByTestId("table-import-unselect-all-rows-0").IsNone).toBe true
        RTL.screen.getByText "Sample 1" |> ignore
        RTL.screen.getByText "Columns: 2/2 selected" |> ignore
)

Vitest.test (
    "a table without rows says so and has no row checkbox",
    fun () ->
        RTL.render (
            TableImportPreview.TableImportPreview([| sourceWithRows 0 |], callbacks (fun _ _ -> ()) (fun _ _ -> true))
        )
        |> ignore

        RTL.screen.getByText "This table has no rows." |> ignore
        Vitest.expect(rowCheckboxes ()).toBe 0
)

Vitest.test (
    "scrolling the grid renders the rows at the new position",
    fun () -> promise {
        installGeometry ()
        let toggled = ResizeArray<int * int>()

        let rowCallbacks = {
            callbacks (fun _ _ -> ()) (fun _ _ -> true) with
                ToggleRowSelection = fun index row -> toggled.Add(index, row)
        }

        RTL.render (TableImportPreview.TableImportPreview([| sourceWithRows 5000 |], rowCallbacks))
        |> ignore

        Vitest.expect(RTL.screen.queryByTestId("table-import-row-0-3000").IsNone).toBe true

        let grid = RTL.screen.getByTestId "table-import-grid-0"
        setScrollTop grid (3000.0 * 28.0)
        dispatchScroll grid

        let! row = RTL.screen.findByTestId "table-import-row-0-3000"
        RTL.fireEvent.click row
        Vitest.expect(List.ofSeq toggled).toEqual [ 0, 3000 ]
    }
)
