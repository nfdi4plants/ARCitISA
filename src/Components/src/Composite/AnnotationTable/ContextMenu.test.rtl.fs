module internal Swate.Components.Composite.AnnotationTable.ContextMenuTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Vitest

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

[<Emit("new KeyboardEvent('keydown', {key: $0, code: $1, ctrlKey: $2, metaKey: $3, bubbles: true, cancelable: true})")>]
let private shortcutEvent (key: string) (code: string) (ctrl: bool) (meta: bool) : KeyboardEvent = jsNative

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

for includeBody in [ false; true ] do
    Vitest.test (
        $"Delete preserves headers and clears only selected body cells (includeBody={includeBody})",
        fun () -> promise {
            let table, updated = renderTable ()
            let header = table.Headers.[0].Copy()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 0)).toBe false)
            RTL.fireEvent.click (cell 1 0)

            if includeBody then
                RTL.fireEvent.custom ("click", cell 1 2, {| shiftKey = true |})

            let event = shortcutEvent "Delete" "Delete" false false
            (cell 1 0).dispatchEvent event |> ignore
            Vitest.expect(event.defaultPrevented).toBe true
            Vitest.expect(updated.Value.IsSome).toBe true
            Vitest.expect(updated.Value.Value.Headers.[0]).toEqual header
            Vitest.expect(updated.Value.Value.GetCellAt(0, 0).AsFreeText).toBe (if includeBody then "" else "A")
            Vitest.expect(updated.Value.Value.GetCellAt(0, 1).AsFreeText).toBe (if includeBody then "" else "B")
        }
    )

for ctrl, meta in [ true, false; false, true ] do
    Vitest.test (
        $"Cut leaves header selections unchanged (ctrl={ctrl}, meta={meta})",
        fun () -> promise {
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 0)).toBe false)
            RTL.fireEvent.click (cell 1 0)
            (cell 1 0).dispatchEvent (shortcutEvent "x" "KeyX" ctrl meta) |> ignore
            Vitest.expect(updated.Value.IsNone).toBe true
            Vitest.expect(table.GetCellAt(0, 0).AsFreeText).toBe "A"
        }
    )

Vitest.test (
    "context menu shows implemented shortcut chords and omits unsupported mnemonics",
    fun () -> promise {
        renderTable () |> ignore
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 1)).toBe false)
        openMenu 1 1
        let! _ = RTL.screen.findByRole "menu"

        let menuButton label =
            RTL.screen.getByRole (
                "button",
                ByRoleOptions(name = TextMatch.Regex(System.Text.RegularExpressions.Regex $"^{label}"))
            )

        Vitest.expect((menuButton "Details").querySelector("kbd").textContent).toBe "F2"
        let copyHint = (menuButton "Copy").querySelector("kbd").textContent
        Vitest.expect(copyHint = "Ctrl+C" || copyHint = "⌘+C").toBe true

        for label in
            [
                "Edit"
                "Fill Column"
                "Delete Row"
                "Delete Column"
                "Move Column"
            ] do
            Vitest.expect(isNull ((menuButton label).querySelector "kbd")).toBe true
    }
)
