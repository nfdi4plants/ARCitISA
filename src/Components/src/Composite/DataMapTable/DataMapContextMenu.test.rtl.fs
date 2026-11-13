module internal Swate.Components.Composite.DataMapTable.DataMapContextMenuTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Vitest

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

let private renderTable () =
    mockGeometry Vitest.vi "offsetWidth" 800
    mockGeometry Vitest.vi "offsetHeight" 600

    let dataMap =
        Datamap(
            ResizeArray [
                for i in 1..3 -> DataContext(name = $"data-{i}.csv", label = $"Label {i}")
            ]
        )

    let updated = ref None

    RTL.render (DataMapTable.DataMapTable(dataMap, (fun next -> updated.Value <- Some next)))
    |> ignore

    dataMap, updated

let private cell row =
    document.querySelector ($"[data-row='{row}'][data-column='1'] > div") :?> HTMLElement

let private openMenu row =
    RTL.fireEvent.custom (
        "contextMenu",
        cell row,
        {|
            clientX = 40
            clientY = 40
            bubbles = true
        |}
    )

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
