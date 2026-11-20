module internal Swate.Components.Page.ArcFileEditor.SearchTests

open ARCtrl
open Vitest
open Swate.Components.Page.ArcFileEditor.Types

Vitest.test (
    "search matches displayed string values once per cell in row order, ignoring case",
    fun () ->
        let table = ArcTable.init "Search"

        table.AddColumn(
            CompositeHeader.FreeText "Text",
            ResizeArray [
                CompositeCell.FreeText "match MATCH"
                CompositeCell.FreeText "other"
            ]
        )

        table.AddColumn(
            CompositeHeader.Parameter(OntologyAnnotation("Term")),
            ResizeArray [
                CompositeCell.Term(OntologyAnnotation("Match term", "HIDDEN", "HIDDEN:123"))
                CompositeCell.Unitized("21", OntologyAnnotation("Match unit"))
            ]
        )

        table.AddColumn(
            CompositeHeader.Output IOType.Data,
            ResizeArray [
                CompositeCell.Data(Data(name = "Match.csv"))
                CompositeCell.emptyData
            ]
        )

        let target = Some(AddRowsTarget.Table table)

        Vitest.expect(Helper.findSearchMatches target "mAtCh").toEqual [|
            {| x = 1; y = 1 |}
            {| x = 2; y = 1 |}
            {| x = 3; y = 1 |}
            {| x = 2; y = 2 |}
        |]

        Vitest.expect(Helper.findSearchMatches target "21 Match").toEqual [| {| x = 2; y = 2 |} |]
        Vitest.expect(Helper.findSearchMatches target "HIDDEN").toEqual [||]
)

Vitest.test (
    "blank queries, missing targets and empty tables have no matches",
    fun () ->
        let table = ArcTable.init "Empty"
        table.AddColumn(CompositeHeader.FreeText "Header")
        let target = Some(AddRowsTarget.Table table)
        Vitest.expect(Helper.findSearchMatches target "Header").toEqual [||]
        Vitest.expect(Helper.findSearchMatches target "").toEqual [||]
        Vitest.expect(Helper.findSearchMatches target "  ").toEqual [||]
        Vitest.expect(Helper.findSearchMatches None "anything").toEqual [||]
)

Vitest.test (
    "DataMap search uses the same displayed string values and UI coordinates",
    fun () ->
        let dataMap =
            Datamap(
                ResizeArray [
                    DataContext(name = "Sample A")
                    DataContext(name = "Sample B")
                ]
            )

        Vitest.expect(Helper.findSearchMatches (Some(AddRowsTarget.DataMap dataMap)) "sample").toEqual [|
            {| x = 1; y = 1 |}
            {| x = 1; y = 2 |}
        |]
)
