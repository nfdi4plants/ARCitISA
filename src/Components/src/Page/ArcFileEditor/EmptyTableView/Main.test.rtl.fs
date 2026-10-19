module internal Swate.Components.Page.ArcFileEditor.EmptyTableView.MainTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Page.ArcFileEditor.EmptyTableView
open Swate.Components.Page.ArcFileEditor.EmptyTableViewSample
open Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter

module private Queries =

    [<Import("getByRole", "@testing-library/dom")>]
    let inRole (container: HTMLElement, role: string, options: ByRoleOptions) : HTMLElement = jsNative

    let button name =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))

    let card name =
        RTL.screen.getByRole (
            "button",
            ByRoleOptions(name = TextMatch.Regex(System.Text.RegularExpressions.Regex name))
        )

    let heading name =
        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text name))

    let dialog name =
        RTL.screen.getByRole ("dialog", ByRoleOptions(name = Text name))

    let clickButton name = RTL.fireEvent.click (button name)

    let openTemplates () =
        RTL.fireEvent.click (card "Start with template!")

    let selectLeafTemplate () =
        RTL.fireEvent.click (RTL.screen.getByText ("Leaf template", ByTextOptions(exact = true)))

    let confirmImport () =
        RTL.fireEvent.click (inRole (dialog "Import templates", "button", ByRoleOptions(name = Text "Import")))

let private view arcFile setArcFile activeIndex =
    Main.Providers(
        Swate.Components.Page.ArcFileEditor.EmptyTableView.Main.EmptyTableView(arcFile, setArcFile, activeIndex)
    )

let private viewWithArcNodes arcFile setArcFile activeIndex fetchArcNodes =
    Main.Providers(
        Swate.Components.Page.ArcFileEditor.EmptyTableView.Main.EmptyTableView(
            arcFile,
            setArcFile,
            activeIndex,
            fetchArcNodes = fetchArcNodes
        )
    )

module private PriorTables =

    let ioTable (name: string) (input: string) (output: string) =
        let table = ArcTable.init name
        table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.createFreeText input ])
        table.AddColumn(CompositeHeader.Output IOType.Sample, ResizeArray [ CompositeCell.createFreeText output ])
        table

    /// The open assay holds one filled table and one empty table at index 1.
    let arcFile () =
        let assay = ArcAssay.init "Inline setup"
        assay.AddTable(ioTable "Filled" "local-source" "local-sample")
        assay.AddTable(ArcTable.init "Empty")
        ArcFiles.Assay assay

    let arcNode scope parentIdentifier (table: ArcTable) : Types.ArcNodeTable = {
        Location = {
            Scope = scope
            ParentIdentifier = parentIdentifier
            TableName = table.Name
        }
        Table = table
    }

    let card () = Queries.card "Utilize prior tables!"

    let dialog () = Queries.dialog "Utilize prior tables"

    let columnSelect () =
        Queries.inRole (dialog (), "combobox", ByRoleOptions(name = Text "Column:")) :?> HTMLSelectElement

    let optionLabels () =
        columnSelect().options
        |> fun options -> [|
            for index in 0 .. options.length - 1 -> (options.item index :?> HTMLOptionElement).text
        |]

    let selectOption (label: string) =
        let select = columnSelect ()

        let option =
            [|
                for index in 0 .. select.options.length - 1 -> select.options.item index :?> HTMLOptionElement
            |]
            |> Array.find (fun option -> option.text = label)

        RTL.fireEvent.change (
            select,
            {|
                target = {| value = option.value |}
            |}
        )

    let importButton () =
        Queries.inRole (dialog (), "button", ByRoleOptions(name = Text "Import as input column")) :?> HTMLButtonElement

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "inline setup focuses its heading and Back returns to the choices",
    fun () -> promise {
        RTL.render (view (Fixtures.arcFile ()) ignore (Some 0)) |> ignore

        RTL.fireEvent.click (Queries.card "Start from scratch!")
        let heading = Queries.heading "Select a building block"

        Vitest
            .expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Select a building block")).IsNone)
            .toBe
            true

        do! RTL.waitFor (fun () -> Vitest.expect(box document.activeElement).toBe (box heading))

        Queries.clickButton "Back"
        Queries.card "Start from scratch!" |> ignore

        Queries.openTemplates ()
        let templateHeading = Queries.heading "Select template(s)"
        do! RTL.waitFor (fun () -> Vitest.expect(box document.activeElement).toBe (box templateHeading))
        Queries.clickButton "Back"
        Queries.card "Start with template!" |> ignore
    }
)

Vitest.test (
    "switching empty tables resets the inline route and template selection",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let rendered = RTL.render (view arcFile ignore (Some 0))
        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        RTL.screen.getByText "1 selected" |> ignore

        rendered.rerender (view arcFile ignore (Some 1))

        Vitest.expect(RTL.screen.queryByRole("heading", ByRoleOptions(name = Text "Select template(s)")).IsNone).toBe
            true

        Queries.openTemplates ()
        Vitest.expect((Queries.button "Import" :?> HTMLButtonElement).disabled).toBe true
        Vitest.expect(arcFile.Tables().[0].ColumnCount).toBe 0
        Vitest.expect(arcFile.Tables().[1].ColumnCount).toBe 0
    }
)

Vitest.test (
    "adding a building block invokes the file callback with the selected column",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 0)) |> ignore
        RTL.fireEvent.click (Queries.card "Start from scratch!")
        Queries.clickButton "Parameter"
        let picker = Queries.dialog "Building block type"
        RTL.fireEvent.click (RTL.screen.getByText ("More", ByTextOptions(exact = true)))
        // A single-column header makes the observable callback independent of ontology search.
        RTL.fireEvent.click (Queries.inRole (picker, "button", ByRoleOptions(name = Text "Date")))
        Queries.clickButton "Add Column"

        Vitest.expect(updated.IsSome).toBe true
        Vitest.expect(updated.Value.Tables().[0].Headers.[0]).toEqual CompositeHeader.Date
        Vitest.expect(updated.Value.Tables().[1].ColumnCount).toBe 0
    }
)

Vitest.test (
    "canceling import retains selection and confirmed append updates only the active table",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        let closedWidgets = ResizeArray<WidgetType>()

        let widgetController = {
            WidgetControllerContext.init () with
                activeWidgets = [ WidgetType.Template ]
                closeWidget = closedWidgets.Add
        }

        RTL.render (
            WidgetControllerCtx.Provider(widgetController, view arcFile (fun next -> updated <- Some next) (Some 0))
        )
        |> ignore

        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        Queries.clickButton "Import"

        RTL.fireEvent.click (
            Queries.inRole (Queries.dialog "Import templates", "button", ByRoleOptions(name = Text "Cancel"))
        )

        Vitest.expect(updated.IsNone).toBe true
        RTL.screen.getByText "1 selected" |> ignore
        Queries.heading "Select template(s)" |> ignore
        Queries.clickButton "Import"
        Queries.confirmImport ()

        Vitest.expect(updated.IsSome).toBe true
        let tables = updated.Value.Tables()
        Vitest.expect(tables.[0].ColumnCount).toBe 3

        Vitest
            .expect(tables.[0].Headers.[1])
            .toEqual (CompositeHeader.Parameter(OntologyAnnotation("Leaf temperature")))

        Vitest.expect(tables.[1].ColumnCount).toBe 0
        Vitest.expect(closedWidgets.Count).toBe 0
    }
)

Vitest.test (
    "import as a new table returns to choices while preserving the empty active table",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 0)) |> ignore
        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        Queries.clickButton "Import"

        RTL.fireEvent.click (
            Queries.inRole (Queries.dialog "Import templates", "radio", ByRoleOptions(name = Text "Import (new table)"))
        )

        Queries.confirmImport ()

        Vitest.expect(updated.IsSome).toBe true
        Vitest.expect(updated.Value.Tables().Count).toBe 3
        Vitest.expect(updated.Value.Tables().[0].ColumnCount).toBe 0
        Vitest.expect(updated.Value.Tables().[2].ColumnCount).toBe 3
        Queries.card "Start with template!" |> ignore
    }
)

Vitest.test (
    "prior tables are unavailable without other input or output columns or an ARC loader",
    fun () -> promise {
        RTL.render (view (Fixtures.arcFile ()) ignore (Some 0)) |> ignore

        RTL.fireEvent.click (PriorTables.card ())

        Vitest.expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Utilize prior tables")).IsNone).toBe
            true
    }
)

Vitest.test (
    "with an ARC loader prior tables open even when the open file has no columns to offer",
    fun () -> promise {
        let fetchArcNodes () = promise { return ([||]: Types.ArcNodeTable[]) }

        RTL.render (viewWithArcNodes (Fixtures.arcFile ()) ignore (Some 0) fetchArcNodes)
        |> ignore

        RTL.fireEvent.click (PriorTables.card ())

        let! _ = RTL.screen.findByText ("No prior table has an input or output column.", ByTextOptions(exact = true))
        Vitest.expect(PriorTables.importButton().disabled).toBe true
    }
)

Vitest.test (
    "without an ARC loader the output of a table in the open file becomes the new input column",
    fun () -> promise {
        let arcFile = PriorTables.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 1)) |> ignore

        RTL.fireEvent.click (PriorTables.card ())

        Vitest
            .expect(PriorTables.optionLabels ())
            .toEqual (
                [|
                    "Filled – Output [Sample Name]"
                    "Filled – Input [Source Name]"
                |]
            )

        RTL.fireEvent.click (PriorTables.importButton ())

        Vitest.expect(updated.IsSome).toBe true
        let imported = updated.Value.Tables().[1]
        Vitest.expect(imported.Headers |> Seq.toArray).toEqual ([| CompositeHeader.Input IOType.Sample |])
        Vitest.expect(imported.GetColumn(0).Cells.[0]).toEqual (CompositeCell.createFreeText "local-sample")
        Vitest.expect(updated.Value.Tables().[0].ColumnCount).toBe 2

        Vitest.expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Utilize prior tables")).IsNone).toBe
            true
    }
)

Vitest.test (
    "an input column of any ARC table can be imported, while the ARC copy of the open file is skipped",
    fun () -> promise {
        let arcFile = PriorTables.arcFile ()
        let mutable updated: ArcFiles option = None

        let fetchArcNodes () = promise {
            return [|
                PriorTables.arcNode ArcTableScope.Study "Field" (PriorTables.ioTable "Harvest" "plant-1" "leaf-1")
                PriorTables.arcNode
                    ArcTableScope.Assay
                    "Inline setup"
                    (PriorTables.ioTable "Filled" "stale-source" "stale-sample")
            |]
        }

        RTL.render (viewWithArcNodes arcFile (fun next -> updated <- Some next) (Some 1) fetchArcNodes)
        |> ignore

        RTL.fireEvent.click (PriorTables.card ())

        do!
            RTL.waitFor (fun () ->
                Vitest
                    .expect(PriorTables.optionLabels ())
                    .toEqual (
                        [|
                            "Filled – Output [Sample Name]"
                            "Filled – Input [Source Name]"
                            "Harvest – Output [Sample Name]"
                            "Harvest – Input [Source Name]"
                        |]
                    )
            )

        PriorTables.selectOption "Harvest – Input [Source Name]"
        RTL.screen.getByText ("plant-1", ByTextOptions(exact = true)) |> ignore
        RTL.fireEvent.click (PriorTables.importButton ())

        Vitest.expect(updated.IsSome).toBe true
        let imported = updated.Value.Tables().[1]
        Vitest.expect(imported.Headers |> Seq.toArray).toEqual ([| CompositeHeader.Input IOType.Source |])
        Vitest.expect(imported.GetColumn(0).Cells.[0]).toEqual (CompositeCell.createFreeText "plant-1")
    }
)

Vitest.test (
    "a failed ARC load keeps the open file's columns usable and can be retried",
    fun () -> promise {
        let mutable attempts = 0

        let fetchArcNodes () = promise {
            attempts <- attempts + 1

            if attempts = 1 then
                return failwith "ARC is not loaded"
            else
                return [|
                    PriorTables.arcNode ArcTableScope.Run "Measure" (PriorTables.ioTable "Readout" "in" "out")
                |]
        }

        RTL.render (viewWithArcNodes (PriorTables.arcFile ()) ignore (Some 1) fetchArcNodes)
        |> ignore

        RTL.fireEvent.click (PriorTables.card ())

        let! alert = RTL.screen.findByRole ("alert", ByRoleOptions())
        Vitest.expect(alert.textContent).toContain "ARC is not loaded"
        Vitest.expect(PriorTables.optionLabels () |> Array.length).toBe 2
        Vitest.expect(PriorTables.importButton().disabled).toBe false

        RTL.fireEvent.click (Queries.inRole (PriorTables.dialog (), "button", ByRoleOptions(name = Text "Retry")))

        do!
            RTL.waitFor (fun () ->
                Vitest.expect(PriorTables.optionLabels ()).toContain "Readout – Output [Sample Name]"
            )

        Vitest.expect(RTL.screen.queryByRole("alert", ByRoleOptions()).IsNone).toBe true
    }
)
