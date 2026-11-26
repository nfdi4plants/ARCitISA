module Components.Tests.Table.KeyboardNavigation

open Fable.Mocha
open Arcitisa.Components

open global.Arcitisa.Components.Composite.Table
open global.Arcitisa.Components.Composite.Table.GridSelect


[<Literal>]
let private DEFAULT_MAX = 10

let private DEFAULT_START_POINT: CellCoordinate = {| x = 5; y = 5 |}

let private tests_single =
    testList "Single" [

        test "Single down" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(Kbd.ArrowDown, false, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 5; y = 6 |} |> Some

            Expect.equal selectedCells expected ""
        }

        test "Single Up" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowUp, false, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 5; y = 4 |} |> Some

            Expect.equal selectedCells expected ""
        }

        test "Single left" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowLeft, false, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 4; y = 5 |} |> Some

            Expect.equal selectedCells expected ""
        }

        test "Single right" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowRight, false, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 6; y = 5 |} |> Some

            Expect.equal selectedCells expected ""
        }
    ]

let private tests_singleJump =
    testList "Single jump" [
        test "jump down" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowDown, true, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 5; y = DEFAULT_MAX |} |> Some

            Expect.equal selectedCells expected ""
        }

        test "jump up" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowUp, true, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 5; y = 0 |} |> Some

            Expect.equal selectedCells expected ""
        }

        test "jump left" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowLeft, true, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 0; y = 5 |} |> Some

            Expect.equal selectedCells expected ""
        }

        test "jump right" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowRight, true, false, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = DEFAULT_MAX; y = 5 |} |> Some

            Expect.equal selectedCells expected ""
        }
    ]

let private tests_appendSingles =
    testList "Append singles" [
        test "Append down" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowDown, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let expected = SelectedCellRange.make 5 5 5 10 |> Some
            let actual = selectedCells
            Expect.equal actual expected ""
        }

        test "Append up" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowUp, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 5 0 5 5 |> Some
            Expect.equal actual expected ""
        }

        test "Append left" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowLeft, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 0 5 5 5 |> Some
            Expect.equal actual expected ""
        }

        test "Append right" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 10 5 |> Some
            Expect.equal actual expected ""
        }
    ]

let private tests_appendMultiple =
    testList "appendMultiple" [
        test "append down" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            for _ in 1..appendCount do
                nav.SelectBy(ArrowDown, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 6 10 |> Some
            Expect.equal actual expected ""
        }

        test "append up" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            for _ in 1..appendCount do
                nav.SelectBy(ArrowUp, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 5 0 6 5 |> Some
            Expect.equal actual expected ""
        }

        test "append left" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowDown, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            for _ in 1..appendCount do
                nav.SelectBy(ArrowLeft, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 0 5 5 6 |> Some
            Expect.equal actual expected ""
        }

        test "append right" {
            let appendCount = 5

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowDown, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            for _ in 1..appendCount do
                nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 10 6 |> Some
            Expect.equal actual expected ""
        }
    ]

let private tests_appendJump =
    testList "Append jump" [
        test "append jump down" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowDown, true, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 5 DEFAULT_MAX |> Some
            Expect.equal actual expected ""
        }
        test "append right then jump down" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            nav.SelectBy(ArrowDown, true, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 6 DEFAULT_MAX |> Some
            Expect.equal actual expected ""
        }
        test "append jump up" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowUp, true, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 0 5 5 |> Some
            Expect.equal actual expected ""
        }

        test "append jump left" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowLeft, true, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 0 5 5 5 |> Some
            Expect.equal actual expected ""
        }

        test "append jump right" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectBy(ArrowRight, true, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 DEFAULT_MAX 5 |> Some
            Expect.equal actual expected ""
        }
    ]

let private tests_selectAt =
    testList "Select At" [
        test "select at append" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectAt({| x = 6; y = 6 |}, true, selectedCells, setter)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 6 6 |> Some
            Expect.equal actual expected ""
        }
        test "select at" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectAt({| x = 6; y = 6 |}, false, selectedCells, setter)
            let actual = selectedCells

            let expected: CellCoordinateRange option =
                SelectedCellRange.singleton {| x = 6; y = 6 |} |> Some

            Expect.equal actual expected ""
        }

        test "select at append up left" {
            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()
            nav.SelectAt({| x = 4; y = 4 |}, true, selectedCells, setter)
            let actual = selectedCells
            let expected = SelectedCellRange.make 4 4 5 5 |> Some
            Expect.equal actual expected ""
        }
    ]

let private tests_appendReverse =
    testList "append reverse" [
        test "append up then down" {
            let appendCount = 3

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowUp, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            nav.SelectBy(ArrowDown, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 3 5 5 |> Some
            Expect.equal actual expected ""
        }

        test "append down then up" {
            let appendCount = 3

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowDown, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            nav.SelectBy(ArrowUp, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 5 7 |> Some
            Expect.equal actual expected ""
        }

        test "append left then right" {
            let appendCount = 3

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowLeft, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 3 5 5 5 |> Some
            Expect.equal actual expected ""
        }

        test "append right then left" {
            let appendCount = 3

            let mutable selectedCells =
                Set.singleton DEFAULT_START_POINT |> SelectedCellRange.fromSet

            let setter cells = selectedCells <- cells
            let nav = GridSelect()

            for _ in 1..appendCount do
                nav.SelectBy(ArrowRight, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)

            nav.SelectBy(ArrowLeft, false, true, selectedCells, setter, DEFAULT_MAX, DEFAULT_MAX)
            let actual = selectedCells
            let expected = SelectedCellRange.make 5 5 7 5 |> Some
            Expect.equal actual expected ""
        }
    ]

let Main =
    testList "KeyboardNavigation" [
        tests_single
        tests_singleJump
        tests_appendSingles
        tests_appendMultiple
        tests_appendJump
        tests_selectAt
        tests_appendReverse
    ]
