module Components.Tests.Table.ContextMenu

open Fable.Mocha
open Fable.Core
open ARCtrl
open Swate.Components
open Swate.Components.Composite.AnnotationTable.Types
open global.Swate.Components.Composite.AnnotationTable
open global.Swate.Components.Composite.AnnotationTable.Types.AnnotationTableContextMenu
open global.Swate.Components.Composite.DataMapTable.Types
open global.Swate.Components.Composite.DataMapTable.ClipboardTarget
open global.Swate.Components.Composite.Table
open global.Swate.Components.Composite.Table.Types
open global.Swate.Components.Primitive.ContextMenu.Types
open Browser.Types

[<Emit("Object.defineProperty($0, 'clipboard', { configurable: true, value: $1 })")>]
let private setNavigatorClipboard
    (navigator: Swate.Components.Navigator)
    (clipboard: Swate.Components.Clipboard)
    : unit =
    jsNative

[<Emit("(() => { const original = globalThis.ClipboardItem; globalThis.ClipboardItem = class { constructor(values) { this.types = Object.keys(values); } }; return original; })()")>]
let private installClipboardItemMock () : obj = jsNative

[<Emit("globalThis.ClipboardItem = $0")>]
let private restoreClipboardItem (clipboardItem: obj) : unit = jsNative

type TestCases =

    static member private NoSelectionHandle() =
        SelectHandle(
            (fun _ -> false),
            (fun _ -> ()),
            (fun _ -> ()),
            (fun _ -> None),
            (fun _ -> ResizeArray()),
            (fun _ -> 0)
        )

    static member private TriggerMenuItem (item: ContextMenuItem) (spawnData: CellCoordinate) =
        item.onClick
        |> Option.iter (fun onClick ->
            onClick {|
                buttonEvent = unbox<MouseEvent> null
                spawnData = box spawnData
            |}
        )

    static member AddColumns(selectHandle: SelectHandle, pasteData: string[][], expectedColumns: CompositeColumn[]) =
        let currentTable = Fixture.mkTable ()

        let cellCoordinates = selectHandle.getSelectedCells () |> Array.ofSeq
        let clickedCell = cellCoordinates.[0]

        //Group all cells based on their row
        let groupedCellCoordinates =
            cellCoordinates
            |> Array.groupBy (fun item -> item.y)
            |> Array.map (fun (_, row) -> row)

        let pasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (clickedCell, currentTable, selectHandle, pasteData)

        Expect.equal
            pasteBehavior
            (PasteCases.AddColumns {|
                coordinate = clickedCell
                coordinates = groupedCellCoordinates
                data = expectedColumns |> ResizeArray
            |})
            "Should predict add column behavior"

    static member AddSingleCell(pasteData: string[][], expectedColumn: CompositeColumn[]) =

        let currentTable = Fixture.mkTable ()
        let clickedCell: CellCoordinate = {| x = 1; y = 1 |}

        let selectHandle: SelectHandle = Fixture.mkSelectHandle (1, 1, 1, 1)

        let pasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (clickedCell, currentTable, selectHandle, pasteData)

        Expect.equal
            pasteBehavior
            (PasteCases.PasteCells {|
                data = expectedColumn |> ResizeArray
                coordinates = [| [| clickedCell |] |]
            |})
            "Should predict paste single cell behavior"

    static member PasteMultipleCells
        (selectHandle: SelectHandle, pasteData: string[][], expectedColumns: CompositeColumn[])
        =
        let currentTable = Fixture.mkTable ()
        let cellCoordinates = Fixture.getRangeOfSelectedCells (selectHandle)

        let clickedCell: CellCoordinate = {| x = 1; y = 1 |}

        let pasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (clickedCell, currentTable, selectHandle, pasteData)

        Expect.equal
            pasteBehavior
            (PasteCases.PasteCells {|
                data = expectedColumns |> ResizeArray
                coordinates = cellCoordinates
            |})
            "Should predict paste fitted cells behavior"

    static member AddFittingTerm
        (selectHandle: SelectHandle, pasteData: string[][], expectedColumns: CompositeColumn[])
        =
        let currentTable = Fixture.mkTable ()
        let cellCoordinates = Fixture.getRangeOfSelectedCells (selectHandle)

        let clickedCell: CellCoordinate = {| x = 3; y = 1 |}

        let pasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (clickedCell, currentTable, selectHandle, pasteData)

        Expect.equal
            pasteBehavior
            (PasteCases.PasteCells {|
                data = expectedColumns |> ResizeArray
                coordinates = cellCoordinates
            |})
            "Should predict paste fitted cells behavior"

    static member AddUnknownPattern(pasteData: string[][]) =
        let currentTable = Fixture.mkTable ()
        let selectHandle: SelectHandle = Fixture.mkSelectHandle (1, 1, 4, 4)

        let headers =
            let columnIndices =
                selectHandle.getSelectedCells ()
                |> Array.ofSeq
                |> Array.distinctBy (fun item -> item.x)

            columnIndices
            |> Array.map (fun index -> currentTable.GetColumn(index.x - 1).Header)

        let clickedCell: CellCoordinate = {| x = 4; y = 1 |}

        let adaptedData = pasteData |> Array.map (fun item -> item)

        let pasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (clickedCell, currentTable, selectHandle, adaptedData)

        Expect.equal
            pasteBehavior
            (PasteCases.Unknown {|
                data = adaptedData
                headers = headers
            |})
            "Should predict paste fitted cells behavior"

    static member PasteIntoClickedColumnOutsideSelection() =
        let currentTable = Fixture.mkTable ()
        let staleSelection = Fixture.mkSelectHandle (1, 1, 1, 1)
        let clickedTermCell: CellCoordinate = {| x = 3; y = 1 |}

        let termPasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (
                clickedTermCell,
                currentTable,
                staleSelection,
                Fixture.Body_Component_InstrumentModel_SingleRow_Term_String
            )

        Expect.equal
            termPasteBehavior
            (PasteCases.PasteCells {|
                data = [| Fixture.Component_InstrumentModel_Term_Body |] |> ResizeArray
                coordinates = [| [| clickedTermCell |] |]
            |})
            "Term paste should use the clicked column header when the previous selection is elsewhere"

        let clickedUnitCell: CellCoordinate = {| x = 4; y = 1 |}
        let unitData = [| [| "4"; "Degree Celsius"; "UO"; "UO:000000001" |] |]

        let unitPasteBehavior =
            AnnotationTableContextMenuUtil.predictPasteBehaviour (
                clickedUnitCell,
                currentTable,
                staleSelection,
                unitData
            )

        let expectedUnitColumn =
            CompositeColumn.create (
                currentTable.GetColumn(3).Header,
                [|
                    CompositeCell.createUnitizedFromString ("4", "Degree Celsius", "UO", "UO:000000001")
                |]
                |> ResizeArray
            )

        Expect.equal
            unitPasteBehavior
            (PasteCases.PasteCells {|
                data = [| expectedUnitColumn |] |> ResizeArray
                coordinates = [| [| clickedUnitCell |] |]
            |})
            "Unit paste should use the clicked column header when the previous selection is elsewhere"

    static member HeaderDeleteFirstColumn() =
        let table = Fixture.mkTable ()
        let selectHandle = TestCases.NoSelectionHandle()
        let originalColumnCount = table.ColumnCount
        let mutable updatedTable = table

        let menuItems =
            AnnotationTableContextMenu.CompositeHeaderContent(
                1,
                table,
                (fun nextTable -> updatedTable <- nextTable),
                selectHandle,
                ignore
            )

        TestCases.TriggerMenuItem menuItems.[6] {| x = 1; y = 0 |}

        Expect.equal
            updatedTable.ColumnCount
            (originalColumnCount - 1)
            "Delete column should remove the first data column"

        Expect.equal
            updatedTable.Headers.[0]
            (CompositeHeader.Output IOType.Data)
            "After deleting first column, previous second column should become first"

    static member IndexDeleteFirstRow() =
        let table = Fixture.mkTable ()
        let selectHandle = TestCases.NoSelectionHandle()
        let originalRowCount = table.RowCount
        let expectedFirstCellAfterDelete = table.GetCellAt(0, 1).ToTabStr()
        let mutable updatedTable = table

        let menuItems =
            AnnotationTableContextMenu.IndexColumnContent(
                1,
                table,
                (fun nextTable -> updatedTable <- nextTable),
                selectHandle
            )

        TestCases.TriggerMenuItem menuItems.[0] {| x = 0; y = 1 |}

        Expect.equal updatedTable.RowCount (originalRowCount - 1) "Delete row should remove the first data row"

        Expect.equal
            (updatedTable.GetCellAt(0, 0).ToTabStr())
            expectedFirstCellAfterDelete
            "After deleting first row, second row content should become first row"

    static member DataMainFieldPreservesSelector() =
        let original = CompositeCell.createDataFromString "old.txt"
        let updated = original.UpdateMainField "DatamapTesting.txt#row=2"

        Expect.equal updated.AsData.FilePath (Some "DatamapTesting.txt") "The path should exclude the selector."
        Expect.equal updated.AsData.Selector (Some "row=2") "The selector behind # should be preserved."

    static member DataMapCopyIncludesSelector() =
        let dataMap =
            Datamap(ResizeArray [ DataContext(name = "DatamapTesting.txt#row=2") ])

        let copiedText =
            Swate.Components.ClipboardContract.Contract.Capture.matrix
                (fun coordinate -> dataMap.GetCell(coordinate.x - 1, coordinate.y - 1))
                [ {| x = 1; y = 1 |} ]
            |> Swate.Components.ClipboardContract.Contract.Capture.toPlainText

        Expect.equal copiedText "DatamapTesting.txt#row=2" "Copied DataMap data should include its selector."

        let dataMapWithoutSelector =
            Datamap(ResizeArray [ DataContext(name = "DatamapTesting.txt") ])

        let copiedTextWithoutSelector =
            Swate.Components.ClipboardContract.Contract.Capture.matrix
                (fun coordinate -> dataMapWithoutSelector.GetCell(coordinate.x - 1, coordinate.y - 1))
                [ {| x = 1; y = 1 |} ]
            |> Swate.Components.ClipboardContract.Contract.Capture.toPlainText

        Expect.equal
            copiedTextWithoutSelector
            "DatamapTesting.txt"
            "Copied DataMap data should not include trailing tabs for empty metadata."

    static member DataMapCopyKeepsTermsInGridColumns() =
        let source =
            Datamap(
                ResizeArray [
                    DataContext(
                        explication = OntologyAnnotation("explicit", "TST", "TST:1"),
                        unit = OntologyAnnotation("metre", "UO", "UO:0000008")
                    )
                ]
            )

        let copiedText =
            Swate.Components.ClipboardContract.Contract.Capture.matrix
                (fun coordinate -> source.GetCell(coordinate.x - 1, coordinate.y - 1))
                [ {| x = 5; y = 1 |}; {| x = 6; y = 1 |} ]
            |> Swate.Components.ClipboardContract.Contract.Capture.toPlainText

        Expect.equal copiedText "explicit\tmetre" "Each selected DataMap cell should occupy one clipboard column."

        let target = Datamap(ResizeArray [ DataContext() ])
        let targetAnchor: CellCoordinate = {| x = 2; y = 1 |}
        target.PasteTabText(targetAnchor, [| targetAnchor |], copiedText)

        Expect.equal target.DataContexts.[0].Label (Some "explicit") "The first value should use the target column."
        Expect.equal target.DataContexts.[0].Description (Some "metre") "The next value should not shift right."

    static member DataMapPlainTextPasteKeepsTsvCellsSeparate() =
        let dataMap = Datamap(ResizeArray [ DataContext() ])

        let firstAnchor: CellCoordinate = {| x = 5; y = 1 |}
        dataMap.PasteTabText(firstAnchor, [| firstAnchor |], "foo\tbar\tbaz")

        Expect.equal
            dataMap.DataContexts.[0].Explication.Value.NameText
            "foo"
            "The first value should be pasted into Explication."

        Expect.equal dataMap.DataContexts.[0].Unit.Value.NameText "bar" "The second value should be pasted into Unit."

        Expect.equal
            dataMap.DataContexts.[0].ObjectType.Value.NameText
            "baz"
            "The third value should be pasted into ObjectType."

        Expect.equal
            dataMap.DataContexts.[0].Explication.Value.TermSourceREF
            None
            "Ordinary TSV must not be interpreted as ontology metadata."

        let secondAnchor: CellCoordinate = {| x = 6; y = 2 |}
        dataMap.PasteTabText(secondAnchor, [| secondAnchor |], "one\ttwo\tthree\tfour")

        Expect.equal
            dataMap.DataContexts.[1].Unit.Value.NameText
            "one"
            "Four-field TSV starting at Unit should remain cell-oriented."

        Expect.equal
            dataMap.DataContexts.[1].ObjectType.Value.NameText
            "two"
            "The next in-range value should be pasted into ObjectType."

        let repeatedMap = Datamap(ResizeArray [ DataContext(); DataContext() ])
        let repeatAnchor: CellCoordinate = {| x = 5; y = 1 |}

        let selection: CellCoordinate[] = [|
            {| x = 5; y = 1 |}
            {| x = 6; y = 1 |}
            {| x = 7; y = 1 |}
            {| x = 5; y = 2 |}
            {| x = 6; y = 2 |}
            {| x = 7; y = 2 |}
        |]

        repeatedMap.PasteTabText(repeatAnchor, selection, "alpha\tbeta")

        for row in repeatedMap.DataContexts do
            Expect.equal row.Explication.Value.NameText "alpha" "Plain text should repeat from the first source column."
            Expect.equal row.Unit.Value.NameText "beta" "Plain text should repeat from the second source column."
            Expect.equal row.ObjectType.Value.NameText "alpha" "Plain text should wrap across a wider selection."

    static member DataMapStructuredPastePreservesTermsAndUnits() =
        let term = CompositeCell.createTermFromString ("explicit", "TST", "TST:1")
        let unit = CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
        let structuredTarget = Datamap(ResizeArray [ DataContext() ])
        let cells = [| [| term; unit |] |]
        let anchor: CellCoordinate = {| x = 5; y = 1 |}

        structuredTarget.PasteStructuredCells(anchor, [| anchor |], cells)

        Expect.equal
            structuredTarget.DataContexts.[0].Explication.Value.TermSourceREF
            (Some "TST")
            "Structured term paste should preserve ontology metadata."

        Expect.equal
            structuredTarget.DataContexts.[0].Unit.Value.NameText
            "metre"
            "Structured unit paste should preserve the unit."

        Expect.equal
            structuredTarget.DataContexts.[0].Unit.Value.TermAccessionNumber
            (Some "UO:0000008")
            "Structured unit paste should preserve ontology metadata."

    static member DataMapUnitizedPasteIntoFreeTextPreservesVisibleValue() =
        let dataMap = Datamap(ResizeArray [ DataContext() ])
        let anchor: CellCoordinate = {| x = 2; y = 1 |}

        dataMap.PasteStructuredCells(
            anchor,
            [| anchor |],
            [|
                [|
                    CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                |]
            |]
        )

        Expect.equal
            dataMap.DataContexts.[0].Label
            (Some "4 metre")
            "Pasting a unitized DataMap cell into free text should retain its complete visible value."

    static member PlainTextSelectionGeometryMatchesAcrossTargets() =
        let dataMapSelection: CellCoordinate[] = [|
            {| x = 5; y = 1 |}
            {| x = 6; y = 1 |}
            {| x = 7; y = 1 |}
            {| x = 5; y = 2 |}
            {| x = 6; y = 2 |}
            {| x = 7; y = 2 |}
        |]

        let dataMap = Datamap(ResizeArray [ DataContext(); DataContext() ])
        dataMap.PasteTabText(dataMapSelection.[0], dataMapSelection, "A\tB")

        let table = Fixture.mkTable ()
        let mutable updatedTable = table
        let tableSelection = Fixture.mkSelectHandle (1, 2, 2, 4)

        AnnotationTableContextMenuUtil.applyPlainTextCells (
            {| x = 2; y = 1 |},
            table,
            tableSelection,
            "A\tB",
            (fun nextTable -> updatedTable <- nextTable)
        )

        for rowIndex in 0..1 do
            let dataMapValues = [|
                for columnIndex in 4..6 -> dataMap.GetCell(columnIndex, rowIndex).ToString()
            |]

            let tableValues = [|
                for columnIndex in 1..3 -> updatedTable.GetCellAt(columnIndex, rowIndex).ToString()
            |]

            Expect.equal dataMapValues [| "A"; "B"; "A" |] "DataMap should repeat the two source cells."
            Expect.equal tableValues dataMapValues "AnnotationTable should preserve the same source geometry."

    static member PlainTextPreservesEmptyRowsAcrossTargets() =
        let text = "A\n\nB"
        let dataMap = Datamap(ResizeArray [ DataContext(); DataContext(); DataContext() ])
        let dataMapAnchor: CellCoordinate = {| x = 2; y = 1 |}
        dataMap.PasteTabText(dataMapAnchor, [| dataMapAnchor |], text)

        let table = Fixture.mkTable ()
        let tableAnchor: CellCoordinate = {| x = 1; y = 1 |}
        let mutable updatedTable = table

        AnnotationTableContextMenuUtil.applyPlainTextCells (
            tableAnchor,
            table,
            Fixture.mkSelectHandle (1, 1, 1, 1),
            text,
            (fun nextTable -> updatedTable <- nextTable)
        )

        let dataMapValues = [|
            for rowIndex in 0..2 -> dataMap.GetCell(1, rowIndex).ToString()
        |]

        let tableValues = [|
            for rowIndex in 0..2 -> updatedTable.GetCellAt(0, rowIndex).ToString()
        |]

        Expect.equal dataMapValues [| "A"; ""; "B" |] "DataMap should preserve the empty middle row."
        Expect.equal tableValues dataMapValues "AnnotationTable should preserve the same empty-row geometry."

    static member PlainTextParserIgnoresTerminalSeparators() =
        let parse = Swate.Components.ClipboardContract.Contract.PlainText.parseRows
        Expect.equal (parse "A\n") [| [| "A" |] |] "A terminal newline should terminate, not add, a row."
        Expect.equal (parse "A\n\nB") [| [| "A" |]; [| "" |]; [| "B" |] |] "An internal empty row should be retained."

    static member AnnotationTablePlainNumericPastePreservesTargetUnit() =
        let table = Fixture.mkTable ()
        let anchor: CellCoordinate = {| x = 4; y = 1 |}
        let mutable updatedTable = table

        AnnotationTableContextMenuUtil.applyPlainTextCells (
            anchor,
            table,
            Fixture.mkSelectHandle (1, 1, 4, 4),
            "4",
            (fun nextTable -> updatedTable <- nextTable)
        )

        let value, unit = updatedTable.GetCellAt(3, 0).AsUnitized
        Expect.equal value "4" "A numeric plain-text value should remain unitized."
        Expect.equal unit.NameText "Degree Celsius" "The existing destination unit should be retained."
        Expect.equal unit.TermAccessionNumber (Some "UO:000000001") "The existing unit metadata should be retained."

    static member AnnotationTableStructuredOverflowClipsColumnsAndExtendsRows() =
        let table = Fixture.mkTable ()
        let originalRowCount = table.RowCount
        let originalColumnCount = table.ColumnCount

        let anchor: CellCoordinate = {|
            x = originalColumnCount
            y = originalRowCount
        |}

        let mutable updatedTable = table

        AnnotationTableContextMenuUtil.applyStructuredCells (
            anchor,
            table,
            Fixture.mkSelectHandle (anchor.y, anchor.y, anchor.x, anchor.x),
            [|
                [| CompositeCell.FreeText "A"; CompositeCell.FreeText "B" |]
                [| CompositeCell.FreeText "C"; CompositeCell.FreeText "D" |]
            |],
            (fun nextTable -> updatedTable <- nextTable)
        )

        Expect.equal updatedTable.ColumnCount originalColumnCount "Right overflow should be clipped."
        Expect.equal updatedTable.RowCount (originalRowCount + 1) "Bottom overflow should extend rows."

        Expect.equal
            (updatedTable.GetCellAt(originalColumnCount - 1, originalRowCount - 1).ToString())
            "A"
            "The in-range source cell should be applied."

        Expect.equal
            (updatedTable.GetCellAt(originalColumnCount - 1, originalRowCount).ToString())
            "C"
            "The next source row should be applied to the extended row."

    static member AnnotationTableStructuredPastePreservesOntologyMetadata() =
        let table = Fixture.mkTable ()
        let term = CompositeCell.createTermFromString ("instrument", "MS", "MS:1000031")

        let unitized =
            CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")

        let anchor: CellCoordinate = {| x = 3; y = 1 |}
        let mutable updatedTable = table

        AnnotationTableContextMenuUtil.applyStructuredCells (
            anchor,
            table,
            Fixture.mkSelectHandle (1, 1, 3, 4),
            [| [| term; unitized |] |],
            (fun nextTable -> updatedTable <- nextTable)
        )

        let pastedTerm = updatedTable.GetCellAt(2, 0).AsTerm
        let _, pastedUnit = updatedTable.GetCellAt(3, 0).AsUnitized
        Expect.equal pastedTerm.TermSourceREF (Some "MS") "Term source metadata should be preserved."
        Expect.equal pastedTerm.TermAccessionNumber (Some "MS:1000031") "Term accession metadata should be preserved."
        Expect.equal pastedUnit.TermSourceREF (Some "UO") "Unit source metadata should be preserved."
        Expect.equal pastedUnit.TermAccessionNumber (Some "UO:0000008") "Unit accession metadata should be preserved."

    static member AnnotationTableStructuredUnitlessValuePreservesTargetUnit() =
        let table = Fixture.mkTable ()
        let anchor: CellCoordinate = {| x = 4; y = 1 |}
        let mutable updatedTable = table

        AnnotationTableContextMenuUtil.applyStructuredCells (
            anchor,
            table,
            Fixture.mkSelectHandle (1, 1, 4, 4),
            [| [| CompositeCell.createUnitizedFromString "4" |] |],
            (fun nextTable -> updatedTable <- nextTable)
        )

        let value, unit = updatedTable.GetCellAt(3, 0).AsUnitized
        Expect.equal value "4" "The structured numeric value should be applied."
        Expect.equal unit.NameText "Degree Celsius" "The existing destination unit should be retained."
        Expect.equal unit.TermSourceREF (Some "UO") "The existing unit source should be retained."
        Expect.equal unit.TermAccessionNumber (Some "UO:000000001") "The existing unit accession should be retained."

    static member AnnotationTableUnitizedPasteIntoFreeTextPreservesVisibleValue() =
        let table = Fixture.mkTable ()
        let anchor: CellCoordinate = {| x = 1; y = 1 |}
        let mutable updatedTable = table

        AnnotationTableContextMenuUtil.applyStructuredCells (
            anchor,
            table,
            Fixture.mkSelectHandle (1, 1, 1, 1),
            [|
                [|
                    CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                |]
            |],
            (fun nextTable -> updatedTable <- nextTable)
        )

        Expect.equal
            (updatedTable.GetCellAt(0, 0).ToString())
            "4 metre"
            "Pasting a unitized cell into free text should retain its complete visible value."

    static member private ClipboardItem(plainText: string, cells: CompositeCell[][]) =
        let payload =
            cells
            |> Swate.Components.ClipboardCodec.createPayload
            |> Swate.Components.ClipboardCodec.encode

        let blob text =
            { new ClipboardBlob with
                member _.text() = promise { return text }
            }

        { new ClipboardItem with
            member _.types = [| Swate.Components.ClipboardCodec.MimeType; "text/plain" |]

            member _.getType mimeType = promise {
                return
                    if mimeType = Swate.Components.ClipboardCodec.MimeType then
                        blob payload
                    else
                        blob plainText
            }
        }

    static member StructuredPasteFallsBackForHeadersAndBlankBodies() = async {
        let table = Fixture.mkTable ()
        let originalClipboard = GlobalBindings.navigator.clipboard

        let runPaste coordinate cells plainText = async {
            let item = TestCases.ClipboardItem(plainText, cells)

            let clipboardMock =
                { new Clipboard with
                    member _.read() = promise { return [| item |] }
                    member _.readText() = promise { return plainText }
                    member _.write _ = promise { return () }
                    member _.writeText _ = promise { return () }
                }

            let mutable modal = None
            let mutable tableWasUpdated = false

            try
                setNavigatorClipboard GlobalBindings.navigator clipboardMock

                do!
                    AnnotationTableContextMenuUtil.tryPasteCopiedCells (
                        coordinate,
                        table,
                        Fixture.mkSelectHandle (coordinate.y, coordinate.y, coordinate.x, coordinate.x),
                        (fun nextModal -> modal <- nextModal),
                        (fun _ -> tableWasUpdated <- true)
                    )
                    |> Async.AwaitPromise

                return modal, tableWasUpdated
            finally
                setNavigatorClipboard GlobalBindings.navigator originalClipboard
        }

        let headerText = table.ToStringSeqs().[0].[0]

        let! headerModal, headerUpdated =
            runPaste {| x = 1; y = 0 |} [| [| CompositeCell.FreeText "structured" |] |] headerText

        match headerModal with
        | Some(AnnotationTable.ModalTypes.PasteCaseUserInput(PasteCases.AddColumns _, _)) -> ()
        | _ -> failwith "Structured clipboard content targeting a header should use the plain-text header flow."

        Expect.isFalse headerUpdated "Header fallback should not apply structured body cells."

        let! blankModal, blankUpdated = runPaste {| x = 1; y = 1 |} [| [| CompositeCell.FreeText "" |] |] ""

        match blankModal with
        | Some(AnnotationTable.ModalTypes.UnknownPasteCase(PasteCases.Unknown _)) -> ()
        | _ -> failwith "An all-blank structured body payload should use the Unknown flow."

        Expect.isFalse blankUpdated "An all-blank structured payload should not clear the target cell."
    }

    static member ClipboardWriteFallsBackFromTypedToHtml() = async {
        let originalClipboard = GlobalBindings.navigator.clipboard
        let originalClipboardItem = installClipboardItemMock ()
        let attempts = ResizeArray<string[]>()
        let mutable plainTextFallbackUsed = false

        let clipboardMock =
            { new Clipboard with
                member _.read() = promise { return [||] }
                member _.readText() = promise { return "" }

                member _.write items = promise {
                    attempts.Add(items.[0].types)

                    if attempts.Count = 1 then
                        return raise (System.Exception "custom MIME unsupported")
                }

                member _.writeText _ = promise { plainTextFallbackUsed <- true }
            }

        try
            setNavigatorClipboard GlobalBindings.navigator clipboardMock

            do!
                Swate.Components.ClipboardCodec.write "plain" (Some [| [| CompositeCell.FreeText "plain" |] |])
                |> Async.AwaitPromise

            Expect.equal attempts.Count 2 "Writing should retry after custom MIME writing fails."

            Expect.isTrue
                (attempts.[0] |> Array.contains Swate.Components.ClipboardCodec.MimeType)
                "The first write should contain the typed payload."

            Expect.isFalse
                (attempts.[1] |> Array.contains Swate.Components.ClipboardCodec.MimeType)
                "The HTML fallback should omit the unsupported custom MIME type."

            Expect.isTrue (attempts.[1] |> Array.contains "text/html") "The second write should retain HTML content."
            Expect.isFalse plainTextFallbackUsed "A successful HTML write should not fall back to writeText."

            let mutable finalPlainText = None

            let plainTextClipboardMock =
                { new Clipboard with
                    member _.read() = promise { return [||] }
                    member _.readText() = promise { return "" }
                    member _.write _ = promise { return raise (System.Exception "rich clipboard unsupported") }
                    member _.writeText value = promise { finalPlainText <- Some value }
                }

            setNavigatorClipboard GlobalBindings.navigator plainTextClipboardMock

            do!
                Swate.Components.ClipboardCodec.write "plain" (Some [| [| CompositeCell.FreeText "plain" |] |])
                |> Async.AwaitPromise

            Expect.equal finalPlainText (Some "plain") "Failed typed and HTML writes should fall back to plain text."
        finally
            setNavigatorClipboard GlobalBindings.navigator originalClipboard
            restoreClipboardItem originalClipboardItem
    }

    static member ClipboardCodecRoundTripsCompositeCells() =
        let cells = [|
            [|
                CompositeCell.createFreeText "plain"
                CompositeCell.createTermFromString ("term", "TST", "TST:1")
                CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
                CompositeCell.createDataFromString "file.txt#row=2"
            |]
        |]

        let decoded =
            cells
            |> Swate.Components.ClipboardCodec.createPayload
            |> Swate.Components.ClipboardCodec.encode
            |> Swate.Components.ClipboardCodec.tryDecode
            |> Option.get

        let actual =
            decoded.Rows
            |> Array.map (Array.map (Swate.Components.ClipboardCodec.toCompositeCell >> _.ToTabStr()))

        let expected = cells |> Array.map (Array.map _.ToTabStr())
        Expect.equal actual expected "The typed clipboard codec should round-trip every composite cell kind."

        Expect.isNone
            (Swate.Components.ClipboardCodec.tryDecode """{"Version":1,"Rows":[[null]]}""")
            "A payload containing a null cell should be rejected."

        Expect.isNone
            (Swate.Components.ClipboardCodec.tryDecode
                """{"Version":1,"Rows":[[{"Kind":"term","Name":"incomplete"}]]}""")
            "A payload with missing cell fields should be rejected."

        Expect.isNone
            (Swate.Components.ClipboardCodec.tryDecode
                """{"Version":1,"Rows":[[{"Kind":"unknown","Value":"","Name":"","TermSourceRef":"","TermAccessionNumber":"","Selector":"","Format":"","SelectorFormat":""}]]}""")
            "A payload with an unknown cell kind should be rejected."

        Expect.isNone
            (Swate.Components.ClipboardCodec.tryDecode
                """{"Version":1,"Rows":[[{"Kind":"freetext","Value":"A","Name":"","TermSourceRef":"","TermAccessionNumber":"","Selector":"","Format":"","SelectorFormat":""}],[],[{"Kind":"freetext","Value":"B","Name":"","TermSourceRef":"","TermAccessionNumber":"","Selector":"","Format":"","SelectorFormat":""}]]}""")
            "A payload containing an empty row should be rejected."

    static member TableCopyIncludesSelector() =
        let dataCell = CompositeCell.createDataFromString "DatamapTesting.txt#row=2"

        Expect.equal
            (dataCell.ToClipboardStr())
            "DatamapTesting.txt#row=2"
            "Copied table data should include its selector in the displayed clipboard cell."

        Expect.equal
            (CompositeCell.ToClipboardTableTxt [| [| dataCell; CompositeCell.FreeText "label" |] |])
            "DatamapTesting.txt#row=2\tlabel"
            "A table data cell and its selector must occupy one clipboard cell."

    static member DataMapCellPastePreservesSelector() =
        let dataMap = Datamap(ResizeArray [ DataContext() ])

        let anchor: CellCoordinate = {| x = 1; y = 1 |}
        dataMap.PasteTabText(anchor, [| anchor |], "DatamapTesting.txt#row=2")

        Expect.equal dataMap.DataContexts.[0].FilePath (Some "DatamapTesting.txt") "The path should be pasted."
        Expect.equal dataMap.DataContexts.[0].Selector (Some "row=2") "The selector should be pasted."

    static member DataMapLabelSurvivesArcFileRefresh() =
        let assay = ArcAssay.init "assay"
        assay.Datamap <- Some(Datamap(ResizeArray [ DataContext(label = "asdhjasklhd") ]))

        let refreshed =
            Swate.Components.Shared.ARCtrlHelper.ArcFiles.refreshRef (
                Swate.Components.Shared.ARCtrlHelper.ArcFiles.Assay assay
            )

        let label = refreshed.TryGetDataMap().Value.DataContexts.[0].Label

        Expect.equal label (Some "asdhjasklhd") "Refreshing an ARC file must preserve DataMap labels."

    static member DataMapGridPasteGrowsRows() =
        let dataMap = Datamap(ResizeArray [ DataContext() ])

        let anchor: CellCoordinate = {| x = 1; y = 1 |}

        dataMap.PasteTabText(anchor, [| anchor |], "first.txt#row=2\tFirst\nsecond.txt#row=3\tSecond")

        Expect.equal dataMap.DataContexts.Count 2 "The DataMap should grow for additional clipboard rows."
        Expect.equal dataMap.DataContexts.[1].FilePath (Some "second.txt") "The second path should be pasted."
        Expect.equal dataMap.DataContexts.[1].Selector (Some "row=3") "The second selector should be pasted."
        Expect.equal dataMap.DataContexts.[1].Label (Some "Second") "The second label should be pasted."

    static member DataMapFillColumnCopiesCompleteCell() =
        let dataMap =
            Datamap(
                ResizeArray [
                    DataContext(name = "first.txt#row=2", format = "text/csv")
                    DataContext(name = "second.txt#row=3")
                ]
            )

        Swate.Components.Composite.Table.Helper.fillColumn
            dataMap.RowCount
            {| x = 1; y = 1 |}
            (fun coordinate -> dataMap.GetCell(coordinate.x - 1, coordinate.y - 1))
            _.Copy()
            (fun coordinate cell -> dataMap.SetCell(coordinate.x - 1, coordinate.y - 1, cell))

        Expect.equal dataMap.DataContexts.[1].FilePath (Some "first.txt") "The complete source path should be copied."
        Expect.equal dataMap.DataContexts.[1].Selector (Some "row=2") "The complete source selector should be copied."
        Expect.equal dataMap.DataContexts.[1].Format (Some "text/csv") "The complete source format should be copied."

    static member DataMapRowAndClearActions() =
        let dataMap =
            Datamap(
                ResizeArray [
                    DataContext(name = "first.txt#row=2", label = "First")
                    DataContext(name = "second.txt#row=3", label = "Second")
                ]
            )

        dataMap.ClearCells [ {| x = 2; y = 1 |} ]
        Expect.equal dataMap.DataContexts.[0].Label None "Clear should affect only the selected cell."

        for rowIndex in 0 .. dataMap.RowCount - 1 do
            dataMap.Clear(0, rowIndex)

        Expect.equal dataMap.DataContexts.[0].FilePath None "Clear Column should clear the first row."
        Expect.equal dataMap.DataContexts.[1].FilePath None "Clear Column should clear the second row."

        ([ {| x = 1; y = 1 |} ]: CellCoordinate list)
        |> Swate.Components.Composite.Table.Helper.selectedRowIndices dataMap.RowCount
        |> Array.iter dataMap.DataContexts.RemoveAt

        Expect.equal dataMap.DataContexts.Count 1 "Only the unselected row should remain."

    static member HeaderMoveColumnUsesSelectedHeaderIndex() =
        let table = Fixture.mkTable ()
        let selectHandle = TestCases.NoSelectionHandle()
        let mutable openedModal: AnnotationTable.ModalTypes option = None

        let menuItems =
            AnnotationTableContextMenu.CompositeHeaderContent(
                1,
                table,
                ignore,
                selectHandle,
                (fun modal -> openedModal <- modal)
            )

        TestCases.TriggerMenuItem menuItems.[7] {| x = 1; y = 0 |}

        match openedModal with
        | Some(AnnotationTable.ModalTypes.MoveColumn(_, arcTableIndex)) ->
            Expect.equal arcTableIndex.x 1 "Move column should target the first header column (1-based UI index)"
            Expect.equal arcTableIndex.y 0 "Move column target should stay on header row"
        | _ -> failwith "Move column menu entry should open move-column modal"

    static member ClipboardIndexWrapsAndRejectsInvalidLengths() =
        Expect.equal (AnnotationTableContextMenuUtil.getIndex (7, 3)) 1 "Clipboard indices should wrap using modulo."

        Expect.throws
            (fun () -> AnnotationTableContextMenuUtil.getIndex (0, 0) |> ignore)
            "An empty clipboard range should be rejected instead of looping forever."

    static member ClipboardCaptureAndMappingPreserveGeometry() =
        let source =
            Swate.Components.ClipboardContract.Contract.Capture.matrix
                (fun coordinate -> CompositeCell.FreeText $"{coordinate.x},{coordinate.y}")
                [
                    {| x = 2; y = 2 |}
                    {| x = 1; y = 1 |}
                    {| x = 2; y = 1 |}
                    {| x = 1; y = 2 |}
                ]

        Expect.equal
            (source |> Array.map (Array.map _.ToString()))
            [| [| "1,1"; "2,1" |]; [| "1,2"; "2,2" |] |]
            "Capture should retain a two-dimensional selection."

        let anchor: CellCoordinate = {| x = 3; y = 4 |}

        let selection: CellCoordinate[] = [|
            {| x = 3; y = 4 |}
            {| x = 4; y = 4 |}
            {| x = 5; y = 4 |}
            {| x = 3; y = 5 |}
            {| x = 4; y = 5 |}
            {| x = 5; y = 5 |}
        |]

        let mapped =
            Swate.Components.ClipboardContract.Contract.Mapping.map source anchor selection

        Expect.equal
            (mapped |> Array.map (fun item -> item.Target, item.Source.ToString()))
            [|
                {| x = 3; y = 4 |}, "1,1"
                {| x = 4; y = 4 |}, "2,1"
                {| x = 5; y = 4 |}, "1,1"
                {| x = 3; y = 5 |}, "1,2"
                {| x = 4; y = 5 |}, "2,2"
                {| x = 5; y = 5 |}, "1,2"
            |]
            "The shared mapper should wrap the source matrix across the full selection."

        let twoByTwoSelection: CellCoordinate[] = [|
            {| x = 3; y = 4 |}
            {| x = 4; y = 4 |}
            {| x = 3; y = 5 |}
            {| x = 4; y = 5 |}
        |]

        let bottomRightAnchor: CellCoordinate = {| x = 4; y = 5 |}

        let mappedFromBottomRight =
            Swate.Components.ClipboardContract.Contract.Mapping.map source bottomRightAnchor twoByTwoSelection

        Expect.equal
            (mappedFromBottomRight |> Array.map (fun item -> item.Source.ToString()))
            [| "1,1"; "2,1"; "1,2"; "2,2" |]
            "A clicked cell inside a multi-cell target must not rotate the source relative to the selection."

    static member DataMapStructuredTermRulesAreExplicit() =
        let unit = CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")

        let dataMap =
            Datamap(
                ResizeArray [
                    DataContext(
                        explication = OntologyAnnotation("old", "OLD", "OLD:1"),
                        unit = OntologyAnnotation("old", "OLD", "OLD:1"),
                        objectType = OntologyAnnotation("old", "OLD", "OLD:1")
                    )
                ]
            )

        let anchor: CellCoordinate = {| x = 5; y = 1 |}
        dataMap.PasteStructuredCells(anchor, [| anchor |], [| [| unit; unit; unit |] |])

        for term in
            [
                dataMap.DataContexts.[0].Explication
                dataMap.DataContexts.[0].Unit
                dataMap.DataContexts.[0].ObjectType
            ] do
            Expect.equal term.Value.NameText "metre" "Every DataMap term column should use the unit ontology term."
            Expect.equal term.Value.TermAccessionNumber (Some "UO:0000008") "Unit metadata should be retained."

        dataMap.PasteStructuredCells(anchor, [| anchor |], [| [| CompositeCell.FreeText "replacement" |] |])
        let replacement = dataMap.DataContexts.[0].Explication.Value
        Expect.equal replacement.NameText "replacement" "Structured text should replace the visible term name."

        Expect.equal
            replacement.TermSourceREF
            None
            "Unrelated destination ontology identity must not survive replacement."

        let data = CompositeCell.createDataFromString "file.txt#row=2"
        dataMap.PasteStructuredCells(anchor, [| anchor |], [| [| data |] |])
        let dataReplacement = dataMap.DataContexts.[0].Explication.Value

        Expect.equal
            dataReplacement.NameText
            "file.txt#row=2"
            "Structured data should use its visible value in a term column."

        Expect.equal
            dataReplacement.TermAccessionNumber
            None
            "Structured data must not retain the destination ontology accession."

        Expect.equal
            dataReplacement.TermSourceREF
            None
            "Structured data must not retain the destination ontology source."

    static member ClipboardRejectsMalformedFieldTypes() =
        Expect.isNone
            (Swate.Components.ClipboardCodec.tryDecode
                """{"Version":1,"Rows":[[{"Kind":"freetext","Value":42,"Name":"","TermSourceRef":"","TermAccessionNumber":"","Selector":"","Format":"","SelectorFormat":""}]]}""")
            "Structured DTO fields with non-string runtime values must be rejected."

    static member CutKeepsSourceWhenClipboardWriteFails() = async {
        let table = ArcTable.init "Cut failure"
        table.AddColumn(CompositeHeader.FreeText "Value", ResizeArray [ CompositeCell.FreeText "keep me" ])

        let originalClipboard = GlobalBindings.navigator.clipboard
        let mutable currentTable = table

        let clipboardMock =
            { new Clipboard with
                member _.read() = promise { return [||] }
                member _.readText() = promise { return "" }
                member _.write _ = promise { return raise (System.Exception "write failed") }
                member _.writeText _ = promise { return raise (System.Exception "write failed") }
            }

        try
            setNavigatorClipboard GlobalBindings.navigator clipboardMock

            try
                do!
                    AnnotationTableContextMenuUtil.cut (
                        {| x = 1; y = 1 |},
                        table,
                        (fun nextTable -> currentTable <- nextTable),
                        TestCases.NoSelectionHandle()
                    )
                    |> Async.AwaitPromise

                failwith "The rejected clipboard write should propagate."
            with _ ->
                Expect.equal
                    (currentTable.GetCellAt(0, 0).ToString())
                    "keep me"
                    "A failed clipboard write must not clear the active AnnotationTable source."
        finally
            setNavigatorClipboard GlobalBindings.navigator originalClipboard
    }

    static member CutClearsSourceAfterClipboardWriteSucceeds() = async {
        let table = ArcTable.init "Cut success"
        table.AddColumn(CompositeHeader.FreeText "Value", ResizeArray [ CompositeCell.FreeText "clear me" ])

        let originalClipboard = GlobalBindings.navigator.clipboard
        let mutable currentTable = table
        let mutable writeCompleted = false

        let clipboardMock =
            { new Clipboard with
                member _.read() = promise { return [||] }
                member _.readText() = promise { return "" }
                member _.write _ = promise { writeCompleted <- true }
                member _.writeText _ = promise { writeCompleted <- true }
            }

        try
            setNavigatorClipboard GlobalBindings.navigator clipboardMock

            do!
                AnnotationTableContextMenuUtil.cut (
                    {| x = 1; y = 1 |},
                    table,
                    (fun nextTable -> currentTable <- nextTable),
                    TestCases.NoSelectionHandle()
                )
                |> Async.AwaitPromise

            Expect.isTrue writeCompleted "Clipboard writing must finish before cut completes."
            Expect.equal (currentTable.GetCellAt(0, 0).ToString()) "" "A successful cut should clear the source."
        finally
            setNavigatorClipboard GlobalBindings.navigator originalClipboard
    }

let Main =

    testList "Context Menu" [
        testList "Prediction" [
            testCase "Add term column"
            <| fun _ ->
                TestCases.AddColumns(
                    Fixture.mkSelectHandle (1, 1, 3, 3),
                    Fixture.Component_Term_InstrumentModel_String,
                    [| Fixture.Component_InstrumentModel_Term_Column |]
                )
            testCase "Add unit column"
            <| fun _ ->
                TestCases.AddColumns(
                    Fixture.mkSelectHandle (1, 1, 3, 3),
                    Fixture.Component_Unit_InstrumentModel_String,
                    [| Fixture.Component_InstrumentModel_Unit_Column |]
                )
            testCase "Add unit - term column"
            <| fun _ ->
                TestCases.AddColumns(
                    Fixture.mkSelectHandle (1, 1, 3, 4),
                    Fixture.Component_Unit_InstrumentModel_Unit_Term_String,
                    Fixture.Component_Unit_InstrumentModel_Unit_Term_Columns
                )
            testCase "Paste single Cell"
            <| fun _ ->
                TestCases.AddSingleCell(
                    Fixture.Body_Component_InstrumentModel_Pseudo_SingleRow_String,
                    [|
                        Fixture.Body_Component_InstrumentModel_Pseudo_SingleRow_Column
                    |]
                )
            testCase $"Paste {1} Cell(s) in the same row. Paste {2} Cell(s) in the same column"
            <| fun _ ->
                TestCases.PasteMultipleCells(
                    Fixture.mkSelectHandle (1, 2, 1, 1),
                    Fixture.Body_Component_InstrumentModel_TwoRows_Term_Strings,
                    [|
                        Fixture.Body_Component_InstrumentModel_TwoRows_Term_Column
                    |]
                )
            testCase $"Paste {1} Cell(s) in the same row. Paste {3} Cell(s) in the same column"
            <| fun _ ->
                TestCases.PasteMultipleCells(
                    Fixture.mkSelectHandle (1, 3, 1, 1),
                    Fixture.Body_Component_InstrumentModel_ThreeRows_Term_Strings,
                    [|
                        Fixture.Body_Component_InstrumentModel_ThreeRows_Term_Column
                    |]
                )
            testCase $"Paste {2} Cell(s) in the same row. Paste {1} Cell(s) in the same column"
            <| fun _ ->
                TestCases.PasteMultipleCells(
                    Fixture.mkSelectHandle (1, 1, 1, 2),
                    Fixture.Body_Component_InstrumentModel_TwoColumns_Term_Strings,
                    Fixture.Body_Component_InstrumentModel_TwoColumns_Term_Columns
                )
            testCase $"Paste {2} Cell(s) in the same row. Paste {2} Cell(s) in the same column"
            <| fun _ ->
                TestCases.PasteMultipleCells(
                    Fixture.mkSelectHandle (1, 2, 1, 2),
                    Fixture.Body_Component_InstrumentModel_TwoRowsColumns_Term_Strings,
                    Fixture.Body_Component_InstrumentModel_TwoRowsColumns_Term_Columns
                )
            testCase $"Add fitting Term"
            <| fun _ ->
                TestCases.AddFittingTerm(
                    Fixture.mkSelectHandle (1, 1, 3, 3),
                    Fixture.Body_Component_InstrumentModel_SingleRow_Term_String,
                    [| Fixture.Component_InstrumentModel_Term_Body |]
                )
            testCase $"Add 1 Freetext and 1 Term"
            <| fun _ ->
                TestCases.AddFittingTerm(
                    Fixture.mkSelectHandle (1, 1, 2, 3),
                    Fixture.Body_Component_InstrumentModel_SingleRow_1Freetext_1_Term_Strings,
                    Fixture.Body_Component_InstrumentModel_SingleRow_1Freetext_1_Term_Columns
                )
            testCase $"Add unit value"
            <| fun _ ->
                TestCases.AddFittingTerm(
                    Fixture.mkSelectHandle (1, 1, 3, 3),
                    Fixture.Body_Integer,
                    [| Fixture.Body_Integer_Column |]
                )
            testCase $"Add unknown value"
            <| fun _ -> TestCases.AddUnknownPattern([| [| "" |] |])
        ]
        testList "Regression" [
            testCase "Paste uses clicked column rather than stale selection"
            <| fun _ -> TestCases.PasteIntoClickedColumnOutsideSelection()
            testCase "DataMap cell copy includes the selector behind #"
            <| fun _ -> TestCases.DataMapCopyIncludesSelector()
            testCase "DataMap term copy does not shift following columns"
            <| fun _ -> TestCases.DataMapCopyKeepsTermsInGridColumns()
            testCase "DataMap plain-text paste keeps TSV cells separate"
            <| fun _ -> TestCases.DataMapPlainTextPasteKeepsTsvCellsSeparate()
            testCase "DataMap structured paste preserves terms and units"
            <| fun _ -> TestCases.DataMapStructuredPastePreservesTermsAndUnits()
            testCase "DataMap unitized paste into free text preserves the visible value"
            <| fun _ -> TestCases.DataMapUnitizedPasteIntoFreeTextPreservesVisibleValue()
            testCase "Plain-text selection geometry matches across targets"
            <| fun _ -> TestCases.PlainTextSelectionGeometryMatchesAcrossTargets()
            testCase "Plain text preserves empty rows across targets"
            <| fun _ -> TestCases.PlainTextPreservesEmptyRowsAcrossTargets()
            testCase "Plain-text parser ignores terminal separators and preserves internal empty rows"
            <| fun _ -> TestCases.PlainTextParserIgnoresTerminalSeparators()
            testCase "AnnotationTable plain numeric paste preserves the target unit"
            <| fun _ -> TestCases.AnnotationTablePlainNumericPastePreservesTargetUnit()
            testCase "AnnotationTable structured overflow clips columns and extends rows"
            <| fun _ -> TestCases.AnnotationTableStructuredOverflowClipsColumnsAndExtendsRows()
            testCase "AnnotationTable structured paste preserves term and unit metadata"
            <| fun _ -> TestCases.AnnotationTableStructuredPastePreservesOntologyMetadata()
            testCase "AnnotationTable structured unitless values preserve the target unit"
            <| fun _ -> TestCases.AnnotationTableStructuredUnitlessValuePreservesTargetUnit()
            testCase "AnnotationTable unitized paste into free text preserves the visible value"
            <| fun _ -> TestCases.AnnotationTableUnitizedPasteIntoFreeTextPreservesVisibleValue()
            testCaseAsync "Structured paste falls back for headers and blank bodies"
            <| TestCases.StructuredPasteFallsBackForHeadersAndBlankBodies()
            testCaseAsync "Clipboard write falls back from typed content to HTML"
            <| TestCases.ClipboardWriteFallsBackFromTypedToHtml()
            testCase "Typed clipboard codec round-trips composite cells"
            <| fun _ -> TestCases.ClipboardCodecRoundTripsCompositeCells()
            testCase "Table cell copy includes the selector behind #"
            <| fun _ -> TestCases.TableCopyIncludesSelector()
            testCase "DataMap cell paste includes the selector behind #"
            <| fun _ -> TestCases.DataMapCellPastePreservesSelector()
            testCase "DataMap labels survive ARC file refreshes"
            <| fun _ -> TestCases.DataMapLabelSurvivesArcFileRefresh()
            testCase "DataMap grid paste grows rows"
            <| fun _ -> TestCases.DataMapGridPasteGrowsRows()
            testCase "DataMap fill column copies the complete cell"
            <| fun _ -> TestCases.DataMapFillColumnCopiesCompleteCell()
            testCase "DataMap delete-row and clear actions update the selected targets"
            <| fun _ -> TestCases.DataMapRowAndClearActions()
            testCase "Data main-field paste preserves the selector behind #"
            <| fun _ -> TestCases.DataMainFieldPreservesSelector()
            testCase "Header delete targets first column correctly"
            <| fun _ -> TestCases.HeaderDeleteFirstColumn()
            testCase "Index delete targets first row correctly"
            <| fun _ -> TestCases.IndexDeleteFirstRow()
            testCase "Header move column keeps 1-based header index"
            <| fun _ -> TestCases.HeaderMoveColumnUsesSelectedHeaderIndex()
            testCase "Clipboard index wraps and rejects invalid lengths"
            <| fun _ -> TestCases.ClipboardIndexWrapsAndRejectsInvalidLengths()
            testCase "Clipboard capture and mapping preserve matrix geometry"
            <| fun _ -> TestCases.ClipboardCaptureAndMappingPreserveGeometry()
            testCase "DataMap structured term conversions are target-owned"
            <| fun _ -> TestCases.DataMapStructuredTermRulesAreExplicit()
            testCase "Clipboard rejects malformed structured field types"
            <| fun _ -> TestCases.ClipboardRejectsMalformedFieldTypes()
            testCaseAsync "Cut keeps source cells when clipboard writing fails"
            <| TestCases.CutKeepsSourceWhenClipboardWriteFails()
            testCaseAsync "Cut clears source cells after clipboard writing succeeds"
            <| TestCases.CutClearsSourceAfterClipboardWriteSucceeds()
        ]
    ]
