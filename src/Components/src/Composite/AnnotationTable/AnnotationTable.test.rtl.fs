module internal Swate.Components.Composite.AnnotationTable.AnnotationTableTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components.Composite.AnnotationTable.Context

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

[<ReactComponent>]
let private SelectionKeys () =
    let ctx = useAnnotationTableStateCtx ()

    Html.output [
        prop.testId "selection-keys"
        prop.text (ctx.state |> Map.toList |> List.map fst |> String.concat ",")
    ]

let private tableNamed (name: string) =
    let table = ArcTable.init name
    table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "Alpha" ])
    table

let private view (table: ArcTable) =
    AnnotationTableContextProvider.AnnotationTableContextProvider(
        React.Fragment [
            AnnotationTable.AnnotationTable(table, ignore)
            SelectionKeys()
        ]
    )

let private firstCell () =
    document.querySelector "[data-row='1'][data-column='1'] > div" :?> HTMLElement

let private hasSelectedCell () =
    not (isNull (document.querySelector "[data-selected='true']"))

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

Vitest.test (
    "rendering another table in the same instance drops the selection and its context entry",
    fun () -> promise {
        mockGeometry Vitest.vi "offsetWidth" 800
        mockGeometry Vitest.vi "offsetHeight" 600
        let rendered = RTL.render (view (tableNamed "A"))
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (firstCell ())).toBe false)
        RTL.fireEvent.click (firstCell ())
        Vitest.expect(hasSelectedCell ()).toBe true
        Vitest.expect((RTL.screen.getByTestId "selection-keys").textContent).toBe "A"

        rendered.rerender (view (tableNamed "B"))
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (firstCell ())).toBe false)
        Vitest.expect(hasSelectedCell ()).toBe false
        Vitest.expect((RTL.screen.getByTestId "selection-keys").textContent).toBe ""
    }
)
