module internal Swate.Components.Composite.Table.TableCellTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components
open Swate.Components.Composite.TermSearch.Context.ConfigContext

type private User =
    abstract click: Element -> JS.Promise<unit>
    abstract dblClick: Element -> JS.Promise<unit>
    abstract keyboard: string -> JS.Promise<unit>
    abstract paste: string -> JS.Promise<unit>

type private UserEvent =
    abstract setup: unit -> User

[<ImportDefault("@testing-library/user-event")>]
let private userEvent: UserEvent = jsNative

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

let private renderTable (cell: CompositeCell) (setCell: CompositeCell -> unit) =
    mockGeometry Vitest.vi "offsetWidth" 800
    mockGeometry Vitest.vi "offsetHeight" 600

    RTL.render (
        TermSearchConfigCtx.Provider(
            {
                TermSearchConfigContext.init () with
                    disableDefault = true
            },
            Table.Table(
                rowCount = 3,
                columnCount = 2,
                renderCell = (fun index -> TableCell.StringInactiveCell(index, cell.ToString())),
                renderActiveCell = (fun index -> TableCell.CompositeCellActiveCell(index, cell, setCell)),
                height = 400,
                width = 600,
                debug = true
            )
        )
    )
    |> ignore

let private cellElement row =
    document.querySelector ($"[data-row='{row}'][data-column='1'] > div") :?> HTMLElement

let private input () =
    document.querySelector "input" :?> HTMLInputElement

let private expectSelected (text: string) =
    let editor = input ()
    Vitest.expect(document.activeElement).toBe editor
    Vitest.expect(editor.value).toBe text
    Vitest.expect(editor.selectionStart).toBe 0
    Vitest.expect(editor.selectionEnd).toBe text.Length

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

for name, cell in
    [
        "text", CompositeCell.FreeText "Existing value"
        "term", CompositeCell.Term(OntologyAnnotation("Existing value"))
        "unitized", CompositeCell.Unitized("Existing value", OntologyAnnotation("kg"))
        "data", CompositeCell.Data(Data(name = "Existing value"))
    ] do
    Vitest.test (
        $"activating a {name} cell selects its value and typing replaces it",
        fun () -> promise {
            let changes = ResizeArray<CompositeCell>()
            renderTable cell changes.Add
            let user = userEvent.setup ()
            do! RTL.waitFor (fun () -> Vitest.expect(isNull (cellElement 1)).toBe false)
            do! user.dblClick (cellElement 1)
            expectSelected "Existing value"

            do! user.keyboard "Replacement"
            Vitest.expect((input ()).value).toBe "Replacement"
            do! user.keyboard "!"
            Vitest.expect((input ()).value).toBe "Replacement!"
            do! user.keyboard "{Enter}"
            Vitest.expect(changes.Count).toBe 1

            match changes.[0] with
            | CompositeCell.FreeText value -> Vitest.expect(value).toBe "Replacement!"
            | CompositeCell.Term term -> Vitest.expect(term.NameText).toBe "Replacement!"
            | CompositeCell.Unitized(value, unit) ->
                Vitest.expect(value).toBe "Replacement!"
                Vitest.expect(unit.NameText).toBe "kg"
            | CompositeCell.Data data -> Vitest.expect(data.Name).toEqual (Some "Replacement!")
        }
    )

Vitest.test (
    "typing in a selected cell replaces the value, including the activation keystroke",
    fun () -> promise {
        renderTable (CompositeCell.FreeText "Existing value") ignore
        let user = userEvent.setup ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cellElement 1)).toBe false)
        do! user.click (cellElement 1)
        do! user.keyboard "Replacement"
        Vitest.expect((input ()).value).toBe "Replacement"
    }
)

Vitest.test (
    "pasting into an activated cell replaces its value and Escape cancels the change",
    fun () -> promise {
        let changes = ResizeArray<CompositeCell>()
        renderTable (CompositeCell.FreeText "Existing value") changes.Add
        let user = userEvent.setup ()
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cellElement 1)).toBe false)
        do! user.dblClick (cellElement 1)
        do! user.paste "Pasted value"
        Vitest.expect((input ()).value).toBe "Pasted value"
        do! user.keyboard "{Escape}"
        Vitest.expect(changes.Count).toBe 0
        Vitest.expect(isNull (input ())).toBe true
    }
)
