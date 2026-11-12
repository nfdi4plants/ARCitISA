module internal Swate.Components.Composite.Widgets.FilePickerWidgetTests

open ARCtrl
open Browser.Types
open Feliz
open Fable.Core
open Vitest
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.AnnotationTable.Context
open Swate.Components.Composite.DataMapTable
open Swate.Components.Shared

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockHTMLElementGeometry (vi: Vi) (propertyName: string) (value: int) : unit = jsNative

[<ReactComponent>]
let private InsertionTarget (arcFile: ArcFiles, activeTableIndex: int option) =
    let ctx = useAnnotationTableStateCtx ()

    Html.output [
        prop.testId "insertion-target"
        prop.text (
            if CellInsertion.tryGetTarget arcFile activeTableIndex ctx.state |> Option.isSome then
                "selected"
            else
                "none"
        )
    ]

let private checkDeselection arcFile activeTableIndex table clearSelection = promise {
    mockHTMLElementGeometry Vitest.vi "offsetWidth" 800
    mockHTMLElementGeometry Vitest.vi "offsetHeight" 600
    let mutable submitted = false
    let mutable updated: ArcFiles option = None

    RTL.render (
        AnnotationTableContextProvider.AnnotationTableContextProvider(
            Html.form [
                prop.onSubmit (fun event ->
                    event.preventDefault ()
                    submitted <- true
                )
                prop.children [
                    table
                    InsertionTarget(arcFile, activeTableIndex)
                    FilePickerWidget.Main(
                        arcFile,
                        activeTableIndex,
                        (fun next -> updated <- Some next),
                        fun () -> promise { return [| "file.csv" |] }
                    )
                ]
            ]
        )
    )
    |> ignore

    RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Pick Files")))
    let! _ = RTL.screen.findByTestId "sortable-list-row-file.csv"

    let insertButton () =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Insert file names")) :?> HTMLButtonElement

    let cell () =
        Browser.Dom.document.querySelector ("[data-row='1'][data-column='1'] > div") :?> HTMLElement

    Vitest.expect((insertButton ()).disabled).toBe true
    RTL.screen.getByText "Select table cells" |> ignore
    do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell ())).toBe false)

    RTL.fireEvent.click (cell ())
    Vitest.expect((cell ()).parentElement.getAttribute "data-selected").toBe "true"
    Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "selected"
    Vitest.expect((insertButton ()).disabled).toBe false

    clearSelection (cell ())
    Vitest.expect((cell ()).parentElement.hasAttribute "data-selected").toBe false
    Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "none"
    Vitest.expect((insertButton ()).disabled).toBe true
    RTL.screen.getByText "Select table cells" |> ignore

    // Selecting again restores both the destination and the button.
    RTL.fireEvent.click (cell ())
    Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "selected"
    Vitest.expect((insertButton ()).disabled).toBe false
    Vitest.expect(RTL.screen.queryByText("Select table cells").IsNone).toBe true
    submitted <- false
    RTL.fireEvent.click (insertButton ())
    Vitest.expect(updated.IsSome).toBe true
    Vitest.expect(submitted).toBe false
}

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

let private annotationDestination () =
    let table = ArcTable.init "Destination"
    table.AddColumn(CompositeHeader.Output IOType.Data, ResizeArray [ CompositeCell.emptyData ])
    let assay = ArcAssay.init "deselection"
    assay.AddTable table
    ArcFiles.Assay assay, AnnotationTable.AnnotationTable(table, ignore)

Vitest.test (
    "deselecting an annotation cell clears its insertion target",
    fun () ->
        let arcFile, table = annotationDestination ()
        checkDeselection arcFile (Some 0) table RTL.fireEvent.click
)

Vitest.test (
    "deselecting a DataMap cell clears its insertion target",
    fun () ->
        let dataMap = Datamap.init ()
        dataMap.DataContexts.Add(DataContext(name = "existing.csv"))

        checkDeselection
            (ArcFiles.DataMap(None, dataMap))
            None
            (DataMapTable.DataMapTable(dataMap, ignore))
            RTL.fireEvent.click
)

Vitest.test (
    "Escape from an active annotation cell clears its insertion target",
    fun () ->
        let arcFile, table = annotationDestination ()

        checkDeselection
            arcFile
            (Some 0)
            table
            (fun cell ->
                RTL.fireEvent.click (cell, {| detail = 2 |})
                RTL.fireEvent.custom ("keyDown", RTL.screen.getByRole "textbox", {| key = "Escape"; code = "Escape" |})
            )
)

Vitest.test (
    "file picker keeps path selection and clear-selected after sorting with SortableList",
    fun () -> promise {
        RTL.render (
            FilePickerWidget.Main(
                ArcFiles.Assay(ArcAssay.init "sortable-file-picker"),
                None,
                ignore,
                fun () -> promise { return [| "beta.txt"; "alpha.txt"; "gamma.txt" |] }
            )
        )
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Pick Files")))
        let! beta = RTL.screen.findByTestId "sortable-list-row-beta.txt"
        RTL.fireEvent.click beta
        Vitest.expect(beta.classList.contains "swt:bg-base-300").toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-down-beta.txt")

        let rows =
            RTL.screen.getAllByTestId (System.Text.RegularExpressions.Regex "^sortable-list-row-")

        Vitest.expect(rows |> Seq.map _.textContent |> Seq.toArray).toEqual [| "alpha.txt"; "beta.txt"; "gamma.txt" |]

        Vitest.expect((RTL.screen.getByTestId "sortable-list-row-beta.txt").classList.contains "swt:bg-base-300").toBe
            true

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-remove-gamma.txt")
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Clear Selected")))
        Vitest.expect(RTL.screen.queryByTestId("sortable-list-row-beta.txt").IsNone).toBe true
        Vitest.expect(RTL.screen.queryByTestId("sortable-list-row-gamma.txt").IsNone).toBe true
        RTL.screen.getByTestId "sortable-list-row-alpha.txt" |> ignore

        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Pick more files"))
        |> ignore

        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Insert file names"))
        |> ignore
    }
)

Vitest.test (
    "file picker uses SortableList shift-range selection",
    fun () -> promise {
        RTL.render (
            FilePickerWidget.Main(
                ArcFiles.Assay(ArcAssay.init "shift-select-file-picker"),
                None,
                ignore,
                fun () -> promise { return [| "alpha.txt"; "beta.txt"; "gamma.txt" |] }
            )
        )
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Pick Files")))
        let! _ = RTL.screen.findByTestId "sortable-list-row-alpha.txt"

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-row-alpha.txt")

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-row-gamma.txt", {| shiftKey = true |})

        Vitest.expect((RTL.screen.getByTestId "sortable-list-row-beta.txt").classList.contains "swt:bg-base-300").toBe
            true
    }
)
