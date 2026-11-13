module internal Swate.Components.Composite.AnnotationTable.ContextMenuTests

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
    let table = ArcTable.init "Context menu"

    table.AddColumn(
        CompositeHeader.Input IOType.Source,
        ResizeArray [ CompositeCell.FreeText "A"; CompositeCell.FreeText "B" ]
    )

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation("Temperature")),
        ResizeArray [
            CompositeCell.Term(OntologyAnnotation("warm"))
            CompositeCell.Term(OntologyAnnotation("cold"))
        ]
    )

    let updated = ref None

    RTL.render (AnnotationTable.AnnotationTable(table, (fun next -> updated.Value <- Some next)))
    |> ignore

    table, updated

let private cell column row =
    document.querySelector ($"[data-row='{row}'][data-column='{column}'] > div") :?> HTMLElement

let private openMenu column row =
    RTL.fireEvent.custom (
        "contextMenu",
        cell column row,
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
    "Duplicate Row targets a right-click outside the selection and copies the entire row",
    fun () -> promise {
        let table, updated = renderTable ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 2)).toBe false)
        RTL.fireEvent.click (cell 1 1)
        openMenu 1 2
        let! duplicate = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Duplicate Row"))
        RTL.fireEvent.click duplicate
        Vitest.expect(updated.Value.Value.RowCount).toBe 3
        Vitest.expect(updated.Value.Value.GetRow(2)).toEqual (table.GetRow(1))
        Vitest.expect(table.RowCount).toBe 2
    }
)

Vitest.test (
    "Duplicate Row is available on the row index",
    fun () -> promise {
        let table, updated = renderTable ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 0 1)).toBe false)
        openMenu 0 1
        let! duplicate = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Duplicate Row"))
        RTL.fireEvent.click duplicate
        Vitest.expect(updated.Value.Value.GetRow(1)).toEqual (table.GetRow(0))
    }
)

for row in [ 0; 1 ] do
    Vitest.test (
        $"Move Column opens the sortable dialog from table row {row} and applies the chosen order",
        fun () -> promise {
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2 row)).toBe false)
            openMenu 2 row

            let! move =
                RTL.screen.findByRole (
                    "button",
                    ByRoleOptions(name = TextMatch.Regex(System.Text.RegularExpressions.Regex "^Move Column"))
                )

            RTL.fireEvent.click move
            let! moveUp = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Move Parameter [Temperature] up"))
            RTL.fireEvent.click moveUp
            Vitest.expect(updated.Value).toEqual None
            RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Submit")))
            Vitest.expect(updated.Value.Value.GetColumn(0)).toEqual (table.GetColumn(1))
            Vitest.expect(updated.Value.Value.GetColumn(1)).toEqual (table.GetColumn(0))
        }
    )

    Vitest.test (
        $"Duplicate Column copies the entire column from table row {row}",
        fun () -> promise {
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2 row)).toBe false)
            RTL.fireEvent.click (cell 1 1)
            openMenu 2 row
            let! duplicate = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Duplicate Column"))
            RTL.fireEvent.click duplicate
            Vitest.expect(updated.Value.Value.ColumnCount).toBe 3
            Vitest.expect(updated.Value.Value.GetColumn(2)).toEqual (table.GetColumn(1))
            Vitest.expect(table.ColumnCount).toBe 2
        }
    )

Vitest.test (
    "unique columns have no Duplicate Column menu item",
    fun () -> promise {
        let _, _ = renderTable ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 0)).toBe false)
        openMenu 1 0
        let! _ = RTL.screen.findByRole "menu"
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Duplicate Column")).IsNone).toBe true
    }
)
