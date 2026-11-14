module internal Swate.Components.Composite.DataMapTable.DataMapContextMenuTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Vitest
open Swate.Components

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockImplementation(function () { return $1 === 'offsetWidth' && this.hasAttribute('data-column') ? 120 : $2; })")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

let private renderDataMap dataMap =
    mockGeometry Vitest.vi "offsetWidth" 800
    mockGeometry Vitest.vi "offsetHeight" 600

    let updated = ref None

    RTL.render (DataMapTable.DataMapTable(dataMap, (fun next -> updated.Value <- Some next)))
    |> ignore

    dataMap, updated

let private renderTable () =
    Datamap(
        ResizeArray [
            for i in 1..3 -> DataContext(name = $"data-{i}.csv", label = $"Label {i}")
        ]
    )
    |> renderDataMap

let private cellAt column row =
    document.querySelector ($"[data-row='{row}'][data-column='{column}'] > div") :?> HTMLElement

let private cell row = cellAt 1 row

let private openMenuAt column row =
    RTL.fireEvent.custom (
        "contextMenu",
        cellAt column row,
        {|
            clientX = 40
            clientY = 40
            bubbles = true
        |}
    )

let private openMenu row = openMenuAt 1 row

let private clickMenuItem (name: string) = promise {
    let! item = RTL.screen.findByText name
    RTL.fireEvent.click item
}

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

Vitest.test (
    "Duplicate Row targets the clicked DataMap row outside an existing selection",
    fun () -> promise {
        let dataMap, updated = renderTable ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2)).toBe false)
        RTL.fireEvent.click (cell 1)
        openMenu 2
        let! duplicate = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Duplicate Row"))
        RTL.fireEvent.click duplicate
        Vitest.expect(updated.Value.Value.RowCount).toBe 4
        Vitest.expect(updated.Value.Value.DataContexts.[2].Label).toEqual (Some "Label 2")
        Vitest.expect(updated.Value.Value.DataContexts.[3].Label).toEqual (Some "Label 3")
        Vitest.expect(dataMap.RowCount).toBe 3
    }
)

Vitest.test (
    "Duplicate Selected Rows duplicates each selected DataMap row once",
    fun () -> promise {
        let dataMap, updated = renderTable ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2)).toBe false)
        RTL.fireEvent.click (cell 1)
        RTL.fireEvent.click (cell 2, {| shiftKey = true |})
        openMenu 1
        let! duplicate = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Duplicate Selected Rows"))
        RTL.fireEvent.click duplicate

        Vitest.expect(updated.Value.Value.DataContexts |> Seq.map _.Label |> Seq.toArray).toEqual [|
            Some "Label 1"
            Some "Label 1"
            Some "Label 2"
            Some "Label 2"
            Some "Label 3"
        |]

        Vitest.expect(dataMap.RowCount).toBe 3
    }
)

Vitest.test (
    "Fill Column copies the clicked DataMap cell including selector and format",
    fun () -> promise {
        let dataMap, updated =
            Datamap(
                ResizeArray [
                    DataContext(name = "first.txt#row=2", format = "text/csv", selectorFormat = "text/csv")
                    DataContext(name = "second.txt#row=3")
                ]
            )
            |> renderDataMap

        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2)).toBe false)
        RTL.fireEvent.click (cell 2)
        openMenu 1
        do! clickMenuItem "Fill Column"
        let result = updated.Value.Value.DataContexts.[1]
        Vitest.expect(result.FilePath).toEqual (Some "first.txt")
        Vitest.expect(result.Selector).toEqual (Some "row=2")
        Vitest.expect(result.Format).toEqual (Some "text/csv")
        Vitest.expect(result.SelectorFormat).toEqual (Some "text/csv")
        Vitest.expect(dataMap.DataContexts.[1].FilePath).toEqual (Some "second.txt")
    }
)

for action in [ "Clear"; "Clear Column" ] do
    Vitest.test (
        $"{action} updates only the targeted DataMap cells",
        fun () -> promise {
            let dataMap, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cellAt 2 2)).toBe false)
            RTL.fireEvent.click (cellAt 2 1)
            openMenuAt 2 2
            do! clickMenuItem action
            let result = updated.Value.Value
            Vitest.expect(result.DataContexts.[1].Label).toEqual None
            Vitest.expect(result.DataContexts.[0].Label).toEqual (if action = "Clear" then Some "Label 1" else None)
            Vitest.expect(result.DataContexts.[2].Label).toEqual (if action = "Clear" then Some "Label 3" else None)
            Vitest.expect(result.DataContexts.[1].FilePath).toEqual (Some "data-2.csv")
            Vitest.expect(dataMap.DataContexts.[1].Label).toEqual (Some "Label 2")
        }
    )

for selectedRows in [ false; true ] do
    Vitest.test (
        (if selectedRows then
             "Delete Selected Rows removes each selected DataMap row once"
         else
             "Delete Row targets the clicked DataMap row outside the selection"),
        fun () -> promise {
            let dataMap, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2)).toBe false)
            RTL.fireEvent.click (cell 1)

            if selectedRows then
                RTL.fireEvent.click (cellAt 2 2, {| shiftKey = true |})
                openMenu 1
            else
                openMenu 2

            do!
                clickMenuItem (
                    if selectedRows then
                        "Delete Selected Rows"
                    else
                        "Delete Row"
                )

            Vitest
                .expect(updated.Value.Value.DataContexts |> Seq.map _.Label |> Seq.toArray)
                .toEqual (
                    if selectedRows then
                        [| Some "Label 3" |]
                    else
                        [| Some "Label 1"; Some "Label 3" |]
                )

            Vitest.expect(dataMap.RowCount).toBe 3
        }
    )

for name, firstColumn, lastColumn, expected in
    [
        "data selector", 1, 1, "data.csv#row=2"
        "term columns", 5, 6, "explicit\tmetre"
    ] do
    Vitest.test (
        $"Copy serializes DataMap {name} through the rendered menu",
        fun () -> promise {
            let written = ref [||]

            let clipboard =
                { new Clipboard with
                    member _.read() = promise { return [||] }
                    member _.readText() = promise { return "" }
                    member _.write items = promise { written.Value <- items }
                    member _.writeText _ = promise { return failwith "Expected typed clipboard copy" }
                }

            let restore = ClipboardTestHelpers.install clipboard
            let restoreConstructor = ClipboardTestHelpers.installItemConstructor ()

            try
                let _, updated =
                    Datamap(
                        ResizeArray [
                            DataContext(
                                name = "data.csv#row=2",
                                explication = OntologyAnnotation("explicit", "TST", "TST:1"),
                                unit = OntologyAnnotation("metre", "UO", "UO:0000008")
                            )
                        ]
                    )
                    |> renderDataMap

                do! RTL.waitFor (fun () -> Vitest.expect(isNull (cellAt lastColumn 1)).toBe false)
                RTL.fireEvent.click (cellAt firstColumn 1)

                if lastColumn <> firstColumn then
                    RTL.fireEvent.click (cellAt lastColumn 1, {| shiftKey = true |})

                openMenuAt lastColumn 1
                do! clickMenuItem "Copy"
                do! RTL.waitFor (fun () -> Vitest.expect(written.Value.Length).toBe 1)
                let! blob = written.Value.[0].getType "text/plain"
                let! text = blob.text ()
                Vitest.expect(text).toBe expected
                Vitest.expect(updated.Value).toEqual None
            finally
                restoreConstructor ()
                restore ()
        }
    )

Vitest.test (
    "Paste uses the clicked DataMap cell and retains structured selectors while growing rows",
    fun () -> promise {
        let source =
            CompositeCell.Data(Data(name = "pasted.csv#row=9", format = "text/csv"))

        let item =
            ClipboardTestHelpers.createItem "fallback text" [| [| source |]; [| source |] |]

        let clipboard =
            { new Clipboard with
                member _.read() = promise { return [| item |] }
                member _.readText() = promise { return "fallback text" }
                member _.write _ = promise { return () }
                member _.writeText _ = promise { return () }
            }

        let restore = ClipboardTestHelpers.install clipboard

        try
            let dataMap, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 3)).toBe false)
            RTL.fireEvent.click (cellAt 2 1)
            openMenu 3
            do! clickMenuItem "Paste"
            do! RTL.waitFor (fun () -> Vitest.expect(updated.Value.IsSome).toBe true)
            let result = updated.Value.Value
            Vitest.expect(result.RowCount).toBe 4

            for row in 2..3 do
                Vitest.expect(result.DataContexts.[row].FilePath).toEqual (Some "pasted.csv")
                Vitest.expect(result.DataContexts.[row].Selector).toEqual (Some "row=9")
                Vitest.expect(result.DataContexts.[row].Format).toEqual (Some "text/csv")

            Vitest.expect(result.DataContexts.[0].Label).toEqual (Some "Label 1")
            Vitest.expect(dataMap.RowCount).toBe 3
        finally
            restore ()
    }
)
