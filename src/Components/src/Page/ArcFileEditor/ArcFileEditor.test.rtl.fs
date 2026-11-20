module internal Swate.Components.Page.ArcFileEditor.DataAnnotatorTests

open ARCtrl
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.AnnotationTable.Context
open Swate.Components.Shared
open Swate.Components.Page.ArcFileEditor.Types

module private TestHelpers =

    [<Emit("new File([$0], $1, { type: 'text/csv' })")>]
    let private createFile (content: string) (name: string) : File = jsNative

    [<Emit("Object.defineProperty($0, 'text', { value: async () => $1 })")>]
    let private defineFileText (file: File) (content: string) : unit = jsNative

    [<Emit("({ 0: $0, length: 1, item: index => index === 0 ? $0 : null })")>]
    let private singleFileList (file: File) : FileList = jsNative

    [<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
    let private mockHTMLElementGeometry (vi: Vi) (propertyName: string) (value: int) : unit = jsNative

    let installTableGeometry () =
        mockHTMLElementGeometry Vitest.vi "offsetWidth" 800
        mockHTMLElementGeometry Vitest.vi "offsetHeight" 600

    let upload () = promise {
        let content = "a,b\nfirst,second\nthird,fourth"
        let file = createFile content "data.csv"
        defineFileText file content

        let! uploadInput = RTL.screen.findByLabelText (System.Text.RegularExpressions.Regex "Upload a CSV or TSV")

        RTL.fireEvent.change (
            uploadInput,
            {|
                target = {| files = singleFileList file |}
            |}
        )

        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Preview and Select Targets"))
        return ()
    }

    let selectFirstTarget () = promise {
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Preview and Select Targets")))

        let! target = RTL.screen.findByText ("first", ByTextOptions(exact = true))
        RTL.fireEvent.click target

        RTL.fireEvent.click (
            RTL.screen.getByRole (
                "button",
                ByRoleOptions(name = TextMatch.Regex(System.Text.RegularExpressions.Regex "^(Submit|Add selectors)$"))
            )
        )
    }

    let insertButton (name: string) =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text name)) :?> HTMLButtonElement

let private createArcFile () =
    let table = ArcTable.init "Annotations"

    table.AddColumn(
        CompositeHeader.Output IOType.Data,
        ResizeArray [ CompositeCell.emptyData; CompositeCell.emptyData ]
    )

    let assay = ArcAssay.init "DataAnnotatorLazyBoundary"
    assay.AddTable table
    ArcFiles.Assay assay

[<ReactComponent>]
let private SelectDestination (tableName: string) =
    let annotationCtx = useAnnotationTableStateCtx ()

    Html.button [
        prop.text "Select destination"
        prop.onClick (fun _ ->
            let selection: CellCoordinateRange = {|
                xStart = 1
                xEnd = 1
                yStart = 1
                yEnd = 1
            |}

            annotationCtx.setSelection tableName (Some selection)
        )
    ]

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

Vitest.test (
    "lazy DataAnnotator receives insertion props and enables after selecting a destination",
    fun () -> promise {
        TestHelpers.installTableGeometry ()

        let arcFile = createArcFile ()
        let tableName = arcFile.Tables().[0].Name
        let mutable updatedArcFile: ArcFiles option = None

        RTL.render (
            AnnotationTableContextProvider.AnnotationTableContextProvider(
                React.Fragment [
                    Main.LazyLoaderWithMessage(
                        Main.DataAnnotatorWidget(arcFile, Some 0, (fun next -> updatedArcFile <- Some next), ignore),
                        "Loading Data Annotator Widget..."
                    )
                    SelectDestination(tableName)
                ]
            )
        )
        |> ignore

        do! TestHelpers.upload ()
        do! TestHelpers.selectFirstTarget ()

        Vitest.expect((TestHelpers.insertButton "Insert selectors").disabled).toBe true
        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-row-cell=2,1")
        Vitest.expect((TestHelpers.insertButton "Insert selected").disabled).toBe true
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Select destination")))

        do! RTL.waitFor (fun () -> Vitest.expect((TestHelpers.insertButton "Insert selected").disabled).toBe false)

        RTL.fireEvent.click (TestHelpers.insertButton "Insert selected")
        Vitest.expect(updatedArcFile.IsSome).toBe true

        let updatedTable = updatedArcFile.Value.Tables().[0]
        Vitest.expect(updatedTable.GetCellAt(0, 0).AsData.Selector).toEqual (Some "cell=2,1")
    }
)

[<ReactComponent>]
let private InsertionTarget (arcFile: ArcFiles, activeView: ActiveView) =
    let ctx = useAnnotationTableStateCtx ()

    let target =
        Composite.Widgets.CellInsertion.tryGetTarget arcFile activeView.TryTableIndex ctx.state

    Html.output [
        prop.testId "insertion-target"
        prop.text (if target.IsSome then "selected" else "none")
    ]

let private createEditorArcFile () =
    let assay = ArcAssay.init "SelectionSharing"

    for name in [ "First"; "Second" ] do
        let table = ArcTable.init name
        table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "Alpha" ])
        assay.AddTable table

    assay.Datamap <- Some(Datamap(ResizeArray [ DataContext(name = "Alpha.csv") ]))
    ArcFiles.Assay assay

let private renderEditor (startingActiveView: ActiveView) =
    TestHelpers.installTableGeometry ()

    RTL.render (
        Main.ArcFileEditor(
            createEditorArcFile (),
            ignore,
            (fun _ -> promise { return [||] }),
            trailingNavbarElements =
                (fun (props: ArcFileEditorHeaderProps) -> InsertionTarget(props.arcFile, props.activeView)),
            startingActiveView = startingActiveView
        )
    )
    |> ignore

let private firstCell () =
    Browser.Dom.document.querySelector ("[data-row='1'][data-column='1'] > div") :?> HTMLElement

let private selectFirstCell () = promise {
    do! RTL.waitFor (fun () -> Vitest.expect(isNull (firstCell ())).toBe false)
    RTL.fireEvent.click (firstCell ())
    Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "selected"
}

let private clickElement (selector: string) =
    RTL.fireEvent.click (Browser.Dom.document.querySelector selector :?> HTMLElement)

let private hasSelectedCell () =
    not (isNull (Browser.Dom.document.querySelector "[data-selected='true']"))

Vitest.test (
    "returning to a table from the metadata view leaves no insertion target",
    fun () -> promise {
        renderEditor (ActiveView.Table 0)
        do! selectFirstCell ()
        clickElement "[data-footertabid='metadata']"
        Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "none"
        RTL.fireEvent.click (RTL.screen.getByTestId "arc-file-editor-table-tab-0")
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (firstCell ())).toBe false)
        Vitest.expect(hasSelectedCell ()).toBe false
        Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "none"
    }
)

Vitest.test (
    "switching to another table shows no selection and no insertion target",
    fun () -> promise {
        renderEditor (ActiveView.Table 0)
        do! selectFirstCell ()
        RTL.fireEvent.click (RTL.screen.getByTestId "arc-file-editor-table-tab-1")
        do! RTL.waitFor (fun () -> Vitest.expect(isNull (firstCell ())).toBe false)
        Vitest.expect(hasSelectedCell ()).toBe false
        Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "none"
    }
)

Vitest.test (
    "leaving the DataMap view clears its insertion target",
    fun () -> promise {
        renderEditor ActiveView.DataMap
        do! selectFirstCell ()
        clickElement "[data-footertabid='metadata']"
        Vitest.expect((RTL.screen.getByTestId "insertion-target").textContent).toBe "none"
    }
)
