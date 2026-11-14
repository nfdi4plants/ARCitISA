module internal Swate.Components.Composite.AnnotationTable.ContextMenuTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Vitest
open Swate.Components
open Swate.Components.Composite.Table.Types

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockImplementation(function () { return $1 === 'offsetWidth' && this.hasAttribute('data-column') ? 120 : $2; })")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

[<Emit("new KeyboardEvent('keydown', {key: $0, code: $1, ctrlKey: $2, metaKey: $3, bubbles: true, cancelable: true})")>]
let private shortcutEvent (key: string) (code: string) (ctrl: bool) (meta: bool) : KeyboardEvent = jsNative

let private renderTableValue table =
    mockGeometry Vitest.vi "offsetWidth" 800
    mockGeometry Vitest.vi "offsetHeight" 600
    let updated = ref None

    RTL.render (AnnotationTable.AnnotationTable(table, (fun next -> updated.Value <- Some next)))
    |> ignore

    table, updated

let private renderTable () =
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

    renderTableValue table

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

let private menuButton (label: string) = RTL.screen.findByText label

let private readClipboard text items =
    { new Clipboard with
        member _.read() = promise { return items }
        member _.readText() = promise { return text }
        member _.write _ = promise { return () }
        member _.writeText _ = promise { return () }
    }

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

Vitest.test (
    "Delete Column on the first header removes the first data column",
    fun () -> promise {
        let table, updated = renderTable ()
        let remainingColumn = table.GetColumn(1)
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 0)).toBe false)
        openMenu 1 0
        let! delete = menuButton "Delete Column"
        RTL.fireEvent.click delete
        Vitest.expect(updated.Value.Value.ColumnCount).toBe 1
        Vitest.expect(updated.Value.Value.GetColumn(0)).toEqual remainingColumn
    }
)

Vitest.test (
    "Delete Row on the first row index removes the first data row",
    fun () -> promise {
        let table, updated = renderTable ()
        let remainingRow = table.GetRow(1)
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 0 1)).toBe false)
        openMenu 0 1
        let! delete = menuButton "Delete Row"
        RTL.fireEvent.click delete
        Vitest.expect(updated.Value.Value.RowCount).toBe 1
        Vitest.expect(updated.Value.Value.GetRow(0)).toEqual remainingRow
    }
)

Vitest.test (
    "Paste reads plain clipboard text into the clicked column outside a stale selection",
    fun () -> promise {
        let restore = ClipboardTestHelpers.install (readClipboard "hot" [||])

        try
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 2 1)).toBe false)
            RTL.fireEvent.click (cell 1 1)
            openMenu 2 1
            let! paste = menuButton "Paste"
            RTL.fireEvent.click paste
            do! RTL.waitFor (fun () -> Vitest.expect(updated.Value.IsSome).toBe true)
            Vitest.expect(updated.Value.Value.GetCellAt(1, 0).AsTerm.NameText).toBe "hot"
            Vitest.expect(updated.Value.Value.GetCellAt(0, 0).AsFreeText).toBe "A"
            Vitest.expect(table.GetCellAt(1, 0).AsTerm.NameText).toBe "warm"
        finally
            restore ()
    }
)

Vitest.test (
    "Paste reads structured clipboard cells with selectors and ontology metadata",
    fun () -> promise {
        let source = [|
            CompositeCell.createDataFromString "file.txt#row=2"
            CompositeCell.createTermFromString ("instrument", "MS", "MS:1000031")
            CompositeCell.createUnitizedFromString ("4", "metre", "UO", "UO:0000008")
        |]

        let item =
            ClipboardTestHelpers.createItem "file.txt#row=2\tinstrument\t4 metre" [| source |]

        let restore = ClipboardTestHelpers.install (readClipboard "" [| item |])

        try
            let target = ArcTable.init "Structured paste"
            target.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "keep" ])

            target.AddColumn(
                CompositeHeader.Output IOType.Data,
                ResizeArray [ CompositeCell.createDataFromString "old.txt" ]
            )

            target.AddColumn(
                CompositeHeader.Characteristic(OntologyAnnotation "Instrument"),
                ResizeArray [ CompositeCell.Term(OntologyAnnotation "old") ]
            )

            target.AddColumn(
                CompositeHeader.Parameter(OntologyAnnotation "Length"),
                ResizeArray [ CompositeCell.Term(OntologyAnnotation "old") ]
            )

            let _, updated = renderTableValue target
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 4 1)).toBe false)
            RTL.fireEvent.click (cell 1 1)
            openMenu 2 1
            let! paste = menuButton "Paste"
            RTL.fireEvent.click paste
            do! RTL.waitFor (fun () -> Vitest.expect(updated.Value.IsSome).toBe true)
            Vitest.expect(updated.Value.Value.GetCellAt(0, 0).AsFreeText).toBe "keep"

            for index in 0..2 do
                Vitest.expect(updated.Value.Value.GetCellAt(index + 1, 0)).toEqual source.[index]

            Vitest.expect(target.GetCellAt(1, 0).ToString()).toBe "old.txt"
        finally
            restore ()
    }
)

Vitest.test (
    "Paste on a header uses plain header content instead of structured body cells",
    fun () -> promise {
        let item =
            ClipboardTestHelpers.createItem "Input [Source Name]" [| [| CompositeCell.FreeText "structured body" |] |]

        let restore = ClipboardTestHelpers.install (readClipboard "" [| item |])

        try
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 0)).toBe false)
            openMenu 1 0
            let! paste = menuButton "Paste"
            RTL.fireEvent.click paste
            let! _ = RTL.screen.findByRole ("dialog", ByRoleOptions(name = Text "Headers have been detected"))
            Vitest.expect(updated.Value.IsNone).toBe true
            Vitest.expect(table.GetCellAt(0, 0).AsFreeText).toBe "A"
            Vitest.expect(isNull (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Add columns")))).toBe false

            Vitest
                .expect(
                    isNull (RTL.screen.getByRole ("columnheader", ByRoleOptions(name = Text "Input [Source Name]")))
                )
                .toBe
                false
        finally
            restore ()
    }
)

Vitest.test (
    "Paste of an all-blank structured body reports an unknown paste case without clearing cells",
    fun () -> promise {
        let item = ClipboardTestHelpers.createItem "" [| [| CompositeCell.FreeText "" |] |]
        let restore = ClipboardTestHelpers.install (readClipboard "" [| item |])

        try
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 1)).toBe false)
            openMenu 1 1
            let! paste = menuButton "Paste"
            RTL.fireEvent.click paste
            let! dialog = RTL.screen.findByRole ("dialog", ByRoleOptions(name = Text "Something went wrong"))
            Vitest.expect(dialog.textContent.Contains "We cannot determine the paste case").toBe true
            Vitest.expect(updated.Value.IsNone).toBe true
            Vitest.expect(table.GetCellAt(0, 0).AsFreeText).toBe "A"
        finally
            restore ()
    }
)

Vitest.test (
    "Copy includes the data selector in its plain text fallback",
    fun () -> promise {
        let mutable writtenText = None

        let clipboard =
            { new Clipboard with
                member _.read() = promise { return [||] }
                member _.readText() = promise { return "" }
                member _.write _ = promise { return raise (System.Exception "Rich clipboard unavailable") }
                member _.writeText text = promise { writtenText <- Some text }
            }

        let restore = ClipboardTestHelpers.install clipboard

        try
            let table = ArcTable.init "Copy selector"

            table.AddColumn(
                CompositeHeader.Output IOType.Data,
                ResizeArray [ CompositeCell.createDataFromString "file.txt#row=2" ]
            )

            renderTableValue table |> ignore
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 1)).toBe false)
            openMenu 1 1
            let! copy = menuButton "Copy"
            RTL.fireEvent.click copy
            do! RTL.waitFor (fun () -> Vitest.expect(writtenText).toEqual (Some "file.txt#row=2"))
        finally
            restore ()
    }
)

Vitest.test (
    "Cut clears source cells only after clipboard writing succeeds",
    fun () -> promise {
        let mutable completeWrite: unit -> unit = ignore
        let pendingWrite = Promise.create (fun resolve _ -> completeWrite <- resolve)
        let mutable writeStarted = false

        let clipboard =
            { new Clipboard with
                member _.read() = promise { return [||] }
                member _.readText() = promise { return "" }

                member _.write _ =
                    writeStarted <- true
                    pendingWrite

                member _.writeText _ =
                    writeStarted <- true
                    pendingWrite
            }

        let restore = ClipboardTestHelpers.install clipboard
        let restoreItem = ClipboardTestHelpers.installItemConstructor ()

        try
            let table, updated = renderTable ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell 1 1)).toBe false)
            openMenu 1 1
            let! cut = menuButton "Cut"
            RTL.fireEvent.click cut
            do! RTL.waitFor (fun () -> Vitest.expect(writeStarted).toBe true)
            Vitest.expect(updated.Value.IsNone).toBe true
            Vitest.expect(table.GetCellAt(0, 0).AsFreeText).toBe "A"
            completeWrite ()
            do! RTL.waitFor (fun () -> Vitest.expect(updated.Value.IsSome).toBe true)
            Vitest.expect(updated.Value.Value.GetCellAt(0, 0).AsFreeText).toBe ""
            Vitest.expect(updated.Value.Value.GetCellAt(0, 1).AsFreeText).toBe "B"
            Vitest.expect(table.GetCellAt(0, 0).AsFreeText).toBe "A"
        finally
            restoreItem ()
            restore ()
    }
)

Vitest.test (
    "Cut preserves source cells when every clipboard write fails",
    fun () -> promise {
        let clipboard =
            { new Clipboard with
                member _.read() = promise { return [||] }
                member _.readText() = promise { return "" }
                member _.write _ = promise { return raise (System.Exception "write failed") }
                member _.writeText _ = promise { return raise (System.Exception "write failed") }
            }

        let restore = ClipboardTestHelpers.install clipboard
        let restoreItem = ClipboardTestHelpers.installItemConstructor ()

        try
            let table, updated = renderTable ()
            let anchor: CellCoordinate = {| x = 1; y = 1 |}

            let selected =
                SelectHandle(
                    (fun _ -> false),
                    ignore,
                    ignore,
                    (fun () -> None),
                    (fun () -> ResizeArray()),
                    (fun () -> 0)
                )

            let mutable failure = None

            // Await the utility directly: the menu starts this promise without exposing its rejection.
            try
                do!
                    AnnotationTableContextMenuUtil.cut (
                        anchor,
                        table,
                        (fun next -> updated.Value <- Some next),
                        selected
                    )
            with error ->
                failure <- Some error.Message

            Vitest.expect(failure).toEqual (Some "write failed")
            Vitest.expect(updated.Value.IsNone).toBe true
            Vitest.expect(table.GetCellAt(0, 0).AsFreeText).toBe "A"
        finally
            restoreItem ()
            restore ()
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
