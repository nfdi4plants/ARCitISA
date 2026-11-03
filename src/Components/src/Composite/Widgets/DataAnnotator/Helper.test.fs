module internal Swate.Components.Composite.Widgets.DataAnnotator.HelperTests

open ARCtrl
open Vitest
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.AnnotationTable.Context
open Swate.Components.Composite.DataMapTable
open Swate.Components.Composite.Widgets
open Swate.Components.Composite.Widgets.DataAnnotator.Types
open Swate.Components.Composite.Widgets.DataAnnotator.Helper

let private input selectors = {
    Selectors = selectors
    FileName = "test.csv"
    FileType = "text/csv"
}

let private selection x y = {|
    xStart = x
    xEnd = x + 1
    yStart = y
    yEnd = y + 1
|}

let private createTable () =
    let table = ArcTable.init "Annotations"
    table.AddColumn(CompositeHeader.FreeText "Source", ResizeArray [ for _ in 1..4 -> CompositeCell.FreeText "sample" ])
    table.AddColumn(CompositeHeader.Output IOType.Data, ResizeArray [ for _ in 1..4 -> CompositeCell.emptyData ])
    table.SetCellAt(1, 0, CompositeCell.createData (Data(name = "existing.csv#row=1")))
    let assay = ArcAssay.init "TestAssay"
    assay.AddTable table
    ArcFiles.Assay assay

let private insert arcFile tableIndex contextKey range selectors =
    let target =
        CellInsertion.tryGetTarget arcFile tableIndex (Map.ofList [ contextKey, AnnotationTableContext.init (range) ])

    let mutable updated = None

    let count =
        insertAnnotationIntoSelectedCells arcFile (fun next -> updated <- Some next) (input selectors) target
        |> Result.defaultWith failwith

    updated.Value, count

Vitest.test (
    "inserts at the selected table cell and preserves previous annotations across submissions",
    fun () ->
        let original = createTable ()

        let first, count =
            insert original (Some 0) "Annotations" (selection 2 2) [| "cell=3,2"; "row=4" |]

        let second, _ =
            insert first (Some 0) "Annotations" (selection 2 4) [| "col=2"; "row=9" |]

        let table = second.Tables().[0]
        Vitest.expect(count).toBe 2
        Vitest.expect(table.RowCount).toBe 4

        Vitest
            .expect(
                [|
                    for row in 0..3 -> table.GetCellAt(1, row).AsData.Selector
                |]
            )
            .toEqual
            [|
                Some "row=1"
                Some "cell=3,2"
                Some "row=4"
                Some "col=2"
            |]

        let data = table.GetCellAt(1, 1).AsData
        Vitest.expect(data.FilePath).toEqual (Some "./assays/TestAssay/dataset/test.csv")
        Vitest.expect(data.Format).toEqual (Some "text/csv")
        Vitest.expect(data.SelectorFormat).toEqual (Some URLs.Data.SelectorFormat.csv)
        Vitest.expect(original.Tables().[0].GetCellAt(1, 1).AsData.Selector).toEqual None
        Vitest.expect(first.Tables().[0].GetCellAt(1, 3).AsData.Selector).toEqual None
        Vitest.expect(table.GetCellAt(0, 1).AsFreeText).toBe "sample"
)

Vitest.test (
    "writes path and selector into a non-data cell without replacing its column",
    fun () ->
        let original = createTable ()

        let updated, _ =
            insert original (Some 0) "Annotations" (selection 1 2) [| "row=2" |]

        Vitest.expect(updated.Tables().[0].GetCellAt(0, 1).AsFreeText).toBe "./assays/TestAssay/dataset/test.csv#row=2"
        Vitest.expect(updated.Tables().[0].Headers.[0]).toEqual (CompositeHeader.FreeText "Source")
)

Vitest.test (
    "inserts into sparse template cells resolved by GetCellAt",
    fun () ->
        let table = ArcTable.init "Sparse"
        table.AddColumn(CompositeHeader.Input IOType.Data, ResizeArray())
        table.AddColumn(CompositeHeader.FreeText "Source", ResizeArray [ CompositeCell.FreeText "sample" ])
        let assay = ArcAssay.init "TestAssay"
        assay.AddTable table

        let updated, _ =
            insert (ArcFiles.Assay assay) (Some 0) "Sparse" (selection 1 1) [| "row=2" |]

        Vitest.expect(updated.Tables().[0].GetCellAt(0, 0).ToDataCell().AsData.Selector).toEqual (Some "row=2")
)

Vitest.test (
    "inserts at the selected DataMap row, preserving metadata and extending like FilePicker",
    fun () ->
        let dataMap = Datamap.init ()
        dataMap.DataContexts.Add(DataContext(name = "existing.csv#row=1", label = "keep"))
        dataMap.DataContexts.Add(DataContext(label = "target label", description = "target description"))
        let original = ArcFiles.DataMap(None, dataMap)

        let updated, count =
            insert original None DataMapTable.SelectionContextKey (selection 1 2) [| "row=2"; "row=3" |]

        let next = updated.TryGetDataMap().Value
        Vitest.expect(count).toBe 2
        Vitest.expect(next.RowCount).toBe 3
        Vitest.expect(next.DataContexts.[0].FilePath).toEqual (Some "existing.csv")
        Vitest.expect(next.DataContexts.[1].Label).toEqual (Some "target label")
        Vitest.expect(next.DataContexts.[1].Description).toEqual (Some "target description")
        Vitest.expect(next.DataContexts.[1].Selector).toEqual (Some "row=2")
        Vitest.expect(next.DataContexts.[2].Selector).toEqual (Some "row=3")
        Vitest.expect(next.DataContexts.[2].Format).toEqual (Some "text/csv")
        Vitest.expect(dataMap.RowCount).toBe 2
        Vitest.expect(dataMap.DataContexts.[1].Selector).toEqual None
)

Vitest.test (
    "does not publish a change without a valid destination or selectors",
    fun () ->
        let original = createTable ()
        let mutable published = false
        let publish _ = published <- true

        let missing =
            insertAnnotationIntoSelectedCells original publish (input [| "row=2" |]) None

        Vitest.expect(Result.isError missing).toBe true

        let target =
            CellInsertion.tryGetTarget
                original
                (Some 0)
                (Map.ofList [
                    "Annotations", AnnotationTableContext.init (selection 2 1)
                ])

        let empty = insertAnnotationIntoSelectedCells original publish (input [||]) target
        Vitest.expect(Result.isError empty).toBe true
        Vitest.expect(published).toBe false

        let header =
            CellInsertion.tryGetTarget
                original
                (Some 0)
                (Map.ofList [
                    "Annotations", AnnotationTableContext.init (selection 0 0)
                ])

        Vitest.expect(header.IsNone).toBe true
)
