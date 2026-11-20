module internal Swate.Components.Composite.Widgets.DataAnnotatorTests

open System.Text.RegularExpressions
open Browser.Types
open Fable.Core
open Vitest
open Swate.Components.Composite.Widgets.DataAnnotator.Types

module TestHelpers =

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

    let upload (name: string) = promise {
        let content = "a,b\nfirst,second\nthird,fourth"
        let file = createFile content name
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

    let selectTargets (values: string[]) = promise {
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Preview and Select Targets")))

        for value in values do
            let! target = RTL.screen.findByText (value, ByTextOptions(exact = true))
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

    let selectorRows () =
        RTL.screen.getAllByTestId (System.Text.RegularExpressions.Regex "^sortable-list-row-")


Vitest.beforeEach (fun () -> TestHelpers.installTableGeometry ())

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

Vitest.test (
    "inserts highlighted selectors in list order and supports repeated destinations",
    fun () -> promise {
        let submitted = ResizeArray<AnnotationInput>()

        let onInsert input =
            submitted.Add input
            Ok input.Selectors.Length

        let rendered =
            RTL.render (
                Swate.Components.Composite.Widgets.DataAnnotator.DataAnnotator.Main(
                    onInsert = onInsert,
                    canInsert = false
                )
            )

        do! TestHelpers.upload "data.csv"
        do! TestHelpers.selectTargets [| "first"; "second"; "third" |]

        Vitest.expect(submitted).toHaveLength 0
        Vitest.expect((TestHelpers.insertButton "Insert selectors").disabled).toBe true
        RTL.screen.getByText "Select table cells" |> ignore

        rendered.rerender (
            Swate.Components.Composite.Widgets.DataAnnotator.DataAnnotator.Main(onInsert = onInsert, canInsert = true)
        )

        Vitest.expect(RTL.screen.queryByText("Select table cells").IsNone).toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-row-cell=3,1")
        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-row-cell=2,1")
        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-down-cell=2,1")
        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-down-cell=2,1")

        Vitest.expect((RTL.screen.getByTestId "sortable-list-row-cell=2,1").classList.contains "swt:bg-base-300").toBe
            true

        RTL.fireEvent.click (TestHelpers.insertButton "Insert selected")
        Vitest.expect(submitted.[0].Selectors).toEqual [| "cell=3,1"; "cell=2,1" |]
        Vitest.expect(submitted.[0].FileName).toBe "data.csv"

        RTL.fireEvent.click (TestHelpers.insertButton "Insert selected")
        Vitest.expect(submitted).toHaveLength 2
        Vitest.expect(TestHelpers.selectorRows ()).toHaveLength 3

        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Clear Selected")))
        RTL.fireEvent.click (TestHelpers.insertButton "Insert selectors")
        Vitest.expect(submitted.[2].Selectors).toEqual [| "cell=2,2" |]
    }
)

Vitest.test (
    "removing a highlighted selector clears its selection and a new file resets the list",
    fun () -> promise {
        RTL.render (Swate.Components.Composite.Widgets.DataAnnotator.DataAnnotator.Main())
        |> ignore

        do! TestHelpers.upload "data.csv"
        do! TestHelpers.selectTargets [| "first"; "second" |]

        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-row-cell=2,1")
        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-remove-cell=2,1")

        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Clear Selected")).IsNone).toBe true

        RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Clear")) |> ignore

        do! TestHelpers.upload "other.csv"

        Vitest
            .expect(RTL.screen.queryAllByTestId(System.Text.RegularExpressions.Regex "^sortable-list-row-").Count)
            .toBe
            0

        do! TestHelpers.selectTargets [| "third" |]

        Vitest
            .expect(
                TestHelpers.selectorRows ()
                |> Seq.map (fun (row: HTMLElement) -> row.textContent)
                |> Seq.toArray
            )
            .toEqual
            [| "cell=3,1" |]
    }
)

Vitest.test (
    "shows an insertion error and leaves selectors available for retry",
    fun () -> promise {
        RTL.render (
            Swate.Components.Composite.Widgets.DataAnnotator.DataAnnotator.Main(
                onInsert = (fun _ -> Error "Select a destination cell.")
            )
        )
        |> ignore

        do! TestHelpers.upload "data.csv"
        do! TestHelpers.selectTargets [| "first" |]
        RTL.fireEvent.click (TestHelpers.insertButton "Insert selectors")

        Vitest.expect(RTL.screen.getByRole("alert").textContent).toBe "Select a destination cell."
        RTL.screen.getByTestId "sortable-list-row-cell=2,1" |> ignore
    }
)

Vitest.test (
    "adding targets again appends unique selectors and preserves their existing order",
    fun () -> promise {
        RTL.render (Swate.Components.Composite.Widgets.DataAnnotator.DataAnnotator.Main())
        |> ignore

        do! TestHelpers.upload "data.csv"
        do! TestHelpers.selectTargets [| "first"; "second" |]
        RTL.fireEvent.click (RTL.screen.getByTestId "sortable-list-down-cell=2,1")

        do! TestHelpers.selectTargets [| "first"; "third" |]

        Vitest
            .expect(
                TestHelpers.selectorRows ()
                |> Seq.map (fun (row: HTMLElement) -> row.textContent)
                |> Seq.toArray
            )
            .toEqual
            [| "cell=2,2"; "cell=2,1"; "cell=3,1" |]
    }
)
