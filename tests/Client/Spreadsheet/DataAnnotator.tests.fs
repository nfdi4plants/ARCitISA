module Spreadsheet.Tests.DataAnnotator

open Fable.Mocha
open ARCtrl
open Swate.Components.Shared
open global.Spreadsheet

module ComponentDataAnnotatorTypes = Swate.Components.Composite.Widgets.DataAnnotator.Types
module ComponentDataAnnotatorHelper = Swate.Components.Composite.Widgets.DataAnnotator.Helper
module WidgetTypes = Swate.Components.Composite.Widgets.Types

let private createTableState rowCount =
    let table = ArcTable.init "TestTable"

    let sourceCells =
        Array.init rowCount (fun index -> CompositeCell.FreeText $"row-{index + 1}")
        |> ResizeArray

    table.AddColumn(CompositeHeader.FreeText "Source", sourceCells)

    let assay = ArcAssay.init "TestAssay"
    assay.AddTable table

    Spreadsheet.Model.init (ArcFiles.Assay assay, Spreadsheet.ActiveView.Table 0)

let private createEmptyTableState () =
    let table = ArcTable.init "EmptyTable"
    let assay = ArcAssay.init "TestAssay"
    assay.AddTable table

    Spreadsheet.Model.init (ArcFiles.Assay assay, Spreadsheet.ActiveView.Table 0)

let private addDataAnnotation selectors rowCount =
    let state = createTableState rowCount

    Spreadsheet.Controller.BuildingBlocks.addDataAnnotation
        {|
            fragmentSelectors = selectors
            fileName = "test.csv"
            fileType = "text/csv"
            targetColumn = DataAnnotator.TargetColumn.Autodetect
        |}
        state

let private addDataAnnotationToEmptyTable selectors =
    let state = createEmptyTableState ()

    Spreadsheet.Controller.BuildingBlocks.addDataAnnotation
        {|
            fragmentSelectors = selectors
            fileName = "test.csv"
            fileType = "text/csv"
            targetColumn = DataAnnotator.TargetColumn.Autodetect
        |}
        state

let Main =
    testList "Spreadsheet Data Annotator" [
        testCase "pads shorter selector lists to existing table row count"
        <| fun _ ->
            let nextState = addDataAnnotation [| "row=2" |] 3
            let outputColumn = nextState.ActiveTable.GetOutputColumn()

            Expect.equal nextState.ActiveTable.RowCount 3 "Row count should stay unchanged."
            Expect.equal outputColumn.Cells.Count 3 "Output column must match table row count."

            let firstCell = outputColumn.Cells.[0].AsData
            let secondCell = outputColumn.Cells.[1].AsData

            Expect.equal firstCell.FilePath (Some "test.csv") "First selector should populate the first row."
            Expect.equal firstCell.Selector (Some "row=2") "First selector should be written into the first row."
            Expect.equal secondCell.FilePath None "Remaining rows should stay empty."
            Expect.equal secondCell.Selector None "Remaining rows should stay empty."

        testCase "extends table rows when selector list is longer than current table"
        <| fun _ ->
            let nextState = addDataAnnotation [| "row=1"; "row=2"; "row=3" |] 1
            let outputColumn = nextState.ActiveTable.GetOutputColumn()

            Expect.equal nextState.ActiveTable.RowCount 3 "Table should grow to fit all selectors."
            Expect.equal outputColumn.Cells.Count 3 "Output column must include all selectors."

            let thirdCell = outputColumn.Cells.[2].AsData

            Expect.equal thirdCell.FilePath (Some "test.csv") "Newly added rows should receive annotation data."
            Expect.equal thirdCell.Selector (Some "row=3") "Selectors should be written in order."

        testCase "creates the first data column for an empty table"
        <| fun _ ->
            let nextState = addDataAnnotationToEmptyTable [| "row=1"; "row=2" |]
            let outputColumn = nextState.ActiveTable.GetOutputColumn()

            Expect.equal nextState.ActiveTable.RowCount 2 "Empty tables should grow to fit selected annotations."
            Expect.equal outputColumn.Cells.Count 2 "The created output column should include all selected annotations."

            let secondCell = outputColumn.Cells.[1].AsData

            Expect.equal secondCell.FilePath (Some "test.csv") "Created rows should store the selected file path."
            Expect.equal secondCell.Selector (Some "row=2") "Created rows should store selectors in order."

        testCase "stores assay DataMap uploads relative to the ARC root"
        <| fun _ ->
            let assay = ArcAssay.init "MyAssay"
            let dataMap = Datamap.init ()
            assay.Datamap <- Some dataMap
            let arcFile = ArcFiles.Assay assay
            let mutable nextArcFile = None

            ComponentDataAnnotatorHelper.insertAnnotationIntoSelectedCells
                arcFile
                (fun value -> nextArcFile <- Some value)
                {
                    Selectors = [| "row=2" |]
                    FileName = "test.csv"
                    FileType = "text/csv"
                }
                (Some(
                    WidgetTypes.InsertTarget.DataMap {|
                        xStart = 1
                        xEnd = 1
                        yStart = 1
                        yEnd = 1
                    |}
                ))
            |> Result.defaultWith failwith
            |> ignore

            let nextDataMap = nextArcFile.Value.TryGetDataMap().Value

            Expect.equal
                nextDataMap.DataContexts.[0].FilePath
                (Some "./assays/MyAssay/dataset/test.csv")
                "DataMap file paths must be relative to the ARC root."

            Expect.equal
                dataMap.DataContexts.Count
                0
                "Submitting must not mutate the DataMap currently rendered by React."

        testCase "creates ARC-root-relative paths for every DataMap parent"
        <| fun _ ->
            let cases = [|
                ArcFiles.Assay(ArcAssay.init "MyAssay"), "./assays/MyAssay/dataset/test.csv"
                ArcFiles.Study(ArcStudy.init "MyStudy", []), "./studies/MyStudy/resources/test.csv"
                ArcFiles.Run(ArcRun.init "MyRun"), "./runs/MyRun/test.csv"
                ArcFiles.Workflow(ArcWorkflow.init "MyWorkflow"), "./workflows/MyWorkflow/test.csv"
                ArcFiles.DataMap(Some(DatamapParentInfo.create "MyStudy" DataMapParent.Study), Datamap.init ()),
                "./studies/MyStudy/resources/test.csv"
            |]

            for arcFile, expected in cases do
                Expect.equal
                    (toArcRootRelativeFilePath arcFile "test.csv")
                    expected
                    "Bare filenames must be rooted below their owning ARC entity."

        testCase "preserves an existing ARC-root-relative DataMap path"
        <| fun _ ->
            let arcFile = ArcFiles.Assay(ArcAssay.init "MyAssay")
            let rootRelativePath = "assays/MyAssay/dataset/test.csv"

            let actual = toArcRootRelativeFilePath arcFile rootRelativePath

            Expect.equal
                actual
                $"./{rootRelativePath}"
                "Existing ARC-root-relative paths must use an explicit relative prefix."

        testCase "stores table-view uploads relative to the ARC root"
        <| fun _ ->
            let table = ArcTable.init "MyTable"
            table.AddColumn(CompositeHeader.FreeText "Source", ResizeArray [ CompositeCell.FreeText "sample" ])
            table.AddColumn(CompositeHeader.Output IOType.Data, ResizeArray [ CompositeCell.emptyData ])
            let assay = ArcAssay.init "MyAssay"
            assay.AddTable table
            let arcFile = ArcFiles.Assay assay
            let mutable nextArcFile = None

            ComponentDataAnnotatorHelper.insertAnnotationIntoSelectedCells
                arcFile
                (fun value -> nextArcFile <- Some value)
                {
                    Selectors = [| "row=2" |]
                    FileName = "test.csv"
                    FileType = "text/csv"
                }
                (Some(
                    WidgetTypes.InsertTarget.Table(
                        0,
                        {|
                            xStart = 1
                            xEnd = 1
                            yStart = 0
                            yEnd = 0
                        |}
                    )
                ))
            |> Result.defaultWith failwith
            |> ignore

            let outputColumn = nextArcFile.Value.Tables().[0].GetOutputColumn()

            Expect.equal
                outputColumn.Cells.[0].AsData.FilePath
                (Some "./assays/MyAssay/dataset/test.csv")
                "Table-view Data Annotator paths must be relative to the ARC root."
    ]
